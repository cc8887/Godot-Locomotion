"""Report capsule, animated body and root-motion observations without certifying gait quality."""
import argparse
import json
import math
from pathlib import Path


def length(vector):
    return math.sqrt(sum(x*x for x in vector))


def delta(a, b):
    return [x-y for x, y in zip(a, b)]


def horizontal_body(row):
    offset = delta(row['bones']['pelvis']['world'], row['bones']['root']['world'])
    return math.hypot(offset[0], offset[2])


def identity(value):
    return (all(value['Translation'][k] == 0 for k in 'XYZ') and
            all(value['Rotation'][k] == (1 if k == 'W' else 0) for k in 'XYZW'))


def summarize(data):
    rows = data['frames']
    travel = delta(rows[-1]['position'], rows[0]['position'])
    integral = [sum(r['velocity'][i]*r['dt'] for r in rows[1:]) for i in range(3)]
    stopped = [r for r in rows if r['tick'] >= 330]
    off = [r for r in rows if not r['rootEnabled']]
    return dict(pack=data['pack'], flatFloor=data['flatFloor'], frameCount=len(rows),
        capsuleDeltaMeters=travel, actualVelocityIntegralMeters=integral,
        deltaMinusIntegralMeters=delta(travel, integral),
        maximumHorizontalBodyOffsetMeters=max(map(horizontal_body, rows)),
        disabledRootFrames=len(off),
        disabledRootViolationTicks=[r['tick'] for r in off if r['sampleRoot'] or r['rootSource'] or
            r['consumedRoot'] or r['drive'] != 'MotorDriven' or not identity(r['proposedRoot']) or
            not identity(r['consumedDelta']) or length(r['bones']['root']['local']) > 1e-8],
        stoppedMinimumSoleHeightAboveStartFloorCm=min(r['soles'][s]['MinimumSupportHeight']
            for r in stopped for s in ('Item1', 'Item2')),
        stoppedMaximumSoleHeightAboveStartFloorCm=max(r['soles'][s]['MinimumSupportHeight']
            for r in stopped for s in ('Item1', 'Item2')),
        stoppedMaximumHorizontalFootTargetCm=max(math.hypot(r['preFoot'][s]['Location']['X'],
            r['preFoot'][s]['Location']['Y']) for r in stopped for s in ('TargetLeft','TargetRight')),
        firstFrameSoleHeightAboveStartFloorCm=min(rows[0]['soles'][s]['MinimumSupportHeight']
            for s in ('Item1','Item2')))


def comparable(data):
    return [{k: r[k] for k in ('tick','dt','inputAxes','position','velocity','worldVelocity',
                             'desiredSpeed','drive','stance','gait','bones')} for r in data['frames']]


def capture_report(data):
    rows={r['tick']:r for r in data['frames']}
    result=[]
    for c in data['captures']:
        row=rows.get(c['observedTick'])
        result.append(dict(requestedTick=c['requestedTick'], actualTick=c['observedTick'],
            presentedPoseMatchesRecord=row is not None and c['committed']==c['presented']==c['observedTick']
                and c['bones']==row['bones'],
            sampledRequestedTick=c['requestedTick']==c['observedTick'], screenshot=c['screenshot']))
    return result


def make_sheet(root):
    from PIL import Image, ImageDraw, ImageFont
    columns=[('Active v8 / Root OFF',root/'render-default-clean-forward/forward-450.png'),
             ('Candidate v11 / Root OFF',root/'release-v11-capture-verified/rendered-release-forward/forward-450.png'),
             ('Original / Root OFF',root/'render-original-clean-forward/forward-450.png')]
    sheet=Image.new('RGB',(3*480,490),'#222222'); draw=ImageDraw.Draw(sheet)
    font=ImageFont.truetype('C:/Windows/Fonts/arial.ttf',20)
    for i,(title,path) in enumerate(columns):
        with Image.open(path) as im:
            # Fixed rectangle in the same ordinary-camera screenshots; no pose edits.
            sheet.paste(im.convert('RGB').crop((260,270,740,720)),(i*480,40))
        draw.text((i*480+12,7),title+' | tick 450',font=font,fill='white')
    sheet.save(root/'release-stop-comparison.png')


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--evidence',type=Path,required=True)
    args=parser.parse_args(); root=args.evidence
    names=['default-forward','default-left-forward','v8-flat-forward','v8-flat-left-forward',
           'v8-flat-forward-root-on','v11-flat-forward','v11-flat-left-forward',
           'original-flat-forward','original-flat-left-forward']
    data={name:json.loads((root/(name+'.json')).read_text()) for name in names}
    report=dict(scope='600-frame ordinary input observations. Height is relative to StartFloor; terrain runs are not a sole-ground-clearance certificate.',
                scenarios={name:summarize(d) for name,d in data.items()},
                rootOnOffSameCapsuleAndBones=comparable(data['v8-flat-forward'])==comparable(data['v8-flat-forward-root-on']),
                crossPackCapsuleParity={direction:all(
                    [r['position'] for r in data[pack+'-flat-'+direction]['frames']]==
                    [r['position'] for r in data['original-flat-'+direction]['frames']]
                    for pack in ('v8','v11')) for direction in ('forward','left-forward')})
    captures={}
    for path in root.glob('render-*.json'):
        content=json.loads(path.read_text())
        if isinstance(content,dict) and 'captures' in content:captures[path.stem]=capture_report(content)
    for path in (root/'release-v11-capture-verified').glob('rendered-release-*.json'):
        captures[path.stem]=capture_report(json.loads(path.read_text()))
    report['captures']=captures
    (root/'movement-release-analysis.json').write_text(json.dumps(report,indent=2)+'\n')
    make_sheet(root)
    print(json.dumps({k:v for k,v in report.items() if k!='captures'},indent=2))


if __name__=='__main__':main()
