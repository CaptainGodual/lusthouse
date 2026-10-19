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

> If your scenes were generated **before** the boot UI existed, run
> **LustHouse → Setup → Repair boot scene** once. It adds `BootPresenter` to
> `BootLoader.unity` and removes the old null-font label. Without it the boot screen
> finishes loading and then sits there forever — there is no auto-advance by design.

What it creates:

| Step | Result |
|---|---|
| 1. Render pipeline | `Assets/Settings/LustHouseURP.asset` + 2D renderer, assigned to GraphicsSettings **and every quality level** |
| 2. Project settings | Linear colour space, company/product name, bundle ids, orientation |
| 3. Scenes | `BootLoader`, `Lobby`, `Slot` |
| 4. Build settings | Scene order `BootLoader → Lobby → Slot` |
| 5. Addressables | Groups `Boot`, `Slots`, `Deferred` |
| 6. Starter content | `Slot_VelvetRoom.asset` + `GameCatalog.asset` |

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
distribution, reel engine and payline evaluation, and the RTP simulator.

> **Ship gate (plan §3.6):** never ship a paytable change without a passing 100k-spin
> simulation within ±0.5% of target RTP.

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