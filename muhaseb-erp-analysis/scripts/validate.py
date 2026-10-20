#!/usr/bin/env python3
"""Check document consistency, not a future ERP implementation."""
from pathlib import Path
from html.parser import HTMLParser
from urllib.parse import urlsplit, unquote
from decimal import Decimal as D
import json
import re
import csv
import xml.etree.ElementTree as ET

ROOT=Path(__file__).resolve().parents[1]
screens=json.loads((ROOT/"data/screens.json").read_text())
modules=json.loads((ROOT/"data/modules.json").read_text())
diagrams=json.loads((ROOT/"data/diagrams.json").read_text())
checks=[]
def check(name,ok,detail=""):
    checks.append(dict(check=name,passed=bool(ok),detail=detail))

ids={s["id"] for s in screens}
check("unique_screen_ids",len(ids)==len(screens))
mid={m["id"] for m in modules}
check("valid_modules",all(s["module"] in mid for s in screens))
check("valid_screen_references",all(r in ids for s in screens for r in s["related"]))
fids=[f["id"] for s in screens for f in s["fields"]]
check("unique_field_ids",len(fids)==len(set(fids)))
check("field_contract",all(f["name"] and f["type"] and f["requirement"] and f["source"] and f["validation"] for s in screens for f in s["fields"]))
check("screen_contract",all(s["validation"] and s["outputs"] and s["purpose"] and s["actions"] and s["acceptance_positive"] and s["acceptance_negative"] for s in screens))
check("honest_evidence_status",all(s["evidence"]=="proposed_unverified_ui" for s in screens))
check("module_documents",all((ROOT/f'docs/modules/{m["id"]}.md').is_file() for m in modules))
check("each_screen_in_module_diagram",all(s["id"] in (ROOT/f'diagrams/source/module-{s["module"]}.mmd').read_text() for s in screens))
check("all_diagram_sources",all((ROOT/d["mermaid"]).exists() and (ROOT/d["svg"]).exists() for d in diagrams))
svgfiles=list((ROOT/"diagrams/svg").glob("*.svg"))+list((ROOT/"wireframes").glob("*.svg"))
bad=[]
for f in svgfiles:
    try:
        root=ET.parse(f).getroot()
        if root.tag!="{http://www.w3.org/2000/svg}svg":bad.append(f.name)
    except ET.ParseError:bad.append(f.name)
check("valid_svg_xml",not bad,str(bad))
class Parser(HTMLParser):
    def __init__(self):
        super().__init__();self.ids=[];self.links=[];self.external_assets=[]
    def handle_starttag(self,tag,attrs):
        d=dict(attrs)
        if "id" in d:self.ids.append(d["id"])
        if tag=="a" and "href" in d:self.links.append(d["href"])
        if tag in ("script","img","link","iframe"):
            a=d.get("src",d.get("href",""))
            if a.startswith(("http","//")):self.external_assets.append(a)
p=Parser();p.feed((ROOT/"index.html").read_text())
check("unique_html_ids",len(p.ids)==len(set(p.ids)))
broken=[]
for link in p.links:
    u=urlsplit(link)
    if u.scheme or u.netloc:continue
    if u.path and not (ROOT/unquote(u.path)).exists():broken.append(link)
    if not u.path and u.fragment and unquote(u.fragment) not in p.ids:broken.append(link)
check("local_html_links",not broken,str(broken))
check("offline_no_external_assets",not p.external_assets,str(p.external_assets))
check("all_screens_in_html",all(x in p.ids for x in ids))
for name in ("screens","fields","traceability","parity-matrix"):
    with (ROOT/f"data/{name}.csv").open(encoding="utf-8-sig",newline="") as f:
        rows=list(csv.reader(f))
    check("csv_columns_"+name,all(len(r)==len(rows[0]) for r in rows[1:]))
    check("csv_count_"+name,len(rows)-1==(len(fids) if name=="fields" else len(screens)))
rules=(ROOT/"docs/02-accounting-and-business-rules.md").read_text()
check("referenced_rules_exist",all(r in rules for s in screens for r in s["rules"]))

# Arithmetic only: explicitly not software accounting acceptance tests.
cost=D("100");sell=D("150");tax=D(".10")
ar=D(4)*sell*(1+tax)-D("330")-sell*(1+tax)
stock=(D(10)-D(4)+D(1))*cost
profit=(D(4)-D(1))*(sell-cost)
net_tax=(D(4)-D(1))*sell*tax-D(10)*cost*tax
check("document_example_arithmetic",ar==D("165") and stock==D("700") and profit==D("150") and net_tax==D("-55"),
      "فحص حساب المثال المكتوب فقط، وليس اختبار برنامج")
check("fx_example_arithmetic",D(100)*D(520)-D(100)*D(500)==D(2000))
check("landed_cost_example_arithmetic",D(100)*D(4)/D(10)==D(40) and D(100)*D(6)/D(10)==D(60))
scenario_text=(ROOT/"docs/07-workflows-and-acceptance.md").read_text()
scenarios=set(re.findall(r"\| (AT\d+) \|",scenario_text))
check("thirty_core_acceptance_scenarios",len(scenarios)==30)
for row in scenario_text.splitlines():
    if re.match(r"\| AT\d+ \|",row):
        cols=[c.strip() for c in row.strip("|").split("|")]
        check("scenario_references_"+cols[0],all(x.strip() in ids for x in cols[1].split(",")))
report=dict(scope="فحص اتساق حزمة التحليل؛ اختبارات قبول التطبيق لم تنفذ",modules=len(modules),screens=len(screens),field_occurrences=len(fids),diagrams=len(diagrams),wireframes=len(svgfiles)-len(diagrams),
            passed=all(c["passed"] for c in checks),checks=checks,
            browser_validation="لم ينجح تشغيل Chromium داخل قيود البيئة؛ تفحص الرسومات عبر librsvg، والروابط والبنية بفحص محلي")
(ROOT/"validation-report.json").write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding="utf-8")
print(json.dumps(dict(passed=report["passed"],checks=len(checks),failed=[c for c in checks if not c["passed"]]),ensure_ascii=False,indent=2))
raise SystemExit(0 if report["passed"] else 1)
