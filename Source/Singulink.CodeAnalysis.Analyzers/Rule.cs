using Microsoft.CodeAnalysis;

namespace Singulink.CodeAnalysis.Analyzers;

/// <summary>
/// One message of a diagnostic ID, with a descriptor for each severity that it can be reported with.
/// </summary>
/// <remarks>
/// A rule is not a descriptor, so nothing can report it with <see cref="Diagnostic.Create(DiagnosticDescriptor, Location, object[])"/>
/// directly: every report goes through <c>DoNotCopyValueAnalyzer.Severities</c>, which chooses the descriptor.
/// </remarks>
internal sealed class Rule
{
    public Rule(
        RuleGroup group,
        DiagnosticDescriptor main,
        DiagnosticDescriptor? error = null,
        DiagnosticDescriptor? unsafeWarning = null,
        DiagnosticDescriptor? message = null)
    {
        Group = group;
        Main = main;
        Error = error;
        UnsafeWarning = unsafeWarning;
        Message = message;
    }

    /// <summary>
    /// Gets the group of the rule's ID, which decides the severities that it can be reported with.
    /// </summary>
    public RuleGroup Group { get; }

    /// <summary>
    /// Gets the ID of the rule.
    /// </summary>
    public string Id => Main.Id;

    /// <summary>
    /// Gets the ordinary warning of the rule, which can be suppressed and configured in every way. A rule of
    /// <see cref="RuleGroup.Enforced"/> is reported with it in a compilation that uses the current memory safety rules, and in one
    /// whose project sets NonCopyableErrorsAsWarnings, which reports as under the current rules.
    /// </summary>
    public DiagnosticDescriptor Main { get; }

    /// <summary>
    /// Gets the error of a rule of <see cref="RuleGroup.Enforced"/>, which cannot be suppressed: the descriptor of a report outside an
    /// unsafe context in a compilation that uses the updated memory safety rules. Null for a rule of the other groups.
    /// </summary>
    public DiagnosticDescriptor? Error { get; }

    /// <summary>
    /// Gets the warning of a rule of <see cref="RuleGroup.Enforced"/> that '#pragma' and NoWarn suppress and a [SuppressMessage] does
    /// not: the descriptor of a report in an unsafe context in a compilation that uses the updated memory safety rules. Null for a rule
    /// of the other groups.
    /// </summary>
    public DiagnosticDescriptor? UnsafeWarning { get; }

    /// <summary>
    /// Gets the message of a rule of <see cref="RuleGroup.UncheckedAssembly"/>, for a project that gets the analyzer only through another
    /// package. Null for a rule of the other groups.
    /// </summary>
    public DiagnosticDescriptor? Message { get; }

    /// <summary>
    /// Gets every descriptor of the rule.
    /// </summary>
    public IEnumerable<DiagnosticDescriptor> Descriptors
    {
        get
        {
            yield return Main;

            if (Error is not null)
                yield return Error;

            if (UnsafeWarning is not null)
                yield return UnsafeWarning;

            if (Message is not null)
                yield return Message;
        }
    }
}

/// <summary>
/// The groups of diagnostic IDs, by the severities that their rules are reported with.
/// </summary>
internal enum RuleGroup
{
    /// <summary>
    /// A copy, a hand-off, a wrong declaration, or code that the analyzer could not check. In a compilation that uses the current memory
    /// safety rules it is an ordinary warning, which can be suppressed and configured in every way. In one that uses the updated rules it
    /// is an error that cannot be suppressed, and where it is reported in code that is in an unsafe context, a warning that '#pragma'
    /// and NoWarn suppress and a [SuppressMessage] does not. At a declaration it is always the error there: the place of the report
    /// decides, not the ID. The MSBuild property NonCopyableErrorsAsWarnings makes it the ordinary warning under both rule sets.
    /// </summary>
    Enforced,

    /// <summary>
    /// A plain warning that can be suppressed and configured in every way: a line of a symbol list file, [NotNullRef] on a method of
    /// the project, which its author suppresses after checking the method, and an attribute named NonCopyable that is not the
    /// analyzer's, which a project can keep on purpose for another tool.
    /// </summary>
    Plain,

    /// <summary>
    /// An assembly that was not built with the analyzer. A configurable warning in a project that references a package of the analyzer
    /// itself, and a message in one that only gets the analyzer through another package.
    /// </summary>
    UncheckedAssembly,
}
