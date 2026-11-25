using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using PrefixClassName.MsTest;
using Singulink.CodeAnalysis.Analyzers;

namespace Singulink.CodeAnalysis.Tests;

/// <summary>
/// Tests of the severity that a diagnostic is reported with. Under the current memory safety rules every rule is an ordinary warning,
/// wherever it is reported. Under the updated rules a copy in code and a wrong declaration are errors that cannot be suppressed, a copy
/// in code in an unsafe context is a warning, and the property NonCopyableErrorsAsWarnings makes the analyzer report as under the current
/// rules. SCA1025 is a warning or a message under both.
/// </summary>
/// <remarks>
/// The unsafe contexts are those of the C# language under the updated rules. Each place of the matrix below was checked against what
/// the compiler of the .NET 11 SDK accepts in an unsafe context.
/// </remarks>
[PrefixTestClass]
public class SeverityTests
{
    // A copy that every place of the matrix makes: a stored value passed by value.
    private const string Copy = "G.Take({|#N:G.Stored|})";

    private const string Declarations = """
        [Singulink.CodeAnalysis.NonCopyable]
        struct Guard { }

        static class G
        {
            public static Guard Stored;
            public static int Take(Guard guard) => 0;
        }

        class Base { public Base(int value) { } }
        """;

    /// <summary>
    /// The places of the matrix, each with whether it is in an unsafe context under the updated rules, and whether it needs the
    /// <c>unsafe(expr)</c> expression. X stands for the copy. An 'unsafe' modifier makes nothing unsafe under the updated rules, and the
    /// places that have one show it.
    /// </summary>
    private static readonly (string Code, bool IsUnsafe, bool NeedsUnsafeExpression)[] Places = [
        ("class C00 { void M() { _ = X; } }", false, false),
        ("class C01 { unsafe void M() { _ = X; } }", false, false),
        ("class C02 { void M() { unsafe { _ = X; } } }", true, false),
        ("unsafe class C03 { void M() { _ = X; } }", false, false),
        ("unsafe struct C04 { void M() { _ = X; } }", false, false),
        ("unsafe class C05 { class Inner { void M() { _ = X; } } }", false, false),
        ("unsafe partial class C06 { void M1() { _ = X; } }", false, false),
        ("partial class C06 { void M2() { _ = X; } }", false, false),
        ("class C08 { unsafe C08() { _ = X; } }", false, false),
        ("class C09 : Base { unsafe C09() : base(X) { } }", true, false),
        ("unsafe class C10 : Base { C10() : base(X) { } }", false, false),
        ("class C11 : Base { C11() : base(unsafe(X)) { } }", true, true),
        ("class C12 { static unsafe C12() { _ = X; } }", false, false),
        ("class C13 { unsafe ~C13() { _ = X; } }", false, false),
        ("class C14 { static unsafe int F = X; }", false, false),
        ("class C15 { static int F = X; }", false, false),
        ("unsafe class C16 { static int F = X; }", false, false),
        ("class C17 { static int F = unsafe(X); }", true, true),
        ("class C18 { unsafe int P => X; }", false, false),
        ("class C19 { unsafe int P { get { _ = X; return 0; } } }", false, false),
        ("class C20 { unsafe int P { get => 0; set { _ = X; } } }", false, false),
        ("class C21 { int P { unsafe get { _ = X; return 0; } } }", false, false),
        ("class C22 { unsafe int P { get; } = X; }", false, false),
        ("class C23 { unsafe int this[int i] => X; }", false, false),
        ("class C24 { public static unsafe C24 operator +(C24 a, C24 b) { _ = X; return a; } }", false, false),
        ("class C25 { unsafe event System.Action E { add { _ = X; } remove { } } }", false, false),
        ("class C26 { unsafe int M() => X; }", false, false),
        ("interface IC27 { unsafe void M() { _ = X; } }", false, false),
        ("unsafe class C28() : Base(X);", false, false),
        ("partial class C29 { unsafe partial void M(); unsafe partial void M() { _ = X; } }", false, false),
        ("class C30 { void M() { unsafe void L() { _ = X; } L(); } }", false, false),
        ("class C31 { unsafe void M() { void L() { _ = X; } L(); } }", false, false),
        ("class C32 { unsafe void M() { void L() { void L2() { _ = X; } L2(); } L(); } }", false, false),
        ("class C33 { void M() { unsafe { void L() { _ = X; } L(); } } }", true, false),
        ("class C34 { unsafe void M() { System.Action a = () => { _ = X; }; } }", false, false),
        ("class C35 { void M() { unsafe { System.Action a = () => { _ = X; }; } } }", true, false),
        ("class C36 { void M() { System.Func<int> a = () => X; } }", false, false),
        ("class C37 { unsafe void M() { System.Action a = delegate { _ = X; }; } }", false, false),
        ("class C38 { static unsafe System.Func<int> F = () => X; }", false, false),
        ("class C39 { void M() { System.Action a = () => { unsafe { _ = X; } }; } }", true, false),
        ("class C40 { unsafe System.Collections.Generic.IEnumerable<int> M() { yield return 1; _ = X; } }", false, false),
        ("unsafe class C41 { System.Collections.Generic.IEnumerable<int> M() { yield return 1; _ = X; } }", false, false),
        ("class C42 { System.Collections.Generic.IEnumerable<int> M() { yield return 1; unsafe { _ = X; } } }", true, false),
        ("class C43 { unsafe System.Collections.Generic.IEnumerable<int> M() { yield return 1; void L() { _ = X; } L(); } }", false, false),
        ("class C44 { unsafe System.Collections.Generic.IEnumerable<int> M() { yield return 1; System.Action a = () => { _ = X; }; } }", false, false),
        ("class C45 { unsafe void M() { System.Collections.Generic.IEnumerable<int> L() { yield return 1; _ = X; } L(); } }", false, false),
        ("class C46 { void M() { unsafe { System.Collections.Generic.IEnumerable<int> L() { yield return 1; _ = X; } L(); } } }", false, false),
        ("class C47 { void M() { unsafe System.Collections.Generic.IEnumerable<int> L() { yield return 1; _ = X; } L(); } }", false, false),
        ("class C48 { unsafe System.Collections.Generic.IEnumerable<int> P { get { yield return 1; _ = X; } } }", false, false),
        ("class C49 { unsafe async System.Threading.Tasks.Task M() { _ = X; await System.Threading.Tasks.Task.Yield(); } }", false, false),
        ("class C50 { void M() { _ = unsafe(X); } }", true, true),
        ("class C51 { void M() { System.Func<int> f = unsafe(() => X); } }", true, true),
        ("class C52 { void M() { try { } catch when (unsafe(X) == 0) { } } }", true, true),
        ("class C53 { unsafe int M() => unsafe(X); }", true, true),
    ];

    private static bool CompilerHasUnsafeExpression => Enum.TryParse<SyntaxKind>("UnsafeExpression", out _);

    [TestMethod]
    public Task UnsafeContext_UnderTheUpdatedRulesIsThatOfTheLanguage()
    {
        if (!Verify.CompilerHasUpdatedRules)
            Assert.Inconclusive("The compiler of this target does not apply the updated memory safety rules.");

        return RunMatrixAsync(Places.Select(p => (p.Code, p.IsUnsafe)), LanguageVersion.Preview, updatedRules: true);
    }

    // Under the current rules the place plays no part: an 'unsafe' modifier, an 'unsafe' block and an unsafe(expr) expression give the
    // ordinary warning as everywhere else.
    [TestMethod]
    public Task CurrentRules_EveryPlaceGetsTheOrdinaryWarning() => RunMatrixAsync(
        Places.Where(p => !p.NeedsUnsafeExpression || CompilerHasUnsafeExpression).Select(p => (p.Code, false)),
        CompilerHasUnsafeExpression ? LanguageVersion.Preview : LanguageVersion.Latest,
        updatedRules: false);

    // A compiler that does not know the updated rules takes the flag and applies the current rules, and so does the analyzer.
    [TestMethod]
    public Task CurrentRules_WithTheFlagInACompilerWithoutTheUpdatedRules()
    {
        if (Verify.CompilerHasUpdatedRules)
            Assert.Inconclusive("The compiler of this target applies the updated memory safety rules.");

        return RunMatrixAsync(Places.Where(p => !p.NeedsUnsafeExpression).Select(p => (p.Code, false)), LanguageVersion.Preview, updatedRules: true);
    }

    // The analyzer reads the rules from the module of the compilation, or from the feature flag where the compiler has no such property.
    [TestMethod]
    public void UpdatedRules_AreReadFromTheCompilation()
    {
        Assert.IsFalse(Verify.CompilerHasUpdatedRules && !UsesUpdatedRules(Verify.CreateUpdatedRulesProbe(withFlag: true)), "The analyzer misses the updated rules.");
        Assert.IsFalse(UsesUpdatedRules(Verify.CreateUpdatedRulesProbe(withFlag: false)), "The analyzer finds updated rules that are not on.");

        static bool UsesUpdatedRules(Compilation compilation) => (bool)typeof(DoNotCopyValueAnalyzer).Assembly
            .GetType("Singulink.CodeAnalysis.Analyzers.UnsafeContext", throwOnError: true)!
            .GetMethod("UsesUpdatedRules")!
            .Invoke(null, [compilation])!;
    }

    // Under the current rules '#pragma' and [SuppressMessage] suppress a copy in code and a wrong declaration, in an unsafe context and
    // outside one.
    [TestMethod]
    public Task CurrentRules_PragmaAndAttributeSuppressEveryReport() => Verify.AnalyzerAsync(
        $$"""
        using System.Diagnostics.CodeAnalysis;

        class C
        {
            Guard _guard;

            void Reported()
            {
                var copy = {|#0:_guard|};
            }

            void Pragma()
            {
        #pragma warning disable SCA1001 // Deliberate: the test shows that '#pragma' suppresses the warning
                var copy = _guard;
        #pragma warning restore SCA1001
            }

            [SuppressMessage("Reliability", "SCA1001:Do not copy a non-copyable value by assignment", Justification = "Deliberate")]
            void Attribute()
            {
                var copy = _guard;
            }

            unsafe void UnsafeReported()
            {
                unsafe
                {
                    var copy = {|#1:_guard|};
                }
            }
        }

        struct WithPragma
        {
        #pragma warning disable SCA1010 // Deliberate: the test shows that '#pragma' suppresses the warning
            Guard _guard;
        #pragma warning restore SCA1010
        }

        [SuppressMessage("Reliability", "SCA1010", Justification = "Deliberate")]
        struct WithAttribute
        {
            Guard _guard;
        }

        unsafe struct InUnsafeStruct
        {
            Guard {|#2:_guard|};
        }

        {{Sources.Guard}}
        """,
        Verify.CopyByAssignment(0, "Guard"),
        Verify.CopyByAssignment(1, "Guard"),
        Verify.FieldOfCopyableType(2, "Guard", "InUnsafeStruct._guard"));

    [TestMethod]
    public Task CurrentRules_NoWarnSuppressesEveryReport() => new Verify.Test {
        TestCode = $$"""
            class C
            {
                Guard _guard;

                void Copy()
                {
                    var copy = _guard;
                }
            }

            struct S
            {
                Guard _guard;
            }

            {{Sources.Guard}}
            """,
        NoWarn = [DiagnosticIds.CopyByAssignment, DiagnosticIds.FieldOfCopyableType],
    }.RunAsync();

    // The ordinary warning of an ID can be configured, though another descriptor of the same ID, the error of the updated rules, is not
    // configurable.
    [TestMethod]
    [DataRow("suggestion", DiagnosticSeverity.Info)]
    [DataRow("none", null)]
    public Task CurrentRules_TheSeverityOfAnIdCanBeConfigured(string configured, DiagnosticSeverity? severity)
    {
        var descriptors = new DoNotCopyValueAnalyzer().SupportedDiagnostics.Where(d => d.Id == DiagnosticIds.CopyByAssignment).ToList();
        Assert.IsTrue(descriptors.Any(d => d.CustomTags.Contains(WellKnownDiagnosticTags.NotConfigurable)), "SCA1001 has no descriptor that is not configurable.");

        var test = new Verify.Test {
            TestCode = $$"""
                class C
                {
                    Guard _guard;

                    void Copy()
                    {
                        var copy = {|#0:_guard|};
                    }
                }

                {{Sources.Guard}}
                """,
        };

        test.TestState.AnalyzerConfigFiles.Add(("/.editorconfig", $"root = true\n\n[*.cs]\ndotnet_diagnostic.SCA1001.severity = {configured}\n"));

        if (severity is { } expected)
            test.ExpectedDiagnostics.Add(Verify.CopyByAssignment(0, "Guard").WithSeverity(expected));

        return test.RunAsync();
    }

    // A warning made an error by the options of the project is one. The testing library shows such a diagnostic as the warning that it
    // was declared as, so the compilation is asked directly.
    [TestMethod]
    public async Task CurrentRules_TheOrdinaryWarningCanBeMadeAnError()
    {
        const string Source = "class C { Guard _g; void M() { var c = _g; } } [Singulink.CodeAnalysis.NonCopyable] struct Guard { }";
        var references = await Verify.Test.NetReferenceAssemblies.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
            .WithSpecificDiagnosticOptions([new(DiagnosticIds.CopyByAssignment, ReportDiagnostic.Error)]);

        var compilation = CSharpCompilation.Create(
            "Configured",
            [CSharpSyntaxTree.ParseText(Source, new CSharpParseOptions(LanguageVersion.Latest))],
            references.Add(MetadataReference.CreateFromFile(typeof(NonCopyableAttribute).Assembly.Location)),
            options);

        var found = await compilation.WithAnalyzers([new DoNotCopyValueAnalyzer()]).GetAnalyzerDiagnosticsAsync(CancellationToken.None);

        Assert.HasCount(1, found);
        Assert.AreEqual(DiagnosticSeverity.Error, found[0].Severity);
        Assert.IsTrue(found[0].IsWarningAsError);
    }

    // The report of every rule carries the ordinary descriptor under the current rules: no tag that keeps a suppression off it.
    [TestMethod]
    public async Task CurrentRules_EveryReportHasTheOrdinaryDescriptor()
    {
        const string Source = """
            class C
            {
                Guard _guard;

                void Copy() => Take(_guard);

                unsafe void InUnsafeCode()
                {
                    unsafe { Take(_guard); }
                }

                static void Take(Guard guard) { }
            }

            struct S
            {
                Guard _guard;
            }

            [Singulink.CodeAnalysis.NonCopyable]
            struct Guard { }
            """;

        var references = await Verify.Test.NetReferenceAssemblies.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);

        var compilation = CSharpCompilation.Create(
            "Ordinary",
            [CSharpSyntaxTree.ParseText(Source, new CSharpParseOptions(LanguageVersion.Latest))],
            references.Add(MetadataReference.CreateFromFile(typeof(NonCopyableAttribute).Assembly.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));

        var found = await compilation.WithAnalyzers([new DoNotCopyValueAnalyzer()]).GetAnalyzerDiagnosticsAsync(CancellationToken.None);

        Assert.HasCount(3, found);

        foreach (var diagnostic in found)
        {
            Assert.AreEqual(DiagnosticSeverity.Warning, diagnostic.Severity, diagnostic.ToString());
            Assert.DoesNotContain(WellKnownDiagnosticTags.Compiler, diagnostic.Descriptor.CustomTags, diagnostic.ToString());
            Assert.DoesNotContain(WellKnownDiagnosticTags.NotConfigurable, diagnostic.Descriptor.CustomTags, diagnostic.ToString());
        }
    }

    // The property changes nothing under the current rules: it makes the analyzer report as under the current rules.
    [TestMethod]
    public Task CurrentRules_ErrorsAsWarningsChangesNothing() => new Verify.Test {
        TestCode = $$"""
            class C
            {
                Guard _guard;

                void Copy()
                {
                    var copy = {|#0:_guard|};
                }
            }

            struct S
            {
                Guard {|#1:_guard|};
            }

            {{Sources.Guard}}
            """,
        ExpectedDiagnostics = {
            Verify.CopyByAssignment(0, "Guard"),
            Verify.FieldOfCopyableType(1, "Guard", "S._guard"),
        },
    }.WithBuildProperty("NonCopyableErrorsAsWarnings", "true").RunAsync();

    [TestMethod]
    public Task UpdatedRules_CopyInCodeIsAnErrorOutsideAnUnsafeContextAndAWarningInOne() => Verify.UpdatedRulesAsync(
        $$"""
        class C
        {
            Guard _guard;

            void Safe()
            {
                var copy = {|#0:_guard|};
            }

            unsafe void UnsafeModifier()
            {
                var copy = {|#1:_guard|};
            }

            void UnsafeBlock()
            {
                unsafe
                {
                    var copy = {|#2:_guard|};
                }
            }

            void UnsafeExpression()
            {
                var copy = unsafe({|#3:_guard|});
            }
        }

        {{Sources.Guard}}
        """,
        Verify.CopyByAssignment(0, "Guard").AsError(),
        Verify.CopyByAssignment(1, "Guard").AsError(),
        Verify.CopyByAssignment(2, "Guard"),
        Verify.CopyByAssignment(3, "Guard"));

    // Neither '#pragma' nor [SuppressMessage] suppresses the error. '#pragma' suppresses the warning in an unsafe context, and
    // [SuppressMessage] does not.
    [TestMethod]
    public Task UpdatedRules_SuppressionLeavesTheErrorAndSuppressesTheWarningByPragmaOnly() => Verify.UpdatedRulesAsync(
        $$"""
        using System.Diagnostics.CodeAnalysis;

        class C
        {
            Guard _guard;

            void Pragma()
            {
        #pragma warning disable SCA1001
                var copy = {|#0:_guard|};
        #pragma warning restore SCA1001
            }

            [SuppressMessage("Reliability", "SCA1001:Do not copy a non-copyable value by assignment")]
            void Attribute()
            {
                var copy = {|#1:_guard|};
            }

            void UnsafePragma()
            {
        #pragma warning disable SCA1001 // Deliberate: the test shows that '#pragma' suppresses the warning
                var copy = unsafe(_guard);
        #pragma warning restore SCA1001
            }

            [SuppressMessage("Reliability", "SCA1001:Do not copy a non-copyable value by assignment")]
            void UnsafeAttribute()
            {
                var copy = unsafe({|#2:_guard|});
            }
        }

        {{Sources.Guard}}
        """,
        Verify.CopyByAssignment(0, "Guard").AsError(),
        Verify.CopyByAssignment(1, "Guard").AsError(),
        Verify.CopyByAssignment(2, "Guard"));

    // NoWarn does not suppress the error, which is not configurable, and suppresses the warning in an unsafe context.
    [TestMethod]
    public Task UpdatedRules_NoWarnLeavesTheErrorAndSuppressesTheWarning()
    {
        if (!Verify.CompilerHasUpdatedRules)
            Assert.Inconclusive("The compiler of this target does not apply the updated memory safety rules.");

        return new Verify.Test {
            TestCode = $$"""
                class C
                {
                    Guard _guard;

                    void Safe()
                    {
                        var copy = {|#0:_guard|};
                    }

                    void Unsafe()
                    {
                        var copy = unsafe(_guard);
                    }
                }

                {{Sources.Guard}}
                """,
            LanguageVersion = LanguageVersion.Preview,
            Features = [Verify.UpdatedRulesFeature],
            NoWarn = [DiagnosticIds.CopyByAssignment],
            ExpectedDiagnostics = { Verify.CopyByAssignment(0, "Guard").AsError() },
        }.RunAsync();
    }

    // Severity configuration, in an .editorconfig or a .globalconfig file, does not reach the error, which is not configurable, and
    // turns the warning in an unsafe context off, as NoWarn does.
    [TestMethod]
    [DataRow("/.editorconfig", "root = true\n\n[*.cs]\n")]
    [DataRow("/.globalconfig", "is_global = true\n")]
    public Task UpdatedRules_SeverityConfigurationLeavesTheErrorAndTurnsOffTheWarning(string path, string header)
    {
        if (!Verify.CompilerHasUpdatedRules)
            Assert.Inconclusive("The compiler of this target does not apply the updated memory safety rules.");

        var test = new Verify.Test {
            TestCode = $$"""
                class C
                {
                    Guard _guard;

                    void Safe()
                    {
                        var copy = {|#0:_guard|};
                    }

                    void Unsafe()
                    {
                        var copy = unsafe(_guard);
                    }
                }

                {{Sources.Guard}}
                """,
            LanguageVersion = LanguageVersion.Preview,
            Features = [Verify.UpdatedRulesFeature],
            ExpectedDiagnostics = { Verify.CopyByAssignment(0, "Guard").AsError() },
        };

        test.TestState.AnalyzerConfigFiles.Add((path, header + $"dotnet_diagnostic.{DiagnosticIds.CopyByAssignment}.severity = none\n"));
        return test.RunAsync();
    }

    // A wrong declaration is an error that neither '#pragma' nor [SuppressMessage] suppresses.
    [TestMethod]
    public Task UpdatedRules_DeclarationIsAnError() => Verify.UpdatedRulesAsync(
        $$"""
        using System.Diagnostics.CodeAnalysis;

        struct Plain
        {
            Guard {|#0:_guard|};
        }

        struct WithPragma
        {
        #pragma warning disable SCA1010
            Guard {|#1:_guard|};
        #pragma warning restore SCA1010
        }

        [SuppressMessage("Reliability", "SCA1010")]
        struct WithAttribute
        {
            Guard {|#2:_guard|};
        }

        {{Sources.Guard}}
        """,
        Verify.FieldOfCopyableType(0, "Guard", "Plain._guard").AsError(),
        Verify.FieldOfCopyableType(1, "Guard", "WithPragma._guard").AsError(),
        Verify.FieldOfCopyableType(2, "Guard", "WithAttribute._guard").AsError());

    // The place of a report decides, not its ID. A rule that mostly reports at declarations is a warning where it reports in an unsafe
    // context, and a rule that mostly reports in code is an error at a declaration.
    [TestMethod]
    public Task UpdatedRules_PlaceDecidesAndNotTheId() => Verify.UpdatedRulesAsync(
        $$"""
        using System.Collections.Generic;

        class Holder
        {
            private List<Guard> _all = new();

            void Keeps(List<Guard> list) => _all = {|#0:list|};

            void KeepsInUnsafeCode(List<Guard> list)
            {
                unsafe { _all = {|#1:list|}; }
            }
        }

        abstract class ItemsBase
        {
            public abstract object GetItems();
        }

        class Items : ItemsBase
        {
            public override Guard[] {|#2:GetItems|}() => new Guard[4];
        }

        {{Sources.Guard}}
        """,
        Verify.KeepsUnmarkedParameter(0, "Holder.Keeps(List<Guard>)", "list", "System.Collections.Generic.List<Guard>").AsError(),
        Verify.KeepsUnmarkedParameter(1, "Holder.KeepsInUnsafeCode(List<Guard>)", "list", "System.Collections.Generic.List<Guard>"),
        Verify.OpaqueReturn(2, "Guard[]", "object", "Items.GetItems()", "ItemsBase.GetItems()").AsError());

    // SCA1047 is a plain warning under both rule sets, which its author suppresses after checking the method: '#pragma' and
    // [SuppressMessage] both do.
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task NotNullRefNotChecked_IsSuppressedByPragmaAndByAttribute(bool updatedRules)
    {
        const string Source = """
            using System.Diagnostics.CodeAnalysis;
            using Singulink.CodeAnalysis;

            static class Checks
            {
                public static void Reported([{|#0:NotNullRef|}] in int value) { }

            #pragma warning disable SCA1047 // Verified: the test needs a suppressed one
                public static void Pragma([NotNullRef] in int value) { }
            #pragma warning restore SCA1047

                [SuppressMessage("Reliability", "SCA1047", Justification = "Verified: the test needs a suppressed one")]
                public static void Attribute([NotNullRef] in int value) { }
            }
            """;

        var expected = Verify.NotNullRefNotChecked(0, "value", "Checks.Reported(in int)");
        return updatedRules ? Verify.UpdatedRulesAsync(Source, expected) : Verify.AnalyzerAsync(Source, expected);
    }

    // The property makes the analyzer report as under the current rules: '#pragma' and [SuppressMessage] suppress a copy in code and a
    // wrong declaration, and an unsafe context plays no part, where [SuppressMessage] suppresses as well.
    [TestMethod]
    public Task UpdatedRules_ErrorsAsWarningsLetsPragmaAndAttributeSuppressEveryReport()
    {
        if (!Verify.CompilerHasUpdatedRules)
            Assert.Inconclusive("The compiler of this target does not apply the updated memory safety rules.");

        return new Verify.Test {
            TestCode = $$"""
                using System.Diagnostics.CodeAnalysis;

                class C
                {
                    Guard _guard;

                    void Copy()
                    {
                        var copy = {|#0:_guard|};
                    }

                    void Pragma()
                    {
                #pragma warning disable SCA1001 // Deliberate: the test shows that '#pragma' suppresses the warning
                        var copy = _guard;
                #pragma warning restore SCA1001
                    }

                    [SuppressMessage("Reliability", "SCA1001:Do not copy a non-copyable value by assignment", Justification = "Deliberate")]
                    void Attribute()
                    {
                        var copy = _guard;
                    }

                    void UnsafeExpression()
                    {
                        var copy = unsafe({|#1:_guard|});
                    }

                    [SuppressMessage("Reliability", "SCA1001:Do not copy a non-copyable value by assignment", Justification = "Deliberate")]
                    void UnsafeAttribute()
                    {
                        var copy = unsafe(_guard);
                    }
                }

                struct S
                {
                    Guard {|#2:_guard|};
                }

                struct WithPragma
                {
                #pragma warning disable SCA1010 // Deliberate: the test shows that '#pragma' suppresses the warning
                    Guard _guard;
                #pragma warning restore SCA1010
                }

                [SuppressMessage("Reliability", "SCA1010", Justification = "Deliberate")]
                struct WithAttribute
                {
                    Guard _guard;
                }

                {{Sources.Guard}}
                """,
            LanguageVersion = LanguageVersion.Preview,
            Features = [Verify.UpdatedRulesFeature],
            ExpectedDiagnostics = {
                Verify.CopyByAssignment(0, "Guard"),
                Verify.CopyByAssignment(1, "Guard"),
                Verify.FieldOfCopyableType(2, "Guard", "S._guard"),
            },
        }.WithBuildProperty("NonCopyableErrorsAsWarnings", "true").RunAsync();
    }

    // With the property the severity of an ID can be configured, in code and at a declaration.
    [TestMethod]
    [DataRow("suggestion", DiagnosticSeverity.Info)]
    [DataRow("none", null)]
    public Task UpdatedRules_ErrorsAsWarningsLetsTheSeverityOfAnIdBeConfigured(string configured, DiagnosticSeverity? severity)
    {
        if (!Verify.CompilerHasUpdatedRules)
            Assert.Inconclusive("The compiler of this target does not apply the updated memory safety rules.");

        var test = new Verify.Test {
            TestCode = $$"""
                class C
                {
                    Guard _guard;

                    void Copy()
                    {
                        var copy = {|#0:_guard|};
                    }
                }

                struct S
                {
                    Guard {|#1:_guard|};
                }

                {{Sources.Guard}}
                """,
            LanguageVersion = LanguageVersion.Preview,
            Features = [Verify.UpdatedRulesFeature],
        }.WithBuildProperty("NonCopyableErrorsAsWarnings", "true");

        test.TestState.AnalyzerConfigFiles.Add((
            "/.editorconfig",
            $"root = true\n\n[*.cs]\ndotnet_diagnostic.SCA1001.severity = {configured}\ndotnet_diagnostic.SCA1010.severity = {configured}\n"));

        if (severity is { } expected)
        {
            test.ExpectedDiagnostics.Add(Verify.CopyByAssignment(0, "Guard").WithSeverity(expected));
            test.ExpectedDiagnostics.Add(Verify.FieldOfCopyableType(1, "Guard", "S._guard").WithSeverity(expected));
        }

        return test.RunAsync();
    }

    // With the property every report is the one of the current rules: the same place, the same ordinary descriptor with no tag that
    // keeps a suppression off it, and the same message, which for some rules differs under the updated rules.
    [TestMethod]
    public async Task UpdatedRules_ErrorsAsWarningsReportsAsTheCurrentRules()
    {
        if (!Verify.CompilerHasUpdatedRules)
            Assert.Inconclusive("The compiler of this target does not apply the updated memory safety rules.");

        const string Source = """
            class C
            {
                Guard _guard;

                void Copy() => Take(_guard);

                void InUnsafeCode()
                {
                    unsafe { Take(_guard); }
                }

                static void Take(Guard guard) { }
            }

            struct S
            {
                Guard _guard;
            }

            sealed class GuardedCulture : System.Globalization.CultureInfo
            {
                private Guard _guard;

                public GuardedCulture() : base("") { }
            }

            [Singulink.CodeAnalysis.NonCopyable]
            struct Guard { }
            """;

        var current = await AnalyzeAsync(updatedRules: false, errorsAsWarnings: false);
        var updated = await AnalyzeAsync(updatedRules: true, errorsAsWarnings: false);
        var updatedAsWarnings = await AnalyzeAsync(updatedRules: true, errorsAsWarnings: true);

        Assert.HasCount(4, current);
        CollectionAssert.AreEqual(Describe(current), Describe(updatedAsWarnings));

        // Without the property the same reports differ, by severity and by the message of SCA1009, so the comparison is not empty.
        Assert.IsTrue(updated.Any(d => d.Severity is DiagnosticSeverity.Error), "No report is an error under the updated rules.");
        Assert.AreNotEqual(
            current.Single(d => d.Id == DiagnosticIds.CloningBase).GetMessage(System.Globalization.CultureInfo.InvariantCulture),
            updated.Single(d => d.Id == DiagnosticIds.CloningBase).GetMessage(System.Globalization.CultureInfo.InvariantCulture));

        foreach (var diagnostic in updatedAsWarnings)
        {
            Assert.AreEqual(DiagnosticSeverity.Warning, diagnostic.Severity, diagnostic.ToString());
            Assert.DoesNotContain(WellKnownDiagnosticTags.Compiler, diagnostic.Descriptor.CustomTags, diagnostic.ToString());
            Assert.DoesNotContain(WellKnownDiagnosticTags.NotConfigurable, diagnostic.Descriptor.CustomTags, diagnostic.ToString());
        }

        async Task<IReadOnlyList<Diagnostic>> AnalyzeAsync(bool updatedRules, bool errorsAsWarnings)
        {
            var references = await Verify.Test.NetReferenceAssemblies.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
            var parseOptions = updatedRules
                ? new CSharpParseOptions(LanguageVersion.Preview).WithFeatures([new(Verify.UpdatedRulesFeature, "true")])
                : new CSharpParseOptions(LanguageVersion.Latest);

            var compilation = CSharpCompilation.Create(
                "Reported",
                [CSharpSyntaxTree.ParseText(Source, parseOptions)],
                references.Add(MetadataReference.CreateFromFile(typeof(NonCopyableAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));

            var properties = errorsAsWarnings
                ? new Dictionary<string, string> { ["build_property.NonCopyableErrorsAsWarnings"] = "true" }
                : [];

            var options = new AnalyzerOptions([], new GlobalOptionsProvider(properties));
            return await compilation.WithAnalyzers([new DoNotCopyValueAnalyzer()], options).GetAnalyzerDiagnosticsAsync(CancellationToken.None);
        }

        static List<string> Describe(IEnumerable<Diagnostic> diagnostics) => [
            .. diagnostics
                .Select(d => $"{d.Id} {d.Location.SourceSpan} {d.Severity} {string.Join(",", d.Descriptor.CustomTags)} " +
                    d.GetMessage(System.Globalization.CultureInfo.InvariantCulture))
                .Order(StringComparer.Ordinal),
        ];
    }

    [TestMethod]
    public Task UpdatedRules_ErrorsAsWarningsLetsNoWarnSuppressTheWarnings()
    {
        if (!Verify.CompilerHasUpdatedRules)
            Assert.Inconclusive("The compiler of this target does not apply the updated memory safety rules.");

        return new Verify.Test {
            TestCode = $$"""
                class C
                {
                    Guard _guard;

                    void Copy()
                    {
                        var copy = _guard;
                    }
                }

                struct S
                {
                    Guard _guard;
                }

                {{Sources.Guard}}
                """,
            LanguageVersion = LanguageVersion.Preview,
            Features = [Verify.UpdatedRulesFeature],
            NoWarn = [DiagnosticIds.CopyByAssignment, DiagnosticIds.FieldOfCopyableType],
        }.WithBuildProperty("NonCopyableErrorsAsWarnings", "TRUE ").RunAsync();
    }

    // Any other value leaves the errors.
    [TestMethod]
    public Task UpdatedRules_ErrorsAsWarningsOtherThanTrueLeavesTheErrors()
    {
        if (!Verify.CompilerHasUpdatedRules)
            Assert.Inconclusive("The compiler of this target does not apply the updated memory safety rules.");

        return new Verify.Test {
            TestCode = $$"""
                class C
                {
                    Guard _guard;

                    void Copy()
                    {
                        var copy = {|#0:_guard|};
                    }
                }

                {{Sources.Guard}}
                """,
            LanguageVersion = LanguageVersion.Preview,
            Features = [Verify.UpdatedRulesFeature],
            ExpectedDiagnostics = { Verify.CopyByAssignment(0, "Guard").AsError() },
        }.WithBuildProperty("NonCopyableErrorsAsWarnings", "false").RunAsync();
    }

    // SCA1025 is a warning in a project that references a package of the analyzer itself, and in a build that does not say, and a
    // message in a project that only gets the analyzer through another package.
    [TestMethod]
    [DataRow(null, DiagnosticSeverity.Warning)]
    [DataRow("true", DiagnosticSeverity.Warning)]
    [DataRow("false", DiagnosticSeverity.Info)]
    public async Task UncheckedAssembly_IsAMessageWhereTheProjectReferencesNoPackage(string? referencesPackage, DiagnosticSeverity severity)
    {
        var guards = await Verify.CompileLibraryAsync(
            Verify.Test.NetReferenceAssemblies, "namespace Guards { [Singulink.CodeAnalysis.NonCopyable] public struct Guard { } }", analyzed: true, "Guards");

        var plugin = await Verify.CompileLibraryAsync(
            Verify.Test.NetReferenceAssemblies, "namespace Plugins { public class Plugin { public void Use(Guards.Guard guard) { } } }", analyzed: false, "Plugin", guards);

        var test = new Verify.Test { TestCode = "class C { }" }.WithBuildProperty("_NonCopyableReferencesPackage", referencesPackage);
        test.TestState.AdditionalReferences.Add(guards);
        test.TestState.AdditionalReferences.Add(plugin);
        test.ExpectedDiagnostics.Add(Verify.UncheckedAssembly("Plugin", "Guards", "Guards.Guard").WithSeverity(severity));
        await test.RunAsync();
    }

    // Each ID has the descriptors of its group: for a copy in code and a wrong declaration the ordinary warning of the current rules, and
    // under the updated rules an error that is not configurable and a warning, both with the tag that keeps [SuppressMessage] off them; a
    // warning and a message for SCA1025; a plain warning for the rest.
    [TestMethod]
    public void Descriptors_HaveTheSeveritiesOfTheirGroup()
    {
        foreach (var group in new DoNotCopyValueAnalyzer().SupportedDiagnostics.GroupBy(d => d.Id))
        {
            string id = group.Key;

            if (Verify.EnforcedIds.Contains(id))
            {
                var errors = group.Where(d => d.DefaultSeverity is DiagnosticSeverity.Error).ToList();
                var unsafeWarnings = group.Where(d => d.DefaultSeverity is DiagnosticSeverity.Warning && d.CustomTags.Contains(WellKnownDiagnosticTags.Compiler)).ToList();
                var ordinary = group.Where(d => d.DefaultSeverity is DiagnosticSeverity.Warning && !d.CustomTags.Contains(WellKnownDiagnosticTags.Compiler)).ToList();

                Assert.IsNotEmpty(errors, id);
                Assert.HasCount(errors.Count, unsafeWarnings, id);
                Assert.HasCount(errors.Count, ordinary, id);
                Assert.AreEqual(group.Count(), errors.Count * 3, id);

                foreach (var error in errors)
                    CollectionAssert.IsSubsetOf(new[] { WellKnownDiagnosticTags.NotConfigurable, WellKnownDiagnosticTags.Compiler }, error.CustomTags.ToArray(), id);

                foreach (var warning in unsafeWarnings.Concat(ordinary))
                    Assert.DoesNotContain(WellKnownDiagnosticTags.NotConfigurable, warning.CustomTags, id);
            }
            else
            {
                var bySeverity = group.ToLookup(d => d.DefaultSeverity);
                Assert.IsEmpty(bySeverity[DiagnosticSeverity.Error], id);

                if (id is DiagnosticIds.UncheckedAssembly)
                    Assert.AreEqual(bySeverity[DiagnosticSeverity.Warning].Count(), bySeverity[DiagnosticSeverity.Info].Count(), id);
                else
                    Assert.IsEmpty(bySeverity[DiagnosticSeverity.Info], id);

                foreach (var descriptor in group)
                {
                    Assert.DoesNotContain(WellKnownDiagnosticTags.Compiler, descriptor.CustomTags, id);
                    Assert.DoesNotContain(WellKnownDiagnosticTags.NotConfigurable, descriptor.CustomTags, id);
                }
            }
        }
    }

    private static async Task RunMatrixAsync(IEnumerable<(string Code, bool IsUnsafe)> places, LanguageVersion languageVersion, bool updatedRules)
    {
        var source = new StringBuilder(Declarations);
        var expected = new List<DiagnosticResult>();
        bool appliesUpdatedRules = updatedRules && Verify.CompilerHasUpdatedRules;

        foreach (var (code, isUnsafe) in places)
        {
            int location = expected.Count;
            source.AppendLine().AppendLine(code.Replace("X", Copy.Replace("N", location.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal), StringComparison.Ordinal));

            var result = Verify.CopyOfStoredValue(location, "'G.Take(Guard)' receives", "Guard");
            expected.Add(appliesUpdatedRules && !isUnsafe ? result.AsError() : result);
        }

        var test = new Verify.Test {
            TestCode = source.ToString(),
            LanguageVersion = languageVersion,
            Features = updatedRules ? [Verify.UpdatedRulesFeature] : [],

            // Some places are rejected by the compiler of a target or under a version of the rules, such as an 'unsafe' type under the
            // updated rules: the analyzer judges what is written.
            CompilerDiagnostics = CompilerDiagnostics.None,
        };

        test.ExpectedDiagnostics.AddRange(expected);
        await test.RunAsync();
    }

    // The properties of a project as the analyzer reads them from the options of the compiler.
    private sealed class GlobalOptionsProvider(IReadOnlyDictionary<string, string> values) : AnalyzerConfigOptionsProvider
    {
        public override AnalyzerConfigOptions GlobalOptions { get; } = new Options(values);

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => GlobalOptions;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => GlobalOptions;

        private sealed class Options(IReadOnlyDictionary<string, string> values) : AnalyzerConfigOptions
        {
            public override bool TryGetValue(string key, out string value) => values.TryGetValue(key, out value!);
        }
    }
}
