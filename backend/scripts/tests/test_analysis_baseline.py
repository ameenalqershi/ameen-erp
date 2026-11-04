"""CORE-01 acceptance: approved baseline, source tampering and reference guards."""
import importlib.util
import json
from pathlib import Path
from tempfile import TemporaryDirectory
import unittest

SCRIPT = Path(__file__).resolve().parents[1] / "check_analysis_baseline.py"
SPEC = importlib.util.spec_from_file_location("check_analysis_baseline", SCRIPT)
baseline = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(baseline)


class AnalysisBaselineAcceptance(unittest.TestCase):
    def setUp(self):
        self.tmp = TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        for directory in ("data", "diagrams/source", "diagrams/svg"):
            (self.root / directory).mkdir(parents=True)
        self.files = {
            "data/screens.json": json.dumps([{"id": "X01", "fields": [{"id": "X01-F01"}]}]),
            "data/diagrams.json": json.dumps([{
                "id": "system-map",
                "mermaid": "diagrams/source/system-map.mmd",
                "svg": "diagrams/svg/system-map.svg"
            }]),
            "diagrams/source/system-map.mmd": "flowchart LR\nA --> B\n",
            "diagrams/svg/system-map.svg": "<svg xmlns='http://www.w3.org/2000/svg'></svg>",
        }
        for name, content in self.files.items():
            (self.root / name).write_text(content, encoding="utf-8")
        self.refresh_manifest()

    def refresh_manifest(self):
        entries = []
        for name in sorted(self.files):
            content = (self.root / name).read_bytes()
            entries.append({
                "path": name,
                "bytes": len(content),
                "sha256": baseline.hashlib.sha256(content).hexdigest(),
            })
        data = (json.dumps(entries, indent=2) + "\n").encode("utf-8")
        (self.root / "manifest.json").write_bytes(data)
        self.pin = baseline.git_blob_sha1(data)

    def verify(self):
        baseline.check(
            self.root, approved_manifest_sha=self.pin,
            expected_screens=1, expected_fields=1, expected_diagrams=1,
        )

    def test_approved_fixture_passes(self):
        self.verify()

    def test_changed_analysis_source_rejected(self):
        (self.root / "diagrams/source/system-map.mmd").write_text("changed", encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "fingerprint mismatch"):
            self.verify()

    def test_changed_manifest_and_source_still_rejected(self):
        (self.root / "diagrams/source/system-map.mmd").write_text("changed", encoding="utf-8")
        original_approved_pin = self.pin
        self.refresh_manifest()
        self.pin = original_approved_pin
        with self.assertRaisesRegex(ValueError, "manifest changed"):
            self.verify()

    def test_missing_diagram_rejected(self):
        (self.root / "diagrams/svg/system-map.svg").unlink()
        with self.assertRaisesRegex(ValueError, "missing analysis source"):
            self.verify()

    def test_untracked_diagram_reference_rejected(self):
        value = json.loads((self.root / "data/diagrams.json").read_text())
        value[0]["svg"] = "diagrams/svg/other.svg"
        (self.root / "data/diagrams.json").write_text(json.dumps(value))
        self.refresh_manifest()
        with self.assertRaisesRegex(ValueError, "untracked diagram reference"):
            self.verify()

    def test_escape_path_rejected(self):
        data = [{
            "path": "../outside.txt", "bytes": 1,
            "sha256": "0" * 64,
        }]
        raw = json.dumps(data).encode()
        (self.root / "manifest.json").write_bytes(raw)
        self.pin = baseline.git_blob_sha1(raw)
        with self.assertRaisesRegex(ValueError, "unsafe analysis path"):
            self.verify()

    def test_checked_in_approved_analysis_passes(self):
        baseline.check()


if __name__ == "__main__":
    unittest.main()
