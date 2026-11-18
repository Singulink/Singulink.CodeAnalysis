using System.Collections.Generic;
using Singulink.CodeAnalysis;

public class KeptDemo
{
    private List<Guard> _all = new();
    private readonly RwLock _rwLock = new();

    void Fill(List<Guard> list) { list.Add(_rwLock.Enter()); }                 // K01
    void Register(List<Guard> list) { _all = list; }                           // K02
    void RegisterKept([Kept] List<Guard> list) { _all = list; }                // K03

    List<Guard> Mine()
    {
        var mine = new List<Guard>();
        Fill(mine);
        return mine;                                                           // K04
    }

    List<Guard> Shared()
    {
        var shared = new List<Guard>();
        RegisterKept(shared);
        return shared;                                                         // K05
    }
}
