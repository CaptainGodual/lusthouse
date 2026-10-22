# Tools

Headless verification that works while the Unity editor has the project locked.

## `check_compile.ps1`

Compiles `LustHouse.Scripts`, `LustHouse.Editor`, and `LustHouse.Tests.Editor` with
Roslyn, referencing the assemblies Unity already built in `Library/ScriptAssemblies`.

Assembly references are **read from the `.asmdef` files**, not hardcoded. An earlier
version of this script kept a flat reference list, which silently masked two missing
asmdef references that only Unity reported. It now fails if an asmdef names an assembly
it cannot resolve, and prefers a freshly-compiled DLL over Unity's stale cached copy of
our own assemblies.

```powershell
.\Tools\check_compile.ps1            # runtime + editor
.\Tools\check_compile.ps1 -WithTests # + test assembly
```

Caveat: `Library/ScriptAssemblies` must be populated, so open the project in the editor
once before this is useful on a fresh clone. Paths are hardcoded to this machine's
editor install — edit `$data` / `$proj` at the top to retarget.

## `VerifyRunner.cs`

Executes the pure-.NET paths (save system, guardrails, seeded RNG) outside Unity so they
can be exercised without the editor. The slot maths and RTP simulator depend on
`ScriptableObject` and need Unity — those are covered by the EditMode suite.

```powershell
# see check_compile.ps1 for how the harness is wired; run the built exe under:
#   "<editor>\Data\MonoBleedingEdge\bin\mono.exe" VerifyRunner.exe
```

Exit code is non-zero if any check fails.

## `run_verify.ps1`

Builds `VerifyRunner.cs` and executes it under Unity's bundled mono.

An exe linked against Unity's *reference-only* netstandard facades loads but dies with
an access violation (0xC0000005) the moment it runs — a ref assembly has no IL to
execute. This script therefore compiles against Mono's own 4.7.2-api BCL plus Mono's
implementation `Facades\netstandard.dll`, which is what `LustHouse.Scripts` (a
netstandard 2.1 assembly) actually resolves its core types through at run time.

It links against the `LustHouse.Scripts.dll` / `LustHouse.Editor.dll` that
`check_compile.ps1` already produced, so run that first.

```powershell
.\Tools\check_compile.ps1 -WithTests   # produces the assemblies this links against
.\Tools\run_verify.ps1
```

Exit code is non-zero if any check fails. Mono prints a
"runtime and class libraries are out of sync" notice referencing
`UnityEngine.CoreModule.dll` *after* the result line; that is a known Unity-mono
artefact from loading engine assemblies outside the editor, not a check failure.