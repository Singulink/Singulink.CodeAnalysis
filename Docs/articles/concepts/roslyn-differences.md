<div class="article">

# Differences From Roslyn's Rule

Where the rules for values come from, and how they differ from it.

## The Differences

The rules for values come from the `RS0042` rule ("Do not copy value") of Roslyn's own analyzers, which are made for use inside the Roslyn repository
and are not documented or supported for other projects. They differ from it, for C#, in these ways:

- The one ID is split into one ID per kind of finding.
- A generic struct over a non-copyable type is a non-copyable type. `RS0042` only knows <xref:System.Nullable`1>, which it forbids altogether, and misses
  copies of tuples and value tasks.
- Type parameters can be marked, generics that are not marked are reported unless listed, and containers are followed through conversions.
  `RS0042` does not look at generic code.
- Awaiting a stored <xref:System.Threading.Tasks.ValueTask`1> is reported. `RS0042` takes it for a new value.
- A variable that a pattern, a deconstruction or a `foreach` declares is checked. `RS0042` misses the copy out of a box by a pattern and the
  copy of an array element by `foreach`.
- Taking a value out of a box is reported for every box. `RS0042` only reports it when the box is read from a variable or a field.
- A lambda, a local function or an iterator that returns a variable which lives on is reported, and so is a method that returns a variable
  which one of its lambdas or local functions captures. `RS0042` lets every local and by-value parameter be returned.
- A local or a by-value parameter is a new value where it is used for the last time, on the paths of the compiler's control-flow graph.
  `RS0042` reports every by-value read of a local but a returned one.
- A by-value parameter of an iterator, a parameter of a primary constructor that a copyable struct keeps, and a struct that leaves an interface
  member to its default implementation are reported.
- A static field of a non-copyable type in a copyable struct is allowed. `RS0042` reports it.
- An `out` argument of a non-copyable type is allowed. `RS0042` reports every one.
- A new value is moved into an array, a tuple, a collection expression, a box, a hole of an interpolated string or a user-defined
  operator or conversion, as into a parameter, and what a user-defined operator returns is a new value, as the result of any call. `RS0042`
  reports it there unless it is an object creation.
- A `switch` expression or a `??` whose operands all give new values is a new value, as a conditional operator (`?:`) is.
- A member on a read-only reference has its own ID, and is not reported for a `readonly` field in a constructor of its type, where the
  compiler makes no copy. The members that the compiler calls for `await`, an index and a range are checked as well.
- A local or a by-value parameter can be returned as a <xref:System.Nullable`1> of its type.
- `with` on a stored value is reported once, and on a new value it is allowed. `RS0042` reports every `with` three times.
- A field of a non-copyable type in a record and a <xref:System.Object.MemberwiseClone> that copies one are reported.
- <xref:System.Threading.SpinLock> and <xref:System.Runtime.InteropServices.GCHandle> are entries of the built-in list, with eight more framework structs, and a project can leave them all out.
- A non-copyable value passed in an `__arglist` is reported. `RS0042` fails on it.

</div>
