using Godot;
using System;

public partial class PauseMenu : CanvasLayer
{
	public override void _Ready()
	{
		ProcessMode = ProcessModeEnum.Always;
		
		var rootControl = new Control();
		AddChild(rootControl);
		rootControl.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		rootControl.Size = GetViewport().GetVisibleRect().Size;

		// Semi-transparent overlay
		var bgOverlay = new ColorRect();
		bgOverlay.Color = new Color(0.0f, 0.0f, 0.0f, 0.65f);
		rootControl.AddChild(bgOverlay);
		bgOverlay.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

		// Container for buttons
		var margin = new MarginContainer();
		margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		rootControl.AddChild(margin);

		var vbox = new VBoxContainer();
		vbox.Alignment = BoxContainer.AlignmentMode.Center;
		vbox.AddThemeConstantOverride("separation", 15);
		margin.AddChild(vbox);

		var titleFont = GD.Load<Font>("res://assets/fonts/silver/Silver.ttf");

		// "PAUSED" title
		var title = new Label();
		title.Text = "PAUSED";
		title.HorizontalAlignment = HorizontalAlignment.Center;
		title.AddThemeFontOverride("font", titleFont);
		title.AddThemeFontSizeOverride("font_size", 28);
		vbox.AddChild(title);

		var btnContinue = CreateMenuButton("CONTINUE", titleFont, vbox);
		btnContinue.Pressed += OnContinuePressed;

		var btnSettings = CreateMenuButton("SETTINGS", titleFont, vbox);
		btnSettings.Pressed += OnSettingsPressed;

		var btnSaveLeave = CreateMenuButton("SAVE AND LEAVE", titleFont, vbox);
		btnSaveLeave.Pressed += OnSaveLeavePressed;

		MainMenu.ConnectButtonSounds(this);
	}

	private Button CreateMenuButton(string text, Font font, Control parent)
	{
		var btn = new Button();
		btn.Text = text;
		btn.Flat = true;
		btn.AddThemeFontOverride("font", font);
		btn.AddThemeFontSizeOverride("font_size", 14);
		btn.CustomMinimumSize = new Vector2(120, 25);
		parent.AddChild(btn);

		var btnNormal = new StyleBoxEmpty();
		var btnHover = new StyleBoxFlat();
		btnHover.BgColor = new Color(1, 1, 1, 0.08f);
		btn.AddThemeStyleboxOverride("normal", btnNormal);
		btn.AddThemeStyleboxOverride("hover", btnHover);
		btn.AddThemeStyleboxOverride("pressed", btnNormal);
		btn.AddThemeStyleboxOverride("focus", btnNormal);

		return btn;
	}

	private void OnContinuePressed()
	{
		GetTree().Paused = false;
		QueueFree();
	}

	private void OnSettingsPressed()
	{
		var settings = new SettingsMenu();
		AddChild(settings);
	}

	private void OnSaveLeavePressed()
	{
		// Save game
		var level = GetTree().GetFirstNodeInGroup("Level") as Level;
		var player = GetTree().GetFirstNodeInGroup("Player") as Player;
		if (level != null && player != null && SaveManager.CurrentSave != null)
		{
			var currentSave = SaveManager.CurrentSave;
			currentSave.CurrentLevelIndex = level.CurrentLevelIndex;
			currentSave.Coins = player.CoinCount;
			currentSave.CurrentHealth = player.CurrentHealth;
			currentSave.CurrentMana = player.CurrentMana;
			currentSave.SkillPoints = player.SkillPoints;
			currentSave.Ability1Level = player.Ability1Level;
			currentSave.Ability2Level = player.Ability2Level;
			currentSave.PlayedTimeSeconds += level.LevelPlayedTime;
			
			// Save weapons
			currentSave.WeaponResourcePaths.Clear();
			for (int i = 0; i < player.WeaponSlots.Count; i++)
			{
				if (player.WeaponSlots[i].CurrentWeapon != null)
				{
					currentSave.WeaponResourcePaths.Add(player.WeaponSlots[i].CurrentWeapon.ResourcePath);
				}
			}
			SaveManager.SaveGame(currentSave);
		}

		GetTree().Paused = false;
		GetTree().ChangeSceneToFile("res://scene/levels/main_menu.tscn");
		QueueFree();
	}

	public override void _Input(InputEvent @event)
	{
		if (@event.IsActionPressed("ui_cancel"))
		{
			// Resume on Escape
			OnContinuePressed();
			GetViewport().SetInputAsHandled();
		}
	}
}
