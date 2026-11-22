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
/// The walker over the operations of a member: its state, the visit of every kind of operation with the general check of a value
/// that is used where it may be copied, what marks an operation as handled, and how a finding is reported. The rules that the visits
/// apply are in the partial files beside this one, one file for each concept.
/// </content>
public sealed partial class DoNotCopyValueAnalyzer
{
    private sealed partial class NonCopyableWalker(OperationBlockAnalysisContext context, NonCopyableTypes types, MoveTrust moves, bool allowsImplicitMoves)
        : OperationWalker
    {
        // What uses an operation has judged its value: the visit of the operation itself does not report it again. An operation is
        // handled from where a visit marks it to the end of that visit.
        private readonly HashSet<IOperation> _handledOperations = [];

        // Every operation of the member, once something has asked for them.
        private List<IOperation>? _allOperations;

        // The handled operations in the order in which they were marked, for the scopes that take them back.
        private readonly List<IOperation> _handledInOrder = [];

        /// <summary>
        /// Reports what can only be known once every operation of the member has been visited.
        /// </summary>
        public void Complete()
        {
            // A method without a body, such as an abstract one, still has blocks if its parameters have default values or attributes.
            if (context.OwningSymbol is IMethodSymbol { IsAbstract: false, IsExtern: false } method &&
                context.OperationBlocks.FirstOrDefault(b => b.Kind is OperationKind.Block or OperationKind.MethodBody or OperationKind.ConstructorBody) is { } body)
            {
                CheckMovedFromParameters(method, body, isLocal: false);
            }
        }

        public override void Visit(IOperation? operation)
        {
            // This runs at every level of the tree, and a long 'else if' chain is as deep as it is long: what is done for the rare
            // operation of no kind is in a function of its own, so that this one takes little room on the stack.
            if (operation is { Kind: OperationKind.None })
                VisitOperationOfNoKind(operation);
            else
                base.Visit(operation);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void VisitOperationOfNoKind(IOperation operation)
        {
            // 'stackalloc' has no operation of its own: its size and the values of its initializer are the children of an operation of no
            // kind, which no method of the walker is called for. New values are moved into the memory, as into an array. Anything else
            // is reported where it is visited.
            using var handled = Handle();

            if (operation is { Syntax: StackAllocArrayCreationExpressionSyntax or ImplicitStackAllocArrayCreationExpressionSyntax })
                handled.AddNewValues(operation.ChildOperations);

            // Nor has the call of GetPinnableReference that a 'fixed' statement makes on a value. The method runs on the value in place,
            // and through a read-only reference on a copy unless it is readonly.
            IOperation? pinned = null;

            if (operation is { IsImplicit: true, Type.TypeKind: TypeKind.Pointer, Parent: IVariableInitializerOperation } &&
                operation.Syntax.FirstAncestorOrSelf<FixedStatementSyntax>() is not null &&
                operation.ChildOperations.FirstOrDefault() is { Type: INamedTypeSymbol { TypeKind: TypeKind.Struct } pinnedType } operand &&
                types.IsNonCopyableType(pinnedType) &&
                pinnedType.GetMembers("GetPinnableReference").OfType<IMethodSymbol>().FirstOrDefault(m => !m.IsStatic && m.Parameters.IsEmpty) is { } pin &&
                (!IsCalledOnCopy(operand, pinnedType, pin.IsReadOnly) || ReportMemberOnCopy(operand, pin)))
            {
                pinned = operand;
            }

            handled.Add(pinned);

            // Roslyn's walker takes an operation of no kind straight to its children, past DefaultVisit: the check of a kind that nobody has
            // decided about is made here.
            CheckTypeInUnsupportedContext(operation);

            base.Visit(operation);
        }

        /// <summary>
        /// Makes the general check for the kinds of operation that need nothing else, and goes on to the children.
        /// </summary>
        /// <remarks>
        /// An operation gets here if its kind has no Visit method of its own in this class, and also from every Visit method that
        /// calls its base, whose kinds are therefore not on the list below.
        /// </remarks>
        public override void DefaultVisit(IOperation operation)
        {
            if (IsCheckedByTypeAlone(operation))
                CheckTypeInUnsupportedContext(operation);

            base.DefaultVisit(operation);
        }

        /// <summary>
        /// Gets a value indicating whether an operation is of a kind for which the general check of its type is all that is done: a value of
        /// a non-copyable type that it gives is reported unless what uses it allows it.
        /// </summary>
        /// <remarks>
        /// This is the one list of the kinds that are decided. The first group needs nothing but the check. The kinds of the second have
        /// a Visit method of their own, which checks them, and those of the third give no value of their own. Every other kind is one that
        /// nobody has decided about, and gets the check: an operation of no kind ('__refvalue', a pointer dereference), the call of a
        /// function pointer, and whatever a later compiler adds. Used in place it is what it is; used by value it is a copy of something
        /// that the analyzer cannot see the owner of, which Acquire takes for a stored value.
        /// </remarks>
        private static bool IsCheckedByTypeAlone(IOperation operation) => operation.Kind switch {
            OperationKind.ArrayCreation or
            OperationKind.Block or
            OperationKind.Branch or
            OperationKind.CatchClause or
            OperationKind.CaughtException or
            OperationKind.ConditionalAccessInstance or
            OperationKind.ConstantPattern or
            OperationKind.ConstructorBody or
            OperationKind.DeclarationPattern or
            OperationKind.Discard or
            OperationKind.DiscardPattern or
            OperationKind.DynamicIndexerAccess or
            OperationKind.DynamicInvocation or
            OperationKind.DynamicMemberReference or
            OperationKind.DynamicObjectCreation or
            OperationKind.Empty or
            OperationKind.FlowAnonymousFunction or
            OperationKind.FlowCapture or
            OperationKind.FlowCaptureReference or
            OperationKind.InstanceReference or
            OperationKind.InterpolatedString or
            OperationKind.InterpolatedStringText or
            OperationKind.IsNull or
            OperationKind.Labeled or
            OperationKind.Literal or
            OperationKind.MemberInitializer or
            OperationKind.MethodBody or
            OperationKind.ObjectOrCollectionInitializer or
            OperationKind.ParameterInitializer or
            OperationKind.Parenthesized or
            OperationKind.Range or
            OperationKind.SizeOf or
            OperationKind.SwitchCase or
            OperationKind.SwitchExpressionArm or
            OperationKind.Throw or
            OperationKind.TranslatedQuery or
            OperationKind.Try or
            OperationKind.TupleBinary or
            OperationKind.TypeOf or
            OperationKind.TypeParameterObjectCreation or
            OperationKind.VariableDeclarationGroup or
            OperationKind.VariableInitializer => true,

            // 'for' and 'while' share their kind with 'foreach', which has a Visit method of its own.
            OperationKind.Loop => operation is ILoopOperation { LoopKind: LoopKind.For or LoopKind.While },

            // The three kinds of clause that a 'switch' statement of C# has.
            OperationKind.CaseClause => operation is ICaseClauseOperation { CaseKind: CaseKind.SingleValue or CaseKind.Pattern or CaseKind.Default },

            // The kinds that have a Visit method of their own, which checks them. 'foreach' is a loop of its own kind.
            OperationKind.AddressOf or
            OperationKind.AnonymousFunction or
            OperationKind.AnonymousObjectCreation or
            OperationKind.Argument or
            OperationKind.ArrayElementReference or
            OperationKind.ArrayInitializer or
            OperationKind.Await or
            OperationKind.Binary or
            OperationKind.Coalesce or
            OperationKind.CoalesceAssignment or
            OperationKind.CollectionExpression or
            OperationKind.CompoundAssignment or
            OperationKind.Conditional or
            OperationKind.ConditionalAccess or
            OperationKind.Conversion or
            OperationKind.DeclarationExpression or
            OperationKind.Decrement or
            OperationKind.DeconstructionAssignment or
            OperationKind.DefaultValue or
            OperationKind.DelegateCreation or
            OperationKind.EventAssignment or
            OperationKind.EventReference or
            OperationKind.ExpressionStatement or
            OperationKind.FieldInitializer or
            OperationKind.FieldReference or
            OperationKind.ImplicitIndexerReference or
            OperationKind.Increment or
            OperationKind.InlineArrayAccess or
            OperationKind.InterpolatedStringHandlerArgumentPlaceholder or
            OperationKind.Interpolation or
            OperationKind.Invalid or
            OperationKind.Invocation or
            OperationKind.IsPattern or
            OperationKind.IsType or
            OperationKind.LocalFunction or
            OperationKind.LocalReference or
            OperationKind.Lock or
            OperationKind.Loop or
            OperationKind.CaseClause or
            OperationKind.MethodReference or
            OperationKind.NameOf or
            OperationKind.ObjectCreation or
            OperationKind.ParameterReference or
            OperationKind.PropertyInitializer or
            OperationKind.PropertyReference or
            OperationKind.Return or
            OperationKind.YieldReturn or
            OperationKind.YieldBreak or
            OperationKind.SimpleAssignment or
            OperationKind.Spread or
            OperationKind.Switch or
            OperationKind.SwitchExpression or
            OperationKind.Tuple or
            OperationKind.Unary or
            OperationKind.Using or
            OperationKind.UsingDeclaration or
            OperationKind.VariableDeclaration or
            OperationKind.VariableDeclarator or
            OperationKind.With => false,

            // The kinds that give no value of their own: the parts of a pattern, which the check of patterns judges where they look into a
            // value, an attribute, and what the compiler makes of an interpolated string for a handler, whose calls and arguments are
            // visited as such and whose handler is a new value that the argument it is passed as judges.
            OperationKind.BinaryPattern or
            OperationKind.ListPattern or
            OperationKind.NegatedPattern or
            OperationKind.PropertySubpattern or
            OperationKind.RecursivePattern or
            OperationKind.RelationalPattern or
            OperationKind.SlicePattern or
            OperationKind.TypePattern or
            OperationKind.Attribute or
            OperationKind.InterpolatedStringAddition or
            OperationKind.InterpolatedStringAppendFormatted or
            OperationKind.InterpolatedStringAppendInvalid or
            OperationKind.InterpolatedStringAppendLiteral or
            OperationKind.InterpolatedStringHandlerCreation or
            OperationKind.Utf8String => false,

            // Nobody has decided about it.
            _ => true,
        };

        public override void VisitAddressOf(IAddressOfOperation operation)
        {
            CheckTypeInUnsupportedContext(operation);

            // A function pointer type carries no marks, so its callers pass anything to a parameter that the method takes to be moved from.
            if (operation.Reference is IMethodReferenceOperation { Method: var pointedTo })
            {
                foreach (var parameter in pointedTo.Parameters.Where(NonCopyableTypes.IsMovedFrom))
                {
                    Report(
                        Rules.MovedFromAdded,
                        operation.Syntax,
                        parameter.Name,
                        Display(pointedTo),
                        "a function pointer type, which carries no marks and which it is converted to,");
                }
            }

            base.VisitAddressOf(operation);
        }

        public override void VisitAnonymousFunction(IAnonymousFunctionOperation operation)
        {
            CheckTypeInUnsupportedContext(operation);
            base.VisitAnonymousFunction(operation);
            CheckMovedFromParameters(operation.Symbol, operation, isLocal: true);

            // The parameters of a lambda and of a local function have no symbol action.
            foreach (var parameter in operation.Symbol.Parameters)
                ReportNotNullRef(parameter, operation.Symbol, context.ReportDiagnostic, context.CancellationToken);
        }

        public override void VisitAnonymousObjectCreation(IAnonymousObjectCreationOperation operation)
        {
            CheckTypeInUnsupportedContext(operation);

            // The members of an anonymous type are auto-properties. Every written read of one is reported where it stands, but
            // nothing shows what the compiler writes for the type: its ToString, Equals and GetHashCode copy each member, and
            // 'with' makes a second object. So a member of a non-copyable type is reported where the object is created.
            foreach (var initializer in operation.Initializers)
            {
                if (initializer is ISimpleAssignmentOperation { Target: IPropertyReferenceOperation { Property: var property } } && types.IsNonCopyableType(property.Type))
                    Report(Rules.AnonymousTypeMember, initializer.Syntax, property.Type, property.Name);
            }

            base.VisitAnonymousObjectCreation(operation);
        }

        public override void VisitArgument(IArgumentOperation operation)
        {
            var value = operation.Value;

            // Roslyn's rule assumes that every argument has a parameter, which is not the case for an __arglist argument, and fails on it. Such an
            // argument is passed by value.
            var parameterRefKind = operation.Parameter?.RefKind ?? RefKind.None;

            // An argument for an 'in' or 'ref readonly' parameter that is not a variable is copied by the compiler into a temporary, and the
            // reference passed is one to that: the argument is passed by value, and the callee holds a second instance for the call.
            if (parameterRefKind is RefKind.In or RefKind.RefReadOnlyParameter && !IsVariable(value))
                parameterRefKind = RefKind.None;

            // An 'out' argument is written and not read. What the member writes to it is checked by the invocation. Roslyn's rule reports
            // every 'out' argument of a non-copyable type.
            if (parameterRefKind is not RefKind.Out && !CanAssign(Acquire(value), parameterRefKind))
            {
                // A stored value is copied into a by-value parameter, which is reported where the value is visited, unless a list says of the
                // member that it only reads the copy during the call.
                if (parameterRefKind is not RefKind.None || !IsArgumentOfBorrowingMember(operation))
                    value = null;
            }

            if (operation.Parameter is { } taking && types.IsContainer(operation.Value.Type) && NonCopyableTypes.IsMovedFrom(taking.OriginalDefinition))
            {
                // Clearing one variable says nothing about other references to the same container, so the move needs a local that the
                // method owns. A [MovedFrom] parameter is the method's own as well: its caller had to own what it passed.
                if (IsOwnedForMove(operation.Value, operation))
                    NoteOwnedParameter(operation.Value);
                else if (SkipParentheses(operation.Value) is IParameterReferenceOperation { Parameter: var slot } passed && NonCopyableTypes.IsMovedFrom(slot) && FindSlotGiver(slot, passed) is { } giver)
                    Report(Rules.ContainerMovedFromSlotParameter, operation.Value.Syntax, operation.Value.Type, slot.Name, giver);
                else
                    Report(
                        Rules.ContainerMovedFromUnowned,
                        operation.Value.Syntax,
                        DescribeContainer(operation.Value.Type),
                        operation.Value.Syntax.ToString(),
                        GetWhyNotOwn(operation.Value, out _) is { } why ? $", because {why}" : string.Empty);
            }

            // A [MovedFrom] parameter of a container type is left holding only what the caller can own. Something that it is passed to by
            // reference can assign anything to it, unless that is a move as well.
            // A read-only reference is a way to write as well when the call turns it into a reference that is not, as Unsafe.AsRef does.
            if (operation.Parameter is { } receiving &&
                (receiving.RefKind is RefKind.Ref or RefKind.Out ||
                    (receiving.RefKind is RefKind.In or RefKind.RefReadOnlyParameter && operation.Parent is IInvocationOperation { TargetMethod.ReturnsByRef: true })))
            {
                CheckMovedFromParameterHandedOn(operation, receiving, operation.Value);
            }

            using var handled = Handle();
            handled.Add(value);

            base.VisitArgument(operation);
        }

        public override void VisitArrayElementReference(IArrayElementReferenceOperation operation)
        {
            CheckTypeInUnsupportedContext(operation);

            // A range of an array ('array[1..]') gives a new array with a copy of every element, which the compiler makes with a call of
            // RuntimeHelpers.GetSubArray. It is a member use like any other, and that method is not one to list.
            if (operation.Type is IArrayTypeSymbol sliced && SymbolEqualityComparer.Default.Equals(sliced, operation.ArrayReference.Type))
                CheckMemberUse(operation.Syntax, GetSubArrayMethod(sliced.ElementType));

            base.VisitArrayElementReference(operation);
        }

        public override void VisitArrayInitializer(IArrayInitializerOperation operation)
        {
            CheckTypeInUnsupportedContext(operation);

            // New values are moved into the array, as into a parameter. Roslyn's rule reports every element that is not an object creation.
            using var handled = Handle();
            handled.AddNewValues(operation.ElementValues);
            base.VisitArrayInitializer(operation);
        }

        public override void VisitAwait(IAwaitOperation operation)
        {
            CheckTypeInUnsupportedContext(operation);
            AnalyzeAwait(operation, report: true);

            // The awaited value is read in place, through the members that the compiler calls on it, which AnalyzeAwait has checked.
            using var handled = Handle();
            handled.Add(operation.Operation);

            base.VisitAwait(operation);
        }

        public override void VisitBinaryOperator(IBinaryOperation operation)
        {
            var use = CheckMemberUse(operation.Syntax, operation.OperatorMethod);

            if (operation.OperatorMethod is null && operation.OperatorKind is BinaryOperatorKind.Add)
                CheckCombinedDelegates(operation.Syntax, operation.Type);

            // 'x && y' is 'operator false(x) ? x : operator &(x, y)': the left operand goes to 'operator false' by value and then on,
            // to 'operator &' or out as the result.
            if (operation is { OperatorMethod: { } logical, OperatorKind: BinaryOperatorKind.ConditionalAnd or BinaryOperatorKind.ConditionalOr } &&
                types.IsNonCopyableType(operation.LeftOperand.Type))
            {
                string test = operation.OperatorKind is BinaryOperatorKind.ConditionalAnd ? "false" : "true";
                Report(
                    Rules.ValuePassedTwice,
                    operation.LeftOperand.Syntax,
                    operation.LeftOperand.Type,
                    $"'operator {test}' and then to '{Display(logical)}' or on as the result");
            }

            // A user-defined operator is a call of its method: what it returns is the result of a call. A lifted operator is not judged.
            if (operation.OperatorMethod is not null && !operation.IsLifted)
            {
                bool operandsAreNew = AreCarriedValuesNew(operation.LeftOperand) && AreCarriedValuesNew(operation.RightOperand);
                CheckHandedOutValue(operation.Syntax, use, operation.Type, operandsAreNew);
            }

            CheckTypeInUnsupportedContext(operation);

            // A comparison with null reads no value: it is how a nullable non-copyable value is tested.
            IOperation? comparedWithNull = null;

            if (operation.OperatorMethod is null && operation.OperatorKind is BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals)
            {
                if (IsNullConstant(operation.RightOperand))
                    comparedWithNull = operation.LeftOperand;
                else if (IsNullConstant(operation.LeftOperand))
                    comparedWithNull = operation.RightOperand;
            }

            using var handled = Handle();
            handled.Add(comparedWithNull);

            var operatorMethod = operation.OperatorMethod;
            handled.Add(CheckOperatorOperand(operatorMethod, 0, operation.LeftOperand));
            handled.Add(CheckOperatorOperand(operatorMethod, 1, operation.RightOperand));

            base.VisitBinaryOperator(operation);
        }

        public override void VisitCollectionExpression(ICollectionExpressionOperation operation)
        {
            // The method that the compiler creates the collection with is a member use: a constructor or the create method of a collection
            // builder. After a constructor it calls Add for each element, which is a member use as well.
            var constructMethod = operation.ConstructMethod;
            CheckMemberUse(operation.Syntax, constructMethod);

            if (constructMethod is { MethodKind: MethodKind.Constructor } && operation.Type is { } collectionType && types.Carries(collectionType))
            {
                foreach (var add in GetAddMethods(operation, collectionType))
                {
                    CheckMemberUse(operation.Syntax, add);
                    CheckReceiverType(operation.Syntax, collectionType, add);
                }
            }

            // New values are moved into the collection, as into a parameter.
            using var handled = Handle();
            handled.AddNewValues(operation.Elements);
            base.VisitCollectionExpression(operation);
        }

        public override void VisitCoalesce(ICoalesceOperation operation)
        {
            CheckTypeInUnsupportedContext(operation);

            // The value is converted to the type of the result with no conversion in the tree: a user-defined conversion is a member
            // that is used, and any other is judged as a conversion node would be.
            var valueConversion = operation.ValueConversion;
            CheckMemberUse(operation.Syntax, valueConversion.MethodSymbol);

            if (valueConversion is { Exists: true, IsIdentity: false, MethodSymbol: null } && operation.Value.Type is { } valueType)
            {
                var convertedType = valueType is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable &&
                    operation.Type is not INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T }
                        ? nullable.TypeArguments[0]
                        : valueType;
                CheckConversionWithoutNode(operation.Value.Syntax, convertedType, operation.Type, IsNew(operation.Value));
            }

            // As for a conditional operator: what uses the result has checked both operands through it (see Acquire).
            bool currentIsHandled = _handledOperations.Contains(operation);
            using var handled = Handle();
            handled.Add(currentIsHandled ? operation.Value : null);
            handled.Add(currentIsHandled ? operation.WhenNull : null);

            base.VisitCoalesce(operation);
        }

        public override void VisitCoalesceAssignment(ICoalesceAssignmentOperation operation)
        {
            // The result is the value that the target then has, read by value: it is reported here unless nothing uses it.
            CheckTypeInUnsupportedContext(operation);
            CheckMembersRunOnOneValue(operation);

            var value = operation.Value;

            // The target is tested for null and assigned in place, and the value is assigned to it as by '='. It is not an assignment as far
            // as a [MovedFrom] parameter goes: it leaves a value that is not null in place.
            if (types.IsNonCopyableType(value.Type) && Acquire(value) != RefKind.None)
                Report(Rules.CopyByAssignment, value.Syntax, value.Type, GetHandlerHint(value));

            CheckLeftInMovedFromParameter(operation.Target, value);

            using var handled = Handle();
            handled.Add(operation.Target);
            handled.Add(value);

            base.VisitCoalesceAssignment(operation);
        }

        public override void VisitCompoundAssignment(ICompoundAssignmentOperation operation)
        {
            CheckMemberUse(operation.Syntax, operation.OperatorMethod);

            // The user-defined conversions that take the target to the operator and its result back, with nothing written for them.
            CheckMemberUse(operation.Syntax, operation.InConversion.MethodSymbol);
            CheckMemberUse(operation.Syntax, operation.OutConversion.MethodSymbol);

            // Any other conversion of the target to the left parameter of a user-defined operator, and of what the operator returns back to
            // the target's type, with no node for either: the operator is given the target as that type, and the target is given its result.
            if (operation.OperatorMethod is { IsStatic: true, Parameters: [var left, _], ReturnType: var operatorResult } binaryOperator)
            {
                if (operation.InConversion is { Exists: true, IsIdentity: false, MethodSymbol: null })
                    CheckConversionWithoutNode(operation.Target.Syntax, operation.Target.Type, left.Type, sourceIsNew: false, binaryOperator);

                if (operation.OutConversion is { Exists: true, IsIdentity: false, MethodSymbol: null })
                    CheckConversionWithoutNode(operation.Syntax, operatorResult, operation.Target.Type, sourceIsNew: true);
            }

            CheckTypeInUnsupportedContext(operation);
            CheckMembersRunOnOneValue(operation);

            if (operation.OperatorMethod is null && operation.OperatorKind is BinaryOperatorKind.Add)
                CheckCombinedDelegates(operation.Syntax, operation.Target.Type);

            // An instance operator ('public void operator +=(int)') runs on the target in place, as a call of an instance method does,
            // and its right side is the argument of that call: a new value is moved in, and a stored one is a copy, which is reported
            // where the value is visited.
            using var handled = Handle();

            if (operation.OperatorMethod is { IsStatic: false, Parameters: [var taking] })
            {
                handled.Add(operation.Target);
                handled.Add(CanAssign(Acquire(operation.Value), taking.RefKind) ? operation.Value : null);
            }

            base.VisitCompoundAssignment(operation);
        }

        public override void VisitConditional(IConditionalOperation operation)
        {
            // The type first: an 'if' statement has none, and Acquire follows the branches, which for an 'else if' chain is the rest of it.
            if (!operation.IsRef && types.IsNonCopyableType(operation.Type) && Acquire(operation) != RefKind.None)
                CheckTypeInUnsupportedContext(operation);

            // What answers for the conditional answers for its branches. An 'if' statement is never handled, and an 'else if' chain is
            // as deep as it is long: the marks are in a function of their own, so that this one takes little room on the stack.
            if (_handledOperations.Contains(operation))
                VisitHandledConditional(operation);
            else
                base.VisitConditional(operation);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void VisitHandledConditional(IConditionalOperation operation)
        {
            using var handled = Handle();
            handled.Add(operation.WhenTrue);
            handled.Add(operation.WhenFalse);

            base.VisitConditional(operation);
        }

        public override void VisitConditionalAccess(IConditionalAccessOperation operation)
        {
            CheckTypeInUnsupportedContext(operation);

            // A new value is used in place. A stored one is not: 'stored?.Dispose()' disposes a copy that is taken out of the nullable. A
            // copy that is only the receiver of a readonly member does no harm.
            var receiver = operation.Operation;

            if (!IsNew(receiver) && types.IsNonCopyableType(receiver.Type) && !IsOnlyReceiverOfReadOnlyMember(FindConditionalAccessInstance(operation)))
                Report(Rules.CopyOfStoredValue, receiver.Syntax, "'?.' runs on", receiver.Type, string.Empty);

            using var handled = Handle();
            handled.Add(receiver);

            base.VisitConditionalAccess(operation);
        }

        public override void VisitConversion(IConversionOperation operation)
        {
            var operand = operation.Operand;
            IOperation? handledOperand = null;

            // A span over an inline array is a view of the buffer, which is not copied for it.
            if (operation.GetConversion().IsInlineArray)
            {
                using var bufferHandled = Handle();
                bufferHandled.Add(operand);
                base.VisitConversion(operation);
                return;
            }

            bool isUnboxing = IsUnboxing(operation);

            if (operation.OperatorMethod is { } operatorMethod)
            {
                var use = CheckMemberUse(operation.Syntax, operatorMethod);

                if (ReturnsByValue(operatorMethod))
                    CheckHandedOutValue(operation.Syntax, use, operation.Type, AreCarriedValuesNew(operand));

                // The operand is the argument of the operator: a new value is moved into it. Roslyn's rule reports every operand of a
                // user-defined conversion that is not an object creation.
                if (IsNew(operand))
                    handledOperand = operand;
                else
                    handledOperand = CheckOperatorOperand(operatorMethod, 0, operand);
            }
            else
            {
                CheckContainerConversion(operation);
                CheckHolderLostByVariance(operation);
            }

            bool isBoxing = operation.OperatorMethod is null && operation.Type is { IsValueType: false } && types.IsNonCopyableType(operand.Type);

            if (isUnboxing)
            {
                // The value is copied out of a heap object that others can hold, whatever gave the reference to it: a variable, a call, a
                // property. What uses the result gets a new value (see Acquire), so the copy is reported here and only here.
                Report(Rules.CopyByAssignmentOutOfBox, operation.Syntax, operation.Type, operand.Type);
                CheckTypeInUnsupportedContext(operation);
            }
            else if (isBoxing && IsFormattedByReadOnlyToString(operation))
            {
                // The temporary copy is only the receiver of a member that cannot change it, which does no harm.
                handledOperand = operand;
            }
            else if (!IsSupportedConversion())
            {
                // A stored value that is converted to a reference type is boxed, which has its own finding. Without it the operand would be reported
                // as an unsupported use when it is visited.
                if (isBoxing && !_handledOperations.Contains(operand))
                {
                    Report(Rules.Boxing, operand.Syntax, operand.Type, operation.Type);
                    handledOperand = operand;
                }

                CheckTypeInUnsupportedContext(operation);
            }
            else if (isBoxing && IsNew(operand))
            {
                // A new value is moved into the box, which then holds the only instance. Roslyn's rule allows it for an object creation and
                // reports every other new value, such as the result of a call.
                handledOperand = operand;

                // With a container bound to a type parameter there is no box: the conversion hands the container out as a type that hides
                // its elements, and nothing in the generic can tell. And a generic struct that is non-copyable only through its type
                // arguments runs unchecked code on its box, which hands out boxed copies. A member that the list marks 'borrows' may
                // receive either, as it may a container.
                if (GetReceivingMember(operation) is not { } member || !types.IsBorrowing(member))
                {
                    if (types.HidesTypeParameter(operand.Type, operation.Type))
                        Report(Rules.OpaqueTypeParameterConversion, operand.Syntax, operand.Type, operation.Type);
                    else if (operation.Type is { } boxType && types.IsStructOfUncheckedCode(operand.Type) && types.LosesNonCopyableType(operand.Type, boxType))
                        Report(IsUnwantedResultOfLambda(operation) ? Rules.OpaqueLambdaBoxing : Rules.OpaqueBoxing, operand.Syntax, operand.Type, operation.Type, Display(GetStructDefinition(operand.Type)));
                }
            }

            // A conversion from one non-copyable type to another, such as from Guard to Guard?, wraps the operand: what uses the result has
            // checked the operand through it (see Acquire), or the conversion has just been reported. An identity conversion is left as Roslyn's
            // rule has it, where what allows the conversion does not always allow the operand, except for a new value, which nothing can copy.
            // An identity conversion between two types that are not the same changes only the names of tuple elements or nullability
            // annotations, and is no use of its own: what allows the conversion allows the operand.
            bool changesNamesOnly = operation.Conversion.IsIdentity && !SymbolEqualityComparer.IncludeNullability.Equals(operand.Type, operation.Type);

            if (operation.OperatorMethod is null &&
                types.IsNonCopyableType(operation.Type) &&
                types.IsNonCopyableType(operand.Type) &&
                (!operation.Conversion.IsIdentity || (_handledOperations.Contains(operation) && (changesNamesOnly || IsNew(operand)))))
            {
                handledOperand = operand;
            }

            using var handled = Handle();
            handled.Add(handledOperand);

            base.VisitConversion(operation);

            bool IsSupportedConversion()
            {
                // A conversion that does not exist is an error of the compiler. The analyzer also runs in the editor on code that is
                // being typed.
                if (!operation.Conversion.Exists)
                    return true;

                // A user-defined conversion whose parameter does not take the operand, which was looked at above, falls through to
                // the general check.
                if (operation.OperatorMethod is not null)
                    return false;

                return Acquire(operation.Operand) switch {
                    RefKind.None => true,
                    RefKind.Ref or RefKind.RefReadOnly or RefKind.RefReadOnlyParameter when operation.Conversion.IsIdentity => true,
                    _ => false,
                };
            }
        }

        public override void VisitDeclarationExpression(IDeclarationExpressionOperation operation)
        {
            CheckTypeInUnsupportedContext(operation);

            // What allows the declaration, such as an 'out' argument, allows the variable in it.
            using var handled = Handle();
            handled.Add(_handledOperations.Contains(operation) ? operation.Expression : null);

            base.VisitDeclarationExpression(operation);
        }

        public override void VisitDeconstructionAssignment(IDeconstructionAssignmentOperation operation)
        {
            CheckTypeInUnsupportedContext(operation);

            var value = operation.Value;
            using var handled = Handle();
            handled.Add(value);
            MarkDeconstructionTargets(operation.Target, handled);

            DeconstructionInfo? info = operation.Syntax is AssignmentExpressionSyntax syntax ? operation.SemanticModel?.GetDeconstructionInfo(syntax) : null;
            CheckDeconstruction(operation.Target, value, info, IsNew(value), handled, value.Type);

            base.VisitDeconstructionAssignment(operation);
        }

        public override void VisitDefaultValue(IDefaultValueOperation operation)
        {
            // default(T) is a valid way to acquire a 'T'.
            base.VisitDefaultValue(operation);
        }

        public override void VisitDelegateCreation(IDelegateCreationOperation operation)
        {
            CheckTypeInUnsupportedContext(operation);

            // A delegate that is created from another delegate, as 'new Func<object>(makeGuards)' is, calls that one's Invoke method.
            var wrappedInvoke = operation.Target is IMethodReferenceOperation or IAnonymousFunctionOperation
                ? null
                : (SkipParentheses(operation.Target).Type as INamedTypeSymbol)?.DelegateInvokeMethod;

            if (operation.Target is IMethodReferenceOperation methodReference)
            {
                CheckMethodGroupConversion(
                    methodReference.Syntax, methodReference.Method, GetParametersBehindDelegate(methodReference, methodReference.Method), Display(methodReference.Method), operation.Type);
            }
            else if (wrappedInvoke is not null)
            {
                CheckMethodGroupConversion(operation.Target.Syntax, wrappedInvoke, wrappedInvoke.Parameters, wrappedInvoke.ContainingType, operation.Type);
            }

            // A caller of the delegate takes what it returns for a new value unless the delegate type says otherwise.
            var function = operation.Target switch {
                IMethodReferenceOperation { Method: var method } => method,
                IAnonymousFunctionOperation { Symbol: var lambda } => lambda,
                _ => wrappedInvoke,
            };

            if (function is not null && operation.Type is INamedTypeSymbol { DelegateInvokeMethod: { } invoke })
            {
                if (NonCopyableTypes.AddsBorrowed(function, invoke))
                    Report(Rules.BorrowedNotOnDelegate, operation.Target.Syntax, Display(function), operation.Type);
                else if (!NonCopyableTypes.HasBorrowedReturn(invoke) && function.RefKind is RefKind.None && wrappedInvoke is null && types.Carries(invoke.ReturnType))
                    CheckContainerHandedOutByDelegate(operation, function, invoke);
                else if (!NonCopyableTypes.HasBorrowedReturn(invoke) && wrappedInvoke is not null && types.LosesHolder(wrappedInvoke.ReturnType, invoke.ReturnType))
                    Report(Rules.DelegateHandsOutStoredContainer, operation.Target.Syntax, Display(function), wrappedInvoke.ReturnType);

                // The parameters that stand behind those of the delegate, one for one.
                var parameters = GetParametersBehindDelegate(operation.Target, function);
                CheckMovedFromOfDelegate(operation, function, parameters, invoke, isLambda: operation.Target is IAnonymousFunctionOperation);

                // And takes a container that it passes to the delegate to be still its own. A method group keeps what its method
                // keeps: by [Kept], or by having a type parameter as the type of its parameter, as 'registry.Add' has. The body of
                // a lambda is checked against the delegate's parameters itself.
                for (int i = 0; i < parameters.Length && parameters.Length == invoke.Parameters.Length; i++)
                {
                    if (types.AddsKept(parameters[i], invoke.Parameters[i]))
                        Report(Rules.KeptNotOnDelegate, operation.Target.Syntax, parameters[i].Name, Display(function), operation.Type);
                    else if (operation.Target is not IAnonymousFunctionOperation && types.KeepsUnseenBehind(parameters[i], invoke.Parameters[i], out bool byUncheckedCode))
                        Report(Rules.KeepingNotOnDelegate, operation.Target.Syntax, parameters[i].Name, Display(function), operation.Type, Rules.DescribeKeeping(byUncheckedCode));
                }
            }

            base.VisitDelegateCreation(operation);
        }

        /// <summary>
        /// Gets the parameters of a function that stand behind the parameters of the delegate that it is converted to, one for one.
        /// A method group of an extension method that is called on a receiver, as 'holder.Keep' is, has one parameter more than the
        /// delegate: its first one is bound to the receiver.
        /// </summary>
        private static ImmutableArray<IParameterSymbol> GetParametersBehindDelegate(IOperation target, IMethodSymbol function) =>
            target is IMethodReferenceOperation { Instance: not null } && function is { IsExtensionMethod: true, MethodKind: not MethodKind.ReducedExtension, Parameters.IsEmpty: false }
                ? function.Parameters.RemoveAt(0)
                : function.Parameters;

        public override void VisitEventAssignment(IEventAssignmentOperation operation)
        {
            CheckTypeInUnsupportedContext(operation);

            // A field-like event combines the delegates that are added to it. An event with accessors of its own does what its code
            // says, which is checked there.
            if (operation is { Adds: true, EventReference: IEventReferenceOperation { Event: var added } } &&
                (added.AddMethod is null or { IsImplicitlyDeclared: true } || added.DeclaringSyntaxReferences.IsEmpty))
            {
                CheckCombinedDelegates(operation.Syntax, added.Type);
            }

            base.VisitEventAssignment(operation);
        }

        // The compiler passes an argument, or the receiver, that an interpolated-string handler asks for with
        // [InterpolatedStringHandlerArgument] to the method and again to the constructor of the handler.
        public override void VisitInterpolatedStringHandlerArgumentPlaceholder(IInterpolatedStringHandlerArgumentPlaceholderOperation operation)
        {
            if (operation.PlaceholderKind is InterpolatedStringArgumentPlaceholderKind.CallsiteArgument or InterpolatedStringArgumentPlaceholderKind.CallsiteReceiver &&
                operation.Parent is IArgumentOperation { Parameter: { RefKind: RefKind.None } parameter } &&
                types.IsNonCopyableType(parameter.Type))
            {
                Report(
                    Rules.ValuePassedTwice,
                    operation.Syntax,
                    parameter.Type,
                    $"the call and the constructor of its interpolated-string handler '{Display(parameter.ContainingType)}'");
            }

            base.VisitInterpolatedStringHandlerArgumentPlaceholder(operation);
        }

        public override void VisitEventReference(IEventReferenceOperation operation)
        {
            CheckMemberUse(operation.Syntax, operation.Event);
            CheckReceiverType(operation.Syntax, GetReceiverType(operation.Instance), operation.Event, subscribes: operation.Parent is not IEventAssignmentOperation { Adds: false });
            CheckTypeInUnsupportedContext(operation);

            // An accessor runs on the value in place, and through a read-only reference on a copy unless it is readonly.
            var instance = operation.Instance;
            var accessor = operation.Parent is IEventAssignmentOperation { Adds: false } ? operation.Event.RemoveMethod : operation.Event.AddMethod;

            // The handler that '+=' or '-=' gives the accessor is a parameter of the accessor, which the event does not show.
            if (accessor is not null && operation.Parent is IEventAssignmentOperation)
                CheckUncheckedInbound(operation.Syntax, accessor);

            if (instance is not null &&
                accessor is not null &&
                IsCalledOnCopy(instance, operation.Event.ContainingType, accessor.IsReadOnly) &&
                !ReportMemberOnCopy(instance, operation.Event))
            {
                instance = null;
            }

            using var handled = Handle();
            handled.Add(instance);

            base.VisitEventReference(operation);
        }

        public override void VisitExpressionStatement(IExpressionStatementOperation operation)
        {
            CheckTypeInUnsupportedContext(operation);

            // The result of the top-most operation in an expression statement is not used
            using var handled = Handle();
            handled.Add(operation.Operation);

            base.VisitExpressionStatement(operation);
        }

        public override void VisitFieldInitializer(IFieldInitializerOperation operation)
        {
            CheckTypeInUnsupportedContext(operation);

            // A new value is moved into the field. A variable can be one here: a parameter of a primary constructor at its last use.
            using var handled = Handle();
            handled.Add(IsNew(operation.Value) ? operation.Value : null);

            base.VisitFieldInitializer(operation);
        }

        public override void VisitFieldReference(IFieldReferenceOperation operation)
        {
            // An instance never needs to be copied to read or write a field value. Even in cases where the instance is read-only, attempts to write to
            // the field would be reported as compiler errors so analyzer diagnostics are not required.
            using var handled = Handle();
            handled.Add(operation.Instance);

            // A field of an object whose binding to a class that clones itself no declaration judged is a member of it, judged by its type.
            if (!operation.Field.IsStatic && GetReceiverType(operation.Instance) is INamedTypeSymbol { TypeKind: TypeKind.Class } receiverClass &&
                types.FindUnjudgedCloningBinding(receiverClass) is { } binding)
            {
                CheckMemberOfClonedObject(operation.Syntax, receiverClass, operation.Field, binding);
            }

            CheckTypeInUnsupportedContext(operation);
            base.VisitFieldReference(operation);
        }

        public override void VisitForEachLoop(IForEachLoopOperation operation)
        {
            // The collection of a 'foreach' is an identity conversion of what is enumerated, on which the compiler calls GetEnumerator.
            // Where that call is fine, the conversion and what it converts are handled here. Anything else is left to be reported
            // where it is visited: a collection that is no conversion, as an inline array is, or one that is converted in another way.
            IConversionOperation? conversion = null;
            IOperation? collection = null;

            if (operation.Collection is IConversionOperation { Conversion: { IsIdentity: true, MethodSymbol: null }, Operand: var enumeratedValue } identity &&
                CheckForEachGetEnumerator(operation, enumeratedValue, out bool isCalledInPlace))
            {
                conversion = identity;
                collection = isCalledInPlace ? enumeratedValue : null;
            }

            using var handled = Handle();
            handled.Add(conversion);
            handled.Add(collection);

            // The compiler enumerates an inline array in place, as a span over the buffer, with no conversion in the tree.
            var buffer = GetInlineArrayElementType(operation.Collection.Type) is not null ? operation.Collection : null;
            handled.Add(buffer);

            // A stored struct that is enumerated through an interface is copied for the call of GetEnumerator. The temporary copy does no
            // harm when the struct implements the method as readonly.
            IOperation? harmlessCopy = null;

            if (operation.Collection is IConversionOperation { IsImplicit: true, OperatorMethod: null, Type.TypeKind: TypeKind.Interface } boxing &&
                boxing.Operand is { Type: INamedTypeSymbol { TypeKind: TypeKind.Struct } structType } enumerated &&
                types.IsNonCopyableType(structType) &&
                operation.Syntax is CommonForEachStatementSyntax forEachSyntax &&
                operation.SemanticModel?.GetForEachStatementInfo(forEachSyntax).GetEnumeratorMethod is { ContainingType.TypeKind: TypeKind.Interface } interfaceMethod &&
                structType.FindImplementationForInterfaceMember(interfaceMethod) is IMethodSymbol { IsReadOnly: true })
            {
                harmlessCopy = enumerated;
            }

            handled.Add(harmlessCopy);

            CheckTypeInUnsupportedContext(operation);

            AnalyzeForEach(operation, handled);
            base.VisitForEachLoop(operation);
        }

        public override void VisitImplicitIndexerReference(IImplicitIndexerReferenceOperation operation)
        {
            // An index or a range ('span[^1]', 'span[1..]') that the compiler turns into a call of a length and an indexer or Slice.
            var instanceType = operation.Instance.Type;
            CheckMemberUse(operation.Syntax, operation.LengthSymbol);
            CheckReceiverType(operation.Syntax, instanceType, operation.LengthSymbol);
            var use = CheckMemberUse(operation.Syntax, operation.IndexerSymbol);
            CheckReceiverType(operation.Syntax, instanceType, operation.IndexerSymbol);

            if (ReturnsByValue(operation.IndexerSymbol))
                CheckHandedOutValue(operation.Syntax, use, operation.Type, !types.IsView(instanceType) && AreCarriedValuesNew(operation.Instance));
            else
                CheckTypeInUnsupportedContext(operation);

            // Through a read-only reference the compiler calls each of them that is not readonly on a copy. A range whose ends both count
            // from the start needs no length.
            bool isAssigned = operation.Parent is ISimpleAssignmentOperation assignment && ReferenceEquals(operation, assignment.Target);

            var called = new[] {
                CarriedSymbols.NeedsLength(operation.Argument) ? (operation.LengthSymbol as IPropertySymbol)?.GetMethod : null,
                operation.IndexerSymbol is IPropertySymbol indexer ? (isAssigned ? indexer.SetMethod : indexer.GetMethod) : operation.IndexerSymbol as IMethodSymbol,
            };

            foreach (var accessor in called)
            {
                if (accessor is { IsStatic: false } &&
                    IsCalledOnCopy(operation.Instance, accessor.ContainingType, accessor.IsReadOnly) &&
                    ReportMemberOnCopy(operation.Instance, accessor.AssociatedSymbol ?? accessor))
                {
                    break;
                }
            }

            // A member of an extension block that takes its receiver by value is given a copy of a stored value, like any other.
            foreach (var accessor in called)
            {
                if (accessor is not null && NonCopyableTypes.GetExtensionReceiver(accessor) is { RefKind: RefKind.None })
                {
                    CheckExtensionReceiver(operation.Instance, accessor);
                    break;
                }
            }

            CheckMembersRunOnOneValue(operation);

            using var handled = Handle();
            handled.Add(operation.Instance);

            base.VisitImplicitIndexerReference(operation);
        }

        public override void VisitInlineArrayAccess(IInlineArrayAccessOperation operation)
        {
            // An element of an inline array is read in place, as an element of an array is, and a range of it is a span over the buffer. The
            // buffer itself is not copied for either.
            using var handled = Handle();
            handled.Add(operation.Instance);

            CheckTypeInUnsupportedContext(operation);
            base.VisitInlineArrayAccess(operation);
        }

        public override void VisitIncrementOrDecrement(IIncrementOrDecrementOperation operation)
        {
            CheckMemberUse(operation.Syntax, operation.OperatorMethod);
            CheckTypeInUnsupportedContext(operation);
            CheckMembersRunOnOneValue(operation);

            // An instance operator ('public void operator ++()') runs on the target in place.
            using var handled = Handle();
            handled.Add(operation.OperatorMethod is { IsStatic: false } ? operation.Target : null);

            // A static operator takes the value of the variable by value: what it receives is a copy of the stored value, which the
            // variable holds until the result is assigned to it. An operator that a list marks 'borrows' only reads the copy.
            if (operation.OperatorMethod is { IsStatic: true, Parameters: [{ RefKind: RefKind.None } taking] } staticOperator &&
                types.IsNonCopyableType(operation.Target.Type) &&
                SymbolEqualityComparer.Default.Equals(taking.Type, operation.Target.Type))
            {
                if (!types.IsBorrowing(staticOperator))
                    Report(Rules.CopyOfStoredValue, operation.Target.Syntax, $"'{Display(staticOperator)}' receives", operation.Target.Type, string.Empty);

                handled.Add(operation.Target);
            }

            base.VisitIncrementOrDecrement(operation);
        }

        public override void VisitInterpolation(IInterpolationOperation operation)
        {
            CheckTypeInUnsupportedContext(operation);

            // The string is built from the value as an object. With a custom handler the value is an argument of a call that is in the tree, and
            // the invocation checks it.
            if (operation.Parent is IInterpolatedStringOperation && types.HidesNonCopyableType(operation.Expression.Type, context.Compilation.ObjectType))
                ReportContainerConversion(operation.Expression.Syntax, operation.Expression.Type, context.Compilation.ObjectType);

            // A hole takes its value like a parameter that takes it by value: the string is built from a copy, on which ToString or the
            // formatting member runs. So a new value, which a variable at its last use is, is moved into the hole, and a stored value
            // is reported where it is visited.
            using var handled = Handle();
            handled.Add(IsNew(operation.Expression) ? operation.Expression : null);
            base.VisitInterpolation(operation);
        }

        public override void VisitInvalid(IInvalidOperation operation)
        {
            // This is already a compiler error. No need to report more diagnostics on it.
            base.VisitInvalid(operation);
        }

        public override void VisitInvocation(IInvocationOperation operation)
        {
            var method = operation.TargetMethod;
            var instance = operation.Instance;
            MemberUse use;

            if (operation.Parent is IInterpolatedStringAppendOperation && types.GetUse(method) is { Kind: MemberUseKind.UnlistedContainer } appended)
            {
                // A hole of an interpolated string that a handler builds is a call of AppendFormatted<T>. A container in it is formatted as an
                // object, as it is in a string that the compiler builds itself, and is judged as that conversion.
                var hole = operation.Arguments.FirstOrDefault()?.Value.Syntax ?? operation.Syntax;

                if (types.HidesNonCopyableType(appended.TypeArgument, context.Compilation.ObjectType))
                    ReportContainerConversion(hole, appended.TypeArgument, context.Compilation.ObjectType);

                use = default;
            }
            else
            {
                use = CheckMemberUse(operation.Syntax, method);
            }

            // MemberwiseClone has its own rule, which looks at the fields that the clone copies, and GetObjectValue clones a boxed struct.
            if (IsMemberwiseClone(method))
                CheckMemberwiseClone(operation, operation.Instance);
            else
                CheckReceiverType(operation.Syntax, GetReceiverType(instance), method);

            CheckCloneOfOwnObject(operation, method, operation.Instance, operation.Arguments);

            if (IsGetObjectValue(method))
                Report(Rules.GetObjectValueCall, operation.Syntax);

            if (use.Kind is MemberUseKind.Listed)
            {
                bool? inputsAreNew = null;

                if (ReturnsByValue(method) && types.IsNonCopyableType(operation.Type))
                    CheckHandedOutValue(operation.Syntax, use, operation.Type, inputsAreNew ??= AreInputsNew(instance, operation.Arguments), instance, operation);

                foreach (var argument in operation.Arguments)
                {
                    if (argument.Parameter is { RefKind: RefKind.Out } parameter && types.IsNonCopyableType(parameter.Type))
                        CheckHandedOutValue(argument.Syntax, use, parameter.Type, inputsAreNew ??= AreInputsNew(instance, operation.Arguments));
                }
            }

            CheckTypeInUnsupportedContext(operation);

            if (instance is not null && IsCalledOnCopy(instance, method.ReceiverType, method.IsReadOnly) && !ReportMemberOnCopy(instance, method))
                instance = null;

            // The receiver of a member of an extension block is an argument for the parameter of the block: a stored value that it takes by
            // value is copied, which is reported where the receiver is visited.
            if (instance is not null && IsCopiedIntoExtensionReceiver(instance, method))
                instance = null;

            using var handled = Handle();
            handled.Add(instance);

            // No need to check the method signature further. Parameters will be checked by the IArgumentOperation.
            base.VisitInvocation(operation);
        }

        public override void VisitIsPattern(IIsPatternOperation operation)
        {
            CheckTypeInUnsupportedContext(operation);

            // The value is examined in place. Only a variable that the pattern declares takes something out of it.
            using var handled = Handle();
            handled.Add(operation.Value);
            var taken = new TakenParts();
            CheckPattern(operation.Pattern, IsNew(operation.Value), handled, inputIsCopied: IsCopiedForPattern(operation.Value), taken: taken);
            ReportPartsUsedTwice(taken);
            base.VisitIsPattern(operation);
        }

        public override void VisitIsType(IIsTypeOperation operation)
        {
            CheckTypeInUnsupportedContext(operation);

            // A type test copies nothing that can be used.
            using var handled = Handle();
            handled.Add(operation.ValueOperand);

            base.VisitIsType(operation);
        }

        public override void VisitLocalFunction(ILocalFunctionOperation operation)
        {
            CheckTypeInUnsupportedContext(operation);
            base.VisitLocalFunction(operation);
            CheckMovedFromParameters(operation.Symbol, operation, isLocal: true);

            // The parameters of a lambda and of a local function have no symbol action.
            foreach (var parameter in operation.Symbol.Parameters)
                ReportNotNullRef(parameter, operation.Symbol, context.ReportDiagnostic, context.CancellationToken);

            // The type parameters of a local function have no symbol action.
            foreach (var typeParameter in operation.Symbol.TypeParameters)
                ReportForeignNonCopyableMarks(typeParameter, context.Compilation.Assembly, context.ReportDiagnostic, context.CancellationToken);
        }

        public override void VisitLocalReference(ILocalReferenceOperation operation)
        {
            CheckTypeInUnsupportedContext(operation);

            if (GetParameterAliases().TryGetValue(operation.Local, out var aliased))
                CheckKeptUse(operation, aliased);

            base.VisitLocalReference(operation);
        }

        public override void VisitLock(ILockOperation operation)
        {
            CheckTypeInUnsupportedContext(operation);

            // The statement hands the value to Monitor.Enter as an object.
            var lockedValue = operation.LockedValue;

            if (types.HidesNonCopyableType(lockedValue.Type, context.Compilation.ObjectType) && !types.IsBorrowing(MonitorEnterId))
                ReportContainerConversion(lockedValue.Syntax, lockedValue.Type, context.Compilation.ObjectType);

            base.VisitLock(operation);
        }

        public override void VisitMethodReference(IMethodReferenceOperation operation)
        {
            // A delegate type has no code of its own, so the method it is created from is checked here.
            var method = operation.Method;
            var use = CheckMemberUse(operation.Syntax, method);
            CheckReceiverType(operation.Syntax, GetReceiverType(operation.Instance), method);
            CheckCloneOfOwnObject(operation, method, operation.Instance, []);

            // A method group of MemberwiseClone or of GetObjectValue clones when the delegate is invoked.
            if (IsMemberwiseClone(method))
                CheckMemberwiseClone(operation, operation.Instance);

            if (IsGetObjectValue(method))
                Report(Rules.GetObjectValueCall, operation.Syntax);

            // The delegate can be invoked any number of times, so what it is created on counts as stored. That goes for a member marked 'new'
            // as well: the delegate hands out what the member does, and its callers take a non-copyable value from it for new.
            if (ReturnsByValue(method))
                CheckHandedOutValue(operation.Syntax, use, method.ReturnType, inputsAreNew: false);

            // And it hands out through its 'out' parameters as it does through its return value: the callers of
            // 'TryGet get = dictionary.TryGetValue' take what comes out for new.
            foreach (var parameter in NonCopyableTypes.GetParametersWithReceiver(method))
            {
                if (parameter.RefKind is RefKind.Out)
                    CheckHandedOutValue(operation.Syntax, use, parameter.Type, inputsAreNew: false);
            }

            CheckTypeInUnsupportedContext(operation);
            base.VisitMethodReference(operation);
        }

        public override void VisitMemberInitializer(IMemberInitializerOperation operation)
        {
            // 'new Holder { Guard = { IsHeld = false } }' sets members of the field of the new object where the field is: the compiler
            // works in place, and nothing is copied.
            using var handled = Handle();
            handled.Add(operation);
            handled.Add(operation.InitializedMember);
            handled.Add(operation.Initializer);

            base.VisitMemberInitializer(operation);
        }

        /// <summary>
        /// Gets the type that the receiver of a member is judged by: 'this' and 'base' are the object of the class whose code it is, judged as
        /// that class, whose declaration got any warning of its own. 'base' has the type of the base class in the tree.
        /// </summary>
        private ITypeSymbol? GetReceiverType(IOperation? instance) =>
            instance is IInstanceReferenceOperation { ReferenceKind: InstanceReferenceKind.ContainingTypeInstance } && context.OwningSymbol.ContainingType is { TypeKind: TypeKind.Class } own
                ? own
                : instance?.Type;

        public override void VisitDynamicObjectCreation(IDynamicObjectCreationOperation operation)
        {
            ReportCloningBindingOfCreation(operation);
            base.VisitDynamicObjectCreation(operation);
        }

        // An object of a class whose objects a class that clones itself clones, created with a non-copyable type bound to it. This is the
        // early warning where the project writes the binding; the uses of the object get theirs where it reaches the cloning code. An
        // object that is converted where it is created gets the one warning of that conversion.
        private void ReportCloningBindingOfCreation(IOperation creation)
        {
            if (creation.Type is INamedTypeSymbol created &&
                !SymbolEqualityComparer.Default.Equals(created, created.OriginalDefinition) &&
                !(SkipParenthesesUpward(creation).Parent is IConversionOperation { OperatorMethod: null } conversion && types.HidesNonCopyableType(created, conversion.Type)) &&
                types.FindCloningBinding(created) is { } binding)
            {
                Report(Rules.CloningBinding, creation.Syntax, binding.Type, binding.Bound, binding.Cloning);
            }
        }

        public override void VisitNameOf(INameOfOperation operation)
        {
            // A 'nameof' operation does not copy anything in the value
        }

        public override void VisitObjectCreation(IObjectCreationOperation operation)
        {
            var use = CheckMemberUse(operation.Syntax, operation.Constructor);

            ReportCloningBindingOfCreation(operation);

            if (use.Kind is MemberUseKind.Listed && types.IsNonCopyableType(operation.Type))
                CheckHandedOutValue(operation.Syntax, use, operation.Type, AreInputsNew(null, operation.Arguments, forValue: true), operation.Arguments is [var only] ? only.Value : null);

            // 'new T()' is a valid way to acquire a 'T'. No need to check the method signature further. Parameters will be checked by the
            // IArgumentOperation. Also mark the initializer (if any) as handled.
            using var handled = Handle();
            handled.Add(operation.Initializer);
            base.VisitObjectCreation(operation);
        }

        public override void VisitParameterReference(IParameterReferenceOperation operation)
        {
            CheckTypeInUnsupportedContext(operation);

            if (IsReadByValue(operation) && IsOwnedMovedFromParameter(operation.Parameter, operation))
                NoteOwnedParameter(operation);

            // A 'ref' local over a [MovedFrom] parameter of a container type, or its address, is a way to assign anything to it. A 'ref'
            // local through which the method does nothing but reset the parameter is not. A reference that a condition chooses is
            // one to each of its branches.
            if (NonCopyableTypes.IsMovedFrom(operation.Parameter) &&
                (types.IsContainer(operation.Parameter.Type) || IsParameterOfGenericMove(operation.Parameter)) &&
                SkipChoicesUpward(operation).Parent is { } taking &&
                taking is IAddressOfOperation or IVariableInitializerOperation { Parent: IVariableDeclaratorOperation { Symbol.RefKind: not RefKind.None } } &&
                !(taking.Parent is IVariableDeclaratorOperation { Symbol: var alias } && IsUsedOnlyToReset(alias)))
            {
                Report(Rules.MovedFromContainerExposed, operation.Syntax, operation.Parameter.Name, "has a reference taken to it");
            }

            if (IsParameterThatMustNotBeKept(operation.Parameter))
                CheckKeptUse(operation, operation.Parameter);

            if (GetParameterAliases().TryGetValue(operation.Parameter, out var aliased))
                CheckKeptUse(operation, aliased);

            base.VisitParameterReference(operation);
        }

        public override void VisitPropertyInitializer(IPropertyInitializerOperation operation)
        {
            CheckTypeInUnsupportedContext(operation);
            using var handled = Handle();
            handled.Add(IsNew(operation.Value) ? operation.Value : null);
            base.VisitPropertyInitializer(operation);
        }

        public override void VisitPropertyReference(IPropertyReferenceOperation operation)
        {
            var property = operation.Property;
            var instance = operation.Instance;
            var use = CheckMemberUse(operation.Syntax, property);
            CheckReceiverType(operation.Syntax, GetReceiverType(instance), property);

            // The value that a setter or an init accessor is given is a parameter of the accessor, which the property does not show.
            if (property.SetMethod is { } setter &&
                (IsAssignmentTarget(operation, out _) || IsSetMethodInvocation(operation)) &&
                types.FindUncheckedInbound(property) is null)
            {
                CheckUncheckedInbound(operation.Syntax, setter);
            }

            // A property of a pattern is checked with the pattern, which knows what it reads from.
            if (use.Kind is MemberUseKind.Listed &&
                ReturnsByValue(property) &&
                types.IsNonCopyableType(operation.Type) &&
                operation.Parent is not IPropertySubpatternOperation &&
                !IsSetMethodInvocation(operation))
            {
                // A view never owns its elements: an element that its indexer hands out by value is a copy, whatever the view came from.
                bool isElementOfView = property.IsIndexer && IsViewBehindConversions(instance);
                CheckHandedOutValue(operation.Syntax, use, operation.Type, !isElementOfView && AreInputsNew(instance, operation.Arguments), instance, operation);
            }

            // Allow a property to return a non-copyable type by value
            if (Acquire(operation) != RefKind.None)
                CheckTypeInUnsupportedContext(operation);

            if (instance is not null)
            {
                var accessor = IsSetMethodInvocation(operation) ? property.SetMethod : property.GetMethod;

                if (accessor is not null && IsCalledOnCopy(instance, property.ContainingType, accessor.IsReadOnly) && !ReportMemberOnCopy(instance, property))
                    instance = null;

                if (instance is not null && IsCopiedIntoExtensionReceiver(instance, property))
                    instance = null;
            }

            using var handled = Handle();
            handled.Add(instance);

            // No need to check the method signature further. Parameters will be checked by the IArgumentOperation.
            base.VisitPropertyReference(operation);
        }

        public override void VisitReturn(IReturnOperation operation)
        {
            var returnedValue = operation.ReturnedValue;

            if (operation.Kind is OperationKind.YieldReturn or OperationKind.YieldBreak)
                CheckIteratorParameters(GetEnclosingMethod(operation));

            if (returnedValue is not null)
            {
                CheckReturnedContainer(operation, returnedValue);
                CheckReturnedByGenericMove(operation, returnedValue);
            }

            if (returnedValue is not null && Acquire(returnedValue) != RefKind.None)
            {
                // A local that is returned as a Guard? or in parentheses is still a local that is returned.
                var source = SkipWrappingConversions(returnedValue);
                bool returnsCopy = GetReturnRefKind(operation) == RefKind.None && types.IsNonCopyableType(returnedValue.Type);

                if (source is ILocalReferenceOperation { Local.IsRef: false } or IParameterReferenceOperation { Parameter.RefKind: RefKind.None })
                {
                    // Returning a by-value local or parameter is allowed where the variable ends with the method. One that lives on does
                    // not, and neither does one that a 'finally' or the end of a 'using' scope uses after its value was read.
                    if (returnsCopy && GetVariableThatLivesOn(operation, source) is { } variable)
                        Report(Rules.CopyByReturnOfLivingVariable, returnedValue.Syntax, returnedValue.Type, variable.Name);
                    else if (returnsCopy && operation.Kind is OperationKind.Return && IsUsedAfterReturn(operation, source, out var used))
                        Report(Rules.CopyByReturnOfVariableUsedAfter, returnedValue.Syntax, returnedValue.Type, used.Name);
                }
                else if (returnsCopy)
                {
                    Report(Rules.CopyByReturn, returnedValue.Syntax, returnedValue.Type);
                }
            }

            using var handled = Handle();
            handled.Add(returnedValue);

            CheckTypeInUnsupportedContext(operation);
            base.VisitReturn(operation);
        }

        public override void VisitSimpleAssignment(ISimpleAssignmentOperation operation)
        {
            // The result is the value that the target then has, read by value: 'Take(_held = guard)' leaves the guard in the field and
            // hands a copy of it to the method. It is reported here unless nothing uses it, or what uses it takes a reference.
            if (!operation.IsRef && !IsResultOfAssignmentJudgedElsewhere(operation) && !IsDiscardOfNewValue(operation))
                CheckTypeInUnsupportedContext(operation);

            var target = operation.Target;
            var value = operation.Value;
            var sourceRefKind = Acquire(value);

            if (types.IsNonCopyableType(value.Type) && !operation.IsRef && target.Kind != OperationKind.Discard && sourceRefKind != RefKind.None)
                Report(Rules.CopyByAssignment, value.Syntax, value.Type, GetHandlerHint(value));

            if (!operation.IsRef)
            {
                CheckLeftInMovedFromParameter(target, value);
            }
            else if (SkipParentheses(target) is IParameterReferenceOperation { Parameter: var reassigned } && NonCopyableTypes.IsMovedFrom(reassigned))
            {
                Report(Rules.MovedFromParameterReassigned, operation.Syntax, reassigned.Name);
            }
            else
            {
                CheckReferenceTakenToMovedFromParameter(value);
            }

            using var handled = Handle();
            handled.Add(target);
            handled.Add(value);

            base.VisitSimpleAssignment(operation);
        }

        /// <summary>
        /// Reports a [MovedFrom] parameter that a 'ref' assignment makes a reference refer to: through the reference the method
        /// assigns anything to it. A reference that a condition chooses is one to each of its branches.
        /// </summary>
        private void CheckReferenceTakenToMovedFromParameter(IOperation value)
        {
            RuntimeHelpers.EnsureSufficientExecutionStack();

            if (SkipParentheses(value) is IConditionalOperation { IsRef: true } chosen)
            {
                CheckReferenceTakenToMovedFromParameter(chosen.WhenTrue);

                if (chosen.WhenFalse is { } otherwise)
                    CheckReferenceTakenToMovedFromParameter(otherwise);
            }
            else if (SkipParentheses(value) is IParameterReferenceOperation { Parameter: var referred } &&
                NonCopyableTypes.IsMovedFrom(referred) &&
                (types.IsContainer(referred.Type) || IsParameterOfGenericMove(referred)))
            {
                Report(Rules.MovedFromContainerExposed, value.Syntax, referred.Name, "has a reference taken to it");
            }
        }

        /// <summary>
        /// Reports a [MovedFrom] parameter that is passed on by reference to something that can assign anything to it. A reference
        /// that a condition chooses is passed on like each of its branches.
        /// </summary>
        private void CheckMovedFromParameterHandedOn(IArgumentOperation argument, IParameterSymbol receiving, IOperation value)
        {
            RuntimeHelpers.EnsureSufficientExecutionStack();

            if (SkipParentheses(value) is IConditionalOperation { IsRef: true } chosen)
            {
                CheckMovedFromParameterHandedOn(argument, receiving, chosen.WhenTrue);

                if (chosen.WhenFalse is { } otherwise)
                    CheckMovedFromParameterHandedOn(argument, receiving, otherwise);
            }
            else if (SkipParentheses(value) is IParameterReferenceOperation { Parameter: var handedOn } &&
                NonCopyableTypes.IsMovedFrom(handedOn) &&
                (types.IsContainer(handedOn.Type)
                    ? !LeavesOwnedContainer(argument) && !IsListedCallWithNothingStored(argument)
                    : IsParameterOfGenericMove(handedOn) &&
                        !(argument.Parent is IInvocationOperation call && NonCopyableTypes.IsMovedFrom(receiving.OriginalDefinition) && IsPureMoveCall(call.TargetMethod, call.Instance, call.Arguments))))
            {
                Report(
                    Rules.MovedFromContainerExposed,
                    value.Syntax,
                    handedOn.Name,
                    $"is passed by reference to '{Display(receiving.ContainingSymbol.OriginalDefinition)}'");
            }
        }

        /// <summary>
        /// Gets a value indicating whether an assignment is one whose result nothing uses though it is no statement, or one whose
        /// result is the result of the operation around it. A member of an object initializer, of a collection initializer with an
        /// index and of an anonymous type is an assignment, and nothing uses what it gives. 'a?.b = c' gives the result of the
        /// assignment as the result of the conditional access, which is judged as that where it is used.
        /// </summary>
        private static bool IsResultOfAssignmentJudgedElsewhere(ISimpleAssignmentOperation operation) => operation.Parent switch {
            IObjectOrCollectionInitializerOperation or IAnonymousObjectCreationOperation => true,
            IConditionalAccessOperation access => ReferenceEquals(access.WhenNotNull, operation),
            _ => false,
        };

        /// <summary>
        /// Gets a value indicating whether an operation is '_ = value' with a new value, which nothing else holds: its result is not a
        /// copy of anything that is stored.
        /// </summary>
        private bool IsDiscardOfNewValue(IOperation operation) =>
            SkipParentheses(operation) is ISimpleAssignmentOperation { IsRef: false, Target: IDiscardOperation, Value: var discarded } && IsNew(discarded);

        public override void VisitSpread(ISpreadOperation operation)
        {
            // A user-defined conversion of each element, with nothing written for it.
            CheckMemberUse(operation.Syntax, operation.ElementConversion.MethodSymbol);

            // Any other conversion of each element to the element type of the new collection, with no node for it either. A stored value
            // of a non-copyable type is reported below as a copy, which its box is.
            if (operation.ElementConversion is { Exists: true, IsIdentity: false, MethodSymbol: null } &&
                GetCollectionElementType(operation.Parent?.Type) is { } collectionElementType &&
                types.HidesNonCopyableType(operation.ElementType, collectionElementType))
            {
                ReportHidingConversion(operation.Operand.Syntax, operation.ElementType!, collectionElementType, receivingMember: null);
            }

            // A spread element copies every element of a collection into the new one, as a 'foreach' with a by-value variable does.
            // A view never owns its elements, so a spread over one copies them, whatever the view came from.
            if (types.IsNonCopyableType(operation.ElementType) &&
                (!IsNew(operation.Operand) || IsObjectWithInheritedMembers(operation.Operand) || IsViewBehindConversions(operation.Operand)))
            {
                Report(
                    Rules.ReadFromStoredContainer,
                    operation.Syntax,
                    "'..'",
                    operation.ElementType,
                    GetNotOwnHint(operation.Operand),
                    GetAdviceForStoredSource(operation.Operand, isRead: true));
            }

            // What the spread enumerates is used as the collection of a 'foreach' is: in place, or on a copy that was reported.
            using var handled = Handle();
            handled.Add(CheckMembersOfSpread(operation));

            base.VisitSpread(operation);
        }

        public override void VisitSwitch(ISwitchOperation operation)
        {
            CheckTypeInUnsupportedContext(operation);

            // As for an 'is' pattern: the value is examined in place.
            using var handled = Handle();
            handled.Add(operation.Value);
            bool valueIsNew = IsNew(operation.Value);

            // A 'switch' statement matches a copy of a local or a parameter as well, in two cases that the source does not show:
            // where any clause has a 'when' that is no constant, and in every build that is not optimised, where the compiler
            // wraps the value for Edit and Continue. A finding that came and went with the configuration would be worse than one
            // that is strict, so a stored value is always taken as copied here.
            bool valueIsCopied = !valueIsNew;

            // Where an 'is' pattern would match the value in place, the message says that the 'switch' statement is what copies it.
            var copiedBy = valueIsCopied && !IsCopiedForPattern(operation.Value) ? CopiedBy.SwitchStatement : CopiedBy.Nothing;

            // A clause with a 'when' runs code with its variables and then lets a later clause bind the value again, so what its
            // variables have taken stays taken for the clauses after it. What the members of a clause did stays done for every
            // later clause: see TakenParts.
            TakenParts? takenBefore = null;

            foreach (var switchCase in operation.Cases)
            {
                foreach (var clause in switchCase.Clauses)
                {
                    if (clause is not IPatternCaseClauseOperation patternClause)
                        continue;

                    var taken = takenBefore?.Clone() ?? new TakenParts();
                    CheckPattern(patternClause.Pattern, valueIsNew, handled, inputIsCopied: valueIsCopied, taken: taken, copiedBy: copiedBy);
                    ReportPartsUsedTwice(taken);
                    takenBefore = taken.ForLaterClauses(hasWhen: patternClause.Guard is not null);
                }
            }

            base.VisitSwitch(operation);
        }

        public override void VisitSwitchExpression(ISwitchExpressionOperation operation)
        {
            // Roslyn's rule has no case for a switch expression and reports every use of one, and its arms as well. Here what uses the expression
            // checks it as it checks a conditional operator (see Acquire): it gives a new value if every arm does.
            CheckTypeInUnsupportedContext(operation);

            // As for an 'is' pattern: the value is examined in place.
            using var handled = Handle();
            handled.Add(operation.Value);
            bool valueIsNew = IsNew(operation.Value);

            // A 'switch' expression matches a local in place, like an 'is' pattern, unless an arm has a 'when' that is no constant:
            // then the compiler matches a copy of the value, for every arm.
            bool isCopiedAnyway = IsCopiedForPattern(operation.Value);
            bool valueIsCopied = isCopiedAnyway ||
                (!IsNew(operation.Value) && operation.Arms.Any(static arm => arm.Guard is { ConstantValue.HasValue: false }));

            var copiedBy = valueIsCopied && !isCopiedAnyway ? CopiedBy.WhenOfSwitchExpression : CopiedBy.Nothing;

            // As for the clauses of a 'switch' statement: what an arm with a 'when' has taken stays taken for the arms after it.
            TakenParts? takenBefore = null;

            foreach (var arm in operation.Arms)
            {
                var taken = takenBefore?.Clone() ?? new TakenParts();
                CheckPattern(arm.Pattern, valueIsNew, handled, inputIsCopied: valueIsCopied, taken: taken, copiedBy: copiedBy);
                ReportPartsUsedTwice(taken);
                takenBefore = taken.ForLaterClauses(hasWhen: arm.Guard is not null);

                // The expression answers for the values of its arms: what uses it has checked them through it, or it has just been reported itself.
                handled.Add(arm.Value);
            }

            base.VisitSwitchExpression(operation);
        }

        public override void VisitTuple(ITupleOperation operation)
        {
            CheckTypeInUnsupportedContext(operation);

            // New values are moved into the tuple, as into a parameter. Anything else is left to be reported where it is visited.
            using var handled = Handle();
            handled.AddNewValues(operation.Elements);
            base.VisitTuple(operation);
        }

        public override void VisitUnaryOperator(IUnaryOperation operation)
        {
            var use = CheckMemberUse(operation.Syntax, operation.OperatorMethod);

            if (operation.OperatorMethod is not null && !operation.IsLifted)
                CheckHandedOutValue(operation.Syntax, use, operation.Type, AreCarriedValuesNew(operation.Operand));

            CheckTypeInUnsupportedContext(operation);

            using var handled = Handle();
            handled.Add(CheckOperatorOperand(operation.OperatorMethod, 0, operation.Operand));

            base.VisitUnaryOperator(operation);
        }

        public override void VisitUsing(IUsingOperation operation)
        {
            CheckTypeInUnsupportedContext(operation);

            var resource = operation.Resources;
            CheckDisposal(resource, operation.IsAsynchronous);

            if (Acquire(resource) != RefKind.None)
            {
                // The statement keeps its own copy of a value that it is given and disposes that. A new value is moved into it. The result
                // of an assignment is a copy of what the target then holds: 'using (_held = guard)' disposes the copy.
                if (types.IsNonCopyableType(resource.Type) && !IsDiscardOfNewValue(resource))
                    Report(Rules.CopyOfStoredValue, resource.Syntax, "'using' disposes", resource.Type, GetHandlerHint(resource));
                else
                    resource = null;
            }

            using var handled = Handle();
            handled.Add(resource);
            base.VisitUsing(operation);
        }

        public override void VisitUsingDeclaration(IUsingDeclarationOperation operation)
        {
            CheckDisposal(operation.DeclarationGroup, operation.IsAsynchronous);
            base.VisitUsingDeclaration(operation);
        }

        public override void VisitVariableDeclaration(IVariableDeclarationOperation operation)
        {
            CheckTypeInUnsupportedContext(operation);

            var initializer = operation.Initializer;

            if (initializer is not null)
            {
                var symbol = operation.Declarators.Single().Symbol;
                var localRefKind = symbol.RefKind;
                var sourceRefKind = Acquire(initializer.Value);

                if (types.IsNonCopyableType(symbol.Type) && !CanAssign(sourceRefKind, localRefKind))
                    Report(Rules.CopyByAssignment, initializer.Syntax, symbol.Type, GetHandlerHint(initializer.Value));
            }

            using var handled = Handle();
            handled.Add(initializer);
            handled.Add(initializer?.Value);

            base.VisitVariableDeclaration(operation);
        }

        public override void VisitVariableDeclarator(IVariableDeclaratorOperation operation)
        {
            var initializer = operation.Initializer;
            var localRefKind = operation.Symbol.RefKind;
            var sourceRefKind = Acquire(initializer?.Value);

            if (initializer is not null && types.IsNonCopyableType(operation.Symbol.Type) && !CanAssign(sourceRefKind, localRefKind))
                Report(Rules.CopyByAssignment, initializer.Syntax, operation.Symbol.Type, GetHandlerHint(initializer.Value));

            using var handled = Handle();
            handled.Add(initializer);
            handled.Add(initializer?.Value);

            base.VisitVariableDeclarator(operation);
        }

        /// <summary>
        /// Reports a constructor that is written with a body and no initializer, and calls a [RequiresCopyable] constructor of its
        /// base class with nothing written. Which constructor that is, is the compiler's choice: the body has the call as an
        /// operation that nothing in the source stands for.
        /// </summary>
        public void CheckBaseConstructorCalledWithNothingWritten()
        {
            if (context.OwningSymbol is not IMethodSymbol { MethodKind: MethodKind.Constructor, IsStatic: false } constructor ||
                !NonCopyableTypes.IsWrittenWithBody(constructor) ||
                context.OperationBlocks is not [{ Parent: IConstructorBodyOperation { Initializer: { } initializer } }, ..])
            {
                return;
            }

            // A call that is written is an operation that the source stands for, and is reported where it is visited.
            foreach (var operation in initializer.DescendantsAndSelf())
            {
                if (operation is IInvocationOperation { IsImplicit: true, TargetMethod: { MethodKind: MethodKind.Constructor } called } &&
                    types.GetUse(called) is { Kind: MemberUseKind.RequiresCopyable } use)
                {
                    context.ReportDiagnostic(CreateDiagnostic(constructor, Rules.RequiresCopyableMember, Display(use.Definition!), use.TypeArgument, use.TypeParameter!.Name));
                }
            }
        }

        public override void VisitWith(IWithOperation operation)
        {
            CheckTypeInUnsupportedContext(operation);

            // 'with' makes a copy of its operand and then sets some members of the copy. A new value is moved into it. A stored one is copied.
            // Roslyn's rule reports every 'with', three times.
            var operand = operation.Operand;

            if (!IsNew(operand) && types.IsNonCopyableType(operand.Type))
                Report(Rules.CopyOfStoredValue, operand.Syntax, "'with' changes", operand.Type, string.Empty);

            // On a record class 'with' runs the copy constructor of the record, which runs that of every base record. Each is a use
            // like a call that is written.
            for (var record = NonCopyableTypes.GetClassOf(operand.Type); record is { IsRecord: true, TypeKind: TypeKind.Class }; record = record.BaseType)
            {
                if (CheckMemberUse(operation.Syntax, NonCopyableTypes.FindCopyConstructor(record)).Kind is MemberUseKind.RequiresCopyable)
                    break;
            }

            using var handled = Handle();
            handled.Add(operand);
            handled.Add(operation.Initializer);

            base.VisitWith(operation);
        }

        private static bool IsSetMethodInvocation(IPropertyReferenceOperation operation)
        {
            if (operation.Property.SetMethod is null)
            {
                // This is either invalid code, or an assignment through a ref-returning getter
                return false;
            }

            IOperation potentialLeftSide = operation;

            while (potentialLeftSide.Parent is IParenthesizedOperation or ITupleOperation)
                potentialLeftSide = potentialLeftSide.Parent;

            return potentialLeftSide.Parent is IAssignmentOperation { Target: var target } && target == potentialLeftSide;
        }

        private static bool IsNullConstant(IOperation operation) => operation.ConstantValue is { HasValue: true, Value: null };

        /// <summary>
        /// Gets a value indicating whether an operation is a variable, so that an 'in' or 'ref readonly' parameter is given a reference to it.
        /// For anything else the compiler copies the value into a temporary and passes a reference to that. A kind that is not named here
        /// is a value.
        /// </summary>
        private static bool IsVariable(IOperation operation)
        {
            RuntimeHelpers.EnsureSufficientExecutionStack();

            return SkipParentheses(operation) switch {
                ILocalReferenceOperation or IParameterReferenceOperation or IArrayElementReferenceOperation => true,

                // 'this' of a struct is a variable; of a class it is a reference, which is not of a non-copyable type.
                IInstanceReferenceOperation { Type.IsValueType: true } => true,

                // A field of an object or a static field is a variable, and a field of a struct is one if the struct is.
                IFieldReferenceOperation { Instance: null or { Type.IsReferenceType: true } } => true,
                IFieldReferenceOperation { Instance: { } instance } => IsVariable(instance),
                IInlineArrayAccessOperation { Type.IsRefLikeType: false } element => IsVariable(element.Instance),

                // What is reached through a reference that a member returns, or through a 'ref' conditional.
                IInvocationOperation { TargetMethod: { ReturnsByRef: true } or { ReturnsByRefReadonly: true } } => true,
                IPropertyReferenceOperation { Property: { ReturnsByRef: true } or { ReturnsByRefReadonly: true } } => true,
                IImplicitIndexerReferenceOperation { IndexerSymbol: IMethodSymbol { ReturnsByRef: true } or IMethodSymbol { ReturnsByRefReadonly: true } } => true,
                IImplicitIndexerReferenceOperation { IndexerSymbol: IPropertySymbol { ReturnsByRef: true } or IPropertySymbol { ReturnsByRefReadonly: true } } => true,
                IFunctionPointerInvocationOperation { Target.Type: IFunctionPointerTypeSymbol { Signature.RefKind: not RefKind.None } } => true,
                IConditionalOperation { IsRef: true } => true,

                // A pointer dereference, an element through a pointer and '__refvalue' are variables, which Roslyn gives as operations of
                // no kind.
                { Kind: OperationKind.None, Syntax: PrefixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.PointerIndirectionExpression } } => true,
                { Kind: OperationKind.None, Syntax: ElementAccessExpressionSyntax or RefValueExpressionSyntax } => true,

                // An identity conversion that the compiler adds, as for the names of tuple elements, leaves a variable a variable.
                IConversionOperation { IsImplicit: true, Conversion.IsIdentity: true } conversion => IsVariable(conversion.Operand),
                _ => false,
            };
        }

        private static IOperation SkipParentheses(IOperation operation)
        {
            while (operation is IParenthesizedOperation parenthesized)
                operation = parenthesized.Operand;

            return operation;
        }

        /// <summary>
        /// Gets the outermost of the parentheses around an operation, or the operation itself: what its parent then is, is what uses it.
        /// </summary>
        private static IOperation SkipParenthesesUpward(IOperation operation)
        {
            while (operation.Parent is IParenthesizedOperation parenthesized)
                operation = parenthesized;

            return operation;
        }

        /// <summary>
        /// Gets the outermost of the parentheses and of the conditions that choose a reference around an operation, or the operation
        /// itself: in 'ref (flag ? ref a : ref b)' what uses the whole is what uses 'a' and 'b'.
        /// </summary>
        private static IOperation SkipChoicesUpward(IOperation operation)
        {
            while (operation.Parent is IParenthesizedOperation || (operation.Parent is IConditionalOperation { IsRef: true } chosen && !ReferenceEquals(chosen.Condition, operation)))
                operation = operation.Parent;

            return operation;
        }

        /// <summary>
        /// Gets the operations that can give the value of a conditional, a '??' or a 'switch' expression. Null for any other operation.
        /// </summary>
        private static IEnumerable<IOperation>? GetBranches(IOperation operation) => operation switch {
            IConditionalOperation conditional => conditional.WhenFalse is null ? [conditional.WhenTrue] : [conditional.WhenTrue, conditional.WhenFalse],
            ICoalesceOperation coalesce => [coalesce.Value, coalesce.WhenNull],
            ISwitchExpressionOperation switchExpression => switchExpression.Arms.Select(arm => arm.Value),
            _ => null,
        };

        /// <summary>
        /// Gets a value indicating whether the parent of an operation only passes its value on: parentheses, a conversion with no
        /// operator, a tuple, and a branch of a conditional, a '??' or a 'switch' expression. The left side of a '??' that
        /// converts it with a user-defined conversion is an argument of that conversion.
        /// </summary>
        private static bool IsPassedOn(IOperation operation) => operation.Parent switch {
            IParenthesizedOperation or ITupleOperation or IConversionOperation { OperatorMethod: null } => true,
            ICoalesceOperation coalesce => !ReferenceEquals(operation, coalesce.Value) || coalesce.ValueConversion.MethodSymbol is null,
            IConditionalOperation conditional => !ReferenceEquals(operation, conditional.Condition),
            ISwitchExpressionArmOperation arm => ReferenceEquals(operation, arm.Value),
            ISwitchExpressionOperation => operation is ISwitchExpressionArmOperation,
            _ => false,
        };

        /// <summary>
        /// Gets a value indicating whether an operation is an element of the array or the span that the compiler makes for a
        /// 'params' parameter, which is how the element is passed to that parameter. A collection of any other type is a
        /// collection expression like one that is written out: its builder or its 'Add' receives the elements.
        /// </summary>
        private static bool IsInParamsArray(IOperation operation) => operation.Parent switch {
            { IsImplicit: true, Kind: OperationKind.ArrayInitializer or OperationKind.ArrayCreation } => true,
            ICollectionExpressionOperation { IsImplicit: true, Type: var type } => type is IArrayTypeSymbol or { IsRefLikeType: true },
            _ => false,
        };

        /// <summary>
        /// Gets a value indicating whether a reference to a local or a parameter is in a lambda or a local function that does not declare
        /// it: the variable is captured, which makes it a field in fact.
        /// </summary>
        private bool IsCapturedAt(IOperation reference, ISymbol variable) =>
            !SymbolEqualityComparer.Default.Equals(GetFunctionOf(variable), GetEnclosingMethod(reference));

        /// <summary>
        /// Gets the function that a local or a parameter is a variable of: the one that declares it, and for the receiver of an
        /// extension block, which the block declares for every member of it, the member that is being checked.
        /// </summary>
        private ISymbol GetFunctionOf(ISymbol variable) =>
            variable is IParameterSymbol { ContainingSymbol: INamedTypeSymbol { IsExtension: true } } ? context.OwningSymbol : variable.ContainingSymbol;

        private RefKind GetReturnRefKind(IReturnOperation operation) => GetEnclosingMethod(operation)?.RefKind ?? RefKind.None;

        /// <summary>
        /// Gets the method, lambda or local function that an operation is in.
        /// </summary>
        private IMethodSymbol? GetEnclosingMethod(IOperation operation)
        {
            for (var current = operation.Parent; current is not null; current = current.Parent)
            {
                switch (current)
                {
                    case IAnonymousFunctionOperation anonymousFunction:
                        return anonymousFunction.Symbol;

                    case ILocalFunctionOperation localFunction:
                        return localFunction.Symbol;
                }
            }

            return context.OwningSymbol as IMethodSymbol;
        }

        private void Report(DiagnosticDescriptor rule, SyntaxNode syntax, params object?[] arguments) => Report(rule, syntax.GetLocation(), arguments);

        private void Report(DiagnosticDescriptor rule, Location? location, params object?[] arguments)
        {
            WithoutNullableAnnotations(arguments);
            context.ReportDiagnostic(Diagnostic.Create(rule, location, arguments));
        }

        /// <summary>
        /// Gets every operation of the member, block by block and each block from the outside in. The list is made once: several
        /// questions about the member as a whole go through it.
        /// </summary>
        private List<IOperation> GetAllOperations()
        {
            if (_allOperations is null)
            {
                _allOperations = [];

                foreach (var block in context.OperationBlocks)
                    _allOperations.AddRange(block.DescendantsAndSelf());
            }

            return _allOperations;
        }

        /// <summary>
        /// Opens a scope for what a visit marks as handled. What is added through the scope is handled until the scope is disposed,
        /// which is at the end of the visit that opened it.
        /// </summary>
        private HandledScope Handle() => new(this, _handledInOrder.Count);

        /// <summary>
        /// The operations that one visit has marked as handled: see <see cref="_handledOperations"/>. An operation that was handled
        /// already stays so when the scope ends. Scopes nest as the visits do, so a scope ends by taking back everything that was
        /// marked since it began.
        /// </summary>
        private readonly struct HandledScope(NonCopyableWalker walker, int start) : IDisposable
        {
            public void Add(IOperation? operation)
            {
                if (operation is not null && walker._handledOperations.Add(operation))
                    walker._handledInOrder.Add(operation);
            }

            /// <summary>
            /// Marks those of the values that are new: they are moved to where they go.
            /// </summary>
            public void AddNewValues(IEnumerable<IOperation> values)
            {
                foreach (var value in values)
                {
                    if (walker.IsNew(value))
                        Add(value);
                }
            }

            public void Dispose()
            {
                var marked = walker._handledInOrder;

                // Most scopes mark nothing that was not handled already.
                if (marked.Count == start)
                    return;

                for (int i = marked.Count - 1; i >= start; i--)
                    walker._handledOperations.Remove(marked[i]);

                marked.RemoveRange(start, marked.Count - start);
            }
        }

        /// <summary>
        /// Non-copyable types are only allowed in cases explicitly handled by this analyzer. This call handles unrecognized coding patterns (not known
        /// to be safe or unsafe), and reports a generic message if a non-copyable type is used in this context.
        /// </summary>
        private void CheckTypeInUnsupportedContext(IOperation operation)
        {
            // Roslyn gives an operation of no kind no type: the type of '__refvalue' or of a pointer dereference is that of its syntax.
            var type = operation.Kind is OperationKind.None
                ? operation.SemanticModel?.GetTypeInfo(operation.Syntax, context.CancellationToken).Type
                : operation.Type;

            // Copies of other types, and operations without a type, are allowed. The type is tested first: it is cheaper than the set, and
            // nearly every operation stops there.
            if (!types.IsNonCopyableType(type) || _handledOperations.Contains(operation))
                return;

            // What takes a copy of a stored value is named where it is known. The text that names the kind of operation is for a
            // construct that the analyzer has not been taught. A variable at its last use is a new value, and is named in the same way
            // where not even a new value is allowed.
            if ((operation.Kind is OperationKind.LocalReference or OperationKind.ParameterReference || !IsNew(operation)) && DescribeCopy(operation) is { } what)
                Report(Rules.CopyOfStoredValue, operation.Syntax, what, type, GetHandlerHint(operation));
            else
                Report(Rules.UnsupportedUse, operation.Syntax, type, operation.Kind);
        }

        /// <summary>
        /// Gets the words that say what takes a copy of a stored value, from where the value is used: '... receives', 'The tuple takes'.
        /// Null if the use is not one that is known to copy.
        /// </summary>
        private static string? DescribeCopy(IOperation operation)
        {
            var current = operation;

            // A conversion that wraps the value, as into a nullable of its type, hands the copy on to what uses the result.
            while (current.Parent is IParenthesizedOperation or IConversionOperation { OperatorMethod: null, Type.IsValueType: true })
                current = current.Parent;

            switch (current.Parent)
            {
                case IArgumentOperation { Parameter: null }:
                    return "An argument list takes";

                case IArgumentOperation { Parameter: { RefKind: RefKind.None } parameter }:
                    return $"'{Display(parameter.ContainingSymbol)}' receives";

                // An argument that is not a variable is passed to an 'in' or 'ref readonly' parameter as a temporary copy.
                case IArgumentOperation { Parameter: { RefKind: RefKind.In or RefKind.RefReadOnlyParameter } parameter } when !IsVariable(current):
                    return $"'{Display(parameter.ContainingSymbol)}' receives";

                case IInvocationOperation { TargetMethod: { ContainingType.IsExtension: true } extensionMethod } invocation when ReferenceEquals(current, invocation.Instance):
                    return $"'{Display(extensionMethod)}' receives";

                case IPropertyReferenceOperation { Property: { ContainingType.IsExtension: true } extensionProperty } reference
                    when ReferenceEquals(current, reference.Instance):
                    return $"'{Display(extensionProperty)}' receives";

                case ICompoundAssignmentOperation { OperatorMethod: { IsStatic: false, Parameters: [{ RefKind: RefKind.None }] } instanceOperator } compound
                    when ReferenceEquals(current, compound.Value):
                    return $"'{Display(instanceOperator)}' receives";

                case IMethodReferenceOperation methodReference when ReferenceEquals(current, methodReference.Instance):
                    return $"The delegate for '{Display(methodReference.Method)}' captures";

                case ITupleOperation:
                    return "The tuple takes";

                case IArrayInitializerOperation:
                    return "The array takes";

                case ICollectionExpressionOperation:
                    return "The collection takes";

                case IInterpolationOperation:
                    return "The interpolated string takes";

                case IFieldInitializerOperation or IPropertyInitializerOperation:
                    return "The initializer takes";

                case IDynamicInvocationOperation or IDynamicObjectCreationOperation or IDynamicIndexerAccessOperation:
                    return "A dynamic call receives";

                // Neither '__arglist' nor the initializer of a 'stackalloc' has an operation of its own.
                case { Kind: OperationKind.None, Syntax: InvocationExpressionSyntax { Expression.RawKind: (int)SyntaxKind.ArgListExpression } }:
                    return "An argument list takes";

                case { Kind: OperationKind.None, Syntax: var syntax } when
                    (syntax as InitializerExpressionSyntax)?.Parent is StackAllocArrayCreationExpressionSyntax or ImplicitStackAllocArrayCreationExpressionSyntax ||
                    syntax is StackAllocArrayCreationExpressionSyntax or ImplicitStackAllocArrayCreationExpressionSyntax:
                    return "The stackalloc memory takes";

                default:
                    return null;
            }
        }
    }
}
