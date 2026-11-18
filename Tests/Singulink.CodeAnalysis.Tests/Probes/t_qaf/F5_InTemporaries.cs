using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

// F5: a temporary copy that the compiler makes for an 'in' argument can be written through accepted members.
public static class InPlace
{
    // Correct when it is given a variable: it releases the caller's guard in place.
    public static void ReleaseInPlace(in Guard g) => Unsafe.AsRef(in g).Dispose();

    public static void ReleaseInPlace(in Guard? g) => Unsafe.AsRef(in Nullable.GetValueRefOrDefaultRef(in g)).Dispose();
}

public static class RTF5
{
    public static Lock Conditional()
    {
        var l = new Lock();
        var a = l.Enter();
        var b = l.Enter();
        InPlace.ReleaseInPlace(true ? a : b);      // no diagnostic: releases a copy of 'a'
        a.Dispose();
        b.Dispose();
        return l;
    }

    public static Lock SwitchExpression()
    {
        var l = new Lock();
        var a = l.Enter();
        InPlace.ReleaseInPlace(a switch { _ => a });   // no diagnostic
        a.Dispose();
        return l;
    }

    public static Lock NullableConversion()
    {
        var l = new Lock();
        var a = l.Enter();
        InPlace.ReleaseInPlace((Guard?)a);         // no diagnostic: the conversion makes a copy
        a.Dispose();
        return l;
    }

    // No Unsafe at all: ReadOnlySpan<T>(in T) and MemoryMarshal.GetReference, both on the list.
    public static Lock SpanOverConditional()
    {
        var l = new Lock();
        var a = l.Enter();
        var b = l.Enter();
        MemoryMarshal.GetReference(new ReadOnlySpan<Guard>(true ? a : b)).Dispose();   // no diagnostic
        a.Dispose();
        b.Dispose();
        return l;
    }
}
