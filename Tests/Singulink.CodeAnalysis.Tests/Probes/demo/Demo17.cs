using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Singulink.CodeAnalysis;

public class TcsLock2
{
    private readonly Queue<TaskCompletionSource<Guard>> _waiters = new();

    public Task<Guard> Lambda(CancellationToken token)
    {
        var source = new TaskCompletionSource<Guard>();
        token.Register(() => source.TrySetCanceled());
        _waiters.Enqueue(source);
        return source.Task;                                           // W01 clean
    }

    public Task<Guard> StaticState(CancellationToken token)
    {
        var source = new TaskCompletionSource<Guard>();
        token.Register(static s => ((TaskCompletionSource<Guard>)s!).TrySetCanceled(), source);   // W02 clean
        _waiters.Enqueue(source);
        return source.Task;                                           // W03 clean
    }

    public Task<Guard> LambdaReads(CancellationToken token)
    {
        var source = new TaskCompletionSource<Guard>();
        token.Register(() => _ = source.Task);                        // W04
        return source.Task;                                           // W05 reported
    }

    public async Task<Guard> ViaObject(object boxed)
    {
        return await ((TaskCompletionSource<Guard>)boxed).Task;       // W06 reported
    }

    public async Task Complete(Guard g)
    {
        var next = _waiters.Dequeue();
        next.SetResult(NonCopyable.Move(ref g));                      // W07 clean: in place
        Guard again = await next.Task;                                // W08 reported: the lock reading its waiter's task
    }
}
