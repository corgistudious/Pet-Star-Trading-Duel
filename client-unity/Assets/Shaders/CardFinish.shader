// Cosmetic card finishes (Foil / Holographic / Shiny / Prismatic) rendered as an additive UI overlay over the art window.
// Finishes are collectible aesthetics only and never affect combat.
Shader "PetStar/UI/CardFinish"
{
    Properties
    {
        [PerRendererData] _MainTex ("Mask (sliced rounded rect)", 2D) = "white" {}
        _Mode ("Mode 0 Foil 1 Holo 2 Shiny 3 Prismatic", Float) = 1
        _Tilt ("Tilt", Vector) = (0,0,0,0)
        _Intensity ("Intensity", Range(0,1)) = 0.55
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="True" }
        Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
        Cull Off Lighting Off ZWrite Off ZTest [unity_GUIZTestMode]
        Blend One One
        ColorMask [_ColorMask]

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct appdata { float4 vertex : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; float4 world : TEXCOORD1; };

            sampler2D _MainTex; float _Mode; float4 _Tilt; float _Intensity; float4 _ClipRect;

            v2f vert (appdata v) { v2f o; o.world = v.vertex; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.uv; o.color = v.color; return o; }

            float3 hue(float h) { h = frac(h); return saturate(abs(fmod(h * 6 + float3(0,4,2), 6) - 3) - 1); }

            fixed4 frag (v2f i) : SV_Target
            {
                float mask = tex2D(_MainTex, i.uv).a * UnityGet2DClipping(i.world.xy, _ClipRect);
                float2 p = i.uv + _Tilt.xy * 0.25;
                float band = p.x * 0.8 + p.y * 1.2;
                float3 c;
                if (_Mode < 0.5)        // Foil: bright silver streak
                    c = pow(saturate(1 - abs(frac(band * 1.5 + _Time.y * 0.05) - 0.5) * 6), 3) * float3(1,1,1);
                else if (_Mode < 1.5)   // Holographic: rainbow bands
                    c = hue(band * 1.6 + _Tilt.x * 0.3) * (0.55 + 0.45 * sin(band * 40));
                else if (_Mode < 2.5)   // Shiny: warm gold sparkle field
                {
                    float2 g = floor(i.uv * 28); float r = frac(sin(dot(g, float2(12.9898,78.233))) * 43758.5453);
                    float tw = step(0.93, r) * (0.5 + 0.5 * sin(_Time.y * 4 + r * 40));
                    c = float3(1, 0.85, 0.45) * (tw + 0.15 * saturate(1 - abs(frac(band) - 0.5) * 4));
                }
                else                    // Prismatic: rainbow + sparkle
                {
                    float2 g = floor(i.uv * 36); float r = frac(sin(dot(g, float2(39.3468,11.135))) * 24634.6345);
                    c = hue(band * 2.2 + _Time.y * 0.08) * 0.7 + step(0.95, r) * (0.6 + 0.4 * sin(_Time.y * 5 + r * 30));
                }
                return fixed4(c * mask * _Intensity * i.color.a, 0);
            }
            ENDCG
        }
    }
}
