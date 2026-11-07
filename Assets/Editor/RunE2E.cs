
// 一境循环端到端冒烟：地图 → 点「普通遭遇」→ 战斗（自动打完）→ 点「回地图」→ 校验状态。
// 注意：这个脚本在编辑器里不稳定（进 Play 后 EditorApplication.update 偶尔不再被调用，
// 表现是进程挂住、run_e2e.txt 一个字都不写）。手动验证请用 MainMenu → 一境探索 点一遍。
// 跑法：Unity.exe -projectPath D:\ljjh\unity -executeMethod RunE2E.Run -logFile D:\ljjh\tmp\e2e.log
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class RunE2E
{
    const string KEY = "ljjh.rune2e.pending";
    static int phase;
    static int frames;
    static string logPath = "D:/ljjh/tmp/run_e2e.txt";

    [MenuItem("灵境/一境循环端到端")]
    public static void Run()
    {
        EditorPrefs.SetInt(KEY, 1);
        EditorSceneManager.OpenScene("Assets/Scenes/RunMap.unity");
        Debug.Log("[RunE2E] Run() 已打开 RunMap，准备进 Play");
        EditorApplication.delayCall += () =>
        {
            Debug.Log("[RunE2E] delayCall 执行，设置 isPlaying=true");
            EditorApplication.isPlaying = true;
        };
    }

    [InitializeOnLoadMethod]
    static void Hook()
    {
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
    }

    static void Say(string s)
    {
        File.AppendAllText(logPath, s + "\n");
        Debug.Log("[RunE2E] " + s);
    }

    static Button FindButton(string keyword)
    {
        var all = Object.FindObjectsOfType<Button>();
        for (int i = 0; i < all.Length; i++)
        {
            var t = all[i].GetComponentInChildren<Text>();
            if (t != null && t.text != null && t.text.Contains(keyword)) return all[i];
        }
        return null;
    }

    static void Tick()
    {
        if (EditorPrefs.GetInt(KEY, 0) != 1) return;
        if (!EditorApplication.isPlaying) return;
        frames++;
        if (frames == 1) Debug.Log("[RunE2E] Tick 开始跑，phase=" + phase);

        if (phase == 0 && frames > 30)
        {
            var b = FindButton("普通遭遇");
            if (b == null) { if (frames > 120) { Say("FAIL 地图上找不到「普通遭遇」按钮"); Finish(); } return; }
            Say("OK 地图已加载，点击「普通遭遇」");
            b.onClick.Invoke();
            phase = 1; frames = 0;
            return;
        }

        if (phase == 1)
        {
            if (SceneManager.GetActiveScene().name != "BattlePrototype")
            {
                if (frames > 600) { Say("FAIL 点了普通遭遇但没进战斗场景（当前=" + SceneManager.GetActiveScene().name + "）"); Finish(); }
                return;
            }
            var hud = Object.FindObjectOfType<LingJing.UI.BattleHud>();
            if (hud == null) { if (frames > 600) { Say("FAIL 战斗场景里没有 BattleHud"); Finish(); } return; }
            Say("OK 已进入战斗场景；地图状态 Floor=" + LingJing.Battle.RunState.Floor +
                " 队伍=" + string.Join(",", LingJing.Battle.RunState.PartyIds.ToArray()));
            hud.autoBattle = true;
            hud.timeScale = 6f;
            phase = 2; frames = 0;
            return;
        }

        if (phase == 2)
        {
            var hud = Object.FindObjectOfType<LingJing.UI.BattleHud>();
            if (hud == null) { if (frames > 300) { Say("FAIL 战斗中 BattleHud 丢了"); Finish(); } return; }
            var sim = hud.Simulator;
            if (sim == null) { if (frames > 600) { Say("FAIL 战斗没有开始（sim 为空）"); Finish(); } return; }
            if (!sim.IsBattleOver)
            {
                if (frames > 40000) { Say("FAIL 战斗在 4 万帧内没打完"); Finish(); }
                return;
            }
            Say("OK 战斗结束：回合=" + sim.round + " 我方存活=" + sim.Alive(LingJing.Data.Side.Player).Count +
                " 掉落=" + sim.loot.Count);
            // ShowFinish 是在 HUD.Update 里跑的，可能比 sim.IsBattleOver 晚一两帧；等按钮出现
            var back = FindButton("回地图");
            if (back == null)
            {
                if (frames > 400)
                {
                    var names = new System.Text.StringBuilder();
                    var all = Object.FindObjectsOfType<Button>(true);
                    for (int i = 0; i < all.Length; i++)
                    {
                        var t = all[i].GetComponentInChildren<Text>(true);
                        names.Append("[").Append(all[i].gameObject.name).Append("=")
                             .Append(t != null ? t.text : "?").Append(all[i].gameObject.activeInHierarchy ? "" : "(inactive)").Append("] ");
                    }
                    var banners = Object.FindObjectsOfType<Text>(true);
                    var btxt = "";
                    for (int i = 0; i < banners.Length; i++)
                        if (banners[i].text != null && banners[i].text.Contains("战斗结束")) btxt = banners[i].text;
                    var hud2 = Object.FindObjectOfType<LingJing.UI.BattleHud>();
                    Say("FAIL 等 400 帧仍找不到「回地图」按钮；RunState.Active=" + LingJing.Battle.RunState.Active +
                        "；simOver=" + (hud2 != null && hud2.Simulator != null ? hud2.Simulator.IsBattleOver.ToString() : "?") +
                        "；横幅=" + (btxt == "" ? "(没有战斗结束字样)" : btxt) + "；现有按钮：" + names);
                    Finish();
                }
                return;
            }
            var beforeFloor = LingJing.Battle.RunState.Floor;
            var beforeHp = LingJing.Battle.RunState.HpPct.Count > 0 ? LingJing.Battle.RunState.HpPct[0] : -1f;
            Say("结算前：Floor=" + beforeFloor + " 水灵血量=" + beforeHp.ToString("F2") + " 掉落=" + LingJing.Battle.RunState.Loot.Count);
            back.onClick.Invoke();
            phase = 3; frames = 0;
            return;
        }

        if (phase == 3)
        {
            if (SceneManager.GetActiveScene().name != "RunMap")
            {
                if (frames > 600) { Say("FAIL 点了回地图但没回到 RunMap（当前=" + SceneManager.GetActiveScene().name + "）"); Finish(); }
                return;
            }
            LingJing.Battle.RunState.Load();
            Say("OK 回到地图：Floor=" + LingJing.Battle.RunState.Floor +
                " 水灵血量=" + (LingJing.Battle.RunState.HpPct.Count > 0 ? LingJing.Battle.RunState.HpPct[0].ToString("F2") : "?") +
                " 掉落=" + LingJing.Battle.RunState.Loot.Count);
            Say("PASS 一境循环端到端跑通");
            ScreenCapture.CaptureScreenshot("D:/ljjh/tmp/run_e2e.png");
            Finish();
        }
    }

    static void Finish()
    {
        EditorPrefs.SetInt(KEY, 0);
        EditorApplication.Exit(0);
    }
}
