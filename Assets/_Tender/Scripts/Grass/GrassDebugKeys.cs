using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// TEMPORARY diagnostic: switches suspects off one at a time in Play Mode, to find what draws the
/// V-shaped shape that follows the camera over the grass while walking. Delete once it is found,
/// together with the _GrassDebugEye lines marked DEBUG-TEMP in GrassTypes.hlsl and GrassPlacement.compute.
///
/// Keys 1-9 toggle; the panel in the top-left corner shows what is off.
/// </summary>
public class GrassDebugKeys : MonoBehaviour
{
    [SerializeField] private ComputeShader _placement;
    [SerializeField] private Material _bladeMaterial;
    [SerializeField] private ParticleSystemRenderer _windStreaks;

    private static readonly int DebugEyeId = Shader.PropertyToID("_GrassDebugEye");

    private readonly string[] _names =
    {
        "1  LOD / rings frozen where you stood (walk ~10 m)",
        "2  Player's GrassInteractors off",
        "3  Sun shadows off",
        "4  Volumetric haze (Atmosphere) off",
        "5  Cloud shadows off",
        "6  Blade edge thickening off",
        "7  Grass translucency + sheen off",
        "8  Wind streaks off",
        "9  Post-processing off"
    };

    private readonly bool[] _off = new bool[9];
    private Vector3 _frozenEye;
    private float _edgeThicken;
    private LightShadows _sunShadows;

    private void OnEnable()
    {
        if (_bladeMaterial != null) _edgeThicken = _bladeMaterial.GetFloat("_EdgeThicken");
        if (RenderSettings.sun != null) _sunShadows = RenderSettings.sun.shadows;
    }

    private void OnDisable()
    {
        for (int i = 0; i < _off.Length; i++)
            if (_off[i]) Toggle(i);
        SetEye(Vector3.zero);
    }

    private void Update()
    {
        for (int i = 0; i < _off.Length; i++)
            if (Input.GetKeyDown(KeyCode.Alpha1 + i)) Toggle(i);
    }

    private void LateUpdate()
    {
        Camera view = Camera.main;
        SetEye(_off[0] && view != null ? _frozenEye - view.transform.position : Vector3.zero);

        // GrassLook sets these every Update; LateUpdate runs after it and before the frame renders.
        if (_off[6])
        {
            Shader.SetGlobalFloat("_GrassTranslucency", 0f);
            Shader.SetGlobalFloat("_GrassSheen", 0f);
        }
    }

    private void Toggle(int index)
    {
        _off[index] = !_off[index];
        bool on = !_off[index];
        Camera view = Camera.main;

        switch (index)
        {
            case 0:
                if (view != null) _frozenEye = view.transform.position;
                break;
            case 1:
                foreach (GrassInteractor interactor in GetComponents<GrassInteractor>()) interactor.enabled = on;
                break;
            case 2:
                if (RenderSettings.sun != null) RenderSettings.sun.shadows = on ? _sunShadows : LightShadows.None;
                break;
            case 3:
                Atmosphere atmosphere = FindAnyObjectByType<Atmosphere>();
                if (atmosphere != null) atmosphere.enabled = on;
                break;
            case 4:
                CloudShadows clouds = FindAnyObjectByType<CloudShadows>();
                if (clouds != null) clouds.enabled = on;       // takes its cookie off the sun when disabled
                break;
            case 5:
                if (_bladeMaterial != null) _bladeMaterial.SetFloat("_EdgeThicken", on ? _edgeThicken : 0f);
                break;
            case 7:
                if (_windStreaks != null) _windStreaks.enabled = on;
                break;
            case 8:
                if (view != null) view.GetUniversalAdditionalCameraData().renderPostProcessing = on;
                break;
        }
    }

    private void SetEye(Vector3 offset)
    {
        Shader.SetGlobalVector(DebugEyeId, offset);
        if (_placement != null) _placement.SetVector(DebugEyeId, offset);
    }

    private void OnGUI()
    {
        GUILayout.BeginArea(new Rect(10, 10, 420, 260), GUI.skin.box);
        for (int i = 0; i < _names.Length; i++)
            GUILayout.Label((_off[i] ? "<b>[OFF]</b> " : "[ on ] ") + _names[i], new GUIStyle(GUI.skin.label) { richText = true });
        GUILayout.EndArea();
    }
}
