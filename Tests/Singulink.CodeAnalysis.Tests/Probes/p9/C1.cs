using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Singulink.CodeAnalysis;

// What may follow the store, in an instance member of a class.
public struct Money
{
    public int V;
    public static implicit operator Money(int v) => v < 0 ? throw new InvalidOperationException("conv") : new Money { V = v };
    public static Money operator +(Money a, Money b) => throw new InvalidOperationException("op");
    public static Money operator ++(Money a) => throw new InvalidOperationException("op++");
    public static bool operator true(Money a) => throw new InvalidOperationException("true");
    public static bool operator false(Money a) => throw new InvalidOperationException("false");
}

public struct Plain { public int X; }
public sealed class Box { public int X; }
public enum State { Free, Held }

[InlineArray(4)]
public struct Buf4 { private int _e; }

public sealed class C1
{
    public Guard Held, Held2;
    private int _n = -1, _i = 7, _state, _zero;
    private int? _ni, _nj;
    private long _l;
    private uint _u;
    private double _d;
    private decimal _m;
    private Index _idx;
    private Range _range;
    private Money _money;
    private (long, Money) _tm;
    private object? _obj;
    private string _s1 = "a", _s2 = "b", _str = "";
    private Plain _plain;
    private Box? _box;
    private Buf4 _buf;
    private State _st;
    private IComparable? _cmp;
    private (int, int) _pair;
    private C1? _self;
    private Type? _type;
    private int[] _arr = new int[1];
    private object[] _oarr = new string[1];
    private nint _nint;
    private Action? _act;

    static void Work() { }

    public void A01(Lock l) { Guard g = l.Enter(); try { Work(); Held = g; _idx = ^_n; } catch { g.Dispose(); throw; } }              // A01 ^n after the store (Index ctor throws for n < 0)
    public void A02(Lock l) { Guard g = l.Enter(); try { Work(); Held = g; _tm = (1L, _n); } catch { g.Dispose(); throw; } }         // A02 tuple literal with a user-defined element conversion
    public void A03(Lock l) { Guard g = l.Enter(); try { Work(); Held = g; _money = _n; } catch { g.Dispose(); throw; } }            // A03 user-defined conversion
    public void A04(Lock l) { Guard g = l.Enter(); try { Work(); Held = g; _money++; } catch { g.Dispose(); throw; } }               // A04 user-defined ++
    public void A05(Lock l) { Guard g = l.Enter(); try { Work(); Held = g; _money += _money; } catch { g.Dispose(); throw; } }       // A05 user-defined compound
    public void A06(Lock l) { Guard g = l.Enter(); try { Work(); Held = g; _obj = _i; } catch { g.Dispose(); throw; } }              // A06 boxing
    public void A07(Lock l) { Guard g = l.Enter(); try { Work(); Held = g; _str = _s1 + _s2; } catch { g.Dispose(); throw; } }       // A07 string concat
    public void A08(Lock l) { Guard g = l.Enter(); try { Work(); Held = g; _state++; } catch { g.Dispose(); throw; } }               // A08 field ++
    public void A09(Lock l) { Guard g = l.Enter(); try { Work(); Held = g; _buf[_i] = 1; } catch { g.Dispose(); throw; } }          // A09 inline array element, index out of range
    public void A10(Lock l) { Guard g = l.Enter(); try { Work(); Held = g; _plain.X = 1; } catch { g.Dispose(); throw; } }          // A10 field of a struct field of this
    public void A11(Lock l) { Guard g = l.Enter(); try { Work(); Held = g; _box!.X = 1; } catch { g.Dispose(); throw; } }           // A11 field of a class field (null)
    public void A12(Lock l) { Guard g = l.Enter(); try { Work(); Held = g; _plain = new Plain(); } catch { g.Dispose(); throw; } }  // A12 new of a struct
    public void A13(Lock l) { Guard g = l.Enter(); try { Work(); Held = g; _cmp = _i; } catch { g.Dispose(); throw; } }              // A13 boxing to an interface
    public void A14(Lock l) { Guard g = l.Enter(); try { Work(); Held = g; _range = _i.._n; } catch { g.Dispose(); throw; } }        // A14 range
    public void A15(Lock l) { Guard g = l.Enter(); try { Work(); Held = g; _d = _d + _i; } catch { g.Dispose(); throw; } }           // A15 double arithmetic
    public void A16(Lock l) { Guard g = l.Enter(); try { Work(); Held = g; _m = _i; } catch { g.Dispose(); throw; } }                // A16 int to decimal
    public void A17(Lock l) { Guard g = l.Enter(); try { Work(); Held = g; _m = _m + 1; } catch { g.Dispose(); throw; } }            // A17 decimal arithmetic (can overflow)
    public void A18(Lock l) { Guard g = l.Enter(); try { Work(); Held = g; _u = (uint)_i; } catch { g.Dispose(); throw; } }          // A18 explicit numeric, unchecked
    public void A19(Lock l) { Guard g = l.Enter(); try { Work(); Held = g; _u = checked((uint)_n); } catch { g.Dispose(); throw; } } // A19 checked explicit numeric
    public void A20(Lock l) { Guard g = l.Enter(); try { Work(); Held = g; _i = checked(_i + int.MaxValue); } catch { g.Dispose(); throw; } } // A20 checked add
    public void A21(Lock l) { Guard g = l.Enter(); try { Work(); Held = g; _i = _i / _zero; } catch { g.Dispose(); throw; } }        // A21 division
    public void A22(Lock l) { Guard g = l.Enter(); try { Work(); Held = g; _ni = _nj + _i; } catch { g.Dispose(); throw; } }         // A22 lifted add
    public void A23(Lock l) { Guard g = l.Enter(); try { Work(); Held = g; _st = State.Held; } catch { g.Dispose(); throw; } }       // A23 enum constant
    public void A24(Lock l) { Guard g = l.Enter(); try { Work(); Held = g; _st = (State)_i; } catch { g.Dispose(); throw; } }        // A24 explicit enum conversion
    public void A25(Lock l) { Guard g = l.Enter(); try { Work(); Held = g; _pair = (_i, _n); } catch { g.Dispose(); throw; } }      // A25 tuple of ints
    public void A26(Lock l) { Guard g = l.Enter(); try { Work(); Held = g; _self = this; } catch { g.Dispose(); throw; } }           // A26 this
    public void A27(Lock l) { Guard g = l.Enter(); try { Work(); Held = g; _type = typeof(C1); } catch { g.Dispose(); throw; } }    // A27 typeof
    public void A28(Lock l) { Guard g = l.Enter(); try { Work(); Held = g; _i = sizeof(long); } catch { g.Dispose(); throw; } }      // A28 sizeof
    public void A29(Lock l) { Guard g = l.Enter(); try { Work(); Held = g; _i = nameof(C1).Length; } catch { g.Dispose(); throw; } } // A29 nameof constant
    public void A30(Lock l) { Guard g = l.Enter(); try { Work(); Held = g; _arr = _arr; } catch { g.Dispose(); throw; } }            // A30 reference field copy
    public void A31(Lock l) { Guard g = l.Enter(); try { Work(); Held = g; _oarr[0] = _obj; } catch { g.Dispose(); throw; } }       // A31 covariant array store
    public void A32(Lock l) { Guard g = l.Enter(); try { Work(); Held = g; _obj = default; } catch { g.Dispose(); throw; } }         // A32 default
    public void A33(Lock l) { Guard g = l.Enter(); try { Work(); Held = g; _nint = _i; } catch { g.Dispose(); throw; } }             // A33 int to nint
    public void A34(Lock l) { Guard g = l.Enter(); try { Work(); Held = g; _l = _i << _n; } catch { g.Dispose(); throw; } }          // A34 shift
    public void A35(Lock l) { Guard g = l.Enter(); try { Work(); Held = g; _i = -_i; } catch { g.Dispose(); throw; } }               // A35 unary minus
    public void A36(Lock l) { Guard g = l.Enter(); try { Work(); Held = g; _act = null; } catch { g.Dispose(); throw; } }            // A36 null to a delegate field
    public void A37(Lock l) { Guard g = l.Enter(); try { Work(); Held = g; _i = _arr.Length; } catch { g.Dispose(); throw; } }       // A37 array length
    public void A38(Lock l) { Guard g = l.Enter(); try { Work(); Held = g; _st = _obj is int ? State.Held : State.Free; } catch { g.Dispose(); throw; } } // A38 conditional splits (FP)
    public void A39(Lock l) { Guard g = l.Enter(); try { Work(); Held = g; _i = _obj is int x ? 1 : 0; } catch { g.Dispose(); throw; } } // A39
    public void A40(Lock l) { Guard g = l.Enter(); try { Work(); Held = g; _st = State.Held; return; } catch { g.Dispose(); throw; } } // A40 return
    public int A41(Lock l) { Guard g = l.Enter(); try { Work(); Held = g; return _i; } catch { g.Dispose(); throw; } }              // A41 return a field
    public Money A42(Lock l) { Guard g = l.Enter(); try { Work(); Held = g; return _n; } catch { g.Dispose(); throw; } }            // A42 return through a user-defined conversion
    public object A43(Lock l) { Guard g = l.Enter(); try { Work(); Held = g; return _i; } catch { g.Dispose(); throw; } }           // A43 return boxed
    public Index A44(Lock l) { Guard g = l.Enter(); try { Work(); Held = g; return ^_n; } catch { g.Dispose(); throw; } }           // A44 return ^n
    public void A45(Lock l) { Guard g = l.Enter(); try { Work(); Held = g; if (_money) _i = 1; } catch { g.Dispose(); throw; } }    // A45 operator true in the branch
    public void A46(Lock l) { Guard g = l.Enter(); try { Work(); Held = g; if (_i > 0) return; } catch { g.Dispose(); throw; } }    // A46 branch on a field, both ways leave
    public void A47(Lock l) { Guard g = l.Enter(); try { Work(); Held = g; _i = (int)_l; } catch { g.Dispose(); throw; } }           // A47 explicit long to int
    public void A48(Lock l) { Guard g = l.Enter(); try { Work(); Held = g; _i = (int)_obj!; } catch { g.Dispose(); throw; } }        // A48 unboxing
    public void A49(Lock l) { Guard g = l.Enter(); try { Work(); Held = g; _i = _i % _zero; } catch { g.Dispose(); throw; } }        // A49 remainder
    public void A50(Lock l) { Guard g = l.Enter(); try { Work(); Held = g; _d = _l; } catch { g.Dispose(); throw; } }                // A50 long to double
    public void A51(Lock l) { Guard g = l.Enter(); try { Work(); Held = g; _box = null; } catch { g.Dispose(); throw; } }            // A51 null to a class field
    public void A52(Lock l) { Guard g = l.Enter(); try { Work(); Held = g; Held2 = default; } catch { g.Dispose(); throw; } }       // A52 default guard
    public void A53(Lock l) { Guard g = l.Enter(); try { Work(); Held = g; _i = _ni!.Value; } catch { g.Dispose(); throw; } }       // A53 .Value

    public static Lock R(Action<C1, Lock> m, Action<C1>? setup = null)
    {
        var c = new C1();
        setup?.Invoke(c);
        var l = new Lock();
        try { m(c, l); } catch { }
        c.Held.Dispose();
        return l;
    }
}

public static class RTC1
{
    public static Lock A01() => C1.R((c, l) => c.A01(l));
    public static Lock A09() => C1.R((c, l) => c.A09(l));
    public static Lock A44() => C1.R((c, l) => c.A44(l));
    public static Lock A02() => C1.R((c, l) => c.A02(l));
}
