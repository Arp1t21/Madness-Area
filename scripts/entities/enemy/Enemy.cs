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
	public EliteModifier ActiveEliteModifier { get; private set; } = EliteModifier.None;
	public bool IsElite => ActiveEliteModifier != EliteModifier.None;
	public int NetId { get; set; } = 0;
	public bool HasDied => _hasDied;

	protected bool IsCoopSession => NetworkManager.Instance != null && NetworkManager.Instance.IsCoopSession;
	protected bool IsServerAuthority => !IsCoopSession || (NetworkManager.Instance?.IsServerAuthority ?? true);
	public string DisplayName
	{
		get
		{
			string baseName = (Config != null && GodotObject.IsInstanceValid(Config)) ? Config.EnemyName : "Enemy";
			if (IsBoss) return $"BOSS: {baseName}";
			if (IsElite) return $"{ActiveEliteModifier.ToString().ToUpper()} {baseName}";
			return baseName;
		}
	}

	public enum EliteModifier
	{
		None = 0,
		Berserker = 1,
		Frost = 2,
		Inferno = 3,
		Volatile = 4
	}

	protected AnimatedSprite2D _sprite;
	protected Sprite2D _groundShadow;
	protected ShaderMaterial _hitFlashMat;
	protected Tween _hitFlashTween;
	protected DungeonRuneAura _bossAura;
	protected DungeonRuneAura _eliteAura;
	protected StatusEffectManager _statusManager;
	protected float _dotTickTimer = 0.0f;
	protected float _infernoAuraTimer = 0.0f;
	protected bool _hasDied = false;
	protected long _lastAttackerPeerId = -1;
	protected Player _player;
	protected AttackController _attackController;
	protected Label _warningLabel;

	private bool _hasRemoteSnapshot = false;
	private Vector2 _remoteTargetPos = Vector2.Zero;
	private string _remoteAnim = "Idle";
	private bool _remoteFlipH = false;

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

	public const float InfernoAuraRadius = 46.0f;

	public float DamageMultiplier { get; protected set; } = 1.0f;
	public float FloorSpeedMultiplier { get; protected set; } = 1.0f;
	public float FloorAttackSpeedMultiplier { get; protected set; } = 1.0f;

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

	protected void PlayAnimationSafe(string animName)
	{
		if (_sprite?.SpriteFrames != null && !string.IsNullOrEmpty(animName) && _sprite.SpriteFrames.HasAnimation(animName))
		{
			_sprite.Play(animName);
		}
	}

	public override void _Ready()
	{
		Scale = Vector2.One;
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
			_hitFlashMat = DungeonVisuals.CreateHitFlashMaterial(1.20f, 0.06f);
			_sprite.Material = _hitFlashMat;
		}
		float shadowScaleMul = (this is BigDemon || this is Quagmire) ? 0.86f : 0.56f;
		_groundShadow = DungeonVisuals.AttachGroundShadow(this, new Vector2(0, -1), new Vector2(shadowScaleMul, shadowScaleMul * 0.86f), 0.55f);
		_player = NetworkManager.GetNearestAlivePlayer(GetTree(), GlobalPosition)
			?? (GetTree().GetFirstNodeInGroup("Player") as Player);

		GameDataRegistry.EnsureVerifiedAtStartup();
		if (Config != null && !GodotObject.IsInstanceValid(Config))
		{
			Config = null;
		}
		if (Config != null && !string.IsNullOrEmpty(Config.ResourcePath))
		{
			Config = GameDataRegistry.LoadFreshEnemy(Config.ResourcePath) ?? Config;
		}
		if (Config == null || !GodotObject.IsInstanceValid(Config))
		{
			string fallbackPath = GameDataRegistry.ResolveFallbackEnemyPath(this);
			if (!string.IsNullOrEmpty(fallbackPath))
			{
				Config = GameDataRegistry.LoadFreshEnemy(fallbackPath);
			}
		}

		if (AttackConfig != null && !GodotObject.IsInstanceValid(AttackConfig))
		{
			AttackConfig = null;
		}
		if (AttackConfig != null && !string.IsNullOrEmpty(AttackConfig.ResourcePath))
		{
			AttackConfig = GameDataRegistry.LoadFreshWeapon(AttackConfig.ResourcePath) ?? AttackConfig;
		}
		if (AttackConfig == null || !GodotObject.IsInstanceValid(AttackConfig))
		{
			string fallbackWeaponPath = GameDataRegistry.ResolveFallbackEnemyWeaponPath(this);
			if (!string.IsNullOrEmpty(fallbackWeaponPath))
			{
				AttackConfig = GameDataRegistry.LoadFreshWeapon(fallbackWeaponPath);
			}
		}

		var level = GetTree().GetFirstNodeInGroup("Level") as Level;
		int levelIndex = level != null ? level.CurrentLevelIndex : 1;

		// Каждые 5 этажей мобы становятся чуть сильнее (ХП, урон, скорость бега и скорость атак)
		float healthMultiplier = GameLogic.CalculateEnemyFloorHealthMultiplier(levelIndex);
		if (IsCoopSession && NetworkManager.Instance != null)
		{
			healthMultiplier *= GameLogic.CalculateCoopEnemyHealthMultiplier(false, NetworkManager.Instance.CoopPlayerCount);
		}
		DamageMultiplier = GameLogic.CalculateEnemyFloorDamageMultiplier(levelIndex);
		FloorSpeedMultiplier = GameLogic.CalculateEnemyFloorSpeedMultiplier(levelIndex);
		FloorAttackSpeedMultiplier = GameLogic.CalculateEnemyFloorAttackSpeedMultiplier(levelIndex);

		if (Config != null && GodotObject.IsInstanceValid(Config))
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
				_attackController.Name = "EnemyWeaponController";
				_attackController.CurrentWeapon = AttackConfig;
				AddChild(_attackController);
				_attackController.ApplyWeaponSettings();
			}
		}

		SetupWarningLabel(WarningLabelColor);

		_statusManager = new StatusEffectManager { Name = "StatusEffectManager" };
		AddChild(_statusManager);

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

		Scale = new Vector2(1.75f, 1.75f);
		float bossHpFactor = 4.5f + (levelIndex / 5) * 0.8f;
		MaxHealthScaled *= bossHpFactor;
		if (IsCoopSession && NetworkManager.Instance != null)
		{
			int playerCount = NetworkManager.Instance.CoopPlayerCount;
			float nonBossMult = Mathf.Max(1.0f, GameLogic.CalculateCoopEnemyHealthMultiplier(false, playerCount));
			float bossMult = GameLogic.CalculateCoopEnemyHealthMultiplier(true, playerCount);
			MaxHealthScaled *= (bossMult / nonBossMult);
		}
		CurrentHealth = MaxHealthScaled;
		DamageMultiplier *= 1.5f;

		if (_sprite != null)
		{
			_sprite.SelfModulate = new Color(1.18f, 0.86f, 0.74f, 1.0f);
		}

		if (_bossAura == null)
		{
			// Keep boss rune ring clearly framing the scaled boss sprite, visible only in light
			float desiredWorldRadius = (this is BigDemon || this is Quagmire) ? 30.0f : 22.0f;
			float localRadius = desiredWorldRadius / Mathf.Max(0.1f, Scale.X);
			_bossAura = DungeonVisuals.CreateRuneAura(
				localRadius,
				new Color(1.0f, 0.26f, 0.22f, 0.85f),
				8,
				-1,
				DungeonRuneAura.AuraVisualStyle.Boss,
				isUnshaded: false);
			_bossAura.Position = new Vector2(0, -2);
			AddChild(_bossAura);
		}
	}

	public void PromoteToElite(EliteModifier mod, int levelIndex)
	{
		if (IsBoss || mod == EliteModifier.None || IsElite) return;
		ActiveEliteModifier = mod;
		AddToGroup("Elites");

		MaxHealthScaled *= 2.15f;
		CurrentHealth = MaxHealthScaled;
		DamageMultiplier *= 1.25f;

		Color auraCol = mod switch
		{
			EliteModifier.Berserker => new Color(0.96f, 0.14f, 0.24f, 0.86f),
			EliteModifier.Frost => new Color(0.34f, 0.88f, 1.0f, 0.85f),
			EliteModifier.Inferno => new Color(1.0f, 0.52f, 0.12f, 0.85f),
			EliteModifier.Volatile => new Color(0.78f, 0.30f, 1.0f, 0.88f),
			_ => new Color(1.0f, 0.85f, 0.3f, 0.8f)
		};

		if (_sprite != null)
		{
			Color tint = mod switch
			{
				EliteModifier.Berserker => new Color(1.22f, 0.82f, 0.75f, 1.0f),
				EliteModifier.Frost => new Color(0.76f, 0.96f, 1.25f, 1.0f),
				EliteModifier.Inferno => new Color(1.25f, 0.90f, 0.65f, 1.0f),
				EliteModifier.Volatile => new Color(1.12f, 0.76f, 1.25f, 1.0f),
				_ => Colors.White
			};
			_sprite.SelfModulate = tint;
		}

		if (_eliteAura == null)
		{
			float desiredWorldRadius = mod == EliteModifier.Inferno
				? InfernoAuraRadius
				: ((this is BigDemon || this is Quagmire) ? 24.0f : 18.0f);
			float localRadius = desiredWorldRadius / Mathf.Max(0.1f, Scale.X);
			var auraStyle = mod switch
			{
				EliteModifier.Berserker => DungeonRuneAura.AuraVisualStyle.Berserker,
				EliteModifier.Frost => DungeonRuneAura.AuraVisualStyle.Frost,
				EliteModifier.Inferno => DungeonRuneAura.AuraVisualStyle.Inferno,
				EliteModifier.Volatile => DungeonRuneAura.AuraVisualStyle.Volatile,
				_ => DungeonRuneAura.AuraVisualStyle.Sanctuary
			};
			_eliteAura = DungeonVisuals.CreateRuneAura(localRadius, auraCol, 6, -1, auraStyle, isUnshaded: false);
			_eliteAura.Position = new Vector2(0, -1);
			AddChild(_eliteAura);
		}
	}

	public void ApplyStatusEffect(StatusEffectType type, float duration, float strength = 1.0f)
	{
		if (!IsServerAuthority || CurrentHealth <= 0 || _hasDied || _statusManager == null) return;
		if (IsBoss && type == StatusEffectType.Stun)
		{
			duration *= 0.45f; // Bosses resist full stun duration while still reacting briefly
		}
		_statusManager.ApplyEffect(type, duration, strength);
	}

	public void ApplyPlayerHitRelicEffects(Player player, float baseDmg, bool isBladeWeapon, bool isCrit)
	{
		if (player == null || CurrentHealth <= 0 || _hasDied) return;

		bool hasCrimson = player.HasRelic(GameLogic.RelicCrimsonEdge);
		if ((isBladeWeapon && (isCrit || GD.Randf() < 0.45f)) || (hasCrimson && (isCrit || isBladeWeapon)))
		{
			float bleedDps = hasCrimson ? Mathf.Max(7.0f, baseDmg * 0.40f) : Mathf.Max(3.5f, baseDmg * 0.28f);
			ApplyStatusEffect(StatusEffectType.Bleed, hasCrimson ? 3.2f : 2.6f, bleedDps);
		}

		if (player.HasRelic(GameLogic.RelicEmberCore))
		{
			ApplyStatusEffect(StatusEffectType.Burn, 3.0f, Mathf.Max(6.0f, baseDmg * 0.35f));
		}

		if (player.HasRelic(GameLogic.RelicVenomCoat))
		{
			ApplyStatusEffect(StatusEffectType.Poison, 3.5f, 5.0f);
			ApplyStatusEffect(StatusEffectType.Slow, 2.0f, 0.15f);
		}
	}

	public void ApplyEliteOnHitToPlayer(Player targetPlayer)
	{
		if (targetPlayer == null || !IsElite) return;
		if (ActiveEliteModifier == EliteModifier.Frost)
		{
			targetPlayer.ApplyStatusEffect(StatusEffectType.Slow, 1.8f, 0.28f);
		}
		else if (ActiveEliteModifier == EliteModifier.Inferno)
		{
			targetPlayer.ApplyStatusEffect(StatusEffectType.Burn, 2.0f, 2.2f);
		}
	}

	protected float GetEnemySpeedMultiplier()
	{
		float mult = FloorSpeedMultiplier;
		if (_statusManager != null && _statusManager.HasEffect(StatusEffectType.Slow))
		{
			float slow = Mathf.Clamp(_statusManager.GetEffectStrength(StatusEffectType.Slow), 0.0f, 0.75f);
			mult *= (1.0f - slow);
		}
		if (ActiveEliteModifier == EliteModifier.Berserker && MaxHealthScaled > 0.0f && CurrentHealth <= MaxHealthScaled * 0.55f)
		{
			mult *= 1.35f;
		}
		return mult;
	}

	private void ApplyElementalDamage(float dmg, Color numberColor)
	{
		if (!IsServerAuthority || CurrentHealth <= 0 || _hasDied || dmg <= 0.0f) return;
		CurrentHealth -= dmg;
		if (IsCoopSession)
		{
			Rpc(nameof(RpcSyncElementalHit), dmg, numberColor, CurrentHealth);
		}
		else
		{
			PlayElementalTickVfx(dmg, numberColor);
		}
		if (CurrentHealth <= 0)
		{
			Die();
		}
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RpcSyncElementalHit(float dmg, Color numberColor, float newHealth)
	{
		int sender = Multiplayer.GetRemoteSenderId();
		if (sender != 0 && sender != 1) return;
		if (!IsServerAuthority) CurrentHealth = newHealth;
		PlayElementalTickVfx(dmg, numberColor);
	}

	private void PlayElementalTickVfx(float dmg, Color numberColor)
	{
		SpawnDamageNumber(dmg, false, numberColor);
		if (GetParent() != null)
		{
			DungeonVisuals.SpawnImpactSparks(
				GetParent(),
				GlobalPosition + new Vector2(0, -8.0f * Scale.Y),
				Vector2.Up,
				numberColor,
				3,
				18.0f,
				32.0f,
				0.55f);
		}
	}

	private void ProcessStatusDotsAndAuras(float dt)
	{
		if (!IsServerAuthority || _statusManager == null || CurrentHealth <= 0 || _hasDied) return;

		_dotTickTimer += dt;
		if (_dotTickTimer >= 0.45f)
		{
			float tickWindow = _dotTickTimer;
			_dotTickTimer = 0.0f;

			if (_statusManager.HasEffect(StatusEffectType.Burn) && CurrentHealth > 0)
			{
				float dmg = GameLogic.ComputeStatusDotTickDamage(_statusManager.GetEffectStrength(StatusEffectType.Burn), tickWindow);
				ApplyElementalDamage(dmg, new Color(1.0f, 0.52f, 0.16f));
			}
			if (_statusManager.HasEffect(StatusEffectType.Bleed) && CurrentHealth > 0)
			{
				float dmg = GameLogic.ComputeStatusDotTickDamage(_statusManager.GetEffectStrength(StatusEffectType.Bleed), tickWindow);
				ApplyElementalDamage(dmg, new Color(0.95f, 0.18f, 0.25f));
			}
			if (_statusManager.HasEffect(StatusEffectType.Poison) && CurrentHealth > 0)
			{
				float dmg = GameLogic.ComputeStatusDotTickDamage(_statusManager.GetEffectStrength(StatusEffectType.Poison), tickWindow);
				ApplyElementalDamage(dmg, new Color(0.35f, 0.92f, 0.30f));
			}
		}

		if (ActiveEliteModifier == EliteModifier.Inferno && CurrentHealth > 0)
		{
			_infernoAuraTimer -= dt;
			if (_infernoAuraTimer <= 0.0f)
			{
				_infernoAuraTimer = 0.65f;
				foreach (Player target in NetworkManager.GetAlivePlayers(GetTree()))
				{
					if (GlobalPosition.DistanceTo(target.GlobalPosition) <= InfernoAuraRadius)
					{
						target.ApplyStatusEffect(StatusEffectType.Burn, 1.5f, 2.0f);
					}
				}
			}
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
		_warningLabel.Position = new Vector2(-20, -24);
		_warningLabel.Visible = false;
		AddChild(_warningLabel);
	}

	protected Vector2 UpdateNavigationDirection(double delta, float waypointReachDist = 12.0f)
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
				if (distToPlayer > 70.0f)
				{
					float flankRadius = Mathf.Clamp((distToPlayer - 35.0f) * 0.65f, 42.0f, 125.0f);
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
		if (distToPlayer > 38.0f && distToPlayer < 140.0f)
		{
			Vector2 desiredPosAroundPlayer = _player.GlobalPosition + new Vector2(Mathf.Cos(_flankAngle), Mathf.Sin(_flankAngle)) * 45.0f;
			Vector2 toFlankSlot = (desiredPosAroundPlayer - GlobalPosition).Normalized();
			float blend = Mathf.Clamp((distToPlayer - 38.0f) / 100.0f, 0.0f, 0.55f);
			baseDir = (baseDir * (1.0f - blend) + toFlankSlot * blend).Normalized();
		}

		return baseDir;
	}

	protected Vector2 GetStrafeDirectionAroundPlayer()
	{
		if (_player == null || !GodotObject.IsInstanceValid(_player)) return Vector2.Zero;
		Vector2 desiredRingPos = _player.GlobalPosition + new Vector2(Mathf.Cos(_flankAngle), Mathf.Sin(_flankAngle)) * 110.0f;
		if (GlobalPosition.DistanceTo(desiredRingPos) > 24.0f)
		{
			return (desiredRingPos - GlobalPosition).Normalized();
		}
		Vector2 radial = (GlobalPosition - _player.GlobalPosition).Normalized();
		return new Vector2(-radial.Y * _flankSign, radial.X * _flankSign);
	}

	protected Vector2 ComputeEnemySeparation(float separationRadius = 27.0f)
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
	public sealed override void _PhysicsProcess(double delta)
	{
		UpdateEnemyClientVisualTick(delta);
		if (IsCoopSession && !IsServerAuthority)
		{
			InterpolateRemoteSnapshot(delta);
			return;
		}
		TickEnemyAI(delta);
	}

	protected virtual void UpdateEnemyClientVisualTick(double delta) { }

	protected virtual void TickEnemyAI(double delta) { }

	public void ApplyNetworkSnapshot(Vector2 pos, float hp, string anim, bool flipH, bool warningVisible)
	{
		if (!IsCoopSession || IsServerAuthority) return;
		_remoteTargetPos = pos;
		_hasRemoteSnapshot = true;
		if (GlobalPosition.DistanceTo(pos) > 96.0f)
		{
			GlobalPosition = pos;
		}
		CurrentHealth = hp;
		_remoteAnim = string.IsNullOrEmpty(anim) ? IdleAnimationName : anim;
		_remoteFlipH = flipH;
		if (GodotObject.IsInstanceValid(_warningLabel))
		{
			_warningLabel.Visible = warningVisible && !_hasDied;
		}
	}

	private void InterpolateRemoteSnapshot(double delta)
	{
		if (!_hasRemoteSnapshot || _hasDied) return;
		float lerpFactor = Mathf.Clamp((float)delta * 16.0f, 0.0f, 1.0f);
		GlobalPosition = GlobalPosition.Lerp(_remoteTargetPos, lerpFactor);
		if (_sprite != null)
		{
			_sprite.FlipH = _remoteFlipH;
			if (!string.IsNullOrEmpty(_remoteAnim) && _sprite.SpriteFrames != null && _sprite.SpriteFrames.HasAnimation(_remoteAnim))
			{
				if (_sprite.Animation != _remoteAnim || !_sprite.IsPlaying())
				{
					_sprite.Play(_remoteAnim);
				}
			}
		}
	}

	public string GetCurrentAnimationName()
	{
		return _sprite != null ? _sprite.Animation.ToString() : IdleAnimationName;
	}

	public bool GetSpriteFlipH() => _sprite != null && _sprite.FlipH;

	public bool IsWarningVisible() => GodotObject.IsInstanceValid(_warningLabel) && _warningLabel.Visible;

	protected void RefreshNearestTarget()
	{
		if (IsCoopSession)
		{
			Player nearest = NetworkManager.GetNearestAlivePlayer(GetTree(), GlobalPosition);
			if (nearest != null && GodotObject.IsInstanceValid(nearest))
			{
				_player = nearest;
				return;
			}
		}

		if (!GodotObject.IsInstanceValid(_player) || _player.IsDead)
		{
			_player = NetworkManager.GetNearestAlivePlayer(GetTree(), GlobalPosition)
				?? (GetTree().GetFirstNodeInGroup("Player") as Player);
		}
	}

	protected bool StepEnemyActionState(double delta)
	{
		RefreshNearestTarget();
		if (_player == null || !GodotObject.IsInstanceValid(_player) || _player.IsDead || Config == null || CurrentHealth <= 0)
		{
			Velocity = Velocity.Lerp(Vector2.Zero, 0.1f);
			return false;
		}

		ProcessStatusDotsAndAuras((float)delta);
		if (CurrentHealth <= 0 || _hasDied)
		{
			Velocity = Vector2.Zero;
			return false;
		}

		if (_statusManager != null && (_statusManager.HasEffect(StatusEffectType.Stun) || _statusManager.HasEffect(StatusEffectType.Root)))
		{
			if (_isPreparingAttack) CancelAttack();
			Velocity = Velocity.Lerp(Vector2.Zero, 0.25f);
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
		float engageDist = baseAttackRange * approachDistanceRatio;
		float retreatDist = retreatDistance;
		float triggerRange = baseAttackRange;
		float dist = GlobalPosition.DistanceTo(_player.GlobalPosition);

		float spdMult = GetEnemySpeedMultiplier();
		Vector2 direction;
		float currentSpeed = Config.Speed * spdMult;

		if (dist > engageDist)
		{
			direction = UpdateNavigationDirection(delta, approachWaypointReach);
		}
		else if (dist < retreatDist)
		{
			direction = (GlobalPosition - _player.GlobalPosition).Normalized();
			currentSpeed = Config.Speed * retreatSpeedFactor * spdMult;
		}
		else
		{
			direction = GetStrafeDirectionAroundPlayer();
			currentSpeed = Config.Speed * strafeSpeedFactor * spdMult;
		}

		Vector2 separation = ComputeEnemySeparation(30.0f);
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
			_sprite.Play(Velocity.Length() > 2.0f ? WalkAnimationName : IdleAnimationName);
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

		Vector2 targetVelocity = direction * Config.Speed * speedMultiplier * GetEnemySpeedMultiplier();
		Velocity = Velocity.Lerp(targetVelocity, velocityLerp);

		if (_sprite != null)
		{
			if (Mathf.Abs(Velocity.X) > 2.0f)
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
			if (GlobalPosition.DistanceTo(_player.GlobalPosition) > Config.AttackRange + 15.0f)
			{
				CancelAttack();
				return;
			}
		}

		float effectivePreDelay = Config.PreAttackDelay / Mathf.Max(0.1f, FloorAttackSpeedMultiplier);
		if (_preAttackTimer >= effectivePreDelay)
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
			TriggerStop(Config.StopDuration / Mathf.Max(0.1f, FloorAttackSpeedMultiplier));
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
		if (!IsServerAuthority || CurrentHealth <= 0 || _hasDied) return;

		if (area.IsInGroup("PlayerWeapon"))
		{
			var controller = GameResourceCache.FindAncestor<AttackController>(area);
			if (controller != null && controller.CurrentWeapon != null)
			{
				Player attacker = controller.OwnerNode as Player;
				if (attacker == null || !GodotObject.IsInstanceValid(attacker))
				{
					attacker = NetworkManager.GetNearestAlivePlayer(GetTree(), GlobalPosition)
						?? (GetTree().GetFirstNodeInGroup("Player") as Player);
				}
				Vector2 source = attacker != null ? attacker.GlobalPosition : controller.GlobalPosition;

				float dist = GlobalPosition.DistanceTo(source);
				float weaponRange = controller.CurrentWeapon.AttackRange;

				if (dist <= weaponRange + 18.0f)
				{
					if (controller.CanHitEnemy(GetInstanceId()))
					{
						float rawDmg = controller.CurrentWeapon.Damage * (attacker != null ? attacker.WeaponDamageMultiplier : 1.0f);
						bool isBlade = GameLogic.IsBladeWeaponName(controller.CurrentWeapon.WeaponName);
						float finalDmg = rawDmg;
						bool isCrit = false;
						if (attacker != null)
						{
							(finalDmg, isCrit) = attacker.RollDamageCrit(rawDmg, isBlade);
						}
						TakeDamage(finalDmg, source, controller.CurrentWeapon.KnockbackForce, true, isCrit, attacker);
						if (attacker != null)
						{
							ApplyPlayerHitRelicEffects(attacker, finalDmg, isBlade, isCrit);
						}
					}
				}
			}
		}
	}

	// Метод для получения позиции игрока
	public Vector2 GetPlayerPosition()
	{
		RefreshNearestTarget();
		return _player != null && GodotObject.IsInstanceValid(_player) ? _player.GlobalPosition : GlobalPosition;
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

		if (wantsToMove && moved < 0.7f * (float)delta)
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

	public virtual void TakeDamage(float amount, Vector2 knockbackSource, float knockbackForce = 15.0f, bool isMelee = true, bool isCrit = false, Player attacker = null)
	{
		if (!IsServerAuthority || CurrentHealth <= 0 || _hasDied) return;

		if (attacker != null && GodotObject.IsInstanceValid(attacker))
		{
			_lastAttackerPeerId = attacker.PeerId;
			if (!IsCoopSession) _player = attacker;
		}

		// Сопротивления из конфига врага (к ближнему/дальнему бою)
		if (Config != null)
		{
			float res = isMelee ? Config.MeleeResistance : Config.RangedResistance;
			amount *= (1.0f - Mathf.Clamp(res, 0.0f, 0.95f));
		}

		CurrentHealth -= amount;

		float effectiveKnockback = IsBoss ? knockbackForce * 0.35f : knockbackForce;
		ApplyKnockback(knockbackSource, effectiveKnockback);

		if (IsCoopSession)
		{
			Rpc(nameof(RpcSyncEnemyHit), amount, knockbackSource, isMelee, isCrit, CurrentHealth);
		}
		else
		{
			PlayEnemyHitVfx(amount, knockbackSource, isMelee, isCrit);
		}

		if (CurrentHealth <= 0)
		{
			Die();
		}
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RpcSyncEnemyHit(float amount, Vector2 knockbackSource, bool isMelee, bool isCrit, float newHealth)
	{
		int sender = Multiplayer.GetRemoteSenderId();
		if (sender != 0 && sender != 1) return;
		if (!IsServerAuthority)
		{
			CurrentHealth = newHealth;
		}
		PlayEnemyHitVfx(amount, knockbackSource, isMelee, isCrit);
	}

	private void PlayEnemyHitVfx(float amount, Vector2 knockbackSource, bool isMelee, bool isCrit)
	{
		FlashRed();
		if (GetParent() != null)
		{
			Vector2 hitDir = knockbackSource != default ? (GlobalPosition - knockbackSource) : Vector2.Up;
			Color sparkCol = isCrit ? new Color(1.0f, 0.92f, 0.25f) : (Config?.FlashColor ?? new Color(1.0f, 0.78f, 0.32f));
			DungeonVisuals.SpawnImpactSparks(
				GetParent(),
				GlobalPosition + new Vector2(0, -10.0f * Scale.Y),
				hitDir,
				sparkCol,
				isCrit ? 12 : (isMelee ? 9 : 6),
				48.0f,
				isCrit ? 90.0f : 68.0f,
				isCrit ? 1.25f : (IsBoss ? 1.2f : 0.85f));
		}
		SpawnDamageNumber(amount, isCrit);
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
		Velocity = dir * force * 10.0f;
		MoveAndSlide();
	}

	protected void SpawnDamageNumber(float amount, bool isCrit = false, Color? overrideColor = null)
	{
		if (GetParent() == null) return;
		var ft = new FloatingText();
		GetParent().AddChild(ft);
		float offsetX = (float)GD.RandRange(-10.0f, 10.0f);
		float offsetY = (float)GD.RandRange(-4.0f, 4.0f);
		ft.GlobalPosition = GlobalPosition + new Vector2(offsetX, -12.0f + offsetY);
		Color textColor = overrideColor ?? (isCrit ? new Color(1.0f, 0.86f, 0.20f) : (Config?.DamageNumberColor ?? new Color(1, 1, 0)));
		int displayDmg = Mathf.Max(1, Mathf.RoundToInt(amount));
		string text = isCrit ? $"{displayDmg}!" : displayDmg.ToString();
		ft.Init(text, textColor);
	}

	protected virtual void Die()
	{
		if (!IsServerAuthority || _hasDied) return;
		_hasDied = true;
		CurrentHealth = 0.0f;

		if (GodotObject.IsInstanceValid(_warningLabel))
		{
			_warningLabel.Visible = false;
		}

		Vector2 deathPos = GlobalPosition;
		Node parentNode = GetParent();

		Player killer = _lastAttackerPeerId >= 0
			? NetworkManager.GetPlayerByPeerId(GetTree(), _lastAttackerPeerId)
			: null;
		if (killer == null || !GodotObject.IsInstanceValid(killer))
		{
			killer = GodotObject.IsInstanceValid(_player)
				? _player
				: NetworkManager.GetNearestAlivePlayer(GetTree(), deathPos);
		}
		if (killer != null && GodotObject.IsInstanceValid(killer))
		{
			killer.OnEnemyKilled(IsBoss, IsElite);
			TriggerDeathRelicEffects(deathPos, parentNode, killer);
		}

		if (IsCoopSession)
		{
			Rpc(nameof(RpcSyncEnemyDied), deathPos);
		}

		float animDuration = PlayEnemyDeathVisuals(deathPos, parentNode);

		if (IsBoss)
		{
			// Босс гарантированно роняет горсть монет и кристаллов маны
			var lvl = GetTree()?.GetFirstNodeInGroup("Level") as Level;
			bool pitchBlackBoss = lvl != null && lvl.CurrentFloorEvent == GameLogic.FloorEventType.PitchBlack;
			int bossCoins = pitchBlackBoss ? 9 : 6;
			for (int i = 0; i < bossCoins; i++)
			{
				Vector2 offset = new Vector2((float)GD.RandRange(-8, 8), (float)GD.RandRange(-8, 8));
				DelayedDrop(deathPos + offset, parentNode, isMana: false, delay: animDuration);
			}
			for (int i = 0; i < 3; i++)
			{
				Vector2 offset = new Vector2((float)GD.RandRange(-7, 7), (float)GD.RandRange(-7, 7));
				DelayedDrop(deathPos + offset, parentNode, isMana: true, delay: animDuration);
			}
		}
		else if (Config != null)
		{
			var lvl = GetTree()?.GetFirstNodeInGroup("Level") as Level;
			bool pitchBlack = lvl != null && lvl.CurrentFloorEvent == GameLogic.FloorEventType.PitchBlack;
			int bonusCoins = IsElite ? 2 : 0;
			if (pitchBlack && GD.Randf() < 0.50f) bonusCoins++;
			for (int c = 0; c < bonusCoins; c++)
			{
				Vector2 offset = new Vector2((float)GD.RandRange(-7, 7), (float)GD.RandRange(-7, 7));
				DelayedDrop(deathPos + offset, parentNode, isMana: false, delay: animDuration);
			}

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

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RpcSyncEnemyDied(Vector2 deathPos)
	{
		if (Multiplayer.GetRemoteSenderId() != 1 || _hasDied) return;
		_hasDied = true;
		CurrentHealth = 0.0f;
		if (GodotObject.IsInstanceValid(_warningLabel))
		{
			_warningLabel.Visible = false;
		}
		PlayEnemyDeathVisuals(deathPos, GetParent());
	}

	private float PlayEnemyDeathVisuals(Vector2 deathPos, Node parentNode)
	{
		if (IsElite && ActiveEliteModifier == EliteModifier.Volatile && parentNode != null)
		{
			SpawnVolatileDeathBlast(deathPos, parentNode);
		}
		return PlayDeathAnimation();
	}

	private void TriggerDeathRelicEffects(Vector2 deathPos, Node parentNode, Player killer)
	{
		if (parentNode == null || killer == null) return;

		if (killer.HasRelic(GameLogic.RelicChainSpark))
		{
			var candidates = new List<(Enemy enemy, float dist)>();
			foreach (Node node in GetTree().GetNodesInGroup("Enemies"))
			{
				if (node is Enemy other && other != this && GodotObject.IsInstanceValid(other) && other.CurrentHealth > 0 && !other._hasDied)
				{
					float d = deathPos.DistanceTo(other.GlobalPosition);
					if (d <= 85.0f) candidates.Add((other, d));
				}
			}
			candidates.Sort((a, b) => a.dist.CompareTo(b.dist));
			for (int i = 0; i < Mathf.Min(2, candidates.Count); i++)
			{
				Enemy target = candidates[i].enemy;
				SpawnChainArcVisual(parentNode, deathPos + new Vector2(0, -6), target.GlobalPosition + new Vector2(0, -6), new Color(0.45f, 0.88f, 1.0f));
				target.TakeDamage(9.0f * killer.WeaponDamageMultiplier, deathPos, 4.0f, false, false, killer);
			}
		}

		if (killer.HasRelic(GameLogic.RelicVolatileSigil))
		{
			float blastDmg = Mathf.Clamp(MaxHealthScaled * 0.35f, 7.0f, 22.0f) * killer.WeaponDamageMultiplier;
			float blastRadius = 38.0f;
			SpawnBlastRingVisual(parentNode, deathPos, blastRadius, new Color(1.0f, 0.36f, 0.22f, 0.85f));
			foreach (Node node in GetTree().GetNodesInGroup("Enemies"))
			{
				if (node is Enemy other && other != this && GodotObject.IsInstanceValid(other) && other.CurrentHealth > 0 && !other._hasDied)
				{
					if (deathPos.DistanceTo(other.GlobalPosition) <= blastRadius)
					{
						other.TakeDamage(blastDmg, deathPos, 10.0f, false, false, killer);
					}
				}
			}
		}
	}

	private void SpawnVolatileDeathBlast(Vector2 deathPos, Node parentNode)
	{
		var ring = new VolatileDeathHazard(deathPos, 42.0f, 0.55f, Mathf.Max(5.0f, Config.Damage * DamageMultiplier * 0.75f));
		parentNode.AddChild(ring);
	}

	private static void SpawnChainArcVisual(Node host, Vector2 from, Vector2 to, Color col)
	{
		var line = new Line2D
		{
			Width = 1.6f,
			DefaultColor = col,
			Material = GameResourceCache.GetUnshadedMaterial(),
			ZIndex = 12
		};
		Vector2 mid = (from + to) * 0.5f + new Vector2((float)GD.RandRange(-7, 7), (float)GD.RandRange(-7, 7));
		line.AddPoint(from);
		line.AddPoint(mid);
		line.AddPoint(to);
		host.AddChild(line);
		var tw = line.CreateTween();
		tw.TweenProperty(line, "modulate:a", 0.0f, 0.16f);
		tw.TweenCallback(Callable.From(line.QueueFree));
	}

	private static void SpawnBlastRingVisual(Node host, Vector2 pos, float radius, Color col)
	{
		var ring = new ExpandingRingVfx(pos, radius, col, 0.24f);
		host.AddChild(ring);
	}

	protected void BroadcastBossTelegraphLine(Vector2 origin, Vector2 dir, float length, float width, float duration, Color color)
	{
		if (IsCoopSession)
		{
			Rpc(nameof(RpcSpawnTelegraphLine), origin, dir, length, width, duration, color);
		}
		else
		{
			RpcSpawnTelegraphLine(origin, dir, length, width, duration, color);
		}
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RpcSpawnTelegraphLine(Vector2 origin, Vector2 dir, float length, float width, float duration, Color color)
	{
		int sender = Multiplayer.GetRemoteSenderId();
		if (IsCoopSession && sender != 0 && sender != 1) return;
		Node host = GetTree()?.CurrentScene ?? GetParent();
		host?.AddChild(new TelegraphLineNode(origin, dir, length, width, duration, color));
	}

	protected void BroadcastBossTelegraphCircle(Vector2 center, float radius, float duration, Color color)
	{
		if (IsCoopSession)
		{
			Rpc(nameof(RpcSpawnTelegraphCircle), center, radius, duration, color);
		}
		else
		{
			RpcSpawnTelegraphCircle(center, radius, duration, color);
		}
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RpcSpawnTelegraphCircle(Vector2 center, float radius, float duration, Color color)
	{
		int sender = Multiplayer.GetRemoteSenderId();
		if (IsCoopSession && sender != 0 && sender != 1) return;
		Node host = GetTree()?.CurrentScene ?? GetParent();
		host?.AddChild(new TelegraphCircleNode(center, radius, duration, color));
	}

	protected void BroadcastBossSlamVfx(Vector2 center, float radius, Color ringColor, float duration)
	{
		if (IsCoopSession)
		{
			Rpc(nameof(RpcSpawnSlamVfx), center, radius, ringColor, duration);
		}
		else
		{
			RpcSpawnSlamVfx(center, radius, ringColor, duration);
		}
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RpcSpawnSlamVfx(Vector2 center, float radius, Color ringColor, float duration)
	{
		int sender = Multiplayer.GetRemoteSenderId();
		if (IsCoopSession && sender != 0 && sender != 1) return;
		Node host = GetTree()?.CurrentScene ?? GetParent();
		host?.AddChild(new ExpandingRingVfx(center, radius, ringColor, duration));
	}

	protected void BroadcastBossOrbs(Vector2 origin, float offsetRadius, int count, float startAngle, float angleStep, float speed, float range, float damage, Color glowColor)
	{
		if (IsCoopSession)
		{
			Rpc(nameof(RpcSpawnBossOrbs), origin, offsetRadius, count, startAngle, angleStep, speed, range, damage, glowColor);
		}
		else
		{
			RpcSpawnBossOrbs(origin, offsetRadius, count, startAngle, angleStep, speed, range, damage, glowColor);
		}
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RpcSpawnBossOrbs(Vector2 origin, float offsetRadius, int count, float startAngle, float angleStep, float speed, float range, float damage, Color glowColor)
	{
		int sender = Multiplayer.GetRemoteSenderId();
		if (IsCoopSession && sender != 0 && sender != 1) return;

		var orbPrefab = GD.Load<PackedScene>("res://scene/weapons/bullets/cultist_orb.tscn");
		Node host = GetTree()?.CurrentScene ?? GetParent();
		if (orbPrefab == null || host == null) return;

		for (int i = 0; i < count; i++)
		{
			var orbNode = orbPrefab.Instantiate();
			if (orbNode is Projectile orb)
			{
				Vector2 dir = Vector2.Right.Rotated(startAngle + i * angleStep);
				orb.GlobalPosition = origin + dir * offsetRadius;
				orb.Direction = dir;
				orb.Rotation = dir.Angle();
				orb.Speed = speed;
				orb.Range = range;
				orb.Damage = damage;
				orb.IsPlayerOwned = false;
				orb.SourceEnemy = this;
				orb.GlowColor = glowColor;
				var area = orb.GetNodeOrNull<Area2D>("Area2D");
				if (area != null && !area.IsInGroup("EnemyWeapon")) area.AddToGroup("EnemyWeapon");
				host.AddChild(orb);
			}
		}
	}

	private void DelayedDrop(Vector2 pos, Node parentNode, bool isMana, float delay)
	{
		var timer = GetTree().CreateTimer(delay, processAlways: false);
		timer.Timeout += () =>
		{
			if (parentNode == null || !GodotObject.IsInstanceValid(parentNode)) return;
			if (NetworkManager.Instance != null && NetworkManager.Instance.IsCoopSession)
			{
				var lvl = parentNode as Level ?? parentNode.GetTree()?.GetFirstNodeInGroup("Level") as Level;
				lvl?.ServerSpawnPickupDrop(pos, isMana);
				return;
			}
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
		if (_eliteAura != null && GodotObject.IsInstanceValid(_eliteAura))
		{
			_eliteAura.QueueFree();
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
		dust.Gravity = new Vector2(0, 75);
		dust.InitialVelocityMin = 20;
		dust.InitialVelocityMax = IsBoss ? 80 : 50;
		dust.ScaleAmountMin = 0.5f;
		dust.ScaleAmountMax = IsBoss ? 1.5f : 1.0f;
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
		Velocity = direction * Config.Speed * GetEnemySpeedMultiplier();
		MoveAndSlide();
	}
}

public partial class ExpandingRingVfx : Node2D
{
	private readonly float _maxRadius;
	private readonly float _startRadius;
	private readonly Color _color;
	private readonly float _duration;
	private readonly float _lineWidth;
	private readonly float _fillAlphaScale;
	private float _elapsed = 0.0f;

	public ExpandingRingVfx() { }

	public ExpandingRingVfx(
		Vector2 pos,
		float maxRadius,
		Color color,
		float duration,
		float lineWidth = 2.2f,
		float fillAlphaScale = 0.16f,
		float startRadius = 4.0f)
	{
		GlobalPosition = pos;
		_maxRadius = maxRadius;
		_startRadius = startRadius;
		_color = color;
		_duration = duration;
		_lineWidth = lineWidth;
		_fillAlphaScale = fillAlphaScale;
		ZIndex = 12;
		Material = GameResourceCache.GetUnshadedMaterial();
	}

	public override void _Process(double delta)
	{
		_elapsed += (float)delta;
		if (_elapsed >= _duration)
		{
			QueueFree();
			return;
		}
		QueueRedraw();
	}

	public override void _Draw()
	{
		float p = Mathf.Clamp(_elapsed / Mathf.Max(0.01f, _duration), 0.0f, 1.0f);
		float r = Mathf.Lerp(_startRadius, _maxRadius, p);
		float alpha = (1.0f - p) * _color.A;
		if (_fillAlphaScale > 0.001f)
		{
			DrawCircle(Vector2.Zero, r, new Color(_color.R, _color.G, _color.B, alpha * _fillAlphaScale));
		}
		DrawArc(Vector2.Zero, r, 0f, Mathf.Tau, 32, new Color(_color.R, _color.G, _color.B, alpha), _lineWidth);
	}
}

public partial class VolatileDeathHazard : Node2D
{
	private readonly float _radius;
	private readonly float _delay;
	private readonly float _damage;
	private float _elapsed = 0.0f;
	private bool _detonated = false;

	public VolatileDeathHazard() { }

	public VolatileDeathHazard(Vector2 pos, float radius, float delay, float damage)
	{
		GlobalPosition = pos;
		_radius = radius;
		_delay = delay;
		_damage = damage;
		ZIndex = 3;
		Material = GameResourceCache.GetUnshadedMaterial();
	}

	public override void _Process(double delta)
	{
		if (_detonated) return;
		_elapsed += (float)delta;
		if (_elapsed >= _delay)
		{
			_detonated = true;
			Detonate();
			return;
		}
		QueueRedraw();
	}

	private void Detonate()
	{
		var tree = GetTree();
		bool isServerAuthority = NetworkManager.Instance == null || NetworkManager.Instance.IsServerAuthority;
		if (isServerAuthority && tree != null)
		{
			foreach (Player player in NetworkManager.GetAlivePlayers(tree))
			{
				if (GlobalPosition.DistanceTo(player.GlobalPosition) <= _radius)
				{
					player.TakeDamage(_damage, GlobalPosition, 8.0f, false);
				}
			}
			foreach (Node n in tree.GetNodesInGroup("Enemies"))
			{
				if (n is Enemy enemy && GodotObject.IsInstanceValid(enemy) && enemy.CurrentHealth > 0 && GlobalPosition.DistanceTo(enemy.GlobalPosition) <= _radius)
				{
					enemy.TakeDamage(_damage * 1.3f, GlobalPosition, 10.0f, false);
				}
			}
		}
		var host = GetParent();
		if (host != null)
		{
			host.AddChild(new ExpandingRingVfx(GlobalPosition, _radius * 1.1f, new Color(0.85f, 0.30f, 1.0f, 0.95f), 0.25f));
			DungeonVisuals.SpawnImpactSparks(host, GlobalPosition, Vector2.Up, new Color(0.85f, 0.30f, 1.0f), 14, 55.0f, 95.0f, 1.0f);
		}
		QueueFree();
	}

	public override void _Draw()
	{
		float p = Mathf.Clamp(_elapsed / Mathf.Max(0.01f, _delay), 0.0f, 1.0f);
		DrawCircle(Vector2.Zero, _radius * p, new Color(0.82f, 0.26f, 1.0f, 0.24f));
		DrawArc(Vector2.Zero, _radius, 0f, Mathf.Tau, 32, new Color(0.95f, 0.45f, 1.0f, 0.85f), 1.6f);
	}
}
