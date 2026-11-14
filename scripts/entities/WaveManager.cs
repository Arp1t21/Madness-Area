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

	private bool IsCoopSession => NetworkManager.Instance != null && NetworkManager.Instance.IsCoopSession;
	private bool IsServerAuthority => !IsCoopSession || (NetworkManager.Instance?.IsServerAuthority ?? true);

	public async void StartNextWave()
	{
		if (!IsServerAuthority) return;
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
		if (IsCoopSession)
		{
			Rpc(nameof(RpcOnWaveStarted), _currentWave);
		}

		bool isBossWave = (_currentWave % 5 == 0);
		var evt = level != null ? level.CurrentFloorEvent : GameLogic.FloorEventType.None;
		int w = Mathf.Max(0, _currentWave - 1);
		int enemyCount = level != null
			? level.GetExpectedMonsterCount()
			: GameLogic.CalculateWaveEnemyCount(_currentWave, Level.DifficultyMultiplier);
		int extraImps = evt == GameLogic.FloorEventType.ImpFrenzy ? 3 : 0;
		enemyCount += extraImps;

		_enemiesToSpawn = enemyCount;
		_isSpawning = true;
		float spawnDelay = Mathf.Clamp(0.45f + w * 0.06f, 0.45f, 1.05f);
		GD.Print($"Wave {_currentWave} started! Enemies: {enemyCount} (BossWave: {isBossWave}, Event: {evt}, SpawnDelay: {spawnDelay:0.00}s)");

		int floorTier = GameLogic.CalculateEnemyFloorTier(_currentWave);
		int maxElites = (_currentWave >= 2) ? Mathf.Min(6, 2 + floorTier) : 0;
		float eliteChance = Mathf.Min(0.45f, 0.22f + floorTier * 0.04f);
		int elitesSpawned = 0;

		for (int i = 0; i < enemyCount; i++)
		{
			await ToSignal(GetTree().CreateTimer(spawnDelay, processAlways: false), "timeout");
			if (!IsInsideTree()) return;

			bool spawnAsBoss = isBossWave && (i == enemyCount - 1);
			bool forceImp = !spawnAsBoss && extraImps > 0 && i >= (enemyCount - extraImps - (isBossWave ? 1 : 0));
			bool forceAmbushRanged = !spawnAsBoss && !forceImp && evt == GameLogic.FloorEventType.MarksmanAmbush && (i == 0 || i == 2);
			bool makeElite = !spawnAsBoss && _currentWave >= 2 && elitesSpawned < maxElites &&
							 ((i == 0) || GD.Randf() < eliteChance);
			if (makeElite) elitesSpawned++;

			_enemiesToSpawn = Mathf.Max(0, _enemiesToSpawn - 1);
			SpawnEnemy(spawnAsBoss, forceImp, evt == GameLogic.FloorEventType.MarksmanAmbush || forceAmbushRanged, forceAmbushRanged, makeElite);
		}

		_isSpawning = false;
		CheckWaveCleared(Vector2.Zero);
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RpcOnWaveStarted(int waveNumber)
	{
		if (Multiplayer.GetRemoteSenderId() != 1) return;
		_currentWave = waveNumber;
		_waveInProgress = true;
		MusicManager.Instance?.PlayMusic("res://assets/sounds/doom_22. Damnation.mp3");
	}

	private void SpawnEnemy(bool isBoss = false, bool forceImp = false, bool bypassRangedSkip = false, bool forceRangedUnit = false, bool makeElite = false)
	{
		if (!IsServerAuthority) return;

		string scenePath = (EnemyPrefab != null && !string.IsNullOrEmpty(EnemyPrefab.ResourcePath))
			? EnemyPrefab.ResourcePath
			: "res://scene/actors/orc_warrior.tscn";

		var level = GetTree().GetFirstNodeInGroup("Level") as Level;
		int levelIndex = level != null ? level.CurrentLevelIndex : 1;

		if (isBoss)
		{
			if (levelIndex % 15 == 0)
				scenePath = "res://scene/actors/big_demon.tscn";
			else if (levelIndex % 10 == 0)
				scenePath = "res://scene/actors/quagmire.tscn";
			else if (levelIndex % 5 == 0 && levelIndex >= 5)
				scenePath = "res://scene/actors/big_demon.tscn";
			else
				scenePath = "res://scene/actors/orc_warrior.tscn";
		}
		else if (forceImp)
		{
			scenePath = "res://scene/actors/chort.tscn";
		}
		else if (forceRangedUnit)
		{
			scenePath = GD.Randf() < 0.55f
				? "res://scene/actors/archer.tscn"
				: "res://scene/actors/dark_cultist.tscn";
		}
		else
		{
			float chortChance = levelIndex >= 1 ? 0.18f : 0.0f;
			float ghostChance = levelIndex >= 2 ? 0.11f : 0.0f;
			float archerChance = levelIndex >= 2 ? 0.12f : 0.0f;
			float cultistChance = levelIndex >= 3 ? 0.10f : 0.0f;
			float quagmireChance = levelIndex >= 3 ? 0.12f : 0.0f;
			float bigDemonChance = levelIndex >= 4 ? 0.08f : 0.0f;
			float roll = GD.Randf();

			if (roll < bigDemonChance)
			{
				scenePath = "res://scene/actors/big_demon.tscn";
			}
			else if (roll < bigDemonChance + quagmireChance)
			{
				scenePath = "res://scene/actors/quagmire.tscn";
			}
			else if (roll < bigDemonChance + quagmireChance + chortChance)
			{
				scenePath = "res://scene/actors/chort.tscn";
			}
			else if (roll < bigDemonChance + quagmireChance + chortChance + ghostChance + archerChance + cultistChance)
			{
				// На каждом этаже ставим на 2 милишных моба больше вместо 2 ренджовых (если нет ambush ивента)
				if (!bypassRangedSkip && (_rangedSkippedThisWave < 2 || (levelIndex == 2 && _rangedSpawnedThisWave >= 3)))
				{
					_rangedSkippedThisWave++;
				}
				else
				{
					_rangedSpawnedThisWave++;
					float rangedStart = bigDemonChance + quagmireChance + chortChance;
					if (roll < rangedStart + ghostChance)
					{
						scenePath = "res://scene/actors/ghost.tscn";
					}
					else if (roll < rangedStart + ghostChance + archerChance)
					{
						scenePath = "res://scene/actors/archer.tscn";
					}
					else
					{
						scenePath = "res://scene/actors/dark_cultist.tscn";
					}
				}
			}
		}

		var alivePlayers = NetworkManager.GetAlivePlayers(GetTree());
		Node2D targetPlayer = alivePlayers.Count > 0
			? alivePlayers[(int)(GD.Randi() % (uint)alivePlayers.Count)]
			: (GetTree().GetFirstNodeInGroup("Player") as Node2D);

		Vector2 spawnPos = Vector2.Zero;
		if (level != null && targetPlayer != null)
		{
			spawnPos = level.GetNextEnemySpawnPosition(targetPlayer.GlobalPosition);
		}
		else if (targetPlayer != null)
		{
			float angle = (float)GD.RandRange(0, Math.PI * 2);
			spawnPos = targetPlayer.GlobalPosition + new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * 60.0f;
		}

		int eliteModInt = -1;
		if (!isBoss && makeElite)
		{
			Enemy.EliteModifier[] mods = new[]
			{
				Enemy.EliteModifier.Berserker,
				Enemy.EliteModifier.Frost,
				Enemy.EliteModifier.Inferno,
				Enemy.EliteModifier.Volatile
			};
			eliteModInt = (int)mods[GD.Randi() % mods.Length];
		}

		int netId = IsCoopSession && NetworkManager.Instance != null
			? NetworkManager.Instance.AllocateEntityNetId()
			: 0;

		SpawnEnemyByParams(netId, scenePath, spawnPos, isBoss, eliteModInt, levelIndex, isServerSpawn: true);
		if (IsCoopSession)
		{
			Rpc(nameof(RpcSpawnEnemy), netId, scenePath, spawnPos, isBoss, eliteModInt, levelIndex);
		}
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RpcSpawnEnemy(int netId, string scenePath, Vector2 spawnPos, bool isBoss, int eliteModInt, int levelIndex)
	{
		if (Multiplayer.GetRemoteSenderId() != 1) return;
		SpawnEnemyByParams(netId, scenePath, spawnPos, isBoss, eliteModInt, levelIndex, isServerSpawn: false);
	}

	private void SpawnEnemyByParams(int netId, string scenePath, Vector2 spawnPos, bool isBoss, int eliteModInt, int levelIndex, bool isServerSpawn)
	{
		PackedScene prefab = GD.Load<PackedScene>(scenePath) ?? EnemyPrefab ?? GD.Load<PackedScene>("res://scene/actors/orc_warrior.tscn");
		if (prefab == null) return;

		var enemy = prefab.Instantiate() as Node2D;
		if (enemy == null) return;

		if (netId > 0)
		{
			enemy.Name = $"Enemy_{netId}";
			if (enemy is Enemy netEnemy)
			{
				netEnemy.NetId = netId;
			}
		}

		enemy.GlobalPosition = spawnPos;
		if (isBoss && enemy is Enemy bossEnemy)
		{
			bossEnemy.Ready += () => bossEnemy.PromoteToBoss(levelIndex);
		}
		else if (eliteModInt > 0 && enemy is Enemy eliteEnemy)
		{
			var chosenMod = (Enemy.EliteModifier)eliteModInt;
			eliteEnemy.Ready += () => eliteEnemy.PromoteToElite(chosenMod, levelIndex);
		}

		GetParent().CallDeferred("add_child", enemy);

		if (isServerSpawn)
		{
			_enemiesAlive++;
			enemy.TreeExiting += () => OnEnemyDied(enemy);
		}
	}

	private void OnEnemyDied(Node2D enemy)
	{
		if (!IsServerAuthority) return;
		if (enemy is Enemy e && !e.HasDied && e.CurrentHealth > 0) return;

		_enemiesAlive = Mathf.Max(0, _enemiesAlive - 1);
		Vector2 lastEnemyPos = IsInstanceValid(enemy) ? enemy.GlobalPosition : Vector2.Zero;

		CheckWaveCleared(lastEnemyPos);
	}

	private void CheckWaveCleared(Vector2 lastEnemyPos)
	{
		if (!IsServerAuthority) return;
		if (!_waveInProgress || _isSpawning || _enemiesToSpawn > 0 || _enemiesAlive > 0) return;

		var level = GetTree().GetFirstNodeInGroup("Level") as Level;
		if (level != null)
		{
			string rewardWeapon = GetWaveWeaponDrop(level.CurrentLevelIndex);
			if (!string.IsNullOrEmpty(rewardWeapon))
			{
				Vector2 dropPos = lastEnemyPos != Vector2.Zero ? lastEnemyPos : Vector2.Zero;
				level.SpawnWeaponPickupAt(dropPos, rewardWeapon);
			}
		}

		if (IsCoopSession)
		{
			Rpc(nameof(RpcOnWaveCleared));
		}
		else
		{
			ApplyWaveClearedEffects();
		}
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RpcOnWaveCleared()
	{
		int sender = Multiplayer.GetRemoteSenderId();
		if (IsCoopSession && sender != 0 && sender != 1) return;
		ApplyWaveClearedEffects();
	}

	private void ApplyWaveClearedEffects()
	{
		_waveInProgress = false;
		GD.Print($"Wave {_currentWave} cleared!");

		ShowYouWon();

		var level = GetTree().GetFirstNodeInGroup("Level") as Level;
		level?.OnWaveCleared();

		foreach (Player player in NetworkManager.GetAllPlayers(GetTree()))
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
			case 4: return "res://data/waepons/laser_cannon.tres";
			case 5: return "res://data/waepons/auto_rifle.tres";
			case 6: return "res://data/waepons/double_axe.tres";
			case 7: return "res://data/waepons/hunter_bow.tres";
			case 8: return "res://data/waepons/greatsword.tres";
			case 9: return "res://data/waepons/knight_sword.tres";
			default:
				if (levelIndex % 2 == 1)
				{
					string[] pool = new[]
					{
						"res://data/waepons/katana.tres",
						"res://data/waepons/double_axe.tres",
						"res://data/waepons/greatsword.tres",
						"res://data/waepons/knight_sword.tres",
						"res://data/waepons/hunter_bow.tres",
						"res://data/waepons/auto_rifle.tres",
						"res://data/waepons/shotgun.tres",
						"res://data/waepons/revolver.tres",
						"res://data/waepons/magic_staff.tres",
						"res://data/waepons/laser_cannon.tres"
					};
					return pool[GD.Randi() % pool.Length];
				}
				return null;
		}
	}

	private void ShowYouWon()
	{
		var level = GetTree()?.GetFirstNodeInGroup("Level") as Level;
		int floorIdx = level != null ? level.CurrentLevelIndex : 1;
		bool isBossFloor = GameLogic.IsMilestoneRelicFloor(floorIdx);

		var canvas = new CanvasLayer { Layer = 8 };
		var rootControl = new Control
		{
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		rootControl.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		canvas.AddChild(rootControl);

		Vector2 viewSize = GetViewport().GetVisibleRect().Size;
		const float bannerW = 360.0f;
		const float bannerH = 54.0f;
		var banner = new PanelContainer
		{
			CustomMinimumSize = new Vector2(bannerW, bannerH),
			Size = new Vector2(bannerW, bannerH),
			Position = new Vector2((viewSize.X - bannerW) * 0.5f, 68.0f),
			PivotOffset = new Vector2(bannerW * 0.5f, bannerH * 0.5f),
			Scale = new Vector2(0.88f, 0.88f),
			Modulate = new Color(1f, 1f, 1f, 0f),
			MouseFilter = Control.MouseFilterEnum.Ignore
		};

		Color accent = isBossFloor ? DungeonVisuals.BrassGold : DungeonVisuals.Emerald;
		var style = DungeonVisuals.CreatePanelStyle(
			new Color(0.05f, 0.065f, 0.095f, 0.94f),
			accent,
			2, 5, 8,
			new Color(accent.R, accent.G, accent.B, 0.30f));
		style.ContentMarginLeft = 16;
		style.ContentMarginRight = 16;
		style.ContentMarginTop = 6;
		style.ContentMarginBottom = 6;
		banner.AddThemeStyleboxOverride("panel", style);
		DungeonVisuals.AttachFrameAccent(banner, accent, 4);

		var vbox = new VBoxContainer
		{
			Alignment = BoxContainer.AlignmentMode.Center,
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		vbox.AddThemeConstantOverride("separation", 1);
		banner.AddChild(vbox);

		var font = GameResourceCache.GetSilverFont();

		var titleLbl = new Label
		{
			Text = isBossFloor ? $"FLOOR {floorIdx} BOSS VANQUISHED!" : $"FLOOR {floorIdx} WAVE CLEARED!",
			HorizontalAlignment = HorizontalAlignment.Center,
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		if (font != null) titleLbl.AddThemeFontOverride("font", font);
		titleLbl.AddThemeFontSizeOverride("font_size", 22);
		titleLbl.AddThemeColorOverride("font_color", accent.Lightened(0.20f));
		DungeonVisuals.ApplyPixelOutline(titleLbl, 2);
		vbox.AddChild(titleLbl);

		var subLbl = new Label
		{
			Text = isBossFloor
				? "+1 SKILL POINT   •   CLAIM FREE RELIC AT ALTAR"
				: "+1 SKILL POINT   •   ALTAR SHOP & STAIRS OPEN",
			HorizontalAlignment = HorizontalAlignment.Center,
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		if (font != null) subLbl.AddThemeFontOverride("font", font);
		subLbl.AddThemeFontSizeOverride("font_size", 13);
		subLbl.AddThemeColorOverride("font_color", DungeonVisuals.Parchment);
		DungeonVisuals.ApplyPixelOutline(subLbl, 1);
		vbox.AddChild(subLbl);

		rootControl.AddChild(banner);
		AddChild(canvas);

		var tw = banner.CreateTween();
		tw.SetParallel(true);
		tw.TweenProperty(banner, "scale", Vector2.One, 0.22f)
			.SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
		tw.TweenProperty(banner, "modulate:a", 1.0f, 0.16f)
			.SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
		tw.Chain().TweenInterval(2.6f);
		tw.Chain().SetParallel(true);
		tw.TweenProperty(banner, "position:y", 56.0f, 0.35f)
			.SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
		tw.TweenProperty(banner, "modulate:a", 0.0f, 0.35f)
			.SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
		tw.Chain().TweenCallback(Callable.From(() =>
		{
			if (GodotObject.IsInstanceValid(canvas)) canvas.QueueFree();
		}));
	}
}
