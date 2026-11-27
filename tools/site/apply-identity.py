"""Point the site and help pages at the product's identity renditions (PRODUCT-IDENTITY rule 8, CYRFLP-0139).

Idempotent; run after `dotnet run --project tools/IconGen` and by tools/help/finalize.py.
- Site pages (docs/**/*.html outside docs/help): the identity block's brand gets the `site.identity`
  rendition, docs/assets/icon-256.png, once, with an empty alt (the name beside it is the reading).
- Help pages (docs/help/<locale>/*.html): the `help.head` rendition, docs/help/product-mark.svg, before the
  title on its line (CONTEXT-HELP 0.6 item Q), and the favicon link to docs/help/favicon.ico. Skipped while the
  generator has not written those files, so a page never links to a missing file.
The brand image is outside the translation fingerprint (tools/site/sync-doc-quality.py body_hash), so adding it
moves no translation stamp. Only the elements named here are touched; nothing else in a page changes.
"""
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
DOCS = ROOT / "docs"
HELP = DOCS / "help"
BRAND = '<span class="brand">'
BRAND_MARK = re.compile(r'<img class="brand-mark"[^>]*>')


def write(path, old, new):
    if new != old:
        path.write_bytes(new.encode("utf-8"))
        return 1
    return 0


def site(changed):
    if not (DOCS / "assets" / "icon-256.png").exists():
        print("apply-identity: docs/assets/icon-256.png missing, site pages left alone")
        return changed
    for path in sorted(DOCS.rglob("*.html")):
        if HELP in path.parents:
            continue
        old = path.read_bytes().decode("utf-8")
        if BRAND not in old or BRAND_MARK.search(old):
            continue
        prefix = "../" * (len(path.relative_to(DOCS).parts) - 1)
        mark = '<img class="brand-mark" src="' + prefix + 'assets/icon-256.png" width="20" height="20" alt="">'
        changed += write(path, old, old.replace(BRAND, BRAND + mark, 1))
    return changed


def help_pages(changed):
    head, icon = HELP / "product-mark.svg", HELP / "favicon.ico"
    if not head.exists() or not icon.exists():
        print("apply-identity: docs/help/product-mark.svg or favicon.ico missing (run tools/IconGen), help pages left alone")
        return changed
    picture = '<img class="product-head" src="../product-mark.svg" width="32" height="32" alt="">'
    for path in sorted(HELP.glob("*/*.html")):
        old = path.read_bytes().decode("utf-8")
        new = old
        if 'rel="icon"' not in new:
            new = new.replace('</head>', '<link rel="icon" href="../favicon.ico"></head>', 1)
        if 'class="product-head"' not in new:
            if '<div class="title-row">' in new:
                new = new.replace('<div class="title-row">', '<div class="title-row">' + picture, 1)
            else:
                new = re.sub(r'<h1>(.*?)</h1>', lambda m: '<div class="title-row">' + picture + m.group(0) + '</div>', new, count=1, flags=re.S)
        changed += write(path, old, new)
    return changed


def main():
    changed = help_pages(site(0))
    print("apply-identity: %d page(s) changed" % changed)
    return 0


if __name__ == "__main__":
    sys.exit(main())
