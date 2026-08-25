using Godot;
using System;

public partial class Coin : Node2D
{
	private bool _isBeingPickedUp = false;
	private bool _isAnimatingSpawn = true;
	private Node2D _targetPlayer;
	private float _speed = 600.0f;
	// Подбор не мгновенный: даже если игрок стоит на монете, она "дозревает" 0.6с
	private float _pickupDelay = 0.6f;

	public override void _Ready()
	{
		Scale = new Vector2(3.0f, 3.0f);
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
		Vector2 jumpPos = startPos + new Vector2(0, -20 * 3f); // Прыжок вверх на 60 пикселей (20 * 3)
		Vector2 landPos = startPos + new Vector2((float)GD.RandRange(-15, 15) * 3f, 5 * 3f); // Приземление со случайным разбросом (масштабировано 3x)

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

		if (_pickupDelay > 0)
		{
			_pickupDelay -= (float)delta;
			// Если игрок уже стоит на монете — подберём её сразу по истечении задержки
			if (_pickupDelay <= 0) {
				var nearby = GetTree().GetFirstNodeInGroup("Player") as Node2D;
				if (nearby != null && GlobalPosition.DistanceTo(nearby.GlobalPosition) < 15.0f * World.Scale) {
					StartPickup(nearby);
				}
			}
			return;
		}

		if (_isBeingPickedUp && IsInstanceValid(_targetPlayer))
		{
			// Плавный полет к игроку
			Vector2 direction = (_targetPlayer.GlobalPosition - GlobalPosition).Normalized();
			GlobalPosition += direction * _speed * (float)delta;
			
			// Увеличиваем скорость для эффекта притяжения
			_speed += 1500.0f * (float)delta;

			// Если долетели совсем близко - удаляем
			if (GlobalPosition.DistanceTo(_targetPlayer.GlobalPosition) < 15.0f)
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
		if (!_isBeingPickedUp && _pickupDelay <= 0 && area.GetParent() is Player player)
		{
			StartPickup(player);
		}
	}

	private void StartPickup(Node2D player)
	{
		_targetPlayer = player;
		_isBeingPickedUp = true;

		// Эффект уменьшения на 0.8
		var tween = CreateTween();
		tween.TweenProperty(this, "scale", Scale * 0.8f, 0.3f);
	}
}
