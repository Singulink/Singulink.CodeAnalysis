using System.Text;
using Microsoft.CodeAnalysis;
using PrefixClassName.MsTest;

namespace Singulink.CodeAnalysis.Tests;

/// <summary>
/// Tests of what the assets file of a build (project.assets.json) says for a package that comes with a reference assembly and was not
/// built with the analyzer (SCA1025): the code of a package can use only the packages that it depends on, so the package is reported
/// only for an assembly of one of those. An assembly of a project, and a build with no assets file, are judged with no such knowledge.
/// </summary>
/// <remarks>
/// Docs: articles/diagnostics/sca1025.md and articles/rules/unchecked-code.md.
/// </remarks>
[PrefixTestClass]
public class PackageGraphTests
{
    // A checked library with a guard.
    private const string Guards = """
        using Singulink.CodeAnalysis;

        namespace Guards
        {
            [NonCopyable]
            public struct Guard
            {
                public int Id;
            }
        }
        """;

    // A library that uses nothing of another library.
    private const string Plain = """
        namespace Helpers
        {
            public static class Helper
            {
                public static int Run() => 1;
            }
        }
        """;

    private const string NoEffect =
        "the assembly gets no warning for the line to answer: no non-copyable value of other code is within its reach, and no field of the compilation behind a class of the assembly holds one";

    // A package with the assembly 'Helper' that depends on nothing of the build, and a package with the assembly 'Guards'.
    private static readonly Library HelperPackage = new("Helper", IsPackage: true, "Helper.Native", "Helper");
    private static readonly Library NativePackage = new("Helper.Native", IsPackage: true, string.Empty, null);
    private static readonly Library GuardsPackage = new("Guards", IsPackage: true, string.Empty, "Guards");

    // A project is in the file under its package ID, and its assembly under a placeholder of that name.
    private static readonly Library GuardsProject = new("Contoso.Guards", IsPackage: false, string.Empty, "Contoso.Guards");

    // The assets file with one target per list of libraries.
    private static string Assets(params Library[][] targets)
    {
        var json = new StringBuilder("{\n  \"version\": 3,\n  \"targets\": {");

        for (int i = 0; i < targets.Length; i++)
        {
            json.Append(i > 0 ? "," : string.Empty).Append($"\n    \"net{10 + i}.0\": {{");

            foreach (var library in targets[i])
            {
                string dependencies = string.Join(", ", library.Dependencies.Split([','], StringSplitOptions.RemoveEmptyEntries).Select(d => $"\"{d}\": \"1.0.0\""));
                string folder = library.IsPackage ? "ref/net10.0" : "bin/placeholder";
                string assembly = library.Assembly is null ? "_._" : library.Assembly + ".dll";

                json.Append(library == targets[i][0] ? string.Empty : ",")
                    .Append($"\n      \"{library.Id}/1.0.0\": {{ \"type\": \"{(library.IsPackage ? "package" : "project")}\", \"framework\": \".NETCoreApp,Version=v10.0\", ")
                    .Append($"\"dependencies\": {{ {dependencies} }}, \"compile\": {{ \"{folder}/{assembly}\": {{ \"related\": \".xml\" }} }}, ")
                    .Append($"\"runtime\": {{ \"lib/net10.0/{assembly}\": {{}} }}, \"build\": {{ \"build/x.targets\": {{}} }} }}");
            }

            json.Append("\n    }");
        }

        return json.Append("\n  },\n  \"libraries\": { \"Helper/1.0.0\": { \"sha512\": \"a\\\"b\\\\\", \"files\": [ \"ref/net10.0/Helper.dll\", 1, true, null ] } },\n  \"project\": { \"version\": \"1.0.0\" }\n}\n").ToString();
    }

    private static async Task<Verify.Test> CreateTestAsync(string? assets, string? listFile = null)
    {
        var guards = await Verify.CompileLibraryAsync(Verify.Test.NetReferenceAssemblies, Guards, analyzed: true, asReferenceAssembly: false, "Guards");
        var helper = await Verify.CompileLibraryAsync(Verify.Test.NetReferenceAssemblies, Plain, analyzed: false, asReferenceAssembly: true, "Helper");

        var test = new Verify.Test { TestCode = "class C { }" };
        test.TestState.AdditionalReferences.Add(guards);
        test.TestState.AdditionalReferences.Add(helper);

        if (assets is not null)
            test.TestState.AdditionalFiles.Add(("obj/project.assets.json", assets));

        if (listFile is not null)
            test.TestState.AdditionalFiles.Add((Verify.ListFileName, listFile));

        return test;
    }

    // With no assets file the package is judged like any reference assembly: a non-copyable type of another assembly is within its
    // reach.
    [TestMethod]
    public async Task Package_IsReportedWhereTheBuildGivesNoAssetsFile()
    {
        var test = await CreateTestAsync(assets: null);
        test.ExpectedDiagnostics.Add(Verify.UncheckedReferenceAssembly("Helper", "Guards"));
        await test.RunAsync();
    }

    // The code of a package that depends on nothing which has the other assembly cannot use it, whether that assembly is of a package
    // or of a project of the solution.
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task Package_IsNotReportedForAnAssemblyThatItDoesNotDependOn(bool otherIsOfAPackage)
    {
        var test = await CreateTestAsync(Assets([HelperPackage, NativePackage, otherIsOfAPackage ? GuardsPackage : GuardsProject]));
        await test.RunAsync();
    }

    // A package that depends on the package of the other assembly, itself or through another package, can use it.
    [TestMethod]
    [DataRow("Guards")]
    [DataRow("Middle")]
    [DataRow("guards")]
    public async Task Package_IsReportedForAnAssemblyOfAPackageThatItDependsOn(string dependency)
    {
        var test = await CreateTestAsync(Assets([
            new("Helper", IsPackage: true, dependency, "Helper"),
            new("Middle", IsPackage: true, "Other,Guards", null),
            new("Other", IsPackage: true, string.Empty, "Other"),
            GuardsPackage,
        ]));

        test.ExpectedDiagnostics.Add(Verify.UncheckedReferenceAssembly("Helper", "Guards"));
        await test.RunAsync();
    }

    // A package that has the other assembly itself can use it.
    [TestMethod]
    public async Task Package_IsReportedForAnotherAssemblyOfItsOwn()
    {
        string assets = Assets([HelperPackage, NativePackage]).Replace("\"lib/net10.0/Helper.dll\": {}", "\"lib/net10.0/Helper.dll\": {}, \"lib/net10.0/Guards.dll\": {}");
        var test = await CreateTestAsync(assets);

        test.ExpectedDiagnostics.Add(Verify.UncheckedReferenceAssembly("Helper", "Guards"));
        await test.RunAsync();
    }

    // The file does not name the assemblies of a project, so a package that depends on one, or on something that the file has no
    // entry for, can use an assembly that is of no package. An assembly of a package that it does not depend on stays out of reach.
    [TestMethod]
    [DataRow("Contoso.Guards", false, true)]
    [DataRow("Missing", false, true)]
    [DataRow("Contoso.Other", true, false)]
    public async Task Package_ThatDependsOnAProjectIsReportedForAnAssemblyOfNoPackage(string dependency, bool otherIsOfAPackage, bool reported)
    {
        var test = await CreateTestAsync(Assets([
            new("Helper", IsPackage: true, dependency, "Helper"),
            new("Contoso.Other", IsPackage: false, string.Empty, "Contoso.Other"),
            otherIsOfAPackage ? GuardsPackage : GuardsProject,
        ]));

        if (reported)
            test.ExpectedDiagnostics.Add(Verify.UncheckedReferenceAssembly("Helper", "Guards"));

        await test.RunAsync();
    }

    // The file says nothing of what the code of a project uses: it leaves out what the project references with private assets. So an
    // assembly that is of no package of the file is judged as with no file.
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task Project_IsReportedWhateverTheFileSays(bool isInTheFile)
    {
        var test = await CreateTestAsync(Assets([
            isInTheFile ? new("Helper", IsPackage: false, string.Empty, "Helper") : new("Unrelated", IsPackage: true, string.Empty, "Unrelated"),
            GuardsPackage,
        ]));

        test.ExpectedDiagnostics.Add(Verify.UncheckedReferenceAssembly("Helper", "Guards"));
        await test.RunAsync();
    }

    // The compilation does not say which target of the file is its own, so the package is reported where one target has the dependency.
    // A target that does not have the package says nothing.
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task Package_IsJudgedByEveryTargetOfTheFile(bool oneTargetHasTheDependency)
    {
        var test = await CreateTestAsync(Assets(
            [HelperPackage, NativePackage, GuardsPackage],
            [GuardsPackage],
            [oneTargetHasTheDependency ? new("Helper", IsPackage: true, "Guards", "Helper") : HelperPackage, GuardsPackage]));

        if (oneTargetHasTheDependency)
            test.ExpectedDiagnostics.Add(Verify.UncheckedReferenceAssembly("Helper", "Guards"));

        await test.RunAsync();
    }

    // A file that cannot be read says nothing.
    [TestMethod]
    [DataRow("")]
    [DataRow("not json")]
    [DataRow("[]")]
    [DataRow("{ \"targets\": 1 }")]
    [DataRow("{ \"targets\": { } }")]
    [DataRow("{ \"targets\": { \"net10.0\": { \"Helper/1.0.0\": { \"type\": \"package\", \"compile\": { \"ref/net10.0/Helper.dll\": {} ")]
    [DataRow("{ \"targets\": { \"net10.0\": { \"Helper/1.0.0\": { \"type\": \"package\", \"compile\": { \"ref/net10.0/Helper.dll\" } } } } }")]
    [DataRow("{ \"targets\": { \"net10.0\": { \"Helper/1.0.0\": { \"type\": \"package\", \"compile\": { \"ref\\")]
    public async Task File_ThatCannotBeReadSaysNothing(string assets)
    {
        var test = await CreateTestAsync(assets);
        test.ExpectedDiagnostics.Add(Verify.UncheckedReferenceAssembly("Helper", "Guards"));
        await test.RunAsync();
    }

    // A 'trusted' line for a package that gets no warning has no effect, and says so.
    [TestMethod]
    public async Task TrustedLine_ForAPackageThatIsNotReportedHasNoEffect()
    {
        var test = await CreateTestAsync(Assets([HelperPackage, NativePackage, GuardsPackage]), "A:Helper;trusted");
        test.ExpectedDiagnostics.Add(Verify.SafeListTrustedHasNoEffect(1, 1, 17, "Helper", NoEffect));
        await test.RunAsync();
    }

    private sealed record Library(string Id, bool IsPackage, string Dependencies, string? Assembly);
}
