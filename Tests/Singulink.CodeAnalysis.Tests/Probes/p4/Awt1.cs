using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Singulink.CodeAnalysis;
class Awt1
{
    private ConfiguredTaskAwaitable<Guard> _cf;
    private TaskAwaiter<Guard> _aw;
    async Task M()
    {
        var aw = Owner.EnterTaskAsync().GetAwaiter();
        Guard a = aw.GetResult();                                   // AW1 stored awaiter
        Guard b = aw.GetResult();                                   // AW2
        var cf = Owner.EnterTaskAsync().ConfigureAwait(false);
        Guard c = await cf;                                         // AW3
        Guard d = await cf;                                         // AW4
        Guard e = await _cf;                                        // AW5
        Guard f = _aw.GetResult();                                  // AW6
        var vaw = Owner.EnterAsync().GetAwaiter();
        Guard g = vaw.GetResult();                                  // AW7
        Guard h = Owner.EnterTaskAsync().GetAwaiter().GetResult();  // AW8 OK
        var cf2 = Owner.EnterTaskAsync().ConfigureAwait(false);
        Guard i = await NonCopyable.Move(ref cf2);                  // AW9 OK?
        var cfaw = cf.GetAwaiter();                                 // AW10
        Guard j = cfaw.GetResult();                                 // AW11
    }
}
