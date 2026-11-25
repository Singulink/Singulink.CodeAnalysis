using System.Text;
using Microsoft.CodeAnalysis;

namespace Singulink.CodeAnalysis.Analyzers;

/// <summary>
/// Formats a symbol for a message. The compiler formats a type by recursion over its type arguments, with no guard against running out of
/// stack, and a type that it infers through a long chain of calls can be nested thousands of levels deep, which ends the process. A type
/// that repeats a part, as a tuple of a tuple of a tuple does, can also have more paths through its parts than the text can hold. So a
/// symbol that mentions a type nested more deeply than <see cref="MaxNesting"/>, or with more parts than <see cref="MaxParts"/>, is
/// shown in short: its outer levels, with "..." for the rest. Every symbol that the analyzer shows is formatted here.
/// </summary>
internal static class SymbolText
{
    /// <summary>
    /// The deepest nesting of type arguments that is shown in full.
    /// </summary>
    public const int MaxNesting = 32;

    /// <summary>
    /// The most parts of the types of a symbol that are shown in full.
    /// </summary>
    public const int MaxParts = 256;

    // How deep and how many parts a type that is shown in short is shown with.
    private const int ShortNesting = 4;
    private const int ShortParts = 32;

    private static readonly SymbolDisplayFormat NameOnly = SymbolDisplayFormat.CSharpErrorMessageFormat
        .WithGenericsOptions(SymbolDisplayGenericsOptions.None)
        .WithMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

    /// <summary>
    /// Gets the text of a symbol in the given format, or in the format of the compiler's messages, which is what a symbol among the
    /// arguments of a diagnostic shows as. A symbol that mentions a type that is too large to show in full is shown in short.
    /// </summary>
    public static string Of(ISymbol symbol, SymbolDisplayFormat? format = null)
    {
        if (!IsTooLargeToShow(symbol))
            return symbol.ToDisplayString(format);

        // A member is shown as it is declared, where that is small enough: its type parameters in the place of what is bound to them.
        if (symbol is not ITypeSymbol type)
            return symbol.OriginalDefinition is { } definition && !IsTooLargeToShow(definition) ? definition.ToDisplayString(format) : symbol.Name;

        var text = new StringBuilder();
        int parts = 0;
        AppendShort(text, type, ShortNesting, ref parts);
        return text.ToString();
    }

    /// <summary>
    /// Replaces each symbol among the arguments of a message that is too large to show in full with its text in short. The others stay
    /// symbols, which the compiler formats as it formats them everywhere.
    /// </summary>
    public static object?[] ShortenArguments(object?[] arguments)
    {
        object?[]? shortened = null;

        for (int i = 0; i < arguments.Length; i++)
        {
            if (arguments[i] is ISymbol symbol && IsTooLargeToShow(symbol))
            {
                shortened ??= (object?[])arguments.Clone();
                shortened[i] = Of(symbol);
            }
        }

        return shortened ?? arguments;
    }

    /// <summary>
    /// Gets a value indicating whether a symbol mentions a type nested more deeply than <see cref="MaxNesting"/>, or types with more parts
    /// than <see cref="MaxParts"/>: its own type, the types of its parameters, its result, its type arguments and its containing types.
    /// The walk keeps a stack of its own, so it cannot run out of stack however deep the type is.
    /// </summary>
    public static bool IsTooLargeToShow(ISymbol symbol)
    {
        var pending = new Stack<(ITypeSymbol Type, int Depth)>();

        switch (symbol)
        {
            case ITypeSymbol type:
                pending.Push((type, 0));
                break;

            case IMethodSymbol method:
                pending.Push((method.ReturnType, 0));

                foreach (var argument in method.TypeArguments)
                    pending.Push((argument, 0));

                foreach (var parameter in method.Parameters)
                    pending.Push((parameter.Type, 0));

                break;

            case IPropertySymbol property:
                pending.Push((property.Type, 0));

                foreach (var parameter in property.Parameters)
                    pending.Push((parameter.Type, 0));

                break;

            case IFieldSymbol field:
                pending.Push((field.Type, 0));
                break;

            case IEventSymbol @event:
                pending.Push((@event.Type, 0));
                break;

            case ILocalSymbol local:
                pending.Push((local.Type, 0));
                break;

            case IParameterSymbol parameter:
                pending.Push((parameter.Type, 0));
                break;
        }

        if (symbol is not ITypeSymbol && symbol.ContainingType is { } containing)
            pending.Push((containing, 0));

        int parts = 0;

        while (pending.Count > 0)
        {
            var (current, depth) = pending.Pop();

            if (depth > MaxNesting || ++parts > MaxParts)
                return true;

            switch (current)
            {
                case IArrayTypeSymbol array:
                    pending.Push((array.ElementType, depth + 1));
                    break;

                case IPointerTypeSymbol pointer:
                    pending.Push((pointer.PointedAtType, depth + 1));
                    break;

                case INamedTypeSymbol named:
                    foreach (var argument in named.TypeArguments)
                        pending.Push((argument, depth + 1));

                    if (named.ContainingType is { } outer)
                        pending.Push((outer, depth));

                    break;
            }
        }

        return false;
    }

    // Writes a type with its outer levels of type arguments, and "..." in the place of what is deeper than that or comes after the most
    // parts that are written.
    private static void AppendShort(StringBuilder text, ITypeSymbol type, int levelsLeft, ref int parts)
    {
        if (levelsLeft < 0 || ++parts > ShortParts)
        {
            text.Append("...");
            return;
        }

        switch (type)
        {
            case IArrayTypeSymbol array:
                AppendShort(text, array.ElementType, levelsLeft - 1, ref parts);
                text.Append('[').Append(',', array.Rank - 1).Append(']');
                break;

            case IPointerTypeSymbol pointer:
                AppendShort(text, pointer.PointedAtType, levelsLeft - 1, ref parts);
                text.Append('*');
                break;

            case INamedTypeSymbol { IsTupleType: true, TupleElements: var elements }:
                text.Append('(');
                AppendList(text, elements.Select(static element => element.Type), levelsLeft, ref parts);
                text.Append(')');
                break;

            case INamedTypeSymbol { IsGenericType: true } named:
                text.Append(named.OriginalDefinition.ToDisplayString(NameOnly)).Append('<');
                AppendList(text, named.TypeArguments, levelsLeft, ref parts);
                text.Append('>');
                break;

            default:
                text.Append(type.ToDisplayString(NameOnly));
                break;
        }
    }

    private static void AppendList(StringBuilder text, IEnumerable<ITypeSymbol> types, int levelsLeft, ref int parts)
    {
        bool first = true;

        foreach (var type in types)
        {
            if (!first)
                text.Append(", ");

            first = false;
            AppendShort(text, type, levelsLeft - 1, ref parts);

            if (parts > ShortParts)
                break;
        }
    }
}
