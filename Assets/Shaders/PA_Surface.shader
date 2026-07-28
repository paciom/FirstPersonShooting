// World-space triplanar SURFACE shader for arena geometry.
//
// The point of this shader, versus PA_SciFiPanel: that one draws panel grooves
// and glowing seams in every mode, so every arena built from it reads as "neon
// lines" whatever colours you feed it. This one carries genuinely different
// material archetypes — stone is mottled and matte with dark crevices, brick has
// staggered courses and mortar, tread plate is glossy diamond checker — and only
// the tech styles emit at all.
//
// Three things do the material differentiation:
//   * a per-style procedural pattern (albedo mask + height field),
//   * cavity shading, so recesses in that height field genuinely darken,
//   * derivative-based bump, so the height field perturbs the normal without
//     UVs, tangents or a normal map (Mikkelsen's unparametrised bump trick).
//
// Roughness drives the specular lobe, which is most of why polished plastic and
// dry sandstone do not read the same even in identical light.
Shader "PhotonArena/Surface"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.5, 0.5, 0.5, 1)
        _SecondColor ("Recess / Mortar / Vein Color", Color) = (0.2, 0.2, 0.2, 1)
        [HDR] _EmitColor ("Emission Color", Color) = (0, 0, 0, 1)
        _Tiling ("Feature Size (world units)", Float) = 1
        _Style ("Style (0 hull,1 stone,2 brick,3 tread,4 organic,5 crystal,6 strata,7 plank)", Float) = 1
        _Roughness ("Roughness", Range(0,1)) = 0.85
        _EmitStrength ("Emission Strength", Float) = 0
        _BumpStrength ("Bump Strength", Range(0,3)) = 1
        _Cavity ("Cavity Shading", Range(0,1)) = 0.55
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _SecondColor;
                float4 _EmitColor;
                float _Tiling;
                float _Style;
                float _Roughness;
                float _EmitStrength;
                float _BumpStrength;
                float _Cavity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs p = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionHCS = p.positionCS;
                OUT.positionWS = p.positionWS;
                OUT.normalWS = GetVertexNormalInputs(IN.normalOS).normalWS;
                return OUT;
            }

            // ---------- noise ----------

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 345.45));
                p += dot(p, p + 34.345);
                return frac(p.x * p.y);
            }

            float ValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = Hash21(i);
                float b = Hash21(i + float2(1, 0));
                float c = Hash21(i + float2(0, 1));
                float d = Hash21(i + float2(1, 1));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            float FBM(float2 p)
            {
                float sum = 0.0;
                float amp = 0.5;
                for (int i = 0; i < 3; i++)
                {
                    sum += amp * ValueNoise(p);
                    p *= 2.03;
                    amp *= 0.5;
                }
                return sum;
            }

            // ---------- styles ----------
            //
            // Returns (mask, emit, height).
            //   mask   0 = base colour, 1 = the recess/mortar/vein colour
            //   emit   how much _EmitColor this texel throws
            //   height 0 = deepest recess, 1 = proudest surface (drives cavity + bump)

            float3 StylePattern(float2 uv, float style)
            {
                // --- 0: HULL — spaceship plating with lit seams ---
                if (style < 0.5)
                {
                    float2 g = frac(uv);
                    float2 d = min(g, 1.0 - g);
                    float edge = min(d.x, d.y);
                    float groove = 1.0 - smoothstep(0.0, 0.025, edge);
                    float seam = smoothstep(0.025, 0.04, edge) - smoothstep(0.05, 0.07, edge);
                    float rivet = (1.0 - smoothstep(0.0, 0.06, length(d))) * 0.6;
                    return float3(groove, saturate(seam + rivet), 1.0 - groove * 0.9);
                }

                // --- 1: STONE — mottled rock with dark irregular crevices ---
                if (style < 1.5)
                {
                    float coarse = FBM(uv * 1.3);
                    float fine = FBM(uv * 5.0);
                    // Crevices trace the ridges of a second noise field, so they
                    // wander like real cracks instead of forming a grid.
                    float ridge = abs(FBM(uv * 0.8) - 0.5);
                    float crack = 1.0 - smoothstep(0.0, 0.055, ridge);
                    float h = saturate(coarse * 0.7 + fine * 0.3 - crack * 0.8);
                    return float3(saturate(crack + fine * 0.25), 0.0, h);
                }

                // --- 2: BRICK — running bond, staggered courses, mortar ---
                if (style < 2.5)
                {
                    // Courses are half as tall as a brick is long.
                    float course = floor(uv.y * 2.0);
                    float stagger = frac(course * 0.5) * 1.0;   // alternate rows by half a brick
                    float2 b = float2(frac(uv.x + stagger), frac(uv.y * 2.0));
                    float mx = min(b.x, 1.0 - b.x);
                    float my = min(b.y, 1.0 - b.y);
                    // Mortar is thinner along the long axis so bricks stay oblong.
                    float mortar = 1.0 - smoothstep(0.0, 0.035, mx);
                    mortar = max(mortar, 1.0 - smoothstep(0.0, 0.07, my));
                    float id = Hash21(float2(floor(uv.x + stagger), course));
                    float grit = FBM(uv * 9.0) * 0.18;
                    // Per-brick tone variation is what stops it reading as wallpaper.
                    float h = saturate(0.72 + id * 0.28 - mortar * 0.75 - grit * 0.4);
                    return float3(saturate(mortar + grit * 0.5 - id * 0.15), 0.0, h);
                }

                // --- 3: TREAD — diamond checker plate ---
                if (style < 3.5)
                {
                    // Diamond studs, as on real tread plate.
                    float2 r = frac(uv * 2.0) - 0.5;
                    float diamond = abs(r.x) + abs(r.y);
                    float stud = smoothstep(0.42, 0.30, diamond);
                    float plateGap = 1.0 - smoothstep(0.0, 0.02, min(min(frac(uv.x), 1.0 - frac(uv.x)),
                                                                     min(frac(uv.y), 1.0 - frac(uv.y))));
                    float h = saturate(0.45 + stud * 0.55 - plateGap * 0.5);
                    return float3(saturate(plateGap * 0.8 - stud * 0.3), 0.0, h);
                }

                // --- 4: ORGANIC — soft cells with glowing veins ---
                if (style < 4.5)
                {
                    float v = FBM(uv * 2.2);
                    float vein = smoothstep(0.44, 0.50, v) - smoothstep(0.50, 0.57, v);
                    float pores = FBM(uv * 7.0);
                    float h = saturate(0.5 + v * 0.5 - vein * 0.35 + pores * 0.15);
                    return float3(saturate(vein * 0.8), saturate(vein), h);
                }

                // --- 5: CRYSTAL — flat faceted cells ---
                if (style < 5.5)
                {
                    float2 cell = floor(uv * 1.4);
                    float id = Hash21(cell);
                    float2 f = frac(uv * 1.4);
                    float2 d = min(f, 1.0 - f);
                    float facetEdge = 1.0 - smoothstep(0.0, 0.03, min(d.x, d.y));
                    // Facets are flat planes, so height steps per cell rather than
                    // varying inside it — that is what makes them read as cut.
                    float h = saturate(0.35 + id * 0.65 - facetEdge * 0.3);
                    return float3(saturate(facetEdge * 0.6), saturate(id * 0.5 + facetEdge * 0.4), h);
                }

                // --- 6: STRATA — sedimentary bands ---
                if (style < 6.5)
                {
                    // Bands follow the second uv axis, which triplanar maps to
                    // world Y on both wall projections — so layers stay level.
                    float warp = FBM(float2(uv.x * 0.5, uv.y * 0.5)) * 0.35;
                    float band = frac(uv.y * 1.6 + warp);
                    float layer = smoothstep(0.0, 0.06, band) - smoothstep(0.85, 1.0, band);
                    float id = Hash21(float2(0.0, floor(uv.y * 1.6 + warp)));
                    float grit = FBM(uv * 8.0) * 0.2;
                    float h = saturate(0.55 + id * 0.35 - (1.0 - layer) * 0.4 + grit * 0.3);
                    return float3(saturate((1.0 - layer) * 0.7 + id * 0.3), 0.0, h);
                }

                // --- 7: PLANK — painted boards with grain ---
                float board = floor(uv.y);
                float slide = Hash21(float2(board, 3.7));
                float2 p = float2(uv.x + slide * 4.0, uv.y);
                float gapY = 1.0 - smoothstep(0.0, 0.03, min(frac(p.y), 1.0 - frac(p.y)));
                float gapX = 1.0 - smoothstep(0.0, 0.02, min(frac(p.x * 0.25), 1.0 - frac(p.x * 0.25)));
                float gap = max(gapY, gapX);
                // Grain runs along the board, stretched hard on one axis.
                float grain = FBM(float2(p.x * 2.0, p.y * 22.0));
                float h = saturate(0.75 + grain * 0.25 - gap * 0.7);
                return float3(saturate(gap + grain * 0.3), 0.0, h);
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 wpos = IN.positionWS / max(_Tiling, 0.01);
                float3 nWS = normalize(IN.normalWS);
                float3 n = abs(nWS);
                n /= (n.x + n.y + n.z + 1e-4);

                // Dominant-axis projection rather than a blend of all three.
                // Arena geometry is axis-aligned boxes, so on every face one
                // projection has weight ~1 anyway — and this evaluates the
                // pattern ONCE instead of three times, which matters a lot for
                // the noise-based styles on WebGL.
                float2 uv = (n.x > n.y && n.x > n.z) ? wpos.zy
                          : ((n.y > n.z) ? wpos.xz : wpos.xy);
                float3 s = StylePattern(uv, _Style);

                float mask   = s.x;
                float emit   = s.y;
                float height = s.z;

                float3 albedo = lerp(_BaseColor.rgb, _SecondColor.rgb, saturate(mask));

                // Bump without UVs or tangents: perturb the normal by the screen
                // -space gradient of the height field. Costs two derivatives
                // rather than extra pattern evaluations.
                float3 dpdx = ddx(IN.positionWS);
                float3 dpdy = ddy(IN.positionWS);
                float dhdx = ddx(height);
                float dhdy = ddy(height);
                float3 rx = cross(dpdy, nWS);
                float3 ry = cross(nWS, dpdx);
                // The determinant is signed — clamping it with max() would flip
                // the bump on back-facing screen orientations.
                float det = dot(dpdx, rx);
                det = (abs(det) > 1e-5) ? det : 1e-5;
                float3 surfGrad = (rx * dhdx + ry * dhdy) / det;
                // Guard against gradient spikes at style discontinuities.
                surfGrad = clamp(surfGrad, -4.0, 4.0);
                float3 bumped = normalize(nWS - _BumpStrength * surfGrad);

                // Cavity: recesses genuinely sit in shadow. Doing this rather
                // than only tinting them is most of why stone reads as stone.
                float cavity = lerp(1.0 - _Cavity, 1.0, saturate(height));

                float3 viewDir = GetWorldSpaceNormalizeViewDir(IN.positionWS);
                float gloss = 1.0 - saturate(_Roughness);
                float specPower = lerp(6.0, 110.0, gloss);
                float specScale = lerp(0.015, 0.5, gloss * gloss);

                Light mainLight = GetMainLight();
                float ndl = saturate(dot(bumped, mainLight.direction));
                float3 halfVec = normalize(mainLight.direction + viewDir);
                float spec = pow(saturate(dot(bumped, halfVec)), specPower) * specScale;
                float3 lighting = albedo * mainLight.color * ndl + mainLight.color * spec * ndl;

                // Ambient from the scene's own probe, so the per-arena
                // RenderSettings.ambientLight actually reaches these surfaces —
                // a hardcoded term would light a sunlit ziggurat and a black
                // void identically.
                lighting += albedo * SampleSH(bumped);

            #ifdef _ADDITIONAL_LIGHTS
                uint count = GetAdditionalLightsCount();
                for (uint li = 0u; li < count; li++)
                {
                    Light l = GetAdditionalLight(li, IN.positionWS);
                    float lndl = saturate(dot(bumped, l.direction));
                    float3 lh = normalize(l.direction + viewDir);
                    float lspec = pow(saturate(dot(bumped, lh)), specPower) * specScale;
                    lighting += (albedo * lndl + lspec * lndl) * l.color * l.distanceAttenuation;
                }
            #endif

                lighting *= cavity;

                float3 emission = _EmitColor.rgb * emit * _EmitStrength;
                return half4(lighting + emission, 1.0);
            }
            ENDHLSL
        }
    }
}
