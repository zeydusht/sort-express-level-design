"""Bar chart of the lever sensitivity study (tools/sensitivity.py output)."""
import json
import sys

import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt  # noqa: E402

data = json.load(open(sys.argv[1]))
base = data['base']['win']
order = ['types 24', 'types 32', 'front room +', 'front room -', 'spread: same-layer', 'spread: random',
         'time -20%', 'time +20%', '6 mystery items', '12 mystery items', '2 caged items', '4 caged items',
         'lock 5 matches', 'lock 9 matches']
labels = {'types 24': '24 item types', 'types 32': '32 item types', 'front room +': 'more empty front slots',
          'front room -': 'fewer empty front slots', 'spread: same-layer': 'triples on the same layer',
          'spread: random': 'triples spread randomly', 'time -20%': 'time -20%', 'time +20%': 'time +20%',
          '6 mystery items': '6 mystery items', '12 mystery items': '12 mystery items', '2 caged items': '2 caged items',
          '4 caged items': '4 caged items', 'lock 5 matches': 'lock (5 matches)', 'lock 9 matches': 'lock (9 matches)'}
deltas = [100 * (data[k]['win'] - base) for k in order]
fig, ax = plt.subplots(figsize=(8, 5.2), dpi=130)
fig.patch.set_facecolor('#121A2E')
ax.set_facecolor('#121A2E')
colors = ['#E2474B' if d < -25 else '#ECAF24' if d < -5 else '#87AFEE' for d in deltas]
ax.barh([labels[k] for k in order], deltas, color=colors)
for i, d in enumerate(deltas):
    ax.text(d - 1 if d < 0 else d + 1, i, '%+.0f' % d, va='center', ha='right' if d < 0 else 'left', color='white', fontsize=9)
ax.axvline(0, color='white', lw=0.8)
ax.invert_yaxis()
ax.set_xlim(-75, 12)
ax.set_xlabel('change in simulated win rate (points) vs. base board (%.0f%%)' % (100 * base), color='white')
ax.set_title('One lever at a time  (Medium layout, 8 boards x 150 players each)', color='white', fontsize=11)
ax.tick_params(colors='white')
for s in ax.spines.values():
    s.set_color('#3D63CD')
plt.tight_layout()
plt.savefig(sys.argv[2], facecolor=fig.get_facecolor())
