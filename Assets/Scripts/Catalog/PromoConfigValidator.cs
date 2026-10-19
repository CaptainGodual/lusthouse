using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace LustHouse.Catalog
{
    public struct ConfigOverrideResult
    {
        public bool applied;
        public string warning;
        public int droppedUnknownGameIds;
        public int droppedMalformedEntries;
        public bool changedFeatured;
        public bool changedSlides;
    }

    /// <summary>
    /// Applies a remote JSON payload on top of the shipped <see cref="LobbyPromoConfig"/>.
    ///
    /// Two rules make this safe to fetch from the network at boot:
    ///
    ///  1. **Never blank the lobby.** A field that is absent or empty in the payload leaves
    ///     the local value alone. An override can only change things, never remove them.
    ///  2. **Never trust the payload.** Every id is checked against the catalog and every
    ///     enum is parsed defensively. Anything unrecognised is dropped with a warning and
    ///     the rest is still applied.
    ///
    /// Payload shape mirrors the asset:
    /// <code>
    /// { "featured": [ { "gameId": "velvet_room", "headline": "...", "order": 0 } ],
    ///   "slides":   [ { "kind": "Newest", "gameId": "neon_nights", "headline": "NEW" } ],
    ///   "carouselDwellSeconds": 8 }
    /// </code>
    /// Pure and static so it is testable without a network or an engine.
    /// </summary>
    public static class PromoConfigValidator
    {
        public static ConfigOverrideResult Apply(LobbyPromoConfig target, string json, ICollection<string> knownGameIds)
        {
            var result = new ConfigOverrideResult();

            if (target == null || string.IsNullOrWhiteSpace(json))
            {
                result.warning = "Empty config payload.";
                return result;
            }

            JObject root;
            try
            {
                root = JObject.Parse(json);
            }
            catch (Exception e)
            {
                // Rule 1: a parse failure changes nothing.
                result.warning = $"Config payload was not valid JSON ({e.Message}); local config kept.";
                return result;
            }

            var known = new HashSet<string>(knownGameIds ?? Array.Empty<string>());

            // ---- featured ------------------------------------------------------
            var featuredToken = root["featured"] as JArray;
            if (featuredToken != null && featuredToken.Count > 0)
            {
                var parsed = new List<FeaturedTile>();

                foreach (var token in featuredToken)
                {
                    var jo = token as JObject;
                    if (jo == null) { result.droppedMalformedEntries++; continue; }

                    string gameId = jo["gameId"]?.Value<string>();
                    if (string.IsNullOrWhiteSpace(gameId))
                    {
                        result.droppedMalformedEntries++;
                        continue;
                    }

                    if (known.Count > 0 && !known.Contains(gameId))
                    {
                        // A featured tile for a game that does not exist cannot be tapped.
                        result.droppedUnknownGameIds++;
                        continue;
                    }

                    parsed.Add(new FeaturedTile
                    {
                        gameId = gameId,
                        headline = jo["headline"]?.Value<string>() ?? string.Empty,
                        subline = jo["subline"]?.Value<string>() ?? string.Empty,
                        order = jo["order"]?.Value<int?>() ?? parsed.Count
                    });
                }

                if (parsed.Count > 0)
                {
                    target.featured = parsed;
                    result.changedFeatured = true;
                }
            }

            // ---- slides --------------------------------------------------------
            var slidesToken = root["slides"] as JArray;
            if (slidesToken != null && slidesToken.Count > 0)
            {
                var parsed = new List<PromoSlide>();

                foreach (var token in slidesToken)
                {
                    var jo = token as JObject;
                    if (jo == null) { result.droppedMalformedEntries++; continue; }

                    if (!Enum.TryParse(jo["kind"]?.Value<string>(), true, out PromoSlideKind kind))
                    {
                        result.droppedMalformedEntries++;
                        continue;
                    }

                    string gameId = jo["gameId"]?.Value<string>();
                    if (!string.IsNullOrWhiteSpace(gameId) && known.Count > 0 && !known.Contains(gameId))
                    {
                        result.droppedUnknownGameIds++;
                        continue;
                    }

                    parsed.Add(new PromoSlide
                    {
                        kind = kind,
                        gameId = gameId ?? string.Empty,
                        headline = jo["headline"]?.Value<string>() ?? string.Empty,
                        subline = jo["subline"]?.Value<string>() ?? string.Empty,
                        ctaLabel = jo["ctaLabel"]?.Value<string>() ?? "PLAY"
                    });
                }

                if (parsed.Count > 0)
                {
                    target.slides = parsed;
                    result.changedSlides = true;
                }
            }

            // ---- scalars -------------------------------------------------------
            float? dwell = root["carouselDwellSeconds"]?.Value<float?>();
            if (dwell.HasValue)
            {
                // Clamp rather than reject: an absurd value should not break the layout.
                target.carouselDwellSeconds = Clamp(dwell.Value, 2f, 30f);
            }

            int? window = root["newWindowDays"]?.Value<int?>();
            if (window.HasValue) target.newWindowDays = Clamp(window.Value, 1, 90);

            int? maxTiles = root["maxFeaturedTiles"]?.Value<int?>();
            if (maxTiles.HasValue) target.maxFeaturedTiles = Clamp(maxTiles.Value, 1, 8);

            result.applied = result.changedFeatured || result.changedSlides ||
                             dwell.HasValue || window.HasValue || maxTiles.HasValue;

            if (result.droppedUnknownGameIds > 0)
                result.warning = $"Config referenced {result.droppedUnknownGameIds} unknown game id(s); those entries were dropped.";

            return result;
        }

        /// <summary>Local clamp so this file stays engine-free and unit-testable.</summary>
        private static float Clamp(float v, float min, float max) =>
            v < min ? min : (v > max ? max : v);

        private static int Clamp(int v, int min, int max) =>
            v < min ? min : (v > max ? max : v);
    }
}