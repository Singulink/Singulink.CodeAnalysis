# usage: python verdict.py OUTDIR  -> per case: reported codes, run result; prints accepted cases that released twice and a summary
import re, sys, glob, os
d = sys.argv[1]
case_at = {}
for f in glob.glob(d + '/*.cs'):
    cur = None
    name = os.path.basename(f)
    for i, t in enumerate(open(f, encoding='utf-8').read().splitlines(), 1):
        m = re.search(r'// CASE (\S+)', t)
        if m: cur = m.group(1)
        if '// END' in t: cur = None
        case_at[(name, i)] = cur
rep = {}
for t in open(d + '/diags.txt', encoding='utf-8'):
    m = re.match(r'(\w+\.cs)\((\d+),\d+\): warning (SCA\d+)', t)
    if m:
        c = case_at.get((m.group(1), int(m.group(2))))
        if c: rep.setdefault(c, set()).add(m.group(3))
runs = {}
for t in open(d + '/run.txt', encoding='utf-8'):
    p = t.strip().split('|')
    if len(p) == 3: runs[p[0]] = (p[1], p[2])
acc = [c for c in runs if c not in rep]
miss = [c for c in acc if 'TWICE' in runs[c][1]]
print(f'{d}: {len(runs)} cases run, {len(acc)} accepted, {sum("TWICE" in r[1] for r in runs.values())} release twice, MISSES: {miss}')
if len(sys.argv) > 2:
    for c in sorted(runs):
        print(f'  {c:8} {",".join(sorted(rep.get(c, []))) or "-":18} {runs[c][0]:24} {runs[c][1]}')
