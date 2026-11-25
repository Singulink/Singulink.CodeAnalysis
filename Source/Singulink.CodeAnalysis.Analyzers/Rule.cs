using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Singulink.CodeAnalysis.Analyzers;

/// <summary>
/// One message of a diagnostic ID, with a descriptor for each severity that it can be reported with.
/// </summary>
/// <remarks>
/// A rule is not a descriptor, so nothing can report it with <see cref="Diagnostic.Create(DiagnosticDescriptor, Location, object[])"/>
/// directly: every report goes through <c>DoNotCopyValueAnalyzer.Severities</c>, which chooses the descriptor.
/// </remarks>
internal sealed class Rule
{
    public Rule(
        RuleGroup group,
        DiagnosticDescriptor main,
        DiagnosticDescriptor? error = null,
        DiagnosticDescriptor? unsafeWarning = null,
        DiagnosticDescriptor? message = null,
        About about = default,
        DiagnosticDescriptor? errorAtBound = null,
        DiagnosticDescriptor? unsafeWarningAtBound = null)
    {
        Group = group;
        Main = main;
        Error = error;
        UnsafeWarning = unsafeWarning;
        Message = message;
        About = about;
        ErrorAtBound = errorAtBound;
        UnsafeWarningAtBound = unsafeWarningAtBound;
    }

    /// <summary>
    /// Gets the group of the rule's ID, which decides the severities that it can be reported with.
    /// </summary>
    public RuleGroup Group { get; }

    /// <summary>
    /// Gets what the reports of a rule of <see cref="RuleGroup.Enforced"/> are about: the types that decide, under the updated memory
    /// safety rules, whether a report is the error or the ordinary warning of the current rules.
    /// </summary>
    public About About { get; }

    /// <summary>
    /// Gets the ID of the rule.
    /// </summary>
    public string Id => Main.Id;

    /// <summary>
    /// Gets the ordinary warning of the rule, which can be suppressed and configured in every way. A rule of
    /// <see cref="RuleGroup.Enforced"/> is reported with it in a compilation that uses the current memory safety rules, in one whose
    /// project sets NonCopyableErrorsAsWarnings, which reports as under the current rules, and under the updated rules for a report
    /// about no type that is unsafe to copy.
    /// </summary>
    public DiagnosticDescriptor Main { get; }

    /// <summary>
    /// Gets the error of a rule of <see cref="RuleGroup.Enforced"/>, which cannot be suppressed: the descriptor of a report about a type
    /// that is unsafe to copy outside an unsafe context, and not at a declaration marked <c>unsafe</c> or <c>safe</c>, in a compilation
    /// that uses the updated memory safety rules. Null for a rule of the other groups.
    /// </summary>
    public DiagnosticDescriptor? Error { get; }

    /// <summary>
    /// Gets the warning of a rule of <see cref="RuleGroup.Enforced"/> that '#pragma' and NoWarn suppress and a [SuppressMessage] does
    /// not: the descriptor of a report about a type that is unsafe to copy in an unsafe context, or at a declaration marked
    /// <c>unsafe</c> or <c>safe</c>, in a compilation that uses the updated memory safety rules. Null for a rule of the other groups.
    /// </summary>
    public DiagnosticDescriptor? UnsafeWarning { get; }

    /// <summary>
    /// Gets the message of a rule of <see cref="RuleGroup.UncheckedAssembly"/>, for a project that gets the analyzer only through another
    /// package. Null for a rule of the other groups.
    /// </summary>
    public DiagnosticDescriptor? Message { get; }

    /// <summary>
    /// Gets the error of <see cref="Error"/> for a report that is strict only because a walk over types reached its bound: a type has
    /// more parts than the analyzer looks at, and is taken as if it could hold a value that is unsafe to copy. Its message says so. Null
    /// for a rule of the other groups.
    /// </summary>
    public DiagnosticDescriptor? ErrorAtBound { get; }

    /// <summary>
    /// Gets the warning of <see cref="UnsafeWarning"/> for a report that is strict only because a walk over types reached its bound, with
    /// a message that says so (see <see cref="ErrorAtBound"/>). Null for a rule of the other groups.
    /// </summary>
    public DiagnosticDescriptor? UnsafeWarningAtBound { get; }

    /// <summary>
    /// Gets every descriptor of the rule.
    /// </summary>
    public IEnumerable<DiagnosticDescriptor> Descriptors
    {
        get
        {
            yield return Main;

            if (Error is not null)
                yield return Error;

            if (UnsafeWarning is not null)
                yield return UnsafeWarning;

            if (Message is not null)
                yield return Message;

            if (ErrorAtBound is not null)
                yield return ErrorAtBound;

            if (UnsafeWarningAtBound is not null)
                yield return UnsafeWarningAtBound;
        }
    }
}

/// <summary>
/// What the reports of an enforced rule are about: the types that decide, under the updated memory safety rules, whether a report is
/// the error of the updated rules or the ordinary warning of the current rules, and what of a type counts. A report is the error where
/// what counts of any type that it is about is unsafe to copy, and where it cannot be tied to a type at all. The default value is no
/// statement, which an enforced rule cannot be created with.
/// </summary>
/// <remarks>
/// What counts of a type depends on what the rule is about. A report that a value was copied is about the values that the copy
/// duplicates: the value itself and what it holds inline, through tuples, nullable types and structs by their type arguments
/// (<see cref="IsCopy"/>). A reference that rides along, to a list, a delegate or any class, is not duplicated, so what is behind it does
/// not count. A report that other code can get at a value or a container, as a member that is not on a list, a conversion that hides it,
/// a method that keeps it or a box, is about everything that code can reach: the value, what it holds inline, and the values of the
/// containers that it carries.
/// </remarks>
internal readonly struct About
{
    private About(ImmutableArray<int> arguments, bool isCopy, bool whatItHolds, bool isGiven)
    {
        Arguments = arguments;
        IsCopy = isCopy;
        WhatItHolds = whatItHolds;
        IsGiven = isGiven;
    }

    /// <summary>
    /// Gets what a report is about where it cannot be tied to a type, such as a call of <c>RuntimeHelpers.GetObjectValue</c>, or a
    /// generic whose type parameter stands for whatever a caller binds: every report is the error under the updated rules.
    /// </summary>
    public static About NoType { get; } = new([], isCopy: false, whatItHolds: false, isGiven: false);

    /// <summary>
    /// Gets what a report that a value was copied is about where its message does not name the type: the code that reports it gives the
    /// type as a <see cref="Subject"/>. A report that is given none is the error under the updated rules.
    /// </summary>
    public static About GivenCopy { get; } = new([], isCopy: true, whatItHolds: false, isGiven: true);

    /// <summary>
    /// Gets what a report that other code can get at a value or a container is about where its message does not name the type: the code
    /// that reports it gives the type as a <see cref="Subject"/>. A report that is given none is the error under the updated rules.
    /// </summary>
    public static About GivenReach { get; } = new([], isCopy: false, whatItHolds: false, isGiven: true);

    /// <summary>
    /// Gets a value indicating whether the value says anything: the default value does not.
    /// </summary>
    public bool IsSpecified => !Arguments.IsDefault;

    /// <summary>
    /// Gets the indexes of the arguments of the message that are the types a report is about. An argument that is not a type makes the
    /// report the error.
    /// </summary>
    public ImmutableArray<int> Arguments { get; }

    /// <summary>
    /// Gets a value indicating whether a report is about a copy: what counts of a type is what a copy of a value of it duplicates, which
    /// is the value and what it holds inline. A container that such a report names is about the container left in two places, where other
    /// code can reach it, so it counts by what it holds. Otherwise a report is about everything that code which gets at the value or the
    /// container can reach.
    /// </summary>
    public bool IsCopy { get; }

    /// <summary>
    /// Gets a value indicating whether a type that a report is about counts by what it holds, its type arguments, rather than by itself:
    /// the box of a generic struct gives boxed copies of what the struct is made of.
    /// </summary>
    public bool WhatItHolds { get; }

    /// <summary>
    /// Gets a value indicating whether the code that reports gives the type as a <see cref="Subject"/>.
    /// </summary>
    public bool IsGiven { get; }

    /// <summary>
    /// Gets what a report that a value was copied is about where the given arguments of its message are the types.
    /// </summary>
    public static About Copy(params int[] indexes) => new([.. indexes], isCopy: true, whatItHolds: false, isGiven: false);

    /// <summary>
    /// Gets what a report that other code can get at a value or a container is about where the given arguments of its message are the
    /// types.
    /// </summary>
    public static About Reach(params int[] indexes) => new([.. indexes], isCopy: false, whatItHolds: false, isGiven: false);

    /// <summary>
    /// Gets what a report is about where the given argument of its message is a type that counts by what it holds: a box hides what the
    /// value of a generic struct holds from other code, which reaches it through the interfaces of the box.
    /// </summary>
    public static About WhatArgumentHolds(int index) => new([index], isCopy: false, whatItHolds: true, isGiven: false);
}

/// <summary>
/// The types that a report is about, given by the code that reports it where the message does not name them (<see cref="About.GivenCopy"/>,
/// <see cref="About.GivenReach"/>).
/// </summary>
internal readonly struct Subject
{
    private Subject(
        ImmutableArray<ITypeSymbol?> types, bool isDeclared, bool involvesNoUnsafeToCopy = false, ITypeSymbol? alsoReached = null, bool isTooLarge = false)
    {
        Types = types;
        IsDeclared = isDeclared;
        InvolvesNoUnsafeToCopy = involvesNoUnsafeToCopy;
        AlsoReached = alsoReached;
        IsTooLarge = isTooLarge;
    }

    /// <summary>
    /// Gets the subject of a report about code that the code which reports went through and found no value of a type that is unsafe to
    /// copy in: under the updated rules such a report is the ordinary warning.
    /// </summary>
    public static Subject NoUnsafeToCopy { get; } = new([], isDeclared: false, involvesNoUnsafeToCopy: true);

    /// <summary>
    /// Gets the subject of a report about a declaration that was not checked because a type of it has more parts than a walk looks at:
    /// it cannot be tied to a type, and is strict under the updated rules because of that bound, which its message says.
    /// </summary>
    public static Subject TooLarge { get; } = new([], isDeclared: false, isTooLarge: true);

    /// <summary>
    /// Gets the types, which count as the rule says (<see cref="About.IsCopy"/>). A null type stands for a report that cannot be tied to
    /// one, which is the error under the updated rules.
    /// </summary>
    public ImmutableArray<ITypeSymbol?> Types { get; }

    /// <summary>
    /// Gets a type of a report that stands for a second one, about what other code can reach, which counts by what that code can reach
    /// whatever the rule says. Null if there is none.
    /// </summary>
    public ITypeSymbol? AlsoReached { get; }

    /// <summary>
    /// Gets a value indicating whether the types are those of a declaration whose mark the report is about, such as a [MovedFrom]
    /// parameter: what a caller binds to a type parameter that such a type mentions is not known.
    /// </summary>
    public bool IsDeclared { get; }

    /// <summary>
    /// Gets a value indicating whether the code that reports found no value of a type that is unsafe to copy in what the report is about
    /// (<see cref="NoUnsafeToCopy"/>).
    /// </summary>
    public bool InvolvesNoUnsafeToCopy { get; }

    /// <summary>
    /// Gets a value indicating whether the report is about a declaration with a type that has more parts than a walk looks at
    /// (<see cref="TooLarge"/>).
    /// </summary>
    public bool IsTooLarge { get; }

    /// <summary>
    /// Gets a value indicating whether the code that reports gave a subject: the default value is none.
    /// </summary>
    public bool IsSpecified => !Types.IsDefault;

    /// <summary>
    /// Gets the subject of a report about values or containers of the given types.
    /// </summary>
    public static Subject Of(params ITypeSymbol?[] types) => new([.. types], isDeclared: false);

    /// <summary>
    /// Gets the subject of a report about a copy that stands for the report of a conversion as well: a use of an object that hands it to
    /// code that can clone it, which copies the binding that the report names, and hides what the converted value holds, which that code
    /// can reach.
    /// </summary>
    public static Subject OfCopyAndReach(ITypeSymbol? copied, ITypeSymbol? reached) => new([copied], isDeclared: false, alsoReached: reached);

    /// <summary>
    /// Gets the subject of a report about the mark of a parameter or of a return value of the given type. A type that mentions a type
    /// parameter, marked or not, stands for whatever a caller binds, which may be unsafe to copy, so such a report is the error under
    /// the updated rules.
    /// </summary>
    public static Subject OfDeclared(ITypeSymbol? type) => new([type], isDeclared: true);
}

/// <summary>
/// The groups of diagnostic IDs, by the severities that their rules are reported with.
/// </summary>
internal enum RuleGroup
{
    /// <summary>
    /// A copy, a hand-off, a wrong declaration, or code that the analyzer could not check. In a compilation that uses the current memory
    /// safety rules it is an ordinary warning, which can be suppressed and configured in every way. In one that uses the updated rules a
    /// report about a type that is unsafe to copy, or one that cannot be tied to a type (<see cref="About"/>), is an error that cannot be
    /// suppressed, and where it is reported in code that is in an unsafe context, a warning that '#pragma' and NoWarn suppress and a
    /// [SuppressMessage] does not. At a declaration it is the error there, unless the declaration is marked 'unsafe' or 'safe', where it
    /// is that warning: the place of the report decides, not the ID. A report about any other non-copyable type is the ordinary warning
    /// under the updated rules as well. The MSBuild property NonCopyableErrorsAsWarnings makes every report the ordinary warning under
    /// both rule sets.
    /// </summary>
    Enforced,

    /// <summary>
    /// A plain warning that can be suppressed and configured in every way: a line of a symbol list file, [NotNullRef] on a method of
    /// the project, which its author suppresses after checking the method, and an attribute named NonCopyable that is not the
    /// analyzer's, which a project can keep on purpose for another tool.
    /// </summary>
    Plain,

    /// <summary>
    /// An assembly that was not built with the analyzer. A configurable warning in a project that references a package of the analyzer
    /// itself, and a message in one that only gets the analyzer through another package.
    /// </summary>
    UncheckedAssembly,
}
