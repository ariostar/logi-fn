# logifn

让罗技 K780（以及其它支持 HID++ `0x40A2` 的罗技键盘）的 **F1–F12 默认就是 F 键**，
不需要安装或常驻 Logi Options+。

- 单个 exe 约 **26 KB**，无第三方依赖（只用 .NET Framework 自带的 `csc` 编译）
- 默认**完全不常驻**：手动 / 开机执行一次 `apply` 即可
- 需要时才用 `watch` 做极轻量守护（空闲时不发任何 HID 报文，常驻内存约 18 MB）
- **不联网、不写注册表、不启动其它进程、不读写你的配置文件**，只对本机 HID 设备收发报文

> 非官方项目，与 Logitech 没有任何关联。

## 快速开始

```powershell
# 1. 编译（只需要系统自带的 .NET Framework 编译器，无需安装 SDK）
.\build.ps1

# 2. 把 F 排切成 F1–F12
.\dist\logifn.exe apply

# 3. 查看当前状态
.\dist\logifn.exe fn
#   当前状态 state=0 (F 键为主)   出厂默认 default=1 (媒体键为主)
```

验证方法：在浏览器里按 `F11`。
全屏 = 已经是 F 键模式；变成调音量 = 还是媒体键模式。

想开机自动校正，把下面这条放进启动文件夹（`Win+R` → `shell:startup`）：

```
"<路径>\dist\logifn.exe" apply
```

如果键盘休眠唤醒后状态会被复位，用守护模式代替（见下）。

## 命令

| 命令 | 作用 |
| --- | --- |
| `apply` | 立即把 F 排切成 F1–F12（已正确则不做任何写入） |
| `fn` | 只读：显示当前 `state` 与出厂默认值 |
| `watch [秒]` | 常驻守护。只在「长时间无输入后又开始输入」时才检查并补写，默认空闲阈值 30 秒 |
| `watch -log <路径>` | 同上，指定日志文件（默认 `%LOCALAPPDATA%\logifn\watch.log`，超过 100 KB 自动截断） |
| `help` | 帮助（无参数时默认执行） |
| `list` | 列出所有 HID 接口（排查用） |
| `probe [槽位]` | 探测哪个接口 / 槽位能应答 HID++ |
| `dev [槽位]` | 打印设备的 HID++ feature 表 |
| `fset <state> [fn] [槽位]` | 直接写 `0x40A2` 的 fn1，用于手工实验 |
| `try` / `matrix` / `matrix2` / `matrix3` | 开发期取证用的报文矩阵测试，日常不需要 |
| `get` / `set` | 早期版本走 `0xFF` 地址的旧路径，K780 上无效，仅留作对照 |

退出码：`0` 成功，`1` 失败 / 未找到设备。完整排错见 [docs/troubleshooting.md](docs/troubleshooting.md)。

## 原理

罗技键盘的 Fn 反转不是按键重映射，而是**设备固件里的一个状态位**：
HID++ 2.0 功能 `0x40A2`（`fnInversionWithDefaultState`）的 `fn1` 参数 byte0
就是当前状态（`0` = F 键为主，`1` = 媒体键为主）。

因此只要往设备写一个字节，就能得到和 Options+ 相同的效果，不需要任何常驻软件。
报文格式、K780 上的实测坑位和完整流程见 [docs/protocol.md](docs/protocol.md)。

## 已知限制

- **状态存在设备侧，不会持久保存**：键盘休眠 / 断电 / 换机后可能复位成出厂默认（媒体键）。
  需要 `watch` 或开机 `apply` 补一下。
- **唤醒后第一次按下仍是媒体键**：键盘空闲休眠后第一次按 F 排，可能要等到守护进程发现并补写
  （约 1 秒）。想彻底消除需要监听 HID++ 通知，目前未实现。
- 只在 **K780（Unifying 接收器）** 上实测通过。其它实现 `0x40A2` 的型号理论上可用，
  可用 `dev` / `probe` 自查；实现 `0x40A3`（多主机版本）的机型尚未支持。
- 仅 Windows。需要 .NET Framework 4.x（Win10/11 自带）。

## 构建

```powershell
.\build.ps1                  # 输出到 .\dist
.\build.ps1 -Clean           # 先清空输出目录
.\build.ps1 -OutputDir bin   # 自定义输出目录
```

若提示“禁止运行脚本”，改用：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

脚本等价于：

```powershell
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
& $csc /nologo /optimize+ /platform:anycpu /codepage:65001 /target:exe    /out:dist\logifn.exe  src\LogiFn.cs
& $csc /nologo /optimize+ /platform:anycpu /codepage:65001 /target:winexe /out:dist\logifnw.exe src\LogiFn.cs
```

`logifn.exe` 是控制台版（有输出，便于观察）；`logifnw.exe` 是同样的代码编译成无窗口程序，
适合放启动项后台运行。

## 目录结构

```
logi-fn/
├─ src/
│  └─ LogiFn.cs               # 全部实现（单文件，无依赖）
├─ docs/
│  ├─ protocol.md             # HID++ 报文格式与实测结论
│  └─ troubleshooting.md      # 排错、诊断、自启动
├─ .github/
│  ├─ workflows/build.yml     # CI：构建并上传产物，打 tag 时发布 Release
│  └─ ISSUE_TEMPLATE/         # Issue 模板
├─ build.ps1                  # 构建脚本（输出到 dist/）
├─ dist/                      # 构建产物（已 gitignore）
├─ CHANGELOG.md
├─ LICENSE
└─ README.md
```

## 参考

- HID++ 2.0 功能文档（公开整理）：<https://openlogi.org/hidpp/features/x40a2-fn-inversion>
- Solaar（Linux 下同类开源工具）：<https://github.com/pwr-Solaar/Solaar>

实现代码为独立编写，未使用或复制上述项目的代码。

## 免责声明

本项目非官方，与 Logitech 无任何关联，也未获其认可。
Logitech、Logi、Unifying、K780、HID++ 等名称是 Logitech 的商标，仅用于说明兼容性。
本程序会直接写入键盘固件状态，请自行承担风险；作者不对任何损失负责。

## 许可证

MIT，见 [LICENSE](LICENSE)。
