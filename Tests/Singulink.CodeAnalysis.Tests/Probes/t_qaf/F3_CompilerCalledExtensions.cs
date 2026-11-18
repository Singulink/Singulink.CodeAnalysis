using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

// F3: members of an extension block that the compiler calls take the receiver by value with no argument in the tree.
public static class GuardExtensionBlock
{
    extension(Guard guard)
    {
        public Empty GetEnumerator() { guard.Dispose(); return default; }
        public AsyncEmpty GetAsyncEnumerator(CancellationToken token = default) { guard.Dispose(); return default; }
        public TaskAwaiter GetAwaiter() { guard.Dispose(); return Task.CompletedTask.GetAwaiter(); }
        public void Deconstruct(out bool held, out int id) { held = guard.IsHeld; id = 0; guard.Dispose(); }
    }
}

// The classic form of Deconstruct, in a positional pattern
public readonly struct Token(int id)
{
    public int Id => id;
}

[Singulink.CodeAnalysis.NonCopyable]
public struct Lease
{
    private Guard _guard;
    public Lease(Guard guard) => _guard = guard;
    public void End() => _guard.Dispose();
    public void TakeOver() => _guard.Dispose();
}

public static class LeaseExtensions
{
    public static void Deconstruct(this Lease lease, out int a, out int b) { lease.TakeOver(); a = 0; b = 0; }
}

public struct Empty { public bool MoveNext() => false; public int Current => 0; }

public struct AsyncEmpty
{
    public int Current => 0;
    public ValueTask<bool> MoveNextAsync() => new(false);
    public ValueTask DisposeAsync() => default;
}

public static class RTF3
{
    public static Lock Foreach()
    {
        var l = new Lock();
        var g = l.Enter();
        foreach (var _ in g) { }                   // no diagnostic
        g.Dispose();
        return l;
    }

    public static Lock Await()
    {
        var l = new Lock();
        Run(l).GetAwaiter().GetResult();
        return l;

        static async Task Run(Lock l)
        {
            var g = l.Enter();
            await g;                               // no diagnostic
            g.Dispose();
        }
    }

    public static Lock AwaitForeach()
    {
        var l = new Lock();
        Run(l).GetAwaiter().GetResult();
        return l;

        static async Task Run(Lock l)
        {
            var g = l.Enter();
            await foreach (var _ in g) { }         // no diagnostic
            g.Dispose();
        }
    }

    public static Lock Deconstruction()
    {
        var l = new Lock();
        var g = l.Enter();
        var (held, id) = g;                        // no diagnostic
        g.Dispose();
        return l;
    }

    public static Lock PositionalPatternBlock()
    {
        var l = new Lock();
        var g = l.Enter();
        if (g is (true, _)) { }                    // no diagnostic
        g.Dispose();
        return l;
    }

    public static Lock PositionalPatternClassic()
    {
        var l = new Lock();
        var lease = new Lease(l.Enter());
        if (lease is (0, 0)) { }                   // no diagnostic; 'var (a, b) = lease;' is SCA1003
        lease.End();
        return l;
    }
}
