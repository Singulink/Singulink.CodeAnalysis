using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Singulink.CodeAnalysis;

// A1: a [NonCopyable] awaiter. The compiler copies it into the state machine before it calls OnCompleted on the local.
public readonly struct ReserveAwaitable(Lock l)
{
    public ReserveAwaiter GetAwaiter() => new ReserveAwaiter(l.Enter());
}

[NonCopyable]
public struct ReserveAwaiter : ICriticalNotifyCompletion
{
    private Guard _g;
    public ReserveAwaiter(Guard g) => _g = g;
    public bool IsCompleted => false;
    // On suspension the reservation goes to the scheduler, which releases it when it resumes the waiter.
    public void OnCompleted(Action c) => UnsafeOnCompleted(c);
    public void UnsafeOnCompleted(Action c)
    {
        var held = NonCopyable.Move(ref _g);
        Scheduler.Post(held, c);
    }
    // Releases the reservation if the awaiter still has it.
    public void GetResult() => _g.Dispose();
}

public static class Scheduler
{
    public static void Post(Guard g, Action c) { g.Dispose(); c(); }
}

public static class RTA1
{
    static async Task Run(Lock l) { await new ReserveAwaitable(l); }
    public static Lock Awaiter() { var l = new Lock(); Run(l).GetAwaiter().GetResult(); return l; }
}

// A2: async member of a [NonCopyable] readonly struct runs on a copy; Unsafe.AsRef releases the copy.
[NonCopyable]
public readonly struct Lease : IDisposable
{
    private readonly Guard _g;
    public Lease(Guard g) => _g = g;
    public void Dispose() => Unsafe.AsRef(in _g).Dispose();
    public async Task DisposeAsync2() { await Task.Yield(); Unsafe.AsRef(in _g).Dispose(); }
    public System.Collections.Generic.IEnumerable<int> DisposeLazily() { Unsafe.AsRef(in _g).Dispose(); yield return 1; }
}

public static class RTA2
{
    public static Lock AsyncOnCopy()
    {
        var l = new Lock();
        var lease = new Lease(l.Enter());
        lease.DisposeAsync2().GetAwaiter().GetResult();
        lease.Dispose();
        return l;
    }
    public static Lock IteratorOnCopy()
    {
        var l = new Lock();
        var lease = new Lease(l.Enter());
        foreach (var _ in lease.DisposeLazily()) { }
        lease.Dispose();
        return l;
    }
    // Control: a non-async member does it in place, once.
    public static Lock Control()
    {
        var l = new Lock();
        var lease = new Lease(l.Enter());
        lease.Dispose();
        lease.Dispose();
        return l;
    }
}
