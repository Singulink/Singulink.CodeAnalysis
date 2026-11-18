<div class="article">

# Turning Rules Down

Each kind of finding has an ID of its own, so that a project can turn one down without the others, and two MSBuild properties change what the analyzer takes for granted.

## The Severity of a Rule

Every rule is a warning by default. A rule is turned down like any other analyzer rule, in an `.editorconfig` file:

```ini
[*.cs]
# Containers reach object through logging and assertions
dotnet_diagnostic.SCA1023.severity = none
```

A single finding that is correct code is suppressed where it stands, with a comment that says why it is right, as in [Correct Code That Stays Reported](common-fixes.md#correct-code-that-stays-reported).

## Taking One Type Out

A struct that the built-in list or a library makes non-copyable, and that the project copies on purpose, is taken out with a `copyable` line in the project's symbol list, such as `T:System.Runtime.InteropServices.GCHandle;copyable`. See [The Symbol List](symbol-list.md), and [The Built-in Non-copyable Types](symbol-list.md#the-built-in-non-copyable-types) for when the ten framework structs are on.

## Turning Off the Move on the Last Use

A local or a by-value parameter is a new value [where it is used for the last time](../concepts/last-use.md). A project that wants every
hand-over of a value written out turns that off with a second MSBuild property:

```xml
<PropertyGroup>
  <NonCopyableAllowImplicitMoves>false</NonCopyableAllowImplicitMoves>
</PropertyGroup>
```

Only the value `false` turns it off. Then no read of a variable is a last use: every by-value read of a local or a by-value parameter is a
read of a stored value, as a read of a field is, and [NonCopyable.Move](moving-a-value.md) is what hands a value over.

```cs
LockGuard guard = myLock.Enter();
_held = guard;                           // SCA1001 with the property false
_held = NonCopyable.Move(ref guard);     // OK either way
```

- **Turned off with it:** everything under [The last use of a variable](../concepts/last-use.md). That includes the commit at the end
  of a `try` block and the parameter of a primary constructor that one initializer uses, which are forms of the last use, and a local
  that holds a container of the method's own: `await pending` and `list[0]` then read from a stored container wherever they stand. No
  message has the note about a `catch`, a `finally` or a filter that still uses the variable.
- **Not turned off:** returning a by-value local or parameter, which never depended on the last use, and the check of the paths of a
  `[MovedFrom]` parameter (see [Moving a value](moving-a-value.md)): a method that can leave with what it read still in the parameter is
  reported either way.

## Setting a Property in a Configuration File

Both properties are meant to be set in the project file or in a `Directory.Build.props`. A global analyzer configuration file can set
them as well, as `build_property.NonCopyableAllowImplicitMoves = false`, but the package always passes both properties to the compiler,
empty where the project does not set them, so the file has to take precedence over that. A file that is named `.globalconfig` does. A
global configuration file of any other name needs `global_level = 1` or more: without it the compiler warns that two files set the key
(`MultipleGlobalAnalyzerKeys`) and drops it, and the property has no effect.

</div>
