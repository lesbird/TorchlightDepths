# Torchlight Depths

**A first-person, grid-based dungeon crawler in the spirit of *Dungeon Master* and *Eye of the Beholder* — carved entirely out of code.**

Descend into a procedurally generated dungeon lit by nothing but the torch in your hand. Every floor is larger, darker, and deadlier than the last. The light falls off with distance, so the black ahead of you always holds a little dread — and something is usually waiting in it.

How deep can you get?

## ⚔️ Step, turn, and fight

Movement is classic grid-based crawling: step forward, turn, strafe. Combat is as direct as it gets — **bump into a monster to attack it.** Rats and bats haunt the upper floors; slimes and skeletons wait below. Every enemy you don't put down takes a step toward you each turn, so hesitation costs blood.

## 🧙 Build your hero

Roll a new adventurer with a **point-buy** budget spread across four attributes:

- **Strength** — how hard you hit
- **Dexterity** — your chance to dodge a blow entirely
- **Vitality** — raw health
- **Luck** — critical hits *and* better loot

Your **class is auto-assigned** from your strongest attribute — Warrior, Rogue, Guardian, or Trickster — each with its own passive edge. Reroll for a random spread, or min-max your way to a specialist. It's all previewed live as you spend.

## 💎 Loot the dark

Grab a wide range of pickups, most of which take effect the instant you step on them:

- **Potions, bread &amp; hearts** to heal and grow your max HP
- **Gold &amp; gems** for score — and to buy your way back from death
- **Blades &amp; whetstones** to sharpen your attack
- **Shields** that soak the next few hits, **torch oil** to burn brighter and farther, **healing herbs** that regen over time
- **Hourglass** to freeze every enemy, **boots of haste** to move twice per monster turn
- **Fire scrolls** that erupt on pickup and **maps** that reveal the whole floor

Fall in battle? **Spend your gold to revive** right where you dropped — or roll a fresh hero and start the descent over.

## 🔥 Every pixel and note made from scratch

There are **no asset files.** Every texture, sprite, and font glyph is generated in code. Every sound effect is synthesized at startup by a tiny software synth — footsteps, sword swings, coins, the death arpeggio, a low cave drone. And **every level composes its own music**: seeded by depth, it picks a tempo, a minor or Phrygian scale, a chord progression, and a wandering melody, rendered on a background thread so the descent never hitches.

Under the hood it's **C# and hand-written OpenGL 4.1** — a single flickering torch point-light drives all shading over bump-mapped stonework, with camera-facing sprites and smoothly tweened grid movement.

## Features

- Procedurally generated dungeons that shift palette as you go deeper
- Point-buy character creation with four classes
- A dozen+ interactive items and timed power-ups
- Torch-lit line-of-sight and a minimap that fills in as you explore
- Gold-fueled revival system and permadeath saves
- Fully procedural graphics, sound effects, **and** per-level music
- Auto-saves every floor — pick **Continue** to resume your exact run

## Controls

| Key | Action |
|---|---|
| `W` / `Up` | Step forward (bump to attack) |
| `S` / `Down` | Step back |
| `A` / `Left` | Turn left |
| `D` / `Right` | Turn right |
| `Q` / `E` | Strafe left / right |
| `Space` / `F` | Attack the tile ahead |
| `M` | Toggle music |
| `F11` | Toggle fullscreen / windowed |
| `Esc` | Pause to menu (auto-saves) |

---

*Made with C#, .NET, and OpenGL. No external assets — the whole world is generated when you press New Game.*
