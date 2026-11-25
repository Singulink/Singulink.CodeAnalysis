using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;

namespace Singulink.CodeAnalysis.Analyzers;

/// <content>
/// The check of a member, and where a copy that a hand-off made is reported: a read by value of a local or a by-value parameter that
/// would move the value but for a later use of the variable is reported at the first later uses, and not at the read.
/// </content>
public sealed partial class DoNotCopyValueAnalyzer
{
    /// <summary>
    /// Checks the operations of a member and reports what they do wrong.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A read by value of a local or a by-value parameter moves its value at the last use of the variable, and is a copy where the
    /// variable is used after it. Such a copy is reported at the first use after the read on each path (SCA1016), where the code goes
    /// wrong, with the read as an additional location. Which reports a hand-off made is found by a second walk of the member that takes
    /// the hand-offs for last uses: a report of the first walk that contains a hand-off and that the second walk does not make was made
    /// by the hand-off. That keeps the judgement of the copy where it is, and changes only the place and the ID of the report. The second
    /// walk runs only for a member that has such a report.</para>
    /// <para>
    /// The report stays at the read where the read is its own later use, as in a loop, and where a later use has no place of its own in
    /// the source. A use is reported once, however many hand-offs reach it, and not at all where it has a report of its own.</para>
    /// </remarks>
    private static void AnalyzeOperationBlock(
        OperationBlockAnalysisContext context, Severities severities, NonCopyableTypes types, MoveTrust moves, bool allowsImplicitMoves)
    {
        var walker = new NonCopyableWalker(context, severities, types, moves, allowsImplicitMoves);

        // Every recursion of the analyzer stops with this exception before the stack runs out. The rest of the member is then not
        // checked, which is reported at the member, and the next member is checked as usual. No other exception is caught.
        bool isTooDeep = false;

        try
        {
            walker.Walk();
        }
        catch (InsufficientExecutionStackException)
        {
            isTooDeep = true;
        }

        // A catch block runs on top of the frames that ran out of stack, so the member is reported once the handler has left them.
        if (isTooDeep)
        {
            foreach (var diagnostic in walker.Diagnostics)
                context.ReportDiagnostic(diagnostic);

            // The report is strict only where the member's code involves a value that is unsafe to copy, or where that cannot be told.
            var subject = FindValueUnsafeToCopy(context, types) switch {
                { Found: false } => Subject.NoUnsafeToCopy,
                { Type: var type } => Subject.Of(type),
            };

            context.ReportDiagnostic(severities.CreateAbout(Rules.NestedTooDeeply, subject, GetMemberLocation(context), Display(context.OwningSymbol)));
            return;
        }

        foreach (var diagnostic in MoveReportsOfHandOffs(context, severities, types, allowsImplicitMoves, walker))
            context.ReportDiagnostic(diagnostic);
    }

    /// <summary>
    /// Finds a value of a type that is unsafe to copy in the code of a member that is nested too deeply to be checked, for the severity of
    /// its report: the type of an operation, of a symbol that an operation names (a member, its parameters and type arguments, a local, a
    /// parameter of a lambda or of a local function, an operator) and of a parameter or the result of the member. An instance member of a
    /// struct holds a value of the struct as well. The walk keeps a stack of its own, so it does not run out of stack however deep the
    /// code is.
    /// </summary>
    /// <remarks>
    /// What a rule with no type would report is found as well, with no type, since the value that it is about can be of any type: a call
    /// of MemberwiseClone, which copies the fields of a class below, and of RuntimeHelpers.GetObjectValue, a call of a member that a
    /// 'clones' line names, a value of a type parameter converted to a type that hides what is bound to it, a delegate made from a function
    /// over a marked type parameter or with a [MovedFrom] parameter, and the code of a generic move.
    /// </remarks>
    /// <returns>
    /// Whether such a value was found, with its type, or found as true with no type where the walk could not give an answer.
    /// </returns>
    private static (bool Found, ITypeSymbol? Type) FindValueUnsafeToCopy(OperationBlockAnalysisContext context, NonCopyableTypes types)
    {
        try
        {
            // The class of the member is no value that its code holds: what it reads of its own object is an operation of the walk.
            if (FindInSignature(context.OwningSymbol) is { } ofMember)
                return (true, ofMember);

            if (context.OwningSymbol is { IsStatic: false, ContainingType: { TypeKind: TypeKind.Struct } owner } && types.IsAboutUnsafeToCopy(owner))
                return (true, owner);

            if (context.OwningSymbol is IMethodSymbol owningMethod && NonCopyableTypes.IsGenericPureMove(owningMethod.OriginalDefinition))
                return (true, null);

            var pending = new Stack<IOperation>(context.OperationBlocks);

            while (pending.Count > 0)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                var operation = pending.Pop();

                if (types.IsAboutUnsafeToCopy(operation.Type))
                    return (true, operation.Type);

                if (GetSymbolOf(operation) is { } symbol && FindInSymbol(symbol) is { } ofSymbol)
                    return (true, ofSymbol);

                if (operation is IPatternOperation pattern && types.IsAboutUnsafeToCopy(pattern.NarrowedType))
                    return (true, pattern.NarrowedType);

                if (IsAboutAnyType(operation))
                    return (true, null);

                foreach (var child in operation.ChildOperations)
                    pending.Push(child);
            }

            return (false, null);
        }

        // Whatever stops the walk leaves the report strict. Only a cancellation of the build goes on.
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return (true, null);
        }

        // A symbol that the code uses: its signature, and the type that declares it, since a generic type over a type that is unsafe to
        // copy holds one.
        ITypeSymbol? FindInSymbol(ISymbol symbol) =>
            FindInSignature(symbol) ?? (types.IsAboutUnsafeToCopy(symbol.ContainingType) ? symbol.ContainingType : null);

        ITypeSymbol? FindInSignature(ISymbol symbol)
        {
            switch (symbol)
            {
                case IMethodSymbol method:
                    if (types.IsAboutUnsafeToCopy(method.ReturnType))
                        return method.ReturnType;

                    foreach (var typeArgument in method.TypeArguments)
                    {
                        if (types.IsAboutUnsafeToCopy(typeArgument))
                            return typeArgument;
                    }

                    return FindInParameters(method.Parameters);

                case IPropertySymbol property:
                    return types.IsAboutUnsafeToCopy(property.Type) ? property.Type : FindInParameters(property.Parameters);

                case IFieldSymbol field:
                    return types.IsAboutUnsafeToCopy(field.Type) ? field.Type : null;

                case IEventSymbol @event:
                    return types.IsAboutUnsafeToCopy(@event.Type) ? @event.Type : null;

                case ILocalSymbol local:
                    return types.IsAboutUnsafeToCopy(local.Type) ? local.Type : null;

                default:
                    return null;
            }
        }

        ITypeSymbol? FindInParameters(ImmutableArray<IParameterSymbol> parameters)
        {
            foreach (var parameter in parameters)
            {
                if (types.IsAboutUnsafeToCopy(parameter.Type))
                    return parameter.Type;
            }

            return null;
        }

        // An operation that a rule with no type would report, which the walk counts as a value of any type.
        bool IsAboutAnyType(IOperation operation) => operation switch {
            IInvocationOperation { TargetMethod: var method } => NonCopyableWalker.IsMemberwiseClone(method) || NonCopyableWalker.IsGetObjectValue(method) || types.IsCloningMember(method),
            IMethodReferenceOperation { Method: var method } => NonCopyableWalker.IsMemberwiseClone(method) || NonCopyableWalker.IsGetObjectValue(method) || types.IsCloningMember(method),
            IPropertyReferenceOperation { Property: var property } => types.IsCloningMember(property),
            IEventReferenceOperation { Event: var @event } => types.IsCloningMember(@event),
            IConversionOperation conversion => types.HidesTypeParameter(conversion.Operand.Type, conversion.Type),
            IPatternOperation pattern => types.HidesTypeParameter(pattern.InputType, pattern.NarrowedType),
            IDelegateCreationOperation { Target: var target } => GetFunctionOf(target) is { } function &&
                (NonCopyableTypes.MentionsMarkedTypeParameter(function.ReturnType, out _) || function.Parameters.Any(NonCopyableTypes.IsMovedFrom)),
            _ => false,
        };

        static IMethodSymbol? GetFunctionOf(IOperation target) => target switch {
            IMethodReferenceOperation reference => reference.Method,
            IAnonymousFunctionOperation function => function.Symbol,
            _ => null,
        };

        static ISymbol? GetSymbolOf(IOperation operation) => operation switch {
            IInvocationOperation invocation => invocation.TargetMethod,
            IObjectCreationOperation creation => creation.Constructor,
            IMemberReferenceOperation reference => reference.Member,
            IVariableDeclaratorOperation declarator => declarator.Symbol,
            IDeclarationPatternOperation { DeclaredSymbol: { } declared } => declared,
            IAnonymousFunctionOperation function => function.Symbol,
            ILocalFunctionOperation function => function.Symbol,
            IUnaryOperation { OperatorMethod: { } method } => method,
            IBinaryOperation { OperatorMethod: { } method } => method,
            ICompoundAssignmentOperation { OperatorMethod: { } method } => method,
            IIncrementOrDecrementOperation { OperatorMethod: { } method } => method,
            IConversionOperation { OperatorMethod: { } method } => method,
            _ => null,
        };
    }

    /// <summary>
    /// Gets the reports of a walk, with those that a hand-off made moved to the first later uses of its variable.
    /// </summary>
    private static IReadOnlyList<Diagnostic> MoveReportsOfHandOffs(
        OperationBlockAnalysisContext context, Severities severities, NonCopyableTypes types, bool allowsImplicitMoves, NonCopyableWalker walker)
    {
        var diagnostics = walker.Diagnostics;

        if (diagnostics.Count is 0 || walker.ReadsBeforeLaterUses.Count is 0)
            return diagnostics;

        // Only a hand-off at the place of a report can have made it.
        var handOffs = new List<HandOff>();

        foreach (var (reference, variable) in walker.ReadsBeforeLaterUses)
        {
            if (diagnostics.Any(diagnostic => IsAt(diagnostic, reference)) && walker.Flow.FindFirstLaterUses(reference, variable) is { } later)
                handOffs.Add(new(reference, variable, later.Uses, later.IncludesRead));
        }

        if (handOffs.Count is 0)
            return diagnostics;

        // The second walk notes nothing for the end of the compilation: the first has noted it all.
        var asLastUses = new HashSet<(TextSpan Span, int Kind)>(handOffs.Select(handOff => MemberFlow.GetKey(handOff.Reference)));
        var second = new NonCopyableWalker(context, severities, types, new MoveTrust(), allowsImplicitMoves, asLastUses);

        try
        {
            second.Walk();
        }
        catch (InsufficientExecutionStackException)
        {
            return diagnostics;
        }

        var stillMade = new HashSet<(string Id, SyntaxTree? Tree, TextSpan Span)>(second.Diagnostics.Select(GetPlace));
        var result = new List<Diagnostic>();
        var moved = new List<HandOff>();

        foreach (var diagnostic in diagnostics)
        {
            var within = handOffs.Where(handOff => IsAt(diagnostic, handOff.Reference)).ToList();

            if (within.Count is 0 || stillMade.Contains(GetPlace(diagnostic)))
            {
                result.Add(diagnostic);
                continue;
            }

            // A read that is its own later use, in a loop, keeps the report: the copy is made by the read itself the next time round.
            if (within.Any(handOff => handOff.IncludesRead))
                result.Add(diagnostic);

            foreach (var handOff in within)
            {
                if (!moved.Contains(handOff))
                    moved.Add(handOff);
            }
        }

        // Each later use once, with every hand-off that reaches it, in the order of the source.
        var byUse = new Dictionary<(TextSpan Span, int Kind), (IOperation Use, List<HandOff> HandOffs)>();

        foreach (var handOff in moved)
        {
            foreach (var use in handOff.LaterUses)
            {
                var key = MemberFlow.GetKey(use);

                if (!byUse.TryGetValue(key, out var reaching))
                    byUse[key] = reaching = (use, []);

                reaching.HandOffs.Add(handOff);
            }
        }

        foreach (var (use, reaching) in byUse.Values.OrderBy(entry => entry.Use.Syntax.SpanStart))
        {
            // A use that has a report of its own is not reported twice.
            if (result.Any(diagnostic => IsAt(diagnostic, use) && diagnostic.Location.SourceSpan == use.Syntax.Span))
                continue;

            reaching.Sort(static (x, y) => x.Reference.Syntax.SpanStart.CompareTo(y.Reference.Syntax.SpanStart));
            var first = reaching[0];
            var variable = first.Variable;
            var type = variable is ILocalSymbol local ? local.Type : ((IParameterSymbol)variable).Type;
            var additionalLocations = reaching.Select(handOff => handOff.Reference.Syntax.GetLocation()).ToList();
            object?[] arguments = [variable.Name, type, walker.DescribeHandOff(first.Reference)];
            WithoutNullableAnnotations(arguments);

            var flow = walker.Flow;
            var location = use.Syntax.GetLocation();

            // Where only a handler uses the variable after the hand-off, the message says so, since the code does not show why the
            // handler runs after it. Where the hand-off stores the value at the end of a 'try' block and only a reference that can be
            // a null reference keeps it from being a commit, the message says how to check the reference.
            if (!flow.IsUsedAgainOnlyByHandlers(first.Reference, variable) || !flow.IsUsedInHandler(first.Reference, variable))
                result.Add(severities.Create(Rules.UsedAfterHandOff, location, additionalLocations, arguments));
            else if (flow.FindReferenceThatCanFailAfterStore(first.Reference, variable) is { } failing)
                result.Add(severities.Create(Rules.UsedAfterHandOffBeforeNullReference, location, additionalLocations, [.. arguments, failing]));
            else
                result.Add(severities.Create(Rules.UsedAfterHandOffInHandler, location, additionalLocations, arguments));
        }

        return result;

        static (string Id, SyntaxTree? Tree, TextSpan Span) GetPlace(Diagnostic diagnostic) =>
            (diagnostic.Id, diagnostic.Location.SourceTree, diagnostic.Location.SourceSpan);

        static bool IsAt(Diagnostic diagnostic, IOperation reference) =>
            diagnostic.Location is { IsInSource: true } location &&
            location.SourceTree == reference.Syntax.SyntaxTree &&
            location.SourceSpan.Contains(reference.Syntax.Span);
    }

    /// <summary>
    /// A read by value of a variable that is no last use only because the variable is used after it, with the first uses after it.
    /// </summary>
    private sealed record HandOff(IOperation Reference, ISymbol Variable, IReadOnlyList<IOperation> LaterUses, bool IncludesRead);

    private sealed partial class NonCopyableWalker
    {
        /// <summary>
        /// Gets the words that say what a hand-off did with a variable, for the message at a later use, which they follow "is used after":
        /// "'C.Take(Guard)' received its value", "its value was assigned to 'copy'".
        /// </summary>
        public string DescribeHandOff(IOperation reference)
        {
            var current = reference;

            // A branch of a conditional, an arm of a 'switch' expression and an operand of '??' hand the value on to what uses the
            // result, as a conversion that wraps it does.
            while (true)
            {
                if (current.Parent is IParenthesizedOperation or IConversionOperation { OperatorMethod: null, Type.IsValueType: true } or ICoalesceOperation)
                    current = current.Parent;
                else if (current.Parent is IConditionalOperation conditional && !ReferenceEquals(conditional.Condition, current))
                    current = conditional;
                else if (current.Parent is ISwitchExpressionArmOperation { Parent: ISwitchExpressionOperation switchExpression } arm && ReferenceEquals(arm.Value, current))
                    current = switchExpression;
                else
                    break;
            }

            return current.Parent switch {
                IVariableInitializerOperation { Parent: IVariableDeclaratorOperation declarator } => $"its value was assigned to '{declarator.Symbol.Name}'",
                ISimpleAssignmentOperation assignment when ReferenceEquals(assignment.Value, current) => "its value was assigned",
                ICoalesceAssignmentOperation assignment when ReferenceEquals(assignment.Value, current) => "its value was assigned",
                IAwaitOperation => "its value was awaited",
                IUnaryOperation { OperatorMethod: { } unaryOperator } => $"'{Display(unaryOperator)}' received its value",
                IBinaryOperation { OperatorMethod: { } binaryOperator } => $"'{Display(binaryOperator)}' received its value",
                IDeconstructionAssignmentOperation deconstruction when ReferenceEquals(deconstruction.Value, current) => "its value was deconstructed",
                IReturnOperation => "its value was returned",
                IConversionOperation { Type: { IsValueType: false } boxType } => $"its value was converted to '{Display(boxType)}'",
                IUsingOperation => "'using' disposed its value",
                IWithOperation => "'with' copied its value",
                IFieldReferenceOperation field when ReferenceEquals(field.Instance, current) => $"its field '{field.Field.Name}' was read",
                IPropertyReferenceOperation { Property.ContainingType.IsExtension: false } property when ReferenceEquals(property.Instance, current) => $"'{Display(property.Property)}' read its value",
                IInvocationOperation { TargetMethod.ContainingType.IsExtension: false } invocation when ReferenceEquals(invocation.Instance, current) => $"'{Display(invocation.TargetMethod)}' ran on its value",
                _ => DescribeCopy(current) is { } what ? $"{FirstToLower(ToPastTense(what))} its value" : "its value was read",
            };

            static string FirstToLower(string text) =>
                text.Length > 0 && char.IsUpper(text[0]) ? char.ToLowerInvariant(text[0]) + text[1..] : text;

            // The words of DescribeCopy end in the verb, in the present tense: 'C.Take(Guard)' receives, The tuple takes.
            static string ToPastTense(string what) => what switch {
                _ when what.EndsWith(" receives", StringComparison.Ordinal) => what[..^"receives".Length] + "received",
                _ when what.EndsWith(" takes", StringComparison.Ordinal) => what[..^"takes".Length] + "took",
                _ when what.EndsWith(" captures", StringComparison.Ordinal) => what[..^"captures".Length] + "captured",
                _ => what,
            };
        }
    }
}
