using System;
using System.Collections.Generic;
using Singulink.CodeAnalysis;

[NonCopyable]
public struct HS : IDisposable
{
    private int _s; private int[] _a;
    public override string ToString() => (_s++).ToString();
    public Enumerator GetEnumerator() => default;
    public struct Enumerator { public bool MoveNext() => false; public int Current => 0; }
    public void Dispose() => _s = 0;
    public int Length => _a.Length;
    public int this[int i] => _a[i];
    public int[] Slice(int start, int length) => _a;
    public ref int GetPinnableReference() => ref _a[0];
    public void Deconstruct(out int a, out int b) { a = _s++; b = 0; }
    public System.Runtime.CompilerServices.TaskAwaiter GetAwaiter() => default;
}

static class Ham1
{
    static async System.Threading.Tasks.Task Async(HS[] arr) { }
    static unsafe void M(in HS v)
    {
        string s = "x" + v;                          // HA01 SCA1006
        foreach (var x in v) { }                     // HA02 SCA1006
        using (v) { }                                // HA03 copy
        var r = v[1..];                              // HA04 SCA1006
        int last = v[^1];                            // HA05 SCA1006
        fixed (int* p = v) { }                       // HA06 SCA1006
        var (a, b) = v;                              // HA07 SCA1006
        string s2 = $"{v}";                          // HA08 copy
    }
}
