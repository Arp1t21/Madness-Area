using Godot;
using System;
using System.Collections.Generic;

public static class SettingsManager
{
	private const string ConfigPath = "user://settings.cfg";

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

	public static float MouseSensitivity = 1.0f;

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

		Brightness = (float)config.GetValue("Display", "Brightness", 1.0f);
		MaxFps = (int)config.GetValue("Display", "MaxFps", 60);
		VSync = (bool)config.GetValue("Display", "VSync", true);
		ScreenShake = (float)config.GetValue("Display", "ScreenShake", 1.0f);

		MasterVolume = (float)config.GetValue("Audio", "MasterVolume", 80f);
		MusicVolume = (float)config.GetValue("Audio", "MusicVolume", 70f);
		SFXVolume = (float)config.GetValue("Audio", "SFXVolume", 70f);

		MouseSensitivity = (float)config.GetValue("Controllers", "MouseSensitivity", 1.0f);

		var actions = new List<string>(KeyBinds.Keys);
		foreach (var action in actions)
		{
			if (config.HasSectionKey("Binds", action))
			{
				KeyBinds[action] = (Key)(int)config.GetValue("Binds", action);
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

		config.SetValue("Controllers", "MouseSensitivity", MouseSensitivity);

		foreach (var pair in KeyBinds)
		{
			config.SetValue("Binds", pair.Key, (int)pair.Value);
		}

		config.Save(ConfigPath);
	}

	public static void ApplySettings()
	{
		EnsureAudioBuses();

		Engine.MaxFps = MaxFps;
		DisplayServer.WindowSetVsyncMode(VSync ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled);

		int masterIdx = AudioServer.GetBusIndex("Master");
		if (masterIdx != -1) AudioServer.SetBusVolumeDb(masterIdx, Mathf.LinearToDb(MasterVolume / 100f));

		int musicIdx = AudioServer.GetBusIndex("Music");
		if (musicIdx != -1) AudioServer.SetBusVolumeDb(musicIdx, Mathf.LinearToDb(MusicVolume / 100f));

		int sfxIdx = AudioServer.GetBusIndex("SFX");
		if (sfxIdx != -1) AudioServer.SetBusVolumeDb(sfxIdx, Mathf.LinearToDb(SFXVolume / 100f));

		foreach (var pair in KeyBinds)
		{
			if (pair.Value == Key.None) continue;
			if (!InputMap.HasAction(pair.Key))
			{
				InputMap.AddAction(pair.Key);
			}

			InputMap.ActionEraseEvents(pair.Key);
			var keyEvent = new InputEventKey();
			keyEvent.PhysicalKeycode = pair.Value;
			keyEvent.Keycode = pair.Value;
			InputMap.ActionAddEvent(pair.Key, keyEvent);
		}
	}

	private static void EnsureAudioBuses()
	{
		int musicIdx = AudioServer.GetBusIndex("Music");
		if (musicIdx == -1)
		{
			AudioServer.AddBus();
			musicIdx = AudioServer.GetBusCount() - 1;
			AudioServer.SetBusName(musicIdx, "Music");
			AudioServer.SetBusSend(musicIdx, "Master");
		}

		int sfxIdx = AudioServer.GetBusIndex("SFX");
		if (sfxIdx == -1)
		{
			AudioServer.AddBus();
			sfxIdx = AudioServer.GetBusCount() - 1;
			AudioServer.SetBusName(sfxIdx, "SFX");
			AudioServer.SetBusSend(sfxIdx, "Master");
		}

		ConfigureReverb(sfxIdx);
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
