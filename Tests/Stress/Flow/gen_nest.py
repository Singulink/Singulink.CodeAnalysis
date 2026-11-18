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
# usage: python gen_nest.py SEED COUNT MODE     MODE: nest | nesta | nests | nestn | nestc (and the modes of gen_lz.py)
import random
import re
import sys

NL = chr(10)

seed = int(sys.argv[1])
count = int(sys.argv[2])
mode = sys.argv[3] if len(sys.argv) > 3 else 'mix'
MODES = {'mix': 0, 'commit': 1, 'sparse': 2, 'more': 3, 'async': 4, 'nest': 5, 'nesta': 6, 'nests': 7, 'nestn': 8, 'nestc': 9}
rnd = random.Random(seed * 104729 + MODES[mode])
# nest: nested functions of every kind in a class; nesta: the same in async methods; nests: a plain struct host;
# nestn: a non-copyable struct host; nestc: a class with the commit weighted up.
NEST = mode.startswith('nest')
MORE = mode in ('more', 'async') or NEST
ASYNC = mode in ('async', 'nesta')
HOST = {'nests': 'struct', 'nestn': 'ncstruct'}.get(mode, 'class')
COMMIT = mode in ('commit', 'nestc')
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
        self.is_iter = False
        self.has_c = False      # a 'ref int c' parameter is in scope: a reference that can be null
        self.where = 'method'   # 'method' | 'local' (may use 'this') | 'static' (may not)
        self.depth = 0
        self.is_void = False
        self.is_bool = False

    def but(self, **changes):
        c = Ctx()
        c.vars, c.in_loop, c.in_finally, c.labels, c.back = list(self.vars), self.in_loop, self.in_finally, self.labels, self.back
        c.state, c.protect, c.outer = dict(self.state), set(self.protect), set(self.outer)
        c.is_async, c.in_lock = self.is_async, self.in_lock
        c.is_iter, c.has_c, c.where, c.depth, c.is_void = self.is_iter, self.has_c, self.where, self.depth, self.is_void
        c.is_bool = self.is_bool
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
    if ctx.is_iter:
        return f'{{ Sink.Take({expr}); yield break; }}'
    if ctx.is_void:
        return f'{{ Sink.Take({expr}); return; }}'
    if ctx.is_bool:
        return f'{{ Sink.Take({expr}); return Sink.Flag(); }}'
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

    if kind in ('R', 'M'):
        if weights_kind == 'consume':
            opts = [(f'Sink.Take(NonCopyable.Move(ref {name}));', 4), (f'F1 = NonCopyable.Move(ref {name});', 2), (f'Sink.Take({name});', 0.3)]
            if kind == 'M':
                opts += [(moved_from_shape(ctx, name), 8)]
            return pick(opts)
        if weights_kind == 'assign':
            return pick([(f'{name} = l.Enter();', 1), (f'{name} = default;', 2), (f'Sink.Make(l, out {name});', 1)])
        if weights_kind == 'ref':
            return f'{{ ref Guard {fresh("r")} = ref {name}; r{counter[0]}.Dispose(); }}'
        return pick([(f'{name}.Dispose();', 4), (f'Sink.Release(ref {name});', 2), (f'Sink.Touch(ref {name});', 1), (f'Sink.Use(Sink.Look(in {name}));', 1), (f'Sink.Use({name}.IsHeld);', 1)])

    return 'Sink.Step();'


C_PRE = [('NonCopyable.ThrowIfNullRef(in c);', 4), ('c = 0;', 2), ('NI = c;', 2), ('c++;', 1), ('_ = c;', 1), ('Sink.Use(c);', 1),
         ('if (Sink.Flag()) NonCopyable.ThrowIfNullRef(in c);', 1), ('int ci = c;', 0.5), ('Sink.Use(c > 0);', 0.5), ('', 3)]
C_MID = [('c++;', 3), ('c = 1;', 2), ('NI = c;', 2), ('c += N;', 1), ('N = c;', 1), ('Sink.Use(c);', 0.5), ('(c, N) = (2, 3);', 0.5), ('c--;', 0.5)]
PLAIN_MID = [('', 6), ('N = 1;', 2), ('B = !B;', 1), ('N++;', 1), ('Sink.Step();', 1), ('S = S + "x";', 0.5), ('S = "a" + "b";', 0.3), ('N = N / (N | 1);', 0.3),
             ('F2 = default;', 0.5), ('O = null;', 0.5), ('NB <<= 1;', 0.3), ('checked { NB += 1; }', 0.2), ('checked { NB <<= 1; }', 0.2)]


def moved_from_shape(ctx, name):
    """A read of a [MovedFrom] parameter by value, something between, and the assignment."""
    t = fresh('t')
    pre = pick(C_PRE) if ctx.has_c and rnd.random() < 0.7 else ''
    mid = pick(C_MID) if ctx.has_c and rnd.random() < 0.5 else pick(PLAIN_MID)
    if 'int ci' in pre:
        pre = pre.replace('ci', fresh('ci'))
    shape = pick([('local', 5), ('field', 3), ('group', 1), ('take', 1)])
    reset = pick([('default', 6), ('l.Enter()', 1)])
    if shape == 'local':
        return f'{{ {pre} Guard {t} = {name}; {mid} {name} = {reset}; Sink.Take({t}); }}'
    if shape == 'field':
        return f'{{ {pre} F1 = {name}; {mid} {name} = {reset}; }}'
    if shape == 'group':
        return f'{{ {pre} (F1, {name}) = ({name}, default); {mid} }}'
    return f'{{ {pre} Sink.Take({name}); {mid} {name} = default; }}'


def c_statement(ctx):
    """A statement on the 'ref int c' parameter: proofs, reads that prove nothing, stores."""
    text = pick(C_PRE[:-1] + [('if (Sink.Flag()) c = 1;', 0.5), ('Sink.Use(c > 0 && Sink.Flag());', 0.3), ('c <<= 1;', 0.3)])
    return text.replace('ci', fresh('ci'))


GUARD_FIELDS = 'F1|F2|FN|FT|FK|FV|FU|FA|Holder'
ALL_FIELDS = GUARD_FIELDS + '|N|B|S|O|NI|NB'


def rewrite(text, where):
    """Points the fields that a function cannot reach through 'this' at a place that it can reach."""
    if where == 'static':
        text = re.sub(r'(?<![.\w])(?<!int )(?<!Guard )\b(' + ALL_FIELDS + r')\b', NAME + 'St.X.' + r'\1', text)
        text = text.replace('O = this;', 'O = null;').replace('NonCopyable.ThrowIfNullRef(in this);', 'Sink.Step();')
    elif HOST == 'struct':
        text = re.sub(r'(?<![.\w])(?<!int )(?<!Guard )\b(' + GUARD_FIELDS + r')\b', r'H.\1', text)
    return text


def new_inner_ctx(ctx, variant):
    inner = Ctx()
    inner.depth = ctx.depth + 1
    inner.vars.append(('p', 'G'))
    if variant in ('slocal', 'slambda', 'sref') or ctx.where == 'static' or HOST != 'class':
        inner.where = 'static'
    elif variant in ('local', 'ref', 'movedfrom'):
        inner.where = 'local'
    else:
        inner.where = 'local' if HOST == 'class' else 'static'
    inner.has_c = variant not in ('async', 'asynclocal', 'iter', 'filter', 'static', 'lambda')
    inner.is_async = variant in ('async', 'asynclocal')
    inner.is_iter = variant == 'iter'
    return inner


def nested_function(ctx, ind, b):
    """A lambda or a local function with a body of generated statements, declared here and called here."""
    pad = '    ' * ind
    variant = pick([('local', 3), ('slocal', 1.5), ('lambda', 3), ('slambda', 1.5), ('anon', 1), ('async', 1.5), ('asynclocal', 1),
                    ('iter', 2), ('ref', 1.5), ('sref', 0.7), ('movedfrom', 3)])
    if variant == 'lambda':
        variant = 'lambdac'
    inner = new_inner_ctx(ctx, variant)
    f = fresh('Inner')
    r = fresh('r')
    cparam = ', ref int c' if inner.has_c else ''
    carg = (', ref c' if ctx.has_c else ', ref Sink.Cell()') if inner.has_c else ''
    stat = 'static ' if variant in ('slocal', 'slambda', 'sref') else ''
    head = []
    if variant in ('ref', 'sref', 'movedfrom'):
        inner.vars = [('q', 'M' if variant == 'movedfrom' else 'R')]
        if rnd.random() < 0.5:
            inner.vars.append(('p', 'G'))
            head.append(pad + '        Guard p = l.Enter();')
        inner.is_void = True
        mark = '[MovedFrom] ' if variant == 'movedfrom' else ''
        body = head + block(ind + 2, rnd.randint(2, 4), b, inner)
        if ('p', 'G') in inner.vars:
            body.append(pad + '        p.Dispose();')
        body = [rewrite(x, inner.where) for x in body]
        use_lambda = rnd.random() < 0.4
        dtype = 'MFn' if variant == 'movedfrom' else 'RFn'
        if use_lambda:
            decl = [pad + f'    {dtype} {f} = {stat}(Lock l, {mark}ref Guard q, ref int c) =>', pad + '    {'] + body + [pad + '    };']
        else:
            decl = [pad + f'    {stat}void {f}(Lock l, {mark}ref Guard q, ref int c)', pad + '    {'] + body + [pad + '    }']
        carg = ', ref c' if ctx.has_c else ', ref Sink.Cell()'
        held = [g for g in ctx.held('G') if g not in ctx.protect and g not in ctx.outer] if sensible() else ctx.of('G')
        if held:
            g = rnd.choice(held)
            return [pad + '{'] + decl + [pad + f'    {f}(l, ref {g}{carg});', pad + '}']
        t = fresh('t')
        return [pad + '{'] + decl + [pad + f'    Guard {t} = l.Enter();', pad + f'    {f}(l, ref {t}{carg});', pad + f'    {t}.Dispose();', pad + '}']

    if variant == 'iter':
        inner.protect = {'p'}
        parts = []
        for k in range(rnd.randint(1, 3)):
            inner.labels, inner.back = (), ()
            parts += block(ind + 3, rnd.randint(1, 3), b, inner)
            parts.append(pad + '            yield return ' + str(k) + ';')
        fin = handler_lines(inner.but(in_finally=True), [('p', 'G')], ind + 3, 0)
        body = [pad + '        Guard p = l.Enter();', pad + '        try', pad + '        {'] + parts + [pad + '        }', pad + '        finally', pad + '        {'] + fin + [pad + '        }']
        body = [rewrite(x, inner.where) for x in body]
        decl = [pad + f'    IEnumerable<int> {f}(Lock l)', pad + '    {'] + body + [pad + '    }']
        e = fresh('e')
        use = pick([(f'    foreach (int {e} in {f}(l)) Sink.Step();', 2), (f'    {{ using var {e} = {f}(l).GetEnumerator(); Sink.Use({e}.MoveNext()); }}', 2),
                    (f'    {{ var {e} = {f}(l); foreach (int x in {e}) Sink.Step(); foreach (int x in {e}) Sink.Step(); }}', 1)])
        return [pad + '{'] + decl + [pad + use] + [pad + '}']

    source = guard_source(ctx)
    body = block(ind + 2, rnd.randint(2, 4), b, inner)
    end = 'return 0;' if inner.is_async else 'return default;'
    body = [rewrite(x, inner.where) for x in body + [pad + '        ' + end]]
    if variant in ('local', 'slocal'):
        decl = [pad + f'    {stat}Guard {f}(Lock l, Guard p{cparam})', pad + '    {'] + body + [pad + '    }']
        call = [pad + f'    Guard {r} = {f}(l, {source}{carg});', pad + f'    {r}.Dispose();']
    elif variant == 'asynclocal':
        decl = [pad + f'    async Task<int> {f}(Lock l, Guard p)', pad + '    {'] + body + [pad + '    }']
        call = [pad + f'    Sink.Use({f}(l, {source}).GetAwaiter().GetResult());']
    elif variant == 'async':
        decl = [pad + f'    AFn {f} = async (Lock l, Guard p) =>', pad + '    {'] + body + [pad + '    };']
        call = [pad + f'    Sink.Use({f}(l, {source}).GetAwaiter().GetResult());']
    elif variant == 'anon':
        decl = [pad + f'    GFn {f} = delegate (Lock l, Guard p, ref int c)', pad + '    {'] + body + [pad + '    };']
        call = [pad + f'    Guard {r} = {f}(l, {source}{carg});', pad + f'    {r}.Dispose();']
    else:
        decl = [pad + f'    GFn {f} = {stat}(Lock l, Guard p, ref int c) =>', pad + '    {'] + body + [pad + '    };']
        call = [pad + f'    Guard {r} = {f}(l, {source}{carg});', pad + f'    {r}.Dispose();']
    return [pad + '{'] + decl + call + [pad + '}']


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
        elif kind in ('R', 'M'):
            lines.append(pad + pick([(f'{name}.Dispose();', 6), (f'Sink.Release(ref {name});', 2), (f'Sink.Take(NonCopyable.Move(ref {name}));', 1), (f'{name} = default;', 0.5)]))
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


def filter_expr(ctx, used, ind=3):
    options = [('Sink.Flag()', 4)]
    if NEST and ctx.depth < 2:
        options.append(('LAMBDA', 2))
    for name, kind in used:
        if kind in ('G', 'X'):
            options += [(f'Sink.Look(in {name})', 2), (f'Sink.TakeTrue({name})', 0.7), (f'{name}.IsHeld', 1)]
    chosen = pick(options)
    if chosen == 'LAMBDA':
        inner = new_inner_ctx(ctx, 'filter')
        inner.is_bool = True
        if rnd.random() < 0.4:
            inner.where = 'static'
        body = block(ind + 2, rnd.randint(1, 3), 2, inner)
        stat = 'static ' if inner.where == 'static' else ''
        text = NL.join([f'Sink.Test(l, {stat}(l, p) =>', '    ' * (ind + 1) + '{'] + body + ['    ' * (ind + 2) + 'return Sink.Flag();', '    ' * (ind + 1) + '})'])
        return rewrite(text, inner.where)
    return chosen


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
        options += [('switchexpr', 2), ('usingdecl', 2), ('foreachdecl', 1.5), ('inner', 1.5 if not NEST else (6 if ctx.depth < 2 else 1.5))]
    if NEST:
        options += [('exprtree', 0.3)]
    if ctx.has_c:
        options += [('cuse', 4)]
    if ctx.is_async and not ctx.in_lock:
        options += [('await', 8)]
    if ctx.is_async and ctx.depth > 0:
        options = [o for o in options if o[0] != 'lock']
    kind = pick(options)
    if budget <= 0 and kind not in ('simple', 'step', 'return', 'throw', 'goto', 'break', 'continue', 'cuse'):
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
        nothing = 'yield break;' if ctx.is_iter else 'return;' if ctx.is_void else 'return false;' if ctx.is_bool else 'return 0;' if ctx.is_async else 'return default;'
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
        return nested_function(ctx, ind, b)
    if kind == 'exprtree':
        e = fresh('e')
        held = [g for g in ctx.held('G') if g not in ctx.protect]
        if held and rnd.random() < 0.5:
            g = rnd.choice(held)
            ctx.state[g] = 'escaped'
            return [pad + f'{{ Expression<Func<bool>> {e} = () => {g}.IsHeld; Sink.Use({e}.Compile(true)()); }}']
        r = fresh('r')
        return [pad + f'{{ Expression<GMake> {e} = q => q.Enter(); Guard {r} = {e}.Compile(true)(l); {r}.Dispose(); }}']
    if kind == 'cuse':
        return [pad + c_statement(ctx)]
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
        lines += clause(f'catch (Exception) when ({filter_expr(ctx, used, ind)})')
    elif shape == 'two':
        lines += clause(f'catch (InvalidOperationException) when ({filter_expr(ctx, used, ind)})') + clause('catch')
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


name = f'Z{seed}' + {'mix': '', 'commit': 'c', 'sparse': 's', 'more': 'm', 'async': 'a', 'nest': 'n', 'nesta': 'na', 'nests': 'ns', 'nestn': 'nn', 'nestc': 'nc'}[mode]
NAME = name
GUARD_DECL = ('public Guard F1, F2; public Guard? FN; public (Guard G, int N) FT; public Task<Guard>? FK; public List<Guard>? FV; '
              f'public ValueTask<Guard> FU; public Guard[] FA = new Guard[1]; public Holder{name} Holder = new();')
PLAIN_DECL = 'public int N; public bool B; public string S = ""; public object? O; public int NI; public byte NB = 0x80;'
out = ['// Generated by gen_nest.py. Do not edit.', 'using System;', 'using System.Collections.Generic;', 'using System.Linq.Expressions;',
       'using System.Runtime.CompilerServices;', 'using System.Runtime.InteropServices;', 'using System.Threading.Tasks;', 'using Singulink.CodeAnalysis;', '',
       'public sealed class Holder' + name + ' { public Guard G; public int N; }', '',
       f'public sealed class {name}Box {{ {GUARD_DECL} {PLAIN_DECL} }}', '',
       f'public static class {name}St {{ public static {name}Box X = new(); }}', '']
if HOST == 'class':
    out += [f'public sealed class {name}', '{', f'    {GUARD_DECL}', f'    {PLAIN_DECL}', '']
elif HOST == 'struct':
    out += [f'public struct {name}', '{', f'    public {name}Box H = new();', f'    {PLAIN_DECL}', f'    public {name}() {{ }}', '']
else:
    out += ['[NonCopyable]', f'public struct {name}', '{', f'    {GUARD_DECL}', f'    {PLAIN_DECL}', f'    public {name}() {{ }}', '']
table = []
HAS_C = NEST and not ASYNC
STRUCT = HOST != 'class'

for i in range(count):
    cid = f'{name}.{i:05d}'
    counter[0] = 0
    ctx = Ctx()
    ctx.is_async = ASYNC
    ctx.has_c = HAS_C
    ctx.vars.append(('p', 'G'))
    lines = []
    if STRUCT and rnd.random() < 0.4:
        lines.append('        ' + pick([('N = 0;', 2), ('NonCopyable.ThrowIfNullRef(in this);', 2), ('NI = N;', 1), ('Sink.Use(N);', 1)]))
    for _ in range(rnd.randint(1, 3)):
        declare(ctx, lines, 2)
    if rnd.random() < 0.3:
        ctx.vars.remove(('p', 'G'))
    body = lines + block(2, rnd.randint(3, 6), 4, ctx)
    body = [rewrite(x, 'method') for x in body]
    cparam = ', ref int c' if HAS_C else ''
    head = f'    public async Task<int> M{i:05d}(Lock l, Guard p)' if ASYNC else f'    public Guard M{i:05d}(Lock l, Guard p{cparam})'
    out += [f'    // CASE {cid}', head, '    {'] + body + ['        return 0;' if ASYNC else '        return default;', '    }', '']
    href = 'ref ' if STRUCT else ''
    if HAS_C:
        table.append(f'        ("{cid}", static ({href}{name} h, Lock l, Guard p, ref int c) => h.M{i:05d}(l, p, ref c)),')
    else:
        table.append(f'        ("{cid}", static ({href}{name} h, Lock l, Guard p) => h.M{i:05d}(l, p)),')

href = 'ref ' if STRUCT else ''
cdecl = ', ref int c' if HAS_C else ''
carg = ', ref Sink.Cell()' if HAS_C else ''
if ASYNC:
    call = '                try { fn(h, l, l.Enter()).GetAwaiter().GetResult(); }'
elif STRUCT:
    call = (f'                try {{ if (throwAt % 4 == 3) {{ Guard r = fn(ref Unsafe.NullRef<{name}>(), l, l.Enter(){carg}); r.Dispose(); }} '
            f'else {{ Guard r = fn(ref h, l, l.Enter(){carg}); r.Dispose(); }} }}')
else:
    call = f'                try {{ Guard r = fn(h, l, l.Enter(){carg}); r.Dispose(); }}'
holder = 'h.H' if HOST == 'struct' else 'h'
release = [f'                Release({name}St.X);']
if HOST == 'struct':
    release.append('                Release(h.H);')
else:
    release += ['                h.F1.Dispose(); h.F2.Dispose(); Sink.ReleaseN(ref h.FN); h.FT.G.Dispose(); h.FA[0].Dispose(); h.Holder.G.Dispose();',
                '                if (h.FK is { } fk) { Guard x = fk.Result; x.Dispose(); }',
                '                if (h.FV is { } fv) Sink.Drain(fv);',
                '                Sink.TakeValueTask(NonCopyable.Move(ref h.FU));']

out += ['    // END', '}', '', f'public static class {name}Table', '{',
        f'    public delegate {"Task<int>" if ASYNC else "Guard"} Fn({href}{name} h, Lock l, Guard p{cdecl});', '',
        '    public static readonly (string Id, Fn Run)[] All =', '    ['] + table + ['    ];', '',
        f'    static void Release({name}Box h)', '    {',
        '        h.F1.Dispose(); h.F2.Dispose(); Sink.ReleaseN(ref h.FN); h.FT.G.Dispose(); h.FA[0].Dispose(); h.Holder.G.Dispose();',
        '        if (h.FK is { } fk) { Guard x = fk.Result; x.Dispose(); }',
        '        if (h.FV is { } fv) Sink.Drain(fv);',
        '        Sink.TakeValueTask(NonCopyable.Move(ref h.FU));', '    }', '',
        '    public static void RunAll()', '    {', '        foreach (var (id, fn) in All)', '        {',
        '            if (Program.Skip.Contains(id)) continue;', '            Program.Mark(id);', '            string? worst = null;',
        '            for (int flags = 0; flags < 128 && worst is null; flags++)',
        '            for (int throwAt = 0; throwAt < 10 && worst is null; throwAt++)',
        '            {',
        f'                var l = new Lock(); {name}St.X = new(); var h = new {name}(); string end = "returned";',
        '                Sink.Reset(flags, throwAt);',
        call,
        '                catch (InvalidOperationException) { end = "InvalidOperationException"; }',
        '                catch (Exception e) { end = e.GetType().Name; Sink.Odd++; }',
        '                Sink.Reset(0, 0);'] + release + [
        '                Sink.Calls++;',
        '                if (l.AnyTwice) worst = $"TWICE flags={flags} throwAt={throwAt} {end}";',
        '            }',
        '            if (worst is not null) Console.WriteLine($"{id}|{worst}");',
        '        }', '    }', '}', '']
open(f'{name}.cs', 'w', encoding='utf-8', newline='\n').write('\n'.join(out))
print(name, count, 'methods', len(out), 'lines')
