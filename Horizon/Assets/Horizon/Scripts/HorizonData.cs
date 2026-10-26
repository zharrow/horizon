using System.Collections.Generic;
using UnityEngine;

namespace Horizon
{
    public struct Tier { public float r, hp, val, m; }

    public enum Style { Shard, Moon, Planet, Star, System, Galaxy }
    public enum ThreatType { None, Armor, Repel, Pulsar, Rival, White }

    public sealed class Era
    {
        public string name;
        public Style style;
        public Color32[] cols;
        public Vector3 tint;   // 0..255, comme le rgb() CSS
        public float need;
        public ThreatType threat;
        public Vector3 nebA, nebB;
    }

    public sealed class Currency { public string n; public Color32 c; }

    public sealed class Upgrade
    {
        public string id, g, name, desc;
        public int cur, max, x, y;
        public double @base, mult;
        public string[] req;
    }

    // Données du prototype, à l'identique. Elles migreront vers Remote Config à l'étape 2.
    public static class Data
    {
        public static Color32 Hex(string h)
        {
            ColorUtility.TryParseHtmlString(h, out var c);
            return c;
        }

        static Color32[] Cols(params string[] h)
        {
            var a = new Color32[h.Length];
            for (int i = 0; i < h.Length; i++) a[i] = Hex(h[i]);
            return a;
        }

        public static readonly Tier[] Tiers =
        {
            new Tier { r = 3.5f, hp = .4f, val = 1, m = 1 },
            new Tier { r = 8, hp = 4, val = 4, m = 1.6f },
            new Tier { r = 14, hp = 22, val = 15, m = 2.6f },
            new Tier { r = 26, hp = 140, val = 60, m = 5 },
        };

        public static readonly Era[] Eras =
        {
            new Era { name = "Poussière", style = Style.Shard, cols = Cols("#7dd3fc", "#a78bfa", "#f472b6", "#34d399"), tint = new Vector3(7, 6, 26), need = 40, threat = ThreatType.None, nebA = new Vector3(.36f, .2f, .72f), nebB = new Vector3(.08f, .42f, .6f) },
            new Era { name = "Lunes", style = Style.Moon, cols = Cols("#dbe2ee", "#b4bdd0", "#cfd6e4", "#98a3bb"), tint = new Vector3(9, 12, 30), need = 150, threat = ThreatType.Armor, nebA = new Vector3(.24f, .32f, .52f), nebB = new Vector3(.45f, .44f, .58f) },
            new Era { name = "Planètes", style = Style.Planet, cols = Cols("#5eead4", "#fb923c", "#60a5fa", "#f472b6"), tint = new Vector3(5, 16, 32), need = 350, threat = ThreatType.Repel, nebA = new Vector3(.05f, .45f, .46f), nebB = new Vector3(.62f, .3f, .1f) },
            new Era { name = "Étoiles", style = Style.Star, cols = Cols("#fde68a", "#fff1d6", "#93c5fd", "#fca5a5"), tint = new Vector3(22, 10, 30), need = 800, threat = ThreatType.Pulsar, nebA = new Vector3(.72f, .36f, .14f), nebB = new Vector3(.56f, .14f, .46f) },
            new Era { name = "Systèmes", style = Style.System, cols = Cols("#fcd34d", "#fdba74", "#a5b4fc", "#f0abfc"), tint = new Vector3(14, 8, 36), need = 1600, threat = ThreatType.Rival, nebA = new Vector3(.3f, .2f, .76f), nebB = new Vector3(.72f, .25f, .56f) },
            new Era { name = "Galaxies", style = Style.Galaxy, cols = Cols("#c4b5fd", "#f9a8d4", "#a5f3fc", "#fde68a"), tint = new Vector3(3, 3, 16), need = 3000, threat = ThreatType.White, nebA = new Vector3(.42f, .2f, .82f), nebB = new Vector3(.72f, .56f, .2f) },
        };

        public static readonly Currency[] Cur =
        {
            new Currency { n = "poussière", c = Hex("#7dd3fc") },
            new Currency { n = "régolithe", c = Hex("#cbd5e1") },
            new Currency { n = "gaz", c = Hex("#5eead4") },
            new Currency { n = "plasma", c = Hex("#fde68a") },
            new Currency { n = "antimatière", c = Hex("#f0abfc") },
            new Currency { n = "matière noire", c = Hex("#c4b5fd") },
        };

        static Upgrade U(string id, int cur, string g, string name, string desc, double b, double mult, int max, int x, int y, params string[] req)
            => new Upgrade { id = id, cur = cur, g = g, name = name, desc = desc, @base = b, mult = mult, max = max, x = x, y = y, req = req };

        public static readonly Upgrade[] Up =
        {
            U("grav", 0, "◉", "Gravité", "+15 % de force d'attraction.", 4, 1.45, 40, 0, 0),
            U("reach", 0, "◎", "Portée", "+8 % de rayon d'attraction.", 8, 1.5, 30, 1, 0, "grav"),
            U("core", 0, "●", "Horizon", "+7 % de taille du noyau.", 10, 1.5, 30, 0, -1, "grav"),
            U("time", 0, "⧗", "Stabilité", "Évaporation réduite de 7 %.", 10, 1.5, 30, -1, 0, "grav"),
            U("spawn", 0, "✦", "Densité", "+10 % de débris.", 15, 1.55, 25, 0, 1, "grav"),
            U("crush", 1, "✹", "Broyage", "+25 % de dégâts du noyau. Indispensable contre les blindages.", 5, 1.45, 40, 0, -2, "core"),
            U("value", 1, "◆", "Rendement", "+15 % de ressources gagnées.", 8, 1.5, 30, 1, 1, "spawn", "reach"),
            U("grow", 1, "⬤", "Accrétion", "+15 % de croissance : tu changes d'ère plus vite.", 10, 1.55, 20, -1, -1, "core", "time"),
            U("feed", 1, "❖", "Voracité", "+12 % de jauge rendue par absorption.", 12, 1.55, 20, -1, 1, "spawn", "time"),
            U("crit", 2, "✕", "Spaghettification", "+3 % de chance de critique (×5).", 6, 1.6, 15, 2, 0, "reach"),
            U("gold", 2, "★", "Étoiles dorées", "+1,5 % de débris dorés (×15).", 10, 1.65, 12, 1, -1, "core", "reach"),
            U("combo", 2, "≋", "Résonance", "+3 % de multiplicateur par absorption en chaîne.", 8, 1.6, 15, -2, 1, "feed"),
            U("anchor", 2, "⚓", "Ancrage", "Tu résistes 15 % mieux aux poussées.", 10, 1.6, 10, 0, 2, "spawn"),
            U("chain", 3, "✺", "Réaction en chaîne", "Les gros débris qui éclatent blessent leurs voisins.", 6, 1.8, 8, -1, 2, "anchor", "feed"),
            U("shield", 3, "⬡", "Bouclier thermique", "Les pulsars te brûlent 15 % moins.", 8, 1.7, 8, 1, 2, "anchor"),
            U("giants", 3, "♁", "Géants", "Des objets géants apparaissent. Lents, très rentables.", 10, 2.2, 5, -2, 0, "time"),
            U("twin", 4, "∞", "Trou noir binaire", "Un second trou noir orbite autour du tien.", 6, 2.5, 4, 2, -1, "crit", "gold"),
            U("predator", 4, "⊗", "Prédateur", "Tu peux avaler des rivaux 15 % plus gros.", 8, 2, 5, 2, 1, "shield"),
            U("nova", 5, "✴", "Supernova", "Quand tu t'évapores, tout l'écran est aspiré.", 5, 1, 1, 0, -3, "crush"),
            U("crunch", 5, "⊛", "Big Crunch", "Toutes les ressources ×1,5.", 8, 2.5, 6, -2, -1, "grow"),
        };

        public static readonly Dictionary<string, Upgrade> UpById = new Dictionary<string, Upgrade>();
        static Data() { foreach (var u in Up) UpById[u.id] = u; }

        // Format des nombres du prototype : virgule décimale, suffixes K, M, B…
        static readonly string[] Units = { "K", "M", "B", "T", "Qa", "Qi", "Sx", "Sp", "Oc", "No" };
        static readonly System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;

        public static string Fmt(double n)
        {
            if (n < 10) return (System.Math.Round(n * 10) / 10).ToString(Inv).Replace('.', ',');
            if (n < 1000) return System.Math.Floor(n).ToString(Inv);
            int i = -1;
            while (n >= 1000 && i < Units.Length - 1) { n /= 1000; i++; }
            string s = n < 10 ? n.ToString("F2", Inv) : n < 100 ? n.ToString("F1", Inv) : n.ToString("F0", Inv);
            return s.Replace('.', ',') + Units[i];
        }

        public static string Mmss(float t) => Mathf.FloorToInt(t / 60) + ":" + Mathf.FloorToInt(t % 60).ToString("00");
    }
}
