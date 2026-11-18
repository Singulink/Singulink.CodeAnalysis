using System;

public static class B1
{
    static bool Handle(Guard g) { g.Dispose(); return true; }
    static void Fail() => throw new InvalidOperationException();
    static void Work(ref Guard g) { try { Fail(); } finally { g.Dispose(); } }

    public static Lock RunInnerFinally()
    {
        var l = new Lock();
        Guard g = l.Enter();

        try
        {
            try { Fail(); }
            finally { g.Dispose(); }                // runs after the filter below
        }
        catch (Exception) when (Handle(g)) { }      // taken for the last use of 'g'

        return l;
    }

    public static Lock RunCallee()
    {
        var l = new Lock();
        Guard g = l.Enter();

        try { Work(ref g); }                        // its 'finally' runs after the filter
        catch (Exception) when (Handle(g)) { }

        return l;
    }

    public static Lock RunTwice()
    {
        var l = new Lock();
        Guard g = l.Enter();

        try
        {
            try { Fail(); }
            finally { Fail(); }                     // a second exception: the filter runs again
        }
        catch (Exception) when (Handle(g)) { }

        return l;
    }
}
