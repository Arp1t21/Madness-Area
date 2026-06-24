using Godot;
using System;

public partial class MusicManager : Node
{
	private static MusicManager _instance;
	public static MusicManager Instance => _instance;

	private AudioStreamPlayer _audioPlayer;
	private string _currentTrackPath = "";

	public override void _Ready()
	{
		if (_instance != null && _instance != this)
		{
			QueueFree();
			return;
		}
		_instance = this;
		ProcessMode = ProcessModeEnum.Always; // Keep playing when game is paused

		_audioPlayer = new AudioStreamPlayer();
		_audioPlayer.Bus = "Music";
		AddChild(_audioPlayer);

		PlayMusic("res://assets/sounds/doom_10. Ties That Bind.mp3");
	}

	public void PlayMusic(string path)
	{
		if (_currentTrackPath == path)
			return;

		_currentTrackPath = path;
		if (string.IsNullOrEmpty(path))
		{
			_audioPlayer.Stop();
			return;
		}

		var stream = GD.Load<AudioStream>(path);
		if (stream != null)
		{
			_audioPlayer.Stream = stream;
			_audioPlayer.Play();
		}
		else
		{
			GD.PrintErr($"Failed to load music track: {path}");
		}
	}

	public void StopMusic()
	{
		_currentTrackPath = "";
		_audioPlayer.Stop();
	}
}
