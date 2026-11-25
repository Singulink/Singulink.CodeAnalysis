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
/// The walker's checks of containers and conversions: a container that is converted to a type which hides its non-copyable
/// elements, a method group that becomes a delegate, and a delegate that would hand out a stored container.
/// </content>
public sealed partial class DoNotCopyValueAnalyzer
{
    private sealed partial class NonCopyableWalker
    {
        /// <summary>
        /// Gets a value indicating whether a conversion to object is that of a value in a string concatenation whose ToString cannot
        /// change it. The compiler boxes nothing there: it calls ToString on a temporary copy of the value, which does no harm when the
        /// struct declares the method readonly or leaves it to System.ValueType.
        /// </summary>
        private static bool IsFormattedByReadOnlyToString(IConversionOperation conversion)
        {
            if (conversion.Operand.Type is not INamedTypeSymbol { TypeKind: TypeKind.Struct } type ||
                conversion.Type?.SpecialType is not SpecialType.System_Object ||
                type.OriginalDefinition.SpecialType is SpecialType.System_Nullable_T)
            {
                return false;
            }

            IOperation current = SkipParenthesesUpward(conversion);

            bool isConcatenated = current.Parent is
                IBinaryOperation { OperatorKind: BinaryOperatorKind.Add, OperatorMethod: null, Type.SpecialType: SpecialType.System_String } or
                ICompoundAssignmentOperation { OperatorKind: BinaryOperatorKind.Add, OperatorMethod: null, Type.SpecialType: SpecialType.System_String };

            if (!isConcatenated)
                return false;

            var toString = type.GetMembers(nameof(ToString)).OfType<IMethodSymbol>().FirstOrDefault(m => m.IsOverride && m.Parameters.IsEmpty);
            return toString is null || toString.IsReadOnly;
        }

        /// <summary>
        /// Reports a delegate whose calls hand out a container that something else holds. The result of calling a delegate is a new
        /// value, on the strength of the check of the method or lambda that it is created from. That check says nothing for a method
        /// group of a member that hands out what its container holds, and nothing for a function of generic code that returns a value
        /// of a marked type parameter: with a container bound, its body moves a reference that the caller of the generic still holds.
        /// </summary>
        private void CheckContainerHandedOutByDelegate(IDelegateCreationOperation operation, IMethodSymbol function, IMethodSymbol invoke)
        {
            var returnType = function.ReturnType;

            if (NonCopyableTypes.MentionsMarkedTypeParameter(returnType, out var typeParameter))
            {
                Report(Rules.DelegateOverTypeParameter, operation.Target.Syntax, Display(function), returnType, typeParameter.Name);
            }
            else if (operation.Target is IMethodReferenceOperation &&
                types.IsContainer(returnType) &&
                (types.HandsOutBoundContainer(function, returnType) || types.IsOpenWithListedMembers(returnType) || types.FollowsInputs(types.GetUse(function), returnType)))
            {
                Report(Rules.DelegateHandsOutStoredContainer, operation.Target.Syntax, Display(function), returnType);
            }
            else if (operation.Target is IMethodReferenceOperation && types.LosesHolder(returnType, invoke.ReturnType))
            {
                // The method returns an object of a class that derives from a listed container, and the delegate hands it out as that
                // container: the conversion that would make it stored is not written anywhere.
                Report(Rules.DelegateHandsOutStoredContainer, operation.Target.Syntax, Display(function), returnType);
            }
        }

        /// <summary>
        /// Reports a conversion between two constructions of a variant interface or delegate whose type argument goes from a class
        /// that derives from a listed container, or from a type that is open to stored containers, to the container: what comes out
        /// through the converted reference is the object, with no conversion that would make it stored.
        /// </summary>
        private void CheckHolderLostByVariance(IConversionOperation operation)
        {
            if (types.FindHolderLostByVariance(operation.Operand.Type, operation.Type) is (var holder, var container))
                Report(Rules.HolderLostByVariance, operation.Operand.Syntax, operation.Operand.Type, operation.Type, holder, container);
        }

        /// <summary>
        /// Reports a method group that is converted to a delegate whose signature hides a non-copyable type that the method's shows: the
        /// delegate hands out what the method returns as its own return type, and hands the method what it is called with. Each is the
        /// conversion that it stands for, with no expression for the visit of a conversion to see.
        /// </summary>
        private void CheckMethodGroupConversion(
            SyntaxNode syntax, IMethodSymbol method, ImmutableArray<IParameterSymbol> parameters, object converted, ITypeSymbol? delegateType)
        {
            if (delegateType is not INamedTypeSymbol { DelegateInvokeMethod: { } invoke })
                return;

            Check(method.ReturnType, invoke.ReturnType);

            var invokeParameters = invoke.Parameters;

            for (int i = 0; i < parameters.Length && parameters.Length == invokeParameters.Length; i++)
                Check(invokeParameters[i].Type, parameters[i].Type);

            void Check(ITypeSymbol source, ITypeSymbol target)
            {
                // A type parameter that is constrained to a class converts as a reference does, and can have a container bound to it.
                if (!types.HidesNonCopyableType(source, target) && !types.HidesTypeParameter(source, target))
                    return;

                // Through a variant type parameter it is what is bound to it that is hidden.
                if (types.GetHiddenByVariance(source, target) is (var container, var hidingType))
                    (source, target) = (container, hidingType);

                if (TryReportCloningUse(syntax, source, target))
                    return;

                var rule = NonCopyableTypes.IsUntypedCollection(target) ? Rules.UntypedCollectionMethodGroup : Rules.OpaqueMethodGroup;
                Report(rule, syntax, source, target, converted, delegateType);
            }
        }

        /// <summary>
        /// Reports a container that is converted to a type which no longer mentions the non-copyable type, because the code that receives it cannot
        /// be checked. A member that the list marks 'borrows' may receive it.
        /// </summary>
        private void CheckContainerConversion(IConversionOperation operation)
        {
            var sourceType = operation.Operand.Type;
            var targetType = operation.Type;

            if (!types.HidesNonCopyableType(sourceType, targetType))
                return;

            // The compiler reads an array in a 'foreach' in place, whatever the conversion in the operation tree says. For any other collection
            // the members it calls are checked.
            if (operation.Parent is IForEachLoopOperation forEach && ReferenceEquals(operation, forEach.Collection))
                return;

            // A comparison of references hands the container to no code.
            if (operation.Parent is IBinaryOperation { OperatorMethod: null, OperatorKind: BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals })
                return;

            // An element of tuples that are compared with '==' or '!=' as references, which hands it to no code either.
            if (targetType.SpecialType is SpecialType.System_Object && IsElementOfTupleComparison(operation))
                return;

            ReportHidingConversion(operation.Operand.Syntax, sourceType, targetType, GetReceivingMember(operation), IsUnwantedResultOfLambda(operation));
        }

        /// <summary>
        /// Reports a value that is converted to a type which hides its non-copyable type, wherever the conversion stands, a node of the
        /// tree or one that the compiler applies with no node. A member that receives the value lets it through only where the list
        /// marks it 'borrows': a line with no marker says how the member treats the values that it holds, and not that it forgets a
        /// reference that it is given, as Add, SetResult or TryWrite do not.
        /// </summary>
        private void ReportHidingConversion(SyntaxNode syntax, ITypeSymbol sourceType, ITypeSymbol targetType, ISymbol? receivingMember, bool isResultOfLambda = false)
        {
            if (receivingMember is not null && types.IsBorrowing(receivingMember))
                return;

            // An argument of a member that clones what it is given, such as CultureInfo.ReadOnly. An object whose binding to a class that
            // clones itself was judged where it is declared has its warning there.
            if (receivingMember is not null && types.IsCloningMember(NonCopyableTypes.GetListDefinition(receivingMember)))
            {
                if (TryReportCloningUse(syntax, sourceType, targetType, receivingMember) || types.HasJudgedCloningBinding(sourceType))
                    return;
            }

            ReportContainerConversion(syntax, sourceType, targetType, isResultOfLambda);
        }

        /// <summary>
        /// Reports what a box of a value of a non-copyable type hides from the analyzer, whether the value is new or stored: a value of a
        /// marked type parameter, which a container can be bound to, used as a type that hides it; a generic struct of unchecked code,
        /// whose box hands out boxed copies of what it holds; a struct whose members run on the box with a type that a type parameter
        /// of it does not take; or any other generic struct whose type arguments carry containers, which other code can reach through
        /// the box. A stored value gets the report of its copy as well, where it is boxed: the box breaks both rules, and the
        /// report of the copy says nothing of what the box hides, which can be a container of a type that is unsafe to copy.
        /// </summary>
        private void CheckWhatTheBoxHides(SyntaxNode syntax, ITypeSymbol valueType, ITypeSymbol boxType, bool isResultOfLambda = false)
        {
            if (types.HidesTypeParameter(valueType, boxType))
                Report(Rules.OpaqueTypeParameterConversion, syntax, valueType, boxType);
            else if (types.IsStructOfUncheckedCode(valueType) && types.LosesNonCopyableType(valueType, boxType))
                Report(isResultOfLambda ? Rules.OpaqueLambdaBoxing : Rules.OpaqueBoxing, syntax, valueType, boxType, Display(GetStructDefinition(valueType)));
            else if (types.FindStructBindingNotTaken(valueType) is not null)
                CheckBoxOfBindingNotTaken(syntax, valueType, boxType);
            else if (types.HidesContainersOfTypeArguments(valueType, boxType))
                Report(Rules.OpaqueBoxingOfContainers, syntax, valueType, boxType);
        }

        /// <summary>
        /// Checks a conversion that the compiler applies with no conversion node in the tree: the value of a '??', the elements of a
        /// spread and of a deconstruction, and the receiver of an extension that a pattern calls. It is judged as a node would be: a
        /// conversion that hides the non-copyable type, and a stored value of a non-copyable type that is boxed.
        /// </summary>
        private void CheckConversionWithoutNode(SyntaxNode syntax, ITypeSymbol? sourceType, ITypeSymbol? targetType, bool sourceIsNew, ISymbol? receivingMember = null)
        {
            if (sourceType is null || targetType is null || SymbolEqualityComparer.Default.Equals(sourceType, targetType))
                return;

            if (types.HidesNonCopyableType(sourceType, targetType))
            {
                ReportHidingConversion(syntax, sourceType, targetType, receivingMember);
            }
            else if (targetType is { IsValueType: false } && types.IsNonCopyableType(sourceType) && !(receivingMember is not null && types.IsBorrowing(receivingMember)))
            {
                if (!sourceIsNew)
                    Report(Rules.Boxing, syntax, sourceType, targetType);

                CheckWhatTheBoxHides(syntax, sourceType, targetType);
            }
        }

        /// <summary>
        /// Checks the receiver of an extension that the compiler calls by a pattern, with no argument in the tree for it: GetAwaiter,
        /// GetEnumerator, GetAsyncEnumerator and Deconstruct. The value is converted to the type of the extension's receiver as an
        /// argument would be. A written call of an extension has the argument, which is checked where it is visited.
        /// </summary>
        private void CheckPatternExtensionReceiver(SyntaxNode syntax, ITypeSymbol? receiverType, ISymbol? member)
        {
            var extensionReceiverType = member switch {
                IMethodSymbol { IsExtensionMethod: true } method => method.ReducedFrom is not null ? method.ReceiverType : method.Parameters.FirstOrDefault()?.Type,
                _ => NonCopyableTypes.GetExtensionReceiver(member)?.Type,
            };

            if (extensionReceiverType is not null && types.HidesNonCopyableType(receiverType, extensionReceiverType))
                ReportHidingConversion(syntax, receiverType, extensionReceiverType, member);
        }

        /// <summary>
        /// Gets a value indicating whether a converted value is an element of a tuple that is compared with '==' or '!='.
        /// </summary>
        private static bool IsElementOfTupleComparison(IOperation operation)
        {
            for (var current = operation.Parent; current is not null; current = current.Parent)
            {
                switch (current)
                {
                    case ITupleBinaryOperation:
                        return true;
                    case ITupleOperation or IParenthesizedOperation or IConversionOperation { OperatorMethod: null }:
                        continue;
                    default:
                        return false;
                }
            }

            return false;
        }

        /// <summary>
        /// Gets the type of the elements of a collection that a collection expression makes: the element type of an array, the type
        /// argument of a span, or that of the IEnumerable&lt;T&gt; that the type is or implements.
        /// </summary>
        private static ITypeSymbol? GetCollectionElementType(ITypeSymbol? type)
        {
            if (type is IArrayTypeSymbol array)
                return array.ElementType;

            if (type is not INamedTypeSymbol named)
                return null;

            if (named.OriginalDefinition.SpecialType is SpecialType.System_Collections_Generic_IEnumerable_T)
                return named.TypeArguments[0];

            if (named is { IsRefLikeType: true, TypeArguments: [var spanElement] } && named.Name is "Span" or "ReadOnlySpan")
                return spanElement;

            return named.AllInterfaces.FirstOrDefault(i => i.OriginalDefinition.SpecialType is SpecialType.System_Collections_Generic_IEnumerable_T)?.TypeArguments[0];
        }

        /// <summary>
        /// Reports a use of a value as a type that hides it where the value is, or holds, an object with a binding of a non-copyable type
        /// to a class that clones itself which no declaration of the compilation judged (SCA1009), instead of the warning of a hidden
        /// container. Every way that such an object reaches the code that clones it is such a use. A member that clones is named as such.
        /// </summary>
        private bool TryReportCloningUse(SyntaxNode syntax, ITypeSymbol sourceType, ITypeSymbol targetType, ISymbol? member = null)
        {
            if (types.FindCloningBindingWithin(sourceType) is not { } binding)
                return false;

            // The report stands for the warning of the hidden container as well, so it is about what other code can reach of the value
            // besides the binding that the clone copies: strict where either is unsafe to copy.
            var subject = Subject.OfCopyAndReach(binding.Type, sourceType);

            if (member is not null && NonCopyableTypes.GetListDefinition(member) is var definition && types.IsCloningMember(definition))
                ReportAbout(Rules.CloningUseOfCloningMember, subject, syntax, Display(definition), sourceType, binding.Type, binding.Bound);
            else if (member is not null)
                ReportAbout(Rules.CloningUseMember, subject, syntax, Display(NonCopyableTypes.GetListDefinition(member)), sourceType, targetType, binding.Cloning, binding.Type, binding.Bound, GetListId(NonCopyableTypes.GetListDefinition(member)));
            else
                ReportAbout(Rules.CloningUseConversion, subject, syntax, sourceType, targetType, binding.Cloning, binding.Type, binding.Bound);

            return true;
        }

        /// <summary>
        /// Gets a value indicating whether a conversion is that of the result of a call to what a lambda with an expression body
        /// returns, as in 'Func&lt;object&gt; run = () => Start();'. Such a lambda is often meant to return nothing.
        /// </summary>
        private static bool IsUnwantedResultOfLambda(IConversionOperation conversion) =>
            conversion is { IsImplicit: true, Operand: IInvocationOperation or IAwaitOperation, Parent: IReturnOperation { IsImplicit: true } } &&
            conversion.Syntax.Parent is AnonymousFunctionExpressionSyntax { ExpressionBody: not null };

        private void ReportContainerConversion(SyntaxNode syntax, ITypeSymbol sourceType, ITypeSymbol targetType, bool isResultOfLambda = false)
        {
            if (TryReportCloningUse(syntax, sourceType, targetType))
                return;

            // Through a variant type parameter it is what is bound to it that is hidden, from the code behind the converted reference.
            if (types.GetHiddenByVariance(sourceType, targetType) is (var container, var hidingType))
            {
                var varianceRule = NonCopyableTypes.IsUntypedCollection(hidingType) ? Rules.UntypedCollectionVariance : Rules.OpaqueVariance;
                Report(varianceRule, syntax, container, hidingType, sourceType, targetType);
                return;
            }

            var rule = NonCopyableTypes.IsUntypedCollection(targetType)
                ? Rules.UntypedCollectionConversion
                : isResultOfLambda ? Rules.OpaqueLambdaResult : Rules.OpaqueConversion;

            // The report is about what the conversion hides, which is what the container exposes: the key collection of a dictionary
            // hides its keys, and nothing of its values, whatever the key is.
            var exposed = types.GetExposedNonCopyableTypes(sourceType);
            var subject = exposed.IsDefaultOrEmpty ? Subject.Of(sourceType) : Subject.Of([.. exposed]);
            ReportAbout(rule, subject, syntax, DescribeContainer(sourceType), targetType);
        }

        /// <summary>
        /// Gets the member that a converted value is the receiver or an argument of, also as an element of the array of a 'params' parameter.
        /// </summary>
        private static ISymbol? GetReceivingMember(IOperation operation)
        {
            var current = operation;

            while (true)
            {
                switch (current.Parent)
                {
                    case IParenthesizedOperation or IConversionOperation { OperatorMethod: null }:
                    case { IsImplicit: true, Kind: OperationKind.ArrayInitializer or OperationKind.ArrayCreation or OperationKind.CollectionExpression }:
                        current = current.Parent;
                        continue;

                    case IArgumentOperation argument:
                        return argument.Parent switch {
                            IInvocationOperation invocation => invocation.TargetMethod,
                            IObjectCreationOperation creation => creation.Constructor,
                            IPropertyReferenceOperation propertyReference => propertyReference.Property,
                            _ => null,
                        };

                    case IInvocationOperation invocation when ReferenceEquals(current, invocation.Instance):
                        return invocation.TargetMethod;

                    case IMemberReferenceOperation memberReference when ReferenceEquals(current, memberReference.Instance):
                        return memberReference.Member is IFieldSymbol ? null : memberReference.Member;

                    default:
                        return null;
                }
            }
        }
    }
}
