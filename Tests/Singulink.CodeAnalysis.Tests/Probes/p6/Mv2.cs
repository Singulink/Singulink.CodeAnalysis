using System;
using System.Threading.Tasks;
using Singulink.CodeAnalysis;

public class Mover
{
    public virtual T Take<[NonCopyable] T>([MovedFrom] ref T v) { T m = v; v = default!; return m; }
}

public sealed class EvilMover : Mover
{
    public override T Take<[NonCopyable] T>(ref T v)                    // F01 [MovedFrom] dropped
    {
        return NonCopyable.Move(ref Stash<T>.Slot);                      // F02
    }
}

public sealed class EvilMover2 : Mover
{
    public override T Take<[NonCopyable] T>([MovedFrom] ref T v)        // F03 [MovedFrom] kept
    {
        T m = v; v = default!;
        return NonCopyable.Move(ref Stash<T>.Slot);                      // F04
    }
}

public interface IMover
{
    T Take<[NonCopyable] T>([MovedFrom] ref T v);
    static abstract T STake<[NonCopyable] T>([MovedFrom] ref T v);
}

public sealed class EvilImpl : IMover
{
    public T Take<[NonCopyable] T>(ref T v) => NonCopyable.Move(ref Stash<T>.Slot);       // F05
    public static T STake<[NonCopyable] T>(ref T v) => NonCopyable.Move(ref Stash<T>.Slot); // F06
}

public abstract class AbstractMover
{
    public abstract T Take<[NonCopyable] T>([MovedFrom] ref T v);
}

public sealed class Mv2
{
    private Task<Guard> _t = Owner.EnterTaskAsync();

    public async Task Run(Mover m, IMover im)
    {
        Stash<Task<Guard>>.Store(_t);
        var x = Owner.EnterTaskAsync();
        Guard a = await m.Take(ref x);                                   // G01
        Stash<Task<Guard>>.Store(_t);
        var y = Owner.EnterTaskAsync();
        Guard b = await im.Take(ref y);                                  // G02
        Stash<Task<Guard>>.Store(_t);
        var z = Owner.EnterTaskAsync();
        Guard c = await Via2<EvilImpl>(ref z);                           // G03
    }

    static Task<Guard> Via2<TM>(ref Task<Guard> z) where TM : IMover => TM.STake(ref z);   // G04 SCA1031?
}

public sealed class Own1
{
    private Task<Guard> _t = Owner.EnterTaskAsync();

    public async Task Run()
    {
        var w = Owner.EnterTaskAsync();
        ref Task<Guard> rw = ref w;                                      // H01
        rw = _t;                                                         // H02
        Guard a = await NonCopyable.Move(ref w);                         // H03

        var w2 = Owner.EnterTaskAsync();
        Overwrite(ref w2);                                               // H04 plain ref param
        Guard b = await NonCopyable.Move(ref w2);                        // H05

        var w3 = Owner.EnterTaskAsync();
        Span<Task<Guard>> sp = new(ref w3);                              // H06
        sp[0] = _t;
        Guard c = await NonCopyable.Move(ref w3);                        // H07

        var w4 = Owner.EnterTaskAsync();
        var f = () => w4 = _t;                                           // H08
        Guard d = await NonCopyable.Move(ref w4);                        // H09

        Task<Guard> w5 = null!;
        Helpers.Fetch(out w5);                                           // H10
        Guard e = await NonCopyable.Move(ref w5);                        // H11

        var w6 = Owner.EnterTaskAsync();
        PM<Task<Guard>>.ViaRefReturn(ref w6) = _t;                       // H12 (callee reported)
        Guard g = await NonCopyable.Move(ref w6);                        // H13
    }

    void Overwrite(ref Task<Guard> t) => t = _t;                         // H14
}
