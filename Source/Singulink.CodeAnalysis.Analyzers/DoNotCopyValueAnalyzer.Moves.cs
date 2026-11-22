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
/// The walker's checks of moves: [MovedFrom] parameters and what a method leaves in them, generic moves and pure moves, and a
/// container that is moved out of something which the method does not own.
/// </content>
public sealed partial class DoNotCopyValueAnalyzer
{
    private sealed partial class NonCopyableWalker
    {
        // The locals of a generic move whose type mentions a marked type parameter, each with the values that it is assigned, or with
        // null if it is written in a way that is not followed. Found when first asked for.
        private Dictionary<ILocalSymbol, List<IOperation>?>? _moveLocals;
        private HashSet<ILocalSymbol>? _moveLocalsInProgress;

        /// <summary>
        /// Checks [MovedFrom] where a function is converted to a delegate. The mark is kept in both directions: a function that adds it
        /// is passed anything by the callers of the delegate, and one that drops it is not checked for what it leaves behind. And a
        /// function that takes a [MovedFrom] container for its own is not passed a slot of generic code: for a lambda that is judged in
        /// its body, for a method of this compilation when the whole compilation has been seen, and for anything else here.
        /// </summary>
        private void CheckMovedFromOfDelegate(
            IDelegateCreationOperation operation, IMethodSymbol function, ImmutableArray<IParameterSymbol> parameters, IMethodSymbol invoke, bool isLambda)
        {
            if (parameters.Length != invoke.Parameters.Length || operation.Type is not INamedTypeSymbol delegateType)
                return;

            string giver = $"delegate '{Display(delegateType.OriginalDefinition)}'";

            for (int i = 0; i < parameters.Length; i++)
            {
                var parameter = parameters[i];
                bool isMarked = NonCopyableTypes.IsMovedFrom(parameter);

                if (NonCopyableTypes.ChangesMovedFrom(parameter, invoke.Parameters[i]) && operation.Target.Syntax is AnonymousMethodExpressionSyntax { ParameterList: null })
                {
                    // 'delegate { }' has no parameter that could be named or marked.
                    Report(Rules.MovedFromDroppedWithNoParameterList, operation.Target.Syntax, giver, invoke.Parameters[i].Name);
                }
                else if (NonCopyableTypes.ChangesMovedFrom(parameter, invoke.Parameters[i]))
                {
                    var rule = !isMarked
                        ? Rules.MovedFromDropped
                        : NonCopyableTypes.HasUncountedMovedFrom(invoke.Parameters[i])
                            ? Rules.MovedFromAddedOverUncheckedMark
                            : Rules.MovedFromAdded;

                    Report(
                        rule,
                        operation.Target.Syntax,
                        parameter.Name,
                        Display(function),
                        $"{giver}, which it is converted to,");
                }
                else if (isMarked && !isLambda && types.IsContainer(parameter.Type) && !types.IsContainer(invoke.OriginalDefinition.Parameters[i].Type))
                {
                    // Only the body of a method that nothing can replace says what the method does with the parameter.
                    bool isJudgedInItsBody = function is {
                        MethodKind: MethodKind.Ordinary or MethodKind.LocalFunction,
                        IsAbstract: false,
                        IsVirtual: false,
                        IsOverride: false,
                        DeclaringSyntaxReferences.IsEmpty: false,
                    };

                    if (isJudgedInItsBody)
                        moves.NotePassedSlot(function, $"{giver}, which the method is converted to,");
                    else
                        Report(Rules.SlotGivenToOwner, operation.Target.Syntax, Display(function), parameter.Name, giver);
                }
            }
        }

        /// <summary>
        /// Gets a value indicating whether a target of an assignment is a [MovedFrom] parameter of a container type.
        /// </summary>
        private bool IsMovedFromContainerParameter(IOperation target) =>
            SkipParentheses(target) is IParameterReferenceOperation { Parameter: var parameter } && NonCopyableTypes.IsMovedFrom(parameter) && types.IsContainer(parameter.Type);

        /// <summary>
        /// Reports a container that a method leaves in a [MovedFrom] parameter while something else still holds it. The caller's variable
        /// counts as its own afterwards, so the method leaves only null, a new value or a local of its own there.
        /// </summary>
        private void CheckLeftInMovedFromParameter(IOperation target, IOperation? value)
        {
            RuntimeHelpers.EnsureSufficientExecutionStack();
            target = SkipParentheses(target);

            // A reference that a condition chooses is a target like each of its branches.
            if (target is IConditionalOperation { IsRef: true } chosen)
            {
                CheckLeftInMovedFromParameter(chosen.WhenTrue, value);

                if (chosen.WhenFalse is { } otherwise)
                    CheckLeftInMovedFromParameter(otherwise, value);

                return;
            }

            if (target is not IParameterReferenceOperation { Parameter: var parameter } || !NonCopyableTypes.IsMovedFrom(parameter))
                return;

            if (types.IsContainer(parameter.Type))
            {
                if (value is null || !IsContainerOfItsOwn(value, given: true))
                    Report(Rules.MovedFromContainerNotOwned, (value ?? target).Syntax, parameter.Name);
            }
            else if (IsParameterOfGenericMove(parameter) && (value is null || !IsPureMoveValue(value)))
            {
                // With a container bound to the type parameter, the caller's local still counts as its own afterwards.
                Report(Rules.GenericMoveLeavesSomethingElse, (value ?? target).Syntax, Display(parameter.ContainingSymbol), parameter.Type.Name, parameter.Name);
            }
        }

        /// <summary>
        /// Gets a value indicating whether a parameter is a [MovedFrom] parameter of a generic move whose type is a marked type parameter.
        /// </summary>
        private static bool IsParameterOfGenericMove(IParameterSymbol parameter) =>
            parameter.Type is ITypeParameterSymbol typeParameter &&
            NonCopyableTypes.IsMarkedNonCopyable(typeParameter) &&
            parameter.ContainingSymbol is IMethodSymbol method &&
            NonCopyableTypes.IsGenericPureMove(method.OriginalDefinition);

        /// <summary>
        /// Reports a generic move that returns, through a type which mentions a marked type parameter, something other than what it
        /// read from its [MovedFrom] parameters. Its callers take a container that it hands out to be the one that they passed. A method
        /// that returns something else on purpose marks its return value [Borrowed].
        /// </summary>
        private void CheckReturnedByGenericMove(IReturnOperation operation, IOperation returnedValue)
        {
            if (operation.Kind is not OperationKind.Return ||
                GetEnclosingMethod(operation) is not { RefKind: RefKind.None } method ||
                !NonCopyableTypes.IsGenericPureMove(method.OriginalDefinition) ||
                NonCopyableTypes.HasBorrowedReturn(method) ||
                !NonCopyableTypes.MentionsMarkedTypeParameter(method.ReturnType, out var typeParameter))
            {
                return;
            }

            if (!IsPureMoveValue(returnedValue))
                Report(Rules.GenericMoveReturnsSomethingElse, returnedValue.Syntax, Display(method), typeParameter.Name);
        }

        /// <summary>
        /// Gets a value indicating whether a value in a generic move is 'default' or what the method read from one of its [MovedFrom]
        /// parameters: the read itself, a local that is assigned nothing else, or the result of another pure move of one of those. A
        /// conditional, a '??', a 'switch' expression and a tuple are looked through.
        /// </summary>
        private bool IsPureMoveValue(IOperation value)
        {
            RuntimeHelpers.EnsureSufficientExecutionStack();

            return SkipParentheses(value) switch {
                IDefaultValueOperation => true,
                ILiteralOperation { ConstantValue: { HasValue: true, Value: null } } => true,
                IConversionOperation { OperatorMethod: null } conversion => IsPureMoveValue(conversion.Operand),
                var choice when GetBranches(choice) is { } branches => branches.All(IsPureMoveValue),
                ITupleOperation tuple => tuple.Elements.All(element => !NonCopyableTypes.MentionsMarkedTypeParameter(element.Type, out _) || IsPureMoveValue(element)),
                IParameterReferenceOperation { Parameter: var parameter } => NonCopyableTypes.IsMovedFrom(parameter),
                ILocalReferenceOperation { Local: var local } => IsPureMoveLocal(local),
                IInvocationOperation invocation =>
                    IsPureMoveCall(invocation.TargetMethod, invocation.Instance, invocation.Arguments) &&
                    invocation.Arguments.All(argument => argument.Parameter is { } taking && (!NonCopyableTypes.IsMovedFrom(taking.OriginalDefinition) || IsMovedInGenericMove(argument.Value))),
                _ => false,
            };
        }

        private bool IsMovedInGenericMove(IOperation value) => SkipParentheses(value) switch {
            IParameterReferenceOperation { Parameter: var parameter } => NonCopyableTypes.IsMovedFrom(parameter),
            ILocalReferenceOperation { Local: var local } => IsPureMoveLocal(local),
            _ => false,
        };

        /// <summary>
        /// Gets a value indicating whether a local of a generic move holds nothing but what the method read from its [MovedFrom]
        /// parameters: every value that it is assigned is one, and it is written in no other way.
        /// </summary>
        private bool IsPureMoveLocal(ILocalSymbol local)
        {
            // A local with no value that is followed has its value from something else: a pattern, an 'out' argument, a 'foreach'.
            if (!GetMoveLocals().TryGetValue(local, out var values) || values is not { Count: > 0 })
                return false;

            // Two locals that are assigned each other have nowhere to get a value from.
            if (!(_moveLocalsInProgress ??= new(SymbolEqualityComparer.Default)).Add(local))
                return false;

            bool isPure = values.All(IsPureMoveValue);
            _moveLocalsInProgress.Remove(local);
            return isPure;
        }

        private Dictionary<ILocalSymbol, List<IOperation>?> GetMoveLocals()
        {
            if (_moveLocals is not null)
                return _moveLocals;

            var locals = new Dictionary<ILocalSymbol, List<IOperation>?>(SymbolEqualityComparer.Default);

            foreach (var operation in GetAllOperations())
            {
                switch (operation)
                {
                    case IVariableDeclaratorOperation { Symbol: var declared } declarator when NonCopyableTypes.MentionsMarkedTypeParameter(declared.Type, out _):
                        if (declarator.Parent is not IVariableDeclarationOperation declaration || declared.RefKind is not RefKind.None)
                            locals[declared] = null;
                        else if ((declarator.Initializer ?? declaration.Initializer)?.Value is { } initialValue)
                            Add(declared, initialValue);
                        else if (!locals.ContainsKey(declared))
                            locals[declared] = [];

                        break;

                    case ILocalReferenceOperation { Local: var referenced } reference when NonCopyableTypes.MentionsMarkedTypeParameter(referenced.Type, out _):
                        NoteMoveLocalReference(reference);
                        break;

                    // A variable that a pattern declares is given the value that the pattern matched, which is not followed.
                    case IPatternOperation pattern when GetDeclaredVariable(pattern) is ILocalSymbol matched:
                        locals[matched] = null;
                        break;
                }
            }

            return _moveLocals = locals;

            void Add(ILocalSymbol local, IOperation value)
            {
                if (!locals.TryGetValue(local, out var values))
                    locals[local] = [value];
                else
                    values?.Add(value);
            }

            void NoteMoveLocalReference(ILocalReferenceOperation reference)
            {
                var local = reference.Local;

                // A variable that a lambda or a local function captures is a field in fact.
                if (IsCapturedAt(reference, local))
                {
                    locals[local] = null;
                    return;
                }

                // The deconstruction of a tuple literal assigns each element to its variable.
                if (FindDeconstructedElement(reference) is { } element)
                {
                    Add(local, element);
                    return;
                }

                // Any other variable that an 'out' argument, a deconstruction or a pattern declares is given its value by something else.
                if (reference.IsDeclaration)
                {
                    locals[local] = null;
                    return;
                }

                IOperation current = reference;

                while (current.Parent is IParenthesizedOperation or IConversionOperation { OperatorMethod: null })
                    current = current.Parent;

                // A use that reads the local, or assigns it as a whole, leaves it what it is. Anything else may write to it or to a part
                // of it.
                bool isFollowed = current.Parent switch {
                    ISimpleAssignmentOperation { IsRef: false } assignment => !ReferenceEquals(current, assignment.Target) || ReferenceEquals(current, reference),
                    IVariableInitializerOperation { Parent: IVariableDeclaratorOperation { Symbol.RefKind: RefKind.None } } => true,
                    IReturnOperation returned => GetReturnRefKind(returned) is RefKind.None,
                    ITupleOperation tuple => !IsDeconstructionTarget(tuple),
                    IConditionalOperation or ICoalesceOperation or ISwitchExpressionArmOperation or IIsNullOperation or IIsPatternOperation => true,
                    IBinaryOperation { OperatorMethod: null } => true,
                    IArgumentOperation { Parameter: { RefKind: RefKind.None or RefKind.In or RefKind.RefReadOnlyParameter } } => true,
                    IArgumentOperation { Parameter: { } taking, Parent: IInvocationOperation call } =>
                        NonCopyableTypes.IsMovedFrom(taking.OriginalDefinition) && IsPureMoveCall(call.TargetMethod, call.Instance, call.Arguments),
                    _ => false,
                };

                if (!isFollowed)
                    locals[local] = null;
                else if (current.Parent is ISimpleAssignmentOperation written && ReferenceEquals(current, written.Target))
                    Add(local, written.Value);
                else if (!locals.ContainsKey(local))
                    locals[local] = [];
            }

            static bool IsDeconstructionTarget(IOperation tuple)
            {
                while (tuple.Parent is ITupleOperation or IParenthesizedOperation or IDeclarationExpressionOperation)
                    tuple = tuple.Parent;

                return tuple.Parent is IDeconstructionAssignmentOperation deconstruction && ReferenceEquals(tuple, deconstruction.Target);
            }
        }

        /// <summary>
        /// Gets a value indicating whether a container that is passed to a [MovedFrom] parameter is one that the method owns: a local of
        /// its own, or a [MovedFrom] parameter of the method, whose caller had to own what it passed. A value of a non-copyable type has
        /// one holder by construction.
        /// </summary>
        /// <param name="value">The value that is passed.</param>
        /// <param name="at">The argument that passes it. What that call leaves in the local says nothing about what went in.</param>
        private bool IsOwnedForMove(IOperation value, IArgumentOperation at) => !types.IsContainer(value.Type) || SkipParentheses(value) switch {
            ILocalReferenceOperation { Local: var moved } => IsLocalOfItsOwn(moved, movedAt: at),
            IParameterReferenceOperation { Parameter: var passedOn } reference => IsOwnedMovedFromParameter(passedOn, reference),
            _ => false,
        };

        /// <summary>
        /// Gets a value indicating whether a parameter is a [MovedFrom] parameter whose value the method may take for its own. A value of
        /// a non-copyable type has one holder by construction. A container is the method's own only where its caller could see the
        /// container, and so had to own what it passed.
        /// </summary>
        private bool IsOwnedMovedFromParameter(IParameterSymbol parameter, IOperation at) =>
            NonCopyableTypes.IsMovedFrom(parameter) && (!types.IsContainer(parameter.Type) || FindSlotGiver(parameter, at) is null);

        /// <summary>
        /// Gets what gives a [MovedFrom] parameter of a container type the type of a type parameter in the eyes of its caller, in words
        /// for a message: a member that the method overrides or implements, or the delegate type that a lambda is converted to. The
        /// caller is then generic code, which passes a slot of its own without knowing that it holds a container. Null if there is
        /// nothing of the kind. A method that is used as a method group is judged when the whole compilation has been seen.
        /// </summary>
        private string? FindSlotGiver(IParameterSymbol parameter, IOperation at)
        {
            if (parameter.ContainingSymbol is not IMethodSymbol method)
                return null;

            int ordinal = parameter.Ordinal;

            if (method.MethodKind is MethodKind.AnonymousFunction)
            {
                const string Unknown = "the delegate that the lambda is converted to";

                for (var current = at; current is not null; current = current.Parent)
                {
                    if (current is not IAnonymousFunctionOperation lambda || !SymbolEqualityComparer.Default.Equals(lambda.Symbol, method))
                        continue;

                    // A lambda with no delegate around it, or with more parameters than its delegate, is not valid code. The analyzer
                    // also runs in the editor on code that is being typed.
                    if (lambda.Parent is not IDelegateCreationOperation { Type: INamedTypeSymbol { DelegateInvokeMethod: { } invoke } delegateType } ||
                        ordinal >= invoke.OriginalDefinition.Parameters.Length)
                    {
                        return Unknown;
                    }

                    return types.IsContainer(invoke.OriginalDefinition.Parameters[ordinal].Type)
                        ? null
                        : $"delegate '{Display(delegateType.OriginalDefinition)}', which the lambda is converted to,";
                }

                return Unknown;
            }

            foreach (var stoodFor in NonCopyableTypes.GetMembersStoodFor(method))
            {
                var definition = stoodFor.OriginalDefinition;

                if (ordinal < definition.Parameters.Length && !types.IsContainer(definition.Parameters[ordinal].Type))
                    return $"'{Display(definition)}', which the method overrides or implements,";
            }

            return null;
        }

        /// <summary>
        /// Notes that the method takes the container in a [MovedFrom] parameter for its own, for the case that a method group passes
        /// the method to generic code, which is known only when the whole compilation has been seen.
        /// </summary>
        private void NoteOwnedParameter(IOperation value)
        {
            if (SkipParentheses(value) is IParameterReferenceOperation { Parameter: { ContainingSymbol: IMethodSymbol { MethodKind: not MethodKind.AnonymousFunction } method } parameter } &&
                NonCopyableTypes.IsMovedFrom(parameter) &&
                types.IsContainer(parameter.Type))
            {
                moves.NoteOwned(method, value.Syntax.GetLocation(), parameter.Type, parameter);
            }
        }

        /// <summary>
        /// Gets a value indicating whether an argument goes to a member on the safe list whose other arguments bring no container that
        /// something else holds, as in 'Interlocked.Exchange(ref task, null)'. A listed member writes to a reference only what it is
        /// given.
        /// </summary>
        private bool IsListedCallWithNothingStored(IArgumentOperation argument) =>
            argument.Parent is IInvocationOperation invocation &&
            types.IsListed(invocation.TargetMethod) &&
            invocation.Arguments.All(other => ReferenceEquals(other, argument) || !CarriesNonCopyableType(other.Value) || IsContainerOfItsOwn(other.Value, given: true));

        /// <summary>
        /// Gets a value indicating whether a call is a pure move: the method is a generic move whose return value is not marked
        /// [Borrowed], it has no receiver that carries a non-copyable type, and every parameter of it that carries one is [MovedFrom].
        /// The only containers that go into such a call are the ones that are moved, and the body of the method is checked to hand out
        /// and to leave behind nothing else.
        /// </summary>
        private bool IsPureMoveCall(ISymbol? member, IOperation? instance, ImmutableArray<IArgumentOperation> arguments)
        {
            if (member is not IMethodSymbol { MethodKind: not (MethodKind.Constructor or MethodKind.DelegateInvoke) } method ||
                NonCopyableTypes.HasBorrowedReturn(method) ||
                NonCopyableTypes.ReturnsReferenceOrRefStruct(method) ||
                !NonCopyableTypes.IsGenericPureMove(NonCopyableTypes.GetUnreduced(method).OriginalDefinition) ||
                (instance is not null && CarriesNonCopyableType(instance)))
            {
                return false;
            }

            foreach (var argument in arguments)
            {
                if (argument.Parameter is not { } parameter)
                    return false;

                if ((types.Carries(parameter.Type) || CarriesNonCopyableType(argument.Value)) && !NonCopyableTypes.IsMovedFrom(parameter.OriginalDefinition))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Gets a value indicating whether what a [MovedFrom] argument is passed to leaves a container in it that the caller can own. The
        /// body of a method says so for itself, where it assigns a parameter of a container type. With a container bound to a marked
        /// type parameter the body knows nothing of containers, so the call has to be a pure move. Nothing is known of the function
        /// behind a delegate.
        /// </summary>
        private bool LeavesOwnedContainer(IArgumentOperation argument)
        {
            if (argument.Parameter is not { } parameter || !NonCopyableTypes.IsMovedFrom(parameter.OriginalDefinition))
                return false;

            // A move hands back by value: what returns a reference or a ref struct can return one to the caller's variable, through
            // which the caller writes anything.
            return argument.Parent switch {
                IInvocationOperation { TargetMethod.MethodKind: MethodKind.DelegateInvoke } => false,
                IInvocationOperation invocation =>
                    !NonCopyableTypes.ReturnsReferenceOrRefStruct(invocation.TargetMethod) &&
                    (types.GetUse(invocation.TargetMethod).Kind is not MemberUseKind.MarkedContainer ||
                        IsPureMoveCall(invocation.TargetMethod, invocation.Instance, invocation.Arguments)),
                IObjectCreationOperation creation =>
                    creation.Type is { IsRefLikeType: false } && creation.Constructor is { } constructor && types.GetUse(constructor).Kind is not MemberUseKind.MarkedContainer,
                _ => false,
            };
        }

        /// <summary>
        /// Gets a value indicating whether the method does nothing with a 'ref' local but assign 'default' or null through it.
        /// </summary>
        private bool IsUsedOnlyToReset(ILocalSymbol local)
        {
            foreach (var operation in GetAllOperations())
            {
                if (operation is not ILocalReferenceOperation { IsDeclaration: false } reference || !SymbolEqualityComparer.Default.Equals(reference.Local, local))
                    continue;

                if (reference.Parent is not ISimpleAssignmentOperation { IsRef: false } assignment ||
                    !ReferenceEquals(reference, assignment.Target) ||
                    !IsDefaultOrNull(assignment.Value))
                {
                    return false;
                }
            }

            return true;

            static bool IsDefaultOrNull(IOperation value)
            {
                RuntimeHelpers.EnsureSufficientExecutionStack();

                return SkipParentheses(value) switch {
                    IDefaultValueOperation => true,
                    ILiteralOperation { ConstantValue: { HasValue: true, Value: null } } => true,
                    IConversionOperation { OperatorMethod: null } conversion => IsDefaultOrNull(conversion.Operand),
                    _ => false,
                };
            }
        }

        /// <summary>
        /// Gets a value indicating whether a reference to a parameter takes its value, as opposed to writing it, passing it on by reference or
        /// using it in place. Anything that is not known to be one of those counts as a read.
        /// </summary>
        private bool IsReadByValue(IParameterReferenceOperation operation)
        {
            IOperation current = operation;

            // Up to the target of a deconstruction, if the reference is in one.
            while (current.Parent is IParenthesizedOperation or
                ITupleOperation { Parent: IDeconstructionAssignmentOperation or ITupleOperation or IDeclarationExpressionOperation })
            {
                current = current.Parent;
            }

            return current.Parent switch {
                IArgumentOperation { Parameter.RefKind: not RefKind.None } => false,
                ISimpleAssignmentOperation assignment => !assignment.IsRef && !ReferenceEquals(current, assignment.Target),
                IDeconstructionAssignmentOperation deconstruction => !ReferenceEquals(current, deconstruction.Target),
                IMemberReferenceOperation memberReference => !ReferenceEquals(current, memberReference.Instance),
                IInvocationOperation invocation => !ReferenceEquals(current, invocation.Instance),
                IVariableInitializerOperation { Parent: IVariableDeclaratorOperation { Symbol.RefKind: not RefKind.None } } => false,
                IReturnOperation returned => GetReturnRefKind(returned) is RefKind.None,
                IIsPatternOperation or IIsNullOperation or IAddressOfOperation => false,

                // '??=' only looks at whether the parameter is null.
                ICoalesceAssignmentOperation coalesce => !ReferenceEquals(current, coalesce.Target),
                IBinaryOperation { OperatorMethod: null, OperatorKind: BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals } => false,
                _ => true,
            };
        }

        /// <summary>
        /// Checks the [MovedFrom] parameters of a function along its paths: from every read of one to its assignment the function
        /// leaves nowhere, reads the parameter no second time, and runs nothing but reads and writes of its own locals. Otherwise the
        /// value that was moved out would be in two places. For a local function or a lambda, which no symbol action sees, it also
        /// reports the attribute on a parameter that is not a 'ref' parameter.
        /// </summary>
        /// <param name="method">The method, local function or lambda.</param>
        /// <param name="body">An operation of the member that the function is in.</param>
        /// <param name="isLocal">True for a local function or a lambda.</param>
        private void CheckMovedFromParameters(IMethodSymbol method, IOperation body, bool isLocal)
        {
            // The receiver of an extension block is a parameter of every member of the block. The mark on a receiver that is not a
            // 'ref' parameter is reported once, where the block is declared.
            foreach (var parameter in NonCopyableTypes.GetParametersWithReceiver(method))
                Check(parameter);

            void Check(IParameterSymbol parameter)
            {
                if (!NonCopyableTypes.HasAttribute(parameter, NonCopyableTypes.MovedFromAttributeName))
                    return;

                if (parameter.RefKind is not RefKind.Ref)
                {
                    if (isLocal)
                        context.ReportDiagnostic(Diagnostic.Create(Rules.MovedFromNotOnRefParameter, parameter.Locations.FirstOrDefault(l => l.IsInSource)));

                    return;
                }

                _flow ??= new(context, types);

                if (_flow.GetGraph(body, method) is { } graph)
                {
                    _flow.CheckMovedFromPaths(graph, parameter, method, IsMoveOfLocal, (rule, syntax, arguments) => Report(rule, syntax, arguments));
                    moves.NotePathsChecked(method, parameter);
                }
            }

            // The helper of the attributes package does nothing but read its argument and assign 'default' to it.
            static bool IsMoveOfLocal(IInvocationOperation invocation) =>
                NonCopyableTypes.IsMoveHelper(invocation.TargetMethod) &&
                invocation.Arguments is [{ Value: ILocalReferenceOperation { Local.RefKind: RefKind.None } or IParameterReferenceOperation { Parameter.RefKind: RefKind.None } }];
        }
    }
}
