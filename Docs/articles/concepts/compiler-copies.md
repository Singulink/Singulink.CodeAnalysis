<div class="article">

# Where the Compiler Uses One Value Twice

Constructs of the language that pass one value to two places, and how each is reported.

## The Constructs

Some constructs look like one use of a value and are two. Each is reported whatever the value is, a new one included:

- **The clauses of a `switch`.** One value goes to one variable across the clauses as within a pattern. A clause with a `when` runs code
  with its variable and then lets a later clause bind the value again, so a variable of a later clause that overlaps it is a copy
  (`SCA1001`). A clause without a `when` is harmless.

  ```cs
  switch (myLock.Enter())
  {
      case var registered when TryRegister(registered): break;   // takes the guard, and releases it if it fails
      case var rejected: rejected.Dispose(); break;              // SCA1001: a second variable for the same guard
  }
  ```

- **An awaiter of a non-copyable type** (`SCA1015`). When an `await` suspends, the compiler copies the awaiter into its state machine,
  calls `OnCompleted` on the original and `GetResult` on the copy. Make the awaiter a class, or a copyable struct that refers to the
  state. The awaiters of the framework hold a reference to their task and are fine.
- **Combined delegates** (`SCA1003`). Every target of a delegate with several targets gets a copy of its own of a by-value argument. `+`
  and `+=` on a delegate, and `+=` on a field-like event, are reported where the delegate type has a by-value parameter of a non-copyable
  type.
- **User-defined `&&` and `||`** (`SCA1003`). `x && y` is `operator false(x) ? x : operator &(x, y)`: the left operand goes to
  `operator false` by value and then on.
- **An argument that an interpolated-string handler also receives** (`SCA1003`). With `[InterpolatedStringHandlerArgument("guard")]` the
  compiler passes the argument, or the receiver, to the method and again to the constructor of the handler. Take it by `in` or `ref` in
  both.
- **An indexer with a compound assignment or an increment** (`SCA1003`). `counts[guard] += 1` and `counts[guard]++` pass the index to the
  getter and to the setter.
- **A by-value receiver of an extension block that two members get** (`SCA1003`). `g.Uses += 1`, `g.Uses++` and `g.Uses ??= 1` on a
  property of `extension(LockGuard g)` pass `g` to the getter and to the setter, and so does an indexer of such a block (C# 15). An
  index or a range through the length, the indexer and `Slice` of such a block (`g[^1]`, `g[1..]`) passes `g` to each of them. Each of
  them also takes a stored value by value, like any other member with a by-value receiver. Take the receiver by `ref`.
- **A compound assignment operator that is an instance member** (`public void operator +=(T item)`) is a call of that member on its
  target, with the right side as the argument. `pile += myLock.Enter();` moves a new value in, and `pile += _held;` copies a stored one
  (`SCA1003`), as an argument of any call does.
- **A spread** (`SCA1030`). `[.. span]` reads every element by value, like a `foreach` with a by-value variable, also over a view that a
  call has just returned. And it calls the members that a `foreach` calls, with nothing written for them: `[.. bag]` is checked for
  `GetEnumerator`, `MoveNext`, `Current` and `Dispose` as a `foreach` over `bag` is, so a `[RequiresCopyable]` or an unlisted
  `GetEnumerator` is reported there too (`SCA1021`, `SCA1020`), and one that runs on a copy (`SCA1006`).
- **A query expression** (`SCA1006`). `from item in _queue select item` evaluates the source of its `from` clause into a temporary
  and calls `Select` on that, where `_queue.Select(item => item)` runs in place. A method of a non-copyable source that is not
  `readonly` runs on a copy unless the source is a new value.
- **A member that the compiler calls on the receiver of an extension** (`SCA1003`): `GetEnumerator` for a `foreach`, `GetAwaiter` for an
  `await`, `Deconstruct` for a deconstruction or a positional pattern. Where the extension takes its receiver by value, in a block or the
  classic form, a stored value is copied for the call.

</div>
