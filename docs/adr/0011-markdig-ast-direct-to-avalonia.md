# ADR-0011：渲染直接消费 Markdig AST、产出 Avalonia 控件（取消中间 IR）

- 状态：已接受（取代 ADR-0001、ADR-0003）
- 日期：2026-11-05
- 关联：`docs/00-需求对齐与设计复审.md`、架构 §2/§5/§6

## 背景

v0.2 设计为"Markdig AST → 自建 IR（PuraDocument）→ RenderNode 虚拟树 → 适配器 → 平台控件"。用户新需求方案提出四层架构：解析路由（Markdig AST）→ 节点分发（渲染器注册表）→ 渲染执行（直接产出 Avalonia `Control`）→ 流式会话。

## 决策

**取消 IR 与 RenderNode 虚拟树**。Markdig AST 即文档模型；`IPuraRenderer` 直接接收 `MarkdownObject`，返回 Avalonia `Control`。

## 理由

1. **只读单平台**：没有多平台/序列化/跨进程需求，IR 的收益（可移植、可快照）在 1.0 不成立。
2. **减少映射层**：AST→IR→RenderNode→Control 三级转换，带来三套类型、三套测试和三处语义漂移；直连后只有 AST→Control。
3. **插件门槛低**：插件作者熟悉 Markdig 节点即可开发，无需学习 PuraMarkdown 自有节点模型。
4. **流式足够**：块级粒度（顶层块）已满足"只更新变化块"；无需字符级/节点级 diff 抽象。
5. **原型更快**：与提案的落地顺序（先解析+基础渲染）一致，M0 可快速验证。

## 后果

- **核心直接依赖 Avalonia**（见 ADR-0016）；多平台需未来抽契约。
- 失去 IR 级集中安全过滤点：URL/HTML 策略必须由默认渲染器强制执行（架构 §11）。
- 失去 IR golden 快照：改用"无头 Avalonia 控件树 dump"作为确定性基线（需求 FR-209/FR-904）。
- 无法直接复用 v0.2 的 IR 代码；Markdig 研究、安全策略、流式正确性结论仍有效。

## 备选与回退

若未来需要多平台或服务端渲染，可在 `IPuraRenderer` 之上再抽"平台无关渲染描述"（相当于把 RenderNode 加回来）。届时是一次受控重构，成本约 3–4 周，ADR 另行记录。
