# Cryptbound

A one-way dungeon-crawl roguelite built in Unity 6 (URP). Descend floor by floor through a
straight crypt corridor, fight waves of undead, and break each floor's stone Guardian.

## Controls

| Input | Action |
|---|---|
| Arrow keys | Move (forward / back / strafe) |
| X | Attack (auto-aimed; chain combos once learned) |
| Z (hold) | Guard — drains stamina, blocked hits cost stamina |
| Z (tap at the moment of impact) | Parry → cinematic slow-motion → press X to Counter |

Gamepad: left stick / d-pad to move, West or South to attack, East or a shoulder button to guard.

## Game flow

- Each floor is a corridor with enemy waves. Reaching a wave raises a spectral barrier until
  every foe in it falls.
- At the end waits the Guardian (a stone golem). The gate slams shut, it assembles from the
  rubble, and fights with slams, sweeps, rolling shockwaves (sidestep them) and hurled rocks.
- Defeat it to clear the floor, then step into the portal to descend.
- Experience levels you up: base stats rise and you pick one of three skills (18 in total).
- Floors B1–B5 are the designed ~20 minute arc. From B6 on, "The Endless Deep" scales
  exponentially until the crypt claims you.

## Project layout

- `Assets/Scripts` — game code (`Core`, `Player`, `Characters`, `World`, `Effects`)
- `Assets/UI` — UI Toolkit HUD (`Hud.uxml`, `Hud.uss`, `Hud.cs`) and fonts (OFL)
- `Assets/Models` — Tripo P2 meshes generated with Unity AI (textures downscaled)
- `Assets/Audio`, `Assets/Textures` — generated sound, music, textures, VFX and icons
- `Assets/Resources/GameAssets.asset` — name-based asset catalog used at runtime
- `Assets/Editor/CryptboundSetup.cs` — `Cryptbound > Run Full Setup` rebuilds import
  settings, materials, the catalog, render settings and the scene
- `GeneratedAssets/` — generation scripts, concept images and original high-res GLBs (not
  imported by Unity)

## Testing

An editor-only autopilot (`Assets/Scripts/Player/Bot.cs`) plays like an imperfect human
(parries a share of attacks, blocks some, eats the rest) for soak tests. In Play mode, evaluate
`Cryptbound.Bot.Enabled = true; Cryptbound.Game.TimeBase = 3; UnityEngine.Time.timeScale = 3;`
to let it play at 3x speed; progress is logged with a `[RUN]` prefix. With a 60% parry rate it
cleared B5 after about 7 minutes of play time and fell on B8 at about 11 minutes; a human player
moves more slowly than the bot, so expect roughly twice those times.

Characters follow the concept art's rule — upper body only, hands detached — so every motion
is done with part positions and rotations (`PartRig`), without bones.
