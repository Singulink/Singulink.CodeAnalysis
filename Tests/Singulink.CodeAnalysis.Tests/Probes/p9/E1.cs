using System;
using System.Collections.Generic;
using Singulink.CodeAnalysis;

// Primary constructors.
public class PBase { public Guard G; public PBase(Guard g) => G = g; public PBase(ref Guard g) { G = NonCopyable.Move(ref g); } public PBase(in Guard g, int x) { } }
public class PBase2(Guard g) { public Guard Get() => g; }                                           // E01 a member uses it: hidden field (fine)

public sealed class P01(Guard g) { public Guard F = g; }                                              // E02 clean
public sealed class P02(Guard g) : PBase(g) { }                                                       // E03 base arguments, clean
public sealed class P03(Guard g) : PBase(ref g) { }                                                   // E04 by ref to the base
public sealed class P04(Guard g) : PBase(in g, 0) { public Guard F = g; }                             // E05 in to the base and a field
public sealed class P05(Guard g) : PBase2(g) { public Guard F = g; }                                  // E06 base and field
public sealed class P06(Guard g) { public Func<bool> F = () => g.IsHeld; }                            // E07 lambda in an initializer
public sealed class P07(Guard g) { public Guard P { get; init; } = g; }                               // E08 init property initializer
public sealed class P08(Guard g) { public Guard P { get => field; set => field = value; } = g; }     // E09 field-keyword property
public sealed partial class P09(Guard g) { public Guard F = g; }                                     // E10 partial, other part uses g
public sealed partial class P09 { public bool Held => g.IsHeld; }
public sealed class P10(Guard g) { public string S = $"{nameof(g)}"; public Guard F = g; }            // E11 nameof + a field
public sealed class P11(Guard g) { public Guard g = g; public void M() => g.Dispose(); }             // E12 same name as a field
public sealed class P12<T>(Guard g) { public Guard F = g; }                                           // E13 generic
public sealed class Outer { public sealed class P13(Guard g) { public Guard F = g; } }              // E14 nested
[NonCopyable] public struct P14(Guard g) { public Guard F = g; }                                     // E15 non-copyable struct
public record class P15(Guard G);                                                                     // E16 record
public sealed class P16(in Guard g) { public Guard F = g; }                                           // E17 'in' primary ctor parameter
public sealed class P17(Guard g) { public Guard F = g; public P17() : this(default) { } }             // E18 other ctor chains
public sealed class P18(Guard g) { public (Guard, int) F = (g, 1); }                                 // E19 tuple
public sealed class P19(Guard g) { public Guard F = g; public void M(Guard g) { } }                 // E20 parameter of a member with the same name
public sealed class P20(Guard g) { public Guard? F = g; }                                             // E21 nullable
public sealed class P21(Guard g) { public object F = g; }                                             // E22 box
public sealed class P22(Guard g) { public Guard F = g; public static Guard S(Guard g) => g; }       // E23 static member parameter of same name
public sealed class P23(Guard g) { public Guard[] A = [g]; }                                          // E24 collection expression
public sealed class P24(Guard g) { public bool H = g.IsHeld; }                                        // E25 a read in place
public sealed class P25(Guard g) { public List<Guard> L = new() { g }; }                              // E26 collection initializer (List.Add listed?)
public sealed class P26(Guard g) { public Guard F = Sink.Echo(g); }                                  // E27 pass by value
public sealed class P27(Guard g) { public Guard F = g; public Guard G = Sink.Held; }                 // E28
public sealed class P28(Guard g) { public Guard F = g is { IsHeld: true } x ? x : default; }       // E29 pattern
public sealed class P29(Guard g) { public Guard F = (g); }                                           // E30
public sealed class P30(Guard @g) { public Guard F = @g; }                                           // E31 verbatim
public sealed class P31(Guard g) { public Guard F = g; }                                         // E32 escaped identifier
public sealed class P32(Guard g) { public Guard F = g; public class N { public Guard X; } }         // E33 nested type
