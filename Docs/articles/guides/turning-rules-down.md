<div class="article">

# Turning Rules Down

Each kind of warning has an ID of its own, so that a project can turn one down without the others. This page shows the ways to turn things down, from the narrowest to the widest.

### At a Glance

| To turn down | Write | Where |
|---|---|---|
| One warning on code that is correct | `#pragma warning disable` with the reason | At the warning |
| One type that the project copies on purpose | A `copyable` line | A [symbol list file](symbol-list.md) |
| One rule | `dotnet_diagnostic.SCAnnnn.severity = none` | `.editorconfig` |
| The move at the last use | `NonCopyableAllowImplicitMoves` set to `false` | The project file |

## One Warning

Suppress a single warning on correct code where it stands, with a comment that says why the code is right:

```cs
catch
{
#pragma warning disable SCA1016 // _channel can only throw before it takes the lease
    lease.Dispose();
#pragma warning restore SCA1016
    throw;
}
```

[Correct Code That Still Gets a Warning](correct-code-with-warnings.md) shows the shapes that need this.

## One Type

When the [built-in framework symbol list](../rules/framework-symbol-list.md) or a library makes a struct non-copyable and the project copies it on purpose, take the struct out with a `copyable` line in a [symbol list file](symbol-list.md) of the project:

```text
T:System.Runtime.InteropServices.GCHandle;copyable
```

The struct is then an ordinary struct for the project, and nothing that the analyzer guarantees holds for it. A `copyable` line for a struct that the project declares itself gets a warning (SCA1042): take its `[NonCopyable]` attribute off instead. See [Symbol List](symbol-list.md).

## One Rule

Every rule is a warning by default, and a project can raise or lower its severity. These pages say "warning" whatever the severity is set to. You turn a rule down like any other analyzer rule, in an `.editorconfig` file:

```ini
[*.cs]
# Containers reach object through logging and assertions
dotnet_diagnostic.SCA1023.severity = none
```

> [!WARNING]
> A rule that is turned down is off for the whole project, and you get no warning for any copy that it would have caught. Prefer a fix, or the suppression of one warning with its reason.

## Move at the Last Use

A local or a by-value parameter is a new value [where it is used for the last time](../rules/last-use.md). A project that wants every hand-off of a value written out turns that off in the project file:

```xml
<PropertyGroup>
  <NonCopyableAllowImplicitMoves>false</NonCopyableAllowImplicitMoves>
</PropertyGroup>
```

Then only `NonCopyable.Move` hands a value over. What else that turns off, and how to set it in a configuration file: [Turning Off the Move on the Last Use](../rules/msbuild-properties.md#turning-off-the-move-on-the-last-use).

The built-in non-copyable types have a property of their own: see [Built-in Non-copyable Types](built-in-types.md).

## Under the Updated Memory Safety Rules

A project that turns on the updated memory safety rules of C# gets every report about a non-copyable type as under the current rules, with one exception. A report about a type that is unsafe to copy, one whose copy can free or reach memory twice, is an error. Only code in an unsafe context and a declaration marked `safe` or `unsafe` can take such a report on itself:

| To turn down | Write |
|---|---|
| One report on code that is correct | `unsafe(...)` around the expression that is reported, such as `unsafe(lease).Dispose();`, or an `unsafe` block around the statement, and `#pragma warning disable` with the reason |
| One report at a declaration that is correct | `safe` on the declaration, in a project that allows unsafe code, and `#pragma warning disable` with the reason. See [Marking a Declaration](../rules/updated-memory-safety-rules.md#marking-a-declaration) |
| A report about a type that is not unsafe to copy, and a plain warning: SCA1042, SCA1045, SCA1047, SCA1049, and SCA1025 | As under the current rules |
| Every error of the project | `NonCopyableErrorsAsWarnings` set to `true`, which makes the analyzer report as under the current rules |

`.editorconfig`, `.globalconfig`, `NoWarn` and `[SuppressMessage]` have no effect on an error. In an unsafe context and at a marked declaration, all of them but `[SuppressMessage]` turn the warning off. See [Updated Memory Safety Rules](../rules/updated-memory-safety-rules.md#suppressing-the-warning).

</div>
