<div class="article">

# Why Am I Seeing This?

Your build shows `SCA` warnings, and you never added these analyzers. This page explains where they came from, what they check in your project and what to do about them.

## Where the Analyzers Came From

A library whose public API has structs that must not be copied, such as lock guards or handles, can bring the analyzers with it. The projects that use the library then get a warning when they copy those structs, because such a copy can release a lock twice or free a handle twice.

The analyzers reach projects further down the chain as well. If you reference a package that depends on such a library, you get the analyzers too. See [Sharing Your Library](../guides/libraries.md) for how a library passes them on.

## What They Check in Your Project

The analyzers check the types that the library marked. Even with nothing marked in your own code, you get a warning where your code copies one of the library's structs, or a collection or a task that holds one:

```cs
// LockGuard comes from the library, and _guards is a List<LockGuard> field
_guards.Add(myLock.Enter());                 // OK: a new guard moves into the list

LockGuard first = _guards[0];                // SCA1030: a copy, and the list still holds the guard
```

In a project that gets the analyzers only through a library, the analyzers do not check the ten framework structs that they otherwise treat as non-copyable, such as <xref:System.Runtime.InteropServices.GCHandle>: see [Built-in Types](../guides/built-in-types.md).

#### Warnings Where Nothing Is Copied

Three warnings are about code that can copy such a struct without a visible copy. So they can appear even in a project that copies nothing:

| ID | What it warns about | What to do |
|---|---|---|
| [SCA1007](../diagnostics/sca1007.md) | Any use of <xref:System.Runtime.CompilerServices.RuntimeHelpers.GetObjectValue*?displayProperty=nameWithType>, which clones a boxed struct field by field | Use the object as it is |
| [SCA1008](../diagnostics/sca1008.md) | <xref:System.Object.MemberwiseClone> in a class that another assembly can derive from, where a derived class could hold a non-copyable value | Seal the class, copy the fields yourself, or suppress the warning and document the rule for derived classes |
| [SCA1025](../diagnostics/sca1025.md) | A referenced assembly that uses the library's structs and was not built with the analyzers | Build that assembly with the analyzers, or vouch for it with a `trusted` line in a symbol list file (see [Symbol List](../guides/symbol-list.md)) |

## What to Do About a Warning

Every warning links to an article on this site, with an example and the ways to fix it. Most are fixed in one of a few ways: see [Fixing Common Warnings](fixing-common-warnings.md).

#### Correct Code

If the code is right, suppress that one warning where it stands, with a comment that says why it is right:

```cs
#pragma warning disable SCA1003 // TryWrite takes the lease only where it returns true
if (!writer.TryWrite(lease))
    lease.Dispose();
#pragma warning restore SCA1003
```

[Correct Code That Still Gets a Warning](../guides/correct-code-with-warnings.md) shows the usual cases.

#### Turning a Rule Down

Every rule is a warning, and you turn it down like any other analyzer rule, in an `.editorconfig` file:

```ini
[*.cs]
dotnet_diagnostic.SCA1023.severity = none
```

> [!WARNING]
> A rule that is turned down is off for the whole project, and you get no warning for any copy that it would have caught. Prefer the fix, or a suppression of one warning with its reason. See [Turning Rules Down](../guides/turning-rules-down.md).

## Next Steps

Here is where to go from here:

- [Fixing Common Warnings](fixing-common-warnings.md) - The handful of fixes that cover most warnings.
- [What the Analyzer Does](what-it-does.md) - The problem it solves, in a few minutes.
- [New and Stored Values](new-and-stored-values.md) - Why one line is a copy and the next is not.

</div>
