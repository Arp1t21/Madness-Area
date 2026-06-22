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

	private Vector2 _startPos;
	private readonly System.Collections.Generic.HashSet<ulong> _hitTargets = new System.Collections.Generic.HashSet<ulong>();

	public override void _Ready()
	{
		_startPos = GlobalPosition;
		
		var area = GetNodeOrNull<Area2D>("Area2D") ?? GetNodeOrNull<Area2D>(".");
		if (area != null) {
			area.AreaEntered += _on_area_2d_area_entered;
			area.BodyEntered += _on_body_entered; // Добавляем обнаружение тел (CharacterBody2D)
		} else {
			GD.PushWarning("Projectile: No Area2D found to detect collisions!");
		}
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
		if (IsPlayerOwned && hitNode.IsInGroup("enemy"))
		{
			ulong targetId = hitNode.GetInstanceId();
			if (_hitTargets.Contains(targetId)) return;
			_hitTargets.Add(targetId);

			if (hitNode.HasMethod("TakeDamage"))
			{
				hitNode.Call("TakeDamage", Damage, GlobalPosition, 5.0f, false);
			}

			if (!IsPiercing) {
				_isDestroyed = true;
				QueueFree();
			}
		}
		// Если пуля врага попала в игрока
		else if (!IsPlayerOwned && hitNode.IsInGroup("Player"))
		{
			ulong targetId = hitNode.GetInstanceId();
			if (_hitTargets.Contains(targetId)) return;
			_hitTargets.Add(targetId);

			if (hitNode.HasMethod("TakeDamage"))
			{
				hitNode.Call("TakeDamage", Damage, GlobalPosition, 2.0f);
			}

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
