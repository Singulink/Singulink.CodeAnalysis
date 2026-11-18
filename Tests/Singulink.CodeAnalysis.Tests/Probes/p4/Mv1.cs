using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Singulink.CodeAnalysis;

static class Mv1
{
    private static Guard s_dummy;
    private static Task<Guard>? s_dummyT;

    public static Guard Move1([MovedFrom] ref Guard value)
    {
        Guard moved = value;
        value = ref s_dummy;            // T01 ref reassignment: the reset below no longer reaches the caller
        value = default;
        return moved;
    }

    public static T Move2<[NonCopyable] T>([MovedFrom] ref T value, ref T scratch)
    {
        T moved = value;
        value = ref scratch;            // T02
        value = default!;
        return moved;
    }

    public static Task<Guard>? Move3([MovedFrom] ref Task<Guard>? value)
    {
        var moved = value;
        value = ref s_dummyT;           // T03
        value = null;
        return moved;
    }

    static async Task Use()
    {
        Guard g = Owner.Enter();
        Guard a = Move1(ref g);         // T04 a and g both hold the guard
        g.Dispose();
        var t = Owner.EnterTaskAsync();
        Task<Guard>? t2 = t;
        Guard b = await Move3(ref t)!;  // T05
    }
}
