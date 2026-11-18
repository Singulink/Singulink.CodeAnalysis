using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Singulink.CodeAnalysis;

[InlineArray(4)] struct GuardBuf { private Guard _e0; }                         // O01 SCA1010
[NonCopyable, InlineArray(4)] struct GuardBuf2 { private Guard _e0; }

class P13
{
    private GuardBuf2 _buf;
    private Guard[] _arr = new Guard[2];

    async Task M(IAsyncEnumerable<Guard> ae, IEnumerable<Guard> e)
    {
        Guard a = _buf[0];                                                     // O02 SCA1001
        _buf[1].Dispose();                                                     // O03 fine
        foreach (Guard g in _buf) { }                                          // O04 copy
        foreach (ref Guard g in _buf) { g.Dispose(); }                         // O05 fine
        Span<Guard> s = _buf;                                                  // O06 fine
        var copy = _buf;                                                       // O07 SCA1001
        await foreach (Guard g in ae.ConfigureAwait(false)) { }                // O08 SCA1030
        var q = from g in e select g.IsHeld;                                   // O09 SCA1020
        if (_arr is [var first, ..]) { }                                       // O10 copy
        if (_arr.AsSpan() is [_, { IsHeld: true }]) { }                        // O11 fine?
        Func<Guard, int> lam = (Guard g = default) => 1;                       // O12
        scoped ref Guard r = ref _arr[0];
        Guard? n = null;
        var tupled = (n, 1) switch { ({ } x, _) => 1, _ => 0 };                // O13
    }
}
