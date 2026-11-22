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
/// The judgment of what an operation runs: the closed list of what is no more than a read or a write of the method's own variables
/// and of what its references refer to, which cannot throw and runs no other code. The commit at the end of a 'try' and the paths of a
/// [MovedFrom] parameter both rest on it, and whatever is not named here counts as a call.
/// </content>
public sealed partial class DoNotCopyValueAnalyzer
{
    private sealed partial class MemberFlow
    {
        private static readonly ICollection<ILocalSymbol> s_noAliases = ImmutableHashSet<ILocalSymbol>.Empty;

        /// <summary>
        /// Gets a value indicating whether a call is one of System.Threading.Interlocked.Exchange that reads and assigns in one
        /// operation and cannot throw. The generic form does so only for a reference type: for a struct that is no primitive it throws.
        /// </summary>
        private static bool IsInterlockedExchange(IOperation call)
        {
            if (call is not IInvocationOperation { TargetMethod: { Name: "Exchange" } method })
                return false;

            bool isOfInterlocked = method.ContainingType is { Name: "Interlocked", ContainingType: null } interlocked &&
                NonCopyableTypes.IsInNamespace(interlocked, "System.Threading");

            return isOfInterlocked && (!method.IsGenericMethod || method.TypeArguments is [{ IsReferenceType: true }]);
        }

        /// <summary>
        /// Gets the stores of a group assignment in the order in which they are made, each target with the value that it takes. A
        /// group assignment is a deconstruction of a tuple that is written out, whose elements run nothing when they are worked
        /// out, and which reads the [MovedFrom] parameters of the method in a way that cannot leave one value in two places. Null
        /// for any other deconstruction.
        /// </summary>
        /// <remarks>
        /// Two parameters can refer to one variable, and then the right side reads one value twice. That is harmless in two shapes:
        /// each of two parameters receives the other's value, which is a swap, or one receives the other's value and is stored
        /// before the other is, which is a shift by one. With three such reads, or with two whose values both go elsewhere, one
        /// value can end in two places. A by-value read of a non-copyable value through any other reference is such a read too.
        /// </remarks>
        private static List<(IOperation Target, IOperation Value)>? FindGroup(FunctionGraph graph, IDeconstructionAssignmentOperation deconstruction, NonCopyableTypes? copiedTypes)
        {
            var stores = new List<(IOperation Target, IOperation Value)>();

            if (!TryPair(deconstruction.Target, deconstruction.Value))
                return null;

            (IParameterSymbol Parameter, int Index)? first = null, second = null;

            for (int i = 0; i < stores.Count; i++)
            {
                var value = stores[i].Value;

                while (value is IParenthesizedOperation or IConversionOperation { OperatorMethod: null })
                {
                    // A conversion that the language makes with a value at hand, and not one that looks into a tuple or runs a binder.
                    if (value is IConversionOperation conversion && conversion.GetConversion() is not { IsImplicit: true, IsUserDefined: false, IsDynamic: false, IsTupleConversion: false })
                        return null;

                    value = value.ChildOperations.First();
                }

                if (GetMovedFromRead(value) is { } moved)
                {
                    if (second is not null || SymbolEqualityComparer.Default.Equals(first?.Parameter, moved))
                        return null;

                    if (first is null)
                        first = (moved, i);
                    else
                        second = (moved, i);

                    continue;
                }

                if (!RunsNothing(value))
                    return null;
            }

            if (first is var (one, readOfOne) && second is var (other, readOfOther))
            {
                int storeOfOne = stores.FindIndex(store => IsReferenceTo(store.Target, one));
                int storeOfOther = stores.FindIndex(store => IsReferenceTo(store.Target, other));
                bool oneGetsOther = storeOfOne == readOfOther;
                bool otherGetsOne = storeOfOther == readOfOne;

                bool isSwap = oneGetsOther && otherGetsOne;
                bool isShift = (oneGetsOther && storeOfOther > storeOfOne) || (otherGetsOne && storeOfOne > storeOfOther);

                if (!isSwap && !isShift)
                    return null;
            }

            return stores;

            // A value that runs nothing when it is worked out: a read of a variable or of a part of one, 'default', a constant, a
            // struct that is created all zeros, and what the operators and conversions of the language make of those. A call is
            // none: it can throw, and it can reach the variable that a parameter refers to in another way.
            bool RunsNothing(IOperation value)
            {
                RuntimeHelpers.EnsureSufficientExecutionStack();

                switch (value)
                {
                    case IParenthesizedOperation { Operand: var operand }:
                        return RunsNothing(operand);

                    case IDefaultValueOperation or ILiteralOperation or ITypeOfOperation or ISizeOfOperation or { ConstantValue.HasValue: true }:
                        return true;

                    case IConversionOperation { OperatorMethod: null, Operand: var converted }:
                        return IsJudgedToRunNothing(value) && RunsNothing(converted);

                    case IUnaryOperation { OperatorMethod: null, Operand: var negated }:
                        return IsJudgedToRunNothing(value) && RunsNothing(negated);

                    case IBinaryOperation { OperatorMethod: null, LeftOperand: var left, RightOperand: var right }:
                        return IsJudgedToRunNothing(value) && RunsNothing(left) && RunsNothing(right);

                    case IObjectCreationOperation:
                        return IsJudgedToRunNothing(value);
                }

                // A read of a variable or of a part of one. Through a reference it must not take a value of a non-copyable type,
                // and a [MovedFrom] parameter is read as a whole, where its read is counted.
                var root = value;

                while (root is IFieldReferenceOperation { Field: { IsStatic: false, RefKind: RefKind.None }, Instance: { } owner })
                    root = owner;

                bool? isThroughReference = root switch {
                    ILocalReferenceOperation { Local.RefKind: var kind } => kind is not RefKind.None,
                    IParameterReferenceOperation { Parameter: { RefKind: not RefKind.None } read } => NonCopyableTypes.IsMovedFrom(read) ? null : true,
                    IParameterReferenceOperation => false,
                    IInstanceReferenceOperation { ReferenceKind: InstanceReferenceKind.ContainingTypeInstance, Type.IsValueType: var isStruct } => isStruct,
                    _ => null,
                };

                return isThroughReference is { } through && !(through && copiedTypes is not null && copiedTypes.Carries(value.Type));
            }

            // What the judgment of a single operation says of an operator, a conversion or a creation: it looks at the kind and at
            // the types alone.
            bool IsJudgedToRunNothing(IOperation single) => DescribeWhatRuns(graph, single, MoveState.Stored, null, s_noAliases, static _ => false) is null;

            bool TryPair(IOperation target, IOperation value)
            {
                RuntimeHelpers.EnsureSufficientExecutionStack();

                if (target is IDeclarationExpressionOperation declaration)
                    target = declaration.Expression;

                if (target is not ITupleOperation targets)
                {
                    stores.Add((target, value));
                    return true;
                }

                while (value is IParenthesizedOperation or IConversionOperation { OperatorMethod: null, Operand: ITupleOperation or IParenthesizedOperation })
                    value = value.ChildOperations.First();

                if (value is not ITupleOperation values || values.Elements.Length != targets.Elements.Length)
                    return false;

                for (int i = 0; i < targets.Elements.Length; i++)
                {
                    if (!TryPair(targets.Elements[i], values.Elements[i]))
                        return false;
                }

                return true;
            }

            static bool IsReferenceTo(IOperation target, IParameterSymbol parameter) =>
                target is IParameterReferenceOperation { Parameter: var referred } && SymbolEqualityComparer.Default.Equals(referred, parameter);
        }

        /// <summary>
        /// Gets the [MovedFrom] parameter that a value of a group assignment reads as a whole, behind conversions that run nothing.
        /// </summary>
        private static IParameterSymbol? GetMovedFromRead(IOperation value)
        {
            while (value is IParenthesizedOperation or IConversionOperation { OperatorMethod: null })
                value = value.ChildOperations.First();

            return value is IParameterReferenceOperation { Parameter: { RefKind: RefKind.Ref } parameter } && NonCopyableTypes.IsMovedFrom(parameter) ? parameter : null;
        }

        /// <summary>
        /// Gets the group assignment that a read of a [MovedFrom] parameter is a value of: see <see cref="FindGroup"/>. From the read
        /// to the stores such an assignment runs nothing but the reads of its other values.
        /// </summary>
        private static IDeconstructionAssignmentOperation? FindGroupOfRead(FunctionGraph graph, IOperation read, NonCopyableTypes? copiedTypes)
        {
            var current = read;

            while (current.Parent is IParenthesizedOperation or IConversionOperation { OperatorMethod: null } or ITupleOperation)
                current = current.Parent;

            return current.Parent is IDeconstructionAssignmentOperation deconstruction &&
                ReferenceEquals(deconstruction.Value, current) &&
                FindGroup(graph, deconstruction, copiedTypes) is not null
                    ? deconstruction
                    : null;
        }

        /// <summary>
        /// Says what an operation runs after a value was stored where it is to stay, with no [MovedFrom] parameter in play: see
        /// <see cref="DescribeWhatRuns"/>. Null if it runs nothing that can throw.
        /// </summary>
        private static string? DescribeWhatRunsAfterCommit(FunctionGraph graph, IOperation operation, StrongBox<string?>? checkable = null) =>
            DescribeWhatRuns(graph, operation, MoveState.Stored, null, s_noAliases, static _ => false, checkable: checkable);

        /// <summary>
        /// Says what an operation runs that is more than a read or a write of the method's own locals, in words for a message. Null if
        /// it runs nothing of the kind: it cannot throw, and it cannot reach what the parameter refers to in another way.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A reference can be null: Unsafe.NullRef makes one in code that the language calls safe, and a read or a write through it
        /// fails with an exception that a caller can catch. So a read or a write through a 'ref', 'in' or 'out' parameter or a 'ref'
        /// local counts as able to throw, and so does a field of 'this' in a struct, where 'this' is such a reference. In a class
        /// 'this' is never null, and a read or a plain store of an instance field of it cannot throw.
        /// </para>
        /// <para>
        /// The exception is a reference that is known to refer to something where the operation runs: see
        /// <see cref="KnownReferences"/>. A read or a plain store through it cannot fail. Between the read and the assignment of a
        /// [MovedFrom] parameter one thing is left of it: a by-value read of a non-copyable value through another reference is a
        /// second read of the same value if the two refer to one variable, and is reported as before.
        /// </para>
        /// <para>
        /// Every case that gives null names what it accepts: the kind of operation, and for an operator, a conversion or a pattern the
        /// kinds of those and the types that they run on. Whatever is not named is reported, so that a kind which nobody has thought
        /// of, or one that a later version of the language adds, cannot go through. Running out of memory is left out of "cannot
        /// throw", as it is everywhere.
        /// </para>
        /// </remarks>
        /// <param name="graph">The graph of the function.</param>
        /// <param name="operation">The operation.</param>
        /// <param name="state">Where the paths stand between the read and the assignment of the parameter.</param>
        /// <param name="parameter">The [MovedFrom] parameter, or null after a commit.</param>
        /// <param name="aliases">The 'ref' locals that refer to the parameter.</param>
        /// <param name="isMoveOfLocal">Says whether a call is a move of a local of the method.</param>
        /// <param name="copiedTypes">The types, for the question of whether a read copies a value through another reference.</param>
        /// <param name="groupOfRead">The group assignment that the read of the parameter is a value of, if it is one.</param>
        /// <param name="checkable">Receives the name of the reference where what runs is a read or a write through a reference that
        /// is not known to refer to something, and nothing else: the message then says how to check it.</param>
        private static string? DescribeWhatRuns(
            FunctionGraph graph,
            IOperation operation,
            MoveState state,
            IParameterSymbol? parameter,
            ICollection<ILocalSymbol> aliases,
            Func<IInvocationOperation, bool> isMoveOfLocal,
            NonCopyableTypes? copiedTypes = null,
            IDeconstructionAssignmentOperation? groupOfRead = null,
            StrongBox<string?>? checkable = null)
        {
            // A deconstruction that is no group assignment is judged with no look at what is known of its references.
            bool judgesAsBefore = false;

            const string Call = "a call";
            const string OtherReference = "a read or a write through another reference";
            const string Member = "a use of a field or an element that is not the method's own";
            const string Other = "an operation that can throw or run other code";

            switch (operation)
            {
                // The reference that 'x = ref y' makes refer to something else is not read. A [MovedFrom] parameter that is made to
                // refer to something is reported by itself.
                case ILocalReferenceOperation or IParameterReferenceOperation
                    when operation.Parent is ISimpleAssignmentOperation { IsRef: true } repointing && ReferenceEquals(repointing.Target, operation):
                    return null;

                // What it is made to refer to is read through, as far as anyone can tell. Used by value, 'x = ref y' is a read of
                // what 'x' then refers to, which no operation stands for: 'n = (r = ref other);'. And where nothing reads through
                // 'x', the compiler of the .NET 11 SDK leaves a read of 'y' in place of the assignment, the declaration of a 'ref'
                // local included. So 'y' is judged as a read through it: able to fail unless it is a variable of the method, the
                // [MovedFrom] parameter itself, or a reference that is known to refer to something.
                // A [MovedFrom] parameter that is made to refer to it is reported by itself, as with a field below.
                case ILocalReferenceOperation { Local: var referredTo } when operation.Parent is ISimpleAssignmentOperation { IsRef: true } pointing:
                    return IsTheParameter(pointing.Target) || referredTo.RefKind is RefKind.None || aliases.Contains(referredTo) || IsKnown(operation) ? null : Through(operation, OtherReference);

                case IParameterReferenceOperation { Parameter: var referredTo } when operation.Parent is ISimpleAssignmentOperation { IsRef: true } pointing:
                    return IsTheParameter(pointing.Target) || IsOwnParameter(referredTo) || IsKnown(operation) ? null : Through(operation, OtherReference);

                // A read or a write through another reference can fail, since a reference can be null, and a read through one can
                // read the very variable that the parameter refers to.
                case ILocalReferenceOperation { Local: var local }:
                    return local.RefKind is RefKind.None || aliases.Contains(local) || IsAssignedToParameter() || IsKnownAndNotCopiedThrough(operation) ? null : Through(operation, OtherReference);

                case IParameterReferenceOperation { Parameter: var referred }:
                    return IsOwnParameter(referred) || IsAssignedToParameter() || IsKnownAndNotCopiedThrough(operation) ? null : Through(operation, OtherReference);

                // 'this' in a struct is a reference, however it is used: assigned, copied or tested by a pattern. Where a member is
                // used on it, the member is what is judged.
                case IInstanceReferenceOperation { ReferenceKind: InstanceReferenceKind.ContainingTypeInstance, Type.IsValueType: true } self:
                    return self.Parent is IMemberReferenceOperation or IInvocationOperation || IsKnownAndNotCopiedThrough(operation) ? null : Through(operation, OtherReference);

                // Taking a reference to a field reads nothing. It still looks at the object that has the field, which can be null, and
                // at the type of a static field, whose initializer it can run. A [MovedFrom] parameter that is made to refer to the
                // field is reported by itself, whatever the field.
                // Of a field of 'this' in a struct it runs a check of 'this', where the reference is kept: that counts as a read
                // through 'this', able to fail unless 'this' is known.
                case IFieldReferenceOperation { Parent: ISimpleAssignmentOperation { IsRef: true, Target: var referring } } taken:
                    if (referring is IParameterReferenceOperation { Parameter: var madeToRefer } && SymbolEqualityComparer.Default.Equals(madeToRefer, parameter))
                        return null;

                    if (!IsFieldOfAVariableOrOfThis(taken))
                        return Member;

                    return FindStructThis(taken) is { } ofThis && !IsKnown(ofThis) ? Through(ofThis, OtherReference) : null;

                // A constant, such as a member of an enum, is a value that the compiler writes into the code.
                case IFieldReferenceOperation { Field.IsConst: true }:
                    return null;

                case IFieldReferenceOperation field:
                    return IsPartOfOwnVariable(field) ? null : Through(field, Member);

                case IArrayElementReferenceOperation or IInlineArrayAccessOperation or IImplicitIndexerReferenceOperation:
                    return Member;

                case IInvocationOperation invocation:
                    return isMoveOfLocal(invocation) || IsExchangeOfParameter(invocation) ? null : Call;

                case IObjectCreationOperation creation:
                    // A struct that is created with no constructor of its own is all zeros.
                    return creation is { Type.IsValueType: true, Constructor.IsImplicitlyDeclared: true, Arguments.IsEmpty: true, Initializer: null } &&
                        !HasInitializers(creation.Type)
                            ? null
                            : Call;

                case IConversionOperation conversion:
                    return DescribeConversion(conversion);

                // An operator of the language runs no other code on simple values. On anything else it can: the concatenation of a
                // string with an object calls ToString, and an operator on a dynamic value runs the binder.
                // The concatenation of two strings throws where the result would be longer than a string can be. That is no lack of
                // memory, which is not counted: it happens for certain values, and it can be caught. One that the compiler folds
                // to a constant runs nothing.
                case IBinaryOperation binary:
                    if (binary.OperatorMethod is not null || IsDynamic(binary.LeftOperand.Type) || IsDynamic(binary.RightOperand.Type))
                        return Call;

                    if (IsString(binary.Type) && binary.OperatorKind is BinaryOperatorKind.Add)
                        return binary.ConstantValue.HasValue ? null : IsString(binary.LeftOperand.Type) && IsString(binary.RightOperand.Type) ? Other : Call;

                    if (binary.IsChecked)
                        return Other;

                    if (IsSimple(binary.LeftOperand.Type) && IsSimple(binary.RightOperand.Type))
                        return IsArithmeticThatCannotThrow(binary.OperatorKind) || IsComparison(binary.OperatorKind) ? null : Other;

                    // A test for null, and a comparison of two references.
                    return binary.OperatorKind is BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals &&
                        (IsNull(binary.LeftOperand) || IsNull(binary.RightOperand) ||
                            (binary.LeftOperand.Type is { IsReferenceType: true } && binary.RightOperand.Type is { IsReferenceType: true }))
                                ? null
                                : Other;

                case IUnaryOperation unary:
                    if (unary.OperatorMethod is not null || IsDynamic(unary.Operand.Type))
                        return Call;

                    // '^n' is not among them: it makes an Index, whose constructor throws for a negative number.
                    return !unary.IsChecked &&
                        IsSimple(unary.Operand.Type) &&
                        unary.OperatorKind is UnaryOperatorKind.Plus or UnaryOperatorKind.Minus or UnaryOperatorKind.BitwiseNegation or UnaryOperatorKind.Not
                            ? null
                            : Other;

                // The elements of a tuple literal are assigned one by one, each with a conversion of its own. A tuple in a variable is
                // read element by element, with conversions that are no operations of their own unless the types are the same. Anything
                // else that is deconstructed, at any depth, has its Deconstruct method called.
                case IDeconstructionAssignmentOperation deconstruction:
                    if (!IsPlainTarget(deconstruction.Target))
                        return Call;

                    // A group assignment reads everything on its right before it stores anything, and stores from left to right. So
                    // a store that fails does harm only if a store before it has put a value where it stays: one that was read from
                    // a [MovedFrom] parameter in this assignment, or the value that was read or committed before it.
                    if (FindGroup(graph, deconstruction, copiedTypes) is { } group)
                    {
                        bool hasMoved = !ReferenceEquals(deconstruction, groupOfRead);

                        foreach (var (target, value) in group)
                        {
                            if (hasMoved && DescribeStore(target) is { } failing)
                                return CountStoresThatCanFail(deconstruction.Target) > 1 ? Other : failing;

                            hasMoved |= GetMovedFromRead(value) is not null;
                        }

                        return IsTakenApartWithNoCall(deconstruction.Target, deconstruction.Value, deconstruction.Value.Type) ? null : Call;
                    }

                    // Anything else that is taken apart is judged as it was before a reference could be known. The stores are made
                    // one after the other, and a second one that fails leaves the first one done. A store into a variable of the
                    // method cannot fail, so at most one target may be anything else.
                    judgesAsBefore = true;

                    if (CountStoresThatCanFail(deconstruction.Target) > 1)
                        return Other;

                    // And each store is made when the deconstruction runs, wherever its target stands: see the assignment below.
                    if (DescribeStores(deconstruction.Target, deconstruction.Value) is { } store)
                        return store;

                    return IsTakenApartWithNoCall(deconstruction.Target, deconstruction.Value, deconstruction.Value.Type) ? null : Call;

                // What it writes to is judged where the target is referred to, unless it writes through a setter.
                case ICompoundAssignmentOperation compound:
                    if (compound.OperatorMethod is not null || !IsPlainTarget(compound.Target) || IsDynamic(compound.Target.Type) || IsDynamic(compound.Value.Type) ||
                        compound.InConversion.IsUserDefined || compound.OutConversion.IsUserDefined)
                    {
                        return Call;
                    }

                    // The concatenation of two strings can be too long for a string, as above.
                    if (IsString(compound.Target.Type) && compound.OperatorKind is BinaryOperatorKind.Add)
                        return IsString(compound.Value.Type) ? Other : Call;

                    // On a type that is narrower than 'int' the result is converted back, as '_slot <<= 1' on a 'byte' is
                    // '_slot = (byte)(_slot << 1)'. The operation tree shows the operator, which is not checked where it cannot
                    // overflow, and has no operation for the conversion, which is checked wherever the context is.
                    return !compound.IsChecked &&
                        !(compound.OutConversion is { IsImplicit: false } back && (back.IsNumeric || back.IsNullable) && IsInCheckedContext(compound)) &&
                        IsSimple(compound.Target.Type) &&
                        IsSimple(compound.Value.Type) &&
                        IsArithmeticThatCannotThrow(compound.OperatorKind)
                            ? null
                            : Other;

                case IIncrementOrDecrementOperation increment:
                    if (increment.OperatorMethod is not null || !IsPlainTarget(increment.Target) || IsDynamic(increment.Target.Type))
                        return Call;

                    // An increment on a type that is narrower than 'int' converts its result back as well. The compiler marks the
                    // whole operation as checked where the context is, so nothing more is asked here.
                    return !increment.IsChecked && IsSimple(increment.Target.Type) && increment.Kind is OperationKind.Increment or OperationKind.Decrement
                        ? null
                        : Other;

                case IPropertyReferenceOperation or IEventReferenceOperation or IAwaitOperation or ITypeParameterObjectCreationOperation:
                case IDynamicInvocationOperation or IDynamicMemberReferenceOperation or IDynamicObjectCreationOperation or IDynamicIndexerAccessOperation:
                case IFunctionPointerInvocationOperation:
                    return Call;

                // A positional pattern reads the elements of a tuple. On any other type it calls Deconstruct, or the members of ITuple.
                // A positional pattern with no subpatterns, 'is ()', is one all the same: on an object it calls ITuple.Length.
                case IRecursivePatternOperation positional when
                    !positional.DeconstructionSubpatterns.IsEmpty ||
                    positional.DeconstructSymbol is not null ||
                    positional.Syntax is RecursivePatternSyntax { PositionalPatternClause: not null }:
                    return positional is { DeconstructSymbol: null, MatchedType.IsTupleType: true } ? null : Call;

                // A test for a type calls nothing where the type is a class or a struct. For an interface or a type parameter the
                // runtime asks an object of a class that implements IDynamicInterfaceCastable, by calling a method of the object.
                case IIsTypeOperation typeTest:
                    return AsksTheObject(typeTest.ValueOperand.Type, typeTest.TypeOperand) ? Other : null;

                case ITypePatternOperation typePattern:
                    return AsksTheObject(typePattern.InputType, typePattern.MatchedType) ? Other : null;

                case IDeclarationPatternOperation declarationPattern:
                    return AsksTheObject(declarationPattern.InputType, declarationPattern.MatchedType) ? Other : null;

                case IRecursivePatternOperation recursivePattern:
                    return AsksTheObject(recursivePattern.InputType, recursivePattern.MatchedType) ? Other : null;

                // A test of a value by a pattern that calls nothing. A property that a pattern reads is an operation of its own. A list
                // pattern and a slice pattern, which call Length and an indexer, are not among them.
                case IIsPatternOperation:
                case IConstantPatternOperation or IDiscardPatternOperation:
                case INegatedPatternOperation:
                case IPropertySubpatternOperation:
                    return null;

                case IRelationalPatternOperation relational:
                    return IsComparison(relational.OperatorKind) ? null : Other;

                case IBinaryPatternOperation combined:
                    return combined.OperatorKind is BinaryOperatorKind.And or BinaryOperatorKind.Or ? null : Other;

                // Making a reference refer to something stores nothing.
                case ISimpleAssignmentOperation { IsRef: true } referring:
                    return IsPlainTarget(referring.Target) ? null : Call;

                // An assignment stores when it runs, wherever its target stands: in 'node.Owner = _current = g' the target is
                // evaluated before the inner store and written after it, and the write is what fails for a null 'node'. So the
                // target is judged here as well as where it is referred to. The one store that takes the value which was read
                // from the parameter is the exception: if it fails, the value is still only in the parameter. A store through a
                // setter, an indexer or an event accessor is a call.
                case ISimpleAssignmentOperation assignment:
                    return IsPlainTarget(assignment.Target) && IsReadOfParameter(assignment.Value) ? null : DescribeStore(assignment.Target);

                case IInstanceReferenceOperation:
                case ILiteralOperation:
                case IDefaultValueOperation:
                case IDiscardOperation:
                case IDeclarationExpressionOperation:
                case ITupleOperation:
                case IExpressionStatementOperation:
                case IFlowCaptureOperation:
                case IFlowCaptureReferenceOperation:
                case IArgumentOperation:
                case IIsNullOperation:
                case ISizeOfOperation:
                case ITypeOfOperation:
                    return null;

                default:
                    return Other;
            }

            // Whether the code is compiled with overflow checks where an operation stands: inside the nearest 'checked' or
            // 'unchecked' statement or expression around it, which a lambda and a local function are inside of as well, and with
            // neither as the project is compiled (<CheckForOverflowUnderflow>).
            bool IsInCheckedContext(IOperation at)
            {
                for (var node = at.Syntax; node is not null; node = node.Parent)
                {
                    switch (node.Kind())
                    {
                        case SyntaxKind.CheckedStatement or SyntaxKind.CheckedExpression:
                            return true;

                        case SyntaxKind.UncheckedStatement or SyntaxKind.UncheckedExpression:
                            return false;
                    }
                }

                return graph.Compilation.Options.CheckOverflow;
            }

            // Whether a test of a value for a type is one that the runtime answers by calling a method of the object: a test for an
            // interface, or for a type parameter that an interface can be bound to, on an object of a class that implements
            // IDynamicInterfaceCastable. A struct and an object of a sealed class cannot be of such a class without the compiler
            // knowing the answer, and a test that every value of the static type passes is no test for a type at all.
            bool AsksTheObject(ITypeSymbol? tested, ITypeSymbol? target)
            {
                // 'var' and a pattern that names no type.
                if (target is null)
                    return false;

                bool canBeInterface = target switch {
                    { TypeKind: TypeKind.Interface } => true,
                    ITypeParameterSymbol typeParameter => !typeParameter.HasValueTypeConstraint &&
                        !typeParameter.HasUnmanagedTypeConstraint &&
                        !typeParameter.ConstraintTypes.Any(static constraint => constraint.TypeKind is TypeKind.Class or TypeKind.Struct or TypeKind.Enum or TypeKind.Delegate or TypeKind.Array),
                    _ => false,
                };

                if (!canBeInterface)
                    return false;

                if (tested is null)
                    return true;

                if (tested is { IsValueType: true })
                    return false;

                // The compiler knows the answer for a sealed class only where it knows the type that is tested for. One with a type
                // parameter in it is known when the code runs, and the test is made then: the object is asked. A struct is never
                // asked.
                if (tested is INamedTypeSymbol { TypeKind: TypeKind.Class, IsSealed: true } && !NonCopyableTypes.MentionsTypeParameter(target))
                    return false;

                var conversion = graph.Compilation.ClassifyCommonConversion(tested, target);
                return !(conversion.Exists && conversion.IsImplicit && (conversion.IsIdentity || conversion.IsReference));
            }

            // A conversion that the language makes with no other code: one that changes nothing, a numeric one, one between a type and
            // a base type, an interface or a box of it, and one into a nullable of the same type. A tuple literal is converted element by
            // element, each an operation of its own. Any other conversion of a tuple can run a user-defined conversion of an element,
            // and one from 'dynamic' runs the binder.
            static string? DescribeConversion(IConversionOperation conversion)
            {
                if (conversion.OperatorMethod is not null)
                    return Call;

                var kind = conversion.GetConversion();

                if (kind.IsIdentity || kind.IsNullLiteral || kind.IsDefaultLiteral)
                    return null;

                if (kind.IsUserDefined || kind.IsDynamic || IsDynamic(conversion.Operand.Type))
                    return Call;

                if (!kind.IsImplicit || conversion.IsChecked)
                    return Other;

                if (kind.IsTupleLiteralConversion)
                    return conversion.Operand is ITupleOperation ? null : Call;

                if (kind.IsTupleConversion)
                    return Call;

                // A number becomes a 'decimal' through a method of that type.
                if (kind.IsNumeric)
                    return IsSimple(conversion.Operand.Type) && IsSimple(conversion.Type) ? null : Call;

                if (kind.IsReference || kind.IsBoxing || kind.IsEnumeration || kind.IsConstantExpression ||
                    kind.IsConditionalExpression || kind.IsSwitchExpression || kind.IsObjectCreation || kind.IsThrow)
                {
                    return null;
                }

                if (kind.IsNullable)
                {
                    var from = StripNullable(conversion.Operand.Type);
                    var to = StripNullable(conversion.Type);
                    return SymbolEqualityComparer.Default.Equals(from, to) || (IsSimple(from) && IsSimple(to)) ? null : Call;
                }

                return Other;
            }

            static ITypeSymbol? StripNullable(ITypeSymbol? type) =>
                type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T, TypeArguments: [var underlying] } ? underlying : type;

            static bool IsDynamic(ITypeSymbol? type) => type is { TypeKind: TypeKind.Dynamic };

            static bool IsString(ITypeSymbol? type) => type is { SpecialType: SpecialType.System_String };

            static bool IsNull(IOperation operand)
            {
                while (operand is IConversionOperation { OperatorMethod: null } conversion)
                    operand = conversion.Operand;

                return operand is ILiteralOperation { ConstantValue: { HasValue: true, Value: null } } or IDefaultValueOperation { Type.IsReferenceType: true };
            }

            // The operators of the language that are one instruction on the simple types and cannot throw unless they are checked.
            // Not among them: division and remainder, which throw for a zero, and whatever kind is not named here.
            static bool IsArithmeticThatCannotThrow(BinaryOperatorKind kind) => kind is
                BinaryOperatorKind.Add or BinaryOperatorKind.Subtract or BinaryOperatorKind.Multiply or
                BinaryOperatorKind.And or BinaryOperatorKind.Or or BinaryOperatorKind.ExclusiveOr or
                BinaryOperatorKind.LeftShift or BinaryOperatorKind.RightShift or BinaryOperatorKind.UnsignedRightShift;

            static bool IsComparison(BinaryOperatorKind kind) => kind is
                BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals or
                BinaryOperatorKind.LessThan or BinaryOperatorKind.LessThanOrEqual or
                BinaryOperatorKind.GreaterThan or BinaryOperatorKind.GreaterThanOrEqual;

            // The types whose operators the runtime has instructions for. An operand with no type is not one of them.
            static bool IsSimple(ITypeSymbol? type) => StripNullable(type) is
                { TypeKind: TypeKind.Enum or TypeKind.Pointer or TypeKind.FunctionPointer } or
                {
                    SpecialType: SpecialType.System_Boolean or SpecialType.System_Char or
                        SpecialType.System_SByte or SpecialType.System_Byte or SpecialType.System_Int16 or SpecialType.System_UInt16 or
                        SpecialType.System_Int32 or SpecialType.System_UInt32 or SpecialType.System_Int64 or SpecialType.System_UInt64 or
                        SpecialType.System_IntPtr or SpecialType.System_UIntPtr or SpecialType.System_Single or SpecialType.System_Double,
                };

            // A deconstruction with no Deconstruct method and no conversion that is hidden in it: where the target is a tuple of
            // targets, the value is a tuple literal with as many elements, each judged in turn, or a value of a tuple type whose
            // elements have the very types of the targets. A value of any other type has its Deconstruct method called.
            static bool IsTakenApartWithNoCall(IOperation target, IOperation? value, ITypeSymbol? valueType)
            {
                RuntimeHelpers.EnsureSufficientExecutionStack();

                if (target is IDeclarationExpressionOperation declaration)
                    target = declaration.Expression;

                if (target is not ITupleOperation targets)
                    return target.Type is { } targetType && SymbolEqualityComparer.Default.Equals(targetType, valueType);

                // The conversion of a tuple literal to the types of its targets is done element by element.
                while (value is IConversionOperation { OperatorMethod: null, Operand: ITupleOperation literal })
                    value = literal;

                if (value is ITupleOperation values)
                {
                    if (values.Elements.Length != targets.Elements.Length)
                        return false;

                    for (int i = 0; i < targets.Elements.Length; i++)
                    {
                        if (!IsTakenApartWithNoCall(targets.Elements[i], values.Elements[i], values.Elements[i].Type))
                            return false;
                    }

                    return true;
                }

                if (valueType is not INamedTypeSymbol { IsTupleType: true, TupleElements: var elements } || elements.Length != targets.Elements.Length)
                    return false;

                for (int i = 0; i < elements.Length; i++)
                {
                    if (!IsTakenApartWithNoCall(targets.Elements[i], null, elements[i].Type))
                        return false;
                }

                return true;
            }

            // Says what a store into a target runs when it is made. Null if it cannot fail: the target is a local or a by-value
            // parameter of the method, a discard, a part of one of those, or an instance field of 'this' in a class.
            string? DescribeStore(IOperation target)
            {
                RuntimeHelpers.EnsureSufficientExecutionStack();

                switch (target)
                {
                    case IDiscardOperation:
                        return null;

                    case IDeclarationExpressionOperation declaration:
                        return DescribeStore(declaration.Expression);

                    case ILocalReferenceOperation { Local: var local }:
                        return local.RefKind is RefKind.None || aliases.Contains(local) || IsKnown(target) ? null : Through(target, OtherReference);

                    case IParameterReferenceOperation { Parameter: var referred }:
                        return IsOwnParameter(referred) || IsKnown(target) ? null : Through(target, OtherReference);

                    // 'this = value' in a struct.
                    case IInstanceReferenceOperation:
                        return IsKnown(target) ? null : Through(target, OtherReference);

                    case IFieldReferenceOperation field:
                        return IsPartOfOwnVariable(field) ? null : Through(field, Member);

                    case IArrayElementReferenceOperation or IInlineArrayAccessOperation or IImplicitIndexerReferenceOperation:
                        return Member;

                    // A target that was captured before the value was worked out, as for a value with a conditional in it: the
                    // store goes into whatever the capture holds.
                    case IFlowCaptureReferenceOperation captured:
                        var captures = graph.GetCaptures(captured.Id);

                        foreach (var capture in captures)
                        {
                            if (DescribeStore(capture.Value) is { } what)
                                return what;
                        }

                        return captures.Count > 0 ? null : Other;

                    // What a call returned a reference to, and anything that is written through an accessor.
                    default:
                        return IsPlainTarget(target) ? OtherReference : Call;
                }
            }

            // The stores of a deconstruction, each with the element of a tuple literal that it takes, if the value is one.
            string? DescribeStores(IOperation target, IOperation? value)
            {
                RuntimeHelpers.EnsureSufficientExecutionStack();

                if (target is IDeclarationExpressionOperation declaration)
                    target = declaration.Expression;

                if (target is not ITupleOperation targets)
                    return value is not null && IsReadOfParameter(value) ? null : DescribeStore(target);

                while (value is IConversionOperation { OperatorMethod: null, Operand: ITupleOperation literal })
                    value = literal;

                var values = value is ITupleOperation { Elements: var elements } && elements.Length == targets.Elements.Length ? elements : default;

                for (int i = 0; i < targets.Elements.Length; i++)
                {
                    if (DescribeStores(targets.Elements[i], values.IsDefault ? null : values[i]) is { } what)
                        return what;
                }

                return null;
            }

            // The by-value read of the [MovedFrom] parameter, or of a part of it, behind conversions that run nothing. A value that a
            // conditional chose is that read if one of its branches is: on the path where another branch ran, nothing was read.
            bool IsReadOfParameter(IOperation value)
            {
                RuntimeHelpers.EnsureSufficientExecutionStack();

                if (parameter is null)
                    return false;

                while (value is IConversionOperation { OperatorMethod: null } or IParenthesizedOperation)
                    value = value.ChildOperations.First();

                if (value is IFlowCaptureReferenceOperation chosen)
                    return graph.GetCaptures(chosen.Id).Any(capture => IsReadOfParameter(capture.Value));

                while (value is IFieldReferenceOperation { Field: { RefKind: RefKind.None, IsStatic: false }, Instance: { Type.IsValueType: true } owner })
                    value = owner;

                return value is IParameterReferenceOperation { Parameter: var read } && SymbolEqualityComparer.Default.Equals(read, parameter);
            }

            int CountStoresThatCanFail(IOperation target)
            {
                RuntimeHelpers.EnsureSufficientExecutionStack();

                return target switch {
                    IDeclarationExpressionOperation declaration => CountStoresThatCanFail(declaration.Expression),
                    ITupleOperation tuple => tuple.Elements.Sum(CountStoresThatCanFail),
                    ILocalReferenceOperation { Local.RefKind: RefKind.None } or IParameterReferenceOperation { Parameter.RefKind: RefKind.None } or IDiscardOperation => 0,
                    IFieldReferenceOperation ofThis when IsInstanceFieldOfThis(ofThis) => 0,
                    _ => 1,
                };
            }

            // The targets that an assignment stores into with no call of its own: a variable, a field, an element of an array, a
            // discard, and what a reference refers to that another operation has produced, which is judged as that operation. Also the
            // targets of a deconstruction, each of which has to be one of these. A property, an indexer or an event is written through
            // an accessor, and whatever is not named here is taken to be written through a call as well.
            static bool IsPlainTarget(IOperation target)
            {
                RuntimeHelpers.EnsureSufficientExecutionStack();

                return target switch {
                    ILocalReferenceOperation or IParameterReferenceOperation or IFieldReferenceOperation or IArrayElementReferenceOperation => true,
                    IDiscardOperation or IInstanceReferenceOperation or IFlowCaptureReferenceOperation or IInlineArrayAccessOperation => true,
                    IPropertyReferenceOperation { Property.RefKind: RefKind.Ref } => true,
                    IInvocationOperation { TargetMethod.RefKind: RefKind.Ref } => true,
                    IImplicitIndexerReferenceOperation { IndexerSymbol: IPropertySymbol { RefKind: RefKind.Ref } } => true,
                    IDeclarationExpressionOperation declaration => IsPlainTarget(declaration.Expression),
                    ITupleOperation tuple => tuple.Elements.All(IsPlainTarget),
                    _ => false,
                };
            }

            // A struct with no constructor of its own still runs the initializers of its fields and properties, where the language
            // version lets it have them.
            static bool HasInitializers(ITypeSymbol? type)
            {
                if (type is null)
                    return true;

                foreach (var reference in type.DeclaringSyntaxReferences)
                {
                    if (reference.GetSyntax() is not TypeDeclarationSyntax declaration)
                        return true;

                    foreach (var member in declaration.Members)
                    {
                        if (member.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.StaticKeyword) || modifier.IsKind(SyntaxKind.ConstKeyword)))
                            continue;

                        if ((member is FieldDeclarationSyntax field && field.Declaration.Variables.Any(variable => variable.Initializer is not null)) ||
                            member is PropertyDeclarationSyntax { Initializer: not null })
                        {
                            return true;
                        }
                    }
                }

                return false;
            }

            // In 'a = b', after the value of 'a' went into a local, the read of 'b' is the first part of the assignment of 'a': if 'b' is
            // the same variable the assignment changes nothing, and if it throws the local is gone.
            bool IsAssignedToParameter()
            {
                if (state is not MoveState.InLocal)
                    return false;

                var current = operation;

                while (current.Parent is IConversionOperation { OperatorMethod: null })
                    current = current.Parent;

                return current.Parent is ISimpleAssignmentOperation { IsRef: false, Target: IParameterReferenceOperation { Parameter: var target } } assignment &&
                    ReferenceEquals(assignment.Value, current) &&
                    SymbolEqualityComparer.Default.Equals(target, parameter);
            }

            bool IsPartOfOwnVariable(IFieldReferenceOperation field)
            {
                IOperation? instance = field;

                while (instance is IFieldReferenceOperation { Field: { RefKind: RefKind.None, IsStatic: false }, Instance: { Type.IsValueType: true } owner })
                    instance = owner;

                return instance switch {
                    ILocalReferenceOperation { Local: var local } => local.RefKind is RefKind.None || aliases.Contains(local) || IsKnown(instance),
                    IParameterReferenceOperation { Parameter: var referred } => IsOwnParameter(referred) || IsKnown(instance),

                    // A field of the value that a pattern tests is read where the value is, and is judged like that value.
                    IInstanceReferenceOperation { ReferenceKind: InstanceReferenceKind.PatternInput } input => IsOwnPatternInput(input),

                    // A field of 'this' in a struct, where 'this' is known to refer to something.
                    IInstanceReferenceOperation { ReferenceKind: InstanceReferenceKind.ContainingTypeInstance, Type.IsValueType: true } => IsKnown(instance),

                    // An instance field of 'this' in a class, where 'this' is never null. In a struct 'this' is a reference, which
                    // can be null. A [MovedFrom] parameter can refer to that very field: a by-value read of a non-copyable value
                    // out of it takes the value a second time, and is the copy of a stored value that the walker reports wherever
                    // it stands. A static field is not safe: its first use can run the initializer of its type.
                    IFieldReferenceOperation ofThis => IsInstanceFieldOfThis(ofThis),
                    _ => false,
                };
            }

            bool IsTheParameter(IOperation target) =>
                target is IParameterReferenceOperation { Parameter: var made } && SymbolEqualityComparer.Default.Equals(made, parameter);

            // Notes the reference that a place is reached through where the one thing that is wrong with the read or the write
            // is that the reference is not known to refer to something: a check of it then makes the code right, and the
            // message says so. A read that copies a value through another reference is not helped by a check.
            string Through(IOperation place, string what)
            {
                var reached = place;

                while (reached is IFieldReferenceOperation { Field: { RefKind: RefKind.None, IsStatic: false }, Instance: { Type.IsValueType: true } owner })
                    reached = owner;

                if (checkable is not null &&
                    !judgesAsBefore &&
                    KnownReferences.GetReference(reached, graph.Function) is { } reference &&
                    !IsKnown(reached) &&
                    !IsCopiedThroughAnotherReference(place))
                {
                    checkable.Value = reference is ITypeSymbol ? "this" : reference.Name;
                }

                return what;
            }

            // A by-value parameter of the method, or the [MovedFrom] parameter itself. A parameter of a primary constructor that a
            // struct keeps is neither: outside the constructor it is a field of 'this', and 'this' in a struct is a reference.
            bool IsOwnParameter(IParameterSymbol referred) =>
                (referred.RefKind is RefKind.None && !KnownReferences.IsKeptByStruct(referred, graph.Function)) || SymbolEqualityComparer.Default.Equals(referred, parameter);

            // A reference that is known to refer to something where the operation that is judged runs: a read or a plain store
            // through it cannot fail there.
            bool IsKnown(IOperation? reference) =>
                !judgesAsBefore &&
                KnownReferences.GetReference(reference, graph.Function) is { } known &&
                graph.TryGetPosition(operation, out var judgedAt) &&
                graph.Known.IsKnownAt(known, judgedAt);

            bool IsKnownAndNotCopiedThrough(IOperation reference) => IsKnown(reference) && !IsCopiedThroughAnotherReference(reference);

            // A by-value read of a value of a non-copyable type, or of a container, through a reference that is not the [MovedFrom]
            // parameter: if the two refer to one variable, it takes the value that was read a second time, or makes a second
            // reference to one task that both callers' variables then hold as their own. Reading the other parameter only to
            // assign it to the parameter, as a swap does, changes nothing in that case.
            bool IsCopiedThroughAnotherReference(IOperation reference)
            {
                if (parameter is null || copiedTypes is null)
                    return false;

                var current = reference;

                while (current.Parent is IFieldReferenceOperation { Field: { RefKind: RefKind.None, IsStatic: false } } part &&
                    ReferenceEquals(part.Instance, current) &&
                    current.Type is { IsValueType: true })
                {
                    current = part;
                }

                if (!copiedTypes.Carries(current.Type) || IsAssignedToParameter())
                    return false;

                while (current.Parent is IParenthesizedOperation or ITupleOperation or IDeclarationExpressionOperation)
                    current = current.Parent;

                return current.Parent switch {
                    IAssignmentOperation assignment when ReferenceEquals(assignment.Target, current) => false,
                    IDeconstructionAssignmentOperation deconstruction when ReferenceEquals(deconstruction.Target, current) => false,
                    ISimpleAssignmentOperation { IsRef: true } => false,
                    IIncrementOrDecrementOperation => false,
                    IMemberReferenceOperation member when ReferenceEquals(member.Instance, current) => false,
                    IInvocationOperation invocation when ReferenceEquals(invocation.Instance, current) => false,
                    IArgumentOperation { Parameter.RefKind: not RefKind.None } => false,
                    _ => true,
                };
            }

            // The value that a pattern tests, found from a reference to it inside the pattern.
            bool IsOwnPatternInput(IOperation input)
            {
                for (var current = input.Parent; current is not null; current = current.Parent)
                {
                    if (current is IIsPatternOperation tested)
                        return IsOwnValue(tested.Value);

                    if (current is not (IPatternOperation or IPropertySubpatternOperation or IMemberReferenceOperation))
                        break;
                }

                return false;
            }

            // A value whose fields are read with nothing that can fail: a variable of the method or a part of one, an object that a
            // field of 'this' in a class holds, or a value that an operation has just produced. A reference, an element and
            // anything that is not named here are not.
            bool IsOwnValue(IOperation value)
            {
                RuntimeHelpers.EnsureSufficientExecutionStack();

                switch (value)
                {
                    case IParenthesizedOperation { Operand: var operand }:
                        return IsOwnValue(operand);

                    case IConversionOperation { OperatorMethod: null, Operand: var converted }:
                        return IsOwnValue(converted);

                    case ILocalReferenceOperation { Local: var local }:
                        return local.RefKind is RefKind.None || aliases.Contains(local) || IsKnown(value);

                    case IParameterReferenceOperation { Parameter: var referred }:
                        return IsOwnParameter(referred) || IsKnown(value);

                    case IFieldReferenceOperation field:
                        return IsPartOfOwnVariable(field);

                    case IInstanceReferenceOperation { ReferenceKind: InstanceReferenceKind.ContainingTypeInstance, Type.IsReferenceType: true }:
                        return true;

                    case IInstanceReferenceOperation { ReferenceKind: InstanceReferenceKind.ContainingTypeInstance }:
                        return IsKnown(value);

                    case IFlowCaptureReferenceOperation captured:
                        var captures = graph.GetCaptures(captured.Id);
                        return captures.Count > 0 && captures.All(capture => IsOwnValue(capture.Value));

                    case IInvocationOperation { TargetMethod.RefKind: RefKind.None }:
                    case IPropertyReferenceOperation { Property.RefKind: RefKind.None }:
                    case IObjectCreationOperation or IDefaultValueOperation or ILiteralOperation or ITupleOperation:
                    case IBinaryOperation or IUnaryOperation or IAssignmentOperation or IAwaitOperation or IIsPatternOperation or IIsTypeOperation:
                        return true;

                    default:
                        return false;
                }
            }

            // A field whose address is a place in a variable or in 'this': neither an object that can be null nor a type that can still
            // be initialized is looked at to get it.
            static bool IsFieldOfAVariableOrOfThis(IFieldReferenceOperation field)
            {
                IOperation? instance = field;

                while (instance is IFieldReferenceOperation { Field: { RefKind: RefKind.None, IsStatic: false }, Instance: { Type.IsValueType: true } owner })
                    instance = owner;

                return instance is ILocalReferenceOperation or IParameterReferenceOperation or
                    IInstanceReferenceOperation { ReferenceKind: InstanceReferenceKind.ContainingTypeInstance } or
                    IFieldReferenceOperation { Field: { RefKind: RefKind.None, IsStatic: false }, Instance: IInstanceReferenceOperation { ReferenceKind: InstanceReferenceKind.ContainingTypeInstance } };
            }

            // 'this' of a struct, if a field is a field of it, at any depth of fields of structs.
            static IOperation? FindStructThis(IFieldReferenceOperation field)
            {
                IOperation? instance = field;

                while (instance is IFieldReferenceOperation { Field: { RefKind: RefKind.None, IsStatic: false }, Instance: { Type.IsValueType: true } owner })
                    instance = owner;

                return instance is IInstanceReferenceOperation { ReferenceKind: InstanceReferenceKind.ContainingTypeInstance, Type.IsValueType: true } ? instance : null;
            }

            static bool IsInstanceFieldOfThis(IFieldReferenceOperation field) =>
                field is { Field: { RefKind: RefKind.None, IsStatic: false }, Instance: IInstanceReferenceOperation { ReferenceKind: InstanceReferenceKind.ContainingTypeInstance, Type.TypeKind: TypeKind.Class } };

            // 'Interlocked.Exchange(ref parameter, ...)' reads and assigns in one operation, and is where the path ends.
            bool IsExchangeOfParameter(IInvocationOperation invocation) =>
                IsInterlockedExchange(invocation) &&
                invocation.Arguments.Length > 0 &&
                invocation.Arguments[0].Value is IParameterReferenceOperation { Parameter: var exchanged } &&
                SymbolEqualityComparer.Default.Equals(exchanged, parameter);
        }
    }
}
