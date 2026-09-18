using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Adds a brush to the GrassField inspector for painting its two maps.
///
/// The PAINT map is an ordinary PNG next to the other grass assets: white everywhere means "leave
/// the grass exactly as the procedural rules made it", and painting only ever darkens. That is the
/// whole mental model - there is no way to paint yourself into a field you cannot undo, and the
/// map can be opened in any image editor if that is quicker than a brush.
///
///   R  how much grass grows here        G  how tall it grows        B  how lush and green it is
///
/// The TYPE map is the other way round: black means "nothing painted", and painting a channel adds
/// the zone of one grass type from the field's list - R for element 1, G for 2, B for 3, A for 4.
///
/// Painting writes straight into the texture and saves it when the stroke ends. It is a few
/// hundred pixels per stroke, so there is no need for anything cleverer than that.
/// </summary>
[CustomEditor(typeof(GrassField))]
public class GrassFieldEditor : Editor
{
    private enum Channel
    {
        Density = 0,
        Height = 1,
        Lushness = 2,
        ZoneOfType1 = 3,
        ZoneOfType2 = 4,
        ZoneOfType3 = 5,
        ZoneOfType4 = 6
    }

    // Static, so the brush keeps its settings while you click between fields.
    private static bool _painting;
    private static Channel _channel = Channel.Density;
    private static float _radius = 6f;
    private static float _strength = 0.5f;
    private static int _newMapSize = 512;

    private bool _unsavedStroke;

    private static bool PaintsZones => _channel >= Channel.ZoneOfType1;

    /// <summary>Which of the texture's four channels the brush writes to.</summary>
    private static int TextureChannel => PaintsZones ? _channel - Channel.ZoneOfType1 : (int)_channel;

    private SerializedProperty MapProperty => serializedObject.FindProperty(PaintsZones ? "_typeMap" : "_paintMap");

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Brush", EditorStyles.boldLabel);

        var terrain = serializedObject.FindProperty("_terrain").objectReferenceValue as Terrain;
        if (terrain == null)
        {
            EditorGUILayout.HelpBox("Assign a Terrain before painting.", MessageType.Info);
            return;
        }

        _channel = (Channel)EditorGUILayout.EnumPopup("Brush paints", _channel);

        SerializedProperty mapProperty = MapProperty;
        if (mapProperty.objectReferenceValue == null)
        {
            _newMapSize = EditorGUILayout.IntPopup("Map size", _newMapSize,
                new[] { "256", "512", "1024", "2048" }, new[] { 256, 512, 1024, 2048 });

            if (PaintsZones)
            {
                if (GUILayout.Button("Create type map")) CreateMap(terrain, mapProperty, "_GrassTypes", Color.clear);

                EditorGUILayout.HelpBox(
                    "A new type map is black, which means 'nothing painted'. Paint a channel to grow "
                    + "that type there, on top of its own zones.", MessageType.None);
            }
            else
            {
                if (GUILayout.Button("Create paint map")) CreateMap(terrain, mapProperty, "_GrassPaint", Color.white);

                EditorGUILayout.HelpBox(
                    "A new map is white, which means 'change nothing'. Paint darker to thin the grass, "
                    + "make it shorter, or dry it out.", MessageType.None);
            }
            return;
        }

        // A map dragged in by hand is almost always imported wrong: the brush reads it back with
        // GetPixels, which throws unless Read/Write is on, and the compute treats the channels as
        // plain multipliers, which is only true with sRGB off. Both are silent failures otherwise
        // - one an exception mid-stroke, the other grass that thins at the wrong rate - so say so
        // here, where there is room to explain, rather than in the console.
        var mapTexture = (Texture2D)mapProperty.objectReferenceValue;
        string problem = DescribeImportProblem(mapTexture);
        if (problem != null)
        {
            EditorGUILayout.HelpBox(problem, MessageType.Warning);
            if (GUILayout.Button("Fix import settings")) FixImportSettings(mapTexture);
            return;
        }

        _painting = GUILayout.Toggle(_painting, _painting ? "Painting - click to stop" : "Start painting", "Button");
        _radius = EditorGUILayout.Slider("Radius (m)", _radius, 0.5f, 40f);
        _strength = EditorGUILayout.Slider("Strength", _strength, 0.02f, 1f);

        EditorGUILayout.HelpBox(PaintsZones
                ? "Drag in the scene to grow this type there. Hold Shift to rub it out."
                : "Drag in the scene to take grass away. Hold Shift to put it back.",
            MessageType.None);
    }

    private void OnSceneGUI()
    {
        if (!_painting) return;

        var terrain = serializedObject.FindProperty("_terrain").objectReferenceValue as Terrain;
        var map = MapProperty.objectReferenceValue as Texture2D;
        if (terrain == null || map == null) return;

        var collider = terrain.GetComponent<TerrainCollider>();
        if (collider == null) return;

        Event current = Event.current;

        // Without this the first click in the scene deselects the object instead of painting.
        HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));

        // Saving comes FIRST, before anything that can bail out.
        //
        // A stroke lives in the texture's memory until mouse-up writes it to the PNG. Put the
        // save after the terrain raycast and letting go of the button while the cursor is over
        // the sky - which is exactly what happens at the end of a sweep across a ridge - skips
        // it silently. The paint stays on screen, looks saved, and is gone at the next reload.
        if (current.type == EventType.MouseUp && current.button == 0 && _unsavedStroke)
        {
            Save(map);
            _unsavedStroke = false;
        }

        Ray ray = HandleUtility.GUIPointToWorldRay(current.mousePosition);
        if (!collider.Raycast(ray, out RaycastHit hit, 10000f)) return;

        // A plain stroke does whatever this map's "painting" means: take grass away on the paint map,
        // grow a type on the type map. Shift does the opposite.
        bool raising = current.shift != PaintsZones;
        Handles.color = raising ? new Color(0.4f, 1f, 0.4f, 0.9f) : new Color(1f, 0.7f, 0.3f, 0.9f);
        // Flat, not laid against the ground normal: the brush measures its radius in the XZ
        // plane, so on a slope a disc drawn along the surface promises an area it will not paint.
        Handles.DrawWireDisc(hit.point, Vector3.up, _radius);
        Handles.DrawWireDisc(hit.point, Vector3.up, _radius * 0.5f);

        // Only when the pointer actually moved. Repainting from inside a Repaint event asks for
        // another Repaint, and the scene view spins at full speed doing nothing.
        if (current.type == EventType.MouseMove || current.type == EventType.MouseDrag)
            SceneView.RepaintAll();

        bool stroke = current.button == 0
                   && (current.type == EventType.MouseDown || current.type == EventType.MouseDrag);

        if (stroke)
        {
            Paint(terrain, map, hit.point, raising);
            current.Use();
        }
    }

    private void Paint(Terrain terrain, Texture2D map, Vector3 point, bool raising)
    {
        Vector3 origin = terrain.transform.position;
        Vector3 size = terrain.terrainData.size;

        float u = (point.x - origin.x) / size.x;
        float v = (point.z - origin.z) / size.z;

        // The brush is round in METRES, so work out how many pixels that is on each axis - a
        // terrain that is not square would otherwise paint ellipses.
        int minX = Mathf.Clamp(Mathf.FloorToInt((u - _radius / size.x) * map.width), 0, map.width - 1);
        int maxX = Mathf.Clamp(Mathf.CeilToInt((u + _radius / size.x) * map.width), 0, map.width - 1);
        int minY = Mathf.Clamp(Mathf.FloorToInt((v - _radius / size.z) * map.height), 0, map.height - 1);
        int maxY = Mathf.Clamp(Mathf.CeilToInt((v + _radius / size.z) * map.height), 0, map.height - 1);

        int width = maxX - minX + 1;
        int height = maxY - minY + 1;
        if (width <= 0 || height <= 0) return;

        Color[] pixels = map.GetPixels(minX, minY, width, height);
        float target = raising ? 1f : 0f;
        int channel = TextureChannel;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float pixelU = (minX + x + 0.5f) / map.width;
                float pixelV = (minY + y + 0.5f) / map.height;

                float dx = (pixelU - u) * size.x;
                float dz = (pixelV - v) * size.z;
                float distance = Mathf.Sqrt(dx * dx + dz * dz);
                if (distance > _radius) continue;

                float fall = 1f - distance / _radius;
                fall = fall * fall * (3f - 2f * fall);      // soft edge, hard centre

                int index = y * width + x;
                Color colour = pixels[index];
                colour[channel] = Mathf.Lerp(colour[channel], target, fall * _strength);
                pixels[index] = colour;
            }
        }

        map.SetPixels(minX, minY, width, height, pixels);
        map.Apply(false);
        _unsavedStroke = true;
    }

    /// <summary>Written back at the end of a stroke, not on every mouse move.</summary>
    private static void Save(Texture2D map)
    {
        string path = AssetDatabase.GetAssetPath(map);
        if (string.IsNullOrEmpty(path)) return;

        File.WriteAllBytes(path, map.EncodeToPNG());
        AssetDatabase.ImportAsset(path);
    }

    /// <summary>What is wrong with this map's import settings, or null if nothing is.</summary>
    private static string DescribeImportProblem(Texture2D map)
    {
        string path = AssetDatabase.GetAssetPath(map);
        if (string.IsNullOrEmpty(path))
            return "This map is not an asset on disk, so a stroke cannot be saved. Use Create paint map.";

        if (AssetImporter.GetAtPath(path) is not TextureImporter importer) return null;

        if (!importer.isReadable)
            return "The brush has to read the map back, so it needs Read/Write enabled.";
        if (importer.sRGBTexture)
            return "The compute shader reads these channels as plain multipliers, so sRGB must be off.";
        if (importer.textureCompression != TextureImporterCompression.Uncompressed)
            return "Compression scrambles the control values. This map should be uncompressed.";

        return null;
    }

    private static void FixImportSettings(Texture2D map)
    {
        string path = AssetDatabase.GetAssetPath(map);
        if (string.IsNullOrEmpty(path)) return;
        if (AssetImporter.GetAtPath(path) is not TextureImporter importer) return;

        ApplyControlMapSettings(importer);
    }

    /// <summary>The import settings a control map needs: raw values, readable, no mips.</summary>
    private static void ApplyControlMapSettings(TextureImporter importer)
    {
        importer.textureType = TextureImporterType.Default;
        importer.sRGBTexture = false;
        importer.mipmapEnabled = false;
        importer.isReadable = true;
        importer.filterMode = FilterMode.Bilinear;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();
    }

    private void CreateMap(Terrain terrain, SerializedProperty mapProperty, string suffix, Color32 fill)
    {
        const string directory = "Assets/_Tender/Art/Grass";
        Directory.CreateDirectory(directory);

        string path = AssetDatabase.GenerateUniqueAssetPath($"{directory}/{terrain.name}{suffix}.png");

        var texture = new Texture2D(_newMapSize, _newMapSize, TextureFormat.RGBA32, false, true);
        var pixels = new Color32[_newMapSize * _newMapSize];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = fill;
        texture.SetPixels32(pixels);
        texture.Apply();
        File.WriteAllBytes(path, texture.EncodeToPNG());
        DestroyImmediate(texture);

        AssetDatabase.ImportAsset(path);

        ApplyControlMapSettings((TextureImporter)AssetImporter.GetAtPath(path));

        mapProperty.objectReferenceValue = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        serializedObject.ApplyModifiedProperties();
    }
}
