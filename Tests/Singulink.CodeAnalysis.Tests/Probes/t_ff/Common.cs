using System;
using Singulink.CodeAnalysis;

public sealed class Lock
{
    public int Acquires, Releases;
    public Guard Enter() { Acquires++; return new Guard(this); }
    public override string ToString() => $"acquires={Acquires} releases={Releases}";
}

[NonCopyable]
public struct Guard : IDisposable
{
    private Lock _lock;
    public Guard(Lock l) => _lock = l;
    public void Dispose() { if (_lock is { } l) { _lock = null; l.Releases++; } }
}

public static class Program
{
    public static void Main()
    {
        Run("A1 return in try/finally", A1.Run);
        Run("A1 return in try/finally (async)", A1.RunAsync);
        Run("A1 return inside a 'using' scope", A1.RunScoped);
        Run("A2 switch with when (by value)", A2.Run);
        Run("A2 switch with when (in place)", A2.RunInPlace);
        Run("A2 switch expression", A2.RunExpression);
        Run("A3 multicast delegate", A3.Run);
        Run("A3 event", A3.RunEvent);
        Run("B1 filter before inner finally", B1.RunInnerFinally);
        Run("B1 filter before callee finally", B1.RunCallee);
        Run("B1 filter runs twice", B1.RunTwice);
        Run("B2 setter after value", B2.Run);
        Run("B2 event accessor after value", B2.RunEvent);
        Run("B3 user-defined &&", B3.Run);
        Run("B4 handler argument", B4.Run);
        Run("B5 'is ()' after commit", B5.RunCommit);
        Run("B5 'is ()' in [MovedFrom]", B5.RunMovedFrom);
        Run("B6 Interlocked.Exchange<struct>", B6.Run);
        Run("B6 class named Interlocked", B6.RunOwn);
        Run("B7 deconstruction store", B7.Run);
    }

    private static void Run(string name, Func<Lock> test)
    {
        var l = test();
        Console.WriteLine($"{name,-36} {l}{(l.Releases > l.Acquires ? "   <-- released twice" : "")}");
    }
}
