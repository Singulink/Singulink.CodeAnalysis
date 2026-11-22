<div class="article">

# What Is Not Covered

Ordinary C# code that is built with the analyzers gets a warning wherever it would copy a non-copyable value. That holds even when the copy is made inside the framework or another library.

A few cases are left, and none of them comes up in ordinary code. Each either cannot be covered by any analyzer, or takes something that you would have to do on purpose. This page goes through them.

### At a Glance

| What | How likely you are to meet it | What to do |
|---|---|---|
| [Unsafe code, pointers, reflection and `dynamic`](#code-that-steps-outside-the-languages-safety) | Only with code that steps outside the language's safety | Nothing new: these can break any guarantee of the language |
| [Live views and value task sources](#apis-used-against-their-documentation) | Only if you ignore the API's documentation | Follow the documentation, as you would without the analyzers |
| [A suppressed warning, a wrong symbol list entry, a `trusted` line](#what-you-vouched-for) | Only where you vouched for something | Vouch only for what you know to be true |
| [Code that was built without the analyzers](#code-built-without-the-analyzers) | Only if a library was built without the analyzers and you vouched for it | Answer SCA1025 once, where it appears |
| [Interceptors, and a framework or compiler newer than the tested ones](#rare-corners) | Rare | Know that the rules judge the method that is written at the call |

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

The same goes for the other ways to copy memory, such as <xref:System.Runtime.InteropServices.NativeMemory.Copy*?displayProperty=nameWithType>, <xref:System.Runtime.CompilerServices.Unsafe.CopyBlock*?displayProperty=nameWithType> and a span of bytes over the pointer. `__makeref` and <xref:System.TypedReference> are not followed either: `TypedReference.ToObject` hands out what the reference refers to as an `object`.

#### Reflection and Dynamic

- **Reflection** can read a field into a box, and an expression tree that a consumer evaluates by reflection gives a boxed copy of a captured value. Compiling or interpreting the tree uses the value in place.
- **`Delegate.Target`** hands out, as an `object`, what a method group was made on: `Action clear = list.Clear;` holds the list, and `Func<string> name = culture.ToString;` the culture. A method group is checked as a use of its member, so a method group of a member that clones gets the same warning as a call of it. Code that reads the target is not followed, as reflection is not.
- **`dynamic`** reaches the result of any erasable container, a completion source and a task included: any `object` can be used as `dynamic`, and nothing checks `d.Task.Result` on it. Converting to `dynamic` a container, or an object whose class binds a non-copyable type behind a base class that clones itself, gets the same warning as converting it to `object`. What the dynamic binder does at run time with a member of a `dynamic` object is not checked:

  ```cs
  dynamic direct = culture;                                // SCA1009: hands the culture to code that can clone it
  dynamic holder = new TaggedHolder { Culture = culture }; // no warning: what the binder does with the field is not checked
  ```

  Here `TaggedHolder` is a class with a field of type `TaggedCulture<LockGuard>`; the type `TaggedHolder` itself shows no binding.

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
- **a `clones=complete` line for a class whose `clones` lines leave a member out:** the marker says that the lines name every member that clones the object or hands it on (see [Clones](symbol-list-format.md#clones));
- **a `borrows` or a `new` entry, or a `trusted` line,** for code that was built without the analyzers;
- **a `clones=none` or a `trusted` line for a base class that does clone itself:** the line says that no code of the class, or of the assembly, clones the object (see [SCA1009](../diagnostics/sca1009.md#a-base-class-that-is-not-known)).

## Code Built Without the Analyzers

A third-party library does not void the protection:

- **A library that was built with the analyzers** is checked like your own code, in its own build, and its marks are trusted.
- **The framework** is covered by the [built-in framework symbol list](framework-symbol-list.md).
- **Any other assembly** cannot be handed a non-copyable value directly to copy it silently. Passed by reference, in a container, in a view or through a pointer, the use gets a warning until you vouch for the member (SCA1024). Passed through a generic, it gets a warning until the member is on the [symbol list](symbol-list-format.md) (SCA1020).
- **A container that such an assembly hands out** counts as stored.
- **A base class of such an assembly** may clone itself, and the analyzer cannot tell. A class of yours that derives from it gets a warning on each field that holds a non-copyable value, until a symbol list line says whether the base class clones itself ([SCA1009](../diagnostics/sca1009.md#a-base-class-that-is-not-known)).
- **An assembly that was not built with the analyzers** itself gets a warning, once, if it uses your non-copyable types or can get at a non-copyable value through a member of your code ([SCA1025](../diagnostics/sca1025.md)).

A library that uses non-copyable types is usually built with the analyzers. The attributes package brings them with it, and passes them on unless a library keeps them private (see [Sharing Your Library](../guides/libraries.md)).

So for a copy to go unseen, three things have to come together:

1. **The library was built without the analyzers.** They were turned off, the library that it uses kept them private, or it is written in another language.
2. **Its code breaks the rule of `[NonCopyable]`:** it copies a value of the type.
3. **Your project vouched for it** with a `trusted` or a `clones=none` line, or suppressed the warning about it (SCA1025 or SCA1009).

One thing is left, and it is small: a value that you pass by value belongs to the callee from then on. That is a move, and it is the same for every callee, analyzed or not.

[Code Not Built With the Analyzer](unchecked-code.md#what-stays-out-of-reach) lists the ways in which such a library can reach a value.

## Rare Corners

#### Interceptors

A method with `[InterceptsLocation]` replaces the method that a call binds to after the analyzer has looked at the call. Every rule that goes by the method that is called judges the method that is written at the call: a `[NotNullRef]` parameter, <xref:Singulink.CodeAnalysis.NonCopyable.Move*?displayProperty=nameWithType>, `[Kept]` and the [symbol list](symbol-list-format.md).

#### Newer Frameworks

The [built-in framework symbol list](framework-symbol-list.md#classes-that-clone-themselves) names every class of the framework that clones itself. It was checked against .NET 10 and .NET 11. A class that a newer framework adds, or one that starts to clone itself there, is not on the list until the list has been checked against that framework.

#### Compilers

The analyzers need a compiler of version 5.0 or later, which is that of the .NET 10 SDK, and do not load in an older one. They are tested with the compilers of the .NET 10 and .NET 11 SDKs. With a newer SDK:

- **New syntax that the analyzers do not know** gets a warning as a use that is not known to be safe.
- **"Not known" covers** an operation of a kind that the analyzers have no judgment for, where it is used by value. It also covers a pattern of a kind that they have no judgment for, where it looks into a value of a non-copyable type.
- **A copy that a newer compiler makes inside syntax that they already know** is covered once that SDK has been tested.

</div>
