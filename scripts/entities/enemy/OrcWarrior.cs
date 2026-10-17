using Godot;

public partial class OrcWarrior : Enemy
{
	protected override Color WarningLabelColor => new Color(1.0f, 0.1f, 0.1f);

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
			Vector2 direction = UpdateNavigationDirection(delta, 36.0f);
			direction = ApplyAntiStuck(direction, delta, () => _navigationPath.Clear());

			Vector2 separation = ComputeEnemySeparation(84.0f);
			if (separation != Vector2.Zero)
			{
				direction = (direction + separation * 0.65f).Normalized();
			}

			float playerDist = GlobalPosition.DistanceTo(_player.GlobalPosition);
			float playerSeparationRadius = 66.0f;
			if (playerDist < playerSeparationRadius && playerDist > 0.1f)
			{
				Vector2 pushFromPlayer = (GlobalPosition - _player.GlobalPosition).Normalized() * (playerSeparationRadius - playerDist);
				direction = (direction + pushFromPlayer * 0.35f).Normalized();
			}

			Vector2 targetVelocity = direction * Config.Speed * World.Scale;
			Velocity = Velocity.Lerp(targetVelocity, 0.1f);

			if (_sprite != null)
			{
				if (Mathf.Abs(Velocity.X) > 5.0f)
				{
					_sprite.FlipH = Velocity.X < 0;
				}
				_sprite.Play(WalkAnimationName);
			}

			if (_cancelAttackCooldown <= 0 && _attackController != null && _attackController.CanAttack())
			{
				if (playerDist < Config.AttackRange * World.Scale)
				{
					StartAttackPreparation();
				}
			}
		}

		MoveAndSlide();
	}
}
