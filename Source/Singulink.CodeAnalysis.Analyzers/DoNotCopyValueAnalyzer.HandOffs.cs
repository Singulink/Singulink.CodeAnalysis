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
        try
        {
            walker.Walk();
        }
        catch (InsufficientExecutionStackException)
        {
            foreach (var diagnostic in walker.Diagnostics)
                context.ReportDiagnostic(diagnostic);

            context.ReportDiagnostic(severities.Create(Rules.NestedTooDeeply, GetMemberLocation(context), Display(context.OwningSymbol)));
            return;
        }

        foreach (var diagnostic in MoveReportsOfHandOffs(context, severities, types, allowsImplicitMoves, walker))
            context.ReportDiagnostic(diagnostic);
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
