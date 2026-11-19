<div class="article">

# Writing a Move Method

A method that takes a value out of its caller's variable, as <xref:Singulink.CodeAnalysis.NonCopyable.Move*?displayProperty=nameWithType> does, marks its `ref` parameter `[MovedFrom]`. Reading the parameter by value is then a move, and in return the method must leave a fresh value behind. This page shows how to write such a method.

### At a Glance

```cs
static LockGuard Move([MovedFrom] ref LockGuard value)
{
    LockGuard moved = value;
    value = default;
    return moved;
}
```

- **Read the parameter, then reset it.** The method may not return or throw after it reads the parameter and before it resets it, and it may not read the parameter twice.
- **Between the read and the reset, run only what cannot fail,** such as reads and writes of the method's own locals and the one store of the value. The table below has the rest.
- **Do whatever can fail first,** before the read, into a local.

## Doing What Can Fail First

Between the read and the reset the value is in two places. If anything throws there, the caller can catch the exception and find the value in both. So whatever can throw comes first, into a local:

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

| Between the read and the reset | Allowed? |
|---|---|
| A read or a write of a local or a by-value parameter of the method | Yes |
| The one store that puts the value where it is going | Yes |
| A field of `this`, in a class | Yes |
| `NonCopyable.Move` of a local | Yes |
| A call, a property, an indexer | No |
| A read or a write through another `ref` parameter, or a field of `this` in a struct | No, unless it is known to refer to something |
| A static field, a field of another object, an array element | No |

## References Can Be Null

A `ref` parameter can be a null reference, which a caller can make with `Unsafe.NullRef`. So a write through another `ref` parameter between the read and the reset counts as able to fail. Check it first:

```cs
static void HandAndCount([MovedFrom] ref LockGuard from, ref LockGuard to, ref int count)
{
    NonCopyable.ThrowIfNullRef(in count);
    to = from;                      // OK: the one store of the value that was read
    count++;                        // OK: checked above. Without the check it is SCA1041
    from = default;
}
```

`NonCopyable.ThrowIfNullRef(in this)` is the check for `this` in a struct, and `NonCopyable.ThrowIfNullRefArgument` throws an `ArgumentNullException` for a parameter of a public method. A project that declares its own attributes writes its own check and marks its parameter `[NotNullRef]`:

```cs
internal static class Ensure
{
#pragma warning disable SCA1047 // Verified and tested: it throws for a null reference and does nothing else
    public static void RefersToSomething<T>(
        [NotNullRef] ref readonly T reference,
        [CallerArgumentExpression(nameof(reference))] string? expression = null)
    {
        if (Unsafe.IsNullRef(in reference))
            Throw(expression);

        static void Throw(string? expression) => throw new NullReferenceException($"The reference '{expression}' is a null reference.");
    }
#pragma warning restore SCA1047
}
```

The analyzer does not look into such a method: the mark is a promise. It gets one warning (SCA1047), which you suppress once you have verified the method. See [NotNullRef Mark](../rules/null-references.md#notnullref-mark).

## Swaps

A swap reads the other parameter only to assign it to the first, and is fine:

```cs
static void Swap([MovedFrom] ref LockGuard a, [MovedFrom] ref LockGuard b)
{
    LockGuard held = a;
    a = b;                          // OK: 'b' is read only to be assigned to 'a'
    b = held;
}
```

Two parameters can refer to the same variable. So reading the second one into anything other than the first is a second read of the same value, and gets a warning.

## Moving Tasks and Generic Moves

In a `[MovedFrom]` parameter of a task or a list type, a move method may leave only what the caller can own: `null`, a new value, or a local of the method's own. Leaving a cached task from a field gets a warning. A generic move, one over a marked `T`, must hand out and leave behind only what it read from its `[MovedFrom]` parameters. See [Moving Containers and Generic Moves](../rules/moves-of-containers.md).

## Keeping the Mark

An override, an implementation, or a lambda or method group that is converted to a delegate, must have `[MovedFrom]` on exactly the same parameters as the member that it stands for (SCA1040).

The exact rules, with every case: [MovedFrom Mark](../rules/moved-from.md).

</div>
