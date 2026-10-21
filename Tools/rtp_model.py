"""
Headless RTP model for Lust House slot definitions.

Mirrors the C# in Assets/Scripts/Slots/ exactly:
  ReelEngine.Stop / BuildGrid   -> spin()
  PaylineEvaluator.EvaluateLine -> evaluate_line()
  SymbolDefinition.PayoutForMatch-> payout_for_match()

Exists so reel strips can be tuned without holding the Unity project lock
(-batchmode cannot run while the editor has the project open). The editor's
LustHouse > Math Simulator remains the authoritative check; this is the fast loop.

Usage:
  python Tools/rtp_model.py <asset> [--spins N] [--seed S]
  python Tools/rtp_model.py <asset> --tune [--target R] [--length L]
"""

import argparse
import hashlib
import random
import re
import sys
from itertools import product

# ---------------------------------------------------------------- parsing


def _read(path):
    with open(path, "r", encoding="utf-8") as handle:
        return handle.read()


def _decode_int32_hex(blob):
    """Unity serializes int[] as a flat little-endian hex string."""
    raw = bytes.fromhex(blob)
    return [int.from_bytes(raw[i:i + 4], "little") for i in range(0, len(raw), 4)]


def _decode_int64_hex(blob):
    raw = bytes.fromhex(blob)
    return [int.from_bytes(raw[i:i + 8], "little") for i in range(0, len(raw), 8)]


def _entry_blocks(text, key):
    """Collect each `- key: value` list entry plus its indented body.

    Unity writes list entries with the key and value on one line, e.g.
        symbols:
        - id: 0
          symbolName: Wild
    so the body is every following line indented deeper than the `-`.
    """
    lines = text.split("\n")
    start = re.compile(r"^(\s*)- " + re.escape(key) + r":")
    blocks = []
    index = 0
    while index < len(lines):
        match = start.match(lines[index])
        if not match:
            index += 1
            continue
        indent = len(match.group(1))
        body = [re.sub(r"^\s*-\s*", "", lines[index])]
        index += 1
        while index < len(lines):
            line = lines[index]
            if not line.strip():
                index += 1
                continue
            line_indent = len(line) - len(line.lstrip())
            if line_indent <= indent:
                break
            body.append(line)
            index += 1
        blocks.append("\n".join(body))
    return blocks


def parse_asset(path):
    text = _read(path)

    def scalar(field, cast=str, default=None):
        match = re.search(r"^\s*" + field + r":\s*(.*?)\s*$", text, re.M)
        if not match:
            return default
        raw = match.group(1)
        if cast is bool:
            return raw == "1"
        if cast is int:
            return int(raw)
        if cast is float:
            return float(raw)
        return raw

    symbols = []
    for block in _entry_blocks(text, "id"):
        def field(name, cast=str, default=None):
            match = re.search(r"^\s*" + name + r":\s*(.*?)\s*$", block, re.M)
            if not match:
                return default
            raw = match.group(1)
            if cast is bool:
                return raw == "1"
            if cast is int:
                return int(raw)
            if cast is float:
                return float(raw)
            return raw
        payouts = field("payoutsByMatch")
        symbols.append({
            "id": field("id", int),
            "name": field("symbolName", str, ""),
            "pays": _decode_int64_hex(payouts) if payouts else [],
            "wild": field("isWild", bool, False),
            "scatter": field("isScatter", bool, False),
            "block": field("megaBlockMultiplier", float, 0.0) or 0.0,
        })

    paylines = []
    for block in _entry_blocks(text, "lineName"):
        match = re.search(r"^\s*rows:\s*([0-9a-fA-F]+)\s*$", block, re.M)
        if match:
            paylines.append(_decode_int32_hex(match.group(1)))

    reel_strips = []
    lines = text.split("\n")
    current = None
    try:
        start = next(i for i, l in enumerate(lines)
                     if re.match(r"^\s*reelStrips:\s*$", l))
    except StopIteration:
        start = None

    if start is not None:
        # Unity's real format for List<ReelStrip> is one hex int32 blob per reel:
        #   reelStrips:
        #   - symbols: 00000000...01000000...
        for line in lines[start + 1:]:
            if not line.strip():
                continue
            blob = re.match(r"^\s*-\s*symbols:\s*([0-9a-fA-F]+)\s*$", line)
            if blob:
                reel_strips.append(_decode_int32_hex(blob.group(1)))
                continue
            # Expanded int[][] form, kept for hand-edited assets:
            #   reelStrips:
            #   - - 1
            #     - 2
            outer = re.match(r"^(\s*)-\s+-\s*(.*)$", line)
            if outer:
                if current is not None:
                    reel_strips.append(current)
                current = []
                rest = outer.group(2).strip()
                if rest:
                    current.append(int(rest))
                continue
            inner = re.match(r"^\s*-\s+(-?\d+)\s*$", line)
            if inner:
                if current is not None:
                    current.append(int(inner.group(1)))
                continue
            break
        if current is not None:
            reel_strips.append(current)
    else:
        # Inline form: reelStrips: [[1,2],[3]]
        inline = re.search(r"^\s*reelStrips:\s*\[(.*?)\]\s*$", text, re.M | re.S)
        if inline:
            for inner in re.findall(r"\[([^\]]*)\]", inline.group(1)):
                reel_strips.append([int(v) for v in inner.split(",") if v.strip()])

    return {
        "id": scalar("id", str, ""),
        "symbols": symbols,
        "by_id": {s["id"]: s for s in symbols},
        "paylines": paylines,
        "reelCount": scalar("reelCount", int, 3),
        "rowCount": scalar("rowCount", int, 3),
        "payType": scalar("payType", int, 0),
        "reelStrips": reel_strips,
        "targetRtp": scalar("targetRtp", float, 0.96),
        "maxWinMultiplier": scalar("maxWinMultiplier", int, 0),
        "hasFreeSpins": scalar("hasFreeSpins", bool, False),
        "freeSpinScatterCount": scalar("freeSpinScatterCount", int, 3),
        "freeSpinAward": scalar("freeSpinAward", int, 0),
        "freeSpinBetMultiplier": scalar("freeSpinBetMultiplier", int, 1),
        "bonus": _parse_bonus_feature(text),
    }


def _parse_bonus_feature(text):
    """The pick-a-prize round, if the game declares one.

    Features live in Unity's SerializeReference registry: the main object holds
    only a rid pointer, and the real data sits under `references: RefIds:` behind
    a `type:` line naming the class. This walks the data block that follows the
    BonusGameFeature type line and reads the trigger and the prize table.
    """
    lines = text.split("\n")
    for i, line in enumerate(lines):
        if "class: BonusGameFeature" not in line:
            continue

        feature = {"triggerSymbolId": -1, "triggerCount": 0, "choices": 0,
                   "picks": 0, "prizes": []}
        prize = None
        for raw in lines[i + 1:]:
            indent = len(raw) - len(raw.lstrip())
            # The data block's fields sit at 8 spaces; anything shallower is the
            # next registry entry or the parent object, and ends this one.
            if raw.strip() and indent < 6:
                break

            payout = re.match(r"^\s*-\s*payout:\s*(\d+)\s*$", raw)
            if payout:
                prize = {"payout": int(payout.group(1)), "weight": 0.0}
                feature["prizes"].append(prize)
                continue

            weight = re.match(r"^\s*weight:\s*([\d.]+)\s*$", raw)
            if weight and prize is not None:
                prize["weight"] = float(weight.group(1))
                continue

            field = re.match(r"^\s*(triggerSymbolId|triggerCount|choices|picks):\s*(-?\d+)\s*$", raw)
            if field:
                feature[field.group(1)] = int(field.group(2))

        return feature if feature["picks"] > 0 else None
    return None


# ---------------------------------------------------------------- math


def payout_for_match(symbol, count):
    """SymbolDefinition.PayoutForMatch: index 0 == three of a kind."""
    if not symbol["pays"]:
        return 0
    index = count - 3
    if index < 0 or index >= len(symbol["pays"]):
        return 0
    return symbol["pays"][index]


def spin(model, stops):
    """ReelEngine.BuildGrid."""
    strips = model["reelStrips"]
    grid = []
    for reel in range(model["reelCount"]):
        strip = strips[reel]
        grid.append([strip[(stops[reel] + row) % len(strip)] for row in range(model["rowCount"])])
    return grid


def highest_paying_symbol(model):
    """PaylineEvaluator.HighestPayingSymbol. Strict '>' so ties keep the first."""
    best_id = -1
    best_payout = -1
    for symbol in model["symbols"]:
        if symbol["scatter"]:
            continue
        five = payout_for_match(symbol, 5)
        if five > best_payout:
            best_payout = five
            best_id = symbol["id"]
    return best_id


def evaluate_line(model, grid, rows, top_symbol):
    """PaylineEvaluator.EvaluateLine. Returns (win_multiples, count)."""
    by_id = model["by_id"]
    base_symbol = grid[0][rows[0]]
    base_def = by_id.get(base_symbol)
    if base_def is None or base_def["scatter"]:
        return 0, 0

    saw_wild = base_def["wild"]
    matched = 1

    for reel in range(1, len(rows)):
        symbol_id = grid[reel][rows[reel]]
        other = by_id.get(symbol_id)
        if other is None:
            break
        if symbol_id == base_symbol:
            matched += 1
            continue
        if other["wild"]:
            matched += 1
            saw_wild = True
            continue
        break

    if matched < 3:
        return 0, 0

    ids = [grid[r][rows[r]] for r in range(len(rows))]
    pay_id = substitute_symbol(model, ids) if saw_wild else base_symbol
    pay_def = by_id.get(pay_id)
    if pay_def is None:
        return 0, 0
    return payout_for_match(pay_def, matched), matched


def evaluate(model, stops, rng=None):
    """PaylineEvaluator.Evaluate. Returns total win in bet-multiples.

    When `rng` is given and the game declares a bonus round, the round is played
    for real: draw `choices` hidden prizes, take `picks` of them blind. The pick
    strategy is irrelevant to the expectation — every option is an i.i.d. draw —
    so the simulator simply takes the first `picks`, matching what
    BonusGameHandler.Run does for a non-interactive round.
    """
    grid = spin(model, stops)
    by_id = model["by_id"]
    total = 0

    if model["payType"] == 0:  # PayType.Lines
        top_symbol = highest_paying_symbol(model)
        for rows in model["paylines"]:
            win, _ = evaluate_line(model, grid, rows, top_symbol)
            total += win

    # Mega blocks: every exact 2x2 of an opted-in symbol pays on top of the lines.
    # Mirrors PaylineEvaluator.EvaluateBlocks — exact match, no wild substitution.
    total += evaluate_blocks(model, grid)

    scatters = 0
    for reel in grid:
        for symbol_id in reel:
            symbol = by_id.get(symbol_id)
            if symbol and symbol["scatter"]:
                scatters += 1

    if model["hasFreeSpins"] and scatters >= model["freeSpinScatterCount"]:
        total += model["freeSpinAward"] * model["freeSpinBetMultiplier"]

    feature = model.get("bonus")
    if feature and rng is not None:
        total += play_bonus_round(model, feature, grid, rng)

    return total


def evaluate_blocks(model, grid):
    """PaylineEvaluator.EvaluateBlocks: bet-multiples won by 2x2 mega blocks.

    Exact match only — all four cells the same symbol id, and that symbol must
    have opted in with megaBlockMultiplier > 0. A wild in the quad breaks it.
    Overlapping windows each pay: a 2x3 slab is two blocks.
    """
    by_id = model["by_id"]
    total = 0
    for reel in range(len(grid) - 1):
        for row in range(len(grid[reel]) - 1):
            symbol_id = grid[reel][row]
            if grid[reel][row + 1] != symbol_id:
                continue
            if grid[reel + 1][row] != symbol_id:
                continue
            if grid[reel + 1][row + 1] != symbol_id:
                continue
            symbol = by_id.get(symbol_id)
            if not symbol or symbol["block"] <= 0:
                continue
            total += payout_for_match(symbol, 4) * symbol["block"]
    return total


def play_bonus_round(model, feature, grid, rng):
    """One pick-a-prize round: the sum of `picks` hidden prizes, in bet-multiples.

    Options are drawn i.i.d. from the weighted prize table, so any `picks` of the
    `choices` options have the same expected total — which is exactly why the
    closed form B = P(trigger) x picks x weightedMean holds for blind play.
    """
    by_id = model["by_id"]

    def is_trigger(symbol_id):
        if feature["triggerSymbolId"] >= 0:
            return symbol_id == feature["triggerSymbolId"]
        symbol = by_id.get(symbol_id)
        return bool(symbol and symbol["scatter"])

    triggers = sum(1 for reel in grid for symbol_id in reel if is_trigger(symbol_id))
    if triggers < feature["triggerCount"]:
        return 0

    prizes = feature["prizes"]
    weights = [p["weight"] for p in prizes]
    total_weight = sum(weights)
    if total_weight <= 0:
        return 0

    total = 0
    for _ in range(min(feature["picks"], feature["choices"] or feature["picks"])):
        roll = rng.random() * total_weight
        for prize in prizes:
            roll -= prize["weight"]
            if roll < 0:
                total += prize["payout"]
                break
    return total


# ---------------------------------------------------------------- simulation


def substitute_symbol(model, ids):
    """The symbol a wild-assisted line pays as.

    Mirrors PaylineEvaluator.SubstituteSymbol: the first non-wild, non-scatter symbol
    the wild covers, or the wild itself when the line is entirely wilds. Paying the
    game's most expensive symbol instead made every wild a Filigree and put the RTP
    floor at 579%, so the declared target was unreachable.
    """
    for symbol_id in ids:
        symbol = model["by_id"].get(symbol_id)
        if symbol and not symbol["wild"] and not symbol["scatter"]:
            return symbol_id
    for symbol_id, symbol in model["by_id"].items():
        if symbol["wild"]:
            return symbol_id
    return -1


def line_win(model, ids, top_symbol=None):
    """EvaluateLine for a single line given the symbol ids it lands on.

    `ids` is one id per reel, already gathered from that reel's rows.
    """
    by_id = model["by_id"]
    base_symbol = ids[0]
    base_def = by_id.get(base_symbol)
    if base_def is None or base_def["scatter"]:
        return 0

    saw_wild = base_def["wild"]
    matched = 1
    for reel in range(1, len(ids)):
        symbol_id = ids[reel]
        other = by_id.get(symbol_id)
        if other is None:
            break
        if symbol_id == base_symbol:
            matched += 1
            continue
        if other["wild"]:
            matched += 1
            saw_wild = True
            continue
        break

    if matched < 3:
        return 0
    pay_def = by_id.get(substitute_symbol(model, ids) if saw_wild else base_symbol)
    if pay_def is None:
        return 0
    return payout_for_match(pay_def, matched)


def expected_line_win(model, reel_probs):
    """E[win in bet-multiples] for one payline, by exact enumeration.

    Mirrors SimulationEngine.SumLine: each reel contributes its own distribution, so
    per-reel strips are enumerated exactly rather than sampled. This is the verified
    reference -- it agrees with Monte Carlo to within sampling noise.

    Every (reel,row) cell is uniform over that reel's multiset, so the row a payline
    visits does not change its expectation: all lines share one value.
    """
    id_lists = [[i for i in sorted(p) if p[i] > 0.0] for p in reel_probs]
    total = 0.0
    for tup in product(*id_lists):
        prob = 1.0
        for r, symbol_id in enumerate(tup):
            prob *= reel_probs[r][symbol_id]
        if prob:
            total += prob * line_win(model, tup)
    return total


def expected_line_win_dp(model, reel_probs):
    """E[win in bet-multiples] for one payline, by exact DP.

    Same answer as expected_line_win, but O(reels x states x symbols) instead of
    O(symbols^reels), so a strip search can afford thousands of evaluations.

    The state must carry `sub` -- the first non-wild, non-scatter id on the line --
    because PaylineEvaluator.SubstituteSymbol reads the whole line, not just the
    matched run. A DP on (base, matched, saw_wild) alone drops the late-wild case
    where Nine/Nine/Nine/Nine/Wild upgrades to the five-of-a-kind pay; carrying
    `sub` is what fixes that.
    """
    by_id = model["by_id"]
    wild_ids = [i for i, s in by_id.items() if s["wild"]]
    wild_id = wild_ids[0] if wild_ids else -1

    # (base, matched, sawWild, broken, sub) -> probability mass.
    states = {}
    for symbol_id in sorted(reel_probs[0]):
        p0 = reel_probs[0][symbol_id]
        if p0 <= 0:
            continue
        base_def = by_id.get(symbol_id)
        if base_def is None or base_def["scatter"]:
            continue                      # a scatter (or unknown) base never pays
        key = (symbol_id, 1, base_def["wild"], False,
               None if base_def["wild"] else symbol_id)
        states[key] = states.get(key, 0.0) + p0

    for r in range(1, model["reelCount"]):
        probs = reel_probs[r]
        ids = [i for i in sorted(probs) if probs[i] > 0.0]
        next_states = {}
        for (base, matched, saw_wild, broken, sub), mass in states.items():
            for symbol_id in ids:
                p = probs[symbol_id]
                n_matched, n_saw, n_broken = matched, saw_wild, broken
                if not n_broken:
                    other = by_id.get(symbol_id)
                    if other is None:
                        n_broken = True
                    elif symbol_id == base:
                        n_matched += 1
                    elif other["wild"]:
                        n_matched += 1
                        n_saw = True
                    else:
                        n_broken = True
                n_sub = sub
                if n_sub is None:
                    other = by_id.get(symbol_id)
                    if other is not None and not other["wild"] and not other["scatter"]:
                        n_sub = symbol_id
                key = (base, n_matched, n_saw, n_broken, n_sub)
                next_states[key] = next_states.get(key, 0.0) + mass * p
        states = next_states

    total = 0.0
    for (base, matched, saw_wild, broken, sub), mass in states.items():
        if matched < 3:
            continue
        pay_id = (sub if sub is not None else wild_id) if saw_wild else base
        pay_def = by_id.get(pay_id)
        if pay_def is None:
            continue
        total += mass * payout_for_match(pay_def, matched)
    return total


def exact_report(model, reel_probs):
    """Exact RTP: expected win per spin in bet-multiples.

    Enumerates each reel's own distribution, so per-reel strips are exact.

    The total is line RTP plus the pick-a-prize round's affine constant
    B = P(trigger) x picks x weightedMean(prize). The bonus does not scale with
    the paytable, so it is kept apart — folding it into the line sum would make
    the retune count it once per paytable entry instead of once per spin.

    Mega blocks live in the line term, not the bonus one: their pay is
    four-of-a-kind x a multiplier, i.e. linear in one paytable entry, so the
    retune's weights carry them (mirrors MathSimulator.BlockRtp inside LineRtp).
    """
    line = len(model["paylines"]) * expected_line_win_dp(model, reel_probs)
    line += block_rtp(model)
    bonus = bonus_rtp(model)
    return {"rtp": line + bonus, "line": line, "bonus": bonus, "exact": True}


def block_rtp(model):
    """Exact 2x2 mega-block RTP, in bet-multiples per spin:

        B = (rows-1) x SUM_r a(r) x a(r+1) x four-of-a-kind x multiplier

    summed over every symbol that opted in. a(r) is the fraction of cyclically
    adjacent stop pairs on reel r that are both the symbol — a reel's visible
    rows are consecutive stops, so "rows j and j+1 are both s" is an adjacent
    pair, and the chance is the same for every window row. The two reels of a
    block window are independent, and overlapping windows each pay (mirrors
    MathSimulator.BlockRtp).
    """
    strips = model["reelStrips"]
    rows = max(1, model["rowCount"])
    total = 0.0
    for symbol in model["symbols"]:
        if symbol["block"] <= 0:
            continue
        pay = payout_for_match(symbol, 4) * symbol["block"]
        if pay <= 0:
            continue
        ratios = []
        for strip in strips:
            length = len(strip)
            pairs = sum(1 for i in range(length)
                        if strip[i] == symbol["id"] and strip[(i + 1) % length] == symbol["id"])
            ratios.append(pairs / length if length else 0.0)
        reel_pairs = sum(ratios[r] * ratios[r + 1] for r in range(len(ratios) - 1))
        total += (rows - 1) * reel_pairs * pay
    return total


def bonus_rtp(model):
    """B = P(trigger) x picks x weightedMean(prize), or 0 without a bonus.

    P(trigger) counts triggers in each reel's visible window. The cells a reel
    shows are consecutive stops of one strip, not independent draws, so each
    reel contributes a histogram over "triggers in my window" (one entry per
    stop position) and the exact grid distribution is the convolution of those
    histograms across reels — mirroring MathSimulator.TriggerChance.
    """
    feature = model.get("bonus")
    if not feature or feature["picks"] <= 0 or feature["triggerCount"] <= 0:
        return 0.0

    total = weight = 0.0
    for prize in feature["prizes"]:
        if prize["weight"] <= 0:
            continue
        total += prize["payout"] * prize["weight"]
        weight += prize["weight"]
    mean = total / weight if weight > 0 else 0.0
    if mean <= 0:
        return 0.0

    return trigger_chance(model, feature) * feature["picks"] * mean


def trigger_chance(model, feature):
    """P(at least triggerCount triggers on the grid), exactly."""
    def is_trigger(symbol_id):
        if feature["triggerSymbolId"] >= 0:
            return symbol_id == feature["triggerSymbolId"]
        symbol = model["by_id"].get(symbol_id)
        return bool(symbol and symbol["scatter"])

    # distribution[n] = P(exactly n triggers on the grid so far).
    distribution = [1.0]
    for strip in model["reelStrips"]:
        length = len(strip)
        window = max(1, model["rowCount"])

        # histogram[k] = P(this reel's window holds k triggers), by walking every
        # stop position of the strip.
        histogram = [0.0] * (window + 1)
        for stop in range(length):
            count = sum(1 for row in range(window)
                        if is_trigger(strip[(stop + row) % length]))
            histogram[min(count, window)] += 1.0 / length

        convolved = [0.0] * (len(distribution) + window)
        for a, da in enumerate(distribution):
            for b, hb in enumerate(histogram):
                convolved[a + b] += da * hb
        distribution = convolved

    chance = sum(distribution[feature["triggerCount"]:])
    return min(1.0, chance)


def probs_from_strip(strip):
    counts = {}
    for symbol_id in strip:
        counts[symbol_id] = counts.get(symbol_id, 0) + 1
    total = len(strip)
    return {k: v / total for k, v in counts.items()}


def simulate(model, spins, bet, seed):
    """Monte Carlo. Kept for cross-checking exact_report; slower and noisy."""
    if not model["reelStrips"] or len(model["reelStrips"]) != model["reelCount"]:
        raise SystemExit("reelStrips missing or wrong length — cannot simulate.")

    rng = random.Random(seed)
    reel_count = model["reelCount"]
    total_win = 0
    total_bet = spins * bet
    hits = 0
    max_multiple = 0

    for _ in range(spins):
        stops = [rng.randrange(len(model["reelStrips"][r])) for r in range(reel_count)]
        multiples = evaluate(model, stops, rng)
        if multiples > 0:
            hits += 1
        if multiples > max_multiple:
            max_multiple = multiples
        total_win += multiples * bet

    return {
        "spins": spins,
        "bet": bet,
        "rtp": total_win / total_bet if total_bet else 0.0,
        "hitRate": hits / spins if spins else 0.0,
        "maxWin": max_multiple,
    }


# ---------------------------------------------------------------- tuning


def build_strip(counts):
    """Counts: list of (symbol_id, count). Deterministic interleave, not shuffled,
    so the same counts always yield the same strip."""
    strip = []
    for symbol_id, count in counts:
        strip.extend([symbol_id] * count)
    return strip


def tune(model, target, length, spins, seed, steps=61):
    """Search strip shapes for an exact RTP of `target`.

    Three knobs: Wild count, Scatter count, and a premium spine share; the four
    common symbols then split whatever budget is left by inverse-payout weight,
    with the cheapest absorbing rounding so the strip is always exactly `length`.
    `scale` sweeps that weighting from "all common symbols equally weighted" toward
    "heavily skewed to the cheap end", which is the main RTP lever.
    """
    paying = [s for s in model["symbols"] if not s["scatter"] and not s["wild"]]
    paying.sort(key=lambda s: -payout_for_match(s, 5))

    premium = paying[:3]
    low = paying[3:]
    if len(low) < 2:
        raise SystemExit("not enough common paying symbols to tune")
    weights = [_default_weight(s) for s in low]
    weight_total = sum(weights)

    best = None
    for wild_c in range(0, 7):
        for scatter_c in (2, 3):
            for premium_share in range(1, max(2, length // 3)):
                per_premium = max(1, premium_share // len(premium))
                pinned = wild_c + scatter_c + per_premium * len(premium)
                low_budget = length - pinned
                if low_budget < len(low):
                    continue
                for step in range(1, steps):
                    scale = step / steps
                    counts = [(0, wild_c), (1, scatter_c)]
                    for s in premium:
                        counts.append((s["id"], per_premium))

                    assigned = 0
                    for index, (s, w) in enumerate(zip(low, weights)):
                        if index == len(low) - 1:
                            count = low_budget - assigned
                        else:
                            count = int(round(low_budget * w / weight_total * scale))
                        count = max(1, count)
                        counts.append((s["id"], count))
                        assigned += count

                    strip = build_strip(counts)
                    if len(strip) != length:
                        continue

                    # The bonus trigger chance depends on strip order, not just
                    # symbol frequencies, so the probe carries the candidate strip.
                    probe = dict(model, reelStrips=[list(strip)] * model["reelCount"])
                    probs = probs_from_strip(strip)
                    rtp = exact_report(probe, [probs] * model["reelCount"])["rtp"]
                    err = abs(rtp - target)
                    if best is None or err < best["err"]:
                        best = {"err": err, "rtp": rtp, "strip": strip,
                                "counts": counts, "scale": scale}

    return best


def _default_weight(symbol):
    """Inverse-payout weighting: cheap symbols are common, premiums are rare."""
    three = payout_for_match(symbol, 3)
    if three <= 0:
        return 1.0
    return max(0.25, 6.0 / three)


# ---------------------------------------------------------------- main


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("asset")
    parser.add_argument("--spins", type=int, default=100_000)
    parser.add_argument("--seed", type=int, default=12345)
    parser.add_argument("--tune", action="store_true")
    parser.add_argument("--target", type=float, default=None)
    parser.add_argument("--length", type=int, default=48)
    args = parser.parse_args()

    model = parse_asset(args.asset)
    target = args.target if args.target is not None else model["targetRtp"]

    print(f"definition : {model['id']}")
    print(f"reels      : {model['reelCount']} x {model['rowCount']}, "
          f"{len(model['paylines'])} paylines, pays={model['payType']}")
    print(f"target RTP : {target:.4f}")
    print(f"symbols    : " + ", ".join(
        f"{s['id']}={s['name']}" for s in model["symbols"]))
    print()

    if args.tune:
        print(f"tuning {args.length}-stop strip (exact enumeration)...")
        best = tune(model, target, args.length, args.spins, args.seed)
        if best is None:
            raise SystemExit("no viable strip found")

        ok = abs(best["rtp"] - target) <= 0.005
        print(f"\nexact RTP  : {best['rtp']:.6f}  vs target {target:.4f} -> "
              f"{'PASS' if ok else 'FAIL'} (+/-0.5%)")
        print(f"scale      : {best['scale']:.3f}")
        print("\ncounts:")
        for symbol_id, count in best["counts"]:
            symbol = model["by_id"].get(symbol_id, {})
            print(f"  {symbol_id:>3} {symbol.get('name','?'):<10} {count:>3}")

        # Cross-check the exact figure against Monte Carlo, as the editor will.
        confirm = dict(model)
        confirm["reelStrips"] = [list(best["strip"]) for _ in range(model["reelCount"])]
        report = simulate(confirm, args.spins, 100, args.seed)
        drift = abs(report["rtp"] - best["rtp"])
        print(f"\nMonte Carlo cross-check @ {report['spins']:,} spins: "
              f"RTP {report['rtp']:.4f} (drift {drift:.4f} from exact)")
        print(f"hit rate  : {report['hitRate']:.4f}")
        print(f"max win   : {report['maxWin']}x bet "
              f"(cap {model['maxWinMultiplier']}x -> "
              f"{'OK' if report['maxWin'] <= model['maxWinMultiplier'] else 'BREACH'})")

        emit = ",".join(str(v) for v in best["strip"])
        print(f"\nstrip ({len(best['strip'])} stops, identical on all "
              f"{model['reelCount']} reels):\n  {emit}")
        return

    if not model["reelStrips"]:
        raise SystemExit("reelStrips missing — run with --tune to generate them.")
    probs = [probs_from_strip(s) for s in model["reelStrips"]]
    report = exact_report(model, probs)
    exact = report["rtp"]
    ok = abs(exact - target) <= 0.005
    if report["bonus"] > 0:
        print(f"exact RTP : {exact:.6f} = line {report['line']:.6f} + "
              f"bonus {report['bonus']:.6f} vs target {target:.4f} -> "
              f"{'PASS' if ok else 'FAIL'} (+/-0.5%)")
    else:
        print(f"exact RTP : {exact:.6f} vs target {target:.4f} -> "
              f"{'PASS' if ok else 'FAIL'} (+/-0.5%)")

    mc = simulate(model, args.spins, 100, args.seed)
    print(f"Monte Carlo @ {mc['spins']:,} spins, seed {args.seed}: "
          f"RTP {mc['rtp']:.4f}")
    print(f"hit rate  : {mc['hitRate']:.4f}")
    print(f"max win   : {mc['maxWin']}x bet")


if __name__ == "__main__":
    main()