# Store 商品信息 — Hotline AI (简体中文)

Model-drafted translation of `store/listing.md`; have a native speaker review it before publishing. Paste into Partner
Center › the submission › Store listings › 中文(简体). Product name stays "Hotline AI".

## 简短说明

既然摆脱不掉 Copilot 键，不如物尽其用：为你已有的 AI（Claude、Codex、Gemini）、自己的 API 密钥或本地模型打造的快速原生 AI 聊天面板。无需在我们这里注册账号。自由排列工具栏，还能将聊天转移至其他应用。

## 说明

既然摆脱不掉 Copilot 键，那不如物尽其用。Hotline AI 能将它变成一个快速、原生的 AI 聊天面板，随时与你选择的 AI 对话。

按下按键，面板就会在你当前操作的显示器上打开，随时可以输入。提出问题，接收流式回答，再按一下按键它就消失了。不需要浏览器标签页，也无需在我们这里注册账号。

使用你已有的 AI
• Claude Code：使用已安装的 Claude Code 及其登录信息，无需 API 密钥即可直接使用你的 Claude 订阅。
• OpenAI Codex CLI：使用已安装的 Codex 及其登录信息，无需 API 密钥即可直接使用你的 ChatGPT 订阅。
• Antigravity CLI (agy)：使用你的 Google 登录来使用 Gemini。
• 你自己的 API 密钥：Anthropic、Google Gemini、OpenAI 以及 OpenRouter 等其他兼容 OpenAI 的服务。
• 本地模型：电脑上的任何 OpenAI 风格服务端（例如 llama.cpp 或 LM Studio），任何数据都不会离开你的电脑。
直接在面板的工具栏中选择模型、提供商以及它的思考深度。

随身携带上下文
• 按下按键时，在其他应用中选中的文本会自动附带，你也可以在发送前将其移除。
• 截取你所在的窗口、整个屏幕或拖选出的区域；粘贴或拖放图像与文件。
• 无法识别图像的模型会转而接收截图中的文本，由电脑上 Windows 自带的 OCR 本地读取。

专为快速任务打造
• 快速操作：输入 / 即可翻译、总结、修改或解释，也可以用简单的纯文本文件编写你自己的操作。
• 记忆能力：“/remember 我更喜欢公制单位”，无论使用哪个 AI，每次聊天都会记得。
• 语音输入：按住按键说话；松开即可停止。
• 最近聊天只需一键即达，可在可搜索的列表中查看（Ctrl+H）。
• Click to Do 和 Windows 操作：从其他应用向 Hotline AI 发送文本或图像。
• 继续在...中打开（Continue in）：需要更多空间时，将聊天转移至终端、Open WebUI、llama.cpp 网页界面、LM Studio 或你自定义的目标。
• 面板处于焦点时工具栏按钮均可使用快捷键：新建聊天、最近聊天、设置、固定、截图和各项选择器。你自定义的按钮也可以拥有快捷键。它们需要配合 Ctrl、Alt 或 Win，绝不会干扰正常输入。
• 当你在其他地方忙碌时，Windows 通知会在回答生成完毕后提醒你。

原生、轻巧且属于你
• 基于 WinUI 3 构建：启动迅速，跟随你的浅色或深色主题，无内置浏览器。原生支持 x64 和 ARM64。
• 丰富的回答样式：标题、列表、表格、带复制按钮的代码块、数学公式和简易绘图。
• 按住 Alt 即可移动面板或调整大小；支持固定面板，还可选择聊天内容向上或向下展开。
• 一切皆为可编辑的纯文本文件：设置、连接、提示词，以及工具栏中的按钮。
• 按你的方式排列工具栏：在设置中跨行拖拽按钮、添加带有自定义图标的按钮，并将较少使用的按钮收纳进“更多”菜单。
• “设置 > 备份”（Settings > Backup）可将你的配置迁移至另一台电脑。API 密钥绝不会被导出，且被 Hotline 替换的文件都会事先备份。
• 谨慎使用工具：API 连接可以使用来自 MCP 服务器的工具；任何会做出改动的操作都会先征询你的意见。
• 如果你更想要右侧 Ctrl 键，Copilot 键也可以当作 Right Ctrl 键使用。

从设计上注重隐私
Hotline AI 没有服务器，也不收集任何内容：无账户、无分析统计、无广告。你的消息仅会发送到你选择的 AI 服务，并遵循该服务的条款。API 密钥安全保存在 Windows Credential Locker 中；历史记录、设置和记忆仅留在你的电脑上。免费且开源。

没有 Copilot 键？设置任何你喜欢的快捷键即可，例如 Ctrl+Alt+H。

Hotline AI 是一款独立应用，与 Microsoft、Anthropic、Google 或 OpenAI 无任何关联，亦未获其认可。使用 Claude Code、Codex、Antigravity CLI 或 AI API 需要拥有相应服务的账户。

## 此版本的新增功能

在设置中排列按钮栏、添加带自定义图标的按钮，并使用“更多”菜单。按单元格设置按钮宽度，单独调整每个选择器的大小。面板处于焦点时，工具栏按钮（包括自定义按钮）均可使用键盘快捷键。“设置 > 备份”（Settings > Backup）可迁移配置且不包含 API 密钥；设置错误值会弹出警告，手动编辑的内容在被替换前会自动备份。“继续在...中打开”（Continue in）可在 Open WebUI、llama.cpp、LM Studio 或自定义目标中打开聊天（部分目标需手动粘贴）。窗口宽度使用真实像素，且聊天可自动变宽。我还修复了 Windows 11 26H2 上的 Copilot 键焦点处理、MCP 工具检查、API 响应限制以及备份失败的问题。

## 产品功能

1. 按 Copilot 键（或任意快捷键）在你正在使用的显示器上打开，随即可输入
2. 使用你的 Claude Code、ChatGPT（Codex CLI）或 Gemini（Antigravity CLI）登录：无需 API 密钥
3. 使用自己的密钥：Anthropic、Google Gemini、OpenAI 及 OpenAI 兼容 API
4. 本地模型：你电脑上任何 OpenAI 风格的服务器，聊天数据不离开电脑
5. 在面板中选择模型、提供方和思考强度
6. 自动附加你在之前应用中选中的文本
7. 截取窗口、屏幕或区域；粘贴或拖入图片和文件
8. 为无法查看图片的模型在你的电脑上识别截图文字（OCR）
9. 快速操作：/translate、/summarize、/fix、/explain 等，或使用自定义文本文件
10. 无论使用哪个 AI，每个聊天都能看到的记忆
11. 按住按键即可语音输入，使用 Windows 联机语音识别
12. 从 Click to Do 和其他应用发送文本或图片；在终端、Open WebUI、llama.cpp 或 LM Studio 中继续聊天
13. 丰富的回答：表格、带复制按钮的代码、公式和图形
14. 原生 WinUI 3（支持 x64 和 ARM64）；浅色、深色与高对比度主题，自定义主题色，不内嵌浏览器
15. 在设置中拖放排列按钮栏；自定义带图标的按钮、“更多”菜单以及独立的选择器宽度
16. API 连接可选使用 MCP 工具；除事先允许外，任何更改操作都会先征得同意
17. 无 Hotline 账户、无分析、无广告；API 密钥保存在 Windows 凭据管理器中；免费且开源
18. 可编辑 settings、connections、prompts 和 toolbar.json；错误值会警告并回退，备份保护手动编辑
19. “设置 > 备份”（Settings > Backup）导出和导入设置、工具栏、MCP、音效、连接、快速操作与提示词；绝不导出密钥
20. 面板内快捷键支持工具栏按钮（包括自定义按钮）；移动、固定和调整面板大小，支持像素级宽度与自动加宽

## 屏幕截图说明

1. 按下 Copilot 键：随便问，回答即刻呈现。
2. 选中的文本会随问题一起发送。
3. 用 / 快速操作：翻译、总结、修改、解释。
4. 使用你已有的 AI，一切按你的方式设置。

## 搜索词

AI 聊天助手, Copilot 键快捷键, Claude Code 集成, Codex Gemini CLI, Anthropic OpenAI API, 兼容 OpenRouter, 本地 LLM llama.cpp
