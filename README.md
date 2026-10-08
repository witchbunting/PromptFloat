# PromptFloat

**把常用提示词放在手边。** PromptFloat 是一款 Windows 悬浮提示词助手：从分层菜单中选一个模板，就能填入当前光标所在的位置。选中文字后，还可以把它保存为模板，或通过 OpenAI 兼容 API 获得三个润色版本。

A portable Windows prompt-template assistant with a floating menu, a local library, and optional three-variant text polishing.

[下载最新版本](https://github.com/witchbunting/PromptFloat/releases/latest) · [使用说明](使用说明.md) · [验证报告](验证报告.md) · [更新记录](CHANGELOG.md)

![首次使用教学](docs/images/tutorial.png)

## 下载与运行

1. 从 [Release v1.2.0](https://github.com/witchbunting/PromptFloat/releases/tag/v1.2.0) 下载 `PromptFloat-1.2.0-win-x64.zip`。
2. 解压到可写目录，运行 `PromptFloat.exe`。压缩包自带 .NET 运行环境，无需另行安装 .NET。
3. 首次使用时，跟着四步教学了解基本操作，并选择想保留的初始模板分类。已有初始化数据的用户升级后，可以直接继续使用。

当前版本以 Windows x64 便携 ZIP 发布，没有独立安装器。模板和其他数据保存在 `%LOCALAPPDATA%\PromptFloat`，移动或替换程序时，已有模板会保留下来。升级时先退出旧版，再运行新版，避免两个版本同时响应操作。Release 还提供 SHA-256 校验文件，方便核对下载的文件。

## 日常怎么用

### 从菜单中插入提示词

先点击目标输入框，再点击常驻的“插入”气泡或悬浮球。把鼠标移到分类上，就能展开模板列表；单击对应的模板备注即可填入。

没有选中文字时，模板会插入光标处；有选中文字时，会替换这部分文字。填入后，由你确认并发送消息。

### 把选中文字保存为模板

选中文字后，悬浮球旁会出现“插入”“润色”和“保存提示词”三个操作，按需要选择即可。如果软件没能读到选中文字，可以先手动复制，再通过剪贴板入口新建模板。

### 获取三个润色版本

打开 **设置 → 智能体**，填写 API、模型和密钥，设置三个润色风格，然后点击“保存并立即生效”。

选中文字并点击“润色”后，就能查看三个版本。拖动悬浮球时，润色气泡会一起移动。原文和选区仍然有效时，可以用喜欢的版本替换原文；如果原文或选区已经变化，结果会改为供你复制。

### 整理模板库和调整设置

右键点击悬浮球，可以打开模板管理、进入设置或退出软件。

模板可以添加备注和标签、加入收藏，也支持变量模板。分类内按使用频次排序，常用模板更容易找到。批量操作方便集中整理，回收站用于找回删除的模板；JSON 导入导出和自动备份则便于保存、迁移模板库。

想再看一遍操作指引，可以打开 **设置 → 关于 → 使用教学**。回看教学时，已有模板库会保持原样。

### 默认快捷键

| 快捷键 | 操作 |
|---|---|
| `Ctrl+Alt+P` | 打开菜单 |
| `Ctrl+Alt+S` | 保存选中文字 |
| `Ctrl+Alt+H` | 显示或隐藏悬浮球 |

## 首次教学什么时候出现

教学会在新的使用环境中自动出现一次。关闭、跳过或意外中断后，后续启动也会直接进入日常使用；只有在教学中选择完成，才会导入勾选的初始分类。

这个记录保存在用户数据目录中，因此升级软件或换一个解压位置不会再次触发教学。清空数据，或通过 `--data-dir` 指定新的数据目录后，才会被视为新的使用环境。已有初始化数据的用户沿用原来的状态，包括使用旧版设置文件的用户。

悬浮球会先启动，教学窗口随后打开。教学窗口打开时，你也可以继续使用悬浮球；如果教学状态保存失败，软件会跳过教学，继续正常启动。

<details>
<summary>开发者说明：首次启动的判定与保存</summary>

`settings.json` 中的 `TutorialShown` 和 `Initialized` 都为 `false` 时，软件自动显示教学。显示前会以原子方式写入 `TutorialShown=true`，因此关闭、跳过或意外中断后都不会再次自动弹出。

完成或关闭教学时，会标记初始化；只有选择完成时，才导入勾选的分类。旧设置缺少新增字段时仍可兼容，`Initialized=true` 的用户不会自动看到首次教学。

</details>

## 数据保存与 API 调用

模板、使用统计和设置默认保存在本机。点击润色或测试连接时，软件才会调用你配置的 API；润色请求发送的内容是选中文字和智能体指令。

API 密钥通过 Windows 的 DPAPI 按当前用户加密，模板导出和备份中不包含密钥。换电脑使用时，需要重新填写密钥。

自动填入需要目标软件支持 Windows UI Automation，并具备相应权限，具体能否使用取决于输入控件。密码框、命令终端和设为仅复制模式的应用采用复制方式，不执行自动填入。可以在 [验证范围](验证报告.md) 中查看兼容性检查结果，在 [数据边界](SECURITY.md) 中了解数据处理说明。

## 构建与测试

如果想从源码构建，请先在 Windows 上安装 `global.json` 指定的 .NET 10 SDK，再运行：

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
# 在独立桌面会话进行 UI 与跨软件验证：
powershell -ExecutionPolicy Bypass -File build.ps1 -DesktopChecks
powershell -ExecutionPolicy Bypass -File build.ps1 -CompatibilityChecks
```

构建脚本按锁定的依赖版本还原项目，运行控制台核心测试，再发布自带运行环境的程序，生成 ZIP 和 SHA-256 校验文件。

GitHub Actions 会自动完成核心检查和 Windows 构建。图形界面与第三方软件的兼容性检查，需要在可交互的桌面机器上单独进行。API 测试使用模拟响应，不会调用真实付费接口。

## 项目结构与许可

- `PromptFloat.Core`：模板逻辑、SQLite 存储、导入导出、首次教学状态和润色协议。
- `PromptFloat`：WPF 界面，包括悬浮球、菜单、教学和管理窗口，以及输入桥接和选区监听。
- `PromptFloat.Tests`：数据功能和模拟 API 检查。
- `docs`：界面截图、使用与发布说明，以及已验证版本的公开验证记录。

代码采用 [MIT License](LICENSE)。第三方运行组件的许可见 [第三方声明](LICENSE-THIRD-PARTY.md) 与 `licenses`。参与开发前，可以阅读 [贡献说明](CONTRIBUTING.md)。
