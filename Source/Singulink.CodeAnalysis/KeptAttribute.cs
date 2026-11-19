namespace Singulink.CodeAnalysis;

/// <summary>
/// Marks a parameter whose container the method keeps: it stores the list, the task or the array that it is given in a field, an element or a
/// property, captures it, or passes it on to something that keeps it.
/// </summary>
/// <remarks>
/// <para>
/// What a method is given, it only uses during the call, unless it says otherwise. A caller that passes a container which it created to a method
/// still has the only reference afterwards, and may return it or move it as its own. With this attribute the method keeps the container, and the
/// caller's variable stops being its own. A method that keeps a container parameter without the attribute gets a warning from the analyzer.</para>
/// <para>
/// The parameters of a constructor and of the setter of a property or an indexer are taken to be kept and need no attribute. An override or an
/// implementation cannot have the attribute where the member that it replaces does not.</para>
/// <para>
/// The analyzer recognizes the attribute by its full name, <c>Singulink.CodeAnalysis.KeptAttribute</c>, in any assembly, public or
/// internal.</para>
/// </remarks>
[AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
public sealed class KeptAttribute : Attribute;
