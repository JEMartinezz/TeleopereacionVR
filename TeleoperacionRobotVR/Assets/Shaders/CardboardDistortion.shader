// Compone la imagen estéreo para un visor tipo Cardboard:
// ojo izquierdo | ojo derecho, con pre-distorsión de barril que compensa la distorsión de cojín de los lentes.
//   p' = p · (1 + k1·r² + k2·r⁴) / zoom      (p centrado en el lente de cada ojo)
Shader "Hidden/Teleop/CardboardDistortion"
{
    Properties
    {
        _LeftTex ("Ojo izquierdo", 2D) = "black" {}
        _RightTex ("Ojo derecho", 2D) = "black" {}
        _K1 ("k1", Float) = 0.22
        _K2 ("k2", Float) = 0.24
        _Zoom ("Zoom", Float) = 1.0
        _EyeAspect ("Aspecto de cada ojo", Float) = 0.889
    }
    SubShader
    {
        ZTest Always Cull Off ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _LeftTex;
            sampler2D _RightTex;
            float _K1, _K2, _Zoom, _EyeAspect;

            fixed4 frag (v2f_img i) : SV_Target
            {
                bool right = i.uv.x >= 0.5;
                float2 uv = float2(right ? i.uv.x * 2 - 1 : i.uv.x * 2, i.uv.y);

                // Coordenadas centradas en el lente: vertical en [-1, 1], horizontal escalada por el aspecto
                float2 p = (uv - 0.5) * 2.0;
                p.x *= _EyeAspect;
                float r2 = dot(p, p);
                float2 q = p * (1.0 + _K1 * r2 + _K2 * r2 * r2) / _Zoom;
                q.x /= _EyeAspect;
                float2 src = q * 0.5 + 0.5;

                if (src.x < 0 || src.x > 1 || src.y < 0 || src.y > 1) return fixed4(0, 0, 0, 1);
                fixed4 c = right ? tex2D(_RightTex, src) : tex2D(_LeftTex, src);

                // Viñeta suave en el borde del lente
                float edge = saturate(1.0 - smoothstep(0.85, 1.0, max(abs(src.x - 0.5), abs(src.y - 0.5)) * 2.0));
                return fixed4(c.rgb * edge, 1);
            }
            ENDCG
        }
    }
}
