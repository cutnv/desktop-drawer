# DesktopDrawer：桌面抽屉 + 桌面锁

一个 Windows 桌面工具：把桌面图标放入顶部抽屉，手动分类和排序；需要离开时，可手动开启使用独立 PIN 的桌面锁，保留桌面壁纸。

这个仓库用于备份程序、源码和构建方法。**误删项目文件夹后，可以从 GitHub 下载恢复。**

- 仓库：<https://github.com/cutnv/desktop-drawer>
- 恢复包：[下载最新的 DesktopDrawer-backup.zip](https://github.com/cutnv/desktop-drawer/releases/latest/download/DesktopDrawer-backup.zip)
- 备份版本说明：[Releases](https://github.com/cutnv/desktop-drawer/releases)

## 误删项目后，怎样恢复

### 方法一：下载恢复包

1. 下载上面的 `DesktopDrawer-backup.zip`。
2. 解压到一个固定位置，例如 `D:\codexproject\DesktopDrawer`。
3. 确认该目录下有 `DesktopDrawer.exe` 和 `DesktopDrawer.exe.config`。
4. 双击 `DesktopDrawer.exe`。程序后台启动，鼠标移到屏幕顶端即可呼出抽屉。
5. 如需立即展开，在该目录打开 PowerShell 后执行：

```powershell
.\DesktopDrawer.exe --show
```

恢复到新位置后，需要重新登记开机启动。在新目录执行：

```powershell
Start-Process -FilePath .\DesktopDrawer.exe -ArgumentList '--enable-startup' -Wait
```

也可以在系统托盘中找到「桌面抽屉」，右键设置「开机启动」。启动目录应固定，移动项目后请重新登记。

恢复包自带可执行文件，无需先安装 Visual Studio 或编译。它也包含源码；需要时可按下面的构建步骤重新生成程序。

### 方法二：通过 Git 克隆

安装 Git 后执行：

```powershell
git clone https://github.com/cutnv/desktop-drawer.git D:\codexproject\DesktopDrawer
Set-Location D:\codexproject\DesktopDrawer
.\DesktopDrawer.exe --show
```

仓库同时保存当前备份版本的 `DesktopDrawer.exe`。网页上的 **Code → Download ZIP** 也可下载整个项目。

### 程序能恢复，个人文件需要单独备份

| 内容 | 实际位置 | 是否包含在公开仓库／恢复包中 |
| --- | --- | --- |
| 程序、源码、构建脚本 | 本项目目录 | 是 |
| 已分类的个人文件 | Windows 真实桌面下的 `桌面分类\分类名称` | 否 |
| 待整理文件 | Windows 真实桌面，以及可用的公共桌面项目 | 否 |
| 图标排序、分类排序、系统图标归属 | `%LOCALAPPDATA%\DesktopDrawer` | 否 |
| 独立 PIN 记录 | `%LOCALAPPDATA%\DesktopDrawer\desktop-lock.pin` | 否 |

**只删除程序项目目录，通常不会删除实际桌面文件和上述本地配置。**在同一个 Windows 账户、同样的桌面路径下恢复程序，可继续使用仍然存在的本地数据。

如果桌面文件或本地配置也被删掉，这个仓库无法替代它们的个人备份。公开仓库中不应上传个人桌面文件、PIN 记录、日志或本地排序记录。

PIN 记录由当前 Windows 用户的 DPAPI 加密保护；换电脑、换 Windows 账户或重装系统后，可能无法继续使用原记录。桌面路径改变时，部分排序和系统图标归属也可能需要重新设置。

## 系统要求

- Windows 10／11，64 位。
- .NET Framework 4.8。
- 使用开机启动功能时，Windows 任务计划程序服务需要可用。
- 动态壁纸可继续由 Wallpaper Engine 等现有软件提供，本项目不内置壁纸资源。

## 桌面抽屉

- 自动隐藏原有桌面图标，鼠标碰屏幕顶端后下拉展开。
- 抽屉横跨当前显示器，约占屏幕高度的三分之一，使用暗色半透明界面。
- 其他应用最大化或全屏时，抑制顶部自动唤出；也可从托盘手动展开。
- 手动建立分类，拖动文件到分类标签；**普通文件会实际移动到分类文件夹**。
- 分类标签和分类内图标均支持拖动排序。
- 新保存到桌面的文件会进入「待整理」。
- 搜索框按文件名搜索全部分类和待整理中的直接项目，不递归搜索子文件夹内容。
- 提供 Windows 文件右键菜单、复制／剪切／粘贴、重命名、属性和删除等常用操作。
- 此电脑、控制面板、回收站等系统图标通过归属记录分类，不实际移动或删除系统对象。
- 抽屉右下角每秒更新整台电脑的 CPU 和物理内存占用。

分类使用真实文件夹。移动遇到同名目标时不会静默覆盖。通用撤销快捷键等 Windows 桌面功能尚未全部实现。

### 常用快捷键

文件操作在抽屉内进行；输入框有焦点时保留文字编辑行为。

| 快捷键 | 功能 |
| --- | --- |
| `Ctrl + Shift + N` | 在当前分类中新建文件夹；待整理中则建在真实桌面 |
| `F2` | 重命名选中项目 |
| `Delete` | 将可删除项目移到回收站 |
| `Shift + Delete` | 确认后永久删除 |
| `Ctrl + C / X / V` | 复制／剪切／粘贴 |
| `Ctrl + A` | 全选文件项目 |
| `Alt + Enter` | 属性 |
| `Ctrl + F / E` | 聚焦搜索框 |
| `F5` | 刷新 |
| `Esc` | 收起抽屉 |

## 桌面锁

### 手动开启、在桌面内解锁

1. 在抽屉右上角或托盘右键菜单中点击「桌面锁」。
2. 首次使用时设置独立的 **6 位数字 PIN**，重复输入确认。**保存 PIN 不会立即上锁。**
3. 再次手动点击「桌面锁」才开始保护。
4. 上锁后保留壁纸，拦截普通桌面操作和常用系统快捷键，抽屉与 Dock 无法正常操作。
5. 解锁入口仅位于主屏右下角，使用小号、半透明的「解锁」按钮；点击后在桌面内输入 PIN。
6. 正确验证后解除保护，恢复任务栏和原来最小化的窗口。

此功能不调用 Windows 锁屏、屏保或 Windows Hello。PIN 独立于 Windows 登录 PIN，以加盐 PBKDF2-SHA256 哈希形式保存，再由当前用户 DPAPI 加密；没有默认 PIN。源码自检中的数字仅用于临时测试。

连续输错 5 次后，暂停尝试 30 秒。程序不记录输入的 PIN。

### 保护范围

这是**应用层桌面保护**，不能保证防住 `Ctrl + Alt + Del`、任务管理器强制结束进程或管理员操作，也不提供文件加密或系统权限隔离。

程序带有看门狗，主进程异常退出时会尝试恢复桌面图标；如果退出时桌面锁仍在工作，还会尝试恢复任务栏和最小化窗口。若连同看门狗一起强制结束，可能需要手动重启 Windows 资源管理器。

忘记 PIN 时，可先通过任务管理器结束主进程并退出程序，再将本地 `desktop-lock.pin` 文件移到其他安全位置。重新启动后可重新设置独立 PIN；这不会更改 Windows 登录 PIN。

### 动态壁纸与轮播

全屏拦截层不会获取焦点，键盘焦点由主屏右下角的小窗口接收，以避免 Wallpaper Engine 把桌面锁识别为前台全屏应用后暂停壁纸。

项目不修改 Wallpaper Engine 的播放列表、切换间隔或播放策略。动态效果和定时轮播仍受壁纸引擎自己的暂停、音频规则、电源等设置影响。若锁定后动画或轮播异常，需要核对这些设置。

## 构建源码

不需要额外 NuGet 包或 Visual Studio。脚本调用 Windows 自带的 .NET Framework C# 编译器。

先退出正在运行的抽屉，避免可执行文件被占用；然后在项目目录执行：

```powershell
.\DesktopDrawer.exe --exit
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

`ExecutionPolicy Bypass` 仅用于这一次脚本进程，不修改系统的永久执行策略。构建成功后生成或更新根目录的 `DesktopDrawer.exe`。

构建脚本基于自身所在目录查找源码，项目无需固定在某个盘符或用户名下。

## 不上锁的自检

程序有两组自检，使用临时文件验证分类／排序／删除边界和 PIN 存储校验。**自检不会开启桌面锁。**

在项目目录的 PowerShell 中运行：

```powershell
foreach ($mode in @('--self-test', '--self-test-desktop-pin')) {
    $report = Join-Path $env:TEMP ($mode.TrimStart('-') + '.txt')
    $check = Start-Process -FilePath .\DesktopDrawer.exe `
        -ArgumentList @($mode, ('"' + $report + '"')) -Wait -PassThru
    Get-Content -LiteralPath $report
    if ($check.ExitCode -ne 0) { throw "自检失败：$mode" }
}
```

多屏、动态壁纸、文件右键菜单和输入拦截的完整效果，需由使用者手动操作验证。

## 命令行入口

| 参数 | 功能 |
| --- | --- |
| 无参数 | 后台启动；已有实例时展开抽屉 |
| `--show` | 启动并展开抽屉 |
| `--startup` | 静默启动；已有实例时不弹窗 |
| `--enable-startup` | 为当前程序位置登记登录启动任务 |
| `--disable-startup` | 关闭启动任务 |
| `--exit` | 正常退出，按程序启动时的状态恢复桌面图标 |
| `--restore-icons` | 退出并明确显示原有 Windows 桌面图标 |
| `--self-test <报告路径>` | 运行文件／分类自检 |
| `--self-test-desktop-pin <报告路径>` | 运行 PIN 自检 |

开机启动通过当前用户的 `DesktopDrawer` 任务实现：登录 Windows 后约 3 秒静默启动，不自动开启桌面锁。

如果程序删除后桌面图标仍隐藏，可先恢复程序，再运行：

```powershell
.\DesktopDrawer.exe --restore-icons
```

也可以使用 Windows 桌面右键菜单的「查看 → 显示桌面图标」。

## 项目结构

```text
DesktopDrawer/
├─ DesktopDrawer.exe             # 当前备份的可执行文件
├─ DesktopDrawer.exe.config      # .NET 运行配置
├─ DesktopDrawer.cs              # 抽屉、分类、搜索、启动任务与入口
├─ ShellMenu.cs                  # Windows Shell 右键菜单
├─ ShellClipboard.cs             # Windows 文件剪贴板与文件操作
├─ DesktopLock/
│  ├─ DesktopLock.cs             # 桌面拦截层、解锁界面与恢复
│  └─ DesktopPin.cs              # 独立 PIN 的加盐哈希、加密和校验
├─ app.manifest                  # x64、DPI 与权限声明
├─ build.ps1                     # 本地构建脚本
├─ SHA256SUMS.txt                # 备份版可执行文件校验值
└─ README.md
```

`DesktopLock` 是独立整理的源码模块，运行时仍由根目录的 `DesktopDrawer.exe` 提供统一入口。

## 后续修改怎样继续备份

GitHub 保存的是**已提交并推送的版本**，本地修改不会自动上传。修改和验证后，在项目目录执行：

```powershell
git add .
git commit -m "更新桌面抽屉与桌面锁"
git push
```

代码更新后需要恢复可执行文件时，请先重新构建，再提交新的 `DesktopDrawer.exe`，并更新 `SHA256SUMS.txt`。Release 中的恢复包是对应发布时的快照；后续推送不会自动替换旧恢复包。
