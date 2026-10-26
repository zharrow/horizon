using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Horizon
{
    public enum Mode { Menu, Run, Nova, Win, Summary, Tree }

    // Portage du prototype horizon.html. Le jeu est découpé en fichiers partiels :
    // HorizonGame (boucle, entrées), .Sim (simulation), .Render (dessin + post-traitement), .UI (interface).
    // Toutes les positions sont en points (l'équivalent des px CSS du prototype).
    [RequireComponent(typeof(Synth))]
    public sealed partial class HorizonGame : MonoBehaviour
    {
        const float TAU = Mathf.PI * 2, ZOOM = 2.6f, TRANS = 1.4f;
        static readonly System.Random rng = new System.Random();
        static float R() => (float)rng.NextDouble();

        sealed class Obj
        {
            public int tier, era, sides;
            public float x, y, vx, vy, r, hp, max, armor, born, rot, vr, hitT, seed;
            public bool warned, gold;
        }

        sealed class Part { public float x, y, vx, vy, life, max, size, ring; public Color32 col; }
        sealed class FText { public float x, y, life, max; public string txt; public Color32 col; public bool big; }

        sealed class Threat
        {
            public ThreatType type;
            public float x, y, a, t, vx, vy, born, hitT, hp = 1, max = 1, r, R, push, w, L, cd;
        }

        sealed class ShockWave { public float x, y, amp, speed, life, t; }

        sealed class RunState
        {
            public float e = 1, el, comboT, spawnAcc, prog;
            public double[] gain = new double[6];
            public int kills, combo, maxCombo, crits, era, tk;
        }

        sealed class Hole { public float x, y, tx, ty, ox, oy, spin; }

        // ---------- État ----------
        float W, H, ppp = 1, SP = 110;
        int screenW, screenH;
        SaveData save;
        Stats st;
        Mode mode = Mode.Menu;
        RunState run = new RunState();
        Synth sfx;

        List<Obj> objs = new List<Obj>(400);
        readonly List<Part> parts = new List<Part>(720);
        readonly List<FText> texts = new List<FText>(64);
        List<Threat> threats = new List<Threat>();
        readonly List<ShockWave> waves = new List<ShockWave>(4);
        readonly Hole hole = new Hole();

        float twinA, novaT, winT, transT, shake, hitstop, flash, idleAcc;
        int lastSec = 99;
        float threatCD, hurtT, lastZap = -1;
        bool won;
        Vector3 bgC = new Vector3(7, 6, 26), nebA = new Vector3(.36f, .2f, .72f), nebB = new Vector3(.08f, .42f, .6f);
        float caAmt, slowT, shX, shY, camBase = 1;
        bool paused;

        bool IsLive => mode == Mode.Run || mode == Mode.Nova || mode == Mode.Win;

        void Awake()
        {
            Application.targetFrameRate = 60;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            save = SaveStore.Load();
            st = S();
            sfx = GetComponent<Synth>();
            sfx.Muted = !save.sound;
            Haptics.Enabled = save.haptics;
            Resize();
            hole.x = hole.tx = W / 2; hole.y = hole.ty = H / 2;
            InitRender();
            InitUI();
        }

        void OnDestroy() => DisposeRender();

        void Update()
        {
            if (Screen.width != screenW || Screen.height != screenH) Resize();
            HandleInput();

            float real = Mathf.Min(.05f, Time.unscaledDeltaTime), dt = real;
            if (paused) { UpdateUI(real); return; }
            if (hitstop > 0) { hitstop -= real; dt = 0; }
            else if (slowT > 0) { slowT -= real; dt = real * (1 - .72f * Mathf.Min(1, slowT / .2f)); }
            if (dt > 0) Step(dt);
            caAmt *= Mathf.Pow(.004f, real);
            shake *= Mathf.Pow(.025f, real); if (shake < .2f) shake = 0;
            flash = Mathf.Max(0, flash - real * 2.4f);
            UpdateUI(real);
        }

        void LateUpdate() => Render();

        // ---------- Échelle ----------
        void Resize()
        {
            screenW = Screen.width; screenH = Screen.height;
            float dpi = Screen.dpi;
            // Téléphone : 1 point ≈ 1 px CSS (160 dpi). Ordinateur : on se cale sur la taille de la fenêtre,
            // le DPI de l'écran ne dit rien de la taille de la vue de jeu (petite fenêtre sur un écran Retina)
            if (Application.isMobilePlatform) ppp = dpi > 0 ? Mathf.Clamp(dpi / 160f, 1, 4) : 2;
            else ppp = Mathf.Clamp(Mathf.Min(screenW, screenH) / 720f, 1, 3);
            W = screenW / ppp; H = screenH / ppp;
            SP = Mathf.Max(76, Mathf.Min(112, (W - 60) / 4.4f));
            if (mode != Mode.Run && mode != Mode.Nova) { hole.tx = W / 2; hole.ty = H / 2; }
            ResizeRender();
            ResizeUI();
        }

        // ---------- Entrées ----------
        void HandleInput()
        {
            var kb = Keyboard.current;
            // Échap, et le bouton retour Android que l'Input System présente comme Échap
            if (kb != null && kb.escapeKey.wasPressedThisFrame)
            {
                if (paused) Resume();
                else if (mode == Mode.Run) Pause();
            }

            if ((mode == Mode.Run || mode == Mode.Nova) && !paused)
            {
                var ts = Touchscreen.current;
                if (ts != null && ts.primaryTouch.press.isPressed)
                {
                    var p = ts.primaryTouch.position.ReadValue();
                    // Un doigt qui commence sur le bouton pause ne déplace pas le trou noir
                    if (ts.primaryTouch.press.wasPressedThisFrame) touchOnUI = OverButton(p);
                    if (!touchOnUI) SetTarget(p, true);
                }
                else if (Mouse.current != null)
                {
                    var m = Mouse.current;
                    var p = m.position.ReadValue();
                    if ((m.delta.ReadValue().sqrMagnitude > 0 || m.leftButton.wasPressedThisFrame) && !OverButton(p)) SetTarget(p, false);
                }
            }

            if (kb == null || !(kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)) return;
            if (paused) { if (!ButtonHasFocus()) Resume(); return; }
            if (ButtonHasFocus()) return;
            if (mode == Mode.Tree) StartRun();
            else if (mode == Mode.Summary && summaryReady) OpenTree();
            else if (mode == Mode.Menu) OnPlay();
        }

        bool touchOnUI;

        // Pause automatique quand l'app passe en arrière-plan (appel, notification, changement d'app).
        // Pas dans l'éditeur, où ces événements suivent le bouton pause et le focus de la fenêtre.
#if !UNITY_EDITOR
        void OnApplicationPause(bool pausing) { if (pausing && mode == Mode.Run) Pause(); }
        void OnApplicationFocus(bool focus) { if (!focus && mode == Mode.Run) Pause(); }
#endif

        // Le doigt cache le trou noir : on le décale de 55 points au-dessus, comme le prototype
        void SetTarget(Vector2 screen, bool touch)
        {
            hole.tx = screen.x / ppp;
            hole.ty = (Screen.height - screen.y) / ppp - (touch ? 55 : 0);
        }
    }
}
