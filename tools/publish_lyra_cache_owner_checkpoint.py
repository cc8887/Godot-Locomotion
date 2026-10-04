"""Publish the audited cache ownership checkpoint without closing the goal."""
from pathlib import Path
import json
import hashlib

repo = Path(__file__).resolve().parents[1]
out = repo / 'artifacts/lyra-analysis'
tag = 'cache-owner-v4'
sha = lambda b: hashlib.sha256(b).hexdigest()
audit_path = out / f'{tag}-integrity.json'
audit = json.loads(audit_path.read_bytes())
assert audit['auditPassed'] and audit['godotProcesses'] == 62 and not audit['scope']['goalComplete']
doc = repo / 'docs/verification/2026-10-04-lyra-cache-owner.md'
s = doc.read_text(encoding='utf-8')
assert s.count('最终验证待审计。') == 1
s = s.replace('最终验证待审计。',
    f"最终Debug与实际ExportRelease Optimize均0错误0警告，关联Core187项通过。两构建各27个主矩阵场景加四布局完整Main，共62个Godot进程，全部退出0且无Godot ERROR/WARNING。实际owner原生90行/1620计数字段、MainCache2520帧、CachePose1260帧、Slot47610帧、MainPose/反馈/合成各11340帧、MainRig7560帧、原阶段、初始self/Unlink及普通十角色/Emote通过。四布局每构建4320帧及同数retry保留原最终Rig/字段门槛，初始/解绑计数和普通完整报告同Debug/Optimize及既有基线。\n\n"
    f"独立审计通过27份冻结实现源、其余{audit['protectedOtherSources']}条基线文件、870资源JSON、710原UE包、9宿主配置和31份本机原UE逐字源码副本；三轮Optimize六程序集备份恢复通过，实际Optimize主DLL与Debug不同。原生两进程既有加载/GameplayTag Warning各{audit['nativeWarnings']['first']}条，无Error/Fatal/Ensure。最终runtime v4、native v2/v2-repeat、package-v5；失败v3的27源与十二程序集审计通过。")
doc.write_text(s,encoding='utf-8')
prefix = (
    '最新Lyra缓存统一所有权：ALS68/69/81与十四入口保持，真实Main78/83及Provider78共同历史贯通阶段、延迟选主权重和Main求值作用域，候选统一取消/提交。'
    '新作用域同计数重采样、嵌套恢复重算、骨缓存使Evaluation失效；原UpdateCounter不新增相关性时钟。'
    '原Main/三Provider两UE进程90行逐字同，Godot1620计数字段/234源求值/36retry/12嵌套精确通过。'
    'Debug/实际Optimize0错误警告、Core187、62个Godot进程通过；MainPose增加实际缓存历史、权重、取消/提交、update-only及角色隔离断言，'
    '四布局每构建4320最终Main帧retry，普通十角色/Emote与初始/解绑报告保持。27源/其余{protected}基线、870JSON/710原包/9配置/31原UE副本与三轮六程序集恢复审计过，'
    '见[缓存所有权验证]({link})。**仅关闭固定节点共同历史及Main组合路径；自然Proxy绝对计数、完整阶段与后续重初始化、RequiredBones/LOD、Rig Construction、直接组件/任意图求值仍开放。**'
    'self scalar/部分绑定/重复调用/其它Provider、非零Aiming原生传播、私有字段、物理314/1680与全部暂缓项保留，完整目标active。'
    '探针构建、入口隐式递增和retry覆盖计数失败证据保留，算法/阈值未改。最终runtime v4/native v2/package-v5，无新GPU/全量/十分钟/性能或原资产保存重导、提交推送。\n\n'
)
rows = []
for path,link in (
    ('ROADMAP.md','docs/verification/2026-10-04-lyra-cache-owner.md'),
    ('docs/verification/2026-10-03-lyra-als-interface-review.md','2026-10-04-lyra-cache-owner.md')):
    p = repo / path
    old = p.read_bytes()
    new = prefix.format(link=link,protected=audit['protectedOtherSources']).encode('utf-8')
    p.write_bytes(new+old)
    assert p.read_bytes()[len(new):] == old
    rows.append(dict(path=path,priorSha256=sha(old),currentSha256=sha(p.read_bytes()),prefixBytes=len(new),suffixPreserved=True))
with (out / f'{tag}-documentation.json').open('x',encoding='utf-8') as f:
    json.dump(dict(auditSha256=sha(audit_path.read_bytes()),auditPassed=True,goalComplete=False,documents=rows,
                   verificationPath=doc.relative_to(repo).as_posix(),verificationSha256=sha(doc.read_bytes()),
                   publisherSha256=sha(Path(__file__).read_bytes())),f,indent=2)
print('LYRA_CACHE_OWNER_DOCUMENTATION_OK documents=3 suffixPreserved=2 goalComplete=false')
