"""Publish only the audited BlendSpace initialization checkpoint."""
from pathlib import Path
import json,hashlib
repo=Path(__file__).resolve().parents[1];out=repo/'artifacts/lyra-analysis';tag='blendspace-initialize-v3'
sha=lambda b:hashlib.sha256(b).hexdigest()
audit_path=out/f'{tag}-integrity.json';audit=json.loads(audit_path.read_bytes())
assert audit['auditPassed'] and audit['godotProcesses']==78 and not audit['scope']['goalComplete']
ledger=out/f'{tag}-documentation.json';assert not ledger.exists()
doc=repo/'docs/verification/2026-10-04-lyra-blendspace-initialize.md';s=doc.read_text(encoding='utf-8');assert s.count('最终验证待审计。')==1
s=s.replace('最终验证待审计。','最终Debug/实际ExportRelease Optimize均0错误0警告，关联Core163项通过。每构建35个主矩阵场景加四布局完整Main、共39个进程，两构建合计78个进程全部退出0且无Godot ERROR/WARNING。新30种子记录、原五类瞄准/倾斜连续参考、原Sequence/阶段、初始self/Link/Unlink、取消重试、普通十角色/Emote及四布局final Rig均通过。瞄准每构建7560帧，Main Lean与合成各2100帧，Start/Cycle Lean各3780帧；四布局每构建4320帧及同数retry继续对照既有完整Main原生参考。原字段34/47与多组32/47的比较范围保持。初始/解绑计数及普通十角色/Emote完整JSON同两构建和source-initialize-v2基线。\n\n独立审计artifacts/lyra-analysis/blendspace-initialize-v3-integrity.json通过20份当前冻结源、其余1463条本批基线文件、870资源JSON、710原包、9项宿主/配置、23份逐字原UE源码副本和三轮Optimize六文件恢复。Optimize主DLL与Debug不同。原生参考用blendspace-initialize-v2与-repeat，最终运行与恢复用v3；Core沿未改动的Core代码使用v2-core.trx。原生最终两进程各744条原加载/GameplayTag Warning，无Error/Fatal/Ensure。')
doc.write_text(s,encoding='utf-8')
prefix=('最新Lyra BlendSpace初始化：沿用ALS68/raw69/logical81与十四入口，Provider实际79/74和Main实际22/16/12阶段回调已接真实宿主。'
        '原Main+三Provider两UE最终进程逐字同，15源/30非零种子，原Initialize/CacheBones确认时间/样本/滤波重置、'
        'Marker索引/full-weight重置、Delta/距离/权重/三角缓存及ActualAlpha/LOD历史保持；无标记源保留独立基础Marker。'
        'Debug/实际Optimize0错误警告、Core163、最终78个Godot进程通过，五类瞄准/倾斜连续参考保持；'
        '初始/解绑计数与前批同，四布局每构建4320帧retry及普通十角色/Emote完整报告保持。'
        '20源码/1463基线、870JSON/710原包/9配置、23原UE副本和三轮六文件恢复审计通过，见[BlendSpace初始化验证]({link})。'
        '**仅关闭指定固定配置的可表示BlendSpace初始化字段与实际阶段接线；private滤波/全部Source私有、非零Aiming参数原生传播、'
        '骨控制初始化、阶段/cache统一、后续整图重初始化、RequiredBones/LOD与Rig Construction仍开放。**'
        'self scalar/部分绑定/重复调用/其它Provider、字段34/47、物理314/1680差异、近景和全部暂缓项保持，完整目标active。'
        '首次二维滤波断言与Marker参数接线失败证据保留；最终runtime v3、原生v2。无新GPU/全量/十分钟/性能、原资产保存重导、提交推送。\n\n')
rows=[]
for path,link in [('ROADMAP.md','docs/verification/2026-10-04-lyra-blendspace-initialize.md'),('docs/verification/2026-10-03-lyra-als-interface-review.md','2026-10-04-lyra-blendspace-initialize.md')]:
 p=repo/path;old=p.read_bytes();new=prefix.format(link=link).encode('utf-8');p.write_bytes(new+old);assert p.read_bytes()[len(new):]==old
 rows.append(dict(path=path,priorSha256=sha(old),currentSha256=sha(p.read_bytes()),prefixBytes=len(new),suffixPreserved=True))
with ledger.open('x',encoding='utf-8') as f:
 json.dump(dict(auditSha256=sha(audit_path.read_bytes()),auditPassed=True,goalComplete=False,documents=rows,verificationPath=doc.relative_to(repo).as_posix(),verificationSha256=sha(doc.read_bytes()),publisherSha256=sha(Path(__file__).read_bytes())),f,indent=2)
print('LYRA_BLENDSPACE_INITIALIZE_DOCUMENTATION_OK documents=3 previousSuffixesPreserved=2 goalComplete=false')
