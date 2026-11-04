"""Positive and negative cases for the original, conceptual CORE ER diagram."""
import json
from pathlib import Path
import shutil
import sys
from tempfile import TemporaryDirectory
import unittest

SCRIPTS = Path(__file__).resolve().parents[1]
ROOT = SCRIPTS.parents[1]
sys.path.insert(0, str(SCRIPTS))
import check_core_er_diagram as audit  # noqa: E402


class OriginalErDiagramAudit(unittest.TestCase):
    def setUp(self):
        temporary = TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        for name in (
            "muhaseb-erp-analysis/data/diagrams.json",
            audit.SOURCE,
            audit.SVG,
            "docs/backend/diagrams-implementation.csv",
        ):
            path = self.root / name
            path.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(ROOT / name, path)

    def rewrite(self, filename, old, new):
        path = self.root / filename
        before = path.read_text(encoding="utf-8")
        self.assertIn(old, before)
        path.write_text(before.replace(old, new, 1), encoding="utf-8")

    def test_original_graph_and_manifest_pass(self):
        entities, relations, status = audit.check()
        self.assertGreaterEqual(entities, 12)
        self.assertGreaterEqual(relations, 25)
        self.assertEqual(status, "partial")

    def test_document_line_link_source_and_target_are_both_required(self):
        self.rewrite(audit.SOURCE,
                     "DOCUMENT_LINE ||--o{ LINE_LINK : target",
                     "DOCUMENT_LINE ||--o{ LINE_LINK : former_target")
        with self.assertRaisesRegex(ValueError, "missing original ER relationship"):
            audit.check(self.root)

    def test_optional_einvoice_cardinality_is_preserved(self):
        self.rewrite(audit.SOURCE,
                     "DOCUMENT ||--o| EINVOICE : fiscal_representation",
                     "DOCUMENT ||--o{ EINVOICE : fiscal_representation")
        with self.assertRaisesRegex(ValueError, "missing original ER relationship"):
            audit.check(self.root)

    def test_invalid_cardinality_is_rejected(self):
        self.rewrite(audit.SOURCE,
                     "COMPANY ||--o{ BRANCH : contains",
                     "COMPANY ||--?? BRANCH : contains")
        with self.assertRaisesRegex(ValueError, "unsupported ER relation syntax"):
            audit.check(self.root)

    def test_duplicate_relationship_is_rejected(self):
        path = self.root / audit.SOURCE
        path.write_text(path.read_text() +
                        "  COMPANY ||--o{ BRANCH : contains\n", encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "duplicate ER relationship"):
            audit.check(self.root)

    def test_missing_index_is_rejected(self):
        path = self.root / "muhaseb-erp-analysis/data/diagrams.json"
        data = json.loads(path.read_text(encoding="utf-8"))
        data = [r for r in data if r["id"] != "data-model"]
        path.write_text(json.dumps(data, ensure_ascii=False), encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "missing/duplicate indexed"):
            audit.check(self.root)

    def test_bad_traceability_source_is_rejected(self):
        self.rewrite("docs/backend/diagrams-implementation.csv",
                     "data-model,muhaseb-erp-analysis/diagrams/source/data-model.mmd",
                     "data-model,muhaseb-erp-analysis/diagrams/source/not-original.mmd")
        with self.assertRaisesRegex(ValueError, "missing/incorrect ER traceability"):
            audit.check(self.root)

    def test_invalid_svg_is_rejected(self):
        (self.root / audit.SVG).write_text("<svg><unclosed>", encoding="utf-8")
        with self.assertRaises(Exception):
            audit.check(self.root)

    def test_relation_to_unknown_type_is_rejected(self):
        self.rewrite(audit.SOURCE,
                     "DOCUMENT ||--o{ OUTBOX_EVENT : publishes",
                     "DOCUMENT ||--o{ UNKNOWN_EVENT : publishes")
        with self.assertRaisesRegex(ValueError, "missing original ER relationship"):
            audit.check(self.root)


if __name__ == "__main__":
    unittest.main()
