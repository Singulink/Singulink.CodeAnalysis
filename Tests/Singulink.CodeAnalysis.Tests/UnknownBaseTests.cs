using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using PrefixClassName.MsTest;

namespace Singulink.CodeAnalysis.Tests;

/// <summary>
/// Tests of a class that derives from a class of which nothing says whether its own code clones an object with MemberwiseClone: a class
/// of an assembly that was not built with the analyzer and that is no part of the framework. Every field of the class that holds a
/// non-copyable value gets SCA1009 until a symbol list line says what the base class does: 'clones', 'clones=none', or 'trusted' for
/// its assembly.
/// </summary>
/// <remarks>
/// Docs: articles/diagnostics/sca1009.md, articles/rules/symbol-list-format.md and articles/rules/not-covered.md.
/// </remarks>
[PrefixTestClass]
public class UnknownBaseTests
{
    private const string Handle = "System.Runtime.InteropServices.GCHandle";

    // A library whose Document clones itself, and whose Page does not. Whether it was built with the analyzer is up to the test.
    private const string DocsLibrary = """
        namespace Docs
        {
            public class Document { public Document Copy() => (Document)MemberwiseClone(); }

            public class Page { public int Number; }

            public class Chapter : Page { }
        }
        """;

    private const string Fields = """
        using System.Collections.Generic;
        using System.Runtime.InteropServices;
        using Singulink.CodeAnalysis;

        [NonCopyable]
        struct Guard { }

        class Pinned : Docs.Page
        {
            private GCHandle {|#0:_pinned|};
            private Guard {|#1:_guard|};
            private (GCHandle Handle, int Count) {|#2:_tuple|};
            private static GCHandle s_shared;
            private readonly List<GCHandle> _handles = new();
            private int _count;
        }

        class Plain : Docs.Page
        {
            private int _count;
        }
        """;

    private static Task<MetadataReference> CompileDocsAsync(bool analyzed, string assemblyName = "Docs", string? source = null, params MetadataReference[] libraries) =>
        Verify.CompileLibraryAsync(Verify.Test.NetReferenceAssemblies, source ?? DocsLibrary, analyzed, assemblyName, libraries);

    // A class of a library that was not built with the analyzer may clone itself, and nothing says so. Each field of a class that derives
    // from it and holds a non-copyable value gets the warning: a field of a non-copyable type, a tuple that holds one, and not a static
    // field or a container, which a clone shares. A class that holds no non-copyable value gets none.
    [TestMethod]
    public async Task Field_BehindABaseClassOfAnUnanalyzedLibraryGetsAWarning()
    {
        var test = new Verify.Test { TestCode = Fields };
        test.TestState.AdditionalReferences.Add(await CompileDocsAsync(analyzed: false));

        test.ExpectedDiagnostics.AddRange([
            Verify.FieldBehindUnknownBase(0, "Pinned", "Docs.Page", "Docs", Handle, "_pinned"),
            Verify.FieldBehindUnknownBase(1, "Pinned", "Docs.Page", "Docs", "Guard", "_guard"),
            Verify.FieldBehindUnknownBase(2, "Pinned", "Docs.Page", "Docs", $"({Handle} Handle, int Count)", "_tuple"),
        ]);

        await test.RunAsync();
    }

    // A library that was built with the analyzer is checked in its own build, where a call of MemberwiseClone gets its warning, so its
    // classes are known. So are the classes of the framework, of which the built-in list says which ones clone themselves.
    [TestMethod]
    public async Task Field_BehindABaseClassOfAnAnalyzedLibraryOrOfTheFrameworkGetsNoWarning()
    {
        var test = new Verify.Test {
            TestCode = """
                using System.IO;
                using System.Runtime.InteropServices;

                class Pinned : Docs.Page
                {
                    private GCHandle _pinned;
                }

                class PinnedStream : MemoryStream
                {
                    private GCHandle _pinned;
                }

                class PinnedException : System.Exception
                {
                    private GCHandle _pinned;
                }
                """,
        };

        test.TestState.AdditionalReferences.Add(await CompileDocsAsync(analyzed: true));
        await test.RunAsync();
    }

    // Each of the three lines answers the warning. 'clones' says that the base class clones itself, and the field gets the warning of a
    // class that clones itself. 'clones=none' says that neither the class nor a class above it does, and 'trusted' that no code of the
    // assembly copies a non-copyable value: the field gets no warning, from a project's file or from one that came in a package. A
    // 'trusted' line for such an assembly has an effect, so it gets no warning itself.
    [TestMethod]
    [DataRow("NonCopyableSymbols.txt", "T:Docs.Page;clones", true)]
    [DataRow("NonCopyableSymbols.txt", "T:Docs.Page;clones=none", false)]
    [DataRow("NonCopyableSymbols.txt", "T:Docs.Page;clones = none", false)]
    [DataRow("NonCopyableSymbols.txt", "A:Docs;trusted", false)]
    [DataRow("NonCopyableSymbols.txt", "A:docs;trusted", false)]
    [DataRow("packages/docs/1.0.0/build/NonCopyableSymbols.Docs.txt", "T:Docs.Page;clones=none", false)]
    [DataRow("packages/docs/1.0.0/build/NonCopyableSymbols.Docs.txt", "A:Docs;trusted", false)]
    public async Task Line_AnswersTheWarning(string file, string line, bool clones)
    {
        var test = new Verify.Test {
            TestCode = """
                using System.Runtime.InteropServices;

                class Pinned : Docs.Page
                {
                    private GCHandle {|#0:_pinned|};
                }
                """,
        };

        test.TestState.AdditionalReferences.Add(await CompileDocsAsync(analyzed: false));
        test.TestState.AdditionalFiles.Add((file, line));

        if (clones)
            test.ExpectedDiagnostics.Add(Verify.MemberwiseCloneThroughListedBase(0, "Pinned", "Docs.Page", Handle, "_pinned"));

        await test.RunAsync();
    }

    // The warning names the nearest base class of which nothing is known. A class of the compilation between is checked code, and its own
    // fields get the warning as well. A 'clones=none' line vouches for its class and for every class above it, so a line for the nearest
    // base class answers the warning, and one for a class above it does not: it says nothing of the code of the class below.
    [TestMethod]
    [DataRow(null, true)]
    [DataRow("T:Docs.Chapter;clones=none", false)]
    [DataRow("T:Docs.Page;clones=none", true)]
    public async Task Warning_NamesTheNearestUnknownBaseClass(string? line, bool warns)
    {
        var test = new Verify.Test {
            TestCode = """
                using System.Runtime.InteropServices;

                class Middle : Docs.Chapter
                {
                    private GCHandle {|#0:_inMiddle|};
                }

                class Deep : Middle
                {
                    private GCHandle {|#1:_pinned|};
                }
                """,
        };

        test.TestState.AdditionalReferences.Add(await CompileDocsAsync(analyzed: false));

        if (line is not null)
            test.TestState.AdditionalFiles.Add((Verify.ListFileName, line));

        if (warns)
        {
            test.ExpectedDiagnostics.AddRange([
                Verify.FieldBehindUnknownBase(0, "Middle", "Docs.Chapter", "Docs", Handle, "_inMiddle"),
                Verify.FieldBehindUnknownBase(1, "Deep", "Docs.Chapter", "Docs", Handle, "_pinned"),
            ]);
        }

        await test.RunAsync();
    }

    // A 'clones=none' line for a class of checked code has no effect, and gets a warning in its file. It does not vouch for an unknown
    // class above it by the way: the walk goes on, and the field keeps its warning for the class that is not known.
    [TestMethod]
    public async Task NoneLine_ForAClassOfCheckedCodeDoesNotVouchForTheClassAbove()
    {
        var docs = await CompileDocsAsync(analyzed: false);
        var marked = await CompileDocsAsync(analyzed: true, "Marked", "namespace Marked { public class Sheet : Docs.Page { } }", docs);

        var test = new Verify.Test {
            TestCode = """
                using System.Runtime.InteropServices;

                class Middle : Docs.Page
                {
                }

                class Deep : Middle
                {
                    private GCHandle {|#0:_pinned|};
                }

                class OnSheet : Marked.Sheet
                {
                    private GCHandle {|#1:_pinned|};
                }
                """,
        };

        test.TestState.AdditionalReferences.Add(docs);
        test.TestState.AdditionalReferences.Add(marked);
        test.TestState.AdditionalFiles.Add((Verify.ListFileName, "T:Middle;clones=none\nT:Marked.Sheet;clones=none"));

        test.ExpectedDiagnostics.AddRange([
            Verify.FieldBehindUnknownBase(0, "Deep", "Docs.Page", "Docs", Handle, "_pinned"),
            Verify.FieldBehindUnknownBase(1, "OnSheet", "Docs.Page", "Docs", Handle, "_pinned"),
            Verify.SafeListNeverClonesHasNoEffect(1, 1, 21, "T:Middle", "the class is of the compilation, which is checked code"),
            Verify.SafeListNeverClonesHasNoEffect(2, 1, 27, "T:Marked.Sheet", "the class is of an assembly that was built with the analyzer, so it is checked code"),
        ]);

        await test.RunAsync();
    }

    // A 'trusted' line that another line makes needless has no effect, and its warning says so without claiming that the project has no
    // such class: here a 'clones=none' line answers the warning first.
    [TestMethod]
    public async Task TrustedLine_ThatAnotherLineMakesNeedlessHasNoEffect()
    {
        var test = new Verify.Test {
            TestCode = """
                using System.Runtime.InteropServices;

                class Pinned : Docs.Page
                {
                    private GCHandle _pinned;
                }
                """,
        };

        test.TestState.AdditionalReferences.Add(await CompileDocsAsync(analyzed: false));
        test.TestState.AdditionalFiles.Add((Verify.ListFileName, "T:Docs.Page;clones=none\nA:Docs;trusted"));

        test.ExpectedDiagnostics.Add(Verify.SafeListTrustedHasNoEffect(
            2,
            1,
            15,
            "Docs",
            "the assembly references no assembly that declares a non-copyable type or that lets a non-copyable value out, and the line answers no warning for a field behind a class of the assembly"));

        await test.RunAsync();
    }

    // A 'trusted' line vouches for the classes of its assembly, and the walk goes on above them: a base class of another assembly that
    // was not built with the analyzer is still not known. A 'clones=none' line for the nearest class vouches for every class above it.
    [TestMethod]
    [DataRow("", "More.Report", "More", "T:More.Report")]
    [DataRow("A:More;trusted", "Docs.Page", "Docs", "T:Docs.Page")]
    [DataRow("A:More;trusted\nA:Docs;trusted", null, null, null)]
    [DataRow("T:More.Report;clones=none", null, null, null)]
    [DataRow("A:More;trusted\nT:Docs.Page;clones=none", null, null, null)]
    public async Task Warning_GoesOnAboveATrustedAssembly(string lines, string? unknown, string? assembly, string? id)
    {
        var docs = await CompileDocsAsync(analyzed: false);
        var more = await CompileDocsAsync(analyzed: false, "More", "namespace More { public class Report : Docs.Page { } }", docs);

        var test = new Verify.Test {
            TestCode = """
                using System.Runtime.InteropServices;

                class Pinned : More.Report
                {
                    private GCHandle {|#0:_pinned|};
                }
                """,
        };

        test.TestState.AdditionalReferences.Add(docs);
        test.TestState.AdditionalReferences.Add(more);

        if (lines.Length > 0)
            test.TestState.AdditionalFiles.Add((Verify.ListFileName, lines));

        if (unknown is not null)
            test.ExpectedDiagnostics.Add(Verify.FieldBehindUnknownBase(0, "Pinned", unknown, assembly!, Handle, "_pinned", id));

        await test.RunAsync();
    }

    // A class that clones itself is the answer for the whole chain: the field gets that warning once, and none for an unknown class
    // between. A 'clones=none' line for a class that a 'clones' line names, itself or above it, has no effect and gets a warning.
    [TestMethod]
    public async Task ClonesLine_WinsOverAnUnknownBaseClassAndOverANoneLine()
    {
        var docs = await CompileDocsAsync(analyzed: false);
        var more = await CompileDocsAsync(
            analyzed: false, "More", "namespace More { public class Letter : Docs.Document { } public class Culture : System.Globalization.CultureInfo { public Culture() : base(\"\") { } } }", docs);

        var test = new Verify.Test {
            TestCode = """
                using System.Runtime.InteropServices;

                class Pinned : More.Letter
                {
                    private GCHandle {|#0:_pinned|};
                }

                class PinnedCulture : More.Culture
                {
                    private GCHandle {|#1:_pinned|};
                }
                """,
        };

        test.TestState.AdditionalReferences.Add(docs);
        test.TestState.AdditionalReferences.Add(more);
        test.TestState.AdditionalFiles.Add((Verify.ListFileName, "T:Docs.Document;clones\nT:More.Letter;clones=none\nT:Docs.Document;clones=none\nT:More.Culture;clones=none"));

        test.ExpectedDiagnostics.AddRange([
            Verify.MemberwiseCloneThroughListedBase(0, "Pinned", "Docs.Document", Handle, "_pinned"),
            Verify.MemberwiseCloneThroughListedBase(1, "PinnedCulture", "System.Globalization.CultureInfo", Handle, "_pinned"),
            Verify.SafeListNeverClonesHasNoEffect(2, 1, 26, "T:More.Letter", "a 'clones' line says that its base class 'Docs.Document' clones itself, and that line applies"),
            Verify.SafeListNeverClonesHasNoEffect(3, 1, 28, "T:Docs.Document", "a 'clones' line says that the class clones itself, and that line applies"),
            Verify.SafeListNeverClonesHasNoEffect(4, 1, 27, "T:More.Culture", "a 'clones' line says that its base class 'System.Globalization.CultureInfo' clones itself, and that line applies"),
        ]);

        await test.RunAsync();
    }

    // The field of a marked type parameter holds what a project binds to it, so it gets the warning in the build of its own class.
    [TestMethod]
    public async Task Field_OfAMarkedTypeParameterGetsTheWarning()
    {
        var test = new Verify.Test {
            TestCode = """
                using Singulink.CodeAnalysis;

                class Box<[NonCopyable] T> : Docs.Page
                {
                    public T {|#0:Value|} = default!;
                }

                class Unmarked<T> : Docs.Page
                {
                    public T Value = default!;
                }
                """,
        };

        test.TestState.AdditionalReferences.Add(await CompileDocsAsync(analyzed: false));
        test.ExpectedDiagnostics.Add(Verify.FieldBehindUnknownBase(0, "Box<T>", "Docs.Page", "Docs", "T", "Value"));
        await test.RunAsync();
    }

    // A 'clones=none' line has no effect for a class whose code is known: one of the compilation, of a library that was built with the
    // analyzer or of the framework. Nor for a class that no class can derive from. A line that names nothing is ignored, so that one file
    // can serve several projects.
    [TestMethod]
    public async Task NoneLine_ThatHasNoEffectIsReported()
    {
        var docs = await CompileDocsAsync(analyzed: false);
        var marked = await CompileDocsAsync(analyzed: true, "Marked", "namespace Marked { public class Sheet { } }");
        var closed = await CompileDocsAsync(analyzed: false, "Closed", "namespace Closed { public sealed class Leaf { } public static class Tools { } }");

        var test = new Verify.Test {
            TestCode = """
                public class Own { }
                """,
        };

        test.TestState.AdditionalReferences.Add(docs);
        test.TestState.AdditionalReferences.Add(marked);
        test.TestState.AdditionalReferences.Add(closed);
        test.TestState.AdditionalFiles.Add((Verify.ListFileName, """
            T:Docs.Page;clones=none
            T:Own;clones=none
            T:Marked.Sheet;clones=none
            T:System.IO.Stream;clones=none
            T:Closed.Leaf;clones=none
            T:Closed.Tools;clones=none
            T:Missing.Type;clones=none
            """));

        test.ExpectedDiagnostics.AddRange([
            Verify.SafeListNeverClonesHasNoEffect(2, 1, 18, "T:Own", "the class is of the compilation, which is checked code"),
            Verify.SafeListNeverClonesHasNoEffect(3, 1, 27, "T:Marked.Sheet", "the class is of an assembly that was built with the analyzer, so it is checked code"),
            Verify.SafeListNeverClonesHasNoEffect(4, 1, 31, "T:System.IO.Stream", "the class is of the framework, and the built-in symbol list says which classes of the framework clone themselves"),
            Verify.SafeListNeverClonesHasNoEffect(5, 1, 26, "T:Closed.Leaf", "the class is static or sealed, so no class can derive from it"),
            Verify.SafeListNeverClonesHasNoEffect(6, 1, 27, "T:Closed.Tools", "the class is static or sealed, so no class can derive from it"),
        ]);

        await test.RunAsync();
    }

    // 'none' is a value of the marker 'clones', and only a class's line takes it.
    [TestMethod]
    public Task NoneLine_IsAValueOfClonesForAClassAlone() => Verify.AnalyzerAsync(
        """
        public struct Value { }

        public class Open { public object Copy() => new Open(); }
        """,
        """
        M:Open.Copy;clones=none
        T:Open;none
        T:Open;clones;none
        T:Value;clones=none
        """,
        Verify.SafeListLine(1, 1, 24, "'clones=none' is for a class; a method, a property or an event takes 'clones' alone"),
        Verify.SafeListLine(2, 1, 12, "'none' is a value of the marker 'clones', written 'clones=none'"),
        Verify.SafeListLine(3, 1, 19, "'none' is a value of the marker 'clones', written 'clones=none'"),
        Verify.SafeListLine(4, 1, 20, "'clones=none' is for a class"));

    // A 'trusted' line for an assembly that references no non-copyable type has an effect only where it answers the warning: where a
    // class of the compilation that holds a non-copyable value derives from a class of the assembly.
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task TrustedLine_HasAnEffectOnlyWhereItAnswersTheWarning(bool holdsValue)
    {
        var test = new Verify.Test {
            TestCode = holdsValue
                ? """
                  using System.Runtime.InteropServices;

                  class Pinned : Docs.Page
                  {
                      private GCHandle _pinned;
                  }
                  """
                : """
                  class Plain : Docs.Page
                  {
                      private int _count;
                  }
                  """,
        };

        test.TestState.AdditionalReferences.Add(await CompileDocsAsync(analyzed: false));
        test.TestState.AdditionalFiles.Add((Verify.ListFileName, "A:Docs;trusted"));

        if (!holdsValue)
        {
            test.ExpectedDiagnostics.Add(Verify.SafeListTrustedHasNoEffect(
                1,
                1,
                15,
                "Docs",
                "the assembly references no assembly that declares a non-copyable type or that lets a non-copyable value out, and the line answers no warning for a field behind a class of the assembly"));
        }

        await test.RunAsync();
    }
}
