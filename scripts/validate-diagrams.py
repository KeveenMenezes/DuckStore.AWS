#!/usr/bin/env python3
"""Validate docs/duckstore-backend-improved.drawio against the code that it claims to describe.

The diagrams drift silently: an ADR gets implemented in infra/ and nobody reopens the .drawio, so a
page keeps showing a Lambda name that was renamed or a rule that no longer exists. Everything here
is a check that caught a real defect at least once.

    python3 scripts/validate-diagrams.py              # every check
    python3 scripts/validate-diagrams.py --page Pricing
    python3 scripts/validate-diagrams.py --only content

Groups: structure | content | consistency | docs
Exit code 1 if anything failed.
"""
from __future__ import annotations

import argparse
import glob
import html
import os
import re
import sys
import xml.etree.ElementTree as ET
from collections import Counter, defaultdict

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DRAWIO = os.path.join(ROOT, 'docs', 'duckstore-backend-improved.drawio')

# House style, derived from what the majority of pages already do.
FONT_ROLE = {'page title': {20, 26}, 'legend': {12, 13}, 'AWS Cloud label': {13, 15},
             'edge label': {11}, 'group box': {11, 12, 13}}
# Canonical legend order: sync -> saga -> event -> image pipeline -> failure -> support
LEGEND_ORDER = ['#232F3E', '#8C4FFF', '#1A7F5A', '#2266C4', '#C0392B', '#879196']
PT = re.compile(r'[áàâãéêíóôõúçÁÀÂÃÉÊÍÓÔÕÚÇ]')
EVENTS = ['CatalogViewProductSynced', 'CatalogViewProductDeleted', 'CatalogCategorySync',
          'ProductDiscountChanged', 'ProductCreated', 'ProductUpdated', 'ProductDeleted',
          'ProductSynced', 'ReviewCreated', 'ReviewUpdated', 'BasketCheckout', 'PaymentAuthorized',
          'PaymentDeclined', 'PaymentRequested', 'PriceChanged', 'OrderCreated',
          'ChallengeAnswered', 'PointsRedeemed']
NOT_INTEGRATION_EVENTS = {'SQSEvent', 'DynamoDBEvent', 'S3Event'}
BARE_EVENT = re.compile(r'\b(' + '|'.join(EVENTS) + r')\b(?!Event)')

fails: list[str] = []
checks = 0


def fail(group: str, page: str, msg: str) -> None:
    fails.append(f'  [{group}] {page}: {msg}')


def plain(cell) -> str:
    v = cell.get('value') or ''
    return re.sub(r'\s+', ' ', re.sub(r'<[^>]+>', ' ', html.unescape(v))).strip()


def joined(cell) -> str:
    """<br> removed, so a kebab-case name split across two lines rejoins."""
    v = html.unescape(cell.get('value') or '')
    return re.sub(r'<[^>]+>', ' ', re.sub(r'<br\s*/?>', '', v))


def spaced(cell) -> str:
    """<br> becomes a space - stops two CamelCase event names gluing into one token."""
    v = html.unescape(cell.get('value') or '')
    return re.sub(r'<[^>]+>', ' ', re.sub(r'<br\s*/?>', ' ', v))


def kind(style: str) -> str | None:
    for pat, k in (('resIcon=mxgraph.aws4.lambda', 'LAMBDA'),
                   ('resIcon=mxgraph.aws4.dynamodb', 'TABLE'), ('dynamodb_stream', 'STREAM'),
                   ('resIcon=mxgraph.aws4.eventbridge', 'BUS'),
                   ('resIcon=mxgraph.aws4.appsync', 'APPSYNC'), ('step_functions', 'SAGA'),
                   ('resIcon=mxgraph.aws4.sqs', 'SQS'), ('aws4.alarm', 'ALARM'),
                   ('resIcon=mxgraph.aws4.sns', 'SNS'), ('aws4.bucket', 'S3'),
                   ('aws4.client', 'CLIENT')):
        if pat in style:
            return k
    if 'aws4.group' in style:
        return 'CLOUD'
    if 'dashPattern' in style:
        return 'BOX'
    return None


def geo(cell):
    g = cell.find('mxGeometry')
    if g is None or g.get('x') is None or g.get('y') is None:
        return None
    try:
        return (float(g.get('x')), float(g.get('y')),
                float(g.get('width') or 0), float(g.get('height') or 0))
    except (TypeError, ValueError):
        return None


# --------------------------------------------------------------------------- ground truth
def load_infra():
    src = {}
    for f in glob.glob(os.path.join(ROOT, 'infra', 'constructs', '*.ts')) + \
             glob.glob(os.path.join(ROOT, 'infra', 'stacks', '*.ts')):
        src[os.path.basename(f)] = open(f, encoding='utf-8').read()
    blob = ' '.join(src.values())
    tables = {}
    for s in src.values():
        # Walk each `new dynamodb.Table(` block by brace matching. A non-greedy regex stops at the
        # first nested `});` and silently reports "no stream" for tables that have one.
        for m in re.finditer(r'new dynamodb\.Table\(', s):
            i, depth = m.end() - 1, 0
            while i < len(s):
                if s[i] == '(':
                    depth += 1
                elif s[i] == ')':
                    depth -= 1
                    if depth == 0:
                        break
                i += 1
            body = s[m.end():i]
            name = re.search(r"tableName:\s*'([^']+)'", body)
            if not name:
                continue
            sv = re.search(r'StreamViewType\.(\w+)', body)
            tables[name.group(1)] = {'stream': sv.group(1) if sv else None,
                                     'ttl': bool(re.search(r'timeToLiveAttribute', body))}
    lambdas = set(re.findall(r"functionName:\s*'([^']+)'", blob))
    rules = set(re.findall(r"ruleName:\s*'([^']+)'", blob))
    events = set(re.findall(r"'([A-Z][A-Za-z]+Event)'", blob))
    sst = os.path.join(ROOT, 'src/WebApps/Shopping.Web.SPA.React/sst.config.ts')
    if os.path.exists(sst):
        events |= set(re.findall(r'"([A-Z][A-Za-z]+Event)"', open(sst, encoding='utf-8').read()))
    # Several events only ever appear as nameof(XEvent) in the publishers, never as a literal in CDK.
    for cs in glob.glob(os.path.join(ROOT, 'src/Services/**/*.cs'), recursive=True):
        if '/obj/' in cs or '/bin/' in cs:
            continue
        try:
            body = open(cs, encoding='utf-8', errors='ignore').read()
        except OSError:
            continue
        events |= set(re.findall(r'nameof\((\w+Event)\)', body))
        events |= set(re.findall(r'(?:record|class)\s+(\w+Event)\b', body))
    fields = {os.path.basename(p)[:-3].split('.')[1]
              for p in glob.glob(os.path.join(ROOT, 'graphql/resolvers/*/*/*.js'))}
    return {'tables': tables, 'lambdas': lambdas, 'rules': rules, 'events': events,
            'fields': fields, 'blob': blob}


# --------------------------------------------------------------------------- checks
def check_structure(page, cells, model, infra):
    global checks
    ids = {c.get('id') for c in cells}
    kinds = {c.get('id'): kind(c.get('style') or '') for c in cells}

    for c in cells:
        if c.get('edge') != '1':
            continue
        s, t = c.get('source'), c.get('target')
        if s not in ids or t not in ids:
            g = c.find('mxGeometry')
            fixed = {p.get('as') for p in g.iter('mxPoint') if p.get('as')} if g is not None else set()
            # a sourcePoint/targetPoint means the author anchored it on purpose
            if not fixed:
                fail('structure', page, f'edge {c.get("id")} has no source/target and no fixed point')
            continue
        ks, kt = kinds.get(s), kinds.get(t)
        for bad, why in ((ks == 'TABLE' and kt == 'TABLE', 'table -> table is impossible'),
                         (ks == 'TABLE' and kt == 'LAMBDA', 'table -> lambda needs a stream'),
                         (ks == 'BUS' and kt == 'TABLE', 'bus -> table')):
            if bad:
                fail('structure', page, f'edge {c.get("id")}: {why}')
    checks += 1

    boxes = {c.get('id'): geo(c) for c in cells if geo(c)}
    if boxes:
        pw, ph = int(model.get('pageWidth')), int(model.get('pageHeight'))
        x2 = max(g[0] + g[2] for g in boxes.values())
        y2 = max(g[1] + g[3] for g in boxes.values())
        if x2 > pw or ph < y2:
            fail('structure', page, f'content {x2:.0f}x{y2:.0f} overflows page {pw}x{ph}')
    checks += 1

    icons = {c.get('id'): geo(c) for c in cells
             if geo(c) and kinds.get(c.get('id')) not in (None, 'BOX', 'CLOUD')}
    ks = list(icons)
    for i, a in enumerate(ks):
        for b in ks[i + 1:]:
            ga, gb = icons[a], icons[b]
            # ~36px below each icon for its caption (two 10px lines plus leading)
            if (ga[0] < gb[0] + gb[2] + 8 and gb[0] < ga[0] + ga[2] + 8
                    and ga[1] < gb[1] + gb[3] + 36 and gb[1] < ga[1] + ga[3] + 36):
                fail('structure', page, f'icons {a} and {b} overlap (caption space included)')
    checks += 1


def check_content(page, cells, infra):
    global checks
    blob = ' '.join(joined(c) for c in cells)
    spaced_blob = ' '.join(spaced(c) for c in cells)

    for name in set(re.findall(
            r'\b((?:catalogview|catalog|basket|ordering|paymentgateway|payment|pricing|review|user|'
            r'challenges|notification|product-images)-[a-z0-9-]*'
            r'(?:consumer|publisher|plan|campaign|answer|hint|points|presign|processor))\b', blob)):
        if name in infra['lambdas']:
            continue
        # "the old catalog-review-created-consumer no longer exists" is a deliberate historical note
        context = ' '.join(joined(c) for c in cells if name in joined(c)).lower()
        if any(k in context for k in ('no longer exists', 'is gone', 'was removed', 'the old ')):
            continue
        fail('content', page, f'Lambda "{name}" does not exist in infra/')
    checks += 1

    for c in cells:
        if 'resIcon=mxgraph.aws4.dynamodb' not in (c.get('style') or ''):
            continue
        text = plain(c)
        name = text.split(' ')[0].strip()
        truth = infra['tables'].get(name)
        if not truth:
            continue
        low = text.lower()
        says_no = 'no stream' in low or 'sem stream' in low
        says_yes = 'stream' in low and not says_no
        if truth['stream'] and says_no:
            fail('content', page, f'"{name}" says no stream, infra has {truth["stream"]}')
        if not truth['stream'] and says_yes:
            fail('content', page, f'"{name}" claims a stream, infra has none')
        if truth['stream'] and says_yes:
            m = re.search(r'(NEW_AND_OLD_IMAGES|NEW_IMAGE|KEYS_ONLY|OLD_IMAGE)', text)
            if m and m.group(1) != truth['stream']:
                fail('content', page, f'"{name}" stream {m.group(1)} != infra {truth["stream"]}')
        if truth['ttl'] and 'ttl' not in low:
            fail('content', page, f'"{name}" has TTL in infra but the label does not say so')
    checks += 1

    for c in cells:
        text = plain(c)
        if 'Quer' not in text and 'Mutation' not in text:
            continue
        for f in re.findall(r'(?:Query|Mutation):?\s+(\w+)', text):
            if f[0].islower() and f not in infra['fields'] and len(f) > 3:
                fail('content', page, f'GraphQL field "{f}" has no resolver in graphql/resolvers/')
    checks += 1

    for e in set(re.findall(r'\b([A-Z][A-Za-z]+Event)\b', spaced_blob)):
        if e not in infra['events'] and e not in NOT_INTEGRATION_EVENTS:
            fail('content', page, f'event "{e}" not found in infra/ or sst.config.ts')
    for bare in set(BARE_EVENT.findall(spaced_blob)):
        if bare + 'Event' in infra['events']:
            fail('content', page, f'"{bare}" is missing the Event suffix')
    checks += 1

    for c in cells:
        if c.get('value') and PT.search(html.unescape(c.get('value'))):
            fail('content', page, f'Portuguese text in cell {c.get("id")}: {plain(c)[:50]}')
    checks += 1


def check_consistency(pages):
    global checks
    chrome = set()
    for page, cells, model in pages:
        chrome.add((model.get('background'), model.get('pageScale')))

        used, legend = set(), []
        for c in cells:
            style = c.get('style') or ''
            if c.get('edge') == '1':
                m = re.search(r'strokeColor=(#[0-9A-Fa-f]{6})', style)
                if m:
                    used.add(m.group(1))
            if (c.get('value') or '').startswith('<b>Legend</b>'):
                legend = re.findall(r'color="(#[0-9A-Fa-f]{6})"', c.get('value'))
        if legend and legend != [c for c in LEGEND_ORDER if c in legend]:
            fail('consistency', page, f'legend colours out of canonical order: {legend}')
        undocumented = sorted(used - set(legend) - {'#6B2FD6'})
        if legend and undocumented:
            fail('consistency', page, f'edge colours not in the legend: {undocumented}')

        for c in cells:
            style = c.get('style') or ''
            if 'shape=mxgraph.aws4' in style and 'aws4.group' not in style \
                    and 'dynamodb_stream' not in style and 'aws4.alarm' not in style \
                    and 'fillColor=' not in style:
                fail('consistency', page, f'icon {c.get("id")} has no fillColor (renders as a black frame)')
            if 'aws4.lambda_function' in style:
                fail('consistency', page,
                     f'icon {c.get("id")} uses lambda_function; every other page uses resourceIcon+resIcon=lambda')

            m = re.search(r'fontSize=(\d+)', style)
            if not m:
                continue
            size = int(m.group(1))
            if 'edgeLabel' in style:
                role = 'edge label'
            elif (c.get('value') or '').startswith('<b>Legend</b>'):
                role = 'legend'
            elif style.startswith('text;') and size >= 18:
                role = 'page title'
            elif 'aws4.group' in style:
                role = 'AWS Cloud label'
            elif 'dashPattern' in style:
                role = 'group box'
            else:
                continue
            if size not in FONT_ROLE[role]:
                fail('consistency', page, f'{role} at {size}px; expected {sorted(FONT_ROLE[role])}')
    checks += 1

    if len(chrome) > 1:
        fail('consistency', '(file)', f'pages disagree on (background, pageScale): {chrome}')
    checks += 1


def check_docs():
    global checks
    for md in [os.path.join(ROOT, 'README.md')] + sorted(glob.glob(os.path.join(ROOT, 'src/Services/*/README.md'))):
        rel = os.path.relpath(md, ROOT)
        base = os.path.dirname(md)
        text = open(md, encoding='utf-8').read()
        for m in re.finditer(r'\]\((\.[^)#]+)\)', text):
            if not os.path.exists(os.path.normpath(os.path.join(base, m.group(1)))):
                fail('docs', rel, f'broken link {m.group(1)}')
        for m in re.finditer(r'`dotnet test (tests/[^`\s]+\.csproj)`', text):
            if not os.path.exists(os.path.join(ROOT, m.group(1))):
                fail('docs', rel, f'referenced test project missing: {m.group(1)}')
    checks += 1


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument('--page', help='validate a single page by name')
    ap.add_argument('--only', choices=['structure', 'content', 'consistency', 'docs'],
                    help='run one group of checks')
    args = ap.parse_args()

    if not os.path.exists(DRAWIO):
        print(f'error: {DRAWIO} not found', file=sys.stderr)
        return 1

    infra = load_infra()
    tree = ET.parse(DRAWIO)
    pages = []
    for d in tree.getroot().iter('diagram'):
        name = d.get('name')
        if args.page and name != args.page:
            continue
        model = d.find('mxGraphModel')
        if model is None:
            fail('structure', name, 'page is compressed or empty; re-save it uncompressed')
            continue
        pages.append((name, list(d.iter('mxCell')), model))

    if not pages:
        print(f'error: no page matched {args.page!r}', file=sys.stderr)
        return 1

    run = (lambda g: args.only in (None, g))
    for name, cells, model in pages:
        if run('structure'):
            check_structure(name, cells, model, infra)
        if run('content'):
            check_content(name, cells, infra)
    if run('consistency'):
        check_consistency(pages)
    if run('docs'):
        check_docs()

    print(f'{len(pages)} page(s), {checks} check groups run')
    if fails:
        print(f'\n{len(fails)} problem(s):\n' + '\n'.join(fails))
        return 1
    print('all checks passed')
    return 0


if __name__ == '__main__':
    sys.exit(main())
