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

                var rule = NonCopyableTypes.IsUntypedCollection(target) ? Rules.UntypedCollectionMethodGroup : Rules.OpaqueMethodGroup;
                Report(rule, syntax, source, target, converted, delegateType);
            }
        }

        /// <summary>
        /// Reports a container that is converted to a type which no longer mentions the non-copyable type, because the code that receives it cannot
        /// be checked. A member on the safe list may receive it.
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

            if (GetReceivingMember(operation) is { } member && types.IsListed(member))
                return;

            ReportContainerConversion(operation.Operand.Syntax, sourceType, targetType, IsUnwantedResultOfLambda(operation));
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

            Report(rule, syntax, DescribeContainer(sourceType), targetType);
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
