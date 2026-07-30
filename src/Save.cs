using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Dungeon
{
    public sealed class EnemyDto
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Kind { get; set; }
        public int Hp { get; set; }
        public int MaxHp { get; set; }
        public int Dmg { get; set; }
        public int MoveEvery { get; set; }
        public int MoveCounter { get; set; }
        public string Name { get; set; }
    }

    public sealed class ItemDto
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Kind { get; set; }
        public int Value { get; set; }
    }

    /// <summary>Complete snapshot of a run so the player resumes exactly where they left off.</summary>
    public sealed class SaveData
    {
        public int Depth { get; set; }
        public int Hp { get; set; }
        public int MaxHp { get; set; }
        public int AtkMin { get; set; }
        public int AtkMax { get; set; }
        public int Gold { get; set; }
        public int Shield { get; set; }
        public int TorchTurns { get; set; }
        public int AtkTurns { get; set; }
        public int AtkBonus { get; set; }
        public int RegenTurns { get; set; }
        public int Freeze { get; set; }
        public int Haste { get; set; }

        // hero identity + rolled attributes
        public string Name { get; set; }
        public int Str { get; set; }
        public int Dex { get; set; }
        public int Vit { get; set; }
        public int Luk { get; set; }
        public int DodgePct { get; set; }
        public int CritPct { get; set; }
        public string ClassName { get; set; }

        public int Px { get; set; }
        public int Py { get; set; }
        public int Dir { get; set; }

        public int W { get; set; }
        public int H { get; set; }
        public byte[] Tiles { get; set; }
        public int StartX { get; set; }
        public int StartY { get; set; }
        public int ExitX { get; set; }
        public int ExitY { get; set; }
        public bool[] Seen { get; set; }

        public List<EnemyDto> Enemies { get; set; } = new();
        public List<ItemDto> Items { get; set; } = new();
    }

    /// <summary>Reads/writes the save file. All operations fail soft (never throw to the game).</summary>
    public static class SaveSystem
    {
        static string Dir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TorchlightDepths");
        static string FilePath => Path.Combine(Dir, "save.json");

        public static bool Exists()
        {
            try { return File.Exists(FilePath); } catch { return false; }
        }

        public static void Save(SaveData data)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                var json = JsonSerializer.Serialize(data);
                File.WriteAllText(FilePath, json);
            }
            catch { /* saving is best-effort */ }
        }

        public static SaveData Load()
        {
            try
            {
                if (!Exists()) return null;
                return JsonSerializer.Deserialize<SaveData>(File.ReadAllText(FilePath));
            }
            catch { return null; }
        }

        public static void Delete()
        {
            try { if (Exists()) File.Delete(FilePath); } catch { }
        }
    }
}
