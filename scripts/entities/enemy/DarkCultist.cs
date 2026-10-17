using Godot;

public partial class DarkCultist : Enemy
{
	protected override Color WarningLabelColor => new Color(0.45f, 1.0f, 0.35f);

	private PointLight2D _cultistLight;

	public override void _Ready()
	{
		base._Ready();
		SetupCultistGlow();
	}

	private void SetupCultistGlow()
	{
		_cultistLight = new PointLight2D();
		_cultistLight.Name = "CultistLight";
		_cultistLight.Position = new Vector2(0, -8);
		_cultistLight.ShadowEnabled = false;
		_cultistLight.Color = new Color(0.45f, 0.95f, 0.42f, 1.0f);
		_cultistLight.Energy = 0.72f;

		var gradient = new Gradient();
		gradient.Colors = new Color[]
		{
			new Color(0.65f, 1.0f, 0.55f, 1.0f),
			new Color(0.3f, 0.85f, 0.35f, 0.45f),
			new Color(0.0f, 0.0f, 0.0f, 0.0f)
		};
		gradient.Offsets = new float[] { 0.0f, 0.45f, 1.0f };

		var gradTex = new GradientTexture2D();
		gradTex.Gradient = gradient;
		gradTex.Fill = GradientTexture2D.FillEnum.Radial;
		gradTex.FillFrom = new Vector2(0.5f, 0.5f);
		gradTex.FillTo = new Vector2(0.5f, 0.0f);
		gradTex.Width = 78;
		gradTex.Height = 78;

		_cultistLight.Texture = gradTex;
		AddChild(_cultistLight);
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
			float dist = GlobalPosition.DistanceTo(_player.GlobalPosition);
			Vector2 direction = Vector2.Zero;
			float currentSpeed = Config.Speed * World.Scale;

			if (dist > 135.0f * World.Scale)
			{
				direction = UpdateNavigationDirection(delta, 14.0f * World.Scale);
			}
			else if (dist < 90.0f * World.Scale)
			{
				direction = (GlobalPosition - _player.GlobalPosition).Normalized();
				currentSpeed = Config.Speed * 0.7f * World.Scale;
			}
			else
			{
				direction = GetStrafeDirectionAroundPlayer();
				currentSpeed = Config.Speed * 0.6f * World.Scale;
			}

			Vector2 separation = ComputeEnemySeparation(90.0f);
			if (separation != Vector2.Zero)
			{
				direction = (direction + separation * 0.75f).Normalized();
			}

			if (direction != Vector2.Zero)
			{
				direction = ApplyAntiStuck(direction, delta, () => _navigationPath?.Clear());
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
				if (dist <= 145.0f * World.Scale)
				{
					StartAttackPreparation();
				}
			}
		}

		MoveAndSlide();
	}
}
