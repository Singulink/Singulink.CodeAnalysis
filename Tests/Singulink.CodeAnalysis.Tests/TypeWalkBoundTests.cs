using Microsoft.CodeAnalysis.Testing;
using PrefixClassName.MsTest;

namespace Singulink.CodeAnalysis.Tests;

/// <summary>
/// Tests of the bound of the walks over types. A generic that names a deeper construction of itself in its base list, as
/// <c>class C&lt;T&gt; : IEquatable&lt;C&lt;C&lt;T&gt;&gt;&gt;</c> does, leads a walk that follows base types and interfaces into their
/// type arguments to a new type at every step, and two generics can do it through each other. A generic over a tuple or a delegate of its
/// type parameter doubles the parts of its type at every step. Each walk counts the types that it looks at and the parts of each, and
/// stops at its bound with the answer that leads to a report: a severity is strict, and a type carries a non-copyable value. Every test
/// runs with a timeout, so that a walk that does not end fails the test and does not hold up the run.
/// </summary>
/// <remarks>
/// Docs: articles/rules/not-covered.md.
/// </remarks>
[PrefixTestClass]
public class TypeWalkBoundTests
{
    // A plain non-copyable struct, one that is unsafe to copy, a generic that expands itself, two that expand each other, and a generic
    // method that does not mark its type parameter.
    private const string Types = """
        [Singulink.CodeAnalysis.NonCopyable]
        public struct PG { public void Use() { } }

        [Singulink.CodeAnalysis.NonCopyable(UnsafeToCopy = true)]
        public struct UG { public void Use() { } }

        public class C<T> : System.IEquatable<C<C<T>>>
        {
            public bool Equals(C<C<T>> other) => true;
        }

        public interface I<T> { }

        public class A<T> : I<B<A<T>>> { }

        public class B<T> : I<A<B<T>>> { }

        static class U
        {
            public static void Use<T>(T value) { }
        }
        """;

    // The conversion of the generic to an interface that names a deeper construction: the walks of what each side mentions meet a new
    // type at every step. At the bound the conversion counts as one that hides the non-copyable type.
    [TestMethod]
    public Task Conversion_OfAGenericThatExpandsItselfEnds() => WithinTimeoutAsync(() => Verify.AnalyzerAsync(
        $$"""
        class P
        {
            void M(C<PG> c, C<UG> u)
            {
                System.IEquatable<C<C<PG>>> e = {|#0:c|};
                object o = {|#1:u|};
            }
        }

        {{Types}}
        """,
        Verify.OpaqueConversion(0, "C<PG>", "System.IEquatable<C<C<PG>>>"),
        Verify.OpaqueConversion(1, "C<UG>", "object")));

    [TestMethod]
    public Task Conversion_OfAGenericThatExpandsItselfEndsUnderUpdatedRules() => WithinTimeoutAsync(() => Verify.UpdatedRulesAsync(
        $$"""
        class P
        {
            void M(C<PG> c, C<UG> u)
            {
                System.IEquatable<C<C<PG>>> e = {|#0:c|};
                object o = {|#1:u|};
            }
        }

        {{Types}}
        """,
        Verify.OpaqueConversion(0, "C<PG>", "System.IEquatable<C<C<PG>>>").AsErrorAtBound(),
        Verify.OpaqueConversion(1, "C<UG>", "object").AsError()));

    // Two generics that expand each other.
    [TestMethod]
    public Task Conversion_OfGenericsThatExpandEachOtherEnds() => WithinTimeoutAsync(() => Verify.AnalyzerAsync(
        $$"""
        class P
        {
            void M(A<PG> a, B<UG> b)
            {
                I<B<A<PG>>> i = {|#0:a|};
                object o = {|#1:b|};
            }
        }

        {{Types}}
        """,
        Verify.OpaqueConversion(0, "A<PG>", "I<B<A<PG>>>"),
        Verify.OpaqueConversion(1, "B<UG>", "object")));

    [TestMethod]
    public Task Conversion_OfGenericsThatExpandEachOtherEndsUnderUpdatedRules() => WithinTimeoutAsync(() => Verify.UpdatedRulesAsync(
        $$"""
        class P
        {
            void M(A<PG> a, B<UG> b)
            {
                I<B<A<PG>>> i = {|#0:a|};
                object o = {|#1:b|};
            }
        }

        {{Types}}
        """,
        Verify.OpaqueConversion(0, "A<PG>", "I<B<A<PG>>>").AsErrorAtBound(),
        Verify.OpaqueConversion(1, "B<UG>", "object").AsError()));

    // The severity of a report about the generic follows its base list to find what it holds. Under the current rules the report is the
    // warning; under the updated rules the walk reaches its bound and the report is strict, though the generic holds a plain type only.
    [TestMethod]
    public Task Severity_OfAReportAboutAGenericThatExpandsItselfEnds() => WithinTimeoutAsync(() => Verify.AnalyzerAsync(
        $$"""
        class P
        {
            C<PG> _c = null!;
            A<PG> _a = null!;
            B<PG> _b = null!;

            void M()
            {
                {|#0:U.Use(_c)|};
                {|#1:U.Use(_a)|};
                {|#2:U.Use(_b)|};
            }
        }

        {{Types}}
        """,
        Verify.OpaqueTypeArgument(0, "U.Use<T>(T)", "C<PG>", "T", "M:U.Use``1(``0)"),
        Verify.OpaqueTypeArgument(1, "U.Use<T>(T)", "A<PG>", "T", "M:U.Use``1(``0)"),
        Verify.OpaqueTypeArgument(2, "U.Use<T>(T)", "B<PG>", "T", "M:U.Use``1(``0)")));

    [TestMethod]
    public Task Severity_OfAReportAboutAGenericThatExpandsItselfEndsStrictUnderUpdatedRules() => WithinTimeoutAsync(() => Verify.UpdatedRulesAsync(
        $$"""
        class P
        {
            C<PG> _c = null!;
            A<PG> _a = null!;
            B<PG> _b = null!;

            void M()
            {
                {|#0:U.Use(_c)|};
                {|#1:U.Use(_a)|};
                {|#2:U.Use(_b)|};
            }
        }

        {{Types}}
        """,
        Verify.OpaqueTypeArgument(0, "U.Use<T>(T)", "C<PG>", "T", "M:U.Use``1(``0)").AsErrorAtBound(),
        Verify.OpaqueTypeArgument(1, "U.Use<T>(T)", "A<PG>", "T", "M:U.Use``1(``0)").AsErrorAtBound(),
        Verify.OpaqueTypeArgument(2, "U.Use<T>(T)", "B<PG>", "T", "M:U.Use``1(``0)").AsErrorAtBound()));

    // A local of the generic that a member of it hands a container out of: whether what comes out is an element of the local follows the
    // base list of the generic, as does whether the local holds containers.
    [TestMethod]
    public Task ContainerLocal_OfAGenericThatExpandsItselfEnds() => WithinTimeoutAsync(() => Verify.AnalyzerAsync(
        $$"""
        using System.Collections.Generic;

        class P
        {
            List<PG> M()
            {
                var e = new E<PG>();
                var items = {|#0:e.Items|}();
                return items;
            }
        }

        public class E<[Singulink.CodeAnalysis.NonCopyable] T> : System.IEquatable<E<E<T>>>
        {
            public bool Equals(E<E<T>> other) => true;

            public List<T> Items() => new();
        }

        {{Types}}
        """));

    // A delegate that takes a deeper construction of itself: what its method is given is a new delegate type at every step. The check of
    // an assembly that was not built with the analyzer follows what the method of a delegate of checked code is given, and at the bound
    // takes the delegate to let a value out. The message names the first member by its display.
    [TestMethod]
    public Task UncheckedAssembly_ThatReferencesADelegateThatExpandsItselfEnds() => WithinTimeoutAsync(async () => {
        const string Jobs = """
            public delegate void Chain<T>(Chain<Chain<T>> next);

            public class Job
            {
                public void Each(Chain<System.Threading.SpinLock> callback) { }
            }

            public class Anchor { }
            """;

        var jobs = await Verify.CompileLibraryAsync(Verify.Test.NetReferenceAssemblies, Jobs, analyzed: true, "Jobs");
        var plugin = await Verify.CompileLibraryAsync(Verify.Test.NetReferenceAssemblies, "public static class Plugin { public static Anchor? Anything; }", analyzed: false, "Plugin", jobs);

        var test = new Verify.Test { TestCode = "class C { }" };
        test.TestState.AdditionalReferences.Add(jobs);
        test.TestState.AdditionalReferences.Add(plugin);
        test.ExpectedDiagnostics.Add(Verify.UncheckedAssemblyOfExposedValue("Plugin", "Jobs", "Chain<T>"));
        await test.RunAsync();
    });

    // The check of a member of a class that clones itself follows the types of its signature for one that can hand the object on, through
    // the parameters of a delegate as well, and at the bound takes the member to hand it on.
    [TestMethod]
    public Task CloningMember_WithADelegateThatExpandsItselfEnds() => WithinTimeoutAsync(async () => {
        const string Library = """
            using Singulink.CodeAnalysis;

            namespace LibE
            {
                public delegate void Chain<T>(Chain<Chain<T>> next);

                public class Doc
                {
                    public int Size => 1;

                    public Doc Copy() => (Doc)MemberwiseClone();

                    public void Link(Chain<int> chain) { }
                }

                public class Mid<[NonCopyable] T> : Doc
                {
                    public T Tag = default!;
                }

                public static class Docs
                {
                    public static Mid<T> Make<[NonCopyable] T>() => new();
                }
            }
            """;

        var library = await Verify.CompileLibraryAsync(Verify.Test.NetReferenceAssemblies, Library, analyzed: true, "LibE");

        var test = new Verify.Test {
            TestCode = """
                using LibE;

                class C
                {
                    void Uses()
                    {
                        var doc = Docs.Make<Guard>();
                        {|#0:doc.Link(null!)|};
                        _ = doc.Size;
                    }
                }
                """ + Environment.NewLine + Sources.Guard,
        };

        test.TestState.AdditionalFiles.Add((Verify.ListFileName, "T:LibE.Doc;clones=complete\nM:LibE.Doc.Copy;clones"));
        test.TestState.AdditionalReferences.Add(library);
        test.ExpectedDiagnostics.Add(Verify.CloningUseHandsBack(
            0, "Doc.Link(Chain<int>)", "LibE.Mid<Guard>", "LibE.Chain<int>", "LibE.Doc", "Guard", "LibE.Mid<Guard>", "M:LibE.Doc.Link(LibE.Chain{System.Int32})"));

        await test.RunAsync();
    });

    // A type that the compiler infers through a chain of two thousand calls is nested two thousand levels deep. The walks over its parts
    // reach their bound, and the member gets SCA1048 for a type with more parts than the analyzer looks at. Under the updated rules the
    // walk that looks for a value that is unsafe to copy in the member reaches its bound too, so the report is strict. Formatting such a
    // type for a message by the compiler's recursion would end the process.
    [TestMethod]
    public Task DeepType_OfAChainOfCallsGetsTheReportThatTheMemberWasNotChecked() => WithinTimeoutAsync(() => Verify.AnalyzerAsync(
        DeepChain(2000), Verify.TypeTooLarge(0, "Deep.M()")));

    [TestMethod]
    public Task DeepType_OfAChainOfCallsGetsTheReportThatTheMemberWasNotCheckedUnderUpdatedRules() => WithinTimeoutAsync(() => Verify.UpdatedRulesAsync(
        DeepChain(2000), Verify.TypeTooLarge(0, "Deep.M()").AsErrorAtBound()));

    // A type nested more deeply than a message shows in full is shown with its outer levels, and the member is checked: the chain is
    // shorter than the bound of a walk.
    [TestMethod]
    public Task DeepType_IsShownInShortInAMessage() => WithinTimeoutAsync(() => Verify.AnalyzerAsync(
        $$"""
        using Singulink.CodeAnalysis;

        static class Deep
        {
            static {{Nested("Box", 40, "PG")}} s_deep;

            static void M()
            {
                var copy = {|#0:s_deep|};
            }
        }

        [NonCopyable]
        public struct Box<[NonCopyable] T> { public T Value; }

        {{Types}}
        """,
        Verify.CopyByAssignment(0, "Box<Box<Box<Box<Box<...>>>>>")));

    // A type with more parts than a walk looks at, written in a declaration: the field gets SCA1048 once, where the type is, and the
    // struct around it is checked. A parameter of a primary constructor that the struct keeps in a field gets it in the same way, and
    // the member whose code uses it gets its own.
    [TestMethod]
    public Task DeepType_InADeclarationGetsTheReportThatItWasNotCheckedOnce() => WithinTimeoutAsync(() => Verify.AnalyzerAsync(
        $$"""
        using Singulink.CodeAnalysis;

        [NonCopyable(UnsafeToCopy = true)]
        public struct Holder
        {
            public {{Nested("Box", 600, "UG")}} {|#0:Deep|};

            public async void {|#1:Later|}() { }
        }

        [NonCopyable(UnsafeToCopy = true)]
        public struct Kept({{Nested("Box", 600, "UG")}} {|#2:deep|})
        {
            public object {|#3:Get|}() => {|#4:deep|};
        }

        [NonCopyable]
        public struct Box<[NonCopyable] T> { public T Value; }

        {{Types}}
        """,
        Verify.TypeTooLarge(0, "Deep"),
        Verify.AsyncOrIteratorMember(1, "Holder.Later()", "Holder", "async"),
        Verify.TypeTooLarge(2, "deep"),
        Verify.TypeTooLarge(3, "Kept.Get()"),
        Verify.Boxing(4, "Box<Box<Box<Box<Box<...>>>>>", "object")));

    // Generics whose type doubles at each step of their base list: by a tuple, by a delegate, by two type parameters, through a base class
    // and through a nested type. Each step has twice the parts of the one before, so comparing two of them that were made apart costs
    // 2^n steps however few steps a walk takes. The walks count the parts of each type and stop before they take up one too large.
    private const string DoublingTypes = """
        public class X<T> : System.IEquatable<X<(T, T)>>
        {
            public bool Equals(X<(T, T)>? other) => true;
        }

        public class F<T> : System.IEquatable<F<System.Func<T, T>>>
        {
            public bool Equals(F<System.Func<T, T>>? other) => true;
        }

        public class W<T, V> : System.IEquatable<W<V, W<T, V>>>
        {
            public bool Equals(W<V, W<T, V>>? other) => true;
        }

        public class Base<T> { }

        public class D<T> : Base<D<(T, T)>> { }

        public class N<T>
        {
            public class Inner : N<(T, T)> { }

            public Inner? Kid;
        }
        """;

    [TestMethod]
    public Task Conversion_OfAGenericThatDoublesByATupleToTheInterfaceOfItsListEnds() => WithinTimeoutAsync(() => Verify.AnalyzerAsync(
        $$"""
        using System.Collections.Generic;

        class P
        {
            void M()
            {
                IEnumerable<X<PG>> e = {|#0:new List<X<PG>>()|};
                object o = {|#1:new List<X<PG>>()|};
            }
        }

        {{DoublingTypes}}

        {{Types}}
        """,
        Verify.OpaqueConversion(0, "System.Collections.Generic.List<X<PG>>", "System.Collections.Generic.IEnumerable<X<PG>>"),
        Verify.OpaqueConversion(1, "System.Collections.Generic.List<X<PG>>", "object")));

    [TestMethod]
    public Task Conversion_OfAGenericThatDoublesByATupleToTheInterfaceOfItsListEndsUnderUpdatedRules() => WithinTimeoutAsync(() => Verify.UpdatedRulesAsync(
        $$"""
        using System.Collections.Generic;

        class P
        {
            void M()
            {
                IEnumerable<X<PG>> e = {|#0:new List<X<PG>>()|};
                object o = {|#1:new List<X<PG>>()|};
            }
        }

        {{DoublingTypes}}

        {{Types}}
        """,
        Verify.OpaqueConversion(0, "System.Collections.Generic.List<X<PG>>", "System.Collections.Generic.IEnumerable<X<PG>>").AsErrorAtBound(),
        Verify.OpaqueConversion(1, "System.Collections.Generic.List<X<PG>>", "object").AsErrorAtBound()));

    [TestMethod]
    public Task Use_OfAGenericThatDoublesByADelegateEnds() => WithinTimeoutAsync(() => Verify.AnalyzerAsync(
        $$"""
        class P
        {
            F<PG> _f = null!;

            void M()
            {
                {|#0:U.Use(_f)|};
            }
        }

        {{DoublingTypes}}

        {{Types}}
        """,
        Verify.OpaqueTypeArgument(0, "U.Use<T>(T)", "F<PG>", "T", "M:U.Use``1(``0)")));

    [TestMethod]
    public Task Use_OfAGenericThatDoublesByADelegateEndsUnderUpdatedRules() => WithinTimeoutAsync(() => Verify.UpdatedRulesAsync(
        $$"""
        class P
        {
            F<PG> _f = null!;

            void M()
            {
                {|#0:U.Use(_f)|};
            }
        }

        {{DoublingTypes}}

        {{Types}}
        """,
        Verify.OpaqueTypeArgument(0, "U.Use<T>(T)", "F<PG>", "T", "M:U.Use``1(``0)").AsErrorAtBound()));

    [TestMethod]
    public Task Conversion_OfAGenericThatDoublesByTwoTypeParametersEnds() => WithinTimeoutAsync(() => Verify.AnalyzerAsync(
        $$"""
        using System.Collections.Generic;

        class P
        {
            W<PG, UG> _w = null!;

            void M(List<W<PG, PG>> l)
            {
                System.IEquatable<W<UG, W<PG, UG>>> e = {|#0:_w|};
                System.Collections.IEnumerable u = {|#1:l|};
                IEnumerable<object> o = {|#2:l|};
            }
        }

        {{DoublingTypes}}

        {{Types}}
        """,
        Verify.OpaqueConversion(0, "W<PG, UG>", "System.IEquatable<W<UG, W<PG, UG>>>"),
        Verify.UntypedCollectionConversion(1, "System.Collections.Generic.List<W<PG, PG>>", "System.Collections.IEnumerable"),
        Verify.OpaqueConversion(2, "System.Collections.Generic.List<W<PG, PG>>", "System.Collections.Generic.IEnumerable<object>")));

    [TestMethod]
    public Task Conversion_OfAGenericThatDoublesByTwoTypeParametersEndsUnderUpdatedRules() => WithinTimeoutAsync(() => Verify.UpdatedRulesAsync(
        $$"""
        using System.Collections.Generic;

        class P
        {
            W<PG, UG> _w = null!;

            void M(List<W<PG, PG>> l)
            {
                System.IEquatable<W<UG, W<PG, UG>>> e = {|#0:_w|};
                System.Collections.IEnumerable u = {|#1:l|};
                IEnumerable<object> o = {|#2:l|};
            }
        }

        {{DoublingTypes}}

        {{Types}}
        """,
        Verify.OpaqueConversion(0, "W<PG, UG>", "System.IEquatable<W<UG, W<PG, UG>>>").AsError(),
        Verify.UntypedCollectionConversion(1, "System.Collections.Generic.List<W<PG, PG>>", "System.Collections.IEnumerable").AsErrorAtBound(),
        Verify.OpaqueConversion(2, "System.Collections.Generic.List<W<PG, PG>>", "System.Collections.Generic.IEnumerable<object>").AsErrorAtBound()));

    // Growth through a base class and through a nested type that derives from its own generic, with every kind of use.
    [TestMethod]
    public Task Uses_OfGenericsThatDoubleThroughABaseClassOrANestedTypeEnd() => WithinTimeoutAsync(() => Verify.AnalyzerAsync(
        $$"""
        using System.Collections.Generic;

        class P
        {
            D<PG> _d = null!;
            N<PG> _n = null!;
            List<D<PG>> _l = new();

            void M()
            {
                object a = {|#0:_d|};
                object b = {|#1:_n|};
                {|#2:U.Use(_d)|};
                {|#3:U.Use(_n)|};
                System.Collections.IEnumerable c = {|#4:_l|};
                IEnumerable<D<PG>> e = {|#5:_l|};

                foreach (var d in _l)
                {
                }
            }
        }

        {{DoublingTypes}}

        {{Types}}
        """,
        Verify.OpaqueConversion(0, "D<PG>", "object"),
        Verify.OpaqueConversion(1, "N<PG>", "object"),
        Verify.OpaqueTypeArgument(2, "U.Use<T>(T)", "D<PG>", "T", "M:U.Use``1(``0)"),
        Verify.OpaqueTypeArgument(3, "U.Use<T>(T)", "N<PG>", "T", "M:U.Use``1(``0)"),
        Verify.UntypedCollectionConversion(4, "System.Collections.Generic.List<D<PG>>", "System.Collections.IEnumerable"),
        Verify.OpaqueConversion(5, "System.Collections.Generic.List<D<PG>>", "System.Collections.Generic.IEnumerable<D<PG>>")));

    // A generic of checked code with a marked type parameter that doubles itself in its base list: it is erasable through its marks, so
    // a conversion of it hides nothing, and the walks that work out whether it is end. A member that is not on the symbol list is still
    // reported, and under the updated rules the walk of what it holds reaches its bound, so the report is strict.
    [TestMethod]
    public Task Uses_OfAMarkedGenericThatDoublesItselfEnd() => WithinTimeoutAsync(() => Verify.AnalyzerAsync(
        $$"""
        class P
        {
            Marked<PG> _m = null!;

            void M()
            {
                object o = _m;
                System.IEquatable<Marked<(PG, PG)>> e = _m;
                {|#0:U.Use(_m)|};
            }
        }

        public class Marked<[Singulink.CodeAnalysis.NonCopyable] T> : System.IEquatable<Marked<(T, T)>>
        {
            public bool Equals(Marked<(T, T)>? other) => true;
        }

        {{Types}}
        """,
        Verify.OpaqueTypeArgument(0, "U.Use<T>(T)", "Marked<PG>", "T", "M:U.Use``1(``0)")));

    [TestMethod]
    public Task Uses_OfAMarkedGenericThatDoublesItselfEndUnderUpdatedRules() => WithinTimeoutAsync(() => Verify.UpdatedRulesAsync(
        $$"""
        class P
        {
            Marked<PG> _m = null!;

            void M()
            {
                object o = _m;
                System.IEquatable<Marked<(PG, PG)>> e = _m;
                {|#0:U.Use(_m)|};
            }
        }

        public class Marked<[Singulink.CodeAnalysis.NonCopyable] T> : System.IEquatable<Marked<(T, T)>>
        {
            public bool Equals(Marked<(T, T)>? other) => true;
        }

        {{Types}}
        """,
        Verify.OpaqueTypeArgument(0, "U.Use<T>(T)", "Marked<PG>", "T", "M:U.Use``1(``0)").AsErrorAtBound()));

    // A delegate that takes a construction of itself that doubles: the check of an unchecked assembly follows what its method is given.
    [TestMethod]
    public Task UncheckedAssembly_ThatReferencesADelegateThatDoublesEnds() => WithinTimeoutAsync(async () => {
        const string Jobs = """
            public delegate void Chain<T>(Chain<(T, T)> next);

            public class Job
            {
                public void Each(Chain<System.Threading.SpinLock> callback) { }
            }

            public class Anchor { }
            """;

        var jobs = await Verify.CompileLibraryAsync(Verify.Test.NetReferenceAssemblies, Jobs, analyzed: true, "Jobs");
        var plugin = await Verify.CompileLibraryAsync(Verify.Test.NetReferenceAssemblies, "public static class Plugin { public static Anchor? Anything; }", analyzed: false, "Plugin", jobs);

        var test = new Verify.Test { TestCode = "class C { }" };
        test.TestState.AdditionalReferences.Add(jobs);
        test.TestState.AdditionalReferences.Add(plugin);
        test.ExpectedDiagnostics.Add(Verify.UncheckedAssemblyOfExposedValue("Plugin", "Jobs", "Chain<T>"));
        await test.RunAsync();
    });

    // The check of a member of a class that clones itself follows the types of its signature both ways at each type argument of a class,
    // and looks at each of them once each way: a dictionary of dictionaries nine levels deep is followed to its end, and lets nothing out.
    [TestMethod]
    public Task CloningMember_WithADictionaryOfDictionariesNineLevelsDeepHandsNothingBack() => WithinTimeoutAsync(async () => {
        string deep = string.Concat(Enumerable.Repeat("System.Collections.Generic.Dictionary<string, ", 9)) + "int" + new string('>', 9);

        string library = $$"""
            using Singulink.CodeAnalysis;

            namespace LibE
            {
                public class Doc
                {
                    public int Size => 1;

                    public Doc Copy() => (Doc)MemberwiseClone();

                    public void Link({{deep}} map) { }
                }

                public class Mid<[NonCopyable] T> : Doc
                {
                    public T Tag = default!;
                }

                public static class Docs
                {
                    public static Mid<T> Make<[NonCopyable] T>() => new();
                }
            }
            """;

        var compiled = await Verify.CompileLibraryAsync(Verify.Test.NetReferenceAssemblies, library, analyzed: true, "LibE");

        var test = new Verify.Test {
            TestCode = """
                using LibE;

                class C
                {
                    void Uses()
                    {
                        var doc = Docs.Make<Guard>();
                        doc.Link(null!);
                        _ = doc.Size;
                    }
                }
                """ + Environment.NewLine + Sources.Guard,
        };

        test.TestState.AdditionalFiles.Add((Verify.ListFileName, "T:LibE.Doc;clones=complete\nM:LibE.Doc.Copy;clones"));
        test.TestState.AdditionalReferences.Add(compiled);
        await test.RunAsync();
    });

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    // A method whose locals are each made by a call of a generic method with the one before it, so that the type of the last is nested
    // as deeply as the chain is long.
    private static string DeepChain(int length)
    {
        var body = new System.Text.StringBuilder();
        body.AppendLine("        var a0 = new System.Collections.Generic.List<PG>();");

        for (int i = 1; i <= length; i++)
            body.AppendLine($"        var a{i} = W(a{i - 1});");

        return $$"""
            using Singulink.CodeAnalysis;

            public class CBox<T> { }

            static class Deep
            {
                static CBox<T> W<[NonCopyable] T>(T value) => null!;

                static void {|#0:M|}()
                {
            {{body}}
                }
            }

            {{Types}}
            """;
    }

    // A report that is strict under the updated rules only because a walk reached its bound says so: a list of lists twenty levels deep
    // over a plain guard has more types along its interfaces than a walk looks at, and nineteen levels do not. A type that holds a
    // value that is unsafe to copy is strict by that, and its message adds nothing. Under the current rules nothing changes.
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task Severity_ThatABoundDecidesSaysSo(bool updatedRules)
    {
        string source = $$"""
            using System.Collections;
            using System.Collections.Generic;

            class P
            {
                {{Nested("List", 20, "PG")}} _deep = null!;
                {{Nested("List", 19, "PG")}} _shallower = null!;
                {{Nested("List", 20, "UG")}} _unsafe = null!;

                void M()
                {
                    IEnumerable deep = {|#0:_deep|};
                    IEnumerable shallower = {|#1:_shallower|};
                    IEnumerable unsafeList = {|#2:_unsafe|};
                }
            }

            {{Types}}
            """;

        DiagnosticResult[] expected = [
            Verify.UntypedCollectionConversion(0, ListOf(20, "PG"), "System.Collections.IEnumerable") is var deep && updatedRules ? deep.AsErrorAtBound() : deep,
            Verify.UntypedCollectionConversion(1, ListOf(19, "PG"), "System.Collections.IEnumerable"),
            Verify.UntypedCollectionConversion(2, ListOf(20, "UG"), "System.Collections.IEnumerable").AsError(updatedRules),
        ];

        return updatedRules ? Verify.UpdatedRulesAsync(source, expected) : Verify.AnalyzerAsync(source, expected);

        // A type nested more than the levels that a message shows in full is shown with its outer levels.
        static string ListOf(int depth, string innermost) => depth > 32
            ? throw new ArgumentOutOfRangeException(nameof(depth))
            : Nested("System.Collections.Generic.List", depth, innermost);
    }

    // A tuple of more elements than a walk looks at is a type with more parts too, though it is not nested: a member that uses it gets
    // SCA1048 with the text of a type that is too large, whose advice fits a wide type as well as a deep one, and a copy of it is strict
    // because of the bound, which its message says.
    [TestMethod]
    public Task WideType_GetsTheTextOfATypeWithMorePartsAndAStrictCopySaysWhy()
    {
        string wide = "(" + string.Concat(Enumerable.Repeat("int, ", 520)) + "PG)";

        return WithinTimeoutAsync(() => Verify.UpdatedRulesAsync(
            $$"""
            class P
            {
                static {{wide}} s_wide;

                static void Take<[Singulink.CodeAnalysis.NonCopyable] T>(in T value) { }

                static void {|#0:Use|}() => Take(in s_wide);

                static void Copy()
                {
                    var copy = {|#1:s_wide|};
                }
            }

            {{Types}}
            """,
            Verify.TypeTooLarge(0, "P.Use()").AsErrorAtBound(),
            Verify.CopyByAssignment(1, "(" + string.Concat(Enumerable.Repeat("int, ", 31)) + "...)").AsErrorAtBound()));
    }

    private static string Nested(string generic, int depth, string innermost) =>
        string.Concat(Enumerable.Repeat(generic + "<", depth)) + innermost + new string('>', depth);

    private static async Task WithinTimeoutAsync(Func<Task> test)
    {
        var run = Task.Run(test);

        if (await Task.WhenAny(run, Task.Delay(Timeout)) != run)
            Assert.Fail($"The analyzer did not finish within {Timeout.TotalSeconds} seconds.");

        await run;
    }
}
