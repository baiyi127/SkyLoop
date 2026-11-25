# 天穹循环 SkyLoop

面向国服 Dalamud API 15 / .NET 10 的伊修加德重建循环插件。基于官方 [SamplePlugin](https://github.com/goatcorp/SamplePlugin) 模板，使用 [AGPL-3.0 许可](LICENSE.md)。

当前为开发测试版本。游戏内加载、NPC 窗口回调与完整循环尚未验证通过，分享时请保留这一说明。

## 第三方插件源安装

将以下地址加入卫月设置 → 实验性 → 自定义插件仓库，保存并刷新插件列表，搜索“天穹循环 SkyLoop”安装：

```text
https://raw.githubusercontent.com/baiyi127/SkyLoop/main/repo.json
```

安装兼容的国服 vnavmesh 后，输入 `/skyloop` 打开配置。新增宏默认使用共通宏；请填写自己的编号、选择兑换物品并记录制作位置，再开始测试。

源码与下载：[GitHub 仓库](https://github.com/baiyi127/SkyLoop) · [发布版本](https://github.com/baiyi127/SkyLoop/releases)。插件源按版本固定下载地址，后续更新保持此订阅地址不变。

## 已实现

- 共通/个人宏按 **1–100** 编号组成可增删、启停、上下排序的队列。
- 直接调用游戏的宏模块，不模拟键盘。先确认宏确实执行并结束，再判断配置的完成条件；支持宏执行结束、制作成功、指定物品数量增加。
- 填写配方 ID 后，每轮自动打开配方并启动一次普通制作，然后执行整组宏。制作成功检查实际成品增加；没有材料、职业不符、制作失败或宏超时会停止。
- 默认四个主背包剩余空格 **≤10** 时前往 NPC 提交可交付的重建制作收藏品；只提交达到表内最低收藏价值的物品。
- 默认振兴票 **≥9000** 时兑换所选物品；提交前还检查奖励是否可能超过票数上限，必要时提前兑换。
- 兑换物品下拉框从**当前国服客户端数据表**读取名称和价格，支持搜索，可更换。购买后核对扣票及物品增加，按保留票数和背包容量限制购买。
- 提交和兑换 NPC 可从客户端数据读取默认位置，也可选中 NPC 重新记录；制作点由玩家记录。
- 使用 vnavmesh 的 IPC 计算路径和移动，返回制作点继续循环。区域改变、战斗、死亡、卡住、异常窗口或接口异常会停止。

## 编译与加载

需要 .NET 10 SDK、国服卫月 API 15 和兼容版本的 vnavmesh。构建脚本默认读取 `%APPDATA%\XIVLauncherCN\addon\Hooks\dev` 中的卫月依赖；请使用与自己游戏客户端匹配的依赖。升级客户端后需要重新编译和实测。

```powershell
.\build.ps1
```

也可显式指定依赖：

```powershell
.\build.ps1 -DalamudPath 'C:\path\to\matching\Dalamud'
```

输出 `dist\SkyLoop.dll`、`dist\SkyLoop.json` 和 `dist\SkyLoop.zip`。

发布维护者可运行 `./prepare-release.ps1`，生成仓库根目录的 `repo.json` 和 `dist/SkyLoop-版本.zip`；先上传对应版本的 Release 资源，再更新公开插件源。发布包包含 AGPL 许可证，对应源码位于同版本 Git 标签。

1. 游戏聊天输入 `/xlsettings`，在“实验性 / Experimental”的开发插件位置添加 **dist 中 SkyLoop.dll 的绝对路径**。
2. 输入 `/xlplugins`，点击“扫描开发版插件”，在开发插件列表启用“天穹循环 SkyLoop”。
3. 输入 `/skyloop` 打开界面。加载时默认停止，不会自动执行。
4. 在“宏队列”添加共通宏的编号并排序。如果一件物品需要两个宏，前一个选“宏执行结束”，最后一个选“本次制作成功”。
5. 填写配方 ID 后，插件每轮会自动开始制作。配方为0时，需要宏自行启动制作或执行自己的非制作流程；宏完成条件由用户配置。
6. 在“背包与兑换”选择兑换物品，设置票数阈值和保留票数。
7. 在“NPC 与路线”记录当前制作位置，核对或记录提交/兑换 NPC。所有位置必须位于当前同一区域。
8. 启用兼容国服版本的 **vnavmesh** 并等待当前地图网格就绪，再点击开始。`/skyloop stop` 可停止并取消本插件宏和移动。

## 验证范围与限制

已完成国服 API 15 引用编译及隔离的流程测试。可用 `dotnet run --project tests/FlowChecks/FlowChecks.csproj -c Release` 重跑流程检查。流程测试使用模拟的客户端与服务，未覆盖实际游戏窗口回调。游戏内加载、NPC 窗口回调和完整制作/提交/兑换闭环仍需实际验证；编译通过不代表已经跑通游戏流程。

- “宏执行结束”只表示游戏宏解释器结束；要判断动作结果，请用制作成功或物品增加。按配方循环制作时，最后一个宏必须配置结果条件。
- 自动提交限于 **伊修加德重建的制作收藏品**；不是采集物检品、普通收藏品交易或宇宙探索。
- 导航范围限于同一区域，不包括跨区传送、进入天穹街或自动切换职业。
- 配方必须能用当前职业制作。插件不自动购材料、维修、穿装备或选用额外食药。
- 本插件不会在 UI 布局不符时继续猜测点击；日志和状态栏会保留停止原因。
- 没有可提交收藏品、背包无法容纳兑换物、已持有独占物品、所选物品未解锁或未出售等情况会停止，需要调整配置。

## 开发资料

- [Dalamud 开发入门](https://dalamud.dev/plugin-development/getting-started/)
- [官方 SamplePlugin 模板及开发插件加载说明](https://github.com/goatcorp/SamplePlugin)
- [vnavmesh IPCProvider](https://github.com/awgil/ffxiv_navmesh/blob/master/vnavmesh/IPCProvider.cs)：使用 `Nav.PathfindCancelable`、`Path.MoveTo`、`Path.Stop`。停止会取消仍在计算的本插件路径，避免稍后重新移动。
- [国服 vnavmesh](https://github.com/AtmoOmen/ffxiv_navmesh-cn)
- [TheCollector](https://github.com/Ashylila/TheCollector)：仅研究 HWDSupply、ShopExchangeCurrency 等窗口交互思路，未复制其实现。
- [FFXIVClientStructs](https://github.com/aers/FFXIVClientStructs)：宏、背包、配方及 UI 结构参考；实际编译引用国服随卫月分发的版本。

寻路采用 **NPC编号确定目标 + 世界坐标确定终点 + 导航网格绕障**。只按屏幕 NPC 名牌前进容易受遮挡和视距影响；直接朝坐标直线移动则容易撞障碍。NPC已加载时使用其实际位置，否则先按记录坐标进入附近区域。

代码由 AI 辅助生成，供开发与测试；未提交官方插件仓库。分发的插件包仅包含本插件，卫月、FFXIVClientStructs、vnavmesh 与游戏数据由用户已有环境提供。
