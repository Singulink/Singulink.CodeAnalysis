<div class="article">

# Built-in Non-copyable Types

The built-in non-copyable types are the structs of the framework that the analyzer knows as non-copyable, such as <xref:System.Runtime.InteropServices.GCHandle> and <xref:System.Threading.SpinLock>. This page lists them, says which of them are unsafe to copy, when they are checked, and how to change that for all of them or for one.

### At a Glance

| Project | Built-in non-copyable types |
|---|---|
| References `Singulink.CodeAnalysis` or `Singulink.CodeAnalysis.Analyzers` itself | On |
| Only gets the analyzer through a library | Off |
| Sets `NonCopyableIncludeBuiltInTypes` | As it says |

## Which Structs

Eleven structs, five of them unsafe to copy: a copy of one can free or unpin memory that is still in use. A copy of any of the other six breaks the logic of the program.

| Struct | Unsafe to copy | What a copy does |
|---|---|---|
| <xref:System.Runtime.InteropServices.GCHandle>, <xref:System.Runtime.InteropServices.GCHandle`1>, <xref:System.Runtime.InteropServices.PinnedGCHandle`1>, <xref:System.Runtime.InteropServices.WeakGCHandle`1> | Yes | Its `Free` or `Dispose` releases a slot of the runtime's handle table that may belong to another object by then, or unpins memory that a pointer still points into |
| <xref:System.Buffers.MemoryHandle> | Yes | Its `Dispose` frees the handle and unpins the memory a second time |
| <xref:System.Threading.SpinLock> | No | Is a second lock, so two threads can each hold one |
| <xref:System.Threading.Lock.Scope> | No | Exits the lock twice |
| <xref:System.Threading.AsyncFlowControl> | No | Restores the flow of the execution context twice |
| <xref:System.Threading.Tasks.Sources.ManualResetValueTaskSourceCore`1> | No | Holds the result and the continuation in two places |
| <xref:System.Windows.Threading.DispatcherProcessingDisabled>, of WPF | No | Lowers the count of disabled processing twice |
| <xref:System.Runtime.CompilerServices.DefaultInterpolatedStringHandler> | No | Returns its pooled array twice, so two later renters can share it. That shares data, but no copy reaches memory that is gone |

Under the current memory safety rules of C# the column makes no difference: every copy gets a warning. Under the [updated memory safety rules](../rules/updated-memory-safety-rules.md) a copy of one of the first five is an error, and a copy of one of the other six is the ordinary warning.

## Why They Are Off Through a Library

These are types that a project may copy on purpose. A library that marks its guards brings the analyzer to every project that uses it, to keep those projects from copying the guards. That should not also give them warnings about their own `GCHandle` code. So the analyzer checks these structs only in a project that references one of the two packages itself, with a `PackageReference`.

With central package management a `PackageReference` with no version counts, and so does a `GlobalPackageReference`. A version that is only pinned for a transitive reference does not.

## Changing It

A project that sets the property gets the value that it sets:

```xml
<PropertyGroup>
  <!-- false: the built-in non-copyable types are off here, unless a noncopyable line names one -->
  <NonCopyableIncludeBuiltInTypes>false</NonCopyableIncludeBuiltInTypes>
</PropertyGroup>
```

Only the value `true` turns them on. For one struct, use a line of a [symbol list file](symbol-list.md) instead:

```text
# Get one back where they are off
T:System.Threading.SpinLock;noncopyable

# Drop one where they are on
T:System.Runtime.InteropServices.GCHandle;copyable
```

A `copyable` line takes a struct out completely, also one that is unsafe to copy. A line cannot make one of the five plain non-copyable.

The property does not touch structs marked `[NonCopyable]`, the `noncopyable` and `unsafetocopy` lines of any symbol list file, generic structs over a non-copyable type, or any other entry of the [built-in framework symbol list](../rules/framework-symbol-list.md). The exact rules: [Built-in Non-copyable Types](../rules/msbuild-properties.md#built-in-non-copyable-types).

</div>
