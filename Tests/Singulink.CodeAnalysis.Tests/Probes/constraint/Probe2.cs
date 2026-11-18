using System;
using Singulink.CodeAnalysis;

[NonCopyable]
public struct G2 : IDisposable
{
    private int _state;
    public readonly bool IsHeld => _state != 0;
    public bool IsHeldMutable => _state != 0;
    public void Release() => _state = 0;
    public readonly void Peek() { }
    public void Dispose() => _state = 0;
}

internal class Concrete
{
    private G2 _value;
    private readonly G2 _fixed;

    private void Go(in G2 byIn, ref readonly G2 byRefReadonly)
    {
        if (_value is IDisposable disposable) { }      // R01 boxes a copy through a pattern
        IDisposable? d = _value as IDisposable;        // R02 boxes a copy through `as`
        byIn.Release();                                // R03 non-readonly call through `in`: defensive copy
        _fixed.Release();                              // R04 non-readonly call on a readonly field: defensive copy
        byRefReadonly.Release();                       // R05 same through `ref readonly`
        byIn.Peek();                                   // R06 readonly member: in place, fine
        bool a = byIn.IsHeld;                          // R07 readonly property: fine
        bool b = byIn.IsHeldMutable;                   // R08 non-readonly property: defensive copy
        _value.Release();                              // R09 fine
        if (_value is { IsHeld: true }) { }            // R10 a property pattern on a stored value
        ref readonly G2 local = ref _value;
        local.Release();                               // R11 through a ref readonly local
    }
}

internal sealed class MarkedT<[NonCopyable] T> where T : IDisposable
{
    private T _value = default!;

    private void Go(ref readonly T byRefReadonly)
    {
        IDisposable? d = _value as IDisposable;        // R12
        byRefReadonly.Dispose();                       // R13
        if (_value is IDisposable) { }                 // R14 type test only: fine
    }
}
