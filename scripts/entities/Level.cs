using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class Level : Node2D
{
	private Player _player;
	public Player Player => _player;
	private TileMapLayer _floorLayer;
	private TileMapLayer _wallsVerticalLayer;
	private TileMapLayer _wallsHorizontalLayer;
	private TileMapLayer _columnsLayer;
	private TileMapLayer _stairsLayer;
	private TileMapLayer _spikesLayer;
	private Sprite2D _entryLadder;
	private ExitLadder _exitLadder;
	private SupplyAltar _supplyAltar;
	private Vector2 _stairsPosition = Vector2.Zero;
	private bool _hasStairs = false;

	private int _currentLevelIndex = 1;
	public int CurrentLevelIndex => _currentLevelIndex;
	public GameLogic.FloorEventType CurrentFloorEvent { get; private set; } = GameLogic.FloorEventType.None;
	private CanvasModulate _canvasModulate;
	private readonly List<Node> _arenaProps = new List<Node>();
	public float LevelPlayedTime { get; private set; } = 0.0f;

	// Возвращает накопленное время уровня и обнуляет его (чтобы время не задваивалось при повторных сейвах)
	public float ConsumePlayedTime()
	{
		float t = LevelPlayedTime;
		LevelPlayedTime = 0.0f;
		return t;
	}
	private List<Vector2> _enemySpawnPositions = new List<Vector2>();
	private int _spawnPosIndex = 0;
	private AStarGrid2D _astarGrid;

	private int _halfW;
	private int _halfH;

	// Детерминированный генератор: его сид сохраняется в сейв, чтобы при загрузке
	// сгенерировалась та же самая карта, что была до выхода из игры
	private readonly RandomNumberGenerator _rng = new RandomNumberGenerator();

	private double _spikesTimer = 0.0;
	private double _playerSpikeInvulnerabilityTimer = 0.0;
	private readonly Dictionary<long, double> _playerSpikeCooldowns = new Dictionary<long, double>();
	private readonly Dictionary<ulong, double> _enemySpikeCooldowns = new Dictionary<ulong, double>();
	private bool _coopPlayersSpawned = false;
	private float _enemySyncTimer = 0.0f;
	private bool IsCoopSession => NetworkManager.Instance != null && NetworkManager.Instance.IsCoopSession;
	private bool IsServerAuthority => NetworkManager.Instance == null || NetworkManager.Instance.IsServerAuthority;
	private SpikePhase _currentSpikePhase = SpikePhase.Retracted;
	private WaveManager _cachedWaveManager;
	private Node2D _occludersContainer;
	private Node2D _wallExtrasContainer;
	private Texture2D _dungeonTilesetTexture;
	private readonly List<ulong> _expiredSpikeIds = new List<ulong>();
	private readonly List<long> _expiredPlayerSpikePeers = new List<long>();
	private static OccluderPolygon2D _leftWallOccluderPolygon;
	private static OccluderPolygon2D _rightWallOccluderPolygon;
	private static OccluderPolygon2D _leftSeamOccluderPolygon;
	private static OccluderPolygon2D _rightSeamOccluderPolygon;
	private static OccluderPolygon2D _centerWallOccluderPolygon;
	private static OccluderPolygon2D _topWallOccluderPolygon;
	private static OccluderPolygon2D _bottomWallOccluderPolygon;
	private static OccluderPolygon2D _columnOccluderPolygon;

	private enum SpikePhase
	{
		Retracted,
		Warning,
		Active,
		Retracting
	}

	private static readonly (Vector2I coords, float prob)[] FloorTiles = new[]
	{
		(new Vector2I(1, 4), 50.0f),
		(new Vector2I(2, 4), 14.0f),
		(new Vector2I(3, 4), 15.0f),
		(new Vector2I(3, 5), 0.5f),
		(new Vector2I(2, 5), 20.0f),
		(new Vector2I(1, 5), 0.15f),
		(new Vector2I(1, 6), 0.15f),
		(new Vector2I(2, 6), 0.15f)
	};

	private static readonly Vector2I[] WallTiles = new[]
	{
		new Vector2I(2, 3)
	};

	private Vector2I GetRandomFloorTile()
	{
		float total = 0f;
		foreach (var item in FloorTiles) total += item.prob;
		float roll = _rng.Randf() * total;
		float sum = 0f;
		foreach (var item in FloorTiles)
		{
			sum += item.prob;
			if (roll < sum) return item.coords;
		}
		return new Vector2I(1, 4);
	}

	public override void _Ready()
	{
		AddToGroup("Level");
		GameDataRegistry.EnsureVerifiedAtStartup();

		if (SaveManager.CurrentSave != null) {
			_currentLevelIndex = SaveManager.CurrentSave.CurrentLevelIndex;
		}

		var mapNode = GetNodeOrNull<Node2D>("Map");
		if (mapNode != null) {
			mapNode.Scale = Vector2.One;
		}

		// 1. Черный фон viewport и CanvasModulate (затемненные стены/пол видны, void за картой чисто черный)
		RenderingServer.SetDefaultClearColor(Colors.Black);
		var modulate = new CanvasModulate();
		modulate.Name = "CanvasModulate";
		float ambient = GameLogic.CalculateAmbientLight(SettingsManager.Brightness);
		modulate.Color = new Color(ambient, ambient, ambient, 1.0f);
		AddChild(modulate);
		_canvasModulate = modulate;

		// 2. Находим слой тайлов карты (для генерации окклюдеров)
		_floorLayer = GetNodeOrNull<TileMapLayer>("Map/Floor") ??
					  GetNodeOrNull<TileMapLayer>("Map/Layer0") ?? 
					  GetNodeOrNull<TileMapLayer>("Map") ??
					  GetChildren().Cast<Node>().OfType<TileMapLayer>().FirstOrDefault();

		_wallsVerticalLayer = GetNodeOrNull<TileMapLayer>("Map/WallsVertical");
		_wallsHorizontalLayer = GetNodeOrNull<TileMapLayer>("Map/WallHorizontal");
		_columnsLayer = GetNodeOrNull<TileMapLayer>("Map/Collumn");

		if (_wallsVerticalLayer != null) {
			_wallsVerticalLayer.UseParentMaterial = false;
		}
		if (_wallsHorizontalLayer != null) {
			_wallsHorizontalLayer.UseParentMaterial = false;
		}
		if (_columnsLayer != null) {
			_columnsLayer.UseParentMaterial = false;
		}

		// Draw WallHorizontal trims AFTER WallsVertical so step-wall corners and inner brick seams get clean vertical stone borders.
		if (_wallsHorizontalLayer != null && _wallsVerticalLayer != null &&
			_wallsHorizontalLayer.GetParent() == _wallsVerticalLayer.GetParent() &&
			_wallsHorizontalLayer.GetIndex() < _wallsVerticalLayer.GetIndex())
		{
			_wallsHorizontalLayer.GetParent().MoveChild(_wallsHorizontalLayer, _wallsVerticalLayer.GetIndex());
		}

		_dungeonTilesetTexture = GD.Load<Texture2D>("res://assets/objects/0x72_DungeonTilesetII_v1.7.png");

		// Удаляем призрачные тайлы с несуществующим source_id в слоях стен (в map.tscn на слое WallHorizontal остались тайлы пола от копирования)
		foreach (var layer in new[] { _wallsVerticalLayer, _wallsHorizontalLayer, _columnsLayer })
		{
			if (layer == null || layer.TileSet == null) continue;
			foreach (var cell in layer.GetUsedCells())
			{
				int sourceId = layer.GetCellSourceId(cell);
				if (sourceId == -1 || !layer.TileSet.HasSource(sourceId))
				{
					layer.EraseCell(cell);
				}
			}
		}
		
		_spikesLayer = GetNodeOrNull<TileMapLayer>("Map/Spikes");

		Node occluderHost = (Node)mapNode ?? _floorLayer?.GetParent() ?? this;
		_wallExtrasContainer = new Node2D { Name = "WallExtras" };
		occluderHost.AddChild(_wallExtrasContainer);
		if (_wallsHorizontalLayer != null && _wallExtrasContainer.GetParent() == _wallsHorizontalLayer.GetParent())
		{
			_wallsHorizontalLayer.GetParent().MoveChild(_wallExtrasContainer, _wallsHorizontalLayer.GetIndex() + 1);
		}
		_occludersContainer = new Node2D { Name = "LightOccluders" };
		occluderHost.AddChild(_occludersContainer);
		RebuildLevelOccluders();

		// 3. Находим игрока и слой лестницы
		var templatePlayer = GetNodeOrNull<Player>("Player") ?? GetTree().GetFirstNodeInGroup("Player") as Player;
		if (IsCoopSession)
		{
			if (templatePlayer != null)
			{
				templatePlayer.RemoveFromGroup("Player");
				if (templatePlayer.GetParent() == this)
				{
					RemoveChild(templatePlayer);
				}
				templatePlayer.QueueFree();
			}
			_player = null;
		}
		else
		{
			_player = templatePlayer;
		}
		_stairsLayer = GetNodeOrNull<TileMapLayer>("Map/Stairs");

		if (_stairsLayer != null) {
			var cells = _stairsLayer.GetUsedCells();
			if (cells.Count > 0) {
				_stairsPosition = _stairsLayer.ToGlobal(_stairsLayer.MapToLocal(cells[0]));
				_hasStairs = true;
			} else {
				// Fallback лестницы, если она не поставлена в редакторе (решает баг отсутствия лестницы на Level 1)
				if (_floorLayer != null) {
					var floorCells = _floorLayer.GetUsedCells();
					Vector2 playerPos = _player != null ? _player.GlobalPosition : Vector2.Zero;
					Vector2I bestCell = Vector2I.Zero;
					float maxDist = -1f;

					foreach (var cell in floorCells) {
						if (_wallsVerticalLayer != null && _wallsVerticalLayer.GetCellSourceId(cell) != -1) continue;
						if (_wallsHorizontalLayer != null && _wallsHorizontalLayer.GetCellSourceId(cell) != -1) continue;
						if (_columnsLayer != null && _columnsLayer.GetCellSourceId(cell) != -1) continue;

						Vector2 cellGlobalPos = _floorLayer.ToGlobal(_floorLayer.MapToLocal(cell));
						float dist = cellGlobalPos.DistanceTo(playerPos);
						if (dist > maxDist) {
							maxDist = dist;
							bestCell = cell;
						}
					}

					if (maxDist > 0) {
						_stairsLayer.SetCell(bestCell, 1, new Vector2I(0, 5));
						_stairsPosition = _stairsLayer.ToGlobal(_stairsLayer.MapToLocal(bestCell));
						_hasStairs = true;
					}
				}
			}
			// Скрываем лестницу на время волны
			_stairsLayer.Visible = false;
		}

		// Загружаем сохраненную сгенерированную карту: тот же сид -> та же самая арена,
		// что была до выхода из игры
		if (!IsCoopSession && SaveManager.CurrentSave != null && _currentLevelIndex > 1)
		{
			_rng.Seed = SaveManager.CurrentSave.LevelSeed;
			RollFloorEventOnly();
			GenerateRandomLevel(); // сам переместит игрока в центр и пересоберет все слои
		}

		// 4. Спавним входную лестницу под игроком на старте
		if (!IsCoopSession && _player != null) {
			SpawnEntryLadder(_player.GlobalPosition);
		}

		// 5. Инициализируем спавн-позиции мобов для первого уровня
		//(для сгенерированных уровней это уже сделано внутри GenerateRandomLevel)
		if (_currentLevelIndex == 1)
		{
			if (!IsCoopSession)
			{
				InitializeSpawnPositions();
			}
			InitializeAStarGrid();
		}

		// Скрываем и отключаем дефолтный сундук
		var chest = GetNodeOrNull<Node2D>("Map/Chest");
		if (chest != null)
		{
			chest.Hide();
			chest.GlobalPosition = new Vector2(-9999, -9999);
			if (chest is StaticBody2D sb)
			{
				foreach (var child in sb.GetChildren())
				{
					if (child is CollisionShape2D cs)
					{
						cs.SetDeferred("disabled", true);
					}
					else if (child is Area2D area)
					{
						foreach (var areaChild in area.GetChildren())
						{
							if (areaChild is CollisionShape2D acs)
							{
								acs.SetDeferred("disabled", true);
							}
						}
					}
				}
			}
		}

		Chest.OpenedChestsCount = 0;
		if (!IsCoopSession && _currentLevelIndex == 1)
		{
			_rng.Randomize();
			RollFloorEventOnly();
			GenerateSpikes();
		}

		// Скрываем и отключаем дверь полностью
		var door = GetNodeOrNull<Door>("Map/Door");
		if (door != null)
		{
			door.Hide();
			door.GlobalPosition = new Vector2(-9999, -9999);
			foreach (var child in door.GetChildren())
			{
				if (child is CollisionShape2D cs) cs.SetDeferred("disabled", true);
				else if (child is Area2D area)
				{
					foreach (var areaChild in area.GetChildren())
					{
						if (areaChild is CollisionShape2D acs) acs.SetDeferred("disabled", true);
					}
				}
			}
		}

		// Заполняем проем двери вертикальной стеной (только на ручном первом уровне,
		// на сгенерированных эти координаты — середина арены)
		if (_wallsVerticalLayer != null && _currentLevelIndex == 1)
		{
			_wallsVerticalLayer.SetCell(new Vector2I(0, -6), 0, new Vector2I(2, 1));
			_wallsVerticalLayer.SetCell(new Vector2I(-1, -6), 0, new Vector2I(2, 1));
			SpawnDungeonTorches();
		}

		if (!IsCoopSession)
		{
			ApplyFloorEventVisuals();
			SpawnArenaProps();
			SetupDungeonAtmosphere();
		}
		else
		{
			SetupDungeonAtmosphere();
			NetworkManager.Instance.NotifyLocalLevelReady();
		}
	}

	private void AttachDustMotesToPlayer(Player targetPlayer)
	{
		if (targetPlayer == null || !GodotObject.IsInstanceValid(targetPlayer)) return;
		if (targetPlayer.GetNodeOrNull("DungeonDustMotes") != null) return;

		var motes = new CpuParticles2D
		{
			Name = "DungeonDustMotes",
			Scale = Vector2.One,
			ZIndex = 4,
			Texture = DungeonVisuals.GetPixelSquareTexture(),
			Amount = 22,
			Lifetime = 4.2f,
			Preprocess = 2.0f,
			LocalCoords = false,
			EmissionShape = CpuParticles2D.EmissionShapeEnum.Rectangle,
			EmissionRectExtents = new Vector2(180.0f, 105.0f),
			Direction = new Vector2(0.35f, -1.0f),
			Spread = 35.0f,
			Gravity = new Vector2(0.0f, -1.2f),
			InitialVelocityMin = 1.5f,
			InitialVelocityMax = 4.0f,
			ScaleAmountMin = 0.25f,
			ScaleAmountMax = 0.55f
		};
		var moteGrad = new Gradient();
		moteGrad.SetColor(0, new Color(0.95f, 0.86f, 0.65f, 0.0f));
		moteGrad.AddPoint(0.45f, new Color(0.95f, 0.86f, 0.65f, 0.22f));
		moteGrad.SetColor(1, new Color(0.95f, 0.86f, 0.65f, 0.0f));
		motes.ColorRamp = moteGrad;
		targetPlayer.AddChild(motes);
	}

	private void SetupDungeonAtmosphere()
	{
		var hud = GetNodeOrNull<CanvasLayer>("HUD");
		if (hud != null && hud.Layer < 5)
		{
			hud.Layer = 5;
		}

		var vigLayer = new CanvasLayer
		{
			Name = "DungeonVignetteLayer",
			Layer = 1
		};
		var vigRect = new ColorRect
		{
			Name = "DungeonVignetteRect",
			Color = Colors.White,
			MouseFilter = Control.MouseFilterEnum.Ignore,
			Material = DungeonVisuals.GetVignetteMaterial(0.28f, 0.50f)
		};
		vigRect.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		vigLayer.AddChild(vigRect);
		AddChild(vigLayer);

		if (_player != null && GodotObject.IsInstanceValid(_player) && _player.IsLocalPlayer)
		{
			AttachDustMotesToPlayer(_player);
		}
	}

	private void SpawnEntryLadder(Vector2 position)
	{
		if (GodotObject.IsInstanceValid(_entryLadder))
		{
			_entryLadder.GetParent()?.RemoveChild(_entryLadder);
			_entryLadder.QueueFree();
			_entryLadder = null;
		}
		_entryLadder = new Sprite2D();
		_entryLadder.Name = "EntryLadder";
		_entryLadder.Texture = GD.Load<Texture2D>("res://assets/objects/0x72_DungeonTilesetII_v1.7.png");
		_entryLadder.RegionEnabled = true;
		_entryLadder.RegionRect = new Rect2(144, 80, 16, 16);
		_entryLadder.Scale = new Vector2(0.85f, 0.85f);
		_entryLadder.GlobalPosition = position;
		_entryLadder.ZIndex = -1;
		AddChild(_entryLadder);
	}

	public Vector2 GetValidChestSpawnPosition(Vector2 lastEnemyPos)
	{
		Vector2 target = lastEnemyPos;
		if (target == Vector2.Zero && _player != null && GodotObject.IsInstanceValid(_player))
		{
			target = _player.GlobalPosition;
		}
		return FindNearestFreeSpot(target);
	}

	public Vector2 PickRandomExitStairsPosition(Vector2 avoidWorldPos)
	{
		if (_floorLayer == null)
		{
			return _stairsPosition;
		}

		var floorCells = _floorLayer.GetUsedCells();
		var strictCandidates = new List<Vector2I>();
		var fallbackCandidates = new List<Vector2I>();

		foreach (var cell in floorCells)
		{
			if (_currentLevelIndex == 1 && cell.Y <= -_halfH - 2) continue;
			if (!IsCellFree(cell)) continue;

			Vector2 worldPos = _floorLayer.ToGlobal(_floorLayer.MapToLocal(cell));
			if (worldPos.DistanceTo(avoidWorldPos) < 24.0f) continue;

			fallbackCandidates.Add(cell);

			if (worldPos.DistanceTo(avoidWorldPos) < 48.0f) continue;

			bool hasMargin = true;
			for (int dx = -1; dx <= 1 && hasMargin; dx++)
			{
				for (int dy = -1; dy <= 1; dy++)
				{
					if (!IsCellFree(new Vector2I(cell.X + dx, cell.Y + dy)))
					{
						hasMargin = false;
						break;
					}
				}
			}
			if (hasMargin)
			{
				strictCandidates.Add(cell);
			}
		}

		var pool = strictCandidates.Count > 0 ? strictCandidates : fallbackCandidates;
		if (pool.Count == 0)
		{
			return _stairsPosition;
		}

		Vector2I chosenCell = pool[_rng.RandiRange(0, pool.Count - 1)];
		return _floorLayer.ToGlobal(_floorLayer.MapToLocal(chosenCell));
	}

	public void OnWaveCleared()
	{
		Vector2 chestPos = GetValidChestSpawnPosition(Vector2.Zero);
		Vector2 stairsPos = PickRandomExitStairsPosition(chestPos);
		OnWaveCleared(chestPos, stairsPos);
	}

	public void OnWaveCleared(Vector2 chestWorldPos, Vector2 stairsWorldPos)
	{
		// 1. Входная лестница исчезает
		if (GodotObject.IsInstanceValid(_entryLadder)) {
			_entryLadder.QueueFree();
			_entryLadder = null;
		}

		// 2. Размещаем выходную лестницу в случайной клетке пола и спавним триггер взаимодействия
		if (_stairsLayer != null && _floorLayer != null)
		{
			Vector2 validStairsPos = stairsWorldPos != Vector2.Zero
				? FindNearestFreeSpot(stairsWorldPos)
				: PickRandomExitStairsPosition(chestWorldPos);
			Vector2I stairCell = _stairsLayer.LocalToMap(_stairsLayer.ToLocal(validStairsPos));
			_stairsLayer.Clear();
			_stairsLayer.SetCell(stairCell, 1, new Vector2I(0, 5));
			_stairsPosition = _stairsLayer.ToGlobal(_stairsLayer.MapToLocal(stairCell));
			_hasStairs = true;
			_stairsLayer.Visible = true;
			_spikesLayer?.EraseCell(stairCell);
		}
		else if (_stairsLayer != null)
		{
			_stairsLayer.Visible = true;
		}

		if (_hasStairs) {
			SpawnExitLadderTrigger();
		}

		// 3. Спавним Сундук (Алтарь снабжения) там, где умер последний моб этажа
		SpawnSupplyAltar(chestWorldPos);
	}

	private void SpawnSupplyAltar(Vector2 targetWorldPos)
	{
		if (GodotObject.IsInstanceValid(_supplyAltar))
		{
			_supplyAltar.GetParent()?.RemoveChild(_supplyAltar);
			_supplyAltar.QueueFree();
			_supplyAltar = null;
		}

		Vector2 altarPos = GetValidChestSpawnPosition(targetWorldPos);
		if (_floorLayer != null && _spikesLayer != null)
		{
			Vector2I chestCell = _floorLayer.LocalToMap(_floorLayer.ToLocal(altarPos));
			_spikesLayer.EraseCell(chestCell);
		}

		_supplyAltar = new SupplyAltar { Name = "SupplyAltar" };
		_supplyAltar.Setup(this);
		_supplyAltar.GlobalPosition = altarPos;
		CallDeferred("add_child", _supplyAltar);
	}

	private void SpawnExitLadderTrigger()
	{
		if (GodotObject.IsInstanceValid(_exitLadder))
		{
			_exitLadder.GetParent()?.RemoveChild(_exitLadder);
			_exitLadder.QueueFree();
			_exitLadder = null;
		}
		_exitLadder = new ExitLadder { Name = "ExitLadder" };
		_exitLadder.Setup(this);
		_exitLadder.GlobalPosition = _stairsPosition;
		CallDeferred("add_child", _exitLadder);
	}

	public void DescendStairs()
	{
		if (IsCoopSession)
		{
			if (!IsServerAuthority) return;
			int nextFloor = _currentLevelIndex + 1;
			ulong nextSeed = NetworkManager.GenerateRandomSeed();
			NetworkManager.Instance.CurrentRunFloorIndex = nextFloor;
			NetworkManager.Instance.CurrentFloorSeed = nextSeed;
			ServerInitializeCoopFloor(nextFloor, nextSeed);
			return;
		}

		// 1. Удаляем триггер взаимодействия, алтарь и интерактивные объекты
		ClearArenaProps();
		if (GodotObject.IsInstanceValid(_exitLadder)) {
			_exitLadder.GetParent()?.RemoveChild(_exitLadder);
			_exitLadder.QueueFree();
			_exitLadder = null;
		}
		if (GodotObject.IsInstanceValid(_supplyAltar)) {
			_supplyAltar.GetParent()?.RemoveChild(_supplyAltar);
			_supplyAltar.QueueFree();
			_supplyAltar = null;
		}

		// 2. Скрываем лестницу
		if (_stairsLayer != null) {
			_stairsLayer.Visible = false;
		}

		// 3. Переход на следующий уровень (первый уровень сделан вручную, дальше — генерация)
		_currentLevelIndex++;
		// Новый сид для новой карты; сохраняем его, чтобы после загрузки сейва
		// сгенерировалась точно такая же карта
		_rng.Seed = (ulong)(GD.Randi() << 32) | GD.Randi();
		RollFloorEventOnly();
		GenerateRandomLevel();
		ApplyFloorEventVisuals();
		SpawnArenaProps();

		if (SaveManager.CurrentSave != null)
		{
			SaveManager.CurrentSave.LevelSeed = _rng.Seed;
			if (_player != null)
			{
				SaveManager.CommitFloorCheckpoint(this, _player, SaveManager.CurrentSave);
			}
			else
			{
				SaveManager.SaveGame(SaveManager.CurrentSave);
			}
		}

		// 4. Спавним входную лестницу под игроком
		if (_player != null) {
			SpawnEntryLadder(_player.GlobalPosition);
		}

		// 5. Запускаем следующую волну через countdown
		StartNextFloorWaveCountdown();
	}

	private string BuildWaveAnnouncementMessage(int waveNum)
	{
		string baseMsg = (waveNum % 5 == 0)
			? $"BOSS FLOOR {waveNum} IS COMING!"
			: $"Wave {waveNum} is coming!";
		string evtTitle = GameLogic.GetFloorEventTitle(CurrentFloorEvent);
		return !string.IsNullOrEmpty(evtTitle) ? $"{baseMsg} [{evtTitle}]" : baseMsg;
	}

	private void StartNextFloorWaveCountdown()
	{
		var waveManager = GetTree().GetFirstNodeInGroup("WaveManager") as WaveManager;
		var hud = GetTree().GetFirstNodeInGroup("HUD") as HUD;
		if (hud != null)
		{
			hud.StartCountdown(5.0f, BuildWaveAnnouncementMessage(_currentLevelIndex));
		}
		else if (waveManager != null && IsServerAuthority)
		{
			waveManager.StartNextWave();
		}
	}

	// Множитель сложности для будущих настроек сложности (влияет на число монстров и размер/число секций карты)
	public static float DifficultyMultiplier = 1.0f;

	public int GetExpectedMonsterCount()
	{
		if (IsCoopSession && NetworkManager.Instance != null)
		{
			return GameLogic.CalculateCoopWaveEnemyCount(_currentLevelIndex, DifficultyMultiplier, NetworkManager.Instance.CoopPlayerCount);
		}
		return GameLogic.CalculateWaveEnemyCount(_currentLevelIndex, DifficultyMultiplier);
	}

	private void GenerateRandomLevel()
	{
		int expectedMonsters = GetExpectedMonsterCount();
		GD.Print($"Generating Compound Level {_currentLevelIndex} (Monsters: ~{expectedMonsters}, Difficulty: {DifficultyMultiplier:0.00})...");

		// 1. Очистка старых объектов с предыдущего уровня
		foreach (var child in GetChildren())
		{
			if (child is WeaponPickup || child is Coin || child is ManaCrystal || child is SupplyAltar || child is DungeonTorch || child is Enemy || child is Projectile || child is Fireball)
			{
				RemoveChild(child);
				child.QueueFree();
			}
		}

		// 2. Очищаем тайлы
		_floorLayer.Clear();
		if (_wallsVerticalLayer != null) _wallsVerticalLayer.Clear();
		if (_wallsHorizontalLayer != null) _wallsHorizontalLayer.Clear();
		if (_columnsLayer != null) _columnsLayer.Clear();
		if (_stairsLayer != null) _stairsLayer.Clear();
		if (_spikesLayer != null) _spikesLayer.Clear();
		if (_wallExtrasContainer != null)
		{
			foreach (Node child in _wallExtrasContainer.GetChildren())
			{
				child.QueueFree();
			}
		}

		var door = GetNodeOrNull<Door>("Map/Door");
		if (door != null)
		{
			door.HideDoor();
		}

		// 3. Строим составную арену из главного центрального квадрата/прямоугольника
		// и примыкающих к нему дополнительных квадратов (меньшего или такого же размера) в зависимости от числа монстров
		var rooms = new List<Rect2I>();

		int mainHalfW = _rng.RandiRange(10, 12) + Mathf.Clamp(expectedMonsters / 25, 0, 3);
		int mainHalfH = _rng.RandiRange(9, 11) + Mathf.Clamp(expectedMonsters / 25, 0, 3);
		Rect2I mainRoom = new Rect2I(-mainHalfW, -mainHalfH, mainHalfW * 2 + 1, mainHalfH * 2 + 1);
		rooms.Add(mainRoom);

		// Количество примыкающих квадратов умеренное, чтобы арена была компактнее
		int minExtraSquares = Mathf.Clamp(1 + (_currentLevelIndex - 2) / 3 + expectedMonsters / 26, 1, 4);
		int maxExtraSquares = Mathf.Clamp(minExtraSquares + 1, 2, 5);
		int extraSquaresCount = _rng.RandiRange(minExtraSquares, maxExtraSquares);

		// Перемешиваем 4 направления (0=Север, 1=Юг, 2=Восток, 3=Запад), чтобы первые секции росли в разные стороны
		int[] cardinalOrder = { 0, 1, 2, 3 };
		for (int i = 3; i > 0; i--)
		{
			int j = _rng.RandiRange(0, i);
			(cardinalOrder[i], cardinalOrder[j]) = (cardinalOrder[j], cardinalOrder[i]);
		}

		// Шанс того, что примыкающий квадрат будет такого же размера (а не поменьше), растет с числом монстров
		float equalSizeChance = Mathf.Clamp(0.28f + expectedMonsters * 0.005f, 0.30f, 0.65f);

		for (int i = 0; i < extraSquaresCount; i++)
		{
			// Первые секции прикрепляем к центральному залу, а следующие могут ветвиться и от уже созданных секций
			int parentIdx = (i < 3 || rooms.Count == 1) ? 0 : _rng.RandiRange(0, rooms.Count - 1);
			Rect2I parentRoom = rooms[parentIdx];

			int dir = (i < 4 && parentIdx == 0) ? cardinalOrder[i] : _rng.RandiRange(0, 3);
			bool isEqualSize = _rng.Randf() < equalSizeChance;

			float scaleFactor = isEqualSize
				? _rng.RandfRange(0.82f, 0.98f)
				: _rng.RandfRange(0.52f, 0.75f);

			int baseSpan = Math.Max(parentRoom.Size.X, parentRoom.Size.Y);
			int newW = Mathf.Clamp(Mathf.RoundToInt(baseSpan * scaleFactor) + _rng.RandiRange(-2, 2), 9, 20);
			int newH = Mathf.Clamp(Mathf.RoundToInt(baseSpan * scaleFactor) + _rng.RandiRange(-2, 2), 9, 20);

			// Перекрытие в 3 тайла внутрь родительского квадрата гарантирует широкий проход без узких затыков
			const int overlap = 3;
			const int minSharedPassage = 7;
			int nx = 0;
			int ny = 0;

			if (dir == 0) // Север (-Y)
			{
				ny = parentRoom.Position.Y - newH + overlap;
				int minX = parentRoom.Position.X - newW + minSharedPassage;
				int maxX = parentRoom.End.X - minSharedPassage;
				nx = minX <= maxX ? _rng.RandiRange(minX, maxX) : parentRoom.Position.X;
			}
			else if (dir == 1) // Юг (+Y)
			{
				ny = parentRoom.End.Y - overlap;
				int minX = parentRoom.Position.X - newW + minSharedPassage;
				int maxX = parentRoom.End.X - minSharedPassage;
				nx = minX <= maxX ? _rng.RandiRange(minX, maxX) : parentRoom.Position.X;
			}
			else if (dir == 2) // Восток (+X)
			{
				nx = parentRoom.End.X - overlap;
				int minY = parentRoom.Position.Y - newH + minSharedPassage;
				int maxY = parentRoom.End.Y - minSharedPassage;
				ny = minY <= maxY ? _rng.RandiRange(minY, maxY) : parentRoom.Position.Y;
			}
			else // Запад (-X)
			{
				nx = parentRoom.Position.X - newW + overlap;
				int minY = parentRoom.Position.Y - newH + minSharedPassage;
				int maxY = parentRoom.End.Y - minSharedPassage;
				ny = minY <= maxY ? _rng.RandiRange(minY, maxY) : parentRoom.Position.Y;
			}

			rooms.Add(new Rect2I(nx, ny, newW, newH));
		}

		// 4. Собираем все клетки пола в единое множество и сглаживаем узкие перегородки (1-2 тайла) между соседними секциями
		var floorSet = new HashSet<Vector2I>();
		int boundMinX = 0, boundMaxX = 0, boundMinY = 0, boundMaxY = 0;

		foreach (var r in rooms)
		{
			if (r.Position.X < boundMinX) boundMinX = r.Position.X;
			if (r.End.X > boundMaxX) boundMaxX = r.End.X;
			if (r.Position.Y < boundMinY) boundMinY = r.Position.Y;
			if (r.End.Y > boundMaxY) boundMaxY = r.End.Y;

			for (int x = r.Position.X; x < r.End.X; x++)
			{
				for (int y = r.Position.Y; y < r.End.Y; y++)
				{
					floorSet.Add(new Vector2I(x, y));
				}
			}
		}

		// Сглаживание 1-2 тайловых зазоров между параллельными квадратами, чтобы стены не резали карту тонкими полосками
		for (int pass = 0; pass < 2; pass++)
		{
			var bridgedCells = new List<Vector2I>();
			for (int x = boundMinX; x <= boundMaxX; x++)
			{
				for (int y = boundMinY; y <= boundMaxY; y++)
				{
					Vector2I c = new Vector2I(x, y);
					if (floorSet.Contains(c)) continue;

					bool bridgeH = floorSet.Contains(new Vector2I(x - 1, y)) &&
								   (floorSet.Contains(new Vector2I(x + 1, y)) || floorSet.Contains(new Vector2I(x + 2, y)));
					bool bridgeV = floorSet.Contains(new Vector2I(x, y - 1)) &&
								   (floorSet.Contains(new Vector2I(x, y + 1)) || floorSet.Contains(new Vector2I(x, y + 2)));

					if (bridgeH || bridgeV)
					{
						bridgedCells.Add(c);
					}
				}
			}
			foreach (var bc in bridgedCells) floorSet.Add(bc);
		}

		_halfW = Math.Max(Math.Abs(boundMinX), Math.Abs(boundMaxX)) + 2;
		_halfH = Math.Max(Math.Abs(boundMinY), Math.Abs(boundMaxY)) + 2;

		// Заполняем тайлы пола
		foreach (var cell in floorSet)
		{
			_floorLayer.SetCell(cell, 1, GetRandomFloorTile());
		}

		// 5. Выстраиваем контур внешних стен вокруг всей составной фигуры
		Vector2I hTile = new Vector2I(2, 10); // Source ID 2 (боковая стена)

		int genMinX = boundMinX - 3;
		int genMaxX = boundMaxX + 3;
		int genMinY = boundMinY - 3;
		int genMaxY = boundMaxY + 3;

		// 5.1. Проход кирпичных стен (WallsVertical):
		// Нижняя стена: 2 ряда (верхний ряд с тайлом пола под полупрозрачной шапкой).
		// Верхняя стена: 2 ряда (нижний ряд - тело, верхний - шапка).
		var floorTuples = new HashSet<(int x, int y)>(floorSet.Count);
		foreach (var c in floorSet) floorTuples.Add((c.X, c.Y));
		var lowerCapTuples = new HashSet<(int x, int y)>();
		var upperCapTuples = new HashSet<(int x, int y)>();
		var upperBrickTuples = new HashSet<(int x, int y)>();
		var lowerBrickTuples = new HashSet<(int x, int y)>();

		if (_wallsVerticalLayer != null)
		{
			for (int x = genMinX; x <= genMaxX; x++)
			{
				for (int y = genMinY; y <= genMaxY; y++)
				{
					Vector2I pos = new Vector2I(x, y);
					if (floorSet.Contains(pos)) continue;

					bool floorNorth = floorSet.Contains(new Vector2I(x, y - 1));
					bool floorSouth = floorSet.Contains(new Vector2I(x, y + 1));
					int variantX = (((x % 3) + 3) % 3) + 1;

					// Нижняя стена (комната сверху)
					if (floorNorth && !floorSouth)
					{
						_floorLayer.SetCell(pos, 1, GetRandomFloorTile());
						_wallsVerticalLayer.SetCell(pos, 0, new Vector2I(variantX, 0));
						lowerCapTuples.Add((x, y));

						Vector2I posBelow = new Vector2I(x, y + 1);
						if (!floorSet.Contains(posBelow))
						{
							_wallsVerticalLayer.SetCell(posBelow, 0, new Vector2I(variantX, 1));
							lowerBrickTuples.Add((posBelow.X, posBelow.Y));
						}
					}
					// Верхняя стена (комната снизу)
					else if (floorSouth && !floorNorth)
					{
						_wallsVerticalLayer.SetCell(pos, 0, new Vector2I(variantX, 1));
						upperBrickTuples.Add((x, y));

						Vector2I posAbove = new Vector2I(x, y - 1);
						if (!floorSet.Contains(posAbove))
						{
							_wallsVerticalLayer.SetCell(posAbove, 0, new Vector2I(variantX, 0));
							upperCapTuples.Add((posAbove.X, posAbove.Y));
						}
					}
				}
			}
		}

		// 5.2. Проход боковых стен и ступенчатых/внешних углов (WallHorizontal):
		if (_wallsHorizontalLayer != null)
		{
			for (int x = genMinX; x <= genMaxX; x++)
			{
				for (int y = genMinY; y <= genMaxY; y++)
				{
					Vector2I pos = new Vector2I(x, y);
					var side = GameLogic.ComputeHorizontalWallSide(
						x, y, floorTuples, lowerCapTuples, upperBrickTuples, lowerBrickTuples);

					if (side == GameLogic.HorizontalWallSide.RightEdgeOfLeftVoid)
					{
						_wallsHorizontalLayer.SetCell(pos, 2, hTile, 1);
					}
					else if (side == GameLogic.HorizontalWallSide.LeftEdgeOfRightVoid)
					{
						_wallsHorizontalLayer.SetCell(pos, 2, hTile, 0);
					}
					else if (side == GameLogic.HorizontalWallSide.Both)
					{
						_wallsHorizontalLayer.SetCell(pos, 2, hTile, 0);
						AddWallExtraSprite(pos, new Rect2(32, 160, 16, 16), true, Vector2.Zero);
					}

					// Complete 4x4 stone corner caps at outer top-left / top-right wall corners above vertical trims.
					if (side == GameLogic.HorizontalWallSide.None &&
						upperCapTuples.Contains((x + 1, y)) &&
						GameLogic.ComputeHorizontalWallSide(x, y + 1, floorTuples, lowerCapTuples, upperBrickTuples, lowerBrickTuples)
							is GameLogic.HorizontalWallSide.RightEdgeOfLeftVoid or GameLogic.HorizontalWallSide.Both)
					{
						AddWallExtraSprite(pos, new Rect2(32, 12, 4, 4), false, new Vector2(6, 6));
					}
					if (side == GameLogic.HorizontalWallSide.None &&
						upperCapTuples.Contains((x - 1, y)) &&
						GameLogic.ComputeHorizontalWallSide(x, y + 1, floorTuples, lowerCapTuples, upperBrickTuples, lowerBrickTuples)
							is GameLogic.HorizontalWallSide.LeftEdgeOfRightVoid or GameLogic.HorizontalWallSide.Both)
					{
						AddWallExtraSprite(pos, new Rect2(32, 12, 4, 4), true, new Vector2(-6, 6));
					}
				}
			}
		}

		var floorList = floorSet.ToList();

		// 6. Рисуем внутренние колонны/препятствия пропорционально площади всей составной карты
		if (_columnsLayer != null && floorList.Count > 0)
		{
			int numPillars = Mathf.Clamp(floorList.Count / 165, 2, 6);
			var pillarPositions = new List<Vector2I>();

			for (int i = 0; i < numPillars; i++)
			{
				int px = 0;
				int py = 0;
				bool valid = false;
				int attempts = 0;

				while (!valid && attempts < 120)
				{
					attempts++;
					Vector2I candidate = floorList[_rng.RandiRange(0, floorList.Count - 1)];
					px = candidate.X;
					py = candidate.Y;

					// Не спавним колонны у центра (0,0), где появляется игрок
					if (Math.Abs(px) < 5 && Math.Abs(py) < 6) continue;

					// Проверяем, что вокруг колонны (3 тайла в высоту + отступ 3 тайла от стен) везде пол
					bool hasMargin = true;
					for (int dx = -3; dx <= 3 && hasMargin; dx++)
					{
						for (int dy = -2; dy <= 4; dy++)
						{
							if (!floorSet.Contains(new Vector2I(px + dx, py + dy)))
							{
								hasMargin = false;
								break;
							}
						}
					}
					if (!hasMargin) continue;

					// Проверяем расстояние до других колонн
					bool tooClose = false;
					foreach (var pos in pillarPositions)
					{
						if (Math.Abs(pos.X - px) < 5 && Math.Abs(pos.Y - py) < 6)
						{
							tooClose = true;
							break;
						}
					}
					if (tooClose) continue;

					valid = true;
				}

				if (valid)
				{
					pillarPositions.Add(new Vector2I(px, py));
					_columnsLayer.SetCell(new Vector2I(px, py), 0, new Vector2I(15, 5));
					_columnsLayer.SetCell(new Vector2I(px, py + 1), 0, new Vector2I(15, 6));
					_columnsLayer.SetCell(new Vector2I(px, py + 2), 0, new Vector2I(15, 7));
				}
			}
		}

		// 7. Размещаем выходную лестницу в одной из отдаленных секций карты
		if (_stairsLayer != null && floorList.Count > 0)
		{
			int sx = 0, sy = 0;
			bool validPosition = false;
			int stairAttempts = 0;

			while (!validPosition && stairAttempts < 150)
			{
				stairAttempts++;
				Vector2I candidate = floorList[_rng.RandiRange(0, floorList.Count - 1)];
				sx = candidate.X;
				sy = candidate.Y;

				// Стараемся спавнить лестницу подальше от центра (в примыкающих квадратах), постепенно снижая порог
				int minDist = stairAttempts < 90 ? 10 : 5;
				if (Math.Abs(sx) < minDist && Math.Abs(sy) < minDist) continue;

				// Отступ от стен минимум 2 тайла
				bool insideMargin = true;
				for (int dx = -2; dx <= 2 && insideMargin; dx++)
				{
					for (int dy = -2; dy <= 2; dy++)
					{
						if (!floorSet.Contains(new Vector2I(sx + dx, sy + dy)))
						{
							insideMargin = false;
							break;
						}
					}
				}
				if (!insideMargin) continue;

				bool hitObstacle = false;
				for (int dx = -1; dx <= 1 && !hitObstacle; dx++)
				{
					for (int dy = -1; dy <= 1; dy++)
					{
						Vector2I check = new Vector2I(sx + dx, sy + dy);
						if (_columnsLayer != null && _columnsLayer.GetCellSourceId(check) != -1)
						{
							hitObstacle = true;
							break;
						}
					}
				}

				if (!hitObstacle)
				{
					validPosition = true;
				}
			}

			if (!validPosition)
			{
				sx = Math.Max(4, mainHalfW - 3);
				sy = Math.Max(4, mainHalfH - 3);
			}

			_stairsLayer.SetCell(new Vector2I(sx, sy), 1, new Vector2I(0, 5));
			_stairsPosition = _stairsLayer.ToGlobal(_stairsLayer.MapToLocal(new Vector2I(sx, sy)));
			_hasStairs = true;
			_stairsLayer.Visible = false;
		}

		// 8. Перестраиваем LightOccluder2D под сгенерированную геометрию стен и колонн
		RebuildLevelOccluders();

		// 9. Перемещаем игрока в центр Vector2.Zero
		if (!IsCoopSession && _player != null)
		{
			_player.GlobalPosition = Vector2.Zero;
		}

		// 10. Обновляем доступные точки спавна врагов, AStar-сетку, ловушки и настенные факелы
		InitializeSpawnPositions();
		InitializeAStarGrid();
		GenerateSpikes();
		SpawnDungeonTorches();
	}

	private void ClearArenaProps()
	{
		foreach (var node in _arenaProps)
		{
			if (GodotObject.IsInstanceValid(node))
			{
				node.GetParent()?.RemoveChild(node);
				node.QueueFree();
			}
		}
		_arenaProps.Clear();
		if (IsInsideTree())
		{
			foreach (Node n in GetTree().GetNodesInGroup("Enemies"))
			{
				if (n is MimicEnemy m && GodotObject.IsInstanceValid(m))
				{
					m.GetParent()?.RemoveChild(m);
					m.QueueFree();
				}
			}
		}
	}

	private void RollFloorEventOnly()
	{
		int eventRoll = _rng != null ? _rng.RandiRange(0, 99) : (int)(GD.Randi() % 100);
		int spikeRoll = _rng != null ? _rng.RandiRange(0, 99) : (int)(GD.Randi() % 100);
		CurrentFloorEvent = GameLogic.RollFloorEventWithSpike(_currentLevelIndex, eventRoll, spikeRoll);
	}

	private void ApplyFloorEventVisuals()
	{
		if (_canvasModulate != null && GodotObject.IsInstanceValid(_canvasModulate))
		{
			float ambient = GameLogic.CalculateAmbientLight(SettingsManager.Brightness);
			if (CurrentFloorEvent == GameLogic.FloorEventType.PitchBlack)
			{
				ambient = Mathf.Max(0.20f, ambient * 0.65f);
			}
			_canvasModulate.Color = new Color(ambient, ambient, ambient, 1.0f);
		}

		CallDeferred(nameof(SyncFloorEventToHud));
	}

	private void SyncFloorEventToHud()
	{
		var hud = GetTree()?.GetFirstNodeInGroup("HUD") as HUD;
		hud?.SetFloorEventUI(CurrentFloorEvent);
	}

	private void SpawnArenaProps()
	{
		ClearArenaProps();
		if (_enemySpawnPositions == null || _enemySpawnPositions.Count < 4) return;

		Vector2 playerPos = !IsCoopSession && _player != null && GodotObject.IsInstanceValid(_player)
			? _player.GlobalPosition
			: Vector2.Zero;
		var usedPositions = new List<Vector2>();

		bool IsFreeArenaSpot(Vector2 candidate, float minSpacing)
		{
			if (candidate.DistanceTo(playerPos) < 52.0f) return false;
			if (_hasStairs && candidate.DistanceTo(_stairsPosition) < 42.0f) return false;
			foreach (var p in usedPositions)
			{
				if (candidate.DistanceTo(p) < minSpacing) return false;
			}
			return true;
		}

		int bombTarget = Mathf.Clamp(2 + _currentLevelIndex / 3, 3, 5);
		int bombsPlaced = 0;
		for (int i = 0; i < _enemySpawnPositions.Count && bombsPlaced < bombTarget; i++)
		{
			Vector2 pos = _enemySpawnPositions[i];
			if (!IsFreeArenaSpot(pos, 52.0f)) continue;

			var bomb = new ExplosiveBomb
			{
				Name = $"Bomb_{bombsPlaced}",
				GlobalPosition = pos,
				BlastDamage = 28.0f + (_currentLevelIndex - 1) * 3.0f,
				BlastRadius = 50.0f
			};
			AddChild(bomb);
			_arenaProps.Add(bomb);
			usedPositions.Add(pos);
			bombsPlaced++;
		}

		// Trap Lever + 2-3 linked floor spike pads
		for (int i = 0; i < _enemySpawnPositions.Count; i++)
		{
			Vector2 leverPos = _enemySpawnPositions[i];
			if (!IsFreeArenaSpot(leverPos, 46.0f)) continue;

			var lever = new TrapLever
			{
				Name = "TrapLever_0",
				GlobalPosition = leverPos,
				SurgeDamage = 20.0f + (_currentLevelIndex - 1) * 2.5f
			};
			AddChild(lever);
			_arenaProps.Add(lever);
			usedPositions.Add(leverPos);

			int padsAdded = 0;
			for (int j = 0; j < _enemySpawnPositions.Count && padsAdded < 3; j++)
			{
				Vector2 padPos = _enemySpawnPositions[j];
				float d = padPos.DistanceTo(leverPos);
				if (d < 24.0f || d > 84.0f || !IsFreeArenaSpot(padPos, 24.0f)) continue;

				var pad = new SpikeTrapPad { Name = $"SpikePad_{padsAdded}", GlobalPosition = padPos };
				AddChild(pad);
				_arenaProps.Add(pad);
				usedPositions.Add(padPos);
				lever.LinkedPads.Add(pad);
				padsAdded++;
			}
			break;
		}

		// Rare Mimic Chest starting Floor 2 (non-boss floors ~45% chance)
		if (_currentLevelIndex >= 2 && _currentLevelIndex % 5 != 0 && _rng.Randf() < 0.45f)
		{
			for (int i = _enemySpawnPositions.Count - 1; i >= 0; i--)
			{
				Vector2 mimicPos = _enemySpawnPositions[i];
				if (!IsFreeArenaSpot(mimicPos, 48.0f)) continue;

				var mimicChest = new MimicChest
				{
					Name = "MimicChest_0",
					GlobalPosition = mimicPos,
					LevelIndex = _currentLevelIndex
				};
				AddChild(mimicChest);
				_arenaProps.Add(mimicChest);
				usedPositions.Add(mimicPos);
				break;
			}
		}
	}

	private void SpawnDungeonTorches()
	{
		if (_floorLayer == null) return;

		var candidatePositions = new List<Vector2>();

		// 1. Ищем верхние стены (где ниже стены сразу идет клетка пола)
		if (_wallsVerticalLayer != null)
		{
			foreach (var wallCell in _wallsVerticalLayer.GetUsedCells())
			{
				Vector2I belowCell = wallCell + new Vector2I(0, 1);
				if (_floorLayer.GetCellSourceId(belowCell) != -1 &&
					_wallsVerticalLayer.GetCellSourceId(belowCell) == -1 &&
					(_wallsHorizontalLayer == null || _wallsHorizontalLayer.GetCellSourceId(belowCell) == -1))
				{
					Vector2 worldPos = _floorLayer.ToGlobal(_floorLayer.MapToLocal(wallCell)) + new Vector2(0, 4.5f);
					candidatePositions.Add(worldPos);
				}
			}
		}

		// 2. Ищем основания внутренних колонн
		if (_columnsLayer != null)
		{
			foreach (var colCell in _columnsLayer.GetUsedCells())
			{
				Vector2I belowCell = colCell + new Vector2I(0, 1);
				if (_floorLayer.GetCellSourceId(belowCell) != -1 &&
					_columnsLayer.GetCellSourceId(belowCell) == -1)
				{
					Vector2 worldPos = _floorLayer.ToGlobal(_floorLayer.MapToLocal(colCell)) + new Vector2(0, 3.3f);
					candidatePositions.Add(worldPos);
				}
			}
		}

		// Перемешиваем кандидатов детерминированным генератором уровня
		for (int i = candidatePositions.Count - 1; i > 0; i--)
		{
			int j = _rng.RandiRange(0, i);
			(candidatePositions[i], candidatePositions[j]) = (candidatePositions[j], candidatePositions[i]);
		}

		var placedTorches = new List<Vector2>();
		float minTorchSpacing = 74.0f;
		int maxTorches = Mathf.Clamp(6 + _currentLevelIndex * 2, 6, 20);

		foreach (var pos in candidatePositions)
		{
			if (placedTorches.Count >= maxTorches) break;

			bool tooClose = false;
			foreach (var existing in placedTorches)
			{
				if (pos.DistanceTo(existing) < minTorchSpacing)
				{
					tooClose = true;
					break;
				}
			}
			if (tooClose) continue;

			placedTorches.Add(pos);
			var torch = new DungeonTorch();
			torch.GlobalPosition = pos;
			AddChild(torch);
		}
	}

	public void InitializeSpawnPositions()
	{
		_enemySpawnPositions.Clear();
		_spawnPosIndex = 0;

		var floorLayer = _floorLayer;

		if (floorLayer == null) return;

		var floorCells = floorLayer.GetUsedCells();

		foreach (var cell in floorCells)
		{
			if (_halfH > 0 && cell.Y <= -_halfH - 2) continue;

			// Не спавним врагов внутри стен
			if (_wallsVerticalLayer != null && _wallsVerticalLayer.GetCellSourceId(cell) != -1) continue;
			if (_wallsHorizontalLayer != null && _wallsHorizontalLayer.GetCellSourceId(cell) != -1) continue;
			if (_columnsLayer != null && _columnsLayer.GetCellSourceId(cell) != -1) continue;

			Vector2 globalPos = floorLayer.ToGlobal(floorLayer.MapToLocal(cell));
			_enemySpawnPositions.Add(globalPos);
		}

		// Перемешиваем точки (Фишер-Йетс на сеяном генераторе — детерминированно при загрузке из сейва)
		for (int i = _enemySpawnPositions.Count - 1; i > 0; i--)
		{
			int j = _rng.RandiRange(0, i);
			(_enemySpawnPositions[i], _enemySpawnPositions[j]) = (_enemySpawnPositions[j], _enemySpawnPositions[i]);
		}
	}

	private int _spawnQuadrant = 0;

	private Rect2 GetCameraWorldRect(Vector2 fallbackCenter, float margin = 8.0f)
	{
		var viewport = GetViewport();
		if (viewport != null)
		{
			Transform2D invCanvas = viewport.GetCanvasTransform().AffineInverse();
			Rect2 visibleRect = viewport.GetVisibleRect();
			Vector2 topLeft = invCanvas * visibleRect.Position;
			Vector2 bottomRight = invCanvas * visibleRect.End;
			Rect2 worldRect = new Rect2(topLeft, bottomRight - topLeft).Abs();
			if (worldRect.Size.X > 4.0f && worldRect.Size.Y > 4.0f)
			{
				return worldRect.Grow(margin);
			}
		}

		Vector2 halfSize = new Vector2(180.0f + margin, 100.0f + margin);
		return new Rect2(fallbackCenter - halfSize, halfSize * 2.0f);
	}

	public Vector2 GetNextEnemySpawnPosition(Vector2 playerPos, float minPlayerDist = 48.0f)
	{
		int targetQuad = _spawnQuadrant;
		_spawnQuadrant = (_spawnQuadrant + 1) % 4;

		Rect2 cameraRect = GetCameraWorldRect(playerPos, 8.0f);
		Rect2 strictCameraRect = GetCameraWorldRect(playerPos, 0.0f);

		if (_enemySpawnPositions == null || _enemySpawnPositions.Count == 0)
		{
			float baseAngle = targetQuad * (Mathf.Pi * 0.5f) + (float)GD.RandRange(0.15f, 1.4f);
			float offscreenDist = Mathf.Max(cameraRect.Size.X, cameraRect.Size.Y) * 0.58f;
			return playerPos + new Vector2(Mathf.Cos(baseAngle), Mathf.Sin(baseAngle)) * offscreenDist;
		}

		var alivePlayers = IsCoopSession ? NetworkManager.GetAlivePlayers(GetTree()) : null;
		bool IsFarFromAlivePlayers(Vector2 candidate)
		{
			if (candidate.DistanceTo(playerPos) < minPlayerDist) return false;
			if (alivePlayers != null)
			{
				foreach (var ap in alivePlayers)
				{
					if (candidate.DistanceTo(ap.GlobalPosition) < minPlayerDist) return false;
				}
			}
			return true;
		}

		// 1. Сначала ищем точку строго вне камеры игрока (с запасом) в целевом квадранте
		for (int i = 0; i < _enemySpawnPositions.Count; i++)
		{
			int idx = (_spawnPosIndex + i) % _enemySpawnPositions.Count;
			Vector2 pos = _enemySpawnPositions[idx];
			if (cameraRect.HasPoint(pos) || !IsFarFromAlivePlayers(pos)) continue;

			Vector2 rel = pos - playerPos;
			int quad = (rel.X >= 0 ? 0 : 1) + (rel.Y >= 0 ? 0 : 2);
			if (quad == targetQuad)
			{
				_spawnPosIndex = (idx + 1) % _enemySpawnPositions.Count;
				return pos;
			}
		}

		// 2. Если в целевом квадранте вне камеры точек нет (например, игрок стоит у стены), берем любую точку вне камеры
		for (int i = 0; i < _enemySpawnPositions.Count; i++)
		{
			int idx = (_spawnPosIndex + i) % _enemySpawnPositions.Count;
			Vector2 pos = _enemySpawnPositions[idx];
			if (!cameraRect.HasPoint(pos) && IsFarFromAlivePlayers(pos))
			{
				_spawnPosIndex = (idx + 1) % _enemySpawnPositions.Count;
				return pos;
			}
		}

		// 3. Проверяем без дополнительного отступа на случай маленькой первой карты
		for (int i = 0; i < _enemySpawnPositions.Count; i++)
		{
			int idx = (_spawnPosIndex + i) % _enemySpawnPositions.Count;
			Vector2 pos = _enemySpawnPositions[idx];
			if (!strictCameraRect.HasPoint(pos) && IsFarFromAlivePlayers(pos))
			{
				_spawnPosIndex = (idx + 1) % _enemySpawnPositions.Count;
				return pos;
			}
		}

		// 4. Fallback (только если вся маленькая первая карта целиком поместилась в экран): берем самую дальнюю от центра камеры точку
		Vector2 bestPos = _enemySpawnPositions[_spawnPosIndex];
		float bestDist = -1.0f;
		Vector2 camCenter = strictCameraRect.GetCenter();
		for (int i = 0; i < _enemySpawnPositions.Count; i++)
		{
			int idx = (_spawnPosIndex + i) % _enemySpawnPositions.Count;
			Vector2 pos = _enemySpawnPositions[idx];
			float d = pos.DistanceTo(camCenter);
			if (d > bestDist)
			{
				bestDist = d;
				bestPos = pos;
				_spawnPosIndex = (idx + 1) % _enemySpawnPositions.Count;
			}
		}
		return bestPos;
	}

	public Vector2 GetWalkableFlankTarget(Vector2 playerPos, Vector2 desiredFlankPos)
	{
		if (_floorLayer == null) return playerPos;
		Vector2I cell = _floorLayer.LocalToMap(_floorLayer.ToLocal(desiredFlankPos));
		if (IsCellFree(cell))
		{
			return _floorLayer.ToGlobal(_floorLayer.MapToLocal(cell));
		}
		// Пробуем найти свободную клетку рядом с точкой обхода
		for (int r = 1; r <= 3; r++)
		{
			for (int dx = -r; dx <= r; dx++)
			{
				for (int dy = -r; dy <= r; dy++)
				{
					if (Math.Abs(dx) != r && Math.Abs(dy) != r) continue;
					Vector2I candidate = new Vector2I(cell.X + dx, cell.Y + dy);
					if (IsCellFree(candidate))
					{
						return _floorLayer.ToGlobal(_floorLayer.MapToLocal(candidate));
					}
				}
			}
		}
		return playerPos;
	}

	public Node2D GetExitLadderNode()
	{
		return GodotObject.IsInstanceValid(_exitLadder) ? _exitLadder : null;
	}

	public Node2D GetShopAltarNode()
	{
		return GodotObject.IsInstanceValid(_supplyAltar) && !_supplyAltar.HasPurchased ? _supplyAltar : null;
	}

	public void InitializeAStarGrid()
	{
		_astarGrid = null;
		if (_floorLayer == null) return;

		var usedFloor = _floorLayer.GetUsedCells();
		if (usedFloor.Count == 0) return;

		int minX = usedFloor.Min(c => c.X);
		int maxX = usedFloor.Max(c => c.X);
		int minY = usedFloor.Min(c => c.Y);
		int maxY = usedFloor.Max(c => c.Y);

		int width = maxX - minX + 1;
		int height = maxY - minY + 1;

		_astarGrid = new AStarGrid2D();
		_astarGrid.Region = new Rect2I(minX, minY, width, height);
		_astarGrid.CellSize = new Vector2(16, 16);
		_astarGrid.Offset = new Vector2(8, 8);
		_astarGrid.DiagonalMode = AStarGrid2D.DiagonalModeEnum.OnlyIfNoObstacles;
		_astarGrid.Update();

		// Клетки вне пола (пустота вокруг составных секций карты) делаем непроходимыми
		for (int x = minX; x <= maxX; x++)
		{
			for (int y = minY; y <= maxY; y++)
			{
				Vector2I c = new Vector2I(x, y);
				if (_floorLayer.GetCellSourceId(c) == -1)
				{
					_astarGrid.SetPointSolid(c, true);
				}
			}
		}

		// Заполняем стены как непроходимые препятствия
		if (_wallsVerticalLayer != null)
		{
			foreach (var cell in _wallsVerticalLayer.GetUsedCells())
			{
				if (_astarGrid.Region.HasPoint(cell))
				{
					_astarGrid.SetPointSolid(cell, true);
				}
			}
		}
		if (_wallsHorizontalLayer != null)
		{
			foreach (var cell in _wallsHorizontalLayer.GetUsedCells())
			{
				if (_astarGrid.Region.HasPoint(cell))
				{
					_astarGrid.SetPointSolid(cell, true);
				}
			}
		}
		if (_columnsLayer != null)
		{
			foreach (var cell in _columnsLayer.GetUsedCells())
			{
				if (_astarGrid.Region.HasPoint(cell))
				{
					_astarGrid.SetPointSolid(cell, true);
				}
			}
		}

		// Заполняем закрытые двери как препятствия
		foreach (Node node in GetTree().GetNodesInGroup("Door"))
		{
			if (node is Door door && !door.IsOpen)
			{
				SetCellSolid(door.GlobalPosition + new Vector2(-8, -8), true);
				SetCellSolid(door.GlobalPosition + new Vector2(8, -8), true);
				SetCellSolid(door.GlobalPosition + new Vector2(-8, 8), true);
				SetCellSolid(door.GlobalPosition + new Vector2(8, 8), true);
			}
		}
	}

	public void SetCellSolid(Vector2 globalPos, bool solid)
	{
		if (_astarGrid == null || _floorLayer == null) return;
		Vector2I cell = _floorLayer.LocalToMap(_floorLayer.ToLocal(globalPos));
		if (_astarGrid.Region.HasPoint(cell))
		{
			_astarGrid.SetPointSolid(cell, solid);
		}
	}

	public List<Vector2> GetPathToPlayer(Vector2 enemyPos, Vector2 playerPos)
	{
		if (_astarGrid == null || _floorLayer == null) return null;

		Vector2I startCell = _floorLayer.LocalToMap(_floorLayer.ToLocal(enemyPos));
		Vector2I targetCell = _floorLayer.LocalToMap(_floorLayer.ToLocal(playerPos));

		if (!_astarGrid.Region.HasPoint(startCell) || !_astarGrid.Region.HasPoint(targetCell))
		{
			return null;
		}

		// Временно делаем стартовую и конечную точку проходимыми, чтобы алгоритм не ломался при частичном пересечении со стеной
		bool wasStartSolid = _astarGrid.IsPointSolid(startCell);
		bool wasTargetSolid = _astarGrid.IsPointSolid(targetCell);

		if (wasStartSolid) _astarGrid.SetPointSolid(startCell, false);
		if (wasTargetSolid) _astarGrid.SetPointSolid(targetCell, false);

		var points = _astarGrid.GetPointPath(startCell, targetCell);

		if (wasStartSolid) _astarGrid.SetPointSolid(startCell, true);
		if (wasTargetSolid) _astarGrid.SetPointSolid(targetCell, true);

		var path = new List<Vector2>();
		foreach (var pt in points)
		{
			path.Add(_floorLayer.ToGlobal(pt));
		}
		return path;
	}

	private static OccluderPolygon2D GetOrCreateRectOccluderPolygon(
		ref OccluderPolygon2D cache, float minX, float minY, float maxX, float maxY)
	{
		return cache ??= new OccluderPolygon2D
		{
			Polygon = new Vector2[]
			{
				new Vector2(minX, minY),
				new Vector2(maxX, minY),
				new Vector2(maxX, maxY),
				new Vector2(minX, maxY)
			}
		};
	}

	private void RebuildLevelOccluders()
	{
		if (!IsInstanceValid(_occludersContainer)) return;

		foreach (Node child in _occludersContainer.GetChildren())
		{
			child.QueueFree();
		}

		// Place each occluder behind the visible trim/brick face so room lights illuminate the wall face while still blocking light leak into the void.
		var leftPoly = GetOrCreateRectOccluderPolygon(ref _leftWallOccluderPolygon, -8, -8, 3, 8);
		var rightPoly = GetOrCreateRectOccluderPolygon(ref _rightWallOccluderPolygon, -3, -8, 8, 8);
		var leftSeamPoly = GetOrCreateRectOccluderPolygon(ref _leftSeamOccluderPolygon, 2, -8, 4, 8);
		var rightSeamPoly = GetOrCreateRectOccluderPolygon(ref _rightSeamOccluderPolygon, -4, -8, -2, 8);
		var centerPoly = GetOrCreateRectOccluderPolygon(ref _centerWallOccluderPolygon, -3, -8, 3, 8);
		var topPoly = GetOrCreateRectOccluderPolygon(ref _topWallOccluderPolygon, -8, -8, 8, 3);
		var bottomPoly = GetOrCreateRectOccluderPolygon(ref _bottomWallOccluderPolygon, -8, 4, 8, 8);
		var columnPoly = GetOrCreateRectOccluderPolygon(ref _columnOccluderPolygon, -6, -6, 6, 6);

		if (_wallsHorizontalLayer != null)
		{
			foreach (var cell in _wallsHorizontalLayer.GetUsedCells())
			{
				bool isUpperBrick =
					_wallsVerticalLayer != null &&
					_floorLayer != null &&
					_wallsVerticalLayer.GetCellSourceId(cell) != -1 &&
					_wallsVerticalLayer.GetCellAtlasCoords(cell).Y == 1 &&
					_floorLayer.GetCellSourceId(cell + Vector2I.Down) != -1 &&
					_floorLayer.GetCellSourceId(cell + Vector2I.Up) == -1;

				bool hasRightRoom = _floorLayer != null && _floorLayer.GetCellSourceId(cell + Vector2I.Right) != -1;
				bool hasLeftRoom = _floorLayer != null && _floorLayer.GetCellSourceId(cell + Vector2I.Left) != -1;
				bool isBothSides = hasRightRoom && hasLeftRoom;

				if (isUpperBrick)
				{
					// Keep the front brick face lit from the room below while sealing horizontal light leaks at step corners.
					if (isBothSides)
					{
						AddTileOccluder(_wallsHorizontalLayer, cell, leftSeamPoly);
						AddTileOccluder(_wallsHorizontalLayer, cell, rightSeamPoly);
					}
					else
					{
						var seamPoly = _wallsHorizontalLayer.GetCellAlternativeTile(cell) == 1 ? leftSeamPoly : rightSeamPoly;
						AddTileOccluder(_wallsHorizontalLayer, cell, seamPoly);
					}
				}
				else if (isBothSides)
				{
					AddTileOccluder(_wallsHorizontalLayer, cell, centerPoly);
				}
				else
				{
					var poly = _wallsHorizontalLayer.GetCellAlternativeTile(cell) == 1 ? leftPoly : rightPoly;
					AddTileOccluder(_wallsHorizontalLayer, cell, poly);
				}
			}
		}

		if (_wallsVerticalLayer != null && _floorLayer != null)
		{
			foreach (var cell in _wallsVerticalLayer.GetUsedCells())
			{
				Vector2I atlas = _wallsVerticalLayer.GetCellAtlasCoords(cell);
				// Upper-wall top caps (atlasY == 0 without floor tile) and lower-wall outer rows (atlasY == 1 without adjacent south floor)
				if (atlas.Y == 0 && _floorLayer.GetCellSourceId(cell) == -1)
				{
					AddTileOccluder(_wallsVerticalLayer, cell, topPoly);
				}
				else if (atlas.Y == 1 && _floorLayer.GetCellSourceId(cell) == -1 && _floorLayer.GetCellSourceId(cell + Vector2I.Down) == -1)
				{
					AddTileOccluder(_wallsVerticalLayer, cell, bottomPoly);
				}
			}
		}

		if (_columnsLayer != null)
		{
			foreach (var cell in _columnsLayer.GetUsedCells())
			{
				if (_columnsLayer.GetCellAtlasCoords(cell) == new Vector2I(15, 6))
				{
					AddTileOccluder(_columnsLayer, cell, columnPoly);
				}
			}
		}
	}

	private void AddWallExtraSprite(Vector2I cell, Rect2 regionRect, bool flipH, Vector2 localOffset)
	{
		if (_wallExtrasContainer == null || _wallsHorizontalLayer == null || _dungeonTilesetTexture == null) return;

		var sprite = new Sprite2D
		{
			Texture = _dungeonTilesetTexture,
			RegionEnabled = true,
			RegionRect = regionRect,
			FlipH = flipH,
			Position = _wallsHorizontalLayer.Position + _wallsHorizontalLayer.MapToLocal(cell) + localOffset
		};
		_wallExtrasContainer.AddChild(sprite);
	}

	private void AddTileOccluder(TileMapLayer layer, Vector2I cell, OccluderPolygon2D polygon)
	{
		var occluder = new LightOccluder2D
		{
			Occluder = polygon,
			Position = layer.Position + layer.MapToLocal(cell)
		};
		_occludersContainer.AddChild(occluder);
	}

	private bool TryGetHitSpikeCenter(Vector2 actorGlobalPos, float spikeRadius, out Vector2 hitCenter)
	{
		hitCenter = Vector2.Zero;
		if (_spikesLayer == null) return false;

		Vector2I baseCell = _spikesLayer.LocalToMap(_spikesLayer.ToLocal(actorGlobalPos));
		for (int dx = -1; dx <= 1; dx++)
		{
			for (int dy = -1; dy <= 1; dy++)
			{
				Vector2I candidate = new Vector2I(baseCell.X + dx, baseCell.Y + dy);
				if (_spikesLayer.GetCellSourceId(candidate) == -1) continue;

				Vector2 cellCenter = _spikesLayer.ToGlobal(_spikesLayer.MapToLocal(candidate));
				if (actorGlobalPos.DistanceTo(cellCenter) < spikeRadius)
				{
					hitCenter = cellCenter;
					return true;
				}
			}
		}
		return false;
	}

	public override void _PhysicsProcess(double delta)
	{
		LevelPlayedTime += (float)delta;
		// 1. Обработка музыки в зависимости от положения игрока
		if (!IsInstanceValid(_cachedWaveManager))
		{
			_cachedWaveManager = GetTree().GetFirstNodeInGroup("WaveManager") as WaveManager;
		}
		bool waveActive = _cachedWaveManager != null && _cachedWaveManager.WaveInProgress;
		if (!waveActive)
		{
			MusicManager.Instance?.PlayMusic("res://assets/sounds/doom_10. Ties That Bind.mp3");
		}

		if (_spikesLayer == null) return;

		if (_playerSpikeCooldowns.Count > 0)
		{
			_expiredPlayerSpikePeers.Clear();
			_expiredPlayerSpikePeers.AddRange(_playerSpikeCooldowns.Keys);
			foreach (long peerId in _expiredPlayerSpikePeers)
			{
				double remaining = _playerSpikeCooldowns[peerId] - delta;
				if (remaining <= 0) _playerSpikeCooldowns.Remove(peerId);
				else _playerSpikeCooldowns[peerId] = remaining;
			}
		}

		if (_enemySpikeCooldowns.Count > 0)
		{
			_expiredSpikeIds.Clear();
			_expiredSpikeIds.AddRange(_enemySpikeCooldowns.Keys);
			foreach (ulong id in _expiredSpikeIds)
			{
				double remaining = _enemySpikeCooldowns[id] - delta;
				if (remaining <= 0) _enemySpikeCooldowns.Remove(id);
				else _enemySpikeCooldowns[id] = remaining;
			}
		}

		_spikesTimer += delta;
		double cycleTime = _spikesTimer % 6.0; // 6.0 seconds total cycle

		SpikePhase newPhase;
		if (cycleTime < 4.0) newPhase = SpikePhase.Retracted;
		else if (cycleTime < 4.2) newPhase = SpikePhase.Warning;
		else if (cycleTime < 5.8) newPhase = SpikePhase.Active;
		else newPhase = SpikePhase.Retracting;

		if (newPhase != _currentSpikePhase)
		{
			_currentSpikePhase = newPhase;
			UpdateSpikeTiles();
		}

		// Урон наносим только во время активной фазы (и игроку, и наземным врагам)
		if (IsServerAuthority && _currentSpikePhase == SpikePhase.Active)
		{
			float spikeRadius = 12.0f;

			foreach (var p in NetworkManager.GetAlivePlayers(GetTree()))
			{
				if (_playerSpikeCooldowns.ContainsKey(p.PeerId)) continue;
				if (TryGetHitSpikeCenter(p.GlobalPosition, spikeRadius, out Vector2 playerSpikeCenter))
				{
					p.TakeDamage(5f, playerSpikeCenter, 2.5f, true);
					_playerSpikeCooldowns[p.PeerId] = 1.0;
				}
			}

			foreach (Node node in GetTree().GetNodesInGroup("Enemies"))
			{
				if (node is Enemy enemy && !(enemy is Ghost) && enemy.CurrentHealth > 0)
				{
					ulong id = enemy.GetInstanceId();
					if (_enemySpikeCooldowns.ContainsKey(id)) continue;

					if (TryGetHitSpikeCenter(enemy.GlobalPosition, spikeRadius, out Vector2 enemySpikeCenter))
					{
						enemy.TakeDamage(8f, enemySpikeCenter, 2.0f, true);
						_enemySpikeCooldowns[id] = 1.0;
					}
				}
			}
		}

		if (IsCoopSession && IsServerAuthority)
		{
			SyncEnemiesBatchOnServer((float)delta);
		}
	}

	private void SyncEnemiesBatchOnServer(float dt)
	{
		_enemySyncTimer -= dt;
		if (_enemySyncTimer > 0.0f) return;
		_enemySyncTimer = 0.05f;

		var enemies = GetTree().GetNodesInGroup("Enemies");
		var netIds = new List<int>();
		var positions = new List<Vector2>();
		var hps = new List<float>();
		var anims = new List<string>();
		var flags = new List<int>();

		foreach (Node node in enemies)
		{
			if (node is Enemy enemy && GodotObject.IsInstanceValid(enemy) && enemy.NetId > 0 && !enemy.HasDied && enemy.CurrentHealth > 0.0f)
			{
				netIds.Add(enemy.NetId);
				positions.Add(enemy.GlobalPosition);
				hps.Add(enemy.CurrentHealth);
				anims.Add(enemy.GetCurrentAnimationName());
				int f = (enemy.GetSpriteFlipH() ? 1 : 0) | (enemy.IsWarningVisible() ? 2 : 0);
				flags.Add(f);
			}
		}

		if (netIds.Count > 0)
		{
			Rpc(nameof(RpcSyncEnemiesBatch), netIds.ToArray(), positions.ToArray(), hps.ToArray(), anims.ToArray(), flags.ToArray());
		}
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered)]
	private void RpcSyncEnemiesBatch(int[] netIds, Vector2[] positions, float[] hps, string[] anims, int[] flags)
	{
		if (!IsCoopSession || IsServerAuthority || Multiplayer.GetRemoteSenderId() != 1) return;
		int count = netIds.Length;
		for (int i = 0; i < count; i++)
		{
			var enemy = GetNodeOrNull<Enemy>($"Enemy_{netIds[i]}");
			if (enemy != null && GodotObject.IsInstanceValid(enemy) && !enemy.HasDied)
			{
				enemy.ApplyNetworkSnapshot(
					positions[i],
					hps[i],
					anims[i],
					(flags[i] & 1) != 0,
					(flags[i] & 2) != 0);
			}
		}
	}

	private void UpdateSpikeTiles()
	{
		if (_spikesLayer == null) return;
		Vector2I tileCoords;
		switch (_currentSpikePhase)
		{
			case SpikePhase.Retracted:
				tileCoords = new Vector2I(0, 3);
				_spikesLayer.Modulate = Colors.White;
				break;
			case SpikePhase.Warning:
				tileCoords = new Vector2I(1, 3);
				_spikesLayer.Modulate = new Color(1.38f, 0.78f, 0.42f, 1.0f);
				break;
			case SpikePhase.Active:
				tileCoords = new Vector2I(2, 3);
				_spikesLayer.Modulate = new Color(1.18f, 0.88f, 0.88f, 1.0f);
				break;
			case SpikePhase.Retracting:
				tileCoords = new Vector2I(3, 3);
				_spikesLayer.Modulate = Colors.White;
				break;
			default:
				tileCoords = new Vector2I(0, 3);
				_spikesLayer.Modulate = Colors.White;
				break;
		}

		var cells = _spikesLayer.GetUsedCells();
		foreach (var cell in cells)
		{
			_spikesLayer.SetCell(cell, 0, tileCoords);
		}
	}

	private void GenerateSpikes()
	{
		if (_spikesLayer == null || _floorLayer == null) return;
		_spikesLayer.Clear();

		var floorCells = _floorLayer.GetUsedCells();
		var eligibleCells = new List<Vector2I>(floorCells.Count);
		foreach (var cell in floorCells)
		{
			// Exclude shop room
			if (cell.Y <= -_halfH - 2) continue;

			// Не спавним шипы в центре (где игрок появляется)
			if (Math.Abs(cell.X) < 3 && Math.Abs(cell.Y) < 3) continue;

			// Не спавним на стенах
			if (_wallsVerticalLayer != null && _wallsVerticalLayer.GetCellSourceId(cell) != -1) continue;
			if (_wallsHorizontalLayer != null && _wallsHorizontalLayer.GetCellSourceId(cell) != -1) continue;
			if (_columnsLayer != null && _columnsLayer.GetCellSourceId(cell) != -1) continue;

			// Не спавним на лестнице
			if (_stairsLayer != null && _stairsLayer.GetCellSourceId(cell) != -1) continue;

			eligibleCells.Add(cell);
		}

		if (eligibleCells.Count == 0) return;

		float targetRatio = GameLogic.CalculateSpikeFloorRatio(_currentLevelIndex, CurrentFloorEvent);
		int targetCount = Mathf.Clamp(Mathf.RoundToInt(eligibleCells.Count * targetRatio), 0, eligibleCells.Count);
		if (CurrentFloorEvent != GameLogic.FloorEventType.SpikeChamber)
		{
			int maxNormalCount = Mathf.FloorToInt(eligibleCells.Count * GameLogic.MaxNormalSpikeFloorRatio);
			targetCount = Mathf.Min(targetCount, maxNormalCount);
		}

		for (int i = 0; i < targetCount; i++)
		{
			int swapIdx = _rng.RandiRange(i, eligibleCells.Count - 1);
			(eligibleCells[i], eligibleCells[swapIdx]) = (eligibleCells[swapIdx], eligibleCells[i]);
			_spikesLayer.SetCell(eligibleCells[i], 0, new Vector2I(0, 3));
		}
	}

	public void SpawnShopItems()
	{
		var door = GetNodeOrNull<Door>("Map/Door");
		if (door == null) return;

		var pickupPrefab = GD.Load<PackedScene>("res://scene/weapons/weapon_pickup.tscn");
		if (pickupPrefab == null) return;

		string[] weaponPaths = new string[] {
			"res://data/waepons/axe.tres",
			"res://data/waepons/dagger.tres",
			"res://data/waepons/revolver.tres",
			"res://data/waepons/shotgun.tres"
		};

		Vector2[] offsets = new Vector2[] {
			new Vector2(-24, -48),
			new Vector2(0, -48),
			new Vector2(24, -48)
		};

		var random = new Random();
		for (int i = 0; i < offsets.Length; i++)
		{
			var pickup = pickupPrefab.Instantiate() as WeaponPickup;
			if (pickup != null)
			{
				string path = weaponPaths[random.Next(weaponPaths.Length)];
				var weaponData = GameDataRegistry.LoadFreshWeapon(path) ?? GD.Load<WeaponData>(path);
				pickup.WeaponToGive = weaponData;
				pickup.GlobalPosition = door.GlobalPosition + offsets[i];
				AddChild(pickup);
			}
		}
	}

	public void ServerSpawnPickupDrop(Vector2 pos, bool isMana)
	{
		if (!IsServerAuthority) return;
		Vector2 freePos = FindNearestFreeSpot(pos);
		int netId = IsCoopSession && NetworkManager.Instance != null
			? NetworkManager.Instance.AllocateEntityNetId()
			: 0;
		if (IsCoopSession)
		{
			Rpc(nameof(RpcSpawnPickupItem), netId, freePos, isMana);
		}
		else
		{
			RpcSpawnPickupItem(netId, freePos, isMana);
		}
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RpcSpawnPickupItem(int netId, Vector2 pos, bool isMana)
	{
		int sender = Multiplayer.GetRemoteSenderId();
		if (IsCoopSession && sender != 0 && sender != 1) return;

		string path = isMana ? "res://scene/objects/mana_crystal.tscn" : "res://scene/objects/coin.tscn";
		var prefab = GD.Load<PackedScene>(path);
		if (prefab == null) return;

		var drop = prefab.Instantiate() as Node2D;
		if (drop == null) return;
		if (netId > 0)
		{
			drop.Name = $"Pickup_{netId}";
		}
		drop.GlobalPosition = pos;
		CallDeferred(MethodName.AddChild, drop);
	}

	public void SpawnWeaponPickupAt(Vector2 pos, string weaponPath)
	{
		if (IsCoopSession && !IsServerAuthority) return;
		Vector2 freePos = FindNearestFreeSpot(pos);
		if (IsCoopSession && NetworkManager.Instance != null)
		{
			int netId = NetworkManager.Instance.AllocateEntityNetId();
			Rpc(nameof(RpcSpawnWeaponPickup), netId, freePos, weaponPath);
		}
		else
		{
			SpawnWeaponPickupLocal(0, freePos, weaponPath);
		}
	}

	public void RequestSpawnWeaponPickup(Vector2 pos, string weaponPath)
	{
		if (string.IsNullOrEmpty(weaponPath)) return;
		if (IsCoopSession && !IsServerAuthority)
		{
			RpcId(1, nameof(RpcRequestSpawnWeaponPickup), pos, weaponPath);
			return;
		}
		SpawnWeaponPickupAt(pos, weaponPath);
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RpcRequestSpawnWeaponPickup(Vector2 pos, string weaponPath)
	{
		if (!IsServerAuthority || string.IsNullOrEmpty(weaponPath)) return;
		SpawnWeaponPickupAt(pos, weaponPath);
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RpcSpawnWeaponPickup(int netId, Vector2 pos, string weaponPath)
	{
		int sender = Multiplayer.GetRemoteSenderId();
		if (IsCoopSession && sender != 0 && sender != 1) return;
		SpawnWeaponPickupLocal(netId, pos, weaponPath);
	}

	private void SpawnWeaponPickupLocal(int netId, Vector2 pos, string weaponPath)
	{
		var pickupPrefab = GD.Load<PackedScene>("res://scene/weapons/weapon_pickup.tscn");
		if (pickupPrefab == null) return;

		var pickup = pickupPrefab.Instantiate() as WeaponPickup;
		if (pickup != null)
		{
			if (netId > 0)
			{
				pickup.Name = $"WeaponDrop_{netId}";
			}
			var weaponData = GameDataRegistry.LoadFreshWeapon(weaponPath) ?? GD.Load<WeaponData>(weaponPath);
			pickup.WeaponToGive = weaponData;
			pickup.GlobalPosition = pos;
			CallDeferred(MethodName.AddChild, pickup);
		}
	}

	// Сдвигает позицию, если она оказалась внутри стены/колонны (враг мог умереть, отброшенный в препятствие)
	private Vector2 FindNearestFreeSpot(Vector2 pos)
	{
		if (_floorLayer == null) return pos;

		Vector2I cell = _floorLayer.LocalToMap(_floorLayer.ToLocal(pos));
		if (IsCellFree(cell)) return pos;

		for (int radius = 1; radius <= 4; radius++)
		{
			for (int dx = -radius; dx <= radius; dx++)
			{
				for (int dy = -radius; dy <= radius; dy++)
				{
					if (Math.Abs(dx) != radius && Math.Abs(dy) != radius) continue;
					Vector2I candidate = new Vector2I(cell.X + dx, cell.Y + dy);
					if (IsCellFree(candidate))
					{
						return _floorLayer.ToGlobal(_floorLayer.MapToLocal(candidate));
					}
				}
			}
		}
		return pos;
	}

	private bool IsCellFree(Vector2I cell)
	{
		if (_floorLayer.GetCellSourceId(cell) == -1) return false;
		if (_wallsVerticalLayer != null && _wallsVerticalLayer.GetCellSourceId(cell) != -1) return false;
		if (_wallsHorizontalLayer != null && _wallsHorizontalLayer.GetCellSourceId(cell) != -1) return false;
		if (_columnsLayer != null && _columnsLayer.GetCellSourceId(cell) != -1) return false;
		return true;
	}

	public override void _Input(InputEvent @event)
	{
		if (@event.IsActionPressed("ui_cancel"))
		{
			if (IsCoopSession)
			{
				if (GetTree().Root.GetNodeOrNull("CoopPauseMenu") == null)
				{
					var pauseMenu = new PauseMenu { Name = "CoopPauseMenu" };
					GetTree().Root.AddChild(pauseMenu);
					GetViewport().SetInputAsHandled();
				}
				return;
			}

			if (!GetTree().Paused)
			{
				GetTree().Paused = true;
				var pauseMenu = new PauseMenu();
				GetTree().Root.AddChild(pauseMenu);
				GetViewport().SetInputAsHandled();
			}
		}
	}

	private static readonly Vector2[] CoopSpawnOffsets = new[]
	{
		Vector2.Zero,
		new Vector2(22.0f, 0.0f),
		new Vector2(-22.0f, 0.0f),
		new Vector2(0.0f, 22.0f)
	};

	public void ServerInitializeCoopFloor(int floorIndex, ulong floorSeed)
	{
		if (!IsCoopSession || !IsServerAuthority) return;

		Rpc(nameof(RpcApplyCoopFloor), floorIndex, floorSeed);

		if (!_coopPlayersSpawned)
		{
			var ordered = NetworkManager.Instance.GetOrderedLobbyPlayers();
			int count = ordered.Count;
			long[] peerIds = new long[count];
			string[] names = new string[count];
			string[] chars = new string[count];
			Vector2[] positions = new Vector2[count];

			for (int i = 0; i < count; i++)
			{
				peerIds[i] = ordered[i].PeerId;
				names[i] = ordered[i].PlayerName;
				chars[i] = ordered[i].CharacterName;
				positions[i] = FindNearestFreeSpot(CoopSpawnOffsets[i % CoopSpawnOffsets.Length]);
			}

			Rpc(nameof(RpcSpawnCoopPlayers), peerIds, names, chars, positions);
		}
		else
		{
			var players = NetworkManager.GetAllPlayers(GetTree());
			int count = players.Count;
			long[] peerIds = new long[count];
			Vector2[] positions = new Vector2[count];
			float[] nextHps = new float[count];
			float[] nextMps = new float[count];
			int[] revivedFlags = new int[count];

			for (int i = 0; i < count; i++)
			{
				Player p = players[i];
				peerIds[i] = p.PeerId;
				positions[i] = FindNearestFreeSpot(CoopSpawnOffsets[i % CoopSpawnOffsets.Length]);
				if (p.IsDead)
				{
					nextHps[i] = GameLogic.CalculateCoopReviveHealth(p.MaxHealthEffective);
					nextMps[i] = p.MaxManaEffective;
					revivedFlags[i] = 1;
				}
				else
				{
					nextHps[i] = p.CurrentHealth;
					nextMps[i] = p.CurrentMana;
					revivedFlags[i] = 0;
				}
			}

			Rpc(nameof(RpcPlaceAndReviveCoopPlayers), peerIds, positions, nextHps, nextMps, revivedFlags);
		}

		Rpc(nameof(RpcStartFloorCountdown), floorIndex, 5.0f, BuildWaveAnnouncementMessage(floorIndex));
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RpcApplyCoopFloor(int floorIndex, ulong floorSeed)
	{
		ClearArenaProps();
		if (GodotObject.IsInstanceValid(_exitLadder))
		{
			_exitLadder.GetParent()?.RemoveChild(_exitLadder);
			_exitLadder.QueueFree();
			_exitLadder = null;
		}
		if (GodotObject.IsInstanceValid(_supplyAltar))
		{
			_supplyAltar.GetParent()?.RemoveChild(_supplyAltar);
			_supplyAltar.QueueFree();
			_supplyAltar = null;
		}
		if (floorIndex > 1 && _stairsLayer != null)
		{
			_stairsLayer.Visible = false;
		}

		_currentLevelIndex = floorIndex;
		if (NetworkManager.Instance != null)
		{
			NetworkManager.Instance.CurrentRunFloorIndex = floorIndex;
			NetworkManager.Instance.CurrentFloorSeed = floorSeed;
		}

		_rng.Seed = floorSeed;
		_spikesTimer = 0.0;
		_currentSpikePhase = SpikePhase.Retracted;
		RollFloorEventOnly();

		if (floorIndex == 1)
		{
			InitializeSpawnPositions();
			GenerateSpikes();
		}
		else
		{
			GenerateRandomLevel();
		}

		ApplyFloorEventVisuals();
		SpawnArenaProps();
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RpcSpawnCoopPlayers(long[] peerIds, string[] names, string[] chars, Vector2[] spawnPositions)
	{
		_coopPlayersSpawned = true;
		var playerPrefab = GD.Load<PackedScene>("res://scene/actors/player.tscn");
		int count = peerIds?.Length ?? 0;

		for (int i = 0; i < count; i++)
		{
			string expectedName = $"Player_{peerIds[i]}";
			if (GetNodeOrNull<Player>(expectedName) != null) continue;

			var p = playerPrefab.Instantiate() as Player;
			if (p == null) continue;

			p.Name = expectedName;
			p.PeerId = (int)peerIds[i];
			p.PlayerDisplayName = names[i];
			p.IsCoopSpawned = true;

			string charPath = GameLogic.ResolveCharacterDataPath(chars[i]);
			var loadedConfig = GameDataRegistry.LoadFreshPlayer(charPath) ?? GD.Load<PlayerData>(charPath);
			if (loadedConfig != null) p.Config = loadedConfig;

			p.GlobalPosition = spawnPositions[i];
			AddChild(p);

			if (p.IsLocalPlayer)
			{
				_player = p;
				AttachDustMotesToPlayer(p);
				var hud = GetTree().GetFirstNodeInGroup("HUD") as HUD;
				hud?.BindLocalPlayer(p);
			}
		}

		SpawnEntryLadder(Vector2.Zero);
		(GetTree().GetFirstNodeInGroup("HUD") as HUD)?.UpdatePartyRosterUI();
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RpcPlaceAndReviveCoopPlayers(long[] peerIds, Vector2[] spawnPositions, float[] nextHps, float[] nextMps, int[] revivedFlags)
	{
		int count = peerIds?.Length ?? 0;
		for (int i = 0; i < count; i++)
		{
			Player p = NetworkManager.GetPlayerByPeerId(GetTree(), peerIds[i]);
			if (p == null) continue;

			if (revivedFlags[i] != 0)
			{
				p.ApplyFloorRevive(spawnPositions[i], nextHps[i], nextMps[i]);
			}
			else
			{
				p.TeleportForNewFloor(spawnPositions[i], nextHps[i], nextMps[i]);
			}
		}

		SpawnEntryLadder(Vector2.Zero);
		(GetTree().GetFirstNodeInGroup("HUD") as HUD)?.UpdatePartyRosterUI();
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RpcStartFloorCountdown(int floorIndex, float duration, string msg)
	{
		var hud = GetTree().GetFirstNodeInGroup("HUD") as HUD;
		hud?.SetLadderReadyBanner(false, 0, 1, 0.0f);
		hud?.StartCountdown(duration, msg);
	}

	public void OnCoopPeerDisconnected(long peerId, string leavingName)
	{
		Player leaving = NetworkManager.GetPlayerByPeerId(GetTree(), peerId);
		if (leaving != null)
		{
			leaving.RemoveFromGroup("Player");
			leaving.GetParent()?.RemoveChild(leaving);
			leaving.QueueFree();
		}

		var hud = GetTree().GetFirstNodeInGroup("HUD") as HUD;
		hud?.ShowToast($"{leavingName} left the run", new Color(1.0f, 0.55f, 0.25f));
		hud?.UpdatePartyRosterUI();

		var remaining = NetworkManager.GetAllPlayers(GetTree());
		if (IsServerAuthority && remaining.Count > 0)
		{
			remaining[0].CheckTeamWipeOnServer();
		}
		if (IsServerAuthority)
		{
			GetNodeOrNull<ExitLadder>("ExitLadder")?.RefreshReadyCheckOnServer();
		}
	}
}
