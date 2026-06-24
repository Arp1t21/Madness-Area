using Godot;
using System;
using System.Collections.Generic;

public partial class DeathScreen : CanvasLayer
{
	private string _charName;
	private SpriteFrames _charAnimations;
	private int _levelReached;
	private float _timePlayed;
	private int _coins;
	private List<string> _weapons;

	public void SetupStats(string name, SpriteFrames anims, int level, float time, int coins, List<string> weapons)
	{
		_charName = name;
		_charAnimations = anims;
		_levelReached = level;
		_timePlayed = time;
		_coins = coins;
		_weapons = weapons;
	}

	public override async void _Ready()
	{
		ProcessMode = ProcessModeEnum.Always;

		// 1. You Died screen (4 seconds)
		var rootControl = new Control();
		AddChild(rootControl);
		rootControl.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		rootControl.Size = GetViewport().GetVisibleRect().Size;

		var blackOverlay = new ColorRect();
		blackOverlay.Color = new Color(0, 0, 0, 1);
		rootControl.AddChild(blackOverlay);
		blackOverlay.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

		var titleFont = GD.Load<Font>("res://assets/fonts/silver/Silver.ttf");
		var bigFont = GD.Load<Font>("res://assets/fonts/fff-forward/FFFFORWA.TTF");

		var youDiedLabel = new Label();
		youDiedLabel.Text = "You Died";
		youDiedLabel.HorizontalAlignment = HorizontalAlignment.Center;
		youDiedLabel.VerticalAlignment = VerticalAlignment.Center;
		youDiedLabel.AddThemeFontOverride("font", bigFont);
		youDiedLabel.AddThemeFontSizeOverride("font_size", 30);
		youDiedLabel.AddThemeColorOverride("font_color", new Color(1, 0, 0));
		youDiedLabel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center);
		rootControl.AddChild(youDiedLabel);

		// Wait 4 seconds using a pause-immune timer
		await ToSignal(GetTree().CreateTimer(4.0, true), "timeout");

		// 2. Transition to Stats screen
		if (GodotObject.IsInstanceValid(youDiedLabel))
		{
			youDiedLabel.QueueFree();
		}
		
		// Semi-transparent black background instead of pure black
		blackOverlay.Color = new Color(0, 0, 0, 0.85f);

		// Left Side: Large Hero Sprite
		if (_charAnimations != null)
		{
			var heroSprite = new AnimatedSprite2D();
			heroSprite.SpriteFrames = _charAnimations;
			heroSprite.Position = new Vector2(rootControl.Size.X * 0.25f, rootControl.Size.Y * 0.5f);
			heroSprite.Scale = new Vector2(10.0f, 10.0f);
			heroSprite.TextureFilter = CanvasItem.TextureFilterEnum.Nearest;
			rootControl.AddChild(heroSprite);
			if (heroSprite.SpriteFrames.HasAnimation("idle"))
			{
				heroSprite.Play("idle");
			}
		}

		// Right Side: Stats Panel
		var statsVBox = new VBoxContainer();
		statsVBox.Position = new Vector2(rootControl.Size.X * 0.55f, rootControl.Size.Y * 0.2f);
		statsVBox.Size = new Vector2(rootControl.Size.X * 0.4f, rootControl.Size.Y * 0.6f);
		statsVBox.AddThemeConstantOverride("separation", 10);
		rootControl.AddChild(statsVBox);

		var statsTitle = new Label();
		statsTitle.Text = "DEATH STATISTICS";
		statsTitle.AddThemeFontOverride("font", titleFont);
		statsTitle.AddThemeFontSizeOverride("font_size", 24);
		statsTitle.AddThemeColorOverride("font_color", new Color(1, 0.2f, 0.2f));
		statsVBox.AddChild(statsTitle);

		int mins = (int)(_timePlayed / 60);
		int secs = (int)(_timePlayed % 60);
		string timeStr = $"{mins:D2}:{secs:D2}";
		string weaponsStr = _weapons.Count > 0 ? string.Join(", ", _weapons).ToUpper() : "NONE";

		AddStatLabel($"CHARACTER: {_charName.ToUpper()}", titleFont, statsVBox);
		AddStatLabel($"LEVEL REACHED: {_levelReached}", titleFont, statsVBox);
		AddStatLabel($"TIME PLAYED: {timeStr}", titleFont, statsVBox);
		AddStatLabel($"GOLD COLLECTED: {_coins}", titleFont, statsVBox);
		AddStatLabel($"WEAPONS OWNED:\n{weaponsStr}", titleFont, statsVBox);

		// Back Button
		var backBtn = new Button();
		backBtn.Text = "BACK TO MAIN MENU";
		backBtn.Flat = true;
		backBtn.AddThemeFontOverride("font", titleFont);
		backBtn.AddThemeFontSizeOverride("font_size", 14);
		backBtn.CustomMinimumSize = new Vector2(150, 25);
		backBtn.Position = new Vector2(rootControl.Size.X * 0.55f, rootControl.Size.Y * 0.75f);
		rootControl.AddChild(backBtn);

		var btnNormal = new StyleBoxEmpty();
		var btnHover = new StyleBoxFlat();
		btnHover.BgColor = new Color(1, 1, 1, 0.08f);
		backBtn.AddThemeStyleboxOverride("normal", btnNormal);
		backBtn.AddThemeStyleboxOverride("hover", btnHover);
		backBtn.AddThemeStyleboxOverride("pressed", btnNormal);
		backBtn.AddThemeStyleboxOverride("focus", btnNormal);

		backBtn.Pressed += OnBackToMainMenuPressed;

		MainMenu.ConnectButtonSounds(rootControl);
	}

	private void AddStatLabel(string text, Font font, Control parent)
	{
		var label = new Label();
		label.Text = text;
		label.AddThemeFontOverride("font", font);
		label.AddThemeFontSizeOverride("font_size", 14);
		label.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.9f));
		label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		parent.AddChild(label);
	}

	private void OnBackToMainMenuPressed()
	{
		GetTree().Paused = false;
		Engine.TimeScale = 1.0f;
		GetTree().ChangeSceneToFile("res://scene/levels/main_menu.tscn");
		QueueFree();
	}
}
