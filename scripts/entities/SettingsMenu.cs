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
	private HSlider _sensitivitySlider;
	private Label _sensitivityValueLabel;
	private Dictionary<string, Button> _bindButtons = new Dictionary<string, Button>();

	// General settings (Subliminal decorative placeholders / Reset functionality)
	private OptionButton _textLangOption;
	private OptionButton _voiceLangOption;
	private CheckBox _subtitlesCheck;

	private string _actionToBind = null;
	private bool _isListeningForBind = false;

	public override void _Ready()
	{
		var titleFont = GD.Load<Font>("res://assets/fonts/silver/Silver.ttf");

		// 1. Create a root Control node
		var rootControl = new Control();
		AddChild(rootControl);
		
		rootControl.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		rootControl.Size = GetViewport().GetVisibleRect().Size;

		// Solid dark background overlay (almost black)
		var bgOverlay = new ColorRect();
		bgOverlay.Color = new Color(0.02f, 0.02f, 0.02f, 1.0f);
		rootControl.AddChild(bgOverlay);
		bgOverlay.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

		// Main Margin Container
		var marginContainer = new MarginContainer();
		marginContainer.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		marginContainer.AddThemeConstantOverride("margin_left", 40);
		marginContainer.AddThemeConstantOverride("margin_right", 40);
		marginContainer.AddThemeConstantOverride("margin_top", 20);
		marginContainer.AddThemeConstantOverride("margin_bottom", 20);
		rootControl.AddChild(marginContainer);

		var mainVBox = new VBoxContainer();
		mainVBox.AddThemeConstantOverride("separation", 12);
		marginContainer.AddChild(mainVBox);

		// Title Label: "SETTINGS"
		var titleLabel = new Label();
		titleLabel.Text = "SETTINGS";
		titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
		titleLabel.AddThemeFontOverride("font", titleFont);
		titleLabel.AddThemeFontSizeOverride("font_size", 32);
		titleLabel.AddThemeColorOverride("font_color", new Color(1.0f, 1.0f, 1.0f));
		mainVBox.AddChild(titleLabel);

		// Horizontal tab buttons (GENERAL, GRAPHICS, IMMERSION, AUDIO, CONTROLS)
		var tabsHBox = new HBoxContainer();
		tabsHBox.Alignment = BoxContainer.AlignmentMode.Center;
		tabsHBox.AddThemeConstantOverride("separation", 35);
		mainVBox.AddChild(tabsHBox);

		string[] tabNames = new string[] { "GENERAL", "GRAPHICS", "IMMERSION", "AUDIO", "CONTROLS" };
		for (int i = 0; i < tabNames.Length; i++)
		{
			int tabIndex = i;
			var btn = new Button();
			btn.Text = tabNames[i];
			btn.Flat = true;
			btn.AddThemeFontOverride("font", titleFont);
			btn.AddThemeFontSizeOverride("font_size", 18);
			btn.Pressed += () => SelectTab(tabIndex);
			tabsHBox.AddChild(btn);
			_tabButtons.Add(btn);
		}

		// Horizontal separator line below tabs
		var horizontalLine = new ColorRect();
		horizontalLine.Color = new Color(1.0f, 1.0f, 1.0f, 0.4f);
		horizontalLine.CustomMinimumSize = new Vector2(0, 1);
		mainVBox.AddChild(horizontalLine);

		// Content layout: Left Panel (Options) | Vertical Line Divider | Right Panel (Description)
		var splitHBox = new HBoxContainer();
		splitHBox.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		splitHBox.AddThemeConstantOverride("separation", 20);
		mainVBox.AddChild(splitHBox);

		// Left Container: TabContainer with hidden tabs
		_tabContainer = new TabContainer();
		_tabContainer.TabsVisible = false;
		_tabContainer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_tabContainer.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		var tabEmptyStyle = new StyleBoxEmpty();
		_tabContainer.AddThemeStyleboxOverride("panel", tabEmptyStyle);
		splitHBox.AddChild(_tabContainer);

		// Vertical divider line in the center
		var verticalLine = new ColorRect();
		verticalLine.Color = new Color(1.0f, 1.0f, 1.0f, 0.4f);
		verticalLine.CustomMinimumSize = new Vector2(1, 0);
		verticalLine.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		splitHBox.AddChild(verticalLine);

		// Right Container: Description panel
		var descMargin = new MarginContainer();
		descMargin.CustomMinimumSize = new Vector2(300, 0);
		descMargin.AddThemeConstantOverride("margin_left", 15);
		descMargin.AddThemeConstantOverride("margin_right", 15);
		descMargin.AddThemeConstantOverride("margin_top", 10);
		splitHBox.AddChild(descMargin);

		var descVBox = new VBoxContainer();
		descVBox.AddThemeConstantOverride("separation", 10);
		descMargin.AddChild(descVBox);

		_descriptionTitle = new Label();
		_descriptionTitle.Text = "GENERAL";
		_descriptionTitle.AddThemeFontOverride("font", titleFont);
		_descriptionTitle.AddThemeFontSizeOverride("font_size", 20);
		_descriptionTitle.AddThemeColorOverride("font_color", new Color(1, 1, 1));
		descVBox.AddChild(_descriptionTitle);

		var descUnderline = new ColorRect();
		descUnderline.Color = new Color(1.0f, 1.0f, 1.0f, 0.3f);
		descUnderline.CustomMinimumSize = new Vector2(0, 1);
		descVBox.AddChild(descUnderline);

		_descriptionText = new Label();
		_descriptionText.Text = "";
		_descriptionText.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_descriptionText.AddThemeFontOverride("font", titleFont);
		_descriptionText.AddThemeFontSizeOverride("font_size", 16);
		_descriptionText.AddThemeColorOverride("font_color", new Color(0.8f, 0.8f, 0.8f));
		_descriptionText.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		descVBox.AddChild(_descriptionText);

		// Create each Tab Page
		CreateGeneralTab(titleFont);
		CreateGraphicsTab(titleFont);
		CreateImmersionTab(titleFont);
		CreateAudioTab(titleFont);
		CreateControlsTab(titleFont);

		// Footer: Back Button (aligned left)
		var footerHBox = new HBoxContainer();
		footerHBox.Alignment = BoxContainer.AlignmentMode.Begin;
		mainVBox.AddChild(footerHBox);

		var backBtn = new Button();
		backBtn.Text = "BACK";
		backBtn.Flat = true;
		backBtn.CustomMinimumSize = new Vector2(100, 25);
		backBtn.AddThemeFontOverride("font", titleFont);
		backBtn.AddThemeFontSizeOverride("font_size", 20);
		backBtn.Pressed += OnBackPressed;
		
		// Style BACK button to look minimal
		var backNormal = new StyleBoxEmpty();
		var backHover = new StyleBoxFlat();
		backHover.BgColor = new Color(1, 1, 1, 0.05f);
		backHover.ContentMarginLeft = backHover.ContentMarginRight = 10;
		backBtn.AddThemeStyleboxOverride("normal", backNormal);
		backBtn.AddThemeStyleboxOverride("hover", backHover);
		backBtn.AddThemeStyleboxOverride("pressed", backNormal);
		backBtn.AddThemeStyleboxOverride("focus", backNormal);
		footerHBox.AddChild(backBtn);

		// Select general tab by default
		SelectTab(0);

		// Load configurations
		LoadCurrentSettingsIntoUI();

		// Play UI click sounds
		MainMenu.ConnectButtonSounds(this);
	}

	private void SelectTab(int index)
	{
		_tabContainer.CurrentTab = index;

		for (int i = 0; i < _tabButtons.Count; i++)
		{
			var btnNormal = new StyleBoxEmpty();
			var btnFocus = new StyleBoxEmpty();
			_tabButtons[i].AddThemeStyleboxOverride("normal", btnNormal);
			_tabButtons[i].AddThemeStyleboxOverride("hover", btnNormal);
			_tabButtons[i].AddThemeStyleboxOverride("pressed", btnNormal);
			_tabButtons[i].AddThemeStyleboxOverride("focus", btnFocus);

			if (i == index)
			{
				_tabButtons[i].AddThemeColorOverride("font_color", new Color(1, 1, 1));
				_tabButtons[i].AddThemeColorOverride("font_hover_color", new Color(1, 1, 1));
				_tabButtons[i].AddThemeColorOverride("font_pressed_color", new Color(1, 1, 1));
				_tabButtons[i].AddThemeFontSizeOverride("font_size", 20);
			}
			else
			{
				_tabButtons[i].AddThemeColorOverride("font_color", new Color(0.45f, 0.45f, 0.45f));
				_tabButtons[i].AddThemeColorOverride("font_hover_color", new Color(0.7f, 0.7f, 0.7f));
				_tabButtons[i].AddThemeColorOverride("font_pressed_color", new Color(0.45f, 0.45f, 0.45f));
				_tabButtons[i].AddThemeFontSizeOverride("font_size", 17);
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
				_descriptionText.Text = "A set of the most common settings players like to adjust.\n\nIf you'd like something added here, reach out and we'll get it in.\n\nChange a setting to see an overview and explanation of what it does!";
				break;
			case 1:
				_descriptionTitle.Text = "GRAPHICS";
				_descriptionText.Text = "Configure visual quality and performance options.\n\nAdjust brightness, set frame rate limits, and toggle vertical synchronization to optimize performance on your system.";
				break;
			case 2:
				_descriptionTitle.Text = "IMMERSION";
				_descriptionText.Text = "Adjust variables that control game feel and atmosphere.\n\nEnable or disable screen shake effect intensity on receiving damage.";
				break;
			case 3:
				_descriptionTitle.Text = "AUDIO";
				_descriptionText.Text = "Configure volume levels for music and sound effects.\n\nAdjust master, music, and SFX volume sliders to your preference. SFX bus includes cavern reverb effects.";
				break;
			case 4:
				_descriptionTitle.Text = "CONTROLS";
				_descriptionText.Text = "Configure mouse sensitivity and keyboard bindings.\n\nClick on any key action to bind a new key. Press ESC to cancel binding.";
				break;
		}
	}

	private void CreateGeneralTab(Font font)
	{
		var scroll = CreateScrollContainer("General");
		var vbox = CreateTabVBox(scroll);

		// Text Language
		_textLangOption = new OptionButton();
		_textLangOption.AddThemeFontOverride("font", font);
		_textLangOption.AddThemeFontSizeOverride("font_size", 16);
		_textLangOption.AddItem("English", 0);
		_textLangOption.AddItem("Russian", 1);
		_textLangOption.CustomMinimumSize = new Vector2(80, 20);
		vbox.AddChild(CreateOptionRow("Text Language", _textLangOption, font));

		// Voice Language
		_voiceLangOption = new OptionButton();
		_voiceLangOption.AddThemeFontOverride("font", font);
		_voiceLangOption.AddThemeFontSizeOverride("font_size", 16);
		_voiceLangOption.AddItem("English", 0);
		_voiceLangOption.AddItem("Russian", 1);
		_voiceLangOption.CustomMinimumSize = new Vector2(80, 20);
		vbox.AddChild(CreateOptionRow("Voice Language", _voiceLangOption, font));

		// Subtitles
		_subtitlesCheck = new CheckBox();
		_subtitlesCheck.Text = "ON";
		_subtitlesCheck.AddThemeFontOverride("font", font);
		_subtitlesCheck.AddThemeFontSizeOverride("font_size", 16);
		vbox.AddChild(CreateOptionRow("Subtitles", _subtitlesCheck, font));

		// Reset Everything Button
		var resetBtn = new Button();
		resetBtn.Text = "RESET EVERYTHING";
		resetBtn.Flat = true;
		resetBtn.AddThemeFontOverride("font", font);
		resetBtn.AddThemeFontSizeOverride("font_size", 16);
		resetBtn.AddThemeColorOverride("font_color", new Color(1.0f, 0.25f, 0.25f));
		resetBtn.Pressed += OnResetSettingsPressed;
		vbox.AddChild(CreateOptionRow("Reset Settings", resetBtn, font));
	}

	private void CreateGraphicsTab(Font font)
	{
		var scroll = CreateScrollContainer("Graphics");
		var vbox = CreateTabVBox(scroll);

		// Brightness Row
		var brightnessHBox = new HBoxContainer();
		brightnessHBox.AddThemeConstantOverride("separation", 5);
		brightnessHBox.CustomMinimumSize = new Vector2(160, 20);

		_brightnessSlider = new HSlider();
		_brightnessSlider.MinValue = 0.5f;
		_brightnessSlider.MaxValue = 1.5f;
		_brightnessSlider.Step = 0.05f;
		_brightnessSlider.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_brightnessSlider.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
		_brightnessSlider.ValueChanged += (val) => _brightnessValueLabel.Text = string.Format("{0:0.00}", val);
		brightnessHBox.AddChild(_brightnessSlider);

		_brightnessValueLabel = CreateLabel("1.00", font, 12);
		_brightnessValueLabel.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
		brightnessHBox.AddChild(_brightnessValueLabel);

		vbox.AddChild(CreateOptionRow("Brightness", brightnessHBox, font));

		// FPS Limit Option Button
		_fpsOption = new OptionButton();
		_fpsOption.AddThemeFontOverride("font", font);
		_fpsOption.AddThemeFontSizeOverride("font_size", 16);
		_fpsOption.AddItem("Unlimited", 0);
		_fpsOption.AddItem("30 FPS", 30);
		_fpsOption.AddItem("60 FPS", 60);
		_fpsOption.AddItem("120 FPS", 120);
		_fpsOption.AddItem("144 FPS", 144);
		_fpsOption.CustomMinimumSize = new Vector2(80, 20);
		vbox.AddChild(CreateOptionRow("FPS Limit", _fpsOption, font));

		// VSync CheckBox
		_vsyncCheck = new CheckBox();
		_vsyncCheck.Text = "Enabled";
		_vsyncCheck.AddThemeFontOverride("font", font);
		_vsyncCheck.AddThemeFontSizeOverride("font_size", 16);
		vbox.AddChild(CreateOptionRow("V-Sync", _vsyncCheck, font));
	}

	private void CreateImmersionTab(Font font)
	{
		var scroll = CreateScrollContainer("Immersion");
		var vbox = CreateTabVBox(scroll);

		// Screen Shake Slider Row
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

		// Master
		var mHBox = new HBoxContainer();
		mHBox.AddThemeConstantOverride("separation", 5);
		mHBox.CustomMinimumSize = new Vector2(160, 20);
		_masterSlider = CreateVolumeSlider();
		_masterSlider.ValueChanged += (val) => _masterValueLabel.Text = $"{(int)val}%";
		mHBox.AddChild(_masterSlider);
		_masterValueLabel = CreateLabel("80%", font, 12);
		mHBox.AddChild(_masterValueLabel);
		vbox.AddChild(CreateOptionRow("Master Volume", mHBox, font));

		// Music
		var musHBox = new HBoxContainer();
		musHBox.AddThemeConstantOverride("separation", 5);
		musHBox.CustomMinimumSize = new Vector2(160, 20);
		_musicSlider = CreateVolumeSlider();
		_musicSlider.ValueChanged += (val) => _musicValueLabel.Text = $"{(int)val}%";
		musHBox.AddChild(_musicSlider);
		_musicValueLabel = CreateLabel("70%", font, 12);
		musHBox.AddChild(_musicValueLabel);
		vbox.AddChild(CreateOptionRow("Music Volume", musHBox, font));

		// SFX
		var sfxHBox = new HBoxContainer();
		sfxHBox.AddThemeConstantOverride("separation", 5);
		sfxHBox.CustomMinimumSize = new Vector2(160, 20);
		_sfxSlider = CreateVolumeSlider();
		_sfxSlider.ValueChanged += (val) => _sfxValueLabel.Text = $"{(int)val}%";
		sfxHBox.AddChild(_sfxSlider);
		_sfxValueLabel = CreateLabel("70%", font, 12);
		sfxHBox.AddChild(_sfxValueLabel);
		vbox.AddChild(CreateOptionRow("SFX Volume", sfxHBox, font));
	}

	private void CreateControlsTab(Font font)
	{
		var scroll = CreateScrollContainer("Controls");
		var vbox = CreateTabVBox(scroll);

		// Mouse Sensitivity
		var sensHBox = new HBoxContainer();
		sensHBox.AddThemeConstantOverride("separation", 5);
		sensHBox.CustomMinimumSize = new Vector2(160, 20);
		_sensitivitySlider = new HSlider();
		_sensitivitySlider.MinValue = 0.1f;
		_sensitivitySlider.MaxValue = 3.0f;
		_sensitivitySlider.Step = 0.05f;
		_sensitivitySlider.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_sensitivitySlider.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
		_sensitivitySlider.ValueChanged += (val) => _sensitivityValueLabel.Text = string.Format("{0:0.00}", val);
		sensHBox.AddChild(_sensitivitySlider);
		_sensitivityValueLabel = CreateLabel("1.00", font, 12);
		_sensitivityValueLabel.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
		sensHBox.AddChild(_sensitivityValueLabel);
		vbox.AddChild(CreateOptionRow("Mouse Sensitivity", sensHBox, font));

		// Keybinds section header
		var keybindsHeader = new MarginContainer();
		keybindsHeader.AddThemeConstantOverride("margin_top", 10);
		keybindsHeader.AddThemeConstantOverride("margin_bottom", 5);
		var headerLabel = CreateLabel("Key Bindings (Esc to cancel)", font, 14);
		headerLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.75f, 0.15f));
		keybindsHeader.AddChild(headerLabel);
		vbox.AddChild(keybindsHeader);

		foreach (var action in SettingsManager.KeyBinds.Keys)
		{
			// Button for mapping key
			var bindBtn = new Button();
			bindBtn.Text = SettingsManager.KeyBinds[action].ToString();
			bindBtn.AddThemeFontOverride("font", font);
			bindBtn.AddThemeFontSizeOverride("font_size", 16);
			bindBtn.CustomMinimumSize = new Vector2(100, 20);
			bindBtn.Flat = true;

			// Add a simple border/flat hover style to bind buttons
			var btnNormal = new StyleBoxEmpty();
			var btnHover = new StyleBoxFlat();
			btnHover.BgColor = new Color(1, 1, 1, 0.05f);
			btnHover.BorderColor = new Color(1, 1, 1, 0.2f);
			btnHover.BorderWidthBottom = btnHover.BorderWidthLeft = btnHover.BorderWidthRight = btnHover.BorderWidthTop = 1;
			bindBtn.AddThemeStyleboxOverride("normal", btnNormal);
			bindBtn.AddThemeStyleboxOverride("hover", btnHover);
			bindBtn.AddThemeStyleboxOverride("pressed", btnNormal);
			bindBtn.AddThemeStyleboxOverride("focus", btnNormal);

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

		// Add separator line below the row
		var separator = new ColorRect();
		separator.Color = new Color(1.0f, 1.0f, 1.0f, 0.12f);
		separator.CustomMinimumSize = new Vector2(0, 1);
		rowVBox.AddChild(separator);

		return rowVBox;
	}

	private string FormatActionName(string action)
	{
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
		return slider;
	}

	private Label CreateLabel(string text, Font font, int fontSize)
	{
		var label = new Label();
		label.Text = text;
		label.AddThemeFontOverride("font", font);
		label.AddThemeFontSizeOverride("font_size", fontSize + 4);
		label.AddThemeConstantOverride("letter_spacing", 1);
		return label;
	}

	private void LoadCurrentSettingsIntoUI()
	{
		_brightnessSlider.Value = SettingsManager.Brightness;
		_brightnessValueLabel.Text = string.Format("{0:0.00}", SettingsManager.Brightness);

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

		_sensitivitySlider.Value = SettingsManager.MouseSensitivity;
		_sensitivityValueLabel.Text = string.Format("{0:0.00}", SettingsManager.MouseSensitivity);

		UpdateBindButtonLabels();
	}

	private void UpdateBindButtonLabels()
	{
		foreach (var pair in _bindButtons)
		{
			pair.Value.Text = SettingsManager.KeyBinds[pair.Key].ToString();
		}
	}

	private void StartListeningForBind(string action)
	{
		if (_isListeningForBind) return;

		_actionToBind = action;
		_isListeningForBind = true;
		_bindButtons[action].Text = "...";
	}

	public override void _Input(InputEvent @event)
	{
		if (!_isListeningForBind || _actionToBind == null) return;

		if (@event is InputEventKey keyEvent && keyEvent.Pressed)
		{
			if (keyEvent.Keycode == Key.Escape)
			{
				_isListeningForBind = false;
				_actionToBind = null;
				UpdateBindButtonLabels();
				GetViewport().SetInputAsHandled();
				return;
			}

			// Map key
			SettingsManager.KeyBinds[_actionToBind] = keyEvent.Keycode;
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
		_sensitivitySlider.Value = 1.0f;

		SettingsManager.KeyBinds["move_left"] = Key.A;
		SettingsManager.KeyBinds["move_right"] = Key.D;
		SettingsManager.KeyBinds["move_up"] = Key.W;
		SettingsManager.KeyBinds["move_down"] = Key.S;
		SettingsManager.KeyBinds["interact"] = Key.E;
		SettingsManager.KeyBinds["reload"] = Key.R;

		UpdateBindButtonLabels();
	}

	private void OnBackPressed()
	{
		// Save settings from UI back to SettingsManager
		SettingsManager.Brightness = (float)_brightnessSlider.Value;

		int selectedIdx = _fpsOption.Selected;
		SettingsManager.MaxFps = _fpsOption.GetItemId(selectedIdx);
		SettingsManager.VSync = _vsyncCheck.ButtonPressed;

		SettingsManager.ScreenShake = (float)_screenshakeSlider.Value;

		SettingsManager.MasterVolume = (float)_masterSlider.Value;
		SettingsManager.MusicVolume = (float)_musicSlider.Value;
		SettingsManager.SFXVolume = (float)_sfxSlider.Value;

		SettingsManager.MouseSensitivity = (float)_sensitivitySlider.Value;

		SettingsManager.SaveSettings();
		SettingsManager.ApplySettings();

		// Apply brightness dynamically to level if active
		var level = GetTree().GetFirstNodeInGroup("Level") as Level;
		if (level != null)
		{
			var modulate = level.GetNodeOrNull<CanvasModulate>("CanvasModulate");
			if (modulate != null)
			{
				float ambient = Mathf.Clamp((SettingsManager.Brightness - 1.0f) * 0.5f, 0.0f, 0.5f);
				modulate.Color = new Color(ambient, ambient, ambient, 1.0f);
			}
		}

		QueueFree();
	}
}
