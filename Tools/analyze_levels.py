"""Analyses the shipped Sort Express levels (TextAsset/LevelXXXXX_0.json from the APK export).

    python3 tools/analyze_levels.py <folder with Level*.json> [out.json]

Prints:
  * the difficulty cycle (ld 0 / 10 / 20 by level number)
  * the level where every obstacle / mechanic first appears
  * per-tier medians (time, items, types, shelves, depth, generator fill, seconds per item, obstacle use)
    for levels 1-100 and 101-300
Key meanings (reverse-engineered, see README):
  ld difficulty tag, tl time limit, s shelves, sicw slots per shelf, si layers (front first),
  lmc matches needed to open a locked shelf, pr.ib blocked item, pr.imy mystery item,
  im moving shelf, isdp dispenser, if frozen, it 5 / 7 special items, ig item generator (ssp, slw, blw, rw)
"""
import glob
import json
import os
import re
import statistics
import sys


def level_metrics(path):
    d = json.load(open(path))
    n = int(re.search(r'Level(\d+)_', os.path.basename(path)).group(1))
    shelves = d.get('s') or []
    explicit = 0
    types = set()
    m = dict(n=n, ld=d.get('ld', 0), tl=d.get('tl', 0), shelves=len(shelves))
    flags = dict(single=0, wide=0, lock=0, closed=0, moving=0, dispenser=0, frozen=0, blocked=0, mystery=0,
                 it5=0, it7=0, fbi=0, irs=0)
    depth = []
    for s in shelves:
        w = s.get('sicw', 3)
        if w == 1:
            flags['single'] += 1
        if w > 3:
            flags['wide'] += 1
        if s.get('lmc', 0) > 0:
            flags['lock'] += 1
        if s.get('icl'):
            flags['closed'] += 1
        if s.get('im'):
            flags['moving'] += 1
        if s.get('isdp'):
            flags['dispenser'] += 1
        if s.get('if'):
            flags['frozen'] += 1
        if s.get('fbi'):
            flags['fbi'] += 1
        if s.get('irs'):
            flags['irs'] += 1
        layers = s.get('si') or []
        depth.append(len(layers))
        for layer in layers:
            for c in layer.get('rd') or []:
                it = c.get('it', 0)
                if it == 1:
                    explicit += 1
                    types.add(c.get('iid'))
                    pr = c.get('pr') or {}
                    if pr.get('ib'):
                        flags['blocked'] += 1
                    if pr.get('imy'):
                        flags['mystery'] += 1
                elif it == 5:
                    flags['it5'] += 1
                elif it == 7:
                    flags['it7'] += 1
    gen = 0
    ssp, slw, blw, rw = [], [], [], []
    for g in d.get('ig') or []:
        for isg in g.get('isg') or []:
            ssp.append(isg.get('ssp'))
            slw.append(isg.get('slw'))
            blw.append(isg.get('blw'))
            rw.append(isg.get('rw'))
            for icd in isg.get('icd') or []:
                gen += icd.get('ic', 0)
                id2 = icd.get('id2') or {}
                types.add(id2.get('iid'))
                pr = id2.get('pr') or {}
                if pr.get('ib'):
                    flags['blocked'] += icd.get('ic', 0)
                if pr.get('imy'):
                    flags['mystery'] += icd.get('ic', 0)
    m.update(flags)
    front_slots = sum(sh.get('sicw', 3) for sh in shelves)
    m['items'] = explicit + gen
    m['types'] = len(types)
    # generator levels keep producing items behind the visible slots, so depth = items per front slot
    m['depth'] = (explicit + gen) / front_slots if front_slots else 0
    m['ssp'] = statistics.mean([x for x in ssp if x is not None]) if any(x is not None for x in ssp) else None
    m['slw'] = statistics.mean([x for x in slw if x is not None]) if any(x is not None for x in slw) else None
    m['blw'] = statistics.mean([x for x in blw if x is not None]) if any(x is not None for x in blw) else None
    m['rw'] = statistics.mean([x for x in rw if x is not None]) if any(x is not None for x in rw) else None
    m['s_per_item'] = m['tl'] / m['items'] if m['items'] else None
    return m


def med(rows, key):
    v = [r[key] for r in rows if r.get(key) is not None]
    return statistics.median(v) if v else None


def pct(rows, key):
    return 100.0 * sum(1 for r in rows if r[key] > 0) / max(1, len(rows))


def main():
    folder = sys.argv[1]
    files = sorted(glob.glob(os.path.join(folder, 'Level*_0.json')))
    rows = [level_metrics(f) for f in files]
    rows.sort(key=lambda r: r['n'])
    print('levels analysed:', len(rows), '(1 ..', rows[-1]['n'], ')')

    # difficulty cycle
    by_digit = {}
    for r in rows:
        if r['n'] < 20:
            continue
        by_digit.setdefault(r['n'] % 10, []).append(r['ld'])
    print('\nld by last digit (levels >= 20):')
    for k in sorted(by_digit):
        v = by_digit[k]
        print('  ...%d  ->' % k, {x: v.count(x) for x in sorted(set(v))})
    first_hard = min(r['n'] for r in rows if r['ld'] == 10)
    first_super = min(r['n'] for r in rows if r['ld'] == 20)
    print('first hard (ld 10):', first_hard, ' first super hard (ld 20):', first_super)

    # first appearance
    print('\nfirst appearance of each mechanic:')
    for key in ('single', 'lock', 'moving', 'blocked', 'dispenser', 'closed', 'mystery', 'wide', 'frozen', 'it5',
                'fbi', 'it7', 'irs'):
        first = next((r['n'] for r in rows if r[key] > 0), None)
        print('  %-10s level %s   (in %.0f%% of all levels)' % (key, first, pct(rows, key)))

    # tiers
    for lo, hi in ((1, 100), (101, 300)):
        print('\nlevels %d-%d  medians' % (lo, hi))
        print('  tier        n   time  items  types shelves items/slot ssp  s/item  lock%  blocked%  mystery%')
        for ld, name in ((0, 'normal'), (10, 'hard'), (20, 'super hard')):
            rs = [r for r in rows if lo <= r['n'] <= hi and r['ld'] == ld]
            if not rs:
                continue
            print('  %-10s %3d  %5.0f  %5.0f  %5.0f  %5.0f  %7.2f   %4.2f  %5.2f  %5.0f  %7.0f  %8.0f' % (
                name, len(rs), med(rs, 'tl'), med(rs, 'items'), med(rs, 'types'), med(rs, 'shelves'), med(rs, 'depth'),
                med(rs, 'ssp') or 0, med(rs, 's_per_item'), pct(rs, 'lock'), pct(rs, 'blocked'), pct(rs, 'mystery')))

    # seconds per item over progression
    print('\nseconds per item (median) by 100-level band:')
    for lo in range(1, rows[-1]['n'] + 1, 300):
        rs = [r for r in rows if lo <= r['n'] < lo + 300]
        print('  %4d-%4d  %.2f' % (lo, lo + 299, med(rs, 's_per_item')))

    if len(sys.argv) > 2:
        json.dump(rows, open(sys.argv[2], 'w'))


if __name__ == '__main__':
    main()
