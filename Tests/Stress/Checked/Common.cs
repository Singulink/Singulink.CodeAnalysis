using System;
using System.Collections.Generic;
using Singulink.CodeAnalysis;

public sealed class Token { public int Releases; }

public sealed class Lock
{
    public readonly List<Token> Tokens = [];
    public Guard Enter() { var t = new Token(); Tokens.Add(t); return new Guard(t); }
    public string Report() { int r = 0, twice = 0; foreach (var t in Tokens) { r += t.Releases; if (t.Releases > 1) twice++; } return $"acquires={Tokens.Count} releases={r}" + (twice > 0 ? " TWICE" : ""); }
}

[NonCopyable]
public struct Guard : IDisposable
{
    private Token? _token;
    public Guard(Token token) => _token = token;
    public readonly bool IsHeld => _token is not null;
    public void Dispose() { if (_token is { } t) { _token = null; t.Releases++; } }
}

public static class Run
{
    public static void Case(string name, Action<Lock> body)
    {
        var l = new Lock(); string end = "returned";
        try { body(l); } catch (Exception e) { end = e.GetType().Name; }
        Console.WriteLine($"{name}: {end} {l.Report()}");
    }
}
