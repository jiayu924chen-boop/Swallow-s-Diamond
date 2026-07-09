Shader "UI/White Fill Black Outline Text"
{
    Properties
    {
        [PerRendererData] _MainTex ("Font Texture", 2D) = "white" {}
        _FaceColor ("Face Color", Color) = (1, 1, 1, 1)
        _OutlineColor ("Outline Color", Color) = (0, 0, 0, 1)
        _OutlineWidth ("Outline Width", Range(0, 4)) = 1

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
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
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            fixed4 _FaceColor;
            fixed4 _OutlineColor;
            float _OutlineWidth;

            struct appdata_t
            {
                float4 vertex : POSITION;
                fixed4 color : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            v2f vert(appdata_t v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                o.texcoord = v.texcoord;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float center = tex2D(_MainTex, i.texcoord).a;
                float2 offset = _MainTex_TexelSize.xy * _OutlineWidth;

                float outline = center;
                outline = max(outline, tex2D(_MainTex, i.texcoord + float2(offset.x, 0)).a);
                outline = max(outline, tex2D(_MainTex, i.texcoord + float2(-offset.x, 0)).a);
                outline = max(outline, tex2D(_MainTex, i.texcoord + float2(0, offset.y)).a);
                outline = max(outline, tex2D(_MainTex, i.texcoord + float2(0, -offset.y)).a);
                outline = max(outline, tex2D(_MainTex, i.texcoord + float2(offset.x, offset.y)).a);
                outline = max(outline, tex2D(_MainTex, i.texcoord + float2(-offset.x, offset.y)).a);
                outline = max(outline, tex2D(_MainTex, i.texcoord + float2(offset.x, -offset.y)).a);
                outline = max(outline, tex2D(_MainTex, i.texcoord + float2(-offset.x, -offset.y)).a);

                fixed4 color;
                color.rgb = lerp(_OutlineColor.rgb, _FaceColor.rgb, saturate(center));
                color.a = saturate(outline) * i.color.a;
                return color;
            }
            ENDCG
        }
    }
}
