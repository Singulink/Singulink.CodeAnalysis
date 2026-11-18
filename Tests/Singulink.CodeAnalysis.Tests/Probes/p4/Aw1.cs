using System;
using System.Threading;
using System.Threading.Tasks;
using Singulink.CodeAnalysis;
static class Aw1
{
    static async Task Use(CancellationToken ct)
    {
        Guard a = await Task.Run(() => Owner.EnterTaskAsync());                                   // AA01
        Guard b = await Task.Run(async () => await Owner.EnterAsync());                           // AA02
        Guard c = await Owner.EnterTaskAsync().ConfigureAwait(ConfigureAwaitOptions.None);        // AA03
        Guard d = await Owner.EnterTaskAsync().WaitAsync(TimeSpan.FromSeconds(1), ct);            // AA04
        Guard e = Owner.EnterTaskAsync().GetAwaiter().GetResult();                                // AA05
        Guard f = Owner.EnterAsync().GetAwaiter().GetResult();                                    // AA06
        Guard g = Owner.EnterTaskAsync().Result;                                                  // AA07
        Guard h = await Owner.EnterAsync().AsTask();                                              // AA08
        var vt = Owner.EnterAsync();
        if (vt.IsCompletedSuccessfully) { Guard i = NonCopyable.Move(ref vt).Result; }            // AA09
        else { Guard j = await NonCopyable.Move(ref vt); }                                        // AA10
        Guard k = await Owner.EnterAsync().ConfigureAwait(false);                                 // AA11
        Guard l = await Task.Factory.StartNew(() => Owner.EnterTaskAsync()).Unwrap();             // AA12
        Guard m = await (ct.CanBeCanceled ? Owner.EnterTaskAsync() : Owner.EnterTaskAsync());     // AA13
        Guard n = await Owner.EnterTaskAsync() ?? throw null!;                                    // AA14
    }
}
