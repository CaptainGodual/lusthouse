# Tools

Headless verification that works while the Unity editor has the project locked.

## `check_compile.ps1`

Compiles `LustHouse.Scripts`, `LustHouse.Editor`, and `LustHouse.Tests.Editor` with
Roslyn, referencing the assemblies Unity already built in `Library/ScriptAssemblies`.

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