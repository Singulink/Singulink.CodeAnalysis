<div class="article">

# Null References

A reference can be null: <xref:System.Runtime.CompilerServices.Unsafe.NullRef*?displayProperty=nameWithType> makes one in code that the language calls safe. So the two rules that ask [what can throw](what-can-throw.md) count every read and every store through a reference as able to fail, unless the reference is known to refer to something. This page says when a reference is known to refer to something, and how the `[NotNullRef]` mark proves it.

### At a Glance

- **The two rules:** the stretch between the read and the reset of a [`[MovedFrom]` parameter](moved-from.md), and what follows [the commit at the end of a try block](commit.md).
- **A reference** here is a `ref`, `in` or `out` parameter, a `ref` local, or `this` in a struct.
- **Known to refer to something** at a place means that one of three proofs has run on that reference on every path to that place:

| Proof | Example |
|---|---|
| A call that returns, with the reference passed to a parameter marked `[NotNullRef]` | `NonCopyable.ThrowIfNullRef(in count);` |
| A plain store to what the reference refers to, or to an instance field of it | `count = 0;` |
| A by-value read of it, or of an instance field of it, whose value is stored into a field, an element or through a reference | `_last = count;` |

Where a reference is known to refer to something, a read or a plain store through it does not count as able to throw.

## Known at a Place

A reference is known at a place if one of the proofs has run on it on every path from the start of the method to that place. Nothing after the proof may make the reference refer to something else, as `x = ref other;` does, or the declaration of a `ref` local, even in a loop.

- **A handler** knows only what was known where its `try` statement began. Of that, it loses every reference that the statement makes refer to something else before the handler runs, including in the filters of earlier clauses.
- **Every method, lambda and local function** is judged on its own.

```cs
static void Hand([MovedFrom] ref LockGuard from, ref LockGuard to, ref int count)
{
    NonCopyable.ThrowIfNullRef(in count);
    to = from;                              // OK: the one store of the value that was read
    count++;                                // OK: checked above. Without the check it is SCA1041
    from = default;
}
```

## Proofs

#### Marked Call

When a call returns, it proves every argument that it passed to a parameter marked `[NotNullRef]`, wherever that argument stands in the call. It is a call, so it goes before the read of a `[MovedFrom]` parameter and before a commit. See [NotNullRef Mark](#notnullref-mark).

#### Plain Store

A plain store to what the reference refers to, or to an instance field of it, proves it. A compound assignment and an increment on a [simple type](what-can-throw.md#operators) are a read and a store, so they prove it too.

```cs
static void Reset([MovedFrom] ref LockGuard from, ref LockGuard to, ref int count)
{
    count = 0;                              // a plain store: 'count' is known from here on
    to = from;
    count++;                                // OK
    from = default;
}
```

#### Read Whose Value Is Stored

A by-value read of what the reference refers to, or of an instance field of it, proves the reference if the value that it reads is stored: into a field of an object or a static field, into an element, or through a reference. It also proves the reference if it stands on the right of a [group assignment](moved-from.md#group-assignments) for such a target.

```cs
// In a class:
void Hand([MovedFrom] ref LockGuard from, ref LockGuard to, ref int count)
{
    _last = count;                          // the value is stored in a field: 'count' is known
    to = from;
    count++;                                // OK
    from = default;
}
```

A read into anything else proves nothing, including a read into a field of a local of the method or into a property.

## What Proves Nothing

| Use of the reference | Why it proves nothing |
|---|---|
| Any other read, whatever becomes of its value (see below) | A compiler leaves out a read whose value nothing needs, so such a read is no check |
| A call of a member on it | A method of a struct is entered through a null reference with no check |
| Passing it on by reference to a parameter that is not marked | The callee may not touch it |
| Taking a reference to a field of it | It is no proof. For `this` in a struct it is even a read that can fail: see [Field of This in a Struct](#field-of-this-in-a-struct) |
| A test with <xref:System.Runtime.CompilerServices.Unsafe.IsNullRef*?displayProperty=nameWithType> | Its result is a condition, and a condition proves nothing |

"Any other read" is one of these:

- a read into a local or a discard (`int before = x;`, `_ = x;`);
- a read as an argument (`Use(x)`) or as an operand (`x + 1`, `x > 0`);
- a read as a condition or in a pattern.

A store cannot be left out, and neither can the read of a value that is stored. Code that has neither at hand can call a method whose parameter is marked `[NotNullRef]`.

```cs
static void Hand([MovedFrom] ref LockGuard from, ref LockGuard to, ref int count)
{
    int before = count;                     // a read into a local proves nothing
    to = from;
    count++;                                // SCA1041: 'count' can be a null reference
    from = default;
}
```

#### Calls That a Build Can Leave Out

Inside a call that the compiler can leave out of a build, nothing is a proof, not even a store or a marked call. Whatever stands in the arguments of a call of a `[Conditional]` method, or of a partial method with no body, is gone with the call. That holds even with a condition, a `&&`, a `?:` or a `switch` in the argument.

In `Debug.Assert(IsThere(in x));` and in `Debug.Assert(IsThere(in x) && x >= 0);` the check runs in a debug build only.

```cs
static void Hand([MovedFrom] ref LockGuard from, ref LockGuard to, ref int count)
{
    Debug.Assert(IsThere(in count));        // gone in a release build: it proves nothing
    to = from;
    count++;                                // SCA1041
    from = default;
}
```

## Referring to Fields and Repointing

#### Field of This in a Struct

Taking a reference to a field of `this` in a struct runs a check of `this`. It proves nothing, but it is a read through `this`, so `ref int counter = ref _count;` can fail unless `this` is known to refer to something.

```cs
// In a struct:
void Take([MovedFrom] ref LockGuard value, ref LockGuard to)
{
    to = value;
    ref int counter = ref _count;           // SCA1041: a check of 'this', which can be a null reference
    value = default;
}
```

#### Repointing a Reference

`x = ref y` is a read through `y`, and it proves nothing. That includes the declaration `ref int x = ref y;`. Used by value, the assignment is a read of what `x` then refers to, as in `int before = (counter = ref other);`.

So it can fail unless `y` is known to refer to something, when `y` is a `ref`, `in` or `out` parameter, a `ref` local, or a parameter of a primary constructor that a struct keeps. A reference to a local or a by-value parameter of the method, or to the `[MovedFrom]` parameter itself, reads nothing that can fail.

```cs
static void Hand([MovedFrom] ref LockGuard from, ref LockGuard to, ref int first)
{
    to = from;
    ref int counter = ref first;            // SCA1041: reads through 'first'
    ref LockGuard alias = ref from;         // OK: refers to the [MovedFrom] parameter itself
    from = default;
}
```

#### A Proof Counts for the Reference as It Was

A proof counts for the reference as it was when the statement began:

```cs
counter = Next(ref (counter = ref second));   // stores through what 'counter' referred to before; afterwards 'counter' is 'second'
```

That statement proves nothing of `second`. So an assignment, an increment or a call proves nothing of a reference that something inside it makes refer to something else.

## What Stays the Same

Being known to refer to something changes nothing else:

- A by-value read of a non-copyable value, or of a [container](non-copyable-types.md#containers) of one, through another reference between the read and the reset of a `[MovedFrom]` parameter still gets a warning. If the two references refer to the same variable, the read is a second read of the same value.
- A call, an element and a member of an object still count as able to throw.

The commit itself is often a proof: after `_held = guard;` in a struct, `this` is known to refer to something, so a flag may follow it. See [This in a Struct](commit.md#this-in-a-struct).

## NotNullRef Mark

`[NotNullRef]` goes on a `ref`, `in`, `ref readonly` or `out` parameter. It says that the method returns only if the reference that it is given refers to something.

#### Where the Mark Counts

The mark counts only where the call itself determines which code runs:

| Method | Does the mark count? |
|---|---|
| A method that is not `virtual`, `abstract` or an `override`, static or not, of a class or a struct, an extension method included | Yes |
| A local function | Yes |
| A method that implements an interface member, called by its own name | Yes |
| The same method, called through the interface | No |
| Any other member, such as a virtual member, a delegate or a constructor (see below) | No: other code can run instead. The mark gets a warning where it stands (SCA1046) |

"Any other member" is a virtual or abstract member, an override, a member of an interface, a delegate, a lambda, an operator, a constructor, an accessor, and the receiver of an extension block.

The mark also gets a warning (SCA1046):

- on a by-value parameter, where it says nothing;
- on a method that the compiler can leave out of a build: one that is `[Conditional]`, or a partial method with no body. A check that is there in one build and not in the next proves nothing.

#### A Mark Taken on Trust

The analyzer does not check the method. The mark is a claim by its author, like a [symbol list entry](symbol-list-format.md): the analyzer looks at no body, and knows no method by its name. So every mark that a project declares gets one warning where the attribute stands (SCA1047). The author suppresses that warning there once the method is verified and tested.

A method of a referenced assembly gets no warning, and its mark counts only if that assembly was built with the analyzer (see [Code Not Built With the Analyzer](unchecked-code.md)).

## Checks in the Package

The attributes package has two such methods, next to <xref:Singulink.CodeAnalysis.NonCopyable.Move*?displayProperty=nameWithType>:

| Method | Throws | Use it for |
|---|---|---|
| `NonCopyable.ThrowIfNullRef(in x)` | A <xref:System.NullReferenceException> whose message names the expression | `this` in a struct (`NonCopyable.ThrowIfNullRef(in this)`), a `ref` local, and wherever a null reference is not the fault of an argument |
| `NonCopyable.ThrowIfNullRefArgument(in parameter)` | An <xref:System.ArgumentNullException> that names the argument | A parameter of a public method |

Where a null reference is not the fault of an argument, `ThrowIfNullRef` makes the method fail as it would have failed at its first use of the reference, only earlier.

The assembly for .NET Standard 2.0 takes <xref:System.Runtime.CompilerServices.Unsafe> from the `System.Runtime.CompilerServices.Unsafe` package, which a project on that target gets along with the package.

A project that [declares its own attributes](../guides/marking-a-struct.md) declares `[NotNullRef]` as well, and writes whatever check suits it, under any name and with any exception:

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

</div>
