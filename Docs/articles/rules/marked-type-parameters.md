<div class="article">

# Marked Type Parameters

A generic that does not copy the values of a type parameter says so with `[NonCopyable]` on it. This page gives the exact rules for such a type parameter, for `[RequiresCopyable]`, and for generics that are not marked. [Marking a Type Parameter](../guides/marking-a-type-parameter.md) shows how to write such a generic.

### At a Glance

- **Inside the generic,** a marked type parameter is a non-copyable type, and the body is checked like any code that uses one.
- **Outside,** the analyzer trusts the mark, even on a generic from another assembly, as long as that assembly was built with the analyzer. On a generic from an assembly that was not built with the analyzer, the mark counts as absent: see [Code Not Built With the Analyzer](unchecked-code.md).
- **`[RequiresCopyable]`** marks a member that has to copy. Its body is not checked, and using it with a non-copyable type gets a warning (SCA1021).
- **A container bound to a marked type parameter** has rules of its own: see [Containers Bound to a Type Parameter](bound-containers.md).
- **A generic that is not marked** is assumed to copy, and using one of its members with a non-copyable type gets a warning unless the member is on the [symbol list](symbol-list-format.md) (SCA1020).
- **The marks must be kept** by overrides, implementations and derived types: see [Keeping Marks on Overrides and Delegates](keeping-marks.md).

## Members That Require a Copyable Type

`[RequiresCopyable]` goes on a method, a constructor, a property or an event that has to copy. Its body is not checked for the marked type parameters, and using it with a non-copyable type gets a warning (SCA1021):

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

#### Local Functions and Lambdas

On a local function or a lambda the mark does not exempt the body. The analyzer checks a local function or a lambda as part of the member that contains it. So the body is checked as if the mark were not there, and a call with a non-copyable type gets a warning as well. Make the function a private method instead.

```cs
// In Pool<[NonCopyable] T>, with a field T[] _items:
public void Run()
{
    [RequiresCopyable]
    T First() => _items[0];               // SCA1002: the body is checked all the same

    var first = First();                  // SCA1021: and so is the call
}

[RequiresCopyable]
private T FirstOfPool() => _items[0];     // OK: a private method is exempt
```

#### Constructors Called With Nothing Written

A class with no constructor, and a constructor with no initializer, call a constructor of the base class with no argument. If that base constructor is `[RequiresCopyable]`, you get a warning at the constructor, or at the name of the class if it has no constructor (SCA1021):

```cs
class Cell<[NonCopyable] T>
{
    [RequiresCopyable]
    public Cell() { }
}

class GuardCell : Cell<LockGuard> { }       // SCA1021: the implicit constructor calls Cell<LockGuard>()
```

Where more than one constructor can be called with no argument, as with `Cell(int capacity = 0)` next to `Cell(params long[] values)`, the compiler chooses which one runs:

- For a constructor that has a body, the analyzer takes the compiler's choice.
- For a class with no constructor and for a primary constructor, the analyzer works the choice out as the language does. That is the constructor with no parameters, then the one whose parameters are all optional, then a `params` one.
- Only where that does not decide, in code that does not compile anyway, does every candidate count.

#### Records and With

`with` on a record uses its copy constructor and that of every base record. If any of them is `[RequiresCopyable]`, the `with` gets a warning (SCA1021).

That holds even where the value is of a type parameter that is constrained to the record. A type parameter that is constrained to a class is that class for every rule that looks at the type of a value, not only for `with`.

The copy runs through a virtual method, so a `with` on a variable of the base record runs the copy constructor of the derived one. A derived record that adds the mark gets a warning where it is declared: see [Keeping Marks](keeping-marks.md#records-static-constructors-and-finalizers).

#### The New Constraint

The `new()` constraint lets the generic create what is bound to the type parameter. So a type whose parameterless constructor is `[RequiresCopyable]`, or calls such a constructor of a base class with nothing written, cannot be bound to such a type parameter when the type is used with a non-copyable type argument (SCA1021). An example is `Make<Copies<Guard>>()`, where `Make` has the `new()` constraint and the constructor of `Copies<T>` is `[RequiresCopyable]`.

## Containers Bound to a Marked Type Parameter

A caller can bind a container, such as a task or a list, to a marked type parameter, and nothing inside the generic can tell. Such a container has rules of its own: see [Containers Bound to a Type Parameter](bound-containers.md).

## Members Through a Read-only Reference

A member called through a read-only reference runs on a copy. For a value of a marked type parameter that is true of every member, since the compiler cannot know which of them are `readonly`. So a call or a property read through `in`, `ref readonly` or a `readonly` field gets a warning (SCA1006). Pass the value by `ref`. A type parameter that is constrained to `class` is a reference and is not affected.

```cs
static void Close<[NonCopyable] T>(in T value) where T : IDisposable => value.Dispose();   // SCA1006: runs on a copy
static void CloseByRef<[NonCopyable] T>(ref T value) where T : IDisposable => value.Dispose();  // OK
```

## Generics That Are Not Marked

The analyzer assumes that code which cannot see that a type is non-copyable copies it. Using a member of a generic type, or calling a generic method, with a non-copyable type bound to a type parameter that is not marked gets a warning (SCA1020). The exception is a member on the [symbol list](symbol-list-format.md). It makes no difference where the generic is: your project, another project or a package.

```cs
var list = new List<LockGuard>();        // OK: on the symbol list
list.Add(myLock.Enter());                // OK: on the symbol list
int count = list.Count;                  // OK: on the symbol list
LockGuard[] all = list.ToArray();        // SCA1020: not on the symbol list, it copies
LockGuard b = Dup(in a);                 // SCA1020: static T Dup<T>(in T value) is not marked
```

- **Naming a type gets no warning:** a `List<LockGuard>` field or a `ValueTask<LockGuard>` return type is fine.
- **The members that the compiler calls count,** such as `GetEnumerator` for `foreach`, `GetAwaiter`, `IsCompleted`, `OnCompleted` (or `UnsafeOnCompleted`) and `GetResult` for `await`, and `Dispose` for `using`.
- **Delegate types are exempt:** the method or lambda that a delegate is made from is checked where the delegate is created.

If the generic is yours, mark its type parameter. If it is not, and the member makes no copy, add it to a symbol list file.

</div>
