using System;
using System.Threading;
using System.Threading.Tasks;
using Singulink.CodeAnalysis;

// Where 'this' is, and false positives near the rules.
public class GBase { protected int BaseState; }

public sealed class G1 : GBase
{
    private Guard _held;
    private int _state;
    private volatile int _vol;
    private readonly int _ro = 1;
    private Thread? _owner;
    private int _ownerId;
    private static int s_state;
    private Guard _byField;

    static void Work() { }

    public void G01(Lock l) { Guard g = l.Enter(); try { Work(); _held = g; BaseState = 1; } catch { g.Dispose(); throw; } }          // G01 inherited field
    public void G02(Lock l) { Guard g = l.Enter(); try { Work(); _held = g; base.BaseState = 1; } catch { g.Dispose(); throw; } }     // G02 base.
    public void G03(Lock l) { Guard g = l.Enter(); try { Work(); _held = g; _vol = 1; } catch { g.Dispose(); throw; } }               // G03 volatile
    public void G04(Lock l) { Guard g = l.Enter(); try { Work(); _held = g; _state = _ro; } catch { g.Dispose(); throw; } }           // G04 readonly field read
    public void G05(Lock l) { Guard g = l.Enter(); try { Work(); _held = g; this._state = 1; } catch { g.Dispose(); throw; } }      // G05 this.
    public void G06(Lock l) { Action a = () => { Guard g = l.Enter(); try { Work(); _held = g; _state = 1; } catch { g.Dispose(); throw; } }; } // G06 lambda capturing this
    public void G07(Lock l) { void Local() { Guard g = l.Enter(); try { Work(); _held = g; _state = 1; } catch { g.Dispose(); throw; } } }    // G07 local function
    public G1(Lock l) { Guard g = l.Enter(); try { Work(); _held = g; _state = 1; } catch { g.Dispose(); throw; } }                  // G08 constructor
    public G1() { }
    public int P { get => _state; init { Guard g = default; try { Work(); _held = g; _state = value; } catch { g.Dispose(); throw; } } } // G09 init accessor
    public int Q { get; set { Guard g = default; try { Work(); _held = g; field = value; } catch { g.Dispose(); throw; } } }        // G10 field keyword
    public static void G11(Lock l) { Guard g = l.Enter(); try { Work(); Sink.Held = g; s_state = 1; } catch { g.Dispose(); throw; } } // G11 static: reported
    public void G12(Lock l, G1 other) { Guard g = l.Enter(); try { Work(); _held = g; other._state = 1; } catch { g.Dispose(); throw; } } // G12 other object: reported
    public void G13(Guard g) { try { Work(); _held = g; _state = 1; } catch { g.Dispose(); throw; } }                                  // G13 by-value parameter
    public void G14(Lock l) { var t = l.EnterTaskAsync(); try { Work(); _task = t; _state = 1; } catch { t.Result.Dispose(); throw; } } // G14 container local
    private Task<Guard>? _task;

    // false positives
    public Task F01(Lock l) { Guard g = l.Enter(); try { Work(); _held = g; return Task.CompletedTask; } catch { g.Dispose(); throw; } } // F01 return a cached task (property)
    public void F02(Lock l) { Guard g = l.Enter(); try { Work(); _held = g; _ownerId = Environment.CurrentManagedThreadId; } catch { g.Dispose(); throw; } } // F02
    public void F03(Lock l, bool c) { Guard g = l.Enter(); try { Work(); _held = g; _state = c ? 1 : 2; } catch { g.Dispose(); throw; } } // F03 conditional
    public void F04(Lock l) { Guard g = l.Enter(); try { Work(); _held = g; _state = (int)State.Held; } catch { g.Dispose(); throw; } }    // F04 cast of an enum constant
    public void F05(Lock l) { Guard g = l.Enter(); try { Work(); _held = g; s_state++; } catch { g.Dispose(); throw; } }                // F05 static counter
    public bool F06(Lock l) { Guard g = l.Enter(); try { Work(); _held = g; return true; } catch { g.Dispose(); throw; } }               // F06 return true
    public void F07(Lock l) { Guard g = l.Enter(); try { Work(); _byField = g; _held = default; } catch { g.Dispose(); throw; } }         // F07
    public void F08(Lock l) { Guard g = l.Enter(); try { Work(); _state = 1; _held = g; } catch { g.Dispose(); throw; } }               // F08 flag first
}

[NonCopyable]
public struct GS
{
    private Guard _held;
    private int _state;
    static void Work() { }
    public void S01(Lock l) { Guard g = l.Enter(); try { Work(); _held = g; _state = 1; } catch { g.Dispose(); throw; } }           // S01 struct: reported
}

public sealed record class GR
{
    private Guard _held;
    private int _state;
    static void Work() { }
    public void R01(Lock l) { Guard g = l.Enter(); try { Work(); _held = g; _state = 1; } catch { g.Dispose(); throw; } }           // R01 record class
}

public sealed class GG<T>
{
    private Guard _held;
    private T? _t;
    static void Work() { }
    public void X01(Lock l, T t) { Guard g = l.Enter(); try { Work(); _held = g; _t = t; } catch { g.Dispose(); throw; } }          // X01 generic field
    public void X02(Lock l) { Guard g = l.Enter(); try { Work(); _held = g; _t = default; } catch { g.Dispose(); throw; } }         // X02
}

public static class GExt
{
    extension(G1 self)
    {
        public void E01(Lock l) { Guard g = l.Enter(); try { Sink.Held = g; self.ToString(); } catch { g.Dispose(); throw; } }      // E01 extension: reported
    }
}
