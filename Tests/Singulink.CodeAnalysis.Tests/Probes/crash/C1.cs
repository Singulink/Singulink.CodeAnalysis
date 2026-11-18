using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Singulink.CodeAnalysis;
using static Src;

unsafe partial class C1
{
    Guard _f;
    static void Va(__arglist) { }
    void M(dynamic d, Guard* p, delegate*<Guard, Guard> fp)
    {
        Va(__arglist(_f, Enter()));
        Guard a = *p;
        Guard b = fp(_f);
        Guard c = p[1];
        d.Foo(_f);
        var e = d[_f];
        Guard f = d;
        fixed (Guard* q = &_f) { }
        Guard* r = stackalloc Guard[2];
        Guard g = r[0];
        var tr = __makeref(_f);
        Guard h = __refvalue(tr, Guard);
        Guard i = checked(_f);
        Guard j = (_f);
        Guard k = default(Guard)!;
        Guard l = Enter()!;
        object o = new { _f };
        var x = nameof(_f);
        var y = typeof(List<Guard>);
        int z = sizeof(Guard);
        Guard m = (Guard)(object)(Guard)(object)_f;
    }
    public Guard Prop { get => field; set => field = value; }
    public partial Guard PP { get; }
    Guard Bad() => Undefined(_f);
    Guard Bad2() { return; }
    void Bad4() { await EnterAsync(); }
    IEnumerable<Guard> It() { yield return Enter(); yield return _f; }
    async IAsyncEnumerable<Guard> ItA() { await Task.Yield(); yield return _f; }
    void Lam() { var f = (ref Guard g) => g; var f2 = (scoped ref Guard g) => ref g; Func<Guard, Guard> f3 = static Guard ([NonCopyable] Guard g) => g; }
}
partial class C1 { public partial Guard PP => _f; }
