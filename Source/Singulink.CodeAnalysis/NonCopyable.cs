using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Singulink.CodeAnalysis;

/// <summary>
/// Provides the explicit move of a non-copyable value, and the check that a reference refers to something.
/// </summary>
public static class NonCopyable
{
    /// <summary>
    /// Takes the value out of a variable, a field or an array element and leaves the default value behind, so that the value can be handed on
    /// without a copy: stored in a field, passed by value, returned or awaited later.
    /// </summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <param name="value">The location to take the value out of. It holds the default value of <typeparamref name="T"/> afterwards.</param>
    /// <returns>The value that <paramref name="value"/> held.</returns>
    /// <remarks>
    /// A second use of the location hits a default value and not a second instance. A location that cannot be passed by reference, such as a
    /// <see langword="using"/> variable or a read-only field, cannot be moved from: something else still owns its value.
    /// </remarks>
    public static T Move<[NonCopyable] T>([MovedFrom, MaybeNull] ref T value)
#if NET10_0_OR_GREATER
        where T : allows ref struct
#endif
    {
        T moved = value;
        value = default!;
        return moved;
    }

    /// <summary>
    /// Throws a <see cref="NullReferenceException"/> if a reference is a null reference, as <see cref="Unsafe.NullRef{T}"/> makes one, and
    /// does nothing else.
    /// </summary>
    /// <typeparam name="T">The type of what the reference refers to.</typeparam>
    /// <param name="reference">The reference to check.</param>
    /// <param name="expression">The expression that gives the reference, which the compiler fills in.</param>
    /// <exception cref="NullReferenceException"><paramref name="reference"/> is a null reference.</exception>
    /// <remarks>
    /// <para>
    /// A read or a write through a null reference fails with a <see cref="NullReferenceException"/>, which is why the analyzer counts a
    /// read or a write through a reference as able to throw where it asks what can: between the read and the assignment of a
    /// <c>[MovedFrom]</c> parameter, and after a value was committed at the end of a <see langword="try"/> block. After a call of this
    /// method the reference is known to refer to something.</para>
    /// <para>
    /// Use this one for <see langword="this"/> in a struct (<c>NonCopyable.ThrowIfNullRef(in this)</c>), for a <see langword="ref"/> local,
    /// and wherever a null reference is not the fault of an argument: the method then fails as it would have failed at its first use of
    /// the reference, only earlier. For a parameter of a public method, <see cref="ThrowIfNullRefArgument"/> names the argument.</para>
    /// </remarks>
    public static void ThrowIfNullRef<[NonCopyable] T>([NotNullRef] ref readonly T reference, [CallerArgumentExpression(nameof(reference))] string? expression = null)
#if NET10_0_OR_GREATER
        where T : allows ref struct
#endif
    {
#if NET10_0_OR_GREATER
        if (Unsafe.IsNullRef(in reference))
#else
        if (Unsafe.IsNullRef(ref Unsafe.AsRef(in reference)))
#endif
            ThrowNullReference(expression);
    }

    /// <summary>
    /// Throws an <see cref="ArgumentNullException"/> that names the argument if a reference is a null reference, as
    /// <see cref="Unsafe.NullRef{T}"/> makes one, and does nothing else.
    /// </summary>
    /// <typeparam name="T">The type of what the reference refers to.</typeparam>
    /// <param name="argument">The reference to check.</param>
    /// <param name="paramName">The name of the argument, which the compiler fills in.</param>
    /// <exception cref="ArgumentNullException"><paramref name="argument"/> is a null reference.</exception>
    /// <remarks>
    /// Use this one for a <see langword="ref"/>, <see langword="in"/> or <see langword="out"/> parameter of a public method, where a null
    /// reference is the fault of the caller. Everywhere else <see cref="ThrowIfNullRef"/> is the one to use. After a call of this method
    /// the reference is known to refer to something, as after a call of that one.
    /// </remarks>
    public static void ThrowIfNullRefArgument<[NonCopyable] T>([NotNullRef] ref readonly T argument, [CallerArgumentExpression(nameof(argument))] string? paramName = null)
#if NET10_0_OR_GREATER
        where T : allows ref struct
#endif
    {
#if NET10_0_OR_GREATER
        if (Unsafe.IsNullRef(in argument))
#else
        if (Unsafe.IsNullRef(ref Unsafe.AsRef(in argument)))
#endif
            ThrowArgumentNull(paramName);
    }

    // The exceptions are created and thrown outside the two methods, which stay small enough to be inlined into their callers, and
    // outside generic code, so that there is one copy of each for every type.
    private static void ThrowNullReference(string? expression) => throw new NullReferenceException($"The reference '{expression}' is a null reference.");

    private static void ThrowArgumentNull(string? paramName) => throw new ArgumentNullException(paramName);
}
