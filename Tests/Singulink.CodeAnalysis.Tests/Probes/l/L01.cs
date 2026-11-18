using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static Src;

class L01
{
    void M(Stack<Guard> st, Dictionary<Guard, int> dk, Dictionary<int, Guard> dv, ValueTask<Guard> vt, List<Guard> list, HashSet<Guard> hs, Queue<Guard> q)
    {
        Guard a = st.Peek();                                     // LL1 report (user cannot loosen)
        dk.Add(Enter(), 1);                                      // LL2 report (TKey)
        dk.TryAdd(Enter(), 1);                                   // LL3 report (TKey)
        var vt2 = vt;                                            // LL4 report (cannot make view)
        Guard b = list.First();                                  // LL5 report? user-listed First (read from stored)
        Guard c = CreateList().First();                          // LL6 OK by user's own entry
        list.Add(Enter());                                       // LL7 OK? (for=Q makes it unlisted? combine)
        object o = list;                                         // LL8 user erasable: OK (user's own claim)
        var hs2 = new HashSet<Guard>();                          // LL9 OK (sub list)
        hs.Add(Enter());                                         // LL10 OK
        Guard d = q.Dequeue();                                   // LL11 report (user tightened new)
    }
}
