using Godot;

public partial class Archer : Enemy
{
	protected override Color WarningLabelColor => new Color(1.0f, 0.5f, 0.1f);

	public override void _PhysicsProcess(double delta)
	{
		if (_player == null || !GodotObject.IsInstanceValid(_player) || Config == null || CurrentHealth <= 0)
		{
			Velocity = Velocity.Lerp(Vector2.Zero, 0.1f);
			MoveAndSlide();
			return;
		}

		if (_cancelAttackCooldown > 0)
		{
			_cancelAttackCooldown -= (float)delta;
		}

		if (_isPreparingAttack)
		{
			HandlePreparingState(delta);
			Velocity = Velocity.Lerp(Vector2.Zero, 0.1f);
		}
		else if (_isStopping)
		{
			Velocity = Velocity.Lerp(Vector2.Zero, 0.1f);
		}
		else
		{
			float dist = GlobalPosition.DistanceTo(_player.GlobalPosition);
			Vector2 direction = Vector2.Zero;
			float currentSpeed = Config.Speed * World.Scale;

			if (dist > 120.0f * World.Scale)
			{
				direction = UpdateNavigationDirection(delta, 12.0f * World.Scale);
			}
			else if (dist < 85.0f * World.Scale)
			{
				direction = (GlobalPosition - _player.GlobalPosition).Normalized();
				currentSpeed = Config.Speed * 0.65f * World.Scale;
			}
			else
			{
				direction = GetStrafeDirectionAroundPlayer();
				currentSpeed = Config.Speed * 0.55f * World.Scale;
			}

			Vector2 separation = ComputeEnemySeparation(90.0f);
			if (separation != Vector2.Zero)
			{
				direction = (direction + separation * 0.75f).Normalized();
			}

			if (direction != Vector2.Zero)
			{
				direction = ApplyAntiStuck(direction, delta, () => _navigationPath.Clear());
			}

			Vector2 targetVelocity = direction * currentSpeed;
			Velocity = Velocity.Lerp(targetVelocity, 0.1f);

			if (_sprite != null)
			{
				_sprite.Play(Velocity.Length() > 5.0f ? WalkAnimationName : IdleAnimationName);
				_sprite.FlipH = _player.GlobalPosition.X < GlobalPosition.X;
			}

			if (_cancelAttackCooldown <= 0 && _attackController != null && _attackController.CanAttack())
			{
				if (dist <= 120.0f * World.Scale)
				{
					StartAttackPreparation();
				}
			}
		}

		MoveAndSlide();
	}
}
