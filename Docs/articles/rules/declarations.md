<div class="article">

# Fields, Interfaces and Async Members

Some declarations copy a value by themselves, although the code shows no copy: a field of a type that is copied, an auto-property, an iterator, a default interface member, an `async` member, a clone. This page lists them. Their warnings are SCA1005, SCA1008 to SCA1014 and SCA1017.

### At a Glance

| Declaration | Warning |
|---|---|
| A field of a non-copyable type in a struct that is not marked `[NonCopyable]`, or in a record | SCA1010 |
| A parameter of a primary constructor that a struct which is not marked `[NonCopyable]` keeps | SCA1010 |
| A field of a type that is unsafe to copy in a struct whose attribute does not say `UnsafeToCopy = true` | SCA1017 |
| An auto-property of a non-copyable type | SCA1011 |
| A by-value parameter of a non-copyable type on an iterator that returns a sequence | SCA1012 |
| A non-copyable struct that leaves an interface member to its default implementation | SCA1013 |
| An `async` or iterator instance member of a non-copyable struct that is not `readonly` | SCA1014 |
| <xref:System.Object.MemberwiseClone> of an object or a struct that holds a non-copyable value | SCA1005 |
| <xref:System.Object.MemberwiseClone> on a class that another assembly can derive from | SCA1008 |
| A field that holds a non-copyable value in a class whose base class clones itself, a binding of a non-copyable type to such a class, and a use that lets other code clone such an object | SCA1009 |

## Fields

A field of a non-copyable type can be declared in a class or in a struct marked `[NonCopyable]`. A static field is fine anywhere, since no copy of a struct or a record copies it. An instance field elsewhere gets a warning:

- **A struct that is not marked `[NonCopyable]`** copies the field with itself (SCA1010). A type parameter marked `[NonCopyable]` does not stand in for the mark: `Pair<int>` of a struct `Pair<[NonCopyable] T>` is copyable, and copies the field.
- **A record**, class or struct, cannot have such a field at all (SCA1010). It copies its fields in `with`, in its copy constructor and in the `Equals`, `GetHashCode` and `ToString` that the compiler writes for it.
- **An auto-property** cannot have a non-copyable type, since its getter returns a copy (SCA1011).

```cs
struct Plain { LockGuard _guard; }                     // SCA1010: a copyable struct copies the guard with itself

struct Pair<[NonCopyable] T> { T _first; LockGuard _second; }   // SCA1010: Pair<int> copies the guard with itself

class Owner
{
    public LockGuard Guard { get; set; }               // SCA1011: the getter returns a copy
    private LockGuard _guard;                          // OK: a field of a class
    private static LockGuard s_shared;                 // OK: a static field
}

record Entry(int Id) { LockGuard _guard; }             // SCA1010: 'with' and the copy constructor copy it
```

A non-copyable struct that holds a value that is unsafe to copy is unsafe to copy as well, and says so with `UnsafeToCopy = true` on its own attribute. Otherwise the field gets a warning (SCA1017). See [A Struct That Holds One](../guides/marking-a-struct.md#a-struct-that-holds-one).

#### Fields That Do Not Look Like One

Two things are fields without looking like one:

- **A parameter of a primary constructor that members of the type use.** The compiler keeps it in a field, so in a struct that is not marked it gets a warning like a field does (SCA1010), and so does one of a type that is unsafe to copy in a struct that does not say so (SCA1017).
- **A by-value parameter of an iterator.** The compiler keeps the arguments in the object that the iterator returns and copies them into every enumerator, so the parameter gets a warning where it is declared (SCA1012).

An iterator that returns an enumerator, not a sequence (<xref:System.Collections.Generic.IEnumerator`1>, <xref:System.Collections.Generic.IAsyncEnumerator`1>), keeps its arguments once and gets no warning.

```cs
struct Session(LockGuard guard) { public void Close() => guard.Dispose(); }   // SCA1010: 'guard' is a field of a copyable struct

IEnumerable<int> Read(LockGuard guard) { /* ... */ yield return 1; }          // SCA1012: every enumeration gets a copy of 'guard'
```

## Default Interface Members

A non-copyable struct must implement every member of its interfaces itself. A call of a member that it leaves to the interface's default implementation runs on a boxed copy, even through a constrained type parameter, where nothing shows the box. That includes a default that a derived interface gives for a member of its base interface. Such a struct gets a warning where it is declared (SCA1013).

```cs
interface IReleasable
{
    void Release() { }                         // a default implementation
}

[NonCopyable]
struct Lease : IReleasable { }                 // SCA1013: a call of Release runs on a boxed copy

[NonCopyable]
struct Lease : IReleasable { public void Release() { } }   // OK
```

A generic struct that is non-copyable only through its type arguments, such as a tuple, has no declaration to put the warning on. So it gets the warning where it is bound to a type parameter that is constrained to the interface.

## Async and Iterator Members

An instance member of a non-copyable struct cannot be `async` or an iterator. The compiler copies `this` into the state machine of such a member, so it runs on a copy and what it changes is lost: an `async` `DisposeAsync` leaves the caller's guard as it was. The member gets a warning where it is declared (SCA1014).

- **A member that is not `async`** can change the struct and return the task of a static method.
- **A `readonly struct`** has nothing that a member could change, so its members can be `async` or iterators.
- **A `readonly` member of a struct that is not `readonly`** gets a warning all the same. It changes nothing, but it runs later, on a copy that is out of date by then.
- **The body of an iterator** runs when the sequence is enumerated, and by then its copy can still show a guard as held that has since been released. The clean form is a static iterator over what it needs.

```cs
// In a [NonCopyable] struct:
public async ValueTask DisposeAsync() { await ReleaseAsync(_lock); _lock = null; }                // SCA1014: clears a copy

public ValueTask DisposeAsync() { var owner = _lock; _lock = null; return ReleaseAsync(owner); }  // OK

public readonly IEnumerable<int> Ids { get { foreach (int id in _ids) yield return id; } }       // SCA1014: runs later, on a copy

public readonly IEnumerable<int> Ids => Enumerate(_ids);                                          // OK: a static iterator over the array
```

## Cloning

<xref:System.Object.MemberwiseClone> gets a warning (SCA1005) in a class that has an instance field of a non-copyable type, its own or inherited, and in a non-copyable struct. A field that holds a container, such as a `List<LockGuard>`, does not count: the clone shares the list.

```cs
class Holder
{
    private LockGuard _guard;
    public Holder Clone() => (Holder)MemberwiseClone();   // SCA1005: the clone has a second guard
}
```

A class that derives from a [class that clones itself](#base-classes-that-clone-themselves), such as <xref:System.Text.Encoding>, gets SCA1009 instead, since the call of `MemberwiseClone` is in code that the analyzer cannot read.

A class with no such field also gets a warning when a derived class in the same compilation has one. A class that another assembly can derive from gets a warning with an ID of its own (SCA1008), whatever fields it has, since a derived class there can hold a value of any type. The exact cases, such as `MemberwiseClone(bool)` of <xref:System.MarshalByRefObject> and a method group of either, are in the articles of [SCA1005](../diagnostics/sca1005.md) and [SCA1008](../diagnostics/sca1008.md).

## Base Classes That Clone Themselves

A class that clones itself clones its objects field by field with <xref:System.Object.MemberwiseClone> in code of its own, such as the `Clone` method of <xref:System.Text.Encoding>. The clone copies every field of a class that derives from it as well, and the analyzer cannot see the call in another assembly. So a `clones` line of the [symbol list](symbol-list-format.md#clones) names such a class: the [built-in framework symbol list](framework-symbol-list.md#classes-that-clone-themselves) has one for every class of the framework that clones itself, and a symbol list file names one of another library. A non-copyable value behind such a class gets SCA1009 in three places: a field, a binding and a use.

```cs
sealed class PinnedEncoding : UTF8Encoding
{
    private GCHandle _pinned;                            // SCA1009: Encoding's Clone copies the handle
    private readonly HandleHolder _holder = new();       // OK: the clone shares the object that holds a handle
}
```

A class of the project that derives from a class of a library that was not built with the analyzer gets the warning on such a field as well, until a symbol list line says what the base class does: see [A Base Class That Is Not Known](../diagnostics/sca1009.md#a-base-class-that-is-not-known).

#### Fields Behind the Base Class

A class of the project that derives from a class that clones itself, at any depth, gets the warning on each field that holds a non-copyable value:

| Field | Warning? |
|---|---|
| A field of a non-copyable type, a <xref:System.Runtime.InteropServices.GCHandle> included | Yes |
| A tuple or a struct that holds a non-copyable value | Yes |
| A field of a type parameter that is marked `[NonCopyable]` | Yes, with a text that says so: see below |
| A container, or an object of a class that holds the value | No: the clone shares it |
| A static field | No |
| A field that a call of `MemberwiseClone` in the project copies as well | Yes, and the call gets [SCA1005](../diagnostics/sca1005.md) |

The field of a marked type parameter gets the warning in the build of its own project, since a clone copies what is bound to it. Suppress it there only where the class's own code never clones its objects. A project that binds a non-copyable type to it gets the warning where it calls, on such an object, a member that clones, or where it hands the object to other code. A field of a type parameter that is not marked gets no warning, but each binding of a non-copyable type to it gets its own (see [Bindings](#bindings)).

#### Bindings

A non-copyable type that is bound to a type parameter of such a class gets the warning where the project writes the binding:

- as the base class of a class;
- in an object creation, one with a `dynamic` argument included.

```cs
// In a library: TaggedCulture<[NonCopyable] T> : CultureInfo holds a T in a field
sealed class GuardCulture : TaggedCulture<LockGuard>     // SCA1009: CultureInfo's Clone copies the guard
{
    public GuardCulture() : base("en-US") { }
}
```

A generic class of another library is judged by its type arguments, whether its type parameter is marked or not. Where the type parameter is not marked, a use of such a class of another library gets [SCA1020](../diagnostics/sca1020.md) as well.

#### Uses

The binding can also be in the type of a value, as in the `TaggedCulture<LockGuard>` that a call returns or a `new T()` makes, or in the base list of a class of another assembly. Such an object gets the warning where something can clone it:

- **A member that clones is called on it,** or is given it, such as `culture.Clone()` or `CultureInfo.ReadOnly(culture)`. A `clones` line says that a member clones its object, or the object that it is given, or hands it to code that can, as `PrinterSettings.DefaultPageSettings` does: the page settings that it returns refer back to the printer settings.
- **A member's signature shows that it can hand it on** as the class that clones itself, a class above it, `object` or an interface. A symbol list line for the member with no marker says that it never hands the object itself to other code.
  - *What comes out of the member,* its result, an `out` or `ref` parameter, or a property or field that can be read, counts where such a type is anywhere in its type, as in `Document WithTitle(string)`, `Task<Document>` or `List<Document>.Enumerator`.
  - *What goes in,* a parameter, a property or field that can be set, or the handler of an event where it is subscribed to, counts where something in its type can take such a type in. That is a delegate that takes one, as in `Visit(Action<Document>)` or the `object sender` of an `EventHandler`, or another type that can take one in, as in `SortWith(IComparer<Document>)` or `Fill(List<Document>)`. A delegate reverses the direction for its own parameters, so `Action<Action<Document>>`, which only gives the caller a place to send documents, does not count.
- **A member that no line names is called on it, and no `clones=complete` line makes known the members of the class that declares it.** Without such a line, any member of that class may be one that clones. Which line makes them known depends on where the declaring class stands:

  | The class that declares the member | Its assembly | The class whose line must say `clones=complete` |
  |---|---|---|
  | The class that clones itself, or a class above it, such as `object` | Any | The class that clones itself |
  | A class that writes the binding in its own base list, as `Bound : Tagged<GCHandle>` does | The assembly of the class that clones itself, not built with the analyzer | The class that clones itself, or that class |
  | A class that writes the binding in its own base list | Another assembly, not built with the analyzer | That class |
  | Any other class between the class that clones itself and the object's own class | The assembly of the class that clones itself, or one built with the analyzer | The class that clones itself, or the class between |
  | Any other class between | Another assembly, not built with the analyzer | The class between |

  The line can be in any symbol list file, and a plain `clones` line for any of these classes changes nothing. Two kinds of class are not in the table:

  - A class of the project, or of an assembly that was built with the analyzer, that writes the binding in its own base list. The analyzer judges it in its own build instead.
  - A generic class of an assembly that was not built with the analyzer whose binding comes from its own type argument alone, as `Far<Guard>` where `Far<T> : Doc`. Its members get [SCA1020](../diagnostics/sca1020.md) instead. Where a base list above such a class also binds a non-copyable type, as `Both<T> : Tagged<GCHandle>` does, the class is in the table for that binding.

  The framework's own classes that derive from a class that clones itself, such as <xref:System.Text.UTF8Encoding> or <xref:System.Globalization.GregorianCalendar>, have lines of their own in the [built-in framework symbol list](framework-symbol-list.md#classes-that-clone-themselves) that say `clones=complete`.
- **It is handed to other code as a type that hides its binding:** converted to the class that clones itself, a class above it, `object`, `dynamic` or an interface, written or not. The conversion can be in a variable, an argument or a return; in a `??`, a deconstruction or a spread; through variance; in a `foreach` variable, a pattern or a hole of an interpolated string; in the return of an override; or in the receiver of an extension that a pattern calls, such as a `Deconstruct` or a `GetAwaiter`. A binding of its type to a type parameter that is not marked, or to a marked one whose constraint is such a type, counts as well.

A line for a member says what that member does, not what an override of it does. A member declared in the class that binds the type, or in a class between it and the class that clones itself, is judged by its own line and its own signature. So an override of `GetFormat` that returns the culture itself gets the warning. A member of a base class can run an override too: see [Overrides That a Base Class Runs](#overrides-that-a-base-class-runs).

Any other member that it inherits gets no warning, as with a member of a container's own type. Where a class's line says `clones=complete`, the analyzer counts a member that no line names as one that neither clones the object nor hands it on. The lines must say what a signature cannot show, such as a member that stores the object in another object that it returns, or that gives it to a visitor interface, a registry or a wrapper class. A method group is a use of its member, and the `Target` of its delegate is not checked, as reflection is not:

```cs
// Cultures.Make<T>() creates a TaggedCulture<T>
var culture = Cultures.Make<LockGuard>();                // no warning: the type still shows the guard
_ = culture.Name;                                        // OK: the line of CultureInfo says clones=complete, and no line names Name
_ = culture.Parent;                                      // OK: the built-in list says that Parent never hands back the culture itself
var copy = culture.Clone();                              // SCA1009: Clone clones it
Func<string> name = culture.ToString;                    // OK: ToString neither clones it nor hands it on
Func<object> clone = culture.Clone;                      // SCA1009: a method group of Clone is a use of it
CultureInfo plain = culture;                             // SCA1009: hands the object to code that can clone it
```

The argument of a member on the symbol list gets the warning unless the line marks the member `borrows`, which says that it uses what it is given during the call and keeps nothing of it. `List<CultureInfo>.Add` keeps the culture, and so does `Task.FromResult`; `Monitor.Enter`, which `lock` calls, keeps nothing.

A container of such objects that is converted to a type that hides them gets the warning too. A class of the project that binds the type in its own base list, or whose base class does, gets the warning there, and its objects get none for the clone where they are used. Where that base list binds the type to a type parameter that is not marked, of a generic of the project, the conversion of its objects to the base class gets [SCA1023](../diagnostics/sca1023.md) as well, for the code of that generic.

Converting the object to `dynamic` gets the warning, and so does converting a container whose type shows the binding. What the dynamic binder does at run time with a member of a `dynamic` object is not checked, including the field of an object whose own type shows no binding: see [Reflection and Dynamic](not-covered.md#reflection-and-dynamic).

#### Overrides That a Base Class Runs

The code of a base class can call a virtual member of its own object, and the call runs the override of a class below it. `CultureInfo.GetFormat` reads `NumberFormat`, so it runs an override of `NumberFormat`. The analyzer cannot tell which member of a base class calls which virtual member, so it counts every member of that base class as one that can run the override. For an event of the base class, adding a handler and removing one both count.

```cs
// A library that was not built with the analyzer: Bound overrides NumberFormat with a getter that clones the culture,
// and a 'clones' line names the override
Bound culture = Cultures.MakeBound();
_ = culture.NumberFormat;                                // SCA1009: the line says that the override clones
_ = culture.GetFormat(typeof(NumberFormatInfo));         // SCA1009: GetFormat can run the override
_ = culture.Name;                                        // SCA1009: so can any other member of CultureInfo
_ = culture.GetType();                                   // OK: the code of object calls no virtual member
```

What a member of the base class gets depends on the override:

| The override | A member of the base class |
|---|---|
| A `clones` line names it | Gets the warning |
| It is of a library that was not built with the analyzer, and no line says what it does | Gets the warning until a line says that the override does not clone: a line for the override with no marker, or a `clones=complete` line that covers its class |
| It is of the project or of a library that was built with the analyzer, and no `clones` line names it | No warning |

The analyzer counts what the base class can run:

- **An override of a member that the base class has,** as a member of its own or of a class above it.
- **A member that implements a member of an interface** in a class below. The base class can use its own object as an interface, even one that it does not implement itself.
- **What those members can run in turn.** An override in a class between can call a virtual member of its own class, which a class further down overrides.

Three kinds of member do not count:

- **A member of the class that clones itself, or of a class above it.** The `clones` lines of that class say what the members of those classes do. There is one exception: an override there that a `clones` line names, such as a `CloneCore` that the class overrides with a clone. A member of a class above it that can run it gets the warning until a line for that member says whether it does.
- **An override of a member that clones already,** such as an override of `Clone`. The `clones` lines name the members that run it.
- **An override in a class of the framework.** The lines of the built-in framework symbol list cover those, and a `clones` line of your own for one changes nothing for the members of its base class. A framework class between still passes a call on, so an override of one of its virtual members in a class below it counts.

A constructor of a base class can run an override too. That needs no warning of its own: the place that creates the object binds the type, and gets the warning there (see [Bindings](#bindings)).

#### Which Warning a Use Gets

Each reason has one ID:

| What the code does | Warning |
|---|---|
| Calls `MemberwiseClone` on an object or a struct that holds a non-copyable value | [SCA1005](../diagnostics/sca1005.md) |
| Holds or binds a non-copyable value behind a base class that clones itself, or uses such an object as [Uses](#uses) describes, a binding to a type parameter that is not marked included | [SCA1009](../diagnostics/sca1009.md) |
| Declares a member that clones its own object and has no `clones` line, in a library's class that clones itself and that another project can derive from or bind a type to | [SCA1008](../diagnostics/sca1008.md) |
| Uses a member of a generic whose type parameter is not marked, with a non-copyable type bound, for what the member does with the value | [SCA1020](../diagnostics/sca1020.md) |
| Uses a container as a type that hides it where no binding to a class that clones itself is involved, or converts an object of a class of the project to its base class where its binding is to a type parameter that is not marked | [SCA1023](../diagnostics/sca1023.md) |

A use gets two warnings only where it has two reasons, such as `new TaggedPlain<LockGuard>()` of a library whose type parameter is not marked: SCA1009 for the binding, and SCA1020 for the constructor's own code. A `#pragma` or a suppression attribute for SCA1005 does not cover SCA1009.

What the lines of a class that clones itself vouch for, and how to write them: [Clones](symbol-list-format.md#clones).

</div>
