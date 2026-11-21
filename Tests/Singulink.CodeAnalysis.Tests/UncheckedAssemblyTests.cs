using Microsoft.CodeAnalysis;
using PrefixClassName.MsTest;

namespace Singulink.CodeAnalysis.Tests;

/// <summary>
/// Tests of the finding for a referenced assembly that was not built with the analyzer and that references an assembly which declares a
/// non-copyable type (SCA1025), and of the 'trusted' line of a list file that vouches for one.
/// </summary>
/// <remarks>
/// Docs: articles/diagnostics/sca1025.md, articles/concepts/unchecked-code.md and articles/guides/symbol-list.md.
/// </remarks>
[PrefixTestClass]
public class UncheckedAssemblyTests
{
    // A checked library with a guard, and a struct that is no guard.
    private const string Guards = """
        using Singulink.CodeAnalysis;

        namespace Guards
        {
            [NonCopyable]
            public struct Guard
            {
                public int Id;
            }

            public struct Ticket
            {
                public int Id;
            }
        }
        """;

    // A library that uses the guard in several ways.
    private const string Plugin = """
        using System.Collections.Generic;
        using Guards;

        namespace Plugins
        {
            public static class Plugin
            {
                public static void Use(ref Guard guard) { }

                public static Guard Make() => default;

                public static void Fill(List<Guard> guards) { }
            }
        }
        """;

    // A library that uses the built-in non-copyable types of the framework, and no type of a library.
    private const string Framework = """
        using System.Runtime.InteropServices;
        using System.Threading;

        namespace Clean
        {
            public static class Handles
            {
                public static void Free(ref GCHandle handle) => handle.Free();

                public static void Enter(ref SpinLock spinLock, ref bool taken) => spinLock.Enter(ref taken);
            }
        }
        """;

    private const string Uses = """
        using Plugins;

        class C
        {
            Guards.Guard First() => Plugin.Make();

            Guards.Guard Second() => Plugin.Make();
        }
        """;

    private static Task<MetadataReference> CompileAsync(string source, bool analyzed, string name, params MetadataReference[] libraries) =>
        Verify.CompileLibraryAsync(Verify.Test.NetReferenceAssemblies, source, analyzed, name, libraries);

    private static Verify.Test CreateTest(string? listFile, params MetadataReference[] libraries) => CreateTestOf(Uses, listFile, libraries);

    private static Verify.Test CreateTestOf(string code, string? listFile, params MetadataReference[] libraries)
    {
        var test = new Verify.Test { TestCode = code };
        test.TestState.AdditionalReferences.AddRange(libraries);

        if (listFile is not null)
            test.TestState.AdditionalFiles.Add((Verify.ListFileName, listFile));

        return test;
    }

    // An unchecked assembly that references one that declares a non-copyable type is reported once, however many members of it use the
    // type. The same assembly with the marker is checked code, and one that uses only the framework is clean.
    [TestMethod]
    public async Task UncheckedAssembly_ThatReferencesADeclaringAssemblyIsReportedOnce()
    {
        var guards = await CompileAsync(Guards, analyzed: true, "Guards");
        var test = CreateTest(
            null,
            guards,
            await CompileAsync(Plugin, analyzed: false, "Plugin", guards),
            await CompileAsync(Plugin.Replace("namespace Plugins", "namespace MarkedPlugins", StringComparison.Ordinal), analyzed: true, "MarkedPlugin", guards),
            await CompileAsync(Framework, analyzed: false, "Clean"));

        test.ExpectedDiagnostics.Add(Verify.UncheckedAssembly("Plugin", "Guards", "Guards.Guard"));
        await test.RunAsync();
    }

    // The declaring assembly can itself be unchecked code: a mark on the struct counts wherever it is.
    [TestMethod]
    public async Task UncheckedAssembly_ThatReferencesAnUncheckedDeclaringAssemblyIsReported()
    {
        var guards = await CompileAsync(Guards, analyzed: false, "Guards");
        var test = CreateTest(null, guards, await CompileAsync(Plugin, analyzed: false, "Plugin", guards));

        test.ExpectedDiagnostics.Add(Verify.UncheckedAssembly("Plugin", "Guards", "Guards.Guard"));
        await test.RunAsync();
    }

    // A library that marks its guard with an attribute of its own, of another namespace, declares a non-copyable type all the same: such
    // an attribute counts on a struct of another assembly, and the assemblies that declare one are the ones whose types are read.
    [TestMethod]
    public async Task UncheckedAssembly_ThatReferencesALibraryWithAnAttributeOfItsOwnIsReported()
    {
        var locks = await CompileAsync(
            """
            namespace Locks
            {
                [System.AttributeUsage(System.AttributeTargets.Struct)]
                internal sealed class NonCopyableAttribute : System.Attribute { }

                [NonCopyable]
                public struct Lock
                {
                    public int Id;
                }
            }
            """,
            analyzed: false,
            "Locks");

        var user = await CompileAsync("namespace Users { public static class Holder { public static void Hold(ref Locks.Lock held) { } } }", analyzed: false, "Users", locks);
        var test = CreateTestOf("class C { }", null, locks, user);
        test.ExpectedDiagnostics.Add(Verify.UncheckedAssembly("Users", "Locks", "Locks.Lock"));
        await test.RunAsync();
    }

    // A 'noncopyable' line makes a struct of a checked assembly count; a struct of unchecked code is that code's own business. A
    // 'copyable' line takes the only marked type out.
    [TestMethod]
    [DataRow("T:Third.Ticket;noncopyable", true, true)]
    [DataRow("T:Third.Ticket;noncopyable", false, false)]
    [DataRow("T:Third.Ticket;copyable", true, false)]
    public async Task UncheckedAssembly_GoesByTheLinesOfTheList(string line, bool thirdAnalyzed, bool isReported)
    {
        // The third assembly has one struct, which only the list line can make non-copyable, or which the copyable line takes out of
        // the marked ones.
        string third = line.EndsWith(";copyable", StringComparison.Ordinal)
            ? "namespace Third { [Singulink.CodeAnalysis.NonCopyable] public struct Ticket { public int Id; } }"
            : "namespace Third { public struct Ticket { public int Id; } }";

        var thirdReference = await CompileAsync(third, thirdAnalyzed, "Third");
        var tickets = await CompileAsync("namespace Tickets { public static class Desk { public static void Stamp(ref Third.Ticket ticket) { } } }", analyzed: false, "Tickets", thirdReference);
        var test = CreateTestOf("class C { }", line, thirdReference, tickets);

        if (isReported)
            test.ExpectedDiagnostics.Add(Verify.UncheckedAssembly("Tickets", "Third", "Third.Ticket"));

        await test.RunAsync();
    }

    // A reference to an assembly that forwards the type to the one that declares it counts as well: the code was compiled where the type
    // was declared in the forwarding assembly.
    [TestMethod]
    public async Task UncheckedAssembly_ThroughAnAssemblyThatForwardsTheTypeIsReported()
    {
        var before = await CompileAsync(Guards, analyzed: true, "Facade");
        var plugin = await CompileAsync(Plugin, analyzed: false, "Plugin", before);
        var guards = await CompileAsync(Guards, analyzed: true, "Guards");
        var facade = await CompileAsync(
            """
            [assembly: System.Runtime.CompilerServices.TypeForwardedTo(typeof(Guards.Guard))]
            [assembly: System.Runtime.CompilerServices.TypeForwardedTo(typeof(Guards.Ticket))]
            """,
            analyzed: true,
            "Facade",
            guards);

        var test = CreateTestOf("class C { }", null, guards, facade, plugin);
        test.ExpectedDiagnostics.Add(Verify.UncheckedAssembly("Plugin", "Facade", "Guards.Guard"));
        await test.RunAsync();
    }

    // A 'trusted' line turns the finding off for that assembly and changes nothing else: a member of it that gets at a guard by
    // reference is still reported.
    [TestMethod]
    public async Task TrustedLine_TurnsOffTheFindingForTheAssemblyAlone()
    {
        var guards = await CompileAsync(Guards, analyzed: true, "Guards");
        const string Code = """
            using Plugins;

            class C
            {
                private Guards.Guard _guard;

                void M() => {|#0:Plugin.Use(ref _guard)|};
            }
            """;

        var test = CreateTestOf(Code, "A:Plugin;trusted", guards, await CompileAsync(Plugin, analyzed: false, "Plugin", guards));

        test.ExpectedDiagnostics.Add(Verify.UncheckedMember(0, "Plugin.Use(ref Guard)", "guard", "Guards.Guard", "M:Plugins.Plugin.Use(Guards.Guard@)"));
        await test.RunAsync();
    }

    // A 'trusted' line that has no effect is reported: for an assembly that is not referenced, that was built with the analyzer, that
    // references no assembly which declares a non-copyable type, or that is the compilation's own.
    [TestMethod]
    public async Task TrustedLine_ThatHasNoEffectIsReported()
    {
        var guards = await CompileAsync(Guards, analyzed: true, "Guards");
        var test = CreateTest(
            """
            A:Missing;trusted
            A:MarkedPlugin;trusted
            A:Clean;trusted
            A:TestProject;trusted
            A:plugin;trusted
            """,
            guards,
            await CompileAsync(Plugin, analyzed: false, "Plugin", guards),
            await CompileAsync(Plugin.Replace("namespace Plugins", "namespace MarkedPlugins", StringComparison.Ordinal), analyzed: true, "MarkedPlugin", guards),
            await CompileAsync(Framework, analyzed: false, "Clean"));

        // The name is matched in any case, as the runtime binds it, so the last line vouches for Plugin.
        test.ExpectedDiagnostics.AddRange([
            Verify.SafeListTrustedHasNoEffect(1, 1, 18, "Missing", "the compilation references no assembly of that name"),
            Verify.SafeListTrustedHasNoEffect(2, 1, 23, "MarkedPlugin", "the assembly was built with the analyzer, so it is checked code"),
            Verify.SafeListTrustedHasNoEffect(
                3,
                1,
                16,
                "Clean",
                "the assembly references no assembly that declares a non-copyable type or that lets a non-copyable value out, and the line answers no warning for a field behind a class of the assembly"),
            Verify.SafeListTrustedHasNoEffect(4, 1, 22, "TestProject", "it is the assembly of the compilation, which is checked code"),
        ]);

        await test.RunAsync();
    }

    // The line of an assembly takes its simple name and the one marker 'trusted', which no other line takes.
    [TestMethod]
    public async Task TrustedLine_ThatCannotBeParsedIsReported()
    {
        var test = CreateTestOf(
            "class C { }",
            """
            A:Plugin
            A:Plugin;new
            A:Plugin, Version=1.0.0.0;trusted
            A:Plugin.dll;trusted
            A:Plugin;trusted;new
            T:Guards.Guard;trusted
            M:Plugins.Plugin.Make;trusted
            """);

        test.ExpectedDiagnostics.AddRange([
            Verify.SafeListLine(1, 1, 9, "an assembly needs the marker 'trusted'"),
            Verify.SafeListLine(2, 1, 13, "the marker 'new' is not for an assembly, which takes 'trusted'"),
            Verify.SafeListLine(3, 1, 34, "an assembly is named by its simple name, as in 'A:Contoso.Plugins;trusted'"),
            Verify.SafeListLine(4, 1, 21, "an assembly is named by its simple name, as in 'A:Contoso.Plugins;trusted'"),
            Verify.SafeListLine(5, 1, 21, "an assembly takes the one marker 'trusted' and nothing after it"),
            Verify.SafeListLine(6, 1, 23, "the marker 'trusted' is for an assembly, named after 'A:'"),
            Verify.SafeListLine(7, 1, 30, "the marker 'trusted' is for an assembly, named after 'A:'"),
        ]);

        await test.RunAsync();
    }
}
