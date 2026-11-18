using System;
using System.Collections.Generic;

public static partial class Program
{
    public static readonly HashSet<string> Skip = [];

    // The method that is about to run, for the driver to leave out if the runtime itself fails in it.
    public static void Mark(string id) => System.IO.File.WriteAllText("current.txt", id);

    public static void Main(string[] args)
    {
        if (System.IO.File.Exists("skip.txt"))
            Skip.UnionWith(System.IO.File.ReadAllLines("skip.txt"));

        Tables.RunAll();
        System.IO.File.Delete("current.txt");
        Console.WriteLine($"calls={Sink.Calls} odd={Sink.Odd}");
    }
}
