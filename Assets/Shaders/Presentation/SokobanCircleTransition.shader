Shader "UI/SokobanCircleTransition"
{
    Properties
    {
        _Color ("Color", Color) = (0.03, 0.02, 0.04, 1)
        _Radius ("Radius", Range(0, 2)) = 1.5
        _Feather ("Feather", Range(0.001, 0.25)) = 0.035
        _Aspect ("Aspect", Float) = 1.777
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "Queue"="Overlay" "IgnoreProjector"="True" "RenderType"="Transparent" }
        Cull Off Lighting Off ZWrite Off ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct appdata_t { float4 vertex : POSITION; float4 color : COLOR; float2 texcoord : TEXCOORD0; };
            struct v2f { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            fixed4 _Color; float _Radius, _Feather, _Aspect;
            v2f vert(appdata_t v) { v2f o; o.vertex=UnityObjectToClipPos(v.vertex); o.uv=v.texcoord; o.color=v.color; return o; }
            fixed4 frag(v2f i) : SV_Target
            {
                float2 p = i.uv - 0.5; p.x *= _Aspect;
                float d = length(p);
                // The overlay is black outside the circular opening. Radius 0 covers
                // the whole screen; increasing it expands the opening and reveals
                // the newly loaded scene.
                float alpha = smoothstep(_Radius, _Radius + _Feather, d);
                return fixed4(_Color.rgb, alpha * _Color.a * i.color.a);
            }
            ENDCG
        }
    }
}
