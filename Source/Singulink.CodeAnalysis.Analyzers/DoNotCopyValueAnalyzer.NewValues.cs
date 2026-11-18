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
/// The walker's judgment of whether a value is new: what an operation gives is a new value, which is moved to where it goes, or a
/// stored one, which something else still holds, and how writable the place is that it comes from.
/// </content>
public sealed partial class DoNotCopyValueAnalyzer
{
    private sealed partial class NonCopyableWalker
    {
        private static bool CanAssign(RefKind sourceRefKind, RefKind targetRefKind) => (sourceRefKind, targetRefKind) switch {
            (RefKind.None, _) => true,
            (RefKind.Ref, RefKind.Ref or RefKind.RefReadOnly or RefKind.RefReadOnlyParameter) => true,
            (RefKind.RefReadOnly or RefKind.RefReadOnlyParameter, RefKind.RefReadOnly or RefKind.RefReadOnlyParameter) => true,
            _ => false,
        };

        private static RefKind CombineRestrictions(RefKind first, RefKind second) => (first, second) switch {
            (RefKind.RefReadOnly or RefKind.RefReadOnlyParameter, _) => RefKind.RefReadOnly,
            (_, RefKind.RefReadOnly or RefKind.RefReadOnlyParameter) => RefKind.RefReadOnly,
            (RefKind.Out, _) => RefKind.Out,
            (_, RefKind.Out) => RefKind.Out,
            (RefKind.None, RefKind.None) => RefKind.None,
            _ => RefKind.Ref,
        };

        /// <summary>
        /// Says how an operation gives its value: <see cref="RefKind.None"/> for a new value, which nobody else holds, and a kind of reference for a
        /// stored value. For a container only the difference between the two matters.
        /// </summary>
        /// <remarks>
        /// The name is that of Roslyn's analyzer, which this one is based on. <see cref="RefKind.None"/> is what the design calls a
        /// new value, and every other result a stored value, with the kind saying how writable the place is that holds it.
        /// </remarks>
        private RefKind Acquire(IOperation? operation)
        {
            RuntimeHelpers.EnsureSufficientExecutionStack();

            if (operation is null)
                return RefKind.RefReadOnly;

            switch (operation.Kind)
            {
                case OperationKind.AnonymousFunction:
                case OperationKind.DelegateCreation:
                    return RefKind.None;

                case OperationKind.ArrayCreation:
                    // An array that is created from containers is as new as what is put into it.
                    return AreInitializerValuesNew(((IArrayCreationOperation)operation).Initializer) ? RefKind.None : RefKind.RefReadOnly;

                case OperationKind.ArrayElementReference:
                    return RefKind.Ref;

                case OperationKind.Await:
                    return AnalyzeAwait((IAwaitOperation)operation, report: false) ? RefKind.None : RefKind.RefReadOnly;

                case OperationKind.Coalesce:
                    // As for a conditional, below: the result is a temporary copy of a stored value, also of one that it takes out of
                    // a stored nullable.
                    var coalesce = (ICoalesceOperation)operation;
                    return CombineRestrictions(Acquire(coalesce.Value), Acquire(coalesce.WhenNull)) is RefKind.None ? RefKind.None : RefKind.RefReadOnly;

                case OperationKind.Conditional:
                    var conditional = (IConditionalOperation)operation;
                    var eitherBranch = CombineRestrictions(Acquire(conditional.WhenTrue ?? conditional.Condition), Acquire(conditional.WhenFalse));

                    // By value, a conditional over a stored value gives a temporary copy of it: nothing that is done to that lasts.
                    return conditional.IsRef || eitherBranch is RefKind.None ? eitherBranch : RefKind.RefReadOnly;

                case OperationKind.ConditionalAccess:
                    return Acquire(((IConditionalAccessOperation)operation).WhenNotNull);

                case OperationKind.ConditionalAccessInstance:
                    // For a stored nullable value this is a temporary copy of what the nullable holds: nothing that is done to it lasts.
                    var accessReceiver = GetConditionalAccessReceiver(operation);
                    return accessReceiver is { Type.IsValueType: true } && !IsNew(accessReceiver) ? RefKind.RefReadOnly : Acquire(accessReceiver);

                case OperationKind.Conversion:
                    var conversion = (IConversionOperation)operation;
                    return conversion.OperatorMethod switch {
                        // Taking a value out of a box is reported where the conversion is visited, for every box.
                        null when IsUnboxing(conversion) => RefKind.None,

                        // A container that comes out of a type which does not mention its non-copyable type is stored, whatever gives the
                        // reference: whoever else holds the reference can read from the container too.
                        null when types.GainsNonCopyableType(conversion.Operand.Type, conversion.Type) => RefKind.RefReadOnly,

                        // An object whose own code decides what it holds, used as a framework container: the members of that hand out
                        // what the object holds as new as the object is, and nothing shows what its code put in.
                        null when IsOpenObjectUsedAsListedContainer(conversion) => RefKind.RefReadOnly,

                        // A conversion that gives a value gives a temporary: unwrapping a nullable, converting a tuple and renaming
                        // its elements each make a copy of a stored value, and nothing that is done to the copy lasts.
                        null when GivesTemporaryValue(conversion) => Acquire(conversion.Operand) is RefKind.None ? RefKind.None : RefKind.RefReadOnly,
                        null => Acquire(conversion.Operand),
                        { ReturnsByRefReadonly: true } => RefKind.RefReadOnly,
                        { ReturnsByRef: true } => RefKind.Ref,
                        var operatorMethod => AcquireHandedOutByOperator(conversion.Type, operatorMethod, AreCarriedValuesNew(conversion.Operand)),
                    };

                case OperationKind.DefaultValue:
                    return RefKind.None;

                case OperationKind.Binary:
                    // A user-defined operator is a call of its method, so what it returns is the result of a call.
                    var binary = (IBinaryOperation)operation;
                    return binary.OperatorMethod is { } binaryOperator && !binary.IsLifted
                        ? AcquireHandedOutByOperator(binary.Type, binaryOperator, AreCarriedValuesNew(binary.LeftOperand) && AreCarriedValuesNew(binary.RightOperand))
                        : RefKind.RefReadOnly;

                case OperationKind.Unary:
                    var unary = (IUnaryOperation)operation;
                    return unary.OperatorMethod is { } unaryOperator && !unary.IsLifted
                        ? AcquireHandedOutByOperator(unary.Type, unaryOperator, AreCarriedValuesNew(unary.Operand))
                        : RefKind.RefReadOnly;

                case OperationKind.FieldReference:
                    var fieldReference = (IFieldReferenceOperation)operation;
                    var field = fieldReference.Field;

                    // A ref field is a reference to a value that is stored somewhere else: how writable that is depends on the field alone,
                    // not on the value that holds the field.
                    if (field.RefKind is RefKind.Ref)
                        return RefKind.Ref;

                    if (field.RefKind is RefKind.RefReadOnly)
                        return RefKind.RefReadOnly;

                    // A non-copyable value in a field of a value type is stored inline, so read from a new value it is a new value: nothing else
                    // holds the value it is a part of. Read straight from a tuple literal it is the element that was put there. A container in
                    // a field is always stored: the field holds a reference, and whoever put it there may hold it too.
                    if (!types.IsContainer(fieldReference.Type) && fieldReference.Instance is { Type.IsValueType: true } fieldInstance)
                    {
                        if (SkipParentheses(fieldInstance) is ITupleOperation tuple && GetTupleElementIndex(field) is int index and >= 0)
                            return index < tuple.Elements.Length ? Acquire(tuple.Elements[index]) : RefKind.RefReadOnly;

                        if (IsNew(fieldInstance))
                            return RefKind.None;
                    }

                    if (field.IsReadOnly && !IsReadOnlyFieldWritable(fieldReference))
                        return RefKind.RefReadOnly;

                    // A field of a value that is reached through a read-only reference is read-only as well: a field of an 'in' parameter
                    // or of a readonly field, at any depth, and a field that is used in a readonly member. What a pointer points to is
                    // writable, and is not an operation of a kind that the tree names.
                    return fieldReference.Instance is { Type.IsValueType: true, Kind: not OperationKind.None } owner &&
                        Acquire(owner) is RefKind.RefReadOnly or RefKind.RefReadOnlyParameter
                        ? RefKind.RefReadOnly
                        : RefKind.Ref;

                case OperationKind.ImplicitIndexerReference:
                    var implicitIndexer = (IImplicitIndexerReferenceOperation)operation;
                    return implicitIndexer.IndexerSymbol switch {
                        IPropertySymbol { ReturnsByRefReadonly: true } or IMethodSymbol { ReturnsByRefReadonly: true } => RefKind.RefReadOnly,
                        IPropertySymbol { ReturnsByRef: true } or IMethodSymbol { ReturnsByRef: true } => RefKind.Ref,
                        IPropertySymbol when types.IsContainer(implicitIndexer.Type) => RefKind.RefReadOnly,
                        var indexer => AcquireHandedOutByOperator(implicitIndexer.Type, indexer, AreCarriedValuesNew(implicitIndexer.Instance)),
                    };

                case OperationKind.InlineArrayAccess:
                    // An element of an inline array is a part of the buffer, as a field is a part of a struct: a new value in a new buffer,
                    // and a reference that is as writable as the buffer otherwise.
                    return Acquire(((IInlineArrayAccessOperation)operation).Instance) switch {
                        RefKind.None => RefKind.None,
                        RefKind.RefReadOnly or RefKind.RefReadOnlyParameter => RefKind.RefReadOnly,
                        _ => RefKind.Ref,
                    };

                case OperationKind.InstanceReference:
                    // The object that an initializer fills has just been created.
                    if (((IInstanceReferenceOperation)operation).ReferenceKind is InstanceReferenceKind.ImplicitReceiver)
                        return RefKind.None;

                    return IsReadOnly(context.OwningSymbol) ? RefKind.RefReadOnly : RefKind.Ref;

                case OperationKind.Invocation:
                    var invocation = (IInvocationOperation)operation;
                    return invocation.TargetMethod switch {
                        { ReturnsByRefReadonly: true } => RefKind.RefReadOnly,
                        { ReturnsByRef: true } => RefKind.Ref,
                        var method => AcquireHandedOutByCall(invocation.Type, method, invocation.Instance, invocation.Arguments),
                    };

                case OperationKind.Literal:
                    return RefKind.None;

                case OperationKind.LocalReference:
                    var local = ((ILocalReferenceOperation)operation).Local;

                    // A variable that is never used again cannot be used as a second copy.
                    if (IsMovedAtLastUse(operation, local))
                        return RefKind.None;

                    return local.RefKind == RefKind.RefReadOnly ? RefKind.RefReadOnly : RefKind.Ref;

                case OperationKind.ObjectCreation:
                    var creation = (IObjectCreationOperation)operation;
                    return AcquireHandedOutByCall(creation.Type, creation.Constructor, null, creation.Arguments);

                case OperationKind.ParameterReference:
                    var parameter = ((IParameterReferenceOperation)operation).Parameter;

                    // The method takes the value out of the parameter and leaves a fresh one behind, so reading it is a move. A container
                    // that generic code passed a slot for is not the method's to take.
                    if (IsOwnedMovedFromParameter(parameter, operation) || IsMovedAtLastUse(operation, parameter))
                        return RefKind.None;

                    // A parameter of a primary constructor that a member uses is a field of the type in fact, and a member that cannot
                    // change 'this' reads it through a read-only reference.
                    if (parameter.ContainingSymbol is IMethodSymbol { MethodKind: MethodKind.Constructor, ContainingType.TypeKind: TypeKind.Struct } &&
                        context.OwningSymbol is IMethodSymbol { IsReadOnly: true })
                    {
                        return RefKind.RefReadOnly;
                    }

                    return parameter.RefKind is RefKind.In or RefKind.RefReadOnlyParameter ? parameter.RefKind : RefKind.Ref;

                case OperationKind.Parenthesized:
                    return Acquire(((IParenthesizedOperation)operation).Operand);

                case OperationKind.SimpleAssignment:
                    // A ref assignment gives the reference that it has just stored, which is as writable as its target says.
                    var refAssignment = (ISimpleAssignmentOperation)operation;
                    return refAssignment.IsRef ? Acquire(refAssignment.Target) : RefKind.RefReadOnly;

                case OperationKind.SwitchExpression:
                    var result = RefKind.None;

                    foreach (var arm in ((ISwitchExpressionOperation)operation).Arms)
                        result = CombineRestrictions(result, Acquire(arm.Value));

                    // As for a conditional: by value, the result is a temporary copy of a stored value.
                    return result is RefKind.None ? RefKind.None : RefKind.RefReadOnly;

                case OperationKind.PropertyReference:
                    var propertyReference = (IPropertyReferenceOperation)operation;
                    return propertyReference.Property switch {
                        { ReturnsByRefReadonly: true } => RefKind.RefReadOnly,
                        { ReturnsByRef: true } => RefKind.Ref,

                        // A property or an indexer exposes state: a container that is read from one is stored, whatever it is read from. Only a
                        // method that returns a container is taken to hand it over. A non-copyable value that a property returns by value is
                        // new, by the check of its body or, for a listed one, by the report of the read where it is visited.
                        // A property of an anonymous type is a field in fact, and no body check stands behind it.
                        // The one exception is the task of a completion source that the member has just made.
                        { ContainingType.IsAnonymousType: true } => RefKind.RefReadOnly,
                        _ => types.IsContainer(propertyReference.Type) && !IsOwnedTaskRead(propertyReference) ? RefKind.RefReadOnly : RefKind.None,
                    };

                case OperationKind.Throw:
                    return RefKind.None;

                case OperationKind.Tuple:
                    // A tuple of a non-copyable type is a new value: its non-copyable elements are checked one by one where the tuple is visited.
                    // A tuple of containers is as new as what is put into it.
                    if (types.IsNonCopyableType(operation.Type))
                        return RefKind.None;

                    return AreContainersNew(((ITupleOperation)operation).Elements) ? RefKind.None : RefKind.RefReadOnly;

                case OperationKind.TypeParameterObjectCreation:
                    return RefKind.None;

                case OperationKind.With:
                    // The copy that 'with' makes of a stored operand is reported at the operand. What it gives is a value of its own.
                    return RefKind.None;

                case OperationKind.InterpolatedStringHandlerCreation:
                    return RefKind.None;

                case OperationKind.CollectionExpression:
                    return AreContainersNew(((ICollectionExpressionOperation)operation).Elements) ? RefKind.None : RefKind.RefReadOnly;

                case OperationKind.FunctionPointerInvocation:
                    // What a function pointer returns by reference is as writable as the reference. Its body is not known: what it returns by
                    // value is a stored value.
                    return ((IFunctionPointerInvocationOperation)operation).Target.Type is IFunctionPointerTypeSymbol { Signature.RefKind: RefKind.Ref }
                        ? RefKind.Ref
                        : RefKind.RefReadOnly;

                case OperationKind.None when IsVariable(operation):
                    // A pointer dereference, an element through a pointer and '__refvalue' are writable variables: used in place they are
                    // what they are. By value they are a stored value, as is every other operation that the analyzer has no judgment for.
                    return RefKind.Ref;

                default:
                    return RefKind.RefReadOnly;
            }

            static bool IsReadOnly(ISymbol symbol) => symbol switch {
                IFieldSymbol field => field.IsReadOnly,
                IPropertySymbol property => property.IsReadOnly,
                IMethodSymbol method => method.IsReadOnly,
                ITypeSymbol type => type.IsReadOnly,
                _ => false,
            };
        }

        /// <summary>
        /// Gets a value indicating whether a readonly field can be written where it is read: a field of 'this' in a constructor or an init
        /// accessor of its type, and a static one in the static constructor or a static initializer. The compiler then calls a member on
        /// the field itself and not on a copy. A lambda or a local function is a method of its own, where that does not hold.
        /// </summary>
        private bool IsReadOnlyFieldWritable(IFieldReferenceOperation reference)
        {
            var field = reference.Field;
            var owner = context.OwningSymbol;

            if (!SymbolEqualityComparer.Default.Equals(owner.ContainingType?.OriginalDefinition, field.ContainingType.OriginalDefinition))
                return false;

            if (GetEnclosingMethod(reference) is { } enclosing && !SymbolEqualityComparer.Default.Equals(enclosing, owner))
                return false;

            if (field.IsStatic)
                return owner is IMethodSymbol { MethodKind: MethodKind.StaticConstructor } or IFieldSymbol { IsStatic: true } or IPropertySymbol { IsStatic: true };

            return reference.Instance is IInstanceReferenceOperation { ReferenceKind: InstanceReferenceKind.ContainingTypeInstance } &&
                owner is IMethodSymbol { MethodKind: MethodKind.Constructor } or IMethodSymbol { IsInitOnly: true };
        }

        // An object whose own code decides what it holds, used as a framework container. The same goes for an object of a class that
        // derives from a listed container, as 'class Guards : List<Guard>' does, where it is used as that container or as an interface
        // of it: the code of the class can keep 'this' or hand it out.
        private bool IsOpenObjectUsedAsListedContainer(IConversionOperation conversion) => types.LosesHolder(conversion.Operand.Type, conversion.Type);

        /// <summary>
        /// Gets a value indicating whether an operation gives an object of a class that derives from a listed container, seen through
        /// parentheses and conversions. What the members that the class inherits hand out of such an object is never new: see
        /// <see cref="NonCopyableTypes.DerivesFromListedContainer"/>.
        /// </summary>
        private bool IsObjectWithInheritedMembers(IOperation operation) =>
            Unwrap(operation).Any(unwrapped => types.IsNeverNewForItsMembers(unwrapped.Type));

        /// <summary>
        /// Gets a value indicating whether an operation gives a new value: one that nothing else holds, so that it is moved to where
        /// it goes and no copy is made.
        /// </summary>
        /// <remarks>
        /// The questions of this family differ in how far they look into the value. <see cref="IsNew"/> asks about the value itself.
        /// <see cref="IsNewWithNothingStored"/> asks also that what was put into it is new, at every depth.
        /// <see cref="AreCarriedValuesNew"/> is that question for a value that carries a non-copyable type, and true for any other.
        /// <see cref="IsNewIfContainer"/> and <see cref="AreContainersNew"/> ask it of the containers among some values only,
        /// <see cref="AreInitializerValuesNew"/> of the values of an array initializer, and <see cref="AreInputsNew"/> of what a member
        /// is called on and with.
        /// </remarks>
        private bool IsNew(IOperation? operation) => Acquire(operation) is RefKind.None;

        /// <summary>
        /// Gets how a member gives a value that it returns by value. A non-copyable value always counts as new here: if it is a copy, the member
        /// use is reported where it is visited, and nothing that uses the result needs to report it again. A container is as stored as what a
        /// listed member was called on and with.
        /// </summary>
        private RefKind AcquireHandedOutByCall(ITypeSymbol? type, ISymbol? member, IOperation? instance, ImmutableArray<IArgumentOperation> arguments)
        {
            if (member is null || !types.IsContainer(type))
                return RefKind.None;

            // An object that has the members of a framework container, and whose own code decides what it holds, is never a new value:
            // those members would hand out as new what its code put in.
            if (KeepsWhatItReturns(member) || types.IsOpenWithListedMembers(type))
                return RefKind.RefReadOnly;

            // With a container bound to a marked type parameter, what the generic hands out through a type that mentions it is stored,
            // whatever the member was called on and with. The one exception is a pure move of locals that the method owns: the only
            // thing that went in is a reference that nobody else holds, and the body is checked to hand out nothing else.
            if (types.HandsOutBoundContainer(member, type))
            {
                bool isMove = IsPureMoveCall(member, instance, arguments) && arguments.All(a => !CarriesNonCopyableType(a.Value) || IsOwnedForMove(a.Value, a));
                return isMove ? RefKind.None : RefKind.RefReadOnly;
            }

            if (!types.FollowsInputs(types.GetUse(member), type))
                return RefKind.None;

            return AreInputsNew(instance, arguments) ? RefKind.None : RefKind.RefReadOnly;
        }

        /// <summary>
        /// The same for an operator, a conversion or an implicit index, whose inputs the caller has judged. None of them has a 'ref'
        /// parameter, so none can be a pure move, which is the one thing that the judgment of a call adds.
        /// </summary>
        private RefKind AcquireHandedOutByOperator(ITypeSymbol? type, ISymbol? member, bool inputsAreNew)
        {
            if (member is null || !types.IsContainer(type))
                return RefKind.None;

            if (KeepsWhatItReturns(member) || types.IsOpenWithListedMembers(type) || types.HandsOutBoundContainer(member, type))
                return RefKind.RefReadOnly;

            if (inputsAreNew || !types.FollowsInputs(types.GetUse(member), type))
                return RefKind.None;

            return RefKind.RefReadOnly;
        }

        /// <summary>
        /// Gets a value indicating whether the return value of a method is marked [Borrowed]: the method keeps the container that it returns,
        /// so the result is stored, as what a property returns is.
        /// </summary>
        private static bool KeepsWhatItReturns(ISymbol member) => member is IMethodSymbol method && NonCopyableTypes.HasBorrowedReturn(method);

        /// <summary>
        /// Gets a value indicating whether everything that a member is called on and with is a new value: the receiver and every argument that is,
        /// or carries, a non-copyable type.
        /// </summary>
        /// <param name="instance">The receiver, if any.</param>
        /// <param name="arguments">The arguments.</param>
        /// <param name="forValue">True if the question is whether a value of a non-copyable type that the member hands out is new. A
        /// container that is bound to a type parameter as itself, as the list in <c>new KeyValuePair&lt;Guard, List&lt;Guard&gt;&gt;(guard, list)</c>
        /// is, is then no source of that value: the member can only hold it as the container it is.</param>
        private bool AreInputsNew(IOperation? instance, ImmutableArray<IArgumentOperation> arguments, bool forValue = false)
        {
            if (instance is not null && !AreCarriedValuesNew(instance))
                return false;

            foreach (var argument in arguments)
            {
                if (argument.Parameter?.RefKind is RefKind.Out)
                    continue;

                if (forValue && argument.Parameter?.OriginalDefinition.Type is ITypeParameterSymbol && types.IsContainer(SkipWrappingConversions(argument.Value).Type))
                    continue;

                if (!AreCarriedValuesNew(argument.Value))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Gets a value indicating whether an operation gives a new value, or one that carries no non-copyable type and so does not count.
        /// </summary>
        private bool AreCarriedValuesNew(IOperation operation) =>
            !CarriesNonCopyableType(operation) || (!IsObjectWithInheritedMembers(operation) && IsNewWithNothingStored(operation));

        /// <summary>
        /// Gets a value indicating whether an operation gives a new value that holds nothing stored: an object, a struct or a tuple that is
        /// created is as new as what is put into it by its arguments and its initializer.
        /// </summary>
        /// <remarks>
        /// This is more than <see cref="IsNew"/> asks. A tuple that is made of a new guard and a stored list is a new value, which can be
        /// assigned without a copy, but what a listed member reads out of it is not new.
        /// </remarks>
        private bool IsNewWithNothingStored(IOperation operation)
        {
            RuntimeHelpers.EnsureSufficientExecutionStack();

            if (!IsNew(operation))
                return false;

            while (true)
            {
                switch (operation)
                {
                    case IParenthesizedOperation parenthesized:
                        operation = parenthesized.Operand;
                        continue;

                    case IConversionOperation { OperatorMethod: null } conversion when !IsUnboxing(conversion):
                        operation = conversion.Operand;
                        continue;

                    case ITupleOperation tuple:
                        return tuple.Elements.All(AreCarriedValuesNew);

                    case IObjectCreationOperation creation:
                        // A listed constructor of a non-copyable struct that is given a stored value is reported where the creation is visited,
                        // so its arguments are not held against it a second time.
                        bool argumentsAreJudged = creation.Constructor is { } constructor &&
                            types.IsNonCopyableType(creation.Type) &&
                            types.GetUse(constructor).Kind is MemberUseKind.Listed;

                        return (argumentsAreJudged || creation.Arguments.All(a => a.Parameter?.RefKind is RefKind.Out || AreCarriedValuesNew(a.Value))) &&
                            IsInitializerNewWithNothingStored(creation.Initializer);

                    default:
                        return GetBranches(operation) is not { } branches || branches.All(AreCarriedValuesNew);
                }
            }
        }

        private bool IsInitializerNewWithNothingStored(IObjectOrCollectionInitializerOperation? initializer)
        {
            RuntimeHelpers.EnsureSufficientExecutionStack();

            if (initializer is null)
                return true;

            foreach (var item in initializer.Initializers)
            {
                bool isExclusive = item switch {
                    // Member = value, where the value can be another initializer.
                    ISimpleAssignmentOperation { Value: IObjectOrCollectionInitializerOperation nested } => IsInitializerNewWithNothingStored(nested),
                    ISimpleAssignmentOperation assignment => AreCarriedValuesNew(assignment.Value),
                    IMemberInitializerOperation member => IsInitializerNewWithNothingStored(member.Initializer),

                    // An element of a collection initializer.
                    IInvocationOperation add => add.Arguments.All(a => AreCarriedValuesNew(a.Value)),
                    _ => true,
                };

                if (!isExclusive)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Gets a value indicating whether a conversion takes a value of a non-copyable type out of a box: it has no operator and goes from
        /// object, dynamic, an interface or ValueType to the non-copyable type. That is a copy out of a heap object that others can hold.
        /// </summary>
        /// <summary>
        /// Gets a value indicating whether a conversion with no operator makes a value of its own out of its operand: one between
        /// two value types that is not a conversion of a type to itself. '(Guard)_maybe' unwraps a copy out of the nullable, and a
        /// tuple conversion makes a new tuple, also one that only changes the names of the elements.
        /// </summary>
        private static bool GivesTemporaryValue(IConversionOperation conversion) =>
            conversion is { Type: { IsValueType: true } to, Operand.Type: { IsValueType: true } from } && !SymbolEqualityComparer.Default.Equals(from, to);

        private bool IsUnboxing(IConversionOperation conversion) =>
            conversion.OperatorMethod is null &&
            TakesOutOfBox(conversion.Operand.Type, conversion.Type) &&
            !IsNullConstant(conversion.Operand);

        private bool TakesOutOfBox(ITypeSymbol? source, ITypeSymbol? target)
        {
            RuntimeHelpers.EnsureSufficientExecutionStack();

            if (IsBoxType(source))
                return types.IsNonCopyableType(target);

            // A conversion of a tuple converts its elements one by one.
            if (source is INamedTypeSymbol { IsTupleType: true, TupleElements: var sourceElements } &&
                target is INamedTypeSymbol { IsTupleType: true, TupleElements: var targetElements } &&
                sourceElements.Length == targetElements.Length)
            {
                for (int i = 0; i < sourceElements.Length; i++)
                {
                    if (TakesOutOfBox(sourceElements[i].Type, targetElements[i].Type))
                        return true;
                }
            }

            return false;
        }

        private static bool IsBoxType(ITypeSymbol? type) =>
            type is { TypeKind: TypeKind.Dynamic or TypeKind.Interface } or { SpecialType: SpecialType.System_Object or SpecialType.System_ValueType } ||
            IsUnion(type);

        /// <summary>
        /// Gets a value indicating whether a type is a union, or a nullable of one: a struct or a class that implements
        /// System.Runtime.CompilerServices.IUnion, as a 'union' declaration of C# 15 makes one, or that carries
        /// System.Runtime.CompilerServices.UnionAttribute, which is what the compiler itself goes by. It holds its case value in a
        /// field of type object, so it is a box: a copy of the union shares the box, and a pattern on it unboxes a new copy of a
        /// value that it holds.
        /// </summary>
        private static bool IsUnion(ITypeSymbol? type)
        {
            if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T, TypeArguments: [var underlying] })
                type = underlying;

            if (type is not INamedTypeSymbol { TypeKind: TypeKind.Struct or TypeKind.Class } union)
                return false;

            foreach (var attribute in union.OriginalDefinition.GetAttributes())
            {
                if (attribute.AttributeClass is { Name: "UnionAttribute", ContainingType: null } marked && NonCopyableTypes.IsInNamespace(marked, "System.Runtime.CompilerServices"))
                    return true;
            }

            foreach (var implemented in union.AllInterfaces)
            {
                if (implemented is
                    {
                        Name: "IUnion",
                        ContainingNamespace:
                        {
                            Name: "CompilerServices",
                            ContainingNamespace: { Name: "Runtime", ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true } },
                        },
                    })
                {
                    return true;
                }
            }

            return false;
        }

        // An argument may have been converted to a type that hides what it carries, such as object. This is asked of nearly every value,
        // so it walks down by itself and allocates nothing: see Unwrap for the same walk as a sequence.
        private bool CarriesNonCopyableType(IOperation operation)
        {
            for (IOperation? current = operation; current is not null;)
            {
                if (types.Carries(current.Type))
                    return true;

                current = current switch {
                    IConversionOperation { OperatorMethod: null } conversion => conversion.Operand,
                    IParenthesizedOperation parenthesized => parenthesized.Operand,
                    _ => null,
                };
            }

            return false;
        }

        /// <summary>
        /// Gets an operation and then, as long as it is in parentheses or converted with no operator, what is inside: the same value
        /// under each of the types that it was given on its way.
        /// </summary>
        private static IEnumerable<IOperation> Unwrap(IOperation operation)
        {
            for (IOperation? current = operation; current is not null;)
            {
                yield return current;

                current = current switch {
                    IConversionOperation { OperatorMethod: null } conversion => conversion.Operand,
                    IParenthesizedOperation parenthesized => parenthesized.Operand,
                    _ => null,
                };
            }
        }

        private bool AreInitializerValuesNew(IArrayInitializerOperation? initializer)
        {
            RuntimeHelpers.EnsureSufficientExecutionStack();

            if (initializer is null)
                return true;

            foreach (var value in initializer.ElementValues)
            {
                if (value is IArrayInitializerOperation nested ? !AreInitializerValuesNew(nested) : !IsNewIfContainer(value))
                    return false;
            }

            return true;
        }

        private bool AreContainersNew(IEnumerable<IOperation> values)
        {
            foreach (var value in values)
            {
                if (!IsNewIfContainer(value))
                    return false;
            }

            return true;
        }

        private bool IsNewIfContainer(IOperation value)
        {
            // A spread element puts the elements of a collection into the new one: containers from a stored collection are held there too.
            if (value is ISpreadOperation spread)
                return !types.IsContainer(spread.ElementType) || IsNew(spread.Operand);

            return !types.IsContainer(SkipWrappingConversions(value).Type) || IsNew(value);
        }

        /// <summary>
        /// Gets a value indicating whether a value is a view, also one that is used as an interface that it implements.
        /// </summary>
        private bool IsViewBehindConversions(IOperation? operation) =>
            operation is not null && (types.IsView(operation.Type) || types.IsView(SkipWrappingConversions(operation).Type));

        /// <summary>
        /// Gets the operand of the conversions and parentheses around a value that do not change what it is: a conversion from one non-copyable
        /// type to another, or one that keeps a container a container.
        /// </summary>
        private IOperation SkipWrappingConversions(IOperation operation)
        {
            while (true)
            {
                switch (operation)
                {
                    case IParenthesizedOperation parenthesized:
                        operation = parenthesized.Operand;
                        continue;

                    case IConversionOperation { OperatorMethod: null } conversion when types.Carries(conversion.Operand.Type):
                        operation = conversion.Operand;
                        continue;

                    default:
                        return operation;
                }
            }
        }

        private static int GetTupleElementIndex(IFieldSymbol field)
        {
            // The first seven elements of a tuple are the fields Item1 to Item7 of the tuple type itself.
            const string Prefix = "Item";
            string? name = field.CorrespondingTupleField?.Name;

            if (name is not null && name.StartsWith(Prefix, StringComparison.Ordinal) && int.TryParse(name[Prefix.Length..], out int number))
                return number - 1;

            return -1;
        }
    }
}
