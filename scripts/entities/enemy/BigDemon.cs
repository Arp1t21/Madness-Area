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
		SetupDemonAuraLight();
	}

	private void SetupDemonAuraLight()
	{
		_demonAuraLight = new PointLight2D();
		_demonAuraLight.Name = "BigDemonLight";
		_demonAuraLight.Position = new Vector2(0, -14);
		_demonAuraLight.ShadowEnabled = false;
		_demonAuraLight.Color = new Color(1.0f, 0.32f, 0.12f, 1.0f);
		_demonAuraLight.Energy = 0.95f;

		var gradient = new Gradient();
		gradient.Colors = new Color[]
		{
			new Color(1.0f, 0.6f, 0.25f, 1.0f),
			new Color(0.95f, 0.22f, 0.06f, 0.55f),
			new Color(0.0f, 0.0f, 0.0f, 0.0f)
		};
		gradient.Offsets = new float[] { 0.0f, 0.48f, 1.0f };

		var gradTex = new GradientTexture2D();
		gradTex.Gradient = gradient;
		gradTex.Fill = GradientTexture2D.FillEnum.Radial;
		gradTex.FillFrom = new Vector2(0.5f, 0.5f);
		gradTex.FillTo = new Vector2(0.5f, 0.0f);
		gradTex.Width = 116;
		gradTex.Height = 116;

		_demonAuraLight.Texture = gradTex;
		AddChild(_demonAuraLight);
	}

	public override void _PhysicsProcess(double delta)
	{
		if (_player == null || !GodotObject.IsInstanceValid(_player) || Config == null || CurrentHealth <= 0)
		{
			Velocity = Velocity.Lerp(Vector2.Zero, 0.1f);
			MoveAndSlide();
			return;
		}

		float dt = (float)delta;
		if (_cancelAttackCooldown > 0) _cancelAttackCooldown -= dt;
		if (_chargeCooldown > 0) _chargeCooldown -= dt;
		if (_chargeTimer > 0) _chargeTimer -= dt;

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
			Vector2 direction = UpdateNavigationDirection(delta, 30.0f);
			direction = ApplyAntiStuck(direction, delta, () => _navigationPath?.Clear());

			Vector2 separation = ComputeEnemySeparation(96.0f);
			if (separation != Vector2.Zero)
			{
				direction = (direction + separation * 0.65f).Normalized();
			}

			float playerDist = GlobalPosition.DistanceTo(_player.GlobalPosition);
			if (_chargeCooldown <= 0 && playerDist < 140.0f * World.Scale && playerDist > 38.0f * World.Scale)
			{
				_chargeTimer = 0.65f;
				_chargeCooldown = 4.5f;
			}

			float speedMult = _chargeTimer > 0 ? 1.75f : 1.0f;
			Vector2 targetVelocity = direction * Config.Speed * speedMult * World.Scale;
			Velocity = Velocity.Lerp(targetVelocity, 0.12f);

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
