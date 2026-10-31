using System.Threading;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

/// <summary>
/// 一次性包安装脚本：在批处理模式下补齐 v0.1 需要的两个包。
/// 用法（命令行）：
///   Unity.exe -batchmode -quit -projectPath D:\ljjh\unity -executeMethod PackageSetup.AddCorePackages -logFile D:\ljjh\pkg-add.log
/// 说明：URP 与 Input System 的版本号故意不写死，交给 UPM 按当前编辑器版本解析，
/// 避免"文档写的版本"和"编辑器实装的版本"打架。
/// </summary>
public static class PackageSetup
{
    static void Install(string packageId)
    {
        AddRequest req = Client.Add(packageId);
        int guard = 0;
        while (!req.IsCompleted && guard++ < 3000)   // 最多等 10 分钟
        {
            Thread.Sleep(200);
        }

        if (req.Status == StatusCode.Success)
        {
            Debug.Log($"[PackageSetup] OK  {packageId} -> {req.Result.packageId}");
        }
        else
        {
            Debug.LogError($"[PackageSetup] FAIL {packageId} status={req.Status} err={req.Error?.message}");
        }
    }

    public static void AddCorePackages()
    {
        Debug.Log("[PackageSetup] 开始安装 URP");
        Install("com.unity.render-pipelines.universal");
        Debug.Log("[PackageSetup] 开始安装 Input System");
        Install("com.unity.inputsystem");
        Debug.Log("[PackageSetup] 全部完成");
    }
}
