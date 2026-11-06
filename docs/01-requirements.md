# PuraMarkdown 需求规格说明书（v0.4）

| 项目 | 内容 |
| --- | --- |
| 文档编号 | PMD-REQ-002 |
| 版本 | 0.4（6 项产品决策已确认；IR/虚拟树/适配层列为暂缓并带触发条件） |
| 日期 | 2026-11-05 |
| 状态 | 已对齐（全部决策已确认，设计定稿） |
| 适用范围 | M0–M4（见 §9） |
| 关联文档 | [需求对齐与复审](00-需求对齐与设计复审.md)、[架构](02-architecture.md)、[语法规范](03-agent-syntax-spec.md)、[插件契约](04-plugin-and-rendering.md)、[IR/虚拟树/多平台讨论](07-IR-虚拟渲染树-多平台-讨论与决策.md) |

## 0. 阅读约定

- 优先级：`Must`（1.0 必须）/ `Should`（1.0 目标）/ `Could`（后续版本）。
- 编号稳定；新增需求用新号，废弃项保留编号并标注。
- 每条功能需求都有可检验的验收标准；性能数字为"目标值，待 M0 基线校准"。
- 术语以 §5 为准，全文一致。

## 1. 背景与问题陈述

### 1.1 产品定位

PuraMarkdown 是 **Avalonia 只读 Markdown 控件**：消费 Agent 对话中的 Markdown 文本，输出 Avalonia 原生视觉树；通过插件扩展语法识别、节点渲染、Agent 交互组件、图表与高亮。它是**视图层组件**，不是对话框架、不是编辑器。

### 1.2 现有方案缺口

| 现有做法 | 缺口 |
| --- | --- |
| Avalonia 官方 Markdown 控件（商业包） | 需 license、不可作为开源依赖；不开放自定义 Agent 组件 |
| 社区 Markdown 控件（Markdown.Avalonia 等） | 只渲染标准 Markdown；扩展需改内部；无 Agent 交互组件 |
| Markdig → HTML → WebView | 引入浏览器/JS/CSP；交互与流式增量不可控；安全问题 |
| 宿主自行用 Markdig 拼控件 | 每个宿主重复实现默认渲染、交互、流式、降级、安全策略 |

结论：Avalonia 生态缺少"**只读 + 可交互组件 + 流式 + 可插拔**"的 Agent Markdown 控件。

### 1.3 场景约束

- 只读为主：无编辑、无源码映射；
- 流式：逐 token 追加，必须块级增量，不整篇重解析；
- 交互：单选/多选/确认，结果上报宿主；
- 不可信输入：模型输出可能畸形/超长/恶意；
- 平台：1.0 只做 Avalonia 桌面（net10.0）。

## 2. 目标与非目标

### 2.1 目标

- **G-1**：标准 Markdown（CommonMark + GFM 常用子集）在 Avalonia 中正确渲染，零插件可用。
- **G-2**：`pura-agent` 围栏解析为交互组件，单选/多选可用、事件可上报。
- **G-3**：渲染器注册表允许覆盖任意节点渲染（含默认渲染器），插件不改核心。
- **G-4**：流式追加的解析量与文档长度解耦；UI 只嫁接变化块。
- **G-5**：解析/渲染失败可读降级，不崩溃、不白屏。
- **G-6**：默认安全：不渲染 HTML、URL 白名单、零网络、预算上限。

### 2.2 非目标

| 编号 | 非目标 | 说明 |
| --- | --- | --- |
| NG-1 | 不是编辑器 | 无光标编辑、无源码映射、无编辑会话 |
| NG-2 | 不是对话框架 | 不管消息历史、Agent 通信、会话状态 |
| NG-3 | 不是通用 Markdown 编辑器控件 | 不追求双向绑定、语法补全 |
| NG-4 | 不内置图表引擎 | 图表经 `IDiagramRenderer` 插件 |
| NG-5 | 不内置语法高亮引擎 | 高亮经 `ICodeHighlighter` 注入 |
| NG-6 | 不做多平台抽象 | 1.0 只 Avalonia；多平台后置（ADR-0016） |
| NG-7 | 不支持 HTML 渲染/子集 | 1.0 按转义文本展示（ADR-0009） |
| NG-8 | 不做插件沙箱/市场 | 显式注册、完全信任 |

## 3. 干系人与角色

| 角色 | 关注点 | 成功标准 |
| --- | --- | --- |
| 宿主开发者 | 集成成本、主题、流式、事件、安全默认值 | 半天接入流式+交互示例 |
| 插件作者 | 契约清晰、优先级可预测、调试方便 | 1 人天完成高亮/图表插件 |
| 终端用户 | 阅读体验、交互顺畅、深色主题、可复制、无障碍 | 长回答不卡、键盘可完成交互 |
| 安全审核方 | HTML/URL/DoS/交互伪造 | 默认配置 0 脚本、0 网络、无崩溃 |
| 维护者 | Markdig/Avalonia 版本升级成本、测试基线 | 升级只改适配点，回归可自动 |

## 4. 场景与用户故事

### 4.1 场景

S-1 流式回答；S-2 工具结果/代码块；S-3 单选/多选确认；S-4 图表围栏；S-5 超长消息；S-6 深浅色；S-7 复制/选择文本；S-8 畸形输入降级；S-9 插件扩展（高亮/图表/自定义组件）；S-10 键盘/读屏。

### 4.2 用户故事

| 编号 | 角色 | 故事 | 关联 |
| --- | --- | --- | --- |
| US-01 | 用户 | 回答生成中持续看到内容，不必等结束 | FR-501/502/503 |
| US-02 | 用户 | 工具结果与代码块可读、可复制、可高亮 | FR-203/204/205 |
| US-03 | 用户 | 对 Agent 选项单选/多选并触发上报 | FR-301–305 |
| US-04 | 用户 | 危险操作有确认卡片 | FR-306 |
| US-05 | 用户 | 图表能渲染，不可用时能看源码 | FR-402/403 |
| US-06 | 用户 | 数百轮对话滚动不卡 | FR-504、NFR-03 |
| US-07 | 用户 | 深色主题与系统联动 | FR-801 |
| US-08 | 用户 | 复制代码/整段文本 | FR-205/206 |
| US-09 | 用户 | 畸形输出显示可读降级与原因 | FR-601–604 |
| US-10 | 宿主开发者 | 几行代码接入控件 | FR-101/102、FR-901 |
| US-11 | 宿主开发者 | 用事件拿到选择结果 | FR-307/308 |
| US-12 | 插件作者 | 注册渲染器覆盖代码块高亮 | FR-401/405 |
| US-13 | 插件作者 | 注册图表渲染器 | FR-402/403 |
| US-14 | 插件作者 | 注册自定义 Agent 组件类型 | FR-404 |
| US-15 | 安全审核方 | 确认默认无脚本/无网络/有上限 | FR-701–705 |

## 5. 术语

| 术语 | 定义 |
| --- | --- |
| 控件（View） | `PuraMarkdownView`，Avalonia 只读 Markdown 控件 |
| 渲染器（Renderer） | 实现 `IPuraRenderer`，把 Markdig 节点渲染为 Avalonia `Control` |
| 渲染器注册表 | 按优先级查找渲染器的容器 |
| 基础渲染组 | 核心以 priority 0 注册的默认渲染器 |
| Agent 组件 | `pura-agent` 围栏 + JSON 描述的交互组件实例 |
| 组件解析器 | `IAgentComponentParser`，按 type 解析并校验 JSON |
| 交互事件 | `InteractionOccurred`，携带组件 id 与选中项 |
| 图表渲染器 | `IDiagramRenderer`，把围栏源码渲染为控件 |
| 高亮器 | `ICodeHighlighter`，把代码切分为 token span |
| 流式会话 | `PuraStreamingSession`，append-only 增量输入 |
| 稳定块 / 尾部窗口 | 已确认不再变化的块 / 每次重解析的未稳定尾部 |
| 降级卡 | 组件/图表/渲染失败时的可读替代 UI（含源码与原因） |

## 6. 功能需求

### 6.1 FR-1xx 解析与路由

| 编号 | 需求 | 优先级 | 验收标准 |
| --- | --- | --- | --- |
| FR-101 | 基于 Markdig 1.5.1 构建管道：CommonMark + GFM（表格/任务列表/自动链接/删除线）+ `UsePreciseSourceLocation` | Must | Given GFM 样例 ≥100 条，Then 节点类型/Span 与预期一致 |
| FR-102 | 固定 `DisableHtml()`；HTML 节点保留原文 | Must | Given `<script>`，Then AST 为字面文本节点，无 HTML 执行路径 |
| FR-103 | 自定义 `BlockParser` 识别 ` ```pura-agent ` 围栏，产出 `PuraAgentBlock`（RawJson/IsClosed/Span） | Must | Given 样例围栏，Then 节点类型正确、`IsClosed=true`；未闭合时 `IsClosed=false` |
| FR-104 | 非 `pura-agent` 围栏保持标准 `FencedCodeBlock`；图表语言不特殊解析 | Must | Given ```mermaid，Then 节点为 `FencedCodeBlock`、Info=mermaid |
| FR-105 | 输入规范化：去 BOM、统一 LF | Must | Given BOM 开头，Then 首个标题正常解析 |
| FR-106 | 解析预算：文档大小、围栏大小、嵌套深度、块数上限；超限降级 + warning | Must | Given 超限输入，Then 不 OOM、有 warning、消息仍可读 |
| FR-107 | 解析不抛异常：畸形/超长/深嵌套输入返回可用 AST 或降级文本 | Must | Given 模糊测试语料 1 万条，Then 0 未处理异常 |

### 6.2 FR-2xx 基础渲染

| 编号 | 需求 | 优先级 | 验收标准 |
| --- | --- | --- | --- |
| FR-201 | 块级：段落、标题、有序/无序/任务列表、引用、代码块、表格、分隔线、HTML 转义文本 | Must | Given 每个节点样例，Then 渲染出预期控件类型/层级（dump 断言） |
| FR-202 | 行内：强调、加粗、删除线、行内代码、链接、图片、换行、自动链接 | Must | Given 行内组合样例，Then `TextBlock.Inlines` 结构与样式正确 |
| FR-203 | 代码块：语言标签、复制按钮、等宽字体、横向滚动；高亮器缺失时纯文本 | Must | Given 无高亮器，Then 文本逐字符一致；有高亮器，Then token 着色 |
| FR-204 | 表格：表头、对齐、边框；超宽可滚动 | Must | Given 对齐样例，Then 列对齐正确 |
| FR-205 | 文本选择与复制：段落/代码块可选中；复制内容与原文一致 | Must | Given 代码块复制，Then 剪贴板内容等于源码 |
| FR-206 | 链接：点击经 `ILinkHandler`；URL 经 `IUrlPolicy`；被拒链接显示为文本 | Must | Given `javascript:`，Then 无导航、显示文本 |
| FR-207 | 图片：经 `IImageLoader` 异步加载；失败/禁用显示 alt + 占位 | Must | Given 默认 loader，Then 零网络、显示占位 |
| FR-208 | 主题 token 驱动所有默认样式（颜色/字体/间距/圆角）；无硬编码颜色 | Must | Given 切换主题，Then 所有块样式更新 |
| FR-209 | 渲染确定性：同输入 + 同主题 + 同插件集 ⇒ 控件树 dump 一致 | Must | Given 连续两次渲染，Then dump 逐字节一致 |

### 6.3 FR-3xx Agent 交互组件

| 编号 | 需求 | 优先级 | 验收标准 |
| --- | --- | --- | --- |
| FR-301 | `choice` 单选：RadioButton 语义，选中即上报 | Must | Given 单选卡片，Then 点击后收到 1 个事件、`SelectedIds` 正确 |
| FR-302 | `choice` 多选：CheckBox 列表 + 确认按钮；`min`/`max` 校验 | Must | Given max=2，Then 选第 3 项被阻止；提交事件最多 2 项 |
| FR-303 | `choice` 字段：question/description/options(id,label,description,disabled)/defaultSelected/submitLabel | Must | Given 完整样例，Then 渲染与默认值一致 |
| FR-304 | `choice` 禁用项：展示但不可选；组件级 `disabled` 全禁用 | Must | Given disabled option，Then 点击无效、选中态不变 |
| FR-305 | 状态：未选 / 选中 / 已提交 / 禁用 / 错误；提交后防重复 | Must | Given 已提交，Then 再次点击不产生事件 |
| FR-306 | `confirm`：question/description/confirmLabel/cancelLabel/danger；上报 `Confirmed` | Must | Given 危险确认，Then 确认/取消各产生 1 个事件 |
| FR-307 | 事件载荷：ComponentType/ComponentId/SelectedIds/Confirmed/Timestamp；只含模型内 option id | Must | Given 提交，Then 事件字段符合 schema；伪造 id 不可能出现 |
| FR-308 | 只读模式：`InteractionEnabled=false` 时控件禁用、不产生事件、保留展示 | Must | Given 只读，Then 无事件且选中态可见 |
| FR-309 | 组件降级：JSON 语法错误/schema 失败/未知 type → 降级卡（原因 + 源码） | Must | Given 非法 JSON，Then 显示首条错误与源码，其它块不受影响 |
| FR-310 | 流式未闭合：占位"组件加载中…"，完成后替换为正式组件 | Must | Given 逐字符追加，Then 无异常、无闪烁、完成后交互可用 |

### 6.4 FR-4xx 插件与扩展

| 编号 | 需求 | 优先级 | 验收标准 |
| --- | --- | --- | --- |
| FR-401 | 渲染器注册表：优先级解析、`CanRender` 无副作用、覆盖内置 | Must | Given 插件 priority 300，Then 覆盖 priority 0 的默认渲染器 |
| FR-402 | `IDiagramRenderer`：按 language/source 匹配；无插件回退代码块 | Must | Given mermaid + 插件，Then 图表控件；无插件，Then 代码块 |
| FR-403 | 图表插件失败 → 回退代码块 + warning，不影响其它块 | Must | Given 插件抛异常，Then 显示源码 + warning，消息其余部分正常 |
| FR-404 | `IAgentComponentParser` + `IAgentComponentRenderer` 支持自定义 type | Must | Given 注册 `rating`，Then `pura-agent` JSON type=rating 可渲染并上报 |
| FR-405 | `ICodeHighlighter`：语言匹配、token span；失败回退纯文本 | Must | Given 未知语言，Then 纯文本；已知语言，Then 着色 |
| FR-406 | `IPuraPlugin.Configure` 仅注册；`Build()` 校验重复/冲突并 fail fast | Must | Given 重复组件 type，Then 明确错误；空注册器，Then 配置异常 |
| FR-407 | 默认渲染器本身以注册表项存在，可被覆盖（不是硬编码分支） | Must | Given 覆盖段落渲染器，Then 所有段落使用新渲染器 |
| FR-408 | 插件显式注册，无程序集扫描/反射发现 | Must | Given 未注册的插件包，Then 不生效 |

### 6.5 FR-5xx 流式会话

| 编号 | 需求 | 优先级 | 验收标准 |
| --- | --- | --- | --- |
| FR-501 | `BeginStreaming()` 返回会话；`Append` 线程安全、单写入者 | Must | Given 并发 Append，Then 串行化或明确拒绝；无数据竞争 |
| FR-502 | 稳定块提交 + 尾部窗口重解析；解析量与文档长度解耦 | Must | Given 100KB 已渲染，Then 单次追加重解析字符 ≤ 16KB（窗口上限） |
| FR-503 | 增量嫁接：只增删/替换变化块，不重建整棵树 | Must | Given 追加 100 次，Then 已有块控件实例不变 |
| FR-504 | 滚动跟随：默认吸底；用户上滚后暂停；可配置 | Must | Given 上滚后追加，Then 视口不跳动 |
| FR-505 | 完成：`CompleteAsync` 后解析剩余内容、提交全部块、组件可交互 | Must | Given 流式结束，Then 无残留占位、最终控件树与全量渲染一致 |
| FR-506 | 中断：`AbortAsync` 保留已渲染内容并标记中断；不可续写 | Must | Given 中断后 Append，Then 抛明确异常；内容保留 |
| FR-507 | append-only：无重写/编辑/随机位置更新 API | Must | Given 公开 API，Then 不存在 Rewrite/Replace/Edit |
| FR-508 | 合帧：高频 append 合并为每帧最多一次解析/嫁接 | Must | Given 每 10ms 追加一次，Then 帧率 ≥ 55FPS（目标） |
| FR-509 | 未闭合语法容忍：代码块/围栏/强调/链接按半成品展示，完成时校正 | Must | Given 半截围栏，Then 不报错、显示部分内容 |

### 6.6 FR-6xx 错误与降级

| 编号 | 需求 | 优先级 | 验收标准 |
| --- | --- | --- | --- |
| FR-601 | 渲染器异常隔离：单块失败不影响其它块 | Must | Given 插件抛异常，Then 该块回退源码、其余正常 |
| FR-602 | 回退链：插件渲染器 → 默认渲染器 → 源码文本 | Must | Given 两层都失败，Then 显示源码纯文本 |
| FR-603 | `Warning` 事件：Code/NodeType/RendererName/Message；订阅者异常被吞掉 | Must | Given 未订阅，Then 静默；订阅者抛异常，Then UI 不崩 |
| FR-604 | 解析结果不可用时降级为纯文本，绝不白屏 | Must | Given 极端畸形输入，Then 至少显示文本 |

### 6.7 FR-7xx 安全

| 编号 | 需求 | 优先级 | 验收标准 |
| --- | --- | --- | --- |
| FR-701 | 不渲染 HTML、不提供 HTML 子集（ADR-0009） | Must | Given HTML 输入，Then 转义文本展示、无执行 |
| FR-702 | URL 策略：http/https/mailto + 锚点；`javascript:`/`vbscript:`/`file:`/`data:` 拒绝 | Must | Given 危险 URL，Then 文本降级、无导航 |
| FR-703 | 零网络默认：图片/资源经宿主注入；默认 loader 不联网 | Must | Given 默认配置，Then 网络请求 0 |
| FR-704 | 预算上限：文档/围栏/JSON/options/深度；1MB 恶意输入不卡死 | Must | Given 1MB 恶意输入，Then 无崩溃、UI 阻塞 ≤ 500ms |
| FR-705 | 交互安全：只回传模型内 option id；组件 id/type 来自解析结果 | Must | Given 伪造载荷，Then 不可能产生非法 id 事件 |
| FR-706 | 组件文案（question/label/description）按纯文本渲染，不解析 Markdown/HTML | Must | Given 文案含 `<b>`，Then 显示字面 `<b>` |

### 6.8 FR-8xx 主题 / 可访问性 / i18n

| 编号 | 需求 | 优先级 | 验收标准 |
| --- | --- | --- | --- |
| FR-801 | 主题 token：颜色/字体/间距/圆角/代码配色；内置 Fluent Light/Dark；跟随系统 | Must | Given 系统深色，Then 首屏深色；运行时切换生效 |
| FR-802 | 无障碍语义：标题 Level、列表、表格、代码块名称、单选/多选原生语义 | Must | Given 无障碍检查，Then 0 严重违规（Avalonia AutomationPeer 断言） |
| FR-803 | 键盘可达：Tab 进入组件、方向键移动、空格选择、Enter 提交、Esc 取消（confirm） | Must | Given 仅键盘，Then 完成一次 choice/confirm 交互 |
| FR-804 | 流式播报：不逐字播报；完成时一次摘要播报（可配 off/polite） | Must | Given 追加 100 次，Then 播报 ≤ 5 次 |
| FR-805 | 文案 i18n：内置 zh-CN/en-US（复制/加载中/解析失败等）；可覆盖 | Should | Given en-US，Then 内置文案英文 |
| FR-806 | 减少动效：尊重系统"减少动态效果"，关闭骨架动画 | Should | Given 减少动效，Then 无动画 |

### 6.9 FR-9xx 诊断 / 测试 / 打包

| 编号 | 需求 | 优先级 | 验收标准 |
| --- | --- | --- | --- |
| FR-901 | 快速接入：示例代码 ≤ 10 行完成流式渲染 | Must | Given README 示例，Then 30 分钟跑通 |
| FR-902 | 诊断/指标回调：解析耗时、块数、重解析字符数、降级次数、渲染耗时 | Must | Given 一次流式会话，Then 可获取各指标 |
| FR-903 | 测试工具：无头渲染、确定性控件树 dump、golden 快照、交互探针、流式驱动器 | Must | Given 插件，Then 可用工具写一致性测试 |
| FR-904 | 流式等价性测试：随机切分追加 vs 一次性渲染的 dump 一致 | Must | Given 属性测试 1 万次，Then 100% 一致 |
| FR-905 | 版本与打包：核心/测试/可选插件分包；SemVer；`Directory.Packages.props` 集中版本 | Must | Given 1.0 发布，Then 版本锁定、依赖清单可审计 |
| FR-906 | 性能基准：解析/追加/1MB 首屏/1000 块滚动；CI 阈值回归失败 | Must | Given CI，Then 产出 P50/P95 报告 |

## 7. 非功能需求

| 编号 | 类别 | 指标 | 目标值 | 测量 |
| --- | --- | --- | --- | --- |
| NFR-01 | 性能 | 首屏渲染 20KB Markdown | P95 ≤ 100ms | 基准/无头测量 |
| NFR-02 | 性能 | 追加 100 字符（解析+嫁接） | P95 ≤ 16ms | 流式基准 |
| NFR-03 | 内存 | 1000 条消息（宿主虚拟化后） | ≤ 200MB | 内存快照 |
| NFR-04 | 性能 | 尾部窗口重解析 | ≤ 1ms（8–16KB） | 微基准 |
| NFR-05 | 流畅性 | 长消息滚动 | ≥ 55FPS（默认 StackPanel；不足则虚拟化） | 帧率采样 |
| NFR-06 | 解析 | 1MB 文档解析 | ≤ 250ms（后台线程） | 微基准 |
| NFR-07 | 线程 | UI 线程不执行解析/图片解码；Append 可任意线程 | 0 阻塞违规 | 线程断言测试 |
| NFR-08 | 框架 | `net10.0` 单目标；SDK 10.0.114 | 编译/运行通过 | CI |
| NFR-09 | 裁剪/AOT | 显式注册、无反射扫描；`PublishTrimmed` 示例可运行 | 0 阻断警告（AOT 尽力） | 发布验证 |
| NFR-10 | 确定性 | 同输入+主题+插件 ⇒ 控件树 dump 一致 | 100% | golden 回归 |
| NFR-11 | 可测试性 | 核心行覆盖率 | ≥ 80% | CI 覆盖率 |
| NFR-12 | A11y | WCAG 2.1 AA：键盘全可达、对比度 ≥ 4.5:1、语义完整 | 0 严重违规 | 自动检查 + 人工走查 |
| NFR-13 | 稳定性 | 1000 次消息重建后托管内存增长 | ≤ 5%，无泄漏 | 循环压测 |
| NFR-14 | 安全 | 默认：脚本执行 0、网络请求 0、恶意输入崩溃 0 | 0/0/0 | 安全语料 |
| NFR-15 | 包体积 | 核心包（不含 Avalonia/Markdig） | ≤ 1.5MB | CI 产物检查 |

## 8. 约束与假设

**约束**

- C-1 基于 Markdig 1.5.1（BSD-2），锁版本、不 fork、不改公共 API。
- C-2 基于 Avalonia 12.1.5（MIT），1.0 不做多平台抽象。
- C-3 目标框架 `net10.0`；SDK 10.0.114。
- C-4 依赖须 MIT/BSD/Apache-2.0；禁止 GPL/AGPL 传染性依赖。
- C-5 离线可构建/运行；运行期零网络（宿主注入资源除外）。
- C-6 核心包零图表/高亮引擎依赖；它们只在可选插件包。

**假设**

- A-1 宿主负责 Agent 通信、消息历史、交互结果的处理与发送。
- A-2 宿主负责消息级虚拟化（长会话只创建可见消息的控件）。
- A-3 Agent 输出以 Markdown 文本为载体，`pura-agent` 围栏由宿主提示词约定。
- A-4 插件由宿主显式安装与注册。
- A-5 目标设备为中端桌面/笔记本。

## 9. 里程碑（对应提案的落地顺序）

| 里程碑 | 名称 | 范围 | 退出标准 |
| --- | --- | --- | --- |
| M0 | 技术验证 | 解析路由 + 默认渲染组 + 块宿主 + 主题；版本锁定；流式等价性测试骨架 | 标准 Markdown 样例渲染正确；dump 基线建立；版本组合锁定 |
| M1 | Agent 交互核心 | `pura-agent` 解析 + `choice`（单选/多选）+ 事件上报 + 只读/降级 + confirm | 交互示例端到端可用；键盘可完成；非法载荷降级正确 |
| M2 | 插件机制 | 渲染器注册表/优先级 + `ICodeHighlighter` + `IDiagramRenderer` + 高亮/图表插件各 1 个 + 自定义组件示例 | 接入真实高亮/图表引擎不需改契约 |
| M3 | 流式会话 | `Append/Complete/Abort` + 尾部窗口 + 增量嫁接 + 滚动跟随 + 长消息性能 | 逐段追加与一次性渲染 dump 等价；性能达标 |
| M4 | 1.0 硬化 | 性能/内存、A11y、安全语料、打包、示例应用、文档 | NFR 达标；1.0 发布清单完成 |

## 10. 决策

### 10.1 架构决策

| # | 决策 | 记录 |
| --- | --- | --- |
| D1 | 核心直接依赖 Avalonia，跨平台由 Avalonia 承担；不建渲染适配层 | ADR-0016、ADR-0022 |
| D2 | 无 IR：Markdig AST 直连 Avalonia 控件；补 `IAstSanitizer` 净化 pass | ADR-0011、ADR-0020 |
| D3 | `pura-agent` 围栏 + JSON 为主路径；容器指令为插件 | ADR-0012 |
| D4 | 目标框架 net10.0（本机 SDK 10.0.114，不下载新 SDK），依赖取最新稳定版 | ADR-0013 |
| D5 | 图表/公式/高亮外置插件契约；核心不定义图表 schema | ADR-0014 |
| D6 | 流式 append-only，块级脏检查 + 尾部窗口 + 视觉树嫁接 | ADR-0015、ADR-0010 |
| D7 | 交互只产事件，传输归宿主 | ADR-0006 |
| D8 | 1.0 不渲染 HTML | ADR-0009 |
| D9 | 不引入平台无关虚拟渲染树；用 `BlockRecord` + 块级嫁接 | ADR-0021 |

### 10.2 已确认的产品决策（2026-11-05 第二轮）

| # | 决策 | 结论 |
| --- | --- | --- |
| Q1 | 平台与跨平台方式 | 核心依赖 Avalonia；Windows/macOS/Linux 由 Avalonia 覆盖 |
| Q2 | `choice` 上报时机 | 单选点击即上报；多选点击"确认"后上报 |
| Q3 | 图表 | 核心不保留 `ChartSpec`；`IDiagramRenderer` 插件提供（schema 在插件包） |
| Q4 | 数学公式 | 插件支持（1.1 候选），核心不依赖 |
| Q5 | 代码高亮 | 插件支持；后续提供官方默认高亮插件 |
| Q6 | 长消息虚拟化 | 当前 `StackPanel`，后续按性能实测优化 |

### 10.3 暂不实现、保留触发条件

| 项 | 1.0 | 触发条件 |
| --- | --- | --- |
| IR（文档模型） | 不做 | 非 Avalonia 后端 / 服务端渲染 / 节点级缓存 / Markdig 无法适配的 breaking |
| 虚拟渲染树 | 不做 | 多后端 / 块内超长内容 / 渲染结果序列化 |
| 渲染适配层 | 不做 | Blazor/MAUI/非 Avalonia 宿主 / Avalonia 平台限制 |
| 官方高亮插件 | 后续 | M2 之后按需发布 |
| 数学公式插件 | 后续（1.1） | 有真实需求时 |

> 三项"暂不实现"的完整分析与回补成本见 [`07-IR-虚拟渲染树-多平台-讨论与决策.md`](07-IR-虚拟渲染树-多平台-讨论与决策.md)。
