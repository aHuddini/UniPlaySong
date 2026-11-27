"""Rebuilds FEATURE_IDEAS.html from FEATURE_IDEAS.md.

The page's <head> (styles) and its tail (toolbar, toast, script) are kept from the existing HTML; only the body
between them is regenerated, so re-running is safe. Run after editing the .md:

    python -I docs/dev_docs/roadmaps/build_feature_ideas_html.py
"""
import html
import os
import re

HERE = os.path.dirname(os.path.abspath(__file__))
MD = os.path.join(HERE, 'FEATURE_IDEAS.md')
OUT = os.path.join(HERE, 'FEATURE_IDEAS.html')
TAIL_MARK = '<!-- Floating toolbar -->'

ICONS = {
    'Dashboard & Statistics': '&#128202;', 'Playback & Listening Experience': '&#127925;',
    'Discovery & Smart Features': '&#128269;', 'Playnite-Aware Playback': '&#127918;', 'Audio & Effects': '&#127911;',
    'vgmstream Integration Roadmap': '&#128190;', 'Integration & Streaming': '&#128279;', 'Visual & UI': '&#127912;',
    'Library Management': '&#128218;', 'Social & Community': '&#128101;', 'Gamification & Engagement': '&#127942;',
    'v1.4+ Opportunities Identified': '&#128640;', 'v1.5+ Opportunities Identified': '&#10024;',
    'Atmosphere & Immersion': '&#127756;', 'Experimental / Labs': '&#129514;', 'Architecture Ideas': '&#127959;',
}
PRIORITY_CLASS = {'Quick Wins': 'quick-wins', 'High-Value': 'high-value', 'Ambitious': 'ambitious'}
SUB_H3 = 'style="color: var(--text-dim); font-size: 1.1rem; margin: 1.5rem 0 0.5rem 0;"'


def inline(text):
    """Markdown inline -> HTML: code, strike, bold, italics, links; <br> kept."""
    codes = []

    def keep_code(m):
        codes.append('<code>' + html.escape(m.group(1)) + '</code>')
        return '\x00%d\x00' % (len(codes) - 1)

    t = re.sub(r'`([^`]+)`', keep_code, text)
    t = html.escape(t, quote=False).replace('&lt;br&gt;', '<br>').replace('&lt;br/&gt;', '<br>')
    t = re.sub(r'\[([^\]]+)\]\(([^)]+)\)', r'<a href="\2">\1</a>', t)
    t = re.sub(r'~~(.+?)~~', r'<s>\1</s>', t)
    t = re.sub(r'\*\*(.+?)\*\*', r'<strong>\1</strong>', t)
    t = re.sub(r'(?<![\w*])\*([^*\s][^*]*?)\*(?![\w*])', r'<em>\1</em>', t)
    return re.sub('\x00(\\d+)\x00', lambda m: codes[int(m.group(1))], t)


def cells(line):
    # Split a table row on pipes that are not inside backticks
    out, cur, in_code = [], '', False
    for ch in line.strip().strip('|'):
        if ch == '`':
            in_code = not in_code
        if ch == '|' and not in_code:
            out.append(cur.strip())
            cur = ''
        else:
            cur += ch
    out.append(cur.strip())
    return out


def level_badge(text):
    word = re.sub(r'[^a-z]', ' ', text.lower()).split()
    cls = next((w for w in word if w in ('low', 'medium', 'high')), 'medium')
    return '<span class="badge badge-%s">%s</span>' % (cls, html.escape(text.strip() or '?'))


def plain(text):
    return re.sub(r'[~*`✅🎯]', '', text).strip()


class Page:
    def __init__(self):
        self.out = []
        self.stats = {'ideas': 0, 'shipped': 0, 'quick': 0, 'high': 0}

    def feature_table(self, category, header, rows):
        o = self.out
        o.append('<table data-category="%s">' % html.escape(category))
        o.append('<thead><tr><th style="width:36px"></th><th>Feature</th><th>Description</th>'
                 '<th class="sortable" data-sort="effort">Effort</th><th class="sortable" data-sort="impact">Impact</th></tr></thead>')
        o.append('<tbody>')
        for r in rows:
            r = (r + ['', '', '', ''])[:4]
            name, desc, effort, impact = r
            shipped = name.startswith('~~') or '✅' in name
            self.stats['ideas'] += 1
            badge = ''
            if shipped:
                m = re.search(r'Shipped (v[\d.]+[^*\s,;)]*)', desc)
                badge = ' <span class="badge badge-shipped">%s</span>' % ('Shipped ' + m.group(1) if m else 'Shipped')
            name_html = inline(name.replace('✅', '').strip())
            cb = '<input type="checkbox" checked disabled>' if shipped else '<input type="checkbox">'
            o.append('<tr%s><td class="cb-cell">%s</td><td class="feature-name">%s%s</td><td class="feature-desc">%s</td>'
                     '<td class="feature-effort">%s</td><td class="feature-impact">%s</td></tr>'
                     % (' class="shipped"' if shipped else '', cb, name_html, badge, inline(desc),
                        level_badge(effort), level_badge(impact)))
        o.append('</tbody>')
        o.append('</table>')

    def plain_table(self, header, rows):
        o = self.out
        o.append('<table>')
        o.append('<thead><tr>' + ''.join('<th>%s</th>' % inline(h) for h in header) + '</tr></thead>')
        o.append('<tbody>')
        for r in rows:
            o.append('<tr>' + ''.join('<td class="feature-desc">%s</td>' % inline(c) for c in r) + '</tr>')
        o.append('</tbody>')
        o.append('</table>')


def blocks(lines):
    """Groups lines into (kind, payload): table, ol, ul, quote, code, p."""
    i = 0
    while i < len(lines):
        line = lines[i]
        s = line.strip()
        if not s or s == '---':
            i += 1
            continue
        if s.startswith('```'):
            j = i + 1
            while j < len(lines) and not lines[j].strip().startswith('```'):
                j += 1
            yield 'code', lines[i + 1:j]
            i = j + 1
            continue
        if s.startswith('|'):
            j = i
            while j < len(lines) and lines[j].strip().startswith('|'):
                j += 1
            rows = [cells(l) for l in lines[i:j]]
            rows = [r for r in rows if not all(re.fullmatch(r':?-+:?', c) for c in r if c)]
            yield 'table', rows
            i = j
            continue
        for kind, pat in (('ol', r'\d+\.\s'), ('ul', r'[-*]\s')):
            if re.match(pat, s):
                items = []
                while i < len(lines) and lines[i].strip():
                    t = lines[i].strip()
                    if re.match(pat, t):
                        items.append(re.sub('^' + pat, '', t))
                    elif items:
                        items[-1] += ' ' + t
                    i += 1
                yield kind, items
                break
        else:
            if s.startswith('>'):
                q = []
                while i < len(lines) and lines[i].strip().startswith('>'):
                    q.append(lines[i].strip().lstrip('>').strip())
                    i += 1
                yield 'quote', ' '.join(q)
                continue
            p = []
            while i < len(lines) and lines[i].strip() and not re.match(r'(\||#|>|```|\d+\.\s|[-*]\s)', lines[i].strip()):
                p.append(lines[i].strip())
                i += 1
            if p:
                yield 'p', ' '.join(p)
            else:
                i += 1


def render_blocks(page, lines, category, priority=''):
    o = page.out
    for kind, data in blocks(lines):
        if kind == 'table':
            header, rows = data[0], data[1:]
            if [h.lower() for h in header[:4]] == ['feature', 'description', 'effort', 'impact']:
                page.feature_table(category, header, rows)
            else:
                page.plain_table(header, rows)
        elif kind in ('ol', 'ul'):
            left = sum(1 for it in data if not it.startswith('~~'))
            if 'Quick Wins' in priority:
                page.stats['quick'] += left
            elif 'High-Value' in priority:
                page.stats['high'] += left
            o.append('<%s>' % kind)
            o.extend('<li>%s</li>' % inline(it) for it in data)
            o.append('</%s>' % kind)
        elif kind == 'quote':
            o.append('<p style="margin:0 0 10px;font-size:13px;opacity:.8;">%s</p>' % inline(data))
        elif kind == 'code':
            o.append('<pre><code>%s</code></pre>' % html.escape('\n'.join(data)))
        else:
            o.append('<p style="color:var(--text-dim);margin:0 0 12px;">%s</p>' % inline(data))


def split_sections(text):
    """[(level, title, body_lines)] for ## and ### headings."""
    out, cur = [], (1, None, [])
    for line in text.splitlines():
        m = re.match(r'^(#{2,3})\s+(.*)$', line)
        if m:
            out.append(cur)
            cur = (len(m.group(1)), m.group(2).strip(), [])
        else:
            cur[2].append(line)
    out.append(cur)
    return out


def build():
    md = open(MD, encoding='utf-8-sig').read()
    old = open(OUT, encoding='utf-8').read()
    head = old[:old.index('<body>') + len('<body>')]
    tail = old[old.index(TAIL_MARK):]

    sections = split_sections(md)
    intro_lines = sections[0][2]
    title = next((l.lstrip('# ').strip() for l in intro_lines if l.startswith('# ')), 'UniPlaySong Feature Ideas')
    intro = next((l.strip() for l in intro_lines if l.strip() and not l.startswith('#') and not l.startswith('**Legend')), '')

    page = Page()
    body = page.out
    shipped, rest = [], []
    h2 = None
    for level, name, lines in sections[1:]:
        if level == 2:
            h2 = name
        (shipped if (h2 or '').startswith('Shipped in') else rest).append((level, name, lines))

    # Shipped chips
    chips = []
    for level, name, lines in shipped:
        items = []
        for kind, data in blocks(lines):
            if kind == 'table':
                for r in data[1:]:
                    feat = plain(r[0])
                    ver = plain(r[-1]) if len(r) > 1 else ''
                    page.stats['shipped'] += 1
                    items.append('    <span class="shipped-chip">%s <span class="version">%s</span></span>'
                                 % (inline(feat), html.escape(ver)))
        chips.append('<div class="shipped-section">\n  <h3>&#10003; %s</h3>\n  <div class="shipped-list">\n%s\n  </div>\n</div>\n'
                     % (html.escape(name), '\n'.join(items)))

    # Categories and the rest
    in_priority = False
    open_priority = False
    pending = None  # (body index, icon, name, ideas before) of the open category heading

    def close_heading():
        if pending is None:
            return
        idx, icon, nm, before = pending
        n = page.stats['ideas'] - before
        slug = re.sub(r'[^a-z0-9]+', '-', nm.lower()).strip('-')
        body[idx] = ('<h2 id="%s"><span class="category-icon">%s</span> %s <span class="count">%s</span></h2>'
                     % (slug, icon, html.escape(nm), '(%d ideas)' % n if n else ''))

    for level, name, lines in rest:
        if level == 2:
            close_heading()
            pending = None
            if open_priority:
                body.append('</div>')
                open_priority = False
            in_priority = name.startswith('Priority Recommendations')
            body.append('\n<!-- ============================================ -->')
            if in_priority:
                body.append('<h2 style="border-bottom-color: var(--low);">&#9733; %s</h2>' % html.escape(name))
            else:
                pending = (len(body), ICONS.get(name, '&#128161;'), name, page.stats['ideas'])
                body.append(None)  # heading placeholder, filled once its ideas are counted
                render_blocks(page, lines, name)
                h2 = name
                continue
            render_blocks(page, lines, name)
            h2 = name
        else:
            if in_priority:
                if open_priority:
                    body.append('</div>')
                cls = next((c for k, c in PRIORITY_CLASS.items() if k in name), '')
                body.append('<div class="priority-section %s">' % cls)
                open_priority = True
                body.append('<h3>%s</h3>' % inline(name))
            else:
                body.append('<h3 %s>%s</h3>' % (SUB_H3, inline(name)))
            render_blocks(page, lines, h2, priority=name if in_priority else '')
    close_heading()
    if open_priority:
        body.append('</div>')

    st = page.stats
    top = ['', '<h1>%s</h1>' % html.escape(title),
           '<p class="subtitle">%s Check features you want to explore, then copy your selection to clipboard.</p>' % inline(intro),
           '', '<div class="stats">',
           '  <div class="stat-card"><div class="number">%d</div><div class="label">Ideas Tracked</div></div>' % st['ideas'],
           '  <div class="stat-card"><div class="number" style="color: var(--low);">%d</div><div class="label">Shipped</div></div>' % st['shipped'],
           '  <div class="stat-card"><div class="number">%d</div><div class="label">Quick Wins Left</div></div>' % st['quick'],
           '  <div class="stat-card"><div class="number">%d</div><div class="label">High Value</div></div>' % st['high'],
           '</div>', '']
    doc = head + '\n'.join(top) + '\n' + ''.join(chips) + '\n'.join(b for b in body if b is not None) + '\n\n' + tail
    with open(OUT, 'w', encoding='utf-8', newline='\n') as f:
        f.write(doc)
    print('wrote %s: %d ideas, %d shipped, %d quick wins left, %d high impact'
          % (OUT, st['ideas'], st['shipped'], st['quick'], st['high']))


if __name__ == '__main__':
    build()
