using Godot;

public partial class BigDemon : Enemy
{
	protected override Color WarningLabelColor => new Color(1.0f, 0.2f, 0.05f);

	private PointLight2D _demonAuraLight;
	private float _chargeCooldown = 4.0f;
	private float _chargeTimer = 0.0f;

	private float _specialCooldown = 3.8f;
	private float _telegraphTimer = 0.0f;
	private bool _inTelegraph = false;
	private bool _isChargeTelegraph = true;
	private Vector2 _telegraphDir = Vector2.Right;
	private float _specialDashTimer = 0.0f;
	private bool _specialDashDealtHit = false;
	private bool _nextSpecialIsCharge = true;

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

	protected override void TickEnemyAI(double delta)
	{
		float dt = (float)delta;
		if (_chargeCooldown > 0) _chargeCooldown -= dt;
		if (_chargeTimer > 0) _chargeTimer -= dt;
		if (_specialCooldown > 0) _specialCooldown -= dt;

		if (IsBoss && _inTelegraph)
		{
			RefreshNearestTarget();
			if (CurrentHealth <= 0 || _hasDied || _player == null || !GodotObject.IsInstanceValid(_player) || _player.IsDead)
			{
				_inTelegraph = false;
				return;
			}
			Velocity = Velocity.Lerp(Vector2.Zero, 0.25f);
			MoveAndSlide();
			_telegraphTimer -= dt;
			if (_telegraphTimer <= 0.0f)
			{
				ExecuteTelegraphedSpecial();
			}
			return;
		}

		if (IsBoss && _specialDashTimer > 0.0f)
		{
			_specialDashTimer -= dt;
			Velocity = _telegraphDir * Config.Speed * 2.9f;
			MoveAndSlide();
			if (!_specialDashDealtHit)
			{
				foreach (Player target in NetworkManager.GetAlivePlayers(GetTree()))
				{
					if (GlobalPosition.DistanceTo(target.GlobalPosition) <= 22.0f)
					{
						_specialDashDealtHit = true;
						target.TakeDamage(Config.Damage * DamageMultiplier * 1.15f, GlobalPosition, 12.0f, true);
						target.ApplyStatusEffect(StatusEffectType.Burn, 2.5f, 2.5f);
					}
				}
			}
			return;
		}

		if (StepEnemyActionState(delta))
		{
			float playerDist = GlobalPosition.DistanceTo(_player.GlobalPosition);
			if (IsBoss && _specialCooldown <= 0.0f && playerDist >= 28.0f && playerDist <= 220.0f)
			{
				BeginBossTelegraph();
				return;
			}

			if (_chargeCooldown <= 0 && playerDist < 140.0f && playerDist > 38.0f)
			{
				_chargeTimer = 0.65f;
				_chargeCooldown = 4.5f;
			}

			float speedMult = _chargeTimer > 0 ? 1.75f : 1.0f;
			UpdateMeleeChaseVelocity(delta, 10.0f, 32.0f, speedMult, 0.12f);

			if (_cancelAttackCooldown <= 0 && _attackController != null && _attackController.CanAttack())
			{
				if (playerDist < Config.AttackRange)
				{
					StartAttackPreparation();
				}
			}
		}

		MoveAndSlide();
	}

	private void BeginBossTelegraph()
	{
		RefreshNearestTarget();
		if (_player == null || !GodotObject.IsInstanceValid(_player) || _player.IsDead) return;
		_inTelegraph = true;
		_isChargeTelegraph = _nextSpecialIsCharge;
		_nextSpecialIsCharge = !_nextSpecialIsCharge;
		_telegraphDir = (_player.GlobalPosition - GlobalPosition).Normalized();
		if (_telegraphDir.LengthSquared() < 0.01f) _telegraphDir = Vector2.Right;
		_specialCooldown = 5.2f;

		if (_isChargeTelegraph)
		{
			_telegraphTimer = 0.64f;
			BroadcastBossTelegraphLine(GlobalPosition, _telegraphDir, 130.0f, 22.0f, _telegraphTimer, new Color(1.0f, 0.22f, 0.16f));
		}
		else
		{
			_telegraphTimer = 0.72f;
			BroadcastBossTelegraphCircle(GlobalPosition, 54.0f, _telegraphTimer, new Color(1.0f, 0.45f, 0.12f));
		}
		PlayAnimationSafe(IdleAnimationName);
	}

	private void ExecuteTelegraphedSpecial()
	{
		_inTelegraph = false;
		if (CurrentHealth <= 0 || _hasDied) return;

		if (_isChargeTelegraph)
		{
			_specialDashTimer = 0.42f;
			_specialDashDealtHit = false;
			Node host = GetTree()?.CurrentScene ?? GetParent();
			if (host != null)
			{
				DungeonVisuals.SpawnImpactSparks(host, GlobalPosition, -_telegraphDir, new Color(1.0f, 0.45f, 0.16f), 10, 45.0f, 80.0f, 0.9f);
			}
			return;
		}

		float baseAngle = _telegraphDir.Angle();
		BroadcastBossOrbs(
			GlobalPosition,
			10.0f,
			5,
			baseAngle - 2 * 0.24f,
			0.24f,
			128.0f,
			220.0f,
			Mathf.Max(6.0f, Config.Damage * DamageMultiplier * 0.65f),
			new Color(1.0f, 0.36f, 0.12f, 1.0f));
	}
}
