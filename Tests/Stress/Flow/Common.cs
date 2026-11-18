using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Singulink.CodeAnalysis;

// Every acquisition has a token, so that a second release of one is seen whatever else happens.
public sealed class Token { public int Releases; }

public sealed class Lock
{
    public readonly List<Token> Tokens = [];
    public Guard Enter() { var token = new Token(); Tokens.Add(token); return new Guard(token); }
    public Guard? TryEnter() => Enter();
    public (Guard G, int N) EnterPair() => (Enter(), 1);
    public Task<Guard> EnterTask() => Task.FromResult(Enter());
    public ValueTask<Guard> EnterValueTask() => new ValueTask<Guard>(Enter());
    public List<Guard> EnterList() { var list = new List<Guard>(); list.Add(Enter()); return list; }

    public bool AnyTwice
    {
        get
        {
            foreach (var token in Tokens)
            {
                if (token.Releases > 1)
                    return true;
            }

            return false;
        }
    }
}

[NonCopyable]
public struct Guard : IDisposable
{
    private Token? _token;
    public Guard(Token token) => _token = token;
    public readonly bool IsHeld => _token is not null;
    public void Dispose() { if (_token is { } token) { _token = null; token.Releases++; } }
}

public sealed class StepOnDispose : IDisposable
{
    public void Dispose() => Sink.Step();
}

public delegate Guard GFn(Lock l, Guard p, ref int c);
public delegate Task<int> AFn(Lock l, Guard p);
public delegate void RFn(Lock l, ref Guard q, ref int c);
public delegate void MFn(Lock l, [MovedFrom] ref Guard q, ref int c);
public delegate bool GBool(Lock l, Guard p);
public delegate Guard GMake(Lock l);

public static class Sink
{
    private static int s_cell;

    // A reference that is null in every other run: a test of what can throw through a reference.
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static ref int Cell() { if ((s_throwAt & 1) == 1) return ref Unsafe.NullRef<int>(); return ref s_cell; }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool Test(Lock l, GBool f) => f(l, l.Enter());

    private static int s_flags, s_flagCalls, s_steps, s_throwAt, s_fuel;
    private static readonly int[] s_two = [1, 2];
    public static int Total;
    public static long Calls, Odd;

    public static void Reset(int flags, int throwAt) { s_flags = flags; s_flagCalls = 0; s_steps = 0; s_throwAt = throwAt; s_fuel = 3; }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool Flag() { int call = s_flagCalls++; return (s_flags & (1 << (call % 7))) != 0; }

    // Bounds every loop and every backward jump.
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool Fuel() => s_fuel-- > 0;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Step() { if (++s_steps == s_throwAt) throw new InvalidOperationException(); }

    public static int Pick() => (Flag() ? 1 : 0) + (Flag() ? 2 : 0);
    public static int[] Two() => s_two;
    public static StepOnDispose Scope() => new();

    [MethodImpl(MethodImplOptions.NoInlining)] public static void Use(int v) => Total += v;
    [MethodImpl(MethodImplOptions.NoInlining)] public static void Use(bool v) => Total += v ? 1 : 0;

    // What takes a value releases it, and can fail afterwards.
    [MethodImpl(MethodImplOptions.NoInlining)] public static void Take(Guard guard) { guard.Dispose(); Step(); }
    [MethodImpl(MethodImplOptions.NoInlining)] public static int TakeI(Guard guard) { guard.Dispose(); Step(); return 1; }
    [MethodImpl(MethodImplOptions.NoInlining)] public static bool TakeTrue(Guard guard) { guard.Dispose(); Step(); return Flag(); }
    [MethodImpl(MethodImplOptions.NoInlining)] public static void TakeN(Guard? guard) { ReleaseN(ref guard); }
    [MethodImpl(MethodImplOptions.NoInlining)] public static void TakePair((Guard G, int N) pair) { pair.G.Dispose(); Step(); }
    [MethodImpl(MethodImplOptions.NoInlining)] public static void TakeValueTask(ValueTask<Guard> task) { Guard g = task.Result; g.Dispose(); Step(); }
    [MethodImpl(MethodImplOptions.NoInlining)] public static void Take2(Guard a, Guard b) { a.Dispose(); b.Dispose(); Step(); }

    // What uses a variable in place.
    [MethodImpl(MethodImplOptions.NoInlining)] public static void Release(ref Guard guard) { guard.Dispose(); Step(); }
    [MethodImpl(MethodImplOptions.NoInlining)] public static void Touch(ref Guard guard) { Step(); }
    [MethodImpl(MethodImplOptions.NoInlining)] public static bool Look(in Guard guard) { Step(); return Flag(); }
    [MethodImpl(MethodImplOptions.NoInlining)] public static void Make(Lock l, out Guard guard) { Step(); guard = l.Enter(); }
    [MethodImpl(MethodImplOptions.NoInlining)] public static void ReleaseN(ref Guard? guard) { Guard? taken = NonCopyable.Move(ref guard); if (taken is { } g) g.Dispose(); Step(); }
    [MethodImpl(MethodImplOptions.NoInlining)] public static void ReleasePair(ref (Guard G, int N) pair) { pair.G.Dispose(); Step(); }
    [MethodImpl(MethodImplOptions.NoInlining)] public static ref Guard Same(ref Guard guard) => ref guard;
    [MethodImpl(MethodImplOptions.NoInlining)] public static Span<Guard> SpanOf(ref Guard guard) => new Span<Guard>(ref guard);
    [MethodImpl(MethodImplOptions.NoInlining)] public static Guard Swap(ref Guard guard, Guard other) { Guard old = NonCopyable.Move(ref guard); guard = other; return old; }

    // Containers.
    [MethodImpl(MethodImplOptions.NoInlining)] public static void Drain(List<Guard> list) { foreach (ref Guard g in CollectionsMarshal.AsSpan(list)) g.Dispose(); Step(); }
    [MethodImpl(MethodImplOptions.NoInlining)] public static void Watch(Task<Guard> task) { Step(); }
    [MethodImpl(MethodImplOptions.NoInlining)] public static void Fill(Lock l, List<Guard> list) { list.Add(l.Enter()); Step(); }
    [MethodImpl(MethodImplOptions.NoInlining)] [return: Borrowed] public static Task<Guard> Borrow(Task<Guard> task) => task;
    public static async Task StepAsync() { Step(); await Task.Yield(); Step(); }
}
