#!/usr/bin/env python3
"""Check a deployed WebUI's JavaScript entry points without invoking game actions."""
import sys
from html.parser import HTMLParser
from urllib.parse import urljoin, urlparse
from urllib.request import Request, urlopen


class Scripts(HTMLParser):
    def __init__(self):
        super().__init__()
        self.sources = []

    def handle_starttag(self, tag, attrs):
        attrs = dict(attrs)
        if tag == "script" and attrs.get("src"):
            self.sources.append((attrs["src"], "javascript"))
        elif tag == "link" and attrs.get("rel") == "stylesheet" and attrs.get("href"):
            self.sources.append((attrs["href"], "text/css"))


base = (sys.argv[1] if len(sys.argv) > 1 else "http://127.0.0.1:5290").rstrip("/") + "/"
with urlopen(Request(base, headers={"Cache-Control": "no-cache"}), timeout=15) as response:
    page = Scripts()
    page.feed(response.read().decode("utf-8-sig"))
assert any("blazor.web" in source for source, _ in page.sources), "Blazor bootstrap script is missing"
for source, content_type in page.sources:
    url = urljoin(base, source)
    if urlparse(url).netloc != urlparse(base).netloc:
        continue
    with urlopen(url, timeout=15) as response:
        assert response.status == 200 and content_type in response.headers.get("Content-Type", ""), source
        if content_type == "javascript":
            assert response.read(1), f"Empty script: {source}"
    print(f"PASS: {source}")
