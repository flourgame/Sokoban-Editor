Shader "UI/SokobanBalatroPanel"
{
    Properties
    {
        _BorderColor ("Border", Color) = (0.94, 0.31, 0.20, 1)
        _HighlightColor ("Highlight", Color) = (1.00, 0.68, 0.25, 1)
        _BorderWidth ("Border Width", Range(0.002, 0.08)) = 0.018
        _PixelGrid ("Pixel Grid", Range(8, 128)) = 32
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        [HideInInspector] _Color ("Tint", Color) = (1, 1, 1, 1)
        [HideInInspector] _StencilComp ("Stencil Comparison", Float) = 8
        [HideInInspector] _Stencil ("Stencil ID", Float) = 0
        [HideInInspector] _StencilOp ("Stencil Operation", Float) = 0
        [HideInInspector] _StencilWriteMask ("Stencil Write Mask", Float) = 255
        [HideInInspector] _StencilReadMask ("Stencil Read Mask", Float) = 255
        [HideInInspector] _ColorMask ("Color Mask", Float) = 15
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" }
        Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
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
            float4 _BorderColor, _HighlightColor, _ClipRect;
            float _BorderWidth, _PixelGrid;
            float _SokobanAnimationTime;

            v2f vert(appdata_t v)
            {
                v2f o; o.vertex = UnityObjectToClipPos(v.vertex); o.color = v.color; o.uv = v.texcoord; o.worldPosition = v.vertex; return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 uv = i.uv;
                float edgeDistance = min(min(uv.x, 1.0 - uv.x), min(uv.y, 1.0 - uv.y));
                float border = 1.0 - smoothstep(_BorderWidth, _BorderWidth + 0.012, edgeDistance);
                float pixel = frac(sin(dot(floor(uv * _PixelGrid), float2(12.9898, 78.233))) * 43758.5453);
                float shimmer = 0.94 + 0.06 * sin(_SokobanAnimationTime * 1.4 + uv.x * 5.0 + uv.y * 3.0);
                float3 borderColor = lerp(_BorderColor.rgb, _HighlightColor.rgb, step(0.72, pixel));
                float3 baseColor = i.color.rgb * shimmer;
                float3 color = lerp(baseColor, borderColor, border);
                color += border * pixel * 0.025;
                float alpha = i.color.a * UnityGet2DClipping(i.worldPosition.xy, _ClipRect);
                return fixed4(color, alpha);
            }
            ENDCG
        }
    }
}
