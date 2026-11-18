using Godot;
using System;

public partial class HUD : CanvasLayer
{
	// Монеты
	private Label _coinLabel;
	
	// Здоровье
	private ProgressBar _healthBar;
	private Label _hpLabel;
	private ColorRect _hpTrailRect;
	private ColorRect _hpSheenRect;
	private float _hpTrailRatio = 1.0f;
	private StyleBoxFlat _hpFillStyle;
	
	// Мана
	private ProgressBar _manaBar;
	private Label _mpLabel;
	private ColorRect _mpSheenRect;
	private Control _healthUI;
	private float _lastVitalsMaxHp = -1.0f;
	private float _lastVitalsMaxMp = -1.0f;
	
	// Таймер
	private Label _timeLabel;
	private Control _announcementBanner;
	private Label _announcementLabel;
	private Control _markersContainer;
	private float _time = 5.0f;
	private bool _isCountingDown = true;
	public bool IsCountingDown => _isCountingDown;

	private Player _player;
	private WaveManager _waveManager;
	private Level _level;
	private string _lastAmmoText = null;
	private Color _lastAmmoColor = Colors.Transparent;
	private string _lastFloorText = null;
	private Color _lastFloorColor = Colors.Transparent;
	private Panel _floorBadgePanel;
	private StyleBoxFlat _floorBadgeStyle;
	private ArrowIndicator _arrowIndicator;
	private ArrowIndicator _shopArrowIndicator;

	private Control _abilitiesContainer;
	private AbilitySlotUI _ability1Slot;
	private AbilitySlotUI _ability2Slot;
	private PanelContainer _skillPointsBadge;
	private Label _skillPointsLabel;
	private Control _hudRoot;

	// Новые элементы HUD: этаж/статус зачистки (сверху справа), слоты оружия (снизу справа), полоска здоровья Босса
	private Label _floorEnemiesLabel;
	private Control _weaponsContainer;
	private WeaponShowcaseSlot[] _weaponSlots = new WeaponShowcaseSlot[3];
	private Label _ammoLabel;
	private Panel _weaponTooltipPanel;
	private Label _weaponTooltipLabel;

	private Control _bossBarContainer;
	private Label _bossNameLabel;
	private ProgressBar _bossHealthBar;

	private Control _relicsContainer;
	private Panel _eventBadgePanel;
	private Label _eventBadgeLabel;
	private Control _madnessContainer;
	private ProgressBar _madnessBar;
	private StyleBoxFlat _madnessFillStyle;
	private Label _madnessLabel;
	private Control _madnessEdgeFrame;
	private PanelContainer _toastContainer;
	private Label _toastLabel;
	private Tween _toastTween;
	private HBoxContainer _statusContainer;
	private string _lastStatusKey = "";

	private VBoxContainer _partyRosterContainer;
	private float _partyRosterTimer = 0.0f;
	private PanelContainer _spectateBanner;
	private Label _spectateLabel;
	private PanelContainer _ladderReadyBanner;
	private Label _ladderReadyLabel;

	private sealed class TabCardEntry
	{
		public PanelContainer Card;
		public StyleBoxFlat CardStyle;
		public string Title;
		public string TooltipTag;
		public string Description;
		public Color Accent;
	}

	private PanelContainer _tabOverlay;
	private VBoxContainer _tabPartySection;
	private VBoxContainer _tabRelicsSection;
	private VBoxContainer _tabUpgradesSection;
	private ColorRect _tabSectionsDivider;
	private Control _upgradesContainer;
	private Label _relicsEmptyLabel;
	private bool _isTabOpen = false;
	private Tween _statsFadeTween;
	private int _lastRelicCount = -1;
	private int _lastShopStacks = -1;
	private readonly System.Collections.Generic.List<TabCardEntry> _tabCardEntries =
		new System.Collections.Generic.List<TabCardEntry>();
	private PanelContainer _tabTooltipPanel;
	private Label _tabTooltipTitleLabel;
	private PanelContainer _tabTooltipTagPill;
	private Label _tabTooltipTagLabel;
	private ColorRect _tabTooltipDivider;
	private Label _tabTooltipDescLabel;
	private PanelContainer _lastHoveredTabCard = null;
	private float _tabTooltipScale = 1.0f;

	private PanelContainer _statsPanel;
	private Label _statDmgLabel;
	private Label _statRofLabel;
	private Label _statCritLabel;
	private Label _statSpdLabel;
	private Label _statHprLabel;
	private Label _statMprLabel;
	private Control _statMadRow;
	private Label _statMadLabel;
	private bool _hasStatsCache = false;
	private bool _lastStatsHasWeapon = false;
	private float _lastStatsWeaponDmg = 0.0f;
	private float _lastStatsWeaponCd = 0.0f;
	private int _lastStatsMultPct = -1;
	private float _lastStatsRateMult = 0.0f;
	private int _lastStatsCritPct = -1;
	private int _lastStatsMoveSpd = -1;
	private float _lastStatsHpr = -1.0f;
	private float _lastStatsMpr = -1.0f;
	private bool _lastStatsMadUnlocked = false;
	private int _lastStatsMadPct = -1;

	public override void _Ready()
	{
		AddToGroup("HUD");
		SettingsManager.ApplyFontQualityFixes();
		_hudRoot = new Control();
		_hudRoot.Name = "HUDRoot";
		_hudRoot.MouseFilter = Control.MouseFilterEnum.Ignore;
		AddChild(_hudRoot);

		_hudRoot.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		_hudRoot.Scale = Vector2.One;

		var timerControl = GetNodeOrNull<Control>("TimerControl");
		if (timerControl != null) {
			RemoveChild(timerControl);
			_hudRoot.AddChild(timerControl);
		}
		var coinControl = GetNodeOrNull<Control>("CoinControl");
		if (coinControl != null) {
			RemoveChild(coinControl);
			_hudRoot.AddChild(coinControl);
		}
		var healthUI = GetNodeOrNull<Control>("HealthUI");
		_healthUI = healthUI;
		if (healthUI != null) {
			RemoveChild(healthUI);
			_hudRoot.AddChild(healthUI);
		}

		_coinLabel = _hudRoot.GetNodeOrNull<Label>("CoinControl/HBoxContainer/CoinLabel");
		if (_coinLabel != null) {
			_coinLabel.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
			DungeonVisuals.ApplyPixelOutline(_coinLabel, 3);
		}

		_healthBar = _hudRoot.GetNodeOrNull<ProgressBar>("HealthUI/HealthBar");
		_hpLabel = _hudRoot.GetNodeOrNull<Label>("HealthUI/HPLabel");
		_manaBar = _hudRoot.GetNodeOrNull<ProgressBar>("HealthUI/ManaBar");
		_mpLabel = _hudRoot.GetNodeOrNull<Label>("HealthUI/MPLabel");
		_timeLabel = _hudRoot.GetNodeOrNull<Label>("TimerControl/Label");
		_markersContainer = _hudRoot.GetNodeOrNull<Control>("HealthUI/HealthBar/Markers");

		var silverFont = GameResourceCache.GetSilverFont();
		StyleTopWidgetsAndVitals(timerControl, coinControl, healthUI, silverFont);

		_player = NetworkManager.GetLocalPlayer(GetTree());
		_waveManager = GetTree().GetFirstNodeInGroup("WaveManager") as WaveManager;
		if (NetworkManager.Instance != null && NetworkManager.Instance.IsCoopSession)
		{
			_isCountingDown = false;
			_time = 5.0f;
		}

		// Драматичный баннер объявления волны (в верхней трети экрана, чтобы не перекрывать героя и врагов по центру)
		_announcementBanner = new Control
		{
			LayoutMode = 1,
			MouseFilter = Control.MouseFilterEnum.Ignore,
			Visible = false
		};
		_announcementBanner.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
		_announcementBanner.AnchorTop = 0.19f;
		_announcementBanner.AnchorBottom = 0.19f;
		_announcementBanner.OffsetLeft = -270;
		_announcementBanner.OffsetRight = 270;
		_announcementBanner.OffsetTop = -18;
		_announcementBanner.OffsetBottom = 18;
		_hudRoot.AddChild(_announcementBanner);

		var bannerPanel = new Panel
		{
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		bannerPanel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		bannerPanel.AddThemeStyleboxOverride("panel", DungeonVisuals.CreatePanelStyle(
			new Color(0.038f, 0.042f, 0.068f, 0.90f),
			DungeonVisuals.BrassGold,
			2, 4, 6,
			new Color(0f, 0f, 0f, 0.65f)));
		_announcementBanner.AddChild(bannerPanel);
		DungeonVisuals.AttachFrameAccent(bannerPanel, DungeonVisuals.BrassGold, 3);

		_announcementLabel = new Label();
		_announcementLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_announcementLabel.VerticalAlignment = VerticalAlignment.Center;
		_announcementLabel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		
		var font = silverFont;
		if (font != null) {
			_announcementLabel.AddThemeFontOverride("font", font);
		}
		_announcementLabel.AddThemeConstantOverride("letter_spacing", 2);

		_announcementLabel.AddThemeFontSizeOverride("font_size", 22);
		_announcementLabel.AddThemeColorOverride("font_color", new Color(1.0f, 0.85f, 0.30f));
		DungeonVisuals.ApplyPixelOutline(_announcementLabel, 3, new Color(0.05f, 0.02f, 0.02f, 1f), 2, 2);
		_announcementLabel.Text = "First wave is coming!";
		_announcementBanner.AddChild(_announcementLabel);
		_announcementLabel.Visible = true;

		// Неблокирующий HUD-тост для реликвий и пробуждения Безумия
		_toastContainer = new PanelContainer
		{
			Name = "HudToast",
			LayoutMode = 1,
			MouseFilter = Control.MouseFilterEnum.Ignore,
			Visible = false
		};
		_toastContainer.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
		_toastContainer.AnchorTop = 0.255f;
		_toastContainer.AnchorBottom = 0.255f;
		_toastContainer.OffsetLeft = -200;
		_toastContainer.OffsetRight = 200;
		_toastContainer.OffsetTop = -14;
		_toastContainer.OffsetBottom = 14;
		_toastContainer.AddThemeStyleboxOverride("panel", DungeonVisuals.CreatePanelStyle(
			new Color(0.04f, 0.05f, 0.08f, 0.90f), DungeonVisuals.BrassGold, 1, 3, 4));
		_hudRoot.AddChild(_toastContainer);

		_toastLabel = new Label
		{
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center
		};
		if (font != null) _toastLabel.AddThemeFontOverride("font", font);
		_toastLabel.AddThemeFontSizeOverride("font_size", 17);
		DungeonVisuals.ApplyPixelOutline(_toastLabel, 2);
		_toastContainer.AddChild(_toastLabel);

		// Полоса активных статусов игрока над HP/MP
		_statusContainer = new HBoxContainer
		{
			Name = "StatusEffectsStrip",
			LayoutMode = 1,
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		_statusContainer.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
		_statusContainer.OffsetLeft = 10;
		_statusContainer.OffsetRight = 320;
		_statusContainer.OffsetTop = -102;
		_statusContainer.OffsetBottom = -80;
		_statusContainer.AddThemeConstantOverride("separation", 5);
		_hudRoot.AddChild(_statusContainer);

		UpdateTimerLabel();

		// Сразу запускаем первую волну, если время уже 0
		if ((NetworkManager.Instance == null || !NetworkManager.Instance.IsCoopSession) && _time <= 0 && Engine.TimeScale > 0) {
			CallDeferred(nameof(StartGame));
		}

		// Создаем стрелку для лестницы (зеленая) и для магазина снабжения (золотая)
		_arrowIndicator = new ArrowIndicator();
		_arrowIndicator.Visible = false;
		_hudRoot.AddChild(_arrowIndicator);

		_shopArrowIndicator = new ArrowIndicator
		{
			Color = new Color(1.0f, 0.84f, 0.18f, 0.95f)
		};
		_shopArrowIndicator.Visible = false;
		_hudRoot.AddChild(_shopArrowIndicator);

		CreateAbilitiesUI();
		CreateFloorAndEnemiesUI(font);
		CreateMadnessAndRelicsUI(font);
		CreateWeaponsUI(font);
		CreateBossBarUI(font);
		CreateCoopPartyAndBannersUI(font);
		CreateStatsPanelUI(font);
		CreateTabOverlayUI(font);
		CallDeferred(nameof(UpdateRelicsUI));
		CallDeferred(nameof(SyncInitialFloorEventUI));
		CallDeferred(nameof(RefreshStatsImmediate));
	}

	private void SyncInitialFloorEventUI()
	{
		var lvl = GetLevelCached();
		if (lvl != null) SetFloorEventUI(lvl.CurrentFloorEvent);
	}

	private void StyleTopWidgetsAndVitals(Control timerControl, Control coinControl, Control healthUI, Font silverFont)
	{
		// Пилюля под таймером (центр сверху)
		if (timerControl != null)
		{
			timerControl.MouseFilter = Control.MouseFilterEnum.Ignore;
			timerControl.OffsetTop = 6;
			timerControl.OffsetBottom = 36;
			timerControl.OffsetLeft = -56;
			timerControl.OffsetRight = 56;
			var timerPill = new Panel
			{
				MouseFilter = Control.MouseFilterEnum.Ignore,
				ZIndex = -1
			};
			timerPill.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
			timerPill.AddThemeStyleboxOverride("panel", DungeonVisuals.CreatePanelStyle(
				new Color(0.05f, 0.055f, 0.085f, 0.88f),
				new Color(0.55f, 0.46f, 0.28f, 0.72f),
				2, 4, 4));
			timerControl.AddChild(timerPill);
			timerControl.MoveChild(timerPill, 0);
			DungeonVisuals.AttachFrameAccent(timerPill, DungeonVisuals.BrassDim, 2);

			if (_timeLabel != null)
			{
				_timeLabel.AddThemeColorOverride("font_color", DungeonVisuals.Parchment);
				DungeonVisuals.ApplyPixelOutline(_timeLabel, 3);
			}
		}

		// Пилюля под монетами (слева сверху)
		if (coinControl != null)
		{
			coinControl.MouseFilter = Control.MouseFilterEnum.Ignore;
			coinControl.OffsetTop = 6;
			coinControl.OffsetBottom = 36;
			coinControl.OffsetLeft = 10;
			coinControl.OffsetRight = 128;
			var coinPill = new Panel
			{
				MouseFilter = Control.MouseFilterEnum.Ignore,
				ZIndex = -1
			};
			coinPill.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
			coinPill.AddThemeStyleboxOverride("panel", DungeonVisuals.CreatePanelStyle(
				new Color(0.05f, 0.055f, 0.085f, 0.88f),
				new Color(0.85f, 0.68f, 0.24f, 0.72f),
				2, 4, 4));
			coinControl.AddChild(coinPill);
			coinControl.MoveChild(coinPill, 0);
			DungeonVisuals.AttachFrameAccent(coinPill, DungeonVisuals.BrassGold, 2);

			if (_coinLabel != null)
			{
				_coinLabel.AddThemeColorOverride("font_color", new Color(1.0f, 0.90f, 0.42f));
			}
		}

		if (healthUI == null) return;
		healthUI.MouseFilter = Control.MouseFilterEnum.Ignore;

		// Темная обсидиановая панель-подложка под HP/MP
		var vitalsCard = new Panel
		{
			MouseFilter = Control.MouseFilterEnum.Ignore,
			ZIndex = -2
		};
		vitalsCard.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		vitalsCard.AddThemeStyleboxOverride("panel", DungeonVisuals.CreatePanelStyle(
			new Color(0.045f, 0.048f, 0.075f, 0.88f),
			new Color(0.45f, 0.42f, 0.34f, 0.76f),
			2, 4, 6));
		healthUI.AddChild(vitalsCard);
		healthUI.MoveChild(vitalsCard, 0);
		DungeonVisuals.AttachFrameAccent(vitalsCard, DungeonVisuals.BrassDim, 2);

		// Иконки сердца и кристалла маны слева от полосок (в декоративных оправах)
		var heartSocket = new Panel
		{
			Position = new Vector2(6, 6),
			Size = new Vector2(20, 20),
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		heartSocket.AddThemeStyleboxOverride("panel", DungeonVisuals.CreatePanelStyle(
			new Color(0.08f, 0.03f, 0.045f, 0.92f),
			new Color(0.68f, 0.26f, 0.32f, 0.75f),
			1, 3, 0));
		healthUI.AddChild(heartSocket);

		var heartTex = GD.Load<Texture2D>("res://assets/objects/ui_heart_full.png");
		if (heartTex != null)
		{
			var heartIcon = new TextureRect
			{
				Texture = heartTex,
				TextureFilter = Control.TextureFilterEnum.Nearest,
				ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
				StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
				Position = new Vector2(8, 8),
				Size = new Vector2(16, 16),
				MouseFilter = Control.MouseFilterEnum.Ignore
			};
			healthUI.AddChild(heartIcon);
		}

		var manaSocket = new Panel
		{
			Position = new Vector2(6, 30),
			Size = new Vector2(20, 20),
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		manaSocket.AddThemeStyleboxOverride("panel", DungeonVisuals.CreatePanelStyle(
			new Color(0.03f, 0.055f, 0.11f, 0.92f),
			new Color(0.24f, 0.52f, 0.85f, 0.75f),
			1, 3, 0));
		healthUI.AddChild(manaSocket);

		var manaTex = GD.Load<Texture2D>("res://assets/objects/mana_crystal.png");
		if (manaTex != null)
		{
			var manaIcon = new TextureRect
			{
				Texture = manaTex,
				TextureFilter = Control.TextureFilterEnum.Nearest,
				ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
				StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
				Position = new Vector2(8, 32),
				Size = new Vector2(16, 16),
				MouseFilter = Control.MouseFilterEnum.Ignore
			};
			healthUI.AddChild(manaIcon);
		}

		// Полоса HP с белым следом получаемого урона (White Damage Trail) и бликом только на заполненной части
		if (_healthBar != null)
		{
			var hpBgPanel = new Panel
			{
				MouseFilter = Control.MouseFilterEnum.Ignore,
				Position = _healthBar.Position,
				Size = _healthBar.Size,
				ZIndex = -1
			};
			hpBgPanel.AddThemeStyleboxOverride("panel", DungeonVisuals.CreatePanelStyle(
				new Color(0.10f, 0.035f, 0.05f, 0.96f),
				new Color(0.42f, 0.18f, 0.24f, 0.92f),
				2, 3, 2));
			healthUI.AddChild(hpBgPanel);
			healthUI.MoveChild(hpBgPanel, 1);

			_hpTrailRect = new ColorRect
			{
				Color = new Color(1.0f, 0.92f, 0.72f, 0.85f),
				Position = new Vector2(2, 2),
				Size = new Vector2(Mathf.Max(0, _healthBar.Size.X - 4), Mathf.Max(2, _healthBar.Size.Y - 4)),
				MouseFilter = Control.MouseFilterEnum.Ignore
			};
			hpBgPanel.AddChild(_hpTrailRect);

			_hpFillStyle = new StyleBoxFlat
			{
				BgColor = new Color(0.88f, 0.18f, 0.24f),
				CornerRadiusTopLeft = 2,
				CornerRadiusBottomLeft = 2
			};
			_healthBar.AddThemeStyleboxOverride("background", new StyleBoxEmpty());
			_healthBar.AddThemeStyleboxOverride("fill", _hpFillStyle);

			_hpSheenRect = new ColorRect
			{
				Color = new Color(1f, 0.88f, 0.88f, 0.28f),
				Position = new Vector2(2, 2),
				Size = new Vector2(Mathf.Max(0, _healthBar.Size.X - 4), 2),
				MouseFilter = Control.MouseFilterEnum.Ignore
			};
			_healthBar.AddChild(_hpSheenRect);
		}

		if (_manaBar != null)
		{
			DungeonVisuals.StyleProgressBar(
				_manaBar,
				new Color(0.18f, 0.58f, 0.98f),
				new Color(0.04f, 0.07f, 0.15f, 0.96f),
				new Color(0.20f, 0.38f, 0.64f, 0.92f),
				2, 3);

			_mpSheenRect = new ColorRect
			{
				Color = new Color(0.85f, 0.96f, 1f, 0.28f),
				Position = new Vector2(2, 2),
				Size = new Vector2(Mathf.Max(0, _manaBar.Size.X - 4), 2),
				MouseFilter = Control.MouseFilterEnum.Ignore
			};
			_manaBar.AddChild(_mpSheenRect);
		}

		if (_hpLabel != null)
		{
			_hpLabel.AddThemeColorOverride("font_color", new Color(1.0f, 0.86f, 0.86f));
			DungeonVisuals.ApplyPixelOutline(_hpLabel, 3);
		}
		if (_mpLabel != null)
		{
			_mpLabel.AddThemeColorOverride("font_color", new Color(0.75f, 0.90f, 1.0f));
			DungeonVisuals.ApplyPixelOutline(_mpLabel, 3);
		}

		UpdateVitalsPanelWidth(100.0f, 100.0f);
		_lastVitalsMaxHp = -1.0f;
		_lastVitalsMaxMp = -1.0f;
	}

	/// <summary>
	/// Fits the vitals card and HP/MP text labels to the rendered width of max HP/MP strings
	/// so no trailing blank gap appears while expanding automatically for larger numbers.
	/// </summary>
	private void UpdateVitalsPanelWidth(float maxHp, float maxMp)
	{
		if (!IsInstanceValid(_healthUI) || !IsInstanceValid(_hpLabel) || !IsInstanceValid(_mpLabel)) return;
		int boundedHp = Mathf.Max(0, (int)maxHp);
		int boundedMp = Mathf.Max(0, (int)maxMp);
		if (boundedHp == (int)_lastVitalsMaxHp && boundedMp == (int)_lastVitalsMaxMp) return;

		_lastVitalsMaxHp = boundedHp;
		_lastVitalsMaxMp = boundedMp;

		Font font = _hpLabel.GetThemeFont("font");
		int fontSize = _hpLabel.GetThemeFontSize("font_size");
		string hpRef = $"{boundedHp}/{boundedHp}";
		string mpRef = $"{boundedMp}/{boundedMp}";
		float hpW = font != null ? font.GetStringSize(hpRef, HorizontalAlignment.Left, -1, fontSize).X : hpRef.Length * 9.0f;
		float mpW = font != null ? font.GetStringSize(mpRef, HorizontalAlignment.Left, -1, fontSize).X : mpRef.Length * 9.0f;
		float labelWidth = Mathf.Ceil(Mathf.Max(hpW, mpW)) + 4.0f;

		const float labelStartX = 132.0f;
		const float rightMargin = 6.0f;
		_hpLabel.OffsetLeft = labelStartX;
		_hpLabel.OffsetRight = labelStartX + labelWidth;
		_mpLabel.OffsetLeft = labelStartX;
		_mpLabel.OffsetRight = labelStartX + labelWidth;

		_healthUI.OffsetRight = _healthUI.OffsetLeft + labelStartX + labelWidth + rightMargin;
	}

	public override void _Process(double delta)
	{
		if (Engine.TimeScale == 0) return;

		if (!IsInstanceValid(_player) || _player.IsQueuedForDeletion())
		{
			_player = NetworkManager.GetLocalPlayer(GetTree());
		}

		// Обновление статов игрока
		if (IsInstanceValid(_player))
		{
			if (IsInstanceValid(_coinLabel)) {
				_coinLabel.Text = _player.CoinCount.ToString();
			}

			if (_player.Config != null && IsInstanceValid(_healthBar) && IsInstanceValid(_hpLabel)) {
				float maxHp = _player.MaxHealthEffective;
				_healthBar.MaxValue = maxHp;
				_healthBar.Value = _player.CurrentHealth;
				_hpLabel.Text = $"{(int)_player.CurrentHealth}/{(int)maxHp}";

				float hpRatio = maxHp > 0.01f ? Mathf.Clamp(_player.CurrentHealth / maxHp, 0.0f, 1.0f) : 0.0f;
				if (_hpTrailRatio < hpRatio)
				{
					_hpTrailRatio = hpRatio;
				}
				else
				{
					_hpTrailRatio = Mathf.MoveToward(_hpTrailRatio, hpRatio, (float)delta * 0.45f);
				}
				float innerW = Mathf.Max(0.0f, _healthBar.Size.X - 4.0f);
				if (IsInstanceValid(_hpTrailRect))
				{
					_hpTrailRect.Size = new Vector2(innerW * _hpTrailRatio, Mathf.Max(2.0f, _healthBar.Size.Y - 4.0f));
				}
				if (IsInstanceValid(_hpSheenRect))
				{
					_hpSheenRect.Size = new Vector2(innerW * hpRatio, 2.0f);
				}

				if (_hpFillStyle != null)
				{
					if (hpRatio <= 0.30f)
					{
						float pulse = 0.5f + 0.5f * Mathf.Sin(Time.GetTicksMsec() * 0.012f);
						_hpFillStyle.BgColor = new Color(0.95f, 0.14f + pulse * 0.16f, 0.20f);
					}
					else
					{
						_hpFillStyle.BgColor = new Color(0.88f, 0.18f, 0.24f);
					}
				}
			}

			if (_player.Config != null && IsInstanceValid(_manaBar) && IsInstanceValid(_mpLabel)) {
				float maxMp = _player.MaxManaEffective;
				_manaBar.MaxValue = maxMp;
				_manaBar.Value = _player.CurrentMana;
				_mpLabel.Text = $"{(int)_player.CurrentMana}/{(int)maxMp}";
				if (IsInstanceValid(_mpSheenRect))
				{
					float mpRatio = maxMp > 0.01f ? Mathf.Clamp(_player.CurrentMana / maxMp, 0.0f, 1.0f) : 0.0f;
					float innerMpW = Mathf.Max(0.0f, _manaBar.Size.X - 4.0f);
					_mpSheenRect.Size = new Vector2(innerMpW * mpRatio, 2.0f);
				}
			}

			if (_player.Config != null)
			{
				UpdateVitalsPanelWidth(_player.MaxHealthEffective, _player.MaxManaEffective);
			}
		}

		// Логика таймера
		ProcessTimer((float)delta);

		// Стрелки к лестнице и к магазину
		UpdateExitLadderArrow();
		UpdateShopAltarArrow();

		// Обновление состояния кд и плюсиков способностей
		if (IsInstanceValid(_ability1Slot)) _ability1Slot.UpdateState();
		if (IsInstanceValid(_ability2Slot)) _ability2Slot.UpdateState();

		UpdateFloorAndEnemiesUI();
		UpdateMadnessUI();
		UpdateStatusEffectsUI();
		UpdateWeaponsUI();
		UpdateBossBarUI();
		UpdateTabHoverTooltip();

		if (NetworkManager.Instance != null && NetworkManager.Instance.IsCoopSession)
		{
			_partyRosterTimer -= (float)delta;
			if (_partyRosterTimer <= 0.0f)
			{
				_partyRosterTimer = _isTabOpen ? 0.25f : 0.75f;
				if (_isTabOpen)
				{
					UpdatePartyRosterUI();
				}
			}
		}

		UpdateStatsUI();
	}

	public override void _Input(InputEvent @event)
	{
		if (@event is InputEventKey kb && kb.Pressed && !kb.Echo && kb.Keycode == Key.Tab)
		{
			if (Engine.TimeScale == 0 || GetTree().Paused) return;
			if (GetTree().Root.GetNodeOrNull("CoopPauseMenu") != null) return;
			ToggleTabOverlay();
			GetViewport().SetInputAsHandled();
		}
	}

	private void ProcessTimer(float delta)
	{
		if (_isCountingDown)
		{
			_time -= delta;
			
			if (_time <= 5.0f && _time > 0 && IsInstanceValid(_announcementBanner)) {
				float phase = _time - (float)Math.Floor(_time);
				_announcementBanner.Visible = phase < 0.7f;
			}

			if (_time <= 0)
			{
				if (NetworkManager.Instance == null || NetworkManager.Instance.IsServerAuthority)
				{
					StartGame();
				}
				else
				{
					_time = 0;
					_isCountingDown = false;
					if (IsInstanceValid(_announcementBanner)) _announcementBanner.Visible = false;
				}
			}
		}
		else
		{
			_time += delta;
		}

		UpdateTimerLabel();
	}

	private void StartGame() {
		_time = 0;
		_isCountingDown = false;
		if (IsInstanceValid(_announcementBanner)) _announcementBanner.Visible = false;
		if (_waveManager != null) {
			_waveManager.StartNextWave();
		}
	}

	public void StartCountdown(float duration, string announcementText)
	{
		_time = duration;
		_isCountingDown = true;
		if (IsInstanceValid(_announcementLabel))
		{
			_announcementLabel.Text = announcementText;
		}
		if (IsInstanceValid(_announcementBanner))
		{
			_announcementBanner.Visible = true;
		}
		UpdateTimerLabel();
	}

	private void UpdateTimerLabel()
	{
		if (_timeLabel == null) return;

		int totalSeconds = (int)Math.Floor(_time);
		int minutes = totalSeconds / 60;
		int seconds = totalSeconds % 60;
		_timeLabel.Text = string.Format("{0:D2}:{1:D2}", minutes, seconds);
	}

	private Level GetLevelCached()
	{
		if (!IsInstanceValid(_level))
		{
			_level = GetTree().GetFirstNodeInGroup("Level") as Level;
		}
		return _level;
	}

	private static void SetLabelState(Label label, string text, Color color, ref string cachedText, ref Color cachedColor)
	{
		if (cachedText != text)
		{
			cachedText = text;
			label.Text = text;
		}
		if (cachedColor != color)
		{
			cachedColor = color;
			label.AddThemeColorOverride("font_color", color);
		}
	}

	private void UpdateExitLadderArrow()
	{
		var level = GetLevelCached();
		UpdateTargetArrow(_arrowIndicator, level?.GetExitLadderNode(), 20.0f, -12.0f);
	}

	private void UpdateShopAltarArrow()
	{
		var level = GetLevelCached();
		UpdateTargetArrow(_shopArrowIndicator, level?.GetShopAltarNode(), 28.0f, -18.0f);
	}

	private void UpdateTargetArrow(ArrowIndicator indicator, Node2D targetNode, float margin, float hoverOffsetY)
	{
		if (indicator == null) return;

		if (targetNode == null || !GodotObject.IsInstanceValid(targetNode))
		{
			indicator.Visible = false;
			return;
		}

		indicator.Visible = true;

		// 1. Пульсация размера (на 10% меньше)
		float pulseScale = (1.0f + (float)Math.Sin(Time.GetTicksMsec() * 0.01f) * 0.15f) * 0.9f;
		indicator.Scale = new Vector2(pulseScale, pulseScale);

		Vector2 screenPos = targetNode.GetGlobalTransformWithCanvas().Origin;
		Vector2 viewportSize = GetViewport().GetVisibleRect().Size;
		Vector2 center = viewportSize / 2.0f;

		float minX = Mathf.Max(margin, 28.0f);
		float maxX = viewportSize.X - Mathf.Max(margin, 28.0f);
		float minY = Mathf.Max(margin, 54.0f);
		float maxY = viewportSize.Y - 98.0f;

		Vector2 arrowPos = screenPos;
		float angle = 0.0f;

		if (screenPos.X < minX || screenPos.X > maxX || screenPos.Y < minY || screenPos.Y > maxY)
		{
			// Вне экрана: прижимаем к краю
			Vector2 dir = (screenPos - center).Normalized();
			float tX = float.MaxValue;
			if (dir.X > 0) tX = (maxX - center.X) / dir.X;
			else if (dir.X < 0) tX = (minX - center.X) / dir.X;

			float tY = float.MaxValue;
			if (dir.Y > 0) tY = (maxY - center.Y) / dir.Y;
			else if (dir.Y < 0) tY = (minY - center.Y) / dir.Y;

			float t = Math.Min(tX, tY);
			arrowPos = center + dir * t;
			arrowPos.X = Mathf.Clamp(arrowPos.X, minX, maxX);
			arrowPos.Y = Mathf.Clamp(arrowPos.Y, minY, maxY);
			angle = (screenPos - arrowPos).Angle();

			// Качание вперед-назад по направлению к цели
			float bounce = (float)Math.Sin(Time.GetTicksMsec() * 0.015f) * 3.0f;
			arrowPos += new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * bounce;
		}
		else
		{
			// На экране: парит над объектом и указывает вниз
			arrowPos = screenPos + new Vector2(0, hoverOffsetY);
			angle = (float)(Math.PI / 2.0f); // 90 градусов (вниз)

			// Плавное покачивание по высоте
			float wave = (float)Math.Sin(Time.GetTicksMsec() * 0.007f) * 2.5f;
			arrowPos.Y += wave;
			arrowPos.X = Mathf.Clamp(arrowPos.X, minX, maxX);
			arrowPos.Y = Mathf.Clamp(arrowPos.Y, minY, maxY);
		}

		indicator.Position = arrowPos;
		if (Math.Abs(indicator.Angle - angle) > 0.001f)
		{
			indicator.Angle = angle;
			indicator.QueueRedraw();
		}
	}

	private void CreateAbilitiesUI()
	{
		_abilitiesContainer = new Control();
		_abilitiesContainer.Name = "AbilitiesContainer";
		_abilitiesContainer.LayoutMode = 1;
		_abilitiesContainer.MouseFilter = Control.MouseFilterEnum.Ignore;
		_abilitiesContainer.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);

		// Чуть крупнее прежних (56x56), по центру у нижнего края
		_abilitiesContainer.OffsetLeft = -80;
		_abilitiesContainer.OffsetRight = 80;
		_abilitiesContainer.OffsetTop = -68;
		_abilitiesContainer.OffsetBottom = -8;

		var hbox = new HBoxContainer();
		hbox.LayoutMode = 1;
		hbox.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		hbox.MouseFilter = Control.MouseFilterEnum.Ignore;
		hbox.Alignment = BoxContainer.AlignmentMode.Center;
		hbox.AddThemeConstantOverride("separation", 22);
		_abilitiesContainer.AddChild(hbox);

		_ability1Slot = new AbilitySlotUI(1, this);
		_ability2Slot = new AbilitySlotUI(2, this);

		hbox.AddChild(_ability1Slot);
		hbox.AddChild(_ability2Slot);

		_hudRoot.AddChild(_abilitiesContainer);

		var font = GameResourceCache.GetSilverFont();
		_skillPointsBadge = new PanelContainer
		{
			Name = "SkillPointsBadge",
			LayoutMode = 1,
			MouseFilter = Control.MouseFilterEnum.Ignore,
			Visible = false
		};
		_skillPointsBadge.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
		_skillPointsBadge.GrowHorizontal = Control.GrowDirection.Both;
		_skillPointsBadge.OffsetLeft = -68;
		_skillPointsBadge.OffsetRight = 68;
		_skillPointsBadge.OffsetTop = -124;
		_skillPointsBadge.OffsetBottom = -101;
		_skillPointsBadge.AddThemeStyleboxOverride("panel", DungeonVisuals.CreatePillStyle(
			new Color(0.08f, 0.06f, 0.02f, 0.92f),
			DungeonVisuals.BrassGold,
			1, 2, 10, 2));
		_hudRoot.AddChild(_skillPointsBadge);

		_skillPointsLabel = new Label
		{
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		if (font != null) _skillPointsLabel.AddThemeFontOverride("font", font);
		_skillPointsLabel.AddThemeFontSizeOverride("font_size", 16);
		_skillPointsLabel.AddThemeColorOverride("font_color", new Color(1.0f, 0.90f, 0.42f));
		DungeonVisuals.ApplyPixelOutline(_skillPointsLabel, 1);
		_skillPointsBadge.AddChild(_skillPointsLabel);
	}

	private void CreateFloorAndEnemiesUI(Font font)
	{
		_floorBadgePanel = new Panel
		{
			Name = "FloorBadgePanel",
			LayoutMode = 1,
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		_floorBadgePanel.SetAnchorsPreset(Control.LayoutPreset.TopRight);
		_floorBadgePanel.OffsetLeft = -196;
		_floorBadgePanel.OffsetRight = -10;
		_floorBadgePanel.OffsetTop = 6;
		_floorBadgePanel.OffsetBottom = 36;
		_floorBadgeStyle = DungeonVisuals.CreatePanelStyle(
			new Color(0.05f, 0.055f, 0.085f, 0.90f),
			new Color(0.60f, 0.50f, 0.30f, 0.78f),
			2, 4, 4);
		_floorBadgePanel.AddThemeStyleboxOverride("panel", _floorBadgeStyle);
		_hudRoot.AddChild(_floorBadgePanel);
		DungeonVisuals.AttachFrameAccent(_floorBadgePanel, DungeonVisuals.BrassDim, 2);

		_floorEnemiesLabel = new Label();
		_floorEnemiesLabel.Name = "FloorEnemiesLabel";
		_floorEnemiesLabel.LayoutMode = 1;
		_floorEnemiesLabel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		_floorEnemiesLabel.OffsetLeft = 7;
		_floorEnemiesLabel.OffsetRight = -7;
		_floorEnemiesLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_floorEnemiesLabel.VerticalAlignment = VerticalAlignment.Center;
		if (font != null) _floorEnemiesLabel.AddThemeFontOverride("font", font);
		_floorEnemiesLabel.AddThemeFontSizeOverride("font_size", 21);
		_floorEnemiesLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.88f, 0.7f));
		DungeonVisuals.ApplyPixelOutline(_floorEnemiesLabel, 2);
		_floorBadgePanel.AddChild(_floorEnemiesLabel);

		_eventBadgePanel = new Panel
		{
			Name = "FloorEventBadgePanel",
			LayoutMode = 1,
			MouseFilter = Control.MouseFilterEnum.Ignore,
			Visible = false
		};
		_eventBadgePanel.SetAnchorsPreset(Control.LayoutPreset.TopRight);
		_eventBadgePanel.OffsetLeft = -196;
		_eventBadgePanel.OffsetRight = -10;
		_eventBadgePanel.OffsetTop = 38;
		_eventBadgePanel.OffsetBottom = 61;
		_eventBadgePanel.AddThemeStyleboxOverride("panel", DungeonVisuals.CreatePanelStyle(
			new Color(0.08f, 0.04f, 0.05f, 0.88f),
			new Color(0.95f, 0.62f, 0.24f, 0.80f),
			1, 3, 3));
		_hudRoot.AddChild(_eventBadgePanel);

		_eventBadgeLabel = new Label
		{
			LayoutMode = 1,
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center
		};
		_eventBadgeLabel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		if (font != null) _eventBadgeLabel.AddThemeFontOverride("font", font);
		_eventBadgeLabel.AddThemeFontSizeOverride("font_size", 16);
		_eventBadgeLabel.AddThemeColorOverride("font_color", new Color(1.0f, 0.82f, 0.42f));
		DungeonVisuals.ApplyPixelOutline(_eventBadgeLabel, 1);
		_eventBadgePanel.AddChild(_eventBadgeLabel);
	}

	private void CreateMadnessAndRelicsUI(Font font)
	{
		_madnessContainer = new Control
		{
			Name = "MadnessContainer",
			LayoutMode = 1,
			MouseFilter = Control.MouseFilterEnum.Ignore,
			Visible = false
		};
		_madnessContainer.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
		_madnessContainer.OffsetLeft = -104;
		_madnessContainer.OffsetRight = 104;
		_madnessContainer.OffsetTop = -120;
		_madnessContainer.OffsetBottom = -98;
		_hudRoot.AddChild(_madnessContainer);

		_madnessBar = new ProgressBar
		{
			LayoutMode = 1,
			MaxValue = 100.0,
			Value = 0.0,
			ShowPercentage = false,
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		_madnessBar.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		_madnessBar.AddThemeStyleboxOverride("background", DungeonVisuals.CreatePanelStyle(
			new Color(0.08f, 0.02f, 0.03f, 0.88f),
			new Color(0.86f, 0.22f, 0.26f, 0.82f),
			1, 3, 3));
		_madnessFillStyle = new StyleBoxFlat
		{
			BgColor = new Color(0.92f, 0.20f, 0.24f, 0.92f),
			CornerRadiusTopLeft = 2,
			CornerRadiusTopRight = 2,
			CornerRadiusBottomLeft = 2,
			CornerRadiusBottomRight = 2
		};
		_madnessBar.AddThemeStyleboxOverride("fill", _madnessFillStyle);
		_madnessContainer.AddChild(_madnessBar);

		_madnessLabel = new Label
		{
			LayoutMode = 1,
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		_madnessLabel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		if (font != null) _madnessLabel.AddThemeFontOverride("font", font);
		_madnessLabel.AddThemeFontSizeOverride("font_size", 16);
		_madnessLabel.AddThemeColorOverride("font_color", new Color(1.0f, 0.90f, 0.72f));
		DungeonVisuals.ApplyPixelOutline(_madnessLabel, 1);
		_madnessContainer.AddChild(_madnessLabel);

		_madnessEdgeFrame = new Control
		{
			Name = "MadnessEdgeFrame",
			LayoutMode = 1,
			MouseFilter = Control.MouseFilterEnum.Ignore,
			Visible = false
		};
		_madnessEdgeFrame.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		var borderPanel = new Panel
		{
			LayoutMode = 1,
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		borderPanel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		borderPanel.AddThemeStyleboxOverride("panel", DungeonVisuals.CreatePanelStyle(
			new Color(0f, 0f, 0f, 0f),
			new Color(0.95f, 0.16f, 0.20f, 0.42f),
			3, 0, 0));
		_madnessEdgeFrame.AddChild(borderPanel);
		_hudRoot.AddChild(_madnessEdgeFrame);
		_hudRoot.MoveChild(_madnessEdgeFrame, 0);
	}

	private void CreateWeaponsUI(Font font)
	{
		_weaponsContainer = new Control();
		_weaponsContainer.Name = "WeaponsContainer";
		_weaponsContainer.LayoutMode = 1;
		_weaponsContainer.SetAnchorsPreset(Control.LayoutPreset.BottomRight);
		_weaponsContainer.OffsetLeft = -245;
		_weaponsContainer.OffsetRight = -10;
		_weaponsContainer.OffsetTop = -98;
		_weaponsContainer.OffsetBottom = -10;
		_hudRoot.AddChild(_weaponsContainer);

		_ammoLabel = new Label();
		_ammoLabel.Name = "AmmoLabel";
		_ammoLabel.LayoutMode = 1;
		_ammoLabel.SetAnchorsPreset(Control.LayoutPreset.TopRight);
		_ammoLabel.OffsetLeft = -235;
		_ammoLabel.OffsetRight = -4;
		_ammoLabel.OffsetTop = 0;
		_ammoLabel.OffsetBottom = 24;
		_ammoLabel.HorizontalAlignment = HorizontalAlignment.Right;
		_ammoLabel.VerticalAlignment = VerticalAlignment.Center;
		if (font != null) _ammoLabel.AddThemeFontOverride("font", font);
		_ammoLabel.AddThemeFontSizeOverride("font_size", 19);
		_ammoLabel.AddThemeColorOverride("font_color", new Color(1.0f, 0.9f, 0.45f));
		DungeonVisuals.ApplyPixelOutline(_ammoLabel, 2);
		_weaponsContainer.AddChild(_ammoLabel);

		var hbox = new HBoxContainer();
		hbox.LayoutMode = 1;
		hbox.SetAnchorsPreset(Control.LayoutPreset.BottomRight);
		hbox.OffsetLeft = -235;
		hbox.OffsetRight = 0;
		hbox.OffsetTop = -64;
		hbox.OffsetBottom = 0;
		hbox.Alignment = BoxContainer.AlignmentMode.End;
		hbox.AddThemeConstantOverride("separation", 10);
		_weaponsContainer.AddChild(hbox);

		for (int i = 0; i < 3; i++)
		{
			var slot = new WeaponShowcaseSlot(i + 1, font);
			hbox.AddChild(slot);
			_weaponSlots[i] = slot;
		}

		// Панель подсказки характеристик оружия или способности при наведении курсора
		_weaponTooltipPanel = new Panel();
		_weaponTooltipPanel.Name = "WeaponTooltipPanel";
		_weaponTooltipPanel.Size = new Vector2(138, 52);
		_weaponTooltipPanel.MouseFilter = Control.MouseFilterEnum.Ignore;
		_weaponTooltipPanel.Visible = false;

		var tipStyle = DungeonVisuals.CreatePanelStyle(
			new Color(0.055f, 0.058f, 0.09f, 0.96f),
			DungeonVisuals.BrassDim,
			1, 3, 4);
		_weaponTooltipPanel.AddThemeStyleboxOverride("panel", tipStyle);
		DungeonVisuals.AttachFrameAccent(_weaponTooltipPanel, DungeonVisuals.BrassGold, 2);
		AddChild(_weaponTooltipPanel);

		_weaponTooltipLabel = new Label();
		_weaponTooltipLabel.LayoutMode = 1;
		_weaponTooltipLabel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		_weaponTooltipLabel.OffsetLeft = 5;
		_weaponTooltipLabel.OffsetRight = -5;
		_weaponTooltipLabel.OffsetTop = 3;
		_weaponTooltipLabel.OffsetBottom = -3;
		_weaponTooltipLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_weaponTooltipLabel.VerticalAlignment = VerticalAlignment.Center;
		if (font != null) _weaponTooltipLabel.AddThemeFontOverride("font", font);
		_weaponTooltipLabel.AddThemeFontSizeOverride("font_size", 16);
		_weaponTooltipLabel.AddThemeConstantOverride("line_spacing", 0);
		_weaponTooltipLabel.AddThemeColorOverride("font_color", DungeonVisuals.Parchment);
		DungeonVisuals.ApplyPixelOutline(_weaponTooltipLabel, 1);
		_weaponTooltipPanel.AddChild(_weaponTooltipLabel);
	}

	private Vector2 ComputeTooltipCardSize(float minWidth, float minHeight)
	{
		if (!IsInstanceValid(_weaponTooltipLabel))
		{
			return new Vector2(minWidth, minHeight);
		}
		Vector2 textSize = _weaponTooltipLabel.GetCombinedMinimumSize();
		return new Vector2(
			Mathf.Max(minWidth, Mathf.Ceil(textSize.X) + 14.0f),
			Mathf.Max(minHeight, Mathf.Ceil(textSize.Y) + 10.0f));
	}

	private WeaponData _lastHoveredWeapon = null;
	private AbilitySlotUI _lastHoveredAbilitySlot = null;
	private float _tooltipScaleAnim = 1.0f;

	private void UpdateWeaponsUI()
	{
		if (!IsInstanceValid(_player) || !IsInstanceValid(_weaponsContainer)) return;

		AttackController activeCtrl = (_player.CurrentSlotIndex >= 0 && _player.CurrentSlotIndex < _player.WeaponSlots.Count)
			? _player.WeaponSlots[_player.CurrentSlotIndex]
			: null;

		string activeWeaponName = activeCtrl?.CurrentWeapon?.WeaponName ?? "";

		if (activeCtrl != null && activeCtrl.UsesAmmo)
		{
			if (activeCtrl.IsReloading)
			{
				SetLabelState(
					_ammoLabel,
					$"{activeWeaponName.ToUpper()} — RELOAD {(int)(activeCtrl.ReloadProgress * 100)}%",
					new Color(1.0f, 0.55f, 0.2f),
					ref _lastAmmoText,
					ref _lastAmmoColor);
			}
			else
			{
				SetLabelState(
					_ammoLabel,
					$"{activeWeaponName.ToUpper()} [{activeCtrl.CurrentAmmo}/{activeCtrl.MaxAmmo}]",
					activeCtrl.CurrentAmmo > 0 ? new Color(1.0f, 0.9f, 0.45f) : new Color(1.0f, 0.3f, 0.3f),
					ref _lastAmmoText,
					ref _lastAmmoColor);
			}
		}
		else if (!string.IsNullOrEmpty(activeWeaponName))
		{
			SetLabelState(
				_ammoLabel,
				activeWeaponName.ToUpper(),
				new Color(0.95f, 0.86f, 0.62f),
				ref _lastAmmoText,
				ref _lastAmmoColor);
		}
		else if (_lastAmmoText != "")
		{
			_lastAmmoText = "";
			_ammoLabel.Text = "";
		}

		WeaponData hoveredWeapon = null;
		bool isGroundHover = false;
		Vector2 mouseScreenPos = GetViewport().GetMousePosition();

		for (int i = 0; i < 3; i++)
		{
			if (_weaponSlots[i] == null) continue;
			bool isSelected = (_player.CurrentSlotIndex == i);
			AttackController slotCtrl = i < _player.WeaponSlots.Count ? _player.WeaponSlots[i] : null;
			WeaponData w = slotCtrl?.CurrentWeapon;
			float reloadProg = (slotCtrl != null && slotCtrl.IsReloading) ? slotCtrl.ReloadProgress : -1.0f;
			_weaponSlots[i].UpdateSlot(w, isSelected, reloadProg);

			if (w != null && _weaponSlots[i].GetGlobalRect().HasPoint(mouseScreenPos))
			{
				hoveredWeapon = w;
			}
		}

		// Если мышка не на слоте в HUD — проверяем наведение курсора на оружие на земле
		if (hoveredWeapon == null)
		{
			Vector2 worldMousePos = _player.GetGlobalMousePosition();
			float bestDist = float.MaxValue;
			foreach (Node node in GetTree().GetNodesInGroup("WeaponPickup"))
			{
				if (node is WeaponPickup pickup && pickup.IsHoveredByMouse(worldMousePos))
				{
					float d = pickup.GlobalPosition.DistanceTo(worldMousePos);
					if (d < bestDist)
					{
						bestDist = d;
						hoveredWeapon = pickup.WeaponToGive;
						isGroundHover = pickup.IsPlayerInRange;
					}
				}
			}
		}

		AbilitySlotUI hoveredAbility = null;
		if (hoveredWeapon == null)
		{
			if (IsInstanceValid(_ability1Slot) && _ability1Slot.IsMouseOverSlot(mouseScreenPos))
				hoveredAbility = _ability1Slot;
			else if (IsInstanceValid(_ability2Slot) && _ability2Slot.IsMouseOverSlot(mouseScreenPos))
				hoveredAbility = _ability2Slot;
		}

		if (IsInstanceValid(_weaponTooltipPanel) && IsInstanceValid(_weaponTooltipLabel))
		{
			if (hoveredWeapon != null)
			{
				if (_lastHoveredWeapon != hoveredWeapon || _lastHoveredAbilitySlot != null || !_weaponTooltipPanel.Visible)
				{
					_tooltipScaleAnim = 0.82f;
					_lastHoveredWeapon = hoveredWeapon;
					_lastHoveredAbilitySlot = null;
				}
				_tooltipScaleAnim = Mathf.MoveToward(_tooltipScaleAnim, 1.0f, (float)GetProcessDeltaTime() * 4.5f);

				_weaponTooltipLabel.Text = WeaponShowcaseSlot.FormatWeaponStatsTooltip(
					hoveredWeapon,
					_player.WeaponDamageMultiplier,
					isGroundHover,
					_player.HasRelic(GameLogic.RelicPrismLens));

				int minW = hoveredWeapon.IsContinuousLaser ? 168 : 150;
				int minH = hoveredWeapon.IsContinuousLaser ? 74 : (hoveredWeapon.ManaCost > 0.01f ? 66 : 54);
				Vector2 cardSize = ComputeTooltipCardSize(minW, minH);
				_weaponTooltipPanel.Size = cardSize;

				Vector2 viewSize = GetViewport().GetVisibleRect().Size;
				bool openLeft = (mouseScreenPos.X + cardSize.X > viewSize.X - 6.0f);
				bool openUp = (mouseScreenPos.Y - cardSize.Y >= 6.0f);

				float posX = openLeft ? (mouseScreenPos.X - cardSize.X) : mouseScreenPos.X;
				float posY = openUp ? (mouseScreenPos.Y - cardSize.Y) : mouseScreenPos.Y;

				_weaponTooltipPanel.Position = new Vector2(posX, posY);
				_weaponTooltipPanel.PivotOffset = mouseScreenPos - _weaponTooltipPanel.Position;
				_weaponTooltipPanel.Scale = new Vector2(_tooltipScaleAnim, _tooltipScaleAnim);
				_weaponTooltipPanel.Visible = true;
			}
			else if (hoveredAbility != null)
			{
				if (_lastHoveredAbilitySlot != hoveredAbility || _lastHoveredWeapon != null || !_weaponTooltipPanel.Visible)
				{
					_tooltipScaleAnim = 0.82f;
					_lastHoveredAbilitySlot = hoveredAbility;
					_lastHoveredWeapon = null;
				}
				_tooltipScaleAnim = Mathf.MoveToward(_tooltipScaleAnim, 1.0f, (float)GetProcessDeltaTime() * 4.5f);

				_weaponTooltipLabel.Text = hoveredAbility.FormatAbilityTooltip(_player);
				int lineCount = _weaponTooltipLabel.Text.Split('\n').Length;
				Vector2 cardSize = ComputeTooltipCardSize(188.0f, lineCount >= 4 ? 78.0f : 64.0f);
				_weaponTooltipPanel.Size = cardSize;

				Vector2 viewSize = GetViewport().GetVisibleRect().Size;
				float posX = Mathf.Clamp(mouseScreenPos.X - cardSize.X * 0.5f, 8.0f, viewSize.X - cardSize.X - 8.0f);
				float posY = Mathf.Max(8.0f, mouseScreenPos.Y - cardSize.Y - 12.0f);

				_weaponTooltipPanel.Position = new Vector2(posX, posY);
				_weaponTooltipPanel.PivotOffset = new Vector2(cardSize.X * 0.5f, cardSize.Y);
				_weaponTooltipPanel.Scale = new Vector2(_tooltipScaleAnim, _tooltipScaleAnim);
				_weaponTooltipPanel.Visible = true;
			}
			else
			{
				_lastHoveredWeapon = null;
				_lastHoveredAbilitySlot = null;
				_weaponTooltipPanel.Visible = false;
			}
		}
	}

	private void UpdateFloorAndEnemiesUI()
	{
		if (!IsInstanceValid(_floorEnemiesLabel)) return;
		var level = GetLevelCached();
		int floor = level != null ? level.CurrentLevelIndex : 1;
		int enemies = _waveManager != null ? _waveManager.EnemiesRemaining : 0;
		bool active = _waveManager != null && _waveManager.WaveInProgress;

		if (_isCountingDown)
		{
			if (_floorBadgeStyle != null) _floorBadgeStyle.BorderColor = new Color(0.86f, 0.70f, 0.30f, 0.82f);
			SetLabelState(
				_floorEnemiesLabel,
				$"FLOOR {floor}  |  PREPARE",
				new Color(1.0f, 0.86f, 0.46f),
				ref _lastFloorText,
				ref _lastFloorColor);
		}
		else if (active || enemies > 0)
		{
			if (_floorBadgeStyle != null) _floorBadgeStyle.BorderColor = new Color(0.85f, 0.36f, 0.32f, 0.84f);
			SetLabelState(
				_floorEnemiesLabel,
				$"FLOOR {floor}  |  IN BATTLE",
				new Color(1.0f, 0.58f, 0.48f),
				ref _lastFloorText,
				ref _lastFloorColor);
		}
		else
		{
			if (_floorBadgeStyle != null) _floorBadgeStyle.BorderColor = new Color(0.32f, 0.84f, 0.50f, 0.84f);
			SetLabelState(
				_floorEnemiesLabel,
				$"FLOOR {floor}  |  CLEARED",
				new Color(0.45f, 1.0f, 0.55f),
				ref _lastFloorText,
				ref _lastFloorColor);
		}
	}

	private void CreateBossBarUI(Font font)
	{
		_bossBarContainer = new Control();
		_bossBarContainer.Name = "BossBarContainer";
		_bossBarContainer.LayoutMode = 1;
		_bossBarContainer.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
		_bossBarContainer.OffsetLeft = -161;
		_bossBarContainer.OffsetRight = 161;
		_bossBarContainer.OffsetTop = 32;
		_bossBarContainer.OffsetBottom = 74;
		_bossBarContainer.Visible = false;
		_hudRoot.AddChild(_bossBarContainer);

		var bossPanel = new Panel
		{
			LayoutMode = 1,
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		bossPanel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		bossPanel.AddThemeStyleboxOverride("panel", DungeonVisuals.CreatePanelStyle(
			new Color(0.06f, 0.03f, 0.04f, 0.90f),
			new Color(0.75f, 0.22f, 0.25f, 0.85f),
			2, 5, 7));
		_bossBarContainer.AddChild(bossPanel);

		_bossNameLabel = new Label();
		_bossNameLabel.LayoutMode = 1;
		_bossNameLabel.SetAnchorsPreset(Control.LayoutPreset.TopWide);
		_bossNameLabel.OffsetLeft = 7;
		_bossNameLabel.OffsetRight = -7;
		_bossNameLabel.OffsetTop = 2;
		_bossNameLabel.OffsetBottom = 23;
		_bossNameLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_bossNameLabel.VerticalAlignment = VerticalAlignment.Center;
		if (font != null) _bossNameLabel.AddThemeFontOverride("font", font);
		_bossNameLabel.AddThemeFontSizeOverride("font_size", 18);
		_bossNameLabel.AddThemeColorOverride("font_color", new Color(1.0f, 0.72f, 0.48f));
		DungeonVisuals.ApplyPixelOutline(_bossNameLabel, 2);
		_bossBarContainer.AddChild(_bossNameLabel);

		_bossHealthBar = new ProgressBar();
		_bossHealthBar.LayoutMode = 1;
		_bossHealthBar.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
		_bossHealthBar.OffsetLeft = 9;
		_bossHealthBar.OffsetRight = -9;
		_bossHealthBar.OffsetTop = -18;
		_bossHealthBar.OffsetBottom = -5;
		_bossHealthBar.ShowPercentage = false;

		DungeonVisuals.StyleProgressBar(
			_bossHealthBar,
			new Color(0.86f, 0.14f, 0.20f),
			new Color(0.10f, 0.03f, 0.04f, 0.95f),
			new Color(0.90f, 0.68f, 0.22f, 0.9f),
			2, 2);

		var bossSheen = new ColorRect
		{
			Color = new Color(1f, 0.85f, 0.85f, 0.22f),
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		bossSheen.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopWide);
		bossSheen.OffsetBottom = 2;
		_bossHealthBar.AddChild(bossSheen);
		_bossBarContainer.AddChild(_bossHealthBar);
	}

	private void UpdateBossBarUI()
	{
		if (!IsInstanceValid(_bossBarContainer)) return;

		Enemy activeBoss = null;
		foreach (Node node in GetTree().GetNodesInGroup("Boss"))
		{
			if (node is Enemy boss && boss.CurrentHealth > 0)
			{
				activeBoss = boss;
				break;
			}
		}

		if (activeBoss == null)
		{
			_bossBarContainer.Visible = false;
			return;
		}

		_bossBarContainer.Visible = true;
		_bossNameLabel.Text = $"{activeBoss.DisplayName} ({(int)Math.Ceiling(activeBoss.CurrentHealth)}/{(int)activeBoss.MaxHealthScaled})";
		_bossHealthBar.MaxValue = activeBoss.MaxHealthScaled;
		_bossHealthBar.Value = activeBoss.CurrentHealth;
	}

	public void UpdateAbilitiesUI()
	{
		if (IsInstanceValid(_ability1Slot)) _ability1Slot.UpdateState();
		if (IsInstanceValid(_ability2Slot)) _ability2Slot.UpdateState();

		if (IsInstanceValid(_skillPointsBadge) && IsInstanceValid(_skillPointsLabel))
		{
			bool canSpend = IsInstanceValid(_player)
				&& _player.SkillPoints > 0
				&& (GameLogic.CanUpgradeAbility(_player.Ability1Level) || GameLogic.CanUpgradeAbility(_player.Ability2Level));
			_skillPointsBadge.Visible = canSpend;
			if (canSpend)
			{
				_skillPointsLabel.Text = _player.SkillPoints == 1
					? "1 SKILL POINT"
					: $"{_player.SkillPoints} SKILL POINTS";
				bool madnessVis = IsInstanceValid(_madnessContainer) && _madnessContainer.Visible;
				_skillPointsBadge.OffsetTop = madnessVis ? -146 : -124;
				_skillPointsBadge.OffsetBottom = madnessVis ? -123 : -101;
			}
		}
	}

	public void SetFloorEventUI(GameLogic.FloorEventType evt)
	{
		if (!IsInstanceValid(_eventBadgePanel) || !IsInstanceValid(_eventBadgeLabel)) return;
		if (evt == GameLogic.FloorEventType.None)
		{
			_eventBadgePanel.Visible = false;
			return;
		}
		_eventBadgePanel.Visible = true;
		_eventBadgeLabel.Text = GameLogic.GetFloorEventTitle(evt);
		Color accent = evt switch
		{
			GameLogic.FloorEventType.PitchBlack => new Color(1.0f, 0.84f, 0.30f),
			GameLogic.FloorEventType.ImpFrenzy => new Color(1.0f, 0.42f, 0.30f),
			GameLogic.FloorEventType.SpikeChamber => new Color(1.0f, 0.30f, 0.24f),
			_ => new Color(0.55f, 0.88f, 1.0f)
		};
		_eventBadgeLabel.AddThemeColorOverride("font_color", accent);
	}

	private static readonly (string Id, string Name, string Tag, string ShortDesc, string IconPath, uint AccentHex)[] ShopArtefactVisuals = new[]
	{
		("STAT_HP_REGEN", "Ring of Regen", "REGEN", "+0.25 HP/s per stack", "res://assets/ui/shop_icons/hp_regen.png", 0x59F273u),
		("STAT_MAX_MANA", "Mana Vessel", "MANA", "+50 Max MP per stack", "res://assets/ui/shop_icons/max_mana.png", 0x59B8FFu),
		("STAT_MAX_HP", "Heartstone", "VITAL", "+25 Max HP per stack", "res://assets/ui/shop_icons/max_hp.png", 0xFF6B6Bu),
		("STAT_MANA_REGEN", "Aether Spring", "MPR", "+1.0 MP/s per stack", "res://assets/ui/shop_icons/mana_regen.png", 0x73D9FFu),
		("STAT_MOVE_SPEED", "Wind Greaves", "HASTE", "+10 Move SPD per stack", "res://assets/ui/shop_icons/move_speed.png", 0x8CFFCCu),
		("GLOBAL_WEAPON_DMG", "Whetstone", "POWER", "+10% Weapon DMG per stack", "res://assets/ui/shop_icons/dmg_upgrade.png", 0xFFB840u),
		("STAT_FURY_DRAUGHT", "Berserker Draught", "FURY", "+7% DMG & +6 SPD", "res://assets/ui/shop_icons/fury_draught.png", 0xFF7A47u),
		("CONSUMABLE_HEAL_BOOST", "Crimson Elixir", "ELIXIR", "+15 Max HP & 20% Heal", "res://assets/ui/shop_icons/crimson_elixir.png", 0xF24D73u),
		("STAT_MANA_HARMONY", "Aetheric Harmony", "AETHER", "+30 MP & +0.5 MP/s", "res://assets/ui/shop_icons/mana_harmony.png", 0x6BC7FFu),
		("ABILITY_COOLDOWN", "Chrono Grimoire", "HASTE", "-12% Ability CD per stack", "res://assets/ui/shop_icons/skill_point.png", 0xD185FFu),
		("ABILITY_1_POWER", "Grimoire of Flame", "SPELL Q", "+22% DMG & +18% Rad [Q]", "res://assets/ui/shop_icons/ability_q.png", 0xFF8538u),
		("ABILITY_2_POWER", "Rune of the Aegis", "SPELL V", "-10% CD, +25% DMG & +20% Rad [V]", "res://assets/ui/shop_icons/ability_v.png", 0x59E0FFu)
	};

	private static Texture2D GetRelicBetterIcon(string relicId)
	{
		string path = relicId switch
		{
			GameLogic.RelicPrismLens => "res://assets/ui/shop_icons/piercing.png",
			GameLogic.RelicRicochet => "res://assets/ui/shop_icons/velocity_core.png",
			GameLogic.RelicEmberCore => "res://assets/ui/shop_icons/ability_q.png",
			GameLogic.RelicVenomCoat => "res://assets/ui/shop_icons/mana_harmony.png",
			GameLogic.RelicCrimsonEdge => "res://assets/ui/shop_icons/fury_draught.png",
			GameLogic.RelicChainSpark => "res://assets/ui/shop_icons/multishot.png",
			GameLogic.RelicFrostAegis => "res://assets/ui/shop_icons/ability_v.png",
			GameLogic.RelicVampiricFang => "res://assets/ui/shop_icons/crimson_elixir.png",
			GameLogic.RelicVolatileSigil => "res://assets/ui/shop_icons/impact_core.png",
			_ => null
		};
		if (!string.IsNullOrEmpty(path) && ResourceLoader.Exists(path))
		{
			var tex = GD.Load<Texture2D>(path);
			if (tex != null) return tex;
		}
		return DungeonVisuals.GetRelicIconTexture(relicId);
	}

	private static PanelContainer CreateStyledRelicEntryCard(
		Texture2D iconTex,
		string title,
		string countTag,
		Color accent,
		Font titleFont,
		out StyleBoxFlat cardStyle)
	{
		var card = new PanelContainer
		{
			CustomMinimumSize = new Vector2(158, 0),
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		cardStyle = DungeonVisuals.CreatePillStyle(
			new Color(0.068f, 0.075f, 0.118f, 0.94f),
			new Color(accent.R, accent.G, accent.B, 0.72f),
			1, 4, 7, 4);
		card.AddThemeStyleboxOverride("panel", cardStyle);

		var hbox = new HBoxContainer
		{
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		hbox.AddThemeConstantOverride("separation", 6);
		card.AddChild(hbox);

		var iconFrame = new PanelContainer
		{
			CustomMinimumSize = new Vector2(20, 20),
			SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		iconFrame.AddThemeStyleboxOverride("panel", DungeonVisuals.CreatePillStyle(
			new Color(0.03f, 0.035f, 0.058f, 0.96f),
			new Color(accent.R, accent.G, accent.B, 0.55f),
			1, 3, 2, 2));
		hbox.AddChild(iconFrame);

		var icon = new TextureRect
		{
			Texture = iconTex,
			TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
			CustomMinimumSize = new Vector2(14, 14),
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		iconFrame.AddChild(icon);

		var titleLbl = new Label
		{
			Text = title,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
			VerticalAlignment = VerticalAlignment.Center,
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		if (titleFont != null) titleLbl.AddThemeFontOverride("font", titleFont);
		titleLbl.AddThemeFontSizeOverride("font_size", 15);
		titleLbl.AddThemeColorOverride("font_color", accent.Lerp(Colors.White, 0.38f));
		DungeonVisuals.ApplyPixelOutline(titleLbl, 2);
		hbox.AddChild(titleLbl);

		if (!string.IsNullOrEmpty(countTag))
		{
			var countPill = new PanelContainer
			{
				SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
				MouseFilter = Control.MouseFilterEnum.Ignore
			};
			countPill.AddThemeStyleboxOverride("panel", DungeonVisuals.CreatePillStyle(
				new Color(accent.R * 0.25f, accent.G * 0.25f, accent.B * 0.25f, 0.94f),
				new Color(accent.R, accent.G, accent.B, 0.75f),
				1, 3, 5, 2));

			var countLbl = new Label
			{
				Text = countTag,
				SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
				VerticalAlignment = VerticalAlignment.Center,
				MouseFilter = Control.MouseFilterEnum.Ignore
			};
			if (titleFont != null) countLbl.AddThemeFontOverride("font", titleFont);
			countLbl.AddThemeFontSizeOverride("font_size", 12);
			countLbl.AddThemeColorOverride("font_color", Colors.White);
			DungeonVisuals.ApplyPixelOutline(countLbl, 1);
			countPill.AddChild(countLbl);
			hbox.AddChild(countPill);
		}

		return card;
	}

	private static Font CreateReadableFont(Font baseFont, float embolden, int spacingTop = 0, int spacingBottom = 0)
	{
		return GameResourceCache.CreateFontVariation(baseFont, embolden, 1, spacingTop, spacingBottom);
	}

	public void UpdateRelicsUI()
	{
		if (!IsInstanceValid(_relicsContainer)) return;
		_tabCardEntries.Clear();
		_lastHoveredTabCard = null;
		if (IsInstanceValid(_tabTooltipPanel)) _tabTooltipPanel.Visible = false;

		foreach (Node child in _relicsContainer.GetChildren())
		{
			_relicsContainer.RemoveChild(child);
			child.QueueFree();
		}
		if (IsInstanceValid(_upgradesContainer))
		{
			foreach (Node child in _upgradesContainer.GetChildren())
			{
				_upgradesContainer.RemoveChild(child);
				child.QueueFree();
			}
		}

		if (!IsInstanceValid(_player) || _player.IsQueuedForDeletion())
		{
			_player = NetworkManager.GetLocalPlayer(GetTree());
		}

		var font = GameResourceCache.GetSilverFont();
		var titleFont = CreateReadableFont(font, 0.20f);
		int relicEntries = 0;
		int upgradeEntries = 0;

		if (IsInstanceValid(_player))
		{
			var owned = _player.GetRelicIdsList();
			if (owned != null)
			{
				foreach (string id in owned)
				{
					var def = GameLogic.FindRelic(id);
					if (def == null) continue;

					Color accent = new Color(
						((def.AccentHex >> 16) & 0xFF) / 255.0f,
						((def.AccentHex >> 8) & 0xFF) / 255.0f,
						(def.AccentHex & 0xFF) / 255.0f,
						1.0f);

					var card = CreateStyledRelicEntryCard(
						GetRelicBetterIcon(id),
						def.Name,
						"",
						accent,
						titleFont,
						out StyleBoxFlat cardStyle);
					_relicsContainer.AddChild(card);
					_tabCardEntries.Add(new TabCardEntry
					{
						Card = card,
						CardStyle = cardStyle,
						Title = def.Name,
						TooltipTag = def.ShortTag,
						Description = def.Description,
						Accent = accent
					});
					relicEntries++;
				}
			}

			var targetUpgradesContainer = IsInstanceValid(_upgradesContainer) ? _upgradesContainer : _relicsContainer;
			foreach (var item in ShopArtefactVisuals)
			{
				int count = _player.GetShopPurchaseCount(item.Id);
				if (count <= 0) continue;

				Color accent = new Color(
					((item.AccentHex >> 16) & 0xFF) / 255.0f,
					((item.AccentHex >> 8) & 0xFF) / 255.0f,
					(item.AccentHex & 0xFF) / 255.0f,
					1.0f);

				Texture2D iconTex = ResourceLoader.Exists(item.IconPath)
					? GD.Load<Texture2D>(item.IconPath)
					: DungeonVisuals.GetRelicIconTexture(item.Id);

				string countTag = count > 1 ? $"x{count}" : "";
				string tooltipTag = count > 1 ? $"{item.Tag} x{count}" : item.Tag;
				var card = CreateStyledRelicEntryCard(
					iconTex,
					item.Name,
					countTag,
					accent,
					titleFont,
					out StyleBoxFlat cardStyle);
				targetUpgradesContainer.AddChild(card);
				_tabCardEntries.Add(new TabCardEntry
				{
					Card = card,
					CardStyle = cardStyle,
					Title = item.Name,
					TooltipTag = tooltipTag,
					Description = item.ShortDesc,
					Accent = accent
				});
				upgradeEntries++;
			}
		}

		if (_relicsContainer is GridContainer relicsGrid)
		{
			relicsGrid.Columns = relicEntries >= 2 ? 2 : 1;
		}
		if (_upgradesContainer is GridContainer upgradesGrid)
		{
			upgradesGrid.Columns = upgradeEntries >= 2 ? 2 : 1;
		}

		bool hasRelics = relicEntries > 0;
		bool hasUpgrades = upgradeEntries > 0;
		bool hasAny = hasRelics || hasUpgrades;
		if (IsInstanceValid(_relicsEmptyLabel))
		{
			_relicsEmptyLabel.Visible = !hasAny;
		}
		if (IsInstanceValid(_tabRelicsSection))
		{
			_tabRelicsSection.Visible = hasRelics;
		}
		if (IsInstanceValid(_tabSectionsDivider))
		{
			_tabSectionsDivider.Visible = hasRelics && hasUpgrades;
		}
		if (IsInstanceValid(_tabUpgradesSection))
		{
			_tabUpgradesSection.Visible = hasUpgrades;
		}
		_relicsContainer.Visible = hasRelics;
		if (IsInstanceValid(_upgradesContainer))
		{
			_upgradesContainer.Visible = hasUpgrades;
		}
		UpdateTabOverlayAnchor();
	}

	private void UpdateTabOverlayAnchor()
	{
		if (!IsInstanceValid(_tabOverlay)) return;
		bool bossVis = IsInstanceValid(_bossBarContainer) && _bossBarContainer.Visible;
		bool eventVis = IsInstanceValid(_eventBadgePanel) && _eventBadgePanel.Visible;
		float topOffset = bossVis ? 78.0f : (eventVis ? 64.0f : 42.0f);
		if (Mathf.Abs(_tabOverlay.OffsetTop - topOffset) > 0.5f)
		{
			_tabOverlay.OffsetTop = topOffset;
			_tabOverlay.OffsetBottom = topOffset;
		}
	}

	private void CreateTabTooltipUI(Font font)
	{
		var titleFont = CreateReadableFont(font, 0.22f);
		var bodyFont = CreateReadableFont(font, 0.14f, spacingTop: 1, spacingBottom: 1);

		_tabTooltipPanel = new PanelContainer
		{
			Name = "TabItemTooltipPanel",
			MouseFilter = Control.MouseFilterEnum.Ignore,
			Visible = false
		};
		_tabTooltipPanel.AddThemeStyleboxOverride("panel", DungeonVisuals.CreatePillStyle(
			new Color(0.042f, 0.048f, 0.086f, 0.97f),
			DungeonVisuals.BrassGold,
			1, 4, 9, 6));
		AddChild(_tabTooltipPanel);

		var vbox = new VBoxContainer
		{
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		vbox.AddThemeConstantOverride("separation", 4);
		_tabTooltipPanel.AddChild(vbox);

		var headerRow = new HBoxContainer
		{
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		headerRow.AddThemeConstantOverride("separation", 6);
		vbox.AddChild(headerRow);

		_tabTooltipTitleLabel = new Label
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
			VerticalAlignment = VerticalAlignment.Center,
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		if (titleFont != null) _tabTooltipTitleLabel.AddThemeFontOverride("font", titleFont);
		_tabTooltipTitleLabel.AddThemeFontSizeOverride("font_size", 16);
		DungeonVisuals.ApplyPixelOutline(_tabTooltipTitleLabel, 2);
		headerRow.AddChild(_tabTooltipTitleLabel);

		_tabTooltipTagPill = new PanelContainer
		{
			SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		headerRow.AddChild(_tabTooltipTagPill);

		_tabTooltipTagLabel = new Label
		{
			SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
			VerticalAlignment = VerticalAlignment.Center,
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		if (titleFont != null) _tabTooltipTagLabel.AddThemeFontOverride("font", titleFont);
		_tabTooltipTagLabel.AddThemeFontSizeOverride("font_size", 12);
		_tabTooltipTagLabel.AddThemeColorOverride("font_color", Colors.White);
		DungeonVisuals.ApplyPixelOutline(_tabTooltipTagLabel, 1);
		_tabTooltipTagPill.AddChild(_tabTooltipTagLabel);

		_tabTooltipDivider = new ColorRect
		{
			CustomMinimumSize = new Vector2(0, 1),
			Color = new Color(0.82f, 0.68f, 0.32f, 0.35f),
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		vbox.AddChild(_tabTooltipDivider);

		_tabTooltipDescLabel = new Label
		{
			CustomMinimumSize = new Vector2(194, 0),
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		if (bodyFont != null) _tabTooltipDescLabel.AddThemeFontOverride("font", bodyFont);
		_tabTooltipDescLabel.AddThemeFontSizeOverride("font_size", 14);
		_tabTooltipDescLabel.AddThemeConstantOverride("line_spacing", 1);
		_tabTooltipDescLabel.AddThemeColorOverride("font_color", new Color(0.96f, 0.98f, 1.0f, 0.99f));
		DungeonVisuals.ApplyPixelOutline(_tabTooltipDescLabel, 2);
		vbox.AddChild(_tabTooltipDescLabel);
	}

	private void ApplyTabCardHoverVisual(PanelContainer targetCard, bool isHovered)
	{
		if (!IsInstanceValid(targetCard)) return;
		foreach (var entry in _tabCardEntries)
		{
			if (entry.Card == targetCard && entry.CardStyle != null)
			{
				Color a = entry.Accent;
				entry.CardStyle.BgColor = isHovered
					? new Color(0.11f, 0.125f, 0.20f, 0.98f)
					: new Color(0.068f, 0.075f, 0.118f, 0.94f);
				entry.CardStyle.BorderColor = isHovered
					? a.Lerp(Colors.White, 0.45f)
					: new Color(a.R, a.G, a.B, 0.72f);
				break;
			}
		}
	}

	private void UpdateTabHoverTooltip()
	{
		if (!IsInstanceValid(_tabTooltipPanel)) return;

		if (!_isTabOpen || !IsInstanceValid(_tabOverlay) || !_tabOverlay.Visible)
		{
			if (_tabTooltipPanel.Visible)
			{
				_tabTooltipPanel.Visible = false;
			}
			if (_lastHoveredTabCard != null)
			{
				ApplyTabCardHoverVisual(_lastHoveredTabCard, false);
				_lastHoveredTabCard = null;
			}
			return;
		}

		UpdateTabOverlayAnchor();

		Vector2 mouseScreenPos = GetViewport().GetMousePosition();
		TabCardEntry hovered = null;
		foreach (var entry in _tabCardEntries)
		{
			if (IsInstanceValid(entry.Card) && entry.Card.Visible && entry.Card.GetGlobalRect().HasPoint(mouseScreenPos))
			{
				hovered = entry;
				break;
			}
		}

		if (hovered == null)
		{
			_tabTooltipPanel.Visible = false;
			if (_lastHoveredTabCard != null)
			{
				ApplyTabCardHoverVisual(_lastHoveredTabCard, false);
				_lastHoveredTabCard = null;
			}
			return;
		}

		if (IsInstanceValid(_weaponTooltipPanel))
		{
			_weaponTooltipPanel.Visible = false;
		}

		if (_lastHoveredTabCard != hovered.Card)
		{
			if (_lastHoveredTabCard != null)
			{
				ApplyTabCardHoverVisual(_lastHoveredTabCard, false);
			}
			_lastHoveredTabCard = hovered.Card;
			ApplyTabCardHoverVisual(_lastHoveredTabCard, true);
			_tabTooltipScale = 0.90f;

			Color accent = hovered.Accent;
			var tipStyle = DungeonVisuals.CreatePillStyle(
				new Color(0.042f, 0.048f, 0.086f, 0.97f),
				new Color(accent.R, accent.G, accent.B, 0.88f),
				1, 4, 9, 6);
			tipStyle.ShadowSize = 5;
			tipStyle.ShadowColor = new Color(0f, 0f, 0f, 0.65f);
			_tabTooltipPanel.AddThemeStyleboxOverride("panel", tipStyle);

			if (IsInstanceValid(_tabTooltipTitleLabel))
			{
				_tabTooltipTitleLabel.Text = hovered.Title;
				_tabTooltipTitleLabel.AddThemeColorOverride("font_color", accent.Lerp(Colors.White, 0.40f));
			}

			bool hasTag = !string.IsNullOrEmpty(hovered.TooltipTag);
			if (IsInstanceValid(_tabTooltipTagPill) && IsInstanceValid(_tabTooltipTagLabel))
			{
				_tabTooltipTagPill.Visible = hasTag;
				if (hasTag)
				{
					_tabTooltipTagLabel.Text = hovered.TooltipTag;
					_tabTooltipTagPill.AddThemeStyleboxOverride("panel", DungeonVisuals.CreatePillStyle(
						new Color(accent.R * 0.25f, accent.G * 0.25f, accent.B * 0.25f, 0.95f),
						new Color(accent.R, accent.G, accent.B, 0.78f),
						1, 3, 5, 2));
				}
			}

			if (IsInstanceValid(_tabTooltipDivider))
			{
				_tabTooltipDivider.Color = new Color(accent.R, accent.G, accent.B, 0.35f);
			}

			if (IsInstanceValid(_tabTooltipDescLabel))
			{
				_tabTooltipDescLabel.Text = hovered.Description;
			}

			_tabTooltipPanel.ResetSize();
		}

		_tabTooltipScale = Mathf.Lerp(_tabTooltipScale, 1.0f, 0.30f);

		Vector2 cardSize = _tabTooltipPanel.GetCombinedMinimumSize();
		_tabTooltipPanel.Size = cardSize;
		Vector2 viewSize = GetViewport().GetVisibleRect().Size;

		bool preferLeft = (mouseScreenPos.X + cardSize.X + 14.0f > viewSize.X - 6.0f) || (mouseScreenPos.X > viewSize.X * 0.5f);
		float posX = preferLeft ? (mouseScreenPos.X - cardSize.X - 10.0f) : (mouseScreenPos.X + 12.0f);
		float posY = mouseScreenPos.Y - cardSize.Y * 0.5f;

		posX = Mathf.Clamp(posX, 6.0f, Mathf.Max(6.0f, viewSize.X - cardSize.X - 6.0f));
		posY = Mathf.Clamp(posY, 6.0f, Mathf.Max(6.0f, viewSize.Y - cardSize.Y - 6.0f));

		_tabTooltipPanel.Position = new Vector2(posX, posY);
		_tabTooltipPanel.PivotOffset = cardSize * 0.5f;
		_tabTooltipPanel.Scale = new Vector2(_tabTooltipScale, _tabTooltipScale);
		_tabTooltipPanel.Visible = true;
	}

	public void ShowToast(string text, Color accent)
	{
		if (!IsInstanceValid(_toastContainer) || !IsInstanceValid(_toastLabel)) return;
		if (IsInstanceValid(_toastTween)) _toastTween.Kill();

		_toastContainer.AddThemeStyleboxOverride("panel", DungeonVisuals.CreatePanelStyle(
			new Color(0.04f, 0.05f, 0.08f, 0.92f), accent, 2, 3, 5));
		_toastLabel.Text = text;
		_toastLabel.AddThemeColorOverride("font_color", accent.Lightened(0.22f));
		_toastContainer.Modulate = new Color(1, 1, 1, 1);
		_toastContainer.Visible = true;

		_toastTween = CreateTween();
		_toastTween.TweenInterval(1.85);
		_toastTween.TweenProperty(_toastContainer, "modulate:a", 0.0f, 0.45);
		_toastTween.TweenCallback(Callable.From(() =>
		{
			if (IsInstanceValid(_toastContainer)) _toastContainer.Visible = false;
		}));
	}

	private void UpdateStatusEffectsUI()
	{
		if (!IsInstanceValid(_statusContainer) || !IsInstanceValid(_player)) return;
		var mgr = _player.StatusManager;
		var sb = new System.Text.StringBuilder();
		if (_player.IsShieldActive) sb.Append($"SH:{_player.ActiveShieldTimer:0};");
		if (mgr != null)
		{
			if (mgr.HasEffect(StatusEffectType.Burn)) sb.Append($"BN:{mgr.GetEffectValue(StatusEffectType.Burn):0};");
			if (mgr.HasEffect(StatusEffectType.Poison)) sb.Append($"PS:{mgr.GetEffectValue(StatusEffectType.Poison):0};");
			if (mgr.HasEffect(StatusEffectType.Bleed)) sb.Append($"BL:{mgr.GetEffectValue(StatusEffectType.Bleed):0};");
			if (mgr.HasEffect(StatusEffectType.Slow)) sb.Append($"SL:{mgr.GetEffectValue(StatusEffectType.Slow):0};");
		}
		string key = sb.ToString();
		if (key == _lastStatusKey) return;
		_lastStatusKey = key;

		foreach (Node child in _statusContainer.GetChildren()) child.QueueFree();
		if (key.Length == 0) return;

		var font = GameResourceCache.GetSilverFont();
		void AddPill(string text, Color c)
		{
			var p = new PanelContainer();
			p.AddThemeStyleboxOverride("panel", DungeonVisuals.CreatePillStyle(
				new Color(0.04f, 0.04f, 0.07f, 0.90f), c, 1, 2, 5, 2));
			var l = new Label
			{
				Text = text,
				VerticalAlignment = VerticalAlignment.Center
			};
			if (font != null) l.AddThemeFontOverride("font", font);
			l.AddThemeFontSizeOverride("font_size", 16);
			l.AddThemeColorOverride("font_color", c.Lightened(0.2f));
			DungeonVisuals.ApplyPixelOutline(l, 1);
			p.AddChild(l);
			_statusContainer.AddChild(p);
		}

		if (_player.IsShieldActive) AddPill($"SHIELD {_player.ActiveShieldTimer:0}s", new Color(0.32f, 0.90f, 1.0f));
		if (mgr != null)
		{
			if (mgr.HasEffect(StatusEffectType.Burn)) AddPill($"BURN {mgr.GetEffectValue(StatusEffectType.Burn):0}s", new Color(1.0f, 0.48f, 0.16f));
			if (mgr.HasEffect(StatusEffectType.Poison)) AddPill($"POISON {mgr.GetEffectValue(StatusEffectType.Poison):0}s", new Color(0.38f, 0.92f, 0.36f));
			if (mgr.HasEffect(StatusEffectType.Bleed)) AddPill($"BLEED {mgr.GetEffectValue(StatusEffectType.Bleed):0}s", new Color(0.96f, 0.24f, 0.30f));
			if (mgr.HasEffect(StatusEffectType.Slow)) AddPill($"SLOW {mgr.GetEffectValue(StatusEffectType.Slow):0}s", new Color(0.45f, 0.72f, 1.0f));
		}
	}

	private void UpdateMadnessUI()
	{
		if (!IsInstanceValid(_madnessContainer) || !IsInstanceValid(_player)) return;
		bool unlocked = _player.IsMadnessUnlocked();
		_madnessContainer.Visible = unlocked;
		if (!unlocked)
		{
			if (IsInstanceValid(_madnessEdgeFrame)) _madnessEdgeFrame.Visible = false;
			return;
		}

		var level = GetLevelCached();
		int floor = level != null ? level.CurrentLevelIndex : 1;
		float m = _player.Madness;
		_madnessBar.Value = m;

		int ratePct = Mathf.RoundToInt((GameLogic.GetMadnessAttackRateMultiplier(floor, m) - 1.0f) * 100.0f);
		int critPct = Mathf.RoundToInt(GameLogic.GetMadnessCritChanceBonus(floor, m) * 100.0f);

		_madnessLabel.Text = m > 0.5f
			? $"MADNESS {(int)m}%  +{ratePct}%SPD +{critPct}%CRT"
			: "MADNESS READY";

		if (_madnessFillStyle != null)
		{
			float ratio = m / 100.0f;
			_madnessFillStyle.BgColor = new Color(0.85f, 0.16f + ratio * 0.25f, 0.22f, 0.92f);
		}

		if (IsInstanceValid(_madnessEdgeFrame))
		{
			_madnessEdgeFrame.Visible = m >= 20.0f;
			if (_madnessEdgeFrame.Visible)
			{
				float pulse = 0.5f + 0.5f * Mathf.Sin(Time.GetTicksMsec() * 0.01f);
				_madnessEdgeFrame.Modulate = new Color(1.0f, 1.0f, 1.0f, (m / 100.0f) * (0.45f + 0.35f * pulse));
			}
		}
	}

	public void BindLocalPlayer(Player player)
	{
		_player = player;
		if (_player != null && _player.Config != null)
		{
			UpdateVitalsPanelWidth(_player.MaxHealthEffective, _player.MaxManaEffective);
		}
		UpdateAbilitiesUI();
		UpdateRelicsUI();
		UpdateWeaponsUI();
		UpdatePartyRosterUI();
		RefreshStatsImmediate();
	}

	private void CreateCoopPartyAndBannersUI(Font font)
	{
		_spectateBanner = new PanelContainer
		{
			Name = "SpectateBanner",
			LayoutMode = 1,
			MouseFilter = Control.MouseFilterEnum.Ignore,
			Visible = false
		};
		_spectateBanner.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
		_spectateBanner.OffsetLeft = -190;
		_spectateBanner.OffsetRight = 190;
		_spectateBanner.OffsetTop = -152;
		_spectateBanner.OffsetBottom = -124;
		_spectateBanner.AddThemeStyleboxOverride("panel", DungeonVisuals.CreatePanelStyle(
			new Color(0.05f, 0.05f, 0.08f, 0.90f),
			new Color(0.55f, 0.75f, 0.98f, 0.85f),
			2, 4, 4));
		_hudRoot.AddChild(_spectateBanner);

		_spectateLabel = new Label
		{
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center
		};
		if (font != null) _spectateLabel.AddThemeFontOverride("font", font);
		_spectateLabel.AddThemeFontSizeOverride("font_size", 17);
		_spectateLabel.AddThemeColorOverride("font_color", new Color(0.84f, 0.92f, 1.0f));
		DungeonVisuals.ApplyPixelOutline(_spectateLabel, 2);
		_spectateBanner.AddChild(_spectateLabel);

		_ladderReadyBanner = new PanelContainer
		{
			Name = "LadderReadyBanner",
			LayoutMode = 1,
			MouseFilter = Control.MouseFilterEnum.Ignore,
			Visible = false
		};
		_ladderReadyBanner.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
		_ladderReadyBanner.OffsetLeft = -165;
		_ladderReadyBanner.OffsetRight = 165;
		_ladderReadyBanner.OffsetTop = 44;
		_ladderReadyBanner.OffsetBottom = 68;
		_ladderReadyBanner.AddThemeStyleboxOverride("panel", DungeonVisuals.CreatePanelStyle(
			new Color(0.06f, 0.08f, 0.06f, 0.90f),
			DungeonVisuals.BrassGold,
			1, 3, 4));
		_hudRoot.AddChild(_ladderReadyBanner);

		_ladderReadyLabel = new Label
		{
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center
		};
		if (font != null) _ladderReadyLabel.AddThemeFontOverride("font", font);
		_ladderReadyLabel.AddThemeFontSizeOverride("font_size", 16);
		_ladderReadyLabel.AddThemeColorOverride("font_color", new Color(1.0f, 0.88f, 0.45f));
		DungeonVisuals.ApplyPixelOutline(_ladderReadyLabel, 2);
		_ladderReadyBanner.AddChild(_ladderReadyLabel);
	}

	public void SetSpectateBanner(bool visible, string targetName)
	{
		if (!IsInstanceValid(_spectateBanner) || !IsInstanceValid(_spectateLabel)) return;
		_spectateBanner.Visible = visible;
		if (visible)
		{
			_spectateLabel.Text = string.IsNullOrEmpty(targetName)
				? "FALLEN  -  WAITING FOR TEAM"
				: $"SPECTATING: {targetName.ToUpper()}   [A / D] SWITCH";
		}
	}

	public void SetLadderReadyBanner(bool visible, int readyCount, int aliveCount, float secondsLeft)
	{
		if (!IsInstanceValid(_ladderReadyBanner) || !IsInstanceValid(_ladderReadyLabel)) return;
		_ladderReadyBanner.Visible = visible;
		if (visible)
		{
			int secs = Mathf.Max(0, Mathf.CeilToInt(secondsLeft));
			_ladderReadyLabel.Text = $"EXIT READY: {readyCount}/{Mathf.Max(1, aliveCount)}   ({secs}s)";
		}
	}

	public void UpdatePartyRosterUI()
	{
		if (!IsInstanceValid(_partyRosterContainer)) return;

		if (NetworkManager.Instance == null || !NetworkManager.Instance.IsCoopSession)
		{
			_partyRosterContainer.Visible = false;
			if (IsInstanceValid(_tabPartySection)) _tabPartySection.Visible = false;
			return;
		}

		var all = NetworkManager.GetAllPlayers(GetTree());
		var teammates = new System.Collections.Generic.List<Player>();
		foreach (var p in all)
		{
			if (!p.IsLocalPlayer) teammates.Add(p);
		}

		if (teammates.Count == 0)
		{
			_partyRosterContainer.Visible = false;
			if (IsInstanceValid(_tabPartySection)) _tabPartySection.Visible = false;
			return;
		}

		_partyRosterContainer.Visible = true;
		if (IsInstanceValid(_tabPartySection)) _tabPartySection.Visible = true;
		foreach (Node child in _partyRosterContainer.GetChildren())
		{
			_partyRosterContainer.RemoveChild(child);
			child.QueueFree();
		}

		var font = GameResourceCache.GetSilverFont();
		foreach (var mate in teammates)
		{
			string heroName = mate.Config != null ? mate.Config.CharacterName : "Hero";
			string heroShort = heroName.Length >= 3 ? heroName.Substring(0, 3).ToUpper() : heroName.ToUpper();
			bool dead = mate.IsDead || mate.CurrentHealth <= 0.0f;

			var panel = new PanelContainer
			{
				CustomMinimumSize = new Vector2(190, 30),
				MouseFilter = Control.MouseFilterEnum.Ignore
			};
			Color border = dead ? new Color(0.55f, 0.24f, 0.26f, 0.85f) : new Color(0.55f, 0.46f, 0.28f, 0.85f);
			panel.AddThemeStyleboxOverride("panel", DungeonVisuals.CreatePillStyle(
				new Color(0.06f, 0.065f, 0.105f, 0.92f), border, 1, 4, 8, 5));
			_partyRosterContainer.AddChild(panel);

			var vbox = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
			vbox.AddThemeConstantOverride("separation", 2);
			panel.AddChild(vbox);

			var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
			vbox.AddChild(row);

			var nameLbl = new Label
			{
				Text = $"[{heroShort}] {mate.PlayerDisplayName}",
				SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
				VerticalAlignment = VerticalAlignment.Center,
				MouseFilter = Control.MouseFilterEnum.Ignore
			};
			if (font != null) nameLbl.AddThemeFontOverride("font", font);
			nameLbl.AddThemeFontSizeOverride("font_size", 13);
			nameLbl.AddThemeColorOverride("font_color", dead ? new Color(0.65f, 0.45f, 0.48f) : new Color(0.92f, 0.88f, 0.78f));
			DungeonVisuals.ApplyPixelOutline(nameLbl, 1);
			row.AddChild(nameLbl);

			var hpLbl = new Label
			{
				Text = dead ? "DEAD" : $"{(int)mate.CurrentHealth}/{(int)mate.MaxHealthEffective}",
				VerticalAlignment = VerticalAlignment.Center,
				MouseFilter = Control.MouseFilterEnum.Ignore
			};
			if (font != null) hpLbl.AddThemeFontOverride("font", font);
			hpLbl.AddThemeFontSizeOverride("font_size", 13);
			hpLbl.AddThemeColorOverride("font_color", dead ? new Color(0.95f, 0.32f, 0.36f) : new Color(0.88f, 0.94f, 0.86f));
			DungeonVisuals.ApplyPixelOutline(hpLbl, 1);
			row.AddChild(hpLbl);

			var hpBar = new ProgressBar
			{
				CustomMinimumSize = new Vector2(0, 5),
				MaxValue = Mathf.Max(1.0f, mate.MaxHealthEffective),
				Value = dead ? 0.0f : Mathf.Max(0.0f, mate.CurrentHealth),
				ShowPercentage = false,
				MouseFilter = Control.MouseFilterEnum.Ignore
			};
			hpBar.AddThemeStyleboxOverride("background", new StyleBoxFlat { BgColor = new Color(0.08f, 0.08f, 0.11f, 0.9f) });
			hpBar.AddThemeStyleboxOverride("fill", new StyleBoxFlat { BgColor = dead ? new Color(0.45f, 0.2f, 0.2f) : new Color(0.82f, 0.22f, 0.28f) });
			vbox.AddChild(hpBar);
		}
	}

	private void CreateStatsPanelUI(Font font)
	{
		Font boldStatsFont = GameResourceCache.CreateFontVariation(font, embolden: 0.45f);

		_statsPanel = new PanelContainer
		{
			Name = "IsaacDeadlockStatsPanel",
			LayoutMode = 1,
			MouseFilter = Control.MouseFilterEnum.Ignore,
			Modulate = new Color(1.0f, 1.0f, 1.0f, 0.18f)
		};
		_statsPanel.SetAnchorsPreset(Control.LayoutPreset.CenterLeft);
		_statsPanel.GrowVertical = Control.GrowDirection.Both;
		_statsPanel.OffsetLeft = 10;
		_statsPanel.OffsetRight = 112;
		_statsPanel.OffsetTop = -64;
		_statsPanel.OffsetBottom = 64;
		_statsPanel.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
		_hudRoot.AddChild(_statsPanel);

		var rows = new VBoxContainer
		{
			Alignment = BoxContainer.AlignmentMode.Center,
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		rows.AddThemeConstantOverride("separation", 3);
		_statsPanel.AddChild(rows);

		Label AddStatRow(StatGlyph.GlyphType glyphType, string tag, Color color, out Control rowContainer)
		{
			var row = new HBoxContainer
			{
				MouseFilter = Control.MouseFilterEnum.Ignore
			};
			row.AddThemeConstantOverride("separation", 4);

			var glyph = new StatGlyph(glyphType, color);
			row.AddChild(glyph);

			var tagLbl = new Label
			{
				Text = tag,
				CustomMinimumSize = new Vector2(28, 0),
				HorizontalAlignment = HorizontalAlignment.Left,
				VerticalAlignment = VerticalAlignment.Center,
				SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
				MouseFilter = Control.MouseFilterEnum.Ignore
			};
			if (boldStatsFont != null) tagLbl.AddThemeFontOverride("font", boldStatsFont);
			tagLbl.AddThemeFontSizeOverride("font_size", 14);
			tagLbl.AddThemeColorOverride("font_color", color.Lerp(DungeonVisuals.Parchment, 0.42f));
			DungeonVisuals.ApplyPixelOutline(tagLbl, 2);
			row.AddChild(tagLbl);

			var valLbl = new Label
			{
				Text = "-",
				HorizontalAlignment = HorizontalAlignment.Left,
				VerticalAlignment = VerticalAlignment.Center,
				SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
				MouseFilter = Control.MouseFilterEnum.Ignore
			};
			if (boldStatsFont != null) valLbl.AddThemeFontOverride("font", boldStatsFont);
			valLbl.AddThemeFontSizeOverride("font_size", 15);
			valLbl.AddThemeColorOverride("font_color", DungeonVisuals.Parchment);
			DungeonVisuals.ApplyPixelOutline(valLbl, 2);
			row.AddChild(valLbl);

			rows.AddChild(row);
			rowContainer = row;
			return valLbl;
		}

		_statDmgLabel = AddStatRow(StatGlyph.GlyphType.Damage, "DMG", new Color(1.0f, 0.62f, 0.30f), out _);
		_statRofLabel = AddStatRow(StatGlyph.GlyphType.AttackRate, "ROF", new Color(1.0f, 0.86f, 0.36f), out _);
		_statCritLabel = AddStatRow(StatGlyph.GlyphType.Crit, "CRT", new Color(1.0f, 0.38f, 0.48f), out _);
		_statSpdLabel = AddStatRow(StatGlyph.GlyphType.Speed, "SPD", new Color(0.44f, 0.86f, 1.0f), out _);
		_statHprLabel = AddStatRow(StatGlyph.GlyphType.HpRegen, "HPR", new Color(0.42f, 0.94f, 0.56f), out _);
		_statMprLabel = AddStatRow(StatGlyph.GlyphType.MpRegen, "MPR", new Color(0.42f, 0.72f, 1.0f), out _);
		_statMadLabel = AddStatRow(StatGlyph.GlyphType.Madness, "MAD", new Color(0.98f, 0.32f, 0.36f), out _statMadRow);
		_statMadRow.Visible = false;
	}

	private void CreateTabOverlayUI(Font font)
	{
		_tabOverlay = new PanelContainer
		{
			Name = "TabInfoOverlay",
			LayoutMode = 1,
			MouseFilter = Control.MouseFilterEnum.Ignore,
			Visible = false
		};
		_tabOverlay.SetAnchorsPreset(Control.LayoutPreset.TopRight);
		_tabOverlay.GrowHorizontal = Control.GrowDirection.Begin;
		_tabOverlay.GrowVertical = Control.GrowDirection.End;
		_tabOverlay.OffsetLeft = -10;
		_tabOverlay.OffsetRight = -10;
		_tabOverlay.OffsetTop = 64;
		_tabOverlay.OffsetBottom = 64;

		var outerStyle = DungeonVisuals.CreatePillStyle(
			new Color(0.042f, 0.046f, 0.078f, 0.92f),
			new Color(0.78f, 0.64f, 0.30f, 0.82f),
			2, 5, 11, 9);
		outerStyle.ShadowSize = 6;
		outerStyle.ShadowColor = new Color(0f, 0f, 0f, 0.60f);
		_tabOverlay.AddThemeStyleboxOverride("panel", outerStyle);
		_hudRoot.AddChild(_tabOverlay);

		var rootVBox = new VBoxContainer
		{
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		rootVBox.AddThemeConstantOverride("separation", 6);
		_tabOverlay.AddChild(rootVBox);

		var headerRow = new HBoxContainer
		{
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		headerRow.AddThemeConstantOverride("separation", 8);
		rootVBox.AddChild(headerRow);

		var headerTitleLabel = new Label
		{
			Text = "RUN OVERVIEW",
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			VerticalAlignment = VerticalAlignment.Center,
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		if (font != null) headerTitleLabel.AddThemeFontOverride("font", font);
		headerTitleLabel.AddThemeFontSizeOverride("font_size", 16);
		headerTitleLabel.AddThemeColorOverride("font_color", DungeonVisuals.BrassGold);
		DungeonVisuals.ApplyPixelOutline(headerTitleLabel, 2);
		headerRow.AddChild(headerTitleLabel);

		var keyPill = new PanelContainer
		{
			SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		keyPill.AddThemeStyleboxOverride("panel", DungeonVisuals.CreatePillStyle(
			new Color(0.09f, 0.10f, 0.15f, 0.92f),
			new Color(0.45f, 0.48f, 0.58f, 0.70f),
			1, 3, 5, 2));
		headerRow.AddChild(keyPill);

		var closeHint = new Label
		{
			Text = "TAB",
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		if (font != null) closeHint.AddThemeFontOverride("font", font);
		closeHint.AddThemeFontSizeOverride("font_size", 14);
		closeHint.AddThemeColorOverride("font_color", new Color(0.76f, 0.80f, 0.88f));
		DungeonVisuals.ApplyPixelOutline(closeHint, 1);
		keyPill.AddChild(closeHint);

		var divider = new ColorRect
		{
			CustomMinimumSize = new Vector2(0, 1),
			Color = new Color(0.82f, 0.68f, 0.32f, 0.28f),
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		rootVBox.AddChild(divider);

		// Co-op Party section
		_tabPartySection = new VBoxContainer
		{
			MouseFilter = Control.MouseFilterEnum.Ignore,
			Visible = false
		};
		_tabPartySection.AddThemeConstantOverride("separation", 4);
		rootVBox.AddChild(_tabPartySection);

		var partyHdr = new Label
		{
			Text = "PARTY",
			VerticalAlignment = VerticalAlignment.Center,
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		if (font != null) partyHdr.AddThemeFontOverride("font", font);
		partyHdr.AddThemeFontSizeOverride("font_size", 12);
		partyHdr.AddThemeColorOverride("font_color", new Color(0.58f, 0.84f, 1.0f));
		DungeonVisuals.ApplyPixelOutline(partyHdr, 1);
		_tabPartySection.AddChild(partyHdr);

		_partyRosterContainer = new VBoxContainer
		{
			Name = "PartyRosterContainer",
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		_partyRosterContainer.AddThemeConstantOverride("separation", 4);
		_tabPartySection.AddChild(_partyRosterContainer);

		var partyDivider = new ColorRect
		{
			CustomMinimumSize = new Vector2(0, 1),
			Color = new Color(0.45f, 0.52f, 0.68f, 0.24f),
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		_tabPartySection.AddChild(partyDivider);

		_relicsEmptyLabel = new Label
		{
			Text = "No relics or upgrades collected yet",
			VerticalAlignment = VerticalAlignment.Center,
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		if (font != null) _relicsEmptyLabel.AddThemeFontOverride("font", font);
		_relicsEmptyLabel.AddThemeFontSizeOverride("font_size", 16);
		_relicsEmptyLabel.AddThemeColorOverride("font_color", new Color(0.62f, 0.65f, 0.74f));
		DungeonVisuals.ApplyPixelOutline(_relicsEmptyLabel, 1);
		rootVBox.AddChild(_relicsEmptyLabel);

		// 1. Relics section
		_tabRelicsSection = new VBoxContainer
		{
			MouseFilter = Control.MouseFilterEnum.Ignore,
			Visible = false
		};
		_tabRelicsSection.AddThemeConstantOverride("separation", 4);
		rootVBox.AddChild(_tabRelicsSection);

		var relicsHdr = new Label
		{
			Text = "RELICS",
			VerticalAlignment = VerticalAlignment.Center,
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		if (font != null) relicsHdr.AddThemeFontOverride("font", font);
		relicsHdr.AddThemeFontSizeOverride("font_size", 13);
		relicsHdr.AddThemeColorOverride("font_color", DungeonVisuals.ArcaneCyan);
		DungeonVisuals.ApplyPixelOutline(relicsHdr, 1);
		_tabRelicsSection.AddChild(relicsHdr);

		var relicsGrid = new GridContainer
		{
			Name = "RelicsList",
			Columns = 1,
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		relicsGrid.AddThemeConstantOverride("h_separation", 6);
		relicsGrid.AddThemeConstantOverride("v_separation", 5);
		_tabRelicsSection.AddChild(relicsGrid);
		_relicsContainer = relicsGrid;

		_tabSectionsDivider = new ColorRect
		{
			CustomMinimumSize = new Vector2(0, 1),
			Color = new Color(0.82f, 0.68f, 0.32f, 0.22f),
			MouseFilter = Control.MouseFilterEnum.Ignore,
			Visible = false
		};
		rootVBox.AddChild(_tabSectionsDivider);

		// 2. Upgrades section
		_tabUpgradesSection = new VBoxContainer
		{
			MouseFilter = Control.MouseFilterEnum.Ignore,
			Visible = false
		};
		_tabUpgradesSection.AddThemeConstantOverride("separation", 4);
		rootVBox.AddChild(_tabUpgradesSection);

		var upgradesHdr = new Label
		{
			Text = "UPGRADES",
			VerticalAlignment = VerticalAlignment.Center,
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		if (font != null) upgradesHdr.AddThemeFontOverride("font", font);
		upgradesHdr.AddThemeFontSizeOverride("font_size", 13);
		upgradesHdr.AddThemeColorOverride("font_color", DungeonVisuals.BrassGold);
		DungeonVisuals.ApplyPixelOutline(upgradesHdr, 1);
		_tabUpgradesSection.AddChild(upgradesHdr);

		var upgradesGrid = new GridContainer
		{
			Name = "UpgradesList",
			Columns = 1,
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		upgradesGrid.AddThemeConstantOverride("h_separation", 6);
		upgradesGrid.AddThemeConstantOverride("v_separation", 5);
		_tabUpgradesSection.AddChild(upgradesGrid);
		_upgradesContainer = upgradesGrid;

		CreateTabTooltipUI(font);
	}

	private void ToggleTabOverlay()
	{
		_isTabOpen = !_isTabOpen;
		if (IsInstanceValid(_tabOverlay))
		{
			_tabOverlay.Visible = _isTabOpen;
		}

		if (_isTabOpen)
		{
			UpdateTabOverlayAnchor();
			UpdateRelicsUI();
			UpdatePartyRosterUI();
		}
		else
		{
			UpdateTabHoverTooltip();
		}
		RefreshStatsImmediate();

		if (IsInstanceValid(_statsPanel))
		{
			if (IsInstanceValid(_statsFadeTween)) _statsFadeTween.Kill();
			float targetAlpha = _isTabOpen ? 0.98f : 0.22f;
			_statsFadeTween = CreateTween();
			_statsFadeTween.TweenProperty(_statsPanel, "modulate:a", targetAlpha, 0.12);
		}
	}

	private void RefreshStatsImmediate()
	{
		_lastRelicCount = -1;
		_lastShopStacks = -1;
		_hasStatsCache = false;
		UpdateStatsUI();
	}

	private void UpdateStatsUI()
	{
		if (!IsInstanceValid(_statsPanel) || !IsInstanceValid(_player) || _player.IsQueuedForDeletion()) return;

		int relicCount = _player.RelicCount;
		int shopStacks = 0;
		if (_player.ShopPurchaseCounts != null)
		{
			foreach (var kv in _player.ShopPurchaseCounts) shopStacks += kv.Value;
		}
		if (relicCount != _lastRelicCount || shopStacks != _lastShopStacks)
		{
			_lastRelicCount = relicCount;
			_lastShopStacks = shopStacks;
			UpdateRelicsUI();
		}

		WeaponData weapon = _player.ActiveWeaponData;
		bool hasWeapon = weapon != null;
		float weaponDmg = hasWeapon ? weapon.Damage : 0.0f;
		float weaponCd = hasWeapon ? Mathf.Max(0.01f, weapon.AttackCooldown) : 0.0f;
		int multPct = Mathf.RoundToInt(_player.WeaponDamageMultiplier * 100.0f);
		float rateMult = Mathf.Max(1.0f, _player.GetMadnessAttackRateMultiplier());
		bool isBlade = hasWeapon && !weapon.IsRanged && !weapon.IsContinuousLaser;
		int critPct = Mathf.RoundToInt(_player.GetCritChance(isBlade) * 100.0f);
		int moveSpd = Mathf.RoundToInt(_player.CurrentMoveSpeed);
		float hpr = _player.HealthRegenEffective;
		float mpr = _player.ManaRegenEffective;
		bool madnessUnlocked = _player.IsMadnessUnlocked();
		int madPct = (int)_player.Madness;

		if (_hasStatsCache
			&& hasWeapon == _lastStatsHasWeapon
			&& Mathf.Abs(weaponDmg - _lastStatsWeaponDmg) < 0.0001f
			&& Mathf.Abs(weaponCd - _lastStatsWeaponCd) < 0.0001f
			&& multPct == _lastStatsMultPct
			&& Mathf.Abs(rateMult - _lastStatsRateMult) < 0.0005f
			&& critPct == _lastStatsCritPct
			&& moveSpd == _lastStatsMoveSpd
			&& Mathf.Abs(hpr - _lastStatsHpr) < 0.001f
			&& Mathf.Abs(mpr - _lastStatsMpr) < 0.001f
			&& madnessUnlocked == _lastStatsMadUnlocked
			&& madPct == _lastStatsMadPct)
		{
			return;
		}

		_hasStatsCache = true;
		_lastStatsHasWeapon = hasWeapon;
		_lastStatsWeaponDmg = weaponDmg;
		_lastStatsWeaponCd = weaponCd;
		_lastStatsMultPct = multPct;
		_lastStatsRateMult = rateMult;
		_lastStatsCritPct = critPct;
		_lastStatsMoveSpd = moveSpd;
		_lastStatsHpr = hpr;
		_lastStatsMpr = mpr;
		_lastStatsMadUnlocked = madnessUnlocked;
		_lastStatsMadPct = madPct;

		_statDmgLabel.Text = hasWeapon
			? $"{Mathf.RoundToInt(weaponDmg * _player.WeaponDamageMultiplier)} ({multPct}%)"
			: $"{multPct}%";

		if (hasWeapon)
		{
			float cd = Mathf.Max(0.03f, weaponCd / rateMult);
			_statRofLabel.Text = $"{(1.0f / cd):0.0}/s";
		}
		else
		{
			_statRofLabel.Text = $"{rateMult:0.00}x";
		}

		_statCritLabel.Text = $"{critPct}%";
		_statSpdLabel.Text = moveSpd.ToString();
		_statHprLabel.Text = $"+{hpr:0.0}/s";
		_statMprLabel.Text = $"+{mpr:0.0}/s";

		if (IsInstanceValid(_statMadRow))
		{
			_statMadRow.Visible = madnessUnlocked;
			if (madnessUnlocked && IsInstanceValid(_statMadLabel))
			{
				_statMadLabel.Text = $"{madPct}%";
			}
		}
	}
}

public partial class StatGlyph : Control
{
	public enum GlyphType
	{
		Damage,
		AttackRate,
		Crit,
		Speed,
		HpRegen,
		MpRegen,
		Madness
	}

	private readonly GlyphType _type;
	private readonly Color _color;

	public StatGlyph(GlyphType type, Color color)
	{
		_type = type;
		_color = color;
		CustomMinimumSize = new Vector2(10, 12);
		Size = new Vector2(10, 12);
		SizeFlagsVertical = SizeFlags.ShrinkCenter;
		MouseFilter = MouseFilterEnum.Ignore;
	}

	public override void _Draw()
	{
		Color sh = new Color(0f, 0f, 0f, 0.82f);
		Vector2 d = new Vector2(1, 1);
		switch (_type)
		{
			case GlyphType.Damage:
				DrawColoredPolygon(new[] { new Vector2(5, 1) + d, new Vector2(9, 7) + d, new Vector2(1, 7) + d }, sh);
				DrawRect(new Rect2(4 + d.X, 7 + d.Y, 2, 4), sh);
				DrawColoredPolygon(new[] { new Vector2(5, 1), new Vector2(9, 7), new Vector2(1, 7) }, _color);
				DrawRect(new Rect2(4, 7, 2, 4), _color.Darkened(0.2f));
				break;
			case GlyphType.AttackRate:
				DrawColoredPolygon(new[] { new Vector2(6, 1) + d, new Vector2(2, 6) + d, new Vector2(5, 6) + d, new Vector2(4, 11) + d, new Vector2(8, 5) + d, new Vector2(5, 5) + d }, sh);
				DrawColoredPolygon(new[] { new Vector2(6, 1), new Vector2(2, 6), new Vector2(5, 6), new Vector2(4, 11), new Vector2(8, 5), new Vector2(5, 5) }, _color);
				break;
			case GlyphType.Crit:
				DrawColoredPolygon(new[] { new Vector2(5, 1) + d, new Vector2(9, 6) + d, new Vector2(5, 11) + d, new Vector2(1, 6) + d }, sh);
				DrawColoredPolygon(new[] { new Vector2(5, 1), new Vector2(9, 6), new Vector2(5, 11), new Vector2(1, 6) }, _color);
				break;
			case GlyphType.Speed:
				DrawColoredPolygon(new[] { new Vector2(1, 2) + d, new Vector2(6, 6) + d, new Vector2(1, 10) + d }, sh);
				DrawColoredPolygon(new[] { new Vector2(5, 2) + d, new Vector2(9, 6) + d, new Vector2(5, 10) + d }, sh);
				DrawColoredPolygon(new[] { new Vector2(1, 2), new Vector2(6, 6), new Vector2(1, 10) }, _color);
				DrawColoredPolygon(new[] { new Vector2(5, 2), new Vector2(9, 6), new Vector2(5, 10) }, _color.Lightened(0.25f));
				break;
			case GlyphType.HpRegen:
				DrawRect(new Rect2(4 + d.X, 2 + d.Y, 2, 8), sh);
				DrawRect(new Rect2(1 + d.X, 5 + d.Y, 8, 2), sh);
				DrawRect(new Rect2(4, 2, 2, 8), _color);
				DrawRect(new Rect2(1, 5, 8, 2), _color);
				break;
			case GlyphType.MpRegen:
				DrawColoredPolygon(new[] { new Vector2(5, 1) + d, new Vector2(8.5f, 7.5f) + d, new Vector2(5, 10.5f) + d, new Vector2(1.5f, 7.5f) + d }, sh);
				DrawColoredPolygon(new[] { new Vector2(5, 1), new Vector2(8.5f, 7.5f), new Vector2(5, 10.5f), new Vector2(1.5f, 7.5f) }, _color);
				break;
			case GlyphType.Madness:
				DrawCircle(new Vector2(5, 7) + d, 3.5f, sh);
				DrawCircle(new Vector2(5, 7), 3.5f, _color);
				DrawColoredPolygon(new[] { new Vector2(5, 1), new Vector2(8, 6), new Vector2(2, 6) }, _color.Lightened(0.25f));
				break;
		}
	}
}

public partial class ArrowIndicator : Control
{
	public float Angle = 0.0f;
	public Color Color = new Color(0.2f, 1.0f, 0.2f, 0.9f); // Ярко-зеленый

	public override void _Draw()
	{
		Vector2[] shadowPts = new Vector2[] {
			new Vector2(18.5f, 0).Rotated(Angle) + new Vector2(2, 2.5f),
			new Vector2(-11.4f, -12.6f).Rotated(Angle) + new Vector2(2, 2.5f),
			new Vector2(-5.6f, 0).Rotated(Angle) + new Vector2(2, 2.5f),
			new Vector2(-11.4f, 12.6f).Rotated(Angle) + new Vector2(2, 2.5f)
		};
		DrawPolygon(shadowPts, new Color[] {
			new Color(0, 0, 0, 0.45f), new Color(0, 0, 0, 0.45f), new Color(0, 0, 0, 0.45f), new Color(0, 0, 0, 0.45f)
		});

		Vector2[] points = new Vector2[] {
			new Vector2(18.5f, 0).Rotated(Angle),
			new Vector2(-10.8f, -12.2f).Rotated(Angle),
			new Vector2(-4.9f, 0).Rotated(Angle),
			new Vector2(-10.8f, 12.2f).Rotated(Angle)
		};
		DrawPolygon(points, new Color[] { Color, Color, Color, Color });

		Color core = Color.Lightened(0.55f);
		Vector2[] innerPoints = new Vector2[] {
			new Vector2(12.2f, 0).Rotated(Angle),
			new Vector2(-4.9f, -6.1f).Rotated(Angle),
			new Vector2(-2.5f, 0).Rotated(Angle),
			new Vector2(-4.9f, 6.1f).Rotated(Angle)
		};
		DrawPolygon(innerPoints, new Color[] { core, core, core, core });
		
		// Обводка для контраста
		Vector2[] outline = new Vector2[] {
			points[0], points[1], points[2], points[3], points[0]
		};
		DrawPolyline(outline, new Color(0.03f, 0.03f, 0.05f, 0.95f), 2.8f, true);
	}
}

public partial class WeaponShowcaseSlot : Control
{
	private static readonly System.Collections.Generic.Dictionary<string, Texture2D> s_weaponTexCache =
		new System.Collections.Generic.Dictionary<string, Texture2D>();

	private readonly int _slotNumber;
	private readonly Label _keyBadgeLabel;
	private WeaponData _currentWeapon;
	private Texture2D _weaponTexture;
	private bool _isSelected = false;
	private float _reloadProgress = -1.0f;

	public WeaponShowcaseSlot(int slotNumber, Font font)
	{
		_slotNumber = slotNumber;
		CustomMinimumSize = new Vector2(64, 64);
		Size = new Vector2(64, 64);
		TextureFilter = TextureFilterEnum.Nearest;

		_keyBadgeLabel = new Label();
		_keyBadgeLabel.Text = slotNumber.ToString();
		_keyBadgeLabel.Position = new Vector2(4, 4);
		_keyBadgeLabel.Size = new Vector2(16, 16);
		_keyBadgeLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_keyBadgeLabel.VerticalAlignment = VerticalAlignment.Center;
		if (font != null) _keyBadgeLabel.AddThemeFontOverride("font", font);
		_keyBadgeLabel.AddThemeFontSizeOverride("font_size", 15);
		_keyBadgeLabel.AddThemeColorOverride("font_color", new Color(0.75f, 0.75f, 0.8f));
		DungeonVisuals.ApplyPixelOutline(_keyBadgeLabel, 2);
		AddChild(_keyBadgeLabel);
	}

	public void UpdateSlot(WeaponData weapon, bool isSelected, float reloadProgress = -1.0f)
	{
		bool changed = (_currentWeapon != weapon) || (_isSelected != isSelected) || (Mathf.Abs(_reloadProgress - reloadProgress) > 0.01f);
		_currentWeapon = weapon;
		_isSelected = isSelected;
		_reloadProgress = reloadProgress;

		if (changed)
		{
			_weaponTexture = GetWeaponTexture(weapon);
			_keyBadgeLabel.AddThemeColorOverride("font_color",
				isSelected ? new Color(1.0f, 0.88f, 0.35f) : new Color(0.62f, 0.64f, 0.70f));
		}

		// Плавно перерисовываем активный слот для анимации парения оружия
		if (_isSelected || changed)
		{
			QueueRedraw();
		}
	}

	public static Texture2D ExtractWeaponTextureStatic(WeaponData weapon) => GetWeaponTexture(weapon);

	public static string FormatWeaponStatsTooltip(WeaponData weapon, float damageMultiplier = 1.0f, bool isGroundPickup = false, bool hasPrismRelic = false)
	{
		if (weapon == null) return "";

		string displayName = weapon.WeaponName;
		if (string.IsNullOrEmpty(displayName) || displayName == "Новое оружие")
		{
			string path = !string.IsNullOrEmpty(weapon.ResourcePath) ? weapon.ResourcePath : weapon.OriginalResourcePath;
			displayName = !string.IsNullOrEmpty(path) ? System.IO.Path.GetFileNameWithoutExtension(path) : "WEAPON";
		}

		float effectiveDmg = weapon.Damage * damageMultiplier;
		string ammoText = weapon.IsContinuousLaser
			? "BEAM"
			: ((weapon.IsRanged && weapon.MaxAmmo < 500) ? weapon.MaxAmmo.ToString() : (weapon.IsRanged ? "INF" : "-"));

		string header = isGroundPickup ? $"{displayName.ToUpper()} [E]" : displayName.ToUpper();
		string manaLine = weapon.IsContinuousLaser
			? $"\nMP/S: {weapon.ManaCost:0.#}  SPLASH: {(weapon.SplashRadius > 0 ? weapon.SplashRadius : 28f) * (hasPrismRelic ? 1.35f : 1.0f):0}px" + (hasPrismRelic ? " BOUNCE" : "")
			: (weapon.ManaCost > 0.01f ? $"\nMANA: {weapon.ManaCost:0.#}  RLD: {weapon.ReloadTime:0.##}s" : "");
		return $"{header}\n" +
			   $"DMG: {effectiveDmg:0.#}   CD: {weapon.AttackCooldown:0.##}s\n" +
			   $"AMMO: {ammoText}   KNCK: {weapon.KnockbackForce:0.#}{manaLine}";
	}

	private static Texture2D GetWeaponTexture(WeaponData weapon)
	{
		if (weapon == null || weapon.WeaponVisualPrefab == null) return null;

		string effPath = weapon.GetEffectiveResourcePath();
		string key = !string.IsNullOrEmpty(effPath) ? effPath : (weapon.WeaponName ?? "default");
		if (s_weaponTexCache.TryGetValue(key, out var cached) && cached != null)
		{
			return cached;
		}

		var instance = weapon.WeaponVisualPrefab.Instantiate();
		Texture2D foundTex = FindTextureRecursive(instance);
		instance.Free();

		if (foundTex != null)
		{
			s_weaponTexCache[key] = foundTex;
		}
		return foundTex;
	}

	private static Texture2D FindTextureRecursive(Node node)
	{
		if (node is Sprite2D s && !s.Name.ToString().Contains("Slash") && s.Texture != null)
		{
			return s.Texture;
		}
		if (node is AnimatedSprite2D anim && anim.SpriteFrames != null)
		{
			if (anim.SpriteFrames.HasAnimation("idle") && anim.SpriteFrames.GetFrameCount("idle") > 0)
			{
				return anim.SpriteFrames.GetFrameTexture("idle", 0);
			}
		}
		foreach (Node child in node.GetChildren())
		{
			var tex = FindTextureRecursive(child);
			if (tex != null) return tex;
		}
		return null;
	}

	public override void _Draw()
	{
		Vector2 sz = Size;
		float bob = _isSelected ? Mathf.Sin(Time.GetTicksMsec() * 0.006f) * 2.5f : 0.0f;

		// 1. Внешняя тень и декоративная подложка пьедестала
		DrawRect(new Rect2(2, 3.5f, sz.X, sz.Y), new Color(0f, 0f, 0f, 0.55f));

		Color bgOuter = _isSelected
			? new Color(0.18f, 0.14f, 0.08f, 0.96f)
			: new Color(0.055f, 0.062f, 0.095f, 0.90f);
		Color bgInner = _isSelected
			? new Color(0.28f, 0.21f, 0.11f, 0.96f)
			: new Color(0.082f, 0.092f, 0.138f, 0.92f);
		Color borderOuter = _isSelected
			? new Color(1.0f, 0.82f, 0.28f, 1.0f)
			: new Color(0.32f, 0.35f, 0.44f, 0.92f);
		Color borderInner = _isSelected
			? new Color(1.0f, 0.92f, 0.55f, 0.45f)
			: new Color(0.20f, 0.22f, 0.30f, 0.55f);

		DrawRect(new Rect2(0, 0, sz.X, sz.Y), bgOuter);
		DrawRect(new Rect2(5, 5, sz.X - 10, sz.Y - 10), bgInner);
		DrawLine(new Vector2(5, 5.5f), new Vector2(sz.X - 5, 5.5f), new Color(1f, 0.96f, 0.85f, _isSelected ? 0.22f : 0.08f), 1.0f);

		// Внутренний ромб/ореол за оружием
		Vector2 center = sz * 0.5f + new Vector2(0, 2);
		if (_isSelected)
		{
			DrawCircle(center, 22.0f, new Color(1.0f, 0.80f, 0.22f, 0.18f));
			DrawCircle(center, 15.0f, new Color(1.0f, 0.90f, 0.45f, 0.16f));
		}
		else
		{
			DrawCircle(center, 17.5f, new Color(0.04f, 0.045f, 0.07f, 0.52f));
		}

		// Двойная рамка с декоративными заклепками по углам
		DrawRect(new Rect2(1.0f, 1.0f, sz.X - 2, sz.Y - 2), borderOuter, false, 2.0f);
		DrawRect(new Rect2(4.5f, 4.5f, sz.X - 9, sz.Y - 9), borderInner, false, 1.4f);

		Color cornerColor = _isSelected ? new Color(1.0f, 0.92f, 0.55f) : new Color(0.46f, 0.49f, 0.58f);
		DrawRect(new Rect2(2, 2, 3.0f, 3.0f), cornerColor);
		DrawRect(new Rect2(sz.X - 5.0f, 2, 3.0f, 3.0f), cornerColor);
		DrawRect(new Rect2(2, sz.Y - 5.0f, 3.0f, 3.0f), cornerColor);
		DrawRect(new Rect2(sz.X - 5.0f, sz.Y - 5.0f, 3.0f, 3.0f), cornerColor);

		// Плашка горячей клавиши (1 / 2 / 3) в левом верхнем углу
		Rect2 keyBox = new Rect2(4, 4, 16, 16);
		DrawRect(keyBox, _isSelected ? new Color(0.14f, 0.10f, 0.04f, 0.96f) : new Color(0.045f, 0.05f, 0.08f, 0.94f));
		DrawRect(keyBox, _isSelected ? DungeonVisuals.BrassGold : new Color(0.32f, 0.35f, 0.44f, 0.85f), false, 1.2f);

		// 2. Отрисовка самого оружия (или пустого силуэта)
		if (_weaponTexture != null)
		{
			Vector2 texSize = _weaponTexture.GetSize();
			float maxDim = Mathf.Max(1.0f, Mathf.Max(texSize.X, texSize.Y));
			float targetSpan = _isSelected ? 44.0f : 38.0f;
			float scale = targetSpan / maxDim;

			// Вертикальные клинки/посохи наклоняем по диагонали (+40°), а огнестрел — слегка вверх (-18°)
			float angle = (texSize.Y > texSize.X * 1.15f) ? 0.68f : -0.30f;
			Vector2 drawCenter = center + new Vector2(2.0f, bob);

			// Тень под оружием
			DrawSetTransform(drawCenter + new Vector2(2.5f, 3.0f), angle, new Vector2(scale, scale));
			DrawTexture(_weaponTexture, -texSize * 0.5f, new Color(0f, 0f, 0f, 0.65f));

			// Само оружие
			Color tint = _isSelected ? new Color(1.08f, 1.08f, 1.08f, 1.0f) : new Color(0.82f, 0.84f, 0.88f, 0.92f);
			DrawSetTransform(drawCenter, angle, new Vector2(scale, scale));
			DrawTexture(_weaponTexture, -texSize * 0.5f, tint);

			// Сброс трансформации CanvasItem
			DrawSetTransform(Vector2.Zero, 0.0f, Vector2.One);

			// Полоса перезарядки внизу слота
			if (_reloadProgress >= 0.0f)
			{
				float barW = sz.X - 14.0f;
				DrawRect(new Rect2(7, sz.Y - 9, barW, 4.5f), new Color(0.05f, 0.05f, 0.08f, 0.9f));
				DrawRect(new Rect2(7, sz.Y - 9, barW * Mathf.Clamp(_reloadProgress, 0f, 1f), 4.5f), new Color(1.0f, 0.68f, 0.20f));
			}
		}
		else
		{
			// Пустой слот — аккуратная диагональная эмблема пустого крепления
			DrawLine(center + new Vector2(-9, 9), center + new Vector2(9, -9), new Color(0.32f, 0.34f, 0.40f, 0.45f), 2.5f);
			DrawCircle(center, 3.5f, new Color(0.32f, 0.34f, 0.40f, 0.45f));
		}
	}
}
