import json
from pathlib import Path
import unreal
repo=Path(__file__).resolve().parents[2]
root=repo/'assets/generated/lyra_als'
contracts=json.loads((root/'linked_layer_contracts.json').read_bytes())
classes=[v['class'] for k,v in contracts['classes'].items() if k!='interface']
policy=json.loads(unreal.LyraNotifyDispatchOracleLibrary.read_policy(json.dumps(dict(classes=classes))))
(repo/'artifacts/lyra-analysis/notify-dispatch-diagnostic-policy.json').write_text(json.dumps(policy,indent=2),encoding='utf-8')
assets=json.loads((root/'notify_contract_v1.json').read_bytes())['assets']
a=next(a for a in assets if any('TransitionToLocomotion' in e['notifyStateClass'] for e in a['events']))
e=next(e for e in a['events'] if 'TransitionToLocomotion' in e['notifyStateClass'])
def frame(windows):return dict(delta=1/60,replaceHistory=True,removeHandlers=False,windows=windows)
w=dict(asset=a['source'],previous=e['triggerTime']-.01,delta=.02,current=e['triggerTime']+.01,looping=False,active=True,leader=True,weight=1)
trace=json.loads(unreal.LyraNotifyDispatchOracleLibrary.read_trace(json.dumps(dict(traces=[dict(frames=[frame([w]),frame([]),frame([])])]))))
(repo/'artifacts/lyra-analysis/notify-dispatch-diagnostic-native.json').write_text(json.dumps(trace,indent=2),encoding='utf-8')
unreal.log('LYRA_NOTIFY_DISPATCH_DIAGNOSTIC_OK')
