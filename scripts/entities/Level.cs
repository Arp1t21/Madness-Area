using Godot;
using System;
using System.Linq;

public partial class Level : Node2D
{
	private Player _player;
	private TileMapLayer _stairsLayer;
	private Sprite2D _entryLadder;
	private Area2D _exitLadder;
	private Label _exitPromptLabel;
	private bool _playerOnExitLadder = false;
	private bool _eWasPressed = false;
	private Vector2 _stairsPosition = Vector2.Zero;
	private bool _hasStairs = false;

	public override void _Ready()
	{
		// Регистрируем уровень в группе Level
		AddToGroup("Level");

		// 1. Создаем CanvasModulate для общего затемнения сцены (черный void за картой)
		var modulate = new CanvasModulate();
		modulate.Color = new Color(0.0f, 0.0f, 0.0f, 1.0f); // Полная темнота
		AddChild(modulate);

		// 2. Находим слой тайлов карты (для генерации окклюдеров)
		var tileMapLayer = GetNodeOrNull<TileMapLayer>("Map/Floor") ??
		                   GetNodeOrNull<TileMapLayer>("Map/Layer0") ?? 
		                   GetNodeOrNull<TileMapLayer>("Map") ??
		                   GetChildren().Cast<Node>().OfType<TileMapLayer>().FirstOrDefault();

		if (tileMapLayer != null) {
			var usedCells = tileMapLayer.GetUsedCells();
			foreach (var cell in usedCells) {
				var tileData = tileMapLayer.GetCellTileData(cell);
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
					occluder.Position = tileMapLayer.MapToLocal(cell);
					tileMapLayer.AddChild(occluder);
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
			}
			// Скрываем лестницу на время волны
			_stairsLayer.Visible = false;
		}

		// 4. Спавним входную лестницу под игроком на старте
		if (_player != null) {
			SpawnEntryLadder(_player.GlobalPosition);
		}
	}

	public override void _PhysicsProcess(double delta)
	{
		bool isPressed = Input.IsActionPressed("interact") || Input.IsPhysicalKeyPressed(Key.E);
		
		if (_playerOnExitLadder && isPressed && !_eWasPressed) {
			_playerOnExitLadder = false;
			DescendStairs();
		}
		
		_eWasPressed = isPressed;
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
	}

	private void SpawnExitLadderTrigger()
	{
		_exitLadder = new Area2D();
		_exitLadder.Name = "ExitLadder";
		_exitLadder.CollisionLayer = 0;
		_exitLadder.CollisionMask = 16; // Слой 5 коллизий (для детекта Area2DPickWeapon)

		var collision = new CollisionShape2D();
		var circle = new CircleShape2D();
		circle.Radius = 15.0f;
		collision.Shape = circle;
		_exitLadder.AddChild(collision);

		_exitPromptLabel = new Label();
		_exitPromptLabel.Text = "Go deeper [E]";
		_exitPromptLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_exitPromptLabel.Position = new Vector2(-40, -25);
		_exitPromptLabel.Size = new Vector2(80, 15);
		
		var font = GD.Load<Font>("res://assets/fonts/m5x7/m5x7.ttf");
		if (font != null) {
			_exitPromptLabel.AddThemeFontOverride("font", font);
			_exitPromptLabel.AddThemeFontSizeOverride("font_size", 8);
		}
		_exitPromptLabel.Visible = false;
		_exitLadder.AddChild(_exitPromptLabel);

		_exitLadder.GlobalPosition = _stairsPosition;
		CallDeferred("add_child", _exitLadder);

		// Сигналы входа/выхода
		_exitLadder.AreaEntered += (area) => {
			if (area.GetParent() is Player) {
				_playerOnExitLadder = true;
				_exitPromptLabel.Visible = true;
			}
		};
		_exitLadder.AreaExited += (area) => {
			if (area.GetParent() is Player) {
				_playerOnExitLadder = false;
				_exitPromptLabel.Visible = false;
			}
		};
	}

	private void DescendStairs()
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

		// 3. Спавним входную лестницу под игроком
		if (_player != null) {
			SpawnEntryLadder(_player.GlobalPosition);
		}

		// 4. Запускаем следующую волну
		var waveManager = GetTree().GetFirstNodeInGroup("WaveManager") as WaveManager;
		if (waveManager != null) {
			waveManager.StartNextWave();
		}
	}

	public Node2D GetExitLadderNode()
	{
		return GodotObject.IsInstanceValid(_exitLadder) ? _exitLadder : null;
	}
}
