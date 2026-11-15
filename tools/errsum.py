#!/usr/bin/env python3
"""Summarize Avalonia/dotnet build errors: one line per distinct (file, code, message)."""
import re, sys, collections

pat = re.compile(r'^(?P<file>[^(]+)\((?P<line>\d+)(?:,\d+){1,3}\): (?P<kind>[A-Za-z ]*?) ?error (?P<code>(AVLN|CS)\d+): (?P<msg>.*?)(?: Line \d+, position \d+\.)?(?: \[/.*)?$')
groups = collections.Counter()
first = {}
for path in sys.argv[1:]:
    for line in open(path, errors='replace'):
        m = pat.match(line.strip())
        if not m:
            continue
        f = m['file'].split('/')[-1]
        msg = re.sub(r'\s+', ' ', m['msg'])
        key = (f, m['code'], msg)
        groups[key] += 1
        first.setdefault(key, m['line'])
for (f, code, msg), n in sorted(groups.items(), key=lambda t: (t[0][0], int(first[t[0]]))):
    print(f'{f}:{first[(f, code, msg)]}  {code} x{n}  {msg[:170]}')
total = sum(groups.values())
print(f'--- {total} error lines, {len(groups)} distinct')
