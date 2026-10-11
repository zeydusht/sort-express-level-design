"""Level design tool for the Sort Express case.

Builds Easy / Medium / Hard levels in Sort Express' own JSON format from a small set of design
levers that mirror the ones found in the shipped level data:

    * board size          shelves / slots / layers              (s, sicw, si)
    * item load           number of item types (x3 items)        (iid)
    * look-alike items    colour variants of the same model      (item_<model>_<colour>)
    * breathing room      empty slots per layer                  (generator 'ssp' in the real data)
    * triple spread       same-layer / adjacent-layer / random   (generator 'slw' / 'blw' / 'rw')
    * obstacles           lock (lmc), blocked (ib), mystery (imy), single-slot stacks (sicw=1)
    * time                seconds per item                       (tl)

Every candidate is validated by the C# simulator (same rules code as the Unity game):
a full-information solver must find a solution, and a Monte Carlo player population must land
in the target win-rate band of the tier.
"""
import json
import os
import random
import subprocess
import sys
import uuid

HERE = os.path.dirname(os.path.abspath(__file__))
SIM = os.path.join(HERE, 'sim', 'sim.exe')

W3 = 3.13          # width of a 3-slot shelf in world units (from the real level data)
W1 = W3 / 3        # width of a single slot
SIDE = W3 / 2 + W1 / 2  # x of a single stack sitting next to an offset 3-slot shelf


def row3(y):
    return [(-W3, y, 3), (0.0, y, 3), (W3, y, 3)]


def row2(y, singles=False):
    r = [(-W3 / 2, y, 3), (W3 / 2, y, 3)]
    if singles:
        r += [(-(W3 + W1 / 2), y, 1), (W3 + W1 / 2, y, 1)]
    return r


LEVELS = {
    'easy': dict(
        name='Easy', ld=0, time=100, seed_base=100,
        layout=row3(2) + row3(1) + row3(0),
        layers3=3, layers1=0,
        fill=[0.78, 0.82, 0.85],          # more room in the front layer, fuller at the back
        types=['apple_green', 'anchor_blue', 'babydino_green', 'backpack_wfaceorange', 'ball_basketorange',
               'balloondog_pink', 'baloon_yellowbolts', 'beartotem_default', 'birdhouse_default', 'book_blueclouds',
               'bowlingpin_red', 'boxingglove_red', 'burger_default', 'cactus_green', 'camera_red', 'cerealbox_yellow',
               'chefhat_white', 'carnivaltent_default', 'bigben_default', 'cheesecake_default', 'axe_default',
               'abacus_red', 'bumpercar_default', 'bowlingball_purple'][:22],
        spread=dict(same=0.75, adj=0.22, rand=0.03), allow_pairs=True,
        locks=[], blocked=0, mystery=0,
        target=(0.88, 1.00)),
    'medium': dict(
        name='Medium', ld=10, time=150, seed_base=200,
        layout=row3(3) + row2(2, singles=True) + row3(1) + row2(0),
        layers3=4, layers1=4,
        fill=[0.68, 0.74, 0.78, 0.80],
        types=['camera_red', 'camera_blue', 'backpack_wfaceorange', 'backpack_wfaceblue', 'balloondog_pink',
               'balloondog_blue', 'baloon_yellowbolts', 'baloon_redhearts', 'book_blueclouds', 'book_rednotes',
               'babydino_green', 'babydino_orange', 'boxingglove_red', 'boxingglove_blue', 'cactus_green', 'cactus_pink',
               'apple_green', 'anchor_blue', 'ball_basketorange', 'beartotem_default', 'birdhouse_default',
               'bowlingpin_red', 'burger_default', 'cerealbox_yellow', 'chefhat_white', 'carnivaltent_default',
               'cheesecake_default', 'can_red', 'cauldron_purple', 'chicken_white', 'beermug_default',
               'amphoraround_orange'][:28],
        spread=dict(same=0.60, adj=0.30, rand=0.10), allow_pairs=False,
        locks=[],                                # new mechanic of this tier: blocked items only
        blocked=2, mystery=0,
        target=(0.50, 0.75)),
    'hard': dict(
        name='Hard', ld=20, time=200, seed_base=300,
        layout=row3(4) + row2(3, singles=True) + row3(2) + row2(1, singles=True) + row2(0),
        layers3=4, layers1=4,
        fill=[0.66, 0.72, 0.75, 0.78],
        types=['camera_red', 'camera_blue', 'camera_yellow', 'cameravertical_red', 'cameravertical_blue',
               'balloondog_pink', 'balloondog_blue', 'balloondog_yellow', 'baloon_yellowbolts', 'baloon_redhearts',
               'baloon_blueclouds', 'backpack_wfaceorange', 'backpack_wfaceblue', 'backpack_wfacegreen',
               'book_blueclouds', 'book_rednotes', 'book_greenclovers', 'billiardball_redlined',
               'billiardball_bluelined', 'billiardball_yellow', 'bowlingball_purple', 'bowlingball_green',
               'cactus_green', 'cactus_pink', 'chips_red', 'chips_blue', 'ceramiccup_dottedblue',
               'ceramiccup_dottedred', 'babydino_green', 'babydino_orange', 'boxingglove_red', 'boxingglove_blue',
               'bumpercar_default', 'bumpercar_red', 'chipsbag_red', 'bonbonpackcube_red', 'bleachbottle_blue',
               'cannonpirate_default', 'airballoon_bluewpattern', 'airballoon_redwpattern'][:36],
        spread=dict(same=0.50, adj=0.38, rand=0.12), allow_pairs=False,
        locks=[(0, 5)],                          # (shelf index, matches needed)
        blocked=2, mystery=6,
        target=(0.20, 0.45)),
}


def build(cfg, seed):
    rng = random.Random(seed)
    shelves = []
    for i, (x, y, w) in enumerate(cfg['layout']):
        n_layers = cfg['layers3'] if w == 3 else cfg['layers1']
        shelves.append(dict(x=x, y=y, w=w, layers=[[None] * w for _ in range(n_layers)]))

    # all slots grouped by layer index
    max_layers = max(len(s['layers']) for s in shelves)
    slots_by_layer = [[] for _ in range(max_layers)]
    for si, s in enumerate(shelves):
        for li in range(len(s['layers'])):
            for k in range(s['w']):
                slots_by_layer[li].append((si, li, k))

    n_items = len(cfg['types']) * 3
    locked_ids = {si for si, _ in cfg['locks']}
    # Layer fill ratio (the real generator's 'ssp'): each layer is filled to `fill`, the rest
    # stays empty. Locked shelves are always filled completely (their space is not usable anyway).
    free = set()
    remaining = n_items
    for li in range(max_layers):
        layer_slots = slots_by_layer[li]
        locked_slots = [c for c in layer_slots if c[0] in locked_ids]
        open_ = [c for c in layer_slots if c[0] not in locked_ids]
        fill = cfg['fill'][min(li, len(cfg['fill']) - 1)] if isinstance(cfg['fill'], list) else cfg['fill']
        target = min(remaining, len(locked_slots) + int(round(len(open_) * fill)))
        n_empty = len(layer_slots) - target
        rng.shuffle(open_)
        chosen_empty = []
        for c in open_:
            if len(chosen_empty) >= n_empty:
                break
            si = c[0]
            sh = shelves[si]
            deeper = c[1] < len(sh['layers']) - 1
            if sh['w'] == 1 and deeper and remaining - target > 0:
                continue  # keep single stacks solid while items remain
            same_layer_empty = sum(1 for e in chosen_empty if e[0] == si)
            if sh['w'] == 3 and same_layer_empty >= 2 and li > 0 and remaining - target > 0:
                continue  # never a fully empty layer in the middle of a shelf
            chosen_empty.append(c)
        if len(chosen_empty) < n_empty:
            for c in locked_slots[::-1] + open_:
                if len(chosen_empty) >= n_empty:
                    break
                if c not in chosen_empty:
                    chosen_empty.append(c)
        free.update(chosen_empty)
        remaining -= len(layer_slots) - len(chosen_empty)
    if remaining > 0:
        raise ValueError('not enough layers for %d items' % n_items)

    open_slots = {li: [c for c in slots_by_layer[li] if c not in free] for li in range(max_layers)}
    for li in open_slots:
        rng.shuffle(open_slots[li])

    def take(li, avoid_shelves):
        lst = open_slots.get(li, [])
        for idx, c in enumerate(lst):
            if c[0] not in avoid_shelves:
                return lst.pop(idx)
        return None

    types = cfg['types'][:]
    rng.shuffle(types)
    sp = cfg['spread']
    placed = {}
    for t in types:
        r = rng.random()
        mode = 'same' if r < sp['same'] else ('adj' if r < sp['same'] + sp['adj'] else 'rand')
        got = None
        for attempt in range(40):
            layers_avail = [li for li in range(max_layers) if open_slots[li]]
            if not layers_avail:
                break
            if mode == 'same':
                li = rng.choice(layers_avail)
                plan = [li, li, li]
            elif mode == 'adj':
                li = rng.choice(layers_avail)
                plan = [li, li, min(li + 1, max_layers - 1)] if rng.random() < 0.5 else [li, max(li - 1, 0), li]
            else:
                plan = [rng.choice(layers_avail) for _ in range(3)]
            chosen = []
            used = set()
            ok = True
            for li in plan:
                avoid = set() if cfg['allow_pairs'] else set(used)
                if cfg['allow_pairs']:
                    # never three of the same on one shelf layer
                    same_layer = [c for c in chosen if c[1] == li]
                    if len(same_layer) == 2 and same_layer[0][0] == same_layer[1][0]:
                        avoid = {same_layer[0][0]}
                c = take(li, avoid)
                if c is None:
                    ok = False
                    break
                chosen.append(c)
                used.add(c[0])
            if ok:
                got = chosen
                break
            for c in chosen:          # roll back
                open_slots[c[1]].append(c)
        if got is None:
            # fall back to any slots
            got = []
            for li in range(max_layers):
                while open_slots[li] and len(got) < 3:
                    got.append(open_slots[li].pop())
            if len(got) < 3:
                raise ValueError('ran out of slots')
        for c in got:
            shelves[c[0]]['layers'][c[1]][c[2]] = dict(iid='item_' + t)
        placed[t] = got

    # obstacles -------------------------------------------------------------
    locked = {si: n for si, n in cfg['locks']}
    fronts3 = [si for si, s in enumerate(shelves) if s['w'] == 3 and si not in locked]
    rng.shuffle(fronts3)
    nb = 0
    for si in fronts3:
        if nb >= cfg['blocked']:
            break
        front = shelves[si]['layers'][0]
        cands = [k for k in range(3) if front[k] is not None]
        rng.shuffle(cands)
        for k in cands:
            t = front[k]['iid']
            partners = [c for c in placed[t[5:]] if c != (si, 0, k)]
            # a blocked item is matched where it stands, so both partners must be able to reach it:
            # never behind it on the same shelf, never blocked themselves, never in the same front
            if any(c[0] == si for c in partners):
                continue
            if any(shelves[c[0]]['layers'][c[1]][c[2]].get('blocked') for c in partners):
                continue
            front[k]['blocked'] = True
            nb += 1
            break
    nm = 0
    cands = [(si, li, k) for si, s in enumerate(shelves) for li in range(min(2, len(s['layers'])))
             for k in range(s['w']) if s['layers'][li][k] is not None and not s['layers'][li][k].get('blocked')]
    rng.shuffle(cands)
    for (si, li, k) in cands:
        if nm >= cfg['mystery']:
            break
        shelves[si]['layers'][li][k]['mystery'] = True
        nm += 1

    return to_json(cfg, shelves, locked)


def to_json(cfg, shelves, locked):
    out = dict(nm=cfg['name'], **{'as': [0]}, tid=0, lt=1, ld=cfg['ld'], tl=cfg['time'], osb=False, sir=1.5625, s=[],
               ig=[], igd=None, sfss=False)
    for si, s in enumerate(shelves):
        si_layers = []
        for layer in s['layers']:
            rd = []
            for c in layer:
                if c is None:
                    rd.append(dict(it=0))
                else:
                    d = dict(it=1, iid=c['iid'])
                    pr = {}
                    if c.get('blocked'):
                        pr['ib'] = True
                    if c.get('mystery'):
                        pr['imy'] = True
                    if pr:
                        d['pr'] = pr
                    rd.append(d)
            si_layers.append(dict(rd=rd))
        out['s'].append(dict(
            id=str(uuid.UUID(int=random.Random(si * 7919 + len(cfg['name'])).getrandbits(128))),
            gid='1', csb=True, pos=dict(x=round(s['x'], 4), y=round(s['y'], 4)), im=False, sicw=s['w'],
            si=si_layers, isd=False, icn=False, icl=False, lmc=locked.get(si, 0), pd=len(si_layers),
            scd=[dict(cmd=0, crd=2), dict(cmd=0, crd=1), dict(cmd=0, crd=3), dict(cmd=0, crd=0)]))
    return out


def simulate(path, runs=600, seed=7):
    res = subprocess.run(['mono', SIM, path, str(runs), str(seed)], capture_output=True, text=True, timeout=600)
    if res.returncode != 0:
        raise RuntimeError(res.stderr or res.stdout)
    return json.loads(res.stdout.strip().splitlines()[-1])


def _eval(args):
    key, seed, out_dir, runs = args
    cfg = LEVELS[key]
    lvl = build(cfg, seed)
    path = os.path.join(out_dir, '_cand_%s_%d.json' % (key, seed))
    json.dump(lvl, open(path, 'w'))
    r = simulate(path, runs)
    os.remove(path)
    r['seed'] = seed
    return lvl, r


def search(key, out_dir, tries=24, runs=300, workers=2):
    from concurrent.futures import ProcessPoolExecutor
    cfg = LEVELS[key]
    lo, hi = cfg['target']
    mid = (lo + hi) / 2
    best = None
    jobs = [(key, cfg['seed_base'] + i, out_dir, runs) for i in range(tries)]
    with ProcessPoolExecutor(workers) as ex:
        for lvl, r in ex.map(_eval, jobs):
            ok = r['solvable'] and r['badTypes'] == 0 and lo <= r['winRate'] <= hi
            print(key, r['seed'], 'solvable' if r['solvable'] else 'UNSOLVABLE', 'win %.2f' % r['winRate'],
                  'relaxed %.2f' % r['winRelaxedTimer'], 'stuck %.2f' % r['stuck'], 'timeout %.2f' % r['timeout'],
                  'OK' if ok else '', flush=True)
            if ok and (best is None or abs(r['winRate'] - mid) < abs(best[1]['winRate'] - mid)):
                best = (lvl, r)
    return best


if __name__ == '__main__':
    out_dir = sys.argv[1]
    keys = sys.argv[2:] or ['easy', 'medium', 'hard']
    os.makedirs(out_dir, exist_ok=True)
    report = {}
    for k in keys:
        best = search(k, out_dir)
        if best is None:
            print('NO CANDIDATE FOR', k)
            continue
        lvl, r = best
        json.dump(lvl, open(os.path.join(out_dir, LEVELS[k]['name'] + '.json'), 'w'), indent=1)
        report[k] = r
        print('PICKED', k, r)
    json.dump(report, open(os.path.join(out_dir, 'report.json'), 'w'), indent=1)
