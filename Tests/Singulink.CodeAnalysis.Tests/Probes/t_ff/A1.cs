using System.Threading.Tasks;

public ref struct Releaser
{
    private ref Guard _guard;
    public Releaser(ref Guard guard) { _guard = ref guard; }
    public void Dispose() => _guard.Dispose();
}

public static class A1
{
    static Guard Enter(Lock l)
    {
        Guard g = l.Enter();
        try { return g; }               // the value to return is copied out here
        finally { g.Dispose(); }        // and the variable is used afterwards
    }

    static async Task<Guard> EnterAsync(Lock l)
    {
        Guard g = l.Enter();
        try { await Task.Yield(); return g; }
        finally { g.Dispose(); }
    }

    static Guard EnterScoped(Lock l)
    {
        Guard g = l.Enter();
        using var releaser = new Releaser(ref g);   // releases 'g' at the end of the scope
        return g;
    }

    public static Lock RunScoped() { var l = new Lock(); Guard r = EnterScoped(l); r.Dispose(); return l; }

    public static Lock Run() { var l = new Lock(); Guard r = Enter(l); r.Dispose(); return l; }

    public static Lock RunAsync() { var l = new Lock(); Guard r = EnterAsync(l).GetAwaiter().GetResult(); r.Dispose(); return l; }
}
