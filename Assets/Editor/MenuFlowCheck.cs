using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 场景链路自检：确认 5 个场景都在 Build Settings、文件真实存在、名字能过 CanStreamedLevelBeLoaded。
/// 无头环境下点不了按钮，所以这里只保证「LoadScene 一定找得到目标」这一层，
/// 剩下的按钮命中/跳转需要 GUI 里真点一次。
/// </summary>
public static class MenuFlowCheck
{
    [MenuItem("灵境/场景链路自检")]
    public static void Run()
    {
        string[] expect = { "MainMenu", "BattlePrototype", "BattleWaves", "BattleBoss", "EquipScreen" };
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# 场景链路自检");

        var scenes = EditorBuildSettings.scenes;
        sb.AppendLine("Build Settings 共 " + scenes.Length + " 个：");
        for (int i = 0; i < scenes.Length; i++)
        {
            string name = Path.GetFileNameWithoutExtension(scenes[i].path);
            sb.AppendFormat("  [{0}] {1}  enabled={2}  exists={3}\n", i, name, scenes[i].enabled, File.Exists(scenes[i].path));
        }

        int ok = 0;
        for (int i = 0; i < expect.Length; i++)
        {
            string path = "Assets/Scenes/" + expect[i] + ".unity";
            bool inBuild = false;
            int index = -1;
            for (int k = 0; k < scenes.Length; k++)
                if (scenes[k].path == path) { inBuild = true; index = k; break; }

            bool canLoad = Application.CanStreamedLevelBeLoaded(expect[i]);
            bool exists = File.Exists(path);
            bool pass = inBuild && canLoad && exists;
            if (pass) ok++;
            sb.AppendFormat("  {0} {1}: inBuild={2} idx={3} exists={4} canLoad={5}\n",
                pass ? "OK  " : "FAIL", expect[i], inBuild, index, exists, canLoad);
        }

        sb.AppendFormat("通过 {0} / {1}\n", ok, expect.Length);
        Directory.CreateDirectory("D:/ljjh/tmp");
        File.WriteAllText("D:/ljjh/tmp/menu_flow.txt", sb.ToString());
        Debug.Log(sb.ToString());
    }
}
