using System;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;
sealed class Src : IValueTaskSource<Guard>, IThreadPoolWorkItem
{
    private ManualResetValueTaskSourceCore<Guard> _core;
    public Guard GetResult(short t) => throw null!;
    public ValueTaskSourceStatus GetStatus(short t) => _core.GetStatus(t);
    public void OnCompleted(Action<object?> c, object? s, short t, ValueTaskSourceOnCompletedFlags f) => _core.OnCompleted(c, s, t, f);
    public void Execute() { }
    void M()
    {
        ThreadPool.UnsafeQueueUserWorkItem(this, false);   // ER1 erasable through marks
        object o = this;                                   // ER2
        lock (this) { }                                    // ER3
        Monitor.Enter(this);                               // ER4
    }
}
