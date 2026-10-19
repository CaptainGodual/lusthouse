using System.Collections.Generic;
using LustHouse.Core;
using LustHouse.Economy;
using UnityEngine;

namespace LustHouse.Avatar
{
    /// <summary>
    /// The single avatar render path (plan §5.2). PlayerChip, the Club preview, and slot
    /// games all call this — there is no second renderer to drift.
    ///
    /// Layer z-order is fixed (plan §5.4): Base → Outfit → Hair → Accessory → Emote.
    /// Emotes are hidden from the static portrait, so the Club preview opts in.
    /// </summary>
    [DisallowMultipleComponent]
    public class AvatarComposer : MonoBehaviour
    {
        public static AvatarComposer Instance { get; private set; }

        /// <summary>Rendered bust is authored 3:4 at 768x1024, masked to a circle.</summary>
        [Header("Render settings")]
        public int textureWidth = 768;
        public int textureHeight = 1024;

        [Tooltip("Show the emote overlay. The static portrait hides it.")]
        public bool showEmote = false;

        private readonly Dictionary<string, Texture2D> _cache = new Dictionary<string, Texture2D>();

        /// <summary>id → definition. Populated from the cosmetic catalog by the bootstrap.</summary>
        private readonly Dictionary<string, CosmeticDefinition> _catalog =
            new Dictionary<string, CosmeticDefinition>();

        /// <summary>Fallback used when an owned id no longer resolves (catalog changed).</summary>
        private string _fallbackBaseId = "";

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void SetCatalog(IEnumerable<CosmeticDefinition> definitions, string fallbackBaseId = null)
        {
            _catalog.Clear();
            if (definitions != null)
            {
                foreach (var def in definitions)
                {
                    if (def == null || string.IsNullOrEmpty(def.id)) continue;
                    _catalog[def.id] = def;
                }
            }
            _fallbackBaseId = fallbackBaseId ?? "";
            _cache.Clear();
        }

        /// <summary>
        /// Composes the layered bust. Results are cached per loadout hash, so an unchanged
        /// avatar costs nothing to re-render in either the 40px chip or the 360px preview.
        /// </summary>
        public Texture2D Compose(AvatarLoadout loadout)
        {
            if (loadout == null) return null;

            string key = loadout.Hash() + (showEmote ? ":e" : ":s");
            if (_cache.TryGetValue(key, out var cached) && cached != null) return cached;

            var texture = Render(loadout);
            _cache[key] = texture;
            return texture;
        }

        private Texture2D Render(AvatarLoadout loadout)
        {
            var canvas = new Texture2D(textureWidth, textureHeight, TextureFormat.RGBA32, false);
            var pixels = new Color32[textureWidth * textureHeight];

            // Transparent start so a missing layer does not render as black.
            var transparent = new Color32(0, 0, 0, 0);
            for (int i = 0; i < pixels.Length; i++) pixels[i] = transparent;

            // Fixed z-order. Base first so it always sits under everything.
            Blend(pixels, Resolve(loadout.baseId, true));
            Blend(pixels, Resolve(loadout.outfitId, false));
            Blend(pixels, Resolve(loadout.hairId, false));
            Blend(pixels, Resolve(loadout.accessoryId, false));
            if (showEmote) Blend(pixels, Resolve(loadout.emoteId, false));

            canvas.SetPixels32(pixels);
            canvas.Apply();
            return canvas;
        }

        private void Blend(Color32[] destination, CosmeticDefinition def)
        {
            var sprite = def != null ? def.layerSprite : null;
            if (sprite == null || sprite.texture == null) return;

            var src = sprite.texture.GetPixels32();
            int sw = sprite.texture.width;
            int sh = sprite.texture.height;
            if (sw <= 0 || sh <= 0) return;

            var offset = def.layerOffset;
            var scale = def.layerScale;
            if (scale == Vector2.zero) scale = Vector2.one;

            float scaleX = scale.x <= 0 ? 1f : scale.x;
            float scaleY = scale.y <= 0 ? 1f : scale.y;

            int destW = Mathf.Max(1, Mathf.RoundToInt(sw * scaleX));
            int destH = Mathf.Max(1, Mathf.RoundToInt(sh * scaleY));

            // Centre the scaled layer, then apply the authored offset.
            int originX = (textureWidth - destW) / 2 + Mathf.RoundToInt(offset.x);
            int originY = (textureHeight - destH) / 2 + Mathf.RoundToInt(offset.y);

            for (int y = 0; y < destH; y++)
            {
                int dy = originY + y;
                if (dy < 0 || dy >= textureHeight) continue;

                int sy = Mathf.Clamp(Mathf.FloorToInt(y / scaleY), 0, sh - 1);

                for (int x = 0; x < destW; x++)
                {
                    int dx = originX + x;
                    if (dx < 0 || dx >= textureWidth) continue;

                    int sx = Mathf.Clamp(Mathf.FloorToInt(x / scaleX), 0, sw - 1);

                    var c = src[sy * sw + sx];
                    if (c.a == 0) continue;

                    int di = dy * textureWidth + dx;
                    destination[di] = c.a == 255 ? c : Over(destination[di], c);
                }
            }
        }

        /// <summary>Standard source-over alpha blend.</summary>
        private static Color32 Over(Color32 dst, Color32 src)
        {
            float sa = src.a / 255f;
            float da = dst.a / 255f;
            float outA = sa + da * (1f - sa);
            if (outA <= 0f) return new Color32(0, 0, 0, 0);

            float r = (src.r * sa + dst.r * da * (1f - sa)) / outA;
            float g = (src.g * sa + dst.g * da * (1f - sa)) / outA;
            float b = (src.b * sa + dst.b * da * (1f - sa)) / outA;

            return new Color32(
                (byte)Mathf.Clamp(Mathf.RoundToInt(r), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(g), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(b), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(outA * 255f), 0, 255));
        }

        /// <summary>
        /// Resolves an owned id to a definition. An id that no longer exists falls back to
        /// the starter Base so a catalog change can never render a blank avatar (plan §5.3).
        /// </summary>
        private CosmeticDefinition Resolve(string id, bool isBase)
        {
            if (!string.IsNullOrEmpty(id) && _catalog.TryGetValue(id, out var found) && found != null)
                return found;

            if (isBase && !string.IsNullOrEmpty(_fallbackBaseId) &&
                _catalog.TryGetValue(_fallbackBaseId, out var fallback))
                return fallback;

            return null;
        }

        public void ClearCache() => _cache.Clear();

        /// <summary>Convenience for the top-bar PlayerChip, which owns no state of its own.</summary>
        public Texture2D ComposeFromInventory(InventoryService inventory)
        {
            return inventory == null ? null : Compose(inventory.GetLoadout());
        }
    }
}