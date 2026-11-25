using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.Win32;

namespace Singulink.CodeAnalysis.Tests;

/// <content>
/// Finds the assemblies of the .NET Framework 4.8.1 for the scans, which read them as one more framework where it is installed: the
/// built-in list has lines that only the .NET Framework has, and its classes and members are checked as those of .NET are.
/// </content>
/// <remarks>
/// Docs: DESIGN.md, Symbol List, and NonCopyableSymbols.evidence.md.
/// </remarks>
public partial class FrameworkScanTests
{
    // The NuGet package of the reference assemblies of the .NET Framework 4.8.1. Their folder also has RedistList/FrameworkList.xml, the
    // list of the assemblies of the framework. They have no method bodies, so they serve to resolve the lines of the built-in list, and
    // the scans read the implementation assemblies instead.
    internal static readonly ReferenceAssemblies NetFrameworkReferencePackage = new ReferenceAssemblies(
        "net481", new PackageIdentity("Microsoft.NETFramework.ReferenceAssemblies.net481", "1.0.3"), Path.Combine("build", ".NETFramework", "v4.8.1"))
        .AddAssemblies(["mscorlib"]);

    // The first release of the .NET Framework 4.8.1 in the registry: 533320 on Windows 11 2022 and later, 533325 on the others.
    private const int NetFramework481Release = 533320;

    // The assemblies that FrameworkList.xml names and that neither the reference assemblies nor the runtime have, each with its reason.
    private static readonly Dictionary<string, string> NetFrameworkAssembliesThatDoNotExist = new(StringComparer.Ordinal) {
        ["System.Activities.Statements"] = "the list names it, and there is no such reference assembly or assembly of the runtime",
    };

    // The scans of the .NET Framework read the same assemblies on every target of the tests, so they run in the test run of one target.
    private static bool IsNetFrameworkScanTarget =>
        typeof(FrameworkScanTests).Assembly.GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName is ".NETCoreApp,Version=v10.0";

    /// <summary>
    /// Gets every reference assembly of the .NET Framework 4.8.1: those of the folder of the package, but the two files there that are
    /// not assemblies, System.EnterpriseServices.Thunk and System.EnterpriseServices.Wrapper.
    /// </summary>
    internal static async Task<List<MetadataReference>> GetNetFrameworkReferenceAssembliesAsync() =>
        [.. Directory.GetFiles(await GetNetFrameworkReferenceFolderAsync(), "*.dll").Where(HasMetadata).Select(path => MetadataReference.CreateFromFile(path))];

    private static async Task<string> GetNetFrameworkReferenceFolderAsync()
    {
        var references = await NetFrameworkReferencePackage.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        return Path.GetDirectoryName(references.Single(r => Path.GetFileName(r.Display) is "mscorlib.dll").Display)!;
    }

    /// <summary>
    /// Gets the paths of the implementation assemblies of the .NET Framework 4.8.1 of this machine: every assembly that FrameworkList.xml
    /// of its reference assemblies names, from the global assembly cache or from the folder of the runtime. A test that calls this is
    /// inconclusive, with a message that says why, in the run of a target other than net10.0, on a machine that is not Windows, and on one
    /// that does not have the .NET Framework 4.8.1. It fails for an assembly of the list that the machine does not have.
    /// </summary>
    private static async Task<List<string>> GetNetFrameworkAssembliesAsync()
    {
        if (!IsNetFrameworkScanTarget)
            Assert.Inconclusive("The scans of the .NET Framework 4.8.1 run in the test run of net10.0 only, since they read the same assemblies on every target.");

        if (!OperatingSystem.IsWindows())
            Assert.Inconclusive($"The scans of the .NET Framework 4.8.1 did not run: it exists only on Windows, and this machine runs {RuntimeInformation.OSDescription}.");

        // The platform analyzer does not see that Assert.Inconclusive ends the test, so the registry is read only on Windows here as well.
        int release = OperatingSystem.IsWindows() && Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full", "Release", null) is int value ? value : 0;

        if (release < NetFramework481Release)
        {
            Assert.Inconclusive(
                $"The scans of the .NET Framework 4.8.1 did not run: this machine does not have it. Its release in the registry is {release}, and " +
                $"4.8.1 is {NetFramework481Release} or later.");
        }

        var list = XDocument.Load(Path.Combine(await GetNetFrameworkReferenceFolderAsync(), "RedistList", "FrameworkList.xml"));

        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        string cache = Path.Combine(windows, "Microsoft.NET", "assembly");
        string runtime = Path.Combine(windows, "Microsoft.NET", Environment.Is64BitProcess ? "Framework64" : "Framework", "v4.0.30319");
        string ownCache = Environment.Is64BitProcess ? "GAC_64" : "GAC_32";
        var paths = new List<string>();
        var missing = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var file in list.Root!.Elements("File"))
        {
            string name = (string)file.Attribute("AssemblyName")!;
            string version = $"v4.0_{(string)file.Attribute("Version")!}__{(string)file.Attribute("PublicKeyToken")!}";

            // An assembly with native code is in the cache of its processor, mscorlib and the facades are in the folder of the runtime, and
            // a few assemblies of WPF are in its folder WPF.
            string? path = new[] {
                Path.Combine(cache, ownCache, name, version, name + ".dll"),
                Path.Combine(cache, "GAC_MSIL", name, version, name + ".dll"),
                Path.Combine(runtime, name + ".dll"),
                Path.Combine(runtime, "WPF", name + ".dll"),
            }.FirstOrDefault(File.Exists);

            if (path is not null)
                paths.Add(path);
            else if (!NetFrameworkAssembliesThatDoNotExist.ContainsKey(name))
                missing.Add(name);
        }

        Assert.IsEmpty(missing, "This machine has the .NET Framework 4.8.1, and these assemblies of it are in neither the global assembly cache nor the folder of the runtime: " + string.Join(", ", missing));
        Assert.IsGreaterThan(200, paths.Count, "The scans read the assemblies that FrameworkList.xml names.");

        return [.. paths.Where(HasMetadata)];
    }
}
