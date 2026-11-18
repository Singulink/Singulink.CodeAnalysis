using System.Threading.Tasks;
using Singulink.CodeAnalysis;

public class MoveUse
{
    private Task<Guard> _field = null!;
    private readonly RwLock _rwLock = new();

    static T MyMove<[NonCopyable] T>([MovedFrom] ref T value)
    { T moved = value; value = default!; return moved; }

    static Task<Guard> TakeTask([MovedFrom] ref Task<Guard> value)
    { Task<Guard> moved = value; value = null!; return moved; }

    static T Unmarked<[NonCopyable] T>(ref T value) => NonCopyable.Move(ref value)!;

    async Task Use()
    {
        Task<Guard> a = _rwLock.EnterTaskAsync();
        using Guard g1 = await NonCopyable.Move(ref a);        // U01 ours, a local the method owns

        Task<Guard> b = _rwLock.EnterTaskAsync();
        using Guard g2 = await MyMove(ref b);                  // U02 a user's generic move

        Task<Guard> c = _rwLock.EnterTaskAsync();
        using Guard g3 = await TakeTask(ref c);                // U03 a user's non-generic move

        using Guard g4 = await NonCopyable.Move(ref _field);   // U04 a field
        using Guard g5 = await MyMove(ref _field);             // U05
        using Guard g6 = await TakeTask(ref _field);           // U06

        Task<Guard> d = _rwLock.EnterTaskAsync();
        Task<Guard> alias = d;
        using Guard g7 = await MyMove(ref d);                  // U07 the local was copied
        using Guard g8 = await MyMove(ref alias);              // U08

        Task<Guard> e = _rwLock.EnterTaskAsync();
        using Guard g9 = await Unmarked(ref e);                // U09 no [MovedFrom] on the parameter
    }
}
