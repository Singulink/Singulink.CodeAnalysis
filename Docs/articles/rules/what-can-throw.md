<div class="article">

# What Counts as Able to Throw

Two rules ask whether anything can throw: [the commit at the end of a try block](commit.md), after its store, and [a `[MovedFrom]` parameter](moved-from.md#between-the-read-and-the-reset), between its read and its reset. Both use one closed list of the operations that cannot throw and run no other code. Whatever is not on it counts as a call.

### At a Glance

- **One set of operations.** The analyzer uses the same closed list, given below, for the commit and for a `[MovedFrom]` parameter.
- **A few more for a `[MovedFrom]` parameter.** Between its read and its reset, a few operations that touch the parameter itself, and a move of a local, run nothing. After a commit they count as able to throw. See [Only Between the Read and the Reset](#only-between-the-read-and-the-reset).
- **Not counted:** running out of memory, and a thread abort on .NET Framework. So boxing, whose only way to fail is that memory runs out, is on the closed list.

## Closed List

#### Reads and Stores

| Operation | Runs nothing? |
|---|---|
| A read or a plain store of a local or a by-value parameter of the method, a discard, or a field of one of those | Yes |
| A constant, `default`, a literal, `typeof`, `sizeof` | Yes |
| A tuple literal | Yes |
| A struct that is created all zeros: no constructor of its own, no arguments, no initializer and no field initializers | Yes |
| In an instance member of a class, a read or a plain store of an instance field of `this` | Yes |
| A read or a plain store through a reference that is [known to refer to something](null-references.md) | Yes |
| A read or a write through a `ref`, `in` or `out` parameter or a `ref` local that is not known to refer to something | No: a reference can be null |
| A field of `this` in a struct, where `this` is not known to refer to something | No: `this` in a struct is such a reference |
| A static field | No: its first use can run the initializer of its type |
| A field of another object | No: the object can be null |
| An element of an array or of an inline array, an index from the end | No |
| A property, an indexer, an event, an `await`, any call | No: a call |
| A store through a setter, an indexer or an event accessor | No: a call |

`this` in a struct is such a reference however it is used, even where no field is named:

```cs
// In a struct, each of these counts as able to fail unless 'this' is known to refer to something:
this = default;
var copy = this;
if (this is { State: 1 }) { }
// ...and so does a use of a parameter of a primary constructor that the struct keeps, which is a field of 'this'.
```

#### Operators

| Operation | Runs nothing? |
|---|---|
| `+`, `-`, `*`, `&`, `\|`, `^`, `<<`, `>>`, `>>>` and the comparisons, on the simple types, outside a `checked` context | Yes |
| Unary `+`, `-`, `~`, `!` on the simple types, and `++` and `--` on a variable of a simple type, outside a `checked` context | Yes |
| A test for null, and `==` or `!=` between two references, two strings included | Yes |
| Division and remainder | No: they throw for a zero |
| Arithmetic on `decimal` | No |
| An operator in a `checked` context | No |
| `^n`, a range | No: <xref:System.Index> throws for a negative number |
| A user-defined operator, an operator on `dynamic` | No: a call |
| The concatenation of a string with something that is not a string | No: it calls `ToString` |
| The concatenation of two strings, `+=` included | No: the result can be too long for a string, which code can catch. It runs nothing where the compiler folds it to a constant |

The simple types are the integers, `bool`, `char`, `float`, `double`, enums and pointers.

```cs
// After a commit:
LockGuard guard = myLock.Enter();
try { Work(); _held = guard; _label = _prefix + _suffix; }   // the concatenation can throw...
catch { guard.Dispose(); throw; }                            // SCA1016: ...so the catch can run after the store
```

"Outside a `checked` context" applies to a compound assignment or an increment as a whole. On a type narrower than `int`, the result is converted back, and in a `checked` context that conversion is checked, even where the operator itself cannot overflow:

```cs
// '_slot' is a byte:
LockGuard guard = myLock.Enter();
try { Work(); _held = guard; checked { _slot <<= 1; } }      // in a checked context the shift can throw...
catch { guard.Dispose(); throw; }                            // SCA1016: ...so the catch can run after the store

LockGuard other = myLock.Enter();
try { Work(); _held = other; _slot <<= 1; }                  // OK: outside a checked context
catch { other.Dispose(); throw; }
```

The context is the nearest `checked` or `unchecked` block or expression around the operation. With neither, it is how the project is compiled (`<CheckForOverflowUnderflow>`).

#### Conversions

| Conversion | Runs nothing? |
|---|---|
| An implicit conversion that the language makes with no call (see below) | Yes |
| The conversion of a tuple literal, element by element | Yes |
| An explicit conversion, or one in a `checked` context | No |
| A conversion to `decimal` | No: it is a method of `decimal` |
| A user-defined conversion, a conversion from `dynamic` | No: a call |
| Any other conversion of a tuple | No: it can run a user-defined conversion of an element |

The implicit conversions that the language makes with no call are these:

- an identity, a `null` or `default` literal, a constant;
- a numeric one between simple types;
- a reference conversion, boxing, an enum;
- a nullable of the same type, or between simple types;
- the conversion of a conditional, a `switch` or a `throw` expression.

#### Tests and Patterns

| Operation | Runs nothing? |
|---|---|
| A test for a class or a struct, such as `owner is string` | Yes |
| A test for an interface or a type parameter on a struct or a sealed class, or one that every value passes | Yes |
| Any other test for an interface or a type parameter, even as a type pattern or a declaration pattern | No: the runtime asks an object of a class that implements <xref:System.Runtime.InteropServices.IDynamicInterfaceCastable>, by calling a method of the object |
| A constant pattern, a discard, `not`, `and`, `or`, a relational pattern | Yes |
| A positional pattern on a tuple | Yes |
| A positional pattern on anything else, `is ()` included | No: it calls `Deconstruct`, or a member of <xref:System.Runtime.CompilerServices.ITuple> |
| A list pattern, a slice pattern | No: they call the length and the indexer |

A property that a pattern reads is a property read, and counts as a call.

The exception for a sealed class applies only where the tested type has no type parameter in it. The compiler knows the answer for `closed is IPooled`, but the object answers `closed is T` and `closed is IPooled<T>` when the code runs.

#### Assignments and Deconstructions

An assignment stores its value when the assignment runs, wherever its target appears in the text: see [Where an Assignment Stores](commit.md#where-an-assignment-stores). A store is safe only into one of these:

- a local or a by-value parameter of the method, or a part of one of those;
- a discard;
- an instance field of `this`, in a class;
- a reference that is known to refer to something.

A deconstruction stores into its targets one after the other, so a second store that fails leaves the first one done. It runs nothing only if all of these hold: it calls no `Deconstruct` method, it converts nothing on the way, and at most one of its targets is something other than a local, a by-value parameter or a discard. The analyzer looks more closely at a tuple written out on the right, as a [group assignment](moved-from.md#group-assignments).

Making a reference refer to something else, as in `x = ref y;`, stores nothing, but reads through `y`: see [Null References](null-references.md#repointing-a-reference).

## Only Between the Read and the Reset

These run nothing between the read and the reset of a `[MovedFrom]` parameter, and count as able to throw after a commit:

| Operation | Why it is allowed there |
|---|---|
| <xref:Singulink.CodeAnalysis.NonCopyable.Move*?displayProperty=nameWithType> of a local | Its body only reads, resets and returns. See [Which Move Helper Counts](moved-from.md#which-move-helper-counts) |
| <xref:System.Threading.Interlocked.Exchange*?displayProperty=nameWithType> of the parameter | It reads and resets the parameter in one operation |
| The one store of the value that was read | If it fails, the value is still only in the parameter |
| A read or a write through a `ref` local that is another name for the parameter | It is the parameter |
| In `a = b;`, after the value of `a` went into a local, the read of `b` | It is part of the reset of `a`: see [Swaps](moved-from.md#swaps) |

```cs
static void Hand([MovedFrom] ref LockGuard value, ref LockGuard to, LockGuard spare)
{
    to = value;
    var mine = NonCopyable.Move(ref spare);  // OK between the read and the reset
    value = default;
}
```

```cs
// After a commit:
LockGuard guard = myLock.Enter();
try { Work(); _held = guard; var mine = NonCopyable.Move(ref spare); }   // the move counts as a call here...
catch { guard.Dispose(); throw; }                                        // SCA1016: ...so the catch can run after the store
```

One thing is stricter between the read and the reset. A by-value read of a non-copyable value, or of a container, through another reference gets a warning there even where that reference is known to refer to something. If the two references refer to the same variable, the read is a second read of the same value.

</div>
