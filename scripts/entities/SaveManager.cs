using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;
using FileAccess = Godot.FileAccess;

public class SaveData
{
	public string SlotId { get; set; } = "1";
	public string CharacterName { get; set; } = "Knight";
	public int CurrentLevelIndex { get; set; } = 1;
	public ulong LevelSeed { get; set; } = 0;
	public float PlayedTimeSeconds { get; set; } = 0.0f;
	public int Coins { get; set; } = 0;
	public float CurrentHealth { get; set; } = 100f;
	public float CurrentMana { get; set; } = 100f;
	public float BonusMaxHealth { get; set; } = 0.0f;
	public float BonusMaxMana { get; set; } = 0.0f;
	public float WeaponDamageMultiplier { get; set; } = 1.0f;
	public int SkillPoints { get; set; } = 0;
	public int Ability1Level { get; set; } = 0;
	public int Ability2Level { get; set; } = 0;
	public List<string> WeaponResourcePaths { get; set; } = new List<string>();
	public string SaveDateString { get; set; } = "";
}

public static class SaveManager
{
	public static SaveData CurrentSave { get; set; } = null;
	public static string ActiveSlotId { get; set; } = "";

	public static SaveData LoadSave(string slotId)
	{
		string path = $"user://save_{slotId}.json";
		if (!FileAccess.FileExists(path))
		{
			return null;
		}

		using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
		if (file == null) return null;

		string json = file.GetAsText();
		try
		{
			var data = JsonSerializer.Deserialize<SaveData>(json);
			if (data != null && data.WeaponDamageMultiplier <= 0.01f)
			{
				data.WeaponDamageMultiplier = 1.0f;
			}
			return data;
		}
		catch (Exception e)
		{
			GD.PrintErr($"Failed to deserialize save data for slot {slotId}: {e.Message}");
			return null;
		}
	}

	public static void SaveGame(SaveData data)
	{
		if (data == null) return;
		data.SaveDateString = DateTime.Now.ToString("dd.MM.yyyy HH:mm");
		string path = $"user://save_{data.SlotId}.json";
		using var file = FileAccess.Open(path, FileAccess.ModeFlags.Write);
		if (file == null)
		{
			GD.PrintErr($"Failed to open save file for writing: {path} (error: {FileAccess.GetOpenError()})");
			return;
		}
		string json = JsonSerializer.Serialize(data);
		file.StoreString(json);
	}

	public static void DeleteSave(string slotId)
	{
		string path = $"user://save_{slotId}.json";
		if (FileAccess.FileExists(path))
		{
			DirAccess.RemoveAbsolute(path);
		}
	}
}
