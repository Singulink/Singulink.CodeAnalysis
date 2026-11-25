using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;
using PrefixClassName.MsTest;

namespace Singulink.CodeAnalysis.Tests;

/// <summary>
/// Roslyn's tests of its rule, for C#. The Visual Basic parts are left out, since the analyzer only supports C#. A test that had to change says
/// how the analyzer differs from Roslyn's rule. The generic helper methods of the tests mark their type parameter, without which they could not
/// be used with a non-copyable type.
/// </summary>
/// <remarks>
/// Docs: articles/rules/roslyn-differences.md.
/// </remarks>
[PrefixTestClass]
public class RoslynTests
{
    private const string GCHandle = "System.Runtime.InteropServices.GCHandle";

    [TestMethod]
    public Task SliceOfString() => new Verify.Test {
        LanguageVersion = LanguageVersion.CSharp9,
        TestCode = """
            using System.Runtime.InteropServices;

            class C
            {
                void M()
                {
                    var local = ""[..];
                    local = ""[..];
                }
            }
            """,
    }.RunAsync();

    [TestMethod]
    public Task AcquireFromReturnByValue() => Verify.AnalyzerAsync("""
        using System.Runtime.InteropServices;

        class C
        {
            void M()
            {
                var local = GCHandle.Alloc(new object());
            }
        }
        """);

    [TestMethod]
    [DataRow("Func<ValueTask<GCHandle>>")]
    [DataRow("Func<ConfiguredValueTaskAwaitable<GCHandle>>")]
    [DataRow("Func<Task<GCHandle>>")]
    [DataRow("Func<ConfiguredTaskAwaitable<GCHandle>>")]
    public Task AcquireFromAwaitInvocation(string invokeType) => Verify.AnalyzerAsync($$"""
        using System;
        using System.Runtime.CompilerServices;
        using System.Runtime.InteropServices;
        using System.Threading.Tasks;

        class C
        {
            async Task M({{invokeType}} d)
            {
                var local = await d();
                local = await d();
            }
        }
        """);

    // Differs from Roslyn's rule, which takes the first two for new values: awaiting a stored task or value task reads its result by value, and the
    // task still holds it.
    [TestMethod]
    [DataRow("ValueTask<GCHandle>")]
    [DataRow("ConfiguredValueTaskAwaitable<GCHandle>")]
    [DataRow("Task<GCHandle>")]
    [DataRow("ConfiguredTaskAwaitable<GCHandle>")]
    public Task FailedAcquireFromAwaitOfStoredTask(string awaitableType) => Verify.AnalyzerAsync(
        $$"""
        using System.Runtime.CompilerServices;
        using System.Runtime.InteropServices;
        using System.Threading.Tasks;

        class C
        {
            async Task M([Singulink.CodeAnalysis.Kept] {{awaitableType}} task)
            {
                var local = {|#0:await task|};

                // A variable that a reference is taken to is never moved from at its last use.
                ref var reference = ref task;
            }
        }
        """,
        Verify.ReadFromStoredContainer(0, "'await'", GCHandle));

    [TestMethod]
    [DataRow("field")]
    [DataRow("(field)")]
    [DataRow("this.field")]
    [DataRow("(this).field")]
    [DataRow("((C)this).field")]
    public Task AcquireIntoFieldFromReturnByValue(string fieldReference) => Verify.AnalyzerAsync($$"""
        using System.Runtime.InteropServices;

        class C
        {
            GCHandle field;

            void M(bool condition)
            {
                {{fieldReference}} = GCHandle.Alloc(new object());
                {{fieldReference}} = condition ? GCHandle.Alloc(new object()) : GCHandle.Alloc(new object());
            }
        }
        """);

    [TestMethod]
    [DataRow("field")]
    [DataRow("(field)")]
    [DataRow("this.field")]
    [DataRow("(this).field")]
    [DataRow("((C)this).field")]
    public Task AcquireIntoArrayFieldFromReturnByValue(string fieldReference) => Verify.AnalyzerAsync($$"""
        using System.Runtime.InteropServices;

        class C
        {
            GCHandle[] field;

            void M()
            {
                {{fieldReference}}[0] = GCHandle.Alloc(new object());
            }
        }
        """);

    [TestMethod]
    public Task DoNotAcquireFromReturnByReference() => Verify.AnalyzerAsync(
        """
        using System.Runtime.InteropServices;

        class C
        {
            void M()
            {
                var local = {|#0:GetRef()|};
            }

            ref GCHandle GetRef() => throw null;
        }
        """,
        Verify.CopyByAssignment(0, GCHandle));

    [TestMethod]
    public Task PassToInstancePropertyGetter() => Verify.AnalyzerAsync(
        """
        [Singulink.CodeAnalysis.NonCopyable]
        struct Handle
        {
            private int _value;

            public int Target => _value;

            public readonly int ReadOnlyTarget => _value;
        }

        class C
        {
            Handle field;
            readonly Handle readonlyfield;

            void M()
            {
                var local = new Handle();
                ref var reflocal = ref local;
                ref readonly var refreadonlylocal = ref local;

                // Call each property twice to ensure the analyzer didn't silently treat one like a move
                _ = field.Target;
                _ = field.Target;
                _ = local.Target;
                _ = local.Target;
                _ = reflocal.Target;
                _ = reflocal.Target;

                // Through a read-only reference the compiler runs a getter that is not readonly on a copy. The getters of GCHandle, which
                // Roslyn's test uses, have become readonly since it was written.
                _ = {|#0:readonlyfield|}.Target;
                _ = {|#1:refreadonlylocal|}.Target;

                _ = readonlyfield.ReadOnlyTarget;
                _ = refreadonlylocal.ReadOnlyTarget;
            }
        }
        """,
        Verify.MemberOnReadOnlyReference(0, "Handle", "Handle.Target"),
        Verify.MemberOnReadOnlyReference(1, "Handle", "Handle.Target"));

    [TestMethod]
    public Task PassToInstanceMethod() => Verify.AnalyzerAsync(
        """
        [Singulink.CodeAnalysis.NonCopyable]
        struct Handle
        {
            private int _value;

            public int AddrOfPinnedObject() => _value;

            public readonly int ReadOnlyAddrOfPinnedObject() => _value;
        }

        class C
        {
            Handle field;
            readonly Handle readonlyfield;

            void M()
            {
                var local = new Handle();
                ref var reflocal = ref local;
                ref readonly var refreadonlylocal = ref local;

                // Call each method twice to ensure the analyzer didn't silently treat one like a move
                _ = field.AddrOfPinnedObject();
                _ = field.AddrOfPinnedObject();
                _ = local.AddrOfPinnedObject();
                _ = local.AddrOfPinnedObject();
                _ = reflocal.AddrOfPinnedObject();
                _ = reflocal.AddrOfPinnedObject();

                // Through a read-only reference the compiler runs a method that is not readonly on a copy. The methods of GCHandle, which
                // Roslyn's test uses, have become readonly since it was written.
                _ = {|#0:readonlyfield|}.AddrOfPinnedObject();
                _ = {|#1:refreadonlylocal|}.AddrOfPinnedObject();

                _ = readonlyfield.ReadOnlyAddrOfPinnedObject();
                _ = refreadonlylocal.ReadOnlyAddrOfPinnedObject();
            }
        }
        """,
        Verify.MemberOnReadOnlyReference(0, "Handle", "Handle.AddrOfPinnedObject()"),
        Verify.MemberOnReadOnlyReference(1, "Handle", "Handle.AddrOfPinnedObject()"));

    // Differs from Roslyn's rule, which reports the first two failure cases as well: they do not compile, so they cannot run, and get the
    // compiler's error alone.
    [TestMethod]
    public Task PassToExtensionMethod() => Verify.AnalyzerAsync(
        """
        using System.Runtime.InteropServices;

        class C
        {
            GCHandle field;
            readonly GCHandle readonlyfield;

            void M()
            {
                var local = new GCHandle();
                ref var reflocal = ref local;
                ref readonly var refreadonlylocal = ref local;

                // Success cases. Call each method twice to ensure the analyzer didn't silently treat one like a move.
                field.XRef();
                field.XRef();
                local.XRef();
                local.XRef();
                reflocal.XRef();
                reflocal.XRef();

                readonlyfield.XIn();
                readonlyfield.XIn();
                reflocal.XIn();
                reflocal.XIn();
                local.XIn();
                local.XIn();

                // Failure cases.
                {|CS0192:readonlyfield|}.XRef();
                {|CS1510:refreadonlylocal|}.XRef();
                {|#2:field|}.X();
                {|#3:readonlyfield|}.X();
                {|#4:local|}.X();
                {|#5:reflocal|}.X();
                {|#6:refreadonlylocal|}.X();
            }
        }

        static class E
        {
            public static void X(this GCHandle handle) { }
            public static void XRef(this ref GCHandle handle) { }
            public static void XIn(this in GCHandle handle) { }
        }
        """,
        Verify.CopyOfStoredValue(2, "'E.X(GCHandle)' receives", GCHandle),
        Verify.CopyOfStoredValue(3, "'E.X(GCHandle)' receives", GCHandle),
        Verify.CopyOfStoredValue(4, "'E.X(GCHandle)' receives", GCHandle),
        Verify.CopyOfStoredValue(5, "'E.X(GCHandle)' receives", GCHandle),
        Verify.CopyOfStoredValue(6, "'E.X(GCHandle)' receives", GCHandle));

    [TestMethod]
    [DataRow("throw null")]
    [DataRow("(true ? throw null : default(GCHandle))")]
    [DataRow("(false ? new GCHandle() : throw null)")]
    public Task ConversionFromThrowNull(string throwExpression) => Verify.AnalyzerAsync($$"""
        using System.Runtime.InteropServices;

        class C
        {
            GCHandle Get() => {{throwExpression}};
        }
        """);

    [TestMethod]
    public Task PassByReference() => Verify.AnalyzerAsync("""
        using System.Runtime.InteropServices;

        class C
        {
            void Get(ref GCHandle handle) => Get(ref handle);
        }
        """);

    [TestMethod]
    public Task PassByReadOnlyReference() => Verify.AnalyzerAsync("""
        using System.Runtime.InteropServices;

        class C
        {
            void Get(in GCHandle handle) => Get(in handle);
        }
        """);

    [TestMethod]
    public Task AssignToMember() => Verify.AnalyzerAsync("""
        using System.Runtime.InteropServices;

        class C
        {
            CannotCopy _field;

            void Method(CannotCopy parameter)
            {
                CannotCopy local = new CannotCopy();

                _field.Field = 0;
                parameter.Field = 0;
                local.Field = 0;

                _field.Property = 0;
                parameter.Property = 0;
                local.Property = 0;
            }
        }

        [Singulink.CodeAnalysis.NonCopyable]
        struct CannotCopy
        {
            public int Field;
            public int Property { get; set; }
        }

        namespace Singulink.CodeAnalysis { internal sealed class NonCopyableAttribute : System.Attribute { } }
        """);

    [TestMethod]
    public Task ReturnLocalByValue() => new Verify.Test {
        LanguageVersion = LanguageVersion.CSharp8,
        TestCode = """
            using System.Runtime.InteropServices;

            class C
            {
                GCHandle Method()
                {
                    GCHandle handle = default;
                    return handle;
                }
            }
            """,
    }.RunAsync();

    [TestMethod]
    public Task ReturnMember() => new Verify.Test {
        LanguageVersion = LanguageVersion.CSharp8,
        TestCode = """
            using System.Runtime.InteropServices;

            class C
            {
                CannotCopy _field;
                readonly CannotCopy _readonlyField;

                int ReturnFieldMemberField()
                {
                    return _field.Field;
                }

                int ReturnReadonlyFieldMemberField()
                {
                    return _readonlyField.Field;
                }

                int ReturnParameterMemberField(CannotCopy parameter)
                {
                    return parameter.Field;
                }

                int ReturnArrayParameterMemberField(CannotCopy[] parameter)
                {
                    return parameter[0].Field;
                }

                int ReturnLocalMemberField()
                {
                    CannotCopy local = new CannotCopy();
                    return local.Field;
                }

                int ReturnFieldMemberProperty()
                {
                    return _field.Property;
                }

                int ReturnReadonlyFieldMemberProperty()
                {
                    return {|#0:_readonlyField|}.Property;
                }

                int ReturnParameterMemberProperty(CannotCopy parameter)
                {
                    return parameter.Property;
                }

                int ReturnArrayParameterMemberProperty(CannotCopy[] parameter)
                {
                    return parameter[0].Property;
                }

                int ReturnLocalMemberProperty()
                {
                    CannotCopy local = new CannotCopy();
                    return local.Property;
                }

                int ReturnFieldMemberReadonlyProperty()
                {
                    return _field.ReadonlyProperty;
                }

                int ReturnReadonlyFieldMemberReadonlyProperty()
                {
                    return _readonlyField.ReadonlyProperty;
                }

                int ReturnParameterMemberReadonlyProperty(CannotCopy parameter)
                {
                    return parameter.ReadonlyProperty;
                }

                int ReturnArrayParameterMemberReadonlyProperty(CannotCopy[] parameter)
                {
                    return parameter[0].ReadonlyProperty;
                }

                int ReturnLocalMemberReadonlyProperty()
                {
                    CannotCopy local = new CannotCopy();
                    return local.ReadonlyProperty;
                }
            }

            [Singulink.CodeAnalysis.NonCopyable]
            struct CannotCopy
            {
                public int Field;
                public int Property { get { return 0; } }
                public int ReadonlyProperty { readonly get { return 0; } set { } }
            }

            namespace Singulink.CodeAnalysis { internal sealed class NonCopyableAttribute : System.Attribute { } }
            """,

        // The only reported diagnostic occurs for the invocation of a non-readonly getter of a readonly non-copyable field.
        ExpectedDiagnostics = { Verify.MemberOnReadOnlyReference(0, "CannotCopy", "CannotCopy.Property") },
    }.RunAsync();

    [TestMethod]
    public Task NonReadonlyMemberProperties() => new Verify.Test {
        LanguageVersion = LanguageVersion.CSharp8,
        TestCode = """
            [Singulink.CodeAnalysis.NonCopyable]
            struct CannotCopy
            {
                public int First { get { return 0; } }
                public int Second { get { return First; } }
                public int Third => First;
            }

            namespace Singulink.CodeAnalysis { internal sealed class NonCopyableAttribute : System.Attribute { } }
            """,
    }.RunAsync();

    [TestMethod]
    public Task NonReadonlyMemberMethods() => new Verify.Test {
        LanguageVersion = LanguageVersion.CSharp8,
        TestCode = """
            [Singulink.CodeAnalysis.NonCopyable]
            struct CannotCopy
            {
                public int First() { return 0; }
                public int Second() { return First(); }
                public int Third() => First();
            }

            namespace Singulink.CodeAnalysis { internal sealed class NonCopyableAttribute : System.Attribute { } }
            """,
    }.RunAsync();

    [TestMethod]
    public Task AllowObjectInitializer() => new Verify.Test {
        LanguageVersion = LanguageVersion.CSharp8,
        TestCode = """
            class C
            {
                CannotCopy Method()
                {
                    return new CannotCopy() { First = 0, Second = 1 };
                }
            }

            [Singulink.CodeAnalysis.NonCopyable]
            struct CannotCopy
            {
                public int First { get; set; }
                public int Second { get; set; }
            }

            namespace Singulink.CodeAnalysis { internal sealed class NonCopyableAttribute : System.Attribute { } }
            """,
    }.RunAsync();

    [TestMethod]
    public Task AllowCustomForeachEnumerator() => new Verify.Test {
        LanguageVersion = LanguageVersion.CSharp8,
        TestCode = """
            class C
            {
                void Method()
                {
                    var cannotCopy = new CannotCopy();
                    foreach (var obj in cannotCopy)
                    {
                    }
                }
            }

            [Singulink.CodeAnalysis.NonCopyable]
            struct CannotCopy
            {
                public Enumerator GetEnumerator() => throw null;

                public struct Enumerator
                {
                    public object Current => throw null;
                    public bool MoveNext() => throw null;
                }
            }

            namespace Singulink.CodeAnalysis { internal sealed class NonCopyableAttribute : System.Attribute { } }
            """,
    }.RunAsync();

    [TestMethod]
    [DataRow("", "")]
    [DataRow("", "readonly")]
    [DataRow("ref", "")]
    [DataRow("ref", "readonly")]
    [DataRow("in", "")]
    [DataRow("in", "readonly")]
    public async Task AllowCustomForeachEnumeratorParameterReference(string parameterModifiers, string getEnumeratorModifiers)
    {
        var test = new Verify.Test {
            LanguageVersion = LanguageVersion.CSharp8,
            TestCode = $$"""
                class C
                {
                    void Method({{parameterModifiers}} CannotCopy cannotCopy)
                    {
                        foreach (var obj in {|#0:cannotCopy|})
                        {
                        }
                    }
                }

                [Singulink.CodeAnalysis.NonCopyable]
                struct CannotCopy
                {
                    public {{getEnumeratorModifiers}} Enumerator GetEnumerator() => throw null;

                    public struct Enumerator
                    {
                        public object Current => throw null;
                        public bool MoveNext() => throw null;
                    }
                }

                namespace Singulink.CodeAnalysis { internal sealed class NonCopyableAttribute : System.Attribute { } }
                """,
        };

        if (parameterModifiers is "in" && getEnumeratorModifiers is "")
            test.ExpectedDiagnostics.Add(Verify.MemberOnReadOnlyReference(0, "CannotCopy", "CannotCopy.GetEnumerator()"));

        await test.RunAsync();
    }

    [TestMethod]
    public Task AllowCustomForeachEnumeratorDisposableObject1() => new Verify.Test {
        LanguageVersion = LanguageVersion.CSharp8,
        TestCode = """
            using System;

            class C
            {
                void Method()
                {
                    using var cannotCopy = new CannotCopy();
                    foreach (var obj in cannotCopy)
                    {
                    }
                }
            }

            [Singulink.CodeAnalysis.NonCopyable]
            struct CannotCopy : IDisposable
            {
                public void Dispose() => throw null;
                public Enumerator GetEnumerator() => throw null;

                public struct Enumerator
                {
                    public object Current => throw null;
                    public bool MoveNext() => throw null;
                }
            }

            namespace Singulink.CodeAnalysis { internal sealed class NonCopyableAttribute : System.Attribute { } }
            """,
    }.RunAsync();

    [TestMethod]
    public Task AllowCustomForeachEnumeratorDisposableObject2() => new Verify.Test {
        LanguageVersion = LanguageVersion.CSharp8,
        TestCode = """
            using System;

            class C
            {
                void Method()
                {
                    using (var cannotCopy = new CannotCopy())
                    {
                        foreach (var obj in cannotCopy)
                        {
                        }
                    }
                }
            }

            [Singulink.CodeAnalysis.NonCopyable]
            struct CannotCopy : IDisposable
            {
                public void Dispose() => throw null;
                public Enumerator GetEnumerator() => throw null;

                public struct Enumerator
                {
                    public object Current => throw null;
                    public bool MoveNext() => throw null;
                }
            }

            namespace Singulink.CodeAnalysis { internal sealed class NonCopyableAttribute : System.Attribute { } }
            """,
    }.RunAsync();

    [TestMethod]
    [DataRow("new CannotCopy()")]
    [DataRow("default(CannotCopy)")]
    [DataRow("CannotCopy.Create()")]
    [DataRow("CannotCopy.Empty")]
    public Task AllowDisposableObject(string creation) => new Verify.Test {
        LanguageVersion = LanguageVersion.CSharp8,
        TestCode = $$"""
            using System;

            class C
            {
                void UsingStatement()
                {
                    using ({{creation}})
                    {
                    }
                }

                void UsingStatementWithVariable()
                {
                    using (var cannotCopy = {{creation}})
                    {
                    }
                }

                void UsingStatementWithDiscard()
                {
                    using (_ = {{creation}})
                    {
                    }
                }

                void UsingDeclarationStatement()
                {
                    using var cannotCopy = {{creation}};
                }
            }

            [Singulink.CodeAnalysis.NonCopyable]
            struct CannotCopy : IDisposable
            {
                public static CannotCopy Empty => throw null;
                public static CannotCopy Create() => throw null;

                public void Dispose() => throw null;
            }

            namespace Singulink.CodeAnalysis { internal sealed class NonCopyableAttribute : System.Attribute { } }
            """,
    }.RunAsync();

    [TestMethod]
    public Task AllowCustomForeachReadonlyEnumerator() => new Verify.Test {
        LanguageVersion = LanguageVersion.CSharp8,
        TestCode = """
            class C
            {
                void Method()
                {
                    var cannotCopy = new CannotCopy();
                    foreach (var obj in cannotCopy)
                    {
                    }
                }
            }

            [Singulink.CodeAnalysis.NonCopyable]
            struct CannotCopy
            {
                public readonly Enumerator GetEnumerator() => throw null;

                public struct Enumerator
                {
                    public object Current => throw null;
                    public bool MoveNext() => throw null;
                }
            }

            namespace Singulink.CodeAnalysis { internal sealed class NonCopyableAttribute : System.Attribute { } }
            """,
    }.RunAsync();

    [TestMethod]
    [DataRow("")]
    [DataRow("ref")]
    [DataRow("in")]
    public Task AllowNameOfParameterReference(string parameterModifiers) => new Verify.Test {
        LanguageVersion = LanguageVersion.CSharp8,
        TestCode = $$"""
            class C
            {
                void Method({{parameterModifiers}} CannotCopy value)
                {
                    _ = nameof(CannotCopy);
                    _ = nameof(value);
                    _ = nameof(value.ToString);
                }
            }

            [Singulink.CodeAnalysis.NonCopyable]
            struct CannotCopy
            {
            }

            namespace Singulink.CodeAnalysis { internal sealed class NonCopyableAttribute : System.Attribute { } }
            """,
    }.RunAsync();

    [TestMethod]
    [DataRow("ref")]
    [DataRow("in")]
    public Task AllowUnsafeAsRefParameterReference(string parameterModifiers) => new Verify.Test {
        LanguageVersion = LanguageVersion.CSharp8,
        TestCode = $$"""
            class C
            {
                ref CannotCopy Method({{parameterModifiers}} CannotCopy cannotCopy)
                {
                    return ref AsRef(in cannotCopy);
                }

                ref T AsRef<[Singulink.CodeAnalysis.NonCopyable] T>(in T value)
                    => throw null;
            }

            [Singulink.CodeAnalysis.NonCopyable]
            struct CannotCopy
            {
            }

            namespace Singulink.CodeAnalysis { internal sealed class NonCopyableAttribute : System.Attribute { } }
            """,
    }.RunAsync();

    [TestMethod]
    public async Task AllowUnsafeAsRefParameterReference2()
    {
        foreach (string parameterModifiers in (string[])["ref", "in", "ref readonly"])
        {
            foreach (string asRefParameterModifiers in (string[])["in", "scoped in", "ref readonly", "scoped ref readonly"])
            {
                await new Verify.Test {
                    LanguageVersion = LanguageVersion.CSharp12,
                    TestCode = $$"""
                        class C
                        {
                            ref CannotCopy Method({{parameterModifiers}} CannotCopy cannotCopy)
                            {
                                return ref AsRef(in cannotCopy);
                            }

                            ref T AsRef<[Singulink.CodeAnalysis.NonCopyable] T>({{asRefParameterModifiers}} T value)
                                => throw null;
                        }

                        [Singulink.CodeAnalysis.NonCopyable]
                        struct CannotCopy
                        {
                        }

                        namespace Singulink.CodeAnalysis { internal sealed class NonCopyableAttribute : System.Attribute { } }
                        """,
                }.RunAsync();
            }
        }
    }

    [TestMethod]
    [DataRow("ref", "ref")]
    [DataRow("in", "ref readonly")]
    public Task StoreUnsafeAsRefParameterReferenceToLocal(string parameterModifiers, string localModifiers) => new Verify.Test {
        LanguageVersion = LanguageVersion.CSharp8,
        TestCode = $$"""
            class C
            {
                void Method({{parameterModifiers}} CannotCopy cannotCopy)
                {
                    {{localModifiers}} var local = ref AsRef(in cannotCopy);

                    local = ref AsRef(in cannotCopy);
                }

                ref T AsRef<[Singulink.CodeAnalysis.NonCopyable] T>(in T value)
                    => throw null;
            }

            [Singulink.CodeAnalysis.NonCopyable]
            struct CannotCopy
            {
            }

            namespace Singulink.CodeAnalysis { internal sealed class NonCopyableAttribute : System.Attribute { } }
            """,
    }.RunAsync();

    [TestMethod]
    public Task CannotStoreRefReturnByValue() => new Verify.Test {
        LanguageVersion = LanguageVersion.CSharp8,
        TestCode = """
            class C
            {
                void Method(in CannotCopy cannotCopy)
                {
                    // Test with initializer
                    var local = {|#0:AsRef(in cannotCopy)|};

                    // Test with assignment to local
                    local = {|#1:AsRef(in cannotCopy)|};

                    // Implicit and explicit discard is acceptable
                    AsRef(in cannotCopy);
                    _ = AsRef(in cannotCopy);
                }

                ref T AsRef<[Singulink.CodeAnalysis.NonCopyable] T>(in T value)
                    => throw null;
            }

            [Singulink.CodeAnalysis.NonCopyable]
            struct CannotCopy
            {
            }

            namespace Singulink.CodeAnalysis { internal sealed class NonCopyableAttribute : System.Attribute { } }
            """,
        ExpectedDiagnostics = {
            Verify.CopyByAssignment(0, "CannotCopy"),
            Verify.CopyByAssignment(1, "CannotCopy"),
        },
    }.RunAsync();

    [TestMethod]
    public Task CannotReturnRefReturnByValue() => new Verify.Test {
        LanguageVersion = LanguageVersion.CSharp8,
        TestCode = """
            class C
            {
                CannotCopy Method(in CannotCopy cannotCopy)
                {
                    return {|#0:AsRef(in cannotCopy)|};
                }

                ref T AsRef<[Singulink.CodeAnalysis.NonCopyable] T>(in T value)
                    => throw null;
            }

            [Singulink.CodeAnalysis.NonCopyable]
            struct CannotCopy
            {
            }

            namespace Singulink.CodeAnalysis { internal sealed class NonCopyableAttribute : System.Attribute { } }
            """,
        ExpectedDiagnostics = { Verify.CopyByReturn(0, "CannotCopy") },
    }.RunAsync();

    [TestMethod]
    public Task NonCopyableAttribute() => Verify.AnalyzerAsync(
        """
        class C
        {
            CanCopy _canCopy;
            CannotCopy _cannotCopy;

            void Method(object value)
            {
                Method(_canCopy);
                Method({|#0:_cannotCopy|});
            }
        }

        struct CanCopy
        {
        }

        [Singulink.CodeAnalysis.NonCopyable]
        struct CannotCopy
        {
        }

        namespace Singulink.CodeAnalysis { internal sealed class NonCopyableAttribute : System.Attribute { } }
        """,
        Verify.Boxing(0, "CannotCopy", "object"));

    // Differs from Roslyn's rule, which reports every Nullable<T> of a non-copyable type: it is a non-copyable type itself here.
    [TestMethod]
    public Task AllowNonCopyableTypeInNullableT() => Verify.AnalyzerAsync("""
        using System.Runtime.InteropServices;

        class C
        {
            GCHandle? field = null;
        }
        """);

    [TestMethod]
    public Task DoNotDefineNonCopyableFieldInCopyableType() => Verify.AnalyzerAsync(
        """
        class C1
        {
            CanCopy field1;
            CannotCopy field2;
        }

        struct C2
        {
            CanCopy field1;
            CannotCopy {|#0:field2|};
        }

        [Singulink.CodeAnalysis.NonCopyable]
        struct C3
        {
            CanCopy field1;
            CannotCopy field2;
        }

        struct CanCopy { }
        [Singulink.CodeAnalysis.NonCopyable] struct CannotCopy { }
        namespace Singulink.CodeAnalysis { internal sealed class NonCopyableAttribute : System.Attribute { } }
        """,
        Verify.FieldOfCopyableType(0, "CannotCopy", "C2.field2"));

    [TestMethod]
    public Task DoNotDefineNonCopyableAutoProperty() => Verify.AnalyzerAsync(
        """
        class C1
        {
            CanCopy Property1 { get; set; }
            CanCopy Property2 { get => throw null; set => throw null; }
            CannotCopy {|#0:Property3|} { get; set; }
            CannotCopy Property4 { get => throw null; set => throw null; }
        }

        struct C2
        {
            CanCopy Property1 { get; set; }
            CanCopy Property2 { get => throw null; set => throw null; }
            CannotCopy {|#1:Property3|} { get; set; }
            CannotCopy Property4 { get => throw null; set => throw null; }
        }

        struct CanCopy { }
        [Singulink.CodeAnalysis.NonCopyable] struct CannotCopy { }
        namespace Singulink.CodeAnalysis { internal sealed class NonCopyableAttribute : System.Attribute { } }
        """,
        Verify.AutoProperty(0, "CannotCopy", "C1.Property3"),
        Verify.AutoProperty(1, "CannotCopy", "C2.Property3"));

    [TestMethod]
    public Task AllowCopyFromCollectionExpression() => new Verify.Test {
        LanguageVersion = LanguageVersion.CSharp12,
        TestCode = """
            using System;
            using System.Collections;
            using System.Collections.Generic;
            using System.Runtime.CompilerServices;

            [Singulink.CodeAnalysis.NonCopyable]
            [CollectionBuilder(typeof(MyCollection), nameof(Create))]
            partial struct MyCollection : IEnumerable<int>
            {
                public IEnumerator<int> GetEnumerator() => null;
                IEnumerator IEnumerable.GetEnumerator() => null;

                public static MyCollection Create(ReadOnlySpan<int> r) => throw null;
            }

            class C
            {
                void M()
                {
                    MyCollection m = [1, 2, 3];
                    m = [];
                }
            }

            namespace Singulink.CodeAnalysis { internal sealed class NonCopyableAttribute : System.Attribute { } }
            """,
    }.RunAsync();

    [TestMethod]
    public Task DoNotAllowCopyInCollectionExpressionElement() => new Verify.Test {
        LanguageVersion = LanguageVersion.CSharp12,
        TestCode = """
            using System;
            using System.Collections;
            using System.Collections.Generic;
            using System.Runtime.CompilerServices;

            [Singulink.CodeAnalysis.NonCopyable]
            struct S
            {
            }

            [CollectionBuilder(typeof(MyCollection), nameof(Create))]
            partial struct MyCollection : IEnumerable<S>
            {
                public IEnumerator<S> GetEnumerator() => throw null;
                IEnumerator IEnumerable.GetEnumerator() => throw null;

                public static MyCollection Create(ReadOnlySpan<S> r) => throw null;
            }

            class C
            {
                void M()
                {
                    S s = new();
                    MyCollection m = [{|#0:s|}, new S()];
                    S last = {|#1:s|};
                }
            }

            namespace Singulink.CodeAnalysis { internal sealed class NonCopyableAttribute : System.Attribute { } }
            """,
        ExpectedDiagnostics = { Verify.UsedAfterHandOff(1, "s", "S", "the collection took its value", 0) },
    }.RunAsync();
}
