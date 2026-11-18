using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Singulink.CodeAnalysis;

[NonCopyable]
public struct Guard : IDisposable
{
    private int _state;
    public bool IsHeld => _state != 0;
    public void Dispose() => _state = 0;
    public void Touch() => _state++;
}

public static class Owner
{
    public static Guard Enter() => default;
    public static Task<Guard> EnterTaskAsync() => Task.FromResult(default(Guard));
    public static ValueTask<Guard> EnterAsync() => default;
    public static List<Guard> CreateGuards() => new List<Guard>();
}

public sealed class Bag<[NonCopyable] T>
{
    private T[] _items = new T[16];
    private int _n;
    public void Add(T item) => _items[_n++] = NonCopyable.Move(ref item);
    public T Take() => NonCopyable.Move(ref _items[--_n]);
    public ref T this[int i] => ref _items[i];
    public ref T Peek() => ref _items[_n - 1];
}

public static class Stash<[NonCopyable] T>
{
    public static T Slot = default!;
    public static void Store(T value) => Slot = NonCopyable.Move(ref value);
}

public static class Helpers
{
    public static void Put<[NonCopyable] T>(ref T dst, [MovedFrom] ref T src) => dst = NonCopyable.Move(ref src);
    public static void Swap2<[NonCopyable] T>(ref T a, ref T b) { T t = NonCopyable.Move(ref a); a = NonCopyable.Move(ref b); b = NonCopyable.Move(ref t); }
    public static void Fetch<[NonCopyable] T>(out T x) => x = NonCopyable.Move(ref Stash<T>.Slot);
}
