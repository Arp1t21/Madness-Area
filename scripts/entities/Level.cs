using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class Level : Node2D
{
	private Player _player;
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
	private readonly Dictionary<ulong, double> _enemySpikeCooldowns = new Dictionary<ulong, double>();
	private SpikePhase _currentSpikePhase = SpikePhase.Retracted;

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

		if (SaveManager.CurrentSave != null) {
			_currentLevelIndex = SaveManager.CurrentSave.CurrentLevelIndex;
		}

		var mapNode = GetNodeOrNull<Node2D>("Map");
		if (mapNode != null) {
			mapNode.Scale = new Vector2(3.0f, 3.0f);
		}

		// 1. Создаем CanvasModulate для общего затемнения сцены (черный void за картой)
		var modulate = new CanvasModulate();
		modulate.Name = "CanvasModulate";
		float ambient = Mathf.Clamp((SettingsManager.Brightness - 1.0f) * 0.5f, 0.0f, 0.5f);
		modulate.Color = new Color(ambient, ambient, ambient, 1.0f);
		AddChild(modulate);

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

		// Удаляем призрачные тайлы с несуществующим source_id в слоях стен (в map.tscn на слое WallHorizontal остались тайлы пола от копирования)
		foreach (var layer in new[] { _wallsVerticalLayer, _wallsHorizontalLayer, _columnsLayer })
		{
			if (layer == null) continue;
			foreach (var cell in layer.GetUsedCells())
			{
				if (layer.GetCellTileData(cell) == null)
				{
					layer.EraseCell(cell);
				}
			}
		}
		
		_spikesLayer = GetNodeOrNull<TileMapLayer>("Map/Spikes");

		if (_floorLayer != null) {
			var usedCells = _floorLayer.GetUsedCells();
			foreach (var cell in usedCells) {
				var tileData = _floorLayer.GetCellTileData(cell);
				// Если у тайла террейн равен 1 (террейн "wall" в floor.tres), создаем для него окклюдер
				if (tileData != null && tileData.Terrain == 1) {
					var occluder = new LightOccluder2D();
					var polygon = new OccluderPolygon2D();
					
					polygon.Polygon = new Vector2[] {
						new Vector2(-8, -8),
						new Vector2(8, -8),
						new Vector2(8, 8),
						new Vector2(-8, 8)
					};
					
					occluder.Occluder = polygon;
					occluder.Position = _floorLayer.MapToLocal(cell);
					_floorLayer.AddChild(occluder);
				}
			}
		}

		// 3. Находим игрока и слой лестницы
		_player = GetTree().GetFirstNodeInGroup("Player") as Player;
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
		if (SaveManager.CurrentSave != null && _currentLevelIndex > 1)
		{
			_rng.Seed = SaveManager.CurrentSave.LevelSeed;
			GenerateRandomLevel(); // сам переместит игрока в центр и пересоберет все слои
		}

		// 4. Спавним входную лестницу под игроком на старте
		if (_player != null) {
			SpawnEntryLadder(_player.GlobalPosition);
		}

		// 5. Инициализируем спавн-позиции мобов для первого уровня
		//(для сгенерированных уровней это уже сделано внутри GenerateRandomLevel)
		if (_currentLevelIndex == 1)
		{
			InitializeSpawnPositions();
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
		if (_currentLevelIndex == 1)
		{
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
		}
	}

	private void SpawnEntryLadder(Vector2 position)
	{
		_entryLadder = new Sprite2D();
		_entryLadder.Name = "EntryLadder";
		_entryLadder.Texture = GD.Load<Texture2D>("res://assets/objects/0x72_DungeonTilesetII_v1.7.png");
		_entryLadder.RegionEnabled = true;
		_entryLadder.RegionRect = new Rect2(144, 80, 16, 16);
		_entryLadder.Scale = new Vector2(1.2f, 1.2f);
		_entryLadder.GlobalPosition = position;
		_entryLadder.ZIndex = -1;
		AddChild(_entryLadder);
	}

	public void OnWaveCleared()
	{
		// 1. Входная лестница исчезает
		if (GodotObject.IsInstanceValid(_entryLadder)) {
			_entryLadder.QueueFree();
			_entryLadder = null;
		}

		// 2. Делаем выходную лестницу видимой и спавним триггер взаимодействия
		if (_stairsLayer != null) {
			_stairsLayer.Visible = true;
		}

		if (_hasStairs) {
			SpawnExitLadderTrigger();
		}

		// 3. Спавним Алтарь снабжения рядом с лестницей/игроком
		SpawnSupplyAltar();
	}

	private void SpawnSupplyAltar()
	{
		if (GodotObject.IsInstanceValid(_supplyAltar))
		{
			_supplyAltar.QueueFree();
			_supplyAltar = null;
		}

		Vector2 basePos = _hasStairs ? _stairsPosition : (_player != null ? _player.GlobalPosition : Vector2.Zero);
		Vector2 altarPos = basePos + new Vector2(0, -48);
		if (_floorLayer != null)
		{
			Vector2I baseCell = _floorLayer.LocalToMap(_floorLayer.ToLocal(basePos));
			Vector2I[] offsets = new[]
			{
				new Vector2I(0, -2), new Vector2I(2, 0), new Vector2I(-2, 0), new Vector2I(0, 2),
				new Vector2I(1, -1), new Vector2I(-1, -1), new Vector2I(1, 1), new Vector2I(-1, 1)
			};
			foreach (var off in offsets)
			{
				Vector2I candidate = baseCell + off;
				if (_floorLayer.GetCellSourceId(candidate) == -1) continue;
				if (_wallsVerticalLayer != null && _wallsVerticalLayer.GetCellSourceId(candidate) != -1) continue;
				if (_wallsHorizontalLayer != null && _wallsHorizontalLayer.GetCellSourceId(candidate) != -1) continue;
				if (_columnsLayer != null && _columnsLayer.GetCellSourceId(candidate) != -1) continue;
				altarPos = _floorLayer.ToGlobal(_floorLayer.MapToLocal(candidate));
				break;
			}
		}

		_supplyAltar = new SupplyAltar();
		_supplyAltar.GlobalPosition = altarPos;
		CallDeferred("add_child", _supplyAltar);
	}

	private void SpawnExitLadderTrigger()
	{
		_exitLadder = new ExitLadder();
		_exitLadder.Setup(this);
		_exitLadder.GlobalPosition = _stairsPosition;
		CallDeferred("add_child", _exitLadder);
	}

	public void DescendStairs()
	{
		// 1. Удаляем триггер взаимодействия и алтарь
		if (GodotObject.IsInstanceValid(_exitLadder)) {
			_exitLadder.QueueFree();
			_exitLadder = null;
		}
		if (GodotObject.IsInstanceValid(_supplyAltar)) {
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
		if (SaveManager.CurrentSave != null)
		{
			SaveManager.CurrentSave.LevelSeed = _rng.Seed;
		}
		GenerateRandomLevel();

		// 4. Спавним входную лестницу под игроком
		if (_player != null) {
			SpawnEntryLadder(_player.GlobalPosition);
		}

		// 5. Запускаем следующую волну через countdown
		var waveManager = GetTree().GetFirstNodeInGroup("WaveManager") as WaveManager;
		var hud = GetTree().GetFirstNodeInGroup("HUD") as HUD;
		if (hud != null) {
			int nextWaveNum = _currentLevelIndex;
			string msg = (nextWaveNum % 5 == 0)
				? $"BOSS FLOOR {nextWaveNum} IS COMING!"
				: $"Wave {nextWaveNum} is coming!";
			hud.StartCountdown(5.0f, msg);
		}
		else if (waveManager != null) {
			waveManager.StartNextWave();
		}
	}

	private void GenerateRandomLevel()
	{
		GD.Print($"Generating Random Level {_currentLevelIndex}...");

		// 1. Очистка старых объектов с предыдущего уровня
		foreach (var child in GetChildren())
		{
			if (child is WeaponPickup || child is Coin || child is ManaCrystal || child is SupplyAltar)
			{
				child.QueueFree();
			}
		}

		// 2. Выбираем случайные размеры арены (на 15% меньше прежних 22-35)
		int width = _rng.RandiRange(19, 30);
		int height = _rng.RandiRange(19, 30);

		_halfW = width / 2;
		_halfH = height / 2;
		int halfW = _halfW;
		int halfH = _halfH;

		// 3. Очищаем тайлы
		_floorLayer.Clear();
		if (_wallsVerticalLayer != null) _wallsVerticalLayer.Clear();
		if (_wallsHorizontalLayer != null) _wallsHorizontalLayer.Clear();
		if (_columnsLayer != null) _columnsLayer.Clear();
		if (_stairsLayer != null) _stairsLayer.Clear();
		if (_spikesLayer != null) _spikesLayer.Clear();

		var door = GetNodeOrNull<Door>("Map/Door");
		if (door != null)
		{
			door.HideDoor();
		}

		// 4. Заполняем пол
		for (int x = -halfW; x <= halfW; x++)
		{
			for (int y = -halfH; y <= halfH; y++)
			{
				_floorLayer.SetCell(new Vector2I(x, y), 1, GetRandomFloorTile());
			}
		}

		// 5. Рисуем внешние стены
		// Горизонтальные границы (top and bottom walls) on _wallsVerticalLayer (uses floor.tres front walls)
		if (_wallsVerticalLayer != null)
		{
			Vector2I vTile = new Vector2I(2, 1); // Source ID 0 in floor.tres
			// Углы НЕ рисуем здесь — их занимает боковая стена (WallHorizontal)
			for (int x = -halfW; x <= halfW; x++)
			{
				_wallsVerticalLayer.SetCell(new Vector2I(x, -halfH - 1), 0, vTile);
				_wallsVerticalLayer.SetCell(new Vector2I(x, halfH + 1), 0, vTile);
			}
		}

		// Вертикальные границы (left and right walls) on _wallsHorizontalLayer (uses TileSet_tmxlf side walls)
		if (_wallsHorizontalLayer != null)
		{
			Vector2I hTile = new Vector2I(2, 10); // Source ID 2
			// Включая углы (y от -halfH-1 до halfH+1), чтобы углы были боковыми стенами, а не квадратами
			for (int y = -halfH - 1; y <= halfH + 1; y++)
			{
				// Левая стена — отзеркаленный тайл (alternative 1, flip_h), чтобы не была "обрезанной"
				_wallsHorizontalLayer.SetCell(new Vector2I(-halfW - 1, y), 2, hTile, 1);
				_wallsHorizontalLayer.SetCell(new Vector2I(halfW + 1, y), 2, hTile);
			}
		}

		// 6. Рисуем внутренние колонны/препятствия
		if (_columnsLayer != null)
		{
			int numPillars = _rng.RandiRange(2, 4);
			var pillarPositions = new System.Collections.Generic.List<Vector2I>();
			for (int i = 0; i < numPillars; i++)
			{
				int px = 0;
				int py = 0;
				bool valid = false;
				int attempts = 0;
				while (!valid && attempts < 100)
				{
					attempts++;
					px = _rng.RandiRange(-halfW + 3, halfW - 3);
					py = _rng.RandiRange(-halfH + 3, halfH - 5);

					// Не спавним колонны у центра (0,0), где появляется игрок
					if (Math.Abs(px) < 5 && Math.Abs(py) < 6) continue;

					// Проверяем расстояние до других колонн
					bool tooClose = false;
					foreach (var pos in pillarPositions)
					{
						if (Math.Abs(pos.X - px) < 4 && Math.Abs(pos.Y - py) < 5)
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

		// 7. Размещаем выходную лестницу на случайном свободном тайле
		if (_stairsLayer != null)
		{
			int sx = 0, sy = 0;
			bool validPosition = false;

			while (!validPosition)
			{
				sx = _rng.RandiRange(-halfW + 4, halfW - 4);
				sy = _rng.RandiRange(-halfH + 4, halfH - 4);

				// Лестница должна быть далеко от игрока
				if (Math.Abs(sx) > 6 || Math.Abs(sy) > 6)
				{
					// И не пересекаться со стенами
					bool hitWall = false;
					if (_wallsVerticalLayer != null && _wallsVerticalLayer.GetCellSourceId(new Vector2I(sx, sy)) != -1) hitWall = true;
					if (_wallsHorizontalLayer != null && _wallsHorizontalLayer.GetCellSourceId(new Vector2I(sx, sy)) != -1) hitWall = true;
					if (_columnsLayer != null && _columnsLayer.GetCellSourceId(new Vector2I(sx, sy)) != -1) hitWall = true;

					if (!hitWall)
					{
						validPosition = true;
					}
				}
			}

			_stairsLayer.SetCell(new Vector2I(sx, sy), 1, new Vector2I(0, 5));
			_stairsPosition = _stairsLayer.ToGlobal(_stairsLayer.MapToLocal(new Vector2I(sx, sy)));
			_hasStairs = true;
			_stairsLayer.Visible = false;
		}

		// 8. Удаляем ВСЕ старые LightOccluder2D (в т.ч. на полу) — иначе невидимые
		// окклюдеры с прошлых уровней продолжают блокировать свет на новых местах
		if (_floorLayer != null)
		{
			foreach (var child in _floorLayer.GetChildren())
			{
				if (child is LightOccluder2D) child.QueueFree();
			}
		}
		if (_wallsVerticalLayer != null)
		{
			foreach (var child in _wallsVerticalLayer.GetChildren())
			{
				if (child is LightOccluder2D) child.QueueFree();
			}
		}
		if (_wallsHorizontalLayer != null)
		{
			foreach (var child in _wallsHorizontalLayer.GetChildren())
			{
				if (child is LightOccluder2D) child.QueueFree();
			}
		}
		if (_columnsLayer != null)
		{
			foreach (var child in _columnsLayer.GetChildren())
			{
				if (child is LightOccluder2D) child.QueueFree();
			}
		}

		// 9. Перемещаем игрока в центр Vector2.Zero
		if (_player != null)
		{
			_player.GlobalPosition = Vector2.Zero;
		}

		// 10. Обновляем доступные точки спавна врагов
		InitializeSpawnPositions();
		InitializeAStarGrid();
		GenerateSpikes();
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
			// Exclude shop room
			if (cell.Y <= -_halfH - 2) continue;

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

	public Vector2 GetNextEnemySpawnPosition(Vector2 playerPos, float minPlayerDist = 120f)
	{
		if (_enemySpawnPositions == null || _enemySpawnPositions.Count == 0)
		{
			float angle = (float)GD.RandRange(0, Math.PI * 2);
			return playerPos + new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * 150.0f;
		}

		for (int i = 0; i < _enemySpawnPositions.Count; i++)
		{
			Vector2 pos = _enemySpawnPositions[_spawnPosIndex];
			_spawnPosIndex = (_spawnPosIndex + 1) % _enemySpawnPositions.Count;

			if (pos.DistanceTo(playerPos) >= minPlayerDist)
			{
				return pos;
			}
		}

		Vector2 fallbackPos = _enemySpawnPositions[_spawnPosIndex];
		_spawnPosIndex = (_spawnPosIndex + 1) % _enemySpawnPositions.Count;
		return fallbackPos;
	}

	public Node2D GetExitLadderNode()
	{
		return GodotObject.IsInstanceValid(_exitLadder) ? _exitLadder : null;
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
				SetCellSolid(door.GlobalPosition + new Vector2(-8 * World.Scale, -8 * World.Scale), true);
				SetCellSolid(door.GlobalPosition + new Vector2(8 * World.Scale, -8 * World.Scale), true);
				SetCellSolid(door.GlobalPosition + new Vector2(-8 * World.Scale, 8 * World.Scale), true);
				SetCellSolid(door.GlobalPosition + new Vector2(8 * World.Scale, 8 * World.Scale), true);
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

	public override void _PhysicsProcess(double delta)
	{
		LevelPlayedTime += (float)delta;
		// 1. Обработка музыки в зависимости от положения игрока
		var waveManager = GetTree().GetFirstNodeInGroup("WaveManager") as WaveManager;
		bool waveActive = waveManager != null && waveManager.WaveInProgress;
		if (!waveActive)
		{
			MusicManager.Instance?.PlayMusic("res://assets/sounds/doom_10. Ties That Bind.mp3");
		}

		if (_spikesLayer == null) return;

		if (_playerSpikeInvulnerabilityTimer > 0)
		{
			_playerSpikeInvulnerabilityTimer -= delta;
		}

		if (_enemySpikeCooldowns.Count > 0)
		{
			var keys = _enemySpikeCooldowns.Keys.ToList();
			foreach (ulong id in keys)
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
		if (_currentSpikePhase == SpikePhase.Active)
		{
			var spikeCells = _spikesLayer.GetUsedCells();
			float spikeRadius = 12.0f * World.Scale;

			if (_playerSpikeInvulnerabilityTimer <= 0 && _player != null)
			{
				Vector2 playerPos = _player.GlobalPosition;
				foreach (var cell in spikeCells)
				{
					Vector2 cellCenter = _spikesLayer.ToGlobal(_spikesLayer.MapToLocal(cell));
					if (playerPos.DistanceTo(cellCenter) < spikeRadius)
					{
						_player.TakeDamage(5f, cellCenter, 5.0f, true);
						_playerSpikeInvulnerabilityTimer = 1.0;
						break;
					}
				}
			}

			foreach (Node node in GetTree().GetNodesInGroup("Enemies"))
			{
				if (node is Enemy enemy && !(enemy is Ghost) && enemy.CurrentHealth > 0)
				{
					ulong id = enemy.GetInstanceId();
					if (_enemySpikeCooldowns.ContainsKey(id)) continue;

					Vector2 enemyPos = enemy.GlobalPosition;
					foreach (var cell in spikeCells)
					{
						Vector2 cellCenter = _spikesLayer.ToGlobal(_spikesLayer.MapToLocal(cell));
						if (enemyPos.DistanceTo(cellCenter) < spikeRadius)
						{
							enemy.TakeDamage(8f, cellCenter, 4.0f, true);
							_enemySpikeCooldowns[id] = 1.0;
							break;
						}
					}
				}
			}
		}
	}

	private void UpdateSpikeTiles()
	{
		if (_spikesLayer == null) return;
		Vector2I tileCoords;
		switch (_currentSpikePhase)
		{
			case SpikePhase.Retracted: tileCoords = new Vector2I(0, 3); break;
			case SpikePhase.Warning: tileCoords = new Vector2I(1, 3); break;
			case SpikePhase.Active: tileCoords = new Vector2I(2, 3); break;
			case SpikePhase.Retracting: tileCoords = new Vector2I(3, 3); break;
			default: tileCoords = new Vector2I(0, 3); break;
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

		float spawnChance = 0.005f; // Baseline 1/200
		if (_currentLevelIndex >= 2)
		{
			spawnChance = 0.005f * Mathf.Pow(1.5f, _currentLevelIndex - 2);
		}

		var floorCells = _floorLayer.GetUsedCells();
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

			if (_rng.Randf() < spawnChance)
			{
				_spikesLayer.SetCell(cell, 0, new Vector2I(0, 3));
			}
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
			new Vector2(-72, -144),
			new Vector2(0, -144),
			new Vector2(72, -144)
		};

		var random = new Random();
		for (int i = 0; i < offsets.Length; i++)
		{
			var pickup = pickupPrefab.Instantiate() as WeaponPickup;
			if (pickup != null)
			{
				string path = weaponPaths[random.Next(weaponPaths.Length)];
				var weaponData = GD.Load<WeaponData>(path);
				pickup.WeaponToGive = weaponData;
				pickup.GlobalPosition = door.GlobalPosition + offsets[i];
				AddChild(pickup);
			}
		}
	}

	public void SpawnWeaponPickupAt(Vector2 pos, string weaponPath)
	{
		var pickupPrefab = GD.Load<PackedScene>("res://scene/weapons/weapon_pickup.tscn");
		if (pickupPrefab == null) return;

		var pickup = pickupPrefab.Instantiate() as WeaponPickup;
		if (pickup != null)
		{
			var weaponData = GD.Load<WeaponData>(weaponPath);
			pickup.WeaponToGive = weaponData;
			pickup.GlobalPosition = FindNearestFreeSpot(pos);
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
			if (!GetTree().Paused)
			{
				GetTree().Paused = true;
				var pauseMenu = new PauseMenu();
				GetTree().Root.AddChild(pauseMenu);
				GetViewport().SetInputAsHandled();
			}
		}
	}
}
