using Godot;
using System;
using System.Linq;

public partial class WeaponPickup : Node2D
{
	[Export] public WeaponData WeaponToGive;
	
	private Area2D _pickupArea;
	private Label _nameLabel;
	private bool _playerInRange = false;
	private Player _player;

	public override void _Ready()
	{
		_pickupArea = GetNode<Area2D>("Area2D");
		_nameLabel = GetNode<Label>("NameLabel");

		// Принудительно ставим шрифт m5x7
		var font = GD.Load<Font>("res://assets/fonts/m5x7/m5x7.ttf");
		if (font != null) {
			_nameLabel.AddThemeFontOverride("font", font);
		}

		// Маска 18: Слой 2 (игрок, значение 2) + Слой 5 (зона подбора, значение 16)
		_pickupArea.CollisionMask = 18;
		_pickupArea.CollisionLayer = 0;

		_pickupArea.AreaEntered += OnAreaEntered;
		_pickupArea.AreaExited += OnAreaExited;
		_pickupArea.BodyEntered += OnBodyEntered;
		_pickupArea.BodyExited += OnBodyExited;

		if (WeaponToGive != null) {
			string displayName = WeaponToGive.WeaponName;
			if (string.IsNullOrEmpty(displayName) || displayName == "Новое оружие") {
				displayName = System.IO.Path.GetFileNameWithoutExtension(WeaponToGive.ResourcePath);
			}
			_nameLabel.Text = displayName;

			// АВТОМАТИЧЕСКАЯ ИКОНКА: Вытаскиваем текстуру из префаба оружия
			if (WeaponToGive.WeaponVisualPrefab != null) {
				var instance = WeaponToGive.WeaponVisualPrefab.Instantiate();

				// Ищем спрайт рекурсивно
				Sprite2D weaponSprite = FindSpriteRecursive(instance);

				if (weaponSprite != null) {
					var groundSprite = GetNodeOrNull<Sprite2D>("Sprite2D");
					if (groundSprite != null) {
						groundSprite.Texture = weaponSprite.Texture;
						groundSprite.Scale = weaponSprite.Scale;
						groundSprite.Visible = true;
					}
				}
				instance.QueueFree(); // Удаляем временный экземпляр
			}
		}

		_nameLabel.Visible = false;
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

	public override void _Process(double delta)
	{
		// Проверяем нажатие E (через Input Map или жестко по кнопке)
		bool isEPressed = Input.IsActionJustPressed("interact") || Input.IsPhysicalKeyPressed(Key.E);

		if (_playerInRange && isEPressed) 
		{
			if (_player != null && WeaponToGive != null)
			{
				// Ищем первый пустой слот (null)
				int emptySlotIndex = -1;
				for (int i = 0; i < _player.WeaponSlots.Count; i++) {
					if (_player.WeaponSlots[i].CurrentWeapon == null) {
						emptySlotIndex = i;
						break;
					}
				}

				if (emptySlotIndex != -1) {
					_player.WeaponSlots[emptySlotIndex].CurrentWeapon = WeaponToGive;
					_player.SelectSlot(emptySlotIndex); // Сразу берем в руки
					GD.Print($"Подобрано оружие: {WeaponToGive.WeaponName} в слот {emptySlotIndex + 1}");
					QueueFree();
				} else {
					// Все слоты заняты. Меняем оружие в текущей активной руке!
					int activeIndex = _player.CurrentSlotIndex;
					if (activeIndex >= 0 && activeIndex < _player.WeaponSlots.Count) {
						WeaponData oldWeapon = _player.WeaponSlots[activeIndex].CurrentWeapon;
						
						// 1. Устанавливаем новое оружие в этот слот
						_player.WeaponSlots[activeIndex].CurrentWeapon = WeaponToGive;
						
						// 2. Деактивируем старое состояние
						_player.WeaponSlots[activeIndex].Deactivate();
						
						// 3. Вызываем SelectSlot, который активирует новое оружие
						_player.SelectSlot(activeIndex);

						GD.Print($"Заменено оружие в слоте {activeIndex + 1}: {oldWeapon?.WeaponName} -> {WeaponToGive.WeaponName}");

						// 4. Выбрасываем старое оружие на землю
						if (oldWeapon != null) {
							var pickupPrefab = GD.Load<PackedScene>("res://scene/weapons/weapon_pickup.tscn");
							if (pickupPrefab != null) {
								var droppedPickup = pickupPrefab.Instantiate() as WeaponPickup;
								if (droppedPickup != null) {
									droppedPickup.WeaponToGive = oldWeapon;
									droppedPickup.GlobalPosition = _player.GlobalPosition;
									
									// Добавляем на сцену (на уровень)
									Node levelNode = _player.GetParent();
									if (levelNode != null) {
										levelNode.AddChild(droppedPickup);
									} else {
										_player.GetTree().CurrentScene.AddChild(droppedPickup);
									}
								}
							}
						}

						QueueFree();
					}
				}
			}
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
		if (area.GetParent() is Player)
		{
			SetPlayerInRange(null, false);
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
		if (body is Player)
		{
			SetPlayerInRange(null, false);
		}
	}

	private void SetPlayerInRange(Player player, bool inRange)
	{
		_playerInRange = inRange;
		_player = player;
		if (_nameLabel != null) {
			_nameLabel.Visible = inRange;
		}
	}
}
