using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Singulink.CodeAnalysis;

[NonCopyable]
public struct Guard : IDisposable
{
    private int _state;
    public readonly bool IsHeld => _state != 0;
    public void Dispose() => _state = 0;

    public async ValueTask DisposeAsync() { await Task.Yield(); _state = 0; }   // D01
}

public sealed class RwLock
{
    public Guard Enter() => default;
    public ValueTask<Guard> EnterAsync() => default;
    public Task<Guard> EnterTaskAsync() => Task.FromResult(default(Guard));
}

public struct Holder { private Guard _guard; }                                 // D02

public sealed class Pool<[NonCopyable] T>
{
    private readonly T[] _items = new T[16];

    public ref T this[int i] => ref _items[i];                                  // D03
    public T Take(int i) => NonCopyable.Move(ref _items[i]);                    // D04
    public T Peek(int i) => _items[i];                                          // D05

    [RequiresCopyable]
    public T[] ToArray() => (T[])_items.Clone();                                // D06
}

public class Demo
{
    private Guard _held;
    private Task<Guard> _cached = null!;
    private readonly List<Guard> _guards = new();

    private static void ByValue(Guard g) { }
    private static void ByRef(ref Guard g) { }
    private static void ByIn(in Guard g) => g.Dispose();                        // D07
    private static T Dup<T>(in T value) => value;
    private static IEnumerable<int> Steps(Guard g) { yield return 1; }          // D08

    public void Values(RwLock rwLock)
    {
        Guard g = rwLock.Enter();                                               // V01
        Guard copy = g;                                                         // V02
        ByValue(g);                                                             // V03
        ByRef(ref g);                                                           // V04
        bool held = g.IsHeld;                                                   // V05
        object boxed = g;                                                       // V06
        IDisposable d = rwLock.Enter();                                         // V07
        Guard back = (Guard)d;                                                  // V08
        _held = NonCopyable.Move(ref g);                                        // V09
        Guard? maybe = rwLock.Enter();                                          // V10
        if (maybe is { } taken) { }                                             // V11
        if (NonCopyable.Move(ref maybe) is { } moved) { }                       // V12
    }

    public void Generics(RwLock rwLock, Pool<Guard> pool, Pool<int> numbers)
    {
        Guard dup = Dup(in _held);                                              // G01
        ref Guard slot = ref pool[0];                                           // G02
        Guard taken = pool.Take(1);                                             // G03
        Guard[] all = pool.ToArray();                                           // G04
        int[] ints = numbers.ToArray();                                         // G05
    }

    public void Collections(RwLock rwLock, Stack<Guard> stack, Span<Guard> span)
    {
        var list = new List<Guard>();
        list.Add(rwLock.Enter());                                               // C01
        Guard first = list[0];                                                  // C02
        ref Guard inPlace = ref CollectionsMarshal.AsSpan(list)[0];             // C03
        Guard[] copies = list.ToArray();                                        // C04
        Guard top = stack.Pop();                                                // C05
        foreach (ref Guard r in span) r.Dispose();                              // C06
        foreach (Guard c in span) { }                                           // C07
        object hidden = list;                                                   // C08
        IEnumerable untyped = list;                                             // C09
    }

    public async Task Tasks(RwLock rwLock)
    {
        using Guard a = await rwLock.EnterAsync();                              // T01
        ValueTask<Guard> pending = rwLock.EnterAsync();                         // T02
        ValueTask<Guard> alias = pending;                                       // T03
        using Guard b = await pending;                                          // T04
        using Guard c = await NonCopyable.Move(ref pending);                    // T05
        Task plain = rwLock.EnterTaskAsync();                                   // T06
    }

    public Task<Guard> Cached() => _cached;                                     // R01

    [return: Borrowed]
    public List<Guard> Guards() => _guards;                                     // R02

    public void Callers()
    {
        foreach (Guard g in Guards()) { }                                       // R03
        Guards().Add(default);                                                  // R04
    }
}
