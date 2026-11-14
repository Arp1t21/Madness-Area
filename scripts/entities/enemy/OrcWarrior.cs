using Godot;

public partial class OrcWarrior : Enemy
{
	protected override Color WarningLabelColor => new Color(1.0f, 0.1f, 0.1f);

	protected override void TickEnemyAI(double delta)
	{
		if (StepEnemyActionState(delta))
		{
			UpdateMeleeChaseVelocity(delta, 12.0f, 28.0f, 1.0f, 0.1f, 22.0f);

			float playerDist = GlobalPosition.DistanceTo(_player.GlobalPosition);
			if (_cancelAttackCooldown <= 0 && _attackController != null && _attackController.CanAttack())
			{
				if (playerDist < Config.AttackRange)
				{
					StartAttackPreparation();
				}
			}
		}

		MoveAndSlide();
	}
}
