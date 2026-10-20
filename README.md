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
seams between scenes — currently the lobby → slot launch, which no EditMode test can reach:
every component can pass while the player cannot tap anything, because a tap is a raycast
through a canvas, a listener firing, and a static surviving a scene load, and each link breaks
independently.

```
Unity.exe -batchmode -runTests -testPlatform PlayMode -projectPath .
```

> **Ship gate (plan §3.6):** never ship a paytable change without a passing 100k-spin
> simulation within ±0.5% of target RTP.

> **Placeholder math is deferred.** The four placeholder definitions validate and are
> playable, but their RTP is not tuned, so `RtpGateTests` lists them explicitly as deferred.
> Adding a new definition without either gating or deferring it fails the suite.


### Looking at the lobby

`LobbyScreenshotTests` (PlayMode) renders the lobby to `Logs/lobby.png`. An overlay
canvas is not drawn by any camera, so the test switches the canvas to
`ScreenSpaceCamera`, renders the camera into a RenderTexture and reads the pixels back.
That works in batch mode, where no window is ever opened -- which is the only way to
*see* a UI change without a human in front of the screen. It asserts nothing and always
passes; it is a development tool, not a test of behaviour.

---

## Layout

```
Assets/Scripts/
  Core/        wallet, ledger, inventory, save + migrations, event bus, logging
  Slots/       seeded RNG, reel engine, payline evaluator, game runner
  Avatar/      composer + loadout
  Economy/     cosmetic definitions, level + 7-tier VIP progression
  Catalog/     GameCatalog (the lobby grid's data source)
  Lobby/       tile-tap dispatcher
  Boot/        four-beat loading sequence with true weighted progress
  Editor/      ProjectSetup, ContentBootstrap, MathSimulator   [editor only]
Assets/Tests/Editor/   EditMode suite
```

## Known gaps

Tracked in plan §0.2 and §6. Not yet built: lobby UI, art, audio, IAP/store integration,
tournaments, missions, daily gift, Club screen, localisation.