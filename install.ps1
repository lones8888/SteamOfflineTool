# Steam Offline Tool - one-line install, no installer:
#   irm https://raw.githubusercontent.com/lones8888/SteamOfflineTool/main/install.ps1 | iex
# Downloads the latest release zip, extracts it to Desktop\SteamOfflineTool (notes are kept when updating) and starts it.
function Install-SteamOfflineTool {
    $ErrorActionPreference = 'Stop'
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $url = 'https://github.com/lones8888/SteamOfflineTool/releases/latest/download/SteamOfflineTool.zip'
    $target = Join-Path ([Environment]::GetFolderPath('Desktop')) 'SteamOfflineTool'
    if (Get-Process -Name SteamOfflineTool -ErrorAction SilentlyContinue) {
        Write-Host 'Steam Offline Tool is running. Close it first (tray icon > Exit), then run this line again.' -ForegroundColor Yellow
        return
    }
    $zip = Join-Path $env:TEMP 'SteamOfflineTool.zip'
    $tmp = Join-Path $env:TEMP ('SteamOfflineTool_' + [guid]::NewGuid().ToString('N'))
    Write-Host 'Downloading Steam Offline Tool...'
    Invoke-WebRequest -Uri $url -OutFile $zip -UseBasicParsing
    Expand-Archive -LiteralPath $zip -DestinationPath $tmp -Force
    $src = Join-Path $tmp 'SteamOfflineTool'
    New-Item -ItemType Directory -Force -Path $target | Out-Null
    $oldPython = Join-Path $target 'python'
    if (Test-Path -LiteralPath $oldPython) { Remove-Item -LiteralPath $oldPython -Recurse -Force }
    Get-ChildItem -LiteralPath $src | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $target -Recurse -Force }
    Remove-Item -LiteralPath $tmp -Recurse -Force
    Remove-Item -LiteralPath $zip -Force
    Start-Process -FilePath (Join-Path $target 'SteamOfflineTool.exe') -WorkingDirectory $target
    Write-Host "Done: $target" -ForegroundColor Green
}
Install-SteamOfflineTool
