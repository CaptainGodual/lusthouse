// Standalone verification runner. Executes the pure-.NET paths of LustHouse (save
// system, guardrails, seeded RNG) outside Unity so the logic can be exercised without
// the editor holding the project lock. ScriptableObject-dependent paths (slot maths,
// simulator) are covered by the Unity EditMode tests instead.
using System;
using System.Collections.Generic;
using System.IO;
using LustHouse.Core;
using LustHouse.Slots;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public static class VerifyRunner
{
    private static int _pass;
    private static int _fail;

    private static void Check(string name, bool condition, string detail = "")
    {
        if (condition) { _pass++; Console.WriteLine("  PASS  " + name); }
        else { _fail++; Console.WriteLine("  FAIL  " + name + (detail.Length > 0 ? "  -- " + detail : "")); }
    }

    public static int Main()
    {
        Console.WriteLine("== LustHouse standalone verification ==");
        SaveTests();
        GuardrailTests();
        RngTests();
        Console.WriteLine();
        Console.WriteLine($"RESULT: {_pass} passed, {_fail} failed");
        return _fail == 0 ? 0 : 1;
    }

    private static string NewSaveDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "lh_verify_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    // ---------------------------------------------------------------- save

    private static void SaveTests()
    {
        Console.WriteLine("-- SaveSystem --");

        // Round trip
        string dir = NewSaveDir();
        string path = Path.Combine(dir, "save.json");
        var save = new SaveSystem(path);

        var doc = SaveDocument.CreateNew(DateTime.UtcNow);
        doc.player.displayName = "Verify";
        doc.player.level = 9;
        doc.player.ageConfirmed = true;
        doc.wallet.Find(Currency.Coins).balance = 4242;
        doc.wallet.Find(Currency.Gems).balance = 77;
        doc.wallet.Find(Currency.LustPoints).balance = 1000;
        doc.inventory.ownedIds.Add("base_ivory");
        save.Save(doc);

        Check("save file created", File.Exists(path));
        Check("no temp file left behind", !File.Exists(path + ".tmp"));

        var loaded = save.Load();
        Check("round trip ok", loaded.ok && loaded.warning == null, loaded.warning ?? "");
        Check("display name round-trips", loaded.document.player.displayName == "Verify");
        Check("level round-trips", loaded.document.player.level == 9);
        Check("age flag round-trips", loaded.document.player.ageConfirmed);
        Check("coins round-trip", loaded.document.wallet.Find(Currency.Coins).balance == 4242);
        Check("gems round-trip", loaded.document.wallet.Find(Currency.Gems).balance == 77);
        Check("lust round-trip", loaded.document.wallet.Find(Currency.LustPoints).balance == 1000);
        Check("owned ids round-trip", loaded.document.inventory.ownedIds.Count == 1);
        Check("schemaVersion stamped", loaded.document.schemaVersion == SaveDocument.CurrentSchemaVersion);

        // Overwrite
        doc.player.level = 10;
        save.Save(doc);
        Check("latest write wins", save.Load().document.player.level == 10);

        // Corrupt
        string dir2 = NewSaveDir();
        string path2 = Path.Combine(dir2, "save.json");
        File.WriteAllText(path2, "{ not json ][");
        var corrupt = new SaveSystem(path2).Load();
        Check("corrupt recovers", corrupt.ok && corrupt.document != null);
        Check("corrupt warns (warn chip, not modal)", !string.IsNullOrEmpty(corrupt.warning));
        Check("corrupt is backed up", File.Exists(path2 + ".bak"));

        // Truncated
        string dir3 = NewSaveDir();
        string path3 = Path.Combine(dir3, "save.json");
        File.WriteAllText(path3, "{\"schemaVersion\":1,\"player\":{\"displ");
        var trunc = new SaveSystem(path3).Load();
        Check("truncated recovers", trunc.ok && !string.IsNullOrEmpty(trunc.warning));

        // Legacy v0 flat save -> migration
        string dir4 = NewSaveDir();
        string path4 = Path.Combine(dir4, "save.json");
        File.WriteAllText(path4, "{\"coins\":5000,\"gems\":25,\"lust\":300}");
        var migrated = new SaveSystem(path4).Load();
        Check("v0 migration succeeds", migrated.ok && migrated.warning == null, migrated.warning ?? "");
        Check("migrated to current schema",
              migrated.document.schemaVersion == SaveDocument.CurrentSchemaVersion);
        Check("legacy coins preserved", migrated.document.wallet.Find(Currency.Coins).balance == 5000);
        Check("legacy gems preserved", migrated.document.wallet.Find(Currency.Gems).balance == 25);
        Check("legacy lust preserved", migrated.document.wallet.Find(Currency.LustPoints).balance == 300);
        Check("migration is auditable",
              migrated.document.wallet.Find(Currency.Coins).ledger.Count == 1 &&
              migrated.document.wallet.Find(Currency.Coins).ledger[0].delta == 5000);

        // v1 -> v2: the progressive block and the responsible-play flags appear,
        // at their safe defaults, and nothing else moves.
        string dirV1 = NewSaveDir();
        string pathV1 = Path.Combine(dirV1, "save.json");
        File.WriteAllText(pathV1,
            "{\"schemaVersion\":1,\"savedAtUtc\":\"2026-01-01T00:00:00Z\",\"migrated\":false," +
            "\"player\":{\"displayName\":\"Old Player\",\"level\":4,\"ageConfirmed\":true," +
            "\"totalSpins\":120}," +
            "\"wallet\":{\"accounts\":[{\"currency\":\"Coins\",\"balance\":9000,\"ledger\":[]}]}}");
        var v1 = new SaveSystem(pathV1).Load();
        Check("v1 migration succeeds", v1.ok && v1.warning == null, v1.warning ?? "");
        Check("v1 migrated to v2", v1.document.schemaVersion == 2);
        Check("v1 player preserved", v1.document.player.displayName == "Old Player" &&
                                     v1.document.player.level == 4 &&
                                     v1.document.player.ageConfirmed);
        Check("v1 wallet preserved", v1.document.wallet.Find(Currency.Coins).balance == 9000);
        Check("v1 progressive block is empty", v1.document.progressive != null &&
                                              v1.document.progressive.Count == 0);
        Check("v1 disclosure not yet seen", !v1.document.player.disclosureSeen);
        Check("v1 not self-excluded", v1.document.player.selfExcludedUntilUtc == "");

        // A corrupt progressive block must not crash the load.
        string dirCorruptPot = NewSaveDir();
        string pathCorruptPot = Path.Combine(dirCorruptPot, "save.json");
        File.WriteAllText(pathCorruptPot,
            "{\"schemaVersion\":2,\"progressive\":\"not-a-block\",\"player\":{}}");
        var corruptPot = new SaveSystem(pathCorruptPot).Load();
        Check("corrupt progressive block recovers",
              corruptPot.ok && corruptPot.document.progressive != null);

        // v2 round trip keeps the pots.
        string dirV2 = NewSaveDir();
        string pathV2 = Path.Combine(dirV2, "save.json");
        var v2 = new SaveSystem(pathV2);
        var v2Doc = SaveDocument.CreateNew(DateTime.UtcNow);
        v2Doc.progressive["velvet_room:Mini"] = 61000;
        v2Doc.player.disclosureSeen = true;
        v2.Save(v2Doc);
        var v2Loaded = v2.Load();
        Check("v2 pot round-trips", v2Loaded.ok &&
                                    v2Loaded.document.progressive.TryGetValue("velvet_room:Mini", out var pot) &&
                                    pot == 61000);
        Check("v2 disclosure flag round-trips", v2Loaded.document.player.disclosureSeen);
        Check("v2 schemaVersion round-trips",
              v2Loaded.document.schemaVersion == SaveDocument.CurrentSchemaVersion);

        // Timestamps are strings in the save, so they must come back as the strings
        // that were written. Newtonsoft parses ISO-looking strings into DateTime
        // tokens by default, which reformats them with the current culture.
        var tsDoc = SaveDocument.CreateNew(DateTime.UtcNow);
        tsDoc.player.selfExcludedUntilUtc = "9999-12-31T23:59:59Z";
        tsDoc.player.ageConfirmedUtc = "2026-10-22T03:15:50.1234567Z";
        string dirTimestamps = NewSaveDir();
        var tsSave = new SaveSystem(Path.Combine(dirTimestamps, "save.json"));
        tsSave.Save(tsDoc);
        var tsLoaded = tsSave.Load().document;
        Check("timestamps round-trip as written",
              tsLoaded.player.selfExcludedUntilUtc == "9999-12-31T23:59:59Z" &&
              tsLoaded.player.ageConfirmedUtc == "2026-10-22T03:15:50.1234567Z");

        // A corrupt pot block must not take the rest of the profile with it.
        string dirCorruptPotProfile = NewSaveDir();
        string pathCorruptPotProfile = Path.Combine(dirCorruptPotProfile, "save.json");
        File.WriteAllText(pathCorruptPotProfile,
            "{\"schemaVersion\":2,\"progressive\":42," +
            "\"player\":{\"displayName\":\"Veteran\",\"level\":6}," +
            "\"wallet\":{\"accounts\":[{\"currency\":\"Coins\",\"balance\":240000,\"ledger\":[]}]}}");
        var corruptPotProfile = new SaveSystem(pathCorruptPotProfile).Load();
        Check("corrupt pot block keeps the profile",
              corruptPotProfile.ok &&
              corruptPotProfile.document.player.displayName == "Veteran" &&
              corruptPotProfile.document.wallet.Find(Currency.Coins).balance == 240000);

        // Future schema must not be clobbered
        string dir5 = NewSaveDir();
        string path5 = Path.Combine(dir5, "save.json");
        string future = "{\"schemaVersion\":" + (SaveDocument.CurrentSchemaVersion + 5) + ",\"player\":{}}";
        File.WriteAllText(path5, future);
        var futureResult = new SaveSystem(path5).Load();
        Check("future schema warns", futureResult.ok && !string.IsNullOrEmpty(futureResult.warning));
        Check("future schema file untouched", File.ReadAllText(path5) == future);

        // Delete backs up
        var del = new SaveSystem(path);
        del.Delete();
        Check("delete removes save", !File.Exists(path));
        Check("delete keeps backup", File.Exists(path + ".bak"));

        foreach (var d in new[] { dir, dir2, dir3, dir4, dir5, dirV1, dirCorruptPot, dirV2, dirTimestamps, dirCorruptPotProfile })
        {
            try { Directory.Delete(d, true); } catch { }
        }
    }

    // ------------------------------------------------------------ guardrails

    private static void GuardrailTests()
    {
        Console.WriteLine("-- Guardrails --");

        Check("Lust is non-spendable", !CurrencyRules.IsSpendable(Currency.LustPoints));
        Check("Coins/Gems spendable",
              CurrencyRules.IsSpendable(Currency.Coins) && CurrencyRules.IsSpendable(Currency.Gems));

        var lust = new CurrencyAccount(Currency.LustPoints);
        Check("Lust account refuses debit at construction", !lust.spendable);
        Check("Lust grant works", lust.Apply(500, LedgerReason.Admin) && lust.balance == 500);
        Check("Lust debit refused", !lust.Apply(-100, LedgerReason.Spin));
        Check("Lust balance unchanged after refusal", lust.balance == 500);
        Check("Refused debit writes no ledger entry", lust.ledger.Count == 1);

        var coins = new CurrencyAccount(Currency.Coins);
        coins.Apply(100, LedgerReason.DailyGift);
        Check("Overdraw refused", !coins.Apply(-150, LedgerReason.Spin));
        Check("Overdraw leaves balance", coins.balance == 100);
        coins.Apply(-40, LedgerReason.Spin);
        coins.Apply(90, LedgerReason.SpinPayout);
        Check("Balance derives from ledger", coins.ReplayBalance() == coins.balance,
              $"{coins.ReplayBalance()} vs {coins.balance}");

        // Ledger trimming must not move the balance
        var trim = new CurrencyAccount(Currency.Coins);
        for (int i = 0; i < CurrencyAccount.MaxPersistedLedgerEntries + 50; i++)
            trim.Apply(1, LedgerReason.Spin);
        Check("Ledger is trimmed", trim.ledger.Count == CurrencyAccount.MaxPersistedLedgerEntries);
        Check("Trim does not change balance",
              trim.balance == CurrencyAccount.MaxPersistedLedgerEntries + 50);

        Check("Only Gems are premium", CurrencyRules.IsPremium(Currency.Gems) &&
                                     !CurrencyRules.IsPremium(Currency.Coins));
        Check("Conversion rate is 1000", CurrencyRules.GemsToCoinsRate == 1000);
        Check("Conversion cap is 3/day", CurrencyRules.MaxConversionsPerDay == 3);

        bool hasCoinToGem = false;
        foreach (var m in typeof(WalletService).GetMethods())
            if (m.Name.ToLowerInvariant().Contains("cointogem")) hasCoinToGem = true;
        Check("No Coins->Gems conversion exists", !hasCoinToGem);

        bool cosmeticHasCoinField = false;
        foreach (var f in typeof(LustHouse.Economy.CosmeticDefinition).GetFields())
            if (f.Name.ToLowerInvariant().Contains("coin")) cosmeticHasCoinField = true;
        Check("CosmeticDefinition has no coin price field", !cosmeticHasCoinField);

        string[] banned = { "rtp", "odds", "payout", "betmultiplier", "wager", "power", "bonus" };
        bool cosmeticHasGameplay = false;
        foreach (var f in typeof(LustHouse.Economy.CosmeticDefinition).GetFields())
            foreach (var b in banned)
                if (f.Name.ToLowerInvariant().Contains(b)) cosmeticHasGameplay = true;
        Check("CosmeticDefinition has no gameplay fields", !cosmeticHasGameplay);

        // EventBus unsubscribe must actually remove
        int calls = 0;
        Action<LevelUp> handler = _ => calls++;
        EventBus.Subscribe<LevelUp>(handler);
        EventBus.Publish(new LevelUp { newLevel = 2 });
        EventBus.Unsubscribe<LevelUp>(handler);
        EventBus.Publish(new LevelUp { newLevel = 3 });
        Check("EventBus.Unsubscribe removes the handler", calls == 1, $"calls={calls}");

        // A handler that unsubscribes during dispatch must not throw
        int inner = 0;
        Action<MissionClaimable> selfRemoving = null;
        selfRemoving = _ => { inner++; EventBus.Unsubscribe<MissionClaimable>(selfRemoving); };
        EventBus.Subscribe<MissionClaimable>(selfRemoving);
        try
        {
            EventBus.Publish(new MissionClaimable { missionId = "m1" });
            Check("Unsubscribing during dispatch is safe", true);
        }
        catch (Exception e)
        {
            Check("Unsubscribing during dispatch is safe", false, e.GetType().Name);
        }
        Check("Self-removing handler ran once", inner == 1, $"inner={inner}");
    }

    // ------------------------------------------------------------------ rng

    private static void RngTests()
    {
        Console.WriteLine("-- SeededRng --");

        var a = SeededRng.FromSeed(12345);
        var b = SeededRng.FromSeed(12345);
        bool identical = true;
        for (int i = 0; i < 1000; i++) if (a.NextULong() != b.NextULong()) { identical = false; break; }
        Check("Same seed -> identical stream", identical);

        var c = SeededRng.FromSeed(1);
        var d = SeededRng.FromSeed(2);
        bool differs = false;
        for (int i = 0; i < 32 && !differs; i++) if (c.NextULong() != d.NextULong()) differs = true;
        Check("Adjacent seeds differ", differs);

        var rng = SeededRng.FromSeed(999);
        for (int i = 0; i < 37; i++) rng.NextULong();
        ulong s0 = rng.State0, s1 = rng.State1;
        ulong expected = rng.NextULong();
        var resumed = SeededRng.FromState(s0, s1);
        Check("State round-trip resumes the stream", resumed.NextULong() == expected);

        var rangeRng = SeededRng.FromSeed(7);
        bool inRange = true;
        var seen = new bool[16];
        for (int i = 0; i < 20000; i++)
        {
            int v = rangeRng.NextInt(16);
            if (v < 0 || v >= 16) { inRange = false; break; }
            seen[v] = true;
        }
        Check("NextInt stays in range", inRange);
        bool allSeen = true;
        for (int i = 0; i < 16; i++) if (!seen[i]) allSeen = false;
        Check("NextInt covers the full range", allSeen);

        var uni = SeededRng.FromSeed(3);
        var counts = new int[8];
        const int draws = 80000;
        for (int i = 0; i < draws; i++) counts[uni.NextInt(8)]++;
        double expectedCount = draws / 8.0;
        bool flat = true;
        foreach (var n in counts) if (Math.Abs(n - expectedCount) / expectedCount > 0.10) flat = false;
        Check("NextInt is uniform", flat,
              string.Join(",", Array.ConvertAll(counts, x => x.ToString())));

        Check("NextInt(0) == 0", SeededRng.FromSeed(1).NextInt(0) == 0);
        Check("NextInt(1) == 0", SeededRng.FromSeed(1).NextInt(1) == 0);
    }
}