using Microsoft.CodeAnalysis.Testing;

namespace Singulink.CodeAnalysis.Tests;

/// <summary>
/// A value converted to a type that hides it, as the argument of a member on the symbol list: a line with no marker says how the member
/// treats the values that it holds, not that it forgets a reference that it is given, so only a member that the list marks 'borrows' lets
/// the value through (QA9 round 3, 1.1 and 1.7).
/// </summary>
/// <remarks>
/// Docs: articles/rules/symbol-list-format.md, "Borrows", and articles/diagnostics/sca1009.md.
/// </remarks>
[TestClass]
public class ReceivingMemberTests
{
    private const string Culture = "System.Globalization.CultureInfo";
    private const string Tagged = "Lib.Tagged<Guard>";

    private const string Library = """
        using System.Globalization;
        using Singulink.CodeAnalysis;

        namespace Lib
        {
            public class Tagged<[NonCopyable] T> : CultureInfo
            {
                public T Tag = default!;

                public Tagged() : base("en-US") { }
            }

            public static class Factory
            {
                public static Tagged<T> Make<[NonCopyable] T>() => new();
            }

            public static class Registry
            {
                public static void Note(object value) { }
            }
        }
        """;

    // Every listed member that keeps what it is given, or hands it on, gets the warning that the conversion gets anywhere else: SCA1009
    // for an object with a binding to a class that clones itself, SCA1023 for a container.
    [TestMethod]
    public async Task ListedMember_ThatKeepsWhatItIsGivenGetsTheWarningOfTheConversion()
    {
        var test = await CreateAsync("""
            class C
            {
                void Routes(Channel<CultureInfo> channel)
                {
                    var tagged = Factory.Make<Guard>();
                    new List<CultureInfo>().Add({|#0:tagged|});
                    new Dictionary<int, object>().Add(1, {|#1:tagged|});
                    new Queue<CultureInfo>().Enqueue({|#2:tagged|});
                    _ = Task.FromResult<CultureInfo>({|#3:tagged|});
                    new TaskCompletionSource<CultureInfo>().SetResult({|#4:tagged|});
                    channel.Writer.TryWrite({|#5:tagged|});
                    CultureInfo slot = CultureInfo.InvariantCulture;
                    Interlocked.Exchange(ref slot, {|#6:tagged|});
                    new ConcurrentDictionary<int, CultureInfo>().TryAdd(1, {|#7:tagged|});
                    _ = new Task<object>(state => state!, {|#8:tagged|});
                    _ = new TaskCompletionSource<int>({|#9:tagged|});
                    _ = new List<object> { {|#10:tagged|} };
                    _ = new KeyValuePair<int, object>(1, {|#11:tagged|});

                    // A key lookup gives the key to the dictionary's comparer, code that the application can supply.
                    _ = new Dictionary<object, int>().ContainsKey({|#14:tagged|});
                    _ = new Dictionary<CultureInfo, int>().TryGetValue({|#15:tagged|}, out _);

                    var list = new List<Guard>();
                    new List<object>().Add({|#12:list|});
                    _ = new List<object> { {|#13:list|} };
                }
            }
            """);

        for (int location = 0; location <= 11; location++)
        {
            string target = location is 1 or 8 or 9 or 10 or 11 ? "object" : Culture;
            test.ExpectedDiagnostics.Add(Verify.CloningUseConversion(location, Tagged, target, Culture, "Guard", Tagged));
        }

        test.ExpectedDiagnostics.Add(Verify.CloningUseConversion(14, Tagged, "object", Culture, "Guard", Tagged));
        test.ExpectedDiagnostics.Add(Verify.CloningUseConversion(15, Tagged, Culture, Culture, "Guard", Tagged));
        test.ExpectedDiagnostics.Add(Verify.OpaqueConversion(12, "System.Collections.Generic.List<Guard>", "object"));
        test.ExpectedDiagnostics.Add(Verify.OpaqueConversion(13, "System.Collections.Generic.List<Guard>", "object"));
        await test.RunAsync();
    }

    // A member that the list marks 'borrows' uses what it is given during the call and keeps nothing of it: it lets the value through,
    // 'lock' with it.
    [TestMethod]
    public async Task BorrowingMember_LetsTheValueThrough()
    {
        var test = await CreateAsync("""
            class C
            {
                bool Routes()
                {
                    var tagged = Factory.Make<Guard>();
                    var list = new List<Guard>();
                    lock (tagged) { }
                    lock (list) { }
                    GC.KeepAlive(tagged);
                    ArgumentNullException.ThrowIfNull(list);
                    return ReferenceEquals(tagged, list);
                }
            }
            """);

        await test.RunAsync();
    }

    // A line of a symbol list file lets the value through only with 'borrows'; a line with no marker keeps the warning.
    [TestMethod]
    [DataRow("M:Lib.Registry.Note(System.Object)", true)]
    [DataRow("M:Lib.Registry.Note(System.Object);borrows", false)]
    public async Task LineOfAFile_LetsTheValueThroughOnlyWithBorrows(string line, bool warns)
    {
        var test = await CreateAsync("""
            class C
            {
                void Routes() => Registry.Note({|#0:Factory.Make<Guard>()|});
            }
            """);

        test.TestState.AdditionalFiles.Add((Verify.ListFileName, line));

        if (warns)
            test.ExpectedDiagnostics.Add(Verify.CloningUseConversion(0, Tagged, "object", Culture, "Guard", Tagged));

        await test.RunAsync();
    }

    private static async Task<Verify.Test> CreateAsync(string source)
    {
        var library = await Verify.CompileLibraryAsync(Verify.Test.NetReferenceAssemblies, Library, analyzed: true, "Lib");
        var test = new Verify.Test {
            TestCode = """
                using System;
                using System.Collections.Concurrent;
                using System.Collections.Generic;
                using System.Globalization;
                using System.Threading;
                using System.Threading.Channels;
                using System.Threading.Tasks;
                using Lib;

                """ + source + Environment.NewLine + Sources.Guard,
        };

        test.TestState.AdditionalReferences.Add(library);
        return test;
    }
}
