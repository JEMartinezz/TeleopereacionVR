// Cielo marciano: degradado ocre (cenit marrón, horizonte color caramelo) con disco solar y halo azulado
// alrededor del Sol, como se observa en Marte por la dispersión del polvo.
Shader "Teleop/MarsSky"
{
    Properties
    {
        _Zenith ("Cenit", Color) = (0.36, 0.22, 0.17, 1)
        _Horizon ("Horizonte", Color) = (0.92, 0.66, 0.48, 1)
        _Ground ("Suelo", Color) = (0.42, 0.26, 0.18, 1)
        _HorizonSharp ("Nitidez del horizonte", Float) = 3
        _SunColor ("Sol", Color) = (1, 0.95, 0.88, 1)
        _HaloColor ("Halo", Color) = (0.62, 0.74, 0.9, 1)
        _SunSize ("Tamaño del Sol", Range(0.99, 0.99999)) = 0.9993
        _HaloPower ("Concentración del halo", Float) = 18
        _HaloStrength ("Intensidad del halo", Float) = 0.55
        _Exposure ("Exposición", Float) = 1.0
        _SunDir ("Dirección hacia el Sol", Vector) = (0.3, 0.45, 0.6, 0)
    }
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" }
        Cull Off ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Zenith, _Horizon, _Ground, _SunColor, _HaloColor;
            float4 _SunDir;
            float _HorizonSharp, _SunSize, _HaloPower, _HaloStrength, _Exposure;

            struct v2f { float4 pos : SV_POSITION; float3 dir : TEXCOORD0; };

            v2f vert (appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.dir = v.vertex.xyz;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float h = d.y;
                float3 col = h >= 0 ? lerp(_Horizon.rgb, _Zenith.rgb, 1 - pow(1 - h, _HorizonSharp))
                                    : lerp(_Horizon.rgb, _Ground.rgb, saturate(-h * 6));

                float3 sunDir = normalize(_SunDir.xyz + float3(0, 1e-4, 0));
                float s = dot(d, sunDir);
                float halo = pow(saturate(s), _HaloPower) * _HaloStrength;
                col = lerp(col, _HaloColor.rgb, saturate(halo));
                col += _SunColor.rgb * smoothstep(_SunSize - 0.0004, _SunSize, s) * 4;
                col += _SunColor.rgb * pow(saturate(s), 300) * 0.6;
                return fixed4(max(col * _Exposure, 0), 1);
            }
            ENDCG
        }
    }
    Fallback Off
}
