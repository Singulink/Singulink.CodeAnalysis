<div class="article">

# Read-only References and Temporary Copies

The compiler does not let a member change a value behind a read-only reference. It runs a member that is not `readonly` on a copy instead, and
what the member changes is lost: a `Dispose` through `in` releases a copy and leaves the caller's guard held. That is reported (`SCA1006`) at
the receiver.

## Read-only References

```cs
void Close(in LockGuard guard) => guard.Dispose();   // SCA1006: disposes a copy
bool held = guard.IsHeld;                            // OK if the getter is readonly
```

A reference is read-only when it is

- an `in` or `ref readonly` parameter, a `ref readonly` local or return, or a `ref readonly` field;
- a `readonly` field, except in a constructor or an `init` accessor of its own type, where it is a variable;
- `this` in a `readonly` member or a member of a `readonly struct`, and a parameter of a primary constructor that such a member uses;
- a field of any of these, at any depth.

The members that the compiler calls count: `GetEnumerator` for a `foreach`, `GetAwaiter` for `await`, and the length, the indexer and
`Slice` for an index or a range (`buffer[^1]`, `buffer[1..^1]`). A `foreach` is judged where a call of `GetEnumerator` that is written
would be, so the two agree for every kind of read-only reference. For a value of a marked type parameter every member is reported,
since the compiler cannot know which of them are `readonly`, also the `GetEnumerator` that a `foreach` calls through an interface that
the type parameter is constrained to. Make the member `readonly`, or reach the value through a reference that is not read-only.

## Temporary Copies

The compiler also copies a stored value before it looks into it in a few places: the value of a nullable for `?.` and for a pattern, a value
in a box for a pattern, and a value in a string concatenation. Such a **temporary copy** is reported only when a member that is not `readonly`
runs on it, or when a pattern keeps a part of it in a variable. A copy that is only read through fields and `readonly` members does no harm:

```cs
bool? held = maybe?.IsHeld;                  // OK if 'IsHeld' is readonly; SCA1003 if it is not
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

What a listed member hands out of a stored collection is a temporary copy like the others: it is reported where it is kept, where a
member that is not `readonly` runs on it, and where a value of a non-copyable type comes out of it, by a field or by a `readonly`
member: `_pending.GetAwaiter().GetResult()` hands out the result that the stored task still holds.

**An argument for an `in` or `ref readonly` parameter that is not a variable is passed by value.** The compiler copies it into a
temporary and passes a reference to that, so the method holds a second instance for the length of the call: `Look(flag ? _a : _b)`,
`Look(_maybe ?? _a)`, a `switch` expression, `Look((LockGuard)_maybe)`, the result of an assignment, and a stored `LockGuard` for an
`in LockGuard?` parameter, which is converted first. Such an argument is judged like one that is passed by value: a new value is fine,
and a stored one is reported (`SCA1003`). A variable (a local, a parameter, a field, an element, what a reference refers to) is passed
by reference, as before.

```cs
Look(_guard);                                // OK: a reference to the field
Look(flag ? _read : _write);                 // SCA1003: 'Look' receives a temporary copy of a stored value
Look(flag ? rwLock.Enter() : default);       // OK: a new value
```

A `switch` expression and a `??` by value over a stored value give a temporary copy as a `?:` does: a member that is called on the
result, a deconstruction, a `foreach`, an `await` and a getter that is not `readonly` run on that copy, and are reported like a member
through a read-only reference (`SCA1006`). **A conversion that gives a value gives a temporary** in the same way: `(LockGuard)_maybe`
unwraps a copy out of the nullable, and a tuple conversion makes a new tuple, also one that only renames the elements, as
`((LockGuard g, int n))_entry` does. Move the stored value out first, as in
`if (NonCopyable.Move(ref _maybe) is { } held) held.Dispose();`.

## Where a Pattern Matches in Place

**Where a pattern matches in place, and where on a copy.** A pattern runs members with nothing written for them: a getter, a
`Deconstruct`, the length and the indexer of a list pattern. A member that is not `readonly` is reported where it runs on a copy
of a stored value (`SCA1003`):

- **The value that the whole pattern matches is used in place only if it is a local or a by-value parameter,** and only by an `is`
  pattern and by a `switch` expression with no `when`. Anything else is read into a temporary first: a field, an element, what a
  reference refers to. A parameter of a primary constructor that a member uses is a field: `lease is (var read, var write)` in a
  member of `class Session(Lease lease)` matches a copy. It is that field wherever it is read, so the same pattern matches a copy in
  an initializer of the type and in the arguments of the base constructor: the constructor stores the field before either runs.
- **A `switch` statement matches a copy of a local as well.** In a build that is not optimised the compiler copies the value of
  every `switch` statement, and the analyzer does not follow the configuration: a finding that came and went with it would be
  worse than one that is strict. So does a `switch` expression in which any arm has a `when` that is no constant, for every arm of
  it. Use an `is` pattern or a `switch` expression with no `when`, or move the value into the `switch`. The message says which of
  the two it is, and names that rewrite.
- **Every part that a pattern looks into is a copy,** also where the whole value is a local or an object of a class: the compiler
  reads a field, a tuple element, a list element and what a `ref` property refers to into a temporary before it matches the
  pattern inside. A part of a new value is new, and what a property returns by value is a temporary of its own. A `ref` field of a
  ref struct, and what a `Slice` method or a range indexer returns by reference, refer to stored values also where the ref struct or
  the sliced value is new.

```cs
if (lease is (var read, var write)) { }                    // OK: a local is matched in place
if (session is { Lease: (var read, var write) }) { }       // SCA1003: 'Deconstruct' runs on a copy of the field
if (leases is [(var first, var second)]) { }               // SCA1003: and on a copy of the element
switch (lease) { case (var read, var write): break; }      // SCA1003: a 'switch' statement matches a copy of a local
int n = lease switch { (var r, var w) when ok => 1, _ => 0 };   // SCA1003: so does a 'switch' expression with a 'when'
if (lease is (var again, var more)) { }                    // OK: in place, and 'lease' is used after the 'switch'
if (session is { Lease.IsHeld: true }) { }                 // OK: a readonly member does no harm on a copy
```

**A nested initializer works in place.** `new Holder { Guard = { Count = 1 } }` sets members of the field of the new object where
the field is, and copies nothing.

</div>
