<div class="article">

# What Is Not Covered

Ordinary C# code that is built with the analyzers gets a warning wherever it would copy a non-copyable value. That holds even when the copy is made inside the framework or another library.

A few cases are left. Only one of them can happen in ordinary code. The rest either cannot be covered by any analyzer, or take something that you would have to do on purpose. This page goes through them in that order.

### At a Glance

| What | How likely you are to meet it | What to do |
|---|---|---|
| [A class that derives from a cloneable base class](#the-one-to-know-about) of an assembly that was not built with the analyzers | Ordinary code can hit this | Do not hold a non-copyable value in such a class |
| [Unsafe code, pointers, reflection and `dynamic`](#code-that-steps-outside-the-languages-safety) | Only with code that steps outside the language's safety | Nothing new: these can break any guarantee of the language |
| [Live views and value task sources](#apis-used-against-their-documentation) | Only if you ignore the API's documentation | Follow the documentation, as you would without the analyzers |
| [A suppressed warning, a wrong symbol list entry, a `trusted` line](#what-you-vouched-for) | Only where you vouched for something | Vouch only for what you know to be true |
| [Code that was built without the analyzers](#code-built-without-the-analyzers) | Only if a library was built without the analyzers and you vouched for it, or for the ten built-in types | Answer SCA1025 once, where it appears |
| [Interceptors and a compiler newer than the tested ones](#rare-corners) | Rare | Know that the rules judge the method that is written at the call |

## The One to Know About

A base class can clone itself with <xref:System.Object.MemberwiseClone>. Take a base class of an assembly that was not built with the analyzers, and a class of yours that derives from it and holds a non-copyable value. The clone then copies that value in code that the analyzer does not see. <xref:System.Text.Encoding>, <xref:System.Globalization.CultureInfo> and <xref:System.Globalization.Calendar> are the framework's examples: each has a `Clone` that calls `MemberwiseClone`.

```cs
// Encoding is of the framework, and its Clone calls MemberwiseClone
sealed class GuardedEncoding : UTF8Encoding
{
    private LockGuard _guard;                     // OK: a field of a class
}

var copy = (Encoding)encoding.Clone();         // no warning: for a GuardedEncoding, a second object with the same guard
```

> [!WARNING]
> Do not hold a non-copyable value in a class that derives from a base class that clones itself. The analyzer cannot see which base classes of other assemblies clone themselves, so it could only warn about every class that holds a non-copyable value and derives from any of them.

The analyzers warn about a call to `MemberwiseClone` in code that they check: see [SCA1005](../diagnostics/sca1005.md) and, for a class that another assembly can derive from, [SCA1008](../diagnostics/sca1008.md).

## Code That Steps Outside the Language's Safety

The unsafe classes, pointers, reflection and `dynamic` can break any guarantee of the language, this one included. That is as true without the analyzers as with them. The analyzers warn about what they can see, as below, and leave the rest to the author.

#### Unsafe Classes

The members of <xref:System.Runtime.CompilerServices.Unsafe>, <xref:System.Runtime.InteropServices.MemoryMarshal>, <xref:System.Runtime.InteropServices.CollectionsMarshal>, <xref:System.Runtime.InteropServices.ImmutableCollectionsMarshal> and <xref:System.Runtime.InteropServices.Marshal> are on the [built-in framework symbol list](framework-symbol-list.md) for one fact only: they make no copy. What the code does with them is up to its author.

One case has a consequence that is not obvious. `Unsafe.AsRef(in ...)` or a member of <xref:System.Runtime.InteropServices.MemoryMarshal> can make a read-only reference writable. Several rules take `readonly` at its word: a `readonly` member needs no defensive copy and may run on a temporary one, and a copy of a `readonly struct` never differs from the original. A guard whose `readonly` `Dispose` writes through `Unsafe.AsRef(in this)` breaks that for its callers, in code that is itself clean:

```cs
public readonly void Dispose() => Unsafe.AsRef(in this)._lock = null;   // no warning: AsRef makes no copy

LockGuard? maybe = myLock.TryEnter();
maybe?.Dispose();                        // no warning: a readonly member may run on the copy that '?.' takes out,
                                         // so the guard in 'maybe' is still held and can be released again
```

The same goes for an `async` member of a `readonly struct` that reaches a field that way, and for a temporary that the compiler makes for an `in` argument and that is written that way.

Only two rules follow a null reference from <xref:System.Runtime.CompilerServices.Unsafe.NullRef*?displayProperty=nameWithType>: the ones that ask whether a read or a write can fail. They cover the stretch between the read and the reset of a `[MovedFrom]` parameter, and what follows a [commit](commit.md). See [Null References](null-references.md).

#### Pointers

Reading a non-copyable value through a pointer is checked like any other read. Taking the value out by value is a copy and gets a warning, and using it in place is fine. What the analyzer cannot follow is the pointer itself, and memory that is copied as bytes:

```cs
LockGuard copy = *pointer;                    // SCA1001: a copy of the guard that the pointer points to
Take(pointer[1]);                             // SCA1003: the same for an element
pointer->Dispose();                           // OK: the guard is used in place
ref LockGuard reference = ref *pointer;       // OK: a reference, not a copy

LockGuard* second = pointer;                  // no warning: the pointer is copied, not the guard, and nothing tracks who owns the guard now
Buffer.MemoryCopy(pointer, target, size, size);   // no warning: the bytes of the guard are copied, and no rule sees that
```

The same goes for the other ways to copy memory, such as <xref:System.Runtime.InteropServices.NativeMemory.Copy*?displayProperty=nameWithType>, <xref:System.Runtime.CompilerServices.Unsafe.CopyBlock*?displayProperty=nameWithType> and a span of bytes over the pointer.

#### Reflection and Dynamic

- **Reflection** can read a field into a box, and an expression tree that a consumer evaluates by reflection gives a boxed copy of a captured value. Compiling or interpreting the tree uses the value in place.
- **`dynamic`** reaches the result of any erasable container, a completion source and a task included: any `object` can be used as `dynamic`, and nothing checks `d.Task.Result` on it.

#### Layouts, Handles and IL

- **A struct with an explicit layout** that lays another field over a field of a non-copyable type with `[FieldOffset]`.
- **A handle rebuilt from its value.** `GCHandle.FromIntPtr(GCHandle.ToIntPtr(handle))`, a cast of an <xref:System.IntPtr> to a <xref:System.Runtime.InteropServices.GCHandle>, and the same for the generic handle types make a second owner of one handle. The analyzer cannot follow an <xref:System.IntPtr>.
- **What only IL can do,** which C# cannot: a method that reads its `out` parameter before it writes it, or keeps a pointer to something that it was passed by reference. Both matter for [the last use](last-use.md).
- **An instance method called with a null `this`,** which IL and reflection can do.

## APIs Used Against Their Documentation

Some framework members hand out a view of a collection's own storage, and document that the collection must not change while the view is in use. A view that outlives such a change is a mistake with or without the analyzers. Code that follows the documentation of these members is covered.

| Member | Its documentation says |
|---|---|
| <xref:System.Runtime.InteropServices.CollectionsMarshal.AsSpan*?displayProperty=nameWithType> | "Items should not be added or removed from the `List<T>` while the `Span<T>` is in use." |
| <xref:System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrNullRef*?displayProperty=nameWithType> | "Items should not be added or removed from the `Dictionary<TKey,TValue>` while the ref `TValue` is in use." |
| <xref:System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrAddDefault*?displayProperty=nameWithType> | "Items should not be added to or removed from the `Dictionary<TKey,TValue>` while the ref `TValue` is in use." |
| <xref:System.Runtime.InteropServices.ImmutableCollectionsMarshal.AsMemory*?displayProperty=nameWithType> | No remark, but it gives the builder's own storage in the same way, so the same care applies |

Value tasks are the same. Two value tasks made from one source and token are one operation consumed twice, and <xref:System.Threading.Tasks.ValueTask`1> documents that an instance "may only be awaited once", and that otherwise "the results are undefined". The read of the result inside such a source gets a warning, which you suppress there: see [Value Task Sources](listed-members.md#value-task-sources).

## What You Vouched For

Where you tell the analyzer something, it takes your word:

- **a suppressed warning**;
- **a symbol list entry that is wrong**, such as an entry for a member that copies, or for a member of a type that hands itself to another holder (see [Symbol List Format](symbol-list-format.md));
- **a `borrows` or a `new` entry, or a `trusted` line,** for code that was built without the analyzers.

## Code Built Without the Analyzers

A third-party library does not void the protection:

- **A library that was built with the analyzers** is checked like your own code, in its own build, and its marks are trusted.
- **The framework** is covered by the [built-in framework symbol list](framework-symbol-list.md).
- **Any other assembly** cannot be handed a non-copyable value directly to copy it silently. Passed by reference, in a container, in a view or through a pointer, the use gets a warning until you vouch for the member (SCA1024). Passed through a generic, it gets a warning until the member is on the [symbol list](symbol-list-format.md) (SCA1020).
- **A container that such an assembly hands out** counts as stored.
- **An assembly that was not built with the analyzers and uses your non-copyable types** itself gets a warning, once ([SCA1025](../diagnostics/sca1025.md)).

A library that uses non-copyable types is usually built with the analyzers. The attributes package brings them with it, and passes them on unless a library keeps them private (see [Sharing Your Library](../guides/libraries.md)).

So for a copy to go unseen, three things have to come together:

1. **The library was built without the analyzers.** They were turned off, the library that it uses kept them private, or it is written in another language.
2. **Its code breaks the rule of `[NonCopyable]`:** it copies a value of the type.
3. **Your project vouched for the assembly** with a `trusted` line, or suppressed the warning about it (SCA1025).

Two things are left, and both are small:

- **A value that you pass by value belongs to the callee from then on.** That is a move, and it is the same for every callee, analyzed or not.
- **The ten framework structs that the analyzers treat as non-copyable,** such as <xref:System.Threading.SpinLock> and <xref:System.Runtime.InteropServices.GCHandle>, get no SCA1025. That warning works by spotting an unanalyzed assembly that references the assembly which declares your non-copyable type. Every assembly references the framework, so for the framework's own types that reference says nothing. If an unanalyzed library copies a `SpinLock` or a `GCHandle` that it reached through an interface of yours, you get no warning.

[Code Not Built With the Analyzer](unchecked-code.md#what-stays-out-of-reach) lists the ways in which such a library can reach a value.

## Rare Corners

#### Interceptors

A method with `[InterceptsLocation]` replaces the method that a call binds to after the analyzer has looked at the call. Every rule that goes by the method that is called judges the method that is written at the call: a `[NotNullRef]` parameter, <xref:Singulink.CodeAnalysis.NonCopyable.Move*?displayProperty=nameWithType>, `[Kept]` and the [symbol list](symbol-list-format.md).

#### Compilers

The analyzers need a compiler of version 5.0 or later, which is that of the .NET 10 SDK, and do not load in an older one. They are tested with the compilers of the .NET 10 and .NET 11 SDKs. With a newer SDK:

- **New syntax that the analyzers do not know** gets a warning as a use that is not known to be safe.
- **"Not known" covers** an operation of a kind that the analyzers have no judgment for, where it is used by value. It also covers a pattern of a kind that they have no judgment for, where it looks into a value of a non-copyable type.
- **A copy that a newer compiler makes inside syntax that they already know** is covered once that SDK has been tested.

</div>
