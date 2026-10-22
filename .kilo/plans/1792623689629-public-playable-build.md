# Lust House — the public playable build

Plan date: 2026-10-22. Branch: `main`. Base commit: `da28152`, plus the uncommitted Velvet Room music work.
Companion plans: `lusthouse-casino.md` (the design bible), `lusthouse-jackpots-responsive.md` (the progressive + responsive design, already written and implementation-ready), `lusthouse-economy.md` (the earn/sink model), `lusthouse-audio.md` (sound, as built).

---

## 0. What this chunk is

One vertical slice: a Windows build and an Android APK that a stranger can download, pass the 18+ gate, read the probability disclosure, and play Velvet Room's progressive — with the game telling the truth about its own maths and looking right on a phone.

It is not "add the backlog". The backlog's order was set for a 1.0 with a store, a server-free economy and a catalogue of six games. This build has one game, no store and no server, and the order changes with that. Four things are true today and false by the end of this chunk:

| Today | By the end |
|---|---|
| The age gate says the game publishes its prize tables and its RTP "in its info panel" — and there is no info panel | A real disclosure sheet, reachable from the gate and from the lobby, showing the **measured** maths |
| The lobby shows Velvet Room under a magenta `J A C K P O T S` ribbon, an animated tile border, a `Jackpots` tab and a carousel slide — and there is no pot | A real Mini/Minor/Major pot that pays, with a live meter |
| The phone can rotate to portrait and the layout is landscape-only | Landscape locked; the layout is correct from 4:3 to 21:9, and the reels are placed from the mask geometry instead of by hand |
| Four of the five games are playable but untuned and unarted | They are `COMING SOON` tiles, and the maths that *is* playable is signed off |

**Why this order.** The first two are promises the game already makes and does not keep. The third is the build being unplayable on the device it ships to. The fourth is the marquee. Everything else — missions, tournaments, Club, Boutique, IAP, localisation — waits, and §6 says why.

---

## 1. Where the repo actually is

Facts, each with the file that proves it. Re-verify before starting; a stale line here is a wasted task.

**Working.** Boot → 18+ gate → lobby → slot → spin. Wallet, ledger, progression, daily gift, promo carousel, addressables, an Android build script, procedural audio, per-title audio banks, 185 EditMode + 25 PlayMode tests green. The RTP ship gate and a seeded RNG are real. `SaveSystem` has a schema version, a migration pipeline and an atomic write.

**The four gaps, and where they live.**

1. **No disclosure.** The gate's copy asserts an info panel that does not exist (`AgeGateOverlay.cs:44`). There is no probability table anywhere in `Assets/Scripts`. `SimulationEngine.ExactRtp` / `SimulationEngine.Run` can supply every number it would need.
2. **No progressive.** `Slot_VelvetRoom.asset` has `progressive: 1`, and that flag drives the lobby ribbon (`FeaturedRow.cs:297`), the tile frame (`LobbyGrid.cs:441`), the `Jackpots` category (`LobbyGrid.cs:206`) and the carousel slide (`PromoService.cs:105`). There is no pot, no drip, no handler, no `LedgerReason.Jackpot`, no `SaveDocument.progressive` block. `SlotMachineDefinition.progressiveSeed` (`:207`) is decorative.
3. **The device is not allowed to break the game.** `ProjectSettings.asset` has `defaultScreenOrientation: 4` and `allowedAutorotateToPortrait: 1` while the layout is a 1920×1080 landscape reference. `CanvasScaler.matchWidthOrHeight = 0.5f` is baked in three places (`UiFactory.cs:194`, `ProjectSetup.cs:293`, `Assets/Scenes/Slot.unity`). `ScreenLayout` / `SlotLayout` / `SlotFrameGeometry` do not exist. The reels are placed by hand-tuned constants that miss the mask windows by up to 25px and clip the top and bottom rows by ~15% (`lusthouse-jackpots-responsive.md:71-78`).
4. **Four of five games are unmeasured.** Only `Slot_VelvetRoom.asset` has `rtpStatus: 1`. The other four have no `rtpStatus` line, so they are unmeasured, and `RtpGateTests` only checks the ones that claim `Measured` (`RtpGateTests.cs:92`). They also have **no art** — `ContentBootstrap.ArtForSymbol` only knows Velvet Room's symbols, so they draw as tinted letter tiles (`ReelStripView.cs:499`).

**Two traps that are easy to fall into.**

- **The four placeholders are already a "coming soon" mechanism.** `GameCatalog.Entry.locked` renders a `COMING SOON` badge (`LobbyGrid.cs:461`), refuses the launch (`LobbyGrid.cs:501`) and shows a teaser (`LobbyController.cs:144`). The grid iterates `catalog.games` directly, so setting `locked: 1` on the four entries is a **data change with no code**.
- **Declining the F2P prompt would soft-lock the player.** `ServicesBootstrap.GrantFirstRunPack` gives 50,000 Coins and 25 Gems, and it is the *only* starting balance (`ServicesBootstrap.cs:139-147`). Velvet Room's minimum bet is 10,000, Gems→Coins needs Gems, and the daily gift needs 100 spins. A player who declines and holds 0 Coins can never play. `F2PPromptPending` and `ConsumeF2PPrompt()` exist (`PlayerProgression.cs:62,172`) and **nothing calls them** — the pack is granted silently today.

**Already decided, do not re-litigate:** the progressive is Velvet Room's (casino §0.5); the pot is per-game and house-drip-funded; progressive awards are excluded from the declared RTP; `LedgerReason` is persisted as an int so new reasons are **appended**; `progressive` must mean "has a pot", not "is template E".

---

## 2. Decisions locked

| Question | Answer | Why |
|---|---|---|
| What is the build for? | A public playable build | Decided 2026-10-22 |
| Which platforms? | Windows standalone + Android APK | The desktop path is low-risk; `BuildAndroid.cs` already exists |
| What is in the lobby? | One polished game. The other four become `COMING SOON` tiles | They have no art and no signed-off maths |
| Does the progressive land? | Yes, with the meter | The lobby already advertises it; with one game it is the reason to download |
| IAP / Gems purchase? | **No.** Out of scope this chunk | No store account, cannot be tested, and the plan already scopes it to M6 |
| Responsible play? | Session timer with reality checks + self-exclusion | Guardrail 4, and §5.6 — a public gambling-adjacent build should not ship without it |
| F2P prompt? | Yes, declinable — **but declining still leaves one minimum bet** | Otherwise declining soft-locks the player |
| Save schema | **One bump, 1 → 2**, carrying the disclosure flag, the self-exclusion flag and the progressive block | Two bumps in one chunk is silly, and the plan anticipates this ("if anything else claims schema 2 first…") |

---

## 3. The work, in order

Stages 0–2 can be worked in parallel by different hands; 3.6 needs 2; 4 needs everything.

### Stage 0 — the save schema, 1 → 2

One bump, three new pieces of state, one migration.

- `SaveDocument.CurrentSchemaVersion` 1 → 2. New `SaveDocument.progressive`: `Dictionary<string, long>` keyed `{gameId}:{tierName}`.
- New on `PlayerState`: `disclosureSeen` (bool), `selfExcludedUntilUtc` (string — empty is not excluded; permanent is `"9999-12-31T23:59:59Z"`, one field and no special cases; "irreversible" is enforced by there being no in-game way to clear it and no migration that does).
- `SaveMigration`: v1 → v2 creates an **empty** progressive block and leaves the new player flags at their safe defaults. The pot seeds live in the content, not the save, so a missing key falls back to the tier's `seedValue` — the migration creates nothing but the empty block.
- `SaveSystem.Load`: null-guard the new block the way `player` / `wallet` / `inventory` are guarded.
- The migration must stay idempotent and non-destructive, per the existing contract.

**Proves it:** `ProgressivePersistenceTests` — a v1 save migrates with empty pots and the new flags at their defaults; a corrupt block falls back to the seeds instead of crashing; a save written at schema 2 reloads unchanged. `Tools/VerifyRunner.cs` round-trips `schemaVersion` against `CurrentSchemaVersion` symbolically and must stay green.

### Stage 1 — truth on screen one

**1.1 The probability disclosure.** New `Assets/Scripts/Lobby/ProbabilityDisclosureSheet.cs`.

- Content per shippable game (just Velvet Room): title, **exact** RTP from `SimulationEngine.ExactRtp`, volatility, max-win multiplier, hit rate from a fixed-seed `SimulationEngine.Run`, the bet range, and the full paytable (per symbol, per match count) from the definition.
- The numbers must be **measured, not declared** — read them from the simulator, not from `targetRtp`, so the sheet cannot drift from the maths. A declared target that the sheet prints as fact is the same class of lie as a jackpot ribbon with no pot.
- Shown once after the age gate is confirmed, before the lobby. Permanently reachable from the lobby (a top-bar info entry next to the `18+` chip) and **linked from the age gate** — the gate's copy already claims this panel exists, so either the link lands or the copy changes. Prefer the link.
- The sheet also carries the responsible-play statement the project is required to make: this is a simulation, no real money, no cash-out, no purchase of Coins.

**1.2 Responsible play.** Guardrail 4 and §5.6. New `Assets/Scripts/Core/ResponsiblePlayService.cs` + `Assets/Scripts/Lobby/ResponsiblePlaySheet.cs`.

- A session timer counting real play time, with a reality check at 15 / 30 / 60 minutes: a modal that offers "continue" or "end session". The check must be *shown*; continuing is allowed.
- Self-exclusion: 1 day → permanent, irreversible, and it must **block every entry point** — the lobby's game launch and the tile press. A player who excludes themselves and can still get into a game has no exclusion.
- Reachable from the lobby, permanently.

**1.3 The F2P prompt, and the soft-lock fix.** New prompt after the gate and the disclosure.

- "FREE 50,000 COINS + 25 GEMS", shown once, never re-asked, never a purchase.
- [TAKE IT] grants the existing first-run pack. [NO THANKS] grants a **guaranteed starting stake of one minimum bet (10,000 Coins)** so the player is never at zero, and the prompt is never re-asked.
- Both grants are once-ever and distinguishable in the ledger by note, so `GrantFirstRunPack`'s "derived from the ledger, cannot be re-claimed" property still holds.
- **Re-run the economy report** (`lusthouse-economy.md` §8) afterwards: the first-run grant's value changed, and §4.4's instruction is that if the earn side is cut the gift's share must rise to keep the ratio.

**1.4 The gate's link.** One button on `AgeGateOverlay` that opens the disclosure. If 1.1's "permanently reachable" entry point is in the lobby, the gate can also link to it — but the gate's sentence must be true either way.

**Proves it:** PlayMode — a fresh profile sees gate → disclosure → F2P prompt → lobby, in that order, and only once; declining the prompt still leaves a balance the player can bet; a self-excluded profile cannot launch a game; the disclosure's printed RTP equals `SimulationEngine.ExactRtp` for the shipped definition (an EditMode assertion, so the sheet cannot silently go stale).

### Stage 2 — the device is not allowed to break the game

Follows `lusthouse-jackpots-responsive.md` §4 (Phase 3). This is the stage most likely to overrun, so keep it pure and testable.

**2.1 Lock landscape.** Four places: `ProjectSettings/ProjectSettings.asset` (`defaultScreenOrientation: 2`, `useOSAutorotation: 0`, `allowedAutorotateToPortrait: 0`), `ProjectSetup.cs:212`, `BuildAndroid.cs`'s "Apply Android Player settings", and the baked canvas in `Assets/Scenes/Slot.unity`.

**2.2 The pure layout core.** New `Assets/Scripts/UI/ScreenLayout.cs` (safe area in canvas units, `FitAspect`, compact-width detection) and `Assets/Scripts/Slots/SlotLayout.cs` — a pure `Compute(canvas, safe, reelCount, rowCount)` returning the machine rect, the reel column rects, the header rect, the control-bar rect and the button slots. `SlotPresenter` only applies the result.

**2.3 Reels from the mask geometry.** New `Assets/Scripts/Slots/SlotFrameGeometry.cs` with the measured window rects (168–492, 535–859, 901–1225, 1268–1592, 1634–1958 × 114–954 of 2132×1070) and a comment recording how they were measured. `ReelsView` gains a mode taking **explicit column rects** instead of deriving a square cell from its parent. Cell size = `min(windowWidth, windowHeight / rows)` with a small inset so the mask's gold edge never clips a symbol. **This fixes the existing clipping bug, not just the future one.**

**2.4 The scaler.** `matchWidthOrHeight` 0.5 → **1** (height match) in `UiFactory.cs:194`, `ProjectSetup.cs:293` and the baked `Slot.unity`. Height is then fixed at 1080 reference units on every device and only the width varies.

**2.5 Safe area.** A `SafeAreaFitter` on the scene roots that applies the notch insets and re-lays out when the screen resizes (Android multi-window and foldables resize the activity).

**2.6 Proportional chrome.** Header height `clamp(safeH × 0.075, 64, 96)`; control bar `clamp(safeH × 0.14, 120, 170)`; the six controls placed by **anchor fraction** instead of the fixed `-636, -426, -216, 54, 324, 544`; font sizes from the bar height; touch targets never below 64px.

**2.7 The lobby's bounded pass.** `LobbyPresenter` hard-codes `ReferenceWidth = 1920` and fixed band Y constants. It does not need the full Phase 5 treatment, but it must not look broken on a 21:9 phone: respect the safe area, centre the content column, and make the band heights proportional to the safe height. **Also hide the category chips that would have no entries** — with one game, `Jackpots`, `New` and `High Volatility` are near-empty, and an empty chip is a dead end.

**Proves it:** new `Assets/Tests/Editor/ResponsiveLayoutTests.cs` — at 1920×1080, 2340×1080, 2560×1080, 2048×1536 and 1334×750: the machine fits the safe area, every reel column sits inside its mask window with no clipping, the bar's controls do not overlap and stay inside the bar, touch targets ≥ 64px. A PlayMode test that the slot screen's controls are reachable at the current resolution.

### Stage 3 — Velvet Room's progressive

Phases 1–4 of `lusthouse-jackpots-responsive.md`. Do it in that order: **the maths first, then the UI** — the RTP gate has to prove the retune before any jackpot UI is built (casino §0, line 228).

**3.1 The feature and the handler.** New `ProgressiveJackpotFeature` (three tiers, ids 30/31/32, each with a seed value) and `ProgressiveJackpotHandler : ISlotFeatureHandler`, registered in `SlotFeatureRegistry.EnsureDefaults`. `CanTrigger`: the tier's symbol appears **anywhere on the grid** AND the base game won. `Run`: award the pot value, add it to a new `SpinResult.progressiveWin`, emit a `FeatureStep { kind = Award, amount = pot, tierName }`.

**Purity rule:** handlers must not touch the wallet or the scene. The pot arrives through the context — `FeatureContext` gains `IProgressivePotSource potSource`; null falls back to the tier's `seedValue` so headless tests and the simulator stay deterministic.

**3.2 The service, the drip and the persistence.** New `Assets/Scripts/Core/ProgressiveJackpotService.cs` on the `ServicesBootstrap` root (`DontDestroyOnLoad`), beside `WalletService` / `PlayerProgression`.

- `ApplyDrip(gameId, bet)` on every completed spin; `PotValue(gameId, tier)`; `ResetPot(gameId, tier)` back to the seed.
- Backed by the schema-2 `SaveDocument.progressive` block from Stage 0. A missing key falls back to the seed.
- `SlotGameRunner.Spin()` order becomes: debit → `reelEngine.Spin` → `progressive.ApplyDrip` → credit → reset the pots that were won.

**3.3 The ledger.** Append `LedgerReason.Jackpot` to the **end** of the enum. The base win is credited as `SpinPayout`; the progressive part as `Jackpot`. `SpinResult.progressiveWin` is **excluded** from `maxWinMultiplier` checks — a progressive hit legitimately exceeds the base max win, and `ReconcileMaxWin` must not try to include it.

**3.4 The content.**

- Three new symbols (ids 30 Mini, 31 Minor, 32 Major) in `ContentBootstrap.BuildSymbols` and on Velvet Room's five reel strips. They are ordinary paying symbols; the tier link lives in the feature data, not on the symbol.
- **Art:** the project already generates a placeholder tile procedurally (`SlotSymbolExpansion.EnsurePlaceholderArt` → `BuildPlaceholderTile`, a tinted square with a 5×7 bitmap-font letter, headless-safe). Use it. Do not block on PNGs.
- **Fix the stale art path** while you are there: `ContentBootstrap.ArtForSymbol` still points at `Assets/Art/Slots/ace.png`; the art moved to `Assets/Art/Slots/velvet_room/`.
- **Make `progressive` mean "has a pot".** `ContentBootstrap.cs:474` sets `def.progressive = template == SlotTemplate.Progressive` on creation, and `NewSlotGame` can still produce a Progressive-template game with the flag on and no pot behind it — the same lie as the meter. **Recommendation: drop `Progressive` from the wizard's template list** and let a second progressive be made by copying a real one. `MigrateDefinition` must not touch the flag, so Velvet Room keeps it.
- `SlotMachineDefinition.progressiveSeed` is deprecated in favour of the per-tier seeds; `progressive` stays as the lobby's flag.

**3.5 The retune and the re-sign-off.** Adding three symbols to five strips moves the base RTP off 96.2%.

- `SimulationEngine.Run` subtracts `result.progressiveWin` from the win total and reports `progressiveHits` and `progressivePaid` separately. `ExactRtp` is unchanged.
- Retune with `SlotSymbolExpansion.RetuneToDeclaredTarget(velvetRoom)` — it already subtracts the affine bonus term and scales the paytable, which is right because the progressive is not part of the base budget.
- Re-sign-off: `rtpStatus` → `Measured` via the Math Simulator, and `RtpGateTests` green again.
- Mirror the change into `Tools/rtp_model.py` so it ignores the progressive feature.

**3.6 The UI.** The jackpot strip lives in the header Stage 2 rebuilds, so this comes after 2.

- Slot screen: a Mini/Minor/Major strip in the header showing live pot values, a jackpot banner and a full-screen flash on a hit, and the winning tier cell highlighted so the player can see *why* the pot paid.
- Lobby: Velvet Room's tile gets a live corner meter and the animated magenta border; the hero jackpot slide shows the meters.
- A new `AudioCue.JackpotWin`, synthesised like every other sound (the cut list says the win-tier cues can carry it if this must shrink).
- Reduced motion: the meter snaps instead of rolling (`SettingsState.reducedMotion` exists).

**Proves it:** new `Assets/Tests/Editor/JackpotTests.cs` (a tier pays only with symbol + win; the award equals the pot value; the pot resets to the seed after a hit; the drip grows the pot; two tiers can pay on one spin; the award is excluded from the base RTP) and `ProgressivePersistenceTests` (Stage 0). Extend `GuardrailTests` — a jackpot payout writes a `LedgerReason.Jackpot` entry and the ledger still sums to the balance. Extend `RtpGateTests` — the gate measures the **base** RTP, Velvet Room stays green after the retune, and progressive awards never trip `maxWinBreaches`. A PlayMode test that a jackpot spin credits the wallet and resets the pot.

### Stage 4 — the public build

**4.1 The catalogue.** Set `locked: 1` on the four placeholder entries in `Assets/Data/Catalog/GameCatalog.asset` (neon_nights, midnight_masquerade, diamond_hold, house_edge) and give each a `lockedMessage`. **Data only — no code.** The grid renders a `COMING SOON` badge and refuses the launch on paths that already exist. Their `rtpStatus` stays unmeasured and their targets stay documented as *design values, not achieved* (`ContentBootstrap.cs:389-391` already says so).

**4.2 The desktop build.** New `Assets/Scripts/Editor/BuildDesktop.cs`, modelled on `BuildAndroid.cs`: x86_64 IL2CPP, the same scene list, the same `AddressableAssetSettings.BuildPlayerContent` call, a `Builds/Windows/` output, a menu item and a `-executeMethod` batch entry. Keep `BuildAndroid.cs` working with the new orientation.

**4.3 The release gate.** New `Tools/release_check.ps1`, one command, failing loudly: `check_compile.ps1 -WithTests` → the EditMode suite → the PlayMode suite → `Tools/VerifyRunner.cs` → and an explicit assertion that the RTP gate is green. The build must not be produced by a path that skips any of these.

**4.4 The build's own notes.** A `PUBLIC_BUILD.md` (or a section in the README) that states what is in the build, what is deliberately not in it and why, and the guardrail-2 review: **no cash-out language anywhere in the build or its listing**, and the store copy must say plainly that this is a simulated gambling game for adults with no real money. The listing copy itself is the user's to write; the checklist is ours.

---

## 4. The release gate

The build is public when all of these are true, and the gate script enforces them:

1. `check_compile.ps1 -WithTests` clean.
2. EditMode suite green, **including** `RtpGateTests` and the new `JackpotTests`, `ProgressivePersistenceTests` and `ResponsiveLayoutTests`.
3. PlayMode suite green, **including** the gate → disclosure → prompt order, the self-exclusion block, and the jackpot spin.
4. `Tools/VerifyRunner.cs` green.
5. Velvet Room is `rtpStatus: Measured` and its exact RTP is inside ±0.5% of 96.2%.
6. No placeholder art is visible in the build (the four hidden games are `COMING SOON`; Velvet Room's three new tier symbols may carry the procedural tile).
7. The disclosure is reachable from the gate **and** from the lobby, and its printed RTP equals the measured RTP.
8. A fresh profile can reach the first spin without a purchase and without hitting a 0-Coins wall.
9. The build runs on a 21:9 phone and on a 4:3 tablet with nothing clipped and nothing overlapping.
10. `git status` is clean of stray files; the commit message states what is in the build.

---

## 5. Risks, and the fallback for each

| Risk | How it shows | Fallback |
|---|---|---|
| **The retune will not land within ±0.5%** after three symbols join five hand-tuned strips | `RtpGateTests` goes red | The retune scales the paytable, so this is unlikely. If it fails, ship the build **without** the progressive: remove the jackpot presentation (ribbon, tile border, `Jackpots` tab, carousel slide) and keep the mechanic behind a flag until the maths lands. The lobby must never advertise a pot that does not exist — that is the one thing this chunk must not leave broken |
| The disclosure's numbers drift from the maths | A definition is retuned and the sheet is stale | The sheet reads from `SimulationEngine` at runtime, and an EditMode test asserts the printed RTP equals `ExactRtp`. No hand-typed numbers |
| The responsive pass overruns and eats Stage 3 | Stage 2 grows | Ship the orientation lock and the scaler change (both minutes), defer the reel-from-mask work to the next chunk, and keep the progressive's meter off the header until the header is responsive. **Do not ship a distorted slot screen to make a deadline** |
| The lobby's responsive pass breaks the 8 states | Visual regressions | The lobby is the least visible part of a build; if 2.7 overruns, keep the lobby at 1920 reference and centre it, and say so in the build notes |
| The 1 → 2 migration corrupts an existing save | A player loses progress | The migration is additive and idempotent; a v1 save migrates to empty pots and default flags. Test it against a real v1 fixture, not a hand-written one |
| The public build's APK is too big | The 15.8 MB streamed music plus the content bundle | The music streams from the bundle, not the APK's data segment. Check the size after the first build; if it is over budget, move the definition's content bundle to a remote group |
| Guardrail 2 leaks into the listing copy | Store copy implies cash-out | 4.4's checklist. The copy is reviewed, not assumed |

---

## 6. Explicitly out of scope

Each of these is a real gap and is **not** in this chunk, with the reason:

- **IAP, Gems purchase, the parental gate on purchase** — M6 in the plan; needs a store account and cannot be exercised without one. The Gems economy works without it: 25 Gems at first run plus 25 on day 7 of the daily gift, converting at 1000:1 with a 3-per-day cap.
- **Missions, the rewards pass** — the earn side of the economy is 60% real; missions are what makes it whole, and they are M4 work. §4.4's instruction applies: re-check the ratio with the economy report.
- **Tournaments** — the design is settled (§2.9) and the implementation is M6. The panel without the bracket is a dead end, so neither ships here.
- **Club, the Boutique, cosmetics, Settings** — M4. The `18+` chip and the `+` buttons currently open placeholder sheets; they keep doing so, and the build notes say so.
- **Localisation** — M7. The build is English-only, and the plan's localisation pass is a separate milestone.
- **The other four games' art and maths** — M5-scale. They are `COMING SOON` instead.
- **WebGL** — the first WebGL build this project has ever made, with the 8 MB payload budget and the open Web refusal-screen gap. Not this chunk.
- **The Web refusal screen for the age gate** — the gate's refusal path calls `Application.Quit()`, which is a no-op on Web. Web is out of scope, so the gap stays, and the build notes must not claim otherwise.
- **A server, live leaderboards, the data-deletion path §5.7 calls unresolved** — v1 has no server.

---

## 7. Plan files to correct as part of this work

These are documentation defects that will mislead the next reader, found while planning:

1. `lusthouse-casino.md:1525` — M6 still says "Template E progressive". The progressive is Velvet Room's.
2. `lusthouse-casino.md:928` — the roster still lists Velvet Room as "B + **E**" and names "House Edge" separately; §3.4's note at :938-956 already corrects it, so make the table agree.
3. `lusthouse-casino.md:3` — the status line still says the focus is "slot presentation polish, content/art, and tuning the four placeholder titles". The focus is now this chunk.
4. `lusthouse-casino.md` §0.2 — add a row for the **progressive** (it is specced, not built) and one for **probability disclosure / responsible play** (not built), so §0.2 stops reading as "everything left is content".
5. `lusthouse-casino.md` §6 — insert a milestone for this chunk between M2 and M4, or relabel M3, so the milestone list matches what is actually being built.
6. `README.md` — the "Known gaps" list must move probability disclosure, the progressive, the orientation lock and the catalogue decision out of "not yet built" as each lands.

---

## 8. Open questions

None blocking. Two things are the user's call, and neither blocks the first three stages:

- **The build's public home** — itch.io, a direct download, or a Steam demo — decides the packaging, the listing copy and the screenshots. It changes 4.4 and nothing else.
- **Whether the four `COMING SOON` tiles should show their blurbs.** They have none today (`GameCatalog.asset:33,46,60,73`), and a blank tile with a badge is fine, but a one-line teaser is better. Content, not code.
