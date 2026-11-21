using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;

namespace Singulink.CodeAnalysis.Analyzers;

/// <summary>
/// Tells the assemblies that were built with the analyzer from those that were not. An assembly was built with it if it is the
/// compilation that is being analyzed, or if it carries the marker that the targets file of the package adds to every build that runs
/// the analyzers: <c>[assembly: AssemblyMetadata("Singulink.CodeAnalysis.Analyzed", "0.7.0")]</c>. Every other assembly is unmarked:
/// the framework, a third-party library, a project of the solution with the analyzers turned off.
/// </summary>
/// <remarks>
/// The marker is a statement of the build, like a line of a list file: a project that suppresses findings still carries it. Its value
/// is the version of the analyzers that the assembly was built with, which <see cref="GetRecordedVersion"/> reads. Any value counts
/// as the marker, the "1" that the marker had before it recorded a version included.
/// </remarks>
internal static class AnalyzedAssemblies
{
    /// <summary>
    /// The key of the assembly metadata attribute that marks an assembly that was built with the analyzer.
    /// </summary>
    public const string MarkerKey = "Singulink.CodeAnalysis.Analyzed";

    private static readonly object s_true = true;
    private static readonly object s_false = false;

    // The assembly of each compilation that is being analyzed. A weak table, so that nothing of a compilation outlives it.
    private static readonly ConditionalWeakTable<IAssemblySymbol, object> s_compilations = new();

    // Whether an assembly carries the marker, read once.
    private static readonly ConditionalWeakTable<IAssemblySymbol, object> s_marked = new();

    /// <summary>
    /// Records the assembly of a compilation that is being analyzed, which is analyzed whether it carries the marker or not.
    /// </summary>
    public static void AddCompilation(IAssemblySymbol assembly) => s_compilations.GetValue(assembly, static _ => s_true);

    /// <summary>
    /// Gets a value indicating whether an assembly was built with the analyzer. A symbol with no assembly is taken to be of checked code.
    /// </summary>
    public static bool IsAnalyzed(IAssemblySymbol? assembly) =>
        assembly is null || s_compilations.TryGetValue(assembly, out _) || s_marked.GetValue(assembly, static a => HasMarker(a) ? s_true : s_false) == s_true;

    /// <summary>
    /// Gets a value indicating whether a symbol is declared in an assembly that was built with the analyzer.
    /// </summary>
    public static bool IsOfAnalyzedAssembly(ISymbol symbol) => IsAnalyzed(symbol.ContainingAssembly);

    /// <summary>
    /// Gets the version of the analyzers that an assembly records in its marker. Null where it records none: an assembly with no marker,
    /// and a marker whose value is not a version, such as "1".
    /// </summary>
    public static AnalyzerVersion? GetRecordedVersion(IAssemblySymbol assembly) =>
        FindMarker(assembly) is { } marker && marker.ConstructorArguments[1].Value is string value ? AnalyzerVersion.Parse(value) : null;

    private static bool HasMarker(IAssemblySymbol assembly) => FindMarker(assembly) is not null;

    private static AttributeData? FindMarker(IAssemblySymbol assembly)
    {
        foreach (var attribute in assembly.GetAttributes())
        {
            if (attribute.AttributeClass is { Name: "AssemblyMetadataAttribute" } attributeClass &&
                NonCopyableTypes.IsInNamespace(attributeClass, "System.Reflection") &&
                attribute.ConstructorArguments is [{ Value: MarkerKey }, _])
            {
                return attribute;
            }
        }

        return null;
    }
}

/// <summary>
/// A version of the analyzers as a package version: major, minor and patch, with a prerelease label such as "local.2" where it has
/// one. Build metadata after a '+' is left out.
/// </summary>
internal readonly record struct AnalyzerVersion(int Major, int Minor, int Patch, string? Prerelease)
{
    /// <summary>
    /// Parses a version of the form "0.7.0", "0.7.1-local.2" or "1.0.0+abc". Null for anything else.
    /// </summary>
    public static AnalyzerVersion? Parse(string? value)
    {
        if (value is null)
            return null;

        int plus = value.IndexOf('+');
        string core = plus < 0 ? value : value[..plus];
        int dash = core.IndexOf('-');
        string? prerelease = dash < 0 ? null : core[(dash + 1)..];
        string[] parts = (dash < 0 ? core : core[..dash]).Split('.');

        if (parts.Length is not 3 || prerelease is "" || (plus >= 0 && plus == value.Length - 1))
            return null;

        int[] numbers = new int[3];

        for (int i = 0; i < 3; i++)
        {
            if (parts[i].Length is 0 || !parts[i].All(c => c is >= '0' and <= '9') ||
                !int.TryParse(parts[i], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out numbers[i]))
            {
                return null;
            }
        }

        return new AnalyzerVersion(numbers[0], numbers[1], numbers[2], prerelease);
    }

    /// <inheritdoc/>
    public override string ToString() => Prerelease is null ? $"{Major}.{Minor}.{Patch}" : $"{Major}.{Minor}.{Patch}-{Prerelease}";
}
