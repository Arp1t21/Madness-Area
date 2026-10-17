using Godot;

public partial class Ghost : Enemy
{
	protected override Color WarningLabelColor => new Color(0.2f, 0.6f, 1.0f);

	private PointLight2D _ghostLight;
	private float _glowPulseTime = 0.0f;

	public override void _Ready()
	{
		base._Ready();
		if (_sprite != null)
		{
			_sprite.Material = new CanvasItemMaterial
			{
				LightMode = CanvasItemMaterial.LightModeEnum.Unshaded
			};
			_sprite.SelfModulate = new Color(1.25f, 1.38f, 1.55f, 1.0f);
		}
		SetupGhostGlow();
	}

	private void SetupGhostGlow()
	{
		_ghostLight = new PointLight2D();
		_ghostLight.Name = "GhostSpectralLight";
		_ghostLight.Position = new Vector2(0, -6);
		_ghostLight.ShadowEnabled = false;
		_ghostLight.Color = new Color(0.55f, 0.92f, 1.0f, 1.0f);
		_ghostLight.Energy = 1.45f;

		var gradient = new Gradient();
		gradient.Colors = new Color[]
		{
			new Color(0.95f, 0.99f, 1.0f, 1.0f),
			new Color(0.5f, 0.88f, 1.0f, 0.72f),
			new Color(0.0f, 0.0f, 0.0f, 0.0f)
		};
		gradient.Offsets = new float[] { 0.0f, 0.50f, 1.0f };

		var gradTex = new GradientTexture2D();
		gradTex.Gradient = gradient;
		gradTex.Fill = GradientTexture2D.FillEnum.Radial;
		gradTex.FillFrom = new Vector2(0.5f, 0.5f);
		gradTex.FillTo = new Vector2(0.5f, 0.0f);
		gradTex.Width = 70;
		gradTex.Height = 70;

		_ghostLight.Texture = gradTex;
		AddChild(_ghostLight);
	}

	public override void _PhysicsProcess(double delta)
	{
		if (_ghostLight != null && GodotObject.IsInstanceValid(_ghostLight))
		{
			_glowPulseTime += (float)delta * 3.5f;
			_ghostLight.Energy = 1.40f + Mathf.Sin(_glowPulseTime) * 0.20f;
		}

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

			if (dist > 150.0f * World.Scale)
			{
				direction = UpdateNavigationDirection(delta, 12.0f * World.Scale);
			}
			else if (dist < 105.0f * World.Scale)
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
				if (dist <= 150.0f * World.Scale)
				{
					StartAttackPreparation();
				}
			}
		}

		MoveAndSlide();
	}
}
