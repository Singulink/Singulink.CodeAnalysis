<div class="article">

# Read-only References and Temporary Copies

The compiler does not let a member change a value behind a read-only reference. It runs a member that is not `readonly` on a copy instead, and what the member changes is lost: a `Dispose` through `in` releases a copy and leaves the caller's guard held. This page gives the rules for read-only references and for the other copies that the compiler makes by itself.

### At a Glance

| Copy | Where you get the warning | ID |
|---|---|---|
| A member that is not `readonly`, used through a read-only reference | At the receiver | SCA1006 |
| A temporary copy that the compiler makes for `?.`, a pattern or a concatenation | Where a member that is not `readonly` runs on it, or a part of it is kept | SCA1003 or SCA1001 |
| An argument for an `in` or `ref readonly` parameter that is not a variable | At the argument, if the value is stored | SCA1003 |
| A `?:`, a `switch` expression, a `??` or a conversion by value over a stored value | Where a member runs on the result | SCA1006 |
| A pattern that matches a copy | Where a member that is not `readonly` runs in it | SCA1003 |

## Read-only References

```cs
void Close(in LockGuard guard) => guard.Dispose();   // SCA1006: disposes a copy

// through the 'in' parameter:
bool isHeld = guard.IsHeld;                          // OK if the getter is readonly
```

A reference is read-only when it is:

- an `in` or `ref readonly` parameter, a `ref readonly` local or return, or a `ref readonly` field;
- a `readonly` field, except in a constructor or an `init` accessor of its own type, where it is a variable;
- `this` in a `readonly` member or a member of a `readonly struct`, and a parameter of a primary constructor that such a member uses;
- a field of any of these, at any depth.

#### Members That the Compiler Calls

The members that the compiler calls count too: `GetEnumerator` for a `foreach`, `GetAwaiter` for `await`, and the length, the indexer and `Slice` for an index or a range (`buffer[^1]`, `buffer[1..^1]`). The analyzer judges a `foreach` as it would judge a call of `GetEnumerator` written out at the same place, so the two agree for every kind of read-only reference.

For a value of a marked type parameter, every member gets a warning, since the compiler cannot know which of them are `readonly`. That includes the `GetEnumerator` that a `foreach` calls through an interface that the type parameter is constrained to.

Make the member `readonly`, or reach the value through a reference that is not read-only.

## Temporary Copies

In a few places, the compiler also copies a stored value before it looks into it:

- the value of a nullable, for `?.` and for a pattern;
- a value in a box, for a pattern;
- a value in a string concatenation.

Such a **temporary copy** gets a warning only when a member that is not `readonly` runs on it, or when a pattern keeps a part of it in a variable. A copy that is only read through fields and `readonly` members does no harm:

```cs
bool? isHeld = maybe?.IsHeld;                // OK if 'IsHeld' is readonly; SCA1003 if it is not
if (maybe is { Count: > 0 }) { }             // OK: reads a field
if (box is LockGuard { IsHeld: true }) { }   // OK if 'IsHeld' is readonly; SCA1001 if it is not
string text = "held by " + guard;            // OK if 'ToString' is readonly or not overridden
(flag ? first : second).Dispose();           // SCA1006: a conditional by value is a copy of one of them
(kind switch { 0 => _read, _ => _write }).Dispose();   // SCA1006: so is a 'switch' expression
(_maybe ?? throw new InvalidOperationException()).Dispose();   // SCA1006: and a '??'
((LockGuard)_maybe).Dispose();               // SCA1006: the cast unwraps a copy out of the nullable
if (_buffer is [var first, ..]) { }          // OK if the length and the indexer are readonly; SCA1003 if they are not
if (stack.Peek().IsHeld) { }                 // OK if 'IsHeld' is readonly: the copy that 'Peek' hands out is only read
int id = _leases[i].Id;                      // OK: reads a field of the copy
_leases[i].Dispose();                        // SCA1030: disposes the copy that the indexer hands out
```

#### What a Member on the Symbol List Hands Out

What a member on the [symbol list](symbol-list-format.md) hands out of a stored collection is a temporary copy like the others. Reading a field of it is fine, but keeping it, running a member that is not `readonly` on it, or taking a non-copyable value out of it gets a warning:

```cs
int id = _leases[i].Id;                              // OK: reads a field of the copy
_leases[i].Dispose();                                // SCA1030: disposes the copy
LockGuard g = _pending.GetAwaiter().GetResult();     // SCA1030: the stored task still holds the result
```

#### Conditionals, Switch Expressions and Conversions

Used by value over a stored value, a `switch` expression or a `??` gives a temporary copy, just as a `?:` does. A member called on the result runs on that copy, and so do a deconstruction, a `foreach`, an `await` and a getter that is not `readonly`. They get a warning like a member through a read-only reference (SCA1006).

**A conversion that gives a value gives a temporary** in the same way. `(LockGuard)_maybe` unwraps a copy out of the nullable, and a tuple conversion makes a new tuple, even one that only renames the elements, as `((LockGuard g, int n))_entry` does. Move the stored value out first:

```cs
if (NonCopyable.Move(ref _maybe) is { } held)        // OK: the field is emptied first
    held.Dispose();
```

## Arguments for In Parameters

**An argument for an `in` or `ref readonly` parameter that is not a variable is passed by value.** The compiler copies it into a temporary and passes a reference to that, so the method holds a second instance for the length of the call.

The analyzer treats such an argument like one passed by value: a new value is fine, and a stored one gets a warning (SCA1003). A variable is passed by reference, as before: a local, a parameter, a field, an element, or what a reference refers to.

These arguments are not variables:

- a conditional, as in `Look(flag ? _a : _b)`;
- a `??`, as in `Look(_maybe ?? _a)`, and a `switch` expression;
- a conversion, as in `Look((LockGuard)_maybe)`, and a stored `LockGuard` for an `in LockGuard?` parameter, which is converted first;
- the result of an assignment.

```cs
Look(_guard);                                // OK: a reference to the field
Look(flag ? _read : _write);                 // SCA1003: 'Look' receives a temporary copy of a stored value
Look(flag ? rwLock.Enter() : default);       // OK: a new value
```

## Where a Pattern Matches in Place

A pattern runs members that the code never calls by name: a getter, a `Deconstruct`, the length and the indexer of a list pattern. A member that is not `readonly` gets a warning where it runs on a copy of a stored value (SCA1003).

| Matched value | In place, or a copy? |
|---|---|
| A local or a by-value parameter, matched by an `is` pattern or a `switch` expression with no `when` | In place |
| A field, an element, what a reference refers to | A copy |
| A parameter of a primary constructor that a member uses | A copy: it is a field wherever it is read (see below) |
| A local matched by a `switch` statement | A copy |
| A local matched by a `switch` expression in which any arm has a `when` that is not a constant | A copy, for every arm |
| Every part that a pattern looks into: a field, a tuple element, a list element, what a `ref` property refers to | A copy, even where the whole value is a local or an object of a class |

A parameter of a primary constructor that a member uses is a field even in an initializer of the type and in the arguments of the base constructor. The constructor stores the field before either runs.

#### Switch Statements

In a build that is not optimised, the compiler copies the value of every `switch` statement. The analyzer does not depend on the configuration: a warning that came and went with it would be worse than a strict one. Use an `is` pattern or a `switch` expression with no `when`, or move the value into the `switch`. A `switch` expression with a `when` gets the same warning. The message says which of the two it is, a `switch` statement or a `switch` expression with a `when`, and names the rewrite.

#### Parts of a Value

The compiler reads a part into a temporary before it matches the pattern inside it. A part of a new value is new, and what a property returns by value is a temporary of its own. A `ref` field of a ref struct, and what a `Slice` method or a range indexer returns by reference, refer to stored values even where the ref struct or the sliced value is new.

```cs
if (lease is (var read, var write)) { }                    // OK: a local is matched in place
if (session is { Lease: (var read, var write) }) { }       // SCA1003: 'Deconstruct' runs on a copy of the field
if (leases is [(var first, var second)]) { }               // SCA1003: and on a copy of the element
switch (lease) { case (var read, var write): break; }      // SCA1003: a 'switch' statement matches a copy of a local
int n = lease switch { (var r, var w) when ok => 1, _ => 0 };   // SCA1003: so does a 'switch' expression with a 'when'
if (lease is (var again, var more)) { }                    // OK: in place
if (session is { Lease.IsHeld: true }) { }                 // OK: a readonly member does no harm on a copy
```

## Nested Initializers

A nested initializer works in place. `new Holder { Guard = { Count = 1 } }` sets members of the field of the new object where the field is, and copies nothing.

</div>
