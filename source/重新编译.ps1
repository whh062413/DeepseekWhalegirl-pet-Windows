$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'build.ps1') `
    -SpritePath (Join-Path $PSScriptRoot 'art\sprites.png') `
    -IconPath (Join-Path $PSScriptRoot 'art\pet.ico') `
    -OutputPath (Join-Path (Split-Path $PSScriptRoot -Parent) 'dist\鲸鱼娘桌宠.exe')
