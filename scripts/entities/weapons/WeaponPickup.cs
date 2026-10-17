using Godot;
using System;
using System.Linq;

public partial class WeaponPickup : Node2D, IInteractable
{
	[Export] public WeaponData WeaponToGive;
	
	private Area2D _pickupArea;
	private Label _nameLabel;
	private bool _playerInRange = false;
	private Player _player;

	public override void _Ready()
	{
		Scale = new Vector2(3.0f, 3.0f);
		_pickupArea = GetNode<Area2D>("Area2D");
		_nameLabel = GetNode<Label>("NameLabel");

		var font = GD.Load<Font>("res://assets/fonts/silver/Silver.ttf");
		if (font != null) {
			_nameLabel.AddThemeFontOverride("font", font);
		}
		_nameLabel.AddThemeFontSizeOverride("font_size", 7);
		_nameLabel.AddThemeConstantOverride("letter_spacing", 1);
		_nameLabel.AddThemeConstantOverride("line_spacing", -2);
		_nameLabel.AddThemeColorOverride("font_color", new Color(0.98f, 0.95f, 0.85f));
		_nameLabel.Position = new Vector2(-56, -42);
		_nameLabel.Size = new Vector2(112, 27);
		_nameLabel.ZIndex = 10;

		var boxStyle = new StyleBoxFlat
		{
			BgColor = new Color(0.06f, 0.06f, 0.09f, 0.92f),
			BorderColor = new Color(0.95f, 0.78f, 0.25f, 0.88f),
			BorderWidthLeft = 1,
			BorderWidthTop = 1,
			BorderWidthRight = 1,
			BorderWidthBottom = 1,
			CornerRadiusTopLeft = 2,
			CornerRadiusTopRight = 2,
			CornerRadiusBottomLeft = 2,
			CornerRadiusBottomRight = 2,
			ContentMarginLeft = 3,
			ContentMarginRight = 3,
			ContentMarginTop = 1,
			ContentMarginBottom = 1
		};
		_nameLabel.AddThemeStyleboxOverride("normal", boxStyle);

		// Маска 18: Слой 2 (игрок, значение 2) + Слой 5 (зона подбора, значение 16)
		_pickupArea.CollisionMask = 18;
		_pickupArea.CollisionLayer = 0;

		_pickupArea.AreaEntered += OnAreaEntered;
		_pickupArea.AreaExited += OnAreaExited;
		_pickupArea.BodyEntered += OnBodyEntered;
		_pickupArea.BodyExited += OnBodyExited;

		if (WeaponToGive != null) {
			_nameLabel.Text = WeaponShowcaseSlot.FormatWeaponStatsTooltip(WeaponToGive, 1.0f, true);

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

	public void Interact(Player player)
	{
		if (WeaponToGive == null) return;

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
			if (show && WeaponToGive != null)
			{
				float mult = (_player != null && GodotObject.IsInstanceValid(_player)) ? _player.WeaponDamageMultiplier : 1.0f;
				_nameLabel.Text = WeaponShowcaseSlot.FormatWeaponStatsTooltip(WeaponToGive, mult, true);
			}
			_nameLabel.Visible = show;
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
		_playerInRange = inRange;
		if (inRange) {
			_player = player;
			if (_player != null) {
				_player.RegisterInteractable(this);
			}
		} else {
			if (_player != null) {
				_player.UnregisterInteractable(this);
			}
			_player = null;
		}
	}
}
