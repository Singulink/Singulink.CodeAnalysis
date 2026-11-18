using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;
using Singulink.CodeAnalysis;

[NonCopyable]
public struct Guard : IDisposable
{
    private int _state;
    public bool IsHeld => _state != 0;
    public void Dispose() => _state = 0;
    public void Touch() => _state++;
}

[NonCopyable]
public struct G2 : ICloneable
{
    private int _n;
    public object Clone() => this;                          // D01 box of stored this
    public IDisposable? AsDisposable() => null;
}

public static class Owner
{
    public static Guard Enter() => default;
}

internal class Probe
{
    private object _box = Owner.Enter();                    // A01 allowed
    private IDisposable _disp = Owner.Enter();              // A02 allowed

    private void InPlace()
    {
        object box = Owner.Enter();                         // A03 allowed
        IDisposable d = Owner.Enter();                      // A04 allowed
        d.Dispose();                                        // A05 in place
        ((IDisposable)box).Dispose();                       // A06 in place
        _ = box.ToString();                                 // A07
        _ = box.Equals(box);                                // A08
        object alias = box;                                 // A09 reference copy
        bool isGuard = box is Guard;                        // A10 type test only
        ref Guard r = ref Unsafe.Unbox<Guard>(box);         // A11 in place
        r.Touch();                                          // A12
        using IDisposable u = Owner.Enter();                // A13
        _disp.Dispose();                                    // A14
        UseGeneric(d);                                      // A15 T is IDisposable
        Console.WriteLine("{0}", Owner.Enter());            // A16 box of a new value passed on
    }

    private void Copies(object box, IDisposable d, ValueType vt, object[] boxes, dynamic dyn, Guard stored)
    {
        Guard c01 = (Guard)box;                             // C01
        Guard c02 = (Guard)d;                               // C02
        Guard c03 = (Guard)vt;                              // C03
        ((Guard)box).Touch();                               // C04
        if (box is Guard c05) { }                           // C05
        if (box is Guard { IsHeld: true }) { }              // C06
        if (d is Guard { IsHeld: true } c07) { }            // C07
        Guard? c08 = box as Guard?;                         // C08
        Guard? c09 = (Guard?)box;                           // C09
        switch (box) { case Guard c10: break; }             // C10
        _ = box switch { Guard c11 => 1, _ => 0 };          // C11
        foreach (Guard c12 in boxes) { }                    // C12
        var c13 = boxes.Cast<Guard>();                      // C13
        var c14 = boxes.OfType<Guard>();                    // C14
        Guard c15 = Unbox<Guard>(box);                      // C15
        Guard c16 = UnboxMarked<Guard>(box);                // C16 (the body is where it should report)
        Guard c17 = dyn;                                    // C17
        dyn.Touch();                                        // C18 dynamic call on a box
        object c19 = RuntimeHelpers.GetObjectValue(box);    // C19 clones the box
        object c20 = stored;                                // C20 box of a stored value
        ((IDisposable)stored).Dispose();                    // C21 box of a stored value
        Guard c22 = (Guard)(object)Owner.Enter();           // C22 box and unbox a new value
        (object, int) t = (Owner.Enter(), 1);               // C23 allowed
        var c24 = ((Guard, int))t;                          // C24
        Guard c25 = Unsafe.Unbox<Guard>(box);               // C25 by-value read through the reference
        string c26 = $"{stored}";                           // C26
        string c27 = string.Format("{0}", stored);          // C27
        if (box is Guard { IsHeld: var held }) { }          // C28
        if (box is IDisposable and Guard c29) { }           // C29
        Guard c30 = (Guard)(box ?? d);                      // C30
        Guard c31 = box is Guard ? (Guard)box : default;    // C31
        var c32 = (Guard)Convert.ChangeType(box, typeof(Guard)); // C32
        object c33 = box switch { Guard => 1, _ => 0 };     // C33 type test only
    }

    private static T Unbox<T>(object o) => (T)o;

    private static T UnboxMarked<[NonCopyable] T>(object o) => (T)o;   // D02 unboxing to a marked type parameter

    private static T? UnboxMarked2<[NonCopyable] T>(object o) => o is T t ? t : default;   // D03

    private static void UseGeneric<T>(T value) where T : IDisposable => value.Dispose();
}

// Two value tasks from one source and token.
internal sealed class Source : IValueTaskSource<Guard>
{
    private ManualResetValueTaskSourceCore<Guard> _core;

    public ValueTask<Guard> EnterAsync() => new(this, _core.Version);                    // S01 expected to be allowed

    public (ValueTask<Guard>, ValueTask<Guard>) Twice() => (new(this, _core.Version), new(this, _core.Version));   // S02 two tasks, one result

    public Guard GetResult(short token) => _core.GetResult(token);                       // S03
    public ValueTaskSourceStatus GetStatus(short token) => _core.GetStatus(token);
    public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags) => _core.OnCompleted(continuation, state, token, flags);
}
