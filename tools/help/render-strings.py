"""Render the help copy deck into the existing UI localization catalog."""
import argparse
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
parser = argparse.ArgumentParser()
parser.add_argument('--check', action='store_true')
args = parser.parse_args()
deck = json.loads((ROOT / 'tools/help/strings.json').read_text(encoding='utf-8'))
lines = ['// Generated from tools/help/strings.json by tools/help/render-strings.py.',
         'namespace CyrFlip', '{', '    internal static partial class Localization',
         '    {', '        private static void AddHelpStrings()', '        {']
for key, translations in deck.items():
    assert translations['en'] == key
    arguments = ', '.join(locale + ': ' + json.dumps(value, ensure_ascii=False)
                          for locale, value in translations.items() if locale != 'en')
    lines.append('            Add(' + json.dumps(key, ensure_ascii=False) + ', ' + arguments + ');')
lines += ['        }', '    }', '}']
rendered = '\n'.join(lines) + '\n'
target = ROOT / 'src/CyrFlip/Localization.Help.cs'
if args.check:
    if target.read_text(encoding='utf-8-sig') != rendered:
        raise SystemExit('Help strings are stale; run tools/help/render-strings.py')
else:
    target.write_text(rendered, encoding='utf-8-sig')
