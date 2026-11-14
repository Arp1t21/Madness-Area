using Godot;

public partial class OrcWarrior : Enemy
{
	protected override Color WarningLabelColor => new Color(1.0f, 0.1f, 0.1f);

	public override void _PhysicsProcess(double delta)
	{
		if (StepEnemyActionState(delta))
		{
			UpdateMeleeChaseVelocity(delta, 36.0f, 84.0f, 1.0f, 0.1f, 66.0f);

			float playerDist = GlobalPosition.DistanceTo(_player.GlobalPosition);
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
