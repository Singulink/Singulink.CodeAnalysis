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
/// The walker's check of [Kept]: a method that keeps a container parameter says so, and a parameter is followed wherever the code
/// of the method takes it.
/// </content>
public sealed partial class DoNotCopyValueAnalyzer
{
    /// <summary>
    /// What a use of a container parameter that is not marked [Kept] is, by the list of what is allowed.
    /// </summary>
    private enum ParameterUse
    {
        /// <summary>
        /// On the list: the method only uses the container during the call, or passes it on to something that does.
        /// </summary>
        Allowed,

        /// <summary>
        /// A store, a capture, or passing the container to what keeps it.
        /// </summary>
        Keeps,

        /// <summary>
        /// Neither: the use takes the container as a value in a way that is not on the list, so nothing says where it ends.
        /// </summary>
        NotFollowed,

        /// <summary>
        /// Passing the container to a parameter whose type mentions a type parameter that is not marked, of a listed member that
        /// the list does not say only borrows: nothing checks what the member does with it, so it is taken to keep it.
        /// </summary>
        KeptByUncheckedCode,
    }

    private sealed partial class NonCopyableWalker
    {
        // The locals that hold a container parameter which is not marked [Kept]: keeping one of them keeps the parameter.
        private Dictionary<ISymbol, IParameterSymbol>? _parameterAliases;
        private HashSet<IParameterSymbol>? _keptParametersReported;
        private Dictionary<IMethodSymbol, INamedTypeSymbol>? _delegatesOfFunctions;

        /// <summary>
        /// Gets a value indicating whether a parameter holds a container that its method is only given for the call: it is not marked
        /// [Kept], and its method is not a constructor or a setter, which are taken to keep what they are given. A [MovedFrom] parameter
        /// hands its container over for good, so the method may keep it. A lambda or an anonymous method may keep what the delegate that
        /// it is made as takes to keep at every call: see <see cref="IsKeptThroughItsDelegate"/>.
        /// </summary>
        private bool IsParameterThatMustNotBeKept(IParameterSymbol parameter) =>
            parameter.RefKind is not RefKind.Out &&
            !NonCopyableTypes.IsMovedFrom(parameter) &&
            parameter.Type.TypeKind is not TypeKind.Delegate &&
            types.IsContainer(parameter.Type) &&
            parameter.ContainingSymbol is IMethodSymbol or INamedTypeSymbol { IsExtension: true } &&
            !NonCopyableTypes.KeepsWhatItIsGiven(parameter) &&
            !IsKeptThroughItsDelegate(parameter);

        /// <summary>
        /// Gets a value indicating whether a parameter of a lambda or an anonymous method may keep what it is given: the function has no
        /// signature that anything calls but the delegate that it is made as, and every call through that delegate's parameter takes
        /// what it passes to be kept, by [Kept] or by a type parameter as its type. A lambda that is not made into a delegate where it
        /// stands, such as one of an expression tree, keeps nothing.
        /// </summary>
        /// <remarks>
        /// The delegate is the type of the delegate creation: the target type, or the natural type of a lambda that has none. A later
        /// conversion of that delegate to another delegate type is a delegate creation of its own, which the check of delegate creations
        /// judges by the same question.
        /// </remarks>
        private bool IsKeptThroughItsDelegate(IParameterSymbol parameter) =>
            parameter.ContainingSymbol is IMethodSymbol { MethodKind: MethodKind.AnonymousFunction } function &&
            GetDelegatesOfFunctions().TryGetValue(function, out var delegateType) &&
            delegateType.DelegateInvokeMethod is { } invoke &&
            parameter.Ordinal < invoke.Parameters.Length &&
            types.IsTakenToKeepAtEveryCall(invoke.Parameters[parameter.Ordinal]);

        // The delegate type that each lambda and anonymous method of the member is made as, made once.
        private Dictionary<IMethodSymbol, INamedTypeSymbol> GetDelegatesOfFunctions()
        {
            if (_delegatesOfFunctions is { } known)
                return known;

            var delegates = new Dictionary<IMethodSymbol, INamedTypeSymbol>(SymbolEqualityComparer.Default);

            foreach (var operation in GetAllOperations())
            {
                if (operation is IAnonymousFunctionOperation { Symbol: var function, Parent: IDelegateCreationOperation { Type: INamedTypeSymbol { TypeKind: TypeKind.Delegate } delegateType } })
                    delegates[function] = delegateType;
            }

            return _delegatesOfFunctions = delegates;
        }

        /// <summary>
        /// Gets a value indicating whether a parameter is one of the method, the lambda or the local function that a reference to it
        /// is in. If it is not, a lambda or a local function has captured it. The receiver of an extension block is a parameter of
        /// every member of the block.
        /// </summary>
        private static bool IsParameterOf(IParameterSymbol parameter, IMethodSymbol? function) =>
            SymbolEqualityComparer.Default.Equals(parameter.ContainingSymbol, function) ||
            (parameter.ContainingSymbol is INamedTypeSymbol { IsExtension: true } extension &&
                function is { MethodKind: not (MethodKind.AnonymousFunction or MethodKind.LocalFunction) } &&
                SymbolEqualityComparer.Default.Equals(function.ContainingType, extension));

        /// <summary>
        /// Gets the operation that takes the value which a reference to a container gives, through what only passes it on.
        /// </summary>
        private IOperation GetWhatIsTaken(IOperation reference)
        {
            var current = reference;

            while (GetNextThatPassesOn(current) is { } next)
                current = next;

            return current;
        }

        /// <summary>
        /// Gets the operation that gives the container on from what an operation gives it to, where that only passes it on. Null
        /// where what it is given to takes it.
        /// </summary>
        /// <remarks>
        /// The array that the compiler makes for a 'params' parameter is how the container is passed on. A tuple carries the
        /// container to wherever the tuple goes, and an element of a tuple that is a container is the container that went in. A
        /// delegate for a method of the container carries it as well. And an element of a parameter that holds containers, as a
        /// 'params' array of lists does, is a container that the caller put in: by an index, as an element of a tuple, or as
        /// what a member of a struct of the list hands out of it, as 'entry.Value' of a KeyValuePair does. Every kind that
        /// passes on is one of the list of <see cref="CarriedSymbols"/>, and what it carries is collected on the way.
        /// </remarks>
        private IOperation? GetNextThatPassesOn(IOperation current)
        {
            if (current.Parent is not { } parent || !CarriedSymbols.IsOnTheList(parent.Kind))
                return null;

            if (IsPassedOn(current) ||
                IsInParamsArray(current) ||
                (parent is IMethodReferenceOperation methodReference && ReferenceEquals(current, methodReference.Instance)) ||
                (current is IMethodReferenceOperation && parent is IDelegateCreationOperation) ||
                (parent is IArrayElementReferenceOperation ofArray && ReferenceEquals(current, ofArray.ArrayReference) && types.IsContainer(ofArray.Type)) ||
                (parent is IImplicitIndexerReferenceOperation fromEnd && ReferenceEquals(current, fromEnd.Instance) && types.IsContainer(fromEnd.Type)) ||
                (parent is IPropertyReferenceOperation { Property.IsIndexer: true } ofIndexer &&
                    ReferenceEquals(current, ofIndexer.Instance) &&
                    types.IsContainer(ofIndexer.Type)) ||
                (parent is IPropertyReferenceOperation { Property: { IsIndexer: false, ContainingType.IsValueType: true } ofStruct } elementOfStruct &&
                    ReferenceEquals(current, elementOfStruct.Instance) &&
                    types.IsContainer(elementOfStruct.Type) &&
                    types.IsElementTypeOf(elementOfStruct.Type, current.Type) &&
                    types.GetUse(ofStruct).Kind is MemberUseKind.Listed) ||
                (parent is IFieldReferenceOperation { Field.ContainingType.IsTupleType: true } element &&
                    ReferenceEquals(current, element.Instance) &&
                    types.IsContainer(element.Type)) ||
                (parent is IConditionalAccessOperation whole && ReferenceEquals(current, whole.WhenNotNull)))
            {
                return parent;
            }

            // A member that is called with '?.' is called on the container: the use is where the access stands for it.
            return parent is IConditionalAccessOperation access && ReferenceEquals(current, access.Operation) ? FindConditionalAccessInstance(access) : null;
        }

        /// <summary>
        /// Gets the target that a deconstruction assigns the container to which a reference gives, where the reference is an element
        /// of the tuple literal that is taken apart, as in '(_kept, count) = (list, 1)'. Null if the value is no literal, or the target
        /// cannot be told.
        /// </summary>
        private static IOperation? FindDeconstructionTarget(IOperation reference, IDeconstructionAssignmentOperation deconstruction)
        {
            // The way from the outermost tuple down to the element.
            var path = new Stack<int>();

            for (var current = reference; current.Parent is { } parent && !ReferenceEquals(parent, deconstruction); current = parent)
            {
                if (parent is ITupleOperation tuple)
                    path.Push(tuple.Elements.IndexOf(current));
            }

            if (path.Count is 0)
                return null;

            var target = deconstruction.Target;

            while (path.Count > 0)
            {
                if (target is IDeclarationExpressionOperation declaration)
                    target = declaration.Expression;

                int index = path.Pop();

                if (target is not ITupleOperation targets || index < 0 || index >= targets.Elements.Length)
                    return null;

                target = targets.Elements[index];
            }

            return target is IDeclarationExpressionOperation declared ? declared.Expression : target;
        }

        /// <summary>
        /// Gets a value indicating whether a deconstruction of a value that holds a container writes a container to anything but a
        /// variable of the method. It is asked where the value is no tuple literal, so that nothing says which target takes which.
        /// </summary>
        private bool KeepsByDeconstructingInto(IOperation target)
        {
            RuntimeHelpers.EnsureSufficientExecutionStack();

            return target switch {
                IDeclarationExpressionOperation declaration => KeepsByDeconstructingInto(declaration.Expression),
                ITupleOperation tuple => tuple.Elements.Any(KeepsByDeconstructingInto),
                _ => types.IsContainer(target.Type) && KeepsByAssigningTo(target),
            };
        }

        /// <summary>
        /// Gets the locals and the by-value parameters that hold a container parameter which is not marked [Kept], each with the
        /// parameter: what the parameter, or a variable that holds it already, is assigned to, declared with or matched into. They are
        /// found for the whole member before the first use is judged, so that a store which comes before the assignment in the source
        /// and runs after it, in a loop or after a 'goto', is a store of the parameter all the same.
        /// </summary>
        private Dictionary<ISymbol, IParameterSymbol> GetParameterAliases()
        {
            if (_parameterAliases is { } known)
                return known;

            var aliases = _parameterAliases = new(SymbolEqualityComparer.Default);
            List<IOperation>? references = null;

            foreach (var block in context.OperationBlocks)
            {
                if (!block.DescendantsAndSelf().Any(operation => operation is IParameterReferenceOperation { Parameter: var parameter } && IsParameterThatMustNotBeKept(parameter)))
                    continue;

                foreach (var operation in block.DescendantsAndSelf())
                {
                    if (operation is ILocalReferenceOperation or IParameterReferenceOperation)
                        (references ??= []).Add(operation);
                }
            }

            if (references is null)
                return aliases;

            // A variable that holds the parameter passes it on to the next, in whatever order the assignments stand.
            var taking = new List<ISymbol>();
            bool found;

            do
            {
                found = false;

                foreach (var reference in references)
                {
                    var source = reference switch {
                        IParameterReferenceOperation { Parameter: var parameter } when IsParameterThatMustNotBeKept(parameter) => parameter,
                        IParameterReferenceOperation { Parameter: var parameter } when aliases.TryGetValue(parameter, out var held) => held,
                        ILocalReferenceOperation { Local: var local } when aliases.TryGetValue(local, out var held) => held,
                        _ => null,
                    };

                    if (source is null)
                        continue;

                    taking.Clear();
                    FindVariablesThatTake(reference, taking);

                    foreach (var variable in taking)
                    {
                        if (SymbolEqualityComparer.Default.Equals(variable, source) || aliases.ContainsKey(variable))
                            continue;

                        aliases[variable] = source;
                        found = true;
                    }
                }
            }
            while (found);

            return aliases;
        }

        /// <summary>
        /// Gets the locals and the by-value parameters that then hold the container which a reference gives: the variable that it is
        /// assigned to or declared with, the one that is made to refer to it, and those that a deconstruction or a pattern declares
        /// for it.
        /// </summary>
        /// <remarks>
        /// Where a tuple that holds the container is taken apart and nothing says which variable takes which element, as with a tuple
        /// in a variable or with a pattern, every variable that could take it counts: each one whose type mentions a non-copyable type.
        /// </remarks>
        private void FindVariablesThatTake(IOperation reference, List<ISymbol> variables)
        {
            var current = GetWhatIsTaken(reference);

            // A variable that holds what is made of the container, where that is the container under another name, holds the
            // container: a Memory<T> local over an array parameter is followed like a local that is assigned the array.
            for (int step = 0; step < 16; step++)
            {
                FindVariablesThatTake(reference, current, variables);

                if (DescribeWhatIsMade(current, current.Type, out var made) is not MadeOfContainer.UnderAnotherName)
                    break;

                current = GetWhatIsTaken(made!);
            }
        }

        private void FindVariablesThatTake(IOperation reference, IOperation current, List<ISymbol> variables)
        {
            // What a listed member hands out of the container through an 'out' argument is a part of it, or it under another
            // name: 'MemoryMarshal.TryGetArray(memory, out var segment)', 'map.TryGetValue(key, out var inner)'.
            var call = current.Parent switch {
                IArgumentOperation { Parent: IInvocationOperation taking } => taking,
                IInvocationOperation onInstance when ReferenceEquals(current, onInstance.Instance) => onInstance,
                _ => null,
            };

            foreach (var argument in call?.Arguments ?? ImmutableArray<IArgumentOperation>.Empty)
            {
                var handedOut = argument.Value is IDeclarationExpressionOperation declaration ? declaration.Expression : argument.Value;

                if (argument.Parameter is { RefKind: RefKind.Out } &&
                    handedOut is ILocalReferenceOperation { Local: { RefKind: RefKind.None } local } &&
                    types.IsContainer(local.Type) &&
                    types.GetUse(call!.TargetMethod).Kind is MemberUseKind.Listed)
                {
                    variables.Add(local);
                }
            }

            switch (current.Parent)
            {
                // '(local, count) = (list, 1)' assigns each element to its target.
                case IDeconstructionAssignmentOperation deconstruction when ReferenceEquals(current, deconstruction.Value):
                    if (FindDeconstructionTarget(reference, deconstruction) is { } target)
                        AddVariable(target, isRef: false);
                    else
                        AddTargets(deconstruction.Target);

                    break;

                case ISimpleAssignmentOperation assignment when ReferenceEquals(current, assignment.Value):
                    AddVariable(assignment.Target, assignment.IsRef);
                    break;

                case ICoalesceAssignmentOperation assignment when ReferenceEquals(current, assignment.Value):
                    AddVariable(assignment.Target, isRef: false);
                    break;

                case IVariableInitializerOperation { Parent: IVariableDeclaratorOperation { Symbol: var declared } }:
                    variables.Add(declared);
                    break;

                case IVariableInitializerOperation { Parent: IVariableDeclarationOperation { Declarators: [{ Symbol: var declared }] } }:
                    variables.Add(declared);
                    break;

                case IIsPatternOperation test when ReferenceEquals(current, test.Value):
                    AddDeclaredBy(test.Pattern);
                    break;

                // The variable of a 'foreach' over a parameter that holds containers is given what the caller put in.
                case IForEachLoopOperation { LoopControlVariable: IVariableDeclaratorOperation { Symbol: var element } } loop
                    when ReferenceEquals(current, loop.Collection) && types.IsContainer(element.Type):
                    variables.Add(element);
                    break;

                // And so are the variables that a 'foreach' takes each element apart into.
                case IForEachLoopOperation { LoopControlVariable: not IVariableDeclaratorOperation and var deconstructed } loop when ReferenceEquals(current, loop.Collection):
                    AddTargets(deconstructed);
                    break;

                case ISwitchExpressionOperation switchExpression when ReferenceEquals(current, switchExpression.Value):
                    foreach (var arm in switchExpression.Arms)
                        AddDeclaredBy(arm.Pattern);

                    break;

                case ISwitchOperation switchStatement when ReferenceEquals(current, switchStatement.Value):
                    foreach (var section in switchStatement.Cases)
                    {
                        foreach (var clause in section.Clauses)
                        {
                            if (clause is IPatternCaseClauseOperation { Pattern: var pattern })
                                AddDeclaredBy(pattern);
                        }
                    }

                    break;
            }

            // A variable that is written through a reference is not what holds the container: see KeepsByAssigningTo.
            void AddVariable(IOperation target, bool isRef)
            {
                switch (SkipParentheses(target))
                {
                    case ILocalReferenceOperation { Local: var local } when isRef || local.RefKind is RefKind.None:
                        variables.Add(local);
                        break;

                    case IParameterReferenceOperation { Parameter: var parameter } when isRef || parameter.RefKind is RefKind.None:
                        variables.Add(parameter);
                        break;
                }
            }

            void AddTargets(IOperation target)
            {
                RuntimeHelpers.EnsureSufficientExecutionStack();

                switch (target)
                {
                    case IDeclarationExpressionOperation declaration:
                        AddTargets(declaration.Expression);
                        break;

                    case ITupleOperation tuple:
                        foreach (var element in tuple.Elements)
                            AddTargets(element);

                        break;

                    default:
                        if (types.Carries(target.Type))
                            AddVariable(target, isRef: false);

                        break;
                }
            }

            void AddDeclaredBy(IPatternOperation pattern)
            {
                foreach (var part in pattern.DescendantsAndSelf())
                {
                    var declared = GetDeclaredVariable(part);

                    if (declared is ILocalSymbol local && types.Carries(local.Type))
                        variables.Add(local);
                }
            }
        }

        /// <summary>
        /// Reports a use of a container parameter that is not marked [Kept], or of a variable that holds it, by which the method keeps
        /// the container, or which is not on the list of what is known to leave it with the caller.
        /// </summary>
        private void CheckKeptUse(IOperation reference, IParameterSymbol parameter)
        {
            var function = GetEnclosingMethod(reference);
            var use = JudgeUseOfParameter(reference, parameter, function, out var receiver);

            if (use is ParameterUse.Allowed)
            {
                // A function that goes on after it has returned to its caller keeps what it still uses then. An iterator runs its
                // whole body after the call, and an 'async' function everything that follows its first 'await'. What an 'async'
                // function does before that runs during the call, and keeps nothing.
                if (function is not null &&
                    DescribeUseAfterReturning(reference, function) is var (how, advice) &&
                    (_keptParametersReported ??= new(SymbolEqualityComparer.Default)).Add(parameter))
                {
                    var goingOn = parameter.ContainingSymbol is IMethodSymbol ? parameter.ContainingSymbol : function;
                    Report(Rules.KeepsParameterAfterReturning, reference.Syntax, Display(goingOn), how, parameter.Name, DescribeContainer(parameter.Type), advice);
                }

                return;
            }

            // One finding for a parameter says what there is to say: the mark is on the parameter.
            // The receiver of an extension block is reported for the member that keeps it.
            if ((_keptParametersReported ??= new(SymbolEqualityComparer.Default)).Add(parameter))
            {
                var owner = parameter.ContainingSymbol is IMethodSymbol ? parameter.ContainingSymbol : (ISymbol?)function ?? parameter.ContainingSymbol;

                // What is passed to code that nothing checks has a text of its own: [Kept] on the parameter is rarely the answer
                // for a member of the framework, and the list can say that the member only borrows.
                if (use is ParameterUse.KeptByUncheckedCode && receiver is not null)
                {
                    var member = NonCopyableTypes.GetListDefinition(receiver);

                    Report(
                        Rules.PassesParameterToUncheckedCode,
                        reference.Syntax,
                        Display(owner),
                        parameter.Name,
                        DescribeContainer(parameter.Type),
                        Display(receiver),
                        (SafeMemberList.GetExtensionImplementation(member) ?? member).GetDocumentationCommentId());

                    return;
                }

                var rule = use is ParameterUse.Keeps ? Rules.KeepsUnmarkedParameter : Rules.ParameterUsedInAWayNotFollowed;
                Report(rule, reference.Syntax, Display(owner), parameter.Name, DescribeContainer(parameter.Type));
            }
        }

        /// <summary>
        /// Decides for a use of a container parameter that is not marked [Kept], or of a variable that holds it, whether it is on
        /// the list of what is allowed, keeps the container, or is neither.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The check used to name the ways of keeping a parameter, and each way that was not named was allowed: a 'params' collection
        /// of a type parameter, an operator of a marked generic, the receiver of an extension block over a type parameter. So it is
        /// turned round: what takes the container as a value in a way that is not named here is a finding and not a gap.
        /// </para>
        /// <para>
        /// The list is the parameter's own, and not that of a local that the method owns. That one asks whether anything else can
        /// reach the container or put something stored into it. Keeping is the narrower question of whether something still holds
        /// the container when the method has returned. So a member that is used on it, a reference or a view of it that stays in
        /// the method, and a parameter of any declared type that does not keep are allowed here.
        /// </para>
        /// </remarks>
        private ParameterUse JudgeUseOfParameter(IOperation reference, IParameterSymbol parameter, IMethodSymbol? function, out ISymbol? receiver)
        {
            receiver = null;

            // Declaring the variable, or writing it as a whole, is no use of the container.
            if (IsWrittenAsAWhole(reference))
                return ParameterUse.Allowed;

            // A lambda or a local function that uses the variable of another function captures it.
            if (!IsParameterOf(parameter, function))
                return ParameterUse.Keeps;

            var from = reference;

            // What a use makes of the container that is the container under another name, a view that is no ref struct or a
            // reference to the variable that a callee hands back, is judged in turn for where it goes: stored, it is the container
            // that is stored.
            for (int step = 0; ; step++)
            {
                var current = GetWhatIsTaken(from);
                var use = JudgeWhereItGoes(from, current, out receiver);

                if (use is not ParameterUse.Allowed || step >= 16 || DescribeWhatIsMade(current, current.Type, out var made) is not MadeOfContainer.UnderAnotherName)
                    return use;

                from = made!;
            }
        }

        /// <summary>
        /// Decides for the place that a container, or what is made of it, goes to whether that is on the list of what is allowed,
        /// keeps the container, or is neither.
        /// </summary>
        /// <param name="from">The reference to the parameter or to a variable that holds it, or the operation that gives what is
        /// made of it.</param>
        /// <param name="current">The operation that gives it to what takes it, through what only passes it on.</param>
        /// <param name="keptBy">The member that receives it, where that is code that nothing checks.</param>
        private ParameterUse JudgeWhereItGoes(IOperation from, IOperation current, out ISymbol? keptBy)
        {
            keptBy = null;

            // Every member that the operations carry which the container goes through and into. One that takes the container as a
            // parameter, as an operator, a conversion and an extension 'GetAwaiter' or 'Deconstruct' do, is other code that the
            // container is passed to.
            var carried = new List<(ISymbol Member, IParameterSymbol? Receiving, IOperation Operand)>();
            CollectCarriedMembers(from, carried);

            foreach (var (member, taking, operand) in carried)
            {
                if (taking is not null && JudgePassing(operand, taking, member, null, out keptBy) is var passing and not ParameterUse.Allowed)
                    return passing;
            }

            var use = JudgeWhatTakesIt(from, current, ref keptBy);

            // A kind of operation allows a use only if it is on the list of what a container may be used in, which names what
            // each kind carries. Code that does not compile is the compiler's to report.
            return use is ParameterUse.Allowed && current.Parent is { Kind: not (OperationKind.Invalid or OperationKind.None) and var kind } && !CarriedSymbols.IsOnTheList(kind)
                ? ParameterUse.NotFollowed
                : use;
        }

        private ParameterUse JudgeWhatTakesIt(IOperation reference, IOperation current, ref ISymbol? keptBy)
        {
            bool isDelegate = current.Type is { TypeKind: TypeKind.Delegate };

            switch (current.Parent)
            {
                // A store. A local or a by-value parameter of the method is the container under another name, and is followed:
                // see GetParameterAliases. Making a 'ref' local refer to the variable stores nothing.
                case ISimpleAssignmentOperation assignment when ReferenceEquals(current, assignment.Value):
                    return !assignment.IsRef && KeepsByAssigningTo(assignment.Target) ? ParameterUse.Keeps : ParameterUse.Allowed;

                case ICoalesceAssignmentOperation assignment when ReferenceEquals(current, assignment.Value):
                    return KeepsByAssigningTo(assignment.Target) ? ParameterUse.Keeps : ParameterUse.Allowed;

                case IDeconstructionAssignmentOperation deconstruction when ReferenceEquals(current, deconstruction.Value):
                    bool keepsByDeconstruction = FindDeconstructionTarget(reference, deconstruction) is { } target
                        ? KeepsByAssigningTo(target)
                        : KeepsByDeconstructingInto(deconstruction.Target);

                    return keepsByDeconstruction ? ParameterUse.Keeps : ParameterUse.Allowed;

                case IVariableInitializerOperation { Parent: IVariableDeclaratorOperation or IVariableDeclarationOperation }:
                    return ParameterUse.Allowed;

                // An element of an array or of a collection that is made is kept by it, and so is every element of a parameter
                // that holds containers which a spread hands to the collection, as a 'foreach' that adds them would.
                case IArrayInitializerOperation or ICollectionExpressionOperation:
                    return ParameterUse.Keeps;

                case ISpreadOperation spread when ReferenceEquals(current, spread.Operand) && types.IsContainer(spread.ElementType):
                    return ParameterUse.Keeps;

                // What is written to: an element of it, a field or a property of it, or the variable itself with '??=', '+=' or
                // '++', which is a member used on it or a write of the variable. The operator of a compound assignment or of an
                // increment has been judged with what the operation carries, and so has that of the right side of a compound
                // assignment.
                case IAssignmentOperation written when ReferenceEquals(current, written.Target):
                case IIncrementOrDecrementOperation:
                case ICompoundAssignmentOperation { OperatorMethod: not null }:
                    return ParameterUse.Allowed;

                // Passing it on, in every form. The type of a function pointer has no place for a mark, so nothing says what the
                // method behind it does, and nothing says what a call does with its argument where the method is not known:
                // '__arglist' and a dynamic call.
                case IArgumentOperation { Parent: IFunctionPointerInvocationOperation }:
                case IArgumentOperation { Parameter: null }:
                case IDynamicInvocationOperation or IDynamicObjectCreationOperation or IDynamicIndexerAccessOperation:
                    return ParameterUse.Keeps;

                // A hole of an interpolated string that a handler builds is formatted, not kept: a handler is taken to format what
                // its 'AppendFormatted<T>(T)' is given, and still keeps what a parameter is marked to keep. A delegate that holds
                // the container is kept by whatever it is passed to, as a lambda that captures the container is.
                // Anywhere else what receives it says whether it keeps: see KeepsWhatItReceives, which the check of a local reads
                // as well. This is said here and not of the receiving parameter itself, which would let an override add [Kept]
                // unseen. A parameter of any other declared type that is not marked only uses what it is given, by value or by
                // reference.
                case IArgumentOperation { Parameter: { } receiving } argument:
                    if (isDelegate)
                        return ParameterUse.Keeps;

                    // A handler of checked code whose 'AppendFormatted<[NonCopyable] T>(T)' takes a marked type parameter says by the
                    // mark that it moves what it is given, which with a container bound is keeping it, as for every other
                    // parameter of that kind. The handlers of the framework, whose type parameter is not marked, format.
                    if (argument.Parent?.Parent is IInterpolatedStringAppendOperation)
                    {
                        return NonCopyableTypes.IsMarkedKept(receiving) ||
                            (receiving.OriginalDefinition.Type is ITypeParameterSymbol formatted && NonCopyableTypes.IsMarkedNonCopyable(formatted))
                            ? ParameterUse.Keeps
                            : ParameterUse.Allowed;
                    }

                    // The parameter of an indexer is one of its accessors: of the setter where the indexer is assigned, which keeps
                    // like every setter, and of the getter where it is read.
                    if (argument.Parent is IPropertyReferenceOperation { Property: var indexer } indexed)
                    {
                        bool isAssigned = indexed.Parent is IAssignmentOperation assigned && ReferenceEquals(assigned.Target, indexed);
                        bool isRead = !isAssigned || indexed.Parent is not ISimpleAssignmentOperation;

                        if (indexed.Parent is IIncrementOrDecrementOperation)
                            isAssigned = true;

                        if (isAssigned && indexer.SetMethod is { } setter && receiving.Ordinal < setter.Parameters.Length &&
                            JudgePassing(current, setter.Parameters[receiving.Ordinal], setter, argument, out keptBy) is var set and not ParameterUse.Allowed)
                        {
                            return set;
                        }

                        return isRead && indexer.GetMethod is { } getter && receiving.Ordinal < getter.Parameters.Length
                            ? JudgePassing(current, getter.Parameters[receiving.Ordinal], getter, argument, out keptBy)
                            : ParameterUse.Allowed;
                    }

                    return JudgePassing(current, receiving, null, argument, out keptBy);

                // A user-defined conversion or operator is a call of its method, which has been judged with what the operation
                // carries, and so has the conversion that a '??' applies to its left side.
                case IConversionOperation { OperatorMethod: not null }:
                case IUnaryOperation { OperatorMethod: not null }:
                case IBinaryOperation { OperatorMethod: not null }:
                case ICoalesceOperation:
                    return ParameterUse.Allowed;

                // A delegate that holds the container hands it to whoever gets the delegate: what it is returned to, and the event
                // that it is added to. Returned as it is, the container goes back to the caller, which is judged where a container
                // is returned.
                case IReturnOperation:
                    return isDelegate ? ParameterUse.Keeps : ParameterUse.Allowed;

                case IEventAssignmentOperation:
                    return ParameterUse.Keeps;

                // A member that is used on it. One of the container's own takes the container as 'this', and has no parameter for
                // it: what the member takes in or hands out is not the parameter's business. One of an extension block takes it
                // as its receiver, which has been judged with what the operation carries. A reference or a view of it that the
                // member hands out stays in the method, or is stored, which is reported where a view of something stored is.
                // 'with' on a record is a call of its copy constructor on it, and the copy is followed as the container under
                // another name. A member that is called with '?.' is judged where the call stands: see GetNextThatPassesOn.
                case IInvocationOperation or IMemberReferenceOperation or IArrayElementReferenceOperation or IImplicitIndexerReferenceOperation or
                    IInlineArrayAccessOperation or IWithOperation:
                    return ParameterUse.Allowed;

                // What binds its members with nothing written for them: a pattern, whose variables are followed, a 'foreach', an
                // 'await' and a spread. Every member that one of these binds has been collected, and one that takes the container
                // as a parameter has been judged. What is left runs members of the container's own.
                case IIsPatternOperation or ISwitchExpressionOperation or ISwitchOperation or IPatternCaseClauseOperation:
                case IForEachLoopOperation or IAwaitOperation or ISpreadOperation:
                    return ParameterUse.Allowed;

                // And what binds no member that could take the container: a test for a type; a comparison of the language; 'lock',
                // which gives the object to the monitor of the runtime; 'using', whose Dispose is a member of the type or of
                // IDisposable and never an extension; 'nameof'; a hole of a string that the compiler builds, which calls ToString
                // on the object; and a statement.
                case IIsTypeOperation:
                case IBinaryOperation { OperatorMethod: null }:
                case IUsingOperation or ILockOperation or INameOfOperation:
                case IInterpolationOperation or IExpressionStatementOperation:
                    return ParameterUse.Allowed;

                // Code that does not compile is the compiler's to report, also while it is being typed.
                case IInvalidOperation or { Kind: OperationKind.None, Parent: IInvalidOperation }:
                    return ParameterUse.Allowed;

                // Everything else that takes it as a value: a form that nobody named.
                default:
                    return ParameterUse.NotFollowed;
            }
        }

        /// <summary>
        /// Decides what passing a container to a parameter is, as an argument, as an operand or as the receiver of an extension
        /// block. A use of a member that is not listed is reported as that, and is not reported again as keeping.
        /// </summary>
        /// <param name="current">The operation that gives the container.</param>
        /// <param name="receiving">The parameter that receives it.</param>
        /// <param name="member">The member that it is passed to, where the operation does not say it: a member that the compiler binds.</param>
        /// <param name="argument">The argument that passes it, if it is passed as one.</param>
        /// <param name="keptBy">The member that receives it, where that is code that nothing checks.</param>
        private ParameterUse JudgePassing(IOperation current, IParameterSymbol receiving, ISymbol? member, IArgumentOperation? argument, out ISymbol? keptBy)
        {
            // The receiver of a member of an extension block is a parameter of the block. What it is passed to is the member.
            member ??= receiving.ContainingSymbol is IMethodSymbol
                ? receiving.ContainingSymbol
                : current.Parent switch {
                    IInvocationOperation invocation => invocation.TargetMethod,
                    IPropertyReferenceOperation property => property.Property,
                    _ => receiving.ContainingSymbol,
                };

            var made = DescribeWhatIsMade(current, current.Type, out _, member);

            if (made is MadeOfContainer.NotFollowed)
            {
                keptBy = null;
                return ParameterUse.NotFollowed;
            }

            var judged = JudgeWhatReceives(receiving, member, argument, made);
            keptBy = judged is ReceivedContainer.KeptByUncheckedCode ? member : null;

            return judged switch {
                ReceivedContainer.Kept => ParameterUse.Keeps,
                ReceivedContainer.KeptByUncheckedCode => ParameterUse.KeptByUncheckedCode,
                _ => ParameterUse.Allowed,
            };
        }

        /// <summary>
        /// Says how a reference to a container parameter, or to a variable that holds it, uses the container after the function has
        /// returned to its caller, with what the advice of the message adds for it. Null if it does not: the function is neither an iterator nor
        /// 'async', the reference only assigns the variable, or it runs before the first 'await'.
        /// </summary>
        private (string How, string Advice)? DescribeUseAfterReturning(IOperation reference, IMethodSymbol function)
        {
            if (!function.IsIterator && !function.IsAsync)
                return null;

            if (GetReferencedVariable(reference) is not { } variable ||
                (reference.Parent is ISimpleAssignmentOperation { IsRef: false } assignment && ReferenceEquals(assignment.Target, reference)) ||
                reference is ILocalReferenceOperation { IsDeclaration: true })
            {
                return null;
            }

            if (function.IsIterator)
                return ("is an iterator, which runs after the call that creates it has returned, and uses", string.Empty);

            return (_flow ??= new(context, types)).IsAfterAwait(reference, variable, function)
                ? ("goes on after an 'await', when it has returned to its caller, and uses", ", or use the container only before the first 'await'")
                : null;
        }

        /// <summary>
        /// Gets a value indicating whether an argument is the collection that the compiler makes for a 'params' parameter whose
        /// declared element type is a type parameter: 'params T[]', 'params Span&lt;T&gt;' and the like.
        /// </summary>
        private static bool IsParamsCollectionOfTypeParameter(IArgumentOperation argument, IParameterSymbol receiving) =>
            receiving.IsParams &&
            argument.ArgumentKind is ArgumentKind.ParamArray or ArgumentKind.ParamCollection &&
            receiving.OriginalDefinition.Type is IArrayTypeSymbol { ElementType: ITypeParameterSymbol } or INamedTypeSymbol { TypeArguments: [ITypeParameterSymbol] };

        /// <summary>
        /// Gets a value indicating whether a reference to a variable declares it or writes it as a whole, which uses nothing of
        /// what it held: the target of an assignment or of a deconstruction, and an 'out' argument, also behind a reference that
        /// a condition chooses.
        /// </summary>
        private static bool IsWrittenAsAWhole(IOperation reference)
        {
            if (reference is ILocalReferenceOperation { IsDeclaration: true })
                return true;

            IOperation current = SkipChoicesUpward(reference);

            while (current.Parent is IDeclarationExpressionOperation or IParenthesizedOperation || (current.Parent is ITupleOperation tuple && FunctionGraph.IsDeconstructionTarget(tuple)))
                current = current.Parent;

            return current.Parent switch {
                ISimpleAssignmentOperation assignment => ReferenceEquals(current, assignment.Target),
                IDeconstructionAssignmentOperation deconstruction => ReferenceEquals(current, deconstruction.Target),
                IArgumentOperation { Parameter.RefKind: RefKind.Out } => true,
                _ => false,
            };
        }

        private bool IsMovedByPureMove(IArgumentOperation argument) =>
            argument is { Parameter: { } moved, Parent: IInvocationOperation call } &&
            NonCopyableTypes.IsMovedFrom(moved.OriginalDefinition) &&
            IsPureMoveCall(call.TargetMethod, call.Instance, call.Arguments);

        /// <summary>
        /// Gets a value indicating whether assigning a container to a target keeps it. A local or a by-value parameter holds it for
        /// the call only, and is followed as the parameter is: see <see cref="GetParameterAliases"/>. Anything else keeps it: a field,
        /// an element or a property, and whatever is written through a reference, which can refer to any of those. That is a 'ref'
        /// local, a 'ref' or 'out' parameter, what a call returns by reference, and what a pointer points to.
        /// </summary>
        private static bool KeepsByAssigningTo(IOperation target) => SkipParentheses(target) is not (
            ILocalReferenceOperation { Local.RefKind: RefKind.None } or
            IParameterReferenceOperation { Parameter.RefKind: RefKind.None } or
            IDiscardOperation);
    }
}
