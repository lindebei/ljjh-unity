using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 把 Resources/Art 下的 png 批量设成 Sprite。
/// 必须走这一步：Unity 新建工程的默认纹理类型是 Default，`Resources.Load&lt;Sprite&gt;` 会拿到 null，
/// 图就静默不显示（第一次踩坑就是这么「什么都没发生」的）。
/// 立绘 512、图标 256，图标不需要那么大，压得越狠 UI 里越糊。
/// </summary>
public static class ArtImporter
{
    [MenuItem("灵境/导入美术资源 (立绘+图标 → Sprite)")]
    public static void ImportAll()
    {
        int portraits = ImportDir("Assets/Resources/Art/Creatures", 512);
        int skills = ImportDir("Assets/Resources/Art/Icons/Skills", 256);
        int status = ImportDir("Assets/Resources/Art/Icons/Status", 256);
        int ui = ImportDir("Assets/Resources/Art/UI", 1024);
        Debug.Log(string.Format("[ArtImporter] 导入完成：立绘 {0} 张、技能图标 {1} 张、状态图标 {2} 张、UI 皮肤 {3} 张",
            portraits, skills, status, ui));
    }

    static int ImportDir(string dir, int maxSize)
    {
        if (!Directory.Exists(dir)) return 0;

        int n = 0;
        foreach (var file in Directory.GetFiles(dir, "*.png", SearchOption.AllDirectories))
        {
            var assetPath = file.Replace('\\', '/');
            if (!assetPath.StartsWith("Assets/")) continue;

            var ti = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (ti == null) continue;

            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.alphaIsTransparency = true;
            ti.mipmapEnabled = false;
            ti.maxTextureSize = maxSize;
            ti.filterMode = FilterMode.Bilinear;
            ti.textureCompression = TextureImporterCompression.CompressedHQ;
            ti.SaveAndReimport();
            n++;
        }
        return n;
    }
}
