// Couche de jeu : tout passe par ce shader, en un seul draw call, sortie en alpha prémultiplié.
// uv0 = (u, v, mode, additif). uv1 = paramètres du mode.
// Modes : 0 aplat, 1 halo radial, 2/3 disque d'accrétion arrière/avant, 4 image lentillée du disque,
//         5 lune, 6 planète, 7 géante gazeuse, 8 galaxie spirale.
Shader "Horizon/Draw"
{
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off ZWrite Off ZTest Always
        Blend One OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float4 color : COLOR; float4 uv : TEXCOORD0; float4 p : TEXCOORD1; };
            struct v2f { float4 pos : SV_POSITION; float4 color : COLOR; float4 uv : TEXCOORD0; float4 p : TEXCOORD1; };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = mul(UNITY_MATRIX_VP, float4(v.vertex.xyz, 1));
                o.color = v.color;
                o.uv = v.uv;
                o.p = v.p;
                return o;
            }

            static const float PI = 3.14159265;
            static const float TILT = .33;

            float hash (float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float vnoise (float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3. - 2. * f);
                return lerp(lerp(hash(i), hash(i + float2(1, 0)), f.x), lerp(hash(i + float2(0, 1)), hash(i + 1), f.x), f.y);
            }
            float fbm3 (float2 p)
            {
                float v = 0., a = .5;
                for (int k = 0; k < 3; k++) { v += a * vnoise(p); p = p * 2.03 + float2(1.7, 9.2); a *= .5; }
                return v;
            }

            // Arrêts du halo du prototype : 0 → ff, .22 → 99, .55 → 28, 1 → 00
            float glowCurve (float d)
            {
                if (d < .22) return lerp(1.0, .6, d / .22);
                if (d < .55) return lerp(.6, .157, (d - .22) / .33);
                if (d < 1.0) return lerp(.157, 0.0, (d - .55) / .45);
                return 0;
            }

            // Couleur du disque selon la chaleur h (1 au bord intérieur) : la palette des particules du prototype
            float3 diskPalette (float h, float pal)
            {
                float3 c0 = pal < .5 ? float3(.72, .61, 1.) : float3(.62, .31, .87);
                float3 c1 = pal < .5 ? float3(1., .48, .48) : float3(.96, .45, .71);
                float3 c2 = pal < .5 ? float3(1., .69, .42) : float3(.98, .62, .82);
                float3 c3 = pal < .5 ? float3(1., .95, .86) : float3(1., .84, .93);
                float3 c = lerp(c0, c1, smoothstep(.12, .3, h));
                c = lerp(c, c2, smoothstep(.34, .5, h));
                return lerp(c, c3, smoothstep(.64, .82, h));
            }

            // Luminosité du disque au rayon rn (en rayons du noyau, 1,25 à 3,55) et à l'angle ang.
            // Rotation képlérienne (plus rapide au centre), turbulence échantillonnée sur un cercle pour éviter la couture,
            // et le faisceau relativiste du prototype (un côté plus brillant).
            float diskI (float rn, float ang, float phase, float seed, out float h)
            {
                float t = (rn - 1.25) / 2.3;
                h = 1. - t;
                float edge = smoothstep(0., .06, t) * (1. - smoothstep(.5, 1., t));
                float rot = ang - phase * 2.8 / pow(max(rn, 1.), 1.5);
                float2 c = float2(cos(rot), sin(rot));
                float n = fbm3(c * 2.4 + float2(rn * 2.1 + seed, seed));
                float fine = vnoise(c * 9. + float2(rn * 7., seed * 3.));
                float streak = .35 + .85 * n + .4 * fine;
                float beam = .45 + .55 * (.5 - .5 * cos(ang + PI));
                // Le disque est continu là où le prototype n'avait que des traits épars : on le garde sous l'exposition
                return edge * (.3 + .8 * h) * beam * streak * .48;
            }

            float4 frag (v2f i) : SV_Target
            {
                float mode = i.uv.z;
                float3 rgb = i.color.rgb;
                float a = i.color.a;
                float2 p = i.uv.xy;

                if (mode < .5) { }
                else if (mode < 1.5)
                {
                    a *= glowCurve(length(p * 2 - 1));
                }
                else if (mode < 3.5)
                {
                    // Disque incliné : on le « redresse » pour retrouver le rayon et l'angle dans son plan
                    bool front = p.y > 0;
                    if ((mode < 2.5) == front) return 0;
                    float2 q = float2(p.x, p.y / TILT);
                    float h;
                    float I = diskI(length(q), atan2(q.y, q.x), i.p.x, i.p.w, h);
                    rgb = diskPalette(h, i.p.y);
                    a *= saturate(I);
                }
                else if (mode < 4.5)
                {
                    // Image lentillée : l'arrière du disque replié en anneau autour du noyau, surtout au-dessus
                    float rho = length(p);
                    float rn = 1.25 + (rho - 1.07) / .2;
                    float h;
                    float I = diskI(rn, atan2(p.y, p.x), i.p.x, i.p.w, h);
                    rgb = diskPalette(h, i.p.y);
                    a *= saturate(I * lerp(.55, .16, smoothstep(-.2, .2, p.y)));
                }
                else if (mode < 7.5)
                {
                    // Sphère éclairée par le trou noir : la lumière vient de la direction (p.x, p.y) du paramètre
                    float d = length(p);
                    float aa = saturate((1. - d) / max(fwidth(d), 1e-4));
                    if (aa <= 0) return 0;
                    float3 n = float3(p, sqrt(saturate(1. - d * d)));
                    float3 L = normalize(float3(i.p.x, i.p.y, .65));
                    float diff = saturate(dot(n, L));
                    float seed = i.p.z;
                    float tex;
                    if (mode < 5.5)
                    {
                        float cr = vnoise(n.xy * 3.2 + seed * 7.), cr2 = vnoise(n.xy * 7.5 + seed * 3.);
                        tex = .9 + .14 * fbm3(n.xy * 5. + seed) - .3 * smoothstep(.6, .7, cr) - .16 * smoothstep(.66, .74, cr2);
                    }
                    else if (mode < 6.5)
                    {
                        float lat = n.y + .16 * fbm3(n.xy * 3. + seed);
                        tex = .84 + .2 * sin(lat * 9. + seed * 5.) + .1 * fbm3(n.xy * 6. + seed);
                    }
                    else
                    {
                        float lat = n.y + .22 * fbm3(float2(n.x * 2. + i.p.w * .15, n.y * 3.) + seed);
                        tex = .8 + .26 * sin(lat * 13. + seed);
                    }
                    float rim = pow(1. - n.z, 2.5);
                    rgb = rgb * tex * (.22 + .95 * diff) + rgb * rim * .5 * (.35 + diff);
                    a *= aa;
                }
                else
                {
                    // Galaxie : deux bras en spirale d'Archimède, comme les points du prototype, avec un bulbe blanc
                    float2 q = float2(p.x, p.y / .8);
                    float rho = length(q);
                    float th = atan2(q.y, q.x) - i.p.x;
                    float arms = cos(2. * (th - 3.58 * (rho - .15)));
                    float dens = smoothstep(.15, 1., arms * .5 + .5) * exp(-rho * 2.4) * (1. - smoothstep(.75, 1., rho));
                    dens *= .55 + .8 * vnoise(q * 13. + i.p.z * 9.);
                    float core = exp(-rho * rho * 45.);
                    rgb = lerp(rgb, float3(1, 1, 1), saturate(core * 1.5));
                    a *= saturate(dens * 2.2 + core * 1.2);
                }

                return float4(rgb * a, i.uv.w > .5 ? 0 : a);
            }
            ENDCG
        }
    }
}
