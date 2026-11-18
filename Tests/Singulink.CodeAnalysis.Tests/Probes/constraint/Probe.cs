using System;
using Singulink.CodeAnalysis;

public interface IGuard : IDisposable
{
    bool IsHeld { get; }
    void Release();
    static abstract IGuard Describe();
}

public interface IMake<TSelf> where TSelf : IMake<TSelf>
{
    static abstract TSelf Create();
}

[NonCopyable]
public struct Guard : IGuard, IMake<Guard>
{
    private int _state;
    public bool IsHeld => _state != 0;
    public void Release() => _state = 0;
    public void Dispose() => _state = 0;
    public static IGuard Describe() => null!;
    public static Guard Create() => default;
}

// Type parameter not marked: nothing in here is checked.
internal sealed class Unmarked<T> where T : IGuard
{
    private T _value = default!;
    public T Copy() => _value;
    public void Release() => _value.Release();
}

// Type parameter marked: the body is checked.
internal sealed class Marked<[NonCopyable] T> where T : IGuard, IMake<T>
{
    private T _value = default!;
    private readonly T _fixed = default!;

    public void InPlace(ref T byRef)
    {
        _value.Release();                          // P01 in place: fine
        byRef.Release();                           // P02 in place: fine
        bool held = _value.IsHeld;                 // P03 fine
        T made = T.Create();                       // P04 a new value: fine
        _value = T.Create();                       // P05 fine
    }

    public void Copies(in T byIn, T byValue)
    {
        T copy = _value;                           // P06 copy
        IGuard boxed = _value;                     // P07 boxes a copy
        ((IGuard)_value).Release();                // P08 boxes a copy
        IDisposable d = _value;                    // P09 boxes a copy
        Take(_value);                              // P10 passed as the interface: boxes a copy
        TakeGeneric(_value);                       // P11 by value to an unmarked generic
        if (_value is IDisposable disposable) { }  // P12 boxes a copy
        using (_value) { }                         // P13 disposes a copy
        byIn.Release();                            // P14 a call through `in`: defensive copy
        _fixed.Release();                          // P15 a call on a readonly field: defensive copy
        bool held = byIn.IsHeld;                   // P16 a read through `in`: defensive copy, harmless
        string? text = _value.ToString();          // P17
        object o = _value;                         // P18 boxes a copy
        byValue.Release();                         // P19 its own parameter: fine
    }

    public T Return() => _value;                   // P20 copy

    private static void Take(IGuard guard) { }
    private static void TakeGeneric<U>(U value) where U : IGuard { }
}

internal class Use
{
    private Guard _guard;

    private void Go(Unmarked<Guard> unmarked, Marked<Guard> marked)
    {
        var u = new Unmarked<Guard>();             // Q01 unlisted member of an unmarked generic
        unmarked.Release();                        // Q02
        Guard g = unmarked.Copy();                 // Q03
        var m = new Marked<Guard>();               // Q04 fine: marked
        marked.InPlace(ref _guard);                // Q05 fine
        Guard r = marked.Return();                 // Q06 fine at the caller; reported in the body
        Release(ref _guard);                       // Q07 fine: marked method
        ReleaseUnmarked(ref _guard);               // Q08 unmarked method
        IGuard ig = _guard;                        // Q09 boxes a copy
        IGuard ok = Guard.Create();                // Q10 fine: a new value moves into the box
    }

    private static void Release<[NonCopyable] T>(ref T value) where T : IGuard => value.Release();
    private static void ReleaseUnmarked<T>(ref T value) where T : IGuard => value.Release();
}
