using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;

namespace Singulink.CodeAnalysis.Analyzers;

/// <summary>
/// A cache of answers about types, for one compilation, keyed by the identity of a type (<see cref="TypeIdentities"/>): a type that is
/// equal to one that was asked about finds its answer with no comparison of the two.
/// </summary>
internal sealed class TypeCache<TValue>(TypeIdentities identities)
{
    private readonly ConcurrentDictionary<object, TValue> _answers = new(TypeIdentities.InstanceComparer);

    /// <summary>
    /// Gets the answer that the cache keeps for a type, if it keeps one.
    /// </summary>
    public bool TryGetValue(ITypeSymbol type, out TValue value) => _answers.TryGetValue(identities.Of(type), out value);

    /// <summary>
    /// Keeps the answer for a type.
    /// </summary>
    public void Set(ITypeSymbol type, TValue value) => _answers[identities.Of(type)] = value;
}

/// <summary>
/// Gives each type of one compilation an identity that equal types share, which a cache is keyed by in place of the type.
/// </summary>
/// <remarks>
/// <para>
/// The compiler makes a type anew wherever it is written or substituted, hashes every construction of a generic as its definition, and
/// compares two types that are equal and were made apart part by part. A cache keyed by the type would compare a type nested hundreds of
/// levels deep with one that was kept from another field at each level that a walk asks about: a cost of the square of the depth at each
/// walk. So the identity of a type is made from the identities of its parts, from the innermost out, and is kept for the instance of the
/// type, which the compiler keeps as long as the type that it is a part of: each instance is looked at once, and each lookup after that
/// is one of the instance, with no comparison.
/// </para>
/// <para>
/// The identity of a constructed type is that of its definition, of its type arguments and of the type that contains it; of an array,
/// its rank and its element type; of a pointer, what it points at. Nullable annotations and the names of tuple elements play no part,
/// as in <see cref="SymbolEqualityComparer.Default"/>, and nothing that the analyzer works out about a type depends on them. A type
/// parameter, <c>dynamic</c>, a function pointer and a type that is in error are their own identity, by the default comparer. A type with
/// more parts than a walk looks at (<see cref="TypeWalk.Fits(ITypeSymbol)"/>) is its own identity by its instance, so that the identity is
/// made with no more recursion than a walk counts.
/// </para>
/// </remarks>
internal sealed class TypeIdentities
{
    // Markers that stand for the kind of a type that has no definition to stand for it: an array of each rank, and a pointer.
    private static readonly object PointerMarker = new();
    private static readonly ConcurrentDictionary<int, object> ArrayMarkers = new();

    private readonly ConcurrentDictionary<ITypeSymbol, object> _ofInstance = new(InstanceComparer);
    private readonly ConcurrentDictionary<Shape, object> _ofShape = new();
    private readonly ConcurrentDictionary<ITypeSymbol, object> _ofLeaf = new(SymbolEqualityComparer.Default);

    /// <summary>
    /// Gets the comparer of objects by reference, which an identity is compared with.
    /// </summary>
    public static IEqualityComparer<object> InstanceComparer { get; } = new ReferenceComparer();

    /// <summary>
    /// Gets the identity of a type. Types that are equal have the same identity, unless one has more parts than a walk looks at.
    /// </summary>
    public object Of(ITypeSymbol type)
    {
        var instance = TypeWalk.GetKeptInstance(type);

        if (_ofInstance.TryGetValue(instance, out var identity))
            return identity;

        return _ofInstance.GetOrAdd(instance, TypeWalk.Fits(instance) ? Make(instance) : instance);
    }

    // Makes the identity of a type that fits, from those of its parts, which fit as well: the recursion goes no deeper than its parts.
    private object Make(ITypeSymbol type)
    {
        RuntimeHelpers.EnsureSufficientExecutionStack();

        switch (type)
        {
            case INamedTypeSymbol { TypeKind: not TypeKind.Error } named:
                var parts = new List<object>(named.TypeArguments.Length + 1);

                foreach (var argument in named.TypeArguments)
                    parts.Add(Of(argument));

                if (named.ContainingType is { } containing)
                    parts.Add(Of(containing));

                return _ofShape.GetOrAdd(new Shape(named.OriginalDefinition, [.. parts]), type);

            case IArrayTypeSymbol array:
                var marker = ArrayMarkers.GetOrAdd(array.Rank, static _ => new object());
                return _ofShape.GetOrAdd(new Shape(marker, [Of(array.ElementType)]), type);

            case IPointerTypeSymbol pointer:
                return _ofShape.GetOrAdd(new Shape(PointerMarker, [Of(pointer.PointedAtType)]), type);

            default:
                return _ofLeaf.GetOrAdd(type, type);
        }
    }

    // What a type is made of: what stands for its kind, its definition or a marker, and the identities of its parts, all compared by
    // reference.
    private readonly struct Shape(object head, object[] parts) : IEquatable<Shape>
    {
        private readonly object _head = head;
        private readonly object[] _parts = parts;

        public bool Equals(Shape other)
        {
            if (!ReferenceEquals(_head, other._head) || _parts.Length != other._parts.Length)
                return false;

            for (int i = 0; i < _parts.Length; i++)
            {
                if (!ReferenceEquals(_parts[i], other._parts[i]))
                    return false;
            }

            return true;
        }

        public override bool Equals(object? obj) => obj is Shape other && Equals(other);

        public override int GetHashCode()
        {
            int hash = RuntimeHelpers.GetHashCode(_head);

            foreach (object part in _parts)
                hash = unchecked((hash * 31) + RuntimeHelpers.GetHashCode(part));

            return hash;
        }
    }

    private sealed class ReferenceComparer : IEqualityComparer<object>
    {
        public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);

        public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
    }
}
