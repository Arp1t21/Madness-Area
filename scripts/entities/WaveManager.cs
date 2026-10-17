using Godot;
using System;

public partial class WaveManager : Node2D
{
	[Export] public PackedScene EnemyPrefab;
	[Export] public int InitialEnemyCount = 4;
	[Export] public float SpawnRadius = 400.0f;

	private int _currentWave = 0;
	private int _enemiesAlive = 0;
	private int _enemiesToSpawn = 0;
	private int _rangedSkippedThisWave = 0;
	private int _rangedSpawnedThisWave = 0;
	private bool _isSpawning = false;
	private bool _waveInProgress = false;

	public bool WaveInProgress => _waveInProgress;
	public int CurrentWave => _currentWave;
	public int EnemiesRemaining => Mathf.Max(0, _enemiesAlive + _enemiesToSpawn);

	public override void _Ready()
	{
		var level = GetTree().GetFirstNodeInGroup("Level") as Level;
		if (level != null && level.CurrentLevelIndex > 1)
		{
			_currentWave = level.CurrentLevelIndex - 1;
		}
	}

	public async void StartNextWave()
	{
		if (_waveInProgress || Engine.TimeScale == 0) return;
		_waveInProgress = true;
		_rangedSkippedThisWave = 0;
		_rangedSpawnedThisWave = 0;

		var level = GetTree().GetFirstNodeInGroup("Level") as Level;
		if (level != null)
		{
			_currentWave = level.CurrentLevelIndex;
		}
		else
		{
			_currentWave++;
		}

		MusicManager.Instance?.PlayMusic("res://assets/sounds/doom_22. Damnation.mp3");

		bool isBossWave = (_currentWave % 5 == 0);
		int w = Mathf.Max(0, _currentWave - 1);
		int enemyCount = InitialEnemyCount * 2 + w * 12 + w * w * 2;
		if (isBossWave)
		{
			enemyCount += 1; // +1 большой Босс поверх волны
		}

		_enemiesToSpawn = enemyCount;
		_isSpawning = true;
		float spawnDelay = Mathf.Clamp(0.45f - w * 0.04f, 0.16f, 0.45f);
		GD.Print($"Wave {_currentWave} started! Enemies: {enemyCount} (BossWave: {isBossWave})");

		for (int i = 0; i < enemyCount; i++)
		{
			await ToSignal(GetTree().CreateTimer(spawnDelay, processAlways: false), "timeout");
			if (!IsInsideTree()) return;

			bool spawnAsBoss = isBossWave && (i == enemyCount - 1);
			_enemiesToSpawn = Mathf.Max(0, _enemiesToSpawn - 1);
			SpawnEnemy(spawnAsBoss);
		}

		_isSpawning = false;
		CheckWaveCleared(Vector2.Zero);
	}

	private void SpawnEnemy(bool isBoss = false)
	{
		PackedScene selectedPrefab = EnemyPrefab;
		if (selectedPrefab == null)
		{
			selectedPrefab = GD.Load<PackedScene>("res://scene/actors/orc_warrior.tscn");
		}

		var level = GetTree().GetFirstNodeInGroup("Level") as Level;
		int levelIndex = level != null ? level.CurrentLevelIndex : 1;

		if (isBoss)
		{
			selectedPrefab = (levelIndex % 10 == 0)
				? GD.Load<PackedScene>("res://scene/actors/quagmire.tscn")
				: GD.Load<PackedScene>("res://scene/actors/orc_warrior.tscn");
		}
		else
		{
			float ghostChance = levelIndex >= 2 ? 0.12f : 0.0f;
			float archerChance = levelIndex >= 2 ? 0.14f : 0.0f;
			float quagmireChance = levelIndex >= 3 ? 0.15f : 0.0f;
			float roll = GD.Randf();

			if (roll < quagmireChance)
			{
				selectedPrefab = GD.Load<PackedScene>("res://scene/actors/quagmire.tscn");
			}
			else if (roll < quagmireChance + ghostChance + archerChance)
			{
				// На 2-м этаже делаем на 2 ренджовых моба меньше (и ограничиваем максимум 3 ренджовыми)
				if (levelIndex == 2 && (_rangedSkippedThisWave < 2 || _rangedSpawnedThisWave >= 3))
				{
					_rangedSkippedThisWave++;
				}
				else
				{
					_rangedSpawnedThisWave++;
					if (roll < quagmireChance + ghostChance)
					{
						selectedPrefab = GD.Load<PackedScene>("res://scene/actors/ghost.tscn");
					}
					else
					{
						selectedPrefab = GD.Load<PackedScene>("res://scene/actors/archer.tscn");
					}
				}
			}
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
			float angle = (float)GD.RandRange(0, Math.PI * 2);
			spawnPos = player.GlobalPosition + new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * 180.0f;
		}

		enemy.GlobalPosition = spawnPos;
		if (isBoss && enemy is Enemy bossEnemy)
		{
			bossEnemy.Ready += () => bossEnemy.PromoteToBoss(levelIndex);
		}

		GetParent().CallDeferred("add_child", enemy);

		_enemiesAlive++;
		enemy.TreeExiting += () => OnEnemyDied(enemy);
	}

	private void OnEnemyDied(Node2D enemy)
	{
		if (enemy is Enemy e && e.CurrentHealth > 0) return;

		_enemiesAlive = Mathf.Max(0, _enemiesAlive - 1);
		Vector2 lastEnemyPos = IsInstanceValid(enemy) ? enemy.GlobalPosition : Vector2.Zero;

		CheckWaveCleared(lastEnemyPos);
	}

	private void CheckWaveCleared(Vector2 lastEnemyPos)
	{
		if (!_waveInProgress || _isSpawning || _enemiesToSpawn > 0 || _enemiesAlive > 0) return;

		_waveInProgress = false;
		GD.Print($"Wave {_currentWave} cleared!");

		ShowYouWon();

		var level = GetTree().GetFirstNodeInGroup("Level") as Level;
		if (level != null)
		{
			level.OnWaveCleared();

			string rewardWeapon = GetWaveWeaponDrop(level.CurrentLevelIndex);
			if (!string.IsNullOrEmpty(rewardWeapon))
			{
				Vector2 dropPos = lastEnemyPos != Vector2.Zero ? lastEnemyPos : Vector2.Zero;
				level.SpawnWeaponPickupAt(dropPos, rewardWeapon);
			}
		}

		var player = GetTree().GetFirstNodeInGroup("Player") as Player;
		if (player != null)
		{
			player.OnWaveCleared();
		}
	}

	private string GetWaveWeaponDrop(int levelIndex)
	{
		switch (levelIndex)
		{
			case 1: return "res://data/waepons/revolver.tres";
			case 2: return "res://data/waepons/shotgun.tres";
			case 3: return "res://data/waepons/katana.tres";
			case 4: return "res://data/waepons/auto_rifle.tres";
			case 5: return "res://data/waepons/double_axe.tres";
			default:
				if (levelIndex % 2 == 1)
				{
					string[] pool = new[]
					{
						"res://data/waepons/katana.tres",
						"res://data/waepons/double_axe.tres",
						"res://data/waepons/auto_rifle.tres",
						"res://data/waepons/shotgun.tres",
						"res://data/waepons/revolver.tres",
						"res://data/waepons/magic_staff.tres"
					};
					return pool[GD.Randi() % pool.Length];
				}
				return null;
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
		label.AddThemeFontSizeOverride("font_size", 13);
		label.AddThemeColorOverride("font_color", new Color(0, 1, 0));

		rootControl.AddChild(label);
		AddChild(canvas);

		StartBlinking(label, 5.0f);

		var timer = GetTree().CreateTimer(5.0f, processAlways: false);
		timer.Timeout += () =>
		{
			if (GodotObject.IsInstanceValid(canvas))
			{
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
			await ToSignal(GetTree().CreateTimer(0.6f, processAlways: false), "timeout");
			if (!IsInstanceValid(label)) break;

			label.Visible = false;
			await ToSignal(GetTree().CreateTimer(0.4f, processAlways: false), "timeout");
			elapsed += 1.0f;
		}
	}
}
