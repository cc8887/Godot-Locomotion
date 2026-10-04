"""Prepend a verified checkpoint while preserving the prior document bytes."""
from pathlib import Path
import hashlib
import json

repo = Path(__file__).resolve().parents[1]
out = repo / 'artifacts/lyra-analysis'
tag = 'graph-phases-v1'
audit_path = out / f'{tag}-integrity.json'
audit = json.loads(audit_path.read_bytes())
assert audit['auditPassed'] and audit['godotProcesses'] == 48
assert not audit['scope']['goalComplete']
ledger = out / f'{tag}-documentation.json'
assert not ledger.exists()
sha = lambda b: hashlib.sha256(b).hexdigest()
text = ('最新 Lyra Provider启动与持久阶段历史：继续使用ALS68蒙皮/raw69/logical81及原十四入口合同。'
        '真实实例按绑定函数执行启动阶段，未访问Pivot在Link后进入0状态；Main控制器跨绑定保留历史，'
        'Provider独立保存原BasePose与状态骨缓存counter/global frame，同类保留、新类/重连新建。'
        '原Main+三Provider受控阶段两UE进程0退出、request/native/closure字节同；'
        '每类Initialize56观察项、十个缓存上下文56/34访问，涵盖重复/回退/同counter异帧/有符号回绕，'
        '机器状态及阶段末26源时钟读取，四个非反射StateResult探针限制明确。'
        'Debug/实际Optimize0错误警告，Core131及最终48个Godot进程通过；'
        '初始/解绑计数与前批同，四布局完整Main每构建4320帧retry、普通十角色/Emote完整报告保持。'
        '36源码、870JSON/710原包/9配置、12原UE副本和三轮六文件恢复审计过，见 '
        '[Provider阶段验证]({link})。'
        '**只关闭固定ALS81的真实启动接入与持久阶段计数；源初始化仍有首次Prepare待消费指令，'
        '阶段与Update/Evaluate缓存统一所有权、后续完整重初始化、自然组件绝对计数/RequiredBones/LOD、'
        'Rig Construction及新ALS默认最终native继续开放。**'
        'self scalar/部分绑定/重复函数调用、原字段34/47、物理314/1680差异、近景及全部开放/暂缓项保持，'
        '整个目标active。无新GPU/全量managed/十分钟/性能、原资源保存重导、提交推送。'
        '下方阶段控制器只构造期、Provider尚未接入的描述属于此前批次。\n\n')
rows = []
for path, link in [('ROADMAP.md','docs/verification/2026-10-03-lyra-provider-graph-phases.md'),
                   ('docs/verification/2026-10-03-lyra-als-interface-review.md','2026-10-03-lyra-provider-graph-phases.md')]:
    p = repo / path
    previous = p.read_bytes()
    prefix = text.format(link=link).encode('utf-8')
    assert not previous.startswith(prefix)
    p.write_bytes(prefix + previous)
    current = p.read_bytes()
    assert current[len(prefix):] == previous
    rows.append(dict(path=path, priorSha256=sha(previous), currentSha256=sha(current),
                     prefixBytes=len(prefix), suffixPreserved=True))
doc = repo / 'docs/verification/2026-10-03-lyra-provider-graph-phases.md'
with ledger.open('x',encoding='utf-8') as f:
    json.dump(dict(auditSha256=sha(audit_path.read_bytes()), auditPassed=True, goalComplete=False,
                   documents=rows, verificationPath=doc.relative_to(repo).as_posix(),
                   verificationSha256=sha(doc.read_bytes()), publisherSha256=sha(Path(__file__).read_bytes())),f,indent=2)
print('LYRA_GRAPH_PHASE_DOCUMENTATION_OK documents=3 previousSuffixesPreserved=2 goalComplete=false')
