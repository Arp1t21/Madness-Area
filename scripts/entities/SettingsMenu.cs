using Godot;
using System;
using System.Collections.Generic;

public partial class SettingsMenu : CanvasLayer
{
	private TabContainer _tabContainer;
	private List<Button> _tabButtons = new List<Button>();
	private Label _descriptionTitle;
	private Label _descriptionText;

	// Display settings UI
	private HSlider _brightnessSlider;
	private Label _brightnessValueLabel;
	private OptionButton _fpsOption;
	private CheckBox _vsyncCheck;

	// Immersion settings UI
	private HSlider _screenshakeSlider;
	private Label _screenshakeValueLabel;

	// Audio settings UI
	private HSlider _masterSlider;
	private Label _masterValueLabel;
	private HSlider _musicSlider;
	private Label _musicValueLabel;
	private HSlider _sfxSlider;
	private Label _sfxValueLabel;

	// Controls settings UI
	private Dictionary<string, Button> _bindButtons = new Dictionary<string, Button>();
	private Dictionary<string, Key> _pendingKeyBinds = new Dictionary<string, Key>();

	private string _actionToBind = null;
	private bool _isListeningForBind = false;

	public override void _Ready()
	{
		ProcessMode = ProcessModeEnum.Always;
		Layer = 25;
		SettingsManager.ApplyFontQualityFixes();
		_pendingKeyBinds = new Dictionary<string, Key>(SettingsManager.KeyBinds);
		var titleFont = GameResourceCache.GetSilverFont();

		var rootControl = new Control();
		AddChild(rootControl);
		rootControl.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

		var bgOverlay = new ColorRect();
		bgOverlay.Color = new Color(0.025f, 0.028f, 0.042f, 0.94f);
		rootControl.AddChild(bgOverlay);
		bgOverlay.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

		var vig = new ColorRect
		{
			Material = DungeonVisuals.GetVignetteMaterial(0.75f, 0.55f),
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		vig.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		rootControl.AddChild(vig);

		var marginContainer = new MarginContainer();
		marginContainer.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		marginContainer.AddThemeConstantOverride("margin_left", 36);
		marginContainer.AddThemeConstantOverride("margin_right", 36);
		marginContainer.AddThemeConstantOverride("margin_top", 20);
		marginContainer.AddThemeConstantOverride("margin_bottom", 20);
		rootControl.AddChild(marginContainer);

		var frame = new PanelContainer();
		frame.AddThemeStyleboxOverride("panel", DungeonVisuals.CreatePanelStyle(
			DungeonVisuals.ObsidianDeep, DungeonVisuals.BrassDim, 2, 5, 10));
		marginContainer.AddChild(frame);

		var innerMargin = new MarginContainer();
		innerMargin.AddThemeConstantOverride("margin_left", 24);
		innerMargin.AddThemeConstantOverride("margin_right", 24);
		innerMargin.AddThemeConstantOverride("margin_top", 16);
		innerMargin.AddThemeConstantOverride("margin_bottom", 16);
		frame.AddChild(innerMargin);

		var mainVBox = new VBoxContainer();
		mainVBox.AddThemeConstantOverride("separation", 10);
		innerMargin.AddChild(mainVBox);

		var titleLabel = new Label();
		titleLabel.Text = "SETTINGS";
		titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
		titleLabel.VerticalAlignment = VerticalAlignment.Center;
		titleLabel.AddThemeFontOverride("font", titleFont);
		titleLabel.AddThemeFontSizeOverride("font_size", 32);
		titleLabel.AddThemeColorOverride("font_color", DungeonVisuals.BrassGold);
		DungeonVisuals.ApplyPixelOutline(titleLabel, 2);
		mainVBox.AddChild(titleLabel);

		var tabsHBox = new HBoxContainer();
		tabsHBox.Alignment = BoxContainer.AlignmentMode.Center;
		tabsHBox.AddThemeConstantOverride("separation", 18);
		mainVBox.AddChild(tabsHBox);

		string[] tabNames = new string[] { "GENERAL", "GRAPHICS", "IMMERSION", "AUDIO", "CONTROLS" };
		for (int i = 0; i < tabNames.Length; i++)
		{
			int tabIndex = i;
			var btn = new Button();
			btn.Text = tabNames[i];
			btn.Pressed += () => SelectTab(tabIndex);
			tabsHBox.AddChild(btn);
			_tabButtons.Add(btn);
		}

		DungeonVisuals.AddDivider(mainVBox, new Color(DungeonVisuals.BrassDim.R, DungeonVisuals.BrassDim.G, DungeonVisuals.BrassDim.B, 0.7f), 1.5f);

		var splitHBox = new HBoxContainer();
		splitHBox.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		splitHBox.AddThemeConstantOverride("separation", 18);
		mainVBox.AddChild(splitHBox);

		_tabContainer = new TabContainer();
		_tabContainer.TabsVisible = false;
		_tabContainer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_tabContainer.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		_tabContainer.AddThemeStyleboxOverride("panel", DungeonVisuals.CreatePanelStyle(
			DungeonVisuals.ObsidianPanel, DungeonVisuals.IronBorder, 1, 4, 2));
		splitHBox.AddChild(_tabContainer);

		var descPanel = new PanelContainer
		{
			CustomMinimumSize = new Vector2(280, 0)
		};
		descPanel.AddThemeStyleboxOverride("panel", DungeonVisuals.CreatePanelStyle(
			DungeonVisuals.ObsidianPanel, DungeonVisuals.BrassDim, 1, 4, 3));
		DungeonVisuals.AttachFrameAccent(descPanel, DungeonVisuals.BrassDim, 4);
		splitHBox.AddChild(descPanel);

		var descMargin = new MarginContainer();
		descMargin.AddThemeConstantOverride("margin_left", 16);
		descMargin.AddThemeConstantOverride("margin_right", 16);
		descMargin.AddThemeConstantOverride("margin_top", 12);
		descMargin.AddThemeConstantOverride("margin_bottom", 12);
		descPanel.AddChild(descMargin);

		var descVBox = new VBoxContainer();
		descVBox.AddThemeConstantOverride("separation", 10);
		descMargin.AddChild(descVBox);

		_descriptionTitle = new Label();
		_descriptionTitle.Text = "GENERAL";
		_descriptionTitle.VerticalAlignment = VerticalAlignment.Center;
		_descriptionTitle.AddThemeFontOverride("font", titleFont);
		_descriptionTitle.AddThemeFontSizeOverride("font_size", 22);
		_descriptionTitle.AddThemeColorOverride("font_color", DungeonVisuals.BrassGold);
		DungeonVisuals.ApplyPixelOutline(_descriptionTitle, 1);
		descVBox.AddChild(_descriptionTitle);

		DungeonVisuals.AddDivider(descVBox, new Color(DungeonVisuals.BrassDim.R, DungeonVisuals.BrassDim.G, DungeonVisuals.BrassDim.B, 0.5f), 1.0f);

		_descriptionText = new Label();
		_descriptionText.Text = "";
		_descriptionText.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_descriptionText.AddThemeFontOverride("font", titleFont);
		_descriptionText.AddThemeFontSizeOverride("font_size", 16);
		_descriptionText.AddThemeColorOverride("font_color", DungeonVisuals.Parchment);
		_descriptionText.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		descVBox.AddChild(_descriptionText);

		CreateGeneralTab(titleFont);
		CreateGraphicsTab(titleFont);
		CreateImmersionTab(titleFont);
		CreateAudioTab(titleFont);
		CreateControlsTab(titleFont);

		var footerHBox = new HBoxContainer();
		footerHBox.Alignment = BoxContainer.AlignmentMode.End;
		footerHBox.AddThemeConstantOverride("separation", 14);
		mainVBox.AddChild(footerHBox);

		var cancelBtn = CreateFooterButton("CANCEL [ESC]", titleFont, DungeonVisuals.IronBorder.Lightened(0.35f));
		cancelBtn.Pressed += OnCancelPressed;
		footerHBox.AddChild(cancelBtn);

		var backBtn = CreateFooterButton("SAVE & BACK", titleFont, DungeonVisuals.BrassGold);
		backBtn.Pressed += OnSaveAndBackPressed;
		footerHBox.AddChild(backBtn);

		DungeonVisuals.AttachFrameAccent(frame, DungeonVisuals.BrassGold, 6);

		SelectTab(0);
		LoadCurrentSettingsIntoUI();
		MainMenu.ConnectButtonSounds(this);
		DungeonVisuals.AnimateModalOpen(frame, bgOverlay);
	}

	private Button CreateFooterButton(string text, Font font, Color accent)
	{
		var btn = new Button();
		btn.Text = text;
		btn.CustomMinimumSize = new Vector2(130, 30);
		DungeonVisuals.StyleButton(btn, accent, font, 18, 3, DungeonVisuals.ObsidianCard, 12, 4);
		return btn;
	}

	private void SelectTab(int index)
	{
		_tabContainer.CurrentTab = index;
		var titleFont = GameResourceCache.GetSilverFont();

		for (int i = 0; i < _tabButtons.Count; i++)
		{
			if (i == index)
			{
				DungeonVisuals.StyleButton(_tabButtons[i], DungeonVisuals.BrassGold, titleFont, 18, 3, DungeonVisuals.ObsidianElevated, 12, 4);
			}
			else
			{
				DungeonVisuals.StyleButton(_tabButtons[i], DungeonVisuals.IronBorder, titleFont, 16, 3, DungeonVisuals.ObsidianDeep, 10, 4);
			}
		}

		UpdateDescription(index);
	}

	private void UpdateDescription(int index)
	{
		switch (index)
		{
			case 0:
				_descriptionTitle.Text = "GENERAL";
				_descriptionText.Text = "Reset all settings to their default values.\n\nUse SAVE & BACK to apply and store changes, or press ESC to discard unsaved edits.";
				break;
			case 1:
				_descriptionTitle.Text = "GRAPHICS";
				_descriptionText.Text = "Configure visual quality and performance options.\n\nAdjust ambient brightness (1.00 = default darkness, up to 2.00 = brighter cave), set FPS limits, and toggle V-Sync.";
				break;
			case 2:
				_descriptionTitle.Text = "IMMERSION";
				_descriptionText.Text = "Adjust variables that control game feel and atmosphere.\n\nEnable or disable screen shake effect intensity on receiving damage.";
				break;
			case 3:
				_descriptionTitle.Text = "AUDIO";
				_descriptionText.Text = "Configure volume levels for music, sound effects, and UI.\n\nSFX uses cavern reverb; menu and UI sounds bypass reverb on the dedicated UI bus.";
				break;
			case 4:
				_descriptionTitle.Text = "CONTROLS";
				_descriptionText.Text = "Configure keyboard bindings (layout-independent physical keys).\n\nClick an action to bind a new key. Conflicting keys automatically swap. Press ESC to cancel listening.";
				break;
		}
	}

	private void CreateGeneralTab(Font font)
	{
		var scroll = CreateScrollContainer("General");
		var vbox = CreateTabVBox(scroll);

		var resetBtn = new Button();
		resetBtn.Text = "RESET EVERYTHING";
		DungeonVisuals.StyleButton(resetBtn, DungeonVisuals.Crimson, font, 16, 3, new Color(0.18f, 0.05f, 0.06f, 0.95f), 10, 3);
		resetBtn.Pressed += OnResetSettingsPressed;
		vbox.AddChild(CreateOptionRow("Reset Settings", resetBtn, font));

		var spacer = new Control { CustomMinimumSize = new Vector2(0, 8) };
		vbox.AddChild(spacer);

		var refHeader = CreateLabel("QUICK EXPEDITION REFERENCE", font, 15);
		refHeader.AddThemeColorOverride("font_color", DungeonVisuals.BrassGold);
		DungeonVisuals.ApplyPixelOutline(refHeader, 1);
		vbox.AddChild(refHeader);

		var refPanel = new PanelContainer();
		refPanel.AddThemeStyleboxOverride("panel", DungeonVisuals.CreatePanelStyle(
			DungeonVisuals.ObsidianDeep, DungeonVisuals.IronBorder, 1, 4, 2));
		var refMargin = new MarginContainer();
		refMargin.AddThemeConstantOverride("margin_left", 12);
		refMargin.AddThemeConstantOverride("margin_right", 12);
		refMargin.AddThemeConstantOverride("margin_top", 10);
		refMargin.AddThemeConstantOverride("margin_bottom", 10);
		var refVBox = new VBoxContainer();
		refVBox.AddThemeConstantOverride("separation", 6);
		refMargin.AddChild(refVBox);
		refPanel.AddChild(refMargin);
		vbox.AddChild(refPanel);

		string[][] tips =
		{
			new[] { "Combat & Aim", "WASD to move, Left-Click to attack toward cursor." },
			new[] { "Arsenal Swap", "Keys 1-3 or Mouse Wheel to switch weapons cleanly." },
			new[] { "Abilities", "[Q] Fireball  |  [V] Shield & Dash (upgrade with +)." },
			new[] { "Progression", "[E] Interact with Supply Altar, Chests, Levers & Stairs." },
			new[] { "Checkpoints", "Floor state saves automatically when descending to a new floor." },
			new[] { "Milestones", "Every 5th floor offers a FREE Relic; Floor 21+ awakens Madness." }
		};
		foreach (var tip in tips)
		{
			var row = new HBoxContainer();
			row.AddThemeConstantOverride("separation", 10);
			var keyLbl = CreateLabel(tip[0], font, 13);
			keyLbl.CustomMinimumSize = new Vector2(120, 0);
			keyLbl.AddThemeColorOverride("font_color", DungeonVisuals.ArcaneCyan);
			var valLbl = CreateLabel(tip[1], font, 12);
			valLbl.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
			valLbl.AutowrapMode = TextServer.AutowrapMode.WordSmart;
			valLbl.AddThemeColorOverride("font_color", DungeonVisuals.Parchment);
			row.AddChild(keyLbl);
			row.AddChild(valLbl);
			refVBox.AddChild(row);
		}
	}

	private void CreateGraphicsTab(Font font)
	{
		var scroll = CreateScrollContainer("Graphics");
		var vbox = CreateTabVBox(scroll);

		var brightnessHBox = new HBoxContainer();
		brightnessHBox.AddThemeConstantOverride("separation", 5);
		brightnessHBox.CustomMinimumSize = new Vector2(160, 20);

		_brightnessSlider = new HSlider();
		_brightnessSlider.MinValue = 1.0f;
		_brightnessSlider.MaxValue = 2.0f;
		_brightnessSlider.Step = 0.05f;
		_brightnessSlider.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_brightnessSlider.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
		_brightnessSlider.ValueChanged += (val) => _brightnessValueLabel.Text = string.Format("{0:0.00}", val);
		DungeonVisuals.StyleSlider(_brightnessSlider, DungeonVisuals.BrassGold);
		brightnessHBox.AddChild(_brightnessSlider);

		_brightnessValueLabel = CreateLabel("1.00", font, 12);
		_brightnessValueLabel.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
		brightnessHBox.AddChild(_brightnessValueLabel);

		vbox.AddChild(CreateOptionRow("Brightness", brightnessHBox, font));

		_fpsOption = new OptionButton();
		_fpsOption.AddItem("Unlimited", 0);
		_fpsOption.AddItem("30 FPS", 30);
		_fpsOption.AddItem("60 FPS", 60);
		_fpsOption.AddItem("120 FPS", 120);
		_fpsOption.AddItem("144 FPS", 144);
		_fpsOption.CustomMinimumSize = new Vector2(104, 24);
		DungeonVisuals.StyleButton(_fpsOption, DungeonVisuals.BrassGold, font, 16, 3, DungeonVisuals.ObsidianDeep, 10, 2);
		vbox.AddChild(CreateOptionRow("FPS Limit", _fpsOption, font));

		_vsyncCheck = new CheckBox();
		_vsyncCheck.Text = "Enabled";
		_vsyncCheck.CustomMinimumSize = new Vector2(104, 24);
		DungeonVisuals.StyleButton(_vsyncCheck, DungeonVisuals.ArcaneCyan, font, 16, 3, DungeonVisuals.ObsidianDeep, 10, 2);
		vbox.AddChild(CreateOptionRow("V-Sync", _vsyncCheck, font));
	}

	private void CreateImmersionTab(Font font)
	{
		var scroll = CreateScrollContainer("Immersion");
		var vbox = CreateTabVBox(scroll);

		var shakeHBox = new HBoxContainer();
		shakeHBox.AddThemeConstantOverride("separation", 5);
		shakeHBox.CustomMinimumSize = new Vector2(160, 20);

		_screenshakeSlider = new HSlider();
		_screenshakeSlider.MinValue = 0.0f;
		_screenshakeSlider.MaxValue = 2.0f;
		_screenshakeSlider.Step = 0.1f;
		_screenshakeSlider.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_screenshakeSlider.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
		_screenshakeSlider.ValueChanged += (val) => _screenshakeValueLabel.Text = string.Format("{0:0.0}", val);
		DungeonVisuals.StyleSlider(_screenshakeSlider, DungeonVisuals.CrimsonBright);
		shakeHBox.AddChild(_screenshakeSlider);

		_screenshakeValueLabel = CreateLabel("1.0", font, 12);
		_screenshakeValueLabel.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
		shakeHBox.AddChild(_screenshakeValueLabel);

		vbox.AddChild(CreateOptionRow("Screen Shake", shakeHBox, font));
	}

	private void CreateAudioTab(Font font)
	{
		var scroll = CreateScrollContainer("Audio");
		var vbox = CreateTabVBox(scroll);

		var mHBox = new HBoxContainer();
		mHBox.AddThemeConstantOverride("separation", 5);
		mHBox.CustomMinimumSize = new Vector2(160, 20);
		_masterSlider = CreateVolumeSlider();
		_masterSlider.ValueChanged += (val) => _masterValueLabel.Text = $"{(int)val}%";
		mHBox.AddChild(_masterSlider);
		_masterValueLabel = CreateLabel("80%", font, 12);
		mHBox.AddChild(_masterValueLabel);
		vbox.AddChild(CreateOptionRow("Master Volume", mHBox, font));

		var musHBox = new HBoxContainer();
		musHBox.AddThemeConstantOverride("separation", 5);
		musHBox.CustomMinimumSize = new Vector2(160, 20);
		_musicSlider = CreateVolumeSlider();
		_musicSlider.ValueChanged += (val) => _musicValueLabel.Text = $"{(int)val}%";
		musHBox.AddChild(_musicSlider);
		_musicValueLabel = CreateLabel("70%", font, 12);
		musHBox.AddChild(_musicValueLabel);
		vbox.AddChild(CreateOptionRow("Music Volume", musHBox, font));

		var sfxHBox = new HBoxContainer();
		sfxHBox.AddThemeConstantOverride("separation", 5);
		sfxHBox.CustomMinimumSize = new Vector2(160, 20);
		_sfxSlider = CreateVolumeSlider();
		_sfxSlider.ValueChanged += (val) => _sfxValueLabel.Text = $"{(int)val}%";
		sfxHBox.AddChild(_sfxSlider);
		_sfxValueLabel = CreateLabel("70%", font, 12);
		sfxHBox.AddChild(_sfxValueLabel);
		vbox.AddChild(CreateOptionRow("SFX & UI Volume", sfxHBox, font));
	}

	private void CreateControlsTab(Font font)
	{
		var scroll = CreateScrollContainer("Controls");
		var vbox = CreateTabVBox(scroll);

		var keybindsHeader = new MarginContainer();
		keybindsHeader.AddThemeConstantOverride("margin_top", 4);
		keybindsHeader.AddThemeConstantOverride("margin_bottom", 5);
		var headerLabel = CreateLabel("Key Bindings (Esc to cancel)", font, 14);
		headerLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.75f, 0.15f));
		keybindsHeader.AddChild(headerLabel);
		vbox.AddChild(keybindsHeader);

		foreach (var action in _pendingKeyBinds.Keys)
		{
			var bindBtn = new Button();
			bindBtn.Text = _pendingKeyBinds[action].ToString();
			bindBtn.CustomMinimumSize = new Vector2(105, 22);
			DungeonVisuals.StyleButton(bindBtn, DungeonVisuals.BrassGold, font, 15, 3, DungeonVisuals.ObsidianDeep, 8, 2);

			string actionKey = action;
			bindBtn.Pressed += () => StartListeningForBind(actionKey);
			vbox.AddChild(CreateOptionRow(FormatActionName(action), bindBtn, font));

			_bindButtons[action] = bindBtn;
		}
	}

	private ScrollContainer CreateScrollContainer(string name)
	{
		var scroll = new ScrollContainer();
		scroll.Name = name;
		scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
		scroll.VerticalScrollMode = ScrollContainer.ScrollMode.Auto;
		_tabContainer.AddChild(scroll);
		return scroll;
	}

	private VBoxContainer CreateTabVBox(ScrollContainer scroll)
	{
		var tabMargin = new MarginContainer();
		tabMargin.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		tabMargin.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		tabMargin.AddThemeConstantOverride("margin_left", 10);
		tabMargin.AddThemeConstantOverride("margin_right", 10);
		tabMargin.AddThemeConstantOverride("margin_top", 10);
		tabMargin.AddThemeConstantOverride("margin_bottom", 10);
		scroll.AddChild(tabMargin);

		var vbox = new VBoxContainer();
		vbox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		vbox.AddThemeConstantOverride("separation", 6);
		tabMargin.AddChild(vbox);
		return vbox;
	}

	private VBoxContainer CreateOptionRow(string optionLabel, Control control, Font font)
	{
		var rowVBox = new VBoxContainer();
		rowVBox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		rowVBox.AddThemeConstantOverride("separation", 2);

		var rowHBox = new HBoxContainer();
		rowHBox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		rowVBox.AddChild(rowHBox);

		var label = CreateLabel(optionLabel, font, 14);
		label.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
		rowHBox.AddChild(label);

		var spacer = new Control();
		spacer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		rowHBox.AddChild(spacer);

		control.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
		rowHBox.AddChild(control);

		var separator = new ColorRect();
		separator.Color = new Color(1.0f, 1.0f, 1.0f, 0.12f);
		separator.CustomMinimumSize = new Vector2(0, 1);
		rowVBox.AddChild(separator);

		return rowVBox;
	}

	private string FormatActionName(string action)
	{
		if (action == "ability_1") return "ABILITY 1 (FIREBALL)";
		if (action == "ability_2") return "ABILITY 2 (SHIELD / DASH)";
		return action.Replace("_", " ").ToUpper();
	}

	private HSlider CreateVolumeSlider()
	{
		var slider = new HSlider();
		slider.MinValue = 0;
		slider.MaxValue = 100;
		slider.Step = 1;
		slider.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		slider.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
		DungeonVisuals.StyleSlider(slider, DungeonVisuals.ArcaneCyan);
		return slider;
	}

	private Label CreateLabel(string text, Font font, int fontSize)
	{
		var label = new Label();
		label.Text = text;
		label.VerticalAlignment = VerticalAlignment.Center;
		label.AddThemeFontOverride("font", font);
		label.AddThemeFontSizeOverride("font_size", fontSize + 4);
		label.AddThemeConstantOverride("letter_spacing", 1);
		return label;
	}

	private void LoadCurrentSettingsIntoUI()
	{
		_brightnessSlider.Value = Mathf.Clamp(SettingsManager.Brightness, 1.0f, 2.0f);
		_brightnessValueLabel.Text = string.Format("{0:0.00}", _brightnessSlider.Value);

		for (int i = 0; i < _fpsOption.ItemCount; i++)
		{
			if (_fpsOption.GetItemId(i) == SettingsManager.MaxFps)
			{
				_fpsOption.Select(i);
				break;
			}
		}

		_vsyncCheck.ButtonPressed = SettingsManager.VSync;

		_screenshakeSlider.Value = SettingsManager.ScreenShake;
		_screenshakeValueLabel.Text = string.Format("{0:0.0}", SettingsManager.ScreenShake);

		_masterSlider.Value = SettingsManager.MasterVolume;
		_masterValueLabel.Text = $"{(int)SettingsManager.MasterVolume}%";

		_musicSlider.Value = SettingsManager.MusicVolume;
		_musicValueLabel.Text = $"{(int)SettingsManager.MusicVolume}%";

		_sfxSlider.Value = SettingsManager.SFXVolume;
		_sfxValueLabel.Text = $"{(int)SettingsManager.SFXVolume}%";

		UpdateBindButtonLabels();
	}

	private void UpdateBindButtonLabels()
	{
		var font = GameResourceCache.GetSilverFont();
		foreach (var pair in _bindButtons)
		{
			if (_pendingKeyBinds.TryGetValue(pair.Key, out Key key))
			{
				pair.Value.Text = key.ToString();
				DungeonVisuals.StyleButton(pair.Value, DungeonVisuals.BrassGold, font, 15, 3, DungeonVisuals.ObsidianDeep, 8, 2);
			}
		}
	}

	private void StartListeningForBind(string action)
	{
		if (_isListeningForBind) return;

		_actionToBind = action;
		_isListeningForBind = true;
		var btn = _bindButtons[action];
		btn.Text = "PRESS KEY...";
		DungeonVisuals.StyleButton(btn, DungeonVisuals.ArcaneCyan, GameResourceCache.GetSilverFont(), 15, 3, DungeonVisuals.ObsidianElevated, 8, 2);
	}

	public override void _Input(InputEvent @event)
	{
		if (!_isListeningForBind || _actionToBind == null)
		{
			if (@event.IsActionPressed("ui_cancel"))
			{
				OnCancelPressed();
				GetViewport().SetInputAsHandled();
			}
			return;
		}

		if (@event is InputEventKey keyEvent && keyEvent.Pressed && !keyEvent.Echo)
		{
			Key capturedKey = keyEvent.PhysicalKeycode != Key.None ? keyEvent.PhysicalKeycode : keyEvent.Keycode;
			if (capturedKey == Key.Escape || keyEvent.Keycode == Key.Escape)
			{
				_isListeningForBind = false;
				_actionToBind = null;
				UpdateBindButtonLabels();
				GetViewport().SetInputAsHandled();
				return;
			}

			if (capturedKey != Key.None)
			{
				GameLogic.AssignKeyBind(_pendingKeyBinds, _actionToBind, capturedKey, Key.None);
			}
			_isListeningForBind = false;
			_actionToBind = null;
			UpdateBindButtonLabels();
			GetViewport().SetInputAsHandled();
		}
	}

	private void OnResetSettingsPressed()
	{
		_brightnessSlider.Value = 1.0f;
		_fpsOption.Select(2); // 60 FPS
		_vsyncCheck.ButtonPressed = true;
		_screenshakeSlider.Value = 1.0f;
		_masterSlider.Value = 80.0f;
		_musicSlider.Value = 70.0f;
		_sfxSlider.Value = 70.0f;

		_pendingKeyBinds = SettingsManager.CreateDefaultKeyBinds();
		UpdateBindButtonLabels();
	}

	private void OnCancelPressed()
	{
		QueueFree();
	}

	private void OnSaveAndBackPressed()
	{
		SettingsManager.Brightness = (float)_brightnessSlider.Value;

		int selectedIdx = _fpsOption.Selected;
		SettingsManager.MaxFps = _fpsOption.GetItemId(selectedIdx);
		SettingsManager.VSync = _vsyncCheck.ButtonPressed;

		SettingsManager.ScreenShake = (float)_screenshakeSlider.Value;

		SettingsManager.MasterVolume = (float)_masterSlider.Value;
		SettingsManager.MusicVolume = (float)_musicSlider.Value;
		SettingsManager.SFXVolume = (float)_sfxSlider.Value;

		SettingsManager.KeyBinds = new Dictionary<string, Key>(_pendingKeyBinds);

		SettingsManager.SaveSettings();
		SettingsManager.ApplySettings();

		var level = GetTree().GetFirstNodeInGroup("Level") as Level;
		if (level != null)
		{
			var modulate = level.GetNodeOrNull<CanvasModulate>("CanvasModulate");
			if (modulate != null)
			{
				float ambient = GameLogic.CalculateAmbientLight(SettingsManager.Brightness);
				modulate.Color = new Color(ambient, ambient, ambient, 1.0f);
			}
		}

		QueueFree();
	}
}
