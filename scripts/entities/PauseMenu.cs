using Godot;
using System;

public partial class PauseMenu : CanvasLayer
{
	public override void _Ready()
	{
		ProcessMode = ProcessModeEnum.Always;
		Layer = 15;
		SettingsManager.ApplyFontQualityFixes();

		var rootControl = new Control();
		AddChild(rootControl);
		rootControl.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
		rootControl.Size = GetViewport().GetVisibleRect().Size;

		var bgOverlay = new ColorRect();
		bgOverlay.Color = new Color(0.0f, 0.0f, 0.0f, 0.72f);
		rootControl.AddChild(bgOverlay);
		bgOverlay.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

		var margin = new MarginContainer();
		margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		rootControl.AddChild(margin);

		var vbox = new VBoxContainer();
		vbox.Alignment = BoxContainer.AlignmentMode.Center;
		vbox.AddThemeConstantOverride("separation", 24);
		margin.AddChild(vbox);

		var titleFont = GD.Load<Font>("res://assets/fonts/silver/Silver.ttf");

		var title = new Label();
		title.Text = "PAUSED";
		title.HorizontalAlignment = HorizontalAlignment.Center;
		title.AddThemeFontOverride("font", titleFont);
		title.AddThemeFontSizeOverride("font_size", 96);
		title.AddThemeColorOverride("font_color", new Color(1.0f, 0.88f, 0.55f));
		title.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 1));
		title.AddThemeConstantOverride("outline_size", 4);
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
		btn.AddThemeFontSizeOverride("font_size", 48);
		btn.AddThemeColorOverride("font_color", new Color(0.95f, 0.95f, 0.95f));
		btn.AddThemeColorOverride("font_hover_color", new Color(1.0f, 0.9f, 0.6f));
		btn.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 1));
		btn.AddThemeConstantOverride("outline_size", 2);
		btn.CustomMinimumSize = new Vector2(360, 62);
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
		foreach (Node child in GetChildren())
		{
			if (child is SettingsMenu) return;
		}
		var settings = new SettingsMenu();
		settings.Name = "SettingsMenu";
		AddChild(settings);
	}

	private void OnSaveLeavePressed()
	{
		var level = GetTree().GetFirstNodeInGroup("Level") as Level;
		var player = GetTree().GetFirstNodeInGroup("Player") as Player;
		if (level != null && player != null && SaveManager.CurrentSave != null)
		{
			var currentSave = SaveManager.CurrentSave;
			currentSave.CurrentLevelIndex = level.CurrentLevelIndex;
			currentSave.Coins = player.CoinCount;
			currentSave.CurrentHealth = player.CurrentHealth;
			currentSave.CurrentMana = player.CurrentMana;
			currentSave.BonusMaxHealth = player.BonusMaxHealth;
			currentSave.BonusMaxMana = player.BonusMaxMana;
			currentSave.BonusHealthRegen = player.BonusHealthRegen;
			currentSave.BonusManaRegen = player.BonusManaRegen;
			currentSave.BonusMoveSpeed = player.BonusMoveSpeed;
			currentSave.WeaponDamageMultiplier = player.WeaponDamageMultiplier;
			currentSave.SkillPoints = player.SkillPoints;
			currentSave.Ability1Level = player.Ability1Level;
			currentSave.Ability2Level = player.Ability2Level;
			currentSave.PlayedTimeSeconds += level.ConsumePlayedTime();

			currentSave.ShopPurchaseCounts = new System.Collections.Generic.Dictionary<string, int>(player.ShopPurchaseCounts);
			currentSave.WeaponResourcePaths.Clear();
			currentSave.WeaponSlotCustomStats.Clear();
			for (int i = 0; i < player.WeaponSlots.Count; i++)
			{
				var w = player.WeaponSlots[i].CurrentWeapon;
				if (w != null)
				{
					currentSave.WeaponResourcePaths.Add(w.GetEffectiveResourcePath());
					currentSave.WeaponSlotCustomStats.Add(new SavedWeaponStats
					{
						Damage = w.Damage,
						AttackCooldown = w.AttackCooldown,
						MaxAmmo = w.MaxAmmo,
						ReloadTime = w.ReloadTime,
						ManaCost = w.ManaCost,
						KnockbackForce = w.KnockbackForce
					});
				}
				else
				{
					currentSave.WeaponResourcePaths.Add("");
					currentSave.WeaponSlotCustomStats.Add(null);
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
		// Если открыты настройки поверх паузы, пусть Esc обрабатывает SettingsMenu
		foreach (Node child in GetChildren())
		{
			if (child is SettingsMenu && GodotObject.IsInstanceValid(child) && !child.IsQueuedForDeletion())
			{
				return;
			}
		}

		if (@event.IsActionPressed("ui_cancel"))
		{
			OnContinuePressed();
			GetViewport().SetInputAsHandled();
		}
	}
}
