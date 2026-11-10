#!/usr/bin/env python3
"""Validate local community docs and GitHub issue forms without network access."""
from __future__ import annotations

import argparse
import re
import sys
import tempfile
from pathlib import Path
from urllib.parse import unquote, urlparse

try:
    import yaml
except ImportError:
    yaml = None

ROOT = Path(__file__).resolve().parents[1]
DOCS = ["README.md", "CONTRIBUTING.md", "CODE_OF_CONDUCT.md",
        "GOVERNANCE.md", "SECURITY.md", "SUPPORT.md", "ROADMAP.md", "AGENTS.md"]
LINK = re.compile(r"(?<!!)\[[^\]]+\]\(([^)\s]+)(?:\s+['\"][^)]*['\"])?\)")
FENCE = re.compile(r"^\s*(\x60{3,}|~{3,})")


def links(text: str) -> list[str]:
    lines: list[str] = []
    marker: str | None = None
    for line in text.splitlines():
        match = FENCE.match(line)
        if match:
            fence = match.group(1)
            if marker is None:
                marker = fence
            elif fence[0] == marker[0] and len(fence) >= len(marker):
                marker = None
            continue
        if marker is None:
            lines.append(line)
    return LINK.findall("\n".join(lines))


def local_target(root: Path, source: Path, href: str) -> str | None:
    parsed = urlparse(href)
    if parsed.scheme or parsed.netloc or href.startswith(("#", "//")):
        return None
    name = unquote(parsed.path)
    if not name:
        return None
    try:
        target = ((root / name.lstrip("/")) if name.startswith("/") else source.parent / name).resolve()
        target.relative_to(root.resolve())
    except ValueError:
        return f"{source.relative_to(root)}: link escapes repository: {href}"
    return None if target.exists() else f"{source.relative_to(root)}: missing local target: {href}"


def validate(root: Path) -> list[str]:
    errors: list[str] = []
    files = [root / name for name in DOCS]
    files += sorted((root / "docs/community").glob("*.md"))
    for path in files:
        if not path.is_file():
            errors.append(f"missing file: {path.relative_to(root)}")
            continue
        for link in links(path.read_text(encoding="utf-8")):
            result = local_target(root, path, link)
            if result:
                errors.append(result)

    if yaml is None:
        errors.append("Install PyYAML==6.0.2 to validate issue templates.")
        return errors
    paths = sorted((root / ".github/ISSUE_TEMPLATE").glob("*.yml"))
    paths += [root / ".github/dependabot.yml", root / ".github/workflows/community.yml"]
    for path in paths:
        try:
            data = yaml.safe_load(path.read_text(encoding="utf-8"))
            if not isinstance(data, dict):
                errors.append(f"{path.relative_to(root)}: expected YAML mapping")
            elif path.parent.name == "ISSUE_TEMPLATE":
                if path.name == "config.yml":
                    if not isinstance(data.get("contact_links"), list):
                        errors.append(f"{path}: contact_links must be a list")
                elif not isinstance(data.get("name"), str) or not isinstance(data.get("body"), list):
                    errors.append(f"{path}: issue form requires name and body")
        except (OSError, yaml.YAMLError) as exc:
            errors.append(f"{path}: invalid YAML: {exc}")
    return errors


def self_test() -> list[str]:
    with tempfile.TemporaryDirectory() as tmp:
        root = Path(tmp)
        source = root / "README.md"
        source.write_text("[valid](found.md)\n[invalid](missing.md)", encoding="utf-8")
        (root / "found.md").write_text("ok", encoding="utf-8")
        results = [local_target(root, source, link) for link in links(source.read_text(encoding="utf-8"))]
        if results[0] is not None or results[1] is None:
            return ["self-test: broken-link detection failed"]
        if links("~~~md\n[ignored](missing.md)\n~~~\n[ok](found.md)") != ["found.md"]:
            return ["self-test: fenced-code filtering failed"]
    return []


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()
    errors = self_test() if args.self_test else validate(ROOT)
    for error in errors:
        print("ERROR:", error, file=sys.stderr)
    if errors:
        return 1
    print("Community documentation checks passed.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
