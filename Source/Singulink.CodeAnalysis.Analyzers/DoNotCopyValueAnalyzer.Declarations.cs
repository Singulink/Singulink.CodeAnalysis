using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Singulink.CodeAnalysis.Analyzers;

/// <content>
/// The checks of declarations: fields and auto-properties of a non-copyable type, the marks that derived types, overrides and implementations
/// must keep, where <c>[MovedFrom]</c> may be put, and the attribute classes themselves: one that is conditional, and a
/// <c>NonCopyableAttribute</c> in a namespace in which it is not recognized.
/// </content>
public sealed partial class DoNotCopyValueAnalyzer
{
    /// <summary>
    /// Checks a field of a non-copyable type: in a record, or in a struct that is not marked [NonCopyable] itself.
    /// </summary>
    /// <param name="context">The context of the field.</param>
    /// <param name="severities">The severities of the compilation.</param>
    /// <param name="types">The classifier of the compilation.</param>
    /// <param name="typesByThemselves">The classifier that counts every type parameter as copyable: a struct is non-copyable by
    /// itself where it is non-copyable there, and a field asks the struct to be marked where its type is.</param>
    private static void AnalyzeField(SymbolAnalysisContext context, Severities severities, NonCopyableTypes types, NonCopyableTypes typesByThemselves)
    {
        var field = (IFieldSymbol)context.Symbol;

        // A ref field stores a reference to the value and not the value, and no copy of a struct or a record copies a static field. The field
        // behind an auto-property is reported with the property.
        if (field.RefKind is not RefKind.None || field.IsStatic || field.IsImplicitlyDeclared || !types.IsNonCopyableType(field.Type))
            return;

        var containingType = field.ContainingType;

        if (containingType.IsRecord)
        {
            // A record copies its fields in 'with', in its copy constructor and in the members that the compiler writes for it, whether it is a
            // class or a struct and whether or not it is marked non-copyable.
            context.ReportDiagnostic(CreateDiagnostic(severities, field, Rules.FieldOfRecord, field.Type, field));
        }
        else if (containingType.IsValueType && IsNotMarkedFor(typesByThemselves, containingType, field.Type))
        {
            // Fields of copyable value types must be copyable. Copying a value type makes a shallow copy of the fields, which implicitly copies any
            // value type fields. Where the field is unsafe to copy, the message names the property that the mark needs, so one edit fixes both.
            if (types.IsUnsafeToCopyWithoutTypeParameters(field.Type))
                context.ReportDiagnostic(CreateDiagnostic(severities, field, Rules.FieldOfCopyableTypeUnsafeToCopy, field.Type, field, containingType));
            else
                context.ReportDiagnostic(CreateDiagnostic(severities, field, Rules.FieldOfCopyableType, field.Type, field));
        }
        else if (IsMissingUnsafeToCopy(types, containingType, field.Type))
        {
            context.ReportDiagnostic(CreateDiagnostic(severities, field, Rules.UnsafeToCopyField, field.Type, field, containingType));
        }
    }

    /// <summary>
    /// Gets a value indicating whether a struct holds a field of a type that is non-copyable by itself and is not marked [NonCopyable]
    /// itself: a copy of the struct copies the field. Both are judged with every type parameter counted as copyable. A struct that is
    /// non-copyable only through a marked type parameter of its own, or of a type that contains it, is copyable where a copyable type is
    /// bound to it, and copies the field there. A field whose type is non-copyable only through a type parameter asks nothing of the
    /// struct, since each constructed type of the struct is non-copyable through its type arguments as well.
    /// </summary>
    private static bool IsNotMarkedFor(NonCopyableTypes typesByThemselves, INamedTypeSymbol type, ITypeSymbol fieldType) =>
        typesByThemselves.IsNonCopyableType(fieldType) && !typesByThemselves.IsNonCopyableType(type);

    /// <summary>
    /// Gets a value indicating whether a struct holds a field of a type that is unsafe to copy and does not say itself that it is: a copy of
    /// the struct copies the field. A field whose type is unsafe to copy only through a type parameter asks nothing of the struct, since
    /// each constructed type of it is judged by its type arguments. Until the struct says so, it is what its attribute says.
    /// </summary>
    private static bool IsMissingUnsafeToCopy(NonCopyableTypes types, INamedTypeSymbol type, ITypeSymbol fieldType) =>
        type.TypeKind is TypeKind.Struct && types.IsUnsafeToCopyWithoutTypeParameters(fieldType) && !types.SaysItIsUnsafeToCopy(type);

    /// <summary>
    /// Makes every check of the declaration of a type.
    /// </summary>
    private static void AnalyzeNamedType(SymbolAnalysisContext context, Severities severities, NonCopyableTypes types, NonCopyableTypes typesInCopyingMember, MarkCache marks)
    {
        AnalyzeMarksOfType(context, severities, types, marks);
        AnalyzeStruct(context, severities, types, typesInCopyingMember);
        AnalyzeImplicitBaseConstructorCalls(context, severities, types, typesInCopyingMember);
        AnalyzeErasableBase(context, severities, types);
        AnalyzeAttributeClass(context, severities);
        AnalyzeForeignNonCopyableMark(context, severities);
        AnalyzeMovedFromTarget(context, severities);
    }

    private static void AnalyzeProperty(SymbolAnalysisContext context, Severities severities, NonCopyableTypes types, NonCopyableTypes typesInCopyingMember)
    {
        var property = (IPropertySymbol)context.Symbol;

        // In a member marked [RequiresCopyable] the marked type parameters count as copyable.
        if (NonCopyableTypes.IsMarkedRequiresCopyable(property))
            types = typesInCopyingMember;

        // Auto-properties cannot have non-copyable types. The getter always returns the backing field by value, which requires making a copy.
        // The type first: IsAutoProperty looks through every member of the containing type.
        if (types.IsNonCopyableType(property.Type) && IsAutoProperty(property))
            context.ReportDiagnostic(CreateDiagnostic(severities, property, Rules.AutoProperty, property.Type, property));

        // A positional parameter of a record is an auto-property, which is reported as that, unless the record declares the
        // member itself. A property that returns a reference passes every check of its own, but the Deconstruct method that the
        // compiler writes for the record reads it by value.
        if (property is { ReturnsByRef: true } or { ReturnsByRefReadonly: true } &&
            property is { IsStatic: false, IsImplicitlyDeclared: false, ContainingType: { IsRecord: true } record } &&
            types.IsNonCopyableType(property.Type) &&
            record.GetMembers(WellKnownMemberNames.DeconstructMethodName).Any(static member => member.IsImplicitlyDeclared) &&
            GetPositionalParameter(record, property) is { } positional)
        {
            context.ReportDiagnostic(CreateDiagnostic(severities, positional, Rules.PositionalParameterReadByDeconstruct, property.Type, positional.Name, record));
        }

        static bool IsAutoProperty(IPropertySymbol property) => property.ContainingType.GetMembers()
            .OfType<IFieldSymbol>()
            .Any(f => f.IsImplicitlyDeclared && SymbolEqualityComparer.Default.Equals(property, f.AssociatedSymbol));

        // The parameter of the primary constructor of a record that a member of the record stands for: the one of its name and type.
        static IParameterSymbol? GetPositionalParameter(INamedTypeSymbol record, IPropertySymbol property)
        {
            foreach (var constructor in record.InstanceConstructors)
            {
                if (!constructor.DeclaringSyntaxReferences.Any(static declared => declared.GetSyntax() is TypeDeclarationSyntax))
                    continue;

                foreach (var parameter in constructor.Parameters)
                {
                    if (parameter.Name == property.Name && SymbolEqualityComparer.Default.Equals(parameter.Type, property.Type))
                        return parameter;
                }
            }

            return null;
        }
    }

    /// <summary>
    /// Reports a constructor of a class that calls a [RequiresCopyable] constructor of its base class with nothing written: a class
    /// with no constructor, and a constructor with no initializer, call a constructor of the base class with no argument. A call that
    /// is written is an operation, and is reported where it is visited. So is the call of a constructor that has a body, where the
    /// compiler's choice is taken: see the walker. Here the call is worked out, for the constructor that the compiler declares and
    /// for a primary constructor, and every constructor that could be called counts.
    /// </summary>
    private static void AnalyzeImplicitBaseConstructorCalls(SymbolAnalysisContext context, Severities severities, NonCopyableTypes types, NonCopyableTypes typesInCopyingMember)
    {
        var type = (INamedTypeSymbol)context.Symbol;

        if (type.TypeKind is not TypeKind.Class || type.BaseType is null or { SpecialType: SpecialType.System_Object })
            return;

        foreach (var constructor in type.InstanceConstructors)
        {
            if (NonCopyableTypes.IsWrittenWithBody(constructor))
                continue;

            foreach (var called in NonCopyableTypes.GetBaseConstructorsCalledWithNothingWritten(constructor))
            {
                // In a constructor that is marked itself the marked type parameters count as copyable, as in its body.
                var use = (NonCopyableTypes.IsMarkedRequiresCopyable(constructor) ? typesInCopyingMember : types).GetUse(called);

                if (use.Kind is MemberUseKind.RequiresCopyable)
                {
                    context.ReportDiagnostic(CreateDiagnostic(
                        severities, constructor.IsImplicitlyDeclared ? type : constructor,
                        Rules.RequiresCopyableMember,
                        Display(use.Definition!),
                        use.TypeArgument,
                        use.TypeParameter!.Name));

                    break;
                }
            }
        }
    }

    /// <summary>
    /// Checks what only a struct can get wrong: a parameter of its primary constructor that its members use, which is a field that no
    /// declaration shows, and a struct of a non-copyable type that has an async or iterator instance member, which runs on a copy of the
    /// struct, or that leaves an interface member to the default implementation of the interface, which runs on a boxed copy.
    /// </summary>
    private static void AnalyzeStruct(SymbolAnalysisContext context, Severities severities, NonCopyableTypes types, NonCopyableTypes typesByThemselves)
    {
        var type = (INamedTypeSymbol)context.Symbol;

        if (type.TypeKind is not TypeKind.Struct)
            return;

        // A parameter of the primary constructor that the struct keeps in a field is copied with the struct as a declared field is, and
        // is judged as one (see AnalyzeField). The compiler keeps a parameter that a member uses in a field that it names after the
        // parameter. A record turns the parameters of its primary constructor into properties, which are reported as such.
        foreach (var member in type.GetMembers())
        {
            if (member is not IFieldSymbol { IsImplicitlyDeclared: true, IsStatic: false, AssociatedSymbol: not (IPropertySymbol or IEventSymbol) } field ||
                GetPrimaryConstructorParameter(type, field) is not { } parameter)
            {
                continue;
            }

            if (IsNotMarkedFor(typesByThemselves, type, field.Type))
            {
                if (!type.IsRecord)
                {
                    var rule = types.IsUnsafeToCopyWithoutTypeParameters(field.Type)
                        ? Rules.FieldByPrimaryConstructorUnsafeToCopy
                        : Rules.FieldByPrimaryConstructor;
                    context.ReportDiagnostic(CreateDiagnostic(severities, parameter, rule, parameter.Type, parameter.Name, type));
                }
            }
            else if (IsMissingUnsafeToCopy(types, type, field.Type))
            {
                context.ReportDiagnostic(CreateDiagnostic(severities, parameter, Rules.UnsafeToCopyFieldByPrimaryConstructor, parameter.Type, parameter.Name, type));
            }
        }

        AnalyzeFieldBindingsNotTaken(context, severities, types, type);

        if (!types.IsNonCopyableType(type))
            return;

        foreach (var member in type.GetMembers())
        {
            // The compiler copies 'this' into the state machine of an async member or an iterator. An accessor can be one as well. No copy
            // of a readonly struct can ever differ from the original, so there it does no harm.
            if (!type.IsReadOnly &&
                member is IMethodSymbol { IsStatic: false, IsImplicitlyDeclared: false } method &&
                (method.IsAsync || method.IsIterator))
            {
                context.ReportDiagnostic(CreateDiagnostic(
                    severities, method, Rules.AsyncOrIteratorMember, Display(method), type, method.IsAsync ? "async" : "iterator"));
            }
        }

        foreach (var member in NonCopyableTypes.GetMembersLeftToInterfaces(type, type.AllInterfaces))
            context.ReportDiagnostic(CreateDiagnostic(severities, type, Rules.DefaultInterfaceMember, type, Display(member)));
    }

    /// <summary>
    /// Reports an instance field of a struct that holds, inline, a struct with a binding of a type that is unsafe to copy to a type
    /// parameter marked <c>[NonCopyable(UnsafeToCopy = false)]</c>: the Equals and GetHashCode that the struct inherits from ValueType
    /// run the members of its fields, with no use that shows the binding. A struct that overrides both runs only what its own code calls,
    /// which is a use. A record is left out: its fields of a non-copyable type are reported as such.
    /// </summary>
    private static void AnalyzeFieldBindingsNotTaken(SymbolAnalysisContext context, Severities severities, NonCopyableTypes types, INamedTypeSymbol type)
    {
        if (type.IsRecord || (Overrides(nameof(Equals), parameterCount: 1) && Overrides(nameof(GetHashCode), parameterCount: 0)))
            return;

        foreach (var member in type.GetMembers())
        {
            if (member is not IFieldSymbol { IsStatic: false, IsConst: false } field || types.FindStructBindingNotTaken(field.Type) is not { } notTaken)
                continue;

            object?[] arguments = [DisplayField(field), type, field.Type, Display(notTaken.TypeParameter.ContainingSymbol), notTaken.Argument, notTaken.TypeParameter.Name];
            WithoutNullableAnnotations(arguments);
            context.ReportDiagnostic(severities.Create(Rules.UnsafeToCopyNotTakenInField, GetFieldLocation(field), arguments));
        }

        bool Overrides(string name, int parameterCount) => type
            .GetMembers(name)
            .Any(member => member is IMethodSymbol { IsOverride: true, IsImplicitlyDeclared: false } method && method.Parameters.Length == parameterCount);
    }

    /// <summary>
    /// Reports code of a non-copyable mark of the project, the class that the analyzer recognizes as the attribute or one that derives
    /// from it, that sets <c>UnsafeToCopy</c> (SCA1049): an assignment of the property, its initializer, and an argument for a
    /// constructor parameter named 'unsafeToCopy' in a call of another constructor. The analyzer reads the value only where the attribute
    /// is applied, so such code makes a struct look as if it said nothing. Setting the property from the constructor's own 'bool'
    /// parameter named 'unsafeToCopy', or passing that parameter on, is what the analyzer reads, and is no mistake.
    /// </summary>
    private static void AnalyzeUnsafeToCopySetInOwnCode(OperationBlockStartAnalysisContext context, Severities severities)
    {
        if (context.OwningSymbol.ContainingType is not { TypeKind: TypeKind.Class } type || !NonCopyableTypes.IsNonCopyableMark(type))
            return;

        context.RegisterOperationAction(
            operationContext => {
                switch (operationContext.Operation)
                {
                    case IPropertyReferenceOperation reference when IsUnsafeToCopy(reference.Property) && FindWrite(reference) is { } write && !IsOwnParameter(write):
                        Report(operationContext, write.Syntax);
                        break;

                    case IPropertyInitializerOperation initializer when initializer.InitializedProperties.Any(IsUnsafeToCopy):
                        Report(operationContext, initializer.Syntax);
                        break;

                    case IInvocationOperation { TargetMethod.MethodKind: MethodKind.Constructor } call:
                        foreach (var argument in call.Arguments)
                        {
                            if (argument.Parameter is { } parameter && NonCopyableTypes.IsUnsafeToCopyParameter(parameter) && !IsOwnParameter(argument.Value))
                                Report(operationContext, argument.Syntax);
                        }

                        break;
                }
            },
            OperationKind.PropertyReference,
            OperationKind.PropertyInitializer,
            OperationKind.Invocation);

        void Report(OperationAnalysisContext operationContext, SyntaxNode syntax) =>
            operationContext.ReportDiagnostic(severities.Create(Rules.UnsafeToCopySetInOwnCode, syntax.GetLocation(), Display(type)));

        static bool IsUnsafeToCopy(IPropertySymbol property) =>
            property is { Name: NonCopyableTypes.UnsafeToCopyPropertyName, IsStatic: false } && NonCopyableTypes.IsNonCopyableMark(property.ContainingType);

        // The assignment that writes the property, or the property itself where a deconstruction writes it. Null where it is read.
        static IOperation? FindWrite(IPropertyReferenceOperation reference)
        {
            IOperation current = reference;

            while (current.Parent is ITupleOperation tuple)
                current = tuple;

            return current.Parent switch {
                IAssignmentOperation assignment when ReferenceEquals(assignment.Target, current) =>
                    current is ITupleOperation ? reference : assignment,
                _ => null,
            };
        }

        // A value that is the constructor's own 'bool' parameter named 'unsafeToCopy', which the analyzer reads where the attribute is
        // applied, as an assignment of it, or as the value itself.
        static bool IsOwnParameter(IOperation operation)
        {
            var value = operation is ISimpleAssignmentOperation assignment ? assignment.Value : operation;

            while (value is IConversionOperation { IsImplicit: true } conversion)
                value = conversion.Operand;

            return value is IParameterReferenceOperation { Parameter: { ContainingSymbol: IMethodSymbol { MethodKind: MethodKind.Constructor } } parameter } &&
                NonCopyableTypes.IsUnsafeToCopyParameter(parameter);
        }
    }

    /// <summary>
    /// Reports two mistakes in the declaration of an attribute class. One of the analyzer's attributes that is declared
    /// <c>[Conditional]</c>: the analyzer sees a use of it in the source of its own project whether the symbol is defined or not, and
    /// the compiler leaves the use out of the assembly where it is not, so the marks are gone for every project that uses the assembly.
    /// And an attribute class named NonCopyableAttribute outside the namespace that the attributes are recognised in: a struct that
    /// is marked with it is copyable, in a build that looks clean. For the other four names an attribute that is not recognised only
    /// means more findings, and such a class is often the attribute of another tool. A class of the name that is no attribute marks
    /// nothing.
    /// </summary>
    private static void AnalyzeAttributeClass(SymbolAnalysisContext context, Severities severities)
    {
        var type = (INamedTypeSymbol)context.Symbol;

        if (type.TypeKind is not TypeKind.Class)
            return;

        if (!NonCopyableTypes.AttributeNames.Contains(type.Name))
        {
            // A class that derives from the recognised NonCopyableAttribute marks like it, and is lost like it if it is conditional.
            if (!NonCopyableTypes.IsNonCopyableMark(type.BaseType))
            {
                // '[NonCopyable]' finds an attribute class named NonCopyable, with no suffix, just the same, in whatever namespace
                // it is declared, and nothing recognises it.
                if (type.Name is NonCopyableTypes.NonCopyableNameWithNoSuffix && DerivesFromAttribute(type))
                    context.ReportDiagnostic(severities.Create(Rules.NonCopyableAttributeWithNoSuffix, type.Locations.FirstOrDefault(), type.ToDisplayString()));

                return;
            }
        }
        else if (!NonCopyableTypes.IsInAttributeNamespace(type))
        {
            if (type.Name is NonCopyableTypes.NonCopyableAttributeName && DerivesFromAttribute(type))
                context.ReportDiagnostic(severities.Create(Rules.ForeignNonCopyableAttribute, type.Locations.FirstOrDefault(), type.ToDisplayString()));

            return;
        }

        // ConditionalAttribute is inherited: an attribute that derives from a conditional class is dropped from the assembly just the same.
        for (var current = type; current is not null; current = current.BaseType)
        {
            foreach (var attribute in current.GetAttributes())
            {
                if (!IsConditionalAttribute(attribute.AttributeClass))
                    continue;

                if (SymbolEqualityComparer.Default.Equals(current, type))
                {
                    var location = attribute.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken).GetLocation() ?? type.Locations.FirstOrDefault();
                    context.ReportDiagnostic(severities.Create(Rules.ConditionalMarker, location, type.Name));
                }
                else
                {
                    context.ReportDiagnostic(severities.Create(Rules.ConditionalMarkerBase, type.Locations.FirstOrDefault(), type.Name, current.ToDisplayString()));
                }

                return;
            }
        }
    }

    private static bool DerivesFromAttribute(INamedTypeSymbol type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current is { Name: "Attribute", ContainingType: null } && NonCopyableTypes.IsInNamespace(current, "System"))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Reports a struct or a type parameter of the compilation that is marked with a class named NonCopyableAttribute which a
    /// referenced assembly declares outside the namespace that the attributes are recognised in. The mark has no effect, and the
    /// project has no declaration of the attribute that could be reported, so the use is the only place.
    /// </summary>
    private static void AnalyzeForeignNonCopyableMark(SymbolAnalysisContext context, Severities severities)
    {
        switch (context.Symbol)
        {
            case INamedTypeSymbol type:
                if (type.TypeKind is TypeKind.Struct)
                    ReportForeignNonCopyableMarks(severities, type, context.Compilation.Assembly, context.ReportDiagnostic, context.CancellationToken);

                foreach (var typeParameter in type.TypeParameters)
                    ReportForeignNonCopyableMarks(severities, typeParameter, context.Compilation.Assembly, context.ReportDiagnostic, context.CancellationToken);

                break;

            case IMethodSymbol method:
                foreach (var typeParameter in method.TypeParameters)
                    ReportForeignNonCopyableMarks(severities, typeParameter, context.Compilation.Assembly, context.ReportDiagnostic, context.CancellationToken);

                break;
        }
    }

    private static void ReportForeignNonCopyableMarks(
        Severities severities, ISymbol marked, IAssemblySymbol compilationAssembly, Action<Diagnostic> report, CancellationToken cancellationToken)
    {
        foreach (var attribute in marked.GetAttributes())
        {
            // A class of a referenced assembly that derives from the attribute marks like it, and is lost like it if it is
            // conditional. It can have any name, so no list of the conditional attributes has it: the mark is looked at here.
            if (attribute.AttributeClass is { } derived &&
                !NonCopyableTypes.IsAttribute(derived, NonCopyableTypes.NonCopyableAttributeName) &&
                NonCopyableTypes.IsNonCopyableMark(derived) &&
                !SymbolEqualityComparer.Default.Equals(derived.ContainingAssembly, compilationAssembly) &&
                GetConditionalSymbols(derived) is { IsEmpty: false } symbols &&
                attribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken) is { } application &&
                !IsAnyDefinedAt(symbols, application, cancellationToken))
            {
                report(severities.Create(
                    Rules.ConditionalMarkerUse, application.GetLocation(), derived.Name, derived.ContainingAssembly?.Name, string.Join("' or '", symbols)));

                continue;
            }

            // An attribute that the compilation declares is reported where it is declared. A class that derives from such an
            // attribute is used like it.
            if (NonCopyableTypes.FindForeignNonCopyableAttribute(attribute.AttributeClass) is not { } foreign ||
                SymbolEqualityComparer.Default.Equals(foreign.ContainingAssembly, compilationAssembly))
            {
                continue;
            }

            string effect = marked is ITypeParameterSymbol
                ? $"type parameter '{marked.Name}' counts as not marked"
                : $"struct '{marked.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}' is copied with no warning";

            report(severities.Create(
                Rules.ForeignNonCopyableAttributeUse,
                attribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken).GetLocation() ?? marked.Locations.FirstOrDefault(),
                foreign.ToDisplayString(),
                foreign.ContainingAssembly?.Name,
                effect));
        }
    }

    /// <summary>
    /// Gets the symbols that an attribute class is conditional on: those of the class and of every class that it derives from. The
    /// compiler writes a use of the attribute if one of them is defined. Empty for a class that is not conditional.
    /// </summary>
    private static ImmutableArray<string> GetConditionalSymbols(INamedTypeSymbol attributeClass) => GetSelfAndBaseTypes(attributeClass)
        .SelectMany(static declaring => declaring.GetAttributes())
        .Where(static attribute => IsConditionalAttribute(attribute.AttributeClass))
        .Select(static attribute => attribute.ConstructorArguments is [{ Value: string symbol }] ? symbol : string.Empty)
        .ToImmutableArray();

    /// <summary>
    /// Gets a value indicating whether one of some symbols is defined where a node stands: by the project, or by a '#define' of the file.
    /// </summary>
    private static bool IsAnyDefinedAt(ImmutableArray<string> symbols, SyntaxNode node, CancellationToken cancellationToken)
    {
        var defined = new HashSet<string>(node.SyntaxTree.Options.PreprocessorSymbolNames);

        for (var directive = ((CSharpSyntaxNode)node.SyntaxTree.GetRoot(cancellationToken)).GetFirstDirective();
            directive is not null && directive.SpanStart < node.SpanStart;
            directive = directive.GetNextDirective())
        {
            if (directive is DefineDirectiveTriviaSyntax { IsActive: true } define)
                defined.Add(define.Name.ValueText);
            else if (directive is UndefDirectiveTriviaSyntax { IsActive: true } undefine)
                defined.Remove(undefine.Name.ValueText);
        }

        return symbols.Any(defined.Contains);
    }

    private static IEnumerable<INamedTypeSymbol> GetSelfAndBaseTypes(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.BaseType)
            yield return current;
    }

    private static bool IsConditionalAttribute(INamedTypeSymbol? attributeClass) =>
        attributeClass is { Name: "ConditionalAttribute" } && NonCopyableTypes.IsInNamespace(attributeClass, "System.Diagnostics");

    /// <summary>
    /// Finds the attributes of the analyzer that a referenced assembly declares <c>[Conditional]</c>, each with the symbols that it
    /// depends on. The project that declares such an attribute is told so where it is declared; a project that uses it is told at
    /// every use that the compiler drops.
    /// </summary>
    private static Dictionary<INamedTypeSymbol, ImmutableArray<string>>? FindConditionalMarkers(Compilation compilation)
    {
        Dictionary<INamedTypeSymbol, ImmutableArray<string>>? markers = null;

        foreach (string name in NonCopyableTypes.AttributeNames)
        {
            foreach (var type in compilation.GetTypesByMetadataName($"{NonCopyableTypes.AttributeNamespace}.{name}"))
            {
                // The attribute of this project is reported where it is declared.
                if (SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, compilation.Assembly))
                    continue;

                var symbols = GetConditionalSymbols(type);

                if (!symbols.IsEmpty)
                    (markers ??= new(SymbolEqualityComparer.Default))[type] = symbols;
            }
        }

        return markers;
    }

    /// <summary>
    /// Reports a use of an attribute of the analyzer whose class a referenced assembly declares <c>[Conditional]</c>, where none of
    /// its symbols is defined: the analyzer honours the mark in this project, and the compiler leaves it out of the assembly.
    /// </summary>
    private static void AnalyzeConditionalMarkerUse(SyntaxNodeAnalysisContext context, Severities severities, Dictionary<INamedTypeSymbol, ImmutableArray<string>> markers)
    {
        if (context.SemanticModel.GetSymbolInfo(context.Node, context.CancellationToken).Symbol is not IMethodSymbol { ContainingType: var attributeClass } ||
            !markers.TryGetValue(attributeClass, out var symbols))
        {
            return;
        }

        if (IsAnyDefinedAt(symbols, context.Node, context.CancellationToken))
            return;

        context.ReportDiagnostic(severities.Create(
            Rules.ConditionalMarkerUse,
            context.Node.GetLocation(),
            attributeClass.Name,
            attributeClass.ContainingAssembly?.Name,
            string.Join("' or '", symbols)));
    }

    /// <summary>
    /// Reports a type that derives from an erasable type, or implements one, and adds a way out that the erasable type does not have. A
    /// conversion sees only the type that it is written with, and the object behind it can be of this type.
    /// </summary>
    private static void AnalyzeErasableBase(SymbolAnalysisContext context, Severities severities, NonCopyableTypes types)
    {
        var type = (INamedTypeSymbol)context.Symbol;

        if (type.TypeKind is not (TypeKind.Class or TypeKind.Struct or TypeKind.Interface) || !types.Carries(type))
            return;

        INamedTypeSymbol? erasable = null;

        // The report names an erasable type that holds a value which is unsafe to copy where there is one, which makes it strict.
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (types.Carries(current) && types.IsErasable(current) && (erasable is null || types.IsLessUnsafe(erasable, current)))
                erasable = current;
        }

        foreach (var @interface in type.AllInterfaces)
        {
            if (types.Carries(@interface) && types.IsErasable(@interface) && (erasable is null || types.IsLessUnsafe(erasable, @interface)))
                erasable = @interface;
        }

        if (erasable is null || types.FindErasureObstacle(type) is not { } obstacle)
            return;

        string addedType = Display(obstacle.Type.OriginalDefinition);

        // A position inside a type argument of the interface counts when it is covariant in effect, as 'in' inside 'in' is.
        string variance = obstacle.Interface is { } outer && !SymbolEqualityComparer.Default.Equals(outer, obstacle.Type)
            ? $"'{Display(outer.OriginalDefinition)}' is given '{obstacle.Type}', whose type parameter '{obstacle.TypeParameter.Name}' is there in a " +
                "position that is covariant in effect and"
            : $"'{addedType}' is covariant in '{obstacle.TypeParameter.Name}', which";

        string reason = obstacle.Kind switch {
            ErasureObstacleKind.UncheckedBody =>
                $"'{addedType}' receives '{obstacle.Argument}' through type parameter '{obstacle.TypeParameter.Name}', which is not marked [NonCopyable]",
            _ when obstacle.Argument is ITypeParameterSymbol =>
                $"{variance} is bound to '{obstacle.Argument}', a type parameter that is not constrained to 'struct'",
            _ => $"{variance} is bound to '{obstacle.Argument}'",
        };

        context.ReportDiagnostic(CreateDiagnostic(severities, type, Rules.ErasableBaseNotKept, Display(type), erasable, reason));
    }

    private static IParameterSymbol? GetPrimaryConstructorParameter(INamedTypeSymbol type, IFieldSymbol field)
    {
        foreach (var constructor in type.InstanceConstructors)
        {
            foreach (var parameter in constructor.Parameters)
            {
                // The field is named after the parameter, in a way that no code can write.
                if (field.Name == $"<{parameter.Name}>P")
                    return parameter;
            }
        }

        return null;
    }

    /// <summary>
    /// Checks that a type keeps the marks that callers trust: a type parameter that it passes to a marked type parameter of a base type is marked,
    /// and its overrides and implementations have the marks of the members they override or implement. Otherwise a body that is not checked could
    /// sit behind a marked signature. An override whose return type shows a non-copyable type that the overridden member's hides is reported
    /// with them: it is a conversion that no body has.
    /// </summary>
    private static void AnalyzeMarksOfType(SymbolAnalysisContext context, Severities severities, NonCopyableTypes types, MarkCache marks)
    {
        var type = (INamedTypeSymbol)context.Symbol;

        if (type.BaseType is { } baseType)
            CheckTypeArgumentsOfBaseType(context, severities, type, baseType);

        foreach (var @interface in type.Interfaces)
            CheckTypeArgumentsOfBaseType(context, severities, type, @interface);

        foreach (var member in type.GetMembers())
        {
            // An accessor is checked with its property or event.
            var overridden = member switch {
                IMethodSymbol { AssociatedSymbol: not null } or { IsOverride: false } => null,
                IMethodSymbol method => method.OverriddenMethod,
                IPropertySymbol property => property.OverriddenProperty,
                IEventSymbol @event => (ISymbol?)@event.OverriddenEvent,
                _ => null,
            };

            if (overridden is not null)
                CheckMarksOfMember(context, severities, types, member, overridden, member);

            CheckCovariantReturn(context, severities, types, member);

            // An interface can implement a member of a base interface, which gives it a default implementation that replaces the one that
            // callers trust.
            if (type.TypeKind is TypeKind.Interface)
            {
                var implemented = member switch {
                    IMethodSymbol { AssociatedSymbol: null } method => method.ExplicitInterfaceImplementations.CastArray<ISymbol>(),
                    IPropertySymbol property => property.ExplicitInterfaceImplementations.CastArray<ISymbol>(),
                    IEventSymbol @event => @event.ExplicitInterfaceImplementations.CastArray<ISymbol>(),
                    _ => ImmutableArray<ISymbol>.Empty,
                };

                foreach (var interfaceMember in implemented)
                    CheckMarksOfMember(context, severities, types, member, interfaceMember, member);
            }
        }

        if (type.TypeKind is not TypeKind.Interface)
            CheckImplementations(context, severities, type, types, marks);
    }

    /// <summary>
    /// Reports an override whose return type mentions a non-copyable type that the return type of the overridden member does not. A caller of
    /// the base member gets the container as the type that the base member returns, which is the conversion that the override stands for.
    /// </summary>
    private static void CheckCovariantReturn(SymbolAnalysisContext context, Severities severities, NonCopyableTypes types, ISymbol member)
    {
        if (!NonCopyableTypes.TryGetOverriddenReturn(member, out var returnType, out var overriddenReturnType, out var overridden))
            return;

        // A type parameter that is constrained to a class can be returned as a base type as well, and can have a container bound to it.
        if (types.HidesNonCopyableType(returnType, overriddenReturnType) && types.FindCloningBindingWithin(returnType) is { } binding)
        {
            // The report stands for the warning of the hidden container as well, so it is about what the value holds besides the binding.
            context.ReportDiagnostic(CreateDiagnosticAbout(
                severities, member, Rules.CloningUseReturn, Subject.Of(binding.Type, returnType), returnType, overriddenReturnType, binding.Cloning, binding.Type, binding.Bound));
        }
        else if (types.HidesNonCopyableType(returnType, overriddenReturnType) || types.HidesTypeParameter(returnType, overriddenReturnType))
        {
            var rule = NonCopyableTypes.IsUntypedCollection(overriddenReturnType) ? Rules.UntypedCollectionReturn : Rules.OpaqueReturn;
            context.ReportDiagnostic(CreateDiagnostic(severities, member, rule, returnType, overriddenReturnType, Display(member), Display(overridden.OriginalDefinition)));
        }
        else if (types.LosesHolder(returnType, overriddenReturnType) && !NonCopyableTypes.HasBorrowedReturn(overridden))
        {
            // An object of a class that derives from a listed container, returned as that container: a caller of the base member takes
            // it for a new one. A written conversion makes the value stored where it is converted, and here nothing is written.
            context.ReportDiagnostic(CreateDiagnostic(
                severities, member, Rules.HolderReturnedByOverride, returnType, overriddenReturnType, Display(member), Display(overridden.OriginalDefinition)));
        }
    }

    /// <summary>
    /// Reports an entry of a base list that binds a type which is unsafe to copy to a type parameter marked
    /// <c>[NonCopyable(UnsafeToCopy = false)]</c>, on the entry: the code of the base class or of the interface runs with the type through
    /// every member that a type inherits or implements, an override that is called through a base class that is not generic included,
    /// with no use that shows the binding.
    /// </summary>
    private static void AnalyzeBaseListBindings(SyntaxNodeAnalysisContext context, Severities severities, NonCopyableTypes types)
    {
        foreach (var entry in ((BaseListSyntax)context.Node).Types)
        {
            if (context.SemanticModel.GetSymbolInfo(entry.Type, context.CancellationToken).Symbol is INamedTypeSymbol baseType &&
                types.FindBindingNotTaken(baseType) is { } binding)
            {
                ReportBindingNotTaken(severities, context.ReportDiagnostic, entry.Type.GetLocation(), binding.TypeParameter, binding.Argument);
            }
        }
    }

    /// <summary>
    /// Reports a type that is unsafe to copy, or a type parameter that can stand for one, bound to a type parameter that is marked
    /// <c>[NonCopyable(UnsafeToCopy = false)]</c>.
    /// </summary>
    private static void ReportBindingNotTaken(Severities severities, Action<Diagnostic> report, Location location, ITypeParameterSymbol typeParameter, ITypeSymbol argument)
    {
        var rule = argument is ITypeParameterSymbol ? Rules.UnsafeToCopyTypeParameterNotTaken : Rules.UnsafeToCopyNotTaken;
        object?[] arguments = [Display(typeParameter.ContainingSymbol), argument, typeParameter.Name];
        WithoutNullableAnnotations(arguments);
        report(severities.Create(rule, location, arguments));
    }

    private static void CheckTypeArgumentsOfBaseType(SymbolAnalysisContext context, Severities severities, INamedTypeSymbol type, INamedTypeSymbol baseType)
    {
        for (var current = baseType; current is not null; current = current.ContainingType)
        {
            var typeParameters = current.OriginalDefinition.TypeParameters;
            var typeArguments = current.TypeArguments;

            for (int i = 0; i < typeParameters.Length && i < typeArguments.Length; i++)
            {
                if (typeArguments[i] is not ITypeParameterSymbol argument ||
                    !NonCopyableTypes.IsMarkedNonCopyable(typeParameters[i]) ||
                    NonCopyableTypes.IsMarkedNonCopyable(argument))
                {
                    continue;
                }

                // A type parameter of a containing type has no location in this type, so the type itself is the place then.
                ISymbol reported = SymbolEqualityComparer.Default.Equals(argument.ContainingSymbol, type) ? argument : type;
                context.ReportDiagnostic(CreateDiagnostic(
                    severities, reported, Rules.TypeParameterMarkNotPassedOn, argument.Name, typeParameters[i].Name, Display(current.OriginalDefinition)));
            }
        }
    }

    /// <summary>
    /// Checks the members that implement the interface members that a type brings in: the ones of the interfaces that its base class does not
    /// implement already, and of those that the type names again, which implements them again. A member that the type inherits and uses for
    /// one of them is checked too, and reported on the type, since nothing at the member's declaration says that it implements anything.
    /// </summary>
    private static void CheckImplementations(SymbolAnalysisContext context, Severities severities, INamedTypeSymbol type, NonCopyableTypes types, MarkCache marks)
    {
        var interfaces = type.AllInterfaces;

        if (interfaces.IsEmpty)
            return;

        var baseInterfaces = type.BaseType?.AllInterfaces ?? ImmutableArray<INamedTypeSymbol>.Empty;
        bool? hasMarkedMember = null;

        foreach (var @interface in interfaces)
        {
            // An interface of the base class that the type names again is implemented again, with the members that the type declares for it
            // and with those that hide the ones of the base class. That goes for the interfaces that the named one inherits as well.
            bool isOfBaseClass = baseInterfaces.Contains(@interface, SymbolEqualityComparer.Default);

            if (isOfBaseClass && !IsNamedAgain(@interface))
                continue;

            // Looking up the implementation of every interface member of every type costs too much to do for nothing. A mark can only be lost
            // if the interface has one, or if the type or a base class has a member with a mark: [RequiresCopyable], [Borrowed], [Kept]
            // or [MovedFrom].
            if (!marks.HasMarks(@interface.OriginalDefinition) && !(hasMarkedMember ??= marks.HasMarkedMember(type)))
                continue;

            foreach (var interfaceMember in @interface.GetMembers())
            {
                if (interfaceMember is not (IMethodSymbol { MethodKind: MethodKind.Ordinary } or IPropertySymbol or IEventSymbol))
                    continue;

                if (type.FindImplementationForInterfaceMember(interfaceMember) is not { } implementation)
                    continue;

                if (SymbolEqualityComparer.Default.Equals(implementation.ContainingType, type))
                {
                    CheckMarksOfMember(context, severities, types, implementation, interfaceMember, implementation);
                }
                else if (isOfBaseClass && SymbolEqualityComparer.Default.Equals(type.BaseType?.FindImplementationForInterfaceMember(interfaceMember), implementation))
                {
                    // The base class answers for a member that implemented the interface member there already.
                }
                else if (implementation.ContainingType?.TypeKind is not TypeKind.Interface)
                {
                    CheckMarksOfMember(context, severities, types, implementation, interfaceMember, type);
                    CheckInheritedKeeping(context, severities, type, types, implementation, interfaceMember);
                    CheckInheritedImplementation(context, severities, type, types, implementation, interfaceMember);
                    CheckInheritedOwner(context, severities, type, types, implementation, interfaceMember);
                }
            }
        }

        bool IsNamedAgain(INamedTypeSymbol @interface)
        {
            foreach (var named in type.Interfaces)
            {
                if (SymbolEqualityComparer.Default.Equals(named, @interface) || named.AllInterfaces.Contains(@interface, SymbolEqualityComparer.Default))
                    return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Reports an inherited method that takes a [MovedFrom] parameter of a container type to be its own, and that a type uses to implement
    /// an interface member which gives that parameter the type of a type parameter: generic code passes it a slot of its own, and the
    /// body of the inherited method, which was checked without the interface in sight, may move the container on.
    /// </summary>
    private static void CheckInheritedOwner(SymbolAnalysisContext context, Severities severities, INamedTypeSymbol type, NonCopyableTypes types, ISymbol implementation, ISymbol interfaceMember)
    {
        if (implementation is not IMethodSymbol method || interfaceMember.OriginalDefinition is not IMethodSymbol definition)
            return;

        for (int i = 0; i < method.Parameters.Length && i < definition.Parameters.Length; i++)
        {
            var parameter = method.Parameters[i];

            if (NonCopyableTypes.IsMovedFrom(parameter) &&
                types.IsContainer(parameter.Type) &&
                !types.IsContainer(definition.Parameters[i].Type))
            {
                context.ReportDiagnostic(CreateDiagnostic(
                    severities, type, Rules.SlotGivenToOwner, Display(method.OriginalDefinition), parameter.Name, $"'{Display(definition)}', which '{Display(type)}' uses it to implement,"));
            }
        }
    }

    /// <summary>
    /// Reports an inherited member that implements an interface member whose parameter does not keep, where the parameter of the
    /// member is taken to keep what it is given by having a type parameter as its type, as 'Registry&lt;T&gt;.Add(T)' has for
    /// 'ISink.Add(List&lt;Guard&gt;)'. Only an inherited member can have this shape: one that the class writes names the container
    /// type itself, and its body is checked. A parameter that is marked [Kept] is reported as that with the other marks.
    /// </summary>
    private static void CheckInheritedKeeping(
        SymbolAnalysisContext context, Severities severities, INamedTypeSymbol type, NonCopyableTypes types, ISymbol implementation, ISymbol interfaceMember)
    {
        if (implementation is not IMethodSymbol method || interfaceMember is not IMethodSymbol interfaceMethod)
            return;

        for (int i = 0; i < method.Parameters.Length && i < interfaceMethod.Parameters.Length; i++)
        {
            var parameter = method.Parameters[i];

            if (!NonCopyableTypes.IsMarkedKept(parameter) && types.KeepsUnseenBehind(parameter, interfaceMethod.Parameters[i], out bool byUncheckedCode))
            {
                context.ReportDiagnostic(CreateDiagnosticAbout(
                    severities, type,
                    Rules.KeepingNotOnInterfaceMember,
                    Subject.OfDeclared(interfaceMethod.Parameters[i].Type),
                    parameter.Name,
                    Display(method.OriginalDefinition),
                    Display(interfaceMethod.OriginalDefinition),
                    Display(type),
                    Rules.DescribeKeeping(byUncheckedCode)));
            }
        }
    }

    /// <summary>
    /// Reports an inherited member that implements an interface member which callers trust for a non-copyable type, when the member itself is
    /// not checked code for that type: a member of a base class whose type parameter is not marked.
    /// </summary>
    private static void CheckInheritedImplementation(
        SymbolAnalysisContext context, Severities severities, INamedTypeSymbol type, NonCopyableTypes types, ISymbol implementation, ISymbol interfaceMember)
    {
        // Callers that go through the interface are warned already if the interface member is not checked code either.
        if (types.GetUse(interfaceMember).Kind is not (MemberUseKind.Checked or MemberUseKind.MarkedContainer))
            return;

        var use = types.GetUse(implementation);

        // A listed member is only a concern if it hands out a value, which is as new as what it is called on, while callers of the interface
        // member take the value for new.
        bool isUnchecked = use.Kind switch {
            MemberUseKind.UnlistedNonCopyable => true,
            MemberUseKind.Listed => implementation switch {
                IMethodSymbol method => ReturnsByValue(method) && types.IsNonCopyableType(method.ReturnType),
                IPropertySymbol property => ReturnsByValue(property) && types.IsNonCopyableType(property.Type),
                _ => false,
            },
            _ => false,
        };

        if (isUnchecked && use.TypeParameter is not null)
        {
            context.ReportDiagnostic(CreateDiagnostic(
                severities, type,
                Rules.ImplementationNotChecked,
                Display(implementation.OriginalDefinition),
                Display(interfaceMember.OriginalDefinition),
                Display(type),
                use.TypeArgument!,
                use.TypeParameter.Name));
        }
    }

    /// <summary>
    /// Checks that a member marks what the member that it overrides or implements marks. Marking more is allowed: it only makes the body
    /// stricter. A finding is reported on <paramref name="reported"/>: the member, or the type that inherits the member and uses it as an
    /// implementation.
    /// </summary>
    private static void CheckMarksOfMember(SymbolAnalysisContext context, Severities severities, NonCopyableTypes types, ISymbol member, ISymbol baseMember, ISymbol reported)
    {
        bool reportOnMember = SymbolEqualityComparer.Default.Equals(reported, member);
        member = member.OriginalDefinition;
        baseMember = baseMember.OriginalDefinition;

        if (NonCopyableTypes.IsMarkedRequiresCopyable(member) && !NonCopyableTypes.IsMarkedRequiresCopyable(baseMember))
            context.ReportDiagnostic(CreateDiagnostic(severities, reported, Rules.RequiresCopyableNotOnBaseMember, Display(member), Display(baseMember)));

        if (member is not IMethodSymbol method || baseMember is not IMethodSymbol baseMethod)
            return;

        var typeParameters = method.TypeParameters;
        var baseTypeParameters = baseMethod.TypeParameters;

        for (int i = 0; i < typeParameters.Length && i < baseTypeParameters.Length; i++)
        {
            if (NonCopyableTypes.IsMarkedNonCopyable(baseTypeParameters[i]) && !NonCopyableTypes.IsMarkedNonCopyable(typeParameters[i]))
            {
                context.ReportDiagnostic(CreateDiagnostic(
                    severities, reportOnMember ? typeParameters[i] : reported,
                    Rules.TypeParameterMarkNotKept,
                    typeParameters[i].Name,
                    Display(method),
                    baseTypeParameters[i].Name,
                    Display(baseMethod)));
            }

            // A caller through the base member can bind a type that is unsafe to copy where the base member's type parameter is marked and
            // takes one, and the body of this member is checked for copies of it with warnings only. Where the base member's type parameter
            // is not marked, a call through it with any non-copyable type is reported already, so this member may say false there, as it
            // may mark the type parameter at all.
            if (NonCopyableTypes.TakesNoUnsafeToCopy(typeParameters[i]) &&
                NonCopyableTypes.IsMarkedNonCopyable(baseTypeParameters[i]) &&
                !NonCopyableTypes.TakesNoUnsafeToCopy(baseTypeParameters[i]))
            {
                context.ReportDiagnostic(CreateDiagnostic(
                    severities, reportOnMember ? typeParameters[i] : reported,
                    Rules.TypeParameterTakesNoUnsafeToCopy,
                    typeParameters[i].Name,
                    Display(method),
                    baseTypeParameters[i].Name,
                    Display(baseMethod)));
            }
        }

        // A caller through the base member takes the result for a new value. The other way round is fine: the caller is only more careful.
        if (NonCopyableTypes.AddsBorrowed(method, baseMethod))
            context.ReportDiagnostic(CreateDiagnosticAbout(severities, reported, Rules.BorrowedNotOnBaseMember, Subject.OfDeclared(method.ReturnType), Display(member), Display(baseMember)));

        for (int i = 0; i < method.Parameters.Length && i < baseMethod.Parameters.Length; i++)
        {
            var parameter = method.Parameters[i];
            var baseParameter = baseMethod.Parameters[i];
            var reportedForParameter = reportOnMember ? parameter : reported;

            bool isMarked = NonCopyableTypes.IsMovedFrom(parameter);

            if (NonCopyableTypes.ChangesMovedFrom(parameter, baseParameter))
            {
                var rule = !isMarked
                    ? Rules.MovedFromDropped
                    : NonCopyableTypes.HasUncountedMovedFrom(baseParameter)
                        ? Rules.MovedFromAddedOverUncheckedMark
                        : Rules.MovedFromAdded;

                context.ReportDiagnostic(CreateDiagnosticAbout(
                    severities, reportedForParameter,
                    rule,
                    Subject.OfDeclared(parameter.Type),
                    parameter.Name,
                    Display(member),
                    $"'{Display(baseMember)}', which the member overrides or implements,"));
            }

            // And a caller through the base member takes a container that it passes to be still its own.
            if (types.AddsKept(parameter, baseParameter))
            {
                context.ReportDiagnostic(CreateDiagnosticAbout(
                    severities, reportedForParameter, Rules.KeptNotOnBaseMember, Subject.OfDeclared(parameter.Type), parameter.Name, Display(member), Display(baseMember)));
            }
        }
    }

    /// <summary>
    /// Reports <c>[RequiresCopyable]</c> on a static constructor or a finalizer. The runtime runs both, so there is no use at which the
    /// copy could be reported, and the body is checked as if the mark were not there.
    /// </summary>
    private static void AnalyzeRequiresCopyableTarget(SymbolAnalysisContext context, Severities severities)
    {
        var method = context.Symbol;

        if (NonCopyableTypes.IsNeverCalled(method) && NonCopyableTypes.HasAttribute(method, NonCopyableTypes.RequiresCopyableAttributeName))
            context.ReportDiagnostic(CreateDiagnostic(severities, method, Rules.RequiresCopyableOnMemberNeverCalled, Display(method)));

        // 'with' copies a record through a virtual method, so on a variable of the base record it runs the copy constructor of the
        // derived one. A derived record whose copy constructor is marked where that of its base is not adds the mark like an
        // override does: nothing is reported at such a 'with'.
        if (method is IMethodSymbol { MethodKind: MethodKind.Constructor, ContainingType: { IsRecord: true, TypeKind: TypeKind.Class, BaseType: { IsRecord: true } baseRecord } derived } copyConstructor &&
            NonCopyableTypes.IsCopyConstructor(copyConstructor) &&
            NonCopyableTypes.IsMarkedRequiresCopyable(copyConstructor) &&
            NonCopyableTypes.FindCopyConstructor(baseRecord) is var baseConstructor &&
            (baseConstructor is null || !NonCopyableTypes.IsMarkedRequiresCopyable(baseConstructor)))
        {
            context.ReportDiagnostic(CreateDiagnostic(severities, copyConstructor, Rules.RequiresCopyableNotOnBaseCopyConstructor, Display(copyConstructor), baseRecord, derived));
        }
    }

    /// <summary>
    /// Reports <c>[MovedFrom]</c> on anything that is not a <c>ref</c> parameter. The parameters of local functions and lambdas have no symbol
    /// action and are checked with the operations.
    /// </summary>
    private static void AnalyzeMovedFromTarget(SymbolAnalysisContext context, Severities severities, MoveTrust? moves = null)
    {
        var symbol = context.Symbol;
        Check(symbol, symbol);

        switch (symbol)
        {
            case INamedTypeSymbol type:
                CheckTypeParameters(type.TypeParameters);

                if (type.DelegateInvokeMethod is { } invoke)
                    CheckParameters(invoke.Parameters);

                // The receiver of an extension block is a parameter of the block, which is a type that the class contains.
                foreach (var nested in type.GetTypeMembers())
                {
                    if (nested is { IsExtension: true, ExtensionParameter: { RefKind: not RefKind.Ref } receiver })
                        Check(receiver, receiver);
                }

                break;

            case IMethodSymbol method:
                CheckTypeParameters(method.TypeParameters);

                // An accessor of an indexer has a parameter for each of the indexer's, with its attributes and its place, and the indexer
                // reports those. The value of a setter is the accessor's own.
                CheckParameters(method.AssociatedSymbol is IPropertySymbol { Parameters.Length: var count and > 0 } ? method.Parameters.Skip(count) : method.Parameters);

                // The receiver of an extension block is a parameter of the member too, and its paths are followed in every member.
                foreach (var parameter in NonCopyableTypes.GetParametersWithReceiver(method))
                {
                    if (parameter.RefKind is not RefKind.Ref || !NonCopyableTypes.HasAttribute(parameter, NonCopyableTypes.MovedFromAttributeName))
                        continue;

                    // The code of a primary constructor is the initializers of the type and the call of the base constructor.
                    // They have no order in which the parameter could be read and then assigned, and no path runs through them.
                    if (method.MethodKind is MethodKind.Constructor && method.DeclaringSyntaxReferences.Any(static declared => declared.GetSyntax() is TypeDeclarationSyntax))
                        context.ReportDiagnostic(CreateDiagnosticAbout(severities, parameter, Rules.MovedFromOnPrimaryConstructor, Subject.OfDeclared(parameter.Type), parameter.Name, method.ContainingType));
                    else if (method is { IsAbstract: false, IsExtern: false, IsImplicitlyDeclared: false } && !(method.IsPartialDefinition && method.PartialImplementationPart is null))
                        moves?.NoteDeclared(method, parameter);
                }

                if (NonCopyableTypes.HasReturnAttribute(method, NonCopyableTypes.MovedFromAttributeName))
                    context.ReportDiagnostic(CreateDiagnosticAbout(severities, method, Rules.MovedFromNotOnRefParameter, GetSubjectOfMark(method)));

                break;

            case IPropertySymbol property:
                CheckParameters(property.Parameters);
                break;
        }

        void CheckTypeParameters(ImmutableArray<ITypeParameterSymbol> typeParameters)
        {
            foreach (var typeParameter in typeParameters)
                Check(typeParameter, typeParameter);
        }

        void CheckParameters(IEnumerable<IParameterSymbol> parameters)
        {
            foreach (var parameter in parameters)
            {
                if (parameter.RefKind is not RefKind.Ref)
                    Check(parameter, parameter);
            }
        }

        void Check(ISymbol attributed, ISymbol reported)
        {
            if (NonCopyableTypes.HasAttribute(attributed, NonCopyableTypes.MovedFromAttributeName))
                context.ReportDiagnostic(CreateDiagnosticAbout(severities, reported, Rules.MovedFromNotOnRefParameter, GetSubjectOfMark(attributed)));
        }
    }

    /// <summary>
    /// Reports <c>[NotNullRef]</c> where it stands. Where the mark does not count it is reported as that. Where it counts, on a
    /// parameter that the project declares, it is reported as a claim that nothing checks, to be suppressed there once the method
    /// is verified: the analyzer does not look at the body, and what it says about a [MovedFrom] parameter and about a commit rests
    /// on the method doing what the mark says. The parameters of local functions and lambdas have no symbol action and are checked
    /// with the operations.
    /// </summary>
    private static void AnalyzeNotNullRefTarget(SymbolAnalysisContext context, Severities severities)
    {
        switch (context.Symbol)
        {
            // The parameters of an indexer are reported at the indexer, and not again at its accessors.
            case IMethodSymbol { AssociatedSymbol: not IPropertySymbol } method:
                foreach (var parameter in method.Parameters)
                    ReportNotNullRef(severities, parameter, method, context.ReportDiagnostic, context.CancellationToken);

                break;

            case IPropertySymbol property:
                foreach (var parameter in property.Parameters)
                    ReportNotNullRef(severities, parameter, property, context.ReportDiagnostic, context.CancellationToken);

                break;

            case INamedTypeSymbol type:
                if (type.DelegateInvokeMethod is { } invoke)
                {
                    foreach (var parameter in invoke.Parameters)
                        ReportNotNullRef(severities, parameter, type, context.ReportDiagnostic, context.CancellationToken);
                }

                // The receiver of an extension block is a parameter of the block, which is a type that the class contains.
                foreach (var nested in type.GetTypeMembers())
                {
                    if (nested is { IsExtension: true, ExtensionParameter: { } receiver })
                        ReportNotNullRef(severities, receiver, nested, context.ReportDiagnostic, context.CancellationToken);
                }

                break;
        }
    }

    /// <summary>
    /// Reports the mark [NotNullRef] on one parameter, if it has it: see <see cref="AnalyzeNotNullRefTarget"/>.
    /// </summary>
    private static void ReportNotNullRef(Severities severities, IParameterSymbol parameter, ISymbol declaring, Action<Diagnostic> report, CancellationToken cancellationToken)
    {
        AttributeData? mark = null;

        foreach (var attribute in parameter.GetAttributes())
        {
            if (NonCopyableTypes.IsAttribute(attribute.AttributeClass, NonCopyableTypes.NotNullRefAttributeName))
            {
                mark = attribute;
                break;
            }
        }

        if (mark is null)
            return;

        // The finding stands where the attribute stands, so that it can be suppressed there.
        var location = mark.ApplicationSyntaxReference?.GetSyntax(cancellationToken).GetLocation() ?? parameter.Locations.FirstOrDefault(static l => l.IsInSource);

        if (location is null)
            return;

        string? problem = NonCopyableTypes.GetNotNullRefProblem(parameter) switch {
            NotNullRefProblem.ByValue =>
                "the parameter is passed by value, and the mark is for a reference, which a 'ref', 'in', 'ref readonly' or 'out' parameter is",
            NotNullRefProblem.NotOnAMethodThatIsNotVirtual =>
                "the mark counts only on a method that is not virtual, or on a local function, where a call names the code that runs, and " +
                "not on a virtual or abstract member, an override, a member of an interface, a delegate, a lambda, an operator, a " +
                "constructor, an accessor or the receiver of an extension block",
            NotNullRefProblem.CanBeLeftOut =>
                "the compiler can leave a call of a method out of a build that is [Conditional] or a partial method with no body, and a " +
                "check that is there in one build and not in the next proves nothing",
            _ => null,
        };

        report(problem is null
            ? severities.Create(Rules.NotNullRefNotChecked, location, parameter.Name, Display(declaring))
            : severities.Create(Rules.NotNullRefMisplaced, location, parameter.Name, Display(declaring), problem));
    }

    // A parameter that keeps by its mark, or that is taken to keep by its type: a type parameter, or a type that mentions one
    // which is not marked.
    private static bool HasKeptParameter(IMethodSymbol method) => NonCopyableTypes.GetParametersWithReceiver(method).Any(
        p => NonCopyableTypes.HasAttribute(p, NonCopyableTypes.KeptAttributeName) || p.Type is ITypeParameterSymbol || NonCopyableTypes.MentionsUnmarkedTypeParameter(p.Type));

    private static Diagnostic CreateDiagnostic(Severities severities, ISymbol symbol, Rule rule, params object?[] arguments)
    {
        WithoutNullableAnnotations(arguments);
        var inSource = symbol.Locations.Where(l => l.IsInSource).ToList();

        if (inSource.Count is 0)
            return severities.Create(rule, null, arguments);

        return severities.Create(rule, inSource[0], inSource.Skip(1), arguments);
    }

    // Creates the diagnostic of a rule whose message does not name the types that the report is about, as CreateDiagnostic does: see
    // About.Given.
    private static Diagnostic CreateDiagnosticAbout(Severities severities, ISymbol symbol, Rule rule, Subject subject, params object?[] arguments)
    {
        WithoutNullableAnnotations(arguments);
        var inSource = symbol.Locations.Where(l => l.IsInSource).ToList();

        if (inSource.Count is 0)
            return severities.CreateAbout(rule, subject, null, arguments);

        return severities.CreateAbout(rule, subject, inSource[0], inSource.Skip(1), arguments);
    }

    // The subject of a report about a mark: the type of what carries it, where a caller binds what a type parameter in it stands for.
    private static Subject GetSubjectOfMark(ISymbol marked) => Subject.OfDeclared(marked switch {
        IParameterSymbol parameter => parameter.Type,
        ITypeParameterSymbol typeParameter => typeParameter,
        IFieldSymbol field => field.Type,
        IPropertySymbol property => property.Type,
        IEventSymbol @event => @event.Type,
        IMethodSymbol method => method.ReturnType,
        _ => null,
    });

    /// <summary>
    /// What a compilation's types have of marks, as far as the check of implementations has asked: it is what lets that check skip the types that
    /// have nothing to lose.
    /// </summary>
    private sealed class MarkCache
    {
        private readonly ConcurrentDictionary<INamedTypeSymbol, bool> _interfacesWithMarks = new(SymbolEqualityComparer.Default);
        private readonly ConcurrentDictionary<INamedTypeSymbol, bool> _typesWithMarkedMembers = new(SymbolEqualityComparer.Default);

        /// <summary>
        /// Gets a value indicating whether an interface has a marked type parameter, on itself, on a type that contains it or on one of its
        /// methods.
        /// </summary>
        public bool HasMarks(INamedTypeSymbol interfaceDefinition) => _interfacesWithMarks.GetOrAdd(interfaceDefinition, static definition => {
            for (var type = definition; type is not null; type = type.ContainingType)
            {
                if (type.TypeParameters.Any(NonCopyableTypes.IsMarkedNonCopyable))
                    return true;
            }

            return definition.GetMembers().Any(m =>
                m is IMethodSymbol method &&
                ((method.Arity > 0 && method.TypeParameters.Any(NonCopyableTypes.IsMarkedNonCopyable)) || NonCopyableTypes.HasMovedFromParameter(method)));
        });

        /// <summary>
        /// Gets a value indicating whether a type or one of its base classes declares a member marked <c>[RequiresCopyable]</c>, or a method
        /// whose return value is marked <c>[Borrowed]</c> or that has a <c>[Kept]</c> or a <c>[MovedFrom]</c> parameter, a parameter whose
        /// type is a type parameter, which is taken to keep what it is given, or a type parameter marked <c>UnsafeToCopy = false</c>.
        /// </summary>
        public bool HasMarkedMember(INamedTypeSymbol type)
        {
            for (var current = type; current is not null && current.SpecialType is not SpecialType.System_Object; current = current.BaseType)
            {
                bool hasMarkedMember = _typesWithMarkedMembers.GetOrAdd(current.OriginalDefinition, static definition => definition.GetMembers().Any(
                    m => (m is IMethodSymbol or IPropertySymbol or IEventSymbol && NonCopyableTypes.HasAttribute(m, NonCopyableTypes.RequiresCopyableAttributeName)) ||
                        (m is IMethodSymbol method && (NonCopyableTypes.HasBorrowedReturn(method) || HasKeptParameter(method) || NonCopyableTypes.HasMovedFromParameter(method) ||
                            method.TypeParameters.Any(NonCopyableTypes.TakesNoUnsafeToCopy)))));

                if (hasMarkedMember)
                    return true;
            }

            return false;
        }
    }
}
