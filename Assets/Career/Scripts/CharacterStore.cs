using System.IO;
using UnityEngine;

namespace Career
{
    /// <summary>
    /// The one saved character, as JSON. Written by the creation screen, read by the shot drill.
    /// A single slot for now - the full save (calendar, events) is still to come.
    /// </summary>
    public static class CharacterStore
    {
        public static string Path
        {
            get { return System.IO.Path.Combine(Application.persistentDataPath, "career", "character.json"); }
        }

        public static void Save(PlayerProfile p)
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
            File.WriteAllText(Path, JsonUtility.ToJson(p, true));
        }

        public static bool TryLoad(out PlayerProfile p)
        {
            p = null;
            if (!File.Exists(Path)) return false;
            try
            {
                p = JsonUtility.FromJson<PlayerProfile>(File.ReadAllText(Path));
                return p != null;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[CharacterStore] could not read " + Path + ": " + e.Message);
                return false;
            }
        }
    }
}
