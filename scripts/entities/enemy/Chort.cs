using Godot;

public partial class Chort : Enemy
{
	protected override Color WarningLabelColor => new Color(1.0f, 0.25f, 0.1f);

	private PointLight2D _demonLight;

	public override void _Ready()
	{
		base._Ready();
		_demonLight = SetupGlowLight(
			"ChortLight",
			new Vector2(0, -8),
			new Color(1.0f, 0.35f, 0.18f, 1.0f),
			0.65f,
			new Color(1.0f, 0.55f, 0.25f, 1.0f),
			new Color(0.95f, 0.2f, 0.08f, 0.45f),
			0.45f,
			72);
	}

	protected override void TickEnemyAI(double delta)
	{
		if (StepEnemyActionState(delta))
		{
			float playerDist = GlobalPosition.DistanceTo(_player.GlobalPosition);
			float speedMult = (playerDist < 120.0f && playerDist > 30.0f) ? 1.25f : 1.0f;
			UpdateMeleeChaseVelocity(delta, 14.0f, 25.0f, speedMult, 0.14f);

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
