using Godot;
using System;

public partial class MainMenu : CanvasLayer
{
	[ExportGroup("UI Sounds")]
	[Export] public AudioStream ButtonHoverSound;
	[Export] public AudioStream ButtonClickSound;

	public static AudioStream HoverSound;
	public static AudioStream ClickSound;

	private Control _menuRoot;

	public static void PlayUISound(AudioStream stream)
	{
		if (stream == null) return;
		var audioPlayer = new AudioStreamPlayer();
		audioPlayer.Stream = stream;
		audioPlayer.Bus = "SFX";
		((SceneTree)Engine.GetMainLoop()).Root.AddChild(audioPlayer);
		audioPlayer.Play();
		audioPlayer.Finished += () => audioPlayer.QueueFree();
	}

	public static void ConnectButtonSounds(Node root)
	{
		if (root is Button btn)
		{
			if (!btn.IsConnected(Button.SignalName.MouseEntered, Callable.From(() => OnButtonHover(btn))))
			{
				btn.MouseEntered += () => OnButtonHover(btn);
			}
			if (!btn.IsConnected(Button.SignalName.Pressed, Callable.From(() => OnButtonClick(btn))))
			{
				btn.Pressed += () => OnButtonClick(btn);
			}
		}
		else if (root is Slider slider)
		{
			if (!slider.IsConnected(Slider.SignalName.MouseEntered, Callable.From(() => PlayUISound(HoverSound))))
			{
				slider.MouseEntered += () => PlayUISound(HoverSound);
			}
			if (!slider.IsConnected(Slider.SignalName.DragStarted, Callable.From(() => PlayUISound(ClickSound))))
			{
				slider.DragStarted += () => PlayUISound(ClickSound);
			}
		}
		else if (root is TabContainer tabContainer)
		{
			if (!tabContainer.IsConnected(TabContainer.SignalName.TabSelected, Callable.From((long tab) => PlayUISound(ClickSound))))
			{
				tabContainer.TabSelected += (tab) => PlayUISound(ClickSound);
			}
			if (!tabContainer.IsConnected(TabContainer.SignalName.TabHovered, Callable.From((long tab) => PlayUISound(HoverSound))))
			{
				tabContainer.TabHovered += (tab) => PlayUISound(HoverSound);
			}
		}

		foreach (Node child in root.GetChildren())
		{
			ConnectButtonSounds(child);
		}
	}

	private static void OnButtonHover(Button btn)
	{
		if (btn.Disabled) return;
		var sound = HoverSound ?? GD.Load<AudioStream>("res://assets/sounds/hover.mp3");
		PlayUISound(sound);
	}

	private static void OnButtonClick(Button btn)
	{
		if (btn.Disabled) return;
		var sound = ClickSound ?? GD.Load<AudioStream>("res://assets/sounds/click.mp3");
		PlayUISound(sound);
	}

	public override void _Ready()
	{
		if (ButtonHoverSound == null) {
			ButtonHoverSound = GD.Load<AudioStream>("res://assets/sounds/hover.mp3");
		}
		if (ButtonClickSound == null) {
			ButtonClickSound = GD.Load<AudioStream>("res://assets/sounds/click.mp3");
		}

		HoverSound = ButtonHoverSound;
		ClickSound = ButtonClickSound;

		// Load settings configuration on startup
		SettingsManager.LoadSettings();

		if (MusicManager.Instance == null) {
			var musicManager = new MusicManager();
			GetTree().Root.CallDeferred(Node.MethodName.AddChild, musicManager);
		}
		else
		{
			MusicManager.Instance.PlayMusic("res://assets/sounds/doom_10. Ties That Bind.mp3");
		}

		var viewportSize = GetViewport().GetVisibleRect().Size;
		var scaledSize = viewportSize / 3.5f;

		var background = GetNodeOrNull<Control>("Background");
		if (background != null) {
			background.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
			background.Size = scaledSize;
			background.Scale = new Vector2(3.5f, 3.5f);
		}

		_menuRoot = GetNode<Control>("MenuRoot");
		if (_menuRoot != null) {
			_menuRoot.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
			_menuRoot.Size = scaledSize;
			_menuRoot.Scale = new Vector2(3.5f, 3.5f);
		}
		
		var mainFont = GD.Load<Font>("res://assets/fonts/fff-forward/FFFFORWA.TTF");
		foreach (var node in GetNode<VBoxContainer>("MenuRoot/VBoxContainer").GetChildren())
		{
			if (node is Button btn)
			{
				btn.AddThemeFontOverride("font", mainFont);
				btn.AddThemeFontSizeOverride("font_size", 13);
				btn.AddThemeConstantOverride("letter_spacing", 1);
				btn.CustomMinimumSize = new Vector2(80, 18);
			}
		}

		// Подключаем кнопки	
		GetNode<Button>("MenuRoot/VBoxContainer/PlayButton").Pressed += OnNewGamePressed;
		GetNode<Button>("MenuRoot/VBoxContainer/LoadButton").Pressed += OnLoadPressed;
		GetNode<Button>("MenuRoot/VBoxContainer/SettingsButton").Pressed += OnSettingsPressed;
		GetNode<Button>("MenuRoot/VBoxContainer/ExitButton").Pressed += OnExitPressed;

		// Подключаем звуки ко всем кнопкам в меню
		ConnectButtonSounds(this);
	}	

	private Control _activeOverlay = null;
	private ulong _lastClickTime = 0;
	private string _lastClickedSlot = "";
	private Label _infoLabel = null;
	private string _selectedSlotId = "";
	private Button _deleteBtn = null;

	private void OnNewGamePressed()
	{
		ShowCharacterSelection();
	}

	private void OnLoadPressed()
	{
		ShowSlotSelection(true);
	}

	private void ShowCharacterSelection()
	{
		HideMainMenu();

		var charSelectRoot = new Control();
		charSelectRoot.Name = "CharSelectRoot";
		charSelectRoot.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		_menuRoot.AddChild(charSelectRoot);
		_activeOverlay = charSelectRoot;

		// Solid dark background overlay (matching settings!)
		var bgOverlay = new ColorRect();
		bgOverlay.Color = new Color(0.02f, 0.02f, 0.02f, 1.0f);
		charSelectRoot.AddChild(bgOverlay);
		bgOverlay.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

		var titleFont = GD.Load<Font>("res://assets/fonts/silver/Silver.ttf");

		// Title
		var title = new Label();
		title.Text = "SELECT CHARACTER";
		title.HorizontalAlignment = HorizontalAlignment.Center;
		title.AddThemeFontOverride("font", titleFont);
		title.AddThemeFontSizeOverride("font_size", 32);
		title.AddThemeColorOverride("font_color", new Color(1.0f, 1.0f, 1.0f));
		title.Position = new Vector2(0, 10);
		title.Size = new Vector2(_menuRoot.Size.X, 25);
		charSelectRoot.AddChild(title);

		// Style Card
		var cardStyle = new StyleBoxFlat();
		cardStyle.BgColor = new Color(0.15f, 0.15f, 0.15f, 0.9f);
		cardStyle.BorderWidthBottom = cardStyle.BorderWidthLeft = cardStyle.BorderWidthRight = cardStyle.BorderWidthTop = 1;
		cardStyle.BorderColor = new Color(0.85f, 0.65f, 0.15f); // Gold border

		var cardHover = cardStyle.Duplicate() as StyleBoxFlat;
		cardHover.BgColor = new Color(0.25f, 0.25f, 0.25f, 0.9f);

		var lockStyle = new StyleBoxFlat();
		lockStyle.BgColor = new Color(0.08f, 0.08f, 0.08f, 0.8f);
		lockStyle.BorderWidthBottom = lockStyle.BorderWidthLeft = lockStyle.BorderWidthRight = lockStyle.BorderWidthTop = 1;
		lockStyle.BorderColor = new Color(0.3f, 0.3f, 0.3f);

		// Single card button in the center (shifted left, font size 22)
		var cardBtn = new Button();
		cardBtn.CustomMinimumSize = new Vector2(60, 80);
		cardBtn.Position = new Vector2(35, 52);
		cardBtn.AddThemeFontOverride("font", titleFont);
		cardBtn.AddThemeFontSizeOverride("font_size", 22);
		charSelectRoot.AddChild(cardBtn);

		// Info panel on the right (increased height to 105, Y=40)
		var infoPanel = new Panel();
		infoPanel.Position = new Vector2(_menuRoot.Size.X - 145, 40);
		infoPanel.Size = new Vector2(130, 105);
		charSelectRoot.AddChild(infoPanel);

		var infoStyle = new StyleBoxFlat();
		infoStyle.BgColor = new Color(0.05f, 0.05f, 0.05f, 0.95f);
		infoStyle.BorderWidthBottom = infoStyle.BorderWidthLeft = infoStyle.BorderWidthRight = infoStyle.BorderWidthTop = 1;
		infoStyle.BorderColor = new Color(1, 1, 1, 0.2f);
		infoPanel.AddThemeStyleboxOverride("panel", infoStyle);

		var infoText = new Label();
		infoText.Text = "";
		infoText.HorizontalAlignment = HorizontalAlignment.Center;
		infoText.VerticalAlignment = VerticalAlignment.Top;
		infoText.AddThemeFontOverride("font", titleFont);
		infoText.AddThemeFontSizeOverride("font_size", 18);
		infoText.AddThemeConstantOverride("line_spacing", -2); // Reduce vertical margin/line spacing
		infoText.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		infoPanel.AddChild(infoText);

		// Horizontal navigation buttons (shifted left, halved size, font size 12)
		var prevBtn = new Button();
		prevBtn.Text = "<";
		prevBtn.Flat = true;
		prevBtn.AddThemeFontOverride("font", titleFont);
		prevBtn.AddThemeFontSizeOverride("font_size", 12);
		prevBtn.Position = new Vector2(15, 87);
		prevBtn.CustomMinimumSize = new Vector2(10, 10);
		charSelectRoot.AddChild(prevBtn);

		var nextBtn = new Button();
		nextBtn.Text = ">";
		nextBtn.Flat = true;
		nextBtn.AddThemeFontOverride("font", titleFont);
		nextBtn.AddThemeFontSizeOverride("font_size", 12);
		nextBtn.Position = new Vector2(105, 87);
		nextBtn.CustomMinimumSize = new Vector2(10, 10);
		charSelectRoot.AddChild(nextBtn);

		int currentCardIndex = 0;

		Action updateCardUI = () => {
			if (currentCardIndex == 0)
			{
				cardBtn.Text = "KNIGHT";
				cardBtn.Disabled = false;
				cardBtn.AddThemeStyleboxOverride("normal", cardStyle);
				cardBtn.AddThemeStyleboxOverride("hover", cardHover);
				cardBtn.AddThemeStyleboxOverride("pressed", cardStyle);
				cardBtn.AddThemeStyleboxOverride("focus", cardStyle);
				
				infoText.Text = "KNIGHT STATS:\nHP: 55\nMANA: 100\nSPEED: 60\nWEAPON: Axe";
				infoText.HorizontalAlignment = HorizontalAlignment.Left;
				infoText.Position = new Vector2(8, 3); // Shift up
			}
			else if (currentCardIndex == 1)
			{
				cardBtn.Text = "MAGICIAN";
				cardBtn.Disabled = true;
				cardBtn.AddThemeStyleboxOverride("disabled", lockStyle);
				
				infoText.Text = "MAGICIAN STATS:\nHP: 40\nMANA: 200\nSPEED: 55\nWEAPON: Staff";
				infoText.HorizontalAlignment = HorizontalAlignment.Left;
				infoText.Position = new Vector2(8, 3); // Shift up
			}
			else
			{
				cardBtn.Text = "LOCKED";
				cardBtn.Disabled = true;
				cardBtn.AddThemeStyleboxOverride("disabled", lockStyle);
				
				infoText.Text = "CHARACTER LOCKED";
				infoText.HorizontalAlignment = HorizontalAlignment.Center;
				infoText.Position = Vector2.Zero;
			}
		};

		// Initialize
		updateCardUI();

		prevBtn.Pressed += () => {
			currentCardIndex = (currentCardIndex - 1 + 3) % 3;
			updateCardUI();
		};

		nextBtn.Pressed += () => {
			currentCardIndex = (currentCardIndex + 1) % 3;
			updateCardUI();
		};

		cardBtn.Pressed += () => {
			if (currentCardIndex == 0) {
				charSelectRoot.QueueFree();
				ShowSlotSelection(false);
			}
		};

		// Back button (smaller, lefter, font size 20)
		var backBtn = new Button();
		backBtn.Text = "BACK";
		backBtn.Flat = true;
		backBtn.AddThemeFontOverride("font", titleFont);
		backBtn.AddThemeFontSizeOverride("font_size", 20);
		backBtn.Position = new Vector2(8, _menuRoot.Size.Y - 26);
		backBtn.CustomMinimumSize = new Vector2(40, 16);
		charSelectRoot.AddChild(backBtn);

		backBtn.Pressed += () => {
			charSelectRoot.QueueFree();
			ShowMainMenu();
		};

		ConnectButtonSounds(charSelectRoot);
	}

	private void ShowSlotSelection(bool isLoadMode)
	{
		HideMainMenu();

		var slotSelectRoot = new Control();
		slotSelectRoot.Name = "SlotSelectRoot";
		slotSelectRoot.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		_menuRoot.AddChild(slotSelectRoot);
		_activeOverlay = slotSelectRoot;

		// Reverted design: NO background overlay (transparent, showing main menu grey underneath)

		var titleFont = GD.Load<Font>("res://assets/fonts/silver/Silver.ttf");

		// Title (reverted to 28)
		var title = new Label();
		title.Text = isLoadMode ? "LOAD WORLD" : "CHOOSE SAVE SLOT";
		title.HorizontalAlignment = HorizontalAlignment.Center;
		title.AddThemeFontOverride("font", titleFont);
		title.AddThemeFontSizeOverride("font_size", 28);
		title.Position = new Vector2(0, 10);
		title.Size = new Vector2(_menuRoot.Size.X, 25);
		slotSelectRoot.AddChild(title);

		// VBox for Slots on the left (reverted to Y=45, H=95)
		var vbox = new VBoxContainer();
		vbox.Position = new Vector2(20, 45);
		vbox.Size = new Vector2(130, 95);
		vbox.AddThemeConstantOverride("separation", 6);
		slotSelectRoot.AddChild(vbox);

		// Info box on the right (reverted to Y=45, H=95)
		var infoPanel = new Panel();
		infoPanel.Position = new Vector2(160, 45);
		infoPanel.Size = new Vector2(_menuRoot.Size.X - 180, 95);
		slotSelectRoot.AddChild(infoPanel);

		var infoStyle = new StyleBoxFlat();
		infoStyle.BgColor = new Color(0.05f, 0.05f, 0.05f, 0.95f);
		infoStyle.BorderWidthBottom = infoStyle.BorderWidthLeft = infoStyle.BorderWidthRight = infoStyle.BorderWidthTop = 1;
		infoStyle.BorderColor = new Color(1, 1, 1, 0.2f);
		infoPanel.AddThemeStyleboxOverride("panel", infoStyle);

		_infoLabel = new Label();
		_infoLabel.Text = isLoadMode ? "Select a save\nto see stats." : "Select slot\nto save game.";
		_infoLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_infoLabel.VerticalAlignment = VerticalAlignment.Top;
		_infoLabel.AddThemeFontOverride("font", titleFont);
		_infoLabel.AddThemeFontSizeOverride("font_size", 14); // Reverted to 14
		_infoLabel.AddThemeConstantOverride("line_spacing", -2); // Fit stats vertically
		_infoLabel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		_infoLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_infoLabel.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		infoPanel.AddChild(_infoLabel);

		_selectedSlotId = ""; // Reset selected slot

		// Load slot buttons (reverted to standard full-width layout)
		for (int i = 1; i <= 3; i++)
		{
			string slotId = i.ToString();
			var slotSave = SaveManager.LoadSave(slotId);

			var btn = new Button();
			btn.CustomMinimumSize = new Vector2(130, 24); // Reverted to full width 130
			btn.AddThemeFontOverride("font", titleFont);
			btn.AddThemeFontSizeOverride("font_size", 18); // Reverted to 18
			vbox.AddChild(btn);

			if (slotSave != null)
			{
				btn.Text = $"Slot {slotId} ({slotSave.SaveDateString})";
			}
			else
			{
				btn.Text = $"Slot {slotId} - EMPTY";
				if (isLoadMode)
				{
					btn.Disabled = true;
				}
			}

			// Style button
			var btnNormal = new StyleBoxFlat();
			btnNormal.BgColor = new Color(0.12f, 0.12f, 0.12f, 0.9f);
			btnNormal.BorderWidthBottom = btnNormal.BorderWidthLeft = btnNormal.BorderWidthRight = btnNormal.BorderWidthTop = 1;
			btnNormal.BorderColor = new Color(1, 1, 1, 0.15f);
			btn.AddThemeStyleboxOverride("normal", btnNormal);

			var btnHover = btnNormal.Duplicate() as StyleBoxFlat;
			btnHover.BgColor = new Color(0.2f, 0.2f, 0.2f, 0.9f);
			btn.AddThemeStyleboxOverride("hover", btnHover);
			btn.AddThemeStyleboxOverride("pressed", btnNormal);
			btn.AddThemeStyleboxOverride("focus", btnNormal);

			btn.Pressed += () => OnSlotClicked(slotId, isLoadMode);
		}

		// BACK button (reverted to Y=Size.Y-28, W=60, H=20, font size 18)
		var backBtn = new Button();
		backBtn.Text = "BACK";
		backBtn.Flat = true;
		backBtn.AddThemeFontOverride("font", titleFont);
		backBtn.AddThemeFontSizeOverride("font_size", 18);
		backBtn.Position = new Vector2(20, _menuRoot.Size.Y - 28);
		backBtn.CustomMinimumSize = new Vector2(60, 20);
		slotSelectRoot.AddChild(backBtn);

		backBtn.Pressed += () => {
			slotSelectRoot.QueueFree();
			if (isLoadMode)
			{
				ShowMainMenu();
			}
			else
			{
				ShowCharacterSelection();
			}
		};

		// Single DELETE button in the bottom right corner (X = Size.X - 80, Y = Size.Y - 28, W=60, H=20, font size 18)
		_deleteBtn = new Button();
		_deleteBtn.Text = "DELETE";
		_deleteBtn.Flat = true;
		_deleteBtn.AddThemeFontOverride("font", titleFont);
		_deleteBtn.AddThemeFontSizeOverride("font_size", 18);
		_deleteBtn.Position = new Vector2(_menuRoot.Size.X - 80, _menuRoot.Size.Y - 28);
		_deleteBtn.CustomMinimumSize = new Vector2(60, 20);
		_deleteBtn.Visible = false; // Hidden by default
		slotSelectRoot.AddChild(_deleteBtn);

		var delStyle = new StyleBoxFlat();
		delStyle.BgColor = new Color(0.2f, 0.05f, 0.05f, 0.9f);
		delStyle.BorderWidthBottom = delStyle.BorderWidthLeft = delStyle.BorderWidthRight = delStyle.BorderWidthTop = 1;
		delStyle.BorderColor = new Color(1, 0.2f, 0.2f, 0.4f);
		_deleteBtn.AddThemeStyleboxOverride("normal", delStyle);

		var delHover = delStyle.Duplicate() as StyleBoxFlat;
		delHover.BgColor = new Color(0.35f, 0.08f, 0.08f, 0.9f);
		_deleteBtn.AddThemeStyleboxOverride("hover", delHover);
		_deleteBtn.AddThemeColorOverride("font_color", new Color(1, 0.3f, 0.3f));

		_deleteBtn.Pressed += () => {
			if (!string.IsNullOrEmpty(_selectedSlotId)) {
				SaveManager.DeleteSave(_selectedSlotId);
				slotSelectRoot.QueueFree();
				ShowSlotSelection(isLoadMode);
			}
		};

		ConnectButtonSounds(slotSelectRoot);
	}

	private void OnSlotClicked(string slotId, bool isLoadMode)
	{
		_selectedSlotId = slotId;
		if (_deleteBtn != null) {
			_deleteBtn.Visible = (SaveManager.LoadSave(slotId) != null);
		}

		ulong now = Time.GetTicksMsec();
		if (_lastClickedSlot == slotId && now - _lastClickTime < 400)
		{
			LaunchSlot(slotId, isLoadMode);
		}
		else
		{
			_lastClickTime = now;
			_lastClickedSlot = slotId;
			SelectSlotDetails(slotId);
		}
	}


	private void SelectSlotDetails(string slotId)
	{
		if (_infoLabel == null) return;

		var slotSave = SaveManager.LoadSave(slotId);
		var titleFont = GD.Load<Font>("res://assets/fonts/silver/Silver.ttf");
		
		if (slotSave != null)
		{
			int mins = (int)(slotSave.PlayedTimeSeconds / 60);
			int secs = (int)(slotSave.PlayedTimeSeconds % 60);
			string timeStr = $"{mins:D2}:{secs:D2}";
			
			string weapons = slotSave.WeaponResourcePaths.Count > 0 
				? string.Join(", ", slotSave.WeaponResourcePaths.ConvertAll(w => System.IO.Path.GetFileNameWithoutExtension(w).ToUpper()))
				: "NONE";

			_infoLabel.Text = $"SLOT {slotId} STATISTICS:\n" +
							 $"HERO: {slotSave.CharacterName.ToUpper()}\n" +
							 $"LEVEL REACHED: {slotSave.CurrentLevelIndex}\n" +
							 $"PLAYED TIME: {timeStr}\n" +
							 $"MONEY: {slotSave.Coins}\n" +
							 $"WEAPONS: {weapons}";
			_infoLabel.HorizontalAlignment = HorizontalAlignment.Left;
			_infoLabel.Position = new Vector2(10, 3);
		}
		else
		{
			_infoLabel.Text = "EMPTY SLOT\n\nDouble-click to start\na new world here.";
			_infoLabel.HorizontalAlignment = HorizontalAlignment.Center;
			_infoLabel.Position = Vector2.Zero;
		}
	}

	private void LaunchSlot(string slotId, bool isLoadMode)
	{
		if (isLoadMode)
		{
			var data = SaveManager.LoadSave(slotId);
			if (data != null)
			{
				SaveManager.ActiveSlotId = slotId;
				SaveManager.CurrentSave = data;
				GetTree().ChangeSceneToFile("res://scene/levels/level.tscn");
			}
		}
		else
		{
			// Start new game in this slot (overwriting if exists)
			SaveManager.ActiveSlotId = slotId;
			SaveManager.CurrentSave = new SaveData {
				SlotId = slotId,
				CharacterName = "Knight",
				CurrentLevelIndex = 1,
				CurrentHealth = 55,
				CurrentMana = 100,
				Coins = 0
			};
			SaveManager.SaveGame(SaveManager.CurrentSave);
			GetTree().ChangeSceneToFile("res://scene/levels/level.tscn");
		}
	}

	private void HideMainMenu()
	{
		var vbox = GetNodeOrNull<Control>("MenuRoot/VBoxContainer");
		if (vbox != null) vbox.Visible = false;
	}

	private void ShowMainMenu()
	{
		var vbox = GetNodeOrNull<Control>("MenuRoot/VBoxContainer");
		if (vbox != null) vbox.Visible = true;
	}

	private void OnSettingsPressed()
	{
		var settings = new SettingsMenu();
		AddChild(settings);
	}

	private void OnExitPressed()
	{
		GetTree().Quit();
	}
}
