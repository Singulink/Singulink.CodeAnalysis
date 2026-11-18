using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Singulink.CodeAnalysis;

namespace UL
{
    public sealed class Src<T> { private readonly TaskCompletionSource<T> _s = new(); public Task<T> Task => _s.Task; public void Set(T v) => _s.SetResult(v); public void Set2(T v) { } }
    public sealed class BadList<T> { public List<T> Items { get; } = new(); }
    public struct GuardSrc
    {
        private TaskCompletionSource<Guard> _s;
        public GuardSrc() => _s = new();
        public Task<Guard> Task => _s.Task;
        public void Become(GuardSrc other) => this = other;
        public void Set(Lock l) => _s.TrySetResult(l.Enter());
    }
    public sealed class ClassSrc
    {
        private readonly TaskCompletionSource<Guard> _s;
        public ClassSrc() => _s = new();
        public ClassSrc(ClassSrc other) => _s = other._s;
        public Task<Guard> Task => _s.Task;
        public void Set(Lock l) => _s.TrySetResult(l.Enter());
    }
    public static class StaticSrc { public static Task<Guard> Task => System.Threading.Tasks.Task.FromResult(default(Guard)); }
    public sealed class IdxSrc { private readonly TaskCompletionSource<Guard> _s = new(); public Task<Guard> this[int i] => _s.Task; }
    public sealed class ValueSrc { public ValueTask<Guard> Task => default; }
}

public static class RTQ
{
    private static UL.GuardSrc s_shared;
    private static UL.ClassSrc s_sharedClass = new();

    public static Task<Guard> Q01() { var s = new UL.Src<Guard>(); return s.Task; }                       // Q01 clean
    public static Task<Guard> Q02() { var s = new UL.IdxSrc(); return s[0]; }                              // Q02 ?
    public static Task<Guard> Q03() { var s = new UL.IdxSrc(); var a = s[0]; return s[1]; }               // Q03 reported
    public static Task<Guard> Q04() => UL.StaticSrc.Task;                                                   // Q04 reported
    public static List<Guard> Q05() { var s = new UL.BadList<Guard>(); return s.Items; }                   // Q05 reported

    private static Task<Guard> TakeStruct() { var s = new UL.GuardSrc(); s.Become(s_shared); return s.Task; }        // Q10 clean, wrong
    private static Task<Guard> TakeClass() { var s = new UL.ClassSrc(s_sharedClass); return s.Task; }               // Q11 clean, on trust

    public static Lock Q12()
    {
        var l = new Lock();
        s_shared = new UL.GuardSrc();
        s_shared.Set(l);
        Guard a = TakeStruct().Result;                                                                     // Q12
        Guard b = TakeStruct().Result;                                                                     // Q13
        a.Dispose();
        b.Dispose();
        return l;
    }

    public static Lock Q14()
    {
        var l = new Lock();
        s_sharedClass = new UL.ClassSrc();
        s_sharedClass.Set(l);
        Guard a = TakeClass().Result;                                                                      // Q14
        Guard b = TakeClass().Result;                                                                      // Q15
        a.Dispose();
        b.Dispose();
        return l;
    }
}
