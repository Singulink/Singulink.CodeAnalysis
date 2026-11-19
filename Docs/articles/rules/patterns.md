<div class="article">

# Patterns, Deconstruction and Tuples

Patterns, deconstructions and tuples read from a value without naming a member. This page says how they read, and where one pattern would give one value to two variables.

### At a Glance

- **What a pattern or a deconstruction reads is as new as what it reads from:** a part of a new value is new, a part of a stored value is a copy.
- **A deconstruction runs `Deconstruct` on a copy** of its right side. That is fine for a new value, a move or a local at its last use. Anything else gets a warning unless `Deconstruct` is `readonly` (SCA1001).
- **One value goes to one variable.** A second variable for the same value, or for an overlapping part of it, is a copy (SCA1001).
- **One pattern, and all the clauses of one `switch`, read each member once,** and share what each member hands out.
- **A member that is not `readonly` runs on a copy of the part** that it is used on, so one pattern cannot use a part and the value around it (SCA1003).

## Examples

These read from a value without naming a member, and are as new as what they read from:

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
bool wasEntered = result.ok;                     // (the tuple is used again)

if (boxed is LockGuard unboxed) { }              // SCA1001: a copy out of the box
foreach (LockGuard item in guardArray) { }       // SCA1001: a copy of each element; use 'ref'
LockGuard[] part = guardArray[1..];              // SCA1020: a range of an array copies the elements; slice a span
```

## Deconstruction

**A deconstruction runs `Deconstruct` on a copy of its right side,** whatever the right side is. The compiler first copies the right side into a temporary, even when it is a local, and calls the method on that temporary. A `Deconstruct` that moves two guards out of a lease leaves them in the lease that was deconstructed as well.

So the right side must be a new value, a move or a local at its last use. Anything else gets a warning unless `Deconstruct` is `readonly` (SCA1001). A call of `Deconstruct` written out in the code runs in place.

```cs
var (read, write) = _lease;                         // SCA1001: 'Deconstruct' runs on a copy of the field
var (read, write) = NonCopyable.Move(ref _lease);   // OK: the field is emptied, and the value is moved in
var (read, write) = MakeLease();                    // OK: a new value
_lease.Deconstruct(out var read, out var write);    // OK: in place
```

## One Value Goes to One Variable

A value goes to one variable only. Where one pattern declares two variables that cover the same value, or overlapping parts of it, every one after the first is a copy (SCA1001). That holds whatever the matched value is: the result of a call, a local at its [last use](last-use.md) or a `[MovedFrom]` parameter. Variables for different parts are fine, and so is one variable for the whole next to variables for parts that carry no non-copyable type.

```cs
if (MakeLease() is { Inner: var guard } whole) { }                // SCA1001 at 'whole': 'guard' has a part of it
if (myLock.Enter() is var first and var second) { }                // SCA1001 at 'second'
if (MakeLease() is { Inner: var a } and { Inner: var b }) { }     // SCA1001 at 'b'
if (MakeLease() is { Inner: var c, Other: var d }) { }             // OK: two different parts
if (MakeLease() is { Id: var id } lease) { }                       // OK: 'id' is a number
```

Two list patterns over one list can name one element from both ends, so `[var first, ..] and [.., var last]` gets a warning as well.

The same holds across the clauses of a `switch`: see [Clauses of a Switch](compiler-copies.md#clauses-of-a-switch).

#### Each Member Is Read Once

**One pattern, and all the clauses of one `switch`, read each member once.** The compiler calls a `Deconstruct` method, a getter or an indexer once for a whole `is` pattern or a whole `switch`. It gives the result to every pattern that names the same member on the same input.

So what a member hands out counts as a part of the matched value, like a field or a tuple element, even where the member's code makes a new value at every call. And like any part, it goes to one variable:

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

A pattern does not look into a part of a value in place. It copies the part into a temporary first, and every pattern that names the part uses that one temporary. The value around it still holds the original part. So once a member that is not `readonly` has run on the temporary, or an extension has taken it by value, the temporary and the original differ.

So one pattern, or the clauses of one `switch`, must not use a part and also take or use the value around it (SCA1003). That holds for a new value as well as for a stored one, and in whatever order the patterns are written, since the compiler chooses the order of evaluation itself:

```cs
switch (Open())
{
    case { Inner.Released: false }: break;     // 'Released' is not readonly: it runs on a copy of 'Inner'
    case var slot: slot.Dispose(); break;      // SCA1003: 'slot' is the whole value, which still holds the original 'Inner'
}

if (Open() is { Inner: { Released: true } part }) { }             // OK: only the temporary is used
if (Open() is { Inner.Released: true } and { Inner.Count: 0 }) { } // OK: both members run on the same temporary

if (Open() is { Inner: var inner } and (var moved, _)) { }         // SCA1003: 'inner' is the temporary, and
                                                                   //   'Deconstruct' can move the original out as well
if (Open() is { Inner.Released: true } and (var taken, _)) { }     // SCA1003: 'Released' changed the temporary,
                                                                   //   'Deconstruct' takes the original
```

- **A variable of a clause with a `when`** has handed its part on, so a member in a later clause that runs on that part gets a warning as well.
- **A `Deconstruct` of an extension that takes its receiver by value** is given a copy of the whole value, so it gets a warning alongside any other use of that value.

Take the value apart in one way only, or in a statement of its own.

#### Receivers of Extensions

An extension method that the compiler calls takes its receiver as an argument, with no argument written in the code: see [Members That the Compiler Calls on the Receiver of an Extension](compiler-copies.md#members-that-the-compiler-calls-on-the-receiver-of-an-extension).

The receiver of a member of an extension block (`extension(LockGuard guard)`) is an argument in the same way, and a parameter of every member of the block. Inside the member the receiver is a new value at its last use, like any other by-value parameter:

```cs
static class GuardExtensions
{
    extension(LockGuard guard)
    {
        public void Release() => guard.Dispose();
    }
}

var guard = myLock.Enter();
guard.Release();                            // OK: the last use of 'guard', so it moves into the receiver
_guard.Release();                           // SCA1003: the receiver is a copy of the field
```

## Inline Arrays and Stack Memory

An element of an inline array (a struct marked `[InlineArray]`) is used like an element of an array: in place, by reference, or through a span over the buffer. The buffer is a struct that stores non-copyable values, so it has to be marked `[NonCopyable]` itself.

```cs
[NonCopyable, InlineArray(4)]
struct GuardBuffer { private LockGuard _element; }

[InlineArray(4)]
struct PlainBuffer { private LockGuard _element; }   // SCA1010: the buffer must be marked

_buffer[0].Dispose();                       // OK: in place
LockGuard first = _buffer[0];               // SCA1001: a copy of the element
Span<LockGuard> all = _buffer;              // OK: a span over the buffer
```

Memory from `stackalloc` is used through its span in the same way, and its initializer can move new values into it.

## Created Values and With

- **A value that is created,** such as a tuple, a struct or an object, is as new as what is put into it by its arguments and its initializer.
- **A list pattern** uses the length or count, the indexer and `Slice` of its type, and **a collection expression** uses the constructor or the create method of its type and `Add` for each element. These count as member uses, just like the ones written out in the code.
- **`with` on a value of a non-copyable type copies that value:** on a stored value it gets a warning, and on a new value it is fine. A local or a by-value parameter not used again after the `with` is a new value there, as at any other [last use](last-use.md).

```cs
var changed = _options with { Size = 1 };        // SCA1003: copies the stored '_options'
var created = CreateOptions() with { Size = 1 }; // OK
var next = options with { Size = 2 };            // OK where 'options' is a local that is not used again
```

<xref:System.Object.MemberwiseClone> is under [Cloning](declarations.md#cloning).

</div>
