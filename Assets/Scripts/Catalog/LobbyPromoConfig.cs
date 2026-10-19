using System;
using System.Collections.Generic;
using UnityEngine;

namespace LustHouse.Catalog
{
    /// <summary>Which template a carousel slide uses (plan §2.6).</summary>
    public enum PromoSlideKind
    {
        DailyBonus,
        Newest,
        Jackpot,
        Seasonal,
        Custom
    }

    /// <summary>One large tile in the featured row (the mockup's boxes).</summary>
    [Serializable]
    public class FeaturedTile
    {
        [Tooltip("Catalog entry id. Unknown ids are dropped by the validator.")]
        public string gameId = "";

        [Tooltip("Shown as the tile label. Falls back to the catalog title when blank.")]
        public string headline = "";

        [TextArea(1, 2)]
        public string subline = "";

        [Tooltip("Lower sorts first. Ties break on catalog order.")]
        public int order = 0;

        /// <summary>A tile with no catalog match cannot be tapped and is skipped.</summary>
        public bool IsValid() => !string.IsNullOrWhiteSpace(gameId);
    }

    /// <summary>One carousel slide (plan §2.6: jackpot / newest / seasonal promo).</summary>
    [Serializable]
    public class PromoSlide
    {
        public PromoSlideKind kind = PromoSlideKind.Custom;

        [Tooltip("Catalog entry id this slide points at. Blank for DailyBonus / Seasonal.")]
        public string gameId = "";

        public string headline = "";
        public string subline = "";

        [Tooltip("Label on the call-to-action button.")]
        public string ctaLabel = "PLAY";

        public Color accent = Color.white;
    }

    /// <summary>
    /// Content configuration for the lobby's featured row and carousel.
    ///
    /// Shipped as an asset so content changes need no rebuild, and overridable at boot by
    /// a remote JSON payload with the same shape (plan §1.2 budgets 10% for a config
    /// fetch). The remote layer is a *partial override*: a bad or partial payload never
    /// blanks the lobby, it only fails to change it.
    /// </summary>
    [CreateAssetMenu(menuName = "LustHouse/Lobby Promo Config", fileName = "LobbyPromoConfig")]
    public class LobbyPromoConfig : ScriptableObject
    {
        [Header("Featured row")]
        [Tooltip("Max tiles drawn in the featured row. The mockup shows four.")]
        [Range(1, 8)]
        public int maxFeaturedTiles = 4;

        public List<FeaturedTile> featured = new List<FeaturedTile>();

        [Header("Carousel (plan §2.6)")]
        public List<PromoSlide> slides = new List<PromoSlide>();

        [Tooltip("Auto-advance dwell per slide, seconds. Plan §2.1 specifies 8000ms.")]
        [Range(2f, 30f)]
        public float carouselDwellSeconds = 8f;

        [Tooltip("Never auto-advance while a PLAY button is under the pointer (plan §2.6).")]
        public bool pauseCarouselOnHover = true;

        [Tooltip("Reduced Motion disables auto-advance entirely (plan §2.14).")]
        public bool respectReducedMotion = true;

        [Header("New-release window")]
        [Tooltip("A game counts as 'New' for this many days after its release date.")]
        [Range(1, 90)]
        public int newWindowDays = 14;

        [Header("Remote override")]
        public bool remoteOverrideEnabled = false;

        [Tooltip("JSON endpoint. Overlaid on top of this asset at boot; never replaces it.")]
        public string remoteConfigUrl = "";

        [Tooltip("Hard timeout. Boot must never block on the network (plan §1.3).")]
        [Range(1f, 10f)]
        public float remoteTimeoutSeconds = 3f;

        // ---- helpers ----------------------------------------------------------

        public static LobbyPromoConfig CreateDefault()
        {
            var config = CreateInstance<LobbyPromoConfig>();
            config.name = "LobbyPromoConfig";

            config.slides = new List<PromoSlide>
            {
                new PromoSlide
                {
                    kind = PromoSlideKind.DailyBonus,
                    headline = "D A I L Y   B O N U S",
                    subline = "Claim your free Coins every day",
                    ctaLabel = "CLAIM",
                    accent = new Color32(0xFF, 0x3D, 0x8B, 0xFF)
                }
            };

            return config;
        }

        /// <summary>Featured tiles in display order. Does not check the catalog.</summary>
        public List<FeaturedTile> FeaturedInOrder()
        {
            var ordered = new List<FeaturedTile>(featured);
            ordered.Sort((a, b) =>
            {
                if (a == null) return 1;
                if (b == null) return -1;
                return a.order.CompareTo(b.order);
            });
            return ordered;
        }

        /// <summary>A deep copy, so a remote override cannot mutate the shipped asset.</summary>
        public LobbyPromoConfig Clone()
        {
            var copy = CreateInstance<LobbyPromoConfig>();
            copy.maxFeaturedTiles = maxFeaturedTiles;
            copy.carouselDwellSeconds = carouselDwellSeconds;
            copy.pauseCarouselOnHover = pauseCarouselOnHover;
            copy.respectReducedMotion = respectReducedMotion;
            copy.newWindowDays = newWindowDays;
            copy.remoteOverrideEnabled = remoteOverrideEnabled;
            copy.remoteConfigUrl = remoteConfigUrl;
            copy.remoteTimeoutSeconds = remoteTimeoutSeconds;

            copy.featured = new List<FeaturedTile>();
            foreach (var tile in featured)
            {
                if (tile == null) continue;
                copy.featured.Add(new FeaturedTile
                {
                    gameId = tile.gameId,
                    headline = tile.headline,
                    subline = tile.subline,
                    order = tile.order
                });
            }

            copy.slides = new List<PromoSlide>();
            foreach (var slide in slides)
            {
                if (slide == null) continue;
                copy.slides.Add(new PromoSlide
                {
                    kind = slide.kind,
                    gameId = slide.gameId,
                    headline = slide.headline,
                    subline = slide.subline,
                    ctaLabel = slide.ctaLabel,
                    accent = slide.accent
                });
            }

            return copy;
        }
    }
}