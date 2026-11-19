using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;

namespace Singulink.CodeAnalysis.Analyzers;

/// <summary>
/// Tells the assemblies that were built with the analyzer from those that were not. An assembly was built with it if it is the
/// compilation that is being analyzed, or if it carries the marker that the targets file of the package adds to every build that runs
/// the analyzers: <c>[assembly: AssemblyMetadata("Singulink.CodeAnalysis.Analyzed", "1")]</c>. Every other assembly is unmarked: the
/// framework, a third-party library, a project of the solution with the analyzers turned off.
/// </summary>
/// <remarks>
/// The marker is a statement of the build, like a line of a list file: a project that suppresses findings still carries it. Its value
/// is the version of the marker; any value counts, so that a later version of the marker is still read as one.
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

    private static bool HasMarker(IAssemblySymbol assembly)
    {
        foreach (var attribute in assembly.GetAttributes())
        {
            if (attribute.AttributeClass is { Name: "AssemblyMetadataAttribute" } attributeClass &&
                NonCopyableTypes.IsInNamespace(attributeClass, "System.Reflection") &&
                attribute.ConstructorArguments is [{ Value: MarkerKey }, _])
            {
                return true;
            }
        }

        return false;
    }
}
