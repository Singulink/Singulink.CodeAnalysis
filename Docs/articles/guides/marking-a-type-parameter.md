<div class="article">

# Marking a Type Parameter

A generic type of your own, such as a pool or a bag, can hold non-copyable values if it never copies them. It says so with `[NonCopyable]` on its type parameter. This page shows how to write such a generic.

### At a Glance

- **Mark the type parameter** of a generic that does not copy its values: `class Pool<[NonCopyable] T>`. Inside, `T` is checked like a guard.
- **Mark a member that has to copy** with `[RequiresCopyable]`. Its body is not checked, and using it with a guard gets a warning.
- **Move elements, never copy them,** even when a collection grows.
- **A generic that is not marked** is assumed to copy, and using it with a guard gets a warning unless the member is on the [symbol list](symbol-list.md).

## Example

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

The analyzer trusts the mark, even on a generic from another assembly, as long as that assembly was built with the analyzer. A generic from an assembly that was not built with the analyzer counts as not marked.

## Growing a Collection

A collection that grows moves its elements one by one. <xref:System.Array.Resize*?displayProperty=nameWithType> copies them and leaves the old array holding the originals, so it is not on the [built-in framework symbol list](../rules/framework-symbol-list.md) (SCA1020), and it should not be added to a [symbol list file](symbol-list.md):

```cs
private void Grow()
{
    var bigger = new T[_items.Length * 2];

    for (int i = 0; i < _count; i++)
        bigger[i] = NonCopyable.Move(ref _items[i]);   // OK: each element is moved

    _items = bigger;
}
```

## Things to Know Inside the Generic

#### Members Run on a Copy Through a Read-only Reference

The compiler cannot know which members of `T` are `readonly`, so through an `in` parameter or a `readonly` field every member of a marked `T` runs on a copy (SCA1006). Pass the value by `ref`:

```cs
static void Close<[NonCopyable] T>(in T value) where T : IDisposable => value.Dispose();   // SCA1006: runs on a copy
static void CloseByRef<[NonCopyable] T>(ref T value) where T : IDisposable => value.Dispose();  // OK
```

#### A T Is Never Converted

A caller can bind a task or a list to `T`, and nothing in the generic can tell. So you get a warning (SCA1023) when a value of `T` is converted to `object` or an interface, even a new value: with a list bound to `T`, the conversion would hand the list out untyped.

```cs
static object? Box<[NonCopyable] T>(T value) => value;   // SCA1023: with a list bound to T, this is the list itself
```

For the same reason, what the generic hands out is stored when a container is bound to `T`: `await tasks.Take()` on a bag of tasks is a copy (SCA1030). See [Containers Bound to a Type Parameter](../rules/bound-containers.md).

#### Local Functions and Lambdas

`[RequiresCopyable]` on a local function or a lambda does not exempt its body, since the analyzer checks that body as part of the member that contains it. Make it a private method instead.

## Overrides and Interfaces Keep the Marks

An override or an implementation must mark every type parameter that the member it replaces marks, and a type parameter that is passed to a marked one must be marked too (SCA1040):

```cs
interface IHolder<[NonCopyable] T> { void Hold(T value); }

class Holder<T> : IHolder<T>                      // SCA1040: 'T' is passed to a marked type parameter
{
    public void Hold(T value) { }
}
```

See [Keeping Marks on Overrides and Delegates](../rules/keeping-marks.md).

## Generics That Are Not Marked

The analyzer assumes that code which cannot see that a type is non-copyable copies it. Using a member of an unmarked generic with a non-copyable type gets a warning (SCA1020) unless the member is on the [symbol list](symbol-list.md):

```cs
var list = new List<LockGuard>();        // OK: on the symbol list
list.Add(myLock.Enter());                // OK: on the symbol list
int count = list.Count;                  // OK: on the symbol list
LockGuard[] all = list.ToArray();        // SCA1020: not on the symbol list, it copies
LockGuard b = Dup(in a);                 // SCA1020: static T Dup<T>(in T value) is not marked
```

If the generic is yours, mark its type parameter. If it is not, and the member makes no copy, add it to a symbol list file: see [Symbol List](symbol-list.md). Just naming the type gets no warning: a `List<LockGuard>` field is fine.

The exact rules: [Marked Type Parameters](../rules/marked-type-parameters.md).

</div>
