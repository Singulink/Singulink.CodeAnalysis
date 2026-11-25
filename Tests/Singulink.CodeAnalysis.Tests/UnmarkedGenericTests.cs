using PrefixClassName.MsTest;

namespace Singulink.CodeAnalysis.Tests;

/// <summary>
/// Tests of the rule for generics whose type parameter is not marked: a member that is used with a non-copyable type is reported unless it is on
/// the safe list.
/// </summary>
/// <remarks>
/// Docs: articles/guides/marking-a-type-parameter.md, "Generics That Are Not Marked".
/// </remarks>
[PrefixTestClass]
public class UnmarkedGenericTests
{
    // An unmarked generic with every kind of member, none of which is listed.
    private const string Bag = """
        public class Bag<T> : System.IDisposable, System.Collections.IEnumerable
        {
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => throw null!;
            public Bag() { }
            public Bag(int capacity) { }
            public T First => default!;
            public T this[int index] => default!;
            public ref T GetRef(int index) => throw null!;
            public T Peek() => default!;
            public void Add(T value) { }
            public void Dispose() { }
            public event System.Action<T> Added { add { } remove { } }
            public Enumerator GetEnumerator() => default;
            public static Bag<T> operator +(Bag<T> left, Bag<T> right) => left;
            public static implicit operator Bag<T>(T value) => new();
            public void Deconstruct(out T first, out int count) { first = default!; count = 0; }

            public struct Enumerator
            {
                public T Current => default!;
                public bool MoveNext() => false;
            }
        }
        """;

    [TestMethod]
    public Task MemberOfUnmarkedGeneric_IsReportedUnlessListed() => Verify.AnalyzerAsync(
        $$"""
        using System.Collections.Generic;

        class C
        {
            static T Dup<T>(in T value) => value;

            void M(RwLock rwLock)
            {
                var a = rwLock.Enter();

                var list = new List<Guard>();
                list.Add(rwLock.Enter());
                int count = list.Count;
                Guard[] all = {|#0:list.ToArray()|};
                {|#1:list.Sort()|};
                Guard b = {|#2:Dup(in a)|};
            }

            // The trigger is the use of a member, not the naming of a type.
            List<Guard> _field;

            System.Threading.Tasks.ValueTask<Guard> Return(RwLock rwLock) => rwLock.EnterAsync();

            System.Type Type() => typeof(List<Guard>);

            void Parameter(List<Guard> list, Dictionary<int, Guard> map) { }
        }

        {{Sources.Guard}}
        """,
        Verify.UnlistedMember(0, "List<T>.ToArray()", "Guard", "T", "M:System.Collections.Generic.List`1.ToArray"),
        Verify.UnlistedMember(1, "List<T>.Sort()", "Guard", "T", "M:System.Collections.Generic.List`1.Sort"),
        Verify.UnlistedMember(2, "C.Dup<T>(in T)", "Guard", "T", "M:C.Dup``1(``0@)"));

    [TestMethod]
    public Task EveryKindOfUse_IsReported() => Verify.AnalyzerAsync(
        $$"""
        using System;

        class C
        {
            void M(Bag<Guard> bag, RwLock rwLock)
            {
                var created = {|#0:new Bag<Guard>()|};
                Guard first = {|#1:bag.First|};
                Guard indexed = {|#2:bag[0]|};
                ref Guard reference = ref {|#3:bag.GetRef(0)|};
                Guard peeked = {|#4:bag.Peek()|};
                {|#5:bag.Add(rwLock.Enter())|};
                {|#6:bag.Added|} += g => { };
                Func<Guard> peek = {|#7:bag.Peek|};
                var sum = {|#8:bag + bag|};
                Bag<Guard> converted = {|#9:rwLock.Enter()|};
            }

            // A generic that is used with a copyable type is not reported.
            void N(Bag<int> bag)
            {
                var created = new Bag<int>();
                int first = bag.First;
                bag.Add(1);
            }
        }

        {{Bag}}

        {{Sources.Guard}}
        """,
        Verify.UnlistedMember(0, "Bag<T>.Bag()", "Guard", "T", "M:Bag`1.#ctor"),
        Verify.UnlistedMember(1, "Bag<T>.First", "Guard", "T", "P:Bag`1.First"),
        Verify.UnlistedMember(2, "Bag<T>.this[int]", "Guard", "T", "P:Bag`1.Item(System.Int32)"),
        Verify.UnlistedMember(3, "Bag<T>.GetRef(int)", "Guard", "T", "M:Bag`1.GetRef(System.Int32)"),
        Verify.UnlistedMember(4, "Bag<T>.Peek()", "Guard", "T", "M:Bag`1.Peek"),
        Verify.UnlistedMember(5, "Bag<T>.Add(T)", "Guard", "T", "M:Bag`1.Add(`0)"),
        Verify.UnlistedMember(6, "Bag<T>.Added", "Guard", "T", "E:Bag`1.Added"),
        Verify.UnlistedMember(7, "Bag<T>.Peek()", "Guard", "T", "M:Bag`1.Peek"),
        Verify.UnlistedMember(8, "Bag<T>.operator +(Bag<T>, Bag<T>)", "Guard", "T", "M:Bag`1.op_Addition(Bag{`0},Bag{`0})"),
        Verify.UnlistedMember(9, "Bag<T>.implicit operator Bag<T>(T)", "Guard", "T", "M:Bag`1.op_Implicit(`0)~Bag{`0}"));

    [TestMethod]
    public Task MembersThatTheCompilerCalls_AreReported() => Verify.AnalyzerAsync(
        $$"""
        using System.Runtime.CompilerServices;
        using System.Threading.Tasks;

        class C
        {
            static Bag<Guard> CreateBag() => throw null!;

            static Future<Guard> CreateFuture() => throw null!;

            async Task M(RwLock rwLock)
            {
                foreach (Guard item in {|#0:CreateBag()|}) { }

                Guard awaited = {|#1:await CreateFuture()|};

                var initialized = {|#2:new Bag<Guard>(1) { {|#3:rwLock.Enter()|} }|};

                var (first, count) = {|#4:CreateBag()|};

                if (CreateBag() is {|#5:(var matched, 0)|}) { }

                using (var used = {|#6:CreateBag()|}) { }

                using var declared = {|#7:CreateBag()|};
            }
        }

        public class Future<T>
        {
            public Awaiter GetAwaiter() => default;

            public struct Awaiter : INotifyCompletion
            {
                public bool IsCompleted => true;
                public T GetResult() => default!;
                public void OnCompleted(System.Action continuation) { }
            }
        }

        {{Bag}}

        {{Sources.Guard}}
        """,
        Verify.UnlistedMember(0, "Bag<T>.Enumerator.Current", "Guard", "T", "P:Bag`1.Enumerator.Current"),
        Verify.UnlistedMember(0, "Bag<T>.Enumerator.MoveNext()", "Guard", "T", "M:Bag`1.Enumerator.MoveNext"),
        Verify.UnlistedMember(0, "Bag<T>.GetEnumerator()", "Guard", "T", "M:Bag`1.GetEnumerator"),
        Verify.NonCopyableAwaiter(1, "Future<Guard>.Awaiter"),
        Verify.UnlistedMember(1, "Future<T>.Awaiter.GetResult()", "Guard", "T", "M:Future`1.Awaiter.GetResult"),
        Verify.UnlistedMember(1, "Future<T>.Awaiter.IsCompleted", "Guard", "T", "P:Future`1.Awaiter.IsCompleted"),
        Verify.UnlistedMember(1, "Future<T>.Awaiter.OnCompleted(Action)", "Guard", "T", "M:Future`1.Awaiter.OnCompleted(System.Action)"),
        Verify.UnlistedMember(1, "Future<T>.GetAwaiter()", "Guard", "T", "M:Future`1.GetAwaiter"),
        Verify.UnlistedMember(2, "Bag<T>.Bag(int)", "Guard", "T", "M:Bag`1.#ctor(System.Int32)"),
        Verify.UnlistedMember(3, "Bag<T>.Add(T)", "Guard", "T", "M:Bag`1.Add(`0)"),
        Verify.UnlistedMember(4, "Bag<T>.Deconstruct(out T, out int)", "Guard", "T", "M:Bag`1.Deconstruct(`0@,System.Int32@)"),
        Verify.UnlistedMember(5, "Bag<T>.Deconstruct(out T, out int)", "Guard", "T", "M:Bag`1.Deconstruct(`0@,System.Int32@)"),
        Verify.UnlistedMember(6, "Bag<T>.Dispose()", "Guard", "T", "M:Bag`1.Dispose"),
        Verify.UnlistedMember(7, "Bag<T>.Dispose()", "Guard", "T", "M:Bag`1.Dispose"));

    [TestMethod]
    public Task OriginalDefinitionDecides() => Verify.AnalyzerAsync(
        $$"""
        using System.Collections.Generic;

        class GuardBag : Bag<Guard> { }

        static class Extensions
        {
            public static T Last<T>(this List<T> list) => list[^1];

            public static void Touch<T>(this ref T value) where T : struct { }
        }

        class C
        {
            void M(GuardBag guardBag, List<Guard> list, Bag<Guard>.Enumerator enumerator, RwLock rwLock)
            {
                // An inherited member of a generic base.
                Guard peeked = {|#0:guardBag.Peek()|};

                // An extension method. Its parameter mentions the type parameter, which is not marked, so nothing checks what
                // the method does with the list. The use is reported as that, and not again as keeping the list.
                Guard last = {|#1:list.Last()|};
                var guard = rwLock.Enter();
                {|#2:guard.Touch()|};

                // A member of a nested type of a generic type.
                bool moved = {|#3:enumerator.MoveNext()|};
            }
        }

        {{Bag}}

        {{Sources.Guard}}
        """,
        Verify.UnlistedMember(0, "Bag<T>.Peek()", "Guard", "T", "M:Bag`1.Peek"),
        Verify.UnlistedMember(1, "Extensions.Last<T>(List<T>)", "Guard", "T", "M:Extensions.Last``1(System.Collections.Generic.List{``0})"),
        Verify.UnlistedMember(2, "Extensions.Touch<T>(ref T)", "Guard", "T", "M:Extensions.Touch``1(``0@)"),
        Verify.UnlistedMember(3, "Bag<T>.Enumerator.MoveNext()", "Guard", "T", "M:Bag`1.Enumerator.MoveNext"));

    [TestMethod]
    public Task EachTypeParameter_IsJudgedAlone() => Verify.AnalyzerAsync(
        $$"""
        using Singulink.CodeAnalysis;

        public class Map<TKey, [NonCopyable] TValue>
        {
            public ref TValue Get(TKey key) => throw null!;

            public void Convert<TOther, [NonCopyable] TMarked>(TOther other, ref TMarked marked) { }
        }

        class C
        {
            void M(Map<int, Guard> byNumber, Map<Guard, int> byGuard, RwLock rwLock)
            {
                ref Guard value = ref byNumber.Get(1);
                ref int number = ref {|#0:byGuard.Get(rwLock.Enter())|};

                var guard = rwLock.Enter();
                byNumber.Convert(1, ref guard);
                {|#1:byNumber.Convert(rwLock.Enter(), ref guard)|};
            }
        }

        {{Sources.Guard}}
        """,
        Verify.UnlistedMember(0, "Map<TKey, TValue>.Get(TKey)", "Guard", "TKey", "M:Map`2.Get(`0)"),
        Verify.UnlistedMember(
            1, "Map<TKey, TValue>.Convert<TOther, TMarked>(TOther, ref TMarked)", "Guard", "TOther", "M:Map`2.Convert``2(``0,``1@)"));

    [TestMethod]
    public Task Interfaces_GetNoExemption() => Verify.AnalyzerAsync(
        $$"""
        using System.Collections.Generic;

        interface ISource<T>
        {
            T Next();
        }

        class C
        {
            void M(ISource<Guard> source, ICollection<Guard> collection)
            {
                Guard next = {|#0:source.Next()|};
                bool contains = {|#1:collection.Contains(default)|};
            }
        }

        {{Sources.Guard}}
        """,
        Verify.UnlistedMember(0, "ISource<T>.Next()", "Guard", "T", "M:ISource`1.Next"),
        Verify.UnlistedMember(1, "ICollection<T>.Contains(T)", "Guard", "T", "M:System.Collections.Generic.ICollection`1.Contains(`0)"));

    [TestMethod]
    public Task Delegates_AreExemptAndTheirTargetIsCheckedWhereTheyAreCreated() => Verify.AnalyzerAsync(
        $$"""
        using System;

        delegate T Factory<T>();

        class C
        {
            void M(RwLock rwLock, Bag<Guard> box)
            {
                Func<Guard> enter = rwLock.Enter;
                Guard g = enter();
                Func<Guard> get = {|#0:box.Peek|};

                Func<Guard> lambda = () => rwLock.Enter();
                Guard fromLambda = lambda.Invoke();
                Factory<Guard> factory = rwLock.Enter;
                Guard fromFactory = factory();
                Action<Guard> consume = guard => guard.Dispose();
                consume(rwLock.Enter());
                consume({|#1:g|});
                consume({|#2:g|});
                var created = new Func<Guard>(rwLock.Enter);
            }
        }

        {{Bag}}

        {{Sources.Guard}}
        """,
        Verify.UnlistedMember(0, "Bag<T>.Peek()", "Guard", "T", "M:Bag`1.Peek"),
        Verify.UsedAfterHandOff(2, "g", "Guard", "'Action<Guard>.Invoke(Guard)' received its value", 1));

    [TestMethod]
    public Task ContainerBoundToUnmarkedTypeParameter_IsTheOpaqueCase() => Verify.AnalyzerAsync(
        $$"""
        using System.Collections.Generic;
        using System.Threading.Tasks;
        using Singulink.CodeAnalysis;

        class C
        {
            static T Identity<T>(T value) => value;

            static T Checked<[NonCopyable] T>(ref T value) => NonCopyable.Move(ref value);

            void M([Kept] List<Guard> list)
            {
                // Listed members.
                var task = Task.FromResult(list);
                var lists = new List<Guard[]>();

                var same = {|#0:Identity(list)|};
                var bag = {|#1:new Bag<List<Guard>>()|};

                // A container can be bound to a marked type parameter.
                var moved = Checked(ref list);
            }
        }

        {{Bag}}

        {{Sources.Guard}}
        """,
        Verify.OpaqueTypeArgument(0, "C.Identity<T>(T)", "System.Collections.Generic.List<Guard>", "T", "M:C.Identity``1(``0)"),
        Verify.OpaqueTypeArgument(1, "Bag<T>.Bag()", "System.Collections.Generic.List<Guard>", "T", "M:Bag`1.#ctor"));

    // Where the awaiter has 'UnsafeOnCompleted', that is what the compiler hands the rest of the method to, and 'OnCompleted' is
    // not used.
    [TestMethod]
    public Task Await_UsesUnsafeOnCompletedWhereTheAwaiterHasIt() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using System.Runtime.CompilerServices;
        using System.Threading.Tasks;

        public class Critical<T>
        {
            public Awaiter GetAwaiter() => new Awaiter();

            public sealed class Awaiter : ICriticalNotifyCompletion
            {
                public bool IsCompleted => false;

                public void OnCompleted(Action continuation) => continuation();

                public void UnsafeOnCompleted(Action continuation) => continuation();

                public int GetResult() => 0;
            }
        }

        class C
        {
            static Critical<Guard> Create() => throw null!;

            async Task M()
            {
                int awaited = {|#0:await Create()|};
            }
        }

        {{Sources.Guard}}
        """,
        Verify.UnlistedMember(0, "Critical<T>.Awaiter.GetResult()", "Guard", "T", "M:Critical`1.Awaiter.GetResult"),
        Verify.UnlistedMember(0, "Critical<T>.Awaiter.IsCompleted", "Guard", "T", "P:Critical`1.Awaiter.IsCompleted"),
        Verify.UnlistedMember(0, "Critical<T>.Awaiter.UnsafeOnCompleted(Action)", "Guard", "T", "M:Critical`1.Awaiter.UnsafeOnCompleted(System.Action)"),
        Verify.UnlistedMember(0, "Critical<T>.GetAwaiter()", "Guard", "T", "M:Critical`1.GetAwaiter"));
}
