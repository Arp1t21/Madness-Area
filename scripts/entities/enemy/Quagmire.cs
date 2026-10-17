using Godot;

// Медлительный "болотный" моб:
// - держит вокруг себя видимую пульсирующую ауру, замедляющую игрока на SlowStrength;
// - бьет в ближнем бою как орк, но больнее и с более долгим замахом.
public partial class Quagmire : Enemy
{
	[Export] public float SlowAuraRadius = 36.0f; // в тайлах, умножается на World.Scale
	[Export] public float SlowStrength = 0.25f; // -25% скорости игрока в ауре
	[Export] public float SlowRefreshInterval = 0.3f; // как часто обновляем ауру

	private float _auraTimer = 0.0f;
	private double _time = 0.0;

	protected override Color WarningLabelColor => new Color(0.9f, 0.85f, 0.2f);

	public override void _PhysicsProcess(double delta)
	{
		_time += delta;

		if (Config == null || CurrentHealth <= 0 || _player == null || !GodotObject.IsInstanceValid(_player))
		{
			Velocity = Velocity.Lerp(Vector2.Zero, 0.1f);
			MoveAndSlide();
			return;
		}

		if (_cancelAttackCooldown > 0) _cancelAttackCooldown -= (float)delta;

		float dist = GlobalPosition.DistanceTo(_player.GlobalPosition);

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

			Vector2 separation = ComputeEnemySeparation(90.0f);
			if (separation != Vector2.Zero)
			{
				direction = (direction + separation * 0.65f).Normalized();
			}

			Velocity = Velocity.Lerp(direction * Config.Speed * World.Scale, 0.05f);

			if (_sprite != null)
			{
				_sprite.Play(WalkAnimationName);
				if (Mathf.Abs(Velocity.X) > 5.0f) _sprite.FlipH = Velocity.X < 0;
			}

			if (_cancelAttackCooldown <= 0 && dist < Config.AttackRange * World.Scale)
			{
				StartAttackPreparation();
			}
		}

		MoveAndSlide();

		// Аура замедления (не зависит от состояния атаки)
		_auraTimer -= (float)delta;
		if (_auraTimer <= 0)
		{
			_auraTimer = SlowRefreshInterval;
			if (dist <= SlowAuraRadius * World.Scale)
			{
				_player.ApplyStatusEffect(StatusEffectType.Slow, SlowRefreshInterval + 0.1f, SlowStrength);
			}
		}

		QueueRedraw();
	}

	public override void _Draw()
	{
		float radius = SlowAuraRadius * World.Scale;
		float breathe = 0.9f + Mathf.Sin((float)_time * 1.2f) * 0.1f;
		DrawCircle(Vector2.Zero, radius * breathe, new Color(0.24f, 0.19f, 0.12f, 0.28f));

		for (int i = 0; i < 9; i++)
		{
			float a1 = Mathf.Sin(i * 127.1f + 311.7f) * 43758.5453f;
			float a2 = Mathf.Sin(i * 269.5f + 183.3f) * 24634.6345f;
			float ox = (a1 - Mathf.Floor(a1) - 0.5f) * radius * 1.2f;
			float oy = (a2 - Mathf.Floor(a2) - 0.5f) * radius * 1.2f;
			float pr = radius * (0.12f + (a1 - Mathf.Floor(a1)) * 0.16f);
			var patchColor = new Color(0.30f, 0.24f, 0.14f, 0.22f);
			DrawCircle(new Vector2(ox, oy), pr, patchColor);
		}

		int bubbles = 3;
		for (int i = 0; i < bubbles; i++)
		{
			float phase = ((float)_time * 0.18f + i / (float)bubbles) % 1.0f;
			float b1 = Mathf.Sin(i * 91.7f + 47.3f) * 12543.17f;
			float ang = (b1 - Mathf.Floor(b1)) * Mathf.Tau;
			float r = radius * 0.25f + radius * 0.6f * phase;
			Vector2 pos = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang) * 0.6f) * r;
			float a = 0.16f * Mathf.Sin(phase * Mathf.Pi);
			DrawCircle(pos, 3.0f + 2.0f * phase, new Color(0.38f, 0.31f, 0.18f, a));
		}
	}

	protected override void FinishAttack()
	{
		_isPreparingAttack = false;
		if (_warningLabel != null) _warningLabel.Visible = false;

		if (_player != null && GodotObject.IsInstanceValid(_player) &&
			GlobalPosition.DistanceTo(_player.GlobalPosition) <= (Config.AttackRange + 10.0f) * World.Scale)
		{
			_player.TakeDamage(Config.Damage * DamageMultiplier, GlobalPosition, 5.0f, true);
		}

		TriggerStop(Config.StopDuration);
	}
}
