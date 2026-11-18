# usage: python stats_nest.py GLOB...   -> what the accepted methods of the given batch folders contain (nested functions and proofs)
import glob
import os
import re
import sys

FEATS = [
    ('a non-static local function', r'^\s+Guard Inner\d+\(Lock l, Guard p'),
    ('a static local function', r'static Guard Inner\d+\(|static void Inner\d+\('),
    ('a lambda (GFn)', r'GFn Inner\d+ = (static )?\('),
    ('an anonymous method', r'GFn Inner\d+ = delegate'),
    ('an async lambda', r'AFn Inner\d+ = async'),
    ('an async local function', r'async Task<int> Inner\d+\('),
    ('an iterator local function with a finally', r'IEnumerable<int> Inner\d+\('),
    ('a ref Guard parameter', r'ref Guard q, ref int c\)'),
    ('a [MovedFrom] parameter of a nested function', r'\[MovedFrom\] ref Guard q'),
    ('a lambda in an exception filter', r'when \(Sink\.Test\(l,'),
    ('an expression tree', r'Expression<'),
    ('nested two deep', None),
    ('a nested function inside a try', None),
    ('a nested function inside a catch, finally or filter', None),
    ('a proof by ThrowIfNullRef', r'ThrowIfNullRef\(in (c|this)\)'),
    ('a proof by a store or a stored read of c', r'(?<![\w.])c = 0;|NI = c;|(?<![\w.])c\+\+;'),
    ('a commit followed by a use of c', r'= g\d*;\s*$'),
]


def methods(path):
    text = open(path, encoding='utf-8').read().splitlines()
    cur, body = None, []
    for i, t in enumerate(text, 1):
        m = re.search(r'// CASE (\S+)', t)
        if m or '// END' in t:
            if cur:
                yield cur, start, body
            cur, body, start = (m.group(1) if m else None), [], i
        elif cur:
            body.append((i, t))


total = acc = 0
counts = {n: 0 for n, _ in FEATS}
for pattern in sys.argv[1:]:
    for d in glob.glob(pattern):
        log = open(os.path.join(d, 'build.log'), encoding='utf-8', errors='replace').read()
        reported = {}
        for m in re.finditer(r'(\w+\.cs)\((\d+),\d+\): warning SCA', log):
            reported.setdefault(m.group(1), set()).add(int(m.group(2)))
        for f in glob.glob(os.path.join(d, 'Z*.cs')):
            name = os.path.basename(f)
            lines = reported.get(name, set())
            for cid, start, body in methods(f):
                total += 1
                if any(i in lines for i, _ in body):
                    continue
                acc += 1
                text = '\n'.join(t for _, t in body)
                for n, rx in FEATS:
                    if rx and re.search(rx, text, re.M):
                        counts[n] += 1
                depth, best, intry, inhandler, stack = 0, 0, False, False, []
                for _, t in body:
                    s = t.strip()
                    if re.search(r'Inner\d+(\(| =)', s) and ('(' in s) and not s.startswith(('Guard r', 'Sink.Use', 'foreach', '{ using', '{ var')) and 'Inner' in s.split('(')[0]:
                        best = max(best, t.count('    ') // 1)
                if re.search(r'Inner\d+[\s\S]*?\{[\s\S]*?Inner\d+', text):
                    pass
                ind = [len(t) - len(t.lstrip()) for _, t in body if re.search(r'(GFn|AFn|MFn|RFn) Inner\d+ =|(Guard|void|Task<int>|IEnumerable<int>) Inner\d+\(', t)]
                if len(ind) >= 2 and max(ind) - min(ind) >= 8:
                    counts['nested two deep'] += 1
                # inside a try: a nested function declared after 'try' and before its closing at a deeper indentation
                tries = [len(t) - len(t.lstrip()) for _, t in body if t.strip() in ('try',)]
                if ind and tries and max(ind) > min(tries):
                    counts['a nested function inside a try'] += 1
                if re.search(r'(catch|finally)[^\n]*\n\s*\{[^}]*?(GFn|AFn|MFn|RFn|Guard|void|IEnumerable<int>|Task<int>) Inner\d+', text) or 'when (Sink.Test(l,' in text:
                    counts['a nested function inside a catch, finally or filter'] += 1
print(f'{total} methods, {acc} accepted. Accepted methods with:')
for n, _ in FEATS:
    print(f'  {counts[n]:7d}  {n}')
