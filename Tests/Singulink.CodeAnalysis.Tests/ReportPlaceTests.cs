using PrefixClassName.MsTest;

namespace Singulink.CodeAnalysis.Tests;

/// <summary>
/// Tests of where a copy in code is reported: at the expression that makes it, which an <c>unsafe(expr)</c> expression can hold under
/// the updated memory safety rules, and not at a declaration, which cannot.
/// </summary>
[PrefixTestClass]
public class ReportPlaceTests
{
    private const string Members = """
        Guard _guard;
        Guard[] _guards = new Guard[1];
        (Guard, int) _pair;
        (Guard, int)[] _pairs = new (Guard, int)[1];
        Pair[] _deconstructed = new Pair[1];
        System.Collections.ArrayList _boxes = new();
        """;

    private const string Pair = """
        [Singulink.CodeAnalysis.NonCopyable]
        public struct Pair
        {
            public void Deconstruct(out int a, out int b) => (a, b) = (1, 2);
        }
        """;

    private const string Code = """
            void Declared()
            {
                var copy = {|#0:_guard|};
            }

            void Deconstructed()
            {
                var (guard, number) = {|#1:_pair|};
            }

            void DeconstructedIntoVariables(Guard guard, int number)
            {
                (guard, number) = {|#2:_pair|};
            }

            void ForEach()
            {
                foreach (var guard in {|#3:_guards|}) { }
            }

            void ForEachDeconstructed()
            {
                foreach (var (guard, number) in {|#4:_pairs|}) { }
            }

            void ForEachDeconstructedByMethod()
            {
                foreach (var (a, b) in {|#5:_deconstructed|}) { }
            }

            void ForEachBoxed()
            {
                foreach (object box in {|#6:_guards|}) { }
            }

            void ForEachUnboxed()
            {
                foreach (Guard guard in {|#7:_boxes|}) { }
            }
        """;

    // A class of the project whose type parameter is not marked, so that the Dispose method that a 'using' calls on a pool of guards
    // is a member that the symbol list does not name.
    private const string Pool = """
        public class Pool<T> : System.IDisposable, System.IAsyncDisposable
        {
            public void Dispose() { }

            public System.Threading.Tasks.ValueTask DisposeAsync() => default;
        }
        """;

    // Each form of 'using', with the value of its resource marked. The pool comes from a method, so that its constructor is not used.
    private const string Usings = """
            static Pool<Guard> CreatePool() => throw null!;

            async Task Declarations()
            {
                using var pool = {|#0:CreatePool()|};
                await using var asyncPool = {|#1:CreatePool()|};
                using Pool<Guard> first = {|#2:CreatePool()|}, second = {|#3:CreatePool()|};
            }

            async Task Statements()
            {
                using (var pool = {|#4:CreatePool()|}) { }
                await using (var pool = {|#5:CreatePool()|}) { }
                using ({|#6:CreatePool()|}) { }
                await using ({|#7:CreatePool()|}) { }
            }
        """;

    private static readonly Microsoft.CodeAnalysis.Testing.DiagnosticResult[] ExpectedOfUsings = [
        Verify.UnlistedMember(0, "Pool<T>.Dispose()", "Guard", "T", "M:Pool`1.Dispose"),
        Verify.UnlistedMember(1, "Pool<T>.DisposeAsync()", "Guard", "T", "M:Pool`1.DisposeAsync"),
        Verify.UnlistedMember(2, "Pool<T>.Dispose()", "Guard", "T", "M:Pool`1.Dispose"),
        Verify.UnlistedMember(3, "Pool<T>.Dispose()", "Guard", "T", "M:Pool`1.Dispose"),
        Verify.UnlistedMember(4, "Pool<T>.Dispose()", "Guard", "T", "M:Pool`1.Dispose"),
        Verify.UnlistedMember(5, "Pool<T>.DisposeAsync()", "Guard", "T", "M:Pool`1.DisposeAsync"),
        Verify.UnlistedMember(6, "Pool<T>.Dispose()", "Guard", "T", "M:Pool`1.Dispose"),
        Verify.UnlistedMember(7, "Pool<T>.DisposeAsync()", "Guard", "T", "M:Pool`1.DisposeAsync"),
    ];

    private static readonly Microsoft.CodeAnalysis.Testing.DiagnosticResult[] Expected = [
        Verify.CopyByAssignment(0, "Guard"),
        Verify.CopyByAssignmentIntoVariable(1, "Guard", "guard"),
        Verify.CopyByAssignment(2, "Guard"),
        Verify.CopyByAssignmentIntoForEachVariable(3, "Guard", "guard"),
        Verify.CopyByAssignmentIntoVariable(4, "Guard", "guard"),
        Verify.CopyByAssignmentForDeconstruct(5, "Pair", "Pair.Deconstruct(out int, out int)"),
        Verify.Boxing(6, "Guard", "object"),
        Verify.CopyByAssignmentOutOfBox(7, "Guard", "object"),
    ];

    [TestMethod]
    public Task Copy_IsReportedAtTheExpressionThatMakesIt() => Verify.AnalyzerAsync(
        $$"""
        class C
        {
            {{Members}}

        {{Code}}
        }

        {{Pair}}

        {{Sources.Guard}}
        """,
        Expected);

    // Under the updated rules each of the copies is an error, and the same expression in unsafe(...) is a warning.
    [TestMethod]
    public Task Copy_UnderTheUpdatedRulesAnUnsafeExpressionHoldsIt() => Verify.UpdatedRulesAsync(
        $$"""
        class C
        {
            {{Members}}

        {{Code}}
        }

        class InUnsafeExpressions
        {
            {{Members}}

        {{Code.Replace("{|#", "unsafe({|#", StringComparison.Ordinal).Replace("|}", "|})", StringComparison.Ordinal).Replace("#0:", "#10:", StringComparison.Ordinal)
            .Replace("#1:", "#11:", StringComparison.Ordinal).Replace("#2:", "#12:", StringComparison.Ordinal).Replace("#3:", "#13:", StringComparison.Ordinal)
            .Replace("#4:", "#14:", StringComparison.Ordinal).Replace("#5:", "#15:", StringComparison.Ordinal).Replace("#6:", "#16:", StringComparison.Ordinal)
            .Replace("#7:", "#17:", StringComparison.Ordinal)}}
        }

        {{Sources.AsUnsafeToCopy(Pair)}}

        {{Sources.UnsafeGuard}}
        """,
        [
            .. Expected.Select(result => result.AsError()),
            Verify.CopyByAssignment(10, "Guard"),
            Verify.CopyByAssignmentIntoVariable(11, "Guard", "guard"),
            Verify.CopyByAssignment(12, "Guard"),
            Verify.CopyByAssignmentIntoForEachVariable(13, "Guard", "guard"),
            Verify.CopyByAssignmentIntoVariable(14, "Guard", "guard"),
            Verify.CopyByAssignmentForDeconstruct(15, "Pair", "Pair.Deconstruct(out int, out int)"),
            Verify.Boxing(16, "Guard", "object"),
            Verify.CopyByAssignmentOutOfBox(17, "Guard", "object"),
        ]);

    // With a plain non-copyable type each of the copies is the ordinary warning under the updated rules, in unsafe(...) and outside it.
    [TestMethod]
    public Task Copy_OfAPlainTypeIsTheOrdinaryWarningUnderTheUpdatedRules() => Verify.UpdatedRulesAsync(
        $$"""
        class C
        {
            {{Members}}

        {{Code}}
        }

        class InUnsafeExpressions
        {
            {{Members}}

        {{Code.Replace("{|#", "unsafe({|#", StringComparison.Ordinal).Replace("|}", "|})", StringComparison.Ordinal).Replace("#0:", "#10:", StringComparison.Ordinal)
            .Replace("#1:", "#11:", StringComparison.Ordinal).Replace("#2:", "#12:", StringComparison.Ordinal).Replace("#3:", "#13:", StringComparison.Ordinal)
            .Replace("#4:", "#14:", StringComparison.Ordinal).Replace("#5:", "#15:", StringComparison.Ordinal).Replace("#6:", "#16:", StringComparison.Ordinal)
            .Replace("#7:", "#17:", StringComparison.Ordinal)}}
        }

        {{Pair}}

        {{Sources.Guard}}
        """,
        [
            .. Expected,
            Verify.CopyByAssignment(10, "Guard"),
            Verify.CopyByAssignmentIntoVariable(11, "Guard", "guard"),
            Verify.CopyByAssignment(12, "Guard"),
            Verify.CopyByAssignmentIntoForEachVariable(13, "Guard", "guard"),
            Verify.CopyByAssignmentIntoVariable(14, "Guard", "guard"),
            Verify.CopyByAssignmentForDeconstruct(15, "Pair", "Pair.Deconstruct(out int, out int)"),
            Verify.Boxing(16, "Guard", "object"),
            Verify.CopyByAssignmentOutOfBox(17, "Guard", "object"),
        ]);

    // The Dispose method that a 'using' calls is reported at the value of the resource, in a declaration at the initializer of each
    // variable, and not at the declarator.
    [TestMethod]
    public Task Disposal_IsReportedAtTheValueOfTheResource() => Verify.AnalyzerAsync(
        $$"""
        using System.Threading.Tasks;

        class C
        {
        {{Usings}}
        }

        {{Pool}}

        {{Sources.Guard}}
        """,
        ExpectedOfUsings);

    // Under the updated rules each of them is an error, and the same value in unsafe(...) is a warning: an 'unsafe' block around a
    // 'using' declaration would end the scope of its variable.
    [TestMethod]
    public Task Disposal_UnderTheUpdatedRulesAnUnsafeExpressionHoldsIt() => Verify.UpdatedRulesAsync(
        $$"""
        using System.Threading.Tasks;

        class C
        {
        {{Usings}}
        }

        class InUnsafeExpressions
        {
        {{Usings.Replace("{|#", "unsafe({|#1", StringComparison.Ordinal).Replace("|}", "|})", StringComparison.Ordinal)}}
        }

        {{Pool}}

        {{Sources.UnsafeGuard}}
        """,
        [
            .. ExpectedOfUsings.Select(result => result.AsError()),
            Verify.UnlistedMember(10, "Pool<T>.Dispose()", "Guard", "T", "M:Pool`1.Dispose"),
            Verify.UnlistedMember(11, "Pool<T>.DisposeAsync()", "Guard", "T", "M:Pool`1.DisposeAsync"),
            Verify.UnlistedMember(12, "Pool<T>.Dispose()", "Guard", "T", "M:Pool`1.Dispose"),
            Verify.UnlistedMember(13, "Pool<T>.Dispose()", "Guard", "T", "M:Pool`1.Dispose"),
            Verify.UnlistedMember(14, "Pool<T>.Dispose()", "Guard", "T", "M:Pool`1.Dispose"),
            Verify.UnlistedMember(15, "Pool<T>.DisposeAsync()", "Guard", "T", "M:Pool`1.DisposeAsync"),
            Verify.UnlistedMember(16, "Pool<T>.Dispose()", "Guard", "T", "M:Pool`1.Dispose"),
            Verify.UnlistedMember(17, "Pool<T>.DisposeAsync()", "Guard", "T", "M:Pool`1.DisposeAsync"),
        ]);
}
