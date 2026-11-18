using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Singulink.CodeAnalysis;

[NonCopyable]
public struct Guard : IDisposable
{
    private int _state;
    public void Dispose() => _state = 0;
}

public sealed class RwLock { public Guard Enter() => default; }

public sealed class Keeper
{
    private List<Guard> _kept = new();
    private static readonly List<Keeper> s_all = new();

    public void KeepDirect(List<Guard> l) { _kept = l; }                              // P01 expected SCA1032
    public void KeepViaRef(List<Guard> l) { ref List<Guard> slot = ref _kept; slot = l; }   // P02 the documented gap
    public void UseInPlace() => _kept[0].Dispose();                                   // P03 fine
}

public sealed class Guards : List<Guard>
{
    private static readonly List<Guards> s_all = new();
    public void Register() => s_all.Add(this);                                         // P04 stores this
}

public class Callers
{
    private readonly RwLock _rwLock = new();
    private readonly Keeper _keeper = new();
    private readonly TaskCompletionSource<Guard> _source = new();

    void ViaRef()
    {
        var list = new List<Guard> { _rwLock.Enter() };
        _keeper.KeepViaRef(list);
        foreach (Guard g in NonCopyable.Move(ref list)) g.Dispose();                   // P05 a copy of each guard the keeper still holds?
    }

    void ViaRefLastUse()
    {
        var list = new List<Guard> { _rwLock.Enter() };
        _keeper.KeepViaRef(list);
        foreach (Guard g in list) g.Dispose();                                         // P06 last use of list
    }

    void StoresThis()
    {
        var guards = new Guards();
        guards.Add(_rwLock.Enter());
        guards.Register();
        Guard a = guards[0];                                                           // P07
    }

    Task<Guard> ReturnSourceTask() => _source.Task;                                    // P08
    Task<Guard> ReturnMoved() { var task = _source.Task; return NonCopyable.Move(ref task); }   // P09
}
