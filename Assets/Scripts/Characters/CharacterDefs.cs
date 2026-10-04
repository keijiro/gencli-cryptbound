using UnityEngine;

namespace Cryptbound {

// Part layouts and rest poses for every character.
public static class CharacterDefs
{
    static Pose MakeRest(float bodyY, Vector3 r, Vector3 rDir, Vector3 l, float lYaw) => new Pose
    {
        BodyPos = new Vector3(0, bodyY, 0),
        BodyRot = Quaternion.identity,
        RPos = r,
        RRot = Pose.Aim(rDir),
        LPos = l,
        LRot = Quaternion.Euler(0, lYaw, 0)
    };

    public static readonly RigDef Player = new()
    {
        Body = new PartDef { Model = "player_torso", Scale = 1.5f },
        Right = new PartDef { Model = "player_hand_r", Scale = 1.2f, Pivot = new Vector3(0, -0.2f, 0) },
        Left = new PartDef { Model = "player_hand_l", Scale = 0.95f, Pivot = new Vector3(0.15f, 0.02f, 0) },
        Rest = MakeRest(1.15f, new Vector3(0.5f, 0.95f, 0.22f), new Vector3(0.12f, 1, 0.4f), new Vector3(-0.48f, 1.05f, 0.32f), -18),
        BobAmplitude = 0.045f,
        BobFrequency = 1.1f
    };

    public static readonly RigDef Skeleton = new()
    {
        Body = new PartDef { Model = "skeleton_torso", Scale = 1.45f },
        Right = new PartDef { Model = "skeleton_hand_r", Scale = 1.1f, Pivot = new Vector3(0, -0.2f, 0) },
        Left = new PartDef { Model = "skeleton_hand_l", Scale = 0.95f, Pivot = new Vector3(0.12f, 0, 0) },
        Rest = MakeRest(1.1f, new Vector3(0.46f, 0.92f, 0.22f), new Vector3(0.2f, 1, 0.3f), new Vector3(-0.46f, 1.0f, 0.3f), -15),
        BobAmplitude = 0.05f,
        BobFrequency = 1.3f
    };

    public static readonly RigDef SkeletonLord = new()
    {
        Body = new PartDef { Model = "skeletonlord_torso", Scale = 1.62f },
        Right = new PartDef { Model = "skeletonlord_hand_r", Scale = 1.4f, Pivot = new Vector3(0, -0.25f, 0) },
        Left = new PartDef { Model = "skeletonlord_hand_l", Scale = 1.05f, Pivot = new Vector3(0.15f, 0.02f, 0) },
        Rest = MakeRest(1.22f, new Vector3(0.52f, 1.0f, 0.25f), new Vector3(0.2f, 1, 0.3f), new Vector3(-0.52f, 1.1f, 0.35f), -15),
        BobAmplitude = 0.05f,
        BobFrequency = 1.1f
    };

    // Golem arms hang from their pivot at the wrist end; the fist is the part's -Y end.
    public static readonly RigDef Golem = new()
    {
        Body = new PartDef { Model = "golem_torso", Scale = 3.3f },
        Right = new PartDef { Model = "golem_hand_r", Scale = 2.0f, Pivot = new Vector3(0, 0.42f, 0) },
        Left = new PartDef { Model = "golem_hand_r", Scale = 2.0f, Pivot = new Vector3(0, 0.42f, 0), Mirror = true },
        Rest = new Pose
        {
            BodyPos = new Vector3(0, 2.75f, 0),
            BodyRot = Quaternion.identity,
            RPos = new Vector3(1.75f, 3.3f, 0.35f),
            RRot = Fist(new Vector3(0.15f, -1, 0.25f)),
            LPos = new Vector3(-1.75f, 3.3f, 0.35f),
            LRot = Fist(new Vector3(-0.15f, -1, 0.25f))
        },
        BobAmplitude = 0.09f,
        BobFrequency = 0.55f
    };

    public static Quaternion Fist(Vector3 fistDir) => Pose.Aim(-fistDir);

    public static readonly RigDef Ghost = new()
    {
        Body = new PartDef { Model = "ghost", Scale = 2.5f },
        Rest = new Pose { BodyPos = new Vector3(0, 1.85f, 0), BodyRot = Quaternion.identity },
        BobAmplitude = 0.14f,
        BobFrequency = 0.55f
    };

    public static readonly RigDef GhostLord = new()
    {
        Body = new PartDef { Model = "ghostlord", Scale = 2.9f },
        Rest = new Pose { BodyPos = new Vector3(0, 2.1f, 0), BodyRot = Quaternion.identity },
        BobAmplitude = 0.16f,
        BobFrequency = 0.5f
    };
}

} // namespace Cryptbound
