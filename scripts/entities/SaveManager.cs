using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;
using FileAccess = Godot.FileAccess;

public static class SaveManager
{
	public static SaveData CurrentSave { get; set; } = null;
	public static string ActiveSlotId { get; set; } = "";

	public static SavedWeaponStats CaptureWeaponStats(WeaponData w)
	{
		if (w == null) return null;
		return new SavedWeaponStats
		{
			Damage = w.Damage,
			AttackCooldown = w.AttackCooldown,
			MaxAmmo = w.MaxAmmo,
			ReloadTime = w.ReloadTime,
			ManaCost = w.ManaCost,
			KnockbackForce = w.KnockbackForce,
			BulletCount = w.BulletCount,
			Spread = w.Spread,
			BulletSpeed = w.BulletSpeed,
			IsPiercing = w.IsPiercing,
			AllowMultiHit = w.AllowMultiHit
		};
	}

	public static WeaponData RestoreWeaponWithStats(string resourcePath, SavedWeaponStats stats)
	{
		if (string.IsNullOrEmpty(resourcePath)) return null;
		var weaponData = GD.Load<WeaponData>(resourcePath);
		if (weaponData == null) return null;
		if (stats == null) return weaponData;

		weaponData = weaponData.CreateUniqueCopy();
		weaponData.Damage = stats.Damage;
		weaponData.AttackCooldown = stats.AttackCooldown;
		weaponData.MaxAmmo = stats.MaxAmmo;
		weaponData.ReloadTime = stats.ReloadTime;
		weaponData.ManaCost = stats.ManaCost;
		weaponData.KnockbackForce = stats.KnockbackForce;

		if (stats.BulletCount.HasValue) weaponData.BulletCount = stats.BulletCount.Value;
		if (stats.Spread.HasValue) weaponData.Spread = stats.Spread.Value;
		if (stats.BulletSpeed.HasValue) weaponData.BulletSpeed = stats.BulletSpeed.Value;
		if (stats.IsPiercing.HasValue) weaponData.IsPiercing = stats.IsPiercing.Value;
		if (stats.AllowMultiHit.HasValue) weaponData.AllowMultiHit = stats.AllowMultiHit.Value;

		return weaponData;
	}

	public static void CapturePlayerInventory(Player player, SaveData save)
	{
		if (player == null || save == null) return;

		save.WeaponResourcePaths.Clear();
		save.WeaponSlotCustomStats.Clear();
		for (int i = 0; i < player.WeaponSlots.Count; i++)
		{
			var w = player.WeaponSlots[i]?.CurrentWeapon;
			if (w != null)
			{
				save.WeaponResourcePaths.Add(w.GetEffectiveResourcePath());
				save.WeaponSlotCustomStats.Add(CaptureWeaponStats(w));
			}
			else
			{
				save.WeaponResourcePaths.Add("");
				save.WeaponSlotCustomStats.Add(null);
			}
		}
	}

	/// <summary>
	/// Commits the player's and level's current progression as the floor-entry checkpoint save.
	/// </summary>
	public static void CommitFloorCheckpoint(Level level, Player player, SaveData save)
	{
		if (level == null || player == null || save == null) return;

		save.CurrentLevelIndex = level.CurrentLevelIndex;
		save.Coins = player.CoinCount;
		save.CurrentHealth = player.CurrentHealth;
		save.CurrentMana = player.CurrentMana;
		save.BonusMaxHealth = player.BonusMaxHealth;
		save.BonusMaxMana = player.BonusMaxMana;
		save.BonusHealthRegen = player.BonusHealthRegen;
		save.BonusManaRegen = player.BonusManaRegen;
		save.BonusMoveSpeed = player.BonusMoveSpeed;
		save.WeaponDamageMultiplier = player.WeaponDamageMultiplier;
		save.SkillPoints = player.SkillPoints;
		save.Ability1Level = player.Ability1Level;
		save.Ability2Level = player.Ability2Level;
		save.PlayedTimeSeconds += level.ConsumePlayedTime();
		save.ShopPurchaseCounts = new Dictionary<string, int>(player.ShopPurchaseCounts);
		CapturePlayerInventory(player, save);
		SaveGame(save);
	}

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
			string globalPath = ProjectSettings.GlobalizePath(path);
			DirAccess.RemoveAbsolute(globalPath);
		}
	}
}
