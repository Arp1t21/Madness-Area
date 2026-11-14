using System;
using System.Collections.Generic;

public class SavedWeaponStats
{
	public int Damage { get; set; }
	public float AttackCooldown { get; set; }
	public int MaxAmmo { get; set; }
	public float ReloadTime { get; set; }
	public float ManaCost { get; set; }
	public float KnockbackForce { get; set; }

	// Nullable so legacy saves missing these properties keep the weapon resource defaults.
	public int? BulletCount { get; set; }
	public float? Spread { get; set; }
	public float? BulletSpeed { get; set; }
	public bool? IsPiercing { get; set; }
	public bool? AllowMultiHit { get; set; }
	public bool? IsContinuousLaser { get; set; }
	public float? SplashRadius { get; set; }
}

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
	public float BonusHealthRegen { get; set; } = 0.0f;
	public float BonusManaRegen { get; set; } = 0.0f;
	public float BonusMoveSpeed { get; set; } = 0.0f;
	public float WeaponDamageMultiplier { get; set; } = 1.0f;
	public int SkillPoints { get; set; } = 0;
	public int Ability1Level { get; set; } = 0;
	public int Ability2Level { get; set; } = 0;
	public List<string> WeaponResourcePaths { get; set; } = new List<string>();
	public List<SavedWeaponStats> WeaponSlotCustomStats { get; set; } = new List<SavedWeaponStats>();
	public Dictionary<string, int> ShopPurchaseCounts { get; set; } = new Dictionary<string, int>();
	public List<string> UnlockedRelics { get; set; } = new List<string>();
	public string SaveDateString { get; set; } = "";
}
