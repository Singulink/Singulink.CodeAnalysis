using System;
using Singulink.CodeAnalysis;

public sealed class Lock
{
    public int Acquires, Releases;
    public Guard Enter() { Acquires++; return new Guard(this); }
    public override string ToString() => $"acquires={Acquires} releases={Releases}";
}

[NonCopyable]
public struct Guard : IDisposable
{
    private Lock? _lock;
    public Guard(Lock l) => _lock = l;
    public readonly bool IsHeld => _lock is not null;
    public void Dispose() { if (_lock is { } l) { _lock = null; l.Releases++; } }
}
