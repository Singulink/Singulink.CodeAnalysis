using Microsoft.CodeAnalysis.Testing;

namespace Singulink.CodeAnalysis.Tests;

/// <summary>
/// The uses of an object whose class binds a non-copyable type to a type parameter of a class that clones itself, where no declaration of
/// the project judged the binding (SCA1009): each way that the object reaches the code that clones it, for each way that the project gets
/// the object, with a marked and an unmarked type parameter, with the generic in the project and in a library built with and without the
/// analyzers.
/// </summary>
[TestClass]
public class CloningUseTests
{
    public enum Place
    {
        Project,
        AnalyzedLibrary,
        UncheckedLibrary,
    }

    // The markers are those of the generic in the project: a library is built from the same text without them.
    private const string Library = """
        using System;
        using System.Globalization;
        using System.Text;
        using Singulink.CodeAnalysis;

        namespace Lib
        {
            public interface ITagged { }

            public class Tagged<MARK T> : CultureInfo, ITagged
            {
                public T {|#20:Tag|} = default!;

                public Tagged() : base("en-US") { }

                public Tagged(string name) : base(name) { }
            }

            public class TaggedEncoding<MARK T> : UTF8Encoding
            {
                public T {|#21:Tag|} = default!;
            }

            public static class Factory
            {
                public static Tagged<T> Make<MARK T>() => {|#22:new Tagged<T>()|};

                public static CultureInfo MakeCulture<MARK T>() => {|#23:new Tagged<T>()|};

                public static ICloneable MakeCloneable<MARK T>() => {|#24:new Tagged<T>()|};

                public static ITagged MakeTagged<MARK T>() => {|#25:new Tagged<T>()|};
            }
        }
        """;

    private const string App = """
        using System;
        using System.Globalization;
        using Lib;
        using Singulink.CodeAnalysis;

        class C
        {
            static T MakeNew<[NonCopyable] T>() where T : new() => new T();

            void Routes(dynamic name)
            {
                var created = {|#0:new Tagged<Guard>()|};
                {|#1:created.Clone()|};

                var fromDynamic = {|#2:new Tagged<Guard>(name)|};
                {|#3:fromDynamic.Clone()|};

                var fromNew = MakeNew<Tagged<Guard>>();
                CultureInfo.ReadOnly({|#4:fromNew|});

                var activated = {|#5:Activator.CreateInstance<Tagged<Guard>>()|};
                ((ICloneable){|#6:activated|}).Clone();

                var made = {|#7:Factory.Make<Guard>()|};
                CultureInfo culture = {|#8:made|};
                culture.Clone();
                ITagged tagged = {|#9:made|};

                CultureInfo asBase = {|#10:Factory.MakeCulture<Guard>()|};
                asBase.Clone();
                ICloneable asCloneable = {|#11:Factory.MakeCloneable<Guard>()|};
                asCloneable.Clone();
                ITagged asInterface = {|#12:Factory.MakeTagged<Guard>()|};
                ((ICloneable)asInterface).Clone();

                var encoding = {|#13:new TaggedEncoding<Guard>()|};
                Console.OutputEncoding = {|#14:encoding|};
                _ = made.Name;
            }
        }
        """;

    private const string Culture = "System.Globalization.CultureInfo";
    private const string Tagged = "Lib.Tagged<Guard>";

    [TestMethod]
    [DataRow(Place.Project, true)]
    [DataRow(Place.Project, false)]
    [DataRow(Place.AnalyzedLibrary, true)]
    [DataRow(Place.AnalyzedLibrary, false)]
    [DataRow(Place.UncheckedLibrary, true)]
    [DataRow(Place.UncheckedLibrary, false)]
    public async Task Use_OfAnObjectWithABindingThatNoDeclarationJudgedGetsAWarningOnEveryRouteToTheClone(Place place, bool marked)
    {
        string library = Library.Replace("MARK ", marked ? "[NonCopyable] " : string.Empty, StringComparison.Ordinal);
        var test = new Verify.Test { TestCode = App + Environment.NewLine + Sources.Guard };

        if (place is Place.Project)
        {
            test.TestState.Sources.Add(("Lib.cs", library));
        }
        else
        {
            var reference = await Verify.CompileLibraryAsync(Verify.Test.NetReferenceAssemblies, library, analyzed: place is Place.AnalyzedLibrary, "Lib");
            test.TestState.AdditionalReferences.Add(reference);
        }

        // Every route from the object to the code that clones it gets SCA1009 in the project that has the object, whatever the mark.
        test.ExpectedDiagnostics.AddRange([
            Verify.CloningBinding(0, "Guard", Tagged, Culture),
            Verify.CloningUseOfCloningMember(1, "CultureInfo.Clone()", Tagged, "Guard", Tagged),
            Verify.CloningBinding(2, "Guard", Tagged, Culture),
            Verify.CloningUseOfCloningMember(3, "CultureInfo.Clone()", Tagged, "Guard", Tagged),
            Verify.CloningUseOfCloningMember(4, "CultureInfo.ReadOnly(CultureInfo)", Tagged, "Guard", Tagged),
            Verify.CloningUseConversion(6, Tagged, "System.ICloneable", Culture, "Guard", Tagged),
            Verify.CloningUseConversion(8, Tagged, Culture, Culture, "Guard", Tagged),
            Verify.CloningUseConversion(9, Tagged, "Lib.ITagged", Culture, "Guard", Tagged),
            Verify.CloningBinding(13, "Guard", "Lib.TaggedEncoding<Guard>", "System.Text.Encoding"),
            Verify.CloningUseConversion(14, "Lib.TaggedEncoding<Guard>", "System.Text.Encoding", "System.Text.Encoding", "Guard", "Lib.TaggedEncoding<Guard>"),

            // Activator's own type parameter is not marked: its code can use the object as any type.
            Verify.CloningUseTypeArgument(5, "Activator.CreateInstance<T>()", Tagged, "T", Culture, "Guard", Tagged),
        ]);

        // Where the mark does not count, the generic's members are unchecked code for Guard, and every use of one gets SCA1020: the
        // factories that hand the object out as a base class or an interface included.
        if (!marked || place is Place.UncheckedLibrary)
        {
            (int Location, string Member, string Id)[] unlisted = [
                (0, "Tagged<T>.Tagged()", "M:Lib.Tagged`1.#ctor"),
                (7, "Factory.Make<T>()", "M:Lib.Factory.Make``1"),
                (10, "Factory.MakeCulture<T>()", "M:Lib.Factory.MakeCulture``1"),
                (11, "Factory.MakeCloneable<T>()", "M:Lib.Factory.MakeCloneable``1"),
                (12, "Factory.MakeTagged<T>()", "M:Lib.Factory.MakeTagged``1"),
                (13, "TaggedEncoding<T>.TaggedEncoding()", "M:Lib.TaggedEncoding`1.#ctor"),
            ];

            foreach (var (location, member, id) in unlisted)
            {
                test.ExpectedDiagnostics.Add(marked
                    ? Verify.UnlistedMemberOfUncheckedCode(location, member, "Guard", "T", id)
                    : Verify.UnlistedMember(location, member, "Guard", "T", id));
            }
        }

        // With a marked type parameter, a factory that hands the object out as a base class or an interface is checked code, and the
        // conversion in it gets the warning in the build of the generic's own project: the factories' callers get none.
        if (marked && place is Place.Project)
        {
            test.ExpectedDiagnostics.AddRange([
                Verify.MemberwiseCloneOfTypeParameterThroughListedBase(20, "Lib.Tagged<T>", Culture, "T", "Tag"),
                Verify.MemberwiseCloneOfTypeParameterThroughListedBase(21, "Lib.TaggedEncoding<T>", "System.Text.Encoding", "T", "Tag"),
                Verify.CloningBinding(22, "T", "Lib.Tagged<T>", Culture),
                Verify.CloningUseConversion(23, "Lib.Tagged<T>", Culture, Culture, "T", "Lib.Tagged<T>"),
                Verify.CloningUseConversion(24, "Lib.Tagged<T>", "System.ICloneable", Culture, "T", "Lib.Tagged<T>"),
                Verify.CloningUseConversion(25, "Lib.Tagged<T>", "Lib.ITagged", Culture, "T", "Lib.Tagged<T>"),
            ]);
        }

        await test.RunAsync();
    }

    // A class of the project whose binding or field is judged where it is declared gets that one warning, and its objects none where
    // they are used as the class that clones itself: the field, or the base class, is the answer. (QA9 r2: one warning where one is
    // enough)
    [TestMethod]
    public Task Use_OfAClassOfTheProjectJudgedAtItsDeclarationGetsNoSecondWarning() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using System.Globalization;
        using Singulink.CodeAnalysis;

        public class GuardedCulture : CultureInfo
        {
            public Guard {|#0:G|};

            public GuardedCulture() : base("en-US") { }
        }

        public class Tagged<[NonCopyable] T> : CultureInfo
        {
            public T {|#1:Tag|} = default!;

            public Tagged() : base("en-US") { }
        }

        public class GuardCulture : {|#2:Tagged<Guard>|} { }

        public sealed class Leaf : GuardCulture { }

        class C
        {
            object Uses(GuardedCulture plain, GuardCulture bound, Leaf leaf)
            {
                CultureInfo a = plain;
                plain.Clone();
                _ = plain.Name;
                CultureInfo b = bound;
                bound.Clone();
                _ = bound.Name;
                ICloneable c = bound;
                CultureInfo d = leaf;
                leaf.Clone();
                return CultureInfo.ReadOnly(bound);
            }
        }

        {{Sources.Guard}}
        """,
        Verify.MemberwiseCloneThroughListedBase(0, "GuardedCulture", Culture, "Guard", "G"),
        Verify.MemberwiseCloneOfTypeParameterThroughListedBase(1, "Tagged<T>", Culture, "T", "Tag"),
        Verify.CloningBinding(2, "Guard", "Tagged<Guard>", Culture));

    // A class of another assembly that binds a non-copyable type in its own base list was not judged in this project: its objects,
    // and those of a class of the project that derives from it, get the warning where they are used. (QA9 r2 1.4)
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task Use_OfAClassOfAnotherAssemblyThatBindsInItsBaseListGetsAWarning(bool analyzed)
    {
        var library = await Verify.CompileLibraryAsync(
            Verify.Test.NetReferenceAssemblies,
            """
            using System.Globalization;
            using Singulink.CodeAnalysis;

            namespace Lib
            {
                public class Tagged<[NonCopyable] T> : CultureInfo
                {
                    public T Tag = default!;

                    public Tagged() : base("en-US") { }
                }

                [NonCopyable]
                public struct LibGuard { }

                public class LibGuardCulture : Tagged<LibGuard> { }
            }
            """,
            analyzed,
            "Lib");

        var test = new Verify.Test {
            TestCode = """
                using System.Globalization;
                using Lib;

                public sealed class MyLeaf : LibGuardCulture { }

                class C
                {
                    object Uses(LibGuardCulture culture, MyLeaf leaf)
                    {
                        CultureInfo a = {|#0:culture|};
                        {|#1:leaf.Clone()|};
                        return leaf.Name;
                    }
                }
                """,
        };

        test.TestState.AdditionalReferences.Add(library);
        test.ExpectedDiagnostics.AddRange([
            Verify.CloningUseConversion(0, "Lib.LibGuardCulture", Culture, Culture, "Lib.LibGuard", "Lib.Tagged<Lib.LibGuard>"),
            Verify.CloningUseOfCloningMember(1, "CultureInfo.Clone()", "MyLeaf", "Lib.LibGuard", "Lib.Tagged<Lib.LibGuard>"),
        ]);

        await test.RunAsync();
    }

    // Each other form in which a value is used as a type that hides it is a route to the clone as well: variance, a container of such
    // objects, a hole of an interpolated string, a 'lock', a 'foreach' variable, a pattern, the constraint of a marked type parameter, a
    // method group, 'dynamic' and the return of an override. A member on the symbol list, such as Monitor.Enter for a 'lock', says that it
    // makes no copy, and so no clone.
    [TestMethod]
    public async Task Use_InEveryFormThatHidesTheTypeGetsAWarning()
    {
        var library = await Verify.CompileLibraryAsync(
            Verify.Test.NetReferenceAssemblies,
            """
            using System.Globalization;
            using Singulink.CodeAnalysis;

            namespace Lib
            {
                public class Tagged<[NonCopyable] T> : CultureInfo
                {
                    public T Tag = default!;

                    public Tagged() : base("en-US") { }
                }
            }
            """,
            analyzed: true,
            "Lib");

        var test = new Verify.Test {
            TestCode = """
                using System;
                using System.Collections.Generic;
                using System.Globalization;
                using Lib;
                using Singulink.CodeAnalysis;

                class Base
                {
                    public virtual CultureInfo Make() => CultureInfo.InvariantCulture;
                }

                class Derived : Base
                {
                    public override Tagged<Guard> {|#2:Make|}() => {|#0:new()|};
                }

                class C
                {
                    static void Constrained<[NonCopyable] T>(T value) where T : CultureInfo { }

                    static Tagged<Guard> MakeTagged() => {|#1:new()|};

                    void Uses(List<Tagged<Guard>> list, Tagged<Guard> tagged)
                    {
                        IEnumerable<CultureInfo> cultures = {|#3:list|};
                        object boxed = {|#4:list|};
                        string text = $"{{|#5:tagged|}}";
                        lock (tagged) { }
                        foreach (CultureInfo culture in {|#6:list|}) { }
                        if (tagged is {|#7:ICloneable cloneable|}) { }
                        {|#8:Constrained(MakeTagged())|};
                        Func<CultureInfo> make = {|#9:MakeTagged|};
                        dynamic late = {|#10:tagged|};
                    }
                }
                """ + Environment.NewLine + Sources.Guard,
        };

        test.TestState.AdditionalReferences.Add(library);
        const string List = "System.Collections.Generic.List<Lib.Tagged<Guard>>";

        test.ExpectedDiagnostics.AddRange([
            Verify.CloningBinding(0, "Guard", Tagged, Culture),
            Verify.CloningBinding(1, "Guard", Tagged, Culture),
            Verify.CloningUseConversion(2, Tagged, Culture, Culture, "Guard", Tagged),
            Verify.CloningUseConversion(3, List, "System.Collections.Generic.IEnumerable<System.Globalization.CultureInfo>", Culture, "Guard", Tagged),
            Verify.CloningUseConversion(4, List, "object", Culture, "Guard", Tagged),
            Verify.CloningUseConversion(5, Tagged, "object", Culture, "Guard", Tagged),
            Verify.CloningUseConversion(6, Tagged, Culture, Culture, "Guard", Tagged),
            Verify.CloningUseConversion(7, Tagged, "System.ICloneable", Culture, "Guard", Tagged),
            Verify.CloningUseConstraint(8, "C.Constrained<T>(T)", Tagged, "T", Culture, Culture, "Guard", Tagged),
            Verify.CloningUseConversion(9, Tagged, Culture, Culture, "Guard", Tagged),
            Verify.CloningUseConversion(10, Tagged, "dynamic", Culture, "Guard", Tagged),
        ]);

        await test.RunAsync();
    }

    // The binding of a partial class gets its warning on the entry of the base list that names the base class, in the part where it is
    // written, so that a suppression there answers it: not on an interface that another part names first. (QA9 r2 2.1)
    [TestMethod]
    public Task Binding_OfAPartialClassSitsOnThePartThatNamesTheBaseClass()
    {
        var test = new Verify.Test {
            TestCode = """
                using System;
                using System.Globalization;
                using Singulink.CodeAnalysis;

                public class Tagged<[NonCopyable] T> : CultureInfo
                {
                    public T {|#0:Tag|} = default!;

                    public Tagged() : base("en-US") { }
                }

                public partial class Pinned : IDisposable
                {
                    public void Dispose() { }
                }

                public partial class Pinned : {|#1:Tagged<Guard>|} { }

                public partial class Suppressed : IDisposable
                {
                    public void Dispose() { }
                }

                #pragma warning disable SCA1009 // OK: no object of the class is ever cloned
                public partial class Suppressed : Tagged<Guard> { }
                #pragma warning restore SCA1009
                """ + Environment.NewLine + Sources.Guard,
        };

        test.ExpectedDiagnostics.AddRange([
            Verify.MemberwiseCloneOfTypeParameterThroughListedBase(0, "Tagged<T>", Culture, "T", "Tag"),
            Verify.CloningBinding(1, "Guard", "Tagged<Guard>", Culture),
        ]);

        return test.RunAsync();
    }

    // A line that says a member makes no copy gets SCA1042 where a 'clones' line names the member, whichever symbol list file that is
    // in: the project's own or one that comes with a library. (QA9 r2 2.2)
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public Task ContradictingLine_GetsAWarningWhereverTheClonesLineIs(bool sameFile)
    {
        var test = new Verify.Test {
            TestCode = """
                public class Open
                {
                    public object Copy() => this;
                }
                """,
        };

        if (sameFile)
        {
            test.TestState.AdditionalFiles.Add((Verify.ListFileName, "M:Open.Copy\nM:Open.Copy;clones"));
        }
        else
        {
            test.TestState.AdditionalFiles.Add((Verify.ListFileName, "M:Open.Copy"));
            test.TestState.AdditionalFiles.Add(("packages/mylibrary/1.0.0/build/NonCopyableSymbols.MyLibrary.txt", "M:Open.Copy;clones"));
        }

        test.ExpectedDiagnostics.Add(Verify.SafeListContradictsClones(1, 1, 12, "M:Open.Copy"));
        return test.RunAsync();
    }

    // A class whose base list got the warning for its binding is judged: a member that clones, called on its object or on 'this' or 'base'
    // in its own code, gets nothing more. Where the binding is to an unmarked type parameter of a generic of the project, the conversion of
    // its object to the base class keeps SCA1023, for the code of that generic. (QA9 round 3, 2.1)
    [TestMethod]
    public Task Use_OfAJudgedClassGetsNoWarningForTheClone() => Verify.AnalyzerAsync(
        $$"""
        using System.Globalization;

        public class LikeC14<T> : CultureInfo
        {
            public T V = default!;

            public LikeC14() : base("en-US") { }
        }

        public sealed class D01 : {|#0:LikeC14<Guard>|}
        {
            public object Dup() => Clone();

            public object Dup2() => base.Clone();

            public CultureInfo AsCulture() => {|#1:this|};
        }

        class C
        {
            object Uses(D01 judged) => judged.Clone();
        }

        {{Sources.Guard}}
        """,
        Verify.CloningBinding(0, "Guard", "LikeC14<Guard>", Culture),
        Verify.OpaqueConversion(1, "D01", Culture, through: "LikeC14<Guard>"));

    // A class that passes an unmarked type parameter on to a marked one of a generic that clones itself binds nothing that its declaration
    // shows: its base list gets SCA1040, not SCA1009, so its objects are judged where they are used. A binding to an unmarked type
    // parameter of a member, as ToList and ConvertAll have, gets SCA1009 as well. (QA9 round 3, 2.1 and 2.2)
    [TestMethod]
    public async Task Use_OfAClassThatPassesAnUnmarkedTypeParameterOnGetsAWarning()
    {
        var library = await Verify.CompileLibraryAsync(Verify.Test.NetReferenceAssemblies, ListedLibrary, analyzed: true, "Lib");
        var test = new Verify.Test {
            TestCode = """
                using System.Collections.Generic;
                using System.Globalization;
                using System.Linq;
                using Lib;

                public class DU<{|#0:T|}> : Tagged<T> { }

                class C
                {
                    object Uses(DU<Guard> passed, List<Tagged<Guard>> taggeds)
                    {
                        CultureInfo culture = {|#1:passed|};
                        var copied = {|#2:taggeds.ToList()|};
                        return {|#3:taggeds.ConvertAll<CultureInfo>(t => {|#4:t|})|};
                    }
                }
                """ + System.Environment.NewLine + Sources.Guard,
        };

        test.TestState.AdditionalReferences.Add(library);
        test.ExpectedDiagnostics.AddRange([
            Verify.TypeParameterMarkNotPassedOn(0, "T", "T", "Tagged<T>"),
            Verify.CloningUseConversion(1, "DU<Guard>", Culture, Culture, "Guard", Tagged),
            Verify.CloningUseTypeArgument(2, "Enumerable.ToList<TSource>(IEnumerable<TSource>)", Tagged, "TSource", Culture, "Guard", Tagged),
            Verify.CloningUseTypeArgument(3, "List<T>.ConvertAll<TOutput>(Converter<T, TOutput>)", Tagged, "T", Culture, "Guard", Tagged),
            Verify.CloningUseConversion(4, Tagged, Culture, Culture, "Guard", Tagged),
        ]);

        await test.RunAsync();
    }

    // A member of the class that clones itself, called on an object whose binding no declaration judged: it gets SCA1009 where it clones
    // (rule 1), where it can hand the object back as a type that hides the binding (rule 2), and where the class's line does not say
    // 'clones=complete' and no line names the member (rule 3). A 'clones' member line speaks for its member alone, and a line with no
    // marker vouches for its member whether the class's line says 'clones=complete' or not. Anything else gets nothing: a member that
    // hands out another type, one that the object's own class overrides, and one of object where the lines are complete. A member of the
    // class that shows the binding is code of its own project, whose line says whether it clones. (QA9 round 3, 3 and 2.4)
    [TestMethod]
    [DataRow(false, false, true)]
    [DataRow(true, false, true)]
    [DataRow(false, true, false)]
    [DataRow(false, false, false)]
    [DataRow(true, false, false)]
    public async Task Member_OfTheCloningClassOnTheObjectGetsAWarningOnlyWhereItCanCloneIt(bool withHandBackLine, bool withoutMemberLines, bool complete)
    {
        var library = await Verify.CompileLibraryAsync(Verify.Test.NetReferenceAssemblies, DocLibrary, analyzed: true, "LibC");
        var test = new Verify.Test {
            TestCode = """
                using LibC;

                class C
                {
                    void Uses()
                    {
                        var doc = Docs.Make<Guard>();
                        _ = {|#0:doc.Size|};
                        _ = {|#1:doc.WithTitle("x")|};
                        _ = {|#2:doc.Copy()|};
                        _ = {|#3:doc.Snapshot()|};
                        _ = {|#4:doc.Dup()|};
                        _ = {|#5:doc.Describe()|};
                        _ = {|#6:doc.ToString()|};
                    }
                }
                """ + System.Environment.NewLine + Sources.Guard,
        };

        string typeLine = complete ? "T:LibC.Doc;clones=complete" : "T:LibC.Doc;clones";
        string lines = withoutMemberLines
            ? typeLine
            : typeLine + "\nM:LibC.Doc.Copy;clones\nM:LibC.Doc.Snapshot;clones\nM:LibC.TaggedDoc`1.Dup;clones" + (withHandBackLine ? "\nM:LibC.Doc.WithTitle(System.String)" : string.Empty);

        test.TestState.AdditionalFiles.Add((Verify.ListFileName, lines));
        test.TestState.AdditionalReferences.Add(library);
        const string TaggedDoc = "LibC.TaggedDoc<Guard>";

        if (!withoutMemberLines)
        {
            test.ExpectedDiagnostics.AddRange([
                Verify.CloningUseOfCloningMember(2, "Doc.Copy()", TaggedDoc, "Guard", TaggedDoc),
                Verify.CloningUseOfCloningMember(3, "Doc.Snapshot()", TaggedDoc, "Guard", TaggedDoc),
                Verify.CloningUseOfCloningMember(4, "TaggedDoc<T>.Dup()", TaggedDoc, "Guard", TaggedDoc),
            ]);
        }

        if (!complete)
        {
            // Rule 3: the line of Doc does not say 'clones=complete', so any member that no line names can clone, and the advice gives both
            // lines. The class's own members and the override are its own code.
            var unknown = new List<(int Location, string Member, string Declaring, string Id)> { (0, "Doc.Size", "LibC.Doc", "P:LibC.Doc.Size") };

            if (!withHandBackLine)
                unknown.Add((1, "Doc.WithTitle(string)", "LibC.Doc", "M:LibC.Doc.WithTitle(System.String)"));

            if (withoutMemberLines)
                unknown.AddRange([(2, "Doc.Copy()", "LibC.Doc", "M:LibC.Doc.Copy"), (3, "Doc.Snapshot()", "LibC.Doc", "M:LibC.Doc.Snapshot")]);

            unknown.Add((6, "object.ToString()", "object", "M:System.Object.ToString"));

            foreach (var (location, member, declaring, id) in unknown)
                test.ExpectedDiagnostics.Add(Verify.CloningUseOfUnknownMember(location, member, TaggedDoc, declaring, "LibC.Doc", "Guard", TaggedDoc, id, "T:LibC.Doc"));
        }
        else if (!withHandBackLine)
        {
            test.ExpectedDiagnostics.Add(Verify.CloningUseHandsBack(1, "Doc.WithTitle(string)", TaggedDoc, "LibC.Doc", "LibC.Doc", "Guard", TaggedDoc, "M:LibC.Doc.WithTitle(System.String)"));
        }

        await test.RunAsync();
    }

    // Rule 2 goes by what a signature shows: a member warns where its result, or a 'ref' or 'out' parameter, is a type that hides the
    // binding or contains one (a type argument, an array element, a tuple element, a delegate's return), where it takes a callback whose
    // delegate type takes one, and where an event whose handler type takes one is subscribed to. A line for a member does not vouch for an
    // override of it, and a member of the class that shows the binding is judged by its own signature. A visitor interface, a registry or a
    // wrapper class is left to the list. (QA9 round 4, 1)
    [TestMethod]
    public async Task Member_ThatCanHandTheObjectOnAsItsSignatureShowsGetsAWarning()
    {
        var library = await Verify.CompileLibraryAsync(Verify.Test.NetReferenceAssemblies, HandingLibrary, analyzed: true, "LibD");
        var test = new Verify.Test {
            TestCode = """
                using System;
                using LibD;

                class C
                {
                    void Uses(IVisitor visitor)
                    {
                        var doc = Docs.Make<Guard>();
                        _ = {|#0:doc.Parts()|};
                        _ = {|#1:doc.LoadAsync()|};
                        _ = {|#2:doc.AsArray()|};
                        _ = {|#3:doc.Pair()|};
                        _ = {|#4:doc.Maker()|};
                        _ = {|#5:doc.Bag()|};
                        {|#6:doc.Visit(d => { })|};
                        {|#7:doc.VisitAny(d => { })|};
                        {|#8:doc.Fill(out _)|};
                        {|#9:doc.Changed|} += (sender, e) => { };
                        doc.Changed -= Handler;
                        doc.Counted += n => { };
                        _ = doc.Names();
                        doc.Run(n => { });
                        doc.Accept(visitor);
                        _ = {|#10:doc.Format()|};
                        _ = {|#11:doc.Mine()|};
                        _ = {|#12:doc.Self()|};
                        _ = doc.Size;
                    }

                    static void Handler(object? sender, EventArgs e) { }
                }
                """ + System.Environment.NewLine + Sources.Guard,
        };

        test.TestState.AdditionalFiles.Add((Verify.ListFileName, "T:LibD.Doc;clones=complete\nM:LibD.Doc.Copy;clones\nM:LibD.Doc.Format\nM:LibD.Doc.Accept(LibD.IVisitor)"));
        test.TestState.AdditionalReferences.Add(library);
        const string Mid = "LibD.Mid<Guard>";

        foreach (var (location, member, type, id) in new[] {
            (0, "Doc.Parts()", "System.Collections.Generic.IEnumerable<LibD.Doc>", "M:LibD.Doc.Parts"),
            (1, "Doc.LoadAsync()", "System.Threading.Tasks.Task<LibD.Doc>", "M:LibD.Doc.LoadAsync"),
            (2, "Doc.AsArray()", "LibD.Doc[]", "M:LibD.Doc.AsArray"),
            (3, "Doc.Pair()", "(LibD.Doc, int)", "M:LibD.Doc.Pair"),
            (4, "Doc.Maker()", "System.Func<LibD.Doc>", "M:LibD.Doc.Maker"),
            (5, "Doc.Bag()", "System.Collections.Generic.List<object>", "M:LibD.Doc.Bag"),
            (6, "Doc.Visit(Action<Doc>)", "System.Action<LibD.Doc>", "M:LibD.Doc.Visit(System.Action{LibD.Doc})"),
            (7, "Doc.VisitAny(Action<object>)", "System.Action<object>", "M:LibD.Doc.VisitAny(System.Action{System.Object})"),
            (8, "Doc.Fill(out Doc)", "LibD.Doc", "M:LibD.Doc.Fill(LibD.Doc@)"),
            (9, "Doc.Changed", "System.EventHandler", "E:LibD.Doc.Changed"),
            (10, "Mid<T>.Format()", "object", "M:LibD.Mid`1.Format"),
            (11, "Mid<T>.Mine()", "LibD.Doc", "M:LibD.Mid`1.Mine"),
            (12, "Doc.Self()", "LibD.Doc", "M:LibD.Doc.Self"),
        })
        {
            test.ExpectedDiagnostics.Add(Verify.CloningUseHandsBack(location, member, Mid, type, "LibD.Doc", "Guard", Mid, id));
        }

        await test.RunAsync();
    }

    // A member of a class that clones itself, of a class that another project can derive from or bind a type to, which clones its own
    // object, with MemberwiseClone, through a member of the class that clones, or through an interface that the class implements with one,
    // gets a warning in a library that helps its author complete the 'clones' lines (SCA1008). It is not a proof: a static helper that is
    // given the object is not followed, nor is a private or internal member, which gets no warning of its own. A call of MemberwiseClone in
    // an open class keeps the warning of its own, as released. (QA9 round 3, E, and rounds 4 and 5)
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Member_ThatClonesItsOwnObjectNeedsAClonesLine(bool helperHasLine)
    {
        var test = new Verify.Test {
            TestCode = """
                using System;
                using System.Globalization;
                using Singulink.CodeAnalysis;

                public class Note : CultureInfo
                {
                    public Note() : base("en-US") { }

                    public Note Copy() => (Note){|#0:Clone()|};

                    private object Helper() => {|#1:MemberwiseClone()|};

                    public object Indirect() => {|#2:Helper()|};

                    public object Other(CultureInfo other) => other.Clone();

                    public object ThroughTheInterface() => {|#5:((ICloneable)this).Clone()|};

                    public object ThroughAStaticHelper() => Helpers.Copy(this);
                }

                public static class Helpers
                {
                    public static object Copy(CultureInfo culture) => culture.Clone();
                }

                public sealed class Box<[NonCopyable] T> : CultureInfo
                {
                    public T {|#3:V|} = default!;

                    public Box() : base("en-US") { }

                    public object Dup() => {|#4:Clone()|};
                }

                public sealed class Plain : CultureInfo
                {
                    public Plain() : base("en-US") { }

                    public Plain Copy() => (Plain)Clone();
                }
                """,
        };

        if (helperHasLine)
            test.TestState.AdditionalFiles.Add((Verify.ListFileName, "M:Note.Helper;clones"));

        test.ExpectedDiagnostics.AddRange([
            Verify.MemberClonesWithoutLine(0, "Note.Copy()", "CultureInfo.Clone()", "Note", "M:Note.Copy"),
            Verify.MemberwiseCloneOfTypeParameterThroughListedBase(3, "Box<T>", Culture, "T", "V"),
            Verify.MemberClonesWithoutLine(4, "Box<T>.Dup()", "CultureInfo.Clone()", "Box<T>", "M:Box`1.Dup"),
            Verify.MemberClonesWithoutLine(5, "Note.ThroughTheInterface()", "CultureInfo.Clone()", "Note", "M:Note.ThroughTheInterface"),
            Verify.MemberwiseCloneOfOpenClass(1, "Note"),
        ]);

        // A private member gets no warning of its own, since a project that references the library could not use a line for it; with a
        // line of the library's own file, the member that calls it gets the warning. (QA9 round 5, 3)
        if (helperHasLine)
            test.ExpectedDiagnostics.Add(Verify.MemberClonesWithoutLine(2, "Note.Indirect()", "Note.Helper()", "Note", "M:Note.Indirect"));

        await test.RunAsync();
    }

    // The warning for a member is a help to the author of a library, whose lines another project goes by: an application gets none.
    [TestMethod]
    public async Task Member_ThatClonesItsOwnObjectGetsNoWarningInAnApplication()
    {
        var test = new Verify.Test {
            TestCode = """
                using System.Globalization;

                public class Note : CultureInfo
                {
                    public Note() : base("en-US") { }

                    public Note Copy() => (Note)Clone();

                    public static void Main() { }
                }
                """,
        };

        test.TestState.OutputKind = Microsoft.CodeAnalysis.OutputKind.ConsoleApplication;
        await test.RunAsync();
    }

    private const string ListedLibrary = """
        using System.Globalization;
        using Singulink.CodeAnalysis;

        namespace Lib
        {
            public class Tagged<[NonCopyable] T> : CultureInfo
            {
                public T Tag = default!;

                public Tagged() : base("en-US") { }
            }
        }
        """;

    // The walk of rule 2 over a member's types, by polarity. A result, a 'ref' or 'out' parameter and what can be read start at "out"; a
    // parameter, what can be set and an event's handler start at "in". Type arguments, of the type and of the types that contain it, array
    // and tuple elements, pointers and constraints keep the polarity, and a delegate turns it round for its parameters. The member can hand
    // its object on where a hiding type stands at "out". (QA9 rounds 5 and 6, 1 and 2)
    [TestMethod]
    public async Task Member_IsJudgedByOneWalkOverTheTypesOfItsSignature()
    {
        var library = await Verify.CompileLibraryAsync(Verify.Test.NetReferenceAssemblies, """
            using System;
            using System.Collections.Generic;
            using System.Threading.Tasks;
            using Singulink.CodeAnalysis;

            namespace LibW
            {
                public delegate void Sink(out Doc doc);
                public delegate void DocHandler(Doc doc);

                public class Doc
                {
                    public Doc Copy() => (Doc)MemberwiseClone();

                    // Out of the member.
                    public Dictionary<string, List<Doc[]>> Index() => new();
                    public unsafe delegate*<Doc> Pointer() => null;
                    public Sink Sinking() => (out Doc d) => d = this;
                    public Lazy<object> Later => new(() => this);
                    public readonly Func<Doc>? Getter;
                    public Action<Doc> Callback() => d => { };
                    public DocHandler? Handler { get; }

                    // Into the member.
                    public void All(params Action<Doc>[] callbacks) { }
                    public void Pair((Action<object> Callback, int N) pair) { }
                    public void Later2(Task<Action<Doc>> callback) { }
                    public void Ask(Action<Func<Doc>> callback) { }
                    public void Sinks(Action<Action<Doc>> callback) { }
                    public void Makes(Func<Action<Doc>> callback) { }
                    public Func<Action<Doc>> Maker() => () => d => { };
                    public Func<Doc> Getter2() => () => this;
                    public readonly Action<Doc>? Listener;
                    public List<Doc>.Enumerator Each() => default;
                    public Dictionary<int, Doc>.ValueCollection Values() => new Dictionary<int, Doc>().Values;
                    public void Callbacks(Dictionary<int, Action<Doc>>.ValueCollection callbacks) { }
                    public void Constrained<TCallbacks>(TCallbacks callbacks) where TCallbacks : IEnumerable<Action<Doc>> { }
                    public TDoc As<TDoc>() where TDoc : class => null!;
                    public int? Count() => null;
                    public DocHandler? OnTouch { get; set; }
                    public Action<object>? Field;
                    public void Pull(Func<int, Doc> source) { }
                    public void Compare(Doc other) { }
                    public void Fill(List<Doc> docs) { }

                    // Variance: an 'in' type argument turns the polarity round, an 'out' one keeps it, and one with no variance, or an
                    // array's element, goes both ways. A tuple is passed by value.
                    public void SortWith(IComparer<Doc> comparer) { }
                    public void ReportTo(IProgress<Doc> progress) { }
                    public void ObserveBy(IObserver<object> observer) { }
                    public IComparer<Doc> Comparer() => null!;
                    public void Read(IEnumerable<Doc> docs) { }
                    public IEnumerable<Doc> Everything() => [];
                    public void Take(Doc[] docs) { }
                    public void Pair2((Doc Doc, int N) pair) { }
                }

                public class Mid<[NonCopyable] T> : Doc { public T Tag = default!; }

                public static class Docs
                {
                    public static Mid<T> Make<[NonCopyable] T>() => new();
                }
            }
            """, analyzed: true, "LibW");

        var test = new Verify.Test {
            TestCode = """
                using System;
                using System.Threading.Tasks;
                using LibW;

                class C
                {
                    unsafe void Uses()
                    {
                        var doc = Docs.Make<Guard>();
                        _ = {|#0:doc.Index()|};
                        _ = {|#1:doc.Pointer()|};
                        _ = {|#2:doc.Sinking()|};
                        _ = {|#3:doc.Later|};
                        _ = {|#4:doc.Getter|};
                        _ = doc.Callback();
                        _ = doc.Handler;
                        {|#5:doc.All(d => { })|};
                        {|#6:doc.Pair((o => { }, 1))|};
                        {|#7:doc.Later2(Task.FromResult<Action<Doc>>(d => { }))|};
                        {|#8:doc.Ask(f => { })|};
                        doc.Sinks(a => { });
                        {|#9:doc.Makes(() => d => { })|};
                        _ = doc.Maker();
                        _ = {|#12:doc.Getter2()|};
                        _ = doc.Listener;
                        _ = {|#13:doc.Each()|};
                        _ = {|#14:doc.Values()|};
                        {|#15:doc.Callbacks(new System.Collections.Generic.Dictionary<int, Action<Doc>>().Values)|};
                        {|#16:doc.Constrained(new Action<Doc>[0])|};
                        _ = {|#17:doc.As<Doc>()|};
                        _ = doc.Count();
                        {|#10:doc.OnTouch|} = d => { };
                        {|#11:doc.Field|} = o => { };
                        doc.Pull(n => new Doc());
                        doc.Compare(new Doc());
                        {|#19:doc.Fill(new())|};
                        {|#20:doc.SortWith(null!)|};
                        {|#21:doc.ReportTo(null!)|};
                        {|#22:doc.ObserveBy(null!)|};
                        _ = doc.Comparer();
                        doc.Read([]);
                        _ = {|#23:doc.Everything()|};
                        {|#24:doc.Take([])|};
                        doc.Pair2((new Doc(), 1));
                    }

                    // A type parameter is walked by its constraints.
                    void Through<TX>(TX callbacks) where TX : System.Collections.Generic.IEnumerable<Action<Doc>> =>
                        {|#18:Docs.Make<Guard>().Constrained(callbacks)|};
                }
                """ + System.Environment.NewLine + Sources.Guard,
        };

        test.TestState.AdditionalFiles.Add((Verify.ListFileName, "T:LibW.Doc;clones=complete\nM:LibW.Doc.Copy;clones"));
        test.TestState.AdditionalReferences.Add(library);
        test.SolutionTransforms.Add((solution, projectId) => solution.WithProjectCompilationOptions(
            projectId, ((Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions)solution.GetProject(projectId)!.CompilationOptions!).WithAllowUnsafe(true)));
        const string Mid = "LibW.Mid<Guard>";

        foreach (var (location, member, type, id) in new[] {
            (0, "Doc.Index()", "System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<LibW.Doc[]>>", "M:LibW.Doc.Index"),
            (1, "Doc.Pointer()", "delegate*<LibW.Doc>", "M:LibW.Doc.Pointer"),
            (2, "Doc.Sinking()", "LibW.Sink", "M:LibW.Doc.Sinking"),
            (3, "Doc.Later", "System.Lazy<object>", "P:LibW.Doc.Later"),
            (4, "Doc.Getter", "System.Func<LibW.Doc>", "F:LibW.Doc.Getter"),
            (5, "Doc.All(params Action<Doc>[])", "System.Action<LibW.Doc>[]", "M:LibW.Doc.All(System.Action{LibW.Doc}[])"),
            (6, "Doc.Pair((Action<object> Callback, int N))", "(System.Action<object> Callback, int N)", "M:LibW.Doc.Pair(System.ValueTuple{System.Action{System.Object},System.Int32})"),
            (7, "Doc.Later2(Task<Action<Doc>>)", "System.Threading.Tasks.Task<System.Action<LibW.Doc>>", "M:LibW.Doc.Later2(System.Threading.Tasks.Task{System.Action{LibW.Doc}})"),
            (8, "Doc.Ask(Action<Func<Doc>>)", "System.Action<System.Func<LibW.Doc>>", "M:LibW.Doc.Ask(System.Action{System.Func{LibW.Doc}})"),
            (9, "Doc.Makes(Func<Action<Doc>>)", "System.Func<System.Action<LibW.Doc>>", "M:LibW.Doc.Makes(System.Func{System.Action{LibW.Doc}})"),
            (12, "Doc.Getter2()", "System.Func<LibW.Doc>", "M:LibW.Doc.Getter2"),
            (13, "Doc.Each()", "System.Collections.Generic.List<LibW.Doc>.Enumerator", "M:LibW.Doc.Each"),
            (14, "Doc.Values()", "System.Collections.Generic.Dictionary<int, LibW.Doc>.ValueCollection", "M:LibW.Doc.Values"),
            (15, "Doc.Callbacks(Dictionary<int, Action<Doc>>.ValueCollection)", "System.Collections.Generic.Dictionary<int, System.Action<LibW.Doc>>.ValueCollection", "M:LibW.Doc.Callbacks(System.Collections.Generic.Dictionary{System.Int32,System.Action{LibW.Doc}}.ValueCollection)"),
            (16, "Doc.Constrained<TCallbacks>(TCallbacks)", "System.Action<LibW.Doc>[]", "M:LibW.Doc.Constrained``1(``0)"),
            (17, "Doc.As<TDoc>()", "LibW.Doc", "M:LibW.Doc.As``1"),
            (18, "Doc.Constrained<TCallbacks>(TCallbacks)", "TX", "M:LibW.Doc.Constrained``1(``0)"),
            (19, "Doc.Fill(List<Doc>)", "System.Collections.Generic.List<LibW.Doc>", "M:LibW.Doc.Fill(System.Collections.Generic.List{LibW.Doc})"),
            (20, "Doc.SortWith(IComparer<Doc>)", "System.Collections.Generic.IComparer<LibW.Doc>", "M:LibW.Doc.SortWith(System.Collections.Generic.IComparer{LibW.Doc})"),
            (21, "Doc.ReportTo(IProgress<Doc>)", "System.IProgress<LibW.Doc>", "M:LibW.Doc.ReportTo(System.IProgress{LibW.Doc})"),
            (22, "Doc.ObserveBy(IObserver<object>)", "System.IObserver<object>", "M:LibW.Doc.ObserveBy(System.IObserver{System.Object})"),
            (23, "Doc.Everything()", "System.Collections.Generic.IEnumerable<LibW.Doc>", "M:LibW.Doc.Everything"),
            (24, "Doc.Take(Doc[])", "LibW.Doc[]", "M:LibW.Doc.Take(LibW.Doc[])"),
            (10, "Doc.OnTouch", "LibW.DocHandler", "P:LibW.Doc.OnTouch"),
            (11, "Doc.Field", "System.Action<object>", "F:LibW.Doc.Field"),
        })
        {
            test.ExpectedDiagnostics.Add(Verify.CloningUseHandsBack(location, member, Mid, type, "LibW.Doc", "Guard", Mid, id));
        }

        await test.RunAsync();
    }

    // A class of the framework that derives from a class that clones itself, such as UTF8Encoding, is covered by the built-in list's
    // scan, whichever assembly of the reference pack it is in: its members are judged by the lines and their signatures, not as members
    // that can clone. A class of a library that was not built with the analyzers, between the framework's class and the object's own, is
    // not covered: its members can clone, and the message says why. A 'clones' line for one of its members speaks for that member alone,
    // and a line with no marker vouches for its member. A line for the class itself that says 'clones=complete' covers its members: one
    // that no line names neither clones the object nor hands it on, and the line gets no warning for having no effect. (QA9 round 5, 2)
    [TestMethod]
    [DataRow("")]
    [DataRow("M:LibK.Mid.Other;clones")]
    [DataRow("M:LibK.Mid.Size")]
    [DataRow("T:LibK.Mid;clones=complete\nM:LibK.Mid.Other;clones")]
    public async Task Member_OfAClassThatTheScanCoversIsKnownAndOneOfAnUnanalyzedLibraryIsNot(string line)
    {
        var library = await Verify.CompileLibraryAsync(Verify.Test.NetReferenceAssemblies, """
            using System.Globalization;
            using System.Text;
            using Singulink.CodeAnalysis;

            namespace LibK
            {
                public class Utf8<[NonCopyable] T> : UTF8Encoding { public T Tag = default!; }
                public class Ascii<[NonCopyable] T> : ASCIIEncoding { public T Tag = default!; }
                public class Unicode<[NonCopyable] T> : UnicodeEncoding { public T Tag = default!; }
                public class Gregorian<[NonCopyable] T> : GregorianCalendar { public T Tag = default!; }
                public class Hebrew<[NonCopyable] T> : HebrewCalendar { public T Tag = default!; }
                public class Lunisolar<[NonCopyable] T> : KoreanLunisolarCalendar { public T Tag = default!; }

                public class Mid : UTF8Encoding
                {
                    public virtual int Size() => 1;

                    public object Other() => Clone();
                }

                public class Leaf<[NonCopyable] T> : Mid { public T Tag = default!; }

                public static class Make
                {
                    public static Utf8<T> Utf8<[NonCopyable] T>() => new();
                    public static Ascii<T> Ascii<[NonCopyable] T>() => new();
                    public static Unicode<T> Unicode<[NonCopyable] T>() => new();
                    public static Gregorian<T> Gregorian<[NonCopyable] T>() => new();
                    public static Hebrew<T> Hebrew<[NonCopyable] T>() => new();
                    public static Lunisolar<T> Lunisolar<[NonCopyable] T>() => new();
                    public static Leaf<T> Leaf<[NonCopyable] T>() => new();
                }
            }
            """, analyzed: false, "LibK");

        var test = new Verify.Test {
            TestCode = """
                using System;
                using LibK;

                class C
                {
                    void Uses()
                    {
                        var utf8 = Make.Utf8<Guard>();
                        _ = utf8.GetByteCount("a") + utf8.GetMaxByteCount(1) + utf8.GetPreamble().Length + utf8.Preamble.Length;
                        _ = utf8.GetEncoder();
                        _ = utf8.GetDecoder();
                        _ = utf8.GetBytes("a".AsSpan(), new byte[4]);
                        _ = utf8.GetString(new byte[1]);
                        var ascii = Make.Ascii<Guard>();
                        _ = ascii.GetByteCount("a") + ascii.GetMaxCharCount(1) + ascii.IsSingleByte.GetHashCode();
                        var unicode = Make.Unicode<Guard>();
                        _ = unicode.GetPreamble().Length + unicode.GetMaxByteCount(2);
                        _ = unicode.GetDecoder();
                        var gregorian = Make.Gregorian<Guard>();
                        _ = gregorian.GetYear(DateTime.Now) + gregorian.GetDaysInMonth(2024, 2) + gregorian.Eras.Length;
                        _ = gregorian.AddMonths(DateTime.Now, 1);
                        _ = gregorian.CalendarType;
                        var hebrew = Make.Hebrew<Guard>();
                        _ = hebrew.IsLeapYear(5784) && hebrew.GetMonthsInYear(5784) > 0;
                        var lunisolar = Make.Lunisolar<Guard>();
                        _ = lunisolar.GetSexagenaryYear(DateTime.Now) + lunisolar.GetEra(DateTime.Now);
                        var leaf = Make.Leaf<Guard>();
                        _ = {|#0:leaf.Size()|};
                        _ = {|#1:leaf.Other()|};
                        _ = leaf.GetByteCount("a");
                    }
                }
                """ + System.Environment.NewLine + Sources.Guard,
        };

        // The factories of a library that was not built with the analyzers hand out a new object.
        string factories = string.Join("\n", new[] { "Utf8", "Ascii", "Unicode", "Gregorian", "Hebrew", "Lunisolar", "Leaf" }.Select(f => $"M:LibK.Make.{f}``1;new"));
        test.TestState.AdditionalFiles.Add((Verify.ListFileName, factories + "\n" + line));
        test.TestState.AdditionalReferences.Add(library);

        const string Leaf = "LibK.Leaf<Guard>";
        bool complete = line.Contains("clones=complete", StringComparison.Ordinal);

        if (!complete && line is not "M:LibK.Mid.Size")
            test.ExpectedDiagnostics.Add(Verify.CloningUseOfMemberOfUncoveredClass(0, "Mid.Size()", Leaf, "LibK.Mid", "System.Text.Encoding", "Guard", Leaf, "M:LibK.Mid.Size", "T:LibK.Mid"));

        if (line.Contains("M:LibK.Mid.Other;clones", StringComparison.Ordinal))
            test.ExpectedDiagnostics.Add(Verify.CloningUseOfCloningMember(1, "Mid.Other()", Leaf, "Guard", Leaf));
        else
            test.ExpectedDiagnostics.Add(Verify.CloningUseOfMemberOfUncoveredClass(1, "Mid.Other()", Leaf, "LibK.Mid", "System.Text.Encoding", "Guard", Leaf, "M:LibK.Mid.Other", "T:LibK.Mid"));

        await test.RunAsync();
    }

    private const string HandingLibrary = """
        using System;
        using System.Collections.Generic;
        using System.Threading.Tasks;
        using Singulink.CodeAnalysis;

        namespace LibD
        {
            public interface IVisitor
            {
                void Visit(Doc doc);
            }

            public class Doc
            {
                public int Size => 1;

                public Doc Copy() => (Doc)MemberwiseClone();

                public IEnumerable<Doc> Parts() => [this];

                public Task<Doc> LoadAsync() => Task.FromResult(this);

                public Doc[] AsArray() => [this];

                public (Doc, int) Pair() => (this, 1);

                public Func<Doc> Maker() => () => this;

                public List<object> Bag() => [this];

                public void Visit(Action<Doc> visitor) => visitor(this);

                public void VisitAny(Action<object> visitor) => visitor(this);

                public void Fill(out Doc doc) => doc = this;

                public IEnumerable<string> Names() => [];

                public void Run(Action<int> action) => action(1);

                public void Accept(IVisitor visitor) => visitor.Visit(this);

                public virtual object Format() => "doc";

                public virtual Doc Self() => this;

                public event EventHandler? Changed;

                public event Action<int>? Counted;

                protected void Raise()
                {
                    Changed?.Invoke(this, EventArgs.Empty);
                    Counted?.Invoke(1);
                }
            }

            public class Mid<[NonCopyable] T> : Doc
            {
                public T Tag = default!;

                public override object Format() => this;

                public Doc Mine() => this;
            }

            public static class Docs
            {
                public static Mid<T> Make<[NonCopyable] T>() => new();
            }
        }
        """;

    // 'clones=complete' is a value of the marker 'clones', and only a class's line takes it. A member line with it, another value, the
    // word on its own, and a second marker get SCA1042 as lines that cannot be parsed, and 'complete' written as a marker of its own is
    // pointed to 'clones=complete'; one for a struct or for a sealed class gets it as a 'clones' line does. A line for a class that derives
    // from a class that clones itself has an effect: it covers the members of that class, and gets no warning.
    [TestMethod]
    public Task CompleteLine_IsAValueOfClonesForAClassAlone() => Verify.AnalyzerAsync(
        """
        using System.Text;

        public sealed class Closed { }

        public struct Value { }

        public class Open { public object Copy() => new Open(); }

        public abstract class Text : Encoding { }
        """,
        """
        T:Text;clones=complete
        T:Text;clones = complete
        M:Open.Copy;clones=complete
        T:Open;clones=all
        T:Open;complete
        T:Open;clones;complete
        T:Value;clones=complete
        T:Closed;clones=complete
        """,
        Verify.SafeListLine(3, 1, 28, "'clones=complete' is for a class; a method, a property or an event takes 'clones' alone"),
        Verify.SafeListLine(4, 1, 18, "'clones' takes no value other than 'complete' or 'none', as in 'clones=complete'"),
        Verify.SafeListLine(5, 1, 16, "'complete' is a value of the marker 'clones', written 'clones=complete'"),
        Verify.SafeListLine(6, 1, 23, "'complete' is a value of the marker 'clones', written 'clones=complete'"),
        Verify.SafeListLine(7, 1, 24, "the marker 'clones' is for a class, a method, a property or an event"),
        Verify.SafeListClonesHasNoEffect(8, 1, 25, "T:Closed", "the class is sealed and not generic, so no class can derive from it and no type can be bound to it"));

    // A class has complete member lines where any 'clones' line for it says 'clones=complete', in a project's file or in one that came in
    // a package. A plain 'clones' line says nothing of completeness, so it takes nothing away, in another file or in the same one.
    [TestMethod]
    [DataRow("T:LibC.Doc;clones=complete", "T:LibC.Doc;clones")]
    [DataRow("T:LibC.Doc;clones", "T:LibC.Doc;clones=complete")]
    [DataRow("T:LibC.Doc;clones\nT:LibC.Doc;clones=complete", "")]
    [DataRow("T:LibC.Doc;clones", "T:LibC.Doc;clones")]
    public async Task CompleteLine_AppliesWhereAnyLineForTheClassSaysIt(string projectLines, string packageLines)
    {
        var library = await Verify.CompileLibraryAsync(Verify.Test.NetReferenceAssemblies, DocLibrary, analyzed: true, "LibC");
        var test = new Verify.Test {
            TestCode = """
                using LibC;

                class C
                {
                    void Uses() => _ = {|#0:Docs.Make<Guard>().Size|};
                }
                """ + System.Environment.NewLine + Sources.Guard,
        };

        test.TestState.AdditionalFiles.Add((Verify.ListFileName, projectLines));

        if (packageLines.Length > 0)
            test.TestState.AdditionalFiles.Add(("packages/libc/1.0.0/build/NonCopyableSymbols.LibC.txt", packageLines));

        test.TestState.AdditionalReferences.Add(library);
        const string TaggedDoc = "LibC.TaggedDoc<Guard>";

        if (!(projectLines + packageLines).Contains("clones=complete", StringComparison.Ordinal))
            test.ExpectedDiagnostics.Add(Verify.CloningUseOfUnknownMember(0, "Doc.Size", TaggedDoc, "LibC.Doc", "LibC.Doc", "Guard", TaggedDoc, "P:LibC.Doc.Size", "T:LibC.Doc"));

        await test.RunAsync();
    }

    // A file's line may say 'clones=complete' for a class that the built-in framework symbol list names with 'clones' alone, such as
    // XmlSchemaObject: its author vouches for the members. It gets no warning, in a project's file or in one that came in a package, and
    // it covers the XmlSchema classes of the same assembly, also one with a plain 'clones' line of its own. Without it, every member counts
    // as one that can clone, and the warning gives the line to add.
    [TestMethod]
    [DataRow("")]
    [DataRow("T:System.Xml.Schema.XmlSchemaObject;clones=complete")]
    [DataRow("T:System.Xml.Schema.XmlSchemaObject;clones=complete\nT:System.Xml.Schema.XmlSchemaAnnotation;clones")]
    [DataRow("T:System.Xml.Schema.XmlSchemaObject;clones=complete", true)]
    public async Task CompleteLineForAClassThatTheBuiltInListNamesPlain_TakesEffect(string lines, bool inPackage = false)
    {
        string path = inPackage ? "/packages/lib/1.0.0/build/NonCopyableSymbols.Lib.txt" : Verify.ListFileName;
        var library = await Verify.CompileLibraryAsync(Verify.Test.NetReferenceAssemblies, """
            using System.Xml.Schema;
            using Singulink.CodeAnalysis;

            namespace LibX
            {
                public class Note<[NonCopyable] T> : XmlSchemaAnnotation { public T Tag = default!; }

                public static class Notes
                {
                    public static Note<T> Make<[NonCopyable] T>() => new();
                }
            }
            """, analyzed: true, "LibX");

        var test = new Verify.Test {
            TestCode = """
                using LibX;

                class C
                {
                    void Uses()
                    {
                        var note = Notes.Make<Guard>();
                        _ = {|#0:note.Id|};
                        _ = {|#1:note.LineNumber|};
                    }
                }
                """ + System.Environment.NewLine + Sources.Guard,
        };

        test.TestState.AnalyzerConfigFiles.Add(("/.globalconfig", "is_global = true" + "\n" + "build_property.NuGetPackageRoot = /packages/" + "\n"));

        if (lines.Length > 0)
            test.TestState.AdditionalFiles.Add((path, lines));

        test.TestState.AdditionalReferences.Add(library);
        const string Note = "LibX.Note<Guard>";
        const string Schema = "System.Xml.Schema.XmlSchemaObject";

        if (lines.Length is 0)
        {
            test.ExpectedDiagnostics.Add(Verify.CloningUseOfUnknownMember(0, "XmlSchemaAnnotation.Id", Note, "System.Xml.Schema.XmlSchemaAnnotation", Schema, "Guard", Note, "P:System.Xml.Schema.XmlSchemaAnnotation.Id", "T:" + Schema));
            test.ExpectedDiagnostics.Add(Verify.CloningUseOfUnknownMember(1, "XmlSchemaObject.LineNumber", Note, Schema, Schema, "Guard", Note, "P:System.Xml.Schema.XmlSchemaObject.LineNumber", "T:" + Schema));
        }

        await test.RunAsync();
    }

    private const string UncheckedDocLibrary = """
        namespace LibU
        {
            public class Doc
            {
                public Doc Copy() => (Doc)MemberwiseClone();

                public void Touch() { }
            }

            public class Tagged<T> : Doc { public T Tag = default!; }
        }
        """;

    // The binding can be written in the base list of a class of a library, as 'Bound : Tagged<Token>'. Where that library was not built
    // with the analyzers, nothing vouches for what the class's own members do, and they are judged as those of a class between: a member
    // that no line names gets the warning unless a line for the class says 'clones=complete'. That holds for an object of a class of the
    // project that derives from it, for an object of its own class that a factory hands out, and for one of a class below it. Where the
    // library was built with the analyzers, its own build judged the class, and its members get no such warning. (QA9 round 8, F1)
    [TestMethod]
    [DataRow(false, "")]
    [DataRow(false, "T:LibB.Bound;clones=complete\nT:LibB.Bound2`1;clones=complete")]
    [DataRow(true, "")]
    public async Task Member_OfAClassThatBindsInItsOwnBaseListIsJudgedByTheLinesUnlessItsLibraryWasAnalyzed(bool analyzed, string lines)
    {
        var docs = await Verify.CompileLibraryAsync(Verify.Test.NetReferenceAssemblies, UncheckedDocLibrary, analyzed: false, "LibU");
        var bound = await Verify.CompileLibraryAsync(Verify.Test.NetReferenceAssemblies, """
            using Singulink.CodeAnalysis;

            namespace LibB
            {
                [NonCopyable]
                public struct Token { public int Value; }

                public class Bound : LibU.Tagged<Token>
                {
                    public void BoundSneak() => _ = Copy();
                }

                public class Below : Bound { }

                public class Bound2<T> : LibU.Tagged<Token>
                {
                    public void B2Sneak() => _ = Copy();
                }

                public static class Make
                {
                    public static Bound NewBound() => new();

                    public static Below NewBelow() => new();

                    public static Bound2<int> NewBound2() => new();
                }
            }
            """, analyzed, "LibB", docs);

        var test = new Verify.Test {
            TestCode = """
                using LibB;

                public class PB : Bound { }

                class C
                {
                    void Uses()
                    {
                        {|#0:new PB().BoundSneak()|};
                        {|#1:Make.NewBound2().B2Sneak()|};
                        {|#2:Make.NewBelow().BoundSneak()|};
                        {|#3:Make.NewBound().BoundSneak()|};
                    }
                }
                """,
        };

        string factories = "M:LibB.Make.NewBound;new\nM:LibB.Make.NewBelow;new\nM:LibB.Make.NewBound2;new";
        test.TestState.AdditionalFiles.Add((Verify.ListFileName, "T:LibU.Doc;clones=complete\nM:LibU.Doc.Copy;clones\n" + factories + (lines.Length > 0 ? "\n" + lines : string.Empty)));
        test.TestState.AdditionalReferences.Add(docs);
        test.TestState.AdditionalReferences.Add(bound);

        if (!analyzed && lines.Length is 0)
        {
            foreach (var (location, member, receiver, declaring, id) in new[] {
                (0, "Bound.BoundSneak()", "PB", "LibB.Bound", "M:LibB.Bound.BoundSneak"),
                (1, "Bound2<T>.B2Sneak()", "LibB.Bound2<int>", "LibB.Bound2<int>", "M:LibB.Bound2`1.B2Sneak"),
                (2, "Bound.BoundSneak()", "LibB.Below", "LibB.Bound", "M:LibB.Bound.BoundSneak"),
                (3, "Bound.BoundSneak()", "LibB.Bound", "LibB.Bound", "M:LibB.Bound.BoundSneak"),
            })
            {
                string declaringId = id[..id.LastIndexOf('.')].Replace("M:", "T:", StringComparison.Ordinal);
                test.ExpectedDiagnostics.Add(Verify.CloningUseOfMemberOfUncoveredClass(location, member, receiver, declaring, "LibU.Doc", "LibB.Token", "LibU.Tagged<LibB.Token>", id, declaringId));
            }
        }

        await test.RunAsync();
    }

    // In a library whose own class clones itself, the warning for a member that clones its object without a line also says that the
    // class's line takes 'clones=complete' once the lines name every such member. A class whose class that clones itself is of another
    // assembly gets no such advice: its members are covered by that class's line.
    [TestMethod]
    public Task MemberThatClonesWithoutLine_InALibrarysOwnCloningClassAdvisesCompleteToo() => Verify.AnalyzerAsync(
        """
        using System;

        public class Doc : ICloneable
        {
            public object Clone() => {|#0:MemberwiseClone()|};

            public Doc Copy() => (Doc){|#1:Clone()|};
        }
        """,
        "T:Doc;clones\nM:Doc.Clone;clones",
        Verify.MemberwiseCloneOfOpenClass(0, "Doc"),
        Verify.MemberClonesWithoutLine(1, "Doc.Copy()", "Doc.Clone()", "Doc", "M:Doc.Copy", "Doc", "T:Doc"));

    // A 'clones' line for a class between the class that clones itself and the object's class, as one that says 'clones=complete' for
    // it, does not make that class the one that clones itself: a binding above it, in the base list of a class of another library, is
    // still found, and the messages name the class that clones itself.
    [TestMethod]
    public async Task CompleteLineOfAClassBetween_KeepsTheBindingAboveItAndTheClassThatClonesItself()
    {
        var library = await Verify.CompileLibraryAsync(Verify.Test.NetReferenceAssemblies, """
            using System.Globalization;
            using Singulink.CodeAnalysis;

            namespace LibM
            {
                [NonCopyable]
                public struct Token { public int Value; }

                public class Tagged<[NonCopyable] T> : CultureInfo
                {
                    public T Tag = default!;

                    public Tagged() : base("en-US") { }
                }

                public class Mid : Tagged<Token>
                {
                    public int Size() => 1;
                }

                public static class Make
                {
                    public static Mid Mid() => new();
                }
            }
            """, analyzed: false, "LibM");

        var test = new Verify.Test {
            TestCode = """
                using System.Globalization;
                using LibM;

                class C
                {
                    void Uses()
                    {
                        var mid = Make.Mid();
                        _ = mid.Size();
                        CultureInfo culture = {|#0:mid|};
                    }
                }
                """,
        };

        test.TestState.AdditionalFiles.Add((Verify.ListFileName, "M:LibM.Make.Mid;new\nT:LibM.Mid;clones=complete"));
        test.TestState.AdditionalReferences.Add(library);
        test.ExpectedDiagnostics.Add(Verify.CloningUseConversion(0, "LibM.Mid", Culture, Culture, "LibM.Token", "LibM.Tagged<LibM.Token>"));
        await test.RunAsync();
    }

    // A 'clones' line can name an event, and a line for a member that has none gets SCA1042 as for a method or a property. A line of any
    // marker that names an accessor of a property or an event has no effect, since the analyzer looks up every use of an accessor under
    // its property or event: it gets SCA1042 with the line to write instead. The method that the compiler makes for a property of an
    // extension block is no accessor, and its line gets no such warning. (QA9 round 9, N1)
    [TestMethod]
    public Task ClonesLine_NamesAnEventAndALineForAnAccessorGetsAWarning() => Verify.AnalyzerAsync(
        """
        using System;

        public class Open
        {
            public event EventHandler? Changed;

            public int Size { get; set; }

            public void Raise() => Changed?.Invoke(this, EventArgs.Empty);
        }

        public static class OpenExtensions
        {
            extension(Open open)
            {
                public int Twice => open.Size * 2;
            }
        }
        """,
        """
        E:Open.Changed;clones
        E:Open.Missing;clones
        M:Open.add_Changed(System.EventHandler);clones
        M:Open.remove_Changed(System.EventHandler)
        M:Open.get_Size;clones
        M:Open.set_Size(System.Int32);borrows
        M:OpenExtensions.get_Twice(Open)
        """,
        Verify.SafeListClonesHasNoEffect(2, 1, 22, "E:Open.Missing", "the compilation has no event of that ID"),
        Verify.SafeListNamesAccessor(3, 47, "M:Open.add_Changed(System.EventHandler)", "E:Open.Changed"),
        Verify.SafeListNamesAccessor(4, 43, "M:Open.remove_Changed(System.EventHandler)", "E:Open.Changed"),
        Verify.SafeListNamesAccessor(5, 23, "M:Open.get_Size", "P:Open.Size"),
        Verify.SafeListNamesAccessor(6, 38, "M:Open.set_Size(System.Int32)", "P:Open.Size"));

    private const string EventLibrary = """
        using System;
        using Singulink.CodeAnalysis;

        namespace LibE
        {
            [NonCopyable]
            public struct Token { public int Value; }

            public class Doc
            {
                public Doc Copy() => (Doc)MemberwiseClone();
            }

            public class Tagged<T> : Doc { public T Tag = default!; }

            public class Board : Tagged<Token>
            {
                public event Action? Changed { add { _ = Copy(); } remove { } }

                public void Touch() { }
            }

            public static class Make
            {
                public static Board Board() => new();
            }
        }
        """;

    // An event whose line says 'clones' clones the object, or hands it on, where a handler is added or removed: both get the warning
    // of a member that clones, also where the class's line says 'clones=complete'. Without its line, the complete class's event counts
    // as one that does neither; in a class whose line does not say 'clones=complete', a line with no marker for the event vouches for it.
    // (QA9 round 9, N1)
    [TestMethod]
    [DataRow("T:LibE.Board;clones=complete\nE:LibE.Board.Changed;clones")]
    [DataRow("T:LibE.Board;clones=complete")]
    [DataRow("E:LibE.Board.Changed")]
    public async Task Event_WithAClonesLineGetsAWarningWhereAHandlerIsAddedOrRemoved(string lines)
    {
        var library = await Verify.CompileLibraryAsync(Verify.Test.NetReferenceAssemblies, EventLibrary, analyzed: false, "LibE");
        var test = new Verify.Test {
            TestCode = """
                using LibE;

                class C
                {
                    void Uses()
                    {
                        var board = Make.Board();
                        {|#0:board.Changed|} += Handler;
                        {|#1:board.Changed|} -= Handler;
                    }

                    static void Handler() { }
                }
                """,
        };

        test.TestState.AdditionalFiles.Add((Verify.ListFileName, "T:LibE.Doc;clones=complete\nM:LibE.Doc.Copy;clones\nM:LibE.Make.Board;new\n" + lines));
        test.TestState.AdditionalReferences.Add(library);
        const string Bound = "LibE.Tagged<LibE.Token>";

        if (lines.Contains("E:LibE.Board.Changed;clones", StringComparison.Ordinal))
        {
            test.ExpectedDiagnostics.Add(Verify.CloningUseOfCloningMember(0, "Board.Changed", "LibE.Board", "LibE.Token", Bound));
            test.ExpectedDiagnostics.Add(Verify.CloningUseOfCloningMember(1, "Board.Changed", "LibE.Board", "LibE.Token", Bound));
        }

        await test.RunAsync();
    }

    private const string BothLibrary = """
        using Singulink.CodeAnalysis;

        namespace LibT
        {
            [NonCopyable]
            public struct Token { public int Value; }

            public class Doc
            {
                public Doc Copy() => (Doc)MemberwiseClone();
            }

            public class Tagged<T> : Doc { public T Tag = default!; }

            public class Both<T> : Tagged<Token>
            {
                public void Sneak() => _ = Copy();
            }

            public class Far<T> : Doc
            {
                public T Tag = default!;

                public void Op() { }
            }

            public static class Make
            {
                public static Both<T> Both<T>() => new();

                public static Far<T> Far<T>() => new();
            }
        }
        """;

    // A library that was not built with the analyzers, whose classes put members of their own in the place of members of CultureInfo.
    private const string OverridingLibrary = """
        using System;
        using System.Globalization;
        using System.Runtime.InteropServices;

        namespace LibO
        {
            public class TaggedCulture<T> : CultureInfo
            {
                public T Tag = default!;

                public TaggedCulture() : base("en-US") { }
            }

            // Overrides a member that the code of CultureInfo calls: GetFormat returns NumberFormat.
            public class Bound : TaggedCulture<GCHandle>
            {
                public override NumberFormatInfo NumberFormat
                {
                    get { _ = Clone(); return base.NumberFormat; }
                    set => base.NumberFormat = value;
                }
            }

            // Overrides only a member that clones already.
            public class Recloning : TaggedCulture<GCHandle>
            {
                public override object Clone() => base.Clone();
            }

            // Overrides nothing.
            public class Plain : TaggedCulture<GCHandle>
            {
                public int Size() => 1;
            }

            // Implements a member of an interface of CultureInfo again.
            public class Reimplementing : TaggedCulture<GCHandle>, IFormatProvider
            {
                object? IFormatProvider.GetFormat(Type? formatType)
                {
                    _ = Clone();
                    return null;
                }
            }

            // Has a virtual member of its own, which the code of CultureInfo cannot call.
            public class Mid : TaggedCulture<GCHandle>
            {
                public virtual void Hook() { }

                public void RunHook() => Hook();
            }

            public class Low : Mid
            {
                public override void Hook() => _ = Clone();
            }

            // Its binding comes from its own type argument alone.
            public class Open<T> : CultureInfo
            {
                public T Tag = default!;

                public Open() : base("en-US") { }

                public override NumberFormatInfo NumberFormat
                {
                    get { _ = Clone(); return base.NumberFormat; }
                    set => base.NumberFormat = value;
                }
            }
        }
        """;

    private const string Handle = "System.Runtime.InteropServices.GCHandle";
    private const string TaggedHandleCulture = "LibO.TaggedCulture<System.Runtime.InteropServices.GCHandle>";

    private static async Task<Verify.Test> CreateOverridingTestAsync(string code, string lines)
    {
        var library = await Verify.CompileLibraryAsync(Verify.Test.NetReferenceAssemblies, OverridingLibrary, analyzed: false, "LibO");
        var test = new Verify.Test { TestCode = code };

        if (lines.Length > 0)
            test.TestState.AdditionalFiles.Add((Verify.ListFileName, lines));

        test.TestState.AdditionalReferences.Add(library);
        return test;
    }

    // A line for a member says what that member's own code does, and that code can call a virtual member of its own object, which runs
    // the override of a class below. CultureInfo.GetFormat returns NumberFormat, which the library's class overrides with a getter that
    // clones the culture. The analyzer cannot tell which member of CultureInfo calls which, so every member of CultureInfo counts as
    // one that can run the override, and gets the warning where the override's 'clones' line says that it clones. A member of object
    // calls no virtual member of its object, and gets none.
    [TestMethod]
    public async Task Member_OfABaseClassGetsAWarningWhereAnOverrideBelowItClones()
    {
        var test = await CreateOverridingTestAsync(
            """
            using System.Globalization;
            using LibO;

            class C
            {
                void Uses(Bound culture)
                {
                    _ = {|#0:culture.GetFormat(typeof(NumberFormatInfo))|};
                    _ = {|#1:culture.Name|};
                    _ = {|#2:culture.ToString()|};
                    _ = {|#3:culture.NumberFormat|};
                    _ = culture.GetType();
                }
            }
            """,
            "T:LibO.Bound;clones=complete\nP:LibO.Bound.NumberFormat;clones");

        test.ExpectedDiagnostics.Add(Verify.CloningUseRunsMemberThatClones(0, "CultureInfo.GetFormat(Type?)", "LibO.Bound", "Bound.NumberFormat", Culture, Handle, TaggedHandleCulture));
        test.ExpectedDiagnostics.Add(Verify.CloningUseRunsMemberThatClones(1, "CultureInfo.Name", "LibO.Bound", "Bound.NumberFormat", Culture, Handle, TaggedHandleCulture));
        test.ExpectedDiagnostics.Add(Verify.CloningUseRunsMemberThatClones(2, "CultureInfo.ToString()", "LibO.Bound", "Bound.NumberFormat", Culture, Handle, TaggedHandleCulture));
        test.ExpectedDiagnostics.Add(Verify.CloningUseOfCloningMember(3, "Bound.NumberFormat", "LibO.Bound", Handle, TaggedHandleCulture));
        await test.RunAsync();
    }

    // Where nothing says what the members of the overriding class do, the override can clone: the class is of a library that was not
    // built with the analyzers, and no line that says 'clones=complete' covers it. A line for the class that says it, or a line for the
    // override with no marker, says that the override does not clone, and the members of the base class get no warning.
    [TestMethod]
    [DataRow("", true)]
    [DataRow("T:LibO.Bound;clones=complete", false)]
    [DataRow("P:LibO.Bound.NumberFormat", false)]
    public async Task Member_OfABaseClassGetsAWarningWhereNothingSaysWhatAnOverrideBelowItDoes(string lines, bool reported)
    {
        var test = await CreateOverridingTestAsync(
            """
            using System.Globalization;
            using LibO;

            class C
            {
                void Uses(Bound culture)
                {
                    _ = {|#0:culture.GetFormat(typeof(NumberFormatInfo))|};
                    _ = {|#1:culture.Name|};
                    _ = culture.GetType();
                }
            }
            """,
            lines);

        if (reported)
        {
            test.ExpectedDiagnostics.Add(Verify.CloningUseRunsUnknownMember(
                0, "CultureInfo.GetFormat(Type?)", "LibO.Bound", "Bound.NumberFormat", "LibO.Bound", Culture, Culture, Handle, TaggedHandleCulture, "P:LibO.Bound.NumberFormat", "T:LibO.Bound"));
            test.ExpectedDiagnostics.Add(Verify.CloningUseRunsUnknownMember(
                1, "CultureInfo.Name", "LibO.Bound", "Bound.NumberFormat", "LibO.Bound", Culture, Culture, Handle, TaggedHandleCulture, "P:LibO.Bound.NumberFormat", "T:LibO.Bound"));
        }

        await test.RunAsync();
    }

    // A class that puts nothing in the place of a member of the base class gives the base class nothing to run: one that overrides
    // nothing, and one that overrides only a member that clones already, since a member of the base class that runs that one counts as
    // one that clones by its own line.
    [TestMethod]
    public async Task Member_OfABaseClassGetsNoWarningWhereTheClassBelowReplacesNothingThatItCanRun()
    {
        var test = await CreateOverridingTestAsync(
            """
            using System.Globalization;
            using LibO;

            class C
            {
                void Uses(Plain plain, Recloning recloning)
                {
                    _ = plain.GetFormat(typeof(NumberFormatInfo));
                    _ = plain.Name;
                    _ = recloning.GetFormat(typeof(NumberFormatInfo));
                    _ = recloning.Name;
                    _ = {|#0:recloning.Clone()|};
                }
            }
            """,
            string.Empty);

        // The override of Clone is a member of the library's class, which no line covers.
        test.ExpectedDiagnostics.Add(Verify.CloningUseOfMemberOfUncoveredClass(
            0, "Recloning.Clone()", "LibO.Recloning", "LibO.Recloning", Culture, Handle, TaggedHandleCulture, "M:LibO.Recloning.Clone", "T:LibO.Recloning"));
        await test.RunAsync();
    }

    // A member that implements a member of an interface of the base class again is run where the code of the base class calls its
    // object through the interface, so it is judged as an override is.
    [TestMethod]
    [DataRow("", true)]
    [DataRow("T:LibO.Reimplementing;clones=complete", false)]
    public async Task Member_OfABaseClassGetsAWarningWhereAClassBelowItImplementsItsInterfaceAgain(string lines, bool reported)
    {
        var test = await CreateOverridingTestAsync(
            """
            using LibO;

            class C
            {
                void Uses(Reimplementing culture)
                {
                    _ = {|#0:culture.Name|};
                }
            }
            """,
            lines);

        if (reported)
        {
            test.ExpectedDiagnostics.Add(Verify.CloningUseRunsUnknownMember(
                0, "CultureInfo.Name", "LibO.Reimplementing", "Reimplementing.IFormatProvider.GetFormat(Type?)", "LibO.Reimplementing", Culture, Culture, Handle, TaggedHandleCulture,
                "M:LibO.Reimplementing.System#IFormatProvider#GetFormat(System.Type)", "T:LibO.Reimplementing"));
        }

        await test.RunAsync();
    }

    // The code of a class can run only an override of a member that it has or inherits. An override of a virtual member that a class
    // below CultureInfo declares is nothing that a member of CultureInfo can run; a member of the class that declares it can.
    [TestMethod]
    public async Task Member_OfABaseClassIsJudgedOnlyByTheOverridesOfMembersThatItsClassHas()
    {
        var test = await CreateOverridingTestAsync(
            """
            using LibO;

            class C
            {
                void Uses(Low culture)
                {
                    _ = culture.Name;
                    {|#0:culture.RunHook()|};
                    {|#1:culture.Hook()|};
                }
            }
            """,
            "T:LibO.Mid;clones=complete\nT:LibO.Low;clones=complete\nM:LibO.Low.Hook;clones");

        test.ExpectedDiagnostics.Add(Verify.CloningUseRunsMemberThatClones(0, "Mid.RunHook()", "LibO.Low", "Low.Hook()", "LibO.Mid", Handle, TaggedHandleCulture));
        test.ExpectedDiagnostics.Add(Verify.CloningUseOfCloningMember(1, "Low.Hook()", "LibO.Low", Handle, TaggedHandleCulture));
        await test.RunAsync();
    }

    // The same holds for a generic class of such a library whose binding comes from its own type argument alone: a member of the base
    // class gets the warning until a line says what the override does.
    [TestMethod]
    [DataRow("", true)]
    [DataRow("T:LibO.Open`1;clones=complete", false)]
    [DataRow("P:LibO.Open`1.NumberFormat", false)]
    public async Task Member_OfABaseClassIsJudgedByTheOverridesOfAGenericClassBelowItToo(string lines, bool reported)
    {
        var test = await CreateOverridingTestAsync(
            """
            using System.Globalization;
            using System.Runtime.InteropServices;
            using LibO;

            class C
            {
                void Uses(Open<GCHandle> culture)
                {
                    _ = {|#0:culture.GetFormat(typeof(NumberFormatInfo))|};
                }
            }
            """,
            lines);

        if (reported)
        {
            test.ExpectedDiagnostics.Add(Verify.CloningUseRunsUnknownMember(
                0, "CultureInfo.GetFormat(Type?)", "LibO.Open<System.Runtime.InteropServices.GCHandle>", "Open<T>.NumberFormat", "LibO.Open<System.Runtime.InteropServices.GCHandle>", Culture, Culture, Handle,
                "LibO.Open<System.Runtime.InteropServices.GCHandle>", "P:LibO.Open`1.NumberFormat", "T:LibO.Open`1"));
        }

        await test.RunAsync();
    }

    // A 'clones' line names an override in any assembly: a library that was built with the analyzers names its members that clone
    // (SCA1008), and a member of the base class can run such an override as well.
    [TestMethod]
    [DataRow("P:LibA.Marked`1.NumberFormat;clones", true)]
    [DataRow("", false)]
    public async Task Member_OfABaseClassGetsAWarningWhereAnOverrideOfAnAnalyzedLibraryClones(string lines, bool reported)
    {
        var library = await Verify.CompileLibraryAsync(Verify.Test.NetReferenceAssemblies, """
            using System.Globalization;
            using Singulink.CodeAnalysis;

            namespace LibA
            {
                public class Marked<[NonCopyable] T> : CultureInfo
                {
                    public T Tag = default!;

                    public Marked() : base("en-US") { }

                    public override NumberFormatInfo NumberFormat
                    {
                        get { _ = Clone(); return base.NumberFormat; }
                        set => base.NumberFormat = value;
                    }
                }
            }
            """, analyzed: true, "LibA");

        var test = new Verify.Test {
            TestCode = """
                using System.Runtime.InteropServices;
                using LibA;

                class C
                {
                    void Uses(Marked<GCHandle> culture)
                    {
                        _ = {|#0:culture.Name|};
                    }
                }
                """,
        };

        if (lines.Length > 0)
            test.TestState.AdditionalFiles.Add((Verify.ListFileName, lines));

        test.TestState.AdditionalReferences.Add(library);

        if (reported)
        {
            test.ExpectedDiagnostics.Add(Verify.CloningUseRunsMemberThatClones(
                0, "CultureInfo.Name", "LibA.Marked<System.Runtime.InteropServices.GCHandle>", "Marked<T>.NumberFormat", Culture, Handle, "LibA.Marked<System.Runtime.InteropServices.GCHandle>"));
        }

        await test.RunAsync();
    }

    // Two libraries that were not built with the analyzers: one with a class that clones itself and a base class above it whose
    // members run members of their own object in every way that a class below can take part in, and one with the classes below.
    private const string RunningLibrary = """
        using System;

        namespace LibD
        {
            public interface IWork
            {
                void Run();

                int Size { get; }
            }

            public interface IOther { void Go(); }

            public class Root : IWork
            {
                public virtual void Run() { }

                public virtual int Size => 0;

                // Calls a virtual member, its own object as an interface of its class, and as an interface that it does not implement.
                public void Plain()
                {
                    Run();
                    ((IWork)this).Run();
                    _ = ((IWork)this).Size;
                    (this as IOther)?.Go();
                }

                public virtual event EventHandler? Custom { add { } remove { OnRemove(); } }

                protected virtual void OnRemove() { }
            }

            // The class that clones itself implements an interface that the class above it does not, with a member that clones.
            public class Doc : Root, ICloneable
            {
                public Doc Copy() => (Doc)MemberwiseClone();

                public object Clone() => MemberwiseClone();
            }

            public class Tagged<T> : Doc { public T Tag = default!; }
        }
        """;

    private const string RunLibrary = """
        using System.Runtime.InteropServices;
        using LibD;

        namespace LibK
        {
            // Its override of a member of Root runs a virtual member of its own, which the class below overrides.
            public class Mid : Tagged<GCHandle>
            {
                public override void Run() => HookA();

                public virtual void HookA() { }
            }

            public class Chained : Mid
            {
                public override void HookA() => _ = Copy();
            }

            // Implements an interface that Root does not implement and tests its object for.
            public class Other : Tagged<GCHandle>, IOther
            {
                public void Go() => _ = Copy();
            }

            // Overrides what the remove accessor of an event of Root runs.
            public class Removing : Tagged<GCHandle>
            {
                protected override void OnRemove() => _ = Copy();
            }

            public sealed class Sealed : Tagged<GCHandle>
            {
                public override void Run() { }
            }

            // Lists the interface of Root again, which makes the member that it inherits from the class between the implementation.
            public class Hiding : Tagged<GCHandle>
            {
                public new void Run() => _ = Copy();
            }

            public class Relisting : Hiding, IWork { }

            // The same for a property, for which the compiler writes only an accessor in the class that lists the interface again.
            public class HidingSize : Tagged<GCHandle>
            {
                public new int Size
                {
                    get
                    {
                        _ = Copy();
                        return 0;
                    }
                }
            }

            public class RelistingSize : HidingSize, IWork { }

            // Puts nothing in the place of a member of the classes above it.
            public class Nothing : Tagged<GCHandle> { }

            // A sealed class that is below no class that clones itself.
            public sealed class Lone : System.Exception { }
        }
        """;

    private const string RunLines = "T:LibD.Doc;clones=complete\nM:LibD.Doc.Copy;clones\nM:LibD.Doc.Clone;clones\n";
    private const string TaggedHandleDoc = "LibD.Tagged<System.Runtime.InteropServices.GCHandle>";

    private static async Task<Verify.Test> CreateRunTestAsync(string code, string lines)
    {
        var docs = await Verify.CompileLibraryAsync(Verify.Test.NetReferenceAssemblies, RunningLibrary, analyzed: false, "LibD");
        var below = await Verify.CompileLibraryAsync(Verify.Test.NetReferenceAssemblies, RunLibrary, analyzed: false, "LibK", docs);

        var test = new Verify.Test { TestCode = code };
        test.TestState.AdditionalFiles.Add((Verify.ListFileName, RunLines + lines));
        test.TestState.AdditionalReferences.Add(docs);
        test.TestState.AdditionalReferences.Add(below);
        return test;
    }

    // The code of a member that can be run can run the members below its own class in turn: Root.Plain runs Mid.Run, which runs a
    // virtual member of Mid that the class below overrides. So the walk goes down from the member's class, class by class.
    [TestMethod]
    public async Task Member_OfABaseClassIsJudgedByWhatTheOverridesBelowItRunInTurn()
    {
        var test = await CreateRunTestAsync(
            """
            using LibK;

            class C
            {
                void Uses(Chained chained)
                {
                    {|#0:chained.Plain()|};
                    {|#1:chained.Run()|};
                    {|#2:chained.HookA()|};
                }
            }
            """,
            "T:LibK.Mid;clones=complete\nT:LibK.Chained;clones=complete\nM:LibK.Chained.HookA;clones");

        test.ExpectedDiagnostics.Add(Verify.CloningUseRunsMemberThatClones(0, "Root.Plain()", "LibK.Chained", "Chained.HookA()", "LibD.Root", Handle, TaggedHandleDoc));
        test.ExpectedDiagnostics.Add(Verify.CloningUseRunsMemberThatClones(1, "Mid.Run()", "LibK.Chained", "Chained.HookA()", "LibK.Mid", Handle, TaggedHandleDoc));
        test.ExpectedDiagnostics.Add(Verify.CloningUseOfCloningMember(2, "Chained.HookA()", "LibK.Chained", Handle, TaggedHandleDoc));
        await test.RunAsync();
    }

    // The code of a class can test its object for an interface that the class does not implement, and call it. So a member that
    // implements a member of any interface of a class below counts as one that the base class can run.
    [TestMethod]
    [DataRow("T:LibK.Other;clones=complete\nM:LibK.Other.Go;clones", true, false)]
    [DataRow("", false, true)]
    [DataRow("T:LibK.Other;clones=complete", false, false)]
    public async Task Member_OfABaseClassIsJudgedByTheMembersOfAnInterfaceThatOnlyAClassBelowImplements(string lines, bool clones, bool unknown)
    {
        var test = await CreateRunTestAsync(
            """
            using LibK;

            class C
            {
                void Uses(Other other)
                {
                    {|#0:other.Plain()|};
                }
            }
            """,
            lines);

        if (clones)
            test.ExpectedDiagnostics.Add(Verify.CloningUseRunsMemberThatClones(0, "Root.Plain()", "LibK.Other", "Other.Go()", "LibD.Root", Handle, TaggedHandleDoc));

        if (unknown)
        {
            test.ExpectedDiagnostics.Add(Verify.CloningUseRunsUnknownMember(
                0, "Root.Plain()", "LibK.Other", "Other.Go()", "LibK.Other", "LibD.Root", "LibD.Doc", Handle, TaggedHandleDoc, "M:LibK.Other.Go", "T:LibK.Other"));
        }

        await test.RunAsync();
    }

    // The accessors of an event are code of its class like any other member: removing a handler runs the remove accessor, which can run
    // an override below it. Only the handing on of the object to a handler is a matter of adding one.
    [TestMethod]
    public async Task Member_OfABaseClassThatIsAnEventIsJudgedWhereAHandlerIsRemovedToo()
    {
        var test = await CreateRunTestAsync(
            """
            using LibK;

            class C
            {
                void Uses(Removing removing)
                {
                    {|#0:removing.Custom|} += null;
                    {|#1:removing.Custom|} -= null;
                }
            }
            """,
            "E:LibD.Root.Custom\nT:LibK.Removing;clones=complete\nM:LibK.Removing.OnRemove;clones");

        test.ExpectedDiagnostics.Add(Verify.CloningUseRunsMemberThatClones(0, "Root.Custom", "LibK.Removing", "Removing.OnRemove()", "LibD.Root", Handle, TaggedHandleDoc));
        test.ExpectedDiagnostics.Add(Verify.CloningUseRunsMemberThatClones(1, "Root.Custom", "LibK.Removing", "Removing.OnRemove()", "LibD.Root", Handle, TaggedHandleDoc));
        await test.RunAsync();
    }

    // The lines of the class that clones itself say what its members and those of the classes above it do with an object of it, as
    // one piece of code. So a member of the class that clones itself is nothing that the walk looks at: its Clone, which implements an
    // interface that the class above does not, leaves the members of the class above with no warning where nothing below replaces one.
    [TestMethod]
    public async Task Member_OfAClassAboveTheCloningClassIsNotJudgedByTheMembersOfTheCloningClass()
    {
        var test = await CreateRunTestAsync(
            """
            using LibK;

            class C
            {
                void Uses(Nothing nothing)
                {
                    nothing.Plain();
                    nothing.Run();
                    _ = nothing.Size;
                    _ = {|#0:nothing.Clone()|};
                }
            }
            """,
            string.Empty);

        test.ExpectedDiagnostics.Add(Verify.CloningUseOfCloningMember(0, "Doc.Clone()", "LibK.Nothing", Handle, TaggedHandleDoc));
        await test.RunAsync();
    }

    // A member of a class above the class that clones itself can run an override below only through what it can call: an override
    // of a virtual member of the class that clones itself is out of its reach where nothing between runs that member.
    [TestMethod]
    public async Task Member_OfAClassAboveTheCloningClassIsJudgedOnlyByWhatItCanReach()
    {
        var library = await Verify.CompileLibraryAsync(Verify.Test.NetReferenceAssemblies, """
            using System.Runtime.InteropServices;

            namespace LibT
            {
                public class Top
                {
                    public virtual void TopHook() { }

                    public void TopPlain() => TopHook();
                }

                public class Doc : Top
                {
                    public Doc Copy() => (Doc)MemberwiseClone();

                    public virtual void DocHook() { }

                    public void DocPlain() => DocHook();
                }

                public class Tagged<T> : Doc { public T Tag = default!; }

                public class Low : Tagged<GCHandle>
                {
                    public override void DocHook() => _ = Copy();
                }
            }
            """, analyzed: false, "LibT");

        var test = new Verify.Test {
            TestCode = """
                using LibT;

                class C
                {
                    void Uses(Low low)
                    {
                        low.TopPlain();
                        {|#0:low.DocPlain()|};
                    }
                }
                """,
        };

        test.TestState.AdditionalFiles.Add((Verify.ListFileName, "T:LibT.Doc;clones=complete\nM:LibT.Doc.Copy;clones\nM:LibT.Low.DocHook;clones"));
        test.TestState.AdditionalReferences.Add(library);
        test.ExpectedDiagnostics.Add(Verify.CloningUseRunsMemberThatClones(0, "Doc.DocPlain()", "LibT.Low", "Low.DocHook()", "LibT.Doc", Handle, "LibT.Tagged<System.Runtime.InteropServices.GCHandle>"));
        await test.RunAsync();
    }

    // The lines of the class that clones itself speak for its members and for those of the classes above it, with one exception: an
    // override there that a 'clones' line names. The lines say that it clones, and nothing of a member above it that can run it, as
    // with a template method whose step the class that clones itself overrides with a clone. Such a member gets the warning until a
    // line of its own says whether it runs the override. A member of the class that clones itself which implements an interface is no
    // such exception: see the test of ICloneable above.
    [TestMethod]
    [DataRow("", true, true)]
    [DataRow("M:LibS.Top.Size", true, false)]
    [DataRow("M:LibS.Top.Snapshot;clones\nM:LibS.Top.Size", false, false)]
    public async Task Member_OfAClassAboveTheCloningClassGetsAWarningWhereTheCloningClassOverridesItsStepWithAClone(string lines, bool snapshotRuns, bool sizeRuns)
    {
        var library = await Verify.CompileLibraryAsync(Verify.Test.NetReferenceAssemblies, """
            namespace LibS
            {
                public abstract class Top
                {
                    protected abstract object CloneCore();

                    public void Snapshot() => _ = CloneCore();

                    public int Size() => 0;
                }

                public class Doc : Top
                {
                    protected override object CloneCore() => MemberwiseClone();

                    public void Touch() { }
                }

                public class Tagged<T> : Doc { public T Tag = default!; }
            }
            """, analyzed: false, "LibS");

        var test = new Verify.Test {
            TestCode = """
                using System.Runtime.InteropServices;
                using LibS;

                class C
                {
                    void Uses(Tagged<GCHandle> tagged)
                    {
                        {|#0:tagged.Snapshot()|};
                        _ = {|#1:tagged.Size()|};
                        tagged.Touch();
                    }
                }
                """,
        };

        const string Tagged = "LibS.Tagged<System.Runtime.InteropServices.GCHandle>";

        test.TestState.AdditionalFiles.Add((Verify.ListFileName, "T:LibS.Doc;clones=complete\nM:LibS.Doc.CloneCore;clones" + (lines.Length > 0 ? "\n" + lines : string.Empty)));
        test.TestState.AdditionalReferences.Add(library);

        test.ExpectedDiagnostics.Add(snapshotRuns
            ? Verify.CloningUseRunsOverrideOfCloningClass(0, "Top.Snapshot()", Tagged, "Doc.CloneCore()", "LibS.Top", Handle, Tagged, "M:LibS.Top.Snapshot")
            : Verify.CloningUseOfCloningMember(0, "Top.Snapshot()", Tagged, Handle, Tagged));

        if (sizeRuns)
            test.ExpectedDiagnostics.Add(Verify.CloningUseRunsOverrideOfCloningClass(1, "Top.Size()", Tagged, "Doc.CloneCore()", "LibS.Top", Handle, Tagged, "M:LibS.Top.Size"));

        await test.RunAsync();
    }

    // The same as the test before for a property: the compiler writes only an accessor into the class that lists the interface again,
    // so the class has no member that implements the property, and the inherited property that fits is what counts.
    [TestMethod]
    public async Task Member_OfABaseClassIsJudgedByTheInheritedPropertyThatAnInterfaceListedAgainRuns()
    {
        var test = await CreateRunTestAsync(
            """
            using LibK;

            class C
            {
                void Uses(RelistingSize relisting)
                {
                    {|#0:relisting.Plain()|};
                }
            }
            """,
            "T:LibK.HidingSize;clones=complete\nT:LibK.RelistingSize;clones=complete\nP:LibK.HidingSize.Size;clones");

        test.ExpectedDiagnostics.Add(Verify.CloningUseRunsMemberThatClones(0, "Root.Plain()", "LibK.RelistingSize", "HidingSize.Size", "LibD.Root", Handle, TaggedHandleDoc));
        await test.RunAsync();
    }

    // A line that says 'clones=complete' for a sealed class that is below no class that clones itself still has no effect.
    [TestMethod]
    public async Task CompleteLine_ForASealedClassBelowNoCloningClassHasNoEffect()
    {
        var test = await CreateRunTestAsync("class C { }", "T:LibK.Lone;clones=complete");

        test.ExpectedDiagnostics.Add(Verify.SafeListClonesHasNoEffect(
            4, 1, 28, "T:LibK.Lone", "the class is sealed and not generic, so no class can derive from it and no type can be bound to it"));

        await test.RunAsync();
    }

    // The line that the warning proposes for the class of the override has its effect for a sealed class too, and is not reported as a
    // line with none.
    [TestMethod]
    [DataRow("", true)]
    [DataRow("T:LibK.Sealed;clones=complete", false)]
    public async Task CompleteLine_ForASealedClassBelowCoversItsOverrides(string lines, bool reported)
    {
        var test = await CreateRunTestAsync(
            """
            using LibK;

            class C
            {
                void Uses(Sealed @sealed)
                {
                    {|#0:@sealed.Plain()|};
                }
            }
            """,
            lines);

        if (reported)
        {
            test.ExpectedDiagnostics.Add(Verify.CloningUseRunsUnknownMember(
                0, "Root.Plain()", "LibK.Sealed", "Sealed.Run()", "LibK.Sealed", "LibD.Root", "LibD.Doc", Handle, TaggedHandleDoc, "M:LibK.Sealed.Run", "T:LibK.Sealed"));
        }

        await test.RunAsync();
    }

    // A class that lists an interface of the base class again makes a member that it inherits from a class between the
    // implementation. In a compiled class the compiler wrote a member that hands the call on to it, whose code cannot be read, so the
    // inherited member that fits counts as well: its 'clones' line gives the warning.
    [TestMethod]
    public async Task Member_OfABaseClassIsJudgedByTheInheritedMemberThatAnInterfaceListedAgainRuns()
    {
        var test = await CreateRunTestAsync(
            """
            using LibK;

            class C
            {
                void Uses(Relisting relisting)
                {
                    {|#0:relisting.Plain()|};
                }
            }
            """,
            "T:LibK.Hiding;clones=complete\nT:LibK.Relisting;clones=complete\nM:LibK.Hiding.Run;clones");

        test.ExpectedDiagnostics.Add(Verify.CloningUseRunsMemberThatClones(0, "Root.Plain()", "LibK.Relisting", "Hiding.Run()", "LibD.Root", Handle, TaggedHandleDoc));
        await test.RunAsync();
    }

    // A generic class of an assembly that was not built with the analyzers, whose binding comes from its own type argument alone, gets
    // SCA1020 for its members and no warning for the clone. Where a base list above it also binds a closed non-copyable type, as
    // 'Both<T> : Tagged<Token>' does, the clone copies that one: the member gets SCA1020 for its own code and the type argument, and
    // SCA1009 for the clone, with a text that names the closed binding. Here the class is of the assembly of the class that clones
    // itself, so the line of that class covers it. A 'clones' line for the member names the closed binding too. (QA9 round 9, N2 and N4)
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Member_OfAClassThatAlsoBindsAClosedTypeAboveItsTypeArgumentGetsTheWarningForTheClone(bool clonesLine)
    {
        var library = await Verify.CompileLibraryAsync(Verify.Test.NetReferenceAssemblies, BothLibrary, analyzed: false, "LibT");
        var test = new Verify.Test {
            TestCode = """
                using LibT;

                class C
                {
                    void Uses()
                    {
                        {|#0:Make.Both<Guard>().Sneak()|};
                        {|#1:Make.Both<int>().Sneak()|};
                        {|#2:Make.Far<Guard>().Op()|};
                    }
                }
                """ + System.Environment.NewLine + Sources.Guard,
        };

        string lines = "T:LibT.Doc;clones\nM:LibT.Doc.Copy;clones\nM:LibT.Make.Both``1;new\nM:LibT.Make.Far``1;new" + (clonesLine ? "\nM:LibT.Both`1.Sneak;clones" : string.Empty);
        test.TestState.AdditionalFiles.Add((Verify.ListFileName, lines));
        test.TestState.AdditionalReferences.Add(library);
        const string Bound = "LibT.Tagged<LibT.Token>";

        if (clonesLine)
        {
            test.ExpectedDiagnostics.Add(Verify.CloningUseOfCloningMember(0, "Both<T>.Sneak()", "LibT.Both<Guard>", "LibT.Token", Bound));
            test.ExpectedDiagnostics.Add(Verify.UnlistedMemberThatClones(0, "Both<T>.Sneak()", "Guard", "T"));
            test.ExpectedDiagnostics.Add(Verify.CloningUseOfCloningMember(1, "Both<T>.Sneak()", "LibT.Both<int>", "LibT.Token", Bound));
        }
        else
        {
            test.ExpectedDiagnostics.Add(Verify.CloningUseOfUnknownMember(0, "Both<T>.Sneak()", "LibT.Both<Guard>", "LibT.Both<Guard>", "LibT.Doc", "LibT.Token", Bound, "M:LibT.Both`1.Sneak", "T:LibT.Doc"));
            test.ExpectedDiagnostics.Add(Verify.UnlistedMember(0, "Both<T>.Sneak()", "Guard", "T", "M:LibT.Both`1.Sneak"));
            test.ExpectedDiagnostics.Add(Verify.CloningUseOfUnknownMember(1, "Both<T>.Sneak()", "LibT.Both<int>", "LibT.Both<int>", "LibT.Doc", "LibT.Token", Bound, "M:LibT.Both`1.Sneak", "T:LibT.Doc"));
        }

        test.ExpectedDiagnostics.Add(Verify.UnlistedMember(2, "Far<T>.Op()", "Guard", "T", "M:LibT.Far`1.Op"));
        await test.RunAsync();
    }

    private const string DocLibrary = """
        using System;
        using Singulink.CodeAnalysis;

        namespace LibC
        {
            public class Doc
            {
                public int Size => 1;

                public Doc WithTitle(string title) => this;

                public Doc Copy() => (Doc)MemberwiseClone();

                public Doc Snapshot() => Copy();

                public virtual string Describe() => "doc";
            }

            public class TaggedDoc<[NonCopyable] T> : Doc
            {
                public T Tag = default!;

                public TaggedDoc<T> Dup() => (TaggedDoc<T>)Copy();

                public override string Describe() => "tagged";
            }

            public static class Docs
            {
                public static TaggedDoc<T> Make<[NonCopyable] T>() => new();
            }
        }
        """;
}
