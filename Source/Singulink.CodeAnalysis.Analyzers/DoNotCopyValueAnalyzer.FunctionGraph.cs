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
/// The control-flow graph of one function as the flow checks see it: the operations of every block in the order in which they run,
/// where each one is, what follows a block, and which handlers can run after it. It knows nothing of the rules.
/// </content>
public sealed partial class DoNotCopyValueAnalyzer
{
    /// <summary>
    /// The control-flow graph of a method, a lambda or a local function, with its operations in the order of evaluation.
    /// </summary>
    private sealed class FunctionGraph
    {
        private readonly Dictionary<IOperation, FlowPosition> _positions = [];
        private readonly IOperation[][] _order;
        private readonly Dictionary<CaptureId, List<IFlowCaptureReferenceOperation>> _captureReferences = [];
        private readonly Dictionary<CaptureId, bool> _valueCaptures = [];
        private readonly Dictionary<CaptureId, List<IFlowCaptureOperation>> _captures = [];
        private Dictionary<ISymbol, List<IOperation>>? _references;
        private int[][]? _successors;
        private int[][]? _handlers;
        private ControlFlowRegion?[]? _innermostHandlers;
        private KnownReferences? _knownReferences;
        private int[]? _firstAwaits;
        private bool[]? _followsAwait;

        public FunctionGraph(ControlFlowGraph graph, IMethodSymbol? function)
        {
            Graph = graph;
            Function = function;

            var blocks = graph.Blocks;
            var order = new List<IOperation>();
            _order = new IOperation[blocks.Length][];

            for (int block = 0; block < blocks.Length; block++)
            {
                order.Clear();

                foreach (var operation in blocks[block].Operations)
                    AddInOrder(operation, order);

                if (blocks[block].BranchValue is { } branchValue)
                    AddInOrder(branchValue, order);

                _order[block] = [.. order];

                for (int index = 0; index < order.Count; index++)
                {
                    _positions[order[index]] = new(block, index);

                    if (order[index] is IFlowCaptureReferenceOperation captureReference)
                    {
                        if (!_captureReferences.TryGetValue(captureReference.Id, out var references))
                            _captureReferences[captureReference.Id] = references = [];

                        references.Add(captureReference);
                    }
                    else if (order[index] is IFlowCaptureOperation capture)
                    {
                        if (!_captures.TryGetValue(capture.Id, out var captures))
                            _captures[capture.Id] = captures = [];

                        captures.Add(capture);
                    }
                }
            }
        }

        public ControlFlowGraph Graph { get; }

        /// <summary>
        /// Gets the compilation that the function is in.
        /// </summary>
        public Compilation Compilation => Graph.OriginalOperation.SemanticModel!.Compilation;

        /// <summary>
        /// Gets the method, lambda or local function. Null for an initializer.
        /// </summary>
        public IMethodSymbol? Function { get; }

        public int BlockCount => _order.Length;

        /// <summary>
        /// Gets what is known of the references of the function, place by place. It is worked out when first asked for.
        /// </summary>
        public KnownReferences Known => _knownReferences ??= new(this);

        /// <summary>
        /// Gets the operations of a block in the order in which they are evaluated: every operation after the ones that it uses. The value
        /// that the block branches on, or returns, comes last.
        /// </summary>
        public IOperation[] GetOperations(int block) => _order[block];

        public bool TryGetPosition(IOperation operation, out FlowPosition position) => _positions.TryGetValue(operation, out position);

        public bool IsExit(int block) => Graph.Blocks[block].Kind is BasicBlockKind.Exit;

        /// <summary>
        /// Gets a value indicating whether a block ends by throwing.
        /// </summary>
        public bool Throws(int block) => Graph.Blocks[block].FallThroughSuccessor is { Semantics: ControlFlowBranchSemantics.Throw or ControlFlowBranchSemantics.Rethrow };

        /// <summary>
        /// Gets a value indicating whether control leaves a block in the normal way only: by going on to another block or by returning.
        /// A block that ends in a 'throw', and the last block of a 'finally' or of a filter, can pass an exception on to the handlers
        /// around it.
        /// </summary>
        public bool EndsNormally(int block)
        {
            var basicBlock = Graph.Blocks[block];
            return IsNormal(basicBlock.FallThroughSuccessor) && IsNormal(basicBlock.ConditionalSuccessor);

            static bool IsNormal(ControlFlowBranch? branch) =>
                branch is null or { Semantics: ControlFlowBranchSemantics.Regular or ControlFlowBranchSemantics.Return };
        }

        /// <summary>
        /// Gets a value indicating whether a block is in a 'catch' clause, an exception filter or a 'finally' that another block is not
        /// in: seen from that block, what it does is done by a handler.
        /// </summary>
        /// <remarks>
        /// The regions around a block are nested, so another block that is outside one of the clauses around the block is outside the
        /// innermost of them: only that one needs to be looked at.
        /// </remarks>
        public bool IsInHandlerOutside(int block, int other)
        {
            var region = (_innermostHandlers ??= FindInnermostHandlers())[block];
            return region is not null && (other < region.FirstBlockOrdinal || other > region.LastBlockOrdinal);
        }

        /// <summary>
        /// Gets a value indicating whether a block is the end of an exception filter, where a filter that gives false lets the exception
        /// go on: to the clauses that follow, to the handlers around the 'try' statement, or out of the method.
        /// </summary>
        public bool LetsExceptionThrough(int block) =>
            Graph.Blocks[block].FallThroughSuccessor is { Destination: null, Semantics: ControlFlowBranchSemantics.StructuredExceptionHandling } &&
            FindFilter(Graph.Blocks[block]) is not null;

        public IReadOnlyList<IFlowCaptureReferenceOperation> GetCaptureReferences(CaptureId id) =>
            _captureReferences.TryGetValue(id, out var references) ? references : [];

        /// <summary>
        /// Gets a value indicating whether a capture is made at one place only. One that is made at several, as for
        /// 'condition ? ref a : ref b', holds one of several variables, so that what is assigned through it assigns none of them for sure.
        /// </summary>
        public bool IsCapturedOnce(CaptureId id) => _captures.TryGetValue(id, out var captures) && captures.Count == 1;

        /// <summary>
        /// Gets the places where a capture is made, each with what it captures.
        /// </summary>
        public IReadOnlyList<IFlowCaptureOperation> GetCaptures(CaptureId id) => _captures.TryGetValue(id, out var captures) ? captures : [];

        /// <summary>
        /// Gets the operations that refer to a local or a parameter, and the patterns that declare it, in no particular order.
        /// </summary>
        public IReadOnlyList<IOperation> GetReferences(ISymbol variable) =>
            (_references ??= FindReferences()).TryGetValue(variable, out var references) ? references : [];

        /// <summary>
        /// Gets the blocks that control can go to from the end of a block without an exception. A branch out of a 'try' with a 'finally'
        /// goes through the 'finally' first, and from its end to wherever any branch that runs it leads.
        /// </summary>
        public int[] GetSuccessors(int block) => (_successors ??= FindSuccessors())[block];

        /// <summary>
        /// Gets the blocks that control can go to from the end of a block that branches on a condition, where the value that the block
        /// branches on is the given one. A branch out of a 'try' with a 'finally' goes to the 'finally' first, from whose end the code
        /// goes on as <see cref="GetSuccessors"/> says. Null for a block that does not branch on a condition.
        /// </summary>
        public int[]? GetSuccessorsWhen(int block, bool value)
        {
            var basicBlock = Graph.Blocks[block];

            if (basicBlock.ConditionKind is ControlFlowConditionKind.None)
                return null;

            var branch = (basicBlock.ConditionKind is ControlFlowConditionKind.WhenTrue) == value ? basicBlock.ConditionalSuccessor : basicBlock.FallThroughSuccessor;

            if (branch is null)
                return null;

            if (!branch.FinallyRegions.IsEmpty)
                return [branch.FinallyRegions[0].FirstBlockOrdinal];

            return branch.Destination is { } destination ? [destination.Ordinal] : null;
        }

        /// <summary>
        /// Gets the first blocks of the 'catch' clauses, exception filters and 'finally' clauses that an exception in a block can reach.
        /// </summary>
        public int[] GetHandlers(int block) => (_handlers ??= FindHandlers())[block];

        /// <summary>
        /// Gets a value indicating whether a place in the graph can be reached after an 'await' has run: it follows one in its block,
        /// or its block follows a block that has one, on a path of the code or of an exception. The 'await' that the compiler adds
        /// for 'await foreach' and 'await using' is one. What an 'await' is given is evaluated before it.
        /// </summary>
        public bool IsAfterAwait(FlowPosition position)
        {
            if (_firstAwaits is null || _followsAwait is null)
            {
                var firstAwaits = new int[BlockCount];
                bool[] followsAwait = new bool[BlockCount];
                var pending = new Stack<int>();

                for (int block = 0; block < BlockCount; block++)
                {
                    firstAwaits[block] = Array.FindIndex(_order[block], static operation => operation is IAwaitOperation);

                    if (firstAwaits[block] >= 0)
                        pending.Push(block);
                }

                while (pending.Count > 0)
                {
                    int block = pending.Pop();

                    foreach (int next in GetSuccessors(block).Concat(GetHandlers(block)))
                    {
                        if (!followsAwait[next])
                        {
                            followsAwait[next] = true;
                            pending.Push(next);
                        }
                    }
                }

                (_firstAwaits, _followsAwait) = (firstAwaits, followsAwait);
            }

            return _followsAwait[position.Block] || (_firstAwaits[position.Block] >= 0 && position.Index > _firstAwaits[position.Block]);
        }

        /// <summary>
        /// Gets a value indicating whether a block is in one of the 'catch' clauses, exception filters and 'finally' clauses that an
        /// exception in another block can reach.
        /// </summary>
        public bool IsInHandlerOf(int block, int throwing)
        {
            _innermostHandlers ??= FindInnermostHandlers();

            foreach (int first in GetHandlers(throwing))
            {
                if (_innermostHandlers[first] is { } clause && block >= clause.FirstBlockOrdinal && block <= clause.LastBlockOrdinal)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Gets a value indicating whether a capture holds a value and not a reference to a variable: everything that uses it takes a value.
        /// The graph does not say which of the two a capture is.
        /// </summary>
        public bool IsValueCapture(CaptureId id)
        {
            if (_valueCaptures.TryGetValue(id, out bool known))
                return known;

            // 'with' on a struct copies its operand into a temporary and sets the members on the copy. The graph shows the copy as a
            // capture of the operand and each member as a store through the capture, which looks like a reference to the operand
            // and is none: the capture holds a value, and the operand is read where it is captured.
            if (GetCaptures(id) is [{ Value.Type.IsValueType: true } copy] && IsOperandOfWith(copy.Syntax))
                return _valueCaptures[id] = true;

            // A capture that leads back to itself is not followed.
            _valueCaptures[id] = false;

            bool isValue = true;

            foreach (var reference in GetCaptureReferences(id))
            {
                if (reference.IsInitialization || !IsUsedByValue(reference))
                {
                    isValue = false;
                    break;
                }
            }

            _valueCaptures[id] = isValue;
            return isValue;
        }

        private static bool IsOperandOfWith(SyntaxNode operand) => operand.Parent is WithExpressionSyntax with && ReferenceEquals(with.Expression, operand);

        /// <summary>
        /// Gets a value indicating whether what uses an operation takes its value. False for a use that needs the variable itself, such as a
        /// 'ref' argument, the receiver of a member of a struct or the target of an assignment, and for any use that is not known.
        /// </summary>
        public bool IsUsedByValue(IOperation operation)
        {
            RuntimeHelpers.EnsureSufficientExecutionStack();

            switch (operation.Parent)
            {
                case null:
                    return IsBranchByValue(operation);

                case IArgumentOperation { Parameter.RefKind: RefKind.None }:
                    return true;

                case ISimpleAssignmentOperation assignment:
                    return ReferenceEquals(assignment.Value, operation) && !assignment.IsRef;

                case IAssignmentOperation assignment:
                    return ReferenceEquals(assignment.Value, operation);

                case IFlowCaptureOperation capture:
                    return IsValueCapture(capture.Id);

                case IConversionOperation conversion:
                    return conversion.OperatorMethod is { } converting ? TakesByValue(converting, 0) : !conversion.GetConversion().IsInlineArray;

                case IBinaryOperation binary:
                    return binary.OperatorMethod is not { } binaryOperator || TakesByValue(binaryOperator, ReferenceEquals(binary.LeftOperand, operation) ? 0 : 1);

                case IUnaryOperation unary:
                    return unary.OperatorMethod is not { } unaryOperator || TakesByValue(unaryOperator, 0);

                case ITupleOperation tuple:
                    return !IsDeconstructionTarget(tuple);

                case IMemberReferenceOperation memberReference when ReferenceEquals(memberReference.Instance, operation):
                    return operation.Type is { IsReferenceType: true };

                // The hidden variable of a 'using' statement, which holds the value of its expression, is disposed in a call that the
                // compiler adds.
                case IInvocationOperation { IsImplicit: true, TargetMethod.Name: "Dispose" or "DisposeAsync" } disposal
                    when ReferenceEquals(disposal.Instance, operation) && operation is IFlowCaptureReferenceOperation:
                    return true;

                case IInvocationOperation invocation when ReferenceEquals(invocation.Instance, operation):
                    return operation.Type is { IsReferenceType: true };

                case IArrayElementReferenceOperation:
                case IIsNullOperation:
                case IIsPatternOperation:
                case IIsTypeOperation:
                case IAwaitOperation:
                case IInterpolationOperation:
                case IArrayInitializerOperation:
                case ICollectionExpressionOperation:
                case ISpreadOperation:
                case IThrowOperation:
                case IWithOperation:
                case IReturnOperation:
                case IExpressionStatementOperation:
                    return true;

                default:
                    return false;
            }

            static bool TakesByValue(IMethodSymbol method, int ordinal) => ordinal < method.Parameters.Length && method.Parameters[ordinal].RefKind is RefKind.None;
        }

        /// <summary>
        /// Gets a value indicating whether the value that a block ends with is taken by value: a condition, what is thrown, or what a function
        /// that does not return a reference returns.
        /// </summary>
        public bool IsBranchByValue(IOperation branchValue)
        {
            if (!TryGetPosition(branchValue, out var position))
                return false;

            var block = Graph.Blocks[position.Block];

            if (!ReferenceEquals(block.BranchValue, branchValue))
                return false;

            if (block.ConditionKind is not ControlFlowConditionKind.None)
                return true;

            return block.FallThroughSuccessor?.Semantics switch {
                ControlFlowBranchSemantics.Return => Function is not { RefKind: not RefKind.None },
                ControlFlowBranchSemantics.Throw => true,
                _ => false,
            };
        }

        public static bool IsDeconstructionTarget(IOperation tuple)
        {
            while (tuple.Parent is ITupleOperation or IDeclarationExpressionOperation)
                tuple = tuple.Parent;

            return tuple.Parent is IDeconstructionAssignmentOperation deconstruction && ReferenceEquals(deconstruction.Target, tuple);
        }

        private static void AddInOrder(IOperation root, List<IOperation> order)
        {
            var pending = new Stack<(IOperation Operation, bool IsExpanded)>();
            pending.Push((root, false));

            while (pending.Count > 0)
            {
                var (operation, isExpanded) = pending.Pop();

                // The body of a lambda is a graph of its own.
                if (isExpanded || operation is IFlowAnonymousFunctionOperation)
                {
                    order.Add(operation);
                    continue;
                }

                pending.Push((operation, true));

                // The last child goes in first, so that the first one comes out first.
                foreach (var child in operation.ChildOperations.Reverse())
                    pending.Push((child, false));
            }
        }

        private Dictionary<ISymbol, List<IOperation>> FindReferences()
        {
            var references = new Dictionary<ISymbol, List<IOperation>>(SymbolEqualityComparer.Default);

            foreach (var operations in _order)
            {
                foreach (var operation in operations)
                {
                    var variable = GetReferencedVariable(operation) ?? GetDeclaredVariable(operation);

                    if (variable is null)
                        continue;

                    if (!references.TryGetValue(variable, out var ofVariable))
                        references[variable] = ofVariable = [];

                    ofVariable.Add(operation);
                }
            }

            return references;
        }

        private int[][] FindSuccessors()
        {
            var blocks = Graph.Blocks;
            var successors = new HashSet<int>[blocks.Length];
            var afterFinally = new Dictionary<ControlFlowRegion, HashSet<int>>();

            for (int i = 0; i < blocks.Length; i++)
            {
                successors[i] = [];
                AddBranch(successors[i], blocks[i].FallThroughSuccessor);
                AddBranch(successors[i], blocks[i].ConditionalSuccessor);
            }

            // The end of a 'finally' has no destination of its own.
            for (int i = 0; i < blocks.Length; i++)
            {
                if (blocks[i].FallThroughSuccessor is not { Destination: null, Semantics: ControlFlowBranchSemantics.StructuredExceptionHandling })
                    continue;

                for (var region = blocks[i].EnclosingRegion; region is not null; region = GetRegionAround(region))
                {
                    if (region.Kind is ControlFlowRegionKind.Finally)
                    {
                        if (afterFinally.TryGetValue(region, out var next))
                            successors[i].UnionWith(next);

                        break;
                    }

                    // A filter that gives false lets the exception go on to where an exception inside the filter goes.
                    if (region.Kind is ControlFlowRegionKind.Filter)
                    {
                        successors[i].UnionWith(GetHandlers(i));
                        break;
                    }

                    if (region.Kind is ControlFlowRegionKind.Catch)
                        break;
                }
            }

            return [.. successors.Select(s => s.ToArray())];

            void AddBranch(HashSet<int> from, ControlFlowBranch? branch)
            {
                if (branch is null)
                    return;

                var finallyRegions = branch.FinallyRegions;
                int? destination = branch.Destination?.Ordinal;

                if (finallyRegions.IsEmpty)
                {
                    if (destination is int direct)
                        from.Add(direct);

                    return;
                }

                from.Add(finallyRegions[0].FirstBlockOrdinal);

                for (int i = 0; i < finallyRegions.Length; i++)
                {
                    int? next = i + 1 < finallyRegions.Length ? finallyRegions[i + 1].FirstBlockOrdinal : destination;

                    if (next is not int following)
                        continue;

                    if (!afterFinally.TryGetValue(finallyRegions[i], out var continuations))
                        afterFinally[finallyRegions[i]] = continuations = [];

                    continuations.Add(following);
                }
            }
        }

        private ControlFlowRegion?[] FindInnermostHandlers()
        {
            var innermost = new ControlFlowRegion?[BlockCount];

            for (int block = 0; block < BlockCount; block++)
            {
                for (var region = Graph.Blocks[block].EnclosingRegion; region is not null; region = GetRegionAround(region))
                {
                    if (region.Kind is ControlFlowRegionKind.Catch or ControlFlowRegionKind.FilterAndHandler or ControlFlowRegionKind.Finally)
                    {
                        innermost[block] = region;
                        break;
                    }
                }
            }

            return innermost;
        }

        private int[][] FindHandlers()
        {
            var blocks = Graph.Blocks;
            var handlers = new int[blocks.Length][];
            var found = new List<int>();

            for (int i = 0; i < blocks.Length; i++)
            {
                found.Clear();

                // An exception in a filter counts as false: the clauses that follow the filter's own are tried next, and after the last
                // of them the handlers around the 'try' statement, which the search below comes to.
                if (FindFilter(blocks[i]) is { } filter && GetRegionAround(filter) is { } ownClause && GetRegionAround(ownClause) is { } filtered)
                {
                    bool follows = false;

                    foreach (var clause in filtered.NestedRegions)
                    {
                        if (follows && clause.Kind is ControlFlowRegionKind.Catch or ControlFlowRegionKind.FilterAndHandler)
                            found.Add(clause.FirstBlockOrdinal);

                        follows |= ReferenceEquals(clause, ownClause);
                    }
                }

                for (var region = blocks[i].EnclosingRegion; region is not null; region = GetRegionAround(region))
                {
                    if (region.Kind is not ControlFlowRegionKind.Try || GetRegionAround(region) is not { } statement)
                        continue;

                    foreach (var clause in statement.NestedRegions)
                    {
                        if (clause.Kind is ControlFlowRegionKind.Catch or ControlFlowRegionKind.FilterAndHandler or ControlFlowRegionKind.Finally)
                            found.Add(clause.FirstBlockOrdinal);
                    }
                }

                handlers[i] = [.. found];
            }

            return handlers;
        }

        /// <summary>
        /// Gets the region around a region of a function, and none around the root region of the function.
        /// </summary>
        /// <remarks>
        /// The paths of a function end at the function. The root region of a lambda or of a local function has the region of the
        /// enclosing function around it in which the function is written: a 'try' there is not around the body, which runs when the
        /// function is called, and the blocks of that region are blocks of another graph, with numbers that mean nothing in this one.
        /// Every walk over the regions around a block goes through here, and none reads <see cref="ControlFlowRegion.EnclosingRegion"/>.
        /// </remarks>
        public static ControlFlowRegion? GetRegionAround(ControlFlowRegion region) =>
            region.Kind is ControlFlowRegionKind.Root ? null : region.EnclosingRegion;

        /// <summary>
        /// Gets a value indicating whether a block is in an exception filter. Filters run in a first pass, before the 'finally' blocks
        /// between the 'throw' and the 'catch', and a filter can run more than once.
        /// </summary>
        public bool IsInFilter(int block) => FindFilter(Graph.Blocks[block]) is not null;

        private static ControlFlowRegion? FindFilter(BasicBlock block)
        {
            // A lambda inside a filter is not in the filter: the walk ends at the lambda's own root.
            for (var region = block.EnclosingRegion; region is not null; region = GetRegionAround(region))
            {
                if (region.Kind is ControlFlowRegionKind.Filter)
                    return GetRegionAround(region) is { Kind: ControlFlowRegionKind.FilterAndHandler } clause && GetRegionAround(clause) is not null ? region : null;
            }

            return null;
        }
    }
}
