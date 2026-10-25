<#
.SYNOPSIS
    Builds the native Windows Game Detector & Global Hotkey engine.
.DESCRIPTION
    Compiles win_input_hooks.exe and test_input_hooks.exe using GCC/MinGW or MSVC (cl.exe).
    Links with c_asm_crypto and native Win32 libraries (user32, ws2_32, psapi).
.PARAMETER Clean
    Removes build artifacts.
.PARAMETER Test
    Runs the automated unit & integration test suite after building.
.PARAMETER March
    Target architecture (e.g. -march=native).
#>
param (
    [switch]$Clean,
    [switch]$Test = $true,
    [string]$March = ""
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $ScriptDir

$BuildDir = Join-Path $ScriptDir "build"
$CryptoDir = Join-Path $ScriptDir "..\..\server\c_asm_crypto"

if ($Clean) {
    Write-Host "[clean] Removing $BuildDir..." -ForegroundColor Yellow
    if (Test-Path $BuildDir) {
        Remove-Item -Recurse -Force $BuildDir
    }
    Write-Host "[clean] Clean complete." -ForegroundColor Green
    return
}

if (-not (Test-Path $BuildDir)) {
    New-Item -ItemType Directory -Path $BuildDir | Out-Null
}

$GccPath = Get-Command gcc -ErrorAction SilentlyContinue
$ClPath = Get-Command cl -ErrorAction SilentlyContinue

if ($GccPath) {
    Write-Host "[build] Found GCC: $($GccPath.Source)" -ForegroundColor Cyan

    $CryptoSrc = Join-Path $CryptoDir "src\asm_crypto.c"
    $CryptoObj = Join-Path $BuildDir "asm_crypto.o"
    $CryptoInc = Join-Path $CryptoDir "include"
    $IncDir = Join-Path $ScriptDir "include"

    $CFlags = @("-std=c11", "-O3", "-Wall", "-Wextra", "-Wpedantic", "-I$IncDir", "-I$CryptoInc")
    if ($March) {
        $CFlags += "-march=$March"
    }

    Write-Host "[build] Compiling asm_crypto.c -> asm_crypto.o..."
    & gcc @CFlags -c $CryptoSrc -o $CryptoObj
    if ($LASTEXITCODE -ne 0) { throw "Failed to compile asm_crypto.c" }

    $Sources = @("src/main.c", "src/config.c", "src/window_tracker.c", "src/hotkey_engine.c", "src/protocol.c", "src/ws_client.c")
    $Objs = @()
    foreach ($src in $Sources) {
        $base = [System.IO.Path]::GetFileNameWithoutExtension($src)
        $obj = Join-Path $BuildDir "$base.o"
        $srcPath = Join-Path $ScriptDir $src
        Write-Host "[build] Compiling $src -> $obj..."
        & gcc @CFlags -c $srcPath -o $obj
        if ($LASTEXITCODE -ne 0) { throw "Failed to compile $src" }
        $Objs += $obj
    }

    $Bin = Join-Path $BuildDir "win_input_hooks.exe"
    Write-Host "[link] Linking $Bin..." -ForegroundColor Cyan
    & gcc @Objs $CryptoObj -o $Bin -lws2_32 -luser32 -lpsapi
    if ($LASTEXITCODE -ne 0) { throw "Failed to link win_input_hooks.exe" }
    Write-Host "[ok] Successfully generated: $Bin" -ForegroundColor Green

    # Build tests
    $TestSrc = Join-Path $ScriptDir "tests\test_input_hooks.c"
    $TestObj = Join-Path $BuildDir "test_input_hooks.o"
    Write-Host "[build] Compiling test_input_hooks.c..."
    & gcc @CFlags -c $TestSrc -o $TestObj
    if ($LASTEXITCODE -ne 0) { throw "Failed to compile test_input_hooks.c" }

    $TestBin = Join-Path $BuildDir "test_input_hooks.exe"
    $TestObjs = @($TestObj) + ($Objs | Where-Object { $_ -notlike "*main.o" }) + @($CryptoObj)
    Write-Host "[link] Linking test_input_hooks.exe..."
    & gcc @TestObjs -o $TestBin -lws2_32 -luser32 -lpsapi
    if ($LASTEXITCODE -ne 0) { throw "Failed to link test_input_hooks.exe" }

    if ($Test) {
        Write-Host "`n[test] Running $TestBin..." -ForegroundColor Magenta
        & $TestBin
        if ($LASTEXITCODE -ne 0) { throw "Tests failed with code $LASTEXITCODE" }
    }
} elseif ($ClPath) {
    Write-Host "[build] Found MSVC: $($ClPath.Source)" -ForegroundColor Cyan
    $CryptoSrc = Join-Path $CryptoDir "src\asm_crypto.c"
    $CryptoInc = Join-Path $CryptoDir "include"
    $IncDir = Join-Path $ScriptDir "include"

    $Sources = @("src\main.c", "src\config.c", "src\window_tracker.c", "src\hotkey_engine.c", "src\protocol.c", "src\ws_client.c", $CryptoSrc)
    $Bin = Join-Path $BuildDir "win_input_hooks.exe"
    & cl /O2 /W3 /I$IncDir /I$CryptoInc $Sources /Fe:$Bin /link ws2_32.lib user32.lib psapi.lib
    if ($LASTEXITCODE -ne 0) { throw "Failed to compile with MSVC" }
    Write-Host "[ok] Successfully generated: $Bin" -ForegroundColor Green
} else {
    throw "No supported C compiler found (neither GCC nor MSVC cl.exe in PATH)."
}
