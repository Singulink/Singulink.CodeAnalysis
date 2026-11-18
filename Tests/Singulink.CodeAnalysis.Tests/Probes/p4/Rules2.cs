using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Singulink.CodeAnalysis;

[NonCopyable]
public struct RG : IDisposable
{
    private int _s;
    public readonly bool IsHeld => _s != 0;
    public void Dispose() => _s = 0;
    public void Touch() => _s++;
    public readonly int Peek() => _s;
    public static RG Create() => default;
    public static RG operator +(RG a, int b) => a;                            // L01 returns by-value param: OK
    public static bool operator ==(in RG a, in RG b) => a._s == b._s;         // L02 OK
    public static bool operator !=(in RG a, in RG b) => !(a == b);           // L03 OK
    public static bool operator true(RG a) => a._s != 0;                      // L04
    public static bool operator false(RG a) => a._s == 0;
    public override readonly bool Equals(object? o) => false;
    public override readonly int GetHashCode() => 0;
    public void operator +=(int x) => _s += x;                                // L05 C# 14 instance compound op
}

public struct Holder2
{
    public RG G;                                                              // L06 SCA1010 expected
}

[NonCopyable]
public struct Outer
{
    public RG Inner;
    public readonly void Bad() => Inner.Touch();                              // L07 SCA1006
    public readonly bool Good() => Inner.IsHeld;                              // L08 OK
    public async Task DoAsync() { await Task.Yield(); Inner.Touch(); }        // L09 SCA1014
    public IEnumerable<int> Iter() { yield return Inner.Peek(); }             // L10 SCA1014
}

[NonCopyable]
public readonly struct RoGuard
{
    private readonly object? _o;
    public async Task DoAsync() { await Task.Yield(); }                       // L11 OK: readonly struct
    public IEnumerable<int> Iter() { yield return 1; }                        // L12 OK
}

static class RGExt
{
    extension(RG g)
    {
        public bool HeldX => g.IsHeld;                                        // L13
        public void TouchX() => g.Touch();
    }
    extension(ref RG g)
    {
        public void TouchRef() => g.Touch();
    }
    extension(in RG g)
    {
        public bool HeldIn => g.IsHeld;
        public void TouchIn() => g.Touch();                                   // L14 SCA1006 expected
    }
    public static void Classic(this RG g) { }
    public static void ClassicIn(this in RG g) { }
}

interface IRel { void Release(); void Other() { } }
[NonCopyable] struct DimStruct : IRel { public void Release() { } }           // L15 SCA1013 expected
struct GenDim<T> : IRel { T _v; public void Release() { } }
static class DimUse
{
    static void Call<T>(ref T x) where T : IRel => x.Other();
    static void Call2<[NonCopyable] T>(ref T x) where T : IRel => x.Other();
    static void Use()
    {
        var g = new GenDim<RG>();
        Call2(ref g);                                                         // L16 SCA1013 at binding expected
        Call(ref g);                                                          // L17 SCA1020 (unmarked)
    }
}

class RC<[NonCopyable] T> { [RequiresCopyable] public RC() { } }
static class NewUse
{
    static T Make<[NonCopyable] T>() where T : new() => new T();
    static void Use() { var b = Make<RC<RG>>(); }                             // L18 SCA1021
}

// Constraints that name a type parameter
static class Cn
{
    static void A<[NonCopyable] T, U>(ref T x) where T : IEnumerable<U> { }
    static void B<[NonCopyable] T, [NonCopyable] U>(ref T x) where T : IEnumerable<U> { }
    static void C<[NonCopyable] T, U>(ref T x) where T : U { }
    static void Use()
    {
        var l = new List<RG>();
        A<List<RG>, RG>(ref l);                                               // L19 SCA1020/1023?
        B<List<RG>, RG>(ref l);                                               // L20 ? IEnumerable<RG> keeps visible: OK
        C<List<RG>, object>(ref l);                                           // L21 SCA1023 expected
        C<List<RG>, IEnumerable<RG>>(ref l);                                  // L22 ? U unmarked bound to container -> SCA1023
    }
}

class RO
{
    private readonly RG _ro;
    private RG _rw;
    private RG? _maybe;
    private readonly Outer _outer;
    void M(in RG p, ref readonly RG q)
    {
        p.Touch();                                                            // M01 SCA1006
        bool h = p.IsHeld;                                                    // M02 OK
        q.Touch();                                                            // M03 SCA1006
        _ro.Touch();                                                          // M04 SCA1006
        _outer.Inner.Touch();                                                 // M05 SCA1006
        bool b = _outer.Inner.IsHeld;                                         // M06 OK
        _maybe?.Touch();                                                      // M07 SCA1003
        bool? mh = _maybe?.IsHeld;                                            // M08 OK
        if (_maybe is { IsHeld: true }) { }                                   // M09 OK
        _rw.HeldX.ToString();                                                 // M10 SCA1003 (by-value receiver)
        _rw.TouchX();                                                         // M11 SCA1003
        _rw.TouchRef();                                                       // M12 OK
        _rw.TouchIn();                                                        // M13 SCA1006? (in extension receiver of rw field: copy happens inside)
        bool hi = _rw.HeldIn;                                                 // M14 OK
        _rw.Classic();                                                        // M15 SCA1003
        _rw.ClassicIn();                                                      // M16 OK
        RG.Create().TouchX();                                                 // M17 OK (new)
        var s = _rw + 1;                                                      // M18 SCA1003 (stored operand by value)
        var s2 = RG.Create() + 1;                                             // M19 OK
        bool eq = _rw == _ro;                                                 // M20 OK (in operands)
        if (_rw) { }                                                          // M21 SCA1003 (operator true by value)
        _rw += 1;                                                             // M22 OK (instance compound op, in place)
        ref readonly RG rr = ref _rw;
        rr.Touch();                                                           // M23 SCA1006
        object box = RG.Create();
        if (box is RG { IsHeld: true }) { }                                   // M24 OK
        if (box is RG { }) { }                                                // M25 OK?
        object o2 = _rw;                                                      // M26 SCA1004
        (true ? ref _rw : ref _rw).Touch();                                   // M27 OK ref conditional
        (true ? _rw : _rw).Touch();                                           // M28 SCA1006 (or 1001)
    }
}

static class RGFactory { }
public static class RGEx2 { }
partial struct RGHelper { }
static class RGCreate { }
