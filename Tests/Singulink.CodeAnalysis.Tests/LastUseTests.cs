using PrefixClassName.MsTest;

namespace Singulink.CodeAnalysis.Tests;

/// <summary>
/// Tests of the move on the last use: a variable that is never used again is a new value where it is used for the last time.
/// </summary>
/// <remarks>
/// Docs: articles/rules/last-use.md.
/// </remarks>
[PrefixTestClass]
public class LastUseTests
{
    [TestMethod]
    public Task LastUse_AVariableThatIsNotUsedAgainIsANewValue() => Verify.AnalyzerAsync(
        $$"""
        using System.Collections.Generic;

        class C
        {
            Guard _field;
            (Guard, int) _tuple;

            static void Use(Guard guard) { }
            static void Touch(ref Guard guard) { }
            static bool TryGet(out Guard guard) { guard = default; return true; }

            Guard Property { set => _field = value; }

            void Assigned(RwLock rwLock)
            {
                Guard guard = rwLock.Enter();
                _field = guard;
            }

            void UsedAgain(RwLock rwLock)
            {
                Guard guard = rwLock.Enter();
                _field = {|#0:guard|};
                {|#2:guard|}.Dispose();
            }

            // Every branch has a last use of its own.
            void OnEveryPath(RwLock rwLock, bool condition)
            {
                Guard guard = rwLock.Enter();

                if (condition)
                {
                    _field = guard;
                    return;
                }

                Use(guard);
            }

            Guard EitherOne(bool condition, Guard a, Guard b) => condition ? a : b;

            void Parameter(Guard guard, Guard? maybe, (Guard, int) tuple, (Guard guard, int number) named)
            {
                _field = guard;
                Guard? other = maybe;
                _tuple = tuple;

                // A part of the variable, with the rest of it dropped.
                _field = named.guard;
            }

            // Whatever a new value is allowed for is allowed.
            void OtherUses(RwLock rwLock)
            {
                Guard boxed = rwLock.Enter();
                object box = boxed;

                Guard disposed = rwLock.Enter();
                using (disposed) { }

                Guard matched = rwLock.Enter();
                if (matched is { Value: 0 } taken) taken.Dispose();

                Guard inTuple = rwLock.Enter();
                var pair = (inTuple, 1);
            }

            // What uses the variable in place before does not count against it.
            void UsedInPlaceBefore(RwLock rwLock)
            {
                Guard guard = rwLock.Enter();
                guard.Dispose();
                Touch(ref guard);
                int value = guard.Value;
                _field = guard;
            }

            // Assigned as a whole, the variable starts again.
            void AssignedInBetween(RwLock rwLock)
            {
                Guard guard = rwLock.Enter();
                _field = guard;
                guard = rwLock.Enter();
                _field = guard;
            }

            // A variable that an 'out' argument, a deconstruction, a pattern or a 'foreach' declares is one like any other.
            void Declared(RwLock rwLock, object boxed)
            {
                if (TryGet(out var fromOut))
                    _field = fromOut;

                var (deconstructed, number) = (rwLock.Enter(), 1);
                _field = deconstructed;

                foreach (Guard item in rwLock.CreateGuards())
                    _field = item;
            }

            // An iterator goes on, but it never uses the variable again.
            IEnumerable<Guard> Yielded(RwLock rwLock)
            {
                Guard guard = rwLock.Enter();
                yield return guard;
            }

            // A lambda and a local function have variables of their own.
            void InAFunction(RwLock rwLock)
            {
                System.Action<Guard> store = guard => _field = guard;

                System.Action<Guard> storeAndRelease = guard =>
                {
                    _field = {|#1:guard|};
                    {|#3:guard|}.Dispose();
                };

                System.Action create = () =>
                {
                    Guard guard = rwLock.Enter();
                    _field = guard;
                };

                void Store(Guard guard) => _field = guard;
            }
        }

        {{Sources.Guard}}
        """,
        Verify.UsedAfterHandOff(2, "guard", "Guard", "its value was assigned", 0),
        Verify.UsedAfterHandOff(3, "guard", "Guard", "its value was assigned", 1));

    [TestMethod]
    public Task LastUse_IsJudgedInTheOrderOfEvaluationAlongEveryPath() => Verify.AnalyzerAsync(
        $$"""
        class C
        {
            Guard _field;

            static void Use(Guard guard) { }
            static void Use(Guard a, Guard b) { }
            static Guard Echo(ref Guard guard) => default;
            static bool TryGet(out Guard guard) { guard = default; return true; }

            // A read in a loop of a variable that is declared outside the loop is followed by itself.
            void InALoop(RwLock rwLock)
            {
                Guard guard = rwLock.Enter();

                for (int i = 0; i < 2; i++)
                    _field = {|#0:guard|};
            }

            void DeclaredInTheLoop(RwLock rwLock)
            {
                for (int i = 0; i < 2; i++)
                {
                    Guard guard = rwLock.Enter();
                    _field = guard;
                }
            }

            void AssignedInTheLoop(Guard guard, bool condition)
            {
                while (condition)
                {
                    if (TryGet(out guard))
                        _field = guard;
                }
            }

            // A backward 'goto' is a loop.
            void BackwardGoto(RwLock rwLock)
            {
                Guard guard = rwLock.Enter();

                again:
                _field = {|#1:guard|};

                if ({|#5:guard|}.Value == 0)
                    goto again;
            }

            // The first argument is read before 'Echo' uses the variable, whatever the order of the parameters.
            void NamedArguments(Guard guard) => Use(b: {|#2:guard|}, a: Echo(ref {|#6:guard|}));

            void ReadTwice(Guard guard) => Use({|#3:guard|}, {|#7:guard|});

            void InEachSection(Guard guard, int number)
            {
                switch (number)
                {
                    case 0:
                        _field = guard;
                        break;

                    case 1:
                        Use(guard);
                        break;
                }
            }

            void InAnArm(RwLock rwLock, Guard guard, bool condition) => _field = condition ? guard : rwLock.Enter();

            // What is assigned through one of two references assigns neither variable for sure.
            void AssignedThroughEitherReference(RwLock rwLock, bool condition)
            {
                Guard a = rwLock.Enter();
                Guard b = rwLock.Enter();
                _field = {|#4:a|};
                (condition ? ref a : ref b) = rwLock.Enter();
                a.Dispose();
                b.Dispose();
            }
        }

        {{Sources.Guard}}
        """,
        Verify.CopyByAssignment(0, "Guard"),
        Verify.UsedAfterHandOff(5, "guard", "Guard", "its value was assigned", 1),
        Verify.UsedAfterHandOff(6, "guard", "Guard", "'C.Use(Guard, Guard)' received its value", 2),
        Verify.UsedAfterHandOff(7, "guard", "Guard", "'C.Use(Guard, Guard)' received its value", 3),
        Verify.CopyByAssignment(4, "Guard"));

    [TestMethod]
    public Task LastUse_AnExceptionIsAPath() => Verify.AnalyzerAsync(
        $$"""
        using System;

        class Holder
        {
            public void Take(Guard guard) { }
        }

        class C
        {
            Guard _field;
            Holder _holder = new();

            // A use in a 'catch', a 'finally' or an exception filter follows every operation of its 'try' block.
            void UsedInCatch(RwLock rwLock)
            {
                Guard guard = rwLock.Enter();

                try
                {
                    _holder.Take({|#0:guard|});
                }
                catch
                {
                    {|#4:guard|}.Dispose();
                }
            }

            void UsedInFinally(RwLock rwLock)
            {
                Guard guard = rwLock.Enter();

                try
                {
                    _holder.Take({|#1:guard|});
                }
                finally
                {
                    {|#5:guard|}.Dispose();
                }
            }

            void UsedInAFilter(RwLock rwLock)
            {
                Guard guard = rwLock.Enter();

                try
                {
                    _holder.Take({|#2:guard|});
                }
                catch (Exception) when ({|#6:guard|}.Value == 0)
                {
                }
            }

            void UsedInAnOuterCatch(RwLock rwLock)
            {
                Guard guard = rwLock.Enter();

                try
                {
                    try
                    {
                        _holder.Take({|#3:guard|});
                    }
                    finally
                    {
                        Console.WriteLine();
                    }
                }
                catch
                {
                    {|#7:guard|}.Dispose();
                }
            }

            void NotUsedInCatch(RwLock rwLock)
            {
                Guard guard = rwLock.Enter();

                try
                {
                    _holder.Take(guard);
                }
                catch
                {
                    Console.WriteLine();
                }
            }

            void LastUsedInFinally(RwLock rwLock)
            {
                Guard guard = rwLock.Enter();

                try
                {
                    Console.WriteLine();
                }
                finally
                {
                    _field = guard;
                }
            }
        }

        {{Sources.Guard}}
        """,
        Verify.UsedAfterHandOffInHandler(4, "guard", "Guard", "'Holder.Take(Guard)' received its value", 0),
        Verify.UsedAfterHandOffInHandler(5, "guard", "Guard", "'Holder.Take(Guard)' received its value", 1),
        Verify.UsedAfterHandOffInHandler(6, "guard", "Guard", "'Holder.Take(Guard)' received its value", 2),
        Verify.UsedAfterHandOffInHandler(7, "guard", "Guard", "'Holder.Take(Guard)' received its value", 3));

    [TestMethod]
    public Task LastUse_OnlyALocalOrAByValueParameterIsMovedFrom() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using System.Collections.Generic;

        class Holder
        {
            public Guard Field;
        }

        // A parameter of a primary constructor is a field.
        class Primary(Guard guard)
        {
            public Guard Field = {|#0:guard|};

            public void Release() => guard.Dispose();
        }

        class Base(Guard guard)
        {
            public void Release() => guard.Dispose();
        }

        class Derived(Guard guard) : Base(guard);

        class C
        {
            Guard _field;
            Holder _holder = new();

            void RefParameter(ref Guard guard) => _field = {|#1:guard|};

            void InParameter(in Guard guard) => _field = {|#2:guard|};

            void Field() => _field = {|#3:_holder.Field|};

            void Element(Guard[] guards) => _field = {|#4:guards[0]|};

            // A variable that a lambda or a local function captures is a field.
            void CapturedByALambda(RwLock rwLock)
            {
                Guard guard = rwLock.Enter();
                Action release = () => guard.Dispose();
                _field = {|#5:guard|};
            }

            void CapturedByALocalFunction(RwLock rwLock)
            {
                Guard guard = rwLock.Enter();
                void Release() => guard.Dispose();
                _field = {|#6:guard|};
            }

            // Also a parameter of a lambda that a lambda inside it captures.
            Func<Guard, Action> CapturedByAnInnerLambda() => guard => () => _field = {|#10:guard|};

            // The end of its scope uses a 'using' variable.
            void UsingVariable(RwLock rwLock)
            {
                using Guard guard = rwLock.Enter();
                _field = {|#7:guard|};
            }

            // A parameter of an iterator is copied for every enumeration.
            IEnumerable<int> Iterator(Guard {|#8:guard|})
            {
                _field = {|#9:guard|};
                yield break;
            }
        }

        {{Sources.Guard}}
        """,
        Verify.CopyOfStoredValue(0, "The initializer takes", "Guard"),
        Verify.CopyByAssignment(1, "Guard"),
        Verify.CopyByAssignment(2, "Guard"),
        Verify.CopyByAssignment(3, "Guard"),
        Verify.CopyByAssignment(4, "Guard"),
        Verify.CopyByAssignment(5, "Guard"),
        Verify.CopyByAssignment(6, "Guard"),
        Verify.CopyByAssignment(7, "Guard"),
        Verify.IteratorParameter(8, "Guard", "guard", "C.Iterator(Guard)"),
        Verify.CopyByAssignment(9, "Guard"),
        Verify.CopyByAssignment(10, "Guard"));

    [TestMethod]
    public Task LastUse_AVariableThatAReferenceCanOutliveIsNeverMovedFrom() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using System.Diagnostics.CodeAnalysis;
        using System.Runtime.CompilerServices;
        using Singulink.CodeAnalysis;

        ref struct RefHolder
        {
            public ref Guard Reference;
        }

        [NonCopyable]
        struct Self : IDisposable
        {
            int _state;

            public void Dispose() { }

            [UnscopedRef]
            public ref Self This => ref this;

            [UnscopedRef]
            public Span<int> State => new Span<int>(ref _state);

            public int Plain => _state;
        }

        [NonCopyable]
        [InlineArray(2)]
        struct Buffer
        {
            private Guard _element;
        }

        unsafe class C
        {
            Guard _field;
            (Guard, int) _tuple;
            Self _self;
            Buffer _buffer;

            static ref readonly Guard Echo(in Guard guard) => ref guard;
            static void Make(ref Guard guard, out Span<Guard> span) => span = new Span<Guard>(ref guard);
            static void* Point(ref Guard guard) => null;
            static void Touch(ref Guard guard) { }

            void RefLocal(RwLock rwLock)
            {
                Guard guard = rwLock.Enter();
                ref Guard reference = ref guard;
                _field = {|#0:guard|};
                reference.Dispose();
            }

            // A reference to a part of the variable.
            void RefLocalToAPart(RwLock rwLock)
            {
                (Guard, int) tuple = (rwLock.Enter(), 1);
                ref Guard part = ref tuple.Item1;
                _tuple = {|#1:tuple|};
                part.Dispose();
            }

            void RefField(RwLock rwLock)
            {
                Guard guard = rwLock.Enter();
                scoped RefHolder holder = default;
                holder.Reference = ref guard;
                _field = {|#2:guard|};
                holder.Reference.Dispose();
            }

            void RefFieldInAnInitializer(RwLock rwLock)
            {
                Guard guard = rwLock.Enter();
                var holder = new RefHolder { Reference = ref guard };
                _field = {|#15:guard|};
                holder.Reference.Dispose();
            }

            void EitherReference(RwLock rwLock, bool condition)
            {
                Guard a = rwLock.Enter();
                Guard b = rwLock.Enter();
                ref Guard reference = ref (condition ? ref a : ref b);
                _field = {|#16:a|};
                reference.Dispose();
            }

            void Address(RwLock rwLock)
            {
                Guard guard = rwLock.Enter();
                Guard* pointer = &{|#3:guard|};
                _field = {|#4:guard|};
                pointer->Dispose();
            }

            void TypedReference(RwLock rwLock)
            {
                Guard guard = rwLock.Enter();
                TypedReference reference = __makeref({|#5:guard|});
                _field = {|#6:guard|};
            }

            // Passed by reference to a call that hands out a ref struct through an 'out' parameter, or returns one.
            void OutRefStruct(RwLock rwLock)
            {
                Guard guard = rwLock.Enter();
                Make(ref guard, out var span);
                _field = {|#7:guard|};
                span[0].Dispose();
            }

            void ConstructorOfARefStruct(RwLock rwLock)
            {
                Guard guard = rwLock.Enter();
                var span = new Span<Guard>(ref guard);
                _field = {|#8:guard|};
                span[0].Dispose();
            }

            // An argument for an 'in' parameter is passed by reference even with no modifier at the call.
            void InWithNoModifier(RwLock rwLock)
            {
                Guard guard = rwLock.Enter();
                ref readonly Guard reference = ref Echo(guard);
                _field = {|#9:guard|};
            }

            // The receiver of a call that returns a reference or a ref struct.
            void ReceiverOfAReference()
            {
                Self self = default;
                ref Self alias = ref self.This;
                _self = {|#10:self|};
                alias.Dispose();
            }

            void ReceiverOfARefStruct()
            {
                Self self = default;
                Span<int> state = self.State;
                _self = {|#11:self|};
            }

            void ReceiverOfAValue()
            {
                Self self = default;
                int plain = self.Plain;
                _self = self;
            }

            // A pointer counts like a reference.
            void Pointer(RwLock rwLock)
            {
                Guard guard = rwLock.Enter();
                void* pointer = Point(ref guard);
                _field = {|#12:guard|};
            }

            // An inline array that is converted to a span, or sliced, has a reference taken to it with nothing written at all.
            void InlineArrayAsSpan()
            {
                Buffer buffer = default;
                Span<Guard> span = buffer;
                _buffer = {|#13:buffer|};
                span[0].Dispose();
            }

            void InlineArraySliced()
            {
                Buffer buffer = default;
                Span<Guard> span = buffer[1..];
                _buffer = {|#14:buffer|};
            }

            void InlineArrayElement()
            {
                Buffer buffer = default;
                buffer[0].Dispose();
                _buffer = buffer;
            }

            // A call that returns nothing keeps no reference.
            void PassedByReference(RwLock rwLock)
            {
                Guard guard = rwLock.Enter();
                Touch(ref guard);
                _field = guard;
            }
        }

        {{Sources.Guard}}
        """,
        Verify.CopyByAssignment(0, "Guard"),
        Verify.CopyByAssignment(1, "(Guard, int)"),
        Verify.CopyByAssignment(2, "Guard"),
        Verify.UnsupportedUse(3, "Guard", "LocalReference"),
        Verify.CopyByAssignment(4, "Guard"),
        Verify.UnsupportedUse(5, "Guard", "LocalReference"),
        Verify.CopyByAssignment(6, "Guard"),
        Verify.CopyByAssignment(7, "Guard"),
        Verify.CopyByAssignment(8, "Guard"),
        Verify.CopyByAssignment(9, "Guard"),
        Verify.CopyByAssignment(10, "Self"),
        Verify.CopyByAssignment(11, "Self"),
        Verify.CopyByAssignment(12, "Guard"),
        Verify.CopyByAssignment(13, "Buffer"),
        Verify.CopyByAssignment(14, "Buffer"),
        Verify.CopyByAssignment(15, "Guard"),
        Verify.CopyByAssignment(16, "Guard"));

    [TestMethod]
    public Task LastUse_AContainerLocalIsMovedFromIfItIsTheMethodsOwn() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using System.Collections.Generic;
        using System.Runtime.InteropServices;
        using System.Threading.Tasks;

        class C
        {
            Guard _field;
            List<Guard> _kept = new();
            Task<Guard> _task = null!;

            static Task<Guard> WaitAsync(ValueTask<Guard> pending) => pending.AsTask();
            static void Look(List<Guard> list) { }

            // The method keeps the list, and says so.
            void Keep([Singulink.CodeAnalysis.Kept] List<Guard> list)
            {
                ref var slot = ref _kept;
                slot = list;
            }

            async Task AwaitedLater(RwLock rwLock)
            {
                Task<Guard> pending = rwLock.EnterTaskAsync();
                Guard guard = await pending;
                guard.Dispose();
            }

            async Task AwaitedTwice(RwLock rwLock)
            {
                Task<Guard> pending = rwLock.EnterTaskAsync();
                Guard first = {|#0:await pending|};
                Guard second = await pending;
            }

            async Task OnEveryPath(RwLock rwLock)
            {
                Task<Guard> pending = rwLock.EnterTaskAsync();

                if (pending.IsCompleted)
                {
                    Guard now = pending.Result;
                }
                else
                {
                    Guard later = await pending;
                }
            }

            // A view of the task that is awaited or read where it is made is gone with the statement.
            async Task ThroughAView(RwLock rwLock)
            {
                Task<Guard> configured = rwLock.EnterTaskAsync();
                Guard first = await configured.ConfigureAwait(false);

                Task<Guard> blocking = rwLock.EnterTaskAsync();
                Guard second = blocking.GetAwaiter().GetResult();

                // One that is stored in a variable still reaches the task.
                Task<Guard> viewed = rwLock.EnterTaskAsync();
                var awaitable = viewed.ConfigureAwait(false);
                Guard third = {|#15:await viewed|};
            }

            void ReadOut(RwLock rwLock)
            {
                var list = new List<Guard> { rwLock.Enter() };
                Guard first = list[0];

                var filled = rwLock.CreateGuards();
                filled.Add(rwLock.Enter());

                foreach (Guard guard in filled)
                    _field = guard;
            }

            // The strict test: a span over the list still reaches what the read took.
            void WithASpan(RwLock rwLock)
            {
                var list = new List<Guard> { rwLock.Enter() };
                Span<Guard> span = CollectionsMarshal.AsSpan(list);
                Guard copy = {|#1:list[0]|};
                span[0].Dispose();
            }

            // Passed to anything, the list may have been kept.
            void PassedOn(RwLock rwLock)
            {
                var kept = new List<Guard> { rwLock.Enter() };
                Keep(kept);
                Guard first = {|#2:kept[0]|};

                var seen = new List<Guard> { rwLock.Enter() };
                Look(seen);
                Guard second = {|#3:seen[0]|};
            }

            void NotItsOwn(RwLock rwLock, List<Guard> parameter)
            {
                var fromField = _kept;
                Guard first = {|#4:fromField[0]|};
                Guard second = {|#5:parameter[0]|};

                var stored = new List<Guard> { rwLock.Enter() };
                _kept = stored;
                Guard third = {|#6:stored[0]|};

                var captured = new List<Guard> { rwLock.Enter() };
                Func<int> count = () => captured.Count;
                Guard fourth = {|#7:captured[0]|};
            }

            async Task StoredTask(RwLock rwLock)
            {
                Task<Guard> pending = rwLock.EnterTaskAsync();
                _task = pending;
                Guard guard = {|#8:await pending|};
            }

            // A container that is read out of a non-copyable value stays stored at the last use of that value.
            void OutOfATuple(RwLock rwLock)
            {
                (Guard guard, List<Guard> list) tuple = (rwLock.Enter(), rwLock.CreateGuards());
                Span<Guard> span = CollectionsMarshal.AsSpan(tuple.list);
                var (guard, list) = tuple;
                Guard copy = {|#9:list[0]|};
                span[0].Dispose();
            }

            void OutOfAField(RwLock rwLock)
            {
                (Guard guard, List<Guard> list) tuple = (rwLock.Enter(), rwLock.CreateGuards());
                Guard copy = {|#10:tuple.list[0]|};
            }

            void OutOfAPattern(RwLock rwLock)
            {
                (Guard guard, List<Guard> list) tuple = (rwLock.Enter(), rwLock.CreateGuards());

                if (tuple is (_, var list))
                {
                    Guard copy = {|#11:list[0]|};
                }
            }

            // A local that is put into another container is judged while the uses of both are still being gathered.
            void PutIntoAnother(RwLock rwLock)
            {
                var inner = rwLock.CreateGuards();
                var outer = new List<List<Guard>>();
                outer.Add(inner);

                // The copy is reported where it is made. The read that follows is the last use of its list.
                var first = rwLock.CreateGuards();
                var second = rwLock.CreateGuards();
                first.Add({|#16:second[0]|});
                second.Add(first[0]);
            }

            // A view is never moved from.
            void Views()
            {
                Span<Guard> span = new Guard[1];
                Guard first = {|#12:span[0]|};

                var segment = new ArraySegment<Guard>(new Guard[1]);
                Guard second = {|#13:segment[0]|};
            }

            // A value task is a value of a non-copyable type, which needs no test of what else holds it.
            Task<Guard> ValueTask(RwLock rwLock)
            {
                var pending = rwLock.EnterAsync();

                if (!pending.IsCompletedSuccessfully)
                    return WaitAsync(pending);

                using var held = pending.Result;
                return null!;
            }

            void ValueTaskReadTwice(RwLock rwLock)
            {
                var pending = rwLock.EnterAsync();
                var first = {|#14:{|#17:pending|}.Result|};
                var second = {|#18:pending|}.Result;
            }
        }

        {{Sources.Guard}}
        """,
        Verify.ReadFromStoredContainer(0, "'await'", "Guard"),
        Verify.ReadFromStoredContainer(1, "'List<T>.this[int]'", "Guard", because: "'list' is not the method's own anywhere in the method, because on line 81 it is passed to 'CollectionsMarshal.AsSpan<Guard>(List<Guard>?)', which hands out a view of it"),
        Verify.ReadFromStoredContainer(2, "'List<T>.this[int]'", "Guard", because: "'kept' is not the method's own anywhere in the method, because on line 90 it is passed to 'C.Keep(List<Guard>)', which keeps it"),
        Verify.ReadFromStoredContainer(3, "'List<T>.this[int]'", "Guard"),
        Verify.ReadFromStoredContainer(4, "'List<T>.this[int]'", "Guard"),
        Verify.ReadFromStoredContainer(5, "'List<T>.this[int]'", "Guard"),
        Verify.ReadFromStoredContainer(6, "'List<T>.this[int]'", "Guard", because: "'stored' is not the method's own anywhere in the method, because on line 105 it is assigned to something else"),
        Verify.ReadFromStoredContainer(7, "'List<T>.this[int]'", "Guard", because: "'captured' is not the method's own anywhere in the method, because on line 109 a lambda or a local function uses it"),
        Verify.ReadFromStoredContainer(8, "'await'", "Guard", because: "'pending' is not the method's own anywhere in the method, because on line 116 it is assigned to something else"),
        Verify.ReadFromStoredContainer(9, "'List<T>.this[int]'", "Guard", because: "'list' is not the method's own anywhere in the method, because on line 125 it is given what something else hands out"),
        Verify.ReadFromStoredContainer(10, "'List<T>.this[int]'", "Guard"),
        Verify.ReadFromStoredContainer(11, "'List<T>.this[int]'", "Guard"),
        Verify.CopyByAssignment(12, "Guard"),
        Verify.ReadFromStoredContainer(13, "'ArraySegment<T>.this[int]'", "Guard"),
        Verify.UsedAfterHandOff(18, "pending", "System.Threading.Tasks.ValueTask<Guard>", "'ValueTask<Guard>.Result' read its value", 17),
        Verify.ReadFromStoredContainer(15, "'await'", "Guard", because: "'viewed' is not the method's own anywhere in the method, because on line 61 'Task<Guard>.ConfigureAwait(bool)' can keep it, hand out a view of it or take in what something else holds"),
        Verify.ReadFromStoredContainer(16, "'List<T>.this[int]'", "Guard"));

    [TestMethod]
    public Task LastUse_AFilterThatGivesFalseIsFollowedByTheNextClauses() => Verify.AnalyzerAsync(
        $$"""
        using System;

        class C
        {
            static bool Log(Guard guard) => false;
            static void Work() { }

            void NextCatch(RwLock rwLock)
            {
                Guard guard = rwLock.Enter();

                try { Work(); }
                catch when (Log({|#0:guard|})) { }
                catch { guard.Dispose(); }
            }

            void NextTypedCatch(RwLock rwLock)
            {
                Guard guard = rwLock.Enter();

                try { Work(); }
                catch (InvalidOperationException) when (Log({|#1:guard|})) { }
                catch (InvalidOperationException) { guard.Dispose(); }
            }

            void NextFilter(RwLock rwLock)
            {
                Guard guard = rwLock.Enter();

                try { Work(); }
                catch when (Log({|#2:guard|})) { }
                catch when (guard.Value == 0) { }
            }

            // After the last clause the exception goes to the handlers around the 'try' statement.
            void HandlerAroundTheStatement(RwLock rwLock)
            {
                Guard guard = rwLock.Enter();

                try
                {
                    try { Work(); }
                    catch when (Log({|#3:guard|})) { }
                }
                catch
                {
                    guard.Dispose();
                }
            }

            // A read in a filter is never a last use, whatever follows it: a 'finally' inside the 'try' runs after the filter, and the
            // filter can run again for another exception.
            void EarlierCatch(RwLock rwLock)
            {
                Guard guard = rwLock.Enter();

                try { Work(); }
                catch (InvalidOperationException) { guard.Dispose(); }
                catch when (Log({|#4:guard|})) { }
            }

            void NoOtherUse(RwLock rwLock)
            {
                Guard guard = rwLock.Enter();

                try { Work(); }
                catch when (Log({|#5:guard|})) { }
                catch { }
            }

            void WithAFinallyInside(RwLock rwLock)
            {
                Guard guard = rwLock.Enter();

                try
                {
                    try { Work(); }
                    finally { guard.Dispose(); }
                }
                catch (Exception) when (Log({|#6:guard|})) { }
            }
        }

        {{Sources.Guard}}
        """,
        Verify.CopyOfStoredValueInFilter(0, "'C.Log(Guard)' receives", "Guard"),
        Verify.CopyOfStoredValueInFilter(1, "'C.Log(Guard)' receives", "Guard"),
        Verify.CopyOfStoredValueInFilter(2, "'C.Log(Guard)' receives", "Guard"),
        Verify.CopyOfStoredValueInFilter(3, "'C.Log(Guard)' receives", "Guard"),
        Verify.CopyOfStoredValueInFilter(4, "'C.Log(Guard)' receives", "Guard"),
        Verify.CopyOfStoredValueInFilter(5, "'C.Log(Guard)' receives", "Guard"),
        Verify.CopyOfStoredValueInFilter(6, "'C.Log(Guard)' receives", "Guard"));

    [TestMethod]
    public Task LastUse_AValueCommittedAtTheEndOfATryIsNotFollowedByItsHandlers() => Verify.AnalyzerAsync(
        $$"""
        class Holder
        {
            public Guard Field;

            public Guard Kept { set => Field = value; }

            public void Take(Guard guard) { }
        }

        class C
        {
            Guard _held;
            Guard[] _slots = new Guard[2];
            Holder _holder = new();

            static void Work() { }

            // Acquire, set up, commit, or dispose on failure: nothing after the store can throw.
            void Committed(RwLock rwLock)
            {
                Guard guard = rwLock.Enter();

                try
                {
                    Work();
                    _held = guard;
                }
                catch
                {
                    guard.Dispose();
                    throw;
                }
            }

            Guard CommittedToALocal(RwLock rwLock)
            {
                Guard guard = rwLock.Enter();
                Guard held;

                try
                {
                    Work();
                    held = guard;
                }
                catch
                {
                    guard.Dispose();
                    throw;
                }

                return held;
            }

            // If the store itself fails, the value is still only in the variable.
            void CommittedToAnElementOrAFieldOfAnObject(RwLock rwLock, int index)
            {
                Guard first = rwLock.Enter();

                try
                {
                    Work();
                    _slots[index] = first;
                }
                catch
                {
                    first.Dispose();
                    throw;
                }

                Guard second = rwLock.Enter();

                try
                {
                    Work();
                    _holder.Field = second;
                }
                catch
                {
                    second.Dispose();
                    throw;
                }
            }

            void CallAfterTheStore(RwLock rwLock)
            {
                Guard guard = rwLock.Enter();

                try
                {
                    _held = {|#0:guard|};
                    Work();
                }
                catch
                {
                    {|#6:guard|}.Dispose();
                    throw;
                }
            }

            void SomethingThatCanThrowAfterTheStore(RwLock rwLock, int index)
            {
                Guard guard = rwLock.Enter();

                try
                {
                    Work();
                    _held = {|#1:guard|};
                    _slots[index] = default;
                }
                catch
                {
                    {|#7:guard|}.Dispose();
                    throw;
                }
            }

            // A setter and a method are calls: they can throw after they have stored the value.
            void ThroughASetter(RwLock rwLock)
            {
                Guard guard = rwLock.Enter();

                try
                {
                    Work();
                    _holder.Kept = {|#2:guard|};
                }
                catch
                {
                    {|#8:guard|}.Dispose();
                    throw;
                }
            }

            void ThroughAMethod(RwLock rwLock)
            {
                Guard guard = rwLock.Enter();

                try
                {
                    Work();
                    _holder.Take({|#3:guard|});
                }
                catch
                {
                    {|#9:guard|}.Dispose();
                    throw;
                }
            }

            // A 'finally' always runs.
            void UsedInFinally(RwLock rwLock)
            {
                Guard guard = rwLock.Enter();

                try
                {
                    Work();
                    _held = {|#4:guard|};
                }
                finally
                {
                    {|#10:guard|}.Dispose();
                }
            }

            // What follows the inner 'try' can still throw into the outer 'catch'.
            void InsideAnotherTry(RwLock rwLock)
            {
                Guard guard = rwLock.Enter();

                try
                {
                    try
                    {
                        Work();
                        _held = {|#5:guard|};
                    }
                    catch
                    {
                        throw;
                    }

                    Work();
                }
                catch
                {
                    {|#11:guard|}.Dispose();
                }
            }
        }

        {{Sources.Guard}}
        """,
        Verify.UsedAfterHandOffInHandler(6, "guard", "Guard", "its value was assigned", 0),
        Verify.UsedAfterHandOffInHandler(7, "guard", "Guard", "its value was assigned", 1),
        Verify.UsedAfterHandOffInHandler(8, "guard", "Guard", "its value was assigned", 2),
        Verify.UsedAfterHandOffInHandler(9, "guard", "Guard", "'Holder.Take(Guard)' received its value", 3),
        Verify.UsedAfterHandOffInHandler(10, "guard", "Guard", "its value was assigned", 4),
        Verify.UsedAfterHandOffInHandler(11, "guard", "Guard", "its value was assigned", 5));

    [TestMethod]
    public Task LastUse_ADeconstructMethodIsAUseWhereTheValueIsDeconstructed() => Verify.AnalyzerAsync(
        $$"""
        using Singulink.CodeAnalysis;

        [NonCopyable]
        struct Pair
        {
            public Guard First;
            public Guard Second;

            public readonly void Deconstruct(out int first, out int second)
            {
                first = First.Value;
                second = Second.Value;
            }
        }

        class C
        {
            Pair _pair;

            void DeconstructedBefore(Pair pair)
            {
                var (first, second) = pair;
                _pair = pair;
            }

            void DeconstructedAfter(Pair pair)
            {
                _pair = {|#0:pair|};
                var (first, second) = {|#1:pair|};
            }

            void NeverDeconstructed(Pair pair) => _pair = pair;
        }

        {{Sources.Guard}}
        """,
        Verify.UsedAfterHandOff(1, "pair", "Pair", "its value was assigned", 0));

    [TestMethod]
    public Task LastUse_ABlockThatDoesNotEndInTheNormalWayCommitsNothing() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using System.Collections.Generic;
        using System.Threading.Tasks;

        class C
        {
            Guard _held;
            object _gate = new();

            static void Work() { }
            static void Fail() => throw new InvalidOperationException();
            static IDisposable Open() => null!;

            // A 'throw' sends the exception to the very handlers that a commit would skip.
            void Rethrown(RwLock rwLock)
            {
                Guard guard = rwLock.Enter();

                try
                {
                    Fail();
                }
                catch
                {
                    _held = {|#0:guard|};
                    throw;
                }
                finally
                {
                    {|#17:guard|}.Dispose();
                }
            }

            void Thrown(RwLock rwLock, Exception parameter)
            {
                Guard guard = rwLock.Enter();
                var local = new InvalidOperationException();

                try { Work(); _held = {|#1:guard|}; throw local; }
                catch { {|#18:guard|}.Dispose(); }

                guard = rwLock.Enter();

                try { Work(); _held = {|#2:guard|}; throw parameter; }
                catch { {|#19:guard|}.Dispose(); }

                guard = rwLock.Enter();

                try { Work(); _held = {|#3:guard|}; throw null!; }
                catch { {|#20:guard|}.Dispose(); }
            }

            void RethrownIntoAnOuterCatch(RwLock rwLock)
            {
                Guard guard = rwLock.Enter();

                try
                {
                    try { Fail(); }
                    catch { _held = {|#4:guard|}; throw; }
                }
                catch
                {
                    {|#21:guard|}.Dispose();
                }
            }

            void RethrownIntoAnOuterFinally(RwLock rwLock)
            {
                Guard guard = rwLock.Enter();

                try
                {
                    try { Fail(); }
                    catch { _held = {|#5:guard|}; throw; }
                }
                finally
                {
                    {|#22:guard|}.Dispose();
                }
            }

            // The end of a 'finally' passes an exception on to the handlers around it.
            void StoredInAFinally(RwLock rwLock)
            {
                Guard guard = rwLock.Enter();

                try
                {
                    try { Fail(); }
                    finally { _held = {|#6:guard|}; }
                }
                catch
                {
                    {|#23:guard|}.Dispose();
                }
            }

            // A 'return' leaves in the normal way, through every 'finally' on its way.
            void Returned(RwLock rwLock)
            {
                Guard guard = rwLock.Enter();

                try { Work(); _held = guard; return; }
                catch { guard.Dispose(); throw; }
            }

            void ReturnedThroughAFinally(RwLock rwLock)
            {
                Guard guard = rwLock.Enter();

                try { Work(); _held = {|#7:guard|}; return; }
                finally { {|#24:guard|}.Dispose(); }
            }

            // 'continue', 'break' and 'goto' leave the 'try' or stay in it.
            void ContinuedOutOfTheTry(RwLock rwLock, int count)
            {
                for (int i = 0; i < count; i++)
                {
                    Guard guard = rwLock.Enter();

                    try { Work(); _held = guard; continue; }
                    catch { guard.Dispose(); throw; }
                }
            }

            void BrokenOutOfALoopInsideTheTry(RwLock rwLock)
            {
                Guard guard = rwLock.Enter();

                try
                {
                    while (true)
                    {
                        Work();
                        _held = {|#8:guard|};
                        break;
                    }

                    Work();
                }
                catch
                {
                    {|#25:guard|}.Dispose();
                    throw;
                }
            }

            void GoneToALabel(RwLock rwLock, bool inside)
            {
                Guard first = rwLock.Enter();

                try { Work(); _held = first; goto done; }
                catch { first.Dispose(); throw; }

                done:
                Guard second = rwLock.Enter();

                try
                {
                    Work();
                    _held = {|#9:second|};
                    goto more;

                    more:
                    Work();
                }
                catch
                {
                    {|#26:second|}.Dispose();
                    throw;
                }
            }

            // An iterator that is disposed at a 'yield' runs its 'finally', and so does one that ends.
            IEnumerable<int> YieldedAfterTheStore(RwLock rwLock)
            {
                Guard guard = rwLock.Enter();

                try { Work(); _held = {|#10:guard|}; yield return 1; }
                finally { {|#27:guard|}.Dispose(); }
            }

            IEnumerable<int> EndedAfterTheStore(RwLock rwLock)
            {
                Guard guard = rwLock.Enter();

                try { yield return 1; _held = {|#11:guard|}; yield break; }
                finally { {|#28:guard|}.Dispose(); }
            }

            async Task AwaitedAfterTheStore(RwLock rwLock, Task pending)
            {
                Guard guard = rwLock.Enter();

                try { Work(); _held = {|#12:guard|}; await pending; }
                catch { {|#29:guard|}.Dispose(); throw; }
            }

            // Falling out of a 'lock' or a 'using' runs a hidden 'finally', which can throw inside the 'try'.
            void InsideALock(RwLock rwLock)
            {
                Guard guard = rwLock.Enter();

                try
                {
                    lock (_gate)
                    {
                        Work();
                        _held = {|#13:guard|};
                    }
                }
                catch
                {
                    {|#30:guard|}.Dispose();
                    throw;
                }
            }

            void InsideAUsing(RwLock rwLock)
            {
                Guard guard = rwLock.Enter();

                try
                {
                    using (Open())
                    {
                        Work();
                        _held = {|#14:guard|};
                    }
                }
                catch
                {
                    {|#31:guard|}.Dispose();
                    throw;
                }
            }

            // A condition after the store, with a branch that stays in the 'try'.
            void BeforeACondition(RwLock rwLock, bool more)
            {
                Guard guard = rwLock.Enter();

                try
                {
                    Work();
                    _held = {|#15:guard|};

                    if (more)
                        Work();
                }
                catch
                {
                    {|#32:guard|}.Dispose();
                    throw;
                }
            }

            void InABranch(RwLock rwLock, bool commit)
            {
                Guard guard = rwLock.Enter();

                try
                {
                    Work();

                    if (commit)
                        _held = guard;
                }
                catch
                {
                    guard.Dispose();
                    throw;
                }
            }

            // The end of a 'catch' block that is itself inside a 'try'.
            void InACatchInsideATry(RwLock rwLock)
            {
                Guard first = rwLock.Enter();

                try
                {
                    try { Fail(); }
                    catch { _held = first; }
                }
                catch
                {
                    first.Dispose();
                    throw;
                }

                Guard second = rwLock.Enter();

                try
                {
                    try { Fail(); }
                    catch { _held = {|#16:second|}; }

                    Work();
                }
                catch
                {
                    {|#33:second|}.Dispose();
                    throw;
                }
            }
        }

        {{Sources.Guard}}
        """,
        Verify.UsedAfterHandOffInHandler(17, "guard", "Guard", "its value was assigned", 0),
        Verify.UsedAfterHandOffInHandler(18, "guard", "Guard", "its value was assigned", 1),
        Verify.UsedAfterHandOffInHandler(19, "guard", "Guard", "its value was assigned", 2),
        Verify.UsedAfterHandOffInHandler(20, "guard", "Guard", "its value was assigned", 3),
        Verify.UsedAfterHandOffInHandler(21, "guard", "Guard", "its value was assigned", 4),
        Verify.UsedAfterHandOffInHandler(22, "guard", "Guard", "its value was assigned", 5),
        Verify.UsedAfterHandOffInHandler(23, "guard", "Guard", "its value was assigned", 6),
        Verify.UsedAfterHandOffInHandler(24, "guard", "Guard", "its value was assigned", 7),
        Verify.UsedAfterHandOffInHandler(25, "guard", "Guard", "its value was assigned", 8),
        Verify.UsedAfterHandOffInHandler(26, "second", "Guard", "its value was assigned", 9),
        Verify.UsedAfterHandOffInHandler(27, "guard", "Guard", "its value was assigned", 10),
        Verify.UsedAfterHandOffInHandler(28, "guard", "Guard", "its value was assigned", 11),
        Verify.UsedAfterHandOffInHandler(29, "guard", "Guard", "its value was assigned", 12),
        Verify.UsedAfterHandOffInHandler(30, "guard", "Guard", "its value was assigned", 13),
        Verify.UsedAfterHandOffInHandler(31, "guard", "Guard", "its value was assigned", 14),
        Verify.UsedAfterHandOffInHandler(32, "guard", "Guard", "its value was assigned", 15),
        Verify.UsedAfterHandOffInHandler(33, "second", "Guard", "its value was assigned", 16));

    [TestMethod]
    public Task LastUse_AFieldOfThisCanFollowTheCommit() => Verify.AnalyzerAsync(
        $$"""
        enum State
        {
            Free,
            Held,
        }

        class Other
        {
            public int State;
        }

        class C
        {
            static int s_count;

            Guard _held;
            Guard _second;
            State _state;
            volatile int _flag;
            Other _other = new();

            int Count { get; set; }

            public static void Work() { }
            static int Compute() => 1;

            // A state flag after the commit: in a class, a field of 'this' cannot be reached through a null reference.
            void Flagged(RwLock rwLock)
            {
                Guard guard = rwLock.Enter();

                try { Work(); _held = guard; _state = State.Held; }
                catch { guard.Dispose(); throw; }
            }

            void TwoGuards(RwLock rwLock)
            {
                Guard a = rwLock.Enter();
                Guard b = rwLock.Enter();

                try { Work(); _held = a; _second = b; }
                catch { a.Dispose(); b.Dispose(); throw; }
            }

            void Counted(RwLock rwLock)
            {
                Guard guard = rwLock.Enter();

                try { Work(); _held = guard; _flag = _flag + 1; _state = State.Held; }
                catch { guard.Dispose(); throw; }
            }

            // The first use of a static field can run the initializer of its type.
            void StaticField(RwLock rwLock)
            {
                Guard guard = rwLock.Enter();

                try { Work(); _held = {|#0:guard|}; s_count = 1; }
                catch { {|#5:guard|}.Dispose(); throw; }
            }

            void FieldOfAnotherObject(RwLock rwLock)
            {
                Guard guard = rwLock.Enter();

                try { Work(); _held = {|#1:guard|}; _other.State = 1; }
                catch { {|#6:guard|}.Dispose(); throw; }
            }

            void Property(RwLock rwLock)
            {
                Guard guard = rwLock.Enter();

                try { Work(); _held = {|#2:guard|}; Count = 1; }
                catch { {|#7:guard|}.Dispose(); throw; }
            }

            void ValueThatCanThrow(RwLock rwLock)
            {
                Guard guard = rwLock.Enter();

                try { Work(); _held = {|#3:guard|}; _flag = Compute(); }
                catch { {|#8:guard|}.Dispose(); throw; }
            }
        }

        // The 'this' of a struct is a reference that its caller gives, and a reference can be null. Where the commit is a store
        // through 'this', it shows that 'this' refers to something. Where the value goes anywhere else, nothing does.
        [Singulink.CodeAnalysis.NonCopyable]
        struct S
        {
            Guard _held;
            int _state;

            void Flagged(RwLock rwLock)
            {
                Guard guard = rwLock.Enter();

                try { C.Work(); _held = guard; _state = 1; }
                catch { guard.Dispose(); throw; }
            }

            void FlaggedAfterAStoreElsewhere(RwLock rwLock, ref Guard slot)
            {
                Guard guard = rwLock.Enter();

                try { C.Work(); slot = {|#4:guard|}; _state = 1; }
                catch { {|#9:guard|}.Dispose(); throw; }
            }
        }

        {{Sources.Guard}}
        """,
        Verify.UsedAfterHandOffInHandler(5, "guard", "Guard", "its value was assigned", 0),
        Verify.UsedAfterHandOffInHandler(6, "guard", "Guard", "its value was assigned", 1),
        Verify.UsedAfterHandOffInHandler(7, "guard", "Guard", "its value was assigned", 2),
        Verify.UsedAfterHandOffInHandler(8, "guard", "Guard", "its value was assigned", 3),
        Verify.UsedAfterHandOffBeforeNullReference(9, "guard", "Guard", "its value was assigned", "this", 4));

    [TestMethod]
    public Task LastUse_APrimaryConstructorParameterThatOneInitializerUses() => Verify.AnalyzerAsync(
        $$"""
        using System;

        // No member uses the parameter, so it is no field: it is an ordinary parameter, used once.
        class Holder(Guard guard)
        {
            private Guard _guard = guard;
        }

        class Derived(Guard guard) : Holder(guard);

        // Initializers have no order that the paths follow.
        class TwoInitializers(Guard guard)
        {
            private Guard _first = {|#0:guard|};
            private Guard _second = {|#1:guard|};
        }

        class InitializerAndMember(Guard guard)
        {
            private Guard _guard = {|#2:guard|};

            public void Release() => guard.Dispose();
        }

        class InitializerAndBase(Guard guard) : Holder({|#3:guard|})
        {
            private Guard _guard = {|#4:guard|};
        }

        class CapturedByALambda(Guard guard)
        {
            private Func<Guard> _take = () => {|#5:guard|};
        }

        // A use in another part of the type counts like any other.
        partial class InTwoParts(Guard guard)
        {
            private Guard _guard = {|#6:guard|};
        }

        partial class InTwoParts
        {
            public void Release() => guard.Dispose();
        }

        {{Sources.Guard}}
        """,
        Verify.CopyOfStoredValue(0, "The initializer takes", "Guard"),
        Verify.CopyOfStoredValue(1, "The initializer takes", "Guard"),
        Verify.CopyOfStoredValue(2, "The initializer takes", "Guard"),
        Verify.CopyOfStoredValue(3, "'Holder.Holder(Guard)' receives", "Guard"),
        Verify.CopyOfStoredValue(4, "The initializer takes", "Guard"),
        Verify.CopyByReturnOfLivingVariable(5, "Guard", "guard"),
        Verify.CopyOfStoredValue(6, "The initializer takes", "Guard"));

    // What the move on the last use keeps clean, and a [MovedFrom] method whose check does not depend on it.
    private const string ImplicitMoves = $$"""
        using System.Threading.Tasks;
        using Singulink.CodeAnalysis;

        class Holder(Guard guard)
        {
            Guard _guard = {|#5:guard|};
        }

        class C
        {
            Guard _held;

            static void Work() { }
            static void Use(Guard guard) { }
            static async Task<Guard> WaitAsync(ValueTask<Guard> pending) => {|#9:await pending|};

            void Stored(RwLock rwLock)
            {
                Guard guard = rwLock.Enter();
                _held = {|#0:guard|};
            }

            void PassedOn(RwLock rwLock)
            {
                Guard guard = rwLock.Enter();
                Use({|#1:guard|});
            }

            // The fast path of a value task: the task is read where it has completed and passed on where it has not.
            Task<Guard> FastPath(RwLock rwLock)
            {
                var pending = rwLock.EnterAsync();

                if (!pending.IsCompletedSuccessfully)
                    return WaitAsync({|#2:pending|});

                using var held = {|#7:pending.Result|};
                return null!;
            }

            async Task Awaited(RwLock rwLock)
            {
                Task<Guard> pending = rwLock.EnterTaskAsync();
                using Guard guard = {|#3:await pending|};
            }

            // The commit at the end of a 'try' is a form of the last use.
            void Committed(RwLock rwLock)
            {
                Guard guard = rwLock.Enter();

                try { Work(); _held = {|#4:guard|}; }
                catch { guard.Dispose(); throw; }
            }

            // A use by a handler: with the moves on, the handler's use is reported as a use after the hand-off.
            void UsedByAHandler(RwLock rwLock)
            {
                Guard guard = rwLock.Enter();

                try { _held = {|#8:guard|}; Work(); }
                catch { {|#10:guard|}.Dispose(); throw; }
            }

            // An explicit move hands a value over either way.
            void Moved(RwLock rwLock)
            {
                Guard guard = rwLock.Enter();
                _held = NonCopyable.Move(ref guard);
            }

            // The paths of a [MovedFrom] parameter are checked either way.
            static Guard LeavesWithTheValue([MovedFrom] ref Guard value, bool reset)
            {
                Guard moved = {|#6:value|};

                if (reset)
                    value = default;

                return moved;
            }
        }

        {{Sources.Guard}}
        """;

    [TestMethod]
    public Task ImplicitMoves_AreOnWithoutTheProperty() => Verify.AnalyzerAsync(
        ImplicitMoves,
        Verify.UsedAfterHandOffInHandler(10, "guard", "Guard", "its value was assigned", 8),
        Verify.MovedFromParameterNotAssigned(6, "C.LeavesWithTheValue(ref Guard, bool)", "value"));

    [TestMethod]
    [DataRow("true")]
    [DataRow("")]
    [DataRow("no")]
    [DataRow("0")]
    public Task ImplicitMoves_OnlyFalseTurnsThemOff(string value)
    {
        var test = CreateTest(ImplicitMoves, value);
        test.ExpectedDiagnostics.Add(Verify.UsedAfterHandOffInHandler(10, "guard", "Guard", "its value was assigned", 8));
        test.ExpectedDiagnostics.Add(Verify.MovedFromParameterNotAssigned(6, "C.LeavesWithTheValue(ref Guard, bool)", "value"));
        return test.RunAsync();
    }

    // With the moves off every by-value read of a variable is a read of a stored value, and no message has the note about a handler.
    [TestMethod]
    [DataRow("false")]
    [DataRow("False")]
    [DataRow(" FALSE ")]
    public Task ImplicitMoves_AreTurnedOffByTheProperty(string value)
    {
        var test = CreateTest(ImplicitMoves, value);
        test.ExpectedDiagnostics.Add(Verify.CopyByAssignment(0, "Guard"));
        test.ExpectedDiagnostics.Add(Verify.CopyOfStoredValue(1, "'C.Use(Guard)' receives", "Guard"));
        test.ExpectedDiagnostics.Add(Verify.CopyOfStoredValue(2, "'C.WaitAsync(ValueTask<Guard>)' receives", "System.Threading.Tasks.ValueTask<Guard>"));
        test.ExpectedDiagnostics.Add(Verify.ReadFromStoredContainer(3, "'await'", "Guard"));
        test.ExpectedDiagnostics.Add(Verify.CopyByAssignment(4, "Guard"));
        test.ExpectedDiagnostics.Add(Verify.CopyOfStoredValue(5, "The initializer takes", "Guard"));
        test.ExpectedDiagnostics.Add(Verify.MovedFromParameterNotAssigned(6, "C.LeavesWithTheValue(ref Guard, bool)", "value"));
        test.ExpectedDiagnostics.Add(Verify.ReadFromStoredContainer(7, "'ValueTask<TResult>.Result'", "Guard"));
        test.ExpectedDiagnostics.Add(Verify.CopyByAssignment(8, "Guard"));
        test.ExpectedDiagnostics.Add(Verify.ReadFromStoredContainer(9, "'await'", "Guard"));
        return test.RunAsync();
    }

    private static Verify.Test CreateTest(string source, string allowImplicitMoves) =>
        new Verify.Test { TestCode = source }.WithBuildProperty("NonCopyableAllowImplicitMoves", allowImplicitMoves);

    private const string Returned = $$"""
        using System;
        using System.Threading.Tasks;

        ref struct Releaser
        {
            private ref Guard _guard;

            public Releaser(ref Guard guard) => _guard = ref guard;

            public void Dispose() => _guard.Dispose();
        }

        class C
        {
            static void Work() { }

            // The value to return is read first, and then the 'finally' uses the variable.
            Guard InATry(RwLock rwLock)
            {
                Guard guard = rwLock.Enter();

                try { return {|#0:guard|}; }
                finally { {|#10:guard|}.Dispose(); }
            }

            async Task<Guard> Async(RwLock rwLock)
            {
                Guard guard = rwLock.Enter();

                try
                {
                    await Task.Yield();
                    return {|#1:guard|};
                }
                finally
                {
                    {|#11:guard|}.Dispose();
                }
            }

            Func<Guard> InALambda(RwLock rwLock) => () =>
            {
                Guard guard = rwLock.Enter();

                try { return {|#2:guard|}; }
                finally { {|#12:guard|}.Dispose(); }
            };

            Guard? AsANullable(Guard guard)
            {
                try { return {|#3:guard|}; }
                finally { {|#13:guard|}.Dispose(); }
            }

            Guard InAnInnerTry(RwLock rwLock)
            {
                Guard guard = rwLock.Enter();

                try
                {
                    try { return {|#4:guard|}; }
                    catch (InvalidOperationException) { throw; }
                }
                finally
                {
                    {|#14:guard|}.Dispose();
                }
            }

            // A scope that holds a reference to the variable uses it where the scope ends.
            Guard InAScopeThatRefersToIt(RwLock rwLock)
            {
                Guard guard = rwLock.Enter();
                using var releaser = new Releaser(ref guard);
                return {|#5:guard|};
            }

            // A 'finally' that resets the variable, or that leaves it alone, runs after the return with no harm.
            Guard ResetAfterwards(RwLock rwLock)
            {
                Guard guard = rwLock.Enter();

                try { return guard; }
                finally { guard = default; }
            }

            Guard InAnUnrelatedScope(RwLock rwLock, IDisposable other)
            {
                Guard guard = rwLock.Enter();

                using (other)
                {
                    Work();
                    return guard;
                }
            }

            Guard WithAnUnrelatedFinally(RwLock rwLock)
            {
                Guard guard = rwLock.Enter();

                try { return guard; }
                finally { Work(); }
            }

            Guard AfterTheTry(RwLock rwLock)
            {
                Guard guard = rwLock.Enter();

                try { Work(); }
                catch { guard.Dispose(); throw; }

                return guard;
            }
        }

        {{Sources.Guard}}
        """;

    // With the moves on, the 'finally' that uses the variable after the return is reported as a use after the hand-off.
    private static readonly Microsoft.CodeAnalysis.Testing.DiagnosticResult[] s_returnedAndUsed = [
        Verify.UsedAfterHandOffInHandler(10, "guard", "Guard", "its value was returned", 0),
        Verify.UsedAfterHandOffInHandler(11, "guard", "Guard", "its value was returned", 1),
        Verify.UsedAfterHandOffInHandler(12, "guard", "Guard", "its value was returned", 2),
        Verify.UsedAfterHandOffInHandler(13, "guard", "Guard", "its value was returned", 3),
        Verify.UsedAfterHandOffInHandler(14, "guard", "Guard", "its value was returned", 4),
        Verify.CopyByReturnOfVariableUsedAfter(5, "Guard", "guard"),
    ];

    // With the moves off, every such return is reported where it is.
    private static readonly Microsoft.CodeAnalysis.Testing.DiagnosticResult[] s_returnedAndUsedWithTheMovesOff = [
        Verify.CopyByReturnOfVariableUsedAfter(0, "Guard", "guard"),
        Verify.CopyByReturnOfVariableUsedAfter(1, "Guard", "guard"),
        Verify.CopyByReturnOfVariableUsedAfter(2, "Guard", "guard"),
        Verify.CopyByReturnOfVariableUsedAfter(3, "Guard?", "guard"),
        Verify.CopyByReturnOfVariableUsedAfter(4, "Guard", "guard"),
        Verify.CopyByReturnOfVariableUsedAfter(5, "Guard", "guard"),
    ];

    [TestMethod]
    public Task LastUse_AReturnedVariableIsJudgedLikeEveryOtherRead() => Verify.AnalyzerAsync(Returned, s_returnedAndUsed);

    // The return of a variable does not depend on the move on the last use.
    [TestMethod]
    public Task LastUse_AReturnedVariableIsJudgedWithTheMovesTurnedOff()
    {
        var test = CreateTest(Returned, "false");
        test.ExpectedDiagnostics.AddRange(s_returnedAndUsedWithTheMovesOff);
        return test.RunAsync();
    }

    [TestMethod]
    public Task LastUse_ASetterRunsAfterTheValueThatIsAssigned() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using Singulink.CodeAnalysis;

        [NonCopyable]
        struct Lease
        {
            public int Field;

            public int Uses { get => 0; set { } }

            public int this[int index] { get => 0; set { } }

            public event Action Closed { add { } remove { } }

            public static int Close(Lease lease) => 0;

            public static Action CloseLater(Lease lease) => () => { };
        }

        class C
        {
            // The setter, the indexer setter or the event accessor runs on the variable after the value was handed over.
            void M()
            {
                Lease first = default;
                {|#6:first|}.Uses = Lease.Close({|#0:first|});

                Lease second = default;
                {|#7:second|}[0] = Lease.Close({|#1:second|});

                Lease third = default;
                {|#8:third|}.Uses += Lease.Close({|#2:third|});

                Lease fourth = default;
                {|#9:fourth|}.Closed += Lease.CloseLater({|#3:fourth|});

                (Lease lease, int count) fifth = default;
                {|#11:fifth|}.lease.Uses = Lease.Close({|#4:{|#10:fifth|}.lease|});

                // A field is assigned where the assignment stands as well.
                Lease sixth = default;
                {|#12:sixth|}.Field = Lease.Close({|#5:sixth|});
            }
        }
        """,
        Verify.UsedAfterHandOff(6, "first", "Lease", "'Lease.Close(Lease)' received its value", 0),
        Verify.UsedAfterHandOff(7, "second", "Lease", "'Lease.Close(Lease)' received its value", 1),
        Verify.UsedAfterHandOff(8, "third", "Lease", "'Lease.Close(Lease)' received its value", 2),
        Verify.UsedAfterHandOff(9, "fourth", "Lease", "'Lease.CloseLater(Lease)' received its value", 3),
        Verify.UsedAfterHandOff(12, "sixth", "Lease", "'Lease.Close(Lease)' received its value", 5),
        Verify.UsedAfterHandOff(11, "fifth", "(Lease lease, int count)", "its field \'lease\' was read", 10));

    // An assignment stores when it runs, wherever its target stands: the outer target is evaluated before the commit and written after it.
    [TestMethod]
    public Task LastUse_AnOuterAssignmentStoresAfterTheCommitThatIsItsValue() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using Singulink.CodeAnalysis;

        class Node
        {
            public IDisposable? Owner;
            public bool Flag;
        }

        class C
        {
            static bool s_fresh;

            IDisposable? _current;
            Guard _held;
            bool _fresh;

            static void Work() { }

            // A null 'node' fails with the box in '_current'.
            void IntoAFieldOfAnObject(RwLock rwLock, Node node)
            {
                Guard g = rwLock.Enter();

                try { Work(); node.Owner = _current = {|#0:g|}; }
                catch { {|#5:g|}.Dispose(); throw; }
            }

            void IntoAnElement(RwLock rwLock, IDisposable?[] owners, int index)
            {
                Guard g = rwLock.Enter();

                try { Work(); owners[index] = _current = {|#1:g|}; }
                catch { {|#6:g|}.Dispose(); throw; }
            }

            void ThroughAReference(RwLock rwLock, ref bool fresh)
            {
                Guard g = rwLock.Enter();

                try { Work(); fresh = (_held = {|#2:g|}) is { Value: 0 }; }
                catch { {|#7:g|}.Dispose(); throw; }
            }

            void IntoAStaticField(RwLock rwLock)
            {
                Guard g = rwLock.Enter();

                try { Work(); s_fresh = (_held = {|#3:g|}) is { Value: 0 }; }
                catch { {|#8:g|}.Dispose(); throw; }
            }

            // The stores of a deconstruction are made when it runs as well.
            void ByADeconstruction(RwLock rwLock, Node node)
            {
                Guard g = rwLock.Enter();
                int n;

                try { Work(); (node.Flag, n) = ((_held = {|#4:g|}) is { Value: 0 }, 1); }
                catch { {|#9:g|}.Dispose(); throw; }
            }

            // A local and a field of 'this' in a class cannot fail.
            void IntoALocal(RwLock rwLock)
            {
                Guard g = rwLock.Enter();
                bool fresh;

                try { Work(); fresh = (_held = g) is { Value: 0 }; }
                catch { g.Dispose(); throw; }
            }

            void IntoAFieldOfThis(RwLock rwLock)
            {
                Guard g = rwLock.Enter();
                int n;

                try { Work(); _fresh = (_held = g) is { Value: 0 }; (_fresh, n) = (true, 1); }
                catch { g.Dispose(); throw; }
            }
        }

        {{Sources.Guard}}
        """,
        Verify.UsedAfterHandOffInHandler(5, "g", "Guard", "its value was converted to \'IDisposable\'", 0),
        Verify.UsedAfterHandOffInHandler(6, "g", "Guard", "its value was converted to \'IDisposable\'", 1),
        Verify.UsedAfterHandOffBeforeNullReference(7, "g", "Guard", "its value was assigned", "fresh", 2),
        Verify.UsedAfterHandOffInHandler(8, "g", "Guard", "its value was assigned", 3),
        Verify.UsedAfterHandOffInHandler(9, "g", "Guard", "its value was assigned", 4));

    [TestMethod]
    public Task LastUse_ThisInAStructCanFailAfterTheCommit() => Verify.AnalyzerAsync(
        $$"""
        using Singulink.CodeAnalysis;

        struct Host
        {
            public int State;

            static void Work() { }

            void Resets(RwLock rwLock, ref Guard slot)
            {
                Guard g = rwLock.Enter();

                try { Work(); slot = {|#0:g|}; this = default; }
                catch { {|#4:g|}.Dispose(); throw; }
            }

            void Copies(RwLock rwLock, ref Guard slot)
            {
                Guard g = rwLock.Enter();

                try { Work(); slot = {|#1:g|}; Host copy = this; }
                catch { {|#5:g|}.Dispose(); throw; }
            }

            bool Tests(RwLock rwLock, ref Guard slot)
            {
                Guard g = rwLock.Enter();
                bool isFirst;

                try { Work(); slot = {|#2:g|}; isFirst = this is { State: 1 }; }
                catch { {|#6:g|}.Dispose(); throw; }

                return isFirst;
            }
        }

        struct Seeded(int seed)
        {
            static void Work() { }

            int Reads(RwLock rwLock, ref Guard slot)
            {
                Guard g = rwLock.Enter();
                int read;

                try { Work(); slot = {|#3:g|}; read = seed; }
                catch { {|#7:g|}.Dispose(); throw; }

                return read;
            }
        }

        class Owner(int seed)
        {
            public int State;

            Guard _held;

            static void Work() { }

            int Reads(RwLock rwLock)
            {
                Guard g = rwLock.Enter();
                int read;
                Owner same;

                try { Work(); _held = g; read = seed; same = this; }
                catch { g.Dispose(); throw; }

                return read + same.State;
            }
        }

        {{Sources.Guard}}
        """,
        Verify.UsedAfterHandOffBeforeNullReference(4, "g", "Guard", "its value was assigned", "this", 0),
        Verify.UsedAfterHandOffBeforeNullReference(5, "g", "Guard", "its value was assigned", "this", 1),
        Verify.UsedAfterHandOffBeforeNullReference(6, "g", "Guard", "its value was assigned", "this", 2),
        Verify.UsedAfterHandOffBeforeNullReference(7, "g", "Guard", "its value was assigned", "this", 3));

    // The note on a read that would be a last use names a 'catch', a 'finally' or a filter that uses the variable only where there is
    // one. The 'finally' that the compiler adds for a 'foreach' uses nothing: the variable is used by what follows the loop.
    [TestMethod]
    public Task LastUse_TheNoteSaysWhatStillUsesTheVariable() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using System.Collections.Generic;
        using Singulink.CodeAnalysis;

        class C
        {
            static void Consume(Guard guard) { }

            void AfterAForEach(RwLock rwLock, IEnumerable<int> numbers)
            {
                Guard g = rwLock.Enter();

                foreach (int number in numbers)
                {
                    Consume({|#0:g|});
                    g = rwLock.Enter();
                }

                g.Dispose();
            }

            void InAHandler(RwLock rwLock)
            {
                Guard g = rwLock.Enter();

                try
                {
                    Consume({|#1:g|});
                }
                catch
                {
                    {|#2:g|}.Dispose();
                    throw;
                }
            }
        }

        {{Sources.Guard}}
        """,
        Verify.CopyOfStoredValue(0, "'C.Consume(Guard)' receives", "Guard", usedAfterFinally: "g"),
        Verify.UsedAfterHandOffInHandler(2, "g", "Guard", "'C.Consume(Guard)' received its value", 1));

    // A hole of an interpolated string takes its value like a parameter that takes it by value: a new value is moved into it, and so
    // is a local or a by-value parameter at its last use. A stored value is copied.
    [TestMethod]
    public Task Interpolation_TakesANewValue() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using Singulink.CodeAnalysis;

        sealed class C
        {
            private Guard _field;

            static Guard Create() => default;

            static string Show(Guard guard) => $"{guard}";

            static string New(int form) => $"{Create()} { form switch { 0 => Create(), _ => new Guard() } }";

            static string AtItsLastUse()
            {
                var local = Create();
                return $"local at its last use: {local,10}";
            }

            static string UsedAgain(Guard guard)
            {
                string text = $"{ {|#0:guard|} }";
                {|#3:guard|}.Dispose();
                return text;
            }

            string Stored(in Guard byReference) => $"{ {|#1:_field|} } { {|#2:byReference|} }";
        }

        {{Sources.Guard}}
        """,
        Verify.UsedAfterHandOff(3, "guard", "Guard", "the interpolated string took its value", 0),
        Verify.CopyOfStoredValue(1, "The interpolated string takes", "Guard"),
        Verify.CopyOfStoredValue(2, "The interpolated string takes", "Guard"));

    // The operand of 'with' is copied, so a variable that is not used again after it is moved into the copy like into any other
    // read by value. One that is used again is copied, and so is a field.
    [TestMethod]
    public Task WithExpression_OperandAtItsLastUse_IsANewValue() => Verify.AnalyzerAsync(
        $$"""
        class C
        {
            Guard _held;

            static Guard Create() => default;

            static void Take(Guard guard) { }

            void Declared()
            {
                Guard guard = Create();
                var next = guard with { Value = 1 };
                next.Dispose();
            }

            Guard Returned(Guard guard) => guard with { Value = 2 };

            Guard ReturnedFromABlock()
            {
                Guard guard = Create();
                return guard with { Value = 3 };
            }

            void Passed(Guard guard) => Take(guard with { });

            void Stored()
            {
                Guard guard = Create();
                _held = guard with { Value = 4 };
            }

            // The variable is used again, in the expression itself or after it.
            void UsedAgain()
            {
                Guard guard = Create();
                var next = {|#0:guard|} with { Value = 1 };
                {|#6:guard|}.Dispose();
            }

            void ReadInTheInitializer()
            {
                Guard guard = Create();
                var next = {|#1:guard|} with { Value = {|#7:guard|}.Value + 1 };
            }

            void InALoop(int count)
            {
                Guard guard = Create();

                for (int i = 0; i < count; i++)
                    Take({|#2:guard|} with { Value = i });
            }

            void InATry()
            {
                Guard guard = Create();

                try
                {
                    Take({|#3:guard|} with { });
                }
                finally
                {
                    {|#8:guard|}.Dispose();
                }
            }

            // A field is a stored value, and so is what a reference refers to.
            void OfAField() => Take({|#4:_held|} with { });

            void ThroughAReference(ref Guard guard) => Take({|#5:guard|} with { });
        }

        {{Sources.Guard}}
        """,
        Verify.UsedAfterHandOff(6, "guard", "Guard", "\'with\' copied its value", 0),
        Verify.UsedAfterHandOff(7, "guard", "Guard", "\'with\' copied its value", 1),
        Verify.UsedAfterHandOffInHandler(8, "guard", "Guard", "\'with\' copied its value", 3),
        Verify.CopyOfStoredValue(2, "'with' changes", "Guard"),
        Verify.CopyOfStoredValue(4, "'with' changes", "Guard"),
        Verify.CopyOfStoredValue(5, "'with' changes", "Guard"));

    // A setter runs where the assignment is made, after the value that is assigned. With a branch in the value the flow graph
    // captures the target before the branch and assigns the capture after it: the setter still runs after the read in the value, so
    // that read is not the last use. With no use after the branch, the read is.
    [TestMethod]
    public Task LastUse_ASetterAfterABranchInItsValueRunsAfterTheRead() => Verify.AnalyzerAsync(
        $$"""
        using Singulink.CodeAnalysis;

        [NonCopyable]
        struct Lease
        {
            public int Count;

            public int Uses { set => Count = value; }

            public int this[int index] { set => Count = value; }
        }

        static class C
        {
            static Lease Make() => default;

            static int Close(Lease lease) => 0;

            static void Plain()
            {
                Lease lease = Make();
                {|#5:lease|}.Uses = Close({|#0:lease|});
            }

            static void WithABranch(bool flag)
            {
                Lease lease = Make();
                {|#6:lease|}.Uses = flag ? Close({|#1:lease|}) : 0;
            }

            static void WithACoalesce(int? count)
            {
                Lease lease = Make();
                {|#7:lease|}.Uses = count ?? Close({|#2:lease|});
            }

            static void IndexerWithABranch(bool flag)
            {
                Lease lease = Make();
                {|#8:lease|}[flag ? 1 : 0] = Close({|#3:lease|});
            }

            static void UsedBeforeAndAfter(bool flag)
            {
                Lease lease = Make();
                lease.Uses = flag ? 1 : 2;
                Close(lease);
            }

            static void Compound(bool flag)
            {
                Lease lease = Make();
                lease.Count += flag ? Close({|#4:lease|}) : 0;
            }
        }
        """,
        Verify.UsedAfterHandOff(5, "lease", "Lease", "'C.Close(Lease)' received its value", 0),
        Verify.UsedAfterHandOff(6, "lease", "Lease", "'C.Close(Lease)' received its value", 1),
        Verify.UsedAfterHandOff(7, "lease", "Lease", "'C.Close(Lease)' received its value", 2),
        Verify.UsedAfterHandOff(8, "lease", "Lease", "'C.Close(Lease)' received its value", 3),
        Verify.CopyOfStoredValue(4, "'C.Close(Lease)' receives", "Lease"));
}
