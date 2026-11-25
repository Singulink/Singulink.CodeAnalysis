<div class="article">

# Boxes and Unions

A box holds one value. A new value moves into it, and a stored value is copied into it. This page gives the rules. Converting a container is under [Containers and Conversions](conversions.md).

### At a Glance

| Operation | Result |
|---|---|
| Boxing a new value | OK: the value moves into the box |
| Boxing a stored value | SCA1004: it boxes a copy |
| Boxing a generic struct that is non-copyable only through its type arguments, such as a tuple | SCA1023, even for a new value |
| Taking a value out of a box, in any way | SCA1001 |
| A bare type test, such as `box is LockGuard` | OK: it copies nothing |
| A pattern on a `union` | SCA1001 at every match: a union is a box |

```cs
IDisposable boxed = myLock.Enter();          // OK: the guard moves into the box
boxed.Dispose();                             // OK: runs on the box
object copy = _guard;                        // SCA1004: boxes a copy of a stored value
LockGuard back = (LockGuard)boxed;           // SCA1001: a copy out of the box
object pair = (myLock.Enter(), 1);           // SCA1023: the code of the tuple is not checked
```

## Boxing a New Value

Boxing a new value is a move into the box. Nobody else holds the value, and in the box it is one object that every holder of the reference shares. Calls through an interface run on the box itself.

A value of a marked type parameter is the exception, since a container can be bound to it: see [No Conversion Inside the Generic](bound-containers.md#no-conversion-inside-the-generic).

## Structs That Are Non-copyable Only Through Their Type Arguments

Boxing a generic struct that is non-copyable only through its type arguments gets a warning (SCA1023), even for a new value. Such structs include a tuple, a `ValueTask<LockGuard>` and a `List<LockGuard>.Enumerator`. Their own code is not checked, and what their interfaces hand out from the box, such as `ITuple[0]`, is a boxed copy.

A `LockGuard?` boxes as the guard itself and is not affected, and neither is a struct whose type parameter is marked `[NonCopyable]`.

## Boxing a Stored Value

Boxing a stored value copies it and gets a warning (SCA1004), whether a conversion, `as` or a pattern does it. A bare type test boxes nothing that can be used.

```cs
if (_guard is IDisposable d) { }             // SCA1004: 'd' is a boxed copy of the field
if (_guard is IDisposable) { }               // OK: a bare type test
```

## Taking a Value Out of a Box

Every way back out of a box gets a warning (SCA1001):

- a cast;
- `as`;
- a pattern that declares a variable, or that looks into the value with a member that is not `readonly`;
- an assignment from `dynamic`.

A bare type test (`box is LockGuard`) copies nothing.

## Unions

A union is a box. A `union` declaration (C# 15) makes a struct with one field of type `object`: its constructor boxes the case value, and a copy of the union shares the box.

- **A pattern on a union** takes a copy out of the box at every match (SCA1001). That holds even for a union that a call has just returned, since it can share its box with a union that is stored.
- **A union in every form counts:** a nullable of one (`GuardOrCode?`), and every struct or class that implements `System.Runtime.CompilerServices.IUnion` or carries `System.Runtime.CompilerServices.UnionAttribute`, such as a union written by hand.
- **Use the value in place through the box,** as in `((IDisposable)result.Value!).Dispose();`.
- **A bare type test** copies nothing.

## Type Patterns Between a Struct and a Type Parameter

A type pattern between a struct and a type parameter goes through a box, in both directions:

| Direction | What happens | Warning |
|---|---|---|
| From a value of a type parameter to a struct | The compiler boxes and unboxes: `item is Lease (var read, var write)` on a marked `T` takes a copy out of a box | SCA1001 |
| From a struct to a type parameter | The result can be the box itself: `guard is T t` boxes a copy of a stored guard | SCA1004 |
| A bare test, `item is LockGuard` or `guard is T` | Nothing is copied | OK |

</div>
