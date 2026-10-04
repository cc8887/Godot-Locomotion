"""Read-only arithmetic diagnostics; this is not an acceptance oracle."""
import json
import math
import pathlib
import struct

ROOT = pathlib.Path(__file__).resolve().parents[1]


def single(v):
    return struct.unpack('f', struct.pack('f', v))[0]


def product(a, b, mode):
    x, y, z, w = a
    X, Y, Z, W = b
    terms = [(w*X, x*W, y*Z, -z*Y), (w*Y, -x*Z, y*W, z*X),
             (w*Z, x*Y, -y*X, z*W), (w*W, -x*X, -y*Y, -z*Z)]
    if mode == 'scalar':
        return [sum(t) for t in terms]
    f = math.fma
    if mode == 'w_fma':
        return [f(w, X, x*W)+y*Z-z*Y, f(w, Y, -x*Z)+y*W+z*X,
                f(w, Z, x*Y)-y*X+z*W, f(w, W, -x*X)-y*Y-z*Z]
    if mode == 'x_fma':
        return [f(x, W, w*X)+y*Z-z*Y, f(-x, Z, w*Y)+y*W+z*X,
                f(x, Y, w*Z)-y*X+z*W, f(-x, X, w*W)-y*Y-z*Z]
    return [f(-z, Y, f(y, Z, f(x, W, w*X))),
            f(z, X, f(y, W, f(-x, Z, w*Y))),
            f(z, W, f(-y, X, f(x, Y, w*Z))),
            f(-z, Z, f(-y, Y, f(-x, X, w*W)))]


def normalize(q, mode):
    if mode == 'none':
        return q
    x, y, z, w = q
    if mode == 'xz_yw':
        length = (x*x+z*z)+(y*y+w*w)
    elif mode == 'xy_zw':
        length = (x*x+y*y)+(z*z+w*w)
    else:
        length = ((x*x+y*y)+z*z)+w*w
    scale = 1/math.sqrt(length)
    return [v*scale for v in q]


def main():
    data = json.loads((ROOT/'assets/generated/lyra_als/main_inertia_stages_v1_native.json').read_text())['traces'][0]
    parents = json.loads((ROOT/'assets/generated/lyra_als/logical_controls/calibration.json').read_text())['layout']['logicalParents']
    debug = json.loads((ROOT/'artifacts/lyra-analysis/main-inertia-godot-lerp-debug-data.json').read_text())
    observed = {r['fi']: r for r in debug if r['pass'] == 0}
    for fi in (171, 201):
        row = data['frames'][fi]
        assert not row['frozen']
        leaf = [b['rotation'] for b in row['stages'][0]['leaf']['pose']]
        expected = [b['rotation'] for b in row['stages'][0]['upper']['pose']]
        current = [[b['Rotation'][c] for c in ('X','Y','Z','W')] for b in observed[fi]['upper']]
        results = []
        for prod_mode in ('scalar', 'w_fma', 'x_fma', 'all_fma'):
            for norm_mode in ('xz_yw', 'xy_zw', 'left'):
                for add_norm in ('xz_yw', 'xy_zw', 'left', 'none', 'twice'):
                    for lerp_fma in (False, True):
                        source, target, blend, output = [], [], [], []
                        for bone, original in enumerate(leaf):
                            base = normalize(normalize(original, 'xz_yw'), 'xz_yw') if add_norm == 'twice' else normalize(original, add_norm)
                            parent = parents[bone]
                            s = product(source[parent], base, prod_mode) if parent >= 0 else base
                            t = product(target[parent], original, prod_mode) if parent >= 0 else original
                            weight = data['mask'][bone]
                            if weight <= 1e-5:
                                mixed = s
                            elif weight >= 1-1e-5:
                                mixed = t
                            else:
                                bias = 1 if sum(a*b for a,b in zip(s,t)) >= 0 else -1
                                complement = bias*single(1-weight)
                                mixed = normalize([math.fma(b,weight,a*complement) if lerp_fma else a*complement+b*weight
                                                   for a,b in zip(s,t)], norm_mode)
                            local = normalize(product([-v if c<3 else v for c,v in enumerate(blend[parent])], mixed, prod_mode), norm_mode) if parent >= 0 else base
                            source.append(s); target.append(t); blend.append(mixed); output.append(local)
                        exact = sum(a==b for q,r in zip(output,expected) for a,b in zip(q,r))
                        current_exact = sum(a==b for q,r in zip(output,current) for a,b in zip(q,r))
                        error = max(abs(a-b) for q,r in zip(output,expected) for a,b in zip(q,r))
                        results.append((exact, current_exact, error, prod_mode, norm_mode, add_norm, lerp_fma))
        print('frame',fi,'best native matches',sorted(results,reverse=True)[:5])
        print('frame',fi,'best current matches',sorted(results,key=lambda r:r[1],reverse=True)[:2])


if __name__ == '__main__':
    main()
