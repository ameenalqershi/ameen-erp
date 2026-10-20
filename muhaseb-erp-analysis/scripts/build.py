#!/usr/bin/env python3
"""Build local analysis documents, structured catalogues and native SVG diagrams."""
from pathlib import Path
from collections import Counter
import csv
import html
import json
import re
import textwrap
import xml.etree.ElementTree as ET
from catalog import MODULES, RAW

ROOT = Path(__file__).resolve().parents[1]
DATE = "2026-10-20"
TYPE = {
    "code":"رمز نصي", "text":"نص", "ref":"مرجع", "date":"تاريخ",
    "time":"تاريخ ووقت", "int":"عدد صحيح", "qty":"عدد عشري للكمية",
    "money":"مبلغ عشري", "rate":"نسبة/سعر عشري", "bool":"نعم/لا", "enum":"اختيار من قائمة"
}
REQ = {"required":"إلزامي","optional":"اختياري","conditional":"شرطي","system":"محسوب/نظام"}
KIND = {"master":"البيانات الأساسية","setup":"التهيئة","transaction":"العمليات",
        "journal":"العمليات","invoice":"العمليات","stock":"العمليات","process":"الإجراءات","report":"التقارير"}

def write(path, value):
    target = ROOT / path
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(value, encoding="utf-8")

def jsonwrite(path, obj):
    write(path, json.dumps(obj, ensure_ascii=False, indent=2))

def csvwrite(path, columns, rows):
    with (ROOT/path).open("w", newline="", encoding="utf-8-sig") as f:
        w=csv.writer(f)
        w.writerow(columns)
        w.writerows(rows)

def parse_fields(spec, area, inherited=False):
    result=[]
    for x in filter(None, spec.split(";")):
        name, typ, req = x.split("~")
        assert typ in TYPE and req in REQ, x
        source = ("مولد من النظام/سياق المستخدم" if req=="system" else
                  "اختيار بطاقة فعالة ضمن الشركة والصلاحية" if typ=="ref" else
                  "قائمة قيم معرفة في سياسة الشاشة" if typ=="enum" else "إدخال المستخدم")
        validation = {
            "code":"نص يحفظ الأصفار البادئة؛ طول مناسب وتفرد عند تعريف بطاقة",
            "text":"Unicode؛ يمنع المحتوى التنفيذي؛ طول بحسب الغرض",
            "ref":"مرجع موجود وفعال ومصرح به وفي الشركة الصحيحة",
            "date":"تاريخ صالح؛ يراجع مقابل الفترة إذا كان تاريخ أثر",
            "time":"وقت محفوظ مع المنطقة الزمنية؛ وقت الحدث يولد من الخادم",
            "int":"عدد صحيح؛ الحدود بحسب دلالة الحقل",
            "qty":"Decimal(20,6) مبدئيًا؛ الإشارة والحدود وفق نوع الحركة",
            "money":"Decimal(20,6) داخليًا؛ التقريب بدقة العملة؛ لا float",
            "rate":"Decimal(20,10) مبدئيًا؛ السعر موجب والنسبة ضمن حدود القاعدة",
            "bool":"قيمة منطقية صريحة",
            "enum":"قيمة من قائمة مسموحة ومؤرخة عند لزوم ذلك",
        }[typ]
        result.append(dict(name=name,type=typ,type_ar=TYPE[typ],requirement=req,
                           requirement_ar=REQ[req],area=area,inherited=inherited,
                           source=source,default="من سياق الجلسة" if req=="system" else "بلا قيمة حتى الإدخال أو سياسة معتمدة",
                           validation=validation,
                           condition="حسب نوع الوثيقة وملف الدولة وضوابط الشاشة" if req=="conditional" else ""))
    return result

MASTER="المعرف الداخلي~code~system;الشركة~ref~system;الرمز~code~required;الاسم العربي~text~required;الاسم الإنجليزي~text~optional;فعال~bool~required;ملاحظات~text~optional"
HEAD="المعرف الداخلي~code~system;الشركة~ref~system;الفرع~ref~required;السنة~ref~system;رقم المستند~code~system;تاريخ الوثيقة~date~required;تاريخ الأثر~date~required;العملة~ref~required;سعر الصرف~rate~required;البيان~text~required;المرجع الخارجي~code~optional;الحالة~enum~system"
FILTER="الشركة~ref~system;الفروع المصرح بها~ref~required;من تاريخ~date~required;إلى تاريخ~date~required;عملة العرض~ref~required;طريقة عرض العملات~enum~required;حالة المستندات~enum~required;مستوى التفصيل~enum~required"
AUDIT="أنشأه~ref~system;وقت الإنشاء~time~system;رقم إصدار التعديل~int~system;آخر تعديل~time~system"
TOTALS="الإجمالي قبل الخصم~money~system;إجمالي الخصم~money~system;الوعاء الضريبي~money~system;إجمالي الضريبة~money~system;الإجمالي النهائي~money~system;المبلغ المسدد~money~system;المتبقي~money~system"
screens=[]
modules=[dict(id=i,name=n,scope=s,role=r,purpose=p) for i,n,s,r,p in MODULES]
for row in RAW.strip().splitlines():
    p=row.split("|")
    assert len(p)==9, (len(p),p[0])
    sid,name,kind,purpose,hs,ls,rule,out,links=p
    mid=re.match("[A-Z]+",sid).group()
    mod=next(m for m in modules if m["id"]==mid)
    shared = MASTER if kind=="master" else FILTER if kind=="report" else HEAD if kind in ("transaction","journal","invoice","stock") else "الشركة~ref~system;الفرع~ref~conditional;الحالة~enum~system"
    area="filters" if kind=="report" else "header"
    fs=parse_fields(shared,area,True)+parse_fields(hs,area)+parse_fields(ls,"lines")
    if kind=="invoice":
        fs += parse_fields(TOTALS,"totals",True)
    if kind!="report":
        fs += parse_fields(AUDIT,"audit",True)
    # Expand legal addresses rather than leaving implementers an opaque string.
    for f in list(fs):
        if f["name"]=="العنوان التفصيلي":
            fs.remove(f)
            fs += parse_fields("المدينة~text~"+f["requirement"]+";الحي~text~optional;الشارع~text~"+f["requirement"]+";رقم المبنى~code~conditional;الرمز البريدي~code~conditional;الرقم الإضافي~code~conditional",area)
    seen=set()
    fs=[f for f in fs if not ((f["area"],f["name"]) in seen or seen.add((f["area"],f["name"])))]
    for ix,f in enumerate(fs,1):
        f["id"]=sid+"-F"+str(ix).zfill(2)
        if kind=="stock" and f["name"] in ("العملة","سعر الصرف"):
            f["requirement"]="conditional"; f["requirement_ar"]="شرطي"
            f["condition"]="عند وجود قيمة أو عملة مستند مختلفة؛ لا يطلب سعر بيع في الإذن"
        if f["name"]=="سعر الصرف":
            f["default"]="1 عند تطابق العملة مع الوظيفية؛ وإلا أحدث سعر نافذ مع حفظ نسخته"
        if f["name"]=="فعال":
            f["default"]="نعم للبطاقة الجديدة بعد استيفاء التحقق"
    actions = (
        ["بحث","جديد","حفظ","تعطيل","عرض الاستخدام","سجل التغييرات"] if kind in ("master","setup") else
        ["تصفية","عرض","حفر إلى المصدر","معاينة طباعة","تصدير حسب الصلاحية"] if kind=="report" else
        ["بحث","جديد","حفظ مسودة","تقديم","اعتماد","معاينة الأثر","ترحيل حسب نوع المصدر","طباعة","عكس أو إشعار مرتبط"] if kind in ("transaction","journal","invoice","stock") else
        ["اختيار النطاق","معاينة","تحقق","اعتماد عند وجود أثر","تنفيذ الإجراء","عرض النتيجة","سجل التنفيذ"]
    )
    common = ["BR02","BR18"] if kind=="report" else ["BR02","BR09","BR18"]
    if kind in ("transaction","journal","invoice","stock"):
        common+=["BR01","BR03","BR05","BR06","BR07","BR10"]
    if kind in ("stock","invoice"): common+=["BR12","BR13"]
    if "مرتجع" in name: common+=["BR11"]
    screens.append(dict(id=sid,module=mid,name=name,kind=kind,
        menu=mod["name"]+" ← "+KIND[kind]+" ← "+name,role=mod["role"],scope=mod["scope"],
        purpose=purpose,fields=fs,actions=actions,validation=rule,outputs=out,
        related=links.split(","),rules=common,
        preconditions="شركة ونطاق مصرح؛ بطاقات مرجعية فعالة؛ إعدادات القسم مكتملة"+(" وفترة مفتوحة عند الترحيل" if kind in ("transaction","journal","stock","invoice") else ""),
        evidence="proposed_unverified_ui",
        acceptance_positive="تنفيذ: "+purpose+"؛ يجب أن تكون النتيجة: "+out,
        acceptance_negative="اختبار تجاوز هذا الشرط والتأكد من المنع أو مسار المعالجة الموثق: "+rule))

# Roles in literal screen descriptions are proposed capabilities, not a permission grant.
module_order={m["id"]:i for i,m in enumerate(modules)}
screens.sort(key=lambda s:(module_order[s["module"]],int(re.search(r"\d+",s["id"]).group())))
jsonwrite("data/modules.json",modules)
jsonwrite("data/screens.json",screens)
csvwrite("data/screens.csv",["معرف","قسم","الشاشة","مسار القائمة","نوع","نطاق","مستخدم","هدف","تحقق","مخرجات","ارتباطات","حالة المطابقة"],
 [[s["id"],s["module"],s["name"],s["menu"],s["kind"],s["scope"],s["role"],s["purpose"],s["validation"],s["outputs"],", ".join(s["related"]),s["evidence"]] for s in screens])
csvwrite("data/fields.csv",["الشاشة","معرف الحقل","الحقل","الموضع","النوع","الإلزام","المصدر","الافتراضي","التحقق","شرط الظهور","موروث"],
 [[s["id"],f["id"],f["name"],f["area"],f["type_ar"],f["requirement_ar"],f["source"],f["default"],f["validation"],f["condition"],f["inherited"]] for s in screens for f in s["fields"]])
csvwrite("data/parity-matrix.csv",["معرفنا","اسمنا","اسم شاشة المرجع","إصدار المرجع","مرجع الصفحة أو اللقطة","حالة الدليل","فرق الحقول","فرق السلوك","قرار المعالجة","المسؤول","تاريخ التحقق"],
 [[s["id"],s["name"],"","","","غير متحقق","لم تقارن بإصدار محدد","لم يقارن بإصدار محدد","يلزم دليل أو استعراض","",""] for s in screens])
csvwrite("data/traceability.csv",["متطلب","شاشة","قسم","قواعد مرتبطة","مخرج","اختبار إيجابي","اختبار سلبي"],
 [["REQ-"+s["id"],s["id"],s["module"],", ".join(s["rules"]),s["outputs"],s["acceptance_positive"],s["acceptance_negative"]] for s in screens])

for mod in modules:
    group=[s for s in screens if s["module"]==mod["id"]]
    out=[f'# {mod["name"]}\n',f'النطاق: {mod["scope"]}. المستخدم الرئيسي: {mod["role"]}.\n',
         mod["purpose"]+"\n",f'خريطة الشاشات: [SVG](../../diagrams/svg/module-{mod["id"]}.svg) | [Mermaid](../../diagrams/source/module-{mod["id"]}.mmd).\n',
         "كل التفاصيل التالية تصميم مقترح مستقل؛ التطابق التفصيلي مع إصدار المتكامل لم يُثبت. الحقول الموروثة موسعة لتيسير التنفيذ لاحقًا.\n"]
    for s in group:
        out += [f'## {s["id"]} — {s["name"]}\n',
                f'المسار: {s["menu"]}\n\nالغرض: {s["purpose"]}\n\nالشروط السابقة: {s["preconditions"]}\n',
                '| الحقل | الموضع | النوع | الإلزام | مصدره |\n|---|---|---|---|---|']
        out += [f'| {f["name"]} | {f["area"]} | {f["type_ar"]} | {f["requirement_ar"]} | {f["source"]} |' for f in s["fields"]]
        out += ["\nالأفعال: "+"، ".join(s["actions"])+".\n",
                "**ضابط الشاشة:** "+s["validation"]+".\n",
                "**المخرجات والأثر:** "+s["outputs"]+".\n",
                "قواعد مشتركة: "+", ".join(s["rules"])+". الارتباطات: "+", ".join(s["related"])+".\n",
                "قبول إيجابي: "+s["acceptance_positive"]+".\n",
                "قبول سلبي: "+s["acceptance_negative"]+".\n"]
    write("docs/modules/"+mod["id"]+".md","\n".join(out))

SOURCES=[
("S01","المتكامل بلاس — يمن سوفت","https://yemensoft.com/ar-motakamel-plus-erp-solutions/","مقروء","مرجع المجالات الوظيفية العامة، لا ترتيب الحقول"),
("S02","المتكامل برو — يمن سوفت","https://yemensoft.com/ar-motakamel-pro-system/","مقروء","مجالات النظام التجاري ووظائف عامة"),
("S03","المتكامل الفضي — يمن سوفت","https://yemensoft.com/ar-motakamel-silver-system/","مقروء","تمييز الإصدار"),
("S04","دليل الحسابات V5","https://www.almotakamelplus.com/wp-content/uploads/2019/10/GeneralLedge-motakamelplusV5.pdf","تعذر الجلب","رابط ظهر بالبحث؛ المحتوى غير متحقق"),
("S05","دليل المخزون V5","https://www.almotakamelplus.com/wp-content/uploads/2019/10/StockInventory-motakamelplusV5.pdf","تعذر الجلب","رابط ظهر بالبحث؛ المحتوى غير متحقق"),
("S06","دليل المبيعات V5","https://www.almotakamelplus.com/wp-content/uploads/2019/10/CustomersSales-motakamelplusV5.pdf","تعذر الجلب","رابط ظهر بالبحث؛ المحتوى غير متحقق"),
("S07","السعودية — دليل الفوترة التقني","https://zatca.gov.sa/en/E-Invoicing/Introduction/Guidelines/Documents/E-invoicing-Detailed-Technical-Guideline.pdf","مقروء","الاعتماد والإبلاغ؛ إصدار الدليل الظاهر نوفمبر 2022 ويعاد فحص المواصفات عند التنفيذ"),
("S08","الإمارات — الفوترة الإلكترونية","https://www.tax.gov.ae/en/taxes/Vat/uae.einvoicing.aspx","مقروء","وجود برنامج الفوترة المنظمة"),
("S09","عمان — أسئلة الفوترة الإلكترونية","https://tms.taxoman.gov.om/portal/ar/e-invoicing-faq","مقروء","وجود مسار تطبيق ومتطلبات محلية"),
("S10","البحرين — اللائحة التنفيذية","https://www.lloc.gov.bh/Legislation/HTM/RFNE1218","مقروء جزئيًا","بيانات الفاتورة والتصحيح؛ فحص التعديلات لازم عند التطبيق"),
("S11","اليمن — ضرائب المبيعات","https://tax.gov.ye/salestaxes/","مقروء","وجود قواعد وفواتير وإقرارات؛ الاختصاص الجغرافي يحتاج تحديدًا"),
("S12","قطر — دليل المستثمر","https://gta.gov.qa/ar/investors-guide","مقروء جزئيًا","وضع VAT المعلن وقت الاطلاع"),
]
jsonwrite("data/sources.json",[dict(id=i,title=t,url=u,status=st,usage=use,accessed=DATE) for i,t,u,st,use in SOURCES])
write("references/README.md","# سجل المراجع\n\nالمراجع وروابطها وحالة الاطلاع في [sources.json](../data/sources.json). لم تُنسخ أدلة تجارية أو صور شاشات إلى المشروع. ملفا النطاق والمطابقة يفصلان الدليل المقروء عن الافتراضات. تاريخ البحث 2026-10-20.\n")

# Diagram renderer: native code-authored SVG, portable and independent of a CDN.
ESC=html.escape
diagrams=[]
def text_svg(x,y,value,size=17,color="#18374a",anchor="middle",bold=False,maxchars=29):
    words=textwrap.wrap(value,maxchars,break_long_words=False,break_on_hyphens=False) or [""]
    parts=[]
    # With direction=rtl, "start" is the geometric right edge.
    svg_anchor="start" if anchor=="end" else "end" if anchor=="start" else anchor
    for ix,w in enumerate(words):
        parts.append(f'<text x="{x}" y="{y+ix*(size+6)}" text-anchor="{svg_anchor}" direction="rtl" unicode-bidi="plaintext" fill="{color}" font-size="{size}" font-weight="{700 if bold else 400}">{ESC(w)}</text>')
    return "".join(parts)

def diagram(slug,title,layers,edges,description="",flowtype="flowchart"):
    # layers: each node is (id,label); a layer is arranged horizontally.
    columns=max(len(x) for x in layers)
    w=max(1000, columns*295+80); h=len(layers)*165+150
    positions={}
    pieces=[f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {w} {h}" role="img" aria-label="{ESC(title)}"><title>{ESC(title)}</title><desc>{ESC(description)}</desc>',
    f'<defs><marker id="arrow-{slug}" markerWidth="9" markerHeight="9" refX="8" refY="4" orient="auto"><path d="M0,0 L8,4 L0,8 Z" fill="#6b8b9e"/></marker></defs>',
    f'<rect width="{w}" height="{h}" rx="16" fill="#f4f8fb"/>',
    '<g font-family="Tahoma,Arial,sans-serif">',text_svg(w/2,40,title,24,bold=True,maxchars=100)]
    mmd=["flowchart TB"]
    for iy,layer in enumerate(layers):
        for ix,(nid,label) in enumerate(layer):
            x=(w-len(layer)*295)/2+ix*295+12; y=80+iy*165
            positions[nid]=(x,y,270,103,label)
            mmd.append(f'  {nid}["{label}"]')
    back_index=0
    for a,b,label in edges:
        x,y,bw,bh,_=positions[a]; tx,ty,tw,th,_=positions[b]
        sx=x+bw/2; sy=y+bh; ex=tx+tw/2; ey=ty
        if ty==y:
            sx=x+bw if tx>x else x; ex=tx if tx>x else tx+tw; sy=ey=y+bh/2
            path=f"M {sx} {sy} L {ex} {ey}"
        elif ty<y:
            sx=x; sy=y+bh/2; ex=tx+tw/2; ey=ty
            side=max(10,min(v[0] for v in positions.values())-20-back_index*16)
            back_index+=1
            path=f"M {sx} {sy} L {side} {sy} L {side} {ey-24} L {ex} {ey-24} L {ex} {ey}"
        elif ty-y>170:
            sx=x+bw; sy=y+bh/2; ex=tx+tw/2; ey=ty
            side=min(w-12,max(v[0]+v[2] for v in positions.values())+20)
            path=f"M {sx} {sy} L {side} {sy} L {side} {ey-24} L {ex} {ey-24} L {ex} {ey}"
        else:
            cy=(sy+ey)/2
            path=f"M {sx} {sy} C {sx} {cy}, {ex} {cy}, {ex} {ey}"
        pieces.append(f'<path d="{path}" fill="none" stroke="#6b8b9e" stroke-width="2" marker-end="url(#arrow-{slug})"/>')
        if label:
            lx=(sx+ex)/2; ly=(sy+ey)/2
            if ty==y: ly-=25
            if ty<y: lx=side+60
            if ty-y>170: lx=side-55
            label_width=max(45,len(label)*7+16)
            pieces.append(f'<rect x="{lx-label_width/2}" y="{ly-16}" width="{label_width}" height="25" rx="6" fill="#f4f8fb"/>')
            pieces.append(text_svg(lx,ly+2,label,13))
        mmd.append(f'  {a} -->'+(f'|"{label}"|' if label else "")+f" {b}")
    for nid,(x,y,bw,bh,label) in positions.items():
        pieces.append(f'<rect x="{x}" y="{y}" width="{bw}" height="{bh}" rx="12" fill="white" stroke="#b5cedb" stroke-width="1.5"/>')
        pieces.append(f'<rect x="{x+bw-7}" y="{y+14}" width="4" height="{bh-28}" rx="2" fill="#118a84"/>')
        parts=textwrap.wrap(label,25,break_long_words=False,break_on_hyphens=False)
        pieces.append(text_svg(x+bw/2,y+bh/2-(len(parts)-1)*11+5,label,17,maxchars=25))
    footer="الأسهم لترتيب عناصر القائمة وليست دورة تشغيل • تصميم مقترح 2026-10-20" if slug.startswith("module-") else "تصميم تحليلي مقترح • 2026-10-20 • لا يمثل لقطة من النظام المرجعي"
    pieces.append(text_svg(w/2,h-18,footer,13,color="#647b89",maxchars=120))
    pieces.append("</g></svg>")
    write(f"diagrams/svg/{slug}.svg","".join(pieces))
    write(f"diagrams/source/{slug}.mmd","\n".join(mmd)+"\n")
    diagrams.append(dict(id=slug,title=title,description=description,svg=f"diagrams/svg/{slug}.svg",mermaid=f"diagrams/source/{slug}.mmd"))

for mod in modules:
    ss=[s for s in screens if s["module"]==mod["id"]]
    # A separate chain per category documents navigation, never accounting causality.
    nodes=[[("root",mod["name"])]]
    cats=list(dict.fromkeys(KIND[s["kind"]] for s in ss))
    nodes.append([("cat"+str(ix),cat) for ix,cat in enumerate(cats)])
    edges=[("root","cat"+str(ix),"قائمة") for ix in range(len(cats))]
    maxrows=max(sum(KIND[s["kind"]]==cat for s in ss) for cat in cats)
    for i in range(maxrows):
        layer=[]
        for ci,cat in enumerate(cats):
            subset=[s for s in ss if KIND[s["kind"]]==cat]
            if i<len(subset):
                s=subset[i];layer.append((s["id"],s["id"]+" • "+s["name"]))
                prev="cat"+str(ci) if i==0 else subset[i-1]["id"]
                edges.append((prev,s["id"],"عنصر قائمة" if i==0 else ""))
        nodes.append(layer)
    diagram("module-"+mod["id"],"خريطة شاشات • "+mod["name"],nodes,edges,
            "الخطوط توضح ترتيب التنقل داخل الفئة؛ ليست تسلسلًا محاسبيًا إلزاميًا.")

diagram("system-map","خريطة النظام وحدود الأقسام",[
 [("U","المحاسب والمستخدمون حسب الدور")],
 [("S","التهيئة والصلاحيات والفترات"),("L","ملفات الدولة والضرائب")],
 [("P","المشتريات والموردون والاستيراد"),("I","المخزون والتصنيع"),("A","المبيعات والعملاء ونقاط البيع")],
 [("T","الصناديق والبنوك"),("G","الأستاذ العام ومحرك الترحيل"),("X","الأصول والرواتب والمشروعات")],
 [("R","التقارير والمطابقة والإقفال"),("C","الموازنات ومراكز التكلفة")]],
 [("U","S",""),("S","P",""),("S","I",""),("S","A",""),("L","A","قواعد إصدار"),("P","G","أثر"),("I","G","أثر"),("A","G","أثر"),("T","G","أثر"),("X","G","أثر"),("G","R","دفاتر"),("G","C","تحليل")],
 "كل قسم يمر بضوابط الشركة والصلاحية، والأستاذ العام مصدر القوائم.")
diagram("document-lifecycle","حالات المستند والموافقة",[
 [("D","مسودة")],[("V","مقدم للمراجعة")],
 [("C","معاد للتصحيح"),("A","معتمد"),("E","ملغى قبل الترحيل")],
 [("P","ترحيل ذري وتحقق من الفترة")],
 [("F","فشل دون أثر — يبقى معتمدًا"),("J","مرحل ومحمي من التعديل")],
 [("N","مستند عكس أو إشعار تصحيح مرتبط")]],
 [("D","V","تقديم"),("V","C","رفض مسبب"),("C","D","تصحيح"),("V","A","قبول"),("D","E","إلغاء"),("A","P","صلاحية"),("P","F","تعذر"),("F","A","معالجة السبب"),("P","J","نجاح"),("J","N","تصحيح معتمد")],
 "حالة التنفيذ والتحصيل والإرسال الضريبي مستقلة عن هذه الدورة.")
diagram("purchase-to-pay","دورة الشراء حتى السداد",[
 [("R","طلب شراء واعتماد")],[("Q","مقارنة عروض وأمر شراء")],
 [("G","استلام جزئي أو كامل وفحص"),("D","شراء مباشر بالفاتورة")],
 [("M","مطابقة أمر واستلام وفاتورة")],
 [("H","فرق فوق السماحية: تعليق"),("P","ترحيل فاتورة وتسوية GRNI")],
 [("S","جدولة مستحقات وسند صرف")],[("A","تخصيص السداد وإقفال الذمة")]],
 [("R","Q",""),("Q","G",""),("G","M",""),("D","M","استلام مرة واحدة"),("M","H","فرق"),("M","P","مقبول"),("P","S",""),("S","A","")])
diagram("sale-to-cash","دورة البيع حتى التحصيل",[
 [("C","العميل وعرض السعر")],[("O","أمر بيع وفحص سعر وائتمان")],
 [("H","موافقة استثنائية أو رفض"),("R","حجز كمية متاحة")],
 [("D","تسليم مرتبط"),("I","بيع مباشر مع تسليم")],
 [("F","فاتورة مع أثر مخزون واحد")],
 [("E","قواعد إصدار الدولة"),("P","ذمة وقبض وتخصيص")],
 [("A","متابعة استحقاق ومطابقة")]],
 [("C","O",""),("O","H","تجاوز"),("O","R","مقبول"),("R","D",""),("D","F","دون صرف ثان"),("I","F","صرف مباشر"),("F","E",""),("F","P",""),("P","A","")])
diagram("stock-transfer","تحويل مخزون وجرد",[
 [("S","المخزن المرسل: موجود ومحجوز")],[("T","إرسال وتحويل القيمة إلى الطريق")],
 [("R","استلام جزئي بالوجهة"),("X","عجز أو تلف موثق")],
 [("D","وجهة + طريق متبق = المرسل")],
 [("C","لقطة جرد وعد فعلي")],[("A","مراجعة الحركات بعد القطع")],[("J","اعتماد تسوية كمية وقيمة")]],
 [("S","T",""),("T","R",""),("T","X",""),("R","D",""),("X","D","قرار فرق"),("D","C","دورة لاحقة"),("C","A",""),("A","J","")])
diagram("returns","المرتجعات والتصحيح المالي",[
 [("O","فاتورة أصلية وكمية قابلة للإرجاع")],
 [("F","تصحيح مالي فقط"),("G","مرتجع بضاعة")],
 [("N","إشعار دون مخزون"),("V","فحص الحالة والدفعة والكمية")],
 [("S","صالح: إدخال بتكلفة المصدر"),("Q","تالف: عزل ثم قرار تسوية")],
 [("A","تخفيض الذمة وأثر الضريبة"),("R","رد نقد أو رصيد للعميل")]],
 [("O","F",""),("O","G",""),("F","N",""),("G","V",""),("V","S",""),("V","Q",""),("N","A",""),("S","A",""),("Q","A","قيمة معتمدة"),("A","R","قرار مستقل")])
diagram("period-close","الإقفال والمطابقات",[
 [("D","حصر المستندات المعلقة")],
 [("B","مطابقة البنوك والنقد"),("S","مطابقة الذمم والمخزون والأصول")],
 [("A","تسويات وإهلاك وتكلفة وصرف")],[("T","مطابقة ضريبة وتقارير مالية")],
 [("F","فروق غير مفسرة: منع"),("C","اعتماد وإقفال الفترة")],
 [("Y","نهاية السنة: قيد نتائج معتمد")]],
 [("D","B",""),("D","S",""),("B","A",""),("S","A",""),("A","T",""),("T","F","فشل"),("T","C","نجاح"),("C","Y","عند نهاية السنة")])
diagram("einvoice","الفوترة الإلكترونية: حالات مستقلة",[
 [("D","وثيقة متحققة ومحتوى مثبت")],[("T","تحديد نوع الوثيقة وملف الدولة")],
 [("C","مسار اعتماد قبل المشاركة"),("S","مسار إصدار ثم إبلاغ")],
 [("A","مقبول: مشاركة وحفظ"),("R","مرفوض قبل الإصدار: تصحيح"),("Q","صندوق إرسال دائم")],
 [("F","فشل اتصال: إعادة بنفس الهوية"),("V","استجابة مثبتة وأرشيف")]],
 [("D","T",""),("T","C","حسب النوع"),("T","S","حسب النوع"),("C","A","قبول"),("C","R","رفض"),("S","Q",""),("Q","F","تعذر"),("Q","V","نجاح"),("A","V","")],
 "تصور تصميمي متعدد الدول؛ شروط كل مسار والمهل من المواصفة المحلية المعتمدة.")
diagram("posting-sequence","التكامل والأثر الذري",[
 [("U","طلب ترحيل بهوية منع تكرار")],[("A","تحقق صلاحيات وحالة وفترة")],
 [("L","قفل الموارد وفحص المتاح والذمم")],
 [("J","قيد اليومية"),("S","دفاتر ذمم ومخزون وضريبة"),("O","حدث Outbox")],
 [("C","التزام قاعدة البيانات كوحدة واحدة")],
 [("E","عامل إرسال خارجي قابل للإعادة"),("R","استجابة برقم القيد")]],
 [("U","A",""),("A","L",""),("L","J",""),("L","S",""),("L","O",""),("J","C",""),("S","C",""),("O","C",""),("C","E","بعد الالتزام"),("C","R","")],
 "عند فشل أي كتابة داخل المعاملة تتراجع جميع كتاباتها. الإرسال الخارجي لا يقع داخل قفل المخزون.")
diagram("imports","استيراد وتكلفة شحنة",[
 [("P","عقد شراء وملف شحنة")],[("L","اعتماد أو دفعة مقدمة")],
 [("G","بضاعة واستلام"),("C","شحن وتأمين وجمارك مؤهلة")],
 [("A","توزيع تكلفة ومطابقة إجمالي")],
 [("S","جزء مباع إلى تكلفة المبيعات"),("I","جزء متبق إلى المخزون")],
 [("E","إقفال شحنة وتسوية حساباتها")]],
 [("P","L","حسب العقد"),("P","G",""),("L","C","عمولات مؤهلة"),("G","A",""),("C","A",""),("A","S",""),("A","I",""),("S","E",""),("I","E","")])
diagram("extensions","تكامل الامتدادات مع الحسابات",[
 [("H","موظف وحضور وسياسات"),("M","تركيبة وأمر إنتاج"),("P","عقد وبنود مشروع")],
 [("W","مسير واستحقاق راتب"),("I","صرف خامات واستلام منتج"),("C","تكاليف ومستخلصات")],
 [("G","قيود مرتبطة بالمصدر ومحرك الترحيل")],
 [("R","تقارير وأرصدة ومطابقة")]],
 [("H","W",""),("M","I",""),("P","C",""),("W","G",""),("I","G",""),("C","G",""),("G","R","")])
diagram("opening-migration","نقل الأرصدة وبدء التشغيل",[
 [("M","تعريف الشركات والحسابات والبطاقات")],
 [("A","تفصيل العملاء والموردين"),("I","مخزون وأصول ونقدية")],
 [("G","رصيد عام باستبعاد الرقابات المكررة")],
 [("C","مطابقة الدفاتر وحساب الربط")],
 [("F","فرق: تصحيح الاستيراد"),("P","اتزان: اعتماد تاريخ القطع")]],
 [("M","A",""),("M","I",""),("A","G",""),("I","G",""),("G","C",""),("C","F","فشل"),("C","P","نجاح")])
diagram("data-model","خريطة الكيانات والعلاقات الرئيسية",[
 [("CO","شركة واحدة ← عدة فروع")],
 [("AC","دليل حسابات هرمي"),("DOC","مستند ← عدة سطور"),("ITEM","صنف ← وحدات ودفعات")],
 [("JE","قيد ← سطور إلى الحسابات"),("PARTY","طرف ← فواتير مفتوحة"),("ST","حركات مخزون وطبقات تكلفة")],
 [("AL","تخصيصات سداد للذمم"),("TAX","لقطات ضريبية ووثيقة إلكترونية")],
 [("AUD","اعتمادات وأحداث تدقيق وإرسال")]],
 [("CO","AC","1:N"),("CO","DOC","1:N"),("CO","ITEM","1:N"),("DOC","JE","مصدر أثر"),("DOC","PARTY","طرف"),("DOC","ST","سطور مخزنية"),("ITEM","ST","1:N"),("AC","JE","1:N"),("PARTY","AL","1:N"),("DOC","TAX","1:N"),("TAX","AUD","محاولات"),("JE","AUD","تدقيق")],
 "العلاقات التفصيلية والكاردينالية في أصل Mermaid من نوع ER المرافق.")
# Replace the flow source with the actual conceptual ER; its SVG remains an overview.
write("diagrams/source/data-model.mmd",'''erDiagram
  COMPANY ||--o{ BRANCH : contains
  COMPANY ||--o{ ACCOUNT : owns
  COMPANY ||--o{ FISCAL_PERIOD : defines
  BRANCH ||--o{ WAREHOUSE : contains
  ACCOUNT o|--o{ ACCOUNT : parent
  COMPANY ||--o{ PARTY : owns
  PARTY ||--o{ PARTY_ROLE : has
  COMPANY ||--o{ DOCUMENT : owns
  DOCUMENT ||--|{ DOCUMENT_LINE : contains
  DOCUMENT_LINE ||--o{ LINE_LINK : source
  DOCUMENT_LINE ||--o{ LINE_LINK : target
  PARTY o|--o{ DOCUMENT : counterparty
  DOCUMENT ||--o{ JOURNAL_ENTRY : source
  JOURNAL_ENTRY ||--|{ JOURNAL_LINE : contains
  ACCOUNT ||--o{ JOURNAL_LINE : posted_to
  FISCAL_PERIOD ||--o{ JOURNAL_ENTRY : contains
  COMPANY ||--o{ ITEM : owns
  ITEM ||--|{ ITEM_UNIT : measured_by
  ITEM ||--o{ LOT : tracked_by
  ITEM ||--o{ STOCK_MOVEMENT : moves
  WAREHOUSE ||--o{ STOCK_MOVEMENT : holds
  DOCUMENT_LINE ||--o{ STOCK_MOVEMENT : generates
  STOCK_MOVEMENT ||--o{ COST_ALLOCATION : costed_by
  DOCUMENT ||--o{ OPEN_ITEM : creates
  OPEN_ITEM ||--o{ SETTLEMENT_ALLOCATION : settled_by
  DOCUMENT ||--o{ SETTLEMENT_ALLOCATION : settlement_source
  DOCUMENT_LINE ||--o{ TAX_SNAPSHOT : taxes
  DOCUMENT ||--o| EINVOICE : fiscal_representation
  EINVOICE ||--o{ DELIVERY_ATTEMPT : sent_by
  DOCUMENT ||--o{ APPROVAL : approved_by
  DOCUMENT ||--o{ AUDIT_EVENT : tracked_by
  DOCUMENT ||--o{ OUTBOX_EVENT : publishes
''')
jsonwrite("data/diagrams.json",diagrams)

# Static wireframes intentionally use editable vector elements, no generated bitmap.
def wireframe(slug,title,fields,columns,totals,extra):
    w,h=1400,900
    p=[f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {w} {h}" role="img" aria-label="{ESC(title)}"><title>{ESC(title)}</title><rect width="1400" height="900" fill="#edf3f7"/><g font-family="Tahoma,Arial,sans-serif">',
       '<rect x="24" y="24" width="1352" height="76" rx="12" fill="#15384b"/>',
       text_svg(1340,55,"محاسب • تصميم تخطيطي",20,"white","end",True),
       text_svg(370,56,"الشركة التجريبية | الفرع الرئيسي | 2026",17,"#c9e4ef"),
       '<rect x="1160" y="120" width="216" height="750" rx="12" fill="white"/>']
    for i,t in enumerate(["التهيئة","الحسابات العامة","الصناديق والبنوك","المخازن والأصناف","المبيعات والعملاء","المشتريات والموردون","التقارير"]):
        p.append(text_svg(1348,170+i*66,t,17,anchor="end",bold=i==4))
    p.extend(['<rect x="24" y="120" width="1116" height="750" rx="12" fill="white"/>',text_svg(1108,160,title,24,anchor="end",bold=True)])
    actions=["جديد","بحث","حفظ مسودة","تقديم","اعتماد","معاينة الأثر","طباعة"]
    for i,a in enumerate(actions):
        x=974-i*132
        p.extend([f'<rect x="{x}" y="190" width="120" height="39" rx="6" fill="#e9f4f5" stroke="#bfdbde"/>',text_svg(x+60,215,a,14)])
    for i,f in enumerate(fields):
        col=i%3; row=i//3;x=786-col*360;y=262+row*82
        p.extend([text_svg(x+305,y,f,15,anchor="end"),f'<rect x="{x}" y="{y+10}" width="312" height="36" rx="5" fill="#f8fafc" stroke="#b6cbd7"/>'])
    y=505
    p.extend([f'<rect x="50" y="{y}" width="1050" height="43" fill="#e5f0f5"/>'])
    cw=1050/len(columns)
    for i,c in enumerate(columns):
        x=1100-(i+.5)*cw
        p.append(text_svg(x,y+27,c,14,bold=True))
    for r in range(4):
        yy=y+43+r*36
        p.append(f'<rect x="50" y="{yy}" width="1050" height="36" fill="{("#f7fafc" if r%2==0 else "#fff")}"/><line x1="50" y1="{yy+36}" x2="1100" y2="{yy+36}" stroke="#d9e4eb"/>')
        if r==0:p.append(text_svg(1050,yy+24,"…",17))
    p.append(text_svg(1085,730,extra,15,anchor="end",color="#59717f",maxchars=140))
    for i,t in enumerate(totals):
        x=50+i*(1050/len(totals))
        p.extend([f'<rect x="{x}" y="762" width="{1050/len(totals)-12}" height="65" rx="8" fill="#eff7f6"/>',text_svg(x+(1050/len(totals)-12)/2,789,t,14,bold=True)])
    p.append(text_svg(700,860,"نموذج ترتيب معلومات مقترح — لا يمثل واجهة المتكامل ولا شاشة تشغيل",14,color="#7d5963",maxchars=120))
    p.append("</g></svg>")
    write(f"wireframes/{slug}.svg","".join(p))

wireframe("sales-invoice","SAL06 • فاتورة المبيعات",
 ["رقم الفاتورة / الحالة","تاريخ الوثيقة والأثر","العميل / نوع الفاتورة","المخزن / مرجع التسليم","العملة / سعر الصرف","طريقة الدفع / الاستحقاق","تاريخ التوريد","مرجع أمر البيع","البيان"],
 ["الصنف","الوحدة","الكمية","مجاني","السعر","الخصم","الضريبة","الصافي"],
 ["قبل الضريبة","الخصم","الضريبة","الإجمالي","المتبقي"],"تبويبات: القيد • حركة المخزون • الدفعات • الفوترة • سجل الاعتماد")
wireframe("journal","GL03 • قيد اليومية",
 ["رقم القيد / الحالة","تاريخ الأثر","نوع القيد","الفرع","المرجع الخارجي","البيان","قيد عكسي؟","تاريخ العكس","مرفقات"],
 ["الحساب","البيان","العملة","السعر","مدين","دائن","المركز","المشروع"],
 ["إجمالي المدين","إجمالي الدائن","الفرق = صفر"],"المبالغ الإجمالية بالعملة الوظيفية • القيد غير المتزن لا يرحل")
wireframe("receipt","CASH03 • سند قبض",
 ["رقم السند","التاريخ / الفرع","الدافع","طريقة القبض","الصندوق / البنك","المبلغ","العملة / السعر","مرجع الشيك عند اللزوم","البيان"],
 ["الفاتورة","تاريخها","الاستحقاق","المفتوح","المخصص","فرق الصرف"],
 ["المقبوض","المخصص","غير المخصص"],"مرجع الشيك وبنكه واستحقاقه يظهران حسب طريقة القبض")
wireframe("item-card","INV04 • بطاقة الصنف",
 ["رمز الصنف","الاسم العربي","الاسم الإنجليزي","نوع الصنف / المجموعة","الوحدة الأساسية","طريقة التتبع","رمز الضريبة","حد إعادة الطلب","حالة الصنف"],
 ["الوحدة","معامل التحويل","الباركود","سعر البيع","عملة السعر"],
 ["بيانات أساسية","حسابات الربط","دفعات وتسلسل"],"معامل الوحدة خاص بهذا الصنف • التكلفة تظهر بصلاحية مستقلة")
wireframe("chart-of-accounts","GL01 • دليل الحسابات",
 ["رقم الحساب","اسم الحساب","الحساب الأب","التصنيف","رئيسي / فرعي","الطبيعة","عملة الحساب","حساب رقابة؟","إلزام مركز تكلفة؟"],
 ["المستوى","رقم الحساب","الاسم","التصنيف","نوع الحساب","فعال"],
 ["الأصول","الالتزامات والحقوق","الإيرادات والمصروفات"],"الجدول يمثل الشجرة القابلة للتوسيع • الحركة على حساب فرعي فقط")
wireframe("trial-balance","RPT01 • ميزان المراجعة",
 ["الشركة / الفروع","من تاريخ","إلى تاريخ","مستوى الدليل","مركز التكلفة","عملة العرض","مستندات مرحلة","إظهار الصفري","مقارنة"],
 ["الحساب","الاسم","أول مدين","أول دائن","حركة مدين","حركة دائن","آخر مدين","آخر دائن"],
 ["إجمالي المدين","إجمالي الدائن","فرق الاتزان"],"النقر على حساب يفتح الأستاذ ثم القيد ثم مستند المصدر")

# Minimal safe Markdown rendering for an offline report. Source Markdown remains canonical.
def inline(s):
    s=ESC(s)
    s=re.sub(r"\[([^\]]+)\]\(([^)]+)\)",lambda m:f'<a href="{m.group(2)}">{m.group(1)}</a>',s)
    s=re.sub(r"\*\*(.+?)\*\*",r"<strong>\1</strong>",s)
    s=re.sub(r"`([^`]+)`",r"<code>\1</code>",s)
    return s

def markdown(md):
    out=[]; lines=md.splitlines(); i=0; inlist=False
    while i<len(lines):
        ln=lines[i]
        if ln.startswith("|"):
            if inlist:out.append("</ul>");inlist=False
            tbl=[]
            while i<len(lines) and lines[i].startswith("|"):
                if not re.fullmatch(r"[\s|:\-]+",lines[i]):
                    tbl.append([x.strip() for x in lines[i].strip("|").split("|")])
                i+=1
            out.append('<div class="table-wrap"><table>')
            for ri,row in enumerate(tbl):
                tag="th" if ri==0 else "td"
                out.append("<tr>"+"".join(f"<{tag}>{inline(x)}</{tag}>" for x in row)+"</tr>")
            out.append("</table></div>");continue
        if ln.startswith("#"):
            if inlist:out.append("</ul>");inlist=False
            n=len(ln)-len(ln.lstrip("#"))
            out.append(f"<h{min(n+1,6)}>{inline(ln[n:].strip())}</h{min(n+1,6)}>")
        elif re.match(r"^(- |\d+\. )",ln):
            if not inlist:out.append("<ul>");inlist=True
            out.append("<li>"+inline(re.sub(r"^(- |\d+\. )","",ln))+"</li>")
        elif ln.strip():
            if inlist:out.append("</ul>");inlist=False
            out.append("<p>"+inline(ln)+"</p>")
        else:
            if inlist:out.append("</ul>");inlist=False
        i+=1
    if inlist:out.append("</ul>")
    return "\n".join(out)

css="""
:root{--nav:#15384b;--ink:#18374a;--green:#087c78;--line:#d9e4eb}
*{box-sizing:border-box}body{margin:0;background:#f2f6f9;color:var(--ink);font:16px/1.85 Tahoma,Arial,sans-serif}
header{background:var(--nav);color:white;padding:46px max(5vw,24px)}header .eyebrow{color:#88d6cb;letter-spacing:1px;font-size:14px}
h1{font-size:36px;margin:8px 0}header p{max-width:950px;color:#d8e7ee}
nav{position:sticky;top:0;background:#fff;z-index:20;border-bottom:1px solid var(--line);padding:12px 5vw;display:flex;gap:20px;flex-wrap:wrap}
a{color:#087875;text-decoration:none}nav a{font-weight:bold}main{max-width:1440px;margin:auto;padding:28px}
section{margin-bottom:35px}.panel,article{background:white;border:1px solid var(--line);border-radius:13px;padding:24px;margin:15px 0}
.stats{display:grid;grid-template-columns:repeat(4,1fr);gap:16px}.stat{background:white;border-radius:12px;border:1px solid var(--line);padding:20px}.stat b{display:block;color:var(--green);font-size:34px}
h2{font-size:26px}h3{font-size:22px}.muted{color:#607782;font-size:14px}.note{border-right:5px solid #d09a43;background:#fff9ef;padding:18px;border-radius:8px}
details{background:white;border:1px solid var(--line);border-radius:10px;margin:12px 0;scroll-margin-top:100px}summary{cursor:pointer;padding:17px 20px;font-weight:bold}details .body{padding:0 22px 24px}
details[open]>summary{border-bottom:1px solid #e8eef2;margin-bottom:18px}.table-wrap{overflow:auto}
table{width:100%;border-collapse:collapse;font-size:14px;margin:16px 0}td,th{text-align:right;vertical-align:top;border:1px solid #dce6eb;padding:10px 12px}th{background:#edf5f7}tr:nth-child(even){background:#fafcfd}
.toolbar{display:flex;gap:12px;flex-wrap:wrap;align-items:center}.toolbar input{flex:1;min-width:250px}
input,select,button{font:inherit;padding:10px 15px;border:1px solid #b6cbd7;border-radius:8px;background:white;color:var(--ink)}button{cursor:pointer;background:var(--green);color:white;border:0}
.badge{display:inline-block;background:#e8f5f2;color:#086e69;border-radius:20px;padding:2px 12px;font-size:12px;margin-left:8px}.screen-head{display:flex;gap:10px;align-items:center;flex-wrap:wrap}
.diagram{overflow:auto}.diagram svg{min-width:850px;max-width:100%;height:auto;display:block;margin:auto}.diagram-tall svg{max-height:none}
.wire svg{width:100%;min-width:900px}.links{display:flex;flex-wrap:wrap;gap:15px;margin:10px 0}code{direction:ltr;unicode-bidi:isolate;background:#f0f4f7;padding:2px 6px}
.empty{padding:25px;display:none}.hidden{display:none!important}footer{padding:30px;text-align:center;color:#617986}
@media(max-width:700px){main{padding:12px}.stats{grid-template-columns:repeat(2,1fr)}h1{font-size:27px}nav{gap:12px;font-size:13px}.panel,article{padding:15px}}
@media print{nav,.toolbar,button,.download{display:none!important}header{padding:15px;color:#18374a;background:white}.stats{grid-template-columns:repeat(4,1fr)}main{padding:0;max-width:none}details{break-inside:auto}.diagram svg{min-width:0;width:100%}.hidden{display:none!important}td,th{font-size:10px}h2,h3{break-after:avoid}.note{background:white}article{border:0}a{color:inherit}}
"""
field_count=sum(len(s["fields"]) for s in screens)
page=[f'<!doctype html><html lang="ar" dir="rtl"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>محاسب — التحليل الوظيفي والمخططات</title><style>{css}</style></head><body>',
 '<header><div class="eyebrow">حزمة تحليل قبل البرمجة • الإصدار 1.0</div><h1>نظام محاسبي لليمن والخليج</h1><p>خرائط الأقسام والشاشات، مدخلات المستندات، الدورات المحاسبية، نموذج البيانات، الصلاحيات والتوطين. تصميم وظيفي مستقل يستهدف تجربة مألوفة لمستخدمي المتكامل.</p><p>2026-10-20 · تقرير محلي يعمل دون إنترنت</p></header>',
 '<nav><a href="#overview">النطاق</a><a href="#diagrams">المخططات</a><a href="#catalog">دليل الشاشات</a><a href="#wireframes">تصميم الشاشات</a><a href="#documents">التحليل التفصيلي</a><a href="#sources">المراجع</a></nav><main>',
 '<section id="overview"><div class="stats">'+
 "".join(f'<div class="stat"><b>{v}</b>{k}</div>' for v,k in [(len(modules),"قسمًا وظيفيًا"),(len(screens),"شاشة ووظيفة"),(field_count,"حقلًا ضمن مواصفات الشاشات"),(len(diagrams),"مخططًا")])+
 '</div><div class="note"><strong>المطابقة مع المتكامل:</strong> المصادر العامة تدعم المجالات الوظيفية الرئيسية. تفاصيل هذه الشاشات مقترحة؛ لم يحدد إصدار المرجع ولم تثبت مطابقة ترتيب حقوله أو واجهاته. سجل الفجوات والمراجع مرفق، وأدلة V5 التي تعذر فتحها لا تستخدم دليلًا على التفاصيل.</div>',
 '<div class="panel"><h2>كيف تقرأ الحزمة</h2><p>ابدأ بخريطة النظام، ثم خريطة القسم، ثم ابحث عن الشاشة لعرض مدخلاتها وضوابطها وأثرها. النماذج التخطيطية توضح ترتيب المعلومات. ملفات Markdown وMermaid وJSON وCSV متاحة للمراجعة والتنفيذ لاحقًا.</p><div class="links"><a href="README.md">دليل المشروع</a><a href="diagrams-atlas.pdf">أطلس المخططات PDF</a><a href="data/screens.csv">جدول الشاشات CSV</a><a href="data/fields.csv">قاموس الحقول CSV</a><a href="data/parity-matrix.csv">مصفوفة المطابقة</a><a href="data/traceability.csv">تتبع المتطلبات</a></div></div></section>',
 '<section id="diagrams"><h2>المخططات المرئية</h2><p class="muted">خرائط الأقسام تصف التنقل؛ مخططات الدورات تصف سير العمل. افتح المخطط أو ملف SVG لتكبيره. أصل نموذج البيانات Mermaid من نوع ER، والرسم المعروض يلخص علاقاته.</p>']
for d in sorted(diagrams,key=lambda d:d["id"].startswith("module-")):
    page.append(f'<details id="{d["id"]}"'+(" open" if d["id"]=="system-map" else "")+f'><summary>{ESC(d["title"])}</summary><div class="body"><p>{ESC(d["description"])}</p><div class="links"><a href="{d["svg"]}">فتح SVG</a><a href="{d["mermaid"]}">أصل Mermaid</a></div><div class="diagram">{(ROOT/d["svg"]).read_text()}</div></div></details>')
page.append('</section><section id="catalog"><h2>دليل الشاشات والمدخلات</h2><div class="toolbar"><input id="search" type="search" placeholder="ابحث بالاسم، الحقل، أو المعرف مثل SAL06" aria-label="البحث في دليل الشاشات"><select id="module" aria-label="تصفية القسم"><option value="">جميع الأقسام</option>')
page.extend(f'<option value="{m["id"]}">{ESC(m["name"])}</option>' for m in modules)
page.append('</select><select id="scope" aria-label="تصفية النطاق"><option value="">كل مستويات النطاق</option><option>أساسي</option><option>توسع</option><option>امتداد</option></select><button id="expand">فتح النتائج</button><button id="collapse">طي النتائج</button></div><p id="count" class="muted"></p><p class="muted">العدد يشمل حقولًا مشتركة متكررة بين الشاشات، وليس عدد مفاهيم البيانات الفريدة. الشرط التفصيلي لكل حقل يراجع مع ضابط الشاشة وملف الدولة.</p><div id="screens">')
for s in screens:
    search=" ".join([s["id"],s["name"],s["purpose"]]+[f["name"] for f in s["fields"]])
    page.append(f'<details class="screen" id="{s["id"]}" data-module="{s["module"]}" data-scope="{ESC(s["scope"])}" data-search="{ESC(search)}"><summary><span class="badge">{s["id"]}</span>{ESC(s["name"])} <span class="muted">· {ESC(s["scope"])} · {len(s["fields"])} حقلًا</span></summary><div class="body"><p class="muted">{ESC(s["menu"])}</p><p><strong>الغرض:</strong> {ESC(s["purpose"])}. <strong>المستخدم:</strong> {ESC(s["role"])}.</p><p><strong>الشروط:</strong> {ESC(s["preconditions"])}</p><div class="table-wrap"><table><tr><th>الحقل</th><th>الموضع</th><th>النوع</th><th>الإلزام</th><th>المصدر / الافتراضي / الشرط</th></tr>')
    for f in s["fields"]:
        a={"header":"الرأس","lines":"السطور","filters":"مرشحات","totals":"إجماليات","audit":"تدقيق"}[f["area"]]
        page.append("<tr>"+"".join(f"<td>{ESC(x)}</td>" for x in [f["name"],a,f["type_ar"],f["requirement_ar"],f["source"]+"؛ "+f["default"]+("؛ "+f["condition"] if f["condition"] else "")])+"</tr>")
    page.append(f'</table></div><p><strong>الأفعال:</strong> {ESC(" • ".join(s["actions"]))}</p><p class="note"><strong>التحقق الخاص:</strong> {ESC(s["validation"])}</p><p><strong>المخرجات والأثر:</strong> {ESC(s["outputs"])}</p><p><strong>قواعد مشتركة:</strong> {", ".join(s["rules"])}</p><p><strong>قبول إيجابي:</strong> {ESC(s["acceptance_positive"])}</p><p><strong>قبول سلبي:</strong> {ESC(s["acceptance_negative"])}</p><div class="links"><span>شاشات مرتبطة:</span>'+
                "".join(f'<a class="screen-link" href="#{r}">{r}</a>' for r in s["related"])+
                f'<a href="docs/modules/{s["module"]}.md">ملف القسم الكامل</a></div></div></details>')
page.append('</div><div id="empty" class="empty">لا توجد نتائج مطابقة. جرّب كلمة أقصر أو أزل مرشح القسم.</div></section><section id="wireframes"><h2>نماذج ترتيب الشاشات</h2><p>ستة نماذج ثابتة أصلية للاتفاق على توزيع المعلومات؛ التفاصيل الوظيفية مرجعها دليل الحقول.</p>')
for f in sorted((ROOT/"wireframes").glob("*.svg")):
    title=ET.parse(f).getroot().find("{http://www.w3.org/2000/svg}title").text
    page.append(f'<details><summary>{ESC(title)}</summary><div class="body"><a href="wireframes/{f.name}">فتح الرسم بالحجم الكامل</a><div class="diagram wire">{f.read_text()}</div></div></details>')
page.append('</section><section id="documents"><h2>التحليل التفصيلي</h2>')
for f in sorted((ROOT/"docs").glob("[0-9]*.md")):
    md=f.read_text(); title=md.splitlines()[0].lstrip("# ")
    page.append(f'<details id="doc-{f.stem}"><summary>{ESC(title)}</summary><article><a href="docs/{f.name}">ملف Markdown</a>{markdown(md)}</article></details>')
page.append('</section><section id="sources"><h2>المراجع ودرجة التحقق</h2><div class="table-wrap"><table><tr><th>المعرف</th><th>المرجع</th><th>حالة الاطلاع</th><th>الاستخدام وحدوده</th></tr>')
for i,t,u,st,use in SOURCES:
    page.append(f'<tr><td>{i}</td><td><a href="{ESC(u)}">{ESC(t)}</a></td><td>{ESC(st)}</td><td>{ESC(use)}</td></tr>')
page.append('</table></div></section></main><footer>حزمة تحليل 1.0 · تفاصيل مقترحة قبل التنفيذ · لا توجد بيانات تشغيل حقيقية</footer>')
page.append(r"""<script>
const q=document.querySelector('#search'), mod=document.querySelector('#module'), scope=document.querySelector('#scope');
const cards=[...document.querySelectorAll('.screen')];
const norm=s=>s.toLowerCase().normalize('NFKD').replace(/[\u064B-\u065F\u0670]/g,'').replace(/[أإآ]/g,'ا').replace(/ى/g,'ي');
function filter(){const term=norm(q.value);let n=0;cards.forEach(c=>{const ok=(!mod.value||c.dataset.module===mod.value)&&(!scope.value||c.dataset.scope.startsWith(scope.value))&&norm(c.dataset.search).includes(term);c.classList.toggle('hidden',!ok);if(ok)n++;});document.querySelector('#count').textContent=`عرض ${n} من ${cards.length} شاشة ووظيفة`;document.querySelector('#empty').style.display=n?'none':'block';}
[q,mod,scope].forEach(e=>e.addEventListener('input',filter));
document.querySelector('#expand').onclick=()=>cards.filter(c=>!c.classList.contains('hidden')).forEach(c=>c.open=true);
document.querySelector('#collapse').onclick=()=>cards.forEach(c=>c.open=false);
function openHash(){const id=decodeURIComponent(location.hash.slice(1));const el=document.getElementById(id);if(el&&el.tagName==='DETAILS'){if(el.classList.contains('screen')){q.value='';mod.value='';scope.value='';filter();}el.open=true;requestAnimationFrame(()=>el.scrollIntoView({block:'start'}));}}
window.addEventListener('hashchange',openHash);filter();openHash();
</script></body></html>""")
write("index.html","\n".join(page))

readme=f"""# محاسب — تحليل نظام محاسبي لليمن والخليج

**ابدأ بفتح [التقرير المرئي المحلي](index.html)** في المتصفح. يعمل دون إنترنت أو تثبيت أو خادم؛ يمكن البحث عن الشاشات وعرض الرسومات والحقول من الملف نفسه.

إصدار التحليل 1.0 — {DATE}. هذه مرحلة تحليل وتصميم، ولم يُنشأ تطبيق تشغيل أو قاعدة بيانات إنتاج.

- **{len(modules)} قسمًا**، **{len(screens)} شاشة ووظيفة**، **{field_count} حقلًا عبر الشاشات**؛ العدد يتضمن الحقول المشتركة المتكررة.
- **{len(diagrams)} مخططًا** بملفات SVG مرئية وأصول Mermaid قابلة للتحرير.
- **6 نماذج تخطيطية** للشاشات المحاسبية الأساسية.
- **30 سيناريو قبول جوهريًا**، ومصفوفة تتبع صف لكل شاشة مع قبول إيجابي وسلبي.
- ملفات CSV بترميز UTF-8 مع BOM للفتح في الجداول، وJSON منظم.

## حد المطابقة

الافتراض أن المقصود عائلة المتكامل من يمن سوفت. المصادر الرسمية تثبت المجالات الوظيفية العامة؛ **لا ندعي مطابقة تفصيلية لإصدار لم يحدد ولم يفحص**. تفاصيل الحقول والتصميم سياسة مقترحة مستقلة. انظر [سجل المطابقة](docs/06-reference-parity.md).

## خريطة الملفات

| المسار | المحتوى |
|---|---|
| [index.html](index.html) | التقرير المرئي العربي القابل للبحث؛ SVG مضمّن دون CDN |
| [diagrams-atlas.pdf](diagrams-atlas.pdf) | أطلس مرئي: 28 مخططًا و6 نماذج؛ صفحات بمقاسات الرسومات للتكبير |
| [النطاق والقرارات](docs/01-scope-and-decisions.md) | النطاق، الافتراضات، وحدود الامتدادات |
| [قواعد المحاسبة](docs/02-accounting-and-business-rules.md) | القيود والترحيل والعملات والتكلفة والمرتجعات |
| [عقد الشاشة](docs/03-screen-contract-and-ux.md) | تجربة المستخدم والحقول المشتركة والاختصارات المقترحة |
| [التوطين](docs/04-localization.md) | اليمن ودول الخليج الست، مع بيان ما لم يتحقق |
| [البيانات والرقابة](docs/05-data-and-controls.md) | الكيانات والصلاحيات والنقل والأداء |
| [المطابقة](docs/06-reference-parity.md) | الأدلة والفجوات وخطوات المقارنة |
| [الدورات والقبول](docs/07-workflows-and-acceptance.md) | سيناريوهات القبول المستقبلية |
| [التقارير والمراحل](docs/08-reports-and-roadmap.md) | تعريف التقارير وبوابات التنفيذ |
| docs/modules/ | ملف تفصيلي لكل قسم وحقول كل شاشة |
| data/screens.json وdata/fields.csv | دليل الشاشات وقاموس الحقول |
| data/traceability.csv | ربط المتطلبات بالشاشة والقواعد والمخرجات والقبول |
| data/parity-matrix.csv | مصفوفة مقارنة جاهزة لاستكمال أدلة الإصدار |
| diagrams/source/ وdiagrams/svg/ | أصول المخططات والرسومات المحلية |
| wireframes/ | النماذج التخطيطية الستة |
| scripts/ | مصادر البيانات ومولد الوثائق والتحقق |

## إعادة توليد الحزمة

يتطلب Python 3 من المكتبة القياسية فقط:

```bash
python3 scripts/build.py
python3 scripts/export_pdf.py
python3 scripts/validate.py
```

تصدير PDF اختياري عند إعادة البناء ويتطلب الأداة المحلية `rsvg-convert`؛ نسخة PDF جاهزة مرفقة في الحزمة. الملفات في `docs/01...08` و`scripts/catalog.py` مصادر تحرير. ملفات الأقسام وCSV وJSON وHTML والرسومات مولدة؛ يعدل مصدرها أولًا ثم يعاد البناء. الفحص يتحقق من اتساق الحزمة، ولا يعني اختبار النظام المحاسبي المستقبلي.

## الأقسام

"""+"\n".join(f'- [{m["name"]}](docs/modules/{m["id"]}.md) — {m["scope"]}' for m in modules)+"\n"
write("README.md",readme)
print(json.dumps(dict(modules=len(modules),screens=len(screens),field_occurrences=field_count,diagrams=len(diagrams),wireframes=6),ensure_ascii=False))
