<div class="article">

# What Is Not Covered

What the analyzer cannot see, and what it takes on trust.

## The Gaps

- **<xref:System.Object.MemberwiseClone> called in a base class** that has no field of a non-copyable type, on an object of a derived class that has one.
- **<xref:System.Runtime.CompilerServices.RuntimeHelpers.GetObjectValue*?displayProperty=nameWithType>,** which clones a boxed struct.

  A project that wants these two ruled out can ban the calls with the
  [Microsoft.CodeAnalysis.BannedApiAnalyzers](https://www.nuget.org/packages/Microsoft.CodeAnalysis.BannedApiAnalyzers) package, which
  reports every use of a symbol that its `BannedSymbols.txt` file names (`RS0030`):

  ```
  M:System.Object.MemberwiseClone;Copies the fields of an object, non-copyable ones included, with no diagnostic in a base class
  M:System.Runtime.CompilerServices.RuntimeHelpers.GetObjectValue(System.Object);Clones a boxed struct
  ```

  That bans the calls in the code of the project itself, a <xref:System.Object.MemberwiseClone> inside one of its classes included. A base class in another
  assembly is not seen.
- **Two value tasks made from one source and token.** The contract of <xref:System.Threading.Tasks.Sources.IValueTaskSource`1> forbids it. The read of the result inside the
  source is reported and suppressed there: see [What a listed member hands out](listed-members.md).
- **A handle rebuilt from its value.** `GCHandle.FromIntPtr(GCHandle.ToIntPtr(handle))`, a cast of an <xref:System.IntPtr> to a <xref:System.Runtime.InteropServices.GCHandle>, and the same
  for the generic handle types make a second owner of one handle. The analyzer cannot follow an <xref:System.IntPtr>.
- **A struct with an explicit layout** that lays another field over a field of a non-copyable type with `[FieldOffset]`.
- **Code that keeps a container without saying so:** a method of an assembly that was built without the analyzer and has no `[Kept]`. A
  caller takes the container to be still its own. What the other holder reads from it is a read from a stored container all the same.
- **A method that stores `this`** into a field or a collection keeps the object that it is called on, and no mark says so. No copy follows
  from that where the members of the object are checked code, as those of a marked generic are: what they hand out is a value that they
  moved out, whoever else holds the object. A class that derives from a listed container is never a new container for the members that
  it inherits: see [What a listed member hands out](listed-members.md). What remains is a type whose members a list file
  of the project vouches for, a class of another assembly that derives from a listed container among them: an entry says that the
  member hands out what the object holds as new as the object is, which is wrong for a type that stores itself.
- **An instance method that is called with a null `this`,** which reflection and IL can do and C# cannot. A field of `this` is taken to be
  reachable after a [commit](last-use.md).
- **An entry for a member of an interface is a statement about every implementation of it.** An implementation in checked code cannot
  break it without a finding of its own, and an unlisted generic one is reported where it is created. An object from an assembly that
  was not built with the analyzer is trusted to keep to it.
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
- **What the language rules out and the runtime does not,** for [the last use](last-use.md): a method that reads its `out`
  parameter before it writes it, which only code that is not C# can do, and a method that keeps a pointer to something that it was passed
  by reference.
- **A list entry that is wrong,** and a library with a marked type parameter whose own build did not run the analyzer.
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
