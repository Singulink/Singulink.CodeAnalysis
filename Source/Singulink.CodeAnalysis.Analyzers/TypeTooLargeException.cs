namespace Singulink.CodeAnalysis.Analyzers;

/// <summary>
/// Thrown by a walk over the parts of a type that reaches its bound (<see cref="TypeWalk"/>) where its answer has to name what it found,
/// as the binding that a report names. Such a walk follows the type arguments of a type and not what it derives from, so only a type with
/// more parts than a walk looks at reaches the bound, as one that the compiler infers through a long chain of calls can be. The check of
/// the member or of the declaration that asked stops, and gets SCA1048, as one that a recursion over code stops does.
/// </summary>
internal sealed class TypeTooLargeException : Exception
{
    public TypeTooLargeException()
        : base("A type has more parts than a walk of the analyzer looks at.")
    {
    }

    public TypeTooLargeException(string message)
        : base(message)
    {
    }

    public TypeTooLargeException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
