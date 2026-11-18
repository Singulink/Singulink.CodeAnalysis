# Stress tools

These tools are run on purpose, before a release or after a change to the flow rules, and are no part of `dotnet test`. Each
one generates C# code, builds it with the analyzer and runs it, and counts as a miss every case that the analyzer accepted with
no finding and that released one acquisition of a guard twice when it ran. The guard of the generated code counts tokens, so a
second release of one acquisition is seen at run time, whatever the analyzer said.

They need bash (Git Bash on Windows), Python 3 and the .NET SDKs that are named below. Build the repository in Release first:
the tools take the analyzer from `Source/*/bin/Release` unless `ANALYZER` names a folder with
`Singulink.CodeAnalysis.Analyzers.dll` and `Singulink.CodeAnalysis.dll`. The generated projects go to `WORK`, by default
`$TMPDIR/sca-stress`, outside the repository, so that its build settings and analyzers do not apply to them. `SDK` picks the SDK
(11.0.100 by default; 10.0.403 runs the compiler that the analyzer is built against).

## `Flow/`: methods with guards in every kind of control flow

`Flow/run.sh LABEL GENERATOR MODE COUNT SEED...` writes one file of `COUNT` methods for each seed, and runs every method under 128
patterns of its conditions and with a throw at each of ten positions.

- **`gen_lz.py`:** methods that own locals and a by-value parameter of a guard type, of `Guard?`, of a tuple with a guard, of
  `Task<Guard>` and of `List<Guard>`, read them by value, use them in place, take references to them and assign them, inside `if`,
  loops, `switch`, `goto`, `try` with every kind of clause, `using`, `lock`, `foreach` and conditional expressions, with the commit
  shape `try { ...; F1 = g; <tail> } catch { g.Dispose(); }`. Modes: `mix`, `commit`, `sparse`, `more` (nested local functions,
  `using` declarations, `foreach` variables, `switch` expressions, value tasks, aliases of containers) and `async` (`await` between
  the statements).
- **`gen_nest.py`:** the same with functions inside functions, to depth two and wherever a statement can stand: lambdas, static
  lambdas, anonymous methods, local functions, `async` ones with `await`, iterators with a `finally`, lambdas in exception filters,
  expression trees, and nested functions with a `ref Guard` and a `[MovedFrom] ref Guard` parameter. Modes: `nest` (a class),
  `nesta` (`async` hosts), `nests` (a struct), `nestn` (a non-copyable struct, also called on a null `this`) and `nestc` (more
  commits).
- **`gen_nest2.py`:** commit tails through a `ref int` that is a null reference in every other run, and through `this` of a struct.
  Modes: `nestx` and `nestsx`.
- `verdict.py` reads the build log and the run and prints the summary; `stats.py` and `stats_nest.py` say what the accepted
  methods of a run contain.

Example, and what it costs on a developer machine: `bash Tests/Stress/Flow/run.sh more gen_lz.py more 1000 1 2 3 4 5` builds and
runs 5,000 methods in about half a minute (`gen_nest.py` about 40 seconds). The passes ran 20,000 to 100,000 methods per mode.

## `Checked/`: compound assignments and increments in checked contexts

`Checked/run.sh [true|false]` writes every narrow type and its nullable, every compound operator and increment, in every form of
checked context (a block, an expression, nested `checked` and `unchecked`, a lambda, the project setting), each after a commit and
between the read and the assignment of a `[MovedFrom]` parameter, with values that overflow: about 10,400 cases. The argument sets
`<CheckForOverflowUnderflow>`. One run takes about ten seconds.

## What a result looks like

Every run ends with a summary line, and `AD0001:` gives the count of exceptions of the analyzer, which must be 0:

```
== .../flow-more-11.0.100: 5000 methods, 1419 accepted, 1897 with a double release at run time, 0 MISSES; ...
.../checked-11.0.100-true: 10381 cases run, 5314 accepted, 1552 release twice, MISSES: []
```

A failure is a method or a case that is named before the summary (`MISS Z901m.00042 TWICE flags=3 throwAt=4 returned`, or an ID in
the list of `MISSES`): the analyzer accepted it, and it released a guard twice. The method is in the generated file under
`WORK`, at the line that `// CASE` names; cut it down to a test of the repository. `RUNTIME FAILURE` names a method that crashed the
process, which is skipped: that is a fault of the generated code, not of the analyzer.
