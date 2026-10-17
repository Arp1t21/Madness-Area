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
			direction = ApplyAntiStuck(direction, delta, () => _navigationPath?.Clear());

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
		float t = (float)_time;
		float breathe = 0.96f + Mathf.Sin(t * 1.6f) * 0.04f;
		float curRadius = radius * breathe;

		// 1. Глубокая темно-изумрудная подложка болотной трясины (контрастирует с коричневым полом)
		DrawCircle(Vector2.Zero, curRadius, new Color(0.04f, 0.20f, 0.16f, 0.34f));
		DrawCircle(Vector2.Zero, curRadius * 0.72f, new Color(0.06f, 0.28f, 0.20f, 0.22f));

		// 2. Медленно вращающиеся пятна ядовито-болотного тумана внутри зоны
		for (int i = 0; i < 8; i++)
		{
			float seed1 = Mathf.Sin(i * 127.1f + 311.7f) * 43758.5453f;
			float seed2 = Mathf.Sin(i * 269.5f + 183.3f) * 24634.6345f;
			float f1 = seed1 - Mathf.Floor(seed1);
			float f2 = seed2 - Mathf.Floor(seed2);

			float baseAngle = f1 * Mathf.Tau + t * (i % 2 == 0 ? 0.22f : -0.18f);
			float distFactor = 0.22f + f2 * 0.48f;
			Vector2 patchPos = new Vector2(Mathf.Cos(baseAngle), Mathf.Sin(baseAngle)) * (curRadius * distFactor);
			float patchRadius = curRadius * (0.14f + f1 * 0.12f);

			Color patchColor = (i % 2 == 0)
				? new Color(0.10f, 0.46f, 0.30f, 0.20f)
				: new Color(0.18f, 0.58f, 0.34f, 0.16f);
			DrawCircle(patchPos, patchRadius, patchColor);
		}

		// 3. Расходящиеся волновые кольца от центра и четкий контур границы ауры
		float ripplePhase = (t * 0.45f) % 1.0f;
		float rippleRadius = curRadius * (0.25f + 0.73f * ripplePhase);
		float rippleAlpha = (1.0f - ripplePhase) * 0.38f;
		DrawArc(Vector2.Zero, rippleRadius, 0f, Mathf.Tau, 48, new Color(0.30f, 0.92f, 0.58f, rippleAlpha), 1.4f);

		// Четкое двойное кольцо по краю зоны замедления
		float rimPulse = 0.55f + 0.20f * Mathf.Sin(t * 2.8f);
		DrawArc(Vector2.Zero, curRadius, 0f, Mathf.Tau, 64, new Color(0.12f, 0.55f, 0.38f, 0.42f), 3.2f);
		DrawArc(Vector2.Zero, curRadius, 0f, Mathf.Tau, 64, new Color(0.38f, 1.0f, 0.66f, rimPulse), 1.4f);

		// 4. Всплывающие светящиеся болотные пузырьки с бликом
		int bubbles = 6;
		for (int i = 0; i < bubbles; i++)
		{
			float phase = (t * 0.32f + i / (float)bubbles) % 1.0f;
			float b1 = Mathf.Sin(i * 91.7f + 47.3f) * 12543.17f;
			float ang = (b1 - Mathf.Floor(b1)) * Mathf.Tau + Mathf.Sin(t * 0.7f + i) * 0.25f;
			float r = curRadius * (0.18f + 0.68f * ((i * 0.37f) % 1.0f));
			Vector2 pos = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang) * 0.85f) * r + new Vector2(0, -phase * 8.0f);

			float alpha = Mathf.Sin(phase * Mathf.Pi);
			float bRadius = (2.2f + 2.4f * phase) * (World.Scale / 3.0f);
			DrawCircle(pos, bRadius, new Color(0.22f, 0.82f, 0.48f, 0.38f * alpha));
			DrawArc(pos, bRadius, 0f, Mathf.Tau, 16, new Color(0.62f, 1.0f, 0.78f, 0.65f * alpha), 1.0f);
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
