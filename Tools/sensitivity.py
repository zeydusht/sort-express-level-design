"""Lever sensitivity study: how much does each design lever move the simulated win rate?

Base board = the Medium layout (12 shelves, 4 layers) with no obstacles. One lever is changed at a time;
every variant is generated on the same 8 seeds and played by 150 simulated players per board.

    python3 tools/sensitivity.py <out.json>
"""
import json
import os
import statistics
import sys
from concurrent.futures import ProcessPoolExecutor

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import leveldesign as L  # noqa: E402

SEEDS = list(range(500, 508))
RUNS = 150
BASE = dict(L.LEVELS['medium'])
BASE.update(locks=[], blocked=0, mystery=0, time=150, name='Base')
MED = L.LEVELS['medium']['types']                       # the 28 Medium item types
TYPES = MED + [t for t in L.LEVELS['hard']['types'] if t not in MED][:4]

VARIANTS = [
    ('base', {}),
    ('types 24', dict(types=TYPES[:24])),
    ('types 32', dict(types=TYPES[:32])),
    ('front room +', dict(fill=[0.60, 0.74, 0.78, 0.80])),
    ('front room -', dict(fill=[0.76, 0.74, 0.78, 0.80])),
    ('spread: same-layer', dict(spread=dict(same=0.85, adj=0.15, rand=0.0))),
    ('spread: random', dict(spread=dict(same=0.30, adj=0.40, rand=0.30))),
    ('lock 5 matches', dict(locks=[(1, 5)])),
    ('lock 9 matches', dict(locks=[(1, 9)])),
    ('2 caged items', dict(blocked=2)),
    ('4 caged items', dict(blocked=4)),
    ('6 mystery items', dict(mystery=6)),
    ('12 mystery items', dict(mystery=12)),
    ('time -20%', dict(time=120)),
    ('time +20%', dict(time=180)),
]


def run(job):
    name, over, seed = job
    cfg = dict(BASE)
    cfg.update(over)
    if 'types' not in over:
        cfg['types'] = TYPES[:28]
    path = '/tmp/sens_%s_%d.json' % (name.replace(' ', '_').replace(':', '').replace('%', 'p').replace('+', 'p'), seed)
    json.dump(L.build(cfg, seed), open(path, 'w'))
    r = L.simulate(path, RUNS, seed)
    os.remove(path)
    return name, seed, r


if __name__ == '__main__':
    jobs = [(n, o, s) for n, o in VARIANTS for s in SEEDS]
    out = {}
    with ProcessPoolExecutor(2) as ex:
        for name, seed, r in ex.map(run, jobs):
            out.setdefault(name, []).append(r)
            if len(out[name]) == len(SEEDS):
                rs = out[name]
                print('%-20s win %.2f  relaxed-timer %.2f  stuck %.2f  timeout %.2f' % (
                    name, statistics.mean(x['winRate'] for x in rs), statistics.mean(x['winRelaxedTimer'] for x in rs),
                    statistics.mean(x['stuck'] for x in rs), statistics.mean(x['timeout'] for x in rs)), flush=True)
    summary = {n: dict(win=statistics.mean(x['winRate'] for x in rs),
                       winRelaxedTimer=statistics.mean(x['winRelaxedTimer'] for x in rs),
                       stuck=statistics.mean(x['stuck'] for x in rs),
                       timeout=statistics.mean(x['timeout'] for x in rs),
                       perSeed=[x['winRate'] for x in rs]) for n, rs in out.items()}
    json.dump(summary, open(sys.argv[1], 'w'), indent=1)
