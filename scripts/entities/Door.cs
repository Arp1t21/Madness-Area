using Godot;
using System;
using System.Linq;

public partial class Door : StaticBody2D, IInteractable
{
	[Export] public bool IsOpen = false;

	private CollisionShape2D _collisionShape;
	private Sprite2D _sprite;
	private Area2D _interactArea;
	private Label _promptLabel;
	private bool _playerInRange = false;
	private bool _eWasPressed = false;

	// Текстуры закрытой и открытой двери (задаются в инспекторе)
	[Export] public Texture2D ClosedTexture;
	[Export] public Texture2D OpenTexture;

	public override void _Ready()
	{
		AddToGroup("Door");
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
			_promptLabel.Position = new Vector2(-34, -35);
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
			// Гарантируем правильную маску коллизий: слой 5 (значение 16)
			_interactArea.CollisionMask = 16;
			
			// Подписываемся на события входа как физических тел, так и зон (для надежности)
			_interactArea.BodyEntered += OnBodyEntered;
			_interactArea.BodyExited += OnBodyExited;
			_interactArea.AreaEntered += OnAreaEntered;
			_interactArea.AreaExited += OnAreaExited;
		}

		UpdateDoorState();
	}

	public void Interact(Player player)
	{
		ToggleDoor();
	}

	public Vector2 GetInteractionPosition()
	{
		return GlobalPosition;
	}

	public void ShowPrompt(bool show)
	{
		if (_promptLabel != null) {
			_promptLabel.Text = IsOpen ? "Press E to close" : "Press E to open";
			_promptLabel.Visible = show;
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

		// Обновляем проходимость сетки AStar в уровне
		var level = GetTree()?.GetFirstNodeInGroup("Level") as Level;
		if (level != null)
		{
			level.SetCellSolid(GlobalPosition + new Vector2(-8, -8), !IsOpen);
			level.SetCellSolid(GlobalPosition + new Vector2(8, -8), !IsOpen);
			level.SetCellSolid(GlobalPosition + new Vector2(-8, 8), !IsOpen);
			level.SetCellSolid(GlobalPosition + new Vector2(8, 8), !IsOpen);
		}
	}

	private void OnBodyEntered(Node2D body) { CheckPlayer(body, true); }
	private void OnBodyExited(Node2D body) { CheckPlayer(body, false); }
	private void OnAreaEntered(Area2D area) { CheckPlayer(area.GetParent(), true); }
	private void OnAreaExited(Area2D area) { CheckPlayer(area.GetParent(), false); }

	private void CheckPlayer(Node node, bool entered)
	{
		Player player = node as Player;
		if (player == null && node != null && node.IsInGroup("Player")) {
			player = GetTree().GetFirstNodeInGroup("Player") as Player;
		}

		if (player != null) {
			_playerInRange = entered;
			if (entered) {
				player.RegisterInteractable(this);
			} else {
				player.UnregisterInteractable(this);
			}
		}
	}
}
