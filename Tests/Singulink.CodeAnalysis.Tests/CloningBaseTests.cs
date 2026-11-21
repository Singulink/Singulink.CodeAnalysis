using Microsoft.CodeAnalysis.Testing;
using PrefixClassName.MsTest;

namespace Singulink.CodeAnalysis.Tests;

/// <summary>
/// Tests of a class that derives from a class whose own code clones its objects with MemberwiseClone, which a 'clones' line of the
/// symbol list names: every field of the class that holds a non-copyable value gets SCA1005, since the clone copies it.
/// </summary>
/// <remarks>
/// Docs: articles/diagnostics/sca1005.md, articles/rules/symbol-list-format.md and articles/rules/framework-symbol-list.md.
/// </remarks>
[PrefixTestClass]
public class CloningBaseTests
{
    private const string EncodingType = "System.Text.Encoding";
    private const string Handle = "System.Runtime.InteropServices.GCHandle";

    // What a class that derives from Encoding has to implement.
    private const string EncodingMembers = """
            public override int GetByteCount(char[] chars, int index, int count) => count;

            public override int GetBytes(char[] chars, int charIndex, int charCount, byte[] bytes, int byteIndex) => 0;

            public override int GetCharCount(byte[] bytes, int index, int count) => count;

            public override int GetChars(byte[] bytes, int byteIndex, int byteCount, char[] chars, int charIndex) => 0;

            public override int GetMaxByteCount(int charCount) => charCount;

            public override int GetMaxCharCount(int byteCount) => byteCount;
        """;

    private static readonly ReferenceAssemblies DesktopReferenceAssemblies =
        Verify.Test.NetReferenceAssemblies.AddPackages([new PackageIdentity("Microsoft.WindowsDesktop.App.Ref", "10.0.0")]);

    // An encoding that hands buffers to native code holds a handle, and a culture or a calendar can hold one as well: the built-in
    // framework symbol list says that their base classes clone themselves, so each field that holds a non-copyable value gets a
    // warning, a GCHandle of the framework and a struct of the project alike.
    [TestMethod]
    public Task Field_OfAClassThatDerivesFromAFrameworkClassThatClonesItselfGetsAWarning() => Verify.AnalyzerAsync(
        $$"""
        using System.Collections.Generic;
        using System.Globalization;
        using System.Runtime.InteropServices;
        using System.Text;

        sealed class PinnedEncoding : Encoding
        {
            private GCHandle {|#0:_pinned|};

        {{EncodingMembers}}
        }

        sealed class GuardedEncoding : Encoding
        {
            private Guard {|#1:_guard|};

        {{EncodingMembers}}
        }

        sealed class PinnedCulture : CultureInfo
        {
            private GCHandle {|#2:_pinned|};

            public PinnedCulture() : base("en-US") { }
        }

        sealed class PinnedCalendar : GregorianCalendar
        {
            private GCHandle {|#3:_pinned|};
        }

        // A tuple and a struct that hold a non-copyable value are copied with it.
        sealed class TupledEncoding : Encoding
        {
            private (GCHandle Handle, int Count) {|#4:_entry|};

        {{EncodingMembers}}
        }

        [Singulink.CodeAnalysis.NonCopyable]
        struct Holder
        {
            public Guard Guard;
        }

        sealed class HeldEncoding : Encoding
        {
            private Holder {|#5:_holder|};

        {{EncodingMembers}}
        }

        // A container, an object of a class and a static field are shared by the clone: no warning.
        sealed class HandleHolder
        {
            public GCHandle Handle;
        }

        sealed class SharedEncoding : Encoding
        {
            private readonly List<GCHandle> _handles = new();
            private readonly HandleHolder _holder = new();
            private static GCHandle s_pinned;

        {{EncodingMembers}}
        }

        // A class that derives from no class that clones itself: no warning.
        sealed class Plain
        {
            private GCHandle _pinned;
        }

        {{Sources.Guard}}
        """,
        Verify.MemberwiseCloneThroughListedBase(0, "PinnedEncoding", EncodingType, Handle, "_pinned"),
        Verify.MemberwiseCloneThroughListedBase(1, "GuardedEncoding", EncodingType, "Guard", "_guard"),
        Verify.MemberwiseCloneThroughListedBase(2, "PinnedCulture", "System.Globalization.CultureInfo", Handle, "_pinned"),
        Verify.MemberwiseCloneThroughListedBase(3, "PinnedCalendar", "System.Globalization.Calendar", Handle, "_pinned"),
        Verify.MemberwiseCloneThroughListedBase(4, "TupledEncoding", EncodingType, $"({Handle} Handle, int Count)", "_entry"),
        Verify.MemberwiseCloneThroughListedBase(5, "HeldEncoding", EncodingType, "Holder", "_holder"));

    // The field can be in a class between: it gets one warning, in the class that declares it. A generic class whose field is of an
    // unmarked type parameter gets the warning where a non-copyable type is bound to it, and one with a marked type parameter on the
    // field as well, with a text that says so. A captured parameter of a primary constructor is a field. A class that calls
    // MemberwiseClone itself gets the warning at the call and on the field: the call and the clone of the base class are two routes.
    [TestMethod]
    public Task Field_ThroughAClassBetweenAGenericBaseOrACapturedParameterGetsAWarningForEachRoute() => Verify.AnalyzerAsync(
        $$"""
        using System.Runtime.InteropServices;
        using System.Text;
        using Singulink.CodeAnalysis;

        abstract class TextBase : Encoding
        {
        {{EncodingMembers}}
        }

        sealed class Deep : TextBase
        {
            private GCHandle {|#0:_pinned|};
        }

        class Middle : TextBase
        {
            protected GCHandle {|#1:_inMiddle|};
        }

        sealed class Bottom : Middle { }

        class Box<T> : TextBase
        {
            public T Value = default!;
        }

        sealed class Boxed : {|#2:Box<GCHandle>|} { }

        class MarkedBox<[NonCopyable] T> : TextBase
        {
            public T {|#3:Value|} = default!;
        }

        sealed class Captures(Guard {|#4:guard|}) : TextBase
        {
            public int Id => guard.Value;
        }

        sealed class SelfCloning : TextBase
        {
            private GCHandle {|#6:_pinned|};

            public object Copy() => {|#5:MemberwiseClone()|};
        }

        {{Sources.Guard}}
        """,
        Verify.MemberwiseCloneThroughListedBase(0, "Deep", EncodingType, Handle, "_pinned"),
        Verify.MemberwiseCloneThroughListedBase(1, "Middle", EncodingType, Handle, "_inMiddle"),
        Verify.CloningBinding(2, Handle, "Box<System.Runtime.InteropServices.GCHandle>", EncodingType),
        Verify.MemberwiseCloneOfTypeParameterThroughListedBase(3, "MarkedBox<T>", EncodingType, "T", "Value"),
        Verify.MemberwiseCloneThroughListedBase(4, "Captures", EncodingType, "Guard", "guard", isParameter: true),
        Verify.MemberwiseCloneCall(5, "SelfCloning", Handle, "_pinned"),
        Verify.MemberwiseCloneThroughListedBase(6, "SelfCloning", EncodingType, Handle, "_pinned"));

    // WPF's Stroke and the printing classes are on the built-in framework symbol list as well.
    [TestMethod]
    public Task Field_OfAClassThatDerivesFromADesktopClassThatClonesItselfGetsAWarning()
    {
        // The desktop pack has a WindowsBase of its own version, which the facade of the core pack conflicts with (CS1705). The two
        // warnings show that the classes bind.
        var test = new Verify.Test {
            ReferenceAssemblies = DesktopReferenceAssemblies,
            CompilerDiagnostics = CompilerDiagnostics.None,
            TestCode = """
                using System.Drawing.Printing;
                using System.Runtime.InteropServices;
                using System.Windows.Ink;
                using System.Windows.Input;

                sealed class PinnedStroke : Stroke
                {
                    private GCHandle {|#0:_pinned|};

                    public PinnedStroke() : base(new StylusPointCollection(new[] { new StylusPoint(0, 0) })) { }
                }

                sealed class PinnedPageSettings : PageSettings
                {
                    private GCHandle {|#1:_pinned|};
                }
                """,
        };

        test.ExpectedDiagnostics.AddRange([
            Verify.MemberwiseCloneThroughListedBase(0, "PinnedStroke", "System.Windows.Ink.Stroke", Handle, "_pinned"),
            Verify.MemberwiseCloneThroughListedBase(1, "PinnedPageSettings", "System.Drawing.Printing.PageSettings", Handle, "_pinned"),
        ]);

        return test.RunAsync();
    }

    // A 'clones' line of a project's symbol list file makes a class of a referenced library count, and a class of the compilation
    // itself: the line is about the classes that derive from it, not about the class's own fields. A library's shipped file counts the
    // same. With no line, the class of the library, which was not built with the analyzer, is one of which nothing is known, and the field
    // gets the warning for that: see UnknownBaseTests.
    [TestMethod]
    [DataRow(null, false)]
    [DataRow("NonCopyableSymbols.txt", true)]
    [DataRow("packages/mylibrary/1.0.0/build/NonCopyableSymbols.MyLibrary.txt", true)]
    public async Task ClonesLine_OfAProjectOrOfALibraryMakesAClassCount(string? file, bool counts)
    {
        var library = await Verify.CompileLibraryAsync(
            Verify.Test.NetReferenceAssemblies,
            "namespace Docs { public class Document { public Document Copy() => (Document)MemberwiseClone(); } }",
            analyzed: false,
            "MyLibrary");

        var test = new Verify.Test {
            TestCode = """
                using System.Runtime.InteropServices;

                class Pinned : Docs.Document
                {
                    private GCHandle {|#0:_pinned|};
                }

                public class Own
                {
                    private GCHandle _ownHandle;
                }

                sealed class OwnDerived : Own
                {
                    private GCHandle {|#1:_pinned|};
                }
                """,
        };

        test.TestState.AdditionalReferences.Add(library);

        if (file is not null)
            test.TestState.AdditionalFiles.Add((file, "T:Docs.Document;clones\nT:Own;clones"));

        if (counts)
        {
            test.ExpectedDiagnostics.AddRange([
                Verify.MemberwiseCloneThroughListedBase(0, "Pinned", "Docs.Document", Handle, "_pinned"),
                Verify.MemberwiseCloneThroughListedBase(1, "OwnDerived", "Own", Handle, "_pinned"),
            ]);
        }
        else
        {
            test.ExpectedDiagnostics.Add(Verify.FieldBehindUnknownBase(0, "Pinned", "Docs.Document", "MyLibrary", Handle, "_pinned"));
        }

        await test.RunAsync();
    }

    // A 'clones' line for a sealed generic class has an effect: nothing derives from the class, but a non-copyable type can be bound to it,
    // and the clone copies it. So the line gets no SCA1042, and the binding gets its warning. A line for a sealed class that is not
    // generic, or for a static class, still has no effect.
    [TestMethod]
    public async Task ClonesLine_ForASealedGenericClassHasAnEffect()
    {
        var library = await Verify.CompileLibraryAsync(Verify.Test.NetReferenceAssemblies, """
            namespace LibS
            {
                public sealed class Box<T>
                {
                    public T Value = default!;

                    public Box<T> Copy() => (Box<T>)MemberwiseClone();
                }

                public sealed class Plain { }

                public static class Helpers { }
            }
            """, analyzed: false, "LibS");

        var test = new Verify.Test {
            TestCode = """
                using LibS;

                class C
                {
                    void Uses() => _ = {|#0:new Box<Guard>()|};
                }
                """ + System.Environment.NewLine + Sources.Guard,
        };

        test.TestState.AdditionalFiles.Add((Verify.ListFileName, "T:LibS.Box`1;clones\nT:LibS.Plain;clones\nT:LibS.Helpers;clones"));
        test.TestState.AdditionalReferences.Add(library);
        test.ExpectedDiagnostics.Add(Verify.CloningBinding(0, "Guard", "LibS.Box<Guard>", "LibS.Box<T>"));
        test.ExpectedDiagnostics.Add(Verify.UnlistedMember(0, "Box<T>.Box()", "Guard", "T", "M:LibS.Box`1.#ctor"));
        test.ExpectedDiagnostics.Add(Verify.SafeListClonesHasNoEffect(2, 1, 20, "T:LibS.Plain", "the class is sealed and not generic, so no class can derive from it and no type can be bound to it"));
        test.ExpectedDiagnostics.Add(Verify.SafeListClonesHasNoEffect(3, 1, 22, "T:LibS.Helpers", "the class is static, so no class can derive from it"));
        await test.RunAsync();
    }

    // A 'clones' line that has no effect gets SCA1042: one that names no type, a static class or a sealed one that is not generic, or something that is not a
    // class. A line that repeats one of the built-in list, or names a class whose base class is listed, has its effect already.
    [TestMethod]
    public Task ClonesLine_ThatHasNoEffectOrDoesNotFitGetsAWarning() => Verify.AnalyzerAsync(
        """
        using System.Text;

        public sealed class Closed { }

        public static class Helpers { }

        public struct Value { }

        public abstract class Text : Encoding { }

        public class Open
        {
            public object Copy() => {|#0:MemberwiseClone()|};
        }
        """,
        """
        T:Missing;clones
        T:Closed;clones
        T:Helpers;clones
        T:Value;clones
        M:Open.Missing;clones
        A:MyLibrary;clones
        T:System.Text.Encoding;clones
        T:Text;clones
        M:Open.Copy;clones
        M:System.Globalization.CultureInfo.Clone
        M:Open.Copy;borrows
        P:Open.Missing;clones
        """,
        Verify.SafeListClonesHasNoEffect(1, 1, 17, "T:Missing", "the compilation has no type of that ID"),
        Verify.SafeListClonesHasNoEffect(2, 1, 16, "T:Closed", "the class is sealed and not generic, so no class can derive from it and no type can be bound to it"),
        Verify.SafeListClonesHasNoEffect(3, 1, 17, "T:Helpers", "the class is static, so no class can derive from it"),
        Verify.SafeListLine(4, 1, 15, "the marker 'clones' is for a class, a method, a property or an event"),
        Verify.SafeListClonesHasNoEffect(5, 1, 22, "M:Open.Missing", "the compilation has no method of that ID"),
        Verify.SafeListLine(6, 1, 19, "the marker 'clones' is not for an assembly, which takes 'trusted'"),
        Verify.SafeListContradictsClones(10, 1, 41, "M:System.Globalization.CultureInfo.Clone"),
        Verify.SafeListContradictsClones(11, 1, 20, "M:Open.Copy"),
        Verify.SafeListClonesHasNoEffect(12, 1, 22, "P:Open.Missing", "the compilation has no property of that ID"),
        Verify.MemberwiseCloneOfOpenClass(0, "Open"));

    // With the built-in non-copyable types turned off for the project, a GCHandle is an ordinary struct, and only the struct that the
    // project marks counts.
    [TestMethod]
    public Task Field_OfABuiltInTypeCountsOnlyWhereTheBuiltInTypesAreOn() => new Verify.Test {
        TestCode = $$"""
            using System.Runtime.InteropServices;
            using System.Text;

            sealed class PinnedEncoding : Encoding
            {
                private GCHandle _pinned;
                private Guard {|#0:_guard|};

            {{EncodingMembers}}
            }

            {{Sources.Guard}}
            """,
        ExpectedDiagnostics = { Verify.MemberwiseCloneThroughListedBase(0, "PinnedEncoding", EncodingType, "Guard", "_guard") },
    }.WithBuildProperty("NonCopyableIncludeBuiltInTypes", "false").RunAsync();

    // A generic class of a library that derives from a class that clones itself gets the warning where the project binds a non-copyable
    // type to it: as the base class of a class of the project, and in an object creation. Its fields are not looked at, since a
    // reference assembly may leave them out: the type argument is enough. That holds whether or not the library was built with the
    // analyzers, and for a marked and an unmarked type parameter. (QA9 1.1)
    [TestMethod]
    [DataRow(true, true)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(false, false)]
    public async Task Binding_OfANonCopyableTypeToAGenericClassOfALibraryThatClonesGetsAWarning(bool libraryAnalyzed, bool marked)
    {
        string mark = marked ? "[NonCopyable] " : string.Empty;
        var library = await Verify.CompileLibraryAsync(
            Verify.Test.NetReferenceAssemblies,
            $$"""
            using System.Globalization;
            using Singulink.CodeAnalysis;

            namespace LibO
            {
                public class TaggedCulture<{{mark}}T> : CultureInfo
                {
                    public T Tag;

                    public TaggedCulture(string name) : base(name) { }
                }
            }
            """,
            libraryAnalyzed,
            "LibO");

        var test = new Verify.Test {
            TestCode = $$"""
                using System.Globalization;
                using LibO;

                sealed class GuardCulture : {|#0:TaggedCulture<Guard>|}
                {
                    public GuardCulture() {|#2:: base("en-US")|} { }
                }

                sealed class HeldCulture : TaggedCulture<GuardHolder>
                {
                    public HeldCulture() : base("en-US") { }
                }

                sealed class GuardHolder
                {
                    public Guard Guard;
                }

                class C
                {
                    CultureInfo ReadOnlyCopy()
                    {
                        var culture = {|#1:new TaggedCulture<Guard>("en-US")|};
                        return CultureInfo.ReadOnly({|#3:culture|});
                    }
                }

                {{Sources.Guard}}
                """,
        };

        test.TestState.AdditionalReferences.Add(library);
        test.ExpectedDiagnostics.Add(Verify.CloningBinding(0, "Guard", "LibO.TaggedCulture<Guard>", "System.Globalization.CultureInfo"));
        test.ExpectedDiagnostics.Add(Verify.CloningBinding(1, "Guard", "LibO.TaggedCulture<Guard>", "System.Globalization.CultureInfo"));

        // Where the mark does not count, unmarked or in a library that was not built with the analyzers, the generic is unchecked code
        // for the non-copyable type: its constructor is not on the symbol list.
        if (!marked || !libraryAnalyzed)
        {
            const string Constructor = "TaggedCulture<T>.TaggedCulture(string)";
            const string Id = "M:LibO.TaggedCulture`1.#ctor(System.String)";

            foreach (int location in new[] { 1, 2 })
            {
                test.ExpectedDiagnostics.Add(marked
                    ? Verify.UnlistedMemberOfUncheckedCode(location, Constructor, "Guard", "T", Id)
                    : Verify.UnlistedMember(location, Constructor, "Guard", "T", Id));
            }
        }

        // The object reaches the code that clones it as the argument of a member that clones: that use gets SCA1009 in every case.
        test.ExpectedDiagnostics.Add(Verify.CloningUseOfCloningMember(3, "CultureInfo.ReadOnly(CultureInfo)", "LibO.TaggedCulture<Guard>", "Guard", "LibO.TaggedCulture<Guard>"));

        await test.RunAsync();
    }

    // A class that copies itself with MemberwiseClone and fixes the copy up gets the warning at the call, which it may suppress, and
    // the warning on the field for the clone of its base class, a second route that the suppression does not answer. (QA9 1.2)
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public Task Field_KeepsItsWarningWhenTheWarningAtACallIsSuppressed(bool suppressed)
    {
        string attribute = suppressed ? "[SuppressMessage(\"Copy\", \"SCA1005\", Justification = \"The clone's guard is replaced.\")]" : string.Empty;
        string call = suppressed ? "{|#2:MemberwiseClone()|}" : "{|#1:{|#2:MemberwiseClone()|}|}";

        var test = new Verify.Test {
            TestCode = $$"""
                using System.Diagnostics.CodeAnalysis;
                using System.Globalization;

                public class GuardedCulture : CultureInfo
                {
                    public Guard {|#0:G|};

                    public GuardedCulture() : base("en-US") { }

                    {{attribute}}
                    public GuardedCulture Copy()
                    {
                        var copy = (GuardedCulture){{call}};
                        copy.G = default;
                        return copy;
                    }
                }

                {{Sources.Guard}}
                """,
        };

        test.ExpectedDiagnostics.Add(Verify.MemberwiseCloneThroughListedBase(0, "GuardedCulture", "System.Globalization.CultureInfo", "Guard", "G"));

        // A class that clones itself, and that another assembly can derive from, ships a 'clones' line for each member that clones its object.
        test.ExpectedDiagnostics.Add(Verify.MemberClonesWithoutLine(2, "GuardedCulture.Copy()", "object.MemberwiseClone()", "GuardedCulture", "M:GuardedCulture.Copy"));

        if (!suppressed)
            test.ExpectedDiagnostics.Add(Verify.MemberwiseCloneCall(1, "GuardedCulture", "Guard", "G"));

        return test.RunAsync();
    }

    // A member that clones its object is no member that a symbol list line can vouch for: the warning on the call says so, and a line
    // of a symbol list file that says it makes no copy gets a warning and is left out, so the call keeps its warning. (QA9 z4)
    [TestMethod]
    public Task CloningMember_GetsNoAdviceToVouchForIt() => Verify.AnalyzerAsync(
        $$"""
        using System.Globalization;

        public class Tagged<T> : CultureInfo
        {
            public T Tag = default!;

            public Tagged() : base("en-US") { }
        }

        class C
        {
            object Copy()
            {
        #pragma warning disable SCA1020
                var tagged = {|#0:new Tagged<Guard>()|};
        #pragma warning restore SCA1020
                return {|#1:tagged.Clone()|};
            }
        }

        {{Sources.Guard}}
        """,
        "M:System.Globalization.CultureInfo.Clone",
        Verify.CloningBinding(0, "Guard", "Tagged<Guard>", "System.Globalization.CultureInfo"),
        Verify.CloningUseOfCloningMember(1, "CultureInfo.Clone()", "Tagged<Guard>", "Guard", "Tagged<Guard>"),
        Verify.SafeListContradictsClones(1, 1, 41, "M:System.Globalization.CultureInfo.Clone"));

    // SCA1008 gives the 'clones' line that a library ships to keep a class open and cloneable, with the ID of a generic class.
    [TestMethod]
    public Task OpenClass_TheWarningGivesTheClonesLineOfTheClass() => Verify.AnalyzerAsync(
        """
        public class Box<T>
        {
            public object Copy() => {|#0:MemberwiseClone()|};
        }
        """,
        Verify.MemberwiseCloneOfOpenClass(0, "Box<T>", "T:Box`1"));
}
