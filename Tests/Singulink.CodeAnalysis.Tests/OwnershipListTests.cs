using Microsoft.CodeAnalysis.Testing;
using PrefixClassName.MsTest;

namespace Singulink.CodeAnalysis.Tests;

/// <summary>
/// Tests of the one list of what a method may do with a local that holds a container and still have it as its own, and of what the
/// method on the other side of a call is reported for.
/// </summary>
/// <remarks>
/// Docs: articles/concepts/returning-containers.md.
/// </remarks>
[PrefixTestClass]
public class OwnershipListTests
{
    private const string Types = """
        using System;
        using System.Buffers;
        using System.Collections.Generic;
        using System.Runtime.CompilerServices;
        using System.Runtime.InteropServices;
        using Singulink.CodeAnalysis;

        delegate void Filler(List<Guard> list);

        sealed class Holder
        {
            public Guard Guard;
        }

        sealed class Bag<[NonCopyable] T>
        {
            private T _item = default!;

            public void Add(T item) => _item = NonCopyable.Move(ref item);
        }

        static class Box<[NonCopyable] T>
        {
            private static T s_item = default!;

            public static void Keep(T item) => s_item = NonCopyable.Move(ref item);
        }

        sealed class Wrapper
        {
            private List<Guard> _list = new();

            public static implicit operator Wrapper([Kept] List<Guard> list) => new() { _list = list };

            public static Wrapper operator +(Wrapper wrapper, [Kept] List<Guard> list) => new() { _list = list };
        }

        [InterpolatedStringHandler]
        ref struct KeepingHandler
        {
            public static List<Guard> Last = new();

            public KeepingHandler(int literalLength, int formattedCount) { }

            public void AppendLiteral(string text) { }

            public void AppendFormatted([Kept] List<Guard> list) => Last = list;
        }

        static class Extensions
        {
            public static List<Guard> Registered = new();

            extension([Kept] List<Guard> list)
            {
                public void Register() => Registered = list;
            }

            extension(List<Guard> list)
            {
                public int CountAll() => list.Count;
            }
        }
        """;

    [TestMethod]
    public Task OwnLocal_IsTheMethodsOwnByAListOfWhatIsAllowed() => Verify.AnalyzerAsync(
        $$"""
        {{Types}}

        class C
        {
            static List<Guard> s_cache = new();
            static Memory<Guard> s_memory;
            static ArraySegment<Guard> s_segment;

            static void Fill(List<Guard> list, RwLock rwLock) => list.Add(rwLock.Enter());
            static void Register(params List<Guard>[] lists) { }
            static void RegisterSpan(params ReadOnlySpan<List<Guard>> lists) { }
            static void Look(IReadOnlyList<Guard> list) { }
            static void Log(KeepingHandler message) { }
            static void Touch(ref Guard guard) { }
            static Memory<Guard> Window(Guard[] guards) => guards;

            [return: Borrowed]
            static List<Guard> EnsureNotEmpty(List<Guard> list) => list;

            // What is allowed: a method that only uses the list, a delegate of the same kind, members of the list itself, and elements
            // of an array that are used in place.
            List<Guard> Filled(RwLock rwLock, Filler fill)
            {
                var list = new List<Guard>();
                Fill(list, rwLock);
                fill(list);
                list.Add(rwLock.Enter());
                int count = list.Count + list.CountAll();
                return list;
            }

            // A view of the local that is made and used in one go leaves no view behind, and neither does a field of an object
            // that is given a new value.
            List<Guard> ViewedOnTheSpot(RwLock rwLock, bool failed)
            {
                var list = new List<Guard>();
                list.Add(rwLock.Enter());

                foreach (ref Guard guard in CollectionsMarshal.AsSpan(list))
                {
                    if (failed)
                        guard.Dispose();
                }

                CollectionsMarshal.AsSpan(list)[0].Value = 1;
                int length = CollectionsMarshal.AsSpan(list).Length;
                return list;
            }

            Holder FieldsFilled(RwLock rwLock)
            {
                var holder = new Holder();
                holder.Guard = rwLock.Enter();
                holder.Guard.Value = 2;
                return holder;
            }

            List<Guard> ViewKept()
            {
                var list = new List<Guard>();
                Span<Guard> span = CollectionsMarshal.AsSpan(list);
                return {|#21:list|};
            }

            Guard[] ElementsUsedInPlace(RwLock rwLock)
            {
                var guards = new Guard[2];
                guards[0] = rwLock.Enter();
                guards[0].Value = 1;
                guards[0].Dispose();
                Touch(ref guards[1]);
                guards[1] = NonCopyable.Move(ref guards[0]);
                return guards;
            }

            // A marked collection keeps what it is given.
            List<Guard> InAMarkedCollection(Bag<List<Guard>> bag)
            {
                var list = new List<Guard>();
                bag.Add(list);
                return {|#0:list|};
            }

            Guard[] InAStaticMarkedGeneric()
            {
                var guards = new Guard[1];
                Box<Guard[]>.Keep(guards);
                return {|#1:guards|};
            }

            List<Guard> InACollectionInitializer()
            {
                var list = new List<Guard>();
                var lists = new List<List<Guard>> { list };
                return {|#2:list|};
            }

            // A helper that hands its argument back gives a second reference to it.
            List<Guard> HandedBack()
            {
                var list = new List<Guard>();
                s_cache = EnsureNotEmpty(list);
                return {|#3:list|};
            }

            // The collection that the compiler makes for a 'params' parameter is another container that holds it.
            List<Guard> InAParamsArray()
            {
                var list = new List<Guard>();
                Register(list);
                return {|#4:list|};
            }

            List<Guard> InAParamsSpan()
            {
                var list = new List<Guard>();
                RegisterSpan(list);
                return {|#5:list|};
            }

            // A view of the local that is stored reaches its elements in place.
            Guard[] AsAView()
            {
                var guards = new Guard[1];
                s_memory = guards;
                return {|#6:guards|};
            }

            Guard[] AsASegment()
            {
                var guards = new Guard[1];
                s_segment = guards;
                return {|#7:guards|};
            }

            Guard[] AsAViewFromACall()
            {
                var guards = new Guard[1];
                s_memory = Window(guards);
                return {|#8:guards|};
            }

            Guard[] AsAViewFromTheFramework()
            {
                var guards = new Guard[1];
                s_memory = guards.AsMemory();
                return {|#9:guards|};
            }

            List<Guard> WithAViewHandedOut(out Span<Guard> span)
            {
                var list = new List<Guard>();
                span = CollectionsMarshal.AsSpan(list);
                return {|#10:list|};
            }

            // A conversion, an operator and a handler are not on the list, whatever their parameters say.
            List<Guard> ThroughAConversion()
            {
                var list = new List<Guard>();
                Wrapper wrapper = list;
                return {|#11:list|};
            }

            List<Guard> ThroughAnOperator(Wrapper wrapper)
            {
                var list = new List<Guard>();
                wrapper += list;
                return {|#12:list|};
            }

            List<Guard> ThroughAHandler()
            {
                var list = new List<Guard>();
                Log($"{list}");
                return {|#13:list|};
            }

            // The receiver of a member of an extension block is a parameter, and can be marked like one.
            List<Guard> ThroughAnExtensionBlock()
            {
                var list = new List<Guard>();
                list.Register();
                return {|#14:list|};
            }

            // Neither is a parameter of another type, or one of code that is not checked.
            List<Guard> AsAnInterface()
            {
                var list = new List<Guard>();
                Look(list);
                return {|#15:list|};
            }

            List<Guard> ToADelegateOverATypeParameter(Action<List<Guard>> fill)
            {
                var list = new List<Guard>();
                fill(list);
                return {|#16:list|};
            }

            Guard[] ToAPool()
            {
                var guards = new Guard[16];
                ArrayPool<Guard>.Shared.Return(guards);
                return {|#17:guards|};
            }

            // A local that is not the method's own is not one to move a container out of either.
            List<Guard> Moved(Bag<List<Guard>> bag)
            {
                var list = new List<Guard>();
                bag.Add(list);
                return {|#20:NonCopyable.Move(ref {|#18:list|})|};
            }

            // The last use allows less: a list that was passed to anything is not moved from.
            void AtTheLastUse(RwLock rwLock)
            {
                var list = new List<Guard>();
                Fill(list, rwLock);
                Guard first = {|#19:list[0]|};
            }
        }

        {{Sources.Guard}}
        """,
        Returned(0, "C.InAMarkedCollection(Bag<List<Guard>>)", "System.Collections.Generic.List<Guard>", because: "'list' is not the method's own anywhere in the method, because on line 142 it is passed to 'Bag<List<Guard>>.Add(List<Guard>)', which is taken to keep what a type parameter is given"),
        Returned(1, "C.InAStaticMarkedGeneric()", "Guard[]", because: "'guards' is not the method's own anywhere in the method, because on line 149 it is passed to 'Box<Guard[]>.Keep(Guard[])', which is taken to keep what a type parameter is given"),
        Returned(2, "C.InACollectionInitializer()", "System.Collections.Generic.List<Guard>", because: "'list' is not the method's own anywhere in the method, because on line 156 it is passed to 'List<List<Guard>>.Add(List<Guard>)', which is taken to keep what a type parameter is given"),
        Returned(3, "C.HandedBack()", "System.Collections.Generic.List<Guard>", because: "'list' is not the method's own anywhere in the method, because on line 164 it is passed to 'C.EnsureNotEmpty(List<Guard>)'"),
        Returned(4, "C.InAParamsArray()", "System.Collections.Generic.List<Guard>", because: "'list' is not the method's own anywhere in the method, because on line 172 it is passed to 'C.Register(params List<Guard>[])'"),
        Returned(5, "C.InAParamsSpan()", "System.Collections.Generic.List<Guard>", because: "'list' is not the method's own anywhere in the method, because on line 179 it is passed to 'C.RegisterSpan(params ReadOnlySpan<List<Guard>>)'"),
        Returned(6, "C.AsAView()", "Guard[]", because: "'guards' is not the method's own anywhere in the method, because on line 187 it is passed to 'Memory<Guard>.implicit operator Memory<Guard>(Guard[]?)'"),
        Returned(7, "C.AsASegment()", "Guard[]", because: "'guards' is not the method's own anywhere in the method, because on line 194 it is passed to 'ArraySegment<Guard>.implicit operator ArraySegment<Guard>(Guard[])'"),
        Returned(8, "C.AsAViewFromACall()", "Guard[]", because: "'guards' is not the method's own anywhere in the method, because on line 201 it is passed to 'C.Window(Guard[])', which hands out a view of it"),
        Returned(9, "C.AsAViewFromTheFramework()", "Guard[]", because: "'guards' is not the method's own anywhere in the method, because on line 208 it is passed to 'MemoryExtensions.AsMemory<Guard>(Guard[]?)', which hands out a view of it"),
        Returned(10, "C.WithAViewHandedOut(out Span<Guard>)", "System.Collections.Generic.List<Guard>", because: "'list' is not the method's own anywhere in the method, because on line 215 it is passed to 'CollectionsMarshal.AsSpan<Guard>(List<Guard>?)', which hands out a view of it"),
        Returned(11, "C.ThroughAConversion()", "System.Collections.Generic.List<Guard>", because: "'list' is not the method's own anywhere in the method, because on line 223 it is passed to 'Wrapper.implicit operator Wrapper(List<Guard>)', which keeps it"),
        Returned(12, "C.ThroughAnOperator(Wrapper)", "System.Collections.Generic.List<Guard>", because: "'list' is not the method's own anywhere in the method, because on line 230 it is passed to 'Wrapper.operator +(Wrapper, List<Guard>)', which keeps it"),
        Returned(13, "C.ThroughAHandler()", "System.Collections.Generic.List<Guard>", because: "'list' is not the method's own anywhere in the method, because on line 237 it is passed to an interpolated-string handler"),
        Returned(14, "C.ThroughAnExtensionBlock()", "System.Collections.Generic.List<Guard>", because: "'list' is not the method's own anywhere in the method, because on line 245 it is passed to 'Extensions.extension(List<Guard>).Register()', which keeps it"),
        Returned(15, "C.AsAnInterface()", "System.Collections.Generic.List<Guard>", because: "'list' is not the method's own anywhere in the method, because on line 253 it is passed to 'C.Look(IReadOnlyList<Guard>)'"),
        Returned(16, "C.ToADelegateOverATypeParameter(Action<List<Guard>>)", "System.Collections.Generic.List<Guard>", because: "'list' is not the method's own anywhere in the method, because on line 260 it is passed to 'Action<List<Guard>>.Invoke(List<Guard>)', which is taken to keep what a type parameter is given"),
        Returned(17, "C.ToAPool()", "Guard[]", because: "'guards' is not the method's own anywhere in the method, because on line 267 it is passed to 'ArrayPool<Guard>.Return(Guard[], bool)', which is taken to keep it: the type of its parameter mentions a type parameter that is not marked"),
        Returned(21, "C.ViewKept()", "System.Collections.Generic.List<Guard>", because: "'list' is not the method's own anywhere in the method, because on line 123 it is passed to 'CollectionsMarshal.AsSpan<Guard>(List<Guard>?)', which hands out a view of it"),
        Verify.ContainerMovedFromUnowned(18, "System.Collections.Generic.List<Guard>", "list", because: "on line 275 it is passed to 'Bag<List<Guard>>.Add(List<Guard>)', which is taken to keep what a type parameter is given"),
        Returned(20, "C.Moved(Bag<List<Guard>>)", "System.Collections.Generic.List<Guard>"),
        Verify.ReadFromStoredContainer(19, "'List<T>.this[int]'", "Guard"));

    [TestMethod]
    public Task Kept_WhatTheOtherSideOfACallKeeps() => Verify.AnalyzerAsync(
        $$"""
        {{Types}}

        static class Keeping
        {
            public static List<Guard> Kept = new();

            // The receiver of a member of an extension block is a parameter of the member.
            extension(List<Guard> list)
            {
                public void Keep() => Kept = {|#0:list|};

                public int Size => list.Count;
            }

            // An element of a parameter that holds containers is a container that the caller put in.
            public static void FirstOf(params List<Guard>[] lists) => Kept = {|#1:lists|}[0];

            public static void FirstOfSpan(params ReadOnlySpan<List<Guard>> lists) => Kept = {|#2:lists|}[0];

            public static void LastOf(List<List<Guard>> lists)
            {
                foreach (var list in lists)
                    Kept = {|#3:list|};
            }

            // A handler keeps what a parameter of it is marked to keep, and a method that passes its parameter on to that keeps it.
            public static void Logged(List<Guard> list) => Log($"{ {|#4:list|} }");

            public static void Formatted(List<Guard> list) => Format($"{list.Count}");

            static void Log(KeepingHandler message) { }

            static void Format(string message) { }
        }

        {{Sources.Guard}}
        """,
        Verify.KeepsUnmarkedParameter(0, "Keeping.extension(List<Guard>).Keep()", "list", "System.Collections.Generic.List<Guard>"),
        Verify.KeepsUnmarkedParameter(1, "Keeping.FirstOf(params List<Guard>[])", "lists", "System.Collections.Generic.List<Guard>[]"),
        Verify.KeepsUnmarkedParameter(2, "Keeping.FirstOfSpan(params ReadOnlySpan<List<Guard>>)", "lists", "System.ReadOnlySpan<System.Collections.Generic.List<Guard>>"),
        Verify.KeepsUnmarkedParameter(3, "Keeping.LastOf(List<List<Guard>>)", "lists", "System.Collections.Generic.List<System.Collections.Generic.List<Guard>>"),
        Verify.KeepsUnmarkedParameter(4, "Keeping.Logged(List<Guard>)", "list", "System.Collections.Generic.List<Guard>"));

    // The mark against a delegate is judged as the mark against a base member is: by what the other side is taken to keep.
    [TestMethod]
    public Task Kept_AgainstADelegateIsJudgedByWhatTheDelegateIsTakenToKeep() => Verify.AnalyzerAsync(
        $$"""
        {{Types}}

        class C
        {
            static List<Guard> s_kept = new();

            static void Keep([Kept] List<Guard> list) => s_kept = list;

            void M()
            {
                Filler filler = {|#0:Keep|};

                // What is passed to a parameter whose type is a type parameter that is not marked is taken to be kept.
                Action<List<Guard>> action = Keep;
            }
        }

        {{Sources.Guard}}
        """,
        Verify.KeptNotOnDelegate(0, "list", "C.Keep(List<Guard>)", "Filler"));

    // A value that holds a container through a type argument of a type that contains its type holds a container all the same.
    [TestMethod]
    public Task OwnLocal_ThatHoldsContainersThroughAContainingTypeIsHeldToTheStrictList() => Verify.AnalyzerAsync(
        $$"""
        using System.Collections.Generic;
        using System.Threading.Tasks;
        using Singulink.CodeAnalysis;

        struct Outer<[NonCopyable] T>
        {
            public struct Entry<[NonCopyable] U>
            {
                public T First;
                public U Second;
            }
        }

        class C
        {
            static void Fill(List<Outer<Task<Guard>>.Entry<Guard>> list) { }
            static void FillPlain(List<Outer<int>.Entry<Guard>> list) { }

            List<Outer<Task<Guard>>.Entry<Guard>> WithATaskInside()
            {
                var list = new List<Outer<Task<Guard>>.Entry<Guard>>();
                Fill(list);
                return {|#0:list|};
            }

            List<Outer<int>.Entry<Guard>> WithValuesOnly()
            {
                var list = new List<Outer<int>.Entry<Guard>>();
                FillPlain(list);
                return list;
            }
        }

        {{Sources.Guard}}
        """,
        Returned(0, "C.WithATaskInside()", "System.Collections.Generic.List<Outer<System.Threading.Tasks.Task<Guard>>.Entry<Guard>>", because: "'list' is not the method's own anywhere in the method, because on line 22 it is passed to 'C.Fill(List<Outer<Task<Guard>>.Entry<Guard>>)'"));

    private static DiagnosticResult Returned(int location, string method, string type, string? because = null, bool createdHere = true) =>
        Verify.UnownedContainerReturned(location, method, type, because, createdHere);

    // A parameter whose type is a type parameter keeps what it is given, marked or not, by value or by reference: that is what a caller
    // assumes of 'bag.Add(list)'. So a method that passes its own container parameter to one keeps that parameter.
    [TestMethod]
    public Task Kept_PassingAParameterToATypeParameterOfAMarkedGenericKeepsIt() => Verify.AnalyzerAsync(
        $$"""
        using System.Collections.Generic;
        using Singulink.CodeAnalysis;

        sealed class Bag<[NonCopyable] T>
        {
            private T[] _items = new T[4];
            private int _count;

            public void Add(T item) => _items[_count++] = NonCopyable.Move(ref item);

            public void AddByReference(ref T item) => _items[_count++] = NonCopyable.Move(ref item);

            public void AddReadOnly(in T item) { }

            public int Count => _count;
        }

        static class Slot<[NonCopyable] T>
        {
            public static T? Value;
        }

        static class Registry
        {
            static readonly Bag<List<Guard>> s_registered = new();

            static void Register(List<Guard> guards) => s_registered.Add({|#0:guards|});

            static void RegisterByReference(List<Guard> guards) => s_registered.AddByReference(ref {|#1:guards|});

            static void RegisterReadOnly(List<Guard> guards) => s_registered.AddReadOnly(in {|#2:guards|});

            static void Put<[NonCopyable] T>(ref T value) => Slot<T>.Value = NonCopyable.Move(ref value);

            static void RegisterByAMethod(List<Guard> guards) => Put(ref {|#3:guards|});

            // With the mark the method says so, and its callers know.
            static void RegisterMarked([Kept] List<Guard> guards) => s_registered.Add(guards);

            // A parameter of the container's own type only uses what it is given.
            static int Count(List<Guard> guards) => Measure(guards);

            static int Measure(List<Guard> guards) => guards.Count;

            // The caller of a method that keeps is told by the mark alone.
            static List<Guard> Make(RwLock rwLock)
            {
                var guards = rwLock.CreateGuards();
                RegisterMarked(guards);
                return {|#4:guards|};
            }
        }

        {{Sources.Guard}}
        """,
        Verify.KeepsUnmarkedParameter(0, "Registry.Register(List<Guard>)", "guards", "System.Collections.Generic.List<Guard>"),
        Verify.KeepsUnmarkedParameter(1, "Registry.RegisterByReference(List<Guard>)", "guards", "System.Collections.Generic.List<Guard>"),
        Verify.KeepsUnmarkedParameter(2, "Registry.RegisterReadOnly(List<Guard>)", "guards", "System.Collections.Generic.List<Guard>"),
        Verify.KeepsUnmarkedParameter(3, "Registry.RegisterByAMethod(List<Guard>)", "guards", "System.Collections.Generic.List<Guard>"),
        Verify.UnownedContainerReturned(4, "Registry.Make(RwLock)", "System.Collections.Generic.List<Guard>", because: "'guards' is not the method's own anywhere in the method, because on line 49 it is passed to 'Registry.RegisterMarked(List<Guard>)', which keeps it"));

    // A method that goes on after it has returned keeps what it still uses: an iterator every container parameter that it uses, and an
    // 'async' method every one that it uses on a path after an 'await'.
    [TestMethod]
    public Task Kept_AnIteratorAndAnAsyncMethodKeepWhatTheyUseAfterTheyHaveReturned() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using System.Collections.Generic;
        using System.Runtime.InteropServices;
        using System.Threading;
        using System.Threading.Tasks;
        using Singulink.CodeAnalysis;

        static class Later
        {
            static async Task ReleaseLaterAsync(List<Guard> guards, SemaphoreSlim gate)
            {
                await gate.WaitAsync();

                foreach (ref Guard g in CollectionsMarshal.AsSpan({|#0:guards|}))
                    g.Dispose();
            }

            static IEnumerable<int> ReleaseLater(List<Guard> guards)
            {
                foreach (ref Guard g in CollectionsMarshal.AsSpan({|#1:guards|}))
                    g.Dispose();

                yield break;
            }

            // The caller takes its local to be its own after either call.
            static List<Guard> Make(RwLock rwLock, SemaphoreSlim gate, out Task pending, out IEnumerable<int> later)
            {
                var guards = rwLock.CreateGuards();
                pending = ReleaseLaterAsync(guards, gate);
                later = ReleaseLater(guards);
                return guards;
            }

            // With the mark the method says so, and its caller is told.
            static async Task ReleaseMarkedAsync([Kept] List<Guard> guards, SemaphoreSlim gate)
            {
                await gate.WaitAsync();
                guards.Clear();
            }

            static IEnumerable<int> ReleaseMarked([Kept] List<Guard> guards)
            {
                guards.Clear();
                yield break;
            }

            static List<Guard> MakeForMarked(RwLock rwLock, SemaphoreSlim gate, out Task pending)
            {
                var guards = rwLock.CreateGuards();
                pending = ReleaseMarkedAsync(guards, gate);
                return {|#2:guards|};
            }

            static List<Guard> MakeForMarkedIterator(RwLock rwLock, out IEnumerable<int> later)
            {
                var guards = rwLock.CreateGuards();
                later = ReleaseMarked(guards);
                return {|#3:guards|};
            }

            // What an 'async' method does before its first 'await' runs during the call: the method keeps nothing. That the task is not
            // the method's own to read from is another matter.
            static async Task<Guard> Wrap(Task<Guard> inner) => {|#15:await inner|};

            static async Task CountFirstAsync(List<Guard> guards, SemaphoreSlim gate)
            {
                int count = guards.Count;
                await gate.WaitAsync();
                Console.WriteLine(count);
            }

            static async Task DelayAsync(List<Guard> guards)
            {
                await Task.Delay(guards.Count);
            }

            // A use in one branch after an 'await' is a use after it.
            static async Task InOneBranchAsync(List<Guard> guards, SemaphoreSlim gate, bool flag)
            {
                await gate.WaitAsync();

                if (flag)
                    {|#4:guards|}.Clear();
            }

            // A loop comes back to the use.
            static async Task InALoopAsync(List<Guard> guards, SemaphoreSlim gate)
            {
                for (int i = 0; i < 2; i++)
                {
                    {|#5:guards|}.Clear();
                    await gate.WaitAsync();
                }
            }

            // A local that holds the container is the container.
            static async Task ThroughALocalAsync(List<Guard> guards, SemaphoreSlim gate)
            {
                var held = guards;
                await gate.WaitAsync();
                {|#6:held|}.Clear();
            }

            // The 'await' of an 'await foreach' and of an 'await using' counts.
            static async Task AfterAwaitForEachAsync(List<Guard> guards, IAsyncEnumerable<int> numbers)
            {
                guards.Clear();

                await foreach (int number in numbers)
                    {|#7:guards|}.Clear();
            }

            static async Task AfterAwaitUsingAsync(List<Guard> guards, IAsyncDisposable resource)
            {
                await using (resource)
                    guards.Clear();

                {|#8:guards|}.Clear();
            }

            // An async iterator is both.
            static async IAsyncEnumerable<int> EnumerateAsync(List<Guard> guards)
            {
                {|#9:guards|}.Clear();
                await Task.Yield();
                yield return 1;
            }

            // A lambda and a local function are judged the same way.
            static void Functions(SemaphoreSlim gate)
            {
                Func<List<Guard>, Task> lambda = async guards =>
                {
                    await gate.WaitAsync();
                    {|#10:guards|}.Clear();
                };

                async Task LocalAsync(List<Guard> guards)
                {
                    await gate.WaitAsync();
                    {|#11:guards|}.Clear();
                }

                IEnumerable<int> Local(List<Guard> guards)
                {
                    {|#12:guards|}.Clear();
                    yield break;
                }
            }
        }

        // The mark is in the signature, so an interface whose implementation needs it needs it too.
        interface IReleaser
        {
            Task ReleaseAsync(List<Guard> guards, SemaphoreSlim gate);

            Task ReleaseMarkedAsync([Kept] List<Guard> guards, SemaphoreSlim gate);
        }

        sealed class Releaser : IReleaser
        {
            public async Task ReleaseAsync([Kept] List<Guard> {|#13:guards|}, SemaphoreSlim gate)
            {
                await gate.WaitAsync();
                guards.Clear();
            }

            public async Task ReleaseMarkedAsync([Kept] List<Guard> guards, SemaphoreSlim gate)
            {
                await gate.WaitAsync();
                guards.Clear();
            }
        }

        sealed class UnmarkedReleaser : IReleaser
        {
            public async Task ReleaseAsync(List<Guard> guards, SemaphoreSlim gate)
            {
                await gate.WaitAsync();
                {|#14:guards|}.Clear();
            }

            public Task ReleaseMarkedAsync([Kept] List<Guard> guards, SemaphoreSlim gate) => Task.CompletedTask;
        }

        {{Sources.Guard}}
        """,
        Verify.KeepsParameterAfterReturning(0, "Later.ReleaseLaterAsync(List<Guard>, SemaphoreSlim)", "guards", "System.Collections.Generic.List<Guard>"),
        Verify.KeepsParameterAfterReturning(1, "Later.ReleaseLater(List<Guard>)", "guards", "System.Collections.Generic.List<Guard>", isIterator: true),
        Verify.UnownedContainerReturned(2, "Later.MakeForMarked(RwLock, SemaphoreSlim, out Task)", "System.Collections.Generic.List<Guard>", because: "'guards' is not the method's own anywhere in the method, because on line 51 it is passed to 'Later.ReleaseMarkedAsync(List<Guard>, SemaphoreSlim)', which keeps it"),
        Verify.UnownedContainerReturned(3, "Later.MakeForMarkedIterator(RwLock, out IEnumerable<int>)", "System.Collections.Generic.List<Guard>", because: "'guards' is not the method's own anywhere in the method, because on line 58 it is passed to 'Later.ReleaseMarked(List<Guard>)', which keeps it"),
        Verify.ReadFromStoredContainer(15, "'await'", "Guard"),
        Verify.KeepsParameterAfterReturning(4, "Later.InOneBranchAsync(List<Guard>, SemaphoreSlim, bool)", "guards", "System.Collections.Generic.List<Guard>"),
        Verify.KeepsParameterAfterReturning(5, "Later.InALoopAsync(List<Guard>, SemaphoreSlim)", "guards", "System.Collections.Generic.List<Guard>"),
        Verify.KeepsParameterAfterReturning(6, "Later.ThroughALocalAsync(List<Guard>, SemaphoreSlim)", "guards", "System.Collections.Generic.List<Guard>"),
        Verify.KeepsParameterAfterReturning(7, "Later.AfterAwaitForEachAsync(List<Guard>, IAsyncEnumerable<int>)", "guards", "System.Collections.Generic.List<Guard>"),
        Verify.KeepsParameterAfterReturning(8, "Later.AfterAwaitUsingAsync(List<Guard>, IAsyncDisposable)", "guards", "System.Collections.Generic.List<Guard>"),
        Verify.KeepsParameterAfterReturning(9, "Later.EnumerateAsync(List<Guard>)", "guards", "System.Collections.Generic.List<Guard>", isIterator: true),
        Verify.KeepsParameterAfterReturning(10, "lambda expression", "guards", "System.Collections.Generic.List<Guard>"),
        Verify.KeepsParameterAfterReturning(11, "LocalAsync(List<Guard>)", "guards", "System.Collections.Generic.List<Guard>"),
        Verify.KeepsParameterAfterReturning(12, "Local(List<Guard>)", "guards", "System.Collections.Generic.List<Guard>", isIterator: true),
        Verify.KeptNotOnBaseMember(13, "guards", "Releaser.ReleaseAsync(List<Guard>, SemaphoreSlim)", "IReleaser.ReleaseAsync(List<Guard>, SemaphoreSlim)"),
        Verify.KeepsParameterAfterReturning(14, "UnmarkedReleaser.ReleaseAsync(List<Guard>, SemaphoreSlim)", "guards", "System.Collections.Generic.List<Guard>"));

    private const string ListOfGuardsType = "System.Collections.Generic.List<Guard>";

    // An operation that is allowed is allowed with every symbol that it carries. Each of these is a kind of operation that a container
    // parameter may be used in, and carries a member of the project that takes the container where nothing but what the compiler
    // bound shows it: the operator of a compound assignment or of an increment that is an instance member of an extension block, the
    // conversion that a '??' applies to its left side and that a deconstruction applies to an element, a lifted conversion, and the
    // method of a method group. The 'Slice' and the length behind a range or an index from the end, which the older of the two
    // compilers of these tests does not yet take from an extension, are in Kept_TheSliceAndTheCountBehindARangeAreJudged.
    [TestMethod]
    public Task Kept_EverySymbolThatAnAllowedOperationCarriesIsJudged() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using System.Collections.Generic;
        using Singulink.CodeAnalysis;

        static class Tally
        {
            public static List<Guard>? Tallied;

            extension([Kept] List<Guard> guards)
            {
                public void operator +=(int count) => Tallied = guards;

                public void operator ++() => Tallied = guards;

                public void Register() => Tallied = guards;
            }
        }

        sealed class Handle
        {
            public static List<Guard>? Registered;

            public static readonly Handle None = new();

            public static implicit operator Handle([Kept] List<Guard> guards)
            {
                Registered = guards;
                return new Handle();
            }
        }

        struct Window
        {
            public static Memory<Guard> Last;

            public static implicit operator Window([Kept] Memory<Guard> window)
            {
                Last = window;
                return default;
            }
        }

        static class C
        {
            static void CompoundOfAnExtensionBlock(List<Guard> guards) => {|#0:guards|} += 1;

            static void IncrementOfAnExtensionBlock(List<Guard> guards) => {|#1:guards|}++;

            static void DeconstructionThatConverts(List<Guard> guards)
            {
                var pair = (guards, 1);
                (Handle handle, int count) = {|#2:pair|};
            }

            static void DeconstructionIntoVariables(List<Guard> guards)
            {
                var pair = (guards, 1);
                Handle handle;
                int count;
                (handle, count) = {|#3:pair|};
            }

            static void DeconstructionOfALiteral(List<Guard> guards)
            {
                (Handle handle, int count) = ({|#4:guards|}, 1);
            }

            static void DeconstructionInAForEach(List<(List<Guard> Guards, int Count)> pairs)
            {
                foreach ((Handle handle, int count) in {|#5:pairs|})
                {
                }
            }

            static void CoalesceThatConverts(List<Guard>? guards)
            {
                Handle handle = {|#6:guards|} ?? Handle.None;
            }

            static void LiftedConversion(Memory<Guard>? window)
            {
                Window? converted = {|#7:window|};
            }

            static void MethodGroup(List<Guard> guards)
            {
                Action register = {|#8:guards|}.Register;
                register();
            }

            // The same members that take the container in place keep nothing.
            static void Used(List<Guard> guards, IReadOnlyList<Guard> view, List<Guard>? maybe)
            {
                int count = guards.Count + view[0].Value;
                var other = maybe ?? guards;
                other.Clear();

                var (first, second) = (guards, 1);
                first.Clear();
            }
        }

        {{Sources.Guard}}
        """,
        Verify.KeepsUnmarkedParameter(0, "C.CompoundOfAnExtensionBlock(List<Guard>)", "guards", ListOfGuardsType),
        Verify.KeepsUnmarkedParameter(1, "C.IncrementOfAnExtensionBlock(List<Guard>)", "guards", ListOfGuardsType),
        Verify.KeepsUnmarkedParameter(2, "C.DeconstructionThatConverts(List<Guard>)", "guards", ListOfGuardsType),
        Verify.KeepsUnmarkedParameter(3, "C.DeconstructionIntoVariables(List<Guard>)", "guards", ListOfGuardsType),
        Verify.KeepsUnmarkedParameter(4, "C.DeconstructionOfALiteral(List<Guard>)", "guards", ListOfGuardsType),
        Verify.KeepsUnmarkedParameter(5, "C.DeconstructionInAForEach(List<(List<Guard> Guards, int Count)>)", "pairs", "System.Collections.Generic.List<(System.Collections.Generic.List<Guard> Guards, int Count)>"),
        Verify.KeepsUnmarkedParameter(6, "C.CoalesceThatConverts(List<Guard>?)", "guards", ListOfGuardsType),
        Verify.KeepsUnmarkedParameter(7, "C.LiftedConversion(Memory<Guard>?)", "window", "System.Memory<Guard>?"),
        Verify.KeepsUnmarkedParameter(8, "C.MethodGroup(List<Guard>)", "guards", ListOfGuardsType));

    // The collection that the compiler makes for a 'params' parameter is a collection expression: where its type is not an array or
    // a span, its builder or its 'Add' receives the elements, and it keeps them like one that is written out. And a spread of a
    // parameter that holds containers hands each of them to the collection that is made, as a 'foreach' that adds them does.
    [TestMethod]
    public Task Kept_ACollectionThatIsMadeKeepsItsElements() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using System.Collections;
        using System.Collections.Generic;
        using System.Runtime.CompilerServices;
        using Singulink.CodeAnalysis;

        [CollectionBuilder(typeof(Batch), nameof(Create))]
        sealed class Batch : IEnumerable<List<Guard>>
        {
            public static List<Guard>? First;

            public static Batch Create([Kept] ReadOnlySpan<List<Guard>> lists)
            {
                First = lists[0];
                return new Batch();
            }

            public IEnumerator<List<Guard>> GetEnumerator() => throw null!;

            IEnumerator IEnumerable.GetEnumerator() => throw null!;
        }

        sealed class Set : IEnumerable<List<Guard>>
        {
            public static List<Guard>? First;

            public void Add([Kept] List<Guard> guards) => First ??= guards;

            public IEnumerator<List<Guard>> GetEnumerator() => throw null!;

            IEnumerator IEnumerable.GetEnumerator() => throw null!;
        }

        static class C
        {
            static List<List<Guard>> s_all = new();

            static void Submit(params Batch batch)
            {
            }

            static void Enroll(params Set set)
            {
            }

            static int CountAll(params List<Guard>[] lists) => lists.Length;

            static int CountSpan(params ReadOnlySpan<List<Guard>> lists) => lists.Length;

            static void ThroughABuilder(List<Guard> guards) => Submit({|#0:guards|});

            static void ThroughAdd(List<Guard> guards) => Enroll({|#1:guards|});

            static void Spread(List<List<Guard>> lists) => s_all = [.. {|#2:lists|}];

            static void SpreadOfAnArray(List<Guard>[] lists)
            {
                List<List<Guard>> copy = [.. {|#3:lists|}];
                s_all = copy;
            }

            // An array or a span is how the elements are passed to the parameter, which keeps nothing here.
            static int ThroughAnArray(List<Guard> guards) => CountAll(guards) + CountSpan(guards);

            // A spread of what holds values copies each value, which is reported as that, and keeps no container.
            static void SpreadOfValues(List<int> numbers, List<Guard> guards)
            {
                int[] copy = [.. numbers];
                _ = guards.Count;
            }
        }

        {{Sources.Guard}}
        """,
        Verify.KeepsUnmarkedParameter(0, "C.ThroughABuilder(List<Guard>)", "guards", ListOfGuardsType),
        Verify.KeepsUnmarkedParameter(1, "C.ThroughAdd(List<Guard>)", "guards", ListOfGuardsType),
        Verify.KeepsUnmarkedParameter(2, "C.Spread(List<List<Guard>>)", "lists", "System.Collections.Generic.List<System.Collections.Generic.List<Guard>>"),
        Verify.KeepsUnmarkedParameter(3, "C.SpreadOfAnArray(List<Guard>[])", "lists", "System.Collections.Generic.List<Guard>[]"));

    // What checked code hands back where its return is not checked is the container under another name. The return of a getter is
    // not checked, and neither is that of a Memory<T>: so a getter or an indexer that is given a container, as an argument or as the
    // receiver of an extension block, hands back that container whatever the type of its result, and a task of a memory from an
    // 'async' function is judged like the memory.
    [TestMethod]
    public Task Kept_WhatAGetterOrAnAsyncFunctionHandsBackIsTheContainer() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using System.Collections.Generic;
        using System.Threading.Tasks;
        using Singulink.CodeAnalysis;

        static class Views
        {
            extension(Guard[] buffer)
            {
                public ArraySegment<Guard> Segment => buffer;
            }

            extension(List<Guard> guards)
            {
                public List<Guard> Self => guards;

                public IReadOnlyList<Guard> ReadOnly => guards;
            }
        }

        sealed class Index
        {
            public IReadOnlyList<Guard> this[List<Guard> key] => key;

            public ArraySegment<Guard> this[Guard[] key] => key;

            public (List<Guard> Key, int Hits) this[List<Guard> key, int hits] => (key, hits);
        }

        // A member of the container's own type hands out what the object holds.
        sealed class Waiter : List<Guard>
        {
            public Waiter? Next { get; set; }
        }

        delegate Task<Memory<Guard>> Windower(Guard[] buffer);

        interface IWindows
        {
            Task<Memory<Guard>> WindowAsync(Guard[] buffer);
        }

        static class C
        {
            static readonly Index s_index = new();
            static readonly Windower s_windower = async buffer => buffer;
            static ArraySegment<Guard> s_segment;
            static List<Guard>? s_kept;
            static IReadOnlyList<Guard>? s_view;
            static (List<Guard> Key, int Hits) s_entry;
            static Task<Memory<Guard>>? s_window;
            static ValueTask<Memory<Guard>> s_valueWindow;
            static Memory<Guard> s_memory;
            static Waiter? s_head;

            static async Task<Memory<Guard>> WindowAsync(Guard[] buffer) => buffer.AsMemory(0, 1);

            static async ValueTask<Memory<Guard>> WindowValueAsync(Guard[] buffer) => buffer;

            static void ExtensionProperty(Guard[] buffer) => s_segment = {|#0:buffer|}.Segment;

            static void ExtensionPropertyOfTheSameType(List<Guard> guards) => s_kept = {|#1:guards|}.Self;

            static void ExtensionPropertyOfAnInterface(List<Guard> guards) => s_view = {|#2:guards|}.ReadOnly;

            static void Indexer(List<Guard> guards) => s_view = s_index[{|#3:guards|}];

            static void IndexerOfAView(Guard[] buffer) => s_segment = s_index[{|#4:buffer|}];

            static void IndexerOfATuple(List<Guard> guards) => s_entry = s_index[{|#5:guards|}, 1];

            static void TaskOfAMemory(Guard[] buffer) => s_window = WindowAsync({|#6:buffer|});

            static void ValueTaskOfAMemory(Guard[] buffer) => s_valueWindow = WindowValueAsync({|#7:buffer|});

            static void FromALocalFunction(Guard[] buffer)
            {
                s_window = Local({|#8:buffer|});

                static async Task<Memory<Guard>> Local(Guard[] buffer) => buffer;
            }

            static void FromADelegate(Guard[] buffer) => s_window = s_windower({|#9:buffer|});

            static void ThroughAnInterface(IWindows windows, Guard[] buffer) => s_window = windows.WindowAsync({|#10:buffer|});

            static async Task Awaited(Guard[] buffer) => s_memory = await WindowAsync({|#11:buffer|});

            static Guard[] OfALocal(RwLock rwLock)
            {
                var buffer = new Guard[1];
                buffer[0] = rwLock.Enter();
                s_window = WindowAsync(buffer);
                return {|#12:buffer|};
            }

            // Used where it is made, nothing is left of it.
            static async Task<int> UsedOnTheSpot(Guard[] buffer, List<Guard> guards) =>
                buffer.Segment.Count + guards.ReadOnly.Count + s_index[guards].Count + (await WindowAsync(buffer)).Length;

            static void OfItsOwnType(Waiter waiter) => s_head = waiter.Next;
        }

        {{Sources.Guard}}
        """,
        Verify.KeepsUnmarkedParameter(0, "C.ExtensionProperty(Guard[])", "buffer", "Guard[]"),
        Verify.KeepsUnmarkedParameter(1, "C.ExtensionPropertyOfTheSameType(List<Guard>)", "guards", ListOfGuardsType),
        Verify.KeepsUnmarkedParameter(2, "C.ExtensionPropertyOfAnInterface(List<Guard>)", "guards", ListOfGuardsType),
        Verify.KeepsUnmarkedParameter(3, "C.Indexer(List<Guard>)", "guards", ListOfGuardsType),
        Verify.KeepsUnmarkedParameter(4, "C.IndexerOfAView(Guard[])", "buffer", "Guard[]"),
        Verify.KeepsUnmarkedParameter(5, "C.IndexerOfATuple(List<Guard>)", "guards", ListOfGuardsType),
        Verify.KeepsUnmarkedParameter(6, "C.TaskOfAMemory(Guard[])", "buffer", "Guard[]"),
        Verify.KeepsUnmarkedParameter(7, "C.ValueTaskOfAMemory(Guard[])", "buffer", "Guard[]"),
        Verify.KeepsUnmarkedParameter(8, "C.FromALocalFunction(Guard[])", "buffer", "Guard[]"),
        Verify.KeepsUnmarkedParameter(9, "C.FromADelegate(Guard[])", "buffer", "Guard[]"),
        Verify.KeepsUnmarkedParameter(10, "C.ThroughAnInterface(IWindows, Guard[])", "buffer", "Guard[]"),
        Verify.KeepsUnmarkedParameter(11, "C.Awaited(Guard[])", "buffer", "Guard[]"),
        Verify.UnownedContainerReturned(
            12,
            "C.OfALocal(RwLock)",
            "Guard[]",
            because: "'buffer' is not the method's own anywhere in the method, because on line 93 it is passed to 'C.WindowAsync(Guard[])'"));

    // 'with' gives a copy of what it is applied to, which holds what that holds.
    [TestMethod]
    public Task Kept_WithGivesACopyThatHoldsTheContainer() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using System.Collections.Generic;
        using Singulink.CodeAnalysis;

        static class C
        {
            static Memory<Guard> s_pending;
            static ArraySegment<Guard> s_segment;
            static (List<Guard> Guards, int Count) s_entry;

            static void OfAMemory(Guard[] buffer)
            {
                Memory<Guard> window = buffer;
                s_pending = {|#0:window|} with { };
            }

            static void OfASegment(Guard[] buffer)
            {
                ArraySegment<Guard> segment = buffer;
                s_segment = {|#1:segment|} with { };
            }

            static void OfATuple(List<Guard> guards)
            {
                var entry = (Guards: guards, Count: 0);
                s_entry = {|#2:entry|} with { Count = 1 };
            }

            // The copy stays in the method.
            static int Used(List<Guard> guards)
            {
                var entry = (Guards: guards, Count: 0);
                var next = entry with { Count = 1 };
                return next.Count + next.Guards.Count;
            }
        }

        {{Sources.Guard}}
        """,
        Verify.KeepsUnmarkedParameter(0, "C.OfAMemory(Guard[])", "buffer", "Guard[]"),
        Verify.KeepsUnmarkedParameter(1, "C.OfASegment(Guard[])", "buffer", "Guard[]"),
        Verify.KeepsUnmarkedParameter(2, "C.OfATuple(List<Guard>)", "guards", ListOfGuardsType));

    // A 'fixed' statement has no kind of operation of its own, so it is not on the list of what a container parameter may be used
    // in: the 'GetPinnableReference' that it binds, also from an extension, is not followed, and the use is reported as that.
    [TestMethod]
    public Task Kept_AFixedStatementIsNotOnTheList() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using System.Collections.Generic;
        using Singulink.CodeAnalysis;

        static class Pins
        {
            public static List<Guard>? Pinned;

            public static ref Guard GetPinnableReference([Kept] this List<Guard> guards)
            {
                Pinned = guards;
                throw null!;
            }
        }

        static unsafe class C
        {
            static void Pin(List<Guard> guards)
            {
                fixed (Guard* first = {|#0:guards|})
                {
                }
            }
        }

        {{Sources.Guard}}
        """,
        Verify.ParameterUsedInAWayNotFollowed(0, "C.Pin(List<Guard>)", "guards", ListOfGuardsType));

#if NET11_0_OR_GREATER
    // The 'Slice' and the count behind a range and an index from the end, which the compiler of the newer SDK takes from an extension
    // block as well. Each takes the container as its receiver where nothing but what the compiler bound shows it, and is judged like
    // a member that is written out: one that keeps the container needs the mark on the parameter that is passed.
    [TestMethod]
    public Task Kept_TheSliceAndTheCountBehindARangeAreJudged() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using System.Collections.Generic;
        using Singulink.CodeAnalysis;

        static class Keeping
        {
            public static IReadOnlyList<Guard>? Sliced;
            public static IEnumerable<Guard>? Counted;

            extension([Kept] IReadOnlyList<Guard> guards)
            {
                [return: Borrowed]
                public IReadOnlyList<Guard> Slice(int start, int length)
                {
                    Sliced = guards;
                    return guards;
                }
            }

            extension([Kept] IEnumerable<Guard> guards)
            {
                public int Count
                {
                    get
                    {
                        Counted = guards;
                        return 0;
                    }
                }

                public int this[int index] => index;
            }
        }

        static class Using
        {
            extension(IList<Guard> guards)
            {
                [return: Borrowed]
                public IList<Guard> Slice(int start, int length) => guards;
            }

            extension(ICollection<Guard> guards)
            {
                public int Length => guards.Count;

                public int this[int index] => index;
            }
        }

        static class C
        {
            static void RangeThatKeeps(IReadOnlyList<Guard> guards)
            {
                var tail = {|#0:guards|}[1..];
            }

            static void IndexFromTheEndThatKeeps(IEnumerable<Guard> guards)
            {
                int last = {|#1:guards|}[^1];
            }

            // The same members that only use the container keep nothing.
            static void Used(IList<Guard> list, ICollection<Guard> collection)
            {
                var tail = list[1..];
                int last = collection[^1];
            }
        }

        {{Sources.Guard}}
        """,
        Verify.KeepsUnmarkedParameter(0, "C.RangeThatKeeps(IReadOnlyList<Guard>)", "guards", "System.Collections.Generic.IReadOnlyList<Guard>"),
        Verify.KeepsUnmarkedParameter(1, "C.IndexFromTheEndThatKeeps(IEnumerable<Guard>)", "guards", "System.Collections.Generic.IEnumerable<Guard>"));
#endif

    // Every pattern inside a pattern binds its members too, and so does every link of a chain: a container that reaches a nested
    // pattern through a tuple, or through what a member of it hands back, is given to the members that the pattern runs there.
    // What a member of a pattern hands back of the container is not followed, so 'Same', which hands it back, is a use of that
    // kind wherever it stands in a pattern.
    [TestMethod]
    public Task Kept_TheMembersOfANestedPatternAndOfAChainAreJudged() => Verify.AnalyzerAsync(
        $$"""
        using System.Collections.Generic;
        using Singulink.CodeAnalysis;

        static class K
        {
            public static List<Guard>? List;

            extension([Kept] List<Guard> guards)
            {
                public int Tag
                {
                    get
                    {
                        List = guards;
                        return 0;
                    }
                }

                public void Deconstruct(out int a, out int b)
                {
                    List = guards;
                    a = b = 0;
                }
            }

            extension(List<Guard> guards)
            {
                [return: Borrowed]
                public List<Guard> Same => guards;
            }
        }

        static class C
        {
            static bool PropertyInAPart(List<Guard> guards) => ({|#0:guards|}, 1) is ({ Tag: 1 }, _);

            static bool DeconstructionInAPart(List<Guard> guards) => ({|#1:guards|}, 1) is ((var a, var b), _);

            static void InASwitch(List<Guard> guards)
            {
                switch (({|#2:guards|}, 1))
                {
                    case ({ Tag: 1 }, _):
                        break;
                }
            }

            static bool ALaterLinkOfAChain(List<Guard> guards) => {|#3:guards|} is { Same.Tag: 1 };

            static bool APatternOfAProperty(List<Guard> guards) => {|#4:guards|} is { Same: { Tag: 1 } };

            static bool BehindNot(List<Guard> guards) => ({|#5:guards|}, 1) is not ({ Tag: 2 }, _) or (_, 2);

            static bool AnElementOfAList(List<Guard>[] lists) => {|#6:lists|} is [{ Tag: 1 }, ..];

            // A field of a tuple runs no member, and what a pattern of it, or the next link of a chain, runs is collected.
            static bool APatternOfAField(List<Guard> guards) => ({|#7:guards|}, 1) is { Item1: { Tag: 1 } };

            static bool AChainThroughAField(List<Guard> guards) => ({|#8:guards|}, 1) is { Item1.Tag: 1 };

            // A pattern that runs no member of the container keeps nothing.
            static bool Used(List<Guard> guards) => (guards, 1) is ({ Count: 1 }, _) && guards is { Count: 0 };
        }

        {{Sources.Guard}}
        """,
        Verify.KeepsUnmarkedParameter(0, "C.PropertyInAPart(List<Guard>)", "guards", ListOfGuardsType),
        Verify.KeepsUnmarkedParameter(1, "C.DeconstructionInAPart(List<Guard>)", "guards", ListOfGuardsType),
        Verify.KeepsUnmarkedParameter(2, "C.InASwitch(List<Guard>)", "guards", ListOfGuardsType),
        Verify.ParameterUsedInAWayNotFollowed(3, "C.ALaterLinkOfAChain(List<Guard>)", "guards", ListOfGuardsType),
        Verify.ParameterUsedInAWayNotFollowed(4, "C.APatternOfAProperty(List<Guard>)", "guards", ListOfGuardsType),
        Verify.KeepsUnmarkedParameter(5, "C.BehindNot(List<Guard>)", "guards", ListOfGuardsType),
        Verify.KeepsUnmarkedParameter(6, "C.AnElementOfAList(List<Guard>[])", "lists", "System.Collections.Generic.List<Guard>[]"),
        Verify.KeepsUnmarkedParameter(7, "C.APatternOfAField(List<Guard>)", "guards", ListOfGuardsType),
        Verify.KeepsUnmarkedParameter(8, "C.AChainThroughAField(List<Guard>)", "guards", ListOfGuardsType));

    // What an operation hands back is described for every kind of operation: an operator that returns a Memory<T> of its operand
    // hands back the array under another name, as a call does, and so does a member that is given the container by reference with
    // no argument written (the receiver of an extension block, an 'in' parameter of an indexer) and returns that reference.
    [TestMethod]
    public Task Kept_WhatAnOperatorOrAReferenceReturningMemberHandsBackIsFollowed() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using Singulink.CodeAnalysis;

        sealed class Tag
        {
            public static readonly Tag X = new();

            public static Memory<Guard> operator +(Guard[] buffer, Tag tag) => buffer;
        }

        static class Views
        {
            extension(Guard[] buffer)
            {
                public static Memory<Guard> operator ~(Guard[] all) => all;
            }

            extension(ref ArraySegment<Guard> segment)
            {
                public ref ArraySegment<Guard> Self => ref segment;

                public ref ArraySegment<Guard> SelfOf() => ref segment;
            }
        }

        sealed class InIndex
        {
            public ref readonly Guard[] this[in Guard[] key] => ref key;
        }

        static class K
        {
            public static Memory<Guard> Memory;
            public static ArraySegment<Guard> Segment;
            public static Guard[]? Array;
            public static readonly InIndex Index = new();
        }

        static class C
        {
            static void ByABinaryOperator(Guard[] buffer) => K.Memory = {|#0:buffer|} + Tag.X;

            static void ByAUnaryOperator(Guard[] buffer) => K.Memory = ~{|#1:buffer|};

            static void ByARefReceiverOfAProperty(Guard[] buffer)
            {
                ArraySegment<Guard> segment = buffer;
                K.Segment = {|#2:segment|}.Self;
            }

            static void ByARefReceiverOfAMethod(Guard[] buffer)
            {
                ArraySegment<Guard> segment = buffer;
                K.Segment = {|#3:segment|}.SelfOf();
            }

            static void ByAnInParameterOfAnIndexer(Guard[] buffer) => K.Array = K.Index[in {|#4:buffer|}];

            // What is made and used where it is made keeps nothing.
            static int Used(Guard[] buffer)
            {
                Memory<Guard> window = buffer + Tag.X;
                ArraySegment<Guard> segment = buffer;
                return window.Length + segment.Self.Count + K.Index[in buffer].Length;
            }
        }

        {{Sources.Guard}}
        """,
        Verify.KeepsUnmarkedParameter(0, "C.ByABinaryOperator(Guard[])", "buffer", "Guard[]"),
        Verify.KeepsUnmarkedParameter(1, "C.ByAUnaryOperator(Guard[])", "buffer", "Guard[]"),
        Verify.KeepsUnmarkedParameter(2, "C.ByARefReceiverOfAProperty(Guard[])", "buffer", "Guard[]"),
        Verify.KeepsUnmarkedParameter(3, "C.ByARefReceiverOfAMethod(Guard[])", "buffer", "Guard[]"),
        Verify.KeepsUnmarkedParameter(4, "C.ByAnInParameterOfAnIndexer(Guard[])", "buffer", "Guard[]"));
}
