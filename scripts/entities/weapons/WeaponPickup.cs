using Godot;
using System;
using System.Linq;

public partial class WeaponPickup : Node2D, IInteractable
{
	[Export] public WeaponData WeaponToGive;
	
	private Area2D _pickupArea;
	private Label _nameLabel;
	private Sprite2D _groundSprite;
	private Vector2 _groundSpriteBasePos;
	private float _bobTime = 0.0f;
	private bool _playerInRange = false;
	private Player _player;
	private bool _consumed = false;
	private bool _appliedPickup = false;

	private bool IsCoopSession => NetworkManager.Instance != null && NetworkManager.Instance.IsCoopSession;
	private bool IsServerAuthority => !IsCoopSession || (NetworkManager.Instance != null && NetworkManager.Instance.IsServerAuthority);

	public bool IsPlayerInRange => _playerInRange;

	public bool IsHoveredByMouse(Vector2 worldMousePos)
	{
		return WeaponToGive != null && GlobalPosition.DistanceTo(worldMousePos) <= 10.0f;
	}

	public override void _Ready()
	{
		Scale = Vector2.One;
		AddToGroup("WeaponPickup");
		_pickupArea = GetNode<Area2D>("Area2D");
		_nameLabel = GetNode<Label>("NameLabel");
		if (_nameLabel != null)
		{
			string wName = WeaponToGive != null && !string.IsNullOrEmpty(WeaponToGive.WeaponName)
				? WeaponToGive.WeaponName
				: "Weapon";
			_nameLabel.Text = $"[E] {wName}";
			DungeonVisuals.StyleWorldPromptLabel(_nameLabel, DungeonVisuals.ArcaneCyan, -22.0f);
			_nameLabel.Visible = false;
		}

		// Маска 18: Слой 2 (игрок, значение 2) + Слой 5 (зона подбора, значение 16)
		_pickupArea.CollisionMask = 18;
		_pickupArea.CollisionLayer = 0;

		_pickupArea.AreaEntered += OnAreaEntered;
		_pickupArea.AreaExited += OnAreaExited;
		_pickupArea.BodyEntered += OnBodyEntered;
		_pickupArea.BodyExited += OnBodyExited;

		if (WeaponToGive != null) {
			// АВТОМАТИЧЕСКАЯ ИКОНКА: Вытаскиваем текстуру из префаба оружия
			if (WeaponToGive.WeaponVisualPrefab != null) {
				var instance = WeaponToGive.WeaponVisualPrefab.Instantiate();

				// Ищем спрайт рекурсивно
				Sprite2D weaponSprite = FindSpriteRecursive(instance);

				if (weaponSprite != null) {
					_groundSprite = GetNodeOrNull<Sprite2D>("Sprite2D");
					if (_groundSprite != null) {
						_groundSprite.Texture = weaponSprite.Texture;
						_groundSprite.Scale = weaponSprite.Scale;
						_groundSprite.Visible = true;
						_groundSpriteBasePos = _groundSprite.Position;
					}
				}
				instance.Free(); // Сразу освобождаем временный экземпляр вне дерева сцены
			}
		}

		DungeonVisuals.AttachGroundShadow(this, new Vector2(0, 5), new Vector2(0.55f, 0.44f), 0.50f);
		var glowTex = GameResourceCache.GetRadialGlowTexture(
			new Color(0.55f, 0.88f, 1.0f, 0.85f),
			Colors.Transparent,
			0.95f,
			56,
			new Vector2(0.8f, 0.8f));
		GameResourceCache.CreateGlowLight(
			this,
			"WeaponPickupGlow",
			Vector2.Zero,
			new Color(0.55f, 0.86f, 1.0f, 0.9f),
			0.62f,
			glowTex,
			shadowEnabled: false);
		_bobTime = (float)GD.RandRange(0.0, Math.PI * 2.0);
	}

	public override void _Process(double delta)
	{
		if (_groundSprite != null && GodotObject.IsInstanceValid(_groundSprite))
		{
			_bobTime += (float)delta * 2.8f;
			_groundSprite.Position = _groundSpriteBasePos + new Vector2(0f, Mathf.Sin(_bobTime) * 1.3f);
		}
	}

			private Sprite2D FindSpriteRecursive(Node parent) {
			foreach (Node child in parent.GetChildren()) {
			if (child is Sprite2D s && !s.Name.ToString().Contains("Slash")) {
			return s;
			}
			Sprite2D found = FindSpriteRecursive(child);
			if (found != null) return found;
			}
			return null;
			}

	public void Interact(Player player)
	{
		if (WeaponToGive == null || _consumed) return;

		if (IsCoopSession)
		{
			if (player == null || !player.IsLocalPlayer || player.IsDead) return;
			if (!IsServerAuthority)
			{
				RpcId(1, nameof(RpcRequestPickupWeapon), (long)player.PeerId, player.CurrentSlotIndex);
				return;
			}
			ProcessWeaponPickupOnServer(player.PeerId, player.CurrentSlotIndex);
			return;
		}

		// Ищем первый пустой слот (null)
		int emptySlotIndex = -1;
		for (int i = 0; i < player.WeaponSlots.Count; i++) {
			if (player.WeaponSlots[i].CurrentWeapon == null) {
				emptySlotIndex = i;
				break;
			}
		}

		if (emptySlotIndex != -1) {
			player.WeaponSlots[emptySlotIndex].CurrentWeapon = WeaponToGive;
			player.SelectSlot(emptySlotIndex); // Сразу берем в руки
			GD.Print($"Подобрано оружие: {WeaponToGive.WeaponName} в слот {emptySlotIndex + 1}");
			
			player.UnregisterInteractable(this);
			QueueFree();
		} else {
			// Все слоты заняты. Меняем оружие в текущей активной руке!
			int activeIndex = player.CurrentSlotIndex;
			if (activeIndex >= 0 && activeIndex < player.WeaponSlots.Count) {
				WeaponData oldWeapon = player.WeaponSlots[activeIndex].CurrentWeapon;
				
				// 1. Устанавливаем новое оружие в этот слот
				player.WeaponSlots[activeIndex].CurrentWeapon = WeaponToGive;
				
				// 2. Деактивируем старое состояние
				player.WeaponSlots[activeIndex].Deactivate();
				
				// 3. Вызываем SelectSlot, который активирует новое оружие
				player.SelectSlot(activeIndex);

				GD.Print($"Заменено оружие в слоте {activeIndex + 1}: {oldWeapon?.WeaponName} -> {WeaponToGive.WeaponName}");

				// 4. Выбрасываем старое оружие на землю
				if (oldWeapon != null) {
					var pickupPrefab = GD.Load<PackedScene>("res://scene/weapons/weapon_pickup.tscn");
					if (pickupPrefab != null) {
						var droppedPickup = pickupPrefab.Instantiate() as WeaponPickup;
						if (droppedPickup != null) {
							droppedPickup.WeaponToGive = oldWeapon;
							droppedPickup.GlobalPosition = player.GlobalPosition;
							
							// Добавляем на сцену (на уровень)
							Node levelNode = player.GetParent();
							if (levelNode != null) {
								levelNode.AddChild(droppedPickup);
							} else {
								player.GetTree().CurrentScene.AddChild(droppedPickup);
							}
						}
					}
				}

				player.UnregisterInteractable(this);
				QueueFree();
			}
		}
	}

	public Vector2 GetInteractionPosition()
	{
		return GlobalPosition;
	}

	public void ShowPrompt(bool show)
	{
		if (_nameLabel != null)
		{
			if (show && WeaponToGive != null && !string.IsNullOrEmpty(WeaponToGive.WeaponName))
			{
				_nameLabel.Text = $"[E] {WeaponToGive.WeaponName}";
			}
			_nameLabel.Visible = show && !_consumed;
		}
	}

	private void OnAreaEntered(Area2D area)
	{
		if (area.GetParent() is Player player)
		{
			SetPlayerInRange(player, true);
		}
	}

	private void OnAreaExited(Area2D area)
	{
		if (area.GetParent() is Player player)
		{
			SetPlayerInRange(player, false);
		}
	}

	private void OnBodyEntered(Node2D body)
	{
		if (body is Player player)
		{
			SetPlayerInRange(player, true);
		}
	}

	private void OnBodyExited(Node2D body)
	{
		if (body is Player player)
		{
			SetPlayerInRange(player, false);
		}
	}

	private void SetPlayerInRange(Player player, bool inRange)
	{
		if (IsCoopSession && (player == null || !player.IsLocalPlayer || player.IsDead))
		{
			return;
		}
		_playerInRange = inRange;
		if (inRange) {
			_player = player;
			if (_player != null) {
				_player.RegisterInteractable(this);
			}
		} else {
			if (_player != null && _player == player) {
				_player.UnregisterInteractable(this);
				_player = null;
			}
		}
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RpcRequestPickupWeapon(long peerId, int preferredActiveSlot)
	{
		if (!IsServerAuthority) return;
		if (Multiplayer.GetRemoteSenderId() != (int)peerId) return;
		ProcessWeaponPickupOnServer((int)peerId, preferredActiveSlot);
	}

	private void ProcessWeaponPickupOnServer(int peerId, int preferredActiveSlot)
	{
		if (_consumed || WeaponToGive == null) return;
		Player target = NetworkManager.GetPlayerByPeerId(GetTree(), peerId);
		if (target == null || target.IsDead || target.WeaponSlots.Count == 0) return;

		int emptySlotIndex = -1;
		for (int i = 0; i < target.WeaponSlots.Count; i++)
		{
			if (target.WeaponSlots[i].CurrentWeapon == null)
			{
				emptySlotIndex = i;
				break;
			}
		}

		int targetSlot = emptySlotIndex != -1
			? emptySlotIndex
			: Mathf.Clamp(preferredActiveSlot, 0, target.WeaponSlots.Count - 1);

		string oldWeaponPath = emptySlotIndex == -1
			? (target.WeaponSlots[targetSlot]?.CurrentWeapon?.GetEffectiveResourcePath() ?? "")
			: "";

		_consumed = true;
		Vector2 dropPos = target.GlobalPosition;
		Rpc(nameof(RpcCompleteWeaponPickup), (long)peerId, targetSlot);

		if (!string.IsNullOrEmpty(oldWeaponPath))
		{
			(GetTree().GetFirstNodeInGroup("Level") as Level)?.SpawnWeaponPickupAt(dropPos, oldWeaponPath);
		}
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RpcCompleteWeaponPickup(long peerId, int slotIndex)
	{
		int sender = Multiplayer.GetRemoteSenderId();
		if (IsCoopSession && sender != 0 && sender != 1) return;
		if (_appliedPickup) return;
		_appliedPickup = true;
		_consumed = true;

		Player target = NetworkManager.GetPlayerByPeerId(GetTree(), peerId);
		if (target != null && WeaponToGive != null && slotIndex >= 0 && slotIndex < target.WeaponSlots.Count)
		{
			if (target.WeaponSlots[slotIndex].CurrentWeapon != null)
			{
				target.WeaponSlots[slotIndex].Deactivate();
			}
			target.WeaponSlots[slotIndex].CurrentWeapon = WeaponToGive;
			target.SelectSlot(slotIndex);
			target.WeaponSlots[slotIndex].ApplyWeaponSettings();
			target.UnregisterInteractable(this);
		}

		NetworkManager.GetLocalPlayer(GetTree())?.UnregisterInteractable(this);
		QueueFree();
	}
}
