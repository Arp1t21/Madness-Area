using Godot;

// Медлительный "болотный" моб:
// - держит вокруг себя видимую пульсирующую ауру густой грязи, замедляющую игрока на SlowStrength;
// - бьет в ближнем бою, но с умеренным отталкиванием (Knockback 2.5).
public partial class Quagmire : Enemy
{
	[Export] public float SlowAuraRadius = 36.0f; // в тайлах, умножается на World.Scale
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

	public override void _PhysicsProcess(double delta)
	{
		AuraTime += delta;

		if (!StepEnemyActionState(delta))
		{
			MoveAndSlide();
			if (_player != null && GodotObject.IsInstanceValid(_player) && Config != null && CurrentHealth > 0)
			{
				UpdateSlowAura((float)delta, GlobalPosition.DistanceTo(_player.GlobalPosition));
			}
			_auraVisual?.QueueRedraw();
			return;
		}

		float dist = GlobalPosition.DistanceTo(_player.GlobalPosition);
		UpdateMeleeChaseVelocity(delta, 36.0f, 90.0f, 1.0f, 0.05f);

		if (_cancelAttackCooldown <= 0 && dist < Config.AttackRange * World.Scale)
		{
			StartAttackPreparation();
		}

		MoveAndSlide();
		UpdateSlowAura((float)delta, dist);
		_auraVisual?.QueueRedraw();
	}

	private void UpdateSlowAura(float dt, float dist)
	{
		_auraTimer -= dt;
		if (_auraTimer <= 0)
		{
			_auraTimer = SlowRefreshInterval;
			if (dist <= SlowAuraRadius * World.Scale)
			{
				_player.ApplyStatusEffect(StatusEffectType.Slow, SlowRefreshInterval + 0.1f, SlowStrength);
			}
		}
	}

	protected override void FinishAttack()
	{
		_isPreparingAttack = false;
		if (_warningLabel != null) _warningLabel.Visible = false;

		if (_player != null && GodotObject.IsInstanceValid(_player) &&
			GlobalPosition.DistanceTo(_player.GlobalPosition) <= (Config.AttackRange + 10.0f) * World.Scale)
		{
			// Knockback снижен с 5.0f до 2.5f (в диапазоне 0-4 для всех врагов кроме призрака)
			_player.TakeDamage(Config.Damage * DamageMultiplier, GlobalPosition, 2.5f, true);
		}

		TriggerStop(Config.StopDuration);
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

			float radius = _owner.SlowAuraRadius * World.Scale;
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
				float bRadius = (2.2f + 2.2f * phase) * (World.Scale / 3.0f);
				// Тело грязевого пузыря
				DrawCircle(pos, bRadius, new Color(0.30f, 0.18f, 0.09f, 0.65f * alpha));
				// Светящийся блик на шапке пузыря
				DrawArc(pos, bRadius, 0f, Mathf.Tau, 16, new Color(0.92f, 0.70f, 0.34f, 0.85f * alpha), 1.0f);
			}
		}
	}
}
