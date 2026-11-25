<div class="article">

# MSBuild Properties

Three MSBuild properties change what the analyzer takes for granted or how it reports. This page gives each one, with what it changes and how to set it.

### At a Glance

| Property | Default | Effect |
|---|---|---|
| `NonCopyableIncludeBuiltInTypes` | `true` in a project that references one of the two packages itself, `false` in one that only gets the analyzers through a library | Turns the built-in non-copyable types on or off |
| `NonCopyableAllowImplicitMoves` | `true` | `false` turns off the move of a variable at its [last use](last-use.md) |
| `NonCopyableErrorsAsWarnings` | `false` | `true` makes the analyzer report as under the current memory safety rules, in a project that turns on the [updated rules](updated-memory-safety-rules.md) |

All three are meant to be set in the project file or in a `Directory.Build.props`.

## Built-in Non-copyable Types

The built-in non-copyable types are framework structs that a project may copy on purpose. **The analyzer checks them in a project that references one of the two packages itself, with a `PackageReference`, but not in a project that gets the analyzers only through a library.** A library that marks its guards brings the analyzers to every project that uses it, to keep those projects from copying the guards. That does not also give them warnings about their own <xref:System.Runtime.InteropServices.GCHandle> code.

Where the project has not set the property, the package decides. A project that sets the property gets the value that it sets:

```xml
<PropertyGroup>
  <!-- false: the built-in types are off here, unless a noncopyable line names one -->
  <NonCopyableIncludeBuiltInTypes>false</NonCopyableIncludeBuiltInTypes>
</PropertyGroup>
```

- **Only the value `true` turns them on.** A build with no value, such as one in which the package's build files did not run, has them off.
- **With central package management,** a `PackageReference` with no version counts, and so does a `GlobalPackageReference`, which becomes one. A version that is only pinned for a transitive reference does not.
- **Not affected:** structs marked `[NonCopyable]`, the `noncopyable` entries of any [symbol list file](symbol-list-format.md) (the project's or a library's), generic structs over a non-copyable type, and every other entry of the [built-in framework symbol list](framework-symbol-list.md).
- **To get some of them back where they are off,** name them in a symbol list file of the project (`T:System.Threading.SpinLock;noncopyable`). **To drop one of them where they are on,** take it out with a `copyable` line (`T:System.Runtime.InteropServices.GCHandle;copyable`).

## Turning Off the Move on the Last Use

A local or a by-value parameter is a new value [where it is used for the last time](last-use.md). A project that wants every hand-over of a value written out turns that off:

```xml
<PropertyGroup>
  <NonCopyableAllowImplicitMoves>false</NonCopyableAllowImplicitMoves>
</PropertyGroup>
```

Only the value `false` turns it off. Then no read of a variable is a last use. Every by-value read of a local or a by-value parameter is a read of a stored value, as a read of a field is, and only [NonCopyable.Move](moved-from.md#move-helper) hands a value over.

```cs
LockGuard guard = myLock.Enter();
_held = guard;                           // SCA1001 with the property false
_held = NonCopyable.Move(ref guard);     // OK either way
```

| With the property `false` | Turned off? |
|---|---|
| Everything under [Last Use of a Variable](last-use.md) | Yes |
| The [commit at the end of a try block](commit.md), a form of the last use | Yes |
| The parameter of a primary constructor that one initializer uses, also a form of the last use | Yes |
| A local that holds a container of the method's own: `await pending` and `list[0]` then read from a stored container wherever they stand | Yes |
| The note in a message about a `catch`, a `finally` or a filter that still uses the variable | Yes: no message has it |
| Returning a by-value local or parameter, which never depended on the last use | No |
| The check of the paths of a `[MovedFrom]` parameter: a method that can leave with the value still in the parameter gets a warning | No |

## Errors as Warnings

Under the [updated memory safety rules](updated-memory-safety-rules.md) of C#, the analyzer reports most rules as errors. A project that has those rules on, and wants the warnings that it gets under the current rules, sets this property:

```xml
<PropertyGroup>
  <NonCopyableErrorsAsWarnings>true</NonCopyableErrorsAsWarnings>
</PropertyGroup>
```

Only the value `true` turns it on. The analyzer then reports exactly as under the current rules: every report is an ordinary warning, at a declaration as well, every kind of suppression and severity configuration works, and an unsafe context plays no part. The property has no other effect, and under the current rules it changes nothing.

## Setting a Property in a Configuration File

A global analyzer configuration file can set the properties as well, as `build_property.NonCopyableAllowImplicitMoves = false`. But the package always passes its properties to the compiler, empty where the project does not set them, so the file has to take precedence over that:

- A file that is named `.globalconfig` does.
- A global configuration file of any other name needs `global_level = 1` or more. Without it the compiler warns that two files set the key (`MultipleGlobalAnalyzerKeys`) and drops it, and the property has no effect.

</div>
