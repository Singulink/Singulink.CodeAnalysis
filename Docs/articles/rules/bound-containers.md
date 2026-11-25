<div class="article">

# Containers Bound to a Type Parameter

A caller can bind a container, such as a task or a list, to a marked type parameter: `NonCopyable.Move(ref task)` does, and so does a `Pool<Task<LockGuard>>`. Nothing inside the generic can tell. This page gives the rules that keep the generic from handing the container out untyped, and that keep the callers from treating what it hands out as new.

### At a Glance

| Rule | Warning |
|---|---|
| Inside the generic, a value of `T` is never converted to `object`, `dynamic`, a base type or an interface | SCA1023 |
| A container cannot be bound to a type parameter whose constraint hides its non-copyable type | SCA1022, SCA1023 |
| A marked type parameter constrained to a container is a container | SCA1030 for a by-value read |
| A container that the generic hands out is stored, whatever went in | SCA1030 for a by-value read |
| A pure move is the one exception: what it hands out is new | |
| A delegate, or a conversion that is not written in the code, must not pass a stored container off as new | SCA1031 |

## No Conversion Inside the Generic

Inside the generic, a value of the type parameter cannot be converted to `object`, `dynamic`, a base type or an interface, not even a new value (SCA1023). With a container bound to it, what goes into the box is not a value but the container itself:

```cs
static object? Box<[NonCopyable] T>(T value) => value;   // SCA1023: with a list bound to T, this is the list itself
```

## No Constraint That Hides the Type

A container cannot be bound to a type parameter whose constraint hides its non-copyable type, such as `where T : IList`. The constraint would let the generic use it as that type:

```cs
static void UseAsList<[NonCopyable] T>() where T : IList { }

UseAsList<List<LockGuard>>();                          // SCA1022: the constraint lets the generic use the list as an IList
```

A constraint to a non-generic collection gets SCA1022, and a constraint to any other type that hides the non-copyable type gets SCA1023, as for any [conversion](conversions.md). A constraint that names a type parameter, such as `where T : IEnumerable<U>`, is judged with the type arguments filled in. Where such a constraint cannot be judged, binding a container to it gets a warning.

## Type Parameters Constrained to a Container

A marked type parameter that is constrained to a container is a container. With `where TList : IReadOnlyList<LockGuard>` or `where TTask : Task<TResult>`, a value of the type parameter is a reference that the caller may still hold, like a `List<LockGuard>` parameter. It is not a value that the generic alone holds.

So what a member, an `await` or a `foreach` reads out of it by value is read from a stored container (SCA1030), whatever the last use of the value is. Using its elements in place is still fine:

```cs
static void ReleaseFirst<[NonCopyable] TList>(TList guards) where TList : List<LockGuard>
{
    LockGuard first = guards[0];                        // SCA1030: the caller's list still holds the guard
    CollectionsMarshal.AsSpan(guards)[0].Dispose();     // OK: used in place
}
```

- **Members that the compiler calls** are judged like the members written out. A list pattern, a property pattern and a spread over such a value read from a stored container, as `guards[0]`, `task.Result` and a `foreach` do.
- **A new object of a class that derives from a container on the [symbol list](symbol-list-format.md)** is never new for the members that it inherits.

## What the Generic Hands Out Is Stored

The generic treats a `T` as a value that it alone holds. That is true of a guard: nobody can put the same guard in twice without a warning for the copy. It is not true of a task or a list, which the caller may still hold.

Nothing about what goes in can change that. Every type that mentions `T` is a way in, and so is every piece of code that the generic is given. A lambda or an object that implements an interface is a new value, and can still push any stored container in. So nothing gets a warning where a container goes in, and nothing about the inputs is trusted:

```cs
var tasks = new Bag<Task<LockGuard>>();              // Bag<[NonCopyable] T>
tasks.Add(_pending);                                 // OK: storing a reference is fine
LockGuard a = await tasks.Take();                    // SCA1030: what a bag of tasks hands out is stored

LockGuard b = await Echo(EnterAsync());              // SCA1030 as well: static T Echo<[NonCopyable] T>(T value) => value;
LockGuard c = await Build<Task<LockGuard>>(x => x.Add(_pending));   // SCA1030
```

A container that a member hands out by value, through a type that mentions the type parameter, is a stored container, whatever the object and the arguments of the call are. For a value of a non-copyable type nothing changes: a by-value result of checked code is a new value.

#### One Exception: a Pure Move

A [pure move](index.md#moved-from) hands out a new value where it moves a local that the calling method owns:

```cs
var pending = myLock.EnterAsync();
LockGuard a = await NonCopyable.Move(ref pending);   // OK: the method's own
```

The only thing that went in is a reference that nobody else holds, and the analyzer checks that the body of the move hands out nothing else. See [Generic Moves](moves-of-containers.md#generic-moves).

#### Framework Collections

A framework collection keeps its own rule: what a member hands out is [as new as the object and the arguments of the call](listed-members.md). Each of its members on the [built-in framework symbol list](framework-symbol-list.md) was reviewed one by one.

#### Objects That Decide What They Hold

Two kinds of object look like framework containers but are not, because their own code decides what goes in:

- an object of a generic with a container bound to a marked type parameter, such as a `Bag<Task<LockGuard>>`;
- an object of a class that is a container only through what it derives from, such as `class TaskQueue : Queue<Task<LockGuard>>`.

Such an object is stored where it is converted to a framework container (`IEnumerable<Task<LockGuard>>`, a base class). And an object that inherits the members of a framework container is never new for them:

```cs
var queue = new TaskQueue();
queue.Enqueue(myLock.EnterAsync());
LockGuard a = await queue.Dequeue();          // SCA1030: inherited members never hand out a new value
```

The inherited members would otherwise hand out, as new, whatever the class's own code put in.

## Waiters That a Library Links Itself

A library that keeps its waiters in a <xref:System.Collections.Generic.Queue`1> of its own is not affected: it uses what it takes out in place (`source.SetResult(guard)`) and never reads a guard out of it by value.

A list of waiters that the library links itself needs two marks, when each waiter is a class that derives from a task or a completion source:

- `[Kept]` on the parameter of what links a waiter in;
- `[return: Borrowed]` on what hands one back out, since the list still holds it.

```cs
private void Enqueue([Kept] Waiter waiter) { waiter.Previous = _tail; _tail = waiter; }

[return: Borrowed]
private Waiter? Peek() => _head;
```

## Delegates and Conversions With Nothing Written

#### Delegates

The result of calling a delegate counts as a new value, because the analyzer checks what the delegate is created from. So a delegate must not pass a stored container off as new. These get a warning where the delegate is created (SCA1031), unless the delegate type marks its return value `[return: Borrowed]`:

```cs
Func<Task<LockGuard>> f = tasks.Take;                  // SCA1031: the bag still holds what Take hands out

// In generic code, with static T Take<[NonCopyable] T>():
Func<T> f = Take<T>;                                   // SCA1031: the result of a call would be treated as new
```

#### Conversions With Nothing Written

An object of a class that derives from a framework container, or of a type whose own code decides what it holds, is stored where it is converted to the container. Three places convert it with no conversion written in the code, and each gets a warning (SCA1031):

| Place | Example |
|---|---|
| An override whose return type is the derived class, while the overridden member returns the container | A warning where the override is declared |
| A method group whose method returns the derived class, converted to a delegate that returns the container | `Func<List<LockGuard>> make = MakeGuards;` |
| A conversion between two constructions of a variant interface or delegate, from the derived class to the container | `Func<Guards>` to `Func<List<LockGuard>>` |

```cs
// With class Guards : List<LockGuard>
Func<List<LockGuard>> asList = make;                   // SCA1031: Guards is stored where it becomes a List<LockGuard>
```

</div>
