using System;
using System.Collections.Generic;
using System.Text.Json;

internal static class Program
{
	private static int _passed = 0;
	private static int _failed = 0;

	private static void Assert(bool condition, string message)
	{
		if (condition)
		{
			_passed++;
		}
		else
		{
			_failed++;
			Console.Error.WriteLine($"FAIL: {message}");
		}
	}

	private static void AssertEqual<T>(T expected, T actual, string label)
	{
		if (EqualityComparer<T>.Default.Equals(expected, actual))
		{
			_passed++;
		}
		else
		{
			_failed++;
			Console.Error.WriteLine($"FAIL [{label}]: expected '{expected}', got '{actual}'");
		}
	}

	private static int Main()
	{
		Test_ResolveCharacterDataPath();
		Test_CalculateWaveEnemyCount();
		Test_AbilityLevelCaps();
		Test_CalculateAmbientLight();
		Test_FormatWeaponList();
		Test_AssignKeyBind();
		Test_SaveModelsSerializationAndLegacyCompat();

		Console.WriteLine($"Unit tests finished: {_passed} passed, {_failed} failed.");
		return _failed == 0 ? 0 : 1;
	}

	private static void Test_ResolveCharacterDataPath()
	{
		AssertEqual("res://data/characters/knight.tres", GameLogic.ResolveCharacterDataPath("Knight"), "Resolve Knight");
		AssertEqual("res://data/characters/knight.tres", GameLogic.ResolveCharacterDataPath("knight"), "Resolve knight lower");
		AssertEqual("res://data/characters/magician.tres", GameLogic.ResolveCharacterDataPath("magician"), "Resolve magician");
		AssertEqual("res://data/characters/rogue.tres", GameLogic.ResolveCharacterDataPath("Rogue"), "Resolve Rogue");
		AssertEqual("res://data/characters/berserker.tres", GameLogic.ResolveCharacterDataPath("BERSERKER"), "Resolve Berserker");
		AssertEqual("res://data/characters/knight.tres", GameLogic.ResolveCharacterDataPath(""), "Resolve empty fallback");
		AssertEqual("res://data/characters/knight.tres", GameLogic.ResolveCharacterDataPath(null), "Resolve null fallback");
		AssertEqual("res://data/characters/knight.tres", GameLogic.ResolveCharacterDataPath("Other"), "Resolve unknown fallback");
	}

	private static void Test_CalculateWaveEnemyCount()
	{
		AssertEqual(6, GameLogic.CalculateWaveEnemyCount(1, 1.0f), "Floor 1 normal");
		AssertEqual(8, GameLogic.CalculateWaveEnemyCount(2, 1.0f), "Floor 2 normal");
		AssertEqual(15, GameLogic.CalculateWaveEnemyCount(5, 1.0f), "Floor 5 boss");
		AssertEqual(25, GameLogic.CalculateWaveEnemyCount(10, 1.0f), "Floor 10 boss");
		AssertEqual(4, GameLogic.CalculateWaveEnemyCount(1, 0.5f), "Floor 1 min clamp");
		AssertEqual(8, GameLogic.CalculateWaveEnemyCount(5, 0.5f), "Floor 5 scaled boss");
		AssertEqual(6, GameLogic.CalculateWaveEnemyCount(0, 1.0f), "Floor 0 no boss");
	}

	private static void Test_AbilityLevelCaps()
	{
		AssertEqual(4, GameLogic.MaxAbilityLevel, "MaxAbilityLevel constant");
		Assert(GameLogic.CanUpgradeAbility(0), "Can upgrade from 0");
		Assert(GameLogic.CanUpgradeAbility(3), "Can upgrade from 3");
		Assert(!GameLogic.CanUpgradeAbility(4), "Cannot upgrade at 4");
		Assert(!GameLogic.CanUpgradeAbility(5), "Cannot upgrade above 4");

		AssertEqual(0, GameLogic.ClampAbilityLevel(-3), "Clamp negative");
		AssertEqual(2, GameLogic.ClampAbilityLevel(2), "Clamp in range");
		AssertEqual(4, GameLogic.ClampAbilityLevel(4), "Clamp max");
		AssertEqual(4, GameLogic.ClampAbilityLevel(99), "Clamp overflow");
	}

	private static void Test_CalculateAmbientLight()
	{
		Assert(MathF.Abs(GameLogic.CalculateAmbientLight(1.0f) - 0.15f) < 0.0001f, "Brightness 1.0 -> 0.15");
		Assert(MathF.Abs(GameLogic.CalculateAmbientLight(1.5f) - 0.35f) < 0.0001f, "Brightness 1.5 -> 0.35");
		Assert(MathF.Abs(GameLogic.CalculateAmbientLight(2.0f) - 0.55f) < 0.0001f, "Brightness 2.0 -> 0.55");
		Assert(MathF.Abs(GameLogic.CalculateAmbientLight(0.0f) - 0.15f) < 0.0001f, "Brightness clamped low");
		Assert(MathF.Abs(GameLogic.CalculateAmbientLight(5.0f) - 0.55f) < 0.0001f, "Brightness clamped high");
	}

	private static void Test_FormatWeaponList()
	{
		AssertEqual("NONE", GameLogic.FormatWeaponList(null), "Null weapon list");
		AssertEqual("NONE", GameLogic.FormatWeaponList(Array.Empty<string>()), "Empty weapon list");
		AssertEqual("NONE", GameLogic.FormatWeaponList(new[] { "", "   " }), "Whitespace slots");
		AssertEqual(
			"BATTLE_AXE, REVOLVER",
			GameLogic.FormatWeaponList(new[] { "res://data/waepons/battle_axe.tres", "", "res://data/waepons/revolver.tres" }),
			"Mixed slots with empty gap");
	}

	private static void Test_AssignKeyBind()
	{
		var binds = new Dictionary<string, int>
		{
			["ability_1"] = 81, // Q
			["ability_2"] = 86, // V
			["interact"] = 69   // E
		};

		// No conflict
		string swapped = GameLogic.AssignKeyBind(binds, "ability_1", 70, 0);
		Assert(swapped == null, "No swap on free key");
		AssertEqual(70, binds["ability_1"], "Updated key assigned");

		// Conflict swaps previous key onto conflicting action
		swapped = GameLogic.AssignKeyBind(binds, "ability_1", 69, 0);
		AssertEqual("interact", swapped, "Returns conflicting action");
		AssertEqual(69, binds["ability_1"], "ability_1 takes E");
		AssertEqual(70, binds["interact"], "interact receives old ability_1 key");

		// Unset newKey ignored
		swapped = GameLogic.AssignKeyBind(binds, "ability_2", 0, 0);
		Assert(swapped == null, "Unset key ignored");
		AssertEqual(86, binds["ability_2"], "Existing key unchanged when unset passed");

		// Rebinding an initially unset action unsets conflicting action
		binds["dash"] = 0;
		swapped = GameLogic.AssignKeyBind(binds, "dash", 86, 0);
		AssertEqual("ability_2", swapped, "Returns conflicting action on unset swap");
		AssertEqual(86, binds["dash"], "dash takes V");
		AssertEqual(0, binds["ability_2"], "ability_2 cleared to unset");
	}

	private static void Test_SaveModelsSerializationAndLegacyCompat()
	{
		var original = new SaveData
		{
			SlotId = "slot2",
			CharacterName = "Rogue",
			CurrentLevelIndex = 5,
			LevelSeed = 123456789UL,
			PlayedTimeSeconds = 42.5f,
			Coins = 99,
			CurrentHealth = 38.5f,
			CurrentMana = 80.0f,
			WeaponDamageMultiplier = 1.25f,
			SkillPoints = 2,
			Ability1Level = 3,
			Ability2Level = 4,
			WeaponResourcePaths = new List<string> { "res://data/waepons/shotgun.tres", "" },
			WeaponSlotCustomStats = new List<SavedWeaponStats>
			{
				new SavedWeaponStats
				{
					Damage = 9,
					AttackCooldown = 0.65f,
					MaxAmmo = 8,
					ReloadTime = 1.4f,
					ManaCost = 0f,
					KnockbackForce = 12f,
					BulletCount = 6,
					Spread = 0.35f,
					BulletSpeed = 420f,
					IsPiercing = true,
					AllowMultiHit = false
				},
				null
			},
			ShopPurchaseCounts = new Dictionary<string, int> { ["ABILITY_1_LEVEL"] = 3 }
		};

		string json = JsonSerializer.Serialize(original);
		var roundTripped = JsonSerializer.Deserialize<SaveData>(json);
		Assert(roundTripped != null, "Round-trip deserialized non-null");
		AssertEqual("Rogue", roundTripped.CharacterName, "Round-trip CharacterName");
		AssertEqual(123456789UL, roundTripped.LevelSeed, "Round-trip LevelSeed");
		AssertEqual(2, roundTripped.WeaponSlotCustomStats.Count, "Round-trip weapon slot count");
		AssertEqual(6, roundTripped.WeaponSlotCustomStats[0].BulletCount.GetValueOrDefault(), "Round-trip BulletCount");
		AssertEqual(true, roundTripped.WeaponSlotCustomStats[0].IsPiercing.GetValueOrDefault(), "Round-trip IsPiercing");
		Assert(roundTripped.WeaponSlotCustomStats[1] == null, "Round-trip preserves empty slot null");

		// Legacy JSON without BulletCount / Spread / BulletSpeed / IsPiercing / AllowMultiHit
		const string legacyJson = """
		{
		  "SlotId": "1",
		  "CharacterName": "Knight",
		  "CurrentLevelIndex": 3,
		  "WeaponSlotCustomStats": [
		    {
		      "Damage": 18,
		      "AttackCooldown": 0.45,
		      "MaxAmmo": 0,
		      "ReloadTime": 0,
		      "ManaCost": 0,
		      "KnockbackForce": 14
		    }
		  ]
		}
		""";

		var legacy = JsonSerializer.Deserialize<SaveData>(legacyJson);
		Assert(legacy != null && legacy.WeaponSlotCustomStats.Count == 1, "Legacy JSON loaded");
		var st = legacy.WeaponSlotCustomStats[0];
		AssertEqual(18, st.Damage, "Legacy Damage preserved");
		Assert(!st.BulletCount.HasValue, "Legacy BulletCount stays null");
		Assert(!st.Spread.HasValue, "Legacy Spread stays null");
		Assert(!st.BulletSpeed.HasValue, "Legacy BulletSpeed stays null");
		Assert(!st.IsPiercing.HasValue, "Legacy IsPiercing stays null");
		Assert(!st.AllowMultiHit.HasValue, "Legacy AllowMultiHit stays null");
	}
}
