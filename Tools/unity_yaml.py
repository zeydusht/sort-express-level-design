"""Parse a Unity YAML prefab (AssetRipper export) into objects keyed by fileID."""
import re, yaml, sys

class L(yaml.SafeLoader): pass
def _any(loader, suffix, node):
    if isinstance(node, yaml.MappingNode): return loader.construct_mapping(node, deep=True)
    if isinstance(node, yaml.SequenceNode): return loader.construct_sequence(node, deep=True)
    return loader.construct_scalar(node)
L.add_multi_constructor('', _any)

CLS = {1: 'GameObject', 4: 'Transform', 224: 'RectTransform', 114: 'MonoBehaviour', 212: 'SpriteRenderer',
       223: 'Canvas', 222: 'CanvasRenderer', 225: 'CanvasGroup', 23: 'MeshRenderer', 33: 'MeshFilter', 95: 'Animator',
       210: 'SortingGroup', 20: 'Camera', 1001: 'PrefabInstance', 198: 'ParticleSystem', 199: 'ParticleSystemRenderer'}

def load(path):
    text = open(path, encoding='utf-8', errors='replace').read()
    parts = re.split(r'^--- !u!(\d+) &(-?\d+)(?: stripped)?\s*$', text, flags=re.M)
    objs = {}
    for i in range(1, len(parts), 3):
        cid, fid, body = int(parts[i]), parts[i + 1], parts[i + 2]
        try:
            d = yaml.load(body, Loader=L)
        except Exception as e:
            d = {'_error': str(e)}
        if not isinstance(d, dict) or not d: continue
        typ, val = next(iter(d.items()))
        objs[fid] = dict(cls=cid, type=CLS.get(cid, typ), data=val if isinstance(val, dict) else {})
    return objs

def ref(x):
    return str(x.get('fileID')) if isinstance(x, dict) else None
