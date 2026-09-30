# 工具工作台 · Desktop Workbench

<img src="assets/workbench.png" width="96" alt="Workbench icon">

可扩展的 Windows 原生桌面工作台，包含浙江大学校园网自动认证和 SSH 反向代理隧道。v1.2.2 增强退出原因记录、意外结束恢复与常用隧道持久化恢复。

## 使用

从 [Releases](https://github.com/aaAarJimedes/desktop-workbench/releases/latest) 下载便携 ZIP，完整解压后运行 `Workbench.exe`，保留同目录的 `Workbench.Contracts.dll`。需要 Windows 10/11 的 .NET Framework 4.x；隧道另需 Windows OpenSSH 客户端和已配置的 ssh-agent。无需 Python、Node 或额外 .NET SDK。

关闭窗口会隐藏到托盘。再次启动或双击托盘可打开原窗口。托盘“停止常驻并退出”停止工具和守护进程。界面可启用登录 Windows 后启动；移动程序后取消再启用以更新路径。命令行 `Workbench.exe --tray` 隐藏启动，`Workbench.exe --quit` 主动停止。

运行时有两个 Workbench.exe：守护进程和工作进程。工作进程异常退出后按 2 秒至 5 分钟退避恢复，连续稳定运行 5 分钟后重置退避。主动退出不会重启。关机、休眠期间不工作；守护进程本身被结束时无法恢复。不安装系统服务，也不保证解决进程卡死。

托盘菜单可打开 `%USERPROFILE%\.desktop-workbench\logs`。诊断仅记录时间、故障区域、异常类型和方法栈，不记录异常消息、响应或凭据。约 256 KiB 后轮转，保留一份旧日志。

## 自动联网

输入校园网账户、密码后保存。密码留空保留原密码，更换账户需同时输入密码。使用 Windows DPAPI CurrentUser 加密，只有当前 Windows 用户可解密。设置和密文位于 `%USERPROFILE%\.desktop-workbench`，不随程序分发。

启用自动恢复后每 30 秒检查 net3.zju.edu.cn，连续两次明确未认证才登录。14 天失效和设备挤掉均通过实时状态识别。手动检查也遵守限频。不能绕过校园网四设备限制，恢复本机可能使另一设备下线。

门户通过直连 HTTPS、系统证书验证及官方 challenge/HMAC-MD5/SRBX1/SHA1 流程认证。仅在明确返回 `not_online_error` 或 `ip_not_online_error` 时提交凭据。DNS、Wi-Fi、外网故障和未知响应不会触发盲目登录。成功后复核身份及外网连通性。

失败按 2、4、8、15 分钟退避，15 分钟最多 3 次，状态跨重启保存。明确拒绝认证会暂停自动恢复，请检查账户或官网要求后重新启用。门户协议改变可能需要更新适配器。

## 服务器隧道

添加 SSH 主机、端口、用户、密钥路径与转发端口。典型方向是服务器 `127.0.0.1:18080` → 加密 SSH → 本机 `127.0.0.1:7897` 代理。工具等待本机代理就绪后建立连接，网络中断按 10 秒至 5 分钟退避重连。

使用 Windows ssh-agent，不保存 SSH 密码或口令，不读取或复制私钥内容。带口令密钥先在可信终端用 `ssh-add` 解锁。严格验证主机密钥，未知或变更的密钥需自行核验。`ssh.exe -F none` 避免继承额外转发或命令；默认 known_hosts 仍生效。暂不配置跳板机。

端口占用或服务器拒绝转发时提示并停止重试，不结束其他连接。SSH 子进程由 Windows Job Object 管理，工作进程结束时释放。“隧道已建立”表示 SSH 确认转发；“测试外网”另行核验回环监听和 HTTPS。网站返回 401/403 不能直接等同于隧道故障。

“复制代理命令”供远端 shell 设置变量，工具不修改远端配置、系统路由或其他代理软件。隧道不是全局 VPN。配置仅存于 `%USERPROFILE%\.desktop-workbench\tunnels.json`，不分发个人服务器信息。点击停止只停止本次运行，取消自动启动才能阻止下次连接。

## 开发与扩展

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1 -Test
powershell -ExecutionPolicy Bypass -File .\package.ps1
```

构建使用 Windows 自带 C# 编译器；产物在 build/，发行包在 dist/。78 项自动检查覆盖校园网状态机与协议向量、隧道配置与生命周期、守护恢复、单实例、主动停止、日志脱敏及轮转。普通测试不连接真实账户或服务器，Windows CI 运行相同检查。

新工具实现 `IWorkbenchTool`，提供唯一 Id、标题、描述、WinForms 界面、Start 和 Dispose，引用 Workbench.Contracts.dll，把 DLL 放入程序旁 plugins/ 后重启。示例见 examples/HelloTool.cs：

```powershell
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /target:library /out:build\HelloTool.dll /r:build\Workbench.Contracts.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll examples\HelloTool.cs
```

插件是可信进程内代码，没有沙箱。后台任务不要阻塞 UI，退出时释放资源。宿主隔离初始化、启动、停止和主要定时回调的异常。

Program.cs 为宿主，Runtime.cs 管理常驻与诊断，Contracts.cs 为扩展契约，Monitor.cs/Network.cs 处理认证，Tunnels.cs 管理隧道。图标由 assets/workbench.svg 与 IconBuilder.cs 生成，覆盖 16–256 px，用于可执行文件、窗口和托盘。

reference-vector.js 可读取自行获取的官方 Portal.js 生成无真实凭据的协议验证向量。Diagnostics.cs 是开发入口，不加入发行包。`build\TunnelTests.exe --live <配置路径>` 会建立真实测试隧道，仅在有授权且远端端口空闲时使用。

## 验证与维护

v1.2.0 已通过 78 项自动检查，并在 Windows 实测工作进程被终止后的恢复、两条 SSH 子进程重新建立、重复启动、关闭窗口常驻和主动退出保持停止。此前偶发退出未找到对应 Windows 崩溃记录，不能断言原始原因已定位。未进行真实 14 天到期等待、Windows 重启或 Explorer 重启验收。

维护采用唯一 main 分支、不可移动的版本标签和一个正式 Release 页面。新版置顶，保留历史说明及实际附件。update.json 提供版本、下载地址与摘要；当前应用不自动下载更新。发布前检查暂存区无私人信息，上传后下载校验 SHA-256。

卸载前取消登录启动并停止常驻，再删除便携目录。清除账户可用“删除已存凭据”；彻底清除配置需自行移除上述本地应用数据目录。

## 日用版与开发版同步

密码输入框下方会显示“已保存密码（Windows 加密）”。输入新密码时显示“尚未保存”；修改账号、删除密码或无法解密时分别提示下一步操作。输入框清空不会清除已保存的密码。

首次在本机配置日用安装（路径仅为示例）：

```powershell
.\build.ps1 -Test -SkipDeploy
.\deploy.ps1 -TargetPath 'D:\softwares\DesktopWorkbench' -DeliveryPath 'D:\Agent Workspaces\Delivery\desktop-workbench' -Register
```

这会创建桌面“工具工作台”快捷方式；原来已启用的登录启动入口会改为日用版。只替换程序、契约 DLL、说明及图标，不清空 plugins，不复制或覆盖用户数据。账户密文、认证设置和隧道配置继续使用当前用户的 %USERPROFILE%\.desktop-workbench。

本机部署路径记录在不进入 Git 的 deploy.local.json。以后 `build.ps1 -Test` 全部通过后自动同步日用版和指定交付目录；原来在运行则恢复运行，原来主动退出则保持停止。`-SkipDeploy` 可仅验证构建，未通过测试的二进制不能部署。CI 没有本机路径配置，不执行本机安装。更新时仅短暂停止本工作台及其隧道。

## v1.2.2 常用隧道与退出诊断

隧道保存后立即写入用户目录，界面显示已保存条数。启动时会复核空列表，提供重新加载按钮；每次保存维护 tunnels.json.bak，主文件缺失或损坏时读取有效备份。恢复后保存会保留损坏的原文件供排查。自动启动选项随常用隧道保存。

守护进程只在明确停止请求、系统关闭或检测到另一个工作实例时退出。工作进程即使返回 0，只要没有停止请求，也会恢复。日志新增启动、每分钟存活记录、窗口关闭原因和停止来源。旧日志不足以追溯之前未记录的退出，不能把代码中的恢复缺口当作已证实的历史原因。守护进程或整个进程树被外部强制终止时仍需重新启动应用。

部署后通过当前用户的按需任务 DesktopWorkbench.Launch 启动日用版，使常驻生命周期独立于开发终端。该任务没有定时或登录触发器，不提升权限、不保存密码、没有运行时长或电池供电限制。已有登录启动设置仍由应用管理；卸载时可移除此任务。

数据目录已统一为 %USERPROFILE%\.desktop-workbench，避免打包开发宿主的 AppData 重定向造成桌面启动与开发启动各读一份配置。升级自动复制旧数据（仅在目标不存在时），账户与凭据成组迁移，不删除旧文件；日用版和开发版共享同一数据目录。
