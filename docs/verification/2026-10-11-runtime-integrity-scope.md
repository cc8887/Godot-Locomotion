# 2026-10-11 运行时资源完整性校验范围

在 `F:/Github/Godot-Locomotion` 主目录验证。活动包指针和生成目录未修改。

## 范围

- `resource-pack.files` 继续保留完整交付审计清单，供发布和离线验证使用。
- 普通启动默认不遍历完整文件数组、不读取载荷、不计算 SHA-256。
- 设置 `ALS_VERIFY_RESOURCE_PACK_HASHES=1` 或 `true` 后，仅校验 V4 `als_manifest.json`、其登记的导入资源，以及完整清单已有摘要的 compiled animation set。新包由发布器写入 `runtimeAssetIntegrityFiles`；旧包从 manifest 和完整清单推导相同范围。
- 当前活动诊断包有 1,536 个完整清单项。V4 manifest 登记 137 个 FBX 和 4 个 PNG；加 manifest 和 compiled animation set 共 143 项。其余 1,393 项（配置/raw、曲线侧车、root-ownership 和阶段测试／诊断证据等）不进入运行时哈希集，文件仍保留。配置/raw JSON 仍按现有代码路径读取；本改动只限制摘要校验范围。

## 验证

- .NET 优化构建通过：`dotnet exec 'C:\Program Files\dotnet\sdk\10.0.300\MSBuild.dll' GodotALS.csproj -p:Restore=false -p:Optimize=true -v:minimal`。
- Godot 4.7.2 Mono 普通 `als_demo.tscn` headless 启动通过。关闭校验输出 `enabled=False verified=0 skipped=1536`；设置 `ALS_VERIFY_RESOURCE_PACK_HASHES=1` 后，旧格式活动包兼容路径输出 `enabled=True verified=143 skipped=1393`，无 `ERROR:`。
- 使用 Python 标准库从 `publish.py` 提取并执行实际筛选函数，对活动包完成 143/1,536 计数、诊断路径排除和缺失资源拒绝检查。
- `python -m unittest tools.kimodo_pipeline.test_publish` 未能启动：系统 Python 3.14 环境缺少 `scipy`。因此该模块内新增的发布器回归用例以及完整 Python 测试套件未执行；没有重跑完整 Kimodo stage/pipeline。
- 本次涉及文件的 `git diff --check` 通过。

本次仅缩小运行时可选哈希集；不代表完整发布审计、资源导入清单或诊断证据被删除，也不改变当前包仍为 `diagnostic` 的状态。
