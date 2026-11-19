<div class="article">

# Built-in Types

The analyzer knows ten framework structs as non-copyable, such as <xref:System.Runtime.InteropServices.GCHandle> and <xref:System.Threading.SpinLock>. This page says when they are checked, and how to change that for all ten or for one.

### At a Glance

| Project | The ten structs |
|---|---|
| References `Singulink.CodeAnalysis` or `Singulink.CodeAnalysis.Analyzers` itself | On |
| Only gets the analyzers through a library | Off |
| Sets `NonCopyableIncludeBuiltInTypes` | As it says |

## The Ten Structs

- <xref:System.Threading.SpinLock>
- <xref:System.Runtime.InteropServices.GCHandle>, <xref:System.Runtime.InteropServices.GCHandle`1>, <xref:System.Runtime.InteropServices.PinnedGCHandle`1>, <xref:System.Runtime.InteropServices.WeakGCHandle`1>
- <xref:System.Threading.Lock.Scope>
- <xref:System.Buffers.MemoryHandle>
- <xref:System.Runtime.CompilerServices.DefaultInterpolatedStringHandler>
- <xref:System.Threading.AsyncFlowControl>
- <xref:System.Threading.Tasks.Sources.ManualResetValueTaskSourceCore`1>

## Why They Are Off Through a Library

These are types that a project may copy on purpose. A library that marks its guards brings the analyzers to every project that uses it, to keep those projects from copying the guards. That should not also give them warnings about their own `GCHandle` code. So the analyzer checks the ten structs only in a project that references one of the two packages itself, with a `PackageReference`.

With central package management a `PackageReference` with no version counts, and so does a `GlobalPackageReference`. A version that is only pinned for a transitive reference does not.

## Changing It

A project that sets the property gets the value that it sets:

```xml
<PropertyGroup>
  <!-- false: the ten framework structs are off here, unless a noncopyable line names one -->
  <NonCopyableIncludeBuiltInTypes>false</NonCopyableIncludeBuiltInTypes>
</PropertyGroup>
```

Only the value `true` turns them on. For one struct, use a line of a [symbol list file](symbol-list.md) instead:

```text
# Get one back where the ten are off
T:System.Threading.SpinLock;noncopyable

# Drop one where the ten are on
T:System.Runtime.InteropServices.GCHandle;copyable
```

The property does not touch structs marked `[NonCopyable]`, the `noncopyable` lines of any symbol list file, generic structs over a non-copyable type, or any other entry of the [built-in framework symbol list](../rules/framework-symbol-list.md). The exact rules: [Built-in Non-copyable Types](../rules/msbuild-properties.md#built-in-non-copyable-types).

</div>
