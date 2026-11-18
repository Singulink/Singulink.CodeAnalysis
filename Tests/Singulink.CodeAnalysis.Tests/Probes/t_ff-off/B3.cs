using System;
using Singulink.CodeAnalysis;

[NonCopyable]
public struct Flag : IDisposable
{
    private Lock _lock;
    public Flag(Lock l) { _lock = l; l.Acquires++; }
    public void Dispose() { if (_lock is { } l) { _lock = null; l.Releases++; } }
    public static bool operator true(Flag f) => false;
    public static bool operator false(Flag f) { f.Dispose(); return true; }
    public static Flag operator &(Flag a, Flag b) { b.Dispose(); return a; }
}

public static class B3
{
    public static Lock Run()
    {
        var l = new Lock();
        Flag result = new Flag(l) && new Flag(l);   // 'operator false' gets a copy of the left operand, which is also the result
        result.Dispose();
        return l;
    }
}
