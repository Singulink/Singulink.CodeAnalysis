using System;
using System.Runtime.InteropServices;

// F6: FromIntPtr and the conversion from IntPtr make a second GCHandle for one that is held, and ToIntPtr 'borrows'.
public static class RTF6
{
    public static Lock FromIntPtrRoundTrip()
    {
        object first = "first", third = "third";
        var h = GCHandle.Alloc(first);
        var dup = GCHandle.FromIntPtr(GCHandle.ToIntPtr(h));     // no diagnostic
        dup.Free();
        var other = GCHandle.Alloc(third);                        // takes the slot that 'dup' freed
        bool sameSlot = GCHandle.ToIntPtr(other) == GCHandle.ToIntPtr(h);
        h.Free();                                                 // frees the slot again: 'other' is gone
        Console.WriteLine($"  FromIntPtr: third handle in the freed slot: {sameSlot}; its target now: {other.Target ?? "null"}");
        return new Lock();
    }

    public static Lock ExplicitConversion()
    {
        object first = "first", third = "third";
        var h = GCHandle.Alloc(first);
        var dup = (GCHandle)GCHandle.ToIntPtr(h);                 // no diagnostic
        dup.Free();
        var other = GCHandle.Alloc(third);
        bool sameSlot = GCHandle.ToIntPtr(other) == GCHandle.ToIntPtr(h);
        h.Free();
        Console.WriteLine($"  (GCHandle)ptr: third handle in the freed slot: {sameSlot}; its target now: {other.Target ?? "null"}");
        return new Lock();
    }

    public static Lock GenericFromIntPtr()
    {
        var h = new GCHandle<object>("first");
        var dup = GCHandle<object>.FromIntPtr(GCHandle<object>.ToIntPtr(h));   // no diagnostic
        dup.Dispose();
        var other = new GCHandle<object>("third");
        bool sameSlot = GCHandle<object>.ToIntPtr(other) == GCHandle<object>.ToIntPtr(h);
        h.Dispose();
        object? seen;
        try { seen = other.Target; } catch (Exception e) { seen = e.GetType().Name; }
        Console.WriteLine($"  GCHandle<T>.FromIntPtr: third handle in the freed slot: {sameSlot}; its target now: {seen ?? "null"}");
        return new Lock();
    }
}
