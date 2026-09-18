using MagicaCloth2;
using UnityEngine;

/// <summary>
/// Wet cloth hangs differently: water is weight, so a soaked skirt pulls down harder and stops
/// swinging - it clings instead of fluttering. Put this on the character; it finds every
/// MagicaCloth on her and, for each, the Wettable of the garment it moves, and bends the cloth's
/// gravity and damping by how wet that garment is.
///
/// Nothing is copied or cloned: the cloth's own parameters are nudged and MagicaCloth is told
/// they changed. Dry again, they return to what the artist set.
/// </summary>
public class WetCloth : MonoBehaviour
{
    [SerializeField] private float _heavier = 1.5f;      // soaked: gravity × (1 + this)
    [SerializeField] private float _calmer = 0.3f;       // soaked: damping + this (0..1)

    private MagicaCloth[] _cloths;
    private Wettable[] _garments;
    private float[] _dryGravity;
    private float[] _dryDamping;
    private float[] _applied;

    private void Start()
    {
        _cloths = GetComponentsInChildren<MagicaCloth>();
        _garments = new Wettable[_cloths.Length];
        _dryGravity = new float[_cloths.Length];
        _dryDamping = new float[_cloths.Length];
        _applied = new float[_cloths.Length];

        for (int i = 0; i < _cloths.Length; i++)
        {
            ClothSerializeData data = _cloths[i].SerializeData;
            _dryGravity[i] = data.gravity;
            _dryDamping[i] = data.damping.value;
            // The garment this cloth moves: the first renderer it lists that can get wet, else
            // any Wettable under the cloth's own object.
            foreach (Renderer renderer in data.sourceRenderers)
                if (renderer != null && renderer.TryGetComponent(out Wettable wettable)) { _garments[i] = wettable; break; }
            if (_garments[i] == null) _garments[i] = _cloths[i].GetComponentInChildren<Wettable>();
        }
    }

    private void Update()
    {
        for (int i = 0; i < _cloths.Length; i++)
        {
            if (_garments[i] == null) continue;
            float wet = _garments[i].Average;
            if (Mathf.Abs(wet - _applied[i]) < 0.02f) continue;   // MagicaCloth rebuilds on every change: only when it matters
            _applied[i] = wet;

            ClothSerializeData data = _cloths[i].SerializeData;
            data.gravity = _dryGravity[i] * (1f + _heavier * wet);
            data.damping.SetValue(Mathf.Clamp01(_dryDamping[i] + _calmer * wet));
            _cloths[i].SetParameterChange();
        }
    }
}
