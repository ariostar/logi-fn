# Changelog

本项目的所有重要变更都记录在这里。
格式参考 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)，
版本号遵循 [语义化版本](https://semver.org/lang/zh-CN/)。

## [Unreleased]

## [0.1.0] - 2026-10-23

### Added

- `apply`：一次性把 F 排切换为 F1–F12
- `fn`：只读显示当前状态与出厂默认值
- `watch`：轻量守护，长时间空闲后自动校正，支持 `-log` 指定日志
- `list` / `probe` / `dev` / `fset` 等诊断命令
- 自动查找键盘槽位，并动态查询 `0x40A2` 的 feature index
- 无窗口版本 `logifnw.exe`，适合放入启动项
- 构建脚本，仅依赖 Windows 自带的 .NET Framework 4.x 编译器

[Unreleased]: https://github.com/ariostar/logi-fn/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/ariostar/logi-fn/releases/tag/v0.1.0
