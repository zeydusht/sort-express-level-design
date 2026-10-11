"""Per-tier anatomy of every shipped Sort Express level (Normal / Hard / Super Hard, by level band).

    mono boardstats.exe <levels> > board_stats.jsonl          (tools/stats/BoardStats.cs: board after the generator)
    python3 tools/tier_stats.py <levels> board_stats.jsonl [out.json]

Per level: shelves (single / 3 / wide / moving), items to clear (placed + generated + piñata contents), types,
colour twins, depth, front-row fill, time, seconds per item and every obstacle with its count.
"""
import collections
import glob
import itertools
import json
import os
import re
import statistics
import sys

BANDS = [(1, 100), (101, 300), (301, 600), (601, 1000), (1001, 1500), (1501, 2150)]
TIERS = {0: 'Normal', 10: 'Hard', 20: 'Super Hard'}
# obstacle kinds in the order the game introduces them
OBSTACLES = ['lock', 'moving', 'frozen', 'cargo', 'shutter', 'mystery', 'wide', 'ferris', 'pinata', 'fuse', 'egg', 'iris']


def model(iid):
    """item_camera_red -> camera (colour twins share the model)."""
    parts = iid.split('_')
    return parts[1] if len(parts) > 2 else iid


def level(path, board):
    d = json.load(open(path))
    n = int(re.search(r'Level(\d+)_', os.path.basename(path)).group(1))
    m = dict(n=n, ld=d.get('ld', 0), tl=d.get('tl', 0))
    shelves = d.get('s') or []
    c = collections.Counter()
    types = set()
    rows = set()
    for s in shelves:
        w = s.get('sicw', 3)
        c['single'] += w == 1
        c['three'] += w == 3
        c['wide'] += w > 3
        rows.add(round(s.get('pos', {}).get('y', 0), 2))
        if s.get('lmc', 0) > 0:
            c['lock'] += 1
            c['lockSum'] += s['lmc']
            c['lockMax'] = max(c['lockMax'], s['lmc'])
        c['moving'] += bool(s.get('im'))
        c['cargo'] += bool(s.get('isdp'))
        c['shutter'] += bool(s.get('icl'))
        c['ferris'] += bool(s.get('if'))
        c['iris'] += 'irs' in s
        if s.get('fbi'):
            c['fuse'] += 1
            c['fuseGoals'] += len(s['fbi'])
        for layer in s.get('si') or []:
            for cell in layer.get('rd') or []:
                it = cell.get('it', 0)
                pr = cell.get('pr') or {}
                if it == 1:
                    types.add(cell.get('iid'))
                    c['frozen'] += bool(pr.get('ib'))
                    c['mystery'] += bool(pr.get('imy'))
                elif it == 5:
                    c['pinata'] += 1
                    inside = pr.get('pcii') or {}
                    if inside.get('it') == 1:
                        types.add(inside.get('iid'))
                elif it == 7:
                    c['egg'] += 1
    waves = []
    gaps = 0
    for s in shelves:
        for layer in s.get('si') or []:
            for cell in layer.get('rd') or []:
                gaps += cell.get('it', 0) == 0
    for g in d.get('ig') or []:
        for w in g.get('isg') or []:
            waves.append(w)
            for e in w.get('icd') or []:
                it = (e.get('id2') or {}).get('it', 0)
                k = e.get('ic', 0)
                if it == 0:
                    gaps += k
                elif it == 1:
                    types.add(e['id2'].get('iid'))
                    pr = e['id2'].get('pr') or {}
                    c['frozen'] += k if pr.get('ib') else 0
                    c['mystery'] += k if pr.get('imy') else 0
                elif it == 5:
                    c['pinata'] += k
                    inside = (e['id2'].get('pr') or {}).get('pcii') or {}
                    if inside.get('it') == 1:
                        types.add(inside.get('iid'))
                elif it == 7:
                    c['egg'] += k
    models = collections.Counter(model(t) for t in types)
    m.update(dict(
        shelves=len(shelves), rows=len(rows), single=c['single'], three=c['three'], wide=c['wide'],
        types=len(types), twins=sum(v for v in models.values() if v > 1),
        total=board['total'], generated=board['generated'],
        genShare=board['generated'] / board['total'] if board['total'] else 0,
        frontSlots=board['frontSlots'], frontFill=board['frontFilled'] / board['frontSlots'] if board['frontSlots'] else 0,
        backFill=board['backFilled'] / board['backSlots'] if board['backSlots'] else 0,
        maxDepth=board['maxDepth'], avgDepth=board['avgDepth'],
        secPerItem=m['tl'] / board['total'] if board['total'] else 0,
        itemsPerSlot=board['total'] / board['frontSlots'] if board['frontSlots'] else 0,
        gapShare=gaps / (gaps + board['total']) if board['total'] else 0,
        lockSum=c['lockSum'], lockMax=c['lockMax'], fuseGoals=c['fuseGoals'],
        waves=len(waves),
        ssp=statistics.median([w.get('ssp', 1) for w in waves]) if waves else None,
        slw=statistics.median([w.get('slw', 1) for w in waves]) if waves else None,
        blw=statistics.median([w.get('blw', 1) for w in waves]) if waves else None,
        rw=statistics.median([w.get('rw', 1) for w in waves]) if waves else None,
    ))
    for k in OBSTACLES:
        m[k] = c[k]
    m['kinds'] = sum(1 for k in OBSTACLES if m[k] > 0)
    return m


def med(xs):
    xs = [x for x in xs if x is not None]
    return statistics.median(xs) if xs else None


def summary(ls):
    out = dict(levels=len(ls))
    for k in ['tl', 'total', 'types', 'twins', 'shelves', 'rows', 'single', 'three', 'wide', 'frontSlots',
              'frontFill', 'backFill', 'maxDepth', 'avgDepth', 'secPerItem', 'itemsPerSlot', 'gapShare', 'kinds',
              'genShare', 'ssp', 'slw', 'blw', 'rw']:
        out[k] = med([l[k] for l in ls])
    out['totalP10'] = sorted(l['total'] for l in ls)[len(ls) // 10] if ls else None
    out['totalP90'] = sorted(l['total'] for l in ls)[len(ls) * 9 // 10] if ls else None
    out['tlMin'] = min(l['tl'] for l in ls) if ls else None
    out['tlMax'] = max(l['tl'] for l in ls) if ls else None
    obst = {}
    for k in OBSTACLES:
        have = [l for l in ls if l[k] > 0]
        obst[k] = dict(share=len(have) / len(ls) if ls else 0,
                       median=med([l[k] for l in have]),
                       max=max([l[k] for l in have]) if have else 0)
    have = [l for l in ls if l['lock'] > 0]
    obst['lock']['medianMatches'] = med([l['lockSum'] for l in have])
    obst['lock']['maxMatches'] = max([l['lockMax'] for l in have]) if have else 0
    have = [l for l in ls if l['fuse'] > 0]
    obst['fuse']['medianGoals'] = med([l['fuseGoals'] for l in have])
    out['obstacles'] = obst
    out['noObstacle'] = sum(1 for l in ls if l['kinds'] == 0) / len(ls) if ls else 0
    combos = collections.Counter(' + '.join(k for k in OBSTACLES if l[k] > 0) or '(none)' for l in ls)
    out['topCombos'] = combos.most_common(8)
    return out


def main():
    folder, board_file = sys.argv[1], sys.argv[2]
    boards = {}
    for line in open(board_file):
        b = json.loads(line)
        boards[b['n']] = b
    levels = []
    for f in sorted(glob.glob(os.path.join(folder, 'Level*_0.json'))):
        n = int(re.search(r'Level(\d+)_', os.path.basename(f)).group(1))
        levels.append(level(f, boards[n]))

    result = dict(levels=len(levels), tiers={}, bands={})
    for ld, name in TIERS.items():
        tier = [l for l in levels if l['ld'] == ld]
        result['tiers'][name] = summary(tier)
        result['bands'][name] = {'%d-%d' % b: summary([l for l in tier if b[0] <= l['n'] <= b[1]]) for b in BANDS}
    result['otherLd'] = sorted(set(l['ld'] for l in levels) - set(TIERS))
    result['superHardLevels'] = [l for l in levels if l['ld'] == 20]
    out = sys.argv[3] if len(sys.argv) > 3 else None
    if out:
        json.dump(result, open(out, 'w'), indent=1)

    def row(name, s):
        o = s['obstacles']
        return ('| {} | {} | {:.0f} | {:.0f} | {:.0f} | {:.0f} | {:.0f} | {:.1f} | {:.0%} | {:.0f} | {:.2f} | {:.0f} |'
                .format(name, s['levels'], s['shelves'], s['total'], s['types'], s['twins'], s['tl'], s['avgDepth'],
                        s['frontFill'], s['frontSlots'], s['secPerItem'], s['kinds']))

    print('| Tier | Level | Raf | Item | Tür | Renk ikizi | Süre | Ort. derinlik | Ön doluluk | Ön slot | sn/item | Engel türü |')
    print('|---|---|---|---|---|---|---|---|---|---|---|---|')
    for name in TIERS.values():
        print(row(name, result['tiers'][name]))
    for name in TIERS.values():
        print('\n##', name)
        print('| Bant | Level | Raf | Item | Tür | Renk ikizi | Süre | Ort. derinlik | Ön doluluk | Ön slot | sn/item | Engel türü |')
        print('|---|---|---|---|---|---|---|---|---|---|---|---|')
        for b, s in result['bands'][name].items():
            if s['levels']:
                print(row(b, s))
        print('obstacle share / median count when present:')
        for b, s in result['bands'][name].items():
            if not s['levels']:
                continue
            print(' ', b, '  '.join('{}:{:.0%}/{}'.format(k, v['share'], v['median']) for k, v in s['obstacles'].items() if v['share'] > 0),
                  ' lockMatches', s['obstacles']['lock']['medianMatches'], 'none', '{:.0%}'.format(s['noObstacle']))
            print('   top combos:', s['topCombos'][:5])
            print('   gen: share {} ssp {} slw {} blw {} rw {}  total p10-p90 {}-{}  tl {}-{}  single {} wide {} rows {}'.format(
                s['genShare'], s['ssp'], s['slw'], s['blw'], s['rw'], s['totalP10'], s['totalP90'], s['tlMin'], s['tlMax'],
                s['single'], s['wide'], s['rows']))


def report(path):
    r = json.load(open(path))
    print('\n1c tables (corrected: generator gaps are not items)')
    for band in ['1-100', '101-300']:
        print('\n| Level %s (medyan) | Süre | Item | Tür | Raf | sn/item | Kilitli level | Frozen\'lı level |' % band.replace('-', '–'))
        print('|---|---|---|---|---|---|---|---|')
        for name in TIERS.values():
            s = r['bands'][name][band]
            o = s['obstacles']
            print('| %s | %.0f | %.0f | %.0f | %.0f | %s | %%%.0f | %%%.0f |' % (
                name, s['tl'], s['total'], s['types'], s['shelves'], ('%.2f' % s['secPerItem']).replace('.', ','),
                100 * o['lock']['share'], 100 * o['frozen']['share']))
    print('\ngap share / items per front slot by tier and band')
    for name in TIERS.values():
        print(name, {b: (round(s['gapShare'], 3), round(s['itemsPerSlot'], 2)) for b, s in r['bands'][name].items()},
              'all', round(r['tiers'][name]['gapShare'], 3), round(r['tiers'][name]['itemsPerSlot'], 2))


if __name__ == '__main__':
    main()
    if len(sys.argv) > 3:
        report(sys.argv[3])
