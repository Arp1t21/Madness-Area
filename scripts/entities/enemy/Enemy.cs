using Godot;

public partial class Enemy : CharacterBody2D
{
	[Export] public EnemyData Config;
	public float CurrentHealth;
	protected AnimatedSprite2D _sprite;

	public float DamageMultiplier { get; protected set; } = 1.0f;

	public override void _Ready()
	{
		Scale = new Vector2(3.0f, 3.0f);
		AddToGroup("enemy");
		_sprite = GetNodeOrNull<AnimatedSprite2D>("AnimatedSprite2D");
		
		var level = GetTree().GetFirstNodeInGroup("Level") as Level;
		int levelIndex = level != null ? level.CurrentLevelIndex : 1;

		float healthMultiplier = 1.0f + (levelIndex - 1) * 0.15f;
		DamageMultiplier = 1.0f + (levelIndex - 1) * 0.15f;

		if (Config != null) {
			CurrentHealth = Config.MaxHealth * healthMultiplier;
		} else {
			GD.PrintErr($"{Name}: EnemyData Config не назначен — враг удаляется");
			QueueFree();
		}
	}

	// Метод для получения позиции игрока
	public Vector2 GetPlayerPosition() {
		var player = GetTree().GetFirstNodeInGroup("Player") as Node2D;
		return player != null ? player.GlobalPosition : GlobalPosition;
	}

	// --- Анти-застревание ---
	// Если моб хочет двигаться, но почти не сдвигается (уперся в стену/угол),
	// временно уходим перпендикулярно исходному направлению и сбрасываем путь.
	private Vector2 _antiStuckLastPos = Vector2.Zero;
	private float _antiStuckTimer = 0.0f;
	private float _detourTimer = 0.0f;
	private Vector2 _detourDirection = Vector2.Zero;

	protected Vector2 ApplyAntiStuck(Vector2 desiredDir, double delta, System.Action clearPath = null)
	{
		if (_detourTimer > 0.0f)
		{
			_detourTimer -= (float)delta;
			return _detourDirection;
		}

		bool wantsToMove = desiredDir.LengthSquared() > 0.01f;
		float moved = GlobalPosition.DistanceTo(_antiStuckLastPos);
		_antiStuckLastPos = GlobalPosition;

		if (wantsToMove && moved < 2.0f * (float)delta)
		{
			_antiStuckTimer += (float)delta;
		}
		else
		{
			_antiStuckTimer = 0.0f;
		}

		if (_antiStuckTimer > 0.5f)
		{
			_antiStuckTimer = 0.0f;
			_detourTimer = 0.6f;
			float side = GD.Randf() < 0.5f ? 1.0f : -1.0f;
			_detourDirection = desiredDir.Rotated(Mathf.Pi / 2.0f * side);
			clearPath?.Invoke();
			return _detourDirection;
		}

		return desiredDir;
	}

	public virtual void TakeDamage(float amount, Vector2 knockbackSource, float knockbackForce = 15.0f, bool isMelee = true) {
		if (CurrentHealth <= 0) return;

		// Сопротивления из конфига врага (к ближнему/дальнему бою)
		if (Config != null) {
			float res = isMelee ? Config.MeleeResistance : Config.RangedResistance;
			amount *= (1.0f - Mathf.Clamp(res, 0.0f, 0.95f));
		}

		CurrentHealth -= amount;
		GD.Print($"{Config?.EnemyName} получил урон: {amount}. ХП: {CurrentHealth}");

		FlashRed();
		ApplyKnockback(knockbackSource, knockbackForce);
		SpawnDamageNumber(amount);

		if (CurrentHealth <= 0) {
			Die();
		}
	}

	protected void FlashRed() {
		Color flashColor = Config?.FlashColor ?? new Color(1, 0, 0);
		Modulate = flashColor;
		var timer = GetTree().CreateTimer(0.1f);
		timer.Timeout += () => Modulate = new Color(1, 1, 1);
	}

	protected void ApplyKnockback(Vector2 source, float force) {
		Vector2 dir = (GlobalPosition - source).Normalized();
		Velocity = dir * force * 30.0f;
		MoveAndSlide();
	}

	protected void SpawnDamageNumber(float amount) {
		var ft = new FloatingText();
		GetParent().AddChild(ft);
		float offsetX = (float)GD.RandRange(-15.0f, 15.0f) * World.Scale;
		float offsetY = (float)GD.RandRange(-5.0f, 5.0f) * World.Scale;
		ft.GlobalPosition = GlobalPosition + new Vector2(offsetX, -30.0f + offsetY);
		Color textColor = Config?.DamageNumberColor ?? new Color(1, 1, 0);
		ft.Init(((int)amount).ToString(), textColor);
	}

	protected virtual void Die() {
		GD.Print($"{Config?.EnemyName} погиб!");

		// Дроп появляется ровно в момент, когда заканчивается анимация смерти
		Vector2 deathPos = GlobalPosition;
		Node parentNode = GetParent();

		float animDuration = PlayDeathAnimation();

		if (Config != null) {
			if (GD.Randf() <= Config.CoinDropChance) {
				DelayedDrop(deathPos, parentNode, isMana: false, delay: animDuration);
			}
			if (GD.Randf() <= Config.ManaCrystalDropChance) {
				DelayedDrop(deathPos, parentNode, isMana: true, delay: animDuration);
			}
		}
	}

	private void DelayedDrop(Vector2 pos, Node parentNode, bool isMana, float delay) {
		var timer = GetTree().CreateTimer(delay, processAlways: false);
		timer.Timeout += () => {
			if (parentNode == null || !GodotObject.IsInstanceValid(parentNode)) return;
			string path = isMana ? "res://scene/objects/mana_crystal.tscn" : "res://scene/objects/coin.tscn";
			var prefab = GD.Load<PackedScene>(path);
			if (prefab != null) {
				var drop = prefab.Instantiate() as Node2D;
				drop.GlobalPosition = pos;
				parentNode.AddChild(drop);
			}
		};
	}

	// Анимация смерти: вспышка -> "хлопок" с частицами пыли -> оседание с затуханием.
	// Возвращает длительность (в момент конца выпадает лут).
	protected float PlayDeathAnimation() {
		const float flashTime = 0.08f;
		const float popTime = 0.1f;
		const float fadeTime = 0.5f;
		float total = flashTime + popTime + fadeTime;

		// Труп не должен никого блокировать и получать удары
		CollisionLayer = 0;
		CollisionMask = 0;
		var hitBox = GetNodeOrNull<Area2D>("Area2DHitBox");
		if (hitBox != null) {
			hitBox.SetDeferred("monitoring", false);
			hitBox.SetDeferred("monitorable", false);
		}

		// Облако пыли/крови при смерти
		var dust = new CpuParticles2D();
		dust.OneShot = true;
		dust.Emitting = true;
		dust.Explosiveness = 1.0f;
		dust.Amount = 22;
		dust.Lifetime = 0.5f;
		dust.LocalCoords = false;
		dust.Spread = 180.0f;
		dust.Gravity = new Vector2(0, 260);
		dust.InitialVelocityMin = 55;
		dust.InitialVelocityMax = 140;
		dust.ScaleAmountMin = 1.2f;
		dust.ScaleAmountMax = 2.6f;
		var gradient = new Gradient();
		gradient.Colors = new Color[] {
			new Color(0.75f, 0.2f, 0.15f, 0.9f),
			new Color(0.35f, 0.1f, 0.08f, 0.0f)
		};
		gradient.Offsets = new float[] { 0.0f, 1.0f };
		dust.ColorRamp = gradient;
		AddChild(dust);
		// Частицы удалятся сами, а если нода умрет раньше — они уйдут вместе с ней

		Vector2 baseScale = Scale;
		var tween = CreateTween();

		// 1. Короткая яркая вспышка
		tween.TweenProperty(this, "modulate", new Color(2.2f, 2.2f, 2.2f), flashTime);
		// 2. "Хлопок": чуть раздувается
		tween.TweenProperty(this, "scale", baseScale * 1.15f, popTime)
			.SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
		// 3. Оседает: сжимается, слегка заваливается и растворяется
		tween.TweenProperty(this, "scale", baseScale * 0.3f, fadeTime)
			.SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
		tween.Parallel().TweenProperty(this, "rotation", Rotation + Mathf.DegToRad(22.0f), fadeTime)
			.SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
		tween.Parallel().TweenProperty(this, "modulate", new Color(1, 1, 1, 0), fadeTime)
			.SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
		tween.TweenCallback(Callable.From(QueueFree));

		return total;
	}

	// Метод, который заставляет моба двигаться к цели
	public void MoveTowards(Vector2 target, double delta) {
		if (Config == null) return;
		Vector2 direction = GlobalPosition.DirectionTo(target);
		Velocity = direction * Config.Speed * World.Scale;
		MoveAndSlide();
	}
}
