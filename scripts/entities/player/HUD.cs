using Godot;
using System;

public partial class HUD : CanvasLayer
{
	// Монеты
	private Label _coinLabel;
	
	// Здоровье
	private ProgressBar _healthBar;
	private Label _hpLabel;
	
	// Мана
	private ProgressBar _manaBar;
	private Label _mpLabel;
	
	// Таймер
	private Label _timeLabel;
	private Label _announcementLabel;
	private Control _markersContainer;
	private float _time = 5.0f;
	private bool _isCountingDown = true;
	public bool IsCountingDown => _isCountingDown;
	private float _lastMaxHealth = 0;

	private Player _player;
	private WaveManager _waveManager;
	private ArrowIndicator _arrowIndicator;
	private ArrowIndicator _shopArrowIndicator;

	private Control _abilitiesContainer;
	private AbilitySlotUI _ability1Slot;
	private AbilitySlotUI _ability2Slot;
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

	public override void _Ready()
	{
		AddToGroup("HUD");
		SettingsManager.ApplyFontQualityFixes();
		_hudRoot = new Control();
		_hudRoot.Name = "HUDRoot";
		AddChild(_hudRoot);

		var viewportSize = GetViewport().GetVisibleRect().Size;
		_hudRoot.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
		_hudRoot.Size = viewportSize / World.UiScale;
		_hudRoot.Scale = new Vector2(World.UiScale, World.UiScale);

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
		if (healthUI != null) {
			RemoveChild(healthUI);
			_hudRoot.AddChild(healthUI);
		}

		// Увеличиваем основные элементы HUD (мир зумится камерой, а интерфейс оставался мелким)
		const float hudElementScale = 1.5f;
		if (timerControl != null) timerControl.Scale *= hudElementScale;
		if (healthUI != null) {
			healthUI.Scale *= hudElementScale;
			// Блок HP/MP чуть выше от края экрана
			healthUI.Position -= new Vector2(0, 8);
		}

		// Монетка со счетчиком — крупнее остального на 20%
		if (coinControl != null) {
			coinControl.Scale *= hudElementScale * 1.2f;
			if (_coinLabel == null) {
				_coinLabel = coinControl.GetNodeOrNull<Label>("HBoxContainer/CoinLabel");
			}
			// Иконка подросла — опускаем счетчик на те же пиксели, чтобы цифры
			// стояли на уровне монетки, а не выше нее
			var coinIcon = coinControl.GetNodeOrNull<TextureRect>("HBoxContainer/TextureRect");
			if (coinIcon != null) coinIcon.CustomMinimumSize = new Vector2(6, 6);
			if (_coinLabel != null) {
				_coinLabel.VerticalAlignment = VerticalAlignment.Top;
				_coinLabel.Position += new Vector2(0, 2);
			}
		}

		_coinLabel = _hudRoot.GetNodeOrNull<Label>("CoinControl/HBoxContainer/CoinLabel");
		if (_coinLabel != null) {
			var emptyStyle = new StyleBoxEmpty();
			emptyStyle.ContentMarginBottom = 0.0f;
			_coinLabel.AddThemeStyleboxOverride("normal", emptyStyle);
		}

		_healthBar = _hudRoot.GetNodeOrNull<ProgressBar>("HealthUI/HealthBar");
		_hpLabel = _hudRoot.GetNodeOrNull<Label>("HealthUI/HPLabel");
		_manaBar = _hudRoot.GetNodeOrNull<ProgressBar>("HealthUI/ManaBar");
		_mpLabel = _hudRoot.GetNodeOrNull<Label>("HealthUI/MPLabel");
		_timeLabel = _hudRoot.GetNodeOrNull<Label>("TimerControl/Label");
		_markersContainer = _hudRoot.GetNodeOrNull<Control>("HealthUI/HealthBar/Markers");

		_player = GetTree().GetFirstNodeInGroup("Player") as Player;
		_waveManager = GetTree().GetFirstNodeInGroup("WaveManager") as WaveManager;

		// Подготавливаем объявление волны
		_announcementLabel = new Label();
		_announcementLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_announcementLabel.VerticalAlignment = VerticalAlignment.Center;
		_announcementLabel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center);
		
		// Сдвигаем на 0 пикселей (на 2 пикселя ниже, чем было в предыдущей версии)
		_announcementLabel.OffsetTop += 0;
		_announcementLabel.OffsetBottom += 0;

		var font = GD.Load<Font>("res://assets/fonts/silver/Silver.ttf");
		if (font != null) {
			_announcementLabel.AddThemeFontOverride("font", font);
		}
		_announcementLabel.AddThemeConstantOverride("letter_spacing", 2);

		_announcementLabel.AddThemeFontSizeOverride("font_size", 13);
		_announcementLabel.AddThemeColorOverride("font_color", new Color(1, 0, 0));
		_announcementLabel.Text = "First wave is coming!";
		_hudRoot.AddChild(_announcementLabel);
		_announcementLabel.Visible = false;

		UpdateTimerLabel();

		// Сразу запускаем первую волну, если время уже 0
		if (_time <= 0 && Engine.TimeScale > 0) {
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
		CreateWeaponsUI(font);
		CreateBossBarUI(font);
	}

	public override void _Process(double delta)
	{
		if (Engine.TimeScale == 0) return;

		// Обновление статов игрока
		if (IsInstanceValid(_player))
		{
			if (IsInstanceValid(_coinLabel)) {
				_coinLabel.Text = _player.CoinCount.ToString();
				// HBoxContainer пересортировывает детей при смене текста — возвращаем смещение счетчика вниз
				_coinLabel.Position = new Vector2(_coinLabel.Position.X, 2.0f);
			}

			if (_player.Config != null && IsInstanceValid(_healthBar) && IsInstanceValid(_hpLabel)) {
				float maxHp = _player.MaxHealthEffective;
				_healthBar.MaxValue = maxHp;
				_healthBar.Value = _player.CurrentHealth;
				_hpLabel.Text = $"{(int)_player.CurrentHealth}/{(int)maxHp}";
			}

			if (_player.Config != null && IsInstanceValid(_manaBar) && IsInstanceValid(_mpLabel)) {
				float maxMp = _player.MaxManaEffective;
				_manaBar.MaxValue = maxMp;
				_manaBar.Value = _player.CurrentMana;
				_mpLabel.Text = $"{(int)_player.CurrentMana}/{(int)maxMp}";
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
		UpdateWeaponsUI();
		UpdateBossBarUI();
	}

	private void ProcessTimer(float delta)
	{
		if (_isCountingDown)
		{
			_time -= delta;
			
			if (_time <= 5.0f && _time > 0) {
				_announcementLabel.Visible = true;
				float phase = _time - (float)Math.Floor(_time);
				_announcementLabel.Visible = phase < 0.6f;
			}

			if (_time <= 0)
			{
				StartGame();
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
		_announcementLabel.Visible = false;
		if (_waveManager != null) {
			_waveManager.StartNextWave();
		}
	}

	public void StartCountdown(float duration, string announcementText)
	{
		_time = duration;
		_isCountingDown = true;
		if (_announcementLabel != null)
		{
			_announcementLabel.Text = announcementText;
			_announcementLabel.Visible = true;
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

	private void UpdateExitLadderArrow()
	{
		var level = GetTree().GetFirstNodeInGroup("Level") as Level;
		UpdateTargetArrow(_arrowIndicator, level?.GetExitLadderNode(), 25.0f, -22.0f);
	}

	private void UpdateShopAltarArrow()
	{
		var level = GetTree().GetFirstNodeInGroup("Level") as Level;
		UpdateTargetArrow(_shopArrowIndicator, level?.GetShopAltarNode(), 36.0f, -34.0f);
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

		float minX = margin;
		float maxX = viewportSize.X - margin;
		float minY = margin;
		float maxY = viewportSize.Y - margin;

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
			angle = (screenPos - arrowPos).Angle();

			// Качание вперед-назад по направлению к цели
			float bounce = (float)Math.Sin(Time.GetTicksMsec() * 0.015f) * 5.0f;
			arrowPos += new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * bounce;
		}
		else
		{
			// На экране: парит над объектом и указывает вниз
			arrowPos = screenPos + new Vector2(0, hoverOffsetY);
			angle = (float)(Math.PI / 2.0f); // 90 градусов (вниз)

			// Плавное покачивание по высоте
			float wave = (float)Math.Sin(Time.GetTicksMsec() * 0.007f) * 4.0f;
			arrowPos.Y += wave;
		}

		indicator.Position = arrowPos / World.UiScale;
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
		_abilitiesContainer.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
		
		// Смещаем к центру и прижимаем к нижнему краю (увеличено в 1.3 раза)
		_abilitiesContainer.OffsetLeft = -42;
		_abilitiesContainer.OffsetRight = 42;
		_abilitiesContainer.OffsetTop = -36;
		_abilitiesContainer.OffsetBottom = -6;

		var hbox = new HBoxContainer();
		hbox.LayoutMode = 1;
		hbox.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		hbox.Alignment = BoxContainer.AlignmentMode.Center;
		hbox.AddThemeConstantOverride("separation", 13);
		_abilitiesContainer.AddChild(hbox);

		_ability1Slot = new AbilitySlotUI(1, this);
		_ability2Slot = new AbilitySlotUI(2, this);

		hbox.AddChild(_ability1Slot);
		hbox.AddChild(_ability2Slot);

		_hudRoot.AddChild(_abilitiesContainer);
	}

	private void CreateFloorAndEnemiesUI(Font font)
	{
		_floorEnemiesLabel = new Label();
		_floorEnemiesLabel.Name = "FloorEnemiesLabel";
		_floorEnemiesLabel.LayoutMode = 1;
		_floorEnemiesLabel.SetAnchorsPreset(Control.LayoutPreset.TopRight);
		_floorEnemiesLabel.OffsetLeft = -180;
		_floorEnemiesLabel.OffsetRight = -8;
		_floorEnemiesLabel.OffsetTop = 2;
		_floorEnemiesLabel.OffsetBottom = 22;
		_floorEnemiesLabel.HorizontalAlignment = HorizontalAlignment.Right;
		_floorEnemiesLabel.VerticalAlignment = VerticalAlignment.Center;
		if (font != null) _floorEnemiesLabel.AddThemeFontOverride("font", font);
		_floorEnemiesLabel.AddThemeFontSizeOverride("font_size", 14);
		_floorEnemiesLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.88f, 0.7f));
		_hudRoot.AddChild(_floorEnemiesLabel);
	}

	private void CreateWeaponsUI(Font font)
	{
		_weaponsContainer = new Control();
		_weaponsContainer.Name = "WeaponsContainer";
		_weaponsContainer.LayoutMode = 1;
		_weaponsContainer.SetAnchorsPreset(Control.LayoutPreset.BottomRight);
		_weaponsContainer.OffsetLeft = -140;
		_weaponsContainer.OffsetRight = -6;
		_weaponsContainer.OffsetTop = -56;
		_weaponsContainer.OffsetBottom = -6;
		_hudRoot.AddChild(_weaponsContainer);

		_ammoLabel = new Label();
		_ammoLabel.Name = "AmmoLabel";
		_ammoLabel.LayoutMode = 1;
		_ammoLabel.SetAnchorsPreset(Control.LayoutPreset.TopRight);
		_ammoLabel.OffsetLeft = -134;
		_ammoLabel.OffsetRight = -2;
		_ammoLabel.OffsetTop = 0;
		_ammoLabel.OffsetBottom = 14;
		_ammoLabel.HorizontalAlignment = HorizontalAlignment.Right;
		if (font != null) _ammoLabel.AddThemeFontOverride("font", font);
		_ammoLabel.AddThemeFontSizeOverride("font_size", 11);
		_ammoLabel.AddThemeColorOverride("font_color", new Color(1.0f, 0.9f, 0.45f));
		_weaponsContainer.AddChild(_ammoLabel);

		var hbox = new HBoxContainer();
		hbox.LayoutMode = 1;
		hbox.SetAnchorsPreset(Control.LayoutPreset.BottomRight);
		hbox.OffsetLeft = -134;
		hbox.OffsetRight = 0;
		hbox.OffsetTop = -36;
		hbox.OffsetBottom = 0;
		hbox.Alignment = BoxContainer.AlignmentMode.End;
		hbox.AddThemeConstantOverride("separation", 6);
		_weaponsContainer.AddChild(hbox);

		for (int i = 0; i < 3; i++)
		{
			var slot = new WeaponShowcaseSlot(i + 1, font);
			hbox.AddChild(slot);
			_weaponSlots[i] = slot;
		}

		// Панель подсказки характеристик оружия при наведении курсора (в инвентаре или на земле)
		// Добавляем напрямую в CanvasLayer (в экранных координатах 1:1), чтобы рамка была тонкой в 1px, а карточка выходила прямо из курсора
		_weaponTooltipPanel = new Panel();
		_weaponTooltipPanel.Name = "WeaponTooltipPanel";
		_weaponTooltipPanel.Size = new Vector2(168, 74);
		_weaponTooltipPanel.MouseFilter = Control.MouseFilterEnum.Ignore;
		_weaponTooltipPanel.Visible = false;

		var tipStyle = new StyleBoxFlat
		{
			BgColor = new Color(0.06f, 0.06f, 0.09f, 0.88f),
			BorderColor = new Color(0.58f, 0.54f, 0.44f, 0.40f),
			BorderWidthLeft = 1,
			BorderWidthTop = 1,
			BorderWidthRight = 1,
			BorderWidthBottom = 1,
			CornerRadiusTopLeft = 2,
			CornerRadiusTopRight = 2,
			CornerRadiusBottomLeft = 2,
			CornerRadiusBottomRight = 2
		};
		_weaponTooltipPanel.AddThemeStyleboxOverride("panel", tipStyle);
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
		_weaponTooltipLabel.AddThemeFontSizeOverride("font_size", 20);
		_weaponTooltipLabel.AddThemeConstantOverride("line_spacing", -4);
		_weaponTooltipLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.93f, 0.86f));
		_weaponTooltipPanel.AddChild(_weaponTooltipLabel);
	}

	private WeaponData _lastHoveredWeapon = null;
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
				_ammoLabel.Text = $"{activeWeaponName.ToUpper()} — RELOAD {(int)(activeCtrl.ReloadProgress * 100)}%";
				_ammoLabel.AddThemeColorOverride("font_color", new Color(1.0f, 0.55f, 0.2f));
			}
			else
			{
				_ammoLabel.Text = $"{activeWeaponName.ToUpper()} [{activeCtrl.CurrentAmmo}/{activeCtrl.MaxAmmo}]";
				_ammoLabel.AddThemeColorOverride("font_color", activeCtrl.CurrentAmmo > 0 ? new Color(1.0f, 0.9f, 0.45f) : new Color(1.0f, 0.3f, 0.3f));
			}
		}
		else if (!string.IsNullOrEmpty(activeWeaponName))
		{
			_ammoLabel.Text = activeWeaponName.ToUpper();
			_ammoLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.86f, 0.62f));
		}
		else
		{
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
			_weaponSlots[i].UpdateSlot(w, isSelected);

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

		if (IsInstanceValid(_weaponTooltipPanel) && IsInstanceValid(_weaponTooltipLabel))
		{
			if (hoveredWeapon != null)
			{
				if (_lastHoveredWeapon != hoveredWeapon || !_weaponTooltipPanel.Visible)
				{
					_tooltipScaleAnim = 0.82f;
					_lastHoveredWeapon = hoveredWeapon;
				}
				_tooltipScaleAnim = Mathf.MoveToward(_tooltipScaleAnim, 1.0f, (float)GetProcessDeltaTime() * 4.5f);

				_weaponTooltipLabel.Text = WeaponShowcaseSlot.FormatWeaponStatsTooltip(
					hoveredWeapon,
					_player.WeaponDamageMultiplier,
					isGroundHover);

				Vector2 cardSize = new Vector2(168, hoveredWeapon.ManaCost > 0.01f ? 88 : 72);
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
			else
			{
				_lastHoveredWeapon = null;
				_weaponTooltipPanel.Visible = false;
			}
		}
	}

	private void UpdateFloorAndEnemiesUI()
	{
		if (!IsInstanceValid(_floorEnemiesLabel)) return;
		var level = GetTree().GetFirstNodeInGroup("Level") as Level;
		int floor = level != null ? level.CurrentLevelIndex : 1;
		int enemies = _waveManager != null ? _waveManager.EnemiesRemaining : 0;
		bool active = _waveManager != null && _waveManager.WaveInProgress;

		if (_isCountingDown)
		{
			_floorEnemiesLabel.Text = $"FLOOR {floor}  |  PREPARE";
			_floorEnemiesLabel.AddThemeColorOverride("font_color", new Color(1.0f, 0.85f, 0.45f));
		}
		else if (active || enemies > 0)
		{
			_floorEnemiesLabel.Text = $"FLOOR {floor}  |  IN BATTLE";
			_floorEnemiesLabel.AddThemeColorOverride("font_color", new Color(1.0f, 0.55f, 0.45f));
		}
		else
		{
			_floorEnemiesLabel.Text = $"FLOOR {floor}  |  CLEARED";
			_floorEnemiesLabel.AddThemeColorOverride("font_color", new Color(0.45f, 1.0f, 0.55f));
		}
	}

	private void CreateBossBarUI(Font font)
	{
		_bossBarContainer = new Control();
		_bossBarContainer.Name = "BossBarContainer";
		_bossBarContainer.LayoutMode = 1;
		_bossBarContainer.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
		_bossBarContainer.OffsetLeft = -85;
		_bossBarContainer.OffsetRight = 85;
		_bossBarContainer.OffsetTop = 18;
		_bossBarContainer.OffsetBottom = 38;
		_bossBarContainer.Visible = false;
		_hudRoot.AddChild(_bossBarContainer);

		_bossNameLabel = new Label();
		_bossNameLabel.LayoutMode = 1;
		_bossNameLabel.SetAnchorsPreset(Control.LayoutPreset.TopWide);
		_bossNameLabel.OffsetTop = 0;
		_bossNameLabel.OffsetBottom = 10;
		_bossNameLabel.HorizontalAlignment = HorizontalAlignment.Center;
		if (font != null) _bossNameLabel.AddThemeFontOverride("font", font);
		_bossNameLabel.AddThemeFontSizeOverride("font_size", 10);
		_bossNameLabel.AddThemeColorOverride("font_color", new Color(1.0f, 0.35f, 0.35f));
		_bossBarContainer.AddChild(_bossNameLabel);

		_bossHealthBar = new ProgressBar();
		_bossHealthBar.LayoutMode = 1;
		_bossHealthBar.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
		_bossHealthBar.OffsetTop = -8;
		_bossHealthBar.OffsetBottom = -1;
		_bossHealthBar.ShowPercentage = false;

		var bgStyle = new StyleBoxFlat
		{
			BgColor = new Color(0.12f, 0.04f, 0.04f, 0.9f),
			BorderColor = new Color(0.85f, 0.65f, 0.2f),
			BorderWidthLeft = 1,
			BorderWidthTop = 1,
			BorderWidthRight = 1,
			BorderWidthBottom = 1
		};
		var fillStyle = new StyleBoxFlat
		{
			BgColor = new Color(0.85f, 0.15f, 0.18f),
			BorderWidthLeft = 1,
			BorderWidthTop = 1,
			BorderWidthBottom = 1,
			BorderColor = new Color(1.0f, 0.45f, 0.45f)
		};
		_bossHealthBar.AddThemeStyleboxOverride("background", bgStyle);
		_bossHealthBar.AddThemeStyleboxOverride("fill", fillStyle);
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
	}
}

public partial class ArrowIndicator : Control
{
	public float Angle = 0.0f;
	public Color Color = new Color(0.2f, 1.0f, 0.2f, 0.9f); // Ярко-зеленый

	public override void _Draw()
	{
		// Стрелка, направленная вправо по умолчанию
		Vector2[] points = new Vector2[] {
			new Vector2(10, 0).Rotated(Angle),
			new Vector2(-6, -7).Rotated(Angle),
			new Vector2(-3, 0).Rotated(Angle),
			new Vector2(-6, 7).Rotated(Angle)
		};
		DrawPolygon(points, new Color[] { Color, Color, Color, Color });
		
		// Обводка для контраста
		Vector2[] outline = new Vector2[] {
			new Vector2(10, 0).Rotated(Angle),
			new Vector2(-6, -7).Rotated(Angle),
			new Vector2(-3, 0).Rotated(Angle),
			new Vector2(-6, 7).Rotated(Angle),
			new Vector2(10, 0).Rotated(Angle)
		};
		DrawPolyline(outline, new Color(0, 0, 0, 1), 1.5f, true);
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

	public WeaponShowcaseSlot(int slotNumber, Font font)
	{
		_slotNumber = slotNumber;
		CustomMinimumSize = new Vector2(36, 36);
		Size = new Vector2(36, 36);
		TextureFilter = TextureFilterEnum.Nearest;

		_keyBadgeLabel = new Label();
		_keyBadgeLabel.Text = slotNumber.ToString();
		_keyBadgeLabel.Position = new Vector2(3, 1);
		_keyBadgeLabel.Size = new Vector2(12, 12);
		if (font != null) _keyBadgeLabel.AddThemeFontOverride("font", font);
		_keyBadgeLabel.AddThemeFontSizeOverride("font_size", 10);
		_keyBadgeLabel.AddThemeColorOverride("font_color", new Color(0.75f, 0.75f, 0.8f));
		AddChild(_keyBadgeLabel);
	}

	public void UpdateSlot(WeaponData weapon, bool isSelected)
	{
		bool changed = (_currentWeapon != weapon) || (_isSelected != isSelected);
		_currentWeapon = weapon;
		_isSelected = isSelected;

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

	public static string FormatWeaponStatsTooltip(WeaponData weapon, float damageMultiplier = 1.0f, bool isGroundPickup = false)
	{
		if (weapon == null) return "";

		string displayName = weapon.WeaponName;
		if (string.IsNullOrEmpty(displayName) || displayName == "Новое оружие")
		{
			string path = !string.IsNullOrEmpty(weapon.ResourcePath) ? weapon.ResourcePath : weapon.OriginalResourcePath;
			displayName = !string.IsNullOrEmpty(path) ? System.IO.Path.GetFileNameWithoutExtension(path) : "WEAPON";
		}

		float effectiveDmg = weapon.Damage * damageMultiplier;
		string ammoText = (weapon.IsRanged && weapon.MaxAmmo < 500)
			? weapon.MaxAmmo.ToString()
			: (weapon.IsRanged ? "INF" : "-");

		string header = isGroundPickup ? $"{displayName.ToUpper()} [E]" : displayName.ToUpper();
		string manaLine = weapon.ManaCost > 0.01f ? $"\nMANA: {weapon.ManaCost:0.#}  RLD: {weapon.ReloadTime:0.##}s" : "";
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
		instance.QueueFree();

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
		float bob = _isSelected ? Mathf.Sin(Time.GetTicksMsec() * 0.006f) * 1.4f : 0.0f;

		// 1. Внешняя тень и декоративная подложка пьедестала
		DrawRect(new Rect2(1, 2, sz.X, sz.Y), new Color(0f, 0f, 0f, 0.55f));

		Color bgOuter = _isSelected
			? new Color(0.24f, 0.18f, 0.09f, 0.95f)
			: new Color(0.10f, 0.11f, 0.14f, 0.85f);
		Color bgInner = _isSelected
			? new Color(0.34f, 0.25f, 0.12f, 0.95f)
			: new Color(0.14f, 0.15f, 0.19f, 0.85f);
		Color borderOuter = _isSelected
			? new Color(1.0f, 0.82f, 0.25f, 1.0f)
			: new Color(0.36f, 0.38f, 0.44f, 0.9f);
		Color borderInner = _isSelected
			? new Color(1.0f, 0.92f, 0.55f, 0.45f)
			: new Color(0.22f, 0.24f, 0.28f, 0.5f);

		DrawRect(new Rect2(0, 0, sz.X, sz.Y), bgOuter);
		DrawRect(new Rect2(3, 3, sz.X - 6, sz.Y - 6), bgInner);

		// Внутренний ромб/ореол за оружием
		Vector2 center = sz * 0.5f + new Vector2(0, 1);
		if (_isSelected)
		{
			DrawCircle(center, 12.5f, new Color(1.0f, 0.80f, 0.22f, 0.18f));
			DrawCircle(center, 8.5f, new Color(1.0f, 0.90f, 0.45f, 0.16f));
		}
		else
		{
			DrawCircle(center, 10.0f, new Color(0.05f, 0.05f, 0.07f, 0.45f));
		}

		// Двойная рамка с декоративными заклепками по углам
		DrawRect(new Rect2(0.5f, 0.5f, sz.X - 1, sz.Y - 1), borderOuter, false, 1.2f);
		DrawRect(new Rect2(2.5f, 2.5f, sz.X - 5, sz.Y - 5), borderInner, false, 1.0f);

		Color cornerColor = _isSelected ? new Color(1.0f, 0.92f, 0.55f) : new Color(0.50f, 0.52f, 0.58f);
		DrawRect(new Rect2(1, 1, 2, 2), cornerColor);
		DrawRect(new Rect2(sz.X - 3, 1, 2, 2), cornerColor);
		DrawRect(new Rect2(1, sz.Y - 3, 2, 2), cornerColor);
		DrawRect(new Rect2(sz.X - 3, sz.Y - 3, 2, 2), cornerColor);

		// 2. Отрисовка самого оружия (или пустого силуэта)
		if (_weaponTexture != null)
		{
			Vector2 texSize = _weaponTexture.GetSize();
			float maxDim = Mathf.Max(1.0f, Mathf.Max(texSize.X, texSize.Y));
			float targetSpan = _isSelected ? 25.0f : 22.0f;
			float scale = targetSpan / maxDim;

			// Вертикальные клинки/посохи наклоняем по диагонали (+40°), а огнестрел — слегка вверх (-18°)
			float angle = (texSize.Y > texSize.X * 1.15f) ? 0.68f : -0.30f;
			Vector2 drawCenter = center + new Vector2(1.0f, bob);

			// Тень под оружием
			DrawSetTransform(drawCenter + new Vector2(1.5f, 1.8f), angle, new Vector2(scale, scale));
			DrawTexture(_weaponTexture, -texSize * 0.5f, new Color(0f, 0f, 0f, 0.65f));

			// Само оружие
			Color tint = _isSelected ? new Color(1.08f, 1.08f, 1.08f, 1.0f) : new Color(0.82f, 0.84f, 0.88f, 0.92f);
			DrawSetTransform(drawCenter, angle, new Vector2(scale, scale));
			DrawTexture(_weaponTexture, -texSize * 0.5f, tint);

			// Сброс трансформации CanvasItem
			DrawSetTransform(Vector2.Zero, 0.0f, Vector2.One);
		}
		else
		{
			// Пустой слот — аккуратная диагональная эмблема пустого крепления
			DrawLine(center + new Vector2(-5, 5), center + new Vector2(5, -5), new Color(0.32f, 0.34f, 0.40f, 0.45f), 1.5f);
			DrawCircle(center, 2.0f, new Color(0.32f, 0.34f, 0.40f, 0.45f));
		}
	}
}
