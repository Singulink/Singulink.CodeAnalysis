using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Singulink.CodeAnalysis;

public sealed class Lock
{
    public int Acquires, Releases;
    public Guard Enter() { Acquires++; return new Guard(this); }
    public ValueTask<Guard> EnterAsync() { Acquires++; return new(new Guard(this)); }
    public Task<Guard> EnterTaskAsync() { Acquires++; return Task.FromResult(new Guard(this)); }
    public override string ToString() => $"acquires={Acquires} releases={Releases}";
}

[NonCopyable]
public struct Guard : IDisposable
{
    private Lock? _lock;
    public int Field;
    public Guard(Lock l) => _lock = l;
    public bool IsHeld => _lock is not null;
    public void Dispose() { if (_lock is { } l) { _lock = null; l.Releases++; } }
    public readonly void Peek() { }
}

public static class Sink
{
    public static Guard Held;
    public static List<Guard> HeldList = new();
    public static void Consume(Guard g) => g.Dispose();
    public static void Release(ref Guard g) => g.Dispose();
    public static void Look(in Guard g) { }
    public static void Touch(ref Guard g) { }
    public static int Zero(ref Guard g) => 0;
    public static T Echo<[NonCopyable] T>(T v) => v;
}

public sealed class AsyncLock
{
    public readonly Lock L = new();
    public readonly Queue<System.Threading.Tasks.TaskCompletionSource<Guard>> Waiters = new();

    public Task<Guard> Enter()
    {
        var source = new System.Threading.Tasks.TaskCompletionSource<Guard>();
        Waiters.Enqueue(source);
        return source.Task;
    }

    public void GrantAll()
    {
        while (Waiters.Count > 0)
        {
            var s = Waiters.Dequeue();
            s.TrySetResult(L.Enter());
        }
    }
}
