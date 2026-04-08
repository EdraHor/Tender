using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(MeshRenderer))]
public class HeroCloudGenerator : MonoBehaviour
{
    public Material cloudMaterial; // Перетащи сюда свой материал облака

    [Header("Global Seed & Blending")]
    public int randomSeed = 42;
    [Range(0.01f, 0.5f)] public float blobBlend = 0.15f;

    [Header("Base (Широкое основание)")]
    public int baseCount = 15;
    public float baseRadius = 0.4f;      // Насколько широко разбросаны
    public float baseHeightY = -0.3f;    // Высота в кубе (от -0.5 до 0.5)
    public float baseSphereSize = 0.15f;

    [Header("Tower (Центральный столб)")]
    public int towerCount = 10;
    public float towerScatter = 0.15f;   // Насколько кривой столб
    public float towerMinY = -0.1f;
    public float towerMaxY = 0.3f;
    public float towerSphereSize = 0.18f;[Header("Anvil (Шапка / Наковальня)")]
    public int anvilCount = 12;
    public float anvilRadius = 0.35f;
    public float anvilHeightY = 0.35f;
    public float anvilSphereSize = 0.15f;

    private ComputeBuffer metaballBuffer;
    private Vector4[] metaballData;

    void Update()
    {
        if (cloudMaterial == null) return;
        GenerateMetaballs();
    }

    void GenerateMetaballs()
    {
        int totalSpheres = baseCount + towerCount + anvilCount;
        if (totalSpheres == 0) return;

        // Инициализируем или пересоздаем буфер, если размер изменился
        if (metaballBuffer == null || metaballBuffer.count != totalSpheres)
        {
            ReleaseBuffer();
            metaballBuffer = new ComputeBuffer(totalSpheres, sizeof(float) * 4);
            metaballData = new Vector4[totalSpheres];
        }

        Random.InitState(randomSeed);
        int index = 0;

        // 1. Генерируем основание (широкий, бугристый блин из сфер)
        for (int i = 0; i < baseCount; i++)
        {
            Vector2 circle = Random.insideUnitCircle * baseRadius;
            float yOffset = Random.Range(-0.05f, 0.05f);
            float radiusMod = Random.Range(0.8f, 1.2f);
            metaballData[index++] = new Vector4(circle.x, baseHeightY + yOffset, circle.y, baseSphereSize * radiusMod);
        }

        // 2. Генерируем башню (вертикальный кластер)
        for (int i = 0; i < towerCount; i++)
        {
            Vector2 circle = Random.insideUnitCircle * towerScatter;
            float h = Mathf.Lerp(towerMinY, towerMaxY, (float)i / Mathf.Max(1, towerCount - 1));
            float radiusMod = Random.Range(0.7f, 1.3f);
            metaballData[index++] = new Vector4(circle.x, h, circle.y, towerSphereSize * radiusMod);
        }

        // 3. Генерируем шапку (расширение сверху)
        for (int i = 0; i < anvilCount; i++)
        {
            Vector2 circle = Random.insideUnitCircle * anvilRadius;
            float yOffset = Random.Range(-0.05f, 0.05f);
            float radiusMod = Random.Range(0.8f, 1.2f);
            metaballData[index++] = new Vector4(circle.x, anvilHeightY + yOffset, circle.y, anvilSphereSize * radiusMod);
        }

        // Отправляем данные в шейдер
        metaballBuffer.SetData(metaballData);
        cloudMaterial.SetBuffer("_Metaballs", metaballBuffer);
        cloudMaterial.SetInt("_MetaballCount", totalSpheres);
        cloudMaterial.SetFloat("_BlobBlend", blobBlend);
    }

    void ReleaseBuffer()
    {
        if (metaballBuffer != null)
        {
            metaballBuffer.Release();
            metaballBuffer = null;
        }
    }

    void OnDisable() { ReleaseBuffer(); }
    void OnDestroy() { ReleaseBuffer(); }
}