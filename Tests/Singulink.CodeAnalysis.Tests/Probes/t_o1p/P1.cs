using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Singulink.CodeAnalysis;

public sealed class MemHolder
{
    public readonly Guard[] _arr = new Guard[1];
    public Memory<Guard> Mem() => _arr;                                  // M00 returns a view of a field
    public ReadOnlyMemory<Guard> RoMem() => _arr;                        // M00b
}

public static class RT1
{
    // M01: Memory -> TryGetArray -> segment.Array -> element
    public static Lock M01()
    {
        var l = new Lock();
        var h = new MemHolder();
        h._arr[0] = l.Enter();
        MemoryMarshal.TryGetArray<Guard>(h.Mem(), out var seg);       // M01a
        Guard[] a = seg.Array!;                                         // M01b
        Guard g = a[0];                                                 // M01c
        g.Dispose();
        h._arr[0].Dispose();
        return l;
    }

    // M02: Memory -> segment -> IList<Guard> -> indexer
    public static Lock M02()
    {
        var l = new Lock();
        var h = new MemHolder();
        h._arr[0] = l.Enter();
        MemoryMarshal.TryGetArray<Guard>(h.RoMem(), out ArraySegment<Guard> seg);   // M02a
        IList<Guard> il = seg;                                          // M02b
        Guard g = il[0];                                                // M02c
        g.Dispose();
        h._arr[0].Dispose();
        return l;
    }

    // M03: ArrayPool.Return keeps the array, and the method still returns it
    static Guard[] MakeAndReturnToPool(Lock l, ArrayPool<Guard> pool)
    {
        var a = pool.Rent(1);
        a[0] = l.Enter();
        pool.Return(a);                                                 // M03a
        return a;                                                       // M03b
    }

    public static Lock M03()
    {
        var l = new Lock();
        var pool = ArrayPool<Guard>.Create();
        Guard[] mine = MakeAndReturnToPool(l, pool);
        Guard[] again = pool.Rent(1);                                   // M03c
        Guard g1 = mine[0];                                             // M03d
        Guard g2 = again[0];                                            // M03e
        g1.Dispose(); g2.Dispose();
        return l;
    }

    // M04: shared pool, returned without clearing, rented elsewhere
    static void UseAndReturn(Lock l)
    {
        var a = ArrayPool<Guard>.Shared.Rent(16);
        a[0] = l.Enter();
        a[0].Dispose();                                                 // released in place
        ArrayPool<Guard>.Shared.Return(a);                              // M04a (guard bits stay)
    }

    public static Lock M04()
    {
        var l = new Lock();
        var a = ArrayPool<Guard>.Shared.Rent(16);
        a[0] = l.Enter();
        ArrayPool<Guard>.Shared.Return(a);                              // M04b keeps a
        var b = ArrayPool<Guard>.Shared.Rent(16);                       // M04c
        Guard x = b[0];                                                 // M04d
        x.Dispose();
        a[0].Dispose();                                                 // M04e still works
        return l;
    }

    // M05: an ImmutableArray view boxed to IList
    public static Lock M05()
    {
        var l = new Lock();
        var arr = new Guard[1];
        arr[0] = l.Enter();
        IList<Guard> il = ImmutableCollectionsMarshal.AsImmutableArray(arr);   // M05a
        Guard g = il[0];                                                // M05b
        g.Dispose();
        arr[0].Dispose();
        return l;
    }

    // M06: CompareExchange hands out the old task, the local stays owned
    public static async Task<Lock> M06a()
    {
        var l = new Lock();
        var t = l.EnterTaskAsync();
        var old = Interlocked.CompareExchange(ref t, null, null);       // M06
        Guard g1 = await old;                                           // M06b
        Guard g2 = await t;                                             // M06c
        g1.Dispose(); g2.Dispose();
        return l;
    }
    public static Lock M06() => M06a().Result;

    // M07: Memory -> MemoryMarshal.AsMemory -> TryGetArray via ReadOnlyMemory from a method returning a Memory of a field
    public static Lock M07()
    {
        var l = new Lock();
        var h = new MemHolder();
        h._arr[0] = l.Enter();
        if (MemoryMarshal.TryGetArray<Guard>(MemoryMarshal.AsMemory(h.RoMem()), out var seg))   // M07a
        {
            foreach (Guard g in seg.Array!) { g.Dispose(); }            // M07b
        }
        h._arr[0].Dispose();
        return l;
    }
}
