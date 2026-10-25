<#
.SYNOPSIS
    Builds the Ecosystem Client (WPF, net8.0-windows) and its native
    asm_crypto DLL from ../../server/c_asm_crypto.

.DESCRIPTION
    Two stages:
      1. compile the shared C/ASM core into native\asm_crypto.dll with MinGW gcc
      2. dotnet build/publish the WPF app with the DLL copied next to the exe

    When no gcc is available the script warns and continues: the client runs on
    its managed fallback and the UI reports that state explicitly. Nothing here
    needs administrator rights.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\build.ps1

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\build.ps1 -Configuration Debug -Publish
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',

    [switch] $Publish,

    [switch] $SkipNative,

    [string] $RuntimeIdentifier = 'win-x64'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Push-Location $root

function Write-Step([string] $text) {
    Write-Host ""
    Write-Host "==> $text" -ForegroundColor Cyan
}

function Write-Ok([string] $text) {
    Write-Host "    $text" -ForegroundColor Green
}

function Write-Warn([string] $text) {
    Write-Host "    $text" -ForegroundColor Yellow
}

function Resolve-Gcc {
    $candidates = @(
        'gcc',
        'C:\msys64\mingw64\bin\gcc.exe',
        'C:\msys64\ucrt64\bin\gcc.exe',
        'C:\msys32\mingw32\bin\gcc.exe'
    )

    foreach ($candidate in $candidates) {
        $command = Get-Command $candidate -ErrorAction SilentlyContinue
        if ($command) { return $command.Source }
    }

    return $null
}

try {
    Write-Step 'toolchain'
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw 'dotnet was not found. Install the .NET 8 SDK: https://dotnet.microsoft.com/download/dotnet/8.0'
    }

    $sdkVersion = (& dotnet --version).Trim()
    Write-Ok "dotnet $sdkVersion"

    $project = Join-Path $root 'EcosystemClient.csproj'
    $cryptoDir = Join-Path $root '..\..\server\c_asm_crypto'
    $nativeDir = Join-Path $root 'native'
    $nativeDll = Join-Path $nativeDir 'asm_crypto.dll'

    if (-not (Test-Path $cryptoDir)) {
        throw "Expected the shared C/ASM core at '$cryptoDir'."
    }

    New-Item -ItemType Directory -Force -Path $nativeDir | Out-Null

    # ---------------------------------------------------------------- native
    if (-not $SkipNative) {
        Write-Step 'native core (asm_crypto.dll)'
        $gcc = Resolve-Gcc

        if ($gcc) {
            Write-Ok "using $gcc"
            $gccArgs = @(
                '-std=c11',
                '-O3',
                '-Wall',
                '-Wextra',
                ('-I' + (Join-Path $cryptoDir 'include')),
                '-shared',
                '-o', $nativeDll,
                (Join-Path $cryptoDir 'src\asm_crypto.c'),
                ('-Wl,--out-implib,' + (Join-Path $nativeDir 'libasm_crypto.a'))
            )

            & $gcc @gccArgs
            if ($LASTEXITCODE -ne 0) {
                throw "gcc failed with exit code $LASTEXITCODE."
            }

            Write-Ok "built $nativeDll"
        }
        else {
            Write-Warn 'gcc (MinGW-w64) not found - skipping the native DLL.'
            Write-Warn 'The client still runs on its managed fallback path.'
            Write-Warn 'Install MSYS2, then: pacman -S --needed mingw-w64-x86_64-gcc'
        }
    }

    # --------------------------------------------------------------- managed
    if ($Publish) {
        Write-Step "dotnet publish ($Configuration, $RuntimeIdentifier)"
        & dotnet publish $project -c $Configuration -r $RuntimeIdentifier --self-contained false -p:PublishReadyToRun=true
        if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }
        $outputDir = Join-Path $root "bin\$Configuration\net8.0-windows\$RuntimeIdentifier\publish"
    }
    else {
        Write-Step "dotnet build ($Configuration)"
        & dotnet build $project -c $Configuration
        if ($LASTEXITCODE -ne 0) { throw "dotnet build failed with exit code $LASTEXITCODE." }
        $outputDir = Join-Path $root "bin\$Configuration\net8.0-windows"
    }

    # ------------------------------------------------------------- staging
    Write-Step 'staging native library next to the executable'
    New-Item -ItemType Directory -Force -Path $outputDir | Out-Null

    if (Test-Path $nativeDll) {
        Copy-Item -Path $nativeDll -Destination (Join-Path $outputDir 'asm_crypto.dll') -Force
        Write-Ok "copied asm_crypto.dll -> $outputDir"
    }
    else {
        Write-Warn 'asm_crypto.dll not present; managed fallback will be used.'
    }

    Write-Step 'done'
    Write-Host ("    executable : " + (Join-Path $outputDir 'EcosystemClient.exe')) -ForegroundColor White
    Write-Host ("    native DLL : " + (Join-Path $outputDir 'asm_crypto.dll')) -ForegroundColor White
    Write-Host ""
    Write-Host "    Launch it with:" -ForegroundColor Gray
    Write-Host ("      " + (Join-Path $outputDir 'EcosystemClient.exe')) -ForegroundColor Gray
}
finally {
    Pop-Location
}
