Shader "PrismTactics/SpriteFill"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Fill ("Fill", Range(0,1)) = 1
        _FillDirection ("Fill Direction", Float) = 0
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

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            sampler2D _MainTex;
            fixed4 _Color;
            float _Fill;
            float _FillDirection;

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float fill = saturate(_Fill);

                if (_FillDirection < 0.5)
                {
                    if (i.uv.x > fill) discard;
                }
                else if (_FillDirection < 1.5)
                {
                    if (i.uv.x < 1.0 - fill) discard;
                }
                else if (_FillDirection < 2.5)
                {
                    if (i.uv.y > fill) discard;
                }
                else
                {
                    if (i.uv.y < 1.0 - fill) discard;
                }

                return tex2D(_MainTex, i.uv) * i.color * _Color;
            }
            ENDCG
        }
    }
}
