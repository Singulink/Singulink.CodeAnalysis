# Generates Z<seed>.cs: random methods that test the move on the last use of a variable and the commit at the end of a 'try'.
#
# A method owns locals and a by-value parameter of a guard type, of 'Guard?', of a tuple with a guard, of 'Task<Guard>' and of
# 'List<Guard>'. Its statements read them by value (into calls that release what they take, into fields, into other locals, into
# boxes, tuples, patterns and 'using'), use them in place, take references to them, assign them, and do all of that inside every
# kind of control flow: 'if', loops, 'switch', 'goto', 'try' with typed, filtered and plain 'catch' clauses and 'finally', 'using',
# 'lock', 'foreach', conditional expressions and '&&'. The commit shape 'try { ...; F1 = g; <tail> } catch { g.Dispose(); }' is
# one of the statements.
#
# Every method is run with 128 patterns of its conditions and a throwing call at each of ten positions. What it returns and what
# it left in the fields of its object is then released. A method that the analyzer accepts must never release one acquisition twice.
#
# usage: python gen_lz.py SEED COUNT [MODE]     MODE: mix (default) | commit | sparse
import random
import sys

seed = int(sys.argv[1])
count = int(sys.argv[2])
mode = sys.argv[3] if len(sys.argv) > 3 else 'mix'
rnd = random.Random(seed * 104729 + {'mix': 0, 'commit': 1, 'sparse': 2, 'more': 3, 'async': 4}[mode])
MORE = mode in ('more', 'async')
ASYNC = mode == 'async'
COMMIT = mode == 'commit'
SPARSE = mode == 'sparse'


def pick(options):
    total = sum(w for _, w in options)
    x = rnd.uniform(0, total)
    for value, w in options:
        x -= w
        if x <= 0:
            return value
    return options[-1][0]


counter = [0]


def fresh(prefix):
    counter[0] += 1
    return f'{prefix}{counter[0]}'


class Ctx:
    def __init__(self):
        self.vars = []          # (name, kind)
        self.in_loop = False
        self.in_finally = False
        self.labels = ()
        self.back = ()
        self.state = {}         # name -> 'gone' | 'escaped'; a variable that is not named still holds its value
        self.protect = set()    # variables that a handler of an enclosing 'try' uses
        self.outer = set()      # variables that were declared outside the loop that the code is in
        self.is_async = False
        self.in_lock = False

    def but(self, **changes):
        c = Ctx()
        c.vars, c.in_loop, c.in_finally, c.labels, c.back = list(self.vars), self.in_loop, self.in_finally, self.labels, self.back
        c.state, c.protect, c.outer = dict(self.state), set(self.protect), set(self.outer)
        c.is_async, c.in_lock = self.is_async, self.in_lock
        for k, v in changes.items():
            setattr(c, k, v)
        return c

    def loop(self):
        c = self.but(in_loop=True, labels=(), back=())
        c.outer = {n for n, _ in self.vars}
        return c

    def merge(self, *children):
        """After a statement with nested blocks: what any of them used up is used up."""
        for child in children:
            for n, _ in self.vars:
                if n in child.state:
                    self.state[n] = child.state[n]

    def of(self, kind):
        return [n for n, k in self.vars if k == kind]

    def held(self, kind):
        return [n for n, k in self.vars if k == kind and n not in self.state]

    def any(self):
        return rnd.choice(self.vars)


SENSE = 0.9 if mode != 'mix' else 0.85


def sensible():
    return rnd.random() < SENSE


def guard_source(ctx, avoid=None):
    """An expression that reads a guard by value out of a variable in scope, or makes a new one."""
    careful = sensible()
    options = [('l.Enter()', 2)]
    def usable(n):
        return n != avoid and (not careful or (n not in ctx.state and n not in ctx.protect and n not in ctx.outer))
    for n in ctx.of('G'):
        if usable(n):
            options.append(((n, n), 6))
    for n in ctx.of('T'):
        if usable(n):
            options.append(((f'{n}.G', n), 2))
    for n in ctx.of('K'):
        if usable(n):
            options.append(((f'{n}.Result', n), 2))
    for n in ctx.of('V'):
        if usable(n):
            options.append(((f'{n}[0]', n), 2))
    for n in ctx.of('G'):
        if n != avoid and n not in ctx.state:
            options.append((f'NonCopyable.Move(ref {n})', 1))
    chosen = pick(options)
    if isinstance(chosen, tuple):
        ctx.state[chosen[1]] = 'gone'
        return chosen[0]
    return chosen


def ret(ctx, expr):
    """Returns a guard from the function, which an async function hands to something that releases it."""
    return f'{{ Sink.Take({expr}); return 0; }}' if ctx.is_async else f'return {expr};'


def simple(ctx):
    """One statement on a variable in scope."""
    name, kind = ctx.any()
    weights_kind = pick([('consume', 5), ('inplace', 4), ('assign', 2), ('ref', 0.5), ('quiet', 1)])
    if SPARSE:
        weights_kind = pick([('consume', 3), ('inplace', 2), ('assign', 3), ('ref', 0.2), ('quiet', 2)])

    reassign = False
    if sensible():
        st = ctx.state.get(name)
        if st == 'gone':
            weights_kind = pick([('assign', 6), ('quiet', 3)])
        elif st == 'escaped':
            weights_kind = pick([('inplace', 6), ('quiet', 2), ('assign', 1)])
        elif weights_kind == 'ref':
            weights_kind = pick([('ref', 1), ('inplace', 3)])
        elif weights_kind == 'consume' and name in ctx.protect:
            weights_kind = pick([('inplace', 5), ('quiet', 2), ('assign', 1)])
        elif weights_kind == 'consume' and name in ctx.outer:
            if rnd.random() < 0.5 and kind in ('G', 'N', 'T'):
                reassign = True
            else:
                weights_kind = pick([('inplace', 5), ('quiet', 2), ('assign', 1)])

    if kind == 'X':
        if weights_kind in ('assign', 'ref'):
            weights_kind = 'inplace'
        reassign = False
        while True:
            text = simple_of(ctx, name, 'G', weights_kind)
            if f'ref {name}' not in text and f'out {name}' not in text and f'{name} =' not in text:
                break
    else:
        text = simple_of(ctx, name, kind, weights_kind)
    if weights_kind == 'consume':
        ctx.state[name] = 'gone'
    elif weights_kind == 'assign':
        ctx.state.pop(name, None)
    elif weights_kind == 'ref':
        ctx.state[name] = 'escaped'
    if reassign and 'return' not in text:
        text += ' ' + {'G': f'{name} = l.Enter();', 'N': f'{name} = l.TryEnter();', 'T': f'{name} = l.EnterPair();'}[kind]
        ctx.state.pop(name, None)
    return text


def simple_of(ctx, name, kind, weights_kind):
    if kind == 'G':
        if weights_kind == 'consume':
            return pick([
                (f'Sink.Take({name});', 6),
                (f'F1 = {name};', 4),
                (f'F2 = {name};', 2),
                (f'FN = {name};', 1),
                (f'FT = ({name}, 1);', 1),
                (f'Sink.Use(Sink.TakeI({name}));', 1),
                (f'if (Sink.TakeTrue({name})) Sink.Use(1);', 1),
                (f'Sink.Use(Sink.Flag() ? Sink.TakeI({name}) : 0);', 1),
                (f'Sink.Use(Sink.Flag() && Sink.TakeTrue({name}));', 1),
                (f'Sink.Use(Sink.TakeTrue({name}) || Sink.Flag());', 0.5),
                (f'using ({name}) {{ Sink.Step(); }}', 1),
                (f'if ({name} is {{ IsHeld: true }} {fresh("y")}) Sink.Take(y{counter[0]});', 0.7),
                (f'{{ IDisposable {fresh("d")} = {name}; d{counter[0]}.Dispose(); }}', 1),
                (f'{{ object {fresh("o")} = {name}; ((IDisposable)o{counter[0]}).Dispose(); }}', 0.5),
                (f'{{ Guard {fresh("w")} = {name}; Sink.Step(); w{counter[0]}.Dispose(); }}', 2),
                (f'{{ Guard? {fresh("w")} = {name}; Sink.TakeN(w{counter[0]}); }}', 0.7),
                (f'{{ var {fresh("w")} = ({name}, 3); Sink.TakePair(w{counter[0]}); }}', 0.7),
                (f'Sink.Take2({name}, l.Enter());', 0.5),
                (f'Sink.Take2(l.Enter(), {name});', 0.5),
                (f'F1 = Sink.Swap(ref F2, {name});', 0.5),
                (ret(ctx, name), 1.5),
                (f'{{ Guard[] {fresh("a")} = [{name}]; a{counter[0]}[0].Dispose(); }}', 0.4),
                (f'Sink.Take({name} switch {{ {{ IsHeld: true }} => l.Enter(), _ => l.Enter() }});', 0.2),
            ])
        if weights_kind == 'inplace':
            return pick([
                (f'{name}.Dispose();', 6),
                (f'Sink.Release(ref {name});', 3),
                (f'Sink.Touch(ref {name});', 1.5),
                (f'Sink.Use(Sink.Look(in {name}));', 1),
                (f'Sink.Use({name}.IsHeld);', 1.5),
                (f'if ({name}.IsHeld) {name}.Dispose();', 1),
            ])
        if weights_kind == 'assign':
            return pick([
                (f'{name} = l.Enter();', 4),
                (f'{name} = default;', 2),
                (f'Sink.Make(l, out {name});', 1),
                (f'{name} = {guard_source(ctx, avoid=name)};', 4),
                (f'{name} = Sink.Flag() ? l.Enter() : default;', 0.5),
            ])
        if weights_kind == 'ref':
            return pick([
                (f'{{ ref Guard {fresh("r")} = ref {name}; r{counter[0]}.Dispose(); }}', 2),
                (f'Sink.Same(ref {name}).Dispose();', 1),
                (f'{{ Span<Guard> {fresh("s")} = Sink.SpanOf(ref {name}); s{counter[0]}[0].Dispose(); }}', 1),
                (f'{{ Action {fresh("f")} = () => {name}.Dispose(); f{counter[0]}(); }}', 1),
            ])
        return pick([('Sink.Step();', 3), (f'Sink.Use({name}.IsHeld);', 1), ('N++;', 1)])

    if kind == 'N':
        if weights_kind == 'consume':
            return pick([
                (f'Sink.TakeN({name});', 5),
                (f'FN = {name};', 3),
                (f'if ({name} is {{ }} {fresh("y")}) y{counter[0]}.Dispose();', 3),
                (f'if ({name} is {{ }} {fresh("y")}) Sink.Take(y{counter[0]});', 1),
                (f'{{ Guard? {fresh("w")} = {name}; Sink.ReleaseN(ref w{counter[0]}); }}', 1),
                (f'if ({name}.HasValue) F1 = {name}.GetValueOrDefault();', 0.6),
                (f'Sink.Take({name} ?? l.Enter());', 0.6),
            ])
        if weights_kind in ('inplace', 'ref'):
            return pick([(f'Sink.ReleaseN(ref {name});', 5), (f'Sink.Use({name}.HasValue);', 2), (f'Sink.Use({name} is null);', 1)])
        if weights_kind == 'assign':
            return pick([(f'{name} = l.TryEnter();', 3), (f'{name} = null;', 2), (f'{name} = {guard_source(ctx)};', 3)])
        return 'Sink.Step();'

    if kind == 'T':
        if weights_kind == 'consume':
            return pick([
                (f'Sink.TakePair({name});', 4),
                (f'Sink.Take({name}.G);', 4),
                (f'F1 = {name}.G;', 3),
                (f'FT = {name};', 3),
                (f'{{ var ({fresh("a")}, {fresh("n")}) = {name}; a{counter[0] - 1}.Dispose(); }}', 2),
                (f'{{ (F2, N) = {name}; }}', 1),
                (f'if ({name} is ({{ IsHeld: true }} {fresh("y")}, _)) y{counter[0]}.Dispose();', 1),
                (ret(ctx, f'{name}.G'), 0.7),
            ])
        if weights_kind in ('inplace', 'ref'):
            return pick([
                (f'{name}.G.Dispose();', 5),
                (f'Sink.Release(ref {name}.G);', 2),
                (f'Sink.ReleasePair(ref {name});', 2),
                (f'{name}.N = 5;', 1),
                (f'{{ ref Guard {fresh("r")} = ref {name}.G; r{counter[0]}.Dispose(); }}', 0.5),
            ])
        if weights_kind == 'assign':
            return pick([(f'{name} = l.EnterPair();', 3), (f'{name}.G = l.Enter();', 2), (f'{name} = default;', 1), (f'{name} = ({guard_source(ctx)}, 4);', 2)])
        return 'Sink.Step();'

    if kind == 'K':
        if weights_kind == 'consume':
            return pick([
                (f'Sink.Take({name}.Result);', 5),
                (f'{{ Guard {fresh("w")} = {name}.Result; w{counter[0]}.Dispose(); }}', 3),
                (f'Sink.Take({name}.GetAwaiter().GetResult());', 1),
                (f'F1 = {name}.Result;', 2),
                (f'FK = {name};', 2),
                (f'using (var {fresh("w")} = {name}.Result) {{ Sink.Step(); }}', 1),
            ] + ([
                (f'{{ Task<Guard> {fresh("c")} = {name}; Sink.Take(c{counter[0]}.Result); }}', 0.8),
                (f'{{ var {fresh("c")} = ({name}, 1); Sink.Take(c{counter[0]}.Item1.Result); }}', 0.3),
                (f'Sink.Take((Sink.Flag() ? {name} : l.EnterTask()).Result);', 0.5),
                (f'Sink.Take(Sink.Borrow({name}).Result);', 0.5),
                (f'{{ Func<Task<Guard>> {fresh("f")} = () => {name}; Sink.Take(f{counter[0]}().Result); }}', 0.2),
            ] if MORE else []))
        if weights_kind in ('inplace', 'ref', 'quiet'):
            return pick([(f'Sink.Use({name}.IsCompleted);', 3), (f'Sink.Watch({name});', 1), (f'{name}.Wait();', 1), ('Sink.Step();', 1)])
        return pick([(f'{name} = l.EnterTask();', 3), (f'{name} = Task.FromResult(l.Enter());', 1)])

    if kind == 'V':
        if weights_kind == 'consume':
            return pick([
                (f'Sink.Take({name}[0]);', 5),
                (f'foreach (Guard {fresh("x")} in {name}) Sink.Take(x{counter[0]});', 2),
                (f'F1 = {name}[0];', 2),
                (f'FV = {name};', 2),
                (f'{{ Guard {fresh("w")} = {name}[^1]; w{counter[0]}.Dispose(); }}', 1),
            ] + ([
                (f'{{ List<Guard> {fresh("c")} = {name}; Sink.Take(c{counter[0]}[0]); }}', 0.8),
                (f'{{ IReadOnlyList<Guard> {fresh("c")} = {name}; Sink.Take(c{counter[0]}[0]); }}', 0.3),
                (f'{{ var {fresh("e")} = {name}.GetEnumerator(); if (e{counter[0]}.MoveNext()) Sink.Take(e{counter[0]}.Current); }}', 0.3),
                (f'Sink.Take(CollectionsMarshal.AsSpan({name})[0]);', 0.4),
            ] if MORE else []))
        if weights_kind in ('inplace', 'ref', 'quiet'):
            return pick([
                (f'Sink.Use({name}.Count);', 2),
                (f'{name}.Add(l.Enter());', 2),
                (f'Sink.Drain({name});', 1.5),
                (f'CollectionsMarshal.AsSpan({name})[0].Dispose();', 1.5),
                (f'Sink.Fill(l, {name});', 1),
                (f'foreach (ref Guard {fresh("x")} in CollectionsMarshal.AsSpan({name})) x{counter[0]}.Dispose();', 1),
                (f'{name}.Clear();', 0.5),
            ])
        return pick([(f'{name} = l.EnterList();', 3), (f'{name} = [l.Enter()];', 1)])

    if kind == 'U':
        if weights_kind == 'consume':
            return pick([
                (f'Sink.Take({name}.Result);', 5),
                (f'Sink.TakeValueTask({name});', 4),
                (f'{{ Guard {fresh("w")} = {name}.Result; w{counter[0]}.Dispose(); }}', 2),
                (f'FU = {name};', 2),
                (f'Sink.Take({name}.AsTask().Result);', 1),
                (f'{{ ValueTask<Guard> {fresh("c")} = {name}; Sink.TakeValueTask(c{counter[0]}); }}', 1),
            ] + ([(f'Sink.Take(await {name});', 3)] if ctx.is_async and not ctx.in_lock else []))
        if weights_kind in ('inplace', 'ref', 'quiet'):
            return pick([(f'Sink.Use({name}.IsCompleted);', 3), (f'Sink.Use({name}.IsCompletedSuccessfully);', 1), ('Sink.Step();', 1)])
        return pick([(f'{name} = l.EnterValueTask();', 3), (f'{name} = default;', 1)])

    return 'Sink.Step();'


def declare(ctx, lines, ind):
    """Declares a new variable in the current block and adds it to the scope."""
    kind = pick([('G', 6), ('N', 1.5), ('T', 1.5), ('K', 1.2), ('V', 1.2), ('U', 1.2 if MORE else 0)])
    name = fresh({'G': 'g', 'N': 'm', 'T': 't', 'K': 'k', 'V': 'v', 'U': 'u'}[kind])
    pad = '    ' * ind
    if kind == 'G':
        lines.append(pad + pick([
            (f'Guard {name} = l.Enter();', 6),
            (f'Guard {name} = default;', 1),
            (f'Sink.Make(l, out Guard {name});', 1),
            (f'Guard {name} = {guard_source(ctx)};', 2),
            (f'var ({name}, {fresh("n")}) = l.EnterPair();', 1),
        ]))
    elif kind == 'N':
        lines.append(pad + pick([(f'Guard? {name} = l.TryEnter();', 4), (f'Guard? {name} = null;', 1), (f'Guard? {name} = {guard_source(ctx)};', 1)]))
    elif kind == 'T':
        lines.append(pad + pick([(f'(Guard G, int N) {name} = l.EnterPair();', 4), (f'(Guard G, int N) {name} = ({guard_source(ctx)}, 7);', 1)]))
    elif kind == 'U':
        lines.append(pad + f'ValueTask<Guard> {name} = l.EnterValueTask();')
    elif kind == 'K':
        lines.append(pad + f'Task<Guard> {name} = l.EnterTask();')
    else:
        lines.append(pad + f'List<Guard> {name} = l.EnterList();')
    ctx.vars.append((name, kind))


def handler_lines(ctx, used, ind, budget):
    """What a handler does: mostly it releases what the 'try' may have left held."""
    pad = '    ' * ind
    lines = []
    for name, kind in used:
        if kind == 'G':
            lines.append(pad + pick([(f'{name}.Dispose();', 6), (f'Sink.Release(ref {name});', 2), (f'Sink.Take({name});', 1.0), (f'F2 = {name};', 0.7), (f'{name} = default;', 1)]))
        elif kind == 'X':
            lines.append(pad + pick([(f'{name}.Dispose();', 6), (f'Sink.Take({name});', 1.0), (f'F2 = {name};', 0.7)]))
        elif kind == 'N':
            lines.append(pad + pick([(f'Sink.ReleaseN(ref {name});', 4), (f'Sink.TakeN({name});', 0.7)]))
        elif kind == 'T':
            lines.append(pad + pick([(f'{name}.G.Dispose();', 4), (f'Sink.TakePair({name});', 0.7)]))
        elif kind == 'K':
            lines.append(pad + pick([(f'Sink.Take({name}.Result);', 1), (f'Sink.Use({name}.IsCompleted);', 3)]))
        elif kind == 'U':
            lines.append(pad + pick([(f'Sink.TakeValueTask({name});', 1), (f'Sink.Use({name}.IsCompleted);', 3)]))
        else:
            lines.append(pad + pick([(f'Sink.Drain({name});', 3), (f'Sink.Take({name}[0]);', 0.7), (f'Sink.Use({name}.Count);', 1)]))
    if rnd.random() < 0.2 and budget > 0:
        lines += block(ind, 1, budget - 1, ctx)
    return lines


def pick_used(ctx):
    """The variables that the handlers of a 'try' statement use."""
    used = []
    for _ in range(pick([(0, 2), (1, 5), (2, 2)])):
        v = ctx.any()
        if v not in used:
            used.append(v)
    return used


def filter_expr(ctx, used):
    options = [('Sink.Flag()', 4)]
    for name, kind in used:
        if kind in ('G', 'X'):
            options += [(f'Sink.Look(in {name})', 2), (f'Sink.TakeTrue({name})', 0.7), (f'{name}.IsHeld', 1)]
    return pick(options)


SAFE_TAILS = [('', 6), ('N = 1;', 2), ('N++;', 1), ('B = true;', 1), ('N = N + 2; B = !B;', 1), ('S = S + "x";', 0.5), ('O = null;', 0.5),
              ('B = O is string;', 0.5), ('B = O == null;', 0.5), ('B = N > 3 && !B;', 0.5), ('(N, B) = (2, true);', 0.5), ('N <<= 1;', 0.3),
              ('B = S == "a";', 0.3), ('N = -N ^ 3;', 0.3), ('F2 = default;', 0.5), ('FN = null;', 0.3), ('O = this;', 0.3), ('S = null!;', 0.2)]
UNSAFE_TAILS = [('Sink.Step();', 3), ('N = Sink.Pick();', 1), ('B = Sink.Flag();', 1), ('FA[0].Dispose();', 0.5), ('N = FA.Length;', 0.5),
                ('N = N / (N - N);', 0.5), ('N = checked(N + 1);', 0.3), ('if (B) N = 2;', 0.7), ('Holder.N = 1;', 0.5),
                ('N = S.Length;', 0.5), ('S = S + N;', 0.5), ('O = new object();', 0.5), ('B = O is IDisposable;', 0.5), ('N = FA[0].IsHeld ? 1 : 0;', 0.3),
                ('N = B ? 1 : 2;', 0.5), ('N %= 3;', 0.3), ('FA = new Guard[1];', 0.3), ('throw new InvalidOperationException();', 0.2)]


def commit(ctx, ind, budget):
    """The shape 'acquire, set up, commit, or dispose on failure'."""
    pad = '    ' * ind
    guards = ctx.held('G') if sensible() else ctx.of('G')
    if not guards:
        return [pad + 'Sink.Step();']
    name = rnd.choice(guards)
    lines = [pad + 'try', pad + '{']
    inner = ctx.but(labels=(), back=())
    inner.protect.add(name)
    if rnd.random() < 0.6:
        lines += block(ind + 1, rnd.randint(0, 2), max(budget - 1, 0), inner)
    store = pick([
        (f'F1 = {name};', 6), (f'F2 = {name};', 2), (f'FN = {name};', 1), (f'FT = ({name}, 1);', 1), (f'FT.G = {name};', 1),
        (f'FA[0] = {name};', 1), (f'FA[Sink.Pick()] = {name};', 0.5), (f'Holder.G = {name};', 1), (f'Sink.Take({name});', 0.3),
    ])
    lines.append(pad + '    ' + store)
    tail = pick(SAFE_TAILS) if rnd.random() < 0.6 else pick(UNSAFE_TAILS)
    if tail:
        lines.append(pad + '    ' + tail)
    lines.append(pad + '}')
    clause = pick([('catch', 5), ('catch (InvalidOperationException)', 2), ('catch (Exception) when (Sink.Flag())', 1), ('finally', 0.4)])
    lines += [pad + clause, pad + '{']
    lines.append(pad + '    ' + pick([(f'{name}.Dispose();', 6), (f'Sink.Release(ref {name});', 1), (f'Sink.Take({name});', 0.7)]))
    if clause != 'finally' and rnd.random() < 0.5:
        lines.append(pad + '    throw;')
    lines.append(pad + '}')
    ctx.merge(inner)
    ctx.state[name] = 'gone'
    return lines


def statement(ctx, ind, budget):
    pad = '    ' * ind
    options = [('simple', 30 if not SPARSE else 22), ('if', 7), ('ifelse', 4), ('for', 2), ('while', 2), ('do', 1), ('foreach', 1.5), ('switch', 2),
               ('try', 7), ('commit', 4 if not COMMIT else 14), ('using', 1.5), ('lock', 1), ('step', 4), ('throw', 0.7), ('block', 2), ('scoped', 3)]
    if not ctx.in_finally:
        options.append(('return', 1.5))
        if ctx.labels:
            options.append(('goto', 1.5))
        if ctx.back:
            options.append(('gotoback', 1))
    if ctx.in_loop:
        options += [('break', 1.5), ('continue', 1)]
    if MORE:
        options += [('switchexpr', 2), ('usingdecl', 2), ('foreachdecl', 1.5), ('inner', 1.5)]
    if ctx.is_async and not ctx.in_lock:
        options += [('await', 8)]
    kind = pick(options)
    if budget <= 0 and kind not in ('simple', 'step', 'return', 'throw', 'goto', 'break', 'continue'):
        kind = 'simple'

    if kind == 'simple':
        return [pad + simple(ctx)]
    if kind == 'await':
        return [pad + pick([('await Task.Yield();', 2), ('await Sink.StepAsync();', 3), ('await Task.CompletedTask;', 1)])]
    if kind == 'switchexpr':
        guards = ctx.held('G') if sensible() else ctx.of('G')
        guards = [g for g in guards if not sensible() or (g not in ctx.protect and g not in ctx.outer)]
        if not guards:
            return [pad + 'Sink.Step();']
        first = rnd.choice(guards)
        second = rnd.choice(guards)
        ctx.state[first] = 'gone'
        ctx.state[second] = 'gone'
        return [pad + pick([
            (f'Sink.Use(Sink.Pick() switch {{ 0 => Sink.TakeI({first}), 1 => 2, _ => Sink.TakeI({second}) }});', 3),
            (f'Sink.Use(Sink.Pick() switch {{ 0 => Sink.TakeI({first}), _ => 3 }});', 2),
            (f'F1 = Sink.Pick() switch {{ 0 => {first}, 1 => l.Enter(), _ => {second} }};', 2),
            (f'Sink.Take(Sink.Flag() ? {first} : {second});', 2),
            (f'Sink.Take(Sink.Flag() ? {first} : l.Enter());', 1),
        ])]
    if kind == 'step':
        return [pad + 'Sink.Step();']
    if kind == 'throw':
        return [pad + 'if (Sink.Flag()) throw new InvalidOperationException();']
    if kind == 'return':
        guards = [g for g in (ctx.held('G') if sensible() else ctx.of('G')) if g not in ctx.protect or not sensible()]
        nothing = 'return 0;' if ctx.is_async else 'return default;'
        return [pad + (f'if (Sink.Flag()) {ret(ctx, rnd.choice(guards))}' if guards and rnd.random() < 0.5 else f'if (Sink.Flag()) {nothing}')]
    if kind == 'goto':
        return [pad + f'if (Sink.Flag()) goto {rnd.choice(ctx.labels)};']
    if kind == 'gotoback':
        return [pad + f'if (Sink.Fuel()) goto {rnd.choice(ctx.back)};']
    if kind == 'break':
        return [pad + 'if (Sink.Flag()) break;']
    if kind == 'continue':
        return [pad + 'if (Sink.Flag()) continue;']
    if kind == 'commit':
        return commit(ctx, ind, budget)

    n = rnd.randint(1, 3)
    b = budget - 1

    def nested(child, count=None):
        lines = block(ind + 1, n if count is None else count, b, child)
        ctx.merge(child)
        return lines

    if kind == 'block':
        return [pad + '{'] + nested(ctx.but()) + [pad + '}']
    if kind == 'usingdecl':
        return [pad + '{', pad + f'    using var {fresh("s")} = Sink.Scope();'] + nested(ctx.but(labels=(), back=())) + [pad + '}']
    if kind == 'foreachdecl':
        x = fresh('x')
        inner = ctx.loop()
        inner.vars.append((x, 'X'))
        inner.outer.discard(x)
        lines = [pad + f'foreach (Guard {x} in l.EnterList())', pad + '{'] + block(ind + 1, n, b, inner) + [pad + '}']
        ctx.merge(inner)
        return lines
    if kind == 'inner':
        f = fresh('Inner')
        r = fresh('r')
        inner = Ctx()
        inner.vars.append(('p', 'G'))
        body = block(ind + 2, rnd.randint(2, 4), b, inner)
        source = guard_source(ctx)
        return ([pad + '{', pad + f'    Guard {f}(Lock l, Guard p)', pad + '    {'] + body + [pad + '        return default;', pad + '    }',
                 pad + f'    Guard {r} = {f}(l, {source});', pad + f'    {r}.Dispose();', pad + '}'])
    if kind == 'scoped':
        inner = ctx.but()
        lines = [pad + '{']
        declare(inner, lines, ind + 1)
        lines += block(ind + 1, n, b, inner)
        ctx.merge(inner)
        return lines + [pad + '}']
    if kind == 'if':
        return [pad + 'if (Sink.Flag())', pad + '{'] + nested(ctx.but()) + [pad + '}']
    if kind == 'ifelse':
        first, second = ctx.but(), ctx.but()
        lines = ([pad + 'if (Sink.Flag())', pad + '{'] + block(ind + 1, n, b, first) + [pad + '}', pad + 'else', pad + '{'] +
                 block(ind + 1, rnd.randint(1, 2), b, second) + [pad + '}'])
        ctx.merge(first, second)
        return lines
    if kind == 'for':
        i = fresh('i')
        return [pad + f'for (int {i} = 0; {i} < 2; {i}++)', pad + '{'] + nested(ctx.loop()) + [pad + '}']
    if kind == 'while':
        return [pad + 'while (Sink.Fuel())', pad + '{'] + nested(ctx.loop()) + [pad + '}']
    if kind == 'do':
        return [pad + 'do', pad + '{'] + nested(ctx.loop()) + [pad + '}', pad + 'while (Sink.Fuel());']
    if kind == 'foreach':
        e = fresh('e')
        return [pad + f'foreach (int {e} in Sink.Two())', pad + '{'] + nested(ctx.loop()) + [pad + '}']
    if kind == 'using':
        return [pad + 'using (Sink.Scope())', pad + '{'] + nested(ctx.but(labels=(), back=())) + [pad + '}']
    if kind == 'lock':
        return [pad + 'lock (l)', pad + '{'] + nested(ctx.but(labels=(), back=(), in_lock=True)) + [pad + '}']
    if kind == 'switch':
        lines = [pad + 'switch (Sink.Pick())', pad + '{']
        arms = [ctx.but(in_loop=False, labels=(), back=()) for _ in range(3)]
        lines += [pad + '    case 0:', pad + '    {'] + block(ind + 2, rnd.randint(1, 2), b, arms[0]) + [pad + '        ' + pick([('break;', 4), ('goto case 1;', 1)]), pad + '    }']
        lines += [pad + '    case 1:', pad + '    {'] + block(ind + 2, rnd.randint(1, 2), b, arms[1]) + [pad + '        ' + pick([('break;', 4), ('goto default;', 1)]), pad + '    }']
        lines += [pad + '    default:', pad + '    {'] + block(ind + 2, rnd.randint(0, 2), b, arms[2]) + [pad + '        break;', pad + '    }', pad + '}']
        ctx.merge(*arms)
        return lines

    # try
    used = pick_used(ctx)
    body = ctx.but(labels=(), back=())
    body.protect |= {name for name, _ in used}
    lines = [pad + 'try', pad + '{'] + block(ind + 1, n, b, body) + [pad + '}']
    ctx.merge(body)
    shape = pick([('catch', 5), ('typed', 2), ('filter', 2), ('finally', 3), ('catchfinally', 2), ('two', 1.5)])

    def clause(head, throws=True):
        handler = ctx.but(labels=(), back=())
        out = [pad + head, pad + '{'] + handler_lines(handler, used, ind + 1, b)
        if throws and rnd.random() < 0.3:
            out.append(pad + '    throw;')
        return out + [pad + '}']

    if shape == 'catch':
        lines += clause('catch')
    elif shape == 'typed':
        lines += clause('catch (InvalidOperationException)')
    elif shape == 'filter':
        lines += clause(f'catch (Exception) when ({filter_expr(ctx, used)})')
    elif shape == 'two':
        lines += clause(f'catch (InvalidOperationException) when ({filter_expr(ctx, used)})') + clause('catch')
    if shape in ('finally', 'catchfinally'):
        if shape == 'catchfinally':
            lines += clause('catch')
        handler = ctx.but(in_finally=True, in_loop=False, labels=(), back=())
        lines += [pad + 'finally', pad + '{'] + handler_lines(handler, used, ind + 1, 0) + [pad + '}']
    # What a handler released in place is still a variable; what it took by value is used up.
    for name, kind in used:
        if rnd.random() < 0.5:
            ctx.state.setdefault(name, 'gone')
    return lines


def block(ind, n, budget, ctx):
    lines = []
    pad = '    ' * ind
    label = None
    if not ctx.in_finally and rnd.random() < 0.12:
        label = fresh('L')
        ctx.labels = ctx.labels + (label,)
    if not ctx.in_finally and rnd.random() < 0.08:
        back = fresh('B')
        lines.append(pad + f'{back}:;')
        ctx.back = ctx.back + (back,)
    for _ in range(n):
        if rnd.random() < 0.12 and budget > 0:
            declare(ctx, lines, ind)
        lines += statement(ctx, ind, budget)
    if label:
        lines.append(pad + f'{label}:;')
    return lines


name = f'Z{seed}' + {'mix': '', 'commit': 'c', 'sparse': 's', 'more': 'm', 'async': 'a'}[mode]
out = ['// Generated by gen_lz.py. Do not edit.', 'using System;', 'using System.Collections.Generic;', 'using System.Runtime.CompilerServices;',
       'using System.Runtime.InteropServices;', 'using System.Threading.Tasks;', 'using Singulink.CodeAnalysis;', '',
       'public sealed class Holder' + name + ' { public Guard G; public int N; }', '',
       f'public sealed class {name}', '{',
       '    public Guard F1, F2; public Guard? FN; public (Guard G, int N) FT; public Task<Guard>? FK; public List<Guard>? FV; public ValueTask<Guard> FU; public Guard[] FA = new Guard[1];',
       f'    public Holder{name} Holder = new(); public int N; public bool B; public string S = ""; public object? O;', '']
table = []

for i in range(count):
    cid = f'{name}.{i:05d}'
    counter[0] = 0
    ctx = Ctx()
    ctx.is_async = ASYNC
    ctx.vars.append(('p', 'G'))
    lines = []
    for _ in range(rnd.randint(1, 3)):
        declare(ctx, lines, 2)
    if rnd.random() < 0.3:
        ctx.vars.remove(('p', 'G'))
    body = lines + block(2, rnd.randint(3, 6), 4, ctx)
    head = f'    public async Task<int> M{i:05d}(Lock l, Guard p)' if ASYNC else f'    public Guard M{i:05d}(Lock l, Guard p)'
    out += [f'    // CASE {cid}', head, '    {'] + body + ['        return 0;' if ASYNC else '        return default;', '    }', '']
    table.append(f'        ("{cid}", static ({name} h, Lock l, Guard p) => h.M{i:05d}(l, p)),')

out += ['    // END', '}', '', f'public static class {name}Table', '{',
        f'    public delegate {"Task<int>" if ASYNC else "Guard"} Fn({name} h, Lock l, Guard p);', '',
        '    public static readonly (string Id, Fn Run)[] All =', '    ['] + table + ['    ];', '',
        '    public static void RunAll()', '    {', '        foreach (var (id, fn) in All)', '        {',
        '            if (Program.Skip.Contains(id)) continue;', '            Program.Mark(id);', '            string? worst = null;',
        '            for (int flags = 0; flags < 128 && worst is null; flags++)',
        '            for (int throwAt = 0; throwAt < 10 && worst is null; throwAt++)',
        '            {',
        f'                var l = new Lock(); var h = new {name}(); string end = "returned";',
        '                Sink.Reset(flags, throwAt);',
        ('                try { fn(h, l, l.Enter()).GetAwaiter().GetResult(); }' if ASYNC else '                try { Guard r = fn(h, l, l.Enter()); r.Dispose(); }'),
        '                catch (InvalidOperationException) { end = "InvalidOperationException"; }',
        '                catch (Exception e) { end = e.GetType().Name; Sink.Odd++; }',
        '                Sink.Reset(0, 0);',
        '                h.F1.Dispose(); h.F2.Dispose(); Sink.ReleaseN(ref h.FN); h.FT.G.Dispose(); h.FA[0].Dispose(); h.Holder.G.Dispose();',
        '                if (h.FK is { } fk) { Guard x = fk.Result; x.Dispose(); }',
        '                if (h.FV is { } fv) Sink.Drain(fv);',
        '                Sink.TakeValueTask(NonCopyable.Move(ref h.FU));',
        '                Sink.Calls++;',
        '                if (l.AnyTwice) worst = $"TWICE flags={flags} throwAt={throwAt} {end}";',
        '            }',
        '            if (worst is not null) Console.WriteLine($"{id}|{worst}");',
        '        }', '    }', '}', '']
open(f'{name}.cs', 'w', encoding='utf-8', newline='\n').write('\n'.join(out))
print(name, count, 'methods', len(out), 'lines')
