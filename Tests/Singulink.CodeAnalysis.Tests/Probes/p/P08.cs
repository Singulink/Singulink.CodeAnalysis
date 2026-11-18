using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Singulink.CodeAnalysis;
using static Src;

[NonCopyable]
struct Op
{
    public int V;
    public static Op operator +(Op a, int b) => new Op { V = a.V + b };
    public void operator +=(int b) => V += b;
    public static implicit operator int(Op o) => o.V;
    public static implicit operator Op(int v) => new Op { V = v };
    public static bool operator ==(Op a, Op b) => a.V == b.V;
    public static bool operator !=(Op a, Op b) => a.V != b.V;
    public override bool Equals(object? o) => false;
    public override int GetHashCode() => 0;
}
struct Wrap { public static implicit operator Wrap(Guard g) => default; }
interface IRel { void Release() { } }
[NonCopyable] struct RelG : IRel { }

class P08
{
    Op _op;
    Guard _f;

    void Ops(Op local)
    {
        Op a = _op + 1;                                          // Y01 report (copy into operator)
        _op += 1;                                                // Y02 OK (instance compound operator)
        int i = _op;                                             // Y03 report (copy into conversion)
        Wrap w = _f;                                             // Y04 report
        Wrap w2 = Enter();                                       // Y05 OK
        bool eq = _op == local;                                  // Y06 report
        Guard g2 = _f with { State = 1 };                        // Y07 report
        Guard g3 = Enter() with { State = 1 };                   // Y08 OK
        if (_f is var copy) { }                                  // Y09 report
        var s = _f switch { var x => x.State };                  // Y10 report
        if (_f is { State: 1 }) { }                              // Y11 OK
        if (_f is Guard named) { }                               // Y12 report
    }

    static void CallRel<[NonCopyable] T>(ref T t) where T : IRel => t.Release(); // Y20 (boxes for a DIM)
    static void CallDisp<[NonCopyable] T>(ref T t) where T : IDisposable => t.Dispose(); // Y21 OK

    // library side of an async lock
    readonly Queue<TaskCompletionSource<Guard>> _waiters = new();          // (no report: a field type)
    public ValueTask<Guard> EnterAsync()
    {
        var tcs = new TaskCompletionSource<Guard>(TaskCreationOptions.RunContinuationsAsynchronously);
        _waiters.Enqueue(tcs);                                   // Z01 unlisted? Queue<TCS<Guard>>.Enqueue listed
        return new ValueTask<Guard>(tcs.Task);                   // Z02 FP?
    }
    public Task<Guard> EnterTaskAsync()
    {
        var tcs = new TaskCompletionSource<Guard>();
        _waiters.Enqueue(tcs);
        return tcs.Task;                                         // Z03 OK
    }
    void Release()
    {
        if (_waiters.TryDequeue(out var next))                   // Z04 OK
            next.SetResult(Enter());                             // Z05 OK
    }
    public async ValueTask<Guard> EnterAsync2()
    {
        if (_f.State == 0) return Enter();                       // Z06 OK
        var tcs = new TaskCompletionSource<Guard>();
        _waiters.Enqueue(tcs);
        return await tcs.Task.ConfigureAwait(false);             // Z07 FP? (spec: stored)
    }
}
