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

	private bool _isActive = false;
	public bool IsActive => _isActive;
	private bool _isLocked = true;
	public bool IsLocked => _isLocked;
	private bool _shopItemsSpawned = false;
	private Level _cachedLevel;

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
			AddChild(_promptLabel);
		}
		_promptLabel.Text = "[E] Open Door";
		_promptLabel.Visible = false;
		DungeonVisuals.StyleWorldPromptLabel(_promptLabel, DungeonVisuals.BrassGold, -25.0f);

		if (_interactArea != null) {
			// Гарантируем правильную маску коллизий: слой 5 (значение 16)
			_interactArea.CollisionMask = 16;
			
			// Подписываемся на события входа как физических тел, так и зон (для надежности)
			_interactArea.BodyEntered += OnBodyEntered;
			_interactArea.BodyExited += OnBodyExited;
			_interactArea.AreaEntered += OnAreaEntered;
			_interactArea.AreaExited += OnAreaExited;
		}

		ShowLockedDoor();
	}

	private bool IsCoopSession => NetworkManager.Instance != null && NetworkManager.Instance.IsCoopSession;
	private bool IsServerAuthority => !IsCoopSession || (NetworkManager.Instance != null && NetworkManager.Instance.IsServerAuthority);

	public void Interact(Player player)
	{
		if (IsCoopSession && (player == null || !player.IsLocalPlayer || player.IsDead)) return;
		ToggleDoor();
	}

	public Vector2 GetInteractionPosition()
	{
		return GlobalPosition;
	}

	public void ShowPrompt(bool show)
	{
		if (_promptLabel != null) {
			_promptLabel.Text = IsOpen ? "[E] Close Door" : "[E] Open Door";
			_promptLabel.Visible = show;
		}
	}

	public void ToggleDoor()
	{
		if (IsCoopSession)
		{
			if (!IsServerAuthority)
			{
				RpcId(1, nameof(RpcRequestToggleDoor));
				return;
			}
			Rpc(nameof(RpcSetDoorOpen), !IsOpen);
			return;
		}

		ApplyDoorToggle(!IsOpen);
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RpcRequestToggleDoor()
	{
		if (!IsServerAuthority) return;
		Rpc(nameof(RpcSetDoorOpen), !IsOpen);
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RpcSetDoorOpen(bool open)
	{
		int sender = Multiplayer.GetRemoteSenderId();
		if (IsCoopSession && sender != 0 && sender != 1) return;
		ApplyDoorToggle(open);
	}

	private void ApplyDoorToggle(bool open)
	{
		IsOpen = open;
		UpdateDoorState();
		if (IsOpen && !_shopItemsSpawned)
		{
			_shopItemsSpawned = true;
			if (!IsCoopSession || IsServerAuthority)
			{
				var level = GetTree()?.GetFirstNodeInGroup("Level") as Level;
				level?.SpawnShopItems();
			}
		}
	}

	private void UpdateDoorState()
	{
		if (!_isActive)
		{
			if (_collisionShape != null) {
				_collisionShape.SetDeferred("disabled", true);
			}
			if (_interactArea != null) {
				foreach (var child in _interactArea.GetChildren()) {
					if (child is CollisionShape2D cs) {
						cs.SetDeferred("disabled", true);
					}
				}
			}
			return;
		}

		// Включаем или отключаем физическую преграду
		if (_collisionShape != null) {
			_collisionShape.SetDeferred("disabled", _isLocked ? false : IsOpen);
		}

		if (_interactArea != null) {
			foreach (var child in _interactArea.GetChildren()) {
				if (child is CollisionShape2D cs) {
					cs.SetDeferred("disabled", _isLocked);
				}
			}
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
			_promptLabel.Text = IsOpen ? "[E] Close Door" : "[E] Open Door";
		}

		// Обновляем проходимость сетки AStar в уровне
		if (!IsInstanceValid(_cachedLevel))
		{
			_cachedLevel = GetTree()?.GetFirstNodeInGroup("Level") as Level;
		}
		if (_cachedLevel != null)
		{
			bool solid = _isLocked || !IsOpen;
			_cachedLevel.SetCellSolid(GlobalPosition + new Vector2(-8, -8), solid);
			_cachedLevel.SetCellSolid(GlobalPosition + new Vector2(8, -8), solid);
			_cachedLevel.SetCellSolid(GlobalPosition + new Vector2(-8, 8), solid);
			_cachedLevel.SetCellSolid(GlobalPosition + new Vector2(8, 8), solid);
		}
	}

	private void OnBodyEntered(Node2D body) { CheckPlayer(body, true); }
	private void OnBodyExited(Node2D body) { CheckPlayer(body, false); }
	private void OnAreaEntered(Area2D area) { CheckPlayer(area.GetParent(), true); }
	private void OnAreaExited(Area2D area) { CheckPlayer(area.GetParent(), false); }

	private void CheckPlayer(Node node, bool entered)
	{
		Player player = node as Player;
		if (player == null && node != null && node.IsInGroup("Player") && !IsCoopSession) {
			player = GetTree().GetFirstNodeInGroup("Player") as Player;
		}

		if (player != null) {
			if (IsCoopSession && (!player.IsLocalPlayer || player.IsDead)) return;
			_playerInRange = entered;
			if (entered) {
				if (_isActive && !_isLocked) player.RegisterInteractable(this);
			} else {
				player.UnregisterInteractable(this);
			}
		}
	}

	public void ShowLockedDoor()
	{
		_isActive = true;
		_isLocked = true;
		IsOpen = false;
		Visible = true;
		UpdateDoorState();
	}

	public void SpawnDoor()
	{
		_isActive = true;
		_isLocked = false;
		Visible = true;
		UpdateDoorState();
		if (_playerInRange)
		{
			var player = GetTree().GetFirstNodeInGroup("Player") as Player;
			player?.RegisterInteractable(this);
		}
	}

	public void HideDoor()
	{
		_isActive = false;
		_isLocked = true;
		Visible = false;
		UpdateDoorState();
	}
}
