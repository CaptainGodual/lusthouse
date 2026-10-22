using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace LustHouse.Core
{
    public struct SaveLoadResult
    {
        public SaveDocument document;
        public bool ok;
        /// <summary>Non-null when the save was recovered from corruption or migration failure.</summary>
        public string warning;
    }

    /// <summary>
    /// Versioned, atomic, self-healing JSON save (plan §5.2, §2.13).
    ///
    /// Guarantees:
    ///  * wallet + inventory live in one document, so a migration cannot half-apply them;
    ///  * writes go to a temp file and are swapped in with <see cref="File.Replace"/>, so a
    ///    crash mid-write cannot destroy the previous save;
    ///  * a corrupt or unreadable save is copied to a backup before defaults are rebuilt —
    ///    the player is never shown a blocking dialog;
    ///  * a save from a *newer* schema is never overwritten.
    ///
    /// On WebGL <c>Application.persistentDataPath</c> is already IndexedDB-backed by Unity;
    /// no IDBFS layer is required (plan §0.3).
    /// </summary>
    public sealed class SaveSystem
    {
        private const string FileName = "save.json";
        private const string TempSuffix = ".tmp";
        private const string BackupSuffix = ".bak";

        private static SaveSystem _default;

        public string SavePath { get; }

        public SaveSystem(string path)
        {
            SavePath = path;
        }

        public static SaveSystem Default
        {
            get
            {
                if (_default == null)
                    _default = new SaveSystem(Path.Combine(Application.persistentDataPath, FileName));
                return _default;
            }
        }

        public bool Exists => File.Exists(SavePath);

        // ---- write ------------------------------------------------------------

        public void Save(SaveDocument doc)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));

            doc.schemaVersion = SaveDocument.CurrentSchemaVersion;
            doc.savedAtUtc = DateTime.UtcNow.ToString("o");

            string json = JsonConvert.SerializeObject(doc, Formatting.Indented);
            string tmp = SavePath + TempSuffix;

            var dir = Path.GetDirectoryName(SavePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            // Atomic-ish: write the full payload to a sibling temp file, flush it, then swap.
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var sw = new StreamWriter(fs))
            {
                sw.Write(json);
                sw.Flush();
                fs.Flush(true);
            }

            if (File.Exists(SavePath))
            {
                try
                {
                    File.Replace(tmp, SavePath, SavePath + BackupSuffix);
                    return;
                }
                catch (PlatformNotSupportedException)
                {
                    // Some platforms (notably WebGL-backed FS) do not implement Replace.
                }
                catch (IOException)
                {
                    // Falls through to the delete/move fallback below.
                }

                File.Delete(SavePath);
                File.Move(tmp, SavePath);
                return;
            }

            File.Move(tmp, SavePath);
        }

        // ---- read -------------------------------------------------------------

        public SaveLoadResult Load()
        {
            var result = new SaveLoadResult();

            if (!File.Exists(SavePath))
            {
                result.document = SaveDocument.CreateNew(DateTime.UtcNow);
                result.ok = true;
                return result;
            }

            string json;
            try
            {
                json = File.ReadAllText(SavePath);
            }
            catch (Exception e)
            {
                result.document = SaveDocument.CreateNew(DateTime.UtcNow);
                result.ok = true;
                result.warning = $"Save could not be read ({e.GetType().Name}); started a new profile.";
                Backup();
                return result;
            }

            JObject root = null;
            string parseError = null;
            try
            {
                // DateParseHandling.None. Newtonsoft's default turns any ISO-looking string
                // into a DateTime token, and handing that token to a string field formats
                // it with the current culture — so "9999-12-31T23:59:59Z" came back as
                // "12/31/9999 23:59:59" and every timestamp in the save was mangled on
                // the way in. The save stores timestamps as strings, so they must be
                // parsed as strings.
                using (var reader = new JsonTextReader(new StringReader(json))
                {
                    DateParseHandling = DateParseHandling.None
                })
                {
                    root = JObject.Load(reader);
                }
            }
            catch (Exception e)
            {
                parseError = e.Message;
            }

            if (root == null)
            {
                result.document = SaveDocument.CreateNew(DateTime.UtcNow);
                result.ok = true;
                result.warning = "Save was corrupt and has been reset to defaults.";
                LustLog.Warn($"[LustHouse] Corrupt save ({parseError}). Backed up to {SavePath}{BackupSuffix}");
                Backup();
                return result;
            }

            if (!SaveMigrationRunner.Run(root, out var migrationError))
            {
                if (migrationError != null && migrationError.Contains("this build understands up to"))
                {
                    // From a newer build. Do NOT clobber it — run in memory with defaults
                    // and leave the file untouched so downgrading is not destructive.
                    result.document = SaveDocument.CreateNew(DateTime.UtcNow);
                    result.ok = true;
                    result.warning = "Save was written by a newer version; running with a temporary profile.";
                    return result;
                }

                result.document = SaveDocument.CreateNew(DateTime.UtcNow);
                result.ok = true;
                result.warning = "Save could not be migrated and has been reset to defaults.";
                LustLog.Warn($"[LustHouse] Migration failed: {migrationError}");
                Backup();
                return result;
            }

            SaveDocument doc;
            try
            {
                // A malformed progressive block is dropped rather than allowed to fail the
                // whole load. The pots are the one part of the save that can be rebuilt
                // from the content (a missing key falls back to the tier's seed), so a
                // broken block must not cost the player their wallet or their level.
                if (root["progressive"] != null && root["progressive"].Type != JTokenType.Object)
                {
                    LustLog.Warn("[LustHouse] The progressive pot block was unreadable; " +
                                 "the pots fall back to their seeds.");
                    root["progressive"] = new JObject();
                }

                doc = root.ToObject<SaveDocument>();
            }
            catch (Exception e)
            {
                result.document = SaveDocument.CreateNew(DateTime.UtcNow);
                result.ok = true;
                result.warning = "Save shape was unreadable and has been reset to defaults.";
                LustLog.Warn($"[LustHouse] Deserialize failed: {e.Message}");
                Backup();
                return result;
            }

            if (doc == null)
            {
                result.document = SaveDocument.CreateNew(DateTime.UtcNow);
                result.ok = true;
                result.warning = "Save was empty and has been reset to defaults.";
                Backup();
                return result;
            }

            doc.schemaVersion = SaveDocument.CurrentSchemaVersion;
            doc.player = doc.player ?? new PlayerState();
            doc.wallet = doc.wallet ?? WalletState.CreateNew();
            doc.inventory = doc.inventory ?? new InventoryState();
            doc.settings = doc.settings ?? new SettingsState();
            doc.progressive = doc.progressive ?? new Dictionary<string, long>();
            result.document = doc;
            result.ok = true;
            return result;
        }

        public void Backup()
        {
            try
            {
                if (File.Exists(SavePath))
                    File.Copy(SavePath, SavePath + BackupSuffix, true);
            }
            catch (Exception e)
            {
                LustLog.Warn($"[LustHouse] Could not back up save: {e.Message}");
            }
        }

        /// <summary>Used by the Club screen's "delete account" action (plan §2.10).</summary>
        public void Delete()
        {
            try
            {
                Backup();
                if (File.Exists(SavePath)) File.Delete(SavePath);
            }
            catch (Exception e)
            {
                LustLog.Warn($"[LustHouse] Could not delete save: {e.Message}");
            }
        }
    }
}