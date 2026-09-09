$ErrorActionPreference = 'Stop'

$shaderDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$kitsRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
$fxc = Get-ChildItem -Path $kitsRoot -Filter fxc.exe -Recurse -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -match '\\x64\\fxc\.exe$' } |
    Sort-Object FullName -Descending |
    Select-Object -First 1

if ($null -eq $fxc) {
    throw 'fxc.exe was not found in the Windows 10/11 SDK.'
}

function Compile-Shader([string] $source, [string] $entry, [string] $target, [string] $output) {
    $sourcePath = Join-Path $shaderDir $source
    $outputPath = Join-Path $shaderDir $output
    & $fxc.FullName /nologo /Ges /O3 /T $target /E $entry /Fo $outputPath /I $shaderDir $sourcePath
    if ($LASTEXITCODE -ne 0) {
        throw "Shader compilation failed: $source $entry $target"
    }
}

Compile-Shader 'Mirror.hlsl' 'VSMain' 'vs_5_0' 'MirrorVS.cso'
Compile-Shader 'Mirror.hlsl' 'PSMain' 'ps_5_0' 'MirrorPS.cso'
Compile-Shader 'Mirror.hlsl' 'CursorVSMain' 'vs_5_0' 'CursorVS.cso'
Compile-Shader 'Mirror.hlsl' 'CursorPSMain' 'ps_5_0' 'CursorPS.cso'
Compile-Shader 'Luminance.hlsl' 'CSFrameStats' 'cs_5_0' 'LuminanceCS.cso'
Compile-Shader 'Luminance.hlsl' 'CSPointerProbe' 'cs_5_0' 'PointerProbeCS.cso'
Compile-Shader 'Gamut.hlsl' 'CSClearGamut' 'cs_5_0' 'GamutClearCS.cso'
Compile-Shader 'Gamut.hlsl' 'CSAnalyzeGamut' 'cs_5_0' 'GamutAnalysisCS.cso'
