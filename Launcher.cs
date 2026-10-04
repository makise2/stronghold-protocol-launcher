// 卫戍协议：盟约 · 联机启动器
// Copyright (C) 2026 makise2
//
// 本程序是自由软件：你可以按 GNU 通用公共许可证（GPL）第 3 版或（由你选择）任何更新版本
// 的条款再分发和/或修改它。本程序按「原样」提供，不附带任何担保。
// 许可证全文见仓库根目录的 LICENSE，版权与来源声明见 NOTICE.md。
// ---------------------------------------------------------------------------
// 说明：本启动器是第三方工具，游戏本体来自开源同人项目 sganggs/Stronghold-Protocol（GPL-3.0-or-later）；
//       游戏素材（美术/音乐/音效/文本/数据）版权归上海鹰角网络 / Yostar，不在本程序与 GPL 范围内。
// ═══════════════════════════════════════════════════════════════════════════════
//  卫戍协议：盟约 · 联机启动器                                            v1.2
//  完整客户端外壳：启动画面 / 库主页（单机开始）/ 联机（组网+开房+房间面板）/ 设置（安装、自启、更新、打包、卸载）
//  单文件 WinForms 程序，用 Windows 自带的 csc.exe 编译（见 build.bat），
//  目标框架 .NET Framework 4.8 → 使用者零运行环境。
//  界面适配要点：尺寸/字号统一由一个缩放系数换算（Sc/Lg），别混用两套单位；详见下面「交付前必查」。
//
//  ┌──────────────────────────────────────────────────────────────────────────┐
//  │  ★ 交付前必查（每条都出过事，别跳过）                                      │
//  ├──────────────────────────────────────────────────────────────────────────┤
//  │ 1. 删干净调试代码：搜 "TODO(交付前删除)" 和 "[状态]"、"[调试]"               │
//  │    特别是写在 Timer / 轮询 / 循环里的 Log —— 它不会只写一次，会把日志刷爆    │
//  │ 2. 不碰用户全局资源：桌面/开始菜单/启动项/注册表/防火墙规则                  │
//  │    且"已存在的快捷方式绝不覆盖"；开发目录(IsDevFolder)必须跳过所有全局操作     │
//  │ 3. 部署三步走：杀进程 → 复制 → 比对 SHA256                                 │
//  │    不做第 3 步，exe 被运行中的进程锁住会"静默复制失败"，改了等于没改          │
//  │ 4. 页面容器不要"先算尺寸再加 Anchor"（会超出父容器，导致下半部不渲染）         │
//  │ 5. 文字宽度按**内层容器**算，不要按窗口/屏幕宽度假设                          │
//  │ 6. 所有尺寸/字号/MeasureText 结果统一乘 DpiScale()，一个系数贯穿到底           │
//  │ 7. 布局用游标式（ResetCur/Sec/Line/BtnRow/Mk），不要手写 y 坐标              │
//  │ 8. 改完实跑一遍，确认 logs\布局诊断.txt 里三页"重叠数=0"                     │
//  │ 9. 有生命周期的临时状态（如 banner）必须有清理点，否则会读到过期值             │
//  │10. 别用脚本正则批量删多字段声明行 —— 曾经把 126KB 源码删成 77KB（有备份）     │
//  └──────────────────────────────────────────────────────────────────────────┘
//
//  非官方同人作品，与鹰角网络 / Yostar 无任何关系；仅供个人非商业使用，禁止盈利。
//  游戏本体来自 github.com/sganggs/Stronghold-Protocol（GPL-3.0-or-later）。
// ═══════════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;

static class Prog
{
    const string AppName = "卫戍协议启动器";
    const string GameName = "卫戍协议：盟约";
    const string Version = "1.2.3";   // ★ 用户 2026-10-04 决定：什么都没发布过，就用 1.2.3（v1.2.2/v1.2.4/v1.2.5 都不发）
    const string DefaultRoomName = "AAAlappland";
    const string DefaultRoomPass = "909090pq";
    const string PublicPeer = "tcp://public.easytier.cn:11010";
    const string GameFolderName = "Stronghold-Protocol";
    // 默认安装位置：有 D 盘的机器照老规矩放 D:\卫戍协议\（房主这台就是）；
    // 没有 D 盘（很多粥友机器只有 C）就退回 %LOCALAPPDATA%，否则点「安装」会直接"创建目录失败"
    static string DefaultInstall
    {
        get
        {
            try { if (Directory.Exists(@"D:\")) return @"D:\卫戍协议\卫戍协议启动器"; }
            catch { }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppName);
        }
    }
    const string Repo = "makise2/stronghold-protocol-launcher";

    static string[] seats = new string[] { DefaultRoomName, DefaultRoomPass, "1" };
    static string gamePath = "";
    static string cachedGame;
    static bool optAskInstall = true, optAutoBoot = false;
    static bool cliInstall, cliUninstall, cliShortcut, cliNoSplash;
    static bool cliHost, cliJoin, cliStart, cliKill;
    static Size cliWinSize = Size.Empty;   // -winsize:WxH 测试钩子：强制窗口尺寸，用来模拟小屏
    static bool cliScrollTest;             // -scrolltest 测试钩子：跑一遍滚动链路并写日志
    static Size cliScreen = Size.Empty;    // -screensize:WxH 测试钩子：模拟别的电脑的屏幕可用区
    static float cliForceDpi;              // -forcedpi:150 测试钩子：强制缩放系数（百分数，如 125）
    static bool cliLayoutCheck;            // -layoutcheck 测试钩子：自检本页并写报告，退出码表示成败
    static int cliSetTab;                  // -settab:2 测试钩子：自检"分享与关于"子页
    static string cliUiFont;               // -uifont:名称 测试钩子：强制 UI 字体（模拟 Win7 没有雅黑 UI 的情况）
    static Size cliResize = Size.Empty;    // -resize:WxH 测试钩子：启动后再改窗口大小（复现"用户把窗口拖小"）
    static string cliDlgCheck;             // -dlgcheck:install|token|pack|splash|gamedl 测试钩子：自动自检某个对话框
    static string cliGameDlTest;           // -gamedltest:<zip或local:路径> 测试钩子：离线跑一遍下载(可断点)+解压剥壳+校验
    static bool cliTunnelSim;              // -tunnelsim 测试钩子：离线验"断线后自动重开游戏页面"这条链路
    static string cliPackOut;              // -pack:<zip路径> / -packfull:<zip路径> 钩子：无人值守打包（发版用）
    static bool cliPackFull;               // 上面那个钩子：true = 带上便携版 Node（打"开房包"）
    static bool cliShake;                  // -uishake 测试钩子：把"交互后才出现"的布局逐个走一遍自检
    static string lcSuffix = "";           // 非空 = 震荡模式：报告名加后缀、检查完不退出
    static string lcStep = "";             // 当前震荡步骤名
    static string cliSeat = "";
    static string cliToken = "";
    static int cliPage = -1;   // -page:0/1/2 直接打开某一页（给脚本和验收用）

    static Color C_BG = Color.FromArgb(17, 19, 24);
    static Color C_PANEL = Color.FromArgb(24, 27, 33);
    static Color C_BOX = Color.FromArgb(58, 66, 78);
    static Color C_EDGE = Color.FromArgb(118, 132, 150);
    static Color C_MINT = Color.FromArgb(110, 232, 200);
    static Color C_ON = Color.FromArgb(32, 104, 88);
    static Color C_OFF = Color.FromArgb(66, 74, 88);
    static Color C_LOG = Color.FromArgb(12, 14, 18);
    static Color C_TEXT = Color.FromArgb(232, 238, 246);
    static Color C_DIM = Color.FromArgb(176, 186, 200);

    static Form form;
    // ═══════════════════════════════════════════════════════════════════════════
    // 【已知死代码清单 —— 改动前务必先读这段】
    //
    // 背景：这些字段/函数在 2026-10-03 的清理中经编译器与人工双重核对确认为"无使用"，
    //       当时试图删除，但因批量正则误删活代码导致源码损坏（126KB→77KB），
    //       故**保留原样、仅作标注**。它们不影响运行与性能，只影响可读性。
    //
    // 未使用字段（csc 警告 CS0169 = 从未使用 / CS0649 = 从未赋值）：
    //   linkBox, copyBtn, stopBtn2, setIncludeGame, heroGoNet,
    //   bodyState, bodyPath, hHomeTips, homeKillBtn, bodyBrowse, bodyOpen   (CS0169)
    //   keyValue, memberList, updBtn, updLine                              (CS0649)
    //
    // 未使用函数（无任何调用方）：
    //   ParsePeers()  ← 只被 MemberText 调用，而 MemberText 自己也没人调，整条链是死的
    //   MemberText()
    //   NextFreeSeat()      （原本是"自动分配编号"，简化联机页时被弃用）
    //   RefreshGameLine()   （被 RefreshGameUi 取代）
    //   BtnAt()             （被 FlowButtons 取代）
    //
    // 删除时的正确做法：**在 IDE 里逐一重命名/删除（有引用分析）**，
    //        不要用脚本批量正则替换多字段声明行——上一事故就是这么来的。
    // ═══════════════════════════════════════════════════════════════════════════

    static Panel content;
    static Label statusLine;
    static Button navHome, navNet, navSet;
    static TextBox logBox;

    // 联机页控件
    static Label modeHost, modeJoin, gameLine, membersLine, roomInfo, seatHint;
    static TextBox roomBox, passBox, seatBox, linkBox;
    static Button startBtn, openBtn, copyBtn, browseBtn, shareBtn, stopBtn2;

    // 主页/设置页控件
    static Label setPathLabel, updateLine;
    static Button setUpdBtn;   // 设置页「检查更新」按钮：发现新版时它会变成「立即更新到 vX.Y」
    static CheckBox setIncludeGame;

    static Process pEt, pNode;
    static System.Windows.Forms.Timer tick;
    static Panel heroPanel, sidePanel;
    static Label bodyStateLbl, bodyPathLbl;
    static Label heroTitle, heroSub, heroDesc, heroStateLbl, heroGoNet, bodyState, bodyPath, sideTipLbl, hHomeTips;
    static Label setTokenLabel;   // 设置页：联机令牌状态
    static Label keyValue, memberList, netHostTip, netJoinTip, joinHint, roomStatus;
    static TextBox netLink, joinPaste;
    static Button joinBtn, advToggle, killBtn;
    static Button homeKillBtn;
    static Label legalLabel;
    static Panel advPanel;
    static bool advOpen;
    static Label logTitle;   // 「日志」标题（展开高级选项时要跟着下移，所以要有名字）
    static Panel modePanel;  // 模式按钮容器（宽度要随页面走，所以提升成字段）
    static Button keyCopyBtn, updBtn;
    static Label updLine;
    static string latestTag = "";
    static Button heroPlay, bodyBrowse, bodyOpen;
    static bool busy;
    static bool started;
    // 解析 easytier-cli peer 的表格：返回 "虚拟IP|主机名"
    // ⚠ 死代码（无调用方）：保留仅为避免误删风险，见文件顶部"已知死代码清单"
    static List<string> ParsePeers()
    {
        List<string> res = new List<string>();
        try
        {
            ProcessStartInfo si = new ProcessStartInfo(EtCli(), "peer");
            si.UseShellExecute = false; si.CreateNoWindow = true;
            si.RedirectStandardOutput = true; si.RedirectStandardError = true;
            si.StandardOutputEncoding = Encoding.UTF8; si.StandardErrorEncoding = Encoding.UTF8;   // 主机名可能是中文
            Process p = Process.Start(si);
            string o = p.StandardOutput.ReadToEnd();
            p.WaitForExit(2000);
            foreach (string line in o.Split('\n'))
            {
                string tt = line.Trim();
                if (!tt.StartsWith("10.126.126.")) continue;
                string[] cells = tt.Split('|');
                string ip = cells[0].Trim().Split('/')[0].Trim();
                string host = cells.Length > 1 ? cells[1].Trim() : "";
                res.Add(ip + "|" + host);
            }
        }
        catch { }
        return res;
    }

    // 把在线节点列成清单，并标出自己
    // ⚠ 死代码（无调用方）
    static string MemberText(List<string> peers)
    {
        if (pEt == null || pEt.HasExited) return "（虚拟网未连接）";
        if (peers.Count == 0) return "还没看到其他节点（等粥友点「开始」）";
        StringBuilder sb = new StringBuilder();
        foreach (string s in peers)
        {
            string[] a = s.Split('|');
            string num = a[0].Replace("10.126.126.", "");
            bool me = (num == seats[2]);
            sb.Append(me ? "★ " : "· ");
            sb.Append("编号 " + num);
            if (a.Length > 1 && a[1].Length > 0) sb.Append("　" + a[1]);
            if (me) sb.Append("（你）");
            sb.Append("　　");
        }
        sb.Append("\r\n共 " + peers.Count + " 个节点在线（编号要和各自启动器里填的一致）");
        return sb.ToString();
    }

    // 从 2 开始找没被占用的编号
    // ⚠ 死代码（无调用方）
    static string NextFreeSeat(List<string> peers)
    {
        List<string> used = new List<string>();
        foreach (string s in peers) used.Add(s.Split('|')[0].Replace("10.126.126.", ""));
        for (int i = 2; i <= 9; i++)
        {
            string c = i.ToString();
            if (!used.Contains(c)) return c;
        }
        return "2";
    }
    // 从配置文件读同盟密钥；没有则生成一个 4 位码并写回
    static string KeyOf()
    {
        try
        {
            if (File.Exists(CfgFile))
            {
                foreach (string l in File.ReadAllLines(CfgFile, Encoding.UTF8))
                    if (l.StartsWith("key=") && l.Length > 4) return l.Substring(4).Trim();
            }
            string abc = "ABCDEFGHJKLMNPQRSTUVWXYZ";
            Random rnd = new Random();
            char[] buf = new char[4];
            for (int i = 0; i < 4; i++) buf[i] = abc[rnd.Next(abc.Length)];
            string k = new string(buf);
            File.AppendAllText(CfgFile, "key=" + k + "\r\n", Encoding.UTF8);
            return k;
        }
        catch { return "----"; }
    }

    // 自检并报告最新整合包版本
    static void CheckUpdateNow()
    {
        if (updLine == null) return;
        updLine.Text = "检查中…";
        updLine.ForeColor = C_DIM;
        ThreadPool.QueueUserWorkItem(delegate
        {
            string msg; Color col = C_TEXT;
            try
            {
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
                HttpWebRequest rq = (HttpWebRequest)WebRequest.Create("https://api.github.com/repos/" + Repo + "/releases/latest");
                rq.Timeout = 15000;
                rq.UserAgent = "sp-launcher";
                using (WebResponse rp = rq.GetResponse())
                using (StreamReader sr = new StreamReader(rp.GetResponseStream()))
                {
                    string j = sr.ReadToEnd();
                    int i = j.IndexOf("\"tag_name\":\"");
                    string tag = i >= 0 ? j.Substring(i + 12, j.IndexOf('"', i + 12) - i - 12) : "";
                    string local = Version;   // ★ 比的是启动器自己的版本，不是游戏本体版本（以前这里查错了对象，等于查不出更新）
                    if (tag.Length == 0) msg = "没读到版本号，稍后再试";
                    // ★ 必须比数值大小，不能比字符串相等：否则本机 v1.2.4 遇上仓库里最新的 v1.2.3 会报"发现新版启动器 v1.2.3"
                    else if (VerNum(tag) <= VerNum(local))
                    { msg = "已是最新（启动器 " + local + "）"; col = C_MINT; }
                    else
                    {
                        msg = "发现新版启动器：" + tag + "（本机 " + local + "）→ 点「打开项目主页」下载";
                        col = Color.FromArgb(240, 200, 120);
                    }
                }
            }
            catch (Exception ex) { msg = "检查失败：" + ex.Message; }
            string m = msg; Color c = col;
            Ui(delegate { if (updLine != null) { updLine.Text = m; updLine.ForeColor = c; } });
        });
    }

    static string GameLocalVersion()
    {
        try
        {
            string g = FindGame(false);
            if (g == null) return "";
            string pj = Path.Combine(g, "package.json");
            if (!File.Exists(pj)) return "";
            foreach (string l in File.ReadAllLines(pj, Encoding.UTF8))
            {
                int i = l.IndexOf("\"version\"");
                if (i < 0) continue;
                int a = l.IndexOf('"', i + 9);
                int b = l.IndexOf('"', a + 1);
                if (a > 0 && b > a) return l.Substring(a + 1, b - a - 1).Trim();
            }
        }
        catch { }
        return "";
    }
    // 标题字体：优先思源黑体（Noto Sans SC，鹰角风格），没有则回退雅黑
    static string pickTitleFamily;
    static string TitleFamily()
    {
        if (pickTitleFamily != null) return pickTitleFamily;
        pickTitleFamily = UiFamily();
        try
        {
            foreach (FontFamily f in FontFamily.Families)
                if (f.Name == "Noto Sans SC") { pickTitleFamily = "Noto Sans SC"; break; }
        }
        catch { }
        return pickTitleFamily;
    }
    // ★ UI 字体族必须逐级回退：Win7 没有 "Microsoft YaHei UI"（Win8 才有），
    //   直接用它会被 GDI+ 静默换成默认字体 → 中文宽度/行高全变 → 界面裁字。
    static string pickUiFamily;
    static string UiFamily()
    {
        if (pickUiFamily != null) return pickUiFamily;
        if (cliUiFont != null && cliUiFont.Length > 0) { pickUiFamily = cliUiFont; return pickUiFamily; }   // 测试钩子
        string[] want = new string[] { "Microsoft YaHei UI", "Microsoft YaHei", "Noto Sans SC", "SimHei", "SimSun" };
        pickUiFamily = "Microsoft YaHei UI";
        try
        {
            foreach (string w in want)
                foreach (FontFamily fam in FontFamily.Families)
                    if (fam.Name == w) { pickUiFamily = w; return pickUiFamily; }
        }
        catch { }
        return pickUiFamily;
    }
    static Font FTitle(float pt, FontStyle st) { return FontOf(TitleFamily(), pt, st); }
    // 字号跟随窗口像素一起放大，保证文字与控件同比例
    static readonly Dictionary<string, Font> fontCache = new Dictionary<string, Font>();
    // ★ 屏幕装不下设计尺寸时整体等比缩小：这个系数同时作用到"布局尺寸"和"字号"上，
    //   所以任何 DPI/任何屏幕下版式比例都一样，只是整体大小变了（下限 0.62 保证还能读）。
    static float fitK = -1f;
    static float FitK()
    {
        if (fitK > 0) return fitK;
        float s = DpiScale();
        Rectangle wa = Wa();
        float k = 1f;
        int dw = (int)(940 * s), dh = (int)(620 * s);
        if (dw > wa.Width - 40) k = Math.Min(k, (wa.Width - 40f) / dw);
        if (dh > wa.Height - 40) k = Math.Min(k, (wa.Height - 40f) / dh);
        if (k < 0.62f) k = 0.62f;
        fitK = k;
        return fitK;
    }
    static Font FontOf(string fam, float pt, FontStyle st)
    {
        string k = fam + "|" + pt + "|" + (int)st;
        Font f;
        if (!fontCache.TryGetValue(k, out f)) { f = new Font(fam, pt * FitK() * EmuFontK(), st); fontCache[k] = f; }
        return f;
    }
    static Font Fui(float pt) { return FontOf(UiFamily(), pt, FontStyle.Regular); }
    static Font Fui(float pt, FontStyle st) { return FontOf(UiFamily(), pt, st); }
    static Font Fmono(float pt) { return FontOf("Consolas", pt, FontStyle.Regular); }
    static float dpiScale;
    static float DpiScale()
    {
        if (dpiScale > 0) return dpiScale;
        try { using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) dpiScale = g.DpiX / 96f; } catch { dpiScale = 1f; }
        if (dpiScale < 0.8f || dpiScale > 4f) dpiScale = 1f;
        return dpiScale;
    }
    // ★ -forcedpi（自检用"模拟别的电脑的缩放"）必须把字号一起补偿，否则模拟是假的：
    //   GDI+ 永远按本机【真实】DPI 渲染字号，而 Sc() 按【模拟】DPI 放大布局。
    //   实测（真机 150%，-forcedpi:100）：字比版面大 1.5 倍 → 自检报一堆假越界/假截断；
    //   反过来 -forcedpi:200 时字比版面小一半 → 真问题反而漏报。
    //   乘以 模拟DPI/真实DPI 之后，屏幕上的比例才和那台模拟机器一致。
    static float realDpi;
    static float RealDpiScale()
    {
        if (realDpi > 0) return realDpi;
        try { using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) realDpi = g.DpiX / 96f; } catch { realDpi = 1f; }
        if (realDpi < 0.8f || realDpi > 4f) realDpi = 1f;
        return realDpi;
    }
    static float EmuFontK()
    {
        if (cliForceDpi <= 0) return 1f;
        return (cliForceDpi / 100f) / RealDpiScale();
    }
    static string lastMembers = "";
    static int page = 0;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool SetProcessDPIAware();

    // 全局界面缩放：窗口按 DPI 放大后，布局尺寸也必须同比放大，否则内容会溢出被裁
    static float uiS;
    static float UiScale()
    {
        // 内容尺寸与窗口一起按 DPI 放大，保证"窗口多大、内容就占多大"，不会出现大片空白或内容溢出
        if (uiS > 0) return uiS;
        // ★ 唯一系数：字体是 pt（GDI+ 按 DPI 放大），布局就必须同比放大。
        //   以前这里恒等于 1 —— 150% 下行距不够（标签互相压、按钮文字被截）、100% 下宽度不够（右边被裁）。
        uiS = DpiScale() * FitK();
        return uiS;
    }
    static int Sc(int v) { return (int)Math.Round(v * UiScale()); }
    // ★ MeasureText 给回来的是"当前 DPI 下的物理像素"，而设计坐标是"设计单位"（96dpi 口径）。
    //   要把量测值当设计宽度用时，必须先 Lg() 换算回设计单位再进 Sc()。
    //   直接 Sc(need.Width) = 把 DPI 乘两遍：150% 按钮比设计宽 1.5 倍、175% 宽 1.75 倍
    //   （实测 200% 时"浏览游戏"按钮被这个虚宽顶出可视区右边界 → 按钮直接看不见了）
    static int Lg(int physPx) { return (int)Math.Round(physPx / UiScale()); }
    // 逻辑尺寸：把物理像素换算回设计单位（容器布局一律用设计单位算，只在 SetBounds 时 Sc 一次）
    static int LW() { return form == null ? 940 : (int)Math.Round(form.ClientSize.Width / UiScale()); }
    static int LH() { return form == null ? 620 : (int)Math.Round(form.ClientSize.Height / UiScale()); }
    static int CW() { return Math.Max(300, LW() - 224 - 8 - 30); }   // 预留垂直滚动条宽度（逻辑单位随缩放变化，30 才够）
    // 是不是"开发/源码目录"：旁边有源码或编译脚本。用于避免误删、避免污染全局资源
    static bool IsDevFolder()
    {
        try
        {
            return File.Exists(Path.Combine(AppDir, "Launcher.cs"))
                || File.Exists(Path.Combine(AppDir, "build.bat"))
                || File.Exists(Path.Combine(AppDir, "probe.cs"));
        }
        catch { return false; }
    }
    // ===== 管理员权限：EasyTier 创建虚拟网卡必需（否则报 "Failed to create adapter" / 0x80070005）=====
    [System.Runtime.InteropServices.DllImport("shell32.dll", SetLastError = true)]
    static extern bool IsUserAnAdmin();

    static bool isElevatedCached;

    // 提权重启：弹一次 UAC，用相同参数重新启动自己，然后退出当前实例
    static void RelaunchElevated()
    {
        try
        {
            string exe = Application.ExecutablePath;
            StringBuilder a = new StringBuilder();
            foreach (string s in Environment.GetCommandLineArgs())
            {
                if (s == exe) continue;
                if (a.Length > 0) a.Append(' ');
                a.Append(s.IndexOf(' ') >= 0 ? "\"" + s + "\"" : s);
            }
            ProcessStartInfo si = new ProcessStartInfo(exe, a.ToString());
            si.UseShellExecute = true;
            si.Verb = "runas";                 // ← 触发 UAC
            si.WorkingDirectory = AppDir;
            Process.Start(si);
            Application.Exit();
        }
        catch
        {
            // 用户点了"否"，UAC 取消 → 继续以普通权限运行（只能单机/加入）
        }
    }

    static bool IsElevated()
    {
        try { return IsUserAnAdmin(); }
        catch { return false; }
    }
    static string AppDir { get { return Path.GetDirectoryName(Application.ExecutablePath); } }
    static string EtCore() { string p = Path.Combine(AppDir, "easytier\\easytier-core.exe"); if (File.Exists(p)) return p; return Path.Combine(EtHome(), "easytier-core.exe"); }
    static string EtCli() { string p = Path.Combine(AppDir, "easytier\\easytier-cli.exe"); if (File.Exists(p)) return p; return Path.Combine(EtHome(), "easytier-cli.exe"); }

    // 引擎目录：优先自带，其次 OPL 自带的 EasyTier（粥友那边也能直接用）
    static string etHome;
    static string EtHome()
    {
        if (etHome != null) return etHome;
        string[] cand = new string[] {
            Path.Combine(AppDir, "easytier"),
            @"C:\Program Files (x86)\Guailoudou\OPL\bin\easytier-windows-x86_64",
            @"C:\Program Files\Guailoudou\OPL\bin\easytier-windows-x86_64"
        };
        foreach (string c in cand) if (File.Exists(Path.Combine(c, "easytier-core.exe"))) { etHome = c; return c; }
        etHome = Path.Combine(AppDir, "easytier");
        return etHome;
    }
    static string CfgFile { get { return Path.Combine(AppDir, "启动器配置.txt"); } }
    // 所有日志统一放在 D:\卫戍协议\logs\ 下，文件名用中文，方便以后排查
    static string logRootCached;
    static string LogDirRoot()
    {
        if (logRootCached != null) return logRootCached;
        string root = Path.GetDirectoryName(AppDir);
        try
        {
            string d = Path.Combine(root == null ? AppDir : root, "logs");
            if (!Directory.Exists(d)) Directory.CreateDirectory(d);
            logRootCached = d;
            return d;
        }
        catch { logRootCached = AppDir; return AppDir; }
    }

    static string TempLog { get { return Path.Combine(LogDirRoot(), "启动器.log"); } }

    // ================= 配置 =================
    static string randStr(int n, string abc)
    {
        Random r = new Random(Guid.NewGuid().GetHashCode());
        char[] b = new char[n];
        for (int i = 0; i < n; i++) b[i] = abc[r.Next(abc.Length)];
        return new string(b);
    }

    static void LoadCfg()
    {
        try
        {
            if (!File.Exists(CfgFile)) return;
            string[] l = File.ReadAllLines(CfgFile, Encoding.UTF8);
            if (l.Length > 0 && l[0].Trim().Length > 0) seats[0] = l[0].Trim();
            if (l.Length > 1 && l[1].Trim().Length > 0) seats[1] = l[1].Trim();
            if (l.Length > 2 && l[2].Trim().Length > 0) seats[2] = l[2].Trim();
            if (l.Length > 3) gamePath = l[3].Trim();
            if (l.Length > 4) optAskInstall = l[4].Trim() != "0";
            if (l.Length > 5) optAutoBoot = l[5].Trim() == "1";
        }
        catch { }
    }
    // 写配置：保持「前 6 行位置不变」（老格式），并原样保留其它行（o2pnode= / o2ptoken= 等）。
    // ★ 别改回只写 6 行的老写法：那样会把 o2pnode= 行抹掉，节点名一变，之前发出去的邀请全部失效。
    static void SaveCfg()
    {
        try
        {
            List<string> keep = new List<string>();
            if (File.Exists(CfgFile))
            {
                foreach (string s in File.ReadAllLines(CfgFile, Encoding.UTF8))
                {
                    string t = s.Trim();
                    if (t.Length == 0 || t.StartsWith("o2pnode=") || t.StartsWith("o2ptoken=")) continue;
                    keep.Add(s);
                }
            }
            while (keep.Count < 6) keep.Add("");
            keep[0] = seats[0]; keep[1] = seats[1]; keep[2] = seats[2]; keep[3] = gamePath;
            keep[4] = optAskInstall ? "1" : "0";
            keep[5] = optAutoBoot ? "1" : "0";
            keep.Add("o2pnode=" + MyO2PNode());
            if (Token().Length > 0) keep.Add("o2ptoken=" + Token());
            File.WriteAllLines(CfgFile, keep.ToArray(), Encoding.UTF8);
        }
        catch { }
    }

    // ================= 日志 =================
    // ★ 令牌打码（2026-10-04 深夜修）：openp2p 会把自己完整的命令行原样打出来，里面带
    //   `-token 一串数字` —— 我们照抄进日志，等于把令牌明文写进文件。而日志正是要发给粥友、
    //   或贴到公开 Issue 里的东西。实测本机 logs\启动器.log 里躺着 17 行明文令牌。
    //   所有落盘日志（Log / LogEt）都先过这一层，以后新增的输出自动被保护。
    static string MaskTok(string s)
    {
        if (s == null) return s;
        string r = s;
        int i = r.IndexOf("-token", StringComparison.OrdinalIgnoreCase);
        while (i >= 0)
        {
            int j = i + 6;
            while (j < r.Length && (r[j] == ' ' || r[j] == '\t' || r[j] == '"')) j++;
            int k = j;
            while (k < r.Length && r[k] != ' ' && r[k] != '\t' && r[k] != '"' && r[k] != ']' && r[k] != ',') k++;
            if (k > j) { r = r.Substring(0, j) + "****" + r.Substring(k); i = r.IndexOf("-token", j + 4, StringComparison.OrdinalIgnoreCase); }
            else i = r.IndexOf("-token", j, StringComparison.OrdinalIgnoreCase);
        }
        return r;
    }
    static void Log(string s)
    {
        s = MaskTok(s);
        try { File.AppendAllText(TempLog, DateTime.Now.ToString("HH:mm:ss") + "  " + s + "\r\n", Encoding.UTF8); } catch { }
        try
        {
            if (logBox != null && logBox.IsHandleCreated)
                logBox.BeginInvoke((Action)delegate
                {
                    logBox.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + s + "\r\n");
                    if (logBox.Lines.Length > 200)
                    {
                        string[] keep = new string[100];
                        Array.Copy(logBox.Lines, logBox.Lines.Length - 100, keep, 0, 100);
                        logBox.Text = string.Join("\r\n", keep);
                    }
                    logBox.SelectionStart = logBox.TextLength;
                    logBox.ScrollToCaret();
                });
        }
        catch { }
    }

    static void LogEt(string s)
    {
        s = MaskTok(s);
        try { File.AppendAllText(Path.Combine(LogDirRoot(), "组网.log"), DateTime.Now.ToString("HH:mm:ss") + "  " + s + "\r\n", Encoding.UTF8); } catch { }
    }


    // ================= 动画与反馈 =================
    static System.Windows.Forms.Timer animTick;
    static int animStep;
    static bool busyAnim;
    static string busyText = "";

    static readonly string[] SPIN = new string[] { "◐", "◓", "◑", "◒" };

    static void StartAnim()
    {
        if (animTick != null) return;
        animTick = new System.Windows.Forms.Timer();
        animTick.Interval = 110;
        animTick.Tick += delegate { animStep++; AnimFrame(); };
        animTick.Start();
    }

    static void AnimFrame()
    {
        if (!busyAnim)
        {
            if (animTick != null) { animTick.Stop(); animTick.Dispose(); animTick = null; }   // 空闲就停，别空转
            return;
        }
        if (statusLine != null)
        {
            statusLine.Text = SPIN[animStep % SPIN.Length] + "  " + busyText;
            statusLine.ForeColor = Color.FromArgb(240, 200, 120);
        }
    }

    static void StatusBusy(string text)
    {
        busyText = text;
        busyAnim = true;
        bannerText = SPIN[0] + "  " + text;
        bannerColor = Color.FromArgb(240, 200, 120);
        bannerUntil = DateTime.Now.AddSeconds(20);   // 上限 20 秒，避免任何异常导致永久卡住
        StartAnim();
        if (statusLine != null) { statusLine.Text = bannerText; statusLine.ForeColor = bannerColor; }
    }

    // 完成提示：闪两下再回到常态
    static void FlashStatus(string text, Color col)
    {
        FlashStatusFor(text, col, 4);
    }

    // 同 FlashStatus，但可指定横幅停留秒数（重要提示停久一点，比如"有新版本"）
    static void FlashStatusFor(string text, Color col, int seconds)
    {
        busyAnim = false;
        ShowBanner(text, col, seconds);
        if (statusLine == null) return;
        StartAnim();
        int n = 0;
        System.Windows.Forms.Timer ft = new System.Windows.Forms.Timer();
        ft.Interval = 260;
        ft.Tick += delegate
        {
            n++;
            if (statusLine != null && statusLine.IsHandleCreated)
                statusLine.ForeColor = (n % 2 == 1) ? Color.White : col;
            if (n >= 4) { ft.Stop(); ft.Dispose(); if (statusLine != null) statusLine.ForeColor = col; }
        };
        ft.Start();
    }

    // ===== 临时横幅与持久状态分离：横幅显示 N 秒后自动让位给真实状态 =====
    static string bannerText = "";
    static Color bannerColor = Color.White;
    static DateTime bannerUntil = DateTime.MinValue;

    static void ShowBanner(string text, Color col, int seconds)
    {
        Log("[横幅] 设置: " + text + " 持续 " + seconds + "s");
        bannerText = text;
        bannerColor = col;
        bannerUntil = DateTime.Now.AddSeconds(seconds);
        if (statusLine != null && statusLine.IsHandleCreated)
        {
            statusLine.Text = text;
            statusLine.ForeColor = col;
            statusLine.Refresh();   // 强制重绘，否则文字不更新
        }
    }

    static bool BannerActive()
    {
        if (bannerText.Length == 0) return false;
        if (DateTime.Now < bannerUntil) return true;
        // 已过期：顺手清掉，避免残留旧消息（否则日志/调试里会一直看到上一次的内容）
        bannerText = "";
        bannerUntil = DateTime.MinValue;
        return false;
    }

    static void ClearBanner()
    {
        bannerText = "";
        bannerUntil = DateTime.MinValue;
        busyAnim = false;
    }
    // 灯光闪烁：用于需要吸引注意的提示
    static void PulseAlert(Label l, string text, Color col)
    {
        if (l == null) return;
        busyAnim = false;               // 结束忙碌态
        l.Text = text;
        // 同步更新横幅：让状态栏文字真正变成这条消息，并在几秒后自动让位给真实状态
        bannerText = text;
        bannerColor = col;
        bannerUntil = DateTime.Now.AddSeconds(4);
        if (l.IsHandleCreated) l.Refresh();
        int n = 0;
        System.Windows.Forms.Timer pt = new System.Windows.Forms.Timer();
        pt.Interval = 320;
        pt.Tick += delegate
        {
            n++;
            if (l.IsHandleCreated) l.ForeColor = (n % 2 == 1) ? col : Color.FromArgb(120, 130, 140);
            if (n >= 8) { pt.Stop(); pt.Dispose(); l.ForeColor = col; }
        };
        pt.Start();
    }

    // 给按钮装悬停/按下反馈（浅色底提亮，深色底加深）
    static bool IsDark(Color c) { return (c.R + c.G + c.B) < 330; }

    static void Flu(Button b)
    {
        if (b == null) return;
        Color baseCol = b.BackColor;
        b.FlatAppearance.MouseOverBackColor = IsDark(baseCol) ? Lighten(baseCol, 22) : Darken(baseCol, 16);
        b.FlatAppearance.MouseDownBackColor = IsDark(baseCol) ? Lighten(baseCol, 40) : Darken(baseCol, 30);
        b.Cursor = Cursors.Hand;
    }

    static Color Lighten(Color c, int d)
    {
        return Color.FromArgb(c.A, Math.Min(255, c.R + d), Math.Min(255, c.G + d), Math.Min(255, c.B + d));
    }

    static Color Darken(Color c, int d)
    {
        return Color.FromArgb(c.A, Math.Max(0, c.R - d), Math.Max(0, c.G - d), Math.Max(0, c.B - d));
    }

    // 模式卡也需要悬停反馈
    static void FluCard(Label l, Color onCol)
    {
        if (l == null) return;
        l.Cursor = Cursors.Hand;
        l.MouseEnter += delegate { if (l.BackColor != onCol) l.BackColor = Lighten(l.BackColor, 16); };
        l.MouseLeave += delegate
        {
            bool on = l.BackColor == C_ON || l.BackColor == Lighten(C_ON, 16);
            l.BackColor = on ? C_ON : C_OFF;
        };
    }    static void Ui(Action a) { try { if (form != null && form.IsHandleCreated) form.BeginInvoke(a); } catch { } }
    static void Status(string s) { Ui(delegate { if (statusLine != null) { statusLine.Text = s; statusLine.Refresh(); } }); }

    // ================= 游戏本体 =================
    static bool IsGame(string d)
    {
        try { return d != null && d.Length > 0 && File.Exists(Path.Combine(d, "server\\index.js")); }
        catch { return false; }
    }

    static string FindGame(bool remember)
    {
        if (IsGame(gamePath)) return gamePath;
        if (cachedGame != null)
        {
            if (cachedGame.Length > 0 && IsGame(cachedGame)) return cachedGame;
            return null;
        }
        List<string> roots = new List<string>();
        roots.Add(AppDir);
        string up = AppDir;
        for (int i = 0; i < 3; i++)
        {
            try { up = Path.GetDirectoryName(up); } catch { break; }
            if (up == null) break;
            roots.Add(up);
        }
        foreach (string r in roots)
        {
            string c = Path.Combine(r, GameFolderName);
            if (IsGame(c)) { if (remember) { gamePath = c; SaveCfg(); } return c; }
            if (IsGame(r)) { if (remember) { gamePath = r; SaveCfg(); } return r; }
        }
        string[] guess = new string[] {
            @"D:\卫戍协议\" + GameFolderName, @"C:\卫戍协议\" + GameFolderName, @"E:\卫戍协议\" + GameFolderName
        };
        foreach (string c in guess) if (IsGame(c)) { if (remember) { gamePath = c; SaveCfg(); } cachedGame = c; return c; }
        cachedGame = "";
        return null;
    }

    static string EnsureGame(bool interactive)
    {
        string g = FindGame(true);
        if (g != null) return g;
        if (!interactive || SelfTestMode()) return null;   // 自检模式绝不能弹要人点的框
        DialogResult dr = MessageBox.Show(
            "没有找到游戏本体（需要 " + GameFolderName + " 文件夹，里面有 server\\index.js）。\n\n" +
            "· 想自己开房/单机，已经下载过 → 点「是」选中那个游戏文件夹。\n" +
            "· 还没有游戏本体 → 点「否」让启动器自动下载（约 290 MB，从原作者官方地址取最新版）。\n" +
            "· 只想加入别人的房间 → 点「取消」，不需要游戏本体。",
            "找不到游戏本体", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        if (dr == DialogResult.No) { GameDownloadFlow(); return FindGame(true); }
        if (dr != DialogResult.Yes) return null;
        return BrowseGame();
    }

    static string BrowseGame()
    {
        FolderBrowserDialog d = new FolderBrowserDialog();
        d.Description = "选中游戏本体文件夹（里面有 server 和 public 两个文件夹）";
        d.ShowNewFolderButton = false;
        try { d.SelectedPath = Directory.Exists(@"D:\卫戍协议") ? @"D:\卫戍协议" : AppDir; } catch { }
        if (d.ShowDialog() != DialogResult.OK) return null;
        string sel = d.SelectedPath;
        if (IsGame(sel)) { gamePath = sel; SaveCfg(); return sel; }
        string sub = Path.Combine(sel, GameFolderName);
        if (IsGame(sub)) { gamePath = sub; SaveCfg(); return sub; }
        MessageBox.Show("这个文件夹里没有 server\\index.js，不像是游戏本体。\n选中的路径：" + sel, "路径不对");
        return null;
    }

    static string LogDir()
    {
        string[] r = new string[] { Path.Combine(AppDir, "logs"), Path.Combine(Path.GetDirectoryName(AppDir), "logs") };
        foreach (string c in r) { try { if (!Directory.Exists(c)) Directory.CreateDirectory(c); return c; } catch { } }
        return AppDir;
    }

    // ================= 系统工具 =================
    static bool? nodeCached;
    static bool nodeProbing;

    // Node.js 用哪个可执行文件：优先用包里自带的便携版（node\node.exe），
    // 没有才退回系统 PATH 里的 node。★ 开房包会带一份便携版，朋友那台就不用装 Node 了
    static string nodeExeCached;
    static string NodeExe()
    {
        if (nodeExeCached != null) return nodeExeCached;
        string[] cand = new string[] {
            Path.Combine(AppDir, "node\\node.exe"),
            Path.Combine(AppDir, "nodejs\\node.exe"),
            Path.Combine(AppDir, "node.exe")
        };
        foreach (string c in cand) if (File.Exists(c)) { nodeExeCached = c; return c; }
        nodeExeCached = "node";   // 系统 PATH
        return nodeExeCached;
    }
    static bool HasPortableNode() { return NodeExe() != "node"; }

    // 找一份可复制的 node.exe：优先包里自带的，其次系统安装的（"开房包"会把它塞进去，粥友就不用装 Node 了）
    static string FindSystemNode()
    {
        try
        {
            if (HasPortableNode()) { string p = NodeExe(); if (File.Exists(p)) return p; }
            string[] cand = new string[] {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs\\node.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "nodejs\\node.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs\\nodejs\\node.exe"),
                @"C:\Program Files\nodejs\node.exe",
                @"C:\Program Files (x86)\nodejs\node.exe"
            };
            foreach (string c in cand) if (File.Exists(c)) return c;
        }
        catch { }
        return null;
    }

    // 真正的探测（只能在工作线程调用）：探测的就是"实际会用来起服"的那个 node
    static bool ProbeNode()
    {
        string nodePath = NodeExe();
        try
        {
            if (File.Exists(nodePath))
            {
                ProcessStartInfo si0 = new ProcessStartInfo(nodePath, "-v");
                si0.UseShellExecute = false; si0.CreateNoWindow = true;
                si0.RedirectStandardOutput = true; si0.RedirectStandardError = true;
                using (Process p0 = Process.Start(si0))
                {
                    string o0 = p0.StandardOutput.ReadToEnd();
                    p0.WaitForExit(4000);
                    if (p0.ExitCode == 0 && o0.Trim().Length > 0) { Log("Node.js：用包里自带的便携版 " + nodePath + "（" + o0.Trim() + "）"); return true; }
                    Log("包里自带的 node 跑不起来（" + nodePath + "），改用系统 PATH 里的 node");
                }
            }
            ProcessStartInfo si = new ProcessStartInfo("node", "-v");
            si.UseShellExecute = false; si.CreateNoWindow = true;
            si.RedirectStandardOutput = true; si.RedirectStandardError = true;
            using (Process p = Process.Start(si))
            {
                string o = p.StandardOutput.ReadToEnd();   // 先读完再等退出，否则可能死锁
                p.WaitForExit(4000);
                return p.ExitCode == 0 && o.Trim().Length > 0;
            }
        }
        catch { return false; }
    }

    // 后台探测一次并缓存；UI 侧读缓存，未探测完时返回上次已知值
    static void ProbeNodeAsync()
    {
        if (nodeProbing) return;
        nodeProbing = true;
        ThreadPool.QueueUserWorkItem(delegate
        {
            nodeCached = ProbeNode();
            nodeProbing = false;
            try { Log("环境自检：Node.js " + (nodeCached.Value ? "已安装" : "未安装")); } catch { }
        });
    }

    static bool HasNode()
    {
        if (nodeCached.HasValue) return nodeCached.Value;
        ProbeNodeAsync();
        return true;   // 未探测完时不做否定判断，避免误报"没装 Node"
    }

    static string Health()
    {
        try
        {
            HttpWebRequest rq = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:3000/healthz");
            rq.Timeout = 1200;
            using (WebResponse rp = rq.GetResponse())
            using (StreamReader sr = new StreamReader(rp.GetResponseStream()))
                return sr.ReadToEnd();
        }
        catch { return null; }
    }

    static void KillTree(Process p)
    {
        try
        {
            if (p == null || p.HasExited) return;
            Process.Start(new ProcessStartInfo("taskkill", "/PID " + p.Id + " /T /F") { CreateNoWindow = true, UseShellExecute = false });
            p.WaitForExit(2000);
        }
        catch { }
    }

    // winget 在不在（Win10 1809 之前 / LTSC / 精简版都没有）。找不到时 winget 安装必然秒失败，
    // 而 RunHidden 的 catch{} 会把异常吞掉 → 日志只剩"未成功"，用户和排查的人都看不出原因
    static bool WingetAvailable()
    {
        try
        {
            ProcessStartInfo si = new ProcessStartInfo("where", "winget");
            si.UseShellExecute = false; si.CreateNoWindow = true;
            si.RedirectStandardOutput = true; si.RedirectStandardError = true;
            using (Process p = Process.Start(si))
            {
                string o = p.StandardOutput.ReadToEnd();   // 先读完再等退出，否则可能死锁
                p.WaitForExit(4000);
                return p.ExitCode == 0 && o.Trim().Length > 0;
            }
        }
        catch { return false; }
    }

    static void RunHidden(string f, string a, bool wait) { RunHidden(f, a, wait, false); }

    // show=true 时把子进程自己的窗口显示出来（例如 winget 的下载进度），让用户看到"在装，别关"
    static void RunHidden(string f, string a, bool wait, bool show)
    {
        try
        {
            ProcessStartInfo si = new ProcessStartInfo(f, a);
            if (show)
            {
                si.UseShellExecute = true;          // 显示子进程窗口必须用 ShellExecute
                si.CreateNoWindow = false;
                si.WindowStyle = ProcessWindowStyle.Normal;
            }
            else
            {
                si.UseShellExecute = false;
                si.CreateNoWindow = true;
                si.WindowStyle = ProcessWindowStyle.Hidden;
            }
            // 不重定向输出：一旦重定向却不读取，管道写满(约 4KB)后子进程会永久阻塞
            if (!show) { si.RedirectStandardOutput = false; si.RedirectStandardError = false; }
            using (Process p = Process.Start(si))
            {
                if (wait) p.WaitForExit(300000);
            }
        }
        catch { }
    }

    static void CopyText(string s)
    {
        // ⚠ 必须在 UI 线程(STA)里复制：后台线程调 Clipboard.SetText 会抛 ThreadStateException，
        //   原来的 5 次重试会把异常全吞掉 —— 界面显示"已复制"，剪贴板里其实还是老内容。
        if (form != null && form.IsHandleCreated && form.InvokeRequired)
        {
            try { form.BeginInvoke((Action)delegate { CopyText(s); }); return; } catch { }
        }
        for (int i = 0; i < 5; i++) { try { Clipboard.SetText(s); return; } catch { Thread.Sleep(80); } }
        Log("⚠ 复制到剪贴板失败，请手动选中文字复制");
    }

    static bool IsHost { get { return seats[2] == "1"; } }
    static string Vip { get { return "10.126.126." + seats[2]; } }
    static string HostLink() { return "http://127.0.0.1:3000"; }

    // ================= 快捷方式 / 安装 =================
    static void MakeShortcut(string lnkPath, string exePath, string args, string desc, string workDir)
    {
        try
        {
            Type t = Type.GetTypeFromProgID("WScript.Shell");
            object shell = Activator.CreateInstance(t);
            object sc = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnkPath });
            Type st = sc.GetType();
            st.InvokeMember("TargetPath", BindingFlags.SetProperty, null, sc, new object[] { exePath });
            st.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, sc, new object[] { workDir });
            if (args.Length > 0) st.InvokeMember("Arguments", BindingFlags.SetProperty, null, sc, new object[] { args });
            st.InvokeMember("Description", BindingFlags.SetProperty, null, sc, new object[] { desc });
            st.InvokeMember("IconLocation", BindingFlags.SetProperty, null, sc, new object[] { exePath + ",0" });
            st.InvokeMember("Save", BindingFlags.InvokeMethod, null, sc, null);
        }
        catch (Exception ex) { Log("建快捷方式失败：" + ex.Message); }
    }

    static string DesktopDir() { return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory); }
    static string StartMenuDir() { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "卫戍协议"); }

    static bool IsInstalled()
    {
        return string.Equals(Path.GetFullPath(AppDir).TrimEnd('\\'),
            Path.GetFullPath(DefaultInstall).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
    }

    static void CreateShortcuts(bool quiet)
    {
        string exe = Application.ExecutablePath;
        MakeShortcut(Path.Combine(DesktopDir(), AppName + ".lnk"), exe, "", GameName + " 联机启动器", AppDir);
        string sm = StartMenuDir();
        try { if (!Directory.Exists(sm)) Directory.CreateDirectory(sm); } catch { }
        MakeShortcut(Path.Combine(sm, AppName + ".lnk"), exe, "", GameName + " 联机启动器", AppDir);
        MakeShortcut(Path.Combine(sm, "卸载 " + AppName + ".lnk"), exe, "-uninstall", "卸载", AppDir);
        Log("已创建桌面与开始菜单快捷方式");
        if (!quiet) MessageBox.Show("已创建：\n· 桌面快捷方式\n· 开始菜单（含卸载）", "完成");
    }

    static void SetBoot(bool on) { SetBoot(on, Application.ExecutablePath); }

    static void SetBoot(bool on, string exe)
    {
        try
        {
            string lnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), AppName + ".lnk");
            if (on) MakeShortcut(lnk, exe, "", "开机自启", Path.GetDirectoryName(exe));
            else if (File.Exists(lnk)) File.Delete(lnk);
            Log(on ? "已设置开机自启（后台驻留，随时能开房）" : "已取消开机自启");
        }
        catch (Exception ex) { Log("设置自启失败：" + ex.Message); }
    }

    static void UninstallFlow()
    {
        if (MessageBox.Show(
            "确定卸载 " + AppName + " 吗？\n\n" +
            "· 删除本程序文件夹、桌面与开始菜单快捷方式、开机自启\n" +
            "· 不会动游戏本体（" + GameFolderName + "）\n\n" +
            "当前运行位置：" + AppDir,
            "卸载确认", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

        try { File.Delete(Path.Combine(DesktopDir(), AppName + ".lnk")); } catch { }
        try { Directory.Delete(StartMenuDir(), true); } catch { }
        SetBoot(false);

        string me = Path.GetFullPath(AppDir);
        if (IsDevFolder())
        {
            MessageBox.Show("这是源码/开发目录（旁边有 Launcher.cs），为避免误删源码，只清理了快捷方式，没有删除任何文件。\n\n如果确实要删，请手动删除：" + me, "已跳过删除");
            Application.Exit(); return;
        }
        if (me.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show("本程序在临时目录里运行，为避免误删，文件未删除（快捷方式已清理）。", "提示");
            Application.Exit(); return;
        }
        try
        {
            string helper = Path.Combine(AppDir, "_uninstall.cmd");
            File.WriteAllText(helper,
                "@echo off\r\nping 127.0.0.1 -n 3 >nul\r\ncd /d \"%TEMP%\"\r\nrmdir /s /q \"" + me + "\"\r\ndel \"%~f0\"\r\n",
                Encoding.Default);
            Process.Start(new ProcessStartInfo(helper) { WindowStyle = ProcessWindowStyle.Hidden, UseShellExecute = true });
        }
        catch { }
        Application.Exit();
    }

    static void InstallFlow()
    {
        using (Form d = new Form())
        {
            d.Text = "安装 " + AppName;
            d.ClientSize = new Size(Sc(540), Sc(330));   // ★ 子控件都是 Sc() 过的，窗口尺寸也必须乘同一个系数（否则高 DPI 下内容溢出边框）
            if (cliDlgCheck != null) CheckDialogLater(d, "安装");
            d.StartPosition = FormStartPosition.CenterScreen;
            d.FormBorderStyle = FormBorderStyle.FixedDialog;
            d.MaximizeBox = false; d.MinimizeBox = false;
            d.BackColor = C_BG; d.ForeColor = C_TEXT;
            d.Font = Fui(9.5f);

            Label t = new Label();
            t.Text = "安装 " + AppName;
            t.Font = Fui(13f, FontStyle.Bold);
            t.ForeColor = C_MINT; t.SetBounds(Sc(22), Sc(18), Sc(480), Sc(30));
            d.Controls.Add(t);

            Label sub = new Label();
            sub.Text = "安装到固定位置，并创建快捷方式，之后从开始菜单就能打开";
            sub.ForeColor = C_DIM; sub.SetBounds(Sc(22), Sc(50), Sc(490), Sc(20));
            d.Controls.Add(sub);

            Label cl = new Label(); cl.Text = "当前位置"; cl.SetBounds(Sc(22), Sc(86), Sc(200), Sc(20)); cl.ForeColor = C_DIM; d.Controls.Add(cl);
            Label cur = new Label(); cur.Text = AppDir; cur.SetBounds(Sc(22), Sc(106), Sc(496), Sc(20)); cur.ForeColor = C_TEXT; d.Controls.Add(cur);
            Label tl = new Label(); tl.Text = "安装到"; tl.SetBounds(Sc(22), Sc(138), Sc(200), Sc(20)); tl.ForeColor = C_DIM; d.Controls.Add(tl);

            TextBox path = new TextBox();
            path.Text = DefaultInstall; path.SetBounds(Sc(22), Sc(158), Sc(396), Sc(26));
            path.BorderStyle = BorderStyle.FixedSingle; path.BackColor = C_BOX; path.ForeColor = Color.White;
            d.Controls.Add(path);

            Button pick = new Button();
            pick.Text = "浏览…"; pick.SetBounds(Sc(426), Sc(158), Sc(92), Sc(26));
            pick.FlatStyle = FlatStyle.Flat; pick.FlatAppearance.BorderColor = C_EDGE; pick.ForeColor = Color.White;
            pick.Click += delegate
            {
                FolderBrowserDialog fb = new FolderBrowserDialog();
                fb.Description = "选择安装位置";
                if (fb.ShowDialog() == DialogResult.OK) path.Text = Path.Combine(fb.SelectedPath, AppName);
            };
            d.Controls.Add(pick);

            CheckBox desk = new CheckBox(); desk.Text = "桌面快捷方式"; desk.Checked = true;
            desk.SetBounds(Sc(22), Sc(198), Sc(180), Sc(24)); desk.ForeColor = C_TEXT; d.Controls.Add(desk);
            CheckBox menu = new CheckBox(); menu.Text = "开始菜单项"; menu.Checked = true;
            menu.SetBounds(Sc(210), Sc(198), Sc(180), Sc(24)); menu.ForeColor = C_TEXT; d.Controls.Add(menu);
            CheckBox boot = new CheckBox(); boot.Text = "开机自动启动"; boot.Checked = false;
            boot.SetBounds(Sc(22), Sc(226), Sc(300), Sc(24)); boot.ForeColor = C_TEXT; d.Controls.Add(boot);

            Button ok = new Button();
            ok.Text = "开始安装"; ok.SetBounds(Sc(286), Sc(272), Sc(112), Sc(34));
            ok.FlatStyle = FlatStyle.Flat; ok.BackColor = C_ON; ok.ForeColor = Color.White;
            ok.DialogResult = DialogResult.OK; d.Controls.Add(ok);
            Button no = new Button();
            no.Text = "取消"; no.SetBounds(Sc(408), Sc(272), Sc(110), Sc(34));
            no.FlatStyle = FlatStyle.Flat; no.ForeColor = Color.White; no.DialogResult = DialogResult.Cancel;
            d.Controls.Add(no);

            if (d.ShowDialog() != DialogResult.OK) return;
            string target = path.Text.Trim();
            bool dsh = desk.Checked, mnu = menu.Checked, btset = boot.Checked;

            if (!Directory.Exists(target))
            {
                try { Directory.CreateDirectory(target); }
                catch (Exception ex) { MessageBox.Show("创建目录失败：" + ex.Message, "安装失败"); return; }
            }
            if (string.Equals(Path.GetFullPath(target).TrimEnd('\\'), Path.GetFullPath(AppDir).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            {
                if (dsh || mnu) CreateShortcuts(true);
                if (btset) SetBoot(true);
                MessageBox.Show("本程序已经在这个位置了，快捷方式已更新。", "完成");
                return;
            }
            try
            {
                string o2pSrc = Path.Combine(AppDir, "openp2p\\openp2p.exe");
                if (File.Exists(o2pSrc))
                {
                    Directory.CreateDirectory(Path.Combine(target, "openp2p"));
                    File.Copy(o2pSrc, Path.Combine(target, "openp2p\\openp2p.exe"), true);   // 只带引擎，不带 config.json 与日志，避免节点名撞车
                }
                // ★ 包里自带的便携版 Node 必须一起搬走（2026-10-04 台式机实测踩的坑）：
                //   以前只搬 exe + openp2p，装完 AppDir 里没有 node\ → 开房时又报"没检测到 Node.js"，
                //   而 winget 在很多机器上根本没有 → 自动安装秒失败，用户看到的就是"装了启动器却开不了房"。
                foreach (string relN in new string[] { "node\\node.exe", "nodejs\\node.exe", "node.exe" })
                {
                    string nSrc = Path.Combine(AppDir, relN);
                    if (!File.Exists(nSrc)) continue;
                    string nDst = Path.Combine(target, relN);
                    string nDir = Path.GetDirectoryName(nDst);
                    if (!Directory.Exists(nDir)) Directory.CreateDirectory(nDir);
                    File.Copy(nSrc, nDst, true);
                    Log("安装时一并搬运便携版 Node：" + relN);
                    break;
                }
                File.Copy(Application.ExecutablePath, Path.Combine(target, Path.GetFileName(Application.ExecutablePath)), true);
                List<string> nc = new List<string>();
                nc.Add(seats[0]); nc.Add(seats[1]); nc.Add(seats[2]); nc.Add(gamePath);
                nc.Add("1"); nc.Add(btset ? "1" : "0");
                nc.Add("o2pnode=" + MyO2PNode());                       // 换位置后节点名不变，之前发出去的邀请才还有效
                if (Token().Length > 0) nc.Add("o2ptoken=" + Token());   // 同一台机器同一用户，令牌跟着走，不用重填
                File.WriteAllLines(Path.Combine(target, "启动器配置.txt"), nc.ToArray(), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                MessageBox.Show("复制文件失败：" + ex.Message + "\n\n可以手动把整个文件夹拷过去再运行。", "安装失败");
                return;
            }

            string newexe = Path.Combine(target, Path.GetFileName(Application.ExecutablePath));
            if (dsh) MakeShortcut(Path.Combine(DesktopDir(), AppName + ".lnk"), newexe, "", GameName + " 联机启动器", target);
            if (mnu)
            {
                string sm = StartMenuDir();
                try { if (!Directory.Exists(sm)) Directory.CreateDirectory(sm); } catch { }
                MakeShortcut(Path.Combine(sm, AppName + ".lnk"), newexe, "", GameName + " 联机启动器", target);
                MakeShortcut(Path.Combine(sm, "卸载 " + AppName + ".lnk"), newexe, "-uninstall", "卸载", target);
            }
            if (btset) SetBoot(true, newexe);

            Process.Start(newexe);
            MessageBox.Show("安装完成！\n\n位置：" + target +
                (dsh ? "\n· 桌面快捷方式 ✓" : "") + (mnu ? "\n· 开始菜单项 ✓" : "") +
                (btset ? "\n· 开机自启 ✓" : "") + "\n\n原位置可以删掉了。", "完成");
            Application.Exit();
        }
    }

    // ================= 开房 / 加入 / 单机 =================
    // 放行 3000 端口。注意：netsh 加规则需要管理员权限，失败时绝不能写标记，
    // 否则以后每次都会跳过，规则就永远加不上（本项目实际踩过：.fw 在但规则不存在）
    static void Firewall()
    {
        try
        {
            string mark = Path.Combine(AppDir, ".fw");
            if (File.Exists(mark) && FirewallRuleExists()) return;
            if (FirewallRuleExists()) { try { File.WriteAllText(mark, "1"); } catch { } return; }
            RunHidden("netsh", "advfirewall firewall add rule name=\"卫戍协议启动器 3000\" dir=in action=allow protocol=TCP localport=3000", true);
            if (FirewallRuleExists())
            {
                try { File.WriteAllText(mark, "1"); } catch { }
                Log("已放行 3000 端口（防火墙规则已添加）");
            }
            else
            {
                Log("⚠ 未能自动放行 3000 端口（需要管理员权限）。首次开房时请在弹出的防火墙窗口里勾选「专用网络」并允许。");
            }
        }
        catch { }
    }

    // 规则是否真的存在（只有它说存在，才认为放行成功）
    static bool FirewallRuleExists()
    {
        try
        {
            ProcessStartInfo si = new ProcessStartInfo("netsh", "advfirewall firewall show rule name=\"卫戍协议启动器 3000\"");
            si.UseShellExecute = false; si.CreateNoWindow = true;
            si.RedirectStandardOutput = true; si.RedirectStandardError = true;
            using (Process p = Process.Start(si))
            {
                string o = p.StandardOutput.ReadToEnd();
                p.WaitForExit(3000);
                return o.IndexOf("3000") >= 0 && o.IndexOf("卫戍协议启动器") >= 0;
            }
        }
        catch { return false; }
    }
    // ===== openp2p 隧道（替代 EasyTier：官方公共节点已挂，openp2p 可用且无需管理员）=====
    static Process pO2P;
    static string joinPeerNode = "";   // 加入方：房主的 openp2p 节点名    // ★ 本版起不再在源码里硬编码 openp2p 令牌（公开后会被人反编译蹭网）。
    //   令牌来源顺序：启动器配置.txt 的 o2ptoken= 行 → 本机 OPL 的 bin\config.json → 弹框让用户粘一次。
    //   房主开房时会把令牌写进邀请内容（o2p=…），粥友粘贴邀请即自动保存，不用手填。
    static string tokenCache = null;

    static string O2PExe() { return Path.Combine(AppDir, "openp2p\\openp2p.exe"); }

    // 令牌只看数字/字母，短于 8 位视为无效（防止把空格、说明文字粘进来）
    static string TokenNorm(string s)
    {
        if (s == null) return "";
        StringBuilder sb = new StringBuilder();
        foreach (char c in s.Trim())
            if ((c >= '0' && c <= '9') || (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')) sb.Append(c);
        string v = sb.ToString();
        return v.Length >= 8 ? v : "";
    }
    static string TokenMask()
    {
        string t = Token();
        if (t.Length == 0) return "未配置";
        return "已配置（尾号 " + (t.Length > 4 ? t.Substring(t.Length - 4) : t) + "）";
    }

    // 令牌落盘：保留配置里其它行（房间名/密码/编号/游戏路径/o2pnode），只增改 o2ptoken= 这一行
    static void SaveToken(string t)
    {
        t = TokenNorm(t);
        if (t.Length == 0) return;
        try
        {
            List<string> keep = new List<string>();
            if (File.Exists(CfgFile))
            {
                foreach (string s in File.ReadAllLines(CfgFile, Encoding.UTF8))
                    if (s.Trim().Length > 0 && !s.TrimStart().StartsWith("o2ptoken=")) keep.Add(s);
            }
            keep.Add("o2ptoken=" + t);
            File.WriteAllLines(CfgFile, keep.ToArray(), Encoding.UTF8);
            tokenCache = t;
            Log("已保存联机令牌（尾号 " + (t.Length > 4 ? t.Substring(t.Length - 4) : t) + "）");
        }
        catch (Exception ex) { Log("保存令牌失败：" + ex.Message); }
    }

    // 借用本机 OPL（怪猎启动器）里已经配好的 openp2p 令牌，粥友多半都装了，省得手填
    static string TokenFromOpl()
    {
        try
        {
            string[] cand = new string[] {
                @"C:\Program Files (x86)\Guailoudou\OPL\bin\config.json",
                @"C:\Program Files\Guailoudou\OPL\bin\config.json",
                @"C:\Program Files (x86)\Guailoudou\OPL\config.json",
                @"C:\Program Files\Guailoudou\OPL\config.json"
            };
            foreach (string c in cand)
            {
                if (!File.Exists(c)) continue;
                string txt = File.ReadAllText(c, Encoding.UTF8);
                System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(txt, "\"Token\"\\s*:\\s*\"?([0-9A-Za-z]{8,})\"?");
                if (m.Success) return m.Groups[1].Value;
            }
        }
        catch { }
        return "";
    }

    // 当前联机令牌（空字符串 = 还没配置，调用方负责弹框问用户）
    static string Token()
    {
        if (tokenCache != null && tokenCache.Length > 0)
        {
            // 兜底：别的地方（老版本启动器、手动清配置）把文件改掉时，把令牌再写回去，省得再弹框问一次
            try
            {
                if (!File.Exists(CfgFile) || File.ReadAllText(CfgFile, Encoding.UTF8).IndexOf("o2ptoken=") < 0) SaveToken(tokenCache);
            }
            catch { }
            return tokenCache;
        }
        if (tokenCache != null) return tokenCache;
        try
        {
            if (File.Exists(CfgFile))
            {
                foreach (string s in File.ReadAllLines(CfgFile, Encoding.UTF8))
                {
                    string x = s.Trim();
                    if (x.StartsWith("o2ptoken="))
                    {
                        string v = TokenNorm(x.Substring(9));
                        if (v.Length > 0) { tokenCache = v; return tokenCache; }
                    }
                }
            }
        }
        catch { }
        string o = TokenFromOpl();
        if (o.Length > 0) { SaveToken(o); Log("已从本机 OPL 读取到 openp2p 令牌并保存"); return tokenCache; }
        tokenCache = "";
        return tokenCache;
    }

    // 联机令牌状态刷新：设置页那行字 + 首页「组网引擎就绪」那句 + 左侧提示
    static void RefreshTokenUi()
    {
        if (setTokenLabel != null)
        {
            if (Token().Length > 0) { setTokenLabel.Text = "✓ 联机令牌：" + TokenMask(); setTokenLabel.ForeColor = C_TEXT; }
            else { setTokenLabel.Text = "✗ 联机令牌未配置（首次联机时会让你填一次）"; setTokenLabel.ForeColor = Color.FromArgb(240, 170, 90); }
        }
        if (heroStateLbl != null)
        {
            if (!File.Exists(O2PExe())) { heroStateLbl.Text = "⚠ 缺少 openp2p\\openp2p.exe"; heroStateLbl.ForeColor = Color.FromArgb(240, 170, 90); }
            else if (Token().Length == 0) { heroStateLbl.Text = "⚠ 组网引擎就绪，还缺联机令牌"; heroStateLbl.ForeColor = Color.FromArgb(240, 170, 90); }
            else { heroStateLbl.Text = "✓ 组网就绪（引擎 + 令牌）"; heroStateLbl.ForeColor = C_MINT; }
        }
        if (sideTipLbl != null) sideTipLbl.Text = SideTip();
    }
    static string SideTip()
    {
        return "房主负责开房发邀请，\n粥友装上启动器就能加入。\n" +
            (Token().Length > 0 ? "令牌就绪，粘贴邀请即可。\n" : "联机令牌还没填，\n首次联机会让你填一次。\n") +
            "\n游戏本体与启动器都是\n粥友之间自用，禁止盈利。";
    }

    // 弹一次输入框问联机令牌（返回 "" 表示用户放弃）。令牌不是密码，明文显示更方便核对粘贴
    static string AskToken(string why)
    {
        using (Form d = new Form())
        {
            int w = Sc(540), h = Sc(292);
            d.Text = "填一次联机令牌";
            d.ClientSize = new Size(w, h);
            if (cliDlgCheck != null) CheckDialogLater(d, "令牌");
            d.StartPosition = FormStartPosition.CenterParent;
            d.FormBorderStyle = FormBorderStyle.FixedDialog;
            d.MaximizeBox = false; d.MinimizeBox = false;
            d.BackColor = C_BG; d.ForeColor = C_TEXT;
            d.Font = Fui(9.5f);

            Label t = new Label();
            t.Text = "联机令牌（openp2p）";
            t.Font = FTitle(12f, FontStyle.Bold);
            t.ForeColor = C_MINT; t.AutoSize = true; t.Location = new Point(Sc(24), Sc(20));
            d.Controls.Add(t);

            Label info = new Label();
            info.Text = why;
            info.Font = Fui(9.5f); info.ForeColor = C_DIM;
            info.SetBounds(Sc(24), Sc(52), w - Sc(48), Sc(120));
            d.Controls.Add(info);

            TextBox tb = new TextBox();
            tb.SetBounds(Sc(24), Sc(180), w - Sc(48), Sc(26));
            tb.BorderStyle = BorderStyle.FixedSingle;
            tb.BackColor = C_BOX; tb.ForeColor = Color.White;
            tb.Font = Fui(10.5f);
            d.Controls.Add(tb);

            Button ok = B(d, "确定并保存", 24, 222, 150, 36, C_ON, null);
            ok.DialogResult = DialogResult.OK; ok.Font = Fui(10f, FontStyle.Bold);
            Button no = B(d, "以后再说", 188, 222, 110, 36, C_OFF, null);
            no.DialogResult = DialogResult.Cancel;
            d.AcceptButton = ok; d.CancelButton = no;

            string got = "";
            ok.Click += delegate { got = TokenNorm(tb.Text); };
            if (d.ShowDialog() != DialogResult.OK) return "";
            if (got.Length == 0) { MessageBox.Show("令牌里至少要有 8 位数字或字母。若从群里复制，请检查是不是只复制了一半。", "令牌看着不对"); return ""; }
            return got;
        }
    }

    // 我的 openp2p 节点名（8-31 字符，生成一次后持久化到配置）
    static string MyO2PNode()
    {
        try
        {
            string[] l = File.Exists(CfgFile) ? File.ReadAllLines(CfgFile, Encoding.UTF8) : new string[0];
            foreach (string s in l) if (s.StartsWith("o2pnode=") && s.Length > 8) return s.Substring(8).Trim();
            string n = "sp" + randStr(8, "abcdefghijkmnpqrstuvwxyz23456789");
            File.AppendAllText(CfgFile, "o2pnode=" + n + "\r\n", Encoding.UTF8);
            return n;
        }
        catch { return "sp" + randStr(8, "abcdefghijkmnpqrstuvwxyz23456789"); }
    }

    // 启动 openp2p：host=true 时本机作为被连接方（跑游戏服务器那一端）
    static void StartO2P(bool host, string peerNode)
    {
        try
        {
            if (!File.Exists(O2PExe())) { Log("找不到 openp2p 引擎：" + O2PExe()); return; }
            List<string> a = new List<string>();
            a.Add("-d");                                  // 守护模式：worker 挂了自动重启
            a.Add("-loglevel"); a.Add("1");
            a.Add("-node"); a.Add(MyO2PNode());
            string tk = Token();
            if (tk.Length == 0) { Log("还没有联机令牌，隧道不启动（调用方应先弹框让用户填）"); return; }
            a.Add("-token"); a.Add(tk);
            if (host) { a.Add("-sharebandwidth"); a.Add("20"); }   // 注意：Go flag 必须用空格，不能用冒号
            if (!host && peerNode != null && peerNode.Length > 0)
            {
                a.Add("-appname"); a.Add("wsxy");
                a.Add("-peernode"); a.Add(peerNode);   // ⚠ 必须全小写：openp2p 是 Go flag，大小写敏感（v1.2.3 写成 -peerNode，进房侧隧道打印 Usage 后直接退出）
                a.Add("-dstport"); a.Add("3000");
                a.Add("-srcport"); a.Add("3000");
                a.Add("-protocol"); a.Add("tcp");
            }
            ProcessStartInfo si = new ProcessStartInfo(O2PExe(), string.Join(" ", a.ToArray()));
            si.WorkingDirectory = AppDir;   // 日志写在启动器目录下的 log\
            si.UseShellExecute = false; si.CreateNoWindow = true;
            si.RedirectStandardOutput = true; si.RedirectStandardError = true;
            // ★ 2026-10-04 修：openp2p 是 Go 程序、输出 UTF-8，而 .NET 的 RedirectStandardOutput
            //   默认按系统 ANSI(GBK) 解码 → 日志里的中文路径全成 `D:\鍗垗鍗忚\...` 这种乱码。
            si.StandardOutputEncoding = Encoding.UTF8; si.StandardErrorEncoding = Encoding.UTF8;
            pO2P = Process.Start(si);
            pO2P.OutputDataReceived += delegate(object s, DataReceivedEventArgs e) { if (e.Data != null && e.Data.Trim().Length > 0) { Log("[隧道] " + e.Data.Trim()); LogEt(e.Data.Trim()); } };
            pO2P.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e) { if (e.Data != null && e.Data.Trim().Length > 0) { Log("[隧道] " + e.Data.Trim()); LogEt(e.Data.Trim()); } };
            pO2P.BeginOutputReadLine(); pO2P.BeginErrorReadLine();
            TunnelBegin(host);   // 状态栏进入「握手中」，并记下日志基线
            Log("隧道启动中（节点名 " + MyO2PNode() + (host ? "，本机为房主" : "，连接 " + peerNode) + "）");
        }
        catch (Exception ex) { Log("隧道启动失败：" + ex.Message); }
    }
    static void StartEt()
    {
        try
        {
            string args = "--network-name \"" + seats[0] + "\" --network-secret \"" + seats[1] +
                          "\" -i " + Vip + " -p " + PublicPeer + " -p udp://public.easytier.cn:11010";
            ProcessStartInfo si = new ProcessStartInfo(EtCore(), args);
            si.WorkingDirectory = EtHome();
            si.UseShellExecute = false; si.CreateNoWindow = true;
            si.RedirectStandardOutput = true; si.RedirectStandardError = true;
            si.StandardOutputEncoding = Encoding.UTF8; si.StandardErrorEncoding = Encoding.UTF8;   // EasyTier 是 Rust 程序，输出 UTF-8
            pEt = Process.Start(si);
            pEt.OutputDataReceived += delegate(object s, DataReceivedEventArgs e) { if (e.Data != null && e.Data.Trim().Length > 0) { Log("[组网] " + e.Data.Trim()); LogEt(e.Data.Trim()); } };
            pEt.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e) { if (e.Data != null && e.Data.Trim().Length > 0) { Log("[组网] " + e.Data.Trim()); LogEt(e.Data.Trim()); } };
            pEt.BeginOutputReadLine(); pEt.BeginErrorReadLine();
            Log("虚拟网连接中（首次约 10 秒）");
        }
        catch (Exception ex) { Log("组网启动失败：" + ex.Message); }
    }

    static bool StartServer()
    {
        string g = FindGame(false);
        if (srvUp || CachedHealth()) { Log("3000 端口已有服务在跑，直接复用"); return true; }
        if (g == null) { Log("没有游戏本体，无法起服"); return false; }
        try
        {
            ProcessStartInfo si = new ProcessStartInfo(NodeExe(), "server\\index.js");
            si.WorkingDirectory = g;
            si.UseShellExecute = false; si.CreateNoWindow = true;
            si.RedirectStandardOutput = true; si.RedirectStandardError = true;
            // ★ 同上：node 的输出也是 UTF-8，不设这个 logs\游戏服务.log 就是乱码
            si.StandardOutputEncoding = Encoding.UTF8; si.StandardErrorEncoding = Encoding.UTF8;
            pNode = Process.Start(si);
            try
            {
                StreamWriter sw = new StreamWriter(Path.Combine(LogDirRoot(), "游戏服务.log"), false, new UTF8Encoding(false));
                pNode.OutputDataReceived += delegate(object s, DataReceivedEventArgs e) { if (e.Data != null) { lock (sw) sw.WriteLine(e.Data); } };
                pNode.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e) { if (e.Data != null) { lock (sw) sw.WriteLine(e.Data); } };
                pNode.BeginOutputReadLine(); pNode.BeginErrorReadLine();
            }
            catch { }
            Log("游戏服务器启动中…");
        }
        catch (Exception ex) { Log("启动失败：" + ex.Message); return false; }
        for (int i = 0; i < 60; i++) { if (srvUp || CachedHealth()) return true; Thread.Sleep(500); }
        return srvUp;
    }

    static void GoMultiplayer(bool host)
    {
        if (busy) return;
        seats[0] = roomBox.Text.Trim().Length == 0 ? DefaultRoomName : roomBox.Text.Trim();
        seats[1] = passBox.Text.Trim();
        if (host) seats[2] = "1"; else seats[2] = seatBox.Text.Trim();
        if (seats[1].Length == 0) { MessageBox.Show("请填房间密码。", "缺少密码"); return; }
        int n;
        if (!int.TryParse(seats[2], out n) || n < 1 || n > 254) { MessageBox.Show("编号填 1-254：房主是 1，粥友用 2、3、4。", "编号不对"); return; }
        if (!File.Exists(O2PExe())) { MessageBox.Show("缺少组网引擎 openp2p\\openp2p.exe", "缺少文件"); return; }
        // 令牌是联机的前置条件：同一个令牌下的节点才能 P2P 直连。没有就问一次（房主/粥友都一样）
        if (Token().Length == 0)
        {
            string tk = AskToken("第一次联机需要填一次 openp2p 令牌。\n\n" +
                "· 令牌是「组网账号」，同一个令牌下的节点才能互相 P2P 直连，所以房主和所有粥友要填同一个。\n" +
                "· 房主点「开房并复制邀请」时会把令牌写进邀请里，粥友把整段邀请粘进来就自动填好了，一般不用手填。\n" +
                "· 房主要自己开房又没人给令牌：去 https://www.openp2p.cn 注册登录，在控制台复制自己的令牌。");
            if (tk.Length == 0) { FlashStatus("○ 没填令牌，联机没启动", Color.FromArgb(240, 170, 90)); return; }
            SaveToken(tk);
            RefreshTokenUi();
        }
        if (host && EnsureGame(true) == null) return;
        if (host && !EnsureNode()) return;
        SaveCfg();
        busy = true; startBtn.Enabled = false; StatusBusy(host ? "正在组网并启动服务器…" : "正在加入房间…");
        ThreadPool.QueueUserWorkItem(delegate
        {
          try
          {
            bool h = host;
            Log("模式：" + (h ? "我开房（房主，编号 1）" : "加入房间（编号 " + seats[2] + "）"));
            Log("房间 " + seats[0] + " · 我的隧道节点 " + MyO2PNode());
            if (h) Ui(delegate { Step(1, "正在建立隧道（openp2p）…"); });
            StartO2P(h, h ? null : joinPeerNode);
            if (h) { Ui(delegate { Step(2, "正在启动游戏服务器…"); }); if (StartServer()) Log("游戏服务已就绪"); else Log("游戏服务没起来，看 logs\\游戏服务.log"); }
            else Thread.Sleep(2500);
            Ui(delegate
            {
                busy = false;
                startBtn.Enabled = true;
                startBtn.Text = "关闭房间";   // 记录"房间已开"状态，界面由 ApplyModeUi 统一呈现
                openBtn.Enabled = true;
                shareBtn.Enabled = true;
                ApplyModeUi(h);
                Step(3, h ? "开好了，邀请已复制" : "已加入，正在建隧道…");
                FlashStatus(h ? "● 房间已开好，邀请已复制" : "● 已加入，隧道连上就自动开游戏页", C_MINT);
                if (h) { try { Process.Start("http://localhost:3000"); } catch { } }
            });
            if (h)   // ★ 2026-10-05 修：只有房主才该复制邀请 —— 加入方复制出来的是"指向他自己"的假邀请
            {
                CopyText(InviteText());   // 必须复制整段邀请：裸链接不带 peer=，粥友那边隧道建不起来
                Log("邀请已复制（含房主节点名 " + MyO2PNode() + "）");
            }
          }
          catch (Exception ex)
          {
            Log("出错：" + ex.Message);
            busy = false;
            Ui(delegate { PulseAlert(statusLine, "○ 出错，看 logs\\启动器.log", Color.FromArgb(240, 140, 120)); });
          }
        });
    }

    static void StartSolo()
    {
        if (busy) return;
        if (EnsureGame(true) == null) return;
        if (!EnsureNode()) return;
        busy = true; StatusBusy("正在启动单机…");
        ThreadPool.QueueUserWorkItem(delegate
        {
          try
          {
            Log("单机模式：只起游戏服务，不组网");
            Ui(delegate { Step(2, "正在启动游戏服务器…"); });
            bool ok = StartServer();
            busy = false;
            if (ok)
            {
                Log("单机已启动 http://localhost:3000");
                try { Process.Start("http://localhost:3000"); } catch { }
                Ui(delegate { startBtn.Text = "关闭房间"; openBtn.Enabled = true; });
            }
            else Log("单机启动失败，见 logs\\server.log");
            Ui(delegate { if (ok) FlashStatus("● 单机已启动", C_MINT); else PulseAlert(statusLine, "○ 启动失败，看 logs\\游戏服务.log", Color.FromArgb(240, 140, 120)); });
          }
          catch (Exception ex)
          {
            busy = false;
            Log("单机出错：" + ex.Message);
            Ui(delegate { PulseAlert(statusLine, "○ 出错，看 logs\\启动器.log", Color.FromArgb(240, 140, 120)); });
          }
        });
    }

    static bool EnsureNode()
    {
        if (HasNode()) return true;
        if (MessageBox.Show(
            "这台电脑没有检测到 Node.js。\n\n· 只想加入别人的房间 → 不需要它。\n· 开房/单机 → 需要装一次（约 30 MB）。\n\n要现在自动安装吗？",
            "缺少 Node.js", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return false;
        Log("正在安装 Node.js…（界面会短暂无响应，属正常）");
        StatusBusy("正在安装 Node.js（约 30 MB），请勿关闭…");
        // ★ winget 在很多机器上不存在（Win10 1809 之前 / LTSC / 精简版）：那样自动安装必然秒失败，
        //   而 RunHidden 的空 catch 会把异常吞掉，日志只剩一句"未成功"——查不出原因。先说清楚。
        if (!WingetAvailable())
        {
            ClearBanner();
            Log("⚠ 这台电脑没有 winget（自动安装做不了）——请用带 node 的「开房包」，或手动装 Node.js");
            MessageBox.Show("这台电脑没有 winget，自动安装做不了。\n\n两条路任选：\n· 用「开房包」（里面自带 Node.js，解压就带，不用装）\n· 或手动装：https://nodejs.org/zh-cn/download\n装完重开启动器即可。", "提示");
            return false;
        }
        // 显示 winget 自己的进度窗口：让用户看到下载/安装在进行，不会以为卡死
        RunHidden("winget", "install --id OpenJS.NodeJS.LTS -e --accept-source-agreements --accept-package-agreements", true, true);
        if (HasNode())
        {
            Log("Node.js 安装完成");
            FlashStatus("● Node.js 安装完成", C_MINT);
            return true;
        }
        ClearBanner();
        Log("Node.js 自动安装未成功");
        MessageBox.Show("自动安装未成功。\n\n两条路任选：\n· 拿房主发的「开房包」（里面自带 Node.js，解压就带，不用装）\n· 或手动装：https://nodejs.org/zh-cn/download\n装完重开启动器即可。", "提示");
        return false;
    }

    // 强制结束游戏服务器：先杀自己启动的，再按 3000 端口占用者杀，最后兜底兜一次
    // 手动强制结束游戏进程的界面动作
    static int CountVisible(int i)
    {
        if (pageCache[i] == null) return -1;
        int n = 0; foreach (Control k in pageCache[i].Controls) if (k.Visible) n++;
        return n;
    }

    // 布局自检：把窗口/页面尺寸与最低控件写进 logs\布局诊断.txt
    static void DumpLayout()
    {
        try
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("时间=" + DateTime.Now.ToString("HH:mm:ss"));
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            Rectangle sc2 = Screen.PrimaryScreen.Bounds;
            sb.AppendLine("屏幕=" + sc2.Width + "x" + sc2.Height + "  可用区=" + wa.Width + "x" + wa.Height + "  可用区起点=" + wa.X + "," + wa.Y);
            sb.AppendLine("窗口位置=" + form.Left + "," + form.Top + "  窗口外框=" + form.Width + "x" + form.Height + "  客户区=" + form.ClientSize.Width + "x" + form.ClientSize.Height);
            sb.AppendLine("窗口底边=" + (form.Top + form.Height) + "  窗口右边=" + (form.Left + form.Width) + "  可用区底边=" + (wa.Y + wa.Height) + "  可用区右边=" + (wa.X + wa.Width));
            sb.AppendLine("DpiScale=" + DpiScale().ToString("0.00") + "  屏幕DPI=" + (DpiScale() * 96));
            sb.AppendLine(EnvLine());
            sb.AppendLine("内容区=" + content.Width + "x" + content.Height + "  视口高=" + content.ClientSize.Height
                + "  滚动=" + (content.AutoScroll ? "启用" : "关闭")
                + "  可滚最大=" + Math.Max(0, content.AutoScrollMinSize.Height - content.ClientSize.Height));
            sb.AppendLine("当前模式=" + (IsHost ? "我开房(房主)" : "加入房间") + "  当前页=" + page);
            sb.AppendLine("可见控件统计：页0=" + CountVisible(0) + " 页1=" + CountVisible(1) + " 页2=" + CountVisible(2));
            for (int i = 0; i < 3; i++)
            {
                if (pageCache[i] == null) { sb.AppendLine("页面" + i + "=空"); continue; }
                int lowest = 0; string who = "";
                foreach (Control k in pageCache[i].Controls)
                    if (k.Visible && k.Bottom > lowest) { lowest = k.Bottom; who = (k.Text ?? "").Replace("\r", " ").Replace("\n", " "); if (who.Length > 18) who = who.Substring(0, 18); }
                sb.AppendLine("页面" + i + "=" + pageCache[i].Width + "x" + pageCache[i].Height + "  最低可见控件底边=" + lowest + "  余量=" + (pageCache[i].Height - lowest) + "  需求高=" + PageNeedH(i) + "  [" + who + "]");
                foreach (Control k in pageCache[i].Controls) { string kt = (k.Text ?? "").Replace("\r", " ").Replace("\n", " "); if (kt.Length > 14) kt = kt.Substring(0, 14); sb.AppendLine("   页" + i + " [" + k.GetType().Name + "] 可见=" + k.Visible + " 位置=" + k.Left + "," + k.Top + " 尺寸=" + k.Width + "x" + k.Height + "  " + kt); }
            }
            // 重叠检测：同一页内任意两个控件的矩形相交即报警
            for (int i = 0; i < 3; i++)
            {
                Panel pg = pageCache[i];
                if (pg == null) continue;
                List<Control> cs = new List<Control>();
                foreach (Control c in pg.Controls) if (c.Visible) cs.Add(c);
                int bad = 0;
                for (int a = 0; a < cs.Count; a++)
                    for (int b = a + 1; b < cs.Count; b++)
                    {
                        Rectangle ra = cs[a].Bounds, rb = cs[b].Bounds;
                        if (!ra.IntersectsWith(rb)) continue;
                        Rectangle inter = Rectangle.Intersect(ra, rb);
                        if (inter.Width > 4 && inter.Height > 4)
                        {
                            bad++;
                            if (bad <= 6) sb.AppendLine("  ⚠ 重叠 页" + i + "：" + (cs[a].Text ?? "").Substring(0, Math.Min(14, (cs[a].Text ?? "").Length)) + " × " + (cs[b].Text ?? "").Substring(0, Math.Min(14, (cs[b].Text ?? "").Length)) + "  交叠 " + inter.Width + "x" + inter.Height);
                        }
                    }
                sb.AppendLine("页" + i + " 重叠数=" + bad);
            }
            File.WriteAllText(Path.Combine(LogDirRoot(), "布局诊断.txt"), sb.ToString(), Encoding.UTF8);
        }
        catch { }
    }
    static void KillServerUi()
    {
        busy = false;
        ClearBanner();
        Log("[结束] 点击了强制结束");
        StatusBusy("正在结束游戏进程…");
        StopGameServerAsync(delegate(bool did)
        {
            try
            {
                Log("[结束] 后台完成，did=" + did);
                busy = false;
                lastStatus = "";
                lastMembers = "";
                if (did) FlashStatus("● 游戏进程已结束", C_MINT);
                else PulseAlert(statusLine, "○ 没找到正在运行的游戏进程", Color.FromArgb(240, 200, 120));
                Log("[结束] 已给出提示，banner=[" + bannerText + "]");
            }
            catch (Exception ex)
            {
                Log("[结束] 提示阶段出错：" + ex.Message);
            }
        });
    }
    // 页面切换淡入：逐个控件从背景色过渡到自身颜色
    static void FadeIn(Control c)
    {
        if (c == null) return;
        Control[] kids = new Control[c.Controls.Count];
        c.Controls.CopyTo(kids, 0);
        int n = 0;
        System.Windows.Forms.Timer ft = new System.Windows.Forms.Timer();
        ft.Interval = 28;
        ft.Tick += delegate
        {
            n++;
            foreach (Control k in kids)
            {
                if (k == null || !k.IsHandleCreated) continue;
                try { k.ForeColor = n >= 8 ? k.ForeColor : Blend(k.ForeColor, C_BG, n / 8f); }
                catch { }
            }
            if (n >= 8) { ft.Stop(); ft.Dispose(); }
        };
        ft.Start();
    }

    // 把前景色向背景色插值，做出"从暗到亮"的感觉
    static Color Blend(Color fg, Color bg, float t)
    {
        if (t <= 0) return bg;
        if (t > 1) t = 1;
        return Color.FromArgb(fg.A,
            (int)(bg.R + (fg.R - bg.R) * t),
            (int)(bg.G + (fg.G - bg.G) * t),
            (int)(bg.B + (fg.B - bg.B) * t));
    }

    // 开房进度提示（三步）
    static void Step(int i, string what)
    {
        StatusBusy("[" + i + "/3] " + what);
    }
    // 用 Windows API 直接查"监听指定端口的进程 PID"：不启动任何子进程，毫秒级返回
    [System.Runtime.InteropServices.DllImport("iphlpapi.dll", SetLastError = true)]
    static extern uint GetExtendedTcpTable(IntPtr pTcpTable, ref int pdwSize, bool bOrder, int ulAf, int tableClass, int reserved);

    static List<int> PidsListeningOn(int port)
    {
        List<int> res = new List<int>();
        int size = 0;
        try
        {
            GetExtendedTcpTable(IntPtr.Zero, ref size, false, 2 /*AF_INET*/, 5 /*TCP_TABLE_OWNER_PID_LISTENER*/, 0);
            if (size <= 0) return res;
            IntPtr buf = System.Runtime.InteropServices.Marshal.AllocHGlobal(size);
            try
            {
                if (GetExtendedTcpTable(buf, ref size, false, 2, 5, 0) != 0) return res;
                int rows = System.Runtime.InteropServices.Marshal.ReadInt32(buf);
                IntPtr p = (IntPtr)((long)buf + 4);
                int rowSize = 24;   // MIB_TCPROW_OWNER_PID: 4*6 字段 = 24 字节（x86/x64 同）
                for (int i = 0; i < rows; i++)
                {
                    IntPtr row = (IntPtr)((long)p + i * rowSize);
                    int localPort = ((System.Runtime.InteropServices.Marshal.ReadInt32(row, 8) & 0xFFFF));
                    localPort = ((localPort & 0xFF) << 8) | ((localPort >> 8) & 0xFF);   // 网络序 → 主机序
                    int pid = System.Runtime.InteropServices.Marshal.ReadInt32(row, 20);
                    if (localPort == port && pid > 0 && !res.Contains(pid)) res.Add(pid);
                }
            }
            finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(buf); }
        }
        catch { }
        return res;
    }
    // 只在工作线程调用：结束占用 3000 端口的进程。优先用 API 查 PID（毫秒级），不再依赖 netstat 子进程
    static bool StopGameServer()
    {
        bool did = false;
        try { if (pNode != null && !pNode.HasExited) { KillTree(pNode); did = true; } } catch { }
        pNode = null;

        // 一轮 API 查杀；杀完再查一次，最多两轮（应对父子进程一起占端口的情况）
        for (int round = 0; round < 2; round++)
        {
            List<int> pids = PidsListeningOn(3000);
            if (pids.Count == 0) break;
            foreach (int pid in pids)
            {
                try
                {
                    using (Process victim = Process.GetProcessById(pid))
                    {
                        string name = "";
                        try { name = victim.ProcessName; } catch { }
                        victim.Kill();
                        victim.WaitForExit(1500);
                        did = true;
                        Log("已结束占用 3000 端口的进程：" + name + " (PID " + pid + ")");
                    }
                }
                catch { }
            }
            Thread.Sleep(150);
        }

        srvUp = false;
        srvCheckedAt = DateTime.Now;
        return did;
    }
    // 后台执行"结束游戏进程"，完成后回主线程给反馈（UI 不再阻塞）
    static void StopGameServerAsync(Action<bool> done)
    {
        ThreadPool.QueueUserWorkItem(delegate
        {
            bool did = false;
            try { did = StopGameServer(); } catch { }
            if (done != null) Ui(delegate { done(did); });
        });
    }
    static void StopAll()
    {
        KillTree(pEt); ThreadPool.QueueUserWorkItem(delegate { try { StopGameServer(); } catch { } });
        RunHidden("taskkill", "/F /IM easytier-core.exe", false);
        // ★ v1.2.3 修：以前「关闭房间」只关游戏服务，openp2p 隧道没人管 —— 进程留着、状态栏也一直显示「已连接」
        try { if (pO2P != null && !pO2P.HasExited) { KillTree(pO2P); Log("已结束隧道进程 openp2p"); } } catch { }
        pO2P = null;
        TunnelStop();
        pendingJoinOpen = false;
        pEt = null; pNode = null;
        Log("已停止");
        FlashStatus("○ 隧道未连接　○ 游戏服务未运行", Color.FromArgb(150, 200, 255));
        Ui(delegate
        {
            if (membersLine != null) membersLine.Text = "";
            if (roomInfo != null) roomInfo.Text = "";
            startBtn.Text = "关闭房间";
            openBtn.Enabled = false; shareBtn.Enabled = false;
        });
    }

    static void CheckState()
    {
        if (!started) return;
        TunnelPoll();                                // 隧道四态：读 openp2p 自己的日志推进状态
        bool srv = srvUp;
        CheckMembers(false);
        if (memberList != null && memberList.IsHandleCreated) memberList.Text = MemberText(ParsePeers());
        bool bannerOn = BannerActive();
        if (!bannerOn)
        {
            string s = TunnelStatusText() + "　　" + (srv ? "● 游戏服务运行中" : "○ 游戏服务未运行");
            if (s != lastStatus) { lastStatus = s; Status(s); }
        }
        if (membersLine != null && lastMembers != membersLine.Text) membersLine.Text = lastMembers;
    }

    // ===== 隧道四态：未连接 / 握手中 / 已连接 / 失败（v1.2.3）=====
    // 判定来源：openp2p 自己的日志文件 openp2p\log\openp2p.log，每 2 秒只读「新增」的那一段。
    // ★ 2026-10-04 实测纠正：不能靠 pO2P.OutputDataReceived！加了 -d 守护模式后父进程只打印
    //   "daemon run start / system service start / start worker process"，
    //   真正的 "login ok / sdwan init ok" 是 worker 进程写的，只落日志文件 ——
    //   实测那 15 行隧道 stdout 里 "login ok" 命中 0 次 —— 所以判据必须是日志文件，不能是 stdout。
    static readonly object tunnelLock = new object();
    static int tunnelState = 0;                  // 0=未连接 1=握手中 2=已连接 3=失败
    static DateTime tunnelStartedAt = DateTime.MinValue;
    static string tunnelFail = "";
    static bool tunnelHost;
    static long o2pLogPos;
    static bool tunnelLogErrLogged;

    static string O2PLogFile() { return Path.Combine(AppDir, "openp2p", "log", "openp2p.log"); }

    // 开房/进房时调用：重置为「握手中」，并记下日志基线（上一次运行的老日志不算本次）
    static void TunnelBegin(bool host)
    {
        lock (tunnelLock)
        {
            tunnelState = 1; tunnelStartedAt = DateTime.Now; tunnelFail = "";
            tunnelHost = host; tunnelLogErrLogged = false; o2pLogPos = 0;
            try { FileInfo fi = new FileInfo(O2PLogFile()); if (fi.Exists) o2pLogPos = fi.Length; } catch { }
        }
        Log("[隧道状态] 握手中（" + (host ? "房主" : "加入房间") + "，日志基线 " + o2pLogPos + " 字节）");
    }

    static void TunnelStop()
    {
        lock (tunnelLock) { tunnelState = 0; tunnelFail = ""; }
    }

    static void TunnelSet(int st, string reason)
    {
        int old;
        lock (tunnelLock) { old = tunnelState; tunnelState = st; if (reason != null) tunnelFail = reason; }
        if (old == st) return;
        if (st == 0) { pendingJoinOpen = false; rejoinPending = false; nextPortProbeAt = DateTime.MinValue; lastPortOpen = true; portEverUp = false; dropSince = DateTime.MinValue; watchdogOffered = false; }   // ★ 只在"收摊"时清；失败时不清（openp2p 会自愈，连上后还要自动开页面）
        if (st == 2)
        {
            portEverUp = false; lastPortOpen = true;   // 新一次连接：通道"还没就绪"属正常，别当故障报警
            dropSince = DateTime.MinValue; watchdogOffered = false;
            Log("[隧道状态] 已连接（" + (tunnelHost ? "房主已在线" : "已连上房主") + "，启动后 " + (int)(DateTime.Now - tunnelStartedAt).TotalSeconds + " 秒连上）");
            if (pendingJoinOpen) Ui(delegate { JoinOpenGame(); });   // 粥友：隧道真连上了才开浏览器
            else if (rejoinPending) { if (tunnelSim) ReopenAfterDrop(); else Ui(delegate { ReopenAfterDrop(); }); }
        }
        else if (st == 4)
        {
            dropSince = DateTime.Now;   // 看门狗从这里开始算：断开多久了
            Log(tunnelHost ? "[隧道状态] 对方断开了（你自己还在线，openp2p 会等对方重连）"
                           : "[隧道状态] 隧道断开：" + tunnelFail + "（openp2p 会自己重连）");
            // ★ 断线自动恢复（v1.2 起）：只给**加入方**记待办 —— 房主那个页面是本机 127.0.0.1，
            //   跟隧道没关系；而且只在"他这次进房已经进过游戏"之后才自动重开，
            //   免得粥友刚进房就被多开一个标签页。
            if (!tunnelHost && joinOpenedOnce)
            {
                rejoinPending = true;
                Log("　· 重连成功后会自动重新打开游戏页面（浏览器里的 WebSocket 断了不会自愈）");
            }
        }
        else if (st == 3)
        {
            Log("[隧道状态] 没连上：" + tunnelFail);
            Log("　· 先看隧道自己的日志 openp2p\\log\\openp2p.log 最后几行（login fail / token error 写在里面）");
            Log("　· 令牌：房主和粥友必须是同一个 openp2p 令牌（设置页「联机令牌」看尾号），对不上就登录不上");
            Log("　· 防火墙：Windows 问「是否允许联网」时要点允许，放行 卫戍协议启动器.exe 和 openp2p.exe");
            Log("　· 网络：公司/校园网常封 openp2p 的 27183 端口，换手机热点试一次能连上就是网络问题");
            Log("　· 房主那边也要显示「隧道已连接」，房主没连上时粥友连不进来");
        }
    }

    static string Shorten(string s, int n) { return s.Length <= n ? s : s.Substring(0, n) + "…"; }

    // 每 2 秒随 CheckState 跑一次：只读日志新增部分，别整文件重读
    static void TunnelPoll()
    {
        int st;
        lock (tunnelLock) st = tunnelState;
        if (st == 0) return;                                  // 还没开房/进房：没得读
        // ★ st==3（失败）也要继续读日志：openp2p 是守护进程会自己重连，实测出现过
        //   "先误报失败、40 秒后才真正连上"——旧代码失败后直接 return，于是永远卡在红字，
        //   连 pendingJoinOpen 都被清掉、浏览器再也不会自动打开（2026-10-04 双机实测踩到）

        bool alive = false;
        try { alive = pO2P != null && !pO2P.HasExited; } catch { }
        if (!alive)
        {
            if (st == 1) TunnelSet(3, "隧道程序没能启动");
            else if (st == 2) TunnelSet(3, "隧道程序退出了");
            return;
        }

        try
        {
            string f = O2PLogFile();
            if (File.Exists(f))
            {
                long len = new FileInfo(f).Length;
                if (len < o2pLogPos) o2pLogPos = 0;            // 被截断/轮转过 → 从头读
                if (len > o2pLogPos)
                {
                    string txt;
                    using (FileStream fs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        fs.Seek(o2pLogPos, SeekOrigin.Begin);
                        using (StreamReader sr = new StreamReader(fs, Encoding.UTF8)) txt = sr.ReadToEnd();
                    }
                    int cut = txt.LastIndexOf('\n');               // 末尾可能是写了一半的一行，留到下次
                    if (cut >= 0)
                    {
                        o2pLogPos += Encoding.UTF8.GetByteCount(txt.Substring(0, cut + 1));
                        string[] ls = txt.Substring(0, cut).Split('\n');
                        foreach (string raw in ls) TunnelFeed(raw.Trim());
                    }
                }
            }
        }
        catch (Exception ex)
        {
            if (!tunnelLogErrLogged) { tunnelLogErrLogged = true; Log("读隧道日志失败（不影响连接，可能被占用）：" + ex.Message); }
        }

        lock (tunnelLock) { st = tunnelState; }
        if (st == 1 && (DateTime.Now - tunnelStartedAt).TotalSeconds >= 75)
            TunnelSet(3, tunnelFail.Length > 0 ? tunnelFail : "等了 75 秒还没登录成功");

        // ★ 客户端"页面通道"探针（2026-10-04 深夜加）：隧道状态是读 openp2p 日志判出来的，
        //   "浏览器那边还通不通"只有真连一次才知道。双机实测里粥友 19:06 说"中间闪退了一次"，
        //   而房主侧隧道那会儿并没有断 —— 这种故障以前在日志里一点痕迹都不留。
        //   现在进房后每 10 秒探一次 127.0.0.1:3000，状态一变就写一行，下次双机就能定死它。
        if (st == 2 && !tunnelHost && DateTime.Now >= nextPortProbeAt)
        {
            nextPortProbeAt = DateTime.Now.AddSeconds(10);
            bool up = false;
            try
            {
                using (System.Net.Sockets.TcpClient c = new System.Net.Sockets.TcpClient())
                {
                    IAsyncResult ar = c.BeginConnect("127.0.0.1", 3000, null, null);
                    up = ar.AsyncWaitHandle.WaitOne(400) && c.Connected;
                }
            }
            catch { }
            if (up != lastPortOpen)
            {
                lastPortOpen = up;
                bool first = !portEverUp && !joinOpenedOnce;   // 隧道刚连上、页面还没开过 = 正常窗口，不是故障
                if (up) portEverUp = true;
                Log("[页面通道] " + (up ? "恢复可用（127.0.0.1:3000 能连上）"
                                        : (first ? "还没就绪（隧道刚连上，本机 3000 还没开始转发 —— 正常，稍等几秒）"
                                                 : "不可用：隧道看着还好，但本机 3000 连不上 —— 浏览器里会是拒绝连接/白屏")));
                bool upNow = up; bool firstNow = first;
                Ui(delegate
                {
                    FlashStatusFor(upNow ? "● 页面通道已恢复（浏览器若还是白屏就刷新一下）"
                                         : (firstNow ? "◌ 页面通道还没就绪，稍等几秒…" : "◌ 页面通道断了，正在自动重连…"),
                                   upNow ? C_MINT : Color.FromArgb(240, 170, 90), 8);
                });
            }
        }

        // ★ 看门狗（方案 A，2026-10-05）：断开卡住 90 秒还没回来，就把状态栏那行变成可点的「重启组网」。
        //   实测依据：20:07:43 掉线 → 20:12:50 openp2p 的 worker 自己崩掉重启 → 20:13:07 才连上，
        //   中间 5 分 24 秒全是干等。**只提示、不自动重启** —— 自动重启会在"它下一秒钟本来就要连上"时
        //   白打断一次，把这个判断权交给用户更划算。
        if (!tunnelHost && st == 4 && dropSince != DateTime.MinValue)
        {
            bool offer = WatchdogShouldOffer(st, tunnelHost, dropSince, DateTime.Now);
            if (offer != watchdogOffered)
            {
                watchdogOffered = offer;
                if (offer) Log("[看门狗] 断开已 " + (int)(DateTime.Now - dropSince).TotalSeconds + " 秒还没回来 —— 状态栏给了「点这行重启组网」");
                Ui(delegate { if (statusLine != null) statusLine.Cursor = offer ? Cursors.Hand : Cursors.Default; });
            }
        }
        else if (watchdogOffered)
        {
            watchdogOffered = false;
            Ui(delegate { if (statusLine != null) statusLine.Cursor = Cursors.Default; });
        }
    }

    // openp2p 日志的一行 → 状态
    static void TunnelFeed(string ln)
    {
        if (ln.Length == 0) return;
        string low = ln.ToLowerInvariant();
        // ★ openp2p 启动时会打一句 `ERROR c.Network.Token == 0 skip save` —— 意思是
        //   "配置文件里没存 token（这次是用 -token 命令行传的）"，**无害**。
        //   2026-10-04 双机实测：它同时命中下面的"token + error"规则 → 粥友那边进房 1 秒就报
        //   「隧道没连上：令牌无效或已过期」，而隧道 40 秒后其实正常连上了（房主侧日志有
        //   handshakeS2C ok / quic connection ok）。先把这种噪音行挡掉。
        if (low.IndexOf("skip save") >= 0) return;
        // ★ 隧道断了/重连：openp2p 会自己接回来，但浏览器里的 WebSocket 不会自愈。
        //   2026-10-04 双机实测：16:15:34 建好 → 16:23:34 `p2ptunnel close`（房主在手机热点上）
        //   → 16:24:07 自己重连成功；期间状态栏一直写"已连接"，粥友那边只看到游戏里
        //   "服务器连接已中断"，只能干瞪眼。现在断开就说断开，重连上再说已连接。
        if (low.IndexOf("p2ptunnel close") >= 0 || low.IndexOf("peer offline") >= 0)
        { TunnelSet(4, "隧道断开（网络抖动/对方掉线），openp2p 正在自动重连"); return; }
        // ★ 重连成功的标志不止 login ok：重连不会再打一遍 login ok，而是 TCP/QUIC 握手成功那几行
        if (low.IndexOf("login ok") >= 0 || low.IndexOf("sdwan init ok") >= 0 || low.IndexOf("connection ok") >= 0) { TunnelSet(2, null); return; }
        if (low.IndexOf("login fail") >= 0 || low.IndexOf("auth fail") >= 0 || low.IndexOf("login error") >= 0)
        { TunnelSet(3, "登录被拒（令牌或账号不对）"); return; }
        // ★ 这条只在"明确说令牌本身不对"时才判失败；不要再拿 error/fail 当条件（那样启动噪音也会中招）
        if (low.IndexOf("token") >= 0 && (low.IndexOf("invalid") >= 0 || low.IndexOf("expire") >= 0 || low.IndexOf("not match") >= 0))
        { TunnelSet(3, "令牌无效或已过期"); return; }
        if (low.IndexOf("sdwan init fail") >= 0 || low.IndexOf("connect server fail") >= 0)
        { TunnelSet(3, "连不上 openp2p 服务器"); return; }
        if (low.IndexOf("fail") >= 0 || low.IndexOf("error") >= 0)
        {
            lock (tunnelLock) { if (tunnelFail.Length == 0) tunnelFail = "日志里有报错"; }   // 只当线索，等超时再定失败
            Log("[隧道] 注意：" + Shorten(ln, 120));
        }
    }

    // 状态栏文案（秒数按 5 秒一档变，避免状态栏一直闪）
    static string TunnelStatusText()
    {
        int st; string why; bool host; DateTime t0;
        lock (tunnelLock) { st = tunnelState; why = tunnelFail; host = tunnelHost; t0 = tunnelStartedAt; }
        if (st == 1)
        {
            int sec = (int)(DateTime.Now - t0).TotalSeconds; if (sec < 0) sec = 0;
            return "◌ 隧道握手中…（已等 " + (sec / 5 * 5) + " 秒，一般 5–20 秒）";
        }
        if (st == 2) return host ? "● 隧道已连接（房主已在线）" : "● 隧道已连接（已连上房主）";
        if (st == 4)
        {
            int ds = dropSince == DateTime.MinValue ? 0 : (int)(DateTime.Now - dropSince).TotalSeconds;
            if (host) return "○ 还没有粥友连着（你自己在线，等对方重连）";
            if (watchdogOffered) return "▶ 重连卡住了（已等 " + ds / 5 * 5 + " 秒）—— 点这行重启组网";
            return "◌ 隧道断开，正在自动重连…（已等 " + ds / 5 * 5 + " 秒，连上后会自动重开游戏页面）";
        }
        if (st == 3) return "✗ 隧道没连上：" + Shorten(why, 16) + "（看 logs\\组网.log）";
        return "○ 隧道未连接（还没开房/还没进房）";
    }

    // ===== 首次进游戏的预期管理（【7】第 3 项 C 路线）=====
    // 以前「进房」是立刻 Process.Start 打开浏览器 —— 那时隧道才刚开始握手（要 15 秒左右），
    // 页面必定是"无法访问"。现在改成：等隧道状态变成「已连接」再打开，并弹一次"素材要慢慢下"的提示。
    static bool pendingJoinOpen;
    static bool joinTipShown;
    static bool joinOpenedOnce;                     // 这次进房以后，游戏页面至少打开过一次
    static DateTime nextPortProbeAt = DateTime.MinValue;   // 客户端"页面通道"探针的下次探测时间
    static bool lastPortOpen = true;                       // 上次探测结果（只在变化时写日志，免得刷屏）
    static bool portEverUp;                                // 这次连接里通道是否曾经通过（没通过前不报警：那是隧道刚连上的正常窗口）
    static DateTime dropSince = DateTime.MinValue;          // 进入"断开重连中"的时刻（看门狗用它算等了多久）
    static bool watchdogOffered;                            // 状态栏是否已经变成可点的「重启组网」
    static bool rejoinPending;                      // 断线了，等重连上再自动重开一次页面
    static DateTime lastReopenAt = DateTime.MinValue;
    static bool tunnelSim;                          // -tunnelsim 自检：只记数，不真开浏览器
    static int simReopenCount;
    static StringBuilder simRep;

    static void JoinOpenWhenReady()
    {
        int st; lock (tunnelLock) st = tunnelState;
        pendingJoinOpen = true;
        if (st == 2) JoinOpenGame();
        else Log("隧道连上后会自动打开游戏页面（当前状态 " + st + "）");
    }

    // 断线恢复：openp2p 自己重连上之后，把游戏页面重新打开一次
    // ★ 只对加入方、只在进过房之后触发（见 TunnelSet 的两处判断）
    // ★ 防刷屏：60 秒内最多自动重开一次 —— 热点环境实测约 8 分钟断一次，正常撞不上；
    //   万一网络剧烈抖动连着断，也不会甩出一堆标签页
    static void ReopenAfterDrop()
    {
        rejoinPending = false;
        if ((DateTime.Now - lastReopenAt).TotalSeconds < 60)
        {
            Log("隧道重连成功，但 60 秒内刚重开过一次页面 —— 这次只提示，不再开新标签页");
            FlashStatusFor("● 隧道已重连（页面若已失效请手动刷新）", C_MINT, 8);
            return;
        }
        lastReopenAt = DateTime.Now;
        if (tunnelSim)
        {
            simReopenCount++;
            if (simRep != null) simRep.AppendLine("　[自检] 这里会重新打开游戏页面（第 " + simReopenCount + " 次）");
            return;
        }
        // ★ 2026-10-05 修（实测 20:13:07 抓到的）：隧道报"已连接"那一刻本机 3000 还没开始转发，
        //   直接开就又得到一张"拒绝连接"。跟首次进房一样，先真连一次本机端口再开。
        ThreadPool.QueueUserWorkItem(delegate
        {
            bool ok = WaitPortOpen(3000, 25);
            Ui(delegate
            {
                try { Process.Start(BrowserUrl()); } catch { }
                Log("隧道重连成功，已自动重新打开游戏页面：" + BrowserUrl() + (ok ? "" : "（⚠ 打开时本机 3000 仍未就绪）"));
                FlashStatusFor("● 隧道重连成功，游戏页面已重新打开", C_MINT, 8);
            });
        });
    }

    // ===== 看门狗（方案 A，2026-10-05）=====
    // 背景：实测（2026-10-04 20:07–20:13）客户端掉线后**空等了 5 分 24 秒** —— 真正让它回来的不是
    // "重连成功"，是 openp2p 自己的 worker 崩掉、daemon 把它重启。既然"重启"才是解药，就别让人无限干等：
    // 断开超过 90 秒，状态栏那行变成可点的「▶ 重连卡住了 —— 点这行重启组网」。
    // ★ 只给**加入方**提供：房主把自己引擎重启会把正在连的粥友踢下线，而且房主侧"断开"多半只是对方走了。
    // 看门狗判定（纯函数，可离线自检）：只有"加入方 + 状态4 + 断开已满 90 秒"才提示重启
    static bool WatchdogShouldOffer(int st, bool host, DateTime since, DateTime now)
    {
        return !host && st == 4 && since != DateTime.MinValue && (now - since).TotalSeconds >= 90;
    }

    static void WatchdogRestart()
    {
        if (!watchdogOffered) return;     // 没到那一步点了也没事（防误点把好好的连接重启掉）
        Log("[看门狗] 用户点了状态栏 → 重启组网引擎");
        ThreadPool.QueueUserWorkItem(delegate
        {
            try
            {
                Ui(delegate { FlashStatus("◌ 正在重启组网引擎…", Color.FromArgb(240, 200, 120)); });
                // 必须整族清掉：只杀父 daemon 的话 worker 还活着、还占着本机 3000，新的会起不来
                RunHidden("taskkill", "/F /IM openp2p.exe", false);
                Thread.Sleep(800);
                watchdogOffered = false;
                Ui(delegate { if (statusLine != null) statusLine.Cursor = Cursors.Default; });
                TunnelBegin(tunnelHost);
                StartO2P(tunnelHost, tunnelHost ? null : joinPeerNode);
                Log("[看门狗] 组网引擎已重启，等它重新握手（一般 5–20 秒）");
            }
            catch (Exception ex) { Log("[看门狗] 重启组网失败：" + ex.Message); }
        });
    }

    // -tunnelsim：离线验"断线自动恢复"这条链路（不开浏览器、不碰网络、不弹框）
    static void TunnelSimTest()
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("断线自动恢复 离线自检  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + EnvLine());
        simRep = sb; tunnelSim = true;
        tunnelHost = false; joinOpenedOnce = false; pendingJoinOpen = false; rejoinPending = false;
        lastReopenAt = DateTime.MinValue; simReopenCount = 0;

        TunnelSet(2, null); TunnelSet(4, "模拟抖动"); TunnelSet(2, null);
        int a = simReopenCount;
        sb.AppendLine("① 还没进过房就断线 → 重开次数=" + a + "（期望 0）");

        joinOpenedOnce = true;
        TunnelSet(4, "模拟抖动"); TunnelSet(2, null);
        int b = simReopenCount - a;
        sb.AppendLine("② 进过房以后掉线重连 → 新增重开=" + b + "（期望 1）");

        TunnelSet(4, "模拟抖动"); TunnelSet(2, null);
        int c = simReopenCount - a - b;
        sb.AppendLine("③ 紧接着又断一次 → 新增重开=" + c + "（期望 0，被 60 秒节流挡住）");

        lastReopenAt = DateTime.Now.AddSeconds(-61);   // 把节流窗口往前拨
        TunnelSet(4, "模拟抖动"); TunnelSet(2, null);
        int d = simReopenCount - a - b - c;
        sb.AppendLine("④ 隔了 60 秒再断一次 → 新增重开=" + d + "（期望 1，证明节流不是永久失效）");

        tunnelHost = true; lastReopenAt = DateTime.MinValue;
        int e0 = simReopenCount;
        TunnelSet(4, "模拟抖动"); TunnelSet(2, null);
        int e = simReopenCount - e0;
        sb.AppendLine("⑤ 房主身份掉线重连 → 新增重开=" + e + "（期望 0：房主看的是本机页面）");

        tunnelHost = false; joinOpenedOnce = true;
        TunnelSet(4, "模拟抖动"); TunnelSet(0, null);
        bool residue = rejoinPending;
        sb.AppendLine("⑥ 断线途中点了「关闭房间」→ 待办残留=" + residue + "（期望 False）");

        bool pass = (a == 0 && b == 1 && c == 0 && d == 1 && e == 0 && !residue);

        // ⑦ 看门狗（方案 A）：断开卡住 90 秒才把状态栏变成可点的「重启组网」
        DateTime wt = DateTime.Now;
        bool w1 = WatchdogShouldOffer(4, false, wt, wt.AddSeconds(89));    // 还没到 → False
        bool w2 = WatchdogShouldOffer(4, false, wt, wt.AddSeconds(91));    // 到了 → True
        bool w3 = WatchdogShouldOffer(4, true, wt, wt.AddSeconds(600));    // 房主 → False（重启会把粥友踢下线）
        bool w4 = WatchdogShouldOffer(2, false, wt, wt.AddSeconds(600));   // 已经连上 → False
        sb.AppendLine("⑦ 看门狗：89秒=" + w1 + " 91秒=" + w2 + " 房主=" + w3 + " 已连接=" + w4 + "（期望 False/True/False/False）");
        pass = pass && !w1 && w2 && !w3 && !w4;

        sb.AppendLine("总判定=" + (pass ? "PASS" : "FAIL"));
        try { File.AppendAllText(Path.Combine(LogDirRoot(), "断线恢复检查.txt"), sb.ToString() + "\r\n", Encoding.UTF8); } catch { }
    }

    // ★ 2026-10-04 深夜修：隧道"已连接"是**读 openp2p 日志**判出来的（TunnelFeed），那一刻
    //   本机 3000 未必已经开始转发 —— 双机实测里粥友看到的就是"一打开浏览器就 127.0.0.1
    //   拒绝连接，过一会儿/刷新一下又好了"。所以开页面前真连一次本机端口，不通就每秒重试；
    //   实在不通也照样打开（绝不比原来更差，只是状态栏会提示要刷新）。
    static bool WaitPortOpen(int port, int seconds)
    {
        DateTime t0 = DateTime.Now;
        while (true)
        {
            try
            {
                using (System.Net.Sockets.TcpClient c = new System.Net.Sockets.TcpClient())
                {
                    IAsyncResult ar = c.BeginConnect("127.0.0.1", port, null, null);
                    if (ar.AsyncWaitHandle.WaitOne(500) && c.Connected) return true;
                }
            }
            catch { }
            if ((DateTime.Now - t0).TotalSeconds >= seconds) return false;
            Thread.Sleep(600);
        }
    }

    static void JoinOpenGame()
    {
        pendingJoinOpen = false;
        joinOpenedOnce = true;
        FlashStatusFor("● 隧道已连上，正在确认游戏页面通道…", C_MINT, 6);
        ThreadPool.QueueUserWorkItem(delegate
        {
            bool ok = WaitPortOpen(3000, 25);          // 这就是"隧道通了、本机还没开始转发"的那几秒
            if (ok) Log("本机 3000 已就绪，打开游戏页面");
            else Log("等了 25 秒本机 3000 还没就绪，仍然打开页面（浏览器里可能要先刷新一次）");
            Ui(delegate
            {
                try { Process.Start(BrowserUrl()); } catch { }
                Log("已打开游戏页面：" + BrowserUrl());
                FlashStatusFor(ok ? "● 隧道已连上，游戏页面已打开" : "◌ 页面已打开，若显示拒绝连接就刷新一次",
                               ok ? C_MINT : Color.FromArgb(240, 170, 90), 10);
                JoinTipOnce();
            });
        });
    }

    static void JoinTipOnce()
    {
        if (joinTipShown) return;
        joinTipShown = true;
        MessageBox.Show(
            "游戏页面这就打开。\n\n" +
            "★ 第一次进游戏要从房主电脑下载约 250 MB 素材：\n" +
            "　　页面会转圈、图案一张张慢慢出现，这是正常现象 —— 别关页面，也别按刷新。\n\n" +
            "★ 素材浏览器只缓存 1 天：今天玩过，明天再玩可能又要等一次。\n\n" +
            "★ 页面打不开的话：回启动器看状态栏（会写明是隧道没连上还是别的），\n" +
            "　　也可以点联机页的「打开游戏页面」再试一次。",
            "第一次进游戏会比较慢（正常现象）");
    }

    static bool checkBusy;
    static DateTime membersCheckedAt = DateTime.MinValue;

    // 后台健康检查：HTTP 请求绝不在 UI 线程上跑（这正是"到处都卡"的根因）
    static bool srvUp;
    static bool healthBusy;
    static string lastStatus = "";

    static void CheckHealthAsync()
    {
        if (healthBusy) return;
        healthBusy = true;
        ThreadPool.QueueUserWorkItem(delegate
        {
            bool ok = false;
            try
            {
                HttpWebRequest rq = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:3000/healthz");
                rq.Timeout = 800;
                using (WebResponse rp = rq.GetResponse())
                using (StreamReader sr = new StreamReader(rp.GetResponseStream()))
                { sr.ReadToEnd(); ok = true; }
            }
            catch { ok = false; }
            srvUp = ok;
            healthBusy = false;
        });
    }

    static DateTime srvCheckedAt = DateTime.MinValue;

    // 纯 API 判断端口是否在监听：零进程开销（比 netstat 快两个数量级）
    static bool PortListening(int port)
    {
        try
        {
            foreach (IPEndPoint ep in IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners())
                if (ep.Port == port) return true;
        }
        catch { }
        return false;
    }

    // 只读后台健康巡检的结果，绝不在这里发 HTTP（避免与巡检重复探测）
    static bool CachedHealth()
    {
        if (srvUp) return true;
        return PortListening(3000);
    }

    static readonly ManualResetEvent exitSignal = new ManualResetEvent(false);

    static void StartHealthLoop()
    {
        Thread t = new Thread((ThreadStart)delegate
        {
            while (!exitSignal.WaitOne(1500))
            {
                bool ok = false;
                try
                {
                    HttpWebRequest rq = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:3000/healthz");
                    rq.Timeout = 800;
                    using (WebResponse rp = rq.GetResponse())
                    using (StreamReader sr = new StreamReader(rp.GetResponseStream()))
                    { sr.ReadToEnd(); ok = true; }
                }
                catch { ok = false; }
                srvUp = ok;
            }
        });
        t.IsBackground = true;   // 不阻止进程退出
        t.Start();
    }
    // 在后台线程查询在线节点，避免在界面线程里启动进程导致卡顿
    static void CheckMembers(bool force)
    {
        if (checkBusy) return;
        if (DateTime.Now - membersCheckedAt < TimeSpan.FromSeconds(6)) return;
        membersCheckedAt = DateTime.Now;
        if (pEt == null || pEt.HasExited) { lastMembers = ""; return; }
        checkBusy = true;
        ThreadPool.QueueUserWorkItem(delegate
        {
            string res = "";
            try
            {
                ProcessStartInfo si = new ProcessStartInfo(EtCli(), "peer");
                si.UseShellExecute = false; si.CreateNoWindow = true;
                si.RedirectStandardOutput = true; si.RedirectStandardError = true;
                si.StandardOutputEncoding = Encoding.UTF8; si.StandardErrorEncoding = Encoding.UTF8;   // 主机名可能是中文
                Process p = Process.Start(si);
                string o = p.StandardOutput.ReadToEnd();
                p.WaitForExit(2000);
                List<string> who = new List<string>();
                foreach (string line in o.Split('\n'))
                {
                    string tt = line.Trim();
                    if (tt.StartsWith("10.126.126."))
                    {
                        string num = tt.Split(new char[] { ' ', '\t' })[0].Replace("10.126.126.", "");
                        if (!who.Contains(num)) who.Add(num);
                    }
                }
                if (who.Count > 0) { who.Sort(); res = "在线节点 " + who.Count + " 个：编号 " + string.Join("、", who.ToArray()); }
                else res = "还没看到其他节点（等粥友点「开始」）";
            }
            catch { res = ""; }
            checkBusy = false;
            lastMembers = res;
        });
    }

    // ================= 分享 / 更新 / 打包 =================

    // 给粥友的房间链接：带房间名/密码参数，对方粘进启动器就能自动进房
    static string RoomQuery()
    {
        string rn = Convert.ToBase64String(Encoding.UTF8.GetBytes(seats[0]));
        return "?rn=" + Uri.EscapeDataString(rn) + "&pw=" + Uri.EscapeDataString(seats[1]) + "&peer=" + Uri.EscapeDataString(MyO2PNode()) +
               "&o2p=" + Uri.EscapeDataString(Token());   // 把联机令牌一起带给粥友，对方粘贴即自动保存，不用手填
    }

    static string RoomUrl(bool withQuery)
    {
        return HostLink() + "/" + (withQuery ? RoomQuery() : "");   // openp2p：粥友那边始终是本机 3000，不是 EasyTier 虚拟 IP
    }

    // ★ 2026-10-04 深夜：**浏览器**打开的地址只带房间名/密码。
    //   以前这里直接塞整串 RoomQuery()，把 peer= 和 o2p=<联机令牌> 一路带进了地址栏 ——
    //   浏览器根本不需要它们（只有"粘进启动器"的邀请文本才需要），结果令牌留在了
    //   粥友的浏览器历史和聊天截图里（实测截图地址栏里就有明文令牌）。
    static string BrowserUrl()
    {
        string rn = Convert.ToBase64String(Encoding.UTF8.GetBytes(seats[0]));
        return HostLink() + "/?rn=" + Uri.EscapeDataString(rn) + "&pw=" + Uri.EscapeDataString(seats[1]);
    }

    // 整段邀请内容：链接 + 密钥说明 + 玩法提示，整个粘到群里即可
    static string InviteText()
    {
        return "【卫戍协议：盟约】开黑了，进来玩！\n" +
               "1) 打开启动器 → 选「加入粥友的房间」→ 把下面这行整个粘进去 → 点「进房」\n" +
               RoomUrl(true) + "\n" +
               "2) 进房后浏览器会自动打开 http://127.0.0.1:3000 （这是隧道，看着是本机其实是连到我）\n" +
               "3) 进游戏后选「同盟模拟」→ 我这边「创建同盟」会给一串 4 位密钥，我再发群里，你粘一下\n" +
               "4) 第一次进去要从我这边下载几百 MB 素材：页面会转圈、素材慢慢出现，别关别刷新；\n" +
               "   之后有浏览器缓存就快了（缓存只留 1 天，隔天再玩可能又要等一次）";
    }
    static string ShareText()
    {
        if (IsHost)
            return "【" + GameName + "】开黑了！\n" +
                   "① 打开启动器 → 选「加入房间」\n" +
                   "② 房间名：" + seats[0] + "　密码：" + seats[1] + "\n" +
                   "③ 编号：各自不同（2、3、4…），点「开始」\n" +
                   "④ 浏览器打开 " + HostLink() + "（第一次进去要下载素材，稍等一下）";
        return "【" + GameName + "】我在房间里了\n" +
               "房间名：" + seats[0] + "　密码：" + seats[1] + "　我的编号：" + seats[2] + "\n" +
               "房主地址：" + HostLink();
    }

    // ================= 自动更新（直连 GitHub Releases）=================
    // 判定：Release 的 tag（如 v1.2）与启动器自身 Version（如 1.1）不同就认为有新版。
    // 更新方式：下载 Release 里的 zip → 解出 卫戍协议启动器.exe → 用 cmd 脚本等我们退出后替换 → 重启自己
    //（exe 运行中不能覆盖自己，所以必须走"延迟替换"这一步）
    static string updTag = "";        // 结构：已发现的版本号（如 v1.2）
    static string updZip = "";        // 结构：该版本更新包下载地址
    static bool updAvail = false;     // 结构：是否有新版待更新

    static int VerNum(string s)
    {
        try
        {
            s = (s ?? "").Trim().TrimStart('v', 'V');
            string[] a = s.Split('.');
            if (a.Length == 0 || a[0].Length == 0) return -1;
            int maj = int.Parse(a[0]);
            int min = a.Length > 1 ? int.Parse(a[1]) : 0;
            int pat = a.Length > 2 ? int.Parse(a[2]) : 0;
            return maj * 10000 + min * 100 + pat;
        }
        catch { return -1; }
    }

    // 后台静默查一次：不弹框，只把结果写进状态行 / 更新按钮
    static void CheckUpdateQuiet()
    {
        ThreadPool.QueueUserWorkItem(delegate
        {
            string tag = "", zip = "";
            try
            {
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
                HttpWebRequest rq = (HttpWebRequest)WebRequest.Create("https://api.github.com/repos/" + Repo + "/releases/latest");
                rq.Timeout = 12000;
                rq.UserAgent = "sp-launcher";
                using (WebResponse rp = rq.GetResponse())
                using (StreamReader sr = new StreamReader(rp.GetResponseStream()))
                {
                    string j = sr.ReadToEnd();
                    int i = j.IndexOf("\"tag_name\":\"");
                    if (i >= 0) tag = j.Substring(i + 12, j.IndexOf('"', i + 12) - i - 12);
                    int a = j.IndexOf("\"browser_download_url\":\"");
                    if (a >= 0) zip = j.Substring(a + 24, j.IndexOf('"', a + 24) - a - 24);
                }
            }
            catch { Log("自动更新检查失败（打不开 GitHub，可能被墙）：跳过"); return; }
            int rv = VerNum(tag), lv = VerNum(Version);
            if (rv > 0 && lv > 0 && rv > lv)
            {
                updTag = tag; updZip = zip;
                if (updZip.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    updAvail = true;
                    Log("发现新版启动器 " + tag + "（本机 v" + Version + "）");
                    Ui(delegate
                    {
                        if (setUpdBtn != null) { setUpdBtn.Text = "▲ 立即更新到 " + tag; setUpdBtn.BackColor = C_ON; setUpdBtn.ForeColor = Color.White; }
                        if (updateLine != null)
                        {
                            updateLine.Text = "★ 发现新版 " + tag + "（本机 v" + Version + "）→ 到「设置」页点「▲ 立即更新到 " + tag + "」";
                            updateLine.ForeColor = Color.FromArgb(240, 200, 120);
                        }
                        // 提示性要够强：横幅停 10 秒（默认只停 4 秒，用户会看漏）
                        FlashStatusFor("● 启动器有新版本 " + tag + "：设置页点一下就能更新", C_MINT, 10);
                        // 顺手把用户直接送到设置页，省得他找
                        if (page != 2) ShowPage(2);
                    });
                }
                else Log("新版 " + tag + " 的 Release 里没挂 zip 更新包，跳过自动更新");
            }
            else Ui(delegate
            {
                if (updateLine != null) { updateLine.Text = "已是最新（启动器 v" + Version + "）"; updateLine.ForeColor = C_MINT; }
            });
        });
    }

    // 点按钮：有新版就下载更新，没有就告诉用户
    static void CheckUpdateInteractive()
    {
        if (updAvail) { UpdateNow(); return; }
        MessageBox.Show("当前已经是最新版（v" + Version + "）。\n\n如果刚发布过新版本，稍等一两分钟再试（GitHub 有缓存）。", "检查更新");
    }

    // 国内镜像站（只做 GitHub 加速，不参与别的请求）。实测可用；第一个不行就试下一个
    static readonly string[] GhMirrors = new string[] {
        "https://gh-proxy.com/",
        "https://ghfast.top/",
        "https://ghproxy.net/"
    };
    static string MirrorUrl(string mirror, string url)
    {
        if (url.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase))
            return mirror + url;
        if (url.StartsWith("https://raw.githubusercontent.com/", StringComparison.OrdinalIgnoreCase))
            return mirror + url;
        return null;   // 不是 GitHub 地址，镜像帮不上
    }

    // 下载更新包：先直连（给 secs 秒耐心），太慢/失败就自动换镜像重下
    static void DownloadUpdate(string url, string dest, int directSecs)
    {
        string[] tryUrls = new string[] { url, MirrorUrl(GhMirrors[0], url), MirrorUrl(GhMirrors[1], url), MirrorUrl(GhMirrors[2], url) };
        Exception last = null;
        for (int i = 0; i < tryUrls.Length; i++)
        {
            if (tryUrls[i] == null) continue;
            bool direct = (i == 0);
            int timeoutSec = direct ? directSecs : 600;        // 直连只等 directSecs 秒，镜像给足 10 分钟
            try
            {
                if (!direct) { Log("直连太慢/失败，改用镜像：" + tryUrls[i].Substring(0, tryUrls[i].IndexOf("/https"))); StatusBusy("正在通过镜像站下载…"); }
                DownloadTo(url == tryUrls[i] ? url : tryUrls[i], dest, timeoutSec);
                if (!direct) Log("镜像下载成功");
                return;
            }
            catch (Exception ex)
            {
                last = ex;
                string why = ex is TimeoutException ? "太慢（超过 " + timeoutSec + " 秒）" : ex.Message;
                Log((direct ? "直连下载失败：" : "镜像下载失败：") + why);
                try { if (File.Exists(dest)) File.Delete(dest); } catch { }
                if (i == 0) StatusBusy("直连太慢，正在切换国内镜像…");
            }
        }
        throw (last ?? new Exception("下载失败"));
    }

    // 单次下载（带进度回显）。超过 timeoutSec 没下完 → 抛 TimeoutException 好去试镜像
    static void DownloadTo(string url, string dest, int timeoutSec)
    {
        ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
        HttpWebRequest rq = (HttpWebRequest)WebRequest.Create(url);
        rq.Timeout = 20000;
        rq.ReadWriteTimeout = 30000;
        rq.UserAgent = "sp-launcher";
        DateTime t0 = DateTime.Now;
        long total = 0, len = 0;
        using (WebResponse rp = rq.GetResponse())
        using (Stream rs = rp.GetResponseStream())
        using (FileStream fs = new FileStream(dest, FileMode.Create))
        {
            len = rp.ContentLength;
            byte[] buf = new byte[65536];
            int n, lastPct = -1;
            while ((n = rs.Read(buf, 0, buf.Length)) > 0)
            {
                fs.Write(buf, 0, n);
                total += n;
                int pct = len > 0 ? (int)(total * 100 / len) : -1;
                if (pct != lastPct)
                {
                    lastPct = pct;
                    long tot = total, l = len;
                    StatusBusy("正在下载更新 " + (pct >= 0 ? pct + "%" : "") +
                        "（" + (tot / 1048576.0).ToString("0.0") + " / " + (l > 0 ? (l / 1048576.0).ToString("0.0") : "?") + " MB）…");
                }
                if ((DateTime.Now - t0).TotalSeconds > timeoutSec) throw new TimeoutException("下载超时");
            }
        }
        Log("更新包已下载：" + (total / 1024) + " KB");
    }

    // ================= 游戏本体下载向导 =================
    // ★ 版权定性（用户最在意的一条）：只从**原作者官方 Release 地址**下载 + 走公共 GitHub 加速镜像，
    //   启动器只当"下载器"；自己搭镜像/网盘放游戏素材 = 自己在分发素材，是唯一的红线，绝不做。
    // ★ 实测结论（2026-10-04 本机国内网，别再重查）：gh-proxy 整包平均 18.42 MB/s（268.7MB / 13 秒），
    //   官方 Release 支持 Range(206)；**4 连接分块反而只有 0.13 MB/s（被限速）**
    //   → 所以是「单连接 + 断点续传 + 多源轮换」，不要做多线程。
    class GameRel
    {
        public string tag = "";
        public string url = "";
        public string err = "";
        public long bytes = 0;      // 0 = 未知（HEAD 没量到就当未知）
    }

    delegate void MoveProg(long done, long total, DateTime t0);

    static volatile bool gdlCancel;     // 用户点了「取消」→ 下载/解压循环里检查（保留断点）
    static int gdlPhase;                // 0=待机 1=下载中 2=解压中 3=完成 4=失败
    static long gdlDone, gdlTotal;
    static string gdlMsg = "就绪", gdlErr = "", gdlSrcName = "直连 GitHub";

    // 查原作者仓库的最新 Release（拿 tag + 附件的下载地址）
    static GameRel QueryGameRel()
    {
        GameRel r = new GameRel();
        try
        {
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
            HttpWebRequest rq = (HttpWebRequest)WebRequest.Create("https://api.github.com/repos/sganggs/Stronghold-Protocol/releases/latest");
            rq.Timeout = 12000; rq.UserAgent = "sp-launcher";
            using (WebResponse rp = rq.GetResponse())
            using (StreamReader sr = new StreamReader(rp.GetResponseStream()))
            {
                string j = sr.ReadToEnd();
                int i = j.IndexOf("\"tag_name\":\"");
                if (i >= 0) r.tag = j.Substring(i + 12, j.IndexOf('"', i + 12) - i - 12);
                int a = j.IndexOf("\"browser_download_url\":\"");
                if (a >= 0) r.url = j.Substring(a + 24, j.IndexOf('"', a + 24) - a - 24);
            }
            if (r.url.Length == 0) r.err = "这个 Release 里没有挂 zip 整合包";
        }
        catch (Exception ex) { r.err = ex.Message; }
        return r;
    }

    // 问一下文件多大（给用户一个预期；量不到就显示"未知"，不影响下载）
    static long HeadSize(string url)
    {
        try
        {
            HttpWebRequest rq = (HttpWebRequest)WebRequest.Create(url);
            rq.Method = "HEAD"; rq.Timeout = 8000; rq.UserAgent = "sp-launcher";
            using (WebResponse rp = rq.GetResponse()) return rp.ContentLength;
        }
        catch { return 0; }
    }

    static void Note(StringBuilder rep, string s)
    {
        if (rep != null) rep.AppendLine(s);
        Log(s);
    }

    // 本地文件源（只给 -gamedltest 离线自检用：沙箱里 C# 走 schannel 出不了网，只能拿本地文件验断点续传）
    static void CopyLocalResume(string path, string part, MoveProg cb)
    {
        long total = new FileInfo(path).Length;
        long have = 0;
        try { if (File.Exists(part)) have = new FileInfo(part).Length; } catch { }
        if (have > total) have = 0;
        using (FileStream src = new FileStream(path, FileMode.Open, FileAccess.Read))
        using (FileStream fs = new FileStream(part, have > 0 ? FileMode.Append : FileMode.Create))
        {
            src.Seek(have, SeekOrigin.Begin);
            byte[] buf = new byte[262144];
            long done = have;
            int n;
            while ((n = src.Read(buf, 0, buf.Length)) > 0)
            {
                if (gdlCancel) throw new OperationCanceledException();
                fs.Write(buf, 0, n);
                done += n;
                if (cb != null) cb(done, total, DateTime.Now);
                Thread.Sleep(2);   // 给 UI 留刷新时间
            }
        }
    }

    // 单次下载：断点续传（.part 的长度就是进度）。失败/取消都**保留** .part，下次接着下。
    static void DlResume(string url, string part, int timeoutSec, MoveProg cb)
    {
        if (url.StartsWith("local:", StringComparison.OrdinalIgnoreCase))
        { CopyLocalResume(url.Substring(6), part, cb); return; }
        long have = 0;
        try { if (File.Exists(part)) have = new FileInfo(part).Length; } catch { }
        ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
        HttpWebRequest rq = (HttpWebRequest)WebRequest.Create(url);
        rq.Timeout = 20000; rq.ReadWriteTimeout = 30000; rq.UserAgent = "sp-launcher";
        if (have > 0) { try { rq.AddRange(have); } catch { have = 0; } }
        DateTime t0 = DateTime.Now;
        using (WebResponse rp = rq.GetResponse())
        using (Stream rs = rp.GetResponseStream())
        {
            HttpWebResponse hr = rp as HttpWebResponse;
            bool resumed = have > 0 && hr != null && hr.StatusCode == HttpStatusCode.PartialContent;
            if (!resumed) have = 0;                              // 服务器没给 206 → 从头来
            long len = rp.ContentLength;
            long total = len > 0 ? have + len : 0;
            using (FileStream fs = new FileStream(part, resumed ? FileMode.Append : FileMode.Create))
            {
                byte[] buf = new byte[262144];
                long done = have;
                int n;
                while ((n = rs.Read(buf, 0, buf.Length)) > 0)
                {
                    if (gdlCancel) throw new OperationCanceledException();
                    fs.Write(buf, 0, n);
                    done += n;
                    if (cb != null) cb(done, total, t0);
                    if ((DateTime.Now - t0).TotalSeconds > timeoutSec) throw new TimeoutException("下载太慢（超过 " + timeoutSec + " 秒）");
                }
            }
        }
    }

    // 直连 + 三个镜像轮着试；断了就换源**接着下**（.part 不删）
    static void DlMulti(string url, string part, int directSecs, MoveProg cb)
    {
        string[] srcs = new string[] { url, MirrorUrl(GhMirrors[0], url), MirrorUrl(GhMirrors[1], url), MirrorUrl(GhMirrors[2], url) };
        string[] names = new string[] { "直连 GitHub", "gh-proxy.com", "ghfast.top", "ghproxy.net" };
        Exception last = null;
        for (int i = 0; i < srcs.Length; i++)
        {
            if (srcs[i] == null) continue;
            if (gdlCancel) throw new OperationCanceledException();
            gdlSrcName = names[i];
            try { DlResume(srcs[i], part, i == 0 ? directSecs : 900, cb); return; }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                last = ex;
                Log((i == 0 ? "直连下载中断：" : names[i] + " 下载中断：") + ex.Message + "（断点已保留，换源继续）");
            }
        }
        throw (last != null ? last : new Exception("下载失败"));
    }

    // 解压：官方 zip 是「单顶层目录 Stronghold-Protocol/…」而我们的目标目录**本身**就叫这个名，
    // 所以边解边剥壳。.NET 4.5 的 ExtractToDirectory 没有进度回调 → 自己遍历条目报进度。
    static int ExtractGameZip(string zip, string target, MoveProg cb)
    {
        int done = 0;
        using (System.IO.Compression.ZipArchive za = System.IO.Compression.ZipFile.OpenRead(zip))
        {
            int total = za.Entries.Count;
            string shell = null;
            if (total > 0)
            {
                string f = za.Entries[0].FullName.Replace('\\', '/');
                int s = f.IndexOf('/');
                if (s > 0)
                {
                    shell = f.Substring(0, s + 1);
                    foreach (System.IO.Compression.ZipArchiveEntry e in za.Entries)
                        if (!e.FullName.Replace('\\', '/').StartsWith(shell, StringComparison.Ordinal)) { shell = null; break; }
                }
            }
            foreach (System.IO.Compression.ZipArchiveEntry e in za.Entries)
            {
                if (gdlCancel) throw new OperationCanceledException();
                string rel = e.FullName.Replace('\\', '/');
                if (shell != null && rel.StartsWith(shell, StringComparison.Ordinal)) rel = rel.Substring(shell.Length);
                if (rel.Length == 0 || rel.EndsWith("/")) continue;
                if (rel.IndexOf("..", StringComparison.Ordinal) >= 0) continue;    // 防 zip 目录穿越
                string dst = Path.Combine(target, rel.Replace('/', '\\'));
                string dir = Path.GetDirectoryName(dst);
                if (dir != null && dir.Length > 0 && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                e.ExtractToFile(dst, true);
                done++;
                if (cb != null && (done % 25 == 0 || done == total)) cb(done, total, DateTime.MinValue);
            }
        }
        return done;
    }

    // 解压完的校验：这三个文件在就说明是完整的游戏本体（不是半截，也不是别的 zip）
    static string VerifyGame(string dir)
    {
        string[] need = new string[] { "server\\index.js", "public\\index.html", "package.json" };
        foreach (string n in need)
            if (!File.Exists(Path.Combine(dir, n))) return "缺少 " + n;
        return "";
    }

    // 游戏本体该放哪：启动器目录的**上一级** + Stronghold-Protocol（FindGame 就是按这个规则找的）
    static string GameTargetDir()
    {
        string up = "";
        try { up = Path.GetDirectoryName(AppDir); } catch { }
        if (up == null || up.Length == 0) up = AppDir;
        return Path.Combine(up, GameFolderName);
    }

    // 同步核心："" = 成功，否则是失败原因。rep 只在自检时用（记流水）
    static string GameDownloadCore(string url, long expectBytes, string target, StringBuilder rep)
    {
        string part = target + ".part.zip";
        try
        {
            gdlPhase = 1; gdlDone = 0; gdlTotal = expectBytes; gdlErr = "";
            gdlMsg = "正在连接…"; gdlSrcName = "直连 GitHub";
            Note(rep, "开始下载：" + url);
            DlMulti(url, part, 20, delegate(long d, long t, DateTime t0) { gdlDone = d; gdlTotal = t; });
            long got = 0;
            try { got = new FileInfo(part).Length; } catch { }
            Note(rep, "下载结束：落地 " + got + " 字节");
            if (gdlCancel) throw new OperationCanceledException();
            if (expectBytes > 0 && got != expectBytes)
                return "下载不完整（" + got + " / " + expectBytes + " 字节）。断点已保留，点「开始下载」接着下。";
            gdlPhase = 2; gdlDone = 0; gdlTotal = 0; gdlMsg = "正在解压…";
            // ★ 先解到旁边一个 .new 目录，**校验通过才换上去** —— 这样中途失败/断电也不会毁掉
            //   用户原来那份能玩的游戏本体（本项目的老规矩：先写临时文件，验完再替换）
            string staging = target + ".new";
            if (Directory.Exists(staging)) { Note(rep, "清掉上次的残留：" + staging); Directory.Delete(staging, true); }
            Directory.CreateDirectory(staging);
            int n = ExtractGameZip(part, staging, delegate(long d, long t, DateTime t0) { gdlDone = d; gdlTotal = t; });
            Note(rep, "解压完成：" + n + " 个文件");
            if (gdlCancel) throw new OperationCanceledException();
            string bad = VerifyGame(staging);
            if (bad.Length > 0)
            {
                try { Directory.Delete(staging, true); } catch { }
                return "解压出来的文件不完整：" + bad + "（原游戏本体没动，可以重试）";
            }
            string oldDir = target + ".old";
            if (Directory.Exists(oldDir)) { try { Directory.Delete(oldDir, true); } catch { } }
            if (Directory.Exists(target)) { Note(rep, "换上新本体（旧的先改名留着，换完再删）：" + target); Directory.Move(target, oldDir); }
            Directory.Move(staging, target);
            if (Directory.Exists(oldDir)) { try { Directory.Delete(oldDir, true); } catch { } }
            Note(rep, "已就位：" + target);
            try { File.Delete(part); } catch { }      // 成功才删那 290MB 的 zip
            gdlPhase = 3; gdlMsg = "完成"; gdlDone = 0; gdlTotal = 0;
            return "";
        }
        catch (OperationCanceledException)
        {
            gdlPhase = 4; gdlMsg = "已取消";
            return "已取消（断点已保留，下次点「开始下载」接着下）";
        }
        catch (Exception ex)
        {
            gdlPhase = 4; gdlMsg = "失败"; gdlErr = ex.Message;
            return ex.Message;
        }
    }

    static void Di(Form d, Action a)
    {
        try { if (d != null && d.IsHandleCreated) d.BeginInvoke(a); } catch { }
    }

    // ---------- 向导对话框 ----------
    static void GameDownloadFlow()
    {
        if (!SelfTestMode() && (gdlPhase == 1 || gdlPhase == 2))
        {
            MessageBox.Show("已经有一个下载任务在跑了，等它跑完（或取消）再开新的。", "下载游戏本体");
            return;
        }
        if (gdlPhase == 3 || gdlPhase == 4) { gdlPhase = 0; gdlErr = ""; }   // 上一轮的结束态别带到这一轮
        string target = GameTargetDir();
        string foundGame = FindGame(false);
        if (foundGame != null && string.Equals(Path.GetFileName(foundGame.TrimEnd('\\')), GameFolderName, StringComparison.OrdinalIgnoreCase))
            target = foundGame.TrimEnd('\\');      // 已经有本体 → 就地更新，别在别处又下一份
        bool hadGame = Directory.Exists(target);
        if (!SelfTestMode())
        {
            long free = -1;
            try { free = new DriveInfo(Path.GetPathRoot(target)).AvailableFreeSpace; } catch { }
            // 新装：压缩包(约 290MB) + 解压后(约 450MB)；覆盖安装还要多留一份旧的（解压校验通过才替换）
            long needMb = hadGame ? 1700 : 900;
            if (free >= 0 && free < needMb * 1024 * 1024)
            {
                MessageBox.Show("目标磁盘可用空间不够（现在 " + (free / 1073741824.0).ToString("0.0") + " GB，需要约 " +
                    (needMb / 1024.0).ToString("0.0") + " GB）。\n\n" +
                    "下载的压缩包和解压出来的文件都要落在这块盘上" +
                    (hadGame ? "；要覆盖现有游戏本体，还得再多留一份旧的（解压校验通过才替换，失败不动你原来那份）" : "") +
                    "。\n\n换个盘，或先清一清。\n目标：" + target, "空间不够");
                return;
            }
        }

        string localVer = GameLocalVersion();
        string t1 = "下载游戏本体";
        string s1 = "来源：原作者官方 Release（github.com/sganggs/Stronghold-Protocol）｜启动器只当下载器，不做任何素材镜像。";
        string s2 = "版权：游戏素材归鹰角网络 / Yostar，仅限私下非商业游玩，请勿公开转载或上传。";
        string s3 = "目标：" + target;
        string s4 = "版本：本机 " + (localVer.Length > 0 ? "v" + localVer : "未安装") + "　→　正在查询最新版…";
        string s5 = "状态：就绪";
        string s6 = "线路：直连 GitHub（太慢会自动切国内镜像）";
        Font ft = FTitle(14f, FontStyle.Bold);
        Font fs = Fui(9f);
        Font f9 = Fui(9.5f);
        Font f8 = Fui(8.5f);
        int contentW = 0;
        contentW = Math.Max(contentW, TextRenderer.MeasureText(t1, ft).Width);
        contentW = Math.Max(contentW, TextRenderer.MeasureText(s1, fs).Width);
        contentW = Math.Max(contentW, TextRenderer.MeasureText(s2, fs).Width);
        contentW = Math.Max(contentW, TextRenderer.MeasureText(s3, f9).Width);
        contentW = Math.Max(contentW, TextRenderer.MeasureText(s4, f9).Width);
        contentW = Math.Max(contentW, TextRenderer.MeasureText(s6, f8).Width);
        // 状态行是变化的 → 按最长可能的样子量（否则下到一半文字就会被窗口边切掉）
        contentW = Math.Max(contentW, TextRenderer.MeasureText("状态：正在下载 100%（268.7 / 268.7 MB）· 18.4 MB/s · 剩余 999 秒", f9).Width);
        contentW = Math.Max(contentW, TextRenderer.MeasureText("状态：正在解压 100%（11886 / 11886 个文件）", f9).Width);
        // ★ 量测值是【物理像素】（FontOf 已经把字号乘过系数了），而 B()/Sc() 要的是【设计单位】：
        //   不换回来就是 DPI² —— 实测 150% 下这个对话框被撑到 1564px 宽（本该 1088）。
        contentW = Lg(contentW) + 20;

        using (Form d = new Form())
        {
            d.Text = "下载游戏本体";
            d.ClientSize = new Size(Sc(contentW) + Sc(56), Sc(322));
            d.StartPosition = FormStartPosition.CenterParent;
            d.FormBorderStyle = FormBorderStyle.FixedDialog;
            d.MaximizeBox = false; d.MinimizeBox = false;
            d.BackColor = C_BG; d.ForeColor = C_TEXT;
            d.Font = f9;
            if (cliDlgCheck != null) CheckDialogLater(d, "下载向导");

            Label lt = new Label();
            lt.Text = t1; lt.Font = ft; lt.ForeColor = C_MINT; lt.AutoSize = true;
            lt.Location = new Point(Sc(28), Sc(20));
            d.Controls.Add(lt);

            Label l1 = new Label();
            l1.Text = s1; l1.Font = fs; l1.ForeColor = C_DIM; l1.AutoSize = true;
            l1.Location = new Point(Sc(28), Sc(58));
            d.Controls.Add(l1);

            Label l2 = new Label();
            l2.Text = s2; l2.Font = fs; l2.ForeColor = C_DIM; l2.AutoSize = true;
            l2.Location = new Point(Sc(28), Sc(80));
            d.Controls.Add(l2);

            Label l3 = new Label();
            l3.Text = s3; l3.Font = f9; l3.ForeColor = C_TEXT; l3.AutoSize = true;
            l3.Location = new Point(Sc(28), Sc(110));
            d.Controls.Add(l3);

            Label l4 = new Label();
            l4.Text = s4; l4.Font = f9; l4.ForeColor = C_TEXT; l4.AutoSize = true;
            l4.Location = new Point(Sc(28), Sc(134));
            d.Controls.Add(l4);

            ProgressBar pb = new ProgressBar();
            pb.SetBounds(Sc(28), Sc(166), Sc(contentW - 4), Sc(14));
            pb.Style = ProgressBarStyle.Continuous;
            pb.Maximum = 100;
            d.Controls.Add(pb);

            Label l5 = new Label();
            l5.Text = s5; l5.Font = f9; l5.ForeColor = C_TEXT; l5.AutoSize = true;
            l5.Location = new Point(Sc(28), Sc(188));
            d.Controls.Add(l5);

            Label l6 = new Label();
            l6.Text = s6; l6.Font = f8; l6.ForeColor = C_DIM; l6.AutoSize = true;
            l6.Location = new Point(Sc(28), Sc(212));
            d.Controls.Add(l6);

            Button dl = B(d, "开始下载", 28, 252, 160, 40, C_ON, null);
            Button cx = B(d, "关闭", 200, 252, 110, 40, C_OFF, null);
            dl.Font = f9; cx.Font = f9;

            GameRel rel = new GameRel();
            bool[] busyUi = new bool[] { false };

            if (cliDlgCheck == "gamedl")
            {
                // 自检模式：给一份假数据直接把界面填满（不联网、不下载），只为量布局
                rel.tag = "v0.1.1"; rel.bytes = 281792402;
                rel.url = "https://github.com/sganggs/Stronghold-Protocol/releases/download/v0.1.1/Stronghold-Protocol-v0.1.1.zip";
                l4.Text = "版本：本机 v0.1.0　→　最新 v0.1.1（268.7 MB）";
                l5.Text = "状态：正在下载 42%（113.0 / 268.7 MB）· 18.4 MB/s · 剩余 9 秒";
                l6.Text = "线路：gh-proxy.com（国内镜像加速）";
                dl.Text = "开始下载";
            }
            else
            {
                dl.Enabled = false;
                ThreadPool.QueueUserWorkItem(delegate
                {
                    GameRel r = QueryGameRel();
                    Di(d, delegate
                    {
                        rel.tag = r.tag; rel.url = r.url; rel.err = r.err;
                        if (r.err.Length > 0)
                        {
                            l4.Text = "版本：本机 " + (localVer.Length > 0 ? "v" + localVer : "未安装") + "　→　查询失败：" + r.err;
                            l5.Text = "状态：查不到最新版（GitHub 打不开？）—— 可以先点「开始下载」试试，或稍后再来";
                            dl.Enabled = true;
                        }
                        else
                        {
                            string lv = localVer.TrimStart('v', 'V');
                            bool newer = lv.Length == 0 || VerNum(r.tag) > VerNum(lv);
                            l4.Text = "版本：本机 " + (lv.Length > 0 ? "v" + lv : "未安装") + "　→　最新 " + r.tag + (r.bytes > 0 ? "（" + (r.bytes / 1048576.0).ToString("0.0") + " MB）" : "") +
                                      (newer ? "" : "　★ 已是最新，重新下载会覆盖现有本体");
                            l5.Text = newer ? "状态：可以开始下载了（支持断点续传，中途断了接着下）" : "状态：本机已是最新（要重装也可以点「开始下载」）";
                            dl.Text = newer ? "开始下载" : "重新下载";
                            dl.Enabled = true;
                        }
                    });
                    long sz = r.bytes;
                    if (sz <= 0 && r.url.Length > 0)
                    {
                        sz = HeadSize(GhMirrors[0] + r.url);
                        if (sz <= 0) sz = HeadSize(r.url);
                        long s2v = sz;
                        Di(d, delegate { if (s2v > 0 && rel.err.Length == 0) l4.Text = l4.Text + "（" + (s2v / 1048576.0).ToString("0.0") + " MB）"; });
                    }
                });
            }

            // 进度靠**轮询静态状态**刷新（后台线程不碰控件，省掉一堆 Invoke 麻烦）
            long spdLast = 0; DateTime spdT = DateTime.Now; double spd = 0;
            System.Windows.Forms.Timer tm = new System.Windows.Forms.Timer();
            tm.Interval = 300;
            tm.Tick += delegate
            {
                if (gdlPhase == 1 || gdlPhase == 2)
                {
                    if (!busyUi[0]) { busyUi[0] = true; dl.Enabled = false; dl.Text = "下载中…"; cx.Text = "取消"; }
                    DateTime now = DateTime.Now;
                    if ((now - spdT).TotalMilliseconds >= 600)
                    {
                        double inst = (gdlDone - spdLast) / 1048576.0 / Math.Max(0.001, (now - spdT).TotalSeconds);
                        spd = spd <= 0 ? inst : spd * 0.6 + inst * 0.4;
                        spdLast = gdlDone; spdT = now;
                    }
                    if (gdlPhase == 1)
                    {
                        int pct = gdlTotal > 0 ? (int)(gdlDone * 100 / gdlTotal) : 0;
                        if (pct > 100) pct = 100;
                        pb.Value = pct;
                        string eta = "";
                        if (spd > 0.05 && gdlTotal > gdlDone) eta = " · 剩余 " + (int)((gdlTotal - gdlDone) / 1048576.0 / spd) + " 秒";
                        l5.Text = "状态：正在下载 " + pct + "%（" + (gdlDone / 1048576.0).ToString("0.0") + " / " +
                                  (gdlTotal > 0 ? (gdlTotal / 1048576.0).ToString("0.0") : "?") + " MB）· " +
                                  spd.ToString("0.0") + " MB/s" + eta;
                    }
                    else
                    {
                        int pct = gdlTotal > 0 ? (int)(gdlDone * 100 / gdlTotal) : 0;
                        if (pct > 100) pct = 100;
                        pb.Value = pct;
                        l5.Text = "状态：正在解压 " + pct + "%（" + gdlDone + " / " + gdlTotal + " 个文件）";
                    }
                    l6.Text = "线路：" + gdlSrcName + (gdlPhase == 2 ? "（已下载完，正在写盘）" : "");
                }
                else if (busyUi[0])
                {
                    // 收尾：成功 / 失败 / 取消
                    busyUi[0] = false;
                    dl.Enabled = true; cx.Text = "关闭";
                    if (gdlPhase == 3)
                    {
                        pb.Value = 100;
                        l5.Text = "状态：完成！游戏本体已就绪：" + target;
                        l5.ForeColor = C_MINT;
                        dl.Text = "关闭";
                        dl.Enabled = true;
                        gamePath = target; cachedGame = target; SaveCfg();
                        if (setPathLabel != null) { setPathLabel.Text = target; setPathLabel.ForeColor = C_TEXT; }
                        Log("游戏本体已下载并校验通过：" + target);
                        try { tm.Stop(); tm.Dispose(); } catch { }
                    }
                    else
                    {
                        l5.Text = "状态：失败 —— " + gdlErr;
                        l5.ForeColor = Color.FromArgb(240, 170, 90);
                        dl.Text = "重试（接着下）";
                    }
                }
            };
            tm.Start();

            dl.Click += delegate
            {
                if (gdlPhase == 3) { d.Close(); return; }
                if (gdlPhase == 1 || gdlPhase == 2) return;
                if (rel.url.Length == 0)
                {
                    l5.Text = "状态：还没拿到下载地址（网络不通？）—— 稍等再点一次";
                    return;
                }
                gdlCancel = false;
                pb.Value = 0; l5.ForeColor = C_TEXT;
                ThreadPool.QueueUserWorkItem(delegate
                {
                    string err = GameDownloadCore(rel.url, rel.bytes, target, null);
                    if (err.Length > 0 && gdlErr.Length == 0) gdlErr = err;
                    if (err.Length > 0 && gdlPhase == 4) gdlMsg = err;
                });
            };
            cx.Click += delegate
            {
                if (gdlPhase == 1 || gdlPhase == 2) { gdlCancel = true; cx.Text = "正在取消…"; cx.Enabled = false; return; }
                d.Close();
            };
            d.FormClosing += delegate
            {
                try { tm.Stop(); tm.Dispose(); } catch { }
                if (gdlPhase == 1 || gdlPhase == 2) gdlCancel = true;   // 关了窗口也让后台停下来（断点保留）
            };
            d.ShowDialog();
        }
    }

    // -gamedltest:<zip路径 或 local:路径 或 http地址>：离线跑一遍「下载(可断点) → 解压剥壳 → 校验」并写报告。
    // ★ 沙箱里 C# 走 schannel 出不了网，所以本机只能用「先伪造半截断点 + 本地源接着下」来验续传逻辑；
    //   真实 GitHub 下载那一段必须在用户真机上跑一次才算数。
    static void GameDlTest(string arg)
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("游戏本体下载/解压 离线自检  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + EnvLine());
        string baseDir = Path.Combine(Path.GetTempPath(), "sp-gamedltest");
        try { if (Directory.Exists(baseDir)) Directory.Delete(baseDir, true); } catch { }
        Directory.CreateDirectory(baseDir);
        string target = Path.Combine(baseDir, GameFolderName);
        string part = target + ".part.zip";
        string url = arg;
        long expect = 0;
        if (File.Exists(arg))
        {
            url = "local:" + arg;
            expect = new FileInfo(arg).Length;
            // 伪造一个"下到一半断了"的现场，专门验断点续传
            byte[] all = File.ReadAllBytes(arg);
            int half = all.Length / 2;
            using (FileStream fs = new FileStream(part, FileMode.Create)) fs.Write(all, 0, half);
            sb.AppendLine("已伪造断点：" + half + " / " + all.Length + " 字节 → 期望从 " + half + " 处接着下");
        }
        sb.AppendLine("源=" + url);
        sb.AppendLine("目标=" + target);
        string err = GameDownloadCore(url, expect, target, sb);
        sb.AppendLine("结果=" + (err.Length == 0 ? "成功" : "失败：" + err));
        sb.AppendLine("阶段=" + gdlPhase);
        long partLeft = 0;
        try { if (File.Exists(part)) partLeft = new FileInfo(part).Length; } catch { }
        sb.AppendLine("残留断点=" + partLeft + " 字节（成功时应为 0）");
        if (Directory.Exists(target))
        {
            int files = 0; long bytes = 0;
            try
            {
                foreach (string f in Directory.GetFiles(target, "*", SearchOption.AllDirectories)) { files++; bytes += new FileInfo(f).Length; }
            }
            catch { }
            sb.AppendLine("落地产物=" + files + " 个文件 / " + bytes + " 字节");
            sb.AppendLine("校验=" + (VerifyGame(target).Length == 0 ? "server\\index.js + public\\index.html + package.json 齐全" : VerifyGame(target)));
            sb.AppendLine("剥壳=" + (File.Exists(Path.Combine(target, "server\\index.js")) ? "已剥掉顶层 Stronghold-Protocol/" : "★ 壳没剥对"));
        }
        else sb.AppendLine("落地产物=★ 目标目录没建起来");
        sb.AppendLine("总判定=" + (err.Length == 0 && partLeft == 0 && Directory.Exists(target) && VerifyGame(target).Length == 0 ? "PASS" : "FAIL"));
        string rp = Path.Combine(LogDirRoot(), "下载向导检查.txt");
        try { File.AppendAllText(rp, sb.ToString() + "\r\n", Encoding.UTF8); } catch { }
        try { Directory.Delete(baseDir, true); } catch { }
        Console.WriteLine(sb.ToString());
    }

    // 下载更新包 → 解出启动器 → 延迟替换 → 自我重启
    static void UpdateNow()
    {
        if (!updAvail || updZip.Length == 0) return;
        if (busy) { MessageBox.Show("正在开房 / 单机中，先关掉房间再更新。", "更新"); return; }
        if (MessageBox.Show(
            "发现新版启动器 " + updTag + "（当前 v" + Version + "）。\n\n" +
            "· 下载约 3.5 MB（启动器 + 组网引擎）\n" +
            "· 国内直连可能较慢，启动器会自动切换国内镜像站加速\n" +
            "· 你的游戏本体、房间配置、日志都不会动\n" +
            "· 更新完启动器会自动重启\n\n现在更新吗？",
            "更新到 " + updTag, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        Log("开始下载更新包：" + updZip);
        FlashStatus("● 正在下载更新 " + updTag + "（3.5 MB）", C_MINT);
        StatusBusy("正在下载更新 0%（连接中）…");
        ThreadPool.QueueUserWorkItem(delegate
        {
            try
            {
                string tmp = Path.Combine(Path.GetTempPath(), "sp-launcher-upd");
                try { if (Directory.Exists(tmp)) Directory.Delete(tmp, true); } catch { }
                Directory.CreateDirectory(tmp);
                string zip = Path.Combine(tmp, "update.zip");
                // 先直连 20 秒；太慢就自动换国内镜像站重下（都走同一个下载循环，带进度）
                DownloadUpdate(updZip, zip, 20);
                StatusBusy("下载完成，正在解压…");
                string ex = Path.Combine(tmp, "ex");
                System.IO.Compression.ZipFile.ExtractToDirectory(zip, ex);
                string newExe = null;
                foreach (string f in Directory.GetFiles(ex, "*.exe", SearchOption.AllDirectories))
                    if (Path.GetFileName(f).IndexOf("启动器") >= 0) { newExe = f; break; }
                if (newExe == null) throw new Exception("更新包里没找到启动器 exe");
                string cur = Application.ExecutablePath;
                // 延迟替换脚本：等本进程退出 → 复制新的覆盖旧的 → 重启
                string bat = Path.Combine(tmp, "更新.cmd");
                string[] lines = new string[] {
                    "@echo off",
                    "chcp 65001 >nul",
                    "title 正在更新启动器…",
                    "echo 等待启动器退出…",
                    "set /a n=0",
                    ":wait",
                    "tasklist /fi \"imagename=卫戍协议启动器.exe\" 2>nul | find /i \"卫戍协议启动器.exe\" >nul",
                    "if not errorlevel 1 (",
                    "  set /a n+=1",
                    "  if %n% gtr 60 goto force",
                    "  ping -n 2 127.0.0.1 >nul",
                    "  goto wait",
                    ")",
                    "goto copy",
                    ":force",
                    "echo 旧进程还在，强制结束…",
                    "taskkill /f /im \"卫戍协议启动器.exe\" >nul 2>nul",
                    "ping -n 3 127.0.0.1 >nul",
                    ":copy",
                    "echo 正在替换启动器…",
                    "for /l %%i in (1,1,10) do (",
                    "  copy /y \"" + newExe + "\" \"" + cur + "\" >nul 2>nul",
                    "  if not errorlevel 1 goto done",
                    "  ping -n 2 127.0.0.1 >nul",
                    ")",
                    "echo [x] 替换失败，请手动把新启动器复制过去。",
                    "pause",
                    "exit /b 1",
                    ":done",
                    "echo 更新完成，正在重启启动器…",
                    "ping -n 3 127.0.0.1 >nul",
                    "cd /d \"" + Path.GetDirectoryName(cur) + "\"",
                    "start \"\" \"" + cur + "\"",
                    "ping -n 4 127.0.0.1 >nul",
                    // 重启要确认：真起来了就退出；没起来就明说，别让用户以为是"更新完就没反应"
                    "tasklist /fi \"imagename=卫戍协议启动器.exe\" 2>nul | find /i \"卫戍协议启动器.exe\" >nul",
                    "if errorlevel 1 (",
                    "  echo.",
                    "  echo [!] 新版已装好，但自动重启没成功。",
                    "  echo     请手动双击： " + cur,
                    "  echo.",
                    "  pause",
                    ")",
                    "rd /s /q \"" + tmp + "\" >nul 2>nul",
                    "exit /b 0"
                };
                File.WriteAllLines(bat, lines, new UTF8Encoding(false));
                Log("更新就绪，交给脚本替换并重启");
                ProcessStartInfo si = new ProcessStartInfo("cmd.exe", "/c \"" + bat + "\"");
                si.UseShellExecute = false;
                si.CreateNoWindow = true;
                Process.Start(si);
                Ui(delegate { Application.Exit(); });
            }
            catch (Exception ex)
            {
                Log("更新失败：" + ex.Message);
                Ui(delegate
                {
                    ClearBanner();
                    MessageBox.Show("更新失败：" + ex.Message +
                        "\n\n可以手动更新：把「卫戍协议-轻量包.zip」解压后覆盖启动器目录即可。\n（设置 → 打开日志目录 里能看到详细日志）", "更新失败");
                });
            }
        });
    }

    static void CheckUpdate()
    {
        if (updateLine == null) return;
        updateLine.Text = "检查中…";
        ThreadPool.QueueUserWorkItem(delegate
        {
            string msg;
            try
            {
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
                HttpWebRequest rq = (HttpWebRequest)WebRequest.Create("https://api.github.com/repos/" + Repo + "/releases/latest");
                rq.Timeout = 12000;
                rq.UserAgent = "sp-launcher";
                using (WebResponse rp = rq.GetResponse())
                using (StreamReader sr = new StreamReader(rp.GetResponseStream()))
                {
                    string j = sr.ReadToEnd();
                    int i = j.IndexOf("\"tag_name\":\"");
                    string tag = i >= 0 ? j.Substring(i + 12, j.IndexOf('"', i + 12) - i - 12) : "?";
                    msg = "最新版本：" + tag + "（点这里打开下载页）";
                }
            }
            catch (Exception ex) { msg = "检查失败（网络问题）：" + ex.Message; }
            Ui(delegate { updateLine.Text = msg; });
        });
    }


    // 打包分享：先选打包内容（尺寸按文字实际宽度计算，避免内容撑破边框）
    static void PackDialog()
    {
        // ★ v1.2.1 起：两种包都**不再含游戏本体**（版权上彻底干净）。
        //   "开房包"比轻量包多的只是便携版 node.exe —— 给想自己开房/单机的朋友；
        //   游戏本体一律由他自己在启动器里点「下载游戏本体…」从原作者官方地址取（实测十几秒）。
        string nodeSrc = FindSystemNode();
        bool canNode = (nodeSrc != null);
        long nodeMb = 0;
        try { if (canNode) nodeMb = new FileInfo(nodeSrc).Length / (1024 * 1024); } catch { }
        long pkgMb = canNode ? ((long)(nodeMb * 0.37) + 4) : 0;   // 实测：89MB 的 node.exe → 开房包 36.8MB
        string t1 = "要把什么打包给粥友？";
        string s1 = "● 轻量包（推荐）：只有启动器 + 组网引擎，约 3.5 MB —— 给只想加入别人房间的人。";
        string s2 = "● 开房包：再加一份便携版 Node.js —— 给想自己开房 / 玩单机的朋友。";
        string b1t = "轻量包（只有启动器 + 组网引擎，约 3.5 MB）";
        string b2t = canNode ? ("开房包（启动器 + 引擎 + 便携版 Node.js，约 " + pkgMb + " MB）") : "开房包（本机没找到 Node.js，装一个再打）";
        string h1 = "两种包都不含任何游戏素材，可以放心发；朋友那边缺游戏本体时，启动器会带他从原作者官方地址下载（实测十几秒）。";
        string h2 = "提示：开房包解压即可开房，朋友不用自己装 Node.js。" +
                    (canNode ? "" : "（本机没找到 Node.js，所以开房包暂时点不了）");

        Font ft = FTitle(14f, FontStyle.Bold);
        Font fs = Fui(9f);
        Font fb = Fui(10f);
        int wTitle = TextRenderer.MeasureText(t1, ft).Width;
        int wSub = Math.Max(TextRenderer.MeasureText(s1, fs).Width, TextRenderer.MeasureText(s2, fs).Width);
        int wBtn = Math.Max(TextRenderer.MeasureText(b1t, fb).Width, TextRenderer.MeasureText(b2t, fb).Width);
        int wHint = Math.Max(TextRenderer.MeasureText(h1, fs).Width, TextRenderer.MeasureText(h2, fs).Width);
        // ★ contentW 必须是"设计单位"：量测值已经是物理像素（字号里乘过系数），所以要用 Lg() 换回来。
        //   这里原来是直接当设计单位用、又交给 B() 再 Sc 一次 = 双重缩放（实测 150% 下对话框宽 1641px，
        //   1024/1366 那种屏直接就顶出屏幕了）。
        int contentW = Lg(Math.Max(Math.Max(wTitle, wSub), Math.Max(wBtn, wHint))) + 20;
        int dlgW = Sc(contentW) + Sc(56);
        int dlgH = Sc(334);   // 设计高度 = 20+38+46+58+58+62+34+18（见下面 y 的累加）

        using (Form d = new Form())
        {
            d.Text = "打包分享";
            d.ClientSize = new Size(dlgW, dlgH);
            if (cliDlgCheck != null) CheckDialogLater(d, "打包");
            d.StartPosition = FormStartPosition.CenterParent;
            d.FormBorderStyle = FormBorderStyle.FixedDialog;
            d.MaximizeBox = false; d.MinimizeBox = false;
            d.BackColor = C_BG; d.ForeColor = C_TEXT;
            d.Font = Fui(9.5f);

            int y = 20;
            Label lt = new Label();
            lt.Text = t1; lt.Font = ft; lt.ForeColor = C_MINT; lt.AutoSize = true;
            lt.Location = new Point(Sc(28), Sc(y));
            d.Controls.Add(lt);
            y += 38;

            Label ls = new Label();
            ls.Text = s1 + "\n" + s2; ls.Font = fs; ls.ForeColor = C_DIM; ls.AutoSize = true;
            ls.Location = new Point(Sc(28), Sc(y));
            d.Controls.Add(ls);
            y += 46;

            Button b1 = B(d, b1t, 28, y, contentW, 46, C_ON, null);
            b1.Font = fb;
            y += 58;

            Button b2 = B(d, b2t, 28, y, contentW, 46, C_OFF, null);
            b2.Font = fb;
            if (!canNode) b2.Enabled = false;       // 开房包：本机没有 Node.js 就打不了
            y += 58;

            Label lh = new Label();
            lh.Text = h1 + "\n" + h2; lh.Font = fs; lh.ForeColor = C_DIM; lh.AutoSize = true;
            lh.Location = new Point(Sc(28), Sc(y));
            d.Controls.Add(lh);
            y += 62;

            Button cancel = B(d, "取消", 28, y, 120, 34, C_OFF, null);
            cancel.Click += delegate { d.Close(); };

            b1.Click += delegate { d.Close(); PackFlow(false); };
            b2.Click += delegate { d.Close(); PackFlow(true); };

            d.ShowDialog();
        }
    }

    // 真正执行打包：withNode 决定是否带上便携版 Node.js（= "开房包"）。★ 两种包都**不含游戏本体**。
    static void PackFlow(bool withNode)
    {
        string zipPath;
        if (cliPackOut != null) zipPath = cliPackOut;      // -pack: 测试/发版钩子：不弹保存框，无人值守打包
        else
        {
            SaveFileDialog sf = new SaveFileDialog();
            sf.Title = "保存分发包";
            sf.Filter = "压缩包 (*.zip)|*.zip";
            sf.FileName = withNode ? "卫戍协议-开房包.zip" : "卫戍协议-轻量包.zip";
            try { sf.InitialDirectory = Path.GetDirectoryName(AppDir); } catch { }
            if (sf.ShowDialog() != DialogResult.OK) return;
            zipPath = sf.FileName;
        }
        Log("开始打包" + (withNode ? "（含便携版 Node）" : "（仅启动器）") + "…");
        if (cliPackOut == null) StatusBusy("正在打包，请稍候…");
        Action body = delegate
        {
            try
            {
                if (File.Exists(zipPath)) File.Delete(zipPath);
                using (FileStream fs = new FileStream(zipPath, FileMode.Create))
                using (ZipArchive zip = new ZipArchive(fs, ZipArchiveMode.Create))
                {
                    // 必须排除：启动器配置.txt 里有本机 o2p 节点名，带给粥友会撞名把房主顶下线；openp2p 的 config.json 同理
                    AddDir(zip, AppDir, "卫戍协议启动器", new string[] { "logs", "dist", "_uninstall.cmd", "启动器配置.txt", "openp2p\\config.json", "openp2p\\log", "easytier" });
                    // ★ v1.2.1 起**不再把游戏本体打进包里**（版权上彻底干净，作者那边也没得挑）：
                    //   游戏由朋友自己在启动器里点「下载游戏本体…」从原作者官方地址取（实测十几秒）。
                    //   开房包只多一份便携版 node.exe —— 解压就能开房，不用自己装 Node（启动器会优先用它）
                    if (withNode)
                    {
                        string nodeSrc = FindSystemNode();
                        if (nodeSrc != null)
                        {
                            zip.CreateEntryFromFile(nodeSrc, "卫戍协议启动器/node/node.exe", CompressionLevel.Optimal);
                            Log("已把 Node.js 打进开房包：" + nodeSrc);
                        }
                        else Log("本机没找到 node.exe，开房包不带 Node（朋友开房时会被引导安装）");
                    }
                    // 打包根目录放两个"粥友入口"文件，避免对方解压后找不到该点哪个
                    AddTextEntry(zip, "① 双击这里开始.bat", StarterBat());
                    AddTextEntry(zip, "② 使用说明-先看这个.txt", ReadmeTxt(withNode));
                    // ★ ③④ 是"换台电脑验证界面"用的（Win7 那台就靠它）。2026-10-05 发现这两个
                    //   只存在于当时手工打的包里、源码里没有 —— 结果用源码正式打包会把它俩弄丢，
                    //   而设置页还写着"双击解压目录里的 ④ 一键自检.bat"。现在补进源码，别再丢。
                    AddTextEntry(zip, "③ 界面自测.txt", SelfTestTxt());
                    AddTextEntry(zip, "④ 一键自检.bat", SelfTestBat());
                    // 许可文件：分发了 openp2p 的二进制，MIT 要求随附版权声明与许可全文；两种包都再补一份版权说明
                    AddTextEntry(zip, "第三方许可-openp2p-MIT.txt", OpenP2pLicenseTxt());
                    AddTextEntry(zip, "版权与来源-必读.txt", NoticeTxt(withNode));
                }
                long mb = new FileInfo(zipPath).Length / (1024 * 1024);
                Log("打包完成：" + zipPath + "（" + mb + " MB）");
                if (cliPackOut != null) return;      // 无人值守：不碰界面
                FlashStatus("● 打包完成（" + mb + " MB）", C_MINT);
                Ui(delegate
                {
                    MessageBox.Show("打包完成！\n\n" + zipPath + "\n大小约 " + mb + " MB\n\n发给粥友，解压后双击里面的启动器即可。", "完成");
                });
            }
            catch (Exception ex)
            {
                Log("打包失败：" + ex.Message);
                if (cliPackOut == null) Ui(delegate { PulseAlert(statusLine, "○ 打包失败，看日志", Color.FromArgb(240, 140, 120)); });
            }
        };
        if (cliPackOut != null) { body(); return; }   // 无人值守：同步跑完，调用方好判断成败
        ThreadPool.QueueUserWorkItem(delegate { body(); });
    }
    // ③ 界面自测.txt：告诉朋友这包里有自检工具
    static string SelfTestTxt()
    {
        return
"想确认界面有没有被裁 / 被挡：双击本目录的「④ 一键自检.bat」。\r\n" +
"它会跑四个页面 + 五个对话框 + 交互状态 + 断线恢复自检，并把全部报告打印出来。\r\n" +
"报告第一行会写明：系统 / 是否 64 位 / .NET 版本 / 实际用的字体族。\r\n" +
"有 FAIL 的行会写出控件名和坐标，把那段发给作者即可。\r\n";
    }

    // ④ 一键自检.bat：Win7 那台不用敲命令，双击就行（UTF-8 无 BOM + chcp 65001，实测 cmd 下中文正常）
    static string SelfTestBat()
    {
        return
"@echo off\r\n" +
"chcp 65001 >nul\r\n" +
"cd /d \"%~dp0\"\r\n" +
"set \"EXE=%CD%\\卫戍协议启动器\\卫戍协议启动器.exe\"\r\n" +
"if not exist \"%EXE%\" (echo [x] 没找到启动器，请先解压完整。 & pause & exit /b 1)\r\n" +
"echo ===== 一、界面自检（四个页面）=====\r\n" +
"\"%EXE%\" -nosplash -page:0 -layoutcheck\r\n" +
"\"%EXE%\" -nosplash -page:1 -layoutcheck\r\n" +
"\"%EXE%\" -nosplash -page:2 -layoutcheck\r\n" +
"\"%EXE%\" -nosplash -page:2 -settab:2 -layoutcheck\r\n" +
"echo.\r\n" +
"echo ===== 二、对话框自检（安装 / 令牌 / 打包 / 闪屏 / 下载向导）=====\r\n" +
"\"%EXE%\" -nosplash -dlgcheck:install\r\n" +
"\"%EXE%\" -nosplash -dlgcheck:token\r\n" +
"\"%EXE%\" -nosplash -dlgcheck:pack\r\n" +
"\"%EXE%\" -nosplash -dlgcheck:splash\r\n" +
"\"%EXE%\" -nosplash -dlgcheck:gamedl\r\n" +
"echo.\r\n" +
"echo ===== 三、交互与恢复自检 =====\r\n" +
"\"%EXE%\" -nosplash -uishake\r\n" +
"\"%EXE%\" -tunnelsim\r\n" +
"echo.\r\n" +
"for %%f in (0 1 2) do (\r\n" +
"  echo ---- logs\\布局检查-页%%f.txt ----\r\n" +
"  if exist \"logs\\布局检查-页%%f.txt\" type \"logs\\布局检查-页%%f.txt\"\r\n" +
")\r\n" +
"for %%d in (安装 令牌 打包 闪屏 下载向导) do (\r\n" +
"  echo ---- logs\\对话框检查-%%d.txt ----\r\n" +
"  if exist \"logs\\对话框检查-%%d.txt\" type \"logs\\对话框检查-%%d.txt\"\r\n" +
")\r\n" +
"echo ---- logs\\界面震荡检查.txt ----\r\n" +
"if exist \"logs\\界面震荡检查.txt\" type \"logs\\界面震荡检查.txt\"\r\n" +
"echo ---- logs\\断线恢复检查.txt ----\r\n" +
"if exist \"logs\\断线恢复检查.txt\" type \"logs\\断线恢复检查.txt\"\r\n" +
"echo.\r\n" +
"echo 每行 OK 就是没裁；有 FAIL 会写出具体控件和坐标，发给作者即可。\r\n" +
"pause\r\n";
    }

    // 往 zip 里写一个纯文本条目
    static void AddTextEntry(ZipArchive zip, string entryName, string content)
    {
        ZipArchiveEntry e = zip.CreateEntry(entryName, CompressionLevel.Optimal);
        using (Stream s = e.Open())
        using (StreamWriter w = new StreamWriter(s, new UTF8Encoding(false)))
            w.Write(content);
    }

    // openp2p 的 MIT 许可全文：打包分发时必须随附（MIT 对二进制再分发的硬要求），别删
    static string OpenP2pLicenseTxt()
    {
        return
"第三方组件许可：openp2p（MIT）\r\n" +
"============================================================\r\n" +
"\r\n" +
"本包内 openp2p\\openp2p.exe 来自 https://github.com/openp2p-cn/openp2p ，以 MIT 许可证分发。\r\n" +
"卫戍协议启动器只是把它当独立进程调用，没有修改它的二进制。\r\n" +
"\r\n" +
"依据 MIT 许可证，分发本包时须随附下面的版权声明与许可全文。\r\n" +
"\r\n" +
"------------------------------------------------------------\r\n" +
"MIT License\r\n" +
"\r\n" +
"Copyright (c) 2021 OpenP2P.cn\r\n" +
"\r\n" +
"Permission is hereby granted, free of charge, to any person obtaining a copy\r\n" +
"of this software and associated documentation files (the \"Software\"), to deal\r\n" +
"in the Software without restriction, including without limitation the rights\r\n" +
"to use, copy, modify, merge, publish, distribute, sublicense, and/or sell\r\n" +
"copies of the Software, and to permit persons to whom the Software is\r\n" +
"furnished to do so, subject to the following conditions:\r\n" +
"\r\n" +
"The above copyright notice and this permission notice shall be included in all\r\n" +
"copies or substantial portions of the Software.\r\n" +
"\r\n" +
"THE SOFTWARE IS PROVIDED \"AS IS\", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR\r\n" +
"IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,\r\n" +
"FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE\r\n" +
"AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER\r\n" +
"LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,\r\n" +
"OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE\r\n" +
"SOFTWARE.\r\n";
    }

    // 本包的版权与来源说明：谁能传、谁不能传、素材归谁，写在包里省得朋友再问
    static string NoticeTxt(bool withNode)
    {
        return
"卫戍协议启动器　版权与来源说明\r\n" +
"============================================================\r\n" +
"生成时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + "　启动器 v" + Version + "\r\n" +
"包类型：" + (withNode ? "开房包（启动器 + 组网引擎 + 便携版 Node.js）" : "轻量包（只有启动器 + 组网引擎）") + "\r\n" +
"\r\n" +
"【这个包里有什么 / 分别归谁】\r\n" +
"· 卫戍协议启动器、openp2p 组网引擎：启动器是本项目自制（GPL-3.0-or-later）；\r\n" +
"  openp2p 来自 github.com/openp2p-cn/openp2p（MIT），全文见同目录『第三方许可-openp2p-MIT.txt』。\r\n" +
(withNode ? "· Node.js：来自 nodejs.org（MIT），仅房主开房/单机时需要，本包自带便携版。\r\n"
          : "· Node.js：仅房主开房/单机时需要，不在本包内，自行安装或用便携版。\r\n") +
"· ★ 本包不含任何游戏本体与游戏素材。游戏本体由使用者自己在启动器里，从原作者\r\n" +
"  （github.com/sganggs/Stronghold-Protocol，代码 GPL-3.0-or-later）的官方 Release 下载。\r\n" +
"  游戏素材（美术 / 音乐 / 音效 / 文本 / 游戏数据）版权归上海鹰角网络 / Yostar 及其授权方所有，\r\n" +
"  不在 GPL 范围内，本项目无权就它们授权给任何人。\r\n" +
"\r\n" +
"【使用限制】\r\n" +
"仅供学习、研究与个人非商业娱乐。禁止任何形式的盈利：出售或付费分发、收费开服、付费房间、\r\n" +
"植入广告、打赏赞助众筹、打包进收费产品，统统不行。\r\n" +
"\r\n" +
"【声明】\r\n" +
"非官方同人作品，与上海鹰角网络科技有限公司、Yostar 及其关联方没有任何关系，未获其授权或认可。\r\n" +
"按「原样」提供，不附带任何担保。\r\n";
    }

    // 一键启动批处理：在解压出来的目录里双击即可，不用进子文件夹找 exe
    // 一键启动批处理：双击即可运行；首次运行自动在桌面建快捷方式（下次直接从桌面进）
    static string StarterBat()
    {
        return "@echo off\r\n" +
               "chcp 65001 >nul\r\n" +
               "rem 卫戍协议：盟约 启动器 —— 双击本文件即可\r\n" +
               "cd /d \"%~dp0\"\r\n" +
               "set \"EXE=%CD%\\卫戍协议启动器\\卫戍协议启动器.exe\"\r\n" +
               "if not exist \"%EXE%\" (\r\n" +
               "  echo [x] 没找到 卫戍协议启动器\\卫戍协议启动器.exe\r\n" +
               "  echo     请确认解压完整，也不要单独把 exe 拖到别处去。\r\n" +
               "  pause\r\n" +
               "  exit /b 1\r\n" +
               ")\r\n" +
               "\r\n" +
               "rem --- 首次运行时，自动在桌面建一个快捷方式 ---\r\n" +
               "set \"LNK=%USERPROFILE%\\Desktop\\卫戍协议启动器.lnk\"\r\n" +
               "if not exist \"%LNK%\" (\r\n" +
               "  echo 正在创建桌面快捷方式，以后可以从桌面直接打开...\r\n" +
               "  powershell -NoProfile -ExecutionPolicy Bypass -Command \"$w=New-Object -ComObject WScript.Shell; $s=$w.CreateShortcut('%LNK%'); $s.TargetPath='%EXE%'; $s.WorkingDirectory='%CD%\\卫戍协议启动器'; $s.Description='卫戍协议：盟约 联机启动器'; try{ $ic=[System.Drawing.Icon]::ExtractAssociatedIcon('%EXE%'); $ic.Save($env:TEMP+'\\sp.ico'); $s.IconLocation=$env:TEMP+'\\sp.ico' }catch{}; $s.Save()\" >nul 2>nul\r\n" +
               "  if exist \"%LNK%\" (echo 已创建：桌面 \u2192 卫戍协议启动器) else (echo [i] 快捷方式没建成功，可以直接运行 exe，或右键手动发送到桌面)\r\n" +
               ")\r\n" +
               "\r\n" +
               "start \"\" \"%EXE%\"\r\n";
    }
    static string ReadmeTxt(bool withNode)
    {
        string s = "";
        s += "卫戍协议：盟约 —— 使用说明\r\n";
        s += "==================================================\r\n\r\n";
        s += "【怎么打开】\r\n";
        s += "  双击本目录里的  「① 双击这里开始.bat」\r\n";
        s += "  （也可以进 卫戍协议启动器 文件夹，双击 卫戍协议启动器.exe）\r\n";
        s += "  首次打开会在桌面自动建一个快捷方式，以后从桌面图标进就行。\r\n\r\n";
        s += "【只自己玩】\r\n";
        s += "  启动器主页 → 点「▶ 开始游戏（单机）」→ 等待浏览器自动打开\r\n\r\n";
        s += "【和粥友联机】\r\n";
        s += "  房主：联机开黑 → 「① 我开房（当房主）」→ 点绿色大按钮「开房并复制邀请」\r\n";
        s += "        然后到群里粘贴发送（内容已自动复制好）\r\n";
        s += "        进游戏后点「同盟模拟 → 创建同盟」，游戏会给一串 4 位密钥，也发到群里\r\n";
        s += "  粥友：联机开黑 → 「② 加入粥友的房间」→ 把房主发来的链接整个粘进去 → 点「进房」\r\n";
        s += "        进游戏后点「同盟模拟 → 加入同盟」，粘贴房主给的 4 位密钥\r\n\r\n";
        s += "【第一次玩会很久，是正常的】\r\n";
        s += "  第一次进游戏要从房主电脑下载约 250MB 素材：页面会转圈、素材一张张慢慢出现，\r\n";
        s += "  别关页面也别按刷新（关了就得重下）。素材浏览器只缓存 1 天，隔天再玩可能又要等一次。\r\n";
        s += "  启动器状态栏会显示「隧道握手中 → 隧道已连接」，连上了才会自动打开游戏页面。\r\n\r\n";
        s += "【连不上怎么办】\r\n";
        s += "  1) 房主第一次开房时，Windows 会弹防火墙提示 → 必须勾「专用网络」并点允许\r\n";
        s += "  2) 双方都要先点启动器里的「开始」，等状态显示「● 隧道已连接」\r\n";
        s += "  3) 还是不行：看解压目录里 logs 文件夹的 组网.log（启动器设置页也有「打开日志目录」）\r\n\r\n";
        s += "【重要：不要移动文件】\r\n";
        s += "  不要把 .exe 单独拖到桌面或其他地方——它必须和旁边的 openp2p 文件夹在一起。\r\n";
        s += "  想放桌面，请给 exe 建「快捷方式」，而不是移动它。\r\n\r\n";
        if (!withNode)
        {
            s += "【注意：这是轻量包】\r\n";
            s += "  本包不含游戏本体，只有启动器 + 组网引擎。两种用法：\r\n";
            s += "  · 已经装过的人：把解压出来的内容覆盖到原目录即可（只换启动器）\r\n";
            s += "  · 只加入别人房间的人：解压到任意目录双击即可，不需要游戏本体\r\n";
            s += "  ⚠ 想自己开房 / 玩单机的人：到启动器设置页点「下载游戏本体…」自己下\r\n";
            s += "     （约 290MB，实测十几秒），或者找一份「开房包」（里面带了便携版 Node.js）。\r\n\r\n";
        }
        else
        {
            s += "【这个开房包能干什么】\r\n";
            s += "  解压后你就和自己开房的人一样：能单机、能当房主、也能加入别人的房间。\r\n";
            s += "  Node.js 已经放在 卫戍协议启动器\\node\\ 里，开房时启动器会自动用它，不用另外安装。\r\n";
            s += "  ⚠ 本包不含游戏本体（版权原因）：第一次开房 / 单机之前，先到启动器设置页点\r\n";
            s += "     「下载游戏本体…」，它会从原作者官方地址下载（约 290MB，实测十几秒），下完就能玩。\r\n\r\n";
        }
        s += "【开房的人需要 Node.js】\r\n";
        s += withNode ? "  本包已经自带便携版 Node（卫戍协议启动器\\node\\node.exe），开房时自动使用。\r\n"
                      : "  本包不带 Node：第一次开房时启动器会弹出提示并帮你安装，也可以自己装一个。\r\n";
        s += "  只想加入别人房间的人不需要装任何东西。\r\n\r\n";
        s += "==================================================\r\n";
        s += "非官方同人作品，与鹰角网络 / Yostar 无任何关系。\r\n";
        s += "游戏素材版权归上海鹰角网络 / Yostar 所有，不适用 GPL。\r\n";
        s += "仅供粥友个人非商业使用，禁止任何形式盈利。\r\n";
        return s;
    }
    static void AddDir(ZipArchive zip, string dir, string prefix, string[] skip)
    {
        foreach (string f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
        {
            string rel = f.Substring(dir.Length).TrimStart('\\', '/');
            bool skipped = false;
            foreach (string s in skip) if (rel.StartsWith(s + "\\") || rel == s) skipped = true;
            if (skipped) continue;
            zip.CreateEntryFromFile(f, prefix + "/" + rel.Replace('\\', '/'), CompressionLevel.Optimal);
        }
    }

    // ================= 界面构建 =================
    // 按文字实际宽度放一个按钮，返回下一个按钮的 x
    // ⚠ 死代码（无调用方）：已被 FlowButtons 取代
    static int BtnAt(Control host, string text, int x, int y, int minW, int h, Color back, EventHandler click)
    {
        Button b = SizedBtn(text, x, y, minW, h, back, click);
        host.Controls.Add(b);
        return x + b.Width + 12;
    }
    // 按文字实际渲染宽度定尺寸，避免高 DPI 下中文被裁
    // ===== 游标式布局：每放一个控件 y 自动推进，彻底避免"行与行打架" =====
    static int curY;
    static void ResetCur(int y) { curY = y; }
    static Label Sec(Control host, string text)
    {
        curY += 14;
        Label l = new Label();
        l.Font = FTitle(11.5f, FontStyle.Bold);
        l.ForeColor = C_MINT;
        l.Text = "▍" + text;
        l.AutoSize = true;
        l.SetBounds(Sc(4), Sc(curY), 10, 10);
        host.Controls.Add(l);
        curY += 32;
        return l;
    }
    static Label Line(Control host, string text, Color col, float size)
    {
        Label l = new Label();
        l.Font = Fui(size);
        l.ForeColor = col;
        l.Text = text;
        l.AutoSize = true;
        l.SetBounds(Sc(4), Sc(curY), 10, 10);
        host.Controls.Add(l);
        curY += (int)(size * 2.6f) + 6;
        return l;
    }
    static void BtnRow(Control host, int gap, params Button[] bs)
    {
        // 先按可用宽度分行，再按行内居中排布；不会无限换行（去掉过宽的按钮后保证收敛）
        int maxX = Math.Max(200, host.ClientSize.Width - 8);
        List<List<Button>> rows = new List<List<Button>>();
        List<Button> cur = new List<Button>();
        int used = 4;
        foreach (Button b in bs)
        {
            if (b == null) continue;
            if (cur.Count > 0 && used + b.Width > maxX) { rows.Add(cur); cur = new List<Button>(); used = 4; }
            cur.Add(b);
            used += b.Width + gap;
        }
        if (cur.Count > 0) rows.Add(cur);
        foreach (List<Button> row in rows)
        {
            int total = -gap;
            foreach (Button b in row) total += b.Width + gap;
            int x = 4;
            int h = 0;
            foreach (Button b in row)
            {
                int bwR = (int)Math.Round(b.Width / UiScale());
                if (x + bwR > CW() && x > 0) { x = 0; curY += h + 8; h = 0; }   // ★ 放不下就换行
                b.SetBounds(Sc(x), Sc(curY), b.Width, b.Height);
                x += (int)Math.Round(b.Width / UiScale()) + gap;
                if (b.Height > h) h = b.Height;
            }
            curY += h + 10;
        }
    }
    static Button Mk(Control host, string text, int minW, int h, Color back, EventHandler click)
    {
        Button b = new Button();
        b.Text = text;
        b.FlatStyle = FlatStyle.Flat;
        b.BackColor = back;
        b.ForeColor = Color.White;
        b.Font = Fui(10f);
        b.FlatAppearance.BorderColor = C_EDGE;
        b.FlatAppearance.BorderSize = 1;
        Size need = TextRenderer.MeasureText(text, b.Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding);
        b.SetBounds(0, 0, Sc(Math.Max(minW, Lg(need.Width) + 30)), Sc(h));
        Flu(b);
        if (click != null) b.Click += click;
        host.Controls.Add(b);
        return b;
    }
    // 把一组按钮按行流式排布：自动换行 + 统一间距，杜绝挤压

    // 分区标题：左侧薄荷色竖条 + 标题（鹰角风格的版面元素）
    static Panel Accent(Control parent, string text, int x, int y, int w)
    {
        Panel bar = new Panel();
        bar.BackColor = C_MINT;
        bar.SetBounds(Sc(x), Sc(y + 2), Sc(4), Sc(20));
        parent.Controls.Add(bar);
        Label l = new Label();
        l.Text = text;
        l.Font = FTitle(11.5f, FontStyle.Bold);
        l.ForeColor = C_MINT;
        l.AutoSize = true;
        l.SetBounds(Sc(x + 12), Sc(y), Sc(w), Sc(24));
        parent.Controls.Add(l);
        return bar;
    }    static void FlowButtons(Control parent, int startX, int y, int gap, int maxX, int rowH, params Button[] bs)
    {
        int x = startX;
        foreach (Button b in bs)
        {
            if (b == null) continue;
            int bw = (int)Math.Round(b.Width / UiScale());
            if (x + bw > maxX && x > startX) { x = startX; y += rowH; }
            b.SetBounds(Sc(x), Sc(y), b.Width, b.Height);
            x += bw + gap;
        }
    }
    // 自绘深色界面开双缓冲，消除拖动/切页闪烁
    static void SetDoubleBuffered(Control c)
    {
        if (c == null) return;
        try
        {
            typeof(Control).GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(c, true, null);
        }
        catch { }
    }
    static Button SizedBtn(string text, int x, int y, int minW, int h, Color back, EventHandler click)
    {
        Button b = new Button();
        b.Text = text;
        b.FlatStyle = FlatStyle.Flat;
        b.BackColor = back;
        b.ForeColor = Color.White;
        b.FlatAppearance.BorderColor = C_EDGE;
        b.Font = Fui(10f);
        Size need = TextRenderer.MeasureText(text, b.Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding);
        int w = Math.Max(minW, need.Width + 34);
        b.SetBounds(Sc(x), Sc(y), Sc(w), Sc(h));
        Flu(b);
        if (click != null) b.Click += click;
        return b;
    }
    static Label L(Control parent, string text, int x, int y, int w, int h, float size, Color col, bool bold)
    {
        Label l = new Label();
        l.Text = text; l.SetBounds(Sc(x), Sc(y), Sc(w), Sc(h));
        l.Font = FontOf(UiFamily(), size, bold ? FontStyle.Bold : FontStyle.Regular);
        l.ForeColor = col;
        parent.Controls.Add(l);
        return l;
    }

    static Button B(Control parent, string text, int x, int y, int w, int h, Color back, EventHandler click)
    {
        Button b = new Button();
        b.Text = text;
        b.FlatStyle = FlatStyle.Flat;
        b.BackColor = back;
        b.ForeColor = Color.White;
        b.FlatAppearance.BorderColor = C_EDGE;
        b.FlatAppearance.BorderSize = 1;
        b.Font = Fui(10f);
        Size need2 = TextRenderer.MeasureText(text, b.Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding);
        b.SetBounds(Sc(x), Sc(y), Sc(Math.Max(w, Lg(need2.Width) + 30)), Sc(h));
        Flu(b);
        if (click != null) b.Click += click;
        parent.Controls.Add(b);
        return b;
    }

    static TextBox T(Control parent, int x, int y, int w, string ph)
    {
        TextBox t = new TextBox();
        t.SetBounds(Sc(x), Sc(y), Sc(w), Sc(26));
        t.BorderStyle = BorderStyle.FixedSingle;
        t.BackColor = C_BOX; t.ForeColor = Color.White;
        t.Font = Fui(10f);
        try { MethodInfo m = typeof(TextBox).GetMethod("set_PlaceholderText"); if (m != null) m.Invoke(t, new object[] { ph }); } catch { }
        parent.Controls.Add(t);
        return t;
    }

    static void Nav(Button b, bool on)
    {
        b.BackColor = on ? Color.FromArgb(38, 62, 58) : C_PANEL;
        b.ForeColor = on ? C_MINT : C_TEXT;
        b.FlatAppearance.BorderSize = 0;
        b.TextAlign = ContentAlignment.MiddleLeft;
        b.Padding = new Padding(14, 0, 0, 0);
    }

    static readonly Panel[] pageCache = new Panel[3];
    static readonly bool[] pageBuilt = new bool[3];


    static float S = 1f;   // DPI 缩放系数（96dpi = 1.0）
    static bool sized;
    static bool resizing;

    static void LayoutRoot()
    {
        if (form == null || content == null) return;
        if (!sized)
        {
            sized = true;
            using (Graphics g = form.CreateGraphics()) S = g.DpiX / 96f;
            // ★ 不再无条件把窗口撑到 940x620 设计尺寸：屏幕放不下时会把窗口顶出屏幕，
            //   且 MinimumSize 一起被撑大后用户再也缩不小（Win7 实测就是这个问题）。
            //   窗口尺寸由 Main 一次定好并夹进可用区，这里只补一个屏幕放得下的下限。
            Rectangle wa0 = Wa();
            form.MinimumSize = new Size(Math.Min(Sc(760), Math.Max(320, wa0.Width - 24)), Math.Min(Sc(520), Math.Max(280, wa0.Height - 24)));
        }
        int w = form.ClientSize.Width, h = form.ClientSize.Height;
        if (sidePanel != null) sidePanel.SetBounds(0, Sc(46), Sc(190), h - Sc(46));
        if (sideTipLbl != null) sideTipLbl.SetBounds(Sc(16), h - Sc(196), Sc(170), Sc(130));
        // ★ 与 Main 的算法保持一致（原来这里漏了 Sc()，DPI 放大后内容区会跑到窗口外）
        content.SetBounds(Sc(206), Sc(62), Math.Max(300, w - Sc(224)), Math.Max(240, h - Sc(100)));
        FitPage(page);   // ★ 视口装不下就自动开滚动，而不是设成视口尺寸、把超出部分丢掉
    }
    // 三个页面都在启动时建好，任何控件都不会是 null
    // 页面容器：不再为每页套一层 Panel（那层 Panel 在 DPI 放大 + 锚定下会超出容器，导致下半部不渲染）
    static readonly Panel[] made = new Panel[3];

    static Panel EnsurePage(int i)
    {
        if (made[i] != null) return made[i];
        Panel pg = new Panel();
        pg.SetBounds(0, 0, Math.Max(300, content.ClientSize.Width), Math.Max(240, content.ClientSize.Height));
        content.Controls.Add(pg);
        if (i == 0) BuildHome(pg); else if (i == 1) BuildNet(pg); else BuildSettings(pg);
        pg.Visible = false;
        made[i] = pg;
        pageBuilt[i] = true;
        pageCache[i] = pg;
        return pg;
    }

    static void BuildAllPages()
    {
        EnsurePage(0); EnsurePage(1); EnsurePage(2);
    }
    // 页面需要多高：只算当前可见的直接子控件（切房主/加入方后可见集合会变，所以每次重算）
    static int PageNeedH(int i)
    {
        Panel pg = pageCache[i];
        if (pg == null) return 0;
        int need = 0;
        foreach (Control k in pg.Controls)
        {
            if (!k.Visible) continue;
            int b = k.Bottom + 8;
            if (b > need) need = b;
        }
        return need;
    }
    // 页面需要多宽（用户把窗口拖窄时，靠横向滚动保证内容仍能到达）
    static int PageNeedW(int i)
    {
        Panel pg = pageCache[i];
        if (pg == null) return 0;
        int need = 0;
        foreach (Control k in pg.Controls)
        {
            if (!k.Visible) continue;
            // ★ 只排除 heroPanel：它的宽度是 FitPage 自己设成"页面宽"的，算进来会造成自我放大
            //   （每适配一次 +8px）。其余控件（含设置页两个子面板、各种标签）必须计入 ——
            //   否则窗口拖窄时页面不扩宽，横向滚动条就不会出现，右侧内容看不到。
            if (k == heroPanel || k == modePanel) continue;   // 这两个的宽度是随页面走的，算进来会自我放大/误开横条
            int r = k.Right + 8;
            if (r > need) need = r;
        }
        return need;
    }
    static readonly int[] lastFitNeed = new int[3];
    static readonly bool[] lastFitScroll = new bool[3];

    // ★ 视口装不下就开滚动：Win7 小屏 / 高缩放时窗口会被屏幕夹小，内容比视口高 →
    //   底部按钮永远点不到（实测：设置页的「打包」按钮只露出一角）。这里自动检测并开启滚动。
    static void FitPage(int i)
    {
        if (content == null || pageCache[i] == null) return;
        Panel pg = pageCache[i];
        HookWheelAll(pg);
        // ★ 光挂页面子控件不够：焦点在窗体上（没点任何控件）时滚轮消息发给窗体，没人接就滚不动
        if (content != null) HookWheelAll(content);
        if (form != null) { form.MouseWheel -= WheelForward; form.MouseWheel += WheelForward; }
        // ★★ 视口必须按"滚动条出现之后真正能看到的那块"算（2026-10-04 定位到的横条根因）：
        //   content.ClientSize 是"两面派"——滚动条没出现时=全宽(1074)，出现后=扣掉滚动条(1048)，
        //   同一个量两种含义。用它算就会：第一次按 1074 把页面撑到 1074 → 纵向条一出现挤掉 26px
        //   → 页面比可视区宽 → WinForms 立刻补一条横向滚动条（用户看到"一打开就有横条 + 右边被裁"）。
        //   实测：双击正常启动必现；带 -page:N 启动反而看不到（那条路多跑了一遍 FitPage，第二次
        //   ClientSize 已经缩水，页面被改窄，横条又消失）——所以自检钩子一直没能复现它。
        //   这里统一改用 content.Width/Height（面板外框，滚动条出现前后都不变），再自己扣滚动条厚度。
        int boxW = Math.Max(200, content.Width);
        int boxH = Math.Max(200, content.Height);
        int need = PageNeedH(i);
        int needW = PageNeedW(i);
        int vsw = SystemInformation.VerticalScrollBarWidth;     // 纵向条占宽（150% 下 26px）
        int hsw = SystemInformation.HorizontalScrollBarHeight;  // 横向条占高
        bool scroll = need > boxH + 2;
        // ★ 阈值给 40：纵向滚动条只吃掉一条 vsw，不该因此就冒出一条横向滚动条（很丑）。
        //   只有"用户真的把窗口拖窄了"（差几十上百像素）才启用横向滚动
        bool scrollX = needW > boxW - (scroll ? vsw : 0) + 40;
        if (scrollX)   // 横条一出现又吃掉底部高度，可能把"不用纵向滚"变成"要纵向滚"→ 回代一次（两轮必收敛）
        {
            scroll = need > boxH - hsw + 2;
            scrollX = needW > boxW - (scroll ? vsw : 0) + 40;
        }
        int viewW = Math.Max(200, boxW - (scroll ? vsw : 0));   // 真正看得见的宽
        int viewH = Math.Max(200, boxH - (scrollX ? hsw : 0));  // 真正看得见的高
        if (content.AutoScroll != (scroll || scrollX)) content.AutoScroll = (scroll || scrollX);
        // ★ 明确控制横向滚动条：只要 AutoScroll 开着，任何一个子控件超出客户区几像素，
        //   WinForms 就会自己画一条横条（跟上面阈值无关）→ 平时关掉，只有"用户真把窗口拖窄"才放出来
        try { content.HorizontalScroll.Visible = scrollX; } catch { }
        content.AutoScrollMinSize = new Size(scrollX ? needW : 0, scroll ? need : 0);
        // ★ 只有"真的需要横向滚动"时才把页面撑宽：页面宽必须 ≤ viewW，否则 WinForms 立刻补一条横条
        int w = Math.Max(300, scrollX ? Math.Max(viewW, needW) : viewW);
        int h = Math.Max(viewH, need);
        if (pg.Width != w || pg.Height != h) pg.SetBounds(0, 0, w, h);
        if (i == 0 && heroPanel != null) heroPanel.SetBounds(0, 0, w, Sc(196));
        // ★ 模式按钮容器也跟着页面宽（它的宽度原来是建时取的物理宽，纵向滚动条一出现就比视口宽 → 冒出横向条）
        if (i == 1 && modePanel != null) modePanel.SetBounds(Sc(0), Sc(0), w, Sc(44));
        // 把非当前页压回视口高度，免得把滚动范围撑大（WinForms 只算可见子控件，这里再保险一层）
        for (int j = 0; j < 3; j++)
            if (j != i && pageCache[j] != null && pageCache[j].Height > viewH)
                pageCache[j].SetBounds(0, 0, w, viewH);
        if (i == page && (lastFitNeed[i] != need || lastFitScroll[i] != scroll))
        {
            lastFitNeed[i] = need; lastFitScroll[i] = scroll;
            Log("[布局] 页" + i + " 视口高=" + viewH + " 内容需求高=" + need + " 滚动=" + (scroll ? "启用" : "关闭"));
        }
    }

    // 内容区滚动：滚轮过滤器与测试钩子共用同一份逻辑
    internal static void ScrollContentBy(int dy)
    {
        if (content == null || !content.AutoScroll) return;
        int cur = -content.AutoScrollPosition.Y;
        int max = Math.Max(0, content.AutoScrollMinSize.Height - content.ClientSize.Height);
        int next = cur + dy;
        if (next < 0) next = 0;
        if (next > max) next = max;
        content.AutoScrollPosition = new Point(0, next);
    }

    // 测试钩子（-scrolltest）：证明滚动链路真的能动，并把数字写进日志
    static void ScrollTest()
    {
        int max = Math.Max(0, content.AutoScrollMinSize.Height - content.ClientSize.Height);
        Log("[测试] 滚动链路：视口高=" + content.ClientSize.Height + " 可滚最大=" + max + " AutoScroll=" + content.AutoScroll + " 当前=" + (-content.AutoScrollPosition.Y));
        ScrollContentBy(-120); Log("[测试] 上滚 120 后=" + (-content.AutoScrollPosition.Y));
        ScrollContentBy(999999); Log("[测试] 滚到底=" + (-content.AutoScrollPosition.Y) + "（应等于可滚最大）");
        ScrollContentBy(-999999); Log("[测试] 滚回顶=" + (-content.AutoScrollPosition.Y) + "（应为 0）");
    }

    // -uishake：把"交互之后才会出现"的布局逐个走一遍（切房主/加入方、展开高级选项、切设置子页），
    // 每一步都跑一遍和正式自检同样的检查 —— 这是矩阵（只测静态页面）覆盖不到的地方
    static void ShakeTest()
    {
        string rep = Path.Combine(LogDirRoot(), "界面震荡检查.txt");
        try { File.WriteAllText(rep, "界面震荡检查  " + EnvLine() + "\r\n（每步检查项与正式自检相同：重叠 / 越界 / 文字截断 / 子页被裁 / 表单遮挡）\r\n\r\n", Encoding.UTF8); } catch { }
        lcSuffix = "-震荡";
        lcStep = "页1 房主"; ShowPage(1); SetMode(true); LayoutCheck();
        lcStep = "页1 房主+高级选项"; advOpen = true; ApplyAdvancedVisibility(); LayoutCheck();
        lcStep = "页1 加入方"; advOpen = false; ApplyAdvancedVisibility(); SetMode(false); LayoutCheck();
        lcStep = "页1 加入方+高级"; advOpen = true; ApplyAdvancedVisibility(); LayoutCheck();
        advOpen = false; ApplyAdvancedVisibility();
        lcStep = "页2 基本"; ShowPage(2); ShowSetPage(false); LayoutCheck();
        lcStep = "页2 分享与关于"; ShowSetPage(true); LayoutCheck();
        lcStep = "页0 主页"; ShowPage(0); LayoutCheck();
        Log("[震荡] 走完 7 个交互状态，详见 logs\\界面震荡检查.txt");
        try { System.Diagnostics.Process.GetCurrentProcess().Kill(); } catch { }
    }

    // 屏幕可用区（-screensize 钩子可覆盖，用于模拟别的电脑）
    static Rectangle Wa()
    {
        if (cliScreen.Width > 0) return new Rectangle(0, 0, cliScreen.Width, Math.Max(200, cliScreen.Height - 40));
        return Screen.PrimaryScreen.WorkingArea;
    }
    static string Fmt(Rectangle r) { return r.X + "," + r.Y + " " + r.Width + "x" + r.Height; }
    // -dlgcheck 用：对话框是模态的，用一次性定时器等它建好→量一遍→写报告→关掉（自动化验证对话框）
    static void CheckDialogLater(Form d, string name)
    {
        System.Windows.Forms.Timer tm = new System.Windows.Forms.Timer();
        tm.Interval = 700;   // 闪屏只活约 1.2 秒，间隔太短会量到"还没建好控件"的空壳
        tm.Tick += delegate
        {
            tm.Stop(); tm.Dispose();
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("对话框=" + name + "  客户区=" + d.ClientSize.Width + "x" + d.ClientSize.Height + "  " + EnvLine());
            int needR = 0, needB = 0, bad = 0;
            foreach (Control k in d.Controls)
            {
                if (k.Right > needR) needR = k.Right;
                if (k.Bottom > needB) needB = k.Bottom;
                if (k.Right > d.ClientSize.Width + 2 || k.Bottom > d.ClientSize.Height + 2)
                {
                    bad++;
                    sb.AppendLine("越界: [" + Tx(k) + " " + Fmt(k.Bounds) + "] 客户区 " + d.ClientSize.Width + "x" + d.ClientSize.Height);
                }
            }
            sb.AppendLine("最深控件=" + needR + "x" + needB + "  越界数=" + bad + "  总判定=" + (bad == 0 ? "PASS" : "FAIL"));
            try { File.AppendAllText(Path.Combine(LogDirRoot(), "对话框检查-" + name + ".txt"), sb.ToString(), Encoding.UTF8); } catch { }
            try { d.Close(); } catch { }
        };
        tm.Start();
    }
    // 本机 .NET 版本：★ 打包与自动更新用的是 ZipArchive，那是 .NET 4.5 才有的类
    // （Win7 只装到 4.0 的话，点「打包」会失败 —— 报告里写清楚，省得猜）
    static string DotNetRelease()
    {
        try
        {
            object v = Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full", "Release", null);
            if (v == null) return "未检测到 4.x";
            int r = Convert.ToInt32(v);
            if (r >= 533320) return "4.8.1 或更高";
            if (r >= 528040) return "4.8";
            if (r >= 461808) return "4.7.2";
            if (r >= 460798) return "4.7";
            if (r >= 394802) return "4.6.2";
            if (r >= 394254) return "4.6.1";
            if (r >= 393295) return "4.6";
            if (r >= 379893) return "4.5.2";
            if (r >= 378675) return "4.5.1";
            if (r >= 378389) return "4.5";
            return "4.0（★ 打包与自动更新需要 4.5+）";
        }
        catch { return "未知"; }
    }
    static string EnvLine()
    {
        return "系统=" + Environment.OSVersion.VersionString + "  64位进程=" + Environment.Is64BitProcess
             + "  .NET=" + DotNetRelease() + "  字体族=" + UiFamily();
    }
    static string Tx(Control c)
    {
        string s = (c.Text == null ? "" : c.Text).Replace("\r", " ").Replace("\n", " ");
        if (s.Length > 16) s = s.Substring(0, 16);
        return s.Length > 0 ? s : c.GetType().Name;
    }

    // ===== 布局自检（-layoutcheck）：把这一页的裁切/重叠/文字截断写成机器可读报告，再用退出码表示成败 =====
    static void LayoutCheck()
    {
        StringBuilder sb = new StringBuilder();
        int fail = 0;
        Panel pg = pageCache[page];
        // ★ 可视区按"扣掉滚动条之后真正能看到的"算（和 FitPage 同一套口径），并记录两条滚动条的真实状态——
        //   以前只打印 ClientSize，横条明明在屏幕上却看不出来，这个 bug 因此躲过了一整轮自检。
        bool hBar = false, vBar = false;
        try { hBar = content.HorizontalScroll.Visible; vBar = content.VerticalScroll.Visible; } catch { }
        int viewW = Math.Max(200, content.Width - (vBar ? SystemInformation.VerticalScrollBarWidth : 0));
        int viewH = Math.Max(200, content.Height - (hBar ? SystemInformation.HorizontalScrollBarHeight : 0));
        int reach = Math.Max(viewH, content.AutoScrollMinSize.Height);
        sb.AppendLine(EnvLine());
        sb.AppendLine("DPI=" + (int)Math.Round(DpiScale() * 100) + "%  客户区=" + form.ClientSize.Width + "x" + form.ClientSize.Height
            + "  内容外框=" + content.Width + "x" + content.Height + "  可视区=" + viewW + "x" + viewH
            + "  横条=" + (hBar ? "显示" : "隐藏") + "  纵条=" + (vBar ? "显示" : "隐藏")
            + "  页=" + page + "  页面=" + pg.Width + "x" + pg.Height
            + "  滚动=" + (content.AutoScroll ? "启用" : "关闭") + "  可滚=" + Math.Max(0, content.AutoScrollMinSize.Height - viewH));
        int clipSub = 0;
        List<Control> cs = new List<Control>();
        List<Control> cs2 = new List<Control>();
        foreach (Control c in pg.Controls)
        {
            if (!c.Visible) continue;
            cs.Add(c);
            Panel sub = c as Panel;   // ★ 子页内容（设置页分两页后在里面）也要查
            if (sub != null)
            {
                int ph = sub.Height;
                foreach (Control k in sub.Controls)
                {
                    if (!k.Visible) continue;
                    cs2.Add(k);
                    if (k.Bottom > ph + 2) { clipSub++; if (clipSub <= 5) sb.AppendLine("子页被裁: [" + Tx(k) + " " + Fmt(k.Bounds) + "] Bottom=" + k.Bottom + " > 面板高=" + ph); }
                }
            }
        }
        List<Control> all = new List<Control>(cs); all.AddRange(cs2);
        int ov = 0, minor = 0;
        for (int a = 0; a < cs.Count; a++)
            for (int b = a + 1; b < cs.Count; b++)
                if (cs[a].Bounds.IntersectsWith(cs[b].Bounds))
                {
                    Rectangle ix = Rectangle.Intersect(cs[a].Bounds, cs[b].Bounds);
                    if (ix.Width <= 3 || ix.Height <= 3) { minor++; }   // 1~3px 的边贴边：视觉不可见，单独计数
                    else { ov++; if (ov <= 5) sb.AppendLine("重叠: [" + Tx(cs[a]) + " " + cs[a].GetType().Name + " " + Fmt(cs[a].Bounds) + "] x [" + Tx(cs[b]) + " " + cs[b].GetType().Name + " " + Fmt(cs[b].Bounds) + "] 相交 " + ix.Width + "x" + ix.Height); }
                }
        // ★ 只比"同一个父容器里的兄弟"：不同面板的子控件坐标是各自相对的，混在一起比会出假重叠
        foreach (Control pSub in pg.Controls)
        {
            Panel pp = pSub as Panel;
            if (pp == null || !pp.Visible) continue;
            List<Control> sib = new List<Control>();
            foreach (Control k in pp.Controls) if (k.Visible) sib.Add(k);
            for (int a = 0; a < sib.Count; a++)
                for (int b = a + 1; b < sib.Count; b++)
                    if (sib[a].Bounds.IntersectsWith(sib[b].Bounds))
                    {
                        Rectangle ix2 = Rectangle.Intersect(sib[a].Bounds, sib[b].Bounds);
                        if (ix2.Width <= 3 || ix2.Height <= 3) { minor++; }
                        else { ov++; if (ov <= 6) sb.AppendLine("重叠(子面板" + Tx(pp) + "): [" + Tx(sib[a]) + " " + Fmt(sib[a].Bounds) + "] x [" + Tx(sib[b]) + " " + Fmt(sib[b].Bounds) + "] 相交 " + ix2.Width + "x" + ix2.Height); }
                    }
        }
        // ★ 表单级：内容区不能被底部署名栏/顶栏/侧栏盖住（控件级检查看不到容器之间的问题）
        if (content != null && legalLabel != null && content.Bounds.IntersectsWith(legalLabel.Bounds))
        {
            ov++;
            sb.AppendLine("表单遮挡: 内容区 " + Fmt(content.Bounds) + " x 署名栏 " + Fmt(legalLabel.Bounds));
        }
        sb.AppendLine((ov == 0 ? "OK  " : "FAIL ") + "重叠数=" + ov + "（另有轻微重叠 " + minor + " 处，相交<=12平方像素，不算失败）");
        if (ov > 0) fail++;
        int oob = 0;
        foreach (Control c in all)
        {
            if (c.Right > pg.Width + 2) { oob++; if (oob <= 5) sb.AppendLine("横向越界: [" + Tx(c) + " " + c.GetType().Name + " " + Fmt(c.Bounds) + "] Right=" + c.Right + " > 页宽=" + pg.Width); }
            if (c.Bottom > reach + 2) { oob++; if (oob <= 5) sb.AppendLine("纵向越界: [" + Tx(c) + " " + c.GetType().Name + " " + Fmt(c.Bounds) + "] Bottom=" + c.Bottom + " > 可达高=" + reach); }
        }
        sb.AppendLine((oob == 0 ? "OK  " : "FAIL ") + "越界数=" + oob);
        if (oob > 0) fail++;
        int clip = 0;
        foreach (Control c in all)
        {
            if (c.AutoSize || c.Text == null || c.Text.Length == 0) continue;
            Size need = TextRenderer.MeasureText(c.Text, c.Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding);
            int needW = need.Width;   // ★ 不再乘 UiScale：MeasureText 返回的已经是"当前 DPI 下的物理像素"，
                                                         //   控件 Width 也是物理像素，直接比才等价。以前乘了 UiScale 等于把 DPI 算两遍，
                                                         //   高 DPI 满屏假截断（200% 实测 6 条假报），宽出来的真问题反被淹没。
            int pad = (c is Button) ? 8 : 6;   // 按钮两侧各留 4px 就够（16 太严，会把"文字其实放得下"误报成截断）
            if (needW + pad > c.Width + 2) { clip++; if (clip <= 5) sb.AppendLine("文字可能截断: [" + Tx(c) + "] 需要 " + needW + "px 实宽 " + c.Width); }
        }
        sb.AppendLine((clipSub == 0 ? "OK  " : "FAIL ") + "子页被裁数=" + clipSub);
        if (clipSub > 0) fail++;
        sb.AppendLine((clip == 0 ? "OK  " : "FAIL ") + "文字截断数=" + clip);
        if (clip > 0) fail++;
        // ★ 假横条判定：页面并没有比可视区宽，屏幕上却挂着一条横向滚动条（= FitPage 的口径错了）
        int maxRight = 0;
        foreach (Control k in pg.Controls) if (k.Visible && k.Right > maxRight) maxRight = k.Right;
        bool xSpurious = hBar && pg.Width <= viewW + 2;
        sb.AppendLine((xSpurious ? "FAIL " : "OK  ") + "横向滚动条=" + (hBar ? "显示" : "隐藏") + "  页宽=" + pg.Width
            + "  可视宽=" + viewW + "  最右子控件=" + maxRight + (xSpurious ? "  ← 页面不比可视区宽却出现横条（假横条）" : ""));
        if (xSpurious) fail++;
        sb.AppendLine("总判定=" + (fail == 0 ? "PASS" : "FAIL") + " 失败项=" + fail);
        try { File.WriteAllText(Path.Combine(LogDirRoot(), "布局检查-页" + page + lcSuffix + ".txt"), sb.ToString(), Encoding.UTF8); } catch { }
        Log("[布局自检] " + (fail == 0 ? "PASS" : "FAIL") + " 页" + page + "（详见 logs\\布局检查-页" + page + lcSuffix + ".txt）");
        if (lcSuffix.Length > 0)   // 震荡模式：记一行到汇总报告，然后返回（不退出，继续走下一个状态）
        {
            try { File.AppendAllText(Path.Combine(LogDirRoot(), "界面震荡检查.txt"),
                lcStep.PadRight(24) + " 页" + page + "  " + (fail == 0 ? "PASS" : "FAIL") + "  失败项=" + fail + "\r\n", Encoding.UTF8); } catch { }
            return;
        }
        // ★ 干净退出：先停健康检查循环，再用 Application.Exit。
        //   直接 Environment.Exit 会在定时器回调里抛 0xC000041D（用户回调致命异常）→ 退出码变负数被脚本误判
        // ★ 自检模式直接终结进程：Application.Exit / Environment.Exit 都会走托管收尾，
        //   定时器回调可能在这期间撞车（实测抛过 0xC000041D 和原生 0xE8 访问违例，还会弹系统错误框）。
        //   Kill 不进收尾流程 → 不可能弹框。报告在这之前已经写盘，判定由报告决定（脚本不看退出码）。
        try { System.Diagnostics.Process.GetCurrentProcess().Kill(); } catch { }
    }

    // 把滚轮挂到页面里每个子控件上：WinForms 只把 WM_MOUSEWHEEL 发给焦点控件，
    // 焦点在按钮/输入框上时 AutoScroll 面板收不到 → 用户滚不动。
    // ★ 不用全局消息过滤器：那会"拦截所有消息 + 读鼠标位置"，实测被 Windows Defender 查杀
    static void HookWheelAll(Control root)
    {
        if (root == null) return;
        root.MouseWheel -= WheelForward;
        root.MouseWheel += WheelForward;
        foreach (Control k in root.Controls) HookWheelAll(k);
    }
    static void WheelForward(object s, MouseEventArgs e)
    {
        Control kk = s as Control;
        if (kk is TextBox && ((TextBox)kk).Multiline && ((TextBox)kk).ScrollBars != ScrollBars.None) return;
        if (content == null || !content.AutoScroll) return;
        int step = SystemInformation.MouseWheelScrollLines;
        if (step <= 0) step = 3;
        ScrollContentBy(-Math.Sign(e.Delta) * step * 24);
    }

    static void ShowPage(int p)
    {
        page = p;
        Nav(navHome, p == 0); Nav(navNet, p == 1); Nav(navSet, p == 2);
        for (int i = 0; i < 3; i++)
        {
            EnsurePage(i);
            pageCache[i].Visible = (i == p);
            if (i == p) pageCache[i].BringToFront();
        }
        RefreshGameUi();
        // ★ 设置页子面板的高度要在"整页真的可见"之后才算得准（否则 k.Visible 全是 false，会量成 120 把内容裁掉）
        if (p == 2 && setPageA != null) ShowSetPage(setShareOn);
        FitPage(p);   // ★ 必须在 RefreshGameUi 之后：它会改变控件可见性，需求高度得按"最终可见集合"算
        if (p == 1) { if (keyValue != null) keyValue.Text = KeyOf(); }
    }

    // ---- 主页 ----
    static void BuildHome(Panel host)
    {
        // 顶部英雄区（保留你喜欢的那个版式）
        heroPanel = new Panel();
        heroPanel.SetBounds(0, 0, Math.Max(600, host.Width), Sc(196));
        heroPanel.BackColor = Color.FromArgb(22, 34, 33);
        host.Controls.Add(heroPanel);

        heroTitle = new Label();
        heroTitle.Font = FTitle(23f, FontStyle.Bold);
        heroTitle.ForeColor = C_MINT;
        heroTitle.Text = GameName;
        heroTitle.AutoSize = true;
        heroTitle.SetBounds(Sc(28), Sc(14), Sc(10), Sc(10));
        heroPanel.Controls.Add(heroTitle);

        heroSub = new Label();
        heroSub.Font = FTitle(9.5f, FontStyle.Regular);
        heroSub.ForeColor = C_DIM;
        heroSub.Text = "STRONGHOLD PROTOCOL · COVENANT　非官方同人联机版";
        heroSub.AutoSize = true;
        heroSub.SetBounds(Sc(30), Sc(78), Sc(10), Sc(10));
        heroPanel.Controls.Add(heroSub);

        heroDesc = new Label();
        heroDesc.ForeColor = C_TEXT;
        heroDesc.Font = Fui(10f);
        heroDesc.Text = "塔防 + 自走棋合作玩法，1–4 人联机，可加 AI 队友。";
        heroDesc.AutoSize = true;
        heroDesc.SetBounds(Sc(30), Sc(102), Sc(10), Sc(10));
        heroPanel.Controls.Add(heroDesc);

        heroStateLbl = new Label();
        heroStateLbl.Font = Fui(10f, FontStyle.Bold);
        heroStateLbl.Text = File.Exists(O2PExe()) ? "✓ 组网引擎就绪" : "⚠ 缺少 openp2p\\openp2p.exe";
        heroStateLbl.ForeColor = File.Exists(O2PExe()) ? C_MINT : Color.FromArgb(240, 170, 90);
        heroStateLbl.AutoSize = true;
        heroStateLbl.SetBounds(Sc(30), Sc(126), Sc(10), Sc(10));
        heroPanel.Controls.Add(heroStateLbl);

        heroPlay = new Button();
        heroPlay.Text = "▶  开始游戏（单机）";
        heroPlay.Font = Fui(10f, FontStyle.Bold);        // ★ 字体先定，再按文字量测定宽（换系统字体也不会贴边裁字）
        Size needHero = TextRenderer.MeasureText(heroPlay.Text, heroPlay.Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding);
        heroPlay.SetBounds(Sc(30), Sc(152), Sc(Math.Max(210, Lg(needHero.Width) + 36)), Sc(36));
        heroPlay.FlatStyle = FlatStyle.Flat;
        heroPlay.BackColor = C_ON;
        heroPlay.ForeColor = Color.White;
        heroPlay.FlatAppearance.BorderSize = 0;
        heroPlay.Click += delegate { StartSolo(); };
        Flu(heroPlay);
        heroPanel.Controls.Add(heroPlay);

        Label gn = new Label();
        gn.Text = "联机开黑请用左侧「联机开黑」";
        gn.ForeColor = C_DIM;
        gn.Font = Fui(9.5f);
        gn.AutoSize = true;
        gn.SetBounds(heroPlay.Right + Sc(16), Sc(160), 10, 10);
        heroPanel.Controls.Add(gn);

        // ---- 以下直接挂在页面容器上（页面容器已不再套锚定放大的 Panel）----
        L2(host, "游戏本体", 4, 214, C_MINT, 11f, true);
        bodyStateLbl = L2(host, "", 4, 242, C_TEXT, 10f, false);
        bodyPathLbl = L2(host, "", 4, 266, C_DIM, 9f, false);
        Button bb1 = B(host, "浏览指定游戏位置…", 4, 298, 160, 32, C_OFF, null);
        bb1.Click += delegate { string g = BrowseGame(); if (g != null) { Log("已指定游戏本体：" + g); RefreshHome(); } };
        Button bb2 = B(host, "打开游戏文件夹", 0, 298, 150, 32, C_OFF, null);
        bb2.Click += delegate
        {
            string g = FindGame(false);
            if (g != null) { try { Process.Start("explorer.exe", "\"" + g + "\""); } catch { } }
        };

        L2(host, "本局可以怎么玩", 4, 356, C_MINT, 11f, true);
        FlowButtons(host, 4, 298, 16, CW(), 40, bb1, bb2);
        L2(host, "· 独立模拟（单机）：一个人调配资金与干员，按自己的节奏打完 14 回合。", 4, 388, C_TEXT, 9.5f, false);
        L2(host, "· 同盟模拟（联机）：1–4 人共享干员池，联防协作，空位可补 AI 队友。", 4, 412, C_TEXT, 9.5f, false);
        L2(host, "· 四档难度：标准 AC-1 → 终极 AC-4（后三档含「隐秘核心」）", 4, 436, C_TEXT, 9.5f, false);
        L2(host, "· 节奏：休整期招募摆阵 → 作战期干员自动开技能，漏怪扣全队生命值。", 4, 460, C_TEXT, 9.5f, false);

        Button kb = B(host, "强制结束游戏进程", 4, 494, 180, 32, C_OFF, null);
        kb.Click += delegate { KillServerUi(); };
        L2(host, "本启动器只负责组网、开房与启动；游戏本体来自开源同人项目。", 4, 538, C_DIM, 9f, false);
        L2(host, "游戏代码 GPL-3.0 · 素材版权归鹰角 / Yostar · 禁止任何形式盈利", 4, 560, C_DIM, 9f, false);

        RefreshHome();
    }
    // 面板内专用：左对齐文字标签（与英雄区标题同样的创建顺序）
    static Label L2(Control parent, string text, int x, int y, Color col, float size, bool bold)
    {
        Label l = new Label();
        l.Font = Fui(size, bold ? FontStyle.Bold : FontStyle.Regular);
        l.ForeColor = col;
        l.Text = text;
        l.AutoSize = true;
        l.SetBounds(Sc(x), Sc(y), 10, 10);
        parent.Controls.Add(l);
        return l;
    }

    // 面板内专用：外观像纯文字的按钮（按文字量测宽度）
    static Button B2(Control parent, string text, int x, int y, int minW)
    {
        Button b = new Button();
        b.Font = Fui(11f);
        b.ForeColor = C_TEXT;
        b.Text = text;
        b.FlatStyle = FlatStyle.Flat;
        b.BackColor = Color.FromArgb(22, 34, 33);
        b.FlatAppearance.BorderSize = 0;
        b.FlatAppearance.MouseOverBackColor = Color.FromArgb(38, 54, 52);
        b.TextAlign = ContentAlignment.MiddleLeft;
        Size need = TextRenderer.MeasureText(text, b.Font);
        b.SetBounds(Sc(x), Sc(y), Sc(Math.Max(minW, Lg(need.Width) + 24)), Sc(28));
        b.Cursor = Cursors.Hand;
        parent.Controls.Add(b);
        return b;
    }
    static void RefreshHome()
    {
        if (page != 0 || bodyStateLbl == null || bodyPathLbl == null) return;
        string g = FindGame(false);
        if (g == null)
        {
            bodyStateLbl.Text = "✗ 没有找到游戏本体（只能加入别人开的房间）";
            bodyStateLbl.ForeColor = Color.FromArgb(240, 170, 90);
            bodyPathLbl.Text = "点下面「浏览指定游戏位置…」选中 Stronghold-Protocol 文件夹";
        }
        else
        {
            bodyStateLbl.Text = "✓ 已就绪，可以单机或开房";
            bodyStateLbl.ForeColor = C_MINT;
            bodyPathLbl.Text = g;
        }
    }
    static void BuildNet(Panel host)
    {
        // 模式卡
        modePanel = new Panel();
        modePanel.SetBounds(Sc(0), Sc(0), host.ClientSize.Width, Sc(44));
        modePanel.BackColor = Color.FromArgb(28, 31, 38);
        modePanel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        host.Controls.Add(modePanel);

        modeHost = ModeBtn("① 我开房（当房主）", 6, 7);
        modeHost.SetBounds(Sc(6), Sc(7), Sc(300), Sc(30));
        modeHost.Click += delegate { SetMode(true); };
        FluCard(modeHost, C_ON);
        modePanel.Controls.Add(modeHost);
        modeJoin = ModeBtn("② 加入粥友的房间", 318, 7);
        modeJoin.SetBounds(Sc(318), Sc(7), Sc(300), Sc(30));
        modeJoin.Click += delegate { SetMode(false); };
        FluCard(modeJoin, C_ON);
        modePanel.Controls.Add(modeJoin);

        // ---- 房主：只需要一个按钮 ----
        netHostTip = L(host, "① 点下面的绿色大按钮开房", 4, 62, CW(), 26, 12f, C_TEXT, true);
        roomStatus = L(host, "当前状态：未开房", 4, 92, CW(), 26, 12f, Color.FromArgb(150, 200, 255), true);

        netLink = new TextBox();
        netLink.SetBounds(Sc(4), Sc(124), Sc(CW()), Sc(32));
        netLink.ReadOnly = true;
        netLink.BorderStyle = BorderStyle.FixedSingle;
        netLink.BackColor = C_BOX; netLink.ForeColor = C_MINT;
        netLink.Font = Fmono(11.5f);
        netLink.Text = "点下面的按钮后，这里会出现给粥友的东西";
        host.Controls.Add(netLink);

        int bx = 4;
        shareBtn = B(host, "开房并复制邀请", bx, 168, 190, 50, C_ON, delegate
        {
            if (!IsHost) { GoMultiplayer(false); return; }
            // ★ 房间开着时这个绿按钮写着「关闭房间」，那就必须真的关房。
            //   以前这里只是"重发一次邀请" → 用户点了以为关了，隧道和游戏服务还在跑，别人还能连进来。
            if (startBtn.Text.Trim() == "关闭房间") { StopAll(); return; }
            GoMultiplayer(true);
        });
        shareBtn.Font = Fui(12f, FontStyle.Bold);
        bx += shareBtn.Width + 12;
        keyCopyBtn = B(host, "只复制链接", bx, 168, 140, 50, C_OFF, delegate
        {
            CopyText(RoomUrl(true)); Log("已复制房间链接（粥友粘进「加入房间」就能进）");
        });
        bx += keyCopyBtn.Width + 12;
        openBtn = B(host, "打开游戏页面", bx, 168, 140, 50, C_OFF, delegate
        {
            try { Process.Start(IsHost ? "http://localhost:3000" : HostLink() + "/" + RoomQuery()); } catch { }
        });
        openBtn.Enabled = false;
        bx += openBtn.Width + 12;
        startBtn = B(host, "开 始", bx, 146, 120, 46, C_OFF, delegate
        {
            if (startBtn.Text.Trim() == "关闭房间") StopAll(); else GoMultiplayer(true);
        });
        startBtn.Visible = false;

        // ---- 加入方：只需要一个粘贴框 ----
        netJoinTip = L(host, "把房主发来的链接粘到下面，点「进房」就进去了。", 4, 66, CW(), 24, 11f, C_TEXT, false);
        joinHint = L(host, "你现在是「加入方」：不需要分享任何东西给粥友，只需要房主给你的那条链接。", 4, 94, CW(), 20, 9f, C_DIM, false);
        joinPaste = new TextBox();
        joinPaste.SetBounds(Sc(4), Sc(100), Sc(CW()), Sc(32));
        joinPaste.BorderStyle = BorderStyle.FixedSingle;
        joinPaste.BackColor = C_BOX; joinPaste.ForeColor = Color.White;
        joinPaste.Font = Fmono(11f);
        try { MethodInfo pm = typeof(TextBox).GetMethod("set_PlaceholderText"); if (pm != null) pm.Invoke(joinPaste, new object[] { "在这里粘贴房主发来的链接（Ctrl+V）" }); } catch { }
        host.Controls.Add(joinPaste);

        joinBtn = B(host, "进房", 4, 146, 190, 46, C_ON, delegate { PasteJoin(); });
        joinBtn.Font = Fui(12f, FontStyle.Bold);

        // ---- 高级选项（房间名/密码/编号）----
        killBtn = B(host, "强制结束游戏进程", 4, 230, 190, 30, C_OFF, delegate { KillServerUi(); });
        advToggle = B(host, "高级选项 ▾", 206, 230, 120, 30, C_OFF, delegate
        {
            advOpen = !advOpen;
            if (advToggle != null) advToggle.Text = advOpen ? "高级选项 ▴" : "高级选项 ▾";
            ApplyAdvancedVisibility();
        });
        advPanel = new Panel();
        advPanel.SetBounds(Sc(0), Sc(272), Sc(CW()), Sc(96));
        advPanel.BackColor = C_BG;
        host.Controls.Add(advPanel);
        L(advPanel, "房间名", 4, 2, 200, 20, 9.5f, C_TEXT, false);
        roomBox = T(advPanel, 4, 24, 220, "例如 AAAlappland");
        L(advPanel, "密码", 240, 2, 200, 20, 9.5f, C_TEXT, false);
        passBox = T(advPanel, 240, 24, 220, "例如 909090pq");
        L(advPanel, "我的编号", 476, 2, 120, 20, 9.5f, C_TEXT, false);
        seatBox = T(advPanel, 476, 24, 80, "1");
        browseBtn = B(advPanel, "浏览游戏", 572, 24, 110, 27, C_OFF, delegate
        {
            string g = BrowseGame();
            if (g != null) { Log("已指定游戏本体：" + g); RefreshGameUi(); }
        });
        gameLine = L(advPanel, "", 4, 60, 420, 20, 9f, C_TEXT, false);
        seatHint = L(advPanel, "编号 1 = 房主，粥友 2/3/4", 430, 60, CW() - 438, 20, 9f, C_DIM, false);   // ★ 宽度跟着可用宽度走（原来写死 460，x=430 → 溢出到面板外）

        // ---- 状态与会话信息 ----
        roomInfo = L(host, "", 4, 344, CW(), 22, 10f, C_MINT, false);
        logTitle = L(host, "日志", 4, 374, 100, 20, 10f, C_MINT, true);
        membersLine = L(host, "", 200, 374, 700, 20, 9.5f, C_DIM, false);
        logBox = new TextBox();
        logBox.Multiline = true;
        logBox.ScrollBars = ScrollBars.Vertical;
        logBox.SetBounds(Sc(4), Sc(398), Sc(CW()), Sc(130));
        logBox.BorderStyle = BorderStyle.FixedSingle;
        logBox.BackColor = C_LOG; logBox.ForeColor = Color.FromArgb(168, 178, 190);
        logBox.Font = Fmono(9f);
        logBox.ReadOnly = true;
        host.Controls.Add(logBox);

        SetMode(true);
        roomBox.Text = seats[0]; passBox.Text = seats[1]; seatBox.Text = seats[2];
    }

    // 模式切换按钮（左右并排的两个标签按钮）
    static Label ModeBtn(string text, int x, int y)
    {
        Label l = new Label();
        l.Text = text;
        l.SetBounds(Sc(x), Sc(y), Sc(300), Sc(30));
        l.TextAlign = ContentAlignment.MiddleCenter;
        l.Font = Fui(10f, FontStyle.Bold);
        l.ForeColor = Color.White;
        return l;
    }

    // 一次性刷新所有与"游戏本体"有关的界面提示，避免多处重复扫描磁盘
    static void RefreshGameUi()
    {
        string g = FindGame(false);
        bool has = g != null;
        if (gameLine != null && IsHost)
        {
            gameLine.Text = has ? "✓ 已找到游戏本体" : "⚠ 未找到游戏本体";
            gameLine.ForeColor = has ? C_MINT : Color.FromArgb(240, 170, 90);
        }
        else if (gameLine != null)
        {
            gameLine.Text = "✓ 加入模式（不需要本体）";
            gameLine.ForeColor = C_DIM;
        }
        if (bodyStateLbl != null && bodyPathLbl != null && page == 0)
        {
            bodyStateLbl.Text = has ? "✓ 已就绪，可以单机或开房" : "✗ 没有找到游戏本体（只能加入别人开的房间）";
            bodyStateLbl.ForeColor = has ? C_MINT : Color.FromArgb(240, 170, 90);
            bodyPathLbl.Text = has ? g : "点下面「浏览指定游戏位置…」选中 Stronghold-Protocol 文件夹";
        }
        if (setPathLabel != null && page == 2)
        {
            setPathLabel.Text = has ? g : "✗ 未找到（可点「浏览指定…」）";
            setPathLabel.ForeColor = has ? C_TEXT : Color.FromArgb(240, 170, 90);
        }
        FitPage(page);   // ★ 可见性变了就重算滚动，否则内容长高后没人再管
    }

    static string GameLog()
    {
        string g = FindGame(false);
        return g == null ? "未找到游戏本体" : "游戏本体：" + g;
    }
    // ⚠ 死代码（无调用方）：已被 RefreshGameUi 取代
    static void RefreshGameLine()
    {
        if (gameLine == null) return;
        string g = FindGame(false);
        if (IsHost)
        {
            gameLine.Text = g == null ? "⚠ 未找到游戏本体" : "✓ 已找到游戏本体";
            gameLine.ForeColor = g == null ? Color.FromArgb(240, 170, 90) : C_MINT;
        }
        else
        {
            gameLine.Text = "✓ 加入模式（不需要本体）";
            gameLine.ForeColor = C_DIM;
        }
    }

    static void SetMode(bool host)
    {
        if (modeHost == null) return;
        seats[2] = host ? "1" : (seatBox != null && seatBox.Text.Trim().Length > 0 && seatBox.Text.Trim() != "1" ? seatBox.Text.Trim() : "2");
        modeHost.BackColor = host ? C_ON : C_OFF;
        modeJoin.BackColor = host ? C_OFF : C_ON;
        if (roomBox != null) roomBox.Text = seats[0];
        if (passBox != null) passBox.Text = seats[1];
        if (seatBox != null) seatBox.Text = seats[2];
        ApplyModeUi(host);
        RefreshGameUi();
    }

    // 唯一定义的联机页布局：先设可见性，再一次性设好所有坐标（避免两种模式互相覆盖）
    static void ApplyModeUi(bool host)
    {
        bool roomOn = startBtn != null && startBtn.Text.Trim() == "关闭房间";

        // --- 可见性 ---
        if (netHostTip != null) netHostTip.Visible = host;
        if (roomStatus != null) roomStatus.Visible = host;
        if (netLink != null) netLink.Visible = host;
        if (shareBtn != null) { shareBtn.Visible = host; shareBtn.Text = roomOn ? "关闭房间" : "开房并复制邀请"; }
        if (keyCopyBtn != null) { keyCopyBtn.Visible = host; keyCopyBtn.Text = "只复制链接"; }
        if (openBtn != null) openBtn.Visible = host;
        if (startBtn != null) startBtn.Visible = false;   // 由 shareBtn 兼任开关
        if (netJoinTip != null) netJoinTip.Visible = !host;
        if (joinHint != null) joinHint.Visible = !host;
        if (joinPaste != null) joinPaste.Visible = !host;
        if (joinBtn != null) joinBtn.Visible = !host;
        if (killBtn != null) killBtn.Visible = true;
        if (advToggle != null) advToggle.Visible = true;

        // --- 坐标（设计单位，固定值） ---
        if (host)
        {
            if (netHostTip != null) { netHostTip.Text = roomOn ? "① 房间已开好，下面是发给粥友的链接" : "① 点下面的绿色大按钮开房"; netHostTip.SetBounds(Sc(4), Sc(56), Sc( CW()), Sc(26)); }
            if (roomStatus != null)
            {
                roomStatus.Text = roomOn ? "✅ 房间已开好 · 服务器运行中" : "当前状态：未开房";
                roomStatus.ForeColor = roomOn ? C_MINT : Color.FromArgb(150, 200, 255);
                roomStatus.SetBounds(Sc(4), Sc(86), Sc( CW()), Sc(26));
            }
            if (netLink != null) { netLink.Text = roomOn ? RoomUrl(true) : "点下面的绿色按钮后，这里会出现给粥友的链接"; netLink.SetBounds(Sc(4), Sc(116), Sc( CW()), Sc(32)); }
            if (shareBtn != null)
            {
                // ★ 三个按钮按比例分配可用宽度：窄窗口自动变窄，既不越界也不换行（换行会压到下面的按钮）
                int avail = Math.Max(360, CW() - 8), gap2 = 12;
                int baseSum = 230 + 190 + 190;
                double k = Math.Min(1.0, (double)(avail - 2 * gap2 - 8) / baseSum);
                int w1 = (int)(230 * k), w2 = (int)(190 * k), w3 = Math.Max(120, avail - 2 * gap2 - 8 - w1 - w2);
                shareBtn.SetBounds(Sc(4), Sc(158), Sc(w1), Sc(50));
                if (keyCopyBtn != null) keyCopyBtn.SetBounds(Sc(4 + w1 + gap2), Sc(158), Sc(w2), Sc(50));
                if (openBtn != null) openBtn.SetBounds(Sc(4 + w1 + gap2 + w2 + gap2), Sc(158), Sc(w3), Sc(50));
            }
            if (killBtn != null) killBtn.SetBounds(Sc(4), Sc(226), Sc(190), Sc(34));
            if (advToggle != null) advToggle.SetBounds(Sc(206), Sc(226), Sc(130), Sc(34));
            if (advPanel != null) advPanel.SetBounds(Sc(0), Sc(274), Sc( CW()), Sc(100));
            if (roomInfo != null) roomInfo.SetBounds(Sc(4), Sc(advOpen ? 384 : 274), Sc(CW()), Sc(22));
            if (membersLine != null) membersLine.SetBounds(Sc(200), Sc(advOpen ? 412 : 302), Sc(CW() - 200 - 8), Sc(20));
        }
        else
        {
            if (netJoinTip != null) netJoinTip.SetBounds(Sc(4), Sc(56), Sc( CW()), Sc(26));
            if (joinHint != null) joinHint.SetBounds(Sc(4), Sc(84), Sc( CW()), Sc(20));
            if (joinPaste != null) joinPaste.SetBounds(Sc(4), Sc(112), Sc( CW()), Sc(34));
            if (joinBtn != null) joinBtn.SetBounds(Sc(4), Sc(160), Sc(200), Sc(48));
            if (killBtn != null) killBtn.SetBounds(Sc(4), Sc(226), Sc(190), Sc(34));
            if (advToggle != null) advToggle.SetBounds(Sc(206), Sc(226), Sc(130), Sc(34));
            if (advPanel != null) advPanel.SetBounds(Sc(0), Sc(274), Sc( CW()), Sc(100));
            if (roomInfo != null) roomInfo.SetBounds(Sc(4), Sc(advOpen ? 384 : 274), Sc(CW()), Sc(22));
            if (membersLine != null) membersLine.SetBounds(Sc(200), Sc(advOpen ? 412 : 302), Sc(CW() - 200 - 8), Sc(20));
        }
        if (advPanel != null) advPanel.Visible = advOpen;
        FitPage(page);   // ★ 可见性变了就重算滚动，否则内容长高后没人再管
    }
    static void ApplyAdvancedVisibility()
    {
        // ★ 展开时把下面这组（会话信息/日志标题/成员行/日志框）整体下移，否则会被高级面板压住、
        //   成员行还会落进日志框里（原来只有 RefreshGameUi 会摆这几个，点开高级时没人管）
        int dy = advOpen ? 110 : 0;
        if (roomInfo != null) roomInfo.SetBounds(Sc(4), Sc(274 + dy), Sc(CW()), Sc(22));
        if (membersLine != null) membersLine.SetBounds(Sc(200), Sc(302 + dy), Sc(CW() - 200 - 8), Sc(20));
        if (logTitle != null) logTitle.SetBounds(Sc(4), Sc(374 + dy), Sc(100), Sc(20));
        if (logBox != null) logBox.SetBounds(Sc(4), Sc(398 + dy), Sc(CW()), Sc(130));
        if (advPanel == null) return;
        advPanel.Visible = advOpen;
        int baseY = IsHost ? 244 : 288;
        int y = advOpen ? baseY + 110 : baseY;
        if (roomInfo != null) roomInfo.SetBounds(Sc(4), Sc(advOpen ? 384 : 274), Sc(CW()), Sc(22));   // ★ 和另外两处统一：以前用局部 y，展开高级时会和成员行压 8px
            FitPage(page);   // ★ 重排后必须重算页面高度/滚动，否则下移后的日志框会被判成越界
}

    // 粘贴即进房：从邀请内容里自动取出房间名/密码并直接打开页面
    static void PasteJoin()
    {
        string s = joinPaste == null ? "" : joinPaste.Text.Trim();
        if (s.Length == 0) { MessageBox.Show("先把房主发的链接粘贴进来（Ctrl+V）。"); return; }
        string url = null, rn = null, pw = null, rm = null, inviteToken = null;
        foreach (string tok in s.Split(new char[] { ' ', '\r', '\n', '\t' }))
        {
            if (tok.StartsWith("http://") || tok.StartsWith("https://")) { url = tok; break; }
        }
        if (url == null)
        {
            int i = s.IndexOf("http");
            if (i >= 0) { int j = s.IndexOf(' ', i); url = j > i ? s.Substring(i, j - i) : s.Substring(i); }
        }
        if (url == null)
        {
            MessageBox.Show("没找到链接。请让房主点「开房并复制邀请」，把整段内容发给你，你整段粘进来就行。", "粘贴的内容里没有链接");
            return;
        }
        foreach (string kv in url.Split('?', '#')[url.Contains("?") ? 1 : 0].Split('&'))
        {
            int e = kv.IndexOf('=');
            if (e <= 0) continue;
            string k = kv.Substring(0, e);
            string v = Uri.UnescapeDataString(kv.Substring(e + 1));
            if (k == "room") rm = v;
            else if (k == "rn") rn = v;
            else if (k == "pw") pw = v;
            else if (k == "o2p") inviteToken = v;
            else if (k == "peer") joinPeerNode = v;
        }
        if (rm != null && rm.Length > 0) { seats[0] = rm; }
        if (pw != null && pw.Length > 0) { seats[1] = pw; }
        if (rn != null && rn.Length > 0)
        {
            try { seats[0] = Encoding.UTF8.GetString(Convert.FromBase64String(rn)); } catch { }
        }
        if (roomBox != null) roomBox.Text = seats[0];
        if (passBox != null) passBox.Text = seats[1];
        // 邀请里带了房主的联机令牌 → 直接保存，粥友不用手填（老邀请没有 o2p= 时下面还会弹框问一次）
        if (inviteToken != null && TokenNorm(inviteToken).Length > 0)
        {
            SaveToken(inviteToken);
            Log("已从邀请里读取联机令牌并保存");
        }
        SaveCfg();
        if (seats[1].Length == 0) { MessageBox.Show("链接里没有房间密码。请让房主点「开房并复制邀请」后把完整内容发你。", "链接不完整"); return; }
        if (Token().Length == 0)
        {
            string tk = AskToken("这次邀请里没有联机令牌（大概是房主用的是旧版启动器）。\n\n" +
                "请找房主把他启动器里的联机令牌发给你（设置页「联机令牌」能看到尾号），粘进来即可：");
            if (tk.Length == 0) { FlashStatus("○ 没填令牌，进房没启动", Color.FromArgb(240, 170, 90)); return; }
            SaveToken(tk);
        }
        RefreshTokenUi();
        Log("已从链接读取房间信息，准备进房…");
        GoMultiplayer(false);
        JoinOpenWhenReady();   // ★ 不再立刻开浏览器：隧道握手要十几秒，那时打开必定"无法访问"
    }

    static Panel setPageA, setPageB;      // 设置页两个子页：基本 / 分享与关于
    static bool setShareOn;               // 当前停在哪一页
    static Button setTabA, setTabB;
    static int PanelNeed(Panel p)
    {
        if (p == null) return 120;
        int need = 120;
        foreach (Control k in p.Controls) if (k.Visible && k.Bottom + 10 > need) need = k.Bottom + 10;
        return need;
    }
    static void ShowSetPage(bool share)
    {
        if (setPageA == null) return;
        setShareOn = share;
        // 先都设为可见再量高度（隐藏面板里的控件 Visible 一律 false，量不准）
        setPageA.Visible = true; setPageB.Visible = true;
        setPageA.Height = PanelNeed(setPageA); setPageB.Height = PanelNeed(setPageB);
        setPageA.Visible = !share; setPageB.Visible = share;
        if (setTabA != null) setTabA.BackColor = share ? C_PANEL : C_ON;
        if (setTabB != null) setTabB.BackColor = share ? C_ON : C_PANEL;
        FitPage(page);
    }
    // ★ 设置页内容分两页：六段竖排需要 882 设计单位，而标准窗口只有 562 → 必须拆，否则任何机器都要滚
    static void BuildSettings(Panel host)
    {
        setTabA = B(host, "基本", 4, 4, 96, 30, C_ON, delegate { ShowSetPage(false); });
        setTabB = B(host, "分享与关于", 108, 4, 132, 30, C_PANEL, delegate { ShowSetPage(true); });
        setPageA = new Panel(); setPageB = new Panel();
        setPageA.SetBounds(Sc(0), Sc(42), Sc(CW() + 8), Sc(120));
        setPageB.SetBounds(Sc(0), Sc(42), Sc(CW() + 8), Sc(120));
        setPageA.BackColor = C_BG; setPageB.BackColor = C_BG;
        host.Controls.Add(setPageA); host.Controls.Add(setPageB);
        BuildSettingsBase(setPageA);
        BuildSettingsShare(setPageB);
        ShowSetPage(false);
    }
    static void BuildSettingsBase(Panel host)
    {
        ResetCur(8);

        // ---- 游戏本体位置 ----
        Sec(host, "游戏本体位置");
        {
            string gp = FindGame(false);
            string gv = gp == null ? "" : GameLocalVersion();
            setPathLabel = Line(host, gp == null ? "✗ 未找到游戏本体（想自己开房就点下面的「下载游戏本体…」）"
                                                  : gp + (gv.Length > 0 ? "（本体 v" + gv + "）" : ""), C_TEXT, 9.5f);
            if (gp == null) setPathLabel.ForeColor = Color.FromArgb(240, 170, 90);
        }
        BtnRow(host, 14,
            Mk(host, "下载游戏本体…", 160, 32, C_ON, delegate { GameDownloadFlow(); }),
            Mk(host, "浏览指定…", 120, 32, C_OFF, delegate { string g = BrowseGame(); if (g != null) { Log("已指定：" + g); ShowPage(2); } }),
            Mk(host, "打开文件夹", 120, 32, C_OFF, delegate { string g = FindGame(false); if (g != null) { try { Process.Start("explorer.exe", "\"" + g + "\""); } catch { } } }),
            Mk(host, "重新检测", 120, 32, C_OFF, delegate { Log(FindGame(true) == null ? "仍未找到" : "已找到：" + gamePath); ShowPage(2); }));

        // ---- 联机令牌 ----
        Sec(host, "联机令牌");
        setTokenLabel = Line(host, "", C_TEXT, 9.5f);
        Line(host, "openp2p 组网账号：房主和粥友必须填同一个令牌，否则连不上。", C_DIM, 9f);
        BtnRow(host, 14,
            Mk(host, "修改联机令牌…", 160, 32, C_OFF, delegate
            {
                string tk = AskToken("当前：" + TokenMask() + "\n\n" +
                    "· 房主的令牌在 https://www.openp2p.cn 控制台里；如果本机装过 OPL，启动器会自动借用 OPL 里那份。\n" +
                    "· 粥友不用手填：房主发的邀请里带着令牌，粘一次邀请就自动保存了。");
                if (tk.Length > 0) { SaveToken(tk); RefreshTokenUi(); Log("联机令牌已更新：" + TokenMask()); }
            }));

        // ---- 安装与启动 ----
        Sec(host, "安装与启动");
        BtnRow(host, 14,
            Mk(host, "创建桌面快捷方式", 180, 34, C_OFF, delegate { CreateShortcuts(false); }),
            Mk(host, "安装到固定位置…", 180, 34, C_OFF, delegate { InstallFlow(); }),
            Mk(host, "卸载启动器", 140, 34, C_OFF, delegate { UninstallFlow(); }));
        {
            bool on = File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), AppName + ".lnk"));
            Button bb = Mk(host, on ? "开机自启：已开启（点此关闭）" : "开机自启：已关闭（点此开启）", 300, 32, on ? C_ON : C_OFF, null);
            bb.Click += delegate
            {
                bool cur = File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), AppName + ".lnk"));
                SetBoot(!cur); optAutoBoot = !cur; SaveCfg();
                bb.Text = !cur ? "开机自启：已开启（点此关闭）" : "开机自启：已关闭（点此开启）";
                bb.BackColor = !cur ? C_ON : C_OFF;
            };
            BtnRow(host, 14, bb);
        }

    }
    static void BuildSettingsShare(Panel host)
    {
        ResetCur(8);
        // ---- 打包分享 ----
        Sec(host, "打包分享给粥友");
        Line(host, "生成一个 zip：含启动器 + 组网引擎，可选择是否连游戏本体一起打包。", C_DIM, 9f);
        BtnRow(host, 14, Mk(host, "打包分享给粥友…", 220, 38, C_ON, delegate { PackDialog(); }));

        // ---- 关于与更新 ----
        Sec(host, "关于与更新");
        updateLine = Line(host, "启动时自动检查 GitHub 上的新版本；也可点下面按钮手动查", C_TEXT, 9.5f);
        Line(host, "换电脑后想确认界面有没有被裁/被挡：双击解压目录里的「④ 一键自检.bat」，它会生成报告。", C_DIM, 9f);
        setUpdBtn = Mk(host, "检查更新", 120, 32, C_OFF, delegate { CheckUpdateInteractive(); });
        BtnRow(host, 14,
            setUpdBtn,
            Mk(host, "自检", 100, 32, C_OFF, delegate
            {
                Log("开始自检…");
                ThreadPool.QueueUserWorkItem(delegate
                {
                    Log("· Node.js：" + (HasNode() ? "正常" + (HasPortableNode() ? "（包里自带的便携版）" : "（系统安装的）") : "未安装（不能开房，但能加入）"));
                    Log("· 组网引擎：" + (File.Exists(O2PExe()) ? "正常（openp2p）" : "缺失 openp2p\\openp2p.exe"));
                    Log("· 联机令牌：" + (Token().Length == 0 ? "未配置（联机时会弹框让用户填一次）" : TokenMask()));
                    string g = FindGame(false);
                    Log("· 游戏本体：" + (g == null ? "未找到" : "正常（" + g + "）"));
                    Log("· 游戏服务：" + (Health() == null ? "未运行" : "运行中"));
                    Log("· 安装状态：" + (IsInstalled() ? "已安装到固定位置" : "便携模式：" + AppDir));
                    Log("自检结束");
                });
            }),
            Mk(host, "打开项目主页", 140, 32, C_OFF, delegate { try { Process.Start("https://github.com/" + Repo); } catch { } }),
            Mk(host, "导出布局诊断", 150, 32, C_OFF, delegate
            {
                DumpLayout();
                MessageBox.Show("已导出：\n" + Path.Combine(LogDirRoot(), "布局诊断.txt"), "导出完成");
            }),
            Mk(host, "打开日志目录", 150, 32, C_OFF, delegate { try { Process.Start("explorer.exe", "\"" + LogDir() + "\""); } catch { } }));

        // ---- 声明 ----
        Sec(host, "声明与来源");
        Line(host, "启动器 v" + Version + "　·　本项目自制，代码 GPL-3.0-or-later", C_DIM, 9f);
        Line(host, "游戏本体：github.com/sganggs/Stronghold-Protocol（GPL-3.0-or-later）", C_DIM, 9f);
        Line(host, "游戏素材版权归上海鹰角网络 / Yostar，不适用 GPL。", C_DIM, 9f);
        Line(host, "非官方同人作品，与鹰角 / Yostar 无关；禁止任何形式盈利。", C_DIM, 9f);
        Line(host, "配置：同级目录下 启动器配置.txt（房间名/密码/令牌/节点名都在里面）", C_DIM, 9f);
    }
    // ================= 启动画面 =================
    static void Splash()
    {
        using (Form s = new Form())
        {
            s.FormBorderStyle = FormBorderStyle.None;
            s.StartPosition = FormStartPosition.CenterScreen;
            s.ClientSize = new Size(Sc(460), Sc(240));   // ★ 同上：子控件 Sc() 过，窗口也得乘
            if (cliDlgCheck != null) CheckDialogLater(s, "闪屏");
            s.BackColor = Color.FromArgb(14, 17, 21);
            s.TopMost = true;

            Panel bar = new Panel();
            bar.SetBounds(Sc(0), Sc(0), Sc(460), Sc(4));
            bar.BackColor = C_MINT;
            s.Controls.Add(bar);

            Label t = new Label();
            t.Text = GameName;
            t.Font = Fui(17f, FontStyle.Bold);
            t.ForeColor = C_MINT;
            t.SetBounds(Sc(30), Sc(52), Sc(400), Sc(46));
            t.AutoSize = true;
            s.Controls.Add(t);

            Label sub = new Label();
            sub.Text = "STRONGHOLD PROTOCOL · COVENANT";
            sub.Font = Fmono(9f);
            sub.ForeColor = C_DIM;
            sub.SetBounds(Sc(32), Sc(112), Sc(400), Sc(20));
            sub.AutoSize = true;
            s.Controls.Add(sub);

            Label st = new Label();
            st.Font = Fui(9.5f);
            st.ForeColor = C_TEXT;
            st.SetBounds(Sc(32), Sc(152), Sc(400), Sc(22));
            st.AutoSize = true;
            s.Controls.Add(st);

            ProgressBar pb = new ProgressBar();
            pb.SetBounds(Sc(32), Sc(184), Sc(396), Sc(6));
            pb.Style = ProgressBarStyle.Continuous;
            s.Controls.Add(pb);

            Label tag = new Label();
            tag.Text = "启动器 v" + Version + "　非官方同人";
            tag.ForeColor = C_DIM;
            tag.Font = Fui(8f);
            tag.SetBounds(Sc(32), Sc(204), Sc(300), Sc(18));
            s.Controls.Add(tag);

            s.Show();
            s.Refresh();

            // 进度按"真实完成的步数"推进，不做固定假等待；总时长设 260ms 下限避免闪一下就没
            DateTime t0 = DateTime.Now;
            st.Text = "检查运行环境…"; pb.Value = 25; s.Refresh();
            bool hasEt = File.Exists(EtCore());
            st.Text = "定位游戏本体…"; pb.Value = 50; s.Refresh();
            string g0 = FindGame(true);
            gamePath = g0 == null ? "" : g0;
            st.Text = "检查组网引擎…"; pb.Value = 75; s.Refresh();
            bool hasNodeNow = nodeCached.HasValue ? nodeCached.Value : true;
            st.Text = "准备界面…"; pb.Value = 100; s.Refresh();
            int spent = (int)(DateTime.Now - t0).TotalMilliseconds;
            if (spent < 260) Thread.Sleep(260 - spent);
            // ★ 自检模式：闪屏平时是"同步刷帧、不泵消息"，定时器根本不会触发 → 量到空壳。
            //   这里多停 1.5 秒并泵消息，让 CheckDialogLater 能真的量到它的子控件
            if (cliDlgCheck != null)
            {
                DateTime t2 = DateTime.Now;
                while ((DateTime.Now - t2).TotalMilliseconds < 1500) { Application.DoEvents(); Thread.Sleep(30); }
            }
            s.Close();
        }
    }

    // ================= 入口 =================
    // ★ 自动化自检模式（-layoutcheck / -uishake / -dlgcheck / -scrolltest）：
    //   这些模式下绝不能弹"要人点一下"的框——脚本会一直卡在那里等（实测：闪屏自检里
    //   Application.DoEvents() 泵消息时撞上首次运行的「安装到本机？」框 → 自检永久挂住，
    //   屏幕上还留一个模态框）。所以凡是首次运行的交互提示都跳过。
    static bool SelfTestMode()
    {
        return cliLayoutCheck || cliShake || cliScrollTest || cliDlgCheck != null;
    }

    [STAThread]
    static void Main(string[] args)
    {
        foreach (string a in args)
        {
            if (a == "-install") cliInstall = true;
            else if (a == "-uninstall") cliUninstall = true;
            else if (a == "-shortcut") cliShortcut = true;
            else if (a == "-nosplash") cliNoSplash = true;
            else if (a == "-host") cliHost = true;
            else if (a == "-join") cliJoin = true;
            else if (a == "-play") cliStart = true;
            else if (a == "-killserver") cliKill = true;
            else if (a.StartsWith("-seat:")) cliSeat = a.Substring(6);
            else if (a.StartsWith("-token:")) cliToken = a.Substring(7);
            else if (a.StartsWith("-page:")) { int pv; if (int.TryParse(a.Substring(6), out pv) && pv >= 0 && pv <= 2) cliPage = pv; }
            else if (a.StartsWith("-winsize:")) { string[] wh = a.Substring(9).Split(new char[] { 'x', 'X' }); int wv, hv; if (wh.Length == 2 && int.TryParse(wh[0], out wv) && int.TryParse(wh[1], out hv) && wv > 200 && hv > 200) cliWinSize = new Size(wv, hv); }
            else if (a == "-scrolltest") cliScrollTest = true;
            else if (a.StartsWith("-screensize:")) { string[] wh2 = a.Substring(12).Split(new char[] { 'x', 'X' }); int w2, h2; if (wh2.Length == 2 && int.TryParse(wh2[0], out w2) && int.TryParse(wh2[1], out h2) && w2 > 400 && h2 > 300) cliScreen = new Size(w2, h2); }
            else if (a.StartsWith("-forcedpi:")) { float fv; if (float.TryParse(a.Substring(10), out fv) && fv >= 50 && fv <= 400) cliForceDpi = fv; }
            else if (a == "-layoutcheck") cliLayoutCheck = true;
            else if (a.StartsWith("-dlgcheck:")) cliDlgCheck = a.Substring(10);
            else if (a.StartsWith("-gamedltest:")) cliGameDlTest = a.Substring(12);
            else if (a == "-tunnelsim") cliTunnelSim = true;
            else if (a.StartsWith("-packfull:")) { cliPackOut = a.Substring(10); cliPackFull = true; }
            else if (a.StartsWith("-pack:")) cliPackOut = a.Substring(6);
            else if (a == "-uishake") cliShake = true;
            else if (a.StartsWith("-settab:")) { int tv; if (int.TryParse(a.Substring(8), out tv)) cliSetTab = tv; }
            else if (a.StartsWith("-uifont:")) cliUiFont = a.Substring(8);
            else if (a.StartsWith("-resize:")) { string[] wh3 = a.Substring(8).Split(new char[] { 'x', 'X' }); int w3, h3; if (wh3.Length == 2 && int.TryParse(wh3[0], out w3) && int.TryParse(wh3[1], out h3) && w3 > 200 && h3 > 200) cliResize = new Size(w3, h3); }
        }
        if (cliForceDpi > 0) dpiScale = cliForceDpi / 100f;   // 测试钩子：模拟别的电脑的缩放
        // -gamedltest：完全不走界面，跑完写报告就退出（离线验断点续传 / 解压剥壳 / 校验）
        if (cliGameDlTest != null) { GameDlTest(cliGameDlTest); return; }
        if (cliTunnelSim) { TunnelSimTest(); return; }
        // -pack: / -packfull: 无人值守打包（AppDir 决定包里装什么：所以要在**部署目录**里跑，
        //   这样打出来的轻量包只有 exe + openp2p + app.ico，不会把源码和内部文档一起打进去）
        if (cliPackOut != null) { PackFlow(cliPackFull); return; }
        LoadCfg();
        if (cliSeat.Length > 0) seats[2] = cliSeat;
        // 命令行直接写令牌（给脚本/批量部署用，省得一台台点界面）
        if (cliToken.Length > 0)
        {
            string tv = TokenNorm(cliToken);
            if (tv.Length == 0) { MessageBox.Show("令牌至少要有 8 位数字或字母。", "令牌不对"); return; }
            SaveToken(tv);
            MessageBox.Show("联机令牌已保存：" + TokenMask() + "\n\n配置：" + CfgFile, "完成");
            return;
        }

        if (cliKill) { bool ok2 = StopGameServer(); MessageBox.Show(ok2 ? "已结束游戏服务器进程。" : "没找到正在运行的游戏服务器。", "结束游戏进程"); return; }
        if (cliUninstall) { UninstallFlow(); return; }
        if (cliShortcut) { CreateShortcuts(true); MessageBox.Show("快捷方式已创建。", "完成"); return; }

        try { SetProcessDPIAware(); } catch { }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        // ★ 全局兜底：任何未处理异常都只写日志，绝不弹"应用程序发生异常"系统框
        //   （0xe0434352 那种框关都关不完，用户看到只会以为程序坏了）
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += delegate(object s1, ThreadExceptionEventArgs e1)
        {
            try { Log("⚠ UI 线程未处理异常：" + e1.Exception.Message); } catch { }
        };
        AppDomain.CurrentDomain.UnhandledException += delegate(object s2, UnhandledExceptionEventArgs e2)
        {
            try { Log("⚠ 未处理异常：" + (e2.ExceptionObject == null ? "?" : e2.ExceptionObject.ToString())); } catch { }
        };
        if (!cliNoSplash) Splash();

        form = new Form();
        form.Text = AppName + " · " + GameName;
        form.StartPosition = FormStartPosition.CenterScreen;
        {
            Rectangle wa = Wa();
            int cw = Math.Min(Sc(940), wa.Width - 40);
            int ch = Math.Min(Sc(640), wa.Height - 40);
            // ★ 下限也不能超过屏幕可用区：Win7 小屏 + 125%/150% 缩放时，固定下限 760x520
            //   会把窗口顶出屏幕底部（实测症状：设置页下半部被裁、「打包」按钮只露出一角）
            cw = Math.Max(Math.Min(760, wa.Width - 24), Math.Min(cw, wa.Width - 24));
            ch = Math.Max(Math.Min(520, wa.Height - 24), Math.Min(ch, wa.Height - 24));
            form.ClientSize = new Size(cw, ch);
        }
        // ★ 下限用"设计尺寸×缩放"：以前写 760x520 物理像素，高 DPI 下用户能把窗口拖到内容放不下
        form.MinimumSize = new Size(Math.Min(Sc(760), Math.Max(320, Wa().Width - 24)),
                                    Math.Min(Sc(520), Math.Max(280, Wa().Height - 24)));
        if (cliWinSize.Width > 0) { form.MinimumSize = new Size(0, 0); form.ClientSize = cliWinSize; }   // -winsize:WxH 测试钩子
        form.BackColor = C_BG;
        form.ForeColor = C_TEXT;
        form.Font = Fui(9.5f);
        form.AutoScaleMode = AutoScaleMode.None;

        // 顶栏
        Panel top = new Panel();
        top.SetBounds(0, 0, form.ClientSize.Width, Sc(46));
        top.BackColor = Color.FromArgb(13, 15, 19);
        top.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        form.Controls.Add(top);

        Label brand = new Label();
        brand.Text = "  卫戍协议：盟约　启动器";
        brand.Font = Fui(11.5f, FontStyle.Bold);
        brand.ForeColor = C_MINT;
        brand.SetBounds(Sc(12), Sc(0), Sc(340), Sc(46));
        brand.AutoSize = true;
        brand.TextAlign = ContentAlignment.MiddleLeft;
        top.Controls.Add(brand);

        statusLine = new Label();
        statusLine.Text = "○ 隧道未连接　○ 游戏服务未运行";
        statusLine.ForeColor = Color.FromArgb(150, 200, 255);
        statusLine.SetBounds(Sc(300), Sc(4), Sc(620), Sc(22));
        statusLine.TextAlign = ContentAlignment.MiddleRight;
        statusLine.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        statusLine.TextAlign = ContentAlignment.MiddleRight;
        statusLine.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        top.Controls.Add(statusLine);
        // ★ 看门狗（方案 A，2026-10-05）：断开卡住 90 秒后，这一行会变成可点的「重启组网」
        statusLine.Click += delegate { WatchdogRestart(); };

        // 左侧导航
        sidePanel = new Panel();
        sidePanel.SetBounds(0, Sc(46), Sc(190), form.ClientSize.Height - Sc(46));
        sidePanel.BackColor = C_PANEL;
        sidePanel.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left;
        form.Controls.Add(sidePanel);

        navHome = B(sidePanel, "🏠  游戏库", 0, 16, 190, 44, C_PANEL, delegate { ShowPage(0); });
        navNet = B(sidePanel, "🌐  联机开黑", 0, 64, 190, 44, C_PANEL, delegate { ShowPage(1); });
        navSet = B(sidePanel, "⚙  设置", 0, 112, 190, 44, C_PANEL, delegate { ShowPage(2); });

        sideTipLbl = new Label();
        sideTipLbl.Text = "房主负责开房发链接，\n粥友装上启动器就能加入。\n\n游戏本体与启动器都是\n粥友之间自用，禁止盈利。";
        sideTipLbl.ForeColor = C_DIM;
        sideTipLbl.Font = Fui(8.5f);
        sideTipLbl.SetBounds(Sc(16), form.ClientSize.Height - Sc(196), Sc(170), Sc(130));
        sideTipLbl.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        sidePanel.Controls.Add(sideTipLbl);

        // 内容区
        content = new Panel();
        content.AutoScroll = false;   // 由 FitPage() 按视口高与页面需求高自动开关
        // ★ 高度减 100（原来 78）：底部署名栏从 h-Sc(34) 开始，内容区到 h-Sc(38) 结束，留 4 单位不重叠
        content.SetBounds(Sc(206), Sc(62), form.ClientSize.Width - Sc(224), form.ClientSize.Height - Sc(100));
        content.BackColor = C_BG;
        content.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        SetDoubleBuffered(form);
        form.Controls.Add(content);
        SetDoubleBuffered(content);
        // 滚轮：把 MouseWheel 挂到页面里每个子控件上（见 HookWheelAll），不用全局消息过滤器

        // 版权声明固定在内容区底部（每页都可见，不再依赖各页自己的布局）
        legalLabel = new Label();
        legalLabel.Text = "非官方同人作品 · 游戏本体 sganggs/Stronghold-Protocol · 素材版权归鹰角/Yostar · 禁止盈利";
        legalLabel.Font = Fui(9f);
        legalLabel.ForeColor = Color.FromArgb(150, 156, 166);
        legalLabel.AutoSize = false;
        legalLabel.TextAlign = ContentAlignment.MiddleLeft;
        legalLabel.SetBounds(Sc(206), form.ClientSize.Height - Sc(34), form.ClientSize.Width - Sc(224), Sc(24));
        legalLabel.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        form.Controls.Add(legalLabel);
        legalLabel.BringToFront();

        BuildAllPages();
        ShowPage(0);
        Log("════════════ 新会话 " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ════════════");
        Log("运行位置：" + AppDir + (IsInstalled() ? "（已安装）" : "（便携模式）") + "　版本 v" + Version);
        Log(GameLog());
        Log(File.Exists(O2PExe()) ? "组网引擎：正常（openp2p）" : "⚠ 缺少 openp2p\\openp2p.exe");
        if (Token().Length == 0) Log("联机令牌：未配置（首次开房/进房时会弹框让用户填一次）");
        else Log("联机令牌：" + TokenMask() + "（来源：启动器配置.txt 或本机 OPL）");
        RefreshTokenUi();
        if (!HasNode()) Log("未检测到 Node.js：加入房间没问题，开房/单机时会提示安装");

        // ★ 窗体显示后可见性才真实，这时必须再适配一次（FitPage 在 DumpLayout 之前，诊断才反映真实状态）
        form.Shown += delegate { started = true; StartHealthLoop(); if (cliResize.Width > 0) { form.MinimumSize = new Size(0, 0); form.ClientSize = cliResize; } CheckState(); FitPage(page); DumpLayout(); CheckUpdateQuiet(); if (cliPage >= 0) ShowPage(cliPage); if (cliSetTab == 2) ShowSetPage(true);
        if (cliDlgCheck != null)
        {
            if (cliDlgCheck == "install") InstallFlow();
            else if (cliDlgCheck == "token") { AskToken("对话框自检"); }
            else if (cliDlgCheck == "pack") { PackDialog(); }
            else if (cliDlgCheck == "gamedl") { GameDownloadFlow(); }
            else if (cliDlgCheck == "splash") { Splash(); }
            ThreadPool.QueueUserWorkItem(delegate { try { Thread.Sleep(2000); System.Diagnostics.Process.GetCurrentProcess().Kill(); } catch { } });
        }
        if (cliShake) { ShakeTest(); return; }
        if (cliScrollTest) ScrollTest(); if (cliLayoutCheck) LayoutCheck(); };
        tick = new System.Windows.Forms.Timer(); tick.Interval = 2000; tick.Tick += delegate { CheckState(); }; tick.Start();
        form.ResizeBegin += delegate { resizing = true; };
        form.ResizeEnd += delegate { resizing = false; LayoutRoot(); };
        form.Resize += delegate { if (!resizing) LayoutRoot(); };
        form.FormClosing += delegate { try { exitSignal.Set(); StopAll(); } catch { } };

        // 命令行直接开房/加入
        if (cliHost || cliJoin || cliStart)
        {
            System.Windows.Forms.Timer t2 = new System.Windows.Forms.Timer(); t2.Interval = 600; t2.Tick += delegate
            {
                t2.Stop(); t2.Dispose();
                if (cliStart) StartSolo();
                else { ShowPage(1); SetMode(cliHost); GoMultiplayer(cliHost); }
            };
            t2.Start();
        }
        else if (cliInstall)
        {
            System.Windows.Forms.Timer t3 = new System.Windows.Forms.Timer(); t3.Interval = 600; t3.Tick += delegate { t3.Stop(); t3.Dispose(); InstallFlow(); };
            t3.Start();
        }
        else if (optAskInstall && !IsInstalled() && !File.Exists(Path.Combine(AppDir, ".portable")) && !SelfTestMode())
        {
            System.Windows.Forms.Timer t4 = new System.Windows.Forms.Timer(); t4.Interval = 900; t4.Tick += delegate
            {
                t4.Stop(); t4.Dispose();
                if (MessageBox.Show(
                    "要把启动器安装到固定位置吗？\n\n· 安装后创建桌面和开始菜单快捷方式\n· 当前位置：" + AppDir +
                    "\n\n选「否」继续以便携方式运行（之后可在设置里再安装）",
                    "安装到本机？", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                    InstallFlow();
            };
            t4.Start();
        }
        // 房间名与密码必须每人不同：首次运行随机生成一次，写进配置后保持不变
        try
        {
            if (seats[0] == DefaultRoomName || seats[1] == DefaultRoomPass)
            {
                if (seats[0] == DefaultRoomName) seats[0] = "WS" + randStr(6, "ABCDEFGHJKLMNPQRSTUVWXYZ23456789");
                if (seats[1] == DefaultRoomPass) seats[1] = randStr(10, "abcdefghijkmnpqrstuvwxyz23456789");
                SaveCfg();
                Log("已为本机生成专属房间名与密码：" + seats[0] + " / " + seats[1]);
            }
            if (roomBox != null) roomBox.Text = seats[0];
            if (passBox != null) passBox.Text = seats[1];
        }
        catch { }

        // 首次运行时自动建桌面快捷方式（仅分发包适用），并且"已存在就不动"，避免覆盖用户自己指向别处的快捷方式
        try
        {
            bool isDevFolder = IsDevFolder();   // 开发目录不打扰（不碰桌面资源）
            string deskLnk = Path.Combine(DesktopDir(), AppName + ".lnk");
            if (!isDevFolder && !SelfTestMode() && !File.Exists(deskLnk))   // ★ 自检也别在别人桌面上留快捷方式
            {
                System.Windows.Forms.Timer tLnk = new System.Windows.Forms.Timer();
                tLnk.Interval = 1500;
                tLnk.Tick += delegate
                {
                    tLnk.Stop(); tLnk.Dispose();
                    try
                    {
                        if (!File.Exists(deskLnk))
                        {
                            MakeShortcut(deskLnk, Application.ExecutablePath, "", GameName + " 联机启动器", AppDir);
                            Log("已创建桌面快捷方式：" + deskLnk);
                        }
                    }
                    catch { }
                };
                tLnk.Start();
            }
        }
        catch { }
        Application.Run(form);
    }
}
