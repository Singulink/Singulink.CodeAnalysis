using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Singulink.CodeAnalysis;

public sealed class Slot<[NonCopyable] T>
{
    private T _value = default!;

    public void Put(T value) => _value = value;                                   // A01
    public T Take() => NonCopyable.Move(ref _value)!;                             // A02
    public ref T Value => ref _value;                                             // A03

    [RequiresCopyable]
    public T Snapshot() => _value;                                                // A04
}

public class Attributes
{
    private Guard _held;
    private readonly List<Guard> _guards = new();

    public static Guard TakeOut([MovedFrom] ref Guard value)                      // A05
    {
        Guard moved = value;                                                      // A06
        value = default;
        return moved;                                                             // A07
    }

    public static Guard Broken([MovedFrom] ref Guard value) => value;             // A08

    [return: Borrowed]
    public List<Guard> Guards() => _guards;                                       // A09

    public List<Guard> Leaked() => _guards;                                       // A10

    public void Use(RwLock rwLock, Slot<Guard> slot, Slot<int> ints)
    {
        Guard g = rwLock.Enter();
        _held = TakeOut(ref g);                                                   // A11
        slot.Put(rwLock.Enter());                                                 // A12
        slot.Put(_held);                                                          // A13
        Guard taken = slot.Take();                                                // A14
        Guard copy = slot.Snapshot();                                             // A15
        int n = ints.Snapshot();                                                  // A16
        Guard first = Guards()[0];                                                // A17
        ref Guard inPlace = ref System.Runtime.InteropServices.CollectionsMarshal.AsSpan(Guards())[0];   // A18
    }
}
