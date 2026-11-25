using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;
using PrefixClassName.MsTest;

namespace Singulink.CodeAnalysis.Tests;

/// <summary>
/// Tests of the types that are unsafe to copy, whose copy can break memory safety: under the updated memory safety rules only a
/// report about such a type is an error, and a report about any other non-copyable type is the ordinary warning of the current
/// rules. Under the current rules the two are reported alike.
/// </summary>
/// <remarks>
/// Docs: articles/rules/updated-memory-safety-rules.md, articles/guides/marking-a-struct.md, articles/guides/built-in-types.md and
/// articles/rules/symbol-list-format.md.
/// </remarks>
[PrefixTestClass]
public class UnsafeToCopyTests
{
    // A plain non-copyable struct and one that is unsafe to copy, each with a field of the class below to copy from.
    private const string Types = """
        [Singulink.CodeAnalysis.NonCopyable]
        public struct Guard { }

        [Singulink.CodeAnalysis.NonCopyable(UnsafeToCopy = true)]
        public struct Handle { }
        """;

    [TestMethod]
    public Task Attribute_InSourceMakesACopyAnErrorUnderTheUpdatedRules() => Verify.UpdatedRulesAsync(
        $$"""
        class C
        {
            Guard _guard;
            Handle _handle;

            void M()
            {
                var guard = {|#0:_guard|};
                var handle = {|#1:_handle|};
            }
        }

        {{Types}}
        """,
        Verify.CopyByAssignment(0, "Guard"),
        Verify.CopyByAssignment(1, "Handle").AsError());

    // Under the current rules the property changes nothing.
    [TestMethod]
    public Task Attribute_ChangesNothingUnderTheCurrentRules() => Verify.AnalyzerAsync(
        $$"""
        class C
        {
            Guard _guard;
            Handle _handle;

            void M()
            {
                var guard = {|#0:_guard|};
                var handle = {|#1:_handle|};
            }
        }

        {{Types}}
        """,
        Verify.CopyByAssignment(0, "Guard"),
        Verify.CopyByAssignment(1, "Handle"));

    // The property is read from the metadata of a referenced assembly.
    [TestMethod]
    public async Task Attribute_InAReferencedAssemblyCounts()
    {
        if (!Verify.CompilerHasUpdatedRules)
            Assert.Inconclusive("The compiler of this target does not apply the updated memory safety rules.");

        var library = await Verify.CompileLibraryAsync(
            Verify.Test.NetReferenceAssemblies, "namespace Lib { " + Types + " }", analyzed: true, "Lib");

        var test = new Verify.Test {
            TestCode = """
                class C
                {
                    Lib.Guard _guard;
                    Lib.Handle _handle;

                    void M()
                    {
                        var guard = {|#0:_guard|};
                        var handle = {|#1:_handle|};
                    }
                }
                """,
            LanguageVersion = LanguageVersion.Preview,
            Features = [Verify.UpdatedRulesFeature],
            ExpectedDiagnostics = {
                Verify.CopyByAssignment(0, "Lib.Guard"),
                Verify.CopyByAssignment(1, "Lib.Handle").AsError(),
            },
        };

        test.TestState.AdditionalReferences.Add(library);
        await test.RunAsync();
    }

    // A project's own internal copy of the attribute counts by its full name, the property with it. A copy without the property cannot
    // be given the argument, and makes a struct plain non-copyable.
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public Task Attribute_OfTheProjectCountsWithThePropertyAndWithoutIt(bool declaresProperty)
    {
        if (!Verify.CompilerHasUpdatedRules)
            Assert.Inconclusive("The compiler of this target does not apply the updated memory safety rules.");

        string property = declaresProperty ? "public bool UnsafeToCopy { get; set; }" : string.Empty;
        string mark = declaresProperty ? "[NonCopyable(UnsafeToCopy = true)]" : "[NonCopyable]";
        var expected = Verify.CopyByAssignment(0, "App.Handle");

        var test = new Verify.Test {
            TestCode = $$"""
                namespace Singulink.CodeAnalysis
                {
                    [System.AttributeUsage(System.AttributeTargets.Struct | System.AttributeTargets.GenericParameter)]
                    internal sealed class NonCopyableAttribute : System.Attribute
                    {
                        {{property}}
                    }
                }

                namespace App
                {
                    using Singulink.CodeAnalysis;

                    class C
                    {
                        Handle _handle;

                        void M()
                        {
                            var handle = {|#0:_handle|};
                        }
                    }

                    {{mark}}
                    struct Handle { }
                }
                """,
            LanguageVersion = LanguageVersion.Preview,
            Features = [Verify.UpdatedRulesFeature],
            ExpectedDiagnostics = { declaresProperty ? expected.AsError() : expected },
        };

        test.TestState.AdditionalReferences.Clear();
        return test.RunAsync();
    }

    // A struct that declares a field of a type that is unsafe to copy says so itself, with the property on its own attribute. Until it
    // does, the field is reported, an ordinary warning under the current rules and an error under the updated rules, and the struct is
    // what its attribute says: a copy of it is plain. A field that holds such a value by reference asks nothing.
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task Field_OfATypeThatIsUnsafeToCopyAsksTheStructToSaySo(bool updatedRules)
    {
        string source = $$"""
            using Singulink.CodeAnalysis;

            class C
            {
                Outer _outer;
                Said _said;

                void M()
                {
                    var outer = {|#0:_outer|};
                    var said = {|#1:_said|};
                }
            }

            [NonCopyable]
            struct Outer { Handle {|#2:_handle|}; }

            [NonCopyable(UnsafeToCopy = true)]
            struct Said { Handle _handle; }

            [NonCopyable(UnsafeToCopy = false)]
            struct SaidFalse { Handle {|#3:_handle|}; }

            [NonCopyable]
            struct OfNullable { Handle? {|#4:_handle|}; }

            [NonCopyable]
            struct OfTuple { (int, Handle) {|#5:_pair|}; }

            [NonCopyable]
            struct OfOuter { Said {|#6:_said|}; }

            [NonCopyable]
            struct ByParameter(Native {|#7:native|})
            {
                public int Size() => native.Size;
            }

            [NonCopyable(UnsafeToCopy = true)]
            struct SaidByParameter(Native native)
            {
                public int Size() => native.Size;
            }

            [NonCopyable]
            struct OfGuard { Guard _guard; }

            [NonCopyable]
            struct OfArray { Handle[] _handles; }

            [NonCopyable]
            struct OfClass { Holder _holder; }

            class Holder { public Handle Handle; }

            [NonCopyable(UnsafeToCopy = true)]
            struct Native { public readonly int Size => 0; }

            {{Types}}
            """;

        DiagnosticResult[] expected = [
            Verify.CopyByAssignment(0, "Outer"),
            Verify.CopyByAssignment(1, "Said").AsError(updatedRules),
            Verify.UnsafeToCopyField(2, "Handle", "Outer._handle", "Outer").AsError(updatedRules),
            Verify.UnsafeToCopyField(3, "Handle", "SaidFalse._handle", "SaidFalse").AsError(updatedRules),
            Verify.UnsafeToCopyField(4, "Handle?", "OfNullable._handle", "OfNullable").AsError(updatedRules),
            Verify.UnsafeToCopyField(5, "(int, Handle)", "OfTuple._pair", "OfTuple").AsError(updatedRules),
            Verify.UnsafeToCopyField(6, "Said", "OfOuter._said", "OfOuter").AsError(updatedRules),
            Verify.UnsafeToCopyFieldByPrimaryConstructor(7, "Native", "native", "ByParameter").AsError(updatedRules),
        ];

        return updatedRules ? Verify.UpdatedRulesAsync(source, expected) : Verify.AnalyzerAsync(source, expected);
    }

    // A field whose type is a type parameter of the struct, or a constructed type that is unsafe to copy only through one, asks nothing
    // of the struct: each constructed type of it is judged by its type arguments.
    [TestMethod]
    public Task Field_OfATypeParameterAsksNothingOfTheStruct() => Verify.UpdatedRulesAsync(
        $$"""
        using Singulink.CodeAnalysis;

        class C
        {
            Pair<Handle> _handles;
            Pair<Guard> _guards;

            void M()
            {
                var handles = {|#0:_handles|};
                var guards = {|#1:_guards|};
            }
        }

        [NonCopyable]
        struct Pair<[NonCopyable] T>
        {
            T _first;
            Box<T> _second;
            (T, int) _third;
        }

        class Outer<[NonCopyable] T>
        {
            [NonCopyable]
            struct Inner { T _value; }
        }

        struct Box<T> { public T Value; }

        {{Types}}
        """,
        Verify.CopyByAssignment(0, "Pair<Handle>").AsError(),
        Verify.CopyByAssignment(1, "Pair<Guard>"));

    // A struct that declares a field of a non-copyable type must be marked itself, also where it has a marked type parameter, of its own
    // or of a type that contains it: Pair<int> is copyable and copies the field. A field of a type that is unsafe to copy gets the one
    // report that names the property, and a field that is non-copyable only through the type parameter asks nothing. A parameter of the
    // primary constructor that the struct keeps in a field is judged as a declared field.
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task Field_OfANonCopyableTypeAsksAStructWithAMarkedTypeParameterToBeMarked(bool updatedRules)
    {
        const string Source = """
            using Singulink.CodeAnalysis;

            struct Pair<[NonCopyable] T> { T _first; Lock {|#0:_second|}; }

            struct UnsafePair<[NonCopyable] T> { T _first; Native {|#1:_second|}; }

            struct Mixed<[NonCopyable] T> { (T, Lock) {|#2:_pair|}; }

            struct ByParameter<[NonCopyable] T>(Lock {|#3:held|})
            {
                public int Size() => held.Size;
            }

            struct UnsafeByParameter<[NonCopyable] T>(Native {|#4:native|})
            {
                public int Size() => native.Size;
            }

            class Outer<[NonCopyable] T>
            {
                struct Inner { Lock {|#5:_lock|}; }
            }

            [NonCopyable]
            struct MarkedUnsafe<[NonCopyable] T> { T _first; Native {|#6:_second|}; }

            [NonCopyable]
            struct Marked<[NonCopyable] T> { T _first; Lock _second; }

            struct Wrapper<[NonCopyable] T> { T _value; Box<T> _box; }

            struct Box<T> { public T Value; }

            [NonCopyable]
            struct Lock { public readonly int Size => 0; }

            [NonCopyable(UnsafeToCopy = true)]
            struct Native { public readonly int Size => 0; }
            """;

        DiagnosticResult[] expected = [
            Verify.FieldOfCopyableType(0, "Lock", "Pair<T>._second"),
            Verify.FieldOfCopyableTypeUnsafeToCopy(1, "Native", "UnsafePair<T>._second", "UnsafePair<T>").AsError(updatedRules),
            Verify.FieldOfCopyableType(2, "(T, Lock)", "Mixed<T>._pair").AsError(updatedRules),
            Verify.FieldByPrimaryConstructor(3, "Lock", "held", "ByParameter<T>"),
            Verify.FieldByPrimaryConstructorUnsafeToCopy(4, "Native", "native", "UnsafeByParameter<T>").AsError(updatedRules),
            Verify.FieldOfCopyableType(5, "Lock", "Outer<T>.Inner._lock"),
            Verify.UnsafeToCopyField(6, "Native", "MarkedUnsafe<T>._second", "MarkedUnsafe<T>").AsError(updatedRules),
        ];

        return updatedRules ? Verify.UpdatedRulesAsync(Source, expected) : Verify.AnalyzerAsync(Source, expected);
    }

    // A struct of a referenced assembly that holds such a field without the property is what its attribute says for the project that
    // references it: plain. The library's own build has the report.
    [TestMethod]
    public async Task Field_InAReferencedAssemblyLeavesTheStructAsItsAttributeSays()
    {
        if (!Verify.CompilerHasUpdatedRules)
            Assert.Inconclusive("The compiler of this target does not apply the updated memory safety rules.");

        var library = await Verify.CompileLibraryAsync(
            Verify.Test.NetReferenceAssemblies,
            "namespace Lib { [Singulink.CodeAnalysis.NonCopyable] public struct Outer { Handle _handle; } " + Types + " }",
            analyzed: true,
            "Lib");

        var test = new Verify.Test {
            TestCode = """
                class C
                {
                    Lib.Outer _outer;

                    void M()
                    {
                        var outer = {|#0:_outer|};
                    }
                }
                """,
            LanguageVersion = LanguageVersion.Preview,
            Features = [Verify.UpdatedRulesFeature],
            ExpectedDiagnostics = { Verify.CopyByAssignment(0, "Lib.Outer") },
        };

        test.TestState.AdditionalReferences.Add(library);
        await test.RunAsync();
    }

    // A constructed struct is unsafe to copy where it is non-copyable through a type argument that is: a nullable, a tuple, a generic
    // struct with a type parameter that is not marked or that is, at any depth. No field is looked at for this.
    [TestMethod]
    public Task ConstructedTypes_AreJudgedByTheirTypeArguments() => Verify.UpdatedRulesAsync(
        $$"""
        using System.Collections.Generic;
        using Singulink.CodeAnalysis;

        class C
        {
            Handle? _nullable;
            Guard? _nullableGuard;
            (Handle, int) _tuple;
            (Guard, int) _tupleOfGuard;
            (int, int, int, int, int, int, int, Handle) _longTuple;
            Box<Handle> _box;
            Box<Guard> _boxOfGuard;
            Marked<Handle> _marked;
            Marked<Guard> _markedOfGuard;
            Box<Marked<Handle>> _twoLevels;
            Box<Marked<Guard>> _twoLevelsOfGuard;
            Bag<Handle> _bag;
            KeyValuePair<int, Handle> _pair;

            void M()
            {
                var a = {|#0:_nullable|};
                var b = {|#1:_nullableGuard|};
                var c = {|#2:_tuple|};
                var d = {|#3:_tupleOfGuard|};
                var e = {|#4:_longTuple|};
                var f = {|#5:_box|};
                var g = {|#6:_boxOfGuard|};
                var h = {|#7:_marked|};
                var i = {|#8:_markedOfGuard|};
                var j = {|#9:_twoLevels|};
                var k = {|#10:_twoLevelsOfGuard|};
                var l = {|#11:_bag|};
                var m = {|#12:_pair|};
            }
        }

        struct Box<T> { public T Value; }

        [NonCopyable]
        struct Marked<[NonCopyable] T> { public T Value; }

        // No field holds a value of the type argument inline, and the struct is judged by its type argument all the same.
        struct Bag<T> { public T[] Items; }

        {{Types}}
        """,
        Verify.CopyByAssignment(0, "Handle?").AsError(),
        Verify.CopyByAssignment(1, "Guard?"),
        Verify.CopyByAssignment(2, "(Handle, int)").AsError(),
        Verify.CopyByAssignment(3, "(Guard, int)"),
        Verify.CopyByAssignment(4, "(int, int, int, int, int, int, int, Handle)").AsError(),
        Verify.CopyByAssignment(5, "Box<Handle>").AsError(),
        Verify.CopyByAssignment(6, "Box<Guard>"),
        Verify.CopyByAssignment(7, "Marked<Handle>").AsError(),
        Verify.CopyByAssignment(8, "Marked<Guard>"),
        Verify.CopyByAssignment(9, "Box<Marked<Handle>>").AsError(),
        Verify.CopyByAssignment(10, "Box<Marked<Guard>>"),
        Verify.CopyByAssignment(11, "Bag<Handle>").AsError(),
        Verify.CopyByAssignment(12, "System.Collections.Generic.KeyValuePair<int, Handle>").AsError());

    // A type parameter marked [NonCopyable], with UnsafeToCopy not written or true, can stand for any non-copyable type, so a report
    // about it is strict. One marked UnsafeToCopy = false is a plain non-copyable type, and a report about it is the ordinary warning,
    // as is one about a constructed type over it. No form of the property gets a report.
    [TestMethod]
    public Task TypeParameter_IsUnsafeToCopyUnlessItSaysFalse() => Verify.UpdatedRulesAsync(
        """
        using Singulink.CodeAnalysis;

        class Slot<[NonCopyable] T, [NonCopyable(UnsafeToCopy = true)] U, [NonCopyable(UnsafeToCopy = false)] V>
        {
            T _value;
            U _other;
            V _plain;
            Wrapper<T> _wrapped;
            Wrapper<V> _wrappedPlain;

            void M()
            {
                var value = {|#0:_value|};
                var other = {|#1:_other|};
                var plain = {|#2:_plain|};
                var wrapped = {|#3:_wrapped|};
                var wrappedPlain = {|#4:_wrappedPlain|};
            }
        }

        struct Wrapper<T> { public T Value; }
        """,
        Verify.CopyByAssignment(0, "T").AsError(),
        Verify.CopyByAssignment(1, "U").AsError(),
        Verify.CopyByAssignment(2, "V"),
        Verify.CopyByAssignment(3, "Wrapper<T>").AsError(),
        Verify.CopyByAssignment(4, "Wrapper<V>"));

    // Under the current rules the three forms are reported alike.
    [TestMethod]
    public Task TypeParameter_EveryFormIsTheOrdinaryWarningUnderTheCurrentRules() => Verify.AnalyzerAsync(
        """
        using Singulink.CodeAnalysis;

        class Slot<[NonCopyable] T, [NonCopyable(UnsafeToCopy = true)] U, [NonCopyable(UnsafeToCopy = false)] V>
        {
            T _value;
            U _other;
            V _plain;

            void M()
            {
                var value = {|#0:_value|};
                var other = {|#1:_other|};
                var plain = {|#2:_plain|};
            }
        }
        """,
        Verify.CopyByAssignment(0, "T"),
        Verify.CopyByAssignment(1, "U"),
        Verify.CopyByAssignment(2, "V"));

    // A line of the project's own symbol list file makes a library struct that is only [NonCopyable] unsafe to copy in that project,
    // and so does a file that came in a package.
    [TestMethod]
    [DataRow("/NonCopyableSymbols.txt")]
    [DataRow("/packages/lib/1.0.0/build/NonCopyableSymbols.Lib.txt")]
    public async Task ListLine_MakesALibraryStructUnsafeToCopy(string path)
    {
        if (!Verify.CompilerHasUpdatedRules)
            Assert.Inconclusive("The compiler of this target does not apply the updated memory safety rules.");

        var library = await Verify.CompileLibraryAsync(
            Verify.Test.NetReferenceAssemblies, "namespace Lib { " + Types + " }", analyzed: true, "Lib");

        var test = new Verify.Test {
            TestCode = """
                class C
                {
                    Lib.Guard _guard;
                    Lib.Handle _handle;

                    void M()
                    {
                        var guard = {|#0:_guard|};
                        var handle = {|#1:_handle|};
                    }
                }
                """,
            LanguageVersion = LanguageVersion.Preview,
            Features = [Verify.UpdatedRulesFeature],
            ExpectedDiagnostics = {
                Verify.CopyByAssignment(0, "Lib.Guard").AsError(),
                Verify.CopyByAssignment(1, "Lib.Handle").AsError(),
            },
        }.WithBuildProperty("NuGetPackageRoot", "/packages/");

        test.TestState.AdditionalReferences.Add(library);
        test.TestState.AdditionalFiles.Add((path, "T:Lib.Guard;unsafetocopy"));
        await test.RunAsync();
    }

    // A line makes a struct of no attribute non-copyable and unsafe to copy, as 'noncopyable' makes it non-copyable. Under the current
    // rules the two lines are reported alike.
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task ListLine_MarksAStructThatHasNoAttribute(bool updatedRules)
    {
        const string Source = """
            class C
            {
                Listed _listed;
                Unsafe _unsafe;

                void M()
                {
                    var listed = {|#0:_listed|};
                    var copy = {|#1:_unsafe|};
                }
            }

            struct Listed { }

            struct Unsafe { }
            """;

        const string List = "T:Listed;noncopyable\nT:Unsafe;unsafetocopy";
        var listed = Verify.CopyByAssignment(0, "Listed");
        var copy = Verify.CopyByAssignment(1, "Unsafe");

        return updatedRules
            ? Verify.UpdatedRulesAsync(Source, List, listed, copy.AsError())
            : Verify.AnalyzerAsync(Source, List, listed, copy);
    }

    // One file says 'noncopyable' and another 'unsafetocopy': for this claim the stricter line applies, in either order.
    [TestMethod]
    [DataRow("T:Lib.Guard;noncopyable", "T:Lib.Guard;unsafetocopy")]
    [DataRow("T:Lib.Guard;unsafetocopy", "T:Lib.Guard;noncopyable")]
    public async Task ListLines_ThatDisagreeMergeToUnsafeToCopy(string first, string second)
    {
        if (!Verify.CompilerHasUpdatedRules)
            Assert.Inconclusive("The compiler of this target does not apply the updated memory safety rules.");

        var library = await Verify.CompileLibraryAsync(
            Verify.Test.NetReferenceAssemblies, "namespace Lib { public struct Guard { } }", analyzed: true, "Lib");

        var test = new Verify.Test {
            TestCode = """
                class C
                {
                    Lib.Guard _guard;

                    void M()
                    {
                        var guard = {|#0:_guard|};
                    }
                }
                """,
            LanguageVersion = LanguageVersion.Preview,
            Features = [Verify.UpdatedRulesFeature],
            ExpectedDiagnostics = { Verify.CopyByAssignment(0, "Lib.Guard").AsError() },
        };

        test.TestState.AdditionalReferences.Add(library);
        test.TestState.AdditionalFiles.Add(("/a/NonCopyableSymbols.txt", first));
        test.TestState.AdditionalFiles.Add(("/b/NonCopyableSymbols.txt", second));
        await test.RunAsync();
    }

    // Nothing makes a type that is unsafe to copy plain non-copyable: a 'noncopyable' line leaves one by attribute and one of the
    // built-in list as they are. The line for the type by attribute gets a warning for having no effect; the line for the type of the
    // built-in list gets none, since a project that leaves the built-in types out needs it.
    [TestMethod]
    public async Task NonCopyableLine_LeavesATypeThatIsUnsafeToCopyAsItIs()
    {
        if (!Verify.CompilerHasUpdatedRules)
            Assert.Inconclusive("The compiler of this target does not apply the updated memory safety rules.");

        var library = await Verify.CompileLibraryAsync(
            Verify.Test.NetReferenceAssemblies, "namespace Lib { " + Types + " }", analyzed: true, "Lib");

        var test = new Verify.Test {
            TestCode = """
                using System.Runtime.InteropServices;

                class C
                {
                    Lib.Handle _handle;
                    GCHandle _gcHandle;

                    void M()
                    {
                        var handle = {|#0:_handle|};
                        var gcHandle = {|#1:_gcHandle|};
                    }
                }
                """,
            LanguageVersion = LanguageVersion.Preview,
            Features = [Verify.UpdatedRulesFeature],
            ExpectedDiagnostics = {
                Verify.CopyByAssignment(0, "Lib.Handle").AsError(),
                Verify.CopyByAssignment(1, "System.Runtime.InteropServices.GCHandle").AsError(),
                Verify.SafeListNonCopyableHasNoEffect(1, 25, "T:Lib.Handle", "non-copyable", "its attribute marks it unsafe to copy, which makes it non-copyable as well"),
            },
        };

        test.TestState.AdditionalReferences.Add(library);
        test.TestState.AdditionalFiles.Add((Verify.ListFileName, "T:Lib.Handle;noncopyable\nT:System.Runtime.InteropServices.GCHandle;noncopyable"));
        await test.RunAsync();
    }

    // An 'unsafetocopy' line that names no type gets a warning, as a 'noncopyable' line does: a mistyped ID would leave the type
    // copyable.
    [TestMethod]
    public Task ListLine_UnsafeToCopyThatNamesNoTypeGetsAWarning() => Verify.AnalyzerAsync(
        "class C { }",
        "T:ThirdParty.Missing;unsafetocopy",
        Verify.SafeListNonCopyableNamesNothing(1, 1, 34, "T:ThirdParty.Missing"));

    // A 'copyable' line of a file of the project takes a type that is unsafe to copy out, as it does any type: the project copies it on
    // purpose. The same line in a file that came in a package has no effect and gets no warning: whether a type may be copied is the
    // decision of the project that copies it, and the library's own build honors the line in its own file.
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CopyableLine_CountsOnlyInAFileOfTheProject(bool inPackage)
    {
        if (!Verify.CompilerHasUpdatedRules)
            Assert.Inconclusive("The compiler of this target does not apply the updated memory safety rules.");

        var library = await Verify.CompileLibraryAsync(
            Verify.Test.NetReferenceAssemblies, "namespace Lib { " + Types + " }", analyzed: true, "Lib");

        var test = new Verify.Test {
            TestCode = """
                using System.Runtime.InteropServices;

                class C
                {
                    Lib.Handle _handle;
                    GCHandle _gcHandle;

                    void M()
                    {
                        var handle = {|#0:_handle|};
                        var gcHandle = {|#1:_gcHandle|};
                    }
                }
                """,
            LanguageVersion = LanguageVersion.Preview,
            Features = [Verify.UpdatedRulesFeature],
        }.WithBuildProperty("NuGetPackageRoot", "/packages/");

        string path = inPackage ? "/packages/lib/1.0.0/build/NonCopyableSymbols.Lib.txt" : Verify.ListFileName;
        test.TestState.AdditionalReferences.Add(library);
        test.TestState.AdditionalFiles.Add((path, "T:Lib.Handle;copyable\nT:System.Runtime.InteropServices.GCHandle;copyable"));

        if (inPackage)
        {
            test.ExpectedDiagnostics.Add(Verify.CopyByAssignment(0, "Lib.Handle").AsError());
            test.ExpectedDiagnostics.Add(Verify.CopyByAssignment(1, "System.Runtime.InteropServices.GCHandle").AsError());
        }

        await test.RunAsync();
    }

    // 'unsafetocopy' fits a struct only, as 'noncopyable' does, and a member line cannot have it.
    [TestMethod]
    public Task ListLine_UnsafeToCopyIsForAStruct() => Verify.AnalyzerAsync(
        """
        class C { }

        class Plain { }

        struct S
        {
            public void M() { }
        }
        """,
        "T:Plain;unsafetocopy\nM:S.M;unsafetocopy",
        Verify.SafeListLine(1, 1, 21, "the marker 'unsafetocopy' is for a struct"),
        Verify.SafeListLine(2, 1, 19, "the marker 'unsafetocopy' is for a type; a member takes 'new', 'borrows', 'drains', 'owned', 'borrowswhen', 'clones' or no marker"));

    // A field of a type that is unsafe to copy in a struct that is not marked is an error, and a warning at a field marked 'safe', with a
    // message that names the property, so that one edit fixes both. A field of a plain non-copyable type is the ordinary warning, with
    // the modifier and without it.
    [TestMethod]
    public Task Field_OfATypeThatIsUnsafeToCopyIsAnErrorUnlessItIsMarked() => Verify.UpdatedRulesAsync(
        $$"""
        struct S
        {
            Handle {|#0:_handle|};
            safe Handle {|#1:_marked|};
            Guard {|#2:_guard|};
            safe Guard {|#3:_markedGuard|};
        }

        {{Types}}
        """,
        Verify.FieldOfCopyableTypeUnsafeToCopy(0, "Handle", "S._handle", "S").AsError(),
        Verify.FieldOfCopyableTypeUnsafeToCopy(1, "Handle", "S._marked", "S"),
        Verify.FieldOfCopyableType(2, "Guard", "S._guard"),
        Verify.FieldOfCopyableType(3, "Guard", "S._markedGuard"));

    // A report about a container is about the values that it holds: a copy out of a List<T> of a type that is unsafe to copy is
    // strict, and one out of a List<T> of a plain type is not. So is the keeping of a list, and its conversion to a type that hides
    // its elements.
    [TestMethod]
    public Task Container_IsJudgedByTheValuesThatItHolds() => Verify.UpdatedRulesAsync(
        $$"""
        using System.Collections.Generic;

        class C
        {
            List<Guard> _guards = new();
            List<Handle> _handles = new();
            object _kept;

            void Read()
            {
                Guard guard = {|#0:_guards[0]|};
                Handle handle = {|#1:_handles[0]|};
            }

            void KeepGuards(List<Guard> guards) => _kept = {|#2:guards|};

            void KeepHandles(List<Handle> handles) => _kept = {|#3:handles|};
        }

        {{Types}}
        """,
        Verify.ReadFromStoredContainer(0, "'List<T>.this[int]'", "Guard"),
        Verify.ReadFromStoredContainer(1, "'List<T>.this[int]'", "Handle").AsError(),
        Verify.KeepsUnmarkedParameter(2, "C.KeepGuards(List<Guard>)", "guards", "System.Collections.Generic.List<Guard>"),
        Verify.OpaqueConversion(2, "System.Collections.Generic.List<Guard>", "object"),
        Verify.KeepsUnmarkedParameter(3, "C.KeepHandles(List<Handle>)", "handles", "System.Collections.Generic.List<Handle>").AsError(),
        Verify.OpaqueConversion(3, "System.Collections.Generic.List<Handle>", "object").AsError());

    // A message that advises a suppression gives the advice of the updated rules only for a report about a type that is unsafe to copy,
    // which at a declaration says that marking it 'safe' answers for it. A report about a plain type has the message of the current
    // rules.
    [TestMethod]
    public Task Message_OfTheUpdatedRulesIsForATypeThatIsUnsafeToCopyOnly() => Verify.UpdatedRulesAsync(
        $$"""
        sealed class GuardCulture : System.Globalization.CultureInfo
        {
            Guard {|#0:_guard|};

            public GuardCulture() : base("") { }
        }

        sealed class HandleCulture : System.Globalization.CultureInfo
        {
            Handle {|#1:_handle|};
            safe Handle {|#2:_marked|};

            public HandleCulture() : base("") { }
        }

        {{Types}}
        """,
        Verify.MemberwiseCloneThroughListedBase(0, "GuardCulture", "System.Globalization.CultureInfo", "Guard", "_guard"),
        Verify.MemberwiseCloneThroughListedBaseOfTypeThatIsUnsafeToCopy(1, "HandleCulture", "System.Globalization.CultureInfo", "Handle", "_handle").AsError(),
        Verify.MemberwiseCloneThroughListedBaseOfTypeThatIsUnsafeToCopy(2, "HandleCulture", "System.Globalization.CultureInfo", "Handle", "_marked"));

    // A binding in the base list of a class is at a declaration, which marking the class 'safe' answers for.
    [TestMethod]
    public Task Message_OfABindingInABaseListSaysThatTheClassCanBeMarkedSafe() => Verify.UpdatedRulesAsync(
        $$"""
        using System.Globalization;

        sealed class GuardCulture : {|#0:Tagged<Guard>|}
        {
        }

        sealed class HandleCulture : {|#1:Tagged<Handle>|}
        {
        }

        safe sealed class MarkedCulture : {|#2:Tagged<Handle>|}
        {
        }

        class Tagged<[Singulink.CodeAnalysis.NonCopyable] T> : CultureInfo
        {
            public T {|#3:Tag|};

            public Tagged() : base("") { }
        }

        {{Types}}
        """,
        Verify.CloningBinding(0, "Guard", "Tagged<Guard>", "System.Globalization.CultureInfo"),
        Verify.CloningBindingInBaseListOfTypeThatIsUnsafeToCopy(1, "Handle", "Tagged<Handle>", "System.Globalization.CultureInfo").AsError(),
        Verify.CloningBindingInBaseListOfTypeThatIsUnsafeToCopy(2, "Handle", "Tagged<Handle>", "System.Globalization.CultureInfo"),
        Verify.MemberwiseCloneOfTypeParameterThroughListedBaseOfTypeThatIsUnsafeToCopy(3, "Tagged<T>", "System.Globalization.CultureInfo", "T", "Tag").AsError());

    // NonCopyableErrorsAsWarnings reports both kinds of type as under the current rules.
    [TestMethod]
    public Task ErrorsAsWarnings_ReportsBothKindsAsTheCurrentRules()
    {
        if (!Verify.CompilerHasUpdatedRules)
            Assert.Inconclusive("The compiler of this target does not apply the updated memory safety rules.");

        return new Verify.Test {
            TestCode = $$"""
                class C
                {
                    Guard _guard;
                    Handle _handle;

                    void M()
                    {
                        var guard = {|#0:_guard|};
                        var handle = {|#1:_handle|};
                    }
                }

                struct S
                {
                    Handle {|#2:_handle|};
                }

                {{Types}}
                """,
            LanguageVersion = LanguageVersion.Preview,
            Features = [Verify.UpdatedRulesFeature],
            ExpectedDiagnostics = {
                Verify.CopyByAssignment(0, "Guard"),
                Verify.CopyByAssignment(1, "Handle"),
                Verify.FieldOfCopyableTypeUnsafeToCopy(2, "Handle", "S._handle", "S"),
            },
        }.WithBuildProperty("NonCopyableErrorsAsWarnings", "true").RunAsync();
    }

    // [MovedFrom] on a parameter of an indexer is reported once, at the parameter, and not again for each accessor, which has a
    // parameter of its own for it. A [MovedFrom] on the value of a setter is the setter's own.
    [TestMethod]
    public Task MovedFrom_OnAParameterOfAnIndexerIsReportedOnce() => Verify.AnalyzerAsync(
        $$"""
        using Singulink.CodeAnalysis;

        class C
        {
            int this[[MovedFrom] Guard {|#0:guard|}]
            {
                get => 0;
                set { }
            }

            int this[int index, [MovedFrom] Guard {|#1:guard|}] => 0;

            int Value
            {
                get => 0;

                [param: MovedFrom]
                {|#2:set|} { }
            }
        }

        {{Types}}
        """,
        Verify.MovedFromNotOnRefParameter(0),
        Verify.MovedFromNotOnRefParameter(1),
        Verify.MovedFromNotOnRefParameter(2));

    // The analyzer reads UnsafeToCopy only where the attribute is applied, so a mark of the project's own that sets it in its own code
    // gets a warning there: in a constructor, as the initializer of the property, and as a constant for a base constructor. A struct
    // marked with it is what can be read where it is marked: plain non-copyable.
    [TestMethod]
    public Task DerivedAttribute_ThatSetsUnsafeToCopyInItsOwnCodeGetsAWarning() => Verify.UpdatedRulesAsync(
        """
        namespace Singulink.CodeAnalysis
        {
            [System.AttributeUsage(System.AttributeTargets.Struct | System.AttributeTargets.GenericParameter)]
            internal class NonCopyableAttribute : System.Attribute
            {
                public NonCopyableAttribute() { }

                public NonCopyableAttribute(bool unsafeToCopy) { UnsafeToCopy = unsafeToCopy; }

                public bool UnsafeToCopy { get; set; }
            }

            internal sealed class SetInConstructorAttribute : NonCopyableAttribute
            {
                public SetInConstructorAttribute() { {|#0:UnsafeToCopy = true|}; }
            }

            internal sealed class PassedToBaseAttribute : NonCopyableAttribute
            {
                public PassedToBaseAttribute() : base({|#1:unsafeToCopy: true|}) { }
            }

            internal sealed class PassedOnAttribute : NonCopyableAttribute
            {
                public PassedOnAttribute(bool unsafeToCopy) : base(unsafeToCopy) { }
            }

            internal sealed class SetFromParameterAttribute : NonCopyableAttribute
            {
                public SetFromParameterAttribute(bool unsafeToCopy) { UnsafeToCopy = unsafeToCopy; }
            }
        }

        namespace App
        {
            using Singulink.CodeAnalysis;

            [SetInConstructor]
            struct SetInConstructor { }

            [PassedToBase]
            struct PassedToBase { }

            [PassedOn(unsafeToCopy: true)]
            struct PassedOn { }

            [SetFromParameter(true)]
            struct SetFromParameter { }

            [NonCopyable(true)]
            struct ByConstructor { }

            class C
            {
                SetInConstructor _a;
                PassedToBase _b;
                PassedOn _c;
                SetFromParameter _d;
                ByConstructor _e;

                void M()
                {
                    var a = {|#2:_a|};
                    var b = {|#3:_b|};
                    var c = {|#4:_c|};
                    var d = {|#5:_d|};
                    var e = {|#6:_e|};
                }
            }
        }
        """,
        Verify.UnsafeToCopySetInOwnCode(0, "SetInConstructorAttribute"),
        Verify.UnsafeToCopySetInOwnCode(1, "PassedToBaseAttribute"),
        Verify.CopyByAssignment(2, "App.SetInConstructor"),
        Verify.CopyByAssignment(3, "App.PassedToBase"),
        Verify.CopyByAssignment(4, "App.PassedOn").AsError(),
        Verify.CopyByAssignment(5, "App.SetFromParameter").AsError(),
        Verify.CopyByAssignment(6, "App.ByConstructor").AsError());

    // An argument for a 'bool' parameter of the constructor named 'unsafeToCopy' is read from metadata as well, on a struct of a
    // referenced assembly and on a type parameter, where 'false' makes it take no type that is unsafe to copy.
    [TestMethod]
    public async Task DerivedAttribute_ArgumentForUnsafeToCopyParameterIsReadFromMetadata()
    {
        if (!Verify.CompilerHasUpdatedRules)
            Assert.Inconclusive("The compiler of this target does not apply the updated memory safety rules.");

        var library = await Verify.CompileLibraryAsync(
            Verify.Test.NetReferenceAssemblies,
            """
            namespace Singulink.CodeAnalysis
            {
                [System.AttributeUsage(System.AttributeTargets.Struct | System.AttributeTargets.GenericParameter)]
                internal class NonCopyableAttribute : System.Attribute
                {
                    public bool UnsafeToCopy { get; set; }
                }

                internal sealed class MarkAttribute : NonCopyableAttribute
                {
                    public MarkAttribute(bool UNSAFETOCOPY) { UnsafeToCopy = UNSAFETOCOPY; }
                }
            }

            namespace Lib
            {
                [Singulink.CodeAnalysis.Mark(true)]
                public struct Handle { }

                [Singulink.CodeAnalysis.Mark(false)]
                public struct Guard { }

                public static class Generic
                {
                    public static void Take<[Singulink.CodeAnalysis.Mark(false)] T>(ref T value) { }
                }
            }
            """,
            analyzed: true,
            "Lib");

        var test = new Verify.Test {
            TestCode = """
                class C
                {
                    Lib.Handle _handle;
                    Lib.Guard _guard;

                    void M()
                    {
                        var handle = {|#0:_handle|};
                        var guard = {|#1:_guard|};
                        {|#2:Lib.Generic.Take(ref _handle)|};
                        Lib.Generic.Take(ref _guard);
                    }
                }
                """,
            LanguageVersion = LanguageVersion.Preview,
            Features = [Verify.UpdatedRulesFeature],
            ExpectedDiagnostics = {
                Verify.CopyByAssignment(0, "Lib.Handle").AsError(),
                Verify.CopyByAssignment(1, "Lib.Guard"),
                Verify.UnsafeToCopyNotTaken(2, "Generic.Take<T>(ref T)", "Lib.Handle", "T").AsError(),
            },
        };

        test.TestState.AdditionalReferences.Add(library);
        await test.RunAsync();
    }

    // The initializer of the property is code of the attribute class too: a struct marked with no argument is plain for the analyzer.
    [TestMethod]
    public Task OwnAttribute_WithAnInitializedPropertyGetsAWarning() => Verify.UpdatedRulesAsync(
        """
        namespace Singulink.CodeAnalysis
        {
            [System.AttributeUsage(System.AttributeTargets.Struct | System.AttributeTargets.GenericParameter)]
            internal sealed class NonCopyableAttribute : System.Attribute
            {
                public bool UnsafeToCopy { get; set; } {|#0:= true|};
            }
        }

        namespace App
        {
            [Singulink.CodeAnalysis.NonCopyable]
            struct Handle { }

            class C
            {
                Handle _handle;

                void M()
                {
                    var handle = {|#1:_handle|};
                }
            }
        }
        """,
        Verify.UnsafeToCopySetInOwnCode(0, "NonCopyableAttribute"),
        Verify.CopyByAssignment(1, "App.Handle"));

    // A public field named UnsafeToCopy is set by a named argument as the property is, so code of the attribute class that writes it, in
    // a constructor or in its initializer, gets the warning as well. Setting it from the constructor's own 'unsafeToCopy' parameter is what
    // the analyzer reads, and gets nothing, and a named argument for the field counts where the attribute is applied.
    [TestMethod]
    public Task OwnAttribute_WithAFieldThatItsOwnCodeSetsGetsAWarning() => Verify.UpdatedRulesAsync(
        """
        namespace Singulink.CodeAnalysis
        {
            [System.AttributeUsage(System.AttributeTargets.Struct | System.AttributeTargets.GenericParameter)]
            internal class NonCopyableAttribute : System.Attribute
            {
                public bool UnsafeToCopy;

                public NonCopyableAttribute() { }

                public NonCopyableAttribute(bool unsafeToCopy) => UnsafeToCopy = unsafeToCopy;
            }

            internal sealed class AssignedAttribute : NonCopyableAttribute
            {
                public AssignedAttribute() { {|#0:UnsafeToCopy = true|}; }
            }

            internal sealed class AssignedThroughThisAttribute : NonCopyableAttribute
            {
                public AssignedThroughThisAttribute() { {|#1:this.UnsafeToCopy = true|}; }
            }

            internal sealed class AssignedThroughBaseAttribute : NonCopyableAttribute
            {
                public AssignedThroughBaseAttribute() { {|#2:base.UnsafeToCopy = true|}; }
            }
        }

        namespace Other
        {
            [System.AttributeUsage(System.AttributeTargets.Struct)]
            internal sealed class InitializedAttribute : Singulink.CodeAnalysis.NonCopyableAttribute
            {
                public new bool UnsafeToCopy {|#6:= true|};
            }
        }

        namespace App
        {
            [Singulink.CodeAnalysis.Assigned]
            struct Assigned { }

            [Singulink.CodeAnalysis.NonCopyable(UnsafeToCopy = true)]
            struct Named { }

            [Singulink.CodeAnalysis.NonCopyable(unsafeToCopy: true)]
            struct ByParameter { }

            class C
            {
                Assigned _assigned;
                Named _named;
                ByParameter _byParameter;

                void M()
                {
                    var assigned = {|#3:_assigned|};
                    var named = {|#4:_named|};
                    var byParameter = {|#5:_byParameter|};
                }
            }
        }
        """,
        Verify.UnsafeToCopySetInOwnCode(0, "AssignedAttribute"),
        Verify.UnsafeToCopySetInOwnCode(1, "AssignedThroughThisAttribute"),
        Verify.UnsafeToCopySetInOwnCode(2, "AssignedThroughBaseAttribute"),
        Verify.UnsafeToCopySetInOwnCode(6, "InitializedAttribute"),
        Verify.CopyByAssignment(3, "App.Assigned"),
        Verify.CopyByAssignment(4, "App.Named").AsError(),
        Verify.CopyByAssignment(5, "App.ByParameter").AsError());

    // The initializer of the field is code of the attribute class too.
    [TestMethod]
    public Task OwnAttribute_WithAnInitializedFieldGetsAWarning() => Verify.UpdatedRulesAsync(
        """
        namespace Singulink.CodeAnalysis
        {
            [System.AttributeUsage(System.AttributeTargets.Struct | System.AttributeTargets.GenericParameter)]
            internal sealed class NonCopyableAttribute : System.Attribute
            {
                public bool UnsafeToCopy {|#0:= true|};
            }
        }

        namespace App
        {
            [Singulink.CodeAnalysis.NonCopyable]
            struct Handle { }

            class C
            {
                Handle _handle;

                void M()
                {
                    var handle = {|#1:_handle|};
                }
            }
        }
        """,
        Verify.UnsafeToCopySetInOwnCode(0, "NonCopyableAttribute"),
        Verify.CopyByAssignment(1, "App.Handle"));
}
