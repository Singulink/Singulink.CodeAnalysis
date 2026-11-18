<div class="article">

# Non-copyable Types

Which types the rules treat as non-copyable, which are containers, and what follows for fields, interfaces and members.

## What Is Non-copyable

- A struct marked `[NonCopyable]`.
- A type parameter marked `[NonCopyable]`, inside the generic that declares it.
- A generic struct with a non-copyable type argument, at any depth, because copying it copies what it stores: `ValueTask<LockGuard>`,
  `LockGuard?`, `(LockGuard, bool)`, `KeyValuePair<int, LockGuard>`.
- A struct that a [list file](../guides/symbol-list.md) names as non-copyable. The built-in list names ten framework structs, which are on in a project
  that references a package itself, and which a project can [leave out](../guides/symbol-list.md#the-built-in-non-copyable-types): <xref:System.Threading.SpinLock>, <xref:System.Runtime.InteropServices.GCHandle>, <xref:System.Runtime.InteropServices.GCHandle`1>, <xref:System.Runtime.InteropServices.PinnedGCHandle`1>, <xref:System.Runtime.InteropServices.WeakGCHandle`1>,
  <xref:System.Threading.Lock.Scope>, <xref:System.Buffers.MemoryHandle>, <xref:System.Runtime.CompilerServices.DefaultInterpolatedStringHandler>, <xref:System.Threading.AsyncFlowControl> and <xref:System.Threading.Tasks.Sources.ManualResetValueTaskSourceCore`1>.

## Containers

A **container** is any other type that mentions a non-copyable type: `LockGuard[]`, `List<LockGuard>`, `Task<LockGuard>`, `Span<LockGuard>`. Copying
a container copies no element, so that is fine. What the rules control is how it is used. A type that has a container among its base types or
its interfaces is a container of the same non-copyable types: `class Guards : List<LockGuard>`, `interface IGuards : IReadOnlyList<LockGuard>`.

## Fields

A field of a non-copyable type can be declared in a class or in a non-copyable struct, and a non-copyable type cannot be the type of an
auto-property, whose getter returns a copy. A record, class or struct, cannot have such a field at all: it copies its fields in `with`, in its copy
constructor and in the `Equals`, `GetHashCode` and `ToString` that the compiler writes for it. A static field is fine anywhere, since no copy of
a struct or a record copies it.

Two things are fields without looking like one:

- **A parameter of a primary constructor that members of the type use.** The compiler keeps it in a field, so in a copyable struct it is reported
  like one (`SCA1010`).
- **A by-value parameter of an iterator.** The compiler keeps the arguments in the object that the iterator returns and copies them into every
  enumerator, so the parameter is reported where it is declared (`SCA1012`). An iterator that returns an enumerator and not a sequence
  (<xref:System.Collections.Generic.IEnumerator`1>, <xref:System.Collections.Generic.IAsyncEnumerator`1>) keeps its arguments once, and is not reported.

```cs
struct Session(LockGuard guard) { public void Close() => guard.Dispose(); }   // SCA1010: 'guard' is a field of a copyable struct

IEnumerable<int> Read(LockGuard guard) { /* ... */ yield return 1; }          // SCA1012: every enumeration gets a copy of 'guard'
```

## Interfaces and Async Members

A struct of a non-copyable type implements every member of its interfaces itself. A call of a member that it leaves to the default
implementation of the interface runs on a boxed copy, also through a constrained type parameter, where nothing shows the box. That includes a
default that a derived interface gives for a member of its base interface. Such a struct is reported where it is declared (`SCA1013`). A
generic struct that is non-copyable only through its type arguments, such as a tuple, has no declaration to report, so it is reported where
it is bound to a type parameter that is constrained to the interface.

An instance member of a non-copyable struct cannot be `async` or an iterator. The compiler copies `this` into the state machine of such a
member, so it runs on a copy and what it changes is lost: an `async` `DisposeAsync` leaves the caller's guard as it was. The member is reported
where it is declared (`SCA1014`). A member that is not `async` can change the struct and return the task of a static method. A `readonly struct`
has nothing that a member could change, so its members can be `async` or iterators. A `readonly` member of a struct that is not `readonly`
is reported all the same: it changes nothing, but it runs later, on a copy that is out of date by then. The body of an iterator runs when
the sequence is enumerated, and its copy still shows a guard as held that has been released since. The clean form is a static iterator
over what it needs.

```cs
// In a [NonCopyable] struct:
public async ValueTask DisposeAsync() { await ReleaseAsync(_lock); _lock = null; }                // SCA1014: clears a copy

public ValueTask DisposeAsync() { var held = _lock; _lock = null; return ReleaseAsync(held); }    // OK

public readonly IEnumerable<int> Ids { get { foreach (int id in _ids) yield return id; } }       // SCA1014: runs later, on a copy

public readonly IEnumerable<int> Ids => Enumerate(_ids);                                          // OK: a static iterator over the array
```

</div>
