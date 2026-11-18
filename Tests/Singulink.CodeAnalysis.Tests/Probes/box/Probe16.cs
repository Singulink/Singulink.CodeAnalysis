using System.Collections.Generic;
using System.Threading.Tasks;
using Singulink.CodeAnalysis;

[NonCopyable]
internal struct Mutable
{
    private int _state;

    public readonly async Task ReadAsync() { await Task.Yield(); _ = _state; }      // Y01 readonly async on a mutable struct
    public async Task WriteAsync() { await Task.Yield(); _state = 1; }              // Y02 async, not readonly
    public readonly IEnumerable<int> Steps() { yield return _state; }               // Y03 readonly iterator
}

[NonCopyable]
internal readonly struct Fixed
{
    private readonly object _lock;
    private readonly int _version;

    public Fixed(object l, int v) { _lock = l; _version = v; }

    public async ValueTask DisposeAsync() { await Task.Yield(); _ = _lock; _ = _version; }   // Y04 async on a readonly struct
}
