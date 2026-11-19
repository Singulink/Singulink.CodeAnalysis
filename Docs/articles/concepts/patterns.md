<div class="article">

# Patterns, Deconstruction and Tuples

How patterns, deconstructions and tuples read from a value, and where one pattern would give one value to two variables.

## Examples

These read from a value without a member being named, and are as new as what they read from:

```cs
if (myLock.TryEnter() is { } guard) { }          // OK: read from a new value

LockGuard? maybe = myLock.TryEnter();            // OK: stored once
if (maybe is { } copy) { }                       // SCA1001: read from a stored nullable
if (NonCopyable.Move(ref maybe) is { } moved) { } // OK
maybe?.Dispose();                                // SCA1003: disposes a copy
if (maybe is { IsHeld: true }) { }               // SCA1003 unless 'IsHeld' is readonly: it runs on a copy
bool any = maybe.HasValue;                       // (the nullable is used again)

var (entered, ok) = myLock.TryEnterTuple();      // OK
var result = myLock.TryEnterTuple();
result.guard.Dispose();                          // OK: in place
LockGuard taken = result.guard;                  // SCA1001
bool entered = result.ok;                        // (the tuple is used again)

if (boxed is LockGuard unboxed) { }              // SCA1001: a copy out of the box
foreach (LockGuard item in guardArray) { }       // SCA1001: a copy of each element; use 'ref'
LockGuard[] part = guardArray[1..];              // SCA1020: a range of an array copies the elements; slice a span
```

## Deconstruction

**A deconstruction runs `Deconstruct` on a copy of its right side,** whatever the right side is: the compiler copies it into a temporary
first, a local included, and calls the method on that. A `Deconstruct` that moves two guards out of a lease leaves them in the lease
that was deconstructed as well. So the right side is a new value, a move or a local at its last use; anything else is reported unless
`Deconstruct` is `readonly` (`SCA1001`). The call written out runs in place.

```cs
var (read, write) = _lease;                         // SCA1001: 'Deconstruct' runs on a copy of the field
var (read, write) = NonCopyable.Move(ref _lease);   // OK: the field is emptied, and the value is moved in
var (read, write) = MakeLease();                    // OK: a new value
_lease.Deconstruct(out var read, out var write);    // OK: in place
```

## One Value Goes to One Variable

A value goes to one variable only. Where one pattern declares two variables that cover the same value, or overlapping parts of it, every
one after the first is a copy (`SCA1001`), whatever the matched value is: the result of a call, a local at its
[last use](last-use.md) or a `[MovedFrom]` parameter. Variables for different parts are fine, and so is one variable for
the whole next to variables for parts that carry no non-copyable type.

```cs
if (MakeLease() is { Inner: var guard } whole) { }                // SCA1001 at 'whole': 'guard' has a part of it
if (myLock.Enter() is var first and var second) { }                // SCA1001 at 'second'
if (MakeLease() is { Inner: var a } and { Inner: var b }) { }     // SCA1001 at 'b'
if (MakeLease() is { Inner: var c, Other: var d }) { }             // OK: two different parts
if (MakeLease() is { Id: var id } lease) { }                       // OK: 'id' is a number
```

Two list patterns over one list can name one element from both ends, so `[var first, ..] and [.., var last]` is reported as well.

**One pattern, and all the clauses of one `switch`, read each member once.** The compiler calls a `Deconstruct` method, a getter or an
indexer once for a whole `is` pattern or a whole `switch`, and gives the result to every pattern that names the same member on the
same input. So what a member hands out is a part of the matched value like a field or a tuple element, also where its code makes a
new value at every call, and it goes to one variable:

```cs
switch (MakeLease())
{
    case (var read, var write) when TryRegister(read, write): break;   // takes the guards, and releases them if it fails
    case (var read2, var write2): read2.Dispose(); break;               // SCA1001 twice: 'Deconstruct' ran once, these are the same two
}

if (pool is { Next: var a } and { Next: var b }) { }                   // SCA1001 at 'b': the getter ran once
if (pool is { Next: var c } && pool is { Next: var d }) { }            // OK: two patterns, two reads
```

## Members That Use a Part of the Value

**A member that is not `readonly`, and a receiver that takes its value by value, use the part that they run on.** The compiler copies
each part that a pattern looks into to a temporary, the value that the part belongs to still holds the original, and every pattern
that names the part shares the temporary. So one pattern, or the clauses of one `switch`, cannot use a part and also take or use the
value around it (`SCA1003`). That holds for a new value as for a stored one, and whatever the order in which the patterns are
written, since the compiler orders the evaluations itself:

```cs
switch (Open())
{
    case { Inner.Released: false }: break;     // 'Released' is not readonly: it runs on a copy of 'Inner'
    case var slot: slot.Dispose(); break;      // SCA1003: 'slot' is the whole value, which still holds the original 'Inner'
}

if (Open() is { Inner: var inner } and (var moved, _)) { }           // SCA1003: 'Deconstruct' on the whole can move 'Inner' out again
if (Open() is { Inner.Released: true } and (var taken, _)) { }       // SCA1003: 'Deconstruct' runs on the original of what 'Released' ran on a copy of
if (Open() is { Inner: { Released: true } held }) { }                // OK: the variable is bound after the member, and holds what it left
if (Open() is { Inner.Released: true } and { Inner.Count: 0 }) { }   // OK: two members on one part run on the one temporary in turn
```

A variable of a clause with a `when` has handed its part on, so a member in a later clause that runs on that part is reported as well.
A `Deconstruct` of an extension that takes its receiver by value is given a copy of the whole value, and is reported next to any
other use of it. Take the value apart in one way only, or in a statement of its own.

An extension method that the compiler calls takes its receiver as an argument, with no argument written: a `GetEnumerator`, a `GetAwaiter` or
a `Deconstruct` that takes a non-copyable type by value copies a stored value (`SCA1003`). The receiver of a member of an extension block
(`extension(LockGuard guard)`) is an argument in the same way, and a parameter of every member of the block: a local that is not used
again after `guard.Release();` is moved into it, and inside the member the receiver is a new value at its last use, like any other
by-value parameter.

## Inline Arrays, Created Values and Copies

An element of an inline array (a struct marked `[InlineArray]`) is used like an element of an array: in place, by reference, or through a span
over the buffer. The buffer is a struct that stores non-copyable values, so it has to be marked `[NonCopyable]` itself. Memory from
`stackalloc` is used through its span in the same way, and new values can be moved into it by its initializer.

A value that is created, such as a tuple, a struct or an object, is as new as what is put into it by its arguments and its initializer. A list
pattern uses the length or count, the indexer and `Slice` of its type, and a collection expression uses the constructor or the create method of
its type and `Add` for each element: these are member uses like the ones that are written out.

`with` on a value of a non-copyable type copies that value: on a stored value it is reported, on a new value it is fine. A local or a
by-value parameter that is not used again after the `with` is a new value there, as at any other
[last use](last-use.md).
<xref:System.Object.MemberwiseClone> is reported (`SCA1005`) in a class that has an instance field of a non-copyable type, its own or inherited, and in a
non-copyable struct. A field that holds a container, such as a `List<LockGuard>`, does not count: the clone shares the list. A class with
no such field is reported too where a derived class of the compilation has one, and with an ID of its own (`SCA1008`) where another
assembly can derive from it.

```cs
var changed = _options with { Size = 1 };        // SCA1003: copies the stored '_options'
var created = CreateOptions() with { Size = 1 }; // OK
var next = options with { Size = 2 };            // OK where 'options' is a local that is not used again
```

</div>
