using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Singulink.CodeAnalysis;

// A3: the awaitable is itself [NonCopyable] and is awaited from a local in place; the awaiter is still copied.
[NonCopyable]
public struct OwnedAwaitable
{
    private Guard _g;
    public OwnedAwaitable(Guard g) => _g = g;
    public ReserveAwaiter GetAwaiter() => new ReserveAwaiter(NonCopyable.Move(ref _g));
}

// A4: a [NonCopyable] awaiter with an ordinary guard field: copies seen in IsCompleted/OnCompleted/GetResult.
[NonCopyable]
public struct CountingAwaiter : INotifyCompletion
{
    private int _calls;
    public bool IsCompleted { get { _calls++; return false; } }
    public void OnCompleted(Action c) { _calls++; Task.Run(c); }
    public int GetResult() => ++_calls;
}
public readonly struct CountingAwaitable { public CountingAwaiter GetAwaiter() => default; }

public static class RTA3
{
    static async Task Run(Lock l) { var a = new OwnedAwaitable(l.Enter()); await a; }
    public static Lock OwnedAwaitableCopy() { var l = new Lock(); Run(l).GetAwaiter().GetResult(); return l; }
    public static Lock CountingCopy()
    {
        int n = Go().GetAwaiter().GetResult();
        Console.WriteLine($"  GetResult saw {n} calls on its copy; 3 if no copy");
        return new Lock();
        static async Task<int> Go() => await new CountingAwaitable();
    }
}
