using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
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
/// The walker's side of the last use of a variable: which locals and parameters can be moved from at their last use, what a message
/// says where a handler still uses one, and a returned variable that lives on or is used after the return. The paths themselves are
/// followed in the flow classes.
/// </content>
public sealed partial class DoNotCopyValueAnalyzer
{
    private sealed partial class NonCopyableWalker
    {
        // The references to the parameters of primary constructors, as far as something has asked for them.
        private Dictionary<IParameterSymbol, (int Count, bool IsUsedByMember, bool IsCaptured)>? _usesOfPrimaryConstructorParameters;

        // The paths through the member, for the last use of a variable and for the [MovedFrom] parameters. Made when first asked for.
        private MemberFlow? _flow;

        // Whether a local or a by-value parameter can be moved from at its last use at all, as far as that does not depend on the paths.
        private Dictionary<ISymbol, bool>? _movableVariables;

        // The locals and parameters of a non-copyable type that a lambda or a local function captures. Found when first asked for.
        private HashSet<ISymbol>? _capturedVariables;

        // The reads of a variable of a non-copyable type that can be moved from, which are no last uses because the variable is used
        // again, in the order in which they were judged.
        private List<(IOperation Reference, ISymbol Variable)>? _readsBeforeLaterUses;

        /// <summary>
        /// Gets the reads by value of a local or a by-value parameter of a non-copyable type that can be moved from, where the read was no
        /// last use because the variable is used again: the hand-offs that a report of a copy can have been made for.
        /// </summary>
        public IReadOnlyList<(IOperation Reference, ISymbol Variable)> ReadsBeforeLaterUses => _readsBeforeLaterUses ?? [];

        /// <summary>
        /// Gets the paths through the member.
        /// </summary>
        public MemberFlow Flow => _flow ??= new(context, types);

        /// <summary>
        /// Gets a value indicating whether a variable that a 'return' reads is used after the read: by a 'finally' block, or by the end
        /// of a 'using' scope that holds a reference to it. The paths decide, as for any other read, and whatever the switch for the
        /// move on the last use says. They are looked at only where something can run after the 'return' at all.
        /// </summary>
        private bool IsUsedAfterReturn(IReturnOperation operation, IOperation source, [NotNullWhen(true)] out ISymbol? variable)
        {
            variable = GetReferencedVariable(source);

            if (variable is null || !IsFollowedByCleanup(operation.Syntax) || handOffs?.Contains(MemberFlow.GetKey(source)) is true)
                return false;

            if (Flow.IsLastUse(source, variable))
                return false;

            if (allowsImplicitMoves && IsOfMovableKind(variable) && IsMovable(variable))
                NoteReadBeforeLaterUse(source, variable);

            return true;
        }

        /// <summary>
        /// Gets a value indicating whether code of the function runs after a 'return' at a place: the place is in a 'try' statement
        /// with a 'finally', in a 'using' statement or a 'foreach', which have one that is not written out, or in the scope of a 'using'
        /// declaration.
        /// </summary>
        private static bool IsFollowedByCleanup(SyntaxNode place)
        {
            for (SyntaxNode? child = place, current = place.Parent; current is not null; child = current, current = current.Parent)
            {
                switch (current)
                {
                    case AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax or MemberDeclarationSyntax or AccessorDeclarationSyntax:
                        // Top-level statements are members of the compilation unit.
                        if (current is not GlobalStatementSyntax)
                            return false;

                        break;

                    case TryStatementSyntax { Finally: not null } tryStatement when !ReferenceEquals(tryStatement.Finally, child):
                    case UsingStatementSyntax or CommonForEachStatementSyntax:
                        return true;

                    case BlockSyntax block when HasUsingDeclarationBefore(block.Statements, child):
                    case SwitchSectionSyntax section when HasUsingDeclarationBefore(section.Statements, child):
                        return true;

                    case CompilationUnitSyntax unit:
                        foreach (var member in unit.Members)
                        {
                            if (member.SpanStart >= place.SpanStart)
                                break;

                            if (member is GlobalStatementSyntax { Statement: LocalDeclarationStatementSyntax { UsingKeyword.RawKind: not (int)SyntaxKind.None } })
                                return true;
                        }

                        return false;
                }
            }

            return false;

            static bool HasUsingDeclarationBefore(SyntaxList<StatementSyntax> statements, SyntaxNode? child)
            {
                foreach (var statement in statements)
                {
                    if (ReferenceEquals(statement, child))
                        return false;

                    if (statement is LocalDeclarationStatementSyntax { UsingKeyword.RawKind: not (int)SyntaxKind.None })
                        return true;
                }

                return false;
            }
        }

        /// <summary>
        /// Gets the local or parameter that a return gives by value if the variable does not end with the method that returns: one that a lambda
        /// or a local function captures, wherever the return is, a parameter of a primary constructor read by a member, a 'using' variable,
        /// which is disposed after the return, or anything that an iterator yields, since it goes on. Each is a stored value, and returning it
        /// is a copy.
        /// </summary>
        private ISymbol? GetVariableThatLivesOn(IReturnOperation operation, IOperation source)
        {
            if (GetReferencedVariable(source) is not { } variable)
                return null;

            if (operation.Kind is OperationKind.YieldReturn || variable is ILocalSymbol { IsUsing: true })
                return variable;

            // A variable that another method declares is a captured one, which is a field in fact. It is that field in the method that
            // declares it as well: the function that captured it can still use what the method has returned.
            if (GetEnclosingMethod(operation) is { } method &&
                GetFunctionOf(variable) is IMethodSymbol owner &&
                !SymbolEqualityComparer.Default.Equals(owner, method))
            {
                return variable;
            }

            return GetCapturedVariables().Contains(variable) ? variable : null;
        }

        /// <summary>
        /// Gets the locals and parameters of a non-copyable type that are used in a lambda or a local function other than the function that
        /// declares them.
        /// </summary>
        private HashSet<ISymbol> GetCapturedVariables()
        {
            if (_capturedVariables is not null)
                return _capturedVariables;

            var captured = new HashSet<ISymbol>(SymbolEqualityComparer.Default);

            foreach (var operation in GetAllOperations())
            {
                var variable = GetReferencedVariable(operation);

                if (variable is null || captured.Contains(variable) || !types.IsNonCopyableType(operation.Type))
                    continue;

                if (IsCapturedAt(operation, variable))
                    captured.Add(variable);
            }

            return _capturedVariables = captured;
        }

        /// <summary>
        /// Gets a value indicating whether a reference to a local or to a by-value parameter is the last use of the variable, at which
        /// it is a new value instead of a stored one: nothing uses the variable again, so nothing can use it as a second copy. See
        /// <see cref="MemberFlow.IsLastUse"/> for what a last use is, and <see cref="CanBeMovedFrom"/> for the variables that are
        /// never moved from.
        /// </summary>
        private bool IsMovedAtLastUse(IOperation reference, ISymbol variable)
        {
            // With the moves turned off no read is a last use, and the paths through the member are not worked out for it.
            if (!allowsImplicitMoves || !IsOfMovableKind(variable) || !CanMatterAsNew(reference) || !IsMovable(variable))
                return false;

            // The second walk of a member takes a hand-off for a last use, to find the reports that it made.
            if (handOffs?.Contains(MemberFlow.GetKey(reference)) is true)
                return true;

            if (Flow.IsLastUse(reference, variable))
                return true;

            NoteReadBeforeLaterUse(reference, variable);
            return false;
        }

        /// <summary>
        /// Gets a value indicating whether a variable of a kind that can be moved from is not ruled out by what holds for the whole member
        /// (see <see cref="CanBeMovedFrom"/>), worked out once for each variable.
        /// </summary>
        private bool IsMovable(ISymbol variable)
        {
            if (!(_movableVariables ??= new(SymbolEqualityComparer.Default)).TryGetValue(variable, out bool isMovable))
            {
                // Whether a local holds a container of its own is known only once every use of every such local has been looked at, and
                // looking at a use asks whether what it is given is new. Until then no container local is a new value, as none was
                // before there was a last use. The same answer goes to a question about a variable that comes up while it is judged.
                if (_isFindingContainerLocals && variable is ILocalSymbol local && IsReferenceContainer(local.Type))
                    return false;

                _movableVariables[variable] = false;
                _movableVariables[variable] = isMovable = CanBeMovedFrom(variable);
            }

            return isMovable;
        }

        /// <summary>
        /// Notes a read of a variable of a non-copyable type that is no last use because the variable is used again. A variable that holds
        /// a container is left out: what a read of one hands over is the container, which is judged by whether the method owns it.
        /// </summary>
        private void NoteReadBeforeLaterUse(IOperation reference, ISymbol variable)
        {
            var type = variable switch {
                ILocalSymbol local => local.Type,
                IParameterSymbol parameter => parameter.Type,
                _ => null,
            };

            if (!types.IsNonCopyableType(type))
                return;

            var key = MemberFlow.GetKey(reference);
            var reads = _readsBeforeLaterUses ??= [];

            if (!reads.Any(read => MemberFlow.GetKey(read.Reference) == key))
                reads.Add((reference, variable));
        }

        /// <summary>
        /// Gets what a message for a copied stored value adds where the value is a variable which would be at its last use but for a
        /// 'catch' clause, an exception filter or a 'finally' that uses it: there the code does not show why the read is a copy. Empty
        /// for anything else.
        /// </summary>
        private string GetHandlerHint(IOperation? value)
        {
            const string FilterHint =
                ": a read in an exception filter is never the last use of a variable, because a 'finally' inside the 'try' runs after the " +
                "filter and the filter can run again";

            if (value is null || !allowsImplicitMoves)
                return string.Empty;

            var reference = SkipWrappingConversions(value);

            var variable = GetReferencedVariable(reference);

            // Whether the variable can be moved from at all was worked out when the read was judged.
            if (variable is null ||
                !IsOfMovableKind(variable) ||
                !CanMatterAsNew(reference) ||
                _movableVariables is null ||
                !_movableVariables.TryGetValue(variable, out bool isMovable) ||
                !isMovable)
            {
                return string.Empty;
            }

            // A read in an exception filter is never a last use, whatever follows it.
            for (var node = reference.Syntax.Parent; node is not null and not (AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax or MemberDeclarationSyntax); node = node.Parent)
            {
                if (node is CatchFilterClauseSyntax)
                    return FilterHint;
            }

            if (!(_flow ??= new(context, types)).IsUsedAgainOnlyByHandlers(reference, variable))
                return string.Empty;

            // The note names a handler that uses the variable only where there is one. Otherwise the variable is used after a
            // 'finally', as the compiler adds one for a 'foreach', a 'using' or a 'lock': the code after it counts as following an
            // exception too.
            // Where the value is stored at the end of a 'try' block and only a read or a write through a reference that can be a
            // null reference follows, a check of that reference makes the store the commit.
            if (_flow.IsUsedInHandler(reference, variable) && _flow.FindReferenceThatCanFailAfterStore(reference, variable) is { } failing)
            {
                return $": '{variable.Name}' is not used again after this, but a 'catch', a 'finally' or an exception filter that uses it can " +
                    $"still run, because the 'try' block then reads or writes through '{failing}', which can be a null reference. Check the " +
                    $"reference first, with NonCopyable.ThrowIfNullRef(in {failing}) or with a method whose parameter is marked " +
                    "[NotNullRef], which counts only in an assembly that was built with the analyzer";
            }

            return _flow.IsUsedInHandler(reference, variable)
                ? $": '{variable.Name}' is not used again after this, but a 'catch', a 'finally' or an exception filter that uses it can still run"
                : $": '{variable.Name}' is not used again after this, but it is used after a 'finally' that can still run, written or added by " +
                    "the compiler for a 'foreach', a 'using' or a 'lock', and what follows a 'finally' counts as following an exception as well";
        }

        /// <summary>
        /// Gets a value indicating whether a variable is of a kind that can be moved from at its last use: a local or a by-value
        /// parameter of a non-copyable type, or a local that holds a container of a reference type. A view is neither, so it is never
        /// moved from. Nor is a 'using' variable, whose scope uses it when it ends.
        /// </summary>
        private bool IsOfMovableKind(ISymbol variable) => variable switch {
            ILocalSymbol { RefKind: RefKind.None, IsUsing: false, IsFixed: false, IsConst: false, Type: var type } =>
                types.IsNonCopyableType(type) || IsReferenceContainer(type),
            IParameterSymbol { RefKind: RefKind.None, IsThis: false, Type: var type } => types.IsNonCopyableType(type),
            _ => false,
        };

        private bool IsReferenceContainer(ITypeSymbol type) => type is { IsReferenceType: true, TypeKind: not TypeKind.Delegate } && types.IsContainer(type);

        /// <summary>
        /// Gets a value indicating whether it can make a difference that a variable is a new value where it is referred to. It makes none
        /// where the variable is written or passed by reference, where it is returned as it is, which is allowed without a look at the
        /// paths, and where a member is used on it that hands out nothing non-copyable. This keeps the paths from being looked at for
        /// code that copies nothing.
        /// </summary>
        private bool CanMatterAsNew(IOperation reference)
        {
            var current = SkipParenthesesUpward(reference);

            switch (current.Parent)
            {
                case null:
                case IArgumentOperation { Parameter.RefKind: not RefKind.None }:
                case IAddressOfOperation:
                case IVariableInitializerOperation { Parent: IVariableDeclaratorOperation { Symbol.RefKind: not RefKind.None } }:
                case IIncrementOrDecrementOperation:
                    return false;

                case IAssignmentOperation assignment when ReferenceEquals(current, assignment.Target):
                    return false;

                case ISimpleAssignmentOperation { IsRef: true }:
                    return false;

                // A member of an extension block that takes its receiver by value is given the variable as an argument is.
                case IInvocationOperation invocation when ReferenceEquals(current, invocation.Instance):
                    return types.Carries(invocation.Type) || NonCopyableTypes.GetExtensionReceiver(invocation.TargetMethod) is { RefKind: RefKind.None };

                case IMemberReferenceOperation memberReference when ReferenceEquals(current, memberReference.Instance):
                    return types.Carries(memberReference.Type) ||
                        memberReference is IMethodReferenceOperation ||
                        NonCopyableTypes.GetExtensionReceiver(memberReference.Member) is { RefKind: RefKind.None };

                case IConditionalAccessOperation access when ReferenceEquals(current, access.Operation):
                    return types.Carries(access.Type);

                case IDeclarationExpressionOperation:
                    return false;
            }

            // A local or a parameter that is returned as it is, also as a nullable of its type.
            while (current.Parent is IParenthesizedOperation or IConversionOperation { OperatorMethod: null, Type.IsValueType: true })
                current = current.Parent;

            return current.Parent is not IReturnOperation { Kind: OperationKind.Return };
        }

        /// <summary>
        /// Gets a value indicating whether a variable of a kind that can be moved from is not ruled out by what holds for the whole
        /// member: no lambda or local function captures it, it is not a parameter of a primary constructor that a member uses, which is
        /// a field, or of an iterator, which is copied for every enumeration, and a local that holds a container is the method's own by
        /// the strict test. A reference that can outlive the statement is found with the paths.
        /// </summary>
        private bool CanBeMovedFrom(ISymbol variable)
        {
            if (variable is ILocalSymbol local && IsReferenceContainer(local.Type))
                return IsLocalOfItsOwnByStrictTest(local);

            if (variable is IParameterSymbol parameter)
            {
                // The receiver of an extension block is a parameter of the member that is being checked.
                if (GetFunctionOf(parameter) is not IMethodSymbol method || method.IsIterator)
                    return false;

                // A primary constructor is declared by the type, and its parameters are used by the initializers of the type, which
                // are no part of a constructor as far as the paths go.
                if (method.MethodKind is MethodKind.Constructor)
                {
                    if (method.DeclaringSyntaxReferences.Any(reference => reference.GetSyntax(context.CancellationToken) is TypeDeclarationSyntax))
                        return IsUsedOnceByAnInitializer(parameter, method.ContainingType);
                }
            }

            return !GetCapturedVariables().Contains(variable);
        }

        /// <summary>
        /// Gets a value indicating whether a parameter of a primary constructor is an ordinary parameter that is used at one place: no
        /// member uses it, which would make a field of it, and the one use is in an initializer of a field or a property or in the
        /// arguments of the base constructor. Initializers have no order that the paths follow, so a second use anywhere rules the
        /// parameter out. A use is a reference that binds to the parameter: another variable of its name, such as an 'out' parameter
        /// of a member, is none.
        /// </summary>
        private bool IsUsedOnceByAnInitializer(IParameterSymbol parameter, INamedTypeSymbol type)
        {
            // A positional record makes a property of the parameter.
            if (type.IsRecord)
                return false;

            return GetUsesOfPrimaryConstructorParameter(parameter) is { Count: 1, IsUsedByMember: false, IsCaptured: false };
        }

        /// <summary>
        /// Counts the references to a parameter of a primary constructor in the type that declares it, and says where they stand.
        /// Null for any other parameter.
        /// </summary>
        /// <remarks>
        /// A parameter that a member of the type refers to is kept by the type in a field, and is that field wherever it is read. One
        /// that a lambda, a local function or a query in an initializer refers to is captured there.
        /// </remarks>
        private (int Count, bool IsUsedByMember, bool IsCaptured)? GetUsesOfPrimaryConstructorParameter(IParameterSymbol parameter)
        {
            if (parameter.ContainingSymbol is not IMethodSymbol { MethodKind: MethodKind.Constructor, ContainingType: { } type } constructor ||
                !constructor.DeclaringSyntaxReferences.Any(reference => reference.GetSyntax(context.CancellationToken) is TypeDeclarationSyntax))
            {
                return null;
            }

            if ((_usesOfPrimaryConstructorParameters ??= new(SymbolEqualityComparer.Default)).TryGetValue(parameter, out var known))
                return known;

            int count = 0;
            bool isUsedByMember = false;
            bool isCaptured = false;

            foreach (var reference in type.DeclaringSyntaxReferences)
            {
                if (reference.GetSyntax(context.CancellationToken) is not TypeDeclarationSyntax declaration)
                    continue;

                SemanticModel? model = null;

                foreach (var node in declaration.DescendantNodes())
                {
                    if (node is not IdentifierNameSyntax name || name.Identifier.ValueText != parameter.Name)
                        continue;

                    // The declaration can be in another file than the member that asks, so the model of its file is asked for. It is
                    // asked once for a parameter of a primary constructor of a non-copyable type.
#pragma warning disable RS1030 // Do not invoke Compilation.GetSemanticModel() method within a diagnostic analyzer
                    model ??= context.Compilation.GetSemanticModel(declaration.SyntaxTree);
#pragma warning restore RS1030

                    if (!SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(name, context.CancellationToken).Symbol, parameter))
                        continue;

                    count++;

                    for (var around = name.Parent; around is not null && !ReferenceEquals(around, declaration); around = around.Parent)
                    {
                        // A lambda or a local function that uses the parameter captures it.
                        if (around is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax or QueryExpressionSyntax)
                        {
                            isCaptured = true;
                            continue;
                        }

                        if (around is PrimaryConstructorBaseTypeSyntax or
                            EqualsValueClauseSyntax { Parent: PropertyDeclarationSyntax or VariableDeclaratorSyntax { Parent.Parent: BaseFieldDeclarationSyntax } })
                        {
                            break;
                        }

                        if (around is MemberDeclarationSyntax or AccessorDeclarationSyntax)
                        {
                            isUsedByMember = true;
                            break;
                        }
                    }
                }
            }

            return _usesOfPrimaryConstructorParameters[parameter] = (count, isUsedByMember, isCaptured);
        }

        /// <summary>
        /// Gets a value indicating whether a local that holds a container is the method's own with nothing left open: every value that
        /// it is assigned is the method's own, and every use of it is one of the short list of <see cref="IsQuietUseOfLocalThatHoldsContainers"/>,
        /// whatever the container holds. A container is a reference, so anything that the local is passed to can keep it, or a view of
        /// its storage, and would still reach what a read at the last use took.
        /// </summary>
        private bool IsLocalOfItsOwnByStrictTest(ILocalSymbol local)
        {
            if (!IsLocalOfItsOwn(local))
                return false;

            // What an iterator has yielded is in the hands of its consumer. And a local that was passed to anything, or that had an
            // element used in place, is not moved from, though the method may still return it.
            var use = GetContainerLocals()[local];
            return use.Yields is null && !use.IsUsedLoosely;
        }

        /// <summary>
        /// Gets a value indicating whether a member that is called on a container hands out a view of it which nothing keeps: the view
        /// is awaited, or read out of by value, where it is made, as in 'await pending.ConfigureAwait(false)' and
        /// 'pending.GetAwaiter().GetResult()'. Stored in a variable, the view would still reach what a read at the last use of the
        /// container took.
        /// </summary>
        private bool IsViewUsedOnTheSpot(IOperation receiver, ITypeSymbol holder)
        {
            RuntimeHelpers.EnsureSufficientExecutionStack();

            if (receiver.Parent is not IInvocationOperation { TargetMethod.RefKind: RefKind.None } invocation ||
                !ReferenceEquals(receiver, invocation.Instance) ||
                !types.IsView(invocation.Type) ||
                !AreQuietArguments(invocation.Arguments, holder))
            {
                return false;
            }

            IOperation view = SkipParenthesesUpward(invocation);

            return view.Parent switch {
                IAwaitOperation awaited => IsQuietResult(awaited.Type),
                IInvocationOperation { TargetMethod.RefKind: RefKind.None, Arguments.IsEmpty: true } onView when ReferenceEquals(view, onView.Instance) =>
                    types.IsView(onView.Type) ? IsViewUsedOnTheSpot(view, holder) : IsQuietResult(onView.Type),
                IPropertyReferenceOperation { Property.RefKind: RefKind.None, Arguments.IsEmpty: true } onView when ReferenceEquals(view, onView.Instance) =>
                    !IsAssignmentTarget(onView, out _) && IsQuietResult(onView.Type),
                _ => false,
            };
        }
    }
}
