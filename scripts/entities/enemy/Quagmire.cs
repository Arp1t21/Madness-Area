using Godot;

// Медлительный "болотный" моб:
// - держит вокруг себя видимую пульсирующую ауру густой грязи, замедляющую игрока на SlowStrength;
// - бьет в ближнем бою, но с умеренным отталкиванием (Knockback 2.5).
public partial class Quagmire : Enemy
{
	[Export] public float SlowAuraRadius = 19.0f; // в пикселях 1x-мира (~1.2 тайла радиус)
	[Export] public float SlowStrength = 0.25f; // -25% скорости игрока в ауре
	[Export] public float SlowRefreshInterval = 0.3f; // как часто обновляем ауру

	private float _auraTimer = 0.0f;
	public double AuraTime { get; private set; } = 0.0;
	private QuagmireAuraVisual _auraVisual;

	protected override Color WarningLabelColor => new Color(0.9f, 0.85f, 0.2f);

	public override void _Ready()
	{
		base._Ready();

		// Создаем отдельный узел для отрисовки грязи на полу:
		// ZIndex = 1 (ZAsRelative = false), чтобы грязь рисовалась строго под ногами всех персонажей (герой и враги имеют ZIndex = 2)
		_auraVisual = new QuagmireAuraVisual(this);
		_auraVisual.ZAsRelative = false;
		_auraVisual.ZIndex = 1;
		AddChild(_auraVisual);
	}

	private float _bossSlamCooldown = 4.5f;
	private bool _inSlamTelegraph = false;
	private float _slamTelegraphTimer = 0.0f;
	private const float BossSlamRadius = 62.0f;

	protected override void UpdateEnemyClientVisualTick(double delta)
	{
		AuraTime += delta;
		_auraVisual?.QueueRedraw();
	}

	protected override void TickEnemyAI(double delta)
	{
		float dt = (float)delta;
		if (_bossSlamCooldown > 0.0f) _bossSlamCooldown -= dt;

		if (IsBoss && _inSlamTelegraph)
		{
			Velocity = Velocity.Lerp(Vector2.Zero, 0.25f);
			MoveAndSlide();
			_slamTelegraphTimer -= dt;
			if (_slamTelegraphTimer <= 0.0f)
			{
				ExecuteBossToxicSlam();
			}
			return;
		}

		if (!StepEnemyActionState(delta))
		{
			MoveAndSlide();
			if (Config != null && CurrentHealth > 0)
			{
				UpdateSlowAura(dt);
			}
			return;
		}

		float dist = GlobalPosition.DistanceTo(_player.GlobalPosition);
		if (IsBoss && _bossSlamCooldown <= 0.0f && dist <= 135.0f)
		{
			_inSlamTelegraph = true;
			_slamTelegraphTimer = 0.78f;
			_bossSlamCooldown = 5.4f;
			BroadcastBossTelegraphCircle(GlobalPosition, BossSlamRadius, _slamTelegraphTimer, new Color(0.82f, 0.92f, 0.22f));
			PlayAnimationSafe(IdleAnimationName);
			Velocity = Vector2.Zero;
			MoveAndSlide();
			return;
		}

		UpdateMeleeChaseVelocity(delta, 12.0f, 30.0f, 1.0f, 0.05f);

		if (_cancelAttackCooldown <= 0 && dist < Config.AttackRange)
		{
			StartAttackPreparation();
		}

		MoveAndSlide();
		UpdateSlowAura(dt);
	}

	private void ExecuteBossToxicSlam()
	{
		_inSlamTelegraph = false;
		if (CurrentHealth <= 0 || _hasDied) return;

		BroadcastBossSlamVfx(GlobalPosition, BossSlamRadius, new Color(0.55f, 0.92f, 0.25f, 0.92f), 0.28f);

		foreach (Player target in NetworkManager.GetAlivePlayers(GetTree()))
		{
			if (GlobalPosition.DistanceTo(target.GlobalPosition) <= BossSlamRadius)
			{
				target.TakeDamage(Config.Damage * DamageMultiplier * 1.15f, GlobalPosition, 12.0f, true);
				target.ApplyStatusEffect(StatusEffectType.Poison, 3.0f, 3.0f);
				target.ApplyStatusEffect(StatusEffectType.Slow, 2.0f, 0.35f);
			}
		}

		RefreshNearestTarget();
		if (_player != null && GodotObject.IsInstanceValid(_player) && !_player.IsDead)
		{
			float baseAngle = (_player.GlobalPosition - GlobalPosition).Angle();
			BroadcastBossOrbs(
				GlobalPosition,
				10.0f,
				3,
				baseAngle - 0.38f,
				0.38f,
				108.0f,
				180.0f,
				Mathf.Max(5.0f, Config.Damage * DamageMultiplier * 0.55f),
				new Color(0.52f, 0.96f, 0.28f, 1.0f));
		}
	}

	public float GetEffectiveAuraRadius() => IsBoss ? SlowAuraRadius * 1.25f : SlowAuraRadius;

	private void UpdateSlowAura(float dt)
	{
		_auraTimer -= dt;
		if (_auraTimer <= 0)
		{
			_auraTimer = SlowRefreshInterval;
			float radius = GetEffectiveAuraRadius();
			foreach (Player target in NetworkManager.GetAlivePlayers(GetTree()))
			{
				if (GlobalPosition.DistanceTo(target.GlobalPosition) <= radius)
				{
					target.ApplyStatusEffect(StatusEffectType.Slow, SlowRefreshInterval + 0.1f, SlowStrength);
				}
			}
		}
	}

	protected override void FinishAttack()
	{
		_isPreparingAttack = false;
		if (_warningLabel != null) _warningLabel.Visible = false;

		if (Config != null)
		{
			foreach (Player target in NetworkManager.GetAlivePlayers(GetTree()))
			{
				if (GlobalPosition.DistanceTo(target.GlobalPosition) <= Config.AttackRange + 10.0f)
				{
					// Knockback снижен с 5.0f до 2.5f (в диапазоне 0-4 для всех врагов кроме призрака)
					target.TakeDamage(Config.Damage * DamageMultiplier, GlobalPosition, 2.5f, true);
					target.ApplyStatusEffect(StatusEffectType.Poison, 1.6f, 2.0f);
					ApplyEliteOnHitToPlayer(target);
				}
			}
			TriggerStop(Config.StopDuration);
		}
	}

	/// <summary>
	/// Отдельный узел для отрисовки трясины под ногами персонажей (ZIndex = 1).
	/// Выполнен в виде густой бурлящей грязи со светящимся золотисто-янтарным ореолом,
	/// благодаря чему не сливается с полом и не выглядит как ядовитая зелень.
	/// </summary>
	private partial class QuagmireAuraVisual : Node2D
	{
		private readonly Quagmire _owner;

		public QuagmireAuraVisual(Quagmire owner)
		{
			_owner = owner;
		}

		public override void _Draw()
		{
			if (_owner == null || !GodotObject.IsInstanceValid(_owner)) return;

			float invScale = 1.0f / Mathf.Max(0.1f, _owner.Scale.X);
			float radius = _owner.GetEffectiveAuraRadius() * invScale;
			float t = (float)_owner.AuraTime;
			float breathe = 0.96f + Mathf.Sin(t * 1.5f) * 0.04f;
			float curRadius = radius * breathe;

			// 1. Густая темная подложка вязкой грязи (ил/мокрая глина)
			DrawCircle(Vector2.Zero, curRadius, new Color(0.18f, 0.12f, 0.08f, 0.48f));
			DrawCircle(Vector2.Zero, curRadius * 0.74f, new Color(0.13f, 0.08f, 0.05f, 0.58f));

			// 2. Вращающиеся пятна вязкого глинистого ила
			for (int i = 0; i < 8; i++)
			{
				float seed1 = Mathf.Sin(i * 127.1f + 311.7f) * 43758.5453f;
				float seed2 = Mathf.Sin(i * 269.5f + 183.3f) * 24634.6345f;
				float f1 = seed1 - Mathf.Floor(seed1);
				float f2 = seed2 - Mathf.Floor(seed2);

				float baseAngle = f1 * Mathf.Tau + t * (i % 2 == 0 ? 0.20f : -0.16f);
				float distFactor = 0.20f + f2 * 0.50f;
				Vector2 patchPos = new Vector2(Mathf.Cos(baseAngle), Mathf.Sin(baseAngle)) * (curRadius * distFactor);
				float patchRadius = curRadius * (0.13f + f1 * 0.13f);

				Color patchColor = (i % 2 == 0)
					? new Color(0.34f, 0.22f, 0.12f, 0.36f)
					: new Color(0.48f, 0.32f, 0.16f, 0.30f);
				DrawCircle(patchPos, patchRadius, patchColor);
			}

			// 3. Расходящаяся янтарно-глиняная волна по поверхности трясины
			float ripplePhase = (t * 0.42f) % 1.0f;
			float rippleRadius = curRadius * (0.22f + 0.76f * ripplePhase);
			float rippleAlpha = (1.0f - ripplePhase) * 0.45f;
			DrawArc(Vector2.Zero, rippleRadius, 0f, Mathf.Tau, 48, new Color(0.85f, 0.55f, 0.20f, rippleAlpha), 1.6f);

			// 4. Теплый золотисто-янтарный светящийся контур границы грязи (не дает слиться с темным полом арены)
			float rimPulse = 0.55f + 0.22f * Mathf.Sin(t * 2.6f);
			// Темный ободок грязевой кромки
			DrawArc(Vector2.Zero, curRadius, 0f, Mathf.Tau, 64, new Color(0.40f, 0.25f, 0.12f, 0.65f), 3.2f);
			// Мягкое янтарное свечение вокруг кромки
			DrawArc(Vector2.Zero, curRadius + 1.8f, 0f, Mathf.Tau, 64, new Color(0.82f, 0.48f, 0.15f, 0.32f * rimPulse), 2.5f);
			// Четкая светящаяся линия границы
			DrawArc(Vector2.Zero, curRadius, 0f, Mathf.Tau, 64, new Color(0.96f, 0.68f, 0.24f, rimPulse), 1.6f);

			// 5. Всплывающие и лопающиеся пузыри густой грязи с глиняно-золотым бликом
			int bubbles = 6;
			for (int i = 0; i < bubbles; i++)
			{
				float phase = (t * 0.30f + i / (float)bubbles) % 1.0f;
				float b1 = Mathf.Sin(i * 91.7f + 47.3f) * 12543.17f;
				float ang = (b1 - Mathf.Floor(b1)) * Mathf.Tau + Mathf.Sin(t * 0.6f + i) * 0.25f;
				float r = curRadius * (0.16f + 0.68f * ((i * 0.37f) % 1.0f));
				Vector2 pos = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang) * 0.85f) * r + new Vector2(0, -phase * 7.0f);

				float alpha = Mathf.Sin(phase * Mathf.Pi);
				float bRadius = 1.6f + 1.6f * phase;
				// Тело грязевого пузыря
				DrawCircle(pos, bRadius, new Color(0.30f, 0.18f, 0.09f, 0.65f * alpha));
				// Светящийся блик на шапке пузыря
				DrawArc(pos, bRadius, 0f, Mathf.Tau, 16, new Color(0.92f, 0.70f, 0.34f, 0.85f * alpha), 1.0f);
			}
		}
	}
}
