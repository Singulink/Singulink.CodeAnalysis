namespace Singulink.CodeAnalysis;

/// <summary>
/// Marks a member of a generic type or a generic method that copies values of the type parameters that are marked with
/// <see cref="NonCopyableAttribute"/>, such as a <c>ToArray</c> method.
/// </summary>
/// <remarks>
/// <para>
/// Inside the member the marked type parameters count as copyable, so its body is not checked for them. Using the member with a non-copyable type
/// bound to one of them gets an error; with a copyable type it is fine.</para>
/// <para>
/// A member that overrides or implements a member without this attribute must not have it. The analyzer recognizes the attribute by its
/// full name, <c>Singulink.CodeAnalysis.RequiresCopyableAttribute</c>, in any assembly, public or internal.</para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Constructor | AttributeTargets.Property | AttributeTargets.Event, Inherited = false)]
public sealed class RequiresCopyableAttribute : Attribute;
