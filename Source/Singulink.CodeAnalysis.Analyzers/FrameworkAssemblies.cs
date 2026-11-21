using Microsoft.CodeAnalysis;

namespace Singulink.CodeAnalysis.Analyzers;

/// <summary>
/// Tells the assemblies of the framework from every other assembly that was not built with the analyzer. The framework is what the
/// built-in symbol list was checked against: the shared frameworks Microsoft.NETCore.App, Microsoft.AspNetCore.App and
/// Microsoft.WindowsDesktop.App, whose IL the scan of the tests reads for every class that clones itself. An assembly is one of theirs
/// by its simple name, which the embedded file FrameworkAssemblies.txt lists.
/// </summary>
/// <remarks>
/// <para>
/// The list says what is known about a class, not who wrote it: for a class of the framework, the built-in symbol list says whether it
/// clones itself. A library of Microsoft's that is no part of a shared framework, such as WinUI, was not read, and so is not listed.
/// </para>
/// <para>
/// A project of another target is judged by the same names: the reference assemblies of .NET Standard and of the .NET Framework, such
/// as netstandard, mscorlib and System, are among the assemblies of the shared frameworks, where they forward to the implementation.
/// The name alone is what counts, as for a 'trusted' line: an assembly of another origin that takes a framework's name only loses a
/// warning, which is the state before the check of an unknown base class existed.
/// </para>
/// </remarks>
internal static class FrameworkAssemblies
{
    private const string ResourceName = "Singulink.CodeAnalysis.Analyzers.FrameworkAssemblies.txt";

    private static readonly Lazy<HashSet<string>> s_names = new(Read);

    /// <summary>
    /// Gets the simple names of the assemblies of the framework.
    /// </summary>
    public static IReadOnlyCollection<string> Names => s_names.Value;

    /// <summary>
    /// Gets a value indicating whether an assembly is one of the framework, by its simple name in any case, as the runtime binds it.
    /// </summary>
    public static bool Contains(IAssemblySymbol assembly) => s_names.Value.Contains(assembly.Name);

    private static HashSet<string> Read()
    {
        using var stream = typeof(FrameworkAssemblies).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Resource '{ResourceName}' is missing.");

        using var reader = new StreamReader(stream);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        while (reader.ReadLine() is { } line)
        {
            line = line.Trim();

            if (line.Length > 0 && line[0] is not '#')
                names.Add(line);
        }

        return names;
    }
}
