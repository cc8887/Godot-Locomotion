"""Publish the verified checkpoint while retaining historical document bytes."""
from pathlib import Path
import hashlib
import json

repo = Path(__file__).resolve().parents[1]
out = repo/'artifacts/lyra-analysis'
tag = 'proxy-phase-v4'
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
read = lambda p: json.loads(p.read_bytes())
integrity = read(out/f'{tag}-integrity.json')
assert integrity['passed'] and integrity['godotProcesses'] == 62 and integrity['corePassed'] == 170
assert not integrity['naturalComponentCounters'] and not integrity['fullPhaseScheduler'] and not integrity['goalComplete']
assert (out/f'{tag}-optimize-property.log').read_text(encoding='utf-8-sig').strip() == 'true'
for p, digest in read(out/f'{tag}-frozen-sources.json').items(): assert sha(repo/p) == digest, p
before = read(out/'proxy-phase-v1-before.json')
baseline = out/f'{tag}-document-baselines'
baseline.mkdir(exist_ok=False)
docs = ('ROADMAP.md', 'docs/verification/2026-10-03-lyra-als-interface-review.md')
for name in docs:
    assert sha(repo/name) == before[name], name
    with (baseline/Path(name).name).open('xb') as f: f.write((repo/name).read_bytes())
body = (
 '最新Lyra Proxy求值计数：ALS68/69/81与十四入口保持，Main实际根求值counter已与视图序号分开，'
 '角色候选推进/取消恢复/统一提交；修正Rebind新Slot漏传实际CacheOwner，并新增真实初始self/解绑重连断言。'
 '原UE两独立进程31步/三Proxy、65536次根回绕、零counter写入，requests/native/closure逐字同；'
 'Core170/768计数标量、Debug与实际Optimize0错误警告、62个Godot进程通过，'
 'MainPose/反馈各11340帧、Slot47610、四布局每构建4320最终Main帧retry，普通十角色/Emote报告与前批及两构建相同。'
 '15源码/其余4339基线、870JSON/710原包/9配置/7原UE源码与三轮六程序集恢复审计通过。'
 '**仅关闭原入口counter规则、生产Evaluation候选和换层Slot实际owner；受控外部frame非自然组件调度，'
 '生产Update/完整阶段、后续整图重初始化、RequiredBones/LOD与Rig Construction继续开放。**'
 'self scalar/部分/任意重复调用/其它Provider、非零Aiming原生传播、全部私有字段、物理314/1680及全部暂缓项保持；'
 '完整目标active。失败包/SDK目录与首次测试计数断言保留，无原资产保存重导、GPU/全量/十分钟/性能或提交推送。'
 '见[Proxy阶段验证]({link})。\r\n\r\n'
)
for name in docs:
    link = 'docs/verification/2026-10-04-lyra-proxy-phase.md' if name == 'ROADMAP.md' else '2026-10-04-lyra-proxy-phase.md'
    original = (baseline/Path(name).name).read_bytes()
    (repo/name).write_bytes(body.format(link=link).encode('utf-8') + original)
    assert (repo/name).read_bytes().endswith(original), name
document = repo/'docs/verification/2026-10-04-lyra-proxy-phase.md'
text = document.read_text(encoding='utf-8')
pending = 'Godot完整回归正在执行，最终进程结果与独立资源保护审计完成前，本节不作为运行验收结论。'
assert text.count(pending) == 1
verified = (
 '最终Debug与实际ExportRelease Optimize共62个Godot进程全部通过，无Godot ERROR/WARNING。'
 'MainPose和真实反馈各11340帧、Slot合成47610帧、最终Rig7560帧及原阶段、初始self、Unlink/reLink、'
 '默认根、武器/通知、普通十角色/Emote通过。四布局每构建4320最终Main帧及同数retry保留原姿态与字段门槛；'
 '普通完整报告与前批及Debug/Optimize相同。\n\n'
 f'独立审计通过{integrity["sources"]}份冻结源码、其余{integrity["otherProtectedFiles"]}条基线、'
 f'{integrity["assets"]}份资产JSON、{integrity["originalPackages"]}个原UE包、'
 f'{integrity["hostConfigurations"]}份宿主配置和{integrity["engineSources"]}份本机引擎源码。'
 'Optimize三轮六程序集备份恢复通过，当前为已验证Debug程序集。'
 f'原UE两进程加载警告各{integrity["originalWarnings"][0]}条保留，无Error/Fatal/Ensure；'
 '最终package-v4/native-v1/runtime-v4。没有保存或重导原资产、修改引擎/原项目源码配置、提交或推送。'
)
document.write_text(text.replace(pending, verified), encoding='utf-8')
hashes = {p: sha(repo/p) for p in (*docs, document.relative_to(repo).as_posix())}
for name in docs:
    original = (baseline/Path(name).name).read_bytes()
    assert sha(baseline/Path(name).name) == before[name]
    assert (repo/name).read_bytes().endswith(original)
for p, digest in read(out/f'{tag}-frozen-sources.json').items(): assert sha(repo/p) == digest, p
with (out/f'{tag}-documentation.json').open('x', encoding='utf-8') as f:
    json.dump(dict(passed=True, documentSha256=hashes, historicalSuffixPreserved=True,
        publisherSha256=sha(Path(__file__)), integritySha256=sha(out/f'{tag}-integrity.json'), goalComplete=False), f, indent=2)
print('Proxy phase checkpoint published; historical document bytes preserved.')
