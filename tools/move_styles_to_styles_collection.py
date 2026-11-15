#!/usr/bin/env python3
"""Move <Style>/<StyleInclude> children out of <X.Resources> into <X.Styles>.

Avalonia keeps styles in a Styles collection; WPF kept them in Resources.  The generated
axaml still has them under Resources, so the XAML compiler rejects them.  This moves the
style elements (and only those) to the file's Styles section, leaving every other resource
(brushes, sizes, drawings) where it is.
"""
import re, sys, glob

MOVE = ('Style', 'StyleInclude')
INDENT = '    '


def match_close(text, lt):
    """Index just past the tag that closes the element opening at text[lt]."""
    depth, pos = 1, text.index('>', lt) + 1
    while pos < len(text):
        nxt = text.find('<', pos)
        if nxt < 0:
            raise ValueError('unbalanced markup')
        if text.startswith('<!--', nxt):
            pos = text.index('-->', nxt) + 3
            continue
        gt = text.index('>', nxt)
        if text.startswith('</', nxt):
            depth -= 1
            pos = gt + 1
            if depth == 0:
                return pos
        elif text[gt - 1] != '/':
            depth += 1
            pos = gt + 1
        else:
            pos = gt + 1
    raise ValueError('unbalanced markup')


def split_top(body):
    """Split one element body into (move?, text) segments covering the whole body.

    A comment sitting directly above a style belongs with that style, so it travels with it."""
    segs, i, n = [], 0, len(body)
    while i < n:
        lt = body.find('<', i)
        if lt < 0:
            segs.append((False, body[i:]))
            break
        if lt > i:
            segs.append((False, body[i:lt]))
        if body.startswith('<!--', lt):
            end = body.index('-->', lt) + 3
        elif body.startswith('</', lt):
            end = body.index('>', lt) + 1
        else:
            gt = body.index('>', lt)
            name = re.match(r'[^\s/>]+', body[lt + 1:gt]).group(0)
            end = gt + 1 if body[gt - 1] == '/' else match_close(body, lt)
            move = name in MOVE
            segs.append((move, body[lt:end]))
            i = end
            continue
        segs.append((False, body[lt:end]))
        i = end
    merged = []
    for move, text in segs:
        if move and merged and not merged[-1][0]:
            gap = merged[-1][1]
            trail = re.search(r'((?:<!--[\s\S]*?-->[ \t]*)+)$', gap)
            if trail:
                merged[-1] = (False, gap[:trail.start()])
                text = gap[trail.start():].rstrip('\n') + '\n' + INDENT + text
        merged.append((move, text))
    return merged


def indented(segments):
    """Re-join moved elements, putting each one's opening tag back at the body indent."""
    out = []
    for seg in segments:
        lines = seg.rstrip().split('\n')
        out.append(INDENT + '\n'.join(lines))
    return '\n'.join(out).strip('\n')


def fix(text):
    for m in re.finditer(r'<([A-Za-z][\w:]*)\.Resources\s*>', text):
        owner, open_end = m.group(1), m.end()
        closing = f'</{owner}.Resources>'
        close = text.find(closing, open_end)
        if close < 0:
            continue
        body, end = text[open_end:close], close + len(closing)
        segs = split_top(body)
        moved = [s for move, s in segs if move]
        if not moved:
            continue
        keep = re.sub(r'\n[ \t]*\n+', '\n', ''.join(s for move, s in segs if not move)).strip('\n')
        styles = indented(moved)
        existing = re.search(rf'<{owner}\.Styles\s*>', text)
        if keep.strip():
            replacement = f'<{owner}.Resources>\n{keep}\n</{owner}.Resources>'
        elif existing:
            replacement = ''
        else:
            replacement = f'<{owner}.Styles>\n{styles}\n</{owner}.Styles>'
        text = text[:m.start()] + replacement + text[end:]
        if existing:
            at = text.index(f'</{owner}.Styles>')
            text = text[:at] + styles + '\n  ' + text[at:]
        elif keep.strip():
            at = m.start() + len(replacement)
            text = text[:at] + f'\n  <{owner}.Styles>\n{styles}\n</{owner}.Styles>' + text[at:]
        return text                       # offsets moved: run() rescans
    return text


def run(text):
    while True:
        new = fix(text)
        if new == text:
            return new
        text = new


for path in sys.argv[1:] or glob.glob('*.axaml'):
    src = open(path).read()
    new = run(src)
    if new != src:
        open(path, 'w').write(new)
        print('rewrote', path)
