// Post-traitement du prototype, porté du GLSL : nébuleuse, extraction des hautes lumières, flou,
// composition (lentille gravitationnelle, ondes de choc, aberration chromatique, vignette, grain, flash).
// Toutes les coordonnées sont en points, comme les px CSS du prototype.
Shader "Horizon/Post"
{
    Properties { _MainTex ("Scene", 2D) = "black" {} }

    CGINCLUDE
    #include "UnityCG.cginc"
    sampler2D _MainTex, _Bg, _Bl;

    float hs (float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
    ENDCG

    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        // 0 : nébuleuse
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            float _T, _Z;
            float2 _Asp;
            float3 _A, _B, _Base;

            float n (float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3. - 2. * f);
                return lerp(lerp(hs(i), hs(i + float2(1, 0)), f.x), lerp(hs(i + float2(0, 1)), hs(i + 1), f.x), f.y);
            }
            float fbm (float2 p)
            {
                float v = 0., a = .5;
                for (int i = 0; i < 5; i++) { v += a * n(p); p = p * 2.03 + float2(1.7, 9.2); a *= .5; }
                return v;
            }
            float4 frag (v2f_img i) : SV_Target
            {
                float2 p = (i.uv - .5) * _Asp * _Z * 1.6;
                float t = _T * .02;
                float2 q = float2(fbm(p + t), fbm(p - t + 3.1));
                float f = fbm(p * 1.1 + q * 1.8 + t * .5), f2 = fbm(p * 2.4 - q + 9.);
                float3 c = _Base;
                c += _A * smoothstep(.38, .95, f) * .5;
                c += _B * smoothstep(.3, .95, f2 * f * 1.4) * .45;
                c *= .7 + .4 * smoothstep(.25, .8, fbm(p * 4. + q * 2.));
                return float4(c, 1);
            }
            ENDCG
        }

        // 1 : hautes lumières
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            float4 frag (v2f_img i) : SV_Target
            {
                float3 c = tex2D(_MainTex, i.uv).rgb;
                float l = max(c.r, max(c.g, c.b));
                return float4(c * smoothstep(.42, 1., l), 1);
            }
            ENDCG
        }

        // 2 : flou gaussien séparable
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            float2 _Dir;
            float4 frag (v2f_img i) : SV_Target
            {
                float2 uv = i.uv;
                float3 c = tex2D(_MainTex, uv).rgb * .227027;
                c += (tex2D(_MainTex, uv + _Dir * 1.384615).rgb + tex2D(_MainTex, uv - _Dir * 1.384615).rgb) * .316216;
                c += (tex2D(_MainTex, uv + _Dir * 3.230769).rgb + tex2D(_MainTex, uv - _Dir * 3.230769).rgb) * .070270;
                return float4(c, 1);
            }
            ENDCG
        }

        // 3 : composition finale
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            float2 _Res;
            float3 _H0, _H1, _FlashC;
            float4 _W[4];
            float _CA, _T, _Flash, _Bloom, _LensS, _Zs, _Danger;

            float2 lens (float2 px, float3 h, float k, inout float ring)
            {
                if (h.z <= 0.) return px;
                float2 d = px - h.xy;
                float r = length(d) + .001;
                float Re = h.z * 1.9;
                float def = min(Re * Re / r, r * .96);
                float x = (r - Re) / (h.z * .2);
                ring += exp(-x * x);
                return px - d / r * def * k;
            }

            float starf (float2 p)
            {
                float s = 0.;
                for (int i = 0; i < 3; i++)
                {
                    float fi = float(i);
                    float sc = 34. + fi * 38.;
                    float2 g = p / sc + fi * 17.3;
                    float2 id = floor(g);
                    float2 f = frac(g) - .5;
                    float h = hs(id);
                    if (h > .62)
                    {
                        float2 o = float2(hs(id + 3.1), hs(id + 5.7)) - .5;
                        float d = length(f - o * .6) * sc;
                        float tw = .55 + .45 * sin(_T * (.8 + h * 3.) + h * 50.);
                        s += smoothstep(2.2 - fi * .4, 0., d) * tw * (h - .62) * 2.6 * (1. - fi * .25);
                    }
                }
                return s;
            }

            float4 frag (v2f_img i) : SV_Target
            {
                float2 uv = i.uv;
                float2 px = float2(uv.x, 1. - uv.y) * _Res;
                float2 off = 0;
                float ws = 0.;
                for (int k = 0; k < 4; k++)
                {
                    float4 w = _W[k];
                    if (w.w > 0.)
                    {
                        float2 d = px - w.xy;
                        float r = length(d) + .001;
                        float x = (r - w.z) / 38.;
                        float b = exp(-x * x);
                        off += d / r * sin(x * 2.6) * b * w.w * 18.;
                        ws += b * w.w;
                    }
                }
                float2 q = px + off;
                float ring = 0., rs = 0.;
                float2 pb = lens(lens(q, _H0, 1., ring), _H1, 1., ring);
                float2 ps = lens(lens(q, _H0, _LensS, rs), _H1, _LensS, rs);
                float2 tb = float2(pb.x / _Res.x, 1. - pb.y / _Res.y), ts = float2(ps.x / _Res.x, 1. - ps.y / _Res.y);
                float3 bg = tex2D(_Bg, tb).rgb;
                bg += float3(.9, .93, 1.) * starf(_H0.xy + (pb - _H0.xy) / _Zs);
                float2 cd = uv - .5;
                float ca = _CA + .004 * dot(cd, cd) + ws * .006;
                float4 s = tex2D(_MainTex, ts);
                float sr = tex2D(_MainTex, ts + cd * ca).r, sb = tex2D(_MainTex, ts - cd * ca).b;
                float3 col = bg * (1. - s.a) + float3(sr, s.g, sb);
                col += tex2D(_Bl, ts).rgb * _Bloom;
                col += float3(1., .74, .48) * ring * .22;
                float v = length(cd * float2(_Res.x / _Res.y, 1.));
                col *= lerp(1., .45, smoothstep(.45, 1.05, v));
                col += float3(1., .18, .3) * _Danger * smoothstep(.3, 1., v);
                col += (hs(px + frac(_T) * 91.) - .5) * .035;
                col = lerp(col, _FlashC, saturate(_Flash));
                col = saturate(col);
                #if !defined(UNITY_COLORSPACE_GAMMA)
                col = GammaToLinearSpace(col);
                #endif
                return float4(col, 1);
            }
            ENDCG
        }
    }
}
