using UnityEngine;
using System.IO;
using Newtonsoft.Json;

namespace LustHouse.Core
{
    public class SaveSystem
    {
        private static string SavePath => Path.Combine(Application.persistentDataPath, "save.json");

        public static void Save<T>(T data)
        {
            var json = JsonConvert.SerializeObject(data, Formatting.Indented);
            var tmp = SavePath + ".tmp";
            File.WriteAllText(tmp, json);
            if (File.Exists(SavePath)) File.Delete(SavePath);
            File.Move(tmp, SavePath);
        }

        public static T Load<T>() where T : class
        {
            if (!File.Exists(SavePath)) return null;
            try { return JsonConvert.DeserializeObject<T>(File.ReadAllText(SavePath)); }
            catch { return null; }
        }

        public static bool Exists() => File.Exists(SavePath);
    }
}