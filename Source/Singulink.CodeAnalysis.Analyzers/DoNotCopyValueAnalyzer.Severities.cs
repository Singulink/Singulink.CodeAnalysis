using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Singulink.CodeAnalysis.Analyzers;

/// <content>
/// The choice of the descriptor that a rule is reported with, which decides its severity.
/// </content>
public sealed partial class DoNotCopyValueAnalyzer
{
    // The MSBuild property that makes the analyzer report as under the current memory safety rules, as the compiler passes it on.
    private const string ErrorsAsWarningsOption = "build_property.NonCopyableErrorsAsWarnings";

    // Whether the project references a package of the analyzer itself, which the targets file of the package works out and the props file
    // makes visible to the compiler.
    private const string ReferencesPackageOption = "build_property._NonCopyableReferencesPackage";

    /// <summary>
    /// Creates the diagnostics of one compilation: the one place where the descriptor of a rule is chosen. Every report of the analyzer
    /// goes through it, which a <see cref="Rule"/> enforces by not being a descriptor.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A rule of <see cref="RuleGroup.Enforced"/> is an ordinary warning in a compilation that uses the current memory safety rules,
    /// which can be suppressed and configured in every way, wherever it is reported. In a compilation that uses the updated rules it is
    /// an error that cannot be suppressed, and a warning where it is reported in code that is in an unsafe context. The place decides
    /// there, not the ID: a report at a declaration is always the error. The MSBuild property NonCopyableErrorsAsWarnings makes the
    /// analyzer report as under the current rules: the ordinary warning everywhere, with the message of the current rules. It is a
    /// preference for how reports appear and are suppressed; the project still gets every report, so its build keeps the marker of an
    /// analyzed build.</para>
    /// <para>
    /// SCA1025 is a warning in a project that references a package of the analyzer itself, and a message in one that only gets the
    /// analyzer through another package, which did not choose to check the assemblies that it references. A build in which the targets
    /// file did not run, such as a test, counts as one that references a package.</para>
    /// </remarks>
    private sealed class Severities(Compilation compilation, AnalyzerOptions options)
    {
        private readonly bool _errorsAsWarnings = IsSetTo(options, ErrorsAsWarningsOption, "true");
        private readonly bool _referencesPackage = !IsSetToFalse(options, ReferencesPackageOption);
        private readonly bool _updatedMemorySafetyRules = UnsafeContext.UsesUpdatedRules(compilation);

        public Diagnostic Create(Rule rule, Location? location, params object?[] arguments) =>
            Diagnostic.Create(Choose(rule, location), location, arguments);

        public Diagnostic Create(Rule rule, Location? location, IEnumerable<Location>? additionalLocations, params object?[] arguments) =>
            Diagnostic.Create(Choose(rule, location), location, additionalLocations, arguments);

        private DiagnosticDescriptor Choose(Rule rule, Location? location) => rule.Group switch {
            RuleGroup.Enforced when !_updatedMemorySafetyRules || _errorsAsWarnings => rule.Main,
            RuleGroup.Enforced => IsInUnsafeCode(location) ? rule.UnsafeWarning! : rule.Error!,
            RuleGroup.UncheckedAssembly => _referencesPackage ? rule.Main : rule.Message!,
            _ => rule.Main,
        };

        // The context is that of the node at the place of the report. This runs only for a report, so finding the node is no cost to
        // the walk.
        private static bool IsInUnsafeCode(Location? location) =>
            location is { IsInSource: true, SourceTree: { } tree } &&
            UnsafeContext.ContainsCode(tree.GetRoot().FindNode(location.SourceSpan, getInnermostNodeForTie: true));
    }
}
