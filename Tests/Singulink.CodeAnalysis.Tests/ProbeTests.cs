using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using PrefixClassName.MsTest;
using Singulink.CodeAnalysis.Analyzers;

namespace Singulink.CodeAnalysis.Tests;

/// <summary>
/// Holds the analyzer to what it reported on the probe projects of the test passes: each folder under <c>Probes</c> is one such
/// project, and its <c>baseline.txt</c> has every finding as file, line, column and ID. A finding that is new or gone fails the
/// test, which names it.
/// </summary>
/// <remarks>
/// <para>
/// The probes are programs that the passes wrote to find what the analyzer misses or reports wrongly, about 2,200 findings in
/// all. They guard against a change that loosens the analyzer by mistake: a finding that goes away shows here even where no test
/// of its own names the form. Changing a baseline is a deliberate edit that shows in a diff. Set the environment variable
/// <c>SCA_PROBE_WRITE</c> to a folder to have each run write the findings that it got there, in the form of a baseline.
/// </para>
/// <para>
/// A probe is built as its project was, as far as the settings file says (<c>probe.settings</c>): the kind of output, unsafe code,
/// nullable annotations, implicit usings and the build properties that the analyzer reads. Every <c>NonCopyableSymbols*.txt</c>
/// in the folder is an additional file, and a <c>Library</c> folder is compiled first as an assembly that the probe references.
/// The message text is not compared, since it may change.
/// </para>
/// </remarks>
[PrefixTestClass]
public sealed class ProbeTests
{
    private static readonly string Root = Path.Combine(AppContext.BaseDirectory, "Probes");

    public static IEnumerable<object[]> Probes => Directory.GetDirectories(Root).Select(folder => new object[] { Path.GetFileName(folder) }).OrderBy(p => (string)p[0]);

    [TestMethod]
    [DynamicData(nameof(Probes))]
    public async Task Probe_ReportsWhatItsBaselineSays(string probe)
    {
        string folder = Path.Combine(Root, probe);
        var found = await GetFindingsAsync(folder);

        if (Environment.GetEnvironmentVariable("SCA_PROBE_WRITE") is { Length: > 0 } written)
        {
            Directory.CreateDirectory(written);
            File.WriteAllText(Path.Combine(written, probe + ".txt"), string.Join("\n", found) + "\n");
        }

        var baseline = File.ReadAllLines(Path.Combine(folder, "baseline.txt")).Where(line => line.Length > 0).ToHashSet(StringComparer.Ordinal);
        var added = found.Where(line => !baseline.Contains(line)).ToList();
        var gone = baseline.Where(line => !found.Contains(line)).Order(StringComparer.Ordinal).ToList();

        if (added.Count > 0 || gone.Count > 0)
        {
            Assert.Fail(
                $"The findings of probe '{probe}' differ from its baseline." + Environment.NewLine +
                string.Concat(added.Select(line => Environment.NewLine + "new:  " + line)) +
                string.Concat(gone.Select(line => Environment.NewLine + "gone: " + line)));
        }
    }

    private static async Task<SortedSet<string>> GetFindingsAsync(string folder)
    {
        var settings = File.ReadAllLines(Path.Combine(folder, "probe.settings"))
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .Select(line => line.Split('=', 2))
            .ToDictionary(pair => pair[0], pair => pair[1], StringComparer.Ordinal);

        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var references = (await Verify.Test.NetReferenceAssemblies.ResolveAsync(LanguageNames.CSharp, CancellationToken.None))
            .Add(MetadataReference.CreateFromFile(typeof(NonCopyableAttribute).Assembly.Location));

        var analyzerOptions = new AnalyzerOptions(
            [.. Directory.GetFiles(folder, "NonCopyableSymbols*.txt", SearchOption.AllDirectories).Select(path => (AdditionalText)new ProbeText(path))],
            new ProbeConfig(GetBuildProperties(settings)));

        var findings = new SortedSet<string>(StringComparer.Ordinal);

        // A project that the probe references is built first, with the analyzer, as its own project was.
        string library = Path.Combine(folder, "Library");

        if (Directory.Exists(library))
        {
            // The library's project was built with the analyzer, so its assembly carries the marker that the package adds.
            var built = Compile("Library", library, OutputKind.DynamicallyLinkedLibrary);
            built = built.AddSyntaxTrees(CSharpSyntaxTree.ParseText(Verify.AnalyzedMarker, parseOptions, "AnalyzedMarker.cs"));
            await AddFindingsAsync(built);
            var image = new MemoryStream();
            Assert.IsTrue(built.Emit(image).Success, "The library of the probe does not build.");
            references = references.Add(MetadataReference.CreateFromImage(image.ToArray()));
        }

        await AddFindingsAsync(Compile("Probe", folder, settings["OutputKind"] is "Exe" ? OutputKind.ConsoleApplication : OutputKind.DynamicallyLinkedLibrary));
        return findings;

        CSharpCompilation Compile(string name, string sources, OutputKind kind)
        {
            var trees = Directory.GetFiles(sources, "*.cs")
                .Order(StringComparer.Ordinal)
                .Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path), parseOptions, Path.GetFileName(path)))
                .ToList();

            // What the SDK adds for <ImplicitUsings>enable</ImplicitUsings> in a console or library project.
            if (settings["ImplicitUsings"] is "true")
            {
                trees.Add(CSharpSyntaxTree.ParseText(
                    "global using System; global using System.Collections.Generic; global using System.IO; global using System.Linq; " +
                    "global using System.Net.Http; global using System.Threading; global using System.Threading.Tasks;",
                    parseOptions,
                    "GlobalUsings.g.cs"));
            }

            return CSharpCompilation.Create(
                name,
                trees,
                references,
                new CSharpCompilationOptions(
                    kind,
                    allowUnsafe: settings["AllowUnsafe"] is "true",
                    nullableContextOptions: settings["Nullable"] is "enable" ? NullableContextOptions.Enable : NullableContextOptions.Disable));
        }

        async Task AddFindingsAsync(CSharpCompilation compilation)
        {
            var diagnostics = await compilation
                .WithAnalyzers([new DoNotCopyValueAnalyzer()], analyzerOptions)
                .GetAnalyzerDiagnosticsAsync(CancellationToken.None);

            foreach (var diagnostic in diagnostics)
            {
                if (!diagnostic.Id.StartsWith("SCA", StringComparison.Ordinal) && diagnostic.Id is not "AD0001")
                    continue;

                var span = diagnostic.Location.GetLineSpan();
                string file = Path.GetFileName(span.Path);
                findings.Add($"{file}({span.StartLinePosition.Line + 1},{span.StartLinePosition.Character + 1}): {diagnostic.Id}");
            }
        }
    }

    /// <summary>
    /// Gets the build properties that the analyzer saw in the build of a probe. Every probe was a project that referenced a package of the
    /// analyzer itself, so the targets file of the package turned the built-in non-copyable types on, unless the settings say otherwise.
    /// </summary>
    private static ImmutableDictionary<string, string> GetBuildProperties(Dictionary<string, string> settings) =>
        settings.Where(pair => pair.Key.StartsWith("build_property.", StringComparison.Ordinal))
            .ToImmutableDictionary()
            .SetItem("build_property.NonCopyableIncludeBuiltInTypes", settings.GetValueOrDefault("build_property.NonCopyableIncludeBuiltInTypes", "true"));

    private sealed class ProbeText(string path) : AdditionalText
    {
        public override string Path { get; } = path;

        public override SourceText GetText(CancellationToken cancellationToken = default) => SourceText.From(File.ReadAllText(Path));
    }

    private sealed class ProbeConfig(ImmutableDictionary<string, string> globals) : AnalyzerConfigOptionsProvider
    {
        private static readonly ProbeOptions Empty = new(ImmutableDictionary<string, string>.Empty);

        public override AnalyzerConfigOptions GlobalOptions { get; } = new ProbeOptions(globals);

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => Empty;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => Empty;
    }

    private sealed class ProbeOptions(ImmutableDictionary<string, string> values) : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, out string value) => values.TryGetValue(key, out value!);
    }
}
