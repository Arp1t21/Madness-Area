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

	private const string ButtonSoundsConnectedMeta = "_ui_sfx_connected";

	public static void PlayUISound(AudioStream stream)
	{
		if (stream == null) return;
		GameResourceCache.PlayPooledSound(Engine.GetMainLoop() as SceneTree, stream, "UI");
	}

	public static void ConnectButtonSounds(Node root)
	{
		if (root == null) return;

		if (!root.HasMeta(ButtonSoundsConnectedMeta))
		{
			if (root is Button btn)
			{
				btn.SetMeta(ButtonSoundsConnectedMeta, true);
				btn.MouseEntered += () => OnButtonHover(btn);
				btn.Pressed += () => OnButtonClick(btn);
			}
			else if (root is Slider slider)
			{
				slider.SetMeta(ButtonSoundsConnectedMeta, true);
				slider.MouseEntered += () => PlayUISound(HoverSound);
				slider.DragStarted += () => PlayUISound(ClickSound);
			}
			else if (root is TabContainer tabContainer)
			{
				tabContainer.SetMeta(ButtonSoundsConnectedMeta, true);
				tabContainer.TabSelected += (_) => PlayUISound(ClickSound);
				tabContainer.TabHovered += (_) => PlayUISound(HoverSound);
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
		// Re-verify all base weapon, mob, and character .tres stats from disk on every startup.
		GameDataRegistry.VerifyAndRefreshInitialStats(forceReload: true);

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

		var background = GetNodeOrNull<Control>("Background");
		if (background != null) {
			background.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
			background.Scale = Vector2.One;
			EnhanceMenuBackground(background, viewportSize);
		}

		_menuRoot = GetNode<Control>("MenuRoot");
		if (_menuRoot != null) {
			_menuRoot.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
			_menuRoot.Scale = Vector2.One;
		}

		BuildStyledMainMenu(viewportSize);

		// Подключаем кнопки	
		GetNode<Button>("MenuRoot/VBoxContainer/PlayButton").Pressed += OnNewGamePressed;
		GetNode<Button>("MenuRoot/VBoxContainer/LoadButton").Pressed += OnLoadPressed;
		var coopBtn = GetNodeOrNull<Button>("MenuRoot/VBoxContainer/CoopButton");
		if (coopBtn != null) coopBtn.Pressed += OnCoopPressed;
		GetNode<Button>("MenuRoot/VBoxContainer/SettingsButton").Pressed += OnSettingsPressed;
		GetNode<Button>("MenuRoot/VBoxContainer/ExitButton").Pressed += OnExitPressed;

		// Подключаем звуки ко всем кнопкам в меню
		ConnectButtonSounds(this);

		if (NetworkManager.Instance != null && !string.IsNullOrEmpty(NetworkManager.Instance.PendingDisconnectNotice))
		{
			string notice = NetworkManager.Instance.PendingDisconnectNotice;
			NetworkManager.Instance.PendingDisconnectNotice = "";
			ShowDisconnectNoticeBanner(notice, viewportSize);
		}
	}	

	private Control _mainTitleContainer = null;
	private Panel _mainMenuPanel = null;
	private Control _activeOverlay = null;
	private ulong _lastClickTime = 0;
	private string _lastClickedSlot = "";
	private Label _infoLabel = null;
	private string _selectedSlotId = "";
	private Button _deleteBtn = null;
	private string _selectedCharacterName = "Knight";
	private bool _isLoadSlotMode = false;

	private void EnhanceMenuBackground(Control background, Vector2 viewSize)
	{
		// Rich dungeon darkness & vignette over tiled stone wall
		var shade = new ColorRect
		{
			Color = new Color(0.03f, 0.03f, 0.05f, 0.56f),
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		shade.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		background.AddChild(shade);

		var vignette = new ColorRect
		{
			Material = DungeonVisuals.GetVignetteMaterial(0.85f, 0.62f),
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		vignette.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		background.AddChild(vignette);

		// Warm dungeon brazier glows & rising embers flanking the menu
		Texture2D glowTex = GameResourceCache.GetRadialGlowTexture(
			new Color(1.0f, 0.62f, 0.20f, 0.34f),
			new Color(0.95f, 0.35f, 0.08f, 0.12f),
			0.45f,
			112,
			new Vector2(0.85f, 0.85f));

		float[] torchXs = { viewSize.X * 0.16f, viewSize.X * 0.84f };
		float torchY = viewSize.Y * 0.42f;
		Texture2D torchTex = DungeonVisuals.GetPixelTorchTexture();
		foreach (float tx in torchXs)
		{
			var glow = new TextureRect
			{
				Texture = glowTex,
				Size = new Vector2(248, 248),
				Position = new Vector2(tx - 124, torchY - 124),
				Modulate = new Color(1f, 0.88f, 0.65f, 0.72f),
				MouseFilter = Control.MouseFilterEnum.Ignore
			};
			background.AddChild(glow);

			// Physical pixel-art wall sconce + flickering torch mounted on the brick wall
			var torchRect = new TextureRect
			{
				Texture = torchTex,
				TextureFilter = Control.TextureFilterEnum.Nearest,
				ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
				StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
				Position = new Vector2(tx - 24, torchY - 24),
				Size = new Vector2(48, 52),
				Material = GameResourceCache.GetUnshadedMaterial(),
				MouseFilter = Control.MouseFilterEnum.Ignore
			};
			background.AddChild(torchRect);

			var flameTween = torchRect.CreateTween().SetLoops();
			flameTween.TweenProperty(torchRect, "modulate", new Color(1.0f, 0.90f, 0.78f, 1.0f), 0.35f).SetTrans(Tween.TransitionType.Sine);
			flameTween.TweenProperty(torchRect, "modulate", new Color(1.0f, 1.0f, 1.0f, 1.0f), 0.42f).SetTrans(Tween.TransitionType.Sine);

			var tween = glow.CreateTween().SetLoops();
			tween.TweenProperty(glow, "modulate:a", 0.48f, 0.75f).SetTrans(Tween.TransitionType.Sine);
			tween.TweenProperty(glow, "modulate:a", 0.84f, 0.65f).SetTrans(Tween.TransitionType.Sine);

			var embers = new CpuParticles2D
			{
				Position = new Vector2(tx, torchY - 12),
				Texture = DungeonVisuals.GetPixelSquareTexture(),
				Amount = 12,
				Lifetime = 2.4f,
				Direction = Vector2.Up,
				Spread = 28.0f,
				Gravity = new Vector2(0f, -12f),
				InitialVelocityMin = 12f,
				InitialVelocityMax = 28f,
				ScaleAmountMin = 0.8f,
				ScaleAmountMax = 1.4f,
				Color = new Color(1.0f, 0.68f, 0.22f, 0.85f)
			};
			var grad = new Gradient();
			grad.SetColor(0, new Color(1.0f, 0.86f, 0.38f, 0.9f));
			grad.SetColor(1, new Color(0.9f, 0.25f, 0.08f, 0f));
			embers.ColorRamp = grad;
			background.AddChild(embers);
		}
	}

	private void BuildStyledMainMenu(Vector2 viewSize)
	{
		if (_menuRoot == null) return;

		var silverFont = GameResourceCache.GetSilverFont();
		var mainFont = GameResourceCache.GetForwardFont();

		const float panelW = 264.0f;
		const float panelH = 256.0f;
		float panelTop = Mathf.Max(136.0f, (viewSize.Y - panelH) * 0.56f);
		float panelLeft = (viewSize.X - panelW) * 0.5f;

		// Sculpted Title Header
		_mainTitleContainer = new VBoxContainer
		{
			Position = new Vector2(0, Mathf.Max(16.0f, panelTop - 108.0f)),
			Size = new Vector2(viewSize.X, 100.0f),
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		_mainTitleContainer.AddThemeConstantOverride("separation", 2);
		_menuRoot.AddChild(_mainTitleContainer);

		var badgeLabel = new Label
		{
			Text = "- DUNGEON ARENA ROGUELIKE -",
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center
		};
		badgeLabel.AddThemeFontOverride("font", mainFont);
		badgeLabel.AddThemeFontSizeOverride("font_size", 10);
		badgeLabel.AddThemeColorOverride("font_color", DungeonVisuals.BrassDim.Lightened(0.35f));
		DungeonVisuals.ApplyPixelOutline(badgeLabel, 1);
		_mainTitleContainer.AddChild(badgeLabel);

		var titleLabel = new Label
		{
			Text = "MADNESS AREA",
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center
		};
		titleLabel.AddThemeFontOverride("font", silverFont);
		titleLabel.AddThemeFontSizeOverride("font_size", 68);
		titleLabel.AddThemeColorOverride("font_color", DungeonVisuals.BrassGold);
		DungeonVisuals.ApplyPixelOutline(titleLabel, 5, new Color(0.05f, 0.03f, 0.02f, 1f), 3, 3);
		_mainTitleContainer.AddChild(titleLabel);

		var crestRule = new ColorRect
		{
			CustomMinimumSize = new Vector2(220, 2),
			SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
			Color = new Color(DungeonVisuals.BrassGold.R, DungeonVisuals.BrassGold.G, DungeonVisuals.BrassGold.B, 0.55f),
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		_mainTitleContainer.AddChild(crestRule);

		// Subtle controls footer hint
		var footerHint = new Label
		{
			Text = "WASD MOVE   •   LMB ATTACK   •   Q / V ABILITIES   •   E INTERACT",
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			Position = new Vector2(0, viewSize.Y - 28.0f),
			Size = new Vector2(viewSize.X, 20.0f),
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		footerHint.AddThemeFontOverride("font", mainFont);
		footerHint.AddThemeFontSizeOverride("font_size", 8);
		footerHint.AddThemeColorOverride("font_color", new Color(0.55f, 0.58f, 0.65f, 0.72f));
		DungeonVisuals.ApplyPixelOutline(footerHint, 1);
		footerHint.Name = "MainFooterHint";
		_menuRoot.AddChild(footerHint);

		// Obsidian & Brass Card framing VBoxContainer
		_mainMenuPanel = new Panel
		{
			Position = new Vector2(panelLeft, panelTop),
			Size = new Vector2(panelW, panelH),
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		_mainMenuPanel.AddThemeStyleboxOverride("panel",
			DungeonVisuals.CreatePanelStyle(DungeonVisuals.ObsidianDeep, DungeonVisuals.BrassDim, 3, 6, 14));
		DungeonVisuals.AttachFrameAccent(_mainMenuPanel, DungeonVisuals.BrassGold, 6);
		_menuRoot.AddChild(_mainMenuPanel);

		var vbox = GetNode<VBoxContainer>("MenuRoot/VBoxContainer");
		if (vbox != null)
		{
			if (vbox.GetNodeOrNull<Button>("CoopButton") == null)
			{
				var coopButton = new Button { Name = "CoopButton", Text = "CO-OP" };
				vbox.AddChild(coopButton);
				vbox.MoveChild(coopButton, Math.Min(2, vbox.GetChildCount() - 1));
			}

			vbox.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopLeft);
			vbox.Position = new Vector2(panelLeft + 20.0f, panelTop + 17.0f);
			vbox.Size = new Vector2(panelW - 40.0f, panelH - 34.0f);
			vbox.AddThemeConstantOverride("separation", 9);
			vbox.ZIndex = 2;

			var btnSpecs = new System.Collections.Generic.Dictionary<string, (string Label, Color Accent)>
			{
				{ "PlayButton", ("NEW EXPEDITION", DungeonVisuals.BrassGold) },
				{ "LoadButton", ("CONTINUE / LOAD", DungeonVisuals.ArcaneCyan) },
				{ "CoopButton", ("CO-OP LOBBY", DungeonVisuals.Emerald) },
				{ "SettingsButton", ("SETTINGS", DungeonVisuals.IronBorder.Lightened(0.35f)) },
				{ "ExitButton", ("QUIT TO DESKTOP", DungeonVisuals.Crimson) }
			};

			foreach (var node in vbox.GetChildren())
			{
				if (node is Button btn)
				{
					var spec = btnSpecs.TryGetValue(btn.Name, out var s)
						? s
						: (Label: btn.Text.ToUpper(), Accent: DungeonVisuals.BrassGold);
					btn.Text = spec.Label;
					btn.CustomMinimumSize = new Vector2(panelW - 40.0f, 36.0f);
					DungeonVisuals.StyleButton(btn, spec.Accent, silverFont, 26, 5, DungeonVisuals.ObsidianCard, 12, 4);
				}
			}
		}
	}

	private void OnNewGamePressed()
	{
		ShowCharacterSelection();
	}

	private void OnLoadPressed()
	{
		ShowSlotSelection(true);
	}

	private void OnCoopPressed()
	{
		HideMainMenu();
		var lobby = new CoopLobbyOverlay(() =>
		{
			_activeOverlay = null;
			ShowMainMenu();
		});
		_menuRoot.AddChild(lobby);
		_activeOverlay = lobby;
	}

	private void ShowDisconnectNoticeBanner(string message, Vector2 viewSize)
	{
		var mainFont = GameResourceCache.GetSilverFont();
		var banner = new PanelContainer
		{
			Position = new Vector2(Mathf.Max(20f, (viewSize.X - 420f) * 0.5f), 14f),
			CustomMinimumSize = new Vector2(420f, 34f),
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		banner.AddThemeStyleboxOverride("panel", DungeonVisuals.CreatePanelStyle(
			new Color(0.18f, 0.05f, 0.06f, 0.94f), DungeonVisuals.Crimson, 2, 4, 6));
		var lbl = new Label
		{
			Text = message,
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center
		};
		if (mainFont != null) lbl.AddThemeFontOverride("font", mainFont);
		lbl.AddThemeFontSizeOverride("font_size", 20);
		lbl.AddThemeColorOverride("font_color", DungeonVisuals.Parchment);
		banner.AddChild(lbl);
		_menuRoot.AddChild(banner);

		var tw = banner.CreateTween();
		tw.TweenInterval(4.0);
		tw.TweenProperty(banner, "modulate:a", 0.0f, 0.6);
		tw.TweenCallback(Callable.From(banner.QueueFree));
	}

	private void ShowCharacterSelection()
	{
		HideMainMenu();

		var charSelectRoot = new Control();
		charSelectRoot.Name = "CharSelectRoot";
		charSelectRoot.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		_menuRoot.AddChild(charSelectRoot);
		_activeOverlay = charSelectRoot;

		var viewSize = GetViewport().GetVisibleRect().Size;
		float W = viewSize.X;
		float H = viewSize.Y;

		// Atmospheric dark dungeon background with vignette
		var bgOverlay = new ColorRect
		{
			Color = new Color(0.03f, 0.032f, 0.05f, 0.94f)
		};
		charSelectRoot.AddChild(bgOverlay);
		bgOverlay.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

		var vig = new ColorRect
		{
			Material = DungeonVisuals.GetVignetteMaterial(0.75f, 0.55f),
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		vig.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		charSelectRoot.AddChild(vig);

		var titleFont = GameResourceCache.GetSilverFont();
		var knightData = GameDataRegistry.LoadFreshPlayer("res://data/characters/knight.tres") ?? GD.Load<PlayerData>("res://data/characters/knight.tres");
		var magicianData = GameDataRegistry.LoadFreshPlayer("res://data/characters/magician.tres") ?? GD.Load<PlayerData>("res://data/characters/magician.tres");
		var rogueData = GameDataRegistry.LoadFreshPlayer("res://data/characters/rogue.tres") ?? GD.Load<PlayerData>("res://data/characters/rogue.tres");
		var berserkerData = GameDataRegistry.LoadFreshPlayer("res://data/characters/berserker.tres") ?? GD.Load<PlayerData>("res://data/characters/berserker.tres");

		// Title
		var title = new Label
		{
			Text = "CHOOSE YOUR HERO",
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			Position = new Vector2(0, 14),
			Size = new Vector2(W, 46)
		};
		title.AddThemeFontOverride("font", titleFont);
		title.AddThemeFontSizeOverride("font_size", 48);
		title.AddThemeColorOverride("font_color", DungeonVisuals.BrassGold);
		DungeonVisuals.ApplyPixelOutline(title, 3);
		charSelectRoot.AddChild(title);

		var subTitle = new Label
		{
			Text = "Select a hero card to inspect stats, or double-click to embark",
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			Position = new Vector2(0, 60),
			Size = new Vector2(W, 28)
		};
		subTitle.AddThemeFontOverride("font", titleFont);
		subTitle.AddThemeFontSizeOverride("font_size", 22);
		subTitle.AddThemeColorOverride("font_color", DungeonVisuals.MutedSlate);
		charSelectRoot.AddChild(subTitle);

		const int heroCount = 4;
		float cardsStartX = 24.0f;
		float cardsY = 95.0f;
		float cardH = Mathf.Clamp(H - 178.0f, 206.0f, 346.0f);
		float cardW = Mathf.Clamp((W - 368.0f) / heroCount - 10.0f, 112.0f, 136.0f);
		float cardGap = 10.0f;

		Button[] cardButtons = new Button[heroCount];
		Label[] statusLabels = new Label[heroCount];
		string[] internalNames = { "Knight", "Magician", "Rogue", "Berserker" };
		string[] cardNames = { "KNIGHT", "MAGICIAN", "ROGUE", "BERSERKER" };
		string[] cardRoles = { "VANGUARD", "SCHOLAR", "ASSASSIN", "WARLORD" };
		Color[] heroAccents =
		{
			new Color(1.0f, 0.72f, 0.24f), // Knight: warm brass
			new Color(0.32f, 0.80f, 1.0f), // Magician: arcane cyan
			new Color(0.34f, 0.92f, 0.56f), // Rogue: emerald
			new Color(0.96f, 0.34f, 0.38f)  // Berserker: fury crimson
		};
		SpriteFrames[] cardAnims =
		{
			knightData?.CharacterAnimations,
			magicianData?.CharacterAnimations,
			rogueData?.CharacterAnimations,
			berserkerData?.CharacterAnimations
		};

		for (int i = 0; i < heroCount; i++)
		{
			Color accent = heroAccents[i];
			var btn = new Button
			{
				Position = new Vector2(cardsStartX + i * (cardW + cardGap), cardsY),
				Size = new Vector2(cardW, cardH),
				CustomMinimumSize = new Vector2(cardW, cardH),
				ClipContents = true,
				MouseDefaultCursorShape = Control.CursorShape.PointingHand
			};
			charSelectRoot.AddChild(btn);
			cardButtons[i] = btn;

			var roleLbl = new Label
			{
				Text = cardRoles[i],
				HorizontalAlignment = HorizontalAlignment.Center,
				VerticalAlignment = VerticalAlignment.Center,
				Position = new Vector2(0, 12),
				Size = new Vector2(cardW, 24)
			};
			roleLbl.AddThemeFontOverride("font", titleFont);
			roleLbl.AddThemeFontSizeOverride("font_size", 19);
			roleLbl.AddThemeColorOverride("font_color", accent);
			DungeonVisuals.ApplyPixelOutline(roleLbl, 2);
			btn.AddChild(roleLbl);

			float portraitH = Mathf.Clamp(cardH - 168.0f, 90.0f, 168.0f);
			var portraitBg = new Panel
			{
				MouseFilter = Control.MouseFilterEnum.Ignore,
				Position = new Vector2(14, 42),
				Size = new Vector2(cardW - 28, portraitH)
			};
			portraitBg.AddThemeStyleboxOverride("panel", DungeonVisuals.CreatePanelStyle(
				DungeonVisuals.ObsidianDeep,
				new Color(accent.R, accent.G, accent.B, 0.28f),
				2, 5, 4));
			btn.AddChild(portraitBg);

			// Radial class pedestal glow + stone plinth behind animated sprite
			var auraRect = new TextureRect
			{
				MouseFilter = Control.MouseFilterEnum.Ignore,
				Texture = GameResourceCache.GetRadialGlowTexture(
					new Color(accent.R, accent.G, accent.B, 0.34f),
					new Color(accent.R, accent.G, accent.B, 0.08f),
					0.5f, 56, new Vector2(0.9f, 0.9f)),
				Position = new Vector2((cardW - 28 - 91) * 0.5f, (portraitH - 91) * 0.5f + 4),
				Size = new Vector2(91, 91)
			};
			portraitBg.AddChild(auraRect);

			var plinth = new ColorRect
			{
				MouseFilter = Control.MouseFilterEnum.Ignore,
				Color = new Color(accent.R * 0.25f + 0.10f, accent.G * 0.25f + 0.10f, accent.B * 0.25f + 0.12f, 0.80f),
				Position = new Vector2((cardW - 28) * 0.5f - 28, portraitH * 0.72f),
				Size = new Vector2(56, 5)
			};
			portraitBg.AddChild(plinth);

			if (cardAnims[i] != null)
			{
				var animSprite = new AnimatedSprite2D
				{
					SpriteFrames = cardAnims[i],
					Position = new Vector2(cardW * 0.5f, 42 + portraitH * 0.50f),
					Scale = new Vector2(4.0f, 4.0f),
					TextureFilter = CanvasItem.TextureFilterEnum.Nearest
				};
				btn.AddChild(animSprite);
				if (animSprite.SpriteFrames.HasAnimation("idle"))
				{
					animSprite.Play("idle");
				}
			}

			var nameLbl = new Label
			{
				Text = cardNames[i],
				HorizontalAlignment = HorizontalAlignment.Center,
				VerticalAlignment = VerticalAlignment.Center,
				Position = new Vector2(0, 42 + portraitH + 18),
				Size = new Vector2(cardW, 35)
			};
			nameLbl.AddThemeFontOverride("font", titleFont);
			nameLbl.AddThemeFontSizeOverride("font_size", 28);
			nameLbl.AddThemeColorOverride("font_color", DungeonVisuals.Parchment);
			DungeonVisuals.ApplyPixelOutline(nameLbl, 2);
			btn.AddChild(nameLbl);

			var statusLbl = new Label
			{
				HorizontalAlignment = HorizontalAlignment.Center,
				VerticalAlignment = VerticalAlignment.Center,
				Position = new Vector2(0, cardH - 49),
				Size = new Vector2(cardW, 32)
			};
			statusLbl.AddThemeFontOverride("font", titleFont);
			statusLbl.AddThemeFontSizeOverride("font_size", 19);
			btn.AddChild(statusLbl);
			statusLabels[i] = statusLbl;
		}

		// Right-side Hero Info Panel
		float infoX = cardsStartX + heroCount * cardW + (heroCount - 1) * cardGap + 18.0f;
		var infoPanel = new Panel
		{
			Position = new Vector2(infoX, cardsY),
			Size = new Vector2(Mathf.Max(245.0f, W - infoX - 24.0f), cardH)
		};
		charSelectRoot.AddChild(infoPanel);

		var infoStyle = DungeonVisuals.CreatePanelStyle(
			DungeonVisuals.ObsidianPanel, DungeonVisuals.BrassDim, 3, 6, 10);
		infoPanel.AddThemeStyleboxOverride("panel", infoStyle);
		DungeonVisuals.AttachFrameAccent(infoPanel, DungeonVisuals.BrassGold, 6);

		var infoHeader = new Label
		{
			VerticalAlignment = VerticalAlignment.Center,
			Position = new Vector2(20, 14),
			Size = new Vector2(infoPanel.Size.X - 40, 38)
		};
		infoHeader.AddThemeFontOverride("font", titleFont);
		infoHeader.AddThemeFontSizeOverride("font_size", 32);
		infoHeader.AddThemeColorOverride("font_color", DungeonVisuals.BrassGold);
		DungeonVisuals.ApplyPixelOutline(infoHeader, 2);
		infoPanel.AddChild(infoHeader);

		var divider = new ColorRect
		{
			Position = new Vector2(20, 52),
			Size = new Vector2(infoPanel.Size.X - 40, 2.0f),
			Color = new Color(DungeonVisuals.BrassDim.R, DungeonVisuals.BrassDim.G, DungeonVisuals.BrassDim.B, 0.7f),
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		infoPanel.AddChild(divider);

		var infoVBox = new VBoxContainer
		{
			Position = new Vector2(18, 60),
			Size = new Vector2(infoPanel.Size.X - 36, cardH - 68),
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		infoVBox.AddThemeConstantOverride("separation", 5);
		infoPanel.AddChild(infoVBox);

		(Label valLbl, ProgressBar bar) CreateHeroStatRow(string statTitle, Color barCol, float maxVal)
		{
			var colBox = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
			colBox.AddThemeConstantOverride("separation", 1);

			var topRow = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
			var kLbl = new Label
			{
				Text = statTitle,
				VerticalAlignment = VerticalAlignment.Center,
				SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
				MouseFilter = Control.MouseFilterEnum.Ignore
			};
			kLbl.AddThemeFontOverride("font", titleFont);
			kLbl.AddThemeFontSizeOverride("font_size", 18);
			kLbl.AddThemeColorOverride("font_color", DungeonVisuals.MutedSlate);
			DungeonVisuals.ApplyPixelOutline(kLbl, 1);
			topRow.AddChild(kLbl);

			var vLbl = new Label
			{
				HorizontalAlignment = HorizontalAlignment.Right,
				VerticalAlignment = VerticalAlignment.Center,
				MouseFilter = Control.MouseFilterEnum.Ignore
			};
			vLbl.AddThemeFontOverride("font", titleFont);
			vLbl.AddThemeFontSizeOverride("font_size", 18);
			vLbl.AddThemeColorOverride("font_color", barCol.Lightened(0.25f));
			DungeonVisuals.ApplyPixelOutline(vLbl, 1);
			topRow.AddChild(vLbl);
			colBox.AddChild(topRow);

			var pBar = new ProgressBar
			{
				MinValue = 0,
				MaxValue = maxVal,
				ShowPercentage = false,
				CustomMinimumSize = new Vector2(0, 7),
				MouseFilter = Control.MouseFilterEnum.Ignore
			};
			DungeonVisuals.StyleProgressBar(
				pBar,
				barCol,
				new Color(0.04f, 0.04f, 0.06f, 0.95f),
				new Color(barCol.R * 0.45f, barCol.G * 0.45f, barCol.B * 0.45f, 0.85f),
				1, 2);
			colBox.AddChild(pBar);
			infoVBox.AddChild(colBox);
			return (vLbl, pBar);
		}

		var hpStat = CreateHeroStatRow("VITALITY (HP)", DungeonVisuals.Crimson, 80.0f);
		var mpStat = CreateHeroStatRow("ARCANE POOL (MP)", DungeonVisuals.ManaBlue, 180.0f);
		var spdStat = CreateHeroStatRow("AGILITY (SPEED)", DungeonVisuals.Emerald, 85.0f);

		var arsenalCard = new PanelContainer
		{
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		var arsenalStyle = DungeonVisuals.CreatePillStyle(
			DungeonVisuals.ObsidianDeep, DungeonVisuals.IronBorder, 1, 4, 10, 5);
		arsenalCard.AddThemeStyleboxOverride("panel", arsenalStyle);
		infoVBox.AddChild(arsenalCard);

		var infoText = new Label
		{
			HorizontalAlignment = HorizontalAlignment.Left,
			VerticalAlignment = VerticalAlignment.Top,
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		infoText.AddThemeFontOverride("font", titleFont);
		infoText.AddThemeFontSizeOverride("font_size", 19);
		infoText.AddThemeColorOverride("font_color", DungeonVisuals.Parchment);
		infoText.AddThemeConstantOverride("line_spacing", -2);
		arsenalCard.AddChild(infoText);

		var specialtyLbl = new Label
		{
			HorizontalAlignment = HorizontalAlignment.Left,
			VerticalAlignment = VerticalAlignment.Top,
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill,
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		specialtyLbl.AddThemeFontOverride("font", titleFont);
		specialtyLbl.AddThemeFontSizeOverride("font_size", 19);
		specialtyLbl.AddThemeColorOverride("font_color", new Color(0.84f, 0.88f, 0.95f));
		specialtyLbl.AddThemeConstantOverride("line_spacing", -2);
		infoVBox.AddChild(specialtyLbl);

		int currentCardIndex = 0;
		for (int i = 0; i < heroCount; i++)
		{
			if (string.Equals(_selectedCharacterName, internalNames[i], StringComparison.OrdinalIgnoreCase))
			{
				currentCardIndex = i;
				break;
			}
		}

		Action confirmSelection = () =>
		{
			_selectedCharacterName = internalNames[currentCardIndex];
			charSelectRoot.QueueFree();
			_activeOverlay = null;
			ShowSlotSelection(false);
		};

		Action updateCardUI = () =>
		{
			for (int i = 0; i < heroCount; i++)
			{
				bool isSel = (i == currentCardIndex);
				Color accent = heroAccents[i];
				var normalStyle = DungeonVisuals.CreatePanelStyle(
					isSel ? new Color(accent.R * 0.14f + 0.05f, accent.G * 0.14f + 0.05f, accent.B * 0.14f + 0.07f, 0.98f) : DungeonVisuals.ObsidianPanel,
					isSel ? accent : DungeonVisuals.IronBorder,
					isSel ? 3 : 2, 6, isSel ? 10 : 4,
					isSel ? new Color(accent.R, accent.G, accent.B, 0.3f) : null);
				var hoverStyle = normalStyle.Duplicate() as StyleBoxFlat;
				hoverStyle.BgColor = normalStyle.BgColor.Lightened(0.08f);
				hoverStyle.BorderColor = accent;

				cardButtons[i].AddThemeStyleboxOverride("normal", normalStyle);
				cardButtons[i].AddThemeStyleboxOverride("hover", hoverStyle);
				cardButtons[i].AddThemeStyleboxOverride("pressed", hoverStyle);
				cardButtons[i].AddThemeStyleboxOverride("focus", normalStyle);

				statusLabels[i].Text = isSel ? "✦ SELECTED ✦" : "SELECT";
				statusLabels[i].AddThemeColorOverride("font_color", isSel ? accent.Lightened(0.25f) : DungeonVisuals.MutedSlate);
			}

			Color activeAccent = heroAccents[currentCardIndex];
			infoStyle.BorderColor = activeAccent.Darkened(0.15f);
			arsenalStyle.BorderColor = new Color(activeAccent.R, activeAccent.G, activeAccent.B, 0.45f);
			infoHeader.AddThemeColorOverride("font_color", activeAccent.Lightened(0.18f));
			divider.Color = new Color(activeAccent.R, activeAccent.G, activeAccent.B, 0.65f);

			void SetHeroDetails(string header, PlayerData data, string weaponLabel, string rmbLabel, string specialty)
			{
				var inv = System.Globalization.CultureInfo.InvariantCulture;
				float maxHp = data != null ? data.MaxHealth : 55f;
				float maxMp = data != null ? data.MaxMana : 100f;
				float spd = data != null ? data.WalkSpeed : 60f;
				float runMult = data != null ? data.RunMultiplier : 1.5f;

				string hpRegen = data != null && data.HealthRegen > 0.001f
					? $" (+{data.HealthRegen.ToString("0.##", inv)}/s)"
					: "";
				string mpRegen = data != null ? data.ManaRegen.ToString("0.##", inv) : "0";

				infoHeader.Text = header;
				hpStat.valLbl.Text = $"{maxHp.ToString("0", inv)} HP{hpRegen}";
				hpStat.bar.Value = maxHp;

				mpStat.valLbl.Text = $"{maxMp.ToString("0", inv)} MP (+{mpRegen}/s)";
				mpStat.bar.Value = maxMp;

				spdStat.valLbl.Text = $"{spd.ToString("0", inv)} (Sprint x{runMult.ToString("0.##", inv)})";
				spdStat.bar.Value = spd;

				infoText.Text =
					$"{weaponLabel}\n" +
					$"RMB SPECIAL: {rmbLabel}\n" +
					$"ABILITIES: [Q] Fireball  •  [V] Shield Dash";
				specialtyLbl.Text = specialty;
			}

			if (currentCardIndex == 0)
			{
				SetHeroDetails(
					"KNIGHT — VANGUARD",
					knightData,
					"STARTING ARSENAL: Battle Axe",
					"Whirlwind Spin (360° Cleave)",
					"Balanced frontline warrior with high durability, +15% Melee Res, and heavy cleaving strikes.");
			}
			else if (currentCardIndex == 1)
			{
				SetHeroDetails(
					"MAGICIAN — SCHOLAR",
					magicianData,
					"STARTING ARSENAL: Magic Staff",
					"Arcane Bolt Volley (Piercing)",
					"Huge mana pool, fast mana regen, +15% Ranged Res & glowing piercing bolts.");
			}
			else if (currentCardIndex == 2)
			{
				SetHeroDetails(
					"ROGUE — ASSASSIN",
					rogueData,
					"STARTING ARSENAL: Dagger + Revolver",
					"Shadow Step Strike (Backstab)",
					"Fastest runner in the dungeon! Starts with two weapons & +10% All Resist.");
			}
			else
			{
				string bHp = berserkerData != null ? berserkerData.MaxHealth.ToString() : "75";
				SetHeroDetails(
					"BERSERKER — WARLORD",
					berserkerData,
					"STARTING ARSENAL: Double Battle Axe",
					"Returning Boomerang Throw",
					$"Unstoppable dwarf juggernaut with {bHp} HP, passive HP regen & +20% Melee Res.");
			}
		};

		updateCardUI();

		for (int i = 0; i < heroCount; i++)
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
		var backBtn = new Button
		{
			Text = "< BACK",
			Position = new Vector2(28, H - 66),
			CustomMinimumSize = new Vector2(158, 46),
			Size = new Vector2(158, 46)
		};
		DungeonVisuals.StyleButton(backBtn, DungeonVisuals.IronBorder.Lightened(0.35f), titleFont, 28, 5);
		charSelectRoot.AddChild(backBtn);

		backBtn.Pressed += () =>
		{
			charSelectRoot.QueueFree();
			_activeOverlay = null;
			ShowMainMenu();
		};

		var confirmBtn = new Button
		{
			Text = "CONFIRM HERO >",
			Position = new Vector2(W - 273, H - 66),
			CustomMinimumSize = new Vector2(245, 46),
			Size = new Vector2(245, 46)
		};
		DungeonVisuals.StyleButton(confirmBtn, DungeonVisuals.BrassGold, titleFont, 28, 5, new Color(0.18f, 0.14f, 0.06f, 0.98f));
		charSelectRoot.AddChild(confirmBtn);

		confirmBtn.Pressed += confirmSelection;

		ConnectButtonSounds(charSelectRoot);
	}

	private readonly System.Collections.Generic.List<(string SlotId, Button Btn, bool Occupied)> _slotButtons = new();
	private Label _slotHeaderLabel = null;
	private ColorRect _slotDivider = null;
	private Button _launchSlotBtn = null;

	private void ShowSlotSelection(bool isLoadMode)
	{
		_isLoadSlotMode = isLoadMode;
		_slotButtons.Clear();
		HideMainMenu();

		var slotSelectRoot = new Control();
		slotSelectRoot.Name = "SlotSelectRoot";
		slotSelectRoot.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		_menuRoot.AddChild(slotSelectRoot);
		_activeOverlay = slotSelectRoot;

		var viewSize = GetViewport().GetVisibleRect().Size;
		float W = viewSize.X;
		float H = viewSize.Y;

		// Full dark atmospheric backdrop behind slots
		var backdrop = new ColorRect
		{
			Color = new Color(0.03f, 0.032f, 0.05f, 0.94f)
		};
		backdrop.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		slotSelectRoot.AddChild(backdrop);

		var vig = new ColorRect
		{
			Material = DungeonVisuals.GetVignetteMaterial(0.75f, 0.55f),
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		vig.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		slotSelectRoot.AddChild(vig);

		var titleFont = GameResourceCache.GetSilverFont();

		var title = new Label
		{
			Text = isLoadMode ? "LOAD EXPEDITION (23 SLOTS)" : "CHOOSE SAVE SLOT (23 SLOTS)",
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			Position = new Vector2(0, 14),
			Size = new Vector2(W, 46)
		};
		title.AddThemeFontOverride("font", titleFont);
		title.AddThemeFontSizeOverride("font_size", 45);
		title.AddThemeColorOverride("font_color", isLoadMode ? DungeonVisuals.ArcaneCyan : DungeonVisuals.BrassGold);
		DungeonVisuals.ApplyPixelOutline(title, 3);
		slotSelectRoot.AddChild(title);

		float listHeight = Mathf.Max(166.0f, H - 144.0f);

		var listPanel = new Panel
		{
			Position = new Vector2(32, 70),
			Size = new Vector2(318, listHeight)
		};
		listPanel.AddThemeStyleboxOverride("panel", DungeonVisuals.CreatePanelStyle(
			DungeonVisuals.ObsidianDeep, DungeonVisuals.BrassDim, 2, 6, 8));
		DungeonVisuals.AttachFrameAccent(listPanel, DungeonVisuals.BrassDim, 5);
		slotSelectRoot.AddChild(listPanel);

		var scroll = new ScrollContainer
		{
			Position = new Vector2(9, 9),
			Size = new Vector2(300, listHeight - 18),
			HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
		};
		listPanel.AddChild(scroll);

		var vbox = new VBoxContainer
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
		};
		vbox.AddThemeConstantOverride("separation", 7);
		scroll.AddChild(vbox);

		var infoPanel = new Panel
		{
			Position = new Vector2(368, 70),
			Size = new Vector2(Mathf.Max(245.0f, W - 400), listHeight)
		};
		infoPanel.AddThemeStyleboxOverride("panel", DungeonVisuals.CreatePanelStyle(
			DungeonVisuals.ObsidianPanel, DungeonVisuals.BrassDim, 3, 6, 10));
		DungeonVisuals.AttachFrameAccent(infoPanel, DungeonVisuals.BrassGold, 6);
		slotSelectRoot.AddChild(infoPanel);

		_slotHeaderLabel = new Label
		{
			Text = "EXPEDITION CHRONICLE",
			HorizontalAlignment = HorizontalAlignment.Left,
			VerticalAlignment = VerticalAlignment.Center,
			Position = new Vector2(20, 14),
			Size = new Vector2(infoPanel.Size.X - 40, 34)
		};
		_slotHeaderLabel.AddThemeFontOverride("font", titleFont);
		_slotHeaderLabel.AddThemeFontSizeOverride("font_size", 30);
		_slotHeaderLabel.AddThemeColorOverride("font_color", DungeonVisuals.BrassGold);
		DungeonVisuals.ApplyPixelOutline(_slotHeaderLabel, 2);
		infoPanel.AddChild(_slotHeaderLabel);

		_slotDivider = new ColorRect
		{
			Position = new Vector2(20, 50),
			Size = new Vector2(infoPanel.Size.X - 40, 2.0f),
			Color = new Color(DungeonVisuals.BrassDim.R, DungeonVisuals.BrassDim.G, DungeonVisuals.BrassDim.B, 0.65f),
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		infoPanel.AddChild(_slotDivider);

		_infoLabel = new Label
		{
			Text = isLoadMode
				? "No saved expeditions found.\nStart a New Expedition from the main menu!"
				: "Select any slot on the left\nto inspect or overwrite.",
			HorizontalAlignment = HorizontalAlignment.Left,
			VerticalAlignment = VerticalAlignment.Top,
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			Position = new Vector2(20, 62),
			Size = new Vector2(infoPanel.Size.X - 40, listHeight - 122)
		};
		_infoLabel.AddThemeFontOverride("font", titleFont);
		_infoLabel.AddThemeFontSizeOverride("font_size", 23);
		_infoLabel.AddThemeColorOverride("font_color", DungeonVisuals.Parchment);
		_infoLabel.AddThemeConstantOverride("line_spacing", -1);
		infoPanel.AddChild(_infoLabel);

		_launchSlotBtn = new Button
		{
			Text = isLoadMode ? "CONTINUE EXPEDITION >" : "START EXPEDITION >",
			Position = new Vector2(20, listHeight - 54),
			CustomMinimumSize = new Vector2(infoPanel.Size.X - 40, 38),
			Size = new Vector2(infoPanel.Size.X - 40, 38),
			Visible = false
		};
		DungeonVisuals.StyleButton(
			_launchSlotBtn,
			isLoadMode ? DungeonVisuals.ArcaneCyan : DungeonVisuals.BrassGold,
			titleFont, 26, 5,
			isLoadMode ? new Color(0.08f, 0.16f, 0.24f, 0.98f) : new Color(0.18f, 0.14f, 0.06f, 0.98f));
		infoPanel.AddChild(_launchSlotBtn);
		_launchSlotBtn.Pressed += () =>
		{
			if (!string.IsNullOrEmpty(_selectedSlotId))
			{
				LaunchSlot(_selectedSlotId, isLoadMode);
			}
		};

		_selectedSlotId = "";
		string firstOccupiedSlot = "";
		string firstEmptySlot = "";

		// 23 слота сохранения (3 базовых + 20 дополнительных)
		for (int i = 1; i <= 23; i++)
		{
			string slotId = i.ToString();
			var slotSave = SaveManager.LoadSave(slotId);
			bool occupied = slotSave != null;
			if (occupied && string.IsNullOrEmpty(firstOccupiedSlot)) firstOccupiedSlot = slotId;
			if (!occupied && string.IsNullOrEmpty(firstEmptySlot)) firstEmptySlot = slotId;

			var btn = new Button
			{
				CustomMinimumSize = new Vector2(280, 40),
				SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
				Alignment = HorizontalAlignment.Left
			};
			vbox.AddChild(btn);

			if (occupied)
			{
				btn.Text = $"#{slotId}  {slotSave.CharacterName.ToUpper()}  •  FLOOR {slotSave.CurrentLevelIndex}";
				DungeonVisuals.StyleButton(btn, DungeonVisuals.BrassGold, titleFont, 23, 5, DungeonVisuals.ObsidianCard, 14, 4);
			}
			else
			{
				btn.Text = $"#{slotId}  — EMPTY SLOT —";
				if (isLoadMode) btn.Disabled = true;
				DungeonVisuals.StyleButton(btn, DungeonVisuals.IronBorder, titleFont, 23, 5, DungeonVisuals.ObsidianDeep, 14, 4);
			}

			_slotButtons.Add((slotId, btn, occupied));
			btn.Pressed += () => OnSlotClicked(slotId, isLoadMode);
		}

		// BACK button
		var backBtn = new Button
		{
			Text = "< BACK",
			Position = new Vector2(32, H - 56),
			CustomMinimumSize = new Vector2(136, 38),
			Size = new Vector2(136, 38)
		};
		DungeonVisuals.StyleButton(backBtn, DungeonVisuals.IronBorder.Lightened(0.35f), titleFont, 26, 5);
		slotSelectRoot.AddChild(backBtn);

		backBtn.Pressed += () => {
			slotSelectRoot.QueueFree();
			_activeOverlay = null;
			if (isLoadMode)
			{
				ShowMainMenu();
			}
			else
			{
				ShowCharacterSelection();
			}
		};

		// DELETE button in bottom right corner
		_deleteBtn = new Button
		{
			Text = "DELETE SLOT",
			Position = new Vector2(W - 186, H - 56),
			CustomMinimumSize = new Vector2(154, 38),
			Size = new Vector2(154, 38),
			Visible = false
		};
		DungeonVisuals.StyleButton(_deleteBtn, DungeonVisuals.Crimson, titleFont, 24, 5, new Color(0.18f, 0.05f, 0.06f, 0.95f));
		slotSelectRoot.AddChild(_deleteBtn);

		_deleteBtn.Pressed += () => {
			if (!string.IsNullOrEmpty(_selectedSlotId)) {
				SaveManager.DeleteSave(_selectedSlotId);
				slotSelectRoot.QueueFree();
				_activeOverlay = null;
				ShowSlotSelection(isLoadMode);
			}
		};

		string initialSlot = isLoadMode
			? firstOccupiedSlot
			: (!string.IsNullOrEmpty(firstEmptySlot) ? firstEmptySlot : "1");
		if (!string.IsNullOrEmpty(initialSlot))
		{
			_selectedSlotId = initialSlot;
			_lastClickedSlot = initialSlot;
			_lastClickTime = 0;
			RefreshSlotHighlightAndDetails(initialSlot, isLoadMode);
		}

		ConnectButtonSounds(slotSelectRoot);
	}

	private void OnSlotClicked(string slotId, bool isLoadMode)
	{
		ulong now = Time.GetTicksMsec();
		if (_lastClickedSlot == slotId && now - _lastClickTime < 400)
		{
			LaunchSlot(slotId, isLoadMode);
			return;
		}

		_selectedSlotId = slotId;
		_lastClickTime = now;
		_lastClickedSlot = slotId;
		RefreshSlotHighlightAndDetails(slotId, isLoadMode);
	}

	private void RefreshSlotHighlightAndDetails(string slotId, bool isLoadMode)
	{
		var slotSave = SaveManager.LoadSave(slotId);
		bool isOccupied = slotSave != null;
		if (_deleteBtn != null)
		{
			_deleteBtn.Visible = isOccupied;
		}
		if (_launchSlotBtn != null)
		{
			_launchSlotBtn.Visible = !isLoadMode || isOccupied;
			if (!isLoadMode)
			{
				_launchSlotBtn.Text = isOccupied
					? $"OVERWRITE & START AS {_selectedCharacterName.ToUpper()} >"
					: $"START EXPEDITION AS {_selectedCharacterName.ToUpper()} >";
			}
			else
			{
				_launchSlotBtn.Text = $"CONTINUE SLOT #{slotId} >";
			}
		}

		var titleFont = GameResourceCache.GetSilverFont();
		foreach (var entry in _slotButtons)
		{
			if (!GodotObject.IsInstanceValid(entry.Btn)) continue;
			bool isSelected = entry.SlotId == slotId;
			Color accent = isSelected
				? DungeonVisuals.ArcaneCyan
				: (entry.Occupied ? DungeonVisuals.BrassGold : DungeonVisuals.IronBorder);
			Color bg = isSelected
				? new Color(0.14f, 0.18f, 0.26f, 0.98f)
				: (entry.Occupied ? DungeonVisuals.ObsidianCard : DungeonVisuals.ObsidianDeep);
			DungeonVisuals.StyleButton(entry.Btn, accent, titleFont, 23, 5, bg, 14, 4);
		}

		SelectSlotDetails(slotId);
	}

	private void SelectSlotDetails(string slotId)
	{
		if (_infoLabel == null) return;

		var slotSave = SaveManager.LoadSave(slotId);

		if (slotSave != null)
		{
			int mins = (int)(slotSave.PlayedTimeSeconds / 60);
			int secs = (int)(slotSave.PlayedTimeSeconds % 60);
			string timeStr = $"{mins:D2}:{secs:D2}";
			string weapons = GameLogic.FormatWeaponList(slotSave.WeaponResourcePaths);
			int relicCount = slotSave.UnlockedRelics != null ? slotSave.UnlockedRelics.Count : 0;

			if (_slotHeaderLabel != null)
			{
				_slotHeaderLabel.Text = $"SLOT #{slotId}  —  {slotSave.CharacterName.ToUpper()}";
				_slotHeaderLabel.AddThemeColorOverride("font_color", DungeonVisuals.BrassGold);
			}

			_infoLabel.Text =
				$"SAVED: {slotSave.SaveDateString}\n" +
				$"FLOOR REACHED: Floor {slotSave.CurrentLevelIndex}\n" +
				$"PLAYED TIME: {timeStr}   •   GOLD: {slotSave.Coins}\n" +
				$"ABILITIES: [Q] Lv {slotSave.Ability1Level}/4  •  [V] Lv {slotSave.Ability2Level}/4   •   RELICS: {relicCount}\n" +
				$"ARSENAL: {weapons}";
			_infoLabel.HorizontalAlignment = HorizontalAlignment.Left;
		}
		else
		{
			if (_slotHeaderLabel != null)
			{
				_slotHeaderLabel.Text = $"SLOT #{slotId}  —  NEW EXPEDITION";
				_slotHeaderLabel.AddThemeColorOverride("font_color", DungeonVisuals.Emerald);
			}
			_infoLabel.Text =
				$"HERO CHOSEN: {_selectedCharacterName.ToUpper()}\n" +
				$"STARTING FLOOR: Floor 1\n\n" +
				$"Ready to descend into the dungeon.\nClick the button below or double-click the slot to begin!";
			_infoLabel.HorizontalAlignment = HorizontalAlignment.Left;
		}
	}

	private void LaunchSlot(string slotId, bool isLoadMode)
	{
		// Re-check disk .tres stats right before launching a new run or loading a saved floor.
		GameDataRegistry.VerifyAndRefreshInitialStats(forceReload: true);

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
			string charPath = GameLogic.ResolveCharacterDataPath(_selectedCharacterName);

			var charData = GameDataRegistry.LoadFreshPlayer(charPath) ?? GD.Load<PlayerData>(charPath);
			float startHp = charData != null ? charData.MaxHealth : 55f;
			float startMp = charData != null ? charData.MaxMana : 100f;

			SaveManager.ActiveSlotId = slotId;
			SaveManager.CurrentSave = new SaveData {
				SlotId = slotId,
				CharacterName = _selectedCharacterName,
				CurrentLevelIndex = 1,
				CurrentHealth = startHp,
				CurrentMana = startMp,
				SavedBaseMaxHealth = startHp,
				SavedBaseMaxMana = startMp,
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
		if (_mainMenuPanel != null) _mainMenuPanel.Visible = false;
		if (_mainTitleContainer != null) _mainTitleContainer.Visible = false;
		var hint = GetNodeOrNull<Control>("MenuRoot/MainFooterHint");
		if (hint != null) hint.Visible = false;
	}

	private void ShowMainMenu()
	{
		var vbox = GetNodeOrNull<Control>("MenuRoot/VBoxContainer");
		if (vbox != null) vbox.Visible = true;
		if (_mainMenuPanel != null) _mainMenuPanel.Visible = true;
		if (_mainTitleContainer != null) _mainTitleContainer.Visible = true;
		var hint = GetNodeOrNull<Control>("MenuRoot/MainFooterHint");
		if (hint != null) hint.Visible = true;
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
			if (_activeOverlay is CoopLobbyOverlay)
			{
				return;
			}
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
