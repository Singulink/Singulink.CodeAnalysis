using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

internal class Probe3
{
    private void Sinks(List<Guard> list, List<Guard> other, Guard[] array, Task<Guard> task, Guard stored, Guard? maybe)
    {
        ArgumentNullException.ThrowIfNull(list);            // T01 listed sink
        ArgumentNullException.ThrowIfNull(array);           // T02
        ArgumentNullException.ThrowIfNull(task);            // T03
        bool same = ReferenceEquals(list, other);           // T04
        object o = list;                                    // T05 no member to list
        System.Collections.IEnumerable e = list;            // T06 untyped collection
        IEnumerable<Guard> typed = list;                    // T07 type kept visible
        Debug.Assert(list != null);                         // T08
        if (list is null) { }                               // T09
        if (list == other) { }                              // T10
        Console.WriteLine(list);                            // T11 unlisted member taking object
        string s1 = $"{list.Count}";                        // T12
        string? s2 = list.ToString();                       // T13
        int h = list.GetHashCode();                         // T14
        lock (list) { }                                     // T15
        ArgumentNullException.ThrowIfNull(stored);          // T16 boxes a stored guard
        ArgumentNullException.ThrowIfNull(maybe);           // T17 boxes a stored nullable guard
        MyThrowIfNull(list);                                // T18 own helper taking object
        MyThrowIfNullGeneric(list);                         // T19 own unmarked generic helper
        bool eq = Equals(list, other);                      // T20
        GC.KeepAlive(list);                                 // T21
        ArgumentNullException.ThrowIfNull(list, nameof(list)); // T22
        _ = list ?? throw new ArgumentNullException(nameof(list)); // T23
        Task plain = task;                                  // T24 opaque type, no member
        task.Wait();                                        // T25 inherited non-generic member
    }

    private static void MyThrowIfNull(object? o) { }

    private static void MyThrowIfNullGeneric<T>(T? value) where T : class { }
}
