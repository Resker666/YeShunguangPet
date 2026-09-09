# 质量门禁

本轮首先覆盖计时链路，沿用现有 .NET 8 / Windows x64 测试程序，不需要额外测试包、账号或真实用户数据。测试只创建隔离的日志和测试状态；窗口循环使用不可见的测试窗口，不读取个人桌面截图，不写启动项。

## 本地运行

在仓库根目录使用 PowerShell，先运行完整回归：

```powershell
dotnet build --configuration Release
dotnet run --project tests/PetTests.csproj --configuration Release -- . artifacts/qa
dotnet run --project tests/PetTests.csproj --configuration Release --no-build -- . artifacts/ui-smoke --ui-smoke
dotnet run --project tests/PetTests.csproj --configuration Release --no-build -- . artifacts/quality --quality
```

质量模式独立运行控制器契约、随机序列、性能、截图和窗口生命周期检查。退出码不为 0 就是失败，不以“有输出图片”判断成功。

额外延长窗口循环，例如 3 分钟或 30 分钟：

```powershell
dotnet run --project tests/PetTests.csproj --configuration Release --no-build -- . artifacts/quality-soak --quality 180
dotnet run --project tests/PetTests.csproj --configuration Release --no-build -- . artifacts/quality-soak-long --quality 1800
```

时长范围为 0-7200 秒；0 表示至少 30 次窗口循环，不表示跳过。计时使用可控时钟，因此无需实际等待一次 25 分钟专注。长循环测的是反复打开、切换和关闭面板的资源回收，不等同于整机多小时真实使用。

## 检查内容

| 门禁 | 方法 | 默认预算/要求 |
| --- | --- | --- |
| 控制器契约 | 不创建窗口，验证输入、失败回退、命令锁、保存顺序和 Dispose | 全部通过 |
| 随机状态序列 | 32 个固定种子，每个 4,000 步，与独立减法计时模型逐步对照 | 128,000 步全部一致 |
| 截图回归 | 完整/紧凑/迷你 x 浅色/深色，96 DPI 固定客户端、关闭装饰动画 | 平均差异 <= 0.035；高差异块比例 <= 8%；最差局部区域 <= 0.10 |
| 窗口释放 | 预热后循环真实 WPF 窗口，保持 Runtime 存活，回收后检查弱引用 | 窗口和控制器残留为 0 |
| 内存增长 | 循环前后多轮 GC + Dispatcher 排空，取进程数据 | 托管 <= 16 MiB，Private Bytes <= 64 MiB |
| 原生资源 | 比较进程句柄与 USER/GDI 对象 | 增长分别 <= 64 / 16 / 32 |
| 热路径分配 | 25 万次快照、10 万次缓存帧、10 万次轮询 | 每组 <= 1 MiB，单组 <= 10 秒 |
| 显示格式化 | 2 万次时间与状态文案 | <= 16 MiB，<= 10 秒 |

内存为暖机后增长预算，不是整个程序的内存上限。耗时预算故意留足 CI 机器差异，主要防止严重退化；报告保留实测值用于比较，不是精确微基准或帧率承诺。热缓存检查不代替原有后台首次解码测试。

截图检查关闭动画以保持基线稳定；生命周期循环则启用动画，覆盖头像计时器和模式过渡的释放。截图门禁还检查纯背景不能被接受，避免空白画面也“通过”。

## 失败定位与重放

- `quality-report.json`：版本、运行环境、通过状态、已完成阶段数据与异常。
- `sequence-failure.json`：随机种子、失败步、最近 32 条操作。
- `lifecycle-report.json`：窗口循环次数、实际时长、内存/句柄采样及预算。
- 六张 `*-light.png` / `*-dark.png`：本次实际渲染图，失败时也保留已生成的图。

按失败种子完整重放，例如：

```powershell
dotnet run --project tests/PetTests.csproj --configuration Release --no-build -- . artifacts/quality-replay --quality 0 250000
```

种子重放依赖相同测试代码与 .NET 主版本。固定序列让 CI 可复现，但不覆盖所有可能顺序；新增故障应同时补充简短的命名回归用例。

## 截图基线管理

`tests/baselines/focus` 的 6 张 PNG 记录于 v2.4.0 生产代码重构之前。这些是测试窗口渲染图，不是修改过的角色素材。对比排除系统标题栏；每个 8x8 块计算 RGB 差异，允许字体抗锯齿的小幅变化。另按 32x32 区域检查局部差异，防止缺失的控件被大片空白区域稀释。

只有有意改变界面且人工确认时才显式重录：

```powershell
dotnet run --project tests/PetTests.csproj --configuration Release --no-build -- . artifacts/baseline-review --record-focus-baselines
```

逐张检查后把基线更新与对应 UI 修改放在同一个提交里。不要在 CI 中执行重录，也不要为了让失败变绿直接替换基线。尺寸变化直接失败；屏幕 DPI、可点击范围和倒计时布局仍由原有原生 UI 用例补充。

## CI 与发布

Build / Release 在原有功能和原生 UI 检查之后执行质量模式，最长允许 10 分钟。`quality-diagnostics` artifact 在失败时也上传已有报告和测试渲染图，保留 7 天。

任一门禁失败，后续应用包上传和 GitHub Release 步骤不会执行。诊断 artifact 与公开便携 ZIP 分离，不把测试截图发到 Release。推送前的本地通过不代表云端已通过；最终以对应提交的 Actions 结果为准。

当前不覆盖断电、数小时挂起、所有显卡驱动、多显示器热插拔及真实用户输入的所有时序。不要把短循环结果解读为“没有任何内存泄漏”。
