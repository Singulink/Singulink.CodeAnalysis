<div class="article">

# Glossary

This section has the exact rules that the analyzer checks, one topic per page. This page explains the words that the rules use, and how each page is laid out.

### How a Rule Page Is Laid Out

Each page opens with a short summary, often a table, under **At a Glance**. Each rule then has a heading of its own, in the same order:

1. the rule, in one or two sentences;
2. a code block, where each line that matters ends in `// SCAnnnn: why` or `// OK: why`;
3. why the rule is there, where that is not obvious;
4. the exceptions, as a table or a list.

The code blocks use the same names throughout: `LockGuard` is a struct marked `[NonCopyable]`, `myLock.Enter()` returns a new one, `guard` is a local, `_guard` a field and `guards` a `List<LockGuard>`. Every rule is told on one page; other pages link to it.

## Terms

#### New Value and Stored Value

A **new value** is one that nothing else holds yet: the result of a call, `new`, `default`, a move, or a local at its last use. A **stored value** is read from a place that goes on holding it, and reading it by value makes a copy. See [New and Stored Values in Detail](new-and-stored-values.md).

#### Non-copyable Type

A struct marked `[NonCopyable]`, a type parameter so marked inside its generic, a generic struct with a non-copyable type argument such as `(LockGuard, bool)`, or a struct that a [symbol list entry](symbol-list-format.md) names as non-copyable. Such an entry is in the [built-in framework symbol list](framework-symbol-list.md) or in a symbol list file. See [Non-copyable Types](non-copyable-types.md#what-is-non-copyable).

#### Container

Any other type that mentions a non-copyable type, such as `LockGuard[]`, `List<LockGuard>` or `Task<LockGuard>`. Copying a container copies no element; the rules control how a value is read out of it. See [Non-copyable Types](non-copyable-types.md#containers).

#### View

A container struct that only refers to elements stored elsewhere, such as <xref:System.Span`1> or <xref:System.ArraySegment`1>. A view never owns its elements, so a value read out of one by value is always a copy.

#### Last Use

The place where a method uses a local or a by-value parameter for the last time, on every path. There the variable is a new value, so it can be handed on without a copy. See [Last Use of a Variable](last-use.md).

#### Commit

The store at the end of a `try` block that hands a value over, in the shape "acquire, set up, commit, or dispose on failure". The handlers of the `try` do not count as a later use of the variable if nothing that can throw runs after the store. See [Commit at the End of a Try Block](commit.md).

#### Able to Throw

Any operation that is not on the analyzer's closed list of operations that cannot throw and run no other code. The commit and a `[MovedFrom]` parameter both depend on it. Between the read and the reset of a `[MovedFrom]` parameter a few more operations are allowed. See [What Counts as Able to Throw](what-can-throw.md).

#### Known to Refer to Something

A `ref`, `in` or `out` parameter, a `ref` local or `this` in a struct, at a place where, on every path, something has already proved that the reference is not a null reference. A read or a plain store through it then cannot fail. See [Null References](null-references.md).

#### Method's Own Local

A local that holds a container which nothing else holds: everything assigned to it is new, and it is used only in the ways that a closed list allows. A method may return such a local, and move from it. See [Locals That the Method Owns](owned-locals.md).

#### Symbol List, Symbol List File, Built-in Framework Symbol List and Symbol List Entry

- The **symbol list** tells the analyzer what it cannot see in code that you cannot mark: the [built-in framework symbol list](framework-symbol-list.md) and every symbol list file that the project reads, merged. See [Symbol List Format](symbol-list-format.md).
- A **symbol list file** is a `NonCopyableSymbols.txt` file of the project, or one that comes with a library that it uses. All are read alike and merged with the framework symbol list. Where two lines disagree, the one that claims less applies, except for `copyable` and `clones=complete` lines (see [Entries That Disagree](symbol-list-format.md#entries-that-disagree)). Most projects never need one: see [Symbol List](../guides/symbol-list.md).
- The **built-in framework symbol list**, or **framework symbol list** for short, is the symbol list for .NET's own libraries: it is built into the analyzer and merged with the symbol list files of the project. See [Built-in Framework Symbol List](framework-symbol-list.md).
- A **symbol list entry** is one line of a symbol list file or of the framework symbol list.

#### Member on the Symbol List

A member of an unmarked generic that the analyzer may use with a non-copyable type, because the [symbol list](symbol-list-format.md) names it. It hands out a new value if the object that it was called on and its arguments are new, and a copy if they are stored. See [What a Member on the Symbol List Hands Out](listed-members.md).

#### Kept and Borrowed

A parameter marked `[Kept]` keeps the container that it is given, so the caller no longer owns it. A return value marked `[return: Borrowed]` is a container that the method still holds, so the caller treats it as stored. See [Keeping a Container That Is Passed In](keeping-containers.md) and [Returning a Container](returning-containers.md#borrowed-mark).

#### Moved From

A `ref` parameter marked `[MovedFrom]`: reading it by value is a move, and the method must reset it before anything else can see the value in two places. See [MovedFrom Mark](moved-from.md).

A **generic move** is a method such as `NonCopyable.Move` that:

- has a `[MovedFrom]` parameter whose type is a marked type parameter;
- has no other parameter, and no receiver, that mentions a marked type parameter;
- returns by value, not a reference or a ref struct.

The analyzer checks that its body hands out and leaves behind only what it read from its `[MovedFrom]` parameters. A call of it is a **pure move** if it has no receiver that carries a non-copyable type, every argument that carries one goes to a `[MovedFrom]` parameter, and the method's return value is not `[return: Borrowed]`. A pure move of a local that the calling method owns hands out a new value. See [Generic Moves](moves-of-containers.md#generic-moves).

#### Checked and Unchecked Code

**Checked code** is the project itself and every assembly that was built with the analyzer. Everything else, the framework included, is **unchecked code**. In unchecked code, a mark that promises a checked body counts as absent. See [Code Not Built With the Analyzer](unchecked-code.md).

#### Erasable

A generic class or interface where no base type or interface hands out a value of its type arguments, such as <xref:System.Threading.Tasks.Task`1>. A container of such a type may be converted to those base types and interfaces. See [Variance and Erasable Types](variance-and-erasable-types.md#erasable-types).

#### Class That Clones Itself

A class whose own code clones its objects field by field with <xref:System.Object.MemberwiseClone>, such as <xref:System.Text.Encoding>. A class that derives from one and holds a non-copyable value gets a warning on that field (SCA1009). A `clones` line of the symbol list names such a class. See [Symbol List Format](symbol-list-format.md#clones).

#### Temporary Copy

A copy that the compiler makes by itself before it looks into a value: for a member through a read-only reference, for `?.` on a nullable, for a pattern. You get a warning where a member that is not `readonly` runs on a temporary copy, or where a part of one is kept. See [Read-only References and Temporary Copies](read-only-references.md).

</div>
