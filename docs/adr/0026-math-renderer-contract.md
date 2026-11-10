# ADR-0026：数学公式——核心提供语法、TeX 回退与渲染器契约；CSharpMath 仅作示例插件

- 状态：已接受（2026-11-09）
- 关联：ADR-0014（图表/高亮插件化；本 ADR 补充其公式部分）、ADR-0025、[Emoji/容器/公式评估](../37-Emoji-自定义容器-数学公式支持评估.md)

## 背景

数学公式需要进入 Markdown 能力，但引擎选型与依赖边界有冲突：

- 如果把 `CSharpMath.Avalonia` 直接引用进核心包，用户即使使用其它第三方公式插件也必须携带该依赖，无法替换，且增加核心包体积/AOT/升级风险；
- 如果只做 Markdig 语法不处理行内节点，`MathInline` 会走默认 `ToString()` 兜底，显示类型名，不可接受；
- Markdig 1.5.1 只解析 `$...$` / `$$...$$`，不识别 LLM 常用的 `\(...\)` / `\[...\]`。

## 决策

1. **核心只提供语法与文本回退**：`PuraPipelineBuilder.UseMathematics()` 启用 Markdig 数学扩展；核心默认渲染器把行内/块级公式显示为 TeX 源码（块级带复制按钮），**不引用任何 TeX 排版引擎**。
2. **核心修复行内插件分发**：`DefaultInlineRenderer` 在 `MathInline` 与未知行内节点上先询问 `IPuraInlineRenderer`（`PuraRendererRegistry.ResolveInline`），插件可接管；块级 `MathBlock` 由插件通过 `UseRenderer` 覆盖默认回退渲染器。
3. **CSharpMath 只作为示例**：`samples/PuraMarkdown.Plugins.Samples` 提供 `CSharpMathMathPlugin`（行内 `MathView` + 块级 `MathView`），Demo/Spike 引用它展示真实排版。`CSharpMath.Avalonia` 的 PackageReference 只存在于示例项目，不进核心、不进任何发布包依赖。
4. **分隔符范围**：核心与示例都只支持 `$...$` / `$$...$$`；`\(...\)` / `\[...\]` 由宿主/Agent 侧归一化，或由第三方插件自行扩展 parser。
5. **流式**：核心扫描器识别 `$$` 块；示例插件在 `RenderContext.IsTail` 时回退 TeX 文本，稳定/完成后才创建 `MathView`。非法 TeX 由插件回退文本并告警，不影响其它块。

## 理由

- 保持核心“零引擎依赖、可替换”的插件架构（与图表/高亮一致），避免无效依赖；
- 核心仍可安全启用数学语法：没有插件时也显示 TeX 源码，信息不丢失；
- 示例代码可复制到宿主/第三方包，是否引入 CSharpMath 由宿主决定；
- 用户明确要求“CSharpMath 只在 Demo 示例中引用，核心不依赖”。

## 后果

- 核心不随包发布 CSharpMath；`PuraMarkdown.nupkg` 依赖仍为 Syntax/Avalonia/ItemsRepeater/Markdig。
- 需要真正排版的宿主必须自行实现/复制示例渲染器，或使用第三方插件；文档需给出示例路径。
- 行内插件分发被修复后，`IPuraInlineRenderer` 才真正可用（此前 `DefaultInlineRenderer` 从不调用，docs/24 已记录该遗留项）。
- 示例项目的 CSharpMath 版本为 prerelease，升级风险由示例承担；发布说明需注明“官方未提供数学引擎包”。
- `\(...\)` / `\[...\]` 仍不支持，需要在文档中明确。

## 备选

- **核心内置 CSharpMath**：已实现后按用户反馈撤回（造成无效依赖、无法替换）；未采用。
- **官方独立包 `PuraMarkdown.Math.CSharpMath`**：可作为后续选项，但本次按用户要求只放示例，不发布官方包。
- **自研 TeX 渲染**：明确不做。
