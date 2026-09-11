# 皮肤 SDK

这个目录提供制作自定义桌面角色所需的最小模板、校验命令和预览流程。

## 目录结构

```text
my-character/
  pet.json
  spritesheet.png
```

使用 `pet.template.json` 开始制作。`spritesheet.png` 必须与 `pet.json` 的单元格、行列数完全匹配；一张普通立绘不能直接作为动画皮肤。

## 校验

在仓库根目录运行：

```powershell
pwsh -File .\scripts\validate-skin.ps1 .\my-character\pet.json
```

也可以直接校验单角色 ZIP：

```powershell
pwsh -File .\scripts\validate-skin.ps1 .\my-character.zip
```

校验器使用程序本身的皮肤解析逻辑，会检查 JSON、动作参数、PNG 尺寸、文件路径安全性和大小限制。

## 预览

1. 运行程序并打开“设置 → 皮肤”。
2. 导入 `pet.json` 或单角色 ZIP。
3. 在预览区检查待机、挥手、跳跃和方向帧。
4. 校验通过后再保存到用户皮肤目录。

需要编辑动作时，打开皮肤编辑器调整帧行、起始列和每帧时长；编辑器保存前仍会执行同一套校验。

## AI 制作流程

把下面三个文件一起提供给 AI：

- `docs/PET_FORMAT.md`
- `docs/AI_SKIN_GUIDE.md`
- `skin-sdk/pet.template.json`

要求 AI 同时交付 `pet.json` 和 `spritesheet.png`，并明确说明单元格尺寸、行列数、动作行和每帧时长。生成后先运行校验器，再导入程序预览。
