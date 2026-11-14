using System;
using System.Collections.Generic;

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
}
