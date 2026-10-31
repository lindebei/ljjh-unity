using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 把 Resources/Art/Creatures 下的 png 批量设成 Sprite。
/// 必须走这一步：Unity 新建工程的默认纹理类型是 Default，`Resources.Load&lt;Sprite&gt;` 会拿到 null，
/// 立绘就静默不显示（第一次踩坑就是这么「什么都没发生」的）。
/// </summary>
public static class ArtImporter
{
    const string Dir = "Assets/Resources/Art/Creatures";

    [MenuItem("灵境/导入美术资源 (立绘 → Sprite)")]
    public static void ImportAll()
    {
        int n = 0;
        foreach (var file in Directory.GetFiles(Dir, "*.png"))
        {
            var assetPath = file.Replace('\\', '/');
            if (!assetPath.StartsWith("Assets/")) continue;

            var ti = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (ti == null) continue;

            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.alphaIsTransparency = true;
            ti.mipmapEnabled = false;
            ti.maxTextureSize = 512;
            ti.filterMode = FilterMode.Bilinear;
            ti.textureCompression = TextureImporterCompression.CompressedHQ;
            ti.SaveAndReimport();
            n++;
        }
        Debug.Log("[ArtImporter] 立绘导入完成：" + n + " 张");
    }
}
