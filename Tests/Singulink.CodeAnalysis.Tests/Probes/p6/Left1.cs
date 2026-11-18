using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Singulink.CodeAnalysis;

public static class Left1
{
    private static Task<Guard> s_s = Owner.EnterTaskAsync();
    private static void Get(out Task<Guard> t) => t = Owner.EnterTaskAsync();
    private static void Fill(ref Task<Guard> t) { }

    public static Task<Guard> A([MovedFrom] ref Task<Guard> t) { var m = t; t = null!; t ??= s_s; return m; }               // AA01
    public static Task<Guard> B([MovedFrom] ref Task<Guard> t) { var m = t; Get(out t); return m; }                        // AA02 out of a method: fine?
    public static Task<Guard> C([MovedFrom] ref Task<Guard> t) { var m = t; t = null!; Fill(ref t); return m; }            // AA03
    public static Task<Guard> D([MovedFrom] ref Task<Guard> t) { var m = t; int x; (t, x) = (s_s, 1); return m; }          // AA04
    public static Task<Guard> E([MovedFrom] ref Task<Guard> t, bool c) { var m = t; t = c ? null! : s_s; return m; }       // AA05
    public static Task<Guard> F([MovedFrom] ref Task<Guard> t) { var m = t; t = null!; Interlocked.Exchange(ref t, s_s); return m; }  // AA06
    public static Task<Guard> G([MovedFrom] ref Task<Guard> t) { var m = t; var l = s_s; t = l; return m; }                // AA07
    public static Task<Guard> H([MovedFrom] ref Task<Guard> t, Task<Guard> p) { var m = t; t = p; return m; }              // AA08
    public static Task<Guard> I([MovedFrom] ref Task<Guard> t, List<Task<Guard>> l) { var m = t; t = l[0]; return m; }     // AA09
    public static Task<Guard> J([MovedFrom] ref Task<Guard> t) { var m = t; t = Owner.EnterTaskAsync(); return m; }       // AA10 ok
    public static Task<Guard> K([MovedFrom] ref Task<Guard> t) { var m = t; t = Cached(); return m; }                     // AA11 Cached is [Borrowed]
    [return: Borrowed] private static Task<Guard> Cached() => s_s;
    public static Task<Guard> L([MovedFrom] ref Task<Guard> t) { var m = t; t = Bag().Take(); return m; }                  // AA12
    private static Bag<Task<Guard>> Bag() => new();
    public static Task<Guard> M([MovedFrom] ref Task<Guard> t) { var m = t; ref var r = ref t; r = s_s; return m; }        // AA13
    public static Task<Guard> N([MovedFrom] ref Task<Guard> t) { var m = t; t = (Task<Guard>)(object)s_s; return m; }      // AA14
    public static Task<Guard> O([MovedFrom] ref Task<Guard> t) { var m = t; t = Task.FromResult(s_s.Result); return m; }   // AA15 (Result reported anyway)
}
