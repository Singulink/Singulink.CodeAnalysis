using PrefixClassName.MsTest;

namespace Singulink.CodeAnalysis.Tests;

/// <summary>
/// Tests of functions inside functions: a lambda or a local function that stands in a <c>try</c>, a <c>catch</c>, a <c>finally</c>,
/// a filter, a <c>using</c>, a <c>lock</c> or a <c>foreach</c> of the member around it.
/// </summary>
/// <remarks>
/// <para>Docs: articles/concepts/last-use.md and articles/guides/moving-a-value.md.</para>
/// <para>
/// The paths of a lambda or a local function end at the function. Its handlers are those inside it: what is around its declaration
/// is not around its body, which runs when the function is called. An earlier version walked out of the function's graph into the
/// enclosing one, took a block of the member for a handler of the function, and failed with an exception that left the rest of
/// the member unchecked. Every test of the analyzer fails on such an exception, and these put the functions where it came from.
/// </para>
/// </remarks>
[PrefixTestClass]
public class NestedFunctionTests
{
    private const string Lambda = "lambda";
    private const string LocalFunction = "local function";

    // Each row is a place in a member of a class, of a non-copyable struct or of an iterator, with a lambda or a local function in
    // it. The function has a read at a last use on each of two paths, a commit at the end of a 'try' of its own, a read that is
    // not a last use, which is the one finding in it, and a function of its own with a [MovedFrom] parameter. What follows the
    // place in the member is checked as well: the copy of the field is reported.
    [TestMethod]
    [DataRow("try {", "} catch { }")]
    [DataRow("try { } catch {", "}")]
    [DataRow("try { } finally {", "}")]
    [DataRow("try { } catch (Exception) when (Run(", ")) { }")]
    [DataRow("using (new Resource()) {", "}")]
    [DataRow("lock (gate) {", "}")]
    [DataRow("foreach (string name in names) {", "}")]
    [DataRow("try { try {", "} finally { } } catch (Exception) when (flag) { }")]
    public async Task NestedFunction_ItsPathsEndAtTheFunction(string open, string close)
    {
        bool isInFilter = open.EndsWith('(');

        foreach (string host in new[] { "sealed class", "[NonCopyable] struct", "iterator" })
        {
            foreach (string kind in new[] { Lambda, LocalFunction })
            {
                // An expression has no place for a local function.
                if (isInFilter && kind is LocalFunction)
                    continue;

                await Verify.AnalyzerAsync(
                    GetSource(host, kind, open, close, isInFilter),
                    Verify.CopyOfStoredValue(1, "'Host.Take(Guard)' receives", "Guard"),
                    Verify.CopyByAssignment(0, "Guard"));
            }
        }
    }

    private static string GetSource(string host, string kind, string open, string close, bool isInFilter)
    {
        const string Body = """
            {
                Guard guard = rwLock.Enter();

                if (flag)
                {
                    Take(guard);
                    return;
                }

                guard.Dispose();

                Guard committed = rwLock.Enter();

                try
                {
                    registry.Held = committed;
                }
                catch
                {
                    committed.Dispose();
                    throw;
                }

                Guard again = rwLock.Enter();
                Take({|#1:again|});
                again.Dispose();

                Mover mover = ([MovedFrom] ref Guard from, ref Guard to) =>
                {
                    to = from;
                    from = default;
                };

                Guard target = default;
                Move(ref again, ref target);
                target.Dispose();

                static void Move([MovedFrom] ref Guard from, ref Guard to)
                {
                    to = from;
                    from = default;
                }
            }
            """;

        string function = (kind, isInFilter) switch {
            (_, true) => "() => " + Body,
            (Lambda, _) => "Action work = () => " + Body + ";",
            _ => "void Work() " + Body,
        };

        bool isIterator = host is "iterator";

        return $$"""
            using System;
            using System.Collections.Generic;
            using Singulink.CodeAnalysis;

            delegate void Mover([MovedFrom] ref Guard from, ref Guard to);

            sealed class Registry
            {
                public Guard Held;
            }

            sealed class Resource : IDisposable
            {
                public void Dispose()
                {
                }
            }

            {{(isIterator ? "sealed class" : host)}} Host
            {
                private Guard _held;

                static void Take(Guard guard)
                {
                }

                static bool Run(Action action) => false;

                public {{(isIterator ? "IEnumerable<int>" : "void")}} M(RwLock rwLock, Registry registry, List<string> names, object gate, bool flag)
                {
                    {{open}}
                    {{function}}
                    {{close}}

                    Guard copy {|#0:= _held|};
                    copy.Dispose();
                    {{(isIterator ? "yield return 1;" : string.Empty)}}
                }
            }

            {{Sources.Guard}}
            """;
    }

    // The four programs that the exception came from. Each has a function in a 'foreach', in the scope of a 'using' declaration, in
    // a 'try' or in a 'lock', which reads a guard of its own by value, and each copies a field after it. The first block of the
    // enclosing 'catch' or 'finally' has a number that the function has no block for: the analyzer read past the end of an array
    // there, and reported nothing more for the member.
    [TestMethod]
    public Task NestedFunction_WithFewerBlocksThanTheHandlerAroundIt_IsCheckedAndSoIsTheRestOfTheMember()
    {
        const string Source = """
            using System;
            using System.Collections.Generic;

            static class Sink
            {
                public static void Take(Guard guard)
                {
                }

                public static void Use(int n)
                {
                }
            }

            sealed class InForEach
            {
                private readonly List<Action> _work = new();
                private Guard _held;

                public void Schedule(RwLock rwLock, List<string> names)
                {
                    _held = rwLock.Enter();

                    foreach (string name in names)
                    {
                        _work.Add(() =>
                        {
                            Guard guard = rwLock.Enter();
                            Sink.Take(guard);
                        });
                    }

                    Guard copy {|#0:= _held|};
                    copy.Dispose();
                }
            }

            sealed class InUsingDeclaration
            {
                private Guard _held;

                public void Run(RwLock rwLock)
                {
                    _held = rwLock.Enter();
                    using var scope = new Scope();

                    {
                        void Process(Guard guard) { Sink.Take(guard); }
                        Process(rwLock.Enter());
                    }

                    Sink.Take({|#1:_held|});
                }

                private sealed class Scope : IDisposable
                {
                    public void Dispose()
                    {
                    }
                }
            }

            sealed class InTry
            {
                private Guard _held;
                private Action<Guard>? _release;

                public void Start(RwLock rwLock)
                {
                    _held = rwLock.Enter();
                    Sink.Use(1);

                    try
                    {
                        _release = guard => Sink.Take(guard);
                        Sink.Use(2);
                    }
                    catch (InvalidOperationException)
                    {
                        Sink.Use(3);
                    }

                    Guard copy {|#2:= _held|};
                    copy.Dispose();
                }
            }

            sealed class InLock
            {
                private readonly object _gate = new();
                private Guard _held;
                private Func<Guard, int>? _take;

                public void Start(RwLock rwLock)
                {
                    _held = rwLock.Enter();

                    lock (_gate)
                    {
                        _take = guard => { Guard mine = guard; mine.Dispose(); return 1; };
                    }

                    Guard copy {|#3:= _held|};
                    copy.Dispose();
                }
            }

            // A function in a function in a 'try', and the same in a 'finally'.
            sealed class TwoDeep
            {
                private Guard _held;
                private Func<Action<Guard>>? _make;

                public void Start(RwLock rwLock)
                {
                    _held = rwLock.Enter();
                    Sink.Use(1);

                    try
                    {
                        _make = () => guard => Sink.Take(guard);
                        Sink.Use(2);
                    }
                    finally
                    {
                        _make = () =>
                        {
                            void Release(Guard guard) => Sink.Take(guard);
                            return Release;
                        };
                    }

                    Guard copy {|#4:= _held|};
                    copy.Dispose();
                }
            }

            """ + Sources.Guard;

        return Verify.AnalyzerAsync(
            Source,
            Verify.CopyByAssignment(0, "Guard"),
            Verify.CopyOfStoredValue(1, "'Sink.Take(Guard)' receives", "Guard"),
            Verify.CopyByAssignment(2, "Guard"),
            Verify.CopyByAssignment(3, "Guard"),
            Verify.CopyByAssignment(4, "Guard"));
    }
}
