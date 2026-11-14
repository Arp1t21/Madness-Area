using Godot;

public partial class BigDemon : Enemy
{
	protected override Color WarningLabelColor => new Color(1.0f, 0.2f, 0.05f);

	private PointLight2D _demonAuraLight;
	private float _chargeCooldown = 4.0f;
	private float _chargeTimer = 0.0f;

	public override void _Ready()
	{
		base._Ready();
		_demonAuraLight = SetupGlowLight(
			"BigDemonLight",
			new Vector2(0, -14),
			new Color(1.0f, 0.32f, 0.12f, 1.0f),
			0.95f,
			new Color(1.0f, 0.6f, 0.25f, 1.0f),
			new Color(0.95f, 0.22f, 0.06f, 0.55f),
			0.48f,
			116);
	}

	public override void _PhysicsProcess(double delta)
	{
		float dt = (float)delta;
		if (_chargeCooldown > 0) _chargeCooldown -= dt;
		if (_chargeTimer > 0) _chargeTimer -= dt;

		if (StepEnemyActionState(delta))
		{
			float playerDist = GlobalPosition.DistanceTo(_player.GlobalPosition);
			if (_chargeCooldown <= 0 && playerDist < 140.0f * World.Scale && playerDist > 38.0f * World.Scale)
			{
				_chargeTimer = 0.65f;
				_chargeCooldown = 4.5f;
			}

			float speedMult = _chargeTimer > 0 ? 1.75f : 1.0f;
			UpdateMeleeChaseVelocity(delta, 30.0f, 96.0f, speedMult, 0.12f);

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
