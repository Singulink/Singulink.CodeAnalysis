using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Singulink.CodeAnalysis;

public interface IProvider<[NonCopyable] T> { T Value { get; } }
public delegate ref T RefGetter<[NonCopyable] T>();

public static class G2
{
    public static T FromProvider<[NonCopyable] T>(IProvider<T> p) => p.Value;
    public static T TakeVia<[NonCopyable] T>(RefGetter<T> g) => NonCopyable.Move(ref g());
    public static T Pass<[NonCopyable] T>([MovedFrom] ref T v) => NonCopyable.Move(ref v);
}

public sealed class FieldProvider : IProvider<Task<Guard>>
{
    public static Task<Guard> Shared = Owner.EnterTaskAsync();
    public Task<Guard> Value => Shared;                                  // A01
}

public static class Movers
{
    private static Task<Guard> s_sentinel = Owner.EnterTaskAsync();

    public static Task<Guard> TakeLeavingSentinel([MovedFrom] ref Task<Guard> task)   // A02
    {
        var taken = task;
        task = s_sentinel;
        return taken;
    }
}

public sealed class Gen2
{
    private Task<Guard> _t = Owner.EnterTaskAsync();
    private Task<Guard> _t2 = null!;

    public async Task Run()
    {
        Guard a = await G2.FromProvider(new FieldProvider());           // B01
        Guard b = await G2.FromProvider(new FieldProvider());           // B02

        _t2 = _t;
        Guard c = await G2.TakeVia(() => ref _t);                       // B03
        Guard d = await G2.TakeVia(() => ref _t2);                      // B04

        var x = Owner.EnterTaskAsync();
        Guard e = await G2.Pass(ref x);                                 // B05 OK
        Guard f = await G2.Pass(ref _t);                                // B06 SCA1033 expected

        var y = Owner.EnterTaskAsync();
        Task<Guard> first = Movers.TakeLeavingSentinel(ref y);          // B07
        Guard g = await NonCopyable.Move(ref y);                        // B08 y now holds s_sentinel
        var z = Owner.EnterTaskAsync();
        Movers.TakeLeavingSentinel(ref z);                              // B09
        Guard h = await NonCopyable.Move(ref z);                        // B10 second guard from s_sentinel
    }
}
