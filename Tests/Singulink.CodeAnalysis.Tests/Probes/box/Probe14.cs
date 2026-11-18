using System;
using System.Collections;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.Threading.Tasks;
using Singulink.CodeAnalysis;

internal class Probe14
{
    private static Task<Guard> EnterTaskAsync() => null!;
    private readonly Guard[] _arr = new Guard[4];

    private static T Id<[NonCopyable] T>(T x) => x;
    private static object? First<[NonCopyable] T>(ref T items) where T : IList => items[0];   // W03

    private ArraySegment<Guard> Seg() => _arr;                 // W04

    private async Task Go(List<Guard> list)
    {
        Task<Guard> stored = EnterTaskAsync();
        Guard a = await Id(stored);                             // W01 finding 1
        Guard b = await Id(stored);                             // W02
        object? copy = First(ref list);                         // W05 finding 4
        Guard c = Seg()[0];                                     // W06 finding 2
        ITuple tuple = (Owner.Enter(), 1);                      // W07 finding 3
    }
}
