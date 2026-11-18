using System;
using System.Linq;
using System.Reflection;

public static class Program
{
    public static void Main(string[] args)
    {
        foreach (var type in typeof(Program).Assembly.GetTypes().Where(t => t.Name.StartsWith("RT")).OrderBy(t => t.Name))
        foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.Static).Where(m => m.ReturnType == typeof(Lock) && m.GetParameters().Length == 0).OrderBy(m => m.Name))
        {
            if (args.Length > 0 && !args.Contains(m.Name)) continue;
            try { var l = (Lock)m.Invoke(null, null)!; Console.WriteLine($"{type.Name}.{m.Name}: {l}{(l.Releases > l.Acquires ? "   <-- MORE RELEASES THAN ACQUIRES" : "")}"); }
            catch (Exception e) { Console.WriteLine($"{type.Name}.{m.Name}: threw {e.InnerException?.GetType().Name}"); }
        }
    }
}
