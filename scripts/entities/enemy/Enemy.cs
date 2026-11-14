using Godot;
using System;
using System.Collections.Generic;

public partial class Enemy : CharacterBody2D
{
	[Export] public EnemyData Config;
	[Export] public WeaponData AttackConfig;

	public float MaxHealthScaled { get; protected set; } = 30.0f;
	public float CurrentHealth;
	public bool IsBoss { get; private set; } = false;
	public string DisplayName => IsBoss ? $"BOSS: {Config?.EnemyName ?? "Warlord"}" : (Config?.EnemyName ?? "Enemy");

	protected AnimatedSprite2D _sprite;
	protected Sprite2D _groundShadow;
	protected ShaderMaterial _hitFlashMat;
	protected Tween _hitFlashTween;
	protected DungeonRuneAura _bossAura;
	protected Player _player;
	protected AttackController _attackController;
	protected Label _warningLabel;

	protected bool _isStopping = false;
	protected bool _isPreparingAttack = false;
	protected float _preAttackTimer = 0.0f;
	protected float _periodicCheckTimer = 0.0f;
	protected float _cancelAttackCooldown = 0.0f;

	protected List<Vector2> _navigationPath = new List<Vector2>();
	protected float _pathUpdateTimer = 0.0f;
	private Level _cachedLevel;

	private static int s_flankCounter = 0;
	protected float _flankAngle = 0.0f;
	protected float _flankSign = 1.0f;

	public float DamageMultiplier { get; protected set; } = 1.0f;

	protected virtual Color WarningLabelColor => new Color(1.0f, 0.15f, 0.15f);

	protected virtual string IdleAnimationName
	{
		get
		{
			if (_sprite?.SpriteFrames != null)
			{
				if (_sprite.SpriteFrames.HasAnimation("Idle")) return "Idle";
				if (_sprite.SpriteFrames.HasAnimation("idle")) return "idle";
			}
			return "idle";
		}
	}

	protected virtual string WalkAnimationName
	{
		get
		{
			if (_sprite?.SpriteFrames != null)
			{
				if (_sprite.SpriteFrames.HasAnimation("Walk")) return "Walk";
				if (_sprite.SpriteFrames.HasAnimation("walk")) return "walk";
				if (_sprite.SpriteFrames.HasAnimation("Run")) return "Run";
				if (_sprite.SpriteFrames.HasAnimation("run")) return "run";
			}
			return "walk";
		}
	}

	public override void _Ready()
	{
		Scale = new Vector2(3.0f, 3.0f);
		AddToGroup("enemy");
		AddToGroup("Enemies");

		int flankIdx = s_flankCounter++;
		// Золотой угол (~137.5°) равномерно распределяет каждого нового моба по кругу вокруг игрока
		_flankAngle = (flankIdx * 2.3999632f) % Mathf.Tau;
		_flankSign = (flankIdx % 2 == 0) ? 1.0f : -1.0f;

		ZIndex = 2;
		_sprite = GetNodeOrNull<AnimatedSprite2D>("AnimatedSprite2D");
		if (_sprite != null)
		{
			_hitFlashMat = DungeonVisuals.CreateHitFlashMaterial();
			_sprite.Material = _hitFlashMat;
		}
		float shadowScaleMul = (this is BigDemon || this is Quagmire) ? 0.86f : 0.56f;
		_groundShadow = DungeonVisuals.AttachGroundShadow(this, new Vector2(0, -1), new Vector2(shadowScaleMul, shadowScaleMul * 0.86f), 0.55f);
		_player = GetTree().GetFirstNodeInGroup("Player") as Player;

		var level = GetTree().GetFirstNodeInGroup("Level") as Level;
		int levelIndex = level != null ? level.CurrentLevelIndex : 1;

		// ХП мобов увеличивается на 15% после каждого побежденного босса (каждые 5 этажей)
		int bossesDefeated = Mathf.Max(0, (levelIndex - 1) / 5);
		float healthMultiplier = Mathf.Pow(1.15f, bossesDefeated);
		DamageMultiplier = 1.0f + bossesDefeated * 0.15f;

		if (Config != null)
		{
			MaxHealthScaled = Config.MaxHealth * healthMultiplier;
			CurrentHealth = MaxHealthScaled;
		}
		else
		{
			GD.PrintErr($"{Name}: EnemyData Config не назначен — враг удаляется");
			QueueFree();
			return;
		}

		// Создаем контроллер оружия для моба, если задан AttackConfig
		if (AttackConfig != null)
		{
			var controllerPrefab = GD.Load<PackedScene>("res://scene/weapons/attack_controller.tscn");
			if (controllerPrefab != null)
			{
				_attackController = controllerPrefab.Instantiate() as AttackController;
				_attackController.CurrentWeapon = AttackConfig;
				AddChild(_attackController);
				_attackController.ApplyWeaponSettings();
			}
		}

		SetupWarningLabel(WarningLabelColor);

		// Гарантируем, что хитбокс врага видит оружие игрока на 3-м физическом слое (бит 4)
		var hitBox = GetNodeOrNull<Area2D>("Area2DHitBox");
		if (hitBox != null)
		{
			hitBox.CollisionMask |= (1 << 2);
			if (!hitBox.IsConnected(Area2D.SignalName.AreaEntered, new Callable(this, MethodName._on_area_2d_area_entered)))
			{
				hitBox.AreaEntered += _on_area_2d_area_entered;
			}
		}
	}

	public void PromoteToBoss(int levelIndex)
	{
		if (IsBoss) return;
		IsBoss = true;
		AddToGroup("Boss");

		Scale = new Vector2(5.2f, 5.2f);
		float bossHpFactor = 4.5f + (levelIndex / 5) * 0.8f;
		MaxHealthScaled *= bossHpFactor;
		CurrentHealth = MaxHealthScaled;
		DamageMultiplier *= 1.5f;

		if (_sprite != null)
		{
			_sprite.SelfModulate = new Color(1.18f, 0.86f, 0.74f, 1.0f);
		}

		if (_bossAura == null)
		{
			float auraRadius = (this is BigDemon || this is Quagmire) ? 20.0f : 14.5f;
			_bossAura = DungeonVisuals.CreateRuneAura(auraRadius, new Color(1.0f, 0.28f, 0.20f, 0.78f), 8, -1);
			_bossAura.Position = new Vector2(0, -2);
			AddChild(_bossAura);
		}
	}

	protected void SetupWarningLabel(Color color)
	{
		if (_warningLabel != null) return;

		_warningLabel = new Label();
		_warningLabel.Text = "!";
		_warningLabel.AddThemeColorOverride("font_color", color);

		var font = GameResourceCache.GetForwardFont();
		if (font != null)
		{
			_warningLabel.AddThemeFontOverride("font", font);
		}
		_warningLabel.AddThemeFontSizeOverride("font_size", 13);
		_warningLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_warningLabel.VerticalAlignment = VerticalAlignment.Center;
		_warningLabel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterTop);
		_warningLabel.Size = new Vector2(40, 20);
		_warningLabel.Position = new Vector2(-20, -35);
		_warningLabel.Visible = false;
		AddChild(_warningLabel);
	}

	protected Vector2 UpdateNavigationDirection(double delta, float waypointReachDist = 36.0f)
	{
		if (_player == null || !GodotObject.IsInstanceValid(_player)) return Vector2.Zero;

		_flankAngle = (_flankAngle + _flankSign * 0.22f * (float)delta) % Mathf.Tau;
		float distToPlayer = GlobalPosition.DistanceTo(_player.GlobalPosition);

		_pathUpdateTimer -= (float)delta;
		if (_pathUpdateTimer <= 0)
		{
			_pathUpdateTimer = 0.25f + (float)GD.RandRange(-0.05f, 0.05f);
			if (!GodotObject.IsInstanceValid(_cachedLevel))
			{
				_cachedLevel = GetTree().GetFirstNodeInGroup("Level") as Level;
			}
			if (_cachedLevel != null)
			{
				Vector2 navGoal = _player.GlobalPosition;
				// Если моб далеко, он строит маршрут к своей точке окружения вокруг игрока, а не идет в одну линию со всеми
				if (distToPlayer > 70.0f * World.Scale)
				{
					float flankRadius = Mathf.Clamp((distToPlayer - 35.0f * World.Scale) * 0.65f, 42.0f * World.Scale, 125.0f * World.Scale);
					Vector2 desiredFlank = _player.GlobalPosition + new Vector2(Mathf.Cos(_flankAngle), Mathf.Sin(_flankAngle)) * flankRadius;
					navGoal = _cachedLevel.GetWalkableFlankTarget(_player.GlobalPosition, desiredFlank);
				}
				_navigationPath = _cachedLevel.GetPathToPlayer(GlobalPosition, navGoal) ?? new List<Vector2>();
			}
		}

		Vector2 targetPos = _player.GlobalPosition;
		if (_navigationPath != null && _navigationPath.Count > 0)
		{
			while (_navigationPath.Count > 1 && GlobalPosition.DistanceTo(_navigationPath[0]) < waypointReachDist)
			{
				_navigationPath.RemoveAt(0);
			}
			if (_navigationPath.Count > 0)
			{
				targetPos = _navigationPath[0];
			}
		}

		Vector2 baseDir = (targetPos - GlobalPosition).Normalized();

		// Добавляем боковое смещение для обхода игрока с назначенного фланга на средней дистанции
		if (distToPlayer > 38.0f * World.Scale && distToPlayer < 140.0f * World.Scale)
		{
			Vector2 desiredPosAroundPlayer = _player.GlobalPosition + new Vector2(Mathf.Cos(_flankAngle), Mathf.Sin(_flankAngle)) * (45.0f * World.Scale);
			Vector2 toFlankSlot = (desiredPosAroundPlayer - GlobalPosition).Normalized();
			float blend = Mathf.Clamp((distToPlayer - 38.0f * World.Scale) / (100.0f * World.Scale), 0.0f, 0.55f);
			baseDir = (baseDir * (1.0f - blend) + toFlankSlot * blend).Normalized();
		}

		return baseDir;
	}

	protected Vector2 GetStrafeDirectionAroundPlayer()
	{
		if (_player == null || !GodotObject.IsInstanceValid(_player)) return Vector2.Zero;
		Vector2 desiredRingPos = _player.GlobalPosition + new Vector2(Mathf.Cos(_flankAngle), Mathf.Sin(_flankAngle)) * (110.0f * World.Scale);
		if (GlobalPosition.DistanceTo(desiredRingPos) > 24.0f * World.Scale)
		{
			return (desiredRingPos - GlobalPosition).Normalized();
		}
		Vector2 radial = (GlobalPosition - _player.GlobalPosition).Normalized();
		return new Vector2(-radial.Y * _flankSign, radial.X * _flankSign);
	}

	protected Vector2 ComputeEnemySeparation(float separationRadius = 80.0f)
	{
		Vector2 separation = Vector2.Zero;
		var enemies = GetTree().GetNodesInGroup("Enemies");
		int neighborsCount = 0;

		Vector2 toPlayer = (_player != null && GodotObject.IsInstanceValid(_player))
			? (_player.GlobalPosition - GlobalPosition).Normalized()
			: Vector2.Zero;

		foreach (Node node in enemies)
		{
			if (node is CharacterBody2D otherEnemy && otherEnemy != this && GodotObject.IsInstanceValid(otherEnemy))
			{
				float d = GlobalPosition.DistanceTo(otherEnemy.GlobalPosition);
				if (d < separationRadius && d > 0.1f)
				{
					Vector2 away = (GlobalPosition - otherEnemy.GlobalPosition).Normalized();
					float weight = (separationRadius - d) / separationRadius;
					separation += away * (weight * 1.4f);

					// Если другой моб идет перед нами или рядом в сторону игрока — расталкиваемся вбок по дуге, чтобы не идти поездом
					if (toPlayer != Vector2.Zero)
					{
						Vector2 tangent = new Vector2(-toPlayer.Y, toPlayer.X);
						float side = Mathf.Sign(away.Dot(tangent));
						if (Mathf.Abs(side) < 0.01f) side = _flankSign;
						separation += tangent * (side * weight * 1.1f);
					}
					neighborsCount++;
				}
			}
		}

		if (neighborsCount > 0)
		{
			separation /= neighborsCount;
		}
		return separation;
	}

	protected PointLight2D SetupGlowLight(
		string name,
		Vector2 position,
		Color lightColor,
		float energy,
		Color innerColor,
		Color midColor,
		float midOffset,
		int textureSize)
	{
		var tex = GameResourceCache.GetRadialGlowTexture(innerColor, midColor, midOffset, textureSize);
		return GameResourceCache.CreateGlowLight(this, name, position, lightColor, energy, tex, false);
	}

	/// <summary>
	/// Processes inactive, preparing, and post-attack stop states.
	/// Returns true when the enemy is free to steer and trigger a new attack this frame.
	/// </summary>
	protected bool StepEnemyActionState(double delta)
	{
		if (_player == null || !GodotObject.IsInstanceValid(_player) || Config == null || CurrentHealth <= 0)
		{
			Velocity = Velocity.Lerp(Vector2.Zero, 0.1f);
			return false;
		}

		if (_cancelAttackCooldown > 0)
		{
			_cancelAttackCooldown -= (float)delta;
		}

		if (_isPreparingAttack)
		{
			HandlePreparingState(delta);
			Velocity = Velocity.Lerp(Vector2.Zero, 0.1f);
			return false;
		}

		if (_isStopping)
		{
			Velocity = Velocity.Lerp(Vector2.Zero, 0.1f);
			return false;
		}

		return true;
	}

	protected void RunRangedKitingAI(
		double delta,
		float fallbackAttackRange,
		float approachDistanceRatio,
		float retreatDistance,
		float approachWaypointReach,
		float retreatSpeedFactor,
		float strafeSpeedFactor)
	{
		float baseAttackRange = Config.AttackRange > 0 ? Config.AttackRange : fallbackAttackRange;
		float engageDist = baseAttackRange * approachDistanceRatio * World.Scale;
		float retreatDist = retreatDistance * World.Scale;
		float triggerRange = baseAttackRange * World.Scale;
		float dist = GlobalPosition.DistanceTo(_player.GlobalPosition);

		Vector2 direction;
		float currentSpeed = Config.Speed * World.Scale;

		if (dist > engageDist)
		{
			direction = UpdateNavigationDirection(delta, approachWaypointReach * World.Scale);
		}
		else if (dist < retreatDist)
		{
			direction = (GlobalPosition - _player.GlobalPosition).Normalized();
			currentSpeed = Config.Speed * retreatSpeedFactor * World.Scale;
		}
		else
		{
			direction = GetStrafeDirectionAroundPlayer();
			currentSpeed = Config.Speed * strafeSpeedFactor * World.Scale;
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

		Velocity = Velocity.Lerp(direction * currentSpeed, 0.1f);

		if (_sprite != null)
		{
			_sprite.Play(Velocity.Length() > 5.0f ? WalkAnimationName : IdleAnimationName);
			_sprite.FlipH = _player.GlobalPosition.X < GlobalPosition.X;
		}

		if (_cancelAttackCooldown <= 0 && _attackController != null && _attackController.CanAttack())
		{
			if (dist <= triggerRange)
			{
				StartAttackPreparation();
			}
		}
	}

	protected void UpdateMeleeChaseVelocity(
		double delta,
		float waypointReach,
		float separationRadius,
		float speedMultiplier,
		float velocityLerp,
		float playerSeparationRadius = 0.0f)
	{
		Vector2 direction = UpdateNavigationDirection(delta, waypointReach);
		direction = ApplyAntiStuck(direction, delta, () => _navigationPath?.Clear());

		Vector2 separation = ComputeEnemySeparation(separationRadius);
		if (separation != Vector2.Zero)
		{
			direction = (direction + separation * 0.65f).Normalized();
		}

		if (playerSeparationRadius > 0.0f)
		{
			float playerDist = GlobalPosition.DistanceTo(_player.GlobalPosition);
			if (playerDist < playerSeparationRadius && playerDist > 0.1f)
			{
				Vector2 pushFromPlayer = (GlobalPosition - _player.GlobalPosition).Normalized() * (playerSeparationRadius - playerDist);
				direction = (direction + pushFromPlayer * 0.35f).Normalized();
			}
		}

		Vector2 targetVelocity = direction * Config.Speed * speedMultiplier * World.Scale;
		Velocity = Velocity.Lerp(targetVelocity, velocityLerp);

		if (_sprite != null)
		{
			if (Mathf.Abs(Velocity.X) > 5.0f)
			{
				_sprite.FlipH = Velocity.X < 0;
			}
			_sprite.Play(WalkAnimationName);
		}
	}

	protected void StartAttackPreparation()
	{
		_isPreparingAttack = true;
		_preAttackTimer = 0.0f;
		_periodicCheckTimer = 0.5f;

		if (_warningLabel != null) _warningLabel.Visible = true;
		if (_sprite != null && _player != null && GodotObject.IsInstanceValid(_player))
		{
			_sprite.FlipH = _player.GlobalPosition.X < GlobalPosition.X;
			_sprite.Play(IdleAnimationName);
		}
	}

	protected void HandlePreparingState(double delta)
	{
		if (Config == null || _player == null || !GodotObject.IsInstanceValid(_player))
		{
			CancelAttack();
			return;
		}

		_preAttackTimer += (float)delta;
		_periodicCheckTimer -= (float)delta;

		if (_periodicCheckTimer <= 0)
		{
			_periodicCheckTimer = 0.5f;
			if (GlobalPosition.DistanceTo(_player.GlobalPosition) > (Config.AttackRange + 15.0f) * World.Scale)
			{
				CancelAttack();
				return;
			}
		}

		if (_preAttackTimer >= Config.PreAttackDelay)
		{
			FinishAttack();
		}
	}

	protected void CancelAttack()
	{
		_isPreparingAttack = false;
		_cancelAttackCooldown = 0.6f;
		if (_warningLabel != null) _warningLabel.Visible = false;
	}

	protected virtual void FinishAttack()
	{
		_isPreparingAttack = false;
		if (_warningLabel != null) _warningLabel.Visible = false;

		if (_attackController != null)
		{
			_attackController.HandleManualAttack();
		}

		if (Config != null)
		{
			TriggerStop(Config.StopDuration);
		}
	}

	public void TriggerStop(float duration)
	{
		_isStopping = true;
		var timer = GetTree().CreateTimer(duration, processAlways: false);
		timer.Timeout += () =>
		{
			if (GodotObject.IsInstanceValid(this))
			{
				_isStopping = false;
			}
		};
	}

	public void _on_area_2d_area_entered(Area2D area)
	{
		if (CurrentHealth <= 0) return;

		if (area.IsInGroup("PlayerWeapon"))
		{
			var controller = GameResourceCache.FindAncestor<AttackController>(area);
			if (controller != null && controller.CurrentWeapon != null)
			{
				if (!GodotObject.IsInstanceValid(_player))
				{
					_player = GetTree().GetFirstNodeInGroup("Player") as Player;
				}
				Vector2 source = _player != null ? _player.GlobalPosition : GlobalPosition;

				float dist = GlobalPosition.DistanceTo(source);
				float weaponRange = controller.CurrentWeapon.AttackRange * World.Scale;

				if (dist <= weaponRange + 18.0f * World.Scale)
				{
					if (controller.CanHitEnemy(GetInstanceId()))
					{
						float dmg = controller.CurrentWeapon.Damage * (_player != null ? _player.WeaponDamageMultiplier : 1.0f);
						TakeDamage(dmg, source, controller.CurrentWeapon.KnockbackForce, true);
					}
				}
			}
		}
	}

	// Метод для получения позиции игрока
	public Vector2 GetPlayerPosition()
	{
		if (!GodotObject.IsInstanceValid(_player))
		{
			_player = GetTree().GetFirstNodeInGroup("Player") as Player;
		}
		return _player != null ? _player.GlobalPosition : GlobalPosition;
	}

	// --- Анти-застревание ---
	private Vector2 _antiStuckLastPos = Vector2.Zero;
	private float _antiStuckTimer = 0.0f;
	private float _detourTimer = 0.0f;
	private Vector2 _detourDirection = Vector2.Zero;

	protected Vector2 ApplyAntiStuck(Vector2 desiredDir, double delta, Action clearPath = null)
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

	public virtual void TakeDamage(float amount, Vector2 knockbackSource, float knockbackForce = 15.0f, bool isMelee = true)
	{
		if (CurrentHealth <= 0) return;

		// Сопротивления из конфига врага (к ближнему/дальнему бою)
		if (Config != null)
		{
			float res = isMelee ? Config.MeleeResistance : Config.RangedResistance;
			amount *= (1.0f - Mathf.Clamp(res, 0.0f, 0.95f));
		}

		CurrentHealth -= amount;

		FlashRed();
		if (GetParent() != null)
		{
			Vector2 hitDir = knockbackSource != default ? (GlobalPosition - knockbackSource) : Vector2.Up;
			Color sparkCol = Config?.FlashColor ?? new Color(1.0f, 0.78f, 0.32f);
			DungeonVisuals.SpawnImpactSparks(
				GetParent(),
				GlobalPosition + new Vector2(0, -10.0f * Scale.Y),
				hitDir,
				sparkCol,
				isMelee ? 9 : 6,
				140.0f,
				68.0f,
				IsBoss ? 2.1f : 1.5f);
		}
		float effectiveKnockback = IsBoss ? knockbackForce * 0.35f : knockbackForce;
		ApplyKnockback(knockbackSource, effectiveKnockback);
		SpawnDamageNumber(amount);

		if (CurrentHealth <= 0)
		{
			Die();
		}
	}

	protected void FlashRed()
	{
		if (_sprite == null) return;
		Color flashColor = Config?.FlashColor ?? new Color(1, 0, 0);
		_sprite.Modulate = flashColor;

		if (_hitFlashMat != null)
		{
			_hitFlashTween?.Kill();
			_hitFlashMat.SetShaderParameter("flash_color", new Color(1.0f, 0.95f, 0.88f, 1.0f));
			_hitFlashMat.SetShaderParameter("flash_amount", 0.92f);
			_hitFlashTween = CreateTween();
			_hitFlashTween.TweenMethod(
				Callable.From<float>(v => { if (GodotObject.IsInstanceValid(_hitFlashMat)) _hitFlashMat.SetShaderParameter("flash_amount", v); }),
				0.92f,
				0.0f,
				0.12f);
		}

		var timer = GetTree().CreateTimer(0.1f, processAlways: false);
		timer.Timeout += () =>
		{
			if (GodotObject.IsInstanceValid(this) && GodotObject.IsInstanceValid(_sprite) && CurrentHealth > 0)
			{
				_sprite.Modulate = new Color(1, 1, 1);
			}
		};
	}

	protected void ApplyKnockback(Vector2 source, float force)
	{
		Vector2 dir = (GlobalPosition - source).Normalized();
		Velocity = dir * force * 30.0f;
		MoveAndSlide();
	}

	protected void SpawnDamageNumber(float amount)
	{
		var ft = new FloatingText();
		GetParent().AddChild(ft);
		float offsetX = (float)GD.RandRange(-15.0f, 15.0f) * World.Scale;
		float offsetY = (float)GD.RandRange(-5.0f, 5.0f) * World.Scale;
		ft.GlobalPosition = GlobalPosition + new Vector2(offsetX, -30.0f + offsetY);
		Color textColor = Config?.DamageNumberColor ?? new Color(1, 1, 0);
		int displayDmg = Mathf.Max(1, Mathf.RoundToInt(amount));
		ft.Init(displayDmg.ToString(), textColor);
	}

	protected virtual void Die()
	{
		if (GodotObject.IsInstanceValid(_warningLabel))
		{
			_warningLabel.Visible = false;
		}

		Vector2 deathPos = GlobalPosition;
		Node parentNode = GetParent();

		float animDuration = PlayDeathAnimation();

		if (IsBoss)
		{
			// Босс гарантированно роняет горсть монет и кристаллов маны
			for (int i = 0; i < 6; i++)
			{
				Vector2 offset = new Vector2((float)GD.RandRange(-24, 24), (float)GD.RandRange(-24, 24));
				DelayedDrop(deathPos + offset, parentNode, isMana: false, delay: animDuration);
			}
			for (int i = 0; i < 3; i++)
			{
				Vector2 offset = new Vector2((float)GD.RandRange(-20, 20), (float)GD.RandRange(-20, 20));
				DelayedDrop(deathPos + offset, parentNode, isMana: true, delay: animDuration);
			}
		}
		else if (Config != null)
		{
			if (GD.Randf() <= Config.CoinDropChance)
			{
				DelayedDrop(deathPos, parentNode, isMana: false, delay: animDuration);
			}
			if (GD.Randf() <= Config.ManaCrystalDropChance)
			{
				DelayedDrop(deathPos, parentNode, isMana: true, delay: animDuration);
			}
		}
	}

	private void DelayedDrop(Vector2 pos, Node parentNode, bool isMana, float delay)
	{
		var timer = GetTree().CreateTimer(delay, processAlways: false);
		timer.Timeout += () =>
		{
			if (parentNode == null || !GodotObject.IsInstanceValid(parentNode)) return;
			string path = isMana ? "res://scene/objects/mana_crystal.tscn" : "res://scene/objects/coin.tscn";
			var prefab = GD.Load<PackedScene>(path);
			if (prefab != null)
			{
				var drop = prefab.Instantiate() as Node2D;
				drop.GlobalPosition = pos;
				parentNode.AddChild(drop);
			}
		};
	}

	protected float PlayDeathAnimation()
	{
		const float flashTime = 0.08f;
		const float popTime = 0.1f;
		const float fadeTime = 0.5f;
		float total = flashTime + popTime + fadeTime;

		CollisionLayer = 0;
		CollisionMask = 0;
		var hitBox = GetNodeOrNull<Area2D>("Area2DHitBox");
		if (hitBox != null)
		{
			hitBox.SetDeferred("monitoring", false);
			hitBox.SetDeferred("monitorable", false);
		}

		if (_bossAura != null && GodotObject.IsInstanceValid(_bossAura))
		{
			_bossAura.QueueFree();
		}

		var dust = new CpuParticles2D();
		dust.OneShot = true;
		dust.Emitting = true;
		dust.Explosiveness = 1.0f;
		dust.Amount = IsBoss ? 48 : 24;
		dust.Lifetime = IsBoss ? 0.65f : 0.5f;
		dust.LocalCoords = false;
		dust.Texture = DungeonVisuals.GetPixelSquareTexture();
		dust.Material = GameResourceCache.GetUnshadedMaterial();
		dust.Spread = 180.0f;
		dust.Gravity = new Vector2(0, 220);
		dust.InitialVelocityMin = 55;
		dust.InitialVelocityMax = IsBoss ? 240 : 145;
		dust.ScaleAmountMin = 1.2f;
		dust.ScaleAmountMax = IsBoss ? 3.6f : 2.4f;
		var gradient = new Gradient();
		gradient.Colors = IsBoss
			? new Color[]
			{
				new Color(1.0f, 0.72f, 0.25f, 0.95f),
				new Color(0.82f, 0.16f, 0.12f, 0.85f),
				new Color(0.25f, 0.06f, 0.06f, 0.0f)
			}
			: new Color[]
			{
				new Color(0.95f, 0.35f, 0.22f, 0.92f),
				new Color(0.35f, 0.1f, 0.08f, 0.0f)
			};
		gradient.Offsets = IsBoss
			? new float[] { 0.0f, 0.45f, 1.0f }
			: new float[] { 0.0f, 1.0f };
		dust.ColorRamp = gradient;
		AddChild(dust);

		Vector2 baseScale = Scale;
		var tween = CreateTween();

		tween.TweenProperty(this, "modulate", new Color(2.2f, 2.2f, 2.2f), flashTime);
		tween.TweenProperty(this, "scale", baseScale * 1.15f, popTime)
			.SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
		tween.TweenProperty(this, "scale", baseScale * 0.3f, fadeTime)
			.SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
		tween.Parallel().TweenProperty(this, "rotation", Rotation + Mathf.DegToRad(22.0f), fadeTime)
			.SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
		tween.Parallel().TweenProperty(this, "modulate", new Color(1, 1, 1, 0), fadeTime)
			.SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
		tween.TweenCallback(Callable.From(QueueFree));

		return total;
	}

	public void MoveTowards(Vector2 target, double delta)
	{
		if (Config == null) return;
		Vector2 direction = GlobalPosition.DirectionTo(target);
		Velocity = direction * Config.Speed * World.Scale;
		MoveAndSlide();
	}
}
