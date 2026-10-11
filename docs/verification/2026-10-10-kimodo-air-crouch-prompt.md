# Air 中段蹲缩 prompt 调整（2026-10-10）

按用户要求修改 `tools/kimodo_pipeline/air.py` 中的下落提示词。Fall、Fall_Fast、Flail 共用动作描述：每个周期中段弯曲双膝和髋部、短暂蹲缩，随后自然伸腿回到起始空中姿态。分别保留放松手臂、保护性展臂和双臂挥动。

共用英文描述为：

> A person in an upright airborne falling pose bends both knees and hips into a brief crouched tuck midway through each cycle, then smoothly extends the legs back to the starting airborne pose.

实际 prompt 随后拼接对应手臂风格，以及躯干直立、髋部高度稳定并在空中重复动作的描述。空中蹲缩通过收腿表达，角色整体下落仍由物理拥有。

本次核对实际 `inspect --scope air` 和生产图的三个生成节点，确认都含中段蹲缩及伸腿描述；图仍为 32 个节点、16 个交付身份。提示词之外的节点参数、依赖、时长、模型、seed、条件引用及门禁保持。检查证据保存在 `artifacts/kimodo_als/air-20261010_integration_v1/prompt-crouch-check.json`。

没有新 diffusion、转换或 Godot 动作验收；当前默认包与实际已生成资产保持。稀疏全身参考条件仍影响姿态，文字并不保证精确中段时间或蹲缩幅度；是否生成清晰蹲缩需在新批次核查。
