using Godot;
using System;

public partial class HUD : CanvasLayer
{
	// Монеты
	private Label _coinLabel;
	
	// Здоровье
	private ProgressBar _healthBar;
	private Label _hpLabel;
	
	// Таймер
	private Label _timeLabel;
	private Label _announcementLabel;
	private Control _markersContainer;
	private float _time = 0.0f;
	private bool _isCountingDown = true;
	private float _lastMaxHealth = 0;

	private Player _player;
	private WaveManager _waveManager;
	private ArrowIndicator _arrowIndicator;

	public override void _Ready()
	{
		_coinLabel = GetNodeOrNull<Label>("CoinControl/HBoxContainer/CoinLabel");
		if (_coinLabel != null) {
			var emptyStyle = new StyleBoxEmpty();
			emptyStyle.ContentMarginBottom = 2.0f;
			_coinLabel.AddThemeStyleboxOverride("normal", emptyStyle);
		}

		_healthBar = GetNodeOrNull<ProgressBar>("HealthUI/HealthBar");
		_hpLabel = GetNodeOrNull<Label>("HealthUI/HPLabel");
		_timeLabel = GetNodeOrNull<Label>("TimerControl/Label");
		_markersContainer = GetNodeOrNull<Control>("HealthUI/HealthBar/Markers");

		_player = GetTree().GetFirstNodeInGroup("Player") as Player;
		_waveManager = GetTree().GetFirstNodeInGroup("WaveManager") as WaveManager;

		// Подготавливаем объявление волны
		_announcementLabel = new Label();
		_announcementLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_announcementLabel.VerticalAlignment = VerticalAlignment.Center;
		_announcementLabel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center);
		
		// Сдвигаем на 2 пикселя выше
		_announcementLabel.OffsetTop -= 2;
		_announcementLabel.OffsetBottom -= 2;

		var font = GD.Load<Font>("res://assets/fonts/m5x7/m5x7.ttf");
		if (font != null) {
			_announcementLabel.AddThemeFontOverride("font", font);
		}

		_announcementLabel.AddThemeFontSizeOverride("font_size", 13);
		_announcementLabel.AddThemeColorOverride("font_color", new Color(1, 0, 0));
		_announcementLabel.Text = "First wave is coming!";
		AddChild(_announcementLabel);
		_announcementLabel.Visible = false;

		UpdateTimerLabel();

		// Сразу запускаем первую волну, если время уже 0
		if (_time <= 0 && Engine.TimeScale > 0) {
			CallDeferred(nameof(StartGame));
		}

		// Создаем стрелку для лестницы
		_arrowIndicator = new ArrowIndicator();
		_arrowIndicator.Visible = false;
		AddChild(_arrowIndicator);
	}

	public override void _Process(double delta)
	{
		if (Engine.TimeScale == 0) return;

		// Обновление статов игрока
		if (IsInstanceValid(_player))
		{
			if (IsInstanceValid(_coinLabel))
				_coinLabel.Text = _player.CoinCount.ToString();

			if (_player.Config != null && IsInstanceValid(_healthBar) && IsInstanceValid(_hpLabel)) {
				_healthBar.MaxValue = _player.Config.MaxHealth;
				_healthBar.Value = _player.CurrentHealth;
				_hpLabel.Text = $"{(int)_player.CurrentHealth}/{(int)_player.Config.MaxHealth}";
			}
		}

		// Логика таймера
		ProcessTimer((float)delta);

		// Стрелка к лестнице
		UpdateExitLadderArrow();
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

		_arrowIndicator.Position = arrowPos;
		if (Math.Abs(_arrowIndicator.Angle - angle) > 0.001f) {
			_arrowIndicator.Angle = angle;
			_arrowIndicator.QueueRedraw();
		}
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
