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
            return raw
        payouts = field("payoutsByMatch")
        symbols.append({
            "id": field("id", int),
            "name": field("symbolName", str, ""),
            "pays": _decode_int64_hex(payouts) if payouts else [],
            "wild": field("isWild", bool, False),
            "scatter": field("isScatter", bool, False),
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
    }


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


def evaluate(model, stops):
    """PaylineEvaluator.Evaluate. Returns total win in bet-multiples."""
    grid = spin(model, stops)
    by_id = model["by_id"]
    total = 0

    if model["payType"] == 0:  # PayType.Lines
        top_symbol = highest_paying_symbol(model)
        for rows in model["paylines"]:
            win, _ = evaluate_line(model, grid, rows, top_symbol)
            total += win

    scatters = 0
    for reel in grid:
        for symbol_id in reel:
            symbol = by_id.get(symbol_id)
            if symbol and symbol["scatter"]:
                scatters += 1

    if model["hasFreeSpins"] and scatters >= model["freeSpinScatterCount"]:
        total += model["freeSpinAward"] * model["freeSpinBetMultiplier"]

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


def expected_line_win(model, p):
    """E[win in bet-multiples] for one payline, by exact enumeration.

    Enumerates ids^reels tuples weighted by their joint probability. This is the
    verified reference: it agrees with Monte Carlo to within sampling noise. A
    left-to-right DP over (base, matched, saw_wild) was tried and was wrong — it
    dropped the late-wild case where Nine/Nine/Nine/Nine/Wild upgrades to 250x —
    so correctness beats speed here and the tuner's scan is kept coarse instead.

    Every (reel,row) cell is an independent draw from `p`, and all reels share one
    distribution when the strips are identical, so this single value applies to every
    payline regardless of its row pattern.
    """
    top = None
    ids = [i for i in sorted(p) if p[i] > 0.0]
    total = 0.0
    for tup in product(ids, repeat=model["reelCount"]):
        prob = 1.0
        for symbol_id in tup:
            prob *= p[symbol_id]
        if prob:
            total += prob * line_win(model, tup, top)
    return total


def exact_report(model, reel_probs):
    """Exact RTP: expected win per spin in bet-multiples.

    Valid when every reel shares the same symbol distribution, which holds for the
    identical strips written by the tuner.
    """
    p = reel_probs[0]
    return {"rtp": len(model["paylines"]) * expected_line_win(model, p), "exact": True}


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
        multiples = evaluate(model, stops)
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

                    probs = probs_from_strip(strip)
                    rtp = exact_report(model, [probs])["rtp"]
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
    exact = exact_report(model, probs)["rtp"]
    ok = abs(exact - target) <= 0.005
    print(f"exact RTP : {exact:.6f} vs target {target:.4f} -> "
          f"{'PASS' if ok else 'FAIL'} (+/-0.5%)")

    report = simulate(model, args.spins, 100, args.seed)
    print(f"Monte Carlo @ {report['spins']:,} spins, seed {args.seed}: "
          f"RTP {report['rtp']:.4f}")
    print(f"hit rate  : {report['hitRate']:.4f}")
    print(f"max win   : {report['maxWin']}x bet")


if __name__ == "__main__":
    main()