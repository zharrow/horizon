using System.Collections.Generic;
using UnityEngine;

namespace Horizon
{
    struct Stats
    {
        public float G, coreR, pullR, drain, spawn, dps, crit, gold, combo, accr, feed, anchor, shield;
        public double value;
        public int chain, planets, twin, pred;
        public bool nova;
    }

    public sealed partial class HorizonGame
    {
        // ---------- Progression ----------
        int Lv(string id) => save.levels.TryGetValue(id, out var l) ? l : 0;
        double Cost(Upgrade u) => System.Math.Ceiling(u.@base * System.Math.Pow(u.mult, Lv(u.id)));

        bool ReqMet(Upgrade u)
        {
            if (u.req.Length == 0) return true;
            foreach (var r in u.req) if (Lv(r) > 0) return true;
            return false;
        }

        bool EraOk(Upgrade u) => save.maxEra >= u.cur;
        bool IsOpen(Upgrade u) => ReqMet(u) && EraOk(u);

        bool IsTease(Upgrade u)
        {
            if (IsOpen(u)) return false;
            if (ReqMet(u)) return true;
            foreach (var r in u.req) if (IsOpen(Data.UpById[r])) return true;
            return false;
        }

        bool CanBuy(Upgrade u) => IsOpen(u) && Lv(u.id) < u.max && save.cur[u.cur] >= Cost(u);

        // Difficulté : l'ère 1 reste un festin, les dernières ères suivent la puissance du joueur
        float Diff(int e) => Mathf.Pow(1.9f, save.cycle * (e + 1) / 6f) * (1 + e * .35f);

        Stats S() => new Stats
        {
            G = 30000 * (1 + .15f * Lv("grav")),
            coreR = 16 * (1 + .07f * Lv("core")),
            pullR = 110 * (1 + .08f * Lv("reach")),
            drain = Mathf.Pow(.93f, Lv("time")),
            spawn = 5 * (1 + .1f * Lv("spawn")),
            dps = 10 * (1 + .25f * Lv("crush")),
            value = (1 + .15 * Lv("value")) * System.Math.Pow(1.5, Lv("crunch")) * (1 + .6 * save.cycle),
            crit = .03f * Lv("crit"),
            gold = .005f + .015f * Lv("gold"),
            combo = .01f + .03f * Lv("combo"),
            accr = 1 + .15f * Lv("grow"),
            feed = 1 + .12f * Lv("feed"),
            anchor = Mathf.Pow(.85f, Lv("anchor")),
            shield = Mathf.Pow(.85f, Lv("shield")),
            chain = Lv("chain"), planets = Lv("giants"), nova = Lv("nova") > 0, twin = Lv("twin"), pred = Lv("predator"),
        };

        float Prog() => Mathf.Min(1, run.prog / Data.Eras[run.era].need);
        float CoreEff() => st.coreR * (1 + 1.6f * (IsLive ? Prog() : 0));
        float PullEff() => st.pullR * (1 + .6f * (IsLive ? Prog() : 0));

        float Danger()
        {
            if (mode != Mode.Run || transT > 0) return 0;
            float a = run.e < .25f ? 1 - run.e / .25f * .6f : 0;
            return Mathf.Max(a, Mathf.Min(1, hurtT * 5));
        }

        float CamZ()
        {
            float z = camBase;
            if (transT > 0) { float k = 1 - transT / TRANS, e = 1 - Mathf.Pow(1 - k, 3); z *= 1 + (ZOOM - 1) * (1 - e); }
            return z;
        }

        void Wave(float x, float y, float amp, float speed, float life)
        {
            if (waves.Count >= 4) waves.RemoveAt(0);
            waves.Add(new ShockWave { x = x, y = y, amp = amp, speed = speed, life = life });
        }

        // ---------- Recyclage : pas d'allocation en jeu, donc pas de saccade du ramasse-miettes sur mobile ----------
        readonly Stack<Obj> objPool = new Stack<Obj>(400);
        readonly Stack<Part> partPool = new Stack<Part>(720);

        void ClearObjs()
        {
            foreach (var o in objs) objPool.Push(o);
            objs.Clear();
        }

        void RemoveObj(int i)
        {
            objPool.Push(objs[i]);
            objs.RemoveAt(i);
        }

        void AddPart(float x, float y, float vx, float vy, float life, Color32 col, float size, float ring = 0)
        {
            var p = partPool.Count > 0 ? partPool.Pop() : new Part();
            p.x = x; p.y = y; p.vx = vx; p.vy = vy; p.life = p.max = life; p.col = col; p.size = size; p.ring = ring;
            parts.Add(p);
        }

        // Les particules sont additives : l'ordre ne compte pas, on retire par échange avec la dernière
        void RemovePart(int i)
        {
            partPool.Push(parts[i]);
            int last = parts.Count - 1;
            parts[i] = parts[last];
            parts.RemoveAt(last);
        }

        // ---------- Objets ----------
        Obj AddObj(int tier, float x, float y, float vx, float vy, bool gold, int era = -1)
        {
            var T = Data.Tiers[tier];
            if (era < 0) era = run.era;
            var style = Data.Eras[era].style;
            float D = Diff(era);
            var o = objPool.Count > 0 ? objPool.Pop() : new Obj();
            o.tier = tier; o.era = era; o.x = x; o.y = y; o.vx = vx; o.vy = vy;
            o.r = T.r * (.85f + R() * .3f); o.hp = o.max = T.hp * D;
            o.gold = gold; o.warned = false; o.armor = 0; o.born = 0; o.hitT = 0;
            o.rot = R() * TAU; o.vr = (R() - .5f) * (style == Style.Galaxy || style == Style.System ? 1.2f : 2);
            o.sides = tier == 0 ? 4 : tier == 3 ? 14 : tier == 2 ? 5 + (int)(R() * 3) : 3 + (int)(R() * 3);
            o.seed = R() * TAU;
            if (era >= 1 && tier >= 1 && R() < (era == 1 ? .3f : .14f)) o.armor = 5 * D * (1 + tier * .5f);
            objs.Add(o);
            return o;
        }

        void Spawn(int forceTier = -1)
        {
            int tier = forceTier;
            if (tier < 0)
            {
                float pc = st.planets * .012f, r = R();
                tier = r < pc ? 3 : r < pc + .05f ? 2 : r < pc + .3f ? 1 : 0;
            }
            float x, y; int tries = 0;
            do { x = 20 + R() * (W - 40); y = 90 + R() * (H - 110); tries++; }
            while (tries < 10 && Dist(x - hole.x, y - hole.y) < PullEff() * .9f);
            AddObj(tier, x, y, (R() - .5f) * 24, (R() - .5f) * 24, R() < st.gold);
        }

        static float Dist(float dx, float dy) => Mathf.Sqrt(dx * dx + dy * dy);

        void Burst(float x, float y, Color32 col, int n, float spd)
        {
            for (int i = 0; i < n && parts.Count < 700; i++)
            {
                float a = R() * TAU, s = spd * (.3f + R()), life = .35f + R() * .45f;
                AddPart(x, y, Mathf.Cos(a) * s, Mathf.Sin(a) * s, life, col, 1 + R() * 2.2f);
            }
        }

        void Ring(float x, float y, float R, Color32 col) => AddPart(x, y, 0, 0, .4f, col, 0, R);

        void AddText(float x, float y, string txt, Color32 col, bool big)
        {
            if (texts.Count > 60) texts.RemoveAt(0);
            texts.Add(new FText { x = x, y = y, txt = txt, col = col, big = big, life = .9f, max = .9f });
        }

        static readonly Color32 Gold = Data.Hex("#ffd166"), White = Data.Hex("#ffffff"), Danger_ = Data.Hex("#ff5d73");
        static readonly Color32 Text_ = Data.Hex("#f1ecff"), Steel = Data.Hex("#cbd5e1"), Spark = Data.Hex("#e2e8f0"), Evap = Data.Hex("#eef2ff");
        static readonly Color32 RivalCol = Data.Hex("#f472b6");

        Color32 ColOf(Obj o) => o.gold ? Gold : Data.Eras[o.era].cols[o.tier];
        double ValOf(Obj o) => Data.Tiers[o.tier].val * st.value * (o.gold ? 15 : 1);

        // Dégâts du noyau, avec blindage
        void HurtObj(Obj o, float dps, float dt, bool live)
        {
            float eff = dps;
            if (o.armor > 0)
            {
                eff = Mathf.Max(dps * .08f, dps - o.armor);
                if (live && eff < dps * .5f)
                {
                    if (!o.warned) { o.warned = true; AddText(o.x, o.y - o.r - 6, "Blindé", Steel, false); }
                    if (R() < .4f) Burst(o.x, o.y, Spark, 1, 120);
                }
            }
            o.hp -= eff * dt; o.hitT = .06f;
        }

        void Kill(Obj o, bool live)
        {
            var col = ColOf(o);
            Burst(o.x, o.y, col, 5 + o.tier * 12, 60 + o.tier * 70);
            if (o.tier > 0)
            {
                int n = 2 + (int)(R() * 2) + (o.tier > 1 ? 1 : 0);
                for (int k = 0; k < n; k++)
                {
                    float a = R() * TAU, s = 90 + R() * 130;
                    var f = AddObj(o.tier - 1, o.x, o.y, Mathf.Cos(a) * s, Mathf.Sin(a) * s, o.gold && R() < .5f, o.era);
                    f.born = 1; f.armor = 0;
                }
            }
            if (!live) return;
            run.kills++; run.combo++; run.comboT = .9f; run.maxCombo = Mathf.Max(run.maxCombo, run.combo);
            double v = ValOf(o) * (1 + run.combo * st.combo);
            bool crit = R() < st.crit;
            if (crit) { v *= 5; run.crits++; }
            run.gain[o.era] += v;
            float tv = Data.Tiers[o.tier].val;
            if (mode == Mode.Run) run.e = Mathf.Min(1, run.e + tv * .022f * st.feed * (o.gold ? 3 : 1) * (o.armor > 0 ? 2 : 1));
            if (o.era == run.era && transT <= 0) run.prog += tv * st.accr * (o.gold ? 3 : 1) * (o.armor > 0 ? 2 : 1);
            if (o.tier > 0 || crit || o.gold || R() < .3f)
                AddText(o.x, o.y - o.r - 4, (crit ? "CRIT +" : "+") + Data.Fmt(v), crit ? Danger_ : o.gold ? Gold : Text_, crit || o.gold || o.tier >= 2);
            shake = Mathf.Min(18, shake + o.tier * 1.6f + (crit ? 5 : 0) + (o.gold ? 3 : 0) + (o.armor > 0 ? 3 : 0));
            if (o.tier >= 2) { hitstop = Mathf.Max(hitstop, .04f + .03f * o.tier); flash = Mathf.Max(flash, .06f + .04f * o.tier); Wave(o.x, o.y, .45f, 520, .55f); caAmt += .4f; }
            if (o.tier >= 3) slowT = Mathf.Max(slowT, .35f);
            if (crit) { Wave(o.x, o.y, .25f, 420, .4f); caAmt += .5f; }
            if (o.gold) caAmt += .3f;
            if (crit || o.tier >= 3) Haptics.Play(Haptic.Medium);
            else if (o.tier >= 2 || o.gold) Haptics.Play(Haptic.Light);
            if (st.chain > 0 && o.tier > 0)
            {
                float Rr = 60 + o.tier * 28;
                foreach (var p in objs)
                {
                    float dx = p.x - o.x, dy = p.y - o.y, d = Dist(dx, dy);
                    if (d > 0 && d < Rr) { p.hp -= p.max * .3f * st.chain; p.hitT = .1f; float f = (1 - d / Rr) * 220; p.vx += dx / d * f; p.vy += dy / d * f; }
                }
                Ring(o.x, o.y, Rr, col); Wave(o.x, o.y, .25f, 380, .4f);
            }
            sfx.Absorb(run.combo, o.tier, crit, o.gold);
        }

        // ---------- Menaces ----------
        static Color32 TCol(ThreatType t) => t switch
        {
            ThreatType.Repel => Data.Hex("#60a5fa"),
            ThreatType.Pulsar => Data.Hex("#a5f3fc"),
            ThreatType.Rival => Data.Hex("#f472b6"),
            _ => Data.Hex("#e0f2fe"),
        };

        static string TName(ThreatType t) => t switch
        {
            ThreatType.Repel => "Géante gazeuse dévorée",
            ThreatType.Pulsar => "Pulsar éteint",
            ThreatType.Rival => "Rival avalé",
            _ => "Trou blanc refermé",
        };

        int ThreatCount()
        {
            int c = save.cycle;
            switch (Data.Eras[run.era].threat)
            {
                case ThreatType.Repel: return 1 + Mathf.CeilToInt(c / 2f);
                case ThreatType.Pulsar: return 1 + c / 2;
                case ThreatType.Rival: return 1 + (c >= 3 ? 1 : 0);
                case ThreatType.White: return 1 + c / 2;
                default: return 0;
            }
        }

        void SpawnThreat(ThreatType type)
        {
            if (type == ThreatType.None || type == ThreatType.Armor) return;
            float x, y; int tries = 0;
            do { x = 60 + R() * (W - 120); y = 120 + R() * (H - 180); tries++; }
            while (tries < 25 && Dist(x - hole.x, y - hole.y) < 260);
            float D = Diff(run.era); int c = save.cycle;
            var t = new Threat { type = type, x = x, y = y, a = R() * TAU };
            switch (type)
            {
                case ThreatType.Repel: t.r = 24; t.hp = t.max = 120 * D; t.R = 160 + c * 10; t.push = 280 * (1 + .12f * c); break;
                case ThreatType.Pulsar: t.r = 11; t.hp = t.max = 160 * D; t.w = (.9f + .12f * c) * (R() < .5f ? 1 : -1); t.L = Mathf.Max(W, H) * 1.2f; break;
                case ThreatType.Rival: t.r = CoreEff() * (1.05f + .1f * c); break;
                case ThreatType.White: t.r = 18; t.hp = t.max = 380 * D; t.R = 230; t.push = 340 * (1 + .1f * c); t.cd = 1; break;
            }
            threats.Add(t);
        }

        void KillThreat(Threat t, int i)
        {
            threats.RemoveAt(i);
            double v = (t.type == ThreatType.Rival ? 120 : t.type == ThreatType.White ? 200 : 80) * st.value * (1 + run.combo * st.combo);
            run.gain[run.era] += v; run.e = Mathf.Min(1, run.e + .45f); run.prog += Data.Eras[run.era].need * .12f; run.tk++;
            AddText(t.x, t.y - 30, "+" + Data.Fmt(v), Gold, true);
            AddText(t.x, t.y - 58, TName(t.type), Text_, true);
            Burst(t.x, t.y, TCol(t.type), 60, 340);
            Wave(t.x, t.y, .9f, 700, .8f); shake = 16; hitstop = .08f; slowT = .35f; caAmt += 1; flash = .2f;
            sfx.Slay(); threatCD = 6;
            Haptics.Play(Haptic.Heavy);
        }

        bool Edible(Threat t) => t.r <= CoreEff() * (1 + .15f * st.pred);

        void UpdateThreats(float dt)
        {
            int want = ThreatCount();
            if (threats.Count < want) { threatCD -= dt; if (threatCD <= 0) { SpawnThreat(Data.Eras[run.era].threat); threatCD = 6; } }
            float core = CoreEff(); int c = save.cycle;
            for (int i = threats.Count - 1; i >= 0; i--)
            {
                var t = threats[i];
                t.t += dt; t.born = Mathf.Min(1, t.born + dt * 2); t.hitT -= dt;
                float dx = hole.x - t.x, dy = hole.y - t.y, d = Dist(dx, dy); if (d == 0) d = 1;

                if (t.type == ThreatType.Repel || t.type == ThreatType.White)
                {
                    if (d < t.R) { float f = t.push * (1 - d / t.R) * st.anchor; hole.ox += dx / d * f * dt * 4; hole.oy += dy / d * f * dt * 4; }
                    foreach (var o in objs)
                    {
                        float ex = o.x - t.x, ey = o.y - t.y, ed = Dist(ex, ey); if (ed == 0) ed = 1;
                        if (ed < t.R) { float f = t.push * 1.6f * (1 - ed / t.R) / Data.Tiers[o.tier].m; o.vx += ex / ed * f * dt; o.vy += ey / ed * f * dt; }
                    }
                    if (t.type == ThreatType.White)
                    {
                        t.cd -= dt;
                        if (t.cd <= 0)
                        {
                            t.cd = 1.1f;
                            for (int k = 0; k < 3; k++)
                            {
                                float a = R() * TAU;
                                var o = AddObj(R() < .2f ? 1 : 0, t.x + Mathf.Cos(a) * t.r, t.y + Mathf.Sin(a) * t.r, Mathf.Cos(a) * 220, Mathf.Sin(a) * 220, false);
                                o.born = .5f;
                            }
                        }
                    }
                }
                if (t.type == ThreatType.Pulsar)
                {
                    t.a += t.w * dt;
                    float dl = ((Mathf.Atan2(dy, dx) - t.a) % Mathf.PI + Mathf.PI) % Mathf.PI;
                    dl = Mathf.Min(dl, Mathf.PI - dl);
                    if (dl < Mathf.Atan2(core, d) + .02f)
                    {
                        run.e -= dt * .4f * st.shield * (1 + .15f * c); hurtT = .2f;
                        if (Time.time - lastZap > .09f) { lastZap = Time.time; sfx.Zap(); Burst(hole.x, hole.y, PulsarCol, 2, 160); Haptics.Play(Haptic.Light); }
                    }
                }
                if (t.type == ThreatType.Rival)
                {
                    Obj best = null; float bd = 1e9f;
                    foreach (var o in objs) { float q = Dist(o.x - t.x, o.y - t.y); if (q < bd) { bd = q; best = o; } }
                    float gx = best != null ? best.x : W / 2, gy = best != null ? best.y : H / 2;
                    bool big = !Edible(t);
                    if (!big && d < 300) { gx = t.x - dx; gy = t.y - dy; }      // il fuit quand tu es plus gros
                    else if (big && d < 380) { gx = hole.x; gy = hole.y; }     // il te chasse quand il est plus gros
                    float ax = gx - t.x, ay = gy - t.y, al = Dist(ax, ay); if (al == 0) al = 1;
                    float spd = 95 * (1 + .1f * c), kk = Mathf.Min(1, dt * 2);
                    t.vx += (ax / al * spd - t.vx) * kk; t.vy += (ay / al * spd - t.vy) * kk;
                    t.x = Mathf.Clamp(t.x + t.vx * dt, 20, W - 20); t.y = Mathf.Clamp(t.y + t.vy * dt, 90, H - 20);
                    for (int j = objs.Count - 1; j >= 0; j--)
                    {
                        var o = objs[j];
                        float ex = t.x - o.x, ey = t.y - o.y, ed = Dist(ex, ey); if (ed == 0) ed = 1;
                        if (ed < t.r * 4) { float f = 9000 / (ed + 30) / Data.Tiers[o.tier].m; o.vx += ex / ed * f * dt; o.vy += ey / ed * f * dt; }
                        if (ed < t.r + o.r * .5f) { RemoveObj(j); t.r = Mathf.Min(core * 2.4f, t.r + .1f * (o.tier + 1)); Burst(o.x, o.y, RivalCol, 3, 50); }
                    }
                    if (d < core + t.r * .8f)
                    {
                        if (!big) { KillThreat(t, i); continue; }
                        run.e -= dt * .7f; hurtT = .2f; t.r = Mathf.Min(core * 2.4f, t.r + dt * 2);
                        if (Time.time - lastZap > .12f) { lastZap = Time.time; Haptics.Play(Haptic.Light); }
                    }
                    continue;
                }
                if (d < core + t.r)
                {
                    t.hp -= st.dps * dt; t.hitT = .06f;
                    if (R() < dt * 30) Burst(t.x, t.y, White, 1, 60);
                    if (t.hp <= 0) { KillThreat(t, i); continue; }
                }
            }
        }

        // ---------- Changement d'échelle ----------
        void SwallowAll()
        {
            foreach (var o in objs)
            {
                run.gain[o.era] += ValOf(o);
                float dx = hole.x - o.x, dy = hole.y - o.y;
                for (int k = 0; k < 3 && parts.Count < 700; k++)
                    AddPart(o.x, o.y, dx * 2.4f, dy * 2.4f, .3f + R() * .4f, ColOf(o), 1.5f + R() * 2);
            }
            ClearObjs(); threats.Clear();
        }

        void EraUp()
        {
            SwallowAll();
            run.era++; run.prog = 0; run.e = 1;
            transT = TRANS; hitstop = .12f; flash = .5f; shake = 18; slowT = .6f; caAmt = 1.6f;
            Wave(hole.x, hole.y, 1.3f, 950, 1.3f);
            for (int i = 0; i < 36; i++) Spawn();
            int n = ThreatCount();
            for (int i = 0; i < n; i++) SpawnThreat(Data.Eras[run.era].threat);
            threatCD = 6;
            var E = Data.Eras[run.era];
            string warn = E.threat switch
            {
                ThreatType.Armor => "Attention aux blindages",
                ThreatType.Repel => "Les géantes gazeuses te repoussent",
                ThreatType.Pulsar => "Évite les faisceaux des pulsars",
                ThreatType.Rival => "Un trou noir rival chasse ici",
                ThreatType.White => "Les trous blancs crachent de la matière",
                _ => "Horizon restauré",
            };
            Banner($"Ère {run.era + 1} sur {Data.Eras.Length}{(save.cycle > 0 ? $", cycle {save.cycle + 1}" : "")}", E.name, warn);
            sfx.Era(run.era); sfx.DroneEra(run.era);
            Haptics.Play(Haptic.Success);
        }

        void Win()
        {
            SwallowAll(); won = true; mode = Mode.Win; winT = 2.4f;
            hitstop = .2f; flash = .6f; shake = 22; caAmt = 2; slowT = .8f;
            Wave(hole.x, hole.y, 1.6f, 1100, 1.6f);
            Banner($"Cycle {save.cycle + 1}", "Univers dévoré", "L'univers renaît, plus dense");
            sfx.Nova(); sfx.Big();
            Haptics.Play(Haptic.Success);
        }

        // ---------- Simulation ----------
        struct Well { public float x, y, core, pull, G; }
        readonly List<Well> wells = new List<Well>(2);

        void Step(float dt)
        {
            bool live = IsLive;
            float k = Mathf.Min(1, dt * 18);
            float od = Mathf.Pow(.15f, dt); hole.ox *= od; hole.oy *= od;
            hole.x += (hole.tx + hole.ox - hole.x) * k; hole.y += (hole.ty + hole.oy - hole.y) * k; hole.spin += dt; twinA += dt * 1.8f;
            hurtT = Mathf.Max(0, hurtT - dt);

            if (transT > 0) transT = Mathf.Max(0, transT - dt);
            camBase += ((mode == Mode.Menu ? 3 : mode == Mode.Win ? .15f : 1) - camBase) * Mathf.Min(1, dt * (mode == Mode.Win ? 1.1f : 2.5f));
            if (mode == Mode.Menu) { hole.tx = W / 2; hole.ty = H * .26f; }

            if (live)
            {
                if (mode == Mode.Run)
                {
                    if (transT <= 0) { run.el += dt; run.e -= dt * st.drain * (1 + .12f * run.era) * (1 + run.el / 60) * (1 + .08f * save.cycle) / 9; }
                    if (run.e < .35f && transT <= 0 && R() < dt * 50 * (1 - run.e / .35f))
                    {
                        float a = R() * TAU, r = CoreEff(), sp = 60 + R() * 120;
                        AddPart(hole.x + Mathf.Cos(a) * r, hole.y + Mathf.Sin(a) * r, Mathf.Cos(a) * sp, Mathf.Sin(a) * sp, .4f + R() * .4f, Evap, 1 + R() * 1.5f);
                    }
                    if (run.e <= 0)
                    {
                        run.e = 0; Burst(hole.x, hole.y, Evap, 50, 280); Wave(hole.x, hole.y, .8f, 600, .8f);
                        Haptics.Play(Haptic.Heavy);
                        if (st.nova) { mode = Mode.Nova; novaT = 1.6f; flash = .35f; shake = 14; caAmt = 1.2f; Wave(hole.x, hole.y, 1, 700, 1); sfx.Nova(); }
                        else { flash = .45f; EndRun(); return; }
                    }
                    if (transT <= 0)
                    {
                        run.spawnAcc += dt * st.spawn;
                        while (run.spawnAcc >= 1) { run.spawnAcc--; if (objs.Count < 350) Spawn(); }
                        UpdateThreats(dt);
                        if (run.prog >= Data.Eras[run.era].need) { if (run.era == Data.Eras.Length - 1) Win(); else EraUp(); }
                    }
                }
                else if (mode == Mode.Nova)
                {
                    novaT -= dt; if (novaT <= 0 || objs.Count == 0) { EndRun(); return; }
                }
                else
                {
                    winT -= dt; flash = Mathf.Max(flash, Mathf.Max(0, 1 - winT / 2.4f) * .7f); if (winT <= 0) { EndRun(); return; }
                }
                run.comboT -= dt; if (run.comboT <= 0) run.combo = 0;
            }
            else
            {
                idleAcc += dt * 3;
                while (idleAcc >= 1) { idleAcc--; if (mode != Mode.Menu && objs.Count < 45) Spawn(0); }
            }

            var E = Data.Eras[run.era];
            float bk = Mathf.Min(1, dt * 1.5f);
            bgC += (E.tint - bgC) * bk; nebA += (E.nebA - nebA) * bk; nebB += (E.nebB - nebB) * bk;
            diskPhase += dt;
            DiskUpdate(disk, dt); if (st.twin > 0) DiskUpdate(disk2, dt); if (threats.Count > 0) DiskUpdate(diskR, dt);
            for (int i = waves.Count - 1; i >= 0; i--) { waves[i].t += dt; if (waves[i].t >= waves[i].life) waves.RemoveAt(i); }

            bool nova = mode == Mode.Nova;
            float coreR = CoreEff(), pullR = PullEff();
            wells.Clear();
            wells.Add(new Well { x = hole.x, y = hole.y, core = nova ? coreR * 1.6f : coreR, pull = nova ? 1e5f : pullR, G = nova ? st.G * 8 : st.G });
            if (st.twin > 0)
            {
                float d = coreR + 50 + st.twin * 10;
                wells.Add(new Well { x = hole.x + Mathf.Cos(twinA) * d, y = hole.y + Mathf.Sin(twinA) * d, core = coreR * (.45f + .1f * st.twin), pull = pullR * (.4f + .1f * st.twin), G = st.G * .6f });
            }

            for (int i = objs.Count - 1; i >= 0; i--)
            {
                if (i >= objs.Count) continue; // Kill() peut avoir modifié la liste
                var o = objs[i]; var T = Data.Tiers[o.tier];
                o.born = Mathf.Min(1, o.born + dt * 4); o.hitT -= dt;
                bool inField = false;
                foreach (var h in wells)
                {
                    float dx = h.x - o.x, dy = h.y - o.y, d = Dist(dx, dy); if (d == 0) d = 1;
                    if (d < h.pull)
                    {
                        inField = true;
                        float f = h.G / (d + 30) * (1 - .5f * d / Mathf.Min(h.pull, 2000)) / T.m;
                        float nx = dx / d, ny = dy / d;
                        o.vx += (nx - ny * .32f) * f * dt; o.vy += (ny + nx * .32f) * f * dt;
                        if (d < h.core + o.r * .6f)
                        {
                            HurtObj(o, st.dps * (nova ? 30 : 1), dt, live);
                            float s = Mathf.Pow(.02f, dt); o.vx *= s; o.vy *= s;
                            if (live && R() < dt * 25 * (o.tier + 1)) Burst(o.x, o.y, ColOf(o), 1, 40);
                        }
                    }
                }
                float damp = inField ? Mathf.Pow(.4f, dt) : Mathf.Pow(.9f, dt);
                o.vx *= damp; o.vy *= damp; o.x += o.vx * dt; o.y += o.vy * dt; o.rot += o.vr * dt;
                if (o.x < -140 || o.x > W + 140 || o.y < -140 || o.y > H + 140) { RemoveObj(i); continue; }
                if (o.hp <= 0) { objs.RemoveAt(i); Kill(o, live); objPool.Push(o); }
            }

            float pd = Mathf.Pow(.15f, dt);
            for (int i = parts.Count - 1; i >= 0; i--)
            {
                var p = parts[i]; p.life -= dt;
                if (p.life <= 0) { RemovePart(i); continue; }
                p.vx *= pd; p.vy *= pd; p.x += p.vx * dt; p.y += p.vy * dt;
            }
            for (int i = texts.Count - 1; i >= 0; i--)
            {
                var t = texts[i]; t.life -= dt; t.y -= 38 * dt * (t.life / t.max + .3f);
                if (t.life <= 0) texts.RemoveAt(i);
            }
        }
    }
}
