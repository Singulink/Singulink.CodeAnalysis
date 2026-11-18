namespace Singulink.CodeAnalysis;

/// <summary>
/// Marks the return value of a method that keeps the container that it returns, such as a list of non-copyable values that it reads from a field.
/// </summary>
/// <remarks>
/// <para>
/// The result of a call is a new value, so a method must not return a container that something else still holds: the analyzer reports it. With
/// this attribute the method may, and its callers treat the result as stored, as they treat what a property returns: they can use it in place,
/// and a by-value read of a non-copyable value from it is reported.</para>
/// <para>
/// An override or an implementation cannot have the attribute where the member that it replaces does not, since a caller through that member takes
/// the result for a new value.</para>
/// <para>
/// The analyzer recognizes the attribute by its full name, <c>Singulink.CodeAnalysis.BorrowedAttribute</c>, in any assembly, public or
/// internal.</para>
/// </remarks>
[AttributeUsage(AttributeTargets.ReturnValue, Inherited = false)]
public sealed class BorrowedAttribute : Attribute;
