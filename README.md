# Lust House Casino — Unity (URP 2D)

Social casino for Windows / macOS / Linux / Android / Web.
Play-money only. 18+ age-gated. No real-money betting, no cash-out.

Engine: Unity 6000.0.64f1 (LTS) + URP 2D.
Plan: `C:\Users\Hammy\.opencode\plan\lusthouse-casino.md` — see **§0** for repo state.

---

## First run in the editor

The project ships with **no scenes, no render pipeline asset, and no content assets** —
those need the editor to generate. Run this once:

> **LustHouse → Setup → Run all setup**

Or from a terminal (editor must be closed):

```
Unity.exe -batchmode -quit -projectPath . -executeMethod LustHouse.EditorTools.ProjectSetup.RunAll
```

Individual steps are available under `LustHouse → Setup → 1..6` if you want to re-run one.

> **Scenes are only created, never overwritten.** If you re-run setup on a project whose
> scenes already exist, it warns and leaves them alone. `BootSequence` self-heals a missing
> `BootPresenter` at runtime, so a stale scene still boots — but run
> **LustHouse → Setup → Repair boot scene** to persist the fix in the scene file and clear
> the warning.

> **If the UI renders but nothing responds to clicks, run
> **LustHouse → Setup → Repair input blocking**.** A full-screen `Graphic` with
> `raycastTarget` on is returned as the top hit for every pixel, so it swallows every click in
> the game while looking completely normal. The generated scenes shipped with a leftover
> `SceneLabel` that did exactly this.

What it creates:

| Step | Result |
|---|---|
| 1. Render pipeline | `Assets/Settings/LustHouseURP.asset` + 2D renderer, assigned to GraphicsSettings **and every quality level** |
| 2. Project settings | Linear colour space, company/product name, bundle ids, orientation |
| 3. Scenes | `BootLoader`, `Lobby`, `Slot` |
| 4. Build settings | Scene order `BootLoader → Lobby → Slot` |
| 5. Addressables | Groups `Boot`, `Slots`, `Deferred` |
| 6. Starter content | `Slot_VelvetRoom.asset`, four placeholder slot definitions + `GameCatalog.asset` |

> **Existing slot assets are migrated, never deleted.** Run
> **LustHouse → Setup → Migrate slot content** after pulling content changes (Run all setup
> also does this). It fills in missing reel strips and feature symbols and wires the
> shared art, while leaving existing sprites, strips and paytables alone.
> The art lives in `Assets/Art/Slots` and is **shared by every game**: the five card
> ranks are cut as gold-framed glyphs (`ace.png`, `king.png`, `queen.png`, `j.png`,
> `10.png`) and the four character illustrations fill the Wild (blueberry lingerie),
> the Scatter (blueberry dress) and the two premium fruit symbols (grape, kiwi).
> The ids are the same in every game, so one art table in `ContentBootstrap` serves the
> whole catalogue.

Then open `Assets/Scenes/BootLoader.unity` and press Play.

---

## Architecture rules that must not be broken

These are enforced by tests, not convention (`Assets/Tests/Editor/GuardrailTests.cs`):

1. **Coins are never purchasable.** `CosmeticDefinition` has no coin price field — the
   absence of the field is the enforcement.
2. **Lust Points are structurally non-spendable.** `CurrencyAccount.spendable` is false,
   so the account itself rejects a debit; no caller can bypass it.
3. **Conversion is one-directional** (Gems → Coins, capped 3/day). There is no
   Coins → Gems method anywhere.
4. **Every balance change writes a ledger entry**, and the balance equals the sum of the
   ledger deltas (`CurrencyAccount.ReplayBalance`).
5. **RNG is seeded and reproducible** (`SeededRng`, xorshift128+). Never
   `UnityEngine.Random` — it is shared global state and cannot be replayed in QA.
6. **One slot scene.** Games are data (`SlotMachineDefinition`), not scenes or code.
7. **One avatar render path.** `AvatarComposer` is used by the PlayerChip, Club, and slots.
8. **No full-screen graphic may intercept the pointer.** A `Graphic` covering the canvas with
   `raycastTarget` on takes every click in the game. `LobbyTapTests` asserts this across all
   three scenes.
9. **One symbol set across the catalogue.** Every game uses the same symbol id for the same
   symbol, no two symbols in a game share an id or an artwork, and no paytable symbol is
   unreachable from the reels. `SlotContentTests` asserts all three.
10. **One bet ladder, and the level raises the ceiling.** Every game offers the same
   denominations — 1,000 at the floor, 2,400,000 at the house maximum — and a player's
   level decides how far up the ladder they can go (level 1 tops out at 75,000). The
   floor never moves, so a bet always means the same risk. `BetLimitsTests` asserts it.

---

## Testing

```
LustHouse → Math Simulator        # RTP gate for a single definition
Window ▸ General ▸ Test Runner    # EditMode suite
```

Or headless:

```
Unity.exe -batchmode -runTests -testPlatform EditMode -projectPath .
```

Current suites: save round-trip / corruption / migration, guardrails, RNG determinism and
distribution, reel engine and payline evaluation, the RTP simulator, feature rounds, and the
reel view.

**EditMode** covers components in isolation. **PlayMode** (`Assets/Tests/PlayMode`) covers the
seams between scenes — currently the lobby → slot launch, the age gate, and the music bed,
none of which no EditMode test can reach: every component can pass while the player cannot
tap anything, because a tap is a raycast through a canvas, a listener firing, and a static
surviving a scene load, and each link breaks independently.

**As of 2026-10-21: EditMode 184/184 and PlayMode 24/24 passing**, run headless with
`Unity.exe -batchmode -runTests -testPlatform EditMode -projectPath .` (the editor must
not have the project open — Unity holds the lock).

```
Unity.exe -batchmode -runTests -testPlatform PlayMode -projectPath .
```

> **Ship gate (plan §3.6):** never ship a paytable change without a passing 100k-spin
> simulation within ±0.5% of target RTP.

> **Placeholder math is deferred.** The four placeholder definitions validate and are
> playable, but their RTP is not tuned, so `RtpGateTests` lists them explicitly as deferred.
> Adding a new definition without either gating or deferring it fails the suite.

### Looking at the UI

`LobbyScreenshotTests` and `SlotScreenshotTests` (PlayMode) render the lobby and a
winning reel to `Logs/lobby.png` and `Logs/slot-win.png`. An overlay canvas is not drawn
by any camera, so the capture switches the canvas to `ScreenSpaceCamera`, renders that
camera into a RenderTexture and reads the pixels back. That works in batch mode, where no
window is ever opened — which is the only way to *see* a UI change without a human in
front of the screen. They assert nothing and always pass; they are development tools,
not tests of behaviour.

---

## Android device build

The editor needs **Android Build Support** with its SDK, NDK and OpenJDK (Unity Hub →
Installs → 6000.0.64f1 → Add modules). The bundled SDK (API 36), build-tools 36 and
OpenJDK are what the project builds against.

The UI uses `StandaloneInputModule`, so set **Edit → Project Settings → Player → Other
Settings → Active Input Handling** to **Input Manager (Old)** and restart Unity once.
Android rejects the "Both" setting, and new-only input would leave the UI untappable;
the build menu refuses to run unless the legacy setting is active.

Build from the menu:

- **LustHouse → Build → Android APK** — release APK at `Builds/Android/LustHouse.apk`
- **LustHouse → Build → Android APK + Run** — development APK, installed and launched on the connected device
- **LustHouse → Build → Validate Android setup** — checks build support, the JNI module and input handling
- **LustHouse → Build → Apply Android Player settings** — re-applies IL2CPP, ARM64 and min API 24

Or headless:

```
Unity.exe -batchmode -quit -projectPath . ^
  -executeMethod LustHouse.EditorTools.BuildAndroid.BuildApk ^
  -logFile Logs/android-build.log
```

Install and inspect on the device:

```
adb install -r Builds/Android/LustHouse.apk
adb shell monkey -p com.lusthouse.casino -c android.intent.category.LAUNCHER 1
adb logcat -s Unity
adb shell getprop ro.product.cpu.abi
adb shell getprop ro.build.version.sdk
```

If `adb` is not on your `PATH`, the editor bundles it under
`<UnityEditor>\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe`.

| Symptom | Fix |
|---|---|
| `CS0246: AndroidJavaClass could not be found` | `com.unity.modules.androidjni` is missing from `Packages/manifest.json` (or not resolved yet — restart Unity). |
| `Active Input Handling is set to Both ... Cancelling` | Set it to **Input Manager (Old)** and restart the editor. |
| No device in Unity / `adb devices` | Enable USB debugging, use a data cable, accept the RSA prompt, or use Android 11+ wireless debugging. |
| `INSTALL_FAILED_NO_MATCHING_ABIS` | The device is 32-bit only. Add `AndroidArchitecture.ARMv7` in `BuildAndroid.cs` (and `ProjectSetup.cs`) and rebuild. |
| Build succeeds but nothing taps | Run **LustHouse → Setup → Repair input blocking** and check `adb logcat -s Unity` for input exceptions. |

`AndroidBuildGuardTests` keeps `androidjni`, IL2CPP/ARM64, min API 24 and the input
setting from drifting.

---

## Layout

```
Assets/Scripts/
  Core/        wallet, ledger, inventory, save + migrations, event bus, logging
  Slots/       seeded RNG, reel engine, payline evaluator, game runner
  Audio/       the sound system: cue vocabulary, voice pool, synthesised sounds
  Avatar/      composer + loadout
  Economy/     cosmetic definitions, level + 7-tier VIP progression
  Catalog/     GameCatalog (the lobby grid's data source)
  Lobby/       tile-tap dispatcher
  Boot/        four-beat loading sequence with true weighted progress
  Editor/      ProjectSetup, ContentBootstrap, MathSimulator   [editor only]
Assets/Tests/Editor/   EditMode suite
```

## Sound

Most sound is **synthesised at runtime** from maths — the same way the UI builds its
own sprites — so the project ships with a voice without shipping a `.wav` for every
cue. Music is the exception: a bed is a real track when one is on the bank, and the
synthesised loop when one is not.

| Cue | When |
|---|---|
| `UiClick` | any button press |
| `SpinPress` | the spin button (heavier than a click: it spends the player's money) |
| `GameWelcome` | a game opens and its content is valid |
| `ReelSpinLoop` | loops while the reels turn, and fades out when they stop |
| `ReelStop` | one reel lands, a little lower in pitch each time |
| `WinSmall` / `WinMedium` / `WinBig` | a payout, tiered off the game's own celebration thresholds |
| `BonusTrigger` | a free-spins, hold & spin or bonus round starts |
| `LobbyMusic` | the lobby's background bed; fades out as the player leaves for a game |
| `SlotMusic` | the game screen's own bed; it comes up with the reels and fades away a few seconds after they stop |

- **`Assets/Scripts/Audio/`** holds the system. `AudioService` is the live output
  (a pool of eight voices, a loop channel, a music channel, master/music/sfx buses);
  `SlotAudio` maps game events to cues; `ProceduralAudio` makes the sounds.
- **Music has its own channel**, with its own volume (saved with the player's
  settings) and its own fade — a bed fades in on arrival and out on departure,
  because music that cuts is worse than no music.
- **Recorded audio replaces any of it without touching code.** Put a clip on an
  `AudioBank` entry (`Assets/Audio/Banks/`) and the game plays that instead. Two
  banks: the **house bank** (`Assets/Resources/Audio/AudioBank_House.asset`, always
  loaded — the lobby's bed lives here, authored by *LustHouse ▸ Audio ▸ Author house
  music bank*) and a **game's own bank** for per-title overrides. Anything neither
  bank mentions falls back to the synthesised sound, so a half-filled bank is a good
  bank.
- The sounds are **deterministic** — a fixed-seed generator, never
  `UnityEngine.Random` — so a build always sounds the same and a QA report about a
  sound is a report about *that* sound.
- The reel loop and the synthesised music beds are **periodic by construction**, so
  they join up with themselves and do not click once a loop.
- **Every scene gets an audio listener.** The cameras here are built in code, and a
  camera built in code is not given one; a scene without a listener is completely
  silent while looking perfectly normal.
- **The music channel is cue-aware.** The lobby's bed and the game's bed share one
  channel, so a room that unloads late cannot take the next room's bed down with it,
  and a bed that is fading out comes back if it is asked for again.

## Age gate

The store requirement that this game is 18+ (plan §2.3a).

- **Nothing in the game is reachable until the player has confirmed they are 18.**
  The gate comes up in the boot, between `TAP TO ENTER` and the lobby, and the lobby
  is not entered until the answer has been written to the save.
- The answer is **kept**: `PlayerState.ageConfirmed` was already in the save shape,
  so this needed no schema bump, and the confirmation is saved immediately rather
  than left to the autosave.
- The `18+` chip in the lobby's top tier opens the same gate again, for a player who
  wants to re-check it.
- Saying you are under 18 shows a refusal screen and closes the game. **On Web the
  refusal screen is shown but the game does not close** — that is a known gap.

## Known gaps

Tracked in plan §0.2 and §6. Not yet built: IAP/store integration, the tournament
simulator, missions, the Club screen, localisation, a music bed per title (the game
screen has one shared bed; the mechanism for a title to bring its own is in place),
the Web refusal screen for the age gate, and a settings screen for the volumes (the
volumes themselves apply and persist).

> The **daily gift is built** (`DailyBonusService`, its lobby chip and sheet) — the older
> list of gaps predated it.
>
> The **age gate is built** (`AgeGate`, `AgeGateOverlay`, the boot flow and the lobby's
> `18+` chip) — the older list of gaps predated it too.

Plans:

| Plan | What it covers |
|---|---|
| `lusthouse-casino.md` | The design bible — loading screen, lobby, slots, economy, milestones. **§0** is the repo state; **§4.4** is the derived earn/sink table. |
| `lusthouse-economy.md` | The economy model: per-level earn/sink, stake-independent progression, the measurement tooling and the tests that keep it true. |
| `lusthouse-jackpots-responsive.md` | The Mini/Minor/Major progressive on **Velvet Room**, and the device-responsive layout core. |
| `lusthouse-audio.md` | The sound system, as built. |