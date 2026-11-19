using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;

namespace Singulink.CodeAnalysis.Analyzers;

/// <content>
/// The walker's checks of patterns, deconstructions and tuples: one value goes to one variable, what a pattern reads out of a stored
/// value is a copy, and what is taken apart is judged part by part.
/// </content>
public sealed partial class DoNotCopyValueAnalyzer
{
    private sealed partial class NonCopyableWalker
    {
        /// <summary>
        /// Gets a value indicating whether every member that a pattern reads from the value that it looks into is one that cannot change
        /// the value: a field, a readonly getter or a readonly Deconstruct. The pattern then does no harm on a temporary copy.
        /// </summary>
        // The members that a list pattern calls on its input: the length or count, the indexer, and what a slice in it is taken with.
        private static bool RunsOnlyReadOnlyMembers(IListPatternOperation pattern)
        {
            if (pattern.NarrowedType is ITypeParameterSymbol)
                return false;

            foreach (var element in pattern.Patterns)
            {
                if (element is ISlicePatternOperation { Pattern: not null } slice && !IsReadOnly(slice.SliceSymbol))
                    return false;
            }

            return IsReadOnly(pattern.LengthSymbol) && IsReadOnly(pattern.IndexerSymbol);

            // A member of an array or of another reference type is called on the object, which nothing copies.
            static bool IsReadOnly(ISymbol? member) => member switch {
                null => true,
                { ContainingType.IsReferenceType: true } => true,
                IPropertySymbol { GetMethod: { } getter } => getter.IsReadOnly,
                IMethodSymbol method => method.IsReadOnly,
                _ => false,
            };
        }

        private bool ReadsOnlyReadOnlyMembers(IRecursivePatternOperation pattern)
        {
            if (pattern.NarrowedType is ITypeParameterSymbol)
                return false;

            if (pattern.DeconstructSymbol is IMethodSymbol { IsReadOnly: false })
                return false;

            foreach (var subpattern in pattern.DeconstructionSubpatterns)
            {
                // The elements of a tuple are fields of the same copy, and what a listed Deconstruct gives is taken from it.
                bool isPartOfCopy = pattern.DeconstructSymbol is not IMethodSymbol deconstruct ||
                    types.FollowsInputs(types.GetUse(deconstruct), subpattern.InputType);

                if (!ReadsWithoutHarm(subpattern, isPartOfCopy))
                    return false;
            }

            foreach (var subpattern in pattern.PropertySubpatterns)
            {
                // What a getter of checked code returns is a new value. A listed one, as 'KeyValuePair<TKey, TValue>.Value', hands out
                // what the copy holds.
                bool isHarmless = subpattern.Member switch {
                    IFieldReferenceOperation => ReadsWithoutHarm(subpattern.Pattern, isPartOfCopy: true),
                    IPropertyReferenceOperation { Property: { GetMethod.IsReadOnly: true } property } =>
                        ReadsWithoutHarm(subpattern.Pattern, isPartOfCopy: types.FollowsInputs(types.GetUse(property), property.Type)),
                    _ => false,
                };

                if (!isHarmless)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Gets a value indicating whether a pattern for a member of a temporary copy does no harm: it keeps no value of a non-copyable type
        /// in a variable, which would be a second copy that can be used, and reads a part of the copy only through readonly members.
        /// </summary>
        private bool ReadsWithoutHarm(IPatternOperation pattern, bool isPartOfCopy)
        {
            RuntimeHelpers.EnsureSufficientExecutionStack();

            if (!types.IsNonCopyableType(pattern.InputType))
                return true;

            return pattern switch {
                IDeclarationPatternOperation declaration => declaration.DeclaredSymbol is null,
                IRecursivePatternOperation recursive => recursive.DeclaredSymbol is null && (!isPartOfCopy || ReadsOnlyReadOnlyMembers(recursive)),
                INegatedPatternOperation negated => ReadsWithoutHarm(negated.Pattern, isPartOfCopy),
                IBinaryPatternOperation binary => ReadsWithoutHarm(binary.LeftPattern, isPartOfCopy) && ReadsWithoutHarm(binary.RightPattern, isPartOfCopy),
                IListPatternOperation or ISlicePatternOperation => false,

                // The kinds that read nothing of the value. A kind that a later compiler adds is not taken for one of them.
                IConstantPatternOperation or IRelationalPatternOperation or ITypePatternOperation or IDiscardPatternOperation => true,
                _ => false,
            };
        }

        /// <summary>
        /// Checks a pattern: a variable that it declares is as new as the value that is matched, and a property that it reads is a member use.
        /// </summary>
        /// <param name="pattern">The pattern.</param>
        /// <param name="inputIsNew">Whether the value that the pattern matches is a new value.</param>
        /// <param name="handled">The scope of the visit, for what the check marks as handled.</param>
        /// <param name="box">The type of the box that the pattern before this one in an 'and' has narrowed the value out of, if any.</param>
        /// <param name="inputIsCopied">Whether the compiler matches the pattern against a temporary copy of a stored value.</param>
        /// <param name="taken">The parts of the matched value that variables of the whole pattern have taken so far. A value goes to one
        /// variable only.</param>
        /// <param name="part">The part of the value that the whole pattern matches which this pattern matches. Null for all of it.</param>
        /// <param name="copiedBy">What makes the compiler match a local or a by-value parameter as a copy, for the message.</param>
        private void CheckPattern(
            IPatternOperation? pattern,
            bool inputIsNew,
            HandledScope handled,
            ITypeSymbol? box = null,
            bool inputIsCopied = false,
            TakenParts? taken = null,
            PatternPart? part = null,
            CopiedBy copiedBy = CopiedBy.Nothing)
        {
            RuntimeHelpers.EnsureSufficientExecutionStack();

            if (pattern is null)
                return;

            // A container that the pattern takes out of a type which does not mention its non-copyable type is stored, as after a cast.
            if (types.GainsNonCopyableType(pattern.InputType, pattern.NarrowedType))
                inputIsNew = false;

            // A value of a non-copyable type that the pattern takes out of a box is a temporary copy of what the box holds. A bare type test
            // makes none, so the copy is reported for a variable and for a pattern that looks into the value.
            if (IsOutOfBox(pattern))
                box = pattern.InputType;

            // A member that is not readonly, and a receiver that takes the value by value, use the part that they run on: see
            // TakenParts. On a stored value each of them is reported where it runs, so they are followed on a new value only.
            bool recordsUses = inputIsNew && !inputIsCopied && box is null;

            // The other way round: a pattern that matches a stored value of a non-copyable type as a reference type puts a copy of it in a
            // box, as a conversion does. A new value is moved into the box, unless it is a value of a type parameter, which a container can
            // be bound to. A pattern from a struct to a type parameter is that boxing as well: at run time the type parameter can be
            // an interface, and what the pattern gives is then the box.
            bool boxes = types.IsNonCopyableType(pattern.InputType) &&
                (pattern.NarrowedType is { IsReferenceType: true } ||
                    (pattern.NarrowedType is ITypeParameterSymbol && !SymbolEqualityComparer.Default.Equals(pattern.InputType, pattern.NarrowedType)));
            bool boxesCopy = boxes && !inputIsNew;
            bool hidesTypeParameter = boxes && inputIsNew && types.HidesTypeParameter(pattern.InputType, pattern.NarrowedType);
            bool boxesUncheckedStruct = boxes && inputIsNew && !hidesTypeParameter &&
                types.IsStructOfUncheckedCode(pattern.InputType) && types.LosesNonCopyableType(pattern.InputType, pattern.NarrowedType!);

            switch (pattern)
            {
                case IDeclarationPatternOperation declaration:
                    // A second variable for a value that the pattern has given to another one is a copy, whatever the value is.
                    bool isSecond = TakesPatternValue(declaration.DeclaredSymbol, boxes) && taken?.TryTake(part) is false;

                    if (TakesPatternValue(declaration.DeclaredSymbol, boxes))
                        taken?.NoteVariable(part, declaration.DeclaredSymbol!, declaration.Syntax);

                    CheckPatternVariable(declaration, declaration.DeclaredSymbol, declaration.InputType, declaration.MatchedType, inputIsNew, box, isSecond);
                    boxesCopy |= boxes && isSecond;

                    if (boxesCopy && declaration.DeclaredSymbol is not null)
                        Report(Rules.Boxing, declaration.Syntax, declaration.InputType, declaration.NarrowedType);
                    else if (hidesTypeParameter && declaration.DeclaredSymbol is not null)
                        Report(Rules.OpaqueTypeParameterConversion, declaration.Syntax, declaration.InputType, declaration.NarrowedType);
                    else if (boxesUncheckedStruct && declaration.DeclaredSymbol is not null)
                        ReportOpaqueBoxing(declaration);

                    break;

                case IRecursivePatternOperation recursive:
                    taken ??= new TakenParts();

                    // The variable of the pattern comes after the variables for the parts, which have their value first.
                    bool isSecondForWhole = TakesPatternValue(recursive.DeclaredSymbol, boxes) && (DeclaresVariableForPart(recursive) || !taken.TryTake(part));

                    if (TakesPatternValue(recursive.DeclaredSymbol, boxes))
                        taken.NoteVariable(part, recursive.DeclaredSymbol!, recursive.Syntax);

                    // Only a value of a non-copyable type that the pattern matches as it is can be used twice.
                    recordsUses &= types.IsNonCopyableType(recursive.MatchedType);

                    bool variableIsReported = CheckPatternVariable(
                        recursive, recursive.DeclaredSymbol, recursive.InputType, recursive.MatchedType, inputIsNew, box, isSecondForWhole);

                    boxesCopy |= boxes && isSecondForWhole;
                    bool looksIntoValue = !recursive.DeconstructionSubpatterns.IsEmpty || !recursive.PropertySubpatterns.IsEmpty;
                    bool copyIsReported = false;

                    if (box is not null)
                    {
                        // A temporary copy that is only read through readonly members does no harm.
                        if (!variableIsReported && looksIntoValue && !ReadsOnlyReadOnlyMembers(recursive))
                            Report(Rules.CopyByAssignmentOutOfBox, recursive.Syntax, recursive.NarrowedType, box);

                        // The copy has been reported. What the pattern reads out of it is not reported again.
                        if (variableIsReported || looksIntoValue)
                            inputIsNew = true;
                    }
                    else if (boxesCopy || hidesTypeParameter || boxesUncheckedStruct)
                    {
                        // A bare test of the type boxes nothing that can be used.
                        if (recursive.DeclaredSymbol is not null || looksIntoValue)
                        {
                            if (boxesUncheckedStruct)
                                ReportOpaqueBoxing(recursive);
                            else
                                Report(boxesCopy ? Rules.Boxing : Rules.OpaqueTypeParameterConversion, recursive.Syntax, recursive.InputType, recursive.NarrowedType);

                            inputIsNew = true;
                        }
                    }
                    else if (!inputIsNew && looksIntoValue && IsNullableOfNonCopyableType(recursive.InputType))
                    {
                        // A pattern that looks into a stored nullable reads from a temporary copy of its value, as 'stored?.Member' does. A
                        // variable that the pattern declares is that copy, and has been reported as one.
                        if (recursive.DeclaredSymbol is null && !ReadsOnlyReadOnlyMembers(recursive))
                            Report(Rules.CopyOfStoredValue, recursive.Syntax, "The pattern reads from", recursive.InputType, string.Empty);

                        inputIsNew = true;
                    }
                    else if (inputIsCopied && looksIntoValue && types.IsNonCopyableType(recursive.InputType) && !ReadsOnlyReadOnlyMembers(recursive))
                    {
                        // The compiler reads the value of anything but a local into a temporary before it matches it.
                        ReportPatternOnCopy(recursive.Syntax, recursive.InputType, copiedBy);

                        // The copy is reported once: what the pattern looks into inside it is a part of that copy.
                        copyIsReported = true;
                    }

                    // A Deconstruct method of an extension that takes the value by value is given a copy of a stored one.
                    if (recursive.DeconstructSymbol is IMethodSymbol extension &&
                        TakesReceiverByValue(extension) &&
                        types.IsNonCopyableType(recursive.MatchedType) &&
                        !inputIsNew &&
                        !types.IsBorrowing(extension))
                    {
                        Report(Rules.CopyOfStoredValue, recursive.Syntax, $"'{Display(extension)}' receives", recursive.MatchedType, string.Empty);
                    }

                    if (recordsUses && recursive.DeconstructSymbol is IMethodSymbol deconstructing)
                        NoteUseOfPart(taken, part, deconstructing, PartUseKind.Deconstruct, recursive.Syntax);

                    if (!recursive.DeconstructionSubpatterns.IsEmpty)
                    {
                        // A tuple gives its elements as they are. A Deconstruct method is a member like any other.
                        bool elementsAreNew = recursive.DeconstructSymbol is IMethodSymbol deconstruct
                            ? FollowMember(recursive.Syntax, deconstruct, recursive.MatchedType, null, inputIsNew, "'Deconstruct'", null, report: true)
                            : inputIsNew;

                        bool valuesAreNew = elementsAreNew || IsMarkedNew(recursive.DeconstructSymbol);

                        // The elements of a tuple are parts of it, and so is what a Deconstruct method hands out: the compiler calls
                        // the method once for a whole 'is' pattern or a whole 'switch', and gives its outputs to every pattern that
                        // deconstructs the same input. So an output goes to one variable, as an element does.
                        bool isTuple = recursive.DeconstructSymbol is null && recursive.MatchedType is { IsTupleType: true };

                        for (int i = 0; i < recursive.DeconstructionSubpatterns.Length; i++)
                        {
                            var subpattern = recursive.DeconstructionSubpatterns[i];

                            // The compiler reads an element of a tuple into a temporary before it matches the pattern inside, so that
                            // one runs on a copy unless the element is a new value. What a Deconstruct method gives is a value of its own.
                            CheckPattern(
                                subpattern,
                                types.IsNonCopyableType(subpattern.InputType) ? valuesAreNew : elementsAreNew,
                                handled,
                                inputIsCopied: recursive.DeconstructSymbol is null && !elementsAreNew && !copyIsReported,
                                taken: taken,
                                part: isTuple ? new PatternPart(part, PatternPartKind.Position, i) : GetOutputPart(part, recursive.DeconstructSymbol, i));
                        }
                    }

                    // What a property hands out is as new as what it is read from, by the question that a written member asks.
                    bool inputIsNewForMembers = IsNewForItsMembers(inputIsNew, recursive.InputType) && IsNewForItsMembers(inputIsNew, recursive.MatchedType);

                    foreach (var subpattern in recursive.PropertySubpatterns)
                    {
                        var member = subpattern.Member;
                        bool memberIsNew = false;

                        switch (member)
                        {
                            // A container in a field or from a property is always stored. So is what a 'ref' field of a ref struct
                            // refers to, also where the ref struct is a new value.
                            case IFieldReferenceOperation { Field: { ContainingType.IsValueType: true, RefKind: RefKind.None } } when !types.IsContainer(member.Type):
                                memberIsNew = inputIsNew;
                                break;

                            case IPropertyReferenceOperation propertyReference when ReturnsByValue(propertyReference.Property):
                                var property = propertyReference.Property;
                                var use = types.GetUse(property);

                                // The visit of the property reference reports the use of the property. What it reads from is only known here.
                                CheckHandedOutValue(member.Syntax, use, property.Type, inputIsNewForMembers);
                                memberIsNew = !types.IsContainer(property.Type) &&
                                    (use.Kind is not MemberUseKind.Listed || inputIsNewForMembers || types.IsNonCopyableType(property.Type));
                                break;
                        }

                        if (recordsUses && member is IPropertyReferenceOperation { Property: var used })
                            NoteUseOfPart(taken, part, used, PartUseKind.Member, member.Syntax);

                        // The pattern reads the member in place.
                        handled.Add(member);

                        // A field is a part of the value, and so is what a property hands out: the compiler reads a property once
                        // for a whole 'is' pattern or a whole 'switch', whatever its getter does.
                        var memberPart = GetPatternPart(part, member);

                        // The compiler reads a field, and what a property refers to that returns a reference, into a temporary
                        // before it matches the pattern inside, also where the whole value is a local or an object of a class. What
                        // a property returns by value is a temporary already.
                        bool memberIsCopied = !memberIsNew &&
                            !copyIsReported &&
                            member is IFieldReferenceOperation or IPropertyReferenceOperation { Property: { ReturnsByRef: true } or { ReturnsByRefReadonly: true } };

                        CheckPattern(subpattern.Pattern, memberIsNew, handled, inputIsCopied: memberIsCopied, taken: taken, part: memberPart);
                    }

                    break;

                case INegatedPatternOperation negated:
                    CheckPattern(negated.Pattern, inputIsNew, handled, box, inputIsCopied, taken, part, copiedBy);
                    break;

                case IBinaryPatternOperation binary:
                    var left = binary.LeftPattern;
                    taken ??= new TakenParts();
                    CheckPattern(left, inputIsNew, handled, box, inputIsCopied, taken, part, copiedBy);

                    // The right side of an 'and' matches what the left side has narrowed the value to: a container that came out of a type
                    // which does not mention its non-copyable type, or a value that a bare type test took out of a box.
                    if (binary.OperatorKind is BinaryOperatorKind.And)
                    {
                        if (types.GainsNonCopyableType(left.InputType, left.NarrowedType))
                            inputIsNew = false;

                        if (left is ITypePatternOperation or IDeclarationPatternOperation { DeclaredSymbol: null } && IsOutOfBox(left))
                            box = left.InputType;
                    }

                    CheckPattern(binary.RightPattern, inputIsNew, handled, box, inputIsCopied, taken, part, copiedBy);
                    break;

                case IListPatternOperation list:
                    taken ??= new TakenParts();

                    CheckPatternVariable(
                        list,
                        list.DeclaredSymbol,
                        null,
                        null,
                        inputIsNew,
                        box: null,
                        isSecond: TakesPatternValue(list.DeclaredSymbol, boxes: false) && (DeclaresVariableForPart(list) || !taken.TryTake(part)));

                    if (TakesPatternValue(list.DeclaredSymbol, boxes: false))
                        taken.NoteVariable(part, list.DeclaredSymbol!, list.Syntax);

                    recordsUses &= types.IsNonCopyableType(list.NarrowedType);

                    if (recordsUses && list.LengthSymbol is { } length)
                        NoteUseOfPart(taken, part, length, PartUseKind.Member, list.Syntax);

                    if (recordsUses && list.IndexerSymbol is { } indexing)
                        NoteUseOfPart(taken, part, indexing, PartUseKind.Indexer, list.Syntax);

                    // The pattern uses the length or count and the indexer of its type. The elements of an array are read in place, so they are
                    // references, and so are those of an indexer that returns one.
                    var listType = list.NarrowedType;
                    CheckMemberUse(list.Syntax, list.LengthSymbol);
                    CheckReceiverType(list.Syntax, listType, list.LengthSymbol);
                    bool listElementsAreNew = false;

                    // The compiler reads the value of anything but a local into a temporary before it matches it, and calls the
                    // length and the indexer on that copy. Members that are readonly do no harm there.
                    if (inputIsCopied && types.IsNonCopyableType(list.InputType) && !RunsOnlyReadOnlyMembers(list))
                        ReportPatternOnCopy(list.Syntax, list.InputType, copiedBy);

                    if (list.IndexerSymbol is { } indexer)
                    {
                        var indexerUse = CheckMemberUse(list.Syntax, indexer);
                        CheckReceiverType(list.Syntax, listType, indexer);

                        // A container that an indexer gives is stored, as one from any other property.
                        listElementsAreNew = ReturnsByValue(indexer) &&
                            !(indexer is IPropertySymbol { Type: var elementType } && types.IsContainer(elementType)) &&
                            (indexerUse.Kind is not MemberUseKind.Listed || (IsNewForItsMembers(inputIsNew, list.InputType) && IsNewForItsMembers(inputIsNew, listType) && !types.IsView(listType)));
                    }

                    // An element is counted from the start of the list, or from its end if it comes after a slice. Two list patterns
                    // over one list can name one element in both ways. The compiler reads an element once for a whole 'is' pattern
                    // or a whole 'switch', also where the indexer is one of checked code that makes a new value at every call.
                    int sliceAt = list.Patterns.IndexOf(list.Patterns.FirstOrDefault(element => element is ISlicePatternOperation)!);

                    for (int i = 0; i < list.Patterns.Length; i++)
                    {
                        var element = list.Patterns[i];

                        bool elementIsMade = IsMadeBy(list.IndexerSymbol);

                        var elementPart = element is ISlicePatternOperation slicePattern
                            ? new PatternPart(part, PatternPartKind.Slice, 0, IsMadeBy(slicePattern.SliceSymbol))
                            : sliceAt >= 0 && i > sliceAt
                                ? new PatternPart(part, PatternPartKind.FromEnd, list.Patterns.Length - i, elementIsMade)
                                : new PatternPart(part, PatternPartKind.FromStart, i, elementIsMade);

                        // The compiler reads an element into a temporary before it matches the pattern inside.
                        CheckPattern(
                            element,
                            element is ISlicePatternOperation ? inputIsNew : listElementsAreNew,
                            handled,
                            inputIsCopied: element is not ISlicePatternOperation && !listElementsAreNew,
                            taken: taken,
                            part: elementPart);
                    }

                    break;

                case ISlicePatternOperation slice:
                    // A '..' without a pattern takes no slice.
                    if (slice.Pattern is null)
                        break;

                    // A slice uses the Slice method of its type, or for an array the method that copies a range of it, which the operation does
                    // not name.
                    var sliceMethod = slice.SliceSymbol ?? (slice.InputType is IArrayTypeSymbol array ? GetSubArrayMethod(array.ElementType) : null);

                    // The Slice method runs on the list, which the slice is a part of.
                    if (recordsUses && types.IsNonCopyableType(slice.InputType) && slice.SliceSymbol is { } slicing && part is { Kind: PatternPartKind.Slice })
                        NoteUseOfPart(taken ??= new TakenParts(), part.Parent, slicing, PartUseKind.Slice, slice.Syntax);
                    var sliceUse = CheckMemberUse(slice.Syntax, sliceMethod);
                    CheckReceiverType(slice.Syntax, slice.InputType, sliceMethod);

                    // A Slice method or a range indexer that returns a reference refers to a stored value, which the compiler reads
                    // into a temporary before it matches the pattern inside.
                    bool sliceIsReference = sliceMethod is
                        IMethodSymbol { ReturnsByRef: true } or IMethodSymbol { ReturnsByRefReadonly: true } or
                        IPropertySymbol { ReturnsByRef: true } or IPropertySymbol { ReturnsByRefReadonly: true };

                    CheckPattern(
                        slice.Pattern,
                        !sliceIsReference && (!types.FollowsInputs(sliceUse, slice.Pattern.InputType) || inputIsNew),
                        handled,
                        inputIsCopied: sliceIsReference,
                        taken: taken,
                        part: part);

                    break;
            }
        }

        /// <summary>
        /// Gets a value indicating whether a variable that a pattern declares takes the matched value: it holds a value of a
        /// non-copyable type, or a box that the pattern puts one into.
        /// </summary>
        private bool TakesPatternValue(ISymbol? declared, bool boxes) => declared is ILocalSymbol local && (boxes || types.IsNonCopyableType(local.Type));

        /// <summary>
        /// Gets a value indicating whether a pattern declares a variable of a non-copyable type for a part of what it matches: for an
        /// element, or for a member that is a part of the value.
        /// </summary>
        private bool DeclaresVariableForPart(IPatternOperation pattern)
        {
            switch (pattern)
            {
                case IRecursivePatternOperation recursive:
                    return recursive.DeconstructionSubpatterns.Any(DeclaresVariable) || recursive.PropertySubpatterns.Any(sub => DeclaresVariable(sub.Pattern));

                case IListPatternOperation list:
                    return list.Patterns.Any(DeclaresVariable);

                default:
                    return false;
            }

            bool DeclaresVariable(IPatternOperation? part)
            {
                RuntimeHelpers.EnsureSufficientExecutionStack();

                return part switch {
                    IDeclarationPatternOperation { DeclaredSymbol: ILocalSymbol local } => types.IsNonCopyableType(local.Type),
                    IRecursivePatternOperation { DeclaredSymbol: ILocalSymbol local } when types.IsNonCopyableType(local.Type) => true,
                    IListPatternOperation { DeclaredSymbol: ILocalSymbol local } when types.IsNonCopyableType(local.Type) => true,
                    IRecursivePatternOperation or IListPatternOperation => DeclaresVariableForPart(part),
                    INegatedPatternOperation negated => DeclaresVariable(negated.Pattern),
                    IBinaryPatternOperation binary => DeclaresVariable(binary.LeftPattern) || DeclaresVariable(binary.RightPattern),
                    ISlicePatternOperation slice => DeclaresVariable(slice.Pattern),
                    _ => false,
                };
            }
        }

        /// <summary>
        /// Gets the part of a matched value that an output of a Deconstruct method stands for. What a method of checked code hands
        /// out is a value that the method made, named by the method and the output: it is the same value for every pattern that
        /// deconstructs the same input, and another one than what any other member hands out. What a method hands out whose body
        /// is not checked can be any part of the value.
        /// </summary>
        private PatternPart GetOutputPart(PatternPart? part, ISymbol? deconstruct, int index)
        {
            if (deconstruct is IMethodSymbol method && types.GetUse(method).Kind is MemberUseKind.Checked)
            {
                // The receiver of an extension method comes before its outputs.
                var unreduced = method.ReducedFrom ?? method;
                int at = unreduced.IsExtensionMethod ? index + 1 : index;

                if (at < unreduced.Parameters.Length)
                    return new PatternPart(part, PatternPartKind.Member, unreduced.Parameters[at].OriginalDefinition, isMade: true);
            }

            return new PatternPart(part, PatternPartKind.Output, index);
        }

        // What a member of checked code hands out by value is a value that the member made.
        private bool IsMadeBy(ISymbol? member) => member is not null && ReturnsByValue(member) && types.GetUse(member).Kind is MemberUseKind.Checked;

        /// <summary>
        /// Gets the part of a matched value that a member which a property pattern names stands for.
        /// </summary>
        private PatternPart GetPatternPart(PatternPart? part, IOperation? member)
        {
            switch (member)
            {
                case IFieldReferenceOperation { Field: var field }:
                    return GetTupleElementIndex(field) is int index and >= 0
                        ? new PatternPart(part, PatternPartKind.Position, index)
                        : new PatternPart(part, PatternPartKind.Member, field.OriginalDefinition);

                case IPropertyReferenceOperation { Property: var property }:
                    return new PatternPart(part, PatternPartKind.Member, property.OriginalDefinition, isMade: IsMadeBy(property));

                default:
                    return new PatternPart(part, PatternPartKind.Unknown, 0);
            }
        }

        /// <summary>
        /// Gets a value indicating whether the compiler matches a pattern against a temporary copy of a stored value. It reads a by-value
        /// local or parameter in place, and anything else into a temporary first. That is so for an 'is' pattern and for a 'switch'
        /// expression with no 'when'. A 'switch' statement, and a 'switch' expression with a 'when', copy a local as well: see where
        /// they are visited.
        /// </summary>
        /// <remarks>
        /// A parameter of a primary constructor that a member uses is a field of the type. The compiler makes the reference to
        /// the parameter one to the field before it lowers the pattern, so the pattern matches a copy of the field. A local that a
        /// lambda captures is another matter: closures are lowered after patterns, and the local is matched in place.
        /// </remarks>
        private bool IsCopiedForPattern(IOperation value) =>
            !IsNew(value) &&
            SkipParentheses(value) switch {
                ILocalReferenceOperation { Local.RefKind: RefKind.None } => false,
                IParameterReferenceOperation { Parameter: { RefKind: RefKind.None } parameter } => IsFieldOfPrimaryConstructor(parameter),
                _ => true,
            };

        // A parameter of a constructor that another member refers to is one of a primary constructor, which the type keeps in a field.
        // It is that field wherever it is read: in an initializer of the type and in the arguments of the base constructor as in
        // a member, since the constructor stores the field before either runs.
        private bool IsFieldOfPrimaryConstructor(IParameterSymbol parameter)
        {
            if (parameter.ContainingSymbol is not IMethodSymbol { MethodKind: MethodKind.Constructor } constructor)
                return false;

            return context.OwningSymbol is IMethodSymbol member && !SymbolEqualityComparer.Default.Equals(member, constructor)
                ? true
                : GetUsesOfPrimaryConstructorParameter(parameter) is { IsUsedByMember: true };
        }

        /// <summary>
        /// Reports a pattern that runs a member which is not readonly on a temporary copy of a stored value. Where the copy is one
        /// that the source does not show, of a local or a by-value parameter, the message names the cause and the rewrite.
        /// </summary>
        private void ReportPatternOnCopy(SyntaxNode syntax, ITypeSymbol? type, CopiedBy copiedBy)
        {
            switch (copiedBy)
            {
                case CopiedBy.SwitchStatement:
                    Report(Rules.SwitchStatementMatchesCopy, syntax, type);
                    break;

                case CopiedBy.WhenOfSwitchExpression:
                    Report(Rules.SwitchExpressionWithWhenMatchesCopy, syntax, type);
                    break;

                default:
                    Report(Rules.CopyOfStoredValue, syntax, "The pattern reads from", type, string.Empty);
                    break;
            }
        }

        /// <summary>
        /// Notes a member that a pattern runs on a part, if it is one that uses the part: a member of a struct that is not readonly,
        /// which can change what it runs on, or one of an extension, which is given the part as an argument.
        /// </summary>
        private void NoteUseOfPart(TakenParts taken, PatternPart? part, ISymbol member, PartUseKind kind, SyntaxNode syntax)
        {
            var method = member as IMethodSymbol ?? (member as IPropertySymbol)?.GetMethod;

            if (method is null)
                return;

            bool isOfExtension = method.IsExtensionMethod || method.ContainingType is { IsExtension: true };

            if (!isOfExtension && (method.IsStatic || method.IsReadOnly || method.ContainingType is not { IsValueType: true }))
                return;

            taken.NoteUse(part, member, kind, syntax, $"'{Display(member)}'", isOfExtension && TakesReceiverByValue(method));
        }

        /// <summary>
        /// Reports what one pattern, or the clauses of one 'switch', use twice next to the variables: see <see cref="TakenParts"/>.
        /// </summary>
        private void ReportPartsUsedTwice(TakenParts taken)
        {
            HashSet<SyntaxNode>? reported = null;

            foreach (var (first, second) in taken.FindPartsUsedTwice())
            {
                if ((reported ??= []).Add(second.Syntax))
                    Report(Rules.PartUsedTwiceInPattern, second.Syntax, first.Name, second.Name);
            }
        }

        // From a value of a type parameter to a struct the compiler boxes and unboxes, so that is a box as well.
        private bool IsOutOfBox(IPatternOperation pattern) =>
            (IsBoxType(pattern.InputType) || (pattern.InputType is ITypeParameterSymbol && !SymbolEqualityComparer.Default.Equals(pattern.InputType, pattern.NarrowedType))) &&
            types.IsNonCopyableType(pattern.NarrowedType);

        private void ReportOpaqueBoxing(IPatternOperation pattern) =>
            Report(Rules.OpaqueBoxing, pattern.Syntax, pattern.InputType, pattern.NarrowedType, Display(GetStructDefinition(pattern.InputType)));

        /// <summary>
        /// Gets the definition of a generic struct for a message, which for a nullable is that of what it holds.
        /// </summary>
        private static ITypeSymbol GetStructDefinition(ITypeSymbol type)
        {
            RuntimeHelpers.EnsureSufficientExecutionStack();

            return type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T, TypeArguments: [var underlying] }
                ? GetStructDefinition(underlying)
                : type.OriginalDefinition;
        }

        private bool IsNullableOfNonCopyableType(ITypeSymbol? type) =>
            type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } && types.IsNonCopyableType(type);

        private bool IsMarkedNew(ISymbol? member) => member is not null && types.GetUse(member).Kind is MemberUseKind.ListedNew;

        /// <summary>
        /// Checks the variable that a pattern declares and gets whether it has been reported as a copy out of a box.
        /// </summary>
        private bool CheckPatternVariable(
            IPatternOperation pattern,
            ISymbol? declared,
            ITypeSymbol? inputType,
            ITypeSymbol? matchedType,
            bool inputIsNew,
            ITypeSymbol? box,
            bool isSecond = false)
        {
            if (declared is not ILocalSymbol local)
                return false;

            var location = local.Locations.FirstOrDefault(l => l.IsInSource) ?? pattern.Syntax.GetLocation();
            bool isReportedAsUnboxed = false;

            if (types.IsNonCopyableType(local.Type))
            {
                // A variable that is matched out of a box is a copy of what the box holds, whatever gave the reference to the box.
                if (box is not null)
                {
                    Report(Rules.CopyByAssignmentOutOfBox, location, local.Type, box);
                    isReportedAsUnboxed = true;
                }
                else if (!inputIsNew)
                {
                    Report(Rules.CopyByAssignmentIntoVariable, location, local.Type, local.Name);
                }
                else if (isSecond)
                {
                    Report(Rules.CopyByAssignmentIntoSecondVariable, location, local.Type, local.Name);
                }
            }

            // The variable gives the container another type, as a conversion does.
            if (types.HidesNonCopyableType(inputType, matchedType))
                ReportContainerConversion(pattern.Syntax, inputType, matchedType);

            return isReportedAsUnboxed;
        }

        private void MarkDeconstructionTargets(IOperation target, HandledScope handled)
        {
            RuntimeHelpers.EnsureSufficientExecutionStack();

            handled.Add(target);

            switch (target)
            {
                case IParenthesizedOperation parenthesized:
                    MarkDeconstructionTargets(parenthesized.Operand, handled);
                    break;

                case IDeclarationExpressionOperation declaration:
                    MarkDeconstructionTargets(declaration.Expression, handled);
                    break;

                case ITupleOperation tuple:
                    foreach (var element in tuple.Elements)
                        MarkDeconstructionTargets(element, handled);

                    break;
            }
        }

        /// <summary>
        /// Checks the variables of a deconstruction, which are as new as the value that is deconstructed. The elements of a tuple literal are
        /// assigned one by one.
        /// </summary>
        private void CheckDeconstruction(IOperation target, IOperation? value, DeconstructionInfo? info, bool valueIsNew, HandledScope handled)
        {
            RuntimeHelpers.EnsureSufficientExecutionStack();

            // A user-defined conversion of what a Deconstruct method hands out to the type of its target, with nothing written for it.
            CheckMemberUse((value ?? target).Syntax, info?.Conversion?.MethodSymbol);

            target = SkipParentheses(target);

            if (target is IDeclarationExpressionOperation { Expression: ITupleOperation declaredTuple })
                target = declaredTuple;

            if (target is not ITupleOperation targets)
            {
                CheckDeconstructionTarget(target, value, valueIsNew);
                return;
            }

            var nested = info?.Nested ?? ImmutableArray<DeconstructionInfo>.Empty;

            if (value is not null && SkipWrappingConversions(value) is ITupleOperation literal && literal.Elements.Length == targets.Elements.Length)
            {
                handled.Add(literal);

                for (int i = 0; i < targets.Elements.Length; i++)
                {
                    var element = literal.Elements[i];
                    handled.Add(element);
                    CheckDeconstruction(targets.Elements[i], element, i < nested.Length ? nested[i] : null, IsNew(element), handled);
                }

                return;
            }

            // A tuple gives its elements as they are. A Deconstruct method is a member like any other, with one difference: the
            // compiler copies what is deconstructed into a temporary first, whatever it is, a local included, and calls the method on
            // that. So a method that is not readonly runs on a copy of anything but a new value, and what it changes is lost. Through
            // a read-only reference that is said as it is for any other member.
            if (value is not null && info?.Method is { } called)
            {
                CheckExtensionReceiver(value, called);

                // The receiver of a member of an extension block is a parameter, and is judged as that above.
                if (called is { IsStatic: false, IsExtensionMethod: false, ContainingType.IsExtension: false })
                {
                    if (IsCalledOnCopy(value, called.ContainingType, called.IsReadOnly))
                        ReportMemberOnCopy(value, called);
                    else if (!valueIsNew && !called.IsReadOnly && value.Type is { IsReferenceType: false } && types.IsNonCopyableType(value.Type))
                        Report(Rules.DeconstructionOfStoredValue, value.Syntax, value.Type, Display(called));
                }
            }

            bool elementsAreNew = info?.Method is { } deconstruct
                ? FollowMember((value ?? target).Syntax, deconstruct, value?.Type, null, valueIsNew, "'Deconstruct'", null, report: true)
                : valueIsNew;

            bool valuesAreNew = elementsAreNew || IsMarkedNew(info?.Method);

            for (int i = 0; i < targets.Elements.Length; i++)
            {
                var element = targets.Elements[i];
                bool elementIsNew = types.IsNonCopyableType(element.Type) ? valuesAreNew : elementsAreNew;
                CheckDeconstruction(element, null, i < nested.Length ? nested[i] : null, elementIsNew, handled);
            }
        }

        private void CheckDeconstructionTarget(IOperation target, IOperation? value, bool valueIsNew)
        {
            if (target is IDeclarationExpressionOperation declaration)
                target = declaration.Expression;

            CheckLeftInMovedFromParameter(target, value);

            if (valueIsNew || target is IDiscardOperation || !types.IsNonCopyableType(target.Type))
                return;

            // An element of a tuple literal is assigned like any other value. Without one, the variable is what there is to point at.
            if (value is null && target is ILocalReferenceOperation { IsDeclaration: true } declared)
                Report(Rules.CopyByAssignmentIntoVariable, target.Syntax, target.Type, declared.Local.Name);
            else
                Report(Rules.CopyByAssignment, (value ?? target).Syntax, target.Type, GetHandlerHint(value));
        }
    }

    /// <summary>
    /// What a part of the value that a pattern matches is counted by.
    /// </summary>
    private enum PatternPartKind
    {
        /// <summary>
        /// A field, what a property hands out, or an output of a Deconstruct method of checked code.
        /// </summary>
        Member,

        /// <summary>
        /// An element of a tuple.
        /// </summary>
        Position,

        /// <summary>
        /// What a Deconstruct method hands out whose body is not checked, which can be any part.
        /// </summary>
        Output,

        /// <summary>
        /// An element of a list, counted from its start.
        /// </summary>
        FromStart,

        /// <summary>
        /// An element of a list, counted from its end.
        /// </summary>
        FromEnd,

        /// <summary>
        /// The elements that a slice takes.
        /// </summary>
        Slice,

        /// <summary>
        /// A part that is not known, which can be any part.
        /// </summary>
        Unknown,
    }

    /// <summary>
    /// A part of the value that a pattern matches: a member, an element of a tuple or of a list, at any depth.
    /// </summary>
    private sealed class PatternPart(PatternPart? parent, PatternPartKind kind, object key, bool isMade = false)
    {
        public PatternPart? Parent { get; } = parent;

        public PatternPartKind Kind { get; } = kind;

        public object Key { get; } = key;

        // A value that a member of checked code hands out by value is one that the member made: the value that the member ran on
        // does not hold it. Any other part is a copy of something that the value still holds.
        public bool IsMade { get; } = isMade;

        public int Depth { get; } = (parent?.Depth ?? 0) + 1;
    }

    /// <summary>
    /// What makes the compiler match a local or a by-value parameter as a copy, where an 'is' pattern would match it in place.
    /// </summary>
    private enum CopiedBy
    {
        Nothing,
        SwitchStatement,
        WhenOfSwitchExpression,
    }

    /// <summary>
    /// How a member that a pattern runs uses the part that it runs on, which says what it hands out.
    /// </summary>
    private enum PartUseKind
    {
        /// <summary>
        /// A property, or the length of a list pattern: it hands out the part that is named by it.
        /// </summary>
        Member,

        /// <summary>
        /// A Deconstruct method: it hands out its outputs.
        /// </summary>
        Deconstruct,

        /// <summary>
        /// The indexer of a list pattern: it hands out the elements.
        /// </summary>
        Indexer,

        /// <summary>
        /// The Slice method or range indexer of a list pattern: it hands out the slice.
        /// </summary>
        Slice,
    }

    /// <summary>
    /// A variable that takes a part of a matched value, or a member that uses one.
    /// </summary>
    private sealed class PartEntry(PatternPart? part, SyntaxNode syntax, string name, ISymbol? member, PartUseKind kind, bool takesByValue)
    {
        public PatternPart? Part { get; } = part;

        public SyntaxNode Syntax { get; } = syntax;

        // How a message names it.
        public string Name { get; } = name;

        // Null for a variable.
        public ISymbol? Member { get; } = member;

        public PartUseKind Kind { get; } = kind;

        // The member takes the part by value, and works on a copy of it.
        public bool TakesByValue { get; } = takesByValue;
    }

    /// <summary>
    /// The parts of one matched value that the variables of a pattern have taken, and the uses of its parts next to the variables. A
    /// variable takes a part only if no other variable has taken that part, a part of it, or something that it is a part of.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The compiler copies each part that a pattern looks into to a temporary, the value that it is a part of still holds the
    /// original, and every pattern that names the part shares the temporary. So what a member does on a part is done to a copy:
    /// after 'case { Inner.Rel: false }:', 'case var slot:' gives 'slot' the whole value, which still holds the original of
    /// 'Inner'. Following variables alone misses that. So the uses of a part are recorded next to them: a member that is not
    /// readonly and runs on it, and a receiver that takes it by value.
    /// </para>
    /// <para>
    /// A use and a variable, or two uses, are reported where the part of one lies inside that of the other, whatever the order in
    /// which they are written: the compiler orders the evaluations itself. What a use hands out, as the outputs of a Deconstruct
    /// method, lies inside the part that the use runs on and is the use's to give. On exactly one part nothing is wrong: a variable
    /// is bound after the member ran and holds what the member left, and two members run on the one temporary in turn. The
    /// exceptions there are a variable that a clause with a 'when' handed on before the use, and a receiver by value, which works
    /// on a copy while the other holds the original.
    /// </para>
    /// </remarks>
    private sealed class TakenParts
    {
        // Null stands for the whole value.
        private List<PatternPart?>? _parts;

        // The variables and the uses, in the order in which the patterns name them.
        private List<PartEntry>? _entries;

        // How many of each a clause before this one has left: see ForLaterClauses.
        private int _partsBefore;
        private int _entriesBefore;

        public TakenParts Clone() => new() {
            _parts = _parts is null ? null : [.. _parts],
            _entries = _entries is null ? null : [.. _entries],
            _partsBefore = _parts?.Count ?? 0,
            _entriesBefore = _entries?.Count ?? 0,
        };

        /// <summary>
        /// Gets what the clauses after this one start with. A clause with a 'when' runs code with its variables and then lets a
        /// later clause bind the value again, so what its variables have taken stays taken. A clause with none ends the 'switch'
        /// where it matches, so its variables are gone for the later clauses. What its members did to the parts stays done either
        /// way: the compiler ran them to test the clause.
        /// </summary>
        public TakenParts ForLaterClauses(bool hasWhen)
        {
            if (hasWhen)
                return this;

            var later = new TakenParts {
                _parts = _parts is null ? null : [.. _parts.Take(_partsBefore)],
            };

            if (_entries is not null)
            {
                for (int i = 0; i < _entries.Count; i++)
                {
                    if (i < _entriesBefore || _entries[i].Member is not null)
                        (later._entries ??= []).Add(_entries[i]);
                }
            }

            return later;
        }

        public bool TryTake(PatternPart? part)
        {
            if (_parts is not null)
            {
                foreach (var taken in _parts)
                {
                    if (Relate(taken, part) is not PartRelation.Apart)
                        return false;
                }
            }

            (_parts ??= []).Add(part);
            return true;
        }

        public void NoteVariable(PatternPart? part, ISymbol variable, SyntaxNode syntax) =>
            (_entries ??= []).Add(new PartEntry(part, syntax, $"variable '{variable.Name}'", member: null, PartUseKind.Member, takesByValue: false));

        public void NoteUse(PatternPart? part, ISymbol member, PartUseKind kind, SyntaxNode syntax, string name, bool takesByValue) =>
            (_entries ??= []).Add(new PartEntry(part, syntax, name, member, kind, takesByValue));

        /// <summary>
        /// Finds the pairs of a use and a variable, or of two uses, that work on one value twice. One of each pair is of the clause
        /// that this was made for.
        /// </summary>
        public IEnumerable<(PartEntry First, PartEntry Second)> FindPartsUsedTwice()
        {
            if (_entries is null)
                yield break;

            for (int j = Math.Max(_entriesBefore, 1); j < _entries.Count; j++)
            {
                for (int i = 0; i < j; i++)
                {
                    if (AreUsedTwice(_entries[i], _entries[j], firstIsOfEarlierClause: i < _entriesBefore))
                        yield return (_entries[i], _entries[j]);
                }
            }
        }

        private static bool AreUsedTwice(PartEntry first, PartEntry second, bool firstIsOfEarlierClause)
        {
            // Two variables are judged where the second one is declared.
            if (first.Member is null && second.Member is null)
                return false;

            switch (Relate(first.Part, second.Part))
            {
                case PartRelation.Apart:
                    return false;

                // A variable that a clause with a 'when' handed on, and then a member on the temporary that the variable was. And
                // a receiver by value, which is given a copy of what the other holds.
                case PartRelation.Same:
                    return first.TakesByValue || second.TakesByValue || (firstIsOfEarlierClause && first.Member is null);

                case PartRelation.FirstHoldsSecond:
                    return IsHeldBy(first.Part, second.Part) && !HandsOut(first, second.Part);

                case PartRelation.SecondHoldsFirst:
                    return IsHeldBy(second.Part, first.Part) && !HandsOut(second, first.Part);

                default:
                    return true;
            }
        }

        // Whether the value of the outer part still holds the inner part: no step between them is a value that a member made.
        private static bool IsHeldBy(PatternPart? outer, PatternPart? inner)
        {
            int depth = outer?.Depth ?? 0;

            for (; inner is not null && inner.Depth > depth; inner = inner.Parent)
            {
                if (inner.IsMade)
                    return false;
            }

            return true;
        }

        // Whether a part lies inside what a use hands out: the step from the part that the use runs on is one of its outputs.
        private static bool HandsOut(PartEntry use, PatternPart? part)
        {
            if (use.Member is null)
                return false;

            int depth = (use.Part?.Depth ?? 0) + 1;

            while (part is not null && part.Depth > depth)
                part = part.Parent;

            if (part is null)
                return false;

            return use.Kind switch {
                PartUseKind.Deconstruct => part.Kind is PatternPartKind.Output ||
                    (part is { Kind: PatternPartKind.Member, Key: IParameterSymbol output } &&
                        SymbolEqualityComparer.Default.Equals(output.ContainingSymbol, ((use.Member as IMethodSymbol)?.ReducedFrom ?? use.Member).OriginalDefinition)),
                PartUseKind.Indexer => part.Kind is PatternPartKind.FromStart or PatternPartKind.FromEnd,
                PartUseKind.Slice => part.Kind is PatternPartKind.Slice,
                _ => part is { Kind: PatternPartKind.Member, Key: ISymbol named } && SymbolEqualityComparer.Default.Equals(named, use.Member.OriginalDefinition),
            };
        }

        private enum PartRelation
        {
            Apart,
            Same,
            FirstHoldsSecond,
            SecondHoldsFirst,

            // The two can be one part, or parts of each other, by two different ways of naming it.
            Unsure,
        }

        private static PartRelation Relate(PatternPart? first, PatternPart? second)
        {
            int firstDepth = first?.Depth ?? 0;
            int secondDepth = second?.Depth ?? 0;

            // Up to where both are equally deep: the one that is less deep is then the whole, or a part that holds the other.
            while ((first?.Depth ?? 0) > secondDepth)
                first = first!.Parent;

            while ((second?.Depth ?? 0) > firstDepth)
                second = second!.Parent;

            bool isSureOfEveryStep = true;

            // Two parts are apart if they differ in a step that keeps them apart, at any depth.
            for (; first is not null && second is not null; first = first.Parent, second = second.Parent)
            {
                if (AreApart(first, second))
                    return PartRelation.Apart;

                isSureOfEveryStep &= first.Kind == second.Kind && first.Kind is not (PatternPartKind.Output or PatternPartKind.Unknown) && KeysAreEqual(first.Key, second.Key);
            }

            if (!isSureOfEveryStep)
                return PartRelation.Unsure;

            return firstDepth == secondDepth ? PartRelation.Same : firstDepth < secondDepth ? PartRelation.FirstHoldsSecond : PartRelation.SecondHoldsFirst;

            static bool KeysAreEqual(object first, object second) =>
                first is ISymbol firstSymbol && second is ISymbol secondSymbol ? SymbolEqualityComparer.Default.Equals(firstSymbol, secondSymbol) : Equals(first, second);
        }

        private static bool AreApart(PatternPart first, PatternPart second)
        {
            if (first.Kind != second.Kind)
            {
                // A member and an element of a tuple or of a list are different things. Anything else can be the same part.
                return (first.Kind, second.Kind) is
                    (PatternPartKind.Member, PatternPartKind.Position or PatternPartKind.FromStart or PatternPartKind.FromEnd or PatternPartKind.Slice) or
                    (PatternPartKind.Position or PatternPartKind.FromStart or PatternPartKind.FromEnd or PatternPartKind.Slice, PatternPartKind.Member);
            }

            return first.Kind switch {
                PatternPartKind.Member => !SymbolEqualityComparer.Default.Equals((ISymbol)first.Key, (ISymbol)second.Key),
                PatternPartKind.Position or PatternPartKind.Output or PatternPartKind.FromStart or PatternPartKind.FromEnd => !Equals(first.Key, second.Key),
                _ => false,
            };
        }
    }
}
