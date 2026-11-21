using Microsoft.CodeAnalysis;
using PrefixClassName.MsTest;

namespace Singulink.CodeAnalysis.Tests;

/// <summary>
/// Tests of the finding for a referenced assembly that was not built with the analyzer and that references an assembly of checked code
/// whose members let other code get at a non-copyable value (SCA1025): a value of a built-in non-copyable type, which the framework
/// declares and every assembly references, or one that is bound to a marked type parameter.
/// </summary>
/// <remarks>
/// Docs: articles/diagnostics/sca1025.md and articles/rules/unchecked-code.md.
/// </remarks>
[PrefixTestClass]
public class ExposedValueTests
{
    // A library that was not built with the analyzer and that references the library of checked code: it names a type of it, since the
    // compiler leaves out a reference that nothing uses. What it does with the library is not read.
    private const string Plugin = """
        namespace Plugins
        {
            public static class Plugin
            {
                public static Anchor? Anything;
            }
        }
        """;

    private const string Usings = """
        using System.Collections.Generic;
        using System.Runtime.CompilerServices;
        using System.Runtime.InteropServices;
        using System.Threading;
        using Singulink.CodeAnalysis;


        """;

    // The type that the plugin names. It comes last, after an assembly attribute of the library.
    private const string Anchor = """

        public class Anchor { }
        """;

    private static Task<MetadataReference> CompileAsync(string source, bool analyzed, string name, params MetadataReference[] libraries) =>
        Verify.CompileLibraryAsync(Verify.Test.NetReferenceAssemblies, source, analyzed, name, libraries);

    private static async Task<Verify.Test> CreateTestAsync(string jobs, bool jobsAnalyzed = true, bool pluginAnalyzed = false, string? listFile = null)
    {
        var jobsLibrary = await CompileAsync(Usings + jobs + Anchor, jobsAnalyzed, "Jobs");
        var plugin = await CompileAsync(Plugin, pluginAnalyzed, "Plugin", jobsLibrary);

        var test = new Verify.Test { TestCode = "class C { }" };
        test.TestState.AdditionalReferences.Add(jobsLibrary);
        test.TestState.AdditionalReferences.Add(plugin);

        if (listFile is not null)
            test.TestState.AdditionalFiles.Add((Verify.ListFileName, listFile));

        return test;
    }

    // Each of these members lets code of another assembly get at a value that checked code holds: a field, a member that returns a
    // reference, a member that returns a container or another object that carries the value, a member that other code can implement and
    // that is given a reference or a container, and a base class or an interface that carries the value. An assembly that was not built
    // with the analyzer and that references the library is reported, with the member.
    [TestMethod]
    [DataRow("public class Job { public SpinLock Lock; }", "Job.Lock")]
    [DataRow("public class Job { public static GCHandle Shared; }", "Job.Shared")]
    [DataRow("public class Job { protected SpinLock Lock; }", "Job.Lock")]
    [DataRow("public class Job { public (SpinLock Lock, int Count) Pair; }", "Job.Pair")]
    [DataRow("public class Job { public SpinLock[] Locks = []; }", "Job.Locks")]
    [DataRow("public class Job { private SpinLock _lock; public ref SpinLock Lock => ref _lock; }", "Job.Lock")]
    [DataRow("public class Job { private SpinLock _lock; public ref SpinLock GetLock() => ref _lock; }", "Job.GetLock()")]
    [DataRow("public class Job { public List<GCHandle> Handles { get; } = new(); }", "Job.Handles")]
    [DataRow("public class Job { public List<SpinLock> GetLocks() => new(); }", "Job.GetLocks()")]
    [DataRow("public interface IWorker { void Run(ref SpinLock gate); }", "IWorker.Run(ref SpinLock)")]
    [DataRow("public interface IWorker { void Run(in SpinLock gate); }", "IWorker.Run(in SpinLock)")]
    [DataRow("public interface IWorker { void Fill(List<SpinLock> gates); }", "IWorker.Fill(List<SpinLock>)")]
    [DataRow("public abstract class Worker { public abstract void Run(ref SpinLock gate); }", "Worker.Run(ref SpinLock)")]
    [DataRow("public class Worker { protected virtual void Run(ref SpinLock gate) { } }", "Worker.Run(ref SpinLock)")]
    [DataRow("public delegate void Gate(ref SpinLock gate);", "Gate")]
    [DataRow("public class Locks : List<SpinLock> { }", "Locks")]
    [DataRow("public class Locks : IEnumerable<SpinLock> { public IEnumerator<SpinLock> GetEnumerator() => throw null!; System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => throw null!; }", "Locks")]
    [DataRow("public class Outer { public class Job { public SpinLock Lock; } }", "Outer.Job.Lock")]
    [DataRow("public class Job { public event System.Action<List<SpinLock>>? Changed; }", "Job.Changed")]
    [DataRow("public interface IJob { event System.EventHandler<List<SpinLock>> Changed; }", "IJob.Changed")]
    [DataRow("public interface IMap { int this[in SpinLock key] { get; } }", "IMap.this[in SpinLock]")]
    [DataRow("public abstract class Map { public abstract int this[List<SpinLock> keys] { get; } }", "Map.this[List<SpinLock>]")]
    [DataRow("public class Job { public void Each(System.Action<List<SpinLock>> callback) { } }", "Job.Each(Action<List<SpinLock>>)")]
    [DataRow("public class Job { public void Each(System.Func<List<SpinLock>, int> callback) { } }", "Job.Each(Func<List<SpinLock>, int>)")]
    [DataRow("public class Job { public void Fill(System.Buffers.SpanAction<SpinLock, int> callback) { } }", "Job.Fill(SpanAction<SpinLock, int>)")]
    [DataRow("public class Job { public Job(System.Action<SpinLock[]> callback) { } }", "Job.Job(Action<SpinLock[]>)")]
    [DataRow("public class Job { public void Sort(IComparer<List<SpinLock>> comparer) { } }", "Job.Sort(IComparer<List<SpinLock>>)")]
    [DataRow("public delegate void Gate(List<GCHandle> handles);", "Gate")]
    [DataRow("public unsafe class Job { public GCHandle* Handle; }", "Job.Handle")]
    [DataRow("public unsafe class Job { public delegate*<ref SpinLock, void> Callback; }", "Job.Callback")]
    [DataRow("public unsafe class Job { public void Each(delegate*<List<SpinLock>, void> callback) { } }", "Job.Each(delegate*<List<SpinLock>, void>)")]
    [DataRow("public class Box<[NonCopyable] T> { public T Value = default!; }", "Box<T>.Value")]
    [DataRow("public interface IUser { void Use<[NonCopyable] T>(ref T value); }", "IUser.Use<T>(ref T)")]
    public async Task UncheckedAssembly_ThatReferencesAnAssemblyThatLetsAValueOutIsReported(string jobs, string member)
    {
        var test = await CreateTestAsync(jobs);
        test.ExpectedDiagnostics.Add(Verify.UncheckedAssemblyOfExposedValue("Plugin", "Jobs", member));
        await test.RunAsync();
    }

    // None of these lets a value out. A value that a member returns by value is a new one, and a value that it takes by value is its own
    // from then on. A member that no other assembly can reach does not count, nor does a parameter of a member that other code can only
    // call. A class that holds a value and hands out nothing of it does not count either.
    [TestMethod]
    [DataRow("public class Job { private SpinLock _lock; public void Enter(ref bool taken) => _lock.Enter(ref taken); }")]
    [DataRow("public class Job { public SpinLock Make() => default; public GCHandle Handle => default; }")]
    [DataRow("public class Job { public void Take(SpinLock byValue) { } public static void Free(ref GCHandle handle) => handle.Free(); }")]
    [DataRow("public interface IWorker { void Take(SpinLock byValue); SpinLock Make(); }")]
    [DataRow("public sealed class Worker { public void Run(ref SpinLock gate) { } }")]
    [DataRow("class Job { internal SpinLock Lock; } public class Open { internal SpinLock Lock; private SpinLock _own; }")]
    [DataRow("public class Job { public int Count; public string Name = \"\"; public List<int> Numbers { get; } = new(); }")]
    [DataRow("public class Box<T> { public T Value = default!; }")]
    [DataRow("public class Job { public System.Action<SpinLock>? Take; public System.Func<SpinLock>? Make; public System.Action<GCHandle>? Free; }")]
    [DataRow("public delegate void Take(SpinLock byValue); public delegate SpinLock Make();")]
    [DataRow("public class Job { public void Each(System.Action<SpinLock> callback) { } public event System.Action<GCHandle>? Freed; }")]
    [DataRow("#pragma warning disable CS0628\npublic sealed class Job { protected SpinLock Lock; protected class Inner { public SpinLock Lock; } }")]
    [DataRow("public static class Jobs { public static void Enter(ref SpinLock gate, ref bool taken) => gate.Enter(ref taken); }")]
    public async Task UncheckedAssembly_ThatReferencesAnAssemblyThatLetsNoValueOutIsNotReported(string jobs)
    {
        var test = await CreateTestAsync(jobs);
        await test.RunAsync();
    }

    // The internal members of a referenced assembly are not imported, so what they let out is not seen. An assembly that the library
    // names in an InternalsVisibleTo attribute can use them, and gets the warning whatever the library holds: by its simple name in any
    // case, with a public key after it or not. An attribute that names another assembly opens nothing to this one.
    [TestMethod]
    [DataRow("Plugin", true)]
    [DataRow("plugin", true)]
    [DataRow("Plugin, PublicKey=*", true)]
    [DataRow("Friend", false)]
    [DataRow("Plugin.Tests", false)]
    public async Task UncheckedAssembly_ThatIsAFriendOfCheckedCodeIsReported(string friend, bool reported)
    {
        // The compiler leaves out an attribute whose public key is not one, so the row with a key takes a real one: the core library's.
        friend = friend.Replace("*", Convert.ToHexString(typeof(object).Assembly.GetName().GetPublicKey()!), StringComparison.Ordinal);

        var test = await CreateTestAsync($"[assembly: InternalsVisibleTo(\"{friend}\")] class Job {{ internal SpinLock Lock; }}");

        if (reported)
            test.ExpectedDiagnostics.Add(Verify.UncheckedFriendAssembly("Plugin", "Jobs"));

        await test.RunAsync();
    }

    // The library that lets the value out has to be checked code: the values of code that was not built with the analyzer are that code's
    // own business, as its types are. And an assembly that was built with the analyzer is checked code itself.
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, true)]
    public async Task UncheckedAssembly_IsReportedOnlyForCheckedCodeAndOnlyWhenItIsUnchecked(bool jobsAnalyzed, bool pluginAnalyzed)
    {
        var test = await CreateTestAsync("public class Job { public SpinLock Lock; }", jobsAnalyzed, pluginAnalyzed);
        await test.RunAsync();
    }

    // A 'trusted' line vouches for the assembly, and has an effect there. Where the library lets no value out, the line has none.
    [TestMethod]
    [DataRow("public class Job { public SpinLock Lock; }", true)]
    [DataRow("public class Job { public int Count; }", false)]
    public async Task TrustedLine_VouchesForTheAssembly(string jobs, bool hasEffect)
    {
        var test = await CreateTestAsync(jobs, listFile: "A:Plugin;trusted");

        if (!hasEffect)
        {
            test.ExpectedDiagnostics.Add(Verify.SafeListTrustedHasNoEffect(
                1,
                1,
                17,
                "Plugin",
                "the assembly gets no warning for the line to answer: no non-copyable value of other code is within its reach, and no field of the compilation behind a class of the assembly holds one"));
        }

        await test.RunAsync();
    }

    // An assembly that was built against a library before a type of it moved references the old assembly, which now forwards the type.
    // It is judged by the assembly that the type moved to, and the message names that one.
    [TestMethod]
    public async Task UncheckedAssembly_ThatReferencesAnAssemblyThatForwardsToOneThatLetsAValueOutIsReported()
    {
        var before = await CompileAsync("public class Anchor { }", analyzed: true, "Jobs");
        var plugin = await CompileAsync(Plugin, analyzed: false, "Plugin", before);
        var moved = await CompileAsync(Usings + "public class Anchor { } public class Job { public SpinLock Lock; }", analyzed: true, "Moved");
        var forwarder = await CompileAsync("[assembly: System.Runtime.CompilerServices.TypeForwardedTo(typeof(Anchor))]", analyzed: true, "Jobs", moved);

        var test = new Verify.Test { TestCode = "class C { }" };
        test.TestState.AdditionalReferences.Add(forwarder);
        test.TestState.AdditionalReferences.Add(moved);
        test.TestState.AdditionalReferences.Add(plugin);

        test.ExpectedDiagnostics.Add(Verify.UncheckedAssemblyOfExposedValue("Plugin", "Moved", "Job.Lock"));
        await test.RunAsync();
    }

    // The member that the message names is the first by its display, so that every build names the same one.
    [TestMethod]
    public async Task Message_NamesTheFirstMemberByItsDisplay()
    {
        var test = await CreateTestAsync("public class Zed { public SpinLock Lock; } public class Able { public GCHandle Handle; public SpinLock Lock; }");
        test.ExpectedDiagnostics.Add(Verify.UncheckedAssemblyOfExposedValue("Plugin", "Jobs", "Able.Handle"));
        await test.RunAsync();
    }
}
