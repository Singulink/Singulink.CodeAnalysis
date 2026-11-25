<div class="article">

# Copies You Cannot See

The compiler makes some copies by itself, where the code shows no assignment and no argument. A `Dispose` that runs on such a copy releases the copy and leaves the original held. This page shows where these copies come from, and how to avoid them.

### At a Glance

| Where | Warning | Fix |
|---|---|---|
| A member through an `in` parameter or a `readonly` field | SCA1006 | Pass by `ref`, or make the member `readonly` |
| `?.` on a nullable guard | SCA1003 | Move the value out first |
| A pattern on a field, an element, or in a `switch` statement | SCA1003 | Match a local with `is`, or move the value in |
| A deconstruction of a field | SCA1001 | Move the value out first |
| A member called on `a ? b : c`, a `switch` expression or a `??` | SCA1006 | Call it on each branch |
| Boxing a stored guard, taking one out of a box | SCA1004, SCA1001 | Box a new value, use the box in place |
| `with` on a stored value | SCA1003 | Use `with` on a new value |

## Read-only References

The compiler does not let a member change a value behind a read-only reference. It runs a member that is not `readonly` on a copy instead:

```cs
void Close(in LockGuard guard)
{
    bool isHeld = guard.IsHeld;                   // OK: the getter is readonly
    guard.Dispose();                              // SCA1006: disposes a copy
}
```

A reference is read-only in these cases, among others:

- an `in` or `ref readonly` parameter;
- a `readonly` field, outside the constructors and `init` accessors of its own type;
- `this` in a `readonly` member.

Pass the value by `ref` instead, or make the member `readonly` if it changes nothing. In generic code every member of a marked type parameter counts as not `readonly`: see [Members Through a Read-only Reference](../rules/marked-type-parameters.md#members-through-a-read-only-reference).

## Nullables, Conditionals and Conversions

`?.` on a nullable guard looks into a copy of the value. So does a member called on a conditional, on a `switch` expression, on a `??`, or on a cast that unwraps a nullable:

```cs
maybe?.Dispose();                                // SCA1003: disposes a copy
(flag ? first : second).Dispose();           // SCA1006: a conditional by value is a copy of one of them
((LockGuard)_maybe).Dispose();               // SCA1006: the cast unwraps a copy out of the nullable
```

Move the stored value out first:

```cs
if (NonCopyable.Move(ref _maybe) is { } held) held.Dispose();   // OK: moved out first
```

The compiler makes the same copy when such a value is passed to an `in` parameter: see [Arguments for In Parameters](../rules/read-only-references.md#arguments-for-in-parameters).

## Patterns and Switch Statements

A pattern runs members that the code never calls by name: a getter, a `Deconstruct`, the length and the indexer of a list pattern. It matches a local or a by-value parameter in place. It matches a field or an element on a copy, and a `switch` statement matches even a local on a copy:

```cs
if (lease is (var read, var write)) { }                    // OK: a local is matched in place
if (session is { Lease: (var read, var write) }) { }       // SCA1003: 'Deconstruct' runs on a copy of the field
switch (lease) { case (var read, var write): break; }      // SCA1003: a 'switch' statement matches a copy of a local
```

Use an `is` pattern, or a `switch` expression with no `when`, or move the value into the `switch`. A member that is `readonly` does no harm on a copy. Why a `switch` statement copies: [Switch Statements](../rules/read-only-references.md#switch-statements).

Also, one pattern can give a value to only one variable: a second variable for the same guard is a copy (SCA1001). See [One Value Goes to One Variable](../rules/patterns.md#one-value-goes-to-one-variable).

## Deconstruction

A deconstruction runs `Deconstruct` on a copy of its right side. A `Deconstruct` that moves two guards out of a lease leaves them in the original as well:

```cs
var (read, write) = _lease;                         // SCA1001: 'Deconstruct' runs on a copy of the field
var (read, write) = NonCopyable.Move(ref _lease);   // OK: the field is emptied, and the value is moved in
var (read, write) = MakeLease();                    // OK: a new value
_lease.Deconstruct(out var read, out var write);    // OK: in place
```

## Boxes

A new guard moves into a box, and calls through an interface run on the box. A stored guard is copied into the box instead, and every way back out of a box is a copy:

```cs
IDisposable boxed = myLock.Enter();          // OK: the guard moves into the box
boxed.Dispose();                             // OK: runs on the box
object copy = _guard;                        // SCA1004: boxes a copy of a stored value
LockGuard back = (LockGuard)boxed;           // SCA1001: a copy out of the box
```

A tuple or a value task of a guard cannot be boxed at all (SCA1023). A `union` (C# 15) is a box as well. See [Boxes and Unions](../rules/boxes.md).

## Records and With

`with` on a value copies it: on a stored value you get a warning; on a new value or a local at its last use you don't.

```cs
var changed = _options with { Size = 1 };        // SCA1003: copies the stored '_options'
var created = CreateOptions() with { Size = 1 }; // OK
```

## Other Constructs

A few more constructs pass one value to two places: combined delegates, user-defined `&&` and `||`, an indexer with `+=`, and an interpolated-string handler that takes an argument. A spread reads every element by value. You get a warning where a non-copyable value goes through one of them. See [Copies the Compiler Makes](../rules/compiler-copies.md).

</div>
