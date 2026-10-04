# 卫戍协议：盟约 · 联机启动器

《明日方舟》同人游戏「卫戍协议：盟约」的**联机启动器**：一键组网、开房、开服、打包分发、自动更新。
把"和朋友一起玩"从"每人装一堆工具"简化成**双击、点两下**。

![version](https://img.shields.io/badge/version-1.2-2ea44f)
![platform](https://img.shields.io/badge/platform-Windows%2010%2F11-blue)
![license](https://img.shields.io/badge/license-GPL--3.0--or--later-blue)

---

## 它解决什么问题

游戏本体（[sganggs/Stronghold-Protocol](https://github.com/sganggs/Stronghold-Protocol)）自带局域网联机，
但**异地玩需要组网**。市面方案（Tailscale / ZeroTier / Radmin / 内网穿透）都要每人单独装和配。

本启动器把这件事包进一个 exe：

- **组网**：内置 [openp2p](https://github.com/openp2p-cn/openp2p) 引擎，开房即自动打通隧道，异地像同宿舍
  （不需要管理员权限、不装虚拟网卡、不装驱动）
- **开房**：一键起游戏服务器 + 生成邀请（链接含房间名/密码/房主节点名/联机令牌，朋友粘贴即进）
- **下载游戏本体**：没装过游戏本体的人不用再收几百 MB 的包 —— 启动器直接从**原作者的官方 Release**
  下载（支持断点续传、直连太慢自动切国内镜像），解压校验通过才替换，失败不动原来那份
- **断线自愈**：隧道断了会自己重连（状态栏会写出来），**重连成功后自动帮你重新打开一次游戏页面**
  （浏览器里的连接断了不会自己恢复，以前只能手动刷新）
- **环境自检**：缺 Node.js 会弹框引导安装；自动定位游戏本体
- **打包分发**：生成"轻量包"（只有启动器 + 引擎，约 3.5 MB）或"开房包"
  （再加一份便携版 Node.js，给想自己开房的朋友）；**两种包都不含任何游戏素材**，可以放心发
- **自动更新**：启动时检查本仓库 Release，有新版就一键更新（下载 → 替换 → 自动重启）；
  国内直连慢会自动切换镜像站加速
- **日志**：`logs\` 下按组件分文件（启动器 / 游戏服务 / 组网），便于排错

## 组网与令牌（openp2p）

- 组网走 openp2p 的**端口转发模型**：房主只是"在线"，朋友把房主的 3000 端口映射到自己本机的 3000，
  浏览器打开 `http://127.0.0.1:3000` 即可
- **同一个令牌（token）下的节点才能互相 P2P 直连**，所以房主和所有朋友必须用同一个令牌
- 令牌**不在源码里硬编码**，按以下顺序获取：
  1. `启动器配置.txt` 里的 `o2ptoken=` 行
  2. 本机装过 OPL 的话，自动借用其 `bin\config.json` 里的 `Token`
  3. 都没有 → 首次联机时弹框让用户粘一次
- 房主开房时会把令牌写进邀请链接（`&o2p=…`），朋友整段粘贴即自动保存，**一般不用手填**
- 也可以用命令行一次写入：`卫戍协议启动器.exe -token:<你的令牌>`

## 快速开始

### 用（玩家）

1. 下载 [Release](https://github.com/makise2/stronghold-protocol-launcher/releases) 里的 zip，解压到任意目录
2. 双击 `卫戍协议启动器.exe`
3. **房主**：联机开黑 → 点 `开房并复制邀请` → 把复制到的内容粘到群里
4. **朋友**：联机开黑 → 选「加入朋友的房间」→ 粘贴 → 点 `进房`
5. 双方在游戏里：房主「创建同盟」拿到 4 位密钥 → 发群 → 朋友粘贴即可同房

> **第一次进游戏要从房主那边下载约 250 MB 素材**：页面会转圈、图案一张张慢慢出现，这是正常的 ——
> 别关页面、别按刷新（关了要重下）。素材浏览器只缓存 1 天，隔天再玩可能又要等一次。
> 点「进房」后启动器会先握手（十几秒），**状态栏变成 `● 隧道已连接` 才会自动打开游戏页面**。
> 想**自己开房**的人需要游戏本体与 Node.js —— 设置页的「下载游戏本体…」可以直接下（约 290 MB），
> Node.js 用便携版或自行安装；只加入的人则什么都不用装。

## 隧道状态怎么看（1.2 起）

状态栏（窗口右上角）有五种：

| 状态栏 | 意思 |
|---|---|
| `○ 隧道未连接（还没开房/还没进房）` | 还没开始 |
| `◌ 隧道握手中…（已等 N 秒，一般 5–20 秒）` | 进程起来了，正在登录，正常 5–20 秒 |
| `● 隧道已连接（房主已在线）` / `（已连上房主）` | 通了，可以进游戏 |
| `◌ 隧道断开，正在自动重连…` | 网络抖动/对方掉线，openp2p 正在自己接回来；**重连成功会自动重开一次游戏页面** |
| `✗ 隧道没连上：<原因>（看 logs\组网.log）` | 登录被拒 / 超 75 秒 / 隧道进程退出 |

失败时 `logs\启动器.log` 里会写 5 行可操作指引（令牌是否一致、防火墙放行、公司网常封 27183 端口、
换手机热点试、房主那边也要显示已连接）。


### 编译（开发者）

只需要 Windows 自带的 C# 编译器，**不需要安装任何 SDK**：

```powershell
# 双击 build.bat，或手动：
$fw = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
& "$fw\csc.exe" /nologo /target:winexe /optimize+ /platform:anycpu /win32icon:app.ico `
  /out:卫戍协议启动器.exe `
  /reference:"$fw\System.dll" /reference:"$fw\System.Core.dll" `
  /reference:"$fw\System.Drawing.dll" /reference:"$fw\System.Windows.Forms.dll" `
  /reference:"$fw\System.IO.Compression.dll" /reference:"$fw\System.IO.Compression.FileSystem.dll" `
  Launcher.cs
```

- 目标框架 .NET Framework 4.8 → **用户端零运行环境**
- 单文件 exe，约 519 KB

## 文件说明

| 文件 | 说明 |
|---|---|
| `Launcher.cs` | 启动器全部源码（约 5100 行） |
| `app.ico` | 应用图标（自制，不含第三方素材） |
| `build.bat` | 一键编译 |
| `NOTICE.md` | 版权与来源声明（**必读**：本仓库不含任何游戏素材） |
| `第三方许可-openp2p-MIT.txt` | 组网引擎的 MIT 许可全文（分发时必须随附） |

> `openp2p/`（组网引擎二进制）不进本仓库，请从
> [openp2p-cn/openp2p](https://github.com/openp2p-cn/openp2p) 获取后放到启动器同级的 `openp2p\` 目录。

## 依赖与来源

- 游戏本体：[sganggs/Stronghold-Protocol](https://github.com/sganggs/Stronghold-Protocol)（GPL-3.0-or-later）
- 组网引擎：[openp2p](https://github.com/openp2p-cn/openp2p)（MIT），需自行下载放入 `openp2p/`
- 运行环境：Node.js 22/24（仅开房者需要；程序会检测并引导安装，也可用便携版）

## 许可证与声明

- **本启动器代码**：GPL-3.0-or-later
- 游戏本体代码：来自 sganggs/Stronghold-Protocol，GPL-3.0-or-later
- **游戏素材**（美术 / 音乐 / 音效 / 文本 / 数据）：版权归**上海鹰角网络 / Yostar** 所有，**不适用 GPL**，
  本仓库不包含任何游戏素材，请勿单独再分发
- 本项目是**非官方同人作品**，与上海鹰角网络科技有限公司、Yostar 及其关联方**没有任何关系**，
  未获其授权或认可
- **仅供学习交流与个人非商业使用，严禁任何形式的盈利**（包括售卖、付费分发、收费开服、广告变现等）
- 本项目按「现状」提供，不提供任何担保

权利人如认为本项目侵犯其权益，请通过 Issue 联系，我们会立即删除相关内容。

## 打包分发出去的 zip 里会带什么

启动器的「打包分享」生成两种 zip，**两种都不含任何游戏本体与游戏素材**：

| 包 | 内容 | 能不能公开传 |
|---|---|---|
| **轻量包**（推荐） | 启动器 exe + openp2p 引擎 + `第三方许可-openp2p-MIT.txt` + `版权与来源-必读.txt` | ✅ 可以 |
| **开房包** | 轻量包 + 便携版 Node.js（给想自己开房 / 玩单机的人） | ✅ 可以 |

- 分发 openp2p 的二进制时，MIT 要求**随附其版权声明与许可全文** —— 启动器已自动写进每个 zip，别删这两个文件
- 游戏本体不在任何一种包里：对方第一次开房或单机前，让他用启动器里的
  「下载游戏本体…」从原作者仓库自己下（约 290 MB，实测十几秒），素材版权始终归鹰角 / Yostar

## 致谢

- 游戏本体的作者 [sganggs](https://github.com/sganggs) 及所有贡献者
- [openp2p](https://github.com/openp2p-cn/openp2p) 提供组网能力（MIT）
- [PRTS Wiki](https://prts.wiki/) 等社区资料
