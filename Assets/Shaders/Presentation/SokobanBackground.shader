Shader "UI/SokobanBalatroBackground"
{
    Properties
    {
        _ColorA ("Color A", Color) = (0.13, 0.33, 0.34, 1)
        _ColorB ("Color B", Color) = (0.46, 0.21, 0.14, 1)
        _ColorC ("Color C", Color) = (0.035, 0.075, 0.085, 1)
        _Speed ("Speed", Range(0, 2)) = 0.4
        _Contrast ("Contrast", Range(0.3, 3)) = 1.5
        _SpinAmount ("Spin Amount", Range(0, 1)) = 0.3
        _Brightness ("Brightness", Range(0.1, 1)) = 0.7
        _Pixelation ("Pixelation", Range(120, 1400)) = 700
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        [HideInInspector] _StencilComp ("Stencil Comparison", Float) = 8
        [HideInInspector] _Stencil ("Stencil ID", Float) = 0
        [HideInInspector] _StencilOp ("Stencil Operation", Float) = 0
        [HideInInspector] _StencilWriteMask ("Stencil Write Mask", Float) = 255
        [HideInInspector] _StencilReadMask ("Stencil Read Mask", Float) = 255
        [HideInInspector] _ColorMask ("Color Mask", Float) = 15
    }
    SubShader
    {
        Tags { "Queue"="Background" "IgnoreProjector"="True" "RenderType"="Transparent" }
        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }
        Cull Off Lighting Off ZWrite Off ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct appdata_t { float4 vertex : POSITION; float4 color : COLOR; float2 texcoord : TEXCOORD0; };
            struct v2f { float4 vertex : SV_POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; float4 worldPosition : TEXCOORD1; };
            fixed4 _ColorA, _ColorB, _ColorC;
            float _Speed, _Contrast, _Pixelation, _SpinAmount, _Brightness;
            float _SokobanAnimationTime;
            float4 _ClipRect;

            v2f vert(appdata_t v)
            {
                v2f o; o.worldPosition = v.vertex; o.vertex = UnityObjectToClipPos(v.vertex); o.uv = v.texcoord; o.color = v.color; return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // Direct HLSL port of Balatro resources/shaders/background.fs.
                // Keep its five paint iterations and mixing equations intact.
                float screenLength = length(_ScreenParams.xy);
                float pixelSize = screenLength / _Pixelation;
                float2 screenCoords = i.uv * _ScreenParams.xy;
                float2 uv = (floor(screenCoords / pixelSize) * pixelSize - 0.5 * _ScreenParams.xy) / screenLength - float2(0.12, 0.0);
                float uvLength = length(uv);
                float speed = (_SokobanAnimationTime * _Speed * 0.5 * 0.2) + 302.2;
                float angle = atan2(uv.y, uv.x) + speed - 0.5 * 20.0 * (_SpinAmount * uvLength + (1.0 - _SpinAmount));
                uv = float2(uvLength * cos(angle), uvLength * sin(angle));
                uv *= 30.0;
                speed = _SokobanAnimationTime * _Speed * 2.0;
                float2 uv2 = float2(uv.x + uv.y, uv.x + uv.y);
                [unroll] for (int passIndex = 0; passIndex < 5; passIndex++)
                {
                    uv2 += sin(max(uv.x, uv.y)) + uv;
                    uv += 0.5 * float2(cos(5.1123314 + 0.353 * uv2.y + speed * 0.131121), sin(uv2.x - 0.113 * speed));
                    uv -= cos(uv.x + uv.y) - sin(uv.x * 0.711 - uv.y);
                }
                float contrastMod = 0.25 * _Contrast + 0.5 * _SpinAmount + 1.2;
                float paint = clamp(length(uv) * 0.035 * contrastMod, 0.0, 2.0);
                float c1 = max(0.0, 1.0 - contrastMod * abs(1.0 - paint));
                float c2 = max(0.0, 1.0 - contrastMod * abs(paint));
                float c3 = 1.0 - min(1.0, c1 + c2);
                float3 col = (0.3 / _Contrast) * _ColorA.rgb + (1.0 - 0.3 / _Contrast) * (_ColorA.rgb * c1 + _ColorB.rgb * c2 + _ColorC.rgb * c3);
                col *= _Brightness;
                float alpha = UnityGet2DClipping(i.worldPosition.xy, _ClipRect);
                return fixed4(col, alpha);
            }
            ENDCG
        }
    }
}
