using System;
using Singulink.CodeAnalysis;

var l = new Lock();
int n = -1;
Index idx = default;
Guard g = l.Enter();
try { Work(); Sink.Held = g; }                     // T01 clean: commit at the end of a try
catch { g.Dispose(); throw; }
Sink.Held.Dispose(); Console.WriteLine("after T01: " + l);

Guard h = l.Enter();
try { Work(); Sink.Held = h; idx = ^n; }           // T02 ^n after the store, in top-level code
catch { h.Dispose(); }
Sink.Held.Dispose(); Console.WriteLine("after T02: " + l);

Guard k = l.Enter();
try { Work(); Sink.Held = k; n = 1; }              // T03 a local after the store
catch { k.Dispose(); throw; }

static void Work() { }
