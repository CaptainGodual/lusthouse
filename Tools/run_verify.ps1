# Builds and runs Tools/VerifyRunner.cs outside Unity.
#
# Unity holds the project lock while the editor is open, so this drives Roslyn directly
# against the assemblies check_compile.ps1 already produced (LustHouse.Scripts /
# LustHouse.Editor), then executes the result under Unity's bundled mono.
#
# Compiles against Mono's own 4.7.2 reference assemblies rather than Unity's
# netstandard facades: an exe linked against the facade shims loads but dies with an
# access violation on the NetCore runtime. Mono's BCL is what these libraries were
# actually built to sit on.
param([switch]$NoRun)

$ErrorActionPreference = "Stop"

$proj = "C:\users\hammy\documents\projects\lusthouse"
$out  = Join-Path $env:TEMP "opencode\compile"
$editor = "C:\Program Files\Unity\Hub\Editor\6000.0.64f1\Editor\Data"
$dotnet = "$editor\NetCoreRuntime\dotnet.exe"
$csc    = "$editor\DotNetSdkRoslyn\csc.dll"
$monoExe = "$editor\MonoBleedingEdge\bin\mono.exe"
$monoLib  = "$editor\MonoBleedingEdge\lib\mono\4.7.2-api"

$core = @(
    "$monoLib\mscorlib.dll"
    "$monoLib\System.dll"
    "$monoLib\System.Core.dll"
    "$monoLib\System.Xml.dll"
    "$monoLib\System.Runtime.Serialization.dll"
    "$monoLib\System.Numerics.dll"
    # LustHouse.Scripts was compiled against netstandard 2.1, so its type
    # references resolve through a netstandard facade. Use Mono's *implementation*
    # facade, not Unity's ref-only assembly: a reference assembly cannot be loaded
    # at run time and the exe dies with an access violation.
    "$monoLib\Facades\netstandard.dll"
    "$proj\Library\PackageCache\com.unity.nuget.newtonsoft-json@74deb55db2a0\Runtime\Newtonsoft.Json.dll"
)

foreach ($r in $core) {
    if (-not (Test-Path $r)) { Write-Output "  !! missing reference: $r"; exit 1 }
}
foreach ($a in @("LustHouse.Scripts.dll", "LustHouse.Editor.dll")) {
    if (-not (Test-Path (Join-Path $out $a))) {
        Write-Output "  !! $a not built yet -- run Tools\check_compile.ps1 first"
        exit 1
    }
}

$refs = $core + @("$out\LustHouse.Scripts.dll", "$out\LustHouse.Editor.dll")
$exe = Join-Path $out "VerifyRunner.exe"

$lines = @("-target:exe", "-out:`"$exe`"", "-langversion:9.0", "-nologo", "-nostdlib+",
           "-nowarn:0169,0414,0649,0067,1701,1702")
$lines += $refs | ForEach-Object { "-r:`"$_`"" }
$lines += "`"$proj\Tools\VerifyRunner.cs`""

$rsp = Join-Path $out "verify.rsp"
Set-Content -Path $rsp -Value $lines -Encoding UTF8

& $dotnet $csc "@$rsp"
if ($LASTEXITCODE -ne 0) { Write-Output "COMPILE FAILED"; exit 1 }

if ($NoRun) { exit 0 }

& $monoExe $exe
exit $LASTEXITCODE
