<div class="article">

# Null References

A null reference is rare, and the two rules that ask what can throw would otherwise report every read and every store through a
reference: that nothing runs between the read of a `[MovedFrom]` parameter and its assignment ([Moving a value](moving-a-value.md)), and
the [commit at the end of a try block](../concepts/last-use.md). So a read or a plain store through a
reference does not count as able to throw at a place where the reference is known to refer to something. A reference here is a `ref`,
`in` or `out` parameter, a `ref` local, and `this` in a struct.

## Known to Refer to Something

- **Known at a place** means that on every path from the start of the method to that place one of the proofs has run on that reference,
  with nothing after it that makes the reference refer to something else (`x = ref other;`, and the declaration of a `ref` local, in a
  loop too). A handler knows only what was known where its `try` statement began, less everything that the statement makes refer to
  something else before the handler runs, the filters of earlier clauses included. Every method, lambda and local function is judged
  by itself.
- **The proofs are three,** the same for every reference, `this` included:
  - a call that returns, with the reference itself passed to a parameter that is marked `[NotNullRef]`, as
    `NonCopyable.ThrowIfNullRef(in x)` is;
  - a plain store to what the reference refers to, or to an instance field of it. A compound assignment and an increment on a built-in
    type are a read and a store;
  - a by-value read of what the reference refers to, or of an instance field of it, whose value is stored: it is the value of a plain
    store into a field of an object or a static field, into an element or through a reference, and into nothing else, a field of a
    local of the method and a property included (`to = from;`, `_last = count;`), or it stands on the right of a group assignment for
    such a target.
- **What proves nothing,** though it names the reference: every other read, whatever becomes of its value, into a local or a discard
  (`_ = x;`, `int before = x;`), as an argument (`Use(x)`), as an operand (`x + 1`, `x > 0`), as a condition or in a pattern; a call
  of a member on it, since a method of a struct is entered through a null reference with no check; passing it on by reference to a
  parameter that is not marked; taking a reference to a field of it; and a test with <xref:System.Runtime.CompilerServices.Unsafe.IsNullRef*?displayProperty=nameWithType>. A compiler leaves out a
  read whose value nothing needs, so such a read is no check. A store cannot be left out, and neither can the read of a value that
  is stored. Code that has neither at hand calls a method whose parameter is marked `[NotNullRef]`.
- **Nothing proves anything inside a call that the compiler can leave out of a build,** a store and a marked call included: whatever
  stands in the arguments of a call of a `[Conditional]` method, or of a partial method with no body, is gone with the call, with a
  condition, a `&&`, a `?:` or a `switch` in the argument as well. In `Debug.Assert(IsThere(in x));` and in
  `Debug.Assert(IsThere(in x) && x >= 0);` the check runs in a debug build only.
- **Taking a reference to a field of `this` in a struct runs a check of `this`,** and is no proof but a read through it:
  `ref int counter = ref _count;` can fail unless `this` is known.
- **`x = ref y` is a read through `y`,** the declaration `ref int x = ref y;` included, and no proof either. Used by value, the
  assignment is a read of what `x` then refers to (`int before = (counter = ref other);`). So it can fail unless `y` is known to
  refer to something, where `y` is a `ref`, `in` or `out` parameter, a `ref` local, or a parameter of a primary constructor that a
  struct keeps. A reference to a local or a by-value parameter of the method, and one to the `[MovedFrom]` parameter itself
  (`ref LockGuard alias = ref from;`), reads nothing that can fail.
- **A proof counts for the reference as it was when the statement began.** `counter = Next(ref (counter = ref second));` stores through
  what `counter` referred to before, and afterwards `counter` refers to `second`, of which nothing is known. So an assignment, an
  increment or a call proves nothing of a reference that something inside it makes refer to something else.
- **Nothing else changes.** A by-value read of a non-copyable value, or of a [container](../concepts/non-copyable-types.md) of one, through another reference
  between the read and the assignment of a `[MovedFrom]` parameter is reported as before: if the two refer to one variable, it is a
  second read of the same value. A call, an element and a member of an object count as before.

```cs
public void Acquire()                       // in a struct
{
    LockGuard guard = _lock.Enter();
    try
    {
        Prepare();
        _held = guard;                      // the commit; also a store through 'this'
        _isHeld = true;                     // OK: 'this' is known to refer to something
    }
    catch { guard.Dispose(); throw; }
}

static void Hand([MovedFrom] ref LockGuard from, ref LockGuard to, ref int count)
{
    NonCopyable.ThrowIfNullRef(in count);
    to = from;                              // OK: the one store of the value that was read
    count++;                                // OK: checked above. Without the check it is SCA1041
    from = default;
}
```

## The NotNullRef Mark

**`[NotNullRef]`** goes on a `ref`, `in`, `ref readonly` or `out` parameter and says that the method returns only if the reference that
it is given refers to something. A call that returns proves the argument of every parameter that is so marked, wherever it stands in
the call. It is a call, so it goes before the read of a `[MovedFrom]` parameter and before a commit.

- **The mark counts where a call names the code that runs:** on a method that is not `virtual`, `abstract` or an `override`, static or
  not, of a class or a struct, an extension method included, and on a local function. A call of such a method runs the method that it
  binds to. Anywhere else the code that runs can be another, so the mark is reported where it stands (`SCA1046`) and a call proves
  nothing: on a virtual or abstract member, an override, a member of an interface, a delegate, a lambda, an operator, a constructor,
  an accessor and the receiver of an extension block. A method that implements a member of an interface proves its argument where it
  is called by its own name, and not where it is called through the interface.
- **It is also reported** on a by-value parameter, where it says nothing, and on a method that the compiler can leave out of a build:
  one that is `[Conditional]`, or a partial method with no body. A check that is there in one build and not in the next proves nothing.
- **The analyzer does not check the method.** The mark is a statement of its author, like a line of [the safe list](symbol-list.md):
  no body is looked at, and no method is known by its name. So every mark that a project declares is reported once, where the
  attribute stands (`SCA1047`), and the author suppresses that finding there once the method is verified and tested. A method of a
  referenced assembly is not reported.

The attributes package has two such methods, next to <xref:Singulink.CodeAnalysis.NonCopyable.Move*?displayProperty=nameWithType>:

- `NonCopyable.ThrowIfNullRef(in x)` throws a <xref:System.NullReferenceException> whose message names the expression. It is the one for `this` in
  a struct (`NonCopyable.ThrowIfNullRef(in this)`), for a `ref` local, and wherever a null reference is not the fault of an argument:
  the method fails as it would have failed at its first use of the reference, only earlier.
- `NonCopyable.ThrowIfNullRefArgument(in parameter)` throws an <xref:System.ArgumentNullException> that names the argument. It is the one for a
  parameter of a public method.

The assembly for .NET Standard 2.0 takes <xref:System.Runtime.CompilerServices.Unsafe> from the `System.Runtime.CompilerServices.Unsafe` package, which a project on that
target gets with it. A project that [declares its own attributes](marking-a-struct.md) declares `[NotNullRef]` as well and writes
whatever check suits it, under any name and with any exception:

```cs
internal static class Ensure
{
#pragma warning disable SCA1047 // Verified and tested: it throws for a null reference and does nothing else
    public static void RefersToSomething<T>([NotNullRef] ref readonly T reference, [CallerArgumentExpression(nameof(reference))] string? expression = null)
    {
        if (Unsafe.IsNullRef(in reference))
            Throw(expression);

        static void Throw(string? expression) => throw new NullReferenceException($"The reference '{expression}' is a null reference.");
    }
#pragma warning restore SCA1047
}
```

## Group Assignments

**A group assignment reads everything before it stores anything.** In `(a, b, c) = (b, c, default);` the values on the right are
evaluated from left to right, and then the stores are made from left to right. So with a tuple that is written out on the right, whose
elements run nothing when they are worked out (plain reads, `default`, constants, a struct that is created all zeros, and what the
operators of the language make of those; not a call):

- **A reference that is read on the right, as a whole element for a target that is not a local or a discard, is known to refer to
  something when the stores run.** That is the proof by a read whose value is stored. An operand proves nothing here either:
  `(to, count) = (from, count + 1);` is reported at the store through `count`, and is fine after `NonCopyable.ThrowIfNullRef(in count);`.
- **A store that can fail is reported only if a store before it, in the same assignment, stored a value that was read from a
  `[MovedFrom]` parameter.** Before that, a failure leaves every value where it was. A store can fail if its target is anything but a
  local, a by-value parameter, a discard, a reference that is known to refer to something, or an instance field of `this` in a class.
- **At most two `[MovedFrom]` parameters are read on the right, and with two, one of them is assigned the other's value.** Two parameters
  can refer to one variable, and then the right side reads one value twice. That is harmless in two shapes: each of the two receives the
  other's value, which is a swap, or one receives the other's value and is stored before the other is, which is a shift by one. With
  three reads, or with two whose values both go elsewhere, as in `(p, q, a, b) = (a, b, default, default);`, one value can end in two
  places, and the assignment is judged read by read, as it was before.

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
```

A deconstruction of anything but a tuple that is written out calls a `Deconstruct` method or reads a tuple value, and is judged as before.

A variable that a pattern declares has a value that the check does not follow, so in a generic move it is not what the method read from
its parameter: `if (Take() is { } cached) return cached;` is reported like `T? old = Take(); return old;`.

## Which Move Helper Counts

**<xref:Singulink.CodeAnalysis.NonCopyable.Move*?displayProperty=nameWithType> is the helper only in the namespace `Singulink.CodeAnalysis`.** A call of it on a local counts as running nothing
that can throw because of what its body is: the read, the reset and the return. A class of that name in another namespace is an ordinary
class, and a helper that the project declares in the namespace is taken for the helper only if its body is those three statements, and
only if its class has no static constructor and no static field with an initializer: either runs at the first call, and can throw there.

</div>
