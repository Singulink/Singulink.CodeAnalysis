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
/// The walker's judgment of which containers the method owns: a local that holds a container is the method's own by a list of the
/// uses that are allowed, and a method returns only a container that is its own.
/// </content>
public sealed partial class DoNotCopyValueAnalyzer
{
    private sealed partial class NonCopyableWalker
    {
        // True while the uses of the locals that hold a container are being gathered. It breaks a recursion: gathering them asks whether
        // what a local is given is new (GetContainerLocals -> Acquire), a variable that is read at its last use is new if it can be
        // moved from (IsMovedAtLastUse -> CanBeMovedFrom), and a local that holds a container can be moved from only if it is the
        // method's own (IsLocalOfItsOwnByStrictTest -> GetContainerLocals). While the flag is set, no such local is a new value.
        private bool _isFindingContainerLocals;

        // What the code does with its locals that hold a container, for the check of a container that is returned. Found when first asked for.
        private Dictionary<ILocalSymbol, LocalUse>? _containerLocals;

        /// <summary>
        /// Reports a container that a method, a local function, a lambda or an operator returns by value while something else still holds
        /// it. The result of a call is a new value, so every caller would read the non-copyable values in it as new.
        /// </summary>
        private void CheckReturnedContainer(IReturnOperation operation, IOperation returnedValue)
        {
            // An array and an ArraySegment are checked like any other container: a segment and the typed interfaces of an array hand out
            // elements by value. A span or a memory cannot become an interface, and every by-value read of an element through one is
            // reported whatever it came from. A delegate holds no value.
            // What the getter of a property or an indexer returns is stored for every caller, and so is what a method returns whose
            // return value is marked [Borrowed]. Which returns are checked is asked in one place, also by what describes what a
            // call hands back: the two must not differ.
            if (GetEnclosingMethod(operation) is not { } method || returnedValue.Type is not { } type || !types.IsReturnCheckedWhereItIsWritten(method, type))
                return;

            if (!IsContainerOfItsOwn(returnedValue))
            {
                // A local that the method filled itself and then handed to something else is not helped by "return a container that
                // the method created".
                string because = GetNotOwnHint(returnedValue);

                string advice = because.Length > 0 && SkipWrappingConversions(returnedValue) is ILocalReferenceOperation { Local: var local } && HoldsOnlyItsOwn(local)
                    ? "Hand the container to nothing that keeps it before it is returned"
                    : "Return a container that the method created";

                // [return: Borrowed] says that the method hands out what something holds for good. A method that makes an object
                // and returns what that object hands out acquires what it returns, and no caller could use a result that is stored.
                advice = IsReadOutOfObjectMadeHere(returnedValue)
                    ? advice + ": this one comes out of an object that the method made, so make the container in the method and hand it to the " +
                        "object, as a completion source is made in the method that returns its 'Task'"
                    : advice + ", or mark the method [return: Borrowed] so that callers treat the result as stored";

                ReportAbout(Rules.UnownedContainerReturned, Subject.Of(type), returnedValue.Syntax, Display(method), DescribeContainer(type), because, advice);
            }
        }

        /// <summary>
        /// Gets a value indicating whether a value is read out of an object that the method made: a member of a new object, or of a
        /// local that is only ever assigned new objects, as in 'var waiter = new Waiter(); ... return waiter.Task;'. The member is
        /// one of checked code: a completion source of the framework has a rule of its own for its task.
        /// </summary>
        private bool IsReadOutOfObjectMadeHere(IOperation value)
        {
            var (instance, member) = SkipWrappingConversions(value) switch {
                IPropertyReferenceOperation { Instance: { } of, Property: var property } => (of, (ISymbol)property),
                IFieldReferenceOperation { Instance: { } of, Field: var field } => (of, field),
                IInvocationOperation { Instance: { } of, TargetMethod: var method } => (of, method),
                _ => (null, null),
            };

            if (member is null || types.GetUse(member).Kind is not MemberUseKind.Checked)
                return false;

            switch (instance is null ? null : SkipWrappingConversions(instance))
            {
                case IObjectCreationOperation:
                    return true;

                case ILocalReferenceOperation { Local: var local }:
                    bool isMade = false;

                    foreach (var operation in GetAllOperations())
                    {
                        IOperation? assigned = operation switch {
                            IVariableDeclaratorOperation declarator when SymbolEqualityComparer.Default.Equals(declarator.Symbol, local) =>
                                (declarator.Initializer ?? (declarator.Parent as IVariableDeclarationOperation)?.Initializer)?.Value,
                            ISimpleAssignmentOperation { Target: ILocalReferenceOperation target, Value: var written }
                                when SymbolEqualityComparer.Default.Equals(target.Local, local) => written,
                            _ => null,
                        };

                        if (assigned is null)
                        {
                            // Anything else that can write the local makes it one that the method did not make.
                            if (operation is ILocalReferenceOperation reference &&
                                SymbolEqualityComparer.Default.Equals(reference.Local, local) &&
                                reference.Parent is IArgumentOperation { Parameter.RefKind: RefKind.Ref or RefKind.Out } or IDeconstructionAssignmentOperation or ITupleOperation)
                            {
                                return false;
                            }

                            continue;
                        }

                        if (SkipWrappingConversions(assigned) is not IObjectCreationOperation)
                            return false;

                        isMade = true;
                    }

                    return isMade;

                default:
                    return false;
            }
        }

        /// <summary>
        /// Gets a value indicating whether an operation gives a container that nothing but the code that has it holds: one that it created or
        /// got from a call, or a local that it only filled. One stored branch of a conditional, a '??' or a 'switch' expression is enough to
        /// make it something else's.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <param name="given">True if the value is what the method leaves in a [MovedFrom] parameter, which hands it to the caller. A
        /// local can be handed over in that way once.</param>
        private bool IsContainerOfItsOwn(IOperation value, bool given = false)
        {
            RuntimeHelpers.EnsureSufficientExecutionStack();

            switch (SkipParentheses(value))
            {
                // A container that comes out of a type which hides its non-copyable type is stored, whatever gives the reference. And an
                // object whose own code decides what it holds is not handed on as a framework container.
                case IConversionOperation { OperatorMethod: null } conversion when !types.GainsNonCopyableType(conversion.Operand.Type, conversion.Type):
                    return !IsOpenObjectUsedAsListedContainer(conversion) && IsContainerOfItsOwn(conversion.Operand, given);

                case var choice when GetBranches(choice) is { } branches:
                    return branches.All(branch => IsContainerOfItsOwn(branch, given));

                case ILocalReferenceOperation localReference:
                    return IsLocalOfItsOwn(localReference.Local, given);

                // What is created is its own only as far as what is put into it is.
                case var other:
                    return IsNewWithNothingStored(other);
            }
        }

        /// <summary>
        /// Gets a value indicating whether a local holds a container that only the local holds: every value that it is assigned is one,
        /// no lambda or local function captures it, and every other use of it is on a list of what is allowed.
        /// </summary>
        /// <remarks>
        /// The list is that of <see cref="IsQuietUseOfLocalThatHoldsContainers"/>. A local that holds values directly, as a
        /// List&lt;Guard&gt; does, may also be awaited, be passed to something that only uses it during the call, and have an element
        /// of it used in place: see <see cref="NoteContainerLocalReference"/>.
        /// </remarks>
        private bool IsLocalOfItsOwn(ILocalSymbol local, bool given = false, IArgumentOperation? movedAt = null)
        {
            if (!GetContainerLocals().TryGetValue(local, out var use) || !use.IsDeclared || use.IsShared)
                return false;

            // A local that the method leaves in a [MovedFrom] parameter is the caller's from then on: it is no longer the method's own to
            // return or to move, and it can be left there only once.
            if (use.GivenTo is { } givenTo && (!given || givenTo.Count > 1 || use.Yields is not null || IsInLoopOutside(givenTo[0], use.Declarator)))
                return false;

            // What the local was passed to as a [MovedFrom] argument must have left a container in it that the method owns.
            if (use.MovedBy is { } movedBy && !movedBy.All(moving => ReferenceEquals(moving, movedAt) || LeavesOwnedContainer(moving)))
                return false;

            // An iterator goes on after it has yielded: a local that it yields at two places, or in a loop that the local is declared outside
            // of, is handed out more than once.
            if (use.Yields is { } yields && (yields.Count > 1 || IsInLoopOutside(yields[0], use.Declarator)))
                return false;

            if (use.IsOwned is { } known)
                return known;

            // A local that is assigned another local is shared with it, so no chain of locals leads back here. This is for what is not foreseen.
            // A stored container that the method put into the local is held by the local from then on, and by whatever held it before.
            use.IsOwned = false;
            bool isOwned = use.Values is null || use.Values.All(v => IsContainerOfItsOwn(v));
            use.IsOwned = isOwned;
            return isOwned;
        }

        /// <summary>
        /// Gets a value indicating whether every value that a local is assigned is a container of the method's own, whatever the
        /// method then does with the local.
        /// </summary>
        private bool HoldsOnlyItsOwn(ILocalSymbol local) =>
            GetContainerLocals().TryGetValue(local, out var use) && use.IsDeclared && (use.Values is null || use.Values.All(value => IsContainerOfItsOwn(value)));

        private Dictionary<ILocalSymbol, LocalUse> GetContainerLocals()
        {
            if (_containerLocals is not null)
                return _containerLocals;

            var locals = new Dictionary<ILocalSymbol, LocalUse>(SymbolEqualityComparer.Default);
            _isFindingContainerLocals = true;

            foreach (var operation in GetAllOperations())
            {
                switch (operation)
                {
                    case IVariableDeclaratorOperation { Symbol: var declared } declarator when types.IsContainer(declared.Type):
                        var declaredUse = GetUse(declared);

                        // The variable of a 'foreach' is given the elements of something else.
                        if (declarator.Parent is not IVariableDeclarationOperation declaration || declared.RefKind is not RefKind.None)
                        {
                            Share(declaredUse, "it is given what something else holds", declarator);
                            break;
                        }

                        declaredUse.IsDeclared = true;
                        declaredUse.Declarator = declarator;

                        if ((declarator.Initializer ?? declaration.Initializer)?.Value is { } initialValue)
                            (declaredUse.Values ??= []).Add(initialValue);

                        break;

                    case ILocalReferenceOperation { Local: var referenced } reference when types.IsContainer(referenced.Type):
                        NoteContainerLocalReference(reference, GetUse(referenced));
                        break;
                }
            }

            _isFindingContainerLocals = false;
            return _containerLocals = locals;

            LocalUse GetUse(ILocalSymbol local)
            {
                if (!locals.TryGetValue(local, out var use))
                    locals[local] = use = new LocalUse();

                return use;
            }
        }

        private void NoteContainerLocalReference(ILocalReferenceOperation reference, LocalUse use)
        {
            // A variable that a lambda or a local function captures is a field in fact.
            if (IsCapturedAt(reference, reference.Local))
            {
                Share(use, "a lambda or a local function uses it", reference);
                return;
            }

            // The deconstruction of a tuple literal assigns each element to its variable, as a declaration or an assignment does.
            if (FindDeconstructedElement(reference) is { } element)
            {
                if (reference.IsDeclaration)
                {
                    use.IsDeclared = true;
                    use.Declarator = reference;
                }

                (use.Values ??= []).Add(element);
                return;
            }

            // Any other variable that an 'out' argument or a deconstruction declares is given what something else hands out.
            if (reference.IsDeclaration)
            {
                Share(use, "it is given what something else hands out", reference);
                return;
            }

            // What is made of the local that is its container under another name, a view that is no ref struct or the container as
            // another type, is used where it is made or ends the local being the method's own: whatever holds it holds the container.
            // At the last use of the local nothing is left to end: what is made takes the place of the local, and is as new as
            // the local is, as in 'var waiting = task.WaitAsync(timeout); return await waiting;'. A move is judged by itself, with
            // what it hands back: see LeavesOwnedContainer.
            if (DescribeWhatIsMade(reference, reference.Local.Type, out var made) is MadeOfContainer.UnderAnotherName &&
                !IsUsedWhereItIsMade(made!) &&
                !(SkipParenthesesUpward(reference).Parent is IArgumentOperation { Parameter: { } moving } && NonCopyableTypes.IsMovedFrom(moving.OriginalDefinition)) &&
                !(_flow ??= new(context, types)).IsLastUse(reference, reference.Local))
            {
                Share(use, DescribeUse(reference), reference);
                return;
            }

            // A member that the use binds with nothing written for it, and that takes the local as a parameter, is other code that
            // the local is passed to: an extension 'GetAwaiter', an extension property in a pattern, the constructor of a handler
            // that the call passes its argument on to. Where that keeps, something else holds the container.
            if (FindBoundMemberThatKeeps(reference) is var (keeping, taking))
            {
                Share(use, DescribePassing(taking, null, keeping), reference);
                return;
            }

            // A local that holds containers inside a container is held to the list of what is allowed.
            if (use.HoldsContainers ??= types.HoldsContainers(reference.Local.Type))
            {
                if (!IsQuietUseOfLocalThatHoldsContainers(reference, use))
                    Share(use, DescribeUse(reference), reference);

                return;
            }

            // And so is one that holds values directly. A list of what ends a local being the method's own always misses a way: a
            // marked collection that keeps the list, a helper that hands its argument back, a 'params' parameter, a view of the
            // local that is stored. A few more uses are allowed here, since nothing stored can be put into such a container by code
            // that is checked. The last use of the local allows less: see IsLocalOfItsOwnByStrictTest.
            IOperation current = SkipParenthesesUpward(reference);

            // The compiler's own awaiter reads the result out by value, and keeps nothing.
            if (current.Parent is IAwaitOperation awaited && IsQuietResult(awaited.Type))
                return;

            if (IsViewUsedOnTheSpot(current, reference.Local.Type) || IsQuietUseOfLocalThatHoldsContainers(reference, use))
                return;

            if (IsOnlyUsedByWhatItIsPassedTo(current, reference.Local.Type) ||
                IsElementUsedInPlace(current) ||
                IsFieldOfLocalUsed(current) ||
                IsViewMadeAndUsedOnTheSpot(current))
            {
                use.IsUsedLoosely = true;
            }
            else
            {
                Share(use, DescribeUse(reference), reference);
            }
        }

        private static void Share(LocalUse use, string because, IOperation at)
        {
            use.IsShared = true;

            if (use.SharedBecause is null)
            {
                use.SharedBecause = because;
                use.SharedAt = at.Syntax;
            }
        }

        /// <summary>
        /// Says what a use of a local does that can leave its container with something else, in words that go after "because".
        /// </summary>
        private string DescribeUse(IOperation reference)
        {
            var current = reference;

            while (current.Parent is
                IParenthesizedOperation or ITupleOperation or ICoalesceOperation or IConversionOperation { OperatorMethod: null } or
                { IsImplicit: true, Kind: OperationKind.ArrayInitializer or OperationKind.ArrayCreation or OperationKind.CollectionExpression })
            {
                current = current.Parent;
            }

            // A member that is called with '?.' is named like the call that it stands for.
            if (current.Parent is IConditionalAccessOperation access && ReferenceEquals(current, access.Operation) && FindConditionalAccessInstance(access) is { } instance)
                current = instance;

            return current.Parent switch {
                IArgumentOperation { Parent.Parent: IInterpolatedStringAppendOperation } => "it is passed to an interpolated-string handler",
                IArgumentOperation { Parameter: { } parameter } argument => DescribePassing(parameter, argument.Parent),
                IConversionOperation { OperatorMethod.Parameters: [var converted] } => DescribePassing(converted, null),
                IBinaryOperation { OperatorMethod.Parameters: [var left, var right] } binary =>
                    DescribePassing(ReferenceEquals(binary.LeftOperand, current) ? left : right, null),
                IConversionOperation { OperatorMethod: { } conversion } => $"it is passed to '{Display(conversion)}'",
                IBinaryOperation { OperatorMethod: { } binary } => $"it is passed to '{Display(binary)}'",
                ICompoundAssignmentOperation { OperatorMethod: { } compound } => $"it is passed to '{Display(compound)}'",
                IInvocationOperation invocation when ReferenceEquals(invocation.Instance, current) =>
                    $"'{Display(invocation.TargetMethod)}' can keep it, hand out a view of it or take in what something else holds",
                IPropertyReferenceOperation property when ReferenceEquals(property.Instance, current) =>
                    $"'{Display(property.Property)}' can hand out a view of it or take in what something else holds",
                IAssignmentOperation or IVariableInitializerOperation or IFieldInitializerOperation or IPropertyInitializerOperation or
                    IArrayInitializerOperation or ICollectionExpressionOperation => "it is assigned to something else",
                _ => "it is used in a way that can keep it or a view of it",
            };
        }

        /// <summary>
        /// Says what a local is passed to, and what that does with it where the declaration shows it.
        /// </summary>
        private string DescribePassing(IParameterSymbol parameter, IOperation? call, ISymbol? member = null)
        {
            string target = Display(member ?? parameter.ContainingSymbol);

            if (parameter.OriginalDefinition.Type is ITypeParameterSymbol)
                return $"it is passed to '{target}', which is taken to keep what a type parameter is given";

            if (NonCopyableTypes.KeepsWhatItIsGiven(parameter))
                return $"it is passed to '{target}', which keeps it";

            // The receiver of a member of an extension block is judged with the member that it is passed to.
            var judged = member is null
                ? types.JudgeReceivingBySignature(parameter)
                : types.JudgeReceiving(parameter, member, MadeOfContainer.Nothing, handsBackThroughOut: false);

            if (judged is ReceivedContainer.KeptByUncheckedCode or ReceivedContainer.KeptByUnlistedCode)
                return $"it is passed to '{target}', which is taken to keep it: the type of its parameter mentions a type parameter that is not marked";

            if (judged is ReceivedContainer.KeptByUnmarkedAssembly)
                return $"it is passed to '{target}', which is taken to keep it: the member is of an assembly that was not built with the analyzer, and the list does not mark it 'borrows'";

            if (call is IInvocationOperation { TargetMethod: var method } invocation &&
                (method.ReturnsByRef || method.ReturnsByRefReadonly || types.IsView(invocation.Type) || invocation.Type is { IsRefLikeType: true }))
            {
                return $"it is passed to '{target}', which hands out a view of it";
            }

            return $"it is passed to '{target}'";
        }

        /// <summary>
        /// Gets what a message adds about a container that is read from or returned where it is a local which is not the method's
        /// own: the name of the local and what made it so. Empty for anything else.
        /// </summary>
        /// <remarks>
        /// One use anywhere in the method ends the local being the method's own for all of it, also for a read that comes before
        /// that use. So the text says "anywhere in the method" and names the line of the use: it does not read as if the use had
        /// come first.
        /// </remarks>
        private string GetNotOwnHint(IOperation? source)
        {
            if (GetWhyNotOwn(source, out var local) is { } because)
                return $": '{local!.Name}' is not the method's own anywhere in the method, because {because}";

            // A container that a member of an assembly that was not built with the analyzer hands out is stored, unless the list says 'new'.
            while (source is IParenthesizedOperation or IConversionOperation { OperatorMethod: null })
                source = source is IConversionOperation conversion ? conversion.Operand : ((IParenthesizedOperation)source).Operand;

            // An object of a class of such an assembly that derives from a container is never new, wherever it comes from.
            if (source is not null && types.IsUncheckedDerivedContainer(source.Type))
                return GetUncheckedDerivedContainerHint(source.Type);

            ISymbol? member = source switch {
                IInvocationOperation invocation => invocation.TargetMethod,
                IPropertyReferenceOperation property => property.Property,
                IAwaitOperation { Syntax: AwaitExpressionSyntax awaitSyntax } awaited => awaited.SemanticModel?.GetAwaitExpressionInfo(awaitSyntax).GetResultMethod,
                _ => null,
            };

            if (member is not null && types.HandsOutUncheckedContainer(member, source!.Type, GetDeclaredResultType(member)))
                return GetUncheckedSourceHint(member, source.Type);

            return string.Empty;
        }

        /// <summary>
        /// Gets what a message adds about a container that a member of an assembly that was not built with the analyzer hands out, which
        /// counts as stored: a list entry marked 'new' says otherwise, except for an object of a class of such an assembly that derives
        /// from a container, for which no entry helps.
        /// </summary>
        private string GetUncheckedSourceHint(ISymbol member, ITypeSymbol? handedOut)
        {
            if (types.IsUncheckedDerivedContainer(handedOut))
                return GetUncheckedDerivedContainerHint(handedOut);

            var definition = NonCopyableTypes.GetListDefinition(member);
            return $": '{Display(definition)}' is a member of an assembly that was not built with the analyzer, so the container that it " +
                $"hands out counts as held by something else; if it hands out a container that nothing else holds, add " +
                $"'{GetListId(definition)};new' to a NonCopyableSymbols.txt file";
        }

        private static string GetUncheckedDerivedContainerHint(ITypeSymbol type) =>
            $": '{type}' is of an assembly that was not built with the analyzer and is a container only through what it derives from or " +
            "implements, so nothing checks what its own code puts in it, and an object of it is never a new container";

        /// <summary>
        /// Gets what made a local that holds a container not the method's own, in words that go after "because" and start with
        /// the line of the use, or null for anything else.
        /// </summary>
        private string? GetWhyNotOwn(IOperation? source, out ILocalSymbol? local)
        {
            while (source is IParenthesizedOperation or IConversionOperation { OperatorMethod: null })
                source = source is IConversionOperation conversion ? conversion.Operand : ((IParenthesizedOperation)source).Operand;

            local = (source as ILocalReferenceOperation)?.Local;

            if (local is null || !GetContainerLocals().TryGetValue(local, out var use) || use.SharedBecause is not { } because)
                return null;

            return use.SharedAt is { } at ? $"on line {at.GetLocation().GetMappedLineSpan().StartLinePosition.Line + 1} {because}" : because;
        }

        /// <summary>
        /// Gets the name of a container type for a message, in quotes. A class that is a container only through a base class or an
        /// interface is named with that, since its own name does not show what it holds.
        /// </summary>
        private string DescribeContainer(ITypeSymbol? type)
        {
            var shown = type is { IsValueType: false, NullableAnnotation: NullableAnnotation.Annotated } ? type.WithNullableAnnotation(NullableAnnotation.NotAnnotated) : type;
            return types.GetContainerBehind(shown) is { } behind ? $"'{shown}' (a container through '{behind}')" : $"'{shown}'";
        }

        /// <summary>
        /// Gets a value indicating whether a field of the object that a local holds is read, used in place, or assigned a new value:
        /// 'holder._value = Create();' puts nothing into the object that something else holds.
        /// </summary>
        private bool IsFieldOfLocalUsed(IOperation current)
        {
            if (current.Parent is not IFieldReferenceOperation { Field.RefKind: RefKind.None } field || !ReferenceEquals(current, field.Instance))
                return false;

            if (IsAssignmentTarget(field, out var assigned))
                return assigned is not null && AreCarriedValuesNew(assigned);

            return IsReferenceUsedInPlace(field);
        }

        /// <summary>
        /// Gets a value indicating whether a view of a local is made and used in one go, so that no view is left when the statement
        /// or the loop is over: 'foreach (ref Guard guard in CollectionsMarshal.AsSpan(list))', and
        /// 'CollectionsMarshal.AsSpan(list)[0].Dispose()'. A view that is put into a variable, passed on or returned is not one.
        /// </summary>
        private bool IsViewMadeAndUsedOnTheSpot(IOperation current)
        {
            // The local is the one thing that the call is given which carries a non-copyable type.
            IInvocationOperation? invocation = current.Parent switch {
                IArgumentOperation { Parameter.RefKind: RefKind.None, Parent: IInvocationOperation called } argument
                    when called.Arguments.All(other => ReferenceEquals(other, argument) || !CarriesNonCopyableType(other.Value)) => called,
                IInvocationOperation { Arguments.IsEmpty: true } called when ReferenceEquals(current, called.Instance) => called,
                _ => null,
            };

            if (invocation is not { TargetMethod: { ReturnsByRef: false, ReturnsByRefReadonly: false } } || !types.IsView(invocation.Type))
                return false;

            IOperation view = invocation;

            while (view.Parent is IParenthesizedOperation or IConversionOperation { OperatorMethod: null, Conversion.IsIdentity: true })
                view = view.Parent;

            return view.Parent switch {
                IForEachLoopOperation loop => ReferenceEquals(view, loop.Collection),
                IPropertyReferenceOperation { Property.IsIndexer: true, Arguments: [{ Value.Type.SpecialType: SpecialType.System_Int32 }] } element
                    when ReferenceEquals(view, element.Instance) => IsReferenceUsedInPlace(element),
                IPropertyReferenceOperation { Property: { IsIndexer: false, ReturnsByRef: false, ReturnsByRefReadonly: false } } property
                    when ReferenceEquals(view, property.Instance) => IsNoViewAndNoReference(property.Type),
                _ => false,
            };
        }

        /// <summary>
        /// Gets a value indicating whether a local that holds values directly, as a List&lt;Guard&gt; does, is passed to something that
        /// only uses it during the call: it is passed by value to a parameter that is declared with the container's own type and is
        /// not taken to keep what it is given, of a method, a local function or a delegate whose result is no reference, no view and
        /// no ref struct and is not marked [Borrowed], and which hands out no view through another parameter. That keeps
        /// 'Fill(list); return list;' clean. The receiver of a member of an extension block is such a parameter.
        /// </summary>
        /// <remarks>
        /// A parameter whose declared type is a type parameter is not one, marked or not: a marked collection keeps what it is given.
        /// Nor is one whose declared type mentions a type parameter that is not marked: the body behind it is not checked, and a
        /// member of the framework that takes an array can keep it, as a pool does. An implicit 'params' collection, a conversion, an
        /// operator and an interpolated-string handler are not on the list either.
        /// </remarks>
        private bool IsOnlyUsedByWhatItIsPassedTo(IOperation current, ITypeSymbol localType)
        {
            IParameterSymbol? parameter;
            IMethodSymbol? method;
            ITypeSymbol? result;

            switch (current.Parent)
            {
                case IArgumentOperation { Parameter: { } receiving, Parent: IInvocationOperation invocation }:
                    (parameter, method, result) = (receiving, invocation.TargetMethod, invocation.Type);
                    break;

                case IInvocationOperation { TargetMethod: { ContainingType: { IsExtension: true, ExtensionParameter: { } receiver } } called } onReceiver
                    when ReferenceEquals(current, onReceiver.Instance):
                    (parameter, method, result) = (receiver, called, onReceiver.Type);
                    break;

                default:
                    return false;
            }

            // Whether what receives it keeps is asked where the check of a parameter asks it. A local allows less besides: only a
            // parameter of its own type, by value, since a parameter of another type can be one that hides what it is given.
            if (parameter.RefKind is not RefKind.None ||
                !SymbolEqualityComparer.Default.Equals(parameter.Type, localType) ||
                JudgeWhatReceives(parameter, method, current.Parent as IArgumentOperation, MadeOfContainer.Nothing) is not ReceivedContainer.Used)
            {
                return false;
            }

            if (method.MethodKind is not (MethodKind.Ordinary or MethodKind.LocalFunction or MethodKind.DelegateInvoke or MethodKind.ReducedExtension) ||
                method.ReturnsByRef ||
                method.ReturnsByRefReadonly ||
                NonCopyableTypes.HasBorrowedReturn(method) ||
                !IsNoViewAndNoReference(result))
            {
                return false;
            }

            // An 'out' or 'ref' parameter can hand out a view over the local as well as the result can.
            foreach (var other in NonCopyableTypes.GetParametersWithReceiver(method))
            {
                if (other.RefKind is RefKind.Out or RefKind.Ref && !IsNoViewAndNoReference(other.Type))
                    return false;
            }

            return true;
        }

        private bool IsNoViewAndNoReference(ITypeSymbol? type) =>
            type is null or { IsRefLikeType: false, TypeKind: not (TypeKind.Pointer or TypeKind.FunctionPointer) } && !types.IsView(type);

        /// <summary>
        /// Gets a value indicating whether a local array of values is used through an element in place, in a way that leaves no
        /// reference to the element behind: a member is used on the element, or the element is passed by reference to a call that
        /// can hand no reference on. An element that is read or assigned is on the list for every container.
        /// </summary>
        private bool IsElementUsedInPlace(IOperation current) =>
            current.Parent is IArrayElementReferenceOperation element && ReferenceEquals(current, element.ArrayReference) && IsReferenceUsedInPlace(element);

        /// <summary>
        /// Gets a value indicating whether a reference to a part of a container, an element or a field, is used where it stands and
        /// leaves no reference behind: it is assigned, a member is used on it, or it is passed by reference to a call that can hand
        /// no reference on.
        /// </summary>
        private bool IsReferenceUsedInPlace(IOperation element)
        {
            IOperation used = SkipParenthesesUpward(element);

            switch (used.Parent)
            {
                case IAssignmentOperation assignment when ReferenceEquals(used, assignment.Target):
                    return assignment is not ISimpleAssignmentOperation { IsRef: true };

                case IIncrementOrDecrementOperation:
                    return true;

                case IFieldReferenceOperation field when ReferenceEquals(used, field.Instance):
                    return field.Field.RefKind is RefKind.None;

                case IPropertyReferenceOperation property when ReferenceEquals(used, property.Instance):
                    return ReturnsByValue(property.Property) && IsNoViewAndNoReference(property.Type);

                case IEventReferenceOperation raised when ReferenceEquals(used, raised.Instance):
                    return true;

                case IInvocationOperation invocation when ReferenceEquals(used, invocation.Instance):
                    return ReturnsByValue(invocation.TargetMethod) && IsNoViewAndNoReference(invocation.Type);

                // A reference to the element lives on only in what the call returns, or in a ref struct that the call was given.
                case IArgumentOperation { Parameter: { RefKind: not RefKind.None } parameter, Parent: IInvocationOperation invocation }:
                    return NonCopyableTypes.IsMovedFrom(parameter.OriginalDefinition) ||
                        (!invocation.TargetMethod.ReturnsByRef &&
                            !invocation.TargetMethod.ReturnsByRefReadonly &&
                            IsNoViewAndNoReference(invocation.Type) &&
                            invocation.Instance?.Type is not { IsRefLikeType: true } &&
                            NonCopyableTypes.GetParametersWithReceiver(invocation.TargetMethod).All(p => !p.Type.IsRefLikeType));

                default:
                    return false;
            }
        }

        /// <summary>
        /// Gets the element of a tuple literal that a deconstruction assigns to a local: in 'var (a, b) = (First(), Second());' that is
        /// 'First()' for 'a'. Null if the local is not a target of a deconstruction of a tuple literal.
        /// </summary>
        private IOperation? FindDeconstructedElement(ILocalReferenceOperation reference)
        {
            // The indexes of the tuples that the local is in, innermost first.
            var path = new List<int>();
            IOperation current = reference;

            while (true)
            {
                switch (current.Parent)
                {
                    case IParenthesizedOperation or IDeclarationExpressionOperation:
                        current = current.Parent;
                        continue;

                    case ITupleOperation tuple:
                        path.Add(tuple.Elements.IndexOf(current));
                        current = tuple;
                        continue;

                    case IDeconstructionAssignmentOperation deconstruction when path.Count > 0 && ReferenceEquals(current, deconstruction.Target):
                        var value = deconstruction.Value;

                        for (int i = path.Count - 1; i >= 0; i--)
                        {
                            if (SkipWrappingConversions(value) is not ITupleOperation literal || path[i] < 0 || path[i] >= literal.Elements.Length)
                                return null;

                            value = literal.Elements[path[i]];
                        }

                        return value;

                    default:
                        return null;
                }
            }
        }

        /// <summary>
        /// Gets a value indicating whether a use of a local that holds containers inside a container leaves it the method's own. That is
        /// an assignment of the local as a whole, whose value is noted; a member that is called on it with new values for every container
        /// argument and that hands out no reference, no view and no container of containers; an element or an indexer of it that is
        /// assigned a new value, or read by value; a 'foreach' over it by value; a test for null; returning it; and moving it. Anything
        /// else ends it, being passed to another method included.
        /// </summary>
        /// <remarks>
        /// A quiet use, here and in the names of the functions below, is one that is on the list of what is allowed: it leaves the
        /// local the method's own.
        /// </remarks>
        private bool IsQuietUseOfLocalThatHoldsContainers(ILocalReferenceOperation reference, LocalUse use)
        {
            IOperation current = SkipParenthesesUpward(reference);

            switch (current.Parent)
            {
                case ISimpleAssignmentOperation { IsRef: false } assignment when ReferenceEquals(current, assignment.Target):
                    (use.Values ??= []).Add(assignment.Value);
                    return true;

                case ICoalesceAssignmentOperation assignment when ReferenceEquals(current, assignment.Target):
                    (use.Values ??= []).Add(assignment.Value);
                    return true;

                case IArgumentOperation { Parameter: { } parameter } moving when NonCopyableTypes.IsMovedFrom(parameter.OriginalDefinition):
                    (use.MovedBy ??= []).Add(moving);
                    return true;

                // A member that is called with '?.' is a use of the same kind as the call that it stands for.
                case IConditionalAccessOperation access when ReferenceEquals(current, access.Operation):
                    return FindConditionalAccessInstance(access) is { } accessInstance && IsQuietMemberUse(accessInstance, reference.Local.Type) is true;

                // And so is a pattern that reads members which are not containers.
                case IIsPatternOperation isPattern when ReferenceEquals(current, isPattern.Value):
                    return IsQuietPattern(isPattern.Pattern);
            }

            if (IsQuietMemberUse(current, reference.Local.Type) is { } isQuietMemberUse)
                return isQuietMemberUse;

            // Up to a return or a 'foreach', through what only passes the local on.
            while (IsPassedOn(current))
                current = current.Parent!;

            switch (current.Parent)
            {
                case IReturnOperation { Kind: OperationKind.YieldReturn } yield:
                    (use.Yields ??= []).Add(yield);
                    return true;

                case IReturnOperation:
                    return true;

                // Left in a [MovedFrom] parameter of the method, which hands it to the caller.
                case ISimpleAssignmentOperation { IsRef: false } giving when ReferenceEquals(current, giving.Value) && IsMovedFromContainerParameter(giving.Target):
                    (use.GivenTo ??= []).Add(giving);
                    return true;

                // A comparison with null, which converts both sides to object.
                case IBinaryOperation { OperatorMethod: null, OperatorKind: BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals } comparison:
                    return IsNullConstant(comparison.LeftOperand) || IsNullConstant(comparison.RightOperand);

                // A by-value variable cannot write back, and the enumerator is the compiler's own.
                case IForEachLoopOperation { LoopControlVariable: IVariableDeclaratorOperation { Symbol: { RefKind: RefKind.None } variable } } loop
                    when ReferenceEquals(current, loop.Collection):
                    return !types.HoldsContainers(variable.Type);

                default:
                    return false;
            }
        }

        /// <summary>
        /// Gets a value indicating whether what a member hands out by value gives no way to put a container into what it was called on: it
        /// is no view and no ref struct, which can refer to the storage, and no container of containers, which can be filled in turn.
        /// </summary>
        private bool IsQuietResult(ITypeSymbol? type) =>
            type is null || (type is { IsRefLikeType: false, TypeKind: not (TypeKind.Pointer or TypeKind.FunctionPointer) } && !types.IsView(type) && !types.HoldsContainers(type));

        /// <summary>
        /// Judges a member that is used on a local which holds containers inside a container: a method that is called on it, a property
        /// or an indexer of it, or an element of it. Null if the operation is not the receiver of one of those.
        /// </summary>
        private bool? IsQuietMemberUse(IOperation receiver, ITypeSymbol holder)
        {
            // The receiver of a member of an extension block is a parameter of other code, which the local is passed to.
            if (receiver.Parent is IInvocationOperation { TargetMethod.ContainingType.IsExtension: true } or IPropertyReferenceOperation { Property.ContainingType.IsExtension: true })
                return false;

            switch (receiver.Parent)
            {
                case IInvocationOperation invocation when ReferenceEquals(receiver, invocation.Instance):
                    return !invocation.TargetMethod.ReturnsByRef &&
                        !invocation.TargetMethod.ReturnsByRefReadonly &&
                        IsQuietResult(invocation.Type) &&
                        AreQuietArguments(invocation.Arguments, holder);

                case IPropertyReferenceOperation propertyReference when ReferenceEquals(receiver, propertyReference.Instance):
                    if (!AreQuietArguments(propertyReference.Arguments, holder))
                        return false;

                    if (IsAssignmentTarget(propertyReference, out var assignedToProperty))
                        return assignedToProperty is not null && types.IsElementTypeOf(propertyReference.Property.Type, holder) && AreCarriedValuesNew(assignedToProperty);

                    return ReturnsByValue(propertyReference.Property) && IsQuietResult(propertyReference.Type);

                case IArrayElementReferenceOperation element when ReferenceEquals(receiver, element.ArrayReference):
                    if (IsAssignmentTarget(element, out var assignedToElement))
                        return assignedToElement is not null && AreCarriedValuesNew(assignedToElement);

                    return !types.HoldsContainers(element.Type) && IsOnlyRead(element);

                default:
                    return null;
            }
        }

        /// <summary>
        /// Gets a value indicating whether a pattern only tests a value and reads members of it that are not containers. A variable that
        /// it declares for the value would be a second reference to it, and a Deconstruct method can do anything.
        /// </summary>
        private bool IsQuietPattern(IPatternOperation pattern)
        {
            RuntimeHelpers.EnsureSufficientExecutionStack();

            return pattern switch {
                IConstantPatternOperation or ITypePatternOperation or IRelationalPatternOperation or IDiscardPatternOperation => true,
                INegatedPatternOperation negated => IsQuietPattern(negated.Pattern),
                IBinaryPatternOperation binary => IsQuietPattern(binary.LeftPattern) && IsQuietPattern(binary.RightPattern),
                IDeclarationPatternOperation declaration => declaration.DeclaredSymbol is null,
                IRecursivePatternOperation recursive =>
                    recursive.DeclaredSymbol is null &&
                    recursive.DeconstructSymbol is null &&
                    recursive.DeconstructionSubpatterns.IsEmpty &&
                    recursive.PropertySubpatterns.All(sub => sub.Member is IMemberReferenceOperation { Type: var memberType } && !types.Carries(memberType)),
                _ => false,
            };
        }

        /// <summary>
        /// Gets a value indicating whether the arguments of a member bring nothing stored into what the member is called on. An argument
        /// qualifies if its type carries no non-copyable type, if it is a new value of exactly an element type of the container, or if
        /// it goes to a [MovedFrom] parameter, whose move is reported if it is not one. A delegate, an object that implements an
        /// interface over the element type, an array or a span of it does not qualify, new or not: code can push any stored container in.
        /// What an 'out' parameter hands out is judged like a result.
        /// </summary>
        private bool AreQuietArguments(ImmutableArray<IArgumentOperation> arguments, ITypeSymbol holder)
        {
            foreach (var argument in arguments)
            {
                if (argument.Parameter is not { } parameter)
                    return false;

                if (parameter.RefKind is RefKind.Out)
                {
                    if (!IsQuietResult(argument.Value.Type))
                        return false;

                    continue;
                }

                if (NonCopyableTypes.IsMovedFrom(parameter.OriginalDefinition) || (!types.Carries(parameter.Type) && !CarriesNonCopyableType(argument.Value)))
                    continue;

                if (!types.IsElementTypeOf(parameter.Type, holder) || !AreCarriedValuesNew(argument.Value))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Gets a value indicating whether an element or a member is written to, and the value that '=' or '??=' assigns to it. The value
        /// is null for any other way of writing, such as a compound assignment or an increment.
        /// </summary>
        private static bool IsAssignmentTarget(IOperation target, out IOperation? value)
        {
            value = null;

            switch (target.Parent)
            {
                case ISimpleAssignmentOperation { IsRef: false } assignment when ReferenceEquals(target, assignment.Target):
                    value = assignment.Value;
                    return true;

                case ICoalesceAssignmentOperation assignment when ReferenceEquals(target, assignment.Target):
                    value = assignment.Value;
                    return true;

                case IAssignmentOperation assignment when ReferenceEquals(target, assignment.Target):
                case IIncrementOrDecrementOperation:
                    return true;

                default:
                    return false;
            }
        }

        /// <summary>
        /// Gets a value indicating whether an element of an array is only read: awaited, passed or assigned by value, tested, returned by
        /// value, or used as the object that a member is called on. Anything else may take a reference to the slot.
        /// </summary>
        private bool IsOnlyRead(IArrayElementReferenceOperation element)
        {
            IOperation current = element;

            while (current.Parent is IParenthesizedOperation or IConversionOperation { OperatorMethod: null })
                current = current.Parent;

            bool isObject = element.Type is { IsReferenceType: true };

            return current.Parent switch {
                IAwaitOperation or IExpressionStatementOperation or IIsPatternOperation => true,
                IArgumentOperation { Parameter.RefKind: RefKind.None } => true,
                IVariableInitializerOperation { Parent: IVariableDeclaratorOperation { Symbol.RefKind: RefKind.None } } => true,
                ISimpleAssignmentOperation { IsRef: false } assignment => ReferenceEquals(current, assignment.Value),
                IMemberReferenceOperation memberReference => isObject && ReferenceEquals(current, memberReference.Instance),
                IInvocationOperation invocation => isObject && ReferenceEquals(current, invocation.Instance),
                IConditionalAccessOperation access => ReferenceEquals(current, access.Operation),
                IReturnOperation returned => GetReturnRefKind(returned) is RefKind.None,
                IBinaryOperation { OperatorMethod: null } => true,
                _ => false,
            };
        }

        /// <summary>
        /// Gets a value indicating whether an operation is in a loop that a declaration is not in, so that it can run more than once for one
        /// run of the declaration.
        /// </summary>
        private static bool IsInLoopOutside(IOperation operation, IOperation? declaration)
        {
            for (var current = operation.Parent; current is not null and not (IAnonymousFunctionOperation or ILocalFunctionOperation); current = current.Parent)
            {
                if (current is not ILoopOperation loop)
                    continue;

                bool declaredInLoop = false;

                for (var parent = declaration?.Parent; parent is not null && !declaredInLoop; parent = parent.Parent)
                    declaredInLoop = ReferenceEquals(parent, loop);

                if (!declaredInLoop)
                    return true;
            }

            return false;
        }
    }

    /// <summary>
    /// What the code of a member does with a local that holds a container.
    /// </summary>
    private sealed class LocalUse
    {
        /// <summary>
        /// Gets or sets a value indicating whether a declaration statement declares the local, which is the only kind of local that the code
        /// can have filled itself.
        /// </summary>
        public bool IsDeclared { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether something other than the local can hold its container: the code assigns the local to
        /// something, passes it by reference, or a lambda or a local function captures it.
        /// </summary>
        public bool IsShared { get; set; }

        /// <summary>
        /// Gets or sets the values that the local is assigned.
        /// </summary>
        public List<IOperation>? Values { get; set; }

        /// <summary>
        /// Gets or sets what made something other than the local hold its container, in words for a message that go after "because".
        /// It is the first such thing that the code does.
        /// </summary>
        public string? SharedBecause { get; set; }

        /// <summary>
        /// Gets or sets where the code does what <see cref="SharedBecause"/> names.
        /// </summary>
        public SyntaxNode? SharedAt { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the code uses the local in a way that leaves it the method's own to return or to
        /// move, and still not one whose last use is a move: it passes it to something that only uses it during the call, or uses an
        /// element of it in place.
        /// </summary>
        public bool IsUsedLoosely { get; set; }

        /// <summary>
        /// Gets or sets the declarator of the local, if a declaration statement declares it, or its declaration in a deconstruction.
        /// </summary>
        public IOperation? Declarator { get; set; }

        /// <summary>
        /// Gets or sets whether the local holds containers inside a container, once that is known. Such a local is held to a list of what
        /// is allowed, not of what is forbidden.
        /// </summary>
        public bool? HoldsContainers { get; set; }

        /// <summary>
        /// Gets or sets the assignments by which the local is left in a [MovedFrom] parameter of the method, which hands it to the caller.
        /// </summary>
        public List<IOperation>? GivenTo { get; set; }

        /// <summary>
        /// Gets or sets the arguments by which the local is passed to a [MovedFrom] parameter, which may leave another container in it.
        /// </summary>
        public List<IArgumentOperation>? MovedBy { get; set; }

        /// <summary>
        /// Gets or sets the places where an iterator yields the local.
        /// </summary>
        public List<IOperation>? Yields { get; set; }

        /// <summary>
        /// Gets or sets whether every value that the local is assigned is a container of its own, once that is known.
        /// </summary>
        public bool? IsOwned { get; set; }
    }
}
