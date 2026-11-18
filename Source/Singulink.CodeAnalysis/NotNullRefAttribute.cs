namespace Singulink.CodeAnalysis;

/// <summary>
/// Marks a <see langword="ref"/>, <see langword="in"/>, <c>ref readonly</c> or <see langword="out"/> parameter of a method that
/// returns only if the reference that it is given refers to something: given a null reference, as <c>Unsafe.NullRef</c> makes one, the method
/// throws.
/// </summary>
/// <remarks>
/// <para>
/// A read or a write through a null reference fails, so the analyzer counts a read or a write through a reference as able to throw where it
/// asks what can: between the read and the assignment of a <c>[MovedFrom]</c> parameter, and after a value was committed at the end of a
/// <see langword="try"/> block. After a call of a method with this attribute that returns, the reference that was passed to the marked
/// parameter is known to refer to something. <see cref="NonCopyable.ThrowIfNullRef"/> and <see cref="NonCopyable.ThrowIfNullRefArgument"/>
/// are such methods.</para>
/// <para>
/// The attribute counts where a call names the code that runs: on a method that is not virtual, abstract or an override, static or not, an
/// extension method included, and on a local function. Anywhere else it is reported.</para>
/// <para>
/// The analyzer does not check the body of the method. The attribute is a statement of its author, and every use of it in a project is
/// reported once, to be suppressed there when the method has been verified and tested.</para>
/// <para>
/// The analyzer recognizes the attribute by its full name, <c>Singulink.CodeAnalysis.NotNullRefAttribute</c>, in any assembly, public or
/// internal.</para>
/// </remarks>
[AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
public sealed class NotNullRefAttribute : Attribute;
