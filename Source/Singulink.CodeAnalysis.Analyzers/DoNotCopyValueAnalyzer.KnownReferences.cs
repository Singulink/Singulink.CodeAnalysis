using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Singulink.CodeAnalysis.Analyzers;

/// <content>
/// What is known of the references of a function: at which places a reference is known to refer to something, so that a read or a
/// plain store through it cannot fail there. The two rules that ask what can throw use it, and nothing else does.
/// </content>
public sealed partial class DoNotCopyValueAnalyzer
{
    /// <summary>
    /// The references of one function that are known to refer to something, place by place.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A reference here is a 'ref', 'in' or 'out' parameter, a 'ref' local, and 'this' in a struct. Any of them can be a null
    /// reference, as Unsafe.NullRef makes one, and a read or a write through it then fails. A reference is known at a place if on
    /// every path from the start of the function to that place one of the proofs has run on it to its end, with nothing after it
    /// that makes the reference refer to something else. The proofs are a closed list of three, the same for every reference
    /// and with every compiler: a call that returns, with the reference itself passed to a parameter that is marked [NotNullRef];
    /// a plain store through it; and a by-value read through it whose value is stored, in a field, in an element or through a
    /// reference. A read or a store of an instance field of what it refers to counts like one of the whole.
    /// </para>
    /// <para>
    /// What names the reference and is not on the list proves nothing: every other read, whatever becomes of its value (into a
    /// local, into a discard, as an argument, as an operand, as a condition, as the receiver of a member), a call of a member on
    /// it, since a method of a struct is entered through a null reference with no check, passing it on by reference to a
    /// parameter that is not marked, taking a reference to a field of it, a test with Unsafe.IsNullRef and a pattern. A compiler
    /// leaves out a read whose value nothing needs, and which one it leaves out differs by compiler; a store, and the read of a
    /// value that is stored, it cannot leave out. Nor does anything prove that stands in the arguments of a call which the
    /// compiler can leave out as a whole. A handler knows only what was known where its 'try' statement began.
    /// </para>
    /// </remarks>
    private sealed class KnownReferences
    {
        private readonly FunctionGraph _graph;
        private readonly HashSet<ISymbol>?[] _knownAtStart;

        public KnownReferences(FunctionGraph graph)
        {
            _graph = graph;
            _knownAtStart = FindKnownAtStart();
        }

        /// <summary>
        /// Gets what stands for the reference that an operation refers through: the parameter, the 'ref' local, or for 'this' in a
        /// struct the type. Null if the operation is no reference of the kinds that can be known.
        /// </summary>
        public static ISymbol? GetReference(IOperation? operation, IMethodSymbol? function) => operation switch {
            IParameterReferenceOperation { Parameter: { RefKind: not RefKind.None } parameter } => parameter,
            ILocalReferenceOperation { Local: { RefKind: not RefKind.None } local } => local,
            IInstanceReferenceOperation { ReferenceKind: InstanceReferenceKind.ContainingTypeInstance, Type: { IsValueType: true } type } => type,

            // A parameter of a primary constructor that a struct keeps is a field of 'this'.
            IParameterReferenceOperation { Parameter: var kept } when IsKeptByStruct(kept, function) => kept.ContainingType,
            _ => null,
        };

        /// <summary>
        /// Gets a value indicating whether a parameter is one of a primary constructor of a struct that is referred to outside the
        /// constructor: the struct keeps it in a field that no declaration shows.
        /// </summary>
        public static bool IsKeptByStruct(IParameterSymbol parameter, IMethodSymbol? function) =>
            parameter is { RefKind: RefKind.None, ContainingSymbol: IMethodSymbol { MethodKind: MethodKind.Constructor, ContainingType.IsValueType: true } constructor } &&
            function is not null &&
            !SymbolEqualityComparer.Default.Equals(constructor, function) &&
            constructor.DeclaringSyntaxReferences.Any(static declared => declared.GetSyntax() is TypeDeclarationSyntax);

        /// <summary>
        /// Gets a value indicating whether a reference is known to refer to something where an operation runs: before the operation
        /// at the given place, on every path that leads there.
        /// </summary>
        public bool IsKnownAt(ISymbol reference, FlowPosition position)
        {
            if (_knownAtStart[position.Block] is not { } atStart)
                return false;

            bool isKnown = atStart.Contains(reference);
            var operations = _graph.GetOperations(position.Block);

            for (int i = 0; i < position.Index && i < operations.Length; i++)
            {
                if (SymbolEqualityComparer.Default.Equals(GetRepointed(operations[i]), reference))
                    isKnown = false;
                else if (!isKnown && Proves(operations[i], reference))
                    isKnown = true;
            }

            return isKnown;
        }

        private bool Proves(IOperation operation, ISymbol reference)
        {
            foreach (var proven in GetProven(operation))
            {
                if (SymbolEqualityComparer.Default.Equals(proven, reference))
                    return true;
            }

            return false;
        }

        // The state at the start of every block: the references that are known on every path to it. Null for a block that no path
        // reaches.
        private HashSet<ISymbol>?[] FindKnownAtStart()
        {
            var blocks = _graph.Graph.Blocks;
            var atStart = new HashSet<ISymbol>?[blocks.Length];
            var atEnd = new HashSet<ISymbol>?[blocks.Length];
            var repointedIn = new Dictionary<ControlFlowRegion, HashSet<ISymbol>>();
            bool hasChanged = true;

            if (blocks.Length > 0)
                atStart[0] = new(SymbolEqualityComparer.Default);

            // The sets only shrink, from "everything" for a block that has not been reached yet.
            while (hasChanged)
            {
                hasChanged = false;

                for (int block = 0; block < blocks.Length; block++)
                {
                    var known = block is 0 ? atStart[0] : GetKnownOnEntry(blocks[block]);

                    if (known is null)
                        continue;

                    if (atStart[block] is null || !atStart[block]!.SetEquals(known))
                    {
                        atStart[block] = known;
                        hasChanged = true;
                    }

                    var after = new HashSet<ISymbol>(known, SymbolEqualityComparer.Default);

                    foreach (var operation in _graph.GetOperations(block))
                    {
                        if (GetRepointed(operation) is { } repointed)
                            after.Remove(repointed);

                        foreach (var proven in GetProven(operation))
                            after.Add(proven);
                    }

                    if (atEnd[block] is null || !atEnd[block]!.SetEquals(after))
                    {
                        atEnd[block] = after;
                        hasChanged = true;
                    }
                }
            }

            return atStart;

            HashSet<ISymbol>? GetKnownOnEntry(BasicBlock block)
            {
                HashSet<ISymbol>? known = null;

                // A handler knows what was known where its 'try' statement began, less everything that the statement makes refer to
                // something else before the handler runs: the 'try' block, and the filters of the clauses before it. The whole
                // statement is taken, which is no less. A block is the entry of a handler by its place and not by having no
                // predecessor: a handler that starts with a loop has the back edge of the loop as one, and is joined with it below.
                if (FindStatementOfHandler(block) is { } statement)
                {
                    if (statement.NestedRegions.FirstOrDefault(static nested => nested.Kind is ControlFlowRegionKind.Try) is not { } tried ||
                        atStart[tried.FirstBlockOrdinal] is not { } before)
                    {
                        return null;
                    }

                    known = new HashSet<ISymbol>(before, SymbolEqualityComparer.Default);
                    known.ExceptWith(GetRepointedIn(statement));
                }

                foreach (var branch in block.Predecessors)
                {
                    if (atEnd[branch.Source.Ordinal] is not { } atSource)
                        continue;

                    // A 'finally' that the branch runs on its way can make a reference refer to something else as well.
                    var arriving = new HashSet<ISymbol>(atSource, SymbolEqualityComparer.Default);

                    foreach (var passed in branch.FinallyRegions)
                        arriving.ExceptWith(GetRepointedIn(passed));

                    if (known is null)
                        known = arriving;
                    else
                        known.IntersectWith(arriving);
                }

                return known;
            }

            HashSet<ISymbol> GetRepointedIn(ControlFlowRegion region)
            {
                if (repointedIn.TryGetValue(region, out var repointed))
                    return repointed;

                repointed = new(SymbolEqualityComparer.Default);

                for (int block = region.FirstBlockOrdinal; block <= region.LastBlockOrdinal; block++)
                {
                    foreach (var operation in _graph.GetOperations(block))
                    {
                        if (GetRepointed(operation) is { } symbol)
                            repointed.Add(symbol);
                    }
                }

                return repointedIn[region] = repointed;
            }
        }

        // The 'try' statement of the 'catch' clause, exception filter or 'finally' that a block is the first block of.
        private static ControlFlowRegion? FindStatementOfHandler(BasicBlock block)
        {
            for (var region = block.EnclosingRegion;
                region is not null && region.FirstBlockOrdinal == block.Ordinal;
                region = FunctionGraph.GetRegionAround(region))
            {
                if (region.Kind is not (ControlFlowRegionKind.Catch or ControlFlowRegionKind.Filter or ControlFlowRegionKind.Finally))
                    continue;

                // A clause with a filter is a region of its own around the filter and the handler.
                var clause = FunctionGraph.GetRegionAround(region) is { Kind: ControlFlowRegionKind.FilterAndHandler } filtered ? filtered : region;
                return FunctionGraph.GetRegionAround(clause);
            }

            return null;
        }

        // The reference that an operation makes refer to something else: 'x = ref other', which is also how a 'ref' local is declared
        // and how a 'foreach' gives its 'ref' variable the next element.
        private static ISymbol? GetRepointed(IOperation operation) =>
            operation is ISimpleAssignmentOperation { IsRef: true, Target: var target } ? GetReferencedVariable(target) : null;

        // The references that are known once an operation has run to its end.
        private IEnumerable<ISymbol> GetProven(IOperation operation)
        {
            // What stands in the arguments of a call that the compiler can leave out never runs.
            if (IsInCallThatCanBeLeftOut(operation))
                yield break;

            switch (operation)
            {
                // A call that returns, with the reference itself passed to a parameter that is marked [NotNullRef], wherever the
                // argument stands in the call. The mark is read off the method that the call binds to, and counts only where that
                // is the code that runs: see NonCopyableTypes.GetNotNullRefProblem.
                case IInvocationOperation { Arguments.IsEmpty: false } invocation:
                    foreach (var argument in invocation.Arguments)
                    {
                        if (NonCopyableTypes.ProvesNotNull(argument.Parameter) &&
                            GetReference(argument.Value, _graph.Function) is { } checkedSymbol &&
                            !IsRepointedInside(operation, checkedSymbol))
                        {
                            yield return checkedSymbol;
                        }
                    }

                    break;

                // A plain store. A compound assignment and an increment on a built-in type are a read and a store.
                case ISimpleAssignmentOperation { IsRef: false }:
                case ICompoundAssignmentOperation { OperatorMethod: null }:
                case IIncrementOrDecrementOperation { OperatorMethod: null }:
                    var target = operation is IAssignmentOperation assignment ? assignment.Target : ((IIncrementOrDecrementOperation)operation).Target;

                    if (GetReferenceOfPlace(target) is { } stored && !IsRepointedInside(operation, stored))
                        yield return stored;

                    break;

                // The stores of a deconstruction, once all of them are made.
                case IDeconstructionAssignmentOperation { Target: var targets }:
                    foreach (var leaf in GetLeaves(targets))
                    {
                        if (GetReferenceOfPlace(leaf) is { } deconstructedInto && !IsRepointedInside(operation, deconstructedInto))
                            yield return deconstructedInto;
                    }

                    break;

                // A by-value read that is on the list of proofs: the operation is what the reference refers to, or an instance
                // field of it, and not a part of a longer path to a field.
                default:
                    if (GetReferenceOfPlace(operation) is { } read &&
                        !(operation.Parent is IFieldReferenceOperation { Field: { IsStatic: false, RefKind: RefKind.None } } longer && ReferenceEquals(longer.Instance, operation) && operation.Type is { IsValueType: true }) &&
                        IsProofByRead(operation))
                    {
                        yield return read;
                    }

                    break;
            }
        }

        /// <summary>
        /// Gets a value indicating whether an operation inside another makes a reference refer to something else. The compiler
        /// takes the target of an assignment, and an earlier argument of a call, before the value or a later argument runs: in
        /// 'counter = Next(ref (counter = ref second))' the store goes through what 'counter' referred to before, and afterwards
        /// 'counter' refers to 'second', of which nothing is known. A proof counts for the reference as it was when the statement
        /// began.
        /// </summary>
        private static bool IsRepointedInside(IOperation operation, ISymbol reference)
        {
            if (reference is not (ILocalSymbol or IParameterSymbol))
                return false;

            foreach (var inside in operation.Descendants())
            {
                if (SymbolEqualityComparer.Default.Equals(GetRepointed(inside), reference))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Gets a value indicating whether an operation is an argument, or a part of one, of a call that the compiler leaves out
        /// with its arguments: a call of a [Conditional] method whose symbol is not defined, as Debug.Assert is in a release build,
        /// and a call of a partial method that has no implementation. The symbols are not looked at: nothing in such a call
        /// proves anything, a store and a call of a method with a marked parameter included, since none of it runs.
        /// The method is judged as it is declared, so that a generic one and a member of a generic type are included, and an
        /// override is conditional like the method that it overrides.
        /// </summary>
        /// <remarks>
        /// It is asked of the syntax, up to the function that the operation is in. With a branch in an argument the flow graph
        /// moves the pieces of the argument into blocks of their own, and an operation there has no parent that is the call.
        /// </remarks>
        private bool IsInCallThatCanBeLeftOut(IOperation operation)
        {
            var model = _graph.Graph.OriginalOperation.SemanticModel;

            for (var node = operation.Syntax.Parent; node is not null; node = node.Parent)
            {
                if (node is MemberDeclarationSyntax or AccessorDeclarationSyntax or LocalFunctionStatementSyntax or AnonymousFunctionExpressionSyntax)
                    break;

                // Without a model nothing can be told: the reference proves nothing.
                if (node is InvocationExpressionSyntax call && (model is null || (model.GetSymbolInfo(call).Symbol is IMethodSymbol called && CanBeLeftOut(called))))
                    return true;
            }

            return false;

            static bool CanBeLeftOut(IMethodSymbol called)
            {
                var declared = NonCopyableTypes.GetUnreduced(called).OriginalDefinition;

                if (declared is { IsPartialDefinition: true, PartialImplementationPart: null })
                    return true;

                for (var method = declared; method is not null; method = method.OverriddenMethod?.OriginalDefinition)
                {
                    if (NonCopyableTypes.IsConditional(method))
                        return true;
                }

                return false;
            }
        }

        // The reference that a place is reached through: the place is what the reference refers to, or an instance field of that,
        // at any depth of structs.
        private ISymbol? GetReferenceOfPlace(IOperation? place)
        {
            while (place is IFieldReferenceOperation { Field: { IsStatic: false, RefKind: RefKind.None }, Instance: { Type.IsValueType: true } owner })
                place = owner;

            return GetReference(place, _graph.Function);
        }

        private static IEnumerable<IOperation> GetLeaves(IOperation target)
        {
            RuntimeHelpers.EnsureSufficientExecutionStack();

            if (target is IDeclarationExpressionOperation declaration)
                target = declaration.Expression;

            if (target is not ITupleOperation tuple)
            {
                yield return target;
                yield break;
            }

            foreach (var element in tuple.Elements)
            {
                foreach (var leaf in GetLeaves(element))
                    yield return leaf;
            }
        }

        /// <summary>
        /// Gets a value indicating whether a by-value read through a reference is one whose value is stored: the one kind of read
        /// that proves the reference.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The read is the value of a plain store into a field of an object or a static field, into an element or through a reference,
        /// and into nothing else, or it stands on the right of a group assignment for such a target. A store cannot be left out, and
        /// neither can the read of a value that is stored. A field of a local of the method is no such place.
        /// </para>
        /// <para>
        /// Every other read proves nothing, for any reference: into a discard or into a local, as an argument, as an operand, as
        /// a condition. Earlier versions counted more of them, and each needed its own account of what the compiler leaves out,
        /// which differed by reference and by compiler: that of the .NET 10 SDK emits nothing for '_ = x;' through a reference
        /// to a struct where that of the .NET 11 SDK emits the read.
        /// </para>
        /// </remarks>
        private static bool IsProofByRead(IOperation read)
        {
            var current = read;

            while (current.Parent is IParenthesizedOperation or IConversionOperation { OperatorMethod: null })
                current = current.Parent;

            switch (current.Parent)
            {
                case ISimpleAssignmentOperation { IsRef: false } assignment when ReferenceEquals(assignment.Value, current):
                    return IsStoredInto(assignment.Target);

                // The target that takes the element where the tuple is taken apart. The tuple of targets of a deconstruction is none.
                case ITupleOperation tuple:
                    return !FunctionGraph.IsDeconstructionTarget(tuple) && FindTargetOfElement(current, tuple) is { } taking && IsStoredInto(taking);

                default:
                    return false;
            }

            // A target that the compiler cannot leave out, which is named: a field of an object or a static field, an element, and what a
            // reference refers to. A field of a struct is such a place where the struct is one. Anything else is not.
            static bool IsStoredInto(IOperation target)
            {
                RuntimeHelpers.EnsureSufficientExecutionStack();

                while (target is IParenthesizedOperation or IDeclarationExpressionOperation)
                    target = target.ChildOperations.First();

                return target switch {
                    IFieldReferenceOperation { Instance: null or { Type.IsReferenceType: true } } => true,
                    IFieldReferenceOperation { Instance: { } owner } => IsStoredInto(owner),
                    IInlineArrayAccessOperation { Instance: var buffer } => IsStoredInto(buffer),
                    IArrayElementReferenceOperation => true,
                    ILocalReferenceOperation { Local.RefKind: not RefKind.None } => true,
                    IParameterReferenceOperation { Parameter.RefKind: not RefKind.None } => true,
                    IInstanceReferenceOperation { Type.IsValueType: true } => true,
                    IInvocationOperation { TargetMethod.ReturnsByRef: true } => true,
                    IPropertyReferenceOperation { Property.ReturnsByRef: true } => true,
                    _ => false,
                };
            }
        }

        /// <summary>
        /// Gets the target of a deconstruction that takes an element of the tuple which is taken apart, where that tuple is written
        /// out. Null if the tuple is not the value of a deconstruction, or the target cannot be told.
        /// </summary>
        public static IOperation? FindTargetOfElement(IOperation element, ITupleOperation tuple)
        {
            var path = new Stack<int>();
            IOperation current = element;
            IOperation parent = tuple;

            while (true)
            {
                if (parent is ITupleOperation containing)
                    path.Push(containing.Elements.IndexOf(current));
                else if (parent is not (IConversionOperation { OperatorMethod: null } or IParenthesizedOperation))
                    break;

                if (parent.Parent is null)
                    return null;

                (current, parent) = (parent, parent.Parent);
            }

            if (parent is not IDeconstructionAssignmentOperation deconstruction || !ReferenceEquals(deconstruction.Value, current))
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

            return target;
        }
    }
}
