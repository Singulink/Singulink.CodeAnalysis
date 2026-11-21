using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Singulink.CodeAnalysis.Tests;

/// <summary>
/// Pins why the members that the compiler writes for a record or an anonymous type are not checked: they do nothing with the values of
/// their members but call ToString, Equals and GetHashCode, and no member of that name of a framework class clones its object.
/// </summary>
/// <remarks>
/// Docs: DESIGN.md, the row of the code that the compiler writes for a record or an anonymous type.
/// </remarks>
public partial class FrameworkScanTests
{
    private const string CompilerWrittenSource = """
        public class Holder;

        public record class RecordClass(Holder Holder, int Number);

        public record struct RecordStruct(Holder Holder, int Number);

        public static class Anonymous
        {
            public static object Make(Holder holder, int number) => new { Holder = holder, Number = number };
        }
        """;

    // Every member of another type that a member of the records and the anonymous type calls, as 'Type::Name', with whether it touches
    // the values of their members. A member of the records or the anonymous type themselves, such as a getter, PrintMembers or the copy
    // constructor, is scanned in its own right.
    private static readonly Dictionary<string, string> CompilerWrittenCalls = new(StringComparer.Ordinal) {
        // These touch the values: they call ToString, Equals or GetHashCode of a member, which is what the built-in list test pins.
        ["System.Object::ToString"] = "touches a value: ToString of a member, called on a value type with 'constrained.'",
        ["System.Text.StringBuilder::Append"] = "touches a value: Append(object) calls ToString of a member",
        ["System.Collections.Generic.EqualityComparer`1::Equals"] = "touches a value: calls Equals of a member",
        ["System.Collections.Generic.EqualityComparer`1::GetHashCode"] = "touches a value: calls GetHashCode of a member",

        // These do not touch the values of the members.
        ["System.Object::.ctor"] = "the base constructor",
        ["System.Text.StringBuilder::.ctor"] = "makes the builder of ToString",
        ["System.Collections.Generic.EqualityComparer`1::get_Default"] = "gets the comparer",
        ["System.String::Format"] = "formats the ToString of an anonymous type from the strings of its members, which their ToString made",
        ["System.Runtime.CompilerServices.RuntimeHelpers::EnsureSufficientExecutionStack"] = "the stack check of PrintMembers",
        ["System.Type::GetTypeFromHandle"] = "the EqualityContract of a record class",
        ["System.Type::op_Equality"] = "compares the EqualityContract of two records",
    };

    // The members that the compiler writes for a record class, a record struct and an anonymous type, with a member of a class type and
    // one of a value type, call no member of another type but those above. It fails if a compiler makes them do anything else with the
    // values of their members, MemberwiseClone or Clone among them. It runs with the compiler of each target of the tests.
    [TestMethod]
    public async Task CompilerWrittenMembers_OfRecordsAndAnonymousTypesCallOnlyToStringEqualsAndGetHashCodeOfTheirMembers()
    {
        var references = await Verify.Test.NetReferenceAssemblies.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);

        var compilation = CSharpCompilation.Create(
            "CompilerWritten",
            [CSharpSyntaxTree.ParseText(CompilerWrittenSource, new CSharpParseOptions(LanguageVersion.Latest))],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        Assert.IsTrue(result.Success, string.Join(Environment.NewLine, result.Diagnostics));

        stream.Position = 0;
        using var pe = new PEReader(stream);
        var md = pe.GetMetadataReader();
        var scanned = new HashSet<string>(StringComparer.Ordinal);
        var called = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var handle in md.TypeDefinitions)
        {
            string name = GetName(md, handle)!;

            if (name is not ("RecordClass" or "RecordStruct") && !name.StartsWith("<>f__AnonymousType", StringComparison.Ordinal))
                continue;

            scanned.Add(name);

            foreach (var methodHandle in md.GetTypeDefinition(handle).GetMethods())
            {
                var method = md.GetMethodDefinition(methodHandle);

                if (method.RelativeVirtualAddress is not 0)
                    CollectCalledMembers(md, pe.GetMethodBody(method.RelativeVirtualAddress).GetILBytes()!, called);
            }
        }

        Assert.HasCount(3, scanned, "The scan finds the two records and the anonymous type: " + string.Join(", ", scanned));

        // The calls of the members of the records and of the anonymous type themselves.
        called.RemoveWhere(c => scanned.Contains(c[..c.IndexOf("::", StringComparison.Ordinal)]));

        var unexpected = called.Where(c => !CompilerWrittenCalls.ContainsKey(c)).ToList();
        Assert.IsEmpty(unexpected, "The members that the compiler writes call members that may do something else with the values of their members:\n" + string.Join("\n", unexpected));
        Assert.Contains("System.Collections.Generic.EqualityComparer`1::Equals", called, "The scan finds the calls of Equals.");
        Assert.Contains("System.Text.StringBuilder::Append", called, "The scan finds the calls of Append.");
    }

    // No 'clones' line of the built-in list names a member called ToString, Equals or GetHashCode. The scan of the classes fails for a class
    // that clones itself with MemberwiseClone, can be derived from in another assembly and has no 'clones' line of its own or of a base
    // class. The scan of the members fails for a public or protected member of a class whose base chain reaches a class with such a line,
    // which clones its object or an object that it is given, directly or through the members of the framework that it calls (a virtual
    // call reaching the overrides of the framework, to a depth of 48 calls), and has no 'clones' line. Both read the shared frameworks
    // that the tests run on. So together with this test they show that no ToString, Equals or GetHashCode of a framework class that a
    // class of another assembly can derive from clones its object or one that it is given, and the members that the compiler writes for a
    // record or an anonymous type, which do nothing else with the values of their members (the test above), need no check for them.
    [TestMethod]
    public void BuiltInList_NamesNoToStringEqualsOrGetHashCodeAsAMemberThatClones()
    {
        var entries = ReadEntries();
        var clonesLines = entries.Where(e => e.Value is "clones").Select(e => e.Key).ToList();
        Assert.IsTrue(clonesLines.Any(id => id.StartsWith("M:", StringComparison.Ordinal)), "The list has 'clones' lines for members.");

        var named = clonesLines
            .Where(id => id.StartsWith("M:", StringComparison.Ordinal) || id.StartsWith("P:", StringComparison.Ordinal))
            .Where(id => id[2..].Split('(')[0].Split('.')[^1] is "ToString" or "Equals" or "GetHashCode")
            .ToList();

        Assert.IsEmpty(named, "These 'clones' lines name a member that the code which the compiler writes for a record or an anonymous type calls:\n" + string.Join("\n", named));
    }

    // Adds the member of every call, 'newobj' and method group of a method body, as 'Type::Name'.
    private static void CollectCalledMembers(MetadataReader md, byte[] il, SortedSet<string> called)
    {
        for (int i = 0; i < il.Length;)
        {
            int code = il[i++];

            if (code is 0xFE)
                code = 0xFE00 | il[i++];

            var op = (ILOpCode)code;
            int size = GetOperandSize(op, il, i);

            if (op is ILOpCode.Call or ILOpCode.Callvirt or ILOpCode.Newobj or ILOpCode.Ldftn or ILOpCode.Ldvirtftn or ILOpCode.Jmp)
                called.Add(GetMemberName(md, MetadataTokens.EntityHandle(BitConverter.ToInt32(il, i))));

            i += size;
        }
    }

    private static string GetMemberName(MetadataReader md, EntityHandle handle) => handle.Kind switch {
        HandleKind.MethodDefinition when md.GetMethodDefinition((MethodDefinitionHandle)handle) is var d => GetName(md, d.GetDeclaringType()) + "::" + md.GetString(d.Name),
        HandleKind.MemberReference when md.GetMemberReference((MemberReferenceHandle)handle) is var r => GetName(md, r.Parent) + "::" + md.GetString(r.Name),
        HandleKind.MethodSpecification => GetMemberName(md, md.GetMethodSpecification((MethodSpecificationHandle)handle).Method),
        _ => $"unknown member {handle.Kind}",
    };
}
