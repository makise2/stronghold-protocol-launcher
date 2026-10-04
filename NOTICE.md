# 版权与来源声明（NOTICE）

**卫戍协议：盟约 · 联机启动器**（下称"本启动器"）是个人自制的第三方启动器，
用于给开源同人游戏 [sganggs/Stronghold-Protocol](https://github.com/sganggs/Stronghold-Protocol) 做组网、开房、开服与分发。
与上海鹰角网络科技有限公司（Hypergryph）、Yostar 及其关联方**没有任何关系**，未获其授权或认可。

---

## 1. 本启动器的代码

Copyright (C) 2026 makise2

本启动器的源代码（`Launcher.cs`、`build.bat` 等，见仓库根目录）以 **GNU 通用公共许可证第 3 版或（由你选择）任何更新版本**（GPL-3.0-or-later）发布，全文见仓库根目录的 [LICENSE](LICENSE)。
你可以在该许可证的条件下使用、修改和再分发这些代码。

---

## 2. 本仓库**不包含**的内容（重要）

本启动器**不是游戏**，也**不包含任何游戏内容**。以下内容全部不在本仓库中：

- 《明日方舟》及「卫戍协议」相关的**名称、角色、美术、Spine 模型、界面图、音乐音效、文本与游戏数据** ——
  版权归上海鹰角网络科技有限公司及其授权方（Yostar 等）所有；
- 游戏本体**代码** —— 版权归 [sganggs/Stronghold-Protocol](https://github.com/sganggs/Stronghold-Protocol) 的作者及贡献者（GPL-3.0-or-later）；
- 游戏本体的 `public/assets/**`、`public/fonts/**`、官方数据生成的 `data/*.json` 等素材与数据 ——
  上游项目自己也不把它们放进仓库（见上游 `.gitignore` 的注释
  `game assets: (c) Hypergryph / Yostar ... NEVER committed`），
  而是由每个人在本机用官方工具获取。

**本仓库不含上述任何内容，本启动器也不会把它上传或公开分发。**
唯一的例外是启动器的「打包分享」功能：它可以把游戏本体打进一个"完整包"交给朋友，
但那只是本机生成的 zip、只在朋友之间私下传递，**不经过本项目的任何服务器、也不公开发布** ——
细节和限制见下一节。

---

## 3. 本启动器打包出的分发文件（zip）里有什么

启动器的「打包分享」功能生成两种 zip：

| 包 | 内容 | 能不能公开传 |
|---|---|---|
| **轻量包** | 本启动器 exe + openp2p 组网引擎 | ✅ 可以（不含游戏内容） |
| **完整包** | 轻量包 + 游戏本体（含 `public/assets/` 素材） | ❌ **不可公开上传**，只限朋友之间私下非商业分享 |

打包"完整包"时，游戏本体及素材仍受其各自的许可约束：
素材部分（鹰角/Yostar）**不在 GPL 范围内**，只允许**学习、研究和个人非商业娱乐**用途，
**不得用于任何形式的盈利**（出售、付费分发、收费开服、广告、打赏等）。
请保留游戏本体自带的 `LICENSE` / `NOTICE.md` / `THIRD-PARTY-NOTICES.md`，并同样注明"非官方、非商业"。

---

## 4. 运行环境与第三方组件

| 组件 | 来源 | 许可证 | 本启动器如何使用 |
|---|---|---|---|
| 组网引擎 | [openp2p-cn/openp2p](https://github.com/openp2p-cn/openp2p) | **MIT** | 作为独立进程调用，随轻量包/完整包一起分发；许可全文见 [第三方许可-openp2p-MIT.txt](第三方许可-openp2p-MIT.txt) |
| Node.js | [nodejs.org](https://nodejs.org/) | MIT | 仅在房主开房/单机时调用；可由用户自行安装，或用便携版 |
| 游戏本体 | [sganggs/Stronghold-Protocol](https://github.com/sganggs/Stronghold-Protocol) | GPL-3.0-or-later | 运行时仅调用其 `server/index.js`，不修改其任何代码；本地打"完整包"时会原样带上它（见第 3 节），仍受其 GPL 与素材声明的约束 |
| .NET Framework | Microsoft | 随 Windows 提供 | 启动器基于 .NET Framework 4.x 编译（Windows 10/11 自带） |

openp2p 的 README 免责声明：免费使用、禁止非法用途、无担保。

---

## 5. 只有非商业用途

本启动器与其分发文件**仅供学习、研究与个人非商业娱乐**。
禁止任何形式的盈利，包括但不限于：出售或付费分发、收费开服、付费房间或会员、植入广告、
与本项目挂钩的打赏/赞助/众筹、打包进任何收费产品或服务。

---

## 6. 权利人通知与删除

如果你是相关权利人，认为本项目或其分发文件中有任何不妥内容，
请在本仓库提交 Issue（或通过 GitHub 联系仓库所有者），我们会尽快删除相关内容或下架分发文件。

---

## 7. 免责声明

本启动器按「原样」提供，**不附带任何明示或暗示的担保**。
使用、架设或公开本项目及其分发文件的风险（网络安全、第三方联机工具与服务、当地法律法规）
由使用者自行承担。本启动器不索取任何游戏账号，也不会读取或上传玩家的游戏文件。

---

**English summary.** An unofficial, non-commercial third-party launcher for the fan remake
[sganggs/Stronghold-Protocol](https://github.com/sganggs/Stronghold-Protocol). The launcher's own code is
GPL-3.0-or-later. This repository contains **no** Arknights / Stronghold-Protocol game assets or code; all such
content is © Hypergryph / Yostar and its licensors (or its respective authors) and is not covered by the GPL.
The bundled networking engine is [openp2p](https://github.com/openp2p-cn/openp2p) (MIT).
Not affiliated with or endorsed by Hypergryph or Yostar. Non-commercial use only: no selling, paid distribution,
paid hosting, ads, donations or any other monetisation. No warranty of any kind.
