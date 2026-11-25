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
/// The two checks that follow the paths through a method, on the compiler's control-flow graph: the last use of a variable, at which it is a
/// new value, and what a method does between the read of a <c>[MovedFrom]</c> parameter and its assignment.
/// </content>
public sealed partial class DoNotCopyValueAnalyzer
{
    /// <summary>
    /// What a reference to a variable does with it, for the last use.
    /// </summary>
    private enum UseKind
    {
        /// <summary>
        /// Nothing by itself: the reference is captured, and judged where the capture is used.
        /// </summary>
        None,

        /// <summary>
        /// Takes the value, or calls a member on it that keeps no reference to it. Such a use can be the last one.
        /// </summary>
        Read,

        /// <summary>
        /// Uses the variable in place: passes it by reference, or writes a part of it.
        /// </summary>
        InPlace,

        /// <summary>
        /// Assigns the variable as a whole.
        /// </summary>
        Assign,

        /// <summary>
        /// Takes a reference that can outlive the statement, or is of a kind that is not known. The variable is never moved from.
        /// </summary>
        Escape,
    }

    /// <summary>
    /// What a reference to a <c>[MovedFrom]</c> parameter does with it, for the paths from its read to its assignment.
    /// </summary>
    private enum MoveKind
    {
        /// <summary>
        /// Nothing that matters for the paths: the reference is the target of a 'ref' assignment, or a use in place.
        /// </summary>
        None,

        /// <summary>
        /// Reads the value of the parameter by value: the move.
        /// </summary>
        Read,

        /// <summary>
        /// Assigns the parameter as a whole, which is where a path from a read ends.
        /// </summary>
        Assign,

        /// <summary>
        /// Reads and assigns in one operation, as 'Interlocked.Exchange' does where its result is used.
        /// </summary>
        ReadAndAssign,
    }

    /// <summary>
    /// How far the value that was read from a <c>[MovedFrom]</c> parameter has got.
    /// </summary>
    private enum MoveState
    {
        /// <summary>
        /// Only locals of the method that no lambda captures hold it, so it is gone if the method throws.
        /// </summary>
        InLocal,

        /// <summary>
        /// It was written to a field, through a reference or to anything else that is no such local, where it stays if the method
        /// throws.
        /// </summary>
        Stored,

        /// <summary>
        /// Something has been reported on this path: only the assignment, a second read and the ways out are still followed.
        /// </summary>
        Reported,
    }

    /// <summary>
    /// A place in a control-flow graph: a block, and the index of an operation in the order in which the block evaluates its operations.
    /// </summary>
    private readonly record struct FlowPosition(int Block, int Index);

    /// <summary>
    /// A use of a variable. For a read that hands the value to a member which has taken it only where its result says so, and whose
    /// result decides a branch, <c>SuccessorsWhereTaken</c> is where the code goes on where the member has taken the value: only those
    /// paths follow the hand-off. On the others the caller still holds the value. <c>IsEarlier</c> is true for a place where a
    /// reference uses the variable before the use that it stands for, as the getter of a compound assignment runs before its value and
    /// the setter after it: it counts on the paths from every other use, and not for whether the reference itself is a last use, which
    /// its latest use decides.
    /// </summary>
    private readonly record struct VariableUse(
        IOperation Reference,
        UseKind Kind,
        FlowPosition At,
        CaptureId? CapturedBy = null,
        int[]? SuccessorsWhereTaken = null,
        bool IsEarlier = false);

    /// <summary>
    /// A use of a [MovedFrom] parameter. For a read, <c>Holder</c> is the local that the value goes straight into, if it goes into one.
    /// </summary>
    private readonly record struct MoveUse(IOperation Reference, MoveKind Kind, FlowPosition At, MoveState State = MoveState.InLocal, ILocalSymbol? Holder = null);

    /// <summary>
    /// The flow of one member: finds the last uses of its variables, and checks its <c>[MovedFrom]</c> parameters along its paths. Everything
    /// is found when first asked for.
    /// </summary>
    private sealed partial class MemberFlow(OperationBlockAnalysisContext context, NonCopyableTypes types)
    {
        private readonly Dictionary<IOperation, FunctionGraph?> _rootGraphs = [];
        private readonly HashSet<FunctionGraph> _searchedForFunctions = [];
        private readonly Dictionary<ISymbol, FunctionGraph> _functionGraphs = new(SymbolEqualityComparer.Default);
        private readonly Dictionary<ISymbol, HashSet<(TextSpan Span, int Kind)>?> _lastUses = new(SymbolEqualityComparer.Default);
        private readonly Dictionary<ISymbol, (FunctionGraph Graph, List<VariableUse>?[] UsesOfBlock)> _uses = new(SymbolEqualityComparer.Default);

        /// <summary>
        /// Gets a value indicating whether a reference to a local or to a by-value parameter is its last use: it takes the value, or calls
        /// a member on it, and on no path that goes on from there, an exception included, is the variable used again before it is
        /// assigned as a whole. False for every reference to a variable that a reference can outlive the statement of, and wherever the
        /// graph does not decide.
        /// </summary>
        public bool IsLastUse(IOperation reference, ISymbol variable)
        {
            if (!_lastUses.TryGetValue(variable, out var lastUses))
            {
                if (GetGraph(reference, variable.ContainingSymbol) is { } graph)
                {
                    lastUses = FindLastUses(graph, variable, types, out var usesOfBlock);

                    if (usesOfBlock is not null)
                        _uses[variable] = (graph, usesOfBlock);
                }

                _lastUses[variable] = lastUses;
            }

            return lastUses is not null && lastUses.Contains(GetKey(reference));
        }

        /// <summary>
        /// Gets a value indicating whether a read of a variable that can be moved from is not its last use only because of what a
        /// 'catch' clause, an exception filter or a 'finally' does with the variable: on the paths that the code takes without an
        /// exception, and outside those clauses, the variable is not used again before it is assigned. This is for the message, which
        /// then says so.
        /// </summary>
        public bool IsUsedAgainOnlyByHandlers(IOperation reference, ISymbol variable)
        {
            if (IsLastUse(reference, variable) || !_uses.TryGetValue(variable, out var flow))
                return false;

            var (graph, usesOfBlock) = flow;
            var key = GetKey(reference);
            VariableUse? read = null;

            foreach (var inBlock in usesOfBlock)
            {
                foreach (var use in inBlock ?? [])
                {
                    if (use.IsEarlier || GetKey(use.Reference) != key)
                        continue;

                    if (read is not null || use.Kind is not UseKind.Read)
                        return false;

                    read = use;
                }
            }

            if (read is not { } found)
                return false;

            // The rest of the block of the read, which a handler is no part of.
            foreach (var use in usesOfBlock[found.At.Block]!)
            {
                if (ReferenceEquals(use.Reference, found.Reference) || use.At.Index < found.At.Index)
                    continue;

                if (use.Kind is not UseKind.Assign || use.At.Index == found.At.Index)
                    return false;

                // The uses of a block are in their order, so this is what comes next: the variable is assigned.
                return true;
            }

            var visited = new HashSet<int>();
            var pending = new Stack<int>(found.SuccessorsWhereTaken ?? graph.GetSuccessors(found.At.Block));

            while (pending.Count > 0)
            {
                int block = pending.Pop();

                if (!visited.Add(block))
                    continue;

                // What a handler does with the variable is what the message is about. The code goes on after a 'finally'.
                if (!graph.IsInHandlerOutside(block, found.At.Block) && usesOfBlock[block] is { Count: > 0 } inBlock)
                {
                    int first = inBlock[0].At.Index;

                    if (inBlock.TakeWhile(use => use.At.Index == first).Any(use => use.Kind is not UseKind.Assign))
                        return false;

                    continue;
                }

                foreach (int successor in graph.GetSuccessors(block))
                    pending.Push(successor);
            }

            return true;
        }

        /// <summary>
        /// Gets the first uses of a variable after a read that is no last use only because the variable is used again: every use that a
        /// path from the read reaches without passing another use of the variable, where an assignment of the whole variable ends a path.
        /// An exception can leave a block at any operation, so the handlers of each block on a path are on it as well, but for those of a
        /// read that is committed at the end of a 'try' block. Null where the read is no such read: the variable is never moved from,
        /// the read is in an exception filter or shares its operation with another use, it is used again only after a 'finally' that
        /// the code reaches by an exception, or a first later use has no place of its own in the source.
        /// </summary>
        /// <returns>The first later uses, as the graph has them, and whether the read is one of them itself, as in a loop.</returns>
        public (List<IOperation> Uses, bool IncludesRead)? FindFirstLaterUses(IOperation reference, ISymbol variable)
        {
            if (IsLastUse(reference, variable) || !_uses.TryGetValue(variable, out var flow))
                return null;

            var (graph, usesOfBlock) = flow;
            var key = GetKey(reference);
            VariableUse? found = null;

            foreach (var inBlock in usesOfBlock)
            {
                foreach (var use in inBlock ?? [])
                {
                    if (use.IsEarlier || GetKey(use.Reference) != key)
                        continue;

                    if (found is not null || use.Kind is not UseKind.Read)
                        return null;

                    found = use;
                }
            }

            if (found is not { } read || graph.IsInFilter(read.At.Block))
                return null;

            // What follows a 'finally' counts as following an exception as well, which the message at the read says. A later use that
            // only such a path reaches is no place that the code shows to follow the read.
            if (IsUsedAgainOnlyByHandlers(reference, variable) && !IsUsedInHandler(reference, variable))
                return null;

            var laterUses = new List<IOperation>();
            bool includesRead = false;
            var visited = new HashSet<int>();
            var pending = new Stack<int>();

            // The rest of the block of the read. A use at the operation of the read makes the read no last use by itself.
            var inReadBlock = usesOfBlock[read.At.Block]!;
            int next = int.MaxValue;

            foreach (var use in inReadBlock)
            {
                if (!ReferenceEquals(use.Reference, read.Reference) && use.At.Index == read.At.Index)
                    return null;

                if (use.At.Index > read.At.Index && use.At.Index < next)
                    next = use.At.Index;
            }

            if (next == int.MaxValue)
            {
                foreach (int successor in read.SuccessorsWhereTaken ?? graph.GetSuccessors(read.At.Block))
                    pending.Push(successor);
            }
            else
            {
                AddFirstUses(inReadBlock, next);
            }

            if (!IsCommittedAtEndOfTry(graph, read))
            {
                foreach (int handler in graph.GetHandlers(read.At.Block))
                    pending.Push(handler);
            }

            while (pending.Count > 0)
            {
                int block = pending.Pop();

                if (!visited.Add(block))
                    continue;

                if (usesOfBlock[block] is { Count: > 0 } inBlock)
                {
                    AddFirstUses(inBlock, inBlock[0].At.Index);
                }
                else
                {
                    foreach (int successor in graph.GetSuccessors(block))
                        pending.Push(successor);
                }

                foreach (int handler in graph.GetHandlers(block))
                    pending.Push(handler);
            }

            // A later use that the source does not show, such as one that the compiler adds, has no place for a report.
            foreach (var use in laterUses)
            {
                if (use.Syntax is not IdentifierNameSyntax name || name.Identifier.ValueText != variable.Name)
                    return null;
            }

            return laterUses.Count > 0 || includesRead ? (laterUses, includesRead) : null;

            // The uses at the first operation of a block that uses the variable: they are first later uses, unless all of them
            // assign the variable, which ends the path.
            void AddFirstUses(List<VariableUse> inBlock, int index)
            {
                var atIndex = inBlock.Where(use => use.At.Index == index).ToList();

                if (atIndex.All(static use => use.Kind is UseKind.Assign))
                    return;

                foreach (var use in atIndex)
                {
                    if (use.Kind is UseKind.Assign)
                        continue;

                    if (GetKey(use.Reference) == key)
                        includesRead = true;
                    else if (!laterUses.Any(known => GetKey(known) == GetKey(use.Reference)))
                        laterUses.Add(use.Reference);
                }
            }
        }

        /// <summary>
        /// Gets a value indicating whether a reference to a variable in an 'async' function can run after an 'await' of that
        /// function: the function has returned to its caller by then. True where the graph does not say.
        /// </summary>
        public bool IsAfterAwait(IOperation reference, ISymbol variable, IMethodSymbol function)
        {
            if (GetGraph(reference, function) is not { } graph)
                return true;

            var key = GetKey(reference);
            bool isFound = false;

            foreach (var inGraph in graph.GetReferences(variable))
            {
                if (GetKey(inGraph) != key || !graph.TryGetPosition(inGraph, out var position))
                    continue;

                if (graph.IsAfterAwait(position))
                    return true;

                isFound = true;
            }

            return !isFound;
        }

        /// <summary>
        /// Gets a value indicating whether one of the clauses that an exception at a read of a variable can reach uses the variable
        /// itself. If none does, the variable is used by what follows a 'finally', which the flow takes to follow an exception as
        /// well: the 'finally' that the compiler adds for a 'foreach', a 'using' or a 'lock' is one.
        /// </summary>
        public bool IsUsedInHandler(IOperation reference, ISymbol variable)
        {
            if (!_uses.TryGetValue(variable, out var flow) || !flow.Graph.TryGetPosition(FindInGraph(flow.UsesOfBlock, reference) ?? reference, out var readAt))
                return true;

            for (int block = 0; block < flow.UsesOfBlock.Length; block++)
            {
                if (flow.UsesOfBlock[block] is { Count: > 0 } && flow.Graph.IsInHandlerOf(block, readAt.Block))
                    return true;
            }

            return false;

            static IOperation? FindInGraph(List<VariableUse>?[] usesOfBlock, IOperation reference)
            {
                var key = GetKey(reference);

                foreach (var inBlock in usesOfBlock)
                {
                    foreach (var use in inBlock ?? [])
                    {
                        if (GetKey(use.Reference) == key)
                            return use.Reference;
                    }
                }

                return null;
            }
        }

        /// <summary>
        /// Gets the graph of the function that an operation is in.
        /// </summary>
        /// <param name="operation">An operation of the member.</param>
        /// <param name="function">The lambda or the local function. Anything else stands for the member itself.</param>
        public FunctionGraph? GetGraph(IOperation operation, ISymbol? function)
        {
            // The graph is asked for with one of the blocks of the member, and is the graph of everything that the block is a part of.
            IOperation? block = null;
            var root = operation;

            while (true)
            {
                if (block is null && context.OperationBlocks.Contains(root))
                    block = root;

                if (root.Parent is null)
                    break;

                root = root.Parent;
            }

            if (!_rootGraphs.TryGetValue(root, out var rootGraph))
                _rootGraphs[root] = rootGraph = block is null ? null : CreateRootGraph(block);

            if (rootGraph is null || function is not IMethodSymbol { MethodKind: MethodKind.AnonymousFunction or MethodKind.LocalFunction })
                return rootGraph;

            if (_searchedForFunctions.Add(rootGraph))
                AddFunctionGraphs(rootGraph);

            return _functionGraphs.TryGetValue(function, out var functionGraph) ? functionGraph : null;
        }

        private FunctionGraph? CreateRootGraph(IOperation block)
        {
            try
            {
                return new FunctionGraph(context.GetControlFlowGraph(block), context.OwningSymbol as IMethodSymbol);
            }
            catch (ArgumentException)
            {
                // Not a block that has a graph, such as an attribute. No reference to a variable is known to sit in one; the analyzer
                // also runs in the editor on code that is being typed.
                return null;
            }
        }

        private void AddFunctionGraphs(FunctionGraph graph)
        {
            RuntimeHelpers.EnsureSufficientExecutionStack();

            foreach (var localFunction in graph.Graph.LocalFunctions)
                Add(localFunction, graph.Graph.GetLocalFunctionControlFlowGraph(localFunction));

            for (int block = 0; block < graph.BlockCount; block++)
            {
                foreach (var operation in graph.GetOperations(block))
                {
                    if (operation is IFlowAnonymousFunctionOperation lambda)
                        Add(lambda.Symbol, graph.Graph.GetAnonymousFunctionControlFlowGraph(lambda));
                }
            }

            void Add(IMethodSymbol function, ControlFlowGraph functionGraph)
            {
                if (_functionGraphs.ContainsKey(function))
                    return;

                var nested = new FunctionGraph(functionGraph, function);
                _functionGraphs[function] = nested;
                AddFunctionGraphs(nested);
            }
        }

        /// <summary>
        /// Gets what a reference in the graph and the same reference in the tree that the walker visits have in common. The two trees
        /// do not share their syntax nodes as objects: a long list lets go of its nodes and makes them anew.
        /// </summary>
        public static (TextSpan Span, int Kind) GetKey(IOperation reference) => (reference.Syntax.Span, reference.Syntax.RawKind);

        /// <summary>
        /// Gets the references to a variable that are last uses. Null if the variable is never moved from.
        /// </summary>
        private static HashSet<(TextSpan Span, int Kind)>? FindLastUses(FunctionGraph graph, ISymbol variable, NonCopyableTypes types, out List<VariableUse>?[]? usesByBlock)
        {
            usesByBlock = null;

            if (FindUses(graph, variable, types) is not { } uses)
                return null;

            int blockCount = graph.BlockCount;
            var usesOfBlock = new List<VariableUse>?[blockCount];
            usesByBlock = usesOfBlock;

            foreach (var use in uses)
            {
                if (use.Kind is not UseKind.None)
                    (usesOfBlock[use.At.Block] ??= []).Add(use);
            }

            // What a block does first with the variable: use it, assign it, or neither.
            var firstUse = new UseKind[blockCount];

            for (int block = 0; block < blockCount; block++)
            {
                if (usesOfBlock[block] is not { } inBlock)
                    continue;

                inBlock.Sort(static (x, y) => x.At.Index.CompareTo(y.At.Index));
                int first = inBlock[0].At.Index;
                firstUse[block] = inBlock.TakeWhile(use => use.At.Index == first).All(use => use.Kind is UseKind.Assign) ? UseKind.Assign : UseKind.InPlace;
            }

            // The variable is live at the start of a block if the block uses it before it assigns it, or leaves it as it is and a block
            // that follows uses it. An exception can leave the block at any operation, before anything that the block assigns.
            bool[] isLiveAtStart = new bool[blockCount];
            bool hasChanged = true;

            while (hasChanged)
            {
                hasChanged = false;

                for (int block = blockCount - 1; block >= 0; block--)
                {
                    if (isLiveAtStart[block])
                        continue;

                    if (firstUse[block] is UseKind.InPlace ||
                        IsLiveIn(graph.GetHandlers(block)) ||
                        (firstUse[block] is not UseKind.Assign && IsLiveIn(graph.GetSuccessors(block))))
                    {
                        isLiveAtStart[block] = true;
                        hasChanged = true;
                    }
                }
            }

            var lastUses = new HashSet<(TextSpan Span, int Kind)>();
            var otherUses = new HashSet<(TextSpan Span, int Kind)>();

            foreach (var use in uses)
            {
                if (!use.IsEarlier)
                    (use.Kind is UseKind.Read && IsLast(use) ? lastUses : otherUses).Add(GetKey(use.Reference));
            }

            // Two references with one syntax, as the compiler makes for some constructs, are last uses only if both are.
            lastUses.ExceptWith(otherUses);
            return lastUses;

            bool IsLiveIn(int[] blocks)
            {
                foreach (int block in blocks)
                {
                    if (isLiveAtStart[block])
                        return true;
                }

                return false;
            }

            bool IsLast(VariableUse use)
            {
                int block = use.At.Block;

                // A read in an exception filter is never the last use: a 'finally' inside the 'try' runs after it, and the filter
                // can run again for another exception.
                if (graph.IsInFilter(block))
                    return false;

                if (IsLiveIn(graph.GetHandlers(block)) && !IsCommittedAtEndOfTry(graph, use))
                    return false;

                UseKind? next = null;
                int nextIndex = int.MaxValue;

                foreach (var other in usesOfBlock[block]!)
                {
                    if (ReferenceEquals(other.Reference, use.Reference) || other.At.Index < use.At.Index)
                        continue;

                    // What runs at the same operation uses the variable together with this use.
                    if (other.At.Index == use.At.Index)
                        return false;

                    if (other.At.Index < nextIndex)
                    {
                        nextIndex = other.At.Index;
                        next = other.Kind;
                    }
                    else if (other.At.Index == nextIndex && other.Kind is not UseKind.Assign)
                    {
                        next = other.Kind;
                    }
                }

                // A hand-off to a member that has taken the value only where its result says so is followed on those paths only.
                return next is null ? !IsLiveIn(use.SuccessorsWhereTaken ?? graph.GetSuccessors(block)) : next is UseKind.Assign;
            }
        }

        // The shape "acquire, set up, commit, or dispose on failure": the value goes into a plain store, nothing that can throw
        // runs from there to the end of the block, and the code leaves the block in the normal way. A block that ends in a
        // 'throw' has committed nothing: the exception goes to the very handlers that this is about. Nor has the last block of a
        // 'finally' or of a filter, which can pass an exception on. If the store itself fails, the value is still only in the
        // variable. What follows the block is judged as what follows any block: a block that is still inside the 'try' has the
        // handlers after it, and a 'finally' always runs.
        private static bool IsCommittedAtEndOfTry(FunctionGraph graph, VariableUse use)
        {
            int block = use.At.Block;

            if (!graph.EndsNormally(block))
                return false;

            if (FindPlainStore(graph, use.Reference) is not { } store || !graph.TryGetPosition(store, out var storedAt) || storedAt.Block != block)
                return false;

            var operations = graph.GetOperations(block);

            // A state flag may follow the commit: a field of 'this' in a class cannot be reached through a null reference.
            for (int i = storedAt.Index + 1; i < operations.Length; i++)
            {
                if (DescribeWhatRunsAfterCommit(graph, operations[i]) is not null)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Gets the name of the reference that keeps a read from being a commit at the end of a 'try' block, where that is all that
        /// does: the value goes into a plain store, the block ends in the normal way, and the first thing after the store that can
        /// throw is a read or a write through a reference which is not known to refer to something. Null for anything else. This is
        /// for the message, which then says how to check the reference.
        /// </summary>
        public string? FindReferenceThatCanFailAfterStore(IOperation reference, ISymbol variable)
        {
            if (!_uses.TryGetValue(variable, out var flow))
                return null;

            // The read as the graph has it: the operations of a graph are not those of the tree that the walker visits.
            var graph = flow.Graph;
            var key = GetKey(reference);
            IOperation? read = null;

            foreach (var inBlock in flow.UsesOfBlock)
            {
                foreach (var use in inBlock ?? [])
                {
                    if (GetKey(use.Reference) == key)
                        read ??= use.Reference;
                }
            }

            if (read is null ||
                FindPlainStore(graph, read) is not { } store ||
                !graph.TryGetPosition(store, out var storedAt) ||
                !graph.EndsNormally(storedAt.Block))
            {
                return null;
            }

            var operations = graph.GetOperations(storedAt.Block);

            for (int i = storedAt.Index + 1; i < operations.Length; i++)
            {
                var failing = new StrongBox<string?>();

                if (DescribeWhatRunsAfterCommit(graph, operations[i], failing) is not null)
                    return failing.Value;
            }

            return null;
        }

        /// <summary>
        /// Gets the assignment that stores the value which a reference reads, if that is a plain store: into a local, a parameter, a
        /// field or an element, with no conversion that runs code, and not through a property, an indexer or any other call.
        /// </summary>
        private static ISimpleAssignmentOperation? FindPlainStore(FunctionGraph graph, IOperation reference)
        {
            var current = reference;

            while (current.Parent is IFieldReferenceOperation { Field.RefKind: RefKind.None } field && ReferenceEquals(field.Instance, current) && current.Type is { IsValueType: true })
                current = field;

            while (current.Parent is IConversionOperation conversion && DescribeWhatRunsAfterCommit(graph, conversion) is null)
                current = conversion;

            return current.Parent is ISimpleAssignmentOperation { IsRef: false } assignment &&
                ReferenceEquals(assignment.Value, current) &&
                assignment.Target is ILocalReferenceOperation or IParameterReferenceOperation or IFieldReferenceOperation or IArrayElementReferenceOperation
                    ? assignment
                    : null;
        }

        /// <summary>
        /// Gets what every reference to a variable does with it. Null if one of them makes the variable one that is never moved from.
        /// </summary>
        private static List<VariableUse>? FindUses(FunctionGraph graph, ISymbol variable, NonCopyableTypes types)
        {
            var uses = new Dictionary<IOperation, VariableUse>();

            // The uses of a reference that run before the use that it stands for, as the getter of a compound assignment does.
            var earlierUses = new Dictionary<IOperation, VariableUse[]>();
            var earlier = new List<VariableUse>();

            // The captures that hold a reference to the variable, or to a part of it if false.
            var captures = new Dictionary<CaptureId, bool>();
            var pending = new Queue<(IOperation Reference, bool IsWhole)>();

            foreach (var operation in graph.GetReferences(variable))
            {
                // A pattern assigns the variable that it declares.
                if (operation is IPatternOperation)
                    uses[operation] = new(operation, UseKind.Assign, graph.TryGetPosition(operation, out var declaredAt) ? declaredAt : default);
                else
                    pending.Enqueue((operation, true));
            }

            while (pending.Count > 0)
            {
                var (reference, isWhole) = pending.Dequeue();
                earlier.Clear();
                var use = Classify(graph, reference, isWhole, types, earlier);

                if (use.Kind is UseKind.Escape)
                    return null;

                // A reference to a capture that turns out to hold only a part is looked at a second time, and its second answer stands.
                uses[reference] = use;

                if (earlier.Count > 0)
                    earlierUses[reference] = [.. earlier];
                else
                    earlierUses.Remove(reference);

                if (use.CapturedBy is not { } id)
                    continue;

                isWhole &= graph.IsCapturedOnce(id);
                bool isNew = !captures.TryGetValue(id, out bool wasWhole);

                if (isNew || (wasWhole && !isWhole))
                {
                    captures[id] = isWhole;

                    foreach (var captureReference in graph.GetCaptureReferences(id))
                        pending.Enqueue((captureReference, isWhole));
                }
            }

            var found = new List<VariableUse>(uses.Values);

            foreach (var inOrder in earlierUses.Values)
                found.AddRange(inOrder);

            return found;
        }

        /// <summary>
        /// Says what a reference to a variable, or to a capture of a reference to it, does with the variable, and where in the order of
        /// evaluation: a read happens where it stands, and what needs the variable itself, such as a call on it, where that runs.
        /// Where the reference is used at more than one place, the use that is returned is the latest, and the others are added to
        /// <paramref name="earlier"/>: a compound assignment reads its target before its value runs and writes it after.
        /// </summary>
        private static VariableUse Classify(FunctionGraph graph, IOperation reference, bool isWhole, NonCopyableTypes types, List<VariableUse> earlier)
        {
            var current = reference;

            // A field of a struct, as an element of a tuple is, and an element of an inline array are parts of the variable.
            while (true)
            {
                if (current.Parent is IFieldReferenceOperation { Field.RefKind: RefKind.None } field &&
                    ReferenceEquals(field.Instance, current) &&
                    current.Type is { IsValueType: true })
                {
                    current = field;
                }
                else if (current.Parent is IInlineArrayAccessOperation { Type.IsRefLikeType: false } element && ReferenceEquals(element.Instance, current))
                {
                    current = element;
                }
                else
                {
                    break;
                }

                isWhole = false;
            }

            var parent = current.Parent;

            switch (parent)
            {
                case IFlowCaptureOperation capture when !graph.IsValueCapture(capture.Id):
                    return new(reference, UseKind.None, At(reference), capture.Id);

                case ISimpleAssignmentOperation assignment when ReferenceEquals(assignment.Target, current):
                    return assignment.IsRef ? Escape() : new(reference, isWhole ? UseKind.Assign : UseKind.InPlace, At(assignment));

                case ISimpleAssignmentOperation { IsRef: true }:
                case IAddressOfOperation:
                    return Escape();

                case IDeconstructionAssignmentOperation deconstruction when ReferenceEquals(deconstruction.Value, current):
                    // A tuple is read element by element. Any other value has its Deconstruct method called, which is a use of the
                    // variable where it is deconstructed, like the call of any other member.
                    if (current.Type is { IsTupleType: true })
                        return Read(current);

                    return FindDeconstructMethod(deconstruction) is { } deconstruct && !CanKeepReference(deconstruct)
                        ? new(reference, UseKind.Read, At(deconstruction))
                        : Escape();

                case IAssignmentOperation assignment when ReferenceEquals(assignment.Target, current):
                    // A compound assignment reads its target before its value runs, where the target is captured if the graph
                    // captures it before a branch in the value, and writes it after. An operator of the target's own type that is
                    // an instance member is called on the target in place, after the value.
                    if (assignment is ICompoundAssignmentOperation { OperatorMethod: not { IsStatic: false } } or ICoalesceAssignmentOperation)
                    {
                        if (current is IFlowCaptureReferenceOperation captured)
                        {
                            // A capture that is made at one place is made of this variable, and the read is reported at the reference
                            // to it there, which the source shows.
                            foreach (var capturedAt in graph.GetCaptures(captured.Id))
                            {
                                var variableAt = capturedAt.Value;

                                while (variableAt is IFieldReferenceOperation { Instance: { } instance })
                                    variableAt = instance;

                                bool isShown = graph.IsCapturedOnce(captured.Id) && variableAt is ILocalReferenceOperation or IParameterReferenceOperation;
                                AddEarlier(UseKind.InPlace, capturedAt.Value, isShown ? variableAt : null);
                            }
                        }
                        else
                        {
                            AddEarlier(UseKind.InPlace, current);
                        }
                    }

                    return new(reference, UseKind.InPlace, At(parent));

                case IIncrementOrDecrementOperation:
                    return new(reference, UseKind.InPlace, At(parent));

                case IDeclarationExpressionOperation:
                case ITupleOperation tuple when FunctionGraph.IsDeconstructionTarget(tuple):
                    var target = current;

                    while (target.Parent is ITupleOperation or IDeclarationExpressionOperation)
                        target = target.Parent;

                    return target.Parent switch {
                        IDeconstructionAssignmentOperation assigned when ReferenceEquals(assigned.Target, target) =>
                            new(reference, isWhole ? UseKind.Assign : UseKind.InPlace, At(assigned)),
                        IArgumentOperation declaring => ClassifyArgument(declaring),
                        _ => Escape(),
                    };

                case IArgumentOperation argument:
                    return ClassifyArgument(argument);

                case IInvocationOperation invocation when ReferenceEquals(invocation.Instance, current):
                    return ClassifyReceiver(invocation, CanKeepReference(invocation));

                case IPropertyReferenceOperation propertyReference when ReferenceEquals(propertyReference.Instance, current):
                    return ClassifyReceiver(propertyReference, CanKeepReference(propertyReference));

                case IMemberReferenceOperation memberReference when ReferenceEquals(memberReference.Instance, current):
                    // A field of an object, an event, or a method that a delegate is made of, which takes a struct by value.
                    return memberReference is IFieldReferenceOperation { Field.RefKind: not RefKind.None }
                        ? Escape()
                        : UseWhereAccessorsRun(memberReference);

                case IArrayElementReferenceOperation element when ReferenceEquals(element.ArrayReference, current):
                    return new(reference, UseKind.Read, At(element));

                case IConversionOperation { OperatorMethod: { } converting } conversion:
                    return ClassifyOperand(converting, 0, conversion);

                case IBinaryOperation { OperatorMethod: { } binaryOperator } binary:
                    return ClassifyOperand(binaryOperator, ReferenceEquals(binary.LeftOperand, current) ? 0 : 1, binary);

                case IUnaryOperation { OperatorMethod: { } unaryOperator } unary:
                    return ClassifyOperand(unaryOperator, 0, unary);

                default:
                    return graph.IsUsedByValue(current) ? Read(current) : Escape();
            }

            FlowPosition At(IOperation operation) => graph.TryGetPosition(operation, out var position) ? position : default;

            VariableUse Read(IOperation at) => new(reference, UseKind.Read, At(at));

            VariableUse Escape() => new(reference, UseKind.Escape, default);

            VariableUse ClassifyArgument(IArgumentOperation argument)
            {
                if (argument.Parameter is not { } parameter || argument.Parent is not { } call)
                    return Escape();

                if (parameter.RefKind is RefKind.None)
                {
                    var read = Read(current);

                    // The variable itself, passed as a whole to a member that has taken it only where its result says so.
                    return reference is ILocalReferenceOperation or IParameterReferenceOperation && ReferenceEquals(current, reference) && isWhole
                        ? read with { SuccessorsWhereTaken = FindSuccessorsWhereTaken(graph, argument, read.At.Block, types) }
                        : read;
                }

                if (CanKeepReference(call))
                    return Escape();

                return new(reference, parameter.RefKind is RefKind.Out && isWhole ? UseKind.Assign : UseKind.InPlace, At(call));
            }

            VariableUse ClassifyReceiver(IOperation member, bool canKeepReference)
            {
                // A member of an object is called on the reference that the variable holds. A member of a struct is called on the variable.
                if (current.Type is not { IsReferenceType: true } && canKeepReference)
                    return Escape();

                return UseWhereAccessorsRun(member);
            }

            // An accessor of a member that is read where it stands runs there. The setter of a property or of an indexer, and the
            // accessor of an event, run where the assignment is made, after the value that is assigned: in 'lease.Uses = Close(lease)'
            // the read in the value is followed by the use by the setter. A compound assignment runs the getter before its value and
            // the setter after it. With a branch in the value the graph captures the target before the branch and assigns the capture
            // after it: the setter runs where a reference to the capture is assigned, the getter of a compound assignment where the
            // target is captured, and a getter whose value is read through the capture where it is read. A capture that holds only
            // a value holds what the getter gave where it was captured. The latest of the places is the use that the reference stands
            // for, and each of the others is a use of its own, before it.
            VariableUse UseWhereAccessorsRun(IOperation member)
            {
                if (member is not (IPropertyReferenceOperation or IEventReferenceOperation))
                    return Read(member);

                var places = new List<IOperation>();

                if (!AddWhereAccessorsRun(member))
                    places.Add(member);

                FlowPosition? latest = null;

                foreach (var place in places)
                {
                    if (graph.TryGetPosition(place, out var position) && (latest is not { } known || position.Block > known.Block ||
                        (position.Block == known.Block && position.Index > known.Index)))
                    {
                        latest = position;
                    }
                }

                foreach (var place in places)
                {
                    if (graph.TryGetPosition(place, out var position) && position != latest)
                        AddEarlier(UseKind.Read, place);
                }

                return new(reference, UseKind.Read, latest ?? At(member));

                bool AddWhereAccessorsRun(IOperation target)
                {
                    RuntimeHelpers.EnsureSufficientExecutionStack();

                    while (target.Parent is ITupleOperation or IDeclarationExpressionOperation or IParenthesizedOperation)
                        target = target.Parent;

                    switch (target.Parent)
                    {
                        case IAssignmentOperation assignment when ReferenceEquals(assignment.Target, target):
                            if (assignment is ICompoundAssignmentOperation or ICoalesceAssignmentOperation)
                                places.Add(member);

                            places.Add(assignment);
                            return true;

                        case IIncrementOrDecrementOperation increment:
                            places.Add(increment);
                            return true;

                        case IEventAssignmentOperation subscription when ReferenceEquals(subscription.EventReference, target):
                            places.Add(subscription);
                            return true;

                        case IFlowCaptureOperation capture when !graph.IsValueCapture(capture.Id):
                            foreach (var captured in graph.GetCaptureReferences(capture.Id))
                            {
                                if (!AddWhereAccessorsRun(captured))
                                    places.Add(captured);
                            }

                            return true;

                        default:
                            return false;
                    }
                }
            }

            void AddEarlier(UseKind kind, IOperation at, IOperation? usedBy = null)
            {
                if (graph.TryGetPosition(at, out var position))
                    earlier.Add(new(usedBy ?? reference, kind, position, IsEarlier: true));
            }

            VariableUse ClassifyOperand(IMethodSymbol operatorMethod, int ordinal, IOperation operation)
            {
                if (ordinal >= operatorMethod.Parameters.Length)
                    return Escape();

                if (operatorMethod.Parameters[ordinal].RefKind is RefKind.None)
                    return Read(current);

                return CanKeepReference(operatorMethod) ? Escape() : new(reference, UseKind.InPlace, At(operation));
            }
        }

        /// <summary>
        /// Gets where the code goes on from a call that is given a variable by value, where the member that is called has taken the
        /// value: the branch of the condition that the call's result decides, on which the list says that the member takes it. The
        /// result decides a branch where it is the condition of an 'if', a loop or a conditional, an operand of '&amp;&amp;' or '||',
        /// or a left operand of them in a 'return', also behind '!', a comparison with the constant true or false, and an 'is' pattern
        /// of either constant or of 'not' and either. The compiler's graph splits those into blocks that branch on the call. A task of a
        /// bool goes by what awaiting it gives, also behind ConfigureAwait. Null where the member takes the value whatever it returns,
        /// and where its result goes anywhere else, as into a local: the hand-off is then judged as any other.
        /// </summary>
        /// <param name="graph">The graph.</param>
        /// <param name="argument">The argument, which is the variable itself.</param>
        /// <param name="readBlock">The block of the read, in which the condition must be.</param>
        /// <param name="types">What the list says of the member.</param>
        private static int[]? FindSuccessorsWhereTaken(FunctionGraph graph, IArgumentOperation argument, int readBlock, NonCopyableTypes types)
        {
            if (argument.Parent is not IInvocationOperation invocation || argument.Parameter is not { } parameter)
                return null;

            // The result where the member has taken the value is the one that the entry does not list.
            bool taken;

            switch (types.GetBorrowedOutcomes(invocation.TargetMethod, parameter) & (SafeListOutcomes.True | SafeListOutcomes.False))
            {
                case SafeListOutcomes.True:
                    taken = false;
                    break;

                case SafeListOutcomes.False:
                    taken = true;
                    break;

                default:
                    return null;
            }

            IOperation current = invocation;

            if (SafeMemberList.IsTaskOfBoolean(invocation.Type))
            {
                if (current.Parent is IInvocationOperation { TargetMethod: { Name: "ConfigureAwait", IsStatic: false } configure } configured &&
                    ReferenceEquals(configured.Instance, current) &&
                    SymbolEqualityComparer.Default.Equals(configure.ContainingType, invocation.Type))
                {
                    current = configured;
                }

                if (current.Parent is not IAwaitOperation awaited)
                    return null;

                current = awaited;
            }
            else if (invocation.Type?.SpecialType is not SpecialType.System_Boolean)
            {
                return null;
            }

            // What the condition is when the member has taken the value.
            while (true)
            {
                switch (current.Parent)
                {
                    case IParenthesizedOperation parenthesized:
                        current = parenthesized;
                        continue;

                    case IUnaryOperation { OperatorKind: UnaryOperatorKind.Not, OperatorMethod: null } not:
                        taken = !taken;
                        current = not;
                        continue;

                    case IBinaryOperation { OperatorKind: BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals, OperatorMethod: null, IsLifted: false } comparison
                        when (ReferenceEquals(comparison.LeftOperand, current) ? comparison.RightOperand : comparison.LeftOperand) is
                            { Type.SpecialType: SpecialType.System_Boolean, ConstantValue: { HasValue: true, Value: bool constant } }:
                        taken = (taken == constant) == (comparison.OperatorKind is BinaryOperatorKind.Equals);
                        current = comparison;
                        continue;

                    case IIsPatternOperation isPattern when ReferenceEquals(isPattern.Value, current) && GetConstantOfPattern(isPattern.Pattern) is var (constant, isNegated):
                        taken = (taken == constant) != isNegated;
                        current = isPattern;
                        continue;
                }

                break;
            }

            if (!graph.TryGetPosition(current, out var position) ||
                position.Block != readBlock ||
                !ReferenceEquals(graph.Graph.Blocks[position.Block].BranchValue, current))
            {
                return null;
            }

            return graph.GetSuccessorsWhen(position.Block, taken);

            static (bool Constant, bool IsNegated)? GetConstantOfPattern(IPatternOperation pattern) => pattern switch {
                IConstantPatternOperation { Value.ConstantValue: { HasValue: true, Value: bool constant } } => (constant, false),
                INegatedPatternOperation { Pattern: IConstantPatternOperation { Value.ConstantValue: { HasValue: true, Value: bool constant } } } => (constant, true),
                _ => null,
            };
        }

        /// <summary>
        /// Gets the Deconstruct method that a deconstruction calls on its value. Null if it cannot be found.
        /// </summary>
        private static IMethodSymbol? FindDeconstructMethod(IDeconstructionAssignmentOperation deconstruction)
        {
            if (deconstruction.Syntax is AssignmentExpressionSyntax assignment &&
                deconstruction.SemanticModel?.GetDeconstructionInfo(assignment).Method is { } method)
            {
                return method;
            }

            // The graph's copy of the operation may have no model to ask. An instance method is then found by its shape, if there is
            // one such method only.
            int count = deconstruction.Target switch {
                ITupleOperation tuple => tuple.Elements.Length,
                IDeclarationExpressionOperation { Expression: ITupleOperation declared } => declared.Elements.Length,
                _ => 0,
            };

            IMethodSymbol? found = null;

            foreach (var member in deconstruction.Value.Type?.GetMembers("Deconstruct") ?? [])
            {
                if (member is not IMethodSymbol { IsStatic: false } candidate ||
                    candidate.Parameters.Length != count ||
                    candidate.Parameters.Any(parameter => parameter.RefKind is not RefKind.Out))
                {
                    continue;
                }

                if (found is not null)
                    return null;

                found = candidate;
            }

            return found;
        }

        /// <summary>
        /// Gets a value indicating whether a call can hand back a reference to something that it is given by reference, so that the
        /// reference outlives the statement: it returns a reference, a ref struct or a pointer, it has a 'ref' or 'out' parameter of such a
        /// type, or it is made on a ref struct. True for a call of a kind that is not known.
        /// </summary>
        private static bool CanKeepReference(IOperation call) => call switch {
            IInvocationOperation invocation => invocation.Instance is { Type.IsRefLikeType: true } || CanKeepReference(invocation.TargetMethod),
            IObjectCreationOperation creation => creation.Constructor is not { } constructor || IsReferenceLike(creation.Type) || CanKeepReference(constructor),
            IPropertyReferenceOperation { Property: var property } reference =>
                reference.Instance is { Type.IsRefLikeType: true } ||
                property.RefKind is not RefKind.None ||
                IsReferenceLike(property.Type) ||
                NonCopyableTypes.GetParametersWithReceiver(property).Any(IsReferenceTaker),
            _ => true,
        };

        private static bool CanKeepReference(IMethodSymbol method) =>
            method.RefKind is not RefKind.None || IsReferenceLike(method.ReturnType) || NonCopyableTypes.GetParametersWithReceiver(method).Any(IsReferenceTaker);

        private static bool IsReferenceTaker(IParameterSymbol parameter) => parameter.RefKind is RefKind.Ref or RefKind.Out && IsReferenceLike(parameter.Type);

        private static bool IsReferenceLike(ITypeSymbol? type) => type is null or
            { IsRefLikeType: true } or
            { TypeKind: TypeKind.Pointer or TypeKind.FunctionPointer or TypeKind.Error } or
            ITypeParameterSymbol { AllowsRefLikeType: true };

        /// <summary>
        /// Follows the paths from every read of a [MovedFrom] parameter to its assignment, and reports a path on which the method leaves
        /// first, reads the parameter again, or runs anything but reads and writes of its own locals.
        /// </summary>
        /// <param name="graph">The graph of the function that declares the parameter.</param>
        /// <param name="parameter">The parameter, which for a member of an extension block can be the receiver of the block.</param>
        /// <param name="function">The function, for the messages.</param>
        /// <param name="isMoveOfLocal">Says of a call whether it is the move helper, called on a local of the method.</param>
        /// <param name="report">Reports a finding at an operation, with the arguments of its message.</param>
        public void CheckMovedFromPaths(
            FunctionGraph graph,
            IParameterSymbol parameter,
            IMethodSymbol function,
            Func<IInvocationOperation, bool> isMoveOfLocal,
            Action<Rule, SyntaxNode, object?[]> report)
        {
            var aliases = FindAliases(graph, parameter);
            var uses = FindMoveUses(graph, parameter, aliases);
            var usesAt = new Dictionary<FlowPosition, List<MoveUse>>();

            foreach (var use in uses)
            {
                if (!usesAt.TryGetValue(use.At, out var atPosition))
                    usesAt[use.At] = atPosition = [];

                atPosition.Add(use);
            }

            var reported = new HashSet<(Rule Rule, TextSpan Span)>();
            var visited = new HashSet<(int Block, MoveState State)>();
            var pending = new Stack<(int Block, int Index, MoveState State)>();

            // The locals that can come to hold the value of the read that is followed, for as long as it is only in locals.
            HashSet<ILocalSymbol>? holders = null;

            // The group assignment that the read is a value of, if it is one: its other values are plain reads, which are judged
            // with the assignment as a whole.
            IDeconstructionAssignmentOperation? groupOfRead = null;

            foreach (var read in uses)
            {
                if (read.Kind is not MoveKind.Read)
                    continue;

                holders = read.State is MoveState.InLocal ? FindHolders(graph, read.Holder) : null;
                groupOfRead = FindGroupOfRead(graph, read.Reference, types);

                visited.Clear();
                pending.Push((read.At.Block, read.At.Index + 1, holders is null ? MoveState.Stored : MoveState.InLocal));

                while (pending.Count > 0)
                {
                    var (block, index, state) = pending.Pop();

                    if (Follow(read, block, index, ref state))
                        continue;

                    if (graph.Throws(block) || graph.LetsExceptionThrough(block))
                        Report(Rules.MovedFromParameterNotAssigned, read.Reference.Syntax, Display(function), parameter.Name);

                    foreach (int successor in graph.GetSuccessors(block))
                    {
                        if (graph.IsExit(successor))
                            Report(Rules.MovedFromParameterNotAssigned, read.Reference.Syntax, Display(function), parameter.Name);
                        else if (visited.Add((successor, state)))
                            pending.Push((successor, 0, state));
                    }
                }
            }

            // Follows a block from an operation on. True if the path ends in the block, with an assignment or with another read.
            bool Follow(MoveUse read, int block, int index, ref MoveState state)
            {
                var operations = graph.GetOperations(block);

                for (int i = index; i < operations.Length; i++)
                {
                    bool assigns = false;

                    // The value is in a lasting place as soon as a local that holds it is read into anything but another such local.
                    if (state is MoveState.InLocal &&
                        operations[i] is ILocalReferenceOperation { Local: var held } use &&
                        holders is not null &&
                        holders.Contains(held) &&
                        !IsWrite(use) &&
                        !(FindLocalThatTakes(use) is { } next && holders.Contains(next)))
                    {
                        state = MoveState.Stored;
                    }

                    if (usesAt.TryGetValue(new(block, i), out var atPosition))
                    {
                        assigns = atPosition.Any(use => use.Kind is MoveKind.Assign);

                        // What reads and assigns in one operation hands out the value a second time.
                        if (atPosition.FirstOrDefault(use => use.Kind is MoveKind.Read or MoveKind.ReadAndAssign) is { Reference: { } again })
                        {
                            Report(Rules.MovedFromParameterReadAgain, again.Syntax, parameter.Name);
                            return true;
                        }
                    }

                    // A call that assigns the parameter, through an 'out' or a [MovedFrom] parameter of its own, still is a call: it can
                    // throw before it assigns, and one that moves takes the value a second time.
                    var failing = new StrongBox<string?>();

                    if (state is not MoveState.Reported &&
                        !IsValueOfGroup(operations[i]) &&
                        DescribeWhatRuns(graph, operations[i], state, parameter, aliases, isMoveOfLocal, types, groupOfRead, failing) is { } what)
                    {
                        // Where it is a read or a write through a reference that can be a null reference, the message says how
                        // to check the reference.
                        if (failing.Value is { } reference)
                            Report(Rules.MovedFromParameterNotAssignedAtOnceThroughReference, operations[i].Syntax, parameter.Name, reference);
                        else
                            Report(Rules.MovedFromParameterNotAssignedAtOnce, operations[i].Syntax, parameter.Name, what);

                        state = MoveState.Reported;
                    }

                    if (assigns)
                        return true;
                }

                return false;
            }

            bool IsValueOfGroup(IOperation operation)
            {
                if (groupOfRead is null)
                    return false;

                for (var current = operation; current is not null && !ReferenceEquals(current, groupOfRead); current = current.Parent)
                {
                    if (ReferenceEquals(current, groupOfRead.Value))
                        return true;
                }

                return false;
            }

            // A reference to a local that assigns it as a whole, or declares it.
            static bool IsWrite(ILocalReferenceOperation reference) =>
                reference.IsDeclaration ||
                (reference.Parent is ISimpleAssignmentOperation { IsRef: false } assignment && ReferenceEquals(assignment.Target, reference));

            void Report(Rule rule, SyntaxNode syntax, params object?[] arguments)
            {
                if (reported.Add((rule, syntax.Span)))
                    report(rule, syntax, arguments);
            }
        }

        /// <summary>
        /// Gets the locals that can come to hold the value which a read of a [MovedFrom] parameter put into a local: that local, and
        /// every local that is assigned one of them, anywhere in the function. Null if the first one is no local of the function that
        /// only the function itself uses: a variable that a lambda or a local function captures is a field of an object, where the
        /// value stays if the function throws.
        /// </summary>
        private HashSet<ILocalSymbol>? FindHolders(FunctionGraph graph, ILocalSymbol? first)
        {
            var holders = new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default);

            // A value that went into a discard is in no local.
            if (first is null)
                return holders;

            if (!IsOwnLocal(first))
                return null;

            holders.Add(first);
            bool hasChanged = true;

            while (hasChanged)
            {
                hasChanged = false;

                for (int block = 0; block < graph.BlockCount; block++)
                {
                    foreach (var operation in graph.GetOperations(block))
                    {
                        if (operation is ILocalReferenceOperation { Local: var held } use &&
                            holders.Contains(held) &&
                            FindLocalThatTakes(use) is { } next &&
                            IsOwnLocal(next) &&
                            holders.Add(next))
                        {
                            hasChanged = true;
                        }
                    }
                }
            }

            return holders;

            bool IsOwnLocal(ILocalSymbol local) => SymbolEqualityComparer.Default.Equals(local.ContainingSymbol, graph.Function) && !IsCaptured(local);
        }

        /// <summary>
        /// Gets a value indicating whether a lambda or a local function other than the function that declares a variable uses it.
        /// </summary>
        private bool IsCaptured(ISymbol variable)
        {
            foreach (var root in _rootGraphs.Values)
            {
                if (root is not null && _searchedForFunctions.Add(root))
                    AddFunctionGraphs(root);
            }

            foreach (var function in _functionGraphs.Values)
            {
                if (!SymbolEqualityComparer.Default.Equals(function.Function, variable.ContainingSymbol) && function.GetReferences(variable).Count > 0)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Gets the by-value local that the value of an operation goes straight into: the operation is what the local is declared
        /// with or assigned, behind conversions that run nothing and inside a tuple literal. Null if it goes anywhere else.
        /// </summary>
        private static ILocalSymbol? FindLocalThatTakes(IOperation value)
        {
            var current = value;

            while (current.Parent is IConversionOperation { OperatorMethod: null } or IParenthesizedOperation or ITupleOperation)
                current = current.Parent;

            return current.Parent is ISimpleAssignmentOperation { IsRef: false, Target: ILocalReferenceOperation { Local: { RefKind: RefKind.None } local } } assignment &&
                ReferenceEquals(assignment.Value, current)
                    ? local
                    : null;
        }

        /// <summary>
        /// Gets the 'ref' locals that are another name for a parameter: declared with 'ref parameter', and never made to refer to
        /// anything else.
        /// </summary>
        private static HashSet<ILocalSymbol> FindAliases(FunctionGraph graph, IParameterSymbol parameter)
        {
            var aliases = new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default);
            var others = new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default);

            for (int block = 0; block < graph.BlockCount; block++)
            {
                foreach (var operation in graph.GetOperations(block))
                {
                    if (operation is not ISimpleAssignmentOperation { IsRef: true, Target: ILocalReferenceOperation { Local: var local }, Value: var value })
                        continue;

                    bool isFirst = !aliases.Contains(local) && !others.Contains(local);

                    if (isFirst && value is IParameterReferenceOperation { Parameter: var referred } && SymbolEqualityComparer.Default.Equals(referred, parameter))
                    {
                        aliases.Add(local);
                    }
                    else
                    {
                        aliases.Remove(local);
                        others.Add(local);
                    }
                }
            }

            return aliases;
        }

        private List<MoveUse> FindMoveUses(FunctionGraph graph, IParameterSymbol parameter, HashSet<ILocalSymbol> aliases)
        {
            var uses = new List<MoveUse>();
            var captures = new HashSet<CaptureId>();
            var pending = new Queue<(IOperation Reference, bool IsWhole)>();

            foreach (var operation in graph.GetReferences(parameter))
                pending.Enqueue((operation, true));

            // Through another name for the parameter only an assignment is followed. A read through it is a copy of a stored value, like
            // a read through any reference.
            foreach (var alias in aliases)
            {
                foreach (var operation in graph.GetReferences(alias))
                {
                    if (operation.Parent is ISimpleAssignmentOperation { IsRef: false } assignment &&
                        ReferenceEquals(assignment.Target, operation) &&
                        graph.TryGetPosition(assignment, out var assignedAt))
                    {
                        uses.Add(new(operation, MoveKind.Assign, assignedAt));
                    }
                }
            }

            while (pending.Count > 0)
            {
                var (reference, isWhole) = pending.Dequeue();
                var use = ClassifyMove(graph, reference, isWhole, out var capturedBy);

                if (use.Kind is not MoveKind.None)
                    uses.Add(use);

                if (capturedBy is { } id && captures.Add(id))
                {
                    foreach (var captureReference in graph.GetCaptureReferences(id))
                        pending.Enqueue((captureReference, isWhole && graph.IsCapturedOnce(id)));
                }
            }

            return uses;
        }

        /// <summary>
        /// Says what a reference to a [MovedFrom] parameter does: read what the parameter holds, which is taking its value, a part of it or
        /// something that a member of it hands out; assign it as a whole; or neither.
        /// </summary>
        private MoveUse ClassifyMove(FunctionGraph graph, IOperation reference, bool isWhole, out CaptureId? capturedBy)
        {
            capturedBy = null;
            var current = reference;

            while (current.Parent is IFieldReferenceOperation { Field.RefKind: RefKind.None } field && ReferenceEquals(field.Instance, current) && current.Type is { IsValueType: true })
            {
                current = field;
                isWhole = false;
            }

            var parent = current.Parent;

            switch (parent)
            {
                // What a method returns by reference.
                case null when !graph.IsBranchByValue(current):
                    return None();

                case IFlowCaptureOperation capture when !graph.IsValueCapture(capture.Id):
                    capturedBy = capture.Id;
                    return None();

                // A value that is captured only to be tested for null, as '??=' does.
                case IFlowCaptureOperation capture when graph.GetCaptureReferences(capture.Id).All(captured => captured.Parent is IIsNullOperation):
                    return None();

                case ISimpleAssignmentOperation assignment when ReferenceEquals(assignment.Target, current):
                    return isWhole && !assignment.IsRef ? new(reference, MoveKind.Assign, At(assignment)) : None();

                // A 'ref' local over the parameter, which is judged where it is used.
                case ISimpleAssignmentOperation { IsRef: true }:
                case IAddressOfOperation:
                    return None();

                case IAssignmentOperation assignment when ReferenceEquals(assignment.Target, current):
                case IIncrementOrDecrementOperation:
                case IIsNullOperation:
                case IBinaryOperation { OperatorMethod: null, OperatorKind: BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals }:
                    return None();

                case IDeclarationExpressionOperation:
                case ITupleOperation tuple when FunctionGraph.IsDeconstructionTarget(tuple):
                    var target = current;

                    while (target.Parent is ITupleOperation or IDeclarationExpressionOperation)
                        target = target.Parent;

                    return isWhole && target.Parent is IDeconstructionAssignmentOperation assigned && ReferenceEquals(assigned.Target, target)
                        ? new(reference, MoveKind.Assign, At(assigned))
                        : None();

                // 'Interlocked.Exchange' reads and assigns in one operation. Where its result is discarded, nothing has been read.
                case IArgumentOperation { Parameter.RefKind: not RefKind.None, Parent: { } exchange } when isWhole && IsExchange(exchange):
                    return new(reference, IsDiscarded(exchange) ? MoveKind.Assign : MoveKind.ReadAndAssign, At(exchange));

                case IArgumentOperation { Parameter: { RefKind: not RefKind.None } taking, Parent: var call }:
                    // Passed on by reference: what receives it resets it only if it says so.
                    return isWhole && call is not null && (taking.RefKind is RefKind.Out || NonCopyableTypes.IsMovedFrom(taking.OriginalDefinition))
                        ? new(reference, MoveKind.Assign, At(call))
                        : None();

                case IInvocationOperation invocation when ReferenceEquals(invocation.Instance, current):
                    return types.Carries(invocation.Type) ? new(reference, MoveKind.Read, At(invocation)) : None();

                case IMemberReferenceOperation memberReference when ReferenceEquals(memberReference.Instance, current):
                    return types.Carries(memberReference.Type) ? new(reference, MoveKind.Read, At(memberReference)) : None();

                // An element that is used in place, as in 'Move(ref values[0])', is not read out of the array.
                case IArrayElementReferenceOperation element when ReferenceEquals(element.ArrayReference, current):
                    return types.Carries(element.Type) && graph.IsUsedByValue(element) ? new(reference, MoveKind.Read, At(element)) : None();

                // A pattern that declares nothing only tests the value.
                case IIsPatternOperation isPattern when !isPattern.Pattern.DescendantsAndSelf().Any(DeclaresVariable):
                    return None();

                default:
                    return new(reference, MoveKind.Read, At(current), GetStateAfterRead(current, out var holder), holder);
            }

            FlowPosition At(IOperation operation) => graph.TryGetPosition(operation, out var position) ? position : default;

            MoveUse None() => new(reference, MoveKind.None, default);

            static bool DeclaresVariable(IOperation pattern) => GetDeclaredVariable(pattern) is not null;

            static bool IsDiscarded(IOperation call) =>
                call.Parent is IExpressionStatementOperation or ISimpleAssignmentOperation { Target: IDiscardOperation };

            static bool IsExchange(IOperation call) => IsInterlockedExchange(call);
        }

        /// <summary>
        /// Gets where the value goes that was read from a [MovedFrom] parameter: straight into a local, which is then the holder, or
        /// into a discard, or anywhere else. Anywhere else can be a place where it stays if the method throws: a field, what a
        /// reference refers to, the target of a deconstruction, a value that a conditional hands on.
        /// </summary>
        private static MoveState GetStateAfterRead(IOperation read, out ILocalSymbol? holder)
        {
            holder = FindLocalThatTakes(read);

            if (holder is not null)
                return MoveState.InLocal;

            var current = read;

            while (current.Parent is IConversionOperation { OperatorMethod: null } or IParenthesizedOperation)
                current = current.Parent;

            return current.Parent is ISimpleAssignmentOperation { IsRef: false, Target: IDiscardOperation } assignment && ReferenceEquals(assignment.Value, current)
                ? MoveState.InLocal
                : MoveState.Stored;
        }
    }
}
