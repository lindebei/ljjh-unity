using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 项目初始化一次性脚本：建 URP 管线资产并挂到 Graphics/Quality，顺带把 v0.1 需要的
/// 项目设置一次设好（Linear 色彩空间 / .NET Standard / Mono 后端 / 新输入系统）。
/// 用法：
///   Unity.exe -batchmode -quit -projectPath D:\ljjh\unity -executeMethod ProjectBootstrap.SetupAll -logFile D:\ljjh\bootstrap.log
/// 说明：全部操作可重复执行（幂等），已存在的资产会复用而不是重建。
/// </summary>
public static class ProjectBootstrap
{
    const string SettingsDir = "Assets/Settings";

    public static void SetupAll()
    {
        SetupPipeline();
        SetupPlayer();
        Debug.Log("[Bootstrap] 全部完成");
    }

    static void SetupPipeline()
    {
        if (!AssetDatabase.IsValidFolder(SettingsDir))
        {
            AssetDatabase.CreateFolder("Assets", "Settings");
        }

        string rendererPath = SettingsDir + "/URP-Renderer.asset";
        string pipelinePath = SettingsDir + "/URP-Pipeline.asset";

        var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererPath);
        if (rendererData == null)
        {
            rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
            AssetDatabase.CreateAsset(rendererData, rendererPath);
            Debug.Log("[Bootstrap] 新建 URP-Renderer.asset");
        }

        var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(pipelinePath);
        if (pipeline == null)
        {
            pipeline = UniversalRenderPipelineAsset.Create(rendererData);
            AssetDatabase.CreateAsset(pipeline, pipelinePath);
            Debug.Log("[Bootstrap] 新建 URP-Pipeline.asset");
        }

        AssetDatabase.SaveAssets();

        GraphicsSettings.defaultRenderPipeline = pipeline;
        int levels = QualitySettings.names.Length;
        for (int i = 0; i < levels; i++)
        {
            QualitySettings.SetQualityLevel(i, false);
            QualitySettings.renderPipeline = pipeline;
        }
        QualitySettings.SetQualityLevel(0, false);
        Debug.Log($"[Bootstrap] URP 已挂到 GraphicsSettings 与 {levels} 个质量档");
    }

    static void SetupPlayer()
    {
        PlayerSettings.colorSpace = ColorSpace.Linear;
        PlayerSettings.SetApiCompatibilityLevel(BuildTargetGroup.Standalone, ApiCompatibilityLevel.NET_Standard);
        PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, ScriptingImplementation.Mono2x);

        var objs = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
        if (objs != null && objs.Length > 0)
        {
            var so = new SerializedObject(objs[0]);
            var prop = so.FindProperty("activeInputHandler");
            if (prop != null)
            {
                prop.intValue = 1;   // 1 = 仅新输入系统（Input System），避免两套后端并存
                so.ApplyModifiedProperties();
                Debug.Log("[Bootstrap] activeInputHandler = 1（仅新输入系统）");
            }
            else
            {
                Debug.LogWarning("[Bootstrap] 没找到 activeInputHandler 属性，跳过");
            }
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[Bootstrap] Player 设置：colorSpace={PlayerSettings.colorSpace}, api={PlayerSettings.GetApiCompatibilityLevel(BuildTargetGroup.Standalone)}, backend={PlayerSettings.GetScriptingBackend(BuildTargetGroup.Standalone)}");
    }
}
