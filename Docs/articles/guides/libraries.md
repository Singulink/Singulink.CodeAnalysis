<div class="article">

# Libraries

If your library's public API has non-copyable structs, let the analyzer reach the projects that use your library, so that they are warned when they
copy your structs. Reference the analyzers package with `PrivateAssets="none"`:

## Passing the Analyzers On

```xml
<PackageReference Include="Singulink.CodeAnalysis.Analyzers" Version="0.5.0" PrivateAssets="none" />
```

Your package then depends on the analyzers package with all its assets (`<dependency id="Singulink.CodeAnalysis.Analyzers" version="0.5.0"
include="All" />`), and a project that references your package gets the analyzer. If you reference `Singulink.CodeAnalysis` instead, keep it a normal
reference and the same holds: the attributes package already passes the analyzers on.

The analyzer also reaches projects further down the chain. A project that references a package that depends on your package gets the analyzer too,
whatever that package's own reference to yours says. The .NET SDK (checked with the .NET 10 and 11 SDKs) applies the analyzers of every package in the
restore graph and does not honor an `exclude="Analyzers"` in a dependency or `ExcludeAssets="analyzers"` on a reference. The only way to keep the
analyzer from flowing past your library is `PrivateAssets="all"`, which leaves the dependency out of your package altogether.

## Shipping a List With a Library

A library whose generics are not marked can ship what its author knows about them, so that the projects which use the library get the
entries without doing anything. **Such a file lists what belongs to the library itself, its own members and types, and nothing of the
framework or of other libraries.** The package carries a list file that is named after the library, and a props file that adds it to the
additional files of the project which references the package. The props file has the name of the package:

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

- **`build` and `buildTransitive`:** with both, a project that references the package gets the file, and so does a project that gets the
  package only through another package.
- **The library reads its own file,** by the first line of the project file, which also covers a file that is not in the folder of the
  project. That checks every line against the library where its author sees the result: a line that cannot be parsed or does not fit what it names is reported there (`SCA1042`). In a project that uses the
  library such a line is reported at the file in the package.
- **`Visible="false"`** keeps the file out of the project tree of the user.
- **The name:** `NonCopyableSymbols.MyLibrary.txt` keeps the file apart from the project's own `NonCopyableSymbols.txt` and from
  the file of every other library, where files are listed and where a line of one is reported.
- **For the project that uses the library** the file is a list file like its own: all list files are merged, with each other and with
  the built-in list.
- A library that ships a list also [passes the analyzer on](#), since the list is of no use without it.

</div>
