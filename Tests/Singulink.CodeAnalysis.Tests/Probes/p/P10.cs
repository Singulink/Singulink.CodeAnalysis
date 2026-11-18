using Singulink.CodeAnalysis;
static class P10
{
    public static T G1<[NonCopyable] T>([MovedFrom] ref T v, T[] arr)
    {
        int i = 0;
    again:
        arr[i] = v;                                              // AC1 hole? read twice via goto loop
        if (++i < 2) goto again;
        v = default!;
        return default!;
    }
}
