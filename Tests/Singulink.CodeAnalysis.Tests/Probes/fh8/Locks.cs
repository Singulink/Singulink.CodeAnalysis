using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading.Tasks.Sources;
using Singulink.CodeAnalysis;

namespace FirstHour;

// ---------- A simple lock and its guard ----------

public sealed class SimpleLock
{
    private readonly object _sync = new();
    private int _held;
    internal int Releases;

    public LockGuard Enter()
    {
        Monitor.Enter(_sync);
        _held++;
        return new LockGuard(this);
    }

    public bool TryEnter(out LockGuard guard)
    {
        if (Monitor.TryEnter(_sync))
        {
            _held++;
            guard = new LockGuard(this);
            return true;
        }

        guard = default;
        return false;
    }

    internal void Exit()
    {
        _held--;
        Releases++;
        Monitor.Exit(_sync);
    }
}

[NonCopyable]
public struct LockGuard : IDisposable
{
    private SimpleLock? _owner;

    internal LockGuard(SimpleLock owner) => _owner = owner;

    public readonly bool IsHeld => _owner is not null;

    public void Dispose()
    {
        var owner = _owner;
        _owner = null;
        owner?.Exit();
    }
}

// ---------- An async lock with a pooled IValueTaskSource ----------

public sealed class AsyncLock
{
    private readonly object _sync = new();
    private readonly Queue<Waiter> _waiters = new();
    private bool _held;

    public ValueTask<AsyncLockGuard> EnterAsync(CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (!_held)
            {
                _held = true;
                return new ValueTask<AsyncLockGuard>(new AsyncLockGuard(this));
            }

            var waiter = new Waiter(this);
            _waiters.Enqueue(waiter);
            return new ValueTask<AsyncLockGuard>(waiter, waiter.Version);
        }
    }

    internal void Release()
    {
        Waiter? next = null;

        lock (_sync)
        {
            if (!_waiters.TryDequeue(out next))
                _held = false;
        }

        next?.Grant();
    }

    private sealed class Waiter(AsyncLock owner) : IValueTaskSource<AsyncLockGuard>
    {
        private ManualResetValueTaskSourceCore<AsyncLockGuard> _core;

        public short Version => _core.Version;

        public void Grant() => _core.SetResult(new AsyncLockGuard(owner));

        public AsyncLockGuard GetResult(short token) => _core.GetResult(token);

        public ValueTaskSourceStatus GetStatus(short token) => _core.GetStatus(token);

        public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags) =>
            _core.OnCompleted(continuation, state, token, flags);
    }
}

[NonCopyable]
public struct AsyncLockGuard : IDisposable
{
    private AsyncLock? _owner;

    internal AsyncLockGuard(AsyncLock owner) => _owner = owner;

    public void Dispose()
    {
        var owner = _owner;
        _owner = null;
        owner?.Release();
    }
}

// ---------- A small generic collection ----------

public sealed class GuardStack<[NonCopyable] T>
{
    private T[] _items = new T[4];
    private int _count;

    public int Count => _count;

    public void Push(T item)
    {
        if (_count == _items.Length)
            Array.Resize(ref _items, _items.Length * 2);

        _items[_count++] = item;
    }

    public T Pop()
    {
        if (_count == 0)
            throw new InvalidOperationException("Empty.");

        return NonCopyable.Move(ref _items[--_count]);
    }

    public bool TryPop([MaybeNullWhen(false)] out T item)
    {
        if (_count == 0)
        {
            item = default;
            return false;
        }

        item = NonCopyable.Move(ref _items[--_count]);
        return true;
    }

    public ref T Peek() => ref _items[_count - 1];

    public Span<T> AsSpan() => _items.AsSpan(0, _count);

    public Enumerator GetEnumerator() => new(this);

    public ref struct Enumerator
    {
        private readonly GuardStack<T> _stack;
        private int _index;

        internal Enumerator(GuardStack<T> stack)
        {
            _stack = stack;
            _index = stack._count;
        }

        public bool MoveNext() => --_index >= 0;

        public ref T Current => ref _stack._items[_index];
    }
}

// ---------- Users of all of the above ----------

public sealed class Service
{
    private readonly SimpleLock _lock = new();
    private readonly AsyncLock _asyncLock = new();
    private LockGuard _held;
    private ValueTask<AsyncLockGuard> _pending;

    public void DoWork()
    {
        using var guard = _lock.Enter();
        Console.WriteLine(guard.IsHeld);
    }

    public void DoWorkExplicit()
    {
        var guard = _lock.Enter();

        try
        {
            Console.WriteLine("work");
        }
        finally
        {
            guard.Dispose();
        }
    }

    public void TryWork()
    {
        if (_lock.TryEnter(out var guard))
        {
            using (guard)
            {
                Console.WriteLine("got it");
            }
        }
    }

    public void Hold()
    {
        var guard = _lock.Enter();
        try
        {
            Validate();
            _held = guard;
        }
        catch
        {
            guard.Dispose();
            throw;
        }
    }

    public void Unhold()
    {
        _held.Dispose();
    }

    public LockGuard Handoff()
    {
        return NonCopyable.Move(ref _held);
    }

    public async Task DoWorkAsync(CancellationToken ct)
    {
        using var guard = await _asyncLock.EnterAsync(ct);
        await Task.Delay(1, ct);
    }

    public async Task StartThenAwaitAsync()
    {
        var pending = _asyncLock.EnterAsync();
        Console.WriteLine("doing other stuff");
        using var guard = await pending;
    }

    public void StartPending() => _pending = _asyncLock.EnterAsync();

    public async Task FinishPendingAsync()
    {
        using var guard = await NonCopyable.Move(ref _pending);
    }

    public async Task<bool> FastPathAsync()
    {
        var pending = _asyncLock.EnterAsync();

        if (pending.IsCompletedSuccessfully)
        {
            using var g = pending.Result;
            return true;
        }

        using var g2 = await pending;
        return false;
    }

    public void Collection()
    {
        var stack = new GuardStack<LockGuard>();
        stack.Push(_lock.Enter());
        stack.Push(_lock.Enter());

        stack.Peek().Dispose();

        foreach (ref var g in stack)
            g.Dispose();

        foreach (ref var g in stack.AsSpan())
            g.Dispose();

        while (stack.TryPop(out var popped))
            popped.Dispose();

        using var last = stack.Pop();
    }

    public List<LockGuard> CollectList()
    {
        var list = new List<LockGuard>();
        list.Add(_lock.Enter());
        list.Add(_lock.Enter());

        foreach (ref var g in System.Runtime.InteropServices.CollectionsMarshal.AsSpan(list))
            Console.WriteLine(g.IsHeld);

        return list;
    }

    public void DisposeAll(List<LockGuard> list)
    {
        foreach (ref var g in System.Runtime.InteropServices.CollectionsMarshal.AsSpan(list))
            g.Dispose();

        list.Clear();
    }

    public LockGuard[] Many(int n)
    {
        var guards = new LockGuard[n];

        for (int i = 0; i < n; i++)
            guards[i] = _lock.Enter();

        return guards;
    }

    public void Swap(ref LockGuard a, ref LockGuard b)
    {
        var tmp = NonCopyable.Move(ref a);
        a = NonCopyable.Move(ref b);
        b = tmp;
    }

    public static void Replace([MovedFrom] ref LockGuard target, [MovedFrom] ref LockGuard source)
    {
        target.Dispose();
        target = source;
        source = default;
    }

    public (LockGuard Guard, bool Fresh) EnterTuple() => (_lock.Enter(), true);

    public void UseTuple()
    {
        var (guard, fresh) = EnterTuple();
        using (guard)
            Console.WriteLine(fresh);
    }

    private static void Validate() { }
}

public static class Program
{
    public static void Main()
    {
        var s = new Service();
        s.DoWork();
        s.DoWorkExplicit();
        s.TryWork();
        s.Collection();
        s.DisposeAll(s.CollectList());
        Console.WriteLine("ok");
    }
}
