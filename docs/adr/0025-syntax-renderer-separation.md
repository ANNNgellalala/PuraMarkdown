# ADR-0025：B 阶段语法/渲染分离（Markdig AST 契约）

- 状态：已接受（2026-11-08）
- 关联：[B→C 技术演进路径](../32-技术演进路径-B到C.md)、[语法与渲染分离讨论](../31-语法与渲染分离讨论.md)、ADR-0011、ADR-0020

## 背景

`PuraMarkdown` 长期把 Markdig 解析、流式扫描和 Avalonia 渲染放在同一程序集；Arc.Avalonia 若想基于语法树自定义控件，无法只依赖语法层。

在 .NET 生态下 Markdig 是事实标准，隐藏 Markdig 的收益有限。因此 B 阶段采用 **Markdig AST 作为语法契约**，只分离"语法服务"和"UI 渲染"，不复制节点模型。

## 决策

1. 新增 `PuraMarkdown.Syntax`，只依赖 Markdig，不依赖 Avalonia。
2. `PuraDocument` 只承载 `MarkdownDocument`、版本、诊断、大纲等旁路信息，**不复制节点**。
3. `PuraMarkdown` 保留默认渲染包，消费 `PuraDocument`；新增 `PuraDocumentView` 与 `IPuraNodeRenderer`。
4. 流式扫描器移入 Syntax；渲染层消费稳定文档。
5. 现有 `PuraMarkdownView` / `PuraPipelineBuilder` 保持兼容，内部委托 Syntax。
6. C 阶段（语义模型）只在触发条件成立时启动；B 预留 `PuraDocument` 扩展接缝。

## 理由

- 零 AST 转换，保留性能与 Markdig 扩展生态；
- Arc 可直接消费 Markdig AST，自定义控件；
- Syntax 包无 Avalonia，可用于测试/工具/其他渲染器；
- 避免双模型维护和扩展映射成本。

## 后果

- Syntax 包与 Markdig 版本绑定；通过支持节点子集 + 版本锁定管理。
- `PuraMarkdown` 依赖 Syntax；包数量增加。
- 流式解析/扫描从渲染层迁移，需要回归测试。
- C 阶段可在 `PuraDocument` 上增量加入语义模型，不破坏 B 消费者。

## 备选

- C 语义模型：解耦更强，但转换/双模型成本高（暂不采用）。
- 只拆程序集不拆 API：Arc 仍绑定渲染层（未采用）。
