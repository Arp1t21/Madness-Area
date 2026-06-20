using Godot;
using System;

public partial class Coin : Node2D
{
	private bool _isBeingPickedUp = false;
	private bool _isAnimatingSpawn = true;
	private Node2D _targetPlayer;
	private float _speed = 200.0f;

	public override void _Ready()
	{
		// Подключаем сигнал из Area2D
		var area = GetNode<Area2D>("Area2D");
		area.AreaEntered += _on_area_2d_area_entered;

		var sprite = GetNodeOrNull<AnimatedSprite2D>("AnimatedSprite2D");
		if (sprite != null) sprite.Play("idle");

		PlaySpawnAnimation();
	}

	private void PlaySpawnAnimation()
	{
		_isAnimatingSpawn = true;
		
		Vector2 startPos = GlobalPosition;
		Vector2 jumpPos = startPos + new Vector2(0, -20); // Прыжок вверх на 20 пикселей
		Vector2 landPos = startPos + new Vector2((float)GD.RandRange(-15, 15), 5); // Приземление со случайным разбросом

		var tween = CreateTween();
		// Вверх
		tween.TweenProperty(this, "global_position", jumpPos, 0.25f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
		// Вниз
		tween.TweenProperty(this, "global_position", landPos, 0.25f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
		
		tween.Finished += () => _isAnimatingSpawn = false;
	}

	public override void _Process(double delta)
	{
		if (_isAnimatingSpawn) return;

		if (_isBeingPickedUp && IsInstanceValid(_targetPlayer))
		{
			// Плавный полет к игроку
			Vector2 direction = (_targetPlayer.GlobalPosition - GlobalPosition).Normalized();
			GlobalPosition += direction * _speed * (float)delta;
			
			// Увеличиваем скорость для эффекта притяжения
			_speed += 500.0f * (float)delta;

			// Если долетели совсем близко - удаляем
			if (GlobalPosition.DistanceTo(_targetPlayer.GlobalPosition) < 5.0f)
			{
				if (_targetPlayer is Player p) {
					p.CoinCount++;
					GD.Print($"Монета собрана! Всего: {p.CoinCount}");
				}
				QueueFree();
			}
		}
	}

	private void _on_area_2d_area_entered(Area2D area)
	{
		// Проверяем, что это Area2D игрока (на 4-м слое / 8 значении маски)
		if (!_isBeingPickedUp && area.GetParent() is Player player)
		{
			_targetPlayer = player;
			_isBeingPickedUp = true;

			// Эффект уменьшения на 0.8
			var tween = CreateTween();
			tween.TweenProperty(this, "scale", Scale * 0.8f, 0.3f);
		}
	}
}
