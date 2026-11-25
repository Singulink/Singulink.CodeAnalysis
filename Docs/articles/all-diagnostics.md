<div class="article">

# All Diagnostics

Every warning of the analyzer has a diagnostic ID of the form `SCA1001`. Each kind of rule has its own thousand: the `SCA1000`s are the non-copyable rules. All are in the category Reliability, and each ID links to an article with an example and the ways to fix it.

Didn't add this analyzer yourself? See [Why Am I Seeing This?](start/why-am-i-seeing-this.md)

In a project that turns on the [updated memory safety rules](rules/updated-memory-safety-rules.md) of C#, most IDs are errors for a type that is unsafe to copy: see [Errors and Warnings](rules/updated-memory-safety-rules.md#errors-and-warnings).

## Copies

| ID | What it warns about |
|---|---|
| [SCA1001](diagnostics/sca1001.md) | A stored value is assigned to a variable, a field or an element, which copies it |
| [SCA1002](diagnostics/sca1002.md) | A stored value, or a variable that lives on, is returned by value |
| [SCA1003](diagnostics/sca1003.md) | A stored value is passed by value, or used in another way that may copy it |
| [SCA1004](diagnostics/sca1004.md) | A stored value is boxed: converted to `object` or an interface |
| [SCA1005](diagnostics/sca1005.md) | `MemberwiseClone` copies an object or a struct that holds a non-copyable value |
| [SCA1006](diagnostics/sca1006.md) | A member that is not `readonly` runs on a copy, behind a read-only reference |
| [SCA1007](diagnostics/sca1007.md) | `RuntimeHelpers.GetObjectValue`, which clones a boxed struct, is used |
| [SCA1008](diagnostics/sca1008.md) | `MemberwiseClone` is called on a class that another assembly can derive from |
| [SCA1009](diagnostics/sca1009.md) | A class whose base class clones itself, such as `Encoding`, holds or binds a non-copyable value, or an object of such a class is used in a way that lets other code clone it |

## Declarations and Hand-offs

| ID | What it warns about |
|---|---|
| [SCA1010](diagnostics/sca1010.md) | A field of a non-copyable type is declared in a copyable struct or a record |
| [SCA1011](diagnostics/sca1011.md) | An auto-property has a non-copyable type |
| [SCA1012](diagnostics/sca1012.md) | An iterator that returns a sequence takes a non-copyable value by value |
| [SCA1013](diagnostics/sca1013.md) | A non-copyable struct leaves an interface member to its default implementation |
| [SCA1014](diagnostics/sca1014.md) | A non-copyable struct has an `async` or iterator instance member that is not `readonly` |
| [SCA1015](diagnostics/sca1015.md) | An `await` uses an awaiter of a non-copyable type |
| [SCA1016](diagnostics/sca1016.md) | A variable is used after its value was handed over, so the hand-off copied the value |
| [SCA1017](diagnostics/sca1017.md) | A struct holds a field of a type that is unsafe to copy, and its attribute does not say `UnsafeToCopy = true` |

## Generic and Unchecked Code

| ID | What it warns about |
|---|---|
| [SCA1020](diagnostics/sca1020.md) | A member of a generic that is not marked is used with a non-copyable type, and is not on the [symbol list](rules/symbol-list-format.md) |
| [SCA1021](diagnostics/sca1021.md) | A member marked `[RequiresCopyable]` is used with a non-copyable type |
| [SCA1022](diagnostics/sca1022.md) | A container is used as an `Array` or a non-generic collection |
| [SCA1023](diagnostics/sca1023.md) | A container is converted to a type that hides what it holds |
| [SCA1024](diagnostics/sca1024.md) | Code that was not built with the analyzer gets at a non-copyable value other than by value |
| [SCA1025](diagnostics/sca1025.md) | A referenced assembly that was not built with the analyzer can use a non-copyable type |
| [SCA1026](diagnostics/sca1026.md) | A type that is unsafe to copy, or a container of one, is used for a type parameter marked `[NonCopyable(UnsafeToCopy = false)]` |

## Containers

| ID | What it warns about |
|---|---|
| [SCA1030](diagnostics/sca1030.md) | A value is read by value out of a list, a task or another container that still holds it |
| [SCA1031](diagnostics/sca1031.md) | A method returns a container that something else still holds |
| [SCA1032](diagnostics/sca1032.md) | A method keeps a container parameter that is not marked `[Kept]` |
| [SCA1033](diagnostics/sca1033.md) | A container is moved out of something that is not a local the method owns |

## Marks and Configuration

| ID | What it warns about |
|---|---|
| [SCA1040](diagnostics/sca1040.md) | An override, an implementation or a delegate does not keep a mark of the member that it stands for |
| [SCA1041](diagnostics/sca1041.md) | A `[MovedFrom]` parameter is used wrongly |
| [SCA1042](diagnostics/sca1042.md) | A line of a [symbol list file](rules/symbol-list-format.md) cannot be parsed, or has no effect |
| [SCA1043](diagnostics/sca1043.md) | A type that derives from an erasable type does not stay erasable |
| [SCA1044](diagnostics/sca1044.md) | An attribute of the analyzer is declared `[Conditional]` |
| [SCA1045](diagnostics/sca1045.md) | A `NonCopyableAttribute` that the analyzer does not read |
| [SCA1046](diagnostics/sca1046.md) | `[NotNullRef]` where it does not count |
| [SCA1047](diagnostics/sca1047.md) | `[NotNullRef]` on a method of the project, to verify and then suppress |
| [SCA1048](diagnostics/sca1048.md) | A member that is nested too deeply for the analyzer, and was not checked |
| [SCA1049](diagnostics/sca1049.md) | A non-copyable mark of the project sets `UnsafeToCopy` in its own code, which the analyzer does not read |

</div>
