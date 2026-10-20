using System;
using System.Collections.Generic;
using System.IO;
using LustHouse.Catalog;
using LustHouse.Core;
using LustHouse.Slots;
using UnityEditor;
using UnityEngine;

namespace LustHouse.EditorTools
{
    /// <summary>
    /// Creates a new slot game: the definition, the catalogue entry and the pack folder.
    ///
    /// A game is data, so nothing here is game logic — it is the paperwork that turns an
    /// idea into an entry in the lobby (plan §3.5, §3.7). The point of the wizard is that
    /// adding a game touches **no code at all**, not even a test file: the new definition
    /// declares itself unmeasured, and the RTP gate reads that field rather than a list of
    /// names.
    ///
    /// The maths source is the important choice. Copying an existing game inherits a
    /// proven model — Velvet Room's is tuned to 96.28% and hits its target — which is how
    /// a new theme ships before its own maths has been figured out. Starting from the
    /// template gives the generic strips and the placeholder paytable, which is a valid
    /// game but an untuned one.
    /// </summary>
    public class NewSlotGameWindow : EditorWindow
    {
        [MenuItem("LustHouse/Content/New slot game", priority = 42)]
        public static void Open() => GetWindow<NewSlotGameWindow>("New slot game");

        // ---- the form ------------------------------------------------------

        private string _id = "new_game";
        private string _title = "New Game";
        private SlotTemplate _template = SlotTemplate.Video5;
        private Volatility _volatility = Volatility.Medium;
        private int _reelCount = 5;
        private int _rowCount = 3;
        private int _faceSymbolId = 16;
        private Color _themeTint = new Color32(0x37, 0xE0, 0xD8, 0xFF);
        private int _mathsSourceIndex;      // 0 = the template, then the games on disk
        private string _message;
        private bool _created;

        private static readonly (SlotTemplate template, string label, int reels, int rows)[] Templates =
        {
            (SlotTemplate.Classic3,    "Classic 3-reel",  3, 3),
            (SlotTemplate.Video5,      "Video 5-reel",    5, 3),
            (SlotTemplate.Cascade,      "Cascade",        5, 3),
            (SlotTemplate.HoldAndSpin,  "Hold & Spin",    5, 3),
            (SlotTemplate.Progressive,   "Progressive",    5, 3)
        };

        private void OnEnable()
        {
            _reelCount = 5;
            _rowCount = 3;
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("New slot game", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "A game is data, not code. This writes the definition, the catalogue entry " +
                "and the pack folder, and leaves the RTP unmeasured — the gate reads that " +
                "from the asset, so nothing else has to be edited (plan §3.7).",
                MessageType.Info);

            _id = EditorGUILayout.TextField("Id (save-data key)", _id);
            _title = EditorGUILayout.TextField("Title", _title);

            int templateIndex = System.Array.FindIndex(Templates, t => t.template == _template);
            if (templateIndex < 0) templateIndex = 1;
            templateIndex = EditorGUILayout.Popup("Template", templateIndex,
                Labels(Templates, t => t.label));
            _template = Templates[templateIndex].template;

            _volatility = (Volatility)EditorGUILayout.EnumPopup("Volatility", _volatility);
            _themeTint = EditorGUILayout.ColorField("Theme tint", _themeTint);

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.IntField("Reels", Templates[templateIndex].reels);
                EditorGUILayout.IntField("Rows", Templates[templateIndex].rows);
            }

            _faceSymbolId = EditorGUILayout.IntField("Face symbol id", _faceSymbolId);

            var sources = MathsSources();
            _mathsSourceIndex = Mathf.Clamp(_mathsSourceIndex, 0, sources.Count - 1);
            _mathsSourceIndex = EditorGUILayout.Popup("Maths source", _mathsSourceIndex,
                Labels(sources, s => s.label));

            EditorGUILayout.Space();

            using (new EditorGUI.DisabledScope(_created))
            {
                if (GUILayout.Button("Create the game", GUILayout.Height(32)))
                    Create(sources);
            }

            if (_created && GUILayout.Button("Create another"))
            {
                _created = false;
                _message = null;
            }

            if (!string.IsNullOrEmpty(_message))
            {
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox(_message, MessageType.Info);
            }
        }

        // ---- the maths source ----------------------------------------------

        /// <summary>Labels for a popup. A loop, because the tuple arrays confuse inference.</summary>
        private static string[] Labels<T>(T[] items, Func<T, string> label)
        {
            var result = new string[items.Length];
            for (int i = 0; i < items.Length; i++) result[i] = label(items[i]);
            return result;
        }

        private static string[] Labels(List<Source> items, Func<Source, string> label)
        {
            var result = new string[items.Count];
            for (int i = 0; i < items.Count; i++) result[i] = label(items[i]);
            return result;
        }

        private struct Source
        {
            public string label;
            public SlotMachineDefinition definition;
        }

        private List<Source> MathsSources()
        {
            var sources = new List<Source> { new Source { label = "The generic template (untuned)", definition = null } };

            foreach (var guid in AssetDatabase.FindAssets("t:SlotMachineDefinition", new[] { ContentBootstrap.SlotFolder }))
            {
                var def = AssetDatabase.LoadAssetAtPath<SlotMachineDefinition>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (def == null) continue;

                string rtp = def.rtpStatus == RtpStatus.Measured ? "measured" : "unmeasured";
                sources.Add(new Source
                {
                    label = $"{def.title} ({def.id}) — {rtp}",
                    definition = def
                });
            }

            return sources;
        }

        // ---- creating ------------------------------------------------------

        private void Create(List<Source> sources)
        {
            _message = null;

            string id = (_id ?? "").Trim().ToLowerInvariant().Replace(' ', '_');
            if (string.IsNullOrEmpty(id))
            {
                _message = "The id cannot be empty.";
                return;
            }

            if (!System.Text.RegularExpressions.Regex.IsMatch(id, "^[a-z0-9_]+$"))
            {
                _message = "The id is the save-data key: lower case, digits and underscores only.";
                return;
            }

            string path = $"{ContentBootstrap.SlotFolder}/Slot_{id}.asset";
            if (AssetDatabase.LoadAssetAtPath<SlotMachineDefinition>(path) != null)
            {
                _message = $"A definition already exists at {path}.";
                return;
            }

            var catalog = ContentBootstrap.Catalog;
            if (catalog == null)
            {
                _message = "No game catalog found. Run LustHouse > Setup > Create starter content first.";
                return;
            }

            if (catalog.Find(id) != null)
            {
                _message = $"The catalog already has a game called '{id}'.";
                return;
            }

            int templateIndex = System.Array.FindIndex(Templates, t => t.template == _template);
            var template = Templates[Mathf.Max(0, templateIndex)];

            var source = sources[Mathf.Clamp(_mathsSourceIndex, 0, sources.Count - 1)].definition;

            var def = ContentBootstrap.BuildDefinition(
                id: id,
                title: string.IsNullOrWhiteSpace(_title) ? id : _title.Trim(),
                template: _template,
                volatility: _volatility,
                targetRtp: VolatilityTarget(_volatility),
                maxWinMultiplier: source != null ? source.maxWinMultiplier : DefaultMaxWin(_template),
                reelCount: template.reels,
                rowCount: template.rows,
                themeTint: _themeTint,
                faceSymbolId: _faceSymbolId,
                mathsSource: source);

            AssetDatabase.CreateAsset(def, path);
            AssetDatabase.SaveAssets();

            catalog.games.Add(new GameCatalog.Entry
            {
                id = id,
                displayTitle = def.title,
                definition = def,
                category = GameCategory.Slots,
                locked = false,
                unlockLevel = NextFreeUnlockLevel(catalog),
                releasedAtUtc = DateTime.UtcNow.ToString("o"),
                isFeatured = false,
                featuredOrder = 0,
                entryCostGems = 0
            });

            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();

            // The pack folder is where this game's own downloadable art goes. The folder
            // has to exist for the Addressables step to register it.
            string packFolder = $"{ContentBootstrap.ArtFolder}/{id}";
            if (!AssetDatabase.IsValidFolder(packFolder))
                AssetDatabase.CreateFolder(ContentBootstrap.ArtFolder, id);

            // Register the pack and the new key with the addressables group.
            ProjectSetup.MarkSlotPacksAddressable();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Selection.activeObject = def;
            EditorGUIUtility.PingObject(def);

            if (!def.IsValid(out string error))
            {
                _message = $"Created '{id}', but it does not validate: {error}\n\n" +
                           "Fix the definition before shipping it.";
                return;
            }

            _created = true;
            _message = $"Created '{id}'.\n\n" +
                       "The game is unmeasured: open it in the Math Simulator, run the " +
                       "simulation, and sign it off once it passes. Until then its target " +
                       "RTP is a design value, not a claim.";
        }

        /// <summary>The RTP band the volatility implies (plan §3.3). A design value.</summary>
        private static float VolatilityTarget(Volatility volatility) => volatility switch
        {
            Volatility.Low => 0.962f,
            Volatility.Medium => 0.955f,
            Volatility.High => 0.945f,
            _ => 0.955f
        };

        private static int DefaultMaxWin(SlotTemplate template) => template switch
        {
            SlotTemplate.Video5 => 2500,
            SlotTemplate.Progressive => 10000,
            SlotTemplate.Cascade => 10000,
            SlotTemplate.HoldAndSpin => 2500,
            _ => 500
        };

        /// <summary>The next level nobody has used yet, so new titles open in order.</summary>
        private static int NextFreeUnlockLevel(GameCatalog catalog)
        {
            int highest = 1;
            foreach (var entry in catalog.games)
                if (entry != null) highest = Mathf.Max(highest, entry.unlockLevel);
            return highest + 1;
        }
    }
}
