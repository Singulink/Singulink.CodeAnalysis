using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;
using PrefixClassName.MsTest;
using Singulink.CodeAnalysis.Analyzers;

namespace Singulink.CodeAnalysis.Tests;

/// <summary>
/// Tests of the symbol list: the format of a list file, the merging of user files with the built-in list, and the built-in list itself.
/// </summary>
/// <remarks>
/// Docs: articles/guides/symbol-list.md.
/// </remarks>
[PrefixTestClass]
public class SafeListTests
{
    private const string Library = """
        public class Bag<T>
        {
            public Bag() { }
            public void Add(T value) { }
            public T Peek() => default!;
            public T Take() => default!;
            public T Other() => default!;
            public int Count => 0;
            public Handle AsHandle() => default;

            public struct Handle { }
        }

        public struct Opaque { }

        public class Map<TKey, TValue>
        {
            public void Add(TKey key, TValue value) { }
            public bool Remove(TKey key, out TValue value) => throw null!;
            public void Convert<TOther>(TOther other) { }
        }
        """;

    [TestMethod]
    public Task UserFile_IsMergedWithTheBuiltInList() => Verify.AnalyzerAsync(
        $$"""
        using System.Collections.Generic;

        class C
        {
            Opaque _opaque;

            void M(RwLock rwLock, Bag<Guard> stored)
            {
                var bag = new Bag<Guard>();
                bag.Add(rwLock.Enter());
                int count = bag.Count;

                // What a listed member hands out is as new as what it was called on, unless the entry says 'new'.
                Guard peeked = {|#0:stored.Peek()|};
                Guard peekedFromNew = new Bag<Guard>().Peek();
                Guard taken = stored.Take();

                // Not in the file.
                Guard other = {|#1:stored.Other()|};

                // A view can be copied.
                var handle = stored.AsHandle();
                var handleCopy = handle;

                // A struct that the file names as non-copyable.
                var opaqueCopy {|#2:= _opaque|};

                // The built-in list still applies.
                var list = new List<Guard>();
                list.Add(rwLock.Enter());
            }
        }

        {{Library}}

        {{Sources.Guard}}
        """,
        """
        # The members of Bag<T> that make no copy
        M:Bag`1.#ctor
        M:Bag`1.Add(`0)      # moves the value in
        M:Bag`1.Peek
        P:Bag`1.Count
        M:Bag`1.AsHandle

        M:Bag`1.Take;new
        T:Bag`1.Handle;view
        T:Opaque;noncopyable
        """,
        Verify.ReadFromStoredContainer(0, "'Bag<T>.Peek()'", "Guard"),
        Verify.UnlistedMember(1, "Bag<T>.Other()", "Guard", "T", "M:Bag`1.Other"),
        Verify.CopyByAssignment(2, "Opaque"));

    [TestMethod]
    public Task UserFile_SeveralFilesAndEntriesForTheSameMember()
    {
        var test = new Verify.Test {
            TestCode = $$"""
                using System.Collections.Generic;

                class C
                {
                    void M(Bag<Guard> stored, Stack<Guard> stack, Map<Guard, Guard> map, RwLock rwLock)
                    {
                        Guard taken = stored.Take();

                        // The files disagree about Peek. The entry that claims less applies, whatever the order of the files: it is not 'new'.
                        Guard peeked = {|#0:stored.Peek()|};

                        // A user file can tighten an entry of the built-in list, which has Stack<T>.Pop as 'new'.
                        Guard popped = {|#1:stack.Pop()|};

                        // Only the type parameter that both entries vouch for is covered.
                        {|#2:map.Add(rwLock.Enter(), rwLock.Enter())|};
                    }
                }

                {{Library}}

                {{Sources.Guard}}
                """,
        };

        test.TestState.AdditionalFiles.Add(("First/NonCopyableSymbols.txt", """
            M:Bag`1.Peek
            M:Bag`1.Take;new
            M:System.Collections.Generic.Stack`1.Pop
            M:Map`2.Add(`0,`1)
            """));

        test.TestState.AdditionalFiles.Add(("Second/NonCopyableSymbols.txt", """
            M:Bag`1.Peek;new
            M:Bag`1.Take;new
            M:Map`2.Add(`0,`1);for=TValue
            """));

        // A file of another name is not a list.
        test.TestState.AdditionalFiles.Add(("Other.txt", "M:Bag`1.Other"));

        test.ExpectedDiagnostics.Add(Verify.ReadFromStoredContainer(0, "'Bag<T>.Peek()'", "Guard"));
        test.ExpectedDiagnostics.Add(Verify.ReadFromStoredContainer(1, "'Stack<T>.Pop()'", "Guard"));
        test.ExpectedDiagnostics.Add(Verify.MemberListedForOtherTypeParameter(2, "Map<TKey, TValue>.Add(TKey, TValue)", "Guard", "TKey", "TValue"));

        return test.RunAsync();
    }

    // A file that is named after a library, as a package ships it, is a list file like the project's own.
    [TestMethod]
    public Task LibraryFile_IsReadAndMergedWithTheOtherFiles()
    {
        const string LibraryFile = "packages/mylibrary/1.0.0/build/NonCopyableSymbols.MyLibrary.txt";
        const string SecondLibraryFile = "packages/second/nonCOPYABLEsymbols.Second.Library.TXT";

        var test = new Verify.Test {
            TestCode = $$"""
                class C
                {
                    void M(Bag<Guard> stored)
                    {
                        // The file of the library has Take as 'new'.
                        Guard taken = stored.Take();

                        // The file of the project claims less for Peek than the file of the library does, and that applies: it is not 'new'.
                        Guard peeked = {|#0:stored.Peek()|};

                        // Only a file with a name of one of the two forms is a list.
                        Guard other = {|#1:stored.Other()|};
                    }
                }

                {{Library}}

                {{Sources.Guard}}
                """,
        };

        test.TestState.AdditionalFiles.Add((LibraryFile, """
            M:Bag`1.Take;new
            M:Bag`1.Peek;new
            not an entry
            """));

        test.TestState.AdditionalFiles.Add((Verify.ListFileName, "M:Bag`1.Peek"));

        // The case of the name does not matter, and neither does a second dot in it.
        test.TestState.AdditionalFiles.Add((SecondLibraryFile, "M:Bag`1.Take;new # the same as the other file\nM:Bag`1.Missing;unknown"));

        // These are not list files: nothing in them counts, and nothing in them is reported.
        test.TestState.AdditionalFiles.Add(("NonCopyableSymbolsX.txt", "M:Bag`1.Other;new\nnot an entry"));
        test.TestState.AdditionalFiles.Add(("NonCopyableSymbols.MyLibrary.md", "M:Bag`1.Other;new\nnot an entry"));
        test.TestState.AdditionalFiles.Add(("MyLibrary.NonCopyableSymbols.txt", "M:Bag`1.Other;new\nnot an entry"));

        // Nor is a file of the name that the list had before the first release.
        test.TestState.AdditionalFiles.Add(("NonCopyableSafeMembers.txt", "M:Bag`1.Other;new\nnot an entry"));
        test.TestState.AdditionalFiles.Add(("NonCopyableSafeMembers.MyLibrary.txt", "M:Bag`1.Other;new\nnot an entry"));

        test.ExpectedDiagnostics.Add(Verify.ReadFromStoredContainer(0, "'Bag<T>.Peek()'", "Guard"));
        test.ExpectedDiagnostics.Add(Verify.UnlistedMember(1, "Bag<T>.Other()", "Guard", "T", "M:Bag`1.Other"));

        // A line that cannot be parsed is reported in the file that has it.
        test.ExpectedDiagnostics.Add(new DiagnosticResult(DiagnosticIds.SafeListLine, DiagnosticSeverity.Warning)
            .WithSpan(LibraryFile, 3, 1, 3, 13)
            .WithMessage("This line of the symbol list file cannot be parsed: expected a documentation comment ID that starts with 'M:', 'P:', 'E:' or 'T:', or the name of an assembly after 'A:'"));

        test.ExpectedDiagnostics.Add(new DiagnosticResult(DiagnosticIds.SafeListLine, DiagnosticSeverity.Warning)
            .WithSpan(SecondLibraryFile, 2, 1, 2, 24)
            .WithMessage("This line of the symbol list file cannot be parsed: unknown marker 'unknown'; the markers are 'new', 'borrows', 'drains', 'owned', 'view', 'noncopyable', 'copyable', 'erasable' and 'clones', and 'trusted' for an assembly"));

        return test.RunAsync();
    }

    [TestMethod]
    public Task UserFile_CannotLoosenABuiltInEntry() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using System.Collections.Generic;
        using System.Threading;

        class C
        {
            SpinLock _lock;

            void M(Stack<Guard> stack, Span<Guard> span, List<Guard> list)
            {
                // The built-in list has SpinLock as non-copyable and Stack<T>.Peek without 'new'. Where two entries disagree, the one that
                // claims less applies, so the user file changes neither.
                SpinLock copy {|#0:= _lock|};
                Guard peeked = {|#1:stack.Peek()|};

                // It can tighten: a view becomes non-copyable.
                Span<Guard> spanCopy {|#2:= span|};
                Span<Guard> last = span;

                // A line whose 'for=' names a type parameter that the member does not have is reported and ignored, like any other line
                // that cannot be parsed: the built-in entry stays as it is.
                list.Clear();
            }
        }

        {{Sources.Guard}}
        """,
        """
        T:System.Threading.SpinLock;view
        M:System.Collections.Generic.Stack`1.Peek;new
        T:System.Span`1;noncopyable
        M:System.Collections.Generic.List`1.Clear;for=TNone
        """,
        Verify.CopyByAssignment(0, "System.Threading.SpinLock"),
        Verify.ReadFromStoredContainer(1, "'Stack<T>.Peek()'", "Guard"),
        Verify.CopyByAssignment(2, "System.Span<Guard>"),
        Verify.SafeListLine(4, 1, 52, "'TNone' is not a type parameter of the member or of a type that contains it"));

    [TestMethod]
    public Task UserFile_EntryThatDoesNotResolveIsIgnored() => Verify.AnalyzerAsync(
        $$"""
        class C
        {
            void M(Bag<Guard> stored)
            {
                Guard taken = stored.Take();
                Guard other = {|#0:stored.Other()|};
            }
        }

        {{Library}}

        {{Sources.Guard}}
        """,
        """
        M:Some.Library.NotReferenced`1.Member(`0)
        M:Some.Library.NotReferenced`1.Other;new;for=TKey
        T:Some.Library.View`1;view
        P:Bag`1.NoSuchProperty
        M:Bag`1.Take;new
        M:Bag`1.Other(System.Int32)
        """,
        Verify.UnlistedMember(0, "Bag<T>.Other()", "Guard", "T", "M:Bag`1.Other"));

    [TestMethod]
    public Task UserFile_LineThatCannotBeParsedIsReported() => Verify.AnalyzerAsync(
        Library,
        """
        # A comment, then lines that are fine
        M:Bag`1.#ctor
           M:Bag`1.Peek   ;   new    # white space is allowed around the parts

        Bag`1.Take
        F:Bag`1._items
        M:
        T:Bag`1
        T:Bag`1.Handle;new
        M:Bag`1.Take;view
        M:Bag`1.Take;noncopyable
        M:Bag`1.Take;safe
        M:Bag`1.Take;new;new
        M:Bag`1 .Take
        M:Bag`1.Take#part-of-the-ID-so-it-names-nothing
        T:Opaque;noncopyable;for=T
        M:Bag`1.Take;for=
        M:Bag`1.Take;for=T,,U
        M:Bag`1.Take;for=T;new
        M:Bag`1.Take ; new ; for = T   # white space is allowed around the '=' as well
        M:Bag`1.Take(
        M:Bag`1.Add(`0
        M:Bag`1.Add(`0))
        M:Bag`1..Take
        T:Bag`1(System.Int32);view
        """,
        Verify.SafeListLine(5, 1, 11, "expected a documentation comment ID that starts with 'M:', 'P:', 'E:' or 'T:', or the name of an assembly after 'A:'"),
        Verify.SafeListLine(6, 1, 15, "expected a documentation comment ID that starts with 'M:', 'P:', 'E:' or 'T:', or the name of an assembly after 'A:'"),
        Verify.SafeListLine(7, 1, 3, "expected a documentation comment ID that starts with 'M:', 'P:', 'E:' or 'T:', or the name of an assembly after 'A:'"),
        Verify.SafeListLine(8, 1, 8, "a type needs the marker 'view', 'noncopyable', 'copyable', 'erasable' or 'clones'; there is no entry for every member of a type"),
        Verify.SafeListLine(9, 1, 19, "the marker 'new' is for a member; a type takes 'view', 'noncopyable', 'copyable', 'erasable' or 'clones'"),
        Verify.SafeListLine(10, 1, 18, "the marker 'view' is for a type; a member takes 'new', 'borrows', 'drains', 'owned', 'clones' or no marker"),
        Verify.SafeListLine(11, 1, 25, "the marker 'noncopyable' is for a type; a member takes 'new', 'borrows', 'drains', 'owned', 'clones' or no marker"),
        Verify.SafeListLine(12, 1, 18, "unknown marker 'safe'; the markers are 'new', 'borrows', 'drains', 'owned', 'view', 'noncopyable', 'copyable', 'erasable' and 'clones', and 'trusted' for an assembly"),
        Verify.SafeListLine(13, 1, 21, "an entry has one marker, which 'for=' can follow"),
        Verify.SafeListLine(14, 1, 14, "a documentation comment ID has no white space"),
        Verify.SafeListLine(16, 1, 27, "'for=' is for a member"),
        Verify.SafeListLine(17, 1, 18, "'for=' takes one or more type parameter names, separated by commas"),
        Verify.SafeListLine(18, 1, 22, "'for=' takes one or more type parameter names, separated by commas"),
        Verify.SafeListLine(19, 1, 23, "'for=' is the last part of an entry"),
        Verify.SafeListLine(21, 1, 14, "the parentheses, braces and brackets of a documentation comment ID come in pairs"),
        Verify.SafeListLine(22, 1, 15, "the parentheses, braces and brackets of a documentation comment ID come in pairs"),
        Verify.SafeListLine(23, 1, 17, "the parentheses, braces and brackets of a documentation comment ID come in pairs"),
        Verify.SafeListLine(24, 1, 14, "a name in a documentation comment ID has no empty part"),
        Verify.SafeListLine(25, 1, 27, "only a method or a property has a parameter list"));

    [TestMethod]
    public Task ForTypeParameters_EntryVouchesOnlyForTheNamedTypeParameters() => Verify.AnalyzerAsync(
        $$"""
        using System.Collections.Generic;

        class C
        {
            void M(RwLock rwLock, Dictionary<int, Guard> byNumber, Dictionary<Guard, int> byGuard, Dictionary<Guard, Guard> both)
            {
                // A non-copyable value.
                byNumber.Add(1, rwLock.Enter());
                if (byNumber.Remove(1, out Guard removed)) { }

                // A non-copyable key: the entry does not vouch for it, so the member counts as not listed.
                {|#0:byGuard.Add(rwLock.Enter(), 1)|};
                {|#1:both.Add(rwLock.Enter(), rwLock.Enter())|};

                // A container counts like a non-copyable type.
                var lists = new Map<int, List<Guard>>();
                lists.Add(1, new List<Guard>());
                var keys = new Map<List<Guard>, int>();
                {|#2:keys.Add(new List<Guard>(), 1)|};

                // A type parameter of the member itself can be named too.
                var map = new Map<int, int>();
                map.Convert(rwLock.Enter());
            }
        }

        {{Library}}

        {{Sources.Guard}}
        """,
        """
        M:System.Collections.Generic.Dictionary`2.Add(`0,`1);for=TValue
        M:System.Collections.Generic.Dictionary`2.Remove(`0,`1@);new;for=TValue
        M:Map`2.#ctor;for=TKey,TValue
        M:Map`2.Add(`0,`1);for=TValue
        M:Map`2.Convert``1(``0);for= TOther
        """,
        Verify.MemberListedForOtherTypeParameter(0, "Dictionary<TKey, TValue>.Add(TKey, TValue)", "Guard", "TKey", "TValue"),
        Verify.MemberListedForOtherTypeParameter(1, "Dictionary<TKey, TValue>.Add(TKey, TValue)", "Guard", "TKey", "TValue"),
        Verify.OpaqueTypeArgumentListedForOther(2, "Map<TKey, TValue>.Add(TKey, TValue)", "System.Collections.Generic.List<Guard>", "TKey", "TValue"));

    [TestMethod]
    public Task ForTypeParameters_NameThatTheMemberDoesNotHaveIsReported() => Verify.AnalyzerAsync(
        $$"""
        class C
        {
            void M(RwLock rwLock, Map<int, Guard> map, System.Collections.Generic.Dictionary<int, Guard> dictionary)
            {
                dictionary.Add(1, rwLock.Enter());

                // The line of the entry cannot be parsed, so the member is not listed.
                {|#0:map.Add(1, rwLock.Enter())|};
            }
        }

        {{Library}}

        {{Sources.Guard}}
        """,
        """
        M:System.Collections.Generic.Dictionary`2.Add(`0,`1);for=TValue
        M:System.Collections.Generic.Dictionary`2.Remove(`0,`1@);for=Value
        M:Map`2.Add(`0,`1);for=TValue,T
        M:Map`2.Convert``1(``0);for=TOther,TKey
        M:Map`2.Convert``1(``0,System.Int32);new;for=TResult
        M:Not.Referenced`1.Member;for=Anything
        """,
        Verify.UnlistedMember(0, "Map<TKey, TValue>.Add(TKey, TValue)", "Guard", "TValue", "M:Map`2.Add(`0,`1)"),
        Verify.SafeListLine(2, 1, 67, "'Value' is not a type parameter of the member or of a type that contains it"),
        Verify.SafeListLine(3, 1, 32, "'T' is not a type parameter of the member or of a type that contains it"));

    [TestMethod]
    public async Task BuiltInList_ParsesAndEveryEntryNamesOnePublicSymbolOfTheFramework()
    {
        string list = ReadBuiltInList();

        // As a user file the built-in list reports every line that cannot be parsed, every 'for=' name that its member does not have and every
        // 'erasable' that is not on a generic class or interface. Its 'clones' lines name classes of the desktop pack as well.
        var asUserFile = new Verify.Test {
            TestCode = "class C { }",
            ReferenceAssemblies = Verify.Test.NetReferenceAssemblies.AddPackages([new PackageIdentity("Microsoft.WindowsDesktop.App.Ref", "10.0.0")]),
        };

        asUserFile.TestState.AdditionalFiles.Add((Verify.ListFileName, list));

        // The core pack has a WindowsBase of its own with none of WPF's types, which the desktop pack's would otherwise give way to.
        asUserFile.TestState.AdditionalReferences.Add((await ResolvePackAsync("Microsoft.WindowsDesktop.App.Ref", "10.0.0")).Single(r => Path.GetFileName(r.Display) is "WindowsBase.dll"));
        asUserFile.CompilerDiagnostics = CompilerDiagnostics.None;
        await asUserFile.RunAsync();

        // The framework of .NET 10 with ASP.NET Core, whose HybridCache has entries, and that of .NET 11 for a line marked "# net11+".
        var compilation = await CreateFrameworkCompilationAsync("10.0.0");
        var compilation11 = await CreateFrameworkCompilationAsync("11.0.0");
        var unresolved = new List<string>();
        int count = 0;

        foreach (string line in list.Split('\n'))
        {
            string entry = line.Trim();

            if (entry.Length is 0 || entry[0] is '#')
                continue;

            string id = entry.Split(';', ' ')[0];
            count++;

            var symbols = DocumentationCommentId.GetSymbolsForDeclarationId(id, entry.EndsWith("# net11+", StringComparison.Ordinal) ? compilation11 : compilation);

            // A protected member is one that a class of another assembly that derives from the class can call.
            if (symbols is not [{ DeclaredAccessibility: Accessibility.Public or Accessibility.Protected or Accessibility.ProtectedOrInternal }])
                unresolved.Add(id);
        }

        Assert.AreEqual(877, count);
        Assert.IsEmpty(unresolved, "Entries that do not name one public symbol of .NET 10: " + string.Join(", ", unresolved));
    }

    // A member that the list names for other type parameters than the one that a non-copyable type or a container is bound to gets a
    // warning that says so, and no advice to add a line: a line of a file can claim less of a member than the built-in list, not more. A
    // line that would name a member of the built-in list for more type parameters than it does has no effect, gets a warning, and leaves
    // the warnings of the uses as they were; one that repeats the built-in line gets none. (QA9 round 6, 3)
    [TestMethod]
    [DataRow("")]
    [DataRow("M:System.Collections.Generic.Dictionary`2.#ctor")]
    [DataRow("M:System.Collections.Generic.Dictionary`2.#ctor;for=TKey")]
    [DataRow("M:System.Collections.Generic.Dictionary`2.#ctor;for=TValue")]
    public async Task MemberListedForOtherTypeParameter_SaysSoAndALineCannotWidenIt(string line)
    {
        var test = new Verify.Test {
            TestCode = """
                using System.Collections.Generic;

                class C
                {
                    void Uses()
                    {
                        _ = {|#0:new Dictionary<Guard, string>()|};
                        _ = {|#1:new Dictionary<List<Guard>, int>()|};
                        _ = new Dictionary<int, Guard>();
                    }
                }
                """ + System.Environment.NewLine + Sources.Guard,
        };

        if (line.Length > 0)
            test.TestState.AdditionalFiles.Add((Verify.ListFileName, line));

        test.ExpectedDiagnostics.Add(Verify.MemberListedForOtherTypeParameter(0, "Dictionary<TKey, TValue>.Dictionary()", "Guard", "TKey", "TValue"));
        test.ExpectedDiagnostics.Add(Verify.OpaqueTypeArgumentListedForOther(1, "Dictionary<TKey, TValue>.Dictionary()", "System.Collections.Generic.List<Guard>", "TKey", "TValue"));

        if (line.EndsWith("#ctor", StringComparison.Ordinal))
            test.ExpectedDiagnostics.Add(Verify.SafeListWidensBuiltIn(1, line.Length + 1, "M:System.Collections.Generic.Dictionary`2.#ctor", "every type parameter", "TValue"));
        else if (line.EndsWith("TKey", StringComparison.Ordinal))
            test.ExpectedDiagnostics.Add(Verify.SafeListWidensBuiltIn(1, line.Length + 1, "M:System.Collections.Generic.Dictionary`2.#ctor", "'TKey'", "TValue"));

        await test.RunAsync();
    }

    // The warning for a line that would widen a built-in line belongs in the build whose author can change the file: a file under the
    // NuGet package root came in a package, and the library's own build reads it from its folder, so a project that uses the library
    // gets none for it. The line is left out all the same. A build with no package root counts every file as its own. (QA9 round 7, 2)
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task WideningLine_GetsItsWarningOnlyInTheBuildThatOwnsTheFile(bool inPackage)
    {
        const string Line = "M:System.Collections.Generic.Dictionary`2.#ctor";
        string path = inPackage ? "/packages/lib/1.0.0/build/NonCopyableSymbols.Lib.txt" : Verify.ListFileName;
        var test = new Verify.Test {
            TestCode = """
                using System.Collections.Generic;

                class C
                {
                    void Uses() => _ = {|#0:new Dictionary<Guard, string>()|};
                }
                """ + System.Environment.NewLine + Sources.Guard,
        };

        test.TestState.AnalyzerConfigFiles.Add(("/.globalconfig", "is_global = true" + "\n" + "build_property.NuGetPackageRoot = /packages/" + "\n"));
        test.TestState.AdditionalFiles.Add((path, Line));
        test.ExpectedDiagnostics.Add(Verify.MemberListedForOtherTypeParameter(0, "Dictionary<TKey, TValue>.Dictionary()", "Guard", "TKey", "TValue"));

        if (!inPackage)
            test.ExpectedDiagnostics.Add(Verify.SafeListWidensBuiltIn(1, Line.Length + 1, Line, "every type parameter", "TValue"));

        await test.RunAsync();
    }

    // The lines that say 'clones=complete' for a class that derives from a class that clones itself name, on the reference packs that a
    // build uses, exactly the public classes that can be derived from and whose base chain reaches a class whose line says
    // 'clones=complete', wherever the pack puts them: UTF8Encoding is in another assembly than Encoding there. FrameworkScanTests
    // checks the same lines against the IL that it scans.
    [TestMethod]
    [DataRow("10.0.0")]
    [DataRow("11.0.0")]
    public async Task CompleteLinesOfDerivedClasses_AreTheClassesOfTheReferencePacksThatDeriveFromACompleteClass(string version)
    {
        var compilation = await CreateFrameworkCompilationAsync(version);
        var entries = ReadBuiltInList().Split('\n').Select(l => l.Split('#')[0].Trim()).Where(l => l.Length > 0).ToList();
        var cloning = entries.Where(e => e.StartsWith("T:", StringComparison.Ordinal) && e.Contains(";clones", StringComparison.Ordinal)).Select(e => e[2..e.IndexOf(';', StringComparison.Ordinal)]).ToHashSet(StringComparer.Ordinal);
        var complete = entries.Where(e => e.StartsWith("T:", StringComparison.Ordinal) && e.EndsWith(";clones=complete", StringComparison.Ordinal)).Select(e => "T:" + e[2..^16]).ToHashSet(StringComparer.Ordinal);

        // The classes that clone themselves whose line says 'clones=complete': no class above them has a 'clones' line.
        var roots = complete
            .Select(c => compilation.GetTypeByMetadataName(c[2..]))
            .OfType<INamedTypeSymbol>()
            .Where(t => !BaseChain(t).Any(b => cloning.Contains(b.GetDocumentationCommentId()![2..])))
            .ToHashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

        Assert.IsNotEmpty(roots);
        var expected = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var type in AllTypes(compilation.GlobalNamespace))
        {
            if (type is not { TypeKind: TypeKind.Class, IsSealed: false, DeclaredAccessibility: Accessibility.Public } || roots.Contains(type))
                continue;

            if (BaseChain(type).Any(roots.Contains))
                expected.Add(type.GetDocumentationCommentId()!);
        }

        var shipped = complete.Except(roots.Select(r => r.GetDocumentationCommentId()!)).ToHashSet(StringComparer.Ordinal);

        Assert.IsTrue(expected.SetEquals(shipped), $"The reference packs of {version} have these classes: {string.Join(", ", expected)}");

        static IEnumerable<INamedTypeSymbol> BaseChain(INamedTypeSymbol type)
        {
            for (var current = type.BaseType; current is not null; current = current.BaseType)
                yield return current;
        }

        static IEnumerable<INamedTypeSymbol> AllTypes(INamespaceSymbol ns)
        {
            foreach (var type in ns.GetTypeMembers())
                yield return type;

            foreach (var child in ns.GetNamespaceMembers())
            {
                foreach (var type in AllTypes(child))
                    yield return type;
            }
        }
    }

    private static async Task<CSharpCompilation> CreateFrameworkCompilationAsync(string version)
    {
        var references = new Dictionary<string, MetadataReference>(StringComparer.OrdinalIgnoreCase);

        // The classes that clone themselves include some of WPF, Windows Forms and System.Drawing, which are in the desktop pack. Its
        // WindowsBase has WPF's types, where the one of the core pack has none, so a later pack's assembly takes the place of an earlier one.
        foreach (string pack in new[] { "Microsoft.NETCore.App.Ref", "Microsoft.AspNetCore.App.Ref", "Microsoft.WindowsDesktop.App.Ref" })
        {
            foreach (var reference in await ResolvePackAsync(pack, version))
                references[Path.GetFileName(reference.Display)!] = reference;
        }

        return CSharpCompilation.Create("Test", references: references.Values);
    }

    private static async Task<System.Collections.Immutable.ImmutableArray<MetadataReference>> ResolvePackAsync(string pack, string version)
    {
        string target = "net" + version[..version.IndexOf('.', StringComparison.Ordinal)] + ".0";
        var assemblies = new ReferenceAssemblies(target, new PackageIdentity(pack, version), Path.Combine("ref", target));
        return await assemblies.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
    }

    [TestMethod]
    public void Diagnostics_EveryDescriptorOfTheRulesIsSupported()
    {
        // The class is internal to the analyzer.
        var declared = typeof(DoNotCopyValueAnalyzer).Assembly.GetType("Singulink.CodeAnalysis.Analyzers.Rules", throwOnError: true)!
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            .Where(field => field.FieldType == typeof(DiagnosticDescriptor))
            .ToList();

        var supported = new DoNotCopyValueAnalyzer().SupportedDiagnostics;

        Assert.IsNotEmpty(declared);
        Assert.HasCount(declared.Count, supported);

        foreach (var field in declared)
            Assert.IsTrue(supported.Any(descriptor => ReferenceEquals(descriptor, field.GetValue(null))), $"Rules.{field.Name} is not in Rules.All.");
    }

    [TestMethod]
    public void Diagnostics_HaveAHelpLinkToTheArticleOfTheirId()
    {
        var descriptors = new DoNotCopyValueAnalyzer().SupportedDiagnostics;

        string[] ids = [
            "SCA1001", "SCA1002", "SCA1003", "SCA1004", "SCA1005", "SCA1006", "SCA1007", "SCA1008", "SCA1009", "SCA1010", "SCA1011", "SCA1012", "SCA1013", "SCA1014", "SCA1015", "SCA1020", "SCA1021",
            "SCA1022", "SCA1023", "SCA1024", "SCA1025", "SCA1030", "SCA1031", "SCA1032", "SCA1033", "SCA1040", "SCA1041", "SCA1042", "SCA1043", "SCA1044", "SCA1045",
            "SCA1046", "SCA1047", "SCA1048",
        ];

        CollectionAssert.AreEquivalent(ids, descriptors.Select(d => d.Id).Distinct().ToArray());

        foreach (var descriptor in descriptors)
        {
            Assert.AreEqual(
                "https://www.singulink.com/Docs/Singulink.CodeAnalysis/articles/diagnostics/" + descriptor.Id.ToLowerInvariant() + ".html",
                descriptor.HelpLinkUri);
            Assert.AreEqual("Reliability", descriptor.Category);
            Assert.AreEqual(DiagnosticSeverity.Warning, descriptor.DefaultSeverity);
            Assert.IsTrue(descriptor.IsEnabledByDefault);
        }
    }

    // The help link of a diagnostic leads to the article of its ID in the diagnostics section of the documentation site, so every ID has
    // an article there, every article there is the article of an ID, and the table of contents of the section lists each in order of ID.
    [TestMethod]
    public void Diagnostics_HaveAnArticleOfTheirIdAndNoOtherArticleIsThere()
    {
        string folder = FindDiagnosticsArticles();
        string[] ids = [.. new DoNotCopyValueAnalyzer().SupportedDiagnostics.Select(d => d.Id.ToLowerInvariant()).Distinct().Order(StringComparer.Ordinal)];
        string[] articles = [.. Directory.GetFiles(folder, "*.md").Select(Path.GetFileNameWithoutExtension).Order(StringComparer.Ordinal)!];

        foreach (string id in ids)
            Assert.Contains(id, articles, $"The diagnostic {id} has no article in Docs/articles/diagnostics.");

        foreach (string article in articles)
            Assert.Contains(article, ids, $"The article {article}.md in Docs/articles/diagnostics is not the article of a diagnostic.");

        string[] listed = [.. File.ReadAllLines(Path.Combine(folder, "toc.yml"))
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("href:", StringComparison.Ordinal))
            .Select(line => line["href:".Length..].Trim())];

        CollectionAssert.AreEqual(ids.Select(id => id + ".md").ToArray(), listed, "The toc.yml of the diagnostics lists every article once, in order of ID.");

        static string FindDiagnosticsArticles()
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            {
                string candidate = Path.Combine(directory.FullName, "Docs", "articles", "diagnostics");

                if (Directory.Exists(candidate))
                    return candidate;
            }

            throw new DirectoryNotFoundException("No Docs/articles/diagnostics folder above the test assembly.");
        }
    }

    [TestMethod]
    public Task Marker_OnSomethingThatItDoesNotFitIsReported() => Verify.AnalyzerAsync(
        """
        class AClass { }

        interface IInterface { }

        struct AStruct { }

        struct AView<T> { }
        """,
        """
        T:AClass;view
        T:IInterface;noncopyable
        T:System.String;noncopyable
        T:AStruct;noncopyable
        T:AView`1;view
        T:Not.Referenced;view
        """,
        Verify.SafeListLine(1, 1, 14, "the marker 'view' is for a struct"),
        Verify.SafeListLine(2, 1, 25, "the marker 'noncopyable' is for a struct"),
        Verify.SafeListLine(3, 1, 28, "the marker 'noncopyable' is for a struct"));

    // A mistyped ID on a 'noncopyable' line leaves the type copyable in a build that looks clean, so that line is reported where it names
    // nothing. Every other marker on a line that names nothing only gives more findings, and is ignored, so that one file can serve several
    // projects.
    [TestMethod]
    public Task NonCopyableLine_ThatNamesNoTypeIsReported() => Verify.AnalyzerAsync(
        """
        struct Handle { }
        """,
        """
        T:Hnadle;noncopyable
        T:Not.Referenced;view
        M:Not.Referenced.Method;new
        T:Handle;noncopyable
        """,
        Verify.SafeListNonCopyableNamesNothing(1, 1, 21, "T:Hnadle"));

    [TestMethod]
    public Task Borrows_AStoredValueMayBePassedByValue() => Verify.AnalyzerAsync(
        $$"""
        using System.Collections.Generic;
        using System.Runtime.InteropServices;

        static class Native
        {
            public static int Peek(Guard guard) => guard.Value;

            public static int Keep(Guard guard) => guard.Value;

            public static int PeekGeneric<T>(T value) => 0;
        }

        class C
        {
            Guard _guard;
            GCHandle _handle;

            void M(List<Guard> list)
            {
                // A member that a list marks 'borrows' reads the copy during the call and keeps nothing of it. It need not be generic.
                int a = Native.Peek(_guard);
                int b = Native.PeekGeneric(_guard);

                // Any other member is given a copy that it may keep, also one that a list names with no marker or with 'new'.
                int c = Native.Keep({|#0:_guard|});
                list.Add({|#1:_guard|});

                // A user file cannot loosen the built-in list, and can tighten it: where two entries disagree, no marker wins.
                nint d = GCHandle.ToIntPtr({|#2:_handle|});
            }
        }

        {{Sources.Guard}}
        """,
        """
        M:Native.Peek(Guard);borrows
        M:Native.PeekGeneric``1(``0);borrows
        M:System.Collections.Generic.List`1.Add(`0);borrows
        M:System.Runtime.InteropServices.GCHandle.ToIntPtr(System.Runtime.InteropServices.GCHandle)
        T:Guard;borrows
        P:System.Collections.Generic.List`1.Count;borrows;new
        """,
        Verify.CopyOfStoredValue(0, "'Native.Keep(Guard)' receives", "Guard"),
        Verify.CopyOfStoredValue(1, "'List<Guard>.Add(Guard)' receives", "Guard"),
        Verify.CopyOfStoredValue(2, "'GCHandle.ToIntPtr(GCHandle)' receives", "System.Runtime.InteropServices.GCHandle"),
        Verify.SafeListLine(5, 1, 16, "the marker 'borrows' is for a member; a type takes 'view', 'noncopyable', 'copyable', 'erasable' or 'clones'"),
        Verify.SafeListLine(6, 1, 54, "an entry has one marker, which 'for=' can follow"));

    private static string ReadBuiltInList()
    {
        using var stream = typeof(DoNotCopyValueAnalyzer).Assembly.GetManifestResourceStream("Singulink.CodeAnalysis.Analyzers.NonCopyableSymbols.txt");
        Assert.IsNotNull(stream);

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    // A name after 'for=' goes by the name alone, so it must name one type parameter: where a member and a containing type, or two
    // containing types, have type parameters of the same name, the line is reported and left out.
    [TestMethod]
    public Task ForTypeParameters_NameThatFitsMoreThanOneTypeParameterIsReported() => Verify.AnalyzerAsync(
        $$"""
        namespace ThirdParty
        {
            public class Outer<T>
            {
                public static T? Stash;
                public static T? Copy;

        #pragma warning disable CS0693
                public sealed class Inner<T>
        #pragma warning restore CS0693
                {
                    // Copies a value of the 'T' of 'Outer', never one of its own.
                    public void Dup() => Copy = Stash;
                }

                public sealed class Other<TOther>
                {
                    public void Dup() => Copy = Stash;
                }
            }

            public class Holder<T>
            {
        #pragma warning disable CS0693
                public void Touch<T>() { }
        #pragma warning restore CS0693

                public void Keep<TOther>() { }
            }
        }

        class C
        {
            void M()
            {
                {|#1:{|#0:new ThirdParty.Outer<Guard>.Inner<int>()|}.Dup()|};
                new ThirdParty.Outer<Guard>.Other<int>().Dup();
                {|#2:new ThirdParty.Holder<Guard>().Touch<int>()|};
                new ThirdParty.Holder<Guard>().Keep<int>();
            }
        }

        {{Sources.Guard}}
        """,
        """
        M:ThirdParty.Outer`1.Inner`1.#ctor;for=T
        M:ThirdParty.Outer`1.Inner`1.Dup;for=T
        M:ThirdParty.Outer`1.Other`1.#ctor;for=T
        M:ThirdParty.Outer`1.Other`1.Dup;for=T
        M:ThirdParty.Holder`1.#ctor
        M:ThirdParty.Holder`1.Touch``1;for=T
        M:ThirdParty.Holder`1.Keep``1;for=T
        """,
        Verify.UnlistedMember(0, "Outer<T>.Inner<T>.Inner()", "Guard", "T", "M:ThirdParty.Outer`1.Inner`1.#ctor"),
        Verify.UnlistedMember(1, "Outer<T>.Inner<T>.Dup()", "Guard", "T", "M:ThirdParty.Outer`1.Inner`1.Dup"),
        Verify.UnlistedMember(2, "Holder<T>.Touch<T>()", "Guard", "T", "M:ThirdParty.Holder`1.Touch``1"),
        Verify.SafeListLine(1, 1, 41, "'T' names more than one type parameter of the member and of the types that contain it"),
        Verify.SafeListLine(2, 1, 39, "'T' names more than one type parameter of the member and of the types that contain it"),
        Verify.SafeListLine(6, 1, 37, "'T' names more than one type parameter of the member and of the types that contain it"));

    // A member of an extension block is listed under the ID of its implementation method: the static method that the compiler makes
    // for it, with the receiver as its first parameter and the type parameters of the block before its own. The ID of the member
    // inside the block has a generated name. A finding that asks for an entry names the implementation method.
    [TestMethod]
    public Task UserFile_ListsAMemberOfAnExtensionBlockByItsImplementationMethod() => Verify.AnalyzerAsync(
        $$"""
        public sealed class Bag<T>
        {
            public T[] Items = new T[4];
        }

        public static class BagExtensions
        {
            extension<T>(Bag<T> bag)
            {
                public void Touch() { }

                public void TouchWith<U>(ref U other) { }

                public void Unlisted() { }

                public int Size => bag.Items.Length;
            }
        }

        class C
        {
            Guard _guard;

            void M(Bag<Guard> bag, Bag<int> numbers)
            {
                // The receiver is a parameter whose type mentions the type parameter, which is not marked: nothing checks what the
                // member does with the bag, so it is taken to keep it. The entry of the member can say that it only borrows.
                {|#2:bag|}.Touch();
                numbers.TouchWith(ref _guard);
                int size = bag.Size;

                // The entry of 'TouchWith' vouches for 'U' only.
                {|#0:bag.TouchWith(ref _guard)|};

                {|#1:bag.Unlisted()|};
            }
        }

        {{Sources.Guard}}
        """,
        """
        M:BagExtensions.Touch``1(Bag{``0})
        M:BagExtensions.TouchWith``2(Bag{``0},``1@);for=U
        M:BagExtensions.get_Size``1(Bag{``0});for=T
        """,
        Verify.MemberListedForOtherTypeParameter(0, "BagExtensions.extension<T>(Bag<T>).TouchWith<U>(ref U)", "Guard", "T", "U"),
        Verify.UnlistedMember(1, "BagExtensions.extension<T>(Bag<T>).Unlisted()", "Guard", "T", "M:BagExtensions.Unlisted``1(Bag{``0})"),
        Verify.PassesParameterToUncheckedCode(2, "C.M(Bag<Guard>, Bag<int>)", "bag", "Bag<Guard>", "BagExtensions.extension<Guard>(Bag<Guard>).Touch()", "M:BagExtensions.Touch``1(Bag{``0})"));

    // A parameter whose type mentions a type parameter that is not marked is taken to keep the container that it is given, and the
    // entry of the member says where it does not: 'borrows' on a member of the user's own file exempts it as it does a member of
    // the built-in list. That holds for a parameter, for a local, and for an inherited member that implements an interface member.
    [TestMethod]
    public Task UserFile_BorrowsSaysThatAMemberKeepsNothingOfAContainer() => Verify.AnalyzerAsync(
        $$"""
        using System.Collections.Generic;
        using Singulink.CodeAnalysis;

        public static class Helpers
        {
            public static int CountHeld<T>(this List<T> items) => items.Count;

            public static int CountAll<T>(this List<T> items) => items.Count;

            public static int CountSome<T>(this List<T> items) => items.Count;
        }

        public class Pool<T>
        {
            public void Return(T[] items) { }

            public void Look(T[] items) { }
        }

        public interface IGuardReturner
        {
            void Return(Guard[] items);
        }

        public interface IGuardLooker
        {
            void Look(Guard[] items);
        }

        public sealed class {|#3:GuardPool|} : Pool<Guard>, IGuardReturner, IGuardLooker;

        class C
        {
            static int Borrowed(List<Guard> guards) => guards.CountHeld();

            static int Listed(List<Guard> guards) => {|#0:guards|}.CountAll();

            // A use of a member that is not listed is reported as that, and not again as keeping.
            static int Unlisted(List<Guard> guards) => {|#1:guards.CountSome()|};

            static List<Guard> MakeAndBorrow(RwLock rwLock)
            {
                var guards = new List<Guard>();
                guards.Add(rwLock.Enter());
                int count = guards.CountHeld();
                return guards;
            }

            static List<Guard> MakeAndPass(RwLock rwLock)
            {
                var guards = new List<Guard>();
                guards.Add(rwLock.Enter());
                int count = guards.CountAll();
                return {|#2:guards|};
            }
        }

        {{Sources.Guard}}
        """,
        """
        M:Helpers.CountHeld``1(System.Collections.Generic.List{``0});borrows
        M:Helpers.CountAll``1(System.Collections.Generic.List{``0})
        M:Pool`1.Return(`0[])
        M:Pool`1.Look(`0[]);borrows
        """,
        Verify.PassesParameterToUncheckedCode(
            0, "C.Listed(List<Guard>)", "guards", "System.Collections.Generic.List<Guard>", "Helpers.CountAll<Guard>(List<Guard>)", "M:Helpers.CountAll``1(System.Collections.Generic.List{``0})"),
        Verify.UnlistedMember(1, "Helpers.CountSome<T>(List<T>)", "Guard", "T", "M:Helpers.CountSome``1(System.Collections.Generic.List{``0})"),
        Verify.UnownedContainerReturned(
            2,
            "C.MakeAndPass(RwLock)",
            "System.Collections.Generic.List<Guard>",
            "'guards' is not the method's own anywhere in the method, because on line 53 it is passed to 'Helpers.CountAll<Guard>(List<Guard>)', which is taken to keep it: the type of its parameter mentions a type parameter that is not marked"),
        Verify.KeepingNotOnInterfaceMember(3, "items", "Pool<T>.Return(T[])", "IGuardReturner.Return(Guard[])", "GuardPool", byUncheckedCode: true));

    // The built-in list has the types that hand a value over between tasks and threads: a write moves the value in, a read takes it
    // out and is a new value, and a peek or an enumerator hands out a copy of what the collection still holds. What copies every
    // element, and a sequence that takes each element out as it is enumerated, are not on the list.
    [TestMethod]
    public Task BuiltInList_HasChannelsAndTheConcurrentCollections() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using System.Collections.Concurrent;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Channels;
        using System.Threading.Tasks;
        using Singulink.CodeAnalysis;

        sealed class Pipeline
        {
            private readonly Channel<Guard> _channel = Channel.CreateBounded<Guard>(4);
            private readonly Channel<Guard> _unbounded = Channel.CreateUnbounded<Guard>();
            private readonly ConcurrentQueue<Guard> _queue = new();
            private readonly ConcurrentStack<Guard> _stack = new();
            private readonly ConcurrentBag<Guard> _bag = new();
            private readonly ConcurrentDictionary<int, Guard> _held = new();
            private readonly BlockingCollection<Guard> _blocking = new(8);
            private readonly Dictionary<int, Guard> _map = new();

            async Task ProduceAsync(RwLock rwLock)
            {
                await _channel.Writer.WriteAsync(rwLock.Enter());

                if (await _unbounded.Writer.WaitToWriteAsync())
                    _unbounded.Writer.TryWrite(rwLock.Enter());

                _queue.Enqueue(rwLock.Enter());
                _stack.Push(rwLock.Enter());
                _bag.Add(rwLock.Enter());
                _held.TryAdd(1, rwLock.Enter());
                _held[2] = rwLock.Enter();
                _blocking.Add(rwLock.Enter());
                _blocking.TryAdd(rwLock.Enter(), 100);
                _channel.Writer.Complete();
                _blocking.CompleteAdding();
            }

            async Task ConsumeAsync()
            {
                ChannelReader<Guard> reader = _channel;

                while (await reader.WaitToReadAsync())
                {
                    while (reader.TryRead(out var guard))
                        guard.Dispose();
                }

                Guard next = await _unbounded.Reader.ReadAsync();
                next.Dispose();

                if (_queue.TryDequeue(out var a))
                    a.Dispose();

                if (_stack.TryPop(out var b))
                    b.Dispose();

                if (_bag.TryTake(out var c))
                    c.Dispose();

                if (_held.TryRemove(1, out var d))
                    d.Dispose();

                Guard e = _blocking.Take();
                e.Dispose();

                if (_blocking.TryTake(out var f, 100))
                    f.Dispose();

                var popped = new Guard[4];
                int count = _stack.TryPopRange(popped);

                foreach (ref var guard in popped.AsSpan(0, count))
                    guard.Dispose();
            }

            // A sequence that takes each element out of its source as it hands it out: what it hands out is new.
            async Task DrainAsync(CancellationToken cancellationToken)
            {
                await foreach (var guard in _channel.Reader.ReadAllAsync())
                    guard.Dispose();

                await foreach (var guard in _unbounded.Reader.ReadAllAsync(cancellationToken))
                    guard.Dispose();

                foreach (var guard in _blocking.GetConsumingEnumerable())
                    guard.Dispose();

                foreach (var guard in _blocking.GetConsumingEnumerable(cancellationToken))
                    guard.Dispose();
            }

            // The keys of a dictionary are read with no copy of a value.
            void ReleaseAll()
            {
                int[] keys = [.. _map.Keys];

                foreach (int key in keys)
                {
                    if (_map.Remove(key, out var guard))
                        guard.Dispose();
                }

                foreach (int key in _held.Keys)
                {
                    if (_held.TryRemove(key, out var guard))
                        guard.Dispose();
                }

                bool any = _map.Keys.Count > 0 && !_queue.IsEmpty && _held.ContainsKey(1) && _channel.Reader.CanCount;
            }

            // What hands out a value that the collection still holds is a copy.
            void Copies()
            {
                _queue.TryPeek({|#0:out var a|});
                _channel.Reader.TryPeek({|#1:out var b|});
                _held.TryGetValue(1, {|#2:out var c|});
                Guard d = {|#3:_held.GetOrAdd(1, default(Guard))|};
                Guard e = {|#4:_held[1]|};

                foreach (var guard in {|#5:_bag|})
                {
                }
            }

            // And these are not on the list.
            async Task NotListedAsync()
            {
                await Task.Yield();
                Guard[] all = {|#6:_queue.ToArray()|};
                {|#7:_held.TryUpdate(1, default, default)|};
            }
        }

        {{Sources.Guard}}
        """,
        Verify.ReadFromStoredContainer(0, "'ConcurrentQueue<T>.TryPeek(out T)'", "Guard"),
        Verify.ReadFromStoredContainer(1, "'ChannelReader<T>.TryPeek(out T)'", "Guard"),
        Verify.ReadFromStoredContainer(2, "'ConcurrentDictionary<TKey, TValue>.TryGetValue(TKey, out TValue)'", "Guard"),
        Verify.ReadFromStoredContainer(3, "'ConcurrentDictionary<TKey, TValue>.GetOrAdd(TKey, TValue)'", "Guard"),
        Verify.ReadFromStoredContainer(4, "'ConcurrentDictionary<TKey, TValue>.this[TKey]'", "Guard"),
        Verify.ReadFromStoredContainer(5, "'foreach'", "Guard"),
        Verify.UnlistedMember(6, "ConcurrentQueue<T>.ToArray()", "Guard", "T", "M:System.Collections.Concurrent.ConcurrentQueue`1.ToArray"),
        Verify.UnlistedMember(7, "ConcurrentDictionary<TKey, TValue>.TryUpdate(TKey, TValue, TValue)", "Guard", "TValue", "M:System.Collections.Concurrent.ConcurrentDictionary`2.TryUpdate(`0,`1,`1)"));

    // The marker 'drains' speaks of the sequence that the member returns, and of nothing else. What it must not allow:
    // an element that is read twice by hand from an enumerator, a sequence that is stored and then read like any stored container,
    // and a sequence of containers, each of which whoever wrote it may still hold. An ordinary enumeration, which hands out what the
    // collection keeps, stays a copy.
    [TestMethod]
    public Task Drains_SaysOnlyThatTheSequenceIsNew() => Verify.AnalyzerAsync(
        $$"""
        using System.Collections.Concurrent;
        using System.Collections.Generic;
        using System.Threading.Channels;
        using System.Threading.Tasks;

        sealed class Pipeline
        {
            private readonly Channel<Guard> _channel = Channel.CreateUnbounded<Guard>();
            private readonly Channel<List<Guard>> _batches = Channel.CreateUnbounded<List<Guard>>();
            private readonly BlockingCollection<Guard> _blocking = new();
            private readonly BlockingCollection<List<Guard>> _batchList = new();
            private readonly ConcurrentQueue<Guard> _queue = new();
            private IAsyncEnumerable<Guard>? _stored;

            // The enumerator holds its current element until it moves on: a second read by hand is a second copy.
            async Task ReadTwiceByHandAsync()
            {
                var enumerator = _channel.Reader.ReadAllAsync().GetAsyncEnumerator();

                if (await enumerator.MoveNextAsync())
                {
                    Guard first = {|#0:enumerator.Current|};
                    Guard second = enumerator.Current;
                    first.Dispose();
                    second.Dispose();
                }
            }

            void ReadTwiceByHand()
            {
                using var enumerator = _blocking.GetConsumingEnumerable().GetEnumerator();

                if (enumerator.MoveNext())
                {
                    Guard first = {|#1:enumerator.Current|};
                    Guard second = {|#2:enumerator.Current|};
                }
            }

            // A sequence in a field is a stored container like any other, whoever made it. Two holders that enumerate it would each
            // get elements of their own, but nothing tells the field from one that holds an ordinary sequence.
            void Store() => _stored = _channel.Reader.ReadAllAsync();

            async Task EnumerateStoredAsync()
            {
                await foreach (var guard in {|#3:_stored|}!)
                    guard.Dispose();
            }

            // In a local the first of two enumerations is not the last use of the local, so it reads from a stored value.
            async Task EnumerateTwiceAsync()
            {
                var sequence = _channel.Reader.ReadAllAsync();

                await foreach (var guard in {|#4:sequence|})
                    guard.Dispose();

                await foreach (var guard in sequence)
                    guard.Dispose();
            }

            // A container that is taken out of a channel is a reference that whoever wrote it may still hold, as with 'new'.
            async Task DrainBatchesAsync()
            {
                await foreach (var batch in _batches.Reader.ReadAllAsync())
                {
                    Guard first = {|#5:batch[0]|};
                    first.Dispose();
                }
            }

            // So a method that returns such a sequence, or a list that is made of one, hands out what something else may still hold.
            // A sequence of values is the method's to return.
            IAsyncEnumerable<List<Guard>> ReturnBatches() => {|#8:_batches.Reader.ReadAllAsync()|};

            List<List<Guard>> ReturnBatchesAsAList() => {|#9:[.. _batchList.GetConsumingEnumerable()]|};

            IEnumerable<Guard> ReturnGuards() => _blocking.GetConsumingEnumerable();

            IAsyncEnumerable<Guard> ReturnGuardsAsync() => _channel.Reader.ReadAllAsync();

            // An ordinary enumeration hands out what the collection still holds.
            void Enumerate()
            {
                foreach (var guard in {|#6:_queue|})
                {
                }

                using var enumerator = _queue.GetEnumerator();

                if (enumerator.MoveNext())
                {
                    Guard current = {|#7:enumerator.Current|};
                }
            }
        }

        {{Sources.Guard}}
        """,
        Verify.ReadFromStoredContainer(0, "'IAsyncEnumerator<T>.Current'", "Guard"),
        Verify.ReadFromStoredContainer(1, "'IEnumerator<T>.Current'", "Guard"),
        Verify.ReadFromStoredContainer(2, "'IEnumerator<T>.Current'", "Guard"),
        Verify.ReadFromStoredContainer(3, "'foreach'", "Guard"),
        Verify.ReadFromStoredContainer(4, "'foreach'", "Guard"),
        Verify.ReadFromStoredContainer(5, "'List<T>.this[int]'", "Guard", because: "'batch' is not the method's own anywhere in the method, because on line 65 it is given what something else holds"),
        Verify.ReadFromStoredContainer(6, "'foreach'", "Guard"),
        Verify.ReadFromStoredContainer(7, "'IEnumerator<T>.Current'", "Guard"),
        Verify.UnownedContainerReturned(8, "Pipeline.ReturnBatches()", "System.Collections.Generic.IAsyncEnumerable<System.Collections.Generic.List<Guard>>"),
        Verify.UnownedContainerReturned(9, "Pipeline.ReturnBatchesAsAList()", "System.Collections.Generic.List<System.Collections.Generic.List<Guard>>"));

    // A library says the same of its own members in a list file. The marker fits only a method whose result is a sequence: on one
    // that returns a list, which holds its elements, and on a property the line is reported. And where two entries disagree, no
    // marker wins.
    [TestMethod]
    public Task Drains_InAUserFile_IsForAMemberThatReturnsASequence() => Verify.AnalyzerAsync(
        $$"""
        using System.Collections.Generic;

        sealed class Feed<T>
        {
            public IEnumerable<T> TakeAll() => throw null!;

            public IAsyncEnumerable<T> TakeAllAsync() => throw null!;

            public IEnumerable<T> Pending => throw null!;

            public List<T> Snapshot() => throw null!;

            public T[] ToArray() => throw null!;

            public IEnumerable<T> Disputed() => throw null!;
        }

        sealed class Consumer
        {
            private readonly Feed<Guard> _feed = new();

            async System.Threading.Tasks.Task ConsumeAsync()
            {
                foreach (var guard in _feed.TakeAll())
                    guard.Dispose();

                await foreach (var guard in _feed.TakeAllAsync())
                    guard.Dispose();

                // A line that is reported is no entry, and where two entries disagree the member is listed with no marker.
                foreach (var guard in {|#0:_feed.Snapshot()|})
                {
                }

                foreach (var guard in {|#1:_feed.Disputed()|})
                {
                }

                // A property exposes state: what is read from one is stored, and the marker is not for it.
                foreach (var guard in {|#2:_feed.Pending|})
                {
                }
            }
        }

        {{Sources.Guard}}
        """,
        """
        M:Feed`1.#ctor
        M:Feed`1.TakeAll;drains
        M:Feed`1.TakeAllAsync;drains
        P:Feed`1.Pending;drains
        M:Feed`1.Snapshot;drains
        M:Feed`1.ToArray;drains
        M:Feed`1.Disputed;drains
        M:Feed`1.Disputed
        T:Feed`1;drains
        P:Some.Library.NotReferenced`1.Items;drains
        """,
        Verify.UnlistedMember(0, "Feed<T>.Snapshot()", "Guard", "T", "M:Feed`1.Snapshot"),
        Verify.ReadFromStoredContainer(1, "'foreach'", "Guard"),
        Verify.UnlistedMember(2, "Feed<T>.Pending", "Guard", "T", "P:Feed`1.Pending"),
        Verify.ReadFromStoredContainer(2, "'foreach'", "Guard", canBeMoved: false),
        Verify.SafeListLine(4, 1, 24, DrainsIsForASequence),
        Verify.SafeListLine(5, 1, 25, DrainsIsForASequence),
        Verify.SafeListLine(6, 1, 24, DrainsIsForASequence),
        Verify.SafeListLine(9, 1, 16, "the marker 'drains' is for a member; a type takes 'view', 'noncopyable', 'copyable', 'erasable' or 'clones'"),
        Verify.SafeListLine(10, 1, 44, DrainsIsForASequence));

    private const string DrainsIsForASequence = "the marker 'drains' is for a method that returns a sequence, an IEnumerable<T> or an IAsyncEnumerable<T>";

    // The same drained sequence behind ConfigureAwait and WithCancellation is the same sequence: what the configuring members hand
    // out is as new as what they were called on. So a drained sequence stays new through them, and an ordinary sequence that is read
    // from a field stays stored: the configured struct over it is a copy of a stored value where it is made.
    [TestMethod]
    public Task Drains_BehindConfigureAwaitAndWithCancellationIsTheSameSequence() => Verify.AnalyzerAsync(
        $$"""
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Channels;
        using System.Threading.Tasks;

        sealed class Pipeline
        {
            private readonly Channel<Guard> _channel = Channel.CreateUnbounded<Guard>();
            private readonly IAsyncEnumerable<Guard> _stored = null!;

            async Task DrainAsync(CancellationToken cancellationToken)
            {
                await foreach (var guard in _channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                    guard.Dispose();

                await foreach (var guard in _channel.Reader.ReadAllAsync().WithCancellation(cancellationToken))
                    guard.Dispose();

                await foreach (var guard in _channel.Reader.ReadAllAsync().WithCancellation(cancellationToken).ConfigureAwait(false))
                    guard.Dispose();

                await foreach (var guard in _channel.Reader.ReadAllAsync().ConfigureAwait(false).WithCancellation(cancellationToken))
                    guard.Dispose();
            }

            async Task StoredAsync(CancellationToken cancellationToken)
            {
                await foreach (var guard in {|#0:_stored.ConfigureAwait(false)|})
                    guard.Dispose();

                await foreach (var guard in {|#1:_stored.WithCancellation(cancellationToken)|}.ConfigureAwait(false))
                    guard.Dispose();
            }
        }

        {{Sources.Guard}}
        """,
        Verify.ReadFromStoredContainer(0, "'TaskAsyncEnumerableExtensions.ConfigureAwait<T>(IAsyncEnumerable<T>, bool)'", Configured),
        Verify.ReadFromStoredContainer(1, "'TaskAsyncEnumerableExtensions.WithCancellation<T>(IAsyncEnumerable<T>, CancellationToken)'", Configured));

    private const string Configured = "System.Runtime.CompilerServices.ConfiguredCancelableAsyncEnumerable<Guard>";

    // The built-in list has the rest of what a Dictionary<TKey, TValue> of guards is used with: the constructors with a comparer,
    // the capacity, and the alternate lookup, a view of the dictionary. What compares values and what reads one by value out of a
    // stored dictionary stays reported.
    [TestMethod]
    public Task BuiltInList_HasTheCapacityAndTheAlternateLookupOfADictionary() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using System.Collections.Generic;
        using System.Runtime.InteropServices;

        sealed class C
        {
            private readonly Dictionary<string, Guard> _held = new(StringComparer.Ordinal);
            private readonly Dictionary<string, Guard> _sized = new(16, StringComparer.Ordinal);
            private readonly Queue<Guard> _queue = new();
            private readonly Stack<Guard> _stack = new();

            void Grow(RwLock rwLock, ReadOnlySpan<char> name)
            {
                _held.EnsureCapacity(32);
                _held.TrimExcess();
                _held.TrimExcess(64);
                int room = _held.Capacity + _queue.Capacity + _stack.Capacity;
                _queue.EnsureCapacity(8);
                _queue.TrimExcess();
                _queue.TrimExcess(8);
                _stack.EnsureCapacity(8);
                _stack.TrimExcess();
                _stack.TrimExcess(8);
                IEqualityComparer<string> comparer = _sized.Comparer;

                var lookup = _held.GetAlternateLookup<ReadOnlySpan<char>>();
                lookup.TryAdd(name, rwLock.Enter());
                lookup[name] = rwLock.Enter();

                if (lookup.Remove(name, out string? key, out var removed))
                    removed.Dispose();

                if (_held.TryGetAlternateLookup<ReadOnlySpan<char>>(out var other) && other.ContainsKey(name))
                    CollectionsMarshal.GetValueRefOrNullRef(other, name).Dispose();

                bool gone = lookup.Remove(name) && lookup.Dictionary.Count == 0;
            }

            // A read by value of what the dictionary still holds is a copy, and so is a comparison of values.
            void Copies(ReadOnlySpan<char> name)
            {
                var lookup = _held.GetAlternateLookup<ReadOnlySpan<char>>();
                lookup.TryGetValue(name, {|#0:out var a|});
                Guard b = {|#1:lookup[name]|};
                bool any = {|#2:_held.ContainsValue(default)|};
            }
        }

        {{Sources.Guard}}
        """,
        Verify.ReadFromStoredContainer(0, "'Dictionary<TKey, TValue>.AlternateLookup<TAlternateKey>.TryGetValue(TAlternateKey, out TValue)'", "Guard"),
        Verify.ReadFromStoredContainer(1, "'Dictionary<TKey, TValue>.AlternateLookup<TAlternateKey>.this[TAlternateKey]'", "Guard"),
        Verify.UnlistedMember(2, "Dictionary<TKey, TValue>.ContainsValue(TValue)", "Guard", "TValue", "M:System.Collections.Generic.Dictionary`2.ContainsValue(`1)"));

    // A priority queue compares priorities only, so it is on the list for its elements. What is dequeued is taken out. A peek and an
    // enumeration of the unordered items hand out a copy, and what enqueues a whole sequence copies it.
    [TestMethod]
    public Task BuiltInList_HasAPriorityQueueForItsElements() => Verify.AnalyzerAsync(
        $$"""
        using System.Collections.Generic;

        sealed class C
        {
            private readonly PriorityQueue<Guard, int> _queue = new();
            private readonly PriorityQueue<Guard, int> _ordered = new(4, Comparer<int>.Default);

            void Use(RwLock rwLock, List<(Guard, int)> pairs)
            {
                _queue.Enqueue(rwLock.Enter(), 1);
                Guard a = _queue.Dequeue();
                a.Dispose();

                if (_queue.TryDequeue(out var b, out int priority))
                    b.Dispose();

                Guard c = _queue.EnqueueDequeue(rwLock.Enter(), 2);
                c.Dispose();
                Guard d = _queue.DequeueEnqueue(rwLock.Enter(), 3);
                d.Dispose();

                _queue.EnsureCapacity(8);
                _queue.TrimExcess();
                _queue.Clear();
                int n = _queue.Count + _queue.Capacity + _queue.UnorderedItems.Count + _ordered.Comparer.Compare(1, 2);

                Guard e = {|#0:_queue.Peek()|};
                _queue.TryPeek({|#1:out var f|}, out _);

                foreach (var (element, rank) in {|#2:_queue.UnorderedItems|})
                {
                }

                {|#3:_queue.EnqueueRange(pairs)|};
            }
        }

        {{Sources.Guard}}
        """,
        Verify.ReadFromStoredContainer(0, "'PriorityQueue<TElement, TPriority>.Peek()'", "Guard"),
        Verify.ReadFromStoredContainer(1, "'PriorityQueue<TElement, TPriority>.TryPeek(out TElement, out TPriority)'", "Guard"),
        Verify.ReadFromStoredContainer(2, "'foreach'", "(Guard Element, int Priority)"),
        Verify.UnlistedMember(3, "PriorityQueue<TElement, TPriority>.EnqueueRange(IEnumerable<(TElement Element, TPriority Priority)>)", "Guard", "TElement", "M:System.Collections.Generic.PriorityQueue`2.EnqueueRange(System.Collections.Generic.IEnumerable{System.ValueTuple{`0,`1}})"));

    // A linked list adds, removes and walks its nodes, and a node holds its value: 'ValueRef' is a reference to it, and 'Value' a
    // read by value. What compares values is not on the list.
    [TestMethod]
    public Task BuiltInList_HasALinkedListAndItsNodes() => Verify.AnalyzerAsync(
        $$"""
        using System.Collections.Generic;

        sealed class C
        {
            private readonly LinkedList<Guard> _list = new();

            void Use(RwLock rwLock)
            {
                LinkedListNode<Guard> first = _list.AddFirst(rwLock.Enter());
                LinkedListNode<Guard> last = _list.AddLast(rwLock.Enter());
                _list.AddAfter(first, rwLock.Enter());
                _list.AddBefore(last, rwLock.Enter());
                var loose = new LinkedListNode<Guard>(rwLock.Enter());
                _list.AddLast(loose);
                _list.Remove(loose);
                loose.ValueRef.Dispose();

                for (var node = _list.First; node is not null; node = node.Next)
                    node.ValueRef.Dispose();

                bool same = _list.Last?.Previous?.List == _list && _list.Count > 0;
                _list.RemoveFirst();
                _list.RemoveLast();
                _list.Clear();

                Guard copy = {|#0:first.Value|};

                foreach (var guard in {|#1:_list|})
                {
                }

                var found = {|#2:_list.Find(default)|};
            }
        }

        {{Sources.Guard}}
        """,
        Verify.ReadFromStoredContainer(0, "'LinkedListNode<T>.Value'", "Guard", because: "'first' is not the method's own anywhere in the method, because on line 11 it is passed to 'LinkedList<Guard>.AddAfter(LinkedListNode<Guard>, Guard)'"),
        Verify.ReadFromStoredContainer(1, "'foreach'", "Guard"),
        Verify.UnlistedMember(2, "LinkedList<T>.Find(T)", "Guard", "T", "M:System.Collections.Generic.LinkedList`1.Find(`0)"));

    // A sorted list and a frozen dictionary search their keys only, so they are on the list for their values, the frozen dictionary
    // with its reference to a value. Making a frozen dictionary copies every value of its source. A sorted dictionary is not on the
    // list: every lookup gives each pair that it passes, value included, to the comparer of pairs by value.
    [TestMethod]
    public Task BuiltInList_HasASortedListAndAFrozenDictionaryForTheirValues() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using System.Collections.Frozen;
        using System.Collections.Generic;

        sealed class C
        {
            private readonly SortedList<int, Guard> _sorted = new(Comparer<int>.Default);
            private readonly FrozenDictionary<string, Guard> _frozen = FrozenDictionary<string, Guard>.Empty;

            void Use(RwLock rwLock, ReadOnlySpan<char> name)
            {
                _sorted.Add(1, rwLock.Enter());
                _sorted.SetValueAtIndex(0, rwLock.Enter());
                _sorted[2] = rwLock.Enter();
                int at = _sorted.IndexOfKey(1) + _sorted.Capacity + _sorted.Count + _sorted.Keys.Count + _sorted.GetKeyAtIndex(0);
                _sorted.RemoveAt(0);
                bool removed = _sorted.Remove(2) && _sorted.ContainsKey(1);
                _sorted.TrimExcess();
                _sorted.Clear();

                int value = _frozen.GetValueRefOrNullRef("a").Value + _frozen["a"].Value;
                bool there = _frozen.ContainsKey("a") && _frozen.Count > 0 && _frozen.Keys.Length > 0;
                bool hit = _frozen.TryGetAlternateLookup<ReadOnlySpan<char>>(out var lookup) && lookup.ContainsKey(name) && lookup.Dictionary.Count > 0;

                Guard a = {|#0:_sorted.GetValueAtIndex(0)|};
                _frozen.TryGetValue("a", {|#1:out var b|});
                bool any = {|#2:_sorted.ContainsValue(default)|};
            }

            FrozenDictionary<string, Guard> Make(Dictionary<string, Guard> source) => {|#3:source.ToFrozenDictionary()|};

            void Sorted(SortedDictionary<int, Guard> sorted, RwLock rwLock) => {|#4:sorted.Add(1, rwLock.Enter())|};
        }

        {{Sources.Guard}}
        """,
        Verify.ReadFromStoredContainer(0, "'SortedList<TKey, TValue>.GetValueAtIndex(int)'", "Guard"),
        Verify.ReadFromStoredContainer(1, "'FrozenDictionary<TKey, TValue>.TryGetValue(TKey, out TValue)'", "Guard"),
        Verify.UnlistedMember(2, "SortedList<TKey, TValue>.ContainsValue(TValue)", "Guard", "TValue", "M:System.Collections.Generic.SortedList`2.ContainsValue(`1)"),
        Verify.UnlistedMember(3, "FrozenDictionary.ToFrozenDictionary<TKey, TValue>(IEnumerable<KeyValuePair<TKey, TValue>>, IEqualityComparer<TKey>?)", "Guard", "TValue", "M:System.Collections.Frozen.FrozenDictionary.ToFrozenDictionary``2(System.Collections.Generic.IEnumerable{System.Collections.Generic.KeyValuePair{``0,``1}},System.Collections.Generic.IEqualityComparer{``0})"),
        Verify.UnlistedMember(4, "SortedDictionary<TKey, TValue>.Add(TKey, TValue)", "Guard", "TValue", "M:System.Collections.Generic.SortedDictionary`2.Add(`0,`1)"));

    // The alternate lookup of a concurrent dictionary does what the dictionary's own members do.
    [TestMethod]
    public Task BuiltInList_HasTheAlternateLookupOfAConcurrentDictionary() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using System.Collections.Concurrent;
        using System.Collections.Generic;

        sealed class C
        {
            private readonly ConcurrentDictionary<string, Guard> _held = new(StringComparer.Ordinal);

            void Use(RwLock rwLock, ReadOnlySpan<char> name)
            {
                var lookup = _held.GetAlternateLookup<ReadOnlySpan<char>>();
                lookup.TryAdd(name, rwLock.Enter());
                lookup[name] = rwLock.Enter();

                if (lookup.TryRemove(name, out var a))
                    a.Dispose();

                if (_held.TryGetAlternateLookup<ReadOnlySpan<char>>(out var other) && other.TryRemove(name, out string? key, out var b))
                    b.Dispose();

                bool there = lookup.ContainsKey(name) && lookup.Dictionary.Comparer is not null;

                lookup.TryGetValue(name, {|#0:out var c|});
            }
        }

        {{Sources.Guard}}
        """,
        Verify.ReadFromStoredContainer(0, "'ConcurrentDictionary<TKey, TValue>.AlternateLookup<TAlternateKey>.TryGetValue(TAlternateKey, out TValue)'", "Guard"));

    // A buffer writer hands out memory to write into, and a sequence and a memory manager hand out views. What copies stays reported.
    [TestMethod]
    public Task BuiltInList_HasTheBufferWriterAndTheSequence() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using System.Buffers;
        using Singulink.CodeAnalysis;

        sealed class C
        {
            private readonly ArrayBufferWriter<Guard> _writer = new(4);

            void Write(RwLock rwLock, IBufferWriter<Guard> other)
            {
                Span<Guard> free = _writer.GetSpan(1);
                free[0] = rwLock.Enter();
                _writer.Advance(1);
                other.GetSpan(1)[0] = rwLock.Enter();
                other.Advance(1);

                foreach (ref readonly var guard in _writer.WrittenSpan)
                    free[0].Value += guard.Value;

                int sizes = _writer.Capacity + _writer.FreeCapacity + _writer.WrittenCount + _writer.WrittenMemory.Length + _writer.GetMemory().Length;
                _writer.ResetWrittenCount();
                _writer.Clear();
            }

            // A constructor keeps what it is given, by its kind: the sequence is over the array and the segments.
            long Read([Kept] ReadOnlySequenceSegment<Guard> start, [Kept] ReadOnlySequenceSegment<Guard> end)
            {
                var array = new Guard[2];
                var sequence = new ReadOnlySequence<Guard>(array);
                var segmented = new ReadOnlySequence<Guard>(start, 0, end, 1);
                var part = sequence.Slice(1).Slice(sequence.Start, 1);
                long length = part.Length + segmented.GetOffset(segmented.GetPosition(1)) + start.RunningIndex + start.Memory.Length;

                foreach (ReadOnlyMemory<Guard> memory in sequence)
                    length += memory.Length;

                Guard[] copy = {|#0:sequence.ToArray()|};
                return length;
            }

            void Manage(MemoryManager<Guard> manager)
            {
                using var pinned = manager.Pin(0);
                manager.GetSpan()[0].Dispose();
                int length = manager.Memory.Length;
            }
        }

        {{Sources.Guard}}
        """,
        Verify.UnlistedMember(0, "BuffersExtensions.ToArray<T>(in ReadOnlySequence<T>)", "Guard", "T", "M:System.Buffers.BuffersExtensions.ToArray``1(System.Buffers.ReadOnlySequence{``0}@)"));

    // A task of a result keeps what its one delegate returns, made with a constructor or with its factory. A factory member that is given
    // other tasks of a non-copyable result is judged by that type parameter, which the entry does not vouch for.
    [TestMethod]
    public Task BuiltInList_HasTheConstructorsAndTheFactoryOfATaskOfAResult() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using System.Threading.Tasks;

        sealed class C
        {
            Task<Guard> Make(RwLock rwLock)
            {
                var task = new Task<Guard>(() => rwLock.Enter());
                task.Start();
                var withState = new Task<Guard>(state => ((RwLock)state!).Enter(), rwLock);
                withState.Start();
                return new TaskFactory<Guard>().StartNew(() => rwLock.Enter());
            }

            Task<Guard> Continue(Task[] tasks, RwLock rwLock) =>
                new TaskFactory<Guard>().ContinueWhenAll(tasks, done => rwLock.Enter());

            Task<int> ContinueOnGuards(Task<Guard>[] tasks) =>
                {|#0:new TaskFactory<int>().ContinueWhenAll(tasks, done => done.Length)|};
        }

        {{Sources.Guard}}
        """,
        Verify.MemberListedForOtherTypeParameter(0, "TaskFactory<TResult>.ContinueWhenAll<TAntecedentResult>(Task<TAntecedentResult>[], Func<Task<TAntecedentResult>[], TResult>)", "Guard", "TAntecedentResult", "TResult"));

    // What a list does in place: remove a range and reverse.
    [TestMethod]
    public Task BuiltInList_HasWhatAListDoesInPlace() => Verify.AnalyzerAsync(
        $$"""
        using System.Collections.Generic;
        using System.Runtime.InteropServices;

        sealed class C
        {
            private readonly List<Guard> _list = new();

            void Use()
            {
                _list.Reverse();
                _list.Reverse(0, 1);
                _list.RemoveRange(0, 1);

                // A count that grows again makes what was removed an element once more, where the elements hold no reference.
                {|#0:CollectionsMarshal.SetCount(_list, 2)|};
            }
        }

        {{Sources.Guard}}
        """,
        Verify.UnlistedMember(0, "CollectionsMarshal.SetCount<T>(List<T>, int)", "Guard", "T", "M:System.Runtime.InteropServices.CollectionsMarshal.SetCount``1(System.Collections.Generic.List{``0},System.Int32)"));

    // A strong box is made with a constructor of the list. Its value is a public field, which a list cannot name: it is reached in
    // place, as a field of an object is.
    [TestMethod]
    public Task BuiltInList_HasTheConstructorsOfAStrongBox() => Verify.AnalyzerAsync(
        $$"""
        using System.Runtime.CompilerServices;

        sealed class C
        {
            private readonly StrongBox<Guard> _box = new();

            void Use(RwLock rwLock)
            {
                var box = new StrongBox<Guard>(rwLock.Enter());
                box.Value.Dispose();
                _box.Value = rwLock.Enter();
                Guard copy {|#0:= _box.Value|};
            }
        }

        {{Sources.Guard}}
        """,
        Verify.CopyByAssignment(0, "Guard"));

    // A 'copyable' line makes a type that the built-in list makes non-copyable an ordinary struct for the project, in the one place that
    // decides it: a struct that holds it, a generic struct over it, a container of it and a type parameter that it is bound to all follow.
    // The other built-in types stay as they are.
    [TestMethod]
    public Task Copyable_TakesABuiltInTypeOut() => Verify.AnalyzerAsync(
        """
        using System.Collections.Generic;
        using System.Runtime.InteropServices;
        using System.Threading;

        class C
        {
            GCHandle _handle;
            Holder _holder;
            KeyValuePair<int, GCHandle> _pair;
            SpinLock _spinLock;

            static T Identity<T>(T value) => value;

            void M(List<GCHandle> handles)
            {
                var handle = _handle;
                var holder = _holder;
                var pair = _pair;
                var first = handles[0];
                var same = Identity(_handle);
                var spinLock {|#0:= _spinLock|};
            }
        }

        struct Holder
        {
            public GCHandle Handle;
        }
        """,
        "T:System.Runtime.InteropServices.GCHandle;copyable",
        Verify.CopyByAssignment(0, "System.Threading.SpinLock"));

    // A struct that a referenced assembly marks with an attribute of another namespace is non-copyable in the project; a 'copyable' line
    // takes it out, and leaves the other struct of that assembly as it is.
    [TestMethod]
    public Task Copyable_TakesOutAStructThatAReferencedAssemblyMarksWithAnAttributeOfAnotherNamespace()
    {
        var test = new Verify.Test {
            TestCode = """
                class C
                {
                    Lease _lease;
                    Ticket _ticket;

                    void M()
                    {
                        var lease = _lease;
                        var ticket {|#0:= _ticket|};
                    }
                }
                """,
        };

        test.TestState.AdditionalProjects["Library"].Sources.Add("""
            // The attribute is for another analyzer, which the library says where it declares it.
            #pragma warning disable SCA1045
            namespace Other
            {
                internal sealed class NonCopyableAttribute : System.Attribute { }
            }
            #pragma warning restore SCA1045

            [Other.NonCopyable]
            public struct Lease { }

            [Other.NonCopyable]
            public struct Ticket { }
            """);

        test.TestState.AdditionalProjectReferences.Add("Library");
        test.TestState.AdditionalFiles.Add((Verify.ListFileName, "T:Lease;copyable"));
        test.ExpectedDiagnostics.Add(Verify.CopyByAssignment(0, "Ticket"));
        return test.RunAsync();
    }

    // Where the line has no effect it is reported, each case with a text of its own, and the type stays as it was.
    [TestMethod]
    public Task Copyable_ThatHasNoEffectIsReported() => Verify.AnalyzerAsync(
        """
        using System.Threading.Tasks;

        class C
        {
            Own _own;

            void M()
            {
                var own {|#0:= _own|};
            }
        }

        [Singulink.CodeAnalysis.NonCopyable]
        struct Own { }

        class Bag
        {
            public void Take() { }
        }
        """,
        """
        T:Own;copyable
        T:System.Threading.Tasks.ValueTask`1;copyable
        T:Not.There;copyable
        T:System.String;copyable
        M:Bag.Take;copyable
        """,
        Verify.CopyByAssignment(0, "Own"),
        Verify.SafeListCopyableHasNoEffect(1, 1, 15, "T:Own", "the project declares the type itself, so take its [NonCopyable] attribute off instead"),
        Verify.SafeListCopyableHasNoEffect(
            2,
            1,
            46,
            "T:System.Threading.Tasks.ValueTask`1",
            "nothing makes the type non-copyable by itself, neither the built-in list nor an attribute; a struct that holds a non-copyable value stays non-copyable through it"),
        Verify.SafeListCopyableHasNoEffect(3, 1, 21, "T:Not.There", "the compilation has no type of that ID"),
        Verify.SafeListLine(4, 1, 25, "the marker 'copyable' is for a struct"),
        Verify.SafeListLine(5, 1, 20, "the marker 'copyable' is for a type; a member takes 'new', 'borrows', 'drains', 'owned', 'clones' or no marker"));

    // Where one file says 'copyable' and another 'noncopyable' of the same type, 'noncopyable' applies. The built-in list is no such file:
    // overriding it is what the marker is for.
    [TestMethod]
    public Task Copyable_LosesToANoncopyableLineOfAnotherFile()
    {
        var test = new Verify.Test {
            TestCode = """
                using System.Runtime.InteropServices;

                class C
                {
                    GCHandle _handle;

                    void M()
                    {
                        var handle {|#0:= _handle|};
                    }
                }
                """,
        };

        test.TestState.AdditionalFiles.Add(("First/NonCopyableSymbols.txt", "T:System.Runtime.InteropServices.GCHandle;copyable"));
        test.TestState.AdditionalFiles.Add(("Second/NonCopyableSymbols.txt", "T:System.Runtime.InteropServices.GCHandle;noncopyable"));
        test.ExpectedDiagnostics.Add(Verify.CopyByAssignment(0, "System.Runtime.InteropServices.GCHandle"));
        return test.RunAsync();
    }
}
