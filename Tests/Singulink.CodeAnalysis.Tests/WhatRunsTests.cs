using System.Text.RegularExpressions;
using PrefixClassName.MsTest;

namespace Singulink.CodeAnalysis.Tests;

/// <summary>
/// Tests of what the analyzer takes to run no other code and to be unable to throw, kind of operation by kind of operation. Two rules
/// rest on it: a value that is committed at the end of a <c>try</c> block is not followed by the handlers if nothing else can throw
/// before the block ends, and nothing may run between the read of a [MovedFrom] parameter and its assignment.
/// </summary>
/// <remarks>
/// <para>Docs: articles/concepts/last-use.md and articles/guides/moving-a-value.md.</para>
/// Each row is a statement that uses only the parameters and locals of the method. What is not in the first table has to be reported:
/// an operation or an operator that the analyzer has not been taught goes to that side.
/// </remarks>
[PrefixTestClass]
public partial class WhatRunsTests
{
    private const string Throws = "an operation that can throw or run other code";
    private const string Call = "a call";
    private const string Member = "a use of a field or an element that is not the method's own";

    private const string Types = """
        using System;
        using Singulink.CodeAnalysis;

        enum Kind
        {
            First = 1,
            Second = 2,
        }

        struct Point
        {
            public int X;
            public int Y;
        }

        struct Small
        {
            public static Small operator +(Small left, Small right) => default;
            public static Small operator -(Small value) => default;
            public static implicit operator Big(Small small) => default;
        }

        struct Big { }

        class Holder
        {
            public static int Shared;
            public int Count;

            public void Deconstruct(out int first, out int second) => (first, second) = (1, 2);
        }
        """;

    private const string Parameters =
        "int i, int j, long big, double real, bool flag, char letter, Kind kind, int? maybe, decimal money, string text, string other, " +
        "object boxed, Small small, int[] numbers, (int, long) pair, Point point, Holder holder, byte tiny, short low, byte? maybeTiny, " +
        "sbyte? maybeSigned, short? maybeLow, ushort? maybeUnsigned, char? maybeLetter";

    /// <summary>
    /// Gets the statements that run nothing: one row for each kind of operation, of operator and of conversion that is let through. The
    /// first rows are the operators that are one instruction on the simple types.
    /// </summary>
    public static IEnumerable<object[]> RunsNothing =>
    [
        ["int result = i + j;"],
        ["int result = i - j;"],
        ["int result = i * j;"],
        ["int result = i & j;"],
        ["int result = i | j;"],
        ["int result = i ^ j;"],
        ["int result = i << 2;"],
        ["int result = i >> 2;"],
        ["int result = i >>> 2;"],
        ["bool result = i == j;"],
        ["bool result = i != j;"],
        ["bool result = i < j;"],
        ["bool result = i <= j;"],
        ["bool result = i > j;"],
        ["bool result = i >= j;"],
        ["bool result = flag & i > 0 | flag ^ true;"],
        ["double result = real * 2.5 + real - 1;"],
        ["int result = letter + 1;"],
        ["Kind result = kind | Kind.Second;"],
        ["bool result = kind == Kind.First;"],
        ["int? result = maybe + 1;"],
        ["int result = +i;"],
        ["int result = -i;"],
        ["int result = ~i;"],
        ["bool result = !flag;"],

        // The same operators as assignments, and the increments.
        ["i += j;"],
        ["i -= j;"],
        ["i *= j;"],
        ["i &= j;"],
        ["i |= j;"],
        ["i ^= j;"],
        ["i <<= 1;"],

        // On a type that is narrower than 'int' the result is converted back, which cannot throw outside a checked context.
        ["tiny <<= 1;"],
        ["tiny |= 1;"],
        ["low >>= 1;"],
        ["tiny++;"],
        ["unchecked { tiny <<= 1; }"],
        ["checked { unchecked { tiny <<= 1; low <<= 1; tiny++; } }"],
        ["checked { tiny = unchecked(tiny <<= 1); }"],
        ["byte result = tiny <<= 1;"],
        ["checked { i <<= 1; }"],
        ["maybeTiny <<= 1;"],
        ["unchecked { maybeLetter >>>= 1; }"],
        ["checked { i |= j; }"],
        ["i >>= 1;"],
        ["i >>>= 1;"],
        ["real += 1.5;"],
        ["i++;"],
        ["--i;"],
        ["int result = i++ + --j;"],

        // A test for null and a comparison of two references.
        ["bool result = boxed == null;"],
        ["bool result = boxed != holder;"],

        // Strings: the comparison of two strings, a method of the framework that cannot throw, and a concatenation that the
        // compiler folds to a constant.
        ["bool result = text == other;"],
        ["string result = \"held by \" + \"the lock\";"],
        ["string result = \"held by \" + nameof(text);"],

        // The conversions that the language makes with no other code.
        ["long result = i;"],
        ["double result = i;"],
        ["int result = letter;"],
        ["int? result = i;"],
        ["long? result = maybe;"],
        ["object result = i;"],
        ["object result = point;"],
        ["object result = text;"],
        ["IComparable result = text;"],
        ["Kind result = 0;"],
        ["byte result = 200;"],
        ["string result = null;"],
        ["Point? result = point;"],

        // Constants, literals and what the compiler works out.
        ["int result = 3;"],
        ["int result = int.MaxValue;"],
        ["Kind result = Kind.First;"],
        ["string result = nameof(i);"],
        ["int result = sizeof(long);"],
        ["Type result = typeof(Guard);"],
        ["const int Limit = 4; int result = Limit + i;"],

        // The default value, and a struct that is all zeros.
        ["int result = default;"],
        ["Point result = default(Point);"],
        ["Point result = new Point();"],
        ["Point result = new();"],

        // Tuples: a literal, its conversion element by element, and a deconstruction with no Deconstruct method.
        ["var result = (i, big);"],
        ["(long, long) result = (i, big);"],
        ["(i, j) = (j, i);"],
        ["long first; object second; (first, second) = (i, text);"],
        ["var (first, second) = pair;"],
        ["(int first, long second) = pair;"],
        ["(i, big) = pair;"],
        ["(i, (j, big)) = (j, pair);"],
        ["(_, big) = pair;"],

        // Tests of a value: 'is' with a type and with a pattern that calls nothing.
        ["bool result = boxed is string;"],
        ["bool result = boxed is null;"],
        ["bool result = boxed is not null;"],
        ["bool result = i is 3 or > 5 and < 9;"],
        ["bool result = i is >= 0 and <= 4 or not 7;"],
        ["bool result = boxed is Point { X: 0 };"],
        ["bool result = boxed is int number;"],
        ["bool result = boxed is Holder found;"],
        ["bool result = maybe is { } some;"],
        ["bool result = pair is (1, 2L);"],
        ["bool result = text is \"x\";"],
        ["bool result = boxed is var anything;"],

        // The method's own variables: a local, a field of one that is a struct, a reference to one, a discard.
        ["int result = i; result = j;"],
        ["int result = point.X; point.Y = result;"],
        ["int result = pair.Item1;"],
        ["ref int result = ref i;"],
        ["ref int result = ref point.X;"],
        ["_ = i;"],
    ];

    /// <summary>
    /// Gets the statements that can throw or run other code: the close relatives of the rows above. Each has the place marked at which
    /// the [MovedFrom] rule reports it, and the words that the message has for it. The first rows: '^n' makes an Index, whose constructor
    /// throws for a negative number, and a range converts its ends to one.
    /// </summary>
    public static IEnumerable<object[]> RunsSomething =>
    [
        ["Index result = {|#0:^i|};", Throws],
        ["Range result = {|#0:i|}..j;", Call],
        ["Range result = {|#0:..|};", Throws],

        // Division by zero, and arithmetic that is checked.
        ["int result = {|#0:i / j|};", Throws],
        ["int result = {|#0:i % j|};", Throws],
        ["double result = {|#0:real / 2|};", Throws],
        ["{|#0:i /= j|};", Throws],
        ["{|#0:i %= j|};", Throws],
        ["int result = checked({|#0:i + j|});", Throws],
        ["int result = checked({|#0:i * j|});", Throws],
        ["int result = checked({|#0:-i|});", Throws],
        ["checked { {|#0:i += j|}; }", Throws],
        ["checked { {|#0:i++|}; }", Throws],

        // The conversion back to a type that is narrower than 'int' is checked where the operator is not.
        ["checked { {|#0:tiny <<= 1|}; }", Throws],
        ["checked { {|#0:low <<= 1|}; }", Throws],
        ["checked { {|#0:tiny |= 1|}; }", Throws],
        ["int result = checked({|#0:tiny <<= 1|});", Throws],
        ["unchecked { checked { {|#0:tiny <<= 1|}; } }", Throws],
        ["checked { {|#0:tiny++|}; }", Throws],
        ["checked { {|#0:--low|}; }", Throws],
        ["checked { {|#0:letter++|}; }", Throws],

        // And the conversion back to a nullable narrow type, which the compiler classes as a nullable conversion.
        ["checked { {|#0:maybeTiny <<= 1|}; }", Throws],
        ["checked { {|#0:maybeSigned >>>= 1|}; }", Throws],
        ["checked { {|#0:maybeLow <<= 2|}; }", Throws],
        ["checked { {|#0:maybeUnsigned >>>= 1|}; }", Throws],
        ["checked { {|#0:maybeLetter <<= 1|}; }", Throws],

        // 'decimal' is a struct with methods for its operators and conversions.
        ["decimal result = {|#0:money + money|};", Throws],
        ["decimal result = money + {|#0:1|};", Call],
        ["decimal result = {|#0:i|};", Call],
        ["decimal result = {|#0:-money|};", Throws],
        ["{|#0:money++|};", Throws],
        ["{|#0:money += money|};", Throws],
        ["bool result = {|#0:money > money|};", Throws],

        // User-defined operators and conversions.
        ["Small result = {|#0:small + small|};", Call],
        ["Small result = {|#0:-small|};", Call],
        ["Big result = {|#0:small|};", Call],

        // Strings: the concatenation of two strings throws where the result would be longer than a string can be.
        ["string result = {|#0:text + other|};", Throws],
        ["{|#0:text += other|};", Throws],
        ["string result = {|#0:\"held by \" + text|};", Throws],

        // A concatenation with anything but a string calls ToString, and an interpolation formats.
        ["string result = {|#0:text + boxed|};", Call],
        ["string result = {|#0:text + i|};", Call],
        ["{|#0:text += boxed|};", Call],
        ["string result = $\"{|#0:{text}|}\";", Throws],

        // The conversions that can fail or that run code.
        ["int result = {|#0:(int)boxed|};", Throws],
        ["int result = {|#0:(int)big|};", Throws],
        ["Kind result = {|#0:(Kind)i|};", Throws],
        ["string result = {|#0:(string)boxed|};", Throws],
        ["int result = {|#0:(int)maybe|};", Throws],
        ["(long, long) result = {|#0:pair|};", Call],
        ["(Big, int) result = ({|#0:small|}, i);", Call],
        ["Span<int> result = {|#0:numbers|};", Throws],

        // A deconstruction that calls Deconstruct, at any depth, or that converts what it reads.
        ["{|#0:var (first, second) = holder|};", Call],
        ["{|#0:(i, j) = holder|};", Call],
        ["{|#0:(big, (i, j)) = (big, holder)|};", Call],
        ["long first; long second; {|#0:(first, second) = pair|};", Call],

        // Creations.
        ["Holder result = {|#0:new Holder()|};", Call],
        ["int[] result = {|#0:new int[2]|};", Throws],
        ["int[] result = new[] {|#0:{ i }|};", Throws],
        ["int[] result = {|#0:[i]|};", Throws],
        ["Span<int> result = {|#0:stackalloc int[2]|};", Throws],
        ["object result = new { {|#0:i|} };", Call],
        ["Func<int> result = {|#0:() => 1|};", Throws],

        // What is not the method's own: an element, a field of another object or of a type, a property, a call.
        ["int result = {|#0:numbers[0]|};", Member],
        ["{|#0:numbers[0]|} = i;", Member],
        ["int result = {|#0:holder.Count|};", Member],
        ["{|#0:holder.Count|} = i;", Member],
        ["int result = {|#0:Holder.Shared|};", Member],
        ["ref int result = ref {|#0:holder.Count|};", Member],
        ["ref int result = ref {|#0:Holder.Shared|};", Member],
        ["ref int result = ref {|#0:numbers[0]|};", Member],
        ["int result = {|#0:text.Length|};", Call],
        ["int result = {|#0:Math.Abs(i)|};", Call],
        ["bool result = boxed is string { {|#0:Length|}: 1 };", Call],
        ["bool result = numbers is {|#0:[1]|};", Throws],
        ["bool result = holder is {|#0:(1, 2)|};", Call],
        ["bool result = boxed is {|#0:()|};", Call],
    ];

    /// <summary>
    /// Gets the statements that the commit rule reports because the code branches after the store: what follows in the 'try' block is no
    /// longer the end of it.
    /// </summary>
    public static IEnumerable<object[]> Branches =>
    [
        ["int? result = text?.Length;"],
        ["int result = flag ? i : j;"],
        ["string result = text ?? other;"],
        ["bool result = flag && i > 0;"],
        ["bool result = flag || i > 0;"],
        ["if (flag) i = 1;"],
        ["int result = kind switch { Kind.First => 1, _ => 2 };"],
    ];

    [TestMethod]
    [DynamicData(nameof(RunsNothing))]
    public Task Commit_WhatRunsNothingCanFollowTheStore(string statement) => Verify.AnalyzerAsync(GetCommit(statement, isReported: false));

    [TestMethod]
    [DynamicData(nameof(RunsSomething))]
    public Task Commit_WhatCanThrowAfterTheStoreLeavesTheHandlers(string statement, string what)
    {
        _ = what;
        return Verify.AnalyzerAsync(GetCommit(statement, isReported: true), Verify.CopyByAssignment(0, "Guard", usedByHandler: "guard"));
    }

    [TestMethod]
    [DynamicData(nameof(Branches))]
    public Task Commit_ABranchAfterTheStoreLeavesTheHandlers(string statement) =>
        Verify.AnalyzerAsync(GetCommit(statement, isReported: true), Verify.CopyByAssignment(0, "Guard", usedByHandler: "guard"));

    [TestMethod]
    [DynamicData(nameof(RunsNothing))]
    public Task MovedFrom_WhatRunsNothingCanComeBetweenTheReadAndTheAssignment(string statement) => Verify.AnalyzerAsync(GetMove(statement));

    [TestMethod]
    [DynamicData(nameof(RunsSomething))]
    public Task MovedFrom_WhatCanThrowBetweenTheReadAndTheAssignmentIsReported(string statement, string what) =>
        Verify.AnalyzerAsync(GetMove(statement), Verify.MovedFromParameterNotAssignedAtOnce(0, "value", what));

    // The shape "acquire, set up, commit, or dispose on failure", with the statement between the store and the end of the 'try'.
    private static string GetCommit(string statement, bool isReported) => $$"""
        {{Types}}

        unsafe class C
        {
            Guard _held;

            static void Work() { }

            void M(RwLock rwLock, {{Parameters}})
            {
                Guard guard = rwLock.Enter();

                try { Work(); _held = {{(isReported ? "{|#0:guard|}" : "guard")}}; {{MarkedPlace().Replace(statement, "$1")}} }
                catch { guard.Dispose(); throw; }
            }
        }

        {{Sources.Guard}}
        """;

    // A [MovedFrom] method with the statement between the read of the parameter and its assignment.
    private static string GetMove(string statement) => $$"""
        {{Types}}

        static unsafe class Moves
        {
            static Guard Take([MovedFrom] ref Guard value, {{Parameters}})
            {
                Guard moved = value;
                {{statement}}
                value = default;
                return moved;
            }
        }

        {{Sources.Guard}}
        """;

    [GeneratedRegex(@"\{\|#0:(.*?)\|\}")]
    private static partial Regex MarkedPlace();

    // With no 'checked' and no 'unchecked' around it, an operation is checked as the project is compiled: <CheckForOverflowUnderflow>
    // makes every conversion back to a narrow type one that can throw, and 'unchecked' takes that away again.
    [TestMethod]
    public Task MovedFrom_ConversionBackToANarrowType_IsCheckedAsTheProjectIsCompiled()
    {
        var test = new Verify.Test
        {
            ChecksOverflow = true,
            TestCode = $$"""
                using Singulink.CodeAnalysis;

                static class Moves
                {
                    static Guard Shift([MovedFrom] ref Guard value, ref byte slot)
                    {
                        NonCopyable.ThrowIfNullRef(in slot);
                        Guard moved = value;
                        {|#0:slot <<= 1|};
                        value = default;
                        return moved;
                    }

                    static Guard Increment([MovedFrom] ref Guard value, short count)
                    {
                        Guard moved = value;
                        {|#1:count++|};
                        value = default;
                        return moved;
                    }

                    static Guard Unchecked([MovedFrom] ref Guard value, byte slot, short count)
                    {
                        Guard moved = value;
                        unchecked { slot <<= 1; count++; }
                        value = default;
                        return moved;
                    }

                    static Guard OnAnInt([MovedFrom] ref Guard value, int number)
                    {
                        Guard moved = value;
                        number <<= 1;
                        value = default;
                        return moved;
                    }
                }

                class Holder
                {
                    Guard _held;
                    byte _slot;

                    void Commit(RwLock rwLock)
                    {
                        Guard guard = rwLock.Enter();

                        try { _held = {|#2:guard|}; _slot <<= 1; }
                        catch { guard.Dispose(); throw; }
                    }

                    void CommitUnchecked(RwLock rwLock)
                    {
                        Guard guard = rwLock.Enter();

                        try { _held = guard; unchecked { _slot <<= 1; } }
                        catch { guard.Dispose(); throw; }
                    }
                }

                {{Sources.Guard}}
                """,
        };

        test.ExpectedDiagnostics.Add(Verify.MovedFromParameterNotAssignedAtOnce(0, "value", Throws));
        test.ExpectedDiagnostics.Add(Verify.MovedFromParameterNotAssignedAtOnce(1, "value", Throws));
        test.ExpectedDiagnostics.Add(Verify.CopyByAssignment(2, "Guard", usedByHandler: "guard"));
        return test.RunAsync();
    }

    // The same code in a project that is compiled without overflow checks, where it is the 'checked' block that makes the difference.
    [TestMethod]
    public Task Commit_ShiftOfANarrowField_ThrowsOnlyInACheckedContext() => Verify.AnalyzerAsync(
        $$"""
        class Holder
        {
            Guard _held;
            byte _slot;

            void Commit(RwLock rwLock)
            {
                Guard guard = rwLock.Enter();

                try { _held = guard; _slot <<= 1; }
                catch { guard.Dispose(); throw; }
            }

            void CommitChecked(RwLock rwLock)
            {
                Guard guard = rwLock.Enter();

                checked
                {
                    try { _held = {|#0:guard|}; _slot <<= 1; }
                    catch { guard.Dispose(); throw; }
                }
            }

            // A lambda is inside the 'checked' block that it is written in.
            void CommitInALambda(RwLock rwLock)
            {
                checked
                {
                    System.Action commit = () =>
                    {
                        Guard guard = rwLock.Enter();

                        try { _held = {|#1:guard|}; _slot <<= 1; }
                        catch { guard.Dispose(); throw; }
                    };
                }
            }
        }

        {{Sources.Guard}}
        """,
        Verify.CopyByAssignment(0, "Guard", usedByHandler: "guard"),
        Verify.CopyByAssignment(1, "Guard", usedByHandler: "guard"));

    // The concatenation of two strings after a commit can throw: its result can be longer than a string can be, which the runtime
    // reports with an OutOfMemoryException that can be caught. Running out of memory is not counted; a length is. One that the
    // compiler folds to a constant runs nothing.
    [TestMethod]
    public Task Commit_AConcatenationOfStringsAfterTheStoreLeavesTheHandlers() => Verify.AnalyzerAsync(
        $$"""
        class Holder
        {
            const string Prefix = "held by ";

            Guard _held;
            string _label = "";
            string _prefix = "";
            string _suffix = "";
            object _boxed = 0;

            void Concatenated(RwLock rwLock)
            {
                Guard g = rwLock.Enter();
                try { _held = {|#0:g|}; _label = _prefix + _suffix; }
                catch { g.Dispose(); throw; }
            }

            void Appended(RwLock rwLock)
            {
                Guard g = rwLock.Enter();
                try { _held = {|#1:g|}; _label += _suffix; }
                catch { g.Dispose(); throw; }
            }

            void Folded(RwLock rwLock)
            {
                Guard g = rwLock.Enter();
                try { _held = g; _label = Prefix + "the lock"; }
                catch { g.Dispose(); throw; }
            }

            // Boxing can only run out of memory, which is not counted.
            void Boxed(RwLock rwLock)
            {
                Guard g = rwLock.Enter();
                try { _held = g; _boxed = 1; }
                catch { g.Dispose(); throw; }
            }
        }

        {{Sources.Guard}}
        """,
        Verify.CopyByAssignment(0, "Guard", usedByHandler: "g"),
        Verify.CopyByAssignment(1, "Guard", usedByHandler: "g"));
}
