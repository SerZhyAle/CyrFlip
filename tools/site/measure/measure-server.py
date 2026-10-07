"""Static server for tools/site/Measure-SiteContracts.ps1.

Serves the docs/ folder at "/", the measurement page at "/harness/" and the run's scratch folder at "/work/";
POST /save?name=<n> stores the harness's JSON result as <out>/<n>.json. Loopback only, no logging.

usage: measure-server.py <port> <docs-dir> <out-dir> [<production-site-base>]
"""
import http.server
import os
import re
import sys
import urllib.parse

PORT = int(sys.argv[1])
DOCS = os.path.abspath(sys.argv[2])
OUT = os.path.abspath(sys.argv[3])
HARNESS = os.path.dirname(os.path.abspath(__file__))
SITE_BASE = sys.argv[4] if len(sys.argv) > 4 else ""
os.makedirs(OUT, exist_ok=True)


class Handler(http.server.SimpleHTTPRequestHandler):
    def do_GET(self):
        # The not-found page needs absolute asset addresses to work at any production
        # depth. Measure this checkout's assets, rather than silently loading the old
        # published CSS. Only same-site linked resources are remapped; navigation stays.
        if SITE_BASE and urllib.parse.urlparse(self.path).path == "/404.html":
            with open(os.path.join(DOCS, "404.html"), encoding="utf-8") as f:
                source = f.read()
            source = re.sub(r'(<link\b[^>]*\bhref=")' + re.escape(SITE_BASE), r'\1/', source)
            body = source.encode("utf-8")
            self.send_response(200)
            self.send_header("Content-Type", "text/html; charset=utf-8")
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)
            return
        super().do_GET()

    def translate_path(self, path):
        path = urllib.parse.unquote(urllib.parse.urlparse(path).path)
        if path.startswith("/harness/"):
            base, rel = HARNESS, path[len("/harness/"):]
        elif path.startswith("/work/"):
            base, rel = OUT, path[len("/work/"):]
        else:
            base, rel = DOCS, path.lstrip("/")
        full = os.path.normpath(os.path.join(base, rel))
        if not full.startswith(base):  # no escaping the served folder
            return os.path.join(base, "__denied__")
        if os.path.isdir(full):
            full = os.path.join(full, "index.html")
        return full

    def do_POST(self):
        query = urllib.parse.parse_qs(urllib.parse.urlparse(self.path).query)
        name = "".join(c for c in query.get("name", ["out"])[0] if c.isalnum() or c in "-_")
        body = self.rfile.read(int(self.headers.get("Content-Length", 0)))
        with open(os.path.join(OUT, name + ".json"), "wb") as f:
            f.write(body)
        self.send_response(200)
        self.send_header("Content-Length", "2")
        self.end_headers()
        self.wfile.write(b"ok")

    def log_message(self, *args):
        pass


http.server.ThreadingHTTPServer(("127.0.0.1", PORT), Handler).serve_forever()
