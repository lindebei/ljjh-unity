# 灵境进化 · Unity 工程仓（ljjh-unity）

> 设计文档与全部数值以 <https://github.com/lindebei/ljjh-design> 为准，本仓只放工程与代码。
> 远端：<https://github.com/lindebei/ljjh-unity>（私有）

## 现状（2026-10-31）

- 工程脚手架已建，**Unity 尚未安装**，因此本仓里没有 `Library/`、没有 `.meta`、没有可播放的场景。
- 首次用 Unity 打开本目录时，编辑器会用 `Packages/manifest.json` 拉包、自动补齐 `ProjectSettings/` 其余文件并生成 `.meta`。
- `ProjectSettings/ProjectVersion.txt` 里现在写的是占位版本 `6000.0.0f1`，**装好 Unity 后必须改成实际安装的版本号**（见 `Docs/工程初始化待办.md`）。

## 技术栈（照设计文档）

| 项 | 取值 |
| --- | --- |
| 引擎 | Unity 6 LTS |
| 渲染 | URP（包在 Unity 装好后加，避免钉错版本号） |
| 构建 | v0.1 用 Mono 后端，**不用 IL2CPP** |
| 平台 | Steam PC（Windows） |
| UI | UGUI（`com.unity.ugui`），不用 UI Toolkit |

## 目录约定

```text
Assets/
  Art/Placeholder/      占位美术（方块/胶囊），v0.1 不做正式模型
  Prefabs/              战斗单位、UI 模块预制体
  Scenes/BattlePrototype.unity   v0.1 唯一场景
  Scripts/
    Battle/  BattleController.cs  ActionGauge.cs  BattleUnit.cs  SpiritResource.cs  SkillExecutor.cs  TargetSelector.cs
    Combat/  DamageCalculator.cs  StatusSystem.cs
    Data/    CreatureDefinition.cs  SkillDefinition.cs  TierConfig.cs
    UI/      BattleHUD 相关
  Settings/             URP / 输入 / 质量设置资产
Packages/               manifest.json
ProjectSettings/        ProjectVersion.txt
Docs/                   工程侧说明（设计文档不放这里，放 design 仓）
```

## 入库纪律（务必遵守）

1. **不提交** `Library/`、`Temp/`、`obj/`、`Logs/`、`Build/`、`Builds/`、`UserSettings/`、`*.csproj`、`*.sln`（已在 `.gitignore` 排除）。
2. **不用 LFS 托管** `.unity` / `.prefab` / `.asset` / `.mat` / `.anim` / `.controller`——它们是 YAML 文本，走 LFS 就无法 diff 与合并。`.gitattributes` 里已给它们 `merge=unityyamlmerge eol=lf`。
3. **二进制资源走 LFS**：png / psd / fbx / wav / mp3 / unitypackage / dll。
4. Unity 设置必须是：`Edit → Project Settings → Editor`：
   - Asset Serialization = **Force Text**
   - Version Control = **Visible Meta Files**
5. 提交前确认没有把 `_repo-meta-unity` 之类临时目录带进来。

## 协作分工

- 数值/流程/文档改动 → design 仓；改完同步到本仓的 ScriptableObject 或常量表。
- 代码改动 → 本仓，`*.cs` 只在这里。
- 数值唯一真相仍是 design 仓的 `一境阶段方案_数值与敌人.md`，本仓代码里的数字必须能指回那份文档。
