# Mantle 曲线双向交付文档核对（2026-10-10）

本次按用户要求将身体避障前移的计算和 Pipeline／导入项目的配套依赖写入稳定文档。只修改文档，保留已有代码与资产修改，不生成资源、不改活动包、不提交或推送。既有真实 Godot 验证范围仍以[中高攀爬记录](2026-10-10-medium-high-mantle.md)为准，本次不声明新运行验收。

## 核对依据

- `tools/kimodo_pipeline/mantle_contacts.py`：最终 Root 前移投影、High 最终蒙皮前缘、0.5 cm 余量、未来上限、200 cm/s 增量限制、0.001 cm 原落点一致性及控制 JSON 字段。
- `tools/kimodo_pipeline/publish.py`：stage 生成包内控制文件并登记载荷摘要；`validate_mantle_pack()` 在正式启用门禁重新烘焙比较。
- `src/Als.Godot/Animation/AlsMantlingDemoResources.cs`：包内文件优先，摘要匹配的独立补烘焙为诊断后备，缺失时报错，编译后替换运动 profile 的进度。
- `src/Als.Import/Compilation/AlsMantlingContactCompiler.cs`：版本／策略／预算、最终输入摘要、sequence 集合、采样时间及剩余高度关系校验；当前不独立验证 `bodyAdvanceControl` 的 mesh 摘要或重算避障几何。
- `src/Als.Core/Actions/AlsMantlingProgressCurve.cs`、`AlsMantlingRootMotion.cs` 与 Godot 运行开关：同一 montage 时钟独立求值前移／抬升；完整接触模式读取控制资源，Root 开启保留原生路径，显式关闭手锁的负对照使用 Root 派生进度。

稳定合同集中在[ALS Mantle 控制](../architecture/als-mantle-contact-control.md)，生成导出策略、完整离线入口和 Pipeline README 记录生产端要求并链接该合同；文档导航与路线图同步更新。未引入新的 manifest 能力声明字段，其他导入项目的支持需由实际集成／运行验证确认。

## 验证边界

本次对七份文档检查 329 个相对链接，目标文件均存在；检查交付合同锚点、重复章节和空白，`git diff --check` 通过。新增描述与上述当前代码核对一致。未重跑 Python／.NET 测试、资产 pipeline 或 Godot 游戏；文档更新不改变既有通过与失败结果。
