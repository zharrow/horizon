using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace Horizon
{
    [Serializable]
    public sealed class SaveData
    {
        public double[] cur = new double[6];
        [SerializeField] public Dictionary<string, int> levels = new Dictionary<string, int>();
        public int runs, maxEra, cycle, maxCycle, wins;
        public float bestTime;
        public bool sound = true, haptics = true;
    }

    public static class SaveStore
    {
        static string FilePath => Path.Combine(Application.persistentDataPath, "horizon-save.json");

        public static SaveData Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var s = JsonConvert.DeserializeObject<SaveData>(File.ReadAllText(FilePath));
                    if (s != null)
                    {
                        if (s.cur == null || s.cur.Length != 6) s.cur = new double[6];
                        if (s.levels == null) s.levels = new Dictionary<string, int>();
                        return s;
                    }
                }
            }
            catch (Exception e) { Debug.LogWarning("Sauvegarde illisible, on repart de zéro : " + e.Message); }
            return new SaveData();
        }

        public static void Persist(SaveData s)
        {
            try
            {
                // Écriture atomique : un crash pendant l'écriture ne corrompt pas la sauvegarde
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, JsonConvert.SerializeObject(s));
                if (File.Exists(FilePath)) File.Delete(FilePath);
                File.Move(tmp, FilePath);
            }
            catch (Exception e) { Debug.LogWarning("Sauvegarde impossible : " + e.Message); }
        }
    }
}
