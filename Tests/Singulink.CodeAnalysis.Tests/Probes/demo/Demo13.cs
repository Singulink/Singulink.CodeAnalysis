using System;
using System.Threading;
using System.Threading.Tasks;
using Singulink.CodeAnalysis;

[NonCopyable] public struct Lease { public Guard Inner; public int Id; }

public class Round8
{
    private Guard _held;
    private readonly RwLock _rwLock = new();
    public Guard Prop { get; set; }

    static bool Log(Guard g) => false;
    static void Work() { }
    Lease MakeLease() => default;

    void Patterns()
    {
        Lease x = MakeLease();
        if (x is { Inner: var g } whole) { }              // R01 two variables for one value: reported
        Lease y = MakeLease();
        if (y is { Inner: var h }) { }                    // R02 one variable: clean
        Guard z = _rwLock.Enter();
        if (z is var a and var b) { }                     // R03 reported
    }

    void Filter()
    {
        Guard g = _rwLock.Enter();
        try { Work(); }
        catch when (Log(g)) { }                           // R04 the next catch still uses g: reported
        catch { g.Dispose(); }
    }

    void Commit()
    {
        Guard g = _rwLock.Enter();
        try { Work(); _held = g; }                        // R05 clean
        catch { g.Dispose(); throw; }
    }

    void CommitThenWork()
    {
        Guard g = _rwLock.Enter();
        try { _held = g; Work(); }                        // R06 something can throw after the store: reported
        catch { g.Dispose(); throw; }
    }

    void CommitToSetter()
    {
        Guard g = _rwLock.Enter();
        try { Work(); Prop = g; }                         // R07 a setter is a call: reported
        catch { g.Dispose(); throw; }
    }

    void CommitWithFinally()
    {
        Guard g = _rwLock.Enter();
        try { Work(); _held = g; }                        // R08 the finally always runs: reported
        finally { g.Dispose(); }
    }

    static Task<Guard> Dup([MovedFrom] ref Task<Guard> t)
    {
        var m = t;
        var n = Interlocked.Exchange(ref t, null!);       // R09 a second read: reported
        t = n;
        return m;
    }

    static Guard Concat([MovedFrom] ref Guard a, object o)
    {
        Guard m = a;
        string s = "x" + o;                               // R10 runs ToString: reported
        a = default;
        return m;
    }
}
