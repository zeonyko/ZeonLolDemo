# ZeonLolDemo

一个 LoL 风格的单线战场 Demo，想认真把「客户端即时反馈」和「服务器最终裁定」这件事做完整。

按下移动或技能时，客户端会立刻响应；命中、伤害和最终位置则由独立游戏服务器结算。客户端与服务器共享同一份战斗规则，避免两边各写一套后再反复对齐。

> 这不是完整 MOBA，也不是任何现有游戏的私服。它更适合用来阅读、演示和继续打磨一套权威服务器战斗链路。

![Unity](https://img.shields.io/badge/Unity-2022.3.62f3-111111?logo=unity) ![.NET](https://img.shields.io/badge/.NET-8-512BD4?logo=dotnet) ![Platform](https://img.shields.io/badge/Platform-Windows-0078D4?logo=windows) ![License](https://img.shields.io/badge/License-MIT-22C55E)

## 这个 Demo 里有什么

- 权威服务器：移动、命中、伤害、Buff 和弹道都由服务器结算（20Hz，默认 TCP `8888`）。
- 客户端预测：自己移动时先本地模拟，收到服务器确认后回放尚未确认的输入；其他单位走快照插值。
- 技能双轨：客户端先播放动作与特效，服务器复核施法并广播真实结果。
- 共用规则：`Shared/` 同时供客户端和服务器使用，包含移动、冷却、射程、施法条件与网络协议。
- 可玩的战场：英雄、小兵、防御塔、兵营、水晶、野怪和训练假人；推掉水晶即可结束对局。
- 完整的本地流程：配置同步、热更新资源服务、PC / Android 出包均有脚本入口。

```
输入 → 本地立即反馈 → 服务器结算 → 播放结果 / 修正位置
```

## 快速开始

仅支持 **Windows**。需要 Unity **2022.3.62f3** 和 .NET 8 SDK；只有测试热更新或真机包时才需要 Node.js LTS。

1. 双击 `Tools/init-初始化.bat`。首次克隆必须执行：它会连接共用代码，并生成本机开发配置。
2. 双击 `Tools/start_server-启动权威服务器.bat` 启动游戏服务器。
3. 在 Unity Hub 中打开 **`Client/`** 文件夹，保持 Launch 的 **EditorSimulate** 模式，点击 Play。

日常开发不需要启动热更新服务，也不需要手动处理 `boot_config.json`。想少点几次脚本，可以打开 `Tools/控制台.bat`。

### 常用操作

| 操作 | 方法 |
| --- | --- |
| 移动 / 镜头 | 右键点地 / 中键拖动 |
| 施放技能 | 按热键进入瞄准；方向技能再左键确认 |
| 打断施法 | `S` 或 `Esc` |
| 改配置 | 只改根目录 `Config/`，然后运行 `Tools/sync_config-同步配置表.bat` |

热键以 `Config/Input/SkillHotkeys.json` 为准，HUD 也可以直接点击。手机端使用虚拟摇杆和 HUD 按钮。

## 项目结构

| 路径 | 说明 |
| --- | --- |
| `Client/` | Unity 客户端：预测、瞄准、动画、特效和 HUD |
| `Server/` | .NET 8 权威游戏服务器 |
| `Shared/` | 双端共用的普通 C# 规则、协议和配置 Schema，不依赖 Unity |
| `Config/` | 配置唯一来源：技能、Buff、弹道、地图、单位和热键 |
| `Tools/` | 初始化、同步配置、启服、热更新和出包脚本 |

`Shared/` 不会复制进客户端。初始化脚本会在 `Client/Assets/Game/Scripts/Shared` 创建一个指向根目录 `Shared/` 的 Windows Junction；规则只需改一处。

## 核心设计

### 服务器说了算，客户端不等

客户端可先模拟、先播放，但最终状态以服务器为准：

- 移动使用同一份 `MovementSimulator` 模拟；偏差明显时客户端再平滑或直接纠正。
- 技能先由 `SkillAdmit` 做本地预检，再由服务器复核、结算并广播。
- 动画、特效和镜头只负责表现，不参与命中与伤害判断。

### 技能把逻辑和表现分开

同一份技能配置分为 `Logic` 与 `Presentation`。前者提供给两端判定，后者只留在客户端播放动画、音效、特效与镜头。关键帧如 `Windup`、`Hit`、`Recovery` 在两端同名，位移则在 `CommitBlink` / `CommitDash` / `CommitJump` 时真正提交。

### 配置只认一个源头

根目录 `Config/` 是唯一权威源。运行同步脚本后，客户端获得完整表现配置，服务器只保留结算需要的逻辑配置。不要直接修改 `Client/` 或 `Server/` 中生成的副本。

## 继续阅读代码

| 想了解什么 | 从这里开始 |
| --- | --- |
| 服务器主循环 | `Server/Program.cs`、`Server/Battle/BattleSystem.cs` |
| 客户端进战场 | `Client/Assets/Game/Scripts/Bootstrap/GameEntry.cs` |
| 移动预测与回放 | `PredictionMovementComponent.cs`、`Shared/Simulation/MovementSimulator.cs` |
| 施法与结果播放 | `SkillCastComponent.cs`、`Playback.cs` |
| 施法条件与协议 | `Shared/Logic/Skills/SkillAdmit.cs`、`Shared/Net/NetProtocol.cs` |
| 热更新启动 | `Client/Assets/Launch/Scripts/LaunchBoot.cs` |

## 热更新、真机与出包

编辑器日常调试使用 **EditorSimulate**；真机和安装包使用 **HostPlay**，两者不是同一条流程。

1. 先运行初始化，或在 Unity 菜单执行 `Launch / 生成启动配置`。
2. 启动 `Tools/local_dev_server-启动本地热更服务.bat`。
3. 在 Build Settings 选择 PC 或 Android，再通过 `Launch / 出安装包` 构建。

热更新服务默认使用 `8080`，游戏服务器默认使用 `8888`。真机联调时，请把这两个端口加入防火墙例外，并检查 `Tools/local_dev.json` 中是局域网 IP，**不要使用 `127.0.0.1`**。

产物位于 `CDN/` 与 `Dist/`，设备日志位于 `Logs/device/`；这些目录均不会提交到 Git。

## 范围与致谢

项目目前不包含多房间、账号体系、帧同步或浏览器直连战斗服；Mac / Linux 初始化脚本也尚未提供。

战斗节奏受英雄联盟启发，灰盒地图与资源均为网上下载；本项目与 Riot Games 无关，仅供参考交流学习。

## License

[MIT](LICENSE)。Unity 引擎不随仓库分发；HybridCLR 等第三方包保留各自许可证。
