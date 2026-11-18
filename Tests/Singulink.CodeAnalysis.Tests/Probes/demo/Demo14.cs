using System;
using Singulink.CodeAnalysis;

public sealed class HolderPc(Guard g) { private Guard _g = g; }                    // Q01 clean
public sealed class HolderPc2(Guard g) { private Guard _a = g; private Guard _b = g; }   // Q02 reported
public sealed class HolderPc3(Guard g) { private Guard _g = g; public void Use() => g.Dispose(); }   // Q03 reported

public class Round9
{
    private enum State { Free, Held }
    private Guard _held;
    private State _state;
    private static int s_count;
    private readonly RwLock _rwLock = new();

    static void Work() { }

    void Rethrow()
    {
        Guard g = _rwLock.Enter();
        try { Work(); }
        catch { _held = g; throw; }                       // Q04 reported
        finally { g.Dispose(); }
    }

    void InFinally()
    {
        Guard g = _rwLock.Enter();
        try { try { Work(); } finally { _held = g; } }    // Q05 reported
        catch { g.Dispose(); }
    }

    void Flag()
    {
        Guard g = _rwLock.Enter();
        try { Work(); _held = g; _state = State.Held; }   // Q06 clean
        catch { g.Dispose(); throw; }
    }

    void StaticFlag()
    {
        Guard g = _rwLock.Enter();
        try { Work(); _held = g; s_count = 1; }           // Q07 a static field: reported
        catch { g.Dispose(); throw; }
    }

    void Plain()
    {
        Guard g = _rwLock.Enter();
        try { Work(); _held = g; }                        // Q08 clean
        catch { g.Dispose(); throw; }
    }

    void ReturnThroughFinally()
    {
        Guard g = _rwLock.Enter();
        try { Work(); _held = g; return; }                // Q09 the finally still runs: reported
        finally { g.Dispose(); }
    }
}
