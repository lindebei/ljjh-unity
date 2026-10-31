using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 自动截图：打开 BattlePrototype、进 Play 模式，按「事件」而不是固定帧号抓图，然后退出编辑器。
/// 关键坑：EnterPlaymode 会触发域重载，静态字段和 update 回调全被清掉，
/// 所以用 EditorPrefs 存状态 + [InitializeOnLoadMethod] 在重载后重新挂回调。
/// 另一个坑：Play 模式在编辑器里帧率极高（deltaTime 很小），固定帧号根本走不到玩家回合，
/// 必须等 sim.awaitingPlayer != null 这种状态条件。
/// 跑法（必须 GUI 模式，不能 -batchmode）：
///   Unity.exe -projectPath D:\ljjh\unity -executeMethod BattleAutoShot.Run -logFile D:\ljjh\tmp\shot.log
/// </summary>
public static class BattleAutoShot
{
    static string ScenePath = "Assets/Scenes/BattlePrototype.unity";
    const string OutDir = "D:/ljjh/tmp/shots";
    const string PendingKey = "ljjh.autoshot.pending";

    static int frames;
    static int phase;
    static int waitFrames;
    static int exitCountdown;
    static bool equipShotTaken;
    static double startedAt;
    static float waitStartedAt;

    [InitializeOnLoadMethod]
    static void HookAfterDomainReload()
    {
        if (!EditorPrefs.GetBool(PendingKey, false)) return;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        Debug.Log("[BattleAutoShot] 域重载后重新挂上 update");
    }

        /// <summary>同上，但跑多波场景（4+3），验波次入场与 4 只卡片排版。</summary>
    public static void RunWaves()
    {
        ScenePath = "Assets/Scenes/BattleWaves.unity";
        Run();
    }

    /// <summary>换装界面单独截一张：进 Play 2 秒后抓图就退出，不依赖战斗状态。</summary>
    public static void RunEquip()
    {
        ScenePath = "Assets/Scenes/EquipScreen.unity";
        Run();
    }

public static void Run()
    {
        Directory.CreateDirectory(OutDir);
        EditorPrefs.SetBool(PendingKey, true);
        if (File.Exists(ScenePath)) EditorSceneManager.OpenScene(ScenePath);

        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        Debug.Log("[BattleAutoShot] 请求进入 Play 模式，场景=" + ScenePath);

        EditorApplication.EnterPlaymode();
    }

    static LingJing.UI.BattleHud Hud()
    {
        return Object.FindObjectOfType<LingJing.UI.BattleHud>();
    }

    static void Tick()
    {
        if (!EditorPrefs.GetBool(PendingKey, false))
        {
            EditorApplication.update -= Tick;
            return;
        }

        if (startedAt <= 0d) startedAt = EditorApplication.timeSinceStartup;

        if (!EditorApplication.isPlaying)
        {
            // 30 秒还没进 Play，说明被别的东西挡住了，直接放弃并报错退出
            if (EditorApplication.timeSinceStartup - startedAt > 30d)
            {
                Debug.LogError("[BattleAutoShot] 30 秒内没进入 Play 模式，放弃");
                Finish();
            }
            return;
        }

        frames++;
        if (frames == 1)
        {
            Debug.Log("[BattleAutoShot] 已进入 Play 模式");
            waitStartedAt = Time.realtimeSinceStartup;
        }

        if (exitCountdown > 0)
        {
            exitCountdown--;
            if (exitCountdown == 0) Finish();
            return;
        }

        var hud = Hud();
        // 换装界面没有 BattleHud：等界面搭完（90 帧）截一张，确认文件真的落盘再退
        if (hud == null)
        {
            if (frames >= 90 && !equipShotTaken)
            {
                Capture("equip_01.png");
                equipShotTaken = true;
                waitFrames = 0;
            }
            if (equipShotTaken)
            {
                waitFrames++;
                if (File.Exists(OutDir + "/equip_01.png") || waitFrames > 240)
                {
                    Debug.Log("[BattleAutoShot] 换装截图落盘：" + File.Exists(OutDir + "/equip_01.png"));
                    exitCountdown = 30;
                }
            }
            return;
        }

        var sim = hud.Simulator;
        if (sim == null) return;

        // 硬超时：整场 420 秒还没跑完就带着现有截图退出（自动战斗一场约 3–4 分钟，240 秒不够）
        if (Time.realtimeSinceStartup - waitStartedAt > 420f)
        {
            Debug.LogWarning("[BattleAutoShot] 420 秒超时，直接退出");
            Debug.Log("[BattleAutoShot] HUD 状态 " + hud.DebugState());
            Capture("99_timeout.png");
            Finish();
            return;
        }

        switch (phase)
        {
            case 0:
                // 开局：等到 60 帧，行动条已经有一点进度，卡片也画完了
                if (frames >= 60)
                {
                    Capture("01_open.png");
                    phase = 1;
                }
                break;

            case 1:
                // 等到轮到玩家出手。注意：检测到的那一帧 HUD 还没来得及跑 Update（按钮/横幅都还是空的），
                // 所以要再多等几帧才截图，否则截到的是一张「轮到我但按钮全空」的图。
                if (sim.awaitingPlayer != null)
                {
                    if (phase == 1) { phase = 10; waitFrames = 0; }
                }
                break;

            case 10:
                waitFrames++;
                if (waitFrames >= 6)
                {
                    // ScreenCapture.CaptureScreenshot 是「本帧末」写盘，所以这一帧绝对不能同时打开自动战斗，
                    // 否则截出来的是自动出手之后的画面（按钮变灰、横幅消失）——踩过一次。
                    Capture("02_player_turn.png");
                    phase = 11;
                }
                break;

            case 11:
                // 让截图先落盘，下一帧再切自动战斗
                if (sim.awaitingPlayer != null) Debug.Log("[BattleAutoShot] 轮到玩家：" + sim.awaitingPlayer.displayName);
                hud.autoBattle = true;
                // 编辑器窗口在后台被限帧时，真实时间跑得比 sim 时间快很多（实测 420 秒才推 37 秒战斗），
                // 自动阶段开时间加速，让整场在两分钟内能跑完
                hud.timeScale = 3f;
                phase = 12;
                break;

            case 12:
                // 等真的打出伤害（有飘字）再截，否则截到的是一张「已经打过但什么都没发生」的图
                if (hud.PopCount >= 3 && sim.round >= 2)
                {
                    Capture("03_auto.png");
                    phase = 13;
                }
                break;

            case 13:
                // 再抓一张「多段伤害同时飘」的画面（裂空斩三段/焚天三段时特别好看）
                if (hud.PopCount >= 6)
                {
                    Capture("03b_hits.png");
                    phase = 3;
                }
                break;

            case 2:
                // 自动战斗打到中期（有掉血、有状态）：等到第 3 回合，而不是按模拟时间
                if (sim.round >= 3)
                {
                    Capture("03_auto.png");
                    phase = 3;
                }
                break;

            case 3:
                if (sim.IsBattleOver)
                {
                    Capture("04_result.png");
                    exitCountdown = 30; // 留 30 帧让 PNG 落盘，再退出
                }
                break;
        }
    }

    static void Finish()
    {
        EditorPrefs.SetBool(PendingKey, false);
        EditorApplication.update -= Tick;
        EditorApplication.Exit(0);
    }

    static void Capture(string name)
    {
        string path = OutDir + "/" + name;

        // 只用异步 CaptureScreenshot：同步的 CaptureScreenshotAsTexture 在 D3D12 下
        // 直接把编辑器干崩了（CheckDeviceStatus → LaunchBugReporter），别再试。
        // 异步版本是「本帧末写盘」，所以调用后要么等够帧数、要么等文件真的出现再退出。
        ScreenCapture.CaptureScreenshot(path);
        Debug.Log("[BattleAutoShot] 截图 " + path + " frame=" + frames);

        var hud = Hud();
        if (hud != null) Debug.Log("[BattleAutoShot] HUD 状态 " + hud.DebugState());
    }
}
