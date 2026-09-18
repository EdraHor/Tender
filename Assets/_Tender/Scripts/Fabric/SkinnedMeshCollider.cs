using UnityEngine;

/// <summary>
/// A MeshCollider that follows a SkinnedMeshRenderer as it animates, so raindrops (Rain.cs raycasts)
/// hit the garment where it is now, not where it was in the bind pose. The mesh is baked every few
/// frames: a garment is a few hundred vertices, so this is cheap, and a drop does not care about a
/// frame or two.
/// </summary>
[RequireComponent(typeof(SkinnedMeshRenderer), typeof(MeshCollider))]
public class SkinnedMeshCollider : MonoBehaviour
{
    [SerializeField] private int _everyFrames = 3;

    private SkinnedMeshRenderer _skin;
    private MeshCollider _collider;
    private Mesh _baked;

    private void OnEnable()
    {
        _skin = GetComponent<SkinnedMeshRenderer>();
        _collider = GetComponent<MeshCollider>();
        _baked = new Mesh { name = "Baked " + name };
    }

    private void OnDisable()
    {
        if (_baked != null) Destroy(_baked);
    }

    private void LateUpdate()
    {
        if (Time.frameCount % Mathf.Max(_everyFrames, 1) != 0) return;
        // useScale = true bakes in the MESH's own space (measured: a Mixamo dress comes out 1.5 cm
        // across, its transform is scaled x100), which is what a collider on the same transform
        // needs. With false the bake is in metres and the collider comes out a hundred times too big.
        _skin.BakeMesh(_baked, true);
        _collider.sharedMesh = null;
        _collider.sharedMesh = _baked;
    }
}
