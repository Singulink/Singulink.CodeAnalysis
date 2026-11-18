# Third-Party Notices

Singulink.CodeAnalysis includes code derived from the following third-party software.

## Roslyn

The non-copyable analyzer (the `SCA1000` rules) and its tests are based on the `DoNotCopyValue` analyzer (rule `RS0042`) of the
Roslyn.Diagnostics.Analyzers package and its tests, from the Roslyn repository:

- Source: https://github.com/dotnet/roslyn, commit `7373c8070ab2ccf71bb874301699552b8d0f7fd3`, files
  `src/RoslynAnalyzers/Roslyn.Diagnostics.Analyzers/Core/AbstractDoNotCopyValue.cs`,
  `src/RoslynAnalyzers/Roslyn.Diagnostics.Analyzers/CSharp/CSharpDoNotCopyValue.cs`,
  `src/RoslynAnalyzers/Roslyn.Diagnostics.Analyzers/UnitTests/DoNotCopyValueTests.cs`, and helpers from
  `src/RoslynAnalyzers/Utilities` and `src/Workspaces/SharedUtilitiesAndExtensions/Compiler/Core/Extensions/OperationExtensions.cs`.
- License: MIT
- Used in: the analyzer itself and its tests, so this notice applies to the `Singulink.CodeAnalysis.Analyzers` NuGet package.

```
The MIT License (MIT)

Copyright (c) .NET Foundation and Contributors

All rights reserved.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## hamarb123.Analyzers

The test class `ThirdPartyDefensiveCopyTests` contains the test sources of the defensive-copy analyzer (rules `HAM0001` and `HAM0003`) of the
hamarb123.Analyzers package, with `[NonCopyable]` added to the structs and type parameters. No code of that analyzer is used.

- Source: https://github.com/hamarb123/hamarb123.Analyzers, commit `5219d1b29408901dc67c7f6cdec1230e2b6a84dd`, files
  `hamarb123.Analyzers.Test/DefensiveCopies/DefensiveCopyCompilerGeneratedTests.cs`, `DefensiveCopyImplicitCallTests.cs`,
  `DefensiveCopyObscureTests.cs`, `DefensiveCopyStandardTests.cs` and `DefensiveCopyWithinTests.cs`.
- License: MIT
- Used in: the test project only. None of this code is in a NuGet package.

```
MIT License

Copyright (c) 2022-2025 Hamish Arblaster

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```
