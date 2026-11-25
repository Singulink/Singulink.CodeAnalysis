using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Singulink.CodeAnalysis.Analyzers;

/// <summary>
/// Classifies the types and the member uses of a compilation, and caches the results. A compilation has two instances: one for checked code, and
/// one for the bodies of members marked <c>[RequiresCopyable]</c>, in which marked type parameters count as copyable.
/// </summary>
internal sealed class NonCopyableTypes(SafeMemberList list, IAssemblySymbol compilationAssembly, bool typeParametersAreCopyable, NonCopyableTypes? definitions = null)
{
    public const string NonCopyableAttributeName = "NonCopyableAttribute";

    // The name with no suffix. '[NonCopyable]' finds an attribute class of this name just the same, and nothing recognises it.
    public const string NonCopyableNameWithNoSuffix = "NonCopyable";
    public const string RequiresCopyableAttributeName = "RequiresCopyableAttribute";
    public const string MovedFromAttributeName = "MovedFromAttribute";
    public const string BorrowedAttributeName = "BorrowedAttribute";
    public const string KeptAttributeName = "KeptAttribute";
    public const string NotNullRefAttributeName = "NotNullRefAttribute";

    /// <summary>
    /// The namespace that the attributes are recognised in.
    /// </summary>
    public const string AttributeNamespace = "Singulink.CodeAnalysis";

    /// <summary>
    /// The names of the six attributes that the analyzer reads.
    /// </summary>
    public static readonly ImmutableArray<string> AttributeNames =
        [NonCopyableAttributeName, RequiresCopyableAttributeName, MovedFromAttributeName, BorrowedAttributeName, KeptAttributeName, NotNullRefAttributeName];

    private readonly ConcurrentDictionary<ITypeSymbol, CopyKind> _kinds = new(SymbolEqualityComparer.Default);
    private readonly ConcurrentDictionary<ISymbol, MemberUse> _uses = new(SymbolEqualityComparer.Default);
    private readonly ConcurrentDictionary<INamedTypeSymbol, bool> _erasableThroughMarks = new(SymbolEqualityComparer.Default);
    private readonly ConcurrentDictionary<INamedTypeSymbol, CloningBinding?> _unjudgedCloningBindings = new(SymbolEqualityComparer.Default);
    private readonly ConcurrentDictionary<INamedTypeSymbol, bool> _inheritsListedContainer = new(SymbolEqualityComparer.Default);
    private readonly ConcurrentDictionary<INamedTypeSymbol, bool> _hasListedMembers = new(SymbolEqualityComparer.Default);

    // The classes that the compilation declares, made once.
    private INamedTypeSymbol[]? _classes;

    /// <summary>
    /// Gets a value indicating whether a list names a member by its documentation comment ID.
    /// </summary>
    public bool IsListed(string memberId) => list.GetMember(memberId) is not null;

    /// <summary>
    /// Gets a value indicating whether the list marks a member, by its ID, 'borrows': it uses what it is given during the call, and
    /// keeps nothing of it.
    /// </summary>
    public bool IsBorrowing(string memberId) => list.GetMember(memberId)?.Marker is SafeListMarker.Borrows;

    /// <summary>
    /// Gets a value indicating whether a class is the attribute of the analyzer with the given name. The attributes are recognised by
    /// their full name: a class of that name directly in the namespace Singulink.CodeAnalysis, in any assembly, public or internal. A
    /// class of the same name anywhere else can be the attribute of another tool, and has no effect.
    /// </summary>
    public static bool IsAttribute([NotNullWhen(true)] INamedTypeSymbol? attributeClass, string attributeName) =>
        attributeClass is not null && attributeClass.Name == attributeName && IsInAttributeNamespace(attributeClass);

    /// <summary>
    /// Gets a value indicating whether a type that is not generic is declared directly in the namespace Singulink.CodeAnalysis.
    /// </summary>
    public static bool IsInAttributeNamespace(INamedTypeSymbol type) => type is { Arity: 0, ContainingType: null } && IsInNamespace(type, AttributeNamespace);

    /// <summary>
    /// Gets a value indicating whether the namespace that a symbol is declared in is the one with the given full name, from the global
    /// namespace down: "System.Threading.Tasks". A type that is nested in another type is in the namespace of that type.
    /// </summary>
    public static bool IsInNamespace(ISymbol? symbol, string fullName)
    {
        var current = symbol?.ContainingNamespace;

        // From the last part of the name to the first, and from the namespace outwards.
        for (int end = fullName.Length; end > 0;)
        {
            int start = fullName.LastIndexOf('.', end - 1) + 1;

            if (current is null ||
                current.IsGlobalNamespace ||
                current.Name.Length != end - start ||
                string.CompareOrdinal(current.Name, 0, fullName, start, end - start) != 0)
            {
                return false;
            }

            current = current.ContainingNamespace;
            end = start - 1;
        }

        return current is { IsGlobalNamespace: true };
    }

    /// <summary>
    /// Gets the type arguments of a type and of the types that contain it: an <c>Outer&lt;Task&lt;Guard&gt;&gt;.Entry&lt;int&gt;</c>
    /// mentions the task as much as an <c>Entry&lt;Task&lt;Guard&gt;&gt;</c> does.
    /// </summary>
    private static IEnumerable<ITypeSymbol> GetAllTypeArguments(INamedTypeSymbol? type)
    {
        for (var current = type; current is not null; current = current.ContainingType)
        {
            foreach (var argument in current.TypeArguments)
                yield return argument;
        }
    }

    /// <summary>
    /// Gets the type arguments of a type and of the types that contain it, each with the type parameter that it is bound to.
    /// </summary>
    private static IEnumerable<(ITypeParameterSymbol TypeParameter, ITypeSymbol Argument)> GetBoundTypeArguments(INamedTypeSymbol? type)
    {
        for (var current = type; current is not null; current = current.ContainingType)
        {
            var typeParameters = current.OriginalDefinition.TypeParameters;

            for (int i = 0; i < current.TypeArguments.Length && i < typeParameters.Length; i++)
                yield return (typeParameters[i], current.TypeArguments[i]);
        }
    }

    /// <summary>
    /// Gets the base type of a type, if it has one, and the interfaces that it names.
    /// </summary>
    private static IEnumerable<INamedTypeSymbol> GetBaseTypeAndInterfaces(INamedTypeSymbol type)
    {
        if (type.BaseType is { } baseType)
            yield return baseType;

        foreach (var @interface in type.Interfaces)
            yield return @interface;
    }

    /// <summary>
    /// Gets the method that a call in extension form stands for, with the receiver as its first parameter, or the method itself.
    /// </summary>
    public static IMethodSymbol GetUnreduced(IMethodSymbol method) => method.ReducedFrom ?? method;

    /// <summary>
    /// Gets the parameters of a member, with the receiver of its extension block first where it has one.
    /// </summary>
    /// <remarks>
    /// The receiver of an extension block is a parameter of every member of the block that is used on something, and it is not in
    /// the parameter list of the member. Two checks in a row left it out for that reason. So every rule that speaks of the
    /// parameters of a member asks here, and <c>Parameters</c> is read only where the receiver cannot be meant: by position
    /// against the arguments of a call or the parameters of another member, and on a delegate, a constructor, an operator, a
    /// lambda or a local function, none of which has such a receiver.
    /// </remarks>
    public static ImmutableArray<IParameterSymbol> GetParametersWithReceiver(ISymbol member)
    {
        var parameters = member switch {
            IMethodSymbol method => method.Parameters,
            IPropertySymbol property => property.Parameters,
            _ => [],
        };

        return GetExtensionReceiver(member) is { } receiver ? parameters.Insert(0, receiver) : parameters;
    }

    /// <summary>
    /// Gets the receiver of the extension block that a member is declared in, which is a parameter of the member. Null for a
    /// static member of a block, which is used on nothing, and for a lambda or a local function inside a member: the receiver is a
    /// variable that those capture.
    /// </summary>
    public static IParameterSymbol? GetExtensionReceiver(ISymbol? member) =>
        member is { IsStatic: false, ContainingSymbol: INamedTypeSymbol { IsExtension: true, ExtensionParameter: { } receiver } } ? receiver : null;

    public static bool HasAttribute(ISymbol symbol, string attributeName)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            if (IsAttribute(attribute.AttributeClass, attributeName))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Gets a value indicating whether a type parameter is marked [NonCopyable]. The mark says that the body of the generic is checked
    /// for copies, which only a build with the analyzer holds: on a generic of an assembly that was not built with it the mark counts
    /// as absent, and the generic goes by the list.
    /// </summary>
    public static bool IsMarkedNonCopyable(ITypeParameterSymbol typeParameter) =>
        AnalyzedAssemblies.IsOfAnalyzedAssembly(typeParameter) && HasNonCopyableMark(typeParameter);

    /// <summary>
    /// Gets a value indicating whether a type parameter carries the mark [NonCopyable] in an assembly that was not built with the analyzer,
    /// where it does not count. A message says so instead of asking for the mark.
    /// </summary>
    public static bool HasUncountedNonCopyableMark(ITypeParameterSymbol typeParameter) =>
        !AnalyzedAssemblies.IsOfAnalyzedAssembly(typeParameter) && HasNonCopyableMark(typeParameter);

    private static bool HasNonCopyableMark(ITypeParameterSymbol typeParameter)
    {
        foreach (var attribute in typeParameter.GetAttributes())
        {
            if (IsNonCopyableMark(attribute.AttributeClass))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Gets a value indicating whether an attribute class marks as <c>NonCopyableAttribute</c> does: it is that attribute, or it
    /// derives from it, as <c>sealed class LockGuardAttribute : NonCopyableAttribute</c> does to give the mark a name of its domain.
    /// Reading such a class as no mark would leave the struct copyable with no finding. This holds for this attribute only: a class
    /// that derives from one of the other four is not recognised, which means more findings and not fewer.
    /// </summary>
    /// <param name="attributeClass">The class of an attribute.</param>
    /// <param name="ofAnyNamespace">True to count a class named NonCopyableAttribute of any namespace, as on a struct of a
    /// referenced assembly: see <see cref="IsMarkedNonCopyable(INamedTypeSymbol)"/>.</param>
    public static bool IsNonCopyableMark(INamedTypeSymbol? attributeClass, bool ofAnyNamespace = false)
    {
        for (var current = attributeClass; current is not null; current = current.BaseType)
        {
            if (current.Name is NonCopyableAttributeName && (ofAnyNamespace || IsInAttributeNamespace(current)))
                return true;

            // An attribute class of the name with no suffix is no mark in the compilation, where it is reported. On a struct of a
            // referenced assembly nothing can be reported, so it counts like one of another namespace.
            if (ofAnyNamespace && current.Name is NonCopyableNameWithNoSuffix)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Gets the class named NonCopyableAttribute outside the namespace of the attributes, or named NonCopyable with no suffix in
    /// any namespace, that an attribute class is or derives from, if it neither is nor derives from the recognised one. Null if
    /// there is none.
    /// </summary>
    public static INamedTypeSymbol? FindForeignNonCopyableAttribute(INamedTypeSymbol? attributeClass)
    {
        if (IsNonCopyableMark(attributeClass))
            return null;

        for (var current = attributeClass; current is not null; current = current.BaseType)
        {
            if (current.Name is NonCopyableAttributeName or NonCopyableNameWithNoSuffix)
                return current;
        }

        return null;
    }

    /// <summary>
    /// Gets a value indicating whether a member is one that the runtime runs and no code calls: a static constructor or a finalizer.
    /// </summary>
    public static bool IsNeverCalled(ISymbol member) => member is IMethodSymbol { MethodKind: MethodKind.StaticConstructor or MethodKind.Destructor };

    /// <summary>
    /// Gets a value indicating whether a member, or the property or event that an accessor belongs to, is marked <c>[RequiresCopyable]</c>.
    /// </summary>
    public static bool IsMarkedRequiresCopyable(ISymbol member)
    {
        // Nothing calls a static constructor or a finalizer, so there is no use at which the copy could be reported: the mark
        // counts for nothing there, and is reported.
        if (IsNeverCalled(member))
            return false;

        if (HasAttribute(member, RequiresCopyableAttributeName))
            return true;

        return member switch {
            IMethodSymbol { AssociatedSymbol: { } associated } => HasAttribute(associated, RequiresCopyableAttributeName),
            IPropertySymbol property => HasAttributeOnAccessor(property.GetMethod) || HasAttributeOnAccessor(property.SetMethod),
            _ => false,
        };

        static bool HasAttributeOnAccessor(IMethodSymbol? accessor) => accessor is not null && HasAttribute(accessor, RequiresCopyableAttributeName);
    }

    /// <summary>
    /// Gets a value indicating whether the return value of a method, a local function or a lambda is marked <c>[Borrowed]</c>: the method keeps
    /// the container that it returns, so its callers treat the result as stored.
    /// </summary>
    /// <summary>
    /// Gets a value indicating whether a function adds <c>[return: Borrowed]</c> to what it stands in for: an override or an
    /// implementation to the member that it replaces, a method or a lambda to the delegate that it is converted to. A caller through
    /// that takes the result for a new value.
    /// </summary>
    public static bool AddsBorrowed(IMethodSymbol function, IMethodSymbol standsInFor) => HasBorrowedReturn(function) && !HasBorrowedReturn(standsInFor);

    /// <summary>
    /// Gets a value indicating whether a parameter adds <c>[Kept]</c> to the parameter that it stands in for, which is not taken to
    /// keep what it is given. A caller through that takes a container that it passes to be still its own.
    /// </summary>
    public bool AddsKept(IParameterSymbol parameter, IParameterSymbol standsInFor) => IsMarkedKept(parameter) && !IsTakenToKeepAtEveryCall(standsInFor);

    /// <summary>
    /// Gets a value indicating whether every call through a parameter, of a delegate or of a member that a method overrides or implements,
    /// takes what it passes to be kept, so that a method or a lambda behind the parameter may keep it with no caller misled.
    /// </summary>
    /// <remarks>
    /// A parameter keeps by <see cref="KeepsWhatItIsGiven"/>, and by a declared type that is a type parameter, marked or not, as
    /// <see cref="JudgeReceiving"/> says of a call. The exceptions are those that a call makes: a member that the list marks 'borrows',
    /// and a [MovedFrom] parameter of a pure move.
    /// </remarks>
    public bool IsTakenToKeepAtEveryCall(IParameterSymbol parameter) =>
        KeepsWhatItIsGiven(parameter) ||
        (parameter.OriginalDefinition.Type is ITypeParameterSymbol && !IsMovedFrom(parameter) && !IsBorrowing(parameter.ContainingSymbol));

    /// <summary>
    /// Gets a value indicating whether a parameter is <c>[MovedFrom]</c> where the parameter that it stands in for is not, or the other
    /// way round. The mark is kept in both directions: added, the callers through the other pass anything; dropped, the body is not
    /// checked for what it leaves behind, and the callers still trust it.
    /// </summary>
    public static bool ChangesMovedFrom(IParameterSymbol parameter, IParameterSymbol standsInFor) => IsMovedFrom(parameter) != IsMovedFrom(standsInFor);

    public static bool HasBorrowedReturn(IMethodSymbol method) => HasReturnAttribute(GetUnreduced(method).OriginalDefinition, BorrowedAttributeName);

    /// <summary>
    /// Gets a value indicating whether what a method, or the getter of a property, returns is marked [Borrowed].
    /// </summary>
    public static bool HasBorrowedReturn(ISymbol member) => member switch {
        IMethodSymbol method => HasBorrowedReturn(method),
        IPropertySymbol { GetMethod: { } getter } => HasBorrowedReturn(getter),
        _ => false,
    };

    /// <summary>
    /// Gets a value indicating whether the return value of a method has one of the attributes of the analyzer.
    /// </summary>
    public static bool HasReturnAttribute(IMethodSymbol method, string attributeName)
    {
        foreach (var attribute in method.GetReturnTypeAttributes())
        {
            if (IsAttribute(attribute.AttributeClass, attributeName))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Gets the constructors of the base class that a constructor of a class can be calling with nothing written: the class has no
    /// constructor, or the constructor has no initializer. Empty if the constructor names what it calls, or if it is not declared
    /// in source, so that nothing shows what it calls.
    /// </summary>
    /// <remarks>
    /// Which of them the call binds to is the compiler's choice, and is taken from the compiler where the constructor has a body:
    /// see <see cref="IsWrittenWithBody"/>. Where it has to be worked out, it is worked out as the language does, among the
    /// constructors that the class can call: the one with no parameters, then the one whose parameters are all optional, which
    /// applies as it is written, then the one with a 'params' parameter, which applies only with its collection made. Only where
    /// that does not decide, which the compiler reports as an ambiguous call, does every candidate count.
    /// </remarks>
    public static IEnumerable<IMethodSymbol> GetBaseConstructorsCalledWithNothingWritten(IMethodSymbol constructor)
    {
        if (constructor is not { MethodKind: MethodKind.Constructor, IsStatic: false, ContainingType: { TypeKind: TypeKind.Class, BaseType: { SpecialType: not SpecialType.System_Object } baseType } type })
            return [];

        if (constructor.IsImplicitlyDeclared)
        {
            // The copy constructor of a record calls the copy constructor of its base record.
            if (!constructor.Parameters.IsEmpty)
                return [];
        }
        else
        {
            if (constructor.DeclaringSyntaxReferences.IsEmpty)
                return [];

            foreach (var declared in constructor.DeclaringSyntaxReferences)
            {
                bool namesWhatItCalls = declared.GetSyntax() switch {
                    ConstructorDeclarationSyntax written => written.Initializer is not null,

                    // A primary constructor writes its call of the base constructor in the base list.
                    TypeDeclarationSyntax { BaseList.Types: var bases } => bases.Any(static named => named is PrimaryConstructorBaseTypeSyntax),
                    _ => true,
                };

                if (namesWhatItCalls)
                    return [];
            }
        }

        var candidates = baseType.InstanceConstructors
            .Where(candidate => candidate.Parameters.All(static parameter => parameter.IsOptional || parameter.IsParams) && CanCall(type, candidate))
            .ToList();

        if (candidates.FirstOrDefault(static candidate => candidate.Parameters.IsEmpty) is { } parameterless)
            return [parameterless];

        var asWritten = candidates.Where(static candidate => !candidate.Parameters.Any(static parameter => parameter.IsParams)).ToList();

        // With none that applies as it is written, what is left are the 'params' ones: one of them decides by being alone.
        return asWritten.Count is 1 ? [asWritten[0]] : candidates;

        // What a class can call of its base class: anything but a private member, unless the class is nested in the base class, and
        // an internal one of another assembly that does not let it in.
        static bool CanCall(INamedTypeSymbol derived, IMethodSymbol candidate)
        {
            switch (candidate.DeclaredAccessibility)
            {
                case Accessibility.Private:
                    for (var outer = derived.ContainingType; outer is not null; outer = outer.ContainingType)
                    {
                        if (SymbolEqualityComparer.Default.Equals(outer.OriginalDefinition, candidate.ContainingType.OriginalDefinition))
                            return true;
                    }

                    return false;

                case Accessibility.Internal or Accessibility.ProtectedAndInternal:
                    return SymbolEqualityComparer.Default.Equals(candidate.ContainingAssembly, derived.ContainingAssembly) ||
                        candidate.ContainingAssembly.GivesAccessTo(derived.ContainingAssembly);

                default:
                    return true;
            }
        }
    }

    /// <summary>
    /// Gets the class that a value of a type is an object of, for a rule that looks at the members of the class: the type itself,
    /// or for a type parameter the class that it is constrained to, also through a constraint to another type parameter. Null if
    /// there is none. A type parameter that is constrained to a class is that class for every rule that looks at the type of a
    /// value.
    /// </summary>
    public static INamedTypeSymbol? GetClassOf(ITypeSymbol? type)
    {
        for (int depth = 0; depth < 8 && type is ITypeParameterSymbol typeParameter; depth++)
        {
            type = typeParameter.ConstraintTypes.FirstOrDefault(static constraint => constraint.TypeKind is TypeKind.Class) ??
                typeParameter.ConstraintTypes.FirstOrDefault(static constraint => constraint is ITypeParameterSymbol);
        }

        return type as INamedTypeSymbol;
    }

    /// <summary>
    /// Gets a value indicating whether a constructor is the copy constructor of its type, which 'with' runs on a record: it has one
    /// parameter, by value, of the type itself.
    /// </summary>
    public static bool IsCopyConstructor(IMethodSymbol constructor) =>
        constructor is { MethodKind: MethodKind.Constructor, IsStatic: false, Parameters: [{ RefKind: RefKind.None, Type: var copied }] } &&
        SymbolEqualityComparer.Default.Equals(copied, constructor.ContainingType);

    /// <summary>
    /// Gets the copy constructor of a type, or null if it has none.
    /// </summary>
    public static IMethodSymbol? FindCopyConstructor(INamedTypeSymbol type) => type.InstanceConstructors.FirstOrDefault(IsCopyConstructor);

    /// <summary>
    /// Gets a value indicating whether a constructor is written with a body, so that the compiler's own choice of the base
    /// constructor that it calls with nothing written is an operation of that body. A primary constructor has none.
    /// </summary>
    public static bool IsWrittenWithBody(IMethodSymbol constructor) =>
        !constructor.IsImplicitlyDeclared &&
        constructor.DeclaringSyntaxReferences.Any(static declared => declared.GetSyntax() is ConstructorDeclarationSyntax { Body: not null } or ConstructorDeclarationSyntax { ExpressionBody: not null });

    /// <summary>
    /// Gets the return types of an override and of the member that it overrides, as the override names them, if the member is an override of
    /// a method or a property. A return type that the override narrows is a conversion with no expression.
    /// </summary>
    public static bool TryGetOverriddenReturn(
        ISymbol member, [NotNullWhen(true)] out ITypeSymbol? returnType, [NotNullWhen(true)] out ITypeSymbol? overriddenReturnType, [NotNullWhen(true)] out ISymbol? overridden)
    {
        switch (member)
        {
            case IMethodSymbol { IsOverride: true, AssociatedSymbol: null, OverriddenMethod: { } overriddenMethod } method:
                // The type parameters of a generic method are its own, so the two return types are compared as the override names them.
                if (method.Arity > 0 && method.Arity == overriddenMethod.Arity && SymbolEqualityComparer.Default.Equals(overriddenMethod, overriddenMethod.ConstructedFrom))
                    overriddenMethod = overriddenMethod.Construct(method.TypeArguments.ToArray());

                (returnType, overriddenReturnType, overridden) = (method.ReturnType, overriddenMethod.ReturnType, overriddenMethod);
                return true;

            case IPropertySymbol { IsOverride: true, OverriddenProperty: { } overriddenProperty } property:
                (returnType, overriddenReturnType, overridden) = (property.Type, overriddenProperty.Type, overriddenProperty);
                return true;

            default:
                (returnType, overriddenReturnType, overridden) = (null, null, null);
                return false;
        }
    }

    /// <summary>
    /// Gets the symbol that a safe list names for a member: its original definition, and for an accessor that of its property or event.
    /// </summary>
    public static ISymbol GetListDefinition(ISymbol member)
    {
        if (member is IMethodSymbol method)
        {
            method = GetUnreduced(method).OriginalDefinition;
            return method.AssociatedSymbol is IPropertySymbol or IEventSymbol ? method.AssociatedSymbol.OriginalDefinition : method;
        }

        return member.OriginalDefinition;
    }

    /// <summary>
    /// Gets a value indicating whether a type is one of the untyped collections, which hand out boxed copies of their elements:
    /// <see cref="Array"/> and the non-generic collection interfaces of <c>System.Collections</c>.
    /// </summary>
    public static bool IsUntypedCollection(ITypeSymbol type)
    {
        if (type.SpecialType is SpecialType.System_Array)
            return true;

        return type is INamedTypeSymbol { TypeKind: TypeKind.Interface, IsGenericType: false, ContainingType: null } &&
            type.Name is "IEnumerable" or "ICollection" or "IList" or "IDictionary" or "IStructuralEquatable" or "IStructuralComparable" &&
            IsInNamespace(type, "System.Collections");
    }

    public bool IsNonCopyableType([NotNullWhen(true)] ITypeSymbol? type) => GetKind(type) is CopyKind.NonCopyable;

    public bool IsContainer([NotNullWhen(true)] ITypeSymbol? type) => GetKind(type) is CopyKind.Container;

    /// <summary>
    /// Gets a value indicating whether a type is, or mentions, a non-copyable type.
    /// </summary>
    public bool Carries([NotNullWhen(true)] ITypeSymbol? type) => GetKind(type) is not CopyKind.Copyable;

    public CopyKind GetKind(ITypeSymbol? type) => GetKind(type, computation: null);

    /// <summary>
    /// Gets a value indicating whether a conversion from one type to another hides a non-copyable type from the code that receives the value:
    /// the source is a container and mentions a non-copyable type that the target does not. The exception is a container in which every
    /// non-copyable type sits inside an erasable type. What comes out of the untyped side is then a reference to the erasable object, and the
    /// only way from there to an element is a conversion to the generic type again, which gives a stored container (see
    /// <see cref="GainsNonCopyableType"/>).
    /// </summary>
    /// <remarks>
    /// Variance is judged by what it does: a conversion between two constructions of a variant interface or delegate converts what is bound
    /// to each variant type parameter, with no conversion written, in the direction in which the values go. An <c>Action&lt;object&gt;</c>
    /// that becomes an <c>Action&lt;List&lt;Guard&gt;&gt;</c> is given every list as an object.
    /// </remarks>
    public bool HidesNonCopyableType([NotNullWhen(true)] ITypeSymbol? source, [NotNullWhen(true)] ITypeSymbol? target) =>
        source is not null && target is not null && (HidesDirectly(source, target) || FindHiddenByVariance(source, target, depth: 0) is not null);

    /// <summary>
    /// Gets the container that a conversion hides through a variant type parameter, and the type that it becomes for the code behind the
    /// converted reference. Null when the conversion hides nothing in that way, or when the type that is converted is itself the container
    /// that is hidden.
    /// </summary>
    public (ITypeSymbol Container, ITypeSymbol HidingType)? GetHiddenByVariance(ITypeSymbol? source, ITypeSymbol? target) =>
        source is null || target is null || HidesDirectly(source, target) ? null : FindHiddenByVariance(source, target, depth: 0);

    /// <summary>
    /// Gets a value indicating whether a type is a generic struct that is non-copyable only through its type arguments and whose own code is
    /// not checked for them: a tuple, a <c>List&lt;T&gt;.Enumerator</c>, a <c>ValueTask&lt;T&gt;</c>. Behind its interfaces such a struct
    /// runs unchecked code on its box. A <c>Nullable&lt;T&gt;</c> boxes as what it holds.
    /// </summary>
    public bool IsStructOfUncheckedCode([NotNullWhen(true)] ITypeSymbol? type)
    {
        RuntimeHelpers.EnsureSufficientExecutionStack();

        if (type is not INamedTypeSymbol { TypeKind: TypeKind.Struct } named || GetKind(named) is not CopyKind.NonCopyable || IsNonCopyableByItself(named))
            return false;

        if (named.OriginalDefinition.SpecialType is SpecialType.System_Nullable_T)
            return IsStructOfUncheckedCode(named.TypeArguments[0]);

        foreach (var (typeParameter, argument) in GetBoundTypeArguments(named))
        {
            if (Carries(argument) && !IsMarkedNonCopyable(typeParameter))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Gets the members of the given interfaces that a struct leaves to an interface: a default implementation, also one that a derived
    /// interface gives for a member of its base interface, or a member that is not virtual and so cannot be implemented. A call of such a
    /// member runs on a boxed copy of the struct.
    /// </summary>
    public static IEnumerable<ISymbol> GetMembersLeftToInterfaces(INamedTypeSymbol type, IEnumerable<INamedTypeSymbol> interfaces)
    {
        foreach (var @interface in interfaces)
        {
            foreach (var member in @interface.GetMembers())
            {
                // A static member is not called on a value, and a private one is not called from outside the interface.
                if (member is not (IMethodSymbol { MethodKind: MethodKind.Ordinary } or IPropertySymbol or IEventSymbol) ||
                    member.IsStatic ||
                    member.DeclaredAccessibility is Accessibility.Private)
                {
                    continue;
                }

                // A member that is not virtual has no implementation to find, and is always the interface's.
                bool isLeftToInterface = type.FindImplementationForInterfaceMember(member) is { } implementation
                    ? implementation.ContainingType?.TypeKind is TypeKind.Interface
                    : !member.IsAbstract;

                if (isLeftToInterface)
                    yield return member;
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether a value of a marked type parameter is used as a reference type that does not mention it. With a
    /// container bound to the type parameter that is a container used as a type which hides its elements, and nothing in the generic can
    /// tell which it is.
    /// </summary>
    public bool HidesTypeParameter([NotNullWhen(true)] ITypeSymbol? source, [NotNullWhen(true)] ITypeSymbol? target) =>
        source is ITypeParameterSymbol && target is { IsValueType: false } && IsNonCopyableType(source) && !Carries(target);

    /// <summary>
    /// Gets a value indicating whether a class or an interface, as it is constructed, is erasable: no base type and no interface of it hands
    /// out a non-copyable value that it holds. A list says so with the marker <c>erasable</c>, and a type of checked code is erasable by its
    /// marks.
    /// </summary>
    public bool IsErasable(INamedTypeSymbol type)
    {
        if (type.TypeKind is not (TypeKind.Class or TypeKind.Interface))
            return false;

        // Behind a class that clones itself, the clone copies what is bound, whatever the marks say and whatever a list line claims.
        if (FindUnjudgedCloningBinding(type) is not null)
            return false;

        if (list.IsErasable(type.OriginalDefinition))
            return true;

        if (_erasableThroughMarks.TryGetValue(type, out bool isErasable))
            return isErasable;

        isErasable = IsErasableThroughMarks(type);
        _erasableThroughMarks[type] = isErasable;
        return isErasable;
    }

    /// <summary>
    /// Gets a value indicating whether a conversion from one type to another gives a container out of a type that does not mention its
    /// non-copyable type, such as a cast from object or from Task to a Task of a guard. Whoever else holds the reference can read from the
    /// container too, so what such a conversion gives is stored, whatever gives the reference.
    /// </summary>
    public bool GainsNonCopyableType([NotNullWhen(true)] ITypeSymbol? source, [NotNullWhen(true)] ITypeSymbol? target) =>
        source is not null && target is not null && IsContainer(target) && LosesNonCopyableType(target, source);

    /// <summary>
    /// Classifies the use of a method, a property or an event from what is bound to its type parameters and to those of the types that contain
    /// it. Each type parameter is judged alone.
    /// </summary>
    public MemberUse GetUse(ISymbol member)
    {
        RuntimeHelpers.EnsureSufficientExecutionStack();

        // Most members are not generic and are not members of a generic type. An event is not tested here: it is rare, and the
        // classification gives it the same answer.
        if (member is IMethodSymbol { IsGenericMethod: false, ContainingType: null or { IsGenericType: false, ContainingType: null } } or
            IPropertySymbol { ContainingType: null or { IsGenericType: false, ContainingType: null } })
        {
            return default;
        }

        // Nothing that carries a non-copyable type is bound: ClassifyUse would judge every type argument copyable and give the default.
        if (!HasCarriedTypeArgument(member))
            return default;

        if (_uses.TryGetValue(member, out var use))
            return use;

        use = ClassifyUse(member);
        _uses[member] = use;
        return use;
    }

    private bool HasCarriedTypeArgument(ISymbol member)
    {
        if (member is IMethodSymbol method)
        {
            foreach (var argument in method.TypeArguments)
            {
                if (Carries(argument))
                    return true;
            }
        }

        for (var type = member.ContainingType; type is not null; type = type.ContainingType)
        {
            foreach (var argument in type.TypeArguments)
            {
                if (Carries(argument))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Gets a value indicating whether what a member hands out by value is as new as what the member was called on and with, which is the
    /// default for a listed member. An entry marked 'new' says otherwise only for a value of a non-copyable type, which leaves no second
    /// instance behind when it is taken out. A container that such a member hands out is a reference that whoever put it in may still hold.
    /// </summary>
    /// <remarks>
    /// An entry marked 'drains' says otherwise for the sequence that the member returns: each element that the sequence hands out
    /// has left the source for good, so the sequence is a new container whatever the member was called on. Like 'new' it speaks of
    /// values of a non-copyable type only: a container that is taken out of a channel is a reference that whoever wrote it may
    /// still hold, so a sequence of containers is as new as what the member was called on.
    /// </remarks>
    public bool FollowsInputs(MemberUse use, ITypeSymbol? handedOutType) => use.Kind switch {
        MemberUseKind.Listed => !(use.Drains && SafeMemberList.IsSequence(handedOutType, out var elementType) && IsNonCopyableType(elementType)),
        MemberUseKind.ListedNew => !IsNonCopyableType(handedOutType),
        _ => false,
    };

    /// <summary>
    /// Gets a value indicating whether a member of checked generic code hands out, by value, a container through a type that mentions a
    /// marked type parameter to which a container is bound, as <c>Bag&lt;Task&lt;Guard&gt;&gt;.Take()</c> does. Such a container is
    /// stored whatever the member was called on and with.
    /// </summary>
    /// <remarks>
    /// The body of the generic treats what is bound as a value that it alone holds. That is not true of a reference, which the caller may
    /// still hold, and nothing about what goes in can be trusted: every type that mentions the type parameter is a way in, and so is
    /// every piece of code that the generic is given.
    /// </remarks>
    public bool HandsOutBoundContainer(ISymbol? member, ITypeSymbol? handedOutType)
    {
        if (member is null || !IsContainer(handedOutType) || GetUse(member).Kind is not MemberUseKind.MarkedContainer)
            return false;

        var definitionType = member switch {
            IMethodSymbol method => GetUnreduced(method).OriginalDefinition.ReturnType,
            IPropertySymbol property => property.OriginalDefinition.Type,
            _ => null,
        };

        return definitionType is not null && MentionsBoundContainer(definitionType, member, depth: 0);
    }

    /// <summary>
    /// Gets a value indicating whether code that is not on a list decides what an object of a type holds, where the type holds containers
    /// inside a container: a generic with a container bound to a marked type parameter, or a type that is a container only through what
    /// it derives from or implements, as <c>class TaskQueue : Queue&lt;Task&lt;Guard&gt;&gt;</c> is. Such an object can never be seen to
    /// hold nothing stored: its own code can put any container in, with no argument that shows it.
    /// </summary>
    public bool IsOpenToStored([NotNullWhen(true)] ITypeSymbol? type)
    {
        if (type is not INamedTypeSymbol named || !HoldsContainers(named))
            return false;

        for (var current = named; current is not null; current = current.BaseType)
        {
            if (IsOpenLevel(current))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Gets a value indicating whether a conversion from one type to another makes an object whose own code decides what it holds
    /// and who else holds it look like a plain container: a class that derives from a listed container becomes that container or an
    /// interface of it, or a type that is open to stored containers becomes one that is not. What is converted counts as stored from
    /// there on.
    /// </summary>
    public bool LosesHolder([NotNullWhen(true)] ITypeSymbol? source, [NotNullWhen(true)] ITypeSymbol? target) =>
        IsContainer(target) &&
        ((IsOpenToStored(source) && !IsOpenToStored(target)) || (DerivesFromListedContainer(source) && !DerivesFromListedContainer(target)));

    /// <summary>
    /// Gets what a conversion to a construction of a variant interface or delegate converts in that way through a type parameter
    /// that hands values out: the type argument that it is bound to in the source, and what it becomes. Null if there is none.
    /// </summary>
    public (ITypeSymbol Holder, ITypeSymbol Container)? FindHolderLostByVariance(ITypeSymbol? source, ITypeSymbol? target, int depth = 0)
    {
        RuntimeHelpers.EnsureSufficientExecutionStack();

        if (depth > 8 ||
            source is null ||
            target is not INamedTypeSymbol { IsGenericType: true, TypeKind: TypeKind.Interface or TypeKind.Delegate } converted ||
            SymbolEqualityComparer.Default.Equals(source, target))
        {
            return null;
        }

        var definition = converted.OriginalDefinition;
        var typeParameters = definition.TypeParameters;

        foreach (var construction in GetConstructions(source))
        {
            if (!SymbolEqualityComparer.Default.Equals(construction.OriginalDefinition, definition))
                continue;

            for (int i = 0; i < typeParameters.Length; i++)
            {
                if (typeParameters[i].Variance is not VarianceKind.Out)
                    continue;

                var from = construction.TypeArguments[i];
                var to = converted.TypeArguments[i];

                if (LosesHolder(from, to))
                    return (from, to);

                if (FindHolderLostByVariance(from, to, depth + 1) is { } nested)
                    return nested;
            }
        }

        return null;

        static IEnumerable<INamedTypeSymbol> GetConstructions(ITypeSymbol type)
        {
            if (type is INamedTypeSymbol named)
                yield return named;

            foreach (var implemented in type.AllInterfaces)
                yield return implemented;
        }
    }

    /// <summary>
    /// Gets a value indicating whether a type is a marked type parameter that is constrained to a container, as <c>TList</c> is with
    /// <c>where TList : IReadOnlyList&lt;Guard&gt;</c> and <c>TTask</c> with <c>where TTask : Task&lt;TResult&gt;</c>. A value of it
    /// is a reference that the caller may still hold, like a <c>List&lt;Guard&gt;</c> parameter, and not a value that the generic
    /// alone holds. So it never counts as new for the members that the constraint gives it, whatever its last use is.
    /// </summary>
    public bool IsTypeParameterOverContainer([NotNullWhen(true)] ITypeSymbol? type) =>
        type is ITypeParameterSymbol typeParameter && IsMarkedNonCopyable(typeParameter) && HasContainerConstraint(typeParameter, depth: 0);

    private bool HasContainerConstraint(ITypeParameterSymbol typeParameter, int depth)
    {
        RuntimeHelpers.EnsureSufficientExecutionStack();

        // A constraint to another type parameter takes that one's constraints. The language allows no circle, and the depth is
        // bounded in case a broken declaration has one.
        foreach (var constraint in typeParameter.ConstraintTypes)
        {
            if (constraint is ITypeParameterSymbol other ? depth < 8 && HasContainerConstraint(other, depth + 1) : IsContainer(constraint))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Gets a value indicating whether an object of a type is never new for the members that are used on it: an object of a class
    /// that derives from a listed container, and a value of a marked type parameter that is constrained to a container.
    /// </summary>
    public bool IsNeverNewForItsMembers([NotNullWhen(true)] ITypeSymbol? type) =>
        DerivesFromListedContainer(type) || IsTypeParameterOverContainer(type) || IsUncheckedDerivedContainer(type);

    /// <summary>
    /// Gets a value indicating whether a type is a class or an interface of an assembly that was not built with the analyzer, which is a
    /// container only through what it derives from or implements, as <c>class Leases : List&lt;Lease&gt;</c> in such a library is. Its own
    /// code decides what it holds and nothing checks that code, so an object of it is never new for the members that it inherits. A
    /// type whose own members the list names is left to those entries, as for <see cref="DerivesFromListedContainer"/>.
    /// </summary>
    public bool IsUncheckedDerivedContainer([NotNullWhen(true)] ITypeSymbol? type) =>
        type is INamedTypeSymbol { TypeKind: TypeKind.Class or TypeKind.Interface } named &&
        !AnalyzedAssemblies.IsOfAnalyzedAssembly(named) &&
        IsContainer(named) &&
        !GetAllTypeArguments(named).Any(Carries) &&
        !HasListedMembers(named.OriginalDefinition);

    /// <summary>
    /// Gets a class of the compilation that derives from a class, at any depth, and declares an instance field of a non-copyable type,
    /// with that field. Null if there is none.
    /// </summary>
    public (INamedTypeSymbol Class, IFieldSymbol Field)? FindDerivedClassWithNonCopyableField(INamedTypeSymbol type)
    {
        foreach (var derived in GetClasses())
        {
            // The fields of the class and of its constructed base classes below the type: 'class D : G<Guard>' holds the 'T' field of
            // 'G<T>' as a Guard. The fields of the type and of its own base classes are judged on the type itself.
            IFieldSymbol? found = null;

            for (var current = derived; current is not null; current = current.BaseType)
            {
                if (!ReferenceEquals(current, derived) && IsSameClassAs(current, type))
                {
                    if (found is not null)
                        return (derived, found);

                    break;
                }

                found ??= current.GetMembers().OfType<IFieldSymbol>().FirstOrDefault(f => f is { IsStatic: false, RefKind: RefKind.None } && IsNonCopyableType(f.Type));
            }
        }

        return null;
    }

    // A base class of a derived class is the type that a call is on where it is of the same class: the same instantiation where both are
    // closed constructed types of one generic class, 'E3D : E3<int>' is not an 'E3<string>'. A type that mentions a type parameter can
    // stand for any instantiation.
    private static bool IsSameClassAs(INamedTypeSymbol baseType, INamedTypeSymbol type) =>
        SymbolEqualityComparer.Default.Equals(baseType.OriginalDefinition, type.OriginalDefinition) &&
        (MentionsTypeParameter(baseType) || MentionsTypeParameter(type) || SymbolEqualityComparer.Default.Equals(baseType, type));

    // Every class that the compilation declares, nested ones included, made once.
    private INamedTypeSymbol[] GetClasses() => _classes ??= [.. GetClasses(compilationAssembly.GlobalNamespace)];

    /// <summary>
    /// Gets every class that the compilation declares, nested ones included.
    /// </summary>
    public IReadOnlyList<INamedTypeSymbol> GetClassesOfCompilation() => GetClasses();

    /// <summary>
    /// Gets a value indicating whether the symbol list marks a member 'clones': it clones its object, or the object that it is given,
    /// field by field with MemberwiseClone, so it copies every non-copyable value that the object holds and no entry can say otherwise.
    /// </summary>
    public bool IsCloningMember(ISymbol member) => list.IsCloningMember(GetListDefinition(member));

    /// <summary>
    /// Gets where a constructed class binds a non-copyable type to a type parameter of a class whose objects a class that the symbol list
    /// marks 'clones' clones, the class or one of its base classes: the class that is bound, the non-copyable type, and the cloning class.
    /// Null if there is none.
    /// </summary>
    /// <remarks>
    /// A class of another assembly is judged by its type arguments alone: what it does with a type parameter cannot be seen, and a
    /// reference assembly may leave its private fields out. A class of the compilation is judged by its fields: a field whose declared
    /// type mentions a type parameter and that is non-copyable as the class is constructed. The base classes are judged as they are
    /// constructed through the class, up to the cloning class, which is judged as well: its own fields are copied by its own clone.
    /// </remarks>
    public CloningBinding? FindCloningBinding(INamedTypeSymbol constructed)
    {
        // The topmost class that clones itself: a binding anywhere up to it is copied by its clone.
        var cloning = FindCloningBase(constructed) ?? (list.IsClones(constructed.OriginalDefinition) ? constructed : null);

        if (cloning is null)
            return null;

        for (var current = constructed; current is not null; current = current.BaseType)
        {
            if (!SymbolEqualityComparer.Default.Equals(current, current.OriginalDefinition))
            {
                if (!SymbolEqualityComparer.Default.Equals(current.ContainingAssembly, compilationAssembly))
                {
                    foreach (var argument in GetAllTypeArguments(current))
                    {
                        if (IsNonCopyableType(argument))
                            return new CloningBinding(current, argument, cloning.OriginalDefinition);
                    }
                }
                else
                {
                    foreach (var field in current.GetMembers().OfType<IFieldSymbol>())
                    {
                        if (field is { IsStatic: false, RefKind: RefKind.None } && MentionsTypeParameter(field.OriginalDefinition.Type) && IsNonCopyableType(field.Type))
                            return new CloningBinding(current, field.Type, cloning.OriginalDefinition);
                    }
                }
            }

            if (SymbolEqualityComparer.Default.Equals(cloning, current))
                break;
        }

        return null;
    }

    /// <summary>
    /// Gets a binding that the declaration of a class, or of a class above it up to the class that clones itself, writes with a closed
    /// non-copyable type in its base list, as 'Both&lt;T&gt; : Tagged&lt;GCHandle&gt;' does: one that does not come from the type
    /// arguments of the class. Null if there is none. A clone copies it whatever the class is constructed with.
    /// </summary>
    public CloningBinding? FindClosedCloningBinding(INamedTypeSymbol type) =>
        FindCloningBinding(type.OriginalDefinition) is { } binding && !MentionsTypeParameter(binding.Type) ? binding : null;

    /// <summary>
    /// Gets the topmost base class of a class, as it is constructed there, that the symbol list marks 'clones': its own code clones an
    /// object of it with MemberwiseClone, an object of the class included. Null if there is none.
    /// </summary>
    /// <remarks>
    /// A class below it can have a 'clones' line too, such as one that says 'clones=complete' for the members of a class between, or one
    /// that clones again in code of its own. The topmost one is the class that clones itself for every check: the chain up to it is where
    /// a binding is copied, and a line below it speaks only for the members of its own class.
    /// </remarks>
    public INamedTypeSymbol? FindCloningBase(INamedTypeSymbol type)
    {
        INamedTypeSymbol? found = null;

        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (list.IsClones(current.OriginalDefinition))
                found = current;
        }

        return found;
    }

    /// <summary>
    /// Gets the nearest base class of a class of which nothing says whether its own code clones an object with MemberwiseClone: a class
    /// of an assembly that was not built with the analyzer, that is no part of the framework, and that no 'trusted' line vouches for,
    /// where no 'clones=none' line names the class or a class between. Null if there is none. A class that a 'clones' line names is not
    /// looked for here: see <see cref="FindCloningBase"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The code of every other base class is known. A class of the compilation or of an analyzed assembly is checked in its own build,
    /// where a call of MemberwiseClone gets its warning. Of a class of the framework the built-in list says whether it clones itself:
    /// see <see cref="FrameworkAssemblies"/>. A 'clones=none' line for such a class has no effect, and gets a warning in its file: the
    /// walk goes on above the class, so that the line cannot vouch for an unknown class above it by the way. A 'clones=none' line for
    /// an unknown class vouches for that class and for every class above it, so the walk ends there. A 'trusted' line vouches for the
    /// classes of its assembly, and the walk goes on above them.
    /// </para>
    /// </remarks>
    /// <param name="type">The class.</param>
    /// <param name="trusted">Takes each assembly that a 'trusted' line vouches for and that has a base class of the class which the walk
    /// passes: the line answers the question for that class, so it has an effect where the class holds a non-copyable value.</param>
    public INamedTypeSymbol? FindUnknownBase(INamedTypeSymbol type, ICollection<IAssemblySymbol> trusted)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            var definition = current.OriginalDefinition;
            var assembly = definition.ContainingAssembly;

            if (assembly is null || AnalyzedAssemblies.IsAnalyzed(assembly) || FrameworkAssemblies.Contains(assembly))
                continue;

            if (list.IsNeverClones(definition))
                return null;

            if (list.IsTrusted(assembly))
            {
                trusted.Add(assembly);
                continue;
            }

            return current;
        }

        return null;
    }

    private static IEnumerable<INamedTypeSymbol> GetClasses(INamespaceOrTypeSymbol container)
    {
        foreach (var member in container.GetMembers())
        {
            if (member is INamespaceSymbol ns)
            {
                foreach (var found in GetClasses(ns))
                    yield return found;
            }
            else if (member is INamedTypeSymbol type)
            {
                if (type.TypeKind is TypeKind.Class)
                    yield return type;

                foreach (var found in GetClasses(type))
                    yield return found;
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether a class can be derived from in another assembly: it, or a class of the compilation that derives
    /// from it, is not sealed, can be reached from outside its assembly, and has a constructor that a derived class there can call.
    /// </summary>
    /// <remarks>
    /// A nested class that is protected can be reached only from a class that derives from the class that contains it, so only where
    /// that class can itself be derived from in another assembly. An assembly with any InternalsVisibleTo counts its internal types and
    /// constructors as reachable. A file-local class is never reachable.
    /// </remarks>
    public bool CanBeDerivedFromElsewhere(INamedTypeSymbol type) => CanBeDerivedFromElsewhere(type, []);

    private bool CanBeDerivedFromElsewhere(INamedTypeSymbol type, HashSet<INamedTypeSymbol> judging)
    {
        type = type.OriginalDefinition;

        // A class that is judged already, through a class nested in it, gives no route of its own.
        if (!judging.Add(type))
            return false;

        try
        {
            return CanItselfBeDerivedFromElsewhere(type, judging) ||
                GetClasses().Any(c => c.BaseType is { } baseType &&
                    SymbolEqualityComparer.Default.Equals(baseType.OriginalDefinition, type) &&
                    CanBeDerivedFromElsewhere(c, judging));
        }
        finally
        {
            judging.Remove(type);
        }
    }

    private bool CanItselfBeDerivedFromElsewhere(INamedTypeSymbol type, HashSet<INamedTypeSymbol> judging)
    {
        if (type.IsSealed || type.TypeKind is not TypeKind.Class)
            return false;

        bool internalsVisible = type.ContainingAssembly?.GetAttributes()
            .Any(a => a.AttributeClass is { Name: "InternalsVisibleToAttribute" } c && IsInNamespace(c, "System.Runtime.CompilerServices")) ?? false;

        for (var current = type; current is not null; current = current.ContainingType)
        {
            if (current.IsFileLocal)
                return false;

            bool reachable = current.DeclaredAccessibility switch {
                Accessibility.Public => true,
                Accessibility.Internal => internalsVisible,
                Accessibility.Protected => IsDerivableContainer(current),
                Accessibility.ProtectedOrInternal => internalsVisible || IsDerivableContainer(current),
                Accessibility.ProtectedAndInternal => internalsVisible && IsDerivableContainer(current),
                _ => false,
            };

            if (!reachable)
                return false;
        }

        return type.InstanceConstructors.Any(constructor => constructor.DeclaredAccessibility switch {
            Accessibility.Public or Accessibility.Protected or Accessibility.ProtectedOrInternal => true,
            Accessibility.Internal or Accessibility.ProtectedAndInternal => internalsVisible,
            _ => false,
        });

        bool IsDerivableContainer(INamedTypeSymbol nested) =>
            nested.ContainingType is { } containing && CanBeDerivedFromElsewhere(containing, judging);
    }

    /// <summary>
    /// Gets a value indicating whether a member of an assembly that was not built with the analyzer hands out a container that it names
    /// concretely, which is then stored: nothing checks that nothing else holds it. A list entry marked 'new' says that it does not. A
    /// constructor makes a new object, a member of the non-copyable type itself is trusted, and a member whose declared type mentions a
    /// type parameter goes by the rules of generic code.
    /// </summary>
    public bool HandsOutUncheckedContainer(ISymbol member, ITypeSymbol? handedOut, ITypeSymbol? declared) =>
        member is not IMethodSymbol { MethodKind: MethodKind.Constructor or MethodKind.StaticConstructor } &&
        IsContainer(handedOut) &&
        declared is not null && !MentionsTypeParameter(declared) &&
        !AnalyzedAssemblies.IsOfAnalyzedAssembly(member) &&
        !IsMemberOfNonCopyableType(member) &&
        list.GetMember(GetListDefinition(member))?.Marker is not SafeListMarker.New;

    /// <summary>
    /// Gets a value indicating whether a type is a class of code that the list does not vouch for which derives from a container that
    /// the list knows, as <c>class Guards : List&lt;Guard&gt;</c> is. The members that it inherits hand out what the object holds as new
    /// as the object is, and nothing checks the code of the class against that: it can keep <c>this</c>, hand it out, or fill it from
    /// anywhere. So an object of such a type never counts as new for those members, whatever it was created from and whoever holds it.
    /// </summary>
    /// <remarks>
    /// A class that is declared in the compilation is such code even where a list file names members of it. A class of another
    /// assembly is unless the list has a member of its own, as it has for the collections of the framework that derive from one
    /// another. A type with no container among its base classes is not affected: what its own members hand out is checked in its
    /// bodies, or reported where an unlisted member is used.
    /// </remarks>
    public bool DerivesFromListedContainer([NotNullWhen(true)] ITypeSymbol? type)
    {
        // A marked type parameter that is constrained to a container is one: see IsTypeParameterOverContainer.
        if (type is not INamedTypeSymbol { TypeKind: TypeKind.Class, BaseType.SpecialType: not SpecialType.System_Object } named)
            return false;

        if (_inheritsListedContainer.TryGetValue(named, out bool inherits))
            return inherits;

        inherits = IsContainer(named) && (named.OriginalDefinition.DeclaringSyntaxReferences.Length > 0 || !HasListedMembers(named.OriginalDefinition));

        if (inherits)
        {
            inherits = false;

            for (var current = named.BaseType; current is { SpecialType: not SpecialType.System_Object }; current = current.BaseType)
            {
                if (IsContainer(current) && HasListedMembers(current.OriginalDefinition))
                {
                    inherits = true;
                    break;
                }
            }
        }

        _inheritsListedContainer[named] = inherits;
        return inherits;
    }

    private bool HasListedMembers(INamedTypeSymbol definition)
    {
        if (_hasListedMembers.TryGetValue(definition, out bool hasListedMembers))
            return hasListedMembers;

        foreach (var member in definition.GetMembers())
        {
            if (member is IMethodSymbol or IPropertySymbol or IEventSymbol && list.GetMember(member) is not null)
            {
                hasListedMembers = true;
                break;
            }
        }

        _hasListedMembers[definition] = hasListedMembers;
        return hasListedMembers;
    }

    /// <summary>
    /// Gets a value indicating whether an object of a type that is open to stored containers also has the members of a framework
    /// container of containers, through a base class or, for an interface, a base interface. Those members hand out what the object
    /// holds as new as the object is, so an object of such a type never counts as a new value.
    /// </summary>
    public bool IsOpenWithListedMembers([NotNullWhen(true)] ITypeSymbol? type)
    {
        if (type is not INamedTypeSymbol named || !IsOpenToStored(named))
            return false;

        if (named.TypeKind is TypeKind.Interface)
            return named.AllInterfaces.Any(i => HoldsContainers(i) && !IsOpenLevel(i));

        for (var current = named.BaseType; current is not null; current = current.BaseType)
        {
            if (HoldsContainers(current) && !IsOpenLevel(current))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Gets a value indicating whether a method is the move helper of the attributes package, <c>NonCopyable.Move</c>: it reads its
    /// argument, assigns <c>default</c> to it and returns what it read, so it cannot throw and reaches nothing else.
    /// </summary>
    /// <remarks>
    /// The helper is recognised like the attributes, by its full name: a class of the name in another namespace is an ordinary class.
    /// A call of it counts as running nothing because of what its body is, so one that the project itself declares is the helper
    /// only if its body is those three statements and nothing else, and only in a class that runs nothing when it is first used: a
    /// static constructor or the initializer of a static field runs at the first call, and can throw there.
    /// </remarks>
    public static bool IsMoveHelper(IMethodSymbol method) =>
        method.OriginalDefinition is { Name: "Move", IsStatic: true, Parameters: [var moved], ContainingType: { Name: "NonCopyable", IsStatic: true } helper } definition &&
        IsInAttributeNamespace(helper) &&
        helper.StaticConstructors.IsEmpty &&
        IsGenericPureMove(definition) &&
        definition.DeclaringSyntaxReferences.All(declared => HasBodyOfMoveHelper(declared.GetSyntax(), moved.Name));

    /// <summary>
    /// Gets a value indicating whether a parameter carries the mark <c>[NotNullRef]</c>, wherever it stands. The attribute is
    /// recognised by its full name like the others, and a class that derives from it is not.
    /// </summary>
    /// <remarks>
    /// The mark promises what only a checked body could hold, so on a member of an assembly that was not built with the analyzer it
    /// counts as absent.
    /// </remarks>
    public static bool IsMarkedNotNullRef(IParameterSymbol parameter) =>
        HasAttribute(parameter.OriginalDefinition, NotNullRefAttributeName) && AnalyzedAssemblies.IsOfAnalyzedAssembly(parameter);

    /// <summary>
    /// Says what keeps the mark <c>[NotNullRef]</c> on a parameter from counting. Null where it counts: on a 'ref', 'in',
    /// 'ref readonly' or 'out' parameter of a method that is not virtual, or of a local function, which no build leaves out.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The mark says that the method returns only if the reference that it is given refers to something. A call of a method that
    /// is not virtual runs the method that it binds to, so the mark is read off that method and nothing has to be followed through
    /// overrides, implementations or delegates. With a virtual or abstract member, an override, a member of an interface or a
    /// delegate, the code that runs can be another, and the mark does not count there. Nor does it on a lambda, an operator, a
    /// constructor or an accessor, and on the receiver of an extension block, which is a parameter of all of those.
    /// </para>
    /// <para>
    /// The body of the method is not looked at, and no method is known by its name: the two of the attributes package carry the
    /// mark like any other.
    /// </para>
    /// </remarks>
    public static NotNullRefProblem? GetNotNullRefProblem(IParameterSymbol parameter)
    {
        parameter = parameter.OriginalDefinition;

        if (parameter.RefKind is RefKind.None)
            return NotNullRefProblem.ByValue;

        if (parameter.ContainingSymbol is not IMethodSymbol { MethodKind: MethodKind.Ordinary or MethodKind.LocalFunction, IsVirtual: false, IsAbstract: false, IsOverride: false } method)
            return NotNullRefProblem.NotOnAMethodThatIsNotVirtual;

        // A proof that is there in one build and not in the next is none.
        if (IsConditional(method) || method is { IsPartialDefinition: true, PartialImplementationPart: null })
            return NotNullRefProblem.CanBeLeftOut;

        return null;
    }

    /// <summary>
    /// Gets a value indicating whether a call that returns proves the reference that it passes to a parameter: the parameter is
    /// marked <c>[NotNullRef]</c> where the mark counts.
    /// </summary>
    public static bool ProvesNotNull([NotNullWhen(true)] IParameterSymbol? parameter) =>
        parameter is not null && IsMarkedNotNullRef(parameter) && GetNotNullRefProblem(parameter) is null;

    /// <summary>
    /// Gets a value indicating whether a method is marked <c>[Conditional]</c>, so that the compiler leaves out a call of it, with
    /// its arguments, where the symbol is not defined.
    /// </summary>
    public static bool IsConditional(IMethodSymbol method) =>
        method.GetAttributes().Any(static attribute => attribute.AttributeClass is { Name: "ConditionalAttribute" } attributeClass && IsInNamespace(attributeClass, "System.Diagnostics"));

    private static bool HasBodyOfMoveHelper(SyntaxNode declaration, string parameter)
    {
        // 'T moved = value; value = default!; return moved;', with nothing before, between or after.
        if (declaration is not MethodDeclarationSyntax { ParameterList.Parameters: [{ Type: { } parameterType }], Body.Statements: [var read, var reset, var returned] } ||
            read is not LocalDeclarationStatementSyntax { UsingKeyword.RawKind: 0, Declaration: { Type: var localType, Variables: [{ Initializer.Value: var initialValue } local] } } ||
            reset is not ExpressionStatementSyntax { Expression: AssignmentExpressionSyntax { RawKind: (int)SyntaxKind.SimpleAssignmentExpression } assignment } ||
            returned is not ReturnStatementSyntax { Expression: var result })
        {
            return false;
        }

        // A local of another type would be converted to, which can run code.
        if (!localType.IsVar && !localType.IsEquivalentTo(parameterType, topLevel: false))
            return false;

        var newValue = assignment.Right is PostfixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.SuppressNullableWarningExpression, Operand: var suppressed }
            ? suppressed
            : assignment.Right;

        return IsName(initialValue, parameter) &&
            IsName(assignment.Left, parameter) &&
            newValue is LiteralExpressionSyntax { RawKind: (int)SyntaxKind.DefaultLiteralExpression } or DefaultExpressionSyntax &&
            IsName(result, local.Identifier.ValueText);

        static bool IsName(ExpressionSyntax? expression, string name) => expression is IdentifierNameSyntax { Identifier.ValueText: var text } && text == name;
    }

    /// <summary>
    /// Gets a value indicating whether a method is a generic move: it has a [MovedFrom] parameter whose type is a marked type parameter,
    /// and it has no other parameter and no receiver that mentions a marked type parameter. Nothing of that type goes into such a
    /// method but what is moved, and its body is checked to hand out and to leave behind nothing else, so a caller can trust the
    /// signature when a container is bound.
    /// </summary>
    public static bool IsGenericPureMove(IMethodSymbol definition)
    {
        bool moves = false;

        foreach (var parameter in GetParametersWithReceiver(definition))
        {
            if (IsMovedFrom(parameter) && parameter.Type is ITypeParameterSymbol typeParameter && IsMarkedNonCopyable(typeParameter))
            {
                moves = true;
            }
            else if (MentionsMarkedTypeParameter(parameter.Type, out _))
            {
                return false;
            }
        }

        // A move hands back by value: one that returns a reference can return one to the caller's variable.
        return moves &&
            !ReturnsReferenceOrRefStruct(definition) &&
            (definition.IsStatic || definition.MethodKind is MethodKind.LocalFunction || !MentionsMarkedTypeParameter(definition.ContainingType, out _));
    }

    /// <summary>
    /// Gets a value indicating whether a method returns a reference or a ref struct, either of which can refer to a variable that the
    /// method was passed by reference.
    /// </summary>
    public static bool ReturnsReferenceOrRefStruct(IMethodSymbol method) => method.RefKind is not RefKind.None || method.ReturnType.IsRefLikeType;

    /// <summary>
    /// Gets a value indicating whether a method has a parameter marked [MovedFrom].
    /// </summary>
    public static bool HasMovedFromParameter(IMethodSymbol method) => GetParametersWithReceiver(method).Any(IsMovedFrom);

    /// <summary>
    /// Gets a value indicating whether a parameter is a 'ref' parameter marked [MovedFrom]. On any other parameter the mark has no
    /// meaning, which is reported where it is written. The mark promises what only a checked body can hold, so on a member of an
    /// assembly that was not built with the analyzer it counts as absent.
    /// </summary>
    public static bool IsMovedFrom(IParameterSymbol parameter) =>
        parameter.RefKind is RefKind.Ref && HasAttribute(parameter, MovedFromAttributeName) && AnalyzedAssemblies.IsOfAnalyzedAssembly(parameter);

    /// <summary>
    /// Gets a value indicating whether a 'ref' parameter carries the mark [MovedFrom] in an assembly that was not built with the analyzer,
    /// where it does not count. A message says so instead of saying that the parameter is not marked.
    /// </summary>
    public static bool HasUncountedMovedFrom(IParameterSymbol parameter) =>
        parameter.RefKind is RefKind.Ref && HasAttribute(parameter, MovedFromAttributeName) && !AnalyzedAssemblies.IsOfAnalyzedAssembly(parameter);

    /// <summary>
    /// Gets the members that a method stands for, which are what a caller can see in its place: the methods that it overrides, and the
    /// interface members that it implements, explicitly or by its name.
    /// </summary>
    public static IEnumerable<IMethodSymbol> GetMembersStoodFor(IMethodSymbol method)
    {
        for (var overridden = method.OverriddenMethod; overridden is not null; overridden = overridden.OverriddenMethod)
            yield return overridden;

        foreach (var implemented in method.ExplicitInterfaceImplementations)
            yield return implemented;

        if (method.MethodKind is not MethodKind.Ordinary || method.ContainingType is not { TypeKind: TypeKind.Class or TypeKind.Struct } type)
            yield break;

        foreach (var @interface in type.AllInterfaces)
        {
            foreach (var member in @interface.GetMembers(method.Name))
            {
                if (member is IMethodSymbol interfaceMethod && SymbolEqualityComparer.Default.Equals(type.FindImplementationForInterfaceMember(interfaceMethod), method))
                    yield return interfaceMethod;
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether a type mentions a type parameter that is marked [NonCopyable].
    /// </summary>
    public static bool MentionsMarkedTypeParameter(ITypeSymbol? type, [NotNullWhen(true)] out ITypeParameterSymbol? typeParameter)
    {
        RuntimeHelpers.EnsureSufficientExecutionStack();

        typeParameter = null;

        switch (type)
        {
            case ITypeParameterSymbol candidate when IsMarkedNonCopyable(candidate):
                typeParameter = candidate;
                return true;

            case IArrayTypeSymbol array:
                return MentionsMarkedTypeParameter(array.ElementType, out typeParameter);

            case INamedTypeSymbol named:
                foreach (var argument in GetAllTypeArguments(named))
                {
                    if (MentionsMarkedTypeParameter(argument, out typeParameter))
                        return true;
                }

                return false;

            default:
                return false;
        }
    }

    /// <summary>
    /// Gets a value indicating whether a type is an element type of a container: the element type of an array, or a type argument of the
    /// type, of a type that contains it, or of one of its base types and interfaces.
    /// </summary>
    public bool IsElementTypeOf(ITypeSymbol type, ITypeSymbol? holder) => IsElementTypeOf(type, holder, depth: 0);

    /// <summary>
    /// Gets a value indicating whether a type holds containers inside a container: an array, a collection or a task of tasks or of lists,
    /// as <c>Bag&lt;Task&lt;Guard&gt;&gt;</c>, <c>List&lt;List&lt;Guard&gt;&gt;</c> and <c>Task&lt;Guard&gt;[]</c> are. A stored container
    /// can be put into such a container without a copy of a value.
    /// </summary>
    public bool HoldsContainers([NotNullWhen(true)] ITypeSymbol? type) => HoldsContainers(type, depth: 0);

    /// <summary>
    /// Gets a value indicating whether a type is one of the four views that cannot become an interface and that hand out an element by
    /// value only through a read that is reported whatever the view came from: a span or a memory, read-only or not.
    /// </summary>
    public static bool IsSpanOrMemory([NotNullWhen(true)] ITypeSymbol? type) =>
        type is INamedTypeSymbol { Name: "Span" or "ReadOnlySpan" or "Memory" or "ReadOnlyMemory", Arity: 1, ContainingType: null } && IsInNamespace(type, "System");

    /// <summary>
    /// Gets a value indicating whether a type is a span, read-only or not, and gets the type of its elements.
    /// </summary>
    public static bool IsSpan([NotNullWhen(true)] ITypeSymbol? type, [NotNullWhen(true)] out ITypeSymbol? elementType)
    {
        if (type is INamedTypeSymbol { Name: "Span" or "ReadOnlySpan", TypeArguments: [var element], ContainingType: null } && IsInNamespace(type, "System"))
        {
            elementType = element;
            return true;
        }

        elementType = null;
        return false;
    }

    /// <summary>
    /// Gets the base class or the interface that makes a type a container, where the type is one through that alone and not through its
    /// own type arguments: <c>List&lt;Guard&gt;</c> for <c>class Guards : List&lt;Guard&gt;</c>. Null for a type that shows what it holds.
    /// </summary>
    public ITypeSymbol? GetContainerBehind(ITypeSymbol? type)
    {
        if (type is not INamedTypeSymbol named || !IsContainer(named))
            return null;

        if (GetAllTypeArguments(named).Any(Carries))
            return null;

        for (var current = named.BaseType; current is not null; current = current.BaseType)
        {
            if (IsContainer(current))
                return current;
        }

        return named.AllInterfaces.FirstOrDefault(IsContainer);
    }

    /// <summary>
    /// Gets a value indicating whether a parameter is marked <c>[Kept]</c> itself.
    /// </summary>
    public static bool IsMarkedKept(IParameterSymbol parameter) => HasAttribute(parameter.OriginalDefinition, KeptAttributeName);

    /// <summary>
    /// Gets a value indicating whether a type is, or mentions, a type parameter that is not marked <c>[NonCopyable]</c>: the code
    /// behind a member that is declared with such a type is not checked for what it does with a container.
    /// </summary>
    public static bool MentionsUnmarkedTypeParameter(ITypeSymbol? type)
    {
        RuntimeHelpers.EnsureSufficientExecutionStack();

        return type switch {
            ITypeParameterSymbol typeParameter => !IsMarkedNonCopyable(typeParameter),
            IArrayTypeSymbol array => MentionsUnmarkedTypeParameter(array.ElementType),
            IPointerTypeSymbol pointer => MentionsUnmarkedTypeParameter(pointer.PointedAtType),
            INamedTypeSymbol named => MentionsUnmarkedTypeParameterIn(named),
            _ => false,
        };
    }

    private static bool MentionsUnmarkedTypeParameterIn(INamedTypeSymbol type)
    {
        if (GetAllTypeArguments(type).Any(MentionsUnmarkedTypeParameter))
            return true;

        return false;
    }

    /// <summary>
    /// Gets a value indicating whether what a parameter is given is taken to be kept: by a constructor, by the setter of a property or an
    /// indexer, by a parameter marked <c>[Kept]</c>, and by a member of an unmarked generic for what is bound to its type parameter, as in
    /// <c>outer.Add(inner)</c>. Any other parameter only uses what it is given during the call.
    /// </summary>
    public static bool KeepsWhatItIsGiven(IParameterSymbol parameter)
    {
        var definition = parameter.OriginalDefinition;

        if (HasAttribute(definition, KeptAttributeName))
            return true;

        if (definition.ContainingSymbol is IMethodSymbol { MethodKind: MethodKind.Constructor or MethodKind.PropertySet })
            return true;

        return definition.Type is ITypeParameterSymbol typeParameter && !IsMarkedNonCopyable(typeParameter);
    }

    /// <summary>
    /// Says what a parameter does with a container that it receives. This is the one place that says who keeps: the check of a
    /// parameter that is not marked [Kept], the check of a local that is to count as the method's own, a method group and an
    /// inherited member all ask it.
    /// </summary>
    /// <param name="receiving">The parameter that receives the container.</param>
    /// <param name="member">The member that it is passed to. For the receiver of an extension block that is the member of the block
    /// that is used, and not what the parameter is declared in.</param>
    /// <param name="made">What the member makes of the container and hands back.</param>
    /// <param name="handsBackThroughOut">Whether the member hands a container back through an 'out' parameter.</param>
    /// <param name="isElementOfTypeParameter">Whether the container goes into the collection that the compiler makes for a 'params'
    /// parameter whose elements are of a type parameter.</param>
    /// <param name="isPureMove">Whether the container is moved by a pure move, which hands it back as its result.</param>
    /// <remarks>
    /// <para>
    /// A parameter keeps by its mark, by its kind, as that of a constructor and of a setter do, and by having a type parameter as
    /// its type, marked or not: that is what a caller assumes of 'bag.Add(list)'.
    /// </para>
    /// <para>
    /// A parameter whose type mentions a type parameter that is not marked keeps as well. The code behind it is not checked for
    /// what it does with a container, and no declaration tells 'ArrayPool&lt;T&gt;.Return(T[])', which keeps its array, from
    /// 'Array.Reverse&lt;T&gt;(T[])', which only uses it. The exceptions are those that hand back what they are given, and are
    /// followed instead: a member that returns a reference or a ref struct, a listed member that hands the container back under
    /// another type, and an 'out' argument of container type. Nor is a ref struct kept.
    /// </para>
    /// <para>
    /// A member whose list entry is marked 'borrows' keeps nothing, whatever its kind and the types of its parameters. A mark
    /// on the parameter itself comes first.
    /// </para>
    /// </remarks>
    public ReceivedContainer JudgeReceiving(
        IParameterSymbol receiving, ISymbol member, MadeOfContainer made, bool handsBackThroughOut, bool isElementOfTypeParameter = false, bool isPureMove = false)
    {
        if (IsMarkedKept(receiving))
            return ReceivedContainer.Kept;

        if (member is IMethodSymbol { AssociatedSymbol: IPropertySymbol property })
            member = property;

        if (IsBorrowing(member))
            return ReceivedContainer.Used;

        // Nothing checks what a member of an assembly that was not built with the analyzer does with a container that it gets. Its use
        // is reported where it stands (SCA1024), and the container is no longer the caller's own.
        if (IsUncheckedInbound(receiving, member))
            return ReceivedContainer.KeptByUnmarkedAssembly;

        // A constructor keeps by its kind. One of a ref struct that the list vouches for is a call that returns a ref struct:
        // 'new Span<T>(array)' keeps nothing more than 'array.AsSpan()' does. The body of a constructor of checked code is not
        // checked for what it does with its parameters, so that one keeps like any other constructor.
        bool isListedConstructorOfRefStruct = made is MadeOfContainer.RefStructView &&
            receiving.ContainingSymbol is IMethodSymbol { MethodKind: MethodKind.Constructor, ContainingType.IsRefLikeType: true } &&
            GetUse(member).Kind is MemberUseKind.Listed or MemberUseKind.ListedNew;

        if (KeepsWhatItIsGiven(receiving) && !isListedConstructorOfRefStruct)
            return ReceivedContainer.Kept;

        var declared = receiving.OriginalDefinition.Type;

        if (declared is ITypeParameterSymbol || isElementOfTypeParameter)
            return isPureMove ? ReceivedContainer.Used : ReceivedContainer.Kept;

        if (made is not MadeOfContainer.Nothing || receiving.Type.IsRefLikeType || handsBackThroughOut || !MentionsUnmarkedTypeParameter(declared))
            return ReceivedContainer.Used;

        // Where nothing that carries a non-copyable type is bound to a type parameter that is not marked, the member is checked
        // code for this use. A member that is not listed is reported as that where it is used.
        return GetUse(member).Kind switch {
            MemberUseKind.Checked => ReceivedContainer.Used,
            MemberUseKind.Listed or MemberUseKind.ListedNew => ReceivedContainer.KeptByUncheckedCode,
            _ => ReceivedContainer.KeptByUnlistedCode,
        };
    }

    /// <summary>
    /// Gets the first parameter, the receiver of an extension included, through which a member of an assembly that was not built with
    /// the analyzer gets at a value of a non-copyable type other than by value: by 'ref', 'in' or 'ref readonly', inside a container,
    /// inside a view or through a pointer. Null where there is none, where the member is of an analyzed assembly, or where the list marks
    /// it 'borrows'. A member of a non-copyable type is trusted with a value of that type, and a parameter whose declared type mentions a
    /// type parameter goes by the rules of generic code instead. The value of a setter or an init accessor is one of its parameters.
    /// </summary>
    public IParameterSymbol? FindUncheckedInbound(ISymbol member)
    {
        if (AnalyzedAssemblies.IsOfAnalyzedAssembly(member) || IsBorrowing(member))
            return null;

        var unreduced = member is IMethodSymbol method ? GetUnreduced(method) : member;

        foreach (var parameter in GetParametersWithReceiver(unreduced))
        {
            if (GetsAtNonCopyableValue(parameter) && !IsTrustedWith(member, parameter))
                return parameter;
        }

        return null;
    }

    private bool IsUncheckedInbound(IParameterSymbol receiving, ISymbol member) =>
        !AnalyzedAssemblies.IsOfAnalyzedAssembly(member) && GetsAtNonCopyableValue(receiving) && !IsTrustedWith(member, receiving);

    // The members of a non-copyable type itself are trusted: 'handle.Free()' on a struct of a library has to work.
    private bool IsMemberOfNonCopyableType(ISymbol member) => member.ContainingType is { } owner && IsNonCopyableType(owner);

    // A member of a non-copyable type is trusted with a value of that type, by reference or through a pointer, as it is with its receiver:
    // 'GCHandle.ToIntPtr(GCHandle)' and the operators of the type. Any other non-copyable value, and a container even of its own type, is
    // judged as for any other member.
    private bool IsTrustedWith(ISymbol member, IParameterSymbol parameter)
    {
        if (!IsMemberOfNonCopyableType(member))
            return false;

        var declared = parameter.OriginalDefinition.Type;

        while (declared is IPointerTypeSymbol { PointedAtType: var pointedAt })
            declared = pointedAt;

        return SymbolEqualityComparer.Default.Equals(declared.OriginalDefinition, member.ContainingType.OriginalDefinition);
    }

    /// <summary>
    /// Gets a value indicating whether a parameter that names a non-copyable type concretely gets at a value of it other than by value.
    /// A value that is passed by value is moved, and an 'out' argument gives the callee nothing to read.
    /// </summary>
    private bool GetsAtNonCopyableValue(IParameterSymbol parameter)
    {
        if (parameter.RefKind is RefKind.Out)
            return false;

        var declared = parameter.OriginalDefinition.Type;

        // A pointer at a pointer gets at what the innermost one points at.
        if (declared is IPointerTypeSymbol)
        {
            var pointedAt = declared;

            while (pointedAt is IPointerTypeSymbol pointer)
                pointedAt = pointer.PointedAtType;

            return !MentionsTypeParameter(pointedAt) && (IsNonCopyableType(pointedAt) || IsContainer(pointedAt));
        }

        if (MentionsTypeParameter(declared))
            return false;

        return (parameter.RefKind is not RefKind.None && IsNonCopyableType(declared)) || IsContainer(declared);
    }

    /// <summary>
    /// Says what a parameter does with a container that it receives where there is no call to look at, by the declaration of its
    /// member alone: for a method group and for an inherited member.
    /// </summary>
    public ReceivedContainer JudgeReceivingBySignature(IParameterSymbol receiving)
    {
        if (receiving.ContainingSymbol is not IMethodSymbol method)
            return JudgeReceiving(receiving, receiving.ContainingSymbol, MadeOfContainer.Nothing, handsBackThroughOut: false);

        // A member that is given the variable by reference and returns a reference can return the one that it was given.
        var made = receiving.RefKind is not (RefKind.None or RefKind.Out) && method is { ReturnsByRef: true } or { ReturnsByRefReadonly: true }
            ? MadeOfContainer.Reference
            : DescribeWhatIsHandedBack(method, method.ReturnType, receiving.Type, isArgument: true);

        bool handsBackThroughOut = GetParametersWithReceiver(method).Any(other => other.RefKind is RefKind.Out && IsContainer(other.Type));
        return JudgeReceiving(receiving, method, made, handsBackThroughOut);
    }

    /// <summary>
    /// Gets a value indicating whether what a function of checked code returns is checked where the return is written: it is a
    /// container, and the function hands out a value of its own. The check of a returned container asks this, and so does the
    /// description of what a call hands back, which must take every return that is not checked for what it can be.
    /// </summary>
    /// <param name="function">The method, accessor, local function or lambda.</param>
    /// <param name="returned">The type of the value that is returned. In an 'async' function that is what its task carries.</param>
    /// <remarks>
    /// Not checked: a delegate, which holds no value; a Memory&lt;T&gt; and a ReadOnlyMemory&lt;T&gt;, since every by-value
    /// read of an element through one is reported whatever it came from; what a getter of a property or an indexer returns,
    /// which usually hands out what its object holds; what is returned by reference; and a return that is marked [Borrowed].
    /// </remarks>
    public bool IsReturnCheckedWhereItIsWritten(IMethodSymbol function, ITypeSymbol returned) =>
        returned.TypeKind is not TypeKind.Delegate &&
        IsContainer(returned) &&
        !IsSpanOrMemory(returned) &&
        function is { MethodKind: not MethodKind.PropertyGet, RefKind: RefKind.None } &&
        !HasBorrowedReturn(function);

    // A function whose body can be seen not to be 'async'. One that an override, an implementation or the target of a delegate
    // stands behind, or that comes from a referenced assembly, can be 'async' for all that its declaration says.
    private static bool IsKnownNotToBeAsync(IMethodSymbol method) =>
        method.OriginalDefinition is { IsAsync: false, IsAbstract: false, IsVirtual: false, IsOverride: false, MethodKind: not MethodKind.DelegateInvoke } definition &&
        definition.ContainingType?.TypeKind is not TypeKind.Interface &&
        !definition.DeclaringSyntaxReferences.IsEmpty;

    // The type of what a task hands out when it is awaited.
    private static ITypeSymbol? GetResultOfTask(ITypeSymbol? type) =>
        type is INamedTypeSymbol { Name: "Task" or "ValueTask", TypeArguments: [var result], ContainingNamespace: var tasks } &&
        tasks is { Name: "Tasks", ContainingNamespace: { Name: "Threading", ContainingNamespace.Name: "System" } }
            ? result
            : null;

    /// <summary>
    /// Says what a member that is called on or with a container hands back of it, by the type of what it returns.
    /// </summary>
    /// <param name="member">The method, property or operator.</param>
    /// <param name="type">The type of what it hands back.</param>
    /// <param name="holder">The type of the container. What is handed back is none of its elements, which are followed by themselves.</param>
    /// <param name="isArgument">Whether the member is given the container as an argument, and not called on it.</param>
    public MadeOfContainer DescribeWhatIsHandedBack(ISymbol member, ITypeSymbol? type, ITypeSymbol? holder, bool isArgument)
    {
        if (type is null or { SpecialType: SpecialType.System_Void })
            return MadeOfContainer.Nothing;

        if (member is IMethodSymbol { ReturnsByRef: true } or IMethodSymbol { ReturnsByRefReadonly: true } or IPropertySymbol { ReturnsByRef: true } or IPropertySymbol { ReturnsByRefReadonly: true })
            return MadeOfContainer.Reference;

        if (type is { IsRefLikeType: true } or { TypeKind: TypeKind.Pointer or TypeKind.FunctionPointer })
            return MadeOfContainer.RefStructView;

        // An element of the container is followed by itself. A type can name itself among the type arguments of its interfaces,
        // as a Memory<T> does with IEquatable<Memory<T>>, and is no element of itself for that. A task of a Memory<T> is judged
        // like the memory, so the memory that comes out of it is not an element that stands by itself.
        if (!IsContainer(type) ||
            (holder is not null &&
                !SymbolEqualityComparer.Default.Equals(type, holder) &&
                IsElementTypeOf(type, holder) &&
                !(IsSpanOrMemory(type) && SymbolEqualityComparer.Default.Equals(GetResultOfTask(holder), type))))
        {
            return MadeOfContainer.Nothing;
        }

        var method = member as IMethodSymbol ?? (member as IPropertySymbol)?.GetMethod;

        // A member that is listed as 'new' takes what it hands out from the container for good, or creates it, as
        // 'ArrayPool<T>.Rent' does: that is no other name for the container.
        var use = GetUse(member);

        if ((method is not null && HasBorrowedReturn(method)) || use.Kind is MemberUseKind.Listed)
            return MadeOfContainer.UnderAnotherName;

        // A member of checked code hands out a value of its own, as far as its return is checked where it is written. Where it is
        // not, what it hands back of a container that it is given is that container under another name.
        if (use.Kind is not MemberUseKind.Checked || method is null)
            return MadeOfContainer.Nothing;

        if (!IsReturnCheckedWhereItIsWritten(method, type))
        {
            // A delegate holds no value. A view is the container that it is over: 's_pending = Window(buffer, 1);' is a store
            // of 'buffer'.
            if (type.TypeKind is TypeKind.Delegate)
                return MadeOfContainer.Nothing;

            if (IsSpanOrMemory(type))
                return MadeOfContainer.UnderAnotherName;

            // A getter or an indexer that is given the container, as an argument or as the receiver of an extension block:
            // 'ArraySegment<Guard> Segment => buffer;', 'IReadOnlyList<Guard> this[List<Guard> key] => key;'. A property of the
            // container's own type is a member that is used on it, and hands out what the object holds: 'waiter.Next'.
            return isArgument ? MadeOfContainer.UnderAnotherName : MadeOfContainer.Nothing;
        }

        // In an 'async' function the value that is returned is what its task carries, and the return of a memory is not checked
        // there either: 'async Task<Memory<Guard>> WindowAsync(Guard[] buffer) => buffer;'.
        if (GetResultOfTask(type) is { } carried && IsSpanOrMemory(carried) && IsContainer(carried) && !IsKnownNotToBeAsync(method))
            return MadeOfContainer.UnderAnotherName;

        return MadeOfContainer.Nothing;
    }

    /// <summary>
    /// Gets a value indicating whether a parameter is taken to keep the container that it is given, where the parameter that it
    /// stands behind is not: that of the delegate that a method group is converted to, or of the interface member that an
    /// inherited member implements. The callers through that take what they pass to be theirs still. Both are asked what every
    /// call is asked: see <see cref="JudgeReceiving"/>. A parameter that is marked [Kept] is reported as that.
    /// </summary>
    /// <param name="taking">The parameter of the method.</param>
    /// <param name="standsInFor">The parameter of the delegate or of the interface member.</param>
    /// <param name="byUncheckedCode">Whether it is taken to keep because its type mentions a type parameter that is not marked.</param>
    public bool KeepsUnseenBehind(IParameterSymbol taking, IParameterSymbol standsInFor, out bool byUncheckedCode)
    {
        byUncheckedCode = false;

        if (standsInFor.RefKind is RefKind.Out ||
            standsInFor.Type.TypeKind is TypeKind.Delegate ||
            !IsContainer(standsInFor.Type) ||
            IsMovedFrom(taking) ||
            JudgeReceivingBySignature(standsInFor) is not ReceivedContainer.Used)
        {
            return false;
        }

        var judged = JudgeReceivingBySignature(taking);
        byUncheckedCode = judged is ReceivedContainer.KeptByUncheckedCode;
        return judged is ReceivedContainer.Kept or ReceivedContainer.KeptByUncheckedCode;
    }

    /// <summary>
    /// Gets a value indicating whether a struct is a view of elements that it does not hold: a list names it with the marker <c>view</c>.
    /// </summary>
    public bool IsView([NotNullWhen(true)] ITypeSymbol? type) => type is INamedTypeSymbol { TypeKind: TypeKind.Struct } named && list.IsView(named.OriginalDefinition);

    /// <summary>
    /// Gets a value indicating whether a list marks a member, generic or not, as one that a stored value of a non-copyable type may be passed
    /// to by value: it reads the copy during the call and keeps nothing of it.
    /// </summary>
    public bool IsBorrowing(ISymbol member) => list.GetMember(GetListDefinition(member))?.Marker is SafeListMarker.Borrows;

    /// <summary>
    /// Gets a value indicating whether the list marks a property as 'owned': its task belongs to the object alone, and no other member
    /// of the object hands it out.
    /// </summary>
    public bool IsOwnedTaskProperty(IPropertySymbol property) => list.GetMember(GetListDefinition(property))?.Marker is SafeListMarker.Owned;

    /// <summary>
    /// Gets the outcomes of a call in which a method keeps nothing of a value of a non-copyable type that it is given by value through a
    /// parameter: those that a 'borrowswhen' entry of the method lists, where the entry covers the parameter. None where the method takes
    /// the value whatever happens, as every method does that the list does not mark so. For a method that returns a task, the outcomes
    /// are those of awaiting the task. 'borrows' gives none here: a member that it marks is given a value without a hand-off.
    /// </summary>
    public SafeListOutcomes GetBorrowedOutcomes(IMethodSymbol method, IParameterSymbol parameter)
    {
        if (parameter.RefKind is not RefKind.None ||
            !IsNonCopyableType(parameter.Type) ||
            list.GetMember(GetListDefinition(method)) is not { Marker: SafeListMarker.BorrowsWhen } entry ||
            !SafeMemberList.CanBeTaken(parameter.OriginalDefinition.Type, entry))
        {
            return SafeListOutcomes.None;
        }

        return entry.BorrowedOutcomes;
    }

    /// <summary>
    /// Gets what stands in the way of a type being erasable, among the things that a type which derives from an erasable type can add to it: a
    /// base class or an interface with bodies that are not checked for a non-copyable type which it receives, or an interface that variance
    /// converts to one over object, or to one that takes a delegate over object. Null if there is nothing of the kind.
    /// </summary>
    /// <remarks>
    /// The type is taken as it is constructed. For a definition that is every way in which it can be constructed, where a type parameter that
    /// is not constrained to a value type counts as a reference type.
    /// </remarks>
    public ErasureObstacle? FindErasureObstacle(INamedTypeSymbol type)
    {
        // A base class that the list marks as erasable is vouched for, together with its own base classes.
        for (var current = type.BaseType; current is not null && !list.IsErasable(current.OriginalDefinition); current = current.BaseType)
        {
            if (FindUncheckedReceiver(current) is { } obstacle)
                return obstacle;
        }

        foreach (var @interface in type.AllInterfaces)
        {
            if (!list.IsErasable(@interface.OriginalDefinition) && FindUncheckedReceiver(@interface) is { } obstacle)
                return obstacle;
        }

        if (type.TypeKind is TypeKind.Interface && FindVariance(type, type, isCovariant: true, depth: 0) is { } own)
            return own;

        foreach (var @interface in type.AllInterfaces)
        {
            if (FindVariance(@interface, @interface, isCovariant: true, depth: 0) is { } obstacle)
                return obstacle;
        }

        return null;
    }

    /// <summary>
    /// Gets a value indicating whether a member is on the safe list, whatever is bound to its type parameters. A listed member may be given a
    /// container as a type that hides its elements.
    /// </summary>
    public bool IsListed(ISymbol member) => list.GetMember(GetListDefinition(member)) is not null;

    /// <summary>
    /// Gets a value indicating whether a conversion from one type to another loses sight of a non-copyable type: the source mentions one that the
    /// target does not.
    /// </summary>
    public bool LosesNonCopyableType(ITypeSymbol source, ITypeSymbol target)
    {
        if (SymbolEqualityComparer.Default.Equals(source, target))
            return false;

        // A target that mentions none loses them all, which is nearly every case: object, a base class, a non-generic interface.
        if (!Carries(target))
            return Carries(source);

        var sourceTypes = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
        var targetTypes = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
        var visited = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
        CollectNonCopyableTypes(source, sourceTypes, visited);
        visited.Clear();
        CollectNonCopyableTypes(target, targetTypes, visited);

        return !sourceTypes.IsSubsetOf(targetTypes);
    }

    /// <summary>
    /// Gets a value indicating whether a type, without its base types, is one whose own code decides what it holds: it has a container
    /// bound to a marked type parameter, or it is a container only through what it derives from or implements.
    /// </summary>
    private bool IsOpenLevel(INamedTypeSymbol type)
    {
        if (!IsContainer(type))
            return false;

        bool carriesThroughArguments = false;

        for (var current = type; current is not null; current = current.ContainingType)
        {
            var typeParameters = current.OriginalDefinition.TypeParameters;

            for (int i = 0; i < current.TypeArguments.Length && i < typeParameters.Length; i++)
            {
                if (!Carries(current.TypeArguments[i]))
                    continue;

                carriesThroughArguments = true;

                if (IsMarkedNonCopyable(typeParameters[i]) && MentionsContainer(current.TypeArguments[i]))
                    return true;
            }
        }

        return !carriesThroughArguments;
    }

    private bool MentionsBoundContainer(ITypeSymbol type, ISymbol member, int depth)
    {
        RuntimeHelpers.EnsureSufficientExecutionStack();

        const int MaxDepth = 16;

        if (depth > MaxDepth)
            return false;

        switch (type)
        {
            case ITypeParameterSymbol typeParameter:
                return IsMarkedNonCopyable(typeParameter) && IsContainer(GetBoundType(typeParameter, member));

            case IArrayTypeSymbol array:
                return MentionsBoundContainer(array.ElementType, member, depth + 1);

            case INamedTypeSymbol named:
                if (GetAllTypeArguments(named).Any(argument => MentionsBoundContainer(argument, member, depth + 1)))
                    return true;

                return false;

            default:
                return false;
        }
    }

    /// <summary>
    /// Gets what is bound to a type parameter of a member, or of a type that contains it, in the member as it is constructed.
    /// </summary>
    private static ITypeSymbol? GetBoundType(ITypeParameterSymbol typeParameter, ISymbol member)
    {
        if (typeParameter.TypeParameterKind is TypeParameterKind.Method)
        {
            return member is IMethodSymbol method && typeParameter.Ordinal < method.TypeArguments.Length
                ? method.TypeArguments[typeParameter.Ordinal]
                : null;
        }

        for (var type = member.ContainingType; type is not null; type = type.ContainingType)
        {
            if (SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, typeParameter.ContainingType) && typeParameter.Ordinal < type.TypeArguments.Length)
                return type.TypeArguments[typeParameter.Ordinal];
        }

        return null;
    }

    private bool IsElementTypeOf(ITypeSymbol type, ITypeSymbol? holder, int depth)
    {
        RuntimeHelpers.EnsureSufficientExecutionStack();

        const int MaxDepth = 8;

        if (depth > MaxDepth)
            return false;

        if (holder is IArrayTypeSymbol array)
            return SymbolEqualityComparer.Default.Equals(type, array.ElementType);

        if (holder is not INamedTypeSymbol named)
            return false;

        if (GetAllTypeArguments(named).Contains(type, SymbolEqualityComparer.Default))
            return true;

        return GetBaseTypeAndInterfaces(named).Any(inherited => IsElementTypeOf(type, inherited, depth + 1));
    }

    private bool HoldsContainers(ITypeSymbol? type, int depth)
    {
        RuntimeHelpers.EnsureSufficientExecutionStack();

        const int MaxDepth = 8;

        if (depth > MaxDepth || !IsContainer(type))
            return false;

        if (type is IArrayTypeSymbol array)
            return MentionsContainer(array.ElementType);

        if (type is not INamedTypeSymbol named)
            return false;

        if (GetAllTypeArguments(named).Any(MentionsContainer))
            return true;

        return GetBaseTypeAndInterfaces(named).Any(inherited => HoldsContainers(inherited, depth + 1));
    }

    /// <summary>
    /// Gets a value indicating whether a type is a container or has one among its type arguments, at any depth: a
    /// <c>KeyValuePair&lt;Guard, Task&lt;Guard&gt;&gt;</c> is a value that holds a container.
    /// </summary>
    private bool MentionsContainer(ITypeSymbol? type)
    {
        RuntimeHelpers.EnsureSufficientExecutionStack();

        if (type is null)
            return false;

        if (IsContainer(type))
            return true;

        // The type arguments of the types that contain it count as its own do: 'Outer<Task<Guard>>.Entry<Guard>'.
        if (GetAllTypeArguments(type as INamedTypeSymbol).Any(MentionsContainer))
            return true;

        return false;
    }

    private bool HidesDirectly(ITypeSymbol source, ITypeSymbol target) =>
        (IsContainer(source) && LosesNonCopyableType(source, target) && ExposesNonCopyableType(source, visited: null)) ||
        IsCloningUse(source, target);

    /// <summary>
    /// Gets a value indicating whether a conversion hands an object with a binding that no declaration of the compilation judged to code
    /// that can clone it (see <see cref="FindUnjudgedCloningBinding"/>): to a type that no longer mentions the non-copyable type, or to an
    /// interface, which can be converted on from there to one that clones, such as ICloneable, with nothing left that shows the binding.
    /// </summary>
    public bool IsCloningUse([NotNullWhen(true)] ITypeSymbol? source, [NotNullWhen(true)] ITypeSymbol? target) =>
        source is INamedTypeSymbol { TypeKind: TypeKind.Class } named && target is not null &&
        !SymbolEqualityComparer.Default.Equals(source, target) &&
        (target.TypeKind is TypeKind.Interface || target.SpecialType is SpecialType.System_Object || target.TypeKind is TypeKind.Dynamic || LosesNonCopyableType(source, target)) &&
        FindUnjudgedCloningBinding(named) is not null;

    /// <summary>
    /// Gets a value indicating whether the symbol list marks a class 'clones': its own code clones its objects.
    /// </summary>
    public bool IsClonesClass(INamedTypeSymbol type) => list.IsClones(type.OriginalDefinition);

    /// <summary>
    /// Gets a value indicating whether the members that clone an object of a class that clones itself, or hand it on, are known for the
    /// members of a class in its chain, so that a member that no line names is taken to do neither. For a member of the class that clones
    /// itself or of a class above it, the line of the class that clones itself says 'clones=complete'. For a member of a class between
    /// it and the object's own class, a line for that class says 'clones=complete', or the line of the class that clones itself says it
    /// and covers the class: it is of the same assembly, or of one that was built with the analyzer, whose members that clone have their
    /// lines by the warning of SCA1008 there. A plain 'clones' line for the class between changes neither.
    /// </summary>
    public bool AreCloningMembersKnown(INamedTypeSymbol declaring, INamedTypeSymbol cloning) =>
        IsAtOrAbove(declaring, cloning)
            ? list.IsComplete(cloning.OriginalDefinition)
            : list.IsComplete(declaring.OriginalDefinition) || (IsCoveredByLinesOf(declaring, cloning) && list.IsComplete(cloning.OriginalDefinition));

    /// <summary>
    /// Gets a value indicating whether the line of the class that clones itself is what says whether the members of a class in its chain
    /// are known: the class is that class or one above it, or a class between of the same assembly or of one that was built with the
    /// analyzer. Otherwise a line for the class itself says it.
    /// </summary>
    public bool IsJudgedByLineOf(INamedTypeSymbol declaring, INamedTypeSymbol cloning) =>
        IsAtOrAbove(declaring, cloning) || IsCoveredByLinesOf(declaring, cloning);

    /// <summary>
    /// Gets a member of a class below the class that declares a member, in the chain of an object's class, that the code of the
    /// declaring class can run on the object and that can clone it. Null if there is none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A line for a member says what the code of that member does, and that code can call a member of its own object that a class below
    /// put in the place of one of its class: an override of a virtual member, or the member that implements a member of an interface
    /// for the object, which the code reaches where it uses its object as the interface. The analyzer cannot tell which member calls
    /// which, so a member of a base class is judged by every such member below its class. The code of a member that can be run can run
    /// the members below its own class in turn, so the walk goes down from the declaring class, and each class that has a member which
    /// can be run adds its own virtual members to what the classes below it can replace.
    /// </para>
    /// <para>
    /// Such a member can clone the object where its 'clones' line says so, in any assembly. One with no line can where nothing says
    /// what the members of its class do: the class is of an assembly that was not built with the analyzers, and no line that says
    /// 'clones=complete' covers it. A line for the member itself, with no marker, says that it neither clones the object nor hands it
    /// on.
    /// </para>
    /// <para>
    /// Four things are left out. A member of the class that clones itself or of a class above it, which the lines of that class speak
    /// for: they say what the members of those classes do with an object of it, where they run each other included. Such a member
    /// still takes part in what the classes below can replace, and is the answer in one case only: it is an override that a 'clones'
    /// line names, and the member that is used has no line of its own. The lines then say that the override clones and nothing of the
    /// member that can run it, as with a template method whose step the class that clones itself overrides with a clone; a line for
    /// the member says whether it runs the override. A member that takes the place of one that clones already: a member that
    /// runs it counts as one that clones by the lines, by its own line or by what 'clones=complete' says of a member with none,
    /// whatever stands in its place. A member of a class of the framework, which is never the answer either: the scan of the framework
    /// followed the framework's own overrides. And everything where the declaring class is <see cref="object"/>, whose code calls no
    /// member of its object that a class can replace.
    /// </para>
    /// </remarks>
    /// <param name="receiver">The class of the object.</param>
    /// <param name="declaring">The class that declares the member that is used.</param>
    /// <param name="cloning">The class that clones itself.</param>
    /// <param name="memberHasLine">Whether a line names the member that is used.</param>
    /// <returns>The member, whether a 'clones' line names it, and whether it is of the class that clones itself or of a class above
    /// it.</returns>
    public (ISymbol Member, bool Clones, bool IsOfCloningClass)? FindMemberBelowThatCanClone(
        INamedTypeSymbol receiver,
        INamedTypeSymbol declaring,
        INamedTypeSymbol cloning,
        bool memberHasLine)
    {
        if (declaring.SpecialType is SpecialType.System_Object || declaring.TypeKind is not TypeKind.Class || !IsAtOrAbove(declaring, receiver))
            return null;

        var declaringDefinition = declaring.OriginalDefinition;
        var below = new List<INamedTypeSymbol>();
        var lowest = declaring;

        for (var current = receiver; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, declaringDefinition))
            {
                lowest = current;
                break;
            }

            below.Add(current);
        }

        ISymbol? unknown = null;

        // From the class below the declaring class down to the object's class. 'lowest' is the lowest class whose code can run.
        for (int i = below.Count - 1; i >= 0; i--)
        {
            bool runs = false;

            foreach (var member in GetMembersThatRunFor(below[i], lowest))
            {
                runs = true;

                // A member of the framework only passes the call on.
                if (member.ContainingType is not { } memberType || FrameworkAssemblies.Contains(memberType.ContainingAssembly))
                    continue;

                // So does one of the class that clones itself or of a class above it, but for an override that a 'clones' line names
                // where no line says what the member that is used does.
                if (IsAtOrAbove(memberType, cloning))
                {
                    if (!memberHasLine && IsCloningMember(member) && member is IMethodSymbol { IsOverride: true } or IPropertySymbol { IsOverride: true } or IEventSymbol { IsOverride: true })
                        return (member, true, true);

                    continue;
                }

                if (IsCloningMember(member))
                    return (member, true, false);

                bool known = SymbolEqualityComparer.Default.Equals(memberType.ContainingAssembly, compilationAssembly) ||
                    AnalyzedAssemblies.IsAnalyzed(memberType.ContainingAssembly) ||
                    AreCloningMembersKnown(memberType, cloning);

                if (!known && unknown is null && !IsListed(member))
                    unknown = member;
            }

            if (runs)
                lowest = below[i];
        }

        return unknown is null ? null : (unknown, false, false);
    }

    // The members that a class puts in the place of members which the code of a class above it can call on its own object: the
    // overrides of a member that the class above has or inherits, and the members that implement a member of an interface for the
    // class where the class below it has another one or none. One that takes the place of a member that clones is left out.
    private IEnumerable<ISymbol> GetMembersThatRunFor(INamedTypeSymbol type, INamedTypeSymbol lowest)
    {
        foreach (var member in type.GetMembers())
        {
            if (member is not (IMethodSymbol { IsOverride: true, MethodKind: MethodKind.Ordinary } or IPropertySymbol { IsOverride: true } or IEventSymbol { IsOverride: true }))
                continue;

            ISymbol? first = null;
            bool replacesCloningMember = false;

            for (var overridden = GetOverridden(member); overridden is not null; overridden = GetOverridden(overridden))
            {
                first = overridden;
                replacesCloningMember |= IsCloningMember(overridden);
            }

            if (first?.ContainingType is { } firstType && IsAtOrAbove(firstType, lowest) && !replacesCloningMember)
                yield return member;
        }

        // The code of a class can use its object as an interface that it does not implement itself, after a test of the object's type,
        // so every interface of the class counts, and not only those of the class above.
        foreach (var contract in type.AllInterfaces)
        {
            foreach (var contractMember in contract.GetMembers())
            {
                if (contractMember.IsStatic || contractMember is not (IMethodSymbol { MethodKind: MethodKind.Ordinary } or IPropertySymbol or IEventSymbol))
                    continue;

                var implementation = type.FindImplementationForInterfaceMember(contractMember);
                var replaced = type.BaseType?.FindImplementationForInterfaceMember(contractMember);

                if (replaced is not null && IsCloningMember(replaced))
                    continue;

                // In a compiled class, an explicit implementation can be one that the compiler wrote to hand the call on to a member that
                // the class inherits, as it does where the class lists the interface again and a class between has a member that fits.
                // What it does cannot be read, so the member that it would hand the call on to counts as well, and is named first. For
                // a property or an event the compiler writes only the accessors, and the class then has no member that implements it.
                bool isCompiled = type.DeclaringSyntaxReferences.IsEmpty;

                if (isCompiled && (implementation is null ? HasAccessorOfItsOwn(type, contractMember) : IsOwnExplicitImplementation(type, implementation)) &&
                    FindInheritedMemberThatFits(type, contractMember, lowest) is { } inherited &&
                    !SymbolEqualityComparer.Default.Equals(replaced?.OriginalDefinition, inherited.OriginalDefinition))
                {
                    yield return inherited;
                }

                if (implementation?.ContainingType is not { } implementationType || SymbolEqualityComparer.Default.Equals(replaced?.OriginalDefinition, implementation.OriginalDefinition))
                    continue;

                // A member of the class above, or of a class above that one, is code that already runs.
                if (implementationType.TypeKind is not TypeKind.Class || IsAtOrAbove(implementationType, lowest))
                    continue;

                yield return implementation;
            }
        }

        static ISymbol? GetOverridden(ISymbol member) => member switch {
            IMethodSymbol method => method.OverriddenMethod,
            IPropertySymbol property => property.OverriddenProperty,
            IEventSymbol @event => @event.OverriddenEvent,
            _ => null,
        };

        static bool IsOwnExplicitImplementation(INamedTypeSymbol type, ISymbol member) =>
            SymbolEqualityComparer.Default.Equals(member.ContainingType, type) && member switch {
                IMethodSymbol method => !method.ExplicitInterfaceImplementations.IsEmpty,
                IPropertySymbol property => !property.ExplicitInterfaceImplementations.IsEmpty,
                IEventSymbol @event => !@event.ExplicitInterfaceImplementations.IsEmpty,
                _ => false,
            };

        static bool HasAccessorOfItsOwn(INamedTypeSymbol type, ISymbol contractMember)
        {
            var accessors = contractMember switch {
                IPropertySymbol property => (property.GetMethod, property.SetMethod),
                IEventSymbol @event => (@event.AddMethod, @event.RemoveMethod),
                _ => default((IMethodSymbol?, IMethodSymbol?)),
            };

            return IsOfType(accessors.Item1) || IsOfType(accessors.Item2);

            bool IsOfType(IMethodSymbol? accessor) =>
                accessor is not null && SymbolEqualityComparer.Default.Equals(type.FindImplementationForInterfaceMember(accessor)?.ContainingType, type);
        }

        static ISymbol? FindInheritedMemberThatFits(INamedTypeSymbol type, ISymbol contractMember, INamedTypeSymbol lowest)
        {
            var lowestDefinition = lowest.OriginalDefinition;

            for (var current = type.BaseType; current is not null && !SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, lowestDefinition); current = current.BaseType)
            {
                foreach (var candidate in current.GetMembers(contractMember.Name))
                {
                    if (candidate is { IsStatic: false, DeclaredAccessibility: Accessibility.Public } && Fits(candidate, contractMember))
                        return candidate;
                }
            }

            return null;
        }

        static bool Fits(ISymbol candidate, ISymbol contractMember) => (candidate, contractMember) switch {
            (IMethodSymbol { MethodKind: MethodKind.Ordinary } method, IMethodSymbol contract) =>
                method.Arity == contract.Arity && HaveSameTypes(method.Parameters, contract.Parameters, method.Arity > 0),
            (IPropertySymbol property, IPropertySymbol contract) => HaveSameTypes(property.Parameters, contract.Parameters, isGeneric: false),
            (IEventSymbol, IEventSymbol) => true,
            _ => false,
        };

        // The parameters of a generic method name its own type parameters, which are not those of the interface's method, so only their
        // number is compared there.
        static bool HaveSameTypes(ImmutableArray<IParameterSymbol> parameters, ImmutableArray<IParameterSymbol> contract, bool isGeneric)
        {
            if (parameters.Length != contract.Length)
                return false;

            for (int i = 0; i < parameters.Length && !isGeneric; i++)
            {
                if (!SymbolEqualityComparer.Default.Equals(parameters[i].Type, contract[i].Type))
                    return false;
            }

            return true;
        }
    }

    private static bool IsAtOrAbove(INamedTypeSymbol declaring, INamedTypeSymbol cloning)
    {
        var declaringDefinition = declaring.OriginalDefinition;

        for (var current = cloning.OriginalDefinition; current is not null; current = current.BaseType?.OriginalDefinition)
        {
            if (SymbolEqualityComparer.Default.Equals(current, declaringDefinition))
                return true;
        }

        return false;
    }

    // A class between the class that clones itself and the object's own class, whose members the lines of the class that clones itself
    // cover: one of the same assembly, as the lines for an assembly cover the classes of that assembly, or one of an assembly that was
    // built with the analyzers, which named its members that clone there.
    private static bool IsCoveredByLinesOf(INamedTypeSymbol declaring, INamedTypeSymbol cloning) =>
        SymbolEqualityComparer.Default.Equals(declaring.ContainingAssembly, cloning.ContainingAssembly) || AnalyzedAssemblies.IsAnalyzed(declaring.ContainingAssembly);

    /// <summary>
    /// Gets the type as which a member can hand the object that it is called on to other code, as far as its signature shows, where that
    /// hides the object's binding. Null where it has none. One walk over the types of the signature decides it, by polarity: what comes
    /// out of the member (a result, a 'ref' or 'out' parameter, a property, an indexer or a field that can be read) starts at "out", what
    /// goes in (a parameter, a 'ref' parameter as well, a property, an indexer or a field that can be set, the handler of an event) starts
    /// at "in". The parts of a type keep its polarity, and a delegate turns it round for its parameters. The member can hand its object on
    /// where a type that the object converts to by a hiding conversion stands at "out": so an 'Action&lt;Doc&gt;' parameter can, and an
    /// 'Action&lt;Action&lt;Doc&gt;&gt;' parameter, which only lets the caller give the member a sink, cannot.
    /// </summary>
    public static ITypeSymbol? FindHandBackType(INamedTypeSymbol receiver, ISymbol member)
    {
        var outputs = new List<ITypeSymbol>();
        var inputs = new List<ITypeSymbol>();

        switch (member)
        {
            case IMethodSymbol method:
                if (!method.ReturnsVoid)
                    outputs.Add(method.ReturnType);

                AddParameters(method.Parameters);
                break;

            case IPropertySymbol property:
                if (property.GetMethod is not null || property.ReturnsByRef)
                    outputs.Add(property.Type);

                if (property.SetMethod is not null || property.ReturnsByRef)
                    inputs.Add(property.Type);

                AddParameters(property.Parameters);
                break;

            case IFieldSymbol field:
                outputs.Add(field.Type);

                if (!field.IsReadOnly && !field.IsConst)
                    inputs.Add(field.Type);

                break;

            case IEventSymbol @event:
                inputs.Add(@event.Type);
                break;
        }

        return outputs.FirstOrDefault(output => Flows(output, comesOut: true, 0)) ?? inputs.FirstOrDefault(input => Flows(input, comesOut: false, 0));

        void AddParameters(ImmutableArray<IParameterSymbol> parameters)
        {
            foreach (var parameter in parameters)
            {
                inputs.Add(parameter.Type);

                if (parameter.RefKind is RefKind.Ref or RefKind.Out)
                    outputs.Add(parameter.Type);
            }
        }

        // Whether a hiding type stands in the type at "out": where a value of it comes out of the member to other code. A type argument is
        // walked by what its type can do with it: one of an 'out' type parameter keeps the polarity, one of an 'in' type parameter turns it
        // round, and one of a type parameter with no variance, of a class, a struct or an invariant interface, goes both ways, since the
        // type can take a value in and hand one out. The type arguments of the types that contain a type count as well. An element of an
        // array and what a pointer points at go both ways, since both can be written. A tuple and Nullable<T> are passed by value, so their
        // elements keep the polarity. A constraint of a type parameter keeps it. A delegate or a function pointer keeps the polarity for its
        // result and turns it round for its parameters, and its 'ref' and 'out' parameters go both ways.
        bool Flows(ITypeSymbol type, bool comesOut, int depth)
        {
            if (depth > 16)
                return false;

            if (comesOut && IsHiding(type))
                return true;

            switch (type)
            {
                case IArrayTypeSymbol array:
                    return Flows(array.ElementType, comesOut: true, depth + 1) || Flows(array.ElementType, comesOut: false, depth + 1);

                case IPointerTypeSymbol pointer:
                    return Flows(pointer.PointedAtType, comesOut: true, depth + 1) || Flows(pointer.PointedAtType, comesOut: false, depth + 1);

                case IFunctionPointerTypeSymbol functionPointer:
                    return FlowsThroughSignature(functionPointer.Signature, comesOut, depth);

                case INamedTypeSymbol { TypeKind: TypeKind.Delegate, DelegateInvokeMethod: { } invoke }:
                    return FlowsThroughSignature(invoke, comesOut, depth);

                case INamedTypeSymbol named:
                    for (var current = named; current is not null; current = current.ContainingType)
                    {
                        bool byValue = current.IsTupleType || current.OriginalDefinition.SpecialType is SpecialType.System_Nullable_T;
                        var parameters = current.OriginalDefinition.TypeParameters;

                        for (int i = 0; i < current.TypeArguments.Length; i++)
                        {
                            var variance = byValue || i >= parameters.Length ? VarianceKind.Out : parameters[i].Variance;
                            var argument = current.TypeArguments[i];

                            bool flows = variance switch {
                                VarianceKind.Out => Flows(argument, comesOut, depth + 1),
                                VarianceKind.In => Flows(argument, !comesOut, depth + 1),
                                _ => Flows(argument, comesOut: true, depth + 1) || Flows(argument, comesOut: false, depth + 1),
                            };

                            if (flows)
                                return true;
                        }
                    }

                    return false;

                case ITypeParameterSymbol typeParameter:
                    return typeParameter.ConstraintTypes.Any(constraint => Flows(constraint, comesOut, depth + 1));

                default:
                    return false;
            }
        }

        bool FlowsThroughSignature(IMethodSymbol signature, bool comesOut, int depth)
        {
            if (!signature.ReturnsVoid && Flows(signature.ReturnType, comesOut, depth + 1))
                return true;

            foreach (var parameter in signature.Parameters)
            {
                if (Flows(parameter.Type, !comesOut, depth + 1))
                    return true;

                if (parameter.RefKind is RefKind.Ref or RefKind.Out && Flows(parameter.Type, comesOut, depth + 1))
                    return true;
            }

            return false;
        }

        bool IsHiding(ITypeSymbol output) => output switch {
            ITypeParameterSymbol typeParameter => !typeParameter.HasValueTypeConstraint && !typeParameter.HasUnmanagedTypeConstraint,
            { SpecialType: SpecialType.System_Object } or { TypeKind: TypeKind.Dynamic } => true,
            INamedTypeSymbol { TypeKind: TypeKind.Interface } @interface => receiver.AllInterfaces.Contains(@interface, SymbolEqualityComparer.Default),
            INamedTypeSymbol { TypeKind: TypeKind.Class } @class => !SymbolEqualityComparer.Default.Equals(@class, receiver) && IsBaseOf(@class, receiver),
            _ => false,
        };

        static bool IsBaseOf(INamedTypeSymbol @class, INamedTypeSymbol type)
        {
            for (var current = type.BaseType; current is not null; current = current.BaseType)
            {
                if (SymbolEqualityComparer.Default.Equals(current, @class))
                    return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Gets the member that a call of a virtual member runs for an object of a type, as far as the type says: the override in the type or
    /// the nearest of its base classes, or the member itself.
    /// </summary>
    public static ISymbol FindImplementationIn(INamedTypeSymbol type, ISymbol member)
    {
        if (member is not (IMethodSymbol { IsVirtual: true } or IMethodSymbol { IsOverride: true } or IMethodSymbol { IsAbstract: true } or
            IPropertySymbol { IsVirtual: true } or IPropertySymbol { IsOverride: true } or IPropertySymbol { IsAbstract: true }))
        {
            return member;
        }

        var original = member.OriginalDefinition;

        for (var current = type; current is not null; current = current.BaseType)
        {
            foreach (var candidate in current.GetMembers(member.Name))
            {
                for (ISymbol? overridden = candidate; overridden is not null; overridden = overridden switch { IMethodSymbol m => m.OverriddenMethod, IPropertySymbol p => p.OverriddenProperty, _ => null })
                {
                    if (SymbolEqualityComparer.Default.Equals(overridden.OriginalDefinition, original))
                        return candidate;
                }
            }
        }

        return member;
    }

    /// <summary>
    /// Gets a value indicating whether the declaration of a class binds a non-copyable type to a type parameter of a class whose objects a
    /// class that clones itself clones, in its base list: the place where the class gets the warning for it (SCA1009).
    /// </summary>
    public bool BindsInItsBaseList(INamedTypeSymbol declaration) =>
        declaration.BaseType is { } baseType &&
        !SymbolEqualityComparer.Default.Equals(baseType, baseType.OriginalDefinition) &&
        FindCloningBinding(baseType) is not null;

    /// <summary>
    /// Gets a value indicating whether a class has a binding to a class that clones itself which a declaration of the compilation judged:
    /// its base list, or that of one of its base classes, got the warning for it. Its objects get none where they are used.
    /// </summary>
    public bool HasJudgedCloningBinding(ITypeSymbol? type) =>
        type is INamedTypeSymbol { TypeKind: TypeKind.Class } named && FindCloningBinding(named) is not null && FindUnjudgedCloningBinding(named) is null;

    /// <summary>
    /// Gets a binding that <see cref="FindUnjudgedCloningBinding"/> finds in a type or in what it holds: its type arguments, those of the
    /// types that contain it, and the element type of an array. A conversion that hides a container of such objects hands them on too.
    /// </summary>
    public CloningBinding? FindCloningBindingWithin(ITypeSymbol? type, int depth = 0)
    {
        RuntimeHelpers.EnsureSufficientExecutionStack();

        const int MaxDepth = 8;

        if (type is null || depth > MaxDepth || !Carries(type))
            return null;

        switch (type)
        {
            case IArrayTypeSymbol array:
                return FindCloningBindingWithin(array.ElementType, depth + 1);

            case INamedTypeSymbol named:
                if (FindUnjudgedCloningBinding(named) is { } binding)
                    return binding;

                foreach (var argument in GetAllTypeArguments(named))
                {
                    if (FindCloningBindingWithin(argument, depth + 1) is { } within)
                        return within;
                }

                return null;

            default:
                return null;
        }
    }

    /// <summary>
    /// Gets the binding of a non-copyable type to a type parameter of a class whose objects a class that clones itself clones, where no
    /// declaration of the compilation judged it: the class is constructed with it, as in 'Tagged&lt;Guard&gt;', or it is in the base list
    /// of a class of another assembly. Null for a class of the compilation that binds it in a base list of its own or of one of its base
    /// classes, which get the warning there (SCA1005), and for a class with no such binding.
    /// </summary>
    /// <remarks>
    /// An object of such a class is cloned only by code that the analyzer does not see: a member of the cloning class or of a class above
    /// it that it is the receiver or an argument of, or code that it reaches as one of those classes or as an interface. Each of those is
    /// a use of the object as a type that hides the binding, which the hiding checks see wherever it is written, and which they report
    /// for such an object.
    /// </remarks>
    public CloningBinding? FindUnjudgedCloningBinding(INamedTypeSymbol type)
    {
        RuntimeHelpers.EnsureSufficientExecutionStack();

        if (type.TypeKind is not TypeKind.Class)
            return null;

        if (_unjudgedCloningBindings.TryGetValue(type, out var known))
            return known;

        CloningBinding? found = null;

        if (FindCloningBinding(type) is { } binding)
        {
            if (SymbolEqualityComparer.Default.Equals(binding.Bound, type))
            {
                found = binding;
            }
            else
            {
                // The class whose base list names the bound class: in the compilation, it got the warning on that base list where its
                // declaration binds a non-copyable type there. A class that passes an unmarked type parameter on, as 'DU<T> : Tagged<T>',
                // binds nothing that its declaration shows, and got no such warning: its objects are judged where they are used.
                for (var current = type; current?.BaseType is { } baseType; current = baseType)
                {
                    if (SymbolEqualityComparer.Default.Equals(baseType, binding.Bound))
                    {
                        if (!SymbolEqualityComparer.Default.Equals(current.ContainingAssembly, compilationAssembly) || !BindsInItsBaseList(current.OriginalDefinition))
                            found = binding;

                        break;
                    }
                }
            }
        }

        _unjudgedCloningBindings[type] = found;
        return found;
    }

    private (ITypeSymbol Container, ITypeSymbol HidingType)? FindHiddenByVariance(ITypeSymbol source, ITypeSymbol target, int depth)
    {
        RuntimeHelpers.EnsureSufficientExecutionStack();

        const int MaxDepth = 8;

        if (depth > MaxDepth ||
            target is not INamedTypeSymbol { IsGenericType: true, TypeKind: TypeKind.Interface or TypeKind.Delegate } targetType ||
            (!Carries(source) && !Carries(target)))
        {
            return null;
        }

        var definition = targetType.OriginalDefinition;
        var typeParameters = definition.TypeParameters;

        if (!typeParameters.Any(p => p.Variance is not VarianceKind.None))
            return null;

        // The constructions of the target's definition that the source is or implements: the conversion goes through one of them.
        var constructions = new List<INamedTypeSymbol>();

        if (source is INamedTypeSymbol named && SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, definition))
        {
            constructions.Add(named);
        }
        else
        {
            foreach (var @interface in source.AllInterfaces)
            {
                if (SymbolEqualityComparer.Default.Equals(@interface.OriginalDefinition, definition))
                    constructions.Add(@interface);
            }
        }

        // With the target itself among them no variance is needed.
        if (constructions.Contains(targetType, SymbolEqualityComparer.Default))
            return null;

        foreach (var construction in constructions)
        {
            for (int i = 0; i < typeParameters.Length && i < construction.TypeArguments.Length && i < targetType.TypeArguments.Length; i++)
            {
                var from = construction.TypeArguments[i];
                var to = targetType.TypeArguments[i];

                if (typeParameters[i].Variance is VarianceKind.None || SymbolEqualityComparer.Default.Equals(from, to))
                    continue;

                // What comes out goes from the source's type argument to the target's, and what goes in the other way.
                var (given, received) = typeParameters[i].Variance is VarianceKind.Out ? (from, to) : (to, from);

                if (HidesDirectly(given, received))
                    return (given, received);

                if (FindHiddenByVariance(given, received, depth + 1) is { } nested)
                    return nested;
            }
        }

        return null;
    }

    private static bool HasMemberWithBody(INamedTypeSymbol interfaceDefinition) =>
        interfaceDefinition.GetMembers().Any(m => m is IMethodSymbol { IsAbstract: false, IsStatic: false });

    private CopyKind GetKind(ITypeSymbol? type, KindComputation? computation)
    {
        RuntimeHelpers.EnsureSufficientExecutionStack();

        switch (type)
        {
            case null:
                return CopyKind.Copyable;

            case ITypeParameterSymbol typeParameter:
                return !typeParametersAreCopyable && IsMarkedNonCopyable(typeParameter) ? CopyKind.NonCopyable : CopyKind.Copyable;

            case IArrayTypeSymbol array:
                return GetKind(array.ElementType, computation) is CopyKind.Copyable ? CopyKind.Copyable : CopyKind.Container;

            case INamedTypeSymbol named:
                if (_kinds.TryGetValue(named, out var kind))
                    return kind;

                // A type can name itself among its base types and interfaces, as in 'class Node : IEquatable<Node>'. A type that is met again
                // while it is being classified counts as copyable for the time being: whether it carries a non-copyable type has to come from
                // somewhere else.
                computation ??= new KindComputation();

                if (!computation.InProgress.Add(named))
                {
                    computation.MetAgain.Add(named);
                    return CopyKind.Copyable;
                }

                kind = GetKindOfNamedType(named, computation);
                computation.InProgress.Remove(named);
                computation.MetAgain.Remove(named);

                // What was found while a type further out was taken for copyable holds only for now, and is not kept.
                if (computation.MetAgain.Count is 0)
                    _kinds[named] = kind;

                return kind;

            default:
                // Pointers, function pointers and dynamic are not covered.
                return CopyKind.Copyable;
        }
    }

    private CopyKind GetKindOfNamedType(INamedTypeSymbol type, KindComputation computation)
    {
        bool isStruct = type.TypeKind is TypeKind.Struct;

        if (isStruct && IsNonCopyableByItself(type))
            return CopyKind.NonCopyable;

        // The types that the language has keywords for, and their like: none of them carries anything.
        if (type.SpecialType is not SpecialType.None && !type.IsGenericType)
            return CopyKind.Copyable;

        bool carries = false;
        bool hasNonCopyableArgument = false;

        // The type arguments of the containing types count: a List<Guard>.Enumerator holds a Guard.
        for (var current = type; current is not null; current = current.ContainingType)
        {
            foreach (var argument in current.TypeArguments)
            {
                var argumentKind = GetKind(argument, computation);
                carries |= argumentKind is not CopyKind.Copyable;
                hasNonCopyableArgument |= argumentKind is CopyKind.NonCopyable;
            }
        }

        // A generic struct stores its type arguments inline, as far as anyone can tell from outside, so copying it copies them. A view is the
        // exception: its copies share the elements.
        if (isStruct && hasNonCopyableArgument && !list.IsView(type.OriginalDefinition))
            return CopyKind.NonCopyable;

        if (carries)
            return CopyKind.Container;

        // A constructed type with nothing but copyable types bound is what its definition is with copyable types bound to its type
        // parameters: its base type and interfaces are the definition's with those types put in.
        var definition = type.OriginalDefinition;

        if (!SymbolEqualityComparer.Default.Equals(type, definition))
        {
            return typeParametersAreCopyable
                ? GetKind(definition, computation)
                : (definitions ?? throw new InvalidOperationException("No classifier for definitions.")).GetKind(definition);
        }

        // A type that has a container among its base types or its interfaces is a container as well: 'class Guards : List<Guard>'. Each of
        // them answers for its own base types and interfaces.
        if (GetKind(type.BaseType, computation) is not CopyKind.Copyable)
            return CopyKind.Container;

        foreach (var @interface in type.Interfaces)
        {
            if (GetKind(@interface, computation) is not CopyKind.Copyable)
                return CopyKind.Container;
        }

        return CopyKind.Copyable;
    }

    /// <summary>
    /// Gets a value indicating whether a type mentions a non-copyable type that does not sit inside an erasable type.
    /// </summary>
    private bool ExposesNonCopyableType(ITypeSymbol? type, HashSet<ITypeSymbol>? visited)
    {
        RuntimeHelpers.EnsureSufficientExecutionStack();

        switch (type)
        {
            case ITypeParameterSymbol:
                return IsNonCopyableType(type);

            case IArrayTypeSymbol array:
                return ExposesNonCopyableType(array.ElementType, visited);

            case INamedTypeSymbol named:
                if (!Carries(named) || IsErasable(named))
                    return false;

                if (named.TypeKind is TypeKind.Struct && IsNonCopyableByItself(named))
                    return true;

                if (!(visited ??= new(SymbolEqualityComparer.Default)).Add(named))
                    return false;

                foreach (var argument in GetAllTypeArguments(named))
                {
                    if (ExposesNonCopyableType(argument, visited))
                        return true;
                }

                // A type that derives from a container exposes what its base types and interfaces expose.
                return GetBaseTypeAndInterfaces(named).Any(inherited => ExposesNonCopyableType(inherited, visited));

            default:
                return false;
        }
    }

    /// <summary>
    /// Gets a value indicating whether a class or an interface of checked code is erasable with no list entry, because its marks and the check
    /// of its bodies guarantee what the marker only claims: a member that boxes a stored value of a non-copyable type, or hands out a container
    /// of it untyped, is reported inside the type. Everything is computed from the symbols, so a type of another assembly is judged the same way.
    /// </summary>
    /// <remarks>
    /// The object behind the type can be of a type that derives from it. What such a type can add is reported where that type is declared: see
    /// <see cref="FindErasureObstacle"/> for a base type or an interface, and the rules for marks and for covariant returns for the rest.
    /// </remarks>
    private bool IsErasableThroughMarks(INamedTypeSymbol type)
    {
        // Its own bodies are checked for every non-copyable type that it receives. A type that is not generic and names the non-copyable type
        // itself is checked code as it is.
        foreach (var (typeParameter, argument) in GetBoundTypeArguments(type))
        {
            if (Carries(argument) && !IsMarkedNonCopyable(typeParameter))
                return false;
        }

        if (FindErasureObstacle(type) is not null)
            return false;

        // No override in the type or its base classes has a return type that mentions a non-copyable type where the return type of the
        // overridden member does not: that hands a container out as the wider type with no conversion for a body check to see.
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (!ReferenceEquals(current, type) && list.IsErasable(current.OriginalDefinition))
                break;

            foreach (var member in current.GetMembers())
            {
                if (TryGetOverriddenReturn(member, out var returnType, out var overriddenReturnType, out _) &&
                    LosesNonCopyableType(returnType, overriddenReturnType))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private ErasureObstacle? FindUncheckedReceiver(INamedTypeSymbol baseType)
    {
        bool isInterface = baseType.TypeKind is TypeKind.Interface;

        for (var current = baseType; current is not null; current = current.ContainingType)
        {
            // An interface none of whose members has a body has no code that could copy or box anything.
            if (isInterface && !HasMemberWithBody(current.OriginalDefinition))
                continue;

            var typeParameters = current.OriginalDefinition.TypeParameters;

            for (int i = 0; i < current.TypeArguments.Length && i < typeParameters.Length; i++)
            {
                var argument = current.TypeArguments[i];

                if (!Carries(argument))
                    continue;

                if (!IsMarkedNonCopyable(typeParameters[i]))
                    return new ErasureObstacle(ErasureObstacleKind.UncheckedBody, current, typeParameters[i], argument);
            }
        }

        return null;
    }

    /// <summary>
    /// Finds a reference type that mentions a non-copyable type in a position of an interface that is covariant in effect: 'out', 'out'
    /// inside 'out', or 'in' inside 'in', with no invariant type parameter on the way. Only from there does variance convert the
    /// interface to one over object. A contravariant position converts toward more derived types only.
    /// </summary>
    private ErasureObstacle? FindVariance(ITypeSymbol type, INamedTypeSymbol @interface, bool isCovariant, int depth)
    {
        RuntimeHelpers.EnsureSufficientExecutionStack();

        const int MaxDepth = 16;

        if (depth > MaxDepth || !Carries(type))
            return null;

        // An array is covariant in its element type.
        if (type is IArrayTypeSymbol array)
            return FindVariance(array.ElementType, @interface, isCovariant, depth + 1);

        if (type is not INamedTypeSymbol { TypeKind: TypeKind.Interface or TypeKind.Delegate } named)
            return null;

        var typeParameters = named.OriginalDefinition.TypeParameters;

        for (int i = 0; i < named.TypeArguments.Length && i < typeParameters.Length; i++)
        {
            var argument = named.TypeArguments[i];

            // Variance needs a reference type. A type parameter can be bound to one unless it is constrained to a value type.
            bool canBeReferenceType = argument is ITypeParameterSymbol ? !argument.IsValueType : argument.IsReferenceType;

            if (typeParameters[i].Variance is VarianceKind.None || !canBeReferenceType || !Carries(argument))
                continue;

            bool isCovariantHere = typeParameters[i].Variance is VarianceKind.Out ? isCovariant : !isCovariant;

            if (isCovariantHere)
                return new ErasureObstacle(ErasureObstacleKind.Variance, named, typeParameters[i], argument, @interface);

            if (FindVariance(argument, @interface, isCovariantHere, depth + 1) is { } nested)
                return nested;
        }

        return null;
    }

    // The one place that decides whether a struct is non-copyable by itself. A 'copyable' line of the project's list takes it out before
    // the built-in entry and the attribute are read, so that fields, generic structs over it, containers and a marked type parameter
    // that it is bound to all follow.
    private bool IsNonCopyableByItself(INamedTypeSymbol type) =>
        !list.IsCopyable(type.OriginalDefinition) && (IsMarkedNonCopyable(type) || list.IsNonCopyable(type.OriginalDefinition));

    /// <summary>
    /// Gets a value indicating whether a struct is marked <c>[NonCopyable]</c>. On a struct that the compilation declares only the
    /// attribute of the analyzer counts, by its full name. On a struct of another assembly, a referenced project included, a class
    /// named NonCopyableAttribute of any namespace does: libraries that are already published mark their guards with an attribute
    /// of their own, most often an internal one in Microsoft.CodeAnalysis, and a project that uses such a library cannot change it.
    /// Reading the mark costs at worst a finding that can be seen and suppressed. Not reading it makes every guard of that library
    /// copyable in a build that looks clean.
    /// </summary>
    private bool IsMarkedNonCopyable(INamedTypeSymbol type)
    {
        bool isOfAnotherAssembly = !SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, compilationAssembly);

        foreach (var attribute in type.GetAttributes())
        {
            if (IsNonCopyableMark(attribute.AttributeClass, ofAnyNamespace: isOfAnotherAssembly))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Gets the use of a [RequiresCopyable] constructor that creating an object of a type with no argument amounts to: the
    /// parameterless constructor of the type, or a constructor of a base class that it calls with nothing written. Null if there is none.
    /// </summary>
    private MemberUse? FindRequiresCopyableConstructorUse(INamedTypeSymbol type)
    {
        return type.InstanceConstructors.FirstOrDefault(static c => c.Parameters.IsEmpty) is { } constructor ? Find(constructor, depth: 0) : null;

        // Nothing here has the body of a constructor, so every constructor that it can be calling counts.
        MemberUse? Find(IMethodSymbol constructor, int depth)
        {
            RuntimeHelpers.EnsureSufficientExecutionStack();

            if (GetUse(constructor) is { Kind: MemberUseKind.RequiresCopyable } use)
                return use;

            if (depth >= 64)
                return null;

            foreach (var called in GetBaseConstructorsCalledWithNothingWritten(constructor))
            {
                if (Find(called, depth + 1) is { } found)
                    return found;
            }

            return null;
        }
    }

    private MemberUse ClassifyUse(ISymbol member)
    {
        var definition = GetListDefinition(member);
        SafeListEntry? entry = null;
        bool entryLookedUp = false;
        (ITypeParameterSymbol Parameter, ITypeSymbol Argument)? listed = null;
        (ITypeParameterSymbol Parameter, ITypeSymbol Argument)? markedNonCopyable = null;
        (ITypeParameterSymbol Parameter, ITypeSymbol Argument)? markedContainer = null;
        (ITypeParameterSymbol Parameter, ITypeSymbol Argument)? unlistedNonCopyable = null;
        (ITypeParameterSymbol Parameter, ITypeSymbol Argument)? unlistedContainer = null;
        (ITypeParameterSymbol Parameter, ITypeSymbol Argument, ITypeSymbol Constraint)? hidingConstraint = null;
        (ITypeParameterSymbol Parameter, ITypeSymbol Argument, ITypeSymbol Constraint, ISymbol Member)? defaultMember = null;
        MemberUse? requiresCopyableConstructor = null;

        var bound = new List<(ITypeParameterSymbol Parameter, ITypeSymbol Argument)>();

        if (member is IMethodSymbol method)
        {
            var typeParameters = GetUnreduced(method).OriginalDefinition.TypeParameters;
            var typeArguments = method.TypeArguments;

            for (int i = 0; i < typeParameters.Length && i < typeArguments.Length; i++)
                bound.Add((typeParameters[i], typeArguments[i]));
        }

        for (var type = member.ContainingType; type is not null; type = type.ContainingType)
        {
            // A delegate type has no code of its own. Its target is checked where the delegate is created.
            if (type.TypeKind is TypeKind.Delegate)
                return default;

            var typeParameters = type.OriginalDefinition.TypeParameters;
            var typeArguments = type.TypeArguments;

            for (int i = 0; i < typeParameters.Length && i < typeArguments.Length; i++)
                bound.Add((typeParameters[i], typeArguments[i]));
        }

        foreach (var (typeParameter, typeArgument) in bound)
            Judge(typeParameter, typeArgument);

        if (markedNonCopyable is { } marked && IsMarkedRequiresCopyable(definition))
            return new MemberUse(MemberUseKind.RequiresCopyable, definition, marked.Parameter, marked.Argument);

        if (requiresCopyableConstructor is { } constructorUse)
            return constructorUse;

        // A member that the list names for other type parameters is not unlisted: the message says what the line covers.
        string? listedFor = entry?.TypeParameterNames is { } names ? string.Join("', '", names) : null;

        if (unlistedNonCopyable is { } nonCopyable)
            return new MemberUse(MemberUseKind.UnlistedNonCopyable, definition, nonCopyable.Parameter, nonCopyable.Argument, ListedFor: listedFor);

        if (unlistedContainer is { } container)
            return new MemberUse(MemberUseKind.UnlistedContainer, definition, container.Parameter, container.Argument, ListedFor: listedFor);

        if (hidingConstraint is { } hidden)
            return new MemberUse(MemberUseKind.ContainerHiddenByConstraint, definition, hidden.Parameter, hidden.Argument, hidden.Constraint);

        if (defaultMember is { } left)
            return new MemberUse(MemberUseKind.DefaultMemberOfConstraint, definition, left.Parameter, left.Argument, left.Constraint, left.Member);

        if (listed is { } covered)
        {
            var kind = entry!.Marker is SafeListMarker.New ? MemberUseKind.ListedNew : MemberUseKind.Listed;
            return new MemberUse(kind, definition, covered.Parameter, covered.Argument, Drains: entry.Marker is SafeListMarker.Drains);
        }

        if (markedContainer is { } boundContainer)
            return new MemberUse(MemberUseKind.MarkedContainer, definition, boundContainer.Parameter, boundContainer.Argument);

        return default;

        void Judge(ITypeParameterSymbol typeParameter, ITypeSymbol typeArgument)
        {
            var kind = GetKind(typeArgument);

            if (kind is CopyKind.Copyable)
                return;

            // The 'new()' constraint lets the generic call the parameterless constructor of what is bound, with no use of it to judge.
            if (typeParameter.HasConstructorConstraint &&
                requiresCopyableConstructor is null &&
                typeArgument is INamedTypeSymbol constructed &&
                FindRequiresCopyableConstructorUse(constructed) is { } use)
            {
                requiresCopyableConstructor = use;
            }

            // A generic struct that is non-copyable only through its type arguments cannot be judged where it is declared. A call through
            // the constraint of a member that it leaves to the interface runs on a boxed copy.
            if (defaultMember is null &&
                typeArgument is INamedTypeSymbol { TypeKind: TypeKind.Struct } boundStruct &&
                kind is CopyKind.NonCopyable &&
                !IsNonCopyableType(boundStruct.OriginalDefinition) &&
                FindDefaultMemberOfConstraint(typeParameter, boundStruct) is { } found)
            {
                defaultMember = (typeParameter, typeArgument, found.Constraint, found.Member);
            }

            if (IsMarkedNonCopyable(typeParameter))
            {
                // A container may be bound to a marked type parameter: the generic does not copy its values, and copying a container would
                // be fine anyway. What it must not have is a constraint that lets the generic use what is bound as a type which hides
                // the non-copyable type: no conversion shows that, here or in the generic. The same goes for a generic struct with
                // unchecked code, which runs that code behind such a type.
                if (kind is CopyKind.NonCopyable)
                    markedNonCopyable ??= (typeParameter, typeArgument);
                else
                    markedContainer ??= (typeParameter, typeArgument);

                if ((kind is CopyKind.Container || IsStructOfUncheckedCode(typeArgument)) &&
                    hidingConstraint is null &&
                    FindHidingConstraint(typeParameter, typeArgument, bound) is { } constraint)
                {
                    hidingConstraint = (typeParameter, typeArgument, constraint);
                }

                return;
            }

            if (!entryLookedUp)
            {
                entry = list.GetMember(definition);
                entryLookedUp = true;
            }

            if (entry?.Covers(typeParameter) is true)
                listed ??= (typeParameter, typeArgument);
            else if (kind is CopyKind.NonCopyable)
                unlistedNonCopyable ??= (typeParameter, typeArgument);
            else
                unlistedContainer ??= (typeParameter, typeArgument);
        }
    }

    /// <summary>
    /// Gets a value indicating whether a type is a type parameter or has one in it: as a type argument, as the element type of an
    /// array, or as a type argument of a type that contains it.
    /// </summary>
    public static bool MentionsTypeParameter(ITypeSymbol type)
    {
        RuntimeHelpers.EnsureSufficientExecutionStack();

        return type switch {
            ITypeParameterSymbol => true,
            IArrayTypeSymbol array => MentionsTypeParameter(array.ElementType),
            INamedTypeSymbol named => named.TypeArguments.Any(MentionsTypeParameter) || (named.ContainingType is { } containing && MentionsTypeParameter(containing)),
            _ => false,
        };
    }

    /// <summary>
    /// Gets the constraint of a type parameter that hides the non-copyable type of what is bound to it: a type that does not mention it. A
    /// constraint that names a type parameter is judged with the type arguments filled in, and counts as hiding when that cannot be done.
    /// </summary>
    private ITypeSymbol? FindHidingConstraint(
        ITypeParameterSymbol typeParameter, ITypeSymbol argument, List<(ITypeParameterSymbol Parameter, ITypeSymbol Argument)> bound)
    {
        foreach (var constraint in typeParameter.ConstraintTypes)
        {
            if (Substitute(constraint, bound) is not { } filledIn)
                return constraint;

            bool hides = GetKind(argument) is CopyKind.Container
                ? HidesNonCopyableType(argument, filledIn)
                : LosesNonCopyableType(argument, filledIn);

            if (hides)
                return filledIn;
        }

        return null;
    }

    /// <summary>
    /// Replaces the type parameters in a type by what is bound to them. Null if the type has a type parameter that nothing is bound to, or
    /// one in a place where it cannot be replaced here: an array, or a type that is nested in a generic type.
    /// </summary>
    private static ITypeSymbol? Substitute(ITypeSymbol type, List<(ITypeParameterSymbol Parameter, ITypeSymbol Argument)> bound)
    {
        RuntimeHelpers.EnsureSufficientExecutionStack();

        if (!MentionsTypeParameter(type))
            return type;

        switch (type)
        {
            case ITypeParameterSymbol typeParameter:
                foreach (var (parameter, argument) in bound)
                {
                    if (SymbolEqualityComparer.Default.Equals(parameter, typeParameter))
                        return argument;
                }

                return null;

            case INamedTypeSymbol { ContainingType: null or { IsGenericType: false } } named:
                var arguments = new ITypeSymbol[named.TypeArguments.Length];

                for (int i = 0; i < arguments.Length; i++)
                {
                    if (Substitute(named.TypeArguments[i], bound) is not { } argument)
                        return null;

                    arguments[i] = argument;
                }

                return named.OriginalDefinition.Construct(arguments);

            default:
                return null;
        }
    }

    /// <summary>
    /// Gets a member of an interface that the type parameter is constrained to, or of an interface that such an interface inherits, which a
    /// struct that is bound to the type parameter leaves to the interface.
    /// </summary>
    private static (ITypeSymbol Constraint, ISymbol Member)? FindDefaultMemberOfConstraint(ITypeParameterSymbol typeParameter, INamedTypeSymbol boundStruct)
    {
        foreach (var constraint in typeParameter.ConstraintTypes)
        {
            if (constraint is not INamedTypeSymbol { TypeKind: TypeKind.Interface } constraintInterface)
                continue;

            // The interfaces as the struct implements them, with its own type arguments.
            var implemented = boundStruct.AllInterfaces.Where(i =>
                SymbolEqualityComparer.Default.Equals(i.OriginalDefinition, constraintInterface.OriginalDefinition) ||
                constraintInterface.OriginalDefinition.AllInterfaces.Any(b => SymbolEqualityComparer.Default.Equals(b.OriginalDefinition, i.OriginalDefinition)));

            if (GetMembersLeftToInterfaces(boundStruct, implemented).FirstOrDefault() is { } member)
                return (constraint, member);
        }

        return null;
    }

    private void CollectNonCopyableTypes(ITypeSymbol? type, HashSet<ITypeSymbol> result, HashSet<ITypeSymbol> visited)
    {
        RuntimeHelpers.EnsureSufficientExecutionStack();

        switch (type)
        {
            case ITypeParameterSymbol when IsNonCopyableType(type):
                result.Add(type);
                break;

            case IArrayTypeSymbol array:
                CollectNonCopyableTypes(array.ElementType, result, visited);
                break;

            case INamedTypeSymbol named when Carries(named) && visited.Add(named):
                if (named.TypeKind is TypeKind.Struct && IsNonCopyableByItself(named))
                    result.Add(named);

                foreach (var argument in GetAllTypeArguments(named))
                    CollectNonCopyableTypes(argument, result, visited);

                // A type that derives from a container is a container of the same non-copyable types.
                foreach (var inherited in GetBaseTypeAndInterfaces(named))
                    CollectNonCopyableTypes(inherited, result, visited);

                break;
        }
    }

    /// <summary>
    /// The state of one classification of a type and of everything that it leads to.
    /// </summary>
    private sealed class KindComputation
    {
        public HashSet<INamedTypeSymbol> InProgress { get; } = new(SymbolEqualityComparer.Default);

        /// <summary>
        /// Gets the types in progress that were met again, and so were taken for copyable by something that has been classified since.
        /// </summary>
        public HashSet<INamedTypeSymbol> MetAgain { get; } = new(SymbolEqualityComparer.Default);
    }
}

/// <summary>
/// How a type relates to non-copyable values.
/// </summary>
internal enum CopyKind
{
    /// <summary>
    /// The type neither is non-copyable nor mentions a non-copyable type.
    /// </summary>
    Copyable,

    /// <summary>
    /// A value of the type must not be copied.
    /// </summary>
    NonCopyable,

    /// <summary>
    /// The type mentions a non-copyable type as a type argument or an array element type, and copying a value of it copies no element: a reference
    /// type or a view.
    /// </summary>
    Container,
}

/// <summary>
/// What the use of a member means for the non-copyable types that are bound to its type parameters and to those of the types that contain it.
/// </summary>
internal enum MemberUseKind
{
    /// <summary>
    /// Nothing that carries a non-copyable type is bound to a type parameter that is not marked: the member is checked code, and what it returns by
    /// value is a new value.
    /// </summary>
    Checked,

    /// <summary>
    /// The member is on the safe list. What it hands out is as new as what it was called on and with.
    /// </summary>
    Listed,

    /// <summary>
    /// The member is on the safe list with the marker <c>new</c>. What it hands out is a new value.
    /// </summary>
    ListedNew,

    /// <summary>
    /// A non-copyable type is bound to a type parameter that is not marked, and the member is not on the safe list.
    /// </summary>
    UnlistedNonCopyable,

    /// <summary>
    /// A container is bound to a type parameter that is not marked, and the member is not on the safe list.
    /// </summary>
    UnlistedContainer,

    /// <summary>
    /// A container is bound to a marked type parameter that is constrained to a type which hides the non-copyable type of the container.
    /// </summary>
    ContainerHiddenByConstraint,

    /// <summary>
    /// A generic struct of a non-copyable type is bound to a type parameter that is constrained to an interface of which the struct leaves a
    /// member to the default implementation.
    /// </summary>
    DefaultMemberOfConstraint,

    /// <summary>
    /// The member is marked <c>[RequiresCopyable]</c> and a non-copyable type is bound to a marked type parameter.
    /// </summary>
    RequiresCopyable,

    /// <summary>
    /// The member is checked code and has a container bound to a marked type parameter. Nothing is reported for the use, and a container
    /// that the member hands out through a type which mentions that type parameter is stored.
    /// </summary>
    MarkedContainer,
}

/// <summary>
/// What stands in the way of a type being erasable.
/// </summary>
internal enum ErasureObstacleKind
{
    /// <summary>
    /// A base class or an interface with bodies receives a non-copyable type through a type parameter that is not marked, so its bodies are not
    /// checked for that type.
    /// </summary>
    UncheckedBody,

    /// <summary>
    /// An interface has a reference type that mentions a non-copyable type in a position that is covariant in effect: variance converts
    /// the interface to one over object, or to one that takes a delegate over object.
    /// </summary>
    Variance,
}

/// <summary>
/// What stands in the way of a type being erasable: the base type or interface, its type parameter and what is bound to that. For variance
/// the type is the one with the variant type parameter, and the interface is the one that has it among its type arguments, or is it.
/// </summary>
/// <summary>
/// A binding of a non-copyable type to a type parameter of a class whose objects a class that the symbol list marks 'clones' clones: the
/// constructed class that binds it, the non-copyable type, and the cloning class.
/// </summary>
internal sealed record CloningBinding(INamedTypeSymbol Bound, ITypeSymbol Type, INamedTypeSymbol Cloning);

internal readonly record struct ErasureObstacle(
    ErasureObstacleKind Kind,
    INamedTypeSymbol Type,
    ITypeParameterSymbol TypeParameter,
    ITypeSymbol Argument,
    INamedTypeSymbol? Interface = null);

/// <summary>
/// The result of classifying the use of a member. For every kind but <see cref="MemberUseKind.Checked"/> it names a type parameter that makes the
/// use that kind, and the type bound to it.
/// </summary>
/// <param name="Kind">The kind of use.</param>
/// <param name="Definition">The original definition of the member, which for an accessor is its property or event. Null for
/// <see cref="MemberUseKind.Checked"/>.</param>
/// <param name="TypeParameter">The type parameter that makes the use that kind.</param>
/// <param name="TypeArgument">What is bound to the type parameter.</param>
/// <param name="Constraint">The constraint of the type parameter that hides the container, for
/// <see cref="MemberUseKind.ContainerHiddenByConstraint"/>.</param>
/// <param name="Member">The interface member that is left to its default implementation, for
/// <see cref="MemberUseKind.DefaultMemberOfConstraint"/>.</param>
/// <param name="Drains">Whether the member is on the safe list with the marker <c>drains</c>: for every rule but that of what its
/// sequence hands out it is a listed member like any other.</param>
/// <param name="ListedFor">For a member that is unlisted for its type parameter, the type parameters that the list names it for with
/// <c>for=</c>, where it names it at all: a line of a file then cannot name it for the type parameter. Null otherwise.</param>
internal readonly record struct MemberUse(
    MemberUseKind Kind,
    ISymbol? Definition = null,
    ITypeParameterSymbol? TypeParameter = null,
    ITypeSymbol? TypeArgument = null,
    ITypeSymbol? Constraint = null,
    ISymbol? Member = null,
    bool Drains = false,
    string? ListedFor = null);

/// <summary>
/// What keeps the mark [NotNullRef] on a parameter from counting.
/// </summary>
internal enum NotNullRefProblem
{
    /// <summary>
    /// The parameter is passed by value, where the mark says nothing.
    /// </summary>
    ByValue,

    /// <summary>
    /// The parameter is one of anything but a method that is not virtual or a local function.
    /// </summary>
    NotOnAMethodThatIsNotVirtual,

    /// <summary>
    /// The compiler can leave a call of the method out of a build.
    /// </summary>
    CanBeLeftOut,
}
