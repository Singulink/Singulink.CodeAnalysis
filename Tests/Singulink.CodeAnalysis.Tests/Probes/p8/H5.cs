using System;
using Singulink.CodeAnalysis;

[NonCopyable]
public struct Counter
{
    public int Value;
    public void operator +=(int x) { Value += x; }
    public static Counter operator +(Counter c, int x) => new Counter { Value = c.Value + x };
}

public sealed class H5
{
    public Guard P1 { get => field; set => field = value; }               // F01 field-backed getter returns a copy
    public Guard P2 { get; set => field = value; }                        // F02 semi-auto
    public ref Guard P3 => ref _g;                                        // F03 clean
    private Guard _g;
    private readonly Counter _ro;
    private Counter _c;
    public RefHolder? H;

    public void N01(Lock l) { Guard g = l.Enter(); H?.G = g; }            // N01 null-conditional assignment at last use: clean
    public void N02(Lock l) { Guard g = l.Enter(); H?.G = g; g.Dispose(); } // N02 reported
    public void C01() { _ro += 1; }                                       // C01 instance compound op on readonly field: defensive copy? (compile error expected)
    public void C02() { _c += 1; }                                        // C02 clean
    public void C03() { var c = _c + 1; }                                 // C03 operator by value on stored: reported
    public static Lock RunF01() { var l = new Lock(); var h = new H5(); h.P1 = l.Enter(); h.P1.Dispose(); h.P1.Dispose(); return l; }
}

public static class RTH5 { public static Lock F01() => H5.RunF01(); }
