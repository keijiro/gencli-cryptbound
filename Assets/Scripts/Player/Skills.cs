using System.Collections.Generic;
using UnityEngine;

namespace Cryptbound {

public enum SkillId
{
    SwiftFeet, TwinStrike, Crescent, IronWall, Endurance, Vitality, KeenEdge, HawkEye, Riposte,
    SecondWind, SoulDrinker, SpectralWave, CounterNova, SpikedGuard, Scholar, QuickHands, Undying, GuardianSlayer
}

public sealed class SkillDef
{
    public SkillId Id;
    public string Name;
    public string Icon;
    public string Description;
    public int MaxRank = 1;
    public SkillId? Requires;

    public static readonly SkillDef[] All =
    {
        new() { Id = SkillId.SwiftFeet, Name = "Swift Feet", Icon = "swift_feet", MaxRank = 3, Description = "Movement speed +12%." },
        new() { Id = SkillId.TwinStrike, Name = "Twin Strike", Icon = "combo", Description = "Press X again mid-swing to chain a second slash." },
        new() { Id = SkillId.Crescent, Name = "Crescent Finisher", Icon = "finisher", Requires = SkillId.TwinStrike, Description = "Adds a crushing third strike to your combo that deals double damage." },
        new() { Id = SkillId.IronWall, Name = "Iron Wall", Icon = "iron_wall", MaxRank = 2, Description = "Guarding and blocking cost 25% less stamina." },
        new() { Id = SkillId.Endurance, Name = "Endurance", Icon = "endurance", MaxRank = 3, Description = "Max stamina +30." },
        new() { Id = SkillId.Vitality, Name = "Vitality", Icon = "vitality", MaxRank = 3, Description = "Max HP +60 and fully restore HP." },
        new() { Id = SkillId.KeenEdge, Name = "Keen Edge", Icon = "keen_edge", MaxRank = 4, Description = "Attack damage +20%." },
        new() { Id = SkillId.HawkEye, Name = "Hawk Eye", Icon = "parry_master", MaxRank = 2, Description = "Parry timing window +40%." },
        new() { Id = SkillId.Riposte, Name = "Riposte", Icon = "riposte", MaxRank = 3, Description = "Counter attack damage +50%." },
        new() { Id = SkillId.SecondWind, Name = "Second Wind", Icon = "second_wind", MaxRank = 2, Description = "Stamina recovers 35% faster." },
        new() { Id = SkillId.SoulDrinker, Name = "Soul Drinker", Icon = "lifesteal", MaxRank = 2, Description = "Heal for 4% of the damage you deal." },
        new() { Id = SkillId.SpectralWave, Name = "Spectral Wave", Icon = "blade_wave", Description = "The last strike of your combo launches a piercing wave of light." },
        new() { Id = SkillId.CounterNova, Name = "Counter Nova", Icon = "shockwave", Description = "Counter attacks unleash a shockwave that strikes every nearby foe." },
        new() { Id = SkillId.SpikedGuard, Name = "Spiked Guard", Icon = "thorns", MaxRank = 2, Description = "Blocking an attack deals 60% of its damage back to the attacker." },
        new() { Id = SkillId.Scholar, Name = "Scholar", Icon = "fortune", MaxRank = 2, Description = "EXP gained +25%." },
        new() { Id = SkillId.QuickHands, Name = "Quick Hands", Icon = "quick_hands", MaxRank = 2, Description = "Attack speed +15%." },
        new() { Id = SkillId.Undying, Name = "Undying", Icon = "last_stand", Description = "Once per floor, survive a fatal blow with 1 HP." },
        new() { Id = SkillId.GuardianSlayer, Name = "Guardian Slayer", Icon = "guardian_slayer", MaxRank = 2, Description = "Damage against Guardians +30%." },
    };

    public static SkillDef Get(SkillId id) => All[(int)id];
}

// Level, experience, learned skills and the stats derived from them.
public sealed class PlayerStats
{
    public int Level { get; private set; } = 1;
    public int Exp { get; private set; }
    public int ExpToNext => Balance.ExpToNext(Level);

    readonly int[] _ranks = new int[SkillDef.All.Length];

    public int Rank(SkillId id) => _ranks[(int)id];

    public void Learn(SkillId id) => _ranks[(int)id]++;

    // Returns the number of level-ups gained.
    public int AddExp(float amount)
    {
        Exp += Mathf.RoundToInt(amount * ExpMul);
        var ups = 0;
        while (Exp >= ExpToNext)
        {
            Exp -= ExpToNext;
            Level++;
            ups++;
        }
        return ups;
    }

    // Derived stats

    public float MaxHp => Balance.PlayerBaseHp * Mathf.Pow(1.08f, Level - 1) + 60 * Rank(SkillId.Vitality);
    public float MaxStamina => Balance.PlayerBaseStamina + 5 * (Level - 1) + 30 * Rank(SkillId.Endurance);
    public float Attack => Balance.PlayerBaseAttack * Mathf.Pow(1.07f, Level - 1) * (1 + 0.2f * Rank(SkillId.KeenEdge));
    public float MoveSpeed => Balance.PlayerBaseSpeed * (1 + 0.12f * Rank(SkillId.SwiftFeet));
    public float GuardCostMul => 1 - 0.25f * Rank(SkillId.IronWall);
    public float StaminaRegen => Balance.StaminaRegen * (1 + 0.35f * Rank(SkillId.SecondWind));
    public float ParryWindow => Balance.ParryWindow * (1 + 0.4f * Rank(SkillId.HawkEye));
    public float CounterMul => Balance.CounterMultiplier * (1 + 0.5f * Rank(SkillId.Riposte));
    public float AttackSpeed => 1 + 0.15f * Rank(SkillId.QuickHands);
    public float ExpMul => 1 + 0.25f * Rank(SkillId.Scholar);
    public float LifeSteal => 0.04f * Rank(SkillId.SoulDrinker);
    public float Thorns => 0.6f * Rank(SkillId.SpikedGuard);
    public float GuardianMul => 1 + 0.3f * Rank(SkillId.GuardianSlayer);
    public int ComboLength => 1 + Rank(SkillId.TwinStrike) + Rank(SkillId.Crescent);

    // Skill offers

    public SkillDef[] Offer(int count)
    {
        var pool = new List<SkillDef>();
        foreach (var s in SkillDef.All)
        {
            if (Rank(s.Id) >= s.MaxRank) continue;
            if (s.Requires.HasValue && Rank(s.Requires.Value) == 0) continue;
            pool.Add(s);
        }
        var result = new List<SkillDef>();
        // The first combo upgrade is guaranteed early so the core loop opens up.
        if (Level <= 3 && Rank(SkillId.TwinStrike) == 0) { result.Add(SkillDef.Get(SkillId.TwinStrike)); pool.Remove(result[0]); }
        while (result.Count < count && pool.Count > 0)
        {
            var i = Random.Range(0, pool.Count);
            result.Add(pool[i]);
            pool.RemoveAt(i);
        }
        return result.ToArray();
    }
}

} // namespace Cryptbound
