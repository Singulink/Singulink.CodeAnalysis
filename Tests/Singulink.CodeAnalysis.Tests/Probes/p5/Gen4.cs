using System;
using System.Threading.Tasks;
using Singulink.CodeAnalysis;

public interface ISource<[NonCopyable] T> { T Current { get; } bool TryTake(out T value); }

public sealed class SharedSource : ISource<Task<Guard>>
{
    private static readonly Task<Guard> s_t = Owner.EnterTaskAsync();
    public Task<Guard> Current => s_t;                                   // K01
    public bool TryTake(out Task<Guard> value) { value = s_t; return true; }   // K02
}

public static class Gen4
{
    private static ISource<Task<Guard>> Open() => new SharedSource();

    public static async Task Use()
    {
        Guard a = await Open().Current;                                  // K03
        Guard b = await Open().Current;                                  // K04
        Guard c = await new SharedSource().Current;                      // K05
    }
}
