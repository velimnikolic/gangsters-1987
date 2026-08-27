using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TrafficLight : MonoBehaviour
{
    // Start is called before the first frame update
    public bool isGreen;

    public delegate void LightChange(bool isGreen);
    public LightChange lightChange;
    public Crosswalk crosswalk;

    // The lamp is the second material slot of the light's renderer. Its colour goes through
    // a property block rather than .materials, which would instantiate a private copy of every
    // material on the renderer and take it out of batching.
    private const int LampMaterialIndex = 1;
    private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

    private Renderer _lampRenderer;
    private MaterialPropertyBlock _lamp;

    private void Awake()
    {
        lightChange += ChangeCrosswalk;
    }

    private void ChangeCrosswalk(bool isGreen)
    {
        // Assigned, never toggled: a repeated or missed invoke must not invert the crossing.
        if (crosswalk)
            crosswalk.CanCross = !isGreen;
    }

    public void SetLampColor(Color color)
    {
        if (!_lampRenderer)
            _lampRenderer = GetComponent<Renderer>();
        if (!_lampRenderer)
            return;

        if (_lamp == null)
            _lamp = new MaterialPropertyBlock();

        _lampRenderer.GetPropertyBlock(_lamp, LampMaterialIndex);
        _lamp.SetColor(EmissionColor, color);
        _lampRenderer.SetPropertyBlock(_lamp, LampMaterialIndex);
    }

}
