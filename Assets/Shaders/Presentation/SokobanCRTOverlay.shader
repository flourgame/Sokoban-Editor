// HLSL adaptation of Balatro Raw/shaders/CRT.fs (LocalThunk / Playstack).
// Ported: cross-axis bulge, feathered screen mask, RGB phosphor scanlines,
// horizontal chromatic samples and quantized noise. See Attribution.md.
// A single late UI pass samples the completed player UI, including popups.
Shader "UI/SokobanCRTOverlay"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        _Intensity ("Intensity", Range(0, 1)) = 0.22
        _Scanlines ("Scanline Rows", Float) = 180
        _Curvature ("Screen Curvature", Range(0, 0.2)) = 0.0075
        _Feather ("Screen Edge Feather", Range(0.001, 0.1)) = 0.025
        _Vignette ("Vignette", Range(0, 1)) = 0.32
        _NoiseAmount ("Noise", Range(0, 0.1)) = 0.006
        _Chromatic ("Chromatic Aberration", Range(0, 1)) = 0.35
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" }
        Cull Off
        Lighting Off
        ZWrite Off
        ZTest Always
        Blend Off

        // ScreenSpaceOverlay canvases bypass camera image effects. Capture once
        // after all player canvases, before the separate scene-transition canvas.
        GrabPass { "_SokobanCrtScreen" }
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float4 grabPosition : TEXCOORD0;
            };
            sampler2D _SokobanCrtScreen;
            float _Intensity, _Scanlines, _Curvature, _Feather;
            float _Vignette, _NoiseAmount, _Chromatic;
            float _SokobanAnimationTime;

            v2f vert(appdata_base v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.grabPosition = ComputeGrabScreenPos(o.vertex);
                return o;
            }

            float3 SampleScreen(float2 uv)
            {
                return tex2D(_SokobanCrtScreen, saturate(uv)).rgb;
            }

            float4 frag(v2f i) : SV_Target
            {
                float2 screenUV = i.grabPosition.xy / i.grabPosition.w;
                float2 centered = screenUV * 2.0 - 1.0;
                // Same mapping as CRT.fs: (distortion_fac - 1) = curvature.
                // SokobanCrtOverlay.ScreenToSource mirrors this for UI input.
                float2 curved = centered + centered.yx * centered.yx * centered * _Curvature;
                float2 edge = 1.0 - smoothstep(1.0 - _Feather, 1.0, abs(curved) - 0.01);
                float mask = edge.x * edge.y;
                float2 tc = (curved + 1.0) * 0.5;
                float3 crtTex = SampleScreen(tc);
                float crtIntensity = _Intensity * 0.36;

                // Original RGB sample offsets, scaled to physical pixels.
                float shift = 0.8 / _ScreenParams.x;
                if (_Chromatic > 0.001)
                {
                    crtTex.r = lerp(crtTex.r, SampleScreen(tc + float2(shift, 0)).r, _Chromatic);
                    crtTex.g = lerp(crtTex.g, SampleScreen(tc - float2(shift, 0)).g, _Chromatic);
                }

                // CRT.fs takes angular frequency; our public parameter is rows.
                // Six screen pixels per row at normal quality (formerly 760 rows).
                float frequency = _Scanlines * 6.2831853;
                float horizontal = tc.y * frequency;
                float vertical = tc.x * frequency * 4.0;
                // Fade subpixel phosphor columns instead of aliasing at narrow sizes.
                float columnVisibility = 1.0 - smoothstep(2.0, 3.1415927, fwidth(vertical));
                float3 columns = float3(
                    clamp(sin(vertical), 0.4, 1.0),
                    clamp(cos(vertical), 0.0, 1.0),
                    clamp(cos(vertical - 3.1415927 / 4.0), 0.0, 1.0));
                columns = lerp(float3(0.6, 0.4, 0.4), columns, columnVisibility);
                float3 rgbScanline = clamp(-0.3 + 2.0 * float3(
                    sin(horizontal - 3.1415927 / 4.0),
                    cos(horizontal),
                    cos(horizontal - 3.1415927 / 3.0)) - 0.8 * columns, -1.0, 2.0);
                float3 result = crtTex * (1.0 - crtIntensity) + crtTex * rgbScanline * crtIntensity;

                // Quantized noise from CRT.fs, tuned down for the dark HUD.
                float noiseTime = _SokobanAnimationTime + 1.0;
                float x = (floor(tc.x / 0.002) * 0.002) * (floor(tc.y / 0.0013) * 0.0013) * noiseTime * 1000.0;
                x = fmod(x, 13.0) * fmod(x, 123.0);
                float noise = fmod(x, 0.11) / 0.11;
                result = lerp(result, noise.xxx, _NoiseAmount);

                // Gentle falloff preserves the dark HUD's readability.
                float vignette = 1.0 - _Vignette * smoothstep(0.18, 1.8, dot(centered, centered));
                return float4(max(0.0, result) * vignette * mask, 1.0);
            }
            ENDCG
        }
    }
}
