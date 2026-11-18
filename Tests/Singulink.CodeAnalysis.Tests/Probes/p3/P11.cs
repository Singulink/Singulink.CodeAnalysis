using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Singulink.CodeAnalysis;

abstract class Base11
{
    public virtual object Get() => null!;
    public virtual IEnumerable Items => null!;
    public abstract object Abs();
    public virtual object this[int i] => null!;
}

class D11 : Base11
{
    private Guard[] _a = new Guard[1];
    public override Guard[] Get() => _a;                                   // M01 SCA1023
    public override List<Guard> Items => null!;                            // M02 SCA1022
    public override Task<Guard> Abs() => null!;                            // M03 fine (erasable)
    public override Guard[] this[int i] => _a;                             // M04 SCA1023
}

class Base11g<U> { public virtual U Get() => default!; }
class D11g : Base11g<object> { public override List<Guard> Get() => null!; }   // M05 SCA1023
class D11h<[NonCopyable] T> : Base11 { private T[] _a = new T[1]; public override T[] Get() => _a; public override object Abs() => null!; }   // M06 SCA1023

static class Ext11 { public static Guard[] Arr(this List<Guard> l) => null!; }

class P11
{
    static Guard[] MakeArr() => null!;
    static List<Guard> MakeList() => null!;
    static void TakeObj(object o) { }
    static void TakeList(List<Guard> l) { }
    static void Run(Func<object> f) { }

    void M(List<Guard> list)
    {
        Func<object> f = MakeArr;                                          // M10 SCA1023
        Action<List<Guard>> a = TakeObj;                                   // M11 SCA1023
        Func<IEnumerable> f2 = MakeList;                                   // M12 SCA1022
        Func<Task> f3 = Owner.EnterTaskAsync;                              // M13 fine
        Run(MakeArr);                                                      // M14 SCA1023
        var f4 = new Func<object>(MakeArr);                                // M15 SCA1023
        Func<object> f5 = list.Arr;                                        // M16 SCA1023
        Func<Guard[]> fa = MakeArr;
        var f6 = new Func<object>(fa);                                     // M17 delegate from delegate
        Func<object> f7 = fa.Invoke;                                       // M18 method group of Invoke
        Action<object> ao = TakeObj;
        Action<List<Guard>> al = ao.Invoke;                                // M19 method group of Invoke (contravariant)
        Action<List<Guard>> al2 = new Action<List<Guard>>(ao);             // M20 delegate from delegate
    }
}
