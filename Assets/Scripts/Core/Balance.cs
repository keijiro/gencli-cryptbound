using UnityEngine;

namespace Cryptbound {

// Tuning constants and difficulty curves.
public static class Balance
{
    // Space

    public const float CorridorHalfWidth = 3.5f;
    public const float CorridorHeight = 5.0f;
    public const float MoveHalfWidth = 2.5f;
    public const float ArenaHalfWidth = 8.0f;
    public const float ArenaLength = 26.0f;
    public const float ArenaHeight = 7.5f;
    public const float ArenaMoveHalfWidth = 7.0f;

    // Player

    public const float PlayerBaseHp = 320;
    public const float PlayerBaseStamina = 180;
    public const float PlayerBaseAttack = 30;
    public const float PlayerBaseSpeed = 4.2f;
    public const float GuardDrain = 9;
    public const float BlockCost = 0.8f;
    public const float StaminaRegen = 42;
    public const float StaminaRegenDelay = 0.6f;
    public const float ParryWindow = 0.2f;
    public const float CounterMultiplier = 5;
    public const float CounterStamina = 0.5f;
    public const float CounterWindow = 1.4f;

    // Floors

    public static float FloorLength(int floor)
      => floor switch { 1 => 120, 2 => 140, 3 => 150, 4 => 160, _ => 170 };

    public static int EncounterCount(int floor)
      => floor switch { 1 => 5, 2 => 6, 3 => 6, _ => 7 };

    // Difficulty: gentle for the first five floors, then steeply exponential.

    public static float HpMul(int floor)
      => floor <= 5 ? 1 + 0.38f * (floor - 1) : HpMul(5) * Mathf.Pow(1.5f, floor - 5);

    public static float DmgMul(int floor)
      => floor <= 5 ? 1 + 0.17f * (floor - 1) : DmgMul(5) * Mathf.Pow(1.33f, floor - 5);

    public static float SpeedMul(int floor)
      => 1 + 0.05f * Mathf.Min(floor - 1, 8);

    public static float ExpMul(int floor)
      => 1 + 0.5f * (floor - 1);

    public static int ExpToNext(int level)
    {
        var l = level - 1;
        return 100 + 80 * l + 20 * l * l;
    }
}

// Per-floor look.
public readonly struct FloorTheme
{
    public readonly string Name;
    public readonly Color Torch;
    public readonly Color Fog;
    public readonly Color Ambient;
    public readonly float Boost;

    public FloorTheme(string name, Color torch, Color fog, Color ambient, float boost = 1)
      => (Name, Torch, Fog, Ambient, Boost) = (name, torch, fog, ambient, boost);

    public static FloorTheme Get(int floor) => floor switch
    {
        1 => new FloorTheme("The Forgotten Halls", new Color(1.0f, 0.55f, 0.25f), new Color(0.045f, 0.03f, 0.025f), new Color(0.16f, 0.12f, 0.11f)),
        2 => new FloorTheme("The Ossuary", new Color(1.0f, 0.68f, 0.32f), new Color(0.05f, 0.04f, 0.025f), new Color(0.17f, 0.14f, 0.1f)),
        3 => new FloorTheme("Halls of Lament", new Color(0.35f, 0.62f, 1.0f), new Color(0.02f, 0.03f, 0.055f), new Color(0.11f, 0.14f, 0.2f), 1.7f),
        4 => new FloorTheme("The Sunken Crypt", new Color(0.45f, 1.0f, 0.55f), new Color(0.02f, 0.045f, 0.035f), new Color(0.1f, 0.16f, 0.13f), 1.35f),
        5 => new FloorTheme("The Abyssal Vault", new Color(1.0f, 0.28f, 0.2f), new Color(0.055f, 0.015f, 0.015f), new Color(0.18f, 0.09f, 0.09f), 1.3f),
        _ => new FloorTheme("The Endless Deep", new Color(0.72f, 0.38f, 1.0f), new Color(0.035f, 0.015f, 0.05f), new Color(0.14f, 0.1f, 0.19f), 1.5f),
    };
}

public enum Ease { Linear, In, Out, InOut, OutBack, OutCubic }

public static class Easing
{
    public static float Apply(Ease e, float t)
    {
        t = Mathf.Clamp01(t);
        switch (e)
        {
            case Ease.In: return t * t;
            case Ease.Out: return 1 - (1 - t) * (1 - t);
            case Ease.InOut: return t < 0.5f ? 2 * t * t : 1 - Mathf.Pow(-2 * t + 2, 2) / 2;
            case Ease.OutCubic: return 1 - Mathf.Pow(1 - t, 3);
            case Ease.OutBack:
                const float c1 = 1.70158f, c3 = c1 + 1;
                return 1 + c3 * Mathf.Pow(t - 1, 3) + c1 * Mathf.Pow(t - 1, 2);
            default: return t;
        }
    }

    // Frame-rate independent exponential approach.
    public static float Damp(float speed, float dt) => 1 - Mathf.Exp(-speed * dt);
}

} // namespace Cryptbound
