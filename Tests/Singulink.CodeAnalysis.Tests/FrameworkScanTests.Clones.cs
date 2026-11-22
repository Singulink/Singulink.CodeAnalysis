using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace Singulink.CodeAnalysis.Tests;

/// <content>
/// Scans the IL of the shared frameworks that the tests run on for the classes whose own code clones an object with MemberwiseClone:
/// every such class that a class of another assembly can derive from must have a 'clones' line in the built-in list, of its own or of
/// a base class.
/// </content>
/// <remarks>
/// Docs: articles/rules/framework-symbol-list.md and NonCopyableSymbols.evidence.md, Classes That Clone Themselves.
/// </remarks>
public partial class FrameworkScanTests
{
    // The classes that clone and need no line, each with its reason.
    private static readonly Dictionary<string, string> CloningClassesWithNoLine = new(StringComparer.Ordinal) {
        // C# does not allow a class to derive from Delegate (CS0644), and every delegate type is sealed.
        ["System.Delegate"] = "no class of C# can derive from it",

        // The call is the body of MemberwiseClone(bool) itself, which clones only where a derived class calls it. That call gets SCA1005
        // where it is, and no class of the framework makes it.
        ["System.MarshalByRefObject"] = "it clones only where a derived class calls MemberwiseClone(bool), which gets SCA1005 there",
    };

    // Every call of MemberwiseClone on 'this' of a class, or on the argument of a static member of the class, as Calendar.ReadOnly and
    // CultureInfo.ReadOnly clone theirs, in a class that another assembly can derive from, is covered by a 'clones' line.
    [TestMethod]
    public void SharedFrameworks_EveryClassThatClonesItselfAndCanBeDerivedFromHasAClonesLine()
    {
        var frameworks = FindSharedFrameworks();
        var types = new List<ScannedType>();
        var cloning = new Dictionary<ScannedType, List<(string Method, bool IsVisible)>>();

        foreach (string path in frameworks.SelectMany(directory => Directory.GetFiles(directory, "*.dll")).Where(HasMetadata))
            ScanForClones(path, types, cloning);

        var byName = types.GroupBy(t => t.FullName, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        foreach (var type in types)
        {
            if (type.BaseName is { } baseName && byName.TryGetValue(baseName, out var bases))
            {
                foreach (var baseType in bases)
                    baseType.Derived.Add(type);
            }
        }

        var clonesLines = ReadEntries().Where(e => e.Value is "clones").Select(e => e.Key).ToList();
        var listed = clonesLines.Where(id => id.StartsWith("T:", StringComparison.Ordinal)).Select(id => id[2..]).ToHashSet(StringComparer.Ordinal);

        // A member line goes by its kind, the type and the name of the member: 'M:System.Globalization.CultureInfo.ReadOnly(...)', and
        // 'P:' or 'E:' for an accessor, whose uses the analyzer looks up under its property or event.
        var listedMembers = clonesLines
            .Where(id => id.StartsWith("M:", StringComparison.Ordinal) || id.StartsWith("P:", StringComparison.Ordinal) || id.StartsWith("E:", StringComparison.Ordinal))
            .Select(id => id.Split('(')[0])
            .ToHashSet(StringComparer.Ordinal);

        var missing = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var (type, methods) in cloning)
        {
            if (CloningClassesWithNoLine.ContainsKey(type.FullName) || !CanBeDerivedFromElsewhere(type, []))
                continue;

            if (!IsCovered(type))
                missing.Add($"T:{type.DocumentationName};clones   # {type.Assembly}: {methods[0].Method}");

            // A method that code can call, and that clones, has a line of its own, so that no warning advises vouching for it.
            foreach (var (method, isVisible) in methods)
            {
                var (kind, name) = MemberOfMethod(method);

                if (isVisible && !listedMembers.Contains($"{kind}:{type.DocumentationName}.{name}"))
                    missing.Add($"{kind}:{type.DocumentationName}.{name};clones   # {type.Assembly}: with its parameters, if it has any");
            }
        }

        Assert.IsGreaterThan(10000, types.Count, "The scan reads the types of the shared frameworks.");
        Assert.IsTrue(cloning.Keys.Any(t => t.FullName is "System.Text.Encoding"), "The scan finds the clone of Encoding.");
        Assert.IsEmpty(missing, "These classes clone themselves, can be derived from in another assembly, and have no 'clones' line of their own or of a base class. Add:\n" + string.Join("\n", missing));

        bool IsCovered(ScannedType type)
        {
            for (var current = type; current is not null; current = current.BaseName is { } b && byName.TryGetValue(b, out var bs) ? bs[0] : null)
            {
                if (listed.Contains(current.DocumentationName))
                    return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Gets the kind of line and the name of the member that a method of the framework is found under: 'P' and the property for the
    /// accessor of a property, 'E' and the event for one of an event, 'M' and the method otherwise. The scan does not read the special-name
    /// flag here, and no method of the framework that clones has a name of an accessor without being one.
    /// </summary>
    internal static (char Kind, string Name) MemberOfMethod(string method) => method switch {
        _ when method.StartsWith("get_", StringComparison.Ordinal) || method.StartsWith("set_", StringComparison.Ordinal) => ('P', method[4..]),
        _ when method.StartsWith("add_", StringComparison.Ordinal) => ('E', method[4..]),
        _ when method.StartsWith("remove_", StringComparison.Ordinal) => ('E', method[7..]),
        _ => ('M', method),
    };

    /// <summary>
    /// Gets a value indicating whether a class can be derived from in another assembly, by the definition of SCA1008 with no
    /// InternalsVisibleTo, since the grants of the framework never reach an application: it, or a class that derives from it, is not
    /// sealed, can be reached together with every class that contains it, and has a constructor that a derived class can call.
    /// </summary>
    private static bool CanBeDerivedFromElsewhere(ScannedType type, HashSet<ScannedType> judging)
    {
        if (!judging.Add(type))
            return false;

        try
        {
            return CanItselfBeDerivedFromElsewhere(type, judging) || type.Derived.Any(d => CanBeDerivedFromElsewhere(d, judging));
        }
        finally
        {
            judging.Remove(type);
        }
    }

    private static bool CanItselfBeDerivedFromElsewhere(ScannedType type, HashSet<ScannedType> judging)
    {
        if (!type.IsClass || type.IsSealed)
            return false;

        for (var current = type; current is not null; current = current.Containing)
        {
            bool reachable = current.Visibility switch {
                TypeAttributes.Public or TypeAttributes.NestedPublic => true,
                TypeAttributes.NestedFamily or TypeAttributes.NestedFamORAssem => current.Containing is { } outer && CanBeDerivedFromElsewhere(outer, judging),
                _ => false,
            };

            if (!reachable)
                return false;
        }

        return type.HasReachableConstructor;
    }

    private static void ScanForClones(string path, List<ScannedType> types, Dictionary<ScannedType, List<(string Method, bool IsVisible)>> cloning)
    {
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        var md = pe.GetMetadataReader();

        if (!md.IsAssembly)
            return;

        string assembly = md.GetString(md.GetAssemblyDefinition().Name);
        var scanned = new Dictionary<TypeDefinitionHandle, ScannedType>();

        ScannedType Get(TypeDefinitionHandle handle)
        {
            if (scanned.TryGetValue(handle, out var known))
                return known;

            var definition = md.GetTypeDefinition(handle);
            var declaring = definition.GetDeclaringType();
            var type = new ScannedType { Assembly = assembly };
            scanned[handle] = type;
            type.Containing = declaring.IsNil ? null : Get(declaring);
            type.FullName = GetName(md, handle)!;
            type.BaseName = GetName(md, definition.BaseType);
            type.IsSealed = (definition.Attributes & TypeAttributes.Sealed) != 0;
            type.IsClass = (definition.Attributes & TypeAttributes.Interface) == 0 && type.BaseName is not ("System.ValueType" or "System.Enum");
            type.Visibility = definition.Attributes & TypeAttributes.VisibilityMask;
            type.HasReachableConstructor = definition.GetMethods().Select(md.GetMethodDefinition).Any(m =>
                md.GetString(m.Name) is ".ctor" &&
                (m.Attributes & MethodAttributes.Static) == 0 &&
                (m.Attributes & MethodAttributes.MemberAccessMask) is MethodAttributes.Public or MethodAttributes.Family or MethodAttributes.FamORAssem);
            return type;
        }

        foreach (var handle in md.TypeDefinitions)
            types.Add(Get(handle));

        foreach (var handle in md.TypeDefinitions)
        {
            foreach (var methodHandle in md.GetTypeDefinition(handle).GetMethods())
            {
                var method = md.GetMethodDefinition(methodHandle);

                // A method of a mixed-mode assembly can have native code in place of IL.
                if (method.RelativeVirtualAddress is 0 ||
                    (method.ImplAttributes & MethodImplAttributes.CodeTypeMask) is not MethodImplAttributes.IL ||
                    !ClonesItsOwnClass(md, pe.GetMethodBody(method.RelativeVirtualAddress).GetILBytes()!, out bool throughCapturedThis))
                    continue;

                // A lambda, a local function or a state machine clones the 'this' that it captured, of the class that contains it, and
                // no code calls it by name.
                var owner = Get(handle);
                bool isVisible = (method.Attributes & MethodAttributes.MemberAccessMask) is MethodAttributes.Public or MethodAttributes.Family or MethodAttributes.FamORAssem;

                if (throughCapturedThis || owner.FullName.Contains('<', StringComparison.Ordinal))
                {
                    isVisible = false;

                    while (owner.Containing is not null && owner.FullName.Split('+')[^1].StartsWith('<'))
                        owner = owner.Containing;
                }

                if (!cloning.TryGetValue(owner, out var methods))
                    cloning[owner] = methods = [];

                methods.Add((md.GetString(method.Name), isVisible));
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether a method body calls Object.MemberwiseClone() or MarshalByRefObject.MemberwiseClone(bool), or makes a
    /// method group of one, on its first argument: 'this' of an instance method, the argument of a static one. 'ldarg.0; ldfld
    /// &lt;&gt;4__this' is the 'this' that a lambda, a local function or a state machine captured.
    /// </summary>
    private static bool ClonesItsOwnClass(MetadataReader md, byte[] il, out bool throughCapturedThis)
    {
        var instructions = new List<(ILOpCode Op, int Operand)>();

        for (int i = 0; i < il.Length;)
        {
            int code = il[i++];

            if (code is 0xFE)
                code = 0xFE00 | il[i++];

            var op = (ILOpCode)code;
            int size = GetOperandSize(op, il, i);
            instructions.Add((op, size is 4 ? BitConverter.ToInt32(il, i) : 0));
            i += size;
        }

        throughCapturedThis = false;

        for (int k = 0; k < instructions.Count; k++)
        {
            var (op, operand) = instructions[k];

            if (op is not (ILOpCode.Call or ILOpCode.Callvirt or ILOpCode.Ldftn or ILOpCode.Ldvirtftn) || !IsMemberwiseClone(md, MetadataTokens.EntityHandle(operand), out bool withBool))
                continue;

            int r = k - 1;

            if (withBool && op is ILOpCode.Call or ILOpCode.Callvirt && r >= 0 && instructions[r].Op is >= ILOpCode.Ldc_i4_m1 and <= ILOpCode.Ldc_i4)
                r--;

            if (op is ILOpCode.Ldvirtftn && r >= 0 && instructions[r].Op is ILOpCode.Dup)
                r--;

            if (r < 0)
                continue;

            if (instructions[r].Op is ILOpCode.Ldarg_0)
                return true;

            if (instructions[r].Op is ILOpCode.Ldfld && r >= 1 && instructions[r - 1].Op is ILOpCode.Ldarg_0 &&
                GetFieldName(md, instructions[r].Operand)?.Contains("4__this", StringComparison.Ordinal) is true)
            {
                throughCapturedThis = true;
                return true;
            }
        }

        return false;
    }

    private static bool IsMemberwiseClone(MetadataReader md, EntityHandle handle, out bool withBool)
    {
        withBool = false;
        (string? name, string? owner, BlobHandle signature) = handle.Kind switch {
            HandleKind.MemberReference when md.GetMemberReference((MemberReferenceHandle)handle) is var r => (md.GetString(r.Name), GetName(md, r.Parent), r.Signature),
            HandleKind.MethodDefinition when md.GetMethodDefinition((MethodDefinitionHandle)handle) is var d => (md.GetString(d.Name), GetName(md, d.GetDeclaringType()), d.Signature),
            _ => (null, null, default),
        };

        if (name is not "MemberwiseClone" || owner is not ("System.Object" or "System.MarshalByRefObject"))
            return false;

        var blob = md.GetBlobReader(signature);
        blob.ReadSignatureHeader();
        withBool = blob.ReadCompressedInteger() is 1;
        return true;
    }

    private static string? GetFieldName(MetadataReader md, int token) => MetadataTokens.EntityHandle(token) switch {
        { Kind: HandleKind.FieldDefinition } h => md.GetString(md.GetFieldDefinition((FieldDefinitionHandle)h).Name),
        { Kind: HandleKind.MemberReference } h => md.GetString(md.GetMemberReference((MemberReferenceHandle)h).Name),
        _ => null,
    };

    // The full name of a type, with '+' before the name of a nested type, and of the generic type of an instance.
    private static string? GetName(MetadataReader md, EntityHandle handle)
    {
        if (handle.IsNil)
            return null;

        switch (handle.Kind)
        {
            case HandleKind.TypeDefinition:
            {
                var definition = md.GetTypeDefinition((TypeDefinitionHandle)handle);
                var declaring = definition.GetDeclaringType();
                string name = md.GetString(definition.Name);

                if (!declaring.IsNil)
                    return GetName(md, declaring) + "+" + name;

                return definition.Namespace.IsNil ? name : md.GetString(definition.Namespace) + "." + name;
            }

            case HandleKind.TypeReference:
            {
                var reference = md.GetTypeReference((TypeReferenceHandle)handle);
                string name = md.GetString(reference.Name);

                if (reference.ResolutionScope.Kind is HandleKind.TypeReference)
                    return GetName(md, reference.ResolutionScope) + "+" + name;

                return reference.Namespace.IsNil ? name : md.GetString(reference.Namespace) + "." + name;
            }

            case HandleKind.TypeSpecification:
            {
                var blob = md.GetBlobReader(md.GetTypeSpecification((TypeSpecificationHandle)handle).Signature);

                if (blob.ReadSignatureTypeCode() is not SignatureTypeCode.GenericTypeInstance)
                    return null;

                blob.ReadSignatureTypeCode();
                return GetName(md, blob.ReadTypeHandle());
            }

            default:
                return null;
        }
    }

    private static int GetOperandSize(ILOpCode op, byte[] il, int at) => op switch {
        ILOpCode.Switch => 4 + (4 * BitConverter.ToInt32(il, at)),
        ILOpCode.Ldc_i8 or ILOpCode.Ldc_r8 => 8,
        ILOpCode.Ldarg_s or ILOpCode.Ldarga_s or ILOpCode.Starg_s or ILOpCode.Ldloc_s or ILOpCode.Ldloca_s or ILOpCode.Stloc_s or ILOpCode.Ldc_i4_s or
            ILOpCode.Unaligned or ILOpCode.Br_s or ILOpCode.Brfalse_s or ILOpCode.Brtrue_s or ILOpCode.Beq_s or ILOpCode.Bge_s or ILOpCode.Bgt_s or
            ILOpCode.Ble_s or ILOpCode.Blt_s or ILOpCode.Bne_un_s or ILOpCode.Bge_un_s or ILOpCode.Bgt_un_s or ILOpCode.Ble_un_s or ILOpCode.Blt_un_s or
            ILOpCode.Leave_s => 1,
        ILOpCode.Ldarg or ILOpCode.Ldarga or ILOpCode.Starg or ILOpCode.Ldloc or ILOpCode.Ldloca or ILOpCode.Stloc => 2,
        ILOpCode.Ldc_i4 or ILOpCode.Ldc_r4 or ILOpCode.Br or ILOpCode.Brfalse or ILOpCode.Brtrue or ILOpCode.Beq or ILOpCode.Bge or ILOpCode.Bgt or
            ILOpCode.Ble or ILOpCode.Blt or ILOpCode.Bne_un or ILOpCode.Bge_un or ILOpCode.Bgt_un or ILOpCode.Ble_un or ILOpCode.Blt_un or ILOpCode.Leave or
            ILOpCode.Jmp or ILOpCode.Call or ILOpCode.Calli or ILOpCode.Callvirt or ILOpCode.Cpobj or ILOpCode.Ldobj or ILOpCode.Ldstr or ILOpCode.Newobj or
            ILOpCode.Castclass or ILOpCode.Isinst or ILOpCode.Unbox or ILOpCode.Ldfld or ILOpCode.Ldflda or ILOpCode.Stfld or ILOpCode.Ldsfld or
            ILOpCode.Ldsflda or ILOpCode.Stsfld or ILOpCode.Stobj or ILOpCode.Box or ILOpCode.Newarr or ILOpCode.Ldelema or ILOpCode.Ldelem or
            ILOpCode.Stelem or ILOpCode.Unbox_any or ILOpCode.Refanyval or ILOpCode.Mkrefany or ILOpCode.Ldtoken or ILOpCode.Ldftn or ILOpCode.Ldvirtftn or
            ILOpCode.Initobj or ILOpCode.Constrained or ILOpCode.Sizeof => 4,
        _ => 0,
    };

    private sealed class ScannedType
    {
        public string Assembly { get; set; } = string.Empty;

        public string FullName { get; set; } = string.Empty;

        // The name of a 'T:' line: a nested type follows the type that contains it after a dot.
        public string DocumentationName => FullName.Replace('+', '.');

        public string? BaseName { get; set; }

        public ScannedType? Containing { get; set; }

        public bool IsSealed { get; set; }

        public bool IsClass { get; set; }

        public bool HasReachableConstructor { get; set; }

        public TypeAttributes Visibility { get; set; }

        public List<ScannedType> Derived { get; } = [];
    }
}
