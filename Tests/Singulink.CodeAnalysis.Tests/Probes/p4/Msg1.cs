using System;
using System.Collections.Generic;
using Singulink.CodeAnalysis;
class Msg1
{
    private Guard _g;
    static void Consume(Guard g) { }
    void M(Guard p)
    {
        Guard l = Owner.Enter();
        Consume(l);                       // MS01
        Consume(_g);                      // MS02
        var t = (_g, 1);                  // MS03
        Action a = _g.Touch;              // MS04
        var arr = new[] { _g };           // MS05
        var list = new List<Guard> { _g };// MS06
        Console.WriteLine(_g);            // MS07
    }
}
