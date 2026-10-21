#!/usr/bin/env python3
"""Render the handoff inventory from tracked traceability and verification evidence."""
import argparse
import csv
import json
from collections import Counter
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
DOCS = ROOT / 'docs/backend'
START = '<!-- generated-progress:start -->'
END = '<!-- generated-progress:end -->'


def rows(name):
    with (DOCS / name).open(encoding='utf-8', newline='') as stream:
        return list(csv.DictReader(stream))


def cell(value):
    return str(value).replace('|', '&#124;').replace('\n', '<br>')


def table(headers, values):
    return '\n'.join(['| ' + ' | '.join(headers) + ' |', '| ' + ' | '.join('---' for _ in headers) + ' |'] +
                     ['| ' + ' | '.join(cell(v) for v in row) + ' |' for row in values])


def generate():
    screens = rows('screens-implementation.csv')
    fields = rows('fields-implementation.csv')
    diagrams = rows('diagrams-implementation.csv')
    rules = rows('rules-implementation.csv')
    evidence = json.loads((DOCS / 'verification-results.json').read_text())
    assert len(screens) == 150 and len(fields) == 2759 and len(diagrams) == 28 and len(rules) == 20
    counts = Counter(r['status'] for r in screens)
    sections = ['## ملخص آلي من الأدلة المسجلة',
                f"الشاشات: {len(screens)}؛ منفذة جزئيًا: {counts['partial']}؛ لم تبدأ: {counts['not-started']}؛ مكتملة: {counts['complete']}. الحقول: {len(fields)}؛ المخططات: {len(diagrams)}؛ القواعد: {len(rules)}. لا يمثل ذلك نسبة اكتمال مالي أو تشغيلي.",
                f"آخر تقرير محفوظ: `{evidence['finishedAtUtc']}`؛ النتيجة `{evidence['result']}`؛ {evidence['passed']}/{evidence['total']}، فشل {evidence['failed']}؛ SQL حي مطلوب: `{evidence['liveSqlRequested']}`. بصمة المصادر: `{evidence['sourceSha256']}`. هذه نتيجة التقرير المحفوظ وليست فحصًا جديدًا للشجرة الحالية.",
                '### حصيلة الأقسام']
    modules = list(dict.fromkeys(r['module'] for r in screens))
    sections.append(table(['القسم', 'الشاشات', 'جزئي', 'لم يبدأ'], [
        [m, sum(r['module'] == m for r in screens), sum(r['module'] == m and r['status'] == 'partial' for r in screens), sum(r['module'] == m and r['status'] == 'not-started' for r in screens)] for m in modules]))
    sections += ['### الحقول حسب حالة الربط', table(['الحالة', 'العدد'], sorted(Counter(r['status'] for r in fields).items())),
                 'implemented يعني ربط الحقل بعقد؛ audit يعني وجوده في التدقيق؛ derived مشتق؛ partial جزئي؛ pending غير منفذ. راجع fields-implementation.csv لمعرفة اسم كل حقل وعقده.']
    for m in modules:
        sections += [f'### شاشات {m}', table(['الشاشة', 'الاسم', 'الحالة', 'المنفذ', 'المتبقي'], [
            [r['screen_id'], r['name'], r['status'], r['implemented_scope'] or '—', r['remaining']] for r in screens if r['module'] == m])]
    sections += ['### المخططات الـ28', table(['المخطط', 'الحالة', 'حدود التنفيذ'], [
        [f"[{r['diagram_id']}](../../{r['analysis_source']})", r['status'], r['scope']] for r in diagrams]),
                 '### قواعد الأعمال الـ20', table(['القاعدة', 'النطاق', 'المنفذ والمتبقي', 'الدليل'], [
        [r['rule_id'], r['scope'], r['implemented_and_remaining'], r['evidence']] for r in rules])]
    return '\n\n'.join(sections)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--check', action='store_true')
    args = parser.parse_args()
    path = DOCS / 'PROGRESS.md'
    current = path.read_text()
    assert current.count(START) == 1 and current.count(END) == 1
    before, rest = current.split(START)
    _, after = rest.split(END)
    result = before + START + '\n\n' + generate() + '\n\n' + END + after
    if args.check:
        if current != result:
            raise SystemExit('Stale PROGRESS.md; run python3 backend/scripts/progress.py')
        print('PASS: handoff inventory matches 150 screens, 2759 fields, 28 diagrams, 20 rules and saved verification.')
    else:
        path.write_text(result)
        print('Updated docs/backend/PROGRESS.md')


if __name__ == '__main__':
    main()
