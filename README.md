# Plain Craft Launcher (PCL) NoRestrictDownload 自动构建

这是独立的第三方构建，不是 PCL 官方版本。上游源码来自 [Meloong-Git/PCL](https://github.com/Meloong-Git/PCL)；请在使用或再分发前阅读上游的 [LICENCE](https://github.com/Meloong-Git/PCL/blob/main/LICENCE)。

本仓库**不保存 PCL 源码**：GitHub Actions 运行时临时检出上游 Release 对应的 tag（包括子模块），只在运行器上把 `Plain Craft Launcher 2/Modules/ModSecret.vb` 中唯一的 `Public Const RestrictMcDownload As Boolean = True` 替换为 `False`。随后按上游方式构建 Release，成功后才发布 EXE 和本次替换的补丁。Release 的 tag 指向本仓库的自动化代码，**并非**上游源码；Release 说明会注明对应的上游提交。

## 触发及失败处理

- 每天 UTC 06:17 检查一次上游已经发布的 Release（包括预发布），也可在 Actions 页手动运行 `Build upstream release without download restriction`。跨仓库的 Release 事件无法直接触发此仓库；无需第三方服务或额外凭据。
- 从上游 `2.99.0.0` 起按发布时间依次处理尚未发布的版本，目标 tag 为 `no-restrict-<上游 tag>`。旧版本的代码不一定有该常量，不自动补发。Action 检查已存在的下游 Release，避免重复构建/发布；失败可重新运行。
- 若常量消失、改名或出现多个声明，**立即失败，不发布**；构建或获取依赖失败也不发布。此时需检查上游变化并调整仓库中的补丁脚本，然后重新运行工作流。不会让模型自动修改并发布未经审查的程序。
- 免费的 OpenCode 模型能否在全新 GitHub runner 上匿名调用、可用模型及其额度可能变化；当前链路不依赖它，也不会使用本地 OpenCode 配置。
- GitHub 可能延迟/跳过定时任务，且可能在公开仓库长期（60 天）无活动后停用定时任务；出现这种情况可手动运行或在仓库 Actions 设置中重新启用。

本仓库远程地址：`git@github.com:YukiHimeno/PCL2NoRestrictDownload.git`。
