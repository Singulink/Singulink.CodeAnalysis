using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using PrefixClassName.MsTest;

namespace Singulink.CodeAnalysis.Tests;

/// <summary>
/// Tests of a reference that is known to refer to something: a read or a plain store through it does not count as able to throw,
/// between the read and the assignment of a [MovedFrom] parameter and after a commit at the end of a <c>try</c> block. And of the
/// group assignment, which reads everything on its right before it stores anything.
/// </summary>
/// <remarks>
/// Docs: articles/rules/moved-from.md and articles/rules/last-use.md.
/// </remarks>
[PrefixTestClass]
public class KnownReferenceTests
{
    private const string Types = """
        struct Counter
        {
            public int Value;

            public void Touch() { }
        }
        """;

    // The four examples of the specification, as they are written there.
    [TestMethod]
    public Task Examples_OfTheSpecification() => Verify.AnalyzerAsync(
        $$"""
        using Singulink.CodeAnalysis;

        [NonCopyable]
        struct Holder
        {
            RwLock _lock;
            Guard _held;
            bool _isHeld;

            static void Prepare() { }

            public void Acquire()
            {
                Guard g = _lock.Enter();
                try
                {
                    Prepare();
                    _held = g;                          // the commit; also a store through 'this'
                    _isHeld = true;                     // OK: 'this' is known to refer to something
                }
                catch { g.Dispose(); throw; }
            }
        }

        static class Moves
        {
            static void Hand([MovedFrom] ref Guard from, ref Guard to, ref int count)
            {
                NonCopyable.ThrowIfNullRef(in count);
                to = from;                              // OK: the one store of the value that was read
                count++;                                // OK: checked above. Without the check it is reported
                from = default;
            }

            static void HandWithNoCheck([MovedFrom] ref Guard from, ref Guard to, ref int count)
            {
                to = from;
                {|#0:count|}++;
                from = default;
            }

            static void Rotate([MovedFrom] ref Guard a, [MovedFrom] ref Guard b, [MovedFrom] ref Guard c)
            {
                (a, b, c) = (b, c, default);            // OK: 'b' and 'c' were read, so only the first store can fail
            }

            static void Spill([MovedFrom] ref Guard a, [MovedFrom] ref Guard b, ref Guard c)
            {
                {|#1:(b, c, a) = (a, default, default)|};      // reported: 'c' was not read, and its store follows the store of the value
                (c, b, a) = (default, a, default);      // OK: nothing was moved before the stores that can fail
            }
        }

        {{Sources.Guard}}
        """,
        Verify.MovedFromParameterNotAssignedAtOnceThroughReference(0, "from", "count"),
        Verify.MovedFromParameterNotAssignedAtOnceThroughReference(1, "a", "c"));

    // The proofs are a closed list of three: a call that returns, with the reference itself passed to a parameter that is marked
    // [NotNullRef]; a plain store through the reference; and a by-value read through it whose value is stored, in a field, in an
    // element, through a reference or by a group assignment into such a target. A read or a store of an instance field of what
    // the reference refers to counts like one of the whole. Everything else that names the reference proves nothing, the same
    // for every reference and with every compiler.
    [TestMethod]
    [DataRow("", "number++;", false)]
    [DataRow("NonCopyable.ThrowIfNullRef(in number);", "number++;", true)]
    [DataRow("NonCopyable.ThrowIfNullRefArgument(in number);", "number++;", true)]
    [DataRow("s_sink = number;", "number++;", true)]
    [DataRow("s_sinks[0] = number;", "number++;", true)]
    [DataRow("other = number;", "number++;", true)]
    [DataRow("s_sink = (int)(long)number;", "number++;", true)]
    [DataRow("number = 0;", "number++;", true)]
    [DataRow("number += 2;", "number++;", true)]
    [DataRow("number++;", "number++;", true)]
    [DataRow("int local; (number, local) = (1, 2);", "number++;", true)]
    [DataRow("int local; (local, s_sink) = (1, number);", "number++;", true)]
    [DataRow("", "counter.Value++;", false)]
    [DataRow("NonCopyable.ThrowIfNullRef(in counter);", "counter.Value++;", true)]
    [DataRow("counter.Value = 1;", "counter.Value++;", true)]
    [DataRow("counter = default;", "counter.Value++;", true)]
    [DataRow("s_sink = counter.Value;", "counter.Value++;", true)]

    // A read whose value is not stored proves nothing: into a local, into a discard, as an argument. A compiler leaves out a
    // read whose value nothing needs, and which one it leaves out differs by compiler.
    [DataRow("int read = number; Use(read);", "number++;", false)]
    [DataRow("Use(number);", "number++;", false)]
    [DataRow("_ = number;", "number++;", false)]
    [DataRow("int unused = number;", "number++;", false)]
    [DataRow("int local; local = number;", "number++;", false)]
    [DataRow("int local; (local, _) = (1, number);", "number++;", false)]
    [DataRow("long wide = number; Use((int)wide);", "number++;", false)]
    [DataRow("int checkedOnly = number; System.Diagnostics.Debug.Assert(checkedOnly > 0);", "number++;", false)]
    [DataRow("s_tracer.Kept(number);", "number++;", false)]
    [DataRow("int read = counter.Value; Use(read);", "counter.Value++;", false)]
    [DataRow("Counter copy = counter; Use(copy.Value);", "counter.Value++;", false)]

    // The eight forms that were measured with a null reference in an optimised build under both SDKs. A read of a field of a
    // struct into a discard or a local, and a read of the whole struct, do not fail under the .NET 10 SDK and fail under the
    // .NET 11 SDK; a getter of a struct fails only if its body reads a field; a member that is used on an object of a class
    // which was read through the reference fails under both. None of them is a proof.
    [DataRow("_ = stats.Value;", "stats.Value = 1;", false)]
    [DataRow("_ = stats.Prop;", "stats.Value = 1;", false)]
    [DataRow("_ = stats.Const;", "stats.Value = 1;", false)]
    [DataRow("int v = stats.Value;", "stats.Value = 1;", false)]
    [DataRow("_ = box.Value;", "box = null!;", false)]
    [DataRow("_ = box.Prop;", "box = null!;", false)]
    [DataRow("_ = box;", "box = null!;", false)]
    [DataRow("_ = stats;", "stats.Value = 1;", false)]

    // Nor does anything else that names the reference: an operand, a condition, a pattern, a call of a member on it, passing it
    // on by reference to a parameter that is not marked, taking a reference to a field of it, and a test with Unsafe.IsNullRef.
    [DataRow("s_sink = -number;", "number++;", false)]
    [DataRow("int sum = number + 1; Use(sum);", "number++;", false)]
    [DataRow("if (number > 0) { Use(0); }", "number++;", false)]
    [DataRow("if (number is 0) { Use(0); }", "number++;", false)]
    [DataRow("Pass(ref number);", "number++;", false)]
    [DataRow("Read(in number);", "number++;", false)]
    [DataRow("bool isNull = Unsafe.IsNullRef(ref number); Use(isNull ? 1 : 0);", "number++;", false)]
    [DataRow("ref int alias = ref number;", "number++;", false)]
    [DataRow("counter.Touch();", "counter.Value++;", false)]
    [DataRow("ref int field = ref counter.Value;", "counter.Value++;", false)]
    [DataRow("Pass(ref counter.Value);", "counter.Value++;", false)]
    [DataRow("int product = number * Stride; Use(product);", "number++;", false)]
    [DataRow("int product = number * 2; Use(product);", "number++;", false)]
    [DataRow("bool isSet = (number != 0) & Checks; Use(isSet ? 1 : 0);", "number++;", false)]
    [DataRow("Use(number > null ? 1 : 0);", "number++;", false)]
    [DataRow("_ = number * Stride;", "number++;", false)]
    [DataRow("if (number == Stride) { Use(0); }", "number++;", false)]
    [DataRow("if (number > new int?()) { Use(0); }", "number++;", false)]
    [DataRow("int less = number - 1; Use(less);", "number++;", false)]
    [DataRow("int square = number * other; Use(square);", "number++;", false)]
    [DataRow("Use(number + 1);", "number++;", false)]
    [DataRow("int total = 0; total += number; Use(total);", "number++;", false)]
    [DataRow("_ = new int?(number);", "number++;", false)]
    [DataRow("var pair = new System.ValueTuple<int, bool>(number, true);", "number++;", false)]
    [DataRow("var both = (number, 1); Use(both.Item1);", "number++;", false)]

    // What stands in a call that the compiler can leave out never runs, a store and a stored read included: a [Conditional]
    // method, an override of one, and a generic partial method with no body.
    [DataRow("System.Diagnostics.Debug.Assert(number > 0);", "number++;", false)]
    [DataRow("System.Diagnostics.Debug.Assert((s_sink = number) > 0);", "number++;", false)]
    [DataRow("System.Diagnostics.Debug.Assert((number = 1) > 0);", "number++;", false)]
    [DataRow("s_tracer.Value(s_sink = number);", "number++;", false)]
    [DataRow("OnHandOver(s_sink = number);", "number++;", false)]
    [DataRow("Generic<int>.Trace(s_sink = number);", "number++;", false)]
    [DataRow("s_tracer.Kept(s_sink = number);", "number++;", true)]

    // A proof counts for the reference as it was when the statement began.
    [DataRow("ref int alias = ref other; alias = Next(ref number);", "alias++;", true)]
    [DataRow("ref int alias = ref other; alias = Next(ref (alias = ref number));", "alias++;", false)]
    [DataRow("ref int alias = ref other; alias += Next(ref (alias = ref number));", "alias++;", false)]
    [DataRow("ref int alias = ref other; NonCopyable.ThrowIfNullRef(in alias, Name(ref (alias = ref number)));", "alias++;", false)]
    [DataRow("ref int alias = ref other; int local; (alias, local) = (Next(ref (alias = ref number)), 2);", "alias++;", false)]
    public Task KnownReference_EachProofAndWhatProvesNothing(string before, string between, bool isKnown)
    {
        string reference = System.Text.RegularExpressions.Regex.Match(between, @"^\w+").Value;
        string marked = isKnown ? between : "{|#0:" + reference + "|}" + between[reference.Length..];

        string source = $$"""
            using System.Runtime.CompilerServices;
            using Singulink.CodeAnalysis;

            {{Types}}

            struct Stats
            {
                public int Value;

                public int Prop => Value;

                public int Const => 7;
            }

            sealed class Box
            {
                public int Value;

                public int Prop => Value;
            }

            abstract class Tracer
            {
                [System.Diagnostics.Conditional("TRACE_HANDOVERS")]
                public virtual void Value(int value) { }

                public virtual void Kept(int value) { }
            }

            sealed class ConsoleTracer : Tracer
            {
                public override void Value(int value) { }

                public override void Kept(int value) { }
            }

            static class Generic<T>
            {
                [System.Diagnostics.Conditional("TRACE_HANDOVERS")]
                public static void Trace(T value) { }
            }

            static partial class Moves
            {
                const int Stride = 0;
                const bool Checks = false;

                static readonly ConsoleTracer s_tracer = new();
                static readonly int[] s_sinks = new int[1];
                static int s_sink;

                static void Use(int value) { }

                static void Pass(ref int value) { }

                static void Read(in int value) { }

                static int Next(ref int value) => 0;

                static string Name(ref int value) => "";

                static partial void OnHandOver<T>(T value);

                static void Hand([MovedFrom] ref Guard from, ref Guard to, ref int number, ref Counter counter, ref int other, ref Stats stats, ref Box box)
                {
                    {{before}}
                    to = from;
                    {{marked}}
                    from = default;
                }
            }

            {{Sources.Guard}}
            """;

        return isKnown
            ? Verify.AnalyzerAsync(source)
            : Verify.AnalyzerAsync(source, Verify.MovedFromParameterNotAssignedAtOnceThroughReference(0, "from", reference));
    }

    // Known at a place means known on every path to it, with nothing after the proof that makes the reference refer to something else.
    [TestMethod]
    public Task KnownReference_IsKnownOnEveryPathAndUntilItIsMadeToReferToSomethingElse() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using Singulink.CodeAnalysis;

        static class Moves
        {
            static void Work() { }

            static void OnOneBranchOnly([MovedFrom] ref Guard from, ref Guard to, ref int number, bool flag)
            {
                if (flag)
                    number = 0;

                to = from;
                {|#0:number|}++;
                from = default;
            }

            static void OnBothBranches([MovedFrom] ref Guard from, ref Guard to, ref int number, bool flag)
            {
                if (flag)
                    number = 0;
                else
                    number = 1;

                to = from;
                number++;
                from = default;
            }

            // A handler knows only what was known where its 'try' block began: the exception can have come before the proof.
            static void InsideATryUsedInItsHandler([MovedFrom] ref Guard from, ref Guard to, ref int number)
            {
                try
                {
                    Work();
                    number = 1;
                    Work();
                }
                catch (InvalidOperationException)
                {
                    to = from;
                    {|#1:number|}++;
                    from = default;
                }
            }

            static void BeforeATryUsedInItsHandler([MovedFrom] ref Guard from, ref Guard to, ref int number)
            {
                number = 1;

                try
                {
                    Work();
                }
                catch (InvalidOperationException)
                {
                    to = from;
                    number++;
                    from = default;
                }
            }

            static void AfterATry([MovedFrom] ref Guard from, ref Guard to, ref int number)
            {
                try
                {
                    number = 1;
                    Work();
                }
                finally
                {
                    Work();
                }

                to = from;
                number++;
                from = default;
            }

            // A 'ref' local that is made to refer to something else is another reference from there on.
            static void MadeToReferToSomethingElse([MovedFrom] ref Guard from, ref Guard to, ref int number, ref int other)
            {
                ref int alias = ref number;
                alias = 1;
                alias = ref other;
                to = from;
                {|#2:alias|}++;
                from = default;
            }

            static void NotMadeToReferToSomethingElse([MovedFrom] ref Guard from, ref Guard to, ref int number)
            {
                ref int alias = ref number;
                alias = 1;
                to = from;
                alias++;
                from = default;
            }

            // A 'ref' local that is declared in a loop refers to something else in every round.
            static void DeclaredInALoop([MovedFrom] ref Guard from, ref Guard to, int[] numbers, ref int other)
            {
                for (int i = 0; i < numbers.Length; i++)
                {
                    ref int slot = ref i > 0 ? ref other : ref numbers[i];

                    if (i > 1)
                    {
                        to = from;
                        {|#3:slot|}++;
                        from = default;
                        return;
                    }

                    slot = 1;
                }
            }

            // What stays whatever is known: a by-value read of a non-copyable value through another reference. If the two refer to
            // one variable, it reads the same value a second time.
            static (Guard, Guard) ReadsThroughAKnownReference([MovedFrom] ref Guard a, ref Guard b)
            {
                NonCopyable.ThrowIfNullRef(in b);
                Guard first = a;
                Guard second = {|#4:{|#5:b|}|};
                a = default;
                return (first, second);
            }
        }

        {{Sources.Guard}}
        """,
        Verify.MovedFromParameterNotAssignedAtOnceThroughReference(0, "from", "number"),
        Verify.MovedFromParameterNotAssignedAtOnceThroughReference(1, "from", "number"),
        Verify.MovedFromParameterNotAssignedAtOnceThroughReference(2, "from", "alias"),
        Verify.MovedFromParameterNotAssignedAtOnceThroughReference(3, "from", "slot"),
        Verify.CopyByAssignment(4, "Guard"),
        Verify.MovedFromParameterNotAssignedAtOnce(5, "a", "a read or a write through another reference"));

    // The commit at the end of a 'try' block uses the same judgment.
    [TestMethod]
    public Task KnownReference_AfterACommit() => Verify.AnalyzerAsync(
        $$"""
        using Singulink.CodeAnalysis;

        class Owner
        {
            Guard _held;

            static void Prepare() { }

            void Counted(RwLock rwLock, ref int count)
            {
                NonCopyable.ThrowIfNullRef(in count);
                Guard g = rwLock.Enter();

                try { Prepare(); _held = g; count++; }
                catch { g.Dispose(); throw; }
            }

            void CountedWithNoCheck(RwLock rwLock, ref int count)
            {
                Guard g = rwLock.Enter();

                try { Prepare(); _held = {|#0:g|}; count++; }
                catch { {|#3:g|}.Dispose(); throw; }
            }

            // A proof in the 'try' block counts where the block goes on.
            void CountedTwice(RwLock rwLock, ref int count)
            {
                Guard g = rwLock.Enter();

                try { Prepare(); count = 0; _held = g; count++; }
                catch { g.Dispose(); throw; }
            }

            // The commit through a reference shows that the reference refers to something.
            void ThroughAReference(RwLock rwLock, ref Guard slot, ref (Guard Held, bool IsHeld) pair)
            {
                Guard g = rwLock.Enter();

                try { Prepare(); pair.Held = g; pair.IsHeld = true; }
                catch { g.Dispose(); throw; }
            }

            // A group assignment after the commit: every store that can fail is one too many.
            void GroupAfterTheCommit(RwLock rwLock, ref int count, ref int other)
            {
                Guard g = rwLock.Enter();
                int local;

                try { Prepare(); count = 0; _held = {|#1:g|}; (local, count, other) = (1, 2, 3); }
                catch { {|#4:g|}.Dispose(); throw; }
            }

            void GroupOfKnownReferencesAfterTheCommit(RwLock rwLock, ref int count, ref int other)
            {
                Guard g = rwLock.Enter();
                int local;

                try { Prepare(); count = 0; other = 0; _held = g; (local, count, other) = (1, 2, 3); }
                catch { g.Dispose(); throw; }
            }

            // A read on the right of a group assignment after the commit is a read through a reference after the commit.
            void GroupThatReadsThroughAReference(RwLock rwLock, ref int count, ref int other)
            {
                Guard g = rwLock.Enter();

                try { Prepare(); _held = {|#2:g|}; (count, other) = (other, count); }
                catch { {|#5:g|}.Dispose(); throw; }
            }
        }

        {{Sources.Guard}}
        """,
        Verify.UsedAfterHandOffBeforeNullReference(3, "g", "Guard", "its value was assigned", "count", 0),
        Verify.UsedAfterHandOffBeforeNullReference(4, "g", "Guard", "its value was assigned", "other", 1),
        Verify.UsedAfterHandOffBeforeNullReference(5, "g", "Guard", "its value was assigned", "count", 2));

    private const string GroupMoves = """
        using Singulink.CodeAnalysis;

        public static class Moves
        {
            public static void Swap([MovedFrom] ref Guard a, [MovedFrom] ref Guard b)
            {
                (a, b) = (b, a);
            }

            public static void Shift([MovedFrom] ref Guard a, [MovedFrom] ref Guard b, [MovedFrom] ref Guard c)
            {
                (a, b, c) = (b, c, default);
            }

            public static void ShiftBack([MovedFrom] ref Guard a, [MovedFrom] ref Guard b, [MovedFrom] ref Guard c)
            {
                (c, b, a) = (b, a, default);
            }

            // A value that runs nothing when it is worked out is as good as 'default': a struct that is created all zeros, and
            // what the operators of the language make of plain reads.
            public static void ResetFirst([MovedFrom] ref Guard a, ref Guard b)
            {
                (a, b) = (new Guard(), a);
            }

            public static void Counted([MovedFrom] ref Guard from, ref Guard to, ref int count)
            {
                NonCopyable.ThrowIfNullRef(in count);
                (to, count) = (from, count + 1);
                from = default;
            }
        }
        """;

    private const string SpreadMoves = """
        using Singulink.CodeAnalysis;

        public static class Spreads
        {
            public static void ShiftAcrossFour([MovedFrom] ref Guard a, [MovedFrom] ref Guard b, [MovedFrom] ref Guard c, [MovedFrom] ref Guard d)
            {
                {|#2:(a, b, c, d) = (b, {|#0:c|}, {|#1:d|}, default)|};
            }

            public static void Spread([MovedFrom] ref Guard a, [MovedFrom] ref Guard b, ref Guard p, ref Guard q)
            {
                {|#4:(p, q, a, b) = (a, {|#3:b|}, default, default)|};
            }

            public static void ShiftTheWrongWayRound([MovedFrom] ref Guard a, [MovedFrom] ref Guard b, [MovedFrom] ref Guard c)
            {
                {|#6:(c, b, a) = (default, c, {|#5:b|})|};
            }

            // A call on the right is a call between the read and the assignment.
            public static void WithACall([MovedFrom] ref Guard from, ref Guard to, ref int count)
            {
                (to, count) = (from, {|#10:Compute()|});
                from = default;
            }

            private static int Compute() => 1;

            // A swap of two with a third read between them: three values are read, and two of them can be one.
            public static void SwapWithAThirdRead([MovedFrom] ref Guard a, [MovedFrom] ref Guard b, [MovedFrom] ref Guard c, ref Guard x)
            {
                {|#9:(a, x, c) = (c, {|#7:b|}, {|#8:a|})|};
                b = default;
            }
        }
        """;

    // With two parameters that refer to one variable, the right side reads one value twice. That is harmless in two shapes: a swap,
    // and a shift by one in which the parameter that receives the other's value is stored first.
    [TestMethod]
    public Task GroupAssignment_ASwapAndAShiftByOneAreFine() => Verify.AnalyzerAsync(GroupMoves + Sources.Guard);

    [TestMethod]
    public Task GroupAssignment_ThatCanLeaveOneValueInTwoPlacesIsReported() => Verify.AnalyzerAsync(
        SpreadMoves + Sources.Guard,
        Verify.MovedFromParameterNotAssignedAtOnce(2, "d", "an operation that can throw or run other code"),
        Verify.MovedFromParameterNotAssignedAtOnce(0, "b", "a read or a write through another reference"),
        Verify.MovedFromParameterNotAssignedAtOnce(1, "c", "a read or a write through another reference"),
        Verify.MovedFromParameterNotAssignedAtOnce(4, "b", "an operation that can throw or run other code"),
        Verify.MovedFromParameterNotAssignedAtOnce(3, "a", "a read or a write through another reference"),
        Verify.MovedFromParameterNotAssignedAtOnce(6, "b", "an operation that can throw or run other code"),
        Verify.MovedFromParameterNotAssignedAtOnce(5, "c", "a read or a write through another reference"),
        Verify.MovedFromParameterNotAssignedAtOnce(10, "from", "a call"),
        Verify.MovedFromParameterNotAssignedAtOnce(9, "a", "an operation that can throw or run other code"),
        Verify.MovedFromParameterNotAssignedAtOnce(7, "c", "a read or a write through another reference"),
        Verify.MovedFromParameterNotAssignedAtOnce(8, "b", "a read or a write through another reference"));

    // The same methods, run: under every pairing of two parameters that refer to one variable, no value ends in two places after
    // a swap or a shift by one, and one does after the shapes that are reported.
    [TestMethod]
    public void GroupAssignment_AtRunTimeTheFineShapesLeaveOneCopyAndTheReportedOnesTwo()
    {
        const string Driver = """
            public static class Driver
            {
                // The values that are in more than one variable after the call, among the variables that are given.
                private static string Twice(params Guard[] variables)
                {
                    var seen = new System.Collections.Generic.HashSet<int>();
                    var twice = new System.Collections.Generic.SortedSet<int>();

                    foreach (var variable in variables)
                    {
                        if (variable.Value != 0 && !seen.Add(variable.Value))
                            twice.Add(variable.Value);
                    }

                    return twice.Count == 0 ? "none" : string.Join(",", twice);
                }

                private static Guard New(int value) => new Guard { Value = value };

                public static string Run()
                {
                    var lines = new System.Collections.Generic.List<string>();

                    { Guard x = New(1); Moves.Swap(ref x, ref x); lines.Add("swap a=b: " + Twice(x)); }
                    { Guard x = New(1), y = New(2); Moves.Swap(ref x, ref y); lines.Add("swap: " + Twice(x, y) + " " + x.Value + y.Value); }

                    { Guard x = New(1), y = New(2), z = New(3); Moves.Shift(ref x, ref y, ref z); lines.Add("shift: " + Twice(x, y, z) + " " + x.Value + y.Value + z.Value); }
                    { Guard x = New(1), y = New(2); Moves.Shift(ref x, ref x, ref y); lines.Add("shift a=b: " + Twice(x, y)); }
                    { Guard x = New(1), y = New(2); Moves.Shift(ref x, ref y, ref x); lines.Add("shift a=c: " + Twice(x, y)); }
                    { Guard x = New(1), y = New(2); Moves.Shift(ref y, ref x, ref x); lines.Add("shift b=c: " + Twice(x, y)); }
                    { Guard x = New(1); Moves.Shift(ref x, ref x, ref x); lines.Add("shift a=b=c: " + Twice(x)); }

                    { Guard x = New(1), y = New(2); Moves.ShiftBack(ref x, ref x, ref y); lines.Add("shift back a=b: " + Twice(x, y)); }
                    { Guard x = New(1), y = New(2); Moves.ShiftBack(ref x, ref y, ref x); lines.Add("shift back a=c: " + Twice(x, y)); }
                    { Guard x = New(1), y = New(2); Moves.ShiftBack(ref y, ref x, ref x); lines.Add("shift back b=c: " + Twice(x, y)); }

                    { Guard x = New(1), p = default, q = default; Spreads.Spread(ref x, ref x, ref p, ref q); lines.Add("spread a=b: " + Twice(x, p, q)); }
                    { Guard x = New(1), y = New(2), z = New(3); Spreads.ShiftAcrossFour(ref y, ref x, ref z, ref x); lines.Add("shift across four b=d: " + Twice(x, y, z)); }
                    { Guard x = New(1), y = New(2); Spreads.ShiftTheWrongWayRound(ref y, ref x, ref x); lines.Add("shift the wrong way round b=c: " + Twice(x, y)); }
                    { Guard x = New(1), y = New(2), z = default; Spreads.SwapWithAThirdRead(ref x, ref x, ref y, ref z); lines.Add("swap with a third read a=b: " + Twice(x, y, z)); }

                    return string.Join("\n", lines);
                }
            }
            """;

        string spreads = System.Text.RegularExpressions.Regex.Replace(SpreadMoves, @"\{\|#\d+:|\|\}", string.Empty).Replace("using Singulink.CodeAnalysis;", string.Empty);
        string output = CompileAndRun(GroupMoves + spreads + Driver + Sources.Guard, "Driver", "Run");

        string expected = string.Join(
            "\n",
            "swap a=b: none",
            "swap: none 21",
            "shift: none 230",
            "shift a=b: none",
            "shift a=c: none",
            "shift b=c: none",
            "shift a=b=c: none",
            "shift back a=b: none",
            "shift back a=c: none",
            "shift back b=c: none",
            "spread a=b: 1",
            "shift across four b=d: 1",
            "shift the wrong way round b=c: 1",
            "swap with a third read a=b: 1");

        Assert.AreEqual(expected, output);
    }

    private static string CompileAndRun(string source, string type, string method)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(path => Path.GetFileName(path).StartsWith("System.", StringComparison.Ordinal) || Path.GetFileName(path) is "netstandard.dll" or "mscorlib.dll")
            .Append(typeof(NonCopyableAttribute).Assembly.Location)
            .Distinct()
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path));

        var compilation = CSharpCompilation.Create(
            "RunTimeCheck",
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest))],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release));

        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);

        if (!result.Success)
            throw new InvalidOperationException(string.Join(Environment.NewLine, result.Diagnostics));

        stream.Position = 0;
        var context = new AssemblyLoadContext("RunTimeCheck", isCollectible: true);

        try
        {
            var assembly = context.LoadFromStream(stream);
            return (string)assembly.GetType(type)!.GetMethod(method, BindingFlags.Public | BindingFlags.Static)!.Invoke(null, null)!;
        }
        finally
        {
            context.Unload();
        }
    }

    // A by-value read of a container through another reference is a second reference to one task if the two parameters refer to
    // one variable, whether the other reference is known to refer to something or not.
    [TestMethod]
    public Task KnownReference_AContainerReadThroughAnotherReference_IsReported() => Verify.AnalyzerAsync(
        $$"""
        using System.Threading.Tasks;
        using Singulink.CodeAnalysis;

        static class Moves
        {
            static Task<Guard>? Advance([MovedFrom] ref Task<Guard>? current, [MovedFrom] ref Task<Guard>? next)
            {
                NonCopyable.ThrowIfNullRef(in next);
                NonCopyable.ThrowIfNullRef(in current);
                var moving = current;
                var result = {|#0:next|};
                current = null;
                next = moving;
                return result;
            }

            static int Count([MovedFrom] ref Task<Guard>? current, ref Task<Guard>? other, ref int count)
            {
                NonCopyable.ThrowIfNullRef(in count);
                var moving = current;
                int result = count;
                current = null;
                other = moving;
                return result;
            }
        }

        {{Sources.Guard}}
        """,
        Verify.MovedFromParameterNotAssignedAtOnce(0, "current", "a read or a write through another reference"));

    // 'this' in a struct is a reference like every other, and so is a parameter of a primary constructor that the struct keeps:
    // the proofs are a call with 'in this' for a marked parameter, a store through it, and a read whose value is stored. A read
    // into a discard or into a local and an argument of a call prove nothing, for 'this' as for a parameter or a 'ref' local.
    [TestMethod]
    public Task KnownReference_OfThisInAStruct_IsProvedLikeEveryOtherReference() => Verify.AnalyzerAsync(
        $$"""
        using Singulink.CodeAnalysis;

        struct Stats
        {
            static int s_last;

            int _acquired;
            bool _isBusy;
            (int, int) _pair;

            static void Use(int value) { }

            void DiscardOfThis([MovedFrom] ref Guard from, ref Guard to) { _ = this; to = from; {|#0:_acquired|}++; from = default; }

            void DiscardOfAField([MovedFrom] ref Guard from, ref Guard to) { _ = _isBusy; to = from; {|#1:_acquired|}++; from = default; }

            void LocalThatNothingReads([MovedFrom] ref Guard from, ref Guard to) { int before = _acquired; to = from; {|#2:_acquired|}++; from = default; }

            void CopyThatNothingReads([MovedFrom] ref Guard from, ref Guard to) { Stats copy = this; to = from; {|#3:_acquired|}++; from = default; }

            // A local that something names is no better: the compiler drops the read where nothing needs the value.
            void LocalThatADiscardNames([MovedFrom] ref Guard from, ref Guard to) { int before = _acquired; _ = before; to = from; {|#4:_acquired|}++; from = default; }

            void LocalThatIsUsed([MovedFrom] ref Guard from, ref Guard to) { int before = _acquired; Use(before); to = from; {|#5:_acquired|}++; from = default; }

            void InAGroup([MovedFrom] ref Guard from, ref Guard to) { (int one, int before) = (1, _acquired); Use(before); to = from; {|#6:_acquired|}++; from = default; }

            void TakenApart([MovedFrom] ref Guard from, ref Guard to) { (int one, int two) = _pair; Use(one); to = from; {|#9:_acquired|}++; from = default; }

            void CompoundValue([MovedFrom] ref Guard from, ref Guard to) { int total = 0; total += _acquired; to = from; {|#10:_acquired|}++; from = default; }

            void ArgumentOfACreation([MovedFrom] ref Guard from, ref Guard to) { _ = new int?(_acquired); to = from; {|#11:_acquired|}++; from = default; }

            void PassedOn([MovedFrom] ref Guard from, ref Guard to) { Use(_acquired); to = from; {|#12:_acquired|}++; from = default; }

            void StoredInAField([MovedFrom] ref Guard from, ref Guard to) { s_last = _acquired; to = from; _acquired++; from = default; }

            void InAGroupForAField([MovedFrom] ref Guard from, ref Guard to) { (int one, s_last) = (1, _acquired); to = from; _acquired++; from = default; }

            // A field of a local of the method is no place that a read is stored in, nor is a property. A field of a struct in a static
            // field is one.
            static Counter s_counter;

            static int LastCount { get; set; }

            void IntoAFieldOfALocal([MovedFrom] ref Guard from, ref Guard to) { Counter local = default; local.Count = _acquired; to = from; {|#16:_acquired|}++; from = default; }

            void IntoAProperty([MovedFrom] ref Guard from, ref Guard to) { LastCount = _acquired; to = from; {|#17:_acquired|}++; from = default; }

            void IntoAFieldOfAStaticField([MovedFrom] ref Guard from, ref Guard to) { s_counter.Count = _acquired; to = from; _acquired++; from = default; }

            void Helper([MovedFrom] ref Guard from, ref Guard to) { NonCopyable.ThrowIfNullRef(in this); to = from; _acquired++; from = default; }

            void Store([MovedFrom] ref Guard from, ref Guard to) { _isBusy = true; to = from; _acquired++; from = default; }

            // After a commit the judgment is the same.
            void AfterACommit(RwLock rwLock, Registry registry)
            {
                int before = _acquired;
                Guard guard = rwLock.Enter();

                try { registry.Held = {|#7:guard|}; _acquired++; }
                catch { {|#18:guard|}.Dispose(); throw; }

                Use(before);
            }
        }

        struct Tally(int start)
        {
            static void Use(int value) { }

            void Discard([MovedFrom] ref Guard from, ref Guard to) { _ = start; to = from; {|#8:start|}++; from = default; }

            void ReadThatIsUsed([MovedFrom] ref Guard from, ref Guard to) { Use(start); to = from; {|#13:start|}++; from = default; }

            void Helper([MovedFrom] ref Guard from, ref Guard to) { NonCopyable.ThrowIfNullRef(in this); to = from; start++; from = default; }
        }

        class Registry
        {
            public Guard Held;
        }

        static class Parameters
        {
            // Through a parameter or a 'ref' local it is the same.
            static void Discard([MovedFrom] ref Guard from, ref Guard to, ref int count) { _ = count; to = from; {|#14:count|}++; from = default; }

            static void Local([MovedFrom] ref Guard from, ref Guard to, ref int count) { ref int counter = ref count; int before = counter; to = from; {|#15:counter|}++; from = default; }
        }

        struct Counter
        {
            public int Count;
        }

        {{Sources.Guard}}
        """,
        Verify.MovedFromParameterNotAssignedAtOnceThroughReference(0, "from", "this"),
        Verify.MovedFromParameterNotAssignedAtOnceThroughReference(1, "from", "this"),
        Verify.MovedFromParameterNotAssignedAtOnceThroughReference(2, "from", "this"),
        Verify.MovedFromParameterNotAssignedAtOnceThroughReference(3, "from", "this"),
        Verify.MovedFromParameterNotAssignedAtOnceThroughReference(4, "from", "this"),
        Verify.MovedFromParameterNotAssignedAtOnceThroughReference(5, "from", "this"),
        Verify.MovedFromParameterNotAssignedAtOnceThroughReference(6, "from", "this"),
        Verify.MovedFromParameterNotAssignedAtOnceThroughReference(9, "from", "this"),
        Verify.MovedFromParameterNotAssignedAtOnceThroughReference(10, "from", "this"),
        Verify.MovedFromParameterNotAssignedAtOnceThroughReference(11, "from", "this"),
        Verify.UsedAfterHandOffBeforeNullReference(18, "guard", "Guard", "its value was assigned", "this", 7),
        Verify.MovedFromParameterNotAssignedAtOnceThroughReference(8, "from", "this"),
        Verify.MovedFromParameterNotAssignedAtOnceThroughReference(12, "from", "this"),
        Verify.MovedFromParameterNotAssignedAtOnceThroughReference(13, "from", "this"),
        Verify.MovedFromParameterNotAssignedAtOnceThroughReference(14, "from", "count"),
        Verify.MovedFromParameterNotAssignedAtOnceThroughReference(15, "from", "counter"),
        Verify.MovedFromParameterNotAssignedAtOnceThroughReference(16, "from", "this"),
        Verify.MovedFromParameterNotAssignedAtOnceThroughReference(17, "from", "this"));

    // On the right of a group assignment a read proves its reference only as a whole value for a target that is not a local or a
    // discard. An operand proves nothing there either: the compiler folds 'x * 0', and which operators it folds is not tracked.
    [TestMethod]
    public Task GroupAssignment_AReadNextToAConstantProvesNothing() => Verify.AnalyzerAsync(
        $$"""
        using Singulink.CodeAnalysis;

        static class Moves
        {
            const int Stride = 0;

            static void Folded([MovedFrom] ref Guard from, ref Guard to, ref int offset)
            {
                {|#0:(to, offset, from) = (from, offset * Stride, default)|};
            }

            static void Counted([MovedFrom] ref Guard from, ref Guard to, ref int count)
            {
                {|#1:(to, count, from) = (from, count + 1, default)|};
            }

            static void Compared([MovedFrom] ref Guard from, ref Guard to, ref bool over, ref int count)
            {
                {|#2:(to, over, count, from) = (from, count > new int?(), 0, default)|};
            }

            // The whole value, for a target that is no local: the read is made, and the stores through both are known to succeed.
            static void Exchanged([MovedFrom] ref Guard from, ref Guard to, ref int offset, ref int other)
            {
                (to, offset, other, from) = (from, other, offset, default);
            }

            // A check before it is the proof for everything else.
            static void Checked([MovedFrom] ref Guard from, ref Guard to, ref int count)
            {
                NonCopyable.ThrowIfNullRef(in count);
                (to, count, from) = (from, count + 1, default);
            }
        }

        {{Sources.Guard}}
        """,
        Verify.MovedFromParameterNotAssignedAtOnceThroughReference(0, "from", "offset"),
        Verify.MovedFromParameterNotAssignedAtOnceThroughReference(1, "from", "count"),
        Verify.MovedFromParameterNotAssignedAtOnceThroughReference(2, "from", "over"));

    // A handler knows what was known where its 'try' statement began, less everything that the statement makes refer to something
    // else before the handler runs, the filters of earlier clauses included. That holds whatever its first statement is: a handler
    // that starts with a loop has an entry like any other.
    [TestMethod]
    public Task KnownReference_AtTheEntryOfAHandler() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using Singulink.CodeAnalysis;

        static class Moves
        {
            static void Use(int value) { }

            static bool TryReset() => false;

            static bool Skip(in int value) => false;

            static void Fail() { }

            // The proof is inside the 'try', and the 'catch' swallows what it throws.
            static void CatchWithALoop([MovedFrom] ref Guard from, ref Guard to, ref int count)
            {
                try { NonCopyable.ThrowIfNullRef(in count); }
                catch { while (TryReset()) { } }

                to = from;
                {|#0:count|}++;
                from = default;
            }

            static void CatchWithADoLoop([MovedFrom] ref Guard from, ref Guard to, ref int count)
            {
                try { NonCopyable.ThrowIfNullRef(in count); }
                catch (InvalidOperationException) { do { } while (TryReset()); }

                to = from;
                {|#1:count|}++;
                from = default;
            }

            static void FinallyWithALoop([MovedFrom] ref Guard from, ref Guard to, ref int count)
            {
                try { NonCopyable.ThrowIfNullRef(in count); }
                finally { while (TryReset()) { } }

                to = from;
                count++;
                from = default;
            }

            // The filter of an earlier clause runs before a later clause, and what it re-points is not known there or afterwards.
            static void FilterThatRepoints([MovedFrom] ref Guard from, ref Guard to, ref int primary, ref int secondary)
            {
                ref int counter = ref primary;
                counter = 0;

                try { Fail(); }
                catch (InvalidOperationException) when (Skip(in (counter = ref secondary))) { throw; }
                catch (InvalidOperationException) { }

                to = from;
                {|#2:counter|}++;
                from = default;
            }

            // What was known before the 'try' statement is known in its handlers and after it.
            static void KnownBefore([MovedFrom] ref Guard from, ref Guard to, ref int count)
            {
                count = 0;

                try { Fail(); }
                catch (InvalidOperationException) when (TryReset()) { while (TryReset()) { } }
                catch { }

                to = from;
                count++;
                from = default;
            }
        }

        {{Sources.Guard}}
        """,
        Verify.MovedFromParameterNotAssignedAtOnceThroughReference(0, "from", "count"),
        Verify.MovedFromParameterNotAssignedAtOnceThroughReference(1, "from", "count"),
        Verify.MovedFromParameterNotAssignedAtOnceThroughReference(2, "from", "counter"));

    // Taking a reference to a field of 'this' in a struct runs a check of 'this' where the reference is kept. It counts as a read
    // through 'this': able to fail unless 'this' is known.
    [TestMethod]
    public Task KnownReference_AReferenceToAFieldOfThisInAStruct_IsAReadThroughThis() => Verify.AnalyzerAsync(
        $$"""
        using Singulink.CodeAnalysis;

        struct Stats
        {
            int _handovers;

            void HandOver([MovedFrom] ref Guard from, ref Guard to)
            {
                to = from;
                ref int counter = ref {|#0:_handovers|};
                from = default;
                counter++;
            }

            void Known([MovedFrom] ref Guard from, ref Guard to)
            {
                NonCopyable.ThrowIfNullRef(in this);
                to = from;
                ref int counter = ref _handovers;
                from = default;
                counter++;
            }

            void AfterACommit(RwLock rwLock, Registry registry, ref int other)
            {
                scoped ref int counter = ref other;
                Guard guard = rwLock.Enter();

                try { registry.Held = {|#1:guard|}; counter = ref _handovers; }
                catch { {|#2:guard|}.Dispose(); throw; }

                counter++;
            }
        }

        class Counts
        {
            int _handovers;

            // In a class 'this' is an object, which is there.
            void HandOver([MovedFrom] ref Guard from, ref Guard to)
            {
                to = from;
                ref int counter = ref _handovers;
                from = default;
                counter++;
            }
        }

        class Registry
        {
            public Guard Held;
        }

        {{Sources.Guard}}
        """,
        Verify.MovedFromParameterNotAssignedAtOnceThroughReference(0, "from", "this"),
        Verify.UsedAfterHandOffBeforeNullReference(2, "guard", "Guard", "its value was assigned", "this", 1));

    // 'x = ref y', the declaration of a 'ref' local included, is a read through 'y'. Used by value it reads what 'x' then refers to,
    // which no operation stands for, and where nothing reads through 'x' a compiler leaves a read of 'y' in its place. So it can fail
    // unless 'y' is a variable of the method, the [MovedFrom] parameter itself, or known to refer to something.
    [TestMethod]
    public Task KnownReference_ARefAssignmentIsAReadThroughWhatItRefersTo() => Verify.AnalyzerAsync(
        $$"""
        using System.Diagnostics;
        using Singulink.CodeAnalysis;

        static class Moves
        {
            static void Use(int value) { }

            // The local is used only where a release build leaves the call out: the compiler reads 'handovers' in its place.
            static void NothingReadsThroughIt([MovedFrom] ref Guard from, ref Guard to, ref int handovers)
            {
                to = from;
                ref int counter = ref {|#0:handovers|};
                from = default;
                Debug.Assert(counter >= 0);
            }

            static void TheValueOfTheAssignment([MovedFrom] ref Guard from, ref Guard to, ref int primary, ref int secondary)
            {
                ref int counter = ref primary;
                to = from;
                int before = (counter = ref {|#1:secondary|});
                from = default;
                Use(before);
            }

            static void ThroughAnInParameter([MovedFrom] ref Guard from, ref Guard to, in int limit)
            {
                to = from;
                ref readonly int max = ref {|#2:limit|};
                from = default;
                Use(max);
            }

            static void ThroughARefLocal([MovedFrom] ref Guard from, ref Guard to, int[] counts)
            {
                ref int first = ref counts[0];
                first = 0;
                ref int other = ref counts[1];
                to = from;
                ref int counter = ref {|#3:other|};
                from = default;
                counter++;
            }

            // A reference to a variable of the method, and one to the [MovedFrom] parameter itself, read nothing that can fail.
            static void OfTheMethodsOwn([MovedFrom] ref Guard from, ref Guard to, int start)
            {
                int own = start;
                to = from;
                ref int counter = ref own;
                ref int given = ref start;
                ref Guard alias = ref from;
                alias = default;
                counter++;
                given++;
            }

            // A reference that is known to refer to something is read through with no risk.
            static void Known([MovedFrom] ref Guard from, ref Guard to, ref int handovers)
            {
                handovers = 0;
                to = from;
                ref int counter = ref handovers;
                from = default;
                counter++;
            }

            static void AfterACommit(RwLock rwLock, Registry registry, ref int primary, ref int secondary)
            {
                scoped ref int counter = ref primary;
                Guard guard = rwLock.Enter();

                try { registry.Held = {|#4:guard|}; counter = ref secondary; }
                catch { {|#6:guard|}.Dispose(); throw; }

                Debug.Assert(counter >= 0);
            }
        }

        // A parameter of a primary constructor that a struct keeps is a field of 'this', and 'this' in a struct is a reference.
        struct HandoverStats(int handovers)
        {
            public readonly int Handovers => handovers;

            public void HandOver([MovedFrom] ref Guard from, ref Guard to)
            {
                to = from;
                ref int counter = ref {|#5:handovers|};
                from = default;
                counter++;
            }

            public void Known([MovedFrom] ref Guard from, ref Guard to)
            {
                NonCopyable.ThrowIfNullRef(in this);
                to = from;
                ref int counter = ref handovers;
                from = default;
                counter++;
            }
        }

        class Registry
        {
            public Guard Held;
        }

        {{Sources.Guard}}
        """,
        Verify.MovedFromParameterNotAssignedAtOnceThroughReference(0, "from", "handovers"),
        Verify.MovedFromParameterNotAssignedAtOnceThroughReference(1, "from", "secondary"),
        Verify.MovedFromParameterNotAssignedAtOnceThroughReference(2, "from", "limit"),
        Verify.MovedFromParameterNotAssignedAtOnceThroughReference(3, "from", "other"),
        Verify.UsedAfterHandOffBeforeNullReference(6, "guard", "Guard", "its value was assigned", "secondary", 4),
        Verify.MovedFromParameterNotAssignedAtOnceThroughReference(5, "from", "this"));

    // A test for an interface or for a type parameter runs code of an object that answers such a test itself, as one of a class
    // that implements IDynamicInterfaceCastable does. It runs nothing only where the static type of what is tested is a struct or a
    // sealed class. A test for a class or a struct calls nothing.
    [TestMethod]
    public Task KnownReference_ATestForAnInterfaceRunsCodeOfTheObject() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using Singulink.CodeAnalysis;

        interface IPooled { }

        sealed class Sealed { }

        class Open { }

        struct Value { }

        static class Moves
        {
            static void Use(bool value) { }

            static void ForAnInterface([MovedFrom] ref Guard from, ref Guard to, object owner)
            {
                to = from;
                bool isPooled = {|#0:owner is IPooled|};
                from = default;
                Use(isPooled);
            }

            static void ForATypeParameter<TMarker>([MovedFrom] ref Guard from, ref Guard to, object owner)
            {
                to = from;
                bool isMarked = owner is not {|#1:TMarker|};
                from = default;
                Use(isMarked);
            }

            static void ByADeclarationPattern([MovedFrom] ref Guard from, ref Guard to, Open owner)
            {
                to = from;
                bool isPooled = owner is {|#2:IPooled pooled|};
                from = default;
                Use(isPooled);
            }

            static void InsideAPattern([MovedFrom] ref Guard from, ref Guard to, object owner)
            {
                to = from;
                bool isPooled = owner is {|#3:IPooled { }|};
                from = default;
                Use(isPooled);
            }

            // A test for a class or a struct calls nothing, and neither does a test of a struct or of an object of a sealed class.
            static void ThatCallNothing<TStruct, TOpen>([MovedFrom] ref Guard from, ref Guard to, object owner, Sealed closed, Value value, IPooled pooled)
                where TStruct : struct
                where TOpen : Open
            {
                to = from;
                bool a = owner is string;
                bool b = owner is Open;
                bool c = owner is Value;
                bool d = owner is TStruct;
                bool e = owner is TOpen;
                bool f = closed is IPooled;
                bool g = value is IPooled;
                bool h = pooled is IPooled;
                bool i = owner is null;
                from = default;
                Use(a | b | c | d | e | f | g | h | i);
            }

        }

        class Holder
        {
            Guard _held;
            bool _isPooled;

            void AfterACommit(RwLock rwLock, object owner)
            {
                Guard guard = rwLock.Enter();

                try { _held = {|#4:guard|}; _isPooled = owner is IPooled; }
                catch { {|#5:guard|}.Dispose(); throw; }
            }

            void AfterACommitWithATestForAClass(RwLock rwLock, object owner)
            {
                Guard guard = rwLock.Enter();

                try { _held = guard; _isPooled = owner is Open; }
                catch { guard.Dispose(); throw; }
            }
        }

        {{Sources.Guard}}
        """,
        Verify.MovedFromParameterNotAssignedAtOnce(0, "from", "an operation that can throw or run other code"),
        Verify.MovedFromParameterNotAssignedAtOnce(1, "from", "an operation that can throw or run other code"),
        Verify.MovedFromParameterNotAssignedAtOnce(2, "from", "an operation that can throw or run other code"),
        Verify.MovedFromParameterNotAssignedAtOnce(3, "from", "an operation that can throw or run other code"),
        Verify.UsedAfterHandOffInHandler(5, "guard", "Guard", "its value was assigned", 4));

    // [NotNullRef] on a 'ref', 'in', 'ref readonly' or 'out' parameter says that the method returns only if the reference that it is
    // given refers to something. It counts where a call names the code that runs: on a method that is not virtual, static or not, of
    // a class or a struct, a classic extension method and a member of an extension block included, and on a local function. A call
    // that returns proves the argument of every parameter that is so marked, wherever it stands in the call. Every such mark that
    // the project declares is reported once, where it stands: the body is not checked.
    [TestMethod]
    public Task NotNullRef_CountsWhereTheCallNamesTheCodeThatRuns() => Verify.AnalyzerAsync(
        $$"""
        using Singulink.CodeAnalysis;

        interface IChecks
        {
            void Implemented(ref int value);
        }

        class Checks : IChecks
        {
            public static void Static([{|#0:NotNullRef|}] in int value) { }

            public static void ByRefReadOnly([{|#1:NotNullRef|}] ref readonly int value) { }

            public static void Out([{|#2:NotNullRef|}] out int value) => value = 0;

            public static void Both([{|#3:NotNullRef|}] ref int first, int skipped, [{|#4:NotNullRef|}] in int second) { }

            public void Instance([{|#5:NotNullRef|}] ref int value) { }

            // Not virtual, and it implements a member of the interface with nothing written: a call of it by its own name counts.
            public void Implemented([{|#6:NotNullRef|}] ref int value) { }
        }

        sealed class SealedChecks
        {
            public void Check([{|#7:NotNullRef|}] ref int value) { }
        }

        struct StructChecks
        {
            public void Check([{|#8:NotNullRef|}] ref int value) { }
        }

        static class Extensions
        {
            public static void EnsureRef([{|#9:NotNullRef|}] this ref int value) { }

            extension(Checks checks)
            {
                public void Ensure([{|#10:NotNullRef|}] ref int value) { }
            }
        }

        static class Moves
        {
            static void Static([MovedFrom] ref Guard from, ref Guard to, ref int count) { Checks.Static(in count); to = from; count++; from = default; }

            static void ByRefReadOnly([MovedFrom] ref Guard from, ref Guard to, ref int count) { Checks.ByRefReadOnly(in count); to = from; count++; from = default; }

            static void Out([MovedFrom] ref Guard from, ref Guard to, ref int count) { Checks.Out(out count); to = from; count++; from = default; }

            // Wherever the argument stands in the call.
            static void Both([MovedFrom] ref Guard from, ref Guard to, ref int count, ref int total)
            {
                Checks.Both(second: in total, skipped: 0, first: ref count);
                to = from;
                count++;
                total++;
                from = default;
            }

            static void Instance([MovedFrom] ref Guard from, ref Guard to, ref int count, Checks checks) { checks.Instance(ref count); to = from; count++; from = default; }

            static void Implemented([MovedFrom] ref Guard from, ref Guard to, ref int count, Checks checks) { checks.Implemented(ref count); to = from; count++; from = default; }

            static void Sealed([MovedFrom] ref Guard from, ref Guard to, ref int count, SealedChecks checks) { checks.Check(ref count); to = from; count++; from = default; }

            static void Struct([MovedFrom] ref Guard from, ref Guard to, ref int count, StructChecks checks) { checks.Check(ref count); to = from; count++; from = default; }

            static void ClassicExtension([MovedFrom] ref Guard from, ref Guard to, ref int count) { count.EnsureRef(); to = from; count++; from = default; }

            static void ExtensionBlock([MovedFrom] ref Guard from, ref Guard to, ref int count, Checks checks) { checks.Ensure(ref count); to = from; count++; from = default; }

            static void LocalFunction([MovedFrom] ref Guard from, ref Guard to, ref int count)
            {
                Check(ref count);
                to = from;
                count++;
                from = default;

                void Check([{|#11:NotNullRef|}] ref int value) { }
            }

            static void StaticLocalFunction([MovedFrom] ref Guard from, ref Guard to, ref int count)
            {
                Check(in count);
                to = from;
                count++;
                from = default;

                static void Check([{|#12:NotNullRef|}] in int value) { }
            }

            // The two of the attributes package are such methods, of a referenced assembly: nothing is reported for their marks.
            static void OfThePackage([MovedFrom] ref Guard from, ref Guard to, ref int count, ref int total)
            {
                NonCopyable.ThrowIfNullRef(in count);
                NonCopyable.ThrowIfNullRefArgument(in total);
                to = from;
                count++;
                total++;
                from = default;
            }

            // The same method through the interface that it implements binds to the member of the interface, which has no mark and
            // could not have one that counts.
            static void ThroughTheInterface([MovedFrom] ref Guard from, ref Guard to, ref int count, IChecks checks)
            {
                checks.Implemented(ref count);
                to = from;
                {|#13:count|}++;
                from = default;
            }

            // The reference itself is what is passed: a reference to a field of it, and a copy, are something else.
            static void NotTheReferenceItself([MovedFrom] ref Guard from, ref Guard to, ref (int Count, int Total) counts)
            {
                Checks.Static(in counts.Count);
                to = from;
                {|#14:counts|}.Total++;
                from = default;
            }

            // A call inside the arguments of a call that the compiler can leave out never runs.
            static void InACallThatIsLeftOut([MovedFrom] ref Guard from, ref Guard to, ref int count)
            {
                System.Diagnostics.Debug.Assert(IsThere(in count));
                to = from;
                {|#15:count|}++;
                from = default;

                static bool IsThere([{|#16:NotNullRef|}] in int value) => true;
            }
        }

        {{Sources.Guard}}
        """,
        Verify.NotNullRefNotChecked(0, "value", "Checks.Static(in int)"),
        Verify.NotNullRefNotChecked(1, "value", "Checks.ByRefReadOnly(ref readonly int)"),
        Verify.NotNullRefNotChecked(2, "value", "Checks.Out(out int)"),
        Verify.NotNullRefNotChecked(3, "first", "Checks.Both(ref int, int, in int)"),
        Verify.NotNullRefNotChecked(4, "second", "Checks.Both(ref int, int, in int)"),
        Verify.NotNullRefNotChecked(5, "value", "Checks.Instance(ref int)"),
        Verify.NotNullRefNotChecked(6, "value", "Checks.Implemented(ref int)"),
        Verify.NotNullRefNotChecked(7, "value", "SealedChecks.Check(ref int)"),
        Verify.NotNullRefNotChecked(8, "value", "StructChecks.Check(ref int)"),
        Verify.NotNullRefNotChecked(9, "value", "Extensions.EnsureRef(ref int)"),
        Verify.NotNullRefNotChecked(10, "value", "Extensions.extension(Checks).Ensure(ref int)"),
        Verify.NotNullRefNotChecked(11, "value", "Check(ref int)"),
        Verify.NotNullRefNotChecked(12, "value", "Check(in int)"),
        Verify.MovedFromParameterNotAssignedAtOnceThroughReference(13, "from", "count"),
        Verify.MovedFromParameterNotAssignedAtOnceThroughReference(14, "from", "counts"),
        Verify.MovedFromParameterNotAssignedAtOnceThroughReference(15, "from", "count"),
        Verify.NotNullRefNotChecked(16, "value", "IsThere(in int)"));

    // Where the mark does not count it is reported where it stands, and a call proves nothing: on a by-value parameter, on a
    // parameter of anything but a method that is not virtual or a local function, and on a parameter of a method that the compiler
    // can leave out of a build. Such a mark is not reported as a claim that nothing checks as well.
    [TestMethod]
    public Task NotNullRef_IsReportedWhereItDoesNotCount() => Verify.AnalyzerAsync(
        $$"""
        using System.Diagnostics;
        using Singulink.CodeAnalysis;

        interface IChecks
        {
            void Abstract([{|#0:NotNullRef|}] ref int value);

            void Default([{|#1:NotNullRef|}] ref int value) { }

            void Explicit(ref int value);
        }

        abstract class Base
        {
            public virtual void Virtual([{|#2:NotNullRef|}] ref int value) { }

            public abstract void Abstract([{|#3:NotNullRef|}] ref int value);
        }

        sealed class Derived : Base, IChecks
        {
            public override void Virtual([{|#4:NotNullRef|}] ref int value) { }

            public sealed override void Abstract([{|#5:NotNullRef|}] ref int value) { }

            void IChecks.Abstract(ref int value) { }

            void IChecks.Explicit([{|#6:NotNullRef|}] ref int value) { }
        }

        delegate void Checker([{|#7:NotNullRef|}] ref int value);

        sealed class Counter
        {
            public Counter([{|#8:NotNullRef|}] ref int value) { }

            public int this[[{|#9:NotNullRef|}] in int index] => 0;

            public static Counter operator +(Counter counter, [{|#10:NotNullRef|}] in int value) => counter;
        }

        static partial class Checks
        {
            public static void ByValue([{|#11:NotNullRef|}] int value) { }

            [Conditional("DEBUG")]
            public static void WhenDebugging([{|#12:NotNullRef|}] ref int value) { }

            static partial void WithNoBody([{|#13:NotNullRef|}] ref int value);

            public static void CallWithNoBody(ref int value) => WithNoBody(ref value);

            extension([{|#14:NotNullRef|}] ref int value)
            {
                public void Ensure() { }
            }

        // Suppressing the finding for a claim that nothing checks leaves this one standing.
        #pragma warning disable SCA1047
            public static void Suppressed([{|#15:NotNullRef|}] int value) { }
        #pragma warning restore SCA1047
        }

        static class Moves
        {
            static void Virtual([MovedFrom] ref Guard from, ref Guard to, ref int count, Base checks) { checks.Virtual(ref count); to = from; {|#20:count|}++; from = default; }

            static void Abstract([MovedFrom] ref Guard from, ref Guard to, ref int count, Base checks) { checks.Abstract(ref count); to = from; {|#21:count|}++; from = default; }

            static void Override([MovedFrom] ref Guard from, ref Guard to, ref int count, Derived checks) { checks.Virtual(ref count); to = from; {|#22:count|}++; from = default; }

            static void SealedOverride([MovedFrom] ref Guard from, ref Guard to, ref int count, Derived checks) { checks.Abstract(ref count); to = from; {|#23:count|}++; from = default; }

            static void InterfaceMember([MovedFrom] ref Guard from, ref Guard to, ref int count, IChecks checks) { checks.Abstract(ref count); to = from; {|#24:count|}++; from = default; }

            static void DefaultInterfaceMember([MovedFrom] ref Guard from, ref Guard to, ref int count, IChecks checks) { checks.Default(ref count); to = from; {|#25:count|}++; from = default; }

            static void ExplicitImplementation([MovedFrom] ref Guard from, ref Guard to, ref int count, IChecks checks) { checks.Explicit(ref count); to = from; {|#26:count|}++; from = default; }

            static void Delegate([MovedFrom] ref Guard from, ref Guard to, ref int count, Checker checker) { checker(ref count); to = from; {|#27:count|}++; from = default; }

            static void Lambda([MovedFrom] ref Guard from, ref Guard to, ref int count)
            {
                Checker checker = ([{|#16:NotNullRef|}] ref int value) => { };
                checker(ref count);
                to = from;
                {|#28:count|}++;
                from = default;
            }

            static void Constructor([MovedFrom] ref Guard from, ref Guard to, ref int count) { _ = new Counter(ref count); to = from; {|#29:count|}++; from = default; }

            static void Indexer([MovedFrom] ref Guard from, ref Guard to, ref int count, Counter counter) { _ = counter[in count]; to = from; {|#30:count|}++; from = default; }

            static void Operator([MovedFrom] ref Guard from, ref Guard to, ref int count, Counter counter) { _ = counter + count; to = from; {|#31:count|}++; from = default; }

            static void Conditional([MovedFrom] ref Guard from, ref Guard to, ref int count) { Checks.WhenDebugging(ref count); to = from; {|#32:count|}++; from = default; }

            static void WithNoBody([MovedFrom] ref Guard from, ref Guard to, ref int count) { Checks.CallWithNoBody(ref count); to = from; {|#33:count|}++; from = default; }

            static void ReceiverOfAnExtensionBlock([MovedFrom] ref Guard from, ref Guard to, ref int count) { count.Ensure(); to = from; {|#34:count|}++; from = default; }
        }

        {{Sources.Guard}}
        """,
        Verify.NotNullRefMisplaced(0, "value", "IChecks.Abstract(ref int)", Verify.NotNullRefNotOnAMethod),
        Verify.NotNullRefMisplaced(1, "value", "IChecks.Default(ref int)", Verify.NotNullRefNotOnAMethod),
        Verify.NotNullRefMisplaced(2, "value", "Base.Virtual(ref int)", Verify.NotNullRefNotOnAMethod),
        Verify.NotNullRefMisplaced(3, "value", "Base.Abstract(ref int)", Verify.NotNullRefNotOnAMethod),
        Verify.NotNullRefMisplaced(4, "value", "Derived.Virtual(ref int)", Verify.NotNullRefNotOnAMethod),
        Verify.NotNullRefMisplaced(5, "value", "Derived.Abstract(ref int)", Verify.NotNullRefNotOnAMethod),
        Verify.NotNullRefMisplaced(6, "value", "Derived.IChecks.Explicit(ref int)", Verify.NotNullRefNotOnAMethod),
        Verify.NotNullRefMisplaced(7, "value", "Checker", Verify.NotNullRefNotOnAMethod),
        Verify.NotNullRefMisplaced(8, "value", "Counter.Counter(ref int)", Verify.NotNullRefNotOnAMethod),
        Verify.NotNullRefMisplaced(9, "index", "Counter.this[in int]", Verify.NotNullRefNotOnAMethod),
        Verify.NotNullRefMisplaced(10, "value", "Counter.operator +(Counter, in int)", Verify.NotNullRefNotOnAMethod),
        Verify.NotNullRefMisplaced(11, "value", "Checks.ByValue(int)", Verify.NotNullRefByValue),
        Verify.NotNullRefMisplaced(12, "value", "Checks.WhenDebugging(ref int)", Verify.NotNullRefCanBeLeftOut),
        Verify.NotNullRefMisplaced(13, "value", "Checks.WithNoBody(ref int)", Verify.NotNullRefCanBeLeftOut),
        Verify.NotNullRefMisplaced(14, "value", "Checks.extension(ref int)", Verify.NotNullRefNotOnAMethod),
        Verify.NotNullRefMisplaced(15, "value", "Checks.Suppressed(int)", Verify.NotNullRefByValue),
        Verify.NotNullRefMisplaced(16, "value", "lambda expression", Verify.NotNullRefNotOnAMethod),
        Verify.MovedFromParameterNotAssignedAtOnceThroughReference(20, "from", "count"),
        Verify.MovedFromParameterNotAssignedAtOnceThroughReference(21, "from", "count"),
        Verify.MovedFromParameterNotAssignedAtOnceThroughReference(22, "from", "count"),
        Verify.MovedFromParameterNotAssignedAtOnceThroughReference(23, "from", "count"),
        Verify.MovedFromParameterNotAssignedAtOnceThroughReference(24, "from", "count"),
        Verify.MovedFromParameterNotAssignedAtOnceThroughReference(25, "from", "count"),
        Verify.MovedFromParameterNotAssignedAtOnceThroughReference(26, "from", "count"),
        Verify.MovedFromParameterNotAssignedAtOnceThroughReference(27, "from", "count"),
        Verify.MovedFromParameterNotAssignedAtOnceThroughReference(28, "from", "count"),
        Verify.MovedFromParameterNotAssignedAtOnceThroughReference(29, "from", "count"),
        Verify.MovedFromParameterNotAssignedAtOnceThroughReference(30, "from", "count"),
        Verify.MovedFromParameterNotAssignedAtOnceThroughReference(31, "from", "count"),
        Verify.MovedFromParameterNotAssignedAtOnceThroughReference(32, "from", "count"),
        Verify.MovedFromParameterNotAssignedAtOnceThroughReference(33, "from", "count"),
        Verify.MovedFromParameterNotAssignedAtOnceThroughReference(34, "from", "count"));

    // The analyzer knows no check by its name and looks at no body. A method of the project is a check by the mark on its parameter
    // and by nothing else: with the mark any body will do, under any name, which is why the mark is reported until its author has
    // verified the method and suppressed the finding there. Without the mark the very name of the package's method proves nothing.
    [TestMethod]
    [DataRow("[NotNullRef] ", "if (Unsafe.IsNullRef(in reference)) throw new System.NullReferenceException();", true)]
    [DataRow("[NotNullRef] ", "", true)]
    [DataRow("", "if (Unsafe.IsNullRef(in reference)) throw new System.ArgumentNullException(nameof(reference));", false)]
    public Task NotNullRef_IsWhatMakesACheckAndNoMethodIsKnownByName(string mark, string body, bool isKnown)
    {
        string source = $$"""
            namespace Singulink.CodeAnalysis
            {
                using System.Runtime.CompilerServices;

                public static class NonCopyable
                {
            #pragma warning disable SCA1047 // Verified: it throws for a null reference and does nothing else
                    public static void ThrowIfNullRef<[NonCopyable] T>({{mark}}ref readonly T reference)
                    {
                        {{body}}
                    }
            #pragma warning restore SCA1047
                }
            }

            namespace MyLibrary
            {
                using Singulink.CodeAnalysis;

                static class Moves
                {
                    static void Hand([MovedFrom] ref Guard from, ref Guard to, ref int count)
                    {
                        NonCopyable.ThrowIfNullRef(in count);
                        to = from;
                        {{(isKnown ? "count" : "{|#0:count|}")}}++;
                        from = default;
                    }
                }
            }

            {{Sources.Guard}}
            """;

        return isKnown
            ? Verify.AnalyzerAsync(source)
            : Verify.AnalyzerAsync(source, Verify.MovedFromParameterNotAssignedAtOnceThroughReference(0, "from", "count"));
    }

    // The messages of a read or a store that can fail say how to check the reference, between the read and the assignment of a
    // [MovedFrom] parameter and after a commit at the end of a 'try' block. For 'this' in a struct they name 'in this'.
    [TestMethod]
    public Task NotNullRef_TheMessagesSayHowToCheckTheReference() => Verify.AnalyzerAsync(
        $$"""
        using Singulink.CodeAnalysis;

        struct Holder
        {
            int _count;

            void Hand([MovedFrom] ref Guard from, ref Guard to, ref int count)
            {
                to = from;
                {|#0:count|}++;
                from = default;
            }

            void Count([MovedFrom] ref Guard from, ref Guard to)
            {
                to = from;
                {|#1:_count|}++;
                from = default;
            }

            void Acquire(RwLock rwLock, Registry registry, ref int count)
            {
                Guard guard = rwLock.Enter();

                try { registry.Held = {|#2:guard|}; count++; }
                catch { {|#5:guard|}.Dispose(); throw; }
            }

            void AcquireAndCount(RwLock rwLock, Registry registry)
            {
                Guard guard = rwLock.Enter();

                try { registry.Held = {|#3:guard|}; _count++; }
                catch { {|#6:guard|}.Dispose(); throw; }
            }

            // Checked first, both are fine.
            void Checked([MovedFrom] ref Guard from, ref Guard to, RwLock rwLock, Registry registry, ref int count)
            {
                NonCopyable.ThrowIfNullRef(in this);
                NonCopyable.ThrowIfNullRefArgument(in count);
                to = from;
                count++;
                _count++;
                from = default;

                Guard guard = rwLock.Enter();

                try { registry.Held = guard; count++; _count++; }
                catch { guard.Dispose(); throw; }
            }

            // A call after the store is not helped by a check, and the message does not name one.
            void AcquireAndLog(RwLock rwLock, Registry registry)
            {
                Guard guard = rwLock.Enter();

                try { registry.Held = {|#4:guard|}; Log(); }
                catch { {|#7:guard|}.Dispose(); throw; }
            }

            static void Log() { }
        }

        class Registry
        {
            public Guard Held;
        }

        {{Sources.Guard}}
        """,
        Verify.MovedFromParameterNotAssignedAtOnceThroughReference(0, "from", "count"),
        Verify.MovedFromParameterNotAssignedAtOnceThroughReference(1, "from", "this"),
        Verify.UsedAfterHandOffBeforeNullReference(5, "guard", "Guard", "its value was assigned", "count", 2),
        Verify.UsedAfterHandOffBeforeNullReference(6, "guard", "Guard", "its value was assigned", "this", 3),
        Verify.UsedAfterHandOffInHandler(7, "guard", "Guard", "its value was assigned", 4));

    // The two checks of the attributes package: nothing for a reference that refers to something, and for a null reference the one
    // throws a NullReferenceException whose message names the expression, the other an ArgumentNullException that names the argument.
    [TestMethod]
    public void ThrowIfNullRef_ThrowsForANullReferenceOnly()
    {
        int number = 1;
        NonCopyable.ThrowIfNullRef(in number);
        NonCopyable.ThrowIfNullRefArgument(in number);

        var notThere = Assert.ThrowsExactly<NullReferenceException>(static () => Use(ref System.Runtime.CompilerServices.Unsafe.NullRef<int>()));
        Assert.Contains("'counter'", notThere.Message);

        var notGiven = Assert.ThrowsExactly<ArgumentNullException>(static () => UseArgument(ref System.Runtime.CompilerServices.Unsafe.NullRef<int>()));
        Assert.AreEqual("counter", notGiven.ParamName);

        static void Use(ref int counter) => NonCopyable.ThrowIfNullRef(in counter);

        static void UseArgument(ref int counter) => NonCopyable.ThrowIfNullRefArgument(in counter);
    }

    // The compiler knows the answer for an object of a sealed class only where it knows the type that is tested for. A test for a
    // type parameter, or for an interface with a type parameter in it, is made when the code runs, and a sealed class that
    // implements IDynamicInterfaceCastable is asked then. A struct is never asked.
    [TestMethod]
    public Task KnownReference_ATestOfASealedClassForATypeParameterRunsCodeOfTheObject() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using Singulink.CodeAnalysis;

        interface IPooled { }

        interface IPooled<T> { }

        sealed class Sealed { }

        struct Value { }

        static class Moves
        {
            static void Use(bool value) { }

            static void ForATypeParameter<TMarker>([MovedFrom] ref Guard from, ref Guard to, Sealed closed)
            {
                to = from;
                bool isMarked = {|#0:closed is TMarker|};
                from = default;
                Use(isMarked);
            }

            static void ForAnInterfaceOfATypeParameter<TItem>([MovedFrom] ref Guard from, ref Guard to, Sealed closed)
            {
                to = from;
                bool isPooled = {|#1:closed is IPooled<TItem>|};
                from = default;
                Use(isPooled);
            }

            static void ByADeclarationPattern<TItem>([MovedFrom] ref Guard from, ref Guard to, Sealed closed)
            {
                to = from;
                bool isPooled = closed is {|#2:IPooled<TItem[]> pooled|};
                from = default;
                Use(isPooled);
            }

            // A type with no type parameter in it is known to the compiler, and so is the answer. A struct is never asked.
            static void ThatCallNothing<TMarker, TStruct>([MovedFrom] ref Guard from, ref Guard to, Sealed closed, Value value)
                where TStruct : struct
            {
                to = from;
                bool a = closed is IPooled;
                bool b = closed is IPooled<int>;
                bool c = value is TMarker;
                bool d = value is IPooled<TMarker>;
                bool e = closed is TStruct;
                from = default;
                Use(a | b | c | d | e);
            }
        }

        class Holder<TMarker>
        {
            Guard _held;
            bool _isMarked;

            void AfterACommit(RwLock rwLock, Sealed closed)
            {
                Guard guard = rwLock.Enter();

                try { _held = {|#3:guard|}; _isMarked = closed is TMarker; }
                catch { {|#4:guard|}.Dispose(); throw; }
            }

            void AfterACommitWithATestForAKnownType(RwLock rwLock, Sealed closed)
            {
                Guard guard = rwLock.Enter();

                try { _held = guard; _isMarked = closed is IPooled; }
                catch { guard.Dispose(); throw; }
            }
        }

        {{Sources.Guard}}
        """,
        Verify.MovedFromParameterNotAssignedAtOnce(0, "from", "an operation that can throw or run other code"),
        Verify.MovedFromParameterNotAssignedAtOnce(1, "from", "an operation that can throw or run other code"),
        Verify.MovedFromParameterNotAssignedAtOnce(2, "from", "an operation that can throw or run other code"),
        Verify.UsedAfterHandOffInHandler(4, "guard", "Guard", "its value was assigned", 3));

    // What stands in the arguments of a call that the compiler can leave out is found by the syntax, whatever the flow graph makes
    // of a branch in the argument: '&&', '||', '?:', a 'switch' expression, a store, a [Conditional] method or local function of the
    // project, a partial method with no body, and Trace.Assert, which is conditional on TRACE.
    [TestMethod]
    public Task KnownReference_ACallThatCanBeLeftOutProvesNothingWithABranchInItsArguments() => Verify.AnalyzerAsync(
        $$"""
        using System.Diagnostics;
        using Singulink.CodeAnalysis;

        static partial class Moves
        {
            static bool IsThere([{|#8:NotNullRef|}] in int value) => true;

            [Conditional("CHECKS")]
            static void Check(bool condition) { }

            static partial void Log(int value, int other);

            static void And([MovedFrom] ref Guard from, ref Guard to, ref int count)
            {
                Debug.Assert(IsThere(in count) && count >= 0);
                to = from;
                {|#0:count|} = 1;
                from = default;
            }

            static void Or([MovedFrom] ref Guard from, ref Guard to, ref int count, bool flag)
            {
                Debug.Assert(IsThere(in count) || flag);
                to = from;
                {|#1:count|} = 1;
                from = default;
            }

            static void Conditional([MovedFrom] ref Guard from, ref Guard to, ref int count, bool flag)
            {
                Debug.Assert(flag ? (count = 0) == 0 : (count = 1) == 1);
                to = from;
                {|#2:count|} = 1;
                from = default;
            }

            static void Switch([MovedFrom] ref Guard from, ref Guard to, ref int count, int kind)
            {
                Debug.Assert(kind switch { 0 => IsThere(in count), _ => IsThere(in count) });
                to = from;
                {|#3:count|} = 1;
                from = default;
            }

            static void OfTheProject([MovedFrom] ref Guard from, ref Guard to, ref int count, bool flag)
            {
                Check(IsThere(in count) && flag);
                to = from;
                {|#4:count|} = 1;
                from = default;
            }

            static void LocalFunction([MovedFrom] ref Guard from, ref Guard to, ref int count, bool flag)
            {
                Assert(IsThere(in count) && flag);
                to = from;
                {|#5:count|} = 1;
                from = default;

                [Conditional("CHECKS")]
                static void Assert(bool condition) { }
            }

            static void PartialWithNoBody([MovedFrom] ref Guard from, ref Guard to, ref int count, bool flag)
            {
                Log(count = 1, flag ? 1 : 2);
                to = from;
                {|#6:count|} = 1;
                from = default;
            }

            static void Trace([MovedFrom] ref Guard from, ref Guard to, ref int count, bool flag)
            {
                System.Diagnostics.Trace.Assert(IsThere(in count) && flag);
                to = from;
                {|#7:count|} = 1;
                from = default;
            }

            // Inside an ordinary call that stands inside one that can be left out.
            static void Nested([MovedFrom] ref Guard from, ref Guard to, ref int count)
            {
                Debug.Assert(Pass(IsThere(in count)));
                to = from;
                {|#9:count|} = 1;
                from = default;
            }

            static bool Pass(bool value) => value;

            // A call that is not left out proves its argument, with a branch or not.
            static void NotLeftOut([MovedFrom] ref Guard from, ref Guard to, ref int count, bool flag)
            {
                bool known = IsThere(in count) && flag;
                to = from;
                count = 1;
                from = default;
            }
        }

        {{Sources.Guard}}
        """,
        Verify.NotNullRefNotChecked(8, "value", "Moves.IsThere(in int)"),
        Verify.MovedFromParameterNotAssignedAtOnceThroughReference(0, "from", "count"),
        Verify.MovedFromParameterNotAssignedAtOnceThroughReference(1, "from", "count"),
        Verify.MovedFromParameterNotAssignedAtOnceThroughReference(2, "from", "count"),
        Verify.MovedFromParameterNotAssignedAtOnceThroughReference(3, "from", "count"),
        Verify.MovedFromParameterNotAssignedAtOnceThroughReference(4, "from", "count"),
        Verify.MovedFromParameterNotAssignedAtOnceThroughReference(5, "from", "count"),
        Verify.MovedFromParameterNotAssignedAtOnceThroughReference(6, "from", "count"),
        Verify.MovedFromParameterNotAssignedAtOnceThroughReference(7, "from", "count"),
        Verify.MovedFromParameterNotAssignedAtOnceThroughReference(9, "from", "count"));
}
