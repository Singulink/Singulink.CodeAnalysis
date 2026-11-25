using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;

namespace Singulink.CodeAnalysis.Analyzers;

/// <summary>
/// The budget of one walk over types. Every walk over types makes one and looks at each type through it, so that what a walk costs is
/// bounded whatever the shape of the types: a walk looks at no more than <see cref="MaxTypes"/> types, and at no type with more parts than
/// <see cref="MaxPartsPerType"/>.
/// </summary>
/// <remarks>
/// <para>
/// A count of the steps alone does not bound the work. A generic can name a deeper construction of itself in its base list, as
/// <c>class C&lt;T&gt; : IEquatable&lt;C&lt;C&lt;T&gt;&gt;&gt;</c> does, so a walk that follows base types and interfaces into their
/// type arguments meets a new type at every step, and with <c>class X&lt;T&gt; : IEquatable&lt;X&lt;(T, T)&gt;&gt;</c> each type has
/// twice the parts of the one before. The compiler shares the parts that repeat, so making such a type is cheap. Comparing two of them
/// that are equal and were made apart is not: the compiler compares them part by part, along every path, so the fortieth type costs
/// 2^40 steps. A visited set and a cache compare what they hold, so a walk must not let such a type in at all.
/// </para>
/// <para>
/// So the parts of each type are counted before a walk looks at it, by a count that stops at the limit and costs no more than that. A
/// type within the limit is compared with any other type in no more steps than its parts. A walk that reaches either limit is at its
/// bound: from then on it looks at nothing, and it gives the answer that leads to a report, or gives up with a
/// <see cref="TypeTooLargeException"/> where its answer has to name what it found. In all, one walk counts no more than
/// <see cref="MaxTypes"/> times <see cref="MaxPartsPerType"/> parts.
/// </para>
/// <para>
/// A cache of the analyzer keys a type by an identity that equal types share (<see cref="TypeIdentities"/>), which it makes only for a type
/// within the limit (<see cref="Fits(ITypeSymbol)"/>). The count and a hash of the parts of each instance of a type are kept, so counting
/// a type whose parts were counted before costs one step.
/// </para>
/// </remarks>
internal sealed class TypeWalk
{
    /// <summary>
    /// The most types that one walk looks at.
    /// </summary>
    public const int MaxTypes = 512;

    /// <summary>
    /// The most parts of a type that a walk looks at: the type itself, its type arguments and those of the types that contain it, the
    /// element type of an array, what a pointer points at and the types of the signature of a function pointer, at every depth, each
    /// time that it is met. A type nested more deeply than this has more parts.
    /// </summary>
    public const int MaxPartsPerType = 512;

    // The count and the hash code of the parts of each type that was counted: see Measure.
    private static readonly ConditionalWeakTable<ITypeSymbol, Measured> s_measured = new();

    private int _types;

    /// <summary>
    /// Gets the comparer of the sets of types that a walk keeps, and of the types that a report lists. It compares types as
    /// <see cref="SymbolEqualityComparer.Default"/> does, and hashes them by their parts, up to <see cref="MaxPartsPerType"/> of them.
    /// </summary>
    /// <remarks>
    /// The compiler hashes every construction of a generic as its definition, so a set of the default comparer that holds many
    /// constructions of one generic, as a walk over <c>C&lt;C&lt;C&lt;T&gt;&gt;&gt;</c> does,
    /// compares each new one with all the others, part by part. A hash of the parts keeps those apart, and costs no more than the count
    /// of the parts that a walk makes before it looks at a type.
    /// </remarks>
    public static IEqualityComparer<ITypeSymbol> Comparer { get; } = new PartsComparer();

    /// <summary>
    /// Gets the comparer of a cache of members as they are constructed: as <see cref="SymbolEqualityComparer.Default"/> compares them,
    /// with a hash of the parts of the types that contain them and of their type arguments (see <see cref="Comparer"/>).
    /// </summary>
    public static IEqualityComparer<ISymbol> MemberComparer { get; } = new PartsMemberComparer();

    /// <summary>
    /// Gets a value indicating whether the walk has reached its bound. It looks at nothing more then.
    /// </summary>
    public bool IsAtBound { get; private set; }

    /// <summary>
    /// Gets a value indicating whether a type has no more parts than a walk looks at, which a cache asks before it keeps the type by
    /// what it is made of.
    /// </summary>
    public static bool Fits(ITypeSymbol type) => CountParts(type, MaxPartsPerType) <= MaxPartsPerType;

    /// <summary>
    /// Gets a value indicating whether the types that a member mentions as it is constructed have no more parts than a walk looks at: the
    /// types that contain it and its type arguments, which a cache of members compares.
    /// </summary>
    public static bool Fits(ISymbol member)
    {
        if (member.ContainingType is { } containing && !Fits(containing))
            return false;

        if (member is IMethodSymbol method)
        {
            foreach (var argument in method.TypeArguments)
            {
                if (!Fits(argument))
                    return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Counts the parts of a type, and stops at one more than the limit, which is at most <see cref="MaxPartsPerType"/>.
    /// </summary>
    public static int CountParts(ITypeSymbol type, int limit) => Math.Min(Measure(type, limit).Count, limit + 1);

    /// <summary>
    /// Gets a hash code of a type from the hash codes of its parts. Types that are equal have the same parts, so they have the same hash
    /// code. A type with more parts than a walk looks at is hashed by those that it counted before it stopped, since no cache keeps one.
    /// </summary>
    public static int GetPartsHashCode(ITypeSymbol type) => Measure(type, MaxPartsPerType).Hash;

    // Counts the parts of a type up to a budget, with a hash code of the parts that it counted. A count above the budget means that the
    // type has more parts than the budget, and that the count stopped there. What it finds is remembered for the instance of the symbol,
    // and so is used for every type that the instance is a part of: a type nested 400 levels deep is counted once, and not once for each
    // of its levels at each look of a walk. A type with more parts than a walk looks at is remembered as such where its whole budget
    // was spent on it. The table holds the symbols weakly, so it keeps no compilation alive.
    private static Measured Measure(ITypeSymbol type, int budget)
    {
        type = GetKeptInstance(type);

        if (s_measured.TryGetValue(type, out var known))
            return known;

        RuntimeHelpers.EnsureSufficientExecutionStack();

        int count = 1;
        int hash = unchecked((17 * 31) + SymbolEqualityComparer.Default.GetHashCode(type));

        switch (type)
        {
            case INamedTypeSymbol named:
                foreach (var argument in named.TypeArguments)
                    Add(argument);

                if (named.ContainingType is { } containing)
                    Add(containing);

                break;

            case IArrayTypeSymbol array:
                Add(array.ElementType);
                break;

            case IPointerTypeSymbol pointer:
                Add(pointer.PointedAtType);
                break;

            case IFunctionPointerTypeSymbol functionPointer:
                Add(functionPointer.Signature.ReturnType);

                foreach (var parameter in functionPointer.Signature.Parameters)
                    Add(parameter.Type);

                break;
        }

        var measured = new Measured(count, hash);

        // A count within the budget is complete. One above it is known to be above the limit only where the budget was the limit.
        if (count <= budget || budget >= MaxPartsPerType)
            measured = s_measured.GetValue(type, _ => count <= budget ? measured : new Measured(MaxPartsPerType + 1, hash));

        return measured;

        void Add(ITypeSymbol part)
        {
            if (count > budget)
                return;

            var partMeasured = Measure(part, budget - count);
            count += partMeasured.Count;
            hash = unchecked((hash * 31) + partMeasured.Hash);
        }
    }

    /// <summary>
    /// Looks at a type: counts it, and its parts. False, and the walk is at its bound, where it has looked at as many types as it looks at
    /// or the type has more parts than a walk looks at; the walk must then not compare the type, keep it or go into it.
    /// </summary>
    public bool LookAt(ITypeSymbol type)
    {
        if (IsAtBound)
            return false;

        if (++_types > MaxTypes || CountParts(type, MaxPartsPerType) > MaxPartsPerType)
        {
            IsAtBound = true;
            return false;
        }

        return true;
    }

    /// <summary>
    /// Counts a step of a walk that looks at no type in it, such as a constructor or a type parameter that is followed through its
    /// constraints. False, and the walk is at its bound, where it has taken as many steps as it looks at types.
    /// </summary>
    public bool Step()
    {
        if (IsAtBound)
            return false;

        if (++_types > MaxTypes)
        {
            IsAtBound = true;
            return false;
        }

        return true;
    }

    private sealed class PartsComparer : IEqualityComparer<ITypeSymbol>
    {
        public bool Equals(ITypeSymbol? x, ITypeSymbol? y) => SymbolEqualityComparer.Default.Equals(x, y);

        public int GetHashCode(ITypeSymbol obj) => GetPartsHashCode(obj);
    }

    private sealed class PartsMemberComparer : IEqualityComparer<ISymbol>
    {
        public bool Equals(ISymbol? x, ISymbol? y) => SymbolEqualityComparer.Default.Equals(x, y);

        public int GetHashCode(ISymbol obj)
        {
            int hash = SymbolEqualityComparer.Default.GetHashCode(obj.OriginalDefinition);

            if (obj.ContainingType is { } containing)
                hash = unchecked((hash * 31) + GetPartsHashCode(containing));

            if (obj is IMethodSymbol method)
            {
                foreach (var argument in method.TypeArguments)
                    hash = unchecked((hash * 31) + GetPartsHashCode(argument));
            }

            return hash;
        }
    }

    /// <summary>
    /// Gets the instance of a type that the compiler keeps for it, which what is kept for an instance is keyed by. The compiler makes a new
    /// instance each time that it hands out a type with a nullable annotation other than the one that the type has where nothing is said,
    /// as a type argument of an interface of an annotated library can have. A member of an extension block has none other to give.
    /// </summary>
    public static ITypeSymbol GetKeptInstance(ITypeSymbol type)
    {
        var annotation = type.IsValueType ? NullableAnnotation.NotAnnotated : NullableAnnotation.None;

        if (type.NullableAnnotation == annotation)
            return type;

        try
        {
            return type.WithNullableAnnotation(annotation);
        }
        catch (NotSupportedException)
        {
            return type;
        }
    }

    private sealed class Measured(int count, int hash)
    {
        public int Count { get; } = count;

        public int Hash { get; } = hash;
    }
}
