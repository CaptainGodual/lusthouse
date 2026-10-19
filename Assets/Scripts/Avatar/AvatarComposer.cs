using System;
using System.Collections.Generic;
using UnityEngine;
using LustHouse.Core;
using LustHouse.Economy;

namespace LustHouse.Avatar
{
    public class AvatarComposer : MonoBehaviour
    {
        public static AvatarComposer Instance { get; private set; }

        private readonly Dictionary<string, Sprite> _cache = new Dictionary<string, Sprite>();

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        public Sprite Compose(AvatarLoadout loadout, CosmeticDefinition[] catalog)
        {
            if (loadout == null) return null;
            string hash = loadout.baseId + "|" + loadout.hairId + "|" + loadout.outfitId +
                          "|" + loadout.accessoryId + "|" + loadout.emoteId;
            if (_cache.TryGetValue(hash, out var cached)) return cached;

            var baseDef = Find(catalog, loadout.baseId);

            var result = baseDef != null ? baseDef.previewSprite : null;
            _cache[hash] = result;
            return result;
        }

        private CosmeticDefinition Find(CosmeticDefinition[] catalog, string id)
        {
            if (catalog == null || string.IsNullOrEmpty(id)) return null;
            foreach (var d in catalog)
                if (d != null && d.id == id) return d;
            return null;
        }
    }
}