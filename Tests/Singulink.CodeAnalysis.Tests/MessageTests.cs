using PrefixClassName.MsTest;

namespace Singulink.CodeAnalysis.Tests;

/// <summary>
/// Tests of what the messages say about why: what made a local not the method's own, what makes a class a container, and the wording for
/// a constructor and for the result of a lambda.
/// </summary>
/// <remarks>
/// Docs: articles/diagnostics.
/// </remarks>
[PrefixTestClass]
public class MessageTests
{
    [TestMethod]
    public Task Message_SaysWhatMadeALocalNotTheMethodsOwn() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using System.Collections.Generic;
        using System.Threading.Tasks;
        using Singulink.CodeAnalysis;

        class C
        {
            private static List<Guard>? _all;

            // A local that a lambda uses is stored, and so is one that a lambda assigns.
            Guard Captured(RwLock rwLock)
            {
                var list = rwLock.CreateGuards();
                Action clear = () => list.Clear();
                return {|#0:list[0]|};
            }

            Guard Assigned(RwLock rwLock)
            {
                var list = rwLock.CreateGuards();
                Action renew = () => list = new List<Guard>();
                return {|#1:list[0]|};
            }

            // What keeps the container is named, and the advice is to hand it to nothing that keeps it.
            List<Guard> Registered()
            {
                var list = new List<Guard>();
                Register(list);
                return {|#2:list|};
            }

            Guard ReadAfterRegistering(RwLock rwLock)
            {
                var list = rwLock.CreateGuards();
                Register(list);
                return {|#3:list[0]|};
            }

            List<Guard> MovedAfterRegistering(RwLock rwLock)
            {
                var list = rwLock.CreateGuards();
                Register(list);
                return {|#5:NonCopyable.Move(ref {|#4:list|})|};
            }

            // A local that was given what the method did not create has nothing to hand over: the advice is to return a new one.
            List<Guard> GivenAStoredOne()
            {
                var list = new List<Guard>();
                list = _all!;
                return {|#6:list|};
            }

            // The mark that the message offers fits here too: with it the callers treat the result as stored.
            [return: Borrowed]
            List<Guard> RegisteredAndBorrowed()
            {
                var list = new List<Guard>();
                Register(list);
                return list;
            }

            // A conversion and an operator are named like a method, with what the mark on their parameter says.
            List<Guard> Converted()
            {
                var list = new List<Guard>();
                Wrapper wrapper = list;
                return {|#7:list|};
            }

            List<Guard> Added(Wrapper wrapper)
            {
                var list = new List<Guard>();
                _ = wrapper + list;
                return {|#8:list|};
            }

            static void Register([Kept] List<Guard> list) => _all = list;
        }

        sealed class Wrapper
        {
            public static List<Guard>? Last;

            public static implicit operator Wrapper([Kept] List<Guard> list) { Last = list; return new Wrapper(); }

            public static Wrapper operator +(Wrapper wrapper, List<Guard> list) => wrapper;
        }

        {{Sources.Guard}}
        """,
        Verify.ReadFromStoredContainer(0, "'List<T>.this[int]'", "Guard", because: "'list' is not the method's own anywhere in the method, because on line 14 a lambda or a local function uses it"),
        Verify.ReadFromStoredContainer(1, "'List<T>.this[int]'", "Guard", because: "'list' is not the method's own anywhere in the method, because on line 21 a lambda or a local function uses it"),
        Verify.UnownedContainerReturned(2, "C.Registered()", "System.Collections.Generic.List<Guard>", because: "'list' is not the method's own anywhere in the method, because on line 29 it is passed to 'C.Register(List<Guard>)', which keeps it"),
        Verify.ReadFromStoredContainer(3, "'List<T>.this[int]'", "Guard", because: "'list' is not the method's own anywhere in the method, because on line 36 it is passed to 'C.Register(List<Guard>)', which keeps it"),
        Verify.ContainerMovedFromUnowned(4, "System.Collections.Generic.List<Guard>", "list", because: "on line 43 it is passed to 'C.Register(List<Guard>)', which keeps it"),
        Verify.UnownedContainerReturned(5, "C.MovedAfterRegistering(RwLock)", "System.Collections.Generic.List<Guard>"),
        Verify.UnownedContainerReturned(6, "C.GivenAStoredOne()", "System.Collections.Generic.List<Guard>"),
        Verify.UnownedContainerReturned(7, "C.Converted()", "System.Collections.Generic.List<Guard>", because: "'list' is not the method's own anywhere in the method, because on line 68 it is passed to 'Wrapper.implicit operator Wrapper(List<Guard>)', which keeps it"),
        Verify.UnownedContainerReturned(8, "C.Added(Wrapper)", "System.Collections.Generic.List<Guard>", because: "'list' is not the method's own anywhere in the method, because on line 75 it is passed to 'Wrapper.operator +(Wrapper, List<Guard>)'"));

    [TestMethod]
    public Task Message_NamesWhatMakesAClassAContainer() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using System.Collections.Generic;
        using System.Threading.Tasks;
        using Singulink.CodeAnalysis;

        sealed class GuardList : List<Guard>;

        sealed class Waiters : IEnumerable<Task<Guard>>
        {
            public IEnumerator<Task<Guard>> GetEnumerator() => null!;
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => null!;
        }

        class C
        {
            private static GuardList? _kept;
            private static Waiters? _waiters;

            // The name of such a class does not show what it holds, so the message names the base class or the interface.
            static void Keep(GuardList list) => _kept = {|#0:list|};

            static void KeepWaiters(Waiters waiters) => _waiters = {|#1:waiters|};

            static GuardList Kept() => {|#2:_kept|}!;

            static object Hidden(GuardList list) => {|#3:list|};

            static GuardList Moved() => {|#8:NonCopyable.Move(ref {|#4:_kept|})|}!;

            // A constructed type shows it by itself.
            static object HiddenList(List<Guard> list) => {|#5:list|};
        }

        {{Sources.Guard}}
        """,
        Verify.KeepsUnmarkedParameter(0, "C.Keep(GuardList)", "list", "GuardList", through: "System.Collections.Generic.List<Guard>"),
        Verify.KeepsUnmarkedParameter(1, "C.KeepWaiters(Waiters)", "waiters", "Waiters", through: "System.Collections.Generic.IEnumerable<System.Threading.Tasks.Task<Guard>>"),
        Verify.UnownedContainerReturned(2, "C.Kept()", "GuardList", through: "System.Collections.Generic.List<Guard>"),
        Verify.OpaqueConversion(3, "GuardList", "object", through: "System.Collections.Generic.List<Guard>"),
        Verify.ContainerMovedFromUnowned(4, "GuardList", "_kept", through: "System.Collections.Generic.List<Guard>"),
        Verify.UnownedContainerReturned(8, "C.Moved()", "GuardList", through: "System.Collections.Generic.List<Guard>"),
        Verify.OpaqueConversion(5, "System.Collections.Generic.List<Guard>", "object"));

    [TestMethod]
    public Task Message_IsWordedForAConstructorAndForTheResultOfALambda() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using System.Collections.Generic;
        using System.Threading.Tasks;
        using Singulink.CodeAnalysis;

        class C
        {
            private Task<Guard> _stored = null!;

            // A constructor hands out no copy: it creates a value that reads what the stored one still holds.
            ValueTask<Guard> Wrapped() => {|#0:new ValueTask<Guard>(_stored)|};

            ValueTask<Guard> WrappedLocal(RwLock rwLock)
            {
                var task = rwLock.EnterTaskAsync();
                _stored = task;
                return {|#1:new ValueTask<Guard>(task)|};
            }

            // A lambda with an expression body returns the result of its call, wanted or not: the fix is a statement body.
            void Run(RwLock rwLock)
            {
                Func<object> run = () => {|#2:rwLock.EnterAsync()|};
                Func<object> list = () => {|#3:rwLock.CreateGuards()|};
                Func<Task<object>> awaited = async () => {|#4:await Task.FromResult(new List<Guard>())|};
                Func<object?> statement = () => { rwLock.EnterAsync(); return null; };
                Action none = () => rwLock.EnterAsync();

                // Anything else that is converted keeps the general wording.
                Func<object> written = () => { return {|#5:rwLock.EnterAsync()|}; };
                object boxed = {|#6:rwLock.EnterAsync()|};
                object hidden = {|#7:rwLock.CreateGuards()|};
            }
        }

        {{Sources.Guard}}
        """,
        Verify.CreatedFromStoredContainer(0, "System.Threading.Tasks.ValueTask<Guard>"),
        Verify.CreatedFromStoredContainer(1, "System.Threading.Tasks.ValueTask<Guard>", because: "'task' is not the method's own anywhere in the method, because on line 16 it is assigned to something else"),
        Verify.OpaqueLambdaBoxing(2, "System.Threading.Tasks.ValueTask<Guard>", "object", "ValueTask<TResult>"),
        Verify.OpaqueLambdaResult(3, "System.Collections.Generic.List<Guard>", "object"),
        Verify.OpaqueLambdaResult(4, "System.Collections.Generic.List<Guard>", "object"),
        Verify.OpaqueBoxing(5, "System.Threading.Tasks.ValueTask<Guard>", "object", "ValueTask<TResult>"),
        Verify.OpaqueBoxing(6, "System.Threading.Tasks.ValueTask<Guard>", "object", "ValueTask<TResult>"),
        Verify.OpaqueConversion(7, "System.Collections.Generic.List<Guard>", "object"));

    // One use anywhere in a method ends a local being the method's own for all of the method. The message names that use with its
    // line, and does not read as if the use had come before the finding: here it comes after it.
    [TestMethod]
    public Task Message_NamesTheUseThatMadeALocalNotTheMethodsOwn_WithItsLine() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using System.Collections.Generic;
        using Singulink.CodeAnalysis;

        sealed class Holder
        {
            public Holder([Kept] List<Guard> guards)
            {
            }
        }

        static class C
        {
            static Holder Build(RwLock rwLock)
            {
                var guards = new List<Guard>();

                try
                {
                    guards.Add(rwLock.Enter());
                }
                catch
                {
                    for (int i = 0; i < guards.Count; i++)
                        {|#0:guards[i]|}.Dispose();

                    throw;
                }

                return new Holder(guards);
            }
        }

        {{Sources.Guard}}
        """,
        Verify.ReadFromStoredContainer(
            0,
            "'List<T>.this[int]'",
            "Guard",
            because: "'guards' is not the method's own anywhere in the method, because on line 30 it is passed to 'Holder.Holder(List<Guard>)', which keeps it"));

    // The advice is chosen by what the thing is. What a property or a call hands out by value cannot be moved out of, so the message
    // does not name NonCopyable.Move for it; a listed member reads from what it is called on, so the stored value is looked for
    // behind it. And a method that makes an object and returns what the object hands out acquires what it returns: [return: Borrowed]
    // would make the result stored for every caller, so the message does not name it.
    [TestMethod]
    public Task Message_GivesTheAdviceThatFitsWhatTheValueIsReadFrom() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using System.Collections.Generic;
        using System.Threading.Tasks;
        using Singulink.CodeAnalysis;

        sealed class Waiter
        {
            private readonly TaskCompletionSource<Guard> _source = new();

            public Task<Guard> Task => _source.Task;

            public List<Guard> Guards { get; } = new();
        }

        sealed class C
        {
            private readonly Queue<Waiter> _waiters = new();
            private readonly Waiter _first = new();
            private Task<Guard> _pending = null!;
            private List<Guard> _guards = new();

            ValueTask<Guard> EnterAsync()
            {
                var waiter = new Waiter();
                _waiters.Enqueue(waiter);
                return {|#0:new ValueTask<Guard>(waiter.Task)|};
            }

            Task<Guard> EnterTaskAsync()
            {
                Waiter waiter;
                waiter = new Waiter();
                _waiters.Enqueue(waiter);
                return {|#1:waiter.Task|};
            }

            // The object is not one that the method made: it hands out what it holds for good.
            Task<Guard> First() => {|#2:_first.Task|};

            async Task Reads()
            {
                Guard a = {|#3:await _first.Task|};
                Guard b = {|#4:await _pending.ConfigureAwait(false)|};
                Guard c = {|#5:_first.Guards[0]|};
                Guard d = {|#6:_guards[0]|};
                ValueTask<Guard> e = {|#7:new ValueTask<Guard>(_pending)|};
            }
        }

        {{Sources.Guard}}
        """,
        Verify.CreatedFromStoredContainer(0, "System.Threading.Tasks.ValueTask<Guard>", handsOut: "Waiter.Task"),
        Verify.UnownedContainerReturned(1, "C.EnterTaskAsync()", "System.Threading.Tasks.Task<Guard>", fromObjectMadeHere: true),
        Verify.UnownedContainerReturned(2, "C.First()", "System.Threading.Tasks.Task<Guard>"),
        Verify.ReadFromStoredContainer(3, "'await'", "Guard", canBeMoved: false),
        Verify.ReadFromStoredContainer(4, "'await'", "Guard"),
        Verify.ReadFromStoredContainer(5, "'List<T>.this[int]'", "Guard", canBeMoved: false),
        Verify.ReadFromStoredContainer(6, "'List<T>.this[int]'", "Guard"),
        Verify.CreatedFromStoredContainer(7, "System.Threading.Tasks.ValueTask<Guard>"));

    // An iterator or async member of a non-copyable struct is reported also where it is readonly: it changes nothing, but it runs
    // later, on a copy that is out of date by then. The message says both reasons and the clean form.
    [TestMethod]
    public Task Message_ForAnIteratorMemberGivesBothReasons() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using System.Collections.Generic;
        using Singulink.CodeAnalysis;

        [NonCopyable]
        struct Leases
        {
            private int[]? _ids;

            public readonly IEnumerable<int> Items
            {
                {|#0:get|}
                {
                    foreach (int id in _ids ?? [])
                        yield return id;
                }
            }

            // The clean form: a static iterator over what it needs.
            public readonly IEnumerable<int> Ids => Enumerate(_ids ?? []);

            private static IEnumerable<int> Enumerate(int[] ids)
            {
                foreach (int id in ids)
                    yield return id;
            }
        }

        // Nothing in a readonly struct can differ between the copy and the original.
        [NonCopyable]
        readonly struct Fixed(int[] ids)
        {
            public IEnumerable<int> Items
            {
                get
                {
                    foreach (int id in ids)
                        yield return id;
                }
            }
        }
        """,
        Verify.AsyncOrIteratorMember(0, "Leases.Items.get", "Leases", "iterator"));

    // The variable of a 'foreach' over stored elements is a copy of each: the message says what to write.
    [TestMethod]
    public Task Message_ForAForEachVariableSaysWhatToWrite() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using System.Collections.Generic;
        using System.Runtime.InteropServices;
        using Singulink.CodeAnalysis;

        sealed class C
        {
            private readonly List<Guard> _guards = new();
            private readonly Guard[] _array = new Guard[2];

            void M()
            {
                // Over a list it is the enumerator that hands out the copy.
                foreach (var guard in {|#0:_guards|})
                    guard.Dispose();

                foreach (var {|#1:guard|} in _array)
                    guard.Dispose();

                foreach (ref var guard in CollectionsMarshal.AsSpan(_guards))
                    guard.Dispose();

                foreach (ref var guard in _array.AsSpan())
                    guard.Dispose();
            }
        }

        {{Sources.Guard}}
        """,
        Verify.ForEachOverStoredList(0, "Guard", "guard", "_guards"),
        Verify.CopyByAssignmentIntoForEachVariable(1, "Guard", "guard"));

    // Docs: README "Common fixes", and articles/guides/common-fixes.md. Each form that the section gives as the fix is clean, and each form that it gives as the finding is
    // reported.
    [TestMethod]
    public Task Readme_CommonFixes() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using System.Collections.Generic;
        using System.Runtime.InteropServices;
        using System.Threading.Tasks;
        using Singulink.CodeAnalysis;

        [NonCopyable]
        struct TransactionScope : IDisposable
        {
            public void Dispose()
            {
            }
        }

        sealed class UnitOfWork
        {
            private TransactionScope _scope;

            public UnitOfWork([MovedFrom] ref TransactionScope scope, string user)
            {
                ArgumentException.ThrowIfNullOrEmpty(user);
                _scope = scope;
                scope = default;
            }

            public UnitOfWork(TransactionScope scope)
            {
                _scope = scope;
            }
        }

        delegate TResult GuardFunc<TResult>(ref Guard guard);

        sealed class C
        {
            private readonly List<Guard> _guards = new();
            private readonly Guard[] _array = new Guard[2];
            private readonly Dictionary<int, Guard> _held = new();
            private readonly Queue<TaskCompletionSource<Guard>> _waiters = new();
            private readonly RwLock _lock = new();
            private Guard _guard;

            static void Take(Guard guard)
            {
            }

            void ElementOfAList()
            {
                CollectionsMarshal.AsSpan(_guards)[0].Dispose();
                ref Guard held = ref CollectionsMarshal.AsSpan(_guards)[0];
                held.Dispose();
            }

            void Loop()
            {
                foreach (ref var guard in CollectionsMarshal.AsSpan(_guards))
                    guard.Dispose();

                foreach (ref var guard in _array.AsSpan())
                    guard.Dispose();
            }

            void ValueInADictionary(int key)
            {
                if (_held.Remove(key, out var taken))
                    taken.Dispose();

                int[] keys = [.. _held.Keys];

                foreach (int k in keys)
                {
                    if (_held.Remove(k, out var each))
                        each.Dispose();
                }
            }

            Guard Field()
            {
                Take(NonCopyable.Move(ref _guard));
                return NonCopyable.Move(ref _guard);
            }

            void ReadFirst(Guard guard)
            {
                int id = guard.Value;
                _guard = guard;
                Console.WriteLine(id);
            }

            TResult With<TResult>(GuardFunc<TResult> body)
            {
                var guard = _lock.Enter();

                try
                {
                    return body(ref guard);
                }
                finally
                {
                    guard.Dispose();
                }
            }

            int Callback() => With((ref Guard guard) => guard.Value);

            UnitOfWork ValidateThenTake(TransactionScope scope, string user)
            {
                try
                {
                    return new UnitOfWork(ref scope, user);
                }
                catch
                {
                    scope.Dispose();
                    throw;
                }
            }

            Task<Guard> EnterAsync()
            {
                var source = new TaskCompletionSource<Guard>();
                _waiters.Enqueue(source);
                return source.Task;
            }

            // What the section gives as the findings.
            void Reported(int key)
            {
                Guard first = {|#0:_guards[0]|};
                {|#1:_guards[0]|}.Dispose();

                foreach (var guard in {|#2:_guards|})
                    guard.Dispose();

                Guard held = {|#3:_held[key]|};
                Take({|#4:_guard|});
            }

            Guard ReturnedField() => {|#5:_guard|};

            void UsedAfter(Guard guard)
            {
                _guard = {|#6:guard|};
                Console.WriteLine(guard.Value);
            }

            UnitOfWork TakenByValue(TransactionScope scope)
            {
                try
                {
                    return new UnitOfWork({|#7:scope|});
                }
                catch
                {
                    scope.Dispose();
                    throw;
                }
            }
        }

        {{Sources.Guard}}
        """,
        Verify.ReadFromStoredContainer(0, "'List<T>.this[int]'", "Guard"),
        Verify.ReadFromStoredContainer(1, "'List<T>.this[int]'", "Guard"),
        Verify.ForEachOverStoredList(2, "Guard", "guard", "_guards"),
        Verify.ReadFromStoredContainer(3, "'Dictionary<TKey, TValue>.this[TKey]'", "Guard"),
        Verify.CopyOfStoredValue(4, "'C.Take(Guard)' receives", "Guard"),
        Verify.CopyByReturn(5, "Guard"),
        Verify.CopyByAssignment(6, "Guard"),
        Verify.CopyOfStoredValue(7, "'UnitOfWork.UnitOfWork(TransactionScope)' receives", "TransactionScope", usedByHandler: "scope"));

    // The loop over references is named only where it can be written: not for an 'await foreach', and not where the loop has an
    // 'await' or a 'yield' in it, which a reference cannot be held across. Where the loop has an expression for its list, the text
    // says 'list'.
    [TestMethod]
    public Task Message_ForAForEachOverAList_NamesTheLoopOverReferencesWhereItCanBeWritten() => Verify.AnalyzerAsync(
        $$"""
        using System.Collections.Generic;
        using System.Threading.Tasks;

        sealed class C
        {
            private readonly List<Guard> _guards = new();
            private readonly Queue<Guard> _queue = new();

            List<Guard> Guards => _guards;

            async Task WithAnAwaitAsync()
            {
                foreach (var guard in {|#0:_guards|})
                {
                    await Task.Yield();
                }
            }

            IEnumerable<int> WithAYield()
            {
                foreach (var guard in {|#1:_guards|})
                    yield return guard.Value;
            }

            void WithALambdaThatAwaits()
            {
                foreach (var guard in {|#2:this._guards|})
                {
                    System.Func<Task> later = async () => await Task.Yield();
                }
            }

            void OverAnExpression(bool first)
            {
                foreach (var guard in ({|#3:first ? _guards : Guards|}))
                {
                }
            }

            void OverAnotherCollection()
            {
                foreach (var guard in {|#4:_queue|})
                {
                }
            }
        }

        {{Sources.Guard}}
        """,
        Verify.ReadFromStoredContainer(0, "'foreach'", "Guard"),
        Verify.ReadFromStoredContainer(1, "'foreach'", "Guard"),
        Verify.ForEachOverStoredList(2, "Guard", "guard", "this._guards"),
        Verify.ForEachOverStoredList(3, "Guard", "guard", "list"),
        Verify.ReadFromStoredContainer(4, "'foreach'", "Guard"));

    // Docs: articles/guides/common-fixes.md, "Correct Code That Stays Reported". Each of the three shapes is reported as the section says, and the form that it
    // gives for the first is clean.
    [TestMethod]
    public Task Docs_CorrectCodeThatStaysReported() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using System.Collections.Generic;
        using System.Runtime.InteropServices;
        using System.Threading.Channels;
        using System.Threading.Tasks;

        sealed class C
        {
            // An 'async' helper that fills a container which its caller awaits.
            static async Task FillAsync(RwLock myLock, List<Guard> guards)
            {
                Guard guard = await myLock.EnterAsync();
                {|#0:guards|}.Add(guard);
            }

            static async Task<List<Guard>> MakeAsync(RwLock myLock)
            {
                var guards = new List<Guard>();
                guards.Add(await myLock.EnterAsync());
                return guards;
            }

            static async Task UseAsync(RwLock myLock)
            {
                List<Guard> guards = await MakeAsync(myLock);

                foreach (ref var guard in CollectionsMarshal.AsSpan(guards))
                    guard.Dispose();
            }

            // Handing a value over and keeping it on failure.
            static void TryWrite(ChannelWriter<Guard> writer, RwLock myLock)
            {
                Guard lease = myLock.Enter();

                if (!writer.TryWrite({|#1:lease|}))
                    lease.Dispose();
            }

            static async Task WriteAsync(ChannelWriter<Guard> writer, RwLock myLock)
            {
                Guard lease = myLock.Enter();

                try
                {
                    await writer.WriteAsync({|#2:lease|});
                }
                catch
                {
                    lease.Dispose();
                    throw;
                }
            }

            static void TrySetResult(TaskCompletionSource<Guard> source, RwLock myLock)
            {
                Guard guard = myLock.Enter();

                if (!source.TrySetResult({|#3:guard|}))
                    guard.Dispose();
            }

            // A race between two tasks that hold a guard.
            static async Task<Guard> RaceAsync(RwLock a, RwLock b)
            {
                Task<Guard> first = a.EnterTaskAsync();
                Task<Guard> second = b.EnterTaskAsync();
                Task<Guard> winner = await Task.WhenAny(first, second);
                Task<Guard> loser = ReferenceEquals(winner, first) ? second : first;
                Guard lost = {|#4:await loser|};
                lost.Dispose();
                return {|#5:await winner|};
            }
        }

        {{Sources.Guard}}
        """,
        Verify.KeepsParameterAfterReturning(0, "C.FillAsync(RwLock, List<Guard>)", "guards", "System.Collections.Generic.List<Guard>"),
        Verify.CopyOfStoredValue(1, "'ChannelWriter<Guard>.TryWrite(Guard)' receives", "Guard"),
        Verify.CopyOfStoredValue(2, "'ChannelWriter<Guard>.WriteAsync(Guard, CancellationToken)' receives", "Guard", usedByHandler: "lease"),
        Verify.CopyOfStoredValue(3, "'TaskCompletionSource<Guard>.TrySetResult(Guard)' receives", "Guard"),
        Verify.ReadFromStoredContainer(4, "'await'", "Guard"),
        Verify.ReadFromStoredContainer(
            5,
            "'await'",
            "Guard",
            because: "'winner' is not the method's own anywhere in the method, because on line 69 it is passed to 'object.ReferenceEquals(object?, object?)'"));

    // Docs: articles/concepts/last-use.md. A list that is the method's own hands out a new value at its last use, through its
    // indexer. An array has no member for that: its element is read in place, and is a stored value there too. And a list that was
    // passed to a method, also to one that only uses it, is no new container for a read, though it is still the method's to return.
    [TestMethod]
    public Task Docs_ElementOfAnArrayAndOfAListAtTheLastUse() => Verify.AnalyzerAsync(
        $$"""
        using System.Collections.Generic;
        using Singulink.CodeAnalysis;

        static class C
        {
            static void Fill(List<Guard> list) { }

            static void OfAList(RwLock myLock)
            {
                var list = new List<Guard>();
                list.Add(myLock.Enter());
                Guard first = list[0];
                first.Dispose();
            }

            static void OfAnArray(RwLock myLock)
            {
                var array = new Guard[1];
                array[0] = myLock.Enter();
                Guard first {|#0:= array[0]|};
                first.Dispose();
            }

            static void MovedOutOfAnArray(RwLock myLock)
            {
                var array = new Guard[1];
                array[0] = myLock.Enter();
                Guard first = NonCopyable.Move(ref array[0]);
                first.Dispose();
            }

            static void AfterItWasPassedOn()
            {
                var list = new List<Guard>();
                Fill(list);
                Guard first = {|#1:list[0]|};
                first.Dispose();
            }

            static List<Guard> ReturnedAfterItWasPassedOn()
            {
                var list = new List<Guard>();
                Fill(list);
                return list;
            }
        }

        {{Sources.Guard}}
        """,
        Verify.CopyByAssignment(0, "Guard"),
        Verify.ReadFromStoredContainer(1, "'List<T>.this[int]'", "Guard"));
}
