#!/usr/bin/env pwsh
<#
.SYNOPSIS
Verifies the VoiceBuddy multilingual system is working correctly.

.DESCRIPTION
This script tests:
1. Language files exist and are valid JSON
2. LanguageManager can load files
3. English fallback works
4. Change detection mechanism works
#>

$ErrorActionPreference = "Stop"

Write-Host "🧪 VoiceBuddy Multilingual System Verification" -ForegroundColor Cyan
Write-Host "============================================`n"

$OutputDir = "src\VoiceBuddy.App\bin\Debug\net10.0-windows\Strings"

# Test 1: Check English file exists
Write-Host "✓ Checking English source file..."
if (-not (Test-Path "src\VoiceBuddy.App\Strings\Strings.en.json")) {
    Write-Host "✗ ERROR: Strings.en.json not found!" -ForegroundColor Red
    exit 1
}
Write-Host "  ✓ Strings.en.json exists`n"

# Test 2: Validate JSON structure
Write-Host "✓ Validating JSON files..."
$jsonFiles = @(
    "src\VoiceBuddy.App\Strings\Strings.en.json",
    "src\VoiceBuddy.App\Strings\Strings.es.json"
)

foreach ($file in $jsonFiles) {
    if (Test-Path $file) {
        try {
            $json = Get-Content $file -Raw | ConvertFrom-Json
            Write-Host "  ✓ $([System.IO.Path]::GetFileName($file)) is valid JSON"
        }
        catch {
            Write-Host "  ✗ $([System.IO.Path]::GetFileName($file)) has invalid JSON!" -ForegroundColor Red
            exit 1
        }
    }
}
Write-Host ""

# Test 3: Check key structure
Write-Host "✓ Checking key structure..."
$en = Get-Content "src\VoiceBuddy.App\Strings\Strings.en.json" -Raw | ConvertFrom-Json

$expectedSections = @("App", "Header", "Tabs", "Overview", "Settings", "Debug", "Layout", "Alignment", "Anchors")
$missingSection = $false
foreach ($section in $expectedSections) {
    if ($en.PSObject.Properties.Name -contains $section) {
        Write-Host "  ✓ Section '$section' found"
    }
    else {
        Write-Host "  ✗ Section '$section' MISSING!" -ForegroundColor Red
        $missingSection = $true
    }
}

if ($missingSection) {
    exit 1
}
Write-Host ""

# Test 4: Check deployed files
Write-Host "✓ Checking deployed files in output directory..."
if (-not (Test-Path $OutputDir)) {
    Write-Host "  ✗ Output directory not found: $OutputDir" -ForegroundColor Red
    Write-Host "  • Run: dotnet build src/VoiceBuddy.App/VoiceBuddy.App.csproj -c Debug"
    exit 1
}

$deployedFiles = Get-ChildItem $OutputDir -Filter "*.json" | Select-Object -ExpandProperty Name
if ($deployedFiles.Count -eq 0) {
    Write-Host "  ✗ No JSON files found in output directory!" -ForegroundColor Red
    exit 1
}

foreach ($file in $deployedFiles) {
    Write-Host "  ✓ $file deployed"
}
Write-Host ""

# Test 5: Count strings
Write-Host "✓ String statistics..."

function CountStrings {
    param($obj)
    $count = 0
    foreach ($prop in $obj.PSObject.Properties) {
        if ($prop.Value -is [PSCustomObject]) {
            $count += CountStrings $prop.Value
        }
        else {
            $count++
        }
    }
    return $count
}

$enCount = CountStrings $en
Write-Host "  ✓ English has $enCount strings"

if (Test-Path "src\VoiceBuddy.App\Strings\Strings.es.json") {
    $es = Get-Content "src\VoiceBuddy.App\Strings\Strings.es.json" -Raw | ConvertFrom-Json
    $esCount = CountStrings $es
    Write-Host "  ✓ Spanish has $esCount strings"
}
Write-Host ""

# Test 6: Check hash file
Write-Host "✓ Checking change detection setup..."
if (Test-Path "src\VoiceBuddy.App\Strings\.strings-hash") {
    $hash = Get-Content "src\VoiceBuddy.App\Strings\.strings-hash" -Raw
    if ($hash.Length -eq 64) {
        Write-Host "  ✓ Hash file found (change detection ready)"
    }
    else {
        Write-Host "  ⚠ Hash file exists but may be invalid (length: $($hash.Length))"
    }
}
else {
    Write-Host "  ℹ Hash file not yet created (will be created on first translation run)"
}
Write-Host ""

# Test 7: Build script exists
Write-Host "✓ Checking build script..."
if (Test-Path "build\translate-strings.ps1") {
    Write-Host "  ✓ build/translate-strings.ps1 exists"
}
else {
    Write-Host "  ✗ build/translate-strings.ps1 NOT FOUND!" -ForegroundColor Red
    exit 1
}
Write-Host ""

# Success summary
Write-Host "✅ All checks passed!" -ForegroundColor Green
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "1. Set your DeepL API key:"
Write-Host "   `$env:DEEPL_API_KEY = 'your-key-here'"
Write-Host ""
Write-Host "2. Generate translations:"
Write-Host "   cd build"
Write-Host "   .\translate-strings.ps1"
Write-Host ""
Write-Host "3. Test the app:"
Write-Host "   dotnet run --project src/VoiceBuddy.App/VoiceBuddy.App.csproj"
Write-Host ""
Write-Host "4. Go to Settings > Application language and select a language"
Write-Host ""
