using System.Text;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;
using PrefixClassName.MsTest;

namespace Singulink.CodeAnalysis.Tests;

/// <summary>
/// Tests of code that is deep or long: the analyzer must get through it, or report the one member that is too deep for it (SCA1048) and go
/// on with the next, and must never run out of stack, which would end the compiler's process.
/// </summary>
/// <remarks>
/// <para>
/// How deep the analyzer gets before it gives up depends on how much stack the thread that runs it has, and on how large the frames of
/// its methods are, which changes as the JIT compiles them again with optimizations. The testing library runs the analysis on whatever
/// thread it is given, so each test here runs the analyzer on a thread of its own with a fixed stack (<see cref="FixedStackAnalyzer"/>): a
/// small one where the member must be too deep, and a large one where the analyzer must get through.</para>
/// <para>
/// The compiler makes the operations of a member before the analyzer is called, on a thread of the testing library, and it makes those of
/// a conditional expression and of a chain of calls by recursion. So those forms stay well below the depth at which the compiler runs
/// out of stack itself: about 1,000 conditionals or 950 calls on a thread of the thread pool on Windows, and a run on another machine
/// has run out at 850 conditionals. On the small stack, of 192 KB, the analyzer gave up after at most 217 conditionals and 99 calls with
/// every method fully optimized, the most that it reaches, and after 20 and 24 before the JIT optimized it, on .NET 10 and .NET 11 alike.
/// A sum of binary operators gets further, since the visit of each of its levels keeps little on the stack, and it is one form that the
/// compiler makes without recursion, so it can be as long as a test likes.</para>
/// </remarks>
[PrefixTestClass]
public class DeepCodeTests
{
    // A stack on which the analyzer gives up on each deep form of these tests, however its methods were compiled. A request for less got
    // a thread with the same stack on Windows.
    private const int SmallStack = 192 * 1024;

    // A stack on which the analyzer gets through each long form of these tests, however its methods were compiled.
    private const int LargeStack = 16 * 1024 * 1024;

    // About twice the most that the analyzer reaches on the small stack, and half of the least at which the compiler has run out.
    private const int CallsInChain = 400;
    private const int Conditionals = 450;

    [TestMethod]
    public Task ElseIfChain_WithNoNonCopyableTypeIsAnalyzedWithoutAnException()
    {
        // Each 'else if' is a conditional inside the one before it. Nothing of the analyzer may walk the rest of the chain at every level.
        var source = new StringBuilder();
        source.AppendLine("class C");
        source.AppendLine("{");
        source.AppendLine("    static int M(int x)");
        source.AppendLine("    {");

        for (int i = 0; i < 1500; i++)
            source.AppendLine($"        {(i == 0 ? string.Empty : "else ")}if (x == {i}) return {i};");

        source.AppendLine("        return -1;");
        source.AppendLine("    }");
        source.AppendLine("}");

        return AnalyzeAsync(LargeStack, source.ToString());
    }

    [TestMethod]
    public Task ElseIfChain_WithGuardsIsAnalyzed()
    {
        // The same with a non-copyable value in every branch: each read of the field is reported, and nothing else happens.
        const int Branches = 300;
        var source = new StringBuilder();
        source.AppendLine("class C");
        source.AppendLine("{");
        source.AppendLine("    private Guard _guard;");
        source.AppendLine();
        source.AppendLine("    void M(int x)");
        source.AppendLine("    {");

        for (int i = 0; i < Branches; i++)
            source.AppendLine($"        {(i == 0 ? string.Empty : "else ")}if (x == {i}) {{ Guard copy = {{|#{i}:_guard|}}; }}");

        source.AppendLine("    }");
        source.AppendLine("}");
        source.AppendLine();
        source.AppendLine(Sources.Guard);

        return AnalyzeAsync(LargeStack, source.ToString(), [.. Enumerable.Range(0, Branches).Select(i => Verify.CopyByAssignment(i, "Guard"))]);
    }

    [TestMethod]
    public Task ChainOfBinaryOperators_TooDeepIsReportedAtTheMember()
    {
        // 'a + a + ...' is a binary operation inside the one on its left, as deep as it is long. '&&' is the same.
        string sum = string.Join(" + ", Enumerable.Repeat("a", 2000));
        string all = string.Join(" && ", Enumerable.Repeat("b", 2000));

        string source = $$"""
            class C
            {
                string {|#0:Sum|}(string a) => {{sum}};

                bool {|#1:All|}(bool b) => {{all}};
            }
            """;

        return AnalyzeAsync(SmallStack, source, Verify.NestedTooDeeply(0, "C.Sum(string)"), Verify.NestedTooDeeply(1, "C.All(bool)"));
    }

    [TestMethod]
    public Task NestedConditionalExpressions_TooDeepAreReportedAtTheMember()
    {
        var conditional = new StringBuilder();

        // The compiler makes these by recursion: see the remarks of the class for the depth.
        for (int i = 0; i < Conditionals; i++)
            conditional.Append($"k == {i} ? {i} : ");

        conditional.Append("-1");

        string source = $$"""
            class C
            {
                int {|#0:F|}(int k) => {{conditional}};
            }
            """;

        return AnalyzeAsync(SmallStack, source, Verify.NestedTooDeeply(0, "C.F(int)"));
    }

    [TestMethod]
    public Task ChainedCalls_TooDeepAreReportedAtTheMember()
    {
        // Each call is the receiver of the next. The walker follows a receiver of a non-copyable type to where it comes from, so a chain over
        // such a type reaches the limit sooner than the compiler's own recursion does.
        string calls = string.Concat(Enumerable.Repeat(".Add(1)", CallsInChain));

        string source = $$"""
            using Singulink.CodeAnalysis;

            class C
            {
                void {|#0:F|}(Builder builder)
                {
                    builder{{calls}}.Dispose();
                }
            }

            [NonCopyable]
            struct Builder
            {
                public readonly Builder Add(int value) => default;

                public void Dispose() { }
            }
            """;

        return AnalyzeAsync(SmallStack, source, Verify.NestedTooDeeply(0, "C.F(Builder)"));
    }

    // Under the updated rules the report is strict only where the member's code involves a value that is unsafe to copy: a type that says
    // so, or a marked type parameter that does not say UnsafeToCopy = false. Under the current rules each is the ordinary warning.
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task TooDeep_IsStrictUnderTheUpdatedRulesOnlyWhereAValueIsUnsafeToCopy(bool updatedRules)
    {
        string sum = string.Join(" + ", Enumerable.Repeat("a", 2000));
        string calls = string.Concat(Enumerable.Repeat(".Add(1)", CallsInChain));

        string source = $$"""
            using Singulink.CodeAnalysis;

            class C
            {
                string {|#0:Plain|}(string a) => {{sum}};

                string {|#1:Marked|}<[NonCopyable] T>(string a, ref T value) => {{sum}};

                string {|#2:MarkedTrue|}<[NonCopyable(UnsafeToCopy = true)] T>(string a, ref T value) => {{sum}};

                string {|#3:MarkedFalse|}<[NonCopyable(UnsafeToCopy = false)] T>(string a, ref T value) => {{sum}};

                string {|#4:Unmarked|}<T>(string a, ref T value) => {{sum}};

                void {|#5:Build|}(Builder builder)
                {
                    builder{{calls}}.Dispose();
                }

                void {|#6:BuildUnsafe|}(UnsafeBuilder builder)
                {
                    builder{{calls}}.Dispose();
                }
            }

            [NonCopyable]
            struct Builder
            {
                public readonly Builder Add(int value) => default;

                public void Dispose() { }
            }

            [NonCopyable(UnsafeToCopy = true)]
            struct UnsafeBuilder
            {
                public readonly UnsafeBuilder Add(int value) => default;

                public void Dispose() { }
            }
            """;

        DiagnosticResult[] expected = [
            Verify.NestedTooDeeply(0, "C.Plain(string)"),
            Verify.NestedTooDeeply(1, "C.Marked<T>(string, ref T)").AsError(updatedRules),
            Verify.NestedTooDeeply(2, "C.MarkedTrue<T>(string, ref T)").AsError(updatedRules),
            Verify.NestedTooDeeply(3, "C.MarkedFalse<T>(string, ref T)"),
            Verify.NestedTooDeeply(4, "C.Unmarked<T>(string, ref T)"),
            Verify.NestedTooDeeply(5, "C.Build(Builder)"),
            Verify.NestedTooDeeply(6, "C.BuildUnsafe(UnsafeBuilder)").AsError(updatedRules),
        ];

        return updatedRules ? AnalyzeUnderUpdatedRulesAsync(SmallStack, source, expected) : AnalyzeAsync(SmallStack, source, expected);
    }

    // A value that is unsafe to copy counts wherever the code has it: in a local that is declared deep in the member, and not only in
    // its signature.
    [TestMethod]
    public Task TooDeep_IsStrictWhereALocalIsUnsafeToCopy()
    {
        string sum = string.Join(" + ", Enumerable.Repeat("a", 2000));

        string source = $$"""
            class C
            {
                string {|#0:Sum|}(string a)
                {
                    Guard guard = default;
                    guard.Dispose();
                    return {{sum}};
                }
            }

            {{Sources.UnsafeGuard}}
            """;

        return AnalyzeUnderUpdatedRulesAsync(SmallStack, source, Verify.NestedTooDeeply(0, "C.Sum(string)").AsError());
    }

    // A value that can be of any type counts as one that is unsafe to copy, as for the rules that have no type: a call of
    // MemberwiseClone, which copies the fields of the class and of any class below it, whatever their types, a call of
    // RuntimeHelpers.GetObjectValue, and a value of a marked type parameter converted to a type that hides what is bound to it, which can
    // be a container of a type that is unsafe to copy. A type parameter that says UnsafeToCopy = false takes no such container, so its
    // value converted counts as what it is, a plain non-copyable type.
    [TestMethod]
    public Task TooDeep_IsStrictWhereAValueCanBeOfAnyType()
    {
        string sum = string.Join(" + ", Enumerable.Repeat("a", 2000));

        string source = $$"""
            using System.Runtime.CompilerServices;

            class Holder
            {
                UG _unsafe;

                public object {|#0:Clone|}(string a)
                {
                    _ = {{sum}};
                    return MemberwiseClone();
                }
            }

            sealed class Plain
            {
                public object {|#1:Clone|}(string a)
                {
                    _ = {{sum}};
                    return MemberwiseClone();
                }
            }

            class C
            {
                object {|#2:Boxed|}(string a, object boxed)
                {
                    _ = {{sum}};
                    return RuntimeHelpers.GetObjectValue(boxed);
                }

                object {|#3:Converted|}<[Singulink.CodeAnalysis.NonCopyable] T>(string a, T value)
                    where T : class
                {
                    _ = {{sum}};
                    return value;
                }

                object {|#5:ConvertedPlain|}<[Singulink.CodeAnalysis.NonCopyable(UnsafeToCopy = false)] T>(string a, T value)
                    where T : class
                {
                    _ = {{sum}};
                    return value;
                }

                string {|#4:Sum|}(string a) => {{sum}};
            }

            [Singulink.CodeAnalysis.NonCopyable(UnsafeToCopy = true)]
            struct UG { }
            """;

        return AnalyzeUnderUpdatedRulesAsync(
            SmallStack,
            source,
            Verify.NestedTooDeeply(0, "Holder.Clone(string)").AsError(),
            Verify.NestedTooDeeply(1, "Plain.Clone(string)").AsError(),
            Verify.NestedTooDeeply(2, "C.Boxed(string, object)").AsError(),
            Verify.NestedTooDeeply(3, "C.Converted<T>(string, T)").AsError(),
            Verify.NestedTooDeeply(4, "C.Sum(string)"),
            Verify.NestedTooDeeply(5, "C.ConvertedPlain<T>(string, T)"));
    }

    [TestMethod]
    public Task MemberAfterOneTooDeep_IsChecked()
    {
        // The member that is too deep is reported, and the analysis goes on: the copies of the members around it are reported as usual.
        string sum = string.Join(" + ", Enumerable.Repeat("a", 2000));

        string source = $$"""
            class C
            {
                private Guard _guard;

                void Before()
                {
                    Guard copy = {|#0:_guard|};
                }

                string {|#1:Sum|}(string a) => {{sum}};

                void After()
                {
                    Guard copy = {|#2:_guard|};
                }
            }

            {{Sources.Guard}}
            """;

        return AnalyzeAsync(
            SmallStack,
            source,
            Verify.CopyByAssignment(0, "Guard"),
            Verify.NestedTooDeeply(1, "C.Sum(string)"),
            Verify.CopyByAssignment(2, "Guard"));
    }

    [TestMethod]
    public Task ConditionalExpressions_OfANonCopyableTypeAreFollowedToTheirEnd()
    {
        // A conditional expression is new if both of its branches are, which follows the chain to its end. Every branch but the last is
        // a new value here, and the last is a field: the whole expression is a stored value.
        const int Branches = 200;
        var source = new StringBuilder();
        source.AppendLine("class C");
        source.AppendLine("{");
        source.AppendLine("    private Guard _guard;");
        source.AppendLine();
        source.AppendLine("    void M(int x, RwLock rwLock)");
        source.AppendLine("    {");
        source.Append("        Guard guard = {|#0:");

        for (int i = 0; i < Branches; i++)
            source.Append($"x == {i} ? rwLock.Enter() : ");

        source.AppendLine("_guard|};");
        source.AppendLine("        guard.Dispose();");
        source.AppendLine("    }");
        source.AppendLine("}");
        source.AppendLine();
        source.AppendLine(Sources.Guard);

        return AnalyzeAsync(LargeStack, source.ToString(), Verify.CopyByAssignment(0, "Guard"));
    }

    private static Task AnalyzeAsync(int stackSize, string source, params DiagnosticResult[] expected)
    {
        var test = new Verify.Test { TestCode = source, AnalysisStackSize = stackSize };
        test.ExpectedDiagnostics.AddRange(expected);
        return test.RunAsync();
    }

    private static Task AnalyzeUnderUpdatedRulesAsync(int stackSize, string source, params DiagnosticResult[] expected)
    {
        if (!Verify.CompilerHasUpdatedRules)
            Assert.Inconclusive("The compiler of this target does not apply the updated memory safety rules.");

        var test = new Verify.Test {
            TestCode = source,
            AnalysisStackSize = stackSize,
            LanguageVersion = LanguageVersion.Preview,
            Features = [Verify.UpdatedRulesFeature],
        };

        test.ExpectedDiagnostics.AddRange(expected);
        return test.RunAsync();
    }
}
