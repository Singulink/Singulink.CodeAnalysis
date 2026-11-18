using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Singulink.CodeAnalysis.Analyzers;

/// <content>
/// The one description of a use of a container that the two checks of who holds it read: the check of a local that is to count
/// as the method's own, and the check of a parameter that is not marked [Kept].
/// </content>
/// <remarks>
/// <para>
/// The two checks ask different questions. A local is the method's own if nothing else can reach its container or put something
/// stored into it. A parameter is kept if something still holds its container when the method has returned. A test pass found
/// the misses of the second exactly where the first already knew better, so what both need to know of a use is said once, here:
/// what is made of the container, and whether what receives it keeps. Where what is made goes is then followed by each check in
/// its own way: a parameter is followed through the variables that hold it, and a local allows only a use where the thing is made.
/// </para>
/// </remarks>
public sealed partial class DoNotCopyValueAnalyzer
{
    private sealed partial class NonCopyableWalker
    {
        /// <summary>
        /// Says what the operation that uses a container makes of it.
        /// </summary>
        /// <param name="value">The operation that gives the container.</param>
        /// <param name="holder">The type of the container. What is made is none of its elements, which are followed by themselves.</param>
        /// <param name="made">The operation that gives what is made, if anything is.</param>
        /// <param name="member">The member that the operation runs with the container, where one is judged: a member that the
        /// compiler binds, as for a pattern or a 'foreach', which the operation does not hand back.</param>
        /// <remarks>
        /// <para>
        /// A member of checked code hands out a value of its own, unless it says [return: Borrowed]. A listed member hands out what
        /// is as new as what it was called on and with, so a container that it hands out of a container is that one under another
        /// name: 'guards.AsMemory()', the conversion of an array to a Memory&lt;T&gt; or an ArraySegment&lt;T&gt;,
        /// 'segment.Array', 'list.AsReadOnly()'. A callee that is given the variable by reference and returns a reference can
        /// return the one that it was given, as an argument, as the receiver of an extension block or as an argument of an indexer.
        /// </para>
        /// <para>
        /// Every operation with an operator method goes through the same judgment of a hand-back as a call. Where the operation is
        /// of a kind that this does not follow, and the member hands back something that can hold the container, it is
        /// <see cref="MadeOfContainer.NotFollowed"/>, which is reported.
        /// </para>
        /// </remarks>
        private MadeOfContainer DescribeWhatIsMade(IOperation value, ITypeSymbol? holder, out IOperation? made, ISymbol? member = null)
        {
            made = null;
            IOperation current = value;

            // A member that is called with '?.' is called on the container, and what it hands back is what the whole access gives.
            while (true)
            {
                if (current.Parent is IParenthesizedOperation or IConversionOperation { OperatorMethod: null })
                    current = current.Parent;
                else if (current.Parent is IConditionalAccessOperation access && ReferenceEquals(current, access.Operation) && FindConditionalAccessInstance(access) is { } instance)
                    current = instance;
                else
                    break;
            }

            switch (current.Parent)
            {
                // What the getter of an indexer makes of its argument. A constructor of a ref struct makes the ref struct.
                case IArgumentOperation { Parent: IPropertyReferenceOperation indexed } byIndex:
                    made = indexed;
                    return DescribeReferenceHandedBack(byIndex.Parameter, indexed.Property, indexed.Type) ??
                        types.DescribeWhatIsHandedBack(indexed.Property, indexed.Type, holder, isArgument: true);

                case IArgumentOperation { Parent: IObjectCreationOperation { Type.IsRefLikeType: true } creation }:
                    made = creation;
                    return MadeOfContainer.RefStructView;

                case IArgumentOperation { Parameter: { } receiving, Parent: IInvocationOperation call }:
                    made = call;

                    if (receiving.RefKind is not (RefKind.None or RefKind.Out) && call.TargetMethod is { ReturnsByRef: true } or { ReturnsByRefReadonly: true })
                        return types.IsContainer(call.Type) ? MadeOfContainer.UnderAnotherName : MadeOfContainer.Reference;

                    return types.DescribeWhatIsHandedBack(call.TargetMethod, call.Type, holder, isArgument: true);

                // The receiver of a member of an extension block is an argument of the member.
                case IInvocationOperation call when ReferenceEquals(current, call.Instance):
                    made = call;
                    return DescribeReferenceHandedBack(GetReceiverParameter(call.TargetMethod), call.TargetMethod, call.Type) ??
                        types.DescribeWhatIsHandedBack(call.TargetMethod, call.Type, holder, isArgument: call.TargetMethod.ContainingType is { IsExtension: true });

                case IPropertyReferenceOperation property when ReferenceEquals(current, property.Instance):
                    made = property;
                    return DescribeReferenceHandedBack(GetReceiverParameter(property.Property), property.Property, property.Type) ??
                        types.DescribeWhatIsHandedBack(property.Property, property.Type, holder, isArgument: property.Property.ContainingType is { IsExtension: true });

                // A user-defined operator or conversion is a call of its method.
                case IConversionOperation { OperatorMethod: { } operatorMethod } conversion:
                    made = conversion;
                    return types.DescribeWhatIsHandedBack(operatorMethod, conversion.Type, holder, isArgument: true);

                case IUnaryOperation { OperatorMethod: { } unaryOperator } unary:
                    made = unary;
                    return types.DescribeWhatIsHandedBack(unaryOperator, unary.Type, holder, isArgument: true);

                case IBinaryOperation { OperatorMethod: { } binaryOperator } binary:
                    made = binary;
                    return types.DescribeWhatIsHandedBack(binaryOperator, binary.Type, holder, isArgument: true);

                // A compound assignment and an increment store what their operator returns in the target, and give it as their
                // value. An operator that is an instance member returns nothing.
                case ICompoundAssignmentOperation { OperatorMethod: { } compoundOperator } compound:
                    made = compound;
                    return types.DescribeWhatIsHandedBack(compoundOperator, compoundOperator.ReturnType, holder, isArgument: true);

                case IIncrementOrDecrementOperation { OperatorMethod: { } incrementOperator } increment:
                    made = increment;
                    return types.DescribeWhatIsHandedBack(incrementOperator, incrementOperator.ReturnType, holder, isArgument: true);

                // The conversion that '??' applies to its left side makes what the '??' gives.
                case ICoalesceOperation { ValueConversion.MethodSymbol: { } converting } coalesce when ReferenceEquals(current, coalesce.Value):
                    made = coalesce;
                    return types.DescribeWhatIsHandedBack(converting, coalesce.Type, holder, isArgument: true);

                // 'with' gives a copy of what it is applied to, which holds what that holds.
                case IWithOperation with when ReferenceEquals(current, with.Operand):
                    made = with;
                    return MadeOfContainer.UnderAnotherName;

                // A task of a Memory<T> is judged like the memory, and so is what an 'await' of it gives.
                case IAwaitOperation awaited when NonCopyableTypes.IsSpanOrMemory(awaited.Type) && types.IsContainer(awaited.Type):
                    made = awaited;
                    return MadeOfContainer.UnderAnotherName;

                // What a member that the compiler binds hands back is used by the construct that binds it, and not followed:
                // where that can hold the container, the use is not followed. Where no member is judged, nothing is made that
                // the caller follows.
                default:
                    // What passes the container on, as a range does through its Slice, is followed as that.
                    if (member is null || GetNextThatPassesOn(current) is not null)
                        return MadeOfContainer.Nothing;

                    var result = member switch {
                        IMethodSymbol method => method.ReturnType,
                        IPropertySymbol property => property.Type,
                        _ => null,
                    };

                    return types.DescribeWhatIsHandedBack(member, result, holder, isArgument: true) is MadeOfContainer.Nothing ? MadeOfContainer.Nothing : MadeOfContainer.NotFollowed;
            }

            // A member that is given the variable by reference and returns a reference can return the one that it was given.
            MadeOfContainer? DescribeReferenceHandedBack(IParameterSymbol? receiving, ISymbol called, ITypeSymbol? type)
            {
                bool returnsReference = called is IMethodSymbol { ReturnsByRef: true } or IMethodSymbol { ReturnsByRefReadonly: true } or
                    IPropertySymbol { ReturnsByRef: true } or IPropertySymbol { ReturnsByRefReadonly: true };

                if (receiving is not { RefKind: not (RefKind.None or RefKind.Out) } || !returnsReference)
                    return null;

                return types.IsContainer(type) ? MadeOfContainer.UnderAnotherName : MadeOfContainer.Reference;
            }
        }

        /// <summary>
        /// Says what the parameter that receives a container does with it: see <see cref="NonCopyableTypes.JudgeReceiving"/>, which
        /// a method group and an inherited member ask as well, by the declaration alone.
        /// </summary>
        /// <param name="receiving">The parameter that receives the container.</param>
        /// <param name="member">The member that it is passed to.</param>
        /// <param name="argument">The argument that passes it, or null for an operand and for the receiver of a member of an extension block.</param>
        /// <param name="made">What the call makes of the container.</param>
        /// <remarks>
        /// A call adds two things to the declaration. The collection that the compiler makes for a 'params' parameter whose elements
        /// are of a type parameter is passing to a type parameter. And a pure move is no keeping: what goes into its [MovedFrom]
        /// parameter comes out as its result.
        /// </remarks>
        private ReceivedContainer JudgeWhatReceives(IParameterSymbol receiving, ISymbol member, IArgumentOperation? argument, MadeOfContainer made) => types.JudgeReceiving(
            receiving,
            member,
            made,
            handsBackThroughOut: argument is not null && HandsBackThroughOut(argument),
            isElementOfTypeParameter: argument is not null && IsParamsCollectionOfTypeParameter(argument, receiving),
            isPureMove: argument is not null && IsMovedByPureMove(argument));

        // A call that hands a container back through an 'out' argument is for that, as 'MemoryMarshal.TryGetArray' is: what it
        // hands back is followed.
        private bool HandsBackThroughOut(IArgumentOperation argument) =>
            argument.Parent is IInvocationOperation call &&
            call.Arguments.Any(other => other.Parameter is { RefKind: RefKind.Out } && types.IsContainer(other.Value.Type));

        /// <summary>
        /// Collects every member that the operations carry which a container goes through and into, from where it is given to
        /// what takes it: each with the parameter of the member that receives the container, and with the operation that gives it
        /// there. The parameter is null for a member of the container's own, or of what such a member hands out: that takes the
        /// container as 'this'.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This is the one place that knows what the compiler binds to an operation. The list of what a parameter may be used
        /// for, and that of what leaves a local the method's own, name kinds of operation, and a kind that runs code of the
        /// project with the container, where nothing but the binding shows it, would be allowed by its kind. So for every kind
        /// on the list of <see cref="CarriedSymbols"/>, this reads every member that the operation and its semantic information
        /// carry, and both checks judge each as the passing that it is. A test holds the list against the compiler's API.
        /// </para>
        /// <para>
        /// The members: a user-defined operator or conversion, also the one that a '??' applies to its left side, that a
        /// compound assignment applies to its target and that a deconstruction, a 'foreach' or a spread applies to an element;
        /// an operator that is an instance member, which takes the variable as its receiver; the method or the property that is
        /// used on the container, which takes it as a parameter where it is one of an extension; the members for a length, an
        /// index and a slice behind a range or an index from the end; 'GetAwaiter', 'IsCompleted', 'OnCompleted' and 'GetResult'
        /// of an 'await'; 'GetEnumerator' or 'GetAsyncEnumerator', 'MoveNext', 'Current' and 'Dispose' of a 'foreach' and of a
        /// spread; 'Deconstruct' of a deconstruction and of a positional pattern; the members of a property pattern and of a list
        /// pattern; 'operator true' and 'operator false' of '&amp;&amp;' and '||'; and the constructor of an interpolated-string
        /// handler that an argument or the receiver of a call is passed on to through [InterpolatedStringHandlerArgument].
        /// </para>
        /// <para>
        /// An argument is judged where it stands, by the parameter that receives it. The builder and the 'Add' of a collection
        /// expression take its elements, which are kept whatever those do.
        /// </para>
        /// </remarks>
        private void CollectCarriedMembers(IOperation from, List<(ISymbol Member, IParameterSymbol? Receiving, IOperation Operand)> members)
        {
            for (var operand = from; ; )
            {
                CollectCarriedBy(from, operand, members);

                if (GetNextThatPassesOn(operand) is not { } next)
                    return;

                operand = next;
            }
        }

        private void CollectCarriedBy(IOperation from, IOperation operand, List<(ISymbol Member, IParameterSymbol? Receiving, IOperation Operand)> members)
        {
            if (operand.Parent is not { SemanticModel: { } model } user)
                return;

            switch (user)
            {
                case IAwaitOperation { Syntax: AwaitExpressionSyntax awaitSyntax }:
                    var awaitInfo = model.GetAwaitExpressionInfo(awaitSyntax);
                    Add(awaitInfo.GetAwaiterMethod);
                    Add(awaitInfo.IsCompletedProperty);
                    Add(awaitInfo.GetResultMethod);

                    // The continuation is given to the awaiter through one of the two members of its interfaces.
                    if (awaitInfo.GetAwaiterMethod?.ReturnType is { } awaiter)
                    {
                        Add(FindMember(awaiter, "UnsafeOnCompleted"));
                        Add(FindMember(awaiter, WellKnownMemberNames.OnCompleted));
                    }

                    break;

                case IForEachLoopOperation { Syntax: CommonForEachStatementSyntax loopSyntax } loop when ReferenceEquals(operand, loop.Collection):
                    AddOfLoop(model.GetForEachStatementInfo(loopSyntax));

                    // The elements of a parameter that holds containers are containers that the caller put in.
                    if (loopSyntax is ForEachVariableStatementSyntax takenApart)
                        AddOfDeconstruction(model.GetDeconstructionInfo(takenApart), withNested: true);

                    break;

                // A spread enumerates its operand as a 'foreach' at its place would, and converts each element.
                case ISpreadOperation spread when ReferenceEquals(operand, spread.Operand):
                    if (operand.Syntax is ExpressionSyntax spreadSyntax && GetForEachInfoAt(model, spreadSyntax.SpanStart, spreadSyntax) is { } spreadInfo)
                        AddOfLoop(spreadInfo);

                    AddConversionOfElement(spread.ElementConversion.MethodSymbol);
                    break;

                case IDeconstructionAssignmentOperation { Syntax: AssignmentExpressionSyntax assignmentSyntax } deconstruction when ReferenceEquals(operand, deconstruction.Value):
                    DeconstructionInfo? info = model.GetDeconstructionInfo(assignmentSyntax);
                    var path = GetPathThroughTuples(from, deconstruction);

                    // Down to the element of a tuple literal that the container stands in, which a nested target takes apart or
                    // converts. Where the value is no literal, nothing says which element the container is: every one counts.
                    foreach (int index in path)
                        info = info is { Nested: var nested } && index >= 0 && index < nested.Length ? nested[index] : null;

                    if (info is { } found)
                        AddOfDeconstruction(found, withNested: path.Count is 0);

                    break;

                case IIsPatternOperation test when ReferenceEquals(operand, test.Value):
                    AddOfPattern(test.Pattern);
                    break;

                case ISwitchExpressionOperation switchExpression when ReferenceEquals(operand, switchExpression.Value):
                    foreach (var arm in switchExpression.Arms)
                        AddOfPattern(arm.Pattern);

                    break;

                case ISwitchOperation switchStatement when ReferenceEquals(operand, switchStatement.Value):
                    foreach (var section in switchStatement.Cases)
                    {
                        foreach (var clause in section.Clauses)
                            AddOfPattern((clause as IPatternCaseClauseOperation)?.Pattern);
                    }

                    break;

                // A user-defined operator or conversion is a call of its method, and what it takes is an argument of that.
                case IUnaryOperation unary:
                    AddOperator(unary.OperatorMethod, 0);
                    break;

                case IConversionOperation conversion:
                    AddOperator(conversion.OperatorMethod, 0);
                    break;

                case IBinaryOperation binary:
                    AddOperator(binary.OperatorMethod, ReferenceEquals(operand, binary.LeftOperand) ? 0 : 1);

                    // 'left && right' with a user-defined operator tests the left side with 'operator false' first, and '||'
                    // with 'operator true'. The operation does not name that operator. It is declared where the operator itself is.
                    if (binary is { OperatorKind: BinaryOperatorKind.ConditionalAnd or BinaryOperatorKind.ConditionalOr, OperatorMethod: { } logicalOperator } &&
                        ReferenceEquals(operand, binary.LeftOperand))
                    {
                        string name = binary.OperatorKind is BinaryOperatorKind.ConditionalAnd ? WellKnownMemberNames.FalseOperatorName : WellKnownMemberNames.TrueOperatorName;

                        if (FindOperator(logicalOperator.ContainingType, name) is { Parameters: [var tested] } shortCircuit)
                            members.Add((shortCircuit, tested, operand));
                    }

                    break;

                // '??' converts its left side with a conversion that is no operation of its own.
                case ICoalesceOperation coalesce when ReferenceEquals(operand, coalesce.Value):
                    AddOperator(coalesce.ValueConversion.MethodSymbol, 0);
                    break;

                // A compound assignment passes what its target held to the operator, after a conversion of its own where the
                // operator takes another type, and its right side as the next operand. An operator that is an instance member
                // (C# 14) runs on the target, which it takes as its receiver where it is one of an extension block.
                case ICompoundAssignmentOperation compound:
                    if (ReferenceEquals(operand, compound.Target))
                    {
                        AddOperator(compound.OperatorMethod, 0);
                        AddOperator(compound.InConversion.MethodSymbol, 0);
                    }
                    else
                    {
                        AddOperator(compound.OperatorMethod, 1);
                    }

                    break;

                case IIncrementOrDecrementOperation increment:
                    AddOperator(increment.OperatorMethod, 0);
                    break;

                // A range or an index from the end binds a member for the length and one for the index or the slice, which C# 14
                // also finds in an extension.
                case IImplicitIndexerReferenceOperation implicitIndexer when ReferenceEquals(operand, implicitIndexer.Instance):
                    Add(implicitIndexer.LengthSymbol);
                    Add(implicitIndexer.IndexerSymbol);
                    break;

                // A delegate for a method that is called on the container carries the container to that method.
                case IMethodReferenceOperation methodReference when ReferenceEquals(operand, methodReference.Instance):
                    Add(methodReference.Method);
                    break;

                // An argument, and the receiver of a call, that the call also passes to the constructor of a handler. The
                // argument itself is judged where it stands, by its parameter.
                case IArgumentOperation { Parent: var call } argument:
                    var arguments = call switch {
                        IInvocationOperation invocation => invocation.Arguments,
                        IObjectCreationOperation creation => creation.Arguments,
                        IPropertyReferenceOperation indexed => indexed.Arguments,
                        _ => ImmutableArray<IArgumentOperation>.Empty,
                    };

                    AddHandlerConstructors(arguments, arguments.IndexOf(argument));
                    break;

                // The member that is used on the container. One of an extension block takes the container as its receiver.
                case IInvocationOperation onReceiver when ReferenceEquals(operand, onReceiver.Instance):
                    Add(onReceiver.TargetMethod);
                    AddHandlerConstructors(onReceiver.Arguments, -1);
                    break;

                case IPropertyReferenceOperation ofReceiver when ReferenceEquals(operand, ofReceiver.Instance):
                    Add(ofReceiver.Property);

                    if (!ofReceiver.Arguments.IsEmpty)
                        AddHandlerConstructors(ofReceiver.Arguments, -1);

                    break;
            }

            void Add(ISymbol? member)
            {
                if (member is not null)
                    members.Add((member, GetReceiverParameter(member), operand));
            }

            // The parameter of an operator that takes the operand at a position. An operator that is an instance member runs on
            // its first operand, and has one parameter for the next.
            void AddOperator(IMethodSymbol? method, int position)
            {
                if (method is null)
                    return;

                if (!method.IsStatic)
                {
                    if (position is 0)
                        Add(method);
                    else if (position <= method.Parameters.Length)
                        members.Add((method, method.Parameters[position - 1], operand));
                }
                else if (position < method.Parameters.Length)
                {
                    members.Add((method, method.Parameters[position], operand));
                }
            }

            // A conversion of an element takes a container where the elements are containers: each one is a container that
            // the caller put in.
            void AddConversionOfElement(IMethodSymbol? method)
            {
                if (method is { Parameters: [var converted] } && types.IsContainer(converted.Type))
                    members.Add((method, converted, operand));
            }

            void AddOfLoop(ForEachStatementInfo loopInfo)
            {
                Add(loopInfo.GetEnumeratorMethod);
                Add(loopInfo.MoveNextMethod);
                Add(loopInfo.CurrentProperty);
                Add(loopInfo.DisposeMethod);
                AddConversionOfElement(loopInfo.CurrentConversion.MethodSymbol);
                AddConversionOfElement(loopInfo.ElementConversion.MethodSymbol);
            }

            // The method that takes the value apart, and the conversion that is applied to it where a target is of another type.
            void AddOfDeconstruction(DeconstructionInfo deconstructionInfo, bool withNested)
            {
                RuntimeHelpers.EnsureSufficientExecutionStack();
                Add(deconstructionInfo.Method);

                if (deconstructionInfo.Conversion is { MethodSymbol: { } converting })
                {
                    if (withNested)
                        AddConversionOfElement(converting);
                    else
                        AddOperator(converting, 0);
                }

                if (!withNested)
                    return;

                foreach (var nestedInfo in deconstructionInfo.Nested)
                    AddOfDeconstruction(nestedInfo, withNested: true);
            }

            void AddOfPattern(IPatternOperation? pattern)
            {
                RuntimeHelpers.EnsureSufficientExecutionStack();

                switch (pattern)
                {
                    // Every pattern inside a pattern binds its members too: the container can reach one through a tuple, or through
                    // what a member of it hands back. A member whose receiver is not the container judges as used.
                    case IRecursivePatternOperation recursive:
                        Add(recursive.DeconstructSymbol);

                        foreach (var part in recursive.DeconstructionSubpatterns)
                            AddOfPattern(part);

                        foreach (var subpattern in recursive.PropertySubpatterns)
                        {
                            // 'A.B: pattern' reads 'B' of what 'A' of the value hands out: every member of the chain is bound.
                            for (var member = subpattern.Member; member is IMemberReferenceOperation reference; member = reference.Instance)
                                Add((reference as IPropertyReferenceOperation)?.Property);

                            AddOfPattern(subpattern.Pattern);
                        }

                        break;

                    case IListPatternOperation list:
                        Add(list.LengthSymbol);
                        Add(list.IndexerSymbol);

                        foreach (var element in list.Patterns)
                        {
                            if (element is ISlicePatternOperation slice)
                            {
                                Add(slice.SliceSymbol);
                                AddOfPattern(slice.Pattern);
                            }
                            else
                            {
                                AddOfPattern(element);
                            }
                        }

                        break;

                    case INegatedPatternOperation negated:
                        AddOfPattern(negated.Pattern);
                        break;

                    case IBinaryPatternOperation binary:
                        AddOfPattern(binary.LeftPattern);
                        AddOfPattern(binary.RightPattern);
                        break;
                }
            }

            // The placeholders in the creation of a handler say which argument of the call, or its receiver, the constructor is given.
            void AddHandlerConstructors(ImmutableArray<IArgumentOperation> arguments, int index)
            {
                foreach (var other in arguments)
                {
                    var value = other.Value;

                    while (value is IConversionOperation { OperatorMethod: null } conversion)
                        value = conversion.Operand;

                    if (value is not IInterpolatedStringHandlerCreationOperation { HandlerCreation: IObjectCreationOperation { Constructor: { } constructor } creation })
                        continue;

                    foreach (var passed in creation.Arguments)
                    {
                        var placeholder = passed.Value;

                        while (placeholder is IConversionOperation { OperatorMethod: null } conversion)
                            placeholder = conversion.Operand;

                        if (placeholder is IInterpolatedStringHandlerArgumentPlaceholderOperation { PlaceholderKind: var kind, ArgumentIndex: var at } &&
                            passed.Parameter is { } taking &&
                            (index < 0
                                ? kind is InterpolatedStringArgumentPlaceholderKind.CallsiteReceiver
                                : kind is InterpolatedStringArgumentPlaceholderKind.CallsiteArgument && at == index))
                        {
                            members.Add((constructor, taking, operand));
                        }
                    }
                }
            }

            // An operator of a type, or of an extension block of the class that declares the other operator.
            static IMethodSymbol? FindOperator(INamedTypeSymbol? declaring, string name)
            {
                for (var type = declaring; type is not null; type = type.BaseType)
                {
                    if (type.GetMembers(name).OfType<IMethodSymbol>().FirstOrDefault() is { } found)
                        return found;
                }

                if (declaring is { IsExtension: true, ContainingType: { } container })
                {
                    foreach (var block in container.GetTypeMembers())
                    {
                        if (block.IsExtension && block.GetMembers(name).OfType<IMethodSymbol>().FirstOrDefault() is { } found)
                            return found;
                    }
                }

                return null;
            }

            static ISymbol? FindMember(ITypeSymbol type, string name)
            {
                for (ITypeSymbol? current = type; current is not null; current = current.BaseType)
                {
                    if (current.GetMembers(name).FirstOrDefault(static member => !member.IsStatic) is { } found)
                        return found;
                }

                return null;
            }
        }

        /// <summary>
        /// Gets the parameter of a member that receives what the member is used on: the first parameter of an extension method, and
        /// the receiver of a member of an extension block. Null for a member of the type itself, which is used on 'this'.
        /// </summary>
        private static IParameterSymbol? GetReceiverParameter(ISymbol member)
        {
            if (member is IMethodSymbol method)
            {
                var unreduced = method.GetConstructedReducedFrom() ?? method;

                if (unreduced is { IsExtensionMethod: true, Parameters: [var first, ..] })
                    return first;
            }

            return NonCopyableTypes.GetExtensionReceiver(member);
        }

        // The way from the value of a deconstruction down to the element of its tuple literal that a reference stands in.
        private static List<int> GetPathThroughTuples(IOperation reference, IDeconstructionAssignmentOperation deconstruction)
        {
            var path = new List<int>();

            for (var current = reference; current.Parent is { } parent && !ReferenceEquals(parent, deconstruction); current = parent)
            {
                if (parent is ITupleOperation tuple)
                    path.Insert(0, tuple.Elements.IndexOf(current));
            }

            return path;
        }

        /// <summary>
        /// Gets the first member that a use of a local carries and that keeps the local which it is given, with the parameter that
        /// receives it. Null if there is none.
        /// </summary>
        private (ISymbol Member, IParameterSymbol Receiving)? FindBoundMemberThatKeeps(IOperation reference)
        {
            var members = new List<(ISymbol Member, IParameterSymbol? Receiving, IOperation Operand)>();
            CollectCarriedMembers(reference, members);

            foreach (var (member, receiving, operand) in members)
            {
                if (receiving is null)
                    continue;

                // A member that hands back what this does not follow is one that the local goes to as well.
                var made = DescribeWhatIsMade(operand, operand.Type, out _, member);

                if (made is MadeOfContainer.NotFollowed || JudgeWhatReceives(receiving, member, null, made) is not ReceivedContainer.Used)
                    return (member, receiving);
            }

            return null;
        }

        /// <summary>
        /// Gets a value indicating whether what is made of a container is used where it is made, so that nothing of it is left:
        /// a member is used on it, or it is awaited, enumerated or tested.
        /// </summary>
        private static bool IsUsedWhereItIsMade(IOperation made)
        {
            IOperation current = made;

            // What a call behind '?.' makes is what the whole access gives.
            while (current.Parent is IParenthesizedOperation or IConversionOperation { OperatorMethod: null } ||
                (current.Parent is IConditionalAccessOperation access && ReferenceEquals(current, access.WhenNotNull)))
            {
                current = current.Parent;
            }

            return current.Parent switch {
                IInvocationOperation invocation => ReferenceEquals(current, invocation.Instance),
                IMemberReferenceOperation member => ReferenceEquals(current, member.Instance),
                IArrayElementReferenceOperation element => ReferenceEquals(current, element.ArrayReference),
                IForEachLoopOperation loop => ReferenceEquals(current, loop.Collection),
                IConditionalAccessOperation access => ReferenceEquals(current, access.Operation),
                IAwaitOperation or IIsPatternOperation or IExpressionStatementOperation or ISpreadOperation => true,
                IBinaryOperation { OperatorMethod: null } => true,
                _ => false,
            };
        }
    }
}

/// <summary>
/// What a use makes of a container.
/// </summary>
internal enum MadeOfContainer
{
    /// <summary>
    /// Nothing that holds the container: no result, a value that carries nothing of it, an element of it, or a new container.
    /// </summary>
    Nothing,

    /// <summary>
    /// A reference into the container, which lives no longer than the method.
    /// </summary>
    Reference,

    /// <summary>
    /// A view of it that is a ref struct, as a span is, which lives no longer than the method either.
    /// </summary>
    RefStructView,

    /// <summary>
    /// The container under another name: a view of it that is no ref struct, as a Memory&lt;T&gt; and an
    /// ArraySegment&lt;T&gt; are, the container as another type, or a reference to the variable itself that a callee hands
    /// back. Whatever holds it holds the container.
    /// </summary>
    UnderAnotherName,

    /// <summary>
    /// What a member hands back that can hold the container, through an operation of a kind that does not follow it. The use
    /// is reported as one that is not followed.
    /// </summary>
    NotFollowed,
}

/// <summary>
/// What a parameter does with a container that it receives.
/// </summary>
internal enum ReceivedContainer
{
    /// <summary>
    /// It only uses the container during the call.
    /// </summary>
    Used,

    /// <summary>
    /// It keeps the container: by its mark, by its kind, or by having a type parameter as its type.
    /// </summary>
    Kept,

    /// <summary>
    /// It is taken to keep the container: its type mentions a type parameter that is not marked, so nothing checks what the member
    /// does with the container, and the list does not say that the member only borrows.
    /// </summary>
    KeptByUncheckedCode,

    /// <summary>
    /// The same of a member that is not listed, whose use is reported as that wherever it stands.
    /// </summary>
    KeptByUnlistedCode,
}
