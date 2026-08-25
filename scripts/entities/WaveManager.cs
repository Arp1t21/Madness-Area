using Godot;
using System;

public partial class WaveManager : Node2D
{
	[Export] public PackedScene EnemyPrefab;
	[Export] public int InitialEnemyCount = 4;
	[Export] public float SpawnRadius = 400.0f;
	
	private int _currentWave = 0;
	private int _enemiesAlive = 0;
	private bool _waveInProgress = false;

	public bool WaveInProgress => _waveInProgress;
	public int CurrentWave => _currentWave;

	public override void _Ready()
	{
		// Теперь волну запускает AreaTimer когда доходит до 00:00
	}

	public async void StartNextWave()
	{
		if (_waveInProgress || Engine.TimeScale == 0) return;
		_waveInProgress = true;
		_currentWave++;
		
		MusicManager.Instance?.PlayMusic("res://assets/sounds/doom_22. Damnation.mp3");
		
		// В 2 раза больше мобов
		int enemyCount = (InitialEnemyCount + (_currentWave - 1) * 3) * 2;
		GD.Print($"Wave {_currentWave} started! Enemies: {enemyCount}");
		
		for (int i = 0; i < enemyCount; i++)
		{
			// processAlways: false — таймер (и спавн) замирают вместе с игрой при паузе
			await ToSignal(GetTree().CreateTimer(0.666f, processAlways: false), "timeout");
			if (!IsInsideTree()) return; // сцена закрылась во время спавна
			SpawnEnemy();
		}
	}

	private void SpawnEnemy()
	{
		PackedScene selectedPrefab = EnemyPrefab;
		if (selectedPrefab == null) {
			selectedPrefab = GD.Load<PackedScene>("res://scene/actors/orc_warrior.tscn");
		}

		var level = GetTree().GetFirstNodeInGroup("Level") as Level;
		int levelIndex = level != null ? level.CurrentLevelIndex : 1;

		float ghostChance = levelIndex >= 2 ? 0.10f : 0.0f;
		float archerChance = levelIndex >= 2 ? 0.10f : 0.0f;
		float quagmireChance = levelIndex >= 3 ? 0.15f : 0.0f;
		float roll = GD.Randf();

		if (roll < quagmireChance) {
			selectedPrefab = GD.Load<PackedScene>("res://scene/actors/quagmire.tscn");
		} else if (roll < quagmireChance + ghostChance) {
			selectedPrefab = GD.Load<PackedScene>("res://scene/actors/ghost.tscn");
		} else if (roll < quagmireChance + ghostChance + archerChance) {
			selectedPrefab = GD.Load<PackedScene>("res://scene/actors/archer.tscn");
		}

		var enemy = selectedPrefab.Instantiate() as Node2D;
		var player = GetTree().GetFirstNodeInGroup("Player") as Node2D;
		
		Vector2 spawnPos = Vector2.Zero;
		if (level != null && player != null)
		{
			spawnPos = level.GetNextEnemySpawnPosition(player.GlobalPosition);
		}
		else if (player != null)
		{
			// Резервный спавн
			float angle = (float)GD.RandRange(0, Math.PI * 2);
			spawnPos = player.GlobalPosition + new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * 180.0f;
		}

		enemy.GlobalPosition = spawnPos;
		GetParent().CallDeferred("add_child", enemy);
		
		_enemiesAlive++;
		enemy.TreeExiting += () => OnEnemyDied(enemy);
	}

	private void OnEnemyDied(Node2D enemy)
	{
		// TreeExiting срабатывает и при смене сцены/выгрузке дерева.
		// Убийством считаем только выход мёртвого врага.
		if (enemy is Enemy e && e.CurrentHealth > 0) return;

		_enemiesAlive--;
		Vector2 lastEnemyPos = IsInstanceValid(enemy) ? enemy.GlobalPosition : Vector2.Zero;
		
		if (_enemiesAlive <= 0)
		{
			_waveInProgress = false;
			GD.Print($"Wave {_currentWave} cleared!");
			
			ShowYouWon();

			var level = GetTree().GetFirstNodeInGroup("Level") as Level;
			if (level != null) {
				level.OnWaveCleared();

				// Оружие падает только со второго уровня — на первом игрок
				// начинает с обычным стартовым набором
				if (level.CurrentLevelIndex > 1)
				{
					if (_currentWave == 1)
					{
						level.SpawnWeaponPickupAt(lastEnemyPos, "res://data/waepons/shotgun.tres");
					}
					else if (_currentWave == 2)
					{
						level.SpawnWeaponPickupAt(lastEnemyPos, "res://data/waepons/revolver.tres");
					}
				}
			}

			var player = GetTree().GetFirstNodeInGroup("Player") as Player;
			if (player != null) {
				player.OnWaveCleared();
			}
		}
	}

	private void ShowYouWon()
	{
		CanvasLayer canvas = new CanvasLayer();
		var rootControl = new Control();
		var viewportSize = GetViewport().GetVisibleRect().Size;
		rootControl.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
		rootControl.Size = viewportSize / World.UiScale;
		rootControl.Scale = new Vector2(World.UiScale, World.UiScale);
		canvas.AddChild(rootControl);

		Label label = new Label();
		label.Text = "You killed wave!";
		
		label.HorizontalAlignment = HorizontalAlignment.Center;
		label.VerticalAlignment = VerticalAlignment.Center;
		label.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center);
		label.AddThemeFontSizeOverride("font_size", 13); // 17 / 1.3 = 13
		label.AddThemeColorOverride("font_color", new Color(0, 1, 0));
		
		rootControl.AddChild(label);
		AddChild(canvas);

		// Логика мигания: 0.6с виден, 0.4с скрыт
		StartBlinking(label, 5.0f);

		// Удаляем надпись через 5 секунд
		var timer = GetTree().CreateTimer(5.0f);
		timer.Timeout += () => {
			if (GodotObject.IsInstanceValid(canvas)) {
				canvas.QueueFree();
			}
		};
	}

	private async void StartBlinking(Label label, float duration)
	{
		float elapsed = 0;
		while (elapsed < duration && IsInstanceValid(label))
		{
			label.Visible = true;
			await ToSignal(GetTree().CreateTimer(0.6f), "timeout");
			if (!IsInstanceValid(label)) break;
			
			label.Visible = false;
			await ToSignal(GetTree().CreateTimer(0.4f), "timeout");
			elapsed += 1.0f;
		}
	}
}
