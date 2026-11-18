using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Singulink.CodeAnalysis;

[NonCopyable]
public struct Guard : IDisposable
{
    public int State;
    public bool IsHeld => State != 0;
    public void Dispose() => State = 0;
    public void Touch() => State++;
}

public static class Src
{
    public static Guard Enter() => default;
    public static Guard? TryEnter() => default;
    public static Task<Guard> EnterTaskAsync() => Task.FromResult(Enter());
    public static ValueTask<Guard> EnterAsync() => new ValueTask<Guard>(Enter());
    public static List<Guard> CreateList() => new List<Guard>();
    public static Guard[] CreateArray() => new Guard[4];
}
