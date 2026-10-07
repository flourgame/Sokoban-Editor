// 全屏 CRT 叠加层（纯叠加式：不采样底层画面）。
// 技法是扫描线 / 噪声 / 暗角 / 色差暗示，移植自 Balatro 的 CRT.fs 思路，
// 但用 ShaderLab/HLSL 重写，并且去掉了需要画面采样的曲率与 bloom。
//
// 两个 Pass：
//   1) Multiply —— 扫描线 + 暗角，把画面压暗（Blend DstColor Zero）
//   2) Additive —— 噪声 + 强度抖动提亮（Blend One One）
// 因此在 Overlay 画布上叠一层即可，不需要相机后处理，也不需要 GrabPass。
//
// 支持 uGUI 的 Mask / RectMask2D / Stencil，支持无贴图（overlay 不使用 _MainTex）。
Shader "Sokoban/UI/CRTOverlay"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}

        _ScanlineIntensity ("扫描线强度", Range(0, 1)) = 0.25
        _ScanlineDensity ("扫描线密度（条/屏高）", Float) = 400
        _VignetteIntensity ("暗角强度", Range(0, 3)) = 0.9
        _VignettePower ("暗角收拢", Range(0.5, 8)) = 2.6
        _NoiseIntensity ("噪声强度", Range(0, 1)) = 0.06
        _Aberration ("色差暗示", Range(0, 1)) = 0.05
        _FlickerIntensity ("亮度抖动", Range(0, 1)) = 0.03
        _TimeScale ("时间缩放", Float) = 1
        _LowEffect ("低特效模式", Range(0, 1)) = 0

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        ColorMask [_ColorMask]

        // ---------- Pass 1：扫描线 + 暗角（乘法压暗） ----------
        Pass
        {
            Name "CRT_MULTIPLY"
            Blend DstColor Zero

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            fixed4 _TextureSampleAdd;
            float4 _ClipRect;

            v2f vert(appdata_t v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.worldPosition = v.vertex;
                o.vertex = UnityObjectToClipPos(o.worldPosition);
                o.texcoord = v.texcoord;
                o.color = v.color;
                return o;
            }

            float _ScanlineIntensity;
            float _ScanlineDensity;
            float _VignetteIntensity;
            float _VignettePower;
            float _Aberration;
            float _FlickerIntensity;
            float _TimeScale;
            float _LowEffect;

            // 与 Balatro CRT.fs 同类做法：以屏幕像素坐标做行条纹；这里用 uv 比例保持分辨率无关。
            float ScanlineMask(float2 uv, float phaseOffset)
            {
                // 注意：不能用 line 作变量名，它是 HLSL 的几何着色器图元关键字。
                float row = uv.y * _ScanlineDensity + phaseOffset;
                // -1..1 的条纹，取负半周压暗
                float wave = sin(row * 6.2831853);
                return 1.0 - _ScanlineIntensity * saturate(-wave);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 uv = i.texcoord;

                if (_LowEffect > 0.5)
                {
                    // 低特效：只保留极轻的暗角，去掉条纹与色差。
                    float2 c0 = uv - 0.5;
                    float v0 = 1.0 - _VignetteIntensity * 0.35 * pow(saturate(dot(c0, c0) * 2.0), _VignettePower * 0.5);
                    #ifdef UNITY_UI_CLIP_RECT
                    v0 *= UnityGet2DClipping(i.worldPosition.xy, _ClipRect);
                    #endif
                    return fixed4(v0, v0, v0, 1);
                }

                float t = _Time * _TimeScale;

                // 色差暗示：R/B 通道的条纹相位左右错开，制造边缘发虚的观感。
                float ab = _Aberration * 2.0;
                float mR = ScanlineMask(uv, ab);
                float mG = ScanlineMask(uv, 0.0);
                float mB = ScanlineMask(uv, -ab);

                // 暗角：径向
                float2 c = uv - 0.5;
                float vig = 1.0 - _VignetteIntensity * pow(saturate(dot(c, c) * 2.0), _VignettePower * 0.5);
                vig = saturate(vig);

                // 亮度抖动
                float flicker = 1.0 - _FlickerIntensity * (0.5 + 0.5 * sin(t * 37.0));

                float3 mod = float3(mR, mG, mB) * vig * flicker;

                #ifdef UNITY_UI_CLIP_RECT
                float clip = UnityGet2DClipping(i.worldPosition.xy, _ClipRect);
                mod *= clip;
                // 乘法 Pass 在遮罩外必须输出 1（不改变画面），而不是 0（会整片变黑）。
                mod = lerp(float3(1, 1, 1), mod, clip);
                #endif

                return fixed4(mod, 1);
            }
            ENDCG
        }

        // ---------- Pass 2：噪声 + 提亮（加法） ----------
        Pass
        {
            Name "CRT_ADDITIVE"
            Blend One One

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float4 _ClipRect;

            v2f vert(appdata_t v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.worldPosition = v.vertex;
                o.vertex = UnityObjectToClipPos(o.worldPosition);
                o.texcoord = v.texcoord;
                o.color = v.color;
                return o;
            }

            float _NoiseIntensity;
            float _FlickerIntensity;
            float _TimeScale;
            float _LowEffect;

            // 廉价的确定性哈希噪声（避免采样噪声贴图）。
            float Hash(float2 p)
            {
                float3 q = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973));
                q += dot(q, q.yzx + 33.33);
                return frac((q.x + q.y) * q.z);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                if (_LowEffect > 0.5) return fixed4(0, 0, 0, 1);

                float t = _Time * _TimeScale;
                float2 uv = i.texcoord;

                // 每帧变化的像素噪声
                float n = Hash(floor(uv * _ScreenParams.xy * 0.5) + floor(t * 24.0));
                float noise = (n - 0.5) * _NoiseIntensity;

                // 极轻的整体提亮，抵消乘法 Pass 带来的压暗
                float lift = _FlickerIntensity * 0.25;

                float3 add = float3(noise + lift, noise + lift, noise + lift) * i.color.rgb;
                add = max(add, 0);

                #ifdef UNITY_UI_CLIP_RECT
                add *= UnityGet2DClipping(i.worldPosition.xy, _ClipRect);
                #endif

                return fixed4(add, 1);
            }
            ENDCG
        }
    }

    Fallback Off
}
