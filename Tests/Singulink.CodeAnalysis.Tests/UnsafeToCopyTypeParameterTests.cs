using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;
using PrefixClassName.MsTest;

namespace Singulink.CodeAnalysis.Tests;

/// <summary>
/// Tests of a type parameter marked <c>[NonCopyable(UnsafeToCopy = false)]</c>: inside the generic it is a plain non-copyable type, and the
/// generic takes only types that are not unsafe to copy. A type that is unsafe to copy, or a type parameter that can stand for one, is
/// reported where it is bound to such a type parameter, and an override or an implementation may say it only where the member that it
/// stands for does.
/// </summary>
/// <remarks>
/// Docs: articles/rules/marked-type-parameters.md, "Types That Are Unsafe to Copy", and articles/diagnostics/sca1026.md.
/// </remarks>
[PrefixTestClass]
public class UnsafeToCopyTypeParameterTests
{
    // A plain non-copyable struct and one that is unsafe to copy.
    private const string Types = """
        [Singulink.CodeAnalysis.NonCopyable]
        public struct Guard { }

        [Singulink.CodeAnalysis.NonCopyable(UnsafeToCopy = true)]
        public struct Handle { }
        """;

    // Generics whose type parameter takes no type that is unsafe to copy, one for each way in which a type can be bound to it.
    private const string Generics = """
        public interface IUse { void Use(); }

        [Singulink.CodeAnalysis.NonCopyable]
        public struct Scoped<[Singulink.CodeAnalysis.NonCopyable(UnsafeToCopy = false)] T> : IUse
        {
            public static Scoped<T> Create() => default;

            public void Use() { }

            public struct Inner { public void Use() { } }
        }

        public class Base<[Singulink.CodeAnalysis.NonCopyable(UnsafeToCopy = false)] T> { }

        public interface IUser<[Singulink.CodeAnalysis.NonCopyable(UnsafeToCopy = false)] T> { }

        public class Cell<[Singulink.CodeAnalysis.NonCopyable(UnsafeToCopy = false)] T> { }

        public delegate void RefAction<[Singulink.CodeAnalysis.NonCopyable] T>(ref T value);

        public static class Methods
        {
            public static void Take<[Singulink.CodeAnalysis.NonCopyable(UnsafeToCopy = false)] T>(ref T value) { }

            public static void Touch<[Singulink.CodeAnalysis.NonCopyable(UnsafeToCopy = false)] T>(this ref T value) where T : struct { }

            public static T Make<[Singulink.CodeAnalysis.NonCopyable] T>() where T : new() => new T();

            public static void Run<[Singulink.CodeAnalysis.NonCopyable] T>(ref T value) where T : IUse => value.Use();
        }
        """;

    // Inside the generic a value of the type parameter is plain under the updated rules, and its copy gets the ordinary warning.
    [TestMethod]
    public Task InsideTheGeneric_ACopyIsTheOrdinaryWarning() => Verify.UpdatedRulesAsync(
        """
        using Singulink.CodeAnalysis;

        static class C
        {
            static T Plain<[NonCopyable(UnsafeToCopy = false)] T>(ref T value) => {|#0:value|};

            static T Strict<[NonCopyable] T>(ref T value) => {|#1:value|};

            static T WrittenOut<[NonCopyable(UnsafeToCopy = true)] T>(ref T value) => {|#2:value|};
        }
        """,
        Verify.CopyByReturn(0, "T"),
        Verify.CopyByReturn(1, "T").AsError(),
        Verify.CopyByReturn(2, "T").AsError());

    // Every way in which a type can be bound: a type named in code, a static member, a member of a value of a type named in a declaration,
    // a nested type, a base type and an interface, a call with type arguments written and inferred, an extension, a method group made into
    // a delegate, a constructor that the 'new()' constraint runs, a box of the struct, and a constrained type parameter that it is bound to.
    // A type that is unsafe to copy is reported at each, and a plain one at none. Naming the type in a declaration runs no code and is
    // not reported; what uses its value is.
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task EveryWayOfBinding_IsReportedForATypeThatIsUnsafeToCopy(bool updatedRules)
    {
        string source = $$"""
            class C
            {
                Scoped<Handle> _field;
                Scoped<Guard> _plainField;

                void M(Handle handle, Guard guard)
                {
                    var created = {|#0:new Scoped<Handle>()|};
                    var fromStatic = {|#1:Scoped<Handle>.Create()|};
                    {|#2:_field.Use()|};
                    var inner = {|#3:new Scoped<Handle>.Inner()|};
                    {|#14:inner.Use()|};
                    {|#4:Methods.Take<Handle>(ref handle)|};
                    {|#5:Methods.Take(ref handle)|};
                    {|#6:handle.Touch()|};
                    RefAction<Handle> action = {|#7:Methods.Take|};
                    var cell = {|#8:Methods.Make<Cell<Handle>>()|};
                    IUse boxed = {|#9:default(Scoped<Handle>)|};
                    var value = default(Scoped<Handle>);
                    {|#10:Methods.Run(ref value)|};
                    var nested = {|#11:new Scoped<Scoped<Handle>>()|};

                    var plainCreated = new Scoped<Guard>();
                    var plainFromStatic = Scoped<Guard>.Create();
                    _plainField.Use();
                    var plainInner = new Scoped<Guard>.Inner();
                    plainInner.Use();
                    Methods.Take<Guard>(ref guard);
                    Methods.Take(ref guard);
                    guard.Touch();
                    RefAction<Guard> plainAction = Methods.Take;
                    var plainCell = Methods.Make<Cell<Guard>>();
                    IUse plainBoxed = default(Scoped<Guard>);
                    var plainValue = default(Scoped<Guard>);
                    Methods.Run(ref plainValue);
                }
            }

            class Derived : {|#12:Base<Handle>|}, {|#13:IUser<Handle>|} { }

            class PlainDerived : Base<Guard>, IUser<Guard> { }

            {{Generics}}

            {{Types}}
            """;

        DiagnosticResult[] expected = [
            Verify.UnsafeToCopyNotTaken(0, "Scoped<T>", "Handle", "T").AsError(updatedRules),
            Verify.UnsafeToCopyNotTaken(1, "Scoped<T>", "Handle", "T").AsError(updatedRules),
            Verify.UnsafeToCopyNotTaken(2, "Scoped<T>", "Handle", "T").AsError(updatedRules),
            Verify.UnsafeToCopyNotTaken(3, "Scoped<T>", "Handle", "T").AsError(updatedRules),
            Verify.UnsafeToCopyNotTaken(14, "Scoped<T>", "Handle", "T").AsError(updatedRules),
            Verify.UnsafeToCopyNotTaken(4, "Methods.Take<T>(ref T)", "Handle", "T").AsError(updatedRules),
            Verify.UnsafeToCopyNotTaken(5, "Methods.Take<T>(ref T)", "Handle", "T").AsError(updatedRules),
            Verify.UnsafeToCopyNotTaken(6, "Methods.Touch<T>(ref T)", "Handle", "T").AsError(updatedRules),
            Verify.UnsafeToCopyNotTaken(7, "Methods.Take<T>(ref T)", "Handle", "T").AsError(updatedRules),
            Verify.UnsafeToCopyNotTaken(8, "Cell<T>", "Handle", "T").AsError(updatedRules),
            Verify.UnsafeToCopyNotTakenBehindBox(9, "Scoped<Handle>", "IUse", "Scoped<T>", "Handle", "T").AsError(updatedRules),
            Verify.UnsafeToCopyNotTakenBehindTypeParameter(10, "Methods.Run<T>(ref T)", "Scoped<Handle>", "T", "Scoped<T>", "Handle", "T").AsError(updatedRules),
            Verify.UnsafeToCopyNotTaken(11, "Scoped<T>", "Scoped<Handle>", "T").AsError(updatedRules),
            Verify.UnsafeToCopyNotTaken(12, "Base<T>", "Handle", "T").AsError(updatedRules),
            Verify.UnsafeToCopyNotTaken(13, "IUser<T>", "Handle", "T").AsError(updatedRules),
        ];

        return updatedRules ? Verify.UpdatedRulesAsync(source, expected) : Verify.AnalyzerAsync(source, expected);
    }

    // A read or a write of a static field runs the initializers of the static fields of the generic and its static constructor, which
    // copy values of the type parameter with warnings only, so it is reported as a use of a member is: a static field of the type
    // parameter or of a container of it, of a nested type, and one that a type parameter of the generic around is bound to. A constant
    // and 'nameof' run nothing, and a plain non-copyable type is taken.
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task StaticField_OfTheGenericIsReportedAsAUseOfAMember(bool updatedRules)
    {
        string source = $$"""
            using System.Collections.Generic;
            using Singulink.CodeAnalysis;

            class Cache<[NonCopyable(UnsafeToCopy = false)] T>
            {
                public const int Size = 4;
                public static T First;
                public static T Second = {|#0:First|};
                public static List<T> All = new();

                static Cache() { }

                public static class Nested
                {
                    public static int Count;
                }
            }

            class C<[NonCopyable] U>
            {
                void M()
                {
                    ref Handle first = ref {|#1:Cache<Handle>.First|};
                    var all = {|#2:Cache<Handle>.All|};
                    {|#3:Cache<Handle>.First|} = default;
                    int count = {|#4:Cache<Handle>.Nested.Count|};
                    ref U open = ref {|#5:Cache<U>.First|};
                    int size = Cache<Handle>.Size;
                    string name = nameof(Cache<Handle>.First);

                    ref Guard plain = ref Cache<Guard>.First;
                    var plainAll = Cache<Guard>.All;
                    int plainCount = Cache<Guard>.Nested.Count;
                }
            }

            {{Types}}
            """;

        DiagnosticResult[] expected = [
            Verify.CopyOfStoredValue(0, "The initializer takes", "T"),
            Verify.UnsafeToCopyNotTaken(1, "Cache<T>", "Handle", "T").AsError(updatedRules),
            Verify.UnsafeToCopyNotTaken(2, "Cache<T>", "Handle", "T").AsError(updatedRules),
            Verify.UnsafeToCopyNotTaken(3, "Cache<T>", "Handle", "T").AsError(updatedRules),
            Verify.UnsafeToCopyNotTaken(4, "Cache<T>", "Handle", "T").AsError(updatedRules),
            Verify.UnsafeToCopyTypeParameterNotTaken(5, "Cache<T>", "U", "T").AsError(updatedRules),
        ];

        return updatedRules ? Verify.UpdatedRulesAsync(source, expected) : Verify.AnalyzerAsync(source, expected);
    }

    // A pattern and a 'foreach' that box a new value of the struct make the box as a conversion does, and a nullable boxes as what it
    // holds. A 'foreach' over a collection of the struct gives it to the collection's code first, where it has its one report.
    [TestMethod]
    public Task Box_ThatAPatternOrAForEachMakesIsReported() => Verify.AnalyzerAsync(
        $$"""
        using System.Collections.Generic;

        class C
        {
            static Scoped<Handle> Create() => default;

            static Scoped<Guard> CreatePlain() => default;

            static IEnumerable<Scoped<Handle>> CreateMany() => [];

            void M()
            {
                if (Create() is {|#0:IUse used|}) { }
                if (CreatePlain() is IUse plainUsed) { }

                foreach (IUse item in {|#1:CreateMany()|}) { }

                object boxed = {|#2:(Scoped<Handle>?)Create()|};
            }
        }

        {{Generics}}

        {{Types}}
        """,
        Verify.UnsafeToCopyNotTakenBehindBox(0, "Scoped<Handle>", "IUse", "Scoped<T>", "Handle", "T"),
        Verify.UnsafeToCopyNotTakenBehindTypeParameter(1, "IEnumerable<T>.GetEnumerator()", "Scoped<Handle>", "T", "Scoped<T>", "Handle", "T"),
        Verify.UnsafeToCopyNotTakenBehindBox(2, "Scoped<Handle>?", "object", "Scoped<T>", "Handle", "T"));

    // A type parameter of the generic around the use counts as unsafe to copy unless it says UnsafeToCopy = false itself. One that is not
    // marked is a copyable type there, and is judged where that generic is used.
    [TestMethod]
    public Task TypeParameterOfTheGenericAround_CountsAsUnsafeToCopyUnlessItSaysFalse() => Verify.UpdatedRulesAsync(
        $$"""
        using Singulink.CodeAnalysis;

        class Outer<[NonCopyable] A, [NonCopyable(UnsafeToCopy = true)] B, [NonCopyable(UnsafeToCopy = false)] P, U>
        {
            void M()
            {
                var a = {|#0:new Scoped<A>()|};
                var b = {|#1:new Scoped<B>()|};
                var p = new Scoped<P>();
                var u = new Scoped<U>();
            }
        }

        class DerivedOver<[NonCopyable] T> : {|#2:Base<T>|} { }

        class PlainDerivedOver<[NonCopyable(UnsafeToCopy = false)] T> : Base<T> { }

        {{Generics}}

        {{Types}}
        """,
        Verify.UnsafeToCopyTypeParameterNotTaken(0, "Scoped<T>", "A", "T").AsError(),
        Verify.UnsafeToCopyTypeParameterNotTaken(1, "Scoped<T>", "B", "T").AsError(),
        Verify.UnsafeToCopyTypeParameterNotTaken(2, "Base<T>", "T", "T").AsError());

    // The property is read from the mark of a type parameter in a referenced assembly, where it was written and where it was not.
    [TestMethod]
    public async Task Mark_OfAReferencedAssemblyIsRead()
    {
        var library = await Verify.CompileLibraryAsync(
            Verify.Test.NetReferenceAssemblies,
            "namespace Lib { " + Generics + Types + " }",
            analyzed: true,
            "Lib");

        var test = new Verify.Test {
            TestCode = """
                using Lib;

                class C
                {
                    void M(Handle handle, Guard guard)
                    {
                        {|#0:Methods.Take(ref handle)|};
                        Methods.Take(ref guard);
                        var made = Methods.Make<Cell<Guard>>();
                        var created = {|#1:new Scoped<Handle>()|};
                    }
                }

                class Derived : {|#2:Base<Handle>|} { }
                """,
            ExpectedDiagnostics = {
                Verify.UnsafeToCopyNotTaken(0, "Methods.Take<T>(ref T)", "Lib.Handle", "T"),
                Verify.UnsafeToCopyNotTaken(1, "Scoped<T>", "Lib.Handle", "T"),
                Verify.UnsafeToCopyNotTaken(2, "Base<T>", "Lib.Handle", "T"),
            },
        };

        test.TestState.AdditionalReferences.Add(library);
        await test.RunAsync();
    }

    // Inside a generic of a referenced assembly the property decides as in source: a copy of a value of a type parameter that says false
    // is plain where the library's own build checks it, and the referencing project binds only what the generic takes.
    [TestMethod]
    public async Task Mark_OfAReferencedAssemblyDecidesTheSeverityOfAReportAboutTheTypeParameter()
    {
        if (!Verify.CompilerHasUpdatedRules)
            Assert.Inconclusive("The compiler of this target does not apply the updated memory safety rules.");

        var library = await Verify.CompileLibraryAsync(
            Verify.Test.NetReferenceAssemblies,
            """
            namespace Lib
            {
                public class Plain<[Singulink.CodeAnalysis.NonCopyable(UnsafeToCopy = false)] T> { public T Value; }

                public class Strict<[Singulink.CodeAnalysis.NonCopyable] T> { public T Value; }
            }
            """,
            analyzed: true,
            "Lib");

        var test = new Verify.Test {
            TestCode = """
                using Singulink.CodeAnalysis;

                class C<[NonCopyable(UnsafeToCopy = false)] P, [NonCopyable] S>
                {
                    void M(Lib.Plain<P> plain, Lib.Strict<S> strict)
                    {
                        var p = {|#0:plain.Value|};
                        var s = {|#1:strict.Value|};
                    }
                }
                """,
            LanguageVersion = LanguageVersion.Preview,
            Features = [Verify.UpdatedRulesFeature],
            ExpectedDiagnostics = {
                Verify.CopyByAssignment(0, "P"),
                Verify.CopyByAssignment(1, "S").AsError(),
            },
        };

        test.TestState.AdditionalReferences.Add(library);
        await test.RunAsync();
    }

    // An override, an implicit and an explicit implementation may say UnsafeToCopy = false only where the member that they stand for
    // does, or does not mark the type parameter at all, as they may mark it there: a call through a member whose type parameter is
    // marked could bind a type that is unsafe to copy, and a call through one whose type parameter is not marked is reported with any
    // non-copyable type. The other way round is fine, and so is saying it on both.
    [TestMethod]
    public Task Overrides_AndImplementationsSayFalseOnlyWhereTheBaseMemberDoesOrDoesNotMark() => Verify.UpdatedRulesAsync(
        """
        using Singulink.CodeAnalysis;

        abstract class Base
        {
            public abstract void Strict<[NonCopyable] T>(ref T value);

            public abstract void Plain<[NonCopyable(UnsafeToCopy = false)] T>(ref T value);

            public abstract void Unmarked<T>(ref T value);
        }

        class Derived : Base
        {
            public override void Strict<[NonCopyable(UnsafeToCopy = false)] {|#0:T|}>(ref T value) { }

            public override void Plain<[NonCopyable] T>(ref T value) { }

            public override void Unmarked<[NonCopyable(UnsafeToCopy = false)] T>(ref T value) { }
        }

        class BothPlain : Base
        {
            public override void Strict<[NonCopyable] T>(ref T value) { }

            public override void Plain<[NonCopyable(UnsafeToCopy = false)] T>(ref T value) { }

            public override void Unmarked<[NonCopyable] T>(ref T value) { }
        }

        // A call through the member whose type parameter is not marked is reported, and so is one of the override with a type that
        // is unsafe to copy.
        class Caller
        {
            void Call(Base through, Derived direct, ref Native native)
            {
                {|#1:through.Unmarked(ref native)|};
                {|#5:direct.Unmarked<Native>(ref native)|};
            }
        }

        [NonCopyable(UnsafeToCopy = true)]
        struct Native { }

        interface IWork
        {
            void Strict<[NonCopyable] T>(ref T value);

            void Plain<[NonCopyable(UnsafeToCopy = false)] T>(ref T value);
        }

        class Implicit : IWork
        {
            public void Strict<[NonCopyable(UnsafeToCopy = false)] {|#2:T|}>(ref T value) { }

            public void Plain<[NonCopyable] T>(ref T value) { }
        }

        class Explicit : IWork
        {
            void IWork.Strict<[NonCopyable(UnsafeToCopy = false)] {|#3:T|}>(ref T value) { }

            void IWork.Plain<[NonCopyable(UnsafeToCopy = false)] T>(ref T value) { }
        }

        // A class that uses an inherited method to implement the interface is reported at the class.
        class Inherits
        {
            public void Strict<[NonCopyable(UnsafeToCopy = false)] T>(ref T value) { }

            public void Plain<[NonCopyable] T>(ref T value) { }
        }

        class {|#4:BringsInherited|} : Inherits, IWork { }
        """,
        Verify.TypeParameterTakesNoUnsafeToCopy(0, "T", "Derived.Strict<T>(ref T)", "T", "Base.Strict<T>(ref T)").AsError(),
        Verify.TypeParameterTakesNoUnsafeToCopy(2, "T", "Implicit.Strict<T>(ref T)", "T", "IWork.Strict<T>(ref T)").AsError(),
        Verify.TypeParameterTakesNoUnsafeToCopy(3, "T", "Explicit.IWork.Strict<T>(ref T)", "T", "IWork.Strict<T>(ref T)").AsError(),
        Verify.TypeParameterTakesNoUnsafeToCopy(4, "T", "Inherits.Strict<T>(ref T)", "T", "IWork.Strict<T>(ref T)").AsError(),
        Verify.UnlistedMember(1, "Base.Unmarked<T>(ref T)", "Native", "T", "M:Base.Unmarked``1(``0@)").AsError(),
        Verify.UnsafeToCopyNotTaken(5, "Derived.Unmarked<T>(ref T)", "Native", "T").AsError());

    // The two parts of a partial method share the attributes of their type parameters, so they cannot disagree: the compiler rejects the
    // mark written on both parts, and a mark on one part is the mark of both.
    [TestMethod]
    public Task PartialMethod_PartsShareTheMark() => Verify.AnalyzerAsync(
        """
        using Singulink.CodeAnalysis;

        partial class C
        {
            partial void OnePart<[NonCopyable(UnsafeToCopy = false)] T>(ref T value);

            partial void BothParts<[NonCopyable] T>(ref T value);

            void M(ref Handle handle) => {|#0:OnePart(ref handle)|};
        }

        partial class C
        {
            partial void OnePart<T>(ref T value) { }

            partial void BothParts<[{|#1:NonCopyable|}(UnsafeToCopy = false)] T>(ref T value) { }
        }

        [NonCopyable(UnsafeToCopy = true)]
        struct Handle { }
        """,
        Verify.UnsafeToCopyNotTaken(0, "C.OnePart<T>(ref T)", "Handle", "T"),
        DiagnosticResult.CompilerError("CS0579").WithLocation(1).WithArguments("NonCopyable"));

    // UnsafeToCopy = true on a type parameter is the default written out, and UnsafeToCopy = false on a struct is too: neither gets a report.
    [TestMethod]
    public Task DefaultWrittenOut_GetsNoReport() => Verify.UpdatedRulesAsync(
        """
        using Singulink.CodeAnalysis;

        [NonCopyable(UnsafeToCopy = false)]
        struct Guard { }

        static class C
        {
            static void Take<[NonCopyable(UnsafeToCopy = true)] T>(ref T value) { }

            static void M(ref Handle handle, ref Guard guard)
            {
                Take(ref handle);
                Take(ref guard);
            }
        }

        [NonCopyable(UnsafeToCopy = true)]
        struct Handle { }
        """);

    // Generic code can run the members of what is bound to its type parameter with nothing that shows them: a static abstract or virtual
    // member through a constraint, ToString, Equals and GetHashCode through any type parameter, and the members of the elements of a
    // collection. So a type with a binding that a type parameter does not take is reported where it is given to other generic code, as
    // the type argument or at any depth inside it, written or inferred. A use that is reported already for that type parameter, as a
    // member that is not on the symbol list, is not reported again: that report is strict, and one line answers it for every type
    // parameter that the member is not vouched for, the one that it names or another.
    [TestMethod]
    public Task TypeGivenToOtherGenericCode_IsReported() => Verify.UpdatedRulesAsync(
        $$"""
        using System.Collections.Generic;
        using Singulink.CodeAnalysis;

        public interface IStat { static abstract void Run(); }

        public interface IVirt { static virtual void Run() { } }

        public class Stat<[NonCopyable(UnsafeToCopy = false)] X> : IStat, IVirt
            where X : new()
        {
            public static X Slot = new();

            public static void Run() { var copy = {|#0:Slot|}; }
        }

        [NonCopyable]
        public struct Text<[NonCopyable(UnsafeToCopy = false)] X>
        {
            public X Value;

            public override string ToString() { var copy = {|#1:Value|}; return ""; }
        }

        public static class Runner
        {
            public static void Run<[NonCopyable] T>() where T : IStat => T.Run();

            public static void RunVirtual<[NonCopyable] T>() where T : IVirt => T.Run();

            public static void RunUnmarked<T>() where T : IVirt => T.Run();

            public static string Show<[NonCopyable] T>(ref T value) => value.ToString();
        }

        public class MarkedRunner<[NonCopyable] T> where T : IStat
        {
            public void Run() => T.Run();
        }

        public class Pair<T1, T2>
        {
            public void Run() { }
        }

        class C
        {
            Text<Handle> _text;
            Text<Guard> _plainText;
            List<Text<Handle>> _list = null!;
            Dictionary<int, Text<Handle>> _dictionary = null!;
            List<List<Text<Handle>>> _nested = null!;
            Pair<Handle, Text<Handle>> _pair = null!;

            void M()
            {
                {|#2:Runner.Run<Stat<Handle>>()|};
                {|#3:Runner.RunVirtual<Stat<Handle>>()|};
                {|#4:Runner.RunUnmarked<Stat<Handle>>()|};
                {|#6:{|#5:new MarkedRunner<Stat<Handle>>()|}.Run()|};
                _ = {|#7:Runner.Show(ref _text)|};
                _ = {|#8:_list.Contains(default)|};
                _ = {|#9:_dictionary.ContainsKey(0)|};
                _ = {|#10:_nested.Count|};
                {|#11:_pair.Run()|};

                Runner.Run<Stat<Guard>>();
                _ = Runner.Show(ref _plainText);
            }
        }

        {{Types}}
        """,
        Verify.CopyByAssignment(0, "X"),
        Verify.CopyByAssignment(1, "X"),
        Verify.UnsafeToCopyNotTakenBehindTypeParameter(2, "Runner.Run<T>()", "Stat<Handle>", "T", "Stat<X>", "Handle", "X").AsError(),
        Verify.UnsafeToCopyNotTakenBehindTypeParameter(3, "Runner.RunVirtual<T>()", "Stat<Handle>", "T", "Stat<X>", "Handle", "X").AsError(),
        Verify.OpaqueTypeArgument(4, "Runner.RunUnmarked<T>()", "Stat<Handle>", "T", "M:Runner.RunUnmarked``1").AsError(),
        Verify.UnsafeToCopyNotTakenBehindTypeParameter(5, "MarkedRunner<T>.MarkedRunner()", "Stat<Handle>", "T", "Stat<X>", "Handle", "X").AsError(),
        Verify.UnsafeToCopyNotTakenBehindTypeParameter(6, "MarkedRunner<T>.Run()", "Stat<Handle>", "T", "Stat<X>", "Handle", "X").AsError(),
        Verify.UnsafeToCopyNotTakenBehindTypeParameter(7, "Runner.Show<T>(ref T)", "Text<Handle>", "T", "Text<X>", "Handle", "X").AsError(),
        Verify.UnlistedMember(8, "List<T>.Contains(T)", "Text<Handle>", "T", "M:System.Collections.Generic.List`1.Contains(`0)").AsError(),
        Verify.UnsafeToCopyNotTakenBehindTypeParameter(9, "Dictionary<TKey, TValue>.ContainsKey(TKey)", "Text<Handle>", "TValue", "Text<X>", "Handle", "X").AsError(),
        Verify.UnsafeToCopyNotTakenBehindTypeParameter(
            10, "List<T>.Count", "System.Collections.Generic.List<Text<Handle>>", "T", "Text<X>", "Handle", "X").AsError(),
        Verify.UnlistedMember(11, "Pair<T1, T2>.Run()", "Handle", "T1", "M:Pair`2.Run").AsError());

    // A struct that holds such a type inline runs its members through the Equals and GetHashCode that it inherits from ValueType, so its
    // field is reported, unless the struct overrides both. A class field and a field of a type that holds it by reference are not.
    [TestMethod]
    public Task FieldOfAStruct_IsReportedWhereTheInheritedEqualityRunsItsMembers() => Verify.UpdatedRulesAsync(
        $$"""
        using System.Collections.Generic;
        using Singulink.CodeAnalysis;

        [NonCopyable]
        public struct Text<[NonCopyable(UnsafeToCopy = false)] X>
        {
            public X Value;
        }

        [NonCopyable(UnsafeToCopy = true)]
        public struct Holds
        {
            public Text<Handle> {|#0:Inner|};
            public (int, Text<Handle>) {|#1:Pair|};
            public List<Text<Handle>> ByReference;
        }

        [NonCopyable(UnsafeToCopy = true)]
        public struct HoldsWithOwnEquality
        {
            public Text<Handle> Inner;

            public override bool Equals(object obj) => false;

            public override int GetHashCode() => 0;
        }

        public class InAClass
        {
            public Text<Handle> Inner;
        }

        {{Types}}
        """,
        Verify.UnsafeToCopyNotTakenInField(0, "Inner", "Holds", "Text<Handle>", "Text<X>", "Handle", "X").AsError(),
        Verify.UnsafeToCopyNotTakenInField(1, "Pair", "Holds", "(int, Text<Handle>)", "Text<X>", "Handle", "X").AsError());

    // Only naming the type runs none of its code: a field, a parameter, a local, an array, 'typeof', 'default' and a field read.
    [TestMethod]
    public Task OnlyNamingTheType_IsNotReported() => Verify.UpdatedRulesAsync(
        $$"""
        using System.Collections.Generic;
        using Singulink.CodeAnalysis;

        [NonCopyable]
        public struct Text<[NonCopyable(UnsafeToCopy = false)] X>
        {
            public int Length;
        }

        class C
        {
            List<Text<Handle>> _list = null!;
            Text<Handle>[] _array = null!;

            int M(List<Text<Handle>> list, ref Text<Handle> text)
            {
                Text<Handle> local = default;
                var type = typeof(Dictionary<int, Text<Handle>>);
                return local.Length + text.Length;
            }
        }

        {{Types}}
        """);
}
