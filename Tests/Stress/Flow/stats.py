# usage: python stats.py DIR...   -> what the accepted methods of the given batch folders contain
import glob, os, re, sys
feats = [
    ('a by-value read into a call that releases', r'Sink\.Take\w*\((?!ref )'),
    ('a store into a field or an element', r'\b(F1|F2|FN|FT|FA\[\w[^\]]*\]|Holder\.G|FT\.G|FK|FV|FU) = (?!default|null|l\.)'),
    ('a use in place', r'\.Dispose\(\);|Sink\.(Release|Touch|ReleaseN|ReleasePair)\(ref'),
    ('a reference taken (ref local, span, lambda)', r'ref Guard r\d+ = ref|Sink\.SpanOf|Sink\.Same|Action f\d+ = \(\) =>'),
    ('a try', r'\btry\b'), ('a catch', r'\bcatch\b'), ('a finally', r'\bfinally\b'), ('an exception filter', r'when \('),
    ('two or more try statements', None), ('a loop', r'\b(for|foreach|while|do)\b'), ('a goto', r'\bgoto [LB]'), ('a switch', r'\bswitch\b'),
    ('a return of a guard', r'return [gptm]\w*'), ('using or lock', r'\busing \(|\block \(|using var'),
    ('a nullable guard', r'Guard\? m\d+'), ('a tuple with a guard', r'\(Guard G, int N\) t\d+'), ('a task of a guard', r'Task<Guard> k\d+'), ('a list of guards', r'List<Guard> v\d+'),
    ('a read of a result or an element by value', r'k\d+\.Result|v\d+\[(0|\^1)\]|u\d+\.Result|in v\d+\)'),
    ('a commit followed by another operation in the try', None),
    ('a nested local function', r'Guard Inner\d+\('), ('an await', r'\bawait\b'), ('a value task of a guard', r'ValueTask<Guard> u\d+'),
]
total = acc = 0
counts = {name: 0 for name, _ in feats}
for d in sys.argv[1:]:
    log = open(os.path.join(d, 'build.log'), encoding='utf-8', errors='replace').read()
    rep = {}
    for m in re.finditer(r'(Z\w+)\.cs\((\d+),\d+\): warning SCA', log):
        rep.setdefault(m.group(1), set()).add(int(m.group(2)))
    for f in glob.glob(os.path.join(d, 'Z*.cs')):
        name = os.path.basename(f)[:-3]
        lines = open(f, encoding='utf-8').read().splitlines()
        starts = [i for i, t in enumerate(lines) if '// CASE ' in t] + [next(i for i, t in enumerate(lines) if '// END' in t)]
        bad = rep.get(name, set())
        for a, b in zip(starts, starts[1:]):
            total += 1
            if any((i + 1) in bad for i in range(a, b)):
                continue
            acc += 1
            text = '\n'.join(lines[a:b])
            for fname, rx in feats:
                if rx is not None:
                    counts[fname] += bool(re.search(rx, text))
            counts['two or more try statements'] += len(re.findall(r'\btry\b', text)) >= 2
            counts['a commit followed by another operation in the try'] += bool(re.search(r'\b(F1|F2|FN|FT\.G|FA\[0\]|Holder\.G) = g\d+;\n\s+(N|B|S|O|F2|FN)\b[^\n]*\n\s+\}\n\s+catch', text))
print(f'{total} methods, {acc} accepted')
for fname, _ in feats:
    print(f'{counts[fname]:7d}  {fname}')
