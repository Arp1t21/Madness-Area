using Godot;
using System;
using System.Collections.Generic;
using FileAccess = Godot.FileAccess;

/// <summary>
/// Loads and verifies initial base stats for weapons, enemies, and player characters from disk
/// at startup and prior to launching saves without replacing cached C# Resource wrappers.
/// </summary>
public static class GameDataRegistry
{
	public readonly record struct PlayerBaseSnapshot(
		int MaxHealth,
		int MaxMana,
		float HealthRegen,
		float ManaRegen,
		float WalkSpeed,
		float RunMultiplier,
		float MeleeResistance,
		float RangedResistance,
		float EffectResistance);

	public readonly record struct EnemyBaseSnapshot(
		int MaxHealth,
		float Speed,
		float Damage,
		float AttackRange,
		float PreAttackDelay,
		float StopDuration,
		float MeleeResistance,
		float RangedResistance,
		float EffectResistance);

	private static readonly string[] KnownPlayerWeaponPaths =
	{
		"res://data/waepons/auto_rifle.tres",
		"res://data/waepons/axe.tres",
		"res://data/waepons/dagger.tres",
		"res://data/waepons/double_axe.tres",
		"res://data/waepons/greatsword.tres",
		"res://data/waepons/hunter_bow.tres",
		"res://data/waepons/katana.tres",
		"res://data/waepons/knight_sword.tres",
		"res://data/waepons/laser_cannon.tres",
		"res://data/waepons/magic_staff.tres",
		"res://data/waepons/revolver.tres",
		"res://data/waepons/shotgun.tres"
	};

	private static readonly string[] KnownEnemyWeaponPaths =
	{
		"res://data/waepons/enemies/archer_bow.tres",
		"res://data/waepons/enemies/cultist_staff.tres",
		"res://data/waepons/enemies/ghost.tres",
		"res://data/waepons/enemies/orc_melee.tres"
	};

	private static readonly string[] KnownEnemyPaths =
	{
		"res://data/enemies/archer.tres",
		"res://data/enemies/big_demon.tres",
		"res://data/enemies/chort.tres",
		"res://data/enemies/dark_cultist.tres",
		"res://data/enemies/ghost.tres",
		"res://data/enemies/orc_warrior.tres",
		"res://data/enemies/quagmire.tres"
	};

	private static readonly string[] KnownPlayerPaths =
	{
		"res://data/characters/knight.tres",
		"res://data/characters/magician.tres",
		"res://data/characters/rogue.tres",
		"res://data/characters/berserker.tres"
	};

	private static bool _hasVerifiedAtStartup = false;

	// Retain strong references to cached base resources so Godot/Mono C# handles are never finalized mid-run.
	private static readonly Dictionary<string, WeaponData> CachedWeapons = new Dictionary<string, WeaponData>(StringComparer.OrdinalIgnoreCase);
	private static readonly Dictionary<string, EnemyData> CachedEnemies = new Dictionary<string, EnemyData>(StringComparer.OrdinalIgnoreCase);
	private static readonly Dictionary<string, PlayerData> CachedPlayers = new Dictionary<string, PlayerData>(StringComparer.OrdinalIgnoreCase);

	public static readonly Dictionary<string, SavedWeaponStats> InitialWeaponStats = new Dictionary<string, SavedWeaponStats>(StringComparer.OrdinalIgnoreCase);
	public static readonly Dictionary<string, EnemyBaseSnapshot> InitialEnemyStats = new Dictionary<string, EnemyBaseSnapshot>(StringComparer.OrdinalIgnoreCase);
	public static readonly Dictionary<string, PlayerBaseSnapshot> InitialPlayerStats = new Dictionary<string, PlayerBaseSnapshot>(StringComparer.OrdinalIgnoreCase);

	public static void EnsureVerifiedAtStartup()
	{
		if (_hasVerifiedAtStartup) return;
		VerifyAndRefreshInitialStats(forceReload: true);
	}

	/// <summary>
	/// Re-reads all weapon, enemy, and character .tres files from disk and syncs scalar base stats
	/// onto cached resource instances safely without detaching active C# GCHandles.
	/// </summary>
	public static void VerifyAndRefreshInitialStats(bool forceReload = true)
	{
		_hasVerifiedAtStartup = true;

		InitialWeaponStats.Clear();
		InitialEnemyStats.Clear();
		InitialPlayerStats.Clear();

		foreach (string path in CollectResourcePaths("res://data/waepons", KnownPlayerWeaponPaths))
		{
			RegisterWeapon(path, forceReload);
		}
		foreach (string path in CollectResourcePaths("res://data/waepons/enemies", KnownEnemyWeaponPaths))
		{
			RegisterWeapon(path, forceReload);
		}
		foreach (string path in CollectResourcePaths("res://data/enemies", KnownEnemyPaths))
		{
			RegisterEnemy(path, forceReload);
		}
		foreach (string path in CollectResourcePaths("res://data/characters", KnownPlayerPaths))
		{
			RegisterPlayer(path, forceReload);
		}

		GD.Print($"[GameDataRegistry] Verified initial stats from disk: {InitialWeaponStats.Count} weapons, {InitialEnemyStats.Count} enemies, {InitialPlayerStats.Count} players.");
	}

	public static WeaponData LoadFreshWeapon(string resourcePath)
	{
		if (string.IsNullOrEmpty(resourcePath)) return null;
		EnsureVerifiedAtStartup();
		return GetOrLoadCached(resourcePath, CachedWeapons);
	}

	public static PlayerData LoadFreshPlayer(string resourcePath)
	{
		if (string.IsNullOrEmpty(resourcePath)) return null;
		EnsureVerifiedAtStartup();
		return GetOrLoadCached(resourcePath, CachedPlayers);
	}

	public static EnemyData LoadFreshEnemy(string resourcePath)
	{
		if (string.IsNullOrEmpty(resourcePath)) return null;
		EnsureVerifiedAtStartup();
		return GetOrLoadCached(resourcePath, CachedEnemies);
	}

	public static string ResolveFallbackEnemyPath(Enemy enemy)
	{
		return enemy switch
		{
			OrcWarrior => "res://data/enemies/orc_warrior.tres",
			Chort => "res://data/enemies/chort.tres",
			Archer => "res://data/enemies/archer.tres",
			DarkCultist => "res://data/enemies/dark_cultist.tres",
			Ghost => "res://data/enemies/ghost.tres",
			BigDemon => "res://data/enemies/big_demon.tres",
			Quagmire => "res://data/enemies/quagmire.tres",
			_ => null
		};
	}

	public static string ResolveFallbackEnemyWeaponPath(Enemy enemy)
	{
		return enemy switch
		{
			OrcWarrior => "res://data/waepons/enemies/orc_melee.tres",
			Chort => "res://data/waepons/enemies/orc_melee.tres",
			BigDemon => "res://data/waepons/enemies/orc_melee.tres",
			Archer => "res://data/waepons/enemies/archer_bow.tres",
			DarkCultist => "res://data/waepons/enemies/cultist_staff.tres",
			Ghost => "res://data/waepons/enemies/ghost.tres",
			_ => null
		};
	}

	private static T GetOrLoadCached<T>(string path, Dictionary<string, T> cache) where T : Resource
	{
		if (cache.TryGetValue(path, out T cached) && GodotObject.IsInstanceValid(cached))
		{
			return cached;
		}
		if (!ResourceLoader.Exists(path)) return null;
		T loaded = ResourceLoader.Load<T>(path, "", ResourceLoader.CacheMode.Reuse);
		if (loaded != null && GodotObject.IsInstanceValid(loaded))
		{
			cache[path] = loaded;
			return loaded;
		}
		return null;
	}

	private static Dictionary<string, string> TryReadTresProperties(string path)
	{
		if (string.IsNullOrEmpty(path) || !FileAccess.FileExists(path)) return null;
		using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
		if (file == null) return null;
		return GameLogic.ParseTresResourceProperties(file.GetAsText());
	}

	private static string GetString(Dictionary<string, string> p, string key, string fallback) =>
		p.TryGetValue(key, out string raw) && GameLogic.TryParseTresString(raw, out string val) ? val : fallback;

	private static int GetInt(Dictionary<string, string> p, string key, int fallback) =>
		p.TryGetValue(key, out string raw) && GameLogic.TryParseTresInt(raw, out int val) ? val : fallback;

	private static float GetFloat(Dictionary<string, string> p, string key, float fallback) =>
		p.TryGetValue(key, out string raw) && GameLogic.TryParseTresFloat(raw, out float val) ? val : fallback;

	private static bool GetBool(Dictionary<string, string> p, string key, bool fallback) =>
		p.TryGetValue(key, out string raw) && GameLogic.TryParseTresBool(raw, out bool val) ? val : fallback;

	private static void RegisterWeapon(string path, bool forceReload)
	{
		var w = GetOrLoadCached(path, CachedWeapons);
		if (w == null) return;

		if (forceReload)
		{
			var props = TryReadTresProperties(path);
			if (props != null)
			{
				w.WeaponName = GetString(props, "WeaponName", "Новое оружие");
				w.Damage = GetInt(props, "Damage", 10);
				w.AttackCooldown = GetFloat(props, "AttackCooldown", 0.5f);
				w.AttackRange = GetFloat(props, "AttackRange", 100.0f);
				w.KnockbackForce = GetFloat(props, "KnockbackForce", 15.0f);
				w.Recoil = GetFloat(props, "Recoil", 0.0f);
				w.ManaCost = GetFloat(props, "ManaCost", 0.0f);
				w.AllowMultiHit = GetBool(props, "AllowMultiHit", false);
				w.IsAutoAim = GetBool(props, "IsAutoAim", false);
				w.OrientationOffset = GetFloat(props, "OrientationOffset", 0.0f);
				w.FlipOrientationOffset = GetFloat(props, "FlipOrientationOffset", 0.0f);
				w.IsRanged = GetBool(props, "IsRanged", false);
				w.BulletSpeed = GetFloat(props, "BulletSpeed", 300.0f);
				w.BulletCount = GetInt(props, "BulletCount", 1);
				w.Spread = GetFloat(props, "Spread", 0.0f);
				w.IsPiercing = GetBool(props, "IsPiercing", false);
				w.MaxAmmo = GetInt(props, "MaxAmmo", 10);
				w.ReloadTime = GetFloat(props, "ReloadTime", 1.5f);
				w.IsContinuousLaser = GetBool(props, "IsContinuousLaser", false);
				w.SplashRadius = GetFloat(props, "SplashRadius", 26.0f);
			}
		}

		var stats = SaveManager.CaptureWeaponStats(w);
		stats.AppliedUpgrades = null;
		InitialWeaponStats[path] = stats;
	}

	private static void RegisterEnemy(string path, bool forceReload)
	{
		var e = GetOrLoadCached(path, CachedEnemies);
		if (e == null) return;

		if (forceReload)
		{
			var props = TryReadTresProperties(path);
			if (props != null)
			{
				e.EnemyName = GetString(props, "EnemyName", "Гоблин");
				e.Speed = GetFloat(props, "Speed", 50.0f);
				e.MaxHealth = GetInt(props, "MaxHealth", 30);
				e.Damage = GetFloat(props, "Damage", 10.0f);
				e.AttackRange = GetFloat(props, "AttackRange", 35.0f);
				e.PreAttackDelay = GetFloat(props, "PreAttackDelay", 1.0f);
				e.StopDuration = GetFloat(props, "StopDuration", 1.0f);
				e.MeleeResistance = GetFloat(props, "MeleeResistance", 0.0f);
				e.RangedResistance = GetFloat(props, "RangedResistance", 0.0f);
				e.EffectResistance = GetFloat(props, "EffectResistance", 0.0f);
				e.CoinDropChance = GetFloat(props, "CoinDropChance", 0.2f);
				e.ManaCrystalDropChance = GetFloat(props, "ManaCrystalDropChance", 0.1f);
				e.IsRanged = GetBool(props, "IsRanged", false);
			}
		}

		InitialEnemyStats[path] = new EnemyBaseSnapshot(
			e.MaxHealth,
			e.Speed,
			e.Damage,
			e.AttackRange,
			e.PreAttackDelay,
			e.StopDuration,
			e.MeleeResistance,
			e.RangedResistance,
			e.EffectResistance);
	}

	private static void RegisterPlayer(string path, bool forceReload)
	{
		var p = GetOrLoadCached(path, CachedPlayers);
		if (p == null) return;

		if (forceReload)
		{
			var props = TryReadTresProperties(path);
			if (props != null)
			{
				p.CharacterName = GetString(props, "CharacterName", "Новый герой");
				p.MaxHealth = GetInt(props, "MaxHealth", 100);
				p.HealthRegen = GetFloat(props, "HealthRegen", 0.0f);
				p.MaxMana = GetInt(props, "MaxMana", 100);
				p.ManaRegen = GetFloat(props, "ManaRegen", 0.0f);
				p.ManaCostPerSecondRunning = GetFloat(props, "ManaCostPerSecondRunning", 10.0f);
				p.WalkSpeed = GetFloat(props, "WalkSpeed", 70.0f);
				p.RunMultiplier = GetFloat(props, "RunMultiplier", 1.8f);
				p.MeleeResistance = GetFloat(props, "MeleeResistance", 0.0f);
				p.RangedResistance = GetFloat(props, "RangedResistance", 0.0f);
				p.EffectResistance = GetFloat(props, "EffectResistance", 0.0f);
			}
		}

		InitialPlayerStats[path] = new PlayerBaseSnapshot(
			p.MaxHealth,
			p.MaxMana,
			p.HealthRegen,
			p.ManaRegen,
			p.WalkSpeed,
			p.RunMultiplier,
			p.MeleeResistance,
			p.RangedResistance,
			p.EffectResistance);
	}

	private static List<string> CollectResourcePaths(string dirPath, string[] knownFallbacks)
	{
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var result = new List<string>();

		if (knownFallbacks != null)
		{
			foreach (string p in knownFallbacks)
			{
				if (!string.IsNullOrEmpty(p) && seen.Add(p))
				{
					result.Add(p);
				}
			}
		}

		using var dir = DirAccess.Open(dirPath);
		if (dir != null)
		{
			dir.ListDirBegin();
			string fileName = dir.GetNext();
			while (!string.IsNullOrEmpty(fileName))
			{
				if (!dir.CurrentIsDir())
				{
					string normalized = fileName.EndsWith(".remap", StringComparison.OrdinalIgnoreCase)
						? fileName[..^6]
						: fileName;
					if (normalized.EndsWith(".tres", StringComparison.OrdinalIgnoreCase))
					{
						string fullPath = dirPath.TrimEnd('/') + "/" + normalized;
						if (seen.Add(fullPath))
						{
							result.Add(fullPath);
						}
					}
				}
				fileName = dir.GetNext();
			}
			dir.ListDirEnd();
		}

		return result;
	}
}
