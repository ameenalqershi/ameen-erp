#!/usr/bin/env python3
"""Export the native vector diagrams into a multi-page PDF using librsvg."""
from pathlib import Path
import json
import shutil
import subprocess

root=Path(__file__).resolve().parents[1]
exe=shutil.which("rsvg-convert")
if not exe:
    raise SystemExit("rsvg-convert غير مثبت؛ ملفات SVG والتقرير المحلي جاهزان دون هذه الأداة.")
diagrams=json.loads((root/"data/diagrams.json").read_text())
ordered=sorted(diagrams,key=lambda d:(d["id"]!="system-map",d["id"].startswith("module-")))
files=[root/d["svg"] for d in ordered]+sorted((root/"wireframes").glob("*.svg"))
subprocess.run([exe,"--format","pdf","--output",str(root/"diagrams-atlas.pdf"),*[str(f) for f in files]],check=True)
index=[dict(page=i+1,file=str(f.relative_to(root))) for i,f in enumerate(files)]
(root/"data/pdf-pages.json").write_text(json.dumps(index,ensure_ascii=False,indent=2),encoding="utf-8")
print(f"PDF pages: {len(files)}")
