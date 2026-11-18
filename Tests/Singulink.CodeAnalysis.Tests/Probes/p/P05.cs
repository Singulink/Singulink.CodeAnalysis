using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Singulink.CodeAnalysis;
using static Src;

ref struct RefHolder
{
    public ref Guard R;
    public RefHolder(ref Guard g) { R = ref g; }
    public Guard Take() => R;                                    // L01 report
}

class Box<T> { public T V = default!; public T Get() => V; public ref T Ref => ref V; }

class Holder
{
    public Guard F;
    public Guard this[int a, int b] { get => default; set { } }
    public ref Guard this[string s] => ref F;
    public event Action<Guard>? E;
    public void Raise(Guard g) => E?.Invoke(g);                  // L02 report? passing stored param
}

class P05
{
    Guard _f;
    Guard? _nf;
    readonly Guard _rf;
    (Guard, int) _tf;
    Holder _h = new();

    Guard Conds(bool c, Guard p, ref Guard r, in Guard i)
    {
        Guard a = c ? Enter() : Enter();                         // M01 OK
        Guard b = c ? _f : Enter();                              // M02 report
        ref Guard d = ref c ? ref _f : ref r;                    // M03 OK
        Guard e = c ? p : default;                               // M04 report
        Guard? f = _nf ?? TryEnter();                            // M05 report
        Guard? g = TryEnter() ?? TryEnter();                     // M06 OK
        Guard h = _nf ?? Enter();                                // M07 report
        Guard k = TryEnter() ?? Enter();                         // M08 OK
        _nf ??= Enter();                                         // M09 OK?
        Guard l = i;                                             // M10 report
        Guard m = r;                                             // M11 report
        Guard n = _rf;                                           // M12 report
        Guard o = _h[1, 2];                                      // M13 OK (checked prop)
        _h[1, 2] = _f;                                           // M14 report
        Guard q = _h["x"];                                       // M15 report
        ref Guard s = ref _h["x"];                               // M16 OK
        Guard t = c switch { true => _f, false => Enter() };     // M17 report
        Guard u = c switch { true => Enter(), false => Enter() };// M18 OK
        Guard v = (_tf).Item1;                                   // M19 report
        Guard w = MakeTuple().Item1;                             // M20 OK
        Guard x = new Holder().F;                                // M21 OK? (field of new class: class is reference -> stored?)
        Guard y = (Enter(), 1).Item1;                            // M22 OK
        Guard z = c ? throw new Exception() : _f;                // M23 report
        return c ? Enter() : p;                                  // M24 report? (p param stored)
    }
    static (Guard, int) MakeTuple() => default;

    void Tuples(Guard p)
    {
        var t1 = (Enter(), 1);                                   // N01 OK
        var t2 = (p, 1);                                         // N02 report
        var t3 = (_f, 1);                                        // N03 report
        (Guard a, int b) = MakeTuple();                          // N04 OK
        (Guard c, int d) = _tf;                                  // N05 report
        (Guard e, int f) = (_f, 1);                              // N06 report
        var anon = new { G = Enter() };                          // N07 report? anonymous type holds guard
        Guard g = anon.G;                                        // N08 report
        var t4 = (Enter(), new List<Guard>());                   // N09 OK
        var (h, i) = t1;                                         // N10 report
        (_f, var j) = MakeTuple();                               // N11 OK
        (_f, var k) = t1;                                        // N12 report
        _tf = (Enter(), 2);                                      // N13 OK
        _tf.Item1 = Enter();                                     // N14 OK
        ValueTuple<Guard> vt = new ValueTuple<Guard>(Enter());   // N15 OK
        ValueTuple<Guard> vt2 = new ValueTuple<Guard>(p);        // N16 report
        KeyValuePair<int, Guard> kv = new(1, Enter());           // N17 OK
        var kv2 = KeyValuePair.Create(1, p);                     // N18 report
        Guard l = kv.Value;                                      // N19 report
        kv.Deconstruct(out _, out Guard m);                      // N20 report
        var (o, q) = kv;                                         // N21 report
        var (r, s) = MakeKvp();                                  // N22 OK
    }
    static KeyValuePair<int, Guard> MakeKvp() => default;

    void Nullables()
    {
        Guard? a = TryEnter();
        if (a is { } x) { }                                      // O01 report
        if (a is Guard y) { }                                    // O02 report
        if (a.HasValue) { }                                      // O03 OK
        Guard z = a.Value;                                       // O04 report
        Guard w = a.GetValueOrDefault();                         // O05 report
        Guard v = (Guard)a;                                      // O06 report
        a?.Dispose();                                            // O07 report
        TryEnter()?.Dispose();                                   // O08 OK
        if (a is { IsHeld: true }) { }                           // O09 OK? looks in place
        if (a != null) { }                                       // O10 OK
        Guard? b = a;                                            // O11 report
        ref Guard r = ref Nullable.GetValueRefOrDefaultRef(ref a); // O12 compile? (ref readonly)
        if (TryEnter() is { } q) { }                             // O13 OK
        Guard u = a ?? default;                                  // O14 report
        Guard t = (a is { } t1) ? t1 : default;                  // O15 report
        bool h = a?.IsHeld ?? false;                             // O16 report? copy (spec says reported)
    }

    void Delegates(Box<Guard> box)
    {
        Func<Guard> f1 = Enter;                                  // P01 OK
        Func<Guard> f2 = box.Get;                                // P02 report
        Func<Guard> f3 = () => _f;                               // P03 report
        Func<Guard, Guard> f4 = g => g;                          // P04 OK
        Guard a = f4(Enter());                                   // P05 OK
        Guard b = f4(_f);                                        // P06 report
        Action<Guard> act = static g => g.Dispose();             // P07 OK
        act(_f);                                                 // P08 report
        Func<Guard[], Guard> f5 = Ext2.Pick;                     // P09 report? (generic method group)
        Func<Guard> f6 = new Func<Guard>(Enter);                 // P10 OK
        Func<Guard> f7 = f1;                                     // P11 OK
        Func<Guard> f8 = f1 + f1;                                // P12 ? two invocations, each new
        Guard c = f8();                                          // P13 OK
    }

    void Usings(Lock lk, SemaphoreSlim sem)
    {
        using (Guard g = Enter()) { }                            // Q01 OK
        using (Enter()) { }                                      // Q02 OK
        using var g2 = Enter();                                  // Q03 OK
        using (_f) { }                                           // Q04 report
        Guard g3 = Enter();
        using (g3) { }                                           // Q05 report? (local stored)
        lock (lk) { }                                            // Q06 OK
        using (lk.EnterScope()) { }                              // Q07 OK
        var sc = lk.EnterScope();
        sc.Dispose();                                            // Q08 OK
        var sc2 = sc;                                            // Q09 report
        Lock.Scope Scoped() => lk.EnterScope();                  // Q10 OK
    }

    async Task Awaits(ValueTask<Guard> vtp, Task<Guard> tp)
    {
        Guard a = await EnterAsync();                            // R01 OK
        Guard b = await EnterAsync().ConfigureAwait(false);      // R02 OK
        Guard c = await EnterTaskAsync().ConfigureAwait(false);  // R03 OK
        var stored = EnterTaskAsync();
        Guard d = await stored;                                  // R04 report
        Guard e = await vtp;                                     // R05 report
        Guard f = await tp;                                      // R06 report
        Guard g = await NonCopyable.Move(ref vtp);               // R07 OK
        Guard h = EnterAsync().Result;                           // R08 OK
        Guard i = await Task.Run(() => Enter());                 // R09 OK
        Guard j = await Task.Run(() => _f);                      // R10 report (lambda)
        Guard k = await EnterTaskAsync().WaitAsync(TimeSpan.Zero); // R11 OK
        Guard l = await EnterAsync().AsTask();                   // R12 OK
        var vt2 = EnterAsync();
        var vt3 = vt2;                                           // R13 report
        Guard m = await (true ? EnterAsync() : vtp);             // R14 report
        Guard n = await Task.FromResult(Enter());                // R15 OK
        Guard o = await tp.ConfigureAwait(false);                // R16 report
        var aw = EnterTaskAsync().GetAwaiter();
        Guard p = aw.GetResult();                                // R17 OK? view of a new task -> aw is a local (stored view)
        Guard q = aw.GetResult();                                // R18 two copies!
    }
}
static class Ext2 { public static T Pick<T>(T[] a) => a[0]; }
