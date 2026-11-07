// 一键进 Play：打开指定场景并进入播放模式。
// 注意：用 -executeMethod 在编辑器启动阶段直接 OpenScene，之后 Unity 会恢复「上次打开的场景」
// 把结果覆盖掉（实测日志写着 Camp、出来的却是 Explore）。所以这里改成「排队 + 等编辑器就绪」，
// 并且用显式 [InitializeOnLoadMethod] 挂钩子（静态构造在 -executeMethod 启动早期并不可靠）。
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class DevLaunch
{
    const string PendingKey = "ljjh.devlaunch.pending";
    static double readyAt = -1;

    [InitializeOnLoadMethod]
    static void Hook()
    {
        EditorApplication.update += Tick;
        if (EditorPrefs.HasKey(PendingKey))
            Debug.Log("[DevLaunch] Hook 已挂上，待办场景=" + EditorPrefs.GetString(PendingKey));
    }

    [MenuItem("灵境/直接进营地（进入 Play）")]
    public static void RunCamp() { Launch("Assets/Scenes/Camp.unity"); }

    [MenuItem("灵境/直接进一境地图（进入 Play）")]
    public static void RunExplore()
    {
        LingJing.Battle.RunState.Reset();
        Launch("Assets/Scenes/Explore.unity");
    }

    [MenuItem("灵境/直接开打（进入 Play）")]
    public static void Run() { Launch("Assets/Scenes/BattlePrototype.unity"); }

    [MenuItem("灵境/从主菜单开打（进入 Play）")]
    public static void RunMenu() { Launch("Assets/Scenes/MainMenu.unity"); }

    static void Launch(string scene)
    {
        EditorPrefs.SetString(PendingKey, scene);
        readyAt = -1;
        Debug.Log("[DevLaunch] 排队：等编辑器就绪后打开 " + scene + " 并进 Play");
    }

    static void Tick()
    {
        if (!EditorPrefs.HasKey(PendingKey)) return;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) { readyAt = -1; return; }
        if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode) { ClearPending(); return; }

        if (readyAt < 0) { readyAt = EditorApplication.timeSinceStartup + 2.0; return; }
        if (EditorApplication.timeSinceStartup < readyAt) return;

        string scene = EditorPrefs.GetString(PendingKey, "");
        if (!string.IsNullOrEmpty(scene) && System.IO.File.Exists(scene))
        {
            EditorSceneManager.OpenScene(scene);
            Debug.Log("[DevLaunch] 打开场景 " + scene + "，进 Play");
            EditorApplication.isPlaying = true;
        }
        ClearPending();
    }

    static void ClearPending()
    {
        EditorPrefs.DeleteKey(PendingKey);
        readyAt = -1;
    }
}
