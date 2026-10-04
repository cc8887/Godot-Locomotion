"""Publish the audited source initialization checkpoint, preserving prior bytes."""
from pathlib import Path
import json,hashlib
repo=Path(__file__).resolve().parents[1]
out=repo/'artifacts/lyra-analysis'
tag='source-initialize-v2'
audit_path=out/f'{tag}-integrity.json'
audit=json.loads(audit_path.read_bytes())
assert audit['auditPassed'] and audit['godotProcesses']==66 and not audit['scope']['goalComplete']
ledger=out/f'{tag}-documentation.json'
assert not ledger.exists()
sha=lambda b:hashlib.sha256(b).hexdigest()
doc=repo/'docs/verification/2026-10-03-lyra-sequence-initialize.md'
s=doc.read_text(encoding='utf-8')
assert s.count('最终验证待审计。')==1
s=s.replace('最终验证待审计。','最终Debug/实际ExportRelease Optimize均0错误0警告；关联Core134项通过。每构建29个主矩阵场景加四布局完整Main、共33个进程，两构建合计66个进程全部退出0、无Godot ERROR/WARNING。新156记录、八类旧源组件、原阶段、初始self/Link/Unlink、取消重试、普通十角色/Emote与四布局final Rig均通过。初始/解绑计数与graph-phases-v1逐项相同，普通十角色/Emote完整JSON也相同。四布局每构建4320帧及同数retry继续对照既有原生参考，原字段34/47和32/47范围保留。\n\n独立审计为artifacts/lyra-analysis/source-initialize-v2-integrity.json：33份当前冻结源、其余本批1463条基线受控文件、870份资源JSON、710个原包、9项宿主/配置和16份原UE源码副本保护通过；三轮Optimize六文件恢复精确，Optimize主DLL与Debug不同。新原生参考使用source-initialize-v1及-repeat标签，最终运行和恢复用source-initialize-v2标签。没有新GPU/全量managed/十分钟/性能或完整物理等价验收。')
doc.write_text(s,encoding='utf-8')
prefix=('最新Lyra Sequence源初始化：继续ALS68/raw69/logical81及原十四入口，真实Provider阶段已进入Sequence宿主。'
        '三Provider各26源，原UE两次独立捕获78节点/156种子记录与CacheBones前后，三JSON逐字同、退出0；'
        'Evaluator保留内部/explicit、Player重设原起点，Marker距离/Delta/已有权重保留，回调留首次Update。'
        'HipFire隐藏reset/取消重试和原null LeftHand源待标记消费已接角色候选。'
        'Debug/实际Optimize0错误警告、Core134，最终66个Godot进程通过；'
        '初始/解绑计数与前批同，四布局每构建4320帧retry、普通十角色/Emote完整报告保持。'
        '33源码、870JSON/710原包/9配置、16原UE副本及三轮六文件恢复审计通过，见[Sequence源初始化验证]({link})。'
        '**只关闭指定Sequence时钟/Marker/Delta/资产启动字段及待标记接入；private/full-weight、外置Cycle/Hip权重逐项比较、'
        'BlendSpace与骨控制初始化、阶段/cache统一、后续完整重初始化、RequiredBones/LOD和Rig Construction继续开放。**'
        'self scalar/部分绑定/重复调用/其它Provider、原字段34/47、物理314/1680差异、近景及全部暂缓项保持，整个目标active。'
        '无新GPU/全量/十分钟/性能、原资源保存重导、提交推送。下方源初始化仅首次Prepare待消费的表述属于此前范围。\n\n')
rows=[]
for path,link in [('ROADMAP.md','docs/verification/2026-10-03-lyra-sequence-initialize.md'),('docs/verification/2026-10-03-lyra-als-interface-review.md','2026-10-03-lyra-sequence-initialize.md')]:
 p=repo/path;old=p.read_bytes();new=prefix.format(link=link).encode('utf-8');p.write_bytes(new+old);assert p.read_bytes()[len(new):]==old
 rows.append(dict(path=path,priorSha256=sha(old),currentSha256=sha(p.read_bytes()),prefixBytes=len(new),suffixPreserved=True))
with ledger.open('x',encoding='utf-8') as f:
 json.dump(dict(auditSha256=sha(audit_path.read_bytes()),auditPassed=True,goalComplete=False,documents=rows,verificationPath=doc.relative_to(repo).as_posix(),verificationSha256=sha(doc.read_bytes()),publisherSha256=sha(Path(__file__).read_bytes())),f,indent=2)
print('LYRA_SEQUENCE_INITIALIZE_DOCUMENTATION_OK documents=3 previousSuffixesPreserved=2 goalComplete=false')
