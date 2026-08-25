using Godot;
using System;
using System.Linq;

public partial class MeleeWeaponVisual : Node2D
{
	public enum FlipMode { None, MirrorY, MirrorX }

	[Export] public float OrientationOffset = 30.0f;
	[Export] public FlipMode Mode = FlipMode.MirrorY;
	[Export] public bool SyncOwnerFlip = true;
	
	private AnimatedSprite2D _ownerSprite;
	private Node2D _ownerNode;
	private Sprite2D _sprite;

	public override void _Ready()
	{
		// Ищем спрайт владельца и тело персонажа
		Node p = GetParent();
		while (p != null) {
			if (p is CharacterBody2D cb) {
				_ownerNode = cb;
				_ownerSprite = cb.GetNodeOrNull<AnimatedSprite2D>("AnimatedSprite2D");
				break;
			}
			p = p.GetParent();
		}

		// Находим спрайт оружия
		_sprite = GetNodeOrNull<Sprite2D>("WeaponSprite") ?? 
				  GetNodeOrNull<Sprite2D>("SpriteDagger") ?? 
				  GetNodeOrNull<Sprite2D>("SpriteAxe") ??
				  GetChildren().OfType<Sprite2D>().FirstOrDefault();

		// ВАЖНО: Принудительно сбрасываем поворот всех дочерних узлов в 0.
		// Это убирает влияние "мусорных" значений из редактора Godot.
		ResetChildRotations(this);
	}

	private void ResetChildRotations(Node parent) {
		foreach (Node child in parent.GetChildren()) {
			if (child is Node2D n2d) n2d.Rotation = 0;
			ResetChildRotations(child);
		}
	}

	public void UpdateOrientation(Vector2 targetPos, bool isAttacking)
	{
		// Рассчитываем направление от стабильного центра игрока, чтобы избежать осцилляции
		Vector2 ownerPos = _ownerNode != null ? _ownerNode.GlobalPosition : GlobalPosition;
		float angleToOwner = (targetPos - ownerPos).Angle();
		
		// ГИСТЕРЕЗИС (Предотвращение дребезга)
		bool currentFlip = _ownerSprite?.FlipH ?? false;
		bool isLeft = currentFlip;
		float absAngleDeg = Mathf.Abs(Mathf.RadToDeg(angleToOwner));
		
		if (absAngleDeg > 100) isLeft = true;
		else if (absAngleDeg < 80) isLeft = false;

		// Сбрасываем поворот спрайта, когда не атакуем, чтобы он не застревал в угле анимации
		if (!isAttacking && _sprite != null) {
			_sprite.Rotation = 0;
		}

		// 1. БАЗОВЫЙ ПОВОРОТ:
		// Добавляем Mathf.Pi / 2 (90 градусов), так как спрайт оружия ближнего боя изначально смотрит вверх
		float finalOffset = isLeft ? -OrientationOffset : OrientationOffset;
		Rotation = Mathf.Pi / 2 + Mathf.DegToRad(finalOffset);

		// 2. ОТЗЕРКАЛИВАНИЕ:
		if (Mode == FlipMode.MirrorY) {
			Vector2 s = Scale;
			s.Y = isLeft ? -Mathf.Abs(s.Y) : Mathf.Abs(s.Y);
			Scale = s;
		} else if (Mode == FlipMode.MirrorX) {
			Vector2 s = Scale;
			s.X = isLeft ? -Mathf.Abs(s.X) : Mathf.Abs(s.X);
			Scale = s;
		}

		// 3. ПОВОРОТ ИГРОКА:
		if (SyncOwnerFlip && _ownerSprite != null) {
			_ownerSprite.FlipH = isLeft;
		}
	}
}
