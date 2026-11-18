using System;
using System.Threading.Tasks;
using Singulink.CodeAnalysis;

internal static class DiscordCase
{
    public static async Task Main2()
    {
        var s = new S();
        Console.WriteLine($"Main: {s.i}");        // X01
        await s.M();                              // X02
        Console.WriteLine($"Main: {s.i}");        // X03
    }
}

[NonCopyable]
internal struct S
{
    public int i;

    public async Task M()                         // X04
    {
        Console.WriteLine($"Inside: {this.i}");
        this.i = 4;
        await Task.Yield();
        this.i = 5;
    }
}

internal struct Unmarked
{
    public int i;

    public async Task M()                         // X05 not a non-copyable struct
    {
        await Task.Yield();
        this.i = 5;
    }
}
