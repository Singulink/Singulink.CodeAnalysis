using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;
using PrefixClassName.MsTest;

namespace Singulink.CodeAnalysis.Tests;

/// <summary>
/// Tests of which attributes the analyzer recognizes: the five by their full name, in the namespace Singulink.CodeAnalysis of any
/// assembly, and nothing of the same name anywhere else.
/// </summary>
/// <remarks>
/// Docs: articles/guides/marking-a-struct.md.
/// </remarks>
[PrefixTestClass]
public class AttributeTests
{
    private const string Uses = """
        class C
        {
            Guard _field;

            void M()
            {
                var local = new Guard();
                var copy {|#0:= local|};
                Take({|#1:_field|});

                // The variable is used again, so the read above is not its last use.
                var last = local;
            }

            void Take(Guard guard) { }
        }
        """;

    // The same with nothing marked, for a struct that is not non-copyable.
    private const string UsesOfACopyableStruct = """
        class C
        {
            Guard _field;

            void M()
            {
                var local = new Guard();
                var copy = local;
                Take(_field);
                var last = local;
            }

            void Take(Guard guard) { }
        }
        """;

    // A library that declares the five attributes itself and uses each of them. '{0}' is the namespace of the attributes, and '{1}'
    // that of 'NonCopyableAttribute'.
    // A library that was built with the analyzer, so its marks on type parameters, [MovedFrom] and [NotNullRef] are trusted.
    private const string LibraryWithItsOwnAttributes = """
        using System;
        using System.Collections.Generic;
        using {0};
        using {1};

        [assembly: System.Reflection.AssemblyMetadata("Singulink.CodeAnalysis.Analyzed", "1")]

        namespace {1}
        {
            [AttributeUsage(AttributeTargets.Struct | AttributeTargets.GenericParameter, Inherited = false)]
            internal sealed class NonCopyableAttribute : Attribute;
        }

        namespace {0}
        {
            [AttributeUsage(AttributeTargets.Method | AttributeTargets.Constructor | AttributeTargets.Property | AttributeTargets.Event, Inherited = false)]
            internal sealed class RequiresCopyableAttribute : Attribute;

            [AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
            internal sealed class MovedFromAttribute : Attribute;

            [AttributeUsage(AttributeTargets.ReturnValue, Inherited = false)]
            internal sealed class BorrowedAttribute : Attribute;

            [AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
            internal sealed class KeptAttribute : Attribute;
        }

        [NonCopyable]
        public struct Guard
        {
            public int Value;
        }

        public sealed class Pool<[NonCopyable] T>
        {
            private readonly T[] _items = new T[4];

            [RequiresCopyable]
            public T[] ToArray() => {|#10:(T[]){|#11:_items.Clone()|}|};
        }

        public sealed class Holder
        {
            private static List<Guard>? _all;
            private readonly List<Guard> _guards = new();

            [return: Borrowed]
            public List<Guard> GetGuards() => {|#12:_guards|};

            public static void Register([Kept] List<Guard> list) => _all = {|#13:list|};

            public static Guard Take([MovedFrom] ref Guard value)
            {
                Guard taken {|#14:= value|};
                value = default;
                return taken;
            }
        }
        """;

    // What a project that uses the library sees of each mark.
    private const string UsesOfTheLibrary = """

        class User
        {
            Guard _field;

            void M(Pool<Guard> pool, Holder holder)
            {
                Guard copy {|#0:= _field|};
                Guard[] all = {|#1:pool.ToArray()|};
                Guard stored = {|#2:holder.GetGuards()[0]|};
                Guard taken = Holder.Take(ref _field);
            }

            System.Collections.Generic.List<Guard> Build()
            {
                var list = new System.Collections.Generic.List<Guard>();
                Holder.Register(list);
                return {|#3:list|};
            }
        }
        """;

    // A library whose attributes are conditional, as a referenced assembly. Its own project is told so where they are declared.
    private const string LibraryWithConditionalAttributes = """
        using System;
        using System.Diagnostics;

        namespace Singulink.CodeAnalysis
        {
            [Conditional("MARKS"), AttributeUsage(AttributeTargets.Struct | AttributeTargets.GenericParameter)]
            public sealed class NonCopyableAttribute : Attribute;

            [Conditional("MARKS")]
            public sealed class RequiresCopyableAttribute : Attribute;

            [Conditional("MARKS"), Conditional("OTHER_MARKS")]
            public sealed class MovedFromAttribute : Attribute;

            [Conditional("MARKS")]
            public sealed class BorrowedAttribute : Attribute;

            [Conditional("MARKS")]
            public sealed class KeptAttribute : Attribute;
        }
        """;

    private const string UsesOfConditionalAttributes = """
        using System.Collections.Generic;
        using Singulink.CodeAnalysis;

        [{|#0:NonCopyable|}]
        public struct Handle
        {
            public int Value;
        }

        public sealed class Pool<[{|#1:NonCopyable|}] T>
        {
            private readonly T[] _items = new T[4];

            [{|#2:RequiresCopyable|}]
            public T[] ToArray() => (T[])_items.Clone();
        }

        public sealed class Holder
        {
            private static List<Handle>? _all;
            private readonly List<Handle> _handles = new();
            private Handle _handle;

            [return: {|#3:Borrowed|}]
            public List<Handle> GetHandles() => _handles;

            public static void Register([{|#4:Kept|}] List<Handle> list) => _all = list;

            public static Handle Take([{|#5:MovedFrom|}] ref Handle value)
            {
                Handle taken = value;
                value = default;
                return taken;
            }

            // The analyzer honours the mark in this project all the same.
            void M()
            {
                var copy {|#6:= _handle|};
            }
        }
        """;

    // A library as they are published today: its guards are marked with an internal attribute of its own. '{0}' is the namespace.
    private const string PublishedLibrary = """
        namespace {0}
        {
            [System.AttributeUsage(System.AttributeTargets.Struct | System.AttributeTargets.GenericParameter)]
            internal sealed class {|#10:NonCopyableAttribute|} : System.Attribute;
        }

        [{0}.NonCopyable]
        public struct Guard
        {
            public int Value;
        }

        [{0}.NonCopyable]
        public struct Guard<T>
        {
            public T Value;
        }

        public sealed class Pool<[{0}.NonCopyable] T>
        {
            public T Get() => default!;
        }
        """;

    private const string UsesOfAPublishedLibrary = """
        class User
        {
            Guard _field;
            Guard<int> _generic;

            void M(Pool<Guard> pool, Pool<int> numbers)
            {
                Guard copy {|#0:= _field|};
                Guard<int> second {|#1:= _generic|};

                // The mark on the type parameter of the pool has no effect: the pool is an unmarked generic.
                Guard taken = {|#2:pool.Get()|};
                int number = numbers.Get();
            }
        }
        """;

    private const string Ours = "Singulink.CodeAnalysis";

    private static readonly DiagnosticResult[] s_marksOfAPublishedLibrary = [
        Verify.CopyByAssignment(0, "Guard"),
        Verify.CopyByAssignment(1, "Guard<int>"),
        Verify.UnlistedMember(2, "Pool<T>.Get()", "Guard", "T", "M:Pool`1.Get"),
    ];

    // The line is that of the call of 'Holder.Register' in the source of the test.
    private static DiagnosticResult[] GetMarksSeenByTheUser(int registeredOnLine) => [
        Verify.CopyByAssignment(0, "Guard"),
        Verify.RequiresCopyableMember(1, "Pool<T>.ToArray()", "Guard", "T"),
        Verify.ReadFromStoredContainer(2, "'List<T>.this[int]'", "Guard", canBeMoved: false),
        Verify.UnownedContainerReturned(
            3,
            "User.Build()",
            "System.Collections.Generic.List<Guard>",
            because: $"'list' is not the method's own anywhere in the method, because on line {registeredOnLine} it is passed to 'Holder.Register(List<Guard>)', which keeps it"),
    ];

    [TestMethod]
    public Task PackageAttribute() => Verify.AnalyzerAsync(
        $$"""
        using Singulink.CodeAnalysis;

        {{Uses}}

        [NonCopyable]
        struct Guard { }
        """,
        Verify.CopyByAssignment(0, "Guard"),
        Verify.CopyOfStoredValue(1, "'C.Take(Guard)' receives", "Guard"));

    [TestMethod]
    public Task PackageAttributeWithFullName() => Verify.AnalyzerAsync(
        $$"""
        {{Uses}}

        [Singulink.CodeAnalysis.NonCopyableAttribute]
        struct Guard { }
        """,
        Verify.CopyByAssignment(0, "Guard"),
        Verify.CopyOfStoredValue(1, "'C.Take(Guard)' receives", "Guard"));

    // A project may declare the attributes itself, internal ones included, and take no dependency: each of the five is recognised by
    // its full name. The attributes package is not referenced here.
    [TestMethod]
    public Task Attributes_DeclaredInTheNamespaceInTheSameProjectAreRecognised()
    {
        var test = new Verify.Test { TestCode = LibraryWithItsOwnAttributes.Replace("{0}", Ours).Replace("{1}", Ours) + UsesOfTheLibrary };
        test.TestState.AdditionalReferences.Clear();
        test.ExpectedDiagnostics.AddRange(GetMarksSeenByTheUser(75));

        return test.RunAsync();
    }

    [TestMethod]
    public Task Attributes_DeclaredInTheNamespaceInAReferencedProjectAreRecognised()
    {
        var test = new Verify.Test { TestCode = UsesOfTheLibrary };
        test.TestState.AdditionalReferences.Clear();
        test.TestState.AdditionalProjects["Library"].Sources.Add(LibraryWithItsOwnAttributes.Replace("{0}", Ours).Replace("{1}", Ours));
        test.TestState.AdditionalProjectReferences.Add("Library");
        test.ExpectedDiagnostics.AddRange(GetMarksSeenByTheUser(17));

        return test.RunAsync();
    }

    [TestMethod]
    public async Task Attributes_DeclaredInTheNamespaceInAReferencedAssemblyAreRecognised()
    {
        var test = new Verify.Test { TestCode = UsesOfTheLibrary };
        test.TestState.AdditionalReferences.Clear();
        test.TestState.AdditionalReferences.Add(await CompileLibraryAsync(test.ReferenceAssemblies, LibraryWithItsOwnAttributes.Replace("{0}", Ours).Replace("{1}", Ours)));
        test.ExpectedDiagnostics.AddRange(GetMarksSeenByTheUser(17));

        await test.RunAsync();
    }

    // A class named NonCopyableAttribute in any other namespace is not the analyzer's: the struct that carries it is copyable. That
    // fails without a sign, so the class is reported where it is declared.
    [TestMethod]
    [DataRow("Microsoft.CodeAnalysis", "internal")]
    [DataRow("FulcrumFS.Utilities", "internal")]
    [DataRow("Some.Other.Namespace", "public")]
    public Task NonCopyableAttribute_InAnotherNamespaceIsReportedAndHasNoEffect(string attributeNamespace, string accessibility) => Verify.AnalyzerAsync(
        $$"""
        using {{attributeNamespace}};

        {{UsesOfACopyableStruct}}

        [NonCopyable]
        struct Guard { }

        namespace {{attributeNamespace}}
        {
            [System.AttributeUsage(System.AttributeTargets.Struct)]
            {{accessibility}} sealed class {|#0:NonCopyableAttribute|} : System.Attribute;
        }
        """,
        Verify.ForeignNonCopyableAttribute(0, $"{attributeNamespace}.NonCopyableAttribute"));

    [TestMethod]
    public Task NonCopyableAttribute_InTheGlobalNamespaceIsReportedAndHasNoEffect() => Verify.AnalyzerAsync(
        $$"""
        {{UsesOfACopyableStruct}}

        [NonCopyable]
        struct Guard { }

        internal sealed class {|#0:NonCopyableAttribute|} : System.Attribute;

        // A nested class of the name is in no namespace of its own either, and a class of the name in a namespace below the one that is
        // recognised is not in it.
        static class Marks
        {
            internal sealed class {|#1:NonCopyableAttribute|} : System.Attribute;
        }

        namespace Singulink.CodeAnalysis.Internal
        {
            internal sealed class {|#2:NonCopyableAttribute|} : System.Attribute;
        }

        // So is a generic one, and one that derives from System.Attribute through another class.
        namespace Generic
        {
            internal sealed class {|#3:NonCopyableAttribute|}<T> : System.Attribute;
        }

        namespace Derived
        {
            internal abstract class MarkAttribute : System.Attribute;

            internal sealed class {|#4:NonCopyableAttribute|} : MarkAttribute;
        }

        // A class of the name that is no attribute marks nothing, and is not reported.
        namespace NoAttribute
        {
            internal sealed class NonCopyableAttribute;

            internal static class Holder
            {
                internal sealed class NonCopyableAttribute : System.Exception;
            }
        }
        """,
        Verify.ForeignNonCopyableAttribute(0, "NonCopyableAttribute"),
        Verify.ForeignNonCopyableAttribute(1, "Marks.NonCopyableAttribute"),
        Verify.ForeignNonCopyableAttribute(2, "Singulink.CodeAnalysis.Internal.NonCopyableAttribute"),
        Verify.ForeignNonCopyableAttribute(3, "Generic.NonCopyableAttribute<T>"),
        Verify.ForeignNonCopyableAttribute(4, "Derived.NonCopyableAttribute"));

    // On a struct of another assembly a NonCopyableAttribute of any namespace is recognised: libraries that are already published mark
    // their guards with an attribute of their own, and a project that uses such a library cannot change it. A mark on a type parameter
    // is another matter: it says that the body of the generic was checked by this analyzer, which a foreign attribute cannot say.
    [TestMethod]
    [DataRow("Microsoft.CodeAnalysis")]
    [DataRow("Some.Library.Internal")]
    public async Task NonCopyableAttribute_OfAnotherNamespaceIsRecognisedOnAStructOfAReferencedAssembly(string attributeNamespace)
    {
        var test = new Verify.Test { TestCode = UsesOfAPublishedLibrary };
        test.TestState.AdditionalReferences.Clear();
        test.TestState.AdditionalReferences.Add(await CompileLibraryAsync(test.ReferenceAssemblies, PublishedLibrary.Replace("{0}", attributeNamespace)));
        test.ExpectedDiagnostics.AddRange(s_marksOfAPublishedLibrary);

        await test.RunAsync();
    }

    // A referenced project is such an assembly. In its own compilation the struct is copyable, and the attribute is reported there.
    [TestMethod]
    [DataRow("Microsoft.CodeAnalysis")]
    [DataRow("Some.Library.Internal")]
    public Task NonCopyableAttribute_OfAnotherNamespaceIsRecognisedOnAStructOfAReferencedProject(string attributeNamespace)
    {
        var test = new Verify.Test { TestCode = UsesOfAPublishedLibrary };
        test.TestState.AdditionalReferences.Clear();
        test.TestState.AdditionalProjects["Library"].Sources.Add(PublishedLibrary.Replace("{0}", attributeNamespace));
        test.TestState.AdditionalProjectReferences.Add("Library");
        test.ExpectedDiagnostics.AddRange(s_marksOfAPublishedLibrary);
        test.ExpectedDiagnostics.Add(Verify.ForeignNonCopyableAttribute(10, $"{attributeNamespace}.NonCopyableAttribute"));

        return test.RunAsync();
    }

    // A project that marks its own struct or type parameter with such an attribute out of a referenced assembly has no declaration of
    // its own that could be reported: the use is the only place. The mark has no effect.
    [TestMethod]
    public async Task NonCopyableAttribute_OfAReferencedAssemblyInAnotherNamespaceIsReportedWhereItIsUsed()
    {
        const string Library = """
            namespace Other.Tool
            {
                [System.AttributeUsage(System.AttributeTargets.Struct | System.AttributeTargets.GenericParameter)]
                public sealed class NonCopyableAttribute : System.Attribute;

                [System.AttributeUsage(System.AttributeTargets.Parameter)]
                public sealed class KeptAttribute : System.Attribute;
            }
            """;

        var test = new Verify.Test {
            TestCode = """
                using Other.Tool;

                [{|#0:NonCopyable|}]
                struct Handle
                {
                    public int Value;
                }

                sealed class Pool<[{|#1:NonCopyable|}] T>
                {
                    private T _item = default!;

                    public T Get() => _item;
                }

                static class Moves
                {
                    public static T Pass<[{|#2:Other.Tool.NonCopyable|}] T>(T value) => value;

                    static void WithALocalFunction()
                    {
                        Inner<int>(1);

                        static U Inner<[{|#3:NonCopyableAttribute|}] U>(U value) => value;
                    }

                    // An attribute of one of the other four names is not reported, here or anywhere.
                    static void Keeps([Kept] object value) { }
                }

                class C
                {
                    Handle _handle;

                    void M()
                    {
                        var copy = _handle;
                        var again = _handle;
                    }
                }
                """,
        };

        test.TestState.AdditionalReferences.Clear();
        test.TestState.AdditionalReferences.Add(await CompileLibraryAsync(test.ReferenceAssemblies, Library));

        test.ExpectedDiagnostics.AddRange([
            Verify.ForeignNonCopyableAttributeUse(0, "Other.Tool.NonCopyableAttribute", "Library", "struct 'Handle' is copied with no warning"),
            Verify.ForeignNonCopyableAttributeUse(1, "Other.Tool.NonCopyableAttribute", "Library", "type parameter 'T' counts as not marked"),
            Verify.ForeignNonCopyableAttributeUse(2, "Other.Tool.NonCopyableAttribute", "Library", "type parameter 'T' counts as not marked"),
            Verify.ForeignNonCopyableAttributeUse(3, "Other.Tool.NonCopyableAttribute", "Library", "type parameter 'U' counts as not marked"),
        ]);

        await test.RunAsync();
    }

    // An attribute named like one of the other four in another namespace belongs to something else. It has no effect, and it is
    // not reported: what it would have allowed is reported instead, and what it would have reported is not.
    [TestMethod]
    public Task OtherAttributes_InAnotherNamespaceHaveNoEffectAndAreNotReported()
    {
        var test = new Verify.Test { TestCode = LibraryWithItsOwnAttributes.Replace("{0}", "Other.Tool").Replace("{1}", Ours) + UsesOfTheLibrary };
        test.TestState.AdditionalReferences.Clear();

        // The user: the struct is non-copyable, and none of the other marks is seen. The library: what each of the four marks allowed
        // is reported.
        test.ExpectedDiagnostics.AddRange([
            Verify.CopyByAssignment(0, "Guard"),
            Verify.UnownedContainerReturned(10, "Pool<T>.ToArray()", "T[]"),
            Verify.UntypedCollectionMember(11, "T[]", "System.Array", "Array.Clone()", "M:System.Array.Clone"),
            Verify.UnownedContainerReturned(12, "Holder.GetGuards()", "System.Collections.Generic.List<Guard>"),
            Verify.KeepsUnmarkedParameter(13, "Holder.Register(List<Guard>)", "list", "System.Collections.Generic.List<Guard>"),
            Verify.CopyByAssignment(14, "Guard"),
        ]);

        return test.RunAsync();
    }

    [TestMethod]
    public Task GenericStruct() => Verify.AnalyzerAsync(
        """
        using Singulink.CodeAnalysis;

        class C
        {
            void M()
            {
                var local = new Guard<int>();
                var copy {|#0:= local|};
                var last = local;
            }
        }

        [NonCopyable]
        struct Guard<T> { }
        """,
        Verify.CopyByAssignment(0, "Guard<int>"));

    [TestMethod]
    public Task UnrelatedAttribute() => Verify.AnalyzerAsync(
        """
        class C
        {
            Guard _field;

            void M()
            {
                var local = new Guard();
                var copy = local;
                Take(_field);
            }

            void Take(Guard guard) { }
        }

        [System.Serializable]
        [NonCopyableish]
        [NonCopyable]
        struct Guard { }

        sealed class NonCopyableishAttribute : System.Attribute;

        // An attribute class named NonCopyable, with no suffix, is not recognized either, and '[NonCopyable]' finds it just the
        // same: it is reported like one named NonCopyableAttribute in another namespace.
        sealed class {|#0:NonCopyable|} : System.Attribute;
        """,
        Verify.NonCopyableAttributeWithNoSuffix(0, "NonCopyable"));

    [TestMethod]
    public Task ClassIsNotNonCopyable() => Verify.AnalyzerAsync("""
        using Singulink.CodeAnalysis;

        class C
        {
            void M()
            {
                var local = new Guard();
                var copy = local;
            }
        }

        [{|CS0592:NonCopyable|}]
        class Guard { }
        """);

    // A project may declare the attributes itself. A conditional one is seen in its own source and left out of its assembly.
    [TestMethod]
    public Task ConditionalMarker_IsReportedWhereItIsDeclared()
    {
        var test = new Verify.Test {
            TestCode = """
                using System;
                using System.Diagnostics;

                namespace Singulink.CodeAnalysis
                {
                    [{|#0:Conditional("CODE_ANALYSIS")|}, AttributeUsage(AttributeTargets.Struct | AttributeTargets.GenericParameter)]
                    internal sealed class NonCopyableAttribute : Attribute;

                    [AttributeUsage(AttributeTargets.ReturnValue), {|#1:Conditional("MARKS")|}, Conditional("OTHER")]
                    internal sealed class BorrowedAttribute : Attribute;

                    [{|#2:Conditional("MARKS")|}]
                    internal sealed class KeptAttribute : Attribute;

                    [{|#3:Conditional("MARKS")|}]
                    internal sealed class MovedFromAttribute : Attribute;

                    [{|#4:Conditional("MARKS")|}]
                    internal sealed class RequiresCopyableAttribute : Attribute;

                    // An attribute of another name is none of the analyzer's.
                    [Conditional("MARKS")]
                    internal sealed class OwnedAttribute : Attribute;

                    [NonCopyable]
                    public struct Handle
                    {
                        public int Value;
                    }

                    class C
                    {
                        Handle _handle;

                        // The mark is seen in the source of the project that declares the attribute, whether its symbol is defined or not.
                        void M()
                        {
                            var copy {|#5:= _handle|};
                        }
                    }
                }

                // Nor is an attribute of one of the names in another namespace: a conditional one there is not the analyzer's business.
                namespace Other.Tool
                {
                    [Conditional("MARKS")]
                    internal sealed class KeptAttribute : Attribute;
                }
                """,
        };

        test.TestState.AdditionalReferences.Clear();

        test.ExpectedDiagnostics.AddRange([
            Verify.ConditionalMarker(0, "NonCopyableAttribute"),
            Verify.ConditionalMarker(1, "BorrowedAttribute"),
            Verify.ConditionalMarker(2, "KeptAttribute"),
            Verify.ConditionalMarker(3, "MovedFromAttribute"),
            Verify.ConditionalMarker(4, "RequiresCopyableAttribute"),
            Verify.CopyByAssignment(5, "Singulink.CodeAnalysis.Handle"),
        ]);

        return test.RunAsync();
    }

    // The project that uses such an attribute is told at every use that the compiler drops: the mark holds here and is gone from the
    // assembly that is being built.
    [TestMethod]
    public async Task ConditionalMarker_OfAReferencedAssemblyIsReportedAtEveryUseThatIsDropped()
    {
        var test = new Verify.Test { TestCode = UsesOfConditionalAttributes };
        test.TestState.AdditionalReferences.Clear();
        test.TestState.AdditionalReferences.Add(await CompileLibraryAsync(test.ReferenceAssemblies, LibraryWithConditionalAttributes));

        test.ExpectedDiagnostics.AddRange([
            Verify.ConditionalMarkerUse(0, "NonCopyableAttribute", "Library", "MARKS"),
            Verify.ConditionalMarkerUse(1, "NonCopyableAttribute", "Library", "MARKS"),
            Verify.ConditionalMarkerUse(2, "RequiresCopyableAttribute", "Library", "MARKS"),
            Verify.ConditionalMarkerUse(3, "BorrowedAttribute", "Library", "MARKS"),
            Verify.ConditionalMarkerUse(4, "KeptAttribute", "Library", "MARKS"),
            Verify.ConditionalMarkerUse(5, "MovedFromAttribute", "Library", "MARKS' or 'OTHER_MARKS"),
            Verify.CopyByAssignment(6, "Handle"),
        ]);

        await test.RunAsync();
    }

    // Where the symbol is defined the compiler writes the use, and nothing is lost: by a '#define' of the file, or by the project.
    [TestMethod]
    [DataRow("#define MARKS", null)]
    [DataRow("#define OTHER\n#define MARKS", null)]
    [DataRow("", "MARKS")]
    public async Task ConditionalMarker_OfAReferencedAssemblyIsNotReportedWhereItsSymbolIsDefined(string directives, string? projectSymbol)
    {
        var test = new Verify.Test { TestCode = directives + "\n" + UsesOfConditionalAttributes, PreprocessorSymbols = projectSymbol is null ? [] : [projectSymbol] };
        test.TestState.AdditionalReferences.Clear();
        test.TestState.AdditionalReferences.Add(await CompileLibraryAsync(test.ReferenceAssemblies, LibraryWithConditionalAttributes));
        test.ExpectedDiagnostics.Add(Verify.CopyByAssignment(6, "Handle"));

        await test.RunAsync();
    }

    // A symbol that is taken back before the use, or defined for one of two symbols only, is judged by what holds at the use.
    [TestMethod]
    public async Task ConditionalMarker_OfAReferencedAssemblyIsJudgedByTheSymbolsAtTheUse()
    {
        var test = new Verify.Test { TestCode = "#define MARKS\n#define OTHER_MARKS\n#undef MARKS\n" + UsesOfConditionalAttributes };
        test.TestState.AdditionalReferences.Clear();
        test.TestState.AdditionalReferences.Add(await CompileLibraryAsync(test.ReferenceAssemblies, LibraryWithConditionalAttributes));

        // OTHER_MARKS is still defined, and [MovedFrom] depends on either.
        test.ExpectedDiagnostics.AddRange([
            Verify.ConditionalMarkerUse(0, "NonCopyableAttribute", "Library", "MARKS"),
            Verify.ConditionalMarkerUse(1, "NonCopyableAttribute", "Library", "MARKS"),
            Verify.ConditionalMarkerUse(2, "RequiresCopyableAttribute", "Library", "MARKS"),
            Verify.ConditionalMarkerUse(3, "BorrowedAttribute", "Library", "MARKS"),
            Verify.ConditionalMarkerUse(4, "KeptAttribute", "Library", "MARKS"),
            Verify.CopyByAssignment(6, "Handle"),
        ]);

        await test.RunAsync();
    }

    // ConditionalAttribute is inherited: a marker that derives from a conditional class is dropped from the assembly just the same.
    [TestMethod]
    public Task ConditionalMarker_ThroughABaseClassIsReportedWhereItIsDeclared()
    {
        var test = new Verify.Test
        {
            TestCode = """
                using System;
                using System.Diagnostics;

                namespace Lib.Annotations
                {
                    [Conditional("LIB_ANNOTATIONS")]
                    public abstract class MarkerAttribute : Attribute;

                    public abstract class DerivedMarkerAttribute : MarkerAttribute;

                    public abstract class PlainMarkerAttribute : Attribute;
                }

                namespace Singulink.CodeAnalysis
                {
                    [AttributeUsage(AttributeTargets.Struct | AttributeTargets.GenericParameter, Inherited = false)]
                    internal sealed class {|#0:NonCopyableAttribute|} : Lib.Annotations.MarkerAttribute;

                    internal sealed class {|#1:KeptAttribute|} : Lib.Annotations.DerivedMarkerAttribute;

                    // A base class that is not conditional is fine.
                    internal sealed class BorrowedAttribute : Lib.Annotations.PlainMarkerAttribute;

                    [NonCopyable]
                    public struct Handle
                    {
                        public int Value;
                    }

                    class C
                    {
                        Handle _handle;

                        void M()
                        {
                            var copy {|#2:= _handle|};
                        }
                    }
                }
                """,
        };

        test.TestState.AdditionalReferences.Clear();

        test.ExpectedDiagnostics.AddRange([
            Verify.ConditionalMarkerBase(0, "NonCopyableAttribute", "Lib.Annotations.MarkerAttribute"),
            Verify.ConditionalMarkerBase(1, "KeptAttribute", "Lib.Annotations.MarkerAttribute"),
            Verify.CopyByAssignment(2, "Singulink.CodeAnalysis.Handle"),
        ]);

        return test.RunAsync();
    }

    // And a use of one that a referenced assembly declares is reported like a use of an attribute that is conditional itself.
    [TestMethod]
    public async Task ConditionalMarker_ThroughABaseClassOfAReferencedAssemblyIsReportedAtEveryUseThatIsDropped()
    {
        const string Library = """
            using System;
            using System.Diagnostics;

            namespace Lib.Annotations
            {
                [Conditional("ANNOTATIONS")]
                public abstract class MarkerAttribute : Attribute;
            }

            namespace Singulink.CodeAnalysis
            {
                [AttributeUsage(AttributeTargets.Struct | AttributeTargets.GenericParameter)]
                public sealed class NonCopyableAttribute : Lib.Annotations.MarkerAttribute;

                [Conditional("KEPT")]
                public sealed class KeptAttribute : Lib.Annotations.MarkerAttribute;
            }
            """;

        const string Uses = """
            using System.Collections.Generic;
            using Singulink.CodeAnalysis;

            [{|#0:NonCopyable|}]
            public struct Handle
            {
                public int Value;
            }

            public sealed class Holder
            {
                private static List<Handle>? _all;
                private Handle _handle;

                public static void Register([{|#1:Kept|}] List<Handle> list) => _all = list;

                void M()
                {
                    var copy {|#2:= _handle|};
                }
            }
            """;

        var test = new Verify.Test { TestCode = Uses };
        test.TestState.AdditionalReferences.Clear();
        test.TestState.AdditionalReferences.Add(await CompileLibraryAsync(test.ReferenceAssemblies, Library));

        test.ExpectedDiagnostics.AddRange([
            Verify.ConditionalMarkerUse(0, "NonCopyableAttribute", "Library", "ANNOTATIONS"),
            Verify.ConditionalMarkerUse(1, "KeptAttribute", "Library", "KEPT' or 'ANNOTATIONS"),
            Verify.CopyByAssignment(2, "Handle"),
        ]);

        await test.RunAsync();

        // With the symbol of the base class defined the compiler writes both uses.
        var defined = new Verify.Test { TestCode = Uses, PreprocessorSymbols = ["ANNOTATIONS"] };
        defined.TestState.AdditionalReferences.Clear();
        defined.TestState.AdditionalReferences.Add(await CompileLibraryAsync(defined.ReferenceAssemblies, Library));
        defined.ExpectedDiagnostics.Add(Verify.CopyByAssignment(2, "Handle"));

        await defined.RunAsync();
    }

    // A class that derives from the recognised NonCopyableAttribute marks like it: on a struct and on a type parameter. A class that
    // derives from one of the other four is not recognised, which means more findings and not fewer.
    [TestMethod]
    public Task DerivedNonCopyableAttribute_MarksLikeTheAttribute()
    {
        var test = new Verify.Test
        {
            TestCode = """
                using System;
                using System.Collections.Generic;

                namespace Singulink.CodeAnalysis
                {
                    [AttributeUsage(AttributeTargets.Struct | AttributeTargets.GenericParameter, Inherited = false)]
                    internal class NonCopyableAttribute : Attribute;

                    [AttributeUsage(AttributeTargets.Parameter)]
                    internal class KeptAttribute : Attribute;
                }

                namespace App
                {
                    internal class LockGuardAttribute : Singulink.CodeAnalysis.NonCopyableAttribute;

                    internal sealed class ReadGuardAttribute : LockGuardAttribute;

                    internal sealed class HeldAttribute : Singulink.CodeAnalysis.KeptAttribute;

                    [LockGuard]
                    public struct Guard
                    {
                        public int Value;
                    }

                    [ReadGuard]
                    public struct ReadGuard
                    {
                        public int Value;
                    }

                    sealed class Pool<[LockGuard] T>
                    {
                        private T _item = default!;

                        public T Peek() => {|#0:_item|};

                        public ref T Slot => ref _item;
                    }

                    class C
                    {
                        static List<Guard>? s_all;

                        Guard _guard;
                        ReadGuard _read;

                        void M(Pool<Guard> pool)
                        {
                            var copy {|#1:= _guard|};
                            var second {|#2:= _read|};

                            // The type parameter is marked, so the pool is checked code and may hold a guard.
                            ref Guard slot = ref pool.Slot;
                        }

                        // The derived mark of another attribute is no mark: the method keeps a parameter that is not marked.
                        static void Register([Held] List<Guard> list) => s_all = {|#3:list|};
                    }
                }
                """,
        };

        test.TestState.AdditionalReferences.Clear();

        test.ExpectedDiagnostics.AddRange([
            Verify.CopyByReturn(0, "T"),
            Verify.CopyByAssignment(1, "App.Guard"),
            Verify.CopyByAssignment(2, "App.ReadGuard"),
            Verify.KeepsUnmarkedParameter(3, "C.Register(List<Guard>)", "list", "System.Collections.Generic.List<App.Guard>"),
        ]);

        return test.RunAsync();
    }

    // On a struct of a referenced assembly a base class of that name in any namespace counts, as the attribute itself does there.
    [TestMethod]
    [DataRow("Singulink.CodeAnalysis")]
    [DataRow("Microsoft.CodeAnalysis")]
    public async Task DerivedNonCopyableAttribute_IsRecognisedOnAStructOfAReferencedAssembly(string attributeNamespace)
    {
        const string Library = """
            namespace {0}
            {
                [System.AttributeUsage(System.AttributeTargets.Struct | System.AttributeTargets.GenericParameter)]
                internal class NonCopyableAttribute : System.Attribute;
            }

            namespace Lib
            {
                internal sealed class LockGuardAttribute : {0}.NonCopyableAttribute;
            }

            [Lib.LockGuard]
            public struct Guard
            {
                public int Value;
            }
            """;

        var test = new Verify.Test
        {
            TestCode = """
                class User
                {
                    Guard _field;

                    void M()
                    {
                        Guard copy {|#0:= _field|};
                    }
                }
                """,
        };

        test.TestState.AdditionalReferences.Clear();
        test.TestState.AdditionalReferences.Add(await CompileLibraryAsync(test.ReferenceAssemblies, Library.Replace("{0}", attributeNamespace)));
        test.ExpectedDiagnostics.Add(Verify.CopyByAssignment(0, "Guard"));

        await test.RunAsync();
    }

    // A class that derives from a NonCopyableAttribute of another namespace marks nothing in the project's own code, like that
    // attribute. Where a referenced assembly declares the attribute, the use is the only place to say so.
    [TestMethod]
    public async Task DerivedNonCopyableAttribute_OfAForeignAttributeIsReportedWhereItIsUsed()
    {
        const string Library = """
            namespace Other.Tool
            {
                [System.AttributeUsage(System.AttributeTargets.Struct | System.AttributeTargets.GenericParameter)]
                public class NonCopyableAttribute : System.Attribute;

                public sealed class TokenAttribute : NonCopyableAttribute;
            }
            """;

        var test = new Verify.Test
        {
            TestCode = """
                using Other.Tool;

                sealed class HandleAttribute : NonCopyableAttribute;

                [{|#0:Token|}]
                struct Token
                {
                    public int Value;
                }

                [{|#1:Handle|}]
                struct Handle
                {
                    public int Value;
                }

                class C
                {
                    Token _token;
                    Handle _handle;

                    void M()
                    {
                        var copy = _token;
                        var second = _handle;
                    }
                }
                """,
        };

        test.TestState.AdditionalReferences.Clear();
        test.TestState.AdditionalReferences.Add(await CompileLibraryAsync(test.ReferenceAssemblies, Library));

        test.ExpectedDiagnostics.AddRange([
            Verify.ForeignNonCopyableAttributeUse(0, "Other.Tool.NonCopyableAttribute", "Library", "struct 'Token' is copied with no warning"),
            Verify.ForeignNonCopyableAttributeUse(1, "Other.Tool.NonCopyableAttribute", "Library", "struct 'Handle' is copied with no warning"),
        ]);

        await test.RunAsync();
    }

    // A class that derives from the attribute is lost like it if it is conditional, itself or through a base class.
    [TestMethod]
    public Task DerivedNonCopyableAttribute_ThatIsConditionalIsReportedWhereItIsDeclared()
    {
        var test = new Verify.Test
        {
            TestCode = """
                using System;
                using System.Diagnostics;

                namespace Singulink.CodeAnalysis
                {
                    [AttributeUsage(AttributeTargets.Struct | AttributeTargets.GenericParameter, Inherited = false)]
                    internal class NonCopyableAttribute : Attribute;
                }

                namespace App
                {
                    [{|#0:Conditional("GUARDS")|}]
                    internal class LockGuardAttribute : Singulink.CodeAnalysis.NonCopyableAttribute;

                    internal sealed class {|#1:ReadGuardAttribute|} : LockGuardAttribute;
                }
                """,
        };

        test.TestState.AdditionalReferences.Clear();

        test.ExpectedDiagnostics.AddRange([
            Verify.ConditionalMarker(0, "LockGuardAttribute"),
            Verify.ConditionalMarkerBase(1, "ReadGuardAttribute", "App.LockGuardAttribute"),
        ]);

        return test.RunAsync();
    }

    // A class of a referenced assembly that derives from the attribute and is conditional has no name by which it could be listed.
    // The project that marks its own struct or type parameter with it is told at the mark that the compiler drops.
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task DerivedNonCopyableAttribute_ThatIsConditionalInAReferencedAssemblyIsReportedWhereItIsUsed(bool isDefined)
    {
        const string Library = """
            using System;
            using System.Diagnostics;

            namespace Singulink.CodeAnalysis
            {
                [AttributeUsage(AttributeTargets.Struct | AttributeTargets.GenericParameter)]
                public class NonCopyableAttribute : Attribute;
            }

            namespace Lib
            {
                [Conditional("GUARDS")]
                public class LockGuardAttribute : Singulink.CodeAnalysis.NonCopyableAttribute;

                public sealed class ReadGuardAttribute : LockGuardAttribute;

                public sealed class PlainGuardAttribute : Singulink.CodeAnalysis.NonCopyableAttribute;
            }
            """;

        var test = new Verify.Test
        {
            TestCode = """
                using Lib;

                [{|#0:LockGuard|}]
                public struct Handle
                {
                    public int Value;
                }

                [{|#1:ReadGuard|}]
                public struct ReadHandle
                {
                    public int Value;
                }

                [PlainGuard]
                public struct PlainHandle
                {
                    public int Value;
                }

                public sealed class Pool<[{|#2:LockGuard|}] T>
                {
                    private T _item = default!;

                    public ref T Slot => ref _item;
                }

                class C
                {
                    Handle _handle;

                    // The analyzer honours the mark in this project all the same.
                    void M()
                    {
                        var copy {|#3:= _handle|};
                    }
                }
                """,
            PreprocessorSymbols = isDefined ? ["GUARDS"] : [],
        };

        test.TestState.AdditionalReferences.Clear();
        test.TestState.AdditionalReferences.Add(await CompileLibraryAsync(test.ReferenceAssemblies, Library));

        if (!isDefined)
        {
            test.ExpectedDiagnostics.AddRange([
                Verify.ConditionalMarkerUse(0, "LockGuardAttribute", "Library", "GUARDS"),
                Verify.ConditionalMarkerUse(1, "ReadGuardAttribute", "Library", "GUARDS"),
                Verify.ConditionalMarkerUse(2, "LockGuardAttribute", "Library", "GUARDS"),
            ]);
        }

        test.ExpectedDiagnostics.Add(Verify.CopyByAssignment(3, "Handle"));

        await test.RunAsync();
    }

    private static async Task<MetadataReference> CompileLibraryAsync(ReferenceAssemblies referenceAssemblies, string source)
    {
        var references = await referenceAssemblies.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);

        // The markup of the places where the library's own project has findings is not part of the source.
        source = System.Text.RegularExpressions.Regex.Replace(source, @"\{\|#\d+:|\|\}", string.Empty);

        var compilation = CSharpCompilation.Create(
            "Library",
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest))],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);

        if (!result.Success)
            throw new InvalidOperationException(string.Join(Environment.NewLine, result.Diagnostics));

        return MetadataReference.CreateFromImage(stream.ToArray());
    }

    // An attribute class named NonCopyable, with no suffix, is found by '[NonCopyable]' just the same and marks nothing. It is
    // reported where it is declared, in every namespace, the namespace of the attributes included. A class of the name that is no
    // attribute is the helper class, or anything else.
    [TestMethod]
    public Task NonCopyableAttributeWithNoSuffix_IsReportedWhereItIsDeclared()
    {
        var test = new Verify.Test {
            TestCode = """
                namespace Singulink.CodeAnalysis
                {
                    [System.AttributeUsage(System.AttributeTargets.Struct | System.AttributeTargets.GenericParameter)]
                    internal sealed class {|#0:NonCopyable|} : System.Attribute;
                }

                namespace Marks
                {
                    internal class {|#1:NonCopyable|} : System.Attribute;

                    // A class that derives from one is used like it.
                    internal sealed class LockGuardAttribute : NonCopyable;
                }

                namespace Helpers
                {
                    // No attribute.
                    internal static class NonCopyable
                    {
                        public static int Count;
                    }
                }

                namespace App
                {
                    using Singulink.CodeAnalysis;

                    [NonCopyable]
                    struct Guard
                    {
                        public int Value;
                    }

                    class C
                    {
                        Guard _guard;

                        // The struct is not marked: the copy is not reported, and the declaration of the class is.
                        void M()
                        {
                            var copy = _guard;
                            var again = _guard;
                        }
                    }
                }
                """,
        };

        // The project declares the attribute itself, with no reference to the attributes package.
        test.TestState.AdditionalReferences.Clear();
        test.ExpectedDiagnostics.Add(Verify.NonCopyableAttributeWithNoSuffix(0, "Singulink.CodeAnalysis.NonCopyable"));
        test.ExpectedDiagnostics.Add(Verify.NonCopyableAttributeWithNoSuffix(1, "Marks.NonCopyable"));
        return test.RunAsync();
    }

    // The same class in a referenced assembly cannot be reported where it is declared, so its use is: on a struct and on a type
    // parameter of this project. A struct of the referenced assembly that is marked with it counts as marked.
    [TestMethod]
    public async Task NonCopyableAttributeWithNoSuffix_OfAReferencedAssemblyIsReportedWhereItIsUsed()
    {
        const string Library = """
            namespace Other.Tool
            {
                [System.AttributeUsage(System.AttributeTargets.Struct | System.AttributeTargets.GenericParameter)]
                public sealed class NonCopyable : System.Attribute;

                [NonCopyable]
                public struct Token
                {
                    public int Value;
                }
            }
            """;

        var test = new Verify.Test {
            TestCode = """
                using Other.Tool;

                [{|#0:NonCopyable|}]
                struct Handle
                {
                    public int Value;
                }

                sealed class Pool<[{|#1:NonCopyable|}] T>
                {
                    private T _item = default!;

                    public T Get() => _item;
                }

                class C
                {
                    Handle _handle;
                    Token _token;

                    void M()
                    {
                        var copy = _handle;
                        var token {|#2:= _token|};
                    }
                }
                """,
        };

        test.TestState.AdditionalReferences.Clear();
        test.TestState.AdditionalReferences.Add(await CompileLibraryAsync(test.ReferenceAssemblies, Library));

        test.ExpectedDiagnostics.AddRange([
            Verify.ForeignNonCopyableAttributeUse(0, "Other.Tool.NonCopyable", "Library", "struct 'Handle' is copied with no warning"),
            Verify.ForeignNonCopyableAttributeUse(1, "Other.Tool.NonCopyable", "Library", "type parameter 'T' counts as not marked"),
            Verify.CopyByAssignment(2, "Other.Tool.Token"),
        ]);

        await test.RunAsync();
    }

    // [NotNullRef] is recognised like the other attributes: by its full name, in any assembly, public or internal. A project that
    // declares its own attributes declares this one too, and writes whatever check it likes, under any name. A class that derives
    // from the attribute is not recognised, and neither is a class of the name in another namespace.
    [TestMethod]
    public Task NotNullRef_IsRecognisedByItsFullNameLikeTheOthers()
    {
        var test = new Verify.Test
        {
            TestCode = """
                using System;
                using Singulink.CodeAnalysis;

                namespace Singulink.CodeAnalysis
                {
                    [AttributeUsage(AttributeTargets.Struct | AttributeTargets.GenericParameter, Inherited = false)]
                    internal sealed class NonCopyableAttribute : Attribute;

                    [AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
                    internal sealed class MovedFromAttribute : Attribute;

                    [AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
                    internal class NotNullRefAttribute : Attribute;
                }

                namespace Other
                {
                    internal sealed class NotNullRefAttribute : Attribute;

                    internal sealed class CheckedAttribute : Singulink.CodeAnalysis.NotNullRefAttribute;
                }

                [NonCopyable]
                struct Guard
                {
                }

                static class Ensure
                {
                    public static void RefersToSomething<T>([{|#0:NotNullRef|}] ref T reference)
                    {
                    }

                    public static void OfAnotherNamespace<T>([Other.NotNullRef] ref T reference)
                    {
                    }

                    public static void Derived<T>([Other.Checked] ref T reference)
                    {
                    }
                }

                static class Moves
                {
                    static void Hand([MovedFrom] ref Guard from, ref Guard to, ref int count)
                    {
                        Ensure.RefersToSomething(ref count);
                        to = from;
                        count++;
                        from = default;
                    }

                    static void HandWithAnotherAttribute([MovedFrom] ref Guard from, ref Guard to, ref int count)
                    {
                        Ensure.OfAnotherNamespace(ref count);
                        to = from;
                        {|#1:count|}++;
                        from = default;
                    }

                    static void HandWithADerivedAttribute([MovedFrom] ref Guard from, ref Guard to, ref int count)
                    {
                        Ensure.Derived(ref count);
                        to = from;
                        {|#2:count|}++;
                        from = default;
                    }
                }
                """,
        };

        test.TestState.AdditionalReferences.Clear();
        test.ExpectedDiagnostics.Add(Verify.NotNullRefNotChecked(0, "reference", "Ensure.RefersToSomething<T>(ref T)"));
        test.ExpectedDiagnostics.Add(Verify.MovedFromParameterNotAssignedAtOnceThroughReference(1, "from", "count"));
        test.ExpectedDiagnostics.Add(Verify.MovedFromParameterNotAssignedAtOnceThroughReference(2, "from", "count"));
        return test.RunAsync();
    }

    // And like the others it must not be declared [Conditional]: the compiler would leave the mark out of the assembly.
    [TestMethod]
    public Task NotNullRef_DeclaredConditional_IsReported()
    {
        var test = new Verify.Test
        {
            TestCode = """
                using System;
                using System.Diagnostics;

                namespace Singulink.CodeAnalysis
                {
                    [{|#0:Conditional("MARKS")|}]
                    [AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
                    internal sealed class NotNullRefAttribute : Attribute;
                }
                """,
        };

        test.TestState.AdditionalReferences.Clear();
        test.ExpectedDiagnostics.Add(Verify.ConditionalMarker(0, "NotNullRefAttribute"));
        return test.RunAsync();
    }
}
