using Godot;
using System;
using System.Collections.Generic;

public partial class DeathScreen : CanvasLayer
{
	private string _charName = "Hero";
	private SpriteFrames _charAnimations;
	private int _levelReached;
	private float _timePlayed;
	private int _coins;
	private List<string> _weapons = new List<string>();

	public void SetupStats(string name, SpriteFrames anims, int level, float time, int coins, List<string> weapons)
	{
		_charName = string.IsNullOrEmpty(name) ? "Hero" : name;
		_charAnimations = anims;
		_levelReached = level;
		_timePlayed = time;
		_coins = coins;
		_weapons = weapons ?? new List<string>();
	}

	private bool _introCanSkip = false;
	private bool _introSkipped = false;

	public override void _Input(InputEvent @event)
	{
		if (!_introCanSkip || _introSkipped) return;
		if ((@event is InputEventKey k && k.Pressed && !k.Echo) ||
			(@event is InputEventMouseButton mb && mb.Pressed))
		{
			_introSkipped = true;
			GetViewport().SetInputAsHandled();
		}
	}

	public override async void _Ready()
	{
		ProcessMode = ProcessModeEnum.Always;
		Layer = 25;
		SettingsManager.ApplyFontQualityFixes();

		var rootControl = new Control();
		AddChild(rootControl);
		rootControl.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		Vector2 viewSize = GetViewport().GetVisibleRect().Size;

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

		var introBox = new VBoxContainer
		{
			Alignment = BoxContainer.AlignmentMode.Center,
			Modulate = new Color(1, 1, 1, 0),
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		introBox.AddThemeConstantOverride("separation", 10);
		introBox.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		rootControl.AddChild(introBox);

		var youDiedLabel = new Label
		{
			Text = "YOU DIED",
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center
		};
		youDiedLabel.AddThemeFontOverride("font", bigFont);
		youDiedLabel.AddThemeFontSizeOverride("font_size", 46);
		youDiedLabel.AddThemeColorOverride("font_color", DungeonVisuals.CrimsonBright);
		DungeonVisuals.ApplyPixelOutline(youDiedLabel, 4, new Color(0.08f, 0f, 0f, 1f), 3, 3);
		introBox.AddChild(youDiedLabel);

		var subDiedLabel = new Label
		{
			Text = $"FALLEN ON FLOOR {_levelReached}   •   CLICK OR PRESS ANY KEY",
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center
		};
		if (titleFont != null) subDiedLabel.AddThemeFontOverride("font", titleFont);
		subDiedLabel.AddThemeFontSizeOverride("font_size", 20);
		subDiedLabel.AddThemeColorOverride("font_color", DungeonVisuals.MutedSlate);
		DungeonVisuals.ApplyPixelOutline(subDiedLabel, 1);
		introBox.AddChild(subDiedLabel);

		var fadeIn = introBox.CreateTween();
		fadeIn.SetPauseMode(Tween.TweenPauseMode.Process);
		fadeIn.TweenProperty(introBox, "modulate:a", 1.0f, 0.55f);

		await ToSignal(GetTree().CreateTimer(0.65, true), "timeout");
		if (!GodotObject.IsInstanceValid(this)) return;
		_introCanSkip = true;

		float waited = 0.0f;
		while (waited < 2.0f && !_introSkipped && GodotObject.IsInstanceValid(this))
		{
			await ToSignal(GetTree().CreateTimer(0.1, true), "timeout");
			waited += 0.1f;
		}
		if (!GodotObject.IsInstanceValid(this)) return;
		_introCanSkip = false;

		// 2. Transition to Stats screen
		if (GodotObject.IsInstanceValid(introBox))
		{
			introBox.QueueFree();
		}
		
		blackOverlay.Color = new Color(0.025f, 0.02f, 0.03f, 0.90f);

		// Left Side: Hero Pedestal & Crimson Halo
		Vector2 heroPos = new Vector2(viewSize.X * 0.23f, viewSize.Y * 0.50f);
		var heroPedestal = new Panel
		{
			Position = heroPos - new Vector2(81, 93),
			Size = new Vector2(162, 186),
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		heroPedestal.AddThemeStyleboxOverride("panel", DungeonVisuals.CreatePanelStyle(
			DungeonVisuals.ObsidianDeep, new Color(0.75f, 0.20f, 0.25f, 0.75f), 2, 5, 10));
		DungeonVisuals.AttachFrameAccent(heroPedestal, DungeonVisuals.Crimson, 5);
		rootControl.AddChild(heroPedestal);

		var crimsonGlow = new TextureRect
		{
			Texture = GameResourceCache.GetRadialGlowTexture(
				new Color(0.95f, 0.18f, 0.22f, 0.35f),
				new Color(0.6f, 0.08f, 0.12f, 0.08f),
				0.5f, 54, new Vector2(0.9f, 0.9f)),
			Position = heroPos - new Vector2(54, 54),
			Size = new Vector2(108, 108),
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		rootControl.AddChild(crimsonGlow);

		// Opaque dark obsidian backdrop behind the summary layout so faded world HUD does not bleed through
		var statsBackdrop = new ColorRect
		{
			Color = new Color(0.025f, 0.015f, 0.025f, 0.94f),
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		statsBackdrop.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		rootControl.AddChild(statsBackdrop);
		rootControl.MoveChild(statsBackdrop, 0);

		// Stone pedestal shelf under the hero sprite
		var plinth = new ColorRect
		{
			Color = new Color(0.18f, 0.08f, 0.10f, 0.95f),
			Position = new Vector2(heroPos.X - 46, heroPos.Y + 46),
			Size = new Vector2(92, 6),
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		rootControl.AddChild(plinth);

		if (_charAnimations != null)
		{
			var heroSprite = new AnimatedSprite2D();
			heroSprite.SpriteFrames = _charAnimations;
			heroSprite.Position = heroPos + new Vector2(0, 4);
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
			Position = new Vector2(viewSize.X * 0.43f, viewSize.Y * 0.12f),
			Size = new Vector2(viewSize.X * 0.51f, viewSize.Y * 0.62f)
		};
		statsCard.AddThemeStyleboxOverride("panel", DungeonVisuals.CreatePanelStyle(
			DungeonVisuals.ObsidianPanel, DungeonVisuals.Crimson.Darkened(0.25f), 2, 5, 10));
		DungeonVisuals.AttachFrameAccent(statsCard, DungeonVisuals.Crimson, 6);
		rootControl.AddChild(statsCard);

		var statsVBox = new VBoxContainer();
		statsVBox.Position = new Vector2(26, 20);
		statsVBox.Size = new Vector2(statsCard.Size.X - 52, statsCard.Size.Y - 40);
		statsVBox.AddThemeConstantOverride("separation", 10);
		statsCard.AddChild(statsVBox);

		var statsTitle = new Label();
		statsTitle.Text = "EXPEDITION FALLEN";
		statsTitle.VerticalAlignment = VerticalAlignment.Center;
		statsTitle.AddThemeFontOverride("font", titleFont);
		statsTitle.AddThemeFontSizeOverride("font_size", 42);
		statsTitle.AddThemeColorOverride("font_color", DungeonVisuals.CrimsonBright);
		DungeonVisuals.ApplyPixelOutline(statsTitle, 3);
		statsVBox.AddChild(statsTitle);

		DungeonVisuals.AddDivider(statsVBox, new Color(DungeonVisuals.Crimson.R, DungeonVisuals.Crimson.G, DungeonVisuals.Crimson.B, 0.55f), 2.0f);

		int mins = (int)(_timePlayed / 60);
		int secs = (int)(_timePlayed % 60);
		string timeStr = $"{mins:D2}:{secs:D2}";
		string weaponsStr = _weapons.Count > 0 ? string.Join(", ", _weapons).ToUpper() : "NONE";

		AddStatRow("HERO", _charName.ToUpper(), DungeonVisuals.Parchment, titleFont, statsVBox);
		AddStatRow("FLOOR REACHED", _levelReached.ToString(), DungeonVisuals.BrassGold, titleFont, statsVBox);
		AddStatRow("TIME SURVIVED", timeStr, DungeonVisuals.Parchment, titleFont, statsVBox);
		AddStatRow("GOLD COLLECTED", _coins.ToString(), DungeonVisuals.BrassGold, titleFont, statsVBox);
		AddStatRow("ARSENAL", weaponsStr, DungeonVisuals.ArcaneCyan, titleFont, statsVBox);

		var localPlayer = NetworkManager.GetLocalPlayer(GetTree());
		int relicCount = localPlayer?.GetRelicIdsList().Count ?? 0;
		AddStatRow("RELICS CLAIMED", relicCount.ToString(), DungeonVisuals.Emerald, titleFont, statsVBox);

		// Back Button aligned to Stats Card
		var backBtn = new Button();
		backBtn.Text = "RETURN TO MAIN MENU";
		backBtn.CustomMinimumSize = new Vector2(statsCard.Size.X, 48);
		backBtn.Size = new Vector2(statsCard.Size.X, 48);
		backBtn.Position = new Vector2(statsCard.Position.X, viewSize.Y * 0.78f);
		DungeonVisuals.StyleButton(backBtn, DungeonVisuals.BrassGold, titleFont, 30, 4, DungeonVisuals.ObsidianCard, 24, 9);
		rootControl.AddChild(backBtn);

		backBtn.Pressed += OnBackToMainMenuPressed;

		MainMenu.ConnectButtonSounds(rootControl);
		DungeonVisuals.AnimateModalOpen(statsCard);
	}

	private void AddStatRow(string labelText, string valueText, Color valueColor, Font font, Control parent)
	{
		var row = new HBoxContainer();
		row.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

		var keyLabel = new Label();
		keyLabel.Text = labelText;
		keyLabel.CustomMinimumSize = new Vector2(180, 0);
		keyLabel.VerticalAlignment = VerticalAlignment.Center;
		keyLabel.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
		keyLabel.AddThemeFontOverride("font", font);
		keyLabel.AddThemeFontSizeOverride("font_size", 24);
		keyLabel.AddThemeColorOverride("font_color", DungeonVisuals.MutedSlate);
		row.AddChild(keyLabel);

		var valLabel = new Label();
		valLabel.Text = valueText;
		valLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		valLabel.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
		valLabel.HorizontalAlignment = HorizontalAlignment.Right;
		valLabel.VerticalAlignment = VerticalAlignment.Center;
		valLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		valLabel.AddThemeFontOverride("font", font);
		valLabel.AddThemeFontSizeOverride("font_size", 26);
		valLabel.AddThemeColorOverride("font_color", valueColor);
		DungeonVisuals.ApplyPixelOutline(valLabel, 1);
		row.AddChild(valLabel);

		parent.AddChild(row);
	}

	private void OnBackToMainMenuPressed()
	{
		GetTree().Paused = false;
		Engine.TimeScale = 1.0f;
		if (NetworkManager.Instance != null && NetworkManager.Instance.IsCoopSession)
		{
			NetworkManager.Instance.Disconnect(returnToMenu: false);
		}
		GetTree().ChangeSceneToFile("res://scene/levels/main_menu.tscn");
		QueueFree();
	}
}
