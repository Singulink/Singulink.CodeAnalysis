using Microsoft.CodeAnalysis;
using PrefixClassName.MsTest;

namespace Singulink.CodeAnalysis.Tests;

/// <summary>
/// Tests of the finding for a referenced assembly that was not built with the analyzer and of which the build is given only a reference
/// assembly (SCA1025). A reference assembly has no method bodies and names only the assemblies that its reachable members use, so what
/// its code uses is not known. A build is given one for a project of the same solution. Such an assembly is reported where a
/// non-copyable value is within its reach.
/// </summary>
/// <remarks>
/// Docs: articles/diagnostics/sca1025.md and articles/rules/unchecked-code.md.
/// </remarks>
[PrefixTestClass]
public class ReferenceAssemblyTests
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

            public static class Shared
            {
                public static Guard Current;
            }
        }
        """;

    // A library that uses the guard only inside a method, and names nothing of the other library in a member that can be reached.
    private const string Helper = """
        namespace Helpers
        {
            public static class Helper
            {
                public static void Run()
                {
                    var copy = Guards.Shared.Current;
                    copy.Id++;
                }
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

    private static Task<MetadataReference> CompileAsync(string source, bool analyzed, bool asReferenceAssembly, string name, params MetadataReference[] libraries) =>
        Verify.CompileLibraryAsync(Verify.Test.NetReferenceAssemblies, source, analyzed, asReferenceAssembly, name, libraries);

    private static Verify.Test CreateTest(string? listFile, params MetadataReference[] libraries)
    {
        var test = new Verify.Test { TestCode = "class C { }" };
        test.TestState.AdditionalReferences.AddRange(libraries);

        if (listFile is not null)
            test.TestState.AdditionalFiles.Add((Verify.ListFileName, listFile));

        return test;
    }

    // The reference assembly of a library that uses a guard only inside a method does not reference the library of the guard, so the
    // check of what an assembly references finds nothing. The assembly is reported because its code is not known and a non-copyable type
    // is within its reach. The same library as its full assembly does reference the library of the guard, and gets the warning for that.
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task UncheckedAssembly_OfWhichOnlyAReferenceAssemblyIsGivenIsReported(bool asReferenceAssembly)
    {
        var guards = await CompileAsync(Guards, analyzed: true, asReferenceAssembly: false, "Guards");
        var helper = await CompileAsync(Helper, analyzed: false, asReferenceAssembly, "Helper", guards);
        var test = CreateTest(null, guards, helper);

        test.ExpectedDiagnostics.Add(asReferenceAssembly
            ? Verify.UncheckedReferenceAssembly("Helper", "Guards")
            : Verify.UncheckedAssembly("Helper", "Guards", "Guards.Guard"));

        await test.RunAsync();
    }

    // A library that lets a value of a built-in non-copyable type out is within reach as one that declares a non-copyable type is.
    [TestMethod]
    public async Task UncheckedAssembly_IsReportedWhereCheckedCodeLetsAValueOut()
    {
        var jobs = await CompileAsync("public class Job { public System.Threading.SpinLock Lock; }", analyzed: true, asReferenceAssembly: false, "Jobs");
        var helper = await CompileAsync(Plain, analyzed: false, asReferenceAssembly: true, "Helper");
        var test = CreateTest(null, jobs, helper);

        test.ExpectedDiagnostics.Add(Verify.UncheckedReferenceAssembly("Helper", "Jobs"));
        await test.RunAsync();
    }

    // Nothing is reported where no non-copyable value is within reach: where no other assembly declares a non-copyable type or lets a
    // value out, where the assembly was built with the analyzer, and where the full assembly is given and references nothing of note.
    [TestMethod]
    [DataRow(false, true, false)]
    [DataRow(true, true, true)]
    [DataRow(true, false, false)]
    public async Task UncheckedAssembly_IsNotReportedWhereNothingIsWithinReachOrItsCodeIsKnown(bool withGuards, bool asReferenceAssembly, bool helperAnalyzed)
    {
        var libraries = new List<MetadataReference>();

        if (withGuards)
            libraries.Add(await CompileAsync(Guards, analyzed: true, asReferenceAssembly: false, "Guards"));
        else
            libraries.Add(await CompileAsync("public class Job { public int Count; }", analyzed: true, asReferenceAssembly: false, "Jobs"));

        libraries.Add(await CompileAsync(Plain, helperAnalyzed, asReferenceAssembly, "Helper"));

        var test = CreateTest(null, [.. libraries]);
        await test.RunAsync();
    }

    // An assembly cannot reference an assembly that references it, so the library of the guard is not within the reach of a library
    // that it is built on.
    [TestMethod]
    public async Task UncheckedAssembly_IsNotReportedForAnAssemblyThatTheOtherOneReferences()
    {
        var helper = await CompileAsync(Plain, analyzed: false, asReferenceAssembly: true, "Helper");
        var guards = await CompileAsync(
            Guards + "\nnamespace Guards { public static class Uses { public static int Value = Helpers.Helper.Run(); } }",
            analyzed: true,
            asReferenceAssembly: false,
            "Guards",
            helper);

        var test = CreateTest(null, guards, helper);
        await test.RunAsync();
    }

    // The compilation's own types are not within the reach of an assembly that the compilation references.
    [TestMethod]
    public async Task UncheckedAssembly_IsNotReportedForTheTypesOfTheCompilation()
    {
        var helper = await CompileAsync(Plain, analyzed: false, asReferenceAssembly: true, "Helper");

        var test = new Verify.Test {
            TestCode = """
                using Singulink.CodeAnalysis;

                [NonCopyable]
                public struct Guard { }

                public class Job { public System.Threading.SpinLock Lock; }
                """,
        };

        test.TestState.AdditionalReferences.Add(helper);
        await test.RunAsync();
    }

    // A 'trusted' line vouches for the assembly, and has an effect there. The message names the first assembly within reach by its
    // name, so that every build names the same one.
    [TestMethod]
    [DataRow(null)]
    [DataRow("A:Helper;trusted")]
    public async Task TrustedLine_VouchesForTheAssembly(string? listFile)
    {
        var guards = await CompileAsync(Guards, analyzed: true, asReferenceAssembly: false, "Guards");
        var jobs = await CompileAsync("public class Job { public System.Threading.SpinLock Lock; }", analyzed: true, asReferenceAssembly: false, "Able");
        var helper = await CompileAsync(Plain, analyzed: false, asReferenceAssembly: true, "Helper");
        var test = CreateTest(listFile, guards, jobs, helper);

        if (listFile is null)
            test.ExpectedDiagnostics.Add(Verify.UncheckedReferenceAssembly("Helper", "Able"));

        await test.RunAsync();
    }
}
