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
	private string _selectedCharacterName = "Knight";
	private bool _isLoadSlotMode = false;

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
		charSelectRoot.Size = _menuRoot.Size;
		_menuRoot.AddChild(charSelectRoot);
		_activeOverlay = charSelectRoot;

		float W = _menuRoot.Size.X;
		float H = _menuRoot.Size.Y;

		// Solid dark background overlay
		var bgOverlay = new ColorRect();
		bgOverlay.Color = new Color(0.04f, 0.04f, 0.06f, 0.98f);
		charSelectRoot.AddChild(bgOverlay);
		bgOverlay.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

		var titleFont = GD.Load<Font>("res://assets/fonts/silver/Silver.ttf");
		var knightData = GD.Load<PlayerData>("res://data/characters/knight.tres");
		var magicianData = GD.Load<PlayerData>("res://data/characters/magician.tres");

		// Title
		var title = new Label();
		title.Text = "CHOOSE YOUR HERO";
		title.HorizontalAlignment = HorizontalAlignment.Center;
		title.AddThemeFontOverride("font", titleFont);
		title.AddThemeFontSizeOverride("font_size", 28);
		title.AddThemeColorOverride("font_color", new Color(0.98f, 0.84f, 0.32f));
		title.Position = new Vector2(0, 10);
		title.Size = new Vector2(W, 24);
		charSelectRoot.AddChild(title);

		var subTitle = new Label();
		subTitle.Text = "Select a character card to view stats, then press CONFIRM HERO";
		subTitle.HorizontalAlignment = HorizontalAlignment.Center;
		subTitle.AddThemeFontOverride("font", titleFont);
		subTitle.AddThemeFontSizeOverride("font_size", 13);
		subTitle.AddThemeColorOverride("font_color", new Color(0.7f, 0.72f, 0.78f));
		subTitle.Position = new Vector2(0, 34);
		subTitle.Size = new Vector2(W, 18);
		charSelectRoot.AddChild(subTitle);

		// Card styles
		var cardNormalStyle = new StyleBoxFlat
		{
			BgColor = new Color(0.10f, 0.10f, 0.13f, 0.95f),
			BorderColor = new Color(0.35f, 0.35f, 0.42f),
			BorderWidthLeft = 1,
			BorderWidthTop = 1,
			BorderWidthRight = 1,
			BorderWidthBottom = 1,
			CornerRadiusTopLeft = 4,
			CornerRadiusTopRight = 4,
			CornerRadiusBottomLeft = 4,
			CornerRadiusBottomRight = 4
		};

		var cardSelectedStyle = new StyleBoxFlat
		{
			BgColor = new Color(0.18f, 0.15f, 0.10f, 0.98f),
			BorderColor = new Color(0.98f, 0.80f, 0.22f),
			BorderWidthLeft = 2,
			BorderWidthTop = 2,
			BorderWidthRight = 2,
			BorderWidthBottom = 2,
			CornerRadiusTopLeft = 4,
			CornerRadiusTopRight = 4,
			CornerRadiusBottomLeft = 4,
			CornerRadiusBottomRight = 4
		};

		var cardHoverStyle = cardSelectedStyle.Duplicate() as StyleBoxFlat;
		cardHoverStyle.BgColor = new Color(0.22f, 0.19f, 0.13f, 0.98f);

		var lockStyle = new StyleBoxFlat
		{
			BgColor = new Color(0.06f, 0.06f, 0.08f, 0.85f),
			BorderColor = new Color(0.22f, 0.22f, 0.26f),
			BorderWidthLeft = 1,
			BorderWidthTop = 1,
			BorderWidthRight = 1,
			BorderWidthBottom = 1,
			CornerRadiusTopLeft = 4,
			CornerRadiusTopRight = 4,
			CornerRadiusBottomLeft = 4,
			CornerRadiusBottomRight = 4
		};

		float cardsStartX = 22.0f;
		float cardsY = 56.0f;
		float cardW = 92.0f;
		float cardH = 196.0f;
		float cardGap = 9.0f;

		Button[] cardButtons = new Button[3];
		Label[] statusLabels = new Label[3];
		string[] cardNames = { "KNIGHT", "MAGICIAN", "LOCKED" };
		string[] cardRoles = { "WARRIOR", "ARCANE MAGE", "COMING SOON" };
		SpriteFrames[] cardAnims = { knightData?.CharacterAnimations, magicianData?.CharacterAnimations, null };

		for (int i = 0; i < 3; i++)
		{
			var btn = new Button();
			btn.Position = new Vector2(cardsStartX + i * (cardW + cardGap), cardsY);
			btn.Size = new Vector2(cardW, cardH);
			btn.CustomMinimumSize = new Vector2(cardW, cardH);
			btn.ClipContents = true;
			charSelectRoot.AddChild(btn);
			cardButtons[i] = btn;

			var roleLbl = new Label();
			roleLbl.Text = cardRoles[i];
			roleLbl.HorizontalAlignment = HorizontalAlignment.Center;
			roleLbl.AddThemeFontOverride("font", titleFont);
			roleLbl.AddThemeFontSizeOverride("font_size", 11);
			roleLbl.AddThemeColorOverride("font_color", i < 2 ? new Color(0.75f, 0.82f, 0.95f) : new Color(0.45f, 0.45f, 0.5f));
			roleLbl.Position = new Vector2(0, 8);
			roleLbl.Size = new Vector2(cardW, 14);
			btn.AddChild(roleLbl);

			// Pedestal backdrop inside card
			var portraitBg = new Panel();
			portraitBg.MouseFilter = Control.MouseFilterEnum.Ignore;
			portraitBg.Position = new Vector2(12, 26);
			portraitBg.Size = new Vector2(cardW - 24, 98);
			var pBgStyle = new StyleBoxFlat
			{
				BgColor = new Color(0.05f, 0.05f, 0.07f, 0.9f),
				BorderColor = new Color(1, 1, 1, 0.08f),
				BorderWidthLeft = 1,
				BorderWidthTop = 1,
				BorderWidthRight = 1,
				BorderWidthBottom = 1,
				CornerRadiusTopLeft = 3,
				CornerRadiusTopRight = 3,
				CornerRadiusBottomLeft = 3,
				CornerRadiusBottomRight = 3
			};
			portraitBg.AddThemeStyleboxOverride("panel", pBgStyle);
			btn.AddChild(portraitBg);

			if (cardAnims[i] != null)
			{
				var animSprite = new AnimatedSprite2D();
				animSprite.SpriteFrames = cardAnims[i];
				animSprite.Position = new Vector2(cardW * 0.5f, 82);
				animSprite.Scale = new Vector2(2.6f, 2.6f);
				animSprite.TextureFilter = CanvasItem.TextureFilterEnum.Nearest;
				btn.AddChild(animSprite);
				if (animSprite.SpriteFrames.HasAnimation("idle"))
				{
					animSprite.Play("idle");
				}
			}
			else
			{
				var lockIcon = new Label();
				lockIcon.Text = "?";
				lockIcon.HorizontalAlignment = HorizontalAlignment.Center;
				lockIcon.VerticalAlignment = VerticalAlignment.Center;
				lockIcon.AddThemeFontOverride("font", titleFont);
				lockIcon.AddThemeFontSizeOverride("font_size", 36);
				lockIcon.AddThemeColorOverride("font_color", new Color(0.35f, 0.35f, 0.4f));
				lockIcon.Position = new Vector2(0, 42);
				lockIcon.Size = new Vector2(cardW, 65);
				btn.AddChild(lockIcon);
			}

			var nameLbl = new Label();
			nameLbl.Text = cardNames[i];
			nameLbl.HorizontalAlignment = HorizontalAlignment.Center;
			nameLbl.AddThemeFontOverride("font", titleFont);
			nameLbl.AddThemeFontSizeOverride("font_size", 18);
			nameLbl.AddThemeColorOverride("font_color", i < 2 ? new Color(1f, 0.96f, 0.85f) : new Color(0.45f, 0.45f, 0.5f));
			nameLbl.Position = new Vector2(0, 132);
			nameLbl.Size = new Vector2(cardW, 22);
			btn.AddChild(nameLbl);

			var statusLbl = new Label();
			statusLbl.HorizontalAlignment = HorizontalAlignment.Center;
			statusLbl.AddThemeFontOverride("font", titleFont);
			statusLbl.AddThemeFontSizeOverride("font_size", 12);
			statusLbl.Position = new Vector2(0, 162);
			statusLbl.Size = new Vector2(cardW, 18);
			btn.AddChild(statusLbl);
			statusLabels[i] = statusLbl;

			if (i == 2)
			{
				btn.Disabled = true;
				btn.AddThemeStyleboxOverride("disabled", lockStyle);
				statusLbl.Text = "LOCKED";
				statusLbl.AddThemeColorOverride("font_color", new Color(0.45f, 0.45f, 0.5f));
			}
		}

		// Right-side Hero Info Panel
		float infoX = cardsStartX + 3 * cardW + 2 * cardGap + 12.0f;
		var infoPanel = new Panel();
		infoPanel.Position = new Vector2(infoX, cardsY);
		infoPanel.Size = new Vector2(W - infoX - 22.0f, cardH);
		charSelectRoot.AddChild(infoPanel);

		var infoStyle = new StyleBoxFlat
		{
			BgColor = new Color(0.08f, 0.08f, 0.11f, 0.96f),
			BorderColor = new Color(0.85f, 0.68f, 0.22f, 0.65f),
			BorderWidthLeft = 1,
			BorderWidthTop = 1,
			BorderWidthRight = 1,
			BorderWidthBottom = 1,
			CornerRadiusTopLeft = 4,
			CornerRadiusTopRight = 4,
			CornerRadiusBottomLeft = 4,
			CornerRadiusBottomRight = 4
		};
		infoPanel.AddThemeStyleboxOverride("panel", infoStyle);

		var infoHeader = new Label();
		infoHeader.AddThemeFontOverride("font", titleFont);
		infoHeader.AddThemeFontSizeOverride("font_size", 20);
		infoHeader.AddThemeColorOverride("font_color", new Color(1.0f, 0.84f, 0.28f));
		infoHeader.Position = new Vector2(12, 8);
		infoHeader.Size = new Vector2(infoPanel.Size.X - 24, 22);
		infoPanel.AddChild(infoHeader);

		var infoText = new Label();
		infoText.HorizontalAlignment = HorizontalAlignment.Left;
		infoText.VerticalAlignment = VerticalAlignment.Top;
		infoText.AddThemeFontOverride("font", titleFont);
		infoText.AddThemeFontSizeOverride("font_size", 14);
		infoText.AddThemeColorOverride("font_color", new Color(0.92f, 0.92f, 0.95f));
		infoText.AddThemeConstantOverride("line_spacing", -1);
		infoText.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		infoText.Position = new Vector2(12, 34);
		infoText.Size = new Vector2(infoPanel.Size.X - 24, cardH - 42);
		infoPanel.AddChild(infoText);

		int currentCardIndex = _selectedCharacterName == "Magician" ? 1 : 0;

		Action confirmSelection = () =>
		{
			_selectedCharacterName = currentCardIndex == 1 ? "Magician" : "Knight";
			charSelectRoot.QueueFree();
			_activeOverlay = null;
			ShowSlotSelection(false);
		};

		Action updateCardUI = () =>
		{
			for (int i = 0; i < 2; i++)
			{
				bool isSel = (i == currentCardIndex);
				var style = isSel ? cardSelectedStyle : cardNormalStyle;
				cardButtons[i].AddThemeStyleboxOverride("normal", style);
				cardButtons[i].AddThemeStyleboxOverride("hover", isSel ? cardHoverStyle : cardSelectedStyle);
				cardButtons[i].AddThemeStyleboxOverride("pressed", cardSelectedStyle);
				cardButtons[i].AddThemeStyleboxOverride("focus", style);

				statusLabels[i].Text = isSel ? "[ SELECTED ]" : "CLICK TO SELECT";
				statusLabels[i].AddThemeColorOverride("font_color", isSel ? new Color(1.0f, 0.85f, 0.25f) : new Color(0.6f, 0.62f, 0.68f));
			}

			if (currentCardIndex == 0)
			{
				infoHeader.Text = "KNIGHT — VANGUARD";
				infoText.Text =
					"HEALTH: 55 HP\n" +
					"MANA: 100 MP (+1.5/s)\n" +
					"MOVE SPEED: 60 (Sprint x1.3)\n" +
					"WEAPON: Battle Axe (Melee)\n\n" +
					"SPECIALTY:\n" +
					"Durable frontline fighter with high base health and heavy cleaving strikes.";
			}
			else
			{
				infoHeader.Text = "MAGICIAN — SCHOLAR";
				infoText.Text =
					"HEALTH: 40 HP (+0.2/s)\n" +
					"MANA: 200 MP (+4.5/s)\n" +
					"MOVE SPEED: 55 (Sprint x1.35)\n" +
					"WEAPON: Magic Staff (Piercing)\n\n" +
					"SPECIALTY:\n" +
					"High mana pool, fast mana regen, +15% Ranged Resistance & piercing bolts.";
			}
		};

		updateCardUI();

		for (int i = 0; i < 2; i++)
		{
			int capturedIndex = i;
			cardButtons[i].Pressed += () =>
			{
				if (currentCardIndex == capturedIndex)
				{
					confirmSelection();
				}
				else
				{
					currentCardIndex = capturedIndex;
					updateCardUI();
				}
			};
		}

		// Bottom Bar: BACK button (left) and CONFIRM HERO button (right)
		var btnStyle = new StyleBoxFlat
		{
			BgColor = new Color(0.14f, 0.14f, 0.18f, 0.95f),
			BorderColor = new Color(0.5f, 0.5f, 0.58f),
			BorderWidthLeft = 1,
			BorderWidthTop = 1,
			BorderWidthRight = 1,
			BorderWidthBottom = 1,
			CornerRadiusTopLeft = 3,
			CornerRadiusTopRight = 3,
			CornerRadiusBottomLeft = 3,
			CornerRadiusBottomRight = 3
		};
		var btnHover = btnStyle.Duplicate() as StyleBoxFlat;
		btnHover.BgColor = new Color(0.22f, 0.22f, 0.28f, 0.98f);

		var backBtn = new Button();
		backBtn.Text = "< BACK";
		backBtn.AddThemeFontOverride("font", titleFont);
		backBtn.AddThemeFontSizeOverride("font_size", 18);
		backBtn.Position = new Vector2(22, H - 40);
		backBtn.CustomMinimumSize = new Vector2(95, 28);
		backBtn.Size = new Vector2(95, 28);
		backBtn.AddThemeStyleboxOverride("normal", btnStyle);
		backBtn.AddThemeStyleboxOverride("hover", btnHover);
		backBtn.AddThemeStyleboxOverride("pressed", btnStyle);
		backBtn.AddThemeStyleboxOverride("focus", btnStyle);
		charSelectRoot.AddChild(backBtn);

		backBtn.Pressed += () =>
		{
			charSelectRoot.QueueFree();
			_activeOverlay = null;
			ShowMainMenu();
		};

		var confirmStyle = new StyleBoxFlat
		{
			BgColor = new Color(0.24f, 0.19f, 0.08f, 0.98f),
			BorderColor = new Color(0.98f, 0.80f, 0.22f),
			BorderWidthLeft = 1,
			BorderWidthTop = 1,
			BorderWidthRight = 1,
			BorderWidthBottom = 1,
			CornerRadiusTopLeft = 3,
			CornerRadiusTopRight = 3,
			CornerRadiusBottomLeft = 3,
			CornerRadiusBottomRight = 3
		};
		var confirmHover = confirmStyle.Duplicate() as StyleBoxFlat;
		confirmHover.BgColor = new Color(0.34f, 0.27f, 0.12f, 1.0f);

		var confirmBtn = new Button();
		confirmBtn.Text = "CONFIRM HERO >";
		confirmBtn.AddThemeFontOverride("font", titleFont);
		confirmBtn.AddThemeFontSizeOverride("font_size", 18);
		confirmBtn.AddThemeColorOverride("font_color", new Color(1.0f, 0.92f, 0.6f));
		confirmBtn.Position = new Vector2(W - 162, H - 40);
		confirmBtn.CustomMinimumSize = new Vector2(140, 28);
		confirmBtn.Size = new Vector2(140, 28);
		confirmBtn.AddThemeStyleboxOverride("normal", confirmStyle);
		confirmBtn.AddThemeStyleboxOverride("hover", confirmHover);
		confirmBtn.AddThemeStyleboxOverride("pressed", confirmStyle);
		confirmBtn.AddThemeStyleboxOverride("focus", confirmStyle);
		charSelectRoot.AddChild(confirmBtn);

		confirmBtn.Pressed += confirmSelection;

		ConnectButtonSounds(charSelectRoot);
	}

	private void ShowSlotSelection(bool isLoadMode)
	{
		_isLoadSlotMode = isLoadMode;
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
			bool isMagician = _selectedCharacterName == "Magician";
			SaveManager.ActiveSlotId = slotId;
			SaveManager.CurrentSave = new SaveData {
				SlotId = slotId,
				CharacterName = _selectedCharacterName,
				CurrentLevelIndex = 1,
				CurrentHealth = isMagician ? 40 : 55,
				CurrentMana = isMagician ? 200 : 100,
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
		foreach (Node child in GetChildren())
		{
			if (child is SettingsMenu) return;
		}
		var settings = new SettingsMenu();
		AddChild(settings);
	}

	private void OnExitPressed()
	{
		GetTree().Quit();
	}

	public override void _Input(InputEvent @event)
	{
		foreach (Node child in GetChildren())
		{
			if (child is SettingsMenu && GodotObject.IsInstanceValid(child) && !child.IsQueuedForDeletion())
			{
				return;
			}
		}

		if (@event.IsActionPressed("ui_cancel") && _activeOverlay != null && GodotObject.IsInstanceValid(_activeOverlay))
		{
			string overlayName = _activeOverlay.Name;
			_activeOverlay.QueueFree();
			_activeOverlay = null;
			if (overlayName == "SlotSelectRoot" && !_isLoadSlotMode)
			{
				ShowCharacterSelection();
			}
			else
			{
				ShowMainMenu();
			}
			GetViewport().SetInputAsHandled();
		}
	}
}
