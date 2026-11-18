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
