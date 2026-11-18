using System;
using Singulink.CodeAnalysis;

[NonCopyable]
public struct Lease : IDisposable
{
    private Lock _lock;
    private int _uses;
    public Lease(Lock l) { _lock = l; l.Acquires++; }
    public int Uses { readonly get => _uses; set { _uses = value; if (value == 0) Dispose(); } }
    public event Action Closed { add { Dispose(); } remove { } }
    public void Dispose() { if (_lock is { } l) { _lock = null; l.Releases++; } }
    public static int Close(Lease lease) { lease.Dispose(); return 0; }
    public static Action CloseLater(Lease lease) { lease.Dispose(); return () => { }; }
}

public static class B2
{
    public static Lock Run()
    {
        var l = new Lock();
        Lease lease = new Lease(l);
        lease.Uses = Lease.Close(lease);            // the setter runs on 'lease' after Close took it
        return l;
    }

    public static Lock RunEvent()
    {
        var l = new Lock();
        Lease lease = new Lease(l);
        lease.Closed += Lease.CloseLater(lease);
        return l;
    }
}
