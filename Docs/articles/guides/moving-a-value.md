<div class="article">

# Moving a Value

`NonCopyable.Move(ref value)` takes the value out of a variable, a field or an array element and leaves the default value behind. Its result is a
new value. A second use of the variable hits a default value, not a second guard. It is for everything that
[the last use](../concepts/last-use.md) does not cover: a field, an array element, a `ref` parameter, a captured variable, a variable
that a reference was taken to, and a move that is not the last use.

## The Move Helper

```cs
_guard = NonCopyable.Move(ref guard);      // store in a field
Consume(NonCopyable.Move(ref guard));      // pass by value
return (NonCopyable.Move(ref guard), true); // put in a tuple
```

A `using` variable and a `readonly` field cannot be moved from, since neither can be passed by `ref`: something else still owns the value. A
moved-from variable of a reference type, such as a `Task<LockGuard>`, is possibly null afterwards as far as the compiler's null checks go, so
declare it with `var` or as nullable. For the same reason a move out of a slot of an unconstrained `T`, as in
`NonCopyable.Move(ref _items[index])` in a collection with no `where T : struct`, gives the compiler's CS8601: declare the slots as `T?[]`,
or suppress CS8601 at the move.

## Moving a Container

A value has one holder by construction, so it can be moved out of anything that can be passed by `ref`. A container is a reference, and
copying a reference is allowed, so clearing one variable says nothing about the others. A container can therefore be moved only out of a
local that the method owns: one that holds what the method created or got from a call, and that it has not stored anywhere or passed to
something that keeps it (see [Returning a container](../concepts/returning-containers.md) for the test). Moving a container out of a field, a parameter,
an element or a local that was assigned one of those is reported (`SCA1033`), with <xref:Singulink.CodeAnalysis.NonCopyable.Move*?displayProperty=nameWithType> and with any other method that takes a
`[MovedFrom]` parameter. What such a move hands out is then as stored as what went in. A `[MovedFrom]` parameter of the method itself is
its own, where its caller could see the container (see below). The deconstruction of a tuple literal assigns each element to its variable, so both locals of
`var (first, second) = (a.EnterTaskAsync(), b.EnterTaskAsync());` are the method's own.

```cs
var pending = myLock.EnterTaskAsync();
LockGuard a = await NonCopyable.Move(ref pending);   // OK: the method's own

LockGuard b = await NonCopyable.Move(ref _cached);   // SCA1033: whoever else has the task can await it too
```

## The MovedFrom Mark

`[MovedFrom]` on a `ref` parameter is what makes such a helper possible: inside the method, reading the parameter by value is a move. In
return the method leaves a fresh value behind, which is checked along the paths through it (`SCA1041`):

- **The method cannot leave with the parameter read and not yet assigned,** by a `return` or by an exception.
- **It cannot read the parameter a second time before it is assigned.** A backward `goto` is a loop. A loop that assigns the parameter
  after every read is fine.
- **From the read to the assignment nothing runs but reads and writes of the method's own locals,** with one exception: the single write
  that stores the value that was read. Anything else can throw, which leaves the value in two places, or reach the same variable in
  another way: a call, a read or a write through another reference, a field or an element. "Between" is judged in the order of
  evaluation, operation by operation. What can throw is done first, into a local.
- **A reference can be null.** <xref:System.Runtime.CompilerServices.Unsafe.NullRef*?displayProperty=nameWithType> makes one in code that the language calls safe, and a read or a write through it
  fails with a <xref:System.NullReferenceException>, which the caller can catch with the value in two places. So a read or a write through another
  `ref`, `in` or `out` parameter or a `ref` local counts as able to throw, and so does a field of `this` in a struct, where `this` is
  such a reference. `b = a; c = default; a = default;` is reported at the write through `c`; `c = default; b = a; a = default;` is
  fine. A swap, which reads the other parameter only to assign it to the first, is fine as well. `this` in a struct is such a
  reference however it is used: `this = default;`, `copy = this;`, `this is { State: 1 }`, and a parameter of a primary constructor that
  the struct keeps. The exception is a reference that is
  [known to refer to something](null-references.md).
- **An assignment stores when it runs, wherever its target stands.** In `node.Owner = _current = value;` the target `node.Owner` is
  evaluated before the read and written after the inner store, and that write is what fails for a null `node`. So the target of every
  assignment between the read and the reset is judged where the assignment stores: it runs nothing only if it is a local or a by-value
  parameter of the method, a discard, a part of one of those, an instance field of `this` in a class, or the one store that takes the
  value that was read. The same holds for the targets of a deconstruction.
- **The swap is fine only while the value that was read is in a local.** A read through the other reference can fail, and then the local
  is gone with the exception. Once the local is stored on, into a field, through a reference, by a deconstruction or a conditional, or
  where a lambda uses it, the value is in a lasting place, and the read is reported:
  `Guard old = slot; _previous = old; slot = replacement;`.
- **The receiver of an extension block is a parameter.** `extension([MovedFrom] ref LockGuard guard)` is checked in every member of the
  block, and the mark on a receiver that is not `ref` is reported like the mark on any other by-value parameter.
- **A reference chosen by a condition is a target like each of its branches.** `(useSpare ? ref spare : ref task) = s_cached;` leaves the
  cached task in a `[MovedFrom]` parameter, and is reported like `task = s_cached;`. The same goes for a `ref` local over such a
  reference and for passing it on: `Fill(out (useSpare ? ref spare : ref task));` passes `task` on by reference.
- **In an instance member of a class, a read or a plain store of an instance field of `this` cannot throw,** so
  `_held = value; _state = State.Held; value = default;` is fine there. That is not so for a static field, whose first use can run the
  initializer of its type, nor for a field of another object, which can be null. The parameter can refer to a field of `this`, so a
  by-value read of a non-copyable value out of one can take the same value a second time: that read is reported as the copy of a
  stored value that it is anywhere (`SCA1001`).
- **What looks like a read or a write of a local must run no other code.** These call code that the method does not see, and count as
  calls: the concatenation of a string with something that is not a string, which calls `ToString`; a conversion of a tuple that
  converts its elements; a conversion from `dynamic`; a positional pattern on anything but a tuple; and a store through a property, an
  indexer or an event. Arithmetic and comparisons of numbers, tests for null and comparisons of references run nothing. The
  concatenation of two strings can throw where the result would be too long for a string, as after a commit.

```cs
static void Replace([MovedFrom] ref LockGuard slot, ref LockGuard old)
{
    old = slot;
    slot = Create();                // SCA1041: if Create throws, 'old' and 'slot' hold the same guard
}

static void ReplaceSafely([MovedFrom] ref LockGuard slot, ref LockGuard old)
{
    LockGuard fresh = Create();     // what can throw comes first
    old = slot;
    slot = fresh;                   // OK: the last use of 'fresh'
}
```

That allows `b = a; a = default;`, the body of <xref:Singulink.CodeAnalysis.NonCopyable.Move*?displayProperty=nameWithType>, and a swap (`LockGuard held = a; a = b; b = held;`), in which the other
parameter is read only to be assigned to the one that was read. Two parameters can refer to one variable, whether or not both are marked:
`var both = (a, b);` reads `b` after `a` and is reported, since both elements could be the same guard.

- `??=` does not count as the assignment: it leaves a value that is not null in place.
- Passing the parameter on by reference assigns it only when it goes to an `out` or a `[MovedFrom]` parameter. Before any read that is the
  move itself, as in `=> NonCopyable.Move(ref value);`. After a read it is a call like any other, and is reported.
- What runs nothing between the read and the assignment is a closed list, as for [the commit](../concepts/last-use.md). A
  deconstruction is on it only if at most one of its targets is something other than a local, a by-value parameter or a discard: its
  stores are made one after the other, and a second one that fails leaves the first one done. `is ()` on anything but a tuple is a call.
- `Interlocked.Exchange(ref value, null)` reads and assigns in one operation. Only <xref:System.Threading.Interlocked.Exchange*?displayProperty=nameWithType> itself counts,
  and its generic form only for a reference type: for a struct that is no primitive it throws. Before any read it is both. After a read it is the
  assignment only where its result is discarded, as in `var taken = value; Interlocked.Exchange(ref value, null);`. Where the result
  is used it hands out the value a second time, and is reported as a second read.
- <xref:Singulink.CodeAnalysis.NonCopyable.Move*?displayProperty=nameWithType> of a local is allowed between the read and the assignment.
- A read in an exception filter has to be followed by the assignment inside the filter: a filter that gives false lets the exception
  go on, and the method goes on or leaves with the parameter as it was.
- A `ref` local that is declared as another name for the parameter (`ref T alias = ref value;`) assigns it as the parameter does.
- A method that only uses the parameter in place has taken nothing out, and is not reported.
- The parameter is never made to refer to something else (`value = ref other;`): the assignment that follows would reset the other
  variable.

## Keeping the Mark, Slots and Pure Moves

**`[MovedFrom]` is kept like the other marks, in both directions** (`SCA1040`). The mark is two promises: to the method, that its caller
owned what it passed, and to the caller, that the method leaves only what the caller may own. An override, an implementation, or a lambda
or a method group that is converted to a delegate, has the mark on exactly the parameters on which the member that it stands for has it.
One that adds the mark would be passed anything by the callers of the base member, and one that drops it would not be checked for what it
leaves behind.

**A `[MovedFrom]` parameter of a container type is the method's own only where its caller could see the container:** where the method
itself, the member that it overrides or implements, and the delegate type that it is converted to all give the parameter the container
type. Where one of them gives it the type of a type parameter, the caller is generic code, which hands over a slot of its own without
knowing that the slot holds a task that was added twice. The parameter is then a stored container: the method uses it in place, and
moving it on is reported (`SCA1033`).

```cs
delegate void MoveAction<[NonCopyable] T>([MovedFrom] ref T value);

// In Drainable<[NonCopyable] T>:
public void Drain(MoveAction<T> action) { for (int i = 0; i < _count; i++) action(ref _items[i]); }

tasks.Drain(([MovedFrom] ref Task<LockGuard> task) => { Log(task.IsCompleted); task = null!; });   // OK: used in place
tasks.Drain(([MovedFrom] ref Task<LockGuard> task) =>
{
    var mine = NonCopyable.Move(ref task);                                      // SCA1033: the slot is not the lambda's own
});
```

For a method that is used as a method group this is known only when the whole compilation has been seen, so that finding comes with a
build and not while typing.

**A move hands back by value.** A method that takes a `[MovedFrom]` argument and returns a reference or a ref struct can return a
reference to the caller's variable, through which the caller writes anything: `Reset(ref task) = _stored;`. Such a method is no pure move,
and a local that was passed to it is no longer the caller's own.

A caller's local that holds a container stays the caller's own after it was passed to a `[MovedFrom]` parameter, so the method has to leave
there only what the caller can own:

- **A parameter of a container type,** such as `[MovedFrom] ref Task<LockGuard> task`, is assigned `null`, a new value or a local that is
  the method's own, which is then the caller's and not the method's to return. A field, such as a cached completed task, is reported at the
  assignment (`SCA1041`), and so is passing the parameter by reference to something that can assign anything to it.
- **A generic move is checked to be a pure move.** That is a method whose `[MovedFrom]` parameters have a marked type parameter as their
  type and which has no other parameter and no receiver that mentions one. Its body treats `T` as a value, so with a task bound a static
  slot of type `T` may hold a task that somebody put there without owning it. What such a method returns, through a type that mentions the
  type parameter, and what it leaves in a `[MovedFrom]` parameter, is `default` or a value that it read from its `[MovedFrom]`
  parameters: the read itself, a local that is assigned nothing else, or the result of another pure move of one of those. Anything else is
  reported (`SCA1041`). A method that returns something else on purpose, such as a swap with a slot that it keeps, marks its return value
  `[return: Borrowed]`: its callers then treat a container that it hands out as stored, and a local that they passed to it as no longer
  their own. There is no mark for leaving something else behind.

```cs
static T Swap([MovedFrom] ref T value)               // in Stash<[NonCopyable] T>, with a static slot
{
    T old = NonCopyable.Move(ref s_slot);
    s_slot = NonCopyable.Move(ref value);
    return old;                                      // SCA1041: not what was read from 'value'; or mark the return [Borrowed]
}
```

## Writing Your Own Move

A project that does not reference the attributes package, or that has to move a ref struct on a target before .NET 10, writes its own move:

```cs
static LockGuard Move([MovedFrom] ref LockGuard value)
{
    LockGuard moved = value;
    value = default;
    return moved;
}
```

</div>
