using System.Collections.Generic;
using System.Threading.Tasks;

internal class Probe10
{
    private readonly Dictionary<string, List<Guard>> _byKey = new();
    private Task<Guard> _cached = null!;

    private bool TryGetGuards(string key, out List<Guard> guards) => _byKey.TryGetValue(key, out guards!);   // M01
    private void GetCached(out Task<Guard> task) => task = _cached;                                          // M02

    private async Task Use()
    {
        if (TryGetGuards("a", out var guards))
        {
            foreach (Guard g in guards) { }          // M03
            Guard first = guards[0];                 // M04
            guards.Add(Owner.Enter());               // M05 in place
        }

        GetCached(out var task);
        Guard a = await task;                        // M06
        GetCached(out Task<Guard> again);
        Guard b = again.Result;                      // M07
    }
}
