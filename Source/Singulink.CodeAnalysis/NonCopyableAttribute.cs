namespace Singulink.CodeAnalysis;

/// <summary>
/// Marks a struct that must not be copied, such as one that holds a lock or another resource that only one instance may own, or a type parameter
/// whose values a generic type or method does not copy. The rules of the Singulink.CodeAnalysis.Analyzers package, which this package depends on,
/// report an error for code that copies a value of a marked type.
/// </summary>
/// <remarks>
/// <para>
/// The analyzer recognizes the attribute by its full name, <c>Singulink.CodeAnalysis.NonCopyableAttribute</c>, in any assembly, public or
/// internal, so a project can declare its own in that namespace instead of using this one. An attribute of the name in another namespace
/// is not recognized on what the project declares, and gets a warning. On a struct of a referenced assembly an attribute of the name is
/// recognized whatever its namespace.</para>
/// <para>
/// A field of a non-copyable type can only be declared in a class or in a struct that is itself non-copyable, and not in a record, which
/// copies its fields in <see langword="with"/> and in the members that the compiler writes for it. A non-copyable type cannot be the type
/// of an auto-property.</para>
/// <para>
/// On a type parameter, the mark makes the type parameter a non-copyable type inside the generic that declares it, so the generic is checked like
/// any other code, and callers can use it with a non-copyable type. A member of the generic that has to copy is marked with
/// <see cref="RequiresCopyableAttribute"/>.</para>
/// </remarks>
[AttributeUsage(AttributeTargets.Struct | AttributeTargets.GenericParameter, Inherited = false)]
public sealed class NonCopyableAttribute : Attribute;
