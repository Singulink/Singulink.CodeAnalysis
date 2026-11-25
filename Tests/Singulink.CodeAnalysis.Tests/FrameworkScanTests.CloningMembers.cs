using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace Singulink.CodeAnalysis.Tests;

/// <content>
/// Scans the IL of the shared frameworks, and of the .NET Framework 4.8.1 where it is installed, for the members that clone an object of a
/// class that clones itself, directly with MemberwiseClone or through other members, and for those whose own object reaches other code:
/// every such member that code of another assembly can call needs a 'clones' line of its own, or for the second kind an entry in the
/// evidence that says why it hands nothing on, since a member of such a class with no line is taken neither to clone the object that it
/// is called on nor to hand it on.
/// </content>
/// <remarks>
/// Docs: articles/rules/framework-symbol-list.md and NonCopyableSymbols.evidence.md, Classes That Clone Themselves and Members Whose Own
/// Object Reaches Other Code.
/// </remarks>
public partial class FrameworkScanTests
{
    // Every public or protected member of a class of the framework whose base chain reaches a class with a 'clones' line, which clones its
    // own object or an object that it is given, directly or through other members of the framework, has a 'clones' line of its own.
    [TestMethod]
    public void SharedFrameworks_EveryMemberThatClonesItsObjectHasAClonesLine() =>
        CheckMembersThatClone(GetSharedFrameworkAssemblies());

    // The same for the .NET Framework 4.8.1.
    [TestMethod]
    public async Task NetFramework_EveryMemberThatClonesItsObjectHasAClonesLine() =>
        CheckMembersThatClone(await GetNetFrameworkAssembliesAsync());

    private static void CheckMembersThatClone(List<string> assemblies)
    {
        var scan = new CloningMemberScan();

        foreach (string path in assemblies)
            scan.Read(path);

        var entries = ReadEntries();
        var cloningClasses = entries.Where(e => e.Value is "clones" && e.Key.StartsWith("T:", StringComparison.Ordinal)).Select(e => e.Key[2..]).ToHashSet(StringComparer.Ordinal);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var members = scan.FindCloningMembers(cloningClasses);
        Console.WriteLine($"Followed {scan.Followed} methods of {scan.Count} in {watch.Elapsed.TotalSeconds:F1} s; {members.Count} clone an argument.");

        // A member line goes by its kind, the type and the name of the member: 'M:System.Globalization.CultureInfo.ReadOnly(...)', and
        // 'P:...' or 'E:...' for an accessor, whose uses the analyzer looks up under its property or event.
        var lined = entries
            .Where(e => e.Value is "clones" && (e.Key.StartsWith("M:", StringComparison.Ordinal) || e.Key.StartsWith("P:", StringComparison.Ordinal) || e.Key.StartsWith("E:", StringComparison.Ordinal)))
            .Select(e => e.Key.Split('(')[0])
            .ToHashSet(StringComparer.Ordinal);

        var missing = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var member in members)
        {
            if (!member.IsVisible || !scan.ChainReaches(member.TypeName, cloningClasses))
                continue;

            if (!lined.Contains($"{member.Kind}:{member.TypeDocumentationName}.{member.Name}"))
                missing.Add($"{member.Kind}:{member.TypeDocumentationName}.{member.Name};clones   # {member.Assembly}: clones argument {member.Arguments}{(member.IsStatic ? string.Empty : " (0 is the object)")}");
        }

        foreach (var member in members.OrderBy(m => m.TypeName, StringComparer.Ordinal).ThenBy(m => m.Name, StringComparer.Ordinal))
            Console.WriteLine($"{(member.IsVisible ? "visible " : "internal")} {member.TypeName}.{member.Name} clones argument {member.Arguments}");

        // A member with a line and no marker, of a class with member lines, whose result the object converts to, says that it never hands
        // the object back: the scan checks that none of its paths returns it.
        foreach (string handsOther in HandsBackAnotherObject)
        {
            int dot = handsOther.LastIndexOf('.');
            Assert.IsFalse(scan.MayReturnItsObject(handsOther[..dot], handsOther[(dot + 1)..]), $"{handsOther} can return the object that it is called on.");
            Assert.IsTrue(entries.ContainsKey((handsOther.EndsWith(".get_Parent", StringComparison.Ordinal) ? "P:" + handsOther.Replace(".get_", ".", StringComparison.Ordinal) : "M:" + handsOther) + MemberSuffix(handsOther)), $"{handsOther} has no line.");
        }

        // A member whose own object may reach code outside the family of its class has a 'clones' line, which says that it hands the object
        // on, or is named in the evidence section 'Members Whose Own Object Reaches Other Code', which says why it does not. A member of a
        // class whose line does not say 'clones=complete' needs neither: every member of it that no line names counts as one that can
        // clone.
        var complete = ReadCompleteClasses();
        var roots = FindCompleteRoots(scan, complete, cloningClasses);
        var handing = scan.FindMembersThatHandOnTheirObject(roots);
        AssertCompleteIsCovered(scan, complete, roots);
        var judged = ReadHandOnEvidence();
        var unjudged = new SortedSet<string>(StringComparer.Ordinal);
        Console.WriteLine($"{handing.Count} members may hand on their object.");

        foreach (var member in handing.OrderBy(m => m.TypeName, StringComparer.Ordinal).ThenBy(m => m.Name, StringComparer.Ordinal))
        {
            string explained = scan.ExplainEscape(member.TypeName, member.Name);
            string shortName = member.TypeName[(member.TypeName.LastIndexOfAny(['.', '+']) + 1)..] + "." + member.Name;
            Console.WriteLine($"hands on {member.TypeName}.{member.Name}: {explained}");

            if (!lined.Contains($"{member.Kind}:{member.TypeDocumentationName}.{member.Name}") && !judged.Contains(shortName))
                unjudged.Add($"{member.TypeName}.{member.Name}: {explained}");
        }

        Assert.IsEmpty(unjudged, "The object of these members may reach code outside the family of their class, and they have neither a 'clones' line nor an entry in the section 'Members Whose Own Object Reaches Other Code' of NonCopyableSymbols.evidence.md:\n" + string.Join("\n", unjudged));

        Assert.IsTrue(members.Any(m => m is { TypeName: "System.Globalization.CultureInfo", Name: "Clone" }), "The scan finds the clone of CultureInfo.");
        Assert.IsTrue(members.Any(m => m is { TypeName: "System.Globalization.CultureInfo", Name: "ReadOnly" }), "The scan finds that CultureInfo.ReadOnly clones what it is given.");
        Assert.IsEmpty(missing, "These members clone their object, or an object that they are given, through other members, and have no 'clones' line. Add:\n" + string.Join("\n", missing));
    }

    // The classes whose line says 'clones=complete' and whose base chain reaches no other class with a 'clones' line: the classes that
    // clone themselves whose families the scan of where an object can go covers.
    private static HashSet<string> FindCompleteRoots(CloningMemberScan scan, HashSet<string> complete, HashSet<string> cloningClasses) =>
        complete.Where(c => !scan.BaseReaches(c, cloningClasses)).ToHashSet(StringComparer.Ordinal);

    // The analyzer takes the members of a class whose line says 'clones=complete' to be known. So each such class of the built-in list is
    // a class that clones itself whose family the scan covers, or a public class that derives from one, and every such public class has
    // the line. The Xml classes are left without it on purpose: their members were not analyzed, and every member of them counts as one
    // that can clone.
    private static void AssertCompleteIsCovered(CloningMemberScan scan, HashSet<string> complete, HashSet<string> roots)
    {
        Assert.IsNotEmpty(roots);

        foreach (string root in roots)
            Assert.IsTrue(scan.HasType(root), $"{root} says 'clones=complete', and the scan finds no such class in the framework.");

        var covered = scan.FindCoveredClasses(roots).Select(id => id[2..]).ToHashSet(StringComparer.Ordinal);
        var derived = complete.Except(roots).ToHashSet(StringComparer.Ordinal);

        Assert.IsTrue(
            covered.SetEquals(derived),
            "The lines that say 'clones=complete' for a class that derives from a class that clones itself differ from the classes that the " +
            $"scan covers. They are:\n{string.Join("\n", covered.Order(StringComparer.Ordinal).Select(c => $"T:{c};clones=complete"))}");

        foreach (string xml in new[] { "System.Xml.Schema.XmlSchemaObject", "System.Xml.XmlQualifiedName" })
            Assert.IsFalse(complete.Contains(xml), $"{xml} is left without 'clones=complete': the members of the Xml classes were not analyzed.");
    }

    // The members that the evidence section 'Members Whose Own Object Reaches Other Code' names, as `Stroke.OnInvalidated`, or
    // `new PrinterSettings()` for the constructors of a class: one name covers every overload.
    private static HashSet<string> ReadHandOnEvidence()
    {
        string? repository = null;

        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null && repository is null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Singulink.CodeAnalysis.slnx")))
                repository = directory.FullName;
        }

        Assert.IsNotNull(repository, "No repository above the test assembly.");
        string text = File.ReadAllText(Path.Combine(repository, "Source", "Singulink.CodeAnalysis.Analyzers", "NonCopyableSymbols.evidence.md"));
        int start = text.IndexOf("\n## Members Whose Own Object Reaches Other Code", StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, start, "The evidence has no section 'Members Whose Own Object Reaches Other Code'.");
        int end = text.IndexOf("\n## ", start + 1, StringComparison.Ordinal);
        string section = end < 0 ? text[start..] : text[start..end];

        return System.Text.RegularExpressions.Regex.Matches(section, @"`(new (?<ctor>\w+)\(\)|(?<member>\w+\.\w+))`")
            .Select(m => m.Groups["ctor"].Success ? m.Groups["ctor"].Value + "..ctor" : m.Groups["member"].Value)
            .ToHashSet(StringComparer.Ordinal);
    }

    // The members of the classes with member lines whose result, or 'out' value, the object converts to, and that have a line with no
    // marker: they never hand the object back, so a call of one on an object with a binding gets no warning (SCA1009).
    private static readonly string[] HandsBackAnotherObject = [
        "System.Globalization.CultureInfo.get_Parent",
        "System.Globalization.CultureInfo.GetFormat",
        "System.Globalization.CultureInfo.GetConsoleFallbackUICulture",
        "System.Windows.Ink.Stroke.GetPropertyData",
        "System.Windows.Ink.DrawingAttributes.GetPropertyData",
    ];

    private static string MemberSuffix(string member) => member switch {
        "System.Globalization.CultureInfo.GetFormat" => "(System.Type)",
        "System.Windows.Ink.Stroke.GetPropertyData" or "System.Windows.Ink.DrawingAttributes.GetPropertyData" => "(System.Guid)",
        _ => string.Empty,
    };

    /// <summary>
    /// A member that the scan finds to clone one of its arguments: 'this' as argument 0 of an instance member, or a parameter.
    /// </summary>
    private sealed record CloningMember(string Assembly, string TypeName, string Name, bool IsStatic, char Kind, bool IsVisible, string Arguments)
    {
        public string TypeDocumentationName => TypeName.Replace('+', '.');
    }

    /// <summary>
    /// Follows the calls of the framework to the members that clone an argument. A first pass reads every method for the methods that it
    /// calls and finds those from which a call of MemberwiseClone can be reached, a call of a virtual method reaching its overrides in the
    /// classes that derive from its class. A second pass follows, in those methods only, which argument each value on the stack may be,
    /// over every path of the method, and finds for each the arguments that it may clone and those that it may return, to a fixed point:
    /// a method clones an argument that it hands to an argument of a method that clones that one, or calls MemberwiseClone on.
    /// </summary>
    private sealed class CloningMemberScan
    {
        private static readonly Dictionary<ushort, OpCode> s_opCodes = typeof(OpCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(f => (OpCode)f.GetValue(null)!)
            .ToDictionary(o => (ushort)o.Value);

        private readonly List<string> _paths = [];
        private readonly List<ScannedMethod> _methods = [];
        private readonly Dictionary<string, int> _keys = new(StringComparer.Ordinal);
        private readonly Dictionary<int, List<int>> _methodsByKey = [];
        private readonly Dictionary<string, string?> _baseNames = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<string>> _derivedNames = new(StringComparer.Ordinal);
        private readonly List<(string Name, int Arity, string Type)> _keyParts = [];
        private readonly Dictionary<int, PEReader> _readers = [];

        private ulong[] _clones = [];
        private ulong[] _returns = [];
        private bool[] _returnsKnown = [];
        private bool[] _returnsInProgress = [];
        private bool[] _isCandidate = [];
        private bool[] _visited = [];
        private bool _changed;
        private int _clonesDepth;
        private readonly Dictionary<int, (List<Instruction> Instructions, Dictionary<int, int> IndexByOffset)> _decoded = [];
        private int _returnsDepth;
        private HashSet<string> _cloningClasses = [];

        public void Read(string path)
        {
            var stream = File.OpenRead(path);
            var pe = new PEReader(stream);
            var md = pe.GetMetadataReader();

            if (!md.IsAssembly)
            {
                pe.Dispose();
                return;
            }

            int pathIndex = _paths.Count;
            _paths.Add(path);
            _readers[pathIndex] = pe;
            string assembly = md.GetString(md.GetAssemblyDefinition().Name);

            foreach (var typeHandle in md.TypeDefinitions)
            {
                var type = md.GetTypeDefinition(typeHandle);
                string typeName = GetName(md, typeHandle)!;
                string? baseName = GetName(md, type.BaseType);
                _baseNames.TryAdd(typeName, baseName);

                if (baseName is not null)
                {
                    if (!_derivedNames.TryGetValue(baseName, out var derived))
                        _derivedNames[baseName] = derived = [];

                    derived.Add(typeName);
                }

                bool typeIsVisible = IsTypeVisible(md, typeHandle);

                if (typeIsVisible && (type.Attributes & TypeAttributes.Sealed) == 0)
                    _derivableTypes.Add(typeName);

                foreach (var methodHandle in type.GetMethods())
                {
                    var method = md.GetMethodDefinition(methodHandle);
                    string name = md.GetString(method.Name);
                    var (arity, _, _) = ReadSignature(md, method.Signature);
                    var access = method.Attributes & MethodAttributes.MemberAccessMask;
                    int id = _methods.Count;

                    var scanned = new ScannedMethod {
                        Assembly = assembly,
                        PathIndex = pathIndex,
                        Handle = methodHandle,
                        TypeName = typeName,
                        Name = name,
                        Arity = arity,
                        IsStatic = (method.Attributes & MethodAttributes.Static) != 0,
                        IsSpecialName = (method.Attributes & MethodAttributes.SpecialName) != 0,
                        IsSealedForCalls = (type.Attributes & TypeAttributes.Sealed) != 0 || (method.Attributes & MethodAttributes.Virtual) == 0 || (method.Attributes & MethodAttributes.Final) != 0 ||
                            access is MethodAttributes.Assembly or MethodAttributes.FamANDAssem or MethodAttributes.Private,
                        IsAbstract = (method.Attributes & MethodAttributes.Abstract) != 0,
                        IsVisible = typeIsVisible && access is MethodAttributes.Public or MethodAttributes.Family or MethodAttributes.FamORAssem,
                        Key = GetKey(typeName, name, arity),
                    };

                    _methods.Add(scanned);

                    if (!_methodsByKey.TryGetValue(scanned.Key, out var sameKey))
                        _methodsByKey[scanned.Key] = sameKey = [];

                    sameKey.Add(id);

                    if (method.RelativeVirtualAddress is not 0 && (method.ImplAttributes & MethodImplAttributes.CodeTypeMask) is MethodImplAttributes.IL)
                        CollectCalls(md, pe.GetMethodBody(method.RelativeVirtualAddress).GetILBytes()!, scanned);
                }
            }
        }

        public bool ChainReaches(string typeName, HashSet<string> classes)
        {
            for (string? current = typeName; current is not null; current = _baseNames.GetValueOrDefault(current))
            {
                if (classes.Contains(current.Replace('+', '.')))
                    return true;
            }

            return false;
        }

        public int Count => _methods.Count;

        // Whether a class, by its documentation name, is a type of the scanned assemblies.
        public bool HasType(string documentationName) => _baseNames.Keys.Any(t => t.Replace('+', '.') == documentationName);

        // Whether a base class of a class, by their documentation names, is one of the classes.
        public bool BaseReaches(string documentationName, HashSet<string> classes)
        {
            string? typeName = _baseNames.Keys.FirstOrDefault(t => t.Replace('+', '.') == documentationName);
            return typeName is not null && _baseNames.GetValueOrDefault(typeName) is { } baseName && ChainReaches(baseName, classes);
        }

        // The public classes that another assembly can derive from, whose base chain reaches one of the classes and that are not one of them:
        // the classes whose members the scan covers for an object of a class that derives from them, as documentation comment IDs.
        public SortedSet<string> FindCoveredClasses(HashSet<string> classes) =>
            new(_derivableTypes.Where(t => !classes.Contains(t.Replace('+', '.')) && ChainReaches(t, classes)).Select(t => "T:" + t.Replace('+', '.')), StringComparer.Ordinal);

        private readonly HashSet<string> _derivableTypes = new(StringComparer.Ordinal);

        // Whether some method of that name in the class can return argument 0, the object that it is called on.
        public bool MayReturnItsObject(string typeName, string name) =>
            _methods.Select((m, id) => (m, id)).Where(x => x.m.TypeName == typeName && x.m.Name == name && !x.m.IsStatic).Any(x => (GetReturns(x.id) & 1) != 0);

        public int Followed { get; private set; }

        public List<CloningMember> FindCloningMembers(HashSet<string> cloningClasses)
        {
            _cloningClasses = cloningClasses;

            // The keys from which a call of MemberwiseClone can be reached: those of the methods that call it, of the declarations in base
            // classes that a virtual call of one binds to, and of the methods that call any of those.
            var reached = new HashSet<int>();
            var candidates = new List<int>();

            for (int id = 0; id < _methods.Count; id++)
            {
                if (_methods[id].CallsMemberwiseClone)
                    AddReached(id);
            }

            for (bool added = true; added;)
            {
                added = false;

                for (int id = 0; id < _methods.Count; id++)
                {
                    if (!reached.Contains(_methods[id].Key) && _methods[id].Callees.Any(reached.Contains))
                    {
                        AddReached(id);
                        added = true;
                    }
                }
            }

            _clones = new ulong[_methods.Count];
            _returns = new ulong[_methods.Count];
            _returnsKnown = new bool[_methods.Count];
            _returnsInProgress = new bool[_methods.Count];
            _isCandidate = new bool[_methods.Count];
            _visited = new bool[_methods.Count];

            foreach (int id in candidates)
                _isCandidate[id] = true;

            // The members that matter are those of the classes whose chain reaches a class that clones itself. What they may clone is
            // worked out from them, through the methods that they call, and again until nothing changes, for calls that go round.
            var targets = candidates.Where(id => ChainReaches(_methods[id].TypeName, cloningClasses)).Distinct().ToList();

            do
            {
                _changed = false;
                Array.Clear(_visited);

                foreach (int id in targets)
                    GetClones(id);
            }
            while (_changed);

            var members = targets.Where(id => _clones[id] != 0).Select(id => {
                var method = _methods[id];
                var (kind, name) = method.IsSpecialName ? MemberOfMethod(method.Name) : ('M', method.Name);
                string arguments = string.Join(",", Enumerable.Range(0, 64).Where(b => (_clones[id] & (1UL << b)) != 0));
                return new CloningMember(method.Assembly, method.TypeName, name, method.IsStatic, kind, method.IsVisible, arguments);
            });

            return [.. members];

            void AddReached(int id)
            {
                candidates.Add(id);
                var method = _methods[id];

                for (string? current = method.TypeName; current is not null; current = _baseNames.GetValueOrDefault(current))
                    reached.Add(GetKey(current, method.Name, method.Arity));
            }
        }

        // The arguments that a method may clone, and those that it may return, with what is known of the methods that it calls.
        private (ulong Clones, ulong Returns) Analyze(int id)
        {
            var (clones, returns, _) = AnalyzeFully(id);
            return (clones, returns);
        }

        // The arguments that a method may clone, those that it may return, and those that may reach code outside the family of its class.
        private (ulong Clones, ulong Returns, ulong Escapes) AnalyzeFully(int id)
        {
            Followed++;
            var method = _methods[id];
            var pe = _readers[method.PathIndex];
            var md = pe.GetMetadataReader();
            var definition = md.GetMethodDefinition(method.Handle);

            if (definition.RelativeVirtualAddress is 0 || (definition.ImplAttributes & MethodImplAttributes.CodeTypeMask) is not MethodImplAttributes.IL)
                return (0, 0, 0);

            var body = pe.GetMethodBody(definition.RelativeVirtualAddress);

            if (!_decoded.TryGetValue(id, out var decoded))
            {
                var decodedInstructions = Decode(body.GetILBytes()!);
                var offsets = new Dictionary<int, int>();

                for (int i = 0; i < decodedInstructions.Count; i++)
                    offsets[decodedInstructions[i].Offset] = i;

                _decoded[id] = decoded = (decodedInstructions, offsets);
            }

            var (instructions, indexByOffset) = decoded;

            int argumentCount = method.Arity + (method.IsStatic ? 0 : 1);
            ulong[] arguments = new ulong[Math.Max(argumentCount, 1)];

            for (int k = 0; k < argumentCount && k < 64; k++)
                arguments[k] = 1UL << k;

            ulong[] locals = new ulong[256];
            ulong clones = 0, returns = 0, escapes = 0;

            // The arguments and locals are followed over the whole method, so the walk is repeated until what they may hold is stable.
            for (bool stable = false; !stable;)
            {
                stable = true;
                ulong[]?[] entry = new ulong[]?[instructions.Count];
                var work = new Stack<int>();

                void Enter(int index, ulong[] stack)
                {
                    if (index < 0 || index >= instructions.Count)
                        return;

                    if (entry[index] is not { } known)
                    {
                        entry[index] = stack;
                        work.Push(index);
                    }
                    else if (known.Length == stack.Length)
                    {
                        bool grew = false;

                        for (int s = 0; s < stack.Length; s++)
                        {
                            if ((stack[s] & ~known[s]) != 0)
                            {
                                known[s] |= stack[s];
                                grew = true;
                            }
                        }

                        if (grew)
                            work.Push(index);
                    }
                }

                Enter(0, []);

                foreach (var region in body.ExceptionRegions)
                {
                    bool takesException = region.Kind is ExceptionRegionKind.Catch or ExceptionRegionKind.Filter;

                    if (indexByOffset.TryGetValue(region.HandlerOffset, out int handler))
                        Enter(handler, takesException ? [0] : []);

                    if (region.Kind is ExceptionRegionKind.Filter && indexByOffset.TryGetValue(region.FilterOffset, out int filter))
                        Enter(filter, [0]);
                }

                while (work.Count > 0)
                {
                    int index = work.Pop();
                    var stack = new List<ulong>(entry[index]!);
                    var instruction = instructions[index];
                    var op = instruction.Op;
                    ulong Pop() => stack.Count > 0 ? stack[^1] : 0;
                    void Drop(int count) => stack.RemoveRange(Math.Max(0, stack.Count - count), Math.Min(count, stack.Count));

                    switch (op)
                    {
                        case ILOpCode.Ldarg_0 or ILOpCode.Ldarg_1 or ILOpCode.Ldarg_2 or ILOpCode.Ldarg_3 or ILOpCode.Ldarg_s or ILOpCode.Ldarg:
                        {
                            int k = op switch { ILOpCode.Ldarg_0 => 0, ILOpCode.Ldarg_1 => 1, ILOpCode.Ldarg_2 => 2, ILOpCode.Ldarg_3 => 3, _ => instruction.Operand };
                            stack.Add(k < arguments.Length ? arguments[k] : 0);
                            break;
                        }

                        case ILOpCode.Starg_s or ILOpCode.Starg:
                        {
                            ulong value = Pop();
                            Drop(1);

                            if (instruction.Operand < arguments.Length && (value & ~arguments[instruction.Operand]) != 0)
                            {
                                arguments[instruction.Operand] |= value;
                                stable = false;
                            }

                            break;
                        }

                        case ILOpCode.Ldloc_0 or ILOpCode.Ldloc_1 or ILOpCode.Ldloc_2 or ILOpCode.Ldloc_3 or ILOpCode.Ldloc_s or ILOpCode.Ldloc:
                        {
                            int l = op switch { ILOpCode.Ldloc_0 => 0, ILOpCode.Ldloc_1 => 1, ILOpCode.Ldloc_2 => 2, ILOpCode.Ldloc_3 => 3, _ => instruction.Operand };
                            stack.Add(l < locals.Length ? locals[l] : 0);
                            break;
                        }

                        case ILOpCode.Stloc_0 or ILOpCode.Stloc_1 or ILOpCode.Stloc_2 or ILOpCode.Stloc_3 or ILOpCode.Stloc_s or ILOpCode.Stloc:
                        {
                            int l = op switch { ILOpCode.Stloc_0 => 0, ILOpCode.Stloc_1 => 1, ILOpCode.Stloc_2 => 2, ILOpCode.Stloc_3 => 3, _ => instruction.Operand };
                            ulong value = Pop();
                            Drop(1);

                            if (l < locals.Length && (value & ~locals[l]) != 0)
                            {
                                locals[l] |= value;
                                stable = false;
                            }

                            break;
                        }

                        case ILOpCode.Dup:
                            stack.Add(Pop());
                            break;

                        case ILOpCode.Castclass or ILOpCode.Isinst or ILOpCode.Box or ILOpCode.Unbox_any:
                        {
                            ulong value = Pop();
                            Drop(1);
                            stack.Add(value);
                            break;
                        }

                        case ILOpCode.Call or ILOpCode.Callvirt or ILOpCode.Newobj:
                        {
                            var (calleeKey, calleeArity, hasThis, returnsVoid, isMemberwiseClone) = ResolveCall(md, instruction.Operand);
                            int taken = calleeArity + (hasThis && op is not ILOpCode.Newobj ? 1 : 0);
                            ulong[] given = new ulong[taken];

                            for (int a = 0; a < taken; a++)
                                given[a] = stack.Count - taken + a >= 0 ? stack[stack.Count - taken + a] : 0;

                            Drop(taken);

                            if (isMemberwiseClone && taken > 0)
                                clones |= given[0];

                            ulong result = 0;

                            // A call that is given none of the arguments of the method can make it clone or return none of them.
                            if (given.Any(g => g != 0))
                            {
                                // An argument reaches what the method that is called gives it to. A virtual call outside the family of the
                                // class, or a call of a method with no body to read, a delegate's Invoke included, can run code of the
                                // application, an override or an implementation of its own, which receives every argument.
                                int escapeShift = op is ILOpCode.Newobj ? 1 : 0;
                                bool resolved = false;
                                string calleeType = calleeKey >= 0 ? _keyParts[calleeKey].Type : string.Empty;
                                bool familyCode = calleeKey >= 0 && IsExtensionPointOf(calleeType, method.TypeName);
                                bool virtualOutside = op is ILOpCode.Callvirt && calleeKey >= 0 && !familyCode && !IsSealedCall(calleeKey);

                                if (!familyCode)
                                {
                                    foreach (int callee in GetImplementations(calleeKey, op is ILOpCode.Callvirt))
                                    {
                                        resolved = true;
                                        ulong calleeEscapes = GetEscapes(callee, id);

                                        for (int a = 0; a < given.Length; a++)
                                        {
                                            if ((calleeEscapes & (1UL << (a + escapeShift))) != 0)
                                            {
                                                Note(id, given[a], null, callee, a + escapeShift);
                                                escapes |= given[a];
                                            }
                                        }
                                    }

                                    // A virtual call runs the override of the class of the object that it is called on, which receives
                                    // that object anyway: only the other arguments reach code that the call can choose.
                                    if (!resolved || virtualOutside)
                                    {
                                        ulong all = given.Skip(resolved && hasThis ? 1 : 0).Aggregate(0UL, (all, g) => all | g);
                                        Note(id, all, $"{(resolved ? "virtual call outside the family of" : "no body for")} {(calleeKey >= 0 ? KeyText(calleeType, _keyParts[calleeKey].Name, _keyParts[calleeKey].Arity) : "?")}", -1, -1);
                                        escapes |= all;
                                    }
                                }

                                // A virtual call on the object itself runs an override of the class of the method or of one that derives
                                // from it, or a declaration of a base class.
                                bool onOwnObject = !method.IsStatic && op is ILOpCode.Callvirt && hasThis && given.Length > 0 && given[0] == 1;

                                foreach (int callee in GetImplementations(calleeKey, op is ILOpCode.Callvirt))
                                {
                                    if (onOwnObject && !IsInFamily(_methods[callee].TypeName, method.TypeName))
                                        continue;

                                    // A constructor's argument 0 is the new object, which is not on the stack.
                                    int shift = op is ILOpCode.Newobj ? 1 : 0;
                                    clones |= Map(GetClones(callee), given, shift);

                                    if (op is not ILOpCode.Newobj)
                                        result |= Map(GetReturns(callee), given, shift);
                                }
                            }

                            if (op is ILOpCode.Newobj || !returnsVoid)
                                stack.Add(result);

                            break;
                        }

                        case ILOpCode.Ldftn or ILOpCode.Ldvirtftn:
                        {
                            // A delegate of a method that clones its object, made on a value: the value is cloned when it is invoked.
                            var (calleeKey, _, hasThis, _, isMemberwiseClone) = ResolveCall(md, instruction.Operand);
                            ulong target = Pop();

                            if (op is ILOpCode.Ldvirtftn)
                                Drop(1);

                            if (target != 0 && hasThis && (isMemberwiseClone || GetImplementations(calleeKey, op is ILOpCode.Ldvirtftn).Any(c => (GetClones(c) & 1) != 0)))
                                clones |= target;

                            stack.Add(0);
                            break;
                        }

                        case ILOpCode.Ret:
                            if (stack.Count > 0)
                                returns |= Pop();

                            continue;

                        // A value stored in a field, a static, an element or through a reference can be read by other code.
                        case ILOpCode.Stfld or ILOpCode.Stsfld or ILOpCode.Stelem_ref or ILOpCode.Stelem or ILOpCode.Stobj or ILOpCode.Stind_ref:
                        {
                            // A field of an extension point of the family, such as the encoding that a fallback of the encoding refers to,
                            // stays in the family.
                            if (op is not ILOpCode.Stfld || !IsExtensionPointOf(GetFieldOwner(md, instruction.Operand), method.TypeName))
                            {
                                Note(id, Pop(), $"stored by {op}", -1, -1);
                                escapes |= Pop();
                            }

                            var code = s_opCodes[(ushort)op];
                            Drop(PopCount(code.StackBehaviourPop, md, instruction.Operand));
                            break;
                        }

                        default:
                        {
                            var code = s_opCodes[(ushort)op];
                            Drop(PopCount(code.StackBehaviourPop, md, instruction.Operand));

                            for (int p = PushCount(code.StackBehaviourPush); p > 0; p--)
                                stack.Add(0);

                            break;
                        }
                    }

                    // 'leave' empties the stack.
                    ulong[] next = op is ILOpCode.Leave or ILOpCode.Leave_s ? [] : stack.ToArray();

                    foreach (int successor in GetSuccessors(instructions, index, indexByOffset))
                        Enter(successor, (ulong[])next.Clone());
                }
            }

            return (clones, returns, escapes);
        }

        // The arguments of a method that may reach code outside the family of its class, as far as is known: the method that asks is worked
        // out again when that grows. A method with no body to read, such as a delegate's Invoke or an external method, is taken to keep every
        // argument, unless the runtime is known to keep none; an abstract method has no code of its own, and the overrides that a call can
        // reach are followed instead.
        private ulong GetEscapes(int id, int asking)
        {
            if (!_escapesPhase)
                return 0;

            if (_escapes.TryAdd(id, 0))
            {
                if (!HasBody(id))
                {
                    // A lock on the object, by any method of Monitor: the runtime holds the lock, not a reference that other code can read.
                    bool keepsNothing = s_runtimeKeepsNothing.Contains(_methods[id].TypeName + "::" + _methods[id].Name) || _methods[id].TypeName is "System.Threading.Monitor";
                    ulong kept = _methods[id].IsAbstract || keepsNothing ? 0 : ulong.MaxValue;
                    Note(id, kept, "no body", -1, -1);
                    _escapes[id] = kept;
                }
                else
                {
                    Enqueue(id);
                }
            }

            if (!_escapeDependents.TryGetValue(id, out var dependents))
                _escapeDependents[id] = dependents = [];

            dependents.Add(asking);
            return _escapes[id];
        }

        private void Enqueue(int id)
        {
            if (_escapesQueued.Add(id))
                _escapesQueue.Enqueue(id);
        }

        // Methods of the runtime with no body to read that keep nothing of the object that they are given and run no code of an application:
        // the identity hash code and the type of the object, and MemberwiseClone of the .NET Framework, which has no body there and copies
        // the object into a new one. The clone itself is what the scan of what a method clones finds, by the call. The methods of Monitor
        // are taken to keep nothing as well.
        private static readonly HashSet<string> s_runtimeKeepsNothing = new(StringComparer.Ordinal) {
            "System.Runtime.CompilerServices.RuntimeHelpers::GetHashCode",
            "System.Runtime.CompilerServices.RuntimeHelpers::TryGetHashCode",
            "System.Object::GetType",
            "System.Object::MemberwiseClone",
        };

        // The members of the classes whose chain reaches a class that clones itself, that code of another assembly can call, whose own object
        // may reach code outside the family of their class: given to it, stored, or captured.
        public List<CloningMember> FindMembersThatHandOnTheirObject(HashSet<string> cloningClasses)
        {
            var result = new List<CloningMember>();

            // An application does not call a constructor on an existing object, but one can store the new object in another object that a
            // member hands out later, as the constructor of PrinterSettings does in the PageSettings of DefaultPageSettings: those are
            // listed as well, whether visible or not, and the evidence names the member that hands out what holds the object.
            var targets = Enumerable.Range(0, _methods.Count)
                .Where(id => _methods[id] is { IsStatic: false } method && (method.IsVisible || method.Name is ".ctor") && ChainReaches(method.TypeName, cloningClasses))
                .ToList();

            // Each method is worked out with what is known of the methods that it calls, and again when that grows, until nothing changes.
            _escapesPhase = true;

            foreach (int id in targets)
            {
                if (_escapes.TryAdd(id, 0))
                    Enqueue(id);
            }

            while (_escapesQueue.TryDequeue(out int next))
            {
                _escapesQueued.Remove(next);
                ulong escapes = AnalyzeFully(next).Escapes;

                // A method that the scan worked out for what it clones or returns, and that asked for this one, is worked out here as well.
                ulong known = _escapes.GetValueOrDefault(next);

                if ((escapes & ~known) != 0 || !_escapes.ContainsKey(next))
                {
                    _escapes[next] = known | escapes;

                    foreach (int dependent in _escapeDependents.GetValueOrDefault(next) ?? [])
                        Enqueue(dependent);
                }
            }

            foreach (int id in targets)
            {
                var method = _methods[id];

                if ((_escapes.GetValueOrDefault(id) & 1) == 0)
                    continue;

                var (kind, name) = method.IsSpecialName ? MemberOfMethod(method.Name) : ('M', method.Name);
                result.Add(new CloningMember(method.Assembly, method.TypeName, name, IsStatic: false, kind, IsVisible: true, "0"));
            }

            return result;
        }

        private bool HasBody(int id)
        {
            var method = _methods[id];
            var definition = _readers[method.PathIndex].GetMetadataReader().GetMethodDefinition(method.Handle);
            return definition.RelativeVirtualAddress is not 0 && (definition.ImplAttributes & MethodImplAttributes.CodeTypeMask) is MethodImplAttributes.IL;
        }

        // Whether a virtual call goes to a method that no class can override: one of a sealed class, or one that is not virtual or is final.
        private bool IsSealedCall(int key) =>
            _methodsByKey.TryGetValue(key, out var methods) && methods.All(id => _methods[id].IsSealedForCalls);

        // The types that a class's own design hands its object to, and that exist to serve that class: a call that gives them the object
        // stays in the family of the class. This is a judgement written into the scan, not a proof: an application's own subclass of one of
        // them does receive the object.
        private static readonly Dictionary<string, string> s_extensionPoints = new(StringComparer.Ordinal) {
            // An encoding hands itself to its fallbacks and their buffers, which replace what it cannot encode or decode, for that encoding.
            ["System.Text.EncoderFallback"] = "System.Text.Encoding",
            ["System.Text.DecoderFallback"] = "System.Text.Encoding",
            ["System.Text.EncoderFallbackBuffer"] = "System.Text.Encoding",
            ["System.Text.DecoderFallbackBuffer"] = "System.Text.Encoding",

            // An encoding's Encoder and Decoder keep the state of a conversion across calls, for that encoding.
            ["System.Text.Encoder"] = "System.Text.Encoding",
            ["System.Text.Decoder"] = "System.Text.Encoding",

            // The buffers that an encoding's own GetBytes and GetChars make for one call, to hand what the encoding cannot convert to its fallback.
            ["System.Text.Encoding+EncodingByteBuffer"] = "System.Text.Encoding",
            ["System.Text.Encoding+EncodingCharBuffer"] = "System.Text.Encoding",
            ["System.Text.EncodingByteBuffer"] = "System.Text.Encoding",
            ["System.Text.EncodingCharBuffer"] = "System.Text.Encoding",

            // The helper that the calendars based on the Gregorian one make for themselves, to share its arithmetic with their own eras.
            ["System.Globalization.GregorianCalendarHelper"] = "System.Globalization.Calendar",

            // The internal helpers that a stroke makes to walk its own points, for its geometry, its bounds and its hit testing.
            ["MS.Internal.Ink.StrokeNodeIterator"] = "System.Windows.Ink.Stroke",
            ["MS.Internal.Ink.StrokeInfo"] = "System.Windows.Ink.Stroke",
        };

        private static string GetFieldOwner(MetadataReader md, int token)
        {
            var handle = MetadataTokens.EntityHandle(token);

            return handle.Kind switch {
                HandleKind.FieldDefinition => GetName(md, md.GetFieldDefinition((FieldDefinitionHandle)handle).GetDeclaringType()) ?? string.Empty,
                HandleKind.MemberReference => GetName(md, md.GetMemberReference((MemberReferenceHandle)handle).Parent) ?? string.Empty,
                _ => string.Empty,
            };
        }

        // Whether a type is one that a class of the family of 'of' hands its object to by its own design, or derives from one.
        private bool IsExtensionPointOf(string type, string of)
        {
            for (string? current = type; current is not null; current = _baseNames.GetValueOrDefault(current))
            {
                if (s_extensionPoints.TryGetValue(current, out string? root) && ChainReaches(of, [root]))
                    return true;
            }

            return false;
        }

        // Why an argument of a method may reach other code: a text, or the argument of a callee that it is given to.
        private readonly Dictionary<(int Id, int Bit), (string? Text, int Callee, int Argument)> _escapeReasons = [];

        private void Note(int id, ulong mask, string? text, int callee, int argument)
        {
            if (!_escapesPhase)
                return;

            for (int b = 0; b < 64 && mask >> b != 0; b++)
            {
                if ((mask & (1UL << b)) != 0)
                    _escapeReasons.TryAdd((id, b), (text, callee, argument));
            }
        }

        // The chain of calls by which the object of a member may reach other code.
        public string ExplainEscape(string typeName, string name)
        {
            var parts = new List<string>();

            foreach (int start in Enumerable.Range(0, _methods.Count).Where(i => _methods[i].TypeName == typeName && (_methods[i].Name == name || _methods[i].Name.EndsWith("_" + name, StringComparison.Ordinal)) && !_methods[i].IsStatic))
            {
                var chain = new List<string>();
                (int id, int bit) = (start, 0);

                for (int step = 0; step < 40 && _escapeReasons.TryGetValue((id, bit), out var reason); step++)
                {
                    if (reason.Text is not null)
                    {
                        chain.Add($"{_methods[id].TypeName}.{_methods[id].Name}: {reason.Text}");
                        break;
                    }

                    chain.Add($"{_methods[id].TypeName}.{_methods[id].Name}#{bit}");
                    (id, bit) = (reason.Callee, reason.Argument);
                }

                parts.Add(string.Join(" > ", chain));
            }

            return string.Join(" | ", parts);
        }

        private readonly Dictionary<int, ulong> _escapes = [];
        private readonly Dictionary<int, HashSet<int>> _escapeDependents = [];
        private readonly Queue<int> _escapesQueue = new();
        private readonly HashSet<int> _escapesQueued = [];
        private bool _escapesPhase;

        // The arguments that a method may clone, with what is known so far: none for a method from which no call of MemberwiseClone can be
        // reached, and what was found before for one that is being worked out further up the calls or beyond the depth that the stack
        // allows, which a later round takes up.
        private ulong GetClones(int id)
        {
            const int MaxDepth = 48;

            if (!_isCandidate[id] || _visited[id] || _clonesDepth >= MaxDepth)
                return _clones[id];

            _visited[id] = true;
            _clonesDepth++;

            try
            {
                ulong clones = Analyze(id).Clones;

                if ((clones & ~_clones[id]) != 0)
                {
                    _clones[id] |= clones;
                    _changed = true;
                }
            }
            finally
            {
                _clonesDepth--;
            }

            return _clones[id];
        }

        // The arguments that a method may return, worked out when a call of it is first followed: for a method of a class whose chain reaches
        // a class that clones itself, where a member can hand back its object for another one to clone, as 'WithTitle(..).Copy()' would.
        // A method of any other class, one that is being worked out further up the calls, or one beyond the depth that the stack allows,
        // counts as returning none of its arguments.
        // Whether a class is a given class, one of the classes that derive from it, or one of its base classes.
        private bool IsInFamily(string type, string of)
        {
            for (string? current = type; current is not null; current = _baseNames.GetValueOrDefault(current))
            {
                if (current == of)
                    return true;
            }

            for (string? current = of; current is not null; current = _baseNames.GetValueOrDefault(current))
            {
                if (current == type)
                    return true;
            }

            return false;
        }

        private ulong GetReturns(int id)
        {
            const int MaxDepth = 48;

            if (_returnsKnown[id] || _returnsInProgress[id] || _returnsDepth >= MaxDepth || !ChainReaches(_methods[id].TypeName, _cloningClasses))
                return _returns[id];

            _returnsInProgress[id] = true;
            _returnsDepth++;

            try
            {
                _returns[id] = Analyze(id).Returns;
            }
            finally
            {
                _returnsDepth--;
                _returnsInProgress[id] = false;
            }

            _returnsKnown[id] = true;
            return _returns[id];
        }

        // The values given for the arguments that a callee may clone or return, as arguments of the method that calls it.
        private static ulong Map(ulong calleeArguments, ulong[] given, int shift)
        {
            ulong result = 0;

            for (int b = 0; calleeArguments >> b != 0 && b < 64; b++)
            {
                if ((calleeArguments & (1UL << b)) != 0 && b - shift >= 0 && b - shift < given.Length)
                    result |= given[b - shift];
            }

            return result;
        }

        // The methods that a call of a key can run: the method itself, and for a virtual call the overrides of every class that derives
        // from its class. The classes do not change once they are read, so each is worked out once: a virtual call of a member of object
        // would walk every class of the framework.
        private int[] GetImplementations(int key, bool isVirtual)
        {
            if (!_implementations.TryGetValue((key, isVirtual), out int[]? known))
                _implementations[(key, isVirtual)] = known = [.. FindImplementations(key, isVirtual)];

            return known;
        }

        private readonly Dictionary<(int Key, bool IsVirtual), int[]> _implementations = [];

        private IEnumerable<int> FindImplementations(int key, bool isVirtual)
        {
            if (_methodsByKey.TryGetValue(key, out var exact))
            {
                foreach (int id in exact)
                    yield return id;
            }

            if (!isVirtual || key < 0)
                yield break;

            var parts = _keyParts[key];

            var pending = new Stack<string>(_derivedNames.GetValueOrDefault(parts.Type) ?? []);
            var seen = new HashSet<string>(StringComparer.Ordinal);

            while (pending.Count > 0)
            {
                string derived = pending.Pop();

                if (!seen.Add(derived))
                    continue;

                if (_keys.TryGetValue(KeyText(derived, parts.Name, parts.Arity), out int derivedKey) && _methodsByKey.TryGetValue(derivedKey, out var overrides))
                {
                    foreach (int id in overrides)
                        yield return id;
                }

                foreach (string further in _derivedNames.GetValueOrDefault(derived) ?? [])
                    pending.Push(further);
            }
        }

        private int GetKey(string type, string name, int arity)
        {
            string text = KeyText(type, name, arity);

            if (!_keys.TryGetValue(text, out int key))
            {
                _keys[text] = key = _keys.Count;
                _keyParts.Add((name, arity, type));
            }

            return key;
        }

        private static string KeyText(string type, string name, int arity) => $"{type}::{name}/{arity}";

        private void CollectCalls(MetadataReader md, byte[] il, ScannedMethod method)
        {
            var callees = new HashSet<int>();

            foreach (var instruction in Decode(il))
            {
                if (instruction.Op is not (ILOpCode.Call or ILOpCode.Callvirt or ILOpCode.Newobj or ILOpCode.Ldftn or ILOpCode.Ldvirtftn))
                    continue;

                var (key, _, _, _, isMemberwiseClone) = ResolveCall(md, instruction.Operand);
                method.CallsMemberwiseClone |= isMemberwiseClone;
                callees.Add(key);
            }

            method.Callees = [.. callees];
        }

        private (int Key, int Arity, bool HasThis, bool ReturnsVoid, bool IsMemberwiseClone) ResolveCall(MetadataReader md, int token)
        {
            var handle = MetadataTokens.EntityHandle(token);

            if (handle.Kind is HandleKind.MethodSpecification)
                handle = md.GetMethodSpecification((MethodSpecificationHandle)handle).Method;

            (string? name, string? owner, BlobHandle signature) = handle.Kind switch {
                HandleKind.MemberReference when md.GetMemberReference((MemberReferenceHandle)handle) is var r => (md.GetString(r.Name), GetName(md, r.Parent), r.Signature),
                HandleKind.MethodDefinition when md.GetMethodDefinition((MethodDefinitionHandle)handle) is var d => (md.GetString(d.Name), GetName(md, d.GetDeclaringType()), d.Signature),
                _ => (null, null, default),
            };

            if (name is null || owner is null || signature.IsNil)
                return (-1, 0, false, true, false);

            var (arity, hasThis, returnsVoid) = ReadSignature(md, signature);
            bool isMemberwiseClone = name is "MemberwiseClone" && owner is "System.Object" or "System.MarshalByRefObject";
            return (GetKey(owner, name, arity), arity, hasThis, returnsVoid, isMemberwiseClone);
        }

        private static (int Arity, bool HasThis, bool ReturnsVoid) ReadSignature(MetadataReader md, BlobHandle signature)
        {
            var blob = md.GetBlobReader(signature);
            var header = blob.ReadSignatureHeader();

            if (header.Kind is not SignatureKind.Method)
                return (0, false, true);

            if (header.IsGeneric)
                blob.ReadCompressedInteger();

            int arity = blob.ReadCompressedInteger();
            var code = blob.ReadSignatureTypeCode();

            while (code is SignatureTypeCode.RequiredModifier or SignatureTypeCode.OptionalModifier)
            {
                blob.ReadTypeHandle();
                code = blob.ReadSignatureTypeCode();
            }

            return (arity, header.IsInstance, code is SignatureTypeCode.Void);
        }

        private static int PopCount(StackBehaviour behaviour, MetadataReader md, int operand) => behaviour switch {
            StackBehaviour.Pop0 => 0,
            StackBehaviour.Pop1 or StackBehaviour.Popi or StackBehaviour.Popref => 1,
            StackBehaviour.Pop1_pop1 or StackBehaviour.Popi_pop1 or StackBehaviour.Popi_popi or StackBehaviour.Popi_popi8 or StackBehaviour.Popi_popr4 or
                StackBehaviour.Popi_popr8 or StackBehaviour.Popref_pop1 or StackBehaviour.Popref_popi => 2,
            StackBehaviour.Popi_popi_popi or StackBehaviour.Popref_popi_popi or StackBehaviour.Popref_popi_popi8 or StackBehaviour.Popref_popi_popr4 or
                StackBehaviour.Popref_popi_popr8 or StackBehaviour.Popref_popi_popref or StackBehaviour.Popref_popi_pop1 => 3,
            StackBehaviour.Varpop => CalliPopCount(md, operand),
            _ => 0,
        };

        // Only calli is left to Varpop here: its operand is a stand-alone signature, and it also takes the function pointer.
        private static int CalliPopCount(MetadataReader md, int operand)
        {
            var handle = MetadataTokens.EntityHandle(operand);

            if (handle.Kind is not HandleKind.StandaloneSignature)
                return 0;

            var (arity, hasThis, _) = ReadSignature(md, md.GetStandaloneSignature((StandaloneSignatureHandle)handle).Signature);
            return arity + (hasThis ? 1 : 0) + 1;
        }

        private static int PushCount(StackBehaviour behaviour) => behaviour switch {
            StackBehaviour.Push0 => 0,
            StackBehaviour.Push1_push1 => 2,
            StackBehaviour.Varpush => 1,
            _ => 1,
        };

        private static IEnumerable<int> GetSuccessors(List<Instruction> instructions, int index, Dictionary<int, int> indexByOffset)
        {
            var instruction = instructions[index];
            int next = index + 1;

            switch (instruction.Op)
            {
                case ILOpCode.Ret or ILOpCode.Throw or ILOpCode.Rethrow or ILOpCode.Endfinally or ILOpCode.Endfilter or ILOpCode.Jmp:
                    yield break;

                case ILOpCode.Br or ILOpCode.Br_s or ILOpCode.Leave or ILOpCode.Leave_s:
                    if (indexByOffset.TryGetValue(instruction.Targets[0], out int target))
                        yield return target;

                    yield break;

                default:
                    foreach (int branch in instruction.Targets)
                    {
                        if (indexByOffset.TryGetValue(branch, out int branchIndex))
                            yield return branchIndex;
                    }

                    yield return next;
                    yield break;
            }
        }

        private static List<Instruction> Decode(byte[] il)
        {
            var instructions = new List<Instruction>();

            for (int i = 0; i < il.Length;)
            {
                int offset = i;
                int code = il[i++];

                if (code is 0xFE)
                    code = 0xFE00 | il[i++];

                var op = (ILOpCode)code;
                int size = GetOperandSize(op, il, i);
                int operand = size switch { 1 => il[i], 2 => BitConverter.ToUInt16(il, i), 4 or 8 => BitConverter.ToInt32(il, i), _ => 0 };

                if (op is ILOpCode.Ldc_i4_s or ILOpCode.Ldarg_s or ILOpCode.Ldloc_s or ILOpCode.Starg_s or ILOpCode.Stloc_s or ILOpCode.Ldarga_s or ILOpCode.Ldloca_s)
                    operand = il[i];

                int end = i + size;
                int[] targets = op switch {
                    ILOpCode.Switch => [.. Enumerable.Range(0, BitConverter.ToInt32(il, i)).Select(t => end + BitConverter.ToInt32(il, i + 4 + (4 * t)))],
                    _ when IsShortBranch(op) => [end + (sbyte)il[i]],
                    _ when IsLongBranch(op) => [end + BitConverter.ToInt32(il, i)],
                    _ => [],
                };

                instructions.Add(new Instruction(offset, op, operand, targets));
                i = end;
            }

            return instructions;
        }

        private static bool IsShortBranch(ILOpCode op) => op is ILOpCode.Br_s or ILOpCode.Brfalse_s or ILOpCode.Brtrue_s or ILOpCode.Beq_s or ILOpCode.Bge_s or
            ILOpCode.Bgt_s or ILOpCode.Ble_s or ILOpCode.Blt_s or ILOpCode.Bne_un_s or ILOpCode.Bge_un_s or ILOpCode.Bgt_un_s or ILOpCode.Ble_un_s or
            ILOpCode.Blt_un_s or ILOpCode.Leave_s;

        private static bool IsLongBranch(ILOpCode op) => op is ILOpCode.Br or ILOpCode.Brfalse or ILOpCode.Brtrue or ILOpCode.Beq or ILOpCode.Bge or
            ILOpCode.Bgt or ILOpCode.Ble or ILOpCode.Blt or ILOpCode.Bne_un or ILOpCode.Bge_un or ILOpCode.Bgt_un or ILOpCode.Ble_un or ILOpCode.Blt_un or
            ILOpCode.Leave;

        private static bool IsTypeVisible(MetadataReader md, TypeDefinitionHandle handle)
        {
            for (var current = handle; !current.IsNil; current = md.GetTypeDefinition(current).GetDeclaringType())
            {
                var visibility = md.GetTypeDefinition(current).Attributes & TypeAttributes.VisibilityMask;

                if (visibility is not (TypeAttributes.Public or TypeAttributes.NestedPublic or TypeAttributes.NestedFamily or TypeAttributes.NestedFamORAssem))
                    return false;
            }

            return true;
        }

        private sealed record Instruction(int Offset, ILOpCode Op, int Operand, int[] Targets);

        private sealed class ScannedMethod
        {
            public string Assembly { get; init; } = string.Empty;

            public int PathIndex { get; init; }

            public MethodDefinitionHandle Handle { get; init; }

            public string TypeName { get; init; } = string.Empty;

            public string Name { get; init; } = string.Empty;

            public int Arity { get; init; }

            public bool IsStatic { get; init; }

            public bool IsSpecialName { get; init; }

            public bool IsSealedForCalls { get; init; }

            public bool IsAbstract { get; init; }

            public bool IsVisible { get; init; }

            public int Key { get; init; }

            public bool CallsMemberwiseClone { get; set; }

            public int[] Callees { get; set; } = [];
        }
    }
}
