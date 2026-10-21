$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $env:TEMP ('DesktopDrawer-notes-check-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $artifacts | Out-Null
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$references = @('System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','Microsoft.CSharp.dll','System.Xaml.dll','System.Security.dll') | ForEach-Object { '/reference:' + (Join-Path $framework $_) }
$references += @('PresentationFramework.dll','PresentationCore.dll','WindowsBase.dll') | ForEach-Object { '/reference:' + (Join-Path (Join-Path $framework 'WPF') $_) }
$sources = @('DesktopDrawer.cs','ShellMenu.cs','ShellClipboard.cs','Notes.cs','DesktopLock\DesktopLock.cs','DesktopLock\DesktopPin.cs') | ForEach-Object { Join-Path $project $_ }
$executable = Join-Path $artifacts 'notes-check.exe'
& (Join-Path $framework 'csc.exe') /nologo /target:winexe /platform:x64 /codepage:65001 /main:NotesCheck ('/win32manifest:' + (Join-Path $project 'app.manifest')) ('/out:' + $executable) @references @sources (Join-Path $PSScriptRoot 'notes-check.cs')
if ($LASTEXITCODE -ne 0) { throw 'Notes check compilation failed' }
$check = Start-Process -FilePath $executable -ArgumentList @(('"' + $artifacts + '"'),('"' + (Join-Path $project 'DesktopDrawer.cs') + '"')) -WindowStyle Hidden -Wait -PassThru
Get-Content -LiteralPath (Join-Path $artifacts 'report.txt')
Write-Output $artifacts
if ($check.ExitCode -ne 0) { throw 'Notes check failed' }
