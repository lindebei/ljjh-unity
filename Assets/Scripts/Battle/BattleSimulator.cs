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

        public int spirit;
        public int potions;
        public float time;
        public int round = 1;
        public int playerActionsTotal;
        int playerActionsThisRound;

        float meanSpd;
        float meanSpr;
        readonly System.Random rng;

        public BattleSimulator(BattleConfig config, int seed = 20261031)
        {
            this.config = config;
            this.calc = new DamageCalculator(config);
            this.rng = new System.Random(seed);
        }

        // ------------------------------------------------------------------ 建队

        public BattleUnit AddPlayer(CreatureDefinition def, int level, int[] freePoints)
        {
            var s = BattleFormulas.PlayerStats(def.baseStats, level, def.potentials, freePoints);
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
            units.Add(u);
            return u;
        }

        public BattleUnit AddEnemy(CreatureDefinition def, int level, int bandIndex)
        {
            var s = BattleFormulas.EnemyStats(def.baseStats, level);
            int qi = (int)def.quality;
            float hpMul = config.qualityHpMul[qi] * config.levelBandHpMul[bandIndex];
            float atkMul = config.qualityAtkMul[qi] * config.levelBandAtkMul[bandIndex];

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
            units.Add(u);
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

            spirit = BattleFormulas.SpiritStart(meanSpr);
            potions = config.potionsPerBattle;
            time = 0f;
            round = 1;
            playerActionsThisRound = 0;
            playerActionsTotal = 0;

            // 起手行动值：按各自速度拉开一点，避免所有人同一节拍撞车
            for (int i = 0; i < units.Count; i++)
                units[i].gauge = 0f;

            Log(string.Format("开场：我方速度均值 {0:F0}，精神均值 {1:F0}，初始灵能 {2}/{3}，药品 {4} 个",
                meanSpd, meanSpr, spirit, config.spiritMax, potions));
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

        public bool IsBattleOver
        {
            get { return Alive(Side.Player).Count == 0 || Alive(Side.Enemy).Count == 0; }
        }

        /// <summary>
        /// 推进一拍。返回 true 表示这拍有人出手（交互模式下则是「轮到玩家了，等你点按钮」）。
        /// 交互模式时把出手权交出去，UI 调 ResolvePlayerTurn 才真正结算。
        /// </summary>
        public bool StepOnce()
        {
            if (IsBattleOver) return false;

            time += config.tickSeconds;

            var live = Alive(null);
            for (int i = 0; i < live.Count; i++)
                live[i].gauge += BattleFormulas.GaugeGainPerTick(live[i].EffectiveSpd, meanSpd, config.gaugeGainPerTick);

            BattleUnit actor = null;
            float best = -1f;
            for (int i = 0; i < live.Count; i++)
            {
                if (live[i].gauge >= config.gaugeThreshold && live[i].gauge > best)
                {
                    best = live[i].gauge;
                    actor = live[i];
                }
            }
            if (actor == null) return false;

            actor.gauge = 0f;
            actor.actionsTaken++;

            if (actor.side == Side.Player && interactive)
            {
                awaitingPlayer = actor;
                return true;
            }

            TakeAction(actor);
            return true;
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
                timedOut = timedOut
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
            round++;

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
                    spirit = Mathf.Min(config.spiritMax, spirit + regen);
                    Log(string.Format("回合{0} 灵能自然回复 +{1} → {2}", round, regen, spirit));
                }
            }
        }

        // ------------------------------------------------------------------ 玩家侧

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

            bool ultReady = ult != null && actor.Cooldown(ult.id) <= 0 && spirit >= ult.spiritCost;

            // 水灵：治疗优先，两三个人半血才交奥义
            if (actor.id == "beast_water")
            {
                if (ultReady && lowAllies >= 2) return ult;
                if (support != null && spirit >= support.spiritCost && lowAllies >= 1) return support;
                if (active != null && spirit >= active.spiritCost) return active;
                return basic;
            }

            // 土灵：血线不健康时先磐石（护盾 + 嘲讽）
            if (actor.id == "beast_earth")
            {
                if (ultReady) return ult;
                if (support != null && spirit >= support.spiritCost && actor.HpPercent < 0.75f) return support;
                if (active != null && spirit >= active.spiritCost) return active;
                return basic;
            }

            // 火灵 / 风灵：奥义 ＞ 主动 ＞ 普攻
            if (ultReady) return ult;
            if (active != null && spirit >= active.spiritCost) return active;
            return basic;
        }

        List<BattleUnit> PickPlayerTargets(BattleUnit actor, SkillDefinition skill)
        {
            var list = new List<BattleUnit>();
            if (skill == null) return list;

            switch (skill.targetSide)
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

            BattleUnit t = null;
            float low = float.MaxValue;
            for (int i = 0; i < players.Count; i++)
                if (players[i].hp < low) { low = players[i].hp; t = players[i]; }
            if (t != null) list.Add(t);
            return list;
        }

        // ------------------------------------------------------------------ 结算一个技能

        public void Cast(BattleUnit actor, SkillDefinition skill, List<BattleUnit> targets)
        {
            if (skill == null) return;

            // 灵能
            if (skill.spiritCost < 0) spirit = Mathf.Min(config.spiritMax, spirit + 1);
            else spirit = Mathf.Max(0, spirit - skill.spiritCost);

            if (skill.cooldown > 0) actor.SetCooldown(skill.id, skill.cooldown);
            if (skill.taunt) actor.tauntRounds = Mathf.Max(1, skill.controlDuration);

            // 治疗（两段式）
            if (skill.isHeal)
            {
                var healTargets = skill.healAllAllies ? Alive(Side.Player) : targets;
                float ratio = calc.HealFirstRatio(actor);
                for (int i = 0; i < healTargets.Count; i++)
                {
                    var t = healTargets[i];
                    if (!t.IsAlive) continue;
                    float first = t.MissingHp * ratio;
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
                var buffTargets = skill.targetSide == TargetSide.AllAllies ? Alive(Side.Player) : targets;
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
                var enemies = skill.targetSide == TargetSide.AllEnemies ? Alive(Side.Enemy) : targets;
                if (enemies.Count > 0)
                {
                    int seg = Mathf.Max(1, skill.segments);
                    for (int s = 0; s < seg; s++)
                    {
                        var t = enemies[skill.spreadSegments ? s % enemies.Count : 0];
                        if (t == null || !t.IsAlive) continue;
                        float dealt = calc.Apply(actor, t, skill.multiplier);
                        if (skill.appliedStatus != StatusType.None && skill.dotStacks > 0)
                            t.ApplyStatus(skill.appliedStatus, skill.dotStacks, config.dotDuration, 0f, config.dotMaxStacks);
                        if (skill.gaugePushbackPercent > 0f)
                            t.gauge = Mathf.Max(0f, t.gauge - config.gaugeThreshold * skill.gaugePushbackPercent);
                        if (verbose)
                            Log(string.Format("{0} → {1} 第{2}段 {3:F0}", actor.displayName, t.displayName, s + 1, dealt));
                    }
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
