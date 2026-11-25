using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
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
    /// which can be suppressed and configured in every way, wherever it is reported. In a compilation that uses the updated rules a
    /// report about a type that is unsafe to copy, or one that cannot be tied to a type (<see cref="About"/>), is an error that cannot
    /// be suppressed, and a warning where it is reported in code that is in an unsafe context, or at a declaration that is marked
    /// <c>unsafe</c> or <c>safe</c>. The place decides there, not the ID: a report at a declaration with neither modifier is the error.
    /// A declaration marked <c>safe</c> counts only where the project allows unsafe code (<see cref="SafeModifierCounts(Compilation)"/>).
    /// A report about any other non-copyable type is the ordinary warning of the current rules, with their message, wherever it is.
    /// The MSBuild property NonCopyableErrorsAsWarnings makes the analyzer report as under the current rules: the ordinary warning
    /// everywhere, with the message of the current rules. It is a preference for how reports appear and are suppressed; the project
    /// still gets every report, so its build keeps the marker of an analyzed build.</para>
    /// <para>
    /// SCA1025 is a warning in a project that references a package of the analyzer itself, and a message in one that only gets the
    /// analyzer through another package, which did not choose to check the assemblies that it references. A build in which the targets
    /// file did not run, such as a test, counts as one that references a package.</para>
    /// </remarks>
    private sealed class Severities(Compilation compilation, AnalyzerOptions options, NonCopyableTypes types)
    {
        private readonly bool _errorsAsWarnings = IsSetTo(options, ErrorsAsWarningsOption, "true");
        private readonly bool _referencesPackage = !IsSetToFalse(options, ReferencesPackageOption);
        private readonly bool _updatedMemorySafetyRules = UnsafeContext.UsesUpdatedRules(compilation);
        private readonly bool _safeModifierCounts = SafeModifierCounts(compilation);

        public Diagnostic Create(Rule rule, Location? location, params object?[] arguments) =>
            Diagnostic.Create(Choose(rule, default, location, arguments), location, arguments);

        public Diagnostic Create(Rule rule, Location? location, IEnumerable<Location>? additionalLocations, params object?[] arguments) =>
            Diagnostic.Create(Choose(rule, default, location, arguments), location, additionalLocations, arguments);

        /// <summary>
        /// Creates the diagnostic of a rule whose message does not name the type that the report is about (<see cref="About.Given"/>).
        /// </summary>
        public Diagnostic CreateAbout(Rule rule, Subject subject, Location? location, params object?[] arguments) =>
            Diagnostic.Create(Choose(rule, subject, location, arguments), location, arguments);

        public Diagnostic CreateAbout(Rule rule, Subject subject, Location? location, IEnumerable<Location>? additionalLocations, params object?[] arguments) =>
            Diagnostic.Create(Choose(rule, subject, location, arguments), location, additionalLocations, arguments);

        private DiagnosticDescriptor Choose(Rule rule, Subject subject, Location? location, object?[] arguments) => rule.Group switch {
            RuleGroup.Enforced when !_updatedMemorySafetyRules || _errorsAsWarnings || !IsAboutUnsafeToCopy(rule, subject, arguments) => rule.Main,
            RuleGroup.Enforced => IsAnsweredFor(location) ? rule.UnsafeWarning! : rule.Error!,
            RuleGroup.UncheckedAssembly => _referencesPackage ? rule.Main : rule.Message!,
            _ => rule.Main,
        };

        // Whether a report of an enforced rule is about a type that is unsafe to copy, or cannot be tied to a type: the types are those
        // that the code which reports gives, or else those of the arguments of the message that the rule names. A report that should
        // have been given a type and was not, or whose argument is no type, cannot be tied to one either. This runs only for a report.
        // Where the answer cannot be worked out, the report is strict: an exception here never reaches the code that reports, where it
        // would stop the check of the member or of the declaration and lose its other reports. Only a cancellation of the build goes on.
        private bool IsAboutUnsafeToCopy(Rule rule, Subject subject, object?[] arguments)
        {
            try
            {
                return IsAboutUnsafeToCopyCore(rule, subject, arguments);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                return true;
            }
        }

        private bool IsAboutUnsafeToCopyCore(Rule rule, Subject subject, object?[] arguments)
        {
            var about = rule.About;

            if (subject.IsSpecified)
            {
                foreach (var type in subject.Types)
                {
                    if (type is null || (subject.IsDeclared && NonCopyableTypes.MentionsTypeParameterThatTakesUnsafeToCopy(type)) || IsAbout(type))
                        return true;
                }

                return subject.Types.IsEmpty && !subject.InvolvesNoUnsafeToCopy;
            }

            if (about.IsGiven || about.Arguments.IsEmpty)
                return true;

            foreach (int index in about.Arguments)
            {
                if (index >= arguments.Length || arguments[index] is not ITypeSymbol type || IsAbout(type))
                    return true;
            }

            return false;

            // A type parameter in the place of the type stands for whatever is bound to it, unless it says that it takes no type that
            // is unsafe to copy.
            bool IsAbout(ITypeSymbol type) => type is ITypeParameterSymbol typeParameter
                ? !NonCopyableTypes.TakesNoUnsafeToCopy(typeParameter)
                : about.WhatItHolds ? types.HoldsUnsafeToCopy(type) : types.IsAboutUnsafeToCopy(type);
        }

        /// <summary>
        /// Gets a value indicating whether a declaration marked <c>safe</c> answers for a report at it under the updated rules, as one
        /// marked <c>unsafe</c> does: only in a project that allows unsafe code. Answering for a report takes the project's choice to
        /// allow unsafe code, whatever a given compiler requires for the modifier. The modifier <c>unsafe</c> needs no such check, since
        /// the compiler accepts it only in such a project.
        /// </summary>
        private static bool SafeModifierCounts(Compilation compilation) => compilation.Options is CSharpCompilationOptions { AllowUnsafe: true };

        // Whether the author answers for a report under the updated rules: in code in an unsafe context, or at a declaration that is
        // marked to answer for it. The place is the node at the location of the report. This runs only for a report, so finding the
        // node is no cost to the walk.
        private bool IsAnsweredFor(Location? location)
        {
            if (location is not { IsInSource: true, SourceTree: { } tree })
                return false;

            var node = tree.GetRoot().FindNode(location.SourceSpan, getInnermostNodeForTie: true);
            return UnsafeContext.ContainsCode(node) || UnsafeContext.IsAtMarkedDeclaration(node, _safeModifierCounts);
        }
    }
}
