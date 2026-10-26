using UnityEngine;
using UnityEngine.Rendering;

namespace Horizon
{
    public sealed partial class HorizonGame
    {
        Draw2D d;
        Material drawMat, postMat;
        CommandBuffer cmd;
        RenderTexture sceneRT, bgRT, blurA, blurB, finalRT;
        readonly Vector4[] waveVec = new Vector4[4];
        static readonly float[] BlurSteps = { 1f, 2.2f };

        static readonly Color32 Black = new Color32(0, 0, 0, 255), Violet = Data.Hex("#a78bfa"), Hot = Data.Hex("#ff9a5a");
        static readonly Color32 Rim = Data.Hex("#ffe8cd"), Mint = Data.Hex("#6ee7b7");
        static readonly Color32 RepelBlue = Data.Hex("#60a5fa"), RepelCore = Data.Hex("#3b82f6"), PulsarCol = Data.Hex("#a5f3fc");
        static readonly Color32 WhiteHole = Data.Hex("#e0f2fe");

        // ---------- Disque d'accrétion en particules, incliné, avec images lentillées façon Gargantua ----------
        sealed class DiskP { public float a, r, w, h; public Color32 col; }
        DiskP[] disk, disk2, diskR;
        const float TILT = .33f;

        static DiskP[] MakeDisk(int n)
        {
            var a = new DiskP[n];
            for (int i = 0; i < n; i++)
            {
                float r = Mathf.Pow(R(), 1.5f), h = 1 - r;
                a[i] = new DiskP { a = R() * TAU, r = r, w = .75f + R() * .5f, h = h, col = Data.Hex(h > .72f ? "#fff3dc" : h > .42f ? "#ffb06a" : h > .2f ? "#ff7a7a" : "#b89bff") };
            }
            return a;
        }

        static void DiskUpdate(DiskP[] dk, float dt)
        {
            foreach (var p in dk) { float rn = 1.25f + p.r * 2.3f; p.a += dt * 2.8f * p.w / Mathf.Pow(rn, 1.5f); }
        }

        // Étincelles par-dessus le disque continu : quelques traits qui donnent le sens de rotation
        void DiskDraw(DiskP[] dk, float x, float y, float core, bool front)
        {
            d.Add();
            foreach (var p in dk)
            {
                float s = Mathf.Sin(p.a); if ((s > 0) != front) continue;
                float rn = 1.25f + p.r * 2.3f, rr = core * rn, beam = .45f + .55f * (.5f - .5f * Mathf.Cos(p.a + Mathf.PI));
                float a0 = p.a - .22f / rn;
                d.Color(p.col, Mathf.Min(1, (.3f + .8f * p.h) * beam) * .7f);
                d.Line(x + Mathf.Cos(a0) * rr, y + Mathf.Sin(a0) * rr * TILT, x + Mathf.Cos(p.a) * rr, y + Mathf.Sin(p.a) * rr * TILT, Mathf.Max(.7f, core * .05f * (1.25f - p.r)), true);
            }
        }

        // Disque d'accrétion continu calculé dans le shader (moitié arrière ou avant)
        const float DiskExtent = 3.7f;
        float diskPhase;

        void DiskQuad(float x, float y, float core, Draw2D.Fx half, float pal, float seed)
        {
            const float E = DiskExtent;
            d.Add(); d.Color(White, .9f);
            d.ShaderQuad(x, y, core * E, core * E * TILT, -E, -E * TILT, E, E * TILT, half, new Vector4(diskPhase, pal, 0, seed));
        }

        void HoleBack(float x, float y, float core, DiskP[] dk, float pal, float seed)
        {
            d.Add();
            d.Glow(x, y, core * 7, Violet, .28f);
            d.Glow(x, y, core * 3.6f, Hot, .55f);
            DiskQuad(x, y, core, Draw2D.Fx.Disk, pal, seed);
            DiskDraw(dk, x, y, core, false);
        }

        void HoleFront(float x, float y, float core, DiskP[] dk, float pal, float seed)
        {
            d.Over(); d.Color(Black, 1); d.FillCircle(x, y, core);
            // L'arrière du disque, replié par la gravité en anneau au-dessus du noyau
            d.Add(); d.Color(White, .95f);
            d.ShaderQuad(x, y, core * 1.6f, core * 1.6f, -1.6f, -1.6f, 1.6f, 1.6f, Draw2D.Fx.Lens, new Vector4(diskPhase, pal, 0, seed));
            d.Over(); d.Color(Rim, .9f); d.StrokeCircle(x, y, core * 1.02f, 1.3f);
            DiskQuad(x, y, core, Draw2D.Fx.DiskFront, pal, seed);
            DiskDraw(dk, x, y, core, true);
            d.Over();
        }

        // Anneau incliné d'une planète géante : la moitié arrière passe derrière la sphère, l'avant devant
        void PlanetRing(Obj o, float r, Color32 c, bool back)
        {
            d.Save(); d.Rotate(o.seed * .3f - .4f); d.Scale(1, .3f);
            d.Color(c, .85f);
            if (back) d.StrokeArc(0, 0, r * 1.7f, Mathf.PI, TAU, 2.5f);
            else d.StrokeArc(0, 0, r * 1.7f, 0, Mathf.PI, 2.5f);
            d.Restore();
        }

        static float EaseBack(float t) { const float c = 1.7f; return 1 + (c + 1) * Mathf.Pow(t - 1, 3) + c * Mathf.Pow(t - 1, 2); }

        void DrawObj(Obj o)
        {
            var style = Data.Eras[o.era].style; var col = ColOf(o);
            float sc = o.born < 1 ? Mathf.Max(0, EaseBack(o.born)) : 1; bool hit = o.hitT > 0;
            float r = o.r * sc * (hit ? .85f + R() * .25f : 1);
            float x = o.x + (hit ? (R() - .5f) * 2.5f : 0), y = o.y + (hit ? (R() - .5f) * 2.5f : 0);
            var c = hit ? White : col;
            bool solid = style == Style.Moon || style == Style.Planet;

            d.Add();
            d.Glow(x, y, r * (style == Style.Star ? 3.6f : 2.5f), col, o.tier == 0 ? .35f : .5f);

            // Spaghettification : étirement radial près du trou noir
            float dx = hole.x - o.x, dy = hole.y - o.y, dd = Dist(dx, dy), P = PullEff(); if (dd == 0) dd = 1;
            float lx = dx / dd, ly = dy / dd; // la lumière vient du trou noir
            float str = 1; if (dd < P) { float t = 1 - dd / P; str = 1 + 1.4f * t * t * t; }
            d.Save(); d.Translate(x, y);
            if (str > 1.02f) { float a = Mathf.Atan2(dy, dx); d.Rotate(a); d.Scale(str, 1 / Mathf.Sqrt(str)); d.Rotate(-a); }

            if (o.tier == 0)
            {
                d.Color(c, .95f);
                if (style == Style.Shard) d.FillPoly(0, 0, r, 4, o.rot); else d.FillCircle(0, 0, r * .8f);
                d.Restore(); return;
            }
            if (solid) d.Over(); else d.Add();
            switch (style)
            {
                case Style.Shard:
                    d.Color(c, .32f); d.FillPoly(0, 0, r, o.sides, o.rot);
                    d.Color(c, 1); d.StrokePoly(0, 0, r, o.sides, o.rot, 1.5f);
                    break;
                case Style.Moon:
                    d.Color(c, 1); d.Sphere(0, 0, r, Draw2D.Fx.Moon, lx, ly, o.seed);
                    break;
                case Style.Planet:
                    if (o.tier >= 2) PlanetRing(o, r, c, true);
                    d.Color(c, 1); d.Sphere(0, 0, r, Draw2D.Fx.Planet, lx, ly, o.seed);
                    if (o.tier >= 2) PlanetRing(o, r, c, false);
                    break;
                case Style.Star:
                    d.Color(c, .8f); d.FillCircle(0, 0, r);
                    d.Color(White, 1); d.FillCircle(0, 0, r * .5f);
                    d.Color(c, .8f);
                    for (int k = 0; k < 2; k++)
                    {
                        float a = o.rot * .2f + k * Mathf.PI / 2, L = r * (2.6f + o.tier * .5f);
                        d.Line(-Mathf.Cos(a) * L, -Mathf.Sin(a) * L, Mathf.Cos(a) * L, Mathf.Sin(a) * L, 1.2f);
                    }
                    break;
                case Style.System:
                {
                    d.Color(c, 1); d.FillCircle(0, 0, r * .32f);
                    int n = Mathf.Min(o.tier + 1, 4);
                    for (int k = 0; k < n; k++)
                    {
                        float rr = r * (.55f + k * .2f);
                        d.Color(c, .22f); d.StrokeCircle(0, 0, rr, 1);
                        float a = o.seed + o.rot * (1.8f - k * .35f);
                        d.Color(k % 2 == 1 ? White : c, 1); d.FillCircle(Mathf.Cos(a) * rr, Mathf.Sin(a) * rr, Mathf.Max(1.3f, r * .09f));
                    }
                    break;
                }
                case Style.Galaxy:
                    d.Color(c, 1);
                    d.ShaderQuad(0, 0, r * 1.15f, r * 1.15f, -1, -1, 1, 1, Draw2D.Fx.Galaxy, new Vector4(o.rot, 0, o.seed, 0));
                    break;
            }
            d.Restore();
        }

        // ---------- Menaces ----------
        void DrawThreats(float tt)
        {
            foreach (var t in threats)
            {
                if (t.type == ThreatType.Rival) continue;
                float b = t.born < 1 ? Mathf.Max(0, EaseBack(t.born)) : 1; bool hit = t.hitT > 0;
                d.Add();
                if (t.type == ThreatType.Repel)
                {
                    for (int k = 0; k < 3; k++) { float ph = (tt * .8f + k / 3f) % 1; d.Color(RepelBlue, (1 - ph) * .35f); d.StrokeCircle(t.x, t.y, t.r + ph * (t.R - t.r), 2); }
                    d.Glow(t.x, t.y, t.r * 3, RepelBlue, .6f);
                    float gx = hole.x - t.x, gy = hole.y - t.y, gd = Mathf.Max(1, Dist(gx, gy));
                    d.Over(); d.Color(hit ? White : RepelCore, 1);
                    d.Sphere(t.x, t.y, t.r * b, Draw2D.Fx.Giant, gx / gd, gy / gd, 2.3f, tt);
                }
                if (t.type == ThreatType.Pulsar)
                {
                    var c0 = PulsarCol; c0.a = 230; var c1 = PulsarCol; c1.a = 0;
                    for (int s = 0; s < 2; s++)
                    {
                        float a = t.a + s * Mathf.PI, ex = t.x + Mathf.Cos(a) * t.L, ey = t.y + Mathf.Sin(a) * t.L;
                        var w0 = c0; w0.a = (byte)(230 * .22f);
                        d.LineGrad(t.x, t.y, ex, ey, 16, w0, c1);
                        d.LineGrad(t.x, t.y, ex, ey, 3, c0, c1);
                    }
                    d.Glow(t.x, t.y, t.r * 5, PulsarCol, .9f);
                    d.Color(hit ? Gold : White, 1); d.FillCircle(t.x, t.y, t.r * b);
                }
                if (t.type == ThreatType.White)
                {
                    for (int k = 0; k < 14; k++)
                    {
                        float a = k / 14f * TAU + tt * .3f, ph = (tt * 1.2f + k * .37f) % 1, r0 = t.r * 1.2f + ph * t.R * .6f;
                        d.Color(WhiteHole, (1 - ph) * .55f);
                        d.Line(t.x + Mathf.Cos(a) * r0, t.y + Mathf.Sin(a) * r0, t.x + Mathf.Cos(a) * (r0 + 18), t.y + Mathf.Sin(a) * (r0 + 18), 1.6f, true);
                    }
                    d.Glow(t.x, t.y, t.r * 4.5f, WhiteHole, 1);
                    d.Color(hit ? Gold : White, 1); d.FillCircle(t.x, t.y, t.r * b);
                }
                if (t.hp < t.max)
                {
                    d.Over(); d.Color(Gold, .95f);
                    d.StrokeArc(t.x, t.y, t.r + 8, -Mathf.PI / 2, -Mathf.PI / 2 + TAU * Mathf.Max(0, t.hp / t.max), 2.5f);
                }
            }
            d.Over();
        }

        void DrawThreatsFront(float tt)
        {
            foreach (var t in threats)
            {
                if (t.type != ThreatType.Rival) continue;
                HoleFront(t.x, t.y, t.r, diskR, 1, 3.1f);
                d.Color(Edible(t) ? Mint : Danger_, .85f);
                d.DashedCircle(t.x, t.y, t.r * 1.9f + 8, 1.6f, 4, 6, -tt * 1000 / 60);
            }
        }

        bool TwinPos(out float tx, out float ty, out float tr)
        {
            tx = ty = tr = 0;
            if (st.twin <= 0) return false;
            float b = CoreEff(), dd = b + 50 + st.twin * 10;
            tx = hole.x + Mathf.Cos(twinA) * dd; ty = hole.y + Mathf.Sin(twinA) * dd; tr = b * (.45f + .1f * st.twin);
            return true;
        }

        // ---------- Image ----------
        void InitRender()
        {
            d = new Draw2D();
            drawMat = new Material(Shader.Find("Horizon/Draw")) { hideFlags = HideFlags.HideAndDontSave };
            postMat = new Material(Shader.Find("Horizon/Post")) { hideFlags = HideFlags.HideAndDontSave };
            cmd = new CommandBuffer { name = "Horizon 2D" };
            disk = MakeDisk(110); disk2 = MakeDisk(45); diskR = MakeDisk(55);
            foreach (var p in diskR) p.col = Data.Hex(p.h > .62f ? "#ffd6ec" : p.h > .3f ? "#f472b6" : "#9d4edd");
            ResizeRender();
        }

        static RenderTexture NewRT(int w, int h, bool srgb = false, int msaa = 1)
        {
            var rt = new RenderTexture(Mathf.Max(1, w), Mathf.Max(1, h), 0, RenderTextureFormat.ARGB32, srgb ? RenderTextureReadWrite.sRGB : RenderTextureReadWrite.Linear)
            { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave, antiAliasing = msaa };
            rt.Create();
            return rt;
        }

        void ReleaseRTs()
        {
            foreach (var rt in new[] { sceneRT, bgRT, blurA, blurB, finalRT }) if (rt != null) { rt.Release(); Destroy(rt); }
        }

        void ResizeRender()
        {
            if (drawMat == null) return;
            ReleaseRTs();
            // Densité plafonnée à 2 (1,5 dans le prototype web, trop flou sur un écran de téléphone),
            // nébuleuse en demi-résolution, bloom en quart
            float rs = Mathf.Min(ppp, 2f);
            int w = Mathf.RoundToInt(W * rs), h = Mathf.RoundToInt(H * rs);
            // MSAA 4× : le canvas du prototype lissait ses bords, nos polygones non (quasi gratuit sur GPU mobile à tuiles)
            sceneRT = NewRT(w, h, false, 4);
            bgRT = NewRT(w >> 1, h >> 1);
            blurA = NewRT(w >> 2, h >> 2); blurB = NewRT(w >> 2, h >> 2);
            finalRT = NewRT(w, h, true);
            OnFinalImage(finalRT);
        }

        void DisposeRender()
        {
            ReleaseRTs();
            if (drawMat != null) Destroy(drawMat);
            if (postMat != null) Destroy(postMat);
            cmd?.Release();
        }

        void Render()
        {
            if (drawMat == null) return;
            float z = CamZ(), tt = Time.unscaledTime;
            d.Begin();
            shX = shake > .3f ? (R() - .5f) * shake : 0; shY = shake > .3f ? (R() - .5f) * shake : 0;
            d.Translate(shX, shY);

            d.Save();
            d.Translate(hole.x, hole.y); d.Scale(z, z); d.Translate(-hole.x, -hole.y);

            bool nova = mode == Mode.Nova;
            float coreR = CoreEff(); if (nova) coreR *= 1 + (1.6f - novaT) * .9f;
            if (Danger() > 0) coreR *= .94f + .06f * R();
            bool hasTwin = TwinPos(out float twx, out float twy, out float twr);

            if (mode == Mode.Run && transT <= 0)
            {
                d.Over(); d.Color(Violet, .12f);
                d.DashedCircle(hole.x, hole.y, PullEff(), 1, 3, 9, hole.spin * 18);
            }
            HoleBack(hole.x, hole.y, coreR, disk, 0, 0);
            if (hasTwin) HoleBack(twx, twy, twr, disk2, 0, 1.7f);
            foreach (var t in threats) if (t.type == ThreatType.Rival) HoleBack(t.x, t.y, t.r, diskR, 1, 3.1f);

            foreach (var o in objs) DrawObj(o);
            DrawThreats(tt);

            d.Add();
            foreach (var p in parts)
            {
                float a = p.life / p.max;
                d.Color(p.col, a);
                if (p.ring > 0) { d.StrokeCircle(p.x, p.y, p.ring * (1 - a * .6f), 2 * a); continue; }
                if (Mathf.Abs(p.vx) + Mathf.Abs(p.vy) > 40)
                {
                    float sp = Dist(p.vx, p.vy), L = Mathf.Min(14, sp * .03f) / sp;
                    d.Line(p.x - p.vx * L, p.y - p.vy * L, p.x, p.y, p.size * (.4f + a * .8f), true);
                }
                else { float s = p.size * (.5f + a); d.FillRect(p.x - s / 2, p.y - s / 2, s, s); }
            }
            d.Over();

            HoleFront(hole.x, hole.y, coreR, disk, 0, 0);
            if (hasTwin) HoleFront(twx, twy, twr, disk2, 0, 1.7f);
            DrawThreatsFront(tt);
            d.Restore();
            d.End();

            cmd.Clear();
            cmd.SetRenderTarget(sceneRT);
            cmd.ClearRenderTarget(false, true, Color.clear);
            // SetViewProjectionMatrices applique lui-même la conversion GPU (inversion Y des render textures)
            cmd.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.Ortho(0, W, H, 0, -1, 1));
            cmd.DrawMesh(d.Mesh, Matrix4x4.identity, drawMat, 0, 0);
            Graphics.ExecuteCommandBuffer(cmd);

            PostFX(z, coreR, hasTwin, twx, twy, twr, tt);
        }

        void PostFX(float z, float coreR, bool hasTwin, float twx, float twy, float twr, float tt)
        {
            float m = Mathf.Min(W, H), t = tt % 1000;
            postMat.SetFloat("_T", t);
            postMat.SetFloat("_Z", 1 / (1 + (z - 1) * .3f));
            postMat.SetVector("_Asp", new Vector4(W / m, H / m));
            postMat.SetVector("_A", nebA); postMat.SetVector("_B", nebB); postMat.SetVector("_Base", bgC / 255f);
            Graphics.Blit(sceneRT, bgRT, postMat, 0);

            Graphics.Blit(sceneRT, blurA, postMat, 1);
            float qw = blurA.width, qh = blurA.height;
            foreach (float k in BlurSteps)
            {
                postMat.SetVector("_Dir", new Vector4(k / qw, 0)); Graphics.Blit(blurA, blurB, postMat, 2);
                postMat.SetVector("_Dir", new Vector4(0, k / qh)); Graphics.Blit(blurB, blurA, postMat, 2);
            }

            postMat.SetTexture("_Bg", bgRT); postMat.SetTexture("_Bl", blurA);
            postMat.SetVector("_Res", new Vector4(W, H));
            postMat.SetVector("_H0", new Vector4(hole.x + shX, hole.y + shY, coreR * z));
            postMat.SetVector("_H1", hasTwin
                ? new Vector4(hole.x + (twx - hole.x) * z + shX, hole.y + (twy - hole.y) * z + shY, twr * z)
                : Vector4.zero);
            for (int i = 0; i < 4; i++)
            {
                if (i < waves.Count)
                {
                    var w = waves[i]; float k = w.t / w.life;
                    waveVec[i] = new Vector4(hole.x + (w.x - hole.x) * z + shX, hole.y + (w.y - hole.y) * z + shY, w.t * w.speed * z, w.amp * (1 - k) * (1 - k));
                }
                else waveVec[i] = Vector4.zero;
            }
            postMat.SetVectorArray("_W", waveVec);
            postMat.SetFloat("_CA", .0015f + caAmt * .006f);
            postMat.SetFloat("_Flash", Mathf.Min(.6f, flash));
            postMat.SetVector("_FlashC", mode == Mode.Nova ? new Vector4(1, .89f, .75f) : new Vector4(1, 1, 1));
            postMat.SetFloat("_Bloom", 1.15f); postMat.SetFloat("_LensS", .3f); postMat.SetFloat("_Zs", 1 + (z - 1) * .35f);
            postMat.SetFloat("_Danger", Danger() * (.35f + .25f * Mathf.Sin(tt * 9)));
            Graphics.Blit(sceneRT, finalRT, postMat, 3);
        }
    }
}
