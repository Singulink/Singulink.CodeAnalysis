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
/// The walker's checks of member uses: a member of an unmarked generic with a non-copyable type, what a listed member hands out, a
/// member that runs on a copy, and the members that the compiler calls for 'await', 'foreach', 'using' and operators.
/// </content>
public sealed partial class DoNotCopyValueAnalyzer
{
    private sealed partial class NonCopyableWalker
    {
        // The methods, local functions and lambdas with a 'yield' whose parameters have been checked.
        private HashSet<IMethodSymbol>? _iterators;

        /// <summary>
        /// Reports the combination of delegates whose type has a by-value parameter of a non-copyable type: every target of a
        /// delegate with several targets gets a copy of its own of the argument.
        /// </summary>
        private void CheckCombinedDelegates(SyntaxNode syntax, ITypeSymbol? type)
        {
            if (type is not INamedTypeSymbol { TypeKind: TypeKind.Delegate, DelegateInvokeMethod: { } invoke })
                return;

            foreach (var parameter in invoke.Parameters)
            {
                if (parameter.RefKind is RefKind.None && types.IsNonCopyableType(parameter.Type))
                {
                    Report(Rules.DelegatesCombined, syntax, type, parameter.Type);
                    return;
                }
            }
        }

        /// <summary>
        /// Reports a value of a non-copyable type that an operation hands to two of the members that it runs, where each takes it by
        /// value: the compiler evaluates it once and passes it to both, new value or not. That is an argument of an indexer, and the
        /// receiver of an extension block, which is a parameter of each of its members. See
        /// <see cref="CarriedSymbols.GetMembersRunOnOneValue"/> for the operations that do this.
        /// </summary>
        private void CheckMembersRunOnOneValue(IOperation operation)
        {
            var members = CarriedSymbols.GetMembersRunOnOneValue(operation, out var receiver, out var arguments);

            if (members.IsEmpty)
                return;

            foreach (var argument in arguments)
            {
                if (argument.Parameter is { RefKind: RefKind.None } parameter && types.IsNonCopyableType(parameter.Type))
                    Report(Rules.ValuePassedTwice, argument.Value.Syntax, parameter.Type, DescribeMembers(members));
            }

            if (receiver is null || !types.IsNonCopyableType(receiver.Type))
                return;

            var taking = members.Where(TakesReceiverByValue).ToImmutableArray();

            if (taking.Length > 1)
                Report(Rules.ValuePassedTwice, receiver.Syntax, receiver.Type, DescribeMembers(taking));

            // The getter and the setter of one property, or the members in turn.
            static string DescribeMembers(ImmutableArray<IMethodSymbol> members) =>
                members is [{ AssociatedSymbol: IPropertySymbol property, MethodKind: MethodKind.PropertyGet }, { MethodKind: MethodKind.PropertySet } setter] &&
                SymbolEqualityComparer.Default.Equals(setter.AssociatedSymbol, property)
                    ? $"the getter and the setter of '{Display(property)}'"
                    : string.Join(" and ", members.Select(member => $"'{Display(member.AssociatedSymbol ?? member)}'"));
        }

        /// <summary>
        /// Gets a value indicating whether the compiler runs a member on a copy of its receiver: a member that is not readonly, called
        /// through a read-only reference to a value of a non-copyable type. With a value of a type parameter the compiler cannot know that
        /// a member is readonly, so it copies for every member.
        /// </summary>
        private bool IsCalledOnCopy(IOperation instance, ITypeSymbol? receiverType, bool isReadOnlyMember)
        {
            // The temporary copy that '?.' makes of a stored nullable is judged where the conditional access is visited.
            if (instance is IConditionalAccessInstanceOperation)
                return false;

            bool mayChangeReceiver = instance.Type is ITypeParameterSymbol { IsReferenceType: false } typeParameter
                ? types.IsNonCopyableType(typeParameter)
                : types.IsNonCopyableType(receiverType) && !isReadOnlyMember;

            if (!mayChangeReceiver)
                return false;

            // A query expression evaluates the source of its 'from' clause into a temporary and calls its method on that, whatever
            // the source is, where the same call written out runs in place.
            return Acquire(instance) is RefKind.In or RefKind.RefReadOnlyParameter || (IsSourceOfQuery(instance) && !IsNew(instance));
        }

        /// <summary>
        /// Gets a value indicating whether an operation is what a query expression calls a method on that it writes no call for: the
        /// source of a 'from' clause, on which the compiler calls 'Select', 'Where' or whatever the next clause stands for.
        /// </summary>
        private static bool IsSourceOfQuery(IOperation instance)
        {
            if (SkipParenthesesUpward(instance).Parent is not IInvocationOperation { Syntax: not InvocationExpressionSyntax } call ||
                !ReferenceEquals(SkipParentheses(call.Instance!), SkipParentheses(instance)))
            {
                return false;
            }

            for (IOperation? current = call.Parent; current is not null and not (IAnonymousFunctionOperation or ILocalFunctionOperation); current = current.Parent)
            {
                if (current is ITranslatedQueryOperation)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Reports a member that the compiler runs on a defensive copy of its receiver. Gets false for a receiver of a kind that is not known
        /// to be a reference at all, such as what a pointer points to: that is left to be reported, or not, where the receiver is visited.
        /// </summary>
        private bool ReportMemberOnCopy(IOperation instance, ISymbol member)
        {
            bool isReference = SkipParentheses(instance).Kind is
                OperationKind.FieldReference or
                OperationKind.ParameterReference or
                OperationKind.LocalReference or
                OperationKind.InstanceReference or
                OperationKind.ArrayElementReference or
                OperationKind.InlineArrayAccess or
                OperationKind.ImplicitIndexerReference or
                OperationKind.Invocation or
                OperationKind.PropertyReference or
                OperationKind.SimpleAssignment or
                OperationKind.Conditional or
                OperationKind.Coalesce or
                OperationKind.SwitchExpression;

            // A conversion that makes a value of its own is a temporary copy like those. A cast of a type to itself is left to be
            // reported where it is visited.
            if (!isReference && !(SkipParentheses(instance) is IConversionOperation { OperatorMethod: null } conversion && GivesTemporaryValue(conversion)))
                return false;

            var rule = instance.Type is ITypeParameterSymbol ? Rules.MemberOnReadOnlyTypeParameter : Rules.MemberOnReadOnlyReference;
            Report(rule, instance.Syntax, instance.Type, Display(member.OriginalDefinition));
            return true;
        }

        /// <summary>
        /// Gets a value indicating whether the receiver of a member of an extension block is a stored value of a non-copyable type that the
        /// block takes by value, as 'extension(Guard guard)' does.
        /// </summary>
        private bool IsCopiedIntoExtensionReceiver(IOperation instance, ISymbol member) =>
            NonCopyableTypes.GetExtensionReceiver(member) is { } receiverParameter &&
            types.IsNonCopyableType(instance.Type) &&
            !CanAssign(Acquire(instance), receiverParameter.RefKind) &&
            !types.IsBorrowing(member);

        /// <summary>
        /// Gets a value indicating whether a temporary copy of a value is only the receiver of a member that cannot change it: a readonly
        /// method or getter, a member that the struct inherits from a class, or a field. Such a copy does no harm.
        /// </summary>
        private bool IsOnlyReceiverOfReadOnlyMember(IOperation? temporary)
        {
            // The compiler cannot tell which members of a type parameter are readonly.
            if (temporary is null || temporary.Type is ITypeParameterSymbol)
                return false;

            return temporary.Parent switch {
                IInvocationOperation invocation when ReferenceEquals(temporary, invocation.Instance) =>
                    invocation.TargetMethod.IsReadOnly || invocation.TargetMethod.ContainingType is { IsReferenceType: true },
                IPropertyReferenceOperation propertyReference when ReferenceEquals(temporary, propertyReference.Instance) =>
                    (IsSetMethodInvocation(propertyReference) ? propertyReference.Property.SetMethod : propertyReference.Property.GetMethod) is { IsReadOnly: true },
                IFieldReferenceOperation fieldReference => ReferenceEquals(temporary, fieldReference.Instance),
                _ => false,
            };
        }

        /// <summary>
        /// Gets a value indicating whether a temporary copy that a member hands out is only read: it is only the receiver of a member
        /// that cannot change it, and nothing that carries a non-copyable type comes out of it. A field of a non-copyable type that
        /// is read out of it is a copy of what the stored value still holds, and has to be only read in the same way. What a readonly
        /// member hands out of it is one too: 'GetResult' of the awaiter of a stored value task hands out the result that the task
        /// still holds.
        /// </summary>
        private bool IsOnlyReadAsTemporary(IOperation? temporary)
        {
            RuntimeHelpers.EnsureSufficientExecutionStack();

            if (!IsOnlyReceiverOfReadOnlyMember(temporary))
                return false;

            return temporary!.Parent switch {
                IFieldReferenceOperation field => !types.IsNonCopyableType(field.Type) || IsOnlyReadAsTemporary(field),
                var member => !types.Carries(member?.Type),
            };
        }

        /// <summary>
        /// Gets the operation that stands for the receiver of a conditional access in the part that runs when the receiver is not null.
        /// </summary>
        private static IOperation? FindConditionalAccessInstance(IConditionalAccessOperation access)
        {
            foreach (var operation in access.WhenNotNull.DescendantsAndSelf())
            {
                if (operation is IConditionalAccessInstanceOperation && ReferenceEquals(access.Operation, GetConditionalAccessReceiver(operation)))
                    return operation;
            }

            return null;
        }

        private bool IsArgumentOfBorrowingMember(IArgumentOperation argument)
        {
            if (!types.IsNonCopyableType(argument.Value.Type))
                return false;

            ISymbol? member = argument.Parent switch {
                IInvocationOperation invocation => invocation.TargetMethod,
                IObjectCreationOperation creation => creation.Constructor,
                IPropertyReferenceOperation propertyReference => propertyReference.Property,
                _ => null,
            };

            return member is not null && types.IsBorrowing(member);
        }

        /// <summary>
        /// Reports the by-value parameters of a non-copyable type of a method, a local function or a lambda that has a 'yield': the compiler
        /// keeps them in the object that the iterator returns and copies them into every enumerator.
        /// </summary>
        private void CheckIteratorParameters(IMethodSymbol? iterator)
        {
            if (iterator is null || !(_iterators ??= new(SymbolEqualityComparer.Default)).Add(iterator))
                return;

            // An iterator that returns an enumerator and not a sequence keeps its arguments once.
            if (iterator.ReturnType.OriginalDefinition is
                { SpecialType: SpecialType.System_Collections_IEnumerator or SpecialType.System_Collections_Generic_IEnumerator_T } or
                {
                    Name: "IAsyncEnumerator",
                    ContainingNamespace: { Name: "Generic", ContainingNamespace: { Name: "Collections", ContainingNamespace.Name: "System" } },
                })
            {
                return;
            }

            // The receiver of an extension block is kept and copied like any other parameter. It is declared by the block, for every
            // member of it, so the finding stands at the member that is the iterator.
            foreach (var parameter in NonCopyableTypes.GetParametersWithReceiver(iterator))
            {
                if (parameter.RefKind is RefKind.None && types.IsNonCopyableType(parameter.Type))
                {
                    var declared = parameter.ContainingSymbol is IMethodSymbol ? parameter.Locations.FirstOrDefault(l => l.IsInSource) : null;
                    Report(Rules.IteratorParameter, declared ?? iterator.Locations.FirstOrDefault(l => l.IsInSource), parameter.Type, parameter.Name, Display(iterator));
                }
            }
        }

        /// <summary>
        /// Checks an operand of a user-defined operator, which is an argument of the operator method: a new value is moved into the parameter,
        /// a stored one can be passed by reference, and a stored one that the operator takes by value is copied, which is reported. Gets the
        /// operand if this has judged it, and null for one that the operator does not take as it is, as with a lifted operator: that is left
        /// to the visit of the operand.
        /// </summary>
        private IOperation? CheckOperatorOperand(IMethodSymbol? operatorMethod, int index, IOperation operand)
        {
            // An operator with fewer parameters than operands is not valid code. The analyzer also runs in the editor on code that is
            // being typed.
            if (operatorMethod is null || index >= operatorMethod.Parameters.Length || !types.IsNonCopyableType(operand.Type))
                return null;

            var parameter = operatorMethod.Parameters[index];

            // A lifted operator takes the value out of a nullable first, which is not this finding.
            if (!SymbolEqualityComparer.Default.Equals(parameter.Type, operand.Type))
                return null;

            // An operator that a list marks 'borrows' only reads the copy.
            if (!CanAssign(Acquire(operand), parameter.RefKind) && !(parameter.RefKind is RefKind.None && types.IsBorrowing(operatorMethod)))
                Report(Rules.CopyOfStoredValue, operand.Syntax, $"'{Display(operatorMethod)}' receives", operand.Type, GetHandlerHint(operand));

            return operand;
        }

        private bool CheckForEachGetEnumerator(IForEachLoopOperation operation, IOperation collection, out bool isCalledInPlace)
        {
            isCalledInPlace = true;

            if (operation.Syntax is CommonForEachStatementSyntax syntax &&
                operation.SemanticModel?.GetForEachStatementInfo(syntax).GetEnumeratorMethod is { } getEnumeratorMethod)
            {
                // The question is asked where it is asked for a call that is written, so that the two agree: through an 'in' or a
                // 'ref readonly' parameter, and for a value of a type parameter whatever the method is.
                if (IsCalledOnCopy(collection, getEnumeratorMethod.ReceiverType, getEnumeratorMethod.IsReadOnly))
                {
                    // The compiler calls GetEnumerator on a copy. A receiver of a kind that is not known to be a reference is left to be
                    // reported where it is visited.
                    if (getEnumeratorMethod is not { IsStatic: false, IsExtensionMethod: false } || !ReportMemberOnCopy(collection, getEnumeratorMethod))
                        isCalledInPlace = false;
                }

                return true;
            }

            // A 'foreach' with no GetEnumerator method does not compile. The analyzer also runs in the editor on code that is being typed.
            return false;
        }

        /// <summary>
        /// Gets the method that the compiler calls for a range of an array, for an array with the given element type.
        /// </summary>
        private IMethodSymbol? GetSubArrayMethod(ITypeSymbol elementType)
        {
            var method = context.Compilation.GetTypeByMetadataName("System.Runtime.CompilerServices.RuntimeHelpers")?
                .GetMembers("GetSubArray")
                .OfType<IMethodSymbol>()
                .FirstOrDefault(m => m.Arity is 1);

            return method?.Construct(elementType);
        }

        private static IOperation? GetConditionalAccessReceiver(IOperation instance)
        {
            // The instance stands for the receiver of the nearest conditional access that has it in its 'when not null' part.
            for (IOperation child = instance, parent = instance.Parent!; parent is not null; child = parent, parent = parent.Parent!)
            {
                if (parent is IConditionalAccessOperation access && ReferenceEquals(access.WhenNotNull, child))
                    return access.Operation;
            }

            // An instance with no access around it is not valid code. The analyzer also runs in the editor on code that is being typed.
            return null;
        }

        /// <summary>
        /// Reports the use of a member of a generic with a non-copyable type or a container bound to a type parameter that is not marked, unless the
        /// member is on the safe list, and the use of a member that requires a copyable type.
        /// </summary>
        private MemberUse CheckMemberUse(SyntaxNode syntax, ISymbol? member)
        {
            if (member is null)
                return default;

            var use = types.GetUse(member);

            switch (use.Kind)
            {
                case MemberUseKind.UnlistedNonCopyable when use.ListedFor is not null && !types.IsCloningMember(use.Definition!):
                    Report(Rules.MemberListedForOtherTypeParameter, syntax, Display(use.Definition!), use.TypeArgument, use.TypeParameter!.Name, use.ListedFor);
                    break;

                case MemberUseKind.UnlistedNonCopyable:
                    // A type parameter that is marked in an assembly that was not built with the analyzer cannot be marked any better.
                    Report(
                        types.IsCloningMember(use.Definition!)
                            ? Rules.UnlistedMemberThatClones
                            : NonCopyableTypes.HasUncountedNonCopyableMark(use.TypeParameter!)
                                ? Rules.UnlistedMemberOfUncheckedCode
                                : Rules.UnlistedMember,
                        syntax,
                        Display(use.Definition!),
                        use.TypeArgument,
                        use.TypeParameter!.Name,
                        GetListId(use.Definition!));
                    break;

                case MemberUseKind.UnlistedContainer when types.FindCloningBindingWithin(use.TypeArgument) is { } binding:
                    Report(Rules.CloningUseTypeArgument, syntax, Display(use.Definition!), use.TypeArgument, use.TypeParameter!.Name, binding.Cloning, binding.Type, binding.Bound);
                    break;

                case MemberUseKind.UnlistedContainer when use.ListedFor is not null:
                    Report(Rules.OpaqueTypeArgumentListedForOther, syntax, Display(use.Definition!), use.TypeArgument, use.TypeParameter!.Name, use.ListedFor);
                    break;

                case MemberUseKind.UnlistedContainer:
                    Report(Rules.OpaqueTypeArgument, syntax, Display(use.Definition!), use.TypeArgument, use.TypeParameter!.Name, GetListId(use.Definition!));
                    break;

                case MemberUseKind.RequiresCopyable:
                    Report(Rules.RequiresCopyableMember, syntax, Display(use.Definition!), use.TypeArgument, use.TypeParameter!.Name);
                    break;

                case MemberUseKind.DefaultMemberOfConstraint:
                    Report(
                        Rules.DefaultInterfaceMemberBound,
                        syntax,
                        use.TypeArgument,
                        Display(use.Member!.OriginalDefinition),
                        use.TypeParameter!.Name,
                        Display(use.Definition!),
                        use.Constraint);

                    break;

                case MemberUseKind.ContainerHiddenByConstraint when types.FindCloningBindingWithin(use.TypeArgument) is { } binding:
                    Report(Rules.CloningUseConstraint, syntax, Display(use.Definition!), use.TypeArgument, use.TypeParameter!.Name, use.Constraint, binding.Cloning, binding.Type, binding.Bound);
                    break;

                case MemberUseKind.ContainerHiddenByConstraint:
                    var constraintRule = NonCopyableTypes.IsUntypedCollection(use.Constraint!) ? Rules.UntypedCollectionConstraint : Rules.OpaqueConstraint;
                    Report(constraintRule, syntax, Display(use.Definition!), use.TypeArgument, use.TypeParameter!.Name, use.Constraint);
                    break;
            }

            CheckUncheckedInbound(syntax, member);
            return use;
        }

        /// <summary>
        /// Reports a member of an assembly that was not built with the analyzer and that gets at a non-copyable value other than by value.
        /// Returns whether it was reported.
        /// </summary>
        private bool CheckUncheckedInbound(SyntaxNode syntax, ISymbol member)
        {
            if (types.FindUncheckedInbound(member) is not { } gettingAt)
                return false;

            var definition = NonCopyableTypes.GetListDefinition(member);
            Report(Rules.UncheckedMember, syntax, Display(definition), gettingAt.Name, gettingAt.Type, GetListId(definition));
            return true;
        }

        /// <summary>
        /// Reports a member that is used on a container through a type that no longer mentions the non-copyable type, such as a member that an array
        /// inherits from <see cref="Array"/>, unless the member is on the safe list. The receiver is used as the type that declares the member.
        /// </summary>
        private void CheckReceiverType(SyntaxNode syntax, ITypeSymbol? receiverType, ISymbol? member, bool subscribes = true)
        {
            if (receiverType is null || member is null || member.IsStatic || member.ContainingType is not { } declaringType)
                return;

            // An extension member takes the receiver as an argument of its own type, also in the reduced form that some compiler calls have.
            if (member is IMethodSymbol { IsExtensionMethod: true } || declaringType.IsExtension)
                return;

            // The object itself, with a binding to a class that clones itself that no declaration judged.
            if (receiverType is INamedTypeSymbol { TypeKind: TypeKind.Class } receiverClass && types.FindUnjudgedCloningBinding(receiverClass) is { } binding)
            {
                // An event hands the object to its handlers only where one is subscribed. Its accessors are code of its class like any
                // other member: one whose line says that it clones does so where a handler is added and where one is removed, and so
                // can one that nothing is known of, or that runs a member of a class below.
                CheckMemberOfClonedObject(syntax, receiverClass, member, binding, handsOn: member is not IEventSymbol || subscribes);
                return;
            }

            if (!types.HidesNonCopyableType(receiverType, declaringType))
                return;

            // A container of objects whose binding to a class that clones itself no declaration judged, used as a type that hides them.
            if (types.FindCloningBindingWithin(receiverType) is not null)
            {
                if (!types.IsListed(member))
                    TryReportCloningUse(syntax, receiverType, declaringType, member);

                return;
            }

            if (types.IsListed(member))
                return;

            var definition = NonCopyableTypes.GetListDefinition(member);

            // An object whose binding was judged where its class is declared has the warning for the clone there.
            if (types.IsCloningMember(definition) && types.HasJudgedCloningBinding(receiverType))
                return;

            // A member that clones its object copies what the container holds, whatever a symbol list file could say of it.
            var rule = NonCopyableTypes.IsUntypedCollection(declaringType)
                ? Rules.UntypedCollectionMember
                : types.IsCloningMember(definition)
                    ? Rules.OpaqueMemberThatClones
                    : Rules.OpaqueMember;
            Report(rule, syntax, receiverType, declaringType, Display(definition), GetListId(definition));
        }

        /// <summary>
        /// Reports a member that is used on an object whose binding to a class that clones itself no declaration judged, where the member
        /// can clone the object or hand it to code that can (SCA1009): it does, by a 'clones' line; its members that clone are not known,
        /// since it is inherited from a class that does not show the binding and whose members no line names; or its signature shows that
        /// it can hand the object to other code as a type that hides the binding, and no line of its own says that it does not. A call that
        /// binds to an override is judged by the override, and a line for the member that it overrides does not vouch for it: a member of
        /// the class that shows the binding, or of a class between it and the class that clones itself, is judged by its own line and its
        /// own signature.
        /// </summary>
        private void CheckMemberOfClonedObject(SyntaxNode syntax, INamedTypeSymbol receiver, ISymbol member, CloningBinding binding, bool handsOn = true)
        {
            var implementation = NonCopyableTypes.FindImplementationIn(receiver, member);
            var definition = NonCopyableTypes.GetListDefinition(implementation);

            // The messages name a binding that a declaration writes with a closed type first: the clone copies it whatever the class is
            // constructed with.
            binding = types.FindClosedCloningBinding(receiver) ?? binding;

            if (types.IsCloningMember(definition) || types.IsCloningMember(NonCopyableTypes.GetListDefinition(member)))
            {
                Report(Rules.CloningUseOfCloningMember, syntax, Display(definition), receiver, binding.Type, binding.Bound);
                return;
            }

            if (implementation.ContainingType is not { } declaring)
                return;

            // A line for the member itself says that it neither clones the object nor hands it on. That is a statement of its own code,
            // which can still run a member that a class below put in the place of one of its class.
            if (types.IsListed(implementation))
            {
                CheckMembersBelow(hasLine: true);
                return;
            }

            // Where the members that clone are not known, because no line that says 'clones=complete' covers the member's class, any member
            // can clone. A member of a class that shows the binding is code of the project, or of an assembly that was built with the
            // analyzers, whose members that clone have their lines there (SCA1008). One of an assembly that was not built with them, which
            // writes the binding in its own base list, is judged as a class between: nothing vouches for what its own members do. A generic
            // class of such an assembly whose binding comes from its own type arguments alone gets SCA1020 for each of its members instead;
            // where a base list above it also binds a closed non-copyable type, the clone copies that one, and the member is judged for it.
            bool judgedInItsOwnBuild = !types.HidesNonCopyableType(receiver, declaring) &&
                (SymbolEqualityComparer.Default.Equals(declaring.ContainingAssembly, context.Compilation.Assembly) ||
                    AnalyzedAssemblies.IsAnalyzed(declaring.ContainingAssembly) ||
                    (SymbolEqualityComparer.Default.Equals(types.FindCloningBinding(declaring)?.Bound, declaring) && types.FindClosedCloningBinding(declaring) is null));

            if (!judgedInItsOwnBuild && !types.AreCloningMembersKnown(declaring, binding.Cloning))
            {
                // The advice names the line that would cover the member's class: that of the class that clones itself, or the class's own.
                if (types.IsJudgedByLineOf(declaring, binding.Cloning))
                    Report(Rules.CloningUseOfUnknownMember, syntax, Display(definition), receiver, declaring, binding.Cloning, binding.Type, binding.Bound, GetListId(definition), GetListId(binding.Cloning));
                else
                    Report(Rules.CloningUseOfMemberOfUncoveredClass, syntax, Display(definition), receiver, declaring, binding.Cloning, binding.Type, binding.Bound, GetListId(definition), GetListId(declaring.OriginalDefinition));
            }
            else if (handsOn && NonCopyableTypes.FindHandBackType(receiver, implementation) is { } handBackType)
            {
                Report(Rules.CloningUseHandsBack, syntax, Display(definition), receiver, handBackType, binding.Cloning, binding.Type, binding.Bound, GetListId(definition));
            }
            else
            {
                CheckMembersBelow(hasLine: false);
            }

            // A member that neither clones the object nor hands it on, as far as its own code goes, can run a member of a class below
            // its own on the object: an override, or a member that implements an interface of its class again.
            void CheckMembersBelow(bool hasLine)
            {
                if (types.FindMemberBelowThatCanClone(receiver, declaring, binding.Cloning, hasLine) is not { } below)
                    return;

                var belowDefinition = NonCopyableTypes.GetListDefinition(below.Member);

                if (below.IsOfCloningClass)
                {
                    Report(
                        Rules.CloningUseRunsOverrideOfCloningClass,
                        syntax,
                        Display(definition),
                        receiver,
                        Display(belowDefinition),
                        declaring,
                        binding.Type,
                        binding.Bound,
                        GetListId(definition));
                }
                else if (below.Clones)
                {
                    Report(Rules.CloningUseRunsMemberThatClones, syntax, Display(definition), receiver, Display(belowDefinition), declaring, binding.Type, binding.Bound);
                }
                else
                {
                    var belowClass = below.Member.ContainingType;

                    Report(
                        Rules.CloningUseRunsUnknownMember,
                        syntax,
                        Display(definition),
                        receiver,
                        Display(belowDefinition),
                        belowClass,
                        declaring,
                        binding.Cloning,
                        binding.Type,
                        binding.Bound,
                        GetListId(belowDefinition),
                        GetListId(belowClass.OriginalDefinition));
                }
            }
        }

        /// <summary>
        /// Reports a member of a class that clones itself, of a class that another project can derive from or bind a type to, which clones
        /// its own object, with MemberwiseClone, through a member of the class that clones, or through an interface whose member the class
        /// implements with one, and has no 'clones' line of its own (SCA1008). A project that calls it on an object with a binding cannot
        /// see the clone, and goes by the lines: this helps the author of a library complete them, and is given only where the compilation
        /// makes a library, and only for a member that another assembly can name. It is not a proof: a member that hands its object to a
        /// static helper that clones it, or clones it through a private or internal member of its class, gets no warning.
        /// </summary>
        private void CheckCloneOfOwnObject(IOperation operation, IMethodSymbol called, IOperation? instance, ImmutableArray<IArgumentOperation> arguments)
        {
            if (context.Compilation.Options.OutputKind is not OutputKind.DynamicallyLinkedLibrary)
                return;

            // A call on the object through an interface, such as '((ICloneable)this).Clone()', runs the member of the class that implements it.
            if (called is { IsStatic: false, ContainingType.TypeKind: TypeKind.Interface } && instance is not null && IsOwnObject(instance) &&
                context.OwningSymbol?.ContainingType?.FindImplementationForInterfaceMember(called) is IMethodSymbol implementing)
            {
                called = implementing;
            }

            if (!IsMemberwiseClone(called) && !types.IsCloningMember(NonCopyableTypes.GetListDefinition(called)))
                return;

            if (!(called.IsStatic ? arguments.Any(a => IsOwnObject(a.Value)) : instance is null || IsOwnObject(instance) || arguments.Any(a => IsOwnObject(a.Value))))
                return;

            if (context.OwningSymbol is not { ContainingType: { TypeKind: TypeKind.Class } owningClass } owner)
                return;

            var ownerDefinition = NonCopyableTypes.GetListDefinition(owner);

            // A line can name only a member that another assembly sees: a private or internal one is not imported where the library is
            // referenced, and its line would have no effect there. A member that clones through such a helper is for the author to list.
            if (ownerDefinition.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Protected or Accessibility.ProtectedOrInternal) ||
                types.IsCloningMember(ownerDefinition) ||
                !(types.IsClonesClass(owningClass) || types.FindCloningBase(owningClass) is not null) ||
                !(IsGenericOrInGeneric(owningClass) || types.CanBeDerivedFromElsewhere(owningClass)))
            {
                return;
            }

            // The line that says whether the 'clones' lines of the class are complete is the class's own, or that of the class of the
            // library that clones itself and whose lines cover it. A class whose class that clones itself is of another assembly is
            // covered by that class's line, which the library does not ship.
            var cloningOfLibrary = types.IsClonesClass(owningClass) ? owningClass : types.FindCloningBase(owningClass);
            string completeHint = cloningOfLibrary is not null && SymbolEqualityComparer.Default.Equals(cloningOfLibrary.ContainingAssembly, context.Compilation.Assembly)
                ? $"; once the 'clones' lines name every member of '{Display(cloningOfLibrary.OriginalDefinition)}' that clones its object or hands it on, " +
                    $"write its line as '{GetListId(cloningOfLibrary.OriginalDefinition)};clones=complete'"
                : string.Empty;

            Report(Rules.MemberClonesWithoutLine, operation.Syntax, Display(ownerDefinition), Display(NonCopyableTypes.GetListDefinition(called)), owningClass, GetListId(ownerDefinition), completeHint);

            static bool IsOwnObject(IOperation? value) =>
                value is not null && SkipConversions(value) is IInstanceReferenceOperation { ReferenceKind: InstanceReferenceKind.ContainingTypeInstance };

            static IOperation SkipConversions(IOperation value)
            {
                while (value is IConversionOperation { OperatorMethod: null } or IParenthesizedOperation)
                    value = value is IConversionOperation conversion ? conversion.Operand : ((IParenthesizedOperation)value).Operand;

                return value;
            }

            static bool IsGenericOrInGeneric(INamedTypeSymbol type)
            {
                for (var current = type; current is not null; current = current.ContainingType)
                {
                    if (current.IsGenericType)
                        return true;
                }

                return false;
            }
        }

        /// <summary>
        /// Reports a listed member that hands out a non-copyable value by value when it was called on or with a stored value: the value is then a
        /// copy of one that the stored container still holds.
        /// </summary>
        /// <param name="syntax">Where to report.</param>
        /// <param name="use">The use of the member.</param>
        /// <param name="handedOutType">The type of what the member hands out.</param>
        /// <param name="inputsAreNew">Whether what the member was called on and with is new.</param>
        /// <param name="from">The receiver, for the hint that says why it is not the method's own.</param>
        /// <param name="result">The operation whose value is what the member hands out, where it is the result of the call or the read.
        /// It is a temporary copy like every other: where it is only the receiver of a field read or of a readonly member, it does no
        /// harm, as in 'stack.Peek().IsHeld'.</param>
        private void CheckHandedOutValue(
            SyntaxNode syntax,
            MemberUse use,
            ITypeSymbol? handedOutType,
            bool inputsAreNew,
            IOperation? from = null,
            IOperation? result = null)
        {
            if (use.Kind is not MemberUseKind.Listed || inputsAreNew || !types.IsNonCopyableType(handedOutType))
                return;

            if (IsOnlyReadAsTemporary(result))
                return;

            // A constructor hands nothing out: it makes a value that reads what the stored one still holds.
            if (use.Definition is IMethodSymbol { MethodKind: MethodKind.Constructor })
                Report(Rules.CreatedFromStoredContainer, syntax, handedOutType, GetNotOwnHint(from), GetAdviceForStoredSource(from, isRead: false));
            else
                Report(Rules.ReadFromStoredContainer, syntax, $"'{Display(use.Definition!)}'", handedOutType, GetNotOwnHint(from), GetAdviceForStoredSource(from, isRead: true));
        }

        /// <summary>
        /// Gets the end of the advice for a value that is read or created out of a stored one, by what the stored one is. A
        /// variable, a field or an element can be moved out of. What a property or a call hands out by value cannot: no reference
        /// to it can be taken, so the message does not name NonCopyable.Move there. A listed member reads from what it is called on,
        /// so the stored value is looked for behind it: in 'await _task.ConfigureAwait(false)' it is the field.
        /// </summary>
        private string GetAdviceForStoredSource(IOperation? from, bool isRead)
        {
            ISymbol? handsOut = null;

            while (from is not null)
            {
                switch (from)
                {
                    case IParenthesizedOperation parenthesized:
                        from = parenthesized.Operand;
                        continue;

                    case IConversionOperation { OperatorMethod: null } conversion:
                        from = conversion.Operand;
                        continue;

                    case IPropertyReferenceOperation { Instance: { } instance, Property: var listed } when types.GetUse(listed).Kind is MemberUseKind.Listed:
                        from = instance;
                        continue;

                    case IInvocationOperation { Instance: { } instance, TargetMethod: var listed } when types.GetUse(listed).Kind is MemberUseKind.Listed:
                        from = instance;
                        continue;

                    case IPropertyReferenceOperation { Property: { ReturnsByRef: false, ReturnsByRefReadonly: false } property }:
                        handsOut = property;
                        break;

                    case IInvocationOperation { TargetMethod: { ReturnsByRef: false, ReturnsByRefReadonly: false } method }:
                        handsOut = method;
                        break;
                }

                break;
            }

            if (handsOut is null)
                return isRead ? ", use a reference, or move the stored value out first with NonCopyable.Move" : ", or move the stored value out first with NonCopyable.Move";

            return isRead
                ? ", or use a reference"
                : $". What '{Display(handsOut.OriginalDefinition)}' hands out stays with what holds it and cannot be moved out of: make the value in this " +
                    "method and hand it to the object, as a completion source is made in the method that returns its 'Task'";
        }

        /// <summary>
        /// Gets a value indicating whether a value is new for the members that are used on it: it is a new value, and not an object
        /// that is never new for its members, as an object of a class that derives from a listed container and a value of a marked
        /// type parameter that is constrained to a container are. This is the one question for a member that the compiler calls
        /// with nothing written: for a 'foreach', an 'await' and a deconstruction through <see cref="FollowMember"/>, and for a
        /// list pattern, a property pattern and a spread where they judge what they read. A member that is written out asks the
        /// same of its receiver and its arguments: see <see cref="AreCarriedValuesNew"/>.
        /// </summary>
        private bool IsNewForItsMembers(bool isNew, ITypeSymbol? type) => isNew && !types.IsNeverNewForItsMembers(type);

        /// <summary>
        /// Follows one member that the compiler calls for a language construct, such as GetAwaiter for 'await' or Current for 'foreach', and gets
        /// whether what the member hands out is a new value.
        /// </summary>
        private bool FollowMember(
            SyntaxNode syntax,
            ISymbol member,
            ITypeSymbol? receiverType,
            ITypeSymbol? handedOutType,
            bool receiverIsNew,
            string construct,
            ITypeSymbol? result,
            bool report,
            IOperation? from = null,
            string? advice = null,
            ISymbol? receiverFrom = null)
        {
            var use = report ? CheckMemberUse(syntax, member) : types.GetUse(member);

            // Every member that is followed here is called by a pattern, with no argument for an extension's receiver.
            if (report)
            {
                CheckReceiverType(syntax, receiverType, member);
                CheckPatternExtensionReceiver(syntax, receiverType, member);
            }

            // What a generic hands out of a container that is bound to a marked type parameter is stored, and so is a container that a
            // member of an assembly that was not built with the analyzer hands out, as for the member written out.
            if (types.HandsOutBoundContainer(member, handedOutType) || types.HandsOutUncheckedContainer(member, handedOutType, GetDeclaredResultType(member)))
                return false;

            // The result of checked code is a new value, and so is a non-copyable value from a member marked 'new'. What has just been reported
            // is not reported again for what follows from it. An object of a class that derives from a listed container is never new
            // for the members that it inherits.
            if (!types.FollowsInputs(use, handedOutType) || IsNewForItsMembers(receiverIsNew, receiverType))
                return true;

            if (types.IsNonCopyableType(handedOutType))
            {
                // The finding names what the construct gives in the end, which says more than the type of an awaiter or an enumerator.
                // Where what it was called on says nothing, the member of unchecked code that handed it out does.
                if (report)
                {
                    string hint = GetNotOwnHint(from);

                    if (hint.Length is 0 && receiverFrom is not null)
                        hint = GetUncheckedSourceHint(receiverFrom, receiverType);

                    Report(
                        Rules.ReadFromStoredContainer,
                        syntax,
                        construct,
                        types.IsNonCopyableType(result) ? result : handedOutType,
                        hint,
                        advice ?? GetAdviceForStoredSource(from, isRead: true));
                }

                return true;
            }

            return false;
        }

        // The member, if it is one of an assembly that was not built with the analyzer and hands out a container that is so stored.
        private ISymbol? GetUncheckedSource(ISymbol member, ITypeSymbol? handedOut) =>
            types.HandsOutUncheckedContainer(member, handedOut, GetDeclaredResultType(member)) ? member : null;

        /// <summary>
        /// Reports an 'await' whose awaiter is of a non-copyable type, whatever is awaited: when the 'await' suspends, the compiler
        /// copies the awaiter into the state machine, calls OnCompleted on the original and GetResult on the copy. The awaiters of the
        /// framework, whose members the list has, hold a reference to their task and nothing that a copy could double.
        /// </summary>
        private void CheckAwaiter(IAwaitOperation operation)
        {
            if (operation.Syntax is AwaitExpressionSyntax syntax &&
                operation.SemanticModel?.GetAwaitExpressionInfo(syntax) is { GetAwaiterMethod.ReturnType: var awaiterType, GetResultMethod: { } getResult } &&
                types.IsNonCopyableType(awaiterType) &&
                !types.IsListed(getResult))
            {
                Report(Rules.NonCopyableAwaiter, syntax, awaiterType);
            }
        }

        /// <summary>
        /// Gets the method by which an 'await' hands its continuation to an awaiter: the awaiter's implementation of
        /// 'ICriticalNotifyCompletion.UnsafeOnCompleted', which the compiler prefers, or of 'INotifyCompletion.OnCompleted'.
        /// </summary>
        private IMethodSymbol? FindOnCompleted(ITypeSymbol awaiterType)
        {
            return Find("System.Runtime.CompilerServices.ICriticalNotifyCompletion", "UnsafeOnCompleted") ??
                Find("System.Runtime.CompilerServices.INotifyCompletion", "OnCompleted");

            IMethodSymbol? Find(string interfaceName, string methodName)
            {
                if (context.Compilation.GetTypeByMetadataName(interfaceName) is not { } notify ||
                    !awaiterType.AllInterfaces.Contains(notify, SymbolEqualityComparer.Default) ||
                    notify.GetMembers(methodName).OfType<IMethodSymbol>().FirstOrDefault() is not { } declared)
                {
                    return null;
                }

                return ResolveImplementation(awaiterType, declared);
            }
        }

        /// <summary>
        /// Checks the members that the compiler calls for an 'await' and gets whether its result is a new value.
        /// </summary>
        private bool AnalyzeAwait(IAwaitOperation operation, bool report)
        {
            var awaited = operation.Operation;

            if (report)
                CheckAwaiter(operation);

            var syntax = operation.Syntax as AwaitExpressionSyntax;
            var info = syntax is null ? default : operation.SemanticModel?.GetAwaitExpressionInfo(syntax);

            // The result of an awaitable that is checked code is a new value, unless a member of an assembly that was not built with the
            // analyzer hands out a container on the way, which is stored, as for the members written out.
            if (!types.Carries(awaited.Type) &&
                (info is not { GetAwaiterMethod: { } awaiterOf, GetResultMethod: { } resultOf } ||
                    (GetUncheckedSource(awaiterOf, awaiterOf.ReturnType) is null && GetUncheckedSource(resultOf, resultOf.ReturnType) is null)))
            {
                return true;
            }

            bool isNew = IsNew(awaited);

            if (syntax is null || info is not { GetAwaiterMethod: { } getAwaiter, GetResultMethod: { } getResult } awaitInfo)
            {
                // A dynamic await is not covered: the result is as new as what is awaited.
                return isNew;
            }

            if (report)
                CheckExtensionReceiver(awaited, getAwaiter);

            // Through a read-only reference the compiler calls a GetAwaiter that is not readonly on a copy.
            if (report &&
                getAwaiter is { IsStatic: false, IsExtensionMethod: false } &&
                IsCalledOnCopy(awaited, getAwaiter.ContainingType, getAwaiter.IsReadOnly))
            {
                ReportMemberOnCopy(awaited, getAwaiter);
            }

            var awaiterType = getAwaiter.ReturnType;
            isNew = FollowMember(syntax, getAwaiter, awaited.Type, awaiterType, isNew, "'await'", operation.Type, report, awaited);

            if (report && awaitInfo.IsCompletedProperty is { } isCompleted)
            {
                CheckMemberUse(syntax, isCompleted);
                CheckReceiverType(syntax, awaiterType, isCompleted);
            }

            // Where the awaited value has not completed, the compiler hands the rest of the method to the awaiter, with nothing
            // written for it and nothing that the semantic model names: 'UnsafeOnCompleted' if the awaiter has it, or 'OnCompleted'.
            if (report && FindOnCompleted(awaiterType) is { } onCompleted)
            {
                CheckMemberUse(syntax, onCompleted);
                CheckReceiverType(syntax, awaiterType, onCompleted);
            }

            return FollowMember(
                syntax, getResult, awaiterType, getResult.ReturnType, isNew, "'await'", operation.Type, report, awaited, receiverFrom: GetUncheckedSource(getAwaiter, awaiterType));
        }

        /// <summary>
        /// Checks the members that a spread calls on its operand: it enumerates the operand as a 'foreach' does, so it uses
        /// 'GetEnumerator', 'MoveNext', 'Current' and 'Dispose' with nothing written for them. The copy of a stored element is
        /// reported where the spread is visited.
        /// </summary>
        /// <remarks>
        /// The operation does not name the members. They are the ones that a 'foreach' over the operand would use, so the compiler is
        /// asked for those of such a loop, at the place of the spread.
        /// </remarks>
        /// <returns>The operand, if the spread calls 'GetEnumerator' on it in place or the call on a copy was reported here: nothing
        /// more is to be said of it where it is visited. Null otherwise.</returns>
        private IOperation? CheckMembersOfSpread(ISpreadOperation operation)
        {
            var operand = operation.Operand is IConversionOperation { IsImplicit: true, OperatorMethod: null } conversion ? conversion.Operand : operation.Operand;
            var collectionType = operand.Type;

            // The compiler reads the elements of an array, a span and an inline array in place, and calls nothing.
            if (collectionType is null or IArrayTypeSymbol ||
                NonCopyableTypes.IsSpan(collectionType, out _) ||
                GetInlineArrayElementType(collectionType) is not null ||
                (!types.Carries(collectionType) && !types.Carries(operation.ElementType)))
            {
                return null;
            }

            if (operation.SemanticModel is not { } model ||
                operand.Syntax is not ExpressionSyntax written ||
                GetForEachInfoAt(model, operation.Syntax.SpanStart, written) is not { GetEnumeratorMethod: { } getEnumerator } info)
            {
                return null;
            }

            // What the spread runs is reported at what it enumerates, which an unsafe(...) expression can hold, and the '..' cannot.
            var site = operand.Syntax;
            var enumeratorType = getEnumerator.ReturnType;
            var handledOperand = operand;
            CheckExtensionReceiver(operand, getEnumerator);

            // A receiver of a kind that is not known to be a reference is left to be reported where it is visited.
            if (IsCalledOnCopy(operand, getEnumerator.ReceiverType, getEnumerator.IsReadOnly) &&
                (getEnumerator is not { IsStatic: false, IsExtensionMethod: false } || !ReportMemberOnCopy(operand, getEnumerator)))
            {
                handledOperand = null;
            }

            CheckMemberUse(site, getEnumerator);
            CheckReceiverType(site, collectionType, getEnumerator);
            CheckPatternExtensionReceiver(site, collectionType, getEnumerator);

            if (info.MoveNextMethod is { } moveNext)
            {
                CheckMemberUse(site, moveNext);
                CheckReceiverType(site, enumeratorType, moveNext);
            }

            if (info.CurrentProperty is { } current)
            {
                CheckMemberUse(site, current);
                CheckReceiverType(site, enumeratorType, current);
            }

            if (info.DisposeMethod is { } dispose)
            {
                dispose = ResolveImplementation(enumeratorType, dispose);
                CheckMemberUse(site, dispose);
                CheckReceiverType(site, enumeratorType, dispose);
            }

            return handledOperand;
        }

        /// <summary>
        /// Gets what the compiler would use for a 'foreach' over an expression at a place: the loop is bound there with nothing of
        /// it written. Null where no loop can be bound, as in the initializer of a constructor.
        /// </summary>
        private static ForEachStatementInfo? GetForEachInfoAt(SemanticModel model, int position, ExpressionSyntax collection)
        {
            var loop = SyntaxFactory.ForEachStatement(SyntaxFactory.IdentifierName("var"), SyntaxFactory.Identifier("element"), collection.WithoutTrivia(), SyntaxFactory.Block());

            if (model.TryGetSpeculativeSemanticModel(position, loop, out var inStatement) && inStatement is not null)
                return inStatement.GetForEachStatementInfo(loop);

            // In the initializer of a field or a property there is no statement for the loop to stand in for: it goes into a lambda.
            var initializer = SyntaxFactory.EqualsValueClause(SyntaxFactory.CastExpression(
                SyntaxFactory.ParseTypeName("global::System.Action"),
                SyntaxFactory.ParenthesizedExpression(SyntaxFactory.ParenthesizedLambdaExpression(SyntaxFactory.Block(loop)))));

            return model.TryGetSpeculativeSemanticModel(position, initializer, out var inInitializer) &&
                inInitializer is not null &&
                initializer.DescendantNodes().OfType<ForEachStatementSyntax>().FirstOrDefault() is { } bound
                    ? inInitializer.GetForEachStatementInfo(bound)
                    : null;
        }

        /// <summary>
        /// Gets the end of the advice for a 'foreach' over a stored <c>List&lt;T&gt;</c>, which says what to write instead: a loop over
        /// references to the elements, through the span that the list hands out over them. Null for any other loop, and for one that
        /// has an 'await' or a 'yield' in it, which a reference cannot live across.
        /// </summary>
        private static string? GetAdviceForLoopOverList(IOperation collection, CommonForEachStatementSyntax loop)
        {
            if (loop.AwaitKeyword.RawKind is not (int)SyntaxKind.None ||
                collection.Type is not INamedTypeSymbol { Name: "List", Arity: 1, ContainingType: null } list ||
                !NonCopyableTypes.IsInNamespace(list, "System.Collections.Generic"))
            {
                return null;
            }

            foreach (var node in loop.Statement.DescendantNodesAndSelf(static node => node is not (AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax)))
            {
                if (node is AwaitExpressionSyntax or YieldStatementSyntax)
                    return null;
            }

            string variable = loop is ForEachStatementSyntax { Identifier.ValueText: { Length: > 0 } name } ? name : "item";

            string source = collection.Syntax is IdentifierNameSyntax or MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax or IdentifierNameSyntax, Name: IdentifierNameSyntax }
                ? collection.Syntax.ToString()
                : "list";

            return $", or loop over references to the elements with 'foreach (ref var {variable} in CollectionsMarshal.AsSpan({source}))'";
        }

        /// <summary>
        /// Gets a value indicating whether a type is a task of the framework: <c>Task</c>, <c>Task&lt;T&gt;</c>, <c>ValueTask</c> or
        /// <c>ValueTask&lt;T&gt;</c>, or what <c>ConfigureAwait</c> makes of one, whose awaiting is known.
        /// </summary>
        private static bool IsTaskOfTheFramework(ITypeSymbol type) => type.OriginalDefinition switch {
            INamedTypeSymbol { Name: "Task" or "ValueTask", Arity: 0 or 1, ContainingType: null } task => NonCopyableTypes.IsInNamespace(task, "System.Threading.Tasks"),
            INamedTypeSymbol { Name: "ConfiguredTaskAwaitable" or "ConfiguredValueTaskAwaitable", Arity: 0 or 1, ContainingType: null } configured =>
                NonCopyableTypes.IsInNamespace(configured, "System.Runtime.CompilerServices"),
            _ => false,
        };

        /// <summary>
        /// Checks the members that the compiler calls for a 'foreach' and the copy that a by-value iteration variable makes of a stored element.
        /// </summary>
        private void AnalyzeForEach(IForEachLoopOperation operation, HandledScope handled)
        {
            var collection = operation.Collection is IConversionOperation { IsImplicit: true, OperatorMethod: null } conversion
                ? conversion.Operand
                : operation.Collection;

            // The operations of the walker have syntax and a model. The analyzer also runs in the editor on code that is being typed.
            if (operation.Syntax is not CommonForEachStatementSyntax syntax || operation.SemanticModel is not { } model)
                return;

            var info = model.GetForEachStatementInfo(syntax);
            var collectionType = collection.Type;
            var elementType = info.ElementType;
            bool elementIsNew;

            // The user-defined conversions of each element to the type of the variable, with nothing written for them.
            CheckMemberUse(collection.Syntax, info.CurrentConversion.MethodSymbol);
            CheckMemberUse(collection.Syntax, info.ElementConversion.MethodSymbol);

            // An 'await foreach' awaits what MoveNextAsync and DisposeAsync return, with members that the compiler binds as for a
            // written 'await'. The compiler that the analyzer is built against does not give them, so an awaitable of the project's
            // own over a non-copyable type or a container is a use that is not known to be safe, whatever the loop is over.
            if (syntax.AwaitKeyword.RawKind is not (int)SyntaxKind.None)
            {
                foreach (var awaited in new[] { info.MoveNextMethod?.ReturnType, info.DisposeMethod?.ReturnType })
                {
                    if (types.Carries(awaited) && !IsTaskOfTheFramework(awaited))
                    {
                        Report(Rules.UnsupportedUse, collection.Syntax, awaited, operation.Kind);
                        break;
                    }
                }
            }

            // A variable of a non-copyable type over boxed elements, as those of an ArrayList, takes each one out of its box.
            if (operation.LoopControlVariable is IVariableDeclaratorOperation { Symbol: var unboxed } &&
                IsBoxType(elementType) &&
                types.IsNonCopyableType(unboxed.Type))
            {
                Report(Rules.CopyByAssignmentOutOfBox, collection.Syntax, unboxed.Type, elementType);
                return;
            }

            if (collectionType is IArrayTypeSymbol array)
            {
                // The compiler reads the elements of an array in place.
                elementType = array.ElementType;
                elementIsNew = false;
            }
            else if (NonCopyableTypes.IsSpan(collectionType, out var spanElementType))
            {
                // And those of a span: it turns the loop into one over the indexes and calls nothing of the enumerator, whatever the semantic
                // model says of GetEnumerator, MoveNext and Dispose.
                elementType = spanElementType;
                elementIsNew = false;
            }
            else if (GetInlineArrayElementType(collectionType) is { } inlineArrayElementType)
            {
                // And those of an inline array, which it enumerates as a span over the buffer.
                elementType = inlineArrayElementType;
                elementIsNew = false;
            }
            else if (info.GetEnumeratorMethod is not { } getEnumerator || info.CurrentProperty is not { } current)
            {
                return;
            }
            else if (!types.Carries(collectionType) && !types.Carries(elementType))
            {
                return;
            }
            else
            {
                const string Construct = "'foreach'";
                var site = collection.Syntax;
                var enumeratorType = getEnumerator.ReturnType;
                string? advice = GetAdviceForLoopOverList(collection, syntax);
                CheckExtensionReceiver(collection, getEnumerator);

                // A view never owns its elements, so enumerating one by value copies them, whatever the view came from.
                bool collectionIsNew = IsNew(collection) && !IsViewBehindConversions(collection);
                bool enumeratorIsNew = FollowMember(
                    site, getEnumerator, collectionType, enumeratorType, collectionIsNew, Construct, elementType, report: true, collection, advice);

                if (info.MoveNextMethod is { } moveNext)
                {
                    CheckMemberUse(site, moveNext);
                    CheckReceiverType(site, enumeratorType, moveNext);
                }

                // A reference to an element is a stored value. Read by value it is copied, and no member needs to be listed for that to be known.
                elementIsNew = ReturnsByValue(current) &&
                    FollowMember(
                        site, current, enumeratorType, current.Type, enumeratorIsNew, Construct, elementType, report: true, collection, advice, GetUncheckedSource(getEnumerator, enumeratorType));

                if (!ReturnsByValue(current))
                {
                    CheckMemberUse(site, current);
                    CheckReceiverType(site, enumeratorType, current);
                }

                if (info.DisposeMethod is { } dispose)
                {
                    dispose = ResolveImplementation(enumeratorType, dispose);
                    CheckMemberUse(site, dispose);
                    CheckReceiverType(site, enumeratorType, dispose);
                }
            }

            var variable = operation.LoopControlVariable;

            if (variable is IVariableDeclaratorOperation declarator)
            {
                var variableType = declarator.Symbol.Type;
                bool isBoxed = types.IsNonCopyableType(elementType) && variableType is { IsValueType: false } && !types.IsNonCopyableType(variableType);

                // The loop converts each element to the type of its variable, with no conversion in the operation tree: a container to a
                // type that hides its elements, or a value to a reference type, which boxes it. What the loop does with each element is
                // reported at the collection, which an unsafe(...) expression can hold, and the declaration of the variable cannot.
                var conversionSite = collection.Syntax;

                if (types.HidesNonCopyableType(elementType, variableType))
                {
                    ReportContainerConversion(conversionSite, elementType, variableType);
                }
                else if (isBoxed && !elementIsNew)
                {
                    Report(Rules.Boxing, conversionSite, elementType, variableType);
                }
                else if (isBoxed && types.HidesTypeParameter(elementType, variableType))
                {
                    Report(Rules.OpaqueTypeParameterConversion, conversionSite, elementType, variableType);
                }
                else if (isBoxed && types.IsStructOfUncheckedCode(elementType) && types.LosesNonCopyableType(elementType, variableType))
                {
                    Report(Rules.OpaqueBoxing, conversionSite, elementType, variableType, Display(GetStructDefinition(elementType)));
                }
                else if (!elementIsNew && declarator.Symbol.RefKind is RefKind.None && types.IsNonCopyableType(elementType))
                {
                    Report(Rules.CopyByAssignmentIntoForEachVariable, collection.Syntax, elementType, declarator.Symbol.Name);
                }
            }
            else if (syntax is ForEachVariableStatementSyntax variableSyntax)
            {
                // foreach (var (a, b) in ...)
                var deconstruction = model.GetDeconstructionInfo(variableSyntax);

                // The compiler copies each element that it reads in place, as from an array or a span, before it calls Deconstruct on
                // it. A method that is not readonly then runs on the copy, and one that takes the element by value is given it.
                if (!elementIsNew && types.IsNonCopyableType(elementType) && deconstruction.Method is { } deconstruct)
                {
                    bool takesByValue = deconstruct.IsExtensionMethod && NonCopyableTypes.GetUnreduced(deconstruct).Parameters is [{ RefKind: RefKind.None }, ..];

                    if (takesByValue || deconstruct is { IsStatic: false, IsExtensionMethod: false, IsReadOnly: false })
                        Report(Rules.CopyByAssignmentForDeconstruct, collection.Syntax, elementType, Display(deconstruct));
                }

                MarkDeconstructionTargets(variable, handled);
                CheckDeconstruction(variable, null, deconstruction, elementIsNew, handled, elementType, collection.Syntax);
            }
        }

        /// <summary>
        /// Reports a stored value of a non-copyable type that the compiler passes to an extension method which takes it by value, as the
        /// GetEnumerator of a 'foreach' or the GetAwaiter of an 'await': there is no argument in the tree for the copy.
        /// </summary>
        private void CheckExtensionReceiver(IOperation receiver, IMethodSymbol method)
        {
            if (TakesReceiverByValue(method) && types.IsNonCopyableType(receiver.Type) && !IsNew(receiver) && !types.IsBorrowing(method))
                Report(Rules.CopyOfStoredValue, receiver.Syntax, $"'{Display(method)}' receives", receiver.Type, GetHandlerHint(receiver));
        }

        /// <summary>
        /// Gets a value indicating whether a method is an extension that takes what it is called on by value: an extension method
        /// whose first parameter is by value, or a member of an extension block whose receiver is.
        /// </summary>
        private static bool TakesReceiverByValue(IMethodSymbol method) =>
            method.IsExtensionMethod
                ? NonCopyableTypes.GetUnreduced(method).Parameters is [{ RefKind: RefKind.None }, ..]
                : NonCopyableTypes.GetExtensionReceiver(method) is { RefKind: RefKind.None };

        /// <summary>
        /// Gets the type of the elements of an inline array: a struct marked [InlineArray], whose one instance field is the first element.
        /// </summary>
        private static ITypeSymbol? GetInlineArrayElementType(ITypeSymbol? type)
        {
            if (type is not INamedTypeSymbol { TypeKind: TypeKind.Struct } buffer)
                return null;

            bool isInlineArray = buffer.OriginalDefinition.GetAttributes().Any(a => a.AttributeClass is {
                Name: "InlineArrayAttribute",
                ContainingNamespace: { Name: "CompilerServices", ContainingNamespace: { Name: "Runtime", ContainingNamespace.Name: "System" } },
            });

            return isInlineArray ? buffer.GetMembers().OfType<IFieldSymbol>().FirstOrDefault(f => !f.IsStatic)?.Type : null;
        }

        /// <summary>
        /// Checks the Dispose member that a 'using' calls on a resource whose type carries a non-copyable type. The report is at the
        /// value of the resource, as the report of a copy is, so that an 'unsafe(...)' expression around the value covers it: for a
        /// declared variable that is its initializer, since an 'unsafe' block around the declaration would end the scope of the variable.
        /// </summary>
        private void CheckDisposal(IOperation resources, bool isAsynchronous)
        {
            if (resources is IVariableDeclarationGroupOperation group)
            {
                foreach (var declaration in group.Declarations)
                {
                    foreach (var declarator in declaration.Declarators)
                        CheckDisposal(declarator.Initializer?.Value.Syntax ?? declarator.Syntax, declarator.Symbol.Type, isAsynchronous);
                }
            }
            else
            {
                CheckDisposal(resources.Syntax, resources.Type, isAsynchronous);
            }
        }

        private void CheckDisposal(SyntaxNode syntax, ITypeSymbol? type, bool isAsynchronous)
        {
            if (type is null || !types.Carries(type))
                return;

            string methodName = isAsynchronous ? "DisposeAsync" : "Dispose";
            var disposable = isAsynchronous
                ? context.Compilation.GetTypeByMetadataName("System.IAsyncDisposable")
                : context.Compilation.GetSpecialType(SpecialType.System_IDisposable);

            IMethodSymbol? dispose = null;

            if (disposable is not null &&
                (SymbolEqualityComparer.Default.Equals(type, disposable) || type.AllInterfaces.Contains(disposable, SymbolEqualityComparer.Default)))
            {
                var interfaceMethod = disposable.GetMembers(methodName).OfType<IMethodSymbol>().FirstOrDefault();
                dispose = interfaceMethod is null ? null : ResolveImplementation(type, interfaceMethod);
            }

            // A ref struct, or any type for 'await using', can have the method without the interface.
            dispose ??= type.GetMembers(methodName).OfType<IMethodSymbol>().FirstOrDefault(m => !m.IsStatic && m.Parameters.All(p => p.IsOptional));

            CheckMemberUse(syntax, dispose);
            CheckReceiverType(syntax, type, dispose);
        }

        /// <summary>
        /// Gets the method that a call of an interface method runs on a value whose type is a class or a struct, as far as that type says: the
        /// member that is used is its implementation, as the member of a virtual call is the one that the type of the receiver declares. A call
        /// on a value of an interface type uses the interface method.
        /// </summary>
        private static IMethodSymbol ResolveImplementation(ITypeSymbol receiverType, IMethodSymbol method)
        {
            if (receiverType.TypeKind is TypeKind.Class or TypeKind.Struct && method.ContainingType?.TypeKind is TypeKind.Interface)
                return receiverType.FindImplementationForInterfaceMember(method) as IMethodSymbol ?? method;

            return method;
        }

        /// <summary>
        /// Reports a call of MemberwiseClone on an object with an instance field of a non-copyable type, its own or inherited, or on a
        /// non-copyable struct: the clone is a shallow copy, field by field. A field that holds a reference to a container does not count,
        /// since the clone shares the container.
        /// </summary>
        /// <remarks>
        /// Where the class has no such field, the object that is cloned can be of a derived class that has one: one of the compilation is
        /// reported as the definite copy that it is, and a class that another assembly can derive from as a possible one (SCA1008). A
        /// sealed class and a struct have no derived class.
        /// </remarks>
        private void CheckMemberwiseClone(IOperation operation, IOperation? instance)
        {
            // 'base.MemberwiseClone()' has the base type as the type of its instance, and clones the object all the same.
            var type = instance is not null && SkipParentheses(instance) is not IInstanceReferenceOperation
                ? SkipParentheses(instance).Type
                : context.OwningSymbol.ContainingType;

            if (types.IsNonCopyableType(type))
            {
                Report(Rules.MemberwiseCloneCallOnValue, operation.Syntax, type);
                return;
            }

            // C# allows the call on an object of a type parameter only where it is constrained to the class or to one derived from it,
            // and the object is of that class or of one derived from it.
            if (type is ITypeParameterSymbol typeParameter)
                type = GetConstraintClass(typeParameter);

            for (var current = type; current is not null; current = current.BaseType)
            {
                foreach (var member in current.GetMembers())
                {
                    if (member is IFieldSymbol { IsStatic: false, RefKind: RefKind.None } field && types.IsNonCopyableType(field.Type))
                    {
                        Report(Rules.MemberwiseCloneCall, operation.Syntax, type, field.Type, DisplayField(field));
                        return;
                    }
                }
            }

            if (type is not INamedTypeSymbol { TypeKind: TypeKind.Class, IsSealed: false } named)
                return;

            if (types.FindDerivedClassWithNonCopyableField(named) is { } derived)
            {
                var field = derived.Field;
                Report(Rules.MemberwiseCloneOfDerivedClass, operation.Syntax, named, derived.Class, field.Type, DisplayField(field));
            }
            else if (types.CanBeDerivedFromElsewhere(named))
            {
                Report(Rules.MemberwiseCloneOfOpenClass, operation.Syntax, named, named.OriginalDefinition.GetDocumentationCommentId() ?? Display(named));
            }
        }

        // The class that a type parameter is constrained to, through the type parameters that it is constrained to. Null if it has none.
        private static INamedTypeSymbol? GetConstraintClass(ITypeParameterSymbol typeParameter)
        {
            foreach (var constraint in typeParameter.ConstraintTypes)
            {
                if (constraint is INamedTypeSymbol { TypeKind: TypeKind.Class } constraintClass)
                    return constraintClass;

                if (constraint is ITypeParameterSymbol other && GetConstraintClass(other) is { } throughOther)
                    return throughOther;
            }

            return null;
        }

        // The clone of object, and the one of MarshalByRefObject that Stream and Component inherit, which clones the object as well.
        private static bool IsMemberwiseClone(IMethodSymbol method) => method is { Name: nameof(MemberwiseClone) } &&
            ((method.Parameters.IsEmpty && method.ContainingType.SpecialType is SpecialType.System_Object) ||
             (method.Parameters is [{ Type.SpecialType: SpecialType.System_Boolean }] &&
                method.ContainingType is { Name: nameof(MarshalByRefObject) } owner && NonCopyableTypes.IsInNamespace(owner, "System")));

        private static bool IsGetObjectValue(IMethodSymbol method) =>
            method is { Name: nameof(RuntimeHelpers.GetObjectValue), ContainingType: { Name: nameof(RuntimeHelpers) } owner } &&
            NonCopyableTypes.IsInNamespace(owner, "System.Runtime.CompilerServices");

        /// <summary>
        /// Gets the Add methods of a collection type that take an element of a collection expression: what the compiler binds for each element
        /// of a type that it creates with a constructor. The operation does not name them.
        /// </summary>
        private IEnumerable<IMethodSymbol> GetAddMethods(ICollectionExpressionOperation operation, ITypeSymbol collectionType)
        {
            if (operation.SemanticModel is not { } model)
                return [];

            var elementTypes = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);

            foreach (var element in operation.Elements)
            {
                if ((element is ISpreadOperation spread ? spread.ElementType : element.Type) is { } elementType)
                    elementTypes.Add(elementType);
            }

            var compilation = context.Compilation;

            return model.LookupSymbols(operation.Syntax.SpanStart, collectionType, "Add", includeReducedExtensionMethods: true)
                .OfType<IMethodSymbol>()
                .Where(add => add.Parameters.Length is 1 && elementTypes.Any(type => compilation.ClassifyCommonConversion(type, add.Parameters[0].Type).IsImplicit));
        }
    }
}
