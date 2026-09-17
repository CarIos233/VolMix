<#
  VolMix build script.

  Compiles the whole application with the in box .NET Framework 4.8 compiler
  (csc.exe, C# 5) and produces a single portable exe in outputs\VolMix.
  No SDK, no NuGet, no network access required.
#>

param(
    [string]$OutputDirectory,
    [switch]$SkipIcon,
    [switch]$Quiet
)

$ErrorActionPreference = 'Stop'

$workRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Split-Path -Parent $workRoot
$sourceDirectory = Join-Path $workRoot 'src'
$assetDirectory = Join-Path $workRoot 'assets'
$manifest = Join-Path $workRoot 'app.manifest'
$iconPath = Join-Path $assetDirectory 'VolMix.ico'

if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $projectRoot 'outputs\VolMix'
}

$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$referenceDirectory = 'C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8'

foreach ($required in @($csc, $referenceDirectory, $manifest, $sourceDirectory)) {
    if (-not (Test-Path $required)) {
        throw "required path missing: $required"
    }
}

$references = @(
    'WindowsBase.dll',
    'PresentationCore.dll',
    'PresentationFramework.dll',
    'System.Xaml.dll'
)

function Invoke-Compile {
    param(
        [string]$Target,
        [string]$Output,
        [string[]]$Sources,
        [string]$EntryPoint,
        [string]$IconFile
    )

    $arguments = New-Object System.Collections.Generic.List[string]
    $arguments.Add('/nologo')
    $arguments.Add('/utf8output')
    $arguments.Add('/codepage:65001')
    $arguments.Add('/target:' + $Target)
    $arguments.Add('/platform:x64')
    $arguments.Add('/optimize+')
    $arguments.Add('/nowarn:0618')
    $arguments.Add('/out:' + $Output)
    if ($EntryPoint) {
        $arguments.Add('/main:' + $EntryPoint)
    }
    if ($IconFile -and (Test-Path $IconFile)) {
        $arguments.Add('/win32icon:' + $IconFile)
    }
    foreach ($reference in $references) {
        $arguments.Add('/reference:' + (Join-Path $referenceDirectory $reference))
    }
    $arguments.Add('/win32manifest:' + $manifest)
    foreach ($source in $Sources) {
        $arguments.Add($source)
    }

    if (-not $Quiet) {
        Write-Host ("  csc " + $Target + " -> " + $Output)
    }

    $output = & $csc $arguments.ToArray() 2>&1
    if ($LASTEXITCODE -ne 0) {
        $output | ForEach-Object { Write-Host $_ }
        throw "compilation failed with exit code $LASTEXITCODE"
    }
    if ($output -and -not $Quiet) {
        $output | ForEach-Object { Write-Host $_ }
    }
}

# ---------------------------------------------------------------- icon asset

if (-not $SkipIcon -and -not (Test-Path $iconPath)) {
    if (-not $Quiet) { Write-Host 'generating application icon...' }
    $iconSources = @(
        (Join-Path $workRoot 'diag\IconGen.cs'),
        (Join-Path $sourceDirectory 'NativeMethods.cs'),
        (Join-Path $sourceDirectory 'AudioInterop.cs'),
        (Join-Path $sourceDirectory 'Theme.cs'),
        (Join-Path $sourceDirectory 'IconHelper.cs'),
        (Join-Path $sourceDirectory 'AppResolver.cs')
    )
    $tempExe = Join-Path $workRoot 'diag\icongen.exe'
    Invoke-Compile -Target 'exe' -Output $tempExe -Sources $iconSources -EntryPoint 'VolMixDiag.IconGen' -IconFile $null
    & $tempExe $iconPath
    if ($LASTEXITCODE -ne 0) {
        Write-Host 'icon generation failed, continuing without an exe icon'
    }
}

# -------------------------------------------------------------------- compile

if (-not (Test-Path $OutputDirectory)) {
    New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
}

$sources = Get-ChildItem -Path (Join-Path $sourceDirectory '*.cs') |
    Sort-Object Name |
    ForEach-Object { $_.FullName }

if (-not $Quiet) {
    Write-Host ("compiling VolMix (" + $sources.Count + " source files)...")
}

$exePath = Join-Path $OutputDirectory 'VolMix.exe'
Invoke-Compile -Target 'winexe' -Output $exePath -Sources $sources -EntryPoint 'VolMix.Program' -IconFile $iconPath

$info = Get-Item $exePath
if (-not $Quiet) {
    Write-Host ''
    Write-Host ("built: " + $info.FullName)
    Write-Host ("size : " + [math]::Round($info.Length / 1KB, 1) + " KB")
}

$exePath
