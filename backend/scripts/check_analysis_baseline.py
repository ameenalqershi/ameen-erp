#!/usr/bin/env python3
"""CORE-01: fail closed when the approved analysis fingerprint changes."""
import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2] / "muhaseb-erp-analysis"
# Git object ID of the approved manifest on main at 8c767dea (2026-11-04).
# A coordinated analysis+manifest change needs an explicit review of this value.
APPROVED_MANIFEST_GIT_SHA1 = "dddfd0a3ad9b0da7daf4668f8c5f341675149fda"


def git_blob_sha1(content: bytes) -> str:
    return hashlib.sha1(b"blob " + str(len(content)).encode("ascii") + b"\0" + content).hexdigest()


def check(
    root: Path = ROOT,
    approved_manifest_sha: str = APPROVED_MANIFEST_GIT_SHA1,
    expected_screens: int = 150,
    expected_fields: int = 2759,
    expected_diagrams: int = 28,
) -> None:
    root = Path(root)
    manifest_bytes = (root / "manifest.json").read_bytes()
    if git_blob_sha1(manifest_bytes) != approved_manifest_sha:
        raise ValueError("CORE-01: analysis manifest changed without approved baseline review")
    entries = json.loads(manifest_bytes)
    if not isinstance(entries, list) or not entries:
        raise ValueError("CORE-01: analysis manifest must be a nonempty list")
    paths = set()
    for entry in entries:
        relative = Path(entry["path"])
        if relative.is_absolute() or ".." in relative.parts or str(relative) in ("", "."):
            raise ValueError("CORE-01: unsafe analysis path")
        name = relative.as_posix()
        if name in paths:
            raise ValueError("CORE-01: duplicate manifest entry: " + name)
        paths.add(name)
        file = root / relative
        if not file.is_file():
            raise ValueError("CORE-01: missing analysis source: " + name)
        data = file.read_bytes()
        if len(data) != entry["bytes"] or hashlib.sha256(data).hexdigest() != entry["sha256"]:
            raise ValueError("CORE-01: analysis source fingerprint mismatch: " + name)
    screens = json.loads((root / "data/screens.json").read_text(encoding="utf-8"))
    diagrams = json.loads((root / "data/diagrams.json").read_text(encoding="utf-8"))
    fields = sum(len(screen["fields"]) for screen in screens)
    if (len(screens), fields, len(diagrams)) != (expected_screens, expected_fields, expected_diagrams):
        raise ValueError("CORE-01: screen/field/diagram count changed without approval")
    if len({s["id"] for s in screens}) != expected_screens:
        raise ValueError("CORE-01: duplicate screen identifiers")
    if len({d["id"] for d in diagrams}) != expected_diagrams:
        raise ValueError("CORE-01: duplicate diagram identifiers")
    mermaid, svg = set(), set()
    for diagram in diagrams:
        for key, paths_seen, prefix, suffix in (
            ("mermaid", mermaid, "diagrams/source/", ".mmd"),
            ("svg", svg, "diagrams/svg/", ".svg"),
        ):
            value = diagram[key]
            if not value.startswith(prefix) or not value.endswith(suffix) or value not in paths:
                raise ValueError("CORE-01: missing/untracked diagram reference: " + value)
            paths_seen.add(value)
    if len(mermaid) != expected_diagrams or len(svg) != expected_diagrams:
        raise ValueError("CORE-01: duplicate diagram file references")
    print(
        f"PASS CORE-01 baseline: {len(entries)} hashed files, "
        f"{len(screens)} screens, {fields} fields, {len(diagrams)} diagrams"
    )


if __name__ == "__main__":
    check()
