using Godot;
using System;

public partial class WaveManager : Node2D
{
	[Export] public PackedScene EnemyPrefab;
	[Export] public int InitialEnemyCount = 6;
	[Export] public float SpawnRadius = 400.0f;
	
	private int _currentWave = 0;
	private int _enemiesAlive = 0;
	private bool _waveInProgress = false;

	public override void _Ready()
	{
		// Теперь волну запускает AreaTimer когда доходит до 00:00
	}

	public async void StartNextWave()
	{
		if (_waveInProgress || Engine.TimeScale == 0) return;
		_waveInProgress = true;
		_currentWave++;
		
		// В 2 раза больше мобов
		int enemyCount = (InitialEnemyCount + (_currentWave - 1) * 3) * 2;
		GD.Print($"Wave {_currentWave} started! Enemies: {enemyCount}");
		
		for (int i = 0; i < enemyCount; i++)
		{
			SpawnEnemy();
			// Медленное появление: 3 моба за 2 секунды (пауза 0.666с)
			await ToSignal(GetTree().CreateTimer(0.666f), "timeout");
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

		if (levelIndex >= 2 && GD.Randf() <= 0.10f) {
			selectedPrefab = GD.Load<PackedScene>("res://scene/actors/ghost.tscn");
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
		enemy.TreeExiting += OnEnemyDied;
	}

	private void OnEnemyDied()
	{
		_enemiesAlive--;
		if (_enemiesAlive <= 0)
		{
			_waveInProgress = false;
			GD.Print($"Wave {_currentWave} cleared!");
			
			ShowYouWon();

			var level = GetTree().GetFirstNodeInGroup("Level") as Level;
			if (level != null) {
				level.OnWaveCleared();
			}
		}
	}

	private void ShowYouWon()
	{
		CanvasLayer canvas = new CanvasLayer();
		Label label = new Label();
		label.Text = "You killed wave!";
		
		label.HorizontalAlignment = HorizontalAlignment.Center;
		label.VerticalAlignment = VerticalAlignment.Center;
		label.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center);
		label.AddThemeFontSizeOverride("font_size", 13); // 17 / 1.3 = 13
		label.AddThemeColorOverride("font_color", new Color(0, 1, 0));
		
		canvas.AddChild(label);
		AddChild(canvas);

		// Логика мигания: 0.6с виден, 0.4с скрыт
		StartBlinking(label, 5.0f);

		// Удаляем надпись через 5 секунд
		var timer = GetTree().CreateTimer(5.0f);
		timer.Timeout += () => canvas.QueueFree();
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
