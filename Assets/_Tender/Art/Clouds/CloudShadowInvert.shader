Shader "Hidden/Tender/CloudShadowInvert"
{
    // Turns a rendered cloud into a light cookie.
    //
    // The cloud renders with alpha = how much of it covers this texel (1 = solid). A light
    // cookie means the opposite: 1 = full sun, 0 = fully shadowed. So this pass is just the
    // flip, done as a blit because it is a single instruction and not worth a render feature.
    Properties { _MainTex ("Texture", 2D) = "white" {} }

    SubShader
    {
        Cull Off  ZWrite Off  ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f     { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            sampler2D _MainTex;
            float _ShadowStrength;

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed coverage = tex2D(_MainTex, i.uv).a;
                // _ShadowStrength lets a cloud shadow the ground without blacking it out -
                // in reality plenty of light still reaches the ground under a cumulus.
                fixed light = 1.0 - coverage * _ShadowStrength;
                return fixed4(light, light, light, light);
            }
            ENDCG
        }
    }
}
