namespace Singulink.CodeAnalysis;

/// <summary>
/// Marks a <see langword="ref"/> parameter that the method takes the value out of, leaving a fresh value behind, as
/// <see cref="NonCopyable.Move{T}(ref T)"/> does.
/// </summary>
/// <remarks>
/// <para>
/// Inside the method, reading the parameter by value is a move and not a copy. The method must assign the parameter and can read it by value only
/// once, and the attribute has no meaning on anything that is not a <see langword="ref"/> parameter: the analyzer warns about each of these.</para>
/// <para>
/// The mark is kept in both directions: an override, an implementation, or a method or a lambda that is converted to a delegate must have
/// it exactly where the member or the delegate that it stands in for has it. A method with a marked parameter of a container type, such
/// as a <c>Task&lt;T&gt;</c> of a non-copyable type, may leave in it only a container that its caller can own.</para>
/// <para>
/// The analyzer recognizes the attribute by its full name, <c>Singulink.CodeAnalysis.MovedFromAttribute</c>, in any assembly, public or
/// internal.</para>
/// </remarks>
[AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
public sealed class MovedFromAttribute : Attribute;
