using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Operations;

namespace Singulink.CodeAnalysis.Analyzers;

/// <summary>
/// The list of what a container parameter may be used in, and of what each of those operations carries.
/// </summary>
/// <remarks>
/// <para>
/// A use of a container parameter that is not marked [Kept] is allowed only by a kind of operation that is named here: the check
/// asks <see cref="IsOnTheList"/> before it allows anything. A kind of operation can run code of the project with the container
/// where nothing but what the compiler bound to the operation shows it: an operator, a conversion, the members behind a range, a
/// pattern, a 'foreach' or an 'await'. So next to the kinds stands every property of their interfaces, and of the semantic
/// information that is asked for them, which gives a member that could take the container, with how that member is judged.
/// </para>
/// <para>
/// A test holds both lists against the compiler's API: for every kind here it finds, by reflection, the properties that give a
/// method, a property, a conversion or a symbol, and fails for one that neither <see cref="Read"/> nor its own table of what
/// takes no container names. A member that a later compiler adds to one of these operations is so found by the test and not by
/// a user.
/// </para>
/// </remarks>
internal static class CarriedSymbols
{
    /// <summary>
    /// The kinds of operation that a container may go through or into, each with the interface of its operation: what passes it
    /// on, what uses it, and the patterns that an 'is' or a 'switch' matches it against.
    /// </summary>
    public static readonly (OperationKind Kind, Type Operation)[] Kinds = [

        // What passes the container on: see GetNextThatPassesOn.
        (OperationKind.Parenthesized, typeof(IParenthesizedOperation)),
        (OperationKind.Tuple, typeof(ITupleOperation)),
        (OperationKind.Coalesce, typeof(ICoalesceOperation)),
        (OperationKind.Conversion, typeof(IConversionOperation)),
        (OperationKind.Conditional, typeof(IConditionalOperation)),
        (OperationKind.SwitchExpression, typeof(ISwitchExpressionOperation)),
        (OperationKind.SwitchExpressionArm, typeof(ISwitchExpressionArmOperation)),
        (OperationKind.ArrayCreation, typeof(IArrayCreationOperation)),
        (OperationKind.ArrayInitializer, typeof(IArrayInitializerOperation)),
        (OperationKind.CollectionExpression, typeof(ICollectionExpressionOperation)),
        (OperationKind.MethodReference, typeof(IMethodReferenceOperation)),
        (OperationKind.DelegateCreation, typeof(IDelegateCreationOperation)),
        (OperationKind.ArrayElementReference, typeof(IArrayElementReferenceOperation)),
        (OperationKind.ImplicitIndexerReference, typeof(IImplicitIndexerReferenceOperation)),
        (OperationKind.PropertyReference, typeof(IPropertyReferenceOperation)),
        (OperationKind.FieldReference, typeof(IFieldReferenceOperation)),
        (OperationKind.ConditionalAccess, typeof(IConditionalAccessOperation)),

        // What uses it: see JudgeWhereItGoes.
        (OperationKind.SimpleAssignment, typeof(ISimpleAssignmentOperation)),
        (OperationKind.CoalesceAssignment, typeof(ICoalesceAssignmentOperation)),
        (OperationKind.CompoundAssignment, typeof(ICompoundAssignmentOperation)),
        (OperationKind.DeconstructionAssignment, typeof(IDeconstructionAssignmentOperation)),
        (OperationKind.Increment, typeof(IIncrementOrDecrementOperation)),
        (OperationKind.Decrement, typeof(IIncrementOrDecrementOperation)),
        (OperationKind.VariableInitializer, typeof(IVariableInitializerOperation)),
        (OperationKind.Argument, typeof(IArgumentOperation)),
        (OperationKind.Invocation, typeof(IInvocationOperation)),
        (OperationKind.ObjectCreation, typeof(IObjectCreationOperation)),
        (OperationKind.EventReference, typeof(IEventReferenceOperation)),
        (OperationKind.InlineArrayAccess, typeof(IInlineArrayAccessOperation)),
        (OperationKind.Unary, typeof(IUnaryOperation)),
        (OperationKind.Binary, typeof(IBinaryOperation)),
        (OperationKind.Return, typeof(IReturnOperation)),
        (OperationKind.YieldReturn, typeof(IReturnOperation)),
        (OperationKind.With, typeof(IWithOperation)),
        (OperationKind.IsPattern, typeof(IIsPatternOperation)),
        (OperationKind.Switch, typeof(ISwitchOperation)),
        (OperationKind.CaseClause, typeof(IPatternCaseClauseOperation)),
        (OperationKind.Loop, typeof(IForEachLoopOperation)),
        (OperationKind.Await, typeof(IAwaitOperation)),
        (OperationKind.Spread, typeof(ISpreadOperation)),
        (OperationKind.IsType, typeof(IIsTypeOperation)),
        (OperationKind.Using, typeof(IUsingOperation)),
        (OperationKind.Lock, typeof(ILockOperation)),
        (OperationKind.NameOf, typeof(INameOfOperation)),
        (OperationKind.Interpolation, typeof(IInterpolationOperation)),
        (OperationKind.ExpressionStatement, typeof(IExpressionStatementOperation)),

        // The patterns of an 'is' and a 'switch', which are parts of those.
        (OperationKind.RecursivePattern, typeof(IRecursivePatternOperation)),
        (OperationKind.PropertySubpattern, typeof(IPropertySubpatternOperation)),
        (OperationKind.ListPattern, typeof(IListPatternOperation)),
        (OperationKind.SlicePattern, typeof(ISlicePatternOperation)),
        (OperationKind.DeclarationPattern, typeof(IDeclarationPatternOperation)),
        (OperationKind.TypePattern, typeof(ITypePatternOperation)),
        (OperationKind.BinaryPattern, typeof(IBinaryPatternOperation)),
        (OperationKind.NegatedPattern, typeof(INegatedPatternOperation)),
        (OperationKind.ConstantPattern, typeof(IConstantPatternOperation)),
        (OperationKind.RelationalPattern, typeof(IRelationalPatternOperation)),
        (OperationKind.DiscardPattern, typeof(IDiscardPatternOperation)),
    ];

    /// <summary>
    /// The semantic information that the collection asks the model for, where an operation does not carry its members itself.
    /// </summary>
    public static readonly Type[] Information = [typeof(AwaitExpressionInfo), typeof(ForEachStatementInfo), typeof(DeconstructionInfo)];

    /// <summary>
    /// Every property of those operations and of that information which gives a member that can take the container, with how
    /// the member is judged.
    /// </summary>
    public static readonly (Type Carrier, string Property, CarriedSymbolUse Use)[] Read = [

        // Operators and conversions: the container is an operand, which is an argument of the method.
        (typeof(IUnaryOperation), nameof(IUnaryOperation.OperatorMethod), CarriedSymbolUse.Collected),
        (typeof(IBinaryOperation), nameof(IBinaryOperation.OperatorMethod), CarriedSymbolUse.Collected),
        (typeof(IConversionOperation), nameof(IConversionOperation.OperatorMethod), CarriedSymbolUse.Collected),
        (typeof(ICoalesceOperation), nameof(ICoalesceOperation.ValueConversion), CarriedSymbolUse.Collected),
        (typeof(ICompoundAssignmentOperation), nameof(ICompoundAssignmentOperation.OperatorMethod), CarriedSymbolUse.Collected),
        (typeof(ICompoundAssignmentOperation), nameof(ICompoundAssignmentOperation.InConversion), CarriedSymbolUse.Collected),
        (typeof(IIncrementOrDecrementOperation), nameof(IIncrementOrDecrementOperation.OperatorMethod), CarriedSymbolUse.Collected),

        // Members that are used on the container: one of an extension takes it as its receiver.
        (typeof(IInvocationOperation), nameof(IInvocationOperation.TargetMethod), CarriedSymbolUse.Collected),
        (typeof(IPropertyReferenceOperation), nameof(IPropertyReferenceOperation.Property), CarriedSymbolUse.Collected),
        (typeof(IPropertyReferenceOperation), nameof(IPropertyReferenceOperation.Member), CarriedSymbolUse.Collected),
        (typeof(IMethodReferenceOperation), nameof(IMethodReferenceOperation.Method), CarriedSymbolUse.Collected),
        (typeof(IMethodReferenceOperation), nameof(IMethodReferenceOperation.Member), CarriedSymbolUse.Collected),
        (typeof(IImplicitIndexerReferenceOperation), nameof(IImplicitIndexerReferenceOperation.LengthSymbol), CarriedSymbolUse.Collected),
        (typeof(IImplicitIndexerReferenceOperation), nameof(IImplicitIndexerReferenceOperation.IndexerSymbol), CarriedSymbolUse.Collected),

        // Patterns.
        (typeof(IRecursivePatternOperation), nameof(IRecursivePatternOperation.DeconstructSymbol), CarriedSymbolUse.Collected),
        (typeof(IListPatternOperation), nameof(IListPatternOperation.LengthSymbol), CarriedSymbolUse.Collected),
        (typeof(IListPatternOperation), nameof(IListPatternOperation.IndexerSymbol), CarriedSymbolUse.Collected),
        (typeof(ISlicePatternOperation), nameof(ISlicePatternOperation.SliceSymbol), CarriedSymbolUse.Collected),

        // A spread converts each element.
        (typeof(ISpreadOperation), nameof(ISpreadOperation.ElementConversion), CarriedSymbolUse.Collected),

        // An argument is judged by the parameter that receives it, where the argument stands, with what the call is.
        (typeof(IArgumentOperation), nameof(IArgumentOperation.Parameter), CarriedSymbolUse.JudgedWhereTheOperationIs),
        (typeof(IObjectCreationOperation), nameof(IObjectCreationOperation.Constructor), CarriedSymbolUse.JudgedWhereTheOperationIs),

        // The semantic information.
        (typeof(AwaitExpressionInfo), nameof(AwaitExpressionInfo.GetAwaiterMethod), CarriedSymbolUse.Collected),
        (typeof(AwaitExpressionInfo), nameof(AwaitExpressionInfo.IsCompletedProperty), CarriedSymbolUse.Collected),
        (typeof(AwaitExpressionInfo), nameof(AwaitExpressionInfo.GetResultMethod), CarriedSymbolUse.Collected),
        (typeof(ForEachStatementInfo), nameof(ForEachStatementInfo.GetEnumeratorMethod), CarriedSymbolUse.Collected),
        (typeof(ForEachStatementInfo), nameof(ForEachStatementInfo.MoveNextMethod), CarriedSymbolUse.Collected),
        (typeof(ForEachStatementInfo), nameof(ForEachStatementInfo.CurrentProperty), CarriedSymbolUse.Collected),
        (typeof(ForEachStatementInfo), nameof(ForEachStatementInfo.DisposeMethod), CarriedSymbolUse.Collected),
        (typeof(ForEachStatementInfo), nameof(ForEachStatementInfo.ElementConversion), CarriedSymbolUse.Collected),
        (typeof(ForEachStatementInfo), nameof(ForEachStatementInfo.CurrentConversion), CarriedSymbolUse.Collected),
        (typeof(DeconstructionInfo), nameof(DeconstructionInfo.Method), CarriedSymbolUse.Collected),
        (typeof(DeconstructionInfo), nameof(DeconstructionInfo.Conversion), CarriedSymbolUse.Collected),
        (typeof(DeconstructionInfo), nameof(DeconstructionInfo.Nested), CarriedSymbolUse.Collected),
    ];

    private static readonly HashSet<OperationKind> s_kinds = [.. Kinds.Select(kind => kind.Kind)];

    /// <summary>
    /// Gets a value indicating whether a kind of operation is one that a container may go through or into.
    /// </summary>
    public static bool IsOnTheList(OperationKind kind) => s_kinds.Contains(kind);

    /// <summary>
    /// Gets the members that an operation runs one after the other on one value, where it is one of the operations that do: the
    /// value is evaluated once and handed to each of them, so a member that takes it by value gets a copy of its own. Empty for
    /// any other operation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Here are those that run members of their own on what they are applied to, with the receiver and the arguments that the
    /// members share:
    /// </para>
    /// <list type="bullet">
    /// <item>A compound assignment, an increment and a '??=' on a property or an indexer run its getter and its setter, which share
    /// the receiver and the arguments. The receiver of an extension block is a parameter of both.</item>
    /// <item>An index or a range that the compiler turns into a call of members (<see cref="IImplicitIndexerReferenceOperation"/>)
    /// runs the length, unless a range counts both of its ends from the start, and the indexer or Slice; with a compound assignment,
    /// an increment or a '??=' the getter and the setter of the indexer as well.</item>
    /// </list>
    /// <para>
    /// The others of the kind are judged where they are, each for a by-value receiver as well as for an argument: a user-defined
    /// '&amp;&amp;' and '||', which pass the left operand to 'operator false' or 'operator true' and then on; an argument or a receiver that
    /// an interpolated-string handler is also given; the targets of a combined delegate; and a pattern, whose members (the
    /// properties of a property pattern, Deconstruct, the length, the indexer and Slice of a list pattern) each get what it
    /// tests. What a 'foreach', an 'await' or a deconstruction calls runs on what the member before it handed out, not on one
    /// value twice.
    /// </para>
    /// </remarks>
    public static ImmutableArray<IMethodSymbol> GetMembersRunOnOneValue(IOperation operation, out IOperation? receiver, out ImmutableArray<IArgumentOperation> arguments)
    {
        receiver = null;
        arguments = [];

        var target = operation switch {
            ICompoundAssignmentOperation compound => compound.Target,
            IIncrementOrDecrementOperation increment => increment.Target,
            ICoalesceAssignmentOperation coalesce => coalesce.Target,
            _ => null,
        };

        while (target is IParenthesizedOperation parenthesized)
            target = parenthesized.Operand;

        switch (target ?? operation)
        {
            case IPropertyReferenceOperation { Property: { GetMethod: { } getter, SetMethod: { } setter } } property when target is not null:
                (receiver, arguments) = (property.Instance, property.Arguments);
                return [getter, setter];

            case IImplicitIndexerReferenceOperation implicitIndexer when target is not null || !IsWrittenAsAWhole(implicitIndexer):
                receiver = implicitIndexer.Instance;
                var members = ImmutableArray.CreateBuilder<IMethodSymbol>();

                if (NeedsLength(implicitIndexer.Argument) && (implicitIndexer.LengthSymbol as IPropertySymbol)?.GetMethod is { } length)
                    members.Add(length);

                if (implicitIndexer.IndexerSymbol is IPropertySymbol indexer)
                {
                    bool isAssigned = implicitIndexer.Parent is ISimpleAssignmentOperation assignment && ReferenceEquals(assignment.Target, implicitIndexer);

                    if ((target is not null || !isAssigned) && indexer.GetMethod is { } get)
                        members.Add(get);

                    if ((target is not null || isAssigned) && indexer.SetMethod is { } set)
                        members.Add(set);
                }
                else if (implicitIndexer.IndexerSymbol is IMethodSymbol slice)
                {
                    members.Add(slice);
                }

                return members.Count > 1 ? members.ToImmutable() : [];

            default:
                return [];
        }

        // An index or a range that is the target of a compound assignment, an increment or a '??=' is judged with that.
        static bool IsWrittenAsAWhole(IOperation implicitIndexer)
        {
            var current = implicitIndexer;

            while (current.Parent is IParenthesizedOperation parenthesized)
                current = parenthesized;

            return current.Parent switch {
                ICompoundAssignmentOperation compound => ReferenceEquals(compound.Target, current),
                IIncrementOrDecrementOperation => true,
                ICoalesceAssignmentOperation coalesce => ReferenceEquals(coalesce.Target, current),
                _ => false,
            };
        }
    }

    /// <summary>
    /// Gets a value indicating whether the compiler reads the length for an index or a range. It does not for a range both of whose
    /// ends are integers that count from the start, as in 'x[1..3]'.
    /// </summary>
    public static bool NeedsLength(IOperation argument)
    {
        return argument is not IRangeOperation range || NeedsLengthFor(range.LeftOperand, isEnd: false) || NeedsLengthFor(range.RightOperand, isEnd: true);

        static bool NeedsLengthFor(IOperation? operand, bool isEnd) => operand is null
            ? isEnd
            : operand is not IConversionOperation { IsImplicit: true, Operand.Type.SpecialType: SpecialType.System_Int32 };
    }
}

/// <summary>
/// How a member that an operation carries is judged.
/// </summary>
internal enum CarriedSymbolUse
{
    /// <summary>
    /// The one collection reads it, and both checks judge it as the passing of the container that it is.
    /// </summary>
    Collected,

    /// <summary>
    /// The check judges it where the operation is, by the parameter that receives the argument.
    /// </summary>
    JudgedWhereTheOperationIs,
}
