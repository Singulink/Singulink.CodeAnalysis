using System;
using System.Collections.Generic;

// F1: a spread over a span that a method returns copies every element, and the storage keeps the originals.
public sealed class GuardTable
{
    private readonly Guard[] _slots = new Guard[1];

    public ref Guard Slot(int i) => ref _slots[i];

    public Span<Guard> Items => _slots;            // a property: a spread over it is reported

    public Span<Guard> GetItems() => _slots;       // a method: a span is not checked where it is returned
}

public static class RTF1
{
    public static Lock SpreadToArray()
    {
        var l = new Lock();
        var table = new GuardTable();
        table.Slot(0) = l.Enter();

        Guard[] snapshot = [.. table.GetItems()];  // no diagnostic: copies the guard out of the table
        snapshot[0].Dispose();
        table.Slot(0).Dispose();                   // the table's guard still works
        return l;
    }

    public static Lock SpreadToList()
    {
        var l = new Lock();
        var table = new GuardTable();
        table.Slot(0) = l.Enter();

        List<Guard> list = [.. table.GetItems()];
        System.Runtime.InteropServices.CollectionsMarshal.AsSpan(list)[0].Dispose();
        table.Slot(0).Dispose();
        return l;
    }
}
