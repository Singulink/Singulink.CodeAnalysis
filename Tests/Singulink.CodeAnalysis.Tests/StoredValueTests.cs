using PrefixClassName.MsTest;

namespace Singulink.CodeAnalysis.Tests;

/// <summary>
/// Tests of what is a stored value beside a variable, a field, an element and a reference: what is in a box, a container that a property or a
/// field gives, a variable that lives on after the method that returns it, and what a created value is made of.
/// </summary>
/// <remarks>
/// Docs: articles/concepts/new-and-stored-values.md.
/// </remarks>
[PrefixTestClass]
public class StoredValueTests
{
    [TestMethod]
    public Task TakingAValueOutOfABox_IsACopyWhateverGivesTheReference() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using System.Collections;
        using System.Collections.Generic;
        using System.Threading.Tasks;

        class Holder
        {
            public object Box = null!;
            public object Prop => Box;
            public object Get() => Box;
        }

        class C
        {
            object _box = null!;

            object GetBox() => _box;

            object BoxProp => _box;

            dynamic GetDynamic() => _box;

            static object Cast(object o) => o;

            async Task M(RwLock rwLock, Holder h, ArrayList al, List<object> objs, object[] arr, IDisposable[] disps, dynamic d)
            {
                // A new value can be boxed: the box then holds the only instance. Every way out of the box is a copy, because others can hold
                // the box, also when the reference to it comes from a call.
                _box = rwLock.Enter();

                Guard x1 = {|#0:(Guard)GetBox()|};
                Guard x2 = {|#1:(Guard)BoxProp|};
                Guard x3 = {|#2:(Guard)h.Prop|};
                Guard x4 = {|#3:(Guard)h.Get()|};
                Guard x5 = {|#4:(Guard)al[0]!|};
                Guard x6 = {|#5:(Guard)objs[0]|};
                Guard x7 = {|#6:(Guard)arr[0]|};
                Guard x8 = {|#7:(Guard)h.Box|};
                Guard x9 = {|#8:(Guard)(object)disps[0]|};
                Guard x10 = {|#9:(Guard)(IDisposable)GetBox()|};
                Guard x11 = {|#10:(Guard)Cast(_box)|};
                Guard x12 = {|#11:(Guard)Task.FromResult(_box).Result|};
                Guard x13 = {|#12:(Guard)(_box ?? GetBox())|};
                Guard? x14 = {|#13:GetBox() as Guard?|};
                Guard x15 = ({|#14:(Guard?)GetBox()|}).Value;
                ({|#15:(Guard)GetBox()|}).Dispose();

                // An element of a tuple is taken out by a conversion of the tuple.
                (object, int) boxedInTuple = (GetBox(), 1);
                var x24 = {|#27:((Guard, int))boxedInTuple|};

                // By a pattern.
                if (GetBox() is Guard {|#16:x16|}) { }
                if (h.Prop is Guard {|#17:x17|}) { }
                int x18 = GetBox() switch { Guard {|#18:g|} => g.Value, _ => 0 };
                if (GetBox() is {|#19:KeyValuePair<int, Guard> { Value: var x19 }|}) { }

                // From dynamic.
                Guard x20 = {|#20:GetDynamic()|};
                Guard x21 = {|#21:(Guard)GetDynamic()|};
                Guard x22 = {|#22:await GetDynamic()|};
                Guard x23 = {|#23:d.Create()|};

                // By the variable of a 'foreach' over boxed elements.
                foreach (Guard {|#24:item|} in al) { }
                foreach (Guard {|#25:item|} in arr) { }
                foreach (Guard {|#26:item|} in disps) { }

                // Nothing is taken out here.
                bool isGuard = GetBox() is Guard;
                var disposable = (IDisposable)GetBox();
                disposable.Dispose();
                Guard? none = (Guard?)null;
                foreach (object element in al) { }
            }
        }

        {{Sources.Guard}}
        """,
        Verify.CopyByAssignmentOutOfBox(0, "Guard", "object"),
        Verify.CopyByAssignmentOutOfBox(1, "Guard", "object"),
        Verify.CopyByAssignmentOutOfBox(2, "Guard", "object"),
        Verify.CopyByAssignmentOutOfBox(3, "Guard", "object"),
        Verify.CopyByAssignmentOutOfBox(4, "Guard", "object"),
        Verify.CopyByAssignmentOutOfBox(5, "Guard", "object"),
        Verify.CopyByAssignmentOutOfBox(6, "Guard", "object"),
        Verify.CopyByAssignmentOutOfBox(7, "Guard", "object"),
        Verify.CopyByAssignmentOutOfBox(8, "Guard", "object"),
        Verify.CopyByAssignmentOutOfBox(9, "Guard", "System.IDisposable"),
        Verify.CopyByAssignmentOutOfBox(10, "Guard", "object"),
        Verify.CopyByAssignmentOutOfBox(11, "Guard", "object"),
        Verify.CopyByAssignmentOutOfBox(12, "Guard", "object"),
        Verify.CopyByAssignmentOutOfBox(13, "Guard?", "object"),
        Verify.CopyByAssignmentOutOfBox(14, "Guard?", "object"),
        Verify.CopyByAssignmentOutOfBox(15, "Guard", "object"),
        Verify.CopyByAssignmentOutOfBox(16, "Guard", "object"),
        Verify.CopyByAssignmentOutOfBox(17, "Guard", "object"),
        Verify.CopyByAssignmentOutOfBox(18, "Guard", "object"),
        Verify.CopyByAssignmentOutOfBox(19, "System.Collections.Generic.KeyValuePair<int, Guard>", "object"),
        Verify.CopyByAssignmentOutOfBox(20, "Guard", "dynamic"),
        Verify.CopyByAssignmentOutOfBox(21, "Guard", "dynamic"),
        Verify.CopyByAssignmentOutOfBox(22, "Guard", "dynamic"),
        Verify.CopyByAssignmentOutOfBox(23, "Guard", "dynamic"),
        Verify.CopyByAssignmentOutOfBox(24, "Guard", "object"),
        Verify.CopyByAssignmentOutOfBox(25, "Guard", "object"),
        Verify.CopyByAssignmentOutOfBox(26, "Guard", "System.IDisposable"),
        Verify.CopyByAssignmentOutOfBox(27, "(Guard, int)", "(object, int)"));

    [TestMethod]
    public Task TakingAValueOutOfABox_ByAPatternThatDeclaresNoVariable() => Verify.AnalyzerAsync(
        """
        using Singulink.CodeAnalysis;

        [NonCopyable]
        struct Guard : System.IDisposable
        {
            private bool _isHeld;
            public int Value;
            public bool IsHeld => _isHeld;
            public readonly int Count => Value;
            public void Deconstruct(out bool isHeld, out int value) { isHeld = _isHeld; value = Value; }
            public void Dispose() { }
        }

        class Holder
        {
            public object Box = null!;
        }

        class C
        {
            static object GetBox() => null!;

            void M(object box, Holder holder, System.IDisposable disposable)
            {
                // A pattern that looks into the value reads from a temporary copy that is taken out of the box. It is reported when a member
                // that it reads is not readonly, since that member could change the copy.
                if (box is {|#0:Guard { IsHeld: true }|}) { }
                if (box is {|#1:Guard { IsHeld: var held }|}) { }
                if (box is {|#2:Guard (true, 1)|}) { }
                if (box is {|#3:Guard (var isHeld, _)|}) { }
                if (GetBox() is {|#4:Guard { IsHeld: false, Value: > 1 }|}) { }
                if (box is not {|#5:Guard { IsHeld: true }|}) { }
                if (box is Guard and {|#6:{ IsHeld: true }|}) { }
                if (holder is { Box: {|#7:Guard { IsHeld: true }|} }) { }
                if (disposable is {|#8:Guard { IsHeld: true }|}) { }

                switch (box)
                {
                    case {|#9:Guard { IsHeld: true }|}:
                        break;
                    case {|#10:Guard (false, var number)|}:
                        break;
                }

                int a = box switch { {|#11:Guard { IsHeld: true }|} => 1, {|#12:Guard { IsHeld: false, Value: var v }|} => v, _ => 0 };

                // A copy that is only read through fields and readonly members does no harm.
                if (box is Guard { Value: > 1 }) { }
                if (box is Guard { Count: 1, Value: var value }) { }
                int g = box switch { Guard { Count: > 1 } => 1, _ => 0 };

                // A bare type test copies nothing.
                bool b = box is Guard;
                bool c = box is not Guard;
                bool d = box is Guard { };
                bool e = box is Guard or null;
                int f = box switch { Guard => 1, _ => 0 };

                switch (box)
                {
                    case Guard:
                        break;
                    case int:
                        break;
                }
            }
        }
        """,
        Verify.CopyByAssignmentOutOfBox(0, "Guard", "object"),
        Verify.CopyByAssignmentOutOfBox(1, "Guard", "object"),
        Verify.CopyByAssignmentOutOfBox(2, "Guard", "object"),
        Verify.CopyByAssignmentOutOfBox(3, "Guard", "object"),
        Verify.CopyByAssignmentOutOfBox(4, "Guard", "object"),
        Verify.CopyByAssignmentOutOfBox(5, "Guard", "object"),
        Verify.CopyByAssignmentOutOfBox(6, "Guard", "object"),
        Verify.CopyByAssignmentOutOfBox(7, "Guard", "object"),
        Verify.CopyByAssignmentOutOfBox(8, "Guard", "System.IDisposable"),
        Verify.CopyByAssignmentOutOfBox(9, "Guard", "object"),
        Verify.CopyByAssignmentOutOfBox(10, "Guard", "object"),
        Verify.CopyByAssignmentOutOfBox(11, "Guard", "object"),
        Verify.CopyByAssignmentOutOfBox(12, "Guard", "object"));

    [TestMethod]
    public Task TakingAValueOutOfABox_IntoAMarkedTypeParameter() => Verify.AnalyzerAsync(
        """
        using Singulink.CodeAnalysis;

        class Pool<[NonCopyable] T>
        {
            public T FromObject(object o) => {|#0:(T)o|};
        }
        """,
        Verify.CopyByAssignmentOutOfBox(0, "T", "object"));

    [TestMethod]
    public Task ContainerFromAPropertyOrAnIndexer_IsStored() => Verify.AnalyzerAsync(
        $$"""
        using System.Collections.Generic;
        using System.Threading.Tasks;

        class Holder
        {
            public List<Guard> Items { get; } = new();
            public Task<Guard> Pending { get; set; } = null!;
            public Guard[] Array { get; } = new Guard[2];
            public List<Guard> this[int index] => Items;
            public List<Guard> GetItems() => new();
            public Guard Created => default;
        }

        class C
        {
            static Holder CreateHolder() => new();

            static List<List<Guard>> CreateLists() => new();

            async Task M(Holder holder, Holder? maybe)
            {
                // A property exposes state, so a container that it gives is stored, as one in a field is.
                Guard a = {|#0:holder.Items[0]|};
                Guard b = {|#1:await holder.Pending|};
                Guard c = {|#2:await holder.Pending|};
                Guard d {|#3:= holder.Array[0]|};
                foreach (Guard item in {|#4:holder.Items|}) { }
                Guard e = {|#5:holder.Pending.Result|};

                // Whatever the property or the indexer is read from.
                Guard f = {|#6:CreateHolder().Items[0]|};
                Guard g = {|#7:CreateHolder()[0][0]|};
                Guard h = {|#8:CreateLists()[0][0]|};
                Guard i = {|#9:CreateLists()[^1][0]|};
                Guard? j = maybe?{|#10:.Items[0]|};
                if (holder is { Items: [var {|#11:k|}, ..] }) { }
                if (CreateHolder() is { Items: [var {|#12:l|}, ..] }) { }

                // Only a method that returns a container hands it over, and a property of checked code that returns a non-copyable value by
                // value gives a new value.
                Guard m = holder.GetItems()[0];
                Guard n = holder.Created;

                // Uses that copy nothing.
                ref Guard reference = ref holder.Array[0];
                holder.Items.Add(default);
                int count = holder.Items.Count;
            }

            // A property of an anonymous type is a field in fact.
            void Anonymous(RwLock rwLock, [Singulink.CodeAnalysis.Kept] List<Guard> list)
            {
                var anonymous = new { {|#16:Guard = rwLock.Enter()|}, List = list };
                Guard a {|#13:= anonymous.Guard|};
                Guard b {|#14:= anonymous.Guard|};
                Guard c = {|#15:anonymous.List[0]|};
            }
        }

        {{Sources.Guard}}
        """,
        Verify.ReadFromStoredContainer(0, "'List<T>.this[int]'", "Guard", canBeMoved: false),
        Verify.ReadFromStoredContainer(1, "'await'", "Guard", canBeMoved: false),
        Verify.ReadFromStoredContainer(2, "'await'", "Guard", canBeMoved: false),
        Verify.CopyByAssignment(3, "Guard"),
        Verify.ForEachOverStoredList(4, "Guard", "item", "holder.Items"),
        Verify.ReadFromStoredContainer(5, "'Task<TResult>.Result'", "Guard", canBeMoved: false),
        Verify.ReadFromStoredContainer(6, "'List<T>.this[int]'", "Guard", canBeMoved: false),
        Verify.ReadFromStoredContainer(7, "'List<T>.this[int]'", "Guard", canBeMoved: false),
        Verify.ReadFromStoredContainer(8, "'List<T>.this[int]'", "Guard", canBeMoved: false),
        Verify.ReadFromStoredContainer(9, "'List<T>.this[int]'", "Guard"),
        Verify.ReadFromStoredContainer(10, "'List<T>.this[int]'", "Guard", canBeMoved: false),
        Verify.CopyByAssignmentIntoVariable(11, "Guard", "k"),
        Verify.CopyByAssignmentIntoVariable(12, "Guard", "l"),
        Verify.CopyByAssignment(13, "Guard"),
        Verify.CopyByAssignment(14, "Guard"),
        Verify.ReadFromStoredContainer(15, "'List<T>.this[int]'", "Guard", canBeMoved: false),
        Verify.AnonymousTypeMember(16, "Guard", "Guard"));

    [TestMethod]
    public Task VariableThatLivesOn_IsStored() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using System.Collections.Generic;
        using System.Threading.Tasks;
        using Singulink.CodeAnalysis;

        // A parameter of a primary constructor that a member reads is a field in fact: every call gives a copy.
        class WithPrimaryConstructor(Guard guard)
        {
            public Guard Get() => {|#0:guard|};

            public Guard GetByStatement()
            {
                return {|#1:guard|};
            }
        }

        [NonCopyable]
        struct StructWithPrimaryConstructor(Guard guard)
        {
            public Guard Get() => {|#2:guard|};
        }

        class C
        {
            static Guard Make() => default;

            // A local or a parameter that a lambda or a local function captures is a field in fact: every call gives a copy.
            Func<Guard> Lambda()
            {
                Guard a = Make();
                Func<Guard> f = () => {|#3:a|};
                Guard LocalFunction() => {|#4:a|};
                Func<Guard> g = LocalFunction;
                return f;
            }

            Func<Guard> LambdaOverParameter(Guard p) => () => {|#5:p|};

            Func<Func<Guard>> Nested(Guard p) => () => () => {|#6:p|};

            // An iterator goes on after it has yielded, and its variables with it.
            IEnumerable<Guard> Iterator()
            {
                Guard a = Make();

                while (true)
                    yield return {|#7:a|};
            }

            IEnumerable<Guard> IteratorOverParameter(Guard {|#12:p|})
            {
                yield return {|#8:p|};
            }

            async IAsyncEnumerable<Guard> AsyncIterator()
            {
                Guard a = Make();
                await Task.Yield();

                // The iterator goes on, but it never uses the variable again.
                yield return a;
            }

            // The 'using' owns the variable: the caller would get a copy of a guard that is disposed.
            Guard UsingDeclaration()
            {
                using var g = Make();
                return {|#10:g|};
            }

            Guard UsingStatement()
            {
                using (var g = Make())
                {
                    return {|#11:g|};
                }
            }

            // A variable that ends with the method can be returned, also from a lambda or a local function that declares it.
            Guard Local()
            {
                Guard a = Make();
                return a;
            }

            Guard Parameter(Guard p) => p;

            Guard? Wrapped()
            {
                Guard a = Make();
                return a;
            }

            async Task<Guard> Async()
            {
                Guard a = Make();
                await Task.Yield();
                return a;
            }

            void Functions()
            {
                Func<Guard, Guard> identity = static x => x;
                Func<Guard> create = () => { Guard own = Make(); return own; };
                Guard LocalFunction(Guard q) => q;
            }

            IEnumerable<Guard> MovingIterator()
            {
                Guard a = Make();
                yield return NonCopyable.Move(ref a);
                yield return Make();
            }
        }

        {{Sources.Guard}}
        """,
        Verify.CopyByReturnOfLivingVariable(0, "Guard", "guard"),
        Verify.CopyByReturnOfLivingVariable(1, "Guard", "guard"),
        Verify.CopyByReturnOfLivingVariable(2, "Guard", "guard"),
        Verify.CopyByReturnOfLivingVariable(3, "Guard", "a"),
        Verify.CopyByReturnOfLivingVariable(4, "Guard", "a"),
        Verify.CopyByReturnOfLivingVariable(5, "Guard", "p"),
        Verify.CopyByReturnOfLivingVariable(6, "Guard", "p"),
        Verify.CopyByReturnOfLivingVariable(7, "Guard", "a"),
        Verify.CopyByReturnOfLivingVariable(8, "Guard", "p"),
        Verify.CopyByReturnOfLivingVariable(10, "Guard", "g"),
        Verify.CopyByReturnOfLivingVariable(11, "Guard", "g"),
        Verify.IteratorParameter(12, "Guard", "p", "C.IteratorOverParameter(Guard)"));

    [TestMethod]
    public Task ContainerInAField_IsStoredWhateverTheFieldIsReadFrom() => Verify.AnalyzerAsync(
        $$"""
        using System.Collections.Generic;
        using System.Threading.Tasks;
        using Singulink.CodeAnalysis;

        struct Box
        {
            public List<Guard> L;
            public Task<Guard> T;
        }

        [NonCopyable]
        struct Bag
        {
            public List<Guard> L;
            public Guard G;
        }

        class Wrapper
        {
            public List<Guard> L = null!;

            public Wrapper() { }

            public Wrapper(List<Guard> l) => L = l;
        }

        class C
        {
            List<Guard> _list = new();
            Task<Guard> _task = null!;
            Box _box;

            static Guard Make() => default;

            static Bag MakeBag() => default;

            static Box Wrap([Kept] Task<Guard> task) => new Box { T = task };

            Box GetBox() => _box;

            async Task M(RwLock rwLock)
            {
                // The struct is new, but the field holds a reference to a container that whoever put it there may hold too.
                Guard a = {|#0:new Box { L = _list }.L[0]|};
                Guard b = {|#1:await new Box { T = _task }.T|};
                Guard c = {|#2:new Bag { L = _list }.L[0]|};
                Guard d = {|#3:GetBox().L[0]|};
                Guard e = {|#4:(Make(), _list).Item2[0]|};
                Guard f = {|#5:new KeyValuePair<int, List<Guard>>(1, _list).Value[0]|};
                Guard g = {|#6:await Wrap(_task).T|};
                Guard h = {|#7:new Wrapper { L = _list }.L[0]|};
                Guard i = {|#8:new Wrapper(_list).L[0]|};
                Guard j = {|#9:(Make(), rwLock.EnterTaskAsync()).Item2.Result|};

                // A listed member that is given a tuple with a stored list in it hands out a value that is not new, and the list in the field
                // of that value is stored as well. The tuple itself is a temporary copy that is only read.
                Guard n = {|#10:Task.FromResult((Make(), _list)).Result.Item2[0]|};

                // A non-copyable value is stored inline in a field, so in a field of a new value it is a new value.
                Guard k = MakeBag().G;
                Guard l = new Bag { G = Make() }.G;
                Guard m = (Make(), 1).Item1;

                // A value that is made of a new guard and a stored container is a new value: it can be assigned without a copy.
                var mixed = (Make(), _list);
                var bag = new Bag { G = Make(), L = _list };
            }
        }

        {{Sources.Guard}}
        """,
        Verify.ReadFromStoredContainer(0, "'List<T>.this[int]'", "Guard"),
        Verify.ReadFromStoredContainer(1, "'await'", "Guard"),
        Verify.ReadFromStoredContainer(2, "'List<T>.this[int]'", "Guard"),
        Verify.ReadFromStoredContainer(3, "'List<T>.this[int]'", "Guard"),
        Verify.ReadFromStoredContainer(4, "'List<T>.this[int]'", "Guard"),
        Verify.ReadFromStoredContainer(5, "'List<T>.this[int]'", "Guard"),
        Verify.ReadFromStoredContainer(6, "'await'", "Guard"),
        Verify.ReadFromStoredContainer(7, "'List<T>.this[int]'", "Guard"),
        Verify.ReadFromStoredContainer(8, "'List<T>.this[int]'", "Guard"),
        Verify.ReadFromStoredContainer(9, "'Task<TResult>.Result'", "Guard"),
        Verify.ReadFromStoredContainer(10, "'List<T>.this[int]'", "Guard"));

    [TestMethod]
    public Task CreatedValue_IsAsNewAsWhatIsPutIntoIt() => Verify.AnalyzerAsync(
        $$"""
        using System.Threading.Tasks;

        public struct Pair<T>
        {
            public Task<T> First;
            public Task<T> Second;

            public Pair(Task<T> first) => First = first;
        }

        public class Holder<T>
        {
            public Holder() { }

            public Holder(Task<T> task) { }

            public Task<T>? Task;
        }

        // Listed for this test by the user list: each hands out what a task in its argument holds.
        public static class Readers
        {
            public static T OfTuple<T>((T First, Task<T> Second) pair) => pair.Second.Result;

            public static T OfPair<T>(Pair<T> pair) => pair.First.Result;

            public static T OfHolder<T>(Holder<T> holder) => holder.Task!.Result;
        }

        class C
        {
            void M(RwLock rwLock, [Singulink.CodeAnalysis.Kept] Task<Guard> stored)
            {
                // A tuple, by its elements.
                Guard a = Readers.OfTuple((rwLock.Enter(), rwLock.EnterTaskAsync()));
                Guard b = {|#0:Readers.OfTuple((rwLock.Enter(), stored))|};

                // A struct or an object, by its initializer and by its arguments. A listed constructor of a non-copyable struct is judged
                // where the struct is created.
                Guard c = Readers.OfPair(new Pair<Guard> { First = rwLock.EnterTaskAsync(), Second = rwLock.EnterTaskAsync() });
                Guard d = {|#1:Readers.OfPair(new Pair<Guard> { First = rwLock.EnterTaskAsync(), Second = stored })|};
                Guard e = Readers.OfPair(new Pair<Guard>(rwLock.EnterTaskAsync()));
                Guard f = Readers.OfPair({|#2:new Pair<Guard>(stored)|});
                Guard g = Readers.OfHolder(new Holder<Guard>(rwLock.EnterTaskAsync()));
                Guard h = {|#3:Readers.OfHolder(new Holder<Guard>(stored))|};
                Guard i = {|#4:Readers.OfHolder(new Holder<Guard> { Task = stored })|};

                // One thing in another.
                Guard j = {|#5:Readers.OfTuple((rwLock.Enter(), (stored, 1).Item1))|};
            }
        }

        {{Sources.Guard}}
        """,
        """
        M:Readers.OfTuple``1(System.ValueTuple{``0,System.Threading.Tasks.Task{``0}})
        M:Readers.OfPair``1(Pair{``0})
        M:Readers.OfHolder``1(Holder{``0})
        M:Pair`1.#ctor
        M:Pair`1.#ctor(System.Threading.Tasks.Task{`0})
        M:Holder`1.#ctor
        M:Holder`1.#ctor(System.Threading.Tasks.Task{`0})
        """,
        Verify.ReadFromStoredContainer(0, "'Readers.OfTuple<T>((T First, Task<T> Second))'", "Guard"),
        Verify.ReadFromStoredContainer(1, "'Readers.OfPair<T>(Pair<T>)'", "Guard"),
        Verify.CreatedFromStoredContainer(2, "Pair<Guard>"),
        Verify.ReadFromStoredContainer(3, "'Readers.OfHolder<T>(Holder<T>)'", "Guard"),
        Verify.ReadFromStoredContainer(4, "'Readers.OfHolder<T>(Holder<T>)'", "Guard"),
        Verify.ReadFromStoredContainer(5, "'Readers.OfTuple<T>((T First, Task<T> Second))'", "Guard"));

    [TestMethod]
    public Task CapturedVariable_IsStoredInTheMethodThatDeclaresItAsWell() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using Singulink.CodeAnalysis;

        class C
        {
            Action _action;

            static Guard Make() => default;

            // The closure keeps the variable, so what the method returns is a second copy next to it.
            Guard CapturedByLambda()
            {
                Guard guard = Make();
                _action = () => guard.Dispose();
                return {|#0:guard|};
            }

            Guard CapturedByLocalFunction()
            {
                Guard guard = Make();
                void Local() => guard.Dispose();
                _action = Local;
                return {|#1:guard|};
            }

            Guard CapturedParameter(Guard guard)
            {
                _action = () => guard.Dispose();
                return {|#2:guard|};
            }

            Guard? CapturedAfterTheReturn(bool early)
            {
                Guard guard = Make();

                if (early)
                    return {|#3:guard|};

                _action = () => guard.Dispose();
                return null;
            }

            Func<Guard> CapturedInANestedFunction() => () => {
                Guard guard = Make();
                Action inner = () => guard.Dispose();
                return {|#4:guard|};
            };

            // A moved value is a new one, and a variable that nothing captures ends with the method.
            Guard Moved()
            {
                Guard guard = Make();
                _action = () => guard.Dispose();
                return NonCopyable.Move(ref guard);
            }

            Guard NotCaptured()
            {
                Guard guard = Make();
                Guard other = Make();
                _action = () => other.Dispose();
                return guard;
            }
        }

        {{Sources.Guard}}
        """,
        Verify.CopyByReturnOfLivingVariable(0, "Guard", "guard"),
        Verify.CopyByReturnOfLivingVariable(1, "Guard", "guard"),
        Verify.CopyByReturnOfLivingVariable(2, "Guard", "guard"),
        Verify.CopyByReturnOfLivingVariable(3, "Guard?", "guard"),
        Verify.CopyByReturnOfLivingVariable(4, "Guard", "guard"));

    [TestMethod]
    public Task Assignment_UsedAsAValueIsAReadOfItsTarget() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using System.Collections.Generic;

        static class Extensions
        {
            public static void ByValue(this Guard guard) { }
            public static void ByReference(this in Guard guard) { }
        }

        class Holder
        {
            public Guard Field;

            public Guard Kept { set => Field = value; }
        }

        class C
        {
            static readonly List<Guard> s_list = new();

            Guard _held;
            Guard _second;

            static void Take(Guard guard) { }
            static void Look(in Guard guard) { }
            static void LookReadOnly(ref readonly Guard guard) { }

            // The result of an assignment is the value that its target then holds, read by value: the guard stays in the field, and
            // what uses the result gets a copy of it.
            void Argument(RwLock rwLock)
            {
                Guard guard = rwLock.Enter();
                Take({|#0:_held = guard|});
            }

            void ArgumentOfANewValue(RwLock rwLock) => Take({|#1:_held = rwLock.Enter()|});

            void TupleElement(RwLock rwLock)
            {
                var tuple = ({|#2:_held = rwLock.Enter()|}, 1);
            }

            void ListedMember(RwLock rwLock) => s_list.Add({|#3:_held = rwLock.Enter()|});

            void CollectionExpression(RwLock rwLock)
            {
                Guard[] guards = [{|#4:_held = rwLock.Enter()|}];
            }

            void ArrayInitializer(RwLock rwLock)
            {
                Guard[] guards = { {|#5:_held = rwLock.Enter()|} };
            }

            void CollectionInitializer(RwLock rwLock)
            {
                var guards = new List<Guard> { ({|#6:_held = rwLock.Enter()|}) };
            }

            void Interpolation(RwLock rwLock)
            {
                string text = $"{ {|#7:_held = rwLock.Enter()|} }";
            }

            void ReceiverThatIsCopied(RwLock rwLock) => ({|#8:_held = rwLock.Enter()|}).ByValue();

            void Using(RwLock rwLock)
            {
                using ({|#9:_held = rwLock.Enter()|})
                {
                }
            }

            void Chained(RwLock rwLock) => Take({|#10:_held = {|#11:_second = rwLock.Enter()|}|});

            void IntoALocal(RwLock rwLock)
            {
                Guard guard;
                Take({|#12:guard = rwLock.Enter()|});
                guard.Dispose();
            }

            void IntoAnElement(Guard[] guards, RwLock rwLock) => Take({|#13:guards[0] = rwLock.Enter()|});

            void OfAStoredValue() => Take({|#14:_ = _held|});

            IEnumerable<Guard> Yielded(RwLock rwLock)
            {
                yield return {|#15:_held = rwLock.Enter()|};
            }

            Func<Guard> InALambdaThatReturnsIt(RwLock rwLock) => () => {|#16:_held = rwLock.Enter()|};

            Guard Returned(RwLock rwLock) => {|#17:_held = rwLock.Enter()|};

            void Declared(RwLock rwLock)
            {
                var copy {|#18:= _held = rwLock.Enter()|};
            }

            // Nothing uses the result, or what uses it takes a reference.
            void Statement(RwLock rwLock) => _held = rwLock.Enter();

            void Discarded(RwLock rwLock)
            {
                _ = _held = rwLock.Enter();
            }

            void ByReference(RwLock rwLock)
            {
                int value = (_held = rwLock.Enter()).Value;
            }

            // An assignment is a value and not a variable: an 'in' parameter is given a temporary copy of it.
            void ToAnInParameter(RwLock rwLock)
            {
                Look({|#19:_held = rwLock.Enter()|});
                ({|#20:_held = rwLock.Enter()|}).ByReference();
            }

            void InALoop()
            {
                for (int i = 0; i < 1; _held = default)
                {
                }
            }

            void InALambdaThatReturnsNothing(RwLock rwLock)
            {
                Action action = () => _held = rwLock.Enter();
            }

            // The members of an object initializer are assignments that nothing uses.
            void ObjectInitializer(RwLock rwLock)
            {
                var holder = new Holder { Field = rwLock.Enter(), Kept = rwLock.Enter() };
            }

            void OnlyIfNotNull(Holder holder, RwLock rwLock)
            {
                holder?.Field = rwLock.Enter();
            }

            // A new value that goes to a discard is still a new value.
            void DiscardOfANewValue(RwLock rwLock)
            {
                Take(_ = rwLock.Enter());

                using (_ = rwLock.Enter())
                {
                }
            }
        }

        {{Sources.Guard}}
        """,
        Verify.CopyOfStoredValue(0, "'C.Take(Guard)' receives", "Guard"),
        Verify.CopyOfStoredValue(1, "'C.Take(Guard)' receives", "Guard"),
        Verify.CopyOfStoredValue(2, "The tuple takes", "Guard"),
        Verify.CopyOfStoredValue(3, "'List<Guard>.Add(Guard)' receives", "Guard"),
        Verify.CopyOfStoredValue(4, "The collection takes", "Guard"),
        Verify.CopyOfStoredValue(5, "The array takes", "Guard"),
        Verify.CopyOfStoredValue(6, "'List<Guard>.Add(Guard)' receives", "Guard"),
        Verify.CopyOfStoredValue(7, "The interpolated string takes", "Guard"),
        Verify.CopyOfStoredValue(8, "'Extensions.ByValue(Guard)' receives", "Guard"),
        Verify.CopyOfStoredValue(9, "'using' disposes", "Guard"),
        Verify.CopyOfStoredValue(10, "'C.Take(Guard)' receives", "Guard"),
        Verify.CopyByAssignment(11, "Guard"),
        Verify.CopyOfStoredValue(12, "'C.Take(Guard)' receives", "Guard"),
        Verify.CopyOfStoredValue(13, "'C.Take(Guard)' receives", "Guard"),
        Verify.CopyOfStoredValue(14, "'C.Take(Guard)' receives", "Guard"),
        Verify.CopyByReturn(15, "Guard"),
        Verify.CopyByReturn(16, "Guard"),
        Verify.CopyByReturn(17, "Guard"),
        Verify.CopyByAssignment(18, "Guard"),
        Verify.CopyOfStoredValue(19, "'C.Look(in Guard)' receives", "Guard"),
        Verify.CopyOfStoredValue(20, "'Extensions.ByReference(in Guard)' receives", "Guard"));

    [TestMethod]
    public Task OperationWithNoJudgment_IsAStoredValueWhereUsedByValue() => Verify.AnalyzerAsync(
        $$"""
        using System;

        unsafe class C
        {
            static void Take(Guard guard) { }
            static void Look(in Guard guard) { }
            static void Change(ref Guard guard) { }

            // '__refvalue' needs no 'unsafe'. Roslyn gives it as an operation of no kind, as it does a pointer dereference.
            void RefValue(TypedReference reference)
            {
                Take({|#0:__refvalue(reference, Guard)|});
                Guard copy {|#1:= __refvalue(reference, Guard)|};

                // In place it is what it is.
                __refvalue(reference, Guard).Dispose();
                Look(__refvalue(reference, Guard));
                Change(ref __refvalue(reference, Guard));
            }

            void Pointer(Guard* pointer)
            {
                Take({|#2:*pointer|});
                Take({|#3:pointer[1]|});
                Guard copy {|#4:= *pointer|};

                (*pointer).Dispose();
                pointer[1].Dispose();
                Look(*pointer);
                Change(ref pointer[1]);
            }

            // The call of a function pointer is a kind that no rule visits.
            void FunctionPointer(delegate*<ref Guard> byReference, delegate*<Guard> byValue)
            {
                Take({|#5:byReference()|});
                Take({|#6:byValue()|});

                byReference().Dispose();
                Change(ref byReference());
                Look(byReference());
            }
        }

        {{Sources.Guard}}
        """,
        Verify.CopyOfStoredValue(0, "'C.Take(Guard)' receives", "Guard"),
        Verify.CopyByAssignment(1, "Guard"),
        Verify.CopyOfStoredValue(2, "'C.Take(Guard)' receives", "Guard"),
        Verify.CopyOfStoredValue(3, "'C.Take(Guard)' receives", "Guard"),
        Verify.CopyByAssignment(4, "Guard"),
        Verify.CopyOfStoredValue(5, "'C.Take(Guard)' receives", "Guard"),
        Verify.CopyOfStoredValue(6, "'C.Take(Guard)' receives", "Guard"));

    [TestMethod]
    public Task ArgumentForAnInParameterThatIsNotAVariable_IsPassedByValue() => Verify.AnalyzerAsync(
        $$"""
        using System;

        class C
        {
            private Guard _a;
            private Guard _b;
            private Guard? _maybe;

            static void Take(Guard guard) { }
            static void Look(in Guard guard) { }
            static void LookAtNullable(in Guard? guard) { }
            static void Read(ref readonly Guard guard) { }

            // The compiler copies each argument into a temporary and passes a reference to that. A conditional is reported with each branch
            // that is not new, as by value.
            void Stored(bool flag, int k)
            {
                Take({|#6:flag ? {|#7:_a|} : {|#8:_b|}|});
                Look({|#0:flag ? {|#9:_a|} : {|#10:_b|}|});
                Look({|#1:{|#11:_maybe|} ?? {|#12:_a|}|});
                Look({|#2:k switch { 0 => _a, _ => _b }|});
                Look({|#3:(Guard)_maybe!|});
                LookAtNullable({|#4:_a|});
                Read({|#5:flag ? {|#13:_a|} : {|#14:_b|}|});
            }

            // A new value is fine, as it is by value.
            void New(bool flag, RwLock rwLock)
            {
                Look(flag ? rwLock.Enter() : default);
                Look(rwLock.Enter());
                LookAtNullable(rwLock.Enter());
            }

            // A variable is passed by reference, as before.
            void Variables(Guard[] array)
            {
                Look(_a);
                Look((_a));
                Look(array[0]);
                LookAtNullable(_maybe);
                Read(in _b);
            }
        }

        {{Sources.Guard}}
        """,
        Verify.CopyOfStoredValue(0, "'C.Look(in Guard)' receives", "Guard"),
        Verify.CopyOfStoredValue(1, "'C.Look(in Guard)' receives", "Guard"),
        Verify.CopyOfStoredValue(2, "'C.Look(in Guard)' receives", "Guard"),
        Verify.CopyOfStoredValue(3, "'C.Look(in Guard)' receives", "Guard"),
        Verify.CopyOfStoredValue(4, "'C.LookAtNullable(in Guard?)' receives", "Guard?"),
        Verify.CopyOfStoredValue(5, "'C.Read(ref readonly Guard)' receives", "Guard"),
        Verify.CopyOfStoredValue(6, "'C.Take(Guard)' receives", "Guard"),
        Verify.UnsupportedUse(7, "Guard", "FieldReference"),
        Verify.UnsupportedUse(8, "Guard", "FieldReference"),
        Verify.UnsupportedUse(9, "Guard", "FieldReference"),
        Verify.UnsupportedUse(10, "Guard", "FieldReference"),
        Verify.UnsupportedUse(11, "Guard?", "FieldReference"),
        Verify.UnsupportedUse(12, "Guard", "FieldReference"),
        Verify.UnsupportedUse(13, "Guard", "FieldReference"),
        Verify.UnsupportedUse(14, "Guard", "FieldReference"));
}
