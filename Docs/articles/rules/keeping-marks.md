<div class="article">

# Keeping Marks on Overrides and Delegates

A mark is a promise to the callers of a member, and they may call it through an override, an implementation or a delegate. So each mark must hold for everything that stands for the member. This page gathers those rules. Breaking any of them gets SCA1040.

### At a Glance

| Mark | What stands for the member must |
|---|---|
| `[NonCopyable]` on a type parameter | Mark every type parameter that the member marks, and pass a marked type parameter only to one that is marked |
| `[RequiresCopyable]` | Not have it where the member does not |
| `[return: Borrowed]` | Not have it where the member does not |
| `[Kept]` | Not have it where the member's parameter is not assumed to keep |
| `[MovedFrom]` | Have it on exactly the parameters on which the member has it |

"What stands for the member" is an override, an implementation, a member that a class inherits and uses to implement an interface, and a lambda or a method group that is converted to a delegate.

## Marks on Type Parameters

- **A type parameter that is passed to a marked type parameter** of a base class or an interface must be marked too.
- **An override or an implementation** must mark every type parameter that the member it overrides or implements marks, and must not be `[RequiresCopyable]` if that member is not.
- **A member that a class inherits and uses to implement an interface** follows the same rules, and the warning is on the class that brings the interface in.
- **A replaced implementation** follows the same rules. That is an implementation in a derived class that names the interface again, with a member of its own or with one that hides an inherited member with `new`.
- **A default implementation that a derived interface gives** for a member of its base interface follows the same rules as well.

```cs
interface IHolder<[NonCopyable] T> { void Hold(T value); }

class Holder<T> : IHolder<T>                      // SCA1040: 'T' is passed to a marked type parameter
{
    public void Hold(T value) { }
}
```

## Records, Static Constructors and Finalizers

- **A derived record** whose copy constructor is marked `[RequiresCopyable]`, while that of its base record is not, adds the mark as an override would. It gets a warning where it is declared.
- **The copy of a `with` runs through a virtual method,** so a `with` on a variable of the base record runs the copy constructor of the derived one.
- **On a static constructor or a finalizer** the mark `[RequiresCopyable]` gets a warning, and the body is checked as if it were not there. The runtime calls both, so there is no call site where a copy could get a warning.

## Borrowed Mark

An override or an implementation must not have `[return: Borrowed]` where the member that it replaces does not, since a caller that goes through that member treats the result as a new value. The same goes for a method or a lambda with the mark that is converted to a delegate whose return value is not marked. The other way round is allowed.

## Kept Mark

A method or a lambda with `[Kept]` may stand only for a parameter that keeps too. An override or an implementation must not have `[Kept]` where the member that it replaces does not, and a method with the mark may be converted only to a delegate whose parameter keeps:

```cs
public delegate void GuardsSink(List<LockGuard> guards);
public delegate void KeptSink([Kept] List<LockGuard> guards);

void Register([Kept] List<LockGuard> list) => _all = list;

Action<List<LockGuard>> keep = Register;       // OK: an unmarked type parameter, as in Action<T>, counts as keeping
KeptSink sink = Register;                      // OK: the delegate's parameter is [Kept]
GuardsSink plain = Register;                   // SCA1040: callers of GuardsSink assume the list is still theirs
```

Here the parameter of the delegate, or of the member that is overridden or implemented, keeps if one of these holds:

- it is marked `[Kept]`;
- it is the value of a setter;
- its type is a type parameter that is **not** marked `[NonCopyable]`, as in `Action<T>`.

A delegate whose parameter has a marked type parameter as its type, such as `delegate void MarkedSink<[NonCopyable] T>(T item)`, does not count as keeping for `[Kept]`: `MarkedSink<List<LockGuard>> m = Register;` gets a warning.

A `[MovedFrom]` parameter hands its container over for good, so a method may keep it with no mark.

#### Method Groups

**A method group keeps what its method keeps.** This is the other side: the method's own parameter, not the delegate's.

A method's parameter whose type is a type parameter is assumed to keep what it is given, marked or not, so `registry.Add` of a `Registry<List<LockGuard>>` keeps the list that it is called with. When such a method group is converted to a delegate whose parameter is a container that is not assumed to keep, the conversion gets a warning, just as an added `[Kept]` would: the callers of the delegate assume that what they pass is still theirs.

```cs
// registry is a Registry<List<LockGuard>>, declared as Registry<[NonCopyable] T>
public delegate void GuardsSink(List<LockGuard> guards);
public delegate void KeptSink([Kept] List<LockGuard> guards);

GuardsSink onCreated = registry.Add;        // SCA1040: 'Registry<T>.Add(T)' keeps what it is given
KeptSink keeping = registry.Add;            // OK: the delegate says that it keeps
Action<List<LockGuard>> action = registry.Add;   // OK: the parameter of 'Action<T>' is a type parameter as well
```

For a method group without `[Kept]`, a delegate's parameter whose type is a type parameter keeps, marked or not: `MarkedSink<List<LockGuard>> m = registry.Add;` is fine.

The analyzer asks of a method group what it asks of every call:

| Method group | Delegate | Warning? |
|---|---|---|
| `ArrayPool<LockGuard>.Shared.Return` | `delegate void Recycler(LockGuard[] buffer, bool clear);` | Yes: `Return` keeps the array, and the delegate's parameter does not |
| <xref:System.Array.Reverse*?displayProperty=nameWithType>, which the [built-in framework symbol list](framework-symbol-list.md) marks `borrows` | a delegate whose parameter does not keep | No |
| `ArrayPool<LockGuard>.Shared.Return` | `Action<LockGuard[], bool>` | No: the parameters of `Action<T1, T2>` are type parameters, which are assumed to keep as well |

The same goes for a delegate that is made from another delegate. The analyzer first lines up the parameters. A method group of an extension method that is called on a receiver, as `holder.Keep` is, has one parameter more than the delegate, and its first one is the receiver.

#### Inherited Members

**An inherited member that implements an interface member is compared in the same way.** `class GuardsRegistry : Registry<List<LockGuard>>, IGuardsSink` implements `IGuardsSink.Add(List<LockGuard>)` with `Registry<T>.Add(T)`, whose parameter is a type parameter and is assumed to keep. A caller that goes through the interface assumes that its list is still its own, so the class gets a warning. Implement the member in the class, or mark the parameter of the interface member `[Kept]`.

An inherited member whose parameter mentions a type parameter that is not marked, as `Pool<T>.Return(T[])` does, gets a warning in the same way, unless its [symbol list entry](symbol-list-format.md) is marked `borrows`.

## MovedFrom Mark

**`[MovedFrom]` is kept in both directions.** The mark makes two promises: to the method, that its caller owned what it passed, and to the caller, that the method leaves only what the caller may own. An override, an implementation, or a lambda or a method group that is converted to a delegate, must have the mark on exactly the parameters on which the member that it stands for has it.

- If one **adds** the mark, the callers of the base member could pass it anything.
- If one **drops** the mark, nothing checks what it leaves behind.

For a method group and for a delegate made from another delegate, `[MovedFrom]` is compared for the same parameters, lined up as for `[Kept]`.

```cs
delegate void Taker(ref LockGuard guard);

static void Take([MovedFrom] ref LockGuard guard) { guard = default; }

Taker taker = Take;                         // SCA1040: the delegate's parameter is not [MovedFrom]
```

</div>
