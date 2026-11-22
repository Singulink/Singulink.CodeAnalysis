using PrefixClassName.MsTest;

namespace Singulink.CodeAnalysis.Tests;

/// <summary>
/// Tests of the copies that the compiler makes where the source shows one value: an awaiter, an argument that goes to two places, a
/// combined delegate, the clauses of a <c>switch</c>, a spread, and the members that it calls on the receiver of an extension.
/// </summary>
/// <remarks>
/// Docs: articles/concepts/compiler-copies.md.
/// </remarks>
[PrefixTestClass]
public class CompilerCopyTests
{
    [TestMethod]
    public Task Await_WithAnAwaiterOfANonCopyableTypeIsReported() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using System.Runtime.CompilerServices;
        using System.Threading.Tasks;
        using Singulink.CodeAnalysis;

        readonly struct Reservation
        {
            public ReservingAwaiter GetAwaiter() => default;
        }

        [NonCopyable]
        struct ReservingAwaiter : INotifyCompletion
        {
            public bool IsCompleted => false;

            public void OnCompleted(Action continuation) { }

            public void GetResult() { }
        }

        [NonCopyable]
        struct OwnedReservation
        {
            public ReservingAwaiter GetAwaiter() => default;
        }

        class C
        {
            // When the 'await' suspends, the compiler copies the awaiter into the state machine, whatever is awaited.
            async Task M(RwLock rwLock)
            {
                {|#0:await new Reservation()|};

                var owned = new OwnedReservation();
                {|#1:await owned|};

                // The awaiters of the framework hold a reference to their task.
                using Guard guard = await rwLock.EnterAsync();
                await Task.Yield();
            }
        }

        {{Sources.Guard}}
        """,
        Verify.NonCopyableAwaiter(0, "ReservingAwaiter"),
        Verify.NonCopyableAwaiter(1, "ReservingAwaiter"));

    [TestMethod]
    public Task Switch_OneValueGoesToOneVariableAcrossTheClauses() => Verify.AnalyzerAsync(
        $$"""
        class C
        {
            static bool TryRegister(Guard guard) => false;
            static bool TryRelease(ref Guard guard) => false;

            // A clause with a 'when' runs code with its variable and then lets a later clause bind the value again.
            void Statement(RwLock rwLock)
            {
                switch (rwLock.Enter())
                {
                    case var registered when TryRegister(registered):
                        break;

                    case var {|#0:rejected|}:
                        rejected.Dispose();
                        break;
                }
            }

            void UsedInPlace(RwLock rwLock)
            {
                switch (rwLock.Enter())
                {
                    case var first when TryRelease(ref first):
                        break;

                    case var {|#1:second|}:
                        second.Dispose();
                        break;
                }
            }

            int Expression(RwLock rwLock) => rwLock.Enter() switch {
                var first when TryRegister(first) => 1,
                var {|#2:second|} => second.Value,
            };

            void OfATuple(RwLock rwLock)
            {
                switch ((rwLock.Enter(), 1))
                {
                    case (var first, _) when TryRegister(first):
                        break;

                    case (var {|#3:second|}, _):
                        second.Dispose();
                        break;
                }
            }

            // Without a 'when' the variable of a clause is never used when a later clause runs, and parts that are apart do not meet.
            void NoWhen(RwLock rwLock)
            {
                switch (rwLock.TryEnter())
                {
                    case null:
                        break;

                    case { } entered:
                        entered.Dispose();
                        break;
                }

                switch ((rwLock.Enter(), rwLock.Enter()))
                {
                    case (var first, _) when TryRelease(ref first):
                        break;

                    case (_, var second):
                        second.Dispose();
                        break;
                }
            }
        }

        {{Sources.Guard}}
        """,
        Verify.CopyByAssignmentIntoSecondVariable(0, "Guard", "rejected"),
        Verify.CopyByAssignmentIntoSecondVariable(1, "Guard", "second"),
        Verify.CopyByAssignmentIntoSecondVariable(2, "Guard", "second"),
        Verify.CopyByAssignmentIntoSecondVariable(3, "Guard", "second"));

    [TestMethod]
    public Task Delegates_ThatAreCombinedCopyAByValueArgument() => Verify.AnalyzerAsync(
        $$"""
        using System;

        delegate void Taker(Guard guard);

        delegate void Looker(in Guard guard);

        class C
        {
            event Action<Guard> Entered;
            event Looker Looked;

            static void Close(Guard guard) => guard.Dispose();
            static void Look(in Guard guard) { }

            // Every target of a delegate with several targets gets a copy of its own of a by-value argument.
            void M(RwLock rwLock)
            {
                Action<Guard> onEntered = Close;
                {|#0:onEntered += Close|};
                Taker taker = Close;
                Taker both = {|#1:taker + taker|};
                {|#2:Entered += Close|};

                // A delegate that takes the value by reference copies nothing, and neither does taking a target away.
                Looker looker = Look;
                looker += Look;
                Looked += Look;
                onEntered -= Close;
                Entered -= Close;

                Action plain = () => { };
                plain += () => { };
            }
        }

        {{Sources.Guard}}
        """,
        Verify.DelegatesCombined(0, "System.Action<Guard>", "Guard"),
        Verify.DelegatesCombined(1, "Taker", "Guard"),
        Verify.DelegatesCombined(2, "System.Action<Guard>", "Guard"));

    [TestMethod]
    public Task Operators_UserDefinedAndAlsoAndOrElsePassTheLeftOperandTwice() => Verify.AnalyzerAsync(
        """
        using Singulink.CodeAnalysis;

        [NonCopyable]
        struct Flag
        {
            public static Flag Create() => default;

            public static Flag operator &(Flag left, Flag right) => left;

            public static Flag operator |(Flag left, Flag right) => left;

            public static bool operator true(Flag flag) => true;

            public static bool operator false(Flag flag) => false;
        }

        class C
        {
            // 'x && y' is 'operator false(x) ? x : operator &(x, y)'.
            void M()
            {
                Flag both = {|#0:Flag.Create()|} && Flag.Create();
                Flag either = {|#1:Flag.Create()|} || Flag.Create();

                // The operator by itself is a call with two arguments.
                Flag and = Flag.Create() & Flag.Create();
            }
        }
        """,
        Verify.ValuePassedTwice(0, "Flag", "'operator false' and then to 'Flag.operator &(Flag, Flag)' or on as the result"),
        Verify.ValuePassedTwice(1, "Flag", "'operator true' and then to 'Flag.operator |(Flag, Flag)' or on as the result"));

    [TestMethod]
    public Task Handler_InAReadOnlyMember_RunsItsMembersInPlace() => Verify.AnalyzerAsync(
        """
        using System.Globalization;
        using System.Runtime.CompilerServices;
        using Singulink.CodeAnalysis;

        [InterpolatedStringHandler]
        [NonCopyable]
        ref struct Message
        {
            public Message(int literalLength, int formattedCount) { }

            public void AppendLiteral(string text) { }

            public void AppendFormatted<T>(T value) { }
        }

        static class Log
        {
            public static void Write(Message message) { }
        }

        // The handler that the compiler builds an interpolated string in is a temporary of its own and not 'this', so its members run
        // on it in place, whatever the member around it may write to.
        readonly record struct Cursor(long Since, long After)
        {
            public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Since}-{After}");

            public string Padded() => string.Create(CultureInfo.InvariantCulture, stackalloc char[64], $"{Since,8}-{After:X}");

            public void Write() => Log.Write($"{Since}-{After}");
        }

        struct Counter
        {
            public long Count;

            public readonly string Text() => string.Create(CultureInfo.InvariantCulture, $"count {Count}");

            public readonly void Write() => Log.Write($"count {Count}");

            public string TextOfAMemberThatWrites() => string.Create(CultureInfo.InvariantCulture, $"count {++Count}");
        }

        sealed class Holder
        {
            public long Count;

            public string Text() => string.Create(CultureInfo.InvariantCulture, $"count {Count}");

            public void Write() => Log.Write($"count {Count}");
        }
        """);

    [TestMethod]
    public Task Pattern_InAReadOnlyMember_IsJudgedAsInAnyOtherMember() => Verify.AnalyzerAsync(
        """
        using Singulink.CodeAnalysis;

        [NonCopyable]
        struct Flag
        {
            private int _reads;

            public bool IsSet => ++_reads > 0;
        }

        // What a pattern reads from is a temporary of the compiler's and not 'this'. The copy is reported, in the same way in both
        // members, and the getter is not reported again as one that runs on a copy.
        struct Holder
        {
            public readonly bool ReadOnlyMember(ref Flag flag) => flag is {|#0:{ IsSet: true }|};

            public bool OtherMember(ref Flag flag) => flag is {|#1:{ IsSet: true }|};
        }
        """,
        Verify.CopyOfStoredValue(0, "The pattern reads from", "Flag"),
        Verify.CopyOfStoredValue(1, "The pattern reads from", "Flag"));

    [TestMethod]
    public Task Handler_AnArgumentThatTheHandlerAlsoReceivesIsPassedTwice() => Verify.AnalyzerAsync(
        $$"""
        using System.Runtime.CompilerServices;
        using Singulink.CodeAnalysis;

        [InterpolatedStringHandler]
        ref struct ReleaseMessage
        {
            public ReleaseMessage(int literalLength, int formattedCount, Guard guard) { }

            public void AppendLiteral(string text) { }
        }

        [InterpolatedStringHandler]
        ref struct LookMessage
        {
            public LookMessage(int literalLength, int formattedCount, in Guard guard) { }

            public void AppendLiteral(string text) { }
        }

        [InterpolatedStringHandler]
        ref struct SessionMessage
        {
            public SessionMessage(int literalLength, int formattedCount, Session session) { }

            public void AppendLiteral(string text) { }
        }

        [NonCopyable]
        struct Session
        {
            public void Log([InterpolatedStringHandlerArgument("")] SessionMessage message) { }
        }

        sealed class Logger
        {
            public Logger(Guard guard, [InterpolatedStringHandlerArgument("guard")] ReleaseMessage message) { }
        }

        class C
        {
            static void Release(Guard guard, [InterpolatedStringHandlerArgument("guard")] ReleaseMessage message) => guard.Dispose();
            static void Look(in Guard guard, [InterpolatedStringHandlerArgument("guard")] LookMessage message) { }

            // The compiler passes the argument to the method and again to the constructor of the handler.
            void M(RwLock rwLock, ref Session session)
            {
                Release({|#0:rwLock.Enter()|}, $"releasing");

                Guard held = rwLock.Enter();
                Release({|#1:held|}, $"releasing");

                var logger = new Logger({|#2:rwLock.Enter()|}, $"logging");
                {|#3:session|}.Log($"working");

                // By reference it is one value in one place.
                Guard looked = rwLock.Enter();
                Look(in looked, $"looking");
                looked.Dispose();
            }
        }

        {{Sources.Guard}}
        """,
        Verify.ValuePassedTwice(0, "Guard", "the call and the constructor of its interpolated-string handler 'ReleaseMessage'"),
        Verify.ValuePassedTwice(1, "Guard", "the call and the constructor of its interpolated-string handler 'ReleaseMessage'"),
        Verify.ValuePassedTwice(2, "Guard", "the call and the constructor of its interpolated-string handler 'ReleaseMessage'"),
        Verify.ValuePassedTwice(3, "Session", "the call and the constructor of its interpolated-string handler 'SessionMessage'"));

    [TestMethod]
    public Task Indexer_WithACompoundAssignmentPassesItsArgumentToBothAccessors() => Verify.AnalyzerAsync(
        $$"""
        sealed class Counts
        {
            public int this[Guard lease]
            {
                get => 0;
                set { }
            }

            public int? this[in Guard lease, int extra]
            {
                get => 0;
                set { }
            }
        }

        class C
        {
            void M(Counts counts, RwLock rwLock)
            {
                counts[{|#0:rwLock.Enter()|}] += 1;

                Guard guard = rwLock.Enter();
                counts[{|#1:guard|}]++;

                Guard other = rwLock.Enter();
                counts[in other, 1] ??= 2;
                other.Dispose();

                // Only one accessor runs here.
                counts[rwLock.Enter()] = 1;
                int count = counts[rwLock.Enter()];
            }
        }

        {{Sources.Guard}}
        """,
        Verify.ValuePassedTwice(0, "Guard", "the getter and the setter of 'Counts.this[Guard]'"),
        Verify.ValuePassedTwice(1, "Guard", "the getter and the setter of 'Counts.this[Guard]'"));

    [TestMethod]
    public Task Spread_OverAViewCopiesItsElements() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using System.Collections.Generic;

        sealed class Table
        {
            private readonly Guard[] _slots = new Guard[1];

            public Span<Guard> GetItems() => _slots;
        }

        class C
        {
            // A view never owns its elements, so a spread over one copies them, whatever the view came from.
            void M(Table table)
            {
                Guard[] snapshot = [{|#0:.. table.GetItems()|}];
                List<Guard> list = [{|#1:.. table.GetItems()|}];
            }
        }

        {{Sources.Guard}}
        """,
        Verify.ReadFromStoredContainer(0, "'..'", "Guard", canBeMoved: false),
        Verify.ReadFromStoredContainer(1, "'..'", "Guard", canBeMoved: false));

    [TestMethod]
    public Task Extension_MembersThatTheCompilerCallsOnAByValueReceiverAreGivenACopy() => Verify.AnalyzerAsync(
        $$"""
        using System.Collections.Generic;
        using System.Runtime.CompilerServices;
        using System.Threading;
        using System.Threading.Tasks;

        static class Extensions
        {
            extension(Guard guard)
            {
                public List<int>.Enumerator GetEnumerator() => default;

                public IAsyncEnumerator<int> GetAsyncEnumerator(CancellationToken token = default) => null!;

                public TaskAwaiter GetAwaiter() => default;

                public void Deconstruct(out bool held, out int id) => (held, id) = (true, 0);
            }

            public static void Deconstruct(this Guard guard, out int first, out int second, out int third) => (first, second, third) = (0, 0, 0);
        }

        class C
        {
            async Task M(RwLock rwLock)
            {
                Guard guard = rwLock.Enter();

                foreach (int item in {|#0:guard|})
                {
                }

                await {|#1:guard|};

                await foreach (int item in {|#2:guard|})
                {
                }

                var (held, id) = {|#3:guard|};

                if (guard is {|#4:(true, _)|})
                {
                }

                if (guard is {|#5:(0, 0, 0)|})
                {
                }

                guard.Dispose();
            }
        }

        {{Sources.Guard}}
        """,
        Verify.CopyOfStoredValue(0, "'Extensions.extension(Guard).GetEnumerator()' receives", "Guard"),
        Verify.CopyOfStoredValue(1, "'Extensions.extension(Guard).GetAwaiter()' receives", "Guard"),
        Verify.CopyOfStoredValue(2, "'Extensions.extension(Guard).GetAsyncEnumerator(CancellationToken)' receives", "Guard"),
        Verify.CopyOfStoredValue(3, "'Extensions.extension(Guard).Deconstruct(out bool, out int)' receives", "Guard"),
        Verify.CopyOfStoredValue(4, "'Extensions.extension(Guard).Deconstruct(out bool, out int)' receives", "Guard"),
        Verify.CopyOfStoredValue(5, "'Extensions.Deconstruct(Guard, out int, out int, out int)' receives", "Guard"));

    // By value, a 'switch' expression and a '??' over a stored value give a temporary copy of it, as a '?:' does: a member that is
    // called on the result runs on that copy, and the stored value stays as it was.
    [TestMethod]
    public Task TemporaryCopy_OfASwitchExpressionOrACoalesceIsReportedWhereAMemberRunsOnIt() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using System.Runtime.CompilerServices;
        using System.Threading.Tasks;
        using Singulink.CodeAnalysis;

        [NonCopyable]
        struct Bag
        {
            public Guard Inner;
            public int Taken;

            public Enumerator GetEnumerator() { Taken++; return default; }

            public int Count { get { Taken++; return 0; } }

            public readonly int Size => Taken;

            public Awaiter GetAwaiter() { Taken++; return default; }

            public void Deconstruct(out int first, out int second) { Taken++; first = second = 0; }

            public struct Enumerator
            {
                public bool MoveNext() => false;

                public int Current => 0;
            }

            public struct Awaiter : INotifyCompletion
            {
                public bool IsCompleted => true;

                public void GetResult() { }

                public void OnCompleted(Action continuation) { }
            }
        }

        sealed class Session
        {
            Guard _read, _write;
            Guard? _maybe;
            Bag _first, _second;

            void Release(int kind, bool flag)
            {
                ({|#0:kind switch { 0 => _read, _ => _write }|}).Dispose();
                ({|#1:_maybe ?? _write|}).Dispose();
                ({|#2:_maybe ?? throw new InvalidOperationException()|}).Dispose();

                // The form that was reported before.
                ({|#3:flag ? _read : _write|}).Dispose();
            }

            async Task Use(int kind, Bag? none)
            {
                foreach (int i in {|#4:kind switch { 0 => _first, _ => _second }|}) { }

                int count = ({|#5:kind switch { 0 => _first, _ => _second }|}).Count;
                await ({|#6:kind switch { 0 => _first, _ => _second }|});
                var (a, b) = {|#7:kind switch { 0 => _first, _ => _second }|};
                {|#8:(kind switch { 0 => _first, _ => _second }).Inner|}.Dispose();
                int other = ({|#9:none ?? _first|}).Count;

                // A member that is readonly does no harm on a copy, and a field that is only read is read.
                int size = (kind switch { 0 => _first, _ => _second }).Size;
                int taken = (none ?? _first).Taken;
            }

            // A new value is used in place.
            void New(RwLock rwLock, int kind)
            {
                (kind switch { 0 => rwLock.Enter(), _ => rwLock.Enter() }).Dispose();
                (rwLock.TryEnter() ?? rwLock.Enter()).Dispose();
            }
        }

        {{Sources.Guard}}
        """,
        Verify.MemberOnReadOnlyReference(0, "Guard", "Guard.Dispose()"),
        Verify.MemberOnReadOnlyReference(1, "Guard", "Guard.Dispose()"),
        Verify.MemberOnReadOnlyReference(2, "Guard", "Guard.Dispose()"),
        Verify.MemberOnReadOnlyReference(3, "Guard", "Guard.Dispose()"),
        Verify.MemberOnReadOnlyReference(4, "Bag", "Bag.GetEnumerator()"),
        Verify.MemberOnReadOnlyReference(5, "Bag", "Bag.Count"),
        Verify.MemberOnReadOnlyReference(6, "Bag", "Bag.GetAwaiter()"),
        Verify.MemberOnReadOnlyReference(7, "Bag", "Bag.Deconstruct(out int, out int)"),
        Verify.MemberOnReadOnlyReference(8, "Guard", "Guard.Dispose()"),
        Verify.MemberOnReadOnlyReference(9, "Bag", "Bag.Count"));

    // The compiler reads the value of anything but a local into a temporary before it matches a list pattern against it, and calls the
    // length and the indexer on that copy.
    [TestMethod]
    public Task TemporaryCopy_OfTheInputOfAListPatternIsReportedUnlessItsMembersAreReadOnly() => Verify.AnalyzerAsync(
        $$"""
        using Singulink.CodeAnalysis;

        [NonCopyable]
        struct Counted
        {
            public int Hits;

            public int Length { get { Hits++; return 1; } }

            public int this[int index] { get { Hits++; return 7; } }
        }

        [NonCopyable]
        struct Quiet
        {
            public readonly int Length => 1;

            public readonly int this[int index] => 7;
        }

        sealed class Holder
        {
            Counted _counted;
            Quiet _quiet;

            bool OfAField() => _counted is {|#0:[var first]|} && first == 7;

            bool OfAnElement(Counted[] array) => array[0] is {|#1:[7]|};

            static bool ThroughAReference(ref Counted value) => value is {|#2:[7]|};

            // A local and a by-value parameter are matched in place.
            static bool OfALocal(Counted parameter)
            {
                Counted local = default;
                return local is [7] && parameter is [7];
            }

            // Members that are readonly do no harm on a copy.
            bool OfReadOnlyMembers() => _quiet is [7];
        }

        {{Sources.Guard}}
        """,
        Verify.CopyOfStoredValue(0, "The pattern reads from", "Counted"),
        Verify.CopyOfStoredValue(1, "The pattern reads from", "Counted"),
        Verify.CopyOfStoredValue(2, "The pattern reads from", "Counted"));

    // A 'foreach' runs GetEnumerator on a copy wherever a call of it that is written would: through an 'in' or 'ref readonly'
    // parameter, a readonly field and every other read-only reference, also where the method comes from an interface that a type
    // parameter is constrained to.
    [TestMethod]
    public Task ForEach_RunsGetEnumeratorOnACopyWhereAWrittenCallWould() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using System.Collections;
        using System.Collections.Generic;
        using Singulink.CodeAnalysis;

        [NonCopyable]
        public struct Batch : IEnumerable<int>
        {
            private Guard _guard;

            public Enumerator GetEnumerator() { _guard.Dispose(); return default; }

            IEnumerator<int> IEnumerable<int>.GetEnumerator() => null!;

            IEnumerator IEnumerable.GetEnumerator() => null!;

            public struct Enumerator
            {
                public bool MoveNext() => false;

                public int Current => 0;
            }
        }

        public static class Extensions
        {
            extension(ref readonly Batch batch)
            {
                public void DrainInABlock()
                {
                    foreach (int item in {|#0:batch|}) { }
                }
            }

            public static void DrainClassic(this ref readonly Batch batch)
            {
                foreach (int item in {|#1:batch|}) { }
            }
        }

        public class Work<[NonCopyable] T>
            where T : struct, IEnumerable<int>
        {
            private readonly T _items;
            private T _own;

            public static void ByIn(in T items)
            {
                foreach (int item in {|#2:items|}) { }
            }

            public static void ByRefReadOnly(ref readonly T items)
            {
                foreach (int item in {|#3:items|}) { }
            }

            public void OfAReadOnlyField()
            {
                foreach (int item in {|#4:_items|}) { }
            }

            // Through a reference that can be written the method runs in place.
            public void InPlace(ref T items)
            {
                foreach (int item in items) { }
                foreach (int item in _own) { }
            }
        }

        class C
        {
            Batch _batch;

            static void Drain(ref readonly Batch batch)
            {
                foreach (int item in {|#5:batch|}) { }
                var enumerator = {|#6:batch|}.GetEnumerator();
            }

            static void InALambda()
            {
                Reader reader = (ref readonly Batch batch) =>
                {
                    foreach (int item in {|#7:batch|}) { }
                };

                static void Local(ref readonly Batch batch)
                {
                    foreach (int item in {|#8:batch|}) { }
                }
            }

            void InPlace(ref Batch batch)
            {
                foreach (int item in batch) { }
                foreach (int item in _batch) { }
            }

            delegate void Reader(ref readonly Batch batch);
        }

        {{Sources.Guard}}
        """,
        Verify.MemberOnReadOnlyReference(0, "Batch", "Batch.GetEnumerator()"),
        Verify.MemberOnReadOnlyReference(1, "Batch", "Batch.GetEnumerator()"),
        Verify.MemberOnReadOnlyTypeParameter(2, "T", "IEnumerable<T>.GetEnumerator()"),
        Verify.MemberOnReadOnlyTypeParameter(3, "T", "IEnumerable<T>.GetEnumerator()"),
        Verify.MemberOnReadOnlyTypeParameter(4, "T", "IEnumerable<T>.GetEnumerator()"),
        Verify.MemberOnReadOnlyReference(5, "Batch", "Batch.GetEnumerator()"),
        Verify.MemberOnReadOnlyReference(6, "Batch", "Batch.GetEnumerator()"),
        Verify.MemberOnReadOnlyReference(7, "Batch", "Batch.GetEnumerator()"),
        Verify.MemberOnReadOnlyReference(8, "Batch", "Batch.GetEnumerator()"));

    // A conversion that gives a value gives a temporary: '(Guard)_maybe' unwraps a copy out of the nullable, and a tuple conversion,
    // also one that only renames the elements, copies the tuple. A member on the result runs on that copy, and is reported like a
    // member on any other temporary copy of a stored value.
    [TestMethod]
    public Task Conversion_ThatGivesAValueGivesATemporaryCopy() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using System.Collections.Generic;
        using Singulink.CodeAnalysis;

        [NonCopyable]
        struct Pair
        {
            public Guard Inner;
            private int _reads;

            public int Reads => _reads++;

            public readonly int Size => 2;

            public int this[int index] => _reads++;

            public List<int>.Enumerator GetEnumerator() { _reads++; return default; }

            public void Deconstruct(out int first, out int second) => (first, second) = (_reads++, 0);
        }

        class Session
        {
            Guard? _held;
            Pair? _pair;
            (Guard Guard, int Id) _entry;

            void Unwrapped()
            {
                ({|#0:(Guard)_held|}).Dispose();
                int reads = ({|#1:(Pair)_pair|}).Reads;
                int element = ({|#2:(Pair)_pair|})[0];
                {|#3:((Pair)_pair).Inner|}.Dispose();

                foreach (int item in {|#4:(Pair)_pair|}) { }

                var (first, second) = {|#5:(Pair)_pair|};

                // A member that changes nothing does no harm on the copy.
                int size = ((Pair)_pair).Size;
            }

            void Tuples()
            {
                {|#6:(((Guard, long))_entry).Item1|}.Dispose();
                {|#7:(((Guard g, int n))_entry).g|}.Dispose();

                // In place, with no conversion.
                _entry.Guard.Dispose();
            }

            // A new value is moved through the conversion.
            void New(RwLock rwLock)
            {
                ((Guard)rwLock.TryEnter()).Dispose();
                ((Guard)NonCopyable.Move(ref _held)).Dispose();
            }
        }

        {{Sources.Guard}}
        """,
        Verify.MemberOnReadOnlyReference(0, "Guard", "Guard.Dispose()"),
        Verify.MemberOnReadOnlyReference(1, "Pair", "Pair.Reads"),
        Verify.MemberOnReadOnlyReference(2, "Pair", "Pair.this[int]"),
        Verify.MemberOnReadOnlyReference(3, "Guard", "Guard.Dispose()"),
        Verify.MemberOnReadOnlyReference(4, "Pair", "Pair.GetEnumerator()"),
        Verify.MemberOnReadOnlyReference(5, "Pair", "Pair.Deconstruct(out int, out int)"),
        Verify.MemberOnReadOnlyReference(6, "Guard", "Guard.Dispose()"),
        Verify.MemberOnReadOnlyReference(7, "Guard", "Guard.Dispose()"));

    // A query expression evaluates the source of its 'from' clause into a temporary and calls its method on that, where the same
    // call written out runs in place. So the source is judged as the receiver of a member that runs on a copy.
    [TestMethod]
    public Task Query_CallsItsMethodsOnACopyOfItsSource() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using Singulink.CodeAnalysis;

        [NonCopyable]
        struct Queue
        {
            private Guard _guard;
            private int _reads;

            public int Select(Func<int, int> selector) { _guard.Dispose(); return selector(0); }

            public Results Where(Func<int, bool> predicate) { _reads++; return new Results(); }
        }

        sealed class Results
        {
            public int Select(Func<int, int> selector) => selector(0);
        }

        [NonCopyable]
        struct Counts
        {
            public readonly int Select(Func<int, int> selector) => selector(0);
        }

        class Worker
        {
            Queue _queue;
            Counts _counts;

            static Queue Make() => default;

            void M(ref Queue byRef)
            {
                var fromAField = from item in {|#0:_queue|} select item;
                var fromAReference = from item in {|#1:byRef|} select item + 1;

                Queue local = Make();
                var fromALocal = from item in {|#2:local|} where item > 0 select item;
                int reads = local.Select(x => x);

                // The call written out runs in place, a new source is moved into the temporary, and a readonly method changes nothing.
                int written = _queue.Select(x => x);
                var fromANewValue = from item in Make() select item;
                var readOnly = from item in _counts select item;
            }
        }

        {{Sources.Guard}}
        """,
        Verify.MemberOnReadOnlyReference(0, "Queue", "Queue.Select(Func<int, int>)"),
        Verify.MemberOnReadOnlyReference(1, "Queue", "Queue.Select(Func<int, int>)"),
        Verify.MemberOnReadOnlyReference(2, "Queue", "Queue.Where(Func<int, bool>)"));

    // A spread calls members like a 'foreach': '[.. bag]' is checked for 'GetEnumerator', 'MoveNext', 'Current' and 'Dispose' as a
    // 'foreach' over 'bag' is, in a method and in an initializer.
    [TestMethod]
    public Task Spread_IsCheckedForTheMembersThatItCallsLikeAForEach() => Verify.AnalyzerAsync(
        $$"""
        using System.Collections.Generic;
        using Singulink.CodeAnalysis;

        public sealed class Bag<[NonCopyable] T>
            where T : struct
        {
            private readonly T[] _items = new T[2];

            [RequiresCopyable]
            public IEnumerator<int> GetEnumerator()
            {
                _items[1] = _items[0];
                return null!;
            }
        }

        public struct Counter
        {
            public bool MoveNext() => false;

            public int Current => 0;
        }

        public class Store<T>
        {
            public Counter GetEnumerator() => default;
        }

        public sealed class Guards : Store<Guard>;

        [NonCopyable]
        public struct Batch
        {
            private int _reads;

            public Counter GetEnumerator() { _reads++; return default; }
        }

        class C
        {
            static readonly Bag<Guard> s_bag = new();
            static readonly int[] s_counts = [{|#0:.. s_bag|}];

            void M(Bag<Guard> bag, Guards guards, Bag<int> numbers, in Batch batch)
            {
                int[] counts = [{|#1:.. bag|}];
                foreach (int count in {|#2:bag|}) { }

                int[] ofGuards = [{|#3:.. guards|}];
                foreach (int count in {|#4:guards|}) { }

                int[] ofABatch = [.. {|#5:batch|}];
                foreach (int count in {|#6:batch|}) { }

                // A copyable type argument, and a list of the framework.
                int[] ofNumbers = [.. numbers];
                int[] ofAList = [.. new List<int>()];
            }
        }

        {{Sources.Guard}}
        """,
        Verify.RequiresCopyableMember(0, "Bag<T>.GetEnumerator()", "Guard", "T"),
        Verify.RequiresCopyableMember(1, "Bag<T>.GetEnumerator()", "Guard", "T"),
        Verify.RequiresCopyableMember(2, "Bag<T>.GetEnumerator()", "Guard", "T"),
        Verify.UnlistedMember(3, "Store<T>.GetEnumerator()", "Guard", "T", "M:Store`1.GetEnumerator"),
        Verify.UnlistedMember(4, "Store<T>.GetEnumerator()", "Guard", "T", "M:Store`1.GetEnumerator"),
        Verify.MemberOnReadOnlyReference(5, "Batch", "Batch.GetEnumerator()"),
        Verify.MemberOnReadOnlyReference(6, "Batch", "Batch.GetEnumerator()"));

    // A pattern runs on a copy of every part that it looks into: the compiler reads a field, a tuple element, a list element and
    // what a 'ref' property refers to into a temporary before it matches the pattern inside, also where the whole value is a local
    // or an object of a class. A 'Deconstruct', a getter that is not readonly and the members of a list pattern inside are reported
    // unless the part is a new value. Measured with both SDKs, Debug and Release.
    [TestMethod]
    public Task Pattern_RunsOnACopyOfEveryPartThatItLooksInto() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using Singulink.CodeAnalysis;

        [NonCopyable]
        struct Lease
        {
            private Guard _read, _write;
            private int _reads;

            public readonly bool IsHeld => true;

            public bool Rel => _reads++ > 0;

            public int Length => _reads++;

            public int this[int index] => _reads++;

            public void Deconstruct(out Guard read, out Guard write)
            {
                read = NonCopyable.Move(ref _read);
                write = NonCopyable.Move(ref _write);
            }
        }

        [NonCopyable]
        struct Slot
        {
            public Lease Lease;
        }

        sealed class Session
        {
            public Lease Lease;
            public Slot Slot;
            private Lease _own;

            public ref Lease Own => ref _own;

            public Lease Fresh => default;

            bool OfThis() => this is { Lease: {|#0:(var read, var write)|} };
        }

        static class C
        {
            static Slot MakeSlot() => default;

            static bool FieldOfAnObject(Session session) => session is { Lease: {|#1:(var read, var write)|} };

            static void Keep(ref Slot slot) { }

            static void Keep(ref (Lease Lease, int Id) pair) { }

            // The local is used again afterwards: at its last use it would be a new value, and a part of that is new.
            static bool FieldOfALocal(Slot slot)
            {
                bool matched = slot is { Lease: {|#2:(var read, var write)|} };
                Keep(ref slot);
                return matched;
            }

            static bool Getter(Slot slot)
            {
                bool matched = slot is { Lease: {|#3:{ Rel: true }|} };
                Keep(ref slot);
                return matched;
            }

            // The copy is reported once, for the outermost part that is copied.
            static bool GetterBehindADot(Session session) => session is { Slot: {|#4:{ Lease.Rel: true }|} };

            static bool ListInside(Slot slot)
            {
                bool matched = slot is { Lease: {|#5:[_]|} };
                Keep(ref slot);
                return matched;
            }

            static bool ElementOfAnArray(Lease[] leases) => leases is [{|#6:(var first, var second)|}];

            static bool ElementOfASpan(Span<Lease> leases) => leases is [{|#7:{ Rel: true }|}, ..];

            static bool ElementOfATuple((Lease Lease, int Id) pair)
            {
                bool matched = pair is ({|#8:{ Rel: true }|}, 1);
                Keep(ref pair);
                return matched;
            }

            static bool ThroughARefProperty(Session session) => session is { Own: {|#9:(var read, var write)|} };

            // A member that changes nothing does no harm on the copy, a part of a new value is new, and what a property returns by
            // value is a temporary already.
            static bool Harmless(Session session, Slot slot) =>
                session is { Lease.IsHeld: true } &&
                slot is { Lease: { IsHeld: true } } &&
                MakeSlot() is { Lease: (var read, var write) } &&
                session is { Fresh: (var freshRead, var freshWrite) };
        }

        {{Sources.Guard}}
        """,
        Verify.CopyOfStoredValue(0, "The pattern reads from", "Lease"),
        Verify.CopyOfStoredValue(1, "The pattern reads from", "Lease"),
        Verify.CopyOfStoredValue(2, "The pattern reads from", "Lease"),
        Verify.CopyOfStoredValue(3, "The pattern reads from", "Lease"),
        Verify.CopyOfStoredValue(4, "The pattern reads from", "Slot"),
        Verify.CopyOfStoredValue(5, "The pattern reads from", "Lease"),
        Verify.CopyOfStoredValue(6, "The pattern reads from", "Lease"),
        Verify.CopyOfStoredValue(7, "The pattern reads from", "Lease"),
        Verify.CopyOfStoredValue(8, "The pattern reads from", "Lease"),
        Verify.CopyOfStoredValue(9, "The pattern reads from", "Lease"));

    // A 'switch' matches a copy of a local or a parameter in two cases that the source does not show: a 'switch' statement in a
    // build that is not optimised, and any 'switch' where a clause or an arm has a 'when' that is no constant, for every clause of it.
    // So a 'switch' statement over a stored local is always judged as matching a copy, and a 'switch' expression where it has such a
    // 'when'. An 'is' pattern and a 'switch' expression with no 'when' match a local in place.
    [TestMethod]
    public Task Switch_MatchesACopyOfALocal() => Verify.AnalyzerAsync(
        $$"""
        using Singulink.CodeAnalysis;

        [NonCopyable]
        struct Lease
        {
            private Guard _read, _write;
            private int _reads;

            public readonly bool IsHeld => true;

            public bool Rel => _reads++ > 0;

            public void Deconstruct(out Guard read, out Guard write)
            {
                read = NonCopyable.Move(ref _read);
                write = NonCopyable.Move(ref _write);
            }

            public void Dispose() { }
        }

        static class C
        {
            static Lease Make() => default;

            static void Statement(Lease lease)
            {
                switch (lease)
                {
                    case {|#0:(var read, var write)|}:
                        read.Dispose();
                        write.Dispose();
                        break;
                }

                lease.Dispose();
            }

            static void StatementWithAWhen(bool writable)
            {
                Lease lease = Make();

                switch (lease)
                {
                    case { IsHeld: false } when writable:
                        break;

                    case {|#1:{ Rel: true }|}:
                        break;
                }

                lease.Dispose();
            }

            static int ExpressionWithAWhen(Lease lease, int limit)
            {
                int n = lease switch
                {
                    {|#2:(var read, var write)|} when limit > 0 => 1,
                    _ => 0,
                };

                lease.Dispose();
                return n;
            }

            static int ExpressionWithAWhenOnAnotherArm(Lease lease, bool ok)
            {
                int n = lease switch
                {
                    { IsHeld: false } when ok => 1,
                    {|#3:{ Rel: true }|} => 2,
                    _ => 0,
                };

                lease.Dispose();
                return n;
            }

            // In place: an 'is' pattern, a 'switch' expression with no 'when' or with a constant one, and a member that is readonly.
            static int InPlace(Lease lease)
            {
                bool rel = lease is { Rel: true };

                int n = lease switch
                {
                    { Rel: true } => 1,
                    _ => 0,
                };

                int m = lease switch
                {
                    { Rel: true } when true => 1,
                    _ => 0,
                };

                switch (lease)
                {
                    case { IsHeld: true }:
                        break;
                }

                lease.Dispose();
                return n + m;
            }

            // A local at its last use is moved into what the 'switch' matches.
            static void LastUse()
            {
                Lease lease = Make();

                switch (lease)
                {
                    case (var read, var write):
                        read.Dispose();
                        write.Dispose();
                        break;
                }
            }
        }

        {{Sources.Guard}}
        """,
        Verify.SwitchStatementMatchesCopy(0, "Lease"),
        Verify.SwitchStatementMatchesCopy(1, "Lease"),
        Verify.SwitchExpressionWithWhenMatchesCopy(2, "Lease"),
        Verify.SwitchExpressionWithWhenMatchesCopy(3, "Lease"));

    // Three constructs that were measured with both SDKs, Debug and Release: an exception filter runs a member of a stored value in
    // place, a postfix increment with a static operator passes a copy of a stored value to the operator and gives the value from
    // before as its result, and a nested initializer sets members of a field of the new object where the field is.
    [TestMethod]
    public Task CompilerCopies_FiltersIncrementsAndNestedInitializers() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using Singulink.CodeAnalysis;

        [NonCopyable]
        struct Counter
        {
            public int Hits;

            public bool Rel { get { Hits++; return true; } set { Hits++; } }

            public static Counter operator ++(Counter counter) => counter;
        }

        sealed class Holder
        {
            public Counter G;
        }

        class C
        {
            Counter _counter;

            void Filter(Holder holder)
            {
                Counter local = default;

                try { }
                catch (InvalidOperationException) when (holder.G.Rel) { }
                catch (ArgumentException) when (local.Rel) { }
                catch (Exception) when (_counter.Rel) { }

                local.Hits++;
            }

            void Postfix(Holder holder)
            {
                Counter old {|#0:= {|#1:holder.G|}++|};
            }

            void Prefix(Holder holder)
            {
                ++{|#2:holder.G|};
            }

            Holder NestedInitializer() => new Holder { G = { Rel = false, Hits = 1 } };
        }
        """,
        Verify.CopyByAssignment(0, "Counter"),
        Verify.CopyOfStoredValue(1, "'Counter.operator ++(Counter)' receives", "Counter"),
        Verify.CopyOfStoredValue(2, "'Counter.operator ++(Counter)' receives", "Counter"));

    // A pattern, and all the clauses of one 'switch', evaluate each member once: the compiler calls a Deconstruct method, a getter or
    // an indexer once and gives the result to every pattern that names the same member on the same input. So what a member of
    // checked code hands out is a part of the matched value, named by the member, and goes to one variable.
    [TestMethod]
    public Task Pattern_EvaluatesEachMemberOnce() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using Singulink.CodeAnalysis;

        [NonCopyable]
        struct Lease
        {
            private Guard _read, _write;

            public void Deconstruct(out Guard read, out Guard write)
            {
                read = NonCopyable.Move(ref _read);
                write = NonCopyable.Move(ref _write);
            }

            public void Deconstruct(out Guard read, out Guard write, out int count)
            {
                read = NonCopyable.Move(ref _read);
                write = NonCopyable.Move(ref _write);
                count = 2;
            }
        }

        sealed class Pool(RwLock rwLock)
        {
            public Guard Next => rwLock.Enter();

            public Guard Other => rwLock.Enter();

            public Lease NextLease => default;

            public int Count => 1;

            public Guard this[int index] => rwLock.Enter();
        }

        static class C
        {
            static Lease MakeLease() => default;

            static bool TryRegister(Guard read, Guard write) => false;

            static int Release(ref Guard read, ref Guard write) => 0;

            static void InTheClausesOfASwitch()
            {
                switch (MakeLease())
                {
                    case (var read, var write) when TryRegister(read, write):
                        break;

                    case (var {|#0:read2|}, var {|#1:write2|}):
                        read2.Dispose();
                        write2.Dispose();
                        break;
                }
            }

            static int InTheArmsOfASwitchExpression() => MakeLease() switch
            {
                (var read, var write) when TryRegister(read, write) => 1,
                (var {|#2:read2|}, var {|#3:write2|}) => Release(ref read2, ref write2),
            };

            static void OnBothSidesOfAnAnd(Pool pool)
            {
                var lease = MakeLease();

                if (lease is (var read, _) and (var {|#4:again|}, _))
                {
                    read.Dispose();
                    again.Dispose();
                }

                if (pool is { Next: var a } and { Next: var {|#5:b|} })
                {
                    a.Dispose();
                    b.Dispose();
                }

                if (pool is { NextLease: (var c, _) } and { NextLease: (var {|#6:d|}, _) })
                {
                    c.Dispose();
                    d.Dispose();
                }

                if (pool is [var e] and [var {|#7:f|}])
                {
                    e.Dispose();
                    f.Dispose();
                }
            }

            static void OfAProperty(Pool pool)
            {
                switch (pool)
                {
                    case { Next: var first } when TryRegister(first, default):
                        break;

                    case { Next: var {|#8:second|} }:
                        second.Dispose();
                        break;
                }
            }

            // Two members hand out two values, two Deconstruct methods as well, and two patterns each read their members themselves.
            static void Apart(Pool pool)
            {
                if (pool is { Next: var a } and { Other: var b })
                {
                    a.Dispose();
                    b.Dispose();
                }

                if (pool is { Next: var c } && pool is { Next: var d })
                {
                    c.Dispose();
                    d.Dispose();
                }

                var lease = MakeLease();

                if (lease is (var read, _) and (var other, _, _))
                {
                    read.Dispose();
                    other.Dispose();
                }
            }
        }

        {{Sources.Guard}}
        """,
        Verify.CopyByAssignmentIntoSecondVariable(0, "Guard", "read2"),
        Verify.CopyByAssignmentIntoSecondVariable(1, "Guard", "write2"),
        Verify.CopyByAssignmentIntoSecondVariable(2, "Guard", "read2"),
        Verify.CopyByAssignmentIntoSecondVariable(3, "Guard", "write2"),
        Verify.CopyByAssignmentIntoSecondVariable(4, "Guard", "again"),
        Verify.CopyByAssignmentIntoSecondVariable(5, "Guard", "b"),
        Verify.CopyByAssignmentIntoSecondVariable(6, "Guard", "d"),
        Verify.CopyByAssignmentIntoSecondVariable(7, "Guard", "f"),
        Verify.CopyByAssignmentIntoSecondVariable(8, "Guard", "second"));

    // A parameter of a primary constructor that a member uses is a field of the type: the compiler makes the reference to it one to
    // the field before it lowers a pattern, so 'is' and a 'switch' expression over it match a copy.
    [TestMethod]
    public Task Pattern_OnAPrimaryConstructorParameterThatAMemberUses_MatchesACopy() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using Singulink.CodeAnalysis;

        [NonCopyable]
        struct Lease
        {
            private Guard _read, _write;

            public void Deconstruct(out Guard read, out Guard write)
            {
                read = NonCopyable.Move(ref _read);
                write = NonCopyable.Move(ref _write);
            }
        }

        sealed class Session(Lease lease)
        {
            public bool Split(out Guard read, out Guard write)
            {
                if (lease is {|#0:(var r, var w)|})
                {
                    read = r;
                    write = w;
                    return true;
                }

                read = default;
                write = default;
                return false;
            }
        }

        [NonCopyable]
        struct Slot(Lease lease)
        {
            public int Split(out Guard read, out Guard write) => lease switch
            {
                {|#1:(var r, var w)|} => Hand(ref r, ref w, out read, out write),
            };

            private static int Hand(ref Guard r, ref Guard w, out Guard read, out Guard write)
            {
                read = NonCopyable.Move(ref r);
                write = NonCopyable.Move(ref w);
                return 2;
            }
        }

        // A parameter of an ordinary constructor, and one of a method, is matched in place.
        sealed class Plain
        {
            private Guard _read, _write;

            public Plain(Lease lease)
            {
                if (lease is (var r, var w))
                {
                    _read = r;
                    _write = w;
                }
            }

            public static bool Take(Lease lease, out Guard read)
            {
                bool matched = lease is (var r, _);
                read = default;
                return matched;
            }
        }

        {{Sources.Guard}}
        """,
        Verify.CopyOfStoredValue(0, "The pattern reads from", "Lease"),
        Verify.CopyOfStoredValue(1, "The pattern reads from", "Lease"));

    // A 'ref' field of a ref struct, and what a Slice method or a range indexer returns by reference, refer to stored values, also
    // where the ref struct or the sliced value is new: a pattern inside runs on a copy of a stored value.
    [TestMethod]
    public Task Pattern_InsideAReferenceToAStoredValue_RunsOnACopy() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using Singulink.CodeAnalysis;

        [NonCopyable]
        struct Lease
        {
            private Guard _read, _write;

            public void Deconstruct(out Guard read, out Guard write)
            {
                read = NonCopyable.Move(ref _read);
                write = NonCopyable.Move(ref _write);
            }
        }

        ref struct LeaseSlot
        {
            public ref Lease Value;

            public LeaseSlot(ref Lease value)
            {
                Value = ref value;
            }
        }

        sealed class Table
        {
            private Lease _lease;

            public LeaseSlot Slot() => new LeaseSlot(ref _lease);
        }

        sealed class Slots
        {
            private Lease _shared;

            public int Count => 2;

            public int this[int index] => index;

            public ref Lease Slice(int start, int length) => ref _shared;
        }

        sealed class Ranges
        {
            private Lease _shared;

            public int Count => 2;

            public int this[Index index] => 0;

            public ref Lease this[Range range] => ref _shared;
        }

        static class C
        {
            static bool OfARefField(Table table) => table.Slot() is { Value: {|#0:(var read, var write)|} };

            static bool OfASliceMethod(Slots slots) => slots is [_, .. {|#1:(var read, var write)|}];

            static bool OfARangeIndexer(Ranges ranges) => ranges is [_, .. {|#2:(var read, var write)|}];

            // A variable for the slice is a copy of the stored value as well.
            static bool TakenWhole(Slots slots) => slots is [_, .. var {|#3:rest|}];
        }

        {{Sources.Guard}}
        """,
        Verify.CopyOfStoredValue(0, "The pattern reads from", "Lease"),
        Verify.CopyOfStoredValue(1, "The pattern reads from", "Lease"),
        Verify.CopyOfStoredValue(2, "The pattern reads from", "Lease"),
        Verify.CopyByAssignmentIntoVariable(3, "Lease", "rest"));

    // The members of an anonymous type are auto-properties. Its ToString, Equals and GetHashCode copy them and 'with' makes a second
    // object, where nothing is written that could be reported, so a member of a non-copyable type is reported where the object is
    // created. And a positional parameter of a record is reported also where the record declares the member as a property that
    // returns a reference: the Deconstruct method that the compiler writes reads it by value.
    [TestMethod]
    public Task AnonymousTypesAndPositionalRecords_AreReportedWhereTheCompilerWritesTheCopy() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using System.Collections.Generic;
        using System.Linq;
        using Singulink.CodeAnalysis;

        public sealed record class Held(Guard {|#3:Lock|})
        {
            private readonly Guard[] _slot = [NonCopyable.Move(ref Lock)];

            public ref Guard Lock => ref _slot[0];
        }

        public sealed record class HeldAndTakenApartByHand(Guard Lock)
        {
            private readonly Guard[] _slot = [NonCopyable.Move(ref Lock)];

            public ref Guard Lock => ref _slot[0];

            // The record declares the method itself, so the compiler writes none.
            public void Deconstruct(out Guard Lock) => Lock = default;
        }

        public sealed class HeldByAClass(Guard guard)
        {
            private readonly Guard[] _slot = [guard];

            public ref Guard Lock => ref _slot[0];
        }

        static class C
        {
            static object Make(RwLock rwLock) => new { Name = "a", {|#0:Lock = rwLock.Enter()|} };

            static object MakeByName(RwLock rwLock)
            {
                var Lock = rwLock.Enter();
                return new { Name = "a", {|#1:Lock|} };
            }

            static IEnumerable<object> Query(RwLock rwLock, string[] names) => from name in names select new { Name = name, {|#2:Lock = rwLock.Enter()|} };

            // A container in an anonymous type is a reference, which is copied freely.
            static object WithAContainer(RwLock rwLock) => new { Name = "a", Guards = rwLock.CreateGuards() };
        }

        {{Sources.Guard}}
        """,
        Verify.AnonymousTypeMember(0, "Guard", "Lock"),
        Verify.AnonymousTypeMember(1, "Guard", "Lock"),
        Verify.AnonymousTypeMember(2, "Guard", "Lock"),
        Verify.PositionalParameterReadByDeconstruct(3, "Guard", "Lock", "Held"));

    private const string SlotSources = """
        [NonCopyable]
        struct Latch : IDisposable
        {
            private bool _released;

            public readonly bool IsHeld => !_released;

            // Not readonly: it changes the latch that it runs on.
            public bool Release
            {
                get
                {
                    _released = true;
                    return true;
                }
            }

            public int Count
            {
                get
                {
                    _released = true;
                    return 0;
                }
            }

            public void Dispose() => _released = true;
        }

        [NonCopyable]
        struct Slot : IDisposable
        {
            public Latch Inner;
            public int Id;

            public void Deconstruct(out Latch inner, out int id)
            {
                inner = NonCopyable.Move(ref Inner);
                id = Id;
            }

            public void Dispose() => Inner.Dispose();
        }

        static class SlotExtensions
        {
            // A Deconstruct method that takes the slot by value, as an extension can.
            public static void Deconstruct(this Slot slot, out Latch inner, out int id, out bool any)
            {
                inner = NonCopyable.Move(ref slot.Inner);
                id = slot.Id;
                any = true;
            }
        }
        """;

    // A union (C# 15) is a struct with one field of type object: its constructor boxes the case value, a copy of the union shares
    // the box, and a pattern unboxes a copy. So a struct that implements System.Runtime.CompilerServices.IUnion is a box, for every
    // value of it, a new one included. The older of the two compilers of these tests does not know unions, so the test declares the
    // interface and takes the compiler's error for each pattern; the real declaration is in Pattern_OnADeclaredUnion_TakesACopyOutOfABox,
    // which runs under the newer one.
    [TestMethod]
    public Task Pattern_OnAUnion_TakesACopyOutOfABox() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using System.Runtime.CompilerServices;
        using Singulink.CodeAnalysis;

        namespace System.Runtime.CompilerServices
        {
            public interface IUnion
            {
                object? Value { get; }
            }
        }

        struct LockResult : IUnion
        {
            public LockResult(Guard guard) => Value = guard;

            public object? Value { get; }
        }

        [NonCopyable]
        struct Lease
        {
            private Guard _read, _write;

            public void Deconstruct(out Guard read, out Guard write)
            {
                read = NonCopyable.Move(ref _read);
                write = NonCopyable.Move(ref _write);
            }
        }

        struct LeaseResult : IUnion
        {
            public object? Value { get; }
        }

        static class C
        {
            static LockResult TryEnter() => default;

            static void Matched(LockResult result, LeaseResult leased)
            {
                if (result is {|CS8121:Guard|} {|#0:guard|})
                    guard.Dispose();

                if (leased is {|#1:{|CS8121:Lease|} (var read, var write)|})
                {
                    read.Dispose();
                    write.Dispose();
                }

                switch (result)
                {
                    case {|CS8121:Guard|} {|#2:again|}:
                        again.Dispose();
                        break;
                }
            }

            // A union that a call has just returned can share its box with one that is stored.
            static void New()
            {
                if (TryEnter() is {|CS8121:Guard|} {|#3:made|})
                    made.Dispose();
            }

            // Through the box nothing is copied, and a copy of the union is a copy of a reference.
            static void InPlace(LockResult result)
            {
                ((IDisposable)result.Value!).Dispose();

                var second = result;
                _ = second;
            }
        }

        {{Sources.Guard}}
        """,
        Verify.CopyByAssignmentOutOfBox(0, "Guard", "LockResult"),
        Verify.CopyByAssignmentOutOfBox(1, "Lease", "LeaseResult"),
        Verify.CopyByAssignmentOutOfBox(2, "Guard", "LockResult"),
        Verify.CopyByAssignmentOutOfBox(3, "Guard", "LockResult"));

    // A parameter of a primary constructor that a member uses is a field wherever it is read: the constructor stores the field before
    // an initializer or the arguments of the base constructor run. A pattern over the parameter there matches a copy.
    [TestMethod]
    public Task PrimaryConstructorParameter_ThatAMemberUses_IsAFieldInInitializers() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using Singulink.CodeAnalysis;

        [NonCopyable]
        struct Lease : IDisposable
        {
            private Guard _read, _write;

            public void Deconstruct(out Guard read, out Guard write)
            {
                read = NonCopyable.Move(ref _read);
                write = NonCopyable.Move(ref _write);
            }

            public void Dispose()
            {
            }
        }

        static class Use
        {
            public static bool Both(ref Guard read, ref Guard write) => true;
        }

        [NonCopyable]
        struct Session(Lease lease) : IDisposable
        {
            private readonly bool _split = lease is {|#0:(var read, var write)|} && Use.Both(ref read, ref write);

            public void Dispose() => lease.Dispose();
        }

        abstract class Base(bool split)
        {
            public bool Split => split;
        }

        [NonCopyable]
        struct Holder
        {
            public Lease Lease;
        }

        sealed class Derived(Holder holder) : Base(holder.Lease is {|#1:(var read, var write)|} && Use.Both(ref read, ref write))
        {
            public void Close() => holder.Lease.Dispose();
        }

        // No member uses the parameter: it is an ordinary parameter, and the pattern matches it in place.
        [NonCopyable]
        struct Unkept(Lease lease)
        {
            private readonly bool _split = lease is (var read, var write) && Use.Both(ref read, ref write);
        }

        sealed class DerivedUnkept(Lease lease) : Base(lease is (var read, var write) && Use.Both(ref read, ref write))
        {
        }

        {{Sources.Guard}}
        """,
        Verify.CopyOfStoredValue(0, "The pattern reads from", "Lease"),
        Verify.CopyOfStoredValue(1, "The pattern reads from", "Lease"));

    // A use of a parameter of a primary constructor is a reference that binds to it. Another variable of the same name, such as an
    // 'out' parameter of a member, is no use of it.
    [TestMethod]
    public Task PrimaryConstructorParameter_IsCountedByWhatBindsToIt() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using Singulink.CodeAnalysis;

        [NonCopyable]
        struct Envelope(int payload, Guard guard) : IDisposable
        {
            private Guard _guard = guard;

            public void Deconstruct(out int value, out Guard guard)
            {
                value = payload;
                guard = NonCopyable.Move(ref _guard);
            }

            public void Dispose() => _guard.Dispose();
        }

        sealed class Box(Guard guard)
        {
            private Guard _guard = guard;

            public bool TryTake(out Guard guard)
            {
                guard = NonCopyable.Move(ref _guard);
                return true;
            }

            public static void Other()
            {
                Guard guard = default;
                guard.Dispose();
            }
        }

        // Two references that bind to it are two uses.
        sealed class Twice(Guard guard)
        {
            private Guard _first = {|#0:guard|};
            private Guard _second = {|#1:guard|};
        }

        {{Sources.Guard}}
        """,
        Verify.CopyOfStoredValue(0, "The initializer takes", "Guard"),
        Verify.CopyOfStoredValue(1, "The initializer takes", "Guard"));

    // In one pattern or one 'switch' the compiler copies each part that a pattern looks into to a temporary, the value that it is a
    // part of still holds the original, and every pattern that names the part shares the temporary. So next to the variables, the
    // uses of a part count: a member that is not readonly, and a receiver that takes the part by value.
    [TestMethod]
    public Task Pattern_UsesAPartTwice() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using Singulink.CodeAnalysis;

        static class C
        {
            static Slot Open() => default;

            static bool TryAdd(Latch latch) => false;

            static int Close(ref Latch latch) => 0;

            // A variable for a part that contains a part which a member ran on: the variable holds the original.
            static void MemberThenWhole()
            {
                switch (Open())
                {
                    case { Inner.Release: false }:
                        break;

                    case {|#0:var slot|}:
                        slot.Dispose();
                        break;
                }
            }

            // A member on a part that contains a part which a variable took: the member can move the original out.
            static void FieldThenDeconstruct()
            {
                switch (Open())
                {
                    case { Inner: var inner } when TryAdd(inner):
                        break;

                    case {|#1:(var moved, _)|}:
                        moved.Dispose();
                        break;
                }
            }

            // Two members of which one runs on a part that contains the part of the other, with no variable for the inner part.
            static void MemberThenDeconstruct()
            {
                switch (Open())
                {
                    case { Inner.Release: false }:
                        break;

                    case {|#2:(var moved, _)|}:
                        moved.Dispose();
                        break;
                }
            }

            // The compiler orders the evaluations itself, so the order in which they are written does not count.
            static void InOnePattern()
            {
                if (Open() is { Inner.Release: true } and {|#3:(var moved, _)|})
                    moved.Dispose();

                if (Open() is (var first, _) and { {|#4:Inner.Release|}: true })
                    first.Dispose();

                if (Open() is (var second, _) and { Inner: {|#5:var inner|} })
                {
                    second.Dispose();
                    inner.Dispose();
                }
            }

            static int InASwitchExpression() => Open() switch
            {
                { Inner.Release: false } => 0,
                {|#6:(var moved, _)|} => Close(ref moved),
            };

            // A member on the part that a variable of an earlier clause with a 'when' took: the 'when' handed the value on.
            static void VariableThenMember()
            {
                switch (Open())
                {
                    case { Inner: var inner } when TryAdd(inner):
                        break;

                    case { {|#7:Inner.Release|}: true }:
                        break;
                }
            }

            // A receiver that takes the value by value gets a copy of the whole.
            static void ByValueReceiver()
            {
                if (Open() is (var a, _, _) and {|#8:(var b, _)|})
                {
                    a.Dispose();
                    b.Dispose();
                }
            }
        }

        {{SlotSources}}

        {{Sources.Guard}}
        """,
        Verify.PartUsedTwiceInPattern(0, "'Latch.Release'", "variable 'slot'"),
        Verify.PartUsedTwiceInPattern(1, "variable 'inner'", "'Slot.Deconstruct(out Latch, out int)'"),
        Verify.PartUsedTwiceInPattern(2, "'Latch.Release'", "'Slot.Deconstruct(out Latch, out int)'"),
        Verify.PartUsedTwiceInPattern(3, "'Latch.Release'", "'Slot.Deconstruct(out Latch, out int)'"),
        Verify.PartUsedTwiceInPattern(4, "'Slot.Deconstruct(out Latch, out int)'", "'Latch.Release'"),
        Verify.PartUsedTwiceInPattern(5, "'Slot.Deconstruct(out Latch, out int)'", "variable 'inner'"),
        Verify.PartUsedTwiceInPattern(6, "'Latch.Release'", "'Slot.Deconstruct(out Latch, out int)'"),
        Verify.PartUsedTwiceInPattern(7, "variable 'inner'", "'Latch.Release'"),
        Verify.PartUsedTwiceInPattern(8, "'SlotExtensions.Deconstruct(Slot, out Latch, out int, out bool)'", "'Slot.Deconstruct(out Latch, out int)'"));

    // What is not a second use: a variable for exactly the part that a member ran on, which is bound afterwards and holds what the
    // member left; two members on exactly one part, which run on the one temporary in turn; a readonly member; what a member hands
    // out, as a Deconstruct method does its outputs and an indexer that returns by reference its elements; and a by-value receiver
    // with nothing next to it.
    [TestMethod]
    public Task Pattern_UsesAPartOnce() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using System.Diagnostics.CodeAnalysis;
        using Singulink.CodeAnalysis;

        [NonCopyable]
        struct Pair
        {
            private Latch _first, _second;

            public readonly int Length => 2;

            // Not readonly, and what it hands out is the element itself.
            [UnscopedRef]
            public ref Latch this[int index] => ref index == 0 ? ref _first : ref _second;
        }

        static class C
        {
            static Slot Open() => default;

            static Pair MakePair() => default;

            // The elements are read through a reference, so each variable is a copy of a stored value, and is reported as that
            // and not again as a second use next to the indexer.
            static void Elements()
            {
                if (MakePair() is [var {|#0:first|}, var {|#1:second|}])
                {
                    first.Dispose();
                    second.Dispose();
                }
            }

            static bool TryAdd(Latch latch) => false;

            static void M()
            {
                if (Open() is { Inner: { Release: true } inner })
                    inner.Dispose();

                if (Open() is { Inner.Release: true } and { Inner.Count: 0 })
                {
                }

                switch (Open())
                {
                    case { Inner.IsHeld: false }:
                        break;

                    case var slot:
                        slot.Dispose();
                        break;
                }

                switch (Open())
                {
                    case { Inner.Release: false }:
                        break;

                    case { Inner: var latch }:
                        latch.Dispose();
                        break;
                }

                if (Open() is ({ Release: true } moved, _))
                    moved.Dispose();

                if (Open() is (var taken, _, _))
                    taken.Dispose();

                switch (Open())
                {
                    case { Inner: var first } when first.IsHeld:
                        first.Dispose();
                        break;

                    case { Id: 0 }:
                        break;
                }
            }
        }

        {{SlotSources}}

        {{Sources.Guard}}
        """,
        Verify.CopyByAssignmentIntoVariable(0, "Latch", "first"),
        Verify.CopyByAssignmentIntoVariable(1, "Latch", "second"));

    // A type pattern between a struct and a type parameter goes through a box, in both directions: from a type parameter to a struct
    // the compiler boxes and unboxes, and from a struct to a type parameter the result can be the box itself.
    [TestMethod]
    public Task Pattern_BetweenAStructAndATypeParameter_GoesThroughABox() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using Singulink.CodeAnalysis;

        [NonCopyable]
        struct Lease : IDisposable
        {
            private Guard _read, _write;

            public bool Release
            {
                get
                {
                    _read = default;
                    return true;
                }
            }

            public void Deconstruct(out Guard read, out Guard write)
            {
                read = NonCopyable.Move(ref _read);
                write = NonCopyable.Move(ref _write);
            }

            public void Dispose()
            {
            }
        }

        static class Pool
        {
            static void Return<[NonCopyable] T>(T item)
                where T : IDisposable
            {
                if (item is {|#0:Lease (var read, var write)|})
                {
                    read.Dispose();
                    write.Dispose();
                }

                item.Dispose();
            }

            static bool Close<[NonCopyable] T>(T item)
                where T : IDisposable
            {
                bool wasOpen = item switch
                {
                    {|#1:Lease { Release: true }|} => true,
                    _ => false,
                };

                if (item is Guard {|#2:guard|})
                    guard.Dispose();

                item.Dispose();
                return wasOpen;
            }

            static void CloseAs<T>(Guard guard)
                where T : IDisposable
            {
                if (guard is {|#3:T t|})
                    t.Dispose();

                guard.Dispose();
            }

            // A bare test copies nothing, in either direction.
            static bool Tests<[NonCopyable] T, TOther>(T item, Guard guard) => item is Guard || item is Lease || guard is TOther;
        }

        {{Sources.Guard}}
        """,
        Verify.CopyByAssignmentOutOfBox(0, "Lease", "T"),
        Verify.CopyByAssignmentOutOfBox(1, "Lease", "T"),
        Verify.CopyByAssignmentOutOfBox(2, "Guard", "T"),
        Verify.Boxing(3, "Guard", "T"));

#if NET11_0_OR_GREATER
    // A union with the declaration that the language has for it, which the compiler of the newer SDK binds: 'union' makes the
    // struct, its constructors, which box the case value, and its 'Value'. It is reported as the struct of
    // Pattern_OnAUnion_TakesACopyOutOfABox is, which stands in for it under the older compiler.
    [TestMethod]
    public Task Pattern_OnADeclaredUnion_TakesACopyOutOfABox()
    {
        var test = new Verify.Test
        {
            ReferenceAssemblies = Verify.Test.Net11ReferenceAssemblies,
            TestCode = $$"""
                using System;
                using System.Collections.Generic;
                using Singulink.CodeAnalysis;

                union LockResult(Guard, int);

                [NonCopyable]
                struct Lease
                {
                    private Guard _read, _write;

                    public void Deconstruct(out Guard read, out Guard write)
                    {
                        read = NonCopyable.Move(ref _read);
                        write = NonCopyable.Move(ref _write);
                    }
                }

                union LeaseResult(Lease, int);

                sealed class Holder
                {
                    public LockResult Result;
                }

                static class C
                {
                    // A new value is moved into the box that the union makes for it.
                    static LockResult TryEnter(RwLock rwLock) => rwLock.Enter();

                    static void Matched(LockResult result, LeaseResult leased)
                    {
                        if (result is Guard {|#0:guard|})
                            guard.Dispose();

                        if (leased is {|#1:Lease (var read, var write)|})
                        {
                            read.Dispose();
                            write.Dispose();
                        }

                        switch (result)
                        {
                            case Guard {|#2:again|}:
                                again.Dispose();
                                break;
                        }
                    }

                    // A union that a call has just returned can share its box with one that is stored.
                    static void New(RwLock rwLock)
                    {
                        if (TryEnter(rwLock) is Guard {|#3:made|})
                            made.Dispose();
                    }

                    static void OutOfAList(List<LockResult> results)
                    {
                        foreach (var result in results)
                        {
                            if (result is Guard {|#4:held|})
                                held.Dispose();
                        }
                    }

                    static void ByACast(Holder holder)
                    {
                        Guard guard = {|#5:(Guard)holder.Result.Value!|};
                        guard.Dispose();
                    }

                    // Through the box nothing is copied, and a copy of the union is a copy of a reference.
                    static void InPlace(LockResult result)
                    {
                        ((IDisposable)result.Value!).Dispose();

                        var second = result;
                        _ = second;

                        bool isCode = result is int;
                    }
                }

                {{Sources.Guard}}
                """,
        };

        test.ExpectedDiagnostics.Add(Verify.CopyByAssignmentOutOfBox(0, "Guard", "LockResult"));
        test.ExpectedDiagnostics.Add(Verify.CopyByAssignmentOutOfBox(1, "Lease", "LeaseResult"));
        test.ExpectedDiagnostics.Add(Verify.CopyByAssignmentOutOfBox(2, "Guard", "LockResult"));
        test.ExpectedDiagnostics.Add(Verify.CopyByAssignmentOutOfBox(3, "Guard", "LockResult"));
        test.ExpectedDiagnostics.Add(Verify.CopyByAssignmentOutOfBox(4, "Guard", "LockResult"));
        test.ExpectedDiagnostics.Add(Verify.CopyByAssignmentOutOfBox(5, "Guard", "object"));
        return test.RunAsync();
    }
#endif

#if NET11_0_OR_GREATER
    // A union in every form is a box: a nullable of one, a union that is a class, and one that is written by hand with [Union] and
    // no IUnion, which is what the compiler goes by. A pattern on each unboxes a copy of the value that it holds.
    [TestMethod]
    public Task Pattern_OnAUnionInEveryForm_TakesACopyOutOfABox()
    {
        var test = new Verify.Test
        {
            ReferenceAssemblies = Verify.Test.Net11ReferenceAssemblies,
            TestCode = $$"""
                using System.Runtime.CompilerServices;

                union GuardOrCode(Guard, int);

                [Union]
                struct ByHand
                {
                    private readonly object? _value;

                    public ByHand(Guard guard) => _value = guard;

                    public ByHand(int code) => _value = code;

                    public object? Value => _value;
                }

                [Union]
                sealed class AsAClass : IUnion
                {
                    private readonly object? _value;

                    public AsAClass(Guard guard) => _value = guard;

                    public AsAClass(int code) => _value = code;

                    public object? Value => _value;
                }

                [Union]
                sealed class AsAClassByHand
                {
                    private readonly object? _value;

                    public AsAClassByHand(Guard guard) => _value = guard;

                    public AsAClassByHand(int code) => _value = code;

                    public object? Value => _value;
                }

                static class C
                {
                    static void Matched(GuardOrCode? maybe, ByHand byHand, AsAClass asAClass, AsAClassByHand asAClassByHand)
                    {
                        if (maybe is Guard {|#0:a|})
                            a.Dispose();

                        if (byHand is Guard {|#1:b|})
                            b.Dispose();

                        if (asAClass is Guard {|#2:c|})
                            c.Dispose();

                        if (asAClassByHand is Guard {|#3:d|})
                            d.Dispose();

                        bool isCode = maybe is int || byHand is int;
                    }
                }

                {{Sources.Guard}}
                """,
        };

        test.ExpectedDiagnostics.Add(Verify.CopyByAssignmentOutOfBox(0, "Guard", "GuardOrCode?"));
        test.ExpectedDiagnostics.Add(Verify.CopyByAssignmentOutOfBox(1, "Guard", "ByHand"));
        test.ExpectedDiagnostics.Add(Verify.CopyByAssignmentOutOfBox(2, "Guard", "AsAClass"));
        test.ExpectedDiagnostics.Add(Verify.CopyByAssignmentOutOfBox(3, "Guard", "AsAClassByHand"));
        return test.RunAsync();
    }
#endif
}
