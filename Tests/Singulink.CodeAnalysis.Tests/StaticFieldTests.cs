using Microsoft.CodeAnalysis.Testing;
using PrefixClassName.MsTest;

namespace Singulink.CodeAnalysis.Tests;

/// <summary>
/// Tests of a read or a write of a static field of a generic type, which runs the static constructor and the static field initializers of
/// the type with its type arguments: a use of the type's code, as a call of one of its members is, which an 'F:' line of a symbol list file
/// answers.
/// </summary>
/// <remarks>
/// Docs: articles/rules/symbol-list-format.md, articles/guides/symbol-list.md, articles/rules/framework-symbol-list.md and
/// articles/diagnostics/sca1020.md.
/// </remarks>
[PrefixTestClass]
public class StaticFieldTests
{
    private const string Cache = """
        public class Cache<T>
        {
            public static T A;
            public static T B = A;
            public static readonly int Count = 0;
            public const string Name = "cache";
            public T Instance;
        }

        public class Pair<TFirst, TSecond>
        {
            public static int Shared;
        }

        public class Outer<T>
        {
            public class Inner
            {
                public static int Shared;
            }
        }

        public class Plain
        {
            public static int Shared;
        }
        """;

    // The initializer of B copies the Guard of A, and nothing in the generic is reported, since its type parameter is not marked: the
    // read and the write of a static field are reported where the type is bound, as a call of a member of the generic is. A constant and
    // 'nameof' run nothing, and an instance field runs no code of its type.
    [TestMethod]
    public Task StaticField_OfAnUnmarkedGenericOfTheProjectIsAMemberUse() => Verify.AnalyzerAsync(
        $$"""
        using System.Collections.Generic;

        class C
        {
            void M(RwLock rwLock, Cache<Guard> cache)
            {
                {|#0:Cache<Guard>.A|} = rwLock.Enter();
                ref Guard a = ref {|#1:Cache<Guard>.A|};
                int count = {|#2:Cache<Guard>.Count|};
                int nested = {|#3:Outer<Guard>.Inner.Shared|};
                {|#4:Cache<List<Guard>>.A|} = null!;

                string name = Cache<Guard>.Name;
                string named = nameof(Cache<Guard>.A);
                ref Guard instance = ref cache.Instance;
                int plain = Plain.Shared + Cache<int>.Count;
            }
        }

        {{Cache}}

        {{Sources.Guard}}
        """,
        Verify.UnlistedStaticField(0, "Cache<T>.A", "Guard", "T", "F:Cache`1.A"),
        Verify.UnlistedStaticField(1, "Cache<T>.A", "Guard", "T", "F:Cache`1.A"),
        Verify.UnlistedStaticField(2, "Cache<T>.Count", "Guard", "T", "F:Cache`1.Count"),
        Verify.UnlistedStaticField(3, "Outer<T>.Inner.Shared", "Guard", "T", "F:Outer`1.Inner.Shared"),
        Verify.OpaqueTypeArgumentOfStaticField(4, "Cache<T>.A", "System.Collections.Generic.List<Guard>", "T", "F:Cache`1.A"));

    // An 'F:' line of the project's own file answers the report, for the type parameters that its 'for=' names.
    [TestMethod]
    public Task StaticField_WithALineGetsNoReport() => Verify.AnalyzerAsync(
        $$"""
        using System.Collections.Generic;

        class C
        {
            void M(RwLock rwLock)
            {
                Cache<Guard>.A = rwLock.Enter();
                Cache<List<Guard>>.A = null!;
                int nested = Outer<Guard>.Inner.Shared;
                int second = Pair<int, Guard>.Shared;
                int first = {|#0:Pair<Guard, int>.Shared|};
            }
        }

        {{Cache}}

        {{Sources.Guard}}
        """,
        """
        F:Cache`1.A
        F:Outer`1.Inner.Shared
        F:Pair`2.Shared;for=TSecond
        """,
        Verify.MemberListedForOtherTypeParameter(0, "Pair<TFirst, TSecond>.Shared", "Guard", "TFirst", "TSecond"));

    // A marked type parameter is checked in the generic's own code, where the initializer that copies its value is reported, so the use
    // of a static field is not.
    [TestMethod]
    public Task StaticField_OfAMarkedGenericIsCheckedInTheGeneric() => Verify.AnalyzerAsync(
        $$"""
        using Singulink.CodeAnalysis;

        class C
        {
            void M(RwLock rwLock)
            {
                Marked<Guard>.A = rwLock.Enter();
                ref Guard a = ref Marked<Guard>.A;
            }
        }

        class Marked<[NonCopyable] T>
        {
            public static T A;
            public static T B = {|#0:A|};
        }

        {{Sources.Guard}}
        """,
        Verify.CopyOfStoredValue(0, "The initializer takes", "T"));

    // The report is about the type that is bound, so it is strict under the updated memory safety rules where that type is unsafe to copy.
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task StaticField_IsStrictUnderTheUpdatedRulesWhereTheTypeIsUnsafeToCopy(bool unsafeToCopy)
    {
        string source = $$"""
            class C
            {
                void M(RwLock rwLock)
                {
                    {|#0:Cache<Guard>.A|} = rwLock.Enter();
                }
            }

            {{Cache}}

            {{(unsafeToCopy ? Sources.UnsafeGuard : Sources.Guard)}}
            """;

        return Verify.UpdatedRulesAsync(source, Verify.UnlistedStaticField(0, "Cache<T>.A", "Guard", "T", "F:Cache`1.A").AsError(unsafeToCopy));
    }

    // A library built without the analyzer is judged in the same way, and its static fields are answered by the lines of the project's
    // own file. A mark of a type parameter of such a library does not count. One built with the analyzer has its marked type parameters
    // checked in its own build.
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task StaticField_OfALibraryBuiltWithoutTheAnalyzerIsAnsweredByTheProjectsLine(bool withLine)
    {
        const string Library = """
            using Singulink.CodeAnalysis;

            public class Pool<T>
            {
                public static T Shared;
                public static readonly int Count = 0;
            }

            public class MarkedPool<[NonCopyable] T>
            {
                public static T Shared;
            }
            """;

        var test = new Verify.Test {
            TestCode = $$"""
                class C
                {
                    void M(RwLock rwLock)
                    {
                        {|#0:Pool<Guard>.Shared|} = rwLock.Enter();
                        int count = {|#1:Pool<Guard>.Count|};
                        {|#2:MarkedPool<Guard>.Shared|} = rwLock.Enter();
                        CheckedPool<Guard>.Shared = rwLock.Enter();
                    }
                }

                {{Sources.Guard}}
                """,
        };

        test.TestState.AdditionalReferences.Add(await Verify.CompileLibraryAsync(test.ReferenceAssemblies, Library, analyzed: false));
        test.TestState.AdditionalReferences.Add(await Verify.CompileLibraryAsync(
            test.ReferenceAssemblies,
            "public class CheckedPool<[Singulink.CodeAnalysis.NonCopyable] T> { public static T Shared; }",
            analyzed: true,
            assemblyName: "Checked"));

        if (withLine)
        {
            test.TestState.AdditionalFiles.Add((Verify.ListFileName, "F:Pool`1.Shared\nF:MarkedPool`1.Shared"));
        }
        else
        {
            test.ExpectedDiagnostics.Add(Verify.UnlistedStaticField(0, "Pool<T>.Shared", "Guard", "T", "F:Pool`1.Shared"));
            test.ExpectedDiagnostics.Add(Verify.UnlistedStaticFieldOfUncheckedCode(2, "MarkedPool<T>.Shared", "Guard", "T", "F:MarkedPool`1.Shared"));
        }

        test.ExpectedDiagnostics.Add(Verify.UnlistedStaticField(1, "Pool<T>.Count", "Guard", "T", "F:Pool`1.Count"));
        await test.RunAsync();
    }

    // The built-in framework symbol list has a line for each public static field of a generic type of the framework, so their uses with a
    // non-copyable type get nothing. A struct is read through a reference, since a by-value read of a stored one that holds the type
    // argument would be a copy of its own.
    [TestMethod]
    public async Task StaticField_OfTheFrameworkIsOnTheBuiltInList()
    {
        var test = new Verify.Test {
            TestCode = $$"""
                using System.Buffers;
                using System.Collections.Generic;
                using System.Collections.Immutable;
                using Microsoft.AspNetCore.Components;
                using Microsoft.AspNetCore.Http.Features;
                using Microsoft.AspNetCore.Routing.Matching;
                using Microsoft.Extensions.Logging.Abstractions;

                class C
                {
                    void M()
                    {
                        ImmutableArray<Guard> array = ImmutableArray<Guard>.Empty;
                        ImmutableDictionary<Guard, Guard> dictionary = ImmutableDictionary<Guard, Guard>.Empty;
                        ImmutableHashSet<Guard> set = ImmutableHashSet<Guard>.Empty;
                        ImmutableList<Guard> list = ImmutableList<Guard>.Empty;
                        ImmutableSortedDictionary<Guard, Guard> sortedDictionary = ImmutableSortedDictionary<Guard, Guard>.Empty;
                        ImmutableSortedSet<Guard> sortedSet = ImmutableSortedSet<Guard>.Empty;
                        ReadOnlySequence<Guard> sequence = ReadOnlySequence<Guard>.Empty;
                        NullLogger<Guard> logger = NullLogger<Guard>.Instance;
                        ref readonly EventCallback<Guard> callback = ref EventCallback<Guard>.Empty;
                        ref readonly FeatureReference<Guard> feature = ref FeatureReference<Guard>.Default;
                        EndpointMetadataComparer<List<Guard>> comparer = EndpointMetadataComparer<List<Guard>>.Default;
                    }
                }

                {{Sources.Guard}}
                """,
            ReferenceAssemblies = Verify.Test.NetReferenceAssemblies.AddPackages([new PackageIdentity("Microsoft.AspNetCore.App.Ref", "10.0.0")]),
        };

        await test.RunAsync();
    }

    // The one such field that only the reference assemblies of the .NET Framework have, with its line marked "# netfx". The project
    // declares its own attribute, since the one of the package is built for .NET.
    [TestMethod]
    public async Task StaticField_OfTheNetFrameworkIsOnTheBuiltInList()
    {
        var test = new Verify.Test {
            TestCode = """
                using System.Xml.Xsl.Runtime;

                class C
                {
                    void M()
                    {
                        XmlQuerySequence<Guard> sequence = XmlQuerySequence<Guard>.Empty;
                        {|#0:Unlisted<Guard>.Shared|} = 0;
                    }
                }

                class Unlisted<T>
                {
                    public static int Shared;
                }

                [Singulink.CodeAnalysis.NonCopyable]
                struct Guard { }

                namespace Singulink.CodeAnalysis
                {
                    [System.AttributeUsage(System.AttributeTargets.Struct | System.AttributeTargets.GenericParameter)]
                    internal sealed class NonCopyableAttribute : System.Attribute { }
                }
                """,
            ReferenceAssemblies = FrameworkScanTests.NetFrameworkReferencePackage.AddAssemblies(["System.Data.SqlXml"]),
        };

        test.TestState.AdditionalReferences.Clear();
        test.ExpectedDiagnostics.Add(Verify.UnlistedStaticField(0, "Unlisted<T>.Shared", "Guard", "T", "F:Unlisted`1.Shared"));
        await test.RunAsync();
    }

    // The line of a field takes 'for=' and no marker. A line for a field that no use is judged by has no effect: an instance field, a
    // constant, and a static field of a type that is not generic. A line that names nothing is ignored, as for any member.
    [TestMethod]
    public Task Line_OfAFieldTakesNoMarkerAndHasAnEffectOnlyForAStaticFieldOfAGenericType() => Verify.AnalyzerAsync(
        Cache,
        """
        F:Cache`1.A
        F:Cache`1.A;for=T
        F:Outer`1.Inner.Shared
        F:Missing`1.Shared
        F:Cache`1.A;new
        F:Cache`1.A;borrows
        F:Cache`1.A;borrowswhen=false
        F:Cache`1.A;clones
        F:Cache`1.A;view
        F:Cache`1.A;for=T;new
        F:Cache`1.A(System.Int32)
        F:Cache`1.A;for=U
        F:Cache`1.Instance
        F:Cache`1.Name
        F:Plain.Shared
        """,
        Verify.SafeListLine(5, 1, 16, "a field holds its value, so what a read of it gives is never new; the line of a static field takes no marker, only 'for='"),
        Verify.SafeListLine(6, 1, 20, "the marker 'borrows' is not for a field; the line of a static field takes no marker, only 'for='"),
        Verify.SafeListLine(7, 1, 30, "the marker 'borrowswhen' is not for a field; the line of a static field takes no marker, only 'for='"),
        Verify.SafeListLine(8, 1, 19, "the marker 'clones' is not for a field; the line of a static field takes no marker, only 'for='"),
        Verify.SafeListLine(9, 1, 17, "the marker 'view' is not for a field; the line of a static field takes no marker, only 'for='"),
        Verify.SafeListLine(10, 1, 22, "'for=' is the last part of an entry"),
        Verify.SafeListLine(11, 1, 26, "only a method or a property has a parameter list"),
        Verify.SafeListLine(12, 1, 18, "'U' is not a type parameter of the member or of a type that contains it"),
        Verify.SafeListFieldHasNoEffect(13, 1, 19, "F:Cache`1.Instance", "the field is not static, and reading or writing it runs no code of its type"),
        Verify.SafeListFieldHasNoEffect(14, 1, 15, "F:Cache`1.Name", "the field is a constant, which runs no code"),
        Verify.SafeListFieldHasNoEffect(15, 1, 15, "F:Plain.Shared", "its type is neither generic nor nested in a generic type, so its static initializers run with no type argument that a use binds"));
}
