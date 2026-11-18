<div class="article">

# New Values and Stored Values

The distinction between a new value and a stored value, which every rule of the analyzer comes down to.

## The Two Kinds of Value

Every rule comes down to one distinction:

- A **new value** is one that nobody else holds: `new()`, `default`, the result of a call or of an operator, the result of <xref:Singulink.CodeAnalysis.NonCopyable.Move*?displayProperty=nameWithType>, and
  a local or a by-value parameter [where it is used for the last time](last-use.md). It can go anywhere once: into a variable, a
  field, a parameter, a tuple, an array, a collection, a hole of an interpolated string.
- A **stored value** is anything read from a local, a parameter, a field, an array element or a reference. Reading it by value makes a copy, and
  the copy is what is reported. It can be used in place (`guard.Dispose()`), passed by `ref`, `in` or `ref readonly`, and referred to with a
  `ref` local or a `ref` return.

A stored value behind a reference that is read-only can be used in place only by members that cannot change it: see
[Read-only references and temporary copies](read-only-references.md).

## The Result of an Assignment

The result of an assignment is a read of its target. `Take(_held = guard)` puts the guard into the field and passes a copy of what the
field then holds, so it is reported like any other by-value read of a stored value (`SCA1003`), wherever the result is taken by value: as
an argument, in a tuple, an array or a collection, as the receiver of an extension method that takes it by value, in a `using` statement.
The same goes for `+=` and `??=`. Nothing is reported where nothing uses the result, as in a statement, a discard or a member of an object
initializer, or where what uses it takes a reference, as an `in` argument does.

```cs
Take(_held = myLock.Enter());      // SCA1003: the field has the guard, and Take gets a copy

Look(_held = myLock.Enter());      // OK: Look takes its parameter by 'in'
```

## Locals and Parameters

A local or a by-value parameter is a stored value up to its last use, and a new value there: a variable that is never used again cannot be
used as a second copy. To hand it on before that, [move it](../guides/moving-a-value.md). A method can also return a by-value local or parameter, because
the variable ends with the method, unless a `finally` or the end of a `using` scope uses the variable after the `return` has read its value:
see [The last use of a variable](last-use.md). Neither holds for a variable that lives on: one that a lambda or a local function
captures, a parameter of a primary constructor that a member uses, a `using` variable, and anything that an iterator yields and then uses again. A captured
variable is a field in fact, in the function that captures it and in the method that declares it.

```cs
Func<LockGuard> f = () => guard;   // SCA1002: every call returns a copy of the captured variable

_onClose = () => guard.Dispose();  // the closure keeps 'guard'
return guard;                      // SCA1002: a second copy next to the one in the closure
```

## Also Stored

Also stored:

- **What is in a box.** Taking a value out of `object`, `dynamic` or an interface is a copy, whatever gives the reference: a cast, `as`, a
  pattern, also on the result of a call. See [Boxes](conversions.md#boxes).
- **A [container](non-copyable-types.md) that a property, an indexer or a field gives.** A property exposes state, so `Items[0]` and
  `await Pending` read from a stored container. Only a method that returns a container hands it over, and it is checked that it does: see
  [Returning a container](returning-containers.md).
- **A container that comes out of a cast.** A `Task<LockGuard>` that a cast, `as` or a pattern gets out of an `object` or a <xref:System.Threading.Tasks.Task> is stored,
  also when the reference comes from a call: whoever else holds the reference can read from the container too.

</div>
