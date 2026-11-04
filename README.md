# AI-PET

一个能自定义、能连接 AI 工具的 Windows 桌面宠物。

![自定义橘猫桌宠在 Codex 测试会话中抬头、动爪，头顶气泡由“任务开始”变为“任务结束”](docs/media/ai-pet-codex-demo.gif)

- **自定义**：使用预设动作，导入宠物主题，或通过创作空间和自己的 Agent 用照片、Prompt 制作动作。
- 打开 **自定义宠物和动作 → 视频提取**，选择视频、起始时间、时长及要使用的动作。可以在起始画面框选宠物，纯色背景也可以点选背景颜色并调整容差。选 **交给我的 Agent 抠像** 会复制一份包含视频位置和选段的说明，交给自己的 Agent 处理。结果可在同一页对照三种背景预览，选择“使用这个动作”后加入当前宠物。
- **连接 AI 工具**：统一五种状态——开始、进行中、结束、暂停、需要回答或确认；每种状态可绑定不同动作。
- **简单操作**：宠物和托盘使用相同右键菜单；自定义、工具连接与设置集中在一个管理窗口。
- **Windows 外观**：管理、偏好和预览窗口跟随系统浅色／深色；Windows 11 支持时使用 Mica 材质和圆角，关闭透明效果或使用高对比度时回退实色。
- **会话气泡**：宠物上方的漫画气泡显示当前 Codex 会话标题和状态。标题保持静止，运行状态轻微发光并显示动态省略号；等待、暂停、结束与待确认使用静态文字。跟随系统浅色／深色，高对比度或关闭系统动画时停用发光动画。连续工作事件不再重置动作，悬停不会打断任务；等待回答的动作会间隔重复，暂停与结束保留最后姿态。
- **Agent 使用**：[AI-PET Skill](skills/ai-pet/SKILL.md) 与 PowerShell 命令可用于安装、启动和配置，无需模拟点击托盘。

当前为开发预览版。内置“小麦”是源码绘制的插画宠物，不使用私人参考照片或外部模型。桌宠运行和预设动作不需要 Python、GPU 或云服务。

## 安装和启动

### 让 Agent 帮你安装

把下面整段复制到你在 Windows 电脑上使用的 Agent 聊天窗口里发送。

```text
请帮我在这台 Windows 电脑上安装并启动 AI-PET 桌宠。
从 https://github.com/DomTang12138/ai-pet/releases 选择最新发布的 Windows x64 版本（包括预发布版），下载该版本的 Windows 安装包 ZIP。
解压后，按照包内 skills/ai-pet/SKILL.md 的说明运行 install.ps1 -Start。如果已经安装了这个版本，直接启动即可。
请确认桌宠进程正在运行，再告诉我安装的版本，以及怎么从系统托盘打开桌宠菜单。
```

### 手动安装或从源码运行

Windows 10/11，需 .NET Framework 4.8 或更新的桌面组件；安装器会先检查，缺少时不修改现有作品。

界面保留现有 C# / WPF 运行环境，直接调用 Windows DWM 系统材质，无需额外安装 WinUI 或 Windows App SDK。系统材质依赖 Windows 11 22621 或更新版本；旧系统仍可使用统一的控件样式。系统设置变化会更新已打开的窗口，管理窗口也可通过 `AIPetDesktop.exe --settings` 打开（已有实例请从托盘进入）。

下载 Release ZIP，解压后让 Agent 执行 `install.ps1`，或直接运行 `AIPetDesktop.exe`。安装器默认保留已有用户作品与偏好，运行中的旧程序需要先从托盘退出。

从源码运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\ai-pet.ps1 -Action build
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\ai-pet.ps1 -Action start
```

公开构建生成在 `runtime/public-publish`，并在缺少网页示例图时生成插画示例。现有照片不会被覆盖。`runtime/build.ps1` 的默认路径保留历史本机参考图回归；公开源码使用 `-Public`。

## 连接自己的工具

```powershell
.\tools\ai-pet.ps1 -Action connect -Tool my-agent -Session task-42
.\tools\ai-pet.ps1 -Action event -Tool my-agent -Session task-42 -State started
.\tools\ai-pet.ps1 -Action event -Tool my-agent -Session task-42 -State running
.\tools\ai-pet.ps1 -Action bind -State finished -Slot click
.\tools\ai-pet.ps1 -Action status
.\tools\ai-pet.ps1 -Action disconnect
```

五个状态标识是 `started`、`running`、`finished`、`paused`、`needs-user`。动作从 `status` 返回的 `actions` 中选择。可用 `-Executable` 指定安装位置、`-DataDirectory` 指定隔离数据目录。

工具适配器须发送真实生命周期事件；Agent 也可报告自己的任务状态。通用协议不等于已适配所有 AI 工具。未提供的状态不会从沉默或普通日志中猜测，结束也不代表成功。见[状态协议](skills/ai-pet/references/tool-events.md)。

### Codex

在连接页勾选“自动跟随正在运行的 Codex 会话”，或让 Agent 执行 `codex-auto`。宠物会自动发现本机有明确信号的运行任务；新任务开始后跟随它，交错的工具活动不会反复抢占，需要你回答的会话优先，当前任务结束后会接续其他任务。连接页显示当前标题和其他活动会话，宠物与托盘的悬停提示也显示当前关联。

新安装且尚未连接其他工具时默认自动；升级保留原来的选择。需要固定某个聊天时，用“手动固定一个 Codex 会话…”或执行 `codex-connect`（默认读取真实 `CODEX_THREAD_ID`，也可通过 `-Session` 指定）。手动固定与断开都会停止自动切换；调整五项动作映射会保留所选模式。

选择列表和已连接提示优先显示聊天标题，附上项目与时间以区分重名会话。本机 `session_index.jsonl` 标题改名会自动刷新；缺少标题时显示“未命名会话”，不扫描聊天正文猜测名称。标题仅用于界面展示，内部仍按原会话标识准确绑定。

```powershell
.\tools\ai-pet.ps1 -Action codex-auto
.\tools\ai-pet.ps1 -Action status
```

桌面端使用本机会话读取，无需切换 CLI 或先配置 Hooks。这是实验性兼容方式，支持开始、已观察到的工具活动、结束、明确中断，以及桌面端明确显示成功的选择题式用户输入请求。后者已通过当前真实会话验证；权限提示和普通文字追问仍不能可靠自动识别。自动模式最多观察 32 个近期根会话，初次只采纳最近两分钟的明确信号，不把文件更新时间直接当作运行；五分钟没有新信号会标为待确认并暂时休息，长时间思考、远程／云端聊天及其他日志格式可能暂时无法确认。旧的结束记录不会在启动时重播。

Agent 可在已绑定的真实当前会话里执行 `-Action codex-event -State needs-user`，在普通文字提问前主动报告需要回答；收到回复继续任务时报告 `running`。这是 Agent 主动上报，不是后台自动监听，也不负责捕获或决定宿主权限审批。命令默认读取 `CODEX_THREAD_ID`，无需自己计算会话指纹。

Hooks 是可选增强：`codex-install` 在当前 Codex 用户目录注册状态观察 Hooks，并保留其他配置；按 [Codex 官方 Hooks 要求](https://learn.chatgpt.com/docs/hooks)，需本人审核并信任，官方明确记录的入口是 CLI 的 `/hooks`。本项目尚未核实桌面端审核入口，不将这一步作为桌面端连接的前提。`codex-status` 只核对登记，不能证明已信任或已收到真实事件。

Hooks 将提交映射为开始，工具执行映射为进行中，Stop 映射为本轮结束，Interrupt 映射为暂停，PermissionRequest 映射为需要确认。普通文字追问暂不自动识别；Stop 不代表更大目标完成或执行成功。只响应绑定会话，不保存 Prompt、命令正文或工具输出。用 `codex-remove` 移除本适配器 Hooks，用 `disconnect` 停止响应。

## 自定义照片和 Prompt

右键 → 自定义宠物和动作，选 1–4 张图片、写 Prompt，点击“复制给 Agent 的制作说明”。桌宠保存图片和描述，并复制一条制作说明；粘贴到自己的 Agent 会话，由它使用已有的图像或动画工具制作。说明包含准确的创作编号、请求位置和数据目录，无需自己找文件。此按钮不会自动发送消息或启动 Agent。

Agent 提交结果后，同一窗口会出现“预览生成的动作”。满意再选择使用，当前宠物直到确认使用才会替换。可以随时通过“再次复制制作说明”重新复制当前请求。

![动作预览窗口中的内置宠物“小麦”：以 190px 桌面尺寸在暖色、蓝色和深色背景上查看待机动作](docs/media/ai-pet-action-preview.png)

内置可选本地适配器用 Wan 2.2 生成动画、BiRefNet 制作透明背景；配置 Qwen Image Edit 后可联合处理 2–3 张参考图。工具路径与模型环境由 Agent 通过 `tools/configure_local_generator.ps1` 配置。也可接入自有命令行生成器，协议支持最多四张参考图，见 [生成器配置](skills/ai-pet/references/generation.md)。模型可能没有正确完成描述中的动作，需以实际预览为准。

已有命令行生成工具也可由 Agent 配置一次，随后主按钮显示“生成动作”，提供进度、取消和重试。页面会显示实际使用的工具、参考图数量限制和数据处理说明。本机模型是可选路线，不是默认安装要求。

Agent 用 `-Action requests` 查询本地请求，用 `-Action result -RequestId ... -Theme ...` 提交生成的主题；已配置生成器时可用 `generate` / `generation-status` / `cancel-generation` 管理原生生成。已有主题也可直接导入。

Agent 的生成工具若输出整张动作图集，可使用 `tools/import_sprite_atlas.py` 拆帧打包，不需要自行编写裁图脚本。它保留透明度和原图像素，检查出画、空白和重复静帧；实际形象、动作与边缘质量仍需预览，见[图集制作说明](skills/ai-pet/references/generation.md#sprite-atlas-output)。

折叠的“使用本机眨眼、歪头模板”提供可选创作空间。当前本机生成模板主要支持眨眼和歪头；任意 Prompt 的新动作需要支持该动作的生成后端或用户 Agent 的图像/动画能力。多张照片可保存为参考，现有本机模板读取主照片。填写文字或保存角色卡本身不代表已生成动画。

可选 Creator 需要独立安装 `tools/creator-requirements.txt` 中的 Python 依赖。配置本地模型后才启用相应生成路线，公开包不含权重或私有环境。来自用户图片的主题在使用前仍需预览确认。

## 当前限制与改进方向

单个视频最大 1 GiB，支持 MP4、MOV、M4V、WebM、MKV 和 AVI。每段最多 12 秒、120 帧。桌面端在这个上限内按实际时间采样。视频在本机保存，输出由原片的画面和透明度组成。本机处理需要 FFmpeg、ffprobe、Python 和 Pillow，框选模式还需要 OpenCV 与 NumPy。快速移动、背景近似毛色或遮挡可能导致轮廓缺失，可缩短选段、重新框选，或让 Agent 用分割遮罩处理。[视频动作说明](skills/ai-pet/references/video-actions.md)

- Codex 桌面会话读取是实验性适配，依赖上游记录格式。明确选择题已验证；普通文字追问和部分宿主权限卡不保证可观察，安装 Hooks 也不能承诺覆盖所有权限路径。结束只代表本轮结束。
- Windows 已启用 Per-Monitor V2 和系统变化后的窗口位置恢复，本机 150% 缩放、真实离屏恢复与系统消息回调检查通过；混合 DPI、真实热插拔、睡眠恢复及其他设备仍须实测。当前包未数字签名，ARM64 尚未验证。播放不需要 GPU，本机生成依赖后端自己的硬件条件。
- 自定义接口当前接收 1–4 张图片，不等于所有后端都联合处理四图；后端能力单独声明。单图与双参考图样例通过不等于已知整体生成成功率。
- 任意照片/Prompt 的身份、动作、透明边缘和循环质量仍可能失败。已有 Wan/BiRefNet 不等于这些问题已解决；VACE 局部控制与时序分割是待验证方向。部分研究依赖有额外许可限制，MIT 主程序不代其授权。

可复用开源项目、技术原理、具体限制和分阶段验收见[实施计划中的开源调研](IMPLEMENTATION-PLAN.md#2026-10-16-开源方案调研与限制补充)。该节区分上游能力与 AI-PET 已实测结果。

## 数据和开源

连接、偏好和作品默认保存在 `%LOCALAPPDATA%/AI Pet Studio`。数据目录与程序安装目录分开；五状态接口不传任务正文。源码导出使用显式文件清单，排除私人照片、作品、日志、模型和虚拟环境。

原始项目代码使用 [MIT](LICENSE)；外部模型和依赖保留各自许可证，见 [第三方说明](THIRD-PARTY-NOTICES.md)。[实施计划](IMPLEMENTATION-PLAN.md)列出当前完成项和后续工作。
