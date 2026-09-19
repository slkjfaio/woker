# WorkBench 桌面端：安装、GitHub 发布和更新

不必先推源码才能打包。GitHub **Releases** 用来分发安装包和更新清单；源码仓库可以保持私有，另建公开发布仓库。
当前实现针对公开 GitHub Releases，不在客户端保存或分发 GitHub Token。私有 Release 不能直接给所有用户匿名更新，需要另建带鉴权的分发服务。

## 本次产物

`artifacts/2.1.0/WorkBench-2.1.0-win-x64-Setup.exe` 是 Windows x64 安装程序，可在安装向导选择当前用户有写入权限的路径（包括其他磁盘）。默认安装到 `%LOCALAPPDATA%\Programs\WorkBench`。后续更新使用原安装目录；卸载入口在 Windows“已安装的应用”中。

安装包带 .NET 和 Windows App SDK 自包含运行文件。安装后的程序由 EXE 和依赖文件组成，不能只复制 `woker.exe` 分发；对外只需给 Setup.exe。
本次未配置真实更新仓库：可安装后在“设置 → 应用更新”填 `用户名/仓库名`，或正式分发前使用 `-Repository` 重新构建。没有填写仓库时不会自动联网查更新。
EXE 尚未做 Authenticode 代码签名，Windows 可能显示未知发布者。生产签名应在生成 SHA-256 更新清单之前完成；签名后需重新计算清单和 SHA256SUMS。

本地验证：更新服务的 18 项检查通过（版本比较、预发布过滤、架构、仓库地址、校验失败、取消下载、GitHub 错误）；WinUI Release 构建通过；安装到带中文和空格的自定义目录、覆盖安装保留目录及安装后启动窗口均通过。尚未使用真实 GitHub Release 完成在线更新验证，需要提供实际发布仓库并发布下一个正式版本。独立发布额外检查 XBF 页面和 PRI 索引，避免仅编译成功却无法启动。

## 数据与地址

服务器地址、主题和会话配置继续保存在 `%LOCALAPPDATA%\WorkBench`，与安装路径分开，更新和卸载不会删除。首次启动会进入登录页填写服务器地址；连接失败页也可进入登录页修改地址。
HTTPS 反向代理部署填写 `https://你的域名/api`，使用默认 443 端口；Java 8080 若只配置 HTTP，就不能填写 `https://域名:8080`。
GitHub 更新源与业务服务器地址独立，发布新客户端无需修改服务器地址。
旧 MSIX 和新 EXE 是两种安装方式：先安装 EXE、确认运行正常后可卸载旧 MSIX；避免同时使用两个入口。MSIX 内不会尝试覆盖受保护安装目录。

## 本机构建

需要 Windows、.NET SDK 10、Windows SDK（WinUI 构建工具）和 Inno Setup 6。依赖通过 NuGet 还原，缓存目录为 `woker/.nuget`，不再使用开发机绝对路径。

在此目录执行：

```powershell
dotnet run --project tests/UpdateChecks/UpdateChecks.csproj -c Release
./packaging/build.ps1 -Version 2.1.1 -Repository '你的用户名/发布仓库'
```

Inno Setup 不在默认目录时加 `-IsccPath '安装路径\ISCC.exe'`。输出目录已存在时会拒绝混入旧文件；可使用新的 `-OutputRoot`。`-SkipPublish` 仅用于当前版本已完成 publish 后重新包装，仍检查程序集版本。

输出包括安装包、`update-win-x64.json` 和 `SHA256SUMS`。构建命令的 Version 同时设置程序集、安装程序、文件名和更新清单版本。当前默认是 2.1.0；后续发布使用更大的三段版本号，如 2.1.1。

## GitHub 分发（可以只有安装包，不公开源码）

1. 创建公开仓库，例如 `你的用户名/workbench-releases`，初始化 README。
2. 使用同一个仓库名构建，或者在客户端设置更新源。
3. 创建 Release，标签必须是 `v2.1.1`，上传同一次构建的这三个文件：
   - `WorkBench-2.1.1-win-x64-Setup.exe`
   - `update-win-x64.json`
   - `SHA256SUMS`
4. 发布为正式版本并设为 Latest。草稿和预发布不会触发更新。

也可安装并登录 GitHub CLI（`gh auth login`），然后运行：

```powershell
./packaging/publish-release.ps1 -Repository '你的用户名/发布仓库' -Version 2.1.1
```

该脚本只上传以上产物并创建**草稿**，核对后在 GitHub 点击 Publish release。不要上传整个项目目录、server.config.json、私钥或开发机缓存。升级版本时创建新的标签和 Release，避免替换正在分发的同版本产物。

## 自动构建

将此桌面目录作为独立源码仓库根目录时，`.github/workflows/desktop-release.yml` 可从 Actions 手动触发，输入版本和发布仓库。它在 Windows 构建并生成可下载的 Artifact，不直接公开发布；下载后将三个文件放入对应 Release。
若保留整个 Java/前端仓库结构，需要把 workflow 放到总仓库根目录 `.github/workflows/`，并调整打包脚本执行路径。
推送桌面源码前检查暂存区；目录包含历史原型 `woker/workbench-daily-log`，不需要发布。可用 `packaging/export-source.ps1` 导出只含当前项目、构建脚本、测试和 workflow 的干净副本，再在副本中初始化 Git。

## 客户端更新行为

- 启动时后台检查一次，不阻塞登录，离线失败不弹窗。
- 发现新版本显示提示；设置页也可手动检查。
- 用户确认后下载，显示进度，可取消。
- 检查正式版本、架构、发布资产地址、安装包长度和 SHA-256。失败不执行安装包，不关闭应用。
- 校验成功后关闭应用，打开安装向导并沿用当前目录。用户也可以取消安装；原版本继续保留。
- 更新清单和安装包来自同一公开 Release；SHA-256 用于完整性验证，不能替代发布账号安全或代码签名。

参考：[GitHub Releases API](https://docs.github.com/en/rest/releases/releases#get-the-latest-release)、[Inno Setup 目录选择](https://jrsoftware.org/ishelp/topic_setup_disabledirpage.htm)、[Windows App SDK 自包含部署](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/self-contained-deploy/deploy-self-contained-apps)。
