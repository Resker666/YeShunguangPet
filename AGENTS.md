# YeShunguangPet 协作规则

## 用户确认优先

- 修改完成后先提供本地可体验版本和验证结果。
- 未得到用户明确的“推送”“发布”或同等确认，不执行 `git push`、打标签或创建 Release。
- 用户确认前，不把未体验的版本当作可发布版本。

## 遇到阻塞时

- 需要网络、额外目录、GitHub、API Key 或系统权限时，先说明原因、范围和风险，再请求授权。
- 不要求用户把 API Key 粘贴到聊天中；API Key 只能通过本地安全存储或用户自己的终端设置。
- 编译、测试、CI 或环境失败时，报告真实错误和下一步，不用猜测结果替代验证。

## 需求沟通

- 需求存在会影响实现的歧义时，主动提问确认。
- 方案选择前说明收益、成本、风险和推荐项。
- 每次改动保持范围明确，先复用现有架构和测试。

## 本地验证

- 当前项目路径：`D:\develop\github\YeShunguangPet.Wpf`
- 当前 SDK 路径：`D:\develop\github\CodexDotnetSdk\8.0\dotnet.exe`
- 编译前设置：

```powershell
$env:DOTNET_CLI_HOME = "D:\develop\github\CodexDotnetSdk\cli-home"
$env:NUGET_PACKAGES = "D:\develop\github\CodexDotnetSdk\nuget-packages"
```

- 本地验证优先使用上述 SDK。若 NuGet 网络不可用，可使用框架依赖、非单文件参数验证代码和测试；必须明确这不等于最终便携包验证。
- 发布前仍需等待用户体验确认，再执行完整推送流程。
