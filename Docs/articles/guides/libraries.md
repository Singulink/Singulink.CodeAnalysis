<div class="article">

# Sharing Your Library

If your library's public API has non-copyable structs, let the analyzer reach the projects that use your library, so that they get a warning when they copy your structs. This page shows how, and how to ship a [symbol list file](symbol-list.md) with the library.

### At a Glance

- **Reference `Singulink.CodeAnalysis` as a normal reference,** or reference the analyzers package with `PrivateAssets="none"`: either one passes the analyzer on.
- **The analyzer then reaches every project down the chain,** and checks there only what your library marked. In those projects, it does not check the [built-in non-copyable types](built-in-types.md).
- **A library that keeps the analyzer to itself** leaves the projects that use it without the analyzer, and a project that references both is told about it (SCA1025).
- **A [symbol list file](symbol-list.md) can ship with the library,** for the members of its own generics that are not marked, for its own members that take a value only where they return true, and for each class of the library that clones itself.

## Passing the Analyzer On

The simplest way is a normal reference to `Singulink.CodeAnalysis`, with no `PrivateAssets`:

```xml
<PackageReference Include="Singulink.CodeAnalysis" Version="1.0.1" />
```

Your package then depends on `Singulink.CodeAnalysis`, which passes the analyzer on, so a project that references your package gets the analyzer.

A library that wants no runtime dependency [declares the attributes itself](marking-a-struct.md#declaring-the-attributes-yourself) and references the analyzers package with `PrivateAssets="none"`:

```xml
<PackageReference Include="Singulink.CodeAnalysis.Analyzers" Version="1.0.1" PrivateAssets="none" />
```

Your package then depends on the analyzers package with all its assets (`<dependency id="Singulink.CodeAnalysis.Analyzers" version="1.0.1" include="All" />`), and a project that references your package gets the analyzer.

| | `Singulink.CodeAnalysis` | `Singulink.CodeAnalysis.Analyzers` alone |
|---|---|---|
| Attributes and helpers | They come from the package, and an update brings them together with the analyzer | You declare them in your project, and bring them up to date yourself |
| Runtime dependency | One small assembly | None |
| Reference | A normal reference | `PrivateAssets="none"` |

#### Down the Chain

The analyzer also reaches projects further down the chain. A project that references a package that depends on your package gets the analyzer too, whatever that package's own reference to yours says.

The .NET SDK (checked with the .NET 10 and 11 SDKs) applies the analyzers of every package in the restore graph. It does not honor an `exclude="Analyzers"` in a dependency or `ExcludeAssets="analyzers"` on a reference. The only way to keep the analyzer from flowing past your library is `PrivateAssets="all"`, which leaves the dependency out of your package altogether.

#### What Your Users See

In a project that only gets the analyzer through your library, the analyzer checks the types that your library marked, and it does not check the [built-in non-copyable types](built-in-types.md). See [Why Am I Seeing This?](../start/why-am-i-seeing-this.md), which is the page your users land on.

Mark a struct `[NonCopyable(UnsafeToCopy = true)]` where a copy of it can free, unpin or reach memory twice, or after it is gone: see [Unsafe to Copy](marking-a-struct.md#unsafe-to-copy). Your users get the same warnings either way under the current memory safety rules of C#. A project that turns on the [updated memory safety rules](../rules/updated-memory-safety-rules.md) gets an error for a copy of such a struct, and the ordinary warning for a copy of any other.

> [!CAUTION]
> **A library that keeps the analyzer to itself leaves the projects that use it without the analyzer.** Their code can copy your structs where it implements one of your interfaces, makes a delegate that you call or reads an object of one of your classes, and neither your library nor a project further down the chain can see that. A project that references both your library and such an assembly gets [SCA1025](../diagnostics/sca1025.md) for it, once for each such assembly: a warning in a project that added the analyzer itself, and a message in one that got it only through a library.

## Shipping a Symbol List File With a Library

Marking the type parameters of the library's generics comes first, since the analyzer then checks their bodies: see [Marking a Type Parameter](marking-a-type-parameter.md). A library whose generics stay unmarked can ship what its author knows about them, so that the projects that use the library get those entries with no setup. Such a file has entries only for the library's own members and types, never for the framework or for other libraries. A member of the library that takes a value only where it returns true gets a [`borrowswhen`](symbol-list.md#a-member-that-takes-a-value-only-where-it-returns-true) line there as well.

The package carries a [symbol list file](symbol-list.md) that is named after the library. A props file named after the package adds the file to the additional files of each project that references the package:

```xml
<!-- build\MyLibrary.props -->
<Project>
  <ItemGroup>
    <AdditionalFiles Include="$(MSBuildThisFileDirectory)NonCopyableSymbols.MyLibrary.txt" Visible="false" />
  </ItemGroup>
</Project>
```

```xml
<!-- MyLibrary.csproj -->
<ItemGroup>
  <AdditionalFiles Include="NonCopyableSymbols.MyLibrary.txt" />
  <None Include="NonCopyableSymbols.MyLibrary.txt" Pack="true" PackagePath="build;buildTransitive" />
  <None Include="build\MyLibrary.props" Pack="true" PackagePath="build;buildTransitive" />
</ItemGroup>
```

| Part | Why |
|---|---|
| `build` and `buildTransitive` | With both, a project that references the package gets the file, and so does a project that gets the package only through another package |
| The `AdditionalFiles` line of the library's own project file | The library reads its own file, so its author sees a line that is wrong (see below) |
| `Visible="false"` | Keeps the file out of the project tree of the user |
| The name `NonCopyableSymbols.MyLibrary.txt` | Keeps the file apart from the project's own `NonCopyableSymbols.txt` and from the file of every other library |

The `AdditionalFiles` line makes the library read its own file, wherever the file is. So a line that cannot be parsed, or that does not fit what it names, gets a warning in the library's own build (SCA1042), where its author sees it. In a project that uses the library, such a line gets a warning at the file in the package. Four kinds of line get a warning only in the library's own build:

- a line that names a member of the built-in framework symbol list for more type parameters than that list does, which has no effect anywhere;
- a `borrowswhen` line that lists an outcome which the built-in framework symbol list does not list for the member, and the entry of that list applies;
- a `borrows` line for a member that the built-in framework symbol list names with `borrowswhen` or with no marker, and the entry of that list applies;
- a `borrowswhen` line with an outcome that the analyzer does not know, which is taken without that outcome.

For the project that uses the library, the file is a symbol list file like its own: all symbol list files are merged, with each other and with the [built-in framework symbol list](../rules/framework-symbol-list.md). A `copyable` line is the one exception: it counts in the library's own build and does nothing for the projects that use the library, since each project decides for itself which structs it copies on purpose. A library that ships a symbol list file also [passes the analyzer on](#passing-the-analyzer-on), since the list is of no use without it.

#### A Class That Clones Itself

A class of the library that clones itself with `MemberwiseClone`, and that other projects may derive from, gets SCA1008 in the library's own build. The message of SCA1008 gives a `clones` line for the class. With that line in the shipped file, a class that derives from it in another project gets a warning for each field that holds a non-copyable value ([SCA1009](../diagnostics/sca1009.md)).

Once the class has its line, SCA1008 also finds the members of the class that another project can call and that clone their object themselves, and asks for a line for each. With those lines, a project that calls such a member on an object of a derived class gets a warning there. The warnings are a help, not a proof: add a line as well for a member that clones its object through a private helper or a static helper, and for one that hands its object to other code, such as one that stores it in another object that it returns. A line for a private or internal member has no effect in the project that uses the library.

Once the lines name every member that clones the object or hands it on, write the type line as `clones=complete`: a project that uses the library then gets no warning for the other members. The shipped file then has lines such as these:

```text
# NonCopyableSymbols.MyLibrary.txt
T:MyLibrary.Options;clones=complete
M:MyLibrary.Options.Copy;clones
```

Then suppress SCA1008 on the call: see [SCA1008](../diagnostics/sca1008.md#shipping-a-clones-line).

What the lines of the file can say: [Symbol List](symbol-list.md).

</div>
