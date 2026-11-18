public static class A2
{
    // Takes the guard over; on failure it releases what it was given.
    static bool TryRegister(Guard g) { g.Dispose(); return false; }

    static bool TryRelease(ref Guard g) { g.Dispose(); return false; }

    public static Lock Run()
    {
        var l = new Lock();

        switch (l.Enter())
        {
            case var registered when TryRegister(registered):
                break;
            case var rejected:              // a second variable for the same value
                rejected.Dispose();
                break;
        }

        return l;
    }

    public static Lock RunInPlace()
    {
        var l = new Lock();

        switch (l.Enter())
        {
            case var a when TryRelease(ref a):
                break;
            case var b:
                b.Dispose();
                break;
        }

        return l;
    }

    public static Lock RunExpression()
    {
        var l = new Lock();
        bool done = l.Enter() switch { var a when TryRegister(a) => true, var b => TryRegister(b) };
        return l;
    }
}
