param(
    [Parameter(Mandatory = $true)][string]$SpritePath,
    [string]$OutputPath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'dist\鲸鱼娘桌宠.exe'),
    [string]$IconPath
)
$ErrorActionPreference = 'Stop'
$spriteFile = (Resolve-Path -LiteralPath $SpritePath).Path
$exeFile = [System.IO.Path]::GetFullPath($OutputPath)
$outputFolder = Split-Path -Parent $exeFile
if (-not (Test-Path -LiteralPath $outputFolder)) {
    New-Item -ItemType Directory -Path $outputFolder | Out-Null
}
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $compiler)) {
    throw 'The Windows .NET Framework C# compiler was not found.'
}
$compileArguments = @(
    '/nologo', '/target:winexe', '/platform:anycpu', '/optimize+', '/codepage:65001',
    '/r:System.Windows.Forms.dll', '/r:System.Drawing.dll',
    ('/out:' + $exeFile), ('/resource:' + $spriteFile + ',PetSprites'),
    (Join-Path $PSScriptRoot 'PetApp.cs')
)
if ($IconPath) {
    $compileArguments += ('/win32icon:' + (Resolve-Path -LiteralPath $IconPath).Path)
}
& $compiler @compileArguments
if ($LASTEXITCODE -ne 0) { throw ('C# compilation failed: ' + $LASTEXITCODE) }
Get-Item -LiteralPath $exeFile | Select-Object FullName, Length, LastWriteTime
