using System.Collections.Generic;
using System.Threading.Tasks;

internal class Probe5
{
    private static Task<Guard> EnterTaskAsync() => null!;

    private async Task FromResult(Task<Guard> stored, List<Task<Guard>> typed)
    {
        Guard f1 = await await Task.FromResult(stored);             // F01 stored task inside a new task
        Guard f2 = await await Task.FromResult(EnterTaskAsync());   // F02 allowed: everything new
        Guard f3 = await Task.FromResult(typed).Result[0];          // F03
        Guard f4 = await Identity(stored);                          // F04 own unmarked generic
        Guard f5 = await typed.Find(t => true)!;                    // F05 unlisted List member
    }

    private static T Identity<T>(T value) => value;
}
