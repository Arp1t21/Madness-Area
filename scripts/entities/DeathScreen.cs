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
		SettingsManager.ApplyFontQualityFixes();

		// 1. You Died screen (4 seconds)
		var rootControl = new Control();
		AddChild(rootControl);
		var viewportSize = GetViewport().GetVisibleRect().Size;
		float uiScale = Mathf.Clamp(viewportSize.X / 640.0f, 1.5f, 3.5f);
		rootControl.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
		rootControl.Size = viewportSize / uiScale;
		rootControl.Scale = new Vector2(uiScale, uiScale);

		var blackOverlay = new ColorRect();
		blackOverlay.Color = new Color(0.02f, 0.01f, 0.015f, 1.0f);
		rootControl.AddChild(blackOverlay);
		blackOverlay.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

		var vig = new ColorRect
		{
			Material = DungeonVisuals.GetVignetteMaterial(0.85f, 0.60f),
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		vig.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		rootControl.AddChild(vig);

		var titleFont = GameResourceCache.GetSilverFont();
		var bigFont = GameResourceCache.GetForwardFont();

		var youDiedLabel = new Label();
		youDiedLabel.Text = "YOU DIED";
		youDiedLabel.HorizontalAlignment = HorizontalAlignment.Center;
		youDiedLabel.VerticalAlignment = VerticalAlignment.Center;
		youDiedLabel.AddThemeFontOverride("font", bigFont);
		youDiedLabel.AddThemeFontSizeOverride("font_size", 32);
		youDiedLabel.AddThemeColorOverride("font_color", DungeonVisuals.CrimsonBright);
		DungeonVisuals.ApplyPixelOutline(youDiedLabel, 3, new Color(0.08f, 0f, 0f, 1f), 2, 2);
		youDiedLabel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center);
		youDiedLabel.Modulate = new Color(1, 1, 1, 0);
		rootControl.AddChild(youDiedLabel);

		var fadeIn = youDiedLabel.CreateTween();
		fadeIn.TweenProperty(youDiedLabel, "modulate:a", 1.0f, 0.85f);

		// Wait 4 seconds using a pause-immune timer
		await ToSignal(GetTree().CreateTimer(4.0, true), "timeout");

		// 2. Transition to Stats screen
		if (GodotObject.IsInstanceValid(youDiedLabel))
		{
			youDiedLabel.QueueFree();
		}
		
		blackOverlay.Color = new Color(0.025f, 0.02f, 0.03f, 0.90f);

		// Left Side: Hero Pedestal & Crimson Halo
		Vector2 heroPos = new Vector2(rootControl.Size.X * 0.23f, rootControl.Size.Y * 0.50f);
		var heroPedestal = new Panel
		{
			Position = heroPos - new Vector2(54, 62),
			Size = new Vector2(108, 124),
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		heroPedestal.AddThemeStyleboxOverride("panel", DungeonVisuals.CreatePanelStyle(
			DungeonVisuals.ObsidianDeep, new Color(0.75f, 0.20f, 0.25f, 0.75f), 2, 4, 8));
		DungeonVisuals.AttachFrameAccent(heroPedestal, DungeonVisuals.Crimson, 4);
		rootControl.AddChild(heroPedestal);

		var crimsonGlow = new TextureRect
		{
			Texture = GameResourceCache.GetRadialGlowTexture(
				new Color(0.95f, 0.18f, 0.22f, 0.35f),
				new Color(0.6f, 0.08f, 0.12f, 0.08f),
				0.5f, 36, new Vector2(0.9f, 0.9f)),
			Position = heroPos - new Vector2(36, 36),
			Size = new Vector2(72, 72),
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		rootControl.AddChild(crimsonGlow);

		if (_charAnimations != null)
		{
			var heroSprite = new AnimatedSprite2D();
			heroSprite.SpriteFrames = _charAnimations;
			heroSprite.Position = heroPos + new Vector2(0, 6);
			heroSprite.Scale = new Vector2(5.5f, 5.5f);
			heroSprite.TextureFilter = CanvasItem.TextureFilterEnum.Nearest;
			rootControl.AddChild(heroSprite);
			if (heroSprite.SpriteFrames.HasAnimation("idle"))
			{
				heroSprite.Play("idle");
			}
		}

		// Right Side: Framed Stats Card
		var statsCard = new Panel
		{
			Position = new Vector2(rootControl.Size.X * 0.43f, rootControl.Size.Y * 0.12f),
			Size = new Vector2(rootControl.Size.X * 0.51f, rootControl.Size.Y * 0.62f)
		};
		statsCard.AddThemeStyleboxOverride("panel", DungeonVisuals.CreatePanelStyle(
			DungeonVisuals.ObsidianPanel, DungeonVisuals.Crimson.Darkened(0.25f), 2, 4, 8));
		DungeonVisuals.AttachFrameAccent(statsCard, DungeonVisuals.Crimson, 5);
		rootControl.AddChild(statsCard);

		var statsVBox = new VBoxContainer();
		statsVBox.Position = new Vector2(18, 14);
		statsVBox.Size = new Vector2(statsCard.Size.X - 36, statsCard.Size.Y - 28);
		statsVBox.AddThemeConstantOverride("separation", 6);
		statsCard.AddChild(statsVBox);

		var statsTitle = new Label();
		statsTitle.Text = "EXPEDITION FALLEN";
		statsTitle.AddThemeFontOverride("font", titleFont);
		statsTitle.AddThemeFontSizeOverride("font_size", 30);
		statsTitle.AddThemeColorOverride("font_color", DungeonVisuals.CrimsonBright);
		DungeonVisuals.ApplyPixelOutline(statsTitle, 2);
		statsVBox.AddChild(statsTitle);

		DungeonVisuals.AddDivider(statsVBox, new Color(DungeonVisuals.Crimson.R, DungeonVisuals.Crimson.G, DungeonVisuals.Crimson.B, 0.55f), 1.5f);

		int mins = (int)(_timePlayed / 60);
		int secs = (int)(_timePlayed % 60);
		string timeStr = $"{mins:D2}:{secs:D2}";
		string weaponsStr = _weapons.Count > 0 ? string.Join(", ", _weapons).ToUpper() : "NONE";

		AddStatLabel($"HERO: {_charName.ToUpper()}", titleFont, statsVBox);
		AddStatLabel($"FLOOR REACHED: {_levelReached}", titleFont, statsVBox);
		AddStatLabel($"TIME SURVIVED: {timeStr}", titleFont, statsVBox);
		AddStatLabel($"GOLD COLLECTED: {_coins}", titleFont, statsVBox);
		AddStatLabel($"ARSENAL: {weaponsStr}", titleFont, statsVBox);

		// Back Button
		var backBtn = new Button();
		backBtn.Text = "RETURN TO MAIN MENU";
		backBtn.CustomMinimumSize = new Vector2(210, 32);
		backBtn.Position = new Vector2(rootControl.Size.X * 0.43f, rootControl.Size.Y * 0.78f);
		DungeonVisuals.StyleButton(backBtn, DungeonVisuals.BrassGold, titleFont, 21, 3, DungeonVisuals.ObsidianCard, 16, 6);
		rootControl.AddChild(backBtn);

		backBtn.Pressed += OnBackToMainMenuPressed;

		MainMenu.ConnectButtonSounds(rootControl);
	}

	private void AddStatLabel(string text, Font font, Control parent)
	{
		var label = new Label();
		label.Text = text;
		label.AddThemeFontOverride("font", font);
		label.AddThemeFontSizeOverride("font_size", 20);
		label.AddThemeColorOverride("font_color", new Color(0.92f, 0.92f, 0.92f));
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
