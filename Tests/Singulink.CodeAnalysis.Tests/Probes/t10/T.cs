using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TCS = System.Threading.Tasks.TaskCompletionSource<Guard>;

var s = new TCS();
TopLevel.Q.Enqueue(s);
TopLevel.Last = s.Task;                                                            // T01 clean
var s2 = new TCS();
TopLevel.Q.Enqueue(s2);
Task<Guard> F() => s2.Task;                                                         // T02 reported
TopLevel.Last = F();

public static class TopLevel
{
    public static readonly Queue<TCS> Q = new();
    public static Task<Guard>? Last;
}

public class Members
{
    private readonly Queue<TCS> _q = new();
    private readonly Task<Guard> _t;
    public Members() { var s = new TCS(); _q.Enqueue(s); _t = s.Task; }           // T03 clean
    public Task<Guard> Get { get { var s = new TCS(); _q.Enqueue(s); return s.Task; } }   // T04 clean
    public static Task<Guard> operator +(Members m, int k) { var s = new TCS(); m._q.Enqueue(s); return s.Task; }   // T05 clean
    public Task<Guard> M06() { static Task<Guard> L() { var s = new TCS(); return s.Task; } return L(); }    // T06 FP: declared and read in a local function
    public Task<Guard> M07() => Task.Run(() => { var s = new TCS(); _q.Enqueue(s); return s.Task; });       // T07 reported (lambda)
    public Func<Task<Guard>> M08() => () => { var s = new TCS(); _q.Enqueue(s); return s.Task; };           // T08 FP: lambda
    public Task<Guard> M09() { var s = new TCS(); Action a = () => { Action b = () => s.TrySetCanceled(); b(); }; _q.Enqueue(s); return s.Task; }   // T09 clean
    public async Task<Guard> M10() { var s = new TCS(); _q.Enqueue(s); await Task.Yield(); return await s.Task; }   // T10 clean
    public async IAsyncEnumerable<Guard> M11() { var s = new TCS(); _q.Enqueue(s); yield return await s.Task; }    // T11 reported
}
