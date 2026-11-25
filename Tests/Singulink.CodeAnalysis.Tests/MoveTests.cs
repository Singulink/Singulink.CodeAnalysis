using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;
using PrefixClassName.MsTest;

namespace Singulink.CodeAnalysis.Tests;

/// <summary>
/// Tests of the explicit move: <c>[MovedFrom]</c> and the <c>NonCopyable.Move</c> helper of the attributes package.
/// </summary>
/// <remarks>
/// Docs: articles/rules/moved-from.md.
/// </remarks>
[PrefixTestClass]
public class MoveTests
{
    [TestMethod]
    public Task Move_HandsAValueOverWithoutACopy() => Verify.AnalyzerAsync(
        $$"""
        using System.Threading.Tasks;
        using Singulink.CodeAnalysis;

        class C
        {
            Guard _guard;

            static void Consume(Guard guard) { }

            async Task M(RwLock rwLock)
            {
                var guard = rwLock.Enter();
                Guard current = default;
                var next = rwLock.Enter();

                _guard = NonCopyable.Move(ref guard);
                current = NonCopyable.Move(ref next);
                Consume(NonCopyable.Move(ref guard));

                var pending = rwLock.EnterWriteGuardAsync();
                using var held = await NonCopyable.Move(ref pending);

                // Out of a field, an array element and a nullable.
                var fromField = NonCopyable.Move(ref _guard);
                var array = new Guard[1];
                var fromArray = NonCopyable.Move(ref array[0]);
                Guard? maybe = rwLock.TryEnter();
                Guard? moved = NonCopyable.Move(ref maybe);

                // A container can be moved as well, which makes it a new value.
                Task<Guard> task = rwLock.EnterTaskAsync();
                Guard fromTask = await NonCopyable.Move(ref task);

                // Without the move a read is a copy, unless it is the last use of the variable.
                _guard = {|#0:guard|};
                Consume({|#1:guard|});
            }
        }

        public static class Extensions
        {
            public static ValueTask<Guard> EnterWriteGuardAsync(this RwLock rwLock) => rwLock.EnterAsync();
        }

        {{Sources.Guard}}
        """,
        Verify.UsedAfterHandOff(1, "guard", "Guard", "its value was assigned", 0));

    [TestMethod]
    public Task Move_CannotTakeFromAUsingVariableOrAReadOnlyField() => Verify.AnalyzerAsync(
        $$"""
        using Singulink.CodeAnalysis;

        class C
        {
            readonly Guard _readOnly;

            static void Consume(Guard guard) { }

            void M(RwLock rwLock)
            {
                // Something else still owns the value, and the compiler does not let either be passed by reference. Code that does
                // not compile cannot run, so it gets the compiler's error alone.
                using var held = rwLock.Enter();
                Consume(NonCopyable.Move(ref {|CS1657:held|}));
                Consume(NonCopyable.Move(ref {|CS0192:_readOnly|}));
            }
        }

        {{Sources.Guard}}
        """);

    [TestMethod]
    public Task MovedFrom_AllowsReadingTheParameterByValue() => Verify.AnalyzerAsync(
        """
        using Singulink.CodeAnalysis;

        static class Moves
        {
            // The helper for one type that the specification gives for targets on which a ref struct cannot be a type argument.
            public static JournalSessionGuard Move([MovedFrom] ref JournalSessionGuard value)
            {
                JournalSessionGuard moved = value;
                value = default;
                return moved;
            }

            public static T MoveAny<[NonCopyable] T>([MovedFrom] ref T value)
            {
                T moved = value;
                value = default!;
                return moved;
            }

            public static T Swap<[NonCopyable] T>([MovedFrom] ref T value, T replacement)
            {
                T moved = value;
                value = NonCopyable.Move(ref replacement);
                return moved;
            }

            // Without the attribute the same body copies.
            public static T Copy<[NonCopyable] T>(ref T value)
            {
                T moved = {|#0:value|};
                value = default!;
                return moved;
            }
        }

        class C
        {
            void M()
            {
                var guard = new JournalSessionGuard();
                var moved = Moves.Move(ref guard);

                var other = new Other();
                var movedOther = Moves.MoveAny(ref other);
                var swapped = Moves.Swap(ref movedOther, new Other());
            }
        }

        [NonCopyable]
        ref struct JournalSessionGuard { }

        [NonCopyable]
        struct Other { }
        """,
        Verify.CopyByAssignment(0, "T"));

    [TestMethod]
    public Task MovedFrom_UsedWrongly() => Verify.AnalyzerAsync(
        """
        using System;
        using Singulink.CodeAnalysis;

        static class Moves
        {
            public static Guard NotRef([MovedFrom] Guard {|#0:value|}) => value;

            public static void In([MovedFrom] in Guard {|#1:value|}) { }

            public static void Out([MovedFrom] out Guard {|#2:value|}) => value = default;

            public static void RefReadOnly([MovedFrom] ref readonly Guard {|#3:value|}) { }

            // The caller would be left with the value that was moved out.
            public static Guard NeverAssigned([MovedFrom] ref Guard value) => {|#4:value|};

            // A method that only uses the parameter in place has taken nothing out of it.
            public static void UsedInPlace([MovedFrom] ref Guard value) => value.Dispose();

            // Passing the parameter on by reference counts: what receives it can reset it.
            public static Guard PassedOn([MovedFrom] ref Guard value) => Reset(ref value);

            public static void AssignedInOneBranch([MovedFrom] ref Guard value, bool condition)
            {
                if (condition)
                    value = default;
            }

            public static void ResetByOut([MovedFrom] ref Guard value) => Create(out value);

            static Guard Reset([MovedFrom] ref Guard value)
            {
                var moved = value;
                value = default;
                return moved;
            }

            static void Create(out Guard value) => value = default;

            [MovedFrom]
            public static void {|#6:OnMethod|}() { }

            [return: MovedFrom]
            public static int {|#7:OnReturn|}() => 0;

            [MovedFrom]
            static int {|#8:s_field|};

            [MovedFrom]
            public static int {|#9:Property|} => 0;

            public static void OnTypeParameter<[MovedFrom] {|#10:T|}>() { }

            static void Local()
            {
                static void NotRefLocal([MovedFrom] Guard {|#11:value|}) { }

                static Guard NeverAssignedLocal([MovedFrom] ref Guard value) => {|#12:value|};

                Func<Guard, int> lambda = ([MovedFrom] {|#13:value|}) => 0;
            }
        }

        // An abstract method has no body that could be checked.
        abstract class Mover
        {
            public abstract Guard Move([MovedFrom] ref Guard value);
        }

        [MovedFrom]
        class {|#14:OnType|} { }

        delegate void Mover2([MovedFrom] Guard {|#15:value|});

        namespace Singulink.CodeAnalysis
        {
            sealed class MovedFromAttribute : Attribute;

            sealed class NonCopyableAttribute : Attribute;
        }

        [NonCopyable]
        struct Guard
        {
            public void Dispose() { }
        }
        """,
        Verify.MovedFromNotOnRefParameter(0),
        Verify.MovedFromNotOnRefParameter(1),
        Verify.MovedFromNotOnRefParameter(2),
        Verify.MovedFromNotOnRefParameter(3),
        Verify.MovedFromParameterNotAssigned(4, "Moves.NeverAssigned(ref Guard)", "value"),
        Verify.MovedFromNotOnRefParameter(6),
        Verify.MovedFromNotOnRefParameter(7),
        Verify.MovedFromNotOnRefParameter(8),
        Verify.MovedFromNotOnRefParameter(9),
        Verify.MovedFromNotOnRefParameter(10),
        Verify.MovedFromNotOnRefParameter(11),
        Verify.MovedFromParameterNotAssigned(12, "NeverAssignedLocal(ref Guard)", "value"),
        Verify.MovedFromNotOnRefParameter(13),
        Verify.MovedFromNotOnRefParameter(14),
        Verify.MovedFromNotOnRefParameter(15));

    [TestMethod]
    public Task MovedFrom_OnlyOneReadCanBeTheMove() => Verify.AnalyzerAsync(
        $$"""
        using System.Collections.Generic;
        using Singulink.CodeAnalysis;

        static class Moves
        {
            static Guard s_a;
            static Guard s_b;

            public static (Guard, Guard) Dup([MovedFrom] ref Guard v)
            {
                var r = (v, {|#0:v|});
                v = default;
                return r;
            }

            public static void Store([MovedFrom] ref Guard v)
            {
                s_a = v;
                {|#6:s_b|} = {|#1:v|};
                v = default;
            }

            public static Guard Twice([MovedFrom] ref Guard v)
            {
                Guard a = v;
                Guard b = {|#2:v|};
                v = default;
                return a;
            }

            public static Guard AssignedToItself([MovedFrom] ref Guard v)
            {
                Guard m = v;
                v = {|#3:v|};
                return m;
            }

            public static Guard InALoop([MovedFrom] ref Guard v)
            {
                Guard r = default;

                for (int i = 0; i < 2; i++)
                    r = {|#4:v|};

                v = default;
                return r;
            }

            // '??=' is not the assignment that a move needs: it leaves a value that is not null in place.
            public static List<Guard> Coalesce([MovedFrom] ref List<Guard> v)
            {
                var m = {|#5:v|};
                v ??= {|#7:new()|};
                return m;
            }

            // A loop that assigns the parameter after every read moves a value out each time.
            public static void Drain([MovedFrom] ref Guard v)
            {
                for (int i = 0; i < 2; i++)
                {
                    Guard taken = v;
                    v = default;
                    taken.Dispose();
                }
            }

            // One read, and uses that are not reads of the value.
            public static Guard Once([MovedFrom] ref Guard v)
            {
                Guard m = v;
                v = default;
                return m;
            }

            public static void InPlace([MovedFrom] ref Guard v)
            {
                v.Dispose();
                v.Dispose();
                int value = v.Value;
                ref Guard reference = ref v;
                v = default;
            }

            public static Guard PassedOn([MovedFrom] ref Guard v) => Once(ref v);

            public static Guard ResetInFinally([MovedFrom] ref Guard v)
            {
                try
                {
                    return v;
                }
                finally
                {
                    v = default;
                }
            }
        }

        {{Sources.Guard}}
        """,
        Verify.MovedFromParameterReadAgain(0, "v"),
        Verify.MovedFromParameterReadAgain(1, "v"),
        Verify.MovedFromParameterReadAgain(2, "v"),
        Verify.MovedFromParameterReadAgain(3, "v"),
        Verify.MovedFromParameterReadAgain(4, "v"),
        Verify.MovedFromParameterNotAssigned(5, "Moves.Coalesce(ref List<Guard>)", "v"),
        Verify.MovedFromParameterNotAssignedAtOnce(6, "v", "a use of a field or an element that is not the method's own"),
        Verify.MovedFromParameterNotAssignedAtOnce(7, "v", "a call"));

    [TestMethod]
    public Task Move_AcceptsARefStructOnDotNet10() => Verify.AnalyzerAsync(
        """
        using Singulink.CodeAnalysis;

        class C
        {
            static void Consume(RefGuard guard) { }

            void M()
            {
                var guard = new RefGuard();
                Consume(NonCopyable.Move(ref guard));
                Consume({|#0:guard|});
            }
        }

        [NonCopyable]
        ref struct RefGuard { }
        """,
        Verify.CopyOfStoredValue(0, "'C.Consume(RefGuard)' receives", "RefGuard"));

    [TestMethod]
    public Task Move_MakesAMovedFromReferencePossiblyNull() => new Verify.Test {
        TestCode = $$"""
            #nullable enable
            #pragma warning disable CS1591
            using System.Threading.Tasks;
            using Singulink.CodeAnalysis;

            class C
            {
                async Task M(RwLock rwLock)
                {
                    var task = rwLock.EnterTaskAsync();
                    using var guard = await NonCopyable.Move(ref task);
                    bool completed = {|CS8602:task|}.IsCompleted;
                }
            }

            {{Sources.Guard}}
            """,
        CompilerDiagnostics = CompilerDiagnostics.Warnings,
    }.RunAsync();

    [TestMethod]
    public Task NonCopyableAttribute_StillBindsBesideTheStaticClassOfTheSameNamespace() => Verify.AnalyzerAsync("""
        using Singulink.CodeAnalysis;

        class C
        {
            void M()
            {
                var guard = new Guard();
                var copy = {|#0:guard|};
                var moved = NonCopyable.Move(ref {|#1:guard|});
                var movedByFullName = Singulink.CodeAnalysis.NonCopyable.Move(ref moved);
            }
        }

        [NonCopyable]
        struct Guard { }

        [NonCopyableAttribute]
        struct Other { }

        class Generic<[NonCopyable] T> { }
        """,
        Verify.UsedAfterHandOff(1, "guard", "Guard", "its value was assigned to \'copy\'", 0));

    [TestMethod]
    public Task MovedFrom_PassingOnByReferenceAssignsOnlyThroughOutOrMovedFrom() => Verify.AnalyzerAsync(
        $$"""
        using Singulink.CodeAnalysis;

        static class C
        {
            // A method that takes the parameter by 'ref' may leave it as it is, or leave anything else in it.
            static T PassesOn<[NonCopyable] T>([MovedFrom] ref T value)
            {
                T moved = {|#0:value|};
                {|#3:Keep(ref {|#1:value|})|};
                return moved;
            }

            static void Keep<[NonCopyable] T>(ref T value) { }

            // One that takes it as 'out' or as [MovedFrom] assigns it. After the read that still is a call: it can throw before it
            // assigns, and reach the variable in another way. In a generic move only another move leaves nothing else there.
            static T PassesToOut<[NonCopyable] T>([MovedFrom] ref T value)
            {
                T moved = value;
                {|#4:Reset(out {|#2:value|})|};
                return moved;
            }

            static Guard PassesAGuardToOut([MovedFrom] ref Guard value)
            {
                Guard moved = value;
                {|#5:Reset(out value)|};
                return moved;
            }

            static Guard PassesAGuardToAMove([MovedFrom] ref Guard value)
            {
                Guard moved = value;
                {|#6:TakeAGuard(ref value)|};
                return moved;
            }

            // Before any read it is the assignment itself, or the move.
            static void ResetsAGuard([MovedFrom] ref Guard value) => Reset(out value);

            static Guard TakeAGuard([MovedFrom] ref Guard value) => NonCopyable.Move(ref value);

            static void Reset<[NonCopyable] T>(out T value) => value = default!;

            static T PassesToMove<[NonCopyable] T>([MovedFrom] ref T value) => NonCopyable.Move(ref value);
        }

        {{Sources.Guard}}
        """,
        Verify.MovedFromParameterNotAssigned(0, "C.PassesOn<T>(ref T)", "value"),
        Verify.MovedFromContainerExposed(1, "value", "is passed by reference to 'C.Keep<T>(ref T)'"),
        Verify.MovedFromContainerExposed(2, "value", "is passed by reference to 'C.Reset<T>(out T)'"),
        Verify.MovedFromParameterNotAssignedAtOnce(3, "value", "a call"),
        Verify.MovedFromParameterNotAssignedAtOnce(4, "value", "a call"),
        Verify.MovedFromParameterNotAssignedAtOnce(5, "value", "a call"),
        Verify.MovedFromParameterNotAssignedAtOnce(6, "value", "a call"));

    [TestMethod]
    public Task MovedFrom_ParameterIsNotMadeToReferToSomethingElse() => Verify.AnalyzerAsync(
        $$"""
        using Singulink.CodeAnalysis;

        static class Helper
        {
            private static Guard s_other;

            // The assignment that follows would reset the other variable, and the caller's would keep the value that was moved out.
            public static Guard Move([MovedFrom] ref Guard value)
            {
                Guard moved = value;
                {|#0:value = ref s_other|};
                value = default;
                return moved;
            }

            public static Guard MoveOther(ref Guard value, [MovedFrom] ref Guard other)
            {
                value = ref other;
                Guard moved = other;
                other = default;
                return moved;
            }
        }

        {{Sources.Guard}}
        """,
        Verify.MovedFromParameterReassigned(0, "value"));

    [TestMethod]
    public Task GenericMove_IsCheckedToBeAPureMove() => Verify.AnalyzerAsync(
        $$"""
        using System.Runtime.CompilerServices;
        using System.Threading.Tasks;
        using Singulink.CodeAnalysis;

        static class Stash<[NonCopyable] T>
        {
            private static T s_slot = default!;

            public static void Put(T value) => s_slot = NonCopyable.Move(ref value);

            // With a task bound to T, the slot may hold a task that somebody put there without owning it.
            public static T Swap([MovedFrom] ref T value)
            {
                T old = NonCopyable.Move(ref s_slot);
                s_slot = NonCopyable.Move(ref value);
                return {|#0:old|};
            }

            public static T TakeAndRefill([MovedFrom] ref T value)
            {
                T refill = NonCopyable.Move(ref s_slot);
                T taken = value;
                value = {|#1:refill|};
                return taken;
            }

            // A method that returns something else on purpose says so. There is no mark for leaving something else behind.
            [return: Borrowed]
            public static T BorrowedSwap([MovedFrom] ref T value)
            {
                T old = NonCopyable.Move(ref s_slot);
                s_slot = NonCopyable.Move(ref value);
                return old;
            }

            [return: Borrowed]
            public static T BorrowedRefill([MovedFrom] ref T value)
            {
                T refill = NonCopyable.Move(ref s_slot);
                T taken = value;
                value = {|#2:refill|};
                return taken;
            }
        }

        static class Moves
        {
            // What a pure move hands out and leaves behind is 'default' or what it read from its [MovedFrom] parameters.
            public static T MyMove<[NonCopyable] T>([MovedFrom] ref T value)
            {
                T moved = value;
                value = default!;
                return moved;
            }

            public static T ThroughAnother<[NonCopyable] T>([MovedFrom] ref T value) => NonCopyable.Move(ref value);

            public static T ThroughALocal<[NonCopyable] T>([MovedFrom] ref T value)
            {
                T first = NonCopyable.Move(ref value);
                T second = NonCopyable.Move(ref first);
                return second;
            }

            public static void Swap<[NonCopyable] T>([MovedFrom] ref T first, [MovedFrom] ref T second)
            {
                T moved = first;
                first = second;
                second = NonCopyable.Move(ref moved);
            }

            public static (T First, T Second) TakeBoth<[NonCopyable] T>([MovedFrom] ref T first, [MovedFrom] ref T second)
            {
                T moved = NonCopyable.Move(ref second);
                return (NonCopyable.Move(ref first), NonCopyable.Move(ref moved));
            }

            public static T ThroughADeconstruction<[NonCopyable] T>([MovedFrom] ref T value)
            {
                (T moved, int number) = (value, 1);
                value = default!;
                return moved;
            }

            public static T OrDefault<[NonCopyable] T>([MovedFrom] ref T value, bool take)
            {
                T moved = value;
                value = default!;
                return take ? NonCopyable.Move(ref moved) : default!;
            }

            public static T ThroughAReadOnlyReference<[NonCopyable] T>([MovedFrom] ref T value)
            {
                T moved = value;
                value = default!;
                Unsafe.AsRef(in {|#10:value|}) = default!;
                return moved;
            }

            // A local that is written in another way is not followed.
            public static T Overwritten<[NonCopyable] T>([MovedFrom] ref T value)
            {
                T moved = NonCopyable.Move(ref value);
                Fill(out moved);
                return {|#3:moved|};
            }

            static void Fill<[NonCopyable] T>(out T value) => value = default!;

            // A method with another parameter or a receiver that carries the type parameter is no generic move, and neither is one
            // whose [MovedFrom] parameter has another type. It is not checked, and its callers do not trust it.
            public static T First<[NonCopyable] T>([MovedFrom] ref T[] values)
            {
                T first = NonCopyable.Move(ref values[0]);
                values = new T[1];
                return first;
            }

            public static T Exchange<[NonCopyable] T>([MovedFrom] ref T location, T value)
            {
                T old = location;
                location = NonCopyable.Move(ref value);
                return old;
            }
        }

        class C
        {
            Task<Guard> _pending = null!;
            Guard _guard;

            async Task M(RwLock rwLock)
            {
                var mine = rwLock.EnterTaskAsync();
                Guard a = await Moves.MyMove(ref mine);

                // A container that a [Borrowed] move hands out is stored, and what it may have left in the local is not known.
                var first = rwLock.EnterTaskAsync();
                Guard b = {|#4:await Stash<Task<Guard>>.BorrowedSwap(ref first)|};
                Guard c = {|#5:await NonCopyable.Move(ref {|#6:first|})|};

                // A value of a non-copyable type is new, as always.
                Guard d = Stash<Guard>.BorrowedSwap(ref _guard);

                // What is no pure move hands out a stored container, and can leave anything in the local.
                var second = rwLock.EnterTaskAsync();
                Guard e = {|#7:await Moves.Exchange(ref second, _pending)|};
                Guard f = {|#8:await NonCopyable.Move(ref {|#9:second|})|};

                var array = new[] { rwLock.EnterTaskAsync() };
                Guard i = {|#11:await Moves.First(ref array)|};

                // A swap of two locals that the method owns leaves both its own.
                var x = rwLock.EnterTaskAsync();
                var y = rwLock.EnterTaskAsync();
                Moves.Swap(ref x, ref y);
                Guard g = await NonCopyable.Move(ref x);
                Guard h = await NonCopyable.Move(ref y);
            }
        }

        {{Sources.Guard}}
        """,
        Verify.GenericMoveReturnsSomethingElse(0, "Stash<T>.Swap(ref T)", "T"),
        Verify.GenericMoveLeavesSomethingElse(1, "Stash<T>.TakeAndRefill(ref T)", "T", "value"),
        Verify.GenericMoveLeavesSomethingElse(2, "Stash<T>.BorrowedRefill(ref T)", "T", "value"),
        Verify.GenericMoveReturnsSomethingElse(3, "Moves.Overwritten<T>(ref T)", "T"),
        Verify.ReadFromStoredContainer(4, "'await'", "Guard", canBeMoved: false),
        Verify.ReadFromStoredContainer(5, "'await'", "Guard", canBeMoved: false),
        Verify.ContainerMovedFromUnowned(6, "System.Threading.Tasks.Task<Guard>", "first"),
        Verify.ReadFromStoredContainer(7, "'await'", "Guard", canBeMoved: false),
        Verify.ReadFromStoredContainer(8, "'await'", "Guard", canBeMoved: false),
        Verify.ContainerMovedFromUnowned(9, "System.Threading.Tasks.Task<Guard>", "second"),
        Verify.MovedFromContainerExposed(10, "value", "is passed by reference to 'Unsafe.AsRef<T>(scoped ref readonly T)'"),
        Verify.ReadFromStoredContainer(11, "'await'", "Guard", canBeMoved: false));

    [TestMethod]
    public Task MovedFrom_AContainerParameterIsLeftHoldingOnlyWhatTheCallerCanOwn() => Verify.AnalyzerAsync(
        $$"""
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Singulink.CodeAnalysis;

        static class Movers
        {
            private static readonly Task<Guard> s_completed = null!;

            static Task<Guard> Start() => null!;

            // The caller's local still counts as its own afterwards, and would hold a task that everyone shares.
            public static Task<Guard> LeavesAField([MovedFrom] ref Task<Guard> task)
            {
                var completed = s_completed;
                var taken = task;
                task = {|#0:completed|};
                return taken;
            }

            public static Task<Guard>? LeavesNull([MovedFrom] ref Task<Guard>? task)
            {
                var taken = task;
                task = null;
                return taken;
            }

            public static Task<Guard> LeavesANewOne([MovedFrom] ref Task<Guard> task)
            {
                var started = Start();
                var taken = task;
                task = started;
                return taken;
            }

            public static List<Guard> LeavesItsOwnLocal([MovedFrom] ref List<Guard> list)
            {
                var fresh = new List<Guard>();
                var taken = list;
                list = fresh;
                return taken;
            }

            // A local that is left there is the caller's from then on.
            public static List<Guard> LeavesAndReturnsIt([MovedFrom] ref List<Guard> list)
            {
                var fresh = new List<Guard>();
                list = fresh;
                return {|#1:fresh|};
            }

            public static Task<Guard>? PassesItOn([MovedFrom] ref Task<Guard>? task)
            {
                var completed = s_completed;
                var taken = task;
                Interlocked.Exchange(ref {|#2:task|}, completed);
                return taken;
            }

            public static Task<Guard>? TakesAReference([MovedFrom] ref Task<Guard>? task)
            {
                var completed = s_completed;
                var taken = task;
                ref var alias = ref {|#3:task|};
                alias = completed;
                return taken;
            }

            // A 'ref' local through which the method does nothing but reset the parameter is another name for it.
            public static Task<Guard>? ResetsThroughAReference([MovedFrom] ref Task<Guard>? task)
            {
                var taken = task;
                ref var alias = ref task;
                alias = null;
                return taken;
            }

            // 'Interlocked.Exchange' reads and assigns in one operation.
            public static Task<Guard>? ExchangesAfterTheRead([MovedFrom] ref Task<Guard>? task)
            {
                var taken = task;
                Interlocked.Exchange(ref task, null);
                return taken;
            }

            // A member on the safe list writes to a reference only what it is given.
            public static Task<Guard>? ExchangesForNull([MovedFrom] ref Task<Guard>? task)
            {
                var taken = Interlocked.Exchange(ref task, null);
                task = null;
                return taken;
            }

            public static Task<Guard> MovesItOn([MovedFrom] ref Task<Guard> task) => NonCopyable.Move(ref task);
        }

        {{Sources.Guard}}
        """,
        Verify.MovedFromContainerNotOwned(0, "task"),
        Verify.UnownedContainerReturned(1, "Movers.LeavesAndReturnsIt(ref List<Guard>)", "System.Collections.Generic.List<Guard>"),
        Verify.MovedFromContainerExposed(2, "task", "is passed by reference to 'Interlocked.Exchange<T>(ref T, T)'"),
        Verify.MovedFromContainerExposed(3, "task", "has a reference taken to it"));

    [TestMethod]
    public Task MovedFrom_AParameterThatGenericCodePassesToIsNotTheMethodsOwn() => Verify.AnalyzerAsync(
        $$"""
        using System.Threading.Tasks;
        using Singulink.CodeAnalysis;

        delegate void MoveAction<[NonCopyable] T>([MovedFrom] ref T value);

        delegate void TaskAction([MovedFrom] ref Task<Guard> value);

        interface IConsumer<[NonCopyable] T>
        {
            void Consume([MovedFrom] ref T value);
        }

        interface ITaskConsumer
        {
            void Consume([MovedFrom] ref Task<Guard> value);
        }

        // In the body of the generic a T is a value, so handing a slot to a [MovedFrom] parameter is a move. With a task bound, the
        // slot may hold a task that was added twice.
        sealed class Drainable<[NonCopyable] T>
        {
            private readonly T[] _items = new T[4];

            public void Add(T item) => _items[0] = NonCopyable.Move(ref item);

            public void Drain(MoveAction<T> action) => action(ref _items[0]);

            public void DrainTo(IConsumer<T> consumer) => consumer.Consume(ref _items[0]);
        }

        abstract class Flushing<[NonCopyable] T>
        {
            protected T _slot = default!;

            protected abstract void OnTake([MovedFrom] ref T value);

            public void Flush() => OnTake(ref _slot);
        }

        // A [MovedFrom] parameter of a container type is the method's own only where the member that its caller sees gives it the
        // container type itself.
        sealed class TaskConsumer : IConsumer<Task<Guard>>
        {
            public void Consume([MovedFrom] ref Task<Guard> value)
            {
                Task<Guard> mine = NonCopyable.Move(ref {|#0:value|});
            }
        }

        sealed class TaskFlushing : Flushing<Task<Guard>>
        {
            protected override void OnTake([MovedFrom] ref Task<Guard> value)
            {
                Task<Guard> mine = NonCopyable.Move(ref {|#1:value|});
            }
        }

        sealed class OwnConsumer : ITaskConsumer
        {
            public void Consume([MovedFrom] ref Task<Guard> value)
            {
                Task<Guard> mine = NonCopyable.Move(ref value);
            }
        }

        // Using the container in place is what a drain callback wants anyway.
        sealed class InPlaceConsumer : IConsumer<Task<Guard>>
        {
            public bool Completed;

            public void Consume([MovedFrom] ref Task<Guard> value)
            {
                Completed = value.IsCompleted;
                value = null!;
            }
        }

        // With a guard bound there is nothing to share.
        sealed class GuardConsumer : IConsumer<Guard>
        {
            private Guard _last;

            public void Consume([MovedFrom] ref Guard value) => _last = NonCopyable.Move(ref value);
        }

        // An inherited method that takes the parameter for its own cannot be used to implement the generic interface.
        class Handler
        {
            public void Consume([MovedFrom] ref Task<Guard> value)
            {
                Task<Guard> mine = NonCopyable.Move(ref value);
            }
        }

        sealed class {|#2:HandlerConsumer|} : Handler, IConsumer<Task<Guard>>
        {
        }

        class C
        {
            void M(Drainable<Task<Guard>> tasks, Drainable<Guard> guards)
            {
                tasks.Drain(([MovedFrom] ref Task<Guard> value) => { Task<Guard> mine = NonCopyable.Move(ref {|#3:value|}); });
                tasks.Drain(([MovedFrom] ref Task<Guard> value) => { bool done = value.IsCompleted; value = null!; });
                guards.Drain(([MovedFrom] ref Guard value) => { Guard mine = NonCopyable.Move(ref value); });

                // A method that is converted to several delegate types is the owner only if every one of them gives the container type.
                tasks.Drain(Moves);
                tasks.Drain(Reads);
                tasks.Drain(InPlace);
                TaskAction both = Moves;
                TaskAction own = Owns;

                // Nothing shows what is behind a delegate.
                tasks.Drain({|#6:own.Invoke|});
            }

            static void Moves([MovedFrom] ref Task<Guard> value)
            {
                Task<Guard> mine = NonCopyable.Move(ref {|#4:value|});
            }

            static void Reads([MovedFrom] ref Task<Guard> value)
            {
                Task<Guard> mine = {|#5:value|};
                value = null!;
            }

            static void InPlace([MovedFrom] ref Task<Guard> value)
            {
                bool done = value.IsCompleted;
                value = null!;
            }

            static void Owns([MovedFrom] ref Task<Guard> value)
            {
                Task<Guard> mine = NonCopyable.Move(ref value);
            }
        }

        {{Sources.Guard}}
        """,
        Verify.ContainerMovedFromSlotParameter(
            0, "System.Threading.Tasks.Task<Guard>", "value", "'IConsumer<T>.Consume(ref T)', which the method overrides or implements,"),
        Verify.ContainerMovedFromSlotParameter(
            1, "System.Threading.Tasks.Task<Guard>", "value", "'Flushing<T>.OnTake(ref T)', which the method overrides or implements,"),
        Verify.SlotGivenToOwner(2, "Handler.Consume(ref Task<Guard>)", "value", "'IConsumer<T>.Consume(ref T)', which 'HandlerConsumer' uses it to implement,"),
        Verify.ContainerMovedFromSlotParameter(3, "System.Threading.Tasks.Task<Guard>", "value", "delegate 'MoveAction<T>', which the lambda is converted to,"),
        Verify.ContainerMovedFromSlotParameter(4, "System.Threading.Tasks.Task<Guard>", "value", "delegate 'MoveAction<T>', which the method is converted to,"),
        Verify.ContainerMovedFromSlotParameter(5, "System.Threading.Tasks.Task<Guard>", "value", "delegate 'MoveAction<T>', which the method is converted to,"),
        Verify.SlotGivenToOwner(6, "TaskAction.Invoke(ref Task<Guard>)", "value", "delegate 'MoveAction<T>'"));

    [TestMethod]
    public Task MovedFrom_IsKeptInBothDirections() => Verify.AnalyzerAsync(
        $$"""
        using System.Threading.Tasks;
        using Singulink.CodeAnalysis;

        class Processor
        {
            public virtual void OnItem(ref Task<Guard> task) { }

            public virtual Task<Guard>? Take([MovedFrom] ref Task<Guard>? task)
            {
                var taken = task;
                task = null;
                return taken;
            }
        }

        // The mark is two promises: to the method, that its caller owned what it passed, and to the caller, that the method leaves only
        // what the caller may own. Adding it breaks the first, and dropping it the second.
        class Adding : Processor
        {
            public override void OnItem([MovedFrom] ref Task<Guard> {|#0:task|}) => task = null!;
        }

        class Dropping : Processor
        {
            public override Task<Guard>? Take(ref Task<Guard>? {|#1:task|}) => null;
        }

        class Keeping : Processor
        {
            public override void OnItem(ref Task<Guard> task) { }

            public override Task<Guard>? Take([MovedFrom] ref Task<Guard>? task) => base.Take(ref task);
        }

        interface IVisitor
        {
            void Visit(ref Task<Guard> task);

            void Take([MovedFrom] ref Task<Guard> task);

            static abstract T Move<[NonCopyable] T>([MovedFrom] ref T value);
        }

        class Visitor : IVisitor
        {
            public void Visit([MovedFrom] ref Task<Guard> {|#2:task|}) => task = null!;

            void IVisitor.Take(ref Task<Guard> {|#3:task|}) { }

            // Without the mark the body is never checked as a pure move, while a caller through the interface trusts it to be one.
            public static T Move<[NonCopyable] T>(ref T {|#4:value|}) => default!;
        }

        delegate void Plain(ref Task<Guard> task);

        delegate void Moving([MovedFrom] ref Task<Guard> task);

        class C
        {
            void M()
            {
                Plain a = {|#5:([MovedFrom] ref Task<Guard> task) => task = null!|};
                Moving b = {|#6:(ref Task<Guard> task) => { }|};
                Plain c = {|#7:Moves|};
                Moving d = {|#8:Uses|};
                Moving e = Moves;
                Plain f = Uses;
                Moving g = ([MovedFrom] ref Task<Guard> task) => task = null!;
            }

            // A function pointer type carries no marks.
            unsafe void Pointers()
            {
                delegate*<ref Task<Guard>, void> moving = {|#9:&Moves|};
                delegate*<ref Task<Guard>, void> plain = &Uses;
            }

            static void Moves([MovedFrom] ref Task<Guard> task) => task = null!;

            static void Uses(ref Task<Guard> task) { }
        }

        {{Sources.Guard}}
        """,
        Verify.MovedFromAdded(0, "task", "Adding.OnItem(ref Task<Guard>)", "'Processor.OnItem(ref Task<Guard>)', which the member overrides or implements,"),
        Verify.MovedFromDropped(1, "task", "Dropping.Take(ref Task<Guard>?)", "'Processor.Take(ref Task<Guard>?)', which the member overrides or implements,"),
        Verify.MovedFromAdded(2, "task", "Visitor.Visit(ref Task<Guard>)", "'IVisitor.Visit(ref Task<Guard>)', which the member overrides or implements,"),
        Verify.MovedFromDropped(3, "task", "Visitor.IVisitor.Take(ref Task<Guard>)", "'IVisitor.Take(ref Task<Guard>)', which the member overrides or implements,"),
        Verify.MovedFromDropped(4, "value", "Visitor.Move<T>(ref T)", "'IVisitor.Move<T>(ref T)', which the member overrides or implements,"),
        Verify.MovedFromAdded(5, "task", "lambda expression", "delegate 'Plain', which it is converted to,"),
        Verify.MovedFromDropped(6, "task", "lambda expression", "delegate 'Moving', which it is converted to,"),
        Verify.MovedFromAdded(7, "task", "C.Moves(ref Task<Guard>)", "delegate 'Plain', which it is converted to,"),
        Verify.MovedFromDropped(8, "task", "C.Uses(ref Task<Guard>)", "delegate 'Moving', which it is converted to,"),
        Verify.MovedFromAdded(9, "task", "C.Moves(ref Task<Guard>)", "a function pointer type, which carries no marks and which it is converted to,"));

    [TestMethod]
    public Task Move_HandsBackByValue() => Verify.AnalyzerAsync(
        $$"""
        using System.Threading.Tasks;
        using Singulink.CodeAnalysis;

        ref struct Holder { }

        static class Moves
        {
            // A method that returns a reference can return one to the caller's variable, through which the caller writes anything.
            public static ref T Reset<[NonCopyable] T>([MovedFrom] ref T value)
            {
                value = default!;
                return ref value;
            }

            public static ref Task<Guard> ResetTask([MovedFrom] ref Task<Guard> value)
            {
                value = null!;
                return ref value;
            }

            public static ref readonly T Peek<[NonCopyable] T>([MovedFrom] ref T value)
            {
                value = default!;
                return ref value;
            }

            public static Holder Hold([MovedFrom] ref Task<Guard> value)
            {
                value = null!;
                return default;
            }
        }

        class C
        {
            Task<Guard> _stored = null!;

            async Task M(RwLock rwLock)
            {
                // So such a method is no pure move, and a local that is passed to it is no longer the caller's own.
                var a = rwLock.EnterTaskAsync();
                Moves.Reset(ref a) = _stored;
                Guard g1 = {|#0:await NonCopyable.Move(ref {|#1:a|})|};

                var b = rwLock.EnterTaskAsync();
                Moves.ResetTask(ref b) = _stored;
                Guard g2 = {|#2:await NonCopyable.Move(ref {|#3:b|})|};

                var c = rwLock.EnterTaskAsync();
                Moves.Peek(ref c);
                Guard g3 = {|#4:await NonCopyable.Move(ref {|#5:c|})|};

                var d = rwLock.EnterTaskAsync();
                Moves.Hold(ref d);
                Guard g4 = {|#6:await NonCopyable.Move(ref {|#7:d|})|};

                var e = rwLock.EnterTaskAsync();
                Guard g5 = await NonCopyable.Move(ref e);
            }
        }

        {{Sources.Guard}}
        """,
        Verify.ReadFromStoredContainer(0, "'await'", "Guard", canBeMoved: false),
        Verify.ContainerMovedFromUnowned(1, "System.Threading.Tasks.Task<Guard>", "a"),
        Verify.ReadFromStoredContainer(2, "'await'", "Guard", canBeMoved: false),
        Verify.ContainerMovedFromUnowned(3, "System.Threading.Tasks.Task<Guard>", "b"),
        Verify.ReadFromStoredContainer(4, "'await'", "Guard", canBeMoved: false),
        Verify.ContainerMovedFromUnowned(5, "System.Threading.Tasks.Task<Guard>", "c"),
        Verify.ReadFromStoredContainer(6, "'await'", "Guard", canBeMoved: false),
        Verify.ContainerMovedFromUnowned(7, "System.Threading.Tasks.Task<Guard>", "d"));

    [TestMethod]
    public Task MovedFrom_NothingRunsBetweenTheReadAndTheAssignment() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using System.Collections.Generic;
        using System.Runtime.CompilerServices;
        using System.Threading;
        using System.Threading.Tasks;
        using Singulink.CodeAnalysis;

        static class Moves
        {
            static Guard s_slot;

            static Guard Create() => default;
            static void Log() { }

            // The forms that a move is written in.
            static void HandOver([MovedFrom] ref Guard a, ref Guard b)
            {
                b = a;
                a = default;
            }

            static Guard Helper([MovedFrom] ref Guard value)
            {
                Guard moved = value;
                value = default;
                return moved;
            }

            static T Generic<[NonCopyable] T>([MovedFrom] ref T value)
            {
                T moved = value;
                value = default!;
                return moved;
            }

            static Guard PassedOn([MovedFrom] ref Guard value) => NonCopyable.Move(ref value);

            static void IntoAField([MovedFrom] ref Guard value)
            {
                s_slot = value;
                value = default;
            }

            // A call can throw, which leaves the value in two places.
            static void Replaced([MovedFrom] ref Guard a, ref Guard b)
            {
                b = a;
                a = {|#0:Create()|};
            }

            // What can throw is done first, into a local.
            static void ReplacedSafely([MovedFrom] ref Guard a, ref Guard b)
            {
                Guard fresh = Create();
                b = a;
                a = fresh;
            }

            static Guard ReplacedAfterAReadIntoALocal([MovedFrom] ref Guard value)
            {
                Guard moved = value;
                value = {|#1:Create()|};
                return moved;
            }

            static void Added([MovedFrom] ref Guard value, List<Guard> list)
            {
                {|#2:list.Add(value)|};
                value = default;
            }

            static Guard Logged([MovedFrom] ref Guard value)
            {
                Guard moved = value;
                {|#3:Log()|};
                value = default;
                return moved;
            }

            // A write through another reference fails for a null reference, and a read through one can read the same variable.
            static void WritesAnother([MovedFrom] ref Guard a, ref Guard b, ref Guard c)
            {
                b = a;
                {|#4:c|} = default;
                a = default;
            }

            // That goes for a value of any type, through any kind of reference, and for a field of the struct that one refers to.
            static Guard CountsThroughAReference([MovedFrom] ref Guard value, ref int count)
            {
                Guard moved = value;
                {|#17:count|} = 1;
                value = default;
                return moved;
            }

            static Guard ReadsThroughAReference([MovedFrom] ref Guard value, in int step)
            {
                Guard moved = value;
                int next = {|#18:step|} + 1;
                value = default;
                return moved;
            }

            static Guard WritesAFieldThroughAReference([MovedFrom] ref Guard value, ref (int Reads, int Writes) totals)
            {
                Guard moved = value;
                {|#19:totals|}.Reads = 1;
                value = default;
                return moved;
            }

            static Guard WritesThroughALocal([MovedFrom] ref Guard value, int[] numbers)
            {
                ref int first = ref numbers[0];
                Guard moved = value;
                {|#20:first|} = 1;
                value = default;
                return moved;
            }

            // What can fail is done first.
            static Guard CountsFirst([MovedFrom] ref Guard value, ref int count)
            {
                count = 1;
                Guard moved = value;
                value = default;
                return moved;
            }

            // A call for the new value, and a field of an object, which can be null.
            static void AssignsACall([MovedFrom] ref Guard a, ref Guard b)
            {
                b = a;
                a = {|#21:Create()|};
            }

            static void WritesAFieldOfAnObject([MovedFrom] ref Guard a, ref Guard b, Counter other)
            {
                b = a;
                {|#13:other.Count|} = 1;
                a = default;
            }

            static void WritesAFieldThroughAReferenceToAnObject([MovedFrom] ref Guard a, ref Guard b, ref Counter other)
            {
                b = a;
                {|#14:other|}.Count = 1;
                a = default;
            }

            // A by-value read of a guard through another reference is still a copy, and it can read the very variable that the
            // parameter refers to.
            static Guard CopiesThroughAnotherReference([MovedFrom] ref Guard a, ref Guard b)
            {
                Guard moved = a;
                Guard copy = {|#15:{|#16:b|}|};
                a = default;
                copy.Dispose();
                return moved;
            }

            public sealed class Counter
            {
                public int Count;
            }

            static T WritesANullReference<[NonCopyable] T>([MovedFrom] ref T value)
            {
                T moved = value;
                {|#5:Unsafe.NullRef<T>()|} = default!;
                value = default!;
                return moved;
            }

            static Guard ReadsAField([MovedFrom] ref Guard value)
            {
                Guard moved = value;
                value = {|#6:{|#7:s_slot|}|};
                return moved;
            }

            // The paths: one that leaves without the assignment, by a return or by an exception, and one that comes back to the read.
            static Guard AssignedInOneBranch([MovedFrom] ref Guard value, bool condition)
            {
                Guard moved = {|#8:value|};

                if (condition)
                    value = default;

                return moved;
            }

            static Guard Thrown([MovedFrom] ref Guard value, Exception exception)
            {
                Guard moved = {|#9:value|};

                if (moved.Value == 0)
                    throw exception;

                value = default;
                return moved;
            }

            static Guard BackwardGoto([MovedFrom] ref Guard value)
            {
                Guard moved;

                again:
                moved = {|#10:value|};

                if (moved.Value == 0)
                    goto again;

                value = default;
                return moved;
            }

            static Guard AssignedInFinally([MovedFrom] ref Guard value)
            {
                try
                {
                    return value;
                }
                finally
                {
                    value = default;
                }
            }

            static Guard AssignedEveryTime([MovedFrom] ref Guard value)
            {
                Guard last = default;

                for (int i = 0; i < 2; i++)
                {
                    last = value;
                    value = default;
                }

                return last;
            }

            // The reset through a 'ref' local that is another name for the parameter, and one that reads and assigns in one operation.
            static T ThroughARefLocal<[NonCopyable] T>([MovedFrom] ref T value)
            {
                T moved = value;
                ref T alias = ref value;
                alias = default!;
                return moved;
            }

            static Task<Guard>? Exchanged([MovedFrom] ref Task<Guard>? task)
            {
                var taken = task;
                Interlocked.Exchange(ref task, null);
                return taken;
            }

            // A test of the local that holds the value runs nothing.
            static Guard TestedFirst([MovedFrom] ref Guard? value)
            {
                Guard? moved = value;

                if (moved is null or { Value: 0 })
                {
                    value = null;
                    return default;
                }

                value = default(Guard);
                return moved.GetValueOrDefault();
            }

            // Reading out of a container, or a part of a value, takes what the parameter holds as well.
            static Guard TakesTheResult([MovedFrom] ref Task<Guard> task) => {|#11:task|}.Result;

            static Guard TakesTheResultAndResets([MovedFrom] ref Task<Guard>? task)
            {
                Guard taken = task!.Result;
                task = null;
                return taken;
            }

            static Guard TakesAPart([MovedFrom] ref (Guard guard, int number) pair) => {|#12:pair|}.guard;
        }

        {{Sources.Guard}}
        """,
        Verify.MovedFromParameterNotAssignedAtOnce(0, "a", "a call"),
        Verify.MovedFromParameterNotAssignedAtOnce(1, "value", "a call"),
        Verify.MovedFromParameterNotAssignedAtOnce(2, "value", "a call"),
        Verify.MovedFromParameterNotAssignedAtOnce(3, "value", "a call"),
        Verify.MovedFromParameterNotAssignedAtOnceThroughReference(4, "a", "c"),
        Verify.MovedFromParameterNotAssignedAtOnce(5, "value", "a call"),
        Verify.MovedFromParameterNotAssignedAtOnce(13, "a", "a use of a field or an element that is not the method's own"),
        Verify.MovedFromParameterNotAssignedAtOnceThroughReference(14, "a", "other"),
        Verify.CopyByAssignment(15, "Guard"),
        Verify.MovedFromParameterNotAssignedAtOnce(16, "a", "a read or a write through another reference"),
        Verify.MovedFromParameterNotAssignedAtOnceThroughReference(17, "value", "count"),
        Verify.MovedFromParameterNotAssignedAtOnceThroughReference(18, "value", "step"),
        Verify.MovedFromParameterNotAssignedAtOnceThroughReference(19, "value", "totals"),
        Verify.MovedFromParameterNotAssignedAtOnceThroughReference(20, "value", "first"),
        Verify.MovedFromParameterNotAssignedAtOnce(21, "a", "a call"),
        Verify.CopyByAssignment(6, "Guard"),
        Verify.MovedFromParameterNotAssignedAtOnce(7, "value", "a use of a field or an element that is not the method's own"),
        Verify.MovedFromParameterNotAssigned(8, "Moves.AssignedInOneBranch(ref Guard, bool)", "value"),
        Verify.MovedFromParameterNotAssigned(9, "Moves.Thrown(ref Guard, Exception)", "value"),
        Verify.MovedFromParameterReadAgain(10, "value"),
        Verify.MovedFromParameterNotAssigned(11, "Moves.TakesTheResult(ref Task<Guard>)", "task"),
        Verify.MovedFromParameterNotAssigned(12, "Moves.TakesAPart(ref (Guard guard, int number))", "pair"));

    // A reference can be null. If the write through 'c' fails, the caller is in its catch block with the value in 'a' and in 'b'.
    [TestMethod]
    public Task MovedFrom_AWriteThroughAReferenceBetweenTheReadAndTheAssignmentIsReported() => Verify.AnalyzerAsync(
        $$"""
        using Singulink.CodeAnalysis;

        static class Moves
        {
            static void WritesBetween([MovedFrom] ref Guard a, [MovedFrom] ref Guard b, ref Guard c)
            {
                b = a;
                {|#0:c|} = default;
                a = default;
            }

            // With the write that can fail done first, nothing runs between the read and the assignment.
            static void WritesFirst([MovedFrom] ref Guard a, [MovedFrom] ref Guard b, ref Guard c)
            {
                c = default;
                b = a;
                a = default;
            }
        }

        {{Sources.Guard}}
        """,
        Verify.MovedFromParameterNotAssignedAtOnceThroughReference(0, "a", "c"));

    [TestMethod]
    public Task MovedFrom_TwoParametersCanReferToOneVariable() => Verify.AnalyzerAsync(
        $$"""
        using Singulink.CodeAnalysis;

        static class Moves
        {
            // Marking the other parameters changes nothing: the attribute says nothing about what a reference refers to.
            static void WritesAnother([MovedFrom] ref Guard a, [MovedFrom] ref Guard b, [MovedFrom] ref Guard c)
            {
                b = a;
                {|#0:c|} = default;
                a = default;
            }

            // If 'a' and 'b' refer to one variable, both locals hold its value. The write through 'a' after the read of 'b' is another
            // matter: a read into a local is no proof that 'a' refers to something, so the write counts as able to fail.
            static (Guard, Guard) TakesBoth([MovedFrom] ref Guard a, [MovedFrom] ref Guard b)
            {
                Guard x = a;
                Guard y = {|#1:b|};
                {|#8:a|} = default;
                b = default;
                return (x, y);
            }

            // The read of 'b' follows the read of 'a' inside one statement. A read into a tuple that is made is no proof of 'a',
            // so the store through 'a' after the read of 'b' counts as able to fail.
            static (Guard, Guard) TakesBothAtOnce([MovedFrom] ref Guard a, [MovedFrom] ref Guard b)
            {
                var both = (a, {|#3:b|});
                ({|#7:a|}, b) = (default, default);
                return both;
            }

            static (Guard, Guard) TakesOneAndMovesTheOther([MovedFrom] ref Guard a, ref Guard b)
            {
                var both = (a, NonCopyable.Move(ref {|#5:b|}));
                a = default;
                return both;
            }

            // A swap reads the other parameter only to assign it to the one that was read: if both are one variable, that changes nothing.
            static void Swap([MovedFrom] ref Guard a, [MovedFrom] ref Guard b)
            {
                Guard held = a;
                a = b;
                b = NonCopyable.Move(ref held);
            }

            static void SwapByTheLastUse([MovedFrom] ref Guard a, [MovedFrom] ref Guard b)
            {
                Guard held = a;
                a = b;
                b = held;
            }

            // Once the value is stored through a reference, it stays there if the read of the other parameter fails.
            static void Rotate([MovedFrom] ref Guard a, [MovedFrom] ref Guard b, ref Guard c)
            {
                c = a;
                a = {|#6:b|};
                b = default;
            }
        }

        {{Sources.Guard}}
        """,
        Verify.MovedFromParameterNotAssignedAtOnceThroughReference(0, "a", "c"),
        Verify.MovedFromParameterNotAssignedAtOnce(1, "a", "a read or a write through another reference"),
        Verify.MovedFromParameterNotAssignedAtOnceThroughReference(8, "b", "a"),
        Verify.MovedFromParameterNotAssignedAtOnce(3, "a", "a read or a write through another reference"),
        Verify.MovedFromParameterNotAssignedAtOnceThroughReference(7, "b", "a"),
        Verify.MovedFromParameterNotAssignedAtOnceThroughReference(5, "a", "b"),
        Verify.MovedFromParameterNotAssignedAtOnce(6, "a", "a read or a write through another reference"));

    [TestMethod]
    public Task MovedFrom_WhatIsTakenForALocalReadOrWriteRunsNoOtherCode() => Verify.AnalyzerAsync(
        $$"""
        using Singulink.CodeAnalysis;

        struct Small
        {
            public static implicit operator Big(Small small) => default;
        }

        struct Big { }

        class Holder
        {
            public Guard Field;
            public int Number;

            public Guard Kept { set => Field = value; }
        }

        static class Moves
        {
            // The concatenation calls 'ToString' on the object.
            static Guard Concatenated([MovedFrom] ref Guard value, object other)
            {
                Guard moved = value;
                string text = {|#0:"x" + other|};
                value = default;
                return moved;
            }

            // The concatenation of two strings throws where the result would be longer than a string can be.
            static Guard ConcatenatedStrings([MovedFrom] ref Guard value, string other)
            {
                Guard moved = value;
                string text = {|#14:"x" + other|};
                value = default;
                return moved;
            }

            static Guard AppendedTo([MovedFrom] ref Guard value, object other)
            {
                Guard moved = value;
                string text = "x";
                {|#1:text += other|};
                value = default;
                return moved;
            }

            // The conversion of a tuple converts its elements, here with a user-defined conversion.
            static Guard TupleConverted([MovedFrom] ref Guard value, (Small, int) pair)
            {
                Guard moved = value;
                (Big, long) converted = {|#2:pair|};
                value = default;
                return moved;
            }

            static Guard TupleRenamed([MovedFrom] ref Guard value, (int, int) pair)
            {
                Guard moved = value;
                (int first, int second) named = pair;
                value = default;
                return moved;
            }

            // A conversion from 'dynamic' runs the binder.
            static Guard ConvertedFromDynamic([MovedFrom] ref Guard value, dynamic other)
            {
                Guard moved = value;
                int number = {|#3:other|};
                value = default;
                return moved;
            }

            // A positional pattern reads the elements of a tuple, and calls the members of 'ITuple' on anything else.
            static Guard MatchedByPosition([MovedFrom] ref Guard value, object other)
            {
                Guard moved = value;
                bool isPair = other is {|#4:(1, 2)|};
                value = default;
                return moved;
            }

            static Guard TupleMatchedByPosition([MovedFrom] ref Guard value, (int, int) pair)
            {
                Guard moved = value;
                bool isPair = pair is (1, 2);
                value = default;
                return moved;
            }

            // A setter is a call. A field is a plain store.
            static void StoredThroughASetter([MovedFrom] ref Guard value, Holder holder)
            {
                {|#5:holder.Kept = value|};
                value = default;
            }

            static void StoredInAField([MovedFrom] ref Guard value, Holder holder)
            {
                holder.Field = value;
                value = default;
            }
        }

        // A read or a plain store of an instance field of 'this' cannot throw: 'this' is never null in a class.
        class Keeper
        {
            static int s_count;

            Guard _kept;
            Guard _other;
            int _state;
            Holder _holder = new();

            void Keep([MovedFrom] ref Guard value)
            {
                _kept = value;
                _other = default;
                _state = _state + 1;
                value = default;
            }

            // The parameter can refer to that very field, so a by-value read of a guard out of a field takes the value a second
            // time. It is the copy of a stored value that it is anywhere.
            Guard CopiesAField([MovedFrom] ref Guard value)
            {
                Guard moved = value;
                Guard copy = {|#6:_other|};
                value = default;
                copy.Dispose();
                return moved;
            }

            // A static field can run the initializer of its type, and a field of another object can be null.
            void CountsInAStaticField([MovedFrom] ref Guard value)
            {
                _kept = value;
                {|#7:s_count|} = 1;
                value = default;
            }

            void WritesAFieldOfAnotherObject([MovedFrom] ref Guard value)
            {
                _kept = value;
                {|#8:_holder.Number|} = 1;
                value = default;
            }
        }

        // In a struct 'this' is a reference, and a reference can be null: a field of it counts as able to throw, as a read or a write
        // through any other reference does.
        [NonCopyable]
        struct KeeperStruct
        {
            static int s_count;

            Guard _kept;
            Guard _other;
            int _state;
            Holder _holder;

            // The store of the value is a store through 'this', which shows that 'this' refers to something.
            void Keep([MovedFrom] ref Guard value)
            {
                _kept = value;
                _other = default;
                _state = _state + 1;
                value = default;
            }

            // With the value in a local nothing has shown it yet.
            Guard KeepsInALocal([MovedFrom] ref Guard value)
            {
                Guard moved = value;
                {|#12:_other|} = default;
                value = default;
                return moved;
            }

            Guard CopiesAField([MovedFrom] ref Guard value)
            {
                Guard moved = value;
                Guard copy = {|#9:{|#13:_other|}|};
                value = default;
                copy.Dispose();
                return moved;
            }

            // What can fail is done first.
            void KeepsLast([MovedFrom] ref Guard value)
            {
                _other = default;
                _state = _state + 1;
                _kept = value;
                value = default;
            }

            void CountsInAStaticField([MovedFrom] ref Guard value)
            {
                _kept = value;
                {|#10:s_count|} = 1;
                value = default;
            }

            // The read of the field of 'this' cannot fail after the store through 'this'. The object that it holds can be null.
            void WritesAFieldOfAnotherObject([MovedFrom] ref Guard value)
            {
                _kept = value;
                {|#11:_holder.Number|} = 1;
                value = default;
            }
        }

        static class MoreMoves
        {

            // Simple values are compared and counted with no other code.
            static Guard Counted([MovedFrom] ref Guard value, int count, object first, object second)
            {
                Guard moved = value;
                bool isLast = count + 1 == 2 && first == second && first != null;
                count++;
                value = default;
                return moved;
            }
        }

        {{Sources.Guard}}
        """,
        Verify.MovedFromParameterNotAssignedAtOnce(0, "value", "a call"),
        Verify.MovedFromParameterNotAssignedAtOnce(1, "value", "a call"),
        Verify.MovedFromParameterNotAssignedAtOnce(2, "value", "a call"),
        Verify.MovedFromParameterNotAssignedAtOnce(3, "value", "a call"),
        Verify.MovedFromParameterNotAssignedAtOnce(4, "value", "a call"),
        Verify.MovedFromParameterNotAssignedAtOnce(5, "value", "a call"),
        Verify.CopyByAssignment(6, "Guard"),
        Verify.MovedFromParameterNotAssignedAtOnce(7, "value", "a use of a field or an element that is not the method's own"),
        Verify.MovedFromParameterNotAssignedAtOnce(8, "value", "a use of a field or an element that is not the method's own"),
        Verify.MovedFromParameterNotAssignedAtOnceThroughReference(12, "value", "this"),
        Verify.CopyByAssignment(9, "Guard"),
        Verify.MovedFromParameterNotAssignedAtOnce(13, "value", "a use of a field or an element that is not the method's own"),
        Verify.MovedFromParameterNotAssignedAtOnce(10, "value", "a use of a field or an element that is not the method's own"),
        Verify.MovedFromParameterNotAssignedAtOnce(11, "value", "a use of a field or an element that is not the method's own"),
        Verify.MovedFromParameterNotAssignedAtOnce(14, "value", "an operation that can throw or run other code"));

    [TestMethod]
    public Task MovedFrom_ExchangeAfterAReadAndAReadInAFilter() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Singulink.CodeAnalysis;

        static class Moves
        {
            static void Fail() => throw new Exception();

            // After a read, 'Interlocked.Exchange' is the assignment only where its result is discarded: otherwise it hands out the
            // value a second time.
            static Task<Guard>? ExchangedForASecondCopy([MovedFrom] ref Task<Guard>? task)
            {
                var taken = task;
                var again = Interlocked.Exchange(ref {|#0:task|}, null);
                return taken;
            }

            static Task<Guard>? ExchangedAndDiscarded([MovedFrom] ref Task<Guard>? task)
            {
                var taken = task;
                _ = Interlocked.Exchange(ref task, null);
                return taken;
            }

            // Before any read it is the read and the assignment in one.
            static Task<Guard>? ExchangedFirst([MovedFrom] ref Task<Guard>? task) => Interlocked.Exchange(ref task, null);

            // A filter that gives false lets the exception go on to the next clause, which returns with the parameter as it was.
            static Guard ReadInAFilter([MovedFrom] ref Guard value, bool condition)
            {
                Guard moved = default;

                try
                {
                    Fail();
                }
                catch when ((moved = {|#1:value|}) is { } && condition)
                {
                    value = default;
                }
                catch
                {
                }

                return moved;
            }
        }

        {{Sources.Guard}}
        """,
        Verify.MovedFromParameterReadAgain(0, "task"),
        Verify.MovedFromParameterNotAssigned(1, "Moves.ReadInAFilter(ref Guard, bool)", "value"));

    [TestMethod]
    public Task MovedFrom_OnlyTheExchangeOfTheFrameworkCountsAndADeconstructionStoresOneByOne() => Verify.AnalyzerAsync(
        $$"""
        using System.Collections.Generic;
        using Singulink.CodeAnalysis;

        // A class of this name in another namespace runs any code.
        static class Interlocked
        {
            public static Guard Exchange(ref Guard location, Guard value) => value;
        }

        class Stats
        {
            public int Count;
        }

        static class Moves
        {
            static Guard s_held;

            static void ByAClassOfTheSameName([MovedFrom] ref Guard slot, ref Guard old)
            {
                old = {|#4:slot|};
                {|#0:Interlocked.Exchange(ref slot, default)|};
            }

            // For a struct that is no primitive the generic form throws.
            static void OfAStruct([MovedFrom] ref Guard slot, ref Guard old)
            {
                old = {|#1:slot|};
                {|#2:System.Threading.Interlocked.Exchange(ref slot, default)|};
            }

            // The stores of a deconstruction are made one after the other: the second fails for a null object, and the first is done.
            static void TwoStores([MovedFrom] ref Guard slot, Stats stats)
            {
                {|#3:(s_held, stats.Count) = (slot, 1)|};
                slot = default;
            }

            static void OneStore([MovedFrom] ref Guard slot)
            {
                int count;
                (s_held, count) = (slot, 1);
                slot = default;
            }

            // A store through a reference fails for a null reference, so two of them are two stores that can fail.
            static void TwoStoresThroughReferences([MovedFrom] ref Guard slot, ref Guard old, ref int count)
            {
                {|#5:(old, count) = (slot, 1)|};
                slot = default;
            }

            static void OneStoreThroughAReference([MovedFrom] ref Guard slot, ref Guard old)
            {
                int count;
                (old, count) = (slot, 1);
                slot = default;
            }
        }

        // A store into a field of 'this' cannot fail in a class. In a struct 'this' is a reference.
        class Keeper
        {
            Guard _held;
            int _count;

            void Keep([MovedFrom] ref Guard slot)
            {
                (_held, _count) = (slot, 1);
                slot = default;
            }
        }

        [NonCopyable]
        struct KeeperStruct
        {
            Guard _held;
            int _count;

            void Keep([MovedFrom] ref Guard slot)
            {
                {|#6:(_held, _count) = (slot, 1)|};
                slot = default;
            }
        }

        {{Sources.Guard}}
        """,
        Verify.MovedFromParameterNotAssignedAtOnce(0, "slot", "a call"),
        Verify.MovedFromParameterNotAssigned(1, "Moves.OfAStruct(ref Guard, ref Guard)", "slot"),
        Verify.MovedFromParameterNotAssignedAtOnce(2, "slot", "a call"),
        Verify.MovedFromParameterNotAssignedAtOnce(3, "slot", "an operation that can throw or run other code"),
        Verify.MovedFromParameterNotAssigned(4, "Moves.ByAClassOfTheSameName(ref Guard, ref Guard)", "slot"),
        Verify.MovedFromParameterNotAssignedAtOnceThroughReference(5, "slot", "count"),
        Verify.MovedFromParameterNotAssignedAtOnceThroughReference(6, "slot", "this"));

    // The receiver of an extension block is a parameter of every member of the block.
    [TestMethod]
    public Task MovedFrom_OnTheReceiverOfAnExtensionBlockIsCheckedInEveryMember() => Verify.AnalyzerAsync(
        $$"""
        using Singulink.CodeAnalysis;

        sealed class Holder
        {
            public Guard Held;

            public void Notify() { }
        }

        static class Extensions
        {
            extension([MovedFrom] ref Guard guard)
            {
                public Guard Take() => {|#0:guard|};

                public Guard Taken => {|#1:guard|};

                public void HandOver(Holder holder)
                {
                    Guard taken = guard;
                    {|#2:holder.Notify()|};
                    guard = default;
                    holder.Held = taken;
                }

                // The members that reset the receiver are fine.
                public Guard Release()
                {
                    Guard taken = guard;
                    guard = default;
                    return taken;
                }

                public Guard Move() => NonCopyable.Move(ref guard);

                public Guard Moved => NonCopyable.Move(ref guard);
            }

            // A generic block is no different.
            extension<[NonCopyable] T>([MovedFrom] ref T value)
                where T : struct
            {
                public T TakeValue() => {|#3:value|};

                public T ReleaseValue()
                {
                    T taken = value;
                    value = default;
                    return taken;
                }
            }

            // The mark on a receiver that is not a 'ref' parameter says nothing, as on any other parameter.
            extension([MovedFrom] Guard {|#4:byValue|})
            {
                public void UseByValue() { }

                public int NumberByValue => 1;
            }

            extension([MovedFrom] in Guard {|#5:byReadOnlyReference|})
            {
                public void UseByReadOnlyReference() { }
            }
        }

        {{Sources.Guard}}
        """,
        Verify.MovedFromParameterNotAssigned(0, "Extensions.extension(ref Guard).Take()", "guard"),
        Verify.MovedFromParameterNotAssigned(1, "Extensions.extension(ref Guard).Taken.get", "guard"),
        Verify.MovedFromParameterNotAssignedAtOnce(2, "guard", "a call"),
        Verify.MovedFromParameterNotAssigned(3, "Extensions.extension<T>(ref T).TakeValue()", "value"),
        Verify.MovedFromNotOnRefParameter(4),
        Verify.MovedFromNotOnRefParameter(5));

    // An assignment stores when it runs, wherever its target stands: the outer target is evaluated before the read and written after it.
    [TestMethod]
    public Task MovedFrom_AnOuterAssignmentStoresAfterTheStoreThatIsItsValue() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using Singulink.CodeAnalysis;

        class Node
        {
            public IDisposable? Owner;
            public bool Flag;
        }

        class Keeper
        {
            static bool s_fresh;

            IDisposable? _current;
            Guard _held;
            bool _fresh;

            // A null 'node' fails with the box in '_current' and the guard still in 'value'.
            void IntoAFieldOfAnObject([MovedFrom] ref Guard value, Node node)
            {
                {|#0:node.Owner = _current = value|};
                value = default;
            }

            void IntoAnElement([MovedFrom] ref Guard value, IDisposable?[] owners, int index)
            {
                {|#1:owners[index] = _current = value|};
                value = default;
            }

            void ThroughAReference([MovedFrom] ref Guard value, ref bool fresh)
            {
                {|#2:fresh = (_held = value) is { Value: 0 }|};
                value = default;
            }

            void IntoAStaticField([MovedFrom] ref Guard value)
            {
                {|#3:s_fresh = (_held = value) is { Value: 0 }|};
                value = default;
            }

            // The target is captured before the store where the value has a conditional in it.
            void ThroughACapturedReference([MovedFrom] ref Guard value, ref bool fresh, bool flag)
            {
                {|#4:fresh = flag ? (_held = value) is { Value: 0 } : (_held = value) is { Value: 1 }|};
                value = default;
            }

            void ByADeconstruction([MovedFrom] ref Guard value, Node node)
            {
                int n;
                {|#5:(node.Flag, n) = ((_held = value) is { Value: 0 }, 1)|};
                value = default;
            }

            // A target that takes no part in the store of the value is judged like any other, once the value has been stored. Before
            // that, a failure leaves every value where it was.
            void BesideTheStore([MovedFrom] ref Guard value, ref Guard target, ref int count)
            {
                {|#6:(target, count) = (value, 1)|};
                value = default;
            }

            void BeforeTheStore([MovedFrom] ref Guard value, ref Guard target, ref int count)
            {
                (count, target) = (1, value);
                value = default;
            }

            // A local and a field of 'this' in a class cannot fail, and the one store of the value that was read may be anything.
            void IntoALocal([MovedFrom] ref Guard value, bool flag)
            {
                bool fresh;
                fresh = (_held = value) is { Value: 0 };
                fresh = flag ? fresh : !fresh;
                value = default;
            }

            void IntoAFieldOfThis([MovedFrom] ref Guard value)
            {
                _fresh = (_held = value) is { Value: 0 };
                value = default;
            }

            void TheOneStore([MovedFrom] ref Guard value, Node node, IDisposable?[] owners, ref Guard target, bool flag)
            {
                if (flag)
                {
                    node.Owner = value;
                    value = default;
                }
                else if (owners.Length > 0)
                {
                    owners[0] = value;
                    value = default;
                }
                else
                {
                    int n;
                    (target, n) = (value, 1);
                    value = default;
                }
            }
        }

        {{Sources.Guard}}
        """,
        Verify.MovedFromParameterNotAssignedAtOnce(0, "value", "a use of a field or an element that is not the method's own"),
        Verify.MovedFromParameterNotAssignedAtOnce(1, "value", "a use of a field or an element that is not the method's own"),
        Verify.MovedFromParameterNotAssignedAtOnceThroughReference(2, "value", "fresh"),
        Verify.MovedFromParameterNotAssignedAtOnce(3, "value", "a use of a field or an element that is not the method's own"),
        Verify.MovedFromParameterNotAssignedAtOnceThroughReference(4, "value", "fresh"),
        Verify.MovedFromParameterNotAssignedAtOnce(5, "value", "a use of a field or an element that is not the method's own"),
        Verify.MovedFromParameterNotAssignedAtOnceThroughReference(6, "value", "count"));

    // 'this' in a struct is a reference however it is used, and a reference can be null.
    [TestMethod]
    public Task MovedFrom_ThisInAStructIsAReferenceHoweverItIsUsed() => Verify.AnalyzerAsync(
        $$"""
        using Singulink.CodeAnalysis;

        struct Host
        {
            public int State;

            void Resets([MovedFrom] ref Guard value, ref Guard target)
            {
                target = value;
                {|#0:this|} = default;
                value = default;
            }

            void Copies([MovedFrom] ref Guard value, ref Guard target)
            {
                target = value;
                Host copy = {|#1:this|};
                value = default;
            }

            bool Tests([MovedFrom] ref Guard value, ref Guard target)
            {
                target = value;
                bool isFirst = {|#2:this|} is { State: 1 };
                value = default;
                return isFirst;
            }

            // What can fail is done first.
            bool TestsFirst([MovedFrom] ref Guard value, ref Guard target)
            {
                bool isFirst = this is { State: 1 };
                Host copy = this;
                target = value;
                value = default;
                return isFirst && copy.State == 1;
            }
        }

        // A parameter of a primary constructor that a struct keeps is a field of 'this'.
        struct Seeded(int seed)
        {
            int Reads([MovedFrom] ref Guard value, ref Guard target)
            {
                target = value;
                int read = {|#3:seed|};
                value = default;
                return read;
            }

            void Writes([MovedFrom] ref Guard value, ref Guard target)
            {
                target = value;
                {|#4:seed|} = 1;
                value = default;
            }
        }

        // In a class 'this' is never null.
        class Owner(int seed)
        {
            public int State;

            int Reads([MovedFrom] ref Guard value, ref Guard target)
            {
                target = value;
                int read = seed;
                Owner same = this;
                seed = 2;
                value = default;
                return read + same.State;
            }
        }

        static class Patterns
        {
            // A field of the value that a pattern tests is judged like that value: the second pattern reads through 'other'.
            static void ThroughAReference([MovedFrom] ref Guard value, ref Guard target, ref Host other)
            {
                switch (other)
                {
                    case { State: 1 } when (target = value) is var _:
                        break;
                    case { {|#5:State|}: 2 }:
                        break;
                }

                value = default;
            }

            static void OfAVariable([MovedFrom] ref Guard value, ref Guard target, Host other)
            {
                switch (other)
                {
                    case { State: 1 } when (target = value) is var _:
                        break;
                    case { State: 2 }:
                        break;
                }

                value = default;
            }
        }

        {{Sources.Guard}}
        """,
        Verify.MovedFromParameterNotAssignedAtOnceThroughReference(0, "value", "this"),
        Verify.MovedFromParameterNotAssignedAtOnceThroughReference(1, "value", "this"),
        Verify.MovedFromParameterNotAssignedAtOnceThroughReference(2, "value", "this"),
        Verify.MovedFromParameterNotAssignedAtOnceThroughReference(3, "value", "this"),
        Verify.MovedFromParameterNotAssignedAtOnceThroughReference(4, "value", "this"),
        Verify.MovedFromParameterNotAssignedAtOnce(5, "value", "a use of a field or an element that is not the method's own"));

    // A read through another reference is allowed while the value that was read is only in a local, as a swap needs: if the read fails,
    // the local is gone. Once the local is stored on, the value is in a lasting place.
    [TestMethod]
    public Task MovedFrom_TheValueIsInALastingPlaceOnceItsLocalIsStoredOn() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using Singulink.CodeAnalysis;

        class Keeper
        {
            Guard _previous;

            // A null 'replacement' fails with the guard in '_previous' and still in 'slot'.
            void IntoAField([MovedFrom] ref Guard slot, [MovedFrom] ref Guard replacement)
            {
                Guard old = slot;
                _previous = old;
                slot = {|#0:replacement|};
                replacement = default;
            }

            void ThroughASecondLocal([MovedFrom] ref Guard slot, [MovedFrom] ref Guard replacement)
            {
                Guard old = slot;
                Guard second = old;
                _previous = second;
                slot = {|#1:replacement|};
                replacement = default;
            }

            void ByAMove([MovedFrom] ref Guard slot, [MovedFrom] ref Guard replacement)
            {
                Guard old = slot;
                _previous = NonCopyable.Move(ref old);
                slot = {|#2:replacement|};
                replacement = default;
            }

            // A read that does not go straight into a local.
            static void ByADeconstruction([MovedFrom] ref Guard a, [MovedFrom] ref Guard b, ref Guard target)
            {
                (target, _) = (a, 0);
                a = {|#3:b|};
                b = default;
            }

            static void ByAConditional([MovedFrom] ref Guard a, [MovedFrom] ref Guard b, ref Guard target, bool flag)
            {
                target = flag ? a : default;
                a = {|#4:b|};
                b = default;
            }

            // A local that a lambda uses is a field of an object.
            static Action IntoACapturedLocal([MovedFrom] ref Guard a, [MovedFrom] ref Guard b)
            {
                Guard held = default;
                Action release = () => held.Dispose();
                held = a;
                a = {|#5:b|};
                b = default;
                return release;
            }

            // While the value is only in locals, the read through the other reference is fine: a swap, with one local or with two.
            static void Swap([MovedFrom] ref Guard a, [MovedFrom] ref Guard b)
            {
                Guard held = a;
                a = b;
                b = held;
            }

            static void SwapThroughTwoLocals([MovedFrom] ref Guard a, [MovedFrom] ref Guard b)
            {
                Guard held = a;
                Guard second = held;
                a = b;
                b = second;
            }

            Guard Replace([MovedFrom] ref Guard slot, [MovedFrom] ref Guard replacement)
            {
                Guard old = slot;
                slot = replacement;
                replacement = default;
                return old;
            }
        }

        {{Sources.Guard}}
        """,
        Verify.MovedFromParameterNotAssignedAtOnce(0, "slot", "a read or a write through another reference"),
        Verify.MovedFromParameterNotAssignedAtOnce(1, "slot", "a read or a write through another reference"),
        Verify.MovedFromParameterNotAssignedAtOnce(2, "slot", "a read or a write through another reference"),
        Verify.MovedFromParameterNotAssignedAtOnce(3, "a", "a read or a write through another reference"),
        Verify.MovedFromParameterNotAssignedAtOnce(4, "a", "a read or a write through another reference"),
        Verify.MovedFromParameterNotAssignedAtOnce(5, "a", "a read or a write through another reference"));

    // A variable that a pattern declares has a value that is not followed: it is not what the method read from its [MovedFrom] parameter.
    [TestMethod]
    public Task GenericMove_AVariableThatAPatternDeclaresIsNoPureMoveValue() => Verify.AnalyzerAsync(
        $$"""
        using Singulink.CodeAnalysis;

        static class Stash<[NonCopyable] T>
            where T : class
        {
            private static T? s_slot;

            private static T? Take() => NonCopyable.Move(ref s_slot);

            public static T? Swap([MovedFrom] ref T? value)
            {
                T? mine = NonCopyable.Move(ref value);

                if (Take() is { } cached)
                {
                    s_slot = mine;
                    return {|#0:cached|};
                }

                return mine;
            }

            public static void Refill([MovedFrom] ref T? value)
            {
                if (Take() is { } cached)
                    value = {|#1:cached|};
            }

            public static T? BySwitch([MovedFrom] ref T? value) => {|#2:Take() switch { var old => old }|};

            public static T? ByVar([MovedFrom] ref T? value) => {|#3:Take() is var a ? a : default|};

            public static T? ThroughALocal([MovedFrom] ref T? value)
            {
                T? result = null;

                if (Take() is { } cached)
                    result = cached;

                return {|#4:result|};
            }

            // A pattern on what was read from the parameter is not followed either. The value itself is fine.
            public static T? Tested([MovedFrom] ref T? value)
            {
                T? mine = NonCopyable.Move(ref value);
                return mine is null ? null : mine;
            }

            // With the return value marked, the callers take what comes out for stored.
            [return: Borrowed]
            public static T? Borrowed([MovedFrom] ref T? value) => Take() is { } cached ? cached : NonCopyable.Move(ref value);
        }
        """,
        Verify.GenericMoveReturnsSomethingElse(0, "Stash<T>.Swap(ref T?)", "T"),
        Verify.GenericMoveLeavesSomethingElse(1, "Stash<T>.Refill(ref T?)", "T", "value"),
        Verify.GenericMoveReturnsSomethingElse(2, "Stash<T>.BySwitch(ref T?)", "T"),
        Verify.GenericMoveReturnsSomethingElse(3, "Stash<T>.ByVar(ref T?)", "T"),
        Verify.GenericMoveReturnsSomethingElse(4, "Stash<T>.ThroughALocal(ref T?)", "T"));

    // A reference that a condition chooses is a target like each of its branches.
    [TestMethod]
    public Task MovedFrom_AReferenceChosenByAConditionIsATargetLikeEachBranch() => Verify.AnalyzerAsync(
        $$"""
        using System.Threading.Tasks;
        using Singulink.CodeAnalysis;

        static class Moves
        {
            static Task<Guard>? s_cached;

            // Leaves the cached task in 'task', which the caller then takes for its own.
            static void Refill([MovedFrom] ref Task<Guard>? task, ref Task<Guard>? spare, bool useSpare)
            {
                (useSpare ? ref spare : ref task) = {|#0:s_cached|};
            }

            static void RefillEither([MovedFrom] ref Task<Guard>? first, [MovedFrom] ref Task<Guard>? second, bool useSecond)
            {
                (useSecond ? ref second : ref first) = {|#1:{|#2:s_cached|}|};
            }

            static void RefillNested([MovedFrom] ref Task<Guard>? task, ref Task<Guard>? spare, ref Task<Guard>? other, int which)
            {
                (which == 0 ? ref spare : ref (which == 1 ? ref other : ref task)) = {|#3:s_cached|};
            }

            static void RefillIfEmpty([MovedFrom] ref Task<Guard>? task, ref Task<Guard>? spare, bool useSpare)
            {
                (useSpare ? ref spare : ref task) ??= {|#4:s_cached|};
            }

            // A new value, null and a local of the method's own are fine.
            static void Reset([MovedFrom] ref Task<Guard>? task, [MovedFrom] ref Task<Guard>? spare, bool useSpare, RwLock rwLock)
            {
                (useSpare ? ref spare : ref task) = null;
                (useSpare ? ref spare : ref task) = rwLock.EnterTaskAsync();
            }
        }

        // In a generic move the same holds for what is left behind.
        static class Stash<[NonCopyable] T>
            where T : class
        {
            static T? s_slot;

            static void Refill([MovedFrom] ref T? value, [MovedFrom] ref T? spare, bool useSpare)
            {
                (useSpare ? ref spare : ref value) = {|#5:{|#6:{|#7:s_slot|}|}|};
            }
        }

        {{Sources.Guard}}
        """,
        Verify.MovedFromContainerNotOwned(0, "task"),
        Verify.MovedFromContainerNotOwned(1, "second"),
        Verify.MovedFromContainerNotOwned(2, "first"),
        Verify.MovedFromContainerNotOwned(3, "task"),
        Verify.MovedFromContainerNotOwned(4, "task"),
        Verify.GenericMoveLeavesSomethingElse(5, "Stash<T>.Refill(ref T?, ref T?, bool)", "T", "spare"),
        Verify.GenericMoveLeavesSomethingElse(6, "Stash<T>.Refill(ref T?, ref T?, bool)", "T", "value"),
        Verify.CopyByAssignment(7, "T"));

    // A call of the move helper on a local counts as running nothing because of what its body is. A class of the name in another
    // namespace is an ordinary class, whatever its attributes say.
    [TestMethod]
    public Task MovedFrom_OnlyTheMoveHelperOfTheAttributesNamespaceRunsNothing() => Verify.AnalyzerAsync(
        $$"""
        using Singulink.CodeAnalysis;

        namespace MyLibrary
        {
            public static class NonCopyable
            {
                public static int Moves;

                public static T Move<[Singulink.CodeAnalysis.NonCopyable] T>([MovedFrom] ref T value)
                {
                    Moves = checked(Moves + 1);
                    T moved = value;
                    value = default!;
                    return moved;
                }
            }

            static class Moves
            {
                static int Hand([MovedFrom] ref Guard value, ref Guard target, int tag)
                {
                    int mine = tag;
                    target = value;
                    int taken = {|#0:NonCopyable.Move(ref mine)|};
                    value = default;
                    return taken;
                }

                // So is one whose body is the helper's own, word for word: outside the namespace nothing says that it is the helper.
                static int HandWithTheSameBody([MovedFrom] ref Guard value, ref Guard target, int tag)
                {
                    int mine = tag;
                    target = value;
                    int taken = {|#1:OtherLibrary.NonCopyable.Move(ref mine)|};
                    value = default;
                    return taken;
                }

                // The helper of the package is fine in the same place.
                static int HandWithTheHelper([MovedFrom] ref Guard value, ref Guard target, int tag)
                {
                    int mine = tag;
                    target = value;
                    int taken = Singulink.CodeAnalysis.NonCopyable.Move(ref mine);
                    value = default;
                    return taken;
                }
            }
        }

        namespace OtherLibrary
        {
            public static class NonCopyable
            {
                public static T Move<[Singulink.CodeAnalysis.NonCopyable] T>([MovedFrom] ref T value)
                {
                    T moved = value;
                    value = default!;
                    return moved;
                }
            }
        }

        {{Sources.Guard}}
        """,
        Verify.MovedFromParameterNotAssignedAtOnce(0, "value", "a call"),
        Verify.MovedFromParameterNotAssignedAtOnce(1, "value", "a call"));

    // A helper that the project declares in the namespace is the helper only if its body is the read, the reset and the return.
    [TestMethod]
    [DataRow("T moved = value; value = default!; return moved;", false)]
    [DataRow("var moved = value; value = default(T)!; return moved;", false)]
    [DataRow("T moved = value; value = default; return moved;", false)]
    [DataRow("Moves = checked(Moves + 1); T moved = value; value = default!; return moved;", true)]
    [DataRow("T moved = value; value = default!; Moves++; return moved;", true)]
    [DataRow("T moved = value; value = default!; if (Moves < 0) throw new System.InvalidOperationException(); return moved;", true)]
    [DataRow("T moved = value; value = default!; return Moves / Moves > 1 ? moved : moved;", true)]
    public Task MovedFrom_AMoveHelperOfTheProjectIsTheHelperOnlyWithItsBody(string body, bool isReported)
    {
        string source = $$"""
            namespace Singulink.CodeAnalysis
            {
                public static class NonCopyable
                {
                    public static int Moves;

                    public static T Move<[NonCopyable] T>([MovedFrom] ref T value)
                    {
                        {{body}}
                    }
                }
            }

            namespace MyLibrary
            {
                using Singulink.CodeAnalysis;

                static class Moves
                {
                    static int Hand([MovedFrom] ref Guard value, ref Guard target, int tag)
                    {
                        int mine = tag;
                        target = value;
                        int taken = {{(isReported ? "{|#0:NonCopyable.Move(ref mine)|}" : "NonCopyable.Move(ref mine)")}};
                        value = default;
                        return taken;
                    }
                }
            }

            {{Sources.Guard}}
            """;

        return isReported
            ? Verify.AnalyzerAsync(source, Verify.MovedFromParameterNotAssignedAtOnce(0, "value", "a call"))
            : Verify.AnalyzerAsync(source);
    }

    // A class that runs something when it is first used can throw at the call: a static constructor, or the initializer of a static
    // field. A helper of the project in such a class is no helper.
    [TestMethod]
    [DataRow("public static int Moves;", false)]
    [DataRow("public const int Limit = 1;", false)]
    [DataRow("static NonCopyable() { }", true)]
    [DataRow("public static readonly int Limit = int.Parse(System.Environment.GetEnvironmentVariable(\"MOVE_LIMIT\") ?? \"none\");", true)]
    public Task MovedFrom_AMoveHelperOfTheProjectIsTheHelperOnlyInAClassThatRunsNothing(string member, bool isReported)
    {
        string source = $$"""
            namespace Singulink.CodeAnalysis
            {
                public static class NonCopyable
                {
                    {{member}}

                    public static T Move<[NonCopyable] T>([MovedFrom] ref T value)
                    {
                        T moved = value;
                        value = default!;
                        return moved;
                    }
                }
            }

            namespace MyLibrary
            {
                using Singulink.CodeAnalysis;

                static class Moves
                {
                    static int Hand([MovedFrom] ref Guard value, ref Guard target, int tag)
                    {
                        int mine = tag;
                        target = value;
                        int taken = {{(isReported ? "{|#0:NonCopyable.Move(ref mine)|}" : "NonCopyable.Move(ref mine)")}};
                        value = default;
                        return taken;
                    }
                }
            }

            {{Sources.Guard}}
            """;

        return isReported
            ? Verify.AnalyzerAsync(source, Verify.MovedFromParameterNotAssignedAtOnce(0, "value", "a call"))
            : Verify.AnalyzerAsync(source);
    }

    // A reference that a condition chooses is a reference to each of its branches: through a 'ref' local over it, or through what
    // it is passed to by reference, the method assigns anything to the [MovedFrom] parameter.
    [TestMethod]
    public Task MovedFrom_AContainerParameterBehindAReferenceThatAConditionChooses_IsReported() => Verify.AnalyzerAsync(
        $$"""
        using System.Threading.Tasks;
        using Singulink.CodeAnalysis;

        static class Moves
        {
            static Task<Guard>? s_cached;

            static void RefLocal([MovedFrom] ref Task<Guard>? task, Task<Guard>?[] spares, int index)
            {
                ref var slot = ref (index >= 0 ? ref spares[index] : ref {|#0:task|});
                slot = s_cached;
            }

            static void OutArgument([MovedFrom] ref Task<Guard>? task, Task<Guard>?[] spares, int index)
            {
                Fill(out (index >= 0 ? ref spares[index] : ref {|#1:task|}));
            }

            static void RefArgument([MovedFrom] ref Task<Guard>? task, Task<Guard>?[] spares, int index)
            {
                Store(ref (index >= 0 ? ref {|#2:task|} : ref spares[index]));
            }

            static void RefAssignment([MovedFrom] ref Task<Guard>? task, ref Task<Guard>? other, Task<Guard>?[] spares, int index)
            {
                ref var slot = ref other;
                slot = ref (index >= 0 ? ref spares[index] : ref (index < -1 ? ref {|#3:task|} : ref spares[1]));
                slot = s_cached;
            }

            // A reference through which the method only resets is fine, as it is with no condition.
            static void OnlyReset([MovedFrom] ref Task<Guard>? task, Task<Guard>?[] spares, int index)
            {
                ref var slot = ref (index >= 0 ? ref spares[index] : ref task);
                slot = null;
            }

            // A condition that reads the parameter chooses nothing of it.
            static void InTheCondition([MovedFrom] ref Task<Guard>? task, Task<Guard>?[] spares)
            {
                ref var slot = ref (task is null ? ref spares[0] : ref spares[1]);
                slot = null;
            }

            static void Fill(out Task<Guard>? task) => task = null;

            static void Store(ref Task<Guard>? task) => task = null;
        }

        {{Sources.Guard}}
        """,
        Verify.MovedFromContainerExposed(0, "task", "has a reference taken to it"),
        Verify.MovedFromContainerExposed(1, "task", "is passed by reference to 'Moves.Fill(out Task<Guard>?)'"),
        Verify.MovedFromContainerExposed(2, "task", "is passed by reference to 'Moves.Store(ref Task<Guard>?)'"),
        Verify.MovedFromContainerExposed(3, "task", "has a reference taken to it"));

    // [MovedFrom] on a parameter of a primary constructor is reported where it is declared. The code of a primary constructor is the
    // initializers of the type and the call of the base constructor, which have no order in which the parameter could be read and
    // then assigned, and no path runs through them. An ordinary constructor is the form to use.
    [TestMethod]
    public Task MovedFrom_OnAParameterOfAPrimaryConstructor_IsReportedWhereItIsDeclared() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using System.Threading.Tasks;
        using Singulink.CodeAnalysis;

        public sealed class Session([MovedFrom] ref Guard {|#0:guard|}) : IDisposable
        {
            private Guard _guard = guard;

            public void Dispose() => _guard.Dispose();
        }

        public class SessionBase
        {
            private Guard _guard;

            public SessionBase(Guard guard)
            {
                _guard = guard;
            }
        }

        public sealed class DerivedSession([MovedFrom] ref Guard {|#1:guard|}) : SessionBase(guard);

        [NonCopyable]
        public struct Scope([MovedFrom] ref Guard {|#2:guard|})
        {
            private Guard _guard = guard;
        }

        public sealed class Pending([MovedFrom] ref Task<Guard>? {|#3:task|})
        {
            public Task<Guard>? Task { get; } = task;
        }

        public sealed class TwoParameters([MovedFrom] ref Guard {|#4:moved|}, ref Guard read)
        {
            private Guard _guard = moved;
        }

        // An ordinary constructor has paths, which are followed.
        public sealed class Ordinary
        {
            private Guard _guard;

            public Ordinary([MovedFrom] ref Guard guard)
            {
                _guard = guard;
                guard = default;
            }
        }

        // And an initializer can move the value out itself.
        public sealed class MovedByAnInitializer(ref Guard guard)
        {
            public Guard Held = NonCopyable.Move(ref guard);
        }

        {{Sources.Guard}}
        """,
        Verify.MovedFromOnPrimaryConstructor(0, "guard", "Session"),
        Verify.MovedFromOnPrimaryConstructor(1, "guard", "DerivedSession"),
        Verify.MovedFromOnPrimaryConstructor(2, "guard", "Scope"),
        Verify.MovedFromOnPrimaryConstructor(3, "task", "Pending"),
        Verify.MovedFromOnPrimaryConstructor(4, "moved", "TwoParameters"));

    // The concatenation of two strings in the values of a group assignment runs after the read of a [MovedFrom] parameter and
    // before its assignment, and can throw there. One that the compiler folds runs nothing.
    [TestMethod]
    public Task MovedFrom_AConcatenationOfStringsInAGroupAssignmentCanThrow() => Verify.AnalyzerAsync(
        $$"""
        using Singulink.CodeAnalysis;

        static class Moves
        {
            const string Prefix = "held by ";

            static void Concatenated([MovedFrom] ref Guard value, ref Guard target, string prefix, string suffix)
            {
                string label;
                (target, label) = (value, {|#0:prefix + suffix|});
                value = default;
            }

            static void Folded([MovedFrom] ref Guard value, ref Guard target)
            {
                string label;
                (target, label) = (value, Prefix + "the lock");
                value = default;
            }
        }

        {{Sources.Guard}}
        """,
        Verify.MovedFromParameterNotAssignedAtOnce(0, "value", "an operation that can throw or run other code"));
}
