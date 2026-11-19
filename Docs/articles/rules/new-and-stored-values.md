<div class="article">

# New and Stored Values in Detail

Every rule of the analyzer comes down to one distinction: a value that nothing else holds, and a value that is read from a place that goes on holding it. This page gives the exact rule. [New and Stored Values](../start/new-and-stored-values.md) in Start Here is the short version.

### At a Glance

| Kind | What it is | What it may do |
|---|---|---|
| **New value** | `new()`, `default`, the result of a call or of an operator, the result of <xref:Singulink.CodeAnalysis.NonCopyable.Move*?displayProperty=nameWithType>, a local or a by-value parameter [at its last use](last-use.md) | Go anywhere once: into a variable, a field, a parameter, a tuple, an array, a collection, a hole of an interpolated string |
| **Stored value** | Anything read from a field, an array element, a reference, or a local or a parameter before its last use | Be used in place, passed or returned by reference, or referred to with a `ref` local. A by-value read gets a warning |

## New Values

A new value is one that nobody else holds, so it can go anywhere, once:

```cs
_guard = myLock.Enter();                    // OK: the result of a call
guards.Add(myLock.Enter());                 // OK: moved into the list
var pair = (myLock.Enter(), 1);             // OK: moved into a tuple
TakeGuard(NonCopyable.Move(ref _held));     // OK: the result of a move
```

## Stored Values

A stored value is anything that is read from a place that goes on holding it: a field, an array element, a reference, or a local or a parameter before its last use. Reading it by value makes a copy, and the copy is what gets the warning:

```cs
_guard.Dispose();                   // OK: used in place
TakeGuard(ref _guard);              // OK: passed by reference
ref LockGuard alias = ref _guard;   // OK: a reference

LockGuard copy = _guard;            // SCA1001: a copy of the field
TakeGuard(_guard);                  // SCA1003: passed by value
```

> [!NOTE]
> Behind a reference that is read-only, a stored value can be used in place only by members that cannot change it. See [Read-only References and Temporary Copies](read-only-references.md).

## Result of an Assignment

The result of an assignment is a read of its target. `Take(_held = myLock.Enter())` puts the guard into the field, and then passes `Take` a copy of what the field holds. So it gets a warning like any other by-value read of a stored value (SCA1003):

```cs
Take(_held = myLock.Enter());      // SCA1003: the field has the guard, and Take gets a copy

Look(_held = myLock.Enter());      // OK: Look takes its parameter by 'in'
```

The same goes for `+=` and `??=`. The result gets a warning wherever it is taken by value:

- as an argument;
- in a tuple, an array or a collection;
- as the receiver of an extension method that takes it by value;
- in a `using` statement.

You get no warning where nothing uses the result, as in a statement, a discard or a member of an object initializer. You also get none where the code that uses the result takes a reference to it, as an `in` argument does.

## Locals and Parameters

#### Up to the Last Use

A local or a by-value parameter is a stored value up to its last use, and a new value there: a variable that is not used again leaves no second copy behind. To hand it on before that, [move it](moved-from.md#move-helper). The exact rule is [Last Use of a Variable](last-use.md).

#### Returning a Variable

A method can return a by-value local or parameter, because the variable ends with the method. The exception is a `finally` or the end of a `using` scope that uses the variable after the `return` has read its value: see [Returned Variables](last-use.md#returned-variables).

#### Variables That Live On

Neither of these two rules holds for a variable that lives on after the method has used it. Such a variable is not new at its last use, and it cannot be returned:

- a variable that a lambda or a local function captures, since the compiler makes it a field (this holds both in the function and in the method that declares the variable);
- a parameter of a primary constructor that a member uses;
- a `using` variable;
- anything that an iterator yields and then uses again.

```cs
Func<LockGuard> f = () => guard;   // SCA1002: every call returns a copy of the captured variable

_onClose = () => guard.Dispose();  // the closure keeps 'guard'
return guard;                      // SCA1002: a second copy next to the one in the closure
```

## Other Stored Values

These are stored as well, though they can look new.

#### What Is in a Box

Taking a value out of `object`, `dynamic` or an interface is a copy, whether by a cast, `as` or a pattern, and even when the box is the result of a call. See [Boxes and Unions](boxes.md).

```cs
LockGuard back = (LockGuard)boxed;              // SCA1001: a copy out of the box
```

#### Container From a Property, an Indexer or a Field

A property exposes state, so `Items[0]` and `await Pending` read from a stored [container](non-copyable-types.md#containers). Only a method that returns a container hands it over, and the analyzer checks that the method is allowed to hand it over: see [Returning a Container](returning-containers.md).

```cs
LockGuard first = Items[0];                     // SCA1030: the list that the property gives still holds the guard
```

#### Container From a Cast

A `Task<LockGuard>` that a cast, `as` or a pattern gets out of an `object` or a <xref:System.Threading.Tasks.Task> is stored, even when the reference comes from a call. Whoever else holds the reference can read from the container too.

```cs
LockGuard held = await (Task<LockGuard>)task;   // SCA1030: a container from a cast is stored
```

</div>
