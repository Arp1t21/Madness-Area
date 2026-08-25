using Godot;
using System;

public partial class Projectile : Node2D
{
	public float Damage = 10;
	public float Speed = 300.0f;
	public float Range = 500.0f;
	public Vector2 Direction = Vector2.Right;
	public bool IsPlayerOwned = true;
	public bool IsPiercing = false; // Если false, пуля исчезнет после первого попадания
	public float KnockbackForce = 5.0f;

	private Vector2 _startPos;
	private readonly System.Collections.Generic.HashSet<ulong> _hitTargets = new System.Collections.Generic.HashSet<ulong>();

	public override void _Ready()
	{
		Scale = new Vector2(3.0f, 3.0f);
		_startPos = GlobalPosition;
		
		var area = GetNodeOrNull<Area2D>("Area2D") ?? GetNodeOrNull<Area2D>(".");
		if (area != null) {
			area.AreaEntered += _on_area_2d_area_entered;
			area.BodyEntered += _on_body_entered; // Добавляем обнаружение тел (CharacterBody2D)
		} else {
			GD.PushWarning("Projectile: No Area2D found to detect collisions!");
		}

		SetupTrail();
	}

	// Добавляет шлейф за пулей, если у сцены нет своих частиц (напр. у ghost_bullet они есть)
	private void SetupTrail()
	{
		foreach (var child in GetChildren())
		{
			if (child is CpuParticles2D) return; // трейл уже есть в сцене
		}

		var trail = new CpuParticles2D();
		trail.Name = "Trail";
		trail.Amount = 24;
		trail.Lifetime = 0.22f;
		trail.LocalCoords = false; // частицы остаются позади, а не летят вместе с пулей
		trail.Gravity = Vector2.Zero;
		trail.Direction = -Direction;
		trail.Spread = 12f;
		trail.InitialVelocityMin = 5f;
		trail.InitialVelocityMax = 15f;
		trail.ScaleAmountMin = 1.5f;
		trail.ScaleAmountMax = 3.0f;

		var gradient = new Gradient();
		if (IsPlayerOwned) {
			gradient.Colors = new Color[] {
				new Color(1.0f, 0.85f, 0.4f, 0.55f),
				new Color(0.9f, 0.6f, 0.1f, 0.0f)
			};
		} else {
			gradient.Colors = new Color[] {
				new Color(0.7f, 0.9f, 1.0f, 0.5f),
				new Color(0.3f, 0.5f, 0.9f, 0.0f)
			};
		}
		gradient.Offsets = new float[] { 0.0f, 1.0f };
		trail.ColorRamp = gradient;

		AddChild(trail);
	}

	public override void _PhysicsProcess(double delta)
	{
		Position += Direction * Speed * (float)delta;

		// Удаляем если улетела слишком далеко
		if (GlobalPosition.DistanceTo(_startPos) >= Range)
		{
			QueueFree();
		}
	}

	private void _on_body_entered(Node2D body)
	{
		HandleCollision(body);
	}

	private void _on_area_2d_area_entered(Area2D area)
	{
		HandleCollision(area.GetParent());
	}

	private bool _isDestroyed = false;

	private void HandleCollision(Node hitNode)
	{
		if (_isDestroyed || hitNode == null || hitNode.IsQueuedForDeletion()) return;

		// Если пуля игрока попала во врага
		// (типизированный вызов: Call() по имени не видит C#-дефолтных значений параметров)
		if (IsPlayerOwned && hitNode is Enemy enemy && !_hitTargets.Contains(hitNode.GetInstanceId()))
		{
			_hitTargets.Add(hitNode.GetInstanceId());
			enemy.TakeDamage(Damage, GlobalPosition, KnockbackForce, false);

			if (!IsPiercing) {
				_isDestroyed = true;
				QueueFree();
			}
		}
		// Если пуля врага попала в игрока
		else if (!IsPlayerOwned && hitNode is Player player && !_hitTargets.Contains(hitNode.GetInstanceId()))
		{
			_hitTargets.Add(hitNode.GetInstanceId());
			player.TakeDamage(Damage, GlobalPosition, KnockbackForce, false);

			if (!IsPiercing) {
				_isDestroyed = true;
				QueueFree();
			}
		}
		// Если пуля попала в стену или препятствие (дверь)
		else if (hitNode is TileMapLayer || 
				 hitNode is StaticBody2D || 
				 hitNode.GetType().Name.Contains("TileMap") || 
				 hitNode.GetType().Name.Contains("StaticBody") ||
				 hitNode.Name.ToString().Contains("Wall") || 
				 hitNode.Name.ToString().Contains("Floor"))
		{
			_isDestroyed = true;
			QueueFree();
		}
	}

}
