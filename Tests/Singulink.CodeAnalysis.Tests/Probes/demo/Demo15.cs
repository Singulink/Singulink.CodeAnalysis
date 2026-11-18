using System;
using System.Collections.Generic;
using Singulink.CodeAnalysis;

public sealed class Keeper2
{
    private List<Guard> _kept = new();
    public void KeepViaRef(List<Guard> l) { ref List<Guard> slot = ref _kept; slot = l; }   // Z01 reported
    public void KeepMarked([Kept] List<Guard> l) { ref List<Guard> slot = ref _kept; slot = l; }   // Z02 clean
    public void Fill(List<Guard> l, RwLock rw) { l.Add(rw.Enter()); }                       // Z03 clean
}

public sealed class Guards2 : List<Guard>
{
    private static readonly List<Guards2> s_all = new();
    public void Register() => s_all.Add(this);
}

public class Round10
{
    private Guard _held;
    private Index _end;
    private int _count;
    private readonly RwLock _rwLock = new();

    static void Work() { }
    static void Take(Guard g) { }

    void Hat()
    {
        Guard g = _rwLock.Enter();
        try { Work(); _held = g; _end = ^_count; }        // Z04 reported
        catch { g.Dispose(); throw; }
    }

    void AssignAsValue()
    {
        Take(_held = _rwLock.Enter());                    // Z05 reported
    }

    void Derived()
    {
        var guards = new Guards2();
        guards.Add(_rwLock.Enter());
        guards.Register();
        Guard a = guards[0];                              // Z06 reported
    }

    void PlainList()
    {
        var list = new List<Guard> { _rwLock.Enter() };
        Guard a = list[0];                                // Z07 clean: last use of an owned list
    }
}
