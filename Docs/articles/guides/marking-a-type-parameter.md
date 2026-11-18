<div class="article">

# Marking a Type Parameter

A generic that does not copy the values of a type parameter says so with `[NonCopyable]` on it. Inside the generic the type parameter is then a
non-copyable type and is checked like one, and code that uses the generic with a non-copyable type is trusted, also from another assembly.

## An Example

```cs
public sealed class Pool<[NonCopyable] T> where T : struct
{
    private readonly T[] _items = new T[16];

    public ref T this[int index] => ref _items[index];              // OK: hands out a reference

    public T Take(int index) => NonCopyable.Move(ref _items[index]); // OK: takes the value out

    public T Get(int index) => _items[index];                        // SCA1002: returns a copy

    [RequiresCopyable]
    public T[] ToArray() => (T[])_items.Clone();                     // not checked: it is marked as copying
}

var pool = new Pool<LockGuard>();
ref LockGuard guard = ref pool[0];     // OK
LockGuard taken = pool.Take(1);        // OK: a new value
LockGuard[] all = pool.ToArray();      // SCA1021: ToArray requires a copyable type
int[] numbers = new Pool<int>().ToArray(); // OK
```

A collection that grows moves its elements one by one. <xref:System.Array.Resize*?displayProperty=nameWithType> copies them and leaves the old array holding the originals, so it is
not on the list (`SCA1020`), and it should not be added to a list file:

```cs
private void Grow()
{
    var bigger = new T[_items.Length * 2];

    for (int i = 0; i < _count; i++)
        bigger[i] = NonCopyable.Move(ref _items[i]);   // OK: each element is moved

    _items = bigger;
}
```

## What a Marked Type Parameter Means

- **`[RequiresCopyable]`** goes on a method, a constructor, a property or an event that has to copy. Its body is not checked for the marked type
  parameters, and using it with a non-copyable type is reported.
  - **On a local function or a lambda the mark does not exempt the body.** A local function and a lambda are checked with the member
    that they are in, so the body is checked as if the mark were not there, and a call with a non-copyable type is reported as well.
    Make it a private method.
  - **A constructor that calls such a base constructor with nothing written uses it all the same.** A class with no constructor, and a
    constructor with no initializer, call a constructor of the base class with no argument. If that one is `[RequiresCopyable]`, the use is
    reported at the constructor, or at the name of the class that has none (`SCA1021`). Which constructor it is, is the compiler's
    choice where more than one can be called with no argument, as with `Cell(int capacity = 0)` next to `Cell(params long[] values)`.
    It is taken from the compiler for a constructor that has a body. For a class with no constructor and for a primary constructor it
    is worked out as the language does: the constructor with no parameters, then the one whose parameters are all optional, then a
    `params` one. Only where that does not decide, which does not compile either, does every candidate count.
  - **`with` on a record uses its copy constructor,** and that of every base record. A `[RequiresCopyable]` one is reported at the
    `with` (`SCA1021`), also where the value is of a type parameter that is constrained to the record: a type parameter that is
    constrained to a class is that class for every rule that looks at the type of a value.
    The copy runs through a virtual method, so a `with` on a variable of the base record runs the copy constructor
    of the derived one. A derived record whose copy constructor is marked where that of its base record is not adds the mark like
    an override does, and is reported where it is declared (`SCA1040`).
  - **On a static constructor or a finalizer the mark is reported (`SCA1040`),** and the body is checked as if it were not there: the
    runtime runs both, so there is no use at which a copy could be reported.
- **The marks must be kept.** A type parameter that is passed to a marked type parameter of a base class or an interface must be marked too. An
  override or an implementation must mark every type parameter that the member it overrides or implements marks, and must not be
  `[RequiresCopyable]` if that member is not. This holds as well for a member that a class inherits and uses to implement an interface: it is
  reported on the class that brings the interface in. And it holds where an implementation is replaced: in a derived class that names the
  interface again, with a member of its own or with one that hides an inherited member with `new`, and in a derived interface that gives a
  default implementation of a member of its base interface.
- **A container can be bound to a marked type parameter,** as in `NonCopyable.Move(ref task)` or a `Pool<Task<LockGuard>>`. Nothing in the
  generic can tell, so two things keep it from handing the container out untyped. Inside the generic, a value of the type parameter cannot be
  converted to `object`, `dynamic`, a base type or an interface, not even a new value (`SCA1023`): with a container bound, that is no value
  that moves into a box but the container itself. And a container cannot be bound to a type parameter whose constraint hides its non-copyable
  type, such as `where T : IList` (`SCA1022`, `SCA1023`): the constraint would let the generic use it as that type. A constraint that names a
  type parameter, such as `where T : IEnumerable<U>`, is judged with the type arguments filled in, and is reported when it cannot be judged.
- **A marked type parameter that is constrained to a container is a container.** With `where TList : IReadOnlyList<LockGuard>` or
  `where TTask : Task<TResult>`, a value of the type parameter is a reference that the caller may still hold, like a `List<LockGuard>`
  parameter, and not a value that the generic alone holds. So what a member, an `await` or a `foreach` reads out of it by value is read
  from a stored container (`SCA1030`), whatever the last use of the value is. Using its elements in place stays free. A member that
  the compiler calls is judged like the member written out: a list pattern, a property pattern and a spread over such a value read
  from a stored container as `guards[0]`, `task.Result` and a `foreach` do. The same goes for a new object of a class that derives
  from a listed container, which is never new for the members that it inherits.

  ```cs
  static void ReleaseFirst<[NonCopyable] TList>(TList guards) where TList : List<LockGuard>
  {
      LockGuard first = guards[0];                        // SCA1030: the caller's list still holds the guard
      CollectionsMarshal.AsSpan(guards)[0].Dispose();     // OK: used in place
  }
  ```
- **With a container bound, what the generic hands out is stored.** The generic treats a `T` as a value that it alone holds. That is true
  of a guard, since nobody can put the same guard in twice without a reported copy. It is not true of a task or a list, which the caller
  may still hold, and nothing about what goes in can change that: every type that mentions `T` is a way in, and so is every piece of code
  that the generic is given. A lambda or an object that implements an interface is a new value, and can still push any stored container
  in. So nothing is reported where a container goes in, and nothing about the inputs is trusted: a container that a member hands out by
  value, through a type that mentions the type parameter, is a stored container, whatever the member was called on and with.

  ```cs
  var tasks = new Bag<Task<LockGuard>>();              // Bag<[NonCopyable] T>
  tasks.Add(_pending);                                 // OK: storing a reference is fine
  LockGuard a = await tasks.Take();                    // SCA1030: what a bag of tasks hands out is stored

  LockGuard b = await Echo(EnterTaskAsync());          // SCA1030 as well: static T Echo<[NonCopyable] T>(T value) => value;
  LockGuard c = await Build<Task<LockGuard>>(x => x.Add(_pending));   // SCA1030
  ```

  - **The one exception is a pure move:** a generic move (see [Moving a value](moving-a-value.md)) with no receiver that carries a
    non-copyable type, whose parameters that carry one are all `[MovedFrom]`, whose return value is not `[Borrowed]`, and that is called
    with locals which the method owns. That is what makes `await NonCopyable.Move(ref pending)` a read from a new task: the only thing
    that went in is a reference that nobody else holds, and the body of the method is checked to hand out nothing else.
  - **A library that keeps its waiters in a <xref:System.Collections.Generic.Queue`1> of its own is not affected:** it uses what it takes out in place
    (`source.SetResult(guard)`) and never reads a guard out of it by value. A list of waiters that the library links itself, each waiter
    being a class that derives from a task or a completion source, needs two marks: `[Kept]` on the parameter of what links a waiter in,
    and `[return: Borrowed]` on what hands one back out, since the list still holds it.

    ```cs
    private void Enqueue([Kept] Waiter waiter) { waiter.Previous = _tail; _tail = waiter; }

    [return: Borrowed]
    private Waiter? Peek() => _head;
    ```
  - **A framework collection keeps its rule,** [as new as what the member was called on and with](../concepts/listed-members.md). Its
    members are a closed list that was read one by one.
  - **An object whose own code decides what it holds is no framework container.** That is an object of a generic with a container bound
    to a marked type parameter, or of a class that is a container only through what it derives from, such as
    `class TaskQueue : Queue<Task<LockGuard>>`. Converted to a framework container (`IEnumerable<Task<LockGuard>>`, a base class) it is
    stored, and one that inherits the members of a framework container is never a new value: those members would hand out as new what
    the object's own code put in.
  - **A delegate must not launder it either.** The result of calling a delegate is a new value, on the strength of the check of what the
    delegate is created from. So a method group of a member that hands out what its container holds (`Func<Task<LockGuard>> f =
    tasks.Take;`), and, in generic code, a function that returns a value of a marked type parameter (`Func<T> f = Take;`), are reported
    where the delegate is created (`SCA1031`) unless the delegate type marks its return value `[return: Borrowed]`.
  - **Nor must a conversion that is not written.** An object of a class that derives from a framework container, or of a type whose own
    code decides what it holds, is stored where it is converted to the container. Three places convert it with no conversion in the code,
    and each is reported (`SCA1031`): an override whose return type is the derived class while the overridden member returns the
    container, where the override is declared; a method group whose method returns the derived class, converted to a delegate that
    returns the container; and a conversion between two constructions of a variant interface or delegate whose type argument goes from
    the derived class to the container (`Func<Guards>` to `Func<List<LockGuard>>`).
  - For a value of a non-copyable type nothing changes: a by-value result of checked code is a new value.
- **The `new()` constraint** lets the generic create what is bound to the type parameter, so a type whose parameterless constructor is
  `[RequiresCopyable]`, or calls such a constructor of a base class with nothing written, cannot be bound to such a type parameter with a
  non-copyable type (`SCA1021`).
- **A member called through a read-only reference runs on a copy.** For a value of a marked type parameter that is true of every member, since
  the compiler cannot know which of them are `readonly`: a call or a property read through `in`, `ref readonly` or a `readonly` field is
  reported (`SCA1006`). Pass the value by `ref`. A type parameter that is constrained to `class` is a reference and is not affected.

## Generics That Are Not Marked

Code that cannot see that a type is non-copyable is assumed to copy it. Using a member of a generic type, or calling a generic method, with a
non-copyable type bound to a type parameter that is not marked is reported (`SCA1020`) unless the member is on the [safe list](symbol-list.md).
It makes no difference where the generic is: your project, another project or a package.

```cs
var list = new List<LockGuard>();        // OK: listed
list.Add(myLock.Enter());                // OK: listed
int count = list.Count;                  // OK: listed
LockGuard[] all = list.ToArray();        // SCA1020: not listed, it copies
LockGuard b = Dup(in a);                 // SCA1020: static T Dup<T>(in T value) is not marked
```

Naming a type reports nothing: a `List<LockGuard>` field or a `ValueTask<LockGuard>` return type is fine. The members that the compiler calls
count, such as `GetEnumerator` for `foreach`, `GetAwaiter`, `IsCompleted`, `OnCompleted` (or `UnsafeOnCompleted`) and `GetResult` for
`await`, and `Dispose` for `using`. Delegate types are exempt: the method or lambda
that a delegate is made from is checked where the delegate is created.

If the generic is yours, mark its type parameter. If it is not, and the member makes no copy, list it.

</div>
