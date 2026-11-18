using System;
using System.Collections.Generic;
using System.Threading.Tasks;

internal class Probe9
{
    private Task<Guard> _cached = null!;
    private readonly List<Guard> _guards = new();
    private readonly Guard[] _array = new Guard[4];

    private Task<Guard> GetCachedAsync() => _cached;                 // K01 hands out the same task every time
    private List<Guard> GetGuards() => _guards;                      // K02 hands out the field
    private Guard[] GetArray() => _array;                            // K03
    private Span<Guard> GetSpan() => _array;                         // K04
    private Task<Guard> CachedProperty => _cached;                   // K05
    private static Task<Guard> Same(Task<Guard> task) => task;       // K06 hands back its argument

    private async Task Use(Task<Guard> stored)
    {
        Guard a = await GetCachedAsync();                            // K07
        Guard b = await GetCachedAsync();                            // K08 second guard from the same task
        foreach (Guard g in GetGuards()) { }                         // K09 copies every stored guard
        Guard c = GetGuards()[0];                                    // K10
        Guard d = GetArray()[0];                                     // K11
        foreach (Guard g in GetArray()) { }                          // K12
        Guard e = GetSpan()[0];                                      // K13
        Guard f = await CachedProperty;                              // K14
        Guard h = await Same(stored);                                // K15
    }
}
