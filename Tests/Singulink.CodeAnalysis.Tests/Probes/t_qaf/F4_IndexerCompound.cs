// F4: a compound assignment or ++ on an indexer passes each by-value argument to the getter and to the setter.
public sealed class CountsByLease
{
    public int this[Guard lease]
    {
        get { lease.Dispose(); return 0; }         // by-value parameters: each accessor owns what it is given
        set { lease.Dispose(); }
    }
}

public static class RTF4
{
    public static Lock CompoundAssignment()
    {
        var l = new Lock();
        var counts = new CountsByLease();
        counts[l.Enter()] += 1;                    // no diagnostic
        return l;
    }

    public static Lock IncrementAtLastUse()
    {
        var l = new Lock();
        var counts = new CountsByLease();
        var g = l.Enter();
        counts[g]++;                               // no diagnostic
        return l;
    }
}
