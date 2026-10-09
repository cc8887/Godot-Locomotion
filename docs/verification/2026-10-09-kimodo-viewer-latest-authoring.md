# Motion Viewer 最新生成批次接入（2026-10-09）

## 原因与修复

旧 Viewer 的①②③固定读取 `assets/generated/kimodo_ue/als_regen` 历史中间目录；④按活动指针读取 `als_nonprop_20261007_v8`。上一轮真实生成产物保存于 `artifacts/tests/2026-10-08-directional-gait-regeneration-v1` 的原生 NPZ 节点缓存，未重定向、未发布，原界面也没有这些文件的播放适配和目录入口。刷新浏览器不能改变资源来源。

新增原生 NPZ→保留原点/所有帧的预览导出与精确回执登记。Runner 结束或门禁拒绝时登记已有输出；前端默认最新登记的源候选，提供批次选择、母动作阶段、刷新及状态说明。加载期间禁用批次/阶段切换，阶段请求按请求版本取结果，刷新清除旧信息。历史中间目录明确标注历史，活动包仍从当前配置读取。

## 实际登记

| 批次 | 源候选 | 母动作 | 状态 |
| --- | --- | --- | --- |
| `dense-cycle-probe-f3252cee5505` | 3 | 0 | 最新密集周期试验，源检查未通过、未发布 |
| `final-profile-v2-999a176553cb` | 5 | 8 | Walk/Crouch 母动作源检查通过，未发布 |

这些是上一轮生成数据的预览登记，本次未重新生成动作。复用的绑定输入和原版教师未冒充新资源；未处理成功的 Run/Sprint 没有母动作条目。用户随后指出的 LF 方向语义、加速及完整循环问题仍需后续重新生成与验收。

## 验证与证据

- Python Pipeline 134 项通过：[日志](../../artifacts/tests/2026-10-08-directional-gait-regeneration-v1/viewer-pipeline-tests-20261009.log)。其中新增 3 项验证全帧原点/旋转、回执变化拒绝和绑定输入排除；这 3 项单独复跑通过。
- Viewer 服务器 3 项通过：活动包溯源、精确批次选择/损坏索引拒绝、预览路径边界。Node 原点对照 3 项通过，main/source-motion/viewport 语法检查通过。
- 实际 HTTP 默认批次、两批次文件与源 NPZ/预览字节哈希、帧数及活动包摘要核对：[API 证据](../../artifacts/tests/2026-10-08-directional-gait-regeneration-v1/viewer-api-verification-20261009.json)。
- 真实浏览器加载最新 `ALS_N_Run_LF`，73 帧、30 Hz、2.4 秒。首帧 Hips `(0,86.02,0)` cm，左右脚与手的世界坐标和源 NPZ FK 计算一致（页面取整显示）；可切换到 8 个源通过的母动作并刷新回最新候选。
- [最新 LF 浏览器截图](../../artifacts/tests/2026-10-08-directional-gait-regeneration-v1/viewer-latest-run-lf-20261009.jpg)。这是源骨架 Viewer 截图。

活动包摘要仍为 `023189034086a952ef116044c218e3e45475984f17e65ceda01d6be20f06485d`。本次只修改生产可见性和展示，不认证动作质量，不改变活动指针，未执行新 Godot E2E/目标蒙皮测试，未提交推送。
