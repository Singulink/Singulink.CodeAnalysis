using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Singulink.CodeAnalysis;

public class TcsLock
{
    private readonly Queue<TaskCompletionSource<Guard>> _waiters = new();
    private readonly TaskCompletionSource<Guard> _source = new();
    private List<Guard> _kept = new();

    public Task<Guard> Enter()
    {
        var source = new TaskCompletionSource<Guard>();
        _waiters.Enqueue(source);
        return source.Task;                                           // C01 clean
    }

    public ValueTask<Guard> EnterValue()
    {
        var source = new TaskCompletionSource<Guard>(TaskCreationOptions.RunContinuationsAsynchronously);
        _waiters.Enqueue(source);
        return new ValueTask<Guard>(source.Task);                     // C02 clean
    }

    public Task<Guard> EnterShared() => _source.Task;                 // C03 reported

    public Task<Guard> EnterTwice()
    {
        var source = new TaskCompletionSource<Guard>();
        var t = source.Task;                                          // C04 two reads: reported somewhere
        return source.Task;                                           // C05
    }

    public Task<Guard> EnterCancellable(CancellationToken token)
    {
        var source = new TaskCompletionSource<Guard>();
        token.Register(() => source.TrySetCanceled());
        _waiters.Enqueue(source);
        return source.Task;                                           // C06 a lambda uses the source: reported today
    }

    public void KeepByPattern(List<Guard> l) { if ((l, 1) is (var x, _)) _kept = x; }   // C07 reported
}
