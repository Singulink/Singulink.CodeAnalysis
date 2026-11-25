<div class="article">

# Updated Memory Safety Rules

C# has two sets of rules for unsafe code: the current rules, which a project has by default, and the updated memory safety rules, a preview feature of the compiler. Under the updated rules the analyzer reports a copy of a type that is unsafe to copy as an error. Code can take such a report on itself only in an unsafe context, and a declaration only with the modifier `safe` or `unsafe`. Every other non-copyable type is reported the same way under both sets of rules.

These errors are the analyzer's rule, not the language's: C# does not treat a copy of a struct as unsafe. The analyzer does, for a type whose author says that a copy of it breaks memory safety, and it uses the unsafe context of the updated rules as the place where code answers for one.

> [!NOTE]
> **This page is only for a project that turns the updated rules on.** Under the current rules every report of the analyzer is an ordinary warning, which `#pragma`, `NoWarn`, `[SuppressMessage]` and `.editorconfig` all suppress, and unsafe code plays no part. See [Turning Rules Down](../guides/turning-rules-down.md).

### At a Glance

- **Most non-copyable types are reported as under the current rules,** with an ordinary warning that every suppression reaches. A lock guard is such a type: a copy of it breaks the logic of the program, not memory safety.
- **A report about a type that is unsafe to copy is an error** that nothing suppresses. The author of a struct says that it is unsafe to copy with `[NonCopyable(UnsafeToCopy = true)]`.
- **In code in an unsafe context, that error is a warning.** `#pragma` suppresses it, and so do `NoWarn` and the severity in `.editorconfig` or `.globalconfig`. `[SuppressMessage]` does not.
- **At a declaration, mark the declaration `safe` or `unsafe`** to make the error the same warning. `safe` counts only in a project that allows unsafe code.
- **Some IDs stay plain warnings:** SCA1042, SCA1045, SCA1047 and SCA1049. SCA1025 is a warning or a message, as under the current rules.
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

## Types That Are Unsafe to Copy

A copy of a non-copyable value breaks the logic of the program, as a lock that is released twice does, or it breaks memory safety, as a handle that is freed twice does. Only the author of a type knows which, so the author says so on the type. A type is unsafe to copy where:

| Where | Example |
|---|---|
| Its attribute says so | `[NonCopyable(UnsafeToCopy = true)] struct NativeBuffer` |
| A line of a symbol list file says so | `T:ThirdParty.PinnedBlock;unsafetocopy`: see [Symbol List Format](symbol-list-format.md#unsafetocopy) |
| It is one of five built-in non-copyable types | The four GC handles and `MemoryHandle`: see [Built-in Non-copyable Types](../guides/built-in-types.md) |
| It is a nullable, a tuple or a generic struct over such a type | `NativeBuffer?`, `(NativeBuffer, int)`, or `Pair<NativeBuffer>` for a struct `Pair<T>` |
| It is a type parameter marked `[NonCopyable]` | It can stand for any non-copyable type, unless its mark says `UnsafeToCopy = false`: see [Marked Type Parameters](marked-type-parameters.md#types-that-are-unsafe-to-copy) |

How to decide for a struct of your own, and why a lock guard is not unsafe to copy: [Marking a Struct](../guides/marking-a-struct.md#unsafe-to-copy).

A struct that holds a `NativeBuffer` in a field says so with `UnsafeToCopy = true` on its own attribute, since the analyzer never works it out from the fields. Until it does, the field gets a report (SCA1017) and the struct is what its attribute says: see [A Struct That Holds One](../guides/marking-a-struct.md#a-struct-that-holds-one).

A report about a container, such as a `List<NativeBuffer>`, a `NativeBuffer[]` or a task of one, is about the values that it holds. An array or a class holds its values by reference, so a `NativeBuffer[]` field asks nothing of the struct that declares it, while the array itself is a container of such values.

What counts of a value depends on what a report says happens to it:

| The report says | What counts |
|---|---|
| A value is copied | What the copy duplicates: the value and what it holds inline, through tuples, nullable types and structs. A list or a delegate that rides along is not duplicated, so what it holds does not count |
| Other code gets at a value or a container | Everything that code can reach: the value, what it holds inline, and the values of the lists, arrays and delegates that it carries, at any depth. A member that is not on the symbol list, a conversion or a box that hides the type, and a method that keeps a container are such reports |

A `(LockGuard, Func<NativeBuffer>)` is a plain non-copyable value. A copy of it duplicates the guard and only a reference to the delegate, so the copy gets the ordinary warning. A box of it lets other code reach the delegate, so the report of what the box hides is an error:

```cs
var copy = _job;        // SCA1001, the ordinary warning, for a field of type (LockGuard, Func<NativeBuffer>)
object boxed = _job;    // SCA1004, the ordinary warning, and SCA1023, an error
```

A report that involves several non-copyable types is an error where any of them is unsafe to copy, and its message names that one, whatever comes first in your code. A member of `Pair<LockGuard, NativeBuffer>` that is not on the symbol list, and `MemberwiseClone` in a class with a field of each, both get an error about `NativeBuffer`. Where one use breaks two rules, each gets its report: a `foreach` over a stored `Dictionary<LockGuard, NativeBuffer>` gets a warning for the key, since the framework symbol list names its members for the values only, and an error for the copy of each value.

#### Reports That Are Errors for Any Type

A few reports are about no single type, and they are errors whatever the types of the project are:

| ID | Report |
|---|---|
| SCA1007 | A call of `RuntimeHelpers.GetObjectValue`, which clones any boxed struct |
| SCA1008 | `MemberwiseClone` in a class that another assembly can derive from, and a member of such a class that clones without a `clones` line |
| SCA1023, SCA1031, SCA1033, SCA1040, SCA1041 | A report about what a generic hands out or keeps, or about a mark of a type parameter, `[RequiresCopyable]` or a generic move, where a caller can bind any type. A value of a type parameter marked `UnsafeToCopy = false` that is hidden or handed out by a delegate gets the ordinary warning, since such a type parameter takes no container of a type that is unsafe to copy |
| SCA1040, SCA1041 | A `[MovedFrom]`, `[Kept]` or `[return: Borrowed]` mark on a parameter or a return value whose type mentions a type parameter, other than one marked `UnsafeToCopy = false` |
| SCA1044 | An attribute of the analyzer that is `[Conditional]` |
| SCA1046 | A `[NotNullRef]` mark that does not count |

SCA1048, a member that is nested too deeply to check, is an error only where the member's code has a value of a type that is unsafe to copy, a container of one, or a type parameter that is marked `[NonCopyable]` without `UnsafeToCopy = false`. So is a member that calls `MemberwiseClone` or `RuntimeHelpers.GetObjectValue`, which can copy a value of any type. A long chain of string concatenations gets the ordinary warning: see [SCA1048](../diagnostics/sca1048.md#under-the-updated-rules).

## Errors and Warnings

Each ID is in one of three groups:

| IDs | About a type that is unsafe to copy, outside an unsafe context and at a declaration with no modifier | The same, in code in an unsafe context and at a declaration marked `safe` or `unsafe` | About any other non-copyable type |
|---|---|---|---|
| SCA1001 to SCA1017, SCA1020 to SCA1024, SCA1026, SCA1030 to SCA1033, SCA1040, SCA1041, SCA1043, SCA1044, SCA1046, SCA1048 | Error | Warning | Ordinary warning |
| SCA1042, SCA1045, SCA1047, SCA1049 | Warning | Warning | Warning |
| SCA1025 | Warning or message: see [SCA1025](../diagnostics/sca1025.md#warning-or-message) | The same | The same |

The warning of the middle column is not quite the ordinary one: `[SuppressMessage]` does not suppress it (see [Suppressing the Warning](#suppressing-the-warning)). The ordinary warning is the one of the current rules, which every kind of suppression and severity configuration reaches, in any place.

You can always answer a report of the first group in your own code, so it is an error where the type is unsafe to copy. The others are plain warnings for a reason of their own:

- **SCA1042** is about a line of a symbol list file, which has no unsafe context.
- **SCA1045** is about an attribute that is not the analyzer's, which a project may keep on purpose for another tool.
- **SCA1047** asks you to check a method and then suppress the warning, with `#pragma` or `[SuppressMessage]`.
- **SCA1049** is about how an attribute class of your own is declared, and changes nothing that the analyzer reads.
- **SCA1025** is about an assembly that someone else built, and has no place in the source.

A copy of a lock guard is the ordinary warning. A copy of a native buffer is an error outside an unsafe context, and a warning that you can suppress inside one:

```cs
[NonCopyable]
public struct LockGuard : IDisposable
{
    // ...
}

[NonCopyable(UnsafeToCopy = true)]
public struct NativeBuffer : IDisposable
{
    // ...
}

class Worker
{
    LockGuard _guard;
    NativeBuffer _buffer;

    void Keep()
    {
        LockGuard guard = _guard;                   // SCA1001: a warning, as under the current rules
        NativeBuffer buffer = _buffer;              // SCA1001: an error
    }

    void KeepOnPurpose()
    {
#pragma warning disable SCA1001 // why this copy is right
        NativeBuffer buffer = unsafe(_buffer);      // OK: a warning in an unsafe context, suppressed here
#pragma warning restore SCA1001
    }
}
```

## Code and Declarations

For a type that is unsafe to copy, the place of a report decides what can make it a warning, not its ID:

| Place of the report | Error | Warning |
|---|---|---|
| **Code:** a body, an initializer of a field or a property, a constructor initializer, the arguments of a primary constructor's base type, a lambda, the body of a local function, top-level statements | Outside an unsafe context | In an unsafe context |
| **Declaration:** a field, a property, a parameter or the return type of a member, a base list, a type parameter | With no modifier | Marked `safe` or `unsafe` |

A declaration has no unsafe context, so what surrounds it changes nothing. To answer for a report at a declaration, mark the declaration itself `safe` or `unsafe`, and suppress the warning there with a comment that says why it is right. Or fix the declaration, or set `NonCopyableErrorsAsWarnings` (see below):

```cs
struct Holding
{
    NativeBuffer _buffer;                       // SCA1010: an error, whatever surrounds it
}

struct HoldingOnPurpose
{
#pragma warning disable SCA1010 // why this field is right
    safe NativeBuffer _buffer;                  // OK: a warning at a marked declaration, suppressed here
#pragma warning restore SCA1010
}

struct HoldingAGuard
{
    LockGuard _guard;                           // SCA1010: a warning, as under the current rules
}
```

#### Marking a Declaration

Mark the declaration that the report is at. A modifier of the type around a member does not count, and neither does one of the member around a local function:

| Report at | Mark |
|---|---|
| A field, a property, an event, a method, a constructor, an operator or a delegate | The member |
| A parameter, the return type, a type parameter or an accessor of a member | The member |
| The parameters or the return type of a local function | The local function |
| The name, the base list, a type parameter or a primary constructor parameter of a type | The type, with `safe` |
| The receiver parameter of an `extension` block | Nothing: the report stays an error |

Prefer `safe`. The compiler does not accept `unsafe` on a type, a delegate, a destructor or a static constructor, or on a member that overrides or implements one that is not `unsafe` itself. A member marked `unsafe` also needs an unsafe context at every use.

> [!IMPORTANT]
> **`safe` counts only in a project that allows unsafe code,** with `AllowUnsafeBlocks` set to `true`. In any other project a report at a declaration marked `safe` stays an error. `unsafe` needs that setting anyway.

## Unsafe Context

The analyzer uses the unsafe context of the C# language, which the updated rules make narrow:

| Code | Unsafe context? |
|---|---|
| An `unsafe { }` block | Yes |
| An `unsafe(...)` expression | Yes |
| The `: base(...)` or `: this(...)` initializer of an `unsafe` constructor | Yes |
| A lambda or a local function | As the code around it |
| A member, a local function or a type with the `unsafe` modifier | No: the modifier makes no code unsafe. It counts only for a report at the declaration itself (see [Marking a Declaration](#marking-a-declaration)) |
| The body of an iterator (a method with `yield`) | No, even inside an `unsafe` block. An `unsafe` block in the iterator is unsafe again |

An `await` may stand in an unsafe context, so `unsafe(await task)` is fine.

## Covering One Report

Put the expression that the report is on in `unsafe(...)`, or the statement in an `unsafe` block, and suppress the warning there with a comment that says why the code is right. Keep the unsafe context to the code that needs it.

Each report in code lands on an expression that `unsafe(...)` can hold:

| Report | Lands on | Covered by |
|---|---|---|
| A copy into a local that is declared | The value | `var copy = unsafe(_buffer);` |
| What a `foreach` copies for each element | The collection | `foreach (var buffer in unsafe(_array))` |
| A deconstruction | The deconstructed value | `var (address, length) = unsafe(_block);` |
| What a spread copies | Its operand | `NativeBuffer[] all = [.. unsafe(_array)];` |
| The `Dispose` that a `using` calls | The value of the resource | `using var pool = unsafe(CreatePool());` |
| A read out of a task | The `await` | `NativeBuffer lost = unsafe(await loser);` |
| A use after a hand-off (SCA1016) | The use | `unsafe(buffer).Dispose();` or `unsafe { buffer.Dispose(); }` |

An `unsafe` block around a declaration would end the scope of the variables that it declares, so for a declaration, cover the value.

#### Covering a Statement

A statement cannot stand in `unsafe(...)`: `unsafe(buffer.Dispose());` does not compile. Two forms cover a statement, and neither copies the value:

| Form | Covers |
|---|---|
| `unsafe(buffer).Dispose();` | The use of `buffer`, where the report lands |
| `unsafe { buffer.Dispose(); }` | Every statement in the block |

A use after a hand-off is reported at the use, so an unsafe context around the hand-off changes nothing. Cover the use:

```cs
NativeBuffer buffer = NativeBuffer.Allocate(4096);

try
{
    await _channel.Writer.WriteAsync(buffer);
}
catch
{
#pragma warning disable SCA1016 // _channel can only throw before it takes the buffer
    unsafe(buffer).Dispose();
#pragma warning restore SCA1016
    throw;
}
```

Why this `catch` gets a report, and when the code is right: [Correct Code That Still Gets a Warning](../guides/correct-code-with-warnings.md#handing-a-value-over-and-keeping-it-on-failure).

## Suppressing the Warning

The warning in an unsafe context and the warning at a marked declaration are suppressed in the same ways:

| Suppression | Effect on the warning | Effect on the error |
|---|---|---|
| `#pragma warning disable` | Suppresses it on the lines that follow | None |
| `NoWarn`, or the severity `none` in `.editorconfig` or `.globalconfig` | Suppresses it in every unsafe context and at every marked declaration of the project | None |
| `[SuppressMessage]` | None | None |

Prefer `#pragma` with its reason: a suppression for the whole project hides every later copy in unsafe code and at marked declarations as well. A report about a type that is not unsafe to copy is the ordinary warning, which every kind of suppression reaches, as under the current rules.

## NonCopyableErrorsAsWarnings

A project that has the updated rules on for other reasons, and is not ready for the errors, sets this property:

```xml
<PropertyGroup>
  <NonCopyableErrorsAsWarnings>true</NonCopyableErrorsAsWarnings>
</PropertyGroup>
```

The analyzer then reports exactly as under the current rules, with an ordinary warning for every report. What else holds for the property: [Errors as Warnings](msbuild-properties.md#errors-as-warnings).

</div>
