using PrefixClassName.MsTest;

namespace Singulink.CodeAnalysis.Tests;

/// <summary>
/// Tests of what a listed member hands out: a value that is as new as what the member was called on and with, or a new value if the entry says so.
/// </summary>
/// <remarks>
/// Docs: articles/concepts/listed-members.md.
/// </remarks>
[PrefixTestClass]
public class ListedMemberTests
{
    [TestMethod]
    public Task HandedOutValue_IsAsNewAsWhatTheMemberWasCalledOn() => Verify.AnalyzerAsync(
        $$"""
        using System.Collections.Generic;
        using System.Threading.Tasks;

        class C
        {
            async Task M(RwLock rwLock, [Singulink.CodeAnalysis.Kept] List<Guard> list)
            {
                Guard a = await rwLock.EnterAsync();
                Guard b = await rwLock.EnterAsync().ConfigureAwait(false);
                Guard c = rwLock.EnterTaskAsync().Result;
                Guard c2 = await rwLock.EnterTaskAsync();
                Guard c3 = await rwLock.EnterTaskAsync().ConfigureAwait(false);
                Guard c4 = rwLock.EnterAsync().Result;
                Guard c5 = rwLock.EnterTaskAsync().GetAwaiter().GetResult();

                Task<Guard> stored = rwLock.EnterTaskAsync();
                Guard d = {|#0:await stored|};
                Guard d2 = await stored;
                Guard e = {|#1:list[0]|};

                foreach (Guard item in rwLock.CreateGuards()) { }
                foreach (Guard item in {|#2:list|}) { }

                // A read that is not assigned to anything is a copy too.
                {|#3:list[0]|}.Dispose();
                using (rwLock.CreateGuards()[0]) { }
            }
        }

        {{Sources.Guard}}
        """,
        Verify.ReadFromStoredContainer(0, "'await'", "Guard"),
        Verify.ReadFromStoredContainer(1, "'List<T>.this[int]'", "Guard"),
        Verify.ForEachOverStoredList(2, "Guard", "item", "list"),
        Verify.ReadFromStoredContainer(3, "'List<T>.this[int]'", "Guard"));

    [TestMethod]
    public Task ContainersAndViews_FollowTheSameDefault() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using System.Collections.Generic;
        using System.Runtime.InteropServices;
        using System.Threading.Tasks;

        class C
        {
            static Guard[] CreateArray() => new Guard[4];

            async Task M(RwLock rwLock, List<Guard> list)
            {
                // A view of a stored task is as stored as the task: nothing is read yet, and the read is reported.
                Task<Guard> stored = rwLock.EnterTaskAsync();
                var awaitable = stored.ConfigureAwait(false);
                Guard a = {|#0:await stored.ConfigureAwait(false)|};
                Guard b = {|#1:await awaitable|};
                Guard c = {|#2:stored.ConfigureAwait(false).GetAwaiter().GetResult()|};
                Guard d = {|#3:stored.Result|};

                // A stored value task holds the value inline, so the awaitable made from it is already a copy.
                ValueTask<Guard> storedValueTask = rwLock.EnterAsync();
                var valueAwaitable = {|#4:storedValueTask.ConfigureAwait(false)|};
                Guard e = {|#5:await storedValueTask|};

                // The variable is not used again, so nothing else can read what this read takes.
                Guard f = storedValueTask.Result;
            }

            void Spans(RwLock rwLock, List<Guard> list)
            {
                // A span over a list is as new as the list. Its indexer returns a reference either way.
                Span<Guard> span = CollectionsMarshal.AsSpan(list);
                ReadOnlySpan<Guard> slice = span.Slice(1);
                ref Guard first = ref CollectionsMarshal.AsSpan(list)[0];
                ref Guard fromNew = ref CollectionsMarshal.AsSpan(rwLock.CreateGuards())[0];

                // An array segment's indexer returns a value. A view never owns its elements, so that is a copy whatever the view came from.
                var segment = new ArraySegment<Guard>(CreateArray());
                Guard fromSegment = {|#7:segment[0]|};
                Guard fromNewSegment = {|#8:new ArraySegment<Guard>(CreateArray())[0]|};

                foreach (Guard item in {|#9:new ArraySegment<Guard>(CreateArray())|}) { }
            }
        }

        {{Sources.Guard}}
        """,
        Verify.ReadFromStoredContainer(0, "'await'", "Guard"),
        Verify.ReadFromStoredContainer(1, "'await'", "Guard"),
        Verify.ReadFromStoredContainer(2, "'ConfiguredTaskAwaitable<TResult>.ConfiguredTaskAwaiter.GetResult()'", "Guard"),
        Verify.ReadFromStoredContainer(3, "'Task<TResult>.Result'", "Guard", because: "'stored' is not the method's own anywhere in the method, because on line 14 'Task<Guard>.ConfigureAwait(bool)' can keep it, hand out a view of it or take in what something else holds"),
        Verify.ReadFromStoredContainer(
            4, "'ValueTask<TResult>.ConfigureAwait(bool)'", "System.Runtime.CompilerServices.ConfiguredValueTaskAwaitable<Guard>"),
        Verify.ReadFromStoredContainer(5, "'await'", "Guard"),
        Verify.ReadFromStoredContainer(7, "'ArraySegment<T>.this[int]'", "Guard"),
        Verify.ReadFromStoredContainer(8, "'ArraySegment<T>.this[int]'", "Guard"),
        Verify.ReadFromStoredContainer(9, "'foreach'", "Guard"));

    [TestMethod]
    public Task ReturnedReference_NeedsNothingExtra() => Verify.AnalyzerAsync(
        $$"""
        using System;

        class C
        {
            void M(RwLock rwLock, Span<Guard> span, ReadOnlySpan<Guard> readOnlySpan)
            {
                ref Guard r = ref span[1];
                span[0] = rwLock.Enter();
                span[1].Dispose();
                Guard copy {|#0:= span[0]|};

                ref readonly Guard readOnly = ref readOnlySpan[0];
                Guard readOnlyCopy {|#1:= readOnlySpan[0]|};
                Guard last {|#2:= span[^1]|};
                ref Guard lastReference = ref span[^1];
                Span<Guard> rest = span[1..];
                int length = span.Length;
            }
        }

        {{Sources.Guard}}
        """,
        Verify.CopyByAssignment(0, "Guard"),
        Verify.CopyByAssignment(1, "Guard"),
        Verify.CopyByAssignment(2, "Guard"));

    [TestMethod]
    public Task EntryMarkedNew_HandsOutANewValueWhateverItWasCalledOn() => Verify.AnalyzerAsync(
        $$"""
        using System.Buffers;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;

        class C
        {
            Task<Guard> _pending;

            void M(RwLock rwLock, Stack<Guard> stack, Queue<Guard> queue)
            {
                stack.Push(rwLock.Enter());
                Guard top = stack.Pop();
                Guard peek = {|#0:stack.Peek()|};

                queue.Enqueue(rwLock.Enter());
                Guard next = queue.Dequeue();
                Guard nextPeek = {|#1:queue.Peek()|};

                // What a member hands out by an 'out' parameter follows the same rule.
                if (stack.TryPop(out Guard popped)) { }
                if (stack.TryPeek({|#2:out Guard peeked|})) { }
                if (queue.TryDequeue(out var dequeued)) { }
                Guard existing;
                if (queue.TryPeek({|#3:out existing|})) { }

                Guard[] rented = ArrayPool<Guard>.Shared.Rent(4);
                ArrayPool<Guard>.Shared.Return(rented, clearArray: true);
            }
        }

        {{Sources.Guard}}
        """,
        Verify.ReadFromStoredContainer(0, "'Stack<T>.Peek()'", "Guard"),
        Verify.ReadFromStoredContainer(1, "'Queue<T>.Peek()'", "Guard"),
        Verify.ReadFromStoredContainer(2, "'Stack<T>.TryPeek(out T)'", "Guard"),
        Verify.ReadFromStoredContainer(3, "'Queue<T>.TryPeek(out T)'", "Guard"));

    [TestMethod]
    public Task ArgumentsCount_AndOnlyThoseThatCarryTheType() => Verify.AnalyzerAsync(
        $$"""
        using System.Collections.Generic;
        using System.Threading.Tasks;

        class C
        {
            void M(RwLock rwLock, [Singulink.CodeAnalysis.Kept] Task<Guard> storedTask, int storedNumber)
            {
                // The argument is what the constructor is called with.
                var fromStored = {|#0:new ValueTask<Guard>(storedTask)|};
                var fromNew = new ValueTask<Guard>(rwLock.EnterTaskAsync());
                var fromValue = new ValueTask<Guard>(rwLock.Enter());

                // An argument that does not carry the non-copyable type does not count.
                var list = new List<Guard>(storedNumber);
                Guard created = Create(storedNumber)[storedNumber];
            }

            static List<Guard> Create(int capacity) => new(capacity);

            // The result of a delegate that is made from a listed member would be a new value, so the member is judged where the delegate is
            // created, and the delegate can be invoked more than once.
            void Delegates(Stack<Guard> stack)
            {
                System.Func<Guard> pop = stack.Pop;
                System.Func<Guard> peek = {|#1:stack.Peek|};
            }
        }

        {{Sources.Guard}}
        """,
        Verify.CreatedFromStoredContainer(0, "System.Threading.Tasks.ValueTask<Guard>"),
        Verify.ReadFromStoredContainer(1, "'Stack<T>.Peek()'", "Guard"));

    [TestMethod]
    public Task StoredTask_AReadThatIsNotTheLastUseNeedsAMove() => Verify.AnalyzerAsync(
        $$"""
        using System.Threading.Tasks;
        using Singulink.CodeAnalysis;

        class C
        {
            async Task M(RwLock rwLock)
            {
                var pending = rwLock.EnterAsync();
                using var held = await NonCopyable.Move(ref pending);

                // Started now and awaited later: the await is the last use of the variable.
                var first = rwLock.EnterAsync();
                using var fromFirst = await first;

                // A value task that is used again afterwards still holds what the read took.
                var second = rwLock.EnterAsync();
                using var copy = {|#0:await second|};
                bool isCompleted = second.IsCompleted;

                var task = rwLock.EnterTaskAsync();
                using var fromTask = await NonCopyable.Move(ref task);
            }
        }

        {{Sources.Guard}}
        """,
        Verify.ReadFromStoredContainer(0, "'await'", "Guard"));

    [TestMethod]
    public Task EntryMarkedNew_HandsOutAContainerAsNewAsWhatItWasCalledOn() => Verify.AnalyzerAsync(
        $$"""
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;

        class C
        {
            Task<Guard> _first, _second;

            static Stack<Task<Guard>> CreateStack() => new();

            async Task M(RwLock rwLock, [Singulink.CodeAnalysis.Kept] Stack<Task<Guard>> stack, [Singulink.CodeAnalysis.Kept] Stack<Task<Guard>> otherStack, [Singulink.CodeAnalysis.Kept] Queue<List<Guard>> queue)
            {
                // 'new' is about a value of a non-copyable type, which leaves no second instance behind. A container that such a member
                // hands out is a reference that whoever put it in may still hold: two fields, two stacks or a list in a variable.
                _first = rwLock.EnterTaskAsync();
                _second = _first;
                Guard a = {|#0:await Interlocked.Exchange(ref _first, null)|};
                Guard b = {|#1:Interlocked.Exchange(ref _second, null).Result|};

                var task = rwLock.EnterTaskAsync();
                stack.Push(task);
                otherStack.Push(task);
                Guard c = {|#2:await stack.Pop()|};
                Guard d = {|#3:otherStack.Pop().Result|};

                Guard e = {|#4:queue.Dequeue()[0]|};

                // Called on a new value, the member hands out a new one.
                Guard f = await CreateStack().Pop();
            }
        }

        {{Sources.Guard}}
        """,
        Verify.ReadFromStoredContainer(0, "'await'", "Guard", canBeMoved: false),
        Verify.ReadFromStoredContainer(1, "'Task<TResult>.Result'", "Guard", canBeMoved: false),
        Verify.ReadFromStoredContainer(2, "'await'", "Guard", canBeMoved: false),
        Verify.ReadFromStoredContainer(3, "'Task<TResult>.Result'", "Guard", canBeMoved: false),
        Verify.ReadFromStoredContainer(4, "'List<T>.this[int]'", "Guard", canBeMoved: false));

    [TestMethod]
    public Task CreatedStruct_AStoredContainerThatItHoldsAsItselfIsNoSourceOfAValue() => Verify.AnalyzerAsync(
        $$"""
        using System.Collections.Generic;
        using System.Threading.Tasks;

        class C
        {
            Task<Guard> _task = null!;

            void M(RwLock rwLock, [Singulink.CodeAnalysis.Kept] List<Guard> list)
            {
                // The pair holds the stored list as the list that it is, like the tuple does: reading it gives a stored container.
                var pair = new KeyValuePair<Guard, List<Guard>>(rwLock.Enter(), list);
                var tuple = (rwLock.Enter(), list);

                // A value task over a stored task hands out the result of that task, which the task still holds.
                var pending = {|#0:new ValueTask<Guard>(_task)|};
            }
        }

        {{Sources.Guard}}
        """,
        Verify.CreatedFromStoredContainer(0, "System.Threading.Tasks.ValueTask<Guard>"));

    [TestMethod]
    public Task WhenAny_HandsOutOneOfTheTasksThatItIsGiven() => Verify.AnalyzerAsync(
        $$"""
        using System.Threading.Tasks;
        using Singulink.CodeAnalysis;

        class C
        {
            async Task M(RwLock rwLock)
            {
                var first = rwLock.EnterTaskAsync();
                var second = rwLock.EnterTaskAsync();

                // The generic overload copies no result. The task that it returns is one of the two, which the locals still hold.
                Task<Guard> winner = await Task.WhenAny(first, second);
                Guard a = {|#0:await await Task.WhenAny(first, second)|};
                Guard b = {|#1:await winner|};

                // And the locals are no longer the method's own: what the call returned holds one of them as well.
                Guard c = {|#2:await NonCopyable.Move(ref {|#3:first|})|};
                Guard d = {|#4:await NonCopyable.Move(ref {|#5:second|})|};
            }
        }

        {{Sources.Guard}}
        """,
        Verify.ReadFromStoredContainer(0, "'await'", "Guard"),
        Verify.ReadFromStoredContainer(1, "'await'", "Guard"),
        Verify.ReadFromStoredContainer(2, "'await'", "Guard", canBeMoved: false),
        Verify.ContainerMovedFromUnowned(3, "System.Threading.Tasks.Task<Guard>", "first", because: "on line 12 it is passed to 'Task.WhenAny<Guard>(Task<Guard>, Task<Guard>)'"),
        Verify.ReadFromStoredContainer(4, "'await'", "Guard", canBeMoved: false),
        Verify.ContainerMovedFromUnowned(5, "System.Threading.Tasks.Task<Guard>", "second", because: "on line 12 it is passed to 'Task.WhenAny<Guard>(Task<Guard>, Task<Guard>)'"));

    // A method group hands out through its 'out' parameters as it does through its return value: the callers of the delegate take what
    // comes out for new.
    [TestMethod]
    public Task MethodGroup_OfAListedMemberHandsOutThroughItsOutParameters() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using System.Collections.Generic;
        using System.Collections.Concurrent;
        using Singulink.CodeAnalysis;

        delegate bool TryGet(int key, out Guard value);

        delegate bool TryTake(out Guard value);

        class C
        {
            static readonly Dictionary<int, Guard> s_byKey = new();
            static readonly Stack<Guard> s_stack = new();
            static readonly Queue<Guard> s_queue = new();

            static void Stored()
            {
                TryGet get = {|#0:s_byKey.TryGetValue|};
                TryTake peek = {|#1:s_stack.TryPeek|};
                TryTake first = {|#2:s_queue.TryPeek|};
                var inferred = {|#3:s_stack.TryPeek|};
            }

            // What takes the value out of the container hands out the only instance each time.
            static void Removing()
            {
                TryTake pop = s_stack.TryPop;
                TryTake dequeue = s_queue.TryDequeue;
            }

            // A method of the project is checked in its body.
            static bool TryCreate(out Guard value)
            {
                value = default;
                return false;
            }

            static void Own()
            {
                TryTake create = TryCreate;
            }
        }

        {{Sources.Guard}}
        """,
        Verify.ReadFromStoredContainer(0, "'Dictionary<TKey, TValue>.TryGetValue(TKey, out TValue)'", "Guard"),
        Verify.ReadFromStoredContainer(1, "'Stack<T>.TryPeek(out T)'", "Guard"),
        Verify.ReadFromStoredContainer(2, "'Queue<T>.TryPeek(out T)'", "Guard"),
        Verify.ReadFromStoredContainer(3, "'Stack<T>.TryPeek(out T)'", "Guard"));

    // A temporary copy that a listed member hands out is a temporary copy like the others: where it is only the receiver of a field
    // read or of a readonly member, nothing that is done to it is lost and nothing of it is kept.
    [TestMethod]
    public Task ListedMember_HandsOutATemporaryCopy_ThatIsOnlyRead() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using System.Collections.Generic;
        using System.Threading.Tasks;
        using Singulink.CodeAnalysis;

        [NonCopyable]
        struct Lease
        {
            public Guard Inner;

            public int Id;

            private int _uses;

            public readonly bool IsHeld => Id != 0;

            public readonly int GetId() => Id;

            public int Uses => _uses++;

            public void Use() => _uses++;
        }

        sealed class C
        {
            private readonly Stack<Lease> _stack = new();
            private readonly List<Lease> _leases = new();
            private ValueTask<Guard> _pending;

            bool ReadOnlyMembers(int i) => _stack.Peek().IsHeld && _leases[i].IsHeld && _stack.Peek().GetId() == _leases[i].Id;

            int Field(int i) => _stack.Peek().Id + _leases[i].Inner.Value;

            // A member that is not readonly changes the copy and not the element.
            void Changes(int i)
            {
                _ = {|#0:_stack.Peek()|}.Uses;
                {|#1:_leases[i]|}.Use();
            }

            // An awaiter that is only asked to register a continuation is a copy that is only read. What 'GetResult' hands out of it
            // is a copy of what the stored task still holds.
            void Awaiter(Action continuation)
            {
                _pending.GetAwaiter().OnCompleted(continuation);
                Guard taken = {|#4:_pending.GetAwaiter()|}.GetResult();
                taken.Dispose();
            }

            // What is read out of the copy is a copy of what the element still holds.
            void Kept(int i)
            {
                Guard first = {|#2:_stack.Peek()|}.Inner;
                first.Dispose();

                var whole = {|#3:_leases[i]|};
                _ = whole.IsHeld;
            }
        }

        {{Sources.Guard}}
        """,
        Verify.ReadFromStoredContainer(0, "'Stack<T>.Peek()'", "Lease"),
        Verify.ReadFromStoredContainer(1, "'List<T>.this[int]'", "Lease"),
        Verify.ReadFromStoredContainer(2, "'Stack<T>.Peek()'", "Lease"),
        Verify.ReadFromStoredContainer(3, "'List<T>.this[int]'", "Lease"),
        Verify.ReadFromStoredContainer(4, "'ValueTask<TResult>.GetAwaiter()'", "System.Runtime.CompilerServices.ValueTaskAwaiter<Guard>"));
}
