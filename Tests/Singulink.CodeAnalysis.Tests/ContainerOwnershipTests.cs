using Microsoft.CodeAnalysis.Testing;
using PrefixClassName.MsTest;

namespace Singulink.CodeAnalysis.Tests;

/// <summary>
/// Tests of who holds a container: what goes into a marked type parameter, what <c>NonCopyable.Move</c> may take out, and what a method keeps
/// of what it is given, with <c>[Kept]</c>.
/// </summary>
/// <remarks>
/// Docs: articles/concepts/keeping-containers.md and articles/guides/moving-a-value.md.
/// </remarks>
[PrefixTestClass]
public class ContainerOwnershipTests
{
    [TestMethod]
    public Task MarkedTypeParameter_WhatIsHandedOutOfABoundContainerIsStored() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using System.Collections.Generic;
        using System.Threading.Tasks;
        using Singulink.CodeAnalysis;

        sealed class Bag<[NonCopyable] T>
        {
            private T[] _items = new T[4];
            private int _count;

            public int Count => _count;

            public void Add(T item) => _items[_count++] = NonCopyable.Move(ref item);

            public void AddRange(Span<T> items)
            {
                foreach (ref T item in items)
                    _items[_count++] = NonCopyable.Move(ref item);
            }

            public void Fill(Action<Bag<T>> fill) => fill(this);

            public T Take() => NonCopyable.Move(ref _items[--_count]);

            public ref T this[int index] => ref _items[index];

            public Span<T> AsSpan() => _items;
        }

        static class Cache<[NonCopyable] T>
        {
            private static T s_slot = default!;

            public static void Put(T value) => s_slot = NonCopyable.Move(ref value);

            public static T Take() => NonCopyable.Move(ref s_slot);
        }

        sealed class Hub
        {
            public T Take<[NonCopyable] T>() => Cache<T>.Take();
        }

        class C
        {
            Task<Guard> _pending = null!;
            Bag<Task<Guard>> _bag = new();
            Hub _hub = new();

            static T Echo<[NonCopyable] T>(T value) => value;

            static T Build<[NonCopyable] T>(Action<Bag<T>> configure)
            {
                var bag = new Bag<T>();
                configure(bag);
                return bag.Take();
            }

            static List<T> Wrap<[NonCopyable] T>(T value)
            {
                var list = new List<T>();
                list.Add(NonCopyable.Move(ref value));
                return list;
            }

            static int Count<[NonCopyable] T>(Bag<T> bag) => bag.Count;

            static List<Guard> Snapshot<[NonCopyable] T>(Bag<T> bag) => new();

            static T MyMove<[NonCopyable] T>([MovedFrom] ref T location)
            {
                T taken = location;
                location = default!;
                return taken;
            }

            static Bag<Task<Guard>> CreateBag() => new();

            async Task M(RwLock rwLock)
            {
                // The body of the generic treats a T as a value that it alone holds. That is not true of a task, which the caller may
                // still hold, and nothing about what goes in can be trusted: every type that mentions T is a way in, and so is every
                // piece of code that the generic is given. So nothing is reported where a container goes in.
                var tasks = new Bag<Task<Guard>>();
                tasks.Add(_pending);
                tasks.AddRange(new[] { _pending, _pending });
                tasks[0] = _pending;
                ref Task<Guard> first = ref tasks[0];
                Cache<Task<Guard>>.Put(_pending);

                // And what comes out through a type that mentions T is stored, whatever the member was called on and with.
                Guard a = {|#0:await tasks.Take()|};
                Guard b = {|#1:await _bag.Take()|};
                Guard c = {|#2:await Echo(_pending)|};
                Guard d = {|#3:await Echo(rwLock.EnterTaskAsync())|};
                Guard e = {|#4:await CreateBag().Take()|};
                Guard f = {|#5:await Build<Task<Guard>>(x => x.Add(_pending))|};
                Guard g = {|#6:await Cache<Task<Guard>>.Take()|};
                Guard h = {|#7:await _hub.Take<Task<Guard>>()|};
                Guard i = {|#8:await Wrap(rwLock.EnterTaskAsync())[0]|};

                // What it hands out in place is used in place.
                bool done = tasks[0].IsCompleted;
                int count = Count(tasks);

                // A result whose type does not mention the type parameter is the result of any call.
                foreach (Guard guard in Snapshot(tasks)) { }

                // The one exception is a pure move of a local that the method owns: the only thing that went in is a reference that
                // nobody else holds.
                var pending = rwLock.EnterTaskAsync();
                Guard j = await MyMove(ref pending);
                var other = rwLock.EnterTaskAsync();
                Guard k = await NonCopyable.Move(ref other);

                // For a value nothing changes: nobody can put the same guard in twice without a reported copy.
                var guards = new Bag<Guard>();
                guards.Add(rwLock.Enter());
                Guard l = guards.Take();
                Guard m = Echo(rwLock.Enter());
            }
        }

        {{Sources.Guard}}
        """,
        Verify.ReadFromStoredContainer(0, "'await'", "Guard", canBeMoved: false),
        Verify.ReadFromStoredContainer(1, "'await'", "Guard", canBeMoved: false),
        Verify.ReadFromStoredContainer(2, "'await'", "Guard", canBeMoved: false),
        Verify.ReadFromStoredContainer(3, "'await'", "Guard", canBeMoved: false),
        Verify.ReadFromStoredContainer(4, "'await'", "Guard", canBeMoved: false),
        Verify.ReadFromStoredContainer(5, "'await'", "Guard", canBeMoved: false),
        Verify.ReadFromStoredContainer(6, "'await'", "Guard", canBeMoved: false),
        Verify.ReadFromStoredContainer(7, "'await'", "Guard", canBeMoved: false),
        Verify.ReadFromStoredContainer(8, "'await'", "Guard", canBeMoved: false));

    [TestMethod]
    public Task MarkedTypeParameter_AnObjectWhoseOwnCodeDecidesWhatItHoldsIsNoFrameworkContainer() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using System.Collections;
        using System.Collections.Generic;
        using System.Threading.Tasks;
        using Singulink.CodeAnalysis;

        // The members of Queue<T> hand out what the queue holds as new as the queue is. What an object of these classes holds is
        // decided by their own code, with no argument that shows it.
        class MarkedQueue<[NonCopyable] T> : Queue<T>
        {
            public MarkedQueue() { }

            public MarkedQueue(Action<MarkedQueue<T>> fill) => fill(this);
        }

        class TaskQueue : Queue<Task<Guard>>
        {
            private static readonly Task<Guard> s_shared = null!;

            public TaskQueue() => Enqueue(s_shared);
        }

        sealed class Items<[NonCopyable] T> : IEnumerable<T>
        {
            private readonly List<T> _items = new();

            public Items() { }

            public Items(Action<Items<T>> fill) => fill(this);

            public void Add(T item) => _items.Add(NonCopyable.Move(ref item));

            IEnumerator<T> IEnumerable<T>.GetEnumerator() => throw null!;

            IEnumerator IEnumerable.GetEnumerator() => throw null!;
        }

        class GuardQueue : Queue<Guard> { }

        class C
        {
            Task<Guard> _pending = null!;

            async Task M(RwLock rwLock)
            {
                Guard a = {|#0:await new MarkedQueue<Task<Guard>>(q => q.Enqueue(_pending)).Dequeue()|};
                Guard b = {|#1:await new MarkedQueue<Task<Guard>>().Dequeue()|};
                Guard c = {|#2:await new TaskQueue().Dequeue()|};

                // Used as a framework interface, such an object is stored as well.
                Guard d = {|#3:await await Task.WhenAny(new Items<Task<Guard>>(x => x.Add(_pending)))|};

                // A queue of guards is not affected: no stored guard gets into it without a reported copy.
                Guard e = new GuardQueue().Dequeue();
            }

            // So it is not handed on as one.
            Queue<Task<Guard>> AsItsBase() => {|#4:new MarkedQueue<Task<Guard>>()|};

            IEnumerable<Task<Guard>> AsAnInterface(RwLock rwLock)
            {
                var items = new Items<Task<Guard>>();
                items.Add(rwLock.EnterTaskAsync());
                return {|#5:items|};
            }

            TaskQueue Concrete() => {|#6:new TaskQueue()|};

            // As what it is, it can be the method's own: whatever is read out of it is stored.
            Items<Task<Guard>> AsItself(RwLock rwLock)
            {
                var items = new Items<Task<Guard>>();
                items.Add(rwLock.EnterTaskAsync());
                return items;
            }
        }

        {{Sources.Guard}}
        """,
        Verify.ReadFromStoredContainer(0, "'await'", "Guard", canBeMoved: false),
        Verify.ReadFromStoredContainer(1, "'await'", "Guard", canBeMoved: false),
        Verify.ReadFromStoredContainer(2, "'await'", "Guard", canBeMoved: false),
        Verify.ReadFromStoredContainer(3, "'await'", "Guard"),
        Verify.UnownedContainerReturned(4, "C.AsItsBase()", "System.Collections.Generic.Queue<System.Threading.Tasks.Task<Guard>>"),
        Verify.UnownedContainerReturned(5, "C.AsAnInterface(RwLock)", "System.Collections.Generic.IEnumerable<System.Threading.Tasks.Task<Guard>>"),
        Verify.UnownedContainerReturned(6, "C.Concrete()", "TaskQueue", through: "System.Collections.Generic.Queue<System.Threading.Tasks.Task<Guard>>"));

    [TestMethod]
    public Task Delegate_MustNotHandOutAStoredContainerAsTheResultOfACall() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using System.Collections.Generic;
        using System.Threading.Tasks;
        using Singulink.CodeAnalysis;

        [return: Borrowed]
        delegate T Taker<[NonCopyable] T>();

        sealed class Bag<[NonCopyable] T>
        {
            private T _item = default!;

            public void Add(T item) => _item = NonCopyable.Move(ref item);

            public T Take() => NonCopyable.Move(ref _item);

            // With a task bound to T, every call of the delegate hands out a task that whoever added it may still hold, and callers of a
            // delegate take its result for a new value.
            public Func<T> GetTaker() => {|#0:Take|};

            public Func<Task<T>> GetAsyncTaker() => {|#1:() => Task.FromResult(Take())|};

            // A delegate type whose return value is marked [Borrowed] says so.
            public Taker<T> GetBorrowedTaker() => Take;

            public Action<T> GetAdder() => Add;
        }

        class C
        {
            Bag<Task<Guard>> _bag = new();
            Queue<Task<Guard>> _queue = new();

            static Task<Guard> Enter() => null!;

            async Task M()
            {
                // The result of a call of a delegate is a new value on the strength of the check of what it is created from. A method
                // group of a member that hands out what its container holds has no such check.
                Func<Task<Guard>> fromBag = {|#2:_bag.Take|};
                Func<Task<Guard>> fromQueue = {|#3:_queue.Dequeue|};
                Func<Task<Guard>> own = Enter;
                Func<Task<Guard>> lambda = () => Enter();
                Taker<Task<Guard>> borrowed = _bag.Take;

                Guard a = await own();
                Guard b = {|#4:await borrowed()|};
                Guard c = {|#5:await _bag.GetBorrowedTaker()()|};
            }
        }

        {{Sources.Guard}}
        """,
        Verify.DelegateOverTypeParameter(0, "Bag<T>.Take()", "T", "T"),
        Verify.DelegateOverTypeParameter(1, "lambda expression", "System.Threading.Tasks.Task<T>", "T"),
        Verify.DelegateHandsOutStoredContainer(2, "Bag<Task<Guard>>.Take()", "System.Threading.Tasks.Task<Guard>"),
        Verify.DelegateHandsOutStoredContainer(3, "Queue<Task<Guard>>.Dequeue()", "System.Threading.Tasks.Task<Guard>"),
        Verify.ReadFromStoredContainer(4, "'await'", "Guard", canBeMoved: false),
        Verify.ReadFromStoredContainer(5, "'await'", "Guard", canBeMoved: false));

    [TestMethod]
    public Task Local_ThatHoldsContainersOfContainersIsHeldToWhatIsAllowed() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using System.Collections.Generic;
        using System.Threading.Tasks;
        using Singulink.CodeAnalysis;

        sealed class Bag<[NonCopyable] T>
        {
            private T[] _items = new T[4];
            private int _count;

            public int Count => _count;

            public void Add(T item) => _items[_count++] = NonCopyable.Move(ref item);

            public void AddRange(Span<T> items)
            {
                foreach (ref T item in items)
                    _items[_count++] = NonCopyable.Move(ref item);
            }

            public void Fill(Action<Bag<T>> fill) => fill(this);

            public T Take() => NonCopyable.Move(ref _items[--_count]);

            public ref T this[int index] => ref _items[index];

            public Span<T> AsSpan() => _items;
        }

        class C
        {
            Task<Guard> _pending = null!;

            static void Fill(Bag<Task<Guard>> bag) { }

            static void FillGuards(List<Guard> list) { }

            // Every value that goes in is new, and nothing is done with the local but to call it, read it and return it.
            Bag<Task<Guard>> Own(RwLock rwLock)
            {
                var bag = new Bag<Task<Guard>>();
                bag.Add(rwLock.EnterTaskAsync());
                int count = bag.Count;

                if (bag is null || bag == null)
                    count = 0;

                // A member that is called with '?.' and a pattern that reads members which are not containers are uses of the same kind.
                bag?.Add(rwLock.EnterTaskAsync());

                if (bag is { Count: > 0 })
                    count = 1;

                return bag;
            }

            Task<Guard>[] OwnArray(RwLock rwLock)
            {
                var tasks = new Task<Guard>[2];
                tasks[0] = rwLock.EnterTaskAsync();
                tasks[1] ??= rwLock.EnterTaskAsync();
                bool done = tasks[0].IsCompleted;

                foreach (var task in tasks)
                    done &= task.IsCompleted;

                return tasks;
            }

            List<List<Guard>> OwnLists(RwLock rwLock)
            {
                var lists = new List<List<Guard>>();
                lists.Add(rwLock.CreateGuards());
                lists[0] = rwLock.CreateGuards();
                lists[0].Add(rwLock.Enter());
                return lists;
            }

            // A stored container can get into such a local by more routes than can be named.
            Bag<Task<Guard>> StoredArgument()
            {
                var bag = new Bag<Task<Guard>>();
                bag.Add(_pending);
                return {|#0:bag|};
            }

            Bag<Task<Guard>> StoredInASpan()
            {
                var bag = new Bag<Task<Guard>>();
                bag.AddRange(new[] { _pending, _pending });
                return {|#1:bag|};
            }

            Bag<Task<Guard>> ThroughAReference()
            {
                var bag = new Bag<Task<Guard>>();
                ref var slot = ref bag[0];
                slot = _pending;
                return {|#2:bag|};
            }

            Bag<Task<Guard>> ThroughAView()
            {
                var bag = new Bag<Task<Guard>>();
                bag.AsSpan()[0] = _pending;
                return {|#3:bag|};
            }

            Task<Guard>[] IntoAnElement()
            {
                var tasks = new Task<Guard>[1];
                tasks[0] = _pending;
                return {|#4:tasks|};
            }

            Task<Guard>[] ThroughAnElementReference()
            {
                var tasks = new Task<Guard>[1];
                ref var slot = ref tasks[0];
                slot = _pending;
                return {|#5:tasks|};
            }

            List<List<Task<Guard>>> ThroughAnInnerList()
            {
                var lists = new List<List<Task<Guard>>>();
                lists.Add(new List<Task<Guard>>());
                lists[0].Add(_pending);
                return {|#6:lists|};
            }

            // An argument brings in nothing stored only as a new value of exactly the element type. Code is a new value too, and can
            // push any stored container in.
            Bag<Task<Guard>> FilledByCode()
            {
                var bag = new Bag<Task<Guard>>();
                bag.Fill(x => x.Add(_pending));
                return {|#9:bag|};
            }

            Bag<Task<Guard>> FilledFromAnArray(RwLock rwLock)
            {
                var bag = new Bag<Task<Guard>>();
                bag.AddRange(new[] { rwLock.EnterTaskAsync() });
                return {|#10:bag|};
            }

            Bag<Task<Guard>> KeepsAnAlias()
            {
                var bag = new Bag<Task<Guard>>();

                if (bag is { Count: 0 } alias)
                    alias.Add(_pending);

                return {|#11:bag|};
            }

            // Being passed to another method ends it as well: nothing shows what that puts in.
            Bag<Task<Guard>> PassedOn()
            {
                var bag = new Bag<Task<Guard>>();
                Fill(bag);
                return {|#7:bag|};
            }

            // The same test decides whether the local can be moved from.
            void Moved()
            {
                var bag = new Bag<Task<Guard>>();
                Fill(bag);
                var taken = NonCopyable.Move(ref {|#8:bag|});
                var own = new Bag<Task<Guard>>();
                var mine = NonCopyable.Move(ref own);
            }

            // A local that holds guards directly keeps the test that names what ends it: no stored guard gets in without a copy.
            List<Guard> Guards()
            {
                var list = new List<Guard>();
                FillGuards(list);
                return list;
            }
        }

        {{Sources.Guard}}
        """,
        Verify.UnownedContainerReturned(0, "C.StoredArgument()", "Bag<System.Threading.Tasks.Task<Guard>>", because: "'bag' is not the method's own anywhere in the method, because on line 83 'Bag<Task<Guard>>.Add(Task<Guard>)' can keep it, hand out a view of it or take in what something else holds"),
        Verify.UnownedContainerReturned(1, "C.StoredInASpan()", "Bag<System.Threading.Tasks.Task<Guard>>", because: "'bag' is not the method's own anywhere in the method, because on line 90 'Bag<Task<Guard>>.AddRange(Span<Task<Guard>>)' can keep it, hand out a view of it or take in what something else holds"),
        Verify.UnownedContainerReturned(2, "C.ThroughAReference()", "Bag<System.Threading.Tasks.Task<Guard>>", because: "'bag' is not the method's own anywhere in the method, because on line 97 'Bag<Task<Guard>>.this[int]' can hand out a view of it or take in what something else holds"),
        Verify.UnownedContainerReturned(3, "C.ThroughAView()", "Bag<System.Threading.Tasks.Task<Guard>>", because: "'bag' is not the method's own anywhere in the method, because on line 105 'Bag<Task<Guard>>.AsSpan()' can keep it, hand out a view of it or take in what something else holds"),
        Verify.UnownedContainerReturned(4, "C.IntoAnElement()", "System.Threading.Tasks.Task<Guard>[]", because: "'tasks' is not the method's own anywhere in the method, because on line 112 it is used in a way that can keep it or a view of it"),
        Verify.UnownedContainerReturned(5, "C.ThroughAnElementReference()", "System.Threading.Tasks.Task<Guard>[]", because: "'tasks' is not the method's own anywhere in the method, because on line 119 it is used in a way that can keep it or a view of it"),
        Verify.UnownedContainerReturned(
            6, "C.ThroughAnInnerList()", "System.Collections.Generic.List<System.Collections.Generic.List<System.Threading.Tasks.Task<Guard>>>", because: "'lists' is not the method's own anywhere in the method, because on line 128 'List<List<Task<Guard>>>.this[int]' can hand out a view of it or take in what something else holds"),
        Verify.UnownedContainerReturned(7, "C.PassedOn()", "Bag<System.Threading.Tasks.Task<Guard>>", because: "'bag' is not the method's own anywhere in the method, because on line 162 it is passed to 'C.Fill(Bag<Task<Guard>>)'"),
        Verify.ContainerMovedFromUnowned(8, "Bag<System.Threading.Tasks.Task<Guard>>", "bag", because: "on line 170 it is passed to 'C.Fill(Bag<Task<Guard>>)'"),
        Verify.UnownedContainerReturned(9, "C.FilledByCode()", "Bag<System.Threading.Tasks.Task<Guard>>", because: "'bag' is not the method's own anywhere in the method, because on line 137 'Bag<Task<Guard>>.Fill(Action<Bag<Task<Guard>>>)' can keep it, hand out a view of it or take in what something else holds"),
        Verify.UnownedContainerReturned(10, "C.FilledFromAnArray(RwLock)", "Bag<System.Threading.Tasks.Task<Guard>>", because: "'bag' is not the method's own anywhere in the method, because on line 144 'Bag<Task<Guard>>.AddRange(Span<Task<Guard>>)' can keep it, hand out a view of it or take in what something else holds"),
        Verify.UnownedContainerReturned(11, "C.KeepsAnAlias()", "Bag<System.Threading.Tasks.Task<Guard>>", because: "'bag' is not the method's own anywhere in the method, because on line 152 it is used in a way that can keep it or a view of it"));

    [TestMethod]
    public Task Move_DeconstructedLocalsAndMovedFromParametersAreTheMethodsOwn() => Verify.AnalyzerAsync(
        $$"""
        using System.Threading.Tasks;
        using Singulink.CodeAnalysis;

        class C
        {
            Task<Guard> _stored = null!;

            async Task Both(RwLock first, RwLock second)
            {
                // The deconstruction of a tuple literal assigns each element to its variable.
                var (a, b) = (first.EnterTaskAsync(), second.EnterTaskAsync());
                Guard g1 = await NonCopyable.Move(ref a);
                Guard g2 = await NonCopyable.Move(ref b);

                Task<Guard> c, d;
                (c, d) = (first.EnterTaskAsync(), _stored);
                Guard g3 = await NonCopyable.Move(ref c);
                Guard g4 = {|#2:await NonCopyable.Move(ref {|#0:d|})|};
            }

            // The caller of a [MovedFrom] parameter had to own what it passed, so the parameter is the method's own.
            static ValueTask<Guard> Wrap([MovedFrom] ref Task<Guard> task) => new(NonCopyable.Move(ref task));

            static Task<Guard> PassOn([MovedFrom] ref Task<Guard> task) => NonCopyable.Move(ref task);

            static Task<Guard> NotOwned(ref Task<Guard> task) => {|#3:NonCopyable.Move(ref {|#1:task|})|};
        }

        {{Sources.Guard}}
        """,
        Verify.ContainerMovedFromUnowned(0, "System.Threading.Tasks.Task<Guard>", "d"),
        Verify.ContainerMovedFromUnowned(1, "System.Threading.Tasks.Task<Guard>", "task"),
        Verify.ReadFromStoredContainer(2, "'await'", "Guard", canBeMoved: false),
        Verify.UnownedContainerReturned(3, "C.NotOwned(ref Task<Guard>)", "System.Threading.Tasks.Task<Guard>"));

    [TestMethod]
    public Task Move_OfAContainerNeedsALocalThatTheMethodOwns() => Verify.AnalyzerAsync(
        $$"""
        using System.Collections.Generic;
        using System.Threading.Tasks;
        using Singulink.CodeAnalysis;

        class C
        {
            Task<Guard> _task = null!;
            Guard _guard;
            List<Guard>[] _lists = new List<Guard>[2];
            static List<Guard> s_kept = new();

            static void Fill(List<Guard> list) { }

            static void Keep([Kept] List<Guard> list) => s_kept = list;

            static T MoveOfMyOwn<[NonCopyable] T>([MovedFrom] ref T value)
            {
                T moved = value;
                value = default!;
                return moved;
            }

            async Task M(RwLock rwLock, [Kept] Task<Guard> parameter)
            {
                // A local that holds what the method created or got from a call, and that it has not stored anywhere, is its own.
                var pending = rwLock.EnterTaskAsync();
                Guard a = await NonCopyable.Move(ref pending);

                var list = rwLock.CreateGuards();
                list.Add(rwLock.Enter());
                Fill(list);

                foreach (Guard guard in NonCopyable.Move(ref list)) { }

                // Clearing one variable says nothing about the other references to a container, and copying a reference is allowed. The
                // move is reported, and what it hands out is as stored as what went in.
                Guard b = {|#7:await NonCopyable.Move(ref {|#0:_task|})|};
                Guard c = {|#8:await NonCopyable.Move(ref {|#1:parameter|})|};
                Guard d = {|#9:NonCopyable.Move(ref {|#2:_lists[0]|})[0]|};

                var alias = _task;
                Guard e = {|#10:await NonCopyable.Move(ref {|#3:alias|})|};

                var shared = rwLock.EnterTaskAsync();
                _task = shared;
                Guard f = {|#11:await NonCopyable.Move(ref {|#4:shared|})|};

                var kept = rwLock.CreateGuards();
                Keep(kept);
                var g = NonCopyable.Move(ref {|#5:kept|});

                // A helper of one's own is checked in the same way.
                Guard h = {|#12:await MoveOfMyOwn(ref {|#6:_task|})|};

                // A value has one holder by construction, so it can be moved out of anything.
                Guard i = NonCopyable.Move(ref _guard);
            }
        }

        {{Sources.Guard}}
        """,
        Verify.ContainerMovedFromUnowned(0, "System.Threading.Tasks.Task<Guard>", "_task"),
        Verify.ContainerMovedFromUnowned(1, "System.Threading.Tasks.Task<Guard>", "parameter"),
        Verify.ContainerMovedFromUnowned(2, "System.Collections.Generic.List<Guard>", "_lists[0]"),
        Verify.ContainerMovedFromUnowned(3, "System.Threading.Tasks.Task<Guard>", "alias"),
        Verify.ContainerMovedFromUnowned(4, "System.Threading.Tasks.Task<Guard>", "shared", because: "on line 45 it is assigned to something else"),
        Verify.ContainerMovedFromUnowned(5, "System.Collections.Generic.List<Guard>", "kept", because: "on line 49 it is passed to 'C.Keep(List<Guard>)', which keeps it"),
        Verify.ContainerMovedFromUnowned(6, "System.Threading.Tasks.Task<Guard>", "_task"),
        Verify.ReadFromStoredContainer(7, "'await'", "Guard", canBeMoved: false),
        Verify.ReadFromStoredContainer(8, "'await'", "Guard", canBeMoved: false),
        Verify.ReadFromStoredContainer(9, "'List<T>.this[int]'", "Guard", canBeMoved: false),
        Verify.ReadFromStoredContainer(10, "'await'", "Guard", canBeMoved: false),
        Verify.ReadFromStoredContainer(11, "'await'", "Guard", canBeMoved: false),
        Verify.ReadFromStoredContainer(12, "'await'", "Guard", canBeMoved: false));

    [TestMethod]
    public Task Kept_AMethodThatKeepsAContainerParameterSaysSo() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using System.Collections.Generic;
        using Singulink.CodeAnalysis;

        sealed class Wrapper(List<Guard> list)
        {
            public int Count => list.Count;
        }

        class Holder
        {
            private List<Guard> _all = new();
            private readonly List<Guard>[] _slots = new List<Guard>[2];
            private readonly List<List<Guard>> _lists = new();
            private Action _action = null!;

            public List<Guard> All { get; set; } = new();

            // What a method is given, it only uses during the call.
            public void Fill(List<Guard> list, RwLock rwLock)
            {
                list.Add(rwLock.Enter());
                int count = list.Count;
                Count(list);
            }

            static int Count(List<Guard> list) => list.Count;

            // One that keeps it has to say so: the caller takes the container to be its own afterwards.
            public void ToField(List<Guard> list) => _all = {|#0:list|};

            public void ToElement(List<Guard> list) => _slots[0] = {|#1:list|};

            public void ToProperty(List<Guard> list) => All = {|#2:list|};

            public void Captures(List<Guard> list) => _action = () => {|#3:list|}.Clear();

            public void PassesOnToOneThatKeeps(List<Guard> list) => Register({|#4:list|});

            public void ToACollection(List<Guard> list) => _lists.Add({|#5:list|});

            public Wrapper ToAConstructor(List<Guard> list) => new Wrapper({|#6:list|});

            public void ThroughALocal(List<Guard> list)
            {
                var alias = list;
                _all = {|#7:alias|};
            }

            public void ByReference(ref List<Guard> list) => _all = {|#8:list|};

            // It is reported once for a parameter, where the method first keeps it.
            public void KeepsTwice(List<Guard> list)
            {
                _all = {|#9:list|};
                All = list;
            }

            public void Register([Kept] List<Guard> list) => _all = list;

            // A [MovedFrom] parameter hands its container over for good.
            public void TakeOver([MovedFrom] ref List<Guard> list)
            {
                var fresh = new List<Guard>();
                _all = list;
                list = fresh;
            }

            // A constructor and a setter are taken to keep what they are given.
            public Holder(List<Guard> list)
            {
                _all = list;
            }

            public List<Guard> Current
            {
                set => _all = value;
            }
        }

        {{Sources.Guard}}
        """,
        Verify.KeepsUnmarkedParameter(0, "Holder.ToField(List<Guard>)", "list", "System.Collections.Generic.List<Guard>"),
        Verify.KeepsUnmarkedParameter(1, "Holder.ToElement(List<Guard>)", "list", "System.Collections.Generic.List<Guard>"),
        Verify.KeepsUnmarkedParameter(2, "Holder.ToProperty(List<Guard>)", "list", "System.Collections.Generic.List<Guard>"),
        Verify.KeepsUnmarkedParameter(3, "Holder.Captures(List<Guard>)", "list", "System.Collections.Generic.List<Guard>"),
        Verify.KeepsUnmarkedParameter(4, "Holder.PassesOnToOneThatKeeps(List<Guard>)", "list", "System.Collections.Generic.List<Guard>"),
        Verify.KeepsUnmarkedParameter(5, "Holder.ToACollection(List<Guard>)", "list", "System.Collections.Generic.List<Guard>"),
        Verify.KeepsUnmarkedParameter(6, "Holder.ToAConstructor(List<Guard>)", "list", "System.Collections.Generic.List<Guard>"),
        Verify.KeepsUnmarkedParameter(7, "Holder.ThroughALocal(List<Guard>)", "list", "System.Collections.Generic.List<Guard>"),
        Verify.KeepsUnmarkedParameter(8, "Holder.ByReference(ref List<Guard>)", "list", "System.Collections.Generic.List<Guard>"),
        Verify.KeepsUnmarkedParameter(9, "Holder.KeepsTwice(List<Guard>)", "list", "System.Collections.Generic.List<Guard>"));

    [TestMethod]
    public Task Local_StopsBeingTheMethodsOwnWhenSomethingKeepsItOrAStoredContainerGoesIn() => Verify.AnalyzerAsync(
        $$"""
        using System.Collections.Generic;
        using Singulink.CodeAnalysis;

        sealed class Wrapper(List<Guard> list)
        {
            public int Count => list.Count;
        }

        class Holder
        {
            private List<Guard> _all = new();

            public List<Guard> All { get; set; } = new();

            public void Fill(List<Guard> list) { }

            public void Register([Kept] List<Guard> list) => _all = list;
        }

        class C
        {
            List<Guard> _stored = new();

            static List<Guard> Create() => new();

            // Passed to something that only uses it, the local stays the method's own.
            List<Guard> Filled(Holder holder)
            {
                var list = Create();
                holder.Fill(list);
                return list;
            }

            // Passed to something that keeps it, it does not.
            List<Guard> Registered(Holder holder)
            {
                var list = Create();
                holder.Register(list);
                return {|#0:list|};
            }

            List<Guard> GivenToAConstructor()
            {
                var list = Create();
                var wrapper = new Wrapper(list);
                return {|#1:list|};
            }

            List<Guard> GivenToASetter(Holder holder)
            {
                var list = Create();
                holder.All = list;
                return {|#2:list|};
            }

            List<Guard> PutIntoACollection(List<List<Guard>> lists)
            {
                var list = Create();
                lists.Add(list);
                return {|#3:list|};
            }

            // A local into which the method put a stored container holds something that is not the method's own.
            List<List<Guard>> HoldsAStoredList()
            {
                var outer = new List<List<Guard>>();
                outer.Add(_stored);
                return {|#4:outer|};
            }

            List<Guard>[] HoldsAStoredListInAnElement()
            {
                var array = new List<Guard>[1];
                array[0] = _stored;
                return {|#5:array|};
            }

            List<List<Guard>> CreatedWithAStoredList() => {|#6:new List<List<Guard>> { _stored }|};

            List<List<Guard>> HoldsItsOwn()
            {
                var outer = new List<List<Guard>>();
                outer.Add(Create());
                return outer;
            }
        }

        {{Sources.Guard}}
        """,
        Verify.UnownedContainerReturned(0, "C.Registered(Holder)", "System.Collections.Generic.List<Guard>", because: "'list' is not the method's own anywhere in the method, because on line 38 it is passed to 'Holder.Register(List<Guard>)', which keeps it"),
        Verify.UnownedContainerReturned(1, "C.GivenToAConstructor()", "System.Collections.Generic.List<Guard>", because: "'list' is not the method's own anywhere in the method, because on line 45 it is passed to 'Wrapper.Wrapper(List<Guard>)', which keeps it"),
        Verify.UnownedContainerReturned(2, "C.GivenToASetter(Holder)", "System.Collections.Generic.List<Guard>", because: "'list' is not the method's own anywhere in the method, because on line 52 it is assigned to something else"),
        Verify.UnownedContainerReturned(3, "C.PutIntoACollection(List<List<Guard>>)", "System.Collections.Generic.List<Guard>", because: "'list' is not the method's own anywhere in the method, because on line 59 it is passed to 'List<List<Guard>>.Add(List<Guard>)', which is taken to keep what a type parameter is given"),
        Verify.UnownedContainerReturned(4, "C.HoldsAStoredList()", "System.Collections.Generic.List<System.Collections.Generic.List<Guard>>", because: "'outer' is not the method's own anywhere in the method, because on line 67 'List<List<Guard>>.Add(List<Guard>)' can keep it, hand out a view of it or take in what something else holds"),
        Verify.UnownedContainerReturned(5, "C.HoldsAStoredListInAnElement()", "System.Collections.Generic.List<Guard>[]", because: "'array' is not the method's own anywhere in the method, because on line 74 it is used in a way that can keep it or a view of it"),
        Verify.UnownedContainerReturned(6, "C.CreatedWithAStoredList()", "System.Collections.Generic.List<System.Collections.Generic.List<Guard>>"));

    [TestMethod]
    public Task Kept_MustNotBeAddedWhereTheBaseMemberOnlyUses() => Verify.AnalyzerAsync(
        $$"""
        using System.Collections.Generic;
        using Singulink.CodeAnalysis;

        abstract class Base
        {
            public abstract void Use(List<Guard> list);

            public abstract void Keep([Kept] List<Guard> list);
        }

        class Derived : Base
        {
            private List<Guard> _list = new();

            // A caller through the base member takes its list to be its own afterwards.
            public override void Use([Kept] List<Guard> {|#0:list|}) => _list = list;

            // The other way round only makes the caller more careful.
            public override void Keep(List<Guard> list) { }
        }

        interface IUse
        {
            void Use(List<Guard> list);
        }

        class Implementation : IUse
        {
            private List<Guard> _list = new();

            public void Use([Kept] List<Guard> {|#1:list|}) => _list = list;
        }

        // A delegate passes on what it is called with, so its type has to say the same.
        delegate void User(List<Guard> list);

        delegate void Keeper([Kept] List<Guard> list);

        class C
        {
            private List<Guard> _list = new();

            void Keep([Kept] List<Guard> list) => _list = list;

            void M()
            {
                User user = {|#2:Keep|};
                Keeper keeper = Keep;
            }

            List<Guard> ThroughADelegate(Keeper keeper)
            {
                var list = new List<Guard>();
                keeper(list);
                return {|#3:list|};
            }
        }

        {{Sources.Guard}}
        """,
        Verify.KeptNotOnBaseMember(0, "list", "Derived.Use(List<Guard>)", "Base.Use(List<Guard>)"),
        Verify.KeptNotOnBaseMember(1, "list", "Implementation.Use(List<Guard>)", "IUse.Use(List<Guard>)"),
        Verify.KeptNotOnDelegate(2, "list", "C.Keep(List<Guard>)", "User"),
        Verify.UnownedContainerReturned(3, "C.ThroughADelegate(Keeper)", "System.Collections.Generic.List<Guard>", because: "'list' is not the method's own anywhere in the method, because on line 54 it is passed to 'Keeper.Invoke(List<Guard>)', which keeps it"));

    [TestMethod]
    public Task Kept_WritingAContainerParameterThroughAReferenceKeepsIt() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using System.Collections.Generic;
        using Singulink.CodeAnalysis;

        struct Pair
        {
            public List<Guard> First;
        }

        sealed class Wrapper
        {
            private List<Guard> _list = new();

            public static implicit operator Wrapper([Kept] List<Guard> list) => new() { _list = list };

            public static Wrapper operator +(Wrapper wrapper, [Kept] List<Guard> list) => new() { _list = list };
        }

        sealed class Count
        {
            public int Value;

            public static implicit operator Count(List<Guard> list) => new() { Value = list.Count };

            public static Count operator +(Count count, List<Guard> list) => new() { Value = count.Value + list.Count };
        }

        unsafe class Holder
        {
            private List<Guard> _all = new();
            private List<Guard>[] _slots = new List<Guard>[2];
            private Pair _pair;
            private (List<Guard>, int) _tuple;
            private int _count;
            private Wrapper _wrapper = new();
            private Action _action = () => { };

            ref List<Guard> GetSlot() => ref _all;

            // A reference can refer to a field, an element or anything else that outlives the call.
            public void ThroughARefLocal(List<Guard> list)
            {
                ref List<Guard> slot = ref _all;
                slot = {|#0:list|};
            }

            public void ThroughARefParameter(List<Guard> list, ref List<Guard> target) => target = {|#1:list|};

            public void ThroughAnOutParameter(List<Guard> list, out List<Guard> target) => target = {|#2:list|};

            public void ThroughARefReturn(List<Guard> list) => GetSlot() = {|#3:list|};

            public void ThroughAFieldOfARefLocal(List<Guard> list)
            {
                ref Pair pair = ref _pair;
                pair.First = {|#4:list|};
            }

            public void ThroughAnElementOfASpan(List<Guard> list)
            {
                Span<List<Guard>> slots = _slots;
                slots[0] = {|#5:list|};
            }

            public void ThroughAPointer(List<Guard> list)
            {
                fixed (List<Guard>* slot = &_all)
                    *slot = {|#6:list|};
            }

            public void ALocalThatHoldsIt(List<Guard> list)
            {
                var alias = list;
                ref List<Guard> slot = ref _all;
                slot = {|#7:alias|};
            }

            public void IfThereIsNone(List<Guard> list)
            {
                ref List<Guard> slot = ref _all;
                slot ??= {|#8:list|};
            }

            // The order of the statements does not matter: the store runs after the assignment.
            public void InALoop(List<Guard> list)
            {
                List<Guard> held = null!;

                for (int i = 0; i < 2; i++)
                {
                    if (held != null)
                        _all = {|#9:held|};

                    held = list;
                }
            }

            public void AfterAGoto(List<Guard> list)
            {
                List<Guard> held = null!;

            again:
                if (held != null)
                {
                    _all = {|#10:held|};
                    return;
                }

                held = list;
                goto again;
            }

            public void ThroughTwoLocals(List<Guard> list)
            {
                List<Guard> first = null!;
                List<Guard> second = null!;

                for (int i = 0; i < 3; i++)
                {
                    if (second != null)
                        _all = {|#11:second|};

                    second = first;
                    first = list;
                }
            }

            public void MatchedByAPattern(List<Guard> list)
            {
                if (list is { } matched)
                    _all = {|#12:matched|};
            }

            public void ThroughAnotherParameter(List<Guard> list, [Kept] List<Guard> other)
            {
                other = list;
                _all = {|#13:other|};
            }

            // A tuple carries the container to wherever the tuple goes.
            public void ByADeconstruction(List<Guard> list) => (_all, _count) = ({|#14:list|}, 1);

            public void InATuple(List<Guard> list) => _tuple = ({|#15:list|}, 1);

            public void InATupleLocal(List<Guard> list)
            {
                var tuple = (list, 1);
                _tuple = {|#16:tuple|};
            }

            public void ThroughADeconstructedLocal(List<Guard> list)
            {
                List<Guard> held;
                (held, _count) = (list, 1);
                _all = {|#17:held|};
            }

            public void InANestedDeconstruction(List<Guard> list) => (_count, (_all, _count)) = (1, ({|#18:list|}, 2));

            public void OutOfATupleLocal(List<Guard> list)
            {
                var tuple = (list, 1);
                _count = tuple.Item2;
                _all = {|#19:tuple|}.Item1;
            }

            public void ByADeconstructionOfALocal(List<Guard> list)
            {
                var tuple = (list, 1);
                (_all, _count) = {|#20:tuple|};
            }

            public void ThroughAPositionalPattern(List<Guard> list)
            {
                if ((list, 1) is (var matched, _))
                    _all = {|#21:matched|};
            }

            public void ThroughASwitchOnATuple(List<Guard> list, bool flag)
            {
                switch ((list, flag))
                {
                    case (var matched, true):
                        _all = {|#22:matched|};
                        break;
                }
            }

            public void ThroughAnArmOfASwitchExpression(List<Guard> list, bool flag) => _all = (list, flag) switch {
                (var matched, true) => {|#23:matched|},
                _ => _all,
            };

            public void ThroughADeconstructedTupleLocal(List<Guard> list)
            {
                var tuple = (list, 1);
                var (held, count) = tuple;
                _all = {|#24:held|};
            }

            public void ThroughATupleLocalTakenApart(List<Guard> list)
            {
                var tuple = (list, 1);
                List<Guard> held;
                (held, _count) = tuple;
                _all = {|#25:held|};
            }

            public void OutOfASwitchExpression(List<Guard> list, bool flag) => _all = flag switch {
                true => {|#26:list|},
                _ => _all,
            };

            // A user-defined conversion or operator is a call, and a delegate for a method of the container holds the container.
            public void ByAConversion(List<Guard> list) => _wrapper = {|#27:list|};

            public void ByAnOperator(List<Guard> list) => _wrapper = _wrapper + {|#28:list|};

            public void ByACompoundOperator(List<Guard> list) => _wrapper += {|#29:list|};

            public void ByAMethodGroup(List<Guard> list) => _action = {|#30:list|}.Clear;

            public void ByAMethodGroupInALocal(List<Guard> list)
            {
                Action clear = list.Clear;
                _action = {|#31:clear|};
            }

            public Action ByAMethodGroupThatIsReturned(List<Guard> list) => {|#32:list|}.Clear;

            public void ByAMethodGroupThatIsPassedOn(List<Guard> list) => Run({|#33:list|}.Clear);

            static void Run(Action action) => action();

            // Nothing says what a call does with its argument where the method is not known.
            public void ToADynamicCall(List<Guard> list, dynamic target) => target.Keep({|#34:list|});

            public void ThroughAFunctionPointer(List<Guard> list)
            {
                delegate*<List<Guard>, void> keep = &Keep;
                keep({|#35:list|});
            }

            static void Keep([Kept] List<Guard> list) { }

            public void ByAMethodGroupThatIsOnlyCalled(List<Guard> list)
            {
                Action clear = list.Clear;
                clear();
            }

            public void ByAConversionThatKeepsNothing(List<Guard> list)
            {
                Count count = list;
                count = count + list;
            }

            // Taken apart into variables of the method, used in place or only tested, it goes nowhere.
            public void TakenApart(List<Guard> list, List<Guard> other, bool flag)
            {
                (List<Guard> first, int count) = (list, 1);
                (_, _count) = (list, 2);
                first.Clear();

                var tuple = (list, 1);
                (List<Guard> second, _count) = tuple;
                int size = (list, 1).Item1.Count + tuple.Item1.Count;

                bool both = (list, other) is (null, null);

                int result = (list, flag) switch {
                    (null, _) => 0,
                    (_, true) => 1,
                    _ => 2,
                };

                switch ((list, other))
                {
                    case (null, null):
                        break;

                    case (var one, _):
                        one.Clear();
                        break;
                }

                if ((list, 1) is (var seen, _))
                    seen.Clear();

                var (third, _) = tuple;
                third.Clear();
            }

            // A variable of the method holds the container for the call only, and a reference to that variable stores nothing.
            public void IntoALocal(List<Guard> list)
            {
                List<Guard> local;
                local = list;
                local.Clear();
            }

            public void ReferredTo(List<Guard> list, List<Guard> other)
            {
                ref List<Guard> alias = ref list;
                int count = alias.Count;
                alias = ref other;
                alias = ref list;
            }

            public void Marked([Kept] List<Guard> list)
            {
                ref List<Guard> slot = ref _all;
                slot = list;
            }

            public void ListOfItsOwn()
            {
                var own = new List<Guard>();
                ref List<Guard> slot = ref _all;
                slot = own;
            }
        }

        {{Sources.Guard}}
        """,
        KeptBy(0, "ThroughARefLocal(List<Guard>)"),
        KeptBy(1, "ThroughARefParameter(List<Guard>, ref List<Guard>)"),
        KeptBy(2, "ThroughAnOutParameter(List<Guard>, out List<Guard>)"),
        KeptBy(3, "ThroughARefReturn(List<Guard>)"),
        KeptBy(4, "ThroughAFieldOfARefLocal(List<Guard>)"),
        KeptBy(5, "ThroughAnElementOfASpan(List<Guard>)"),
        KeptBy(6, "ThroughAPointer(List<Guard>)"),
        KeptBy(7, "ALocalThatHoldsIt(List<Guard>)"),
        KeptBy(8, "IfThereIsNone(List<Guard>)"),
        KeptBy(9, "InALoop(List<Guard>)"),
        KeptBy(10, "AfterAGoto(List<Guard>)"),
        KeptBy(11, "ThroughTwoLocals(List<Guard>)"),
        KeptBy(12, "MatchedByAPattern(List<Guard>)"),
        KeptBy(13, "ThroughAnotherParameter(List<Guard>, List<Guard>)"),
        KeptBy(14, "ByADeconstruction(List<Guard>)"),
        KeptBy(15, "InATuple(List<Guard>)"),
        KeptBy(16, "InATupleLocal(List<Guard>)"),
        KeptBy(17, "ThroughADeconstructedLocal(List<Guard>)"),
        KeptBy(18, "InANestedDeconstruction(List<Guard>)"),
        KeptBy(19, "OutOfATupleLocal(List<Guard>)"),
        KeptBy(20, "ByADeconstructionOfALocal(List<Guard>)"),
        KeptBy(21, "ThroughAPositionalPattern(List<Guard>)"),
        KeptBy(22, "ThroughASwitchOnATuple(List<Guard>, bool)"),
        KeptBy(23, "ThroughAnArmOfASwitchExpression(List<Guard>, bool)"),
        KeptBy(24, "ThroughADeconstructedTupleLocal(List<Guard>)"),
        KeptBy(25, "ThroughATupleLocalTakenApart(List<Guard>)"),
        KeptBy(26, "OutOfASwitchExpression(List<Guard>, bool)"),
        KeptBy(27, "ByAConversion(List<Guard>)"),
        KeptBy(28, "ByAnOperator(List<Guard>)"),
        KeptBy(29, "ByACompoundOperator(List<Guard>)"),
        KeptBy(30, "ByAMethodGroup(List<Guard>)"),
        KeptBy(31, "ByAMethodGroupInALocal(List<Guard>)"),
        KeptBy(32, "ByAMethodGroupThatIsReturned(List<Guard>)"),
        KeptBy(33, "ByAMethodGroupThatIsPassedOn(List<Guard>)"),
        KeptBy(34, "ToADynamicCall(List<Guard>, dynamic)"),
        KeptBy(35, "ThroughAFunctionPointer(List<Guard>)"));

    private static Microsoft.CodeAnalysis.Testing.DiagnosticResult KeptBy(int location, string method) =>
        Verify.KeepsUnmarkedParameter(location, "Holder." + method, "list", "System.Collections.Generic.List<Guard>");

    // A method group keeps what its method keeps. A parameter whose type is a type parameter is taken to keep what it is given, so
    // converted to a delegate whose parameter is a container that is not marked [Kept], the method keeps what every caller of the
    // delegate takes to be its own still.
    [TestMethod]
    public Task Kept_AMethodGroupWhoseParameterIsATypeParameter_IsReportedWhereTheDelegateDoesNotKeep() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using System.Collections.Generic;
        using Singulink.CodeAnalysis;

        public delegate void GuardsSink(List<Guard> guards);

        public delegate void KeptSink([Kept] List<Guard> guards);

        public delegate void TextSink(string text);

        public delegate void MarkedSink<[NonCopyable] T>(T item);

        public sealed class Registry<[NonCopyable] T>
        {
            private readonly T[] _items = new T[16];
            private int _count;

            public void Add(T item) => _items[_count++] = item;
        }

        public static class Puts
        {
            public static void Put<[NonCopyable] T>(T item) { }

            public static void Count(List<Guard> guards) { }
        }

        class C
        {
            static readonly Registry<List<Guard>> s_registered = new();
            static readonly GuardsSink s_onCreated = {|#0:s_registered.Add|};

            void M(Action<List<Guard>> action, Registry<string> texts)
            {
                GuardsSink inALocal = {|#1:s_registered.Add|};
                GuardsSink inferred = {|#2:Puts.Put|};
                GuardsSink wrapped = new GuardsSink({|#3:action|});
                Use({|#4:s_registered.Add|});

                // The delegate's parameter keeps as well: its callers are told so, or take it so.
                KeptSink kept = s_registered.Add;
                Action<List<Guard>> generic = s_registered.Add;
                MarkedSink<List<Guard>> marked = s_registered.Add;

                // A method that only uses what it is given, and a parameter that is no container.
                GuardsSink counting = Puts.Count;
                TextSink text = texts.Add;
            }

            static void Use(GuardsSink sink) { }
        }

        {{Sources.Guard}}
        """,
        Verify.KeepingNotOnDelegate(0, "item", "Registry<List<Guard>>.Add(List<Guard>)", "GuardsSink"),
        Verify.KeepingNotOnDelegate(1, "item", "Registry<List<Guard>>.Add(List<Guard>)", "GuardsSink"),
        Verify.KeepingNotOnDelegate(2, "item", "Puts.Put<List<Guard>>(List<Guard>)", "GuardsSink"),
        Verify.KeepingNotOnDelegate(3, "obj", "Action<List<Guard>>.Invoke(List<Guard>)", "GuardsSink"),
        Verify.KeepingNotOnDelegate(4, "item", "Registry<List<Guard>>.Add(List<Guard>)", "GuardsSink"));

    // Why a container that a call passes to a parameter of a type parameter is not the method's own.
    private static string KeptThroughATypeParameter(int line, string target) =>
        $"'list' is not the method's own anywhere in the method, because on line {line} it is passed to '{target}', which is taken to keep " +
        "what a type parameter is given";

    // A call through a parameter whose type is a type parameter, marked or not, takes the container to be kept, so a method that keeps
    // it may stand behind such a parameter of a delegate: [Kept] adds nothing there that a caller could miss. Behind a parameter of a
    // container type that is not marked [Kept], a caller takes the container to be its own still.
    [TestMethod]
    public Task Kept_AMethodThatKeepsBehindAParameterOfATypeParameterOfADelegateAddsNothing() => Verify.AnalyzerAsync(
        $$"""
        using System.Collections.Generic;
        using Singulink.CodeAnalysis;

        public delegate void PlainSink(List<Guard> list);

        public delegate void KeptSink([Kept] List<Guard> list);

        public delegate void MarkedSink<[NonCopyable] T>(T item);

        public delegate void MarkedRefSink<[NonCopyable] T>(ref T item);

        public delegate void MarkedInSink<[NonCopyable] T>(in T item);

        public sealed class Registry<[NonCopyable] T>
        {
            private readonly T[] _items = new T[16];
            private int _count;

            public void Add(T item) => _items[_count++] = item;
        }

        class C
        {
            static readonly List<List<Guard>> s_all = new();
            static readonly Registry<List<Guard>> s_registry = new();

            static void Register([Kept] List<Guard> list) => s_all.Add(list);

            static void RegisterByRef([Kept] ref List<Guard> list) => s_all.Add(list);

            static void RegisterByIn([Kept] in List<Guard> list) => s_all.Add(list);

            void Assignments()
            {
                PlainSink plain = {|#0:Register|};
                KeptSink kept = Register;
                MarkedSink<List<Guard>> fromRegistry = s_registry.Add;
                MarkedSink<List<Guard>> marked = Register;
                MarkedRefSink<List<Guard>> byRef = RegisterByRef;
                MarkedInSink<List<Guard>> byIn = RegisterByIn;
                MarkedSink<List<Guard>> keptLambda = ([Kept] List<Guard> list) => Register(list);
                Register(new List<Guard>());
            }

            // The caller of a delegate whose parameter is a container keeps the container; the caller of one whose parameter is of a
            // type parameter counts it as kept, in every form.
            List<Guard> ThroughAPlainSink(PlainSink plain)
            {
                var list = new List<Guard>();
                plain(list);
                return list;
            }

            List<Guard> ThroughAMarkedSink(MarkedSink<List<Guard>> marked)
            {
                var list = new List<Guard>();
                marked(list);
                return {|#1:list|};
            }

            List<Guard> ThroughAMarkedRefSink(MarkedRefSink<List<Guard>> byRef)
            {
                var list = new List<Guard>();
                byRef(ref list);
                return {|#2:list|};
            }

            List<Guard> ThroughAMarkedInSink(MarkedInSink<List<Guard>> byIn)
            {
                var list = new List<Guard>();
                byIn(in list);
                return {|#3:list|};
            }
        }

        {{Sources.Guard}}
        """,
        Verify.KeptNotOnDelegate(0, "list", "C.Register(List<Guard>)", "PlainSink"),
        Verify.UnownedContainerReturned(1, "C.ThroughAMarkedSink(MarkedSink<List<Guard>>)", "System.Collections.Generic.List<Guard>", because: KeptThroughATypeParameter(57, "MarkedSink<List<Guard>>.Invoke(List<Guard>)")),
        Verify.UnownedContainerReturned(2, "C.ThroughAMarkedRefSink(MarkedRefSink<List<Guard>>)", "System.Collections.Generic.List<Guard>", because: KeptThroughATypeParameter(64, "MarkedRefSink<List<Guard>>.Invoke(ref List<Guard>)")),
        Verify.UnownedContainerReturned(3, "C.ThroughAMarkedInSink(MarkedInSink<List<Guard>>)", "System.Collections.Generic.List<Guard>", because: KeptThroughATypeParameter(71, "MarkedInSink<List<Guard>>.Invoke(in List<Guard>)")));

    // The same holds for an override and an implementation: a call through a base member or an interface member whose parameter is of
    // a type parameter takes the container to be kept. A base member that the list marks 'borrows', and one whose parameter is a
    // container, are not taken to keep, and a [Kept] parameter behind them still gets the warning.
    [TestMethod]
    public Task Kept_AnOverrideOrImplementationBehindAParameterOfATypeParameterAddsNothing() => Verify.AnalyzerAsync(
        $$"""
        using System.Collections.Generic;
        using Singulink.CodeAnalysis;

        public abstract class Base<[NonCopyable] T>
        {
            public abstract void Use(T item);
        }

        public interface IUse<[NonCopyable] T>
        {
            void Use(T item);
        }

        public abstract class Plain
        {
            public abstract void Use(List<Guard> list);
        }

        public abstract class Borrowing<[NonCopyable] T>
        {
            public abstract void Use(T item);
        }

        sealed class Derived : Base<List<Guard>>
        {
            private List<Guard> _list = new();

            public override void Use([Kept] List<Guard> item) => _list = item;
        }

        sealed class Implementation : IUse<List<Guard>>
        {
            private List<Guard> _list = new();

            public void Use([Kept] List<Guard> item) => _list = item;
        }

        sealed class Explicit : IUse<List<Guard>>
        {
            private List<Guard> _list = new();

            void IUse<List<Guard>>.Use([Kept] List<Guard> item) => _list = item;
        }

        sealed class DerivedPlain : Plain
        {
            private List<Guard> _list = new();

            public override void Use([Kept] List<Guard> {|#0:list|}) => _list = list;
        }

        sealed class DerivedBorrowing : Borrowing<List<Guard>>
        {
            private List<Guard> _list = new();

            public override void Use([Kept] List<Guard> {|#1:item|}) => _list = item;
        }

        class C
        {
            List<Guard> ThroughBase(Base<List<Guard>> used)
            {
                var list = new List<Guard>();
                used.Use(list);
                return {|#2:list|};
            }

            List<Guard> ThroughInterface(IUse<List<Guard>> used)
            {
                var list = new List<Guard>();
                used.Use(list);
                return {|#3:list|};
            }

            List<Guard> ThroughBorrowing(Borrowing<List<Guard>> used)
            {
                var list = new List<Guard>();
                used.Use(list);
                return list;
            }
        }

        {{Sources.Guard}}
        """,
        "M:Borrowing`1.Use(`0);borrows",
        Verify.KeptNotOnBaseMember(0, "list", "DerivedPlain.Use(List<Guard>)", "Plain.Use(List<Guard>)"),
        Verify.KeptNotOnBaseMember(1, "item", "DerivedBorrowing.Use(List<Guard>)", "Borrowing<T>.Use(T)"),
        Verify.UnownedContainerReturned(2, "C.ThroughBase(Base<List<Guard>>)", "System.Collections.Generic.List<Guard>", because: KeptThroughATypeParameter(64, "Base<List<Guard>>.Use(List<Guard>)")),
        Verify.UnownedContainerReturned(3, "C.ThroughInterface(IUse<List<Guard>>)", "System.Collections.Generic.List<Guard>", because: KeptThroughATypeParameter(71, "IUse<List<Guard>>.Use(List<Guard>)")));

    // A lambda or an anonymous method is called only through the delegate that it is made as. Where every call through that delegate's
    // parameter takes the container to be kept, by [Kept] or by a type parameter as its type, the lambda may keep it. The delegate is the
    // target type, or the natural type of a lambda that has none. A lambda made as a delegate whose parameter is a container keeps
    // SCA1032, and so does one of an expression tree, which is not made into a delegate where it stands. A later conversion of the
    // delegate to another delegate type gets SCA1040 where its callers take the container to be theirs still.
    [TestMethod]
    public Task Kept_ALambdaThatKeepsBehindADelegateParameterThatKeepsIsClean() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using System.Collections.Generic;
        using System.Linq.Expressions;
        using Singulink.CodeAnalysis;

        public delegate void PlainSink(List<Guard> list);

        public delegate void KeptSink([Kept] List<Guard> list);

        public delegate void MarkedSink<[NonCopyable] T>(T item);

        class C
        {
            static readonly List<List<Guard>> s_all = new();

            static void Register([Kept] List<Guard> list) => s_all.Add(list);

            static void Take(Action<List<Guard>> action) { }

            static void TakeFunc(Func<List<Guard>, int> func) { }

            static void TakeDelegate<TDel>(TDel d) where TDel : Delegate { }

            static MarkedSink<List<Guard>> MakeMarked() => list => Register(list);

            static PlainSink MakePlain() => list => Register({|#0:list|});

            void Lambdas()
            {
                MarkedSink<List<Guard>> marked = list => Register(list);
                KeptSink kept = list => Register(list);
                KeptSink keptWritten = ([Kept] list) => Register(list);
                Action<List<Guard>> action = list => Register(list);
                Take(list => Register(list));
                TakeFunc(list => { Register(list); return 0; });
                MarkedSink<List<Guard>> anonymous = delegate (List<Guard> list) { Register(list); };
                var natural = (List<Guard> list) => Register(list);
                Delegate asDelegate = {|#1:(List<Guard> list) => Register(list)|};
                object asObject = {|#2:(List<Guard> list) => Register(list)|};
                {|#3:TakeDelegate((List<Guard> list) => Register(list))|};

                PlainSink plain = list => Register({|#4:list|});
                PlainSink cast = (PlainSink)(list => Register({|#5:list|}));
                Expression<Action<List<Guard>>> tree = list => Register({|#6:list|});

                // A conversion of a delegate that keeps to one whose callers take the container to be theirs still.
                PlainSink fromMarked = new PlainSink({|#7:marked|});
                PlainSink fromInvoke = {|#8:marked.Invoke|};
                PlainSink fromAction = new PlainSink({|#9:action|});
                PlainSink fromNatural = new PlainSink({|#10:natural|});
            }

            // The caller of a delegate whose parameter keeps counts the container as kept.
            List<Guard> ThroughAMarkedSink(MarkedSink<List<Guard>> marked)
            {
                var list = new List<Guard>();
                marked(list);
                return {|#11:list|};
            }

            List<Guard> ThroughAKeptSink(KeptSink kept)
            {
                var list = new List<Guard>();
                kept(list);
                return {|#12:list|};
            }

            List<Guard> ThroughAnAction(Action<List<Guard>> action)
            {
                var list = new List<Guard>();
                action(list);
                return {|#13:list|};
            }

            List<Guard> ThroughAFunc(Func<List<Guard>, int> func)
            {
                var list = new List<Guard>();
                func(list);
                return {|#14:list|};
            }
        }

        {{Sources.Guard}}
        """,
        Verify.KeepsUnmarkedParameter(0, "lambda expression", "list", "System.Collections.Generic.List<Guard>"),
        Verify.OpaqueConversion(1, "System.Action<System.Collections.Generic.List<Guard>>", "System.Delegate"),
        Verify.OpaqueConversion(2, "System.Action<System.Collections.Generic.List<Guard>>", "object"),
        Verify.OpaqueTypeArgument(3, "C.TakeDelegate<TDel>(TDel)", "System.Action<System.Collections.Generic.List<Guard>>", "TDel", "M:C.TakeDelegate``1(``0)"),
        Verify.KeepsUnmarkedParameter(4, "lambda expression", "list", "System.Collections.Generic.List<Guard>"),
        Verify.KeepsUnmarkedParameter(5, "lambda expression", "list", "System.Collections.Generic.List<Guard>"),
        Verify.KeepsUnmarkedParameter(6, "lambda expression", "list", "System.Collections.Generic.List<Guard>"),
        Verify.KeepingNotOnDelegate(7, "item", "MarkedSink<List<Guard>>.Invoke(List<Guard>)", "PlainSink"),
        Verify.KeepingNotOnDelegate(8, "item", "MarkedSink<List<Guard>>.Invoke(List<Guard>)", "PlainSink"),
        Verify.KeepingNotOnDelegate(9, "obj", "Action<List<Guard>>.Invoke(List<Guard>)", "PlainSink"),
        Verify.KeepingNotOnDelegate(10, "obj", "Action<List<Guard>>.Invoke(List<Guard>)", "PlainSink"),
        Verify.UnownedContainerReturned(11, "C.ThroughAMarkedSink(MarkedSink<List<Guard>>)", "System.Collections.Generic.List<Guard>", because: KeptThroughATypeParameter(57, "MarkedSink<List<Guard>>.Invoke(List<Guard>)")),
        Verify.UnownedContainerReturned(
            12,
            "C.ThroughAKeptSink(KeptSink)",
            "System.Collections.Generic.List<Guard>",
            because: "'list' is not the method's own anywhere in the method, because on line 64 it is passed to 'KeptSink.Invoke(List<Guard>)', which keeps it"),
        Verify.UnownedContainerReturned(13, "C.ThroughAnAction(Action<List<Guard>>)", "System.Collections.Generic.List<Guard>", because: KeptThroughATypeParameter(71, "Action<List<Guard>>.Invoke(List<Guard>)")),
        Verify.UnownedContainerReturned(14, "C.ThroughAFunc(Func<List<Guard>, int>)", "System.Collections.Generic.List<Guard>", because: KeptThroughATypeParameter(78, "Func<List<Guard>, int>.Invoke(List<Guard>)")));

    // A lambda whose parameter is a container passed by reference keeps SCA1032 where it keeps the container, with its natural type as
    // with a delegate type that it is converted to. A caller that passes its own local by 'ref', 'in' or 'ref readonly' gives it up, but
    // a method that forwards its own parameter the same way does not, and gets no SCA1032, so a lambda that kept the container behind
    // such a delegate would go unwarned. Behind a parameter of a type parameter the forwarding method gets SCA1032, and the lambda may
    // keep. An 'out' parameter is given nothing to keep. (QA9 small thing 1)
    [TestMethod]
    public Task Kept_ALambdaWhoseParameterIsPassedByReferenceKeepsItsWarning() => Verify.AnalyzerAsync(
        $$"""
        using System.Collections.Generic;
        using Singulink.CodeAnalysis;

        public delegate void RefSink(ref List<Guard> list);

        public delegate void InSink(in List<Guard> list);

        public delegate void RefReadOnlySink(ref readonly List<Guard> list);

        public delegate void MarkedRefSink<[NonCopyable] T>(ref T item);

        class C
        {
            static readonly List<List<Guard>> s_all = new();

            static void Register([Kept] List<Guard> list) => s_all.Add(list);

            void Lambdas()
            {
                var naturalRef = (ref List<Guard> list) => Register({|#0:list|});
                var naturalIn = (in List<Guard> list) => Register({|#1:list|});
                var naturalRefReadOnly = (ref readonly List<Guard> list) => Register({|#2:list|});
                RefSink refSink = (ref List<Guard> list) => Register({|#3:list|});
                InSink inSink = (in List<Guard> list) => Register({|#4:list|});
                RefReadOnlySink refReadOnlySink = (ref readonly List<Guard> list) => Register({|#5:list|});
                MarkedRefSink<List<Guard>> markedRef = (ref List<Guard> list) => Register(list);
            }

            // The caller's own local: passing it by reference ends the caller's ownership.
            List<Guard> ThroughRef(RefSink sink)
            {
                var list = new List<Guard>();
                sink(ref list);
                return {|#6:list|};
            }

            List<Guard> ThroughIn(InSink sink)
            {
                var list = new List<Guard>();
                sink(in list);
                return {|#7:list|};
            }

            List<Guard> ThroughRefReadOnly(RefReadOnlySink sink)
            {
                var list = new List<Guard>();
                sink(ref list);
                return {|#8:list|};
            }

            // A parameter of the method that it forwards by reference: no warning, so the lambda must keep its own.
            static void ForwardRef(RefSink sink, List<Guard> list) => sink(ref list);

            static void ForwardIn(InSink sink, List<Guard> list) => sink(in list);

            static void ForwardRefReadOnly(RefReadOnlySink sink, List<Guard> list) => sink(in list);

            // Behind a type parameter, the forwarding method gets the warning instead.
            static void ForwardMarkedRef(MarkedRefSink<List<Guard>> sink, List<Guard> list) => sink(ref {|#9:list|});
        }

        {{Sources.Guard}}
        """,
        Verify.KeepsUnmarkedParameter(0, "lambda expression", "list", "System.Collections.Generic.List<Guard>"),
        Verify.KeepsUnmarkedParameter(1, "lambda expression", "list", "System.Collections.Generic.List<Guard>"),
        Verify.KeepsUnmarkedParameter(2, "lambda expression", "list", "System.Collections.Generic.List<Guard>"),
        Verify.KeepsUnmarkedParameter(3, "lambda expression", "list", "System.Collections.Generic.List<Guard>"),
        Verify.KeepsUnmarkedParameter(4, "lambda expression", "list", "System.Collections.Generic.List<Guard>"),
        Verify.KeepsUnmarkedParameter(5, "lambda expression", "list", "System.Collections.Generic.List<Guard>"),
        Verify.UnownedContainerReturned(6, "C.ThroughRef(RefSink)", "System.Collections.Generic.List<Guard>", because: ByReference(33, "RefSink.Invoke(ref List<Guard>)")),
        Verify.UnownedContainerReturned(7, "C.ThroughIn(InSink)", "System.Collections.Generic.List<Guard>", because: ByReference(40, "InSink.Invoke(in List<Guard>)")),
        Verify.UnownedContainerReturned(8, "C.ThroughRefReadOnly(RefReadOnlySink)", "System.Collections.Generic.List<Guard>", because: ByReference(47, "RefReadOnlySink.Invoke(ref readonly List<Guard>)")),
        Verify.KeepsUnmarkedParameter(9, "C.ForwardMarkedRef(MarkedRefSink<List<Guard>>, List<Guard>)", "list", "System.Collections.Generic.List<Guard>"));

    private static string ByReference(int line, string target) =>
        $"'list' is not the method's own anywhere in the method, because on line {line} it is passed to '{target}'";

    // A parameter whose type is a type parameter is taken to keep what it is given, in whatever form the container is passed to it:
    // as an element of the collection that the compiler makes for 'params', as the operand of an operator or of a conversion, as
    // the right side of a compound assignment operator that is an instance member, and as the receiver of an extension block.
    [TestMethod]
    public Task Kept_PassingAParameterOnToATypeParameterKeepsItInEveryForm() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using System.Collections.Generic;
        using Singulink.CodeAnalysis;

        public sealed class Bag<[NonCopyable] T>
        {
            private readonly T[] _items = new T[16];
            private int _count;

            public void AddRange(params T[] items)
            {
                foreach (ref T item in items.AsSpan())
                    _items[_count++] = NonCopyable.Move(ref item);
            }

            public void AddSpan(params ReadOnlySpan<T> items) { }

            public void Count(params int[] numbers) { }

            public void operator +=(T item) => _items[_count++] = item;

            public static Bag<T> operator +(Bag<T> bag, T item) => new Bag<T>();

            public static Bag<T> operator |(T item, Bag<T> bag) => new Bag<T>();

            public static bool operator !(Bag<T> bag) => bag._count is 0;
        }

        public sealed class Handle<[NonCopyable] T>
        {
            public static implicit operator Handle<T>(T item) => new Handle<T>();
        }

        public static class Puts
        {
            public static void PutMany<[NonCopyable] T>(params T[] items) { }

            extension<[NonCopyable] T>(T item)
            {
                public void Park() { }

                public int Parked => 0;
            }

            extension(List<Guard> guards)
            {
                public int CountOf() => guards.Count;
            }
        }

        class C
        {
            static readonly Bag<List<Guard>> s_bag = new();

            static void Params(List<Guard> guards) => s_bag.AddRange({|#0:guards|});

            static void ParamsSpan(List<Guard> guards) => s_bag.AddSpan({|#1:guards|});

            static void ParamsOfAMethod(List<Guard> guards) => Puts.PutMany({|#2:guards|});

            static void CompoundOperator(List<Guard> guards) { var bag = s_bag; bag += {|#3:guards|}; }

            static void BinaryOperator(List<Guard> guards) { _ = s_bag + {|#4:guards|}; }

            static void LeftOperand(List<Guard> guards) { _ = {|#5:guards|} | s_bag; }

            static void Conversion(List<Guard> guards) { Handle<List<Guard>> handle = {|#6:guards|}; }

            static void ExtensionBlock(List<Guard> guards) => {|#7:guards|}.Park();

            static int ExtensionProperty(List<Guard> guards) => {|#8:guards|}.Parked;

            // What is not passed to a type parameter is only used: a 'params' collection of numbers, an operator that takes the bag
            // itself, and an extension block over the container's own type.
            static void OnlyUsed(List<Guard> guards, Bag<List<Guard>> bag)
            {
                bag.Count(guards.Count, 2);
                bool isEmpty = !bag;
                int count = guards.CountOf();
            }
        }

        {{Sources.Guard}}
        """,
        Verify.KeepsUnmarkedParameter(0, "C.Params(List<Guard>)", "guards", "System.Collections.Generic.List<Guard>"),
        Verify.KeepsUnmarkedParameter(1, "C.ParamsSpan(List<Guard>)", "guards", "System.Collections.Generic.List<Guard>"),
        Verify.KeepsUnmarkedParameter(2, "C.ParamsOfAMethod(List<Guard>)", "guards", "System.Collections.Generic.List<Guard>"),
        Verify.KeepsUnmarkedParameter(3, "C.CompoundOperator(List<Guard>)", "guards", "System.Collections.Generic.List<Guard>"),
        Verify.KeepsUnmarkedParameter(4, "C.BinaryOperator(List<Guard>)", "guards", "System.Collections.Generic.List<Guard>"),
        Verify.KeepsUnmarkedParameter(5, "C.LeftOperand(List<Guard>)", "guards", "System.Collections.Generic.List<Guard>"),
        Verify.KeepsUnmarkedParameter(6, "C.Conversion(List<Guard>)", "guards", "System.Collections.Generic.List<Guard>"),
        Verify.KeepsUnmarkedParameter(7, "C.ExtensionBlock(List<Guard>)", "guards", "System.Collections.Generic.List<Guard>"),
        Verify.KeepsUnmarkedParameter(8, "C.ExtensionProperty(List<Guard>)", "guards", "System.Collections.Generic.List<Guard>"));

    // A container parameter that is not [Kept] is held to a list of its own. Allowed is what only uses it during the call: a member of
    // it, a reference or a view that stays in the method, a parameter of any declared type that does not keep, a test, a write of
    // the variable. Keeping is a store, a capture and passing it to what keeps. Everything else that takes it as a value is
    // reported as well: a form that nobody named is a finding and not a gap.
    [TestMethod]
    public Task Kept_AContainerParameterIsHeldToAListOfItsOwn() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using System.Collections.Generic;
        using System.Runtime.InteropServices;
        using System.Threading.Tasks;
        using Singulink.CodeAnalysis;

        sealed class Holder<[NonCopyable] T>
            where T : struct
        {
            public int Count;

            public Holder<T>? Next { get; set; }

            public event Action? Changed;

            public T Take() => default;
        }

        sealed class Sum<[NonCopyable] T>
        {
            public static T operator +(T item, Sum<T> sum) => item;
        }

        class C
        {
            static Task<Guard>? s_spare;

            event Func<Guard>? Taken;

            static void Fill(out Task<Guard>? task) => task = null;

            static void Make<[NonCopyable] T>(out T value) => value = default!;

            static void Checks(List<Guard> list, List<Guard> other)
            {
                ArgumentNullException.ThrowIfNull(list);
                bool same = ReferenceEquals(list, other);
                string name = nameof(list);
            }

            static void Views(List<Guard> list, Span<Guard> span, Guard[] array)
            {
                Span<Guard> view = CollectionsMarshal.AsSpan(list);
                ref Guard first = ref span[1];
                Span<Guard> rest = span[1..];
                ReadOnlySpan<Guard> read = array;
                view[0].Dispose();
            }

            static void Members(Holder<Guard> holder, List<List<Guard>> lists)
            {
                holder.Count = 1;
                holder.Next = null;
                holder.Changed += () => { };
                lists.Add(new List<Guard>());
                lists[0].Clear();
            }

            static void Written(ref Task<Guard>? spare, bool flag)
            {
                (flag ? ref spare : ref s_spare) = null;
                Fill(out (flag ? ref spare : ref s_spare));

                // An 'out' argument writes the variable, whatever the parameter would do with a value.
                Make(out spare);
                Make(out (flag ? ref spare : ref s_spare));
            }

            static bool Waits(Task<Guard> task)
            {
                task.Wait();
                return task.IsCompleted;
            }

            static Memory<Guard> Window(Guard[] guards) => guards;

            // A delegate for a method of it holds it, and an event keeps the delegate.
            void ToAnEvent(Holder<Guard> holder) => Taken += {|#0:holder|}.Take;

            // A compound assignment with an operator that is a static method passes the variable to it as its left operand.
            static void LeftOfACompound(List<Guard> guards, Sum<List<Guard>> sum) => {|#3:guards|} += sum;

            // Nothing says what these do with it.
            static void ThroughDynamic(Holder<Guard> holder)
            {
                dynamic late = holder;
                {|#1:late|}.Register();
            }

            static void AsATypedReference(Holder<Guard> holder)
            {
                var reference = __makeref({|#2:holder|});
            }

            // Code that does not compile is the compiler's to report.
            static bool NotCompiled(Holder<Guard> holder) => {|CS0837:holder.Take is null|};
        }

        {{Sources.Guard}}
        """,
        Verify.KeepsUnmarkedParameter(0, "C.ToAnEvent(Holder<Guard>)", "holder", "Holder<Guard>"),
        Verify.KeepsUnmarkedParameter(3, "C.LeftOfACompound(List<Guard>, Sum<List<Guard>>)", "guards", "System.Collections.Generic.List<Guard>"),
        Verify.ParameterUsedInAWayNotFollowed(1, "C.ThroughDynamic(Holder<Guard>)", "holder", "Holder<Guard>"),
        Verify.ParameterUsedInAWayNotFollowed(2, "C.AsATypedReference(Holder<Guard>)", "holder", "Holder<Guard>"));

    // A member that the compiler calls is judged like the member written out: a list pattern, a property pattern and a spread read
    // from their input as an indexer, a property and a 'foreach' do. So a value of a marked type parameter that is constrained to a
    // container, and a new object of a class that derives from a listed container, are never new for them either.
    [TestMethod]
    public Task CompilerCalledMembers_AreJudgedLikeTheMemberWrittenOut() => Verify.AnalyzerAsync(
        $$"""
        using System.Collections.Generic;
        using System.Threading.Tasks;
        using Singulink.CodeAnalysis;

        public sealed class SelfRegistered : List<Guard>
        {
            public static readonly List<SelfRegistered> All = new();

            public SelfRegistered() => All.Add(this);
        }

        static class C
        {
            static SelfRegistered Make() => new SelfRegistered();

            static Guard First<[NonCopyable] TList>(TList guards)
                where TList : IReadOnlyList<Guard> => guards is [var {|#0:first|}, ..] ? first : default;

            static Guard FirstWritten<[NonCopyable] TList>(TList guards)
                where TList : IReadOnlyList<Guard> => {|#1:guards[0]|};

            static Guard[] All<[NonCopyable] TList>(TList guards)
                where TList : IReadOnlyList<Guard> => [{|#2:.. guards|}];

            static Guard ResultOf<[NonCopyable] TTask>(TTask task)
                where TTask : Task<Guard> => task is { {|#3:Result|}: var result } ? result : default;

            static Guard ResultWritten<[NonCopyable] TTask>(TTask task)
                where TTask : Task<Guard> => {|#4:task.Result|};

            static Guard FirstOfANewObject() => Make() is [var {|#5:first|}, ..] ? first : default;

            static Guard[] AllOfANewObject() => [{|#6:.. Make()|}];

            // A new list of the framework is new for its members.
            static Guard FirstOfANewList() => new List<Guard>() is [var first, ..] ? first : default;
        }

        {{Sources.Guard}}
        """,
        Verify.CopyByAssignmentIntoVariable(0, "Guard", "first"),
        Verify.ReadFromStoredContainer(1, "'IReadOnlyList<T>.this[int]'", "Guard"),
        Verify.ReadFromStoredContainer(2, "'..'", "Guard"),
        Verify.ReadFromStoredContainer(3, "'Task<TResult>.Result'", "Guard"),
        Verify.ReadFromStoredContainer(4, "'Task<TResult>.Result'", "Guard"),
        Verify.CopyByAssignmentIntoVariable(5, "Guard", "first"),
        Verify.ReadFromStoredContainer(6, "'..'", "Guard", canBeMoved: false));

    // A handler whose 'AppendFormatted' takes a marked type parameter keeps what a hole gives it, like every other parameter of that
    // kind. The handlers of the framework, whose type parameter is not marked, format what they are given.
    [TestMethod]
    public Task Kept_AHandlerWithAMarkedTypeParameterKeepsWhatAHoleGivesIt() => Verify.AnalyzerAsync(
        $$"""
        using System.Collections.Generic;
        using System.Runtime.CompilerServices;
        using Singulink.CodeAnalysis;

        public static class Slot<[NonCopyable] T>
        {
            public static T? Value;
        }

        [InterpolatedStringHandler]
        public ref struct RecordingHandler
        {
            public RecordingHandler(int literalLength, int formattedCount) { }

            public void AppendLiteral(string text) { }

            public void AppendFormatted<[NonCopyable] T>(T value) => Slot<T>.Value = value;
        }

        static class C
        {
            static void Trace(RecordingHandler handler) { }

            static void Log(List<Guard> guards) => Trace($"guards: { {|#0:guards|} }");

            static string Format(List<Guard> guards) => $"guards: {guards.Count}";
        }

        {{Sources.Guard}}
        """,
        Verify.KeepsUnmarkedParameter(0, "C.Log(List<Guard>)", "guards", "System.Collections.Generic.List<Guard>"));

    // Two rules that compare a method with what stands for it line the parameters up. A method group of an extension method that is
    // called on a receiver has one parameter more than the delegate. And a member that a class inherits can implement an interface
    // member of the class: a parameter that keeps, against one that does not, is reported like an added [Kept].
    [TestMethod]
    public Task Kept_ParametersAreLinedUpWithWhatTheyStandBehind() => Verify.AnalyzerAsync(
        $$"""
        using System.Collections.Generic;
        using Singulink.CodeAnalysis;

        public delegate void Sink(List<Guard> guards);

        public delegate void Mover([MovedFrom] ref Guard guard);

        public delegate void Toucher(ref Guard guard);

        public delegate List<Guard> Maker();

        public sealed class Holder
        {
            public List<Guard>? Kept;
        }

        public class Registry<[NonCopyable] T>
        {
            private readonly T[] _items = new T[16];
            private int _count;

            public void Add(T item) => _items[_count++] = item;
        }

        public static class Extensions
        {
            public static void Keep(this Holder holder, [Kept] List<Guard> guards) => holder.Kept = guards;

            public static void AddTo<[NonCopyable] T>(this Registry<T> registry, T item) => registry.Add(item);

            public static void Count(this Holder holder, List<Guard> guards) { }

            public static void Take(this Holder holder, [MovedFrom] ref Guard guard) => guard = default;

            public static void Touch(this Holder holder, ref Guard guard) { }

            [return: Borrowed]
            public static List<Guard> Borrow(this Holder holder) => holder.Kept!;
        }

        public interface IGuardsSink
        {
            void Add(List<Guard> guards);
        }

        public sealed class {|#5:GuardsRegistry|} : Registry<List<Guard>>, IGuardsSink;

        public interface IKeepingSink
        {
            void Add([Kept] List<Guard> guards);
        }

        public sealed class KeepingRegistry : Registry<List<Guard>>, IKeepingSink;

        static class C
        {
            static readonly Holder s_holder = new();
            static readonly Registry<List<Guard>> s_registry = new();

            static readonly Sink s_keep = {|#0:s_holder.Keep|};
            static readonly Sink s_add = {|#1:s_registry.AddTo|};
            static readonly Sink s_count = s_holder.Count;

            // [MovedFrom] is compared for the same parameters, and [Borrowed] is on the return value.
            static readonly Toucher s_take = {|#2:s_holder.Take|};
            static readonly Mover s_touch = {|#3:s_holder.Touch|};
            static readonly Mover s_move = s_holder.Take;
            static readonly Maker s_borrow = {|#4:s_holder.Borrow|};
        }

        {{Sources.Guard}}
        """,
        Verify.KeepingNotOnInterfaceMember(5, "item", "Registry<T>.Add(T)", "IGuardsSink.Add(List<Guard>)", "GuardsRegistry"),
        Verify.KeptNotOnDelegate(0, "guards", "Extensions.Keep(Holder, List<Guard>)", "Sink"),
        Verify.KeepingNotOnDelegate(1, "item", "Extensions.AddTo<List<Guard>>(Registry<List<Guard>>, List<Guard>)", "Sink"),
        Verify.MovedFromAdded(2, "guard", "Extensions.Take(Holder, ref Guard)", "delegate 'Toucher', which it is converted to,"),
        Verify.MovedFromDropped(3, "guard", "Extensions.Touch(Holder, ref Guard)", "delegate 'Mover', which it is converted to,"),
        Verify.BorrowedNotOnDelegate(4, "Extensions.Borrow(Holder)", "Maker"));

    // A type parameter that is constrained to a class is that class for every rule that looks at the type of a value: 'with' on a type
    // parameter that is constrained to a record runs the copy constructor of the record. And a user-defined increment whose operand
    // keeps is passing the variable to it.
    [TestMethod]
    public Task TypeParameterConstrainedToAClass_AndAnIncrementThatKeeps() => Verify.AnalyzerAsync(
        $$"""
        using System.Collections.Generic;
        using Singulink.CodeAnalysis;

        public record class Batch<[NonCopyable] T>
            where T : struct
        {
            public T[] Items = new T[1];

            public Batch() { }

            [RequiresCopyable]
            protected Batch(Batch<T> other)
            {
                Items = new T[1];
                Items[0] = other.Items[0];
            }
        }

        public record class Named<[NonCopyable] T>(string Name) : Batch<T>
            where T : struct;

        public static class Ops
        {
            public static readonly List<List<Guard>> Retired = new();

            extension(List<Guard>)
            {
                public static List<Guard> operator ++([Kept] List<Guard> guards)
                {
                    Retired.Add(guards);
                    return new List<Guard>();
                }

                public static List<Guard> operator --(List<Guard> guards) => new List<Guard>();
            }
        }

        static class C
        {
            static TBatch Duplicate<[NonCopyable] TBatch, [NonCopyable] T>(TBatch batch)
                where TBatch : Batch<T>
                where T : struct => (TBatch)({|#0:batch with { }|});

            static TNamed DuplicateDerived<[NonCopyable] TNamed>(TNamed named)
                where TNamed : Named<Guard> => (TNamed)({|#1:named with { }|});

            static void Rotate(List<Guard> guards) { {|#2:guards|}++; }

            static void Reset(List<Guard> guards) { guards--; }
        }

        {{Sources.Guard}}
        """,
        Verify.RequiresCopyableMember(0, "Batch<T>.Batch(Batch<T>)", "T", "T"),
        Verify.RequiresCopyableMember(1, "Batch<T>.Batch(Batch<T>)", "Guard", "T"),
        Verify.KeepsUnmarkedParameter(2, "C.Rotate(List<Guard>)", "guards", "System.Collections.Generic.List<Guard>"));

    // An element of a parameter that holds containers is a container that the caller put in, by whatever form it is read: an index
    // from the end, and the variables that a 'foreach' takes each element apart into.
    [TestMethod]
    public Task Kept_AnElementOfAParameterThatHoldsContainersIsFollowedInEveryForm() => Verify.AnalyzerAsync(
        $$"""
        using System.Collections.Generic;
        using Singulink.CodeAnalysis;

        class C
        {
            static List<Guard>? s_kept;

            static void FromTheEnd(List<List<Guard>> lists) => s_kept = {|#0:lists|}[^1];

            static void FromTheStart(List<List<Guard>> lists) => s_kept = {|#1:lists|}[0];

            static void TakenApart(List<(List<Guard> Guards, int Id)> pairs)
            {
                foreach (var (guards, id) in pairs)
                    s_kept = {|#2:guards|};
            }

            static int OnlyUsed(List<List<Guard>> lists, List<(List<Guard> Guards, int Id)> pairs)
            {
                int count = lists[^1].Count;

                foreach (var (guards, id) in pairs)
                    count += guards.Count + id;

                return count;
            }
        }

        {{Sources.Guard}}
        """,
        Verify.KeepsUnmarkedParameter(0, "C.FromTheEnd(List<List<Guard>>)", "lists", "System.Collections.Generic.List<System.Collections.Generic.List<Guard>>"),
        Verify.KeepsUnmarkedParameter(1, "C.FromTheStart(List<List<Guard>>)", "lists", "System.Collections.Generic.List<System.Collections.Generic.List<Guard>>"),
        Verify.KeepsUnmarkedParameter(2, "C.TakenApart(List<(List<Guard> Guards, int Id)>)", "pairs", "System.Collections.Generic.List<(System.Collections.Generic.List<Guard> Guards, int Id)>"));

    // What a use makes of a container that is the container under another name is judged for where it goes: a view that is no ref
    // struct, as a Memory<T> and an ArraySegment<T> are, the container as another type, and a reference to the variable that a
    // callee hands back. Stored, it is the container that is stored. A view or a reference that stays in the method is allowed.
    [TestMethod]
    public Task Kept_WhatIsMadeOfAParameterIsTheParameterUnderAnotherName() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using System.Collections.Generic;
        using System.Runtime.InteropServices;
        using Singulink.CodeAnalysis;

        sealed class Box
        {
            public Memory<Guard> Held;
        }

        struct Slots
        {
            public Memory<Guard> Held;
        }

        class C
        {
            static Memory<Guard> s_pending;
            static ArraySegment<Guard> s_segment;
            static Guard[]? s_array;
            static List<Guard>? s_kept;
            static Slots s_slots;

            static ref List<Guard> Pick(ref List<Guard> guards) => ref guards;

            static ref readonly List<Guard> PickIn(in List<Guard> guards) => ref guards;

            static void ByAConversion(Guard[] guards) => s_pending = {|#0:guards|};

            static void ByAMember(Guard[] guards) => s_pending = {|#1:guards|}.AsMemory(0, 1);

            static void AsASegment(Guard[] guards) => s_segment = {|#2:guards|};

            static void ThroughALocal(Guard[] guards)
            {
                Memory<Guard> view = guards;
                s_pending = {|#3:view|}.Slice(0, 1);
            }

            static Box InAnInitializer(Guard[] guards) => new Box { Held = {|#4:guards|} };

            static void InAFieldOfAStruct(Guard[] guards) => s_slots.Held = {|#5:guards|};

            static void OutOfASegment(Guard[] guards)
            {
                ArraySegment<Guard> segment = guards;
                s_array = {|#6:segment|}.Array;
            }

            static void OutOfAMemory(Memory<Guard> memory)
            {
                if (MemoryMarshal.TryGetArray<Guard>(memory, out var segment))
                    s_segment = {|#7:segment|};
            }

            static void ByAReferenceHandedBack(List<Guard> guards) => s_kept = Pick(ref {|#8:guards|});

            static void ByAReadOnlyReferenceHandedBack(List<Guard> guards) => s_kept = PickIn(in {|#9:guards|});

            // A view and a reference that stay in the method are allowed, and so is a member that is used on what is made.
            static void StaysInTheMethod(Guard[] guards, List<Guard> list)
            {
                Memory<Guard> view = guards.AsMemory();
                view.Span[0].Dispose();

                ArraySegment<Guard> segment = guards;
                int count = segment.Count + guards.AsMemory(1).Length;

                Span<Guard> span = guards.AsSpan();
                span[0].Dispose();

                Pick(ref list).Clear();
            }
        }

        {{Sources.Guard}}
        """,
        Verify.KeepsUnmarkedParameter(0, "C.ByAConversion(Guard[])", "guards", "Guard[]"),
        Verify.KeepsUnmarkedParameter(1, "C.ByAMember(Guard[])", "guards", "Guard[]"),
        Verify.KeepsUnmarkedParameter(2, "C.AsASegment(Guard[])", "guards", "Guard[]"),
        Verify.KeepsUnmarkedParameter(3, "C.ThroughALocal(Guard[])", "guards", "Guard[]"),
        Verify.KeepsUnmarkedParameter(4, "C.InAnInitializer(Guard[])", "guards", "Guard[]"),
        Verify.KeepsUnmarkedParameter(5, "C.InAFieldOfAStruct(Guard[])", "guards", "Guard[]"),
        Verify.KeepsUnmarkedParameter(6, "C.OutOfASegment(Guard[])", "guards", "Guard[]"),
        Verify.KeepsUnmarkedParameter(7, "C.OutOfAMemory(Memory<Guard>)", "memory", "System.Memory<Guard>"),
        Verify.KeepsUnmarkedParameter(8, "C.ByAReferenceHandedBack(List<Guard>)", "guards", "System.Collections.Generic.List<Guard>"),
        Verify.KeepsUnmarkedParameter(9, "C.ByAReadOnlyReferenceHandedBack(List<Guard>)", "guards", "System.Collections.Generic.List<Guard>"));

    // What receives a container says whether it keeps, for a parameter as for a local. A parameter whose declared type mentions a
    // type parameter that is not marked keeps: the code behind it is not checked, and 'ArrayPool<T>.Return(T[])' is such a one. Where
    // the call hands back a reference, a ref struct or the container under another name, that is what it is for.
    [TestMethod]
    public Task Kept_AParameterWhoseTypeMentionsAnUnmarkedTypeParameterKeeps() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using System.Buffers;
        using System.Collections.Generic;
        using System.Runtime.InteropServices;
        using Singulink.CodeAnalysis;

        class C
        {
            static void Recycle(Guard[] buffer) => ArrayPool<Guard>.Shared.Return({|#0:buffer|});

            static void RecycleAndClear(Guard[] buffer) => ArrayPool<Guard>.Shared.Return({|#1:buffer|}, clearArray: true);

            // The same parameter ends a local being the method's own.
            static Guard[] Make(RwLock rwLock)
            {
                var buffer = new Guard[16];
                buffer[0] = rwLock.Enter();
                ArrayPool<Guard>.Shared.Return(buffer);
                return {|#2:buffer|};
            }

            // A call that hands back a ref struct or a reference is for that.
            static void Views(Guard[] guards, List<Guard> list)
            {
                Span<Guard> span = guards.AsSpan();
                Span<Guard> ofList = CollectionsMarshal.AsSpan(list);
                ref Guard first = ref MemoryMarshal.GetArrayDataReference(guards);
                first.Dispose();
            }

            // No declaration tells a member that only uses what it is given from one that keeps it, so the list says which
            // do: its entry is marked 'borrows'. Such a member keeps nothing, for a parameter and for a local.
            static void Turn(Guard[] guards) => Array.Reverse(guards);

            static bool AreTheSame(ArraySegment<Guard> first, ArraySegment<Guard> second, Guard[] guards) =>
                first == second && first.Equals(second) && guards.AsMemory().Equals(first.AsMemory());

            static Guard[] MakeAndTurn(RwLock rwLock)
            {
                var guards = new Guard[2];
                guards[0] = rwLock.Enter();
                Array.Reverse(guards);
                return guards;
            }

            // Where nothing that carries a non-copyable type is bound to the type parameter, the member is checked code for the use.
            static int CountOf(Dictionary<int, Guard> map) => Lookups.CountIn(map);

            // What a member that is listed as 'new' hands out is taken out of the container for good, or created: it is no other
            // name for the container, so keeping it does not keep the pool.
            static void Rent(ArrayPool<Guard> pool)
            {
                s_buffer = pool.Rent(16);

                var rented = pool.Rent(16);
                pool.Return(rented);
            }

            static Guard[]? s_buffer;

            // The receiver of an extension block is such a parameter as well.
            static List<Guard> MakeAndStash(RwLock rwLock)
            {
                var list = new List<Guard>();
                list.Add(rwLock.Enter());
                {|#4:list.Stash()|};
                return {|#5:list|};
            }
        }

        static class Stashing
        {
            extension<T>(List<T> list)
            {
                public void Stash() { }
            }
        }

        static class Lookups
        {
            public static int CountIn<TKey>(Dictionary<TKey, Guard> map)
                where TKey : notnull => map.Count;
        }

        {{Sources.Guard}}
        """,
        Verify.PassesParameterToUncheckedCode(0, "C.Recycle(Guard[])", "buffer", "Guard[]", "ArrayPool<Guard>.Return(Guard[], bool)", ReturnOfThePool),
        Verify.PassesParameterToUncheckedCode(1, "C.RecycleAndClear(Guard[])", "buffer", "Guard[]", "ArrayPool<Guard>.Return(Guard[], bool)", ReturnOfThePool),
        Verify.UnownedContainerReturned(
            2,
            "C.Make(RwLock)",
            "Guard[]",
            "'buffer' is not the method's own anywhere in the method, because on line 18 it is passed to 'ArrayPool<Guard>.Return(Guard[], bool)', which is taken to keep it: the type of its parameter mentions a type parameter that is not marked"),
        Verify.UnlistedMember(4, "Stashing.extension<T>(List<T>).Stash()", "Guard", "T", "M:Stashing.Stash``1(System.Collections.Generic.List{``0})"),
        Verify.UnownedContainerReturned(5, "C.MakeAndStash(RwLock)", "System.Collections.Generic.List<Guard>", "'list' is not the method's own anywhere in the method, because on line 66 it is passed to 'Stashing.extension<Guard>(List<Guard>).Stash()', which is taken to keep it: the type of its parameter mentions a type parameter that is not marked"));

    // For a local the same holds as for a parameter: what is made of it that is its container under another name is used where it
    // is made, or the local is no longer the method's own.
    [TestMethod]
    public Task OwnLocal_WhatIsMadeOfItIsTheLocalUnderAnotherName() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using System.Collections.Generic;
        using System.Collections.ObjectModel;
        using System.Threading;
        using System.Threading.Tasks;
        using Singulink.CodeAnalysis;

        class C
        {
            static Task<Guard>? s_waiting;

            static Task<Guard> KeptAsAnotherTask(RwLock rwLock)
            {
                var task = rwLock.EnterTaskAsync();
                s_waiting = task.WaitAsync(CancellationToken.None);
                return {|#0:task|};
            }

            static async Task<Guard> HeldInALocal(RwLock rwLock)
            {
                var task = rwLock.EnterTaskAsync();
                var waiting = task.WaitAsync(CancellationToken.None);
                Guard first = {|#1:await task|};
                return {|#2:await waiting|};
            }

            // Used where it is made, nothing of it is left.
            static async Task<Guard> OnTheSpot(RwLock rwLock)
            {
                var task = rwLock.EnterTaskAsync();
                bool done = task.WaitAsync(CancellationToken.None).IsCompleted;
                return await task;
            }

            // At the last use of the local, what is made takes its place and is as new as the local is.
            static Task<Guard> Returned(RwLock rwLock)
            {
                var task = rwLock.EnterTaskAsync();
                return task.WaitAsync(CancellationToken.None);
            }

            static async Task<Guard> HeldAtTheLastUse(RwLock rwLock)
            {
                var task = rwLock.EnterTaskAsync();
                var waiting = task.WaitAsync(CancellationToken.None);
                return await waiting;
            }

            static void StoredAtTheLastUse(RwLock rwLock)
            {
                var task = rwLock.EnterTaskAsync();
                s_waiting = task.WaitAsync(CancellationToken.None);
            }

            static Task<Guard> ReturnedOfAStoredOne(RwLock rwLock)
            {
                var task = rwLock.EnterTaskAsync();
                s_waiting = task;
                return {|#3:task.WaitAsync(CancellationToken.None)|};
            }

            static async Task<Guard> Awaited(RwLock rwLock)
            {
                var task = rwLock.EnterTaskAsync();
                return await task.WaitAsync(CancellationToken.None);
            }
        }

        {{Sources.Guard}}
        """,
        Verify.UnownedContainerReturned(0, "C.KeptAsAnotherTask(RwLock)", "System.Threading.Tasks.Task<Guard>", WaitAsyncMakesIt(15)),
        Verify.ReadFromStoredContainer(1, "'await'", "Guard", WaitAsyncMakesIt(22)),
        Verify.ReadFromStoredContainer(2, "'await'", "Guard"),
        Verify.UnownedContainerReturned(3, "C.ReturnedOfAStoredOne(RwLock)", "System.Threading.Tasks.Task<Guard>"));

    private const string ReturnOfThePool = "M:System.Buffers.ArrayPool`1.Return(`0[],System.Boolean)";

    private static string WaitAsyncMakesIt(int line) =>
        $"'task' is not the method's own anywhere in the method, because on line {line} 'Task<Guard>.WaitAsync(CancellationToken)' can keep it, hand out " +
        "a view of it or take in what something else holds";

    // A method group keeps what its method is taken to keep, by the question that every call is asked: a member whose parameter
    // mentions a type parameter that is not marked keeps, one that the list marks 'borrows' keeps nothing, and one that hands the
    // container back is followed where it is called.
    [TestMethod]
    public Task Kept_AMethodGroupKeepsWhatItsMethodIsTakenToKeep() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using System.Buffers;
        using Singulink.CodeAnalysis;

        public delegate void Recycler(Guard[] buffer, bool clear);

        public delegate void Turner(Guard[] guards);

        public delegate Memory<Guard> Viewer(Guard[] guards);

        public delegate void KeepingRecycler([Kept] Guard[] buffer, bool clear);

        public delegate void Pooler<T>(T[] buffer, bool clear);

        static class C
        {
            static readonly Recycler s_recycle = {|#0:ArrayPool<Guard>.Shared.Return|};

            static readonly KeepingRecycler s_keepingRecycle = ArrayPool<Guard>.Shared.Return;

            static readonly Turner s_turn = Array.Reverse;

            // What hands the array back is no keeping: what it hands back is judged, here by the rule for what a delegate returns.
        static readonly Viewer s_view = {|#1:MemoryExtensions.AsMemory|};

            // The parameter of 'Action<T1, T2>' is a type parameter, which is taken to keep what it is given as well.
            static readonly Action<Guard[], bool> s_action = ArrayPool<Guard>.Shared.Return;

            // A delegate has no code of its own that could keep: a parameter of one that only mentions a type parameter is
            // taken to use what it is given, so a method that keeps is reported for it as well.
            static readonly Pooler<Guard> s_pooler = {|#2:ArrayPool<Guard>.Shared.Return|};
        }

        {{Sources.Guard}}
        """,
        Verify.KeepingNotOnDelegate(0, "array", "ArrayPool<Guard>.Return(Guard[], bool)", "Recycler", byUncheckedCode: true),
        Verify.DelegateHandsOutStoredContainer(1, "MemoryExtensions.AsMemory<Guard>(Guard[]?)", "System.Memory<Guard>"),
        Verify.KeepingNotOnDelegate(2, "array", "ArrayPool<Guard>.Return(Guard[], bool)", "Pooler<Guard>", byUncheckedCode: true));

    private const string ListOfGuards = "System.Collections.Generic.List<Guard>";

    // Every member that an operation binds is a member that is used. Some kinds of operation run code of the project with the
    // container as its receiver or argument, where nothing but what the compiler bound shows it. Each such member is collected in one
    // place, and a container that it takes as a parameter is judged as passed to it, for a parameter and for a local.
    [TestMethod]
    public Task BoundMembers_AreJudgedAsThePassingThatTheyAre() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using System.Collections.Generic;
        using System.Runtime.CompilerServices;
        using System.Threading;
        using System.Threading.Tasks;
        using Singulink.CodeAnalysis;

        public static class Keeping
        {
            public static List<Guard>? Kept;

            public static void Register([Kept] this List<Guard> guards) => Kept = guards;

            public static void Deconstruct([Kept] this List<Guard> guards, out int count, out int capacity)
            {
                Kept = guards;
                count = guards.Count;
                capacity = guards.Capacity;
            }

            public static TaskAwaiter<int> GetAwaiter([Kept] this List<Guard> guards)
            {
                Kept = guards;
                return Task.FromResult(guards.Count).GetAwaiter();
            }

            public static async IAsyncEnumerator<int> GetAsyncEnumerator([Kept] this List<Guard> guards, CancellationToken cancellationToken = default)
            {
                Kept = guards;
                await Task.Yield();
                yield return 1;
            }

            public static IEnumerator<int> GetEnumerator([Kept] this Registry<Guard> registry)
            {
                Registry<Guard>.Last = registry;
                yield return 1;
            }

            extension([Kept] List<Guard> guards)
            {
                public bool IsRegistered
                {
                    get
                    {
                        Kept = guards;
                        return true;
                    }
                }

                public void Enlist() => Kept = guards;
            }
        }

        public sealed class Registry<[NonCopyable] T>
        {
            public static Registry<T>? Last;
        }

        // A type whose '&&' is user-defined: the left side goes to 'operator false' first.
        public sealed class Batch<[NonCopyable] T>
        {
            public static Batch<T>? Last;

            public static Batch<T> operator &(Batch<T> left, Batch<T> right) => new Batch<T>();

            public static bool operator true(Batch<T> batch) => true;

            public static bool operator false([Kept] Batch<T> batch)
            {
                Last = batch;
                return false;
            }
        }

        [InterpolatedStringHandler]
        public ref struct TargetHandler
        {
            public static List<Guard>? LastTarget;

            public TargetHandler(int literalLength, int formattedCount, List<Guard> target) => LastTarget = target;

            public void AppendLiteral(string text) { }

            public void AppendFormatted(int value) { }
        }

        public static class Log
        {
            public static void Write(List<Guard> target, [InterpolatedStringHandlerArgument("target")] TargetHandler message) { }

            public static void Plain(List<Guard> target, string message) { }
        }

        static class C
        {
            static Memory<Guard>? s_pending;
            static Task<Guard>? s_task;

            // The call behind '?.'.
            static void ToAKeptReceiver(List<Guard> guards) => {|#0:guards|}?.Register();

            static void ToAKeptReceiverOfABlock(List<Guard> guards) => {|#1:guards|}?.Enlist();

            static void ToWhatHandsItBack(Guard[] buffer) => s_pending = {|#2:buffer|}?.AsMemory();

            static void ToAnotherTask(Task<Guard> task) => s_task = {|#3:task|}?.WaitAsync(CancellationToken.None);

            // Deconstruct, of a deconstruction and of a positional pattern.
            static int TakenApart(List<Guard> guards)
            {
                var (count, _) = {|#4:guards|};
                return count;
            }

            static bool ByAPositionalPattern(List<Guard> guards) => {|#5:guards|} is (1, _);

            // GetAwaiter, the member of a property pattern, GetAsyncEnumerator and GetEnumerator, also of a spread.
            static async Task<int> Awaited(List<Guard> guards) => await {|#6:guards|};

            static bool ByAPropertyPattern(List<Guard> guards) => {|#7:guards|} is { IsRegistered: true };

            static async Task Enumerated(List<Guard> guards)
            {
                await foreach (int count in {|#8:guards|})
                {
                }
            }

            static int[] Spread(Registry<Guard> registry) => [.. {|#9:registry|}];

            // 'operator false' of a user-defined '&&'.
            static Batch<Guard> ShortCircuit(Batch<Guard> first, Batch<Guard> second) => {|#10:first|} && second;

            // The constructor of a handler that the call passes its argument on to.
            static void ToAHandler(List<Guard> guards) => Log.Write({|#11:guards|}, $"count {guards.Count}");

            // The same members with nothing that keeps: members of the type itself, and a call with a plain string.
            static async Task<int> WithMembersOfItsOwn(List<Guard> guards, Task<Guard> task, Guard[] buffer)
            {
                int count = (guards?.Count ?? 0) + (buffer?.Length ?? 0);
                bool any = guards is { Count: > 0 } && task is { IsCompleted: true };
                Log.Plain(guards!, $"count {count}");

                foreach (ref Guard guard in buffer.AsSpan())
                    guard.Dispose();

                await Task.Yield();
                return count;
            }

            // On a local the same members end it being the method's own.
            static async Task<List<Guard>> LocalAwaited(RwLock rwLock)
            {
                var guards = new List<Guard>();
                guards.Add(rwLock.Enter());
                _ = await guards;
                return {|#12:guards|};
            }

            static List<Guard> LocalInAPropertyPattern(RwLock rwLock)
            {
                var guards = new List<Guard>();
                guards.Add(rwLock.Enter());
                _ = guards is { IsRegistered: true };
                return {|#13:guards|};
            }

            static List<Guard> LocalToAHandler(RwLock rwLock)
            {
                var guards = new List<Guard>();
                guards.Add(rwLock.Enter());
                Log.Write(guards, $"count {1}");
                return {|#14:guards|};
            }

            static Task<Guard> LocalToAnotherTask(RwLock rwLock)
            {
                var task = rwLock.EnterTaskAsync();
                s_task = task?.WaitAsync(CancellationToken.None);
                return {|#15:task|};
            }

            static List<Guard> LocalWithMembersOfItsOwn(RwLock rwLock)
            {
                var guards = new List<Guard>();
                guards.Add(rwLock.Enter());
                int count = guards?.Count ?? 0;
                bool any = guards is { Count: > 0 };
                return guards!;
            }
        }

        {{Sources.Guard}}
        """,
        Verify.KeepsUnmarkedParameter(0, "C.ToAKeptReceiver(List<Guard>)", "guards", ListOfGuards),
        Verify.KeepsUnmarkedParameter(1, "C.ToAKeptReceiverOfABlock(List<Guard>)", "guards", ListOfGuards),
        Verify.KeepsUnmarkedParameter(2, "C.ToWhatHandsItBack(Guard[])", "buffer", "Guard[]"),
        Verify.KeepsUnmarkedParameter(3, "C.ToAnotherTask(Task<Guard>)", "task", "System.Threading.Tasks.Task<Guard>"),
        Verify.KeepsUnmarkedParameter(4, "C.TakenApart(List<Guard>)", "guards", ListOfGuards),
        Verify.KeepsUnmarkedParameter(5, "C.ByAPositionalPattern(List<Guard>)", "guards", ListOfGuards),
        Verify.KeepsUnmarkedParameter(6, "C.Awaited(List<Guard>)", "guards", ListOfGuards),
        Verify.KeepsUnmarkedParameter(7, "C.ByAPropertyPattern(List<Guard>)", "guards", ListOfGuards),
        Verify.KeepsUnmarkedParameter(8, "C.Enumerated(List<Guard>)", "guards", ListOfGuards),
        Verify.KeepsUnmarkedParameter(9, "C.Spread(Registry<Guard>)", "registry", "Registry<Guard>"),
        Verify.KeepsUnmarkedParameter(10, "C.ShortCircuit(Batch<Guard>, Batch<Guard>)", "first", "Batch<Guard>"),
        Verify.KeepsUnmarkedParameter(11, "C.ToAHandler(List<Guard>)", "guards", ListOfGuards),
        Verify.UnownedContainerReturned(12, "C.LocalAwaited(RwLock)", ListOfGuards, "'guards' is not the method's own anywhere in the method, because on line 157 it is passed to 'Keeping.GetAwaiter(List<Guard>)', which keeps it"),
        Verify.UnownedContainerReturned(13, "C.LocalInAPropertyPattern(RwLock)", ListOfGuards, "'guards' is not the method's own anywhere in the method, because on line 165 it is passed to 'Keeping.extension(List<Guard>).IsRegistered', which keeps it"),
        Verify.UnownedContainerReturned(14, "C.LocalToAHandler(RwLock)", ListOfGuards, "'guards' is not the method's own anywhere in the method, because on line 173 it is passed to 'TargetHandler.TargetHandler(int, int, List<Guard>)', which keeps it"),
        Verify.UnownedContainerReturned(15, "C.LocalToAnotherTask(RwLock)", "System.Threading.Tasks.Task<Guard>", "'task' is not the method's own anywhere in the method, because on line 180 'Task<Guard>.WaitAsync(CancellationToken)' can keep it, hand out a view of it or take in what something else holds"));

    // A view that checked code hands back is the container under another name: the return of a Memory<T> or a ReadOnlyMemory<T> is
    // not checked, and that of a getter or an indexer is not either. The parameter of an indexer keeps where the indexer is assigned,
    // like the parameter of a setter. A constructor of a ref struct that the list vouches for is a call that returns one. And an
    // element of a parameter that holds containers is followed out of a struct of the list as out of a tuple.
    [TestMethod]
    public Task Kept_WhatCheckedCodeHandsBackAndWhatAnIndexerTakes() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using System.Collections.Generic;
        using Singulink.CodeAnalysis;

        public delegate Memory<Guard> Viewer(Guard[] buffer);

        public sealed class Map
        {
            public static List<Guard>? LastKey;

            public int this[List<Guard> key]
            {
                get => 0;
                set => LastKey = key;
            }
        }

        public sealed class Interner
        {
            public List<Guard> this[List<Guard> key] => key;

            public int CountOf(List<Guard> key) => key.Count;
        }

        public sealed class Node<[NonCopyable] T>
        {
            public Node<T>? Next { get; set; }
        }

        static class C
        {
            static Node<Guard>? s_head;
            static Memory<Guard> s_pending;
            static ReadOnlyMemory<Guard> s_readOnly;
            static List<Guard>? s_canonical;
            static List<Guard>? s_kept;
            static readonly Map s_map = new();
            static readonly Interner s_interner = new();

            static Memory<Guard> Window(Guard[] buffer, int count) => buffer.AsMemory(0, count);

            static ReadOnlyMemory<Guard> View(Guard[] buffer) => buffer;

            // What checked code hands back.
            static void ByAMethod(Guard[] buffer) => s_pending = Window({|#0:buffer|}, 1);

            static void AsReadOnly(Guard[] buffer) => s_readOnly = View({|#1:buffer|});

            static void ByALocalFunction(Guard[] buffer)
            {
                s_pending = Local({|#2:buffer|});

                static Memory<Guard> Local(Guard[] b) => b;
            }

            static void ByADelegate(Guard[] buffer, Viewer viewer) => s_pending = viewer({|#3:buffer|});

            static void ByAGetter(List<Guard> guards) => s_canonical = s_interner[{|#4:guards|}];

            // The parameter of an indexer that is assigned.
            static void IndexerAssigned(List<Guard> guards) => s_map[{|#5:guards|}] = 1;

            static Map IndexInAnInitializer(List<Guard> guards) => new Map { [{|#6:guards|}] = 1 };

            static void IndexerAddedTo(List<Guard> guards) => s_map[{|#7:guards|}] += 1;

            // An element of a parameter that holds a container, out of a struct of the list.
            static void OutOfAPair(KeyValuePair<string, List<Guard>> entry) => s_kept = {|#8:entry|}.Value;

            // What stays in the method, or only reads, keeps nothing: a view that is used on the spot, an indexer that is read, and
            // a constructor of a ref struct of the framework, which is a call that returns one.
            static int OnlyUsed(Guard[] buffer, List<Guard> guards, KeyValuePair<string, List<Guard>> entry)
            {
                int length = Window(buffer, 1).Length + s_map[guards] + s_interner[guards].Count + s_interner.CountOf(guards) + entry.Value.Count;
                var span = new Span<Guard>(buffer);
                var readOnly = new ReadOnlySpan<Guard>(buffer, 0, 1);
                span[0].Dispose();
                return length + readOnly.Length;
            }

            // A property of the container's own that hands out a value of its type is a member that is used on it: another node.
            static void Unlink(Node<Guard> node)
            {
                s_head = node.Next;
                node.Next = null;
            }
        }

        {{Sources.Guard}}
        """,
        Verify.KeepsUnmarkedParameter(0, "C.ByAMethod(Guard[])", "buffer", "Guard[]"),
        Verify.KeepsUnmarkedParameter(1, "C.AsReadOnly(Guard[])", "buffer", "Guard[]"),
        Verify.KeepsUnmarkedParameter(2, "C.ByALocalFunction(Guard[])", "buffer", "Guard[]"),
        Verify.KeepsUnmarkedParameter(3, "C.ByADelegate(Guard[], Viewer)", "buffer", "Guard[]"),
        Verify.KeepsUnmarkedParameter(4, "C.ByAGetter(List<Guard>)", "guards", ListOfGuards),
        Verify.KeepsUnmarkedParameter(5, "C.IndexerAssigned(List<Guard>)", "guards", ListOfGuards),
        Verify.KeepsUnmarkedParameter(6, "C.IndexInAnInitializer(List<Guard>)", "guards", ListOfGuards),
        Verify.KeepsUnmarkedParameter(7, "C.IndexerAddedTo(List<Guard>)", "guards", ListOfGuards),
        Verify.KeepsUnmarkedParameter(8, "C.OutOfAPair(KeyValuePair<string, List<Guard>>)", "entry", "System.Collections.Generic.KeyValuePair<string, System.Collections.Generic.List<Guard>>"));
}
