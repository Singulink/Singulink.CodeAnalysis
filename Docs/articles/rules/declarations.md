<div class="article">

# Fields, Interfaces and Async Members

Some declarations copy a value by themselves, although the code shows no copy: a field of a type that is copied, an auto-property, an iterator, a default interface member, an `async` member, a clone. This page lists them. Their warnings are SCA1005, SCA1008 and SCA1010 to SCA1014.

### At a Glance

| Declaration | Warning |
|---|---|
| A field of a non-copyable type in a copyable struct or in a record | SCA1010 |
| A parameter of a primary constructor that a copyable struct keeps | SCA1010 |
| An auto-property of a non-copyable type | SCA1011 |
| A by-value parameter of a non-copyable type on an iterator that returns a sequence | SCA1012 |
| A non-copyable struct that leaves an interface member to its default implementation | SCA1013 |
| An `async` or iterator instance member of a non-copyable struct that is not `readonly` | SCA1014 |
| <xref:System.Object.MemberwiseClone> of an object or a struct that holds a non-copyable value | SCA1005 |
| <xref:System.Object.MemberwiseClone> on a class that another assembly can derive from | SCA1008 |

## Fields

A field of a non-copyable type can be declared in a class or in a non-copyable struct. A static field is fine anywhere, since no copy of a struct or a record copies it. An instance field elsewhere gets a warning:

- **A copyable struct** copies the field with itself (SCA1010).
- **A record**, class or struct, cannot have such a field at all (SCA1010). It copies its fields in `with`, in its copy constructor and in the `Equals`, `GetHashCode` and `ToString` that the compiler writes for it.
- **An auto-property** cannot have a non-copyable type, since its getter returns a copy (SCA1011).

```cs
struct Plain { LockGuard _guard; }                     // SCA1010: a copyable struct copies the guard with itself

class Owner
{
    public LockGuard Guard { get; set; }               // SCA1011: the getter returns a copy
    private LockGuard _guard;                          // OK: a field of a class
    private static LockGuard s_shared;                 // OK: a static field
}

record Entry(int Id) { LockGuard _guard; }             // SCA1010: 'with' and the copy constructor copy it
```

#### Fields That Do Not Look Like One

Two things are fields without looking like one:

- **A parameter of a primary constructor that members of the type use.** The compiler keeps it in a field, so in a copyable struct it gets a warning like a field does (SCA1010).
- **A by-value parameter of an iterator.** The compiler keeps the arguments in the object that the iterator returns and copies them into every enumerator, so the parameter gets a warning where it is declared (SCA1012).

An iterator that returns an enumerator, not a sequence (<xref:System.Collections.Generic.IEnumerator`1>, <xref:System.Collections.Generic.IAsyncEnumerator`1>), keeps its arguments once and gets no warning.

```cs
struct Session(LockGuard guard) { public void Close() => guard.Dispose(); }   // SCA1010: 'guard' is a field of a copyable struct

IEnumerable<int> Read(LockGuard guard) { /* ... */ yield return 1; }          // SCA1012: every enumeration gets a copy of 'guard'
```

## Default Interface Members

A non-copyable struct must implement every member of its interfaces itself. A call of a member that it leaves to the interface's default implementation runs on a boxed copy, even through a constrained type parameter, where nothing shows the box. That includes a default that a derived interface gives for a member of its base interface. Such a struct gets a warning where it is declared (SCA1013).

```cs
interface IReleasable
{
    void Release() { }                         // a default implementation
}

[NonCopyable]
struct Lease : IReleasable { }                 // SCA1013: a call of Release runs on a boxed copy

[NonCopyable]
struct Lease : IReleasable { public void Release() { } }   // OK
```

A generic struct that is non-copyable only through its type arguments, such as a tuple, has no declaration to put the warning on. So it gets the warning where it is bound to a type parameter that is constrained to the interface.

## Async and Iterator Members

An instance member of a non-copyable struct cannot be `async` or an iterator. The compiler copies `this` into the state machine of such a member, so it runs on a copy and what it changes is lost: an `async` `DisposeAsync` leaves the caller's guard as it was. The member gets a warning where it is declared (SCA1014).

- **A member that is not `async`** can change the struct and return the task of a static method.
- **A `readonly struct`** has nothing that a member could change, so its members can be `async` or iterators.
- **A `readonly` member of a struct that is not `readonly`** gets a warning all the same. It changes nothing, but it runs later, on a copy that is out of date by then.
- **The body of an iterator** runs when the sequence is enumerated, and by then its copy can still show a guard as held that has since been released. The clean form is a static iterator over what it needs.

```cs
// In a [NonCopyable] struct:
public async ValueTask DisposeAsync() { await ReleaseAsync(_lock); _lock = null; }                // SCA1014: clears a copy

public ValueTask DisposeAsync() { var owner = _lock; _lock = null; return ReleaseAsync(owner); }  // OK

public readonly IEnumerable<int> Ids { get { foreach (int id in _ids) yield return id; } }       // SCA1014: runs later, on a copy

public readonly IEnumerable<int> Ids => Enumerate(_ids);                                          // OK: a static iterator over the array
```

## Cloning

<xref:System.Object.MemberwiseClone> gets a warning (SCA1005) in a class that has an instance field of a non-copyable type, its own or inherited, and in a non-copyable struct. A field that holds a container, such as a `List<LockGuard>`, does not count: the clone shares the list.

```cs
class Holder
{
    private LockGuard _guard;
    public Holder Clone() => (Holder)MemberwiseClone();   // SCA1005: the clone has a second guard
}
```

A class that derives from a class that clones itself, such as <xref:System.Text.Encoding>, gets a warning (SCA1009) on each field that holds a non-copyable value, since the call of `MemberwiseClone` is in code that the analyzer cannot read. So does a non-copyable type bound to a generic class that derives from one: see [SCA1009](../diagnostics/sca1009.md).

A class with no such field also gets a warning when a derived class in the same compilation has one. Where another assembly can derive from it, the warning has an ID of its own (SCA1008). The exact cases, such as `MemberwiseClone(bool)` of <xref:System.MarshalByRefObject> and a method group of either, are in the articles of [SCA1005](../diagnostics/sca1005.md) and [SCA1008](../diagnostics/sca1008.md).

</div>
