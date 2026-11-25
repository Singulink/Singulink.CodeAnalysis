using PrefixClassName.MsTest;

namespace Singulink.CodeAnalysis.Tests;

/// <summary>
/// Tests of the property that the list marks <c>owned</c>: the task of a completion source that the method has just made is a new
/// value where the method reads it, once.
/// </summary>
/// <remarks>
/// Docs: articles/rules/listed-members.md.
/// </remarks>
[PrefixTestClass]
public class OwnedTaskTests
{
    private const string Clean = $$"""
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;

        class Lock
        {
            readonly Queue<TaskCompletionSource<Guard>> _waiters = new();
            readonly List<TaskCompletionSource<Guard>> _list = new();
            TaskCompletionSource<Guard> _last = new();

            // The source is in the queue as well. What counts is where its task can come out: everything else reads it from the
            // property of a source that it did not make, where it is stored.
            Task<Guard> Returned()
            {
                var source = new TaskCompletionSource<Guard>();
                _waiters.Enqueue(source);
                return source.Task;
            }

            ValueTask<Guard> Wrapped()
            {
                var source = new TaskCompletionSource<Guard>(TaskCreationOptions.RunContinuationsAsynchronously);
                _list.Add(source);
                return new ValueTask<Guard>(source.Task);
            }

            Task<Guard> ThroughALocal()
            {
                var source = new TaskCompletionSource<Guard>(this);
                _last = source;
                var task = source.Task;
                return task;
            }

            async Task Awaited()
            {
                TaskCompletionSource<Guard> source = new(this, TaskCreationOptions.None);
                _waiters.Enqueue(source);
                using Guard guard = await source.Task;
            }

            Task<Guard> AssignedAfterItsDeclaration(bool queued)
            {
                TaskCompletionSource<Guard> source;
                source = new TaskCompletionSource<Guard>();

                if (queued)
                    _waiters.Enqueue(source);

                return source.Task;
            }

            // A lambda or a local function that uses the source is one more holder of it, like the queue.
            Task<Guard> Cancellable(CancellationToken token)
            {
                var source = new TaskCompletionSource<Guard>();
                token.Register(() => source.TrySetCanceled());
                _waiters.Enqueue(source);
                return source.Task;
            }

            Task<Guard> CancellableByALocalFunction(CancellationToken token)
            {
                var source = new TaskCompletionSource<Guard>();
                token.Register(Cancel);
                _waiters.Enqueue(source);
                return source.Task;

                void Cancel() => source.TrySetCanceled(token);
            }

            Task<Guard> CancellableByAnAnonymousMethod(CancellationToken token)
            {
                var source = new TaskCompletionSource<Guard>();
                token.Register(delegate { source.TrySetCanceled(); });
                _waiters.Enqueue(source);
                return source.Task;
            }

            Task<Guard> HeldByALambda()
            {
                var source = new TaskCompletionSource<Guard>();

                Action queue = () =>
                {
                    if (source is not null && source != _last)
                        _waiters.Enqueue(source);

                    var other = source;
                    _last = source;
                };

                return source.Task;
            }

            Task<Guard> CompletedHere(RwLock rwLock)
            {
                var source = new TaskCompletionSource<Guard>();
                source.SetResult(rwLock.Enter());

                if (source is null || source == _last)
                    return null!;

                return source.Task;
            }
        }

        {{Sources.Guard}}
        """;

    [TestMethod]
    public Task OwnedTask_OfASourceThatTheMethodHasJustMadeIsNew() => Verify.AnalyzerAsync(Clean);

    // The rule says where the task is new. It is not a move on the last use of anything.
    [TestMethod]
    public Task OwnedTask_DoesNotDependOnTheMoveOnTheLastUse()
    {
        var test = new Verify.Test { TestCode = Clean }.WithBuildProperty("NonCopyableAllowImplicitMoves", "false");

        return test.RunAsync();
    }

    [TestMethod]
    public Task OwnedTask_OfAnyOtherSourceIsStored() => Verify.AnalyzerAsync(
        $$"""
        using System;
        using System.Collections.Generic;
        using System.Threading.Tasks;

        class Derived : TaskCompletionSource<Guard> { }

        class Lock
        {
            readonly Queue<TaskCompletionSource<Guard>> _waiters = new();
            TaskCompletionSource<Guard> _source = new();

            static TaskCompletionSource<Guard> Create() => new();
            static void Replace(ref TaskCompletionSource<Guard> source) { }
            static void Make(out TaskCompletionSource<Guard> source) => source = new();

            // The bug that the rule must keep catching: one source, and every caller gets its task.
            Task<Guard> Field() => {|#0:_source.Task|};

            async Task AwaitedField()
            {
                using Guard guard = {|#1:await _source.Task|};
            }

            ValueTask<Guard> WrappedField() => {|#2:new ValueTask<Guard>(_source.Task)|};

            // The receiver is a local of the method, and nothing else.
            Task<Guard> Parameter(TaskCompletionSource<Guard> source) => {|#3:source.Task|};

            Task<Guard> RefLocal()
            {
                ref TaskCompletionSource<Guard> source = ref _source;
                return {|#4:source.Task|};
            }

            // A lambda or a local function may hold the source. It may not read the task, assign the local or take a reference to it.
            Func<Task<Guard>> ReadInALambdaOnly()
            {
                var source = new TaskCompletionSource<Guard>();
                _waiters.Enqueue(source);
                return () => {|#5:source.Task|};
            }

            Task<Guard> ReadInALocalFunctionThatIsCalledOnce()
            {
                var source = new TaskCompletionSource<Guard>();
                _waiters.Enqueue(source);
                return Get();

                Task<Guard> Get() => {|#33:source.Task|};
            }

            Task<Guard> AssignedInALambda()
            {
                var source = new TaskCompletionSource<Guard>();
                Action renew = () => source = new TaskCompletionSource<Guard>();
                return {|#34:source.Task|};
            }

            Task<Guard> ReferredToInALambda()
            {
                var source = new TaskCompletionSource<Guard>();
                Action replace = () => Replace(ref source);
                return {|#35:source.Task|};
            }

            // Every value that the local is given is a source that is made there, and it is given one once.
            Task<Guard> FromACall()
            {
                var source = Create();
                return {|#6:source.Task|};
            }

            Task<Guard> FromAField()
            {
                var source = _source;
                return {|#7:source.Task|};
            }

            Task<Guard> FromAConditional(bool fresh)
            {
                var source = fresh ? new TaskCompletionSource<Guard>() : _source;
                return {|#8:source.Task|};
            }

            Task<Guard> AssignedTwice()
            {
                var source = new TaskCompletionSource<Guard>();
                _waiters.Enqueue(source);
                source = new TaskCompletionSource<Guard>();
                return {|#9:source.Task|};
            }

            Task<Guard> PassedByReference()
            {
                var source = new TaskCompletionSource<Guard>();
                Replace(ref source);
                return {|#10:source.Task|};
            }

            Task<Guard> AssignedByAnOutArgument()
            {
                var source = new TaskCompletionSource<Guard>();
                Make(out source);
                return {|#11:source.Task|};
            }

            Task<Guard> DeclaredByAnOutArgument()
            {
                Make(out var source);
                return {|#12:source.Task|};
            }

            Task<Guard> DeclaredByAPattern(object value)
            {
                if (value is TaskCompletionSource<Guard> source)
                    return {|#13:source.Task|};

                return null!;
            }

            Task<Guard> DeclaredByAPatternAndAssigned(object value)
            {
                if (value is not TaskCompletionSource<Guard> source)
                    source = new TaskCompletionSource<Guard>();

                return {|#29:source.Task|};
            }

            Task<Guard> AssignedFromACallLater()
            {
                TaskCompletionSource<Guard> source;
                source = Create();
                return {|#30:source.Task|};
            }

            // A source that is made with something that carries a non-copyable type is not known to be made of nothing.
            Task<Guard> MadeWithATask(Task<Guard> state)
            {
                var source = new TaskCompletionSource<Guard>({|#32:state|});
                return {|#31:source.Task|};
            }

            Task<Guard> DeclaredByADeconstruction()
            {
                var (source, count) = (new TaskCompletionSource<Guard>(), 1);
                return {|#14:source.Task|};
            }

            Task<Guard> ReferredTo()
            {
                var source = new TaskCompletionSource<Guard>();
                ref var other = ref source;
                return {|#15:source.Task|};
            }

            // The type is the one that declares the property: the code of a class that derives from it can hand the task out.
            Task<Guard> OfADerivedType()
            {
                var source = new Derived();
                return {|#16:source.Task|};
            }

            Task<Guard> OfADerivedObject()
            {
                TaskCompletionSource<Guard> source = new Derived();
                return {|#17:source.Task|};
            }

            // The task is read once in the whole member.
            Task<Guard> ReadTwice()
            {
                var source = new TaskCompletionSource<Guard>();
                bool completed = source.Task.IsCompleted;
                return {|#18:source.Task|};
            }

            Task<Guard> ReadInADeadBranchToo()
            {
                var source = new TaskCompletionSource<Guard>();

                if (false)
                {
            #pragma warning disable CS0162
                    _ = source.Task;
            #pragma warning restore CS0162
                }

                return {|#19:source.Task|};
            }

            Task<Guard> ReadInALambdaToo()
            {
                var source = new TaskCompletionSource<Guard>();
                Func<bool> isCompleted = () => source.Task.IsCompleted;
                return {|#27:source.Task|};
            }

            Task<Guard> ReadByAPatternToo()
            {
                var source = new TaskCompletionSource<Guard>();

                if (source is { Task.IsCompleted: true })
                    return null!;

                return {|#28:source.Task|};
            }

            Task<Guard> ReadThroughAConditionalAccessToo()
            {
                var source = new TaskCompletionSource<Guard>();
                var first = source?.Task;
                return {|#20:source.Task|};
            }

            // And that read runs once: it is not in a loop, a lambda, a local function or an iterator.
            Task<Guard> InALoop()
            {
                while (true)
                {
                    var source = new TaskCompletionSource<Guard>();
                    _waiters.Enqueue(source);
                    return {|#21:source.Task|};
                }
            }

            Task<Guard> DeclaredOutsideTheLoop()
            {
                var source = new TaskCompletionSource<Guard>();

                for (int i = 0; i < 2; i++)
                {
                    if (i == 1)
                        return {|#22:source.Task|};
                }

                return null!;
            }

            Task<Guard> AfterAGoto()
            {
                var source = new TaskCompletionSource<Guard>();

            again:
                if (_waiters.Count > 9)
                    goto again;

                return {|#23:source.Task|};
            }

            Func<Task<Guard>> InALambda() => () =>
            {
                var source = new TaskCompletionSource<Guard>();
                return {|#24:source.Task|};
            };

            Task<Guard> InALocalFunction()
            {
                return Enter();

                Task<Guard> Enter()
                {
                    var source = new TaskCompletionSource<Guard>();
                    return {|#25:source.Task|};
                }
            }

            IEnumerable<Task<Guard>> InAnIterator()
            {
                var source = new TaskCompletionSource<Guard>();
                yield return {|#26:source.Task|};
            }
        }

        {{Sources.Guard}}
        """,
        Returned(0, "Lock.Field()"),
        Verify.ReadFromStoredContainer(1, "'await'", "Guard"),
        Verify.CreatedFromStoredContainer(2, "System.Threading.Tasks.ValueTask<Guard>"),
        Returned(3, "Lock.Parameter(TaskCompletionSource<Guard>)"),
        Returned(4, "Lock.RefLocal()"),
        Returned(5, "lambda expression"),
        Returned(6, "Lock.FromACall()"),
        Returned(7, "Lock.FromAField()"),
        Returned(8, "Lock.FromAConditional(bool)"),
        Returned(9, "Lock.AssignedTwice()"),
        Returned(10, "Lock.PassedByReference()"),
        Returned(11, "Lock.AssignedByAnOutArgument()"),
        Returned(12, "Lock.DeclaredByAnOutArgument()"),
        Returned(13, "Lock.DeclaredByAPattern(object)"),
        Returned(14, "Lock.DeclaredByADeconstruction()"),
        Returned(15, "Lock.ReferredTo()"),
        Returned(16, "Lock.OfADerivedType()"),
        Returned(17, "Lock.OfADerivedObject()"),
        Returned(18, "Lock.ReadTwice()"),
        Returned(19, "Lock.ReadInADeadBranchToo()"),
        Returned(20, "Lock.ReadThroughAConditionalAccessToo()"),
        Returned(21, "Lock.InALoop()"),
        Returned(22, "Lock.DeclaredOutsideTheLoop()"),
        Returned(23, "Lock.AfterAGoto()"),
        Returned(24, "lambda expression"),
        Returned(25, "Enter()"),
        Returned(26, "Lock.InAnIterator()"),
        Returned(27, "Lock.ReadInALambdaToo()"),
        Returned(28, "Lock.ReadByAPatternToo()"),
        Returned(29, "Lock.DeclaredByAPatternAndAssigned(object)"),
        Returned(30, "Lock.AssignedFromACallLater()"),
        Returned(31, "Lock.MadeWithATask(Task<Guard>)"),
        Verify.KeepsUnmarkedParameter(32, "Lock.MadeWithATask(Task<Guard>)", "state", "System.Threading.Tasks.Task<Guard>"),
        Returned(33, "Get()"),
        Returned(34, "Lock.AssignedInALambda()"),
        Returned(35, "Lock.ReferredToInALambda()"));

    // The form that allocates no closure passes the source as the state of a static lambda. The source becomes an object on the way,
    // which the list allows: it has the type as erasable, because nothing that an object can be converted to hands out its result.
    [TestMethod]
    public Task OwnedTask_OfASourceThatIsPassedAsStateIsNew() => Verify.AnalyzerAsync(
        $$"""
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;

        class Lock
        {
            readonly Queue<TaskCompletionSource<Guard>> _waiters = new();

            Task<Guard> Cancellable(CancellationToken token)
            {
                var source = new TaskCompletionSource<Guard>();
                token.Register(static state => ((TaskCompletionSource<Guard>)state!).TrySetCanceled(), source);
                _waiters.Enqueue(source);
                return source.Task;
            }

            Task<Guard> CancellableWithTheToken(CancellationToken token)
            {
                var source = new TaskCompletionSource<Guard>();
                token.Register(static (state, token) => ((TaskCompletionSource<Guard>)state!).TrySetCanceled(token), source);
                _waiters.Enqueue(source);
                return source.Task;
            }
        }

        {{Sources.Guard}}
        """);

    // The other side of the entry: what comes back out of the object is stored, and a class that derives from the source must not
    // add a way to its result that an object can be converted to.
    [TestMethod]
    public Task OwnedTask_ASourceThatIsErasableStaysSo() => Verify.AnalyzerAsync(
        $$"""
        using System.Collections;
        using System.Collections.Generic;
        using System.Threading.Tasks;
        using Singulink.CodeAnalysis;

        interface IProducer<out T> { T Take(); }

        interface IWithDefault<T>
        {
            T Peek();

            object PeekUntyped() => Peek()!;
        }

        class Plain : TaskCompletionSource<Guard> { }

        // The object behind a source can be of a class that derives from it. With a container or a type parameter bound, a covariant
        // interface is one over object after a conversion, and an interface with a body that is not checked can box what it is given.
        class {|#0:Lists|} : TaskCompletionSource<List<Guard>>, IProducer<List<Guard>>
        {
            public List<Guard> Take() => new();
        }

        class {|#1:Producing|}<[NonCopyable] T> : TaskCompletionSource<T>, IProducer<T>
        {
            public T Take() => default!;
        }

        class {|#4:Peekable|} : TaskCompletionSource<Guard>, IWithDefault<Guard>
        {
            public Guard Peek() => default;
        }

        // A class that names the non-copyable struct itself is checked code, and variance does not convert a struct: its own bodies
        // answer for what they hand out.
        class Listing : TaskCompletionSource<Guard>, IEnumerable<Guard>, IProducer<Guard>
        {
            public IEnumerator<Guard> GetEnumerator() => null!;

            IEnumerator IEnumerable.GetEnumerator() => null!;

            public Guard Take() => default;
        }

        class C
        {
            Task<Guard> ReturnedOutOfAnObject()
            {
                var source = new TaskCompletionSource<Guard>();
                object boxed = source;
                return {|#2:((TaskCompletionSource<Guard>)boxed).Task|};
            }

            async Task AwaitedOutOfAnObject()
            {
                var source = new TaskCompletionSource<Guard>();
                object boxed = source;
                using Guard guard = {|#3:await ((TaskCompletionSource<Guard>)boxed).Task|};
            }

            void Converted(Plain plain)
            {
                object boxed = plain;
                TaskCompletionSource<Guard> source = plain;
            }
        }

        {{Sources.Guard}}
        """,
        Verify.ErasableBaseNotKept(
            0,
            "Lists",
            "System.Threading.Tasks.TaskCompletionSource<System.Collections.Generic.List<Guard>>",
            "'IProducer<T>' is covariant in 'T', which is bound to 'System.Collections.Generic.List<Guard>'"),
        Verify.ErasableBaseNotKept(
            1,
            "Producing<T>",
            "System.Threading.Tasks.TaskCompletionSource<T>",
            "'IProducer<T>' is covariant in 'T', which is bound to 'T', a type parameter that is not constrained to 'struct'"),
        Verify.ErasableBaseNotKept(
            4,
            "Peekable",
            "System.Threading.Tasks.TaskCompletionSource<Guard>",
            "'IWithDefault<T>' receives 'Guard' through type parameter 'T', which is not marked [NonCopyable]"),
        Verify.UnownedContainerReturned(2, "C.ReturnedOutOfAnObject()", "System.Threading.Tasks.Task<Guard>"),
        Verify.ReadFromStoredContainer(3, "'await'", "Guard"));

    [TestMethod]
    public Task OwnedTask_TheMarkerIsForAPropertyThatGivesATask() => Verify.AnalyzerAsync(
        $$"""
        using System.Collections.Generic;
        using System.Threading.Tasks;

        class Holder
        {
            public List<Guard> Items => new();

            public Task<Guard> Pending => null!;

            public static Task<Guard> Shared => null!;

            public Task<Guard> Get() => null!;
        }

        interface IHolder
        {
            Task<Guard> Pending { get; }
        }

        // A call on a local of a struct passes the local by reference, so 'Become' replaces the local with a source that everyone has.
        struct StructHolder
        {
            private TaskCompletionSource<Guard> _source;

            public StructHolder() => _source = new();

            public Task<Guard> Pending => _source.Task;

            public void Become(StructHolder other) => this = other;
        }

        class C
        {
            static StructHolder s_shared;

            Task<Guard> OfAStruct()
            {
                var holder = new StructHolder();
                holder.Become(s_shared);
                return {|#1:holder.Pending|};
            }

            // A user file that repeats the built-in entry without the marker takes the marker away: the entry that claims less applies.
            Task<Guard> M()
            {
                var source = new TaskCompletionSource<Guard>();
                return {|#0:source.Task|};
            }

            // It can mark a property of its own that gives a task.
            Task<Guard> N()
            {
                var holder = new Holder();
                return holder.Pending;
            }
        }

        {{Sources.Guard}}
        """,
        """
        P:System.Threading.Tasks.TaskCompletionSource`1.Task
        P:Holder.Items;owned
        M:Holder.Get;owned
        T:Holder;owned
        P:Holder.Pending;owned
        P:StructHolder.Pending;owned
        P:IHolder.Pending;owned
        P:Holder.Shared;owned
        """,
        Returned(0, "C.M()"),
        Returned(1, "C.OfAStruct()", fromObjectMadeHere: true),
        Verify.SafeListLine(2, 1, 21, OwnedIsForATask),
        Verify.SafeListLine(3, 1, 19, OwnedIsForATask),
        Verify.SafeListLine(4, 1, 15, OwnedIsForATask),
        Verify.SafeListLine(6, 1, 29, OwnedIsForATask),
        Verify.SafeListLine(7, 1, 24, OwnedIsForATask),
        Verify.SafeListLine(8, 1, 22, OwnedIsForATask));

    private const string OwnedIsForATask = "the marker 'owned' is for an instance property of a class whose type is 'Task<TResult>'";

    private static Microsoft.CodeAnalysis.Testing.DiagnosticResult Returned(int location, string method, bool fromObjectMadeHere = false) =>
        Verify.UnownedContainerReturned(location, method, "System.Threading.Tasks.Task<Guard>", fromObjectMadeHere: fromObjectMadeHere);
}
