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

		_sprite = GetNodeOrNull<AnimatedSprite2D>("AnimatedSprite2D");
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
			_sprite.SelfModulate = new Color(1.15f, 0.88f, 0.75f, 1.0f);
		}
	}

	protected void SetupWarningLabel(Color color)
	{
		if (_warningLabel != null) return;

		_warningLabel = new Label();
		_warningLabel.Text = "!";
		_warningLabel.AddThemeColorOverride("font_color", color);

		var font = GD.Load<Font>("res://assets/fonts/fff-forward/FFFFORWA.TTF");
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
			var level = GetTree().GetFirstNodeInGroup("Level") as Level;
			if (level != null)
			{
				Vector2 navGoal = _player.GlobalPosition;
				// Если моб далеко, он строит маршрут к своей точке окружения вокруг игрока, а не идет в одну линию со всеми
				if (distToPlayer > 70.0f * World.Scale)
				{
					float flankRadius = Mathf.Clamp((distToPlayer - 35.0f * World.Scale) * 0.65f, 42.0f * World.Scale, 125.0f * World.Scale);
					Vector2 desiredFlank = _player.GlobalPosition + new Vector2(Mathf.Cos(_flankAngle), Mathf.Sin(_flankAngle)) * flankRadius;
					navGoal = level.GetWalkableFlankTarget(_player.GlobalPosition, desiredFlank);
				}
				_navigationPath = level.GetPathToPlayer(GlobalPosition, navGoal) ?? new List<Vector2>();
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
			var controller = area.GetParent()?.GetParent() as AttackController;
			if (controller != null && controller.CurrentWeapon != null)
			{
				var player = GetTree().GetFirstNodeInGroup("Player") as Player;
				Vector2 source = player != null ? player.GlobalPosition : GlobalPosition;

				float dist = GlobalPosition.DistanceTo(source);
				float weaponRange = controller.CurrentWeapon.AttackRange * World.Scale;

				if (dist <= weaponRange + 18.0f * World.Scale)
				{
					if (controller.CanHitEnemy(GetInstanceId()))
					{
						float dmg = controller.CurrentWeapon.Damage * (player != null ? player.WeaponDamageMultiplier : 1.0f);
						TakeDamage(dmg, source, controller.CurrentWeapon.KnockbackForce, true);
					}
				}
			}
		}
	}

	// Метод для получения позиции игрока
	public Vector2 GetPlayerPosition()
	{
		var player = GetTree().GetFirstNodeInGroup("Player") as Node2D;
		return player != null ? player.GlobalPosition : GlobalPosition;
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

		var dust = new CpuParticles2D();
		dust.OneShot = true;
		dust.Emitting = true;
		dust.Explosiveness = 1.0f;
		dust.Amount = IsBoss ? 45 : 22;
		dust.Lifetime = 0.5f;
		dust.LocalCoords = false;
		dust.Spread = 180.0f;
		dust.Gravity = new Vector2(0, 260);
		dust.InitialVelocityMin = 55;
		dust.InitialVelocityMax = IsBoss ? 220 : 140;
		dust.ScaleAmountMin = 1.2f;
		dust.ScaleAmountMax = IsBoss ? 4.0f : 2.6f;
		var gradient = new Gradient();
		gradient.Colors = new Color[]
		{
			new Color(0.75f, 0.2f, 0.15f, 0.9f),
			new Color(0.35f, 0.1f, 0.08f, 0.0f)
		};
		gradient.Offsets = new float[] { 0.0f, 1.0f };
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
