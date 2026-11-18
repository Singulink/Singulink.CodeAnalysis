using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Singulink.CodeAnalysis;

static class Helpers3
{
    public static T Take<[NonCopyable] T>(ref T location) => NonCopyable.Move(ref location);
    public static T Exchange<[NonCopyable] T>(ref T location, T value)
    {
        T old = NonCopyable.Move(ref location);
        location = NonCopyable.Move(ref value);
        return old;
    }
}

class Own3
{
    private Task<Guard> _t = null!;
    private Task<Guard> _u = null!;

    async Task Holes()
    {
        var t = _t;
        var u = _t;
        Guard a = await Helpers3.Take(ref t);                       // E01 hole? stored container moved out through ref T
        Guard b = await Helpers3.Take(ref u);                       // E02 second guard from _t
        Guard c = await Helpers3.Take(ref _u);                      // E03 moved out of a field through ref T
        Guard d = await NonCopyable.Move(ref _u);                   // E04 SCA1033 (contrast)
        Task<Guard> x = Owner.EnterTaskAsync();
        Guard e = await Helpers3.Exchange(ref x, _t);              // E05 _t goes in by value: SCA1033 expected
        Guard f = await Helpers3.Exchange(ref _u, Owner.EnterTaskAsync()); // E06 old value of a field out as new
    }
}
