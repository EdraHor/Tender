#ifndef TENDER_GRASS_INTERACTION_INCLUDED
#define TENDER_GRASS_INTERACTION_INCLUDED

// The shapes that touch the grass, and how much each touches the grass at a spot.
//
// Every GrassInteractor turns its colliders into these shapes (GrassInteractor.Gpu on the C# side, same
// layout). GrassInteraction.compute paints them into the map of touched grass round the camera, and
// GrassPlacement.compute asks the REMOVE shapes directly, so a house far past the map still has no
// grass growing through its floor.

#define GRASS_SHAPE_SPHERE  0
#define GRASS_SHAPE_CAPSULE 1
#define GRASS_SHAPE_BOX     2

#define GRASS_EFFECT_BEND    0
#define GRASS_EFFECT_FLATTEN 1
#define GRASS_EFFECT_REMOVE  2

struct GrassShape
{
    float3 centre;       float shape;       // centre (sphere, box) or one end (capsule); GRASS_SHAPE_*
    float3 size;         float radius;      // the other end (capsule) or half its size (box); radius (sphere, capsule)
    float3 axisX;        float reach;       // the box's own axes, world space; metres past the outline it is felt
    float3 axisY;        float strength;    // 0..1
    float3 axisZ;        float effect;      // GRASS_EFFECT_*
    float3 boundsCentre; float boundsRadius; // a sphere round the shape AND its reach, for skipping it quickly
    float  pressRate;    float3 unused;     // 1 / seconds to take full effect
};

/// Distance from a point to the shape, negative inside, and the point of the shape nearest to it.
float GrassShapeDistance(GrassShape s, float3 p, out float3 nearest)
{
    int shape = (int)s.shape;

    if (shape == GRASS_SHAPE_SPHERE)
    {
        nearest = s.centre;
        return length(p - s.centre) - s.radius;
    }

    if (shape == GRASS_SHAPE_CAPSULE)
    {
        // A capsule is every point within `radius` of a line segment.
        float3 along = s.size - s.centre;
        float t = saturate(dot(p - s.centre, along) / max(dot(along, along), 1e-6));
        nearest = s.centre + along * t;
        return length(p - nearest) - s.radius;
    }

    // A box: step into its own space, where it is axis-aligned and the distance is the classic one.
    float3 offset = p - s.centre;
    float3 local = float3(dot(offset, s.axisX), dot(offset, s.axisY), dot(offset, s.axisZ));
    float3 outside = abs(local) - s.size;
    float3 inside = clamp(local, -s.size, s.size);
    nearest = s.centre + s.axisX * inside.x + s.axisY * inside.y + s.axisZ * inside.z;
    return length(max(outside, 0.0)) + min(max(outside.x, max(outside.y, outside.z)), 0.0);
}

/// How much a shape touches the grass rooted at `groundWS`: 0 not at all, up to the shape's strength
/// inside it, easing off over its reach. `away` is the way it pushes the grass, in world XZ.
///
/// The grass at a spot is a column from the ground up to `grassHeight`. The distance is measured from
/// the point of that column level with the middle of the shape, clamped to the column - so a ball
/// rolling on the ground touches the grass, a bird flying over it does not, and a house standing on the
/// ground swallows the whole column.
float GrassShapeTouch(GrassShape s, float3 groundWS, float grassHeight, out float2 away)
{
    away = 0.0;

    float2 fromBounds = groundWS.xz - s.boundsCentre.xz;
    if (dot(fromBounds, fromBounds) > s.boundsRadius * s.boundsRadius) return 0.0;

    float3 p = float3(groundWS.x, clamp(s.boundsCentre.y, groundWS.y, groundWS.y + grassHeight), groundWS.z);
    float3 nearest;
    float distance = GrassShapeDistance(s, p, nearest);

    float touch = 1.0 - smoothstep(0.0, max(s.reach, 1e-3), distance);
    if (touch <= 0.0) return 0.0;

    // Away from the nearest part of the shape. Deep inside a box that point is the grass itself, so
    // there it pushes out from the middle instead.
    float2 push = groundWS.xz - nearest.xz;
    if (dot(push, push) < 1e-6) push = groundWS.xz - s.centre.xz;
    away = push / max(length(push), 1e-4);

    return touch * s.strength;
}

#endif
