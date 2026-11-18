using System.Runtime.CompilerServices;

// F2: [InterpolatedStringHandlerArgument] makes the compiler pass one value to the method and, again, to the handler's constructor.
[InterpolatedStringHandler]
public ref struct ReleaseMessage
{
    public ReleaseMessage(int literalLength, int formattedCount, Guard guard)
    {
        guard.Dispose();                           // a by-value parameter: the handler owns what it is given
    }

    public void AppendLiteral(string s) { }
    public void AppendFormatted<T>(T value) { }
}

public static class Releaser
{
    public static void Release(Guard guard, [InterpolatedStringHandlerArgument("guard")] ReleaseMessage message) => guard.Dispose();
}

public sealed class ReleasingLogger
{
    public ReleasingLogger(Guard guard, [InterpolatedStringHandlerArgument("guard")] ReleaseMessage message) => guard.Dispose();
}

// The receiver form: "" names 'this', which the compiler passes to the handler by value.
[Singulink.CodeAnalysis.NonCopyable]
public struct Session
{
    private Guard _guard;
    public Session(Guard guard) => _guard = guard;
    public void Log([InterpolatedStringHandlerArgument("")] SessionMessage message) { }
    public void End() => _guard.Dispose();
    public void TakeOver() => _guard.Dispose();
}

[InterpolatedStringHandler]
public ref struct SessionMessage
{
    public SessionMessage(int literalLength, int formattedCount, Session session) => session.TakeOver();
    public void AppendLiteral(string s) { }
}

public static class RTF2
{
    public static Lock NewArgument()
    {
        var l = new Lock();
        Releaser.Release(l.Enter(), $"releasing");     // no diagnostic
        return l;
    }

    public static Lock LastUseArgument()
    {
        var l = new Lock();
        var held = l.Enter();
        Releaser.Release(held, $"releasing");          // no diagnostic
        return l;
    }

    public static Lock ConstructorArgument()
    {
        var l = new Lock();
        _ = new ReleasingLogger(l.Enter(), $"x");      // no diagnostic
        return l;
    }

    public static Lock Receiver()
    {
        var l = new Lock();
        var session = new Session(l.Enter());
        session.Log($"working");                       // no diagnostic: the handler gets a copy of 'session'
        session.End();
        return l;
    }
}
