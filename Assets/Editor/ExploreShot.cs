// 一境地图截图：打开 Explore 场景进 Play，等场景稳定后截一张，然后退出。
// 编辑器域重载会清静态字段，所以状态放 EditorPrefs（和 BattleAutoShot 同一套路）。
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class ExploreShot
{
    const string PendingKey = "ljjh.exploreshot.pending";
    const string PhaseKey = "ljjh.exploreshot.phase";
    const string FrameKey = "ljjh.exploreshot.frame";
    const string OutDir = "D:/ljjh/tmp/shots";
    const string Scene = "Assets/Scenes/Explore.unity";

    public static void Run()
    {
        Directory.CreateDirectory(OutDir);
        EditorPrefs.SetBool(PendingKey, true);
        EditorPrefs.SetInt(PhaseKey, 0);
        EditorPrefs.SetInt(FrameKey, 0);
        EditorSceneManager.OpenScene(Scene);
        EditorApplication.EnterPlaymode();
        Debug.Log("[ExploreShot] 请求进入 Play，场景=" + Scene);
    }

    [InitializeOnLoadMethod]
    static void Hook()
    {
        EditorApplication.update += Tick;
    }

    static void Tick()
    {
        if (!EditorPrefs.GetBool(PendingKey, false)) return;
        int phase = EditorPrefs.GetInt(PhaseKey, 0);
        int frame = EditorPrefs.GetInt(FrameKey, 0);
        frame++;
        EditorPrefs.SetInt(FrameKey, frame);

        switch (phase)
        {
            case 0:
                if (frame >= 90)
                {
                    ScreenCapture.CaptureScreenshot(OutDir + "/explore_01.png");
                    Debug.Log("[ExploreShot] 截图 explore_01.png");
                    EditorPrefs.SetInt(PhaseKey, 1);
                    EditorPrefs.SetInt(FrameKey, 0);
                }
                break;
            case 1:
                if (frame >= 30)
                {
                    EditorPrefs.DeleteKey(PendingKey);
                    EditorApplication.Exit(0);
                }
                break;
        }
    }
}