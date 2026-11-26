<div class="article">

# What the Keeping Check Follows

The check of [a container parameter that is not `[Kept]`](keeping-containers.md) follows the parameter wherever the method's code takes it. This page gives every route. Each route gets SCA1032 where the container ends up kept.

### At a Glance

| Route | Example that keeps |
|---|---|
| A variable that holds it | `var other = guards; _all = other;` |
| The container under another name: a view, another type, a reference | `_pending = guards.AsMemory(0, 1);` |
| `with` | `_pending = window with { };` |
| A collection that is made | `_keep = new[] { guards };` |
| A member that the compiler calls | `guards?.Register();` with a `[Kept]` receiver |
| An indexer that is assigned | `map[guards] = 1;` |
| A write through a reference | `slot = guards;` |
| A tuple | `_pair = (guards, 1);` |
| A delegate | `_onClose = guards.Clear;` |
| An operator | a user-defined operator whose parameter is `[Kept]` |
| A type parameter | `bag.Add(guards);` |
| Code that nothing checks | `ArrayPool<LockGuard>.Shared.Return(buffer);` |
| A parameter that holds containers | `_all = lists[0];` |
| The receiver of an extension block | `extension(List<LockGuard> list) { ... _all = list; }` |
| A call that cannot be seen | a `dynamic` call, a function pointer, `__arglist` |

## Variables

The check follows the parameter into every variable that holds it: a local or a by-value parameter that it is assigned to, declared with or matched into by a pattern. The order of the statements does not matter. A local that is stored at the top of a loop and assigned the parameter further down holds the parameter all the same.

```cs
void Variable(List<LockGuard> guards)
{
    var other = guards;
    _all = other;                                  // SCA1032
}
```

## Container Under Another Name

Some results of an operation on the container are the container in another form, and the check follows each such form like the container itself. If such a form is stored in a field, in an object initializer or in a field of a static struct, the container is what is kept. A span or a reference that stays in the method keeps nothing.

| Form | Examples |
|---|---|
| A view that is not a ref struct, such as a <xref:System.Memory`1> or an <xref:System.ArraySegment`1> | `guards.AsMemory(0, 1)` |
| The container as another type that a member on the [symbol list](symbol-list-format.md) hands out, even through an `out` parameter | `segment.Array`, `list.AsReadOnly()`, `task.WaitAsync(timeout)`, `MemoryMarshal.TryGetArray(memory, out var segment)` |
| The variable itself, where a callee is given it by reference and returns a reference | `_all = Pick(ref list);` |

```cs
void View(LockGuard[] guards)
{
    _pending = guards.AsMemory(0, 1);              // SCA1032: the memory is the array itself
}
void ViewSpan(LockGuard[] guards)
{
    Span<LockGuard> span = guards;
    span[0].Dispose();                             // OK: a span that stays in the method
}
```

What a member with a `new` symbol list entry hands out, such as <xref:System.Buffers.ArrayPool`1.Rent*?displayProperty=nameWithType>, has been taken out of the container for good, and is not the container under another name.

#### What the Project's Own Code Hands Back

The analyzer checks what the project's own methods return, so their results are normally their own. Two kinds of result are not checked, so the keeping check assumes them to be the container that went in:

| Result | Taken to be |
|---|---|
| A <xref:System.Memory`1> or a <xref:System.ReadOnlyMemory`1> that a method, a local function or a delegate returns when it is given the container | The container under another name. A task of such a memory from an `async` method is judged like the memory, and so is what an `await` of it gives |
| Whatever a getter or an indexer returns that holds a non-copyable type | The container that it was given, as an argument or as the receiver of an extension block |

The getter and the indexer in this block hand back the container that they were given:

```cs
extension(LockGuard[] buffer)
{
    public ArraySegment<LockGuard> Segment => buffer;           // the segment is 'buffer'
}

public IReadOnlyList<LockGuard> this[List<LockGuard> key] => key;   // returns 'key' itself
```

A property of the container's own type is not such a getter: `waiter.Next` hands out what the object holds.

```cs
static Memory<LockGuard> Window(LockGuard[] buffer, int count) => buffer.AsMemory(0, count);

void WindowCall(LockGuard[] buffer)
{
    _pending = Window(buffer, 1);                  // SCA1032: Window's memory is 'buffer'
}
```

## With

`with` gives a copy of what it is applied to, and the copy holds whatever the original holds. `_pending = window with { };` on a `Memory<LockGuard>` over the parameter, and `_entry = entry with { Count = 1 };` on a tuple that holds it, are stores of the container.

```cs
void With(Memory<LockGuard> window)
{
    _pending = window with { };                    // SCA1032
}
```

## Collections That Are Made

An array or a collection expression keeps its elements. Each argument for a `params` parameter whose type is not an array or a span is kept as well, since the builder or the `Add` of that type receives it. And a spread over a parameter that holds containers keeps each element that it hands on, as a `foreach` that adds them would.

```cs
void Array(List<LockGuard> guards)
{
    var all = new[] { guards };                    // SCA1032: the array keeps 'guards'
    _keep = all;
}
void Spread(List<LockGuard>[] lists)
{
    _keep = [.. lists];                            // SCA1032: the spread keeps what 'lists' holds
}
```

## Members That the Compiler Calls

Some kinds of operation run code with the container as its receiver or argument, and only the compiler's binding shows that. The analyzer judges each such member like the same member written out in the code, so an extension with a `[Kept]` receiver keeps the container:

```cs
// With extension([Kept] List<LockGuard> guards) { public void Register() { ... } }
void Ext(List<LockGuard>? guards)
{
    guards?.Register();                            // SCA1032: the extension's receiver is [Kept]
}
```

The members that the compiler calls are these:

- the call behind `?.`;
- `Deconstruct` of a deconstruction and of a positional pattern;
- `GetAwaiter` of an `await`;
- `GetEnumerator` and `GetAsyncEnumerator` of a `foreach` and of a spread;
- a property of a property pattern, and the length, the indexer and `Slice` of a list pattern;
- `operator true` and `operator false` of a user-defined `&&` and `||`;
- the constructor of an interpolated-string handler that a call passes its argument on to through `[InterpolatedStringHandlerArgument]`;
- the operator of `guards += 1` and `guards++` that is an instance member of an extension block;
- the conversion that a `??` applies to its left side, and that a deconstruction or a `foreach` applies to an element, as in `(Handle handle, int count) = pair;`;
- the `Slice` and the `Length` or `Count` behind a range or an index from the end (`guards[1..]`);
- the method of a method group (`Action register = guards.Register;`).

An operation that is allowed is allowed with every member that it carries. The analyzer keeps a closed list of the kinds of operation that a container may be used in, with every member that each kind carries. A test checks that list against the compiler's API, so that a member which a later compiler adds is caught there.

A `fixed` statement has no operation of its own, and the list names it apart. Pinning a container with it is a known use where the compiler runs no other code with the container: an array, the address of an element, or a type whose own `GetPinnableReference` the compiler calls, such as a span. Pinning through a `GetPinnableReference` of an extension, or pinning a container whose elements are containers, gets a warning for a use that the check does not follow. The pointer itself is not followed: see [Pointers](not-covered.md#pointers).

## Indexers

The parameter of an indexer keeps its argument where the indexer is assigned, like the parameter of a setter: `map[guards] = 1;`, `map[guards] += 1;` and `new Map { [guards] = 1 }`. Where the indexer is read, its getter is a member like any other, and what it hands back is followed.

```cs
void Indexer(List<LockGuard> guards)
{
    _map[guards] = 1;                              // SCA1032: the setter keeps the index
}
```

## References

A write through a reference keeps the container whatever the reference refers to. The reference can be:

- a `ref` local, or a `ref` or `out` parameter of the method;
- what a call returns by reference;
- a field or an element that is reached through one of those;
- what a pointer points to.

Making a `ref` local refer to the parameter keeps nothing.

```cs
void Ref(List<LockGuard> guards, ref List<LockGuard>? slot)
{
    slot = guards;                                 // SCA1032
}
```

## Tuples

A tuple carries the container to wherever the tuple goes. `_pair = (list, 1)` and `(_all, _count) = (list, 1)` keep it. A tuple that is taken apart, by a deconstruction, a pattern or a `switch`, gives it to the variables that take its elements, and those are followed in turn. Where nothing says which variable takes which element, as with a tuple in a variable, each variable that could take it counts.

```cs
void Tuple(List<LockGuard> guards)
{
    _pair = (guards, 1);                           // SCA1032
}
```

## Delegates

`_onClose = list.Clear;` keeps the list as `_onClose = () => list.Clear();` does. A delegate in a local of the method is followed like a tuple, and one that is returned or passed on keeps the list.

```cs
void Delegate(List<LockGuard> guards)
{
    _onClose = guards.Clear;                       // SCA1032
}
```

## Operators and Handlers

A user-defined conversion or operator is a call of its method, and keeps what it is given if the parameter of the method is marked `[Kept]`. That includes `++` and `--`.

`AppendFormatted` of an interpolated-string handler keeps what it is given in the same way, if its parameter is marked `[Kept]` or is a type parameter marked `[NonCopyable]`: such a handler says that it moves what a hole gives it. The framework's handlers only format the value.

## Type Parameters

A parameter whose type is a type parameter is assumed to keep what it is given, marked or not. That holds in every form:

- for an argument (`bag.Add(list)`);
- for an element of the collection that the compiler makes for `params T[]` or `params ReadOnlySpan<T>` (`bag.AddRange(list)`);
- for the operand of an operator or a conversion of a generic (`bag += list`, `_ = bag + list`, `Handle<List<LockGuard>> handle = list;`);
- for the receiver of an extension block over a type parameter (`extension<[NonCopyable] T>(T item)`).

A [pure move](moves-of-containers.md#generic-moves) is the exception.

```cs
// GuardBag<[NonCopyable] T> has void Add(T item)
void TypeParam(List<LockGuard> guards, GuardBag<List<LockGuard>> bag)
{
    bag.Add(guards);                               // SCA1032
}
```

## Code That Nothing Checks

A parameter whose declared type mentions a type parameter that is not marked is assumed to keep what it is given, as an argument, an operand or the receiver of an extension block. `ArrayPool<LockGuard>.Shared.Return(buffer)` keeps the array and `Array.Reverse(guards)` only uses it, and no declaration tells the two apart. So such a parameter keeps unless [the symbol list](symbol-list-format.md) marks the member `borrows`, which says that the member uses what it is given during the call and keeps nothing of it.

```cs
void Unchecked(LockGuard[] buffer)
{
    ArrayPool<LockGuard>.Shared.Return(buffer);    // SCA1032
}
void Borrows(LockGuard[] buffer)
{
    System.Array.Reverse(buffer);                  // OK: the framework symbol list marks it 'borrows'
}
```

- **A constructor of a ref struct that is on the [built-in framework symbol list](framework-symbol-list.md),** such as `new Span<LockGuard>(buffer)`, is a call that returns a ref struct, and keeps nothing more than `buffer.AsSpan()` does.
- **The framework symbol list has the marker** on every member of this shape that the runtime's source shows to keep nothing, including <xref:System.Array.Reverse*?displayProperty=nameWithType> and the comparisons of <xref:System.Memory`1> and <xref:System.ArraySegment`1>. A member in your own symbol list file gets the marker in the same way.
- **A call that hands the container back in another form needs no marker,** whether as a reference, a ref struct, another type or through an `out` parameter. That is what the call is for, and what it hands back is followed (`CollectionsMarshal.AsSpan(list)`, `guards.AsMemory()`).
- **The warning has a text of its own,** which names the entry to add with `borrows` if the member keeps nothing, and `[Kept]` on the parameter if it does.
- **A use of a member that is not on the symbol list at all** gets that warning (SCA1020), and not a second one for keeping.
- **For a generic helper of the project's own,** mark its type parameter `[NonCopyable]` instead of adding the helper to a symbol list file: its body is then checked, and its parameter says for itself whether it keeps.

## Parameters That Hold Containers

An element that is read out of a parameter that holds containers, by an index or a `foreach`, is a container that the caller put in. So is what a member of a struct on the [symbol list](symbol-list-format.md) hands out of it, as `entry.Value` of a `KeyValuePair<string, List<LockGuard>>` does.

```cs
void Params(params List<LockGuard>[] lists)
{
    _all = lists[0];                               // SCA1032
}
```

## Receivers of Extension Blocks

An extension block keeps its receiver where its members store it, and gets a warning unless the receiver is marked `[Kept]`:

```cs
extension(List<LockGuard> list)
{
    public void Register() => s_all = list;        // SCA1032: keeps its receiver
}
```

## Calls That Cannot Be Seen

A dynamic call, a call through a function pointer and `__arglist` keep what they are given.

```cs
void Dyn(List<LockGuard> guards)
{
    dynamic d = this;
    d.Take(guards);                                // SCA1032
}
```

</div>
