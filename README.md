# 叶瞬光桌面宠物

一个不依赖 Codex 的 Windows WPF 桌面助手。程序从本地皮肤包读取动画，随附叶瞬光，可以离线运行，不需要 API key。

<p align="center">
  <img src="docs/idle-preview.png" width="192" height="208" alt="叶瞬光待机动画预览">
</p>

这是非官方、非商业的同人项目，与米哈游或 HoYoverse 没有从属、赞助或授权关系。发布或再使用前请阅读 [素材说明](ASSET_NOTICE.md)。

## 下载与运行

前往 [Releases](https://github.com/Resker666/YeShunguangPet/releases/latest)，下载 Windows x64 便携版 ZIP，解压到一个固定目录后运行 `YeShunguangPet.exe`。

- 支持 Windows 10/11 x64。
- 不需要管理员权限。
- 不需要安装 .NET、Codex 或其他运行库。
- 建议先解压再运行，不要直接在压缩包预览窗口中启动。
- 从 v1.2.0 起，EXE 和旁边的 `Pets` 文件夹必须一起保留；升级时解压完整 ZIP，不能只替换 EXE。

当前程序没有商业代码签名。Windows 可能显示“Windows 已保护你的电脑”或“未知发布者”。请先确认文件来自本仓库的 Release，并核对同一 Release 中的 SHA256 文件；只有在来源和哈希都正确时再决定是否运行。

## 操作

| 操作 | 效果 |
| --- | --- |
| 左键拖动 | 移动叶瞬光 |
| 右键 | 打开动作和设置菜单 |
| `Ctrl + Alt + Y` | 召回主屏幕并关闭点击穿透 |
| 托盘图标双击 | 显示或隐藏 |
| 托盘菜单“设置” | 调整显示和行为 |
| 托盘菜单“退出” | 完全退出程序 |

设置保存在 `%APPDATA%\YeShunguangPet\settings.json`。位置、大小、窗口选项和行为选项会在下次运行时恢复。本地日志位于 `%APPDATA%\YeShunguangPet\logs`。

开启“点击穿透”后，角色窗口不会接收鼠标操作。此时请从 Windows 系统托盘菜单关闭“点击穿透”。

## 主要功能

- 透明无边框、可拖动、可缩放的桌面动画
- 鼠标靠近时看向鼠标方向
- 手动播放待机、打招呼、跳跃、工作、等待和检查等动作
- 系统托盘显示/隐藏与完整退出
- 可选总在最前、点击穿透和开机启动
- 可调随机待机动作和触发间隔
- 可选桌面横向走动、走动间隔和速度
- `Ctrl + Alt + Y` 全局召回快捷键
- 常规、行为和关于设置页面
- 皮肤选择、动作预览、导入与刷新，保存后即时切换
- 外部 `pet.json` 动作参数与 PNG 精灵图，无需重新编译即可增加角色
- 本地故障日志与日志目录入口
- 单实例运行，重复启动会唤醒已有窗口
- 多显示器位置保护和 Per-Monitor V2 DPI 支持

## 用 AI 制作自己的角色

完整教程：[用 AI 制作角色皮肤：步骤与可直接使用的提示词](docs/AI_SKIN_GUIDE.md)。

1. 准备角色的全身参考图、脸部细节和风格要求。
2. 将 [皮肤格式文档](docs/PET_FORMAT.md) 和 [默认 pet.json](Pets/YeShunguang/pet.json) 一起提供给 AI，使用教程中的提示词。
3. 先确认角色外观，再生成各动作、组装透明精灵图，并查看播放预览。
4. 获取同一文件夹中的 `pet.json` 与 `spritesheet.png`。若收到 ZIP，先解压。
5. 打开“设置 → 皮肤 → 导入皮肤”，选择新角色的 `pet.json`，预览后点击“保存”。

制作完整皮肤需要图片生成和文件处理能力。桌宠的导入功能负责读取制作完成的皮肤包，单张立绘不会自动变成动画。默认叶瞬光的 JSON 可作为技术模板，其他角色的外观应使用自己的参考图。

## 常见问题

### 无法拖动

检查系统托盘菜单中的“点击穿透”是否开启。开启时必须先从托盘关闭它。

### 找不到角色

按 `Ctrl + Alt + Y`，或在托盘菜单中选择“召回主屏幕”。召回同时会关闭点击穿透。程序启动和拖动结束时也会自动避免窗口留在屏幕外。

### 快捷键没有反应

打开“设置 → 常规”检查召回快捷键状态。若注册失败，通常表示 `Ctrl + Alt + Y` 已被其他程序占用；托盘菜单中的“召回主屏幕”仍然可用。

### 程序异常退出

在“设置 → 关于”中打开日志目录，将最新的 `app.log` 与复现步骤一起提交到 Issues。日志只保存在本机，不会自动上传；提交前请检查并移除不希望公开的本机路径。

### 开机启动后找不到程序

开机启动记录包含 EXE 的完整路径。移动程序前先关闭“开机启动”，移动完成并重新运行后再开启。

### 如何卸载

1. 在托盘菜单中关闭“开机启动”。
2. 从托盘菜单完全退出。
3. 删除解压后的程序目录。
4. 如需清除偏好设置，再删除 `%APPDATA%\YeShunguangPet`。

## 精灵图约定

皮肤结构和动作参数由 `pet.json` 定义。默认叶瞬光位于 `Pets/YeShunguang/`，沿用 8 列 x 11 行、每格 `192 x 208` 的原始精灵图：

| 行 | 状态 | 使用列 |
| --- | --- | --- |
| 0 | 待机 | 0-5 |
| 1 | 向右拖动 | 0-7 |
| 2 | 向左拖动 | 0-7 |
| 3 | 打招呼 | 0-3 |
| 4 | 跳一下 | 0-4 |
| 5 | 失败 | 0-7 |
| 6 | 等待确认 | 0-5 |
| 7 | 工作中 | 0-5 |
| 8 | 检查成果 | 0-5 |
| 9 | 看向方向 000 到 157.5 | 0-7 |
| 10 | 看向方向 180 到 337.5 | 0-7 |

其他皮肤可以采用不同的行列、单元格尺寸、帧数和播放时长。在“设置 → 皮肤 → 导入皮肤”中选择新皮肤的 `pet.json`，预览后保存即可切换。导入的文件保存在 `%APPDATA%\YeShunguangPet\Pets`，升级程序不会覆盖。

格式、最小示例和动作说明见 [皮肤包格式](docs/PET_FORMAT.md)。仅有单张普通 PNG 不足以导入，还需描述帧位置的清单。

## 开发与构建

安装 .NET 8 SDK 后，在仓库根目录执行：

```powershell
.\scripts\publish-self-contained.ps1
```

开发机没有 .NET SDK 时，可以使用无管理员权限的引导脚本：

```powershell
.\scripts\bootstrap-dotnet-and-publish.ps1
```

生成便携版 ZIP 和 SHA256 文件：

```powershell
.\scripts\package-release.ps1
```

成品位于 `artifacts`。推送与项目版本匹配的标签（例如 `v1.2.0`）后，GitHub Actions 会验证皮肤加载与导入，再自动构建并创建对应 Release。

本地运行皮肤测试：

```powershell
dotnet run --project tests/PetTests.csproj --configuration Release -- .
```

发布脚本会先验证原始精灵图 SHA256。只要精灵图发生任何字节变化，构建就会停止。

## 反馈

发现问题时请创建 [Bug report](https://github.com/Resker666/YeShunguangPet/issues/new?template=bug_report.yml)，并附上 Windows 版本、显示缩放比例、是否使用多显示器和复现步骤。不要上传密码、密钥或其他私人信息。

## 许可与素材

本项目自行编写的源代码使用 [MIT License](LICENSE)。该许可不适用于 `Pets/YeShunguang/spritesheet.png`、`Assets/YeShunguangPet.ico`、叶瞬光角色形象、名称或其他第三方素材；详细边界见 [ASSET_NOTICE.md](ASSET_NOTICE.md)。
