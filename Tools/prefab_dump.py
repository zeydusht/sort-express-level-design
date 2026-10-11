"""Dumps / renders a Unity prefab exported by AssetRipper (mesh renderers with world transforms)."""
import glob
import math
import os
import re
import sys

ROOT = '/mnt/user-data/uploads/Sort Express/temp/ExportedProject/Assets'


def parse_prefab(path):
    t = open(path).read()
    docs = re.split(r'\n--- !u!(\d+) &(-?\d+)', '\n' + t)
    objs = {docs[i + 1]: (int(docs[i]), docs[i + 2]) for i in range(1, len(docs), 3)}
    return objs


def vec(body, key, n=3):
    m = re.search(key + r': \{([^}]*)\}', body)
    if not m:
        return None
    d = dict(kv.split(': ') for kv in m.group(1).split(', '))
    keys = 'xyzw'[:n]
    return [float(d[k]) for k in keys]


def quat_mat(q):
    x, y, z, w = q
    return [[1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
            [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
            [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)]]


def build(path):
    objs = parse_prefab(path)
    go = {}
    tr = {}
    mf = {}
    mr = {}
    for fid, (cls, body) in objs.items():
        if cls == 1:
            go[fid] = dict(name=re.search(r'm_Name: (.*)', body).group(1).strip(),
                           active=re.search(r'm_IsActive: (\d)', body).group(1) == '1')
        elif cls in (4, 224):
            tr[fid] = dict(go=re.search(r'm_GameObject: \{fileID: (-?\d+)', body).group(1),
                           pos=vec(body, 'm_LocalPosition'), rot=vec(body, 'm_LocalRotation', 4),
                           scale=vec(body, 'm_LocalScale'),
                           father=re.search(r'm_Father: \{fileID: (-?\d+)', body).group(1))
        elif cls == 33:
            m = re.search(r'm_Mesh: \{fileID: (-?\d+)(?:, guid: (\w+))?', body)
            mf[re.search(r'm_GameObject: \{fileID: (-?\d+)', body).group(1)] = (m.group(1), m.group(2))
        elif cls == 23:
            en = re.search(r'm_Enabled: (\d)', body).group(1) == '1'
            mats = re.findall(r'- \{fileID: (-?\d+), guid: (\w+), type: 2\}', body)
            mr[re.search(r'm_GameObject: \{fileID: (-?\d+)', body).group(1)] = (en, mats)
    by_go = {t['go']: fid for fid, t in tr.items()}

    def world(tfid):
        t = tr[tfid]
        M = quat_mat(t['rot'])
        S = t['scale']
        P = t['pos']
        # local matrix 3x4
        L = [[M[r][c] * S[c] for c in range(3)] + [P[r]] for r in range(3)]
        if t['father'] == '0' or t['father'] not in tr:
            return L
        F = world(t['father'])
        R = [[sum(F[r][k] * L[k][c] for k in range(3)) + (F[r][3] if c == 3 else 0) for c in range(4)] for r in range(3)]
        return R

    def active(tfid):
        t = tr[tfid]
        if not go[t['go']]['active']:
            return False
        return t['father'] == '0' or t['father'] not in tr or active(t['father'])

    def path_of(tfid):
        t = tr[tfid]
        n = go[t['go']]['name']
        return n if t['father'] == '0' or t['father'] not in tr else path_of(t['father']) + '/' + n

    out = []
    for g, (fid, guid) in mf.items():
        tf = by_go.get(g)
        if tf is None:
            continue
        out.append(dict(path=path_of(tf), active=active(tf), mesh=(fid, guid), M=world(tf),
                        renderer=mr.get(g)))
    return out


if __name__ == '__main__':
    guid2mesh = {}
    for m in glob.glob(ROOT + '/Mesh/*.meta'):
        g = re.search(r'guid: (\w+)', open(m).read()).group(1)
        guid2mesh[g] = os.path.basename(m)[:-5]
    builtin = {'10202': 'CUBE', '10210': 'QUAD', '10209': 'PLANE'}
    for o in build(sys.argv[1]):
        fid, guid = o['mesh']
        name = builtin.get(fid) if guid and guid.startswith('0000000000000000e') else guid2mesh.get(guid, '?')
        M = o['M']
        sx = math.sqrt(sum(M[r][0] ** 2 for r in range(3)))
        sy = math.sqrt(sum(M[r][1] ** 2 for r in range(3)))
        sz = math.sqrt(sum(M[r][2] ** 2 for r in range(3)))
        print('%-5s %-55s %-28s pos(%.2f %.2f %.2f) scale(%.2f %.2f %.2f) %s' % (
            'ON' if o['active'] else 'off', o['path'][-55:], name, M[0][3], M[1][3], M[2][3], sx, sy, sz,
            'mat:' + ','.join(g[:6] for _, g in o['renderer'][1]) if o['renderer'] else ''))
