using System.Threading.Tasks;
public static class Q2
{
    public static Task<Guard> M01() { var s = new TaskCompletionSource<Guard>(); return s.Task; }   // M01 reported (stricter entry)
}
public static class Program { public static void Main() { } }
