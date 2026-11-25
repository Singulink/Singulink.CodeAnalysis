<div class="article">

# Updated Memory Safety Rules

C# has two sets of rules for unsafe code: the current rules, which a project has by default, and the updated memory safety rules, a preview feature of the compiler. Under the updated rules the analyzer reports most of its rules as errors, and code can take a report on itself only in an unsafe context. This page explains what changes, what an unsafe context is there, and how to cover one report.

> [!NOTE]
> **This page is only for a project that turns the updated rules on.** Under the current rules every report of the analyzer is an ordinary warning, which `#pragma`, `NoWarn`, `[SuppressMessage]` and `.editorconfig` all suppress, and unsafe code plays no part. See [Turning Rules Down](../guides/turning-rules-down.md).

### At a Glance

- **A report outside an unsafe context is an error** that nothing suppresses.
- **In code in an unsafe context, the same report is a warning.** `#pragma` suppresses it, and so do `NoWarn` and the severity in `.editorconfig` or `.globalconfig`. `[SuppressMessage]` does not.
- **A report at a declaration is always an error,** such as one at a field or at the return type of an override.
- **Some IDs stay plain warnings:** SCA1042, SCA1045 and SCA1047. SCA1025 is a warning or a message, as under the current rules.
- **`NonCopyableErrorsAsWarnings`** makes the analyzer report as under the current rules.

## Turning the Updated Rules On

The compiler decides which rules a compilation uses, and the analyzer follows it. With a compiler that has the updated rules, such as the one of the .NET 11 SDK, a project turns them on with the preview language version and a feature flag:

```xml
<PropertyGroup>
  <LangVersion>preview</LangVersion>
  <Features>$(Features);updated-memory-safety-rules</Features>
  <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
</PropertyGroup>
```

Every `unsafe` block and `unsafe(...)` expression needs `AllowUnsafeBlocks`, under both sets of rules. A compiler that does not have the updated rules keeps the current ones, and the analyzer then reports as under the current rules.

## Errors and Warnings

Each ID is in one of three groups:

| IDs | Outside an unsafe context | In code in an unsafe context |
|---|---|---|
| SCA1001 to SCA1016, SCA1020 to SCA1024, SCA1030 to SCA1033, SCA1040, SCA1041, SCA1043, SCA1044, SCA1046, SCA1048 | Error | Warning |
| SCA1042, SCA1045, SCA1047 | Warning | Warning |
| SCA1025 | Warning or message: see [SCA1025](../diagnostics/sca1025.md#warning-or-message) | The same |

You can always answer a report of the first group in your own code, so it is an error. The others are plain warnings for a reason of their own:

- **SCA1042** is about a line of a symbol list file, which has no unsafe context.
- **SCA1045** is about an attribute that is not the analyzer's, which a project may keep on purpose for another tool.
- **SCA1047** asks you to check a method and then suppress the warning, with `#pragma` or `[SuppressMessage]`.
- **SCA1025** is about an assembly that someone else built, and has no place in the source.

The same copy is an error outside an unsafe context, and a warning that you can suppress inside one:

```cs
void Keep()
{
    LockGuard copy = _guard;                    // SCA1001: an error
}

void KeepOnPurpose()
{
#pragma warning disable SCA1001 // why this copy is right
    LockGuard copy = unsafe(_guard);            // OK: a warning in an unsafe context, suppressed here
#pragma warning restore SCA1001
}
```

## Code and Declarations

The place of a report decides whether an unsafe context can make it a warning, not its ID:

| Place of the report | In an unsafe context |
|---|---|
| **Code:** a body, an initializer of a field or a property, a constructor initializer, the arguments of a primary constructor's base type, a lambda, a local function, top-level statements | Warning |
| **Declaration:** a field, a property, a parameter or the return type of a member, a base list, a type parameter | Error |

A declaration has no unsafe context, so a report there is an error whatever surrounds it. Fix the declaration, or set `NonCopyableErrorsAsWarnings` (see below):

```cs
struct Holding
{
    LockGuard _guard;                           // SCA1010: an error, whatever surrounds it
}
```

## Unsafe Context

The analyzer uses the unsafe context of the C# language, which the updated rules make narrow:

| Code | Unsafe context? |
|---|---|
| An `unsafe { }` block | Yes |
| An `unsafe(...)` expression | Yes |
| The `: base(...)` or `: this(...)` initializer of an `unsafe` constructor | Yes |
| A lambda or a local function | As the code around it |
| A member, a local function or a type with the `unsafe` modifier | No: the modifier makes nothing unsafe |
| The body of an iterator (a method with `yield`) | No, even inside an `unsafe` block. An `unsafe` block in the iterator is unsafe again |

An `await` may stand in an unsafe context, so `unsafe(await task)` is fine.

## Covering One Report

Put the expression that the report is on in `unsafe(...)`, or the statement in an `unsafe` block, and suppress the warning there with a comment that says why the code is right. Keep the unsafe context to the code that needs it.

Each report in code lands on an expression that `unsafe(...)` can hold:

| Report | Lands on | Covered by |
|---|---|---|
| A copy into a local that is declared | The value | `var copy = unsafe(_guard);` |
| What a `foreach` copies for each element | The collection | `foreach (var guard in unsafe(_array))` |
| A deconstruction | The deconstructed value | `var (read, write) = unsafe(_lease);` |
| What a spread copies | Its operand | `LockGuard[] all = [.. unsafe(_array)];` |
| The `Dispose` that a `using` calls | The value of the resource | `using var pool = unsafe(CreatePool());` |
| A read out of a task | The `await` | `LockGuard lost = unsafe(await loser);` |
| A use after a hand-off (SCA1016) | The use | `unsafe(lease).Dispose();` or `unsafe { lease.Dispose(); }` |

An `unsafe` block around a declaration would end the scope of the variables that it declares, so for a declaration, cover the value.

#### Covering a Statement

A statement cannot stand in `unsafe(...)`: `unsafe(lease.Dispose());` does not compile. Two forms cover a statement, and neither copies the value:

| Form | Covers |
|---|---|
| `unsafe(lease).Dispose();` | The use of `lease`, where the report lands |
| `unsafe { lease.Dispose(); }` | Every statement in the block |

A use after a hand-off is reported at the use, so an unsafe context around the hand-off changes nothing. Cover the use:

```cs
Lease lease = Pools.Rent();

try
{
    await _channel.Writer.WriteAsync(lease);
}
catch
{
#pragma warning disable SCA1016 // _channel can only throw before it takes the lease
    unsafe(lease).Dispose();
#pragma warning restore SCA1016
    throw;
}
```

Why this `catch` gets a report, and when the code is right: [Correct Code That Still Gets a Warning](../guides/correct-code-with-warnings.md#handing-a-value-over-and-keeping-it-on-failure).

## Suppressing the Warning

| Suppression | Effect on the warning in an unsafe context | Effect on the error |
|---|---|---|
| `#pragma warning disable` | Suppresses it on the lines that follow | None |
| `NoWarn`, or the severity `none` in `.editorconfig` or `.globalconfig` | Suppresses it in every unsafe context of the project | None |
| `[SuppressMessage]` | None | None |

Prefer `#pragma` with its reason: a suppression for the whole project hides every later copy in unsafe code as well.

## NonCopyableErrorsAsWarnings

A project that has the updated rules on for other reasons, and is not ready for the errors, sets this property:

```xml
<PropertyGroup>
  <NonCopyableErrorsAsWarnings>true</NonCopyableErrorsAsWarnings>
</PropertyGroup>
```

The analyzer then reports exactly as under the current rules: every report is an ordinary warning, at a declaration as well, and `#pragma`, `NoWarn`, `[SuppressMessage]` and `.editorconfig` all work. An unsafe context plays no part. The property has no other effect, and only the value `true` turns it on. See [MSBuild Properties](msbuild-properties.md#errors-as-warnings).

</div>
