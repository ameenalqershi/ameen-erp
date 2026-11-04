"""CORE DGM module navigation validation (positive, negative and real baseline)."""
import importlib.util
import json
from pathlib import Path
from tempfile import TemporaryDirectory
import unittest

SCRIPT = Path(__file__).resolve().parents[1] / "check_module_diagram_navigation.py"
SPEC = importlib.util.spec_from_file_location("check_module_diagram_navigation", SCRIPT)
audit = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(audit)


class ModuleDiagramAcceptance(unittest.TestCase):
    def setUp(self):
        self.temporary = TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        for part in ("data", "diagrams/source", "diagrams/svg"):
            (self.root / part).mkdir(parents=True)
        self.screens = [
            {"id": "SYS01", "module": "SYS", "name": "الشركات",
             "menu": "إدارة النظام ← البيانات الأساسية ← الشركات"},
            {"id": "SYS02", "module": "SYS", "name": "الفروع",
             "menu": "إدارة النظام ← البيانات الأساسية ← الفروع"},
        ]
        self.diagram = [
            {"id": "module-SYS", "title": "إدارة النظام",
             "mermaid": "diagrams/source/module-SYS.mmd",
             "svg": "diagrams/svg/module-SYS.svg"}
        ]
        self.mermaid = ('flowchart TB\n'
                        '  root["إدارة النظام"]\n'
                        '  cat0["البيانات الأساسية"]\n'
                        '  SYS01["SYS01 • الشركات"]\n'
                        '  SYS02["SYS02 • الفروع"]\n'
                        '  root -->|"قائمة"| cat0\n'
                        '  cat0 -->|"عنصر قائمة"| SYS01\n'
                        '  SYS01 --> SYS02\n')
        (self.root / "diagrams/svg/module-SYS.svg").write_text(
            '<svg xmlns="http://www.w3.org/2000/svg"></svg>', encoding="utf-8")
        self.save()

    def save(self):
        (self.root / "data/screens.json").write_text(json.dumps(self.screens, ensure_ascii=False), encoding="utf-8")
        (self.root / "data/diagrams.json").write_text(json.dumps(self.diagram, ensure_ascii=False), encoding="utf-8")
        (self.root / "diagrams/source/module-SYS.mmd").write_text(self.mermaid, encoding="utf-8")

    def audit(self):
        audit.check(self.root, expected_modules={"SYS"})

    def test_valid_navigation(self):
        self.audit()

    def test_missing_screen_node_rejected(self):
        self.mermaid = self.mermaid.replace('  SYS02["SYS02 • الفروع"]\n', '')
        self.save()
        with self.assertRaisesRegex(ValueError, "screen ID mismatch"):
            self.audit()

    def test_incorrect_screen_label_rejected(self):
        self.mermaid = self.mermaid.replace("SYS01 • الشركات", "SYS01 • عنوان آخر")
        self.save()
        with self.assertRaisesRegex(ValueError, "screen label mismatch"):
            self.audit()

    def test_cross_category_navigation_rejected(self):
        self.screens[1]["menu"] = "إدارة النظام ← التهيئة ← الفروع"
        self.save()
        with self.assertRaisesRegex(ValueError, "wrong category"):
            self.audit()

    def test_dangling_edge_rejected(self):
        self.mermaid += "  SYS02 --> SYS99\n"
        self.save()
        with self.assertRaisesRegex(ValueError, "dangling navigation edge"):
            self.audit()

    def test_cycle_rejected(self):
        self.mermaid = self.mermaid.replace("  SYS01 --> SYS02", "  SYS02 --> SYS01")
        self.save()
        with self.assertRaisesRegex(ValueError, "invalid/multiple navigation parents"):
            self.audit()

    def test_invalid_svg_rejected(self):
        (self.root / "diagrams/svg/module-SYS.svg").write_text("<svg>", encoding="utf-8")
        with self.assertRaises(Exception):
            self.audit()

    def test_real_approved_modules(self):
        audit.check()


if __name__ == "__main__":
    unittest.main()
