using Godot;

public partial class Chort : Enemy
{
	protected override Color WarningLabelColor => new Color(1.0f, 0.25f, 0.1f);

	private PointLight2D _demonLight;

	public override void _Ready()
	{
		base._Ready();
		SetupDemonGlow();
	}

	private void SetupDemonGlow()
	{
		_demonLight = new PointLight2D();
		_demonLight.Name = "ChortLight";
		_demonLight.Position = new Vector2(0, -8);
		_demonLight.ShadowEnabled = false;
		_demonLight.Color = new Color(1.0f, 0.35f, 0.18f, 1.0f);
		_demonLight.Energy = 0.65f;

		var gradient = new Gradient();
		gradient.Colors = new Color[]
		{
			new Color(1.0f, 0.55f, 0.25f, 1.0f),
			new Color(0.95f, 0.2f, 0.08f, 0.45f),
			new Color(0.0f, 0.0f, 0.0f, 0.0f)
		};
		gradient.Offsets = new float[] { 0.0f, 0.45f, 1.0f };

		var gradTex = new GradientTexture2D();
		gradTex.Gradient = gradient;
		gradTex.Fill = GradientTexture2D.FillEnum.Radial;
		gradTex.FillFrom = new Vector2(0.5f, 0.5f);
		gradTex.FillTo = new Vector2(0.5f, 0.0f);
		gradTex.Width = 72;
		gradTex.Height = 72;

		_demonLight.Texture = gradTex;
		AddChild(_demonLight);
	}

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
			Vector2 direction = UpdateNavigationDirection(delta, 42.0f);
			direction = ApplyAntiStuck(direction, delta, () => _navigationPath?.Clear());

			Vector2 separation = ComputeEnemySeparation(76.0f);
			if (separation != Vector2.Zero)
			{
				direction = (direction + separation * 0.65f).Normalized();
			}

			float playerDist = GlobalPosition.DistanceTo(_player.GlobalPosition);
			float speedMult = (playerDist < 120.0f * World.Scale && playerDist > 30.0f * World.Scale) ? 1.25f : 1.0f;

			Vector2 targetVelocity = direction * Config.Speed * speedMult * World.Scale;
			Velocity = Velocity.Lerp(targetVelocity, 0.14f);

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
