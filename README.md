# 叶瞬光桌面宠物

一个不依赖 Codex 的 Windows WPF 桌面助手。程序内置动画精灵图，下载后可以离线运行，不需要 API key，也不会连接网络。

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

当前程序没有商业代码签名。Windows 可能显示“Windows 已保护你的电脑”或“未知发布者”。请先确认文件来自本仓库的 Release，并核对同一 Release 中的 SHA256 文件；只有在来源和哈希都正确时再决定是否运行。

## 操作

| 操作 | 效果 |
| --- | --- |
| 左键拖动 | 移动叶瞬光 |
| 右键 | 打开动作和设置菜单 |
| 托盘图标双击 | 显示或隐藏 |
| 托盘菜单“重置位置” | 将窗口找回主屏幕 |
| 托盘菜单“退出” | 完全退出程序 |

设置保存在 `%APPDATA%\YeShunguangPet\settings.json`。位置、大小、置顶、点击穿透和开机启动状态会在下次运行时恢复。

开启“点击穿透”后，角色窗口不会接收鼠标操作。此时请从 Windows 系统托盘菜单关闭“点击穿透”。

## 主要功能

- 透明无边框、可拖动、可缩放的桌面动画
- 鼠标靠近时看向鼠标方向
- 手动播放待机、打招呼、跳跃、工作、等待和检查等动作
- 系统托盘显示/隐藏与完整退出
- 可选总在最前、点击穿透和开机启动
- 单实例运行，重复启动会唤醒已有窗口
- 多显示器位置保护和 Per-Monitor V2 DPI 支持

## 常见问题

### 无法拖动

检查系统托盘菜单中的“点击穿透”是否开启。开启时必须先从托盘关闭它。

### 找不到角色

双击托盘图标，或在托盘菜单中选择“重置位置”。程序启动和拖动结束时也会自动避免窗口留在屏幕外。

### 开机启动后找不到程序

开机启动记录包含 EXE 的完整路径。移动程序前先关闭“开机启动”，移动完成并重新运行后再开启。

### 如何卸载

1. 在托盘菜单中关闭“开机启动”。
2. 从托盘菜单完全退出。
3. 删除解压后的程序目录。
4. 如需清除偏好设置，再删除 `%APPDATA%\YeShunguangPet`。

## 精灵图约定

当前版本使用固定的 8 列 x 11 行帧表，每格为 `192 x 208`：

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

成品位于 `artifacts`。推送与项目版本匹配的标签（例如 `v1.0.1`）后，GitHub Actions 也会自动构建并创建对应 Release。

发布脚本会先验证原始精灵图 SHA256。只要精灵图发生任何字节变化，构建就会停止。

## 反馈

发现问题时请创建 [Bug report](https://github.com/Resker666/YeShunguangPet/issues/new?template=bug_report.yml)，并附上 Windows 版本、显示缩放比例、是否使用多显示器和复现步骤。不要上传密码、密钥或其他私人信息。

## 许可与素材

本项目自行编写的源代码使用 [MIT License](LICENSE)。该许可不适用于 `Assets/spritesheet.png`、`Assets/YeShunguangPet.ico`、叶瞬光角色形象、名称或其他第三方素材；详细边界见 [ASSET_NOTICE.md](ASSET_NOTICE.md)。
