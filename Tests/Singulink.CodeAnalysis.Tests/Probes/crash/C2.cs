using Singulink.CodeAnalysis;
using static Src;
Guard top = Enter();
Guard copy = top;
System.Console.WriteLine(copy.State);
await System.Threading.Tasks.Task.Yield();
return;
