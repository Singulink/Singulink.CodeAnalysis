using Lib;

var l = new Lock();
var g = l.Enter();
var copy = g;            // C01
copy.Dispose();
g.Dispose();
System.Console.WriteLine($"copy: {l}");

var l2 = new Lock();
var h = new Holder();
h.Add(l2);
foreach (Guard x in h.Guards()) x.Dispose();   // C02
h.ReleaseAll();
System.Console.WriteLine($"borrowed: {l2}");
