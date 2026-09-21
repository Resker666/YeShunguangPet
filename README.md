# 叶瞬光桌面宠物

可换角色的离线桌面陪伴，带迷你番茄钟和专注记录。无需登录，不需要安装额外运行库。

**[下载 Windows 便携版](https://github.com/Resker666/YeShunguangPet/releases/latest)** · [社区皮肤库](https://github.com/Resker666/YeShunguangPet-Skins) · [三步启动](#三步启动) · [完整使用说明](docs/USER_GUIDE.md) · [反馈体验](https://github.com/Resker666/YeShunguangPet/issues/new?template=trial_feedback.yml)

![角色动作、迷你计时器和皮肤预览演示](docs/media/demo.gif)

演示基于 v2.4.0 的原生界面与原始动作帧，使用独立演示数据。[查看高清演示](docs/media/demo-30s.mp4) · [素材说明](docs/promotion/MEDIA_NOTES.md)

这是非官方、非商业的同人项目，与米哈游或 HoYoverse 没有从属、赞助或授权关系。角色图像不属于程序代码的 MIT 授权范围，请阅读[素材与权利说明](ASSET_NOTICE.md)。

## 可以做什么

| 桌面陪伴 | 迷你番茄钟 | 换成喜欢的角色 |
| --- | --- | --- |
| 拖动、点击互动、贴边收纳；最多同时保留 3 个角色 | 调整专注与休息时间，收起成可置顶的小窗，保存完成记录 | 预览并导入皮肤 ZIP，用动作编辑器微调现有皮肤 |

核心功能离线运行。日常角色台词使用可编辑的本地短句；可选接入模型服务，支持角色人设、连续聊天、台词生成和专注总结。只有主动点击发送或生成时才请求服务。

## 三步启动

1. 打开[下载页面](https://github.com/Resker666/YeShunguangPet/releases/latest)，在 **Assets** 中选择名称以 `win-x64-portable.zip` 结尾的文件，不要选 `Source code`。
2. **完整解压 ZIP** 到一个固定文件夹，不要直接在压缩包里运行。
3. 双击 `YeShunguangPet.exe`。旁边的 **`Pets` 文件夹要一起保留**。

支持 **Windows 10 2004 及以上版本 / Windows 11 x64**，无需管理员权限。当前不提供 macOS、Linux 或原生 ARM64 版。

> 当前版本尚无商业代码签名，Windows 可能显示“未知发布者”等提示。请先确认来自本仓库的 Release，并核对同一页面的 SHA256 文件。不要为了试用关闭系统安全防护；无法确认来源时先停止运行并反馈。

升级前先从系统托盘退出旧版，再解压运行完整的新包；旧版仍运行时，再次打开 EXE 只会唤醒旧进程。程序默认不联网，可在“设置 → 应用 → 常规”手动检查 GitHub Release，或自行开启启动检查；检查只提醒，不会自动下载或安装。

## 第一次可以这样试

- **移动和互动**：拖动角色；单击打招呼，双击跳跃。
- **开始专注**：右键角色 → 专注计时。右上角可切换迷你模式，图钉控制迷你钟置顶。
- **自定义快捷键**：设置 → 应用 → 快捷键。可配置召回、打开计时、开始/暂停和迷你模式；默认仅开启 `Ctrl + Alt + Y` 召回。
- **截图与贴图**：右键角色 → 截图会直接进入区域截图；控制中心或托盘可选择区域、当前屏幕或全部屏幕。在原处调整选区和标注，完成后复制、保存或贴到桌面；“文字助手”可在本机 OCR 后复制文字，或由用户确认后交给 AI 总结、翻译、改写。
- **完成提醒**：计时完成后保留任务栏/托盘待查看标记，查看计时面板后清除，不自动开始下一段。
- **查看介绍**：系统托盘 → 设置 → 桌面角色 → 角色设置 → 关于，只读，不会关闭角色。
- **添加或切换角色**：系统托盘 → 设置；皮肤导入入口在“设置 → 当前角色”。
- **找不到角色**：按 `Ctrl + Alt + Y` 召回，或从系统托盘选择“召回全部”。
- **完全退出**：在系统托盘菜单中选择“退出程序”。关闭计时面板不会停止计时。

## 内置角色与社区皮肤

从 v2.18.1 起，便携包内置叶瞬光、知更鸟和胡桃。打开控制中心 →「桌面角色」→「添加角色」即可选择，无需额外下载。胡桃采用社区皮肤 v1.4.0，皮肤 ID 为 `hutao`，显示名称沿用皮肤包中的 `hutao`。

[YeShunguangPet-Skins 社区皮肤库](https://github.com/Resker666/YeShunguangPet-Skins) 独立维护角色作品、作者说明和下载包：

- **下载更多角色**：前往 [皮肤下载页](https://github.com/Resker666/YeShunguangPet-Skins/releases)，下载单角色 ZIP，再在皮肤管理中导入。不要选择 GitHub 的 Source code。
- **分享自己的作品**：在程序中导出皮肤 ZIP，通过 [皮肤投稿表单](https://github.com/Resker666/YeShunguangPet-Skins/issues/new?template=submit-skin.yml) 提交预览、作者、素材来源和使用说明。
- **主程序与皮肤分别更新**：社区皮肤按需下载；只有被选为内置角色的作品才随程序提供。目前没有应用内在线皮肤商店。

已导入过同一份胡桃时，相同 ID 且文件字节相同的皮肤会合并显示。相同 ID 但内容不同则优先使用内置版本并提示冲突；自定义改版应另存为独立 ID，避免覆盖。

## 想制作自己的皮肤

已经有皮肤包时，可导入单个角色 ZIP 或 `pet.json`。只有一张立绘还不够，动画需要精灵图与动作参数。

[用 AI 制作皮肤的步骤与提示词](docs/AI_SKIN_GUIDE.md) · [皮肤包格式](docs/PET_FORMAT.md) · [皮肤 SDK](skin-sdk/README.md) · [动作编辑器](docs/USER_GUIDE.md#动作配置编辑器)

AI 配置、角色人设与聊天使用说明见 [AI_INTEGRATION.md](docs/AI_INTEGRATION.md)。在控制中心左侧「AI 设置」中可保存多套直连、中转或本地模型配置；角色聊天支持流式回复。当前默认完全离线，截图 AI 权限也默认关闭。

## 反馈与试用

独立开发，欢迎告诉我哪里好用、哪里让你不想继续用。

- [反馈一次使用体验](https://github.com/Resker666/YeShunguangPet/issues/new?template=trial_feedback.yml)：安装是否顺利、用过什么、希望改哪一点。
- [报告 Bug](https://github.com/Resker666/YeShunguangPet/issues/new?template=bug_report.yml)：附版本、复现步骤和显示环境。
- 出错时可在“设置 → 关于 → 诊断与日志”导出脱敏诊断包，**检查内容后再决定是否附上**。不必为了反馈先删除配置。

GitHub Issue 默认公开，不要上传个人桌面、密码、密钥、工作文件或未经检查的日志。没有 GitHub 账号时，也可直接回复你看到本项目的演示帖。

## 更多说明

| 文档 | 内容 |
| --- | --- |
| [完整使用说明](docs/USER_GUIDE.md) | 多角色、菜单、贴边收纳、计时、台词、专注记录、升级和卸载 |
| [常见问题](docs/USER_GUIDE.md#常见问题) | 无法拖动、找不到角色、快捷键与异常处理 |
| [备份与恢复](docs/USER_GUIDE.md#备份与恢复) | 皮肤历史版本与恢复 |
| [开发与构建](docs/USER_GUIDE.md#开发与构建) | .NET SDK、本地测试、便携包及自动发布 |
| [运行架构](docs/ARCHITECTURE.md) | 计时、角色行为、界面与生命周期边界 |
| [角色性能测量](docs/ACTIVITY_PERFORMANCE.md) | 回调、帧间隔、CPU 与测量限制 |
| [角色行为规则](docs/BEHAVIOR_RULES.md) | 自动动作条件、权重、冷却及安全边界 |
| [质量门禁](docs/QUALITY_GATES.md) | 随机操作重放、截图基线、资源及性能预算 |
| [更新记录](CHANGELOG.md) | 各版本新增与修复 |

程序源代码使用 [MIT License](LICENSE)，角色素材与宣传演示中的第三方视觉内容不包含在该许可中，详见 [ASSET_NOTICE.md](ASSET_NOTICE.md)。
