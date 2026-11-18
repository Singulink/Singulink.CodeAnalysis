using System;
using Singulink.CodeAnalysis;

// The other part of a type, in another file.
public sealed partial class Q01(Guard g) { public Guard F = g; }                                      // E40 partial across files
public sealed partial class Q02 { public Guard F = g; }                                               // E41 the initializer in the part without the parameter list
