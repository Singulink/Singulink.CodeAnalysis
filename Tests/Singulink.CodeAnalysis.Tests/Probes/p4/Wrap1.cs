using System.Threading.Tasks;
using Singulink.CodeAnalysis;
static class Wrap1
{
    static ValueTask<Guard> W1([MovedFrom] ref Task<Guard> task) { var t = task; task = null!; return new(t); }  // X01
    static ValueTask<Guard> W2([MovedFrom] ref Task<Guard> task) => new(NonCopyable.Move(ref task));            // X02
    static ValueTask<Guard> W3([Kept] Task<Guard> task) => new(task);                                            // X03
    static async ValueTask<Guard> W4(Task<Guard> task) => await task;                                            // X04
    static async Task Use()
    {
        var t = Owner.EnterTaskAsync();
        Guard g = await W2(ref t);                                                                                // X05
        Guard h = await W3(Owner.EnterTaskAsync());                                                              // X06
    }
}
