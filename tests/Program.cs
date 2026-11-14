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
		Test_ComputeHorizontalWallSide();
		Test_StatusAndBladeSynergies();
		Test_RelicDraftAndMilestoneFloors();
		Test_MadnessUnlockAndScaling();
		Test_FloorEventRolling();
		Test_CoopScalingAndRevive();

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
					AllowMultiHit = false,
					IsContinuousLaser = true,
					SplashRadius = 28.0f
				},
				null
			},
			UnlockedRelics = new List<string> { GameLogic.RelicPrismLens, GameLogic.RelicEmberCore },
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
		AssertEqual(true, roundTripped.WeaponSlotCustomStats[0].IsContinuousLaser.GetValueOrDefault(), "Round-trip IsContinuousLaser");
		AssertEqual(28.0f, roundTripped.WeaponSlotCustomStats[0].SplashRadius.GetValueOrDefault(), "Round-trip SplashRadius");
		AssertEqual(2, roundTripped.UnlockedRelics.Count, "Round-trip UnlockedRelics count");
		Assert(roundTripped.UnlockedRelics.Contains(GameLogic.RelicPrismLens), "Round-trip preserves RelicPrismLens");
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
		Assert(!st.IsContinuousLaser.HasValue, "Legacy IsContinuousLaser stays null");
		Assert(!st.SplashRadius.HasValue, "Legacy SplashRadius stays null");
	}

	private static void Test_ComputeHorizontalWallSide()
	{
		// Stepped upper room jutting right over wider lower room:
		// Upper room x=2..4, y=2..3; Lower room x=0..6, y=4..6
		var floor = new HashSet<(int, int)>();
		for (int x = 2; x <= 4; x++)
			for (int y = 2; y <= 3; y++)
				floor.Add((x, y));
		for (int x = 0; x <= 6; x++)
			for (int y = 4; y <= 6; y++)
				floor.Add((x, y));

		var lowerCap = new HashSet<(int, int)>();
		var upperBrick = new HashSet<(int, int)>();
		var lowerBrick = new HashSet<(int, int)>();
		// Top row y=0 (top trim), brick row y=1 above upper room x=2..4
		for (int x = 2; x <= 4; x++)
		{
			upperBrick.Add((x, 0));
			lowerBrick.Add((x, 1));
		}
		// Lower room shoulder brick rows at x=0..1 and x=5..6 at y=2..3
		foreach (int x in new[] { 0, 1, 5, 6 })
		{
			upperBrick.Add((x, 2));
			lowerBrick.Add((x, 3));
		}

		// Upper step side wall columns at x=1, y=0 and x=5, y=0 sit beside bricks (2,0) & (4,0):
		AssertEqual(
			GameLogic.HorizontalWallSide.RightEdgeOfLeftVoid,
			GameLogic.ComputeHorizontalWallSide(1, 0, floor, lowerCap, upperBrick, lowerBrick),
			"Left shoulder cap at (1,0) gets right-edge trim next to upper brick (2,0)");

		AssertEqual(
			GameLogic.HorizontalWallSide.LeftEdgeOfRightVoid,
			GameLogic.ComputeHorizontalWallSide(5, 0, floor, lowerCap, upperBrick, lowerBrick),
			"Right shoulder cap at (5,0) gets left-edge trim next to upper brick (4,0)");

		// Inner flush corridor piece bounded by floor at both sides gets Both:
		var bridgeFloor = new HashSet<(int, int)> { (0, 2), (2, 2) };
		AssertEqual(
			GameLogic.HorizontalWallSide.Both,
			GameLogic.ComputeHorizontalWallSide(1, 2, bridgeFloor, lowerCap, new HashSet<(int, int)>(), new HashSet<(int, int)>()),
			"1-tile separating column gets Both trims");

		// Lower cap cells count as walkable and receive no horizontal side wall themselves:
		var testLowerCap = new HashSet<(int, int)> { (2, 5) };
		AssertEqual(
			GameLogic.HorizontalWallSide.None,
			GameLogic.ComputeHorizontalWallSide(2, 5, floor, testLowerCap, upperBrick, lowerBrick),
			"Lower cap cell is walkable -> None");

		// Cell beside brick with no adjacent room or brick gets None:
		AssertEqual(
			GameLogic.HorizontalWallSide.None,
			GameLogic.ComputeHorizontalWallSide(99, 99, floor, lowerCap, upperBrick, lowerBrick),
			"Distant void cell gets None");
	}

	private static void Test_StatusAndBladeSynergies()
	{
		AssertEqual(0.0f, GameLogic.ComputeStatusDotTickDamage(0.0f, 0.5f), "Zero DPS -> 0 damage");
		AssertEqual(3.0f, GameLogic.ComputeStatusDotTickDamage(6.0f, 0.5f), "6 DPS at 0.5s tick -> 3.0");
		AssertEqual(0.5f, GameLogic.ComputeStatusDotTickDamage(0.2f, 0.5f), "Minimum DoT tick floor is 0.5");

		Assert(GameLogic.IsBladeWeaponName("Katana"), "Katana is blade");
		Assert(GameLogic.IsBladeWeaponName("Double Axe"), "Double Axe is blade");
		Assert(GameLogic.IsBladeWeaponName("Knight Sword"), "Knight Sword is blade");
		Assert(GameLogic.IsBladeWeaponName("Dagger"), "Dagger is blade");
		Assert(!GameLogic.IsBladeWeaponName("Prism Laser"), "Prism Laser is not blade");
		Assert(!GameLogic.IsBladeWeaponName("Revolver"), "Revolver is not blade");
	}

	private static void Test_RelicDraftAndMilestoneFloors()
	{
		Assert(!GameLogic.IsMilestoneRelicFloor(1), "Floor 1 not milestone");
		Assert(!GameLogic.IsMilestoneRelicFloor(4), "Floor 4 not milestone");
		Assert(GameLogic.IsMilestoneRelicFloor(5), "Floor 5 is milestone");
		Assert(GameLogic.IsMilestoneRelicFloor(20), "Floor 20 is milestone");

		Assert(GameLogic.FindRelic(GameLogic.RelicPrismLens) != null, "Prismatic Lens registered");
		Assert(GameLogic.FindRelic("UNKNOWN") == null, "Unknown relic returns null");

		var owned = new HashSet<string>
		{
			GameLogic.RelicPrismLens,
			GameLogic.RelicEmberCore,
			GameLogic.RelicFrostAegis
		};
		int seed = 0;
		var choices = GameLogic.PickRelicChoices(owned, n => (++seed) % n, 3);
		AssertEqual(3, choices.Count, "Milestone draft picks 3 relics");
		foreach (var c in choices)
		{
			Assert(!owned.Contains(c.Id), $"Drafted relic {c.Id} is not already owned");
		}
		var uniqueIds = new HashSet<string>();
		foreach (var c in choices) uniqueIds.Add(c.Id);
		AssertEqual(3, uniqueIds.Count, "All 3 drafted relics are distinct");

		// Almost all owned: returns remaining unowned count without duplicating
		var nearlyAll = new HashSet<string>();
		for (int i = 0; i < GameLogic.AllRelics.Length - 2; i++) nearlyAll.Add(GameLogic.AllRelics[i].Id);
		var remaining = GameLogic.PickRelicChoices(nearlyAll, _ => 0, 3);
		AssertEqual(2, remaining.Count, "Only 2 unowned left -> returns 2");
	}

	private static void Test_MadnessUnlockAndScaling()
	{
		// Floor <= 20: strictly inactive
		Assert(!GameLogic.IsMadnessUnlocked(1), "Madness locked floor 1");
		Assert(!GameLogic.IsMadnessUnlocked(20), "Madness locked on floor 20");
		AssertEqual(0.0f, GameLogic.AddMadnessOnKill(20, 0.0f, true, true), "No Madness gain on floor 20");
		AssertEqual(1.0f, GameLogic.GetMadnessAttackRateMultiplier(20, 100.0f), "No attack rate multiplier on floor 20");
		AssertEqual(0.0f, GameLogic.GetMadnessCritChanceBonus(20, 100.0f), "No crit bonus on floor 20");

		// Floor 21+: active, builds and decays after 2.2s idle
		Assert(GameLogic.IsMadnessUnlocked(21), "Madness unlocked floor 21");
		float m = GameLogic.AddMadnessOnKill(21, 0.0f, false, false);
		AssertEqual(14.0f, m, "Normal kill gives +14 Madness");
		m = GameLogic.AddMadnessOnKill(21, m, false, true);
		AssertEqual(38.0f, m, "Elite kill gives +24 Madness");
		m = GameLogic.AddMadnessOnKill(21, 90.0f, true, false);
		AssertEqual(100.0f, m, "Clamped at 100 Madness");

		AssertEqual(100.0f, GameLogic.DecayMadness(21, 100.0f, 1.0f, 1.5f), "No decay before 2.2s idle");
		Assert(GameLogic.DecayMadness(21, 100.0f, 1.0f, 3.0f) < 100.0f, "Decays after 2.2s idle");

		Assert(MathF.Abs(GameLogic.GetMadnessAttackRateMultiplier(21, 100.0f) - 1.45f) < 0.001f, "Max Madness attack rate +45%");
		Assert(MathF.Abs(GameLogic.GetMadnessCritChanceBonus(21, 100.0f) - 0.30f) < 0.001f, "Max Madness crit chance +30%");
		Assert(MathF.Abs(GameLogic.GetMadnessCritMultiplier(21, 100.0f) - 2.00f) < 0.001f, "Max Madness crit dmg 2.00x");
	}

	private static void Test_FloorEventRolling()
	{
		AssertEqual(GameLogic.FloorEventType.None, GameLogic.RollFloorEvent(1, 10), "Floor 1 no event");
		AssertEqual(GameLogic.FloorEventType.None, GameLogic.RollFloorEvent(2, 10), "Floor 2 no event");
		AssertEqual(GameLogic.FloorEventType.None, GameLogic.RollFloorEvent(15, 10), "Boss Floor 15 skips floor event");

		AssertEqual(GameLogic.FloorEventType.PitchBlack, GameLogic.RollFloorEvent(3, 10), "Floor 3 low roll -> PitchBlack");
		AssertEqual(GameLogic.FloorEventType.ImpFrenzy, GameLogic.RollFloorEvent(6, 50), "Floor 6 mid roll -> ImpFrenzy");
		AssertEqual(GameLogic.FloorEventType.MarksmanAmbush, GameLogic.RollFloorEvent(9, 85), "Floor 9 high roll -> MarksmanAmbush");
	}

	private static void Test_CoopScalingAndRevive()
	{
		// Solo parity when playerCount <= 1
		AssertEqual(GameLogic.CalculateWaveEnemyCount(1, 1.0f), GameLogic.CalculateCoopWaveEnemyCount(1, 1.0f, 1), "1p floor 1 matches solo");
		AssertEqual(GameLogic.CalculateWaveEnemyCount(5, 1.0f), GameLogic.CalculateCoopWaveEnemyCount(5, 1.0f, 1), "1p floor 5 matches solo");
		AssertEqual(GameLogic.CalculateWaveEnemyCount(1, 1.0f), GameLogic.CalculateCoopWaveEnemyCount(1, 1.0f, 0), "0p clamps to 1p");

		// Co-op wave counts
		AssertEqual(9, GameLogic.CalculateCoopWaveEnemyCount(1, 1.0f, 2), "2p floor 1 enemy count");
		AssertEqual(14, GameLogic.CalculateCoopWaveEnemyCount(1, 1.0f, 4), "4p floor 1 enemy count");
		AssertEqual(34, GameLogic.CalculateCoopWaveEnemyCount(5, 1.0f, 4), "4p boss floor 5 count");
		AssertEqual(14, GameLogic.CalculateCoopWaveEnemyCount(1, 1.0f, 99), "Clamps wave player count to MaxCoopPlayers");

		// Co-op health scaling
		AssertEqual(1.0f, GameLogic.CalculateCoopEnemyHealthMultiplier(false, 1), "1p normal HP mult");
		AssertEqual(1.0f, GameLogic.CalculateCoopEnemyHealthMultiplier(true, 1), "1p boss HP mult");
		Assert(MathF.Abs(GameLogic.CalculateCoopEnemyHealthMultiplier(false, 2) - 1.35f) < 0.001f, "2p normal HP mult 1.35x");
		Assert(MathF.Abs(GameLogic.CalculateCoopEnemyHealthMultiplier(false, 4) - 2.05f) < 0.001f, "4p normal HP mult 2.05x");
		Assert(MathF.Abs(GameLogic.CalculateCoopEnemyHealthMultiplier(true, 2) - 1.50f) < 0.001f, "2p boss HP mult 1.50x");
		Assert(MathF.Abs(GameLogic.CalculateCoopEnemyHealthMultiplier(true, 4) - 2.50f) < 0.001f, "4p boss HP mult 2.50x");
		Assert(MathF.Abs(GameLogic.CalculateCoopEnemyHealthMultiplier(true, 99) - 2.50f) < 0.001f, "HP mult clamps player count");

		// Co-op floor-descent revive health
		AssertEqual(50.0f, GameLogic.CalculateCoopReviveHealth(100.0f), "Revive at 50% max HP");
		AssertEqual(38.0f, GameLogic.CalculateCoopReviveHealth(75.0f), "Revive rounds cleanly");
		AssertEqual(1.0f, GameLogic.CalculateCoopReviveHealth(0.0f), "Zero max HP clamps to 1");
		AssertEqual(1.0f, GameLogic.CalculateCoopReviveHealth(1.0f), "Tiny max HP clamps to 1");
	}
}
