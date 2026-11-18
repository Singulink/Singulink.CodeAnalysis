using System;

public static class A3
{
    public static event Action<Guard> Entered;

    static void Close(Guard g) => g.Dispose();

    public static Lock Run()
    {
        var l = new Lock();
        Action<Guard> onEntered = Close;
        onEntered += Close;
        onEntered(l.Enter());           // every target gets its own copy of the argument
        return l;
    }

    public static Lock RunEvent()
    {
        var l = new Lock();
        Entered += Close;
        Entered += static g => g.Dispose();
        Entered?.Invoke(l.Enter());
        Entered = null;
        return l;
    }
}
