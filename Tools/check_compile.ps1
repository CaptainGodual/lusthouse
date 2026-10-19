# Compile-check helper. Unity holds the project lock while the editor is open, so this
# drives Roslyn directly against the assemblies Unity already built in Library/ScriptAssemblies.
#   .\check_compile.ps1
param([switch]$WithTests)

$ErrorActionPreference = "Continue"
$proj = "C:\users\hammy\documents\projects\lusthouse"
$out  = Join-Path $env:TEMP "opencode\compile"
New-Item -ItemType Directory -Force -Path $out | Out-Null

$dotnet = "C:\Program Files\Unity\Hub\Editor\6000.0.64f1\Editor\Data\NetCoreRuntime\dotnet.exe"
$csc    = "C:\Program Files\Unity\Hub\Editor\6000.0.64f1\Editor\Data\DotNetSdkRoslyn\csc.dll"
$data   = "C:\Program Files\Unity\Hub\Editor\6000.0.64f1\Editor\Data"
$managed = "$data\Managed"
$engine  = "$managed\UnityEngine"
$sa      = "$proj\Library\ScriptAssemblies"

function Get-BaseRefs {
    $refs = @("$data\NetStandard\ref\2.1.0\netstandard.dll")
    $refs += (Get-ChildItem "$data\NetStandard\compat\2.1.0\shims\netstandard" -Filter "*.dll" | ForEach-Object { $_.FullName })
    # net40-era assemblies (Unity.Addressables.Editor, nunit) need the netfx mscorlib facade.
    $refs += (Get-ChildItem "$data\NetStandard\compat\2.1.0\shims\netfx" -Filter "*.dll" | ForEach-Object { $_.FullName })
    $refs += (Get-ChildItem $engine -Filter "UnityEngine*.dll" | ForEach-Object { $_.FullName })
    $refs += "$engine\UnityEngine.dll"
    $refs += "$managed\UnityEditor.dll"
    $refs += "$sa\UnityEngine.UI.dll"
    $refs += "$sa\Unity.RenderPipelines.Universal.Runtime.dll"
    $refs += "$sa\Unity.RenderPipelines.Universal.2D.Runtime.dll"
    $refs += "$sa\Unity.RenderPipelines.Core.Runtime.dll"
    $refs += "$sa\Unity.Addressables.dll"
    $refs += "$sa\Unity.Addressables.Editor.dll"
    $refs += "$proj\Library\PackageCache\com.unity.nuget.newtonsoft-json@74deb55db2a0\Runtime\Newtonsoft.Json.dll"
    return $refs
}

function Invoke-Csc([string]$rspName, [string]$target, [string[]]$extraRefs, [string[]]$sources, [string]$outDll) {
    # Unity always defines UNITY_EDITOR for these assemblies; without it any
    # #if UNITY_EDITOR block compiles to nothing and the check is silently vacuous.
    $defines = "UNITY_EDITOR;UNITY_EDITOR_WIN;UNITY_EDITOR_LINUX;UNITY_STANDALONE_WIN;UNITY_STANDALONE;" +
               "UNITY_2019_1_OR_NEWER;UNITY_2020_1_OR_NEWER;UNITY_2021_1_OR_NEWER;UNITY_2022_1_OR_NEWER;UNITY_2023_1_OR_NEWER;UNITY_6000_0_OR_NEWER"
    $lines = @("-target:$target", "-out:`"$outDll`"", "-langversion:9.0", "-nologo", "-nostdlib+",
               "-define:$defines",
               "-nowarn:0169,0414,0649,0067,1701,1702")
    $lines += $extraRefs | ForEach-Object { "-r:`"$_`"" }
    $lines += $sources | ForEach-Object { "`"$_`"" }
    $rsp = Join-Path $out $rspName
    Set-Content -Path $rsp -Value $lines -Encoding UTF8
    $compilerOutput = & $dotnet $csc "@$rsp" 2>&1
    $script:LastCode = $LASTEXITCODE
    if ($compilerOutput) { $compilerOutput | ForEach-Object { Write-Output $_ } }
}

$failed = $false
$script:LastCode = 0

# ---- 1. LustHouse.Scripts -------------------------------------------------
$scriptsDll = Join-Path $out "LustHouse.Scripts.dll"
$srcs = Get-ChildItem "$proj\Assets\Scripts" -Recurse -Filter "*.cs" |
        Where-Object { $_.FullName -notmatch "\\Editor\\" } | ForEach-Object { $_.FullName }
Write-Output "== LustHouse.Scripts ($($srcs.Count) files) =="
Invoke-Csc "scripts.rsp" "library" (Get-BaseRefs) $srcs $scriptsDll
if ($script:LastCode -ne 0) { $failed = $true }

# ---- 2. LustHouse.Editor --------------------------------------------------
$editorDll = Join-Path $out "LustHouse.Editor.dll"
$esrcs = Get-ChildItem "$proj\Assets\Scripts\Editor" -Recurse -Filter "*.cs" | ForEach-Object { $_.FullName }
Write-Output "== LustHouse.Editor ($($esrcs.Count) files) =="
Invoke-Csc "editor.rsp" "library" ((Get-BaseRefs) + @($scriptsDll)) $esrcs $editorDll
if ($script:LastCode -ne 0) { $failed = $true }

# ---- 3. LustHouse.Tests.Editor -------------------------------------------
if ($WithTests) {
    $testsDll = Join-Path $out "LustHouse.Tests.Editor.dll"
    $nunit = "$proj\Library\PackageCache\com.unity.ext.nunit@031a54704bff\net40\unity-custom\nunit.framework.dll"
    # nunit.framework.dll targets net40, so the netfx mscorlib facade is needed alongside
    # the netstandard surface — this mirrors how Unity itself compiles the test assembly.
    $netfxShims = (Get-ChildItem "$data\NetStandard\compat\2.1.0\shims\netfx" -Filter "*.dll" | ForEach-Object { $_.FullName })
    $trefs = (Get-BaseRefs) + $netfxShims + @($scriptsDll, $editorDll, $nunit, "$sa\UnityEngine.TestRunner.dll", "$sa\UnityEditor.TestRunner.dll")
    $tsrcs = Get-ChildItem "$proj\Assets\Tests" -Recurse -Filter "*.cs" | ForEach-Object { $_.FullName }
    Write-Output "== LustHouse.Tests.Editor ($($tsrcs.Count) files) =="
    Invoke-Csc "tests.rsp" "library" $trefs $tsrcs $testsDll
    if ($script:LastCode -ne 0) { $failed = $true }
}

if ($failed) { Write-Output "`nRESULT: COMPILE ERRORS" ; exit 1 }
Write-Output "`nRESULT: all assemblies compiled clean"
exit 0