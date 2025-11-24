using UnityEngine;

[ExecuteInEditMode]
public class BakeSmoothedNormals : MonoBehaviour
{
    void Start()
    {
        Mesh mesh = null;
        
        // Проверяем MeshFilter
        var meshFilter = GetComponent<MeshFilter>();
        if (meshFilter != null)
        {
            mesh = meshFilter.sharedMesh;
        }
        
        // Проверяем SkinnedMeshRenderer (для персонажей)
        var skinnedMesh = GetComponent<SkinnedMeshRenderer>();
        if (skinnedMesh != null)
        {
            mesh = skinnedMesh.sharedMesh;
        }
        
        if (mesh != null)
        {
            BakeSmoothNormals(mesh);
            Debug.Log("Smooth normals baked for: " + gameObject.name);
        }
        else
        {
            Debug.LogWarning("No mesh found on: " + gameObject.name);
        }
    }

    void BakeSmoothNormals(Mesh mesh)
    {
        var smoothNormals = new Vector3[mesh.vertexCount];
        var vertexGroups = new System.Collections.Generic.Dictionary<Vector3, System.Collections.Generic.List<int>>();
        
        for (int i = 0; i < mesh.vertexCount; i++)
        {
            var pos = mesh.vertices[i];
            if (!vertexGroups.ContainsKey(pos))
                vertexGroups[pos] = new System.Collections.Generic.List<int>();
            vertexGroups[pos].Add(i);
        }
        
        foreach (var group in vertexGroups.Values)
        {
            var avgNormal = Vector3.zero;
            foreach (var idx in group)
                avgNormal += mesh.normals[idx];
            avgNormal.Normalize();
            
            foreach (var idx in group)
                smoothNormals[idx] = avgNormal;
        }
        
        var tangents = new Vector4[mesh.vertexCount];
        for (int i = 0; i < mesh.vertexCount; i++)
        {
            tangents[i] = new Vector4(smoothNormals[i].x, smoothNormals[i].y, smoothNormals[i].z, 0);
        }
        
        mesh.tangents = tangents;
    }
}