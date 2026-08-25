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
	private float _lastMaxHealth = 0;

	private Player _player;
	private WaveManager _waveManager;
	private ArrowIndicator _arrowIndicator;

	private Control _abilitiesContainer;
	private AbilitySlotUI _ability1Slot;
	private AbilitySlotUI _ability2Slot;
	private Control _hudRoot;

	public override void _Ready()
	{
		AddToGroup("HUD");
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

		// Создаем стрелку для лестницы
		_arrowIndicator = new ArrowIndicator();
		_arrowIndicator.Visible = false;
		_hudRoot.AddChild(_arrowIndicator);

		CreateAbilitiesUI();
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
				_healthBar.MaxValue = _player.Config.MaxHealth;
				_healthBar.Value = _player.CurrentHealth;
				_hpLabel.Text = $"{(int)_player.CurrentHealth}/{(int)_player.Config.MaxHealth}";
			}

			if (_player.Config != null && IsInstanceValid(_manaBar) && IsInstanceValid(_mpLabel)) {
				_manaBar.MaxValue = _player.Config.MaxMana;
				_manaBar.Value = _player.CurrentMana;
				_mpLabel.Text = $"{(int)_player.CurrentMana}/{(int)_player.Config.MaxMana}";
			}
		}

		// Логика таймера
		ProcessTimer((float)delta);

		// Стрелка к лестнице
		UpdateExitLadderArrow();

		// Обновление состояния кд и плюсиков способностей
		if (IsInstanceValid(_ability1Slot)) _ability1Slot.UpdateState();
		if (IsInstanceValid(_ability2Slot)) _ability2Slot.UpdateState();
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
		if (_arrowIndicator == null) return;

		var level = GetTree().GetFirstNodeInGroup("Level") as Level;
		var exitLadder = level?.GetExitLadderNode();
		
		if (exitLadder == null || !GodotObject.IsInstanceValid(exitLadder)) {
			_arrowIndicator.Visible = false;
			return;
		}

		_arrowIndicator.Visible = true;

		// 1. Пульсация размера (на 10% меньше)
		float pulseScale = (1.0f + (float)Math.Sin(Time.GetTicksMsec() * 0.01f) * 0.15f) * 0.9f;
		_arrowIndicator.Scale = new Vector2(pulseScale, pulseScale);

		Vector2 screenPos = exitLadder.GetGlobalTransformWithCanvas().Origin;
		Vector2 viewportSize = GetViewport().GetVisibleRect().Size;
		Vector2 center = viewportSize / 2.0f;

		float margin = 25.0f;
		float minX = margin;
		float maxX = viewportSize.X - margin;
		float minY = margin;
		float maxY = viewportSize.Y - margin;

		Vector2 arrowPos = screenPos;
		float angle = 0.0f;

		if (screenPos.X < minX || screenPos.X > maxX || screenPos.Y < minY || screenPos.Y > maxY) {
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
			
			// Качание вперед-назад по направлению к лестнице
			float bounce = (float)Math.Sin(Time.GetTicksMsec() * 0.015f) * 5.0f;
			arrowPos += new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * bounce;
		} else {
			// На экране: парит над лестницей и указывает вниз
			arrowPos = screenPos + new Vector2(0, -22);
			angle = (float)(Math.PI / 2.0f); // 90 градусов (вниз)
			
			// Плавное покачивание по высоте
			float wave = (float)Math.Sin(Time.GetTicksMsec() * 0.007f) * 4.0f;
			arrowPos.Y += wave;
		}

		_arrowIndicator.Position = arrowPos / World.UiScale;
		if (Math.Abs(_arrowIndicator.Angle - angle) > 0.001f) {
			_arrowIndicator.Angle = angle;
			_arrowIndicator.QueueRedraw();
		}
	}

	private void CreateAbilitiesUI()
	{
		_abilitiesContainer = new Control();
		_abilitiesContainer.Name = "AbilitiesContainer";
		_abilitiesContainer.LayoutMode = 1;
		_abilitiesContainer.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
		
		// Смещаем к центру и прижимаем к нижнему краю
		_abilitiesContainer.OffsetLeft = -30;
		_abilitiesContainer.OffsetRight = 30;
		_abilitiesContainer.OffsetTop = -30;
		_abilitiesContainer.OffsetBottom = -6;

		var hbox = new HBoxContainer();
		hbox.LayoutMode = 1;
		hbox.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		hbox.Alignment = BoxContainer.AlignmentMode.Center;
		hbox.AddThemeConstantOverride("separation", 10);
		_abilitiesContainer.AddChild(hbox);

		_ability1Slot = new AbilitySlotUI(1, this);
		_ability2Slot = new AbilitySlotUI(2, this);

		hbox.AddChild(_ability1Slot);
		hbox.AddChild(_ability2Slot);

		_hudRoot.AddChild(_abilitiesContainer);
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
