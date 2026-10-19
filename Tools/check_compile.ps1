# Compile-check harness.
#
# Unity holds the project lock while the editor is open, so this drives Roslyn directly
# against the assemblies Unity already built in Library/ScriptAssemblies.
#
# Assembly references are read from the .asmdef files rather than hardcoded. That is the
# whole point: a hardcoded global reference list will happily paper over a missing
# asmdef reference, which is exactly the class of bug Unity reports and this script misses.
#
#   .\Tools\check_compile.ps1
#   .\Tools\check_compile.ps1 -WithTests
param([switch]$WithTests)

$proj = "C:\users\hammy\documents\projects\lusthouse"
$out  = Join-Path $env:TEMP "opencode\compile"
New-Item -ItemType Directory -Force -Path $out | Out-Null

$dotnet = "C:\Program Files\Unity\Hub\Editor\6000.0.64f1\Editor\Data\NetCoreRuntime\dotnet.exe"
$csc    = "C:\Program Files\Unity\Hub\Editor\6000.0.64f1\Editor\Data\DotNetSdkRoslyn\csc.dll"
$data   = "C:\Program Files\Unity\Hub\Editor\6000.0.64f1\Editor\Data"
$managed = "$data\Managed"
$engine  = "$managed\UnityEngine"
$sa      = "$proj\Library\ScriptAssemblies"
$failed  = $false
$script:LastCode = 0

# Unity always defines UNITY_EDITOR for these assemblies. Without it any
# #if UNITY_EDITOR block compiles to nothing and the check is silently vacuous.
$defines = "UNITY_EDITOR;UNITY_EDITOR_WIN;UNITY_EDITOR_LINUX;UNITY_STANDALONE_WIN;UNITY_STANDALONE;" +
           "UNITY_2019_1_OR_NEWER;UNITY_2020_1_OR_NEWER;UNITY_2021_1_OR_NEWER;UNITY_2022_1_OR_NEWER;UNITY_2023_1_OR_NEWER;UNITY_6000_0_OR_NEWER"

function Get-CoreRefs {
    @(
        "$data\NetStandard\ref\2.1.0\netstandard.dll"
        (Get-ChildItem "$data\NetStandard\compat\2.1.0\shims\netstandard" -Filter "*.dll" | ForEach-Object { $_.FullName })
        # net40-era assemblies (Unity.Addressables.Editor, nunit) need the netfx facade.
        (Get-ChildItem "$data\NetStandard\compat\2.1.0\shims\netfx" -Filter "*.dll" | ForEach-Object { $_.FullName })
        (Get-ChildItem $engine -Filter "UnityEngine*.dll" | ForEach-Object { $_.FullName })
        "$engine\UnityEngine.dll"
        "$managed\UnityEditor.dll"
        "$proj\Library\PackageCache\com.unity.nuget.newtonsoft-json@74deb55db2a0\Runtime\Newtonsoft.Json.dll"
    )
}

function Resolve-AssemblyRef([string]$name) {
    # Freshly-compiled output wins over Library/ScriptAssemblies. Unity's cached copy of
    # our own assemblies is stale between editor recompiles and would silently shadow the
    # DLL this script just built.
    $candidates = @(
        (Join-Path $out "$name.dll")
        (Join-Path $sa "$name.dll")
        (Join-Path $managed "$name.dll")
    )
    foreach ($c in $candidates) { if (Test-Path $c) { return $c } }
    return $null
}

function Get-AsmdefRefs([string]$asmdefPath) {
    $json = Get-Content $asmdefPath -Raw | ConvertFrom-Json
    $refs = @()
    foreach ($r in $json.references) {
        $path = Resolve-AssemblyRef $r
        if (-not $path) {
            Write-Output "  !! asmdef '$($json.name)' references '$r' which was NOT FOUND"
            $script:failed = $true
            continue
        }
        $refs += $path
    }
    return $refs
}

function Invoke-Csc([string]$rspName, [string]$target, [string[]]$refs, [string[]]$sources, [string]$outDll) {
    $lines = @("-target:$target", "-out:`"$outDll`"", "-langversion:9.0", "-nologo", "-nostdlib+",
               "-define:$defines", "-nowarn:0169,0414,0649,0067,1701,1702")
    $lines += $refs | ForEach-Object { "-r:`"$_`"" }
    $lines += $sources | ForEach-Object { "`"$_`"" }
    $rsp = Join-Path $out $rspName
    Set-Content -Path $rsp -Value $lines -Encoding UTF8
    $compilerOutput = & $dotnet $csc "@$rsp" 2>&1
    $script:LastCode = $LASTEXITCODE
    if ($compilerOutput) { $compilerOutput | ForEach-Object { Write-Output $_ } }
}

$core = Get-CoreRefs

# ---- 1. LustHouse.Scripts -------------------------------------------------
$scriptsDll = Join-Path $out "LustHouse.Scripts.dll"
$srcs = Get-ChildItem "$proj\Assets\Scripts" -Recurse -Filter "*.cs" |
        Where-Object { $_.FullName -notmatch "\\Editor\\" } | ForEach-Object { $_.FullName }
Write-Output "== LustHouse.Scripts ($($srcs.Count) files) =="
Invoke-Csc "scripts.rsp" "library" ($core + (Get-AsmdefRefs "$proj\Assets\Scripts\LustHouse.Scripts.asmdef")) $srcs $scriptsDll
if ($script:LastCode -ne 0) { $failed = $true }

# ---- 2. LustHouse.Editor --------------------------------------------------
$editorDll = Join-Path $out "LustHouse.Editor.dll"
$esrcs = Get-ChildItem "$proj\Assets\Scripts\Editor" -Recurse -Filter "*.cs" | ForEach-Object { $_.FullName }
Write-Output "== LustHouse.Editor ($($esrcs.Count) files) =="
Invoke-Csc "editor.rsp" "library" ($core + $scriptsDll + (Get-AsmdefRefs "$proj\Assets\Scripts\Editor\LustHouse.Editor.asmdef")) $esrcs $editorDll
if ($script:LastCode -ne 0) { $failed = $true }

# ---- 3. LustHouse.Tests.Editor -------------------------------------------
if ($WithTests) {
    $testsDll = Join-Path $out "LustHouse.Tests.Editor.dll"
    $nunit = "$proj\Library\PackageCache\com.unity.ext.nunit@031a54704bff\net40\unity-custom\nunit.framework.dll"
    $tsrcs = Get-ChildItem "$proj\Assets\Tests" -Recurse -Filter "*.cs" | ForEach-Object { $_.FullName }
    Write-Output "== LustHouse.Tests.Editor ($($tsrcs.Count) files) =="
    $trefs = $core + $scriptsDll + $editorDll + $nunit + (Get-AsmdefRefs "$proj\Assets\Tests\Editor\LustHouse.Tests.Editor.asmdef")
    Invoke-Csc "tests.rsp" "library" $trefs $tsrcs $testsDll
    if ($script:LastCode -ne 0) { $failed = $true }
}

if ($failed) { Write-Output "`nRESULT: COMPILE ERRORS"; exit 1 }
Write-Output "`nRESULT: all assemblies compiled clean"
exit 0