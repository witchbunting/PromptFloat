# 参与开发

在 Windows x64 安装 `global.json` 指定的 .NET 10 SDK。克隆仓库后运行：

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
```

核心检查由 `PromptFloat.Tests` 控制台程序执行，不使用 `dotnet test`。依赖使用提交的 NuGet 锁文件还原。

需要图形桌面会话时，可运行 `build.ps1 -DesktopChecks`；本机安装了目标应用且没有正在操作其他窗口时，再运行 `-CompatibilityChecks`。这些检查会创建隔离的合成输入和临时数据，使用真实鼠标、焦点与单次粘贴，请勿在测试同时操作其他窗口。API 检查使用模拟响应。

变更输入桥接时，必须保持目标与选区校验、失败复制入口、只发送一次粘贴和不自动发送消息。变更教学流程时，应覆盖首次、后续启动、关闭、状态写入失败和已有数据升级。修改数据结构需保持旧 JSON 及数据库兼容。

提交问题时附软件版本、Windows 版本、目标应用和复现步骤。提交 PR 时说明问题、变化与验证结果；不要提交个人模板、正文、密钥、构建缓存或运行日志。
