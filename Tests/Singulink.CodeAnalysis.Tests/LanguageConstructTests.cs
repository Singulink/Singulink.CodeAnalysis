using PrefixClassName.MsTest;

namespace Singulink.CodeAnalysis.Tests;

/// <summary>
/// Tests of the language constructs that read from a value without a member being named. Each is as new as what it reads from.
/// </summary>
/// <remarks>
/// Docs: articles/concepts/new-and-stored-values.md.
/// </remarks>
[PrefixTestClass]
public class LanguageConstructTests
{
    [TestMethod]
    public Task ExamplesOfTheSpecification() => Verify.AnalyzerAsync(
        $$"""
        using Singulink.CodeAnalysis;

        class C
        {
            void M(RwLock rwLock, object boxed)
            {
                if (rwLock.TryEnter() is { } guard) { }

                Guard? maybe = rwLock.TryEnter();
                if (maybe is { } {|#0:copy|}) { }
                if (NonCopyable.Move(ref maybe) is { } moved) { }
                {|#1:maybe|}?.Dispose();

                var (entered, ok) = rwLock.TryEnterTuple();
                var result = rwLock.TryEnterTuple();
                result.guard.Dispose();
                Guard taken {|#2:= result.guard|};
                result.guard.Dispose();

                if (boxed is Guard {|#3:unboxed|}) { }
            }
        }

        {{Sources.Guard}}
        """,
        Verify.CopyByAssignmentIntoVariable(0, "Guard", "copy"),
        Verify.CopyOfStoredValue(1, "'?.' runs on", "Guard?"),
        Verify.CopyByAssignment(2, "Guard"),
        Verify.CopyByAssignmentOutOfBox(3, "Guard", "object"));

    [TestMethod]
    public Task Pattern_VariableIsAsNewAsTheMatchedValue() => Verify.AnalyzerAsync(
        $$"""
        using System.Collections.Generic;

        class Holder
        {
            public Guard Field;
            public Guard Property => default;
            public ref Guard Reference => ref Field;
        }

        class C
        {
            static object CreateBoxed() => new Guard();

            static Holder CreateHolder() => new();

            static KeyValuePair<int, Guard> CreatePair() => default;

            void M(RwLock rwLock, object boxed, Guard? maybe, (Guard guard, bool ok) tuple, Holder holder, KeyValuePair<int, Guard> pair, Guard guard)
            {
                // Declaration, type and property patterns, in 'is' and in both kinds of 'switch'. What is matched out of a box is a copy of
                // what the box holds, whatever gives the reference to the box.
                if (maybe is Guard {|#0:a|}) { }
                if (maybe is var {|#1:b|}) { }
                if (CreateBoxed() is Guard {|#12:c|}) { }
                if (guard is { Value: 1 } {|#2:d|}) { }
                if (guard is { Value: 1 }) { }
                if (maybe is not { } {|#3:e|}) { }
                if (boxed is not Guard {|#4:f|}) { }

                switch (boxed)
                {
                    case Guard {|#5:g|} when g.Value > 0:
                        break;
                    case int number:
                        break;
                }

                int value = maybe switch { { } {|#6:h|} => h.Value, null => 0 };
                int newValue = rwLock.TryEnter() switch { { } i => i.Value, null => 0 };

                // A positional pattern of a tuple gives its elements as they are.
                if (tuple is (var {|#7:j|}, true)) { }
                if (rwLock.TryEnterTuple() is (var k, true)) { }

                // A field of a value type is as new as the value. A field of a reference type is a stored value.
                if (tuple is { guard: var {|#8:l|} }) { }
                if (rwLock.TryEnterTuple() is { guard: var m }) { }
                if (CreateHolder() is { Field: var {|#9:n|} }) { }

                // A property is a member like any other: checked code gives a new value, a reference gives a stored one, and a listed member
                // gives what it was called on.
                if (holder is { Property: var o }) { }
                if (holder is { Reference: var {|#10:p|} }) { }
                if (pair is { {|#11:Value|}: var q }) { }
                if (CreatePair() is { Value: var r }) { }

                // Each is used again here, so none of the patterns above is at the last use of what it matches.
                _ = (maybe, tuple, pair);
            }
        }

        {{Sources.Guard}}
        """,
        Verify.CopyByAssignmentIntoVariable(0, "Guard", "a"),
        Verify.CopyByAssignmentIntoVariable(1, "Guard?", "b"),
        Verify.CopyByAssignmentIntoVariable(2, "Guard", "d"),
        Verify.CopyByAssignmentIntoVariable(3, "Guard", "e"),
        Verify.CopyByAssignmentOutOfBox(4, "Guard", "object"),
        Verify.CopyByAssignmentOutOfBox(5, "Guard", "object"),
        Verify.CopyByAssignmentIntoVariable(6, "Guard", "h"),
        Verify.CopyByAssignmentIntoVariable(7, "Guard", "j"),
        Verify.CopyByAssignmentIntoVariable(8, "Guard", "l"),
        Verify.CopyByAssignmentIntoVariable(9, "Guard", "n"),
        Verify.CopyByAssignmentIntoVariable(10, "Guard", "p"),
        Verify.ReadFromStoredContainer(11, "'KeyValuePair<TKey, TValue>.Value'", "Guard"),
        Verify.CopyByAssignmentOutOfBox(12, "Guard", "object"));

    [TestMethod]
    public Task Deconstruction_VariablesAreAsNewAsTheDeconstructedValue() => Verify.AnalyzerAsync(
        $$"""
        using System.Collections.Generic;
        using Singulink.CodeAnalysis;

        [NonCopyable]
        struct Pair
        {
            public Guard First;
            public Guard Second;

            // Checked code: what it hands out is a new value.
            public void Deconstruct(out Guard first, out Guard second)
            {
                first = NonCopyable.Move(ref First);
                second = NonCopyable.Move(ref Second);
            }
        }

        class C
        {
            Guard _field;

            static KeyValuePair<int, Guard> CreatePair() => default;

            void M(RwLock rwLock, (Guard guard, bool ok) tuple, ((Guard guard, int number) inner, bool ok) nested, Pair pair, KeyValuePair<int, Guard> keyValue)
            {
                var (a, aOk) = rwLock.TryEnterTuple();
                (Guard b, bool bOk) = rwLock.TryEnterTuple();
                (_field, bool cOk) = rwLock.TryEnterTuple();
                var (_, dOk) = tuple;

                var ({|#0:e|}, eOk) = tuple;
                (Guard {|#1:f|}, bool fOk) = tuple;
                ({|#2:_field|}, bool gOk) = tuple;
                var (({|#3:h|}, hNumber), hOk) = nested;

                // The elements of a tuple literal are assigned one by one.
                (Guard i, bool iOk) = (rwLock.Enter(), true);
                (Guard j, bool jOk) = ({|#4:a|}, true);
                (_field, var k) = (rwLock.Enter(), rwLock.Enter());

                // A Deconstruct method is a member like any other.
                var (first, second) = pair;
                var (key, value) = CreatePair();
                var (storedKey, {|#5:storedValue|}) = keyValue;

                // Each is used again here, so none of the deconstructions above is at the last use of what it reads.
                _ = (tuple, nested, a, keyValue);
            }
        }

        {{Sources.Guard}}
        """,
        Verify.CopyByAssignmentIntoVariable(0, "Guard", "e"),
        Verify.CopyByAssignmentIntoVariable(1, "Guard", "f"),
        Verify.CopyByAssignment(2, "Guard"),
        Verify.CopyByAssignmentIntoVariable(3, "Guard", "h"),
        Verify.CopyByAssignment(4, "Guard"),
        Verify.CopyByAssignmentIntoVariable(5, "Guard", "storedValue"));

    [TestMethod]
    public Task TupleLiteral_ElementsAreMovedIn() => Verify.AnalyzerAsync(
        $$"""
        using System.Collections.Generic;
        using Singulink.CodeAnalysis;

        class C
        {
            Guard _field;

            (Guard, bool) Create(RwLock rwLock) => (rwLock.Enter(), true);

            (Guard, bool) CreateFromLocal(RwLock rwLock)
            {
                var guard = rwLock.Enter();
                return (NonCopyable.Move(ref guard), true);
            }

            (Guard, bool) CopyField() => ({|#0:_field|}, true);

            void M(RwLock rwLock, Guard parameter)
            {
                var a = (rwLock.Enter(), 1);
                var b = (new Guard(), default(Guard), 1);
                (Guard? maybe, int number) c = (rwLock.Enter(), 1);
                var d = ({|#1:parameter|}, 1);

                // An array and a collection expression take their elements the same way.
                var array = new[] { rwLock.Enter(), new Guard() };
                Guard[] fromLocal = new[] { {|#2:parameter|} };

                // The last use of the parameter moves it in.
                List<Guard> list = [rwLock.Enter(), parameter];
            }
        }

        {{Sources.Guard}}
        """,
        Verify.CopyOfStoredValue(0, "The tuple takes", "Guard"),
        Verify.CopyOfStoredValue(1, "The tuple takes", "Guard"),
        Verify.CopyOfStoredValue(2, "The array takes", "Guard"));

    [TestMethod]
    public Task TupleLiteral_WithContainers() => Verify.AnalyzerAsync(
        $$"""
        using System.Collections.Generic;
        using System.Threading.Tasks;

        class C
        {
            List<Guard> _list = new();

            void M(RwLock rwLock, Task<Guard> stored)
            {
                // A tuple with a non-copyable element is a new value, whatever containers are put into it beside the element.
                var mixed = (rwLock.Enter(), _list);

                // A container in a field is always stored, also in a field of a tuple that has just been made. A non-copyable value that is
                // read straight from the literal is the element that was put there.
                Guard a = {|#0:(rwLock.Enter(), stored).Item2.Result|};
                Guard b = {|#3:(rwLock.Enter(), rwLock.EnterTaskAsync()).Item2.Result|};
                Guard c = (rwLock.Enter(), 1).Item1;
                Guard d = {|#1:(stored, 1).Item1.Result|};
                var containers = (stored, _list);
                Guard e = {|#2:containers.Item1.Result|};
            }
        }

        {{Sources.Guard}}
        """,
        Verify.ReadFromStoredContainer(0, "'Task<TResult>.Result'", "Guard"),
        Verify.ReadFromStoredContainer(1, "'Task<TResult>.Result'", "Guard"),
        Verify.ReadFromStoredContainer(2, "'Task<TResult>.Result'", "Guard"),
        Verify.ReadFromStoredContainer(3, "'Task<TResult>.Result'", "Guard"));

    [TestMethod]
    public Task RangeOfAnArray_CopiesTheElements() => Verify.AnalyzerAsync(
        $$"""
        using System;

        class C
        {
            Guard[] _array = new Guard[2];

            static Guard[] MakeArray() => new Guard[2];

            void M(Span<Guard> span)
            {
                // The compiler makes a new array with a copy of every element, by a call of RuntimeHelpers.GetSubArray.
                Guard[] a = {|#0:_array[..]|};
                Guard[] b = {|#1:_array[0..1]|};
                var c = {|#2:_array[1..]|};
                Span<Guard> d = {|#3:_array[0..1]|};
                Guard[] e = {|#4:MakeArray()[..1]|};
                if (_array is [_, {|#5:.. var rest|}]) { }
                if (MakeArray() is [_, {|#6:.. [_, ..]|}]) { }

                // A range of a span is a view of the same elements, an index gives a reference, and '..' without a pattern takes no slice.
                Span<Guard> f = span[1..];
                Span<Guard> g = _array.AsSpan()[1..];
                ref Guard h = ref _array[^1];
                if (_array is [_, ..]) { }
                if (span is [_, .. var spanRest]) { }
            }
        }

        {{Sources.Guard}}
        """,
        SubArray(0),
        SubArray(1),
        SubArray(2),
        SubArray(3),
        SubArray(4),
        SubArray(5),
        SubArray(6));

    [TestMethod]
    public Task ListPattern_UsesTheMembersOfItsType() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using System.Collections.Generic;

        class MyList<T>
        {
            public int Count => 0;
            public T this[int index] => default!;
            public MyList<T> Slice(int start, int length) => this;
        }

        class C
        {
            Guard[] _array = new Guard[2];
            List<Guard> _list = new();

            void M(MyList<Guard> myList, Span<Guard> span, RwLock rwLock)
            {
                // The length or count, the indexer and, for a slice, Slice are member uses: of an unmarked generic they must be listed.
                if (myList is {|#0:[var a]|}) { }
                if (myList is {|#1:[_, {|#2:.. var b|}]|}) { }

                // An element that the indexer of a stored list hands out by value is a copy, and so is one that is read from a reference.
                if (_list is [var {|#3:c|}, ..]) { }
                if (_array is [var {|#4:d|}, ..]) { }
                if (span is [var {|#5:e|}, .. var f]) { }

                // Read from a new list it is a new value.
                if (rwLock.CreateGuards() is [var g, ..]) { }

                if (_list is [_, _]) { }
                if (_array is []) { }
            }
        }

        {{Sources.Guard}}
        """,
        Verify.UnlistedMember(0, "MyList<T>.Count", "Guard", "T", "P:MyList`1.Count"),
        Verify.UnlistedMember(0, "MyList<T>.this[int]", "Guard", "T", "P:MyList`1.Item(System.Int32)"),
        Verify.UnlistedMember(1, "MyList<T>.Count", "Guard", "T", "P:MyList`1.Count"),
        Verify.UnlistedMember(1, "MyList<T>.this[int]", "Guard", "T", "P:MyList`1.Item(System.Int32)"),
        Verify.UnlistedMember(2, "MyList<T>.Slice(int, int)", "Guard", "T", "M:MyList`1.Slice(System.Int32,System.Int32)"),
        Verify.CopyByAssignmentIntoVariable(3, "Guard", "c"),
        Verify.CopyByAssignmentIntoVariable(4, "Guard", "d"),
        Verify.CopyByAssignmentIntoVariable(5, "Guard", "e"));

    [TestMethod]
    public Task CollectionExpression_UsesTheConstructorAndAddOfItsType() => Verify.AnalyzerAsync(
        $$"""
        using System.Collections;
        using System.Collections.Generic;

        class MyCollection<T> : IEnumerable<T>
        {
            public void Add(T value) { }
            public IEnumerator<T> GetEnumerator() => null!;
            IEnumerator IEnumerable.GetEnumerator() => null!;
        }

        class Sack<T> : IEnumerable<T>
        {
            public IEnumerator<T> GetEnumerator() => null!;
            IEnumerator IEnumerable.GetEnumerator() => null!;
        }

        static class SackExtensions
        {
            public static void Add<T>(this Sack<T> sack, T value) { }
        }

        class C
        {
            void M(RwLock rwLock, Guard[] stored)
            {
                // The user list has the constructors and not the Add methods, which the compiler calls for each element.
                MyCollection<Guard> a = {|#0:[rwLock.Enter()]|};
                var b = new MyCollection<Guard> { {|#1:rwLock.Enter()|} };
                Sack<Guard> c = {|#2:[rwLock.Enter()]|};
                MyCollection<Guard> d = {|#3:[{|#4:.. stored|}]|};

                // Without an element there is no call of Add, and both members of List<T> are on the built-in list.
                MyCollection<Guard> e = [];
                List<Guard> f = [rwLock.Enter()];
                MyCollection<int> g = [1];
            }
        }

        {{Sources.Guard}}
        """,
        """
        M:MyCollection`1.#ctor
        M:Sack`1.#ctor
        """,
        Verify.UnlistedMember(0, "MyCollection<T>.Add(T)", "Guard", "T", "M:MyCollection`1.Add(`0)"),
        Verify.UnlistedMember(1, "MyCollection<T>.Add(T)", "Guard", "T", "M:MyCollection`1.Add(`0)"),
        Verify.UnlistedMember(2, "SackExtensions.Add<T>(Sack<T>, T)", "Guard", "T", "M:SackExtensions.Add``1(Sack{``0},``0)"),
        Verify.UnlistedMember(3, "MyCollection<T>.Add(T)", "Guard", "T", "M:MyCollection`1.Add(`0)"),
        Verify.ReadFromStoredContainer(4, "'..'", "Guard"));

    private static Microsoft.CodeAnalysis.Testing.DiagnosticResult SubArray(int location) => Verify.UnlistedMember(
        location,
        "RuntimeHelpers.GetSubArray<T>(T[], Range)",
        "Guard",
        "T",
        "M:System.Runtime.CompilerServices.RuntimeHelpers.GetSubArray``1(``0[],System.Range)");

    [TestMethod]
    public Task ForEach_ByValueVariableOverReferencesCopiesEachElement() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using System.Collections.Generic;
        using System.Runtime.InteropServices;

        class C
        {
            static Guard[] CreateArray() => new Guard[4];

            void M(RwLock rwLock, Guard[] array, Guard[,] matrix, Span<Guard> span, ReadOnlySpan<Guard> readOnlySpan, List<Guard> list)
            {
                foreach (Guard {|#0:item|} in array) { }
                foreach (var {|#1:item|} in matrix) { }
                foreach (Guard {|#2:item|} in span) { }
                foreach (var {|#3:item|} in readOnlySpan) { }
                foreach (var {|#4:item|} in CreateArray()) { }

                foreach (ref Guard item in span) { }
                foreach (ref readonly Guard item in span) { }
                foreach (ref readonly Guard item in readOnlySpan) { }
                foreach (ref Guard item in array.AsSpan()) { }
                foreach (ref Guard item in CollectionsMarshal.AsSpan(list)) { }

                // An element that is handed out by value is as new as the collection.
                foreach (Guard item in rwLock.CreateGuards()) { }
                foreach (Guard item in {|#5:list|}) { }

                // The variables of a deconstruction are as new as the element.
                foreach (var (guard, number) in CreateTuples()) { }
                foreach (var ({|#6:guard|}, number) in new (Guard, int)[1]) { }

                // Elements that do not carry the non-copyable type are not looked at.
                foreach (int number in new int[1]) { }
                foreach (char c in "text") { }
            }

            static IEnumerable<int> Numbers() => [];

            static Sequence CreateTuples() => default;
        }

        // Checked code: a non-generic collection of a non-copyable type.
        struct Sequence
        {
            public Enumerator GetEnumerator() => default;

            public struct Enumerator
            {
                public (Guard, int) Current => default;
                public bool MoveNext() => false;
            }
        }

        {{Sources.Guard}}
        """,
        Verify.CopyByAssignmentIntoForEachVariable(0, "Guard", "item"),
        Verify.CopyByAssignmentIntoForEachVariable(1, "Guard", "item"),
        Verify.CopyByAssignmentIntoForEachVariable(2, "Guard", "item"),
        Verify.CopyByAssignmentIntoForEachVariable(3, "Guard", "item"),
        Verify.CopyByAssignmentIntoForEachVariable(4, "Guard", "item"),
        Verify.ForEachOverStoredList(5, "Guard", "item", "list"),
        Verify.CopyByAssignmentIntoVariable(6, "Guard", "guard"));

    [TestMethod]
    public Task FieldOfAValueType_IsAsNewAsTheValue() => Verify.AnalyzerAsync(
        $$"""
        class Holder
        {
            public Guard Field;
        }

        class C
        {
            void M(RwLock rwLock, (Guard guard, bool ok) stored)
            {
                Guard fromNew = rwLock.TryEnterTuple().guard;
                Guard fromStored {|#0:= stored.guard|};
                stored.guard.Dispose();
                ref Guard reference = ref stored.guard;

                // A field of an object is a stored value, even if the object has just been created.
                Guard fromObject {|#1:= new Holder().Field|};
            }
        }

        {{Sources.Guard}}
        """,
        Verify.CopyByAssignment(0, "Guard"),
        Verify.CopyByAssignment(1, "Guard"));

    [TestMethod]
    public Task ArrayOrCollectionCreatedFromContainers_IsAsNewAsWhatIsPutIntoIt() => Verify.AnalyzerAsync(
        $$"""
        using System.Collections.Generic;
        using System.Threading.Tasks;

        static class Tasks
        {
            // Listed for this test by the user list: it hands out what the first task of the array holds.
            public static T First<T>(params Task<T>[] tasks) => tasks[0].Result;
        }

        class C
        {
            void M(RwLock rwLock, [Singulink.CodeAnalysis.Kept] Task<Guard> stored)
            {
                Guard fromNew = Tasks.First(rwLock.EnterTaskAsync(), rwLock.EnterTaskAsync());
                Guard fromStored = {|#0:Tasks.First(rwLock.EnterTaskAsync(), stored)|};
                Guard fromNewArray = Tasks.First(new[] { rwLock.EnterTaskAsync() });
                Guard fromStoredArray = {|#1:Tasks.First(new[] { stored })|};
            }
        }

        {{Sources.Guard}}
        """,
        "M:Tasks.First``1(System.Threading.Tasks.Task{``0}[])",
        Verify.ReadFromStoredContainer(0, "'Tasks.First<T>(params Task<T>[])'", "Guard"),
        Verify.ReadFromStoredContainer(1, "'Tasks.First<T>(params Task<T>[])'", "Guard"));

    [TestMethod]
    public Task Using_AwaitUsingAndConditionalAccess() => Verify.AnalyzerAsync(
        $$"""
        using System.Threading.Tasks;

        class C
        {
            async Task M(RwLock rwLock, Guard? maybe)
            {
                using (rwLock.Enter()) { }
                using var held = await rwLock.EnterAsync();
                using var maybeHeld = rwLock.TryEnter();

                // A new value is used in place. A stored nullable gives a copy.
                rwLock.TryEnter()?.Dispose();
                {|#0:maybe|}?.Dispose();
                int? value = rwLock.TryEnter()?.Value;
            }
        }

        {{Sources.Guard}}
        """,
        Verify.CopyOfStoredValue(0, "'?.' runs on", "Guard?"));

    [TestMethod]
    public Task Iterator_CannotHaveAByValueParameterOfANonCopyableType() => Verify.AnalyzerAsync(
        $$"""
        using System.Collections.Generic;
        using System.Threading.Tasks;
        using Singulink.CodeAnalysis;

        class C
        {
            // The compiler keeps the arguments in the object that the iterator returns and copies them into every enumerator.
            static IEnumerable<int> Run(Guard {|#0:guard|})
            {
                guard.Dispose();
                yield break;
            }

            static async IAsyncEnumerable<int> RunAsync(Guard {|#1:guard|}, int count)
            {
                await Task.Yield();
                yield return count;
            }

            static IEnumerable<int> Generic<[NonCopyable] T>(T {|#2:value|}, ValueTask<Guard> {|#3:pending|}, Guard? {|#4:maybe|})
            {
                yield return 1;
            }

            // An iterator that returns an enumerator and not a sequence keeps its arguments once.
            static IEnumerator<int> Enumerator(Guard guard)
            {
                yield return 1;
            }

            static async IAsyncEnumerator<int> AsyncEnumerator(Guard guard)
            {
                await Task.Yield();
                yield return 1;
            }

            static System.Collections.IEnumerator UntypedEnumerator(Guard guard)
            {
                yield return 1;
            }

            void Local()
            {
                IEnumerable<int> Inner(Guard {|#5:guard|})
                {
                    yield return 1;
                }
            }

            // A container, a copyable value and a method that is not an iterator are fine.
            static IEnumerable<int> OverContainer(List<Guard> guards, Task<Guard> pending, int count)
            {
                yield return count;
            }

            static IEnumerable<int> NotAnIterator(Guard guard) => new[] { guard.Value };

            // In a member that requires a copyable type, the type parameter counts as one.
            [RequiresCopyable]
            static IEnumerable<T> Copies<[NonCopyable] T>(T value)
            {
                yield return value;
            }
        }

        {{Sources.Guard}}
        """,
        Verify.IteratorParameter(0, "Guard", "guard", "C.Run(Guard)"),
        Verify.IteratorParameter(1, "Guard", "guard", "C.RunAsync(Guard, int)"),
        Verify.IteratorParameter(2, "T", "value", "C.Generic<T>(T, ValueTask<Guard>, Guard?)"),
        Verify.IteratorParameter(3, "System.Threading.Tasks.ValueTask<Guard>", "pending", "C.Generic<T>(T, ValueTask<Guard>, Guard?)"),
        Verify.IteratorParameter(4, "Guard?", "maybe", "C.Generic<T>(T, ValueTask<Guard>, Guard?)"),
        Verify.IteratorParameter(5, "Guard", "guard", "Inner(Guard)"));

    [TestMethod]
    public Task TemporaryCopy_IsReportedOnlyForAMemberThatIsNotReadOnly() => Verify.AnalyzerAsync(
        $$"""
        using System.Collections.Generic;
        using Singulink.CodeAnalysis;

        [NonCopyable]
        struct Held
        {
            public int Count;
            public Guard Inner;

            public bool IsHeld
            {
                get
                {
                    Count++;
                    return true;
                }
            }

            public readonly bool IsSet => Count > 0;

            public void Release() => Count = 0;

            public readonly int Read() => Count;
        }

        [NonCopyable]
        struct Maker
        {
            public readonly Held Fresh => default;
        }

        class C
        {
            Held? _maybe;
            Held _held;
            Maker _maker;
            object _box = null!;

            static Held Make() => default;

            void M(Held? maybe, Held local, (Held? held, int number) pair)
            {
                // The compiler takes a copy out of a stored nullable, out of a box and out of anything but a local before it looks into
                // it. A member that is not readonly runs on that copy, and what it changes is lost.
                {|#0:maybe|}?.Release();
                bool a = {|#1:maybe|}?.IsHeld ?? false;
                if (maybe is {|#2:{ IsHeld: true }|}) { }
                if (_maybe is not {|#3:{ IsHeld: true }|}) { }
                if (pair is ({|#4:{ IsHeld: true }|}, 2)) { }
                int b = maybe switch { {|#5:{ IsHeld: true }|} => 1, _ => 0 };
                if (_box is {|#6:Held { IsHeld: true }|}) { }
                if (_held is {|#7:{ IsHeld: true }|}) { }
                int c = _held switch { {|#8:{ IsHeld: true }|} => 1, _ => 0 };

                // A value of a non-copyable type that the pattern keeps is a second copy that can be used.
                if (maybe is { IsSet: true } {|#9:copy|}) { }
                if (maybe is {|#10:{ Inner: var inner }|}) { }

                // What a listed getter hands out is taken from the copy, and what a getter of checked code returns is a new value.
                if (_box is {|#11:KeyValuePair<int, Held> { Value.IsHeld: true }|}) { }
                if (_box is KeyValuePair<int, Held> { Value.IsSet: true }) { }
                if (_maker is { Fresh.IsHeld: true }) { }

                // A copy that is only the receiver of readonly members, or that is only read through fields, does no harm.
                int? d = maybe?.Read();
                bool? e = maybe?.IsSet;
                int? f = maybe?.Count;
                if (maybe is { IsSet: true, Count: 1 }) { }
                if (pair is ({ IsSet: true }, 2)) { }
                if (_box is Held { IsSet: true }) { }
                if (_held is { IsSet: true, Count: > 0 }) { }
                if (_held is { Inner.Value: 1 }) { }

                // A test for null reads nothing, a local is matched in place, and a new value is the only one there is.
                if (maybe is { }) { }
                if (maybe is null) { }
                if (maybe is not null) { }
                if (local is { IsHeld: true }) { }
                if (Make() is { IsHeld: true }) { }
            }
        }

        {{Sources.Guard}}
        """,
        Verify.CopyOfStoredValue(0, "'?.' runs on", "Held?"),
        Verify.CopyOfStoredValue(1, "'?.' runs on", "Held?"),
        Verify.CopyOfStoredValue(2, "The pattern reads from", "Held?"),
        Verify.CopyOfStoredValue(3, "The pattern reads from", "Held?"),
        Verify.CopyOfStoredValue(4, "The pattern reads from", "Held?"),
        Verify.CopyOfStoredValue(5, "The pattern reads from", "Held?"),
        Verify.CopyByAssignmentOutOfBox(6, "Held", "object"),
        Verify.CopyOfStoredValue(7, "The pattern reads from", "Held"),
        Verify.CopyOfStoredValue(8, "The pattern reads from", "Held"),
        Verify.CopyByAssignmentIntoVariable(9, "Held", "copy"),
        Verify.CopyOfStoredValue(10, "The pattern reads from", "Held?"),
        Verify.CopyByAssignmentOutOfBox(11, "System.Collections.Generic.KeyValuePair<int, Held>", "object"));

    [TestMethod]
    public Task Operators_InstanceOperatorsRunInPlace() => Verify.AnalyzerAsync(
        $$"""
        using Singulink.CodeAnalysis;

        [NonCopyable]
        struct Counter
        {
            public int Value;

            public void operator +=(int amount) => Value += amount;

            public void operator ++() => Value++;

            public static bool operator !(Counter counter) => counter.Value is 0;

            public static bool operator ==(Counter left, Counter right) => left.Value == right.Value;

            public static bool operator !=(Counter left, Counter right) => left.Value != right.Value;

            public override bool Equals(object obj) => false;

            public override int GetHashCode() => 0;
        }

        class C
        {
            Counter _counter;

            void M(Counter parameter)
            {
                // An instance operator runs on its target in place.
                _counter += 1;
                ++_counter;
                parameter += 2;

                // A stored operand that an operator takes by value is copied into it. The parameter is used for the last time.
                bool isZero = !{|#0:_counter|};
                bool same = {|#1:_counter|} == parameter;
            }
        }
        """,
        Verify.CopyOfStoredValue(0, "'Counter.operator !(Counter)' receives", "Counter"),
        Verify.CopyOfStoredValue(1, "'Counter.operator ==(Counter, Counter)' receives", "Counter"));

    [TestMethod]
    public Task CoalesceAssignment_AssignsItsValueAsAnAssignmentDoes() => Verify.AnalyzerAsync(
        $$"""
        class C
        {
            Guard? _maybe;

            void M(RwLock rwLock, Guard? parameter, Guard stored)
            {
                // The target is tested and assigned in place, and a new value is moved into it.
                _maybe ??= rwLock.Enter();
                _maybe ??= rwLock.TryEnter();
                parameter ??= default(Guard);

                // A stored value is copied, as by '='.
                _maybe ??= {|#0:stored|};
                _maybe ??= {|#1:parameter|};

                // At its last use a variable is moved from.
                _maybe ??= stored;
                _maybe ??= parameter;
            }
        }

        {{Sources.Guard}}
        """,
        Verify.CopyByAssignment(0, "Guard"),
        Verify.CopyByAssignment(1, "Guard?"));

    [TestMethod]
    public Task InterpolatedStringHandler_FormatsAContainerAsAPlainStringDoes() => Verify.AnalyzerAsync(
        $$"""
        using System.Collections.Generic;
        using System.Runtime.CompilerServices;

        [InterpolatedStringHandler]
        ref struct LogHandler
        {
            public LogHandler(int literalLength, int formattedCount) { }

            public void AppendLiteral(string text) { }

            public void AppendFormatted<T>(T value) { }
        }

        class C
        {
            static void Log(LogHandler handler) { }

            void M(RwLock rwLock, List<Guard> list)
            {
                // A task is erasable, so it can be formatted as an object, by the compiler's handler and by any other.
                string text = $"Pending: { rwLock.EnterTaskAsync() }";
                Log($"Pending: { rwLock.EnterTaskAsync() }");

                // A list is not.
                string listText = $"List: { {|#0:list|} }";
                Log($"List: { {|#1:list|} }");
            }
        }

        {{Sources.Guard}}
        """,
        Verify.OpaqueConversion(0, "System.Collections.Generic.List<Guard>", "object"),
        Verify.OpaqueConversion(1, "System.Collections.Generic.List<Guard>", "object"));

    [TestMethod]
    public Task AsyncOrIteratorMember_OfANonCopyableStructRunsOnACopy() => Verify.AnalyzerAsync(
        """
        using System;
        using System.Collections.Generic;
        using System.Threading.Tasks;
        using Singulink.CodeAnalysis;

        interface IRunner { Task RunAsync(); }

        [NonCopyable]
        struct AsyncGuard : IAsyncDisposable, IRunner
        {
            private int _state;

            // The compiler copies 'this' into the state machine: the caller's guard is left as it was.
            public async ValueTask {|#0:DisposeAsync|}()
            {
                await Task.Yield();
                _state = 0;
            }

            public IEnumerable<int> {|#1:Steps|}()
            {
                yield return _state;
            }

            public async IAsyncEnumerable<int> {|#2:StepsAsync|}()
            {
                await Task.Yield();
                yield return _state;
            }

            public IEnumerable<int> Items
            {
                {|#3:get|}
                {
                    yield return _state;
                }
            }

            async Task IRunner.{|#4:RunAsync|}() => await Task.Yield();

            // A member that is neither async nor an iterator changes the struct itself, and a static one has no 'this'.
            public ValueTask DisposeFastAsync()
            {
                _state = 0;
                return default;
            }

            public Task ReleaseAsync()
            {
                _state = 0;
                return WaitAsync();
            }

            private static async Task WaitAsync() => await Task.Yield();

            // A readonly member sees a copy as well, which the caller can change behind its back.
            public readonly async Task {|#6:ReadAsync|}() => await Task.Yield();
        }

        struct Wrapper<[NonCopyable] T>
        {
            public async Task {|#5:RunAsync|}() => await Task.Yield();
        }

        // No copy of a readonly struct can ever differ from the original.
        [NonCopyable]
        readonly struct Fixed
        {
            private readonly int _version;

            public async Task RunAsync() => await Task.Yield();

            public IEnumerable<int> Steps()
            {
                yield return _version;
            }
        }

        // Copying a copyable struct or a reference is fine.
        struct Copyable
        {
            public async Task RunAsync() => await Task.Yield();
        }

        class AClass
        {
            public async Task RunAsync() => await Task.Yield();
        }
        """,
        Verify.AsyncOrIteratorMember(0, "AsyncGuard.DisposeAsync()", "AsyncGuard", "async"),
        Verify.AsyncOrIteratorMember(1, "AsyncGuard.Steps()", "AsyncGuard", "iterator"),
        Verify.AsyncOrIteratorMember(2, "AsyncGuard.StepsAsync()", "AsyncGuard", "async"),
        Verify.AsyncOrIteratorMember(3, "AsyncGuard.Items.get", "AsyncGuard", "iterator"),
        Verify.AsyncOrIteratorMember(4, "AsyncGuard.IRunner.RunAsync()", "AsyncGuard", "async"),
        Verify.AsyncOrIteratorMember(5, "Wrapper<T>.RunAsync()", "Wrapper<T>", "async"),
        Verify.AsyncOrIteratorMember(6, "AsyncGuard.ReadAsync()", "AsyncGuard", "async"));

    [TestMethod]
    public Task PatternThatBoxesAStoredValue_BoxesACopy() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using Singulink.CodeAnalysis;

        interface IHeld { bool IsHeld { get; } }

        [NonCopyable]
        struct Held : IHeld
        {
            public bool IsHeld => true;
        }

        class C<[NonCopyable] T> where T : IDisposable
        {
            Guard _guard;
            Held _held;
            T _value = default!;

            void M(RwLock rwLock)
            {
                // The variable is a boxed copy of the stored value, as after 'as'.
                if (_guard is {|#0:IDisposable disposable|}) { }
                if (_value is {|#1:IDisposable fromTypeParameter|}) { }
                if (_guard is {|#2:object boxed|}) { }
                if (_held is {|#3:IHeld { IsHeld: true } held|}) { }

                switch (_guard)
                {
                    case {|#4:IDisposable inSwitch|}:
                        break;
                }

                int number = _guard switch { {|#5:IDisposable inExpression|} => 1 };

                // So is what a pattern looks into without declaring a variable.
                if (_held is {|#6:IHeld { IsHeld: true }|}) { }

                // A test of the type boxes nothing that can be used, for a struct and for a type parameter alike.
                if (_guard is IDisposable) { }
                if (_value is IDisposable) { }
                if (_guard is IDisposable { }) { }
                if (_value is not IDisposable) { }

                // A new value is moved into the box.
                if (rwLock.Enter() is IDisposable moved) { }
            }
        }

        {{Sources.Guard}}
        """,
        Verify.Boxing(0, "Guard", "System.IDisposable"),
        Verify.Boxing(1, "T", "System.IDisposable"),
        Verify.Boxing(2, "Guard", "object"),
        Verify.Boxing(3, "Held", "IHeld"),
        Verify.Boxing(4, "Guard", "System.IDisposable"),
        Verify.Boxing(5, "Guard", "System.IDisposable"),
        Verify.Boxing(6, "Held", "IHeld"));

    [TestMethod]
    public Task Operators_AreCallsOfTheirMethods() => Verify.AnalyzerAsync(
        """
        using Singulink.CodeAnalysis;

        [NonCopyable]
        struct Counter
        {
            public int Value;

            public static Counter Make() => default;

            public static Counter operator +(Counter left, int right) => left;

            public static Counter operator -(in Counter left, int right) => default;

            public static Counter operator -(Counter counter) => counter;

            public static bool operator ==(Counter left, Counter right) => left.Value == right.Value;

            public static bool operator !=(Counter left, Counter right) => left.Value != right.Value;

            public static implicit operator Counter(int value) => default;

            public static explicit operator int(in Counter counter) => counter.Value;

            public override bool Equals(object obj) => false;

            public override int GetHashCode() => 0;
        }

        class C
        {
            Counter _counter;

            static void Take(Counter counter) { }

            void M(Counter parameter)
            {
                // A new operand is moved into the operator, as into any parameter, and what the operator returns is a new value.
                Counter a = Counter.Make() + 1;
                _counter = Counter.Make() + 2;
                Counter b = -Counter.Make();
                Counter c = (Counter.Make() + 1) + 2;
                Take(Counter.Make() + 3);
                bool same = Counter.Make() == Counter.Make();

                // A stored operand can be passed by reference.
                Counter d = _counter - 1;
                Counter e = parameter - 1;
                int number = (int)_counter;

                // One that the operator takes by value is copied. The last use of the parameter is moved in.
                Counter f = {|#0:_counter|} + 1;
                Counter g = -{|#1:parameter|};
                bool alsoSame = {|#2:_counter|} == parameter;
            }

            Counter Returned() => Counter.Make() + 1;
        }
        """,
        Verify.CopyOfStoredValue(0, "'Counter.operator +(Counter, int)' receives", "Counter"),
        Verify.CopyOfStoredValue(1, "'Counter.operator -(Counter)' receives", "Counter"),
        Verify.CopyOfStoredValue(2, "'Counter.operator ==(Counter, Counter)' receives", "Counter"));

    [TestMethod]
    public Task ExpressionTree_IsCheckedLikeAnyLambda() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using System.Linq.Expressions;

        class C
        {
            static void Take(Guard guard) { }

            static void TakeRef(ref Guard guard) { }

            void M(RwLock rwLock)
            {
                Guard guard = rwLock.Enter();

                // A tree that is compiled or interpreted uses the captured variable in place, as a lambda does.
                Expression<Action> dispose = () => guard.Dispose();
                Expression<Func<int>> read = () => guard.Value;
                Expression<Action> byReference = () => TakeRef(ref guard);
                Expression<Func<Guard, int>> parameter = value => value.Value;
                Expression<Func<Guard>> create = () => rwLock.Enter();

                // And what copies in a lambda copies in a tree.
                Expression<Action> byValue = () => Take({|#0:guard|});
                Expression<Func<Guard>> returned = () => {|#1:guard|};
                Expression<Func<IDisposable>> boxed = () => {|#2:guard|};
            }
        }

        {{Sources.Guard}}
        """,
        Verify.CopyOfStoredValue(0, "'C.Take(Guard)' receives", "Guard"),
        Verify.CopyByReturnOfLivingVariable(1, "Guard", "guard"),
        Verify.Boxing(2, "Guard", "System.IDisposable"));

    [TestMethod]
    public Task InlineArray_ElementsAreUsedInPlaceLikeThoseOfAnArray() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using System.Runtime.CompilerServices;
        using Singulink.CodeAnalysis;

        // An inline array stores its elements in the struct, so it is a struct with a field of a non-copyable type like any other.
        [InlineArray(4)]
        struct CopyableBuffer
        {
            private Guard {|#0:_element0|};
        }

        [NonCopyable]
        [InlineArray(4)]
        struct GuardBuffer
        {
            private Guard _element0;
        }

        [InlineArray(4)]
        struct Buffer<T>
        {
            private T _element0;
        }

        class C
        {
            GuardBuffer _buffer;
            Buffer<Guard> _generic;

            static GuardBuffer Create() => default;

            void M(RwLock rwLock, ref GuardBuffer byRef, in GuardBuffer byIn)
            {
                // An element is used in place, and a span is a view of the buffer.
                _buffer[0] = rwLock.Enter();
                _buffer[1].Dispose();
                ref Guard reference = ref _buffer[2];
                byRef[0].Dispose();
                _generic[0] = rwLock.Enter();
                _generic[1].Dispose();

                Span<Guard> span = _buffer;
                Span<Guard> part = _buffer[1..];
                ReadOnlySpan<Guard> readOnly = byIn;
                span[0].Dispose();

                var local = new GuardBuffer();
                local[0] = rwLock.Enter();

                foreach (ref Guard guard in _buffer)
                    guard.Dispose();

                foreach (ref readonly Guard guard in byIn)
                {
                    int value = guard.Value;
                }

                // An element of a new buffer is a new value.
                Guard fromNew = Create()[0];

                // Reading an element by value copies it, and copying the buffer copies them all.
                Guard copy {|#1:= _buffer[0]|};
                Guard fromGeneric {|#2:= _generic[0]|};
                GuardBuffer whole {|#3:= _buffer|};
                Buffer<Guard> wholeGeneric {|#4:= _generic|};

                foreach (Guard {|#5:guard|} in _buffer) { }

                // Through a read-only reference to the buffer a member that is not readonly runs on a copy of the element.
                {|#6:byIn[0]|}.Dispose();
            }
        }

        {{Sources.Guard}}
        """,
        Verify.FieldOfCopyableType(0, "Guard", "CopyableBuffer._element0"),
        Verify.CopyByAssignment(1, "Guard"),
        Verify.CopyByAssignment(2, "Guard"),
        Verify.CopyByAssignment(3, "GuardBuffer"),
        Verify.CopyByAssignment(4, "Buffer<Guard>"),
        Verify.CopyByAssignmentIntoForEachVariable(5, "Guard", "guard"),
        Verify.MemberOnReadOnlyReference(6, "Guard", "Guard.Dispose()"));

    [TestMethod]
    public Task StackAlloc_NewValuesAreMovedIntoTheMemory() => Verify.AnalyzerAsync(
        $$"""
        using System;

        class C
        {
            Guard _stored;

            static Guard Make() => default;

            unsafe void M()
            {
                // New values are moved into the memory, as into an array, and the span is a view of it.
                Span<Guard> created = stackalloc Guard[] { Make(), Make() };
                Span<Guard> implicitlyTyped = stackalloc[] { Make() };
                Span<Guard> empty = stackalloc Guard[4];
                empty[0] = Make();
                empty[1].Dispose();
                ref Guard reference = ref empty[2];

                foreach (ref Guard guard in empty)
                    guard.Dispose();

                // A stored value is copied in, and reading an element by value copies it out.
                Span<Guard> copied = stackalloc Guard[] { Make(), {|#0:_stored|} };
                Guard copy {|#1:= empty[0]|};

                foreach (Guard {|#2:guard|} in empty) { }

                // The same through a pointer.
                Guard* pointer = stackalloc Guard[2];
                pointer[0].Dispose();
                Guard viaPointer {|#3:= pointer[0]|};
            }
        }

        {{Sources.Guard}}
        """,
        Verify.CopyOfStoredValue(0, "The stackalloc memory takes", "Guard"),
        Verify.CopyByAssignment(1, "Guard"),
        Verify.CopyByAssignmentIntoForEachVariable(2, "Guard", "guard"),
        Verify.CopyByAssignment(3, "Guard"));

    [TestMethod]
    public Task ExtensionBlock_ByValueReceiverIsAnArgument() => Verify.AnalyzerAsync(
        $$"""
        static class GuardExtensions
        {
            extension(Guard guard)
            {
                public int ValueOfCopy => guard.Value;

                public void DisposeCopy() => guard.Dispose();
            }

            extension(ref Guard guard)
            {
                public void DisposeInPlace() => guard.Dispose();
            }

            extension(in Guard guard)
            {
                public int ValueInPlace => guard.Value;
            }
        }

        class C
        {
            Guard _guard;

            void M(RwLock rwLock)
            {
                // The block takes its receiver by value, so a member of it that is used on a stored value copies it, like a classic
                // extension method with a by-value 'this' parameter.
                int a = {|#0:_guard|}.ValueOfCopy;
                {|#1:_guard|}.DisposeCopy();

                // A new value is moved in, and a receiver that is taken by reference is used in place.
                int b = rwLock.Enter().ValueOfCopy;
                rwLock.Enter().DisposeCopy();
                _guard.DisposeInPlace();
                int c = _guard.ValueInPlace;
            }
        }

        {{Sources.Guard}}
        """,
        Verify.CopyOfStoredValue(0, "'GuardExtensions.extension(Guard).ValueOfCopy' receives", "Guard"),
        Verify.CopyOfStoredValue(1, "'GuardExtensions.extension(Guard).DisposeCopy()' receives", "Guard"));

    [TestMethod]
    public Task ExtensionThatTheCompilerCalls_CopiesAStoredValueThatItTakesByValue() => Verify.AnalyzerAsync(
        """
        using System.Collections.Generic;
        using System.Runtime.CompilerServices;
        using System.Threading.Tasks;
        using Singulink.CodeAnalysis;

        [NonCopyable]
        struct Source { }

        static class Extensions
        {
            public static IEnumerator<int> GetEnumerator(this Source source) => throw null!;

            public static TaskAwaiter GetAwaiter(this Source source) => default;

            public static void Deconstruct(this Source source, out int a, out int b) => (a, b) = (0, 0);
        }

        [NonCopyable]
        struct ByReference { }

        static class ByReferenceExtensions
        {
            public static IEnumerator<int> GetEnumerator(this in ByReference source) => throw null!;
        }

        class C
        {
            Source _source;
            ByReference _byReference;

            static Source Create() => default;

            async Task M()
            {
                // There is no argument in the tree for what a 'foreach', an 'await' or a deconstruction passes to an extension method.
                foreach (int item in {|#0:_source|}) { }
                await {|#1:_source|};
                var (a, b) = {|#2:_source|};

                // A new value is moved into the parameter, and by reference nothing is copied.
                foreach (int item in Create()) { }
                await Create();
                var (c, d) = Create();
                foreach (int item in _byReference) { }
            }
        }
        """,
        Verify.CopyOfStoredValue(0, "'Extensions.GetEnumerator(Source)' receives", "Source"),
        Verify.CopyOfStoredValue(1, "'Extensions.GetAwaiter(Source)' receives", "Source"),
        Verify.CopyOfStoredValue(2, "'Extensions.Deconstruct(Source, out int, out int)' receives", "Source"));

    [TestMethod]
    public Task ReadOnlyReference_MembersThatTheCompilerCallsRunOnACopyToo() => Verify.AnalyzerAsync(
        """
        using System;
        using System.Runtime.CompilerServices;
        using System.Threading.Tasks;
        using Singulink.CodeAnalysis;

        [NonCopyable]
        struct Buffer
        {
            public int Length => 0;

            public readonly int this[int index] => 0;

            public readonly Buffer Slice(int start, int length) => default;

            public TaskAwaiter GetAwaiter() => default;

            public void Touch() { }

            public readonly int Peek() => 0;
        }

        [NonCopyable]
        ref struct Holder
        {
            public ref Buffer Writable;
            public ref readonly Buffer ReadOnly;
        }

        [NonCopyable]
        readonly struct Captures(Buffer buffer)
        {
            // A parameter of a primary constructor that a member uses is a field in fact, and a readonly one in a readonly member.
            public void Touch() => {|#0:buffer|}.Touch();

            public int Peek() => buffer.Peek();
        }

        class C
        {
            readonly Buffer _buffer;
            Buffer _writable;

            public C()
            {
                // A readonly field is a variable in a constructor of its type.
                _buffer.Touch();
                Action later = () => {|#1:_buffer|}.Touch();
            }

            async Task M(Index index, bool flag)
            {
                // An index from the end and a range read the length.
                _ = {|#2:_buffer|}[^1];
                _ = {|#3:_buffer|}[index];
                _ = {|#4:_buffer|}[1..^1];
                _ = _buffer[1..3];
                _ = _writable[^1];

                await {|#5:_buffer|};
                await _writable;

                // A conditional by value gives a temporary copy of a stored value.
                ({|#6:flag ? _writable : _writable|}).Touch();
                (flag ? ref _writable : ref _writable).Touch();
            }

            void Refs(in Buffer readOnly, ref Buffer writable, scoped Holder holder)
            {
                ref readonly Buffer local = ref readOnly;

                // A ref assignment gives the reference that it stores.
                ({|#7:local = ref writable|}).Touch();
                {|#8:holder.ReadOnly|}.Touch();
                holder.Writable.Touch();
            }

            unsafe void Pointers(Wrapper* wrapper)
            {
                // What a pointer points to is writable.
                wrapper->Buffer.Touch();
            }
        }

        [NonCopyable]
        struct Wrapper
        {
            public Buffer Buffer;
        }
        """,
        Verify.MemberOnReadOnlyReference(0, "Buffer", "Buffer.Touch()"),
        Verify.MemberOnReadOnlyReference(1, "Buffer", "Buffer.Touch()"),
        Verify.MemberOnReadOnlyReference(2, "Buffer", "Buffer.Length"),
        Verify.MemberOnReadOnlyReference(3, "Buffer", "Buffer.Length"),
        Verify.MemberOnReadOnlyReference(4, "Buffer", "Buffer.Length"),
        Verify.MemberOnReadOnlyReference(5, "Buffer", "Buffer.GetAwaiter()"),
        Verify.MemberOnReadOnlyReference(6, "Buffer", "Buffer.Touch()"),
        Verify.MemberOnReadOnlyReference(7, "Buffer", "Buffer.Touch()"),
        Verify.MemberOnReadOnlyReference(8, "Buffer", "Buffer.Touch()"));

    [TestMethod]
    public Task Deconstruct_IsCalledOnWhatIsDeconstructed() => Verify.AnalyzerAsync(
        """
        using System;
        using Singulink.CodeAnalysis;

        [NonCopyable]
        struct Pair
        {
            private int _first;

            public void Deconstruct(out int first, out int second) => (first, second) = (_first++, 0);
        }

        [NonCopyable]
        struct ReadOnlyPair
        {
            public readonly void Deconstruct(out int first, out int second) => (first, second) = (0, 0);
        }

        class C
        {
            readonly Pair _readOnly;
            Pair _pair;

            void M(in Pair byIn, ref Pair byRef, in ReadOnlyPair readOnlyPair, Pair[] array, Span<Pair> span, ReadOnlyPair[] readOnlyArray)
            {
                // What is deconstructed is the receiver of Deconstruct. Through a read-only reference the compiler runs a method that
                // is not readonly on a copy.
                var (a, b) = {|#0:byIn|};
                var (c, d) = {|#1:_readOnly|};
                (a, b) = {|#2:byIn|};

                // And so it does through any other reference: it copies what is deconstructed into a temporary first. A readonly
                // method cannot change what it runs on.
                var (e, f) = {|#5:byRef|};
                var (g, h) = {|#6:_pair|};
                var (i, j) = readOnlyPair;

                // A 'foreach' copies each element of an array or a span before it deconstructs it.
                foreach ({|#3:var (k, l)|} in array) { }
                foreach ({|#4:var (m, n)|} in span) { }
                foreach (var (o, p) in readOnlyArray) { }

                foreach (ref Pair pair in span)
                {
                    var (q, r) = {|#7:pair|};
                    pair.Deconstruct(out int s, out int t);
                }
            }
        }
        """,
        Verify.MemberOnReadOnlyReference(0, "Pair", "Pair.Deconstruct(out int, out int)"),
        Verify.MemberOnReadOnlyReference(1, "Pair", "Pair.Deconstruct(out int, out int)"),
        Verify.MemberOnReadOnlyReference(2, "Pair", "Pair.Deconstruct(out int, out int)"),
        Verify.DeconstructionOfStoredValue(5, "Pair", "Pair.Deconstruct(out int, out int)"),
        Verify.DeconstructionOfStoredValue(6, "Pair", "Pair.Deconstruct(out int, out int)"),
        Verify.CopyByAssignmentForDeconstruct(3, "Pair", "Pair.Deconstruct(out int, out int)"),
        Verify.CopyByAssignmentForDeconstruct(4, "Pair", "Pair.Deconstruct(out int, out int)"),
        Verify.DeconstructionOfStoredValue(7, "Pair", "Pair.Deconstruct(out int, out int)"));

    [TestMethod]
    public Task ReadOnlyReference_AnEventAccessorRunsOnACopyToo() => Verify.AnalyzerAsync(
        """
        using System;
        using Singulink.CodeAnalysis;

        [NonCopyable]
        struct Source
        {
            private Action? _handlers;

            public event Action Changed
            {
                add => _handlers += value;
                remove => _handlers -= value;
            }

            public readonly event Action Ignored
            {
                add { }
                remove { }
            }
        }

        class C
        {
            Source _source;

            void M(in Source byIn, Action handler)
            {
                {|#0:byIn|}.Changed += handler;
                {|#1:byIn|}.Changed -= handler;
                byIn.Ignored += handler;
                _source.Changed += handler;
            }
        }
        """,
        Verify.MemberOnReadOnlyReference(0, "Source", "Source.Changed"),
        Verify.MemberOnReadOnlyReference(1, "Source", "Source.Changed"));

    [TestMethod]
    public Task Fixed_CallsGetPinnableReferenceOnTheValueInPlace() => Verify.AnalyzerAsync(
        """
        using Singulink.CodeAnalysis;

        [NonCopyable]
        struct Pinnable
        {
            private int[] _data;

            public ref int GetPinnableReference() => ref _data[0];
        }

        [NonCopyable]
        struct ReadOnlyPinnable
        {
            private int[] _data;

            public readonly ref int GetPinnableReference() => ref _data[0];
        }

        unsafe class C
        {
            Pinnable _writable;
            readonly Pinnable _readOnly;
            readonly ReadOnlyPinnable _readOnlyPinnable;

            void M(in Pinnable byIn)
            {
                fixed (int* a = _writable) { }
                fixed (int* b = _readOnlyPinnable) { }

                // Through a read-only reference the compiler calls a method that is not readonly on a copy.
                fixed (int* c = {|#0:_readOnly|}) { }
                fixed (int* d = {|#1:byIn|}) { }
            }
        }
        """,
        Verify.MemberOnReadOnlyReference(0, "Pinnable", "Pinnable.GetPinnableReference()"),
        Verify.MemberOnReadOnlyReference(1, "Pinnable", "Pinnable.GetPinnableReference()"));

    [TestMethod]
    public Task Conversion_ThatChangesOnlyTupleNamesOrNullabilityIsNoUseOfItsOwn() => Verify.AnalyzerAsync(
        $$"""
        #nullable enable
        using System.Threading.Tasks;

        class C
        {
            // The return of a tuple local whose element names differ from those of the return type is the return of that local.
            static (Guard guard, int number) Named(RwLock rwLock)
            {
                var result = (rwLock.Enter(), 1);
                return result;
            }

            static (Guard, int) Unnamed(RwLock rwLock)
            {
                (Guard guard, int number) result = (rwLock.Enter(), 1);
                return result;
            }

            static (Guard guard, string? text) Nullable(RwLock rwLock)
            {
                (Guard, string) result = (rwLock.Enter(), "");
                return result;
            }

            static async ValueTask<(Guard guard, int number)> Async(RwLock rwLock)
            {
                var result = (rwLock.Enter(), 1);
                await Task.Yield();
                return result;
            }

            // A copy stays a copy.
            static void Copy(RwLock rwLock)
            {
                var result = (rwLock.Enter(), 1);
                (Guard guard, int number) copy {|#0:= result|};
                (Guard guard, int number) last = result;
            }
        }

        {{Sources.Guard}}
        """,
        Verify.CopyByAssignment(0, "(Guard guard, int number)"));

    [TestMethod]
    public Task Pattern_AValueGoesToOneVariableOnly() => Verify.AnalyzerAsync(
        $$"""
        using System.Collections.Generic;
        using Singulink.CodeAnalysis;

        [NonCopyable]
        struct Lease
        {
            public Guard Inner;
            public Guard Other;
            public int Id;
        }

        class C
        {
            static Lease MakeLease() => default;
            static (Guard, int) MakeTuple() => default;
            static List<Guard> MakeList() => new();

            // The whole and a part of it, whatever the matched value is: the result of a call or a local at its last use.
            void TheWholeAndAPart(Lease local)
            {
                if (MakeLease() is { Inner: var a } {|#0:whole|}) { }
                if (local is { Inner: var b } {|#1:localWhole|}) { }
                if (MakeTuple() is (var c, _) {|#2:tuple|}) { }
            }

            void TheSameValueTwice(RwLock rwLock, Guard local, Guard? maybe)
            {
                if (rwLock.Enter() is var a and var {|#3:b|}) { }
                if (local is var c and var {|#4:d|}) { }
                if (maybe is { } e and { } {|#5:f|}) { }
            }

            void TheSamePartTwice((Guard, int) tuple)
            {
                if (MakeLease() is { Inner: var a } and { Inner: var {|#6:b|} }) { }
                if (tuple is (var c, _) and (var {|#7:d|}, _)) { }
                if (MakeTuple() is (var e, _) and { Item1: var {|#8:f|} }) { }
                if (MakeList() is [var g] and [var {|#9:h|}]) { }

                // The first element and the last one can be the same element.
                if (MakeList() is [var i, ..] and [.., var {|#10:j|}]) { }
            }

            int InASwitchArm(Lease lease) => lease switch { { Inner: var a } {|#11:whole|} => 1 };

            void InACaseLabel(Lease lease)
            {
                switch (lease)
                {
                    case { Inner: var a } {|#12:whole|}:
                        break;
                }
            }

            static Guard OfAMovedFromParameter([MovedFrom] ref Guard value, out Guard second)
            {
                if (value is var a and var {|#13:b|})
                {
                    value = default;
                    second = b;
                    return a;
                }

                value = default;
                second = default;
                return default;
            }

            // One variable for the value, and variables for different parts of it, are as they were.
            void Apart(Lease lease, (Guard, Guard) pair)
            {
                if (MakeLease() is { Inner: var a }) { }
                if (MakeLease() is { Id: var id } whole) { }
                if (lease is { Inner: var b, Other: var c }) { }
                if (pair is (var d, var e)) { }
                if (MakeLease() is { Inner: var f } and { Other: var g }) { }
                if (MakeList() is [var h, var i]) { }
            }
        }

        {{Sources.Guard}}
        """,
        Verify.CopyByAssignmentIntoSecondVariable(0, "Lease", "whole"),
        Verify.CopyByAssignmentIntoSecondVariable(1, "Lease", "localWhole"),
        Verify.CopyByAssignmentIntoSecondVariable(2, "(Guard, int)", "tuple"),
        Verify.CopyByAssignmentIntoSecondVariable(3, "Guard", "b"),
        Verify.CopyByAssignmentIntoSecondVariable(4, "Guard", "d"),
        Verify.CopyByAssignmentIntoSecondVariable(5, "Guard", "f"),
        Verify.CopyByAssignmentIntoSecondVariable(6, "Guard", "b"),
        Verify.CopyByAssignmentIntoSecondVariable(7, "Guard", "d"),
        Verify.CopyByAssignmentIntoSecondVariable(8, "Guard", "f"),
        Verify.CopyByAssignmentIntoSecondVariable(9, "Guard", "h"),
        Verify.CopyByAssignmentIntoSecondVariable(10, "Guard", "j"),
        Verify.CopyByAssignmentIntoSecondVariable(11, "Lease", "whole"),
        Verify.CopyByAssignmentIntoSecondVariable(12, "Lease", "whole"),
        Verify.CopyByAssignmentIntoSecondVariable(13, "Guard", "b"));

    // A compound assignment operator that is an instance member is a call of that member with its right side as the argument: a new
    // value is moved in, and a stored one is copied, as with an argument of any call.
    [TestMethod]
    public Task Operators_AnInstanceCompoundOperatorTakesItsRightSideAsAnArgument() => Verify.AnalyzerAsync(
        $$"""
        using Singulink.CodeAnalysis;

        sealed class Pile<[NonCopyable] T>
        {
            private readonly T[] _items = new T[16];
            private int _count;

            public void operator +=(T item) => _items[_count++] = item;

            public void AddTwice(T first, T second)
            {
                var pile = this;
                pile += first;
                pile += second;
            }
        }

        sealed class Readings<[NonCopyable] T>
        {
            private int _count;

            public void operator +=(in T item) => _count++;
        }

        class C
        {
            Guard _held;

            void M(RwLock rwLock, Pile<Guard> pile, Readings<Guard> readings, Guard parameter)
            {
                pile += rwLock.Enter();
                pile += {|#0:_held|};
                pile += parameter;

                // By reference nothing is copied.
                readings += _held;
            }
        }

        {{Sources.Guard}}
        """,
        Verify.CopyOfStoredValue(0, "'Pile<Guard>.operator +=(Guard)' receives", "Guard"));

    // A deconstruction runs 'Deconstruct' on a copy of its right side, whatever the right side is: the compiler copies it into a
    // temporary first, a local included. So the right side is a new value, a move or a local at its last use, or the method is
    // readonly. Measured with both SDKs, Debug and Release, for a field, a local, a parameter and a reference.
    [TestMethod]
    public Task Deconstruction_RunsDeconstructOnACopyOfItsRightSide() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using Singulink.CodeAnalysis;

        [NonCopyable]
        struct Lease : IDisposable
        {
            private Guard _read, _write;

            // Moves both guards out and leaves the lease empty: on a copy, the lease that was deconstructed still holds both.
            public void Deconstruct(out Guard read, out Guard write)
            {
                read = NonCopyable.Move(ref _read);
                write = NonCopyable.Move(ref _write);
            }

            public void Dispose() { _read.Dispose(); _write.Dispose(); }
        }

        [NonCopyable]
        struct Counts
        {
            public int First, Second;

            public readonly void Deconstruct(out int first, out int second) => (first, second) = (First, Second);
        }

        class Session
        {
            Lease _lease;
            Counts _counts;

            static Lease Make() => default;

            void Field()
            {
                var (read, write) = {|#0:_lease|};
                read.Dispose();
                write.Dispose();
            }

            void LocalThatIsUsedAgain()
            {
                Lease lease = Make();
                var (read, write) = {|#1:lease|};
                read.Dispose();
                write.Dispose();
                lease.Dispose();
            }

            void Parameter(Lease lease, ref Lease byRef)
            {
                Guard read, write;
                (read, write) = {|#2:lease|};
                read.Dispose();
                write.Dispose();
                lease.Dispose();

                var (otherRead, otherWrite) = {|#3:byRef|};
                otherRead.Dispose();
                otherWrite.Dispose();
            }

            void Element(Lease[] leases)
            {
                var (read, write) = {|#4:leases[0]|};
                read.Dispose();
                write.Dispose();
            }

            // A new value, a move and a local at its last use are moved into the temporary, and a readonly method changes nothing.
            void Fine(ref Lease byRef)
            {
                var (a, b) = Make();
                a.Dispose();
                b.Dispose();

                var (c, d) = NonCopyable.Move(ref _lease);
                c.Dispose();
                d.Dispose();

                Lease lease = Make();
                var (e, f) = lease;
                e.Dispose();
                f.Dispose();

                var (first, second) = _counts;

                // The call written out runs in place.
                byRef.Deconstruct(out Guard g, out Guard h);
                g.Dispose();
                h.Dispose();
            }
        }

        {{Sources.Guard}}
        """,
        Verify.DeconstructionOfStoredValue(0, "Lease", "Lease.Deconstruct(out Guard, out Guard)"),
        Verify.DeconstructionOfStoredValue(1, "Lease", "Lease.Deconstruct(out Guard, out Guard)"),
        Verify.DeconstructionOfStoredValue(2, "Lease", "Lease.Deconstruct(out Guard, out Guard)"),
        Verify.DeconstructionOfStoredValue(3, "Lease", "Lease.Deconstruct(out Guard, out Guard)"),
        Verify.DeconstructionOfStoredValue(4, "Lease", "Lease.Deconstruct(out Guard, out Guard)"));

    // The receiver of an extension block is a parameter of every member of the block, and it is not in the parameter list of the
    // member. An iterator keeps it in the object that it returns and copies it into every enumerator, like any other by-value
    // parameter. The block declares the receiver for all of its members, so the finding stands at the member that is the iterator.
    [TestMethod]
    public Task ExtensionBlock_IteratorWithByValueReceiver_IsReportedLikeAParameter() => Verify.AnalyzerAsync(
        $$"""
        using System.Collections.Generic;
        using System.Threading.Tasks;
        using Singulink.CodeAnalysis;

        static class Extensions
        {
            extension(Guard guard)
            {
                // Yields the items while the guard is held, and releases it at the end: once for every enumeration.
                public IEnumerable<T> {|#0:Hold|}<T>(IReadOnlyList<T> items)
                {
                    try
                    {
                        foreach (T item in items)
                            yield return item;
                    }
                    finally
                    {
                        guard.Dispose();
                    }
                }

                public async IAsyncEnumerable<int> {|#1:HoldAsync|}(int count)
                {
                    await Task.Yield();
                    yield return count;
                }

                public IEnumerable<int> Values
                {
                    {|#2:get|}
                    {
                        yield return 1;
                    }
                }

                // A parameter of the member's own is reported where it is declared, as before.
                public IEnumerable<int> {|#3:WithAnother|}(Guard {|#4:other|})
                {
                    yield return 1;
                }

                // An iterator that returns an enumerator and not a sequence keeps its arguments once, and a member that is no
                // iterator keeps nothing.
                public IEnumerator<int> Enumerator()
                {
                    yield return 1;
                }

                public IEnumerable<int> NotAnIterator() => new[] { 1 };

                // A static member of the block is used on nothing, and has no receiver.
                public static IEnumerable<int> Static(int count)
                {
                    yield return count;
                }

                // A local function is not a member of the block.
                public void WithLocalFunction()
                {
                    IEnumerable<int> Inner(int count)
                    {
                        yield return count;
                    }
                }
            }

            extension(Guard? maybe)
            {
                public IEnumerable<int> {|#5:OfNullable|}()
                {
                    yield return 1;
                }
            }

            extension((Guard Guard, int Count) pair)
            {
                public IEnumerable<int> {|#6:OfTuple|}()
                {
                    yield return 1;
                }
            }

            extension<[NonCopyable] T>(T value)
            {
                public IEnumerable<int> {|#7:OfTypeParameter|}()
                {
                    yield return 1;
                }
            }

            // A copyable value is fine.
            extension(int number)
            {
                public IEnumerable<int> OverNumber()
                {
                    yield return number;
                }
            }
        }

        {{Sources.Guard}}
        """,
        Verify.IteratorParameter(0, "Guard", "guard", "Extensions.extension(Guard).Hold<T>(IReadOnlyList<T>)"),
        Verify.IteratorParameter(1, "Guard", "guard", "Extensions.extension(Guard).HoldAsync(int)"),
        Verify.IteratorParameter(2, "Guard", "guard", "Extensions.extension(Guard).Values.get"),
        Verify.IteratorParameter(3, "Guard", "guard", "Extensions.extension(Guard).WithAnother(Guard)"),
        Verify.IteratorParameter(4, "Guard", "other", "Extensions.extension(Guard).WithAnother(Guard)"),
        Verify.IteratorParameter(5, "Guard?", "maybe", "Extensions.extension(Guard?).OfNullable()"),
        Verify.IteratorParameter(6, "(Guard Guard, int Count)", "pair", "Extensions.extension((Guard Guard, int Count)).OfTuple()"),
        Verify.IteratorParameter(7, "T", "value", "Extensions.extension<T>(T).OfTypeParameter()"));

    // And in the other direction: a by-value receiver is moved from at its last use like a by-value parameter, at the call and
    // inside the member, as the 'this' parameter of a classic extension method is.
    [TestMethod]
    public Task ExtensionBlock_ByValueReceiverAtItsLastUse_IsANewValueLikeAParameter() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using System.Collections.Generic;

        static class Sink
        {
            public static void Take(Guard guard)
            {
            }
        }

        static class Extensions
        {
            extension(Guard guard)
            {
                // Inside the member: one read, which is the last use.
                public void Once() => Sink.Take(guard);

                public int Taken
                {
                    get
                    {
                        Sink.Take(guard);
                        return 1;
                    }
                }

                // Two reads: the first is a copy.
                public void Twice()
                {
                    Sink.Take({|#0:guard|});
                    Sink.Take(guard);
                }

                // A lambda captures the receiver, which makes a field of it.
                public Action Captured() => () => Sink.Take({|#1:guard|});

                // A member of the block that is used on the receiver takes it as an argument.
                public void ThroughAnother() => guard.Once();

                public void ThroughAnotherTwice()
                {
                    {|#2:guard|}.Once();
                    guard.Once();
                }
            }

            extension(in Guard guard)
            {
                // A receiver that is taken by reference is the caller's variable.
                public void ByReference() => Sink.Take({|#3:guard|});
            }

            // The classic form, for comparison.
            public static void OnceClassic(this Guard guard) => Sink.Take(guard);
        }

        class C
        {
            Guard _held;

            void AtTheCall(RwLock rwLock, Guard parameter)
            {
                Guard guard = rwLock.Enter();
                guard.Once();

                Guard read = rwLock.Enter();
                int taken = read.Taken;

                Guard classic = rwLock.Enter();
                classic.OnceClassic();

                parameter.Once();

                // Used again: the first use is a copy.
                Guard twice = rwLock.Enter();
                {|#4:twice|}.Once();
                twice.Dispose();

                // A field is a stored value.
                {|#5:_held|}.Once();
            }

            void InATry(RwLock rwLock)
            {
                Guard guard = rwLock.Enter();

                try
                {
                    {|#6:guard|}.Once();
                }
                finally
                {
                    guard.Dispose();
                }
            }
        }

        {{Sources.Guard}}
        """,
        Verify.CopyOfStoredValue(0, "'Sink.Take(Guard)' receives", "Guard"),
        Verify.CopyOfStoredValue(1, "'Sink.Take(Guard)' receives", "Guard"),
        Verify.CopyOfStoredValue(2, "'Extensions.extension(Guard).Once()' receives", "Guard"),
        Verify.CopyOfStoredValue(3, "'Sink.Take(Guard)' receives", "Guard"),
        Verify.CopyOfStoredValue(4, "'Extensions.extension(Guard).Once()' receives", "Guard"),
        Verify.CopyOfStoredValue(5, "'Extensions.extension(Guard).Once()' receives", "Guard"),
        Verify.CopyOfStoredValue(6, "'Extensions.extension(Guard).Once()' receives", "Guard", usedByHandler: "guard"));

    // An 'await foreach' awaits what MoveNextAsync and DisposeAsync return. The members of that awaiting are bound by the compiler,
    // and the compiler that the analyzer is built against does not give them, so an awaitable of the project's own over a
    // non-copyable type or a container is reported as a use that is not known to be safe. A task of the framework, configured or
    // not, and an awaitable that carries nothing are known.
    [TestMethod]
    public Task AwaitForEach_AnAwaitableOfItsOwnThatCarriesANonCopyableTypeIsReported() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using System.Collections.Generic;
        using System.Runtime.CompilerServices;
        using System.Threading.Tasks;

        struct Step<T>
        {
            public T Held;

            public Awaiter GetAwaiter() => default;

            public struct Awaiter : INotifyCompletion
            {
                public bool IsCompleted => true;

                public bool GetResult() => true;

                public void OnCompleted(Action continuation) { }
            }
        }

        struct Done
        {
            public Awaiter GetAwaiter() => default;

            public struct Awaiter : INotifyCompletion
            {
                public bool IsCompleted => true;

                public void GetResult() { }

                public void OnCompleted(Action continuation) { }
            }
        }

        sealed class Steps
        {
            public Enumerator GetAsyncEnumerator() => new();

            public sealed class Enumerator
            {
                public int Current => 1;

                public Step<Guard> MoveNextAsync() => default;

                public ValueTask DisposeAsync() => default;
            }
        }

        sealed class Releases
        {
            public Enumerator GetAsyncEnumerator() => new();

            public sealed class Enumerator
            {
                public int Current => 1;

                public ValueTask<bool> MoveNextAsync() => default;

                public Step<List<Guard>> DisposeAsync() => default;
            }
        }

        sealed class Plain
        {
            public Enumerator GetAsyncEnumerator() => new();

            public sealed class Enumerator
            {
                public int Current => 1;

                public Step<int> MoveNextAsync() => default;

                public Done DisposeAsync() => default;
            }
        }

        sealed class Configured
        {
            public Enumerator GetAsyncEnumerator() => new();

            public sealed class Enumerator
            {
                public int Current => 1;

                public ConfiguredValueTaskAwaitable<bool> MoveNextAsync() => default;

                public ConfiguredTaskAwaitable DisposeAsync() => default;
            }
        }

        sealed class TaskOfAGuard
        {
            public Enumerator GetAsyncEnumerator() => new();

            public sealed class Enumerator
            {
                public int Current => 1;

                public ValueTask<bool> MoveNextAsync() => default;

                public Task<Guard> DisposeAsync() => null!;
            }
        }

        sealed class ConfiguredTaskOfAGuard
        {
            public Enumerator GetAsyncEnumerator() => new();

            public sealed class Enumerator
            {
                public int Current => 1;

                public ValueTask<bool> MoveNextAsync() => default;

                public ConfiguredValueTaskAwaitable<Guard> DisposeAsync() => default;
            }
        }

        static class C
        {
            // A task of the framework is known, also one of a non-copyable type, configured or not.
            static async Task TasksAsync(TaskOfAGuard task, ConfiguredTaskOfAGuard configured)
            {
                await foreach (int n in task)
                {
                }

                await foreach (int n in configured)
                {
                }
            }

            static async Task RunAsync(Steps steps, Releases releases, Plain plain, Configured configured, IAsyncEnumerable<int> sequence)
            {
                await foreach (int n in {|#0:steps|})
                {
                }

                await foreach (int n in {|#1:releases|})
                {
                }

                await foreach (int n in plain)
                {
                }

                await foreach (int n in configured)
                {
                }

                await foreach (int n in sequence)
                {
                }
            }
        }

        {{Sources.Guard}}
        """,
        Verify.UnsupportedUse(0, "Step<Guard>", "Loop"),
        Verify.UnsupportedUse(1, "Step<System.Collections.Generic.List<Guard>>", "Loop"));

    // An operation that runs two members on one value passes a by-value receiver to both: a compound assignment, an increment and a
    // '??=' on a property of an extension block run its getter and its setter, and the compiler evaluates the receiver once. That is
    // reported like an argument that is passed twice, new value or not. A stored receiver is a copy for each of them as well.
    [TestMethod]
    public Task ExtensionBlock_AReceiverThatTwoMembersGetIsPassedTwice() => Verify.AnalyzerAsync(
        $$"""
        using Singulink.CodeAnalysis;

        static class Extensions
        {
            extension(Guard g)
            {
                public int Uses { get => 0; set { } }

                public string? Name { get => null; set { } }
            }

            extension(Guard? g)
            {
                public int Count { get => 0; set { } }
            }

            extension<[NonCopyable] T>(T item)
            {
                public int Size { get => 0; set { } }
            }

            extension(ref Guard g)
            {
                public int InPlace { get => 0; set { } }
            }

            extension(Guard g)
            {
                public int ReadOnly => 0;
            }
        }

        static class C
        {
            static Guard s_held;

            static void Compound(RwLock rwLock)
            {
                Guard g = rwLock.Enter();
                {|#0:g|}.Uses += 1;
            }

            static void Increment(RwLock rwLock)
            {
                Guard g = rwLock.Enter();
                {|#1:g|}.Uses++;
            }

            static void Coalesce(RwLock rwLock)
            {
                Guard g = rwLock.Enter();
                {|#2:g|}.Name ??= "x";
            }

            static void OfANullable(Guard? maybe) => {|#3:maybe|}.Count += 1;

            static void OfATypeParameter<[NonCopyable] T>(T value) => {|#4:value|}.Size += 1;

            // At its last use the parameter is a new value, and still goes to both.
            static void Bump(Guard g) => {|#5:g|}.Uses += 1;

            static void Stored() => {|#6:{|#7:s_held|}|}.Uses += 1;

            // A receiver that is taken by reference is used in place, and a plain set or get runs one member.
            static void Clean(RwLock rwLock, ref Guard held)
            {
                held.InPlace += 1;
                Guard g = rwLock.Enter();
                g.Uses = 1;
                Guard h = rwLock.Enter();
                int read = h.ReadOnly;
            }
        }

        {{Sources.Guard}}
        """,
        Verify.ValuePassedTwice(0, "Guard", "the getter and the setter of 'Extensions.extension(Guard).Uses'"),
        Verify.ValuePassedTwice(1, "Guard", "the getter and the setter of 'Extensions.extension(Guard).Uses'"),
        Verify.ValuePassedTwice(2, "Guard", "the getter and the setter of 'Extensions.extension(Guard).Name'"),
        Verify.ValuePassedTwice(3, "Guard?", "the getter and the setter of 'Extensions.extension(Guard?).Count'"),
        Verify.ValuePassedTwice(4, "T", "the getter and the setter of 'Extensions.extension<T>(T).Size'"),
        Verify.ValuePassedTwice(5, "Guard", "the getter and the setter of 'Extensions.extension(Guard).Uses'"),
        Verify.ValuePassedTwice(6, "Guard", "the getter and the setter of 'Extensions.extension(Guard).Uses'"),
        Verify.CopyOfStoredValue(7, "'Extensions.extension(Guard).Uses' receives", "Guard"));

#if NET11_0_OR_GREATER
    // The same for an indexer of an extension block (C# 15), and for an index and a range that the compiler turns into a call of the
    // length, the indexer and Slice of an extension block: each of them gets the receiver. A stored receiver is a copy for them too.
    // The flow takes the receiver of an index or a range for one that a reference can outlive, so a local is never moved from
    // there: its value goes to the first member as a copy.
    [TestMethod]
    public Task ExtensionBlock_AReceiverThatAnIndexOrARangeGivesToTwoMembersIsPassedTwice() => Verify.AnalyzerAsync(
        $$"""
        static class Extensions
        {
            extension(Guard g)
            {
                public int this[int index] { get => 0; set { } }

                public int Length => 2;

                public int Slice(int start, int length) => 0;
            }
        }

        static class C
        {
            static Guard s_held;

            static void IndexerCompound(RwLock rwLock)
            {
                Guard g = rwLock.Enter();
                {|#0:g|}[0] += 1;
            }

            static void FromTheEnd(RwLock rwLock)
            {
                Guard g = rwLock.Enter();
                int last = {|#1:{|#5:g|}|}[^1];
            }

            static void ARange(RwLock rwLock)
            {
                Guard g = rwLock.Enter();
                int tail = {|#2:{|#6:g|}|}[1..];
            }

            static void AStoredRange() => _ = {|#4:s_held|}[1..2];

            // A plain index runs one member.
            static void OneMember(RwLock rwLock)
            {
                Guard h = rwLock.Enter();
                int first = h[0];
            }
        }

        {{Sources.Guard}}
        """,
        Verify.ValuePassedTwice(0, "Guard", "the getter and the setter of 'Extensions.extension(Guard).this[int]'"),
        Verify.ValuePassedTwice(1, "Guard", "'Extensions.extension(Guard).Length' and 'Extensions.extension(Guard).this[int]'"),
        Verify.ValuePassedTwice(2, "Guard", "'Extensions.extension(Guard).Length' and 'Extensions.extension(Guard).Slice(int, int)'"),
        Verify.CopyOfStoredValue(4, "'Extensions.extension(Guard).Slice(int, int)' receives", "Guard"),
        Verify.CopyOfStoredValue(5, "'Extensions.extension(Guard).Length.get' receives", "Guard"),
        Verify.CopyOfStoredValue(6, "'Extensions.extension(Guard).Length.get' receives", "Guard"));
#endif
}
