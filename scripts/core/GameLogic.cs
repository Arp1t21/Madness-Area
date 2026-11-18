using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

/// <summary>
/// Pure C# gameplay/domain helpers (engine-agnostic so unit tests can exercise them directly).
/// </summary>
public static class GameLogic
{
	public const int MaxAbilityLevel = 4;

	/// <summary>
	/// Resolves the PlayerData .tres resource path for a hero name (defaults to Knight).
	/// </summary>
	public static string ResolveCharacterDataPath(string characterName)
	{
		if (!string.IsNullOrEmpty(characterName))
		{
			if (characterName.Equals("Magician", StringComparison.OrdinalIgnoreCase))
				return "res://data/characters/magician.tres";
			if (characterName.Equals("Rogue", StringComparison.OrdinalIgnoreCase))
				return "res://data/characters/rogue.tres";
			if (characterName.Equals("Berserker", StringComparison.OrdinalIgnoreCase))
				return "res://data/characters/berserker.tres";
		}
		return "res://data/characters/knight.tres";
	}

	/// <summary>
	/// Unified enemy count calculation used by both Level generation and WaveManager spawning.
	/// </summary>
	public static int CalculateWaveEnemyCount(int levelIndex, float difficultyMultiplier = 1.0f)
	{
		int w = Math.Max(0, levelIndex - 1);
		int baseCount = 6 + w * 2;
		int scaled = Math.Max(4, (int)MathF.Round(baseCount * difficultyMultiplier));
		if (levelIndex > 0 && levelIndex % 5 == 0)
		{
			scaled += 1; // +1 Boss on every 5th floor
		}
		return scaled;
	}

	/// <summary>
	/// Delay in seconds between consecutive enemy spawns inside a wave.
	/// Gently scales from 0.40s on floor 1 up to a 0.90s cap on deep floors.
	/// </summary>
	public static float CalculateWaveSpawnInterval(int waveIndex)
	{
		int w = Math.Max(0, waveIndex - 1);
		return Math.Clamp(0.40f + w * 0.05f, 0.40f, 0.90f);
	}

	public static bool CanUpgradeAbility(int currentLevel) => currentLevel < MaxAbilityLevel;

	public static int ClampAbilityLevel(int level) => Math.Clamp(level, 0, MaxAbilityLevel);

	/// <summary>
	/// Converts the Brightness setting [1.0 .. 2.0] into CanvasModulate ambient color channel [0.15 .. 0.55].
	/// Keeps unlit areas noticeably dark while side and bottom walls remain visible against the black void.
	/// </summary>
	public static float CalculateAmbientLight(float brightness)
	{
		return Math.Clamp(0.15f + (brightness - 1.0f) * 0.40f, 0.15f, 0.55f);
	}

	/// <summary>
	/// Formats weapon resource paths for slot summaries, ignoring empty slots.
	/// </summary>
	public static string FormatWeaponList(IEnumerable<string> weaponResourcePaths)
	{
		if (weaponResourcePaths == null) return "NONE";

		var names = new List<string>();
		foreach (string path in weaponResourcePaths)
		{
			if (string.IsNullOrWhiteSpace(path)) continue;
			string name = System.IO.Path.GetFileNameWithoutExtension(path).Trim();
			if (!string.IsNullOrEmpty(name))
			{
				names.Add(name.ToUpperInvariant());
			}
		}

		return names.Count > 0 ? string.Join(", ", names) : "NONE";
	}

	/// <summary>
	/// Assigns <paramref name="newKey"/> to <paramref name="action"/>; if another action currently
	/// holds <paramref name="newKey"/>, swaps the previous binding onto that other action so no binds clash.
	/// </summary>
	public static string AssignKeyBind<TKey>(
		IDictionary<string, TKey> keyBinds,
		string action,
		TKey newKey,
		TKey unsetKey) where TKey : notnull
	{
		if (keyBinds == null || string.IsNullOrEmpty(action) || EqualityComparer<TKey>.Default.Equals(newKey, unsetKey))
		{
			return null;
		}

		TKey previousKey = keyBinds.TryGetValue(action, out var existing) ? existing : unsetKey;
		string conflictingAction = null;

		foreach (var pair in keyBinds)
		{
			if (!pair.Key.Equals(action, StringComparison.Ordinal) &&
				EqualityComparer<TKey>.Default.Equals(pair.Value, newKey))
			{
				conflictingAction = pair.Key;
				break;
			}
		}

		if (conflictingAction != null && !EqualityComparer<TKey>.Default.Equals(previousKey, unsetKey))
		{
			keyBinds[conflictingAction] = previousKey;
		}
		else if (conflictingAction != null)
		{
			keyBinds[conflictingAction] = unsetKey;
		}

		keyBinds[action] = newKey;
		return conflictingAction;
	}

	public enum HorizontalWallSide
	{
		None = 0,
		RightEdgeOfLeftVoid = 1, // Walkable room on x+1 (trim on right edge of tile, alt=1)
		LeftEdgeOfRightVoid = 2, // Walkable room on x-1 (trim on left edge of tile, alt=0)
		Both = 3                 // 1-tile vertical peninsula/pillar between two rooms
	}

	public static HorizontalWallSide ComputeHorizontalWallSide(
		int x,
		int y,
		IReadOnlySet<(int x, int y)> floorSet,
		IReadOnlySet<(int x, int y)> lowerCapSet,
		IReadOnlySet<(int x, int y)> upperBrickSet,
		IReadOnlySet<(int x, int y)> lowerBrickSet)
	{
		var pos = (x, y);
		if (floorSet.Contains(pos) || lowerCapSet.Contains(pos))
		{
			return HorizontalWallSide.None;
		}

		bool isBrickWall = upperBrickSet.Contains(pos) || lowerBrickSet.Contains(pos);
		bool Walkable(int cx, int cy) => floorSet.Contains((cx, cy)) || lowerCapSet.Contains((cx, cy));

		bool trimOnRightEdge = Walkable(x + 1, y);
		bool trimOnLeftEdge = Walkable(x - 1, y);

		// Frame exposed ends of upper/lower horizontal brick runs (outer corners and step corners),
		// without inserting vertical trims between adjacent bricks in straight walls.
		if (!trimOnRightEdge && !isBrickWall)
		{
			if (upperBrickSet.Contains((x + 1, y)) || lowerBrickSet.Contains((x + 1, y)))
			{
				trimOnRightEdge = true;
			}
		}

		if (!trimOnLeftEdge && !isBrickWall)
		{
			if (upperBrickSet.Contains((x - 1, y)) || lowerBrickSet.Contains((x - 1, y)))
			{
				trimOnLeftEdge = true;
			}
		}

		if (trimOnRightEdge && trimOnLeftEdge) return HorizontalWallSide.Both;
		if (trimOnRightEdge) return HorizontalWallSide.RightEdgeOfLeftVoid;
		if (trimOnLeftEdge) return HorizontalWallSide.LeftEdgeOfRightVoid;
		return HorizontalWallSide.None;
	}

	public static float ComputeStatusDotTickDamage(float dps, float tickIntervalSeconds)
	{
		if (dps <= 0.0f || tickIntervalSeconds <= 0.0f) return 0.0f;
		return MathF.Max(0.5f, dps * tickIntervalSeconds);
	}

	public static bool IsBladeWeaponName(string weaponName)
	{
		if (string.IsNullOrWhiteSpace(weaponName)) return false;
		return weaponName.IndexOf("Katana", StringComparison.OrdinalIgnoreCase) >= 0 ||
			   weaponName.IndexOf("Dagger", StringComparison.OrdinalIgnoreCase) >= 0 ||
			   weaponName.IndexOf("Sword", StringComparison.OrdinalIgnoreCase) >= 0 ||
			   weaponName.IndexOf("Axe", StringComparison.OrdinalIgnoreCase) >= 0 ||
			   weaponName.IndexOf("Blade", StringComparison.OrdinalIgnoreCase) >= 0;
	}

	// --- Passive Relics & Floor 5 Milestone Draft ---

	public const string RelicPrismLens = "RELIC_PRISM_LENS";
	public const string RelicRicochet = "RELIC_RICOCHET";
	public const string RelicEmberCore = "RELIC_EMBER_CORE";
	public const string RelicVenomCoat = "RELIC_VENOM_COAT";
	public const string RelicCrimsonEdge = "RELIC_CRIMSON_EDGE";
	public const string RelicChainSpark = "RELIC_CHAIN_SPARK";
	public const string RelicFrostAegis = "RELIC_FROST_AEGIS";
	public const string RelicVampiricFang = "RELIC_VAMPIRIC_FANG";
	public const string RelicVolatileSigil = "RELIC_VOLATILE_SIGIL";

	public sealed record RelicDefinition(
		string Id,
		string Name,
		string ShortTag,
		string Description,
		int ShopCost,
		int AccentHex);

	public static readonly RelicDefinition[] AllRelics = new[]
	{
		new RelicDefinition(RelicPrismLens, "Prismatic Lens", "LASER+", "Lasers ricochet off walls (up to 3 bounces) and +35% splash radius", 22, 0x38F2FF),
		new RelicDefinition(RelicRicochet, "Ricochet Chambers", "BOUNCE", "Projectiles ricochet off walls +1 time & +12% projectile speed", 20, 0xFFCC44),
		new RelicDefinition(RelicEmberCore, "Ember Core", "BURN", "Weapon hits ignite enemies with Burn (6 DPS, 3.0s)", 18, 0xFF6A2B),
		new RelicDefinition(RelicVenomCoat, "Venom Coat", "POISON", "Weapon hits poison enemies (5 DPS, 3.5s) and slow by 15%", 18, 0x4CE65A),
		new RelicDefinition(RelicCrimsonEdge, "Crimson Edge", "BLEED", "+10% Crit chance; crits & blades inflict deep Bleed (7 DPS)", 20, 0xFF334E),
		new RelicDefinition(RelicChainSpark, "Storm Conduit", "CHAIN", "Kills arc lightning to up to 2 nearby enemies (9 dmg)", 21, 0x62C6FF),
		new RelicDefinition(RelicFrostAegis, "Frost Aegis", "FROST", "Shield [V] freezes (0.9s Stun) and Slows nearby enemies", 18, 0x7AE8FF),
		new RelicDefinition(RelicVampiricFang, "Vampiric Fang", "LEECH", "Heal +1 HP every 7 kills (+2 HP on Boss kills)", 22, 0xE63B7A),
		new RelicDefinition(RelicVolatileSigil, "Volatile Sigil", "BLAST", "Slain foes detonate in a crimson AoE blast", 21, 0xFF9436)
	};

	public static RelicDefinition FindRelic(string id)
	{
		if (string.IsNullOrEmpty(id)) return null;
		foreach (var r in AllRelics)
		{
			if (string.Equals(r.Id, id, StringComparison.Ordinal)) return r;
		}
		return null;
	}

	public static bool IsMilestoneRelicFloor(int levelIndex) => levelIndex > 0 && levelIndex % 5 == 0;

	public static List<RelicDefinition> PickRelicChoices(IReadOnlyCollection<string> ownedRelics, Func<int, int> nextInt, int count = 3)
	{
		var unowned = new List<RelicDefinition>();
		var fallback = new List<RelicDefinition>();
		foreach (var relic in AllRelics)
		{
			if (ownedRelics == null || !ownedRelics.Contains(relic.Id))
			{
				unowned.Add(relic);
			}
			else
			{
				fallback.Add(relic);
			}
		}

		// Fisher-Yates shuffle unowned using provided deterministic RNG selector
		for (int i = unowned.Count - 1; i > 0; i--)
		{
			int j = Math.Clamp(nextInt(i + 1), 0, i);
			(unowned[i], unowned[j]) = (unowned[j], unowned[i]);
		}

		var result = new List<RelicDefinition>(Math.Min(count, AllRelics.Length));
		for (int i = 0; i < unowned.Count && result.Count < count; i++)
		{
			result.Add(unowned[i]);
		}
		return result;
	}

	// --- Madness System (Unlocks AFTER Floor 20; ZERO enemy buffs!) ---

	public const int MadnessUnlockFloor = 20;

	public static bool IsMadnessUnlocked(int levelIndex) => levelIndex > MadnessUnlockFloor;

	public static float AddMadnessOnKill(int levelIndex, float currentMadness, bool isBoss, bool isElite)
	{
		if (!IsMadnessUnlocked(levelIndex)) return 0.0f;
		float gain = isBoss ? 45.0f : (isElite ? 24.0f : 14.0f);
		return Math.Clamp(currentMadness + gain, 0.0f, 100.0f);
	}

	public static float DecayMadness(int levelIndex, float currentMadness, float deltaSeconds, float timeSinceLastKill)
	{
		if (!IsMadnessUnlocked(levelIndex)) return 0.0f;
		if (timeSinceLastKill < 2.2f || deltaSeconds <= 0.0f) return Math.Clamp(currentMadness, 0.0f, 100.0f);
		return Math.Clamp(currentMadness - 4.8f * deltaSeconds, 0.0f, 100.0f);
	}

	public static float GetMadnessAttackRateMultiplier(int levelIndex, float madness)
	{
		if (!IsMadnessUnlocked(levelIndex) || madness <= 0.0f) return 1.0f;
		return 1.0f + (Math.Clamp(madness, 0.0f, 100.0f) / 100.0f) * 0.45f;
	}

	public static float GetMadnessCritChanceBonus(int levelIndex, float madness)
	{
		if (!IsMadnessUnlocked(levelIndex) || madness <= 0.0f) return 0.0f;
		return (Math.Clamp(madness, 0.0f, 100.0f) / 100.0f) * 0.30f;
	}

	public static float GetMadnessCritMultiplier(int levelIndex, float madness)
	{
		if (!IsMadnessUnlocked(levelIndex) || madness <= 0.0f) return 1.50f;
		return 1.65f + (Math.Clamp(madness, 0.0f, 100.0f) / 100.0f) * 0.35f;
	}

	// --- Floor Events & Elites ---

	public enum FloorEventType
	{
		None = 0,
		PitchBlack = 1,
		ImpFrenzy = 2,
		MarksmanAmbush = 3,
		SpikeChamber = 4
	}

	public const float MaxNormalSpikeFloorRatio = 0.06f;
	public const float SpikeChamberFloorRatio = 0.40f;

	public static bool ShouldDropFloorWeapon(int levelIndex)
	{
		return levelIndex > 0 && levelIndex % 3 == 0;
	}

	public static FloorEventType RollFloorEvent(int levelIndex, int roll0To99)
	{
		if (levelIndex < 3 || levelIndex % 5 == 0 || levelIndex % 3 != 0)
		{
			return FloorEventType.None;
		}
		int r = ((roll0To99 % 100) + 100) % 100;
		if (r < 34) return FloorEventType.PitchBlack;
		if (r < 67) return FloorEventType.ImpFrenzy;
		return FloorEventType.MarksmanAmbush;
	}

	/// <summary>
	/// Rolls a floor event including the 1% Spike Chamber (40% floor spikes) event (Floor 2+).
	/// </summary>
	public static FloorEventType RollFloorEventWithSpike(int levelIndex, int eventRoll0To99, int spikeRoll0To99)
	{
		int spikeRoll = ((spikeRoll0To99 % 100) + 100) % 100;
		if (levelIndex >= 2 && spikeRoll == 0)
		{
			return FloorEventType.SpikeChamber;
		}
		return RollFloorEvent(levelIndex, eventRoll0To99);
	}

	/// <summary>
	/// Returns the target fraction of eligible floor tiles covered by spikes.
	/// Slow progression on early floors, capped at 6% normally or 40% during Spike Chamber.
	/// </summary>
	public static float CalculateSpikeFloorRatio(int levelIndex, FloorEventType evt = FloorEventType.None)
	{
		if (evt == FloorEventType.SpikeChamber)
		{
			return SpikeChamberFloorRatio;
		}
		if (levelIndex <= 1) return 0.005f;
		if (levelIndex == 2) return 0.007f;
		return Math.Min(MaxNormalSpikeFloorRatio, 0.009f + (levelIndex - 3) * 0.0075f);
	}

	public static string GetFloorEventTitle(FloorEventType evt) => evt switch
	{
		FloorEventType.PitchBlack => "PITCH BLACK (+50% GOLD)",
		FloorEventType.ImpFrenzy => "IMP SWARM",
		FloorEventType.MarksmanAmbush => "MARKSMAN AMBUSH",
		FloorEventType.SpikeChamber => "SPIKE CHAMBER (40%)",
		_ => ""
	};

	// --- Co-op Scaling & Revive Helpers ---

	public const int MaxCoopPlayers = 4;

	/// <summary>
	/// Scales wave enemy count for co-op sessions (+45% non-boss enemies per extra player).
	/// Preserves exact solo behavior when playerCount &lt;= 1.
	/// </summary>
	public static int CalculateCoopWaveEnemyCount(int levelIndex, float difficultyMultiplier = 1.0f, int playerCount = 1)
	{
		int clampedPlayers = Math.Clamp(playerCount, 1, MaxCoopPlayers);
		if (clampedPlayers <= 1)
		{
			return CalculateWaveEnemyCount(levelIndex, difficultyMultiplier);
		}

		int w = Math.Max(0, levelIndex - 1);
		int baseCount = 6 + w * 2;
		float coopMult = 1.0f + (clampedPlayers - 1) * 0.45f;
		int scaled = Math.Max(4, (int)MathF.Round(baseCount * difficultyMultiplier * coopMult));
		if (levelIndex > 0 && levelIndex % 5 == 0)
		{
			scaled += 1; // +1 Boss on every 5th floor
		}
		return scaled;
	}

	/// <summary>
	/// Scales enemy/boss max health in co-op sessions (+35% normal / +50% boss per extra player).
	/// </summary>
	public static float CalculateCoopEnemyHealthMultiplier(bool isBoss, int playerCount = 1)
	{
		int clampedPlayers = Math.Clamp(playerCount, 1, MaxCoopPlayers);
		if (clampedPlayers <= 1) return 1.0f;
		float perExtra = isBoss ? 0.50f : 0.35f;
		return 1.0f + (clampedPlayers - 1) * perExtra;
	}

	/// <summary>
	/// Computes health restored when a fallen teammate revives upon descending to the next floor.
	/// </summary>
	public static float CalculateCoopReviveHealth(float maxHealth)
	{
		if (maxHealth <= 0.0f) return 1.0f;
		return Math.Max(1.0f, MathF.Round(maxHealth * 0.50f));
	}

	/// <summary>
	/// Returns the 5-floor enemy progression tier (0 on floors 1-4, +1 every 5 floors: 5, 10, 15...).
	/// </summary>
	public static int CalculateEnemyFloorTier(int levelIndex) => Math.Max(0, levelIndex / 5);

	/// <summary>
	/// Scales enemy max HP slightly every 5 floors (+22% compounding per tier).
	/// </summary>
	public static float CalculateEnemyFloorHealthMultiplier(int levelIndex)
	{
		return MathF.Pow(1.22f, CalculateEnemyFloorTier(levelIndex));
	}

	/// <summary>
	/// Scales enemy damage slightly every 5 floors (+15% compounding per tier).
	/// </summary>
	public static float CalculateEnemyFloorDamageMultiplier(int levelIndex)
	{
		return MathF.Pow(1.15f, CalculateEnemyFloorTier(levelIndex));
	}

	/// <summary>
	/// Scales enemy movement speed slightly every 5 floors (+6% per tier, capped at +42%).
	/// </summary>
	public static float CalculateEnemyFloorSpeedMultiplier(int levelIndex)
	{
		int tier = CalculateEnemyFloorTier(levelIndex);
		return 1.0f + MathF.Min(0.42f, tier * 0.06f);
	}

	/// <summary>
	/// Scales enemy attack/windup speed slightly every 5 floors (+8% per tier, capped at +50%).
	/// </summary>
	public static float CalculateEnemyFloorAttackSpeedMultiplier(int levelIndex)
	{
		int tier = CalculateEnemyFloorTier(levelIndex);
		return 1.0f + MathF.Min(0.50f, tier * 0.08f);
	}

	/// <summary>
	/// Returns smooth 0..1 danger factor for the player's low-HP blood vignette (begins below 55% HP).
	/// </summary>
	public static float CalculateLowHealthDangerFactor(float healthRatio)
	{
		if (healthRatio >= 0.55f) return 0.0f;
		if (healthRatio <= 0.12f) return 1.0f;
		float t = Math.Clamp((0.55f - healthRatio) / (0.55f - 0.12f), 0.0f, 1.0f);
		return t * t * (3.0f - 2.0f * t);
	}

	/// <summary>
	/// Returns true when a save's shop counts contain at least one single-weapon upgrade ("SINGLE_*").
	/// </summary>
	public static bool HasAnySingleWeaponPurchase(IDictionary<string, int> shopPurchaseCounts)
	{
		if (shopPurchaseCounts == null) return false;
		foreach (var kv in shopPurchaseCounts)
		{
			if (!string.IsNullOrEmpty(kv.Key) &&
				kv.Key.StartsWith("SINGLE_", StringComparison.Ordinal) &&
				kv.Value > 0)
			{
				return true;
			}
		}
		return false;
	}

	/// <summary>
	/// Single canonical implementation of single-weapon shop upgrades, shared by live altar
	/// purchases and save-load replay onto refreshed .tres weapon base stats.
	/// </summary>
	public static bool ApplySingleWeaponUpgrade(SavedWeaponStats stats, string upgradeId, out int bonusAmmoAdded)
	{
		bonusAmmoAdded = 0;
		if (stats == null || string.IsNullOrEmpty(upgradeId)) return false;

		switch (upgradeId)
		{
			case "SINGLE_EXTENDED_MAG":
				stats.MaxAmmo += 5;
				bonusAmmoAdded = 5;
				return true;
			case "SINGLE_QUICKLOADER":
				stats.ReloadTime = MathF.Max(0.15f, stats.ReloadTime * 0.90f);
				stats.AttackCooldown = MathF.Max(0.05f, stats.AttackCooldown * 0.91f);
				return true;
			case "SINGLE_MANA_REDUCTION":
				stats.ManaCost = MathF.Max(0.0f, stats.ManaCost * 0.80f);
				return true;
			case "SINGLE_SHARPENED_CORE":
				stats.Damage = Math.Max(stats.Damage + 2, (int)MathF.Round(stats.Damage * 1.17f));
				return true;
			case "SINGLE_IMPACT_CORE":
				stats.KnockbackForce = MathF.Round(stats.KnockbackForce * 1.25f * 10f) / 10f;
				stats.AttackCooldown = MathF.Max(0.05f, stats.AttackCooldown * 0.93f);
				return true;
			case "SINGLE_FEATHER_GRIP":
				stats.AttackCooldown = MathF.Max(0.05f, stats.AttackCooldown * 0.88f);
				return true;
			case "SINGLE_PIERCING":
				stats.IsPiercing = true;
				stats.Damage = Math.Max(stats.Damage + 1, (int)MathF.Round(stats.Damage * 1.09f));
				return true;
			case "SINGLE_MULTISHOT":
				stats.BulletCount = (stats.BulletCount ?? 1) + 1;
				stats.Spread = MathF.Max(stats.Spread ?? 0.0f, 0.08f);
				return true;
			case "SINGLE_VELOCITY_CORE":
				stats.BulletSpeed = MathF.Round((stats.BulletSpeed ?? 300.0f) * 1.25f);
				stats.Damage = Math.Max(stats.Damage + 1, (int)MathF.Round(stats.Damage * 1.07f));
				return true;
			case "SINGLE_CLEAVE_EDGE":
				stats.AllowMultiHit = true;
				stats.Damage = Math.Max(stats.Damage + 2, (int)MathF.Round(stats.Damage * 1.08f));
				return true;
			default:
				return false;
		}
	}

	/// <summary>
	/// Replays an ordered upgrade list on top of a fresh base weapon stats snapshot.
	/// </summary>
	public static void ReplaySingleWeaponUpgrades(SavedWeaponStats stats, IEnumerable<string> appliedUpgrades)
	{
		if (stats == null || appliedUpgrades == null) return;
		foreach (string upgradeId in appliedUpgrades)
		{
			ApplySingleWeaponUpgrade(stats, upgradeId, out _);
		}
	}

	/// <summary>
	/// Preserves the player's saved HP/MP ratio when character base MaxHealth/MaxMana changes in .tres.
	/// </summary>
	public static float AdjustCurrentResourceForBaseChange(
		float savedCurrent,
		float savedBaseMax,
		float currentBaseMax,
		float bonusMax,
		float minValue)
	{
		float currentMax = MathF.Max(minValue, currentBaseMax + bonusMax);
		if (savedBaseMax > 0.01f && MathF.Abs(savedBaseMax - currentBaseMax) > 0.01f)
		{
			float oldMax = MathF.Max(1.0f, savedBaseMax + bonusMax);
			float ratio = Math.Clamp(savedCurrent / oldMax, 0.0f, 1.0f);
			return Math.Clamp(ratio * currentMax, minValue, currentMax);
		}
		return Math.Clamp(savedCurrent, minValue, currentMax);
	}

	/// <summary>
	/// Computes effective movement speed from base walk speed, Slow strength, and sprint multiplier.
	/// </summary>
	public static float CalculatePlayerMoveSpeed(
		float walkSpeedEffective,
		float slowStrength,
		bool isRunning,
		float runMultiplier)
	{
		float baseWalk = MathF.Max(0.0f, walkSpeedEffective);
		float speedMod = 1.0f - Math.Clamp(slowStrength, 0.0f, 0.95f);
		float speed = baseWalk * speedMod;
		if (isRunning && runMultiplier > 0.0f)
		{
			speed *= runMultiplier;
		}
		return speed;
	}

	/// <summary>
	/// Computes real-time movement speed magnitude for HUD display, returning 0 when dead, immobilized, or stationary.
	/// </summary>
	public static float CalculateRealTimeMoveSpeed(
		bool isDead,
		bool isImmobilized,
		float moveDirX,
		float moveDirY,
		float walkSpeedEffective,
		float slowStrength,
		bool isRunning,
		float runMultiplier,
		float extVelX,
		float extVelY)
	{
		if (isDead || isImmobilized) return 0.0f;

		float currentSpeed = CalculatePlayerMoveSpeed(walkSpeedEffective, slowStrength, isRunning, runMultiplier);
		float vx = moveDirX * currentSpeed + extVelX;
		float vy = moveDirY * currentSpeed + extVelY;
		float magSq = vx * vx + vy * vy;
		if (magSq <= 0.0001f) return 0.0f;
		return MathF.Sqrt(magSq);
	}

	/// <summary>
	/// Extracts scalar key-value properties from the [resource] section of a Godot .tres file.
	/// </summary>
	public static Dictionary<string, string> ParseTresResourceProperties(string tresContent)
	{
		var props = new Dictionary<string, string>(StringComparer.Ordinal);
		if (string.IsNullOrEmpty(tresContent)) return props;

		bool inResource = false;
		string[] lines = tresContent.Split('\n');
		foreach (string rawLine in lines)
		{
			string line = rawLine.Trim();
			if (line.Length == 0 || line[0] == ';' || line[0] == '#') continue;

			if (line[0] == '[' && line[^1] == ']')
			{
				if (string.Equals(line, "[resource]", StringComparison.OrdinalIgnoreCase))
				{
					inResource = true;
					continue;
				}
				if (inResource) break;
				continue;
			}

			if (!inResource) continue;

			int eq = line.IndexOf('=');
			if (eq <= 0) continue;

			string key = line[..eq].Trim();
			if (!IsValidPropertyIdentifier(key)) continue;

			string rawVal = line[(eq + 1)..].Trim();
			props[key] = rawVal;
		}

		return props;
	}

	private static bool IsValidPropertyIdentifier(string key)
	{
		if (string.IsNullOrEmpty(key)) return false;
		char first = key[0];
		if (!(char.IsLetter(first) || first == '_')) return false;
		for (int i = 1; i < key.Length; i++)
		{
			char c = key[i];
			if (!(char.IsLetterOrDigit(c) || c == '_')) return false;
		}
		return true;
	}

	/// <summary>
	/// Unescapes and extracts a quoted Godot .tres string literal.
	/// </summary>
	public static bool TryParseTresString(string rawValue, out string value)
	{
		value = null;
		if (string.IsNullOrEmpty(rawValue) || rawValue.Length < 2 || rawValue[0] != '"' || rawValue[^1] != '"')
		{
			return false;
		}

		string inner = rawValue[1..^1];
		var sb = new System.Text.StringBuilder(inner.Length);
		for (int i = 0; i < inner.Length; i++)
		{
			char c = inner[i];
			if (c == '\\' && i + 1 < inner.Length)
			{
				char next = inner[++i];
				sb.Append(next switch
				{
					'"' => '"',
					'\\' => '\\',
					'n' => '\n',
					'r' => '\r',
					't' => '\t',
					_ => next,
				});
			}
			else
			{
				sb.Append(c);
			}
		}

		value = sb.ToString();
		return true;
	}

	/// <summary>
	/// Parses a finite invariant-culture floating-point literal from a .tres property.
	/// </summary>
	public static bool TryParseTresFloat(string rawValue, out float value)
	{
		if (float.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && float.IsFinite(value))
		{
			return true;
		}
		value = 0.0f;
		return false;
	}

	/// <summary>
	/// Parses an invariant-culture integer literal from a .tres property.
	/// </summary>
	public static bool TryParseTresInt(string rawValue, out int value)
	{
		return int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
	}

	public static bool TryParseTresBool(string rawValue, out bool value)
	{
		if (string.Equals(rawValue, "true", StringComparison.OrdinalIgnoreCase))
		{
			value = true;
			return true;
		}
		if (string.Equals(rawValue, "false", StringComparison.OrdinalIgnoreCase))
		{
			value = false;
			return true;
		}
		value = false;
		return false;
	}
}
