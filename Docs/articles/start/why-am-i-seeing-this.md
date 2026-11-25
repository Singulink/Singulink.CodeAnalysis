<div class="article">

# Why Am I Seeing This?

Your build shows `SCA` warnings, and you never added this analyzer. This page explains where it came from, what it checks in your project and what to do about the warnings.

## Where the Analyzer Came From

A library whose public API has structs that must not be copied, such as lock guards or handles, can bring the analyzer with it. The projects that use the library then get a warning when they copy those structs, because such a copy can release a lock twice or free a handle twice.

The analyzer reaches projects further down the chain as well. If you reference a package that depends on such a library, you get the analyzer too. See [Sharing Your Library](../guides/libraries.md) for how a library passes it on.

## What They Check in Your Project

The analyzer checks the types that the library marked. Even with nothing marked in your own code, you get a warning where your code copies one of the library's structs, or a collection or a task that holds one:

```cs
// LockGuard comes from the library, and _guards is a List<LockGuard> field
_guards.Add(myLock.Enter());                 // OK: a new guard moves into the list

LockGuard first = _guards[0];                // SCA1030: a copy, and the list still holds the guard
```

In a project that gets the analyzer only through a library, the analyzer does not check the [built-in non-copyable types](../guides/built-in-types.md), such as <xref:System.Runtime.InteropServices.GCHandle>, which it checks in a project that references the package itself.

#### Warnings Where Nothing Is Copied

Four IDs are about code that can copy such a struct without a visible copy. So they can appear even in a project that copies nothing:

| ID | What it warns about | What to do |
|---|---|---|
| [SCA1007](../diagnostics/sca1007.md) | Any use of <xref:System.Runtime.CompilerServices.RuntimeHelpers.GetObjectValue*?displayProperty=nameWithType>, which clones a boxed struct field by field | Use the object as it is |
| [SCA1008](../diagnostics/sca1008.md) | <xref:System.Object.MemberwiseClone> in a class that another assembly can derive from, where a derived class could hold a non-copyable value | Seal the class, copy the fields yourself, or suppress the warning and either document the rule for derived classes or ship a `clones` line for the class |
| [SCA1009](../diagnostics/sca1009.md) | A field that holds a non-copyable value in a class that derives from a class that clones itself, such as <xref:System.Text.Encoding>, and a non-copyable type bound to a generic class that derives from one | Hold the value in an object of a class that your class refers to |
| [SCA1025](../diagnostics/sca1025.md) | A referenced assembly that uses the library's structs and was not built with the analyzer. In a project that got the analyzer only through a library, this is a message and not a warning | Build that assembly with the analyzer, or vouch for it with a `trusted` line in a symbol list file (see [Symbol List](../guides/symbol-list.md)) |

## What to Do About a Warning

Every warning links to an article on this site, with an example and the ways to fix it. Most are fixed in one of a few ways: see [Fixing Common Warnings](fixing-common-warnings.md).

#### Correct Code

If the code is right, suppress that one warning where it stands, with a comment that says why it is right:

```cs
try
{
    await _channel.Writer.WriteAsync(lease);
}
catch
{
#pragma warning disable SCA1016 // _channel can only throw before it takes the lease
    lease.Dispose();
#pragma warning restore SCA1016
    throw;
}
```

[Correct Code That Still Gets a Warning](../guides/correct-code-with-warnings.md) shows the usual cases.

> [!NOTE]
> **In a project that turns on the updated memory safety rules of C#,** a report about a type that is unsafe to copy is an error, and only code in an unsafe context or a declaration marked `safe` or `unsafe` can suppress one. A report about any other non-copyable type is the same warning as under the current rules. See [Updated Memory Safety Rules](../rules/updated-memory-safety-rules.md).

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
