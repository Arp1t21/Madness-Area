using Godot;
using System;

public partial class RangedWeaponVisual : Node2D
{
	[Export] public float OrientationOffset = 0.0f;
	[Export] public bool UseSmartFlip = true;
	
	private Node2D _visualContainer;
	private AnimatedSprite2D _ownerSprite;
	private Node2D _ownerNode;

	public override void _Ready()
	{
		_visualContainer = this; // Скрипт вешается на корень визуального префаба
		// Ищем спрайт игрока/врага для синхронизации FlipH
		Node p = GetParent();
		while (p != null) {
			if (p is CharacterBody2D cb) {
				_ownerNode = cb;
				_ownerSprite = cb.GetNodeOrNull<AnimatedSprite2D>("AnimatedSprite2D");
				break;
			}
			p = p.GetParent();
		}
	}

	public void UpdateOrientation(Vector2 targetPos, bool isAttacking)
	{
		// Рассчитываем направление от стабильного центра игрока, чтобы избежать осцилляции
		Vector2 ownerPos = _ownerNode != null ? _ownerNode.GlobalPosition : GlobalPosition;
		float angleToOwner = (targetPos - ownerPos).Angle();

		bool currentFlip = _ownerSprite?.FlipH ?? false;
		bool isLeft = currentFlip;

		if (UseSmartFlip) {
			// ГИСТЕРЕЗИС (Предотвращение дребезга)
			float absAngleDeg = Mathf.Abs(Mathf.RadToDeg(angleToOwner));
			
			if (absAngleDeg > 100) isLeft = true;
			else if (absAngleDeg < 80) isLeft = false;
			
			// Зеркалим по Y, чтобы пушка не была вверх ногами
			Vector2 s = Scale;
			s.Y = isLeft ? -Mathf.Abs(s.Y) : Mathf.Abs(s.Y);
			Scale = s;

			if (_ownerSprite != null) _ownerSprite.FlipH = isLeft;
		}

		// ПОВОРОТ: локальный офсет с учетом отзеркаливания
		float finalOffset = (UseSmartFlip && isLeft) ? -OrientationOffset : OrientationOffset;
		Rotation = Mathf.DegToRad(finalOffset);
	}
}
