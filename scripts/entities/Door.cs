using Godot;
using System;
using System.Linq;

public partial class Door : StaticBody2D
{
	[Export] public bool IsOpen = false;

	private CollisionShape2D _collisionShape;
	private Sprite2D _sprite;
	private Area2D _interactArea;
	private Label _promptLabel;
	private bool _playerInRange = false;

	// Текстуры закрытой и открытой двери (задаются в инспекторе)
	[Export] public Texture2D ClosedTexture;
	[Export] public Texture2D OpenTexture;

	public override void _Ready()
	{
		// Ищем дочерние узлы по типу (на случай, если пользователь назвал их иначе)
		_collisionShape = GetNodeOrNull<CollisionShape2D>("CollisionShape2D") ??
						  GetChildren().Cast<Node>().OfType<CollisionShape2D>().FirstOrDefault();
		
		_sprite = GetNodeOrNull<Sprite2D>("Sprite2D") ??
				  GetChildren().Cast<Node>().OfType<Sprite2D>().FirstOrDefault();
		
		_interactArea = GetNodeOrNull<Area2D>("InteractArea") ??
						GetChildren().Cast<Node>().OfType<Area2D>().FirstOrDefault();
		
		_promptLabel = GetNodeOrNull<Label>("PromptLabel") ??
					   GetChildren().Cast<Node>().OfType<Label>().FirstOrDefault();

		// Динамически создаем надпись подсказки, если её нет в сцене
		if (_promptLabel == null) {
			_promptLabel = new Label();
			_promptLabel.Text = "Press E to open";
			_promptLabel.HorizontalAlignment = HorizontalAlignment.Center;
			_promptLabel.Position = new Vector2(-50, -35);
			_promptLabel.Size = new Vector2(100, 20);
			_promptLabel.Visible = false;
			
			var font = GD.Load<Font>("res://assets/fonts/m5x7/m5x7.ttf");
			if (font != null) {
				_promptLabel.AddThemeFontOverride("font", font);
				_promptLabel.AddThemeFontSizeOverride("font_size", 8);
			}
			AddChild(_promptLabel);
		}

		if (_interactArea != null) {
			// Гарантируем правильную маску коллизий: слой 1 (тело игрока) и 16 (зона оружия)
			_interactArea.CollisionMask |= 1 | 16;
			
			// Подписываемся на события входа как физических тел, так и зон (для надежности)
			_interactArea.BodyEntered += OnBodyEntered;
			_interactArea.BodyExited += OnBodyExited;
			_interactArea.AreaEntered += OnAreaEntered;
			_interactArea.AreaExited += OnAreaExited;
		}

		UpdateDoorState();
	}

	public override void _PhysicsProcess(double delta)
	{
		if (_playerInRange && (Input.IsActionJustPressed("interact") || Input.IsPhysicalKeyPressed(Key.E))) {
			ToggleDoor();
		}
	}

	public void ToggleDoor()
	{
		IsOpen = !IsOpen;
		UpdateDoorState();
	}

	private void UpdateDoorState()
	{
		// Включаем или отключаем физическую преграду
		if (_collisionShape != null) {
			_collisionShape.SetDeferred("disabled", IsOpen);
		}

		// Меняем текстуру
		if (_sprite != null) {
			var tex = IsOpen ? OpenTexture : ClosedTexture;
			if (tex != null) {
				_sprite.Texture = tex;
			}
		}

		// Обновляем текст подсказки
		if (_promptLabel != null && _playerInRange) {
			_promptLabel.Text = IsOpen ? "Press E to close" : "Press E to open";
		}
	}

	private void OnBodyEntered(Node2D body) { CheckPlayer(body, true); }
	private void OnBodyExited(Node2D body) { CheckPlayer(body, false); }
	private void OnAreaEntered(Area2D area) { CheckPlayer(area.GetParent(), true); }
	private void OnAreaExited(Area2D area) { CheckPlayer(area.GetParent(), false); }

	private void CheckPlayer(Node node, bool entered)
	{
		if (node is Player || (node != null && node.IsInGroup("Player"))) {
			_playerInRange = entered;
			if (_promptLabel != null) {
				_promptLabel.Text = IsOpen ? "Press E to close" : "Press E to open";
				_promptLabel.Visible = entered;
			}
		}
	}
}
