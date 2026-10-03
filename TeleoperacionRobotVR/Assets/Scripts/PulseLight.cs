using UnityEngine;

/// Hace "respirar" una luz y/o la emisión de un material (balizas, luces de la base, plataforma de entrega).
public class PulseLight : MonoBehaviour
{
    public Light target;
    public Renderer emissive;
    public Color emissionColor = new Color(1f, 0.45f, 0.1f);
    public float minIntensity = 0.3f;
    public float maxIntensity = 2.5f;
    public float speed = 1.5f;
    public float phase;

    MaterialPropertyBlock block;
    float baseLight;

    void Awake()
    {
        if (!target) target = GetComponent<Light>();
        if (target) baseLight = target.intensity;
        block = new MaterialPropertyBlock();
    }

    void Update()
    {
        float s = 0.5f + 0.5f * Mathf.Sin((Time.time * speed + phase) * Mathf.PI * 2f);
        float k = Mathf.Lerp(minIntensity, maxIntensity, s);
        if (target) target.intensity = baseLight * k / Mathf.Max(maxIntensity, 0.01f);
        if (emissive)
        {
            emissive.GetPropertyBlock(block);
            block.SetColor("_EmissionColor", emissionColor * k);
            emissive.SetPropertyBlock(block);
        }
    }
}
