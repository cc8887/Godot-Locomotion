"""Summarize actual final-mesh samples and assemble the captured GPU frames."""
import argparse
import json
from pathlib import Path
import statistics


def summarize(path):
    data=json.loads(path.read_text(encoding='utf-8')); result=[]
    for name in dict.fromkeys(r['scenario'] for r in data['frames']):
        rows=[r for r in data['frames'] if r['scenario']==name and 90<=r['phase']<210]
        last=rows[-1]
        result.append(dict(scenario=name,samples=len(rows),gaits=sorted({r['gait'] for r in rows}),
            speedCmPerSecond=statistics.mean(r['speed'] for r in rows),
            maximumHorizontalPelvisOffsetMeters=max(r['horizontalPelvisOffsetMeters'] for r in rows),
            minimumSoleHeightCm=min(r[s]['MinimumSupportHeight'] for r in rows for s in ('left','right')),
            allContactLeft=last['qualityLeft'],allContactRight=last['qualityRight'],
            fullyPlantedLeft=last['plantedLeft'],fullyPlantedRight=last['plantedRight']))
    return result


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--matrix',type=Path,required=True)
    args=parser.parse_args(); root=args.matrix
    summaries={}; sequences={}
    for name in ('directional-single','directional-parallel','rendered-directional'):
        path=root/(name+'.json')
        if not path.exists():continue
        summaries[name]=summarize(path)
        rows=json.loads(path.read_text(encoding='utf-8'))['frames']
        # Physics/body samples exclude process-specific collider and instance IDs.
        fields=('tick','scenario','phase','speed','velocity','inputDirection','yaw','gait','stance','rotationMode','bones','horizontalPelvisOffsetMeters','actorPosition')
        sequences[name]=[{k:r[k] for k in fields} for r in rows]
    comparisons=[]
    baseline=sequences.get('directional-single')
    if baseline is not None:
        for name,rows in sequences.items():
            if name=='directional-single':continue
            differences=[i for i,(a,b) in enumerate(zip(baseline,rows)) if a!=b]
            comparisons.append(dict(other=name,frames=len(rows),referenceFrames=len(baseline),
                differingFrames=len(differences),firstDifferences=differences[:10],exact=len(rows)==len(baseline) and not differences))
    report=dict(scenarios=summaries,comparisons=comparisons,
        scope='Controlled ordinary input, flat floor, final skinned support geometry. Partial contact is not a planted-foot constraint. Zero planted samples means unavailable, not accepted.')
    (root/'directional-report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    captures=root/'rendered-directional'
    if captures.exists():
        from PIL import Image,ImageDraw,ImageFont
        modes=['walk','run','sprint','crouch','aim-walk','aim-run','aim-crouch']
        directions=['forward','back','left','right']
        for phase in (120,135,150,165):
            sheet=Image.new('RGB',(2560,7*390),'#222222');draw=ImageDraw.Draw(sheet)
            for y,mode in enumerate(modes):
                for x,direction in enumerate(directions):
                    path=captures/f'{mode}-{direction}-{phase}.png'
                    if not path.exists():continue
                    with Image.open(path) as im:sheet.paste(im.convert('RGB').resize((640,360)),(x*640,y*390+30))
                    draw.text((x*640+8,y*390+8),f'{mode} / {direction} / phase {phase}',fill='white')
            sheet.save(root/f'directional-contact-sheet-{phase}.png')
        closeups=Image.new('RGB',(1920,3*450),'#222222');draw=ImageDraw.Draw(closeups)
        font_path=Path('C:/Windows/Fonts/arial.ttf')
        font=ImageFont.truetype(str(font_path),22) if font_path.exists() else ImageFont.load_default()
        for y,mode in enumerate(('walk','run','crouch')):
            for x,direction in enumerate(directions):
                with Image.open(captures/f'{mode}-{direction}-150.png') as im:
                    closeups.paste(im.convert('RGB').crop((420,300,900,720)),(x*480,y*450+30))
                draw.text((x*480+10,y*450+3),f'{mode} / {direction}',font=font,fill='white')
        closeups.save(root/'directional-movement-closeups.png')
    print(json.dumps(comparisons,indent=2))


if __name__=='__main__':main()
