<div class="article">

# What Is Not Covered

What the analyzer cannot see, and what it takes on trust.

## The Gaps

- **A base class of an assembly that was not built with the analyzer and that clones itself.** <xref:System.Text.Encoding>,
  <xref:System.Globalization.CultureInfo> and <xref:System.Globalization.Calendar> are examples from the framework: each has a `Clone`
  that calls <xref:System.Object.MemberwiseClone>. A class of the project that derives from one and holds a non-copyable value is copied
  by that call, in code that the analyzer does not see. A call in the code of the project is reported: see [SCA1005](../diagnostics/sca1005.md)
  and [SCA1008](../diagnostics/sca1008.md).
- **What code that was not built with the analyzer does with what it is given.** Such an assembly can copy what it is given, and no rule
  sees that. A value that is passed to it by value is its business from then on, and so is what a `borrows` or a `new` entry vouches for.
  See [Code Not Built With the Analyzer](unchecked-code.md).
- **Code that was not built with the analyzer and runs behind a type of checked code or of the framework, for the built-in non-copyable
  types.** A call is judged by the member that it binds to, so the code that runs behind it is not seen: a class of checked code with a
  field of a non-copyable type that another assembly can read, or a member that returns a reference to one, given to unchecked code; an
  interface or an abstract class of checked code that unchecked code implements; and a delegate or a function pointer that unchecked code
  made. For a type that a project or a library declares, the reference of the unchecked assembly to the one that declares it is reported
  ([SCA1025](../diagnostics/sca1025.md)); for the built-in types, which every assembly can name, and for a struct of unchecked code that
  only a list line makes non-copyable, nothing is. See [What Stays Out of Reach](unchecked-code.md#what-stays-out-of-reach).
- **Two value tasks made from one source and token.** The contract of <xref:System.Threading.Tasks.Sources.IValueTaskSource`1> forbids it. The read of the result inside the
  source is reported and suppressed there: see [What a listed member hands out](listed-members.md).
- **A handle rebuilt from its value.** `GCHandle.FromIntPtr(GCHandle.ToIntPtr(handle))`, a cast of an <xref:System.IntPtr> to a <xref:System.Runtime.InteropServices.GCHandle>, and the same
  for the generic handle types make a second owner of one handle. The analyzer cannot follow an <xref:System.IntPtr>.
- **A struct with an explicit layout** that lays another field over a field of a non-copyable type with `[FieldOffset]`.
- **Interceptors.** A method with `[InterceptsLocation]` replaces the method that a call binds to after the analyzer has looked at the
  call. Every rule that goes by the method that is called (a `[NotNullRef]` parameter, <xref:Singulink.CodeAnalysis.NonCopyable.Move*?displayProperty=nameWithType>, `[Kept]`, the safe list)
  judges the method that is written at the call.
- **What the unsafe classes allow.** The members of <xref:System.Runtime.CompilerServices.Unsafe>, <xref:System.Runtime.InteropServices.MemoryMarshal>, <xref:System.Runtime.InteropServices.CollectionsMarshal>, <xref:System.Runtime.InteropServices.ImmutableCollectionsMarshal> and
  <xref:System.Runtime.InteropServices.Marshal> are on the safe list for one fact only: they make no copy. What is done with them is the author's to answer for, and the
  analyzer guarantees nothing about code that uses them to do what the language otherwise rules out:
  - **Writing through a read-only reference** that `Unsafe.AsRef(in ...)` or a member of <xref:System.Runtime.InteropServices.MemoryMarshal> made writable. Several rules take
    `readonly` at its word: a `readonly` member needs no defensive copy and may run on a temporary one, and a copy of a `readonly struct`
    never differs from the original. A guard whose `readonly` `Dispose` writes through `Unsafe.AsRef(in this)` breaks that for its
    callers, in code that is itself clean:

    ```cs
    public readonly void Dispose() => Unsafe.AsRef(in this)._lock = null;   // not reported: AsRef makes no copy

    LockGuard? maybe = myLock.TryEnter();
    maybe?.Dispose();                        // not reported: a readonly member may run on the copy that '?.' takes out,
                                             // so the guard in 'maybe' is still held and can be released again
    ```

    The same goes for an `async` member of a `readonly struct` that reaches a field that way: it runs on the copy in its state machine.
    And for a temporary that the compiler makes for an `in` argument and that is written that way: the write is lost with the temporary.
  - **A null reference** from <xref:System.Runtime.CompilerServices.Unsafe.NullRef*?displayProperty=nameWithType> that is given to code which does not expect one. Two rules do count a read or a
    write through a reference as able to fail, unless the reference is
    [known to refer to something](../guides/null-references.md): between the read and the assignment of a
    `[MovedFrom]` parameter, and after a [commit](last-use.md). Nothing else follows a null reference into the code
    that receives it.
- **Live views.** A span from `CollectionsMarshal.AsSpan(list)` that outlives a `list.Add`, a reference from
  <xref:System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrAddDefault*?displayProperty=nameWithType> or `GetValueRefOrNullRef` that outlives a change of the dictionary, and a memory from
  <xref:System.Runtime.InteropServices.ImmutableCollectionsMarshal.AsMemory*?displayProperty=nameWithType> that outlives a change of the builder.
- **What the language rules out and the runtime does not:** for [the last use](last-use.md), a method that reads its `out`
  parameter before it writes it, which only code that is not C# can do, and a method that keeps a pointer to something that it was passed
  by reference; and an instance method that is called with a null `this`, which reflection and IL can do and C# cannot: a field of
  `this` is taken to be reachable after a [commit](last-use.md).
- **A list entry that is wrong.**
- **A compiler newer than the ones tested.** The analyzers are tested with the compilers of the .NET 10 and .NET 11 SDKs. With a newer SDK,
  new syntax that the analyzers do not know is reported as a use that is not known to be safe. "Not known" covers an operation of a kind
  that the analyzers have no judgment for, where it is used by value, and a pattern of a kind that they have no judgment for, where it
  looks into a value of a non-copyable type. A copy that a newer compiler makes inside syntax that they already know is covered once that
  SDK has been tested.
- **Reflection, `dynamic` and pointers.** A pointer is copyable; what it points to is reported where it is used by value. That includes what the code that receives an expression tree does with it: a tree is checked like
  the lambda it is written as, and compiling or interpreting it uses a captured value in place, but a consumer that evaluates a part of the
  tree by reflection gets a boxed copy. `dynamic` reaches the result of any erasable container, a completion source and a task included:
  an erasable type may be converted to `object`, an `object` is a `dynamic` for the asking, and `d.Task.Result` on it is checked by nobody.

</div>
