"""Finish a demo capture run: cross-links, termbase, discovery and site registries.

Run after Generate-Help.ps1 (or after all disjoint -Locale groups complete).
Only generated help pages and the documented help entries of shared registries are rewritten.
"""
import hashlib
import html
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
HELP = ROOT / 'docs/help'
BASE = 'https://serzhyale.github.io/CyrFlip/'
deck = json.loads((ROOT / 'tools/help/strings.json').read_text(encoding='utf-8'))
locales = list(deck['Help'])
inventory = json.loads((ROOT / 'help-map.json').read_text(encoding='utf-8-sig'))
rows = {row['id']: row for row in inventory['surfaces']}
titles = {}
captures = []
for locale in locales:
    localized = json.loads((HELP / locale / 'titles.json').read_text(encoding='utf-8'))
    for key, value in localized.items():
        titles.setdefault(key, {})[locale] = value
    capture = json.loads((HELP / locale / 'capture-info.json').read_text(encoding='utf-8'))
    captures.extend(capture['captures'])

relationships = {
    'settings.General': ['settings-form--settings-search-dialog', 'settings-transfer-dialog', 'backup-restore-dialog', 'welcome.0'],
    'settings.WindowsLanguages': ['layout-picker-dialog', 'hotkey-dialog'],
    'settings.Conversions': ['layout-conversion-dialog', 'hotkey-dialog'],
    'settings.Symbols': ['symbols-window'],
    'settings.Clipboard': ['clipboard-history-window', 'clipboard-history-search-window', 'exchange-export-dialog'],
    'clipboard-history-window': ['clipboard-history-search-window', 'settings.Hotkeys'],
    'settings.QuickNotes': ['quick-notes-window', 'exchange-export-dialog'],
    'settings.Translate': ['translation-dialog', 'translation-result-window', 'hotkey-dialog'],
    'settings.Launcher': ['launcher-scenario-dialog', 'yt-dlp-link-dialog', 'hotkey-dialog'],
    'settings.TrafficLight': ['traffic-light-window', 'traffic-light-quick-add-dialog', 'traffic-light-target-dialog'],
    'traffic-light-window': ['traffic-light-quick-add-dialog', 'traffic-light-target-dialog'],
    'settings.ExchangeServer': ['exchange-account-dialog'],
    'settings.About': ['support-bundle-dialog', 'welcome.0'],
    'settings-form': ['settings.General', 'settings.Hotkeys', 'settings.About'],
    'welcome-dialog': ['welcome.0'],
}
for step in range(5):
    relationships['welcome.' + str(step)] = ['settings.General'] + (['welcome.' + str(step + 1)] if step < 4 else [])
edges = {key: set() for key in rows}
for parent, children in relationships.items():
    for child in children:
        edges[parent].add(child)
        edges[child].add(parent)

purpose_pages = {
    'clipboard-history-window': 'settings.Clipboard', 'clipboard-history-search-window': 'settings.Clipboard',
    'quick-notes-window': 'settings.QuickNotes', 'symbols-window': 'settings.Symbols',
    'traffic-light-window': 'settings.TrafficLight', 'translation-result-window': 'settings.Translate',
    'launcher-scenario-dialog': 'settings.Launcher', 'exchange-account-dialog': 'settings.ExchangeServer',
}

for surface in inventory['surfaces']:
    if surface['id'] in titles:
        surface['name'] = titles[surface['id']]['en']
    if edges[surface['id']]:
        surface['related'] = sorted(edges[surface['id']])
    if surface['form'] != 'page':
        continue
    for locale in locales:
        path = HELP / locale / (surface['help'] + '.html')
        page = path.read_text(encoding='utf-8')
        if surface['id'] == 'index':
            # Settings follow the existing pillar order; other windows follow their map order.
            pages = [r for r in inventory['surfaces'] if r['form'] == 'page' and r['id'] != 'index']
            pages.sort(key=lambda r: (0 if r['id'].startswith('settings.') else 1))
            links = ''.join('<li><a href="' + r['help'] + '.html">' + html.escape(titles[r['id']][locale]) + '</a></li>' for r in pages)
            page = re.sub(r'<ul>.*?</ul>', '<ul>' + links + '</ul>', page, flags=re.S)
        else:
            links = ['<a href="index.html">' + html.escape(deck['Help'][locale]) + '</a>']
            for related in sorted(edges[surface['id']]):
                target = rows[related]
                if target['form'] == 'page':
                    links.append('<a href="' + target['help'] + '.html">' + html.escape(titles[related][locale]) + '</a>')
            guide = BASE + (locale + '/' if locale in ('ru', 'uk') else '') + 'guide.html'
            links += ['<a href="settings-hotkeys.html">' + html.escape(deck['Your shortcuts'][locale]) + '</a>',
                      '<a href="' + guide + '">' + html.escape(deck['Guide'][locale]) + '</a>',
                      '<a href="https://sza.od.ua/">SZA</a>']
            if surface['id'] in ('settings.Launcher', 'launcher-scenario-dialog', 'yt-dlp-link-dialog'):
                links.append('<a href="https://serzhyale.github.io/OneClickRunner/">OneClickRunner</a>')
            if surface['id'] in ('settings.ExchangeServer', 'exchange-account-dialog'):
                links += ['<a href="https://serzhyale.github.io/FastMediaSorter_mob_v2/">FastMediaSorter Android</a>',
                          '<a href="https://serzhyale.github.io/FastMediaSorter_Lite/">FastMediaSorter Windows</a>']
            related = '<nav id="related"><h2>' + html.escape(deck['Related'][locale]) + '</h2>' + ' · '.join(links) + '</nav>'
            page = re.sub(r'<nav id="related">.*?</nav>', lambda _: related, page, flags=re.S)
            if surface['id'] in purpose_pages:
                parent = rows[purpose_pages[surface['id']]]
                parent_html = (HELP / locale / (parent['help'] + '.html')).read_text(encoding='utf-8')
                purpose = re.search(r'<section id="purpose">.*?<p>(.*?)</p>', parent_html, re.S).group(1)
                page = re.sub(r'(<section id="purpose">.*?<p>).*?(</p>)', lambda m: m[1] + purpose + m[2], page, count=1, flags=re.S)
        path.write_text(page, encoding='utf-8')

(ROOT / 'help-map.json').write_text(json.dumps(inventory, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
(HELP / 'titles.json').write_text(json.dumps(titles, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
(HELP / 'captures.json').write_text(json.dumps({'generator': 'tools/help/Generate-Help.ps1', 'captures': captures}, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
termbase_path = ROOT / 'docs/termbase.json'
termbase = json.loads(termbase_path.read_text(encoding='utf-8-sig'))
termbase['helpSurfaces'] = titles
termbase_path.write_text(json.dumps(termbase, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')

# Index links on each existing landing/guide surface. Keep the existing page body intact.
for locale in locales:
    if locale == 'en':
        continue
    path = ROOT / 'docs' / locale / 'index.html'
    if not path.exists():  # RU/UK share the trilingual root page.
        continue
    page = path.read_text(encoding='utf-8-sig')
    snippet = '<p class="context-help-link"><a href="../help/' + locale + '/index.html">' + html.escape(deck['Help'][locale]) + '</a></p>'
    page = re.sub(r'<p class="context-help-link">.*?</p>', '', page, flags=re.S)
    page = page.replace('</main>', snippet + '\n</main>', 1)
    path.write_text(page, encoding='utf-8')
path = ROOT / 'docs/index.html'
page = path.read_text(encoding='utf-8-sig')
page = re.sub(r'<p class="context-help-link">.*?</p>', '', page, flags=re.S)
snippet = '<p class="context-help-link">' + ''.join('<a data-l="' + ('ua' if code == 'uk' else code) + '" href="help/' + code + '/index.html">' + html.escape(deck['Help'][code]) + '</a>' for code in ('en', 'ru', 'uk')) + '</p>'
page = page.replace('</main>', snippet + '\n</main>', 1)
path.write_text(page, encoding='utf-8')
for locale in ('en', 'ru', 'uk'):
    path = ROOT / 'docs' / ('' if locale == 'en' else locale) / 'guide.html'
    page = path.read_text(encoding='utf-8-sig')
    page = re.sub(r'<p class="context-help-link">.*?</p>', '', page, flags=re.S)
    target = ('help/' if locale == 'en' else '../help/') + locale + '/index.html'
    page = page.replace('</main>', '<p class="context-help-link"><a href="' + target + '">' + html.escape(deck['Help'][locale]) + '</a></p>\n</main>', 1)
    path.write_text(page, encoding='utf-8')

# The translation-freshness meta (DocumentationQualityTests) follows the source bodies these edits change.
def refresh_source_digest(source, translations):
    body = re.search(r'<body\b[^>]*>(.*)</body>', source.read_text(encoding='utf-8-sig'), re.S | re.I).group(1)
    body = re.split(r'<footer\b', body, flags=re.I)[0].replace('\r\n', '\n')
    digest = hashlib.sha256(body.encode('utf-8')).hexdigest()
    for path in translations:
        if not path.exists():
            continue
        page = path.read_text(encoding='utf-8-sig')
        page, count = re.subn(r'(name="source-sha256" content=")[0-9a-f]+(")', lambda m: m.group(1) + digest + m.group(2), page, count=1)
        if count:
            path.write_text(page, encoding='utf-8')

refresh_source_digest(ROOT / 'docs/index.html', [ROOT / 'docs' / locale / 'index.html' for locale in locales if locale != 'en'])
refresh_source_digest(ROOT / 'docs/guide.html', [ROOT / 'docs' / locale / 'guide.html' for locale in ('ru', 'uk')])

for locale, filename in [('en', 'README.md'), ('ru', 'README_RU.md'), ('uk', 'README_UK.md')]:
    path = ROOT / filename
    page = path.read_text(encoding='utf-8-sig')
    page = re.sub(r'\n<!-- S0075 -->.*?<!-- /S0075 -->\n', '\n', page, flags=re.S)
    page += '\n<!-- S0075 -->\n' + deck['Help is available offline. Press F1 in a CyrFlip window or use its help button.'][locale] + ' [' + deck['Help'][locale] + '](' + BASE + 'help/' + locale + '/index.html).\n<!-- /S0075 -->\n'
    path.write_text(page, encoding='utf-8')

# Published addresses and search entries are generated from the same page list.
all_pages = sorted(p.relative_to(ROOT / 'docs').as_posix() for p in HELP.rglob('*.html'))
sitemap_path = ROOT / 'docs/sitemap.xml'
sitemap = sitemap_path.read_text(encoding='utf-8-sig')
sitemap = re.sub(r'\s*<url><loc>' + re.escape(BASE) + r'help/.*?</loc></url>', '', sitemap)
sitemap = sitemap.replace('</urlset>', ''.join('  <url><loc>' + BASE + page + '</loc></url>\n' for page in all_pages) + '</urlset>')
sitemap_path.write_text(sitemap, encoding='utf-8')
search_path = ROOT / 'docs/search-index.json'
search = [row for row in json.loads(search_path.read_text(encoding='utf-8-sig')) if '/help/' not in row['url']]
for page in all_pages:
    locale = page.split('/')[1]
    text = (ROOT / 'docs' / page).read_text(encoding='utf-8')
    title = html.unescape(re.search(r'<h1>(.*?)</h1>', text).group(1))
    search.append({'url': BASE + page, 'lang': locale, 'title': title, 'description': deck['Help is available offline. Press F1 in a CyrFlip window or use its help button.'][locale], 'headings': [title]})
search_path.write_text(json.dumps(search, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
held_path = ROOT / 'docs/site-held-addresses.jsonl'
held = [json.loads(line) for line in held_path.read_text(encoding='utf-8-sig').splitlines() if line.strip()]
held = [row for row in held if not row['path'].startswith('help/')]
for locale, filename in [('en', 'README.md'), ('ru', 'README_RU.md'), ('uk', 'README_UK.md')]:
    held.append({'path': 'help/' + locale + '/index.html', 'anchor': '', 'held_by': [{'surface': 'README', 'file': filename}]})
held_path.write_text('\n'.join(json.dumps(row, ensure_ascii=False) for row in held) + '\n', encoding='utf-8')

registry_path = ROOT / 'docs/DOCUMENT_REGISTRY.jsonl'
registry = [json.loads(line) for line in registry_path.read_text(encoding='utf-8-sig').splitlines() if line.strip()]
registry = [row for row in registry if not row['file'].startswith('docs/help/')]
known = {row['file'] for row in registry}
for filename in ['docs/' + page for page in all_pages] + ['docs/contracts/CONTEXT-HELP.md', 'DOCS_SURFACES.md']:
    if filename not in known:
        registry.append({'file': filename, 'topic': 'context help', 'area': 'in-app help', 'triggers': ['window, control, help copy or locale changes'], 'role': 'render' if filename.endswith('.html') else 'source'})
registry_path.write_text('\n'.join(json.dumps(row, ensure_ascii=False) for row in sorted(registry, key=lambda row: row['file'])) + '\n', encoding='utf-8')

# Rung 2: capture inputs and images are held by byte digests. The gate detects stale UI inputs.
inputs = list((ROOT / 'src/CyrFlip').glob('*.cs')) + [ROOT / 'tests/CyrFlip.Tests/HelpArtifacts.cs', ROOT / 'tests/CyrFlip.Tests/ThemeCoverageTests.cs']
freshness = {'inputs': {p.relative_to(ROOT).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted(inputs)},
             'images': {p.relative_to(ROOT).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted(HELP.rglob('*.png'))}}
(HELP / 'freshness.json').write_text(json.dumps(freshness, indent=2) + '\n', encoding='utf-8')
size = sum(p.stat().st_size for p in HELP.rglob('*') if p.is_file())
if size > 128 * 1024 * 1024:
    raise SystemExit('Help exceeds the 128 MiB uncompressed budget')
print(f'Help finalized: {len(all_pages)} pages, {len(captures)} captures, {size / 1024 / 1024:.1f} MiB')
