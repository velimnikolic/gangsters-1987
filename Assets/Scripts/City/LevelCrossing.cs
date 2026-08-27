using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class LevelCrossing : MonoBehaviour
{

    public GameObject Barrier;
    public MeshRenderer meshRenderer;

    // The warning lamp is the third material slot of the crossing's renderer. Its colour goes
    // through a property block rather than .materials, which would instantiate a private copy
    // of every material on the renderer and take it out of batching.
    private const int LampMaterialIndex = 2;
    private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

    private MaterialPropertyBlock _lamp;

    public void SetLampColor(Color color)
    {
        if (!meshRenderer)
            return;

        if (_lamp == null)
            _lamp = new MaterialPropertyBlock();

        meshRenderer.GetPropertyBlock(_lamp, LampMaterialIndex);
        _lamp.SetColor(EmissionColor, color);
        meshRenderer.SetPropertyBlock(_lamp, LampMaterialIndex);
    }

    public void ChangeBarrier(bool open)
    {
        StopAllCoroutines();
        if (open)
            StartCoroutine(OpenBarrier());
        else
            StartCoroutine(CloseBarrier());
    }
    private IEnumerator CloseBarrier()
    {
        while (Mathf.FloorToInt(Barrier.transform.localRotation.eulerAngles.z) != 0)
        {
            Barrier.transform.localRotation = Quaternion.RotateTowards(Barrier.transform.localRotation, Quaternion.Euler(0,0,0),1f);
            yield return null;
        }
        yield break;
    }
    private IEnumerator OpenBarrier()
    {
        while (Mathf.FloorToInt(Barrier.transform.localRotation.eulerAngles.z) != 270)
        {
            Barrier.transform.localRotation = Quaternion.RotateTowards(Barrier.transform.localRotation, Quaternion.Euler(0, 0, -90), 1f);
            yield return null;
        }
        yield break;
    }
}
