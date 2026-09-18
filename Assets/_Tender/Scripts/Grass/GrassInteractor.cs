using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>What an object does to the grass it touches.</summary>
public enum GrassEffect
{
    /// <summary>The grass leans out of the way and stands back up once let go. A player, an animal, a ball.</summary>
    Bend,

    /// <summary>The grass is pressed down and slowly grows back. Feet on a path, a cart, a lying deer.</summary>
    Flatten,

    /// <summary>No grass at all inside. A house, a rock, a road. Grows back slowly if the object goes away.</summary>
    Remove
}

/// <summary>
/// Makes this object touch the grass. Put it on anything with colliders - the player, a boulder, a house.
///
/// The shape comes from the object's colliders, whatever they are: a player's CharacterController, a
/// house's box, a rock made of three spheres. Box, sphere, capsule and CharacterController are used
/// exactly; any other collider counts as its bounding box. Nothing has to be registered anywhere - an
/// enabled interactor is found by the grass on its own.
///
/// This decides WHAT the object does and how fast it presses in. How fast the grass recovers is up to
/// the grass (GrassInteraction on the grass field): a path trodden by a player and one rolled flat by a
/// cart grow back the same way.
/// </summary>
[ExecuteAlways]
public class GrassInteractor : MonoBehaviour
{
    /// <summary>Every enabled interactor. GrassInteraction reads this each frame.</summary>
    public static readonly List<GrassInteractor> All = new List<GrassInteractor>();

    [Tooltip("Bend: grass leans away and stands back up once let go - a player, an animal. Flatten: pressed down, grows back slowly - leaves a path. Remove: no grass inside at all - a house, a rock.")]
    [SerializeField] private GrassEffect _effect = GrassEffect.Bend;

    [Tooltip("How far past the outline of the colliders the grass still feels it, in metres. Grass leans away from a foot before the foot arrives.")]
    [SerializeField] private float _reach = 0.4f;

    [Tooltip("How much, at full contact: 1 leans the grass right over, flattens it completely, or clears it completely.")]
    [Range(0f, 1f)][SerializeField] private float _strength = 1f;

    [Tooltip("Seconds to take full effect. 0 is instant - a house. A tenth or two of a second is a foot pressing in.")]
    [SerializeField] private float _pressTime = 0.1f;

    [Tooltip("The colliders that give the shape. Left empty, every collider on this object and its children is used.")]
    [SerializeField] private Collider[] _colliders = new Collider[0];

    private readonly List<Collider> _found = new List<Collider>();

    /// <summary>One shape as the GPU reads it. Laid out exactly like GrassShape in GrassInteraction.hlsl.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Gpu
    {
        public const int Stride = 28 * sizeof(float);

        public Vector3 Centre; public float Shape;
        public Vector3 Size; public float Radius;
        public Vector3 AxisX; public float Reach;
        public Vector3 AxisY; public float Strength;
        public Vector3 AxisZ; public float Effect;
        public Vector3 BoundsCentre; public float BoundsRadius;
        public float PressRate; public Vector3 Unused;
    }

    private const float Sphere = 0f, Capsule = 1f, Box = 2f;

    private void OnEnable() => All.Add(this);
    private void OnDisable() => All.Remove(this);

    /// <summary>Add this object's shapes to the list, stopping at `max`.</summary>
    public void CollectShapes(List<Gpu> shapes, int max)
    {
        _found.Clear();
        if (_colliders.Length > 0) _found.AddRange(_colliders);
        else GetComponentsInChildren(false, _found);

        foreach (Collider collider in _found)
        {
            if (shapes.Count >= max) return;
            if (collider == null || !collider.enabled || collider is TerrainCollider || collider is WheelCollider) continue;
            shapes.Add(ToShape(collider));
        }
    }

    private Gpu ToShape(Collider collider)
    {
        Transform t = collider.transform;
        Vector3 scale = Abs(t.lossyScale);

        // Axes for boxes, identity-ish for everything else; the GPU only reads them for boxes.
        var shape = new Gpu
        {
            AxisX = t.right, AxisY = t.up, AxisZ = t.forward,
            Reach = Mathf.Max(_reach, 0.01f),
            Strength = _strength,
            Effect = (float)_effect,
            PressRate = 1f / Mathf.Max(_pressTime, 0.0001f)
        };

        // Worked out from the collider's own numbers, NOT from collider.bounds. Bounds come from the
        // physics scene, which only catches up with a moved transform at its next sync - a frame late
        // for anything moving, and all zeros for an object made in the editor a moment ago.
        switch (collider)
        {
            case SphereCollider sphere:
                shape.Shape = Sphere;
                shape.Centre = t.TransformPoint(sphere.center);
                shape.Radius = sphere.radius * Mathf.Max(scale.x, Mathf.Max(scale.y, scale.z));
                SetBounds(ref shape, shape.Centre, shape.Radius);
                break;

            case CapsuleCollider capsule:
                SetCapsule(ref shape, t, capsule.center, capsule.direction, capsule.height, capsule.radius, scale);
                break;

            case CharacterController character:
                SetCapsule(ref shape, t, character.center, 1, character.height, character.radius, scale);
                break;

            case BoxCollider box:
                shape.Shape = Box;
                shape.Centre = t.TransformPoint(box.center);
                shape.Size = Vector3.Scale(box.size * 0.5f, scale);
                SetBounds(ref shape, shape.Centre, shape.Size.magnitude);
                break;

            case MeshCollider mesh when mesh.sharedMesh != null:
                // The mesh's own bounding box, turned with the object: a better fit than the world one.
                shape.Shape = Box;
                shape.Centre = t.TransformPoint(mesh.sharedMesh.bounds.center);
                shape.Size = Vector3.Scale(mesh.sharedMesh.bounds.extents, scale);
                SetBounds(ref shape, shape.Centre, shape.Size.magnitude);
                break;

            default:
                // Anything else: its bounding box in the world.
                shape.Shape = Box;
                shape.AxisX = Vector3.right; shape.AxisY = Vector3.up; shape.AxisZ = Vector3.forward;
                shape.Centre = collider.bounds.center;
                shape.Size = collider.bounds.extents;
                SetBounds(ref shape, shape.Centre, shape.Size.magnitude);
                break;
        }

        return shape;
    }

    /// <summary>The sphere the GPU uses to skip this shape quickly: round the shape, plus its reach.</summary>
    private static void SetBounds(ref Gpu shape, Vector3 centre, float radius)
    {
        shape.BoundsCentre = centre;
        shape.BoundsRadius = radius + shape.Reach;
    }

    /// <summary>A capsule is a segment and a radius. Unity describes it as a height along one local axis.</summary>
    private static void SetCapsule(ref Gpu shape, Transform t, Vector3 centre, int direction, float height, float radius, Vector3 scale)
    {
        Vector3 axis = direction == 0 ? t.right : direction == 1 ? t.up : t.forward;
        float along = direction == 0 ? scale.x : direction == 1 ? scale.y : scale.z;
        float across = direction == 0 ? Mathf.Max(scale.y, scale.z) : direction == 1 ? Mathf.Max(scale.x, scale.z) : Mathf.Max(scale.x, scale.y);

        float worldRadius = radius * across;
        float halfSegment = Mathf.Max(height * 0.5f * along - worldRadius, 0f);
        Vector3 middle = t.TransformPoint(centre);

        shape.Shape = Capsule;
        shape.Radius = worldRadius;
        shape.Centre = middle - axis * halfSegment;
        shape.Size = middle + axis * halfSegment;
        SetBounds(ref shape, middle, halfSegment + worldRadius);
    }

    private static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

    private void OnDrawGizmosSelected()
    {
        // How far out the grass feels this object, round each collider's bounds.
        Gizmos.color = _effect == GrassEffect.Remove ? new Color(1f, 0.4f, 0.3f, 0.8f)
                     : _effect == GrassEffect.Flatten ? new Color(1f, 0.85f, 0.3f, 0.8f)
                     : new Color(0.5f, 1f, 0.5f, 0.8f);

        _found.Clear();
        if (_colliders.Length > 0) _found.AddRange(_colliders);
        else GetComponentsInChildren(false, _found);

        foreach (Collider collider in _found)
        {
            if (collider == null) continue;
            Bounds bounds = collider.bounds;
            Gizmos.DrawWireCube(bounds.center, bounds.size + Vector3.one * (2f * _reach));
        }
    }
}
