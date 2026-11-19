<div class="article">

# Sharing Your Library

If your library's public API has non-copyable structs, let the analyzer reach the projects that use your library, so that they get a warning when they copy your structs. This page shows how, and how to ship a [symbol list file](symbol-list.md) with the library.

### At a Glance

- **Reference the analyzers package with `PrivateAssets="none"`,** or reference `Singulink.CodeAnalysis` as a normal reference: either one passes the analyzer on.
- **The analyzer then reaches every project down the chain,** and checks there only what your library marked. In those projects, it does not check the ten built-in framework structs.
- **A library that keeps the analyzer to itself** leaves the projects that use it without the analyzer, and a project that references both gets a warning about it (SCA1025).
- **A [symbol list file](symbol-list.md) can ship with the library,** for the members of its own generics that are not marked.

## Passing the Analyzer On

```xml
<PackageReference Include="Singulink.CodeAnalysis.Analyzers" Version="0.6.0" PrivateAssets="none" />
```

Your package then depends on the analyzers package with all its assets (`<dependency id="Singulink.CodeAnalysis.Analyzers" version="0.6.0" include="All" />`), and a project that references your package gets the analyzer. If you reference `Singulink.CodeAnalysis` instead, keep it a normal reference and the same holds: the attributes package already passes the analyzers on.

#### Down the Chain

The analyzer also reaches projects further down the chain. A project that references a package that depends on your package gets the analyzer too, whatever that package's own reference to yours says.

The .NET SDK (checked with the .NET 10 and 11 SDKs) applies the analyzers of every package in the restore graph. It does not honor an `exclude="Analyzers"` in a dependency or `ExcludeAssets="analyzers"` on a reference. The only way to keep the analyzer from flowing past your library is `PrivateAssets="all"`, which leaves the dependency out of your package altogether.

#### What Your Users See

In a project that only gets the analyzer through your library, the analyzer checks the types that your library marked, and it does not check the [built-in types](built-in-types.md). See [Why Am I Seeing This?](../start/why-am-i-seeing-this.md), which is the page your users land on.

> [!CAUTION]
> **A library that keeps the analyzer to itself leaves the projects that use it without the analyzer.** Their code can copy your structs where it implements one of your interfaces, makes a delegate that you call or reads an object of one of your classes, and neither your library nor a project further down the chain can see that. A project that references both your library and such an assembly gets a warning about it, once for each such assembly ([SCA1025](../diagnostics/sca1025.md)).

## Shipping a Symbol List File With a Library

Marking the type parameters of the library's generics comes first, since the analyzer then checks their bodies: see [Marking a Type Parameter](marking-a-type-parameter.md). A library whose generics stay unmarked can ship what its author knows about them, so that the projects that use the library get those entries with no setup. Such a file has entries only for the library's own members and types, never for the framework or for other libraries.

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

The `AdditionalFiles` line makes the library read its own file, wherever the file is. So a line that cannot be parsed, or that does not fit what it names, gets a warning in the library's own build (SCA1042), where its author sees it. In a project that uses the library, such a line gets a warning at the file in the package.

For the project that uses the library, the file is a symbol list file like its own: all symbol list files are merged, with each other and with the [built-in framework symbol list](../rules/framework-symbol-list.md). A library that ships a symbol list file also [passes the analyzer on](#passing-the-analyzer-on), since the list is of no use without it.

What the lines of the file can say: [Symbol List](symbol-list.md).

</div>
