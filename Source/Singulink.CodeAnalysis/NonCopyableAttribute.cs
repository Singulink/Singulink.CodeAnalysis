namespace Singulink.CodeAnalysis;

/// <summary>
/// Marks a struct that must not be copied, such as one that holds a lock or another resource that only one instance may own, or a type parameter
/// whose values a generic type or method does not copy. The rules of the Singulink.CodeAnalysis.Analyzers package, which this package depends on,
/// report code that copies a value of a marked type.
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
public sealed class NonCopyableAttribute : Attribute
{
    private bool? _unsafeToCopy;

    /// <summary>
    /// Gets or sets a value indicating whether a copy of a value of the marked type can break memory safety, as opposed to the logic of
    /// the program. On a struct it says that a copy of the struct can; on a type parameter it says whether the generic takes a type
    /// whose copy can.
    /// </summary>
    /// <remarks>
    /// <para>
    /// On a struct, set it to <see langword="true"/> where a copy can free, unpin or reach memory twice or after it is gone: a struct that
    /// owns a native handle or a block of native memory and frees it in <c>Dispose</c>, or one that pins memory that a pointer still
    /// points into. Leave it off where a copy only breaks the logic of the program, as with a lock guard, whose copy releases the lock
    /// twice. Leaving it off and setting it to <see langword="false"/> mean the same on a struct. A struct that declares an instance field
    /// of a type that is unsafe to copy must set it to <see langword="true"/>: a copy of the struct copies the field.</para>
    /// <para>
    /// On a type parameter, leaving it off or setting it to <see langword="true"/> makes the type parameter unsafe to copy inside the
    /// generic, so that the generic takes any non-copyable type. Setting it to <see langword="false"/> makes the type parameter a plain
    /// non-copyable type inside the generic, and the generic then takes only types that are not unsafe to copy.</para>
    /// <para>
    /// Under the current memory safety rules of C# the property changes nothing: every copy of a non-copyable value gets a warning. Under
    /// the updated memory safety rules a copy of a value of a type that is unsafe to copy is an error, which only an unsafe context, or
    /// the modifier <c>safe</c> or <c>unsafe</c> on a declaration, turns into a warning that can be suppressed. A copy of any other
    /// non-copyable value gets the same warning as under the current rules.</para>
    /// <para>
    /// The getter throws where the property was not set, since on a type parameter not setting it differs from setting it to
    /// <see langword="false"/>. Use <see cref="GetUnsafeToCopy"/> to read a value that may not be set.</para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">The property was not set.</exception>
    public bool UnsafeToCopy
    {
        get => _unsafeToCopy ?? throw new InvalidOperationException(NotSetMessage);
        set => _unsafeToCopy = value;
    }

    private static string NotSetMessage => $"The {nameof(UnsafeToCopy)} property was not set. Use {nameof(GetUnsafeToCopy)}() to read a value that may not be set.";

    /// <summary>
    /// Gets the value of <see cref="UnsafeToCopy"/>, or <see langword="null"/> where it was not set.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> or <see langword="false"/> where the property was set. <see langword="null"/> where it was not, which on a
    /// struct means the same as <see langword="false"/>, and on a type parameter the same as <see langword="true"/>.
    /// </returns>
    public bool? GetUnsafeToCopy() => _unsafeToCopy;
}
