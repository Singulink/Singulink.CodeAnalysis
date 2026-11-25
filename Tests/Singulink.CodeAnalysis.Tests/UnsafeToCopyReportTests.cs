using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;
using PrefixClassName.MsTest;
using Singulink.CodeAnalysis.Analyzers;

namespace Singulink.CodeAnalysis.Tests;

/// <summary>
/// Tests of the severity of a report under the updated memory safety rules where working it out is not simple: a report that involves
/// a self-referencing generic type, a report that involves more than one non-copyable type, and a report whose value can be of any type.
/// </summary>
/// <remarks>
/// Docs: articles/rules/updated-memory-safety-rules.md.
/// </remarks>
[PrefixTestClass]
public class UnsafeToCopyReportTests
{
    // A plain non-copyable struct and one that is unsafe to copy.
    private const string Types = """
        [Singulink.CodeAnalysis.NonCopyable]
        public struct PG { public void Use() { } }

        [Singulink.CodeAnalysis.NonCopyable(UnsafeToCopy = true)]
        public struct UG { public void Use() { } }

        public class Box<T> : System.IEquatable<Box<T>>
        {
            public bool Equals(Box<T> other) => false;
        }
        """;

    // A generic type that mentions itself through an interface, as ImmutableArray<T> and Box<T> do, is judged by its type arguments as any
    // other container. Working out the severity of a report about it does not stop the check of the member, so the copy after it is
    // still reported, and nothing is reported as nested too deeply: a '#pragma' for SCA1048 hides nothing.
    [TestMethod]
    public Task SelfReferencingType_LeavesTheRestOfTheMemberChecked() => Verify.UpdatedRulesAsync(
        $$"""
        using System.Collections.Immutable;

        class C
        {
            UG _u;

        #pragma warning disable SCA1048
            void F(ImmutableArray<PG> plain, ImmutableArray<UG> unsafeToCopy, Box<PG> box)
            {
                object a = {|#0:plain|};
                object b = {|#1:unsafeToCopy|};
                object c = {|#2:box|};
                var copy = {|#3:_u|};
                copy.Use();
                _u.Use();
            }
        #pragma warning restore SCA1048
        }

        {{Types}}
        """,
        Verify.OpaqueConversion(0, "System.Collections.Immutable.ImmutableArray<PG>", "object"),
        Verify.OpaqueConversion(1, "System.Collections.Immutable.ImmutableArray<UG>", "object").AsError(),
        Verify.OpaqueConversion(2, "Box<PG>", "object"),
        Verify.CopyByAssignment(3, "UG").AsError());

    // The check of the marks of an override goes on past a parameter of a self-referencing type: the error for the next one stays.
    [TestMethod]
    public Task SelfReferencingType_LeavesTheOtherReportsOfADeclaration() => Verify.UpdatedRulesAsync(
        $$"""
        using System.Collections.Generic;
        using Singulink.CodeAnalysis;

        abstract class Base
        {
            public abstract void M(Box<PG> b);
            public abstract void M2(List<UG> b);
        }

        class Derived : Base
        {
            public override void M([Kept] Box<PG> {|#0:b|}) { }
            public override void M2([Kept] List<UG> {|#1:b|}) { }
        }

        {{Types}}
        """,
        Verify.KeptNotOnBaseMember(0, "b", "Derived.M(Box<PG>)", "Base.M(Box<PG>)"),
        Verify.KeptNotOnBaseMember(1, "b", "Derived.M2(List<UG>)", "Base.M2(List<UG>)").AsError());

    // Correct code with a self-referencing type gets no report: a generic math operator of a class over a type parameter that says
    // UnsafeToCopy = false. A field initializer gets the reports of its own use, as plain warnings for a plain type.
    [TestMethod]
    public Task SelfReferencingType_InCorrectCodeGetsNoReport() => Verify.UpdatedRulesAsync(
        $$"""
        using System.Numerics;
        using Singulink.CodeAnalysis;

        class Sum<[NonCopyable(UnsafeToCopy = false)] X> : IAdditionOperators<Sum<X>, Sum<X>, Sum<X>>
        {
            public static Sum<X> operator +(Sum<X> a, Sum<X> b) => new();
        }

        class Fields
        {
            public object O = {|#0:new Box<PG>()|};
        }

        {{Types}}
        """,
        Verify.UnlistedMember(0, "Box<T>.Box()", "PG", "T", "M:Box`1.#ctor"),
        Verify.OpaqueConversion(0, "Box<PG>", "object"));

    // Generics that mention each other, and a type parameter constrained to a type over itself, are judged by their type arguments.
    [TestMethod]
    public Task MutuallyRecursiveAndSelfConstrainedTypes_AreJudgedByTheirTypeArguments() => Verify.UpdatedRulesAsync(
        $$"""
        using System;

        class Ping<T> : IEquatable<Pong<T>> { public bool Equals(Pong<T> other) => false; }

        class Pong<T> : IEquatable<Ping<T>> { public bool Equals(Ping<T> other) => false; }

        abstract class Shape<TSelf> : IEquatable<TSelf> where TSelf : Shape<TSelf> { public bool Equals(TSelf other) => false; }

        class Circle<T> : Shape<Circle<T>> { }

        class C
        {
            void M(Ping<PG> a, Ping<UG> b, Circle<PG> c, Circle<UG> d)
            {
                object o1 = {|#0:a|};
                object o2 = {|#1:b|};
                object o3 = {|#2:c|};
                object o4 = {|#3:d|};
            }
        }

        {{Types}}
        """,
        Verify.OpaqueConversion(0, "Ping<PG>", "object"),
        Verify.OpaqueConversion(1, "Ping<UG>", "object").AsError(),
        Verify.OpaqueConversion(2, "Circle<PG>", "object"),
        Verify.OpaqueConversion(3, "Circle<UG>", "object").AsError());

    // A member that is not vouched for two type parameters names the one bound to a type that is unsafe to copy, whichever comes first,
    // and the report is strict: a value and a container alike.
    [TestMethod]
    public Task UnlistedMember_NamesTheTypeThatIsUnsafeToCopy() => Verify.UpdatedRulesAsync(
        $$"""
        using System.Collections.Generic;

        class Pair<T1, T2>
        {
            public T1 First;
            public T2 Second;
            public T2 Get() => Second;
        }

        class C
        {
            Pair<PG, UG> _plainFirst = null!;
            Pair<UG, PG> _unsafeFirst = null!;
            Pair<PG, List<UG>> _valueAndContainer = null!;
            Pair<List<PG>, List<UG>> _containers = null!;
            Pair<PG, PG> _plain = null!;

            void M()
            {
                var a = {|#0:_plainFirst.Get()|};
                var b = {|#1:_unsafeFirst.Get()|};
                var c = {|#2:_valueAndContainer.Get()|};
                var d = {|#3:_containers.Get()|};
                var e = {|#4:_plain.Get()|};
            }
        }

        {{Types}}
        """,
        Verify.UnlistedMember(0, "Pair<T1, T2>.Get()", "UG", "T2", "M:Pair`2.Get").AsError(),
        Verify.UnlistedMember(1, "Pair<T1, T2>.Get()", "UG", "T1", "M:Pair`2.Get").AsError(),
        Verify.OpaqueTypeArgument(2, "Pair<T1, T2>.Get()", "System.Collections.Generic.List<UG>", "T2", "M:Pair`2.Get").AsError(),
        Verify.OpaqueTypeArgument(3, "Pair<T1, T2>.Get()", "System.Collections.Generic.List<UG>", "T2", "M:Pair`2.Get").AsError(),
        Verify.UnlistedMember(4, "Pair<T1, T2>.Get()", "PG", "T1", "M:Pair`2.Get"));

    // A member that requires a copyable type names a type parameter bound to a type that is unsafe to copy where there is one.
    [TestMethod]
    public Task RequiresCopyable_NamesTheTypeThatIsUnsafeToCopy() => Verify.UpdatedRulesAsync(
        $$"""
        using Singulink.CodeAnalysis;

        class MPair<[NonCopyable] T1, [NonCopyable] T2>
        {
            [RequiresCopyable]
            public T2 Get() => default!;
        }

        class C
        {
            MPair<PG, UG> _mixed = null!;
            MPair<PG, PG> _plain = null!;

            void M()
            {
                var a = {|#0:_mixed.Get()|};
                var b = {|#1:_plain.Get()|};
            }
        }

        {{Types}}
        """,
        Verify.RequiresCopyableMember(0, "MPair<T1, T2>.Get()", "UG", "T2").AsError(),
        Verify.RequiresCopyableMember(1, "MPair<T1, T2>.Get()", "PG", "T1"));

    // A member of a dictionary that the symbol list names for its values only breaks two rules with a plain key and a value that is
    // unsafe to copy: it is not vouched for the key, and it copies the value out of a stored dictionary. Each has its report, as the
    // copy has with a key of a copyable type. A member that the list does not name at all names the value.
    [TestMethod]
    public Task Dictionary_ReportsTheCopyOfAValueBesidesAnUnlistedKey() => Verify.UpdatedRulesAsync(
        $$"""
        using System.Collections.Generic;

        class C
        {
            Dictionary<PG, UG> _mixed = null!;
            Dictionary<int, UG> _byNumber = null!;

            void M()
            {
                foreach (var pair in {|#0:_mixed|}) { }
                foreach (var pair in {|#1:_byNumber|}) { }
                var a = {|#2:_mixed[default]|};
                var b = {|#3:_byNumber[0]|};
                {|#4:_mixed.TryGetValue(default, {|#5:out var c|})|};
                bool has = {|#6:_mixed.ContainsValue(default)|};
            }
        }

        {{Types}}
        """,
        Verify.MemberListedForOtherTypeParameter(0, "Dictionary<TKey, TValue>.Enumerator.Current", "PG", "TKey", "TValue"),
        Verify.MemberListedForOtherTypeParameter(0, "Dictionary<TKey, TValue>.Enumerator.Dispose()", "PG", "TKey", "TValue"),
        Verify.MemberListedForOtherTypeParameter(0, "Dictionary<TKey, TValue>.Enumerator.MoveNext()", "PG", "TKey", "TValue"),
        Verify.MemberListedForOtherTypeParameter(0, "Dictionary<TKey, TValue>.GetEnumerator()", "PG", "TKey", "TValue"),
        Verify.ReadFromStoredContainer(0, "'foreach'", "System.Collections.Generic.KeyValuePair<PG, UG>").AsError(),
        Verify.ReadFromStoredContainer(1, "'foreach'", "System.Collections.Generic.KeyValuePair<int, UG>").AsError(),
        Verify.MemberListedForOtherTypeParameter(2, "Dictionary<TKey, TValue>.this[TKey]", "PG", "TKey", "TValue"),
        Verify.ReadFromStoredContainer(2, "'Dictionary<TKey, TValue>.this[TKey]'", "UG").AsError(),
        Verify.ReadFromStoredContainer(3, "'Dictionary<TKey, TValue>.this[TKey]'", "UG").AsError(),
        Verify.MemberListedForOtherTypeParameter(4, "Dictionary<TKey, TValue>.TryGetValue(TKey, out TValue)", "PG", "TKey", "TValue"),
        Verify.ReadFromStoredContainer(5, "'Dictionary<TKey, TValue>.TryGetValue(TKey, out TValue)'", "UG").AsError(),
        Verify.UnlistedMember(6, "Dictionary<TKey, TValue>.ContainsValue(TValue)", "UG", "TValue", "M:System.Collections.Generic.Dictionary`2.ContainsValue(`1)").AsError());

    // MemberwiseClone copies every field, so its report names a field whose type is unsafe to copy, of the class or of a class below
    // it, whichever comes first.
    [TestMethod]
    public Task MemberwiseClone_NamesAFieldThatIsUnsafeToCopy() => Verify.UpdatedRulesAsync(
        $$"""
        sealed class PlainFirst
        {
            PG _plain;
            UG _unsafe;

            public object Clone() => {|#0:MemberwiseClone()|};
        }

        sealed class UnsafeFirst
        {
            UG _unsafe;
            PG _plain;

            public object Clone() => {|#1:MemberwiseClone()|};
        }

        class Open
        {
            PG _plain;

            public object Clone() => {|#2:MemberwiseClone()|};
        }

        sealed class Below : Open
        {
            UG _unsafe;
        }

        sealed class Plain
        {
            PG _first;
            PG _second;

            public object Clone() => {|#3:MemberwiseClone()|};
        }

        {{Types}}
        """,
        Verify.MemberwiseCloneCall(0, "PlainFirst", "UG", "_unsafe").AsError(),
        Verify.MemberwiseCloneCall(1, "UnsafeFirst", "UG", "_unsafe").AsError(),
        Verify.MemberwiseCloneOfDerivedClass(2, "Open", "Below", "UG", "_unsafe").AsError(),
        Verify.MemberwiseCloneCall(3, "Plain", "PG", "_first"));

    // A member of an assembly that was not built with the analyzer names a parameter of a type that is unsafe to copy where it has one.
    [TestMethod]
    public async Task UncheckedMember_NamesAParameterOfATypeThatIsUnsafeToCopy()
    {
        if (!Verify.CompilerHasUpdatedRules)
            Assert.Inconclusive("The compiler of this target does not apply the updated memory safety rules.");

        var library = await Verify.CompileLibraryAsync(
            Verify.Test.NetReferenceAssemblies,
            """
            namespace Lib
            {
                [Singulink.CodeAnalysis.NonCopyable]
                public struct MP { }

                [Singulink.CodeAnalysis.NonCopyable(UnsafeToCopy = true)]
                public struct MU { }

                public static class Api
                {
                    public static MU Two(ref MP p, ref MU u) => default;
                    public static void Plain(ref MP p, ref MP q) { }
                }
            }
            """,
            analyzed: false,
            "Lib");

        var test = new Verify.Test {
            TestCode = """
                class C
                {
                    Lib.MP _p;
                    Lib.MP _q;
                    Lib.MU _u;

                    void M()
                    {
                        var c = {|#0:Lib.Api.Two(ref _p, ref _u)|};
                        {|#1:Lib.Api.Plain(ref _p, ref _q)|};
                    }
                }
                """,
            LanguageVersion = LanguageVersion.Preview,
            Features = [Verify.UpdatedRulesFeature],
            ExpectedDiagnostics = {
                Verify.UncheckedMember(0, "Api.Two(ref MP, ref MU)", "u", "Lib.MU", "M:Lib.Api.Two(Lib.MP@,Lib.MU@)").AsError(),
                Verify.UncheckedMember(1, "Api.Plain(ref MP, ref MP)", "p", "Lib.MP", "M:Lib.Api.Plain(Lib.MP@,Lib.MP@)"),
            },
        };

        test.TestState.AdditionalReferences.Add(library);
        await test.RunAsync();
    }

    // Combining delegates names a parameter of a type that is unsafe to copy where the delegate has one.
    [TestMethod]
    public Task CombinedDelegates_NameAParameterThatIsUnsafeToCopy() => Verify.UpdatedRulesAsync(
        $$"""
        delegate void Both(PG plain, UG unsafeToCopy);

        class C
        {
            void M(Both a, Both b)
            {
                var both = {|#0:a + b|};
            }
        }

        {{Types}}
        """,
        Verify.DelegatesCombined(0, "Both", "UG").AsError());

    // A clone of a class that clones itself copies every binding of the object, so a report about one names a binding of a type that is
    // unsafe to copy where there is one: in a base list, in an object creation and in a conversion. A conversion of a value that holds
    // such an object stands for the warning of the hidden container as well, and is strict where the value holds a type that is unsafe
    // to copy besides the binding that it names.
    [TestMethod]
    public Task CloningBinding_NamesABindingThatIsUnsafeToCopy() => Verify.UpdatedRulesAsync(
        $$"""
        using System.Globalization;
        using Singulink.CodeAnalysis;

        class Tagged2<[NonCopyable] A, [NonCopyable] B> : CultureInfo
        {
            public A {|#0:First|};
            public B {|#1:Second|};

            public Tagged2() : base("") { }
        }

        sealed class Derived : {|#2:Tagged2<PG, UG>|} { }

        class Holder<A, B> { }

        class C
        {
            void M(Holder<Tagged2<PG, PG>, UG> holder)
            {
                var created = {|#3:new Tagged2<PG, UG>()|};
                CultureInfo converted = {|#4:new Tagged2<PG, UG>()|};
                object held = {|#5:holder|};
            }
        }

        {{Types}}
        """,
        Verify.MemberwiseCloneOfTypeParameterThroughListedBaseOfTypeThatIsUnsafeToCopy(0, "Tagged2<A, B>", "System.Globalization.CultureInfo", "A", "First").AsError(),
        Verify.MemberwiseCloneOfTypeParameterThroughListedBaseOfTypeThatIsUnsafeToCopy(1, "Tagged2<A, B>", "System.Globalization.CultureInfo", "B", "Second").AsError(),
        Verify.CloningBindingInBaseListOfTypeThatIsUnsafeToCopy(2, "UG", "Tagged2<PG, UG>", "System.Globalization.CultureInfo").AsError(),
        Verify.CloningBindingOfTypeThatIsUnsafeToCopy(3, "UG", "Tagged2<PG, UG>", "System.Globalization.CultureInfo").AsError(),
        Verify.CloningUseConversionOfTypeThatIsUnsafeToCopy(4, "Tagged2<PG, UG>", "System.Globalization.CultureInfo", "System.Globalization.CultureInfo", "UG", "Tagged2<PG, UG>").AsError(),
        Verify.CloningUseConversionOfTypeThatIsUnsafeToCopy(5, "Holder<Tagged2<PG, PG>, UG>", "object", "System.Globalization.CultureInfo", "PG", "Tagged2<PG, PG>").AsError());

    // A conversion that hides two containers through variance names the one that holds a type that is unsafe to copy.
    [TestMethod]
    public Task Variance_NamesAContainerThatHoldsATypeThatIsUnsafeToCopy() => Verify.UpdatedRulesAsync(
        $$"""
        using System.Collections.Generic;

        interface IThree<out A, out B, out C> { }

        class Both<A, B> { }

        class C
        {
            void M(IThree<List<PG>, List<UG>, Both<PG, UG>> both, IThree<List<PG>, List<PG>, Both<PG, PG>> plain)
            {
                IThree<object, object, Both<PG, UG>> a = {|#0:both|};
                IThree<object, object, Both<PG, PG>> b = {|#1:plain|};
            }
        }

        {{Types}}
        """,
        Verify.OpaqueVariance(
            0,
            "System.Collections.Generic.List<UG>",
            "object",
            "IThree<System.Collections.Generic.List<PG>, System.Collections.Generic.List<UG>, Both<PG, UG>>",
            "IThree<object, object, Both<PG, UG>>").AsError(),
        Verify.OpaqueVariance(
            1,
            "System.Collections.Generic.List<PG>",
            "object",
            "IThree<System.Collections.Generic.List<PG>, System.Collections.Generic.List<PG>, Both<PG, PG>>",
            "IThree<object, object, Both<PG, PG>>"));

    // A use that breaks two rules for two type parameters gets both reports: the first does not hide the second.
    [TestMethod]
    public Task MemberUse_ThatBreaksTwoRulesGetsBothReports() => Verify.UpdatedRulesAsync(
        $$"""
        using System.Collections;
        using System.Collections.Generic;
        using Singulink.CodeAnalysis;

        class Gen<T1, [NonCopyable] T2>
            where T2 : IEnumerable
        {
            public static void Run() { }
        }

        class C
        {
            void M() => {|#0:Gen<PG, List<UG>>.Run()|};
        }

        {{Types}}
        """,
        Verify.UnlistedMember(0, "Gen<T1, T2>.Run()", "PG", "T1", "M:Gen`2.Run"),
        Verify.UntypedCollectionConstraint(0, "Gen<T1, T2>.Run()", "System.Collections.Generic.List<UG>", "T2", "System.Collections.IEnumerable").AsError());

    // A report that is strict because of a type that is unsafe to copy beside a plain one is the error that nothing suppresses:
    // '#pragma', [SuppressMessage] and NoWarn leave it, and the report about the plain type alone goes.
    [TestMethod]
    public Task Report_ThatInvolvesATypeThatIsUnsafeToCopyIsNotSuppressed()
    {
        if (!Verify.CompilerHasUpdatedRules)
            Assert.Inconclusive("The compiler of this target does not apply the updated memory safety rules.");

        return new Verify.Test {
            TestCode = $$"""
                using System.Diagnostics.CodeAnalysis;

                class Pair<T1, T2>
                {
                    public T2 Second;
                    public T2 Get() => Second;
                }

                class C
                {
                    Pair<PG, UG> _mixed = null!;
                    Pair<PG, PG> _plain = null!;

                #pragma warning disable SCA1020, SCA1005
                    [SuppressMessage("Reliability", "SCA1020", Justification = "The test shows that nothing suppresses the error")]
                    void M()
                    {
                        var a = {|#0:_mixed.Get()|};
                        var b = _plain.Get();
                    }
                #pragma warning restore SCA1020, SCA1005
                }

                sealed class Holder
                {
                    PG _plain;
                    UG _unsafe;

                #pragma warning disable SCA1005
                    [SuppressMessage("Reliability", "SCA1005", Justification = "The test shows that nothing suppresses the error")]
                    public object Clone() => {|#1:MemberwiseClone()|};
                #pragma warning restore SCA1005
                }

                {{Types}}
                """,
            LanguageVersion = LanguageVersion.Preview,
            Features = [Verify.UpdatedRulesFeature],
            NoWarn = [DiagnosticIds.UnlistedMember, DiagnosticIds.MemberwiseCloneCall],
            ExpectedDiagnostics = {
                Verify.UnlistedMember(0, "Pair<T1, T2>.Get()", "UG", "T2", "M:Pair`2.Get").AsError(),
                Verify.MemberwiseCloneCall(1, "Holder", "UG", "_unsafe").AsError(),
            },
        }.RunAsync();
    }

    // What a value carries is involved in every report that other code can get at it: a tuple of a plain guard and a list of a type that
    // is unsafe to copy lets that code reach the list, at any depth of tuples and structs, so a member that is not on a list and a box
    // that hides what the tuple holds are strict, and stay errors with '#pragma', [SuppressMessage] and NoWarn. A copy of the tuple
    // duplicates the guard and only a reference to the list, so the report of the copy is the ordinary warning, which they remove. Boxing
    // the stored tuple, by an assignment, a pattern or a 'foreach', copies it and hides the list from the analyzer, and both are reported:
    // one report does not hide the other.
    [TestMethod]
    public Task CarriedContainer_MakesAReportThatOtherCodeGetsAtTheValueStrictAndACopyTheWarning()
    {
        if (!Verify.CompilerHasUpdatedRules)
            Assert.Inconclusive("The compiler of this target does not apply the updated memory safety rules.");

        return new Verify.Test {
            TestCode = CarriedSource("UG"),
            LanguageVersion = LanguageVersion.Preview,
            Features = [Verify.UpdatedRulesFeature],
            NoWarn = [DiagnosticIds.UnsupportedUse, DiagnosticIds.Boxing, DiagnosticIds.UnlistedMember, DiagnosticIds.OpaqueType],
            ExpectedDiagnostics = {
                Verify.OpaqueBoxing(0, "(PG, System.Collections.Generic.List<UG>)", "object", "(T1, T2)").AsError(),
                Verify.OpaqueBoxing(1, "System.Collections.Generic.KeyValuePair<PG, System.Collections.Generic.List<UG>>", "object", "KeyValuePair<TKey, TValue>").AsError(),
                Verify.OpaqueBoxing(2, "(PG, (int, System.Collections.Generic.List<UG>))", "object", "(T1, T2)").AsError(),
                Verify.OpaqueBoxing(3, "(PG, System.Collections.Generic.List<UG>)", "object", "(T1, T2)").AsError(),
                Verify.OpaqueBoxing(4, "(PG, System.Collections.Generic.List<UG>)", "object", "(T1, T2)").AsError(),
                Verify.UnlistedMember(5, "C.Use<T>(T)", "(PG, System.Collections.Generic.List<UG>)", "T", "M:C.Use``1(``0)").AsError(),
                Verify.UnlistedMember(6, "C.Use<T>(T)", "(PG, System.Collections.Generic.List<UG>)", "T", "M:C.Use``1(``0)").AsError(),
                Verify.UnlistedMember(8, "C.Use<T>(T)", "Holder<System.Collections.Generic.List<UG>>", "T", "M:C.Use``1(``0)").AsError(),
            },
        }.RunAsync();
    }

    // The mirror with a plain type in the list: every report is the ordinary warning, which NoWarn suppresses.
    [TestMethod]
    public Task CarriedContainer_OfAPlainTypeLeavesEveryReportTheWarning()
    {
        if (!Verify.CompilerHasUpdatedRules)
            Assert.Inconclusive("The compiler of this target does not apply the updated memory safety rules.");

        return new Verify.Test {
            TestCode = CarriedSource("PG"),
            LanguageVersion = LanguageVersion.Preview,
            Features = [Verify.UpdatedRulesFeature],
            NoWarn = [DiagnosticIds.UnsupportedUse, DiagnosticIds.Boxing, DiagnosticIds.UnlistedMember, DiagnosticIds.OpaqueType],
        }.RunAsync();
    }

    // A copy of a tuple of a plain guard and a delegate or a list over a type that is unsafe to copy duplicates the guard and a
    // reference: the delegate and the list are not duplicated, so the report of the copy is the ordinary warning, in either order of
    // the elements, and '#pragma' removes it. The same data in a struct of the project's own gets the same warning.
    [TestMethod]
    public Task CarriedReference_LeavesTheReportOfACopyTheWarning() => Verify.UpdatedRulesAsync(
        $$"""
        using System;
        using System.Collections.Generic;
        using Singulink.CodeAnalysis;

        [NonCopyable]
        public struct Job { public PG Ticket; public Func<UG> MakeGuard; }

        class C
        {
            (PG Ticket, Func<UG> MakeGuard) _a;
            (Func<UG>, PG) _b;
            (PG, List<UG>) _c;
            (List<UG>, PG) _d;
            Job _job;

            void Copies()
            {
                var a = {|#0:_a|};
                var b = {|#1:_b|};
                var c = {|#2:_c|};
                var d = {|#3:_d|};
                var job = {|#4:_job|};
            }

            void Suppressed()
            {
        #pragma warning disable SCA1001
                var a = _a;
                var b = _b;
                var c = _c;
                var d = _d;
                var job = _job;
        #pragma warning restore SCA1001
            }
        }

        {{Types}}
        """,
        Verify.CopyByAssignment(0, "(PG Ticket, System.Func<UG> MakeGuard)"),
        Verify.CopyByAssignment(1, "(System.Func<UG>, PG)"),
        Verify.CopyByAssignment(2, "(PG, System.Collections.Generic.List<UG>)"),
        Verify.CopyByAssignment(3, "(System.Collections.Generic.List<UG>, PG)"),
        Verify.CopyByAssignment(4, "Job"));

    // Other code that gets at such a tuple reaches what is behind the delegate or the list: a member that is not on a list and a box
    // that hides what the tuple holds are strict, in either order of the elements, and '#pragma' leaves them.
    [TestMethod]
    public Task CarriedReference_MakesAReportThatOtherCodeGetsAtTheValueStrict() => Verify.UpdatedRulesAsync(
        $$"""
        using System;
        using System.Collections.Generic;

        class C
        {
            static (PG, Func<UG>) A() => default;
            static (Func<UG>, PG) B() => default;
            static (PG, List<UG>) D() => default;
            static (List<UG>, PG) E() => default;

            static void Use<T>(T value) { }

        #pragma warning disable SCA1020, SCA1023
            void M()
            {
                {|#0:Use(A())|};
                {|#1:Use(B())|};
                {|#2:Use(D())|};
                {|#3:Use(E())|};
                object a = {|#4:A()|};
                object b = {|#5:B()|};
                object d = {|#6:D()|};
                object e = {|#7:E()|};
            }
        #pragma warning restore SCA1020, SCA1023
        }

        {{Types}}
        """,
        Verify.UnlistedMember(0, "C.Use<T>(T)", "(PG, System.Func<UG>)", "T", "M:C.Use``1(``0)").AsError(),
        Verify.UnlistedMember(1, "C.Use<T>(T)", "(System.Func<UG>, PG)", "T", "M:C.Use``1(``0)").AsError(),
        Verify.UnlistedMember(2, "C.Use<T>(T)", "(PG, System.Collections.Generic.List<UG>)", "T", "M:C.Use``1(``0)").AsError(),
        Verify.UnlistedMember(3, "C.Use<T>(T)", "(System.Collections.Generic.List<UG>, PG)", "T", "M:C.Use``1(``0)").AsError(),
        Verify.OpaqueBoxing(4, "(PG, System.Func<UG>)", "object", "(T1, T2)").AsError(),
        Verify.OpaqueBoxing(5, "(System.Func<UG>, PG)", "object", "(T1, T2)").AsError(),
        Verify.OpaqueBoxing(6, "(PG, System.Collections.Generic.List<UG>)", "object", "(T1, T2)").AsError(),
        Verify.OpaqueBoxing(7, "(System.Collections.Generic.List<UG>, PG)", "object", "(T1, T2)").AsError());

    // The box of a generic struct hides what its type arguments make reachable, whether its own code is checked or not: a box of a
    // marked generic struct over a list is reported as the box of a tuple over one is, for a stored value, a new one, a nullable, a
    // pattern and a 'foreach', and is strict where the list holds a type that is unsafe to copy. A struct over no container hides
    // nothing that its box does not show, and gets only the report of the copy.
    [TestMethod]
    public Task MarkedGenericStruct_BoxHidesWhatItsTypeArgumentsCarry() => Verify.UpdatedRulesAsync(
        $$"""
        using System.Collections.Generic;
        using Singulink.CodeAnalysis;

        [NonCopyable]
        public struct Mk<[NonCopyable] T> { public T Value; }

        class C
        {
            Mk<List<UG>> _list;
            Mk<(PG, List<UG>)> _tuple;
            Mk<List<PG>> _plain;
            Mk<PG> _guard;
            (PG, List<UG>) _bare;

            static Mk<List<UG>> Create() => default;

            static IEnumerable<Mk<List<UG>>> CreateMany() => [];

            void M()
            {
                object list = {|#0:_list|};
                object tuple = {|#1:_tuple|};
                object plain = {|#2:_plain|};
                object guard = {|#3:_guard|};
                object bare = {|#4:_bare|};
                object created = {|#5:Create()|};
                object nullable = {|#6:(Mk<List<UG>>?)Create()|};

                if (Create() is {|#7:object matched|}) { }

                foreach (object item in {|#8:CreateMany()|}) { }
            }
        }

        {{Types}}
        """,
        Verify.Boxing(0, "Mk<System.Collections.Generic.List<UG>>", "object"),
        Verify.OpaqueBoxingOfContainers(0, "Mk<System.Collections.Generic.List<UG>>", "object").AsError(),
        Verify.Boxing(1, "Mk<(PG, System.Collections.Generic.List<UG>)>", "object"),
        Verify.OpaqueBoxingOfContainers(1, "Mk<(PG, System.Collections.Generic.List<UG>)>", "object").AsError(),
        Verify.Boxing(2, "Mk<System.Collections.Generic.List<PG>>", "object"),
        Verify.OpaqueBoxingOfContainers(2, "Mk<System.Collections.Generic.List<PG>>", "object"),
        Verify.Boxing(3, "Mk<PG>", "object"),
        Verify.Boxing(4, "(PG, System.Collections.Generic.List<UG>)", "object"),
        Verify.OpaqueBoxing(4, "(PG, System.Collections.Generic.List<UG>)", "object", "(T1, T2)").AsError(),
        Verify.OpaqueBoxingOfContainers(5, "Mk<System.Collections.Generic.List<UG>>", "object").AsError(),
        Verify.OpaqueBoxingOfContainers(6, "Mk<System.Collections.Generic.List<UG>>?", "object").AsError(),
        Verify.OpaqueBoxingOfContainers(7, "Mk<System.Collections.Generic.List<UG>>", "object").AsError(),
        Verify.OpaqueBoxingOfContainers(8, "Mk<System.Collections.Generic.List<UG>>", "object").AsError());

    private static string CarriedSource(string inList) => $$"""
        using System.Collections.Generic;
        using System.Diagnostics.CodeAnalysis;
        using Singulink.CodeAnalysis;

        [NonCopyable]
        public struct Holder<[NonCopyable] T> { public T Value; }

        class C
        {
            (PG, List<{{inList}}>) _tuple;
            KeyValuePair<PG, List<{{inList}}>> _pair;
            (PG, (int, List<{{inList}}>)) _nested;
            (PG, List<{{inList}}>)[] _tuples = [];
            Holder<List<{{inList}}>> _holder;

            static (PG, List<{{inList}}>) Make() => (default, new());

            static void Use<T>(T value) { }

        #pragma warning disable SCA1003, SCA1004, SCA1020, SCA1023
            [SuppressMessage("Reliability", "SCA1003")]
            [SuppressMessage("Reliability", "SCA1004")]
            [SuppressMessage("Reliability", "SCA1020")]
            [SuppressMessage("Reliability", "SCA1023")]
            void M()
            {
                object boxed = {|#0:_tuple|};
                object pair = {|#1:_pair|};
                object nested = {|#2:_nested|};

                if (_tuple is {|#3:object matched|}) { }

                foreach (object element in {|#4:_tuples|}) { }

                {|#5:Use(Make())|};
                {|#6:Use({|#7:_tuple|})|};
                {|#8:Use(NonCopyable.Move(ref _holder))|};
            }
        #pragma warning restore SCA1003, SCA1004, SCA1020, SCA1023
        }

        {{Types}}
        """;
}
