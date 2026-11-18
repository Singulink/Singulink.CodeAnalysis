using System.Text;
using PrefixClassName.MsTest;

namespace Singulink.CodeAnalysis.Tests;

/// <summary>
/// Tests of code that is deep or long: the analyzer must get through it, or report the one member that is too deep for it (SCA1048) and go
/// on with the next, and must never run out of stack, which would end the compiler's process.
/// </summary>
[PrefixTestClass]
public class DeepCodeTests
{
    private const int CallsInChain = 800;

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

        return Verify.AnalyzerAsync(source.ToString());
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
            source.AppendLine($"        {(i == 0 ? string.Empty : "else ")}if (x == {i}) {{ Guard copy {{|#{i}:= _guard|}}; }}");

        source.AppendLine("    }");
        source.AppendLine("}");
        source.AppendLine();
        source.AppendLine(Sources.Guard);

        return Verify.AnalyzerAsync(source.ToString(), [.. Enumerable.Range(0, Branches).Select(i => Verify.CopyByAssignment(i, "Guard"))]);
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

        return Verify.AnalyzerAsync(source, Verify.NestedTooDeeply(0, "C.Sum(string)"), Verify.NestedTooDeeply(1, "C.All(bool)"));
    }

    [TestMethod]
    public Task NestedConditionalExpressions_TooDeepAreReportedAtTheMember()
    {
        var conditional = new StringBuilder();

        for (int i = 0; i < 1000; i++)
            conditional.Append($"k == {i} ? {i} : ");

        conditional.Append("-1");

        string source = $$"""
            class C
            {
                int {|#0:F|}(int k) => {{conditional}};
            }
            """;

        return Verify.AnalyzerAsync(source, Verify.NestedTooDeeply(0, "C.F(int)"));
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

        return Verify.AnalyzerAsync(source, Verify.NestedTooDeeply(0, "C.F(Builder)"));
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
                    Guard copy {|#0:= _guard|};
                }

                string {|#1:Sum|}(string a) => {{sum}};

                void After()
                {
                    Guard copy {|#2:= _guard|};
                }
            }

            {{Sources.Guard}}
            """;

        return Verify.AnalyzerAsync(
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
        source.Append("        Guard guard {|#0:= ");

        for (int i = 0; i < Branches; i++)
            source.Append($"x == {i} ? rwLock.Enter() : ");

        source.AppendLine("_guard|};");
        source.AppendLine("        guard.Dispose();");
        source.AppendLine("    }");
        source.AppendLine("}");
        source.AppendLine();
        source.AppendLine(Sources.Guard);

        return Verify.AnalyzerAsync(source.ToString(), Verify.CopyByAssignment(0, "Guard"));
    }
}
