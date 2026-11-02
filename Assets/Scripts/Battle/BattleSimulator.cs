using System;
using System.Collections.Generic;
using UnityEngine;
using LingJing.Data;

namespace LingJing.Battle
{
    /// <summary>
    /// 战斗内核（无 UI、无 MonoBehaviour）。行动条按 0.5 秒节拍推进，
    /// 所有回合制规则（灵能、冷却、DoT、两段治疗、药品）都在这里实现一遍，
    /// 以后做界面时只读这里的状态，不再把规则抄进 UI 代码。
    /// </summary>
    public class BattleSimulator
    {
        public readonly BattleConfig config;
        readonly DamageCalculator calc;
        public readonly List<BattleUnit> units = new List<BattleUnit>();
        public readonly List<string> log = new List<string>();

        public bool verbose;

        /// <summary>true：轮到玩家时停下来等 UI 点按钮；false：全部自动出招（批量对账用）。</summary>
        public bool interactive;
        /// <summary>交互模式下正在等玩家操作的自己人。</summary>
        public BattleUnit awaitingPlayer;
        int enemyActionsThisRound;   // 本回合敌方已经行动几次（配合 maxEnemyActionsPerRound 排队）

        public int potions;
        public int mpPotions;      // 蓝瓶数量（回 MP，占一次行动）
        public float time;
        public int round = 1;
        public int playerActionsTotal;
        int playerActionsThisRound;

        float meanSpd;
        float meanSpr;
        readonly System.Random rng;

        // ---- 波次：一境设计允许一场战斗分多波入场（每波 ≤4 只，总数十只级别），
        //      一波清完不判胜利，直接入下一波。见 一境战斗流程与状态机 第三节 WaveEnter。
        readonly List<List<BattleUnit>> waves = new List<List<BattleUnit>>();
        List<BattleUnit> building;
        public int currentWave { get; private set; }
        public int WaveTotal { get { return waves.Count; } }

        /// <summary>最大一波的只数——UI 按这个算卡片宽度，不用管总共有多少批。</summary>
        public int MaxWaveSize
        {
            get
            {
                int m = 0;
                for (int i = 0; i < waves.Count; i++) m = Mathf.Max(m, waves[i].Count);
                return m;
            }
        }
        readonly Dictionary<BattleUnit, int> focusThisRound = new Dictionary<BattleUnit, int>();
        /// <summary>最近一次建队时读到的装备战力，供 UI / 对账报告显示。</summary>
        public float lastEquipPower;

        /// <summary>本场灵能上限 = 配置值 + 装备给的 +N（取全队最大）。</summary>

        public BattleSimulator(BattleConfig config, int seed = 20261031)
        {
            this.config = config;
            this.rng = new System.Random(seed);           // 必须先建 rng，下面 calc 要用它
            this.calc = new DamageCalculator(config, this.rng);
        }

        // ------------------------------------------------------------------ 掉落

        /// <summary>本场掉到的装备 id（重复不合并，交给背包层堆叠）。</summary>
        public readonly List<string> loot = new List<string>();

        /// <summary>
        /// 怪死的一瞬间掷一次骰子。放在每拍结算后统一扫，是因为死法有普攻、多段、DoT 三种，
        /// 在 TakeDamage 里挂事件反而要额外判断「这次是不是最后一击」。
        /// </summary>
        void CollectLoot()
        {
            for (int i = 0; i < units.Count; i++)
            {
                var u = units[i];
                if (u.side != Side.Enemy || u.IsAlive || u.lootRolled) continue;
                u.lootRolled = true;
                Loot.Roll(u.id, rng, loot);
            }
        }

        // ------------------------------------------------------------------ 建队

        public BattleUnit AddPlayer(CreatureDefinition def, int level, int[] freePoints, EquipmentLoadout loadout = null, LingJing.Data.SkillDefinition[] equipped = null)
        {
            var s = BattleFormulas.PlayerStats(def.baseStats, level, def.potentials, freePoints);
            if (loadout != null && !loadout.IsEmpty())
            {
                s = loadout.Apply(s);
                lastEquipPower = loadout.Power();
            }
            var u = new BattleUnit
            {
                def = def,
                side = Side.Player,
                level = level,
                index = units.Count,
                id = def.id,
                displayName = def.displayName,
                element = def.element,
                quality = Quality.Normal,
                stats = s,
                maxHp = BattleFormulas.MaxHp(s.vit, level)
            };
            u.hp = u.maxHp;
            if (equipped != null && equipped.Length > 0)
                for (int k = 0; k < u.equipped.Length && k < equipped.Length; k++) u.equipped[k] = equipped[k];
            // 治疗者标记：敌方 AI 靠它决定「先咬谁」
            u.isHealer = false;
            for (int k = 0; k < u.equipped.Length; k++)
                if (u.equipped[k] != null && (u.equipped[k].isHeal || u.equipped[k].healAllAllies)) { u.isHealer = true; break; }
            if (!u.isHealer && def != null)
            {
                var known = def.KnownSkills(level);
                if (known != null)
                    for (int k = 0; k < known.Length; k++)
                        if (known[k] != null && (known[k].isHeal || known[k].healAllAllies)) { u.isHealer = true; break; }
            }
            u.equipPower = loadout != null ? loadout.Power() : 0f;
            u.power = BattleFormulas.Power(s, level, u.equipPower);
            units.Add(u);
            return u;
        }

        public BattleUnit AddEnemy(CreatureDefinition def, int level, int bandIndex)
        {
            var s = BattleFormulas.EnemyStats(def.baseStats, level);
            int qi = (int)def.quality;
            // 品质强度拆成「血」和「攻」两把独立旋钮：
            // 只调一个数会同时把血和攻拉高，结果胜负被 30 回合上限左右（要么秒掉、要么打不完）。
            float qScale = ((config.qualityPowerScale != null && qi < config.qualityPowerScale.Length) ? config.qualityPowerScale[qi] : 1f);
            float qHp = (config.qualityHpScale != null && qi < config.qualityHpScale.Length) ? config.qualityHpScale[qi] : qScale;
            float qAtk = (config.qualityAtkScale != null && qi < config.qualityAtkScale.Length) ? config.qualityAtkScale[qi] : qScale;
            // 波次衰减：第 2 波起逐波变弱，让「10 只普通怪分三波」打得过（单波不受影响）
            int waveIndex = Mathf.Max(0, waves.Count - 1);
            float waveDecay = (waveIndex > 0 && config.wavePowerDecay > 0f && config.wavePowerDecay < 1f)
                ? Mathf.Pow(config.wavePowerDecay, waveIndex) : 1f;
            float hpMul = config.qualityHpMul[qi] * config.levelBandHpMul[bandIndex] * config.enemyPowerScale * qHp * waveDecay;
            float atkMul = config.qualityAtkMul[qi] * config.levelBandAtkMul[bandIndex] * config.enemyPowerScale * qAtk * waveDecay;

            var u = new BattleUnit
            {
                def = def,
                side = Side.Enemy,
                level = level,
                index = units.Count,
                id = def.id,
                displayName = def.displayName,
                element = def.element,
                quality = def.quality,
                stats = s,
                atkMul = atkMul,
                defMul = config.qualityDefMul[qi],
                spdMul = config.qualitySpdMul[qi],
                maxHp = BattleFormulas.MaxHp(s.vit, level) * hpMul
            };
            u.hp = u.maxHp;
            u.power = BattleFormulas.Power(s, level, 0f) * qScale;   // 战力含品质与等级带放大，用于展示/遇敌匹配
            u.inField = false;              // 等 Begin()/入波时再上场
            units.Add(u);

            if (building == null)
            {
                building = new List<BattleUnit>();
                waves.Add(building);
            }
            building.Add(u);
            return u;
        }

        /// <summary>开场：算归一系数（我方开场速度均值，整场锁死）、灵能起手、药品数。</summary>
        public void Begin()
        {
            var players = Alive(Side.Player);
            float sumSpd = 0f, sumSpr = 0f;
            for (int i = 0; i < players.Count; i++)
            {
                sumSpd += players[i].EffectiveSpd;
                sumSpr += players[i].stats.spr;
            }
            meanSpd = players.Count > 0 ? sumSpd / players.Count : 100f;
            meanSpr = players.Count > 0 ? sumSpr / players.Count : 60f;

            for (int i = 0; i < units.Count; i++)
            {
                var u = units[i];
                if (u.side != Side.Player) continue;
                u.mpMax = BattleFormulas.MpMax(u.stats.spr);
                u.mp = u.mpMax;                        // 开局满蓝（用户要求）
            }
            potions = config.potionsPerBattle;
            mpPotions = config.mpPotionsPerBattle;
            time = 0f;
            round = 1;
            playerActionsThisRound = 0;
            playerActionsTotal = 0;

            // 起手行动值：按各自速度拉开一点，避免所有人同一节拍撞车
            for (int i = 0; i < units.Count; i++)
                units[i].gauge = 0f;

            // 敌方第一波上场（其余批次留在 waves 里，等清完再入）
            currentWave = 0;
            SpawnWave(0);

            Log(string.Format("开场：我方速度均值 {0:F0}，精神均值 {1:F0}，开局 MP {2}，药品 {3} 个，共 {4} 波",
                meanSpd, meanSpr, units.Count > 0 ? units[0].mp : 0, potions, Mathf.Max(1, waves.Count)));
        }

        /// <summary>关闭当前波次：之后 AddEnemy 的敌人都归到下一波。</summary>
        public void EndWave()
        {
            building = null;
        }

        void SpawnWave(int index)
        {
            if (index < 0 || index >= waves.Count) return;
            var list = waves[index];
            for (int i = 0; i < list.Count; i++)
            {
                list[i].inField = true;
                list[i].gauge = 0f;
            }
            if (index > 0)
            {
                // 波次喘息：不给补给的话，10 只怪（3 波）就是纯堆数量——我方每回合 3 次行动、
                // 敌方 10 次，数学上必输（实测 0% 胜）。给一口奶 + 灵能，多波才变成资源管理。
                var allies = Alive(Side.Player);
                for (int i = 0; i < allies.Count; i++)
                {
                    float heal = allies[i].maxHp * config.waveClearHealPercent;
                    if (heal > 0f) allies[i].Heal(heal);
                }
                for (int i = 0; i < units.Count; i++)
            {
                var u = units[i];
                if (u.side != Side.Player || !u.IsAlive) continue;
                u.mp = Mathf.Min(u.mpMax, u.mp + config.waveClearSpirit);
            }
                Log(string.Format("第 {0}/{1} 波入场：{2} 只（我方回复 {3:P0} 生命、灵能 +{4}）",
                    index + 1, waves.Count, list.Count, config.waveClearHealPercent, config.waveClearSpirit));
            }
        }

        /// <summary>当前波已清空且还有后续波 → 直接入下一波（不判胜利）。</summary>
        bool AdvanceWaveIfCleared()
        {
            if (Alive(Side.Enemy).Count > 0) return false;
            if (currentWave + 1 >= waves.Count) return false;
            currentWave++;
            focusThisRound.Clear();
            SpawnWave(currentWave);
            return true;
        }

        // ------------------------------------------------------------------ 主循环

        public BattleResult Run(float timeCap = 0f)
        {
            float cap = timeCap > 0f ? timeCap : config.timeCapSeconds;
            bool timedOut = false;
            bool keep = interactive;
            interactive = false;   // 批量模式永远自动出招

            while (true)
            {
                if (IsBattleOver) break;
                if (time >= cap) { timedOut = true; break; }
                StepOnce();
            }

            interactive = keep;
            return BuildResult(timedOut);
        }

        /// <summary>回合硬上限打满：回合数超过上限而敌方还有活口 = 判负（2026-11-01 用户定）。</summary>
        public bool RoundLimitReached
        {
            get { return config.maxRounds > 0 && round > config.maxRounds; }
        }

        public bool IsBattleOver
        {
            get
            {
                if (Alive(Side.Player).Count == 0) return true;
                if (RoundLimitReached) return true;          // 20 回合没清完 → 结束（按失败结算）
                if (Alive(Side.Enemy).Count > 0) return false;
                // 敌方空了：还有后续波就不算结束
                return currentWave + 1 >= waves.Count;
            }
        }

        /// <summary>
        /// 推进一拍。返回 true 表示这拍有人出手（交互模式下则是「轮到玩家了，等你点按钮」）。
        /// 交互模式时把出手权交出去，UI 调 ResolvePlayerTurn 才真正结算。
        /// </summary>
        public bool StepOnce()
        {
            if (IsBattleOver) return false;

            // 上一波刚清空 → 下一波上场，同一拍继续推进
            AdvanceWaveIfCleared();

            time += config.tickSeconds;

            var live = Alive(null);
            for (int i = 0; i < live.Count; i++)
                live[i].gauge += BattleFormulas.GaugeGainPerTick(live[i].EffectiveSpd, meanSpd, config.gaugeGainPerTick);

            BattleUnit actor = null;
            float best = -1f;
            for (int i = 0; i < live.Count; i++)
            {
                if (live[i].gauge < config.gaugeThreshold || live[i].gauge <= best) continue;
                // 本回合敌方行动次数到上限 → 这只排队等下一回合（行动值不清零，优先度保留）
                if (live[i].side == Side.Enemy && config.maxEnemyActionsPerRound > 0
                    && enemyActionsThisRound >= config.maxEnemyActionsPerRound) continue;
                best = live[i].gauge;
                actor = live[i];
            }
            if (actor == null) return false;

            actor.gauge = 0f;
            actor.actionsTaken++;

            // 眩晕：这一次行动直接跳过（行动值仍然清零，等于白白浪费一拍）
            if (actor.HasStatus(StatusType.Stun))
            {
                actor.RemoveStatus(StatusType.Stun);
                Log(actor.displayName + " 被眩晕，跳过这次行动");
                AfterAction(actor);
                return true;
            }

            if (actor.side == Side.Player && interactive)
            {
                awaitingPlayer = actor;
                return true;
            }

            if (actor.side == Side.Enemy) enemyActionsThisRound++;
            TakeAction(actor);
            CollectLoot();
            return true;
        }

        /// <summary>UI 在等待玩家时调用，并指定打谁（单体技能用；给 null 就按默认目标规则挑）。</summary>
        public void ResolvePlayerTurn(SkillDefinition skill, BattleUnit chosenTarget)
        {
            var actor = awaitingPlayer;
            awaitingPlayer = null;
            if (actor == null || !actor.IsAlive) return;

            if (skill == null) { ResolvePlayerTurnInternal(actor, null, null); return; }

            playerActionsTotal++;
            playerActionsThisRound++;

            // 只有「单体」技能才使用玩家点的目标；群体技能一律按技能自己的目标范围算，
            // 否则全体技能会因为带着一个点击目标而只打一只（用户在试玩里踩到过）。
            var eff = SkillTargeting.EffectiveSide(skill);
            bool singleTargetSide = eff == TargetSide.Enemy
                                    || eff == TargetSide.Ally
                                    || eff == TargetSide.Self;
            List<BattleUnit> targets;
            if (singleTargetSide && chosenTarget != null && chosenTarget.IsAlive && chosenTarget.inField)
                targets = new List<BattleUnit> { chosenTarget };
            else
                targets = PickPlayerTargets(actor, skill);

            Cast(actor, skill, targets);
            CollectLoot();
            AfterAction(actor);
        }

        void ResolvePlayerTurnInternal(BattleUnit actor, SkillDefinition skill, List<BattleUnit> targets)
        {
            if (skill == null)
            {
                skill = actor.def.Skill(SkillType.Basic);
                if (skill == null)
                {
                    Log(actor.displayName + " 没有可用技能，跳过");
                    actor.gauge = 0f;
                    AfterAction(actor);
                    return;
                }
            }
            Cast(actor, skill, targets ?? PickPlayerTargets(actor, skill));
            CollectLoot();
            AfterAction(actor);
        }

        /// <summary>UI 在等待玩家时调用：用指定技能结算这个行动。</summary>
        public void ResolvePlayerTurn(SkillDefinition skill)
        {
            var actor = awaitingPlayer;
            awaitingPlayer = null;
            if (actor == null || !actor.IsAlive) return;

            // 兜底：调用方没给技能（灵能不够 / 技能表缺 basic）也要把这一手走完，
            // 否则行动值不归零、下一拍又轮到同一个单位，战斗会原地死循环（踩过一次，自动战斗整场卡死）。
            if (skill == null)
            {
                skill = actor.def.Skill(SkillType.Basic);
                if (skill == null)
                {
                    Log(actor.displayName + " 没有可用技能，跳过");
                    actor.gauge = 0f;
                    AfterAction(actor);
                    return;
                }
            }

            playerActionsTotal++;
            playerActionsThisRound++;

            var targets = PickPlayerTargets(actor, skill);
            Cast(actor, skill, targets);
            CollectLoot();
            AfterAction(actor);
        }

        BattleResult BuildResult(bool timedOut)
        {
            var ps = Alive(Side.Player);
            var es = Alive(Side.Enemy);
            float php = 0f, pmax = 0f, ehp = 0f, emax = 0f;
            for (int i = 0; i < units.Count; i++)
            {
                if (units[i].side == Side.Player)
                {
                    pmax += units[i].maxHp;
                    php += Mathf.Max(0f, units[i].hp);
                }
                else
                {
                    emax += units[i].maxHp;
                    ehp += Mathf.Max(0f, units[i].hp);
                }
            }
            return new BattleResult
            {
                durationSeconds = time,
                playerHpPercent = pmax > 0f ? php / pmax : 0f,
                playersAlive = ps.Count,
                enemyHpPercent = emax > 0f ? ehp / emax : 0f,
                rounds = round,
                playerActions = playerActionsTotal,
                timedOut = timedOut,
                loot = new List<string>(loot)
            };
        }

        void TakeAction(BattleUnit actor)
        {
            if (actor.side == Side.Player)
            {
                playerActionsTotal++;
                playerActionsThisRound++;

                if (TryUsePotion(actor))
                {
                    AfterAction(actor);
                    return;
                }

                var skill = ChoosePlayerSkill(actor);
                var targets = PickPlayerTargets(actor, skill);
                Cast(actor, skill, targets);
            }
            else
            {
                var skill = ChooseEnemySkill(actor);
                var targets = PickEnemyTargets(actor, skill);
                Cast(actor, skill, targets);
            }

            AfterAction(actor);
        }

        void AfterAction(BattleUnit actor)
        {
            if (actor.side != Side.Player) return;
            if (playerActionsThisRound >= config.playerActionsPerRound)
                EndRound();
        }

        /// <summary>交互模式下玩家点「药品」。</summary>
        public void ResolvePlayerPotion()
        {
            var actor = awaitingPlayer;
            awaitingPlayer = null;
            if (actor == null || !actor.IsAlive) return;

            playerActionsTotal++;
            playerActionsThisRound++;
            if (!TryUsePotion(actor))
                Log(actor.displayName + " 现在不需要药品（没有低于 35% 血的自己人）");
            AfterAction(actor);
        }

        // ------------------------------------------------------------------ 回合结算

        void EndRound()
        {
            playerActionsThisRound = 0;
            enemyActionsThisRound = 0;      // 敌方每回合行动配额重置
            for (int i = 0; i < units.Count; i++) units[i].TickRoundCooldowns();   // 奥义按回合计的冷却
            round++;
            focusThisRound.Clear();      // 怪的目标分散上限每回合重置

            // 1) 治愈标记：下一回合开始回第一段的一半
            var all = Alive(null);
            for (int i = 0; i < all.Count; i++)
            {
                var mark = all[i].GetStatus(StatusType.HealMark);
                if (mark != null)
                {
                    all[i].Heal(mark.value);
                    all[i].RemoveStatus(StatusType.HealMark);
                }
            }

            // 2) DoT：每层 0.5% 最大生命，灼烧与中毒独立叠
            for (int i = 0; i < all.Count; i++)
            {
                var u = all[i];
                int burn = u.DotStacks(StatusType.Burn);
                int poison = u.DotStacks(StatusType.Poison);
                float dmg = 0f;
                if (burn > 0) dmg += u.maxHp * config.dotPercentPerStack * burn;
                if (poison > 0) dmg += u.maxHp * config.dotPercentPerStack * poison;
                if (dmg > 0f)
                {
                    u.TakeDamage(dmg);
                    Log(string.Format("回合{0} DoT：{1} 受 {2:F0}（灼烧{3}层/中毒{4}层）", round, u.displayName, dmg, burn, poison));
                }
            }

            // 3) 冷却、增益、状态时长
            all = Alive(null);
            for (int i = 0; i < all.Count; i++)
            {
                var u = all[i];
                u.TickCooldowns();
                if (u.atkBuffRounds > 0) { u.atkBuffRounds--; if (u.atkBuffRounds == 0) u.atkBuff = 0f; }
                if (u.spdBuffRounds > 0) { u.spdBuffRounds--; if (u.spdBuffRounds == 0) u.spdBuff = 0f; }
                if (u.tauntRounds > 0) u.tauntRounds--;

                for (int k = u.statuses.Count - 1; k >= 0; k--)
                {
                    if (u.statuses[k].type == StatusType.HealMark) continue;
                    u.statuses[k].roundsLeft--;
                    if (u.statuses[k].roundsLeft <= 0) u.statuses.RemoveAt(k);
                }
            }

            // 4) 灵能自然回复
            if (config.spiritRegenEveryRounds > 0 && round % config.spiritRegenEveryRounds == 0)
            {
                int regen = BattleFormulas.SpiritRegen(meanSpr);
                if (regen > 0)
                {
                    for (int i = 0; i < units.Count; i++)
                    {
                        var u = units[i];
                        if (u.side != Side.Player || !u.IsAlive) continue;
                        u.mp = Mathf.Min(u.mpMax, u.mp + regen);
                    }
                    Log(string.Format("回合{0} MP 自然回复 +{1}", round, regen));
                }
            }

            CollectLoot();   // DoT 也能打死怪，这里补一次
        }

        // ------------------------------------------------------------------ 玩家侧

        /// <summary>自动战斗/UI 共用：现在该不该喝药（有人低于阈值且还有药）。</summary>
        /// <summary>该不该喝蓝瓶：有蓝瓶、且某个要出奥义/主动的我方蓝不够。</summary>
        public bool ShouldUseMpPotion()
        {
            if (mpPotions <= 0) return false;
            var actor = awaitingPlayer;
            if (actor != null) return actor.mp < Mathf.Min(4, actor.mpMax) && actor.mp < actor.mpMax;
            for (int i = 0; i < units.Count; i++)
            {
                var u = units[i];
                if (u.side == Side.Player && u.IsAlive && u.mp < u.mpMax * 0.4f) return true;
            }
            return false;
        }

        /// <summary>喝蓝瓶：回 MP，占掉这次行动。</summary>
        public void ResolvePlayerMpPotion()
        {
            var actor = awaitingPlayer;
            awaitingPlayer = null;
            if (actor == null || !actor.IsAlive) return;
            if (!TryUseMpPotion(actor))
            {
                Log("没有蓝瓶了");
                awaitingPlayer = actor;
                return;
            }
            playerActionsTotal++;
            playerActionsThisRound++;
            AfterAction(actor);
        }

        bool TryUseMpPotion(BattleUnit actor)
        {
            if (mpPotions <= 0) return false;
            int before = actor.mp;
            actor.mp = Mathf.Min(actor.mpMax, actor.mp + config.mpPotionRestore);
            mpPotions--;
            Log(string.Format("{0} 喝魔药：MP {1} → {2}（剩 {3} 瓶）", actor.displayName, before, actor.mp, mpPotions));
            return true;
        }

        public bool ShouldUsePotion()
        {
            if (potions <= 0) return false;
            var ps = Alive(Side.Player);
            for (int i = 0; i < ps.Count; i++)
                if (ps[i].HpPercent < config.potionTriggerHpPercent) return true;
            return false;
        }

        bool TryUsePotion(BattleUnit actor)
        {
            if (potions <= 0) return false;
            var ps = Alive(Side.Player);
            BattleUnit target = null;
            float lowest = 1f;
            for (int i = 0; i < ps.Count; i++)
            {
                if (ps[i].HpPercent < config.potionTriggerHpPercent && ps[i].HpPercent < lowest)
                {
                    lowest = ps[i].HpPercent;
                    target = ps[i];
                }
            }
            if (target == null) return false;

            potions--;
            float healed = target.Heal(target.maxHp * config.potionHealPercent);
            Log(string.Format("{0} 用药品：{1} 回 {2:F0}（剩 {3} 个）", actor.displayName, target.displayName, healed, potions));
            return true;
        }

        /// <summary>
        /// 玩家侧自动策略：奥义（灵能够 + 冷却好）＞ 治疗/辅助（触发条件）＞ 主动 ＞ 普攻。
        /// v0.1 先按定位硬编码，等自动战斗配置表做完再挪进数据。
        /// </summary>
        public SkillDefinition ChoosePlayerSkill(BattleUnit actor)
        {
            var basic = actor.def.Skill(SkillType.Basic);
            var active = actor.def.Skill(SkillType.Active);
            var support = actor.def.Skill(SkillType.Support);
            var ult = actor.def.Skill(SkillType.Ultimate);

            var allies = Alive(Side.Player);
            int lowAllies = 0;
            for (int i = 0; i < allies.Count; i++) if (allies[i].HpPercent < 0.65f) lowAllies++;

            // 多怪阵容：3 只以上先扔群体技能。只有风灵会群攻，不这样排的话
            // 面对一波 4 只，玩家 AI 永远在砍单体，群攻等于没做。
            var enemies = Alive(Side.Enemy);
            if (enemies.Count >= 3)
            {
                var aoe = BestAoe(actor);
                if (aoe != null) return aoe;
            }

            bool ultReady = ult != null && actor.Cooldown(ult.id) <= 0 && actor.mp >= LingJing.Data.SkillRanks.Cost(ult);

            // 水灵：治疗优先，两三个人半血才交奥义
            if (actor.id == "beast_water")
            {
                if (ultReady && lowAllies >= 2) return ult;
                if (support != null && actor.mp >= LingJing.Data.SkillRanks.Cost(support) && lowAllies >= 1) return support;
                if (active != null && actor.mp >= LingJing.Data.SkillRanks.Cost(active)) return active;
                return basic;
            }

            // 土灵：血线不健康时先磐石（护盾 + 嘲讽）
            if (actor.id == "beast_earth")
            {
                if (ultReady) return ult;
                if (support != null && actor.mp >= LingJing.Data.SkillRanks.Cost(support) && actor.HpPercent < 0.75f) return support;
                if (active != null && actor.mp >= LingJing.Data.SkillRanks.Cost(active)) return active;
                return basic;
            }

            // 火灵 / 风灵：奥义 ＞ 主动 ＞ 普攻
            if (ultReady) return ult;
            if (active != null && actor.mp >= LingJing.Data.SkillRanks.Cost(active)) return active;
            return basic;
        }

        /// <summary>该角色当前能放、且是群体伤害的技能里最贵的那一个（奥义 &gt; 主动）。</summary>
        SkillDefinition BestAoe(BattleUnit actor)
        {
            var ult = actor.def.Skill(SkillType.Ultimate);
            if (ult != null && ult.targetSide == TargetSide.AllEnemies && ult.multiplier > 0f
                && actor.Cooldown(ult.id) <= 0 && actor.mp >= LingJing.Data.SkillRanks.Cost(ult)) return ult;

            var active = actor.def.Skill(SkillType.Active);
            if (active != null && active.targetSide == TargetSide.AllEnemies && active.multiplier > 0f
                && actor.Cooldown(active.id) <= 0 && actor.mp >= LingJing.Data.SkillRanks.Cost(active)) return active;

            return null;
        }

        List<BattleUnit> PickPlayerTargets(BattleUnit actor, SkillDefinition skill)
        {
            var list = new List<BattleUnit>();
            if (skill == null) return list;

            switch (SkillTargeting.EffectiveSide(skill))
            {
                case TargetSide.Enemy:
                {
                    var enemies = Alive(Side.Enemy);
                    // 集火：血最少的
                    BattleUnit t = null;
                    float low = float.MaxValue;
                    for (int i = 0; i < enemies.Count; i++)
                        if (enemies[i].hp < low) { low = enemies[i].hp; t = enemies[i]; }
                    if (t != null) list.Add(t);
                    return list;
                }
                case TargetSide.AllEnemies:
                {
                    list.AddRange(Alive(Side.Enemy));
                    return list;
                }
                case TargetSide.Ally:
                case TargetSide.Self:
                {
                    if (skill.isHeal)
                    {
                        var allies = Alive(Side.Player);
                        BattleUnit t = null;
                        float low = float.MaxValue;
                        for (int i = 0; i < allies.Count; i++)
                            if (allies[i].HpPercent < low) { low = allies[i].HpPercent; t = allies[i]; }
                        if (t != null) list.Add(t);
                    }
                    else list.Add(actor);
                    return list;
                }
                case TargetSide.AllAllies:
                {
                    list.AddRange(Alive(Side.Player));
                    return list;
                }
            }
            return list;
        }

        // ------------------------------------------------------------------ 敌方侧

        public SkillDefinition ChooseEnemySkill(BattleUnit actor)
        {
            var set = ResolveEnemySet(actor.def);
            if (set == null) return actor.def.Skill(SkillType.Basic);
            if (set.basic == null) return actor.def.Skill(SkillType.Basic);

            // 1–5 级只会普攻（AI 行为规则）
            if (actor.level <= 5) return set.basic;

            bool isElite = actor.quality != Quality.Normal;
            bool isBoss = actor.quality == Quality.MiniBoss || actor.quality == Quality.MidBoss || actor.quality == Quality.BigBoss;

            if (isBoss && set.ultimate != null
                && actor.HpPercent <= set.ultimateUnlockHpPercent
                && actor.Cooldown(set.ultimate.id) <= 0)
                return set.ultimate;

            if (isElite && set.aoe != null && actor.Cooldown(set.aoe.id) <= 0
                && Roll(set.aoeChanceWhenReady))
                return set.aoe;

            if (set.active != null && actor.Cooldown(set.active.id) <= 0
                && (actor.quality == Quality.Normal ? Roll(set.activeChanceWhenReady) : true))
                return set.active;

            return set.basic;
        }

        bool Roll(float chance)
        {
            if (config.useExpectedValue) return true;   // 期望值模式：能放就放，保证可复现
            return rng.NextDouble() < chance;
        }

        EnemySkillSet ResolveEnemySet(CreatureDefinition def)
        {
            return BattleDatabase.GetEnemySkillSet(def.element);
        }

        List<BattleUnit> PickEnemyTargets(BattleUnit actor, SkillDefinition skill)
        {
            var list = new List<BattleUnit>();
            if (skill == null) return list;
            var players = Alive(Side.Player);
            if (players.Count == 0) return list;

            if (skill.targetSide == TargetSide.AllEnemies || skill.targetSide == TargetSide.AllAllies)
            {
                list.AddRange(players);
                return list;
            }

            // 嘲讽优先：土灵【磐石】的存在意义，不能被随机目标稀释
            for (int i = 0; i < players.Count; i++)
                if (players[i].tauntRounds > 0 && players[i].IsAlive)
                {
                    list.Add(players[i]);
                    return list;
                }

            // 精英/Boss 的威胁：主动与奥义能同时压 2–3 个人。
            // 只打一个的话，治疗量必然盖过单体 DPS，胜负只剩「秒杀」和「打不动」两种（二元断崖）。
            int want = 1;
            if (skill.type == SkillType.Active || skill.type == SkillType.Ultimate)
            {
                if (actor.quality >= Quality.MidBoss) want = Mathf.Max(1, config.enemyBossTargets);
                else if (actor.quality >= Quality.Elite) want = Mathf.Max(1, config.enemyEliteTargets);
            }
            if (want > 1)
            {
                var pool = new List<BattleUnit>(players);
                // 按威胁分排序：治疗 > 残血 > 脆皮 > 已被盯上的（集火补刀）
                pool.Sort((a, b) =>
                {
                    int fa, fb;
                    focusThisRound.TryGetValue(a, out fa);
                    focusThisRound.TryGetValue(b, out fb);
                    return b.ThreatScore(fb).CompareTo(a.ThreatScore(fa));
                });
                for (int i = 0; i < pool.Count && list.Count < want; i++)
                    if (!list.Contains(pool[i])) list.Add(pool[i]);
                for (int i = 0; i < list.Count; i++)
                {
                    int f2;
                    focusThisRound.TryGetValue(list[i], out f2);
                    focusThisRound[list[i]] = f2 + 1;
                }
                return list;
            }

            // 目标分散：同一回合里每个我方单位最多被 2 只怪盯上。
            // 不限制的话（原来直接挑血最少的），一波 4 只怪会全部砸同一个人，见谁秒谁。
            BattleUnit t = null;
            float bestScore = float.MinValue;
            for (int i = 0; i < players.Count; i++)
            {
                int f;
                if (!focusThisRound.TryGetValue(players[i], out f)) f = 0;
                if (f >= 2) continue;                       // 同一个我方一回合最多被 2 只怪盯
                float sc = players[i].ThreatScore(f);
                if (sc > bestScore) { bestScore = sc; t = players[i]; }
            }

            // 都被盯满 2 次了（怪特别多时）→ 退回「威胁分最高的那个」，不再按纯血量
            if (t == null)
            {
                float fallback = float.MinValue;
                for (int i = 0; i < players.Count; i++)
                {
                    float sc = players[i].ThreatScore(0);
                    if (sc > fallback) { fallback = sc; t = players[i]; }
                }
            }

            if (t != null)
            {
                int f;
                focusThisRound.TryGetValue(t, out f);
                focusThisRound[t] = f + 1;
                list.Add(t);
            }
            return list;
        }

        // ------------------------------------------------------------------ 结算一个技能

        /// <summary>有人开始放技能时广播（UI 用来播技能名横幅、让出手者的卡抖动一下）。</summary>
        public event System.Action<BattleUnit, SkillDefinition> ActionStarted;

        /// <summary>调试用：整场跑下来全体技能被放了几次（清场效率旋钮是否真的生效）。</summary>
        public static int aoeCastCount;
        public static float lastAoeScale = -1f;

        public void Cast(BattleUnit actor, SkillDefinition skill, List<BattleUnit> targets)
        {
            if (skill == null) return;

            if (ActionStarted != null) ActionStarted(actor, skill);

            // 灵能：走技能等级折算后的有效蓝耗
            int cost = LingJing.Data.SkillRanks.Cost(skill);
            if (cost < 0) actor.mp = Mathf.Min(actor.mpMax, actor.mp + 1);
            else actor.mp = Mathf.Max(0, actor.mp - cost);

            // 奥义（SkillType.Ultimate）用「回合」冷却：放完这回合算起，两回合内不许再放（MP 够也不行）
            // 注意：tier 4 里既有一转奥义也有后续主动，判据必须看 type 而不是 tier
            if (skill.type == SkillType.Ultimate)
                actor.SetRoundCooldown(skill.id, Mathf.Max(1, config.ultCooldownRounds));
            else
            {
                int cd = LingJing.Data.SkillRanks.Cooldown(skill);
                if (cd > 0) actor.SetCooldown(skill.id, cd);
            }
            if (skill.taunt) actor.tauntRounds = Mathf.Max(1, skill.controlDuration);

            // 治疗（两段式）
            if (skill.isHeal)
            {
                bool healAll = skill.healAllAllies || SkillTargeting.EffectiveSide(skill) == TargetSide.AllAllies;
                var healTargets = healAll ? Alive(Side.Player) : targets;
                // 系数按「每个目标」单独算：减疗（HealDown）是挂在被治疗者身上的
                for (int i = 0; i < healTargets.Count; i++)
                {
                    var t = healTargets[i];
                    if (!t.IsAlive) continue;
                    float first = t.MissingHp * calc.HealFirstRatio(actor, t);
                    float cap = t.maxHp * config.healCapPercent;
                    if (cap > 0f && first > cap) first = cap;
                    float healed = t.Heal(first);
                    t.ApplyStatus(StatusType.HealMark, 1, 2, healed * config.healSecondRatio, config.dotMaxStacks);
                    if (skill.dispelOne) t.ClearOneDebuff();
                    Log(string.Format("{0} 治疗 {1}：+{2:F0}（第二段 {3:F0} 待结算）",
                        actor.displayName, t.displayName, healed, healed * config.healSecondRatio));
                }
            }

            // 护盾
            if (skill.isShield)
            {
                for (int i = 0; i < targets.Count; i++)
                {
                    var t = targets[i];
                    if (!t.IsAlive) continue;
                    float amount = t.maxHp * skill.shieldPercent;
                    if (t.HasStatus(StatusType.Wet)) amount *= 1.25f;
                    t.shield += amount;
                    if (skill.dispelOne) t.ClearOneDebuff();
                    Log(string.Format("{0} 给 {1} 上盾 {2:F0}", actor.displayName, t.displayName, amount));
                }
            }

            // 增益
            if (skill.buffPercent > 0f)
            {
                var buffTargets = SkillTargeting.EffectiveSide(skill) == TargetSide.AllAllies ? Alive(Side.Player) : targets;
                for (int i = 0; i < buffTargets.Count; i++)
                {
                    var t = buffTargets[i];
                    if (skill.buffIsAttack) { t.atkBuff = skill.buffPercent; t.atkBuffRounds = skill.buffDuration; }
                    else { t.spdBuff = skill.buffPercent; t.spdBuffRounds = skill.buffDuration; }
                }
            }

            // 伤害
            if (skill.multiplier > 0f)
            {
                var enemies = SkillTargeting.EffectiveSide(skill) == TargetSide.AllEnemies ? Alive(Side.Enemy) : targets;
                if (enemies.Count > 0)
                {
                    // 全体技能额外吃一个「清场效率」系数（config.aoeDamageScale）：
                    // 20→30 回合上限那轮发现波次战打不完，靠这个旋钮提效率，不去改设计文档里的技能倍率
                    float scale = SkillTargeting.EffectiveSide(skill) == TargetSide.AllEnemies ? config.aoeDamageScale : 1f;
                    if (scale != 1f) { aoeCastCount++; lastAoeScale = scale; }

                    float mul = skill.multiplier * scale * LingJing.Data.SkillRanks.MultiplierMul(skill);
                    // 敌人奥义里有一部分是真伤：不吃防御减伤、不吃护盾。用来打穿「治疗 + 护盾」的续航。
                    float trueRatio = (actor.side == Side.Enemy && skill.type == SkillType.Ultimate)
                        ? Mathf.Clamp01(config.enemyTrueDamagePercent) : 0f;
                    int seg = Mathf.Max(1, skill.segments);

                    // 单段技能：伤害要落到「每一个目标」上（全体技能以前只打了第一个敌人，试玩里被抓出来）
                    if (seg <= 1)
                    {
                        for (int i = 0; i < enemies.Count; i++)
                        {
                            var t = enemies[i];
                            if (t == null || !t.IsAlive) continue;
                            float dealt = calc.Apply(actor, t, mul, trueRatio);
                            if (skill.appliedStatus != StatusType.None && skill.dotStacks > 0)
                                t.ApplyStatus(skill.appliedStatus, skill.dotStacks, config.dotDuration, 0f, config.dotMaxStacks);
                            if (skill.gaugePushbackPercent > 0f)
                                t.gauge = Mathf.Max(0f, t.gauge - config.gaugeThreshold * skill.gaugePushbackPercent);
                            TryApplyControl(actor, skill, t);
                            if (verbose)
                                Log(string.Format("{0} → {1} {2:F0}", actor.displayName, t.displayName, dealt));
                        }
                    }
                    else
                    {
                        // 多段技能：段数在多目标之间轮转（单体多段就是同一目标吃满）
                        for (int s = 0; s < seg; s++)
                        {
                            var t = enemies[skill.spreadSegments ? s % enemies.Count : 0];
                            if (t == null || !t.IsAlive) continue;
                            float dealt = calc.Apply(actor, t, mul, trueRatio);
                            if (skill.appliedStatus != StatusType.None && skill.dotStacks > 0)
                                t.ApplyStatus(skill.appliedStatus, skill.dotStacks, config.dotDuration, 0f, config.dotMaxStacks);
                            if (skill.gaugePushbackPercent > 0f)
                                t.gauge = Mathf.Max(0f, t.gauge - config.gaugeThreshold * skill.gaugePushbackPercent);
                            TryApplyControl(actor, skill, t);
                            if (verbose)
                                Log(string.Format("{0} → {1} 第{2}段 {3:F0}", actor.displayName, t.displayName, s + 1, dealt));
                        }
                    }
                }
            }

        /// <summary>控制类效果：按 controlChance 掷一次，命中就给目标挂眩晕（controlDuration 回合）。</summary>
        void TryApplyControl(BattleUnit actor, SkillDefinition skill, BattleUnit target)
        {
            if (target == null || !target.IsAlive) return;
            if (skill.controlChance <= 0f || skill.controlDuration <= 0) return;
            if (rng.NextDouble() > skill.controlChance) return;
            target.ApplyStatus(StatusType.Stun, 1, skill.controlDuration, 0f, 99);
            Log(string.Format("{0} 被 {1} 眩晕 {2} 回合", target.displayName, skill.displayName, skill.controlDuration));
        }

            // 纯减益辅助（没有伤害倍率）：状态要自己挂，不能指望伤害段顺手挂上
            if (skill.appliedStatus != StatusType.None && skill.dotStacks > 0 && skill.multiplier <= 0f)
            {
                var st = SkillTargeting.EffectiveSide(skill) == TargetSide.AllEnemies ? Alive(Side.Enemy) : targets;
                for (int i = 0; i < st.Count; i++)
                {
                    var t = st[i];
                    if (t == null || !t.IsAlive) continue;
                    t.ApplyStatus(skill.appliedStatus, skill.dotStacks, config.dotDuration, 0f, config.dotMaxStacks);
                    if (verbose) Log(string.Format("{0} 给 {1} 挂 {2} ×{3}", actor.displayName, t.displayName, skill.appliedStatus, skill.dotStacks));
                }
            }

            // 自身挂状态（例如水灵主动给自己【湿润】）
            if (skill.selfAppliesStatus && skill.appliedStatus != StatusType.None)
                actor.ApplyStatus(skill.appliedStatus, 1, 2, 0f, config.dotMaxStacks);
        }

        // ------------------------------------------------------------------ 工具

        public List<BattleUnit> Alive(Side? side)
        {
            var list = new List<BattleUnit>();
            for (int i = 0; i < units.Count; i++)
            {
                if (!units[i].IsAlive) continue;
                if (!units[i].inField) continue;      // 还没入场的批次不参战、不可被选中
                if (side.HasValue && units[i].side != side.Value) continue;
                list.Add(units[i]);
            }
            return list;
        }

        void Log(string msg)
        {
            if (log.Count < 4000) log.Add(msg);
        }
    }
}
