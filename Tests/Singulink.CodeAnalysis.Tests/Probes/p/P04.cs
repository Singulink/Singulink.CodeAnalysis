using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Singulink.CodeAnalysis;
using static Src;

static class Ext
{
    extension<T>(T[] arr)
    {
        public T First2 => arr[0];
        public T Dup() => arr[0];
        public ref T RefFirst() => ref arr[0];
    }
    extension<T>(List<T> l)
    {
        public T Top => l[^1];
    }
    extension(Guard[] arr)
    {
        public Guard Bad => arr[0];                               // G00 report (checked body)
    }
    public static T Classic<T>(this T[] arr) => arr[0];
}

class P04
{
    Guard[] _arr = new Guard[2];
    List<Guard> _list = new();

    void ExtUse(Guard[] arr)
    {
        Guard a = arr.First2;                                    // G01 report
        Guard b = arr.Dup();                                     // G02 report
        ref Guard c = ref arr.RefFirst();                        // G03 report (unlisted)
        Guard d = arr.Classic();                                 // G04 report
        Guard e = Ext.Dup(arr);                                  // G05 report
        Guard f = _list.Top;                                     // G06 report
        Guard h = CreateList().Top;                              // G07 report (unlisted)
    }

    void Foreach(Span<Guard> span, ReadOnlySpan<Guard> ros, Guard[] arr, ImmutableArray<Guard> ia, Memory<Guard> mem)
    {
        foreach (Guard g in span) { }                            // H01 report
        foreach (ref Guard g in span) { }                        // H02 OK
        foreach (ref readonly Guard g in ros) { }                // H03 OK
        foreach (Guard g in ros) { }                             // H04 report
        foreach (var g in arr) { }                               // H05 report
        foreach (Guard g in CreateArray()) { }                   // H06 report
        foreach (ref Guard g in arr.AsSpan()) { }                // H07 OK
        foreach (Guard g in ia) { }                              // H08 report
        foreach (ref readonly Guard g in ia.AsSpan()) { }        // H09 OK? (AsSpan unlisted)
        foreach (Guard g in mem.Span) { }                        // H10 report
        foreach (ref Guard g in mem.Span) { }                    // H11 OK
        foreach (Guard g in CreateList()) { }                    // H12 OK
        foreach (Guard g in _list) { }                           // H13 report
        foreach (ref Guard g in CollectionsMarshal.AsSpan(_list)) { } // H14 OK
        foreach (var (x, y) in new (Guard, int)[1]) { }          // H15 report
        foreach (var (x, y) in CreateTuples()) { }               // H16 OK
        foreach (var kv in new Dictionary<int, Guard>()) { }     // H17 OK
        var dict = new Dictionary<int, Guard>();
        foreach (var kv in dict) { }                             // H18 report
        foreach (var v in dict.Values) { }                       // H19 report (unlisted Values)
        foreach (Guard g in (IEnumerable<Guard>)_list) { }       // H20 report
        foreach (Guard g in Gen()) { }                           // H21 OK
        foreach (Guard g in stackalloc Guard[0]) { }             // H22 ? stackalloc of non-unmanaged? (compile)
    }
    static List<(Guard, int)> CreateTuples() => new();
    static IEnumerable<Guard> Gen() { yield return Enter(); }

    async Task AwaitForeach(IAsyncEnumerable<Guard> ae)
    {
        await foreach (Guard g in ae) { }                        // H30 report (stored)
        await foreach (Guard g in GenAsync()) { }                // H31 OK
        await foreach (Guard g in GenAsync().ConfigureAwait(false)) { } // H32 report? unlisted ConfigureAwait
    }
    static async IAsyncEnumerable<Guard> GenAsync() { await Task.Yield(); yield return Enter(); }

    void CollExpr(Guard stored)
    {
        Guard[] a = [Enter(), Enter()];                          // I01 OK
        Guard[] b = [stored];                                    // I02 report
        List<Guard> c = [Enter()];                               // I03 OK
        List<Guard> d = [.. _list];                              // I04 report
        List<Guard> e = [.. CreateList()];                       // I05 OK
        Span<Guard> f = [Enter()];                               // I06 OK
        IEnumerable<Guard> g = [Enter()];                        // I07 OK?
        ImmutableArray<Guard> h = [Enter()];                     // I08 report? (builder unlisted)
        HashSet<Guard> i = [Enter()];                            // I09 report (unlisted)
        Guard[] j = [.. _arr];                                   // I10 report
        Guard[] k = [.. CreateArray()];                          // I11 OK
        ReadOnlySpan<Guard> l = [.. _arr];                       // I12 report
        IReadOnlyList<Guard> m = [.. _list];                     // I13 report
        List<Guard> n = [.. (ReadOnlySpan<Guard>)_arr];          // I14 report
        List<Guard> o = [.. _arr.AsSpan()];                      // I15 report
    }

    void Params(Guard stored)
    {
        ParamsArr(Enter(), Enter());                             // J01 OK
        ParamsArr(stored);                                       // J02 report
        ParamsSpan(Enter());                                     // J03 OK
        ParamsSpan(stored);                                      // J04 report
        ParamsList(Enter());                                     // J05 OK?
        ParamsGen(Enter());                                      // J06 report (unmarked T)
        ParamsMarked(Enter());                                   // J07 OK
        ParamsMarked(stored);                                    // J08 report
    }
    static void ParamsArr(params Guard[] g) { }
    static void ParamsSpan(params ReadOnlySpan<Guard> g) { }
    static void ParamsList(params List<Guard> g) { }
    static void ParamsGen<T>(params T[] g) { }
    static void ParamsMarked<[NonCopyable] T>(params T[] g) { }

    void Interp(Guard stored, Task<Guard> t)
    {
        string s1 = $"{stored}";                                 // K01 report
        string s2 = $"{Enter()}";                                // K02 report? (AppendFormatted<T> unlisted)
        string s3 = $"{_list}";                                  // K03 report
        FormattableString f = $"{_list}";                        // K04 report
        string s4 = $"{stored.State}";                           // K05 OK
        string s5 = $"{t}";                                      // K06 OK (erasable)
        var h = new DefaultInterpolatedStringHandler(1, 1);
        h.AppendLiteral("x");                                    // K07 OK
        string s6 = h.ToStringAndClear();                        // K08 OK
        var h2 = h;                                              // K09 report
        string s7 = string.Create(null, ref h);                  // K10 OK
        MyHandler($"{stored}");                                  // K11 report
    }
    static void MyHandler(ref DefaultInterpolatedStringHandler h) { }
}
