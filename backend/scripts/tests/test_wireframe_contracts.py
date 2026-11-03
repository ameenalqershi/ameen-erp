"""CORE WFR-01..06: verify original wireframe labels against the numbered field registry.

An audit of backend contracts only: passing does NOT mean the screen/API exists.
"""
import csv
from pathlib import Path
import unittest
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[3]
ORIGINAL = ROOT / "muhaseb-erp-analysis" / "wireframes"
REGISTRY = ROOT / "docs" / "backend" / "fields-implementation.csv"

# Every mapping names the exact source screen and its original field identifiers.
# Non-field preview labels and unsupported UI actions are documented separately.
MAPPINGS = {
    "chart-of-accounts": ("GL01", {
        "رقم الحساب": (3,), "اسم الحساب": (4,), "الحساب الأب": (8,),
        "التصنيف": (9,), "رئيسي / فرعي": (10,), "الطبيعة": (11,),
        "عملة الحساب": (12,), "حساب رقابة؟": (13,),
        "إلزام مركز تكلفة؟": (14,), "فعال": (6,),
    }),
    "item-card": ("INV04", {
        "رمز الصنف": (3,), "الاسم العربي": (4,), "الاسم الإنجليزي": (5,),
        "نوع الصنف / المجموعة": (8, 9), "الوحدة الأساسية": (10,),
        "طريقة التتبع": (11,), "رمز الضريبة": (14,),
        "حد إعادة الطلب": (13,), "حالة الصنف": (6,),
        "الوحدة": (15,), "معامل التحويل": (16,),
        "الباركود": (17,), "سعر البيع": (18,), "عملة السعر": (18,),
    }),
    "journal": ("GL03", {
        "رقم القيد / الحالة": (5, 12), "تاريخ الأثر": (7,),
        "نوع القيد": (13,), "الفرع": (3,),
        "المرجع الخارجي": (11,), "البيان": (10, 24),
        "قيد عكسي؟": (14,), "تاريخ العكس": (15,),
        "الحساب": (16,), "العملة": (19,), "السعر": (20,),
        "مدين": (17,), "دائن": (18,), "المركز": (21,),
        "المشروع": (22,),
    }),
    "receipt": ("CASH03", {
        "رقم السند": (5,), "التاريخ / الفرع": (6, 3),
        "الدافع": (13,), "طريقة القبض": (14,), "الصندوق / البنك": (15,),
        "المبلغ": (16,), "العملة / السعر": (8, 9),
        "مرجع الشيك عند اللزوم": (17,), "البيان": (10,),
        "الفاتورة": (18,), "المخصص": (19,), "المقبوض": (16,),
    }),
    "sales-invoice": ("SAL06", {
        "رقم الفاتورة / الحالة": (5, 12), "تاريخ الوثيقة والأثر": (6, 7),
        "العميل / نوع الفاتورة": (13, 14), "المخزن / مرجع التسليم": (18, 19),
        "العملة / سعر الصرف": (8, 9), "طريقة الدفع / الاستحقاق": (15, 17),
        "تاريخ التوريد": (16,), "مرجع أمر البيع": (19,),
        "البيان": (10,), "الصنف": (22,), "الوحدة": (23,),
        "الكمية": (24,), "مجاني": (25,), "السعر": (26,),
        "الخصم": (27, 20), "الضريبة": (28, 34,),
        "الصافي": (30,), "قبل الضريبة": (31,),
        "الإجمالي": (35,), "المتبقي": (37,),
    }),
    "trial-balance": ("RPT01", {
        "الشركة / الفروع": (1, 2), "من تاريخ": (3,), "إلى تاريخ": (4,),
        "مستوى الدليل": (9,), "مركز التكلفة": (10,),
        "عملة العرض": (5,), "مستندات مرحلة": (7,),
        "إظهار الصفري": (11,),
    }),
}

# Visible in the artwork, but not a standalone approved backend input or command.
# Keep these visible in the audit: omission is not permission to invent a policy.
NONFIELD_LABELS = {
    "chart-of-accounts": ("المستوى", "تقديم", "اعتماد"),
    "item-card": ("حسابات الربط", "دفعات وتسلسل", "تقديم", "اعتماد"),
    "journal": ("مرفقات", "إجمالي المدين", "إجمالي الدائن", "الفرق = صفر"),
    "receipt": ("تاريخها", "الاستحقاق", "المفتوح", "فرق الصرف", "غير المخصص"),
    "sales-invoice": ("تبويبات: القيد • حركة المخزون • الدفعات • الفوترة • سجل الاعتماد",),
    "trial-balance": ("مقارنة", "طباعة", "فرق الاتزان"),
}


def svg_labels(svg: Path) -> set[str]:
    root = ET.parse(svg).getroot()
    if root.tag != "{http://www.w3.org/2000/svg}svg":
        raise ValueError(f"Not an SVG: {svg.name}")
    return {"".join(node.itertext()).strip() for node in root.iter()
            if node.tag == "{http://www.w3.org/2000/svg}text"}


def check_one(name: str, fields: dict[str, dict[str, str]]):
    screen, mapping = MAPPINGS[name]
    labels = svg_labels(ORIGINAL / f"{name}.svg")
    assert f"{screen} •" in " ".join(labels), f"{name}: wrong original screen"
    for label, nums in mapping.items():
        assert label in labels, f"{name}: absent original label: {label}"
        for n in nums:
            fid = f"{screen}-F{n:02}"
            assert fid in fields, f"{name}: unknown approved field {fid}"
            assert fields[fid]["screen_id"] == screen, f"{name}: cross-screen mapping {fid}"
            assert fields[fid]["status"] in ("implemented", "audit", "derived", "partial", "pending")
    for label in NONFIELD_LABELS[name]:
        assert label in labels, f"{name}: undocumented wireframe-only label: {label}"


class WireframeBackendContractAudit(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        with REGISTRY.open(encoding="utf-8-sig", newline="") as stream:
            rows = list(csv.DictReader(stream))
        cls.fields = {row["field_id"]: row for row in rows}
        if len(cls.fields) != 2759 or len(rows) != 2759:
            raise AssertionError("Original 2759-field registry is missing or duplicated")

    def test_all_six_original_wireframes_have_explicit_backend_field_mapping(self):
        for name in MAPPINGS:
            with self.subTest(wireframe=name):
                check_one(name, self.fields)

    def test_each_wireframe_has_unique_mapping_labels(self):
        for name, (_, mapping) in MAPPINGS.items():
            with self.subTest(wireframe=name):
                self.assertFalse(set(mapping).intersection(NONFIELD_LABELS[name]))
                self.assertTrue(all(nums and len(nums) == len(set(nums)) for nums in mapping.values()))

    def test_missing_label_is_a_failure(self):
        with self.assertRaisesRegex(AssertionError, "absent original label"):
            original = MAPPINGS["chart-of-accounts"]
            try:
                MAPPINGS["chart-of-accounts"] = (original[0], {**original[1], "nonexistent label": (3,)})
                check_one("chart-of-accounts", self.fields)
            finally:
                MAPPINGS["chart-of-accounts"] = original

    def test_nonexistent_field_is_a_failure(self):
        with self.assertRaisesRegex(AssertionError, "unknown approved field"):
            original = MAPPINGS["trial-balance"]
            try:
                MAPPINGS["trial-balance"] = (original[0], {**original[1], "من تاريخ": (999,)})
                check_one("trial-balance", self.fields)
            finally:
                MAPPINGS["trial-balance"] = original


if __name__ == "__main__":
    unittest.main()
