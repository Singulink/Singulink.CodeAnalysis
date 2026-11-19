<div class="article">

# Symbol List

The symbol list tells the analyzer what it cannot see for itself in code that you cannot mark: which members of a library's generics make no copy, which structs are non-copyable, which assemblies to trust. It is made of the [built-in framework symbol list](../rules/framework-symbol-list.md), which the analyzer ships for the framework, and the symbol list files of the project. This page shows where the files go and the lines that you will use most.

> [!NOTE]
> **Most projects never write a symbol list file.** The framework is covered by the framework symbol list: its collections, tasks, spans, channels and the rest work with no file. A member that would copy or compare the values, such as `ToArray`, is left off on purpose, and using one with a non-copyable value gets a real warning. Your own code says what it needs with attributes, and so does a library that was built with the analyzers. A file is only for code that you cannot mark, and in that case the warning's message gives the line to add.

You need a symbol list file when:

| Situation | Warning | The line, from the message |
|---|---|---|
| A generic of a library that is not marked is used with a non-copyable type, and the member makes no copy | [SCA1020](../diagnostics/sca1020.md) | ``M:ThirdParty.Bag`1.Add(`0)`` |
| A member of an assembly that was not built with the analyzers takes a non-copyable value by reference or in a container, and keeps nothing | [SCA1024](../diagnostics/sca1024.md) | `M:ThirdParty.Leases.Inspect(ThirdParty.Lease@);borrows` |
| Such an assembly hands out a container that nothing else holds | [SCA1030](../diagnostics/sca1030.md) | `M:ThirdParty.Leases.Create;new` |
| An assembly that uses your structs cannot be built with the analyzers, and you vouch for it | [SCA1025](../diagnostics/sca1025.md) | `A:Contoso.Plugins;trusted` |
| A struct that cannot be marked in its source must be non-copyable, or one that is non-copyable must not be | None | `T:ThirdParty.Handle;noncopyable`, `T:System.Runtime.InteropServices.GCHandle;copyable` |

### At a Glance

- **A file named `NonCopyableSymbols.txt`** in the folder of the project is read with no further setup.
- **One line per entry:** the documentation comment ID of a member or a type, with an optional marker after a semicolon.
- **The message of SCA1020** gives the line to add.
- **The analyzer ships the [built-in framework symbol list](../rules/framework-symbol-list.md) for the framework,** and all symbol list files are merged with it.

## Symbol List Files

A project reads its own symbol list files and those that come with the libraries it uses, treats them all alike, and merges them with the [built-in framework symbol list](../rules/framework-symbol-list.md). Where two files disagree, the line that claims less applies: a project's file can tighten a line that came with a library, but not loosen it.

A project adds files named `NonCopyableSymbols.txt` or `NonCopyableSymbols.<name>.txt`, whatever the case of the name. Such a file in the folder of the project is read with no further setup. A file in another folder is added as an additional file:

```xml
<ItemGroup>
  <AdditionalFiles Include="Lists\NonCopyableSymbols.ThirdParty.txt" />
</ItemGroup>
```

A file in the project's folder that the project also adds as an additional file is read only once. The second form of name is for a file that is named after what it describes, and it is the form for [a symbol list file that a library ships](libraries.md#shipping-a-symbol-list-file-with-a-library).

## Adding a Member of a Library

The analyzer assumes that an unmarked generic of a library makes copies. Using it with a guard gets a warning, and the message names the line to add if the member makes no copy:

```text
warning SCA1020: 'Bag<T>.Add(T)' may copy non-copyable type 'LockGuard': type parameter 'T' is not marked [NonCopyable] and the
member is not on the symbol list. Mark the type parameter, add 'M:ThirdParty.Bag`1.Add(`0)' to a NonCopyableSymbols.txt file if the
member makes no copy, or avoid the use.
```

Read the member first: an entry promises that the member makes no copy other than the value that it hands out, and the analyzer takes that promise on trust. A member like `ToArray` or `CopyTo`, which leaves two live instances, must not get an entry in a symbol list file.

```text
# NonCopyableSymbols.txt
M:ThirdParty.Bag`1.#ctor
M:ThirdParty.Bag`1.Add(`0)
M:ThirdParty.Bag`1.Take;new
```

Entries are exact per overload, with no wildcard. Your own generics mark their type parameters instead: see [Marking a Type Parameter](marking-a-type-parameter.md).

## Lines You Will Use Most

| Line | Says |
|---|---|
| ``M:ThirdParty.Bag`1.Add(`0)`` | The member makes no copy. What it returns is new if the object and the arguments were new, and a copy if they were stored |
| ``M:ThirdParty.Bag`1.Take;new`` | What the member hands out is always a new value, as for a member that takes it out |
| `M:ThirdParty.Native.GetAddress(ThirdParty.Handle);borrows` | The member reads a stored value during the call and keeps nothing of it |
| `T:ThirdParty.Handle;noncopyable` | The struct is non-copyable, though its source cannot be marked |
| `T:System.Runtime.InteropServices.GCHandle;copyable` | The struct is an ordinary struct for this project |
| `A:Contoso.Plugins;trusted` | The assembly was not built with the analyzer, and makes no copy of the values that it gets at |

The other markers (`drains`, `owned`, `for=`, `view`, `erasable`), what each entry may be put on, and what happens where two entries disagree: [Symbol List Format](../rules/symbol-list-format.md). What the analyzer ships for .NET's own libraries is on the [built-in framework symbol list](../rules/framework-symbol-list.md).

## Code Not Built With the Analyzer

A library that was not built with the analyzer is unchecked code. Three lines vouch for what the analyzer cannot check there, and each is needed only where a warning asks for it:

- **`borrows`** on a member that gets at a guard by `ref`, in a container, in a view or through a pointer. Without it the use gets a warning (SCA1024).
- **`new`** on a member whose container result is new. Without it the container is stored.
- **`trusted`** on an assembly that names your library's structs and cannot be built with the analyzer. Without it you get one warning for the reference (SCA1025).

See [Code Not Built With the Analyzer](../rules/unchecked-code.md).

## Built-in Non-copyable Types

The [built-in framework symbol list](../rules/framework-symbol-list.md) makes ten framework structs non-copyable, such as <xref:System.Runtime.InteropServices.GCHandle> and <xref:System.Threading.SpinLock>. The analyzer checks them in a project that references one of the two packages directly, but not in a project that gets the analyzers only through a library. See [Built-in Types](built-in-types.md).

</div>
