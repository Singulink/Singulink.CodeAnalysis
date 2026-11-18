using System.Runtime.CompilerServices;

[InterpolatedStringHandler]
public ref struct GuardMessage
{
    public GuardMessage(int literalLength, int formattedCount, Guard guard) { guard.Dispose(); }
    public void AppendLiteral(string text) { }
    public void AppendFormatted(int value) { }
}

public static class B4
{
    static void Close(Guard guard, [InterpolatedStringHandlerArgument("guard")] GuardMessage message) => guard.Dispose();

    public static Lock Run()
    {
        var l = new Lock();
        Close(l.Enter(), $"closing {1}");           // the argument goes to 'Close' and to the constructor of the handler
        return l;
    }
}
