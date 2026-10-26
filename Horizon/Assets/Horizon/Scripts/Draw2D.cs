using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Horizon
{
    // Équivalent du canvas 2D du prototype : chaque frame, toute la scène est accumulée dans un seul maillage
    // et rendue en un seul draw call. Le shader Horizon/Draw sort de l'alpha prémultiplié, donc un sommet
    // « additif » (alpha de sortie à 0) et un sommet « normal » cohabitent dans le même mélange, dans l'ordre.
    public sealed class Draw2D
    {
        struct Affine
        {
            public float a, b, c, d, e, f; // x' = a*x + c*y + e ; y' = b*x + d*y + f
            public static readonly Affine Id = new Affine { a = 1, d = 1 };
        }

        readonly List<Vector3> pos = new List<Vector3>(40000);
        readonly List<Color32> col = new List<Color32>(40000);
        readonly List<Vector4> uv = new List<Vector4>(40000);
        readonly List<Vector4> prm = new List<Vector4>(40000);
        readonly List<int> idx = new List<int>(80000);
        readonly Stack<Affine> stack = new Stack<Affine>();
        readonly List<Vector2> path = new List<Vector2>(256);

        public readonly Mesh Mesh;
        Affine m = Affine.Id;
        Color32 c = new Color32(255, 255, 255, 255);
        float additive;

        public Draw2D()
        {
            Mesh = new Mesh { name = "Horizon2D", indexFormat = IndexFormat.UInt32 };
            Mesh.MarkDynamic();
        }

        public void Begin()
        {
            pos.Clear(); col.Clear(); uv.Clear(); prm.Clear(); idx.Clear(); stack.Clear();
            m = Affine.Id; additive = 0;
        }

        public void End()
        {
            Mesh.Clear();
            Mesh.SetVertices(pos);
            Mesh.SetColors(col);
            Mesh.SetUVs(0, uv);
            Mesh.SetUVs(1, prm);
            Mesh.SetIndices(idx, MeshTopology.Triangles, 0, false);
            Mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e6f);
        }

        // ---------- État ----------
        public void Add() => additive = 1;
        public void Over() => additive = 0;

        public void Color(Color32 color, float alpha)
        {
            c = color;
            c.a = (byte)Mathf.Clamp(Mathf.RoundToInt(color.a * Mathf.Clamp01(alpha)), 0, 255);
        }

        public void Save() => stack.Push(m);
        public void Restore() { if (stack.Count > 0) m = stack.Pop(); }
        public void Translate(float x, float y) { m.e += m.a * x + m.c * y; m.f += m.b * x + m.d * y; }
        public void Scale(float sx, float sy) { m.a *= sx; m.b *= sx; m.c *= sy; m.d *= sy; }

        public void Rotate(float r)
        {
            float cs = Mathf.Cos(r), sn = Mathf.Sin(r);
            float a = m.a * cs + m.c * sn, b = m.b * cs + m.d * sn;
            float cc = m.c * cs - m.a * sn, d = m.d * cs - m.b * sn;
            m.a = a; m.b = b; m.c = cc; m.d = d;
        }

        float ScaleAbs => Mathf.Sqrt(Mathf.Abs(m.a * m.d - m.b * m.c));

        int V(float x, float y, Color32 cl, float u = -1, float v = -1, float mode = 0, Vector4 param = default)
        {
            pos.Add(new Vector3(m.a * x + m.c * y + m.e, m.b * x + m.d * y + m.f, 0));
            col.Add(cl);
            uv.Add(new Vector4(u, v, mode, additive));
            prm.Add(param);
            return pos.Count - 1;
        }

        void Tri(int i0, int i1, int i2) { idx.Add(i0); idx.Add(i1); idx.Add(i2); }
        void Quad(int i0, int i1, int i2, int i3) { Tri(i0, i1, i2); Tri(i0, i2, i3); }

        int Seg(float r) => Mathf.Clamp(Mathf.CeilToInt(r * ScaleAbs * .5f) + 6, 8, 72);

        // ---------- Remplissages ----------
        public void FillCircle(float x, float y, float r)
        {
            r = Mathf.Max(.1f, r);
            int n = Seg(r), c0 = V(x, y, c), first = pos.Count;
            for (int k = 0; k < n; k++)
            {
                float a = k * Mathf.PI * 2 / n;
                V(x + Mathf.Cos(a) * r, y + Mathf.Sin(a) * r, c);
            }
            for (int k = 0; k < n; k++) Tri(c0, first + k, first + (k + 1) % n);
        }

        public void FillPoly(float x, float y, float r, int sides, float rot)
        {
            int c0 = V(x, y, c), first = pos.Count;
            for (int k = 0; k < sides; k++)
            {
                float a = rot + k * Mathf.PI * 2 / sides;
                V(x + Mathf.Cos(a) * r, y + Mathf.Sin(a) * r, c);
            }
            for (int k = 0; k < sides; k++) Tri(c0, first + k, first + (k + 1) % sides);
        }

        public void FillRect(float x, float y, float w, float h)
            => Quad(V(x, y, c), V(x + w, y, c), V(x + w, y + h, c), V(x, y + h, c));

        void CirclePath(List<Vector2> into, float x, float y, float r)
        {
            into.Clear();
            int n = Seg(r);
            for (int k = 0; k < n; k++)
            {
                float a = k * Mathf.PI * 2 / n;
                into.Add(new Vector2(x + Mathf.Cos(a) * r, y + Mathf.Sin(a) * r));
            }
        }

        // ---------- Traits ----------
        public void StrokeCircle(float x, float y, float r, float w)
        {
            r = Mathf.Max(.1f, r);
            CirclePath(path, x, y, r);
            StrokePath(path, true, w);
        }

        public void StrokeArc(float x, float y, float r, float a0, float a1, float w)
        {
            path.Clear();
            int n = Mathf.Max(2, Mathf.CeilToInt(Seg(r) * Mathf.Abs(a1 - a0) / (Mathf.PI * 2)) + 1);
            for (int k = 0; k <= n; k++)
            {
                float a = Mathf.Lerp(a0, a1, (float)k / n);
                path.Add(new Vector2(x + Mathf.Cos(a) * r, y + Mathf.Sin(a) * r));
            }
            StrokePath(path, false, w);
        }

        public void StrokePoly(float x, float y, float r, int sides, float rot, float w)
        {
            path.Clear();
            for (int k = 0; k < sides; k++)
            {
                float a = rot + k * Mathf.PI * 2 / sides;
                path.Add(new Vector2(x + Mathf.Cos(a) * r, y + Mathf.Sin(a) * r));
            }
            StrokePath(path, true, w);
        }

        public void DashedCircle(float x, float y, float r, float w, float dash, float gap, float offset)
        {
            float circ = Mathf.PI * 2 * r, period = dash + gap;
            float s = ((offset % period) + period) % period - period;
            for (; s < circ; s += period)
            {
                float d0 = Mathf.Max(0, s), d1 = Mathf.Min(circ, s + dash);
                if (d1 > d0) StrokeArc(x, y, r, d0 / r, d1 / r, w);
            }
        }

        // Polyligne avec jointures en onglet (miter), comme le canvas par défaut
        void StrokePath(List<Vector2> p, bool closed, float w)
        {
            int n = p.Count;
            if (n < 2) return;
            float hw = w * .5f;
            int first = pos.Count;
            for (int i = 0; i < n; i++)
            {
                Vector2 cur = p[i];
                bool hasPrev = closed || i > 0, hasNext = closed || i < n - 1;
                Vector2 dPrev = hasPrev ? (cur - p[(i - 1 + n) % n]).normalized : Vector2.zero;
                Vector2 dNext = hasNext ? (p[(i + 1) % n] - cur).normalized : Vector2.zero;
                if (!hasPrev) dPrev = dNext;
                if (!hasNext) dNext = dPrev;
                var n0 = new Vector2(-dPrev.y, dPrev.x);
                var n1 = new Vector2(-dNext.y, dNext.x);
                var mt = n0 + n1;
                float ml = mt.magnitude;
                Vector2 off;
                if (ml < 1e-4f) off = n1 * hw;
                else
                {
                    mt /= ml;
                    float dot = Mathf.Max(.25f, Vector2.Dot(mt, n1));
                    off = mt * Mathf.Min(hw / dot, hw * 4);
                }
                V(cur.x + off.x, cur.y + off.y, c);
                V(cur.x - off.x, cur.y - off.y, c);
            }
            int segs = closed ? n : n - 1;
            for (int i = 0; i < segs; i++)
            {
                int a = first + i * 2, b = first + ((i + 1) % n) * 2;
                Quad(a, b, b + 1, a + 1);
            }
        }

        public void Line(float x0, float y0, float x1, float y1, float w, bool cap = false)
            => LineGrad(x0, y0, x1, y1, w, c, c, cap);

        public void LineGrad(float x0, float y0, float x1, float y1, float w, Color32 c0, Color32 c1, bool cap = false)
        {
            float dx = x1 - x0, dy = y1 - y0, l = Mathf.Sqrt(dx * dx + dy * dy);
            if (l < 1e-4f) { if (!cap) return; dx = 1; dy = 0; l = 1; }
            dx /= l; dy /= l;
            float hw = w * .5f, nx = -dy * hw, ny = dx * hw;
            if (cap) { x0 -= dx * hw; y0 -= dy * hw; x1 += dx * hw; y1 += dy * hw; }
            Quad(V(x0 + nx, y0 + ny, c0), V(x1 + nx, y1 + ny, c1), V(x1 - nx, y1 - ny, c1), V(x0 - nx, y0 - ny, c0));
        }

        // ---------- Halo (le dégradé radial mis en cache du prototype, calculé dans le shader) ----------
        public void Glow(float x, float y, float R, Color32 color, float alpha)
        {
            var g = color;
            g.a = (byte)Mathf.Clamp(Mathf.RoundToInt(255 * Mathf.Clamp01(alpha)), 0, 255);
            Quad(V(x - R, y - R, g, 0, 0, 1), V(x + R, y - R, g, 1, 0, 1), V(x + R, y + R, g, 1, 1, 1), V(x - R, y + R, g, 0, 1, 1));
        }

        // ---------- Quad calculé par un mode du shader (disque, sphère, galaxie) ----------
        // (u0,v0)-(u1,v1) : coordonnées locales transmises au shader, param : ses paramètres
        public enum Fx { Disk = 2, DiskFront = 3, Lens = 4, Moon = 5, Planet = 6, Giant = 7, Galaxy = 8 }

        public void ShaderQuad(float x, float y, float hx, float hy, float u0, float v0, float u1, float v1, Fx mode, Vector4 param)
        {
            float md = (float)mode;
            Quad(V(x - hx, y - hy, c, u0, v0, md, param), V(x + hx, y - hy, c, u1, v0, md, param),
                 V(x + hx, y + hy, c, u1, v1, md, param), V(x - hx, y + hy, c, u0, v1, md, param));
        }

        // Sphère de rayon r éclairée depuis la direction (lx, ly)
        public void Sphere(float x, float y, float r, Fx mode, float lx, float ly, float seed, float time = 0)
            => ShaderQuad(x, y, r, r, -1, -1, 1, 1, mode, new Vector4(lx, ly, seed, time));
    }
}
