using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class Level : Node2D
{
	private Player _player;
	private TileMapLayer _floorLayer;
	private TileMapLayer _wallLayer;
	private TileMapLayer _stairsLayer;
	private TileMapLayer _spikesLayer;
	private Sprite2D _entryLadder;
	private ExitLadder _exitLadder;
	private Vector2 _stairsPosition = Vector2.Zero;
	private bool _hasStairs = false;

	private int _currentLevelIndex = 1;
	public int CurrentLevelIndex => _currentLevelIndex;
	private List<Vector2> _enemySpawnPositions = new List<Vector2>();
	private int _spawnPosIndex = 0;
	private AStarGrid2D _astarGrid;

	private int _halfW;
	private int _halfH;

	private double _spikesTimer = 0.0;
	private double _playerSpikeInvulnerabilityTimer = 0.0;
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
		float roll = (float)GD.RandRange(0, total);
		float sum = 0f;
		foreach (var item in FloorTiles)
		{
			sum += item.prob;
			if (roll <= sum) return item.coords;
		}
		return new Vector2I(1, 4);
	}

	public override void _Ready()
	{
		AddToGroup("Level");

		// 1. Создаем CanvasModulate для общего затемнения сцены (черный void за картой)
		var modulate = new CanvasModulate();
		modulate.Color = new Color(0.0f, 0.0f, 0.0f, 1.0f); // Полная темнота
		AddChild(modulate);

		// 2. Находим слой тайлов карты (для генерации окклюдеров)
		_floorLayer = GetNodeOrNull<TileMapLayer>("Map/Floor") ??
					  GetNodeOrNull<TileMapLayer>("Map/Layer0") ?? 
					  GetNodeOrNull<TileMapLayer>("Map") ??
					  GetChildren().Cast<Node>().OfType<TileMapLayer>().FirstOrDefault();

		_wallLayer = GetNodeOrNull<TileMapLayer>("Map/Wall");
		if (_wallLayer != null) {
			_wallLayer.UseParentMaterial = false;
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
				_stairsPosition = _stairsLayer.GlobalPosition + _stairsLayer.MapToLocal(cells[0]);
				_hasStairs = true;
			} else {
				// Fallback лестницы, если она не поставлена в редакторе (решает баг отсутствия лестницы на Level 1)
				if (_floorLayer != null) {
					var floorCells = _floorLayer.GetUsedCells();
					Vector2 playerPos = _player != null ? _player.GlobalPosition : Vector2.Zero;
					Vector2I bestCell = Vector2I.Zero;
					float maxDist = -1f;

					foreach (var cell in floorCells) {
						if (_wallLayer != null && _wallLayer.GetCellSourceId(cell) != -1) continue;

						Vector2 cellGlobalPos = _floorLayer.GlobalPosition + _floorLayer.MapToLocal(cell);
						float dist = cellGlobalPos.DistanceTo(playerPos);
						if (dist > maxDist) {
							maxDist = dist;
							bestCell = cell;
						}
					}

					if (maxDist > 0) {
						_stairsLayer.SetCell(bestCell, 1, new Vector2I(0, 5));
						_stairsPosition = _stairsLayer.GlobalPosition + _stairsLayer.MapToLocal(bestCell);
						_hasStairs = true;
					}
				}
			}
			// Скрываем лестницу на время волны
			_stairsLayer.Visible = false;
		}

		// 4. Спавним входную лестницу под игроком на старте
		if (_player != null) {
			SpawnEntryLadder(_player.GlobalPosition);
		}

		// 5. Инициализируем спавн-позиции мобов для первого уровня
		InitializeSpawnPositions();
		InitializeAStarGrid();

		var door = GetNodeOrNull<Door>("Map/Door");
		if (door != null)
		{
			door.HideDoor();
		}
		GenerateSpikes();
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

		// 3. Активируем дверь и убираем стены под ней
		var door = GetNodeOrNull<Door>("Map/Door");
		if (door != null)
		{
			// Для процедурных уровней (Level 2+) перемещаем дверь на верхнюю границу
			if (_currentLevelIndex > 1 && _wallLayer != null)
			{
				door.GlobalPosition = _wallLayer.ToGlobal(_wallLayer.MapToLocal(new Vector2I(0, -_halfH - 1)) + new Vector2(-8, 0));
			}

			// Удаляем стены на пути двери
			if (_wallLayer != null)
			{
				Vector2I doorCell = _wallLayer.LocalToMap(_wallLayer.ToLocal(door.GlobalPosition));
				_wallLayer.SetCell(doorCell, -1);
				_wallLayer.SetCell(doorCell + new Vector2I(-1, 0), -1);

				// Перестраиваем сетку AStar
				InitializeAStarGrid();
			}

			door.SpawnDoor();
		}
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
		// 1. Удаляем триггер взаимодействия
		if (GodotObject.IsInstanceValid(_exitLadder)) {
			_exitLadder.QueueFree();
			_exitLadder = null;
		}

		// 2. Скрываем лестницу
		if (_stairsLayer != null) {
			_stairsLayer.Visible = false;
		}

		// 3. Переход на следующий уровень
		_currentLevelIndex++;
		if (_currentLevelIndex > 1) {
			GenerateRandomLevel();
		}

		// 4. Спавним входную лестницу под игроком
		if (_player != null) {
			SpawnEntryLadder(_player.GlobalPosition);
		}

		// 5. Запускаем следующую волну
		var waveManager = GetTree().GetFirstNodeInGroup("WaveManager") as WaveManager;
		if (waveManager != null) {
			waveManager.StartNextWave();
		}
	}

	private void GenerateRandomLevel()
	{
		GD.Print($"Generating Random Level {_currentLevelIndex}...");

		// 1. Очистка старых объектов с Уровня 1
		var mapNode = GetNodeOrNull("Map");
		if (mapNode != null)
		{
			foreach (var child in mapNode.GetChildren())
			{
				if (child is Door || child.Name.ToString().Contains("Door"))
				{
					child.QueueFree();
				}
			}
		}

		foreach (var child in GetChildren())
		{
			if (child is WeaponPickup)
			{
				child.QueueFree();
			}
		}

		// 2. Выбираем случайные размеры арены (между 22 и 35 тайлами)
		int width = GD.RandRange(22, 35);
		int height = GD.RandRange(22, 35);

		_halfW = width / 2;
		_halfH = height / 2;
		int halfW = _halfW;
		int halfH = _halfH;

		// 3. Очищаем тайлы
		_floorLayer.Clear();
		if (_wallLayer != null) _wallLayer.Clear();
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
		if (_wallLayer != null)
		{
			Vector2I wallTile = WallTiles[0];

			// Верхняя и нижняя границы
			for (int x = -halfW - 1; x <= halfW + 1; x++)
			{
				_wallLayer.SetCell(new Vector2I(x, -halfH - 1), 1, wallTile);
				_wallLayer.SetCell(new Vector2I(x, halfH + 1), 1, wallTile);
			}

			// Левая и правая границы
			for (int y = -halfH; y <= halfH; y++)
			{
				_wallLayer.SetCell(new Vector2I(-halfW - 1, y), 1, wallTile);
				_wallLayer.SetCell(new Vector2I(halfW + 1, y), 1, wallTile);
			}

			// 6. Рисуем внутренние колонны/препятствия
			int numPillars = GD.RandRange(3, 8);
			for (int i = 0; i < numPillars; i++)
			{
				int px = GD.RandRange(-halfW + 3, halfW - 3);
				int py = GD.RandRange(-halfH + 3, halfH - 3);

				// Не спавним колонны у центра (0,0), где появляется игрок
				if (Math.Abs(px) < 5 && Math.Abs(py) < 5) continue;

				Vector2I pTile = WallTiles[GD.RandRange(0, WallTiles.Length - 1)];
				_wallLayer.SetCell(new Vector2I(px, py), 1, pTile);
				_wallLayer.SetCell(new Vector2I(px + 1, py), 1, pTile);
				_wallLayer.SetCell(new Vector2I(px, py + 1), 1, pTile);
				_wallLayer.SetCell(new Vector2I(px + 1, py + 1), 1, pTile);
			}
		}

		// 7. Размещаем выходную лестницу на случайном свободном тайле
		if (_stairsLayer != null)
		{
			int sx = 0, sy = 0;
			bool validPosition = false;

			while (!validPosition)
			{
				sx = GD.RandRange(-halfW + 4, halfW - 4);
				sy = GD.RandRange(-halfH + 4, halfH - 4);

				// Лестница должна быть далеко от игрока
				if (Math.Abs(sx) > 6 || Math.Abs(sy) > 6)
				{
					// И не пересекаться со стенами
					if (_wallLayer == null || _wallLayer.GetCellSourceId(new Vector2I(sx, sy)) == -1)
					{
						validPosition = true;
					}
				}
			}

			_stairsLayer.SetCell(new Vector2I(sx, sy), 1, new Vector2I(0, 5));
			_stairsPosition = _stairsLayer.GlobalPosition + _stairsLayer.MapToLocal(new Vector2I(sx, sy));
			_hasStairs = true;
			_stairsLayer.Visible = false;
		}

		// 8. Генерируем LightOccluder2D для всех стен
		if (_wallLayer != null)
		{
			foreach (var child in _wallLayer.GetChildren())
			{
				if (child is LightOccluder2D) child.QueueFree();
			}

			var usedCells = _wallLayer.GetUsedCells();
			foreach (var cell in usedCells)
			{
				var occluder = new LightOccluder2D();
				var polygon = new OccluderPolygon2D();
				polygon.Polygon = new Vector2[] {
					new Vector2(-8, -8),
					new Vector2(8, -8),
					new Vector2(8, 8),
					new Vector2(-8, 8)
				};
				occluder.Occluder = polygon;
				occluder.Position = _wallLayer.MapToLocal(cell);
				_wallLayer.AddChild(occluder);
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
		var wallLayer = _wallLayer;

		if (floorLayer == null) return;

		var floorCells = floorLayer.GetUsedCells();
		var random = new Random();

		foreach (var cell in floorCells)
		{
			// Не спавним врагов внутри стен
			if (wallLayer != null && wallLayer.GetCellSourceId(cell) != -1)
			{
				continue;
			}

			Vector2 globalPos = floorLayer.GlobalPosition + floorLayer.MapToLocal(cell);
			_enemySpawnPositions.Add(globalPos);
		}

		// Перемешиваем точки
		_enemySpawnPositions = _enemySpawnPositions.OrderBy(x => random.Next()).ToList();
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
		_astarGrid.DiagonalMode = AStarGrid2D.DiagonalModeEnum.AtLeastOneWalkable;
		_astarGrid.Update();

		// Заполняем стены как непроходимые препятствия
		if (_wallLayer != null)
		{
			foreach (var cell in _wallLayer.GetUsedCells())
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
			path.Add(pt);
		}
		return path;
	}

	public override void _PhysicsProcess(double delta)
	{
		if (_spikesLayer == null) return;

		if (_playerSpikeInvulnerabilityTimer > 0)
		{
			_playerSpikeInvulnerabilityTimer -= delta;
		}

		_spikesTimer += delta;
		double cycleTime = _spikesTimer % 4.0;

		SpikePhase newPhase;
		if (cycleTime < 1.7) newPhase = SpikePhase.Retracted;
		else if (cycleTime < 2.0) newPhase = SpikePhase.Warning;
		else if (cycleTime < 3.8) newPhase = SpikePhase.Active;
		else newPhase = SpikePhase.Retracting;

		if (newPhase != _currentSpikePhase)
		{
			_currentSpikePhase = newPhase;
			UpdateSpikeTiles();
		}

		// Урон наносим только во время активной фазы
		if (_currentSpikePhase == SpikePhase.Active && _playerSpikeInvulnerabilityTimer <= 0 && _player != null)
		{
			Vector2 playerPos = _player.GlobalPosition;
			var spikeCells = _spikesLayer.GetUsedCells();
			foreach (var cell in spikeCells)
			{
				Vector2 cellCenter = _spikesLayer.GlobalPosition + _spikesLayer.MapToLocal(cell);
				if (playerPos.DistanceTo(cellCenter) < 12.0f)
				{
					_player.TakeDamage(5f, cellCenter, 5.0f);
					_playerSpikeInvulnerabilityTimer = 1.0;
					break;
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

		var floorCells = _floorLayer.GetUsedCells();
		foreach (var cell in floorCells)
		{
			// Не спавним шипы в центре (где игрок появляется)
			if (Math.Abs(cell.X) < 3 && Math.Abs(cell.Y) < 3) continue;

			// Не спавним на стенах
			if (_wallLayer != null && _wallLayer.GetCellSourceId(cell) != -1) continue;

			// Не спавним на лестнице
			if (_stairsLayer != null && _stairsLayer.GetCellSourceId(cell) != -1) continue;

			// Шанс 1% (1/100)
			if (GD.Randf() < 0.01f)
			{
				_spikesLayer.SetCell(cell, 0, new Vector2I(0, 3));
			}
		}
	}
}
