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
		rootControl.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

		var bgOverlay = new ColorRect();
		bgOverlay.Color = new Color(0.02f, 0.022f, 0.035f, 0.78f);
		rootControl.AddChild(bgOverlay);
		bgOverlay.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

		var vig = new ColorRect
		{
			Material = DungeonVisuals.GetVignetteMaterial(0.8f, 0.58f),
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		vig.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		rootControl.AddChild(vig);

		var center = new CenterContainer();
		center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		rootControl.AddChild(center);

		var frame = new PanelContainer
		{
			CustomMinimumSize = new Vector2(210, 185)
		};
		frame.AddThemeStyleboxOverride("panel", DungeonVisuals.CreatePanelStyle(
			DungeonVisuals.ObsidianDeep, DungeonVisuals.BrassDim, 1, 3, 6));
		center.AddChild(frame);

		var margin = new MarginContainer();
		margin.AddThemeConstantOverride("margin_left", 18);
		margin.AddThemeConstantOverride("margin_right", 18);
		margin.AddThemeConstantOverride("margin_top", 14);
		margin.AddThemeConstantOverride("margin_bottom", 14);
		frame.AddChild(margin);

		var vbox = new VBoxContainer();
		vbox.Alignment = BoxContainer.AlignmentMode.Center;
		vbox.AddThemeConstantOverride("separation", 8);
		margin.AddChild(vbox);

		var titleFont = GD.Load<Font>("res://assets/fonts/silver/Silver.ttf");

		var title = new Label();
		title.Text = "PAUSED";
		title.HorizontalAlignment = HorizontalAlignment.Center;
		title.AddThemeFontOverride("font", titleFont);
		title.AddThemeFontSizeOverride("font_size", 38);
		title.AddThemeColorOverride("font_color", DungeonVisuals.BrassGold);
		DungeonVisuals.ApplyPixelOutline(title, 2);
		vbox.AddChild(title);

		var level = GetTree().GetFirstNodeInGroup("Level") as Level;
		var player = NetworkManager.GetLocalPlayer(GetTree());
		string subText = level != null
			? $"FLOOR {level.CurrentLevelIndex}  •  GOLD {player?.CoinCount ?? 0}"
			: "RUN IN PROGRESS";
		var subLabel = new Label
		{
			Text = subText,
			HorizontalAlignment = HorizontalAlignment.Center
		};
		subLabel.AddThemeFontOverride("font", titleFont);
		subLabel.AddThemeFontSizeOverride("font_size", 12);
		subLabel.AddThemeColorOverride("font_color", DungeonVisuals.MutedSlate);
		vbox.AddChild(subLabel);

		DungeonVisuals.AddDivider(vbox, new Color(DungeonVisuals.BrassDim.R, DungeonVisuals.BrassDim.G, DungeonVisuals.BrassDim.B, 0.7f), 1.0f);

		var btnContinue = CreateMenuButton("CONTINUE", titleFont, vbox, DungeonVisuals.Emerald);
		btnContinue.Pressed += OnContinuePressed;

		var btnSettings = CreateMenuButton("SETTINGS", titleFont, vbox, DungeonVisuals.BrassGold);
		btnSettings.Pressed += OnSettingsPressed;

		bool isCoop = NetworkManager.Instance != null && NetworkManager.Instance.IsCoopSession;
		var btnSaveLeave = CreateMenuButton(isCoop ? "LEAVE SESSION" : "SAVE AND LEAVE", titleFont, vbox, DungeonVisuals.Crimson);
		btnSaveLeave.Pressed += OnSaveLeavePressed;

		DungeonVisuals.AttachFrameAccent(frame, DungeonVisuals.BrassGold, 4);
		MainMenu.ConnectButtonSounds(this);
	}

	private Button CreateMenuButton(string text, Font font, Control parent, Color accent)
	{
		var btn = new Button();
		btn.Text = text;
		btn.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
		btn.CustomMinimumSize = new Vector2(156, 26);
		DungeonVisuals.StyleButton(btn, accent, font, 19, 2, DungeonVisuals.ObsidianCard, 10, 4);
		parent.AddChild(btn);
		return btn;
	}

	private void OnContinuePressed()
	{
		bool isCoop = NetworkManager.Instance != null && NetworkManager.Instance.IsCoopSession;
		if (!isCoop)
		{
			GetTree().Paused = false;
		}
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
		bool isCoop = NetworkManager.Instance != null && NetworkManager.Instance.IsCoopSession;
		if (isCoop)
		{
			GetTree().Paused = false;
			NetworkManager.Instance.Disconnect(returnToMenu: true);
			QueueFree();
			return;
		}

		// Keep the floor-entry checkpoint intact so uncommitted mid-floor gains
		// (coins, skill points, ability upgrades, shop items) do not carry over without descending.
		if (SaveManager.CurrentSave != null)
		{
			SaveManager.SaveGame(SaveManager.CurrentSave);
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
