using Godot;
using System;
using System.Collections.Generic;

public static class SettingsManager
{
	private const string ConfigPath = "user://settings.cfg";
	private static bool _fontsConfigured = false;

	public static float ReverbRoomSize = 0.35f;
	public static float ReverbDampness = 0.4f;
	public static float ReverbWet = 0.2f;
	public static float ReverbDry = 0.8f;

	public static float Brightness = 1.0f;
	public static int MaxFps = 60;
	public static bool VSync = true;
	public static float ScreenShake = 1.0f;

	public static float MasterVolume = 80f;
	public static float MusicVolume = 70f;
	public static float SFXVolume = 70f;
	public static string CoopPlayerName = "Adventurer";

	public static Dictionary<string, Key> KeyBinds = new Dictionary<string, Key>()
	{
		{ "move_left", Key.A },
		{ "move_right", Key.D },
		{ "move_up", Key.W },
		{ "move_down", Key.S },
		{ "interact", Key.E },
		{ "reload", Key.R },
		{ "ability_1", Key.Q },
		{ "ability_2", Key.V }
	};

	public static Dictionary<string, Key> CreateDefaultKeyBinds()
	{
		return new Dictionary<string, Key>()
		{
			{ "move_left", Key.A },
			{ "move_right", Key.D },
			{ "move_up", Key.W },
			{ "move_down", Key.S },
			{ "interact", Key.E },
			{ "reload", Key.R },
			{ "ability_1", Key.Q },
			{ "ability_2", Key.V }
		};
	}

	public static void LoadSettings()
	{
		EnsureAudioBuses();

		var config = new ConfigFile();
		Error err = config.Load(ConfigPath);
		if (err != Error.Ok)
		{
			SaveSettings();
			ApplySettings();
			return;
		}

		Brightness = Mathf.Clamp((float)config.GetValue("Display", "Brightness", 1.0f), 1.0f, 2.0f);
		MaxFps = (int)config.GetValue("Display", "MaxFps", 60);
		VSync = (bool)config.GetValue("Display", "VSync", true);
		ScreenShake = Mathf.Clamp((float)config.GetValue("Display", "ScreenShake", 1.0f), 0.0f, 2.0f);

		MasterVolume = Mathf.Clamp((float)config.GetValue("Audio", "MasterVolume", 80f), 0f, 100f);
		MusicVolume = Mathf.Clamp((float)config.GetValue("Audio", "MusicVolume", 70f), 0f, 100f);
		SFXVolume = Mathf.Clamp((float)config.GetValue("Audio", "SFXVolume", 70f), 0f, 100f);
		string loadedName = (string)config.GetValue("Coop", "PlayerName", "Adventurer");
		CoopPlayerName = NetworkManager.SanitizePlayerName(loadedName, "Adventurer");

		var actions = new List<string>(KeyBinds.Keys);
		foreach (var action in actions)
		{
			if (config.HasSectionKey("Binds", action))
			{
				Key loadedKey = (Key)(int)config.GetValue("Binds", action);
				if (loadedKey != Key.None)
				{
					KeyBinds[action] = loadedKey;
				}
			}
		}

		ApplySettings();
	}

	public static void SaveSettings()
	{
		EnsureAudioBuses();

		var config = new ConfigFile();
		config.SetValue("Display", "Brightness", Brightness);
		config.SetValue("Display", "MaxFps", MaxFps);
		config.SetValue("Display", "VSync", VSync);
		config.SetValue("Display", "ScreenShake", ScreenShake);

		config.SetValue("Audio", "MasterVolume", MasterVolume);
		config.SetValue("Audio", "MusicVolume", MusicVolume);
		config.SetValue("Audio", "SFXVolume", SFXVolume);
		config.SetValue("Coop", "PlayerName", NetworkManager.SanitizePlayerName(CoopPlayerName, "Adventurer"));

		foreach (var pair in KeyBinds)
		{
			config.SetValue("Binds", pair.Key, (int)pair.Value);
		}

		config.Save(ConfigPath);
	}

	public static void ApplySettings()
	{
		EnsureAudioBuses();
		ApplyFontQualityFixes();

		Engine.MaxFps = MaxFps;
		DisplayServer.WindowSetVsyncMode(VSync ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled);

		SetBusVolume("Master", MasterVolume);
		SetBusVolume("Music", MusicVolume);
		SetBusVolume("SFX", SFXVolume);
		// UI bus tracks SFX slider volume without dungeon reverb
		SetBusVolume("UI", SFXVolume);

		foreach (var pair in KeyBinds)
		{
			if (pair.Value == Key.None) continue;
			if (!InputMap.HasAction(pair.Key))
			{
				InputMap.AddAction(pair.Key);
			}

			InputMap.ActionEraseEvents(pair.Key);
			// Use PhysicalKeycode only so movement/abilities remain layout-independent (EN/RU/etc.).
			var keyEvent = new InputEventKey
			{
				PhysicalKeycode = pair.Value
			};
			InputMap.ActionAddEvent(pair.Key, keyEvent);
		}
	}

	private static void SetBusVolume(string busName, float volumePercent)
	{
		int busIdx = AudioServer.GetBusIndex(busName);
		if (busIdx == -1) return;

		float linear = Mathf.Clamp(volumePercent / 100f, 0f, 1f);
		AudioServer.SetBusVolumeDb(busIdx, linear <= 0.0001f ? -80.0f : Mathf.LinearToDb(linear));
		AudioServer.SetBusMute(busIdx, linear <= 0.0001f);
	}

	public static void ApplyFontQualityFixes()
	{
		if (_fontsConfigured) return;
		_fontsConfigured = true;

		Font[] fonts = new[]
		{
			GameResourceCache.GetSilverFont(),
			GameResourceCache.GetForwardFont()
		};

		foreach (var font in fonts)
		{
			FontFile ff = GameResourceCache.UnwrapFontFile(font);
			if (ff != null)
			{
				ff.Antialiasing = TextServer.FontAntialiasing.Gray;
				ff.Hinting = TextServer.Hinting.Light;
				ff.SubpixelPositioning = TextServer.SubpixelPositioning.Auto;
				ff.Oversampling = 4.0f;
			}
		}
	}

	private static void EnsureAudioBuses()
	{
		EnsureBus("Music");
		int sfxIdx = EnsureBus("SFX");
		EnsureBus("UI");

		if (sfxIdx != -1)
		{
			ConfigureReverb(sfxIdx);
		}
	}

	private static int EnsureBus(string busName)
	{
		int idx = AudioServer.GetBusIndex(busName);
		if (idx == -1)
		{
			AudioServer.AddBus();
			idx = AudioServer.GetBusCount() - 1;
			AudioServer.SetBusName(idx, busName);
			AudioServer.SetBusSend(idx, "Master");
		}
		return idx;
	}

	private static void ConfigureReverb(int sfxIdx)
	{
		AudioEffectReverb reverb = null;
		for (int i = 0; i < AudioServer.GetBusEffectCount(sfxIdx); i++)
		{
			if (AudioServer.GetBusEffect(sfxIdx, i) is AudioEffectReverb r)
			{
				reverb = r;
				break;
			}
		}

		if (reverb == null)
		{
			reverb = new AudioEffectReverb();
			AudioServer.AddBusEffect(sfxIdx, reverb);
		}

		reverb.RoomSize = ReverbRoomSize;
		reverb.Damping = ReverbDampness;
		reverb.Wet = ReverbWet;
		reverb.Dry = ReverbDry;
	}
}
