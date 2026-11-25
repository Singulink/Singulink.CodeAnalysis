using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.CodeAnalysis.Text;

namespace Singulink.CodeAnalysis.Analyzers;

/// <summary>
/// What the packages of a build depend on, read from the assets file that NuGet writes for the project (project.assets.json), which the
/// targets file of the package gives the compiler as an additional file. It answers two questions: whether the code of an assembly that
/// came in a package can use another assembly of the build, and in which folders NuGet put the packages of the build.
/// </summary>
/// <remarks>
/// <para>
/// The code of a package can use only what it was compiled against, which is what the package depends on: NuGet gives a project the
/// packages that a package names as its dependencies, and theirs. A reference assembly does not show what the code of its package
/// uses, and this does.
/// </para>
/// <para>
/// Only a package is answered for. The file names the assemblies of a package, and those of a project only by a placeholder, and it
/// leaves out what a project references with private assets. Every target of the file is read, as the compilation does not say which
/// one is its own: an assembly is out of reach only where it is in every target that has the package.
/// </para>
/// </remarks>
internal sealed class PackageGraph
{
    /// <summary>
    /// The name of the assets file.
    /// </summary>
    public const string FileName = "project.assets.json";

    // The text of an additional file is the same object for as long as the file is, so the file is read once.
    private static readonly ConditionalWeakTable<SourceText, StrongBox<PackageGraph?>> s_graphs = new();

    private readonly List<Dictionary<string, Library>> _targets;

    private PackageGraph(List<Dictionary<string, Library>> targets, List<string> packageFolders)
    {
        _targets = targets;
        PackageFolders = packageFolders;
    }

    /// <summary>
    /// Gets the folders that NuGet restores the packages of the build to and from: the global packages folder and every fallback folder.
    /// A package's files are in one of them.
    /// </summary>
    public IReadOnlyList<string> PackageFolders { get; }

    /// <summary>
    /// Gets the graph of an assets file, or null if the text is not one that can be read.
    /// </summary>
    public static PackageGraph? Get(SourceText text) => s_graphs.GetValue(text, static t => new StrongBox<PackageGraph?>(Read(t))).Value;

    /// <summary>
    /// Gets a value indicating whether an assembly came in a package whose code cannot use another assembly: no package that the
    /// package depends on has the other assembly. False where the assembly is of no package, and where that is not known.
    /// </summary>
    public bool IsOutOfReach(string assembly, string other)
    {
        bool isOfAPackage = false;

        foreach (var libraries in _targets)
        {
            HashSet<string>? reach = null;

            foreach (var library in libraries)
            {
                if (library.Value.IsPackage && library.Value.CompileAssemblies.Contains(assembly))
                    AddWithDependencies(library.Key, libraries, reach ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            }

            if (reach is null)
                continue;

            isOfAPackage = true;

            // A project among the dependencies has assemblies that the file does not name, and so has a dependency that the file has
            // no entry for. Either can be the other assembly, unless that one is known to be of a package.
            bool reachesUnnamedAssemblies = false;

            foreach (string id in reach)
            {
                if (!libraries.TryGetValue(id, out var library) || !library.IsPackage)
                    reachesUnnamedAssemblies = true;
                else if (library.Assemblies.Contains(other))
                    return false;
            }

            if (reachesUnnamedAssemblies && !libraries.Values.Any(l => l.IsPackage && l.Assemblies.Contains(other)))
                return false;
        }

        return isOfAPackage;
    }

    private static void AddWithDependencies(string id, Dictionary<string, Library> libraries, HashSet<string> reach)
    {
        var pending = new Stack<string>();
        pending.Push(id);

        while (pending.Count > 0)
        {
            string next = pending.Pop();

            if (!reach.Add(next) || !libraries.TryGetValue(next, out var library))
                continue;

            foreach (string dependency in library.Dependencies)
                pending.Push(dependency);
        }
    }

    private static PackageGraph? Read(SourceText text)
    {
        try
        {
            var reader = new JsonReader(text.ToString());
            var targets = new List<Dictionary<string, Library>>();
            var packageFolders = new List<string>();

            if (!reader.EnterObject())
                return null;

            while (reader.NextKey(out string key))
            {
                if (key is "packageFolders")
                {
                    ReadKeys(ref reader, packageFolders, isPath: false);
                    continue;
                }

                if (key is not "targets" || !reader.EnterObject())
                {
                    reader.Skip();
                    continue;
                }

                while (reader.NextKey(out _))
                    targets.Add(ReadTarget(ref reader));
            }

            return targets.Count > 0 || packageFolders.Count > 0 ? new PackageGraph(targets, packageFolders) : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static Dictionary<string, Library> ReadTarget(ref JsonReader reader)
    {
        var libraries = new Dictionary<string, Library>(StringComparer.OrdinalIgnoreCase);

        if (!reader.EnterObject())
        {
            reader.Skip();
            return libraries;
        }

        // The name of a library is its ID and its version: 'SkiaSharp/3.119.0'.
        while (reader.NextKey(out string name))
        {
            int slash = name.IndexOf('/');
            var library = new Library();
            libraries[slash < 0 ? name : name[..slash]] = library;

            if (!reader.EnterObject())
            {
                reader.Skip();
                continue;
            }

            while (reader.NextKey(out string key))
            {
                switch (key)
                {
                    case "type":
                        library.IsPackage = reader.ReadString() is "package";
                        break;
                    case "dependencies":
                        ReadKeys(ref reader, library.Dependencies, isPath: false);
                        break;
                    case "compile":
                        ReadKeys(ref reader, library.CompileAssemblies, isPath: true);
                        break;
                    case "runtime":
                        ReadKeys(ref reader, library.Assemblies, isPath: true);
                        break;
                    default:
                        reader.Skip();
                        break;
                }
            }

            library.Assemblies.UnionWith(library.CompileAssemblies);
        }

        return libraries;
    }

    // Reads the names of an object's members, and of a path the name of the assembly: 'ref/net10.0/SkiaSharp.dll' is 'SkiaSharp'. A
    // folder with no assembly for the target has the placeholder '_._', which is no assembly.
    private static void ReadKeys(ref JsonReader reader, ICollection<string> names, bool isPath)
    {
        if (!reader.EnterObject())
        {
            reader.Skip();
            return;
        }

        while (reader.NextKey(out string key))
        {
            reader.Skip();

            if (!isPath)
            {
                names.Add(key);
                continue;
            }

            string file = key[(key.LastIndexOf('/') + 1)..];
            int dot = file.LastIndexOf('.');

            if (dot > 0 && file[dot..] is var extension &&
                (extension.Equals(".dll", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".winmd", StringComparison.OrdinalIgnoreCase)))
            {
                names.Add(file[..dot]);
            }
        }
    }

    private sealed class Library
    {
        public bool IsPackage { get; set; }

        public List<string> Dependencies { get; } = [];

        // The assemblies that a project is compiled against, and with them those that run.
        public HashSet<string> CompileAssemblies { get; } = new(StringComparer.OrdinalIgnoreCase);

        public HashSet<string> Assemblies { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    // Reads the objects and the strings of a JSON text and skips everything else, which is all that the assets file is read for. It
    // makes no copy of what it skips. A text that is not JSON ends the reading with a FormatException.
    private struct JsonReader(string text)
    {
        private int _position;

        // Enters an object if the next value is one. Otherwise the value is left where it is, for the caller to skip.
        public bool EnterObject()
        {
            SkipSpace();

            if (_position >= text.Length || text[_position] is not '{')
                return false;

            _position++;
            return true;
        }

        // Reads the name of the next member of the object that was entered, and leaves its value to the caller. At the end of the object,
        // leaves it and returns false.
        public bool NextKey(out string key)
        {
            SkipSpace();

            if (Peek() is ',')
            {
                _position++;
                SkipSpace();
            }

            if (Peek() is '}')
            {
                _position++;
                key = string.Empty;
                return false;
            }

            key = ReadStringCore();
            SkipSpace();

            if (Peek() is not ':')
                throw new FormatException();

            _position++;
            return true;
        }

        // Reads the next value if it is a string, and skips it otherwise.
        public string? ReadString()
        {
            SkipSpace();

            if (Peek() is '"')
                return ReadStringCore();

            Skip();
            return null;
        }

        // Skips the next value, whatever it is.
        public void Skip()
        {
            SkipSpace();
            int depth = 0;

            do
            {
                switch (Peek())
                {
                    case '"':
                        SkipStringCore();
                        continue;
                    case '{' or '[':
                        depth++;
                        break;
                    case '}' or ']':
                        // The end of the object or the array that the value is in ends a number or a word.
                        if (depth is 0)
                            return;

                        depth--;
                        break;
                    case ',' when depth is 0:
                        return;
                }

                _position++;
            }
            while (depth > 0 || !IsEndOfScalar());
        }

        private readonly bool IsEndOfScalar() => _position >= text.Length || text[_position] is ',' or '}' or ']' or ' ' or '\t' or '\r' or '\n';

        private readonly char Peek() => _position < text.Length ? text[_position] : throw new FormatException();

        private void SkipSpace()
        {
            while (_position < text.Length && text[_position] is ' ' or '\t' or '\r' or '\n')
                _position++;
        }

        private void SkipStringCore()
        {
            if (Peek() is not '"')
                throw new FormatException();

            _position++;

            while (Peek() is not '"')
                _position += text[_position] is '\\' ? 2 : 1;

            _position++;
        }

        private string ReadStringCore()
        {
            int start = _position + 1;
            SkipStringCore();
            string value = text[start..(_position - 1)];

            if (value.IndexOf('\\') < 0)
                return value;

            var builder = new StringBuilder(value.Length);

            for (int i = 0; i < value.Length; i++)
            {
                if (value[i] is not '\\')
                {
                    builder.Append(value[i]);
                    continue;
                }

                // SkipStringCore left no backslash at the end of the value.
                switch (value[++i])
                {
                    case 'b':
                        builder.Append('\b');
                        break;
                    case 'f':
                        builder.Append('\f');
                        break;
                    case 'n':
                        builder.Append('\n');
                        break;
                    case 'r':
                        builder.Append('\r');
                        break;
                    case 't':
                        builder.Append('\t');
                        break;
                    case 'u' when i + 4 < value.Length && ushort.TryParse(
                        value[(i + 1)..(i + 5)], System.Globalization.NumberStyles.AllowHexSpecifier, System.Globalization.CultureInfo.InvariantCulture, out ushort code):
                        builder.Append((char)code);
                        i += 4;
                        break;
                    case 'u':
                        throw new FormatException();
                    default:
                        builder.Append(value[i]);
                        break;
                }
            }

            return builder.ToString();
        }
    }
}
