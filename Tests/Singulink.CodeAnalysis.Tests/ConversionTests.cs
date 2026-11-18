using PrefixClassName.MsTest;

namespace Singulink.CodeAnalysis.Tests;

/// <summary>
/// Tests of conversions that lose the non-copyable type: a container that is used as an untyped collection or as an opaque type.
/// </summary>
/// <remarks>
/// Docs: articles/concepts/conversions.md.
/// </remarks>
[PrefixTestClass]
public class ConversionTests
{
    [TestMethod]
    public Task ContainerConvertedToATypeThatLosesTheNonCopyableType_IsReported() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using System.Collections;
        using System.Collections.Generic;
        using System.Threading.Tasks;

        class C
        {
            void M(RwLock rwLock, List<Guard> list, List<Guard> other, Guard[] guards)
            {
                IReadOnlyList<Guard> typed = list;
                IEnumerable<Guard> enumerable = list;
                IEnumerable untyped = {|#0:list|};
                object boxed = {|#1:list|};

                Guard[] clone = (Guard[]){|#3:guards.Clone()|};
                int length = guards.Length;

                ArgumentNullException.ThrowIfNull(list);
                bool same = ReferenceEquals(list, other);
            }
        }

        {{Sources.Guard}}
        """,
        Verify.UntypedCollectionConversion(0, "System.Collections.Generic.List<Guard>", "System.Collections.IEnumerable"),
        Verify.OpaqueConversion(1, "System.Collections.Generic.List<Guard>", "object"),
        Verify.UntypedCollectionMember(3, "Guard[]", "System.Array", "Array.Clone()", "M:System.Array.Clone"));

    [TestMethod]
    public Task UntypedCollections_AndOpaqueTypes() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using System.Collections;
        using System.Collections.Generic;
        using System.Threading.Tasks;

        class C
        {
            void Untyped(List<Guard> list, Guard[] guards, Dictionary<int, Guard> map)
            {
                Array array = {|#0:guards|};
                ICollection collection = {|#1:list|};
                IList untypedList = {|#2:guards|};
                IDictionary dictionary = {|#3:map|};
                IStructuralEquatable equatable = {|#4:guards|};
                IStructuralComparable comparable = {|#5:guards|};
                var cast = (IEnumerable){|#6:list|};
                var tryCast = {|#7:list|} as IEnumerable;
            }

            void Opaque(List<Guard> list, Guard[] guards, Func<Guard> factory, Memory<Guard> memory, Store<Guard> store)
            {
                dynamic dynamicList = {|#8:list|};
                Delegate untypedDelegate = {|#9:factory|};
                ICloneable cloneable = {|#10:guards|};
                object boxedView = {|#11:memory|};
                StoreBase plain = {|#12:store|};
                IEnumerator enumerator = {|#13:((IEnumerable<Guard>)list).GetEnumerator()|};
            }

            // The non-copyable type stays visible, so nothing is lost and the members of the target stay controlled.
            void Kept(List<Guard> list, Guard[] guards, Dictionary<int, Guard> map, object boxed)
            {
                IList<Guard> typedList = guards;
                ICollection<KeyValuePair<int, Guard>> pairs = map;
                IReadOnlyDictionary<int, Guard> readOnlyMap = map;
                var back = (List<Guard>)boxed;
                List<Guard> none = null;
                Guard[] defaulted = default;
            }
        }

        class StoreBase
        {
            public void Flush() { }
        }

        class Store<T> : StoreBase { }

        {{Sources.Guard}}
        """,
        Verify.UntypedCollectionConversion(0, "Guard[]", "System.Array"),
        Verify.UntypedCollectionConversion(1, "System.Collections.Generic.List<Guard>", "System.Collections.ICollection"),
        Verify.UntypedCollectionConversion(2, "Guard[]", "System.Collections.IList"),
        Verify.UntypedCollectionConversion(3, "System.Collections.Generic.Dictionary<int, Guard>", "System.Collections.IDictionary"),
        Verify.UntypedCollectionConversion(4, "Guard[]", "System.Collections.IStructuralEquatable"),
        Verify.UntypedCollectionConversion(5, "Guard[]", "System.Collections.IStructuralComparable"),
        Verify.UntypedCollectionConversion(6, "System.Collections.Generic.List<Guard>", "System.Collections.IEnumerable"),
        Verify.UntypedCollectionConversion(7, "System.Collections.Generic.List<Guard>", "System.Collections.IEnumerable"),
        Verify.OpaqueConversion(8, "System.Collections.Generic.List<Guard>", "dynamic"),
        Verify.OpaqueConversion(9, "System.Func<Guard>", "System.Delegate"),
        Verify.OpaqueConversion(10, "Guard[]", "System.ICloneable"),
        Verify.OpaqueConversion(11, "System.Memory<Guard>", "object"),
        Verify.OpaqueConversion(12, "Store<Guard>", "StoreBase"),
        Verify.OpaqueConversion(13, "System.Collections.Generic.IEnumerator<Guard>", "System.Collections.IEnumerator"));

    [TestMethod]
    public Task ErasableType_CanBeConvertedAndIsStoredWhenItComesBack() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using System.Collections;
        using System.Collections.Generic;
        using System.Threading.Tasks;

        class C
        {
            static void ShouldBeFaulted(Task task) { }

            static T Identity<T>(T value) => value;

            static object GetObject() => null!;

            static Task GetTask() => null!;

            async Task M(RwLock rwLock, [Singulink.CodeAnalysis.Kept] List<Guard> list, Task<Guard>[] tasks, object o)
            {
                // Task<T> is erasable: no base type and no interface of it hands out a result, so the task can be used as one of them.
                Task plain = rwLock.EnterTaskAsync();
                ShouldBeFaulted(rwLock.EnterAsync().AsTask());
                object boxedTask = rwLock.EnterTaskAsync();
                IAsyncResult result = rwLock.EnterTaskAsync();
                dynamic dynamicTask = rwLock.EnterTaskAsync();
                rwLock.EnterTaskAsync().Wait(100);
                string text = $"{rwLock.EnterTaskAsync()}";
                Task ofList = Task.FromResult(new List<Guard>());

                // The only way back to the result is a conversion to the generic type, and what that gives is stored, whatever gives the
                // reference.
                Guard a = {|#0:await (Task<Guard>)plain|};
                Guard b = {|#1:await (Task<Guard>)GetObject()|};
                Guard c = {|#2:await (Task<Guard>)GetTask()|};
                Guard d = {|#3:((Task<Guard>)GetTask()).Result|};
                Guard e = {|#4:await (GetObject() as Task<Guard>)!|};
                if (o is Task<Guard> t) { Guard f = {|#5:await t|}; }
                if (GetObject() is Task<Guard> { {|#6:Result|}: var g }) { }
                Guard h = GetObject() switch { Task<Guard> u => {|#7:u.Result|}, _ => default };
                Guard i = {|#8:((List<Guard>)GetObject())[0]|};

                // Nothing else is exempt: a list hands out boxed elements through IList, and generic code that is given a task can hand it
                // back as the result of a call.
                object boxedList = {|#9:list|};
                var same = {|#10:Identity(rwLock.EnterTaskAsync())|};
            }
        }

        {{Sources.Guard}}
        """,
        Verify.ReadFromStoredContainer(0, "'await'", "Guard"),
        Verify.ReadFromStoredContainer(1, "'await'", "Guard", canBeMoved: false),
        Verify.ReadFromStoredContainer(2, "'await'", "Guard", canBeMoved: false),
        Verify.ReadFromStoredContainer(3, "'Task<TResult>.Result'", "Guard", canBeMoved: false),
        Verify.ReadFromStoredContainer(4, "'await'", "Guard", canBeMoved: false),
        Verify.ReadFromStoredContainer(5, "'await'", "Guard"),
        Verify.ReadFromStoredContainer(6, "'Task<TResult>.Result'", "Guard"),
        Verify.ReadFromStoredContainer(7, "'Task<TResult>.Result'", "Guard"),
        Verify.ReadFromStoredContainer(8, "'List<T>.this[int]'", "Guard", canBeMoved: false),
        Verify.OpaqueConversion(9, "System.Collections.Generic.List<Guard>", "object"),
        Verify.OpaqueTypeArgument(10, "C.Identity<T>(T)", "System.Threading.Tasks.Task<Guard>", "T", "M:C.Identity``1(``0)"));

    [TestMethod]
    public Task ErasableType_FromAUserList() => Verify.AnalyzerAsync(
        $$"""
        class StoreBase { }

        class Store<T> : StoreBase { }

        class OtherStore<T> : StoreBase { }

        struct ValueStore<T> { }

        class Plain { }

        class C
        {
            static StoreBase GetPlain() => null!;

            void M(Store<Guard> store, OtherStore<Guard> other)
            {
                StoreBase a = store;
                object b = store;
                StoreBase c = {|#0:other|};

                // What comes back out of the base type is stored, also when a call gives the reference.
                Guard d = {|#1:((Store<Guard>)GetPlain()).Take()|};
            }
        }

        static class Stores
        {
            // Listed for this test by the user list: it hands out what the store holds.
            public static T Take<T>(this Store<T> store) => default!;
        }

        {{Sources.Guard}}
        """,
        """
        T:Store`1;erasable
        M:Stores.Take``1(Store{``0})
        T:ValueStore`1;erasable
        T:Plain;erasable
        M:Stores.Take``1(Store{``0});erasable
        T:Not.Referenced`1;erasable
        """,
        Verify.OpaqueConversion(0, "OtherStore<Guard>", "StoreBase"),
        Verify.ReadFromStoredContainer(1, "'Stores.Take<T>(Store<T>)'", "Guard"),
        Verify.SafeListLine(3, 1, 24, "the marker 'erasable' is for a generic class or interface"),
        Verify.SafeListLine(4, 1, 17, "the marker 'erasable' is for a generic class or interface"),
        Verify.SafeListLine(5, 1, 38, "the marker 'erasable' is for a type; a member takes 'new', 'borrows', 'drains', 'owned' or no marker"));

    [TestMethod]
    public Task ConversionWithNoCastWritten() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using System.Collections.Generic;
        using System.Threading.Tasks;

        class C
        {
            static void Log(object value) { }

            static void LogAll(params object[] values) { }

            [return: Singulink.CodeAnalysis.Borrowed]
            static IEnumerable<Guard> Typed(List<Guard> list) => list;

            static object Untyped(List<Guard> list) => {|#0:list|};

            void M(List<Guard> list, Guard[] guards, Store<Guard> store, RwLock rwLock)
            {
                // An inherited member is used on the container as the type that declares it.
                string text = {|#1:list.ToString()|};
                int rank = guards.Rank;
                {|#2:store.Flush()|};
                Type type = list.GetType();

                // An argument, also of a 'params' parameter.
                Log({|#3:list|});
                LogAll(1, {|#4:guards|});

                // What the compiler converts.
                string interpolated = $"{ {|#5:list|} }";
                string concatenated = "Guards: " + {|#6:list|};
                lock (list) { }

                if (list is {|#7:object asObject|}) { }
                if (list is IReadOnlyList<Guard> asTyped) { }
                if (list is System.Collections.IEnumerable) { }

                // A comparison of references hands the container to no code.
                bool isNull = list == null;
                bool isNotNull = list is not null;
                bool same = list == rwLock.CreateGuards();
            }
        }

        class StoreBase
        {
            public void Flush() { }
        }

        class Store<T> : StoreBase { }

        {{Sources.Guard}}
        """,
        Verify.OpaqueConversion(0, "System.Collections.Generic.List<Guard>", "object"),
        Verify.OpaqueMember(1, "System.Collections.Generic.List<Guard>", "object", "object.ToString()", "M:System.Object.ToString"),
        Verify.OpaqueMember(2, "Store<Guard>", "StoreBase", "StoreBase.Flush()", "M:StoreBase.Flush"),
        Verify.OpaqueConversion(3, "System.Collections.Generic.List<Guard>", "object"),
        Verify.OpaqueConversion(4, "Guard[]", "object"),
        Verify.OpaqueConversion(5, "System.Collections.Generic.List<Guard>", "object"),
        Verify.OpaqueConversion(6, "System.Collections.Generic.List<Guard>", "object"),
        Verify.OpaqueConversion(7, "System.Collections.Generic.List<Guard>", "object"));

    [TestMethod]
    public Task ConversionIsAllowedForAListedMember() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;

        class C
        {
            async Task M([Singulink.CodeAnalysis.Kept] List<Guard> list, List<Guard> other, Task<Guard> first, Task second)
            {
                ArgumentNullException.ThrowIfNull(list);
                bool same = ReferenceEquals(list, other);
                GC.KeepAlive(list);

                // The non-generic WhenAll, as an element of the array of its 'params' parameter.
                await Task.WhenAll(first, second);

                Monitor.Enter(list);
                Monitor.Exit(list);
            }
        }

        {{Sources.Guard}}
        """);

    [TestMethod]
    public Task BoxingAValueOfANonCopyableType_IsTheFindingForValues() => Verify.AnalyzerAsync(
        $$"""
        using System.Threading.Tasks;

        class C
        {
            void M(Guard guard, ValueTask<Guard> pending, Guard? maybe)
            {
                object o = {|#0:guard|};
                object p = {|#1:pending|};
                System.IDisposable d = {|#2:guard|};
                object q = {|#3:maybe|};

                // Each is read again here, so none of the conversions above is at the last use of its variable.
                _ = (guard, pending, maybe);
            }
        }

        {{Sources.Guard}}
        """,
        Verify.Boxing(0, "Guard", "object"),
        Verify.Boxing(1, "System.Threading.Tasks.ValueTask<Guard>", "object"),
        Verify.Boxing(2, "Guard", "System.IDisposable"),
        Verify.Boxing(3, "Guard?", "object"));

    [TestMethod]
    public Task ErasableType_HidesWhatIsInsideIt() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using System.Collections;
        using System.Collections.Generic;
        using System.Linq;
        using System.Threading.Tasks;

        class TaskList : List<Task<Guard>> { }

        class C
        {
            static void Log(object value) { }

            async Task M([Singulink.CodeAnalysis.Kept] Task<Guard>[] tasks, [Singulink.CodeAnalysis.Kept] List<Task<Guard>> list, Task<Guard[]> ofArray, TaskList taskList)
            {
                // A conversion is allowed when every non-copyable type in the converted type sits inside an erasable type: what comes out of
                // the untyped side is a reference to the task.
                Array array = tasks;
                IList untyped = tasks;
                object[] objects = tasks;
                Task[] plainTasks = tasks;
                IEnumerable<Task> plainList = list;
                object boxed = list;
                Task plain = ofArray;
                IList fromDerived = taskList;
                Log(tasks);
                int length = tasks.Length;
                var clone = tasks.Clone();

                // The way from there to an element is a conversion, and what that gives is stored.
                Guard a = {|#0:await (Task<Guard>)array.GetValue(0)|};
                Guard b = {|#1:((Task<Guard>)untyped[0]).Result|};
                Guard c = {|#2:((Task<Guard>[])clone)[0].Result|};

                foreach (Task<Guard> task in untyped)
                {
                    Guard d = {|#3:task.Result|};
                }

                // Binding to a type parameter that is not marked is not a conversion: generic code can hand the task back as a result.
                var copy = {|#4:tasks.ToArray()|};
                var first = {|#5:list.First()|};
            }

            void Mixed(Dictionary<Task<Guard>, List<Guard>> map, List<Guard>[] lists, (Task<Guard>, Guard[]) pair)
            {
                // A non-copyable type that sits outside an erasable type is still lost.
                object boxed = {|#6:map|};
                Array array = {|#7:lists|};
                IEnumerable untyped = {|#8:pair.Item2|};
            }
        }

        {{Sources.Guard}}
        """,
        Verify.ReadFromStoredContainer(0, "'await'", "Guard", canBeMoved: false),
        Verify.ReadFromStoredContainer(1, "'Task<TResult>.Result'", "Guard", canBeMoved: false),
        Verify.ReadFromStoredContainer(2, "'Task<TResult>.Result'", "Guard"),
        Verify.ReadFromStoredContainer(3, "'Task<TResult>.Result'", "Guard", because: "'task' is not the method's own anywhere in the method, because on line 34 it is given what something else holds"),
        Verify.OpaqueTypeArgument(
            4,
            "Enumerable.ToArray<TSource>(IEnumerable<TSource>)",
            "System.Threading.Tasks.Task<Guard>",
            "TSource",
            "M:System.Linq.Enumerable.ToArray``1(System.Collections.Generic.IEnumerable{``0})"),
        Verify.OpaqueTypeArgument(
            5,
            "Enumerable.First<TSource>(IEnumerable<TSource>)",
            "System.Threading.Tasks.Task<Guard>",
            "TSource",
            "M:System.Linq.Enumerable.First``1(System.Collections.Generic.IEnumerable{``0})"),
        Verify.OpaqueConversion(6, "System.Collections.Generic.Dictionary<System.Threading.Tasks.Task<Guard>, System.Collections.Generic.List<Guard>>", "object"),
        Verify.UntypedCollectionConversion(7, "System.Collections.Generic.List<Guard>[]", "System.Array"),
        Verify.UntypedCollectionConversion(8, "Guard[]", "System.Collections.IEnumerable"));

    [TestMethod]
    public Task TypeThatDerivesFromAContainer_IsAContainer() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using System.Collections;
        using System.Collections.Generic;
        using System.Threading.Tasks;

        class Guards : List<Guard> { }

        class MoreGuards : Guards { }

        interface IGuards : IReadOnlyList<Guard> { }

        // The constructor of the base class is a member of an unmarked generic like any other: this one is listed, and keeps what the
        // delegate returns as the result.
        abstract class GuardTask : Task<Guard>
        {
            protected GuardTask() : base(null!) { }
        }

        // A type that names itself among its interfaces, and one that reaches a container through itself.
        class Node : IEquatable<Node>, IComparable<Node>
        {
            public bool Equals(Node other) => true;

            public int CompareTo(Node other) => 0;
        }

        class Tree : IEnumerable<Tree>, IReadOnlyList<Guard>
        {
            public Guard this[int index] => default;

            public int Count => 0;

            public IEnumerator<Tree> GetEnumerator() => null!;

            IEnumerator<Guard> IEnumerable<Guard>.GetEnumerator() => null!;

            IEnumerator IEnumerable.GetEnumerator() => null!;
        }

        class C
        {
            static Guards Create() => new();

            void M(Guards guards, MoreGuards more, IGuards readOnly, GuardTask task, Node node, Tree tree)
            {
                // Reads from it are reads from a stored container.
                Guard a = {|#0:guards[0]|};
                Guard b = {|#1:more[0]|};
                Guard c = {|#2:readOnly[0]|};
                Guard d = {|#3:task.Result|};

                // Conversions of it lose the non-copyable type as those of its base type do.
                IList untyped = {|#4:guards|};
                object boxed = {|#5:more|};

                // An interface of checked code is erasable: what implements it answers for what it adds.
                IEnumerable alsoUntyped = readOnly;
                object boxedTree = {|#7:tree|};

                // What keeps the type visible is fine.
                List<Guard> asBase = guards;
                IReadOnlyList<Guard> typed = readOnly;
                int count = guards.Count;
                guards.Add(default);

                // A new one is stored as well: the code of the class can keep 'this'.
                Guard e = {|#9:Create()[0]|};

                // A class that derives from an erasable type has its non-copyable type inside that.
                Task plain = task;
                object node2 = node;
            }
        }

        {{Sources.Guard}}
        """,
        Verify.ReadFromStoredContainer(0, "'List<T>.this[int]'", "Guard"),
        Verify.ReadFromStoredContainer(1, "'List<T>.this[int]'", "Guard"),
        Verify.ReadFromStoredContainer(2, "'IReadOnlyList<T>.this[int]'", "Guard"),
        Verify.ReadFromStoredContainer(3, "'Task<TResult>.Result'", "Guard"),
        Verify.UntypedCollectionConversion(4, "Guards", "System.Collections.IList", through: "System.Collections.Generic.List<Guard>"),
        Verify.OpaqueConversion(5, "MoreGuards", "object", through: "Guards"),
        Verify.OpaqueConversion(7, "Tree", "object", through: "System.Collections.Generic.IEnumerable<Tree>"),
        Verify.ReadFromStoredContainer(9, "'List<T>.this[int]'", "Guard", canBeMoved: false));

    [TestMethod]
    public Task TypeThatDerivesFromAContainer_IsNeverNewForTheMembersThatItInherits() => Verify.AnalyzerAsync(
        $$"""
        using System.Collections.Generic;
        using System.Runtime.InteropServices;
        using Singulink.CodeAnalysis;

        // The code of the class decides who else holds the object, and nothing checks it against what the list says of List<T>.
        class Guards : List<Guard>
        {
            static readonly List<Guards> s_all = new();

            public void Register() => s_all.Add(this);
        }

        sealed class MoreGuards : Guards { }

        // A generic of checked code hands out what it moved out, whoever else holds the object.
        sealed class Bag<[NonCopyable] T>
        {
            private T _item = default!;

            public void Add(T item) => _item = NonCopyable.Move(ref item);

            public T Take() => NonCopyable.Move(ref _item);
        }

        class C
        {
            readonly RwLock _rwLock = new();

            static Guards MakeGuards() => new();

            void OwnLocalAtItsLastUse()
            {
                var guards = new Guards();
                guards.Add(_rwLock.Enter());
                guards.Register();
                Guard first = {|#0:guards[0]|};
            }

            void Created()
            {
                foreach (Guard guard in {|#1:new Guards()|})
                {
                }
            }

            void Returned()
            {
                foreach (Guard guard in {|#2:MakeGuards()|})
                {
                }

                Guard first = {|#3:MakeGuards()[0]|};
            }

            void AsAnInterface()
            {
                IReadOnlyList<Guard> list = new Guards();
                Guard first = {|#4:list[0]|};
                Guard second = {|#5:((IReadOnlyList<Guard>)new Guards())[0]|};
            }

            void AsItsBase()
            {
                List<Guard> list = new Guards();
                Guard first = {|#6:list[0]|};
            }

            void DerivedFurther()
            {
                var more = new MoreGuards();
                Guard first = {|#7:more[0]|};
            }

            // Callers take what a method returns as a List<Guard> for a list of their own.
            List<Guard> ReturnedAsItsBase() => {|#8:new Guards()|};

            // Used in place it is a container like any other, and a method may return one as what it is.
            void InPlace()
            {
                var guards = new Guards();
                guards.Add(_rwLock.Enter());
                CollectionsMarshal.AsSpan(guards)[0].Dispose();
                int count = guards.Count;
                guards.Clear();
            }

            Guards Factory()
            {
                var guards = new Guards();
                guards.Add(_rwLock.Enter());
                return guards;
            }

            // None of it goes for a list itself.
            void PlainList()
            {
                var list = new List<Guard> { _rwLock.Enter() };
                Guard first = list[0];

                foreach (Guard guard in new List<Guard>())
                {
                }

                IReadOnlyList<Guard> typed = new List<Guard>();
                Guard second = typed[0];
            }

            void MarkedGeneric()
            {
                var bag = new Bag<Guard>();
                bag.Add(_rwLock.Enter());
                Guard taken = bag.Take();
            }
        }

        {{Sources.Guard}}
        """,
        Verify.ReadFromStoredContainer(0, "'List<T>.this[int]'", "Guard"),
        Verify.ReadFromStoredContainer(1, "'foreach'", "Guard"),
        Verify.ReadFromStoredContainer(2, "'foreach'", "Guard", canBeMoved: false),
        Verify.ReadFromStoredContainer(3, "'List<T>.this[int]'", "Guard", canBeMoved: false),
        Verify.ReadFromStoredContainer(4, "'IReadOnlyList<T>.this[int]'", "Guard"),
        Verify.ReadFromStoredContainer(5, "'IReadOnlyList<T>.this[int]'", "Guard"),
        Verify.ReadFromStoredContainer(6, "'List<T>.this[int]'", "Guard"),
        Verify.ReadFromStoredContainer(7, "'List<T>.this[int]'", "Guard"),
        Verify.UnownedContainerReturned(8, "C.ReturnedAsItsBase()", "System.Collections.Generic.List<Guard>"));

    [TestMethod]
    public Task CovariantReturn_IsTheConversionThatItStandsFor() => Verify.AnalyzerAsync(
        $$"""
        using System.Collections;
        using System.Collections.Generic;
        using System.Threading.Tasks;
        using Singulink.CodeAnalysis;

        abstract class ItemsBase
        {
            public abstract object GetItems();

            public abstract IEnumerable Untyped { get; }

            public abstract Task GetTask();

            public abstract IReadOnlyList<Guard> GetTyped();

            public abstract object GetGeneric<[NonCopyable] T>();
        }

        sealed class Plain : ItemsBase
        {
            private Guard[] _items = new Guard[4];

            // A caller of the base member gets the array as an object, with no conversion in any body.
            public override Guard[] {|#0:GetItems|}() => new Guard[4];

            public override List<Guard> {|#1:Untyped|} => null!;

            public override T[] {|#2:GetGeneric|}<[NonCopyable] T>() => null!;

            // A task is erasable, and a type that keeps the non-copyable type visible loses nothing.
            public override Task<Guard> GetTask() => null!;

            public override Guard[] GetTyped() => new Guard[4];
        }

        abstract class GenericBase<[NonCopyable] T>
        {
            public abstract IEnumerable<T> GetAll();

            public abstract IEnumerable<TOther> Convert<[NonCopyable] TOther>();
        }

        sealed class Generic<[NonCopyable] T> : GenericBase<T>
        {
            public override List<T> GetAll() => null!;

            public override List<TOther> Convert<[NonCopyable] TOther>() => null!;
        }

        {{Sources.Guard}}
        """,
        Verify.OpaqueReturn(0, "Guard[]", "object", "Plain.GetItems()", "ItemsBase.GetItems()"),
        Verify.UntypedCollectionReturn(1, "System.Collections.Generic.List<Guard>", "System.Collections.IEnumerable", "Plain.Untyped", "ItemsBase.Untyped"),
        Verify.OpaqueReturn(2, "T[]", "object", "Plain.GetGeneric<T>()", "ItemsBase.GetGeneric<T>()"));

    [TestMethod]
    public Task MethodGroup_IsTheConversionThatItsDelegateStandsFor() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using System.Collections;
        using System.Collections.Generic;
        using System.Threading.Tasks;

        class C
        {
            static Guard[] MakeGuards() => new Guard[1];

            static Task<Guard> MakeTask() => null!;

            static void TakeObject(object value) { }

            static void TakeUntyped(IEnumerable value) { }

            void M()
            {
                // The delegate hands out what the method returns as its own return type.
                Func<object> make = {|#0:MakeGuards|};
                Func<IEnumerable> makeUntyped = {|#1:MakeGuards|};
                var created = new Func<object>({|#2:MakeGuards|});

                // And hands the method what it is called with.
                Action<List<Guard>> take = {|#3:TakeObject|};
                Action<Guard[]> takeUntyped = {|#4:TakeUntyped|};

                // A delegate that keeps the non-copyable type visible loses nothing, and a task is erasable.
                Func<Guard[]> typed = MakeGuards;
                Func<IReadOnlyList<Guard>> visible = MakeGuards;
                Func<Task> makeTask = MakeTask;
                Action<Task<Guard>> takeTask = TakeObject;
            }
        }

        {{Sources.Guard}}
        """,
        Verify.OpaqueMethodGroup(0, "Guard[]", "object", "C.MakeGuards()", "System.Func<object>"),
        Verify.UntypedCollectionMethodGroup(1, "Guard[]", "System.Collections.IEnumerable", "C.MakeGuards()", "System.Func<System.Collections.IEnumerable>"),
        Verify.OpaqueMethodGroup(2, "Guard[]", "object", "C.MakeGuards()", "System.Func<object>"),
        Verify.OpaqueMethodGroup(3, "System.Collections.Generic.List<Guard>", "object", "C.TakeObject(object)", "System.Action<System.Collections.Generic.List<Guard>>"),
        Verify.UntypedCollectionMethodGroup(4, "Guard[]", "System.Collections.IEnumerable", "C.TakeUntyped(IEnumerable)", "System.Action<Guard[]>"));

    [TestMethod]
    public Task ErasableThroughMarks_ATypeOfCheckedCodeNeedsNoListEntry() => Verify.AnalyzerAsync(
        $$"""
        using System.Collections.Generic;
        using Singulink.CodeAnalysis;

        abstract class PendingBase
        {
            public virtual object Get() => new object();
        }

        interface IUntyped { object GetUntyped(); }

        interface IPlain<T> { T Take(); }

        interface IProducer<out T> { T Take(); }

        interface IWithDefault<T>
        {
            T Take();

            object TakeUntyped() => Take()!;
        }

        class UnmarkedBase<T> : IUntyped
        {
            protected T _value = default!;

            public object GetUntyped() => _value!;
        }

        // The body check guarantees what the marker 'erasable' only claims: what would hand out a T untyped is reported inside the type.
        sealed class Pending<[NonCopyable] T> : PendingBase, IUntyped, IPlain<T> where T : struct
        {
            private T _value;

            object IUntyped.GetUntyped() => {|#0:_value|};

            public T Take() => NonCopyable.Move(ref _value);
        }

        // An override whose return type shows the type parameter hands it out with no conversion in any body.
        sealed class Covariant<[NonCopyable] T> : PendingBase where T : struct
        {
            private T[] _items = new T[4];

            public override T[] {|#1:Get|}() => new T[4];
        }

        // Variance turns an interface with an 'out' type parameter into one over object, with no body involved.
        sealed class Holder<[NonCopyable] T> : IProducer<T>
        {
            private T _value = default!;

            public T Take() => NonCopyable.Move(ref _value);
        }

        // A generic base class and a default implementation of an interface are bodies that are not checked for the type parameter.
        sealed class OverUnmarkedBase<[NonCopyable] T> : UnmarkedBase<T> { }

        sealed class WithDefault<[NonCopyable] T> : IWithDefault<T>
        {
            public T Take() => default!;
        }

        // A class need not be sealed: what derives from it answers for what it adds, where it is declared.
        class NotSealed<[NonCopyable] T> : PendingBase { }

        // That goes for a container that is bound to the type parameter as well: what would hand it out untyped is reported in the type.
        sealed class Boxes<[NonCopyable] T> : IUntyped
        {
            private T _value = default!;

            object IUntyped.GetUntyped() => {|#7:NonCopyable.Move(ref _value)|};
        }

        class C
        {
            void M()
            {
                PendingBase pending = new Pending<Guard>();
                IUntyped untyped = new Pending<Guard>();
                object boxed = new Pending<Guard>();
                object holderOfValue = new Holder<Guard>();
                IProducer<List<Guard>> typed = new Holder<List<Guard>>();

                PendingBase covariant = {|#2:new Covariant<Guard>()|};
                IProducer<object> producer = {|#3:new Holder<List<Guard>>()|};
                object holderOfList = {|#4:new Holder<List<Guard>>()|};
                IUntyped overUnmarked = {|#5:new OverUnmarkedBase<Guard>()|};
                object withDefault = {|#6:new WithDefault<Guard>()|};
                PendingBase notSealed = new NotSealed<Guard>();
                IUntyped boxesGuard = new Boxes<Guard>();
                IUntyped boxesList = new Boxes<List<Guard>>();
                object bagOfLists = new NotSealed<List<Guard>>();
            }
        }

        {{Sources.Guard}}
        """,
        Verify.Boxing(0, "T", "object"),
        Verify.OpaqueReturn(1, "T[]", "object", "Covariant<T>.Get()", "PendingBase.Get()"),
        Verify.OpaqueConversion(2, "Covariant<Guard>", "PendingBase"),
        Verify.OpaqueConversion(3, "Holder<System.Collections.Generic.List<Guard>>", "IProducer<object>"),
        Verify.OpaqueConversion(4, "Holder<System.Collections.Generic.List<Guard>>", "object"),
        Verify.OpaqueConversion(5, "OverUnmarkedBase<Guard>", "IUntyped"),
        Verify.OpaqueConversion(6, "WithDefault<Guard>", "object"),
        Verify.OpaqueTypeParameterConversion(7, "T", "object"));

    [TestMethod]
    public Task ErasableType_WhatDerivesFromItMustStayErasable() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using System.Threading.Tasks.Sources;
        using Singulink.CodeAnalysis;

        interface IProducer<out T> { T Take(); }

        interface IWithDefault<T>
        {
            T Peek();

            object PeekUntyped() => Peek()!;
        }

        class UnmarkedBase<T> { }

        class Bag<[NonCopyable] T> { }

        // A conversion sees only the type that it is written with, and the object behind a Bag<T> can be of this type. With a list of guards
        // as T, the object could be converted on to IProducer<object>.
        class {|#0:SortedBag|}<[NonCopyable] T> : Bag<T>, IProducer<T>
        {
            public T Take() => default!;
        }

        // Variance needs a reference type.
        class ValueBag<[NonCopyable] T> : Bag<T>, IProducer<T> where T : struct
        {
            public T Take() => default;
        }

        // An interface with a default implementation that is not checked for T.
        class {|#1:PeekableBag|}<[NonCopyable] T> : Bag<T>, IWithDefault<T>
        {
            public T Peek() => default!;
        }

        // The same for a type that the list marks as erasable, and for a type that is not generic.
        abstract class {|#2:GuardsTask|} : Task<List<Guard>>, IProducer<List<Guard>>
        {
            protected GuardsTask() : base(null!) { }

            public abstract List<Guard> Take();
        }

        // And for what implements an erasable interface, a struct included.
        interface IPending<[NonCopyable] T> { }

        abstract class PendingBase<[NonCopyable] T> : IPending<T> { }

        class Pending<[NonCopyable] T> : PendingBase<T> { }

        struct {|#4:PendingValue|}<[NonCopyable] T> : IPending<T>, IProducer<T>
        {
            public T Take() => default!;
        }

        class {|#5:OverUnmarked|}<[NonCopyable] T> : UnmarkedBase<T>, IPending<T> { }

        // A class that is not generic and names the non-copyable type itself is checked code as it is.
        sealed class Waiter : IValueTaskSource<Guard>
        {
            private ManualResetValueTaskSourceCore<Guard> _core;

            public Guard GetResult(short token) => default;

            public ValueTaskSourceStatus GetStatus(short token) => _core.GetStatus(token);

            public void OnCompleted(Action<object> continuation, object state, short token, ValueTaskSourceOnCompletedFlags flags) =>
                _core.OnCompleted(continuation, state, token, flags);
        }

        class C
        {
            void M(PendingBase<Guard> pending, IPending<Guard> throughInterface, Waiter waiter, CancellationToken token)
            {
                // A class that is not sealed and an interface of checked code can be converted through a variable of their own type.
                object a = pending;
                object b = throughInterface;
                IPending<Guard> c = pending;

                object d = waiter;
                token.UnsafeRegister(static state => { }, waiter);
            }
        }

        {{Sources.Guard}}
        """,
        Verify.ErasableBaseNotKept(
            0, "SortedBag<T>", "Bag<T>", "'IProducer<T>' is covariant in 'T', which is bound to 'T', a type parameter that is not constrained to 'struct'"),
        Verify.ErasableBaseNotKept(
            1, "PeekableBag<T>", "Bag<T>", "'IWithDefault<T>' receives 'T' through type parameter 'T', which is not marked [NonCopyable]"),
        Verify.ErasableBaseNotKept(
            2,
            "GuardsTask",
            "System.Threading.Tasks.Task<System.Collections.Generic.List<Guard>>",
            "'IProducer<T>' is covariant in 'T', which is bound to 'System.Collections.Generic.List<Guard>'"),
        Verify.ErasableBaseNotKept(
            4, "PendingValue<T>", "IPending<T>", "'IProducer<T>' is covariant in 'T', which is bound to 'T', a type parameter that is not constrained to 'struct'"),
        Verify.ErasableBaseNotKept(
            5, "OverUnmarked<T>", "IPending<T>", "'UnmarkedBase<T>' receives 'T' through type parameter 'T', which is not marked [NonCopyable]"));

    [TestMethod]
    public Task Boxing_AGenericStructWithUncheckedCodeHidesItsNonCopyableType() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using System.Collections;
        using System.Collections.Generic;
        using System.Runtime.CompilerServices;
        using Singulink.CodeAnalysis;

        struct Checked<[NonCopyable] T> : IDisposable
        {
            public T Value;

            public void Dispose() { }
        }

        class C
        {
            (Guard guard, int number) _pair;

            void M(RwLock rwLock)
            {
                // Boxing a new value is a move for a struct whose own code is checked. A tuple, an enumerator, a value task and a pair run
                // unchecked code on their box: ITuple and IEnumerator hand out a boxed copy of the element at every read.
                object a = {|#0:(rwLock.Enter(), 1)|};
                ITuple b = {|#1:(rwLock.Enter(), 1)|};
                IEnumerator c = {|#2:rwLock.CreateGuards().GetEnumerator()|};
                object d = {|#3:rwLock.EnterAsync()|};
                object e = {|#4:new KeyValuePair<int, Guard>(1, rwLock.Enter())|};
                object f = {|#5:rwLock.TryEnterTuple()|};
                if (rwLock.TryEnterTuple() is {|#6:ITuple tuple|}) { }

                // A stored one is a boxed copy, as before.
                object g = {|#7:_pair|};

                // A target that still mentions the non-copyable type hides nothing.
                IEnumerator<Guard> typed = rwLock.CreateGuards().GetEnumerator();
                IEquatable<(Guard, int)> equatable = (rwLock.Enter(), 1);

                // The box of a nullable holds the value itself, and the code of a marked struct is checked.
                object h = rwLock.TryEnter();
                IDisposable i = rwLock.Enter();
                IDisposable j = new Checked<Guard>();
            }
        }

        {{Sources.Guard}}
        """,
        Verify.OpaqueBoxing(0, "(Guard, int)", "object", "(T1, T2)"),
        Verify.OpaqueBoxing(1, "(Guard, int)", "System.Runtime.CompilerServices.ITuple", "(T1, T2)"),
        Verify.OpaqueBoxing(2, "System.Collections.Generic.List<Guard>.Enumerator", "System.Collections.IEnumerator", "List<T>.Enumerator"),
        Verify.OpaqueBoxing(3, "System.Threading.Tasks.ValueTask<Guard>", "object", "ValueTask<TResult>"),
        Verify.OpaqueBoxing(4, "System.Collections.Generic.KeyValuePair<int, Guard>", "object", "KeyValuePair<TKey, TValue>"),
        Verify.OpaqueBoxing(5, "(Guard guard, bool ok)", "object", "(T1, T2)"),
        Verify.OpaqueBoxing(6, "(Guard guard, bool ok)", "System.Runtime.CompilerServices.ITuple", "(T1, T2)"),
        Verify.Boxing(7, "(Guard guard, int number)", "object"));

    [TestMethod]
    public Task Variance_IsJudgedByWhatItDoes() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using System.Collections;
        using System.Collections.Generic;
        using System.Threading.Tasks;

        class C
        {
            object? _leak;

            static Guard[] MakeGuards() => new Guard[1];

            void M(IEnumerable<List<Guard>> lists, IEnumerable<Task<Guard>> tasks)
            {
                // A conversion that puts a non-copyable type into a contravariant position gains nothing: every list that is passed to
                // 'typed' is an object for the sink.
                Action<object> sink = o => _leak = o;
                Action<List<Guard>> typed = {|#0:sink|};
                Action<IList> untypedSink = l => { };
                Action<List<Guard>> typedToo = {|#1:untypedSink|};
                var cast = (Action<List<Guard>>){|#2:sink|};

                // At any depth, in the direction in which the values go.
                Func<Action<object>> makeSink = () => sink;
                Func<Action<List<Guard>>> makeTyped = {|#3:makeSink|};

                // A delegate that is created from another delegate calls that one.
                Func<Guard[]> make = MakeGuards;
                var opaque = new Func<object>({|#4:make|});
                var untyped = new Func<IList>({|#5:make|});
                var same = new Func<Guard[]>(make);

                // What keeps the type visible, or hides it inside an erasable type, is not reported.
                IEnumerable<IReadOnlyList<Guard>> readOnly = lists;
                IEnumerable<Task> plain = tasks;
                Action<IReadOnlyList<Guard>> reader = r => { };
                Action<List<Guard>> narrowed = reader;
                Action<Task> awaiter = t => { };
                Action<Task<Guard>> typedAwaiter = awaiter;
            }
        }

        {{Sources.Guard}}
        """,
        Verify.OpaqueVariance(
            0, "System.Collections.Generic.List<Guard>", "object", "System.Action<object>", "System.Action<System.Collections.Generic.List<Guard>>"),
        Verify.UntypedCollectionVariance(
            1,
            "System.Collections.Generic.List<Guard>",
            "System.Collections.IList",
            "System.Action<System.Collections.IList>",
            "System.Action<System.Collections.Generic.List<Guard>>"),
        Verify.OpaqueVariance(
            2, "System.Collections.Generic.List<Guard>", "object", "System.Action<object>", "System.Action<System.Collections.Generic.List<Guard>>"),
        Verify.OpaqueVariance(
            3,
            "System.Collections.Generic.List<Guard>",
            "object",
            "System.Func<System.Action<object>>",
            "System.Func<System.Action<System.Collections.Generic.List<Guard>>>"),
        Verify.OpaqueMethodGroup(4, "Guard[]", "object", "System.Func<Guard[]>", "System.Func<object>"),
        Verify.UntypedCollectionMethodGroup(5, "Guard[]", "System.Collections.IList", "System.Func<Guard[]>", "System.Func<System.Collections.IList>"));

    [TestMethod]
    public Task ErasableThroughMarks_NoContainerIsInAPositionThatIsCovariantInEffect() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using System.Collections.Generic;
        using Singulink.CodeAnalysis;

        interface IProducer<out V>
        {
            V Get();
        }

        interface IConsumer<in U>
        {
            void Put(U value);
        }

        interface IHolder<V>
        {
            void Hold(V value);
        }

        abstract class PendingBase { }

        // Converted to object, a Pending<Guard> can be converted on to IConsumer<Action<object>>, whose Put hands the list to a delegate
        // that takes an object. Nothing in the body is a conversion. 'in' inside 'in' is covariant in effect.
        sealed class Pending<[NonCopyable] T> : PendingBase, IConsumer<Action<List<T>>>
            where T : struct
        {
            private readonly List<T> _list = new();

            public void Put(Action<List<T>> action) => action(_list);
        }

        sealed class Plain<[NonCopyable] T> : PendingBase
            where T : struct
        {
        }

        class Bag<[NonCopyable] T> { }

        class {|#2:ConsumerBag|}<[NonCopyable] T> : Bag<T>, IConsumer<Action<List<T>>>
        {
            public void Put(Action<List<T>> action) { }
        }

        class {|#3:ProducerBag|}<[NonCopyable] T> : Bag<T>, IProducer<Bag<T>>
        {
            public Bag<T> Get() => new();
        }

        // A contravariant position converts toward more derived types only, and never reaches object.
        class SinkBag<[NonCopyable] T> : Bag<T>, IConsumer<T>, IComparable<SinkBag<T>>
        {
            public void Put(T value) { }

            public int CompareTo(SinkBag<T>? other) => 0;
        }

        // An invariant type parameter on the way allows no conversion at all.
        class HolderBag<[NonCopyable] T> : Bag<T>, IHolder<Action<List<T>>>, IHolder<IProducer<T>>
        {
            public void Hold(Action<List<T>> value) { }

            public void Hold(IProducer<T> value) { }
        }

        // Variance needs a reference type.
        class StructBag<[NonCopyable] T> : Bag<T>, IProducer<T>
            where T : struct
        {
            public T Get() => default;
        }

        class C
        {
            void M()
            {
                object pending = {|#0:new Pending<Guard>()|};
                PendingBase asBase = {|#1:new Pending<Guard>()|};
                object plain = new Plain<Guard>();
            }
        }

        {{Sources.Guard}}
        """,
        Verify.OpaqueConversion(0, "Pending<Guard>", "object"),
        Verify.OpaqueConversion(1, "Pending<Guard>", "PendingBase"),
        Verify.ErasableBaseNotKept(
            2,
            "ConsumerBag<T>",
            "Bag<T>",
            "'IConsumer<U>' is given 'System.Action<System.Collections.Generic.List<T>>', whose type parameter 'T' is there in a position that " +
            "is covariant in effect and is bound to 'System.Collections.Generic.List<T>'"),
        Verify.ErasableBaseNotKept(3, "ProducerBag<T>", "Bag<T>", "'IProducer<V>' is covariant in 'V', which is bound to 'Bag<T>'"));

    [TestMethod]
    public Task ForEach_ConvertsEachElementToTheTypeOfItsVariable() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using System.Collections;
        using System.Collections.Generic;
        using Singulink.CodeAnalysis;

        class C
        {
            static List<T> Create<[NonCopyable] T>() => new();

            void M(List<List<Guard>> lists, Guard[] array, RwLock rwLock)
            {
                // A container becomes a type that hides its elements, with no conversion written.
                foreach ({|#0:IList|} list in lists) { }
                foreach ({|#1:object|} list in lists) { }
                foreach (IReadOnlyList<Guard> list in lists) { }
                foreach (var list in lists) { }

                // A stored value is boxed, which is a copy.
                foreach ({|#2:object|} item in array) { }
                foreach ({|#3:IDisposable|} item in array) { }

                // A new one is moved into the box.
                foreach (IDisposable item in rwLock.CreateGuards()) { }
            }

            void Generic<[NonCopyable] T>()
            {
                // With a container bound to the type parameter there is no box.
                foreach ({|#4:object|} item in Create<T>()) { }
            }
        }

        {{Sources.Guard}}
        """,
        Verify.UntypedCollectionConversion(0, "System.Collections.Generic.List<Guard>", "System.Collections.IList"),
        Verify.OpaqueConversion(1, "System.Collections.Generic.List<Guard>", "object"),
        Verify.Boxing(2, "Guard", "object"),
        Verify.Boxing(3, "Guard", "System.IDisposable"),
        Verify.OpaqueTypeParameterConversion(4, "T", "object"));

    // An object of a class that derives from a listed container is stored where it is converted to that container. Three places convert
    // it with no conversion written: the return of an override, a method group, and a variant type argument.
    [TestMethod]
    public Task DerivedContainer_ConvertedWithNoConversionWrittenIsReported() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using System.Collections.Generic;
        using System.Threading.Tasks;
        using Singulink.CodeAnalysis;

        sealed class Guards : List<Guard>
        {
            public static readonly List<Guards> All = new();

            public Guards() => All.Add(this);
        }

        class Factory
        {
            public virtual List<Guard> Make() => new List<Guard>();

            public virtual List<Guard> Latest => new List<Guard>();

            [return: Borrowed]
            public virtual List<Guard> Lend() => new List<Guard>();

            public virtual IReadOnlyList<Guard> View() => new List<Guard>();

            public virtual Guards Exact() => new Guards();
        }

        sealed class GuardsFactory : Factory
        {
            public override Guards {|#0:Make|}() => new Guards();

            public override Guards {|#1:Latest|} => new Guards();

            // The callers of the base member treat the result as stored.
            [return: Borrowed]
            public override Guards Lend() => new Guards();

            public override Guards {|#2:View|}() => new Guards();

            // The same type keeps what the callers know.
            public override Guards Exact() => new Guards();
        }

        interface ISource<out T>
        {
            T Make();
        }

        sealed class Source : ISource<Guards>
        {
            public Guards Make() => new Guards();
        }

        static class Program
        {
            static Guards MakeGuards() => new Guards();

            static void MethodGroups()
            {
                Func<List<Guard>> make = {|#3:MakeGuards|};
                Func<IEnumerable<Guard>> enumerate = {|#4:MakeGuards|};

                // A delegate that returns the class itself tells its callers what they get.
                Func<Guards> exact = MakeGuards;
                Func<List<Guard>> wrapped = new Func<List<Guard>>({|#5:exact|});
            }

            static void Variance(Func<Guards> exact, ISource<Guards> source, Source implementation, IEnumerable<Guards> all, IEnumerable<ISource<Guards>> sources)
            {
                Func<List<Guard>> make = {|#6:exact|};
                ISource<List<Guard>> lists = {|#7:source|};
                ISource<IList<Guard>> throughTheClass = {|#8:implementation|};
                IEnumerable<List<Guard>> each = {|#9:all|};
                IEnumerable<ISource<List<Guard>>> nested = {|#10:sources|};

                // What is not variant, and the type itself, convert nothing.
                ISource<Guards> same = implementation;
                IEnumerable<Guards> stillGuards = all;
                Action<List<Guard>> taking = list => { };
                Action<Guards> takingGuards = taking;
            }
        }

        {{Sources.Guard}}
        """,
        Verify.HolderReturnedByOverride(0, "Guards", "System.Collections.Generic.List<Guard>", "GuardsFactory.Make()", "Factory.Make()"),
        Verify.HolderReturnedByOverride(1, "Guards", "System.Collections.Generic.List<Guard>", "GuardsFactory.Latest", "Factory.Latest"),
        Verify.HolderReturnedByOverride(2, "Guards", "System.Collections.Generic.IReadOnlyList<Guard>", "GuardsFactory.View()", "Factory.View()"),
        Verify.DelegateHandsOutStoredContainer(3, "Program.MakeGuards()", "Guards"),
        Verify.DelegateHandsOutStoredContainer(4, "Program.MakeGuards()", "Guards"),
        Verify.DelegateHandsOutStoredContainer(5, "Func<Guards>.Invoke()", "Guards"),
        Verify.HolderLostByVariance(6, "System.Func<Guards>", "System.Func<System.Collections.Generic.List<Guard>>", "Guards", "System.Collections.Generic.List<Guard>"),
        Verify.HolderLostByVariance(7, "ISource<Guards>", "ISource<System.Collections.Generic.List<Guard>>", "Guards", "System.Collections.Generic.List<Guard>"),
        Verify.HolderLostByVariance(8, "Source", "ISource<System.Collections.Generic.IList<Guard>>", "Guards", "System.Collections.Generic.IList<Guard>"),
        Verify.HolderLostByVariance(9, "System.Collections.Generic.IEnumerable<Guards>", "System.Collections.Generic.IEnumerable<System.Collections.Generic.List<Guard>>", "Guards", "System.Collections.Generic.List<Guard>"),
        Verify.HolderLostByVariance(10, "System.Collections.Generic.IEnumerable<ISource<Guards>>", "System.Collections.Generic.IEnumerable<ISource<System.Collections.Generic.List<Guard>>>", "Guards", "System.Collections.Generic.List<Guard>"));

    // The same holds for a type that is open to stored containers where it becomes a type that is not.
    [TestMethod]
    public Task OpenType_ConvertedWithNoConversionWrittenIsReported() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using System.Collections;
        using System.Collections.Generic;
        using System.Threading.Tasks;
        using Singulink.CodeAnalysis;

        sealed class TaskQueue : Queue<Task<Guard>>
        {
        }

        sealed class Bag<[NonCopyable] T> : IEnumerable<T>
        {
            public IEnumerator<T> GetEnumerator() => throw new NotImplementedException();

            IEnumerator IEnumerable.GetEnumerator() => throw new NotImplementedException();
        }

        class Factory
        {
            public virtual IEnumerable<Task<Guard>> Pending() => new List<Task<Guard>>();

            public virtual Queue<Task<Guard>> Waiting() => new Queue<Task<Guard>>();
        }

        sealed class OpenFactory : Factory
        {
            public override Bag<Task<Guard>> {|#0:Pending|}() => new Bag<Task<Guard>>();

            // An object of such a class is never new, which is reported where one is returned as well.
            public override TaskQueue {|#1:Waiting|}() => {|#6:new TaskQueue()|};
        }

        static class Program
        {
            static Bag<Task<Guard>> MakeBag() => new Bag<Task<Guard>>();

            static TaskQueue MakeQueue() => {|#7:new TaskQueue()|};

            static void Convert(Func<Bag<Task<Guard>>> exact, Func<TaskQueue> exactQueue)
            {
                Func<IEnumerable<Task<Guard>>> pending = {|#2:MakeBag|};
                Func<Queue<Task<Guard>>> waiting = {|#3:MakeQueue|};
                Func<IEnumerable<Task<Guard>>> byVariance = {|#4:exact|};
                Func<IEnumerable<Task<Guard>>> queueByVariance = {|#5:exactQueue|};
            }
        }

        {{Sources.Guard}}
        """,
        Verify.HolderReturnedByOverride(0, "Bag<System.Threading.Tasks.Task<Guard>>", "System.Collections.Generic.IEnumerable<System.Threading.Tasks.Task<Guard>>", "OpenFactory.Pending()", "Factory.Pending()"),
        Verify.HolderReturnedByOverride(1, "TaskQueue", "System.Collections.Generic.Queue<System.Threading.Tasks.Task<Guard>>", "OpenFactory.Waiting()", "Factory.Waiting()"),
        Verify.DelegateHandsOutStoredContainer(2, "Program.MakeBag()", "Bag<System.Threading.Tasks.Task<Guard>>"),
        Verify.DelegateHandsOutStoredContainer(3, "Program.MakeQueue()", "TaskQueue"),
        Verify.HolderLostByVariance(4, "System.Func<Bag<System.Threading.Tasks.Task<Guard>>>", "System.Func<System.Collections.Generic.IEnumerable<System.Threading.Tasks.Task<Guard>>>", "Bag<System.Threading.Tasks.Task<Guard>>", "System.Collections.Generic.IEnumerable<System.Threading.Tasks.Task<Guard>>"),
        Verify.HolderLostByVariance(5, "System.Func<TaskQueue>", "System.Func<System.Collections.Generic.IEnumerable<System.Threading.Tasks.Task<Guard>>>", "TaskQueue", "System.Collections.Generic.IEnumerable<System.Threading.Tasks.Task<Guard>>"),
        Verify.UnownedContainerReturned(6, "OpenFactory.Waiting()", "TaskQueue", through: "System.Collections.Generic.Queue<System.Threading.Tasks.Task<Guard>>"),
        Verify.UnownedContainerReturned(7, "Program.MakeQueue()", "TaskQueue", through: "System.Collections.Generic.Queue<System.Threading.Tasks.Task<Guard>>"));
}
