using System.Threading.Tasks;
using Singulink.CodeAnalysis;
static class Wrap2
{
    static Task<Guard>? MoveTask([MovedFrom] ref Task<Guard>? v) { var m = v; v = null; return m; }  // Y01
    static Task<Guard>? MoveTask2([MovedFrom] ref Task<Guard>? v) { Task<Guard>? m = v; v = null; return m; } // Y02
}
