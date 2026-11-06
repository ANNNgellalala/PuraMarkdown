# Git 版本管理与发布流程

> 目标：统一分支、提交、版本号与发布动作，让代码、文档、NuGet 包与 Git tag 始终一致。

---

## 1. 分支模型

| 分支 | 用途 | 规则 |
| --- | --- | --- |
| `main` | 唯一长期分支，始终可构建、测试通过 | 禁止直接推送（有远端后由 PR 保护） |
| `feat/<topic>` | 新功能 | 从 `main` 切出，短生命周期，完成后 squash 合并 |
| `fix/<topic>` | 缺陷修复 | 同上；修复必须带回归测试 |
| `docs/<topic>` | 纯文档 | 可合并回 `main` |
| `release/<version>` | 发布前冻结（可选） | 只接受发布修复；打 tag 后合并回 `main` |

当前仓库为本地初始化，尚未配置远端；添加远端后建议保护 `main` 并要求 PR。

```bash
git remote add origin <url>
git push -u origin main
git push --tags
```

---

## 2. 提交规范（Conventional Commits）

格式：

```
<type>(<scope>): <summary>
```

常用 `type`：

| type | 用途 |
| --- | --- |
| `feat` | 新功能 |
| `fix` | 缺陷修复 |
| `perf` | 性能优化 |
| `refactor` | 不改变行为的重构 |
| `test` | 测试 |
| `docs` | 文档 |
| `build` | 构建/依赖/版本 |
| `ci` | CI |
| `chore` | 杂项 |

`scope` 建议：`core`、`streaming`、`render`、`agent`、`plugins`、`virtualization`、`a11y`、`docs`、`demo`、`repo`。

示例：

```
fix(streaming): remove stale tail block before committing stable window
perf(streaming): precompute code highlights off the UI thread
docs(virtualization): add block virtualization metrics
build(repo): centralize package version in Directory.Build.props
```

要求：

- 提交前本地通过 `dotnet build` 与 `dotnet test`；
- 一个提交只做一件事，消息说明"为什么"而不只是"改了什么"；
- 不提交 `bin/`、`obj/`、`artifacts/`、`TestResults/`、`BenchmarkDotNet.Artifacts/`。

---

## 3. 版本号

单一版本源：**`Directory.Build.props` 的 `<Version>`**（当前 `0.1.0-alpha.1`）。

- 四个 NuGet 包（核心/Testing/ColorCode/Mermaid）继承该版本；
- Git tag 使用 `v<version>`，例如 `v0.1.0-alpha.1`；
- 发布前必须保证：`Directory.Build.props` 版本 == Git tag（去掉 `v`）== CHANGELOG 最新条目。

发布新版本步骤：

```bash
# 1) 更新版本与变更日志
#    Directory.Build.props <Version>
#    CHANGELOG.md

# 2) 验证
dotnet build PuraMarkdown.slnx
dotnet test tests/PuraMarkdown.Tests/PuraMarkdown.Tests.csproj
dotnet pack PuraMarkdown.slnx -c Release -o artifacts/nuget

# 3) 提交与打 tag
git add -A
git commit -m "build(repo): release 0.1.0-alpha.1"
git tag -a v0.1.0-alpha.1 -m "PuraMarkdown 0.1.0-alpha.1"
git push origin main --tags
```

---

## 4. 仓库约定文件

| 文件 | 作用 |
| --- | --- |
| `.gitignore` | 排除构建产物、测试结果、BenchmarkDotNet、包输出、IDE 缓存 |
| `.gitattributes` | 文本自动换行归一、二进制文件标记 |
| `CHANGELOG.md` | 面向使用者的版本变更记录 |
| `global.json` | 固定 .NET SDK（10.0.114，latestFeature） |
| `Directory.Build.props` | TFM/语言/Nullable/版本等共享属性 |
| `Directory.Packages.props` | 中央包版本管理 |
| `.github/workflows/ci.yml` | build/test/pack |

---

## 5. 当前基线

- 分支：`main`
- 基线提交：仓库初始化 + 全部实现/文档/CI
- 标签：`v0.1.0-alpha.1`
- 不包含：`bin/`、`obj/`、`artifacts/`、`BenchmarkDotNet.Artifacts/`、测试日志

> 说明：本轮只做本地 Git 初始化与基线提交；远端地址、分支保护、签名与发布流水线在配置远端后补充。
