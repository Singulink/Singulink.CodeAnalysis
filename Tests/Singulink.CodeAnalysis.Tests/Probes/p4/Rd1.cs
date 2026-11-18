using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Singulink.CodeAnalysis;

[NonCopyable]
public struct RdG
{
    public int Count;
    public readonly bool IsHeld => Count > 0;
    public void Dispose() => Count = 0;
}
[NonCopyable]
public record struct Opts(int Size) { }

class Rd1
{
    private RdG _first, _second;
    private RdG? _maybe;
    private object _box = null!;
    static Guard[] CreateArray() => new Guard[1];
    void M(bool flag, Guard[] guardArray, List<List<Guard>> listOfGuardLists, Func<Guard[]> makeGuards, Opts options)
    {
        string text = "held by " + _first;                              // R01 OK (ToString not overridden)
        if (_maybe is { Count: > 0 }) { }                               // R02 OK
        bool? held = _maybe?.IsHeld;                                    // R03 OK
        if (_box is RdG { IsHeld: true }) { }                           // R04 OK
        (flag ? _first : _second).Dispose();                            // R05 SCA1006
        Guard e = new ArraySegment<Guard>(CreateArray())[0];            // R06 SCA1030
        foreach (IList list in listOfGuardLists) { }                    // R07 SCA1022
        var wrapped = new Func<object>(makeGuards);                     // R08 SCA1023
        Array tasks = new Task<Guard>[2];                               // R09 OK
        object pair = (Owner.Enter(), 1);                               // R10 SCA1023
        Guard[] part = guardArray[1..];                                 // R11 SCA1020
        var changed = options with { Size = 1 };                        // R12 SCA1003
        string s2 = $"held by {_first}";                                // R13 ? interpolation
    }
}
