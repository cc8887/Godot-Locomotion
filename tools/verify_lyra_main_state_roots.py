"""Verify actual StateResult root captures and their preserved resource dependencies."""
import argparse
import json
from pathlib import Path
from locomotion_paths import project_path

from verify_lyra_main_source_stop import verify

if __name__=='__main__':
    parser=argparse.ArgumentParser()
    parser.add_argument('--root',type=Path,default=Path('assets/generated/lyra_als'))
    parser.add_argument('--content',type=Path,default=project_path('Content'))
    parser.add_argument('--output',type=Path,default=Path('artifacts/lyra-analysis/main-state-roots-resource-verification.json'))
    args=parser.parse_args();report=verify(args.root,args.content,prefix='main_state_roots',state_roots=True)
    args.output.write_text(json.dumps(report,indent=2),encoding='utf-8')
    print('LYRA_MAIN_STATE_ROOTS_VERIFY_OK '+json.dumps({'counts':report['counts'],'extra':report['extra']}))
