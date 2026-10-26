using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace Horizon
{
    public sealed partial class HorizonGame
    {
        static readonly Color Muted = new Color32(154, 147, 194, 255), TextC = new Color32(241, 236, 255, 255);
        static readonly Color LineC = new Color(167 / 255f, 139 / 255f, 250 / 255f, .26f), LineOn = new Color(167 / 255f, 139 / 255f, 250 / 255f, .75f);
        static readonly Color DangerC = new Color32(255, 93, 115, 255);

        GameObject uiGo;
        PanelSettings panel;
        VisualElement root, sceneEl, floatsEl, hud, barWrap, bar, ebar, eraEl, menuEl, summaryEl, treeEl, treeView, world, bankEl, cycEl, sChips, panelEl;
        Label hMass, hMassCur, hTime, hEra, hCombo, eraSmall, eraBig, eraEm, h1, sTitle, sGain, sCur, sBest, sEra, sKills, sCombo, sDur, cycLbl, pName, pLv, pDesc;
        Button playBtn, sBtn, buyBtn, goBtn, resetBtn, pauseBtn;
        VisualElement pauseEl;
        Label pSub;
        readonly List<Button> soundBtns = new List<Button>(), hapticBtns = new List<Button>();
        int hudSec = -1, hudEra = -1, hudCycle = -1, hudCombo = -1;
        Mode hudMode;
        FontAsset fD300, fD500, fD700, fB400, fB500, fB700;
        Texture2D barTex, veilTex, haloTex;
        readonly List<Label> floatPool = new List<Label>(64);

        VisualElement shownScreen;
        float screenFade = 1, comboAlpha, bannerT = -1, resetArm = -10;
        string hudKey = "";
        bool summaryReady;
        float summaryT = -1; double summaryTarget; string summaryBest;
        string sel = "grav";
        Vector2 pan;
        float ns = 64;

        // Animations de l'arbre
        readonly List<VisualElement> pulseHalos = new List<VisualElement>();
        VisualElement popNode; float popT = -1, shakeT = -1;

        struct Drag { public Vector2 start, pan0; public bool moved; public string id; public int pointer; }
        Drag? drag;

        // ---------- Construction ----------
        void InitUI()
        {
            var symbols = new List<FontAsset> { MakeFont("NotoSansSymbols2"), MakeFont("NotoSansMath"), MakeFont("NotoSansSymbols") };
            fB400 = MakeFont("DMSans-Regular", symbols); fB500 = MakeFont("DMSans-Medium", symbols); fB700 = MakeFont("DMSans-Bold", symbols);
            var displayFallback = new List<FontAsset> { fB500 };
            displayFallback.AddRange(symbols);
            fD300 = MakeFont("Unbounded-Light", displayFallback); fD500 = MakeFont("Unbounded-Medium", displayFallback); fD700 = MakeFont("Unbounded-Bold", displayFallback);

            barTex = Gradient(256, 1, (u, v) => Color.Lerp(new Color32(167, 139, 250, 255), new Color32(255, 157, 77, 255), u));
            veilTex = Gradient(1, 256, (u, v) => { float p = 1 - v; float a = p < .45f ? 0 : p < .85f ? Mathf.Lerp(0, .62f, (p - .45f) / .4f) : .62f; return new Color(11 / 255f, 9 / 255f, 34 / 255f, a); });
            haloTex = Gradient(64, 64, (u, v) => { float dd = new Vector2(u - .5f, v - .5f).magnitude * 2; float a = Mathf.Clamp01((1 - dd) / .45f); return new Color(1, 1, 1, a * a * (3 - 2 * a)); });

            panel = ScriptableObject.CreateInstance<PanelSettings>();
            panel.themeStyleSheet = Resources.Load<ThemeStyleSheet>("HorizonTheme");
            panel.scaleMode = PanelScaleMode.ConstantPixelSize;
            panel.scale = ppp;
            uiGo = new GameObject("UI");
            uiGo.SetActive(false);
            uiGo.transform.SetParent(transform, false);
            var doc = uiGo.AddComponent<UIDocument>();
            doc.panelSettings = panel;
            uiGo.SetActive(true);

            root = El("root", doc.rootVisualElement);
            root.styleSheets.Add(Resources.Load<StyleSheet>("Horizon"));
            SetFont(root, fB400);

            sceneEl = El("fill", root, "scene");
            floatsEl = El("fill", root); floatsEl.pickingMode = PickingMode.Ignore;

            // HUD
            hud = El(null, root, "hud"); hud.pickingMode = PickingMode.Ignore;
            barWrap = El("bar", hud); bar = El(null, barWrap, "bar"); bar.style.backgroundImage = barTex;
            var row = El("hudrow", hud);
            var mass = El("mass", row);
            hMass = Lbl("0", mass, "hMass", fD700); hMassCur = Lbl("", mass, "hMassCur", fB500);
            var hright = El("hudright", row);
            hTime = Lbl("0", hright, "hTime", fD500);
            pauseBtn = Btn("", hright, "pauseBtn", Pause);
            pauseBtn.RemoveFromClassList("btn"); pauseBtn.AddToClassList("pausebtn");
            El("pausebar", pauseBtn); El("pausebar", pauseBtn);
            var erow = El("erarow", hud);
            hEra = Lbl("Poussière", erow, "hEra");
            ebar = El(null, El("ebar", erow), "ebar");
            eraEl = El(null, root, "era");
            eraSmall = Lbl("", eraEl, null); eraSmall.AddToClassList("small");
            eraBig = Lbl("", eraEl, null, fD300); eraBig.AddToClassList("big");
            eraEm = Lbl("", eraEl, null, fD500); eraEm.AddToClassList("em");
            hCombo = Lbl("", root, "hCombo", fD500);
            foreach (var e in new[] { hud, eraEl, hCombo }) Ignore(e);
            pauseBtn.pickingMode = PickingMode.Position;

            // Conseils des premières parties
            var coachRow = El("coachrow", root); coachRow.pickingMode = PickingMode.Ignore;
            coachLbl = Lbl("", coachRow, "coach", fB500); coachLbl.pickingMode = PickingMode.Ignore;

            // Menu
            menuEl = El("screen", root, "menu");
            menuEl.style.backgroundImage = veilTex;
            var menuC = Content(menuEl, true);
            h1 = Lbl("HORIZON", menuC, null, fD300); h1.AddToClassList("h1");
            Lbl("Tu es un trou noir. Si tu arrêtes de manger, tu t'évapores.", menuC, null).AddToClassList("tag");
            playBtn = Btn(save.runs > 0 ? "Continuer" : "Commencer", menuC, "playBtn", OnPlay);
            Lbl(Application.isMobilePlatform ? "Glisse ton doigt pour attirer les débris." : "Déplace la souris pour attirer les débris.", menuC, null).AddToClassList("hint");
            Settings(menuC);

            // Pause
            pauseEl = El("screen", root, "pause");
            var pauseC = Content(pauseEl, false);
            Lbl("Pause", pauseC, "pTitle", fD300);
            pSub = Lbl("", pauseC, "pSub");
            Btn("Reprendre", pauseC, "resumeBtn", Resume);
            var quit = Btn("Terminer la run", pauseC, "quitBtn", Abandon); quit.AddToClassList("ghost"); SetFont(quit, fB500);
            Settings(pauseC);

            // Bilan
            summaryEl = El("screen", root, "summary");
            var sumC = Content(summaryEl, false);
            sTitle = Lbl("Évaporation", sumC, "sTitle", fD300);
            sGain = Lbl("+0", sumC, "sGain", fD700);
            sCur = Lbl("", sumC, "sCur");
            sChips = El("chips", sumC, "sChips");
            sBest = Lbl("", sumC, "sBest");
            var stats = El("stats", sumC);
            sEra = Stat(stats, "ère atteinte", fB700); sKills = Stat(stats, "débris", fD500);
            sCombo = Stat(stats, "combo max", fD500); sDur = Stat(stats, "survie", fD500);
            sBtn = Btn("Évoluer", sumC, "sBtn", OpenTree);

            // Arbre
            treeEl = El("screen", root, "tree");
            var thead = El("thead", treeEl);
            bankEl = El("chips", thead, "bank"); bankEl.AddToClassList("bank");
            var tright = El("tright", thead);
            cycEl = El("cyc", tright);
            Btn("−", cycEl, null, () => { if (save.cycle > 0) { save.cycle--; SaveStore.Persist(save); RenderTree(); sfx.Tick(500); } }).AddToClassList("ghost");
            cycLbl = Lbl("Cycle 1", cycEl, "cycLbl");
            Btn("+", cycEl, null, () => { if (save.cycle < save.maxCycle) { save.cycle++; SaveStore.Persist(save); RenderTree(); sfx.Tick(800); } }).AddToClassList("ghost");
            resetBtn = Btn("Réinitialiser", tright, "resetBtn", OnReset); resetBtn.AddToClassList("ghost"); SetFont(resetBtn, fB500);
            treeView = El(null, treeEl, "treeView");
            world = El(null, treeView, "world");
            world.generateVisualContent += DrawLinks;
            treeView.RegisterCallback<PointerDownEvent>(OnTreeDown);
            treeView.RegisterCallback<PointerMoveEvent>(OnTreeMove);
            treeView.RegisterCallback<PointerUpEvent>(OnTreeUp);
            treeView.RegisterCallback<PointerCancelEvent>(e => drag = null);
            panelEl = El(null, treeEl, "panel");
            var pin = El("pin", panelEl);
            var phead = El("phead", pin);
            pName = Lbl("", phead, "pName", fD500); pLv = Lbl("", phead, "pLv", fD500);
            pDesc = Lbl("", pin, "pDesc");
            var pact = El("pact", pin);
            buyBtn = Btn("Acheter", pact, "buyBtn", () => Buy(sel));
            goBtn = Btn("Lancer la run", pact, "goBtn", StartRun);

            if (finalRT != null) OnFinalImage(finalRT);
            ResizeUI();
            Show(menuEl);
        }

        FontAsset MakeFont(string file, List<FontAsset> fallbacks = null)
        {
            var fa = FontAsset.CreateFontAsset(Resources.Load<Font>("Fonts/" + file));
            if (fallbacks != null) fa.fallbackFontAssetTable = fallbacks;
            return fa;
        }

        // UI Toolkit transforme un rayon trop grand en ellipse : on donne le rayon exact d'un cercle
        static void Radius(VisualElement e, float r)
        {
            e.style.borderTopLeftRadius = r; e.style.borderTopRightRadius = r;
            e.style.borderBottomLeftRadius = r; e.style.borderBottomRightRadius = r;
        }

        static void SetFont(VisualElement e, FontAsset f) => e.style.unityFontDefinition = new StyleFontDefinition(FontDefinition.FromSDFFont(f));
        static void Ignore(VisualElement e) { e.pickingMode = PickingMode.Ignore; foreach (var c in e.Children()) Ignore(c); }

        static Texture2D Gradient(int w, int h, Func<float, float, Color> f)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    t.SetPixel(x, y, f(w > 1 ? x / (w - 1f) : 0, h > 1 ? y / (h - 1f) : 0));
            t.Apply();
            return t;
        }

        static VisualElement El(string cls, VisualElement parent, string name = null)
        {
            var e = new VisualElement();
            if (cls != null) e.AddToClassList(cls);
            if (name != null) e.name = name;
            parent?.Add(e);
            return e;
        }

        static Label Lbl(string text, VisualElement parent, string name, FontAsset font = null)
        {
            var l = new Label(text);
            if (name != null) l.name = name;
            if (font != null) SetFont(l, font);
            parent?.Add(l);
            return l;
        }

        Button Btn(string text, VisualElement parent, string name, Action onClick)
        {
            var b = new Button(onClick) { text = text };
            b.RemoveFromClassList(Button.ussClassName);
            b.AddToClassList("btn");
            if (name != null) b.name = name;
            SetFont(b, fB700);
            parent.Add(b);
            return b;
        }

        Label Stat(VisualElement parent, string label, FontAsset font)
        {
            var s = El("stat", parent);
            var v = Lbl("0", s, null, font); v.AddToClassList("stat-v");
            Lbl(label, s, null).AddToClassList("stat-l");
            return v;
        }

        VisualElement Chip(int i, double v, bool plus)
        {
            var c = El("chip", null);
            El("chip-dot", c).style.backgroundColor = (Color)Data.Cur[i].c;
            Lbl((plus ? "+" : "") + Data.Fmt(v), c, null, fD500).AddToClassList("chip-v");
            Lbl(Data.Cur[i].n, c, null).AddToClassList("chip-n");
            return c;
        }

        // Bloc de contenu d'un écran. S'il ne tient pas (fenêtre basse, paysage), il est réduit pour rester entier ;
        // ancré en bas pour le menu, au centre ailleurs.
        VisualElement Content(VisualElement screen, bool bottom)
        {
            var c = El("content", screen);
            c.style.transformOrigin = new TransformOrigin(Length.Percent(50), Length.Percent(bottom ? 100 : 50), 0);
            void Fit()
            {
                float aw = screen.contentRect.width, ah = screen.contentRect.height;
                float w = c.layout.width, h = c.layout.height, s = 1;
                if (h > 0 && ah > 0) s = Mathf.Min(s, ah / h);
                if (w > 0 && aw > 0) s = Mathf.Min(s, aw / w);
                c.style.scale = new Scale(new Vector3(s, s, 1));
            }
            screen.RegisterCallback<GeometryChangedEvent>(_ => Fit());
            c.RegisterCallback<GeometryChangedEvent>(_ => Fit());
            return c;
        }

        // ---------- Conseils des deux premières parties ----------
        Label coachLbl;
        bool coachOn;
        int coachMask;
        float coachT = -1;
        const float CoachLife = 4.6f;

        void Coach(int bit, string text)
        {
            if (!coachOn || (coachMask & bit) != 0 || (coachT >= 0 && coachT < CoachLife)) return;
            coachMask |= bit; coachLbl.text = text; coachT = 0;
        }

        void CoachTips()
        {
            if (mode != Mode.Run || transT > 0) return;
            if (run.el > .6f) Coach(1, Application.isMobilePlatform
                ? "Glisse ton doigt : le trou noir le suit et avale tout ce qui passe près de lui."
                : "Déplace la souris : le trou noir la suit et avale tout ce qui passe près de lui.");
            if (run.e < .55f) Coach(2, "La jauge du haut se vide sans cesse : continue de manger pour ne pas t'évaporer.");
            if (run.era == 0 && Prog() > .4f) Coach(4, "Remplis la barre dorée pour grandir et passer à l'ère suivante.");
            if (run.combo >= 5) Coach(8, "Enchaîne les absorptions sans t'arrêter : le combo multiplie tes gains.");
        }

        // Réglages son et vibrations (les vibrations n'existent que sur téléphone)
        void Settings(VisualElement parent)
        {
            var row = El("settings", parent);
            var s = Btn("", row, null, () => { save.sound = !save.sound; sfx.Muted = !save.sound; SaveStore.Persist(save); RefreshSettings(); });
            s.AddToClassList("ghost"); SetFont(s, fB500); soundBtns.Add(s);
            if (Application.isMobilePlatform)
            {
                var h = Btn("", row, null, () => { save.haptics = !save.haptics; Haptics.Enabled = save.haptics; SaveStore.Persist(save); RefreshSettings(); Haptics.Play(Haptic.Medium); });
                h.AddToClassList("ghost"); SetFont(h, fB500); hapticBtns.Add(h);
            }
            RefreshSettings();
        }

        void RefreshSettings()
        {
            foreach (var b in soundBtns) b.text = save.sound ? "Son : activé" : "Son : coupé";
            foreach (var b in hapticBtns) b.text = save.haptics ? "Vibrations : activées" : "Vibrations : coupées";
        }

        // Vrai si un point d'écran tombe sur un bouton de l'interface (le bouton pause pendant la run)
        bool OverButton(Vector2 screen)
        {
            if (root?.panel == null) return false;
            var e = root.panel.Pick(new Vector2(screen.x / ppp, (Screen.height - screen.y) / ppp));
            for (; e != null; e = e.parent) if (e is Button) return true;
            return false;
        }

        void OnFinalImage(RenderTexture rt)
        {
            if (sceneEl != null) sceneEl.style.backgroundImage = Background.FromRenderTexture(rt);
        }

        // ---------- Mise en page dépendante de l'écran ----------
        void ResizeUI()
        {
            if (root == null) return;
            panel.scale = ppp;
            var sa = Screen.safeArea;
            float top = (Screen.height - sa.yMax) / ppp, bottom = sa.yMin / ppp;
            hud.style.paddingTop = top + 14;
            hCombo.style.top = top + 88;
            coachLbl.parent.style.bottom = bottom + 28;
            foreach (var s in new[] { menuEl, summaryEl, pauseEl }) { s.style.paddingTop = top + 24; s.style.paddingBottom = bottom + 24; }
            menuEl.style.paddingBottom = bottom + H * .09f;
            treeEl.Q(className: "thead").style.paddingTop = top + 14;
            panelEl.style.paddingBottom = bottom + 16;

            float vw = W / 100;
            float h1Size = Mathf.Clamp(11 * vw, 40, 92);
            h1.style.fontSize = h1Size; h1.style.letterSpacing = h1Size * .28f; h1.style.marginLeft = h1Size * .28f;
            float eraSize = Mathf.Clamp(9 * vw, 34, 68);
            eraBig.style.fontSize = eraSize; eraBig.style.letterSpacing = eraSize * .14f;
            sGain.style.fontSize = Mathf.Clamp(14 * vw, 48, 90);
            ns = W <= 480 ? 52 : 64;
            if (mode == Mode.Tree) RenderTree();
        }

        // ---------- Écrans ----------
        void Show(VisualElement screen)
        {
            foreach (var s in new[] { menuEl, summaryEl, treeEl, pauseEl }) s.EnableInClassList("on", s == screen);
            hud.EnableInClassList("on", screen == null);
            pauseBtn.pickingMode = screen == null ? PickingMode.Position : PickingMode.Ignore;
            if (screen != null) { comboAlpha = 0; hCombo.style.opacity = 0; }
            shownScreen = screen;
            screenFade = 0;
            if (screen != null) screen.style.opacity = 0;
        }

        bool ButtonHasFocus() => root?.panel?.focusController?.focusedElement is Button;

        void Banner(string small, string big, string em)
        {
            eraSmall.text = small; eraBig.text = big; eraEm.text = em;
            bannerT = 0;
        }

        void OnPlay() { if (save.runs > 0) OpenTree(); else StartRun(); }

        void Pause()
        {
            if (mode != Mode.Run || paused) return;
            paused = true; sfx.Paused = true; shake = 0;
            pSub.text = Data.Eras[run.era].name + " · " + Data.Mmss(run.el);
            Show(pauseEl);
        }

        void Resume()
        {
            if (!paused) return;
            paused = false; sfx.Paused = false;
            Show(null);
        }

        // Terminer la run garde ce qui a été gagné, comme une évaporation
        void Abandon()
        {
            paused = false; sfx.Paused = false;
            EndRun();
        }

        void StartRun()
        {
            st = S(); mode = Mode.Run; paused = false; sfx.Paused = false;
            ClearObjs();
            for (int i = parts.Count - 1; i >= 0; i--) RemovePart(i);
            texts.Clear(); threats.Clear(); transT = 0; won = false;
            run = new RunState(); hudKey = ""; hudSec = hudEra = hudCombo = -1;
            lastSec = 99; hole.tx = W / 2; hole.ty = H / 2; hole.ox = hole.oy = 0;
            for (int i = 0; i < 40; i++) Spawn();
            save.runs++; SaveStore.Persist(save); Show(null); sfx.StartRun(); sfx.DroneStart();
            Haptics.Play(Haptic.Medium);
            coachOn = save.runs <= 2; coachMask = 0; coachT = -1;
        }

        void EndRun()
        {
            bool victory = won; mode = Mode.Summary; transT = 0; sfx.DroneStop(); threats.Clear(); won = false; paused = false;
            bool newEra = run.era > save.maxEra, newTime = run.el > save.bestTime;
            for (int i = 0; i < 6; i++) save.cur[i] += run.gain[i];
            save.maxEra = Mathf.Max(save.maxEra, run.era); save.bestTime = Mathf.Max(save.bestTime, run.el);
            string cyc = "";
            if (victory)
            {
                save.wins++;
                if (save.cycle >= save.maxCycle) { save.maxCycle = save.cycle + 1; cyc = $"Cycle {save.maxCycle + 1} débloqué : les ères avancées deviennent plus résistantes, les gains aussi."; }
                save.cycle = save.maxCycle;
            }
            SaveStore.Persist(save); st = S();
            hole.tx = W / 2; hole.ty = H / 2;
            int mi = run.era; while (mi > 0 && run.gain[mi] <= 0) mi--;
            sTitle.text = victory ? "Univers dévoré" : "Évaporation";
            sCur.text = Data.Cur[mi].n; sCur.style.color = (Color)Data.Cur[mi].c; sGain.style.color = (Color)Data.Cur[mi].c;
            sChips.Clear();
            for (int i = 0; i < 6; i++) if (i != mi && run.gain[i] > 0) sChips.Add(Chip(i, run.gain[i], true));
            sEra.text = Data.Eras[run.era].name;
            sKills.text = run.kills.ToString(); sCombo.text = run.maxCombo.ToString(); sDur.text = Data.Mmss(run.el);
            sBest.text = ""; sBtn.style.visibility = Visibility.Hidden; summaryReady = false;
            Show(summaryEl);
            summaryTarget = run.gain[mi]; summaryT = 0;
            summaryBest = victory ? (cyc != "" ? cyc : "Encore une fois, en plus dense.")
                : newEra ? $"Nouvelle ère découverte : {Data.Eras[run.era].name}. De nouvelles améliorations s'ouvrent."
                : newTime && save.runs > 1 ? "Nouveau record de survie : " + Data.Mmss(run.el) : "";
        }

        void OpenTree() { mode = Mode.Tree; hole.tx = W / 2; hole.ty = H / 2; Show(treeEl); RenderTree(); }

        void OnReset()
        {
            if (Time.unscaledTime - resetArm < 2.5f)
            {
                save = new SaveData(); SaveStore.Persist(save); sel = "grav"; pan = Vector2.zero;
                resetBtn.text = "Réinitialiser"; resetArm = -10; mode = Mode.Menu; st = S(); ClearObjs(); run = new RunState();
                playBtn.text = "Commencer"; RefreshSettings(); sfx.Muted = !save.sound; Haptics.Enabled = save.haptics; Show(menuEl);
            }
            else { resetArm = Time.unscaledTime; resetBtn.text = "Confirmer ?"; }
        }

        // ---------- Arbre ----------
        bool Visible(Upgrade u) => IsOpen(u) || IsTease(u);

        void RenderTree()
        {
            st = S();
            world.Clear(); pulseHalos.Clear();
            foreach (var u in Data.Up)
            {
                bool open = IsOpen(u), tease = IsTease(u);
                if (!open && !tease) continue;
                bool known = open || ReqMet(u);
                int L = Lv(u.id);
                float x = u.x * SP, y = u.y * SP, hs = ns + 48;

                bool can = open && L < u.max && CanBuy(u);
                if (can)
                {
                    var halo = El("node-halo", world);
                    halo.style.width = hs; halo.style.height = hs; halo.style.left = x - hs / 2; halo.style.top = y - hs / 2;
                    halo.style.backgroundImage = haloTex; halo.pickingMode = PickingMode.Ignore;
                    pulseHalos.Add(halo);
                }

                var n = El("node", world);
                n.userData = u.id;
                n.style.width = ns; n.style.height = ns; n.style.left = x - ns / 2; n.style.top = y - ns / 2;
                Radius(n, ns / 2);
                if (!open) n.AddToClassList("tease");
                else { if (L > 0) n.AddToClassList("own"); if (L >= u.max) n.AddToClassList("max"); else if (can) n.AddToClassList("can"); }
                if (u.id == sel) n.AddToClassList("sel");
                Radius(El("node-sel", n), ns / 2 + 8);
                var g = Lbl(known ? u.g : "?", n, null, fB400); g.AddToClassList("node-g"); g.style.fontSize = ns * .38f;
                if (open) Lbl($"{L}/{u.max}", n, null, fD500).AddToClassList("node-lv");
                Lbl(known ? u.name : "???", n, null).AddToClassList("node-nm");
                foreach (var c in n.Children()) c.pickingMode = PickingMode.Ignore;
            }
            world.MarkDirtyRepaint();
            ApplyPan(); RenderPanel();
            bankEl.Clear();
            for (int i = 0; i <= save.maxEra; i++) bankEl.Add(Chip(i, save.cur[i], false));
            cycEl.style.display = save.maxCycle > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            cycLbl.text = $"Cycle {save.cycle + 1}";
        }

        void DrawLinks(MeshGenerationContext ctx)
        {
            var p = ctx.painter2D;
            p.lineWidth = 2;
            foreach (var u in Data.Up)
            {
                if (!Visible(u)) continue;
                foreach (var r in u.req)
                {
                    var q = Data.UpById[r];
                    if (!Visible(q)) continue;
                    p.strokeColor = Lv(q.id) > 0 && Lv(u.id) > 0 ? LineOn : LineC;
                    p.BeginPath();
                    p.MoveTo(new Vector2(q.x * SP, q.y * SP));
                    p.LineTo(new Vector2(u.x * SP, u.y * SP));
                    p.Stroke();
                }
            }
        }

        void ApplyPan() => world.style.translate = new Translate(pan.x, pan.y);

        void RenderPanel()
        {
            var u = Data.UpById[sel]; int L = Lv(u.id);
            pName.text = u.name; pName.style.color = (Color)Data.Cur[u.cur].c;
            pLv.text = $"Niveau {L}/{u.max}";
            if (!EraOk(u))
            {
                pDesc.text = $"Se paie en {Data.Cur[u.cur].n}. Atteins l'ère {Data.Eras[u.cur].name} pour la débloquer.";
                buyBtn.text = "Verrouillé"; buyBtn.EnableInClassList("no", true); return;
            }
            pDesc.text = u.desc;
            if (L >= u.max) { buyBtn.text = "Niveau max"; buyBtn.EnableInClassList("no", true); }
            else { buyBtn.text = $"{Data.Fmt(Cost(u))} {Data.Cur[u.cur].n}"; buyBtn.EnableInClassList("no", !CanBuy(u)); }
        }

        void Buy(string id)
        {
            if (!Data.UpById.TryGetValue(id, out var u) || !IsOpen(u)) return;
            if (!CanBuy(u)) { sfx.No(); shakeT = 0; Haptics.Play(Haptic.Warning); return; }
            save.cur[u.cur] -= Cost(u); save.levels[id] = Lv(id) + 1; SaveStore.Persist(save); sfx.Buy();
            Haptics.Play(Haptic.Light);
            RenderTree();
            popNode = world.Query(className: "node").Where(e => (string)e.userData == id).First();
            popT = 0;
            shake = 4;
        }

        void OnNode(string id)
        {
            if (!Data.UpById.TryGetValue(id, out var u)) return;
            if (!IsOpen(u)) { if (ReqMet(u)) { sel = id; RenderTree(); } return; }
            if (sel == id) Buy(id); else { sel = id; RenderTree(); }
        }

        static string NodeId(IEventHandler target)
        {
            for (var e = target as VisualElement; e != null; e = e.parent)
                if (e.userData is string s) return s;
            return null;
        }

        void OnTreeDown(PointerDownEvent e)
        {
            drag = new Drag { start = e.position, pan0 = pan, id = NodeId(e.target), pointer = e.pointerId };
            treeView.CapturePointer(e.pointerId);
        }

        void OnTreeMove(PointerMoveEvent e)
        {
            if (drag == null) return;
            var dg = drag.Value;
            Vector2 dd = (Vector2)e.position - dg.start;
            if (!dg.moved && dd.magnitude > 6) dg.moved = true;
            if (dg.moved) { pan = dg.pan0 + dd; ApplyPan(); }
            drag = dg;
        }

        void OnTreeUp(PointerUpEvent e)
        {
            if (drag != null && !drag.Value.moved && drag.Value.id != null) OnNode(drag.Value.id);
            if (treeView.HasPointerCapture(e.pointerId)) treeView.ReleasePointer(e.pointerId);
            drag = null;
        }

        // ---------- Chaque frame ----------
        void UpdateUI(float real)
        {
            if (root == null) return;
            Hud();

            // Bandeau d'ère : apparition en zoom arrière, tenue, puis fondu (keyframes eraIn du prototype)
            if (bannerT >= 0)
            {
                bannerT += real;
                float k = bannerT / 2.3f, op, sc;
                if (k < .14f) { float e = Ease(k / .14f); op = e; sc = Mathf.Lerp(1.5f, 1, e); }
                else if (k < .72f) { float e = Ease((k - .14f) / .58f); op = 1; sc = Mathf.Lerp(1, .98f, e); }
                else { float e = Ease(Mathf.Min(1, (k - .72f) / .28f)); op = 1 - e; sc = Mathf.Lerp(.98f, .94f, e); }
                eraEl.style.opacity = op; eraEl.style.scale = new Scale(new Vector2(sc, sc));
                if (k >= 1) { bannerT = -1; eraEl.style.opacity = 0; }
            }

            hCombo.style.opacity = comboAlpha = Mathf.MoveTowards(comboAlpha, hCombo.userData is bool on && on && IsLive ? 1 : 0, real / .15f);

            if (shownScreen != null && screenFade < 1) { screenFade = Mathf.Min(1, screenFade + real / .35f); shownScreen.style.opacity = screenFade; }

            // Conseil : fondu d'entrée, tenue, fondu de sortie ; masqué hors partie et en pause
            if (coachT >= 0)
            {
                if (!IsLive || paused) coachT = CoachLife;
                else coachT += real;
                float op = coachT < .3f ? coachT / .3f : coachT < CoachLife - .4f ? 1 : Mathf.Max(0, (CoachLife - coachT) / .4f);
                coachLbl.style.opacity = op;
                if (coachT >= CoachLife) { coachT = -1; coachLbl.style.opacity = 0; }
            }

            // Compteur du bilan
            if (summaryT >= 0)
            {
                summaryT += real;
                float k = Mathf.Min(1, summaryT / .9f), e = 1 - Mathf.Pow(1 - k, 3);
                sGain.text = "+" + Data.Fmt(summaryTarget * e);
                if (k < 1) { if (R() < .45f) sfx.Tick(300 + e * 700); }
                else
                {
                    summaryT = -1; sfx.Big(); sBest.text = summaryBest;
                    sBtn.style.visibility = Visibility.Visible; summaryReady = true; sBtn.Focus();
                }
            }

            if (resetArm > 0 && Time.unscaledTime - resetArm > 2.5f) { resetArm = -10; resetBtn.text = "Réinitialiser"; }

            if (mode == Mode.Tree)
            {
                float pulse = .5f - .5f * Mathf.Cos(Time.unscaledTime * TAU / 1.8f);
                foreach (var h in pulseHalos) h.style.opacity = .5f * pulse;
                if (popT >= 0 && popNode != null)
                {
                    popT += real; float k = Mathf.Min(1, popT / .4f), s = 1 + .28f * Mathf.Sin(Mathf.PI * Mathf.Pow(k, .7f));
                    popNode.style.scale = new Scale(new Vector2(s, s));
                    if (k >= 1) { popT = -1; popNode.style.scale = new Scale(Vector2.one); }
                }
                if (shakeT >= 0)
                {
                    shakeT += real; float k = Mathf.Min(1, shakeT / .3f);
                    float x = k < .25f ? Mathf.Lerp(0, -6, k / .25f) : k < .75f ? Mathf.Lerp(-6, 6, (k - .25f) / .5f) : Mathf.Lerp(6, 0, (k - .75f) / .25f);
                    buyBtn.style.translate = new Translate(x, 0);
                    if (k >= 1) shakeT = -1;
                }
            }

            UpdateFloats();
        }

        static float Ease(float t) => 1 - Mathf.Pow(1 - Mathf.Clamp01(t), 2);

        void Hud()
        {
            if (!IsLive) return;
            CoachTips();
            bar.style.width = Length.Percent(Mathf.Clamp01(run.e) * 100);
            bool low = run.e < .25f;
            bar.style.backgroundImage = low ? null : barTex;
            bar.style.backgroundColor = low ? DangerC : Color.clear;
            string key = Data.Fmt(run.gain[run.era]) + run.era;
            if (key != hudKey)
            {
                hudKey = key;
                hMass.text = Data.Fmt(run.gain[run.era]);
                hMassCur.text = Data.Cur[run.era].n; hMassCur.style.color = (Color)Data.Cur[run.era].c;
            }
            // Les textes ne sont réécrits que lorsqu'ils changent : pas d'allocation ni de remise en page à chaque image
            int sec = Mathf.FloorToInt(run.el);
            if (sec != hudSec || mode != hudMode)
            {
                hudSec = sec; hudMode = mode;
                hTime.text = mode == Mode.Nova ? "Supernova" : mode == Mode.Win ? "Univers dévoré" : Data.Mmss(run.el);
            }
            if (run.era != hudEra || save.cycle != hudCycle)
            {
                hudEra = run.era; hudCycle = save.cycle;
                hEra.text = Data.Eras[run.era].name + (save.cycle > 0 ? $", cycle {save.cycle + 1}" : "");
            }
            ebar.style.width = Length.Percent(Prog() * 100);
            bool showCombo = run.combo >= 5;
            hCombo.userData = showCombo;
            if (showCombo && run.combo != hudCombo)
            {
                hudCombo = run.combo;
                hCombo.text = $"Combo ×{run.combo}, +{Mathf.RoundToInt(run.combo * st.combo * 100)} %";
            }
            int s = Mathf.FloorToInt(run.el * (2 + 3 * (1 - run.e / .25f)));
            if (mode == Mode.Run && run.e < .25f && transT <= 0 && s != lastSec) { lastSec = s; sfx.Beat(); Haptics.Play(Haptic.Light); }
        }

        // Textes flottants : dessinés au-dessus de l'image, positionnés avec le zoom et le tremblement de la caméra
        void UpdateFloats()
        {
            while (floatPool.Count < texts.Count)
            {
                var l = Lbl("", floatsEl, null, fD700);
                l.AddToClassList("float");
                l.pickingMode = PickingMode.Ignore;
                l.style.translate = new Translate(Length.Percent(-50), Length.Percent(-50));
                floatPool.Add(l);
            }
            float z = CamZ();
            for (int i = 0; i < floatPool.Count; i++)
            {
                var l = floatPool[i];
                if (i >= texts.Count) { if (l.style.display != DisplayStyle.None) l.style.display = DisplayStyle.None; continue; }
                var t = texts[i];
                float age = t.max - t.life, pop = age < .12f ? 1 + .7f * (1 - age / .12f) : 1, a = Mathf.Min(1, t.life / .3f);
                l.style.display = DisplayStyle.Flex;
                if (l.text != t.txt) l.text = t.txt;
                l.style.fontSize = (t.big ? 20 : 13) * pop;
                Color c = t.col; c.a = a; l.style.color = c;
                l.style.unityTextOutlineColor = new Color(7 / 255f, 6 / 255f, 26 / 255f, .75f * a);
                l.style.unityTextOutlineWidth = 1.2f;
                l.style.left = hole.x + (t.x - hole.x) * z + shX;
                l.style.top = hole.y + (t.y - hole.y) * z + shY;
            }
        }
    }
}
