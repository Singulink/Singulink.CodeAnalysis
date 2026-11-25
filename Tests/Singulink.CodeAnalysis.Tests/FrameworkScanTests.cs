using System.Reflection.PortableExecutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using PrefixClassName.MsTest;

namespace Singulink.CodeAnalysis.Tests;

/// <summary>
/// Scans the shared frameworks that the tests run on, and the .NET Framework 4.8.1 where it is installed, for members that the rules for
/// code which was not built with the analyzer reach: every public or protected member that gets at one of the built-in non-copyable
/// types other than by value, or hands out a container of one, must have its entry in the built-in list.
/// </summary>
/// <remarks>
/// Docs: articles/guides/symbol-list.md and articles/rules/unchecked-code.md.
/// </remarks>
[PrefixTestClass]
public partial class FrameworkScanTests
{
    // The one built-in non-copyable type of WindowsBase, which of the shared frameworks only Microsoft.WindowsDesktop.App has.
    private const string DispatcherProcessingDisabled = "System.Windows.Threading.DispatcherProcessingDisabled";

    private static readonly string[] BuiltInTypes = [
        "System.Threading.SpinLock",
        "System.Runtime.InteropServices.GCHandle",
        "System.Runtime.InteropServices.GCHandle`1",
        "System.Runtime.InteropServices.PinnedGCHandle`1",
        "System.Runtime.InteropServices.WeakGCHandle`1",
        "System.Threading.Lock+Scope",
        "System.Buffers.MemoryHandle",
        "System.Runtime.CompilerServices.DefaultInterpolatedStringHandler",
        "System.Threading.AsyncFlowControl",
        "System.Threading.Tasks.Sources.ManualResetValueTaskSourceCore`1",
        DispatcherProcessingDisabled,
    ];

    // The built-in non-copyable types that the .NET Framework 4.8.1 has. MemoryHandle and ManualResetValueTaskSourceCore come to it in
    // the packages System.Memory and Microsoft.Bcl.AsyncInterfaces, which are no part of it, and the others only .NET has.
    private static readonly string[] BuiltInTypesOfNetFramework = [
        "System.Threading.SpinLock",
        "System.Runtime.InteropServices.GCHandle",
        "System.Threading.AsyncFlowControl",
        DispatcherProcessingDisabled,
    ];

    // A member that gets at one of them other than by value needs 'borrows'; one that hands out a container of one needs 'new'. Where
    // Microsoft.WindowsDesktop.App is not installed, as on a machine that is not Windows, its type is not there to scan.
    [TestMethod]
    public void SharedFrameworks_EveryMemberThatGetsAtABuiltInTypeOtherThanByValueIsListed()
    {
        bool hasWindowsDesktop = FindSharedFrameworks().Any(directory => directory.Contains("Microsoft.WindowsDesktop.App", StringComparison.Ordinal));
        CheckMembersThatGetAtABuiltInType(GetSharedFrameworkAssemblies(), [.. BuiltInTypes.Where(name => hasWindowsDesktop || name is not DispatcherProcessingDisabled)]);
    }

    // The same for the .NET Framework 4.8.1, which has four of them.
    [TestMethod]
    public async Task NetFramework_EveryMemberThatGetsAtABuiltInTypeOtherThanByValueIsListed() =>
        CheckMembersThatGetAtABuiltInType(await GetNetFrameworkAssembliesAsync(), BuiltInTypesOfNetFramework);

    private static void CheckMembersThatGetAtABuiltInType(List<string> assemblies, string[] builtInTypesThere)
    {
        Assert.IsNotEmpty(assemblies);

        var references = assemblies.Select(path => (MetadataReference)MetadataReference.CreateFromFile(path)).ToList();
        var compilation = CSharpCompilation.Create("Scan", references: references);
        var builtIn = BuiltInTypes.Select(name => compilation.GetTypeByMetadataName(name)).OfType<INamedTypeSymbol>().ToHashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        string[] present = [.. BuiltInTypes.Where(name => compilation.GetTypeByMetadataName(name) is not null)];
        Assert.IsTrue(present.SequenceEqual(builtInTypesThere), "The built-in non-copyable types that the scanned assemblies have are: " + string.Join(", ", present));

        var entries = ReadEntries();
        var missing = new SortedSet<string>(StringComparer.Ordinal);
        int types = 0, found = 0;

        foreach (var reference in references)
        {
            if (compilation.GetAssemblyOrModuleSymbol(reference) is not IAssemblySymbol assembly)
                continue;

            foreach (var type in GetVisibleTypes(assembly.GlobalNamespace))
            {
                types++;

                // A member of one of them is trusted with a value of its own type and with what it hands out.
                var owner = builtIn.Contains(type.OriginalDefinition) ? type.OriginalDefinition : null;

                foreach (var member in type.GetMembers().Where(IsVisible))
                {
                    var (inbound, outbound) = Judge(member, builtIn, owner);

                    if (!inbound && !outbound)
                        continue;

                    found++;
                    string id = member.GetDocumentationCommentId()!;
                    entries.TryGetValue(id, out string? marker);

                    if (inbound && marker is not "borrows")
                        missing.Add(id + ";borrows");
                    else if (outbound && !inbound && marker is not "new")
                        missing.Add(id + ";new");
                }
            }
        }

        Assert.IsGreaterThan(1000, types, "The scan reads the types of the framework.");
        Assert.IsEmpty(missing, $"{found} members reach a built-in non-copyable type; these have no entry with the marker that they need:\n" + string.Join("\n", missing));
    }

    private static (bool Inbound, bool Outbound) Judge(ISymbol member, HashSet<INamedTypeSymbol> builtIn, INamedTypeSymbol? owner)
    {
        // The value of a setter or an init accessor that the scan can see is a parameter, after those of an indexer.
        var parameters = member switch {
            IMethodSymbol method => method.Parameters,
            IPropertySymbol { SetMethod: { } setter } when IsVisible(setter) => setter.Parameters,
            IPropertySymbol property => property.Parameters,
            _ => [],
        };

        bool inbound = parameters.Any(parameter =>
            parameter.RefKind is not RefKind.Out &&
            !IsOwnType(parameter.Type, owner) &&
            ((parameter.RefKind is not RefKind.None && IsOneOf(parameter.Type, builtIn)) || Carries(parameter.Type, builtIn)));

        var handedOut = member switch {
            IMethodSymbol { MethodKind: not (MethodKind.PropertyGet or MethodKind.PropertySet or MethodKind.EventAdd or MethodKind.EventRemove) } method => method.ReturnType,
            IPropertySymbol property => property.Type,
            _ => null,
        };

        bool outbound = owner is null && handedOut is not null && Carries(handedOut, builtIn);

        // An accessor is judged with its property.
        if (member is IMethodSymbol { AssociatedSymbol: not null })
            return (false, false);

        return (inbound, outbound);
    }

    private static bool IsOwnType(ITypeSymbol type, INamedTypeSymbol? owner)
    {
        while (type is IPointerTypeSymbol pointer)
            type = pointer.PointedAtType;

        return owner is not null && SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, owner);
    }

    private static bool IsOneOf(ITypeSymbol type, HashSet<INamedTypeSymbol> builtIn) =>
        type is INamedTypeSymbol named && builtIn.Contains(named.OriginalDefinition);

    // A type that holds one of them without being one: an array, a pointer, a generic type with one among its type arguments.
    private static bool Carries(ITypeSymbol type, HashSet<INamedTypeSymbol> builtIn) => !IsOneOf(type, builtIn) && Mentions(type, builtIn);

    private static bool Mentions(ITypeSymbol type, HashSet<INamedTypeSymbol> builtIn) => type switch {
        IArrayTypeSymbol array => Mentions(array.ElementType, builtIn),
        IPointerTypeSymbol pointer => Mentions(pointer.PointedAtType, builtIn),
        INamedTypeSymbol named => builtIn.Contains(named.OriginalDefinition) || named.TypeArguments.Any(argument => Mentions(argument, builtIn)) ||
            (named.ContainingType is { } containing && Mentions(containing, builtIn)),
        _ => false,
    };

    private static IEnumerable<INamedTypeSymbol> GetVisibleTypes(INamespaceOrTypeSymbol container)
    {
        foreach (var member in container.GetMembers())
        {
            if (member is INamespaceSymbol ns)
            {
                foreach (var type in GetVisibleTypes(ns))
                    yield return type;
            }
            else if (member is INamedTypeSymbol type && IsVisible(type))
            {
                yield return type;

                foreach (var nested in GetVisibleTypes(type))
                    yield return nested;
            }
        }
    }

    private static bool IsVisible(ISymbol symbol) =>
        symbol.DeclaredAccessibility is Accessibility.Public or Accessibility.Protected or Accessibility.ProtectedOrInternal && !symbol.IsImplicitlyDeclared;

    // The check of a base class of which nothing is known takes an assembly for one of the framework by its simple name, from the file
    // FrameworkAssemblies.txt: of those assemblies the built-in list says which classes clone themselves, because the scans of this
    // class read them. So the file must name every assembly that the scans read. A new assembly of a shared framework fails here, and
    // is added to the file together with what the scans find in it.
    //
    // The .NET Framework 4.8.1 has no such test. Its assemblies that the file names are among those that the scans of it read, so what
    // the built-in list says of them is checked there too. The others, such as System.Messaging, are other libraries for the analyzer
    // by design, as every library that is no part of a shared framework is, and a test that pinned their names would pin a list that
    // does not change: 4.8.1 is the last version of the .NET Framework.
    [TestMethod]
    public void FrameworkAssemblies_NameEveryAssemblyOfTheSharedFrameworks()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using (var stream = typeof(Singulink.CodeAnalysis.Analyzers.DoNotCopyValueAnalyzer).Assembly
            .GetManifestResourceStream("Singulink.CodeAnalysis.Analyzers.FrameworkAssemblies.txt")!)
        using (var reader = new StreamReader(stream))
        {
            while (reader.ReadLine() is { } line)
            {
                if (line.Trim() is { Length: > 0 } name && name[0] is not '#')
                    names.Add(name);
            }
        }

        var missing = new SortedSet<string>(StringComparer.Ordinal);
        int read = 0;

        foreach (string path in GetSharedFrameworkAssemblies())
        {
            read++;
            string name = System.Reflection.AssemblyName.GetAssemblyName(path).Name!;

            if (!names.Contains(name))
                missing.Add(name);
        }

        Assert.IsGreaterThan(100, read, "The scan reads the assemblies of the shared frameworks.");
        Assert.IsEmpty(missing, $"FrameworkAssemblies.txt does not name: {string.Join(", ", missing)}");

        foreach (string name in new[] { "System.Runtime", "System.Private.CoreLib", "netstandard", "mscorlib", "System" })
            Assert.Contains(name, names, $"FrameworkAssemblies.txt names {name}.");
    }

    internal static bool HasMetadata(string path)
    {
        try
        {
            using var reader = new PEReader(File.OpenRead(path));
            return reader.HasMetadata;
        }
        catch (BadImageFormatException)
        {
            return false;
        }
    }

    // Every assembly of the shared frameworks that the tests run on.
    private static List<string> GetSharedFrameworkAssemblies() =>
        [.. FindSharedFrameworks().SelectMany(directory => Directory.GetFiles(directory, "*.dll")).Where(HasMetadata)];

    /// <summary>
    /// Gets the folders of the shared frameworks of the runtime that the tests run on: Microsoft.NETCore.App, and Microsoft.AspNetCore.App
    /// and Microsoft.WindowsDesktop.App of the same major version where they are installed. System.Private.CoreLib is taken from the folder
    /// of the running core library, which is where it is.
    /// </summary>
    private static List<string> FindSharedFrameworks()
    {
        string core = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        string shared = Path.GetDirectoryName(Path.GetDirectoryName(core)!)!;
        var version = new Version(Path.GetFileName(core).Split('-')[0]);
        var result = new List<string> { core };

        foreach (string name in new[] { "Microsoft.AspNetCore.App", "Microsoft.WindowsDesktop.App" })
        {
            string root = Path.Combine(shared, name);

            if (!Directory.Exists(root))
                continue;

            string? best = Directory.GetDirectories(root)
                .Select(directory => (directory, Parsed: Version.TryParse(Path.GetFileName(directory).Split('-')[0], out var v) ? v : null))
                .Where(candidate => candidate.Parsed is { } v && v.Major == version.Major)
                .OrderByDescending(candidate => candidate.Parsed)
                .Select(candidate => candidate.directory)
                .FirstOrDefault();

            if (best is not null)
                result.Add(best);
        }

        return result;
    }

    private static Dictionary<string, string?> ReadEntries()
    {
        using var stream = typeof(Singulink.CodeAnalysis.Analyzers.DoNotCopyValueAnalyzer).Assembly
            .GetManifestResourceStream("Singulink.CodeAnalysis.Analyzers.NonCopyableSymbols.txt")!;

        using var reader = new StreamReader(stream);
        var entries = new Dictionary<string, string?>(StringComparer.Ordinal);

        while (reader.ReadLine() is { } line)
        {
            string entry = line.Trim();

            if (entry.Length is 0 || entry[0] is '#')
                continue;

            int comment = entry.IndexOf(" #", StringComparison.Ordinal);
            string[] parts = (comment >= 0 ? entry[..comment] : entry).Split(';', StringSplitOptions.TrimEntries);

            // 'clones=complete' is a 'clones' line too: ReadCompleteClasses reads the classes whose line says it.
            string? marker = parts.Length > 1 && !parts[1].StartsWith("for=", StringComparison.Ordinal) ? parts[1] : null;
            entries[parts[0]] = marker is "clones=complete" ? "clones" : marker;
        }

        return entries;
    }

    // The classes whose line of the built-in list says 'clones=complete', by their names without 'T:'.
    private static HashSet<string> ReadCompleteClasses()
    {
        using var stream = typeof(Singulink.CodeAnalysis.Analyzers.DoNotCopyValueAnalyzer).Assembly
            .GetManifestResourceStream("Singulink.CodeAnalysis.Analyzers.NonCopyableSymbols.txt")!;

        using var reader = new StreamReader(stream);
        var classes = new HashSet<string>(StringComparer.Ordinal);

        while (reader.ReadLine() is { } line)
        {
            string entry = line.Split(" #")[0].Trim();

            if (entry.StartsWith("T:", StringComparison.Ordinal) && entry.EndsWith(";clones=complete", StringComparison.Ordinal))
                classes.Add(entry[2..^";clones=complete".Length]);
        }

        return classes;
    }
}
