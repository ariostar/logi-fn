# HID++ 协议笔记：Fn 反转（`0x40A2`）

本文记录 `logifn` 依赖的报文格式，以及在罗技 K780 + Unifying 接收器上的实测结论。
实现代码见 [`src/LogiFn.cs`](../src/LogiFn.cs)。

## 结论

罗技键盘的 Fn 反转不是一个按键重映射，而是**设备固件里的一个状态位**：

- HID++ 2.0 功能 `0x40A2`：`fnInversionWithDefaultState`
- `fn0`（读）返回 `[state, default_state]`
- `fn1`（写）参数 byte0 = `state`：
  - `0` = 关闭反转（F 排直接发 F1–F12）
  - `1` = 打开反转（F 排发媒体键）

因此只要往设备写一个字节，就能得到和 Logi Options+ 相同的效果，不需要常驻软件。
状态保存在设备侧，但**不会持久保存**：键盘休眠、断电或换机后可能复位为出厂默认值。

## 报文格式

HID++ 2.0 短报文（7 字节，`reportID = 0x10`）：

```
[0x10][设备槽位][featureIndex][func<<4 | swId][参数...]
```

长报文（20 字节，`reportID = 0x11`）结构相同，只是参数区更长。

- `设备槽位`：Unifying 接收器下键盘通常是 `0x01`，可用 `probe` / `dev` 查找
- `featureIndex`：由 `getFeature(0x40A2)` 动态查询（K780 上为 `0x0C`），不要写死
- `swId`：软件 ID，本工具固定为 `0x0A`

请求示例（K780，槽位 `01`）：

| 操作 | 请求报文 | 说明 |
| --- | --- | --- |
| 查询功能索引 | `10 01 00 0A 40 A2 00` | `getFeature(0x40A2)` |
| 读取状态 | `10 01 0C 0A 00 00 00` | `fn0` |
| 写入 F 键为主 | `10 01 0C 1A 00 00 00` | `fn1`，`state = 0` |
| 写入媒体键为主 | `10 01 0C 1A 01 00 00` | `fn1`，`state = 1` |

应答中的 `featureIndex` 与函数号会回显，`fn0` 的应答第 5、6 字节分别为
`state` 和 `default_state`。

## `apply` 的完整流程

1. 枚举 HID 接口，筛选 `VID = 0x046D` 且 `UsagePage = 0xFF00` 的集合。
2. 优先选择 `usage = 0x0002`（长报文，20 字节）的接口，回退到 `usage = 0x0001`（7 字节）。
3. 用 `getFeature(0x0001)` 确认根功能可访问。
4. 遍历槽位 `01`–`06`，找到第一个响应 `getFeature(0x40A2)` 的槽位。
5. 发送 `fn0` 读取当前状态；若已是目标值则不做写入。
6. 发送 `fn1` 写入目标状态，再读一次做校验。
7. 失败时最多重试 4 次，间隔 300 ms（设备休眠时需要先被唤醒）。

## K780 上的两个坑

### 1. 不能寻址 `0xFF`

接收器地址 `0xFF` 会拒绝该功能，返回 HID++ 错误帧（`10 FF 8F ...`）。
必须直接寻址键盘槽位（`0x01`）。

### 2. 报文长度必须匹配接口

HF 接口 `usage = 0x0001` 只接受 `reportID = 0x10` 的 7 字节短报文；
应答要从 `usage = 0x0002`（20 字节，`0x11`）读取。写错接口会静默失败。

## 参考资料

- HID++ 2.0 功能文档（公开整理）：<https://openlogi.org/hidpp/features/x40a2-fn-inversion>
- Solaar（Linux 下同类开源工具）：<https://github.com/pwr-Solaar/Solaar>

实现代码为独立编写，未使用或复制上述项目的代码。
