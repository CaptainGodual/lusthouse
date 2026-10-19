using System;
using System.Collections.Generic;
using LustHouse.Economy;
using UnityEngine;

namespace LustHouse.Avatar
{
    /// <summary>
    /// Exactly one equipped item id per category (plan §5.3). <c>Base</c> is never empty —
    /// an account is granted a starter set on creation so the avatar cannot render blank.
    /// </summary>
    [Serializable]
    public class AvatarLoadout
    {
        public string baseId = "";
        public string hairId = "";
        public string outfitId = "";
        public string accessoryId = "";
        public string emoteId = "";

        public string ForCategory(CosmeticCategory c)
        {
            switch (c)
            {
                case CosmeticCategory.Base: return baseId;
                case CosmeticCategory.Hair: return hairId;
                case CosmeticCategory.Outfit: return outfitId;
                case CosmeticCategory.Accessory: return accessoryId;
                default: return emoteId;
            }
        }

        /// <summary>Stable cache key for the composed portrait.</summary>
        public string Hash() => string.Join("|", baseId, hairId, outfitId, accessoryId, emoteId);

        public AvatarLoadout Clone() => new AvatarLoadout
        {
            baseId = baseId,
            hairId = hairId,
            outfitId = outfitId,
            accessoryId = accessoryId,
            emoteId = emoteId
        };
    }
}