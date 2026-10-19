$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$references = @('System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll', 'Microsoft.CSharp.dll', 'System.Xaml.dll', 'System.Security.dll') | ForEach-Object { '/reference:' + (Join-Path $framework $_) }
$references += @('PresentationFramework.dll', 'PresentationCore.dll', 'WindowsBase.dll') | ForEach-Object { '/reference:' + (Join-Path (Join-Path $framework 'WPF') $_) }
& (Join-Path $framework 'csc.exe') /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 ('/win32manifest:' + (Join-Path $PSScriptRoot 'app.manifest')) ('/out:' + (Join-Path $PSScriptRoot 'DesktopDrawer.exe')) @references (Join-Path $PSScriptRoot 'DesktopDrawer.cs') (Join-Path $PSScriptRoot 'ShellMenu.cs') (Join-Path $PSScriptRoot 'ShellClipboard.cs') (Join-Path $PSScriptRoot 'DesktopLock\DesktopLock.cs') (Join-Path $PSScriptRoot 'DesktopLock\DesktopPin.cs')
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
