using UnityEngine;
using UnityEngine.InputSystem;

namespace Cryptbound {

// Arrow keys to move, X to attack, Z to guard (gamepad: stick, West/South, East/shoulders).
public static class Controls
{
    static Keyboard K => Keyboard.current;
    static Gamepad Pad => Gamepad.current;

#if UNITY_EDITOR
    static bool B => Bot.Enabled;
#else
    const bool B = false;
#endif

    public static Vector2 Move
    {
        get
        {
#if UNITY_EDITOR
            if (B) return Bot.Move;
#endif
            var v = Vector2.zero;
            if (K != null)
            {
                if (K.leftArrowKey.isPressed) v.x -= 1;
                if (K.rightArrowKey.isPressed) v.x += 1;
                if (K.upArrowKey.isPressed) v.y += 1;
                if (K.downArrowKey.isPressed) v.y -= 1;
            }
            if (Pad != null)
            {
                var s = Pad.leftStick.ReadValue() + Pad.dpad.ReadValue();
                if (s.sqrMagnitude > 0.04f) v += s;
            }
            return Vector2.ClampMagnitude(v, 1);
        }
    }

    public static bool AttackPressed
      => BotFlag(0) ||
         (K != null && K.xKey.wasPressedThisFrame) ||
         (Pad != null && (Pad.buttonWest.wasPressedThisFrame || Pad.buttonSouth.wasPressedThisFrame));

    public static bool GuardHeld
      => BotFlag(1) ||
         (K != null && K.zKey.isPressed) ||
         (Pad != null && (Pad.buttonEast.isPressed || Pad.rightShoulder.isPressed || Pad.leftShoulder.isPressed));

    public static bool GuardPressed
      => BotFlag(2) ||
         (K != null && K.zKey.wasPressedThisFrame) ||
         (Pad != null && (Pad.buttonEast.wasPressedThisFrame || Pad.rightShoulder.wasPressedThisFrame || Pad.leftShoulder.wasPressedThisFrame));

    public static bool ConfirmPressed
      => BotFlag(3) ||
         (K != null && (K.xKey.wasPressedThisFrame || K.enterKey.wasPressedThisFrame || K.spaceKey.wasPressedThisFrame)) ||
         (Pad != null && (Pad.buttonSouth.wasPressedThisFrame || Pad.buttonWest.wasPressedThisFrame || Pad.startButton.wasPressedThisFrame));

    public static bool PausePressed
      => (K != null && (K.escapeKey.wasPressedThisFrame || K.pKey.wasPressedThisFrame)) ||
         (Pad != null && Pad.startButton.wasPressedThisFrame);

    public static int HorizontalPressed
    {
        get
        {
            var d = 0;
            if (K != null)
            {
                if (K.leftArrowKey.wasPressedThisFrame) d -= 1;
                if (K.rightArrowKey.wasPressedThisFrame) d += 1;
            }
            if (Pad != null)
            {
                if (Pad.dpad.left.wasPressedThisFrame) d -= 1;
                if (Pad.dpad.right.wasPressedThisFrame) d += 1;
            }
            return d;
        }
    }

    static bool BotFlag(int index)
    {
#if UNITY_EDITOR
        if (!B) return false;
        return index switch { 0 => Bot.Attack, 1 => Bot.GuardHeld, 2 => Bot.GuardPressed, _ => Bot.Confirm };
#else
        return false;
#endif
    }
}

} // namespace Cryptbound
