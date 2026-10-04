"""Publish only the independently audited fixed ALS81 skeletal checkpoint."""
from pathlib import Path
import json
import hashlib

repo = Path(__file__).resolve().parents[1]
out = repo / 'artifacts/lyra-analysis'
tag = 'skeletal-initialize-v6'
sha = lambda b: hashlib.sha256(b).hexdigest()
audit_path = out / f'{tag}-integrity.json'
audit = json.loads(audit_path.read_bytes())
assert audit['auditPassed'] and audit['godotProcesses'] == 98 and not audit['scope']['goalComplete']
ledger = out / f'{tag}-documentation.json'
assert not ledger.exists()
doc = repo / 'docs/verification/2026-10-04-lyra-skeletal-initialize.md'
s = doc.read_text(encoding='utf-8')
assert s.count('最终验证待审计。') == 1
s = s.replace('最终验证待审计。',
    '最终 Debug 与实际 ExportRelease Optimize 均0错误0警告，关联 Core163项通过。每构建45个主矩阵场景加四布局完整 Main，共49个 Godot 进程；两构建98个进程全部退出0且无 Godot ERROR/WARNING。初始化24节点/48种子、162骨引用及真实缓存门禁通过。骨控制更新、FootPlacement、LegIK、组合骨控制各3780帧连续参考；Main Skeletal/Composition/两个 MainPose 各11340帧，Main Rig7560帧。Sequence/BlendSpace/原阶段、初始 self/Link/Unlink、普通十角色/Emote及四布局最终 Rig 均通过。\n\n'
    '四布局每构建4320帧及同数 retry 对照既有完整 Main 原生参考，字段34/47与多组32/47比较范围保持。初始/解绑计数及普通十角色/Emote完整 JSON 同 Debug/Optimize 和 source-initialize-v2 基线。独立审计通过24份冻结源、其余1474条基线文件、870资源 JSON、710原包、9宿主/配置、23逐字原 UE 源码副本及三轮 Optimize 六文件恢复；Optimize 主 DLL 与 Debug 不同。原生最终两进程各744条既有加载/GameplayTag Warning，无 Error/Fatal/Ensure。最终运行证据 skeletal-initialize-v6，原生参考 v2/v2-repeat，probe package-v4。')
doc.write_text(s,encoding='utf-8')
prefix = (
    '最新Lyra骨控制初始化：ALS skin68/raw69/logical81与十四入口保持，三个Provider真实八节点Initialize/CacheBones已接原阶段。'
    '原Main/Provider两UE进程24节点48种子逐字同，Alpha bool/clamp仅清initialized、ActualAlpha与嵌入值保持；'
    'Foot定义插值重置、Delta/短counter/character/root保持，Leg同FK历史保持，真实27骨引用/Provider与参考长度完成缓存。'
    '生产延迟绑定、缺缓存先于写姿态拒绝，重复缓存不清历史。Debug/实际Optimize0错误警告、Core163、98个Godot进程通过；'
    '骨控制四组3780帧、Main组合/姿态11340帧、Rig7560帧，普通十角色/Emote与初始/解绑计数保持，'
    '四布局每构建4320帧及同数retry。24源码/1474基线、870JSON/710原包/9配置、23原UE副本与三轮六文件恢复审计通过，'
    '见[骨控制初始化验证]({link})。**仅关闭固定ALS81八节点定义明确的初始化字段、实际阶段接线及骨绑定；'
    '完整Foot/Leg私有存储/全局counter、阶段与Update/Evaluate缓存统一、后续整图重初始化、RequiredBones/LOD及Rig Construction仍开放。**'
    '非零Aiming原生传播、self scalar/部分绑定/重复调用/其它Provider、字段34/47及32/47、物理314/1680差异与全部暂缓项保持，完整目标active。'
    '构建/骨架投影精度/隐藏参数与完整启动夹具失败证据保留；实际参数传播与阈值未改。最终runtime v6、native v2、probe package-v4。'
    '无新GPU/全量/十分钟/性能验收、原资产保存重导或提交推送。\n\n'
)
rows = []
for path,link in (
    ('ROADMAP.md','docs/verification/2026-10-04-lyra-skeletal-initialize.md'),
    ('docs/verification/2026-10-03-lyra-als-interface-review.md','2026-10-04-lyra-skeletal-initialize.md')):
    p = repo / path
    old = p.read_bytes()
    new = prefix.format(link=link).encode('utf-8')
    p.write_bytes(new + old)
    assert p.read_bytes()[len(new):] == old
    rows.append(dict(path=path,priorSha256=sha(old),currentSha256=sha(p.read_bytes()),prefixBytes=len(new),suffixPreserved=True))
with ledger.open('x',encoding='utf-8') as f:
    json.dump(dict(auditSha256=sha(audit_path.read_bytes()),auditPassed=True,goalComplete=False,documents=rows,
                   verificationPath=doc.relative_to(repo).as_posix(),verificationSha256=sha(doc.read_bytes()),
                   publisherSha256=sha(Path(__file__).read_bytes())),f,indent=2)
print('LYRA_SKELETAL_INITIALIZE_DOCUMENTATION_OK documents=3 previousSuffixesPreserved=2 goalComplete=false')
