using System.Runtime.InteropServices;

// F7 (C): [FieldOffset] lays a copyable struct over a guard, with no 'unsafe'.
public struct GuardBits
{
    public Lock? L;
}

[Singulink.CodeAnalysis.NonCopyable]
[StructLayout(LayoutKind.Explicit)]
public struct GuardOverlay
{
    [FieldOffset(0)] public Guard Guard;
    [FieldOffset(0)] public GuardBits Bits;        // no diagnostic
}

public static class RTF7
{
    public static Lock CopyThroughOverlay()
    {
        var l = new Lock();
        GuardOverlay a = default;
        a.Guard = l.Enter();
        GuardOverlay b = default;
        b.Bits = a.Bits;                           // no diagnostic: copies the guard's bits
        b.Guard.Dispose();
        a.Guard.Dispose();
        return l;
    }
}
