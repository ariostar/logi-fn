# 排查指南

## 先确认是否生效

在浏览器里按 `F11`：

- 全屏 = 已经是 F 键模式
- 变成调音量 = 还是媒体键模式

也可以只读当前状态：

```powershell
.\dist\logifn.exe fn
# state=0 (F 键为主)    default=1 (媒体键为主)
```

## 常见问题

### 执行后没反应，或提示找不到设备

1. 确认键盘已通过 Unifying 接收器连接，且按键能正常输入。
2. 暂时退出 Logi Options+ / Logi Bolt 等官方软件，避免同时占用设备。
3. 用诊断命令确认接口和槽位：

   ```powershell
   .\dist\logifn.exe list     # 列出所有 HID 接口
   .\dist\logifn.exe probe    # 找能应答 HID++ 的接口
   .\dist\logifn.exe dev      # 打印 feature 表
   ```

   正常应能看到 `vid=046D`、`usagePage=FF00`，以及 `getFeature(0x40A2)` 返回非零索引。

### 键盘休眠 / 唤醒后又变回媒体键

状态存在设备侧，休眠、断电或换机后可能复位。任选一种方式：

- 开机执行一次 `apply`（放入启动文件夹，见 README）
- 使用 `watch` 守护模式，在长时间空闲后的第一次输入时自动补写

```powershell
.\dist\logifn.exe watch 30
```

注意：唤醒后**第一次**按 F 排仍可能发出媒体键，守护进程会在约 1 秒内补写。
彻底消除需要监听 HID++ 通知，目前未实现。

### 想让守护进程后台运行、不显示窗口

用 `logifnw.exe`（同样的代码，编译为无窗口程序）：

```
"<路径>\dist\logifnw.exe" watch
```

日志默认写到 `%LOCALAPPDATA%\logifn\watch.log`，超过 100 KB 自动截断。
也可用 `watch -log <路径>` 指定日志文件。

### 开机自启

`Win+R` → `shell:startup`，把快捷方式放进打开的文件夹，目标为：

```
"<路径>\dist\logifn.exe" apply
```

用 `apply` 而不是 `watch` 可以完全不常驻。

### 退出码

| 退出码 | 含义 |
| --- | --- |
| `0` | 成功 |
| `1` | 失败 / 未找到设备 |
| `2` | 未知命令 |
| `3` | 内部看门狗超时（HID 操作卡住） |

## 反馈问题时请附上

- 键盘型号、连接方式（Unifying / Bolt / 蓝牙）
- Windows 版本
- `logifn.exe fn`、`logifn.exe list`、`logifn.exe dev` 的完整输出
- 如果使用 `watch`，附上日志末尾若干行

提交前请移除设备路径中的个人信息（如果需要）。

## 其它型号

只在 **K780（Unifying 接收器）** 上实测通过。其它实现 `0x40A2` 的型号理论上可用，
可用 `dev` / `probe` 自查；实现 `0x40A3`（多主机版本）的机型尚未支持。

开发期取证命令 `try` / `matrix` / `matrix2` / `matrix3` 会发送实验性报文，
日常使用不需要，也不建议在未知设备上运行。
