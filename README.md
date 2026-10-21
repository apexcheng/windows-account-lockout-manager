# Windows 账户锁定策略管理工具

Windows 原生图形程序，用于查看、切换和核验**本机账户锁定策略**。基于 .NET Framework 4 / Windows Forms，运行时不依赖 PowerShell 或第三方组件。

## 功能

| 模式 | 错误次数阈值 | 锁定时长 | 计数重置窗口 | 内置 Administrator 锁定 |
| --- | ---: | ---: | ---: | --- |
| 取消锁定限制 | 0（禁用） | 不生效 | 不生效 | 随阈值停用 |
| 原版 5/60/60 | 5 次 | 60 分钟 | 60 分钟 | 启用 |
| 新版 10/10/10 | 10 次 | 10 分钟 | 10 分钟 | 启用 |

双击 `dist/AccountLockoutManager.exe`，批准 Windows 管理员权限请求。程序启动后展示当前状态；点击所需方案、阅读确认弹窗并确认才会修改系统策略，修改后会通过 `secedit` 回读核验。

> **注意**
> - 该工具会修改真实 Windows 本地安全策略，请先确认有可用的管理员登录和恢复方式。无须也不应通过反复输入错误密码来验证。
> - 取消锁定**不会删除或重设账户密码**，也不保证立即解除已经被锁定的账户。
> - 阈值为 0 时，Windows 安全策略导出通常会省略锁定时长、重置窗口、管理员开关等不再生效的字段。
> - `AllowAdministratorLockout` 针对内置 Administrator 账户；旧系统或特定 Windows 版本可能不支持此设置，程序会显示核验结果。
> - 加入域、受组织组策略管理的电脑，实际生效值可能被域策略覆盖。
> - 可执行文件未经代码签名，系统可能显示 SmartScreen 提示；仅从可信来源获取并校验文件。

## 代码与产物

```text
src/
  AccountLockoutNative.cs     WinForms 界面、策略读取与修改
  AccountLockout.manifest      要求管理员权限的程序清单
tests/
  NativeSmokeTest.cs          不修改账户策略的基础测试
dist/
  AccountLockoutManager.exe   可直接运行的编译产物
build.cmd                     在 Windows 上重新编译
```

## 从源码构建

在已安装 .NET Framework 4.x 的 Windows 上运行 `build.cmd`。它使用系统自带的 `csc.exe`，编译为 AnyCPU WinForms EXE，并嵌入 `requireAdministrator` 清单；产物写入 `dist/AccountLockoutManager.exe`。

```cmd
build.cmd
```

可选：执行 `test.cmd` 重新编译并运行基础测试。测试会读取当前机器的策略，但**不会执行策略修改**，也不会尝试错误登录。

```cmd
test.cmd
```

本项目以 Windows 11 为主要运行环境。测试包含策略解析、三种目标配置判断、关闭阈值后缺少字段的处理、界面显示和基本程序集属性校验。发行版也曾在真实 Windows 设备上依次切换三个模式并回读确认，最后恢复原始策略；本仓库中的 `test.cmd` 不进行这类有风险的真实切换。
