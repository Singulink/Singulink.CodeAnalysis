using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Singulink.CodeAnalysis;

unsafe struct FixedBuf { public fixed int Data[4]; }

[NonCopyable]
public ref struct RefG { public ref int R; public void Dispose() { } }

static class Crash1
{
    static void Args(__arglist) { }
    static Guard s_g;
    static unsafe void M(Guard g, Task<Guard>? t, List<Task<Guard>> lst, object o)
    {
        Args(__arglist(s_g));                                       // CR01
        FixedBuf fb = default; int* p = fb.Data;                    // CR02
        var x = g switch { { IsHeld: true } and var y => 1, _ => 0 }; // CR03
        if (o is not Guard) { }                                     // CR04
        Guard d = default!;                                         // CR05
        var tup = (a: Owner.Enter(), b: lst);                       // CR06
        var (q, r) = tup;                                           // CR07
        _ = t is { IsCompleted: true, Result.IsHeld: true };        // CR08 extended property pattern into a container's Result
        _ = lst is [var first, .. var rest];                        // CR09
        Func<Task<Guard>, ValueTask<Guard>> conv = static tt => new(tt);  // CR10
        var anon = new { G = Owner.EnterTaskAsync() };              // CR11
        Guard z = await2(anon.G);                                   // CR12
        static Guard await2(Task<Guard> t) => t.Result;             // CR13
        TypedReference tr = __makeref(s_g);                         // CR14
        Guard w = __refvalue(tr, Guard);                            // CR15
        Span<Guard> sp = [Owner.Enter(), .. stackalloc Guard[0]];   // CR16
        var e = new { Missing = UndefinedThing };                   // CR17 compile error on purpose
    }
}
