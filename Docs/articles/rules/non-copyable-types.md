<div class="article">

# Non-copyable Types

The rules treat two kinds of type specially: non-copyable types, whose values must not be copied, and containers, which hold such values. This page says which types are which. What follows for the declarations of a type is in [Fields, Interfaces and Async Members](declarations.md).

### At a Glance

| Type | Kind | Example |
|---|---|---|
| A struct marked `[NonCopyable]` | Non-copyable | `LockGuard` |
| A type parameter marked `[NonCopyable]`, inside the generic that declares it | Non-copyable | `T` in `Pool<[NonCopyable] T>` |
| A generic struct with a non-copyable type argument, at any depth | Non-copyable | `ValueTask<LockGuard>`, `LockGuard?`, `(LockGuard, bool)`, `KeyValuePair<int, LockGuard>` |
| A struct that a [symbol list entry](symbol-list-format.md) names as non-copyable, the ten of the [built-in framework symbol list](framework-symbol-list.md) included | Non-copyable | <xref:System.Threading.SpinLock> |
| Any other type that mentions a non-copyable type | Container | `LockGuard[]`, `List<LockGuard>`, `Task<LockGuard>`, `Span<LockGuard>` |
| A type that has a container among its base types or interfaces | Container of the same types | `class Guards : List<LockGuard>` |

## What Is Non-copyable

- **A struct marked `[NonCopyable]`.** See [How the Attributes Are Recognized](attribute-recognition.md).
- **A type parameter marked `[NonCopyable]`,** inside the generic that declares it. See [Marked Type Parameters](marked-type-parameters.md).
- **A generic struct with a non-copyable type argument,** at any depth, because copying it copies what it stores: `ValueTask<LockGuard>`, `LockGuard?`, `(LockGuard, bool)`, `KeyValuePair<int, LockGuard>`.
- **A struct that a [symbol list entry](symbol-list-format.md) names as non-copyable.**

#### Built-in Non-copyable Types

The [built-in framework symbol list](framework-symbol-list.md) names ten framework structs, such as <xref:System.Threading.SpinLock> and <xref:System.Threading.Lock.Scope>. The analyzer checks them in a project that references one of the packages directly, and a project can [leave them out](msbuild-properties.md#built-in-non-copyable-types). All ten are in [Built-in Types](../guides/built-in-types.md).

## Containers

A **container** is any other type that mentions a non-copyable type: `LockGuard[]`, `List<LockGuard>`, `Task<LockGuard>`, `Span<LockGuard>`. Copying a container copies no element, so that is fine. What the rules control is how it is used: see [What a Member on the Symbol List Hands Out](listed-members.md), [Returning a Container](returning-containers.md) and [Keeping a Container That Is Passed In](keeping-containers.md).

A type that has a container among its base types or its interfaces is a container of the same non-copyable types: `class Guards : List<LockGuard>`, `interface IGuards : IReadOnlyList<LockGuard>`.

</div>
