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
#pragma warning disable SCA1003 // TryWrite takes the lease only where it returns true
if (!writer.TryWrite(lease))
    lease.Dispose();
#pragma warning restore SCA1003
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

A local or a by-value parameter is a new value [where it is used for the last time](../rules/last-use.md). A project that wants every handover of a value written out turns that off in the project file:

```xml
<PropertyGroup>
  <NonCopyableAllowImplicitMoves>false</NonCopyableAllowImplicitMoves>
</PropertyGroup>
```

Then only `NonCopyable.Move` hands a value over. What else that turns off, and how to set it in a configuration file: [Turning Off the Move on the Last Use](../rules/msbuild-properties.md#turning-off-the-move-on-the-last-use).

The ten built-in framework structs have a property of their own: see [Built-in Types](built-in-types.md).

</div>
