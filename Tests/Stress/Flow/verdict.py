# usage: python verdict.py build.log run.txt label FILE.cs...
# A method starts at '// CASE id'. The run prints 'id|TWICE ...' for a method that released one acquisition twice under some input.
# A method with no SCA diagnostic that is printed is a miss.
import re
import sys

log, run, label = sys.argv[1:4]
files = sys.argv[4:]
case_at = {}
order = []

for f in files:
    cur = None
    name = f.replace('\\', '/').split('/')[-1]
    for i, t in enumerate(open(f, encoding='utf-8-sig').read().splitlines(), 1):
        m = re.search(r'// CASE (\S+)', t)
        if m:
            cur = m.group(1)
            order.append(cur)
        if '// END' in t:
            cur = None
        case_at[(name, i)] = cur

reported = {}
outside = 0

for line in open(log, encoding='utf-8', errors='replace'):
    m = re.search(r'([\w.]+\.cs)\((\d+),(\d+)\): warning (SCA\d+)', line)
    if m:
        c = case_at.get((m.group(1), int(m.group(2))))
        if c is None:
            outside += 1
        else:
            reported.setdefault(c, set()).add(m.group(4))

twice = {}
other = []

try:
    for line in open(run, encoding='utf-8', errors='replace'):
        m = re.match(r'(\S+?)\|(.*)$', line.strip())
        if m:
            twice[m.group(1)] = m.group(2)
        elif line.strip():
            other.append(line.strip())
except FileNotFoundError:
    other.append('NO RUN')

clean = [c for c in order if c not in reported]
misses = [c for c in clean if c in twice]

for c in misses[:40]:
    print('MISS', c, twice[c])

codes = {}
for c, s in reported.items():
    for code in s:
        codes[code] = codes.get(code, 0) + 1

print(f'== {label}: {len(order)} methods, {len(clean)} accepted, {len(twice)} with a double release at run time, {len(misses)} MISSES; '
      f'diagnostics outside methods: {outside}; other output: {other[:3]}')
print('   reported by rule:', dict(sorted(codes.items())))
