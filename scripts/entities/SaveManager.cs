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
			AllowMultiHit = w.AllowMultiHit,
			IsContinuousLaser = w.IsContinuousLaser,
			SplashRadius = w.SplashRadius,
			AppliedUpgrades = w.AppliedUpgrades != null ? new List<string>(w.AppliedUpgrades) : new List<string>()
		};
	}

	private static void ApplyStatsToWeaponData(WeaponData target, SavedWeaponStats stats, bool copyAppliedUpgrades)
	{
		if (target == null || stats == null) return;

		target.Damage = stats.Damage;
		target.AttackCooldown = stats.AttackCooldown;
		target.MaxAmmo = stats.MaxAmmo;
		target.ReloadTime = stats.ReloadTime;
		target.ManaCost = stats.ManaCost;
		target.KnockbackForce = stats.KnockbackForce;

		if (stats.BulletCount.HasValue) target.BulletCount = stats.BulletCount.Value;
		if (stats.Spread.HasValue) target.Spread = stats.Spread.Value;
		if (stats.BulletSpeed.HasValue) target.BulletSpeed = stats.BulletSpeed.Value;
		if (stats.IsPiercing.HasValue) target.IsPiercing = stats.IsPiercing.Value;
		if (stats.AllowMultiHit.HasValue) target.AllowMultiHit = stats.AllowMultiHit.Value;
		if (stats.IsContinuousLaser.HasValue) target.IsContinuousLaser = stats.IsContinuousLaser.Value;
		if (stats.SplashRadius.HasValue) target.SplashRadius = stats.SplashRadius.Value;

		if (copyAppliedUpgrades)
		{
			target.AppliedUpgrades = stats.AppliedUpgrades != null
				? new List<string>(stats.AppliedUpgrades)
				: new List<string>();
		}
	}

	/// <summary>
	/// Applies a single-weapon upgrade using canonical GameLogic rules and records its ID on the weapon.
	/// </summary>
	public static bool ApplyUpgradeToWeaponData(WeaponData weapon, string upgradeId, out int bonusAmmoAdded)
	{
		bonusAmmoAdded = 0;
		if (weapon == null || string.IsNullOrEmpty(upgradeId)) return false;

		var temp = CaptureWeaponStats(weapon);
		if (!GameLogic.ApplySingleWeaponUpgrade(temp, upgradeId, out bonusAmmoAdded))
		{
			return false;
		}

		ApplyStatsToWeaponData(weapon, temp, copyAppliedUpgrades: false);
		weapon.AppliedUpgrades ??= new List<string>();
		weapon.AppliedUpgrades.Add(upgradeId);
		return true;
	}

	public static WeaponData RestoreWeaponWithStats(string resourcePath, SavedWeaponStats stats, bool hasSingleWeaponUpgradesInSave = true)
	{
		if (string.IsNullOrEmpty(resourcePath)) return null;
		var freshWeapon = GameDataRegistry.LoadFreshWeapon(resourcePath);
		if (freshWeapon == null) return null;
		if (stats == null) return freshWeapon;

		// Tracked save: always rebuild from fresh disk .tres base stats + ordered upgrade IDs.
		if (stats.AppliedUpgrades != null)
		{
			if (stats.AppliedUpgrades.Count == 0)
			{
				return freshWeapon;
			}

			var upgraded = freshWeapon.CreateUniqueCopy();
			var recomputed = CaptureWeaponStats(freshWeapon);
			recomputed.AppliedUpgrades = new List<string>(stats.AppliedUpgrades);
			GameLogic.ReplaySingleWeaponUpgrades(recomputed, stats.AppliedUpgrades);
			ApplyStatsToWeaponData(upgraded, recomputed, copyAppliedUpgrades: true);
			return upgraded;
		}

		// Legacy save without AppliedUpgrades: if no single-weapon upgrades were bought in the save,
		// discard the frozen stat snapshot so updated .tres weapon stats take effect cleanly.
		if (!hasSingleWeaponUpgradesInSave)
		{
			return freshWeapon;
		}

		var legacyWeapon = freshWeapon.CreateUniqueCopy();
		ApplyStatsToWeaponData(legacyWeapon, stats, copyAppliedUpgrades: false);
		return legacyWeapon;
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
		save.SavedBaseMaxHealth = player.Config != null ? player.Config.MaxHealth : 100.0f;
		save.SavedBaseMaxMana = player.Config != null ? player.Config.MaxMana : 100.0f;
		save.BonusMaxHealth = player.BonusMaxHealth;
		save.BonusMaxMana = player.BonusMaxMana;
		save.BonusHealthRegen = player.BonusHealthRegen;
		save.BonusManaRegen = player.BonusManaRegen;
		save.BonusMoveSpeed = player.BonusMoveSpeed;
		save.WeaponDamageMultiplier = player.WeaponDamageMultiplier;
		save.SkillPoints = player.SkillPoints;
		save.Ability1Level = player.Ability1Level;
		save.Ability2Level = player.Ability2Level;
		save.Ability1CooldownScale = player.Ability1CooldownScale;
		save.Ability2CooldownScale = player.Ability2CooldownScale;
		save.Ability1DamageMultiplier = player.Ability1DamageMultiplier;
		save.Ability1RadiusMultiplier = player.Ability1RadiusMultiplier;
		save.Ability2DamageMultiplier = player.Ability2DamageMultiplier;
		save.Ability2RadiusMultiplier = player.Ability2RadiusMultiplier;
		save.PlayedTimeSeconds += level.ConsumePlayedTime();
		save.ShopPurchaseCounts = new Dictionary<string, int>(player.ShopPurchaseCounts);
		save.UnlockedRelics = player.GetRelicIdsList();
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
			if (data != null)
			{
				if (data.WeaponDamageMultiplier <= 0.01f) data.WeaponDamageMultiplier = 1.0f;
				if (data.Ability1CooldownScale <= 0.01f) data.Ability1CooldownScale = 1.0f;
				if (data.Ability2CooldownScale <= 0.01f) data.Ability2CooldownScale = 1.0f;
				if (data.Ability1DamageMultiplier <= 0.01f) data.Ability1DamageMultiplier = 1.0f;
				if (data.Ability1RadiusMultiplier <= 0.01f) data.Ability1RadiusMultiplier = 1.0f;
				if (data.Ability2DamageMultiplier <= 0.01f) data.Ability2DamageMultiplier = 1.0f;
				if (data.Ability2RadiusMultiplier <= 0.01f) data.Ability2RadiusMultiplier = 1.0f;
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
