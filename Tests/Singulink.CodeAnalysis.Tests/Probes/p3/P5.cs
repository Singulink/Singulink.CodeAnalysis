using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Singulink.CodeAnalysis;

[NonCopyable] ref struct RGuard
{
    private int _s;
    public void Dispose() => _s = 0;
    public void Touch() => _s++;
}

ref struct RHolder { public RGuard G; }                                           // F00 SCA1010
ref struct RRef { public ref Guard R; }                                           // F01 fine

static class R5
{
    public static RGuard Make() => default;
    public static void Use<[NonCopyable] T>(ref T t) where T : allows ref struct { }
    public static T Dup<T>(scoped ref T t) where T : allows ref struct => t;     // unmarked
    public static T DupM<[NonCopyable] T>(scoped ref T t) where T : allows ref struct => t;   // F02 SCA1002

    public static void M()
    {
        var a = Make();
        var b = a;                                                                 // F03 SCA1001
        var c = NonCopyable.Move(ref a);                                           // F04 fine
        Use(ref c);                                                                // F05 fine
        var d = Dup(ref c);                                                        // F06 SCA1020
        using var e = Make();                                                      // F07 fine
        Take(c);                                                                   // F08 SCA1003
        Take(Make());                                                              // F09 fine
        RHolder h = default;
        h.G = Make();
        var g = h.G;                                                               // F10 SCA1001
    }

    static void Take(RGuard g) { }
}

[CollectionBuilder(typeof(BagBuilder), nameof(BagBuilder.Create))]
sealed class Bag5<T> : IEnumerable<T>
{
    public IEnumerator<T> GetEnumerator() => throw null!;
    IEnumerator IEnumerable.GetEnumerator() => throw null!;
}

static class BagBuilder
{
    public static Bag5<T> Create<T>(ReadOnlySpan<T> items) => new();
}

[CollectionBuilder(typeof(MBagBuilder), nameof(MBagBuilder.Create))]
sealed class MBag5<[NonCopyable] T> : IEnumerable<T>
{
    public IEnumerator<T> GetEnumerator() => throw null!;                         // (body) 
    IEnumerator IEnumerable.GetEnumerator() => throw null!;
}

static class MBagBuilder
{
    public static MBag5<T> Create<[NonCopyable] T>(ReadOnlySpan<T> items) => new();
}

interface IFactory<[NonCopyable] TSelf> where TSelf : IFactory<TSelf>
{
    static abstract TSelf Create();
    static abstract TSelf operator +(TSelf a, TSelf b);
}

[NonCopyable] struct FGuard : IFactory<FGuard>
{
    private static FGuard s_cached;
    public static FGuard Create() => s_cached;                                     // F20 SCA1002
    public static FGuard operator +(FGuard a, FGuard b) => a;                      // F21 fine? returns by-value param
}

static class F5
{
    public static T Make<[NonCopyable] T>() where T : IFactory<T> => T.Create();   // F22 fine
    public static T Add<[NonCopyable] T>(ref T a, ref T b) where T : IFactory<T> => a + b;  // F23 SCA1003 x2
}

partial class Part5
{
    private Guard _g;
    public partial Part5(Guard g);
    public partial Part5(Guard g) { _g = g; }                                     // F30 SCA1001
    public partial event Action? E;
    public partial event Action? E { add { } remove { } }
    partial void PM<[NonCopyable] T>(T x);
    partial void PM<T>(T x) { var y = x; }                                        // F31 SCA1001 (mark from the other part)
}

class P5
{
    private Guard[] _arr = new Guard[2];
    void M()
    {
        Bag5<Guard> b = [Owner.Enter()];                                           // F40 SCA1020 (Create unmarked)
        MBag5<Guard> mb = [Owner.Enter()];                                         // F41 fine
        MBag5<Guard> mb2 = [.. _arr];                                              // F42 spread stored
        IEnumerable<Guard> e = [Owner.Enter()];                                    // F43
        IList<Guard> l = [Owner.Enter()];                                          // F44
        IReadOnlyList<Guard> r = [Owner.Enter(), Owner.Enter()];                  // F45
        IEnumerable<Guard> e2 = [.. _arr];                                         // F46 spread stored
        ICollection<Guard> c = [];                                                 // F47
        Guard x = r[0];                                                            // F48 SCA1030
        IList il = (IList)r;                                                       // F49 SCA1022
    }
}
