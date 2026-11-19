<div class="article">

# Differences From Roslyn's Rule

The rules for values come from the `RS0042` rule ("Do not copy value") of Roslyn's own analyzers, which are made for use inside the Roslyn repository and are not documented or supported for other projects. This page lists how this analyzer differs from it, for C#. Where the RS0042 column says *Not described*, the row says what this analyzer does, and this page does not describe what RS0042 does there.

## Differences

| Topic | This analyzer | RS0042 |
|---|---|---|
| IDs | One ID per kind of warning | One ID |
| Generic structs | A generic struct over a non-copyable type is a non-copyable type | Knows only <xref:System.Nullable`1>, which it forbids altogether, and misses copies of tuples and value tasks |
| Generic code | Type parameters can be marked, generics that are not marked get a warning unless they are on the [symbol list](symbol-list-format.md), and containers are followed through conversions | Does not look at generic code |
| Value tasks | Awaiting a stored <xref:System.Threading.Tasks.ValueTask`1> gets a warning | Treats it as a new value |
| Declared variables | A variable that a pattern, a deconstruction or a `foreach` declares is checked | Misses the copy out of a box by a pattern, and the copy of an array element by `foreach` |
| Boxes | Taking a value out of a box gets a warning for every box | Warns only when the box is read from a variable or a field |
| Returned variables | A lambda, a local function or an iterator that returns a variable which lives on gets a warning | Lets every local and by-value parameter be returned |
| Captured returns | A method that returns a variable which one of its lambdas or local functions captures gets a warning | Lets every local and by-value parameter be returned |
| The last use | A local or a by-value parameter is a new value at its last use, on the paths of the compiler's control-flow graph | Warns about every by-value read of a local except a returned one |
| Iterator parameters | A by-value parameter of an iterator gets a warning | No warning |
| Primary constructors | A parameter of a primary constructor that a copyable struct keeps gets a warning | No warning |
| Default interface members | A struct that leaves an interface member to its default implementation gets a warning | No warning |
| Static fields | A static field of a non-copyable type in a copyable struct is allowed | Warns |
| Out arguments | An `out` argument of a non-copyable type is allowed | Warns about every one |
| New values | A new value is moved into an array, a tuple, a collection expression, a box, an interpolated string hole, or an operator or conversion | Warns there unless it is an object creation |
| Operator results | What a user-defined operator returns is a new value, as the result of any call |  Not described |
| Conditionals | A `switch` expression or a `??` whose operands all give new values is a new value, as a conditional operator (`?:`) is |  Not described |
| Read-only references | A member on a read-only reference has its own ID |  Not described |
| Readonly fields in constructors | No warning for a `readonly` field in a constructor of its type, where the compiler makes no copy |  Not described |
| Members the compiler calls | The members that the compiler calls for `await`, an index and a range are checked through a read-only reference as well |  Not described |
| Nullable returns | A local or a by-value parameter can be returned as a <xref:System.Nullable`1> of its type |  Not described |
| With | `with` on a stored value gets one warning, and on a new value it is allowed | Warns three times about every `with` |
| Records and clones | A field of a non-copyable type in a record, and a <xref:System.Object.MemberwiseClone> that copies one, get a warning |  Not described |
| Framework structs | <xref:System.Threading.SpinLock> and <xref:System.Runtime.InteropServices.GCHandle> are entries of the [built-in framework symbol list](framework-symbol-list.md), with eight more framework structs, and a project can leave them all out |  Not described |
| Arglist | A non-copyable value passed in an `__arglist` gets a warning | Fails on it |

</div>
