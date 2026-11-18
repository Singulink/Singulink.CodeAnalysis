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
/// The walker's rule for the task of a completion source: a property that a list marks 'owned' hands out a new task where the object
/// is one that the method created and that nothing else can have taken the task from.
/// </content>
public sealed partial class DoNotCopyValueAnalyzer
{
    private sealed partial class NonCopyableWalker
    {
        // Whether a read of a property that the list marks 'owned' is the one read on an object that the member has just made.
        private Dictionary<SyntaxNode, bool>? _ownedReads;

        /// <summary>
        /// Gets a value indicating whether a read of a property is the one read of a property that the list marks 'owned' on an object
        /// which the member has just made, as in 'var source = new TaskCompletionSource&lt;Guard&gt;(); ...; return source.Task;'. The
        /// task is then a new value: everything else reaches it through the same property, where it is stored, and a task gives its
        /// result by value only, which is reported there.
        /// </summary>
        /// <remarks>
        /// All of this has to hold, and none of it looks at the paths through the member:
        /// <list type="bullet">
        /// <item>The receiver is a local of the member itself, by value, of exactly the type that declares the property, declared by an
        /// ordinary declaration.</item>
        /// <item>The member assigns the local once, its declaration included, and what it assigns is a creation of exactly that type
        /// with no initializer whose arguments carry no non-copyable type. Nothing else can assign it: it is not passed by reference,
        /// not referred to, and not the target of a deconstruction, a compound assignment or an 'out' declaration.</item>
        /// <item>The property is read on the local once in the whole member.</item>
        /// <item>That read is not in a loop, a lambda or a local function, and the member has no 'yield' and no 'goto', which is a
        /// loop that no loop statement shows.</item>
        /// </list>
        /// A lambda or a local function may use the local, as in 'token.Register(() => source.TrySetCanceled())': it is one more
        /// holder of the object, like a queue that the object is put into. It may not assign the local or read the property, which
        /// would be an assignment or a read that can run any number of times.
        /// </remarks>
        private bool IsOwnedTaskRead(IPropertyReferenceOperation reference)
        {
            if (reference.Instance is not ILocalReferenceOperation { Local: var local } || !types.IsOwnedTaskProperty(reference.Property))
                return false;

            if (!(_ownedReads ??= []).TryGetValue(reference.Syntax, out bool isOwnedRead))
                _ownedReads[reference.Syntax] = isOwnedRead = IsOnlyReadOfOwnedTask(reference, local);

            return isOwnedRead;
        }

        private bool IsOnlyReadOfOwnedTask(IPropertyReferenceOperation reference, ILocalSymbol local)
        {
            var property = reference.Property;
            var type = property.ContainingType;

            if (local.RefKind is not RefKind.None ||
                !SymbolEqualityComparer.Default.Equals(local.Type, type) ||
                local.DeclaringSyntaxReferences is not [var declaration] ||
                declaration.GetSyntax(context.CancellationToken) is not VariableDeclaratorSyntax { Parent.Parent: LocalDeclarationStatementSyntax { UsingKeyword.RawKind: (int)SyntaxKind.None } })
            {
                return false;
            }

            // A read in a lambda or a local function rules the local out where the uses are looked at below.
            for (var current = reference.Parent; current is not null; current = current.Parent)
            {
                if (current is ILoopOperation)
                    return false;
            }

            int assignments = 0;
            int reads = 0;

            foreach (var operation in GetAllOperations())
            {
                switch (operation)
                {
                    // An iterator runs its body once for every enumeration, and a 'goto' can run the read again.
                    case IReturnOperation { Kind: OperationKind.YieldReturn or OperationKind.YieldBreak }:
                    case IBranchOperation { BranchKind: BranchKind.GoTo }:
                        return false;

                    case IVariableDeclaratorOperation { Symbol: var declared } declarator when SymbolEqualityComparer.Default.Equals(declared, local):
                        if (declarator.GetVariableInitializer() is { Value: var initial })
                        {
                            if (!IsCreationOf(initial))
                                return false;

                            assignments++;
                        }

                        break;

                    case ILocalReferenceOperation { Local: var used } use when SymbolEqualityComparer.Default.Equals(used, local):
                        switch (GetUseOfNewObject(use))
                        {
                            case UseOfTaskSource.Assigned:
                                assignments++;
                                break;

                            case UseOfTaskSource.Read:
                                reads++;
                                break;

                            case UseOfTaskSource.Unknown:
                                return false;
                        }

                        break;
                }
            }

            return assignments is 1 && reads is 1;

            // 'new(...)' with no type is a creation of the type that it is assigned to, under a conversion that changes nothing.
            bool IsCreationOf(IOperation value)
            {
                value = SkipParentheses(value);

                if (value is IConversionOperation { OperatorMethod: null, Operand: IObjectCreationOperation created } conversion &&
                    SymbolEqualityComparer.Default.Equals(conversion.Type, created.Type))
                {
                    value = created;
                }

                return value is IObjectCreationOperation { Initializer: null } creation &&
                    SymbolEqualityComparer.Default.Equals(creation.Type, type) &&
                    creation.Arguments.All(argument => !CarriesNonCopyableType(argument.Value));
            }

            // What a use of the local does, among the uses that cannot assign it and cannot read the property unseen. Whatever is not
            // one of them rules the local out.
            UseOfTaskSource GetUseOfNewObject(ILocalReferenceOperation use)
            {
                // A lambda or a local function can run any number of times, and at any time. It may hold the object and use it, but
                // an assignment or a read of the property in it is not the one assignment or the one read.
                bool isInFunction = false;

                for (var current = use.Parent; current is not null && !isInFunction; current = current.Parent)
                    isInFunction = current is IAnonymousFunctionOperation or ILocalFunctionOperation;

                switch (use.Parent)
                {
                    case ISimpleAssignmentOperation { IsRef: false } assignment when ReferenceEquals(assignment.Target, use):
                        return !isInFunction && IsCreationOf(assignment.Value) ? UseOfTaskSource.Assigned : UseOfTaskSource.Unknown;

                    case IPropertyReferenceOperation read when ReferenceEquals(read.Instance, use):
                        if (!SymbolEqualityComparer.Default.Equals(read.Property.OriginalDefinition, property.OriginalDefinition))
                            return UseOfTaskSource.Other;

                        return isInFunction ? UseOfTaskSource.Unknown : UseOfTaskSource.Read;

                    case IInvocationOperation invocation when ReferenceEquals(invocation.Instance, use):
                    case IMethodReferenceOperation methodReference when ReferenceEquals(methodReference.Instance, use):
                        return UseOfTaskSource.Other;
                }

                // The object itself goes on by value: whoever gets it reads the property where it is stored.
                IOperation value = use;

                while (value.Parent is IConversionOperation { OperatorMethod: null } conversion)
                    value = conversion;

                return value.Parent switch {
                    ISimpleAssignmentOperation { IsRef: false } assignment when ReferenceEquals(assignment.Value, value) => UseOfTaskSource.Other,
                    IVariableInitializerOperation { Parent: IVariableDeclaratorOperation { Symbol.RefKind: RefKind.None } } => UseOfTaskSource.Other,
                    IArgumentOperation { Parameter.RefKind: RefKind.None } => UseOfTaskSource.Other,
                    IReturnOperation returned when GetReturnRefKind(returned) is RefKind.None => UseOfTaskSource.Other,
                    IArrayInitializerOperation or ICollectionExpressionOperation => UseOfTaskSource.Other,
                    IBinaryOperation { OperatorMethod: null, OperatorKind: BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals } => UseOfTaskSource.Other,
                    IIsPatternOperation { Pattern: IConstantPatternOperation or INegatedPatternOperation { Pattern: IConstantPatternOperation } } => UseOfTaskSource.Other,
                    _ => UseOfTaskSource.Unknown,
                };
            }
        }
    }

    /// <summary>
    /// What a use of a local does that holds an object which the member has just made.
    /// </summary>
    private enum UseOfTaskSource
    {
        /// <summary>A use that may assign the local, or that is not known.</summary>
        Unknown,

        /// <summary>An assignment of another object that the member has just made.</summary>
        Assigned,

        /// <summary>A read of the property that is asked about.</summary>
        Read,

        /// <summary>A use that leaves the local alone and does not read the property.</summary>
        Other,
    }
}
