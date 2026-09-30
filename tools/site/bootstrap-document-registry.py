"""One-time registry inventory helper. Review the generated topics and triggers."""
import json
import subprocess
from pathlib import Path

root = Path(__file__).resolve().parents[2]
tracked = subprocess.check_output(["git", "ls-files", "*.md"], cwd=root, text=True).splitlines()
paths = set(tracked)
paths.update(p.relative_to(root).as_posix() for p in (root / "docs/contracts").glob("*.md"))
paths.update(p.name for p in root.glob("*.md"))
paths.update(p.relative_to(root).as_posix() for p in (root / "docs").rglob("*.html"))
paths.update(p.relative_to(root).as_posix() for p in (root / "store").glob("listing-*.txt"))
paths.update(["docs/sitemap.xml", "docs/search-index.json", "docs/termbase.json"])
rows = []
for path in sorted(paths):
    if path.startswith(".claude/"):
        area, trigger = "agent workflow", "agent workflow or build/release procedure changes"
    elif path.startswith(".github/"):
        area, trigger = "contribution", "pull request process changes"
    elif path.startswith("docs/contracts/"):
        area, trigger = "contracts", "contract adoption or implementation changes"
    elif path.startswith("docs/"):
        area, trigger = "public site", "page content, UI, locale or publishing changes"
    elif path.startswith("msix/") or path.startswith("store/"):
        area, trigger = "Store publishing", "Store listing source or submission changes"
    elif path.startswith("tools/"):
        area, trigger = "developer tools", "tool behavior or verification procedure changes"
    elif path.startswith("vscode-extension/"):
        area, trigger = "VS Code extension", "extension behavior or release changes"
    elif path.startswith("winget/"):
        area, trigger = "winget publishing", "manifest or release changes"
    else:
        area, trigger = "product", "feature, setup, configuration or release changes"
    rows.append({"file": path, "topic": Path(path).stem.replace("-", " ").replace("_", " "),
                 "area": area, "triggers": [trigger],
                 "role": "render" if path in {"msix/store-listings.md", "docs/sitemap.xml", "docs/search-index.json"}
                 or path.startswith("store/listing-") else "source"})
(root / "docs/DOCUMENT_REGISTRY.jsonl").write_text(
    "".join(json.dumps(row, ensure_ascii=False) + "\n" for row in rows), encoding="utf-8")
print(len(rows), "documents registered")
