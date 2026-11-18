using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Singulink.CodeAnalysis;

public interface IGet { List<Guard> Get(); }
public interface IPut { void Put(List<Guard> l); }

public class BBase
{
    public static List<Guard> Shared = new();
    [return: Borrowed] public List<Guard> Get() => Shared;
    public void Put([Kept] List<Guard> l) => Shared = l;
}
public sealed class BDer : BBase, IGet, IPut { }                       // B01

public class BBase2
{
    [return: Borrowed] public virtual List<Guard> Get() => BBase.Shared;
}
public class BDer2 : BBase2 { }
public sealed class BDer3 : BDer2, IGet { }                            // B02

public interface IGet2 : IGet
{
    [return: Borrowed] List<Guard> IGet.Get() => BBase.Shared;          // B03
}
public sealed class BImpl2 : IGet2 { }

public interface IGetS { static abstract List<Guard> Make(); }
public sealed class SImpl : IGetS
{
    [return: Borrowed] public static List<Guard> Make() => BBase.Shared;  // B04
}

public abstract class GBase<TL> where TL : class { public abstract TL Get(); }
public sealed class GDer : GBase<List<Guard>>
{
    [return: Borrowed] public override List<Guard> Get() => BBase.Shared;  // B05
}

public static class RT7
{
    static Lock Run(Func<List<Guard>> get)
    {
        var l = new Lock();
        BBase.Shared = new List<Guard> { l.Enter() };
        foreach (Guard g in get()) g.Dispose();
        CollectionsMarshal.AsSpan(BBase.Shared)[0].Dispose();
        return l;
    }
    static List<Guard> ViaI(IGet i) => i.Get();                         // B10
    public static Lock B01() => Run(() => ((IGet)new BDer()).Get());
    public static Lock B02() => Run(() => ((IGet)new BDer3()).Get());
    public static Lock B03() => Run(() => ((IGet)new BImpl2()).Get());
    static List<Guard> ViaS<TS>() where TS : IGetS => TS.Make();
    public static Lock B04() => Run(() => ViaS<SImpl>());
    static List<Guard> ViaG<TL>(GBase<TL> b) where TL : class => (b.Get() as List<Guard>)!;
    public static Lock B05() => Run(() => ((GBase<List<Guard>>)new GDer()).Get());
}
