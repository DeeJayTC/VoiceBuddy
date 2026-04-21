#!/usr/bin/env pwsh
<#
.SYNOPSIS
Translates application strings using DeepL API with change detection.

.DESCRIPTION
This script reads Strings.en.json, detects if it has changed since last run,
and automatically translates it to configured target languages using DeepL API.
Translated files are written as Strings.{lang}.json.

.PARAMETER ApiKey
DeepL API key. If not provided, looks for DEEPL_API_KEY environment variable.

.PARAMETER Languages
Comma-separated list of target language codes (e.g., "de,fr,es,ja").

.EXAMPLE
.\translate-strings.ps1 -ApiKey "xxx" -Languages "de,fr,es"
#>

param(
    [string]$ApiKey = $env:DEEPL_API_KEY,
    [string]$Languages = "de,fr,es,it,nl,pl,pt,ja,zh,ko",
    [string]$Context = "UI labels for a desktop app made for voice translations and audio system integration."
)

$ErrorActionPreference = "Stop"

# Configuration
$ProjectRoot = Split-Path -Parent $PSScriptRoot
$StringsDir = Join-Path $ProjectRoot "src/VoiceBuddy.App/Strings"
$SourceFile = Join-Path $StringsDir "Strings.en.json"
$HashFile = Join-Path $StringsDir ".strings-hash"
$DeepLApiUrl = "https://api.deepl.com/v2/translate"
$TargetLanguages = $Languages -split "," | ForEach-Object { $_.Trim() }
$Utf8NoBom = [System.Text.UTF8Encoding]::new($false)

if (-not $ApiKey) {
    Write-Error "DeepL API key not provided. Set DEEPL_API_KEY environment variable or pass -ApiKey."
}

if (-not (Test-Path $SourceFile)) {
    Write-Error "Source file not found: $SourceFile"
}

# Compute current hash
Write-Host "Computing hash of $SourceFile..."
$CurrentHash = Get-FileHash $SourceFile -Algorithm SHA256 | Select-Object -ExpandProperty Hash
$HashSeed = "${CurrentHash}|$Context|$($TargetLanguages -join ',')"
$HashBytes = [System.Text.Encoding]::UTF8.GetBytes($HashSeed)
$SignatureHashBytes = [System.Security.Cryptography.SHA256]::Create().ComputeHash($HashBytes)
$CurrentSignatureHash = [System.BitConverter]::ToString($SignatureHashBytes).Replace("-", "")

# Check if file has changed
$HasChanged = $true
if (Test-Path $HashFile) {
    $PreviousHash = (Get-Content $HashFile -Raw -Encoding UTF8 -ErrorAction SilentlyContinue).Trim()
    if ($PreviousHash -eq $CurrentSignatureHash) {
        $HasChanged = $false
        Write-Host "No changes detected in source file. Skipping translation."
        exit 0
    }
}

Write-Host "Source file has changed. Translating to: $($TargetLanguages -join ', ')..."

# Load English strings
$EnglishContent = Get-Content $SourceFile -Raw -Encoding UTF8 | ConvertFrom-Json

# Function to flatten nested JSON for easier translation
function Flatten-Json {
    param([object]$Object, [string]$Prefix = "")
    
    $result = @{}
    
    foreach ($key in $Object.PSObject.Properties.Name) {
        $value = $Object.$key
        $fullKey = if ($Prefix) { "$Prefix.$key" } else { $key }
        
        if ($value -is [PSCustomObject]) {
            $flattened = Flatten-Json $value $fullKey
            $result += $flattened
        }
        else {
            $result[$fullKey] = $value
        }
    }
    
    return $result
}

# Function to unflatten JSON
function Unflatten-Json {
    param([hashtable]$Flat)
    
    $result = @{}
    
    foreach ($key in $flat.Keys) {
        $parts = $key -split "\."
        $current = $result
        
        for ($i = 0; $i -lt $parts.Count - 1; $i++) {
            $part = $parts[$i]
            if (-not $current.ContainsKey($part)) {
                $current[$part] = @{}
            }
            $current = $current[$part]
        }
        
        $lastPart = $parts[-1]
        $current[$lastPart] = $flat[$key]
    }
    
    return $result
}

# Flatten the English strings
$FlatStrings = Flatten-Json $EnglishContent
Write-Host "Found $($FlatStrings.Count) strings to translate."

$TermOverrides = @{
    de = @{
        "Settings.SourceLang" = "Quellsprache"
        "Settings.TargetLang" = "Zielsprache"
        "Settings.AppLanguage" = "Anwendungssprache"
    }
}

$null = Add-Type -AssemblyName System.Net.Http
$HttpClient = [System.Net.Http.HttpClient]::new()
$HttpClient.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", "DeepL-Auth-Key $ApiKey") | Out-Null

# Translate for each target language
foreach ($lang in $TargetLanguages) {
    Write-Host "Translating to $lang..."
    
    $translatedFlat = @{}
    $batch = [System.Collections.Generic.List[string]]::new()
    $batchKeys = [System.Collections.Generic.List[string]]::new()
    $allKeys = @($FlatStrings.Keys)
    
    for ($idx = 0; $idx -lt $allKeys.Count; $idx++) {
        $key = $allKeys[$idx]
        $batch.Add($FlatStrings[$key])
        $batchKeys.Add($key)
        
        $isLast = $idx -eq ($allKeys.Count - 1)
        if ($batch.Count -eq 50 -or $isLast) {
            $bodyJson = @{
                text = [string[]]$batch.ToArray()
                target_lang = $lang.ToUpper()
                source_lang = "EN"
                context = $Context
            } | ConvertTo-Json -Depth 5 -Compress
            $content = [System.Net.Http.StringContent]::new($bodyJson, [System.Text.Encoding]::UTF8, "application/json")
            
            try {
                $httpResponse = $HttpClient.PostAsync($DeepLApiUrl, $content).Result
                $responseText = $httpResponse.Content.ReadAsStringAsync().Result
                if (-not $httpResponse.IsSuccessStatusCode) {
                    Write-Error "Translation API error for ${lang}: HTTP $([int]$httpResponse.StatusCode) - $responseText"
                }
                $response = $responseText | ConvertFrom-Json
                
                $translations = $response.translations
                for ($i = 0; $i -lt $batchKeys.Count; $i++) {
                    $translatedFlat[$batchKeys[$i]] = $translations[$i].text
                }
                
                Write-Host "  Translated batch of $($batch.Count) strings."
            }
            catch {
                Write-Error "Translation API error for ${lang}: $_"
            }
            
            $batch = [System.Collections.Generic.List[string]]::new()
            $batchKeys = [System.Collections.Generic.List[string]]::new()
        }
    }

    if ($TermOverrides.ContainsKey($lang)) {
        foreach ($overrideKey in $TermOverrides[$lang].Keys) {
            if ($translatedFlat.ContainsKey($overrideKey)) {
                $translatedFlat[$overrideKey] = $TermOverrides[$lang][$overrideKey]
            }
        }
    }
    
    # Unflatten and write
    $unflattened = Unflatten-Json $translatedFlat
    $outputFile = Join-Path $StringsDir "Strings.$lang.json"
    
    $jsonOut = $unflattened | ConvertTo-Json -Depth 10
    [System.IO.File]::WriteAllText($outputFile, $jsonOut, $Utf8NoBom)
    Write-Host "  Wrote $outputFile"
}

$HttpClient.Dispose()

# Update hash file
[System.IO.File]::WriteAllText($HashFile, $CurrentSignatureHash, $Utf8NoBom)
Write-Host "Updated hash file. Translation complete!"
