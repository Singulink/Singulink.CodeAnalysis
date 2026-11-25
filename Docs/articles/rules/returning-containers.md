<div class="article">

# Returning a Container

The result of a call is a new value, so a method hands over the container that it returns. This page says which containers a method may return as its own, and gives the mark for one that it keeps.

### At a Glance

| A method returns | Result |
|---|---|
| A container that it created, or got from a call of checked code that is not `[return: Borrowed]` | OK |
| A [local that it owns](owned-locals.md), even one that it filled | OK |
| A container from a call of [unchecked code](unchecked-code.md), unless a `new` entry says otherwise | SCA1031 |
| A container from a call that is `[return: Borrowed]` | SCA1031, unless the method is `[return: Borrowed]` too |
| A container that it read from a field, a property, an indexer, an element or a parameter | SCA1031 |
| A local that was assigned one of those, or that a lambda captures | SCA1031 |
| A local that it did anything with other than the uses that keep a local its own | SCA1031 |
| A container that it keeps, with `[return: Borrowed]` | OK, and its callers treat the result as stored |

## What Gets the Warning

A method, a local function, a lambda or an operator that returns, or yields, a container which something else still holds gets a warning at the `return` (SCA1031). Where a local is not the method's own, the message says why.

```cs
Task<LockGuard> GetCachedAsync() => _cached;      // SCA1031: every caller would get the same task

List<LockGuard> Build()
{
    var list = new List<LockGuard>();
    list.Add(myLock.Enter());
    return list;                                  // OK: the method's own
}
```

- **One stored branch** of a `?:`, a `??` or a `switch` expression is enough for a warning.
- **An iterator** keeps running after it has yielded. So a local that it yields at two places, or in a loop when the local is declared outside that loop, gets a warning as well.
- **An array and an <xref:System.ArraySegment`1>** are containers like any other: a method that returns an array field gets a warning.
- **A <xref:System.Span`1>, a <xref:System.ReadOnlySpan`1>, a <xref:System.Memory`1> and a <xref:System.ReadOnlyMemory`1>** are not checked: they cannot become an interface, and every by-value read of an element through one gets a warning whatever it came from.
- **The getter of a property or an indexer** is not checked, because every caller treats its result as stored anyway. Nor is a container that goes out through an `out` or `ref` parameter, which always lands in a variable.

```cs
List<LockGuard> Pick(bool fresh) => fresh ? new List<LockGuard>() : _guards;   // SCA1031: one branch is stored
```

## Borrowed Mark

A method that keeps what it returns says so with `[return: Borrowed]`. Its body may then return a container that it does not own, and its callers treat the result as stored, as they treat what a property returns:

```cs
[return: Borrowed]
List<LockGuard> GetGuardsFor(string key) => _byKey[key];   // OK

foreach (LockGuard g in holder.GetGuardsFor("a")) { }      // SCA1030: a read from a stored container
holder.GetGuardsFor("a").Add(myLock.Enter());              // OK: used in place
```

Overrides and delegates must keep `[Borrowed]` like the other marks: see [Borrowed Mark](keeping-marks.md#borrowed-mark).

</div>
