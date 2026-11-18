using System.Threading.Tasks;
using Singulink.CodeAnalysis;
static class Dec1
{
    static async Task Use()
    {
        var (t1, t2) = (Owner.EnterTaskAsync(), Owner.EnterTaskAsync());   // Z01
        using var g1 = await NonCopyable.Move(ref t1);                      // Z02 FP?
        Task<Guard> a, b;
        (a, b) = (Owner.EnterTaskAsync(), Owner.EnterTaskAsync());          // Z03
        using var g2 = await NonCopyable.Move(ref a);                       // Z04 FP?
        var (v1, v2) = (Owner.EnterAsync(), Owner.EnterAsync());           // Z05
        using var g3 = await NonCopyable.Move(ref v1);                      // Z06 OK (value)
        Guard? m = Owner.Enter();

    }
}
