using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

internal class GapC
{
    private static List<Guard> CreateGuards() => new();

    private void Boxed()
    {
        IEnumerator e = CreateGuards().GetEnumerator();       // C01 a new List<Guard>.Enumerator moved into a box
        e.MoveNext();
        object copy = e.Current;                               // C02 a boxed copy of a guard, every time it is read

        ITuple tuple = (Guard.Create(), 1);                    // C03 a new tuple moved into a box
        object first = tuple[0];                               // C04 a boxed copy of the guard in the tuple
        ((IDisposable)first!).Dispose();
        ((IDisposable)tuple[0]!).Dispose();                    // C05 a second copy disposed
    }
}

internal class Ops
{
    private Counter _counter;

    private void Go(Counter other)
    {
        Counter sum = Counter.Make() + 1;                      // O01 new operand, new result
        Counter again = _counter + 1;                          // O02 stored operand by value
        Counter viaIn = _counter - 1;                          // O03 stored operand through 'in'
        _counter = Counter.Make() + 2;                         // O04
        bool same = _counter == other;                         // O05
    }
}

[Singulink.CodeAnalysis.NonCopyable]
public struct Counter
{
    public int Value;
    public static Counter Make() => default;
    public static Counter operator +(Counter left, int right) => default;
    public static Counter operator -(in Counter left, int right) => default;
    public static bool operator ==(Counter left, Counter right) => true;
    public static bool operator !=(Counter left, Counter right) => false;
    public override bool Equals(object? obj) => false;
    public override int GetHashCode() => 0;
}
