using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Singulink.CodeAnalysis;

public sealed class Stack2<[NonCopyable] T>
{
    private T[] _items = new T[16];
    private int _count;
    public void Push(T value) => _items[_count++] = value;                 // H01 clean
    public void Push2(T value) { if (_count == _items.Length) Grow(); _items[_count++] = value; }   // H02 clean
    public T Pop() => NonCopyable.Move(ref _items[--_count]);
    void Grow() { }
}

public sealed class Ordinary
{
    private readonly Lock _lock = new();
    private Guard _held;
    private Guard? _maybe;
    private readonly List<Guard> _list = new();
    static void Work() { }
    static bool TryEnter(out Guard g) { g = default; return true; }
    static (Guard guard, bool ok) TryEnterTuple() => (default, true);
    static Guard? TryEnterMaybe() => default;

    void H03() { Guard g = _lock.Enter(); _held = g; }                                       // H03 clean
    Guard H04() { Guard g = _lock.Enter(); Work(); return g; }                              // H04 clean
    ValueTask<int> H05()
    {
        ValueTask<Guard> pending = _lock.EnterAsync();
        if (!pending.IsCompletedSuccessfully) return WaitAsync(pending);                  // H05 clean
        using Guard g = pending.Result;                                                   // H06 clean
        return new(0);
    }
    static async ValueTask<int> WaitAsync(ValueTask<Guard> pending) { using Guard g = await pending; return 1; }   // H07 clean
    async Task H08() { ValueTask<Guard> pending = _lock.EnterAsync(); Work(); using Guard g = await pending; }      // H08 clean
    async Task H09() { Task<Guard> pending = _lock.EnterTaskAsync(); Work(); using Guard g = await pending; }      // H09 clean
    async Task H10() { Task<Guard> pending = _lock.EnterTaskAsync(); Work(); using Guard g = await pending.ConfigureAwait(false); }   // H10 clean
    void H11() { if (!TryEnter(out Guard g)) return; _held = g; }                           // H11 clean
    void H12() { Guard g = _lock.Enter(); try { Work(); } catch { g.Dispose(); throw; } _held = g; }   // H12 clean
    void H13() { Guard g = _lock.Enter(); try { Work(); _held = g; } catch { g.Dispose(); throw; } }   // H13 FP? commit at end of try
    void H14() { Guard? maybe = TryEnterMaybe(); if (maybe is { } g) _held = g; }           // H14 clean
    void H15() { Guard? maybe = TryEnterMaybe(); if (maybe.HasValue) _held = maybe.Value; } // H15 clean
    void H16() { var (guard, ok) = TryEnterTuple(); if (ok) _held = guard; }                // H16 clean
    void H17() { var r = TryEnterTuple(); if (r.ok) _held = r.guard; }                      // H17 clean
    void H18() { Guard g = _lock.Enter(); _list.Add(g); }                                    // H18 clean
    void H19(Guard g) => _held = g;                                                          // H19 clean
    Holder3 H20() { Guard g = _lock.Enter(); return new Holder3(g); }                          // H20 clean
    void H21(int k) { Guard g = _lock.Enter(); switch (k) { case 0: _held = g; break; case 1: g.Dispose(); break; default: _maybe = g; break; } }   // H21 clean
    void H22() { Guard g = _lock.Enter(); _maybe = g; }                                      // H22 clean (wrap in nullable)
    void H23() { Guard g = _lock.Enter(); IDisposable d = g; d.Dispose(); }                 // H23 clean (box at last use)
    async Task<int> H24() { ValueTask<Guard> vt = _lock.EnterAsync(); if (vt.IsCompleted) { using var a = vt.Result; return 1; } using var b = await vt; return 2; }   // H24 clean
    List<Guard> H25() { var list = new List<Guard>(); list.Add(_lock.Enter()); foreach (Guard g in list) { } return list; }   // H25 (iterates then returns: reported)
    void H26() { var list = new List<Guard>(); list.Add(_lock.Enter()); foreach (Guard g in list) _held = g; }   // H26 clean (last use)
    Guard H27(bool c) { Guard g = _lock.Enter(); if (c) { Work(); } return g; }             // H27 clean
    void H28() { Guard g = _lock.Enter(); for (int i = 0; i < 3; i++) Work(); _held = g; }   // H28 clean
    void H29() { for (int i = 0; i < 3; i++) { Guard g = _lock.Enter(); _list.Add(g); } }    // H29 clean
    void H30() { Guard g = _lock.Enter(); Action a = () => Work(); a(); _held = g; }        // H30 clean
    void H31() { Guard g = _lock.Enter(); if (g.IsHeld) _held = g; else g.Dispose(); }       // H31 clean
    void H32() { Guard g = _lock.Enter(); using (var other = _lock.Enter()) { Work(); } _held = g; }   // H32 clean
    async Task H33() { Guard g = _lock.Enter(); await Task.Yield(); _held = g; }             // H33 clean
    void H34() { var t = (_lock.Enter(), _lock.Enter()); _held = t.Item1; _maybe = t.Item2; }   // H34 FP? two parts
    void H35() { Guard g = _lock.Enter(); g.Peek(); _held = g; }                             // H35 clean
    void H36() { Guard g = _lock.Enter(); Sink.Look(in g); _held = g; }                      // H36 clean
    void H37() { Guard g = _lock.Enter(); try { _held = g; } finally { Work(); } }           // H37 clean
    void H38() { Guard g = _lock.Enter(); lock (_list) { _held = g; } }                      // H38 clean
}

[NonCopyable] public struct Holder2 { private Guard _g; public Holder2(Guard g) { _g = g; } }   // H39 clean
public sealed class Holder3 { private Guard _g; public Holder3(Guard g) => _g = g; }              // H40 clean
