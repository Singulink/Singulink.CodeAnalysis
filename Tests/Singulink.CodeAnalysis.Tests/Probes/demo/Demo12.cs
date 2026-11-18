using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Singulink.CodeAnalysis;

public class LastUse
{
    private Guard _held;
    private Action _act = null!;
    private readonly RwLock _rwLock = new();

    static void Consume(Guard g) { }

    void Simple()
    {
        Guard guard = _rwLock.Enter();
        _held = guard;                                    // L01 last use: clean
    }

    void UsedAgain()
    {
        Guard guard = _rwLock.Enter();
        Consume(guard);                                   // L02 used again below: reported
        guard.Dispose();
    }

    void InCatch()
    {
        Guard guard = _rwLock.Enter();
        try { Consume(guard); }                           // L03 the catch still uses it: reported
        catch { guard.Dispose(); }
    }

    void InLoop()
    {
        Guard guard = _rwLock.Enter();
        for (int i = 0; i < 2; i++) Consume(guard);       // L04 in a loop: reported
    }

    void Captured()
    {
        Guard guard = _rwLock.Enter();
        _act = () => guard.Dispose();
        Consume(guard);                                   // L05 captured: reported
    }

    void RefLocal()
    {
        Guard guard = _rwLock.Enter();
        ref Guard r = ref guard;
        _held = guard;                                    // L06 a reference outlives it: reported
        r.Dispose();
    }

    ValueTask<int> FastPath()
    {
        ValueTask<Guard> pending = _rwLock.EnterAsync();
        if (!pending.IsCompletedSuccessfully)
            return Wait(pending);                         // L07 last use on this path: clean
        using Guard held = pending.Result;                // L08 last use on this path: clean
        return new(1);
    }

    static async ValueTask<int> Wait(ValueTask<Guard> pending)
    {
        using Guard held = await pending.ConfigureAwait(false);   // L09 clean
        return 0;
    }

    async Task OwnedTask()
    {
        Task<Guard> pending = _rwLock.EnterTaskAsync();
        await Task.Yield();
        using Guard g = await pending;                    // L10 an owned task local at its last use: clean
    }

    async Task SharedTask()
    {
        Task<Guard> pending = _rwLock.EnterTaskAsync();
        Task<Guard> alias = pending;
        using Guard a = await pending;                    // L11 copied to another local: reported
        using Guard b = await alias;                      // L12 reported
    }

    void ListAndSpan()
    {
        var list = new List<Guard> { _rwLock.Enter() };
        Span<Guard> s = CollectionsMarshal.AsSpan(list);
        Guard copy = list[0];                             // L13 a span still reaches the storage: reported
        s[0].Dispose();
    }

    Guard Choose(bool c)
    {
        Guard a = _rwLock.Enter(), b = _rwLock.Enter();
        return c ? a : b;                                 // L14 clean
    }

    void TwiceRead()
    {
        ValueTask<Guard> pending = _rwLock.EnterAsync();
        Guard first = pending.Result;                     // L15 read again below: reported
        Guard second = pending.Result;                    // L16 last use: clean
    }
}

public static class MovedPaths
{
    static Guard Throwing() => throw new Exception();

    static void A([MovedFrom] ref Guard a, ref Guard b) { b = a; a = Throwing(); }            // L17 reported
    static void B([MovedFrom] ref Guard a, ref Guard b, ref Guard c) { b = a; c = default; a = default; }   // L18 reported
    static void C([MovedFrom] ref Guard a, ref Guard b) { b = a; a = default; }               // L19 clean
    static void D([MovedFrom] ref Guard a, bool reset) { Guard m = a; if (reset) a = default; Keep(ref m); }   // L20 reported
    static void Keep(ref Guard g) { }
}
