using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Singulink.CodeAnalysis;
using static Src;

class P01
{
    private Task<Guard>? _a;
    private Task<Guard>? _b;
    private Guard _gf;

    // 'new' entries handing out a container that someone else still holds
    async Task NewContainer()
    {
        _a = EnterTaskAsync();
        _b = _a;
        Guard g1 = await Interlocked.Exchange(ref _a, null)!;   // A01 hole? second holder _b
        Guard g2 = await Interlocked.Exchange(ref _b, null)!;   // A02 hole? two guards from one task

        var t = EnterTaskAsync();
        var s1 = new Stack<Task<Guard>>();
        var s2 = new Stack<Task<Guard>>();
        s1.Push(t);                                              // A03 container pushed twice
        s2.Push(t);                                              // A04
        Guard g3 = await s1.Pop();                               // A05 hole?
        Guard g4 = await s2.Pop();                               // A06 hole?

        var q = new Queue<List<Guard>>();
        var l = CreateList(); l.Add(Enter());
        q.Enqueue(l);
        Guard g5 = q.Dequeue()[0];                               // A07 hole? l still holds it
        Guard g6 = l[0];                                         // A08 reported (stored)
    }

    // captured local returned / assigned by the outer method
    Guard CapturedReturned()
    {
        Guard g = Enter();
        Action a = () => g.Dispose();
        _act = a;
        return g;                                                // A10 copy: closure keeps g
    }
    Action? _act;

    Guard CapturedLocalFunction()
    {
        Guard g = Enter();
        void Local() => g.Dispose();
        _act = Local;
        return g;                                                // A11 copy
    }

    IEnumerable<int> Iter()
    {
        Guard g = Enter();
        _gf = g;                                                 // A12 reported (copy of local)
        yield return 1;
    }

    async Task<Guard> AsyncReturnLocal()
    {
        Guard g = Enter();
        await Task.Yield();
        return g;                                                // A13 OK
    }

    async Task<Guard> AsyncReturnUsing()
    {
        using Guard g = Enter();
        await Task.Yield();
        return g;                                                // A14 reported
    }

    Guard ReturnUsingStatement()
    {
        using (Guard g = Enter())
        {
            return g;                                            // A15 reported
        }
    }

    Guard ReturnForeachVar(Guard[] arr)
    {
        foreach (ref Guard g in arr.AsSpan())
            return g;                                            // A16 reported (ref local)
        return default;
    }
}

class Prim(Guard g)
{
    public Guard Get() => g;                                     // A20 reported
    public Guard Get2() { return g; }                            // A21 reported
    public void Use() { Guard c = g; }                           // A22 reported
    public Func<Guard> F => () => g;                             // A23 reported
}

struct PrimS(Guard g)                                            // A24 field of copyable struct?
{
    public Guard Get() => g;                                     // A25
}

[NonCopyable]
struct PrimNC(Guard g)
{
    public Guard Get() => g;                                     // A26 reported
}
