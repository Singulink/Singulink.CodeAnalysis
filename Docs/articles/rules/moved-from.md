<div class="article">

# MovedFrom Mark

`[MovedFrom]` on a `ref` parameter makes reading that parameter by value a move instead of a copy. In return, the method must reset the parameter before anything else can see the value in two places. This page gives the exact rules that the analyzer checks. A method that breaks one gets SCA1041 unless the section says otherwise.

### At a Glance

The method reads the parameter, which is the move, and then assigns it a fresh value, which is the reset. Between the two, the value is in two places: the parameter and wherever it went. So between the read and the reset the method may only do things that cannot fail and cannot reach the parameter in another way.

| Between the read and the reset | Allowed? |
|---|---|
| A read or a write of a local or a by-value parameter of the method | Yes |
| The one store that puts the value that was read where it is going | Yes |
| A read or a plain store of an instance field of `this`, in a class | Yes |
| <xref:Singulink.CodeAnalysis.NonCopyable.Move*?displayProperty=nameWithType> of a local | Yes |
| A read or a write through another reference that is [known to refer to something](null-references.md) | Yes, except a by-value read of a non-copyable value or a container: see [Reading Twice](#reading-twice) |
| A call, including a property, an indexer or an event | No |
| A read or a write through another `ref`, `in` or `out` parameter, or a `ref` local | No, unless it is known to refer to something |
| A field of `this` in a struct | No, unless `this` is known to refer to something |
| A static field, a field of another object, an array element | No |

The typical fix is to do whatever can fail **before** the read, into a local:

```cs
static void Replace([MovedFrom] ref LockGuard slot, ref LockGuard old)
{
    old = slot;
    slot = Create();                // SCA1041: if Create throws, 'old' and 'slot' hold the same guard
}

static void ReplaceSafely([MovedFrom] ref LockGuard slot, ref LockGuard old)
{
    LockGuard fresh = Create();     // what can fail comes first
    old = slot;
    slot = fresh;                   // OK: the last use of 'fresh'
}
```

## Move Helper

<xref:Singulink.CodeAnalysis.NonCopyable.Move*?displayProperty=nameWithType> is the `[MovedFrom]` method that the attributes package has. `NonCopyable.Move(ref value)` takes the value out of a variable, a field or an array element and leaves the default value behind. Its result is a new value, so a second use of the variable hits a default value, not a second guard.

It covers everything that [the last use](last-use.md) does not: a field, an array element, a `ref` parameter, a captured variable, a variable that a reference was taken to, and a move that is not the last use.

```cs
_guard = NonCopyable.Move(ref guard);      // store in a field
Consume(NonCopyable.Move(ref guard));      // pass by value
return (NonCopyable.Move(ref guard), true); // put in a tuple
```

- **A `using` variable and a `readonly` field cannot be moved from,** since neither can be passed by `ref`: something else still owns the value.
- **A moved-from variable of a reference type,** such as a `Task<LockGuard>`, is possibly null afterwards to the compiler's null checks, so declare it with `var` or as nullable.
- **A move out of a slot of an unconstrained `T`** gives the compiler's CS8601 for the same reason, as `NonCopyable.Move(ref _items[index])` does in a collection with no `where T : struct`. Declare the slots as `T?[]`, or suppress CS8601 at the move.
- **A container** can be moved only out of a local that the method owns: see [Moving a Container](moves-of-containers.md#moving-a-container).

## Paths Through the Method

The analyzer follows every path from each read of the parameter to its reset.

#### Leaving Before the Reset

The method cannot return or throw after it reads the parameter and before it assigns it. The warning is at the read:

```cs
static LockGuard Take([MovedFrom] ref LockGuard value, bool keep)
{
    LockGuard taken = value;        // SCA1041: on one path the method leaves before 'value' is reset
    if (keep)
        return taken;

    value = default;
    return taken;                   // OK
}
```

#### Reading Twice

The method cannot read the parameter a second time before it is assigned. A loop is fine if it assigns the parameter after every read. A backward `goto` counts as a loop.

Two parameters can refer to the same variable, whether or not both are marked. So reading a second parameter after the first counts as a second read as well, even where both are [known to refer to something](null-references.md):

```cs
static (LockGuard, LockGuard) Both([MovedFrom] ref LockGuard a, [MovedFrom] ref LockGuard b)
{
    NonCopyable.ThrowIfNullRef(in a);
    NonCopyable.ThrowIfNullRef(in b);
    var both = (a, b);              // SCA1041: if 'a' and 'b' are one variable, both elements are the same guard
    a = default;
    b = default;
    return both;
}
```

#### What Counts as the Reset

The reset is an assignment of the parameter as a whole. These forms count, and these do not:

| Form | Counts as the reset? |
|---|---|
| `value = default;`, or any other plain assignment | Yes |
| A write through a `ref` local that was declared as another name for the parameter, as in `ref T alias = ref value;` | Yes |
| Passing the parameter by reference to an `out` parameter or a `[MovedFrom]` parameter, before any read | Yes: it is the move itself, as in `=> NonCopyable.Move(ref value);` |
| The same after a read | No: it is a call like any other, and gets a warning |
| Passing the parameter by reference to any other parameter | No: it does not assign the parameter |
| `value ??= other;` | No. It leaves a value that is not null in place |
| <xref:System.Threading.Interlocked.Exchange*?displayProperty=nameWithType>, as in `Interlocked.Exchange(ref value, null);` | See [Read and Reset in One Call](#read-and-reset-in-one-call) |

> [!WARNING]
> The method must never make the parameter refer to something else, as in `value = ref other;`. The reset that follows would then reset the other variable.

#### Exception Filters

A read inside an exception filter has to be followed by the reset inside the same filter. A filter that gives `false` lets the exception propagate, and the method then continues, or leaves, with the parameter as it was.

#### Methods That Take Nothing

A method that only uses the parameter in place, such as `value.Dispose();`, has taken nothing out and gets no warning.

#### Extension Block Receivers

The receiver of an extension block is a parameter. With `extension([MovedFrom] ref LockGuard guard)`, the analyzer checks every member of the block by these rules. The mark on a receiver that is not `ref` gets a warning, like the mark on any other by-value parameter.

## Between the Read and the Reset

Between the read and the reset, anything that fails leaves the value in two places, for a caller that catches the exception. And anything that reaches the parameter in another way can take the value a second time. So only a closed list of operations is allowed there, and everything else counts as a call.

The analyzer goes by the order of evaluation, operation by operation, not by the order of the text.

#### One Store of the Value

The value that was read can be stored once, wherever it is going: a field, an element, or through a `ref` or `out` parameter. That store is allowed even where a store to the same target would otherwise count as able to fail. If it fails, the value is still only in the parameter.

```cs
static void Hand([MovedFrom] ref LockGuard from, ref LockGuard to)
{
    to = from;                      // OK: the one store of the value that was read
    from = default;
}
```

The simplest case is the body of a move, where the one store is into a local: see [Writing Your Own Move](#writing-your-own-move).

#### References Can Be Null

<xref:System.Runtime.CompilerServices.Unsafe.NullRef*?displayProperty=nameWithType> makes a null reference in code that the language calls safe. A read or a write through it fails with a <xref:System.NullReferenceException>, which the caller can catch with the value in two places. So these count as able to fail:

- a read or a write through another `ref`, `in` or `out` parameter, or through a `ref` local;
- a field of `this` in a struct, because `this` in a struct is such a reference.

```cs
static void HandAndClear([MovedFrom] ref LockGuard a, ref LockGuard b, ref LockGuard c)
{
    b = a;                          // OK: the one store
    c = default;                    // SCA1041: the write through 'c' fails if 'c' is a null reference
    a = default;
}

static void ClearAndHand([MovedFrom] ref LockGuard a, ref LockGuard b, ref LockGuard c)
{
    c = default;                    // OK: before the read
    b = a;
    a = default;
}
```

`this` in a struct counts as such a reference however it is used, even where no field is named: see [Reads and Stores](what-can-throw.md#reads-and-stores).

A reference that is known to refer to something is the exception. The usual proof is a check before the read:

```cs
static void HandAndCount([MovedFrom] ref LockGuard from, ref LockGuard to, ref int count)
{
    NonCopyable.ThrowIfNullRef(in count);
    to = from;                      // OK: the one store of the value that was read
    count++;                        // OK: checked above. Without the check it is SCA1041
    from = default;
}
```

The three proofs, and what proves nothing, are in [Null References](null-references.md#proofs).

#### Instance Fields of This, in a Class

In an instance member of a class, a read or a plain store of an instance field of `this` cannot fail, so a state flag can be set after the store:

```cs
// In a class:
void Take([MovedFrom] ref LockGuard value)
{
    _held = value;
    _state = State.Held;            // OK: an instance field of 'this' in a class
    value = default;
}
```

That does not hold for a static field, whose first use can run the static initializer of its type, for a field of another object, which can be null, or for a field of `this` in a struct, which is a reference (see above).

> [!NOTE]
> The parameter can refer to a field of `this`. So reading a non-copyable value by value out of a field of `this` can take the same value a second time. That read gets a warning as the copy of a stored value (SCA1001), as it does anywhere.

#### Where an Assignment Stores

An assignment stores its value when the assignment runs, wherever its target appears in the text. In this statement the target `node.Owner` is evaluated before the read, and written after the inner store into `_current`:

```cs
node.Owner = _current = value;      // the write to 'node.Owner' comes after the read, and fails if 'node' is null
```

So the analyzer judges the target of every assignment between the read and the reset, and each target of a deconstruction, at the point where it stores, by the rules in [Assignments and Deconstructions](what-can-throw.md#assignments-and-deconstructions). The one store of the value that was read is safe wherever it goes.

##### Target Chosen by a Condition

A `ref` conditional is a target like each of its branches:

```cs
(useSpare ? ref spare : ref task) = s_cached;       // SCA1041: as 'task = s_cached;' is, it can leave the cached task in 'task'
Fill(out (useSpare ? ref spare : ref task));        // passes 'task' on by reference
```

The same goes for a `ref` local over such a reference. What a method may leave in a `[MovedFrom]` parameter of a container type, such as a task, is under [What a Move Leaves Behind](moves-of-containers.md#what-a-move-leaves-behind).

#### Code Hidden in Simple Operations

What looks like a read or a write of a local must run no other code. Some operations run code that the method does not see, and count as calls: a concatenation of a string with something that is not a string calls `ToString`, a conversion of a tuple converts its elements, and a conversion from `dynamic` runs the runtime binder. So do a positional pattern or `is ()` on anything but a tuple, which calls a member such as `Deconstruct`, and a store through a property, an indexer or an event.

The full closed list, which [the commit at the end of a try block](commit.md) uses too, is in [What Counts as Able to Throw](what-can-throw.md).

#### Swaps

A swap reads the other parameter only to assign it to the first, and is fine:

```cs
static void Swap([MovedFrom] ref LockGuard a, [MovedFrom] ref LockGuard b)
{
    LockGuard held = a;
    a = b;                          // OK: 'b' is read only to be assigned to 'a'
    b = held;
}
```

The read of `b` can fail, but then the value that was read from `a` is only in the local `held`, which is gone with the exception.

That stops being true once the method stores the local somewhere that outlives the exception: a field, a write through a reference, a deconstruction, a conditional, or a lambda that uses it. A read through the other reference after that gets a warning:

```cs
// In a class:
void Replace([MovedFrom] ref LockGuard slot, [MovedFrom] ref LockGuard replacement)
{
    LockGuard old = slot;
    _previous = old;                // the value is now in a field as well
    slot = replacement;             // SCA1041: if reading 'replacement' fails, the guard is in '_previous' and in 'slot'
    replacement = default;
}
```

#### Group Assignments

In general a deconstruction is judged by [the rule for deconstructions](what-can-throw.md#assignments-and-deconstructions): its stores are made one after another, so at most one target may be able to fail.

The analyzer judges a tuple that is written out on the right more precisely, as a **group assignment**, if its elements run nothing. They may be plain reads, `default`, constants, a struct created all zeros, or the operators of the language on those, but not a call. All the values on the right are read, from left to right, before any store is made. That allows three things:

| Rule | Why |
|---|---|
| A reference that is read on the right as a whole element, for a target that is not a local or a discard, is known to refer to something when the stores run | Its value is stored, which is a [proof](null-references.md#proofs). An operand, as in `count + 1`, proves nothing |
| A store that can fail gets a warning only if an earlier store in the same assignment stored a value read from a `[MovedFrom]` parameter | Before that, a failure leaves every value where it was |
| At most two `[MovedFrom]` parameters are read on the right | See [Two Parameters on the Right](#two-parameters-on-the-right) |

```cs
static void Rotate([MovedFrom] ref LockGuard a, [MovedFrom] ref LockGuard b, [MovedFrom] ref LockGuard c)
{
    (a, b, c) = (b, c, default);            // OK: 'b' and 'c' were read, so only the first store can fail
}

static void Spill([MovedFrom] ref LockGuard a, [MovedFrom] ref LockGuard b, ref LockGuard c)
{
    (b, c, a) = (a, default, default);      // SCA1041: 'c' was not read, and its store follows the store of the value
    (c, b, a) = (default, a, default);      // OK: nothing was moved before the stores that can fail
}

static void Count([MovedFrom] ref LockGuard from, ref LockGuard to, ref int count)
{
    NonCopyable.ThrowIfNullRef(in count);
    (to, count) = (from, count + 1);        // OK after the check. Without it, the store through 'count' is SCA1041
    from = default;
}
```

In `Rotate`, the stores into `b` and `c` cannot fail, since both were read on the right. The store into `a` can, but it comes first, before anything was moved. In `Spill`, the store through `c` comes after the store of the value that was read from `a`.

A deconstruction of anything else, such as a tuple in a variable or a type with a `Deconstruct` method, is judged by the general rule above.

##### Two Parameters on the Right

Two `[MovedFrom]` parameters can refer to the same variable, and then the right side reads one value twice. That is harmless in two shapes only:

| Shape | Example | Result |
|---|---|---|
| A swap: each of the two receives the other's value | `(a, b) = (b, a);` | OK |
| A shift by one: one receives the other's value, and is stored before the other is | `(a, b, c) = (b, c, default);` in `Rotate` | OK |
| Three reads, or two whose values both go elsewhere | `(p, q, a, b) = (a, b, default, default);` | No group assignment: judged by the general rule, under which this one gets a warning |

```cs
static void Swap([MovedFrom] ref LockGuard a, [MovedFrom] ref LockGuard b)
{
    (a, b) = (b, a);                              // OK: a swap
}

static void Spread([MovedFrom] ref LockGuard a, [MovedFrom] ref LockGuard b, ref LockGuard p, ref LockGuard q)
{
    (p, q, a, b) = (a, b, default, default);      // SCA1041
}
```

With three reads, or with two whose values both go elsewhere, one value could end up in two places.

#### Read and Reset in One Call

<xref:System.Threading.Interlocked.Exchange*?displayProperty=nameWithType> reads the parameter and assigns it in one operation:

```cs
static Task<LockGuard>? Take([MovedFrom] ref Task<LockGuard>? task)
{
    return Interlocked.Exchange(ref task, null);    // OK: the read and the reset at once
}

static Task<LockGuard>? TakeLater([MovedFrom] ref Task<LockGuard>? task)
{
    var taken = task;
    Interlocked.Exchange(ref task, null);           // OK: after a read, it is the reset where its result is discarded
    return taken;
}
```

If its result is used after a read, the call hands out the value a second time, and it gets a warning as a second read.

> [!IMPORTANT]
> Only <xref:System.Threading.Interlocked.Exchange*?displayProperty=nameWithType> itself counts, and its generic form only for a reference type: for a struct that is not a primitive type, it throws.

#### Which Move Helper Counts

A call of <xref:Singulink.CodeAnalysis.NonCopyable.Move*?displayProperty=nameWithType> on a local is allowed between the read and the reset, because its body only reads, resets and returns. That holds only for the helper in the namespace `Singulink.CodeAnalysis`:

| Helper | Allowed between the read and the reset? |
|---|---|
| <xref:Singulink.CodeAnalysis.NonCopyable.Move*?displayProperty=nameWithType> from the package | Yes |
| A class named `NonCopyable` in another namespace | No: it is an ordinary call |
| A `NonCopyable.Move` that the project declares in `Singulink.CodeAnalysis` | Only if its body is the read, the reset and the return, with no static constructor or static field initializer |

A static constructor or a static field initializer runs at the first call, and can throw there.

## Writing Your Own Move

A project that does not reference the attributes package, or that has to move a ref struct on a target before .NET 10, writes its own move. It is a `[MovedFrom]` method like any other, and the analyzer checks it by the rules above:

```cs
static LockGuard Move([MovedFrom] ref LockGuard value)
{
    LockGuard moved = value;
    value = default;
    return moved;
}
```

Such a helper counts as running nothing between the read and the reset of another parameter only under the conditions in [Which Move Helper Counts](#which-move-helper-counts).

## Containers, Generic Moves and the Mark

A `[MovedFrom]` parameter of a container type, a generic move and what a move may leave behind have rules of their own: see [Moving Containers and Generic Moves](moves-of-containers.md). Overrides, implementations and delegates must keep the mark: see [Keeping Marks on Overrides and Delegates](keeping-marks.md#movedfrom-mark).

</div>
