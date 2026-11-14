using Godot;
using System;
using System.Collections.Generic;

public partial class Player : CharacterBody2D
{
	[Export] public PlayerData Config;
	[Export] public float DamageShakeIntensity = 5.0f;

	[ExportGroup("Torch Light")]
	[Export] public int TorchRadius = 320;
	[Export] public float TorchGradientMiddleOffset = 0.2f;
	[Export] public float TorchFillToX = 0.8f;
	[Export] public float TorchFillToY = 0.8f;

	private PointLight2D _torchLight;
	private CanvasLayer _damageFlashLayer;
	private ColorRect _damageFlashRect;
	private Sprite2D _groundShadow;
	private ShaderMaterial _hitFlashMat;
	private Tween _hitFlashTween;
	private float _footstepTimer = 0.0f;

	private AnimatedSprite2D _animatedSprite;
	public AnimatedSprite2D Sprite => _animatedSprite;
	private Node2D _abilityManager;
	private Camera2D _camera;

	public List<AttackController> WeaponSlots = new List<AttackController>();
	private int _currentSlotIndex = 0;
	public int CurrentSlotIndex => _currentSlotIndex;

	// Текущие показатели и бонусы от алтаря снабжения
	public float CurrentHealth;
	public float CurrentMana;
	public float BonusMaxHealth = 0.0f;
	public float BonusMaxMana = 0.0f;
	public float BonusHealthRegen = 0.0f;
	public float BonusManaRegen = 0.0f;
	public float BonusMoveSpeed = 0.0f;
	public float WeaponDamageMultiplier = 1.0f;
	public int CoinCount = 0;
	public Dictionary<string, int> ShopPurchaseCounts = new Dictionary<string, int>();

	public int GetShopPurchaseCount(string itemId)
	{
		if (string.IsNullOrEmpty(itemId) || ShopPurchaseCounts == null) return 0;
		return ShopPurchaseCounts.TryGetValue(itemId, out int count) ? count : 0;
	}

	public void RecordShopPurchase(string itemId)
	{
		if (string.IsNullOrEmpty(itemId)) return;
		ShopPurchaseCounts ??= new Dictionary<string, int>();
		ShopPurchaseCounts[itemId] = GetShopPurchaseCount(itemId) + 1;
	}

	public WeaponData EnsureUniqueWeaponInSlot(int slotIndex)
	{
		if (slotIndex < 0 || slotIndex >= WeaponSlots.Count) return null;
		var ctrl = WeaponSlots[slotIndex];
		if (ctrl == null || ctrl.CurrentWeapon == null) return null;

		if (!string.IsNullOrEmpty(ctrl.CurrentWeapon.ResourcePath))
		{
			ctrl.CurrentWeapon = ctrl.CurrentWeapon.CreateUniqueCopy();
		}
		return ctrl.CurrentWeapon;
	}

	public float MaxHealthEffective => (Config != null ? Config.MaxHealth : 100.0f) + BonusMaxHealth;
	public float MaxManaEffective => (Config != null ? Config.MaxMana : 100.0f) + BonusMaxMana;
	public float HealthRegenEffective => (Config != null ? Config.HealthRegen : 0.0f) + BonusHealthRegen;
	public float ManaRegenEffective => (Config != null ? Config.ManaRegen : 0.0f) + BonusManaRegen;
	public float WalkSpeedEffective => (Config != null ? Config.WalkSpeed : 60.0f) + BonusMoveSpeed;

	private StatusEffectManager _statusManager;
	private Vector2 _externalVelocity = Vector2.Zero;
	private float _noManaWarningCooldown = 0.0f;

	private readonly List<IInteractable> _interactablesInRange = new List<IInteractable>();
	private bool _eJustPressed = false;

	public int Ability1Level = 0; // Fireball (Q)
	public int Ability2Level = 0; // Passive Shield + Active Arcane Barrier & Dash (V)
	public int SkillPoints = 0;

	[ExportGroup("Abilities")]
	[Export] public float FireballBaseDamage = 20.0f;
	[Export] public float FireballDamagePerLevel = 6.0f;

	public float FireballCooldownTimer = 0.0f;
	public const float FireballCooldownMax = 10.0f;

	public float ShieldCooldownTimer = 0.0f;
	public const float ShieldCooldownMax = 8.0f;
	public float ActiveShieldTimer = 0.0f;
	public bool IsShieldActive => ActiveShieldTimer > 0.0f;
	private double _shieldPulseTime = 0.0;

	private WaveManager _cachedWaveManager;
	private HUD _cachedHud;
	public CharacterAbilitySet CharacterAbilities { get; private set; }

	public void RegisterInteractable(IInteractable interactable)
	{
		if (!_interactablesInRange.Contains(interactable))
		{
			_interactablesInRange.Add(interactable);
		}
	}

	public void UnregisterInteractable(IInteractable interactable)
	{
		_interactablesInRange.Remove(interactable);
		interactable.ShowPrompt(false);
	}

	public override void _Ready()
	{
		Scale = new Vector2(3.0f, 3.0f);
		ZIndex = 2;
		_animatedSprite = GetNode<AnimatedSprite2D>("AnimatedSprite2D");
		_abilityManager = GetNode<Node2D>("AbilityManager");
		_camera = GetNodeOrNull<Camera2D>("Camera2D") ?? GetNodeOrNull<Camera2D>("../Camera2D");
		if (_camera != null)
		{
			_camera.Zoom = new Vector2(1.8f, 1.8f);
		}

		// Гарантируем, что бинды и аудио-шины инициализированы даже при запуске напрямую со сцены уровня
		SettingsManager.ApplySettings();

		// Подгружаем конфиг выбранного героя из сохранения (Knight, Magician, Rogue, Berserker)
		if (SaveManager.CurrentSave != null && !string.IsNullOrEmpty(SaveManager.CurrentSave.CharacterName))
		{
			string savedName = SaveManager.CurrentSave.CharacterName;
			string charPath = GameLogic.ResolveCharacterDataPath(savedName);

			var loadedConfig = GD.Load<PlayerData>(charPath);
			if (loadedConfig != null)
			{
				Config = loadedConfig;
			}
		}

		// Создаем индивидуальный набор способностей для текущего персонажа (KnightAbilitySet / MagicianAbilitySet)
		CharacterAbilities = CharacterAbilitySet.CreateForCharacter(this, Config?.CharacterName ?? "Knight");

		_animatedSprite.Visible = true;
		_hitFlashMat = DungeonVisuals.CreateHitFlashMaterial();
		_animatedSprite.Material = _hitFlashMat;
		_groundShadow = DungeonVisuals.AttachGroundShadow(this, new Vector2(0, -1), new Vector2(0.55f, 0.48f), 0.58f);

		if (Config != null && Config.CharacterAnimations != null)
		{
			_animatedSprite.SpriteFrames = Config.CharacterAnimations;

			if (_animatedSprite.SpriteFrames.HasAnimation("idle"))
			{
				_animatedSprite.Play("idle");
			}
			else
			{
				GD.PushError("Player error: Animation 'idle' not found in SpriteFrames!");
			}
		}
		else
		{
			GD.PushWarning("Player warning: No CharacterAnimations found in Config.");
		}

		_statusManager = new StatusEffectManager();
		AddChild(_statusManager);

		AddToGroup("Player");

		if (SaveManager.CurrentSave != null)
		{
			var save = SaveManager.CurrentSave;
			BonusMaxHealth = save.BonusMaxHealth;
			BonusMaxMana = save.BonusMaxMana;
			BonusHealthRegen = save.BonusHealthRegen;
			BonusManaRegen = save.BonusManaRegen;
			BonusMoveSpeed = save.BonusMoveSpeed;
			WeaponDamageMultiplier = save.WeaponDamageMultiplier > 0.01f ? save.WeaponDamageMultiplier : 1.0f;
			CurrentHealth = Mathf.Clamp(save.CurrentHealth, 1.0f, MaxHealthEffective);
			CurrentMana = Mathf.Clamp(save.CurrentMana, 0.0f, MaxManaEffective);
			CoinCount = save.Coins;
			SkillPoints = save.SkillPoints;
			Ability1Level = save.Ability1Level;
			Ability2Level = save.Ability2Level;
			ShopPurchaseCounts = save.ShopPurchaseCounts != null
				? new Dictionary<string, int>(save.ShopPurchaseCounts)
				: new Dictionary<string, int>();

			foreach (Node child in _abilityManager.GetChildren())
			{
				child.QueueFree();
			}
			var controllerPrefab = GD.Load<PackedScene>("res://scene/weapons/attack_controller.tscn");
			WeaponSlots.Clear();
			for (int i = 0; i < 3; i++)
			{
				var controllerInstance = controllerPrefab.Instantiate() as AttackController;
				_abilityManager.AddChild(controllerInstance);
				WeaponSlots.Add(controllerInstance);
				controllerInstance.Visible = false;
			}

			bool hasAnySavedWeapon = false;
			if (save.WeaponResourcePaths != null)
			{
				foreach (var p in save.WeaponResourcePaths)
				{
					if (!string.IsNullOrEmpty(p)) { hasAnySavedWeapon = true; break; }
				}
			}

			if (!hasAnySavedWeapon)
			{
				CurrentHealth = MaxHealthEffective;
				CurrentMana = MaxManaEffective;
				save.CurrentHealth = CurrentHealth;
				save.CurrentMana = CurrentMana;

				if (Config != null && Config.StartingWeapons != null)
				{
					for (int i = 0; i < Config.StartingWeapons.Count && i < 3; i++)
					{
						var weaponData = Config.StartingWeapons[i];
						if (weaponData != null)
						{
							WeaponSlots[i].CurrentWeapon = weaponData;
							WeaponSlots[i].ApplyWeaponSettings();
						}
					}
					SaveManager.CapturePlayerInventory(this, save);
					SaveManager.SaveGame(save);
				}
			}
			else
			{
				for (int i = 0; i < save.WeaponResourcePaths.Count && i < 3; i++)
				{
					string resPath = save.WeaponResourcePaths[i];
					if (!string.IsNullOrEmpty(resPath))
					{
						var customStats = (save.WeaponSlotCustomStats != null && i < save.WeaponSlotCustomStats.Count)
							? save.WeaponSlotCustomStats[i]
							: null;
						var weaponData = SaveManager.RestoreWeaponWithStats(resPath, customStats);
						if (weaponData != null)
						{
							WeaponSlots[i].CurrentWeapon = weaponData;
							WeaponSlots[i].ApplyWeaponSettings();
						}
					}
				}
			}
			SelectSlot(0);
		}
		else
		{
			if (Config != null)
			{
				CurrentHealth = MaxHealthEffective;
				CurrentMana = MaxManaEffective;
			}
			SpawnStartingWeapons();
		}

		// Добавляем факел/свет вокруг игрока
		_torchLight = new PointLight2D();
		_torchLight.Name = "TorchLight";
		_torchLight.ShadowEnabled = true;
		_torchLight.ShadowFilter = Light2D.ShadowFilterEnum.Pcf5;
		_torchLight.Position = new Vector2(0, -9);

		_torchLight.Texture = GameResourceCache.GetRadialGlowTexture(
			new Color(1f, 0.92f, 0.75f, 1f),
			new Color(1f, 0.92f, 0.75f, 0.8f),
			TorchGradientMiddleOffset,
			TorchRadius,
			new Vector2(TorchFillToX, TorchFillToY));
		AddChild(_torchLight);

		_damageFlashLayer = new CanvasLayer();
		_damageFlashLayer.Layer = 50;
		_damageFlashRect = new ColorRect();
		_damageFlashRect.Color = Colors.White;
		_damageFlashRect.Material = DungeonVisuals.GetVignetteMaterial(0.88f, 0.62f, new Color(0.82f, 0.04f, 0.06f, 1.0f));
		_damageFlashRect.MouseFilter = Control.MouseFilterEnum.Ignore;
		_damageFlashRect.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		_damageFlashRect.Modulate = new Color(1, 1, 1, 0);
		_damageFlashLayer.AddChild(_damageFlashRect);
		AddChild(_damageFlashLayer);
	}

	public override void _Draw()
	{
		CharacterAbilities?.DrawSpecialAttackIndicator(this);

		if (!IsShieldActive) return;

		float t = (float)_shieldPulseTime;
		float pulse = 1.0f + Mathf.Sin(t * 7.5f) * 0.07f;
		Vector2 center = new Vector2(0, -9);
		float r = 14.0f * pulse;

		DrawCircle(center, r, new Color(0.10f, 0.62f, 0.95f, 0.18f));
		DrawArc(center, r, 0f, Mathf.Tau, 32, new Color(0.45f, 0.92f, 1.0f, 0.90f), 1.35f, true);
		DrawArc(center, r * 0.78f, t * 2.2f, t * 2.2f + Mathf.Pi * 1.45f, 20, new Color(0.80f, 0.98f, 1.0f, 0.62f), 0.85f, true);
		for (int i = 0; i < 6; i++)
		{
			float ang = t * 1.8f + i * (Mathf.Tau / 6.0f);
			Vector2 pt = center + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * r;
			DrawCircle(pt, 1.15f, new Color(0.88f, 0.98f, 1.0f, 0.95f));
		}
	}

	public override void _PhysicsProcess(double delta)
	{
		if (Config == null) return;

		if (FireballCooldownTimer > 0.0f)
		{
			FireballCooldownTimer = Mathf.Max(0.0f, FireballCooldownTimer - (float)delta);
		}
		if (ShieldCooldownTimer > 0.0f)
		{
			ShieldCooldownTimer = Mathf.Max(0.0f, ShieldCooldownTimer - (float)delta);
		}
		if (ActiveShieldTimer > 0.0f)
		{
			_shieldPulseTime += delta;
			ActiveShieldTimer = Mathf.Max(0.0f, ActiveShieldTimer - (float)delta);
			QueueRedraw();
		}

		if (Input.IsActionJustPressed("ability_1"))
		{
			UseFireball();
		}
		if (Input.IsActionJustPressed("ability_2"))
		{
			UseArcaneShieldDash();
		}

		if (_noManaWarningCooldown > 0.0f)
		{
			_noManaWarningCooldown -= (float)delta;
		}

		if (_statusManager.HasEffect(StatusEffectType.Stun) || _statusManager.HasEffect(StatusEffectType.Root))
		{
			CharacterAbilities?.CancelSpecialAttack();
			Velocity = Vector2.Zero;
			MoveAndSlide();
			UpdateAnimation(Vector2.Zero);
			return;
		}

		Vector2 direction = Input.GetVector("move_left", "move_right", "move_up", "move_down");

		float slowStrength = _statusManager.GetEffectStrength(StatusEffectType.Slow);
		float speedMod = 1.0f - Mathf.Clamp(slowStrength, 0.0f, 0.95f);
		float currentSpeed = WalkSpeedEffective * speedMod * World.Scale;

		bool isRunningPressed = direction != Vector2.Zero && Input.IsActionPressed("shift");
		bool hasManaToRun = CurrentMana >= Config.ManaCostPerSecondRunning * (float)delta;
		bool isRunning = isRunningPressed && hasManaToRun;
		if (isRunning)
		{
			currentSpeed *= Config.RunMultiplier;
			CurrentMana = Mathf.Max(0.0f, CurrentMana - Config.ManaCostPerSecondRunning * (float)delta);
		}
		else if (isRunningPressed && !hasManaToRun && Config.ManaCostPerSecondRunning > 0)
		{
			ShowNoManaWarning();
		}

		Velocity = direction * currentSpeed + _externalVelocity;
		MoveAndSlide();

		if (direction != Vector2.Zero && GetParent() != null)
		{
			_footstepTimer -= (float)delta;
			if (_footstepTimer <= 0.0f)
			{
				_footstepTimer = isRunning ? 0.14f : 0.30f;
				Color dustColor = isRunning
					? new Color(0.68f, 0.62f, 0.54f, 0.68f)
					: new Color(0.50f, 0.45f, 0.40f, 0.36f);
				DungeonVisuals.SpawnFootstepDust(GetParent(), GlobalPosition + new Vector2(0, 2), dustColor);
			}
		}
		else
		{
			_footstepTimer = 0.05f;
		}

		if (_externalVelocity.Length() > 0)
		{
			_externalVelocity = _externalVelocity.Lerp(Vector2.Zero, 0.15f);
			if (_externalVelocity.Length() < 1.0f)
			{
				_externalVelocity = Vector2.Zero;
			}
		}

		UpdateAnimation(direction);

		HandleWeaponSwitching();

		// Специальная атака на Правую кнопку мыши (ПКМ) — у каждого персонажа индивидуальная
		bool isRmbPressed = Input.IsMouseButtonPressed(MouseButton.Right)
			&& !_statusManager.HasEffect(StatusEffectType.Disarm)
			&& !_statusManager.HasEffect(StatusEffectType.Stun);
		CharacterAbilities?.UpdateSpecialAttack(delta, isRmbPressed);

		if (Input.IsActionJustPressed("reload"))
		{
			if (WeaponSlots.Count > 0 && _currentSlotIndex < WeaponSlots.Count)
			{
				WeaponSlots[_currentSlotIndex].StartReload();
			}
		}

		if (Input.IsActionPressed("attack")
			&& !_statusManager.HasEffect(StatusEffectType.Disarm)
			&& !_statusManager.HasEffect(StatusEffectType.Stun)
			&& (CharacterAbilities == null || !CharacterAbilities.IsNormalAttackBlocked))
		{
			ExecuteAttack();
		}

		ProcessRegen(delta);

		bool isInteractPressed = Input.IsActionPressed("interact");
		bool interactJustPressed = isInteractPressed && !_eJustPressed;
		_eJustPressed = isInteractPressed;

		_interactablesInRange.RemoveAll(x => !GodotObject.IsInstanceValid(x as GodotObject));

		IInteractable closest = null;
		float minDist = float.MaxValue;
		foreach (var interactable in _interactablesInRange)
		{
			float dist = GlobalPosition.DistanceTo(interactable.GetInteractionPosition());
			if (dist < minDist)
			{
				minDist = dist;
				closest = interactable;
			}
		}

		foreach (var interactable in _interactablesInRange)
		{
			interactable.ShowPrompt(interactable == closest);
		}

		if (interactJustPressed && closest != null)
		{
			closest.Interact(this);
		}
	}

	public void TakeDamage(float amount, Vector2 sourcePos = default, float knockForce = 0, bool isMelee = false)
	{
		if (CurrentHealth <= 0) return;

		if (Config != null)
		{
			float res = isMelee ? Config.MeleeResistance : Config.RangedResistance;
			amount *= (1.0f - Mathf.Clamp(res, 0.0f, 0.95f));
		}

		// Применяем пассивную и активную защиту способностей текущего персонажа
		if (CharacterAbilities != null)
		{
			amount = CharacterAbilities.ApplyPassiveAndActiveDefense(amount, ref knockForce);
		}

		CurrentHealth -= amount;

		ApplyCameraShake(DamageShakeIntensity * SettingsManager.ScreenShake);
		FlashRedScreen();
		TriggerSpriteHitFlash();

		if (sourcePos != default && GetParent() != null)
		{
			DungeonVisuals.SpawnImpactSparks(
				GetParent(),
				GlobalPosition + new Vector2(0, -18),
				GlobalPosition - sourcePos,
				new Color(1.0f, 0.28f, 0.22f),
				7,
				135.0f,
				75.0f,
				1.6f);
		}

		if (knockForce > 0 && sourcePos != default)
		{
			ApplyKnockback(sourcePos, knockForce);
		}

		if (CurrentHealth <= 0)
		{
			Die();
		}
	}

	private void FlashRedScreen()
	{
		if (_damageFlashRect == null) return;

		var tween = _damageFlashRect.CreateTween();
		tween.TweenProperty(_damageFlashRect, "modulate:a", 0.58f, 0.06);
		tween.TweenProperty(_damageFlashRect, "modulate:a", 0.0f, 0.58);
	}

	private void TriggerSpriteHitFlash()
	{
		if (_hitFlashMat == null) return;
		_hitFlashTween?.Kill();
		_hitFlashMat.SetShaderParameter("flash_color", new Color(1.0f, 0.45f, 0.42f, 1.0f));
		_hitFlashMat.SetShaderParameter("flash_amount", 0.92f);
		_hitFlashTween = CreateTween();
		_hitFlashTween.TweenMethod(
			Callable.From<float>(v => { if (GodotObject.IsInstanceValid(_hitFlashMat)) _hitFlashMat.SetShaderParameter("flash_amount", v); }),
			0.92f,
			0.0f,
			0.14f);
	}

	public void PlayDashVisuals(Vector2 dashDir)
	{
		Node parent = GetParent();
		if (parent == null) return;

		DungeonVisuals.SpawnImpactSparks(
			parent,
			GlobalPosition + new Vector2(0, -10),
			-dashDir,
			new Color(0.45f, 0.92f, 1.0f),
			10,
			160.0f,
			65.0f,
			1.7f);

		float[] delays = { 0.01f, 0.055f, 0.105f, 0.155f };
		float[] alphas = { 0.55f, 0.44f, 0.34f, 0.24f };
		for (int i = 0; i < delays.Length; i++)
		{
			float alpha = alphas[i];
			var timer = GetTree().CreateTimer(delays[i], processAlways: false);
			timer.Timeout += () =>
			{
				if (GodotObject.IsInstanceValid(this) && GodotObject.IsInstanceValid(_animatedSprite) && GodotObject.IsInstanceValid(parent))
				{
					DungeonVisuals.SpawnDashAfterimage(parent, _animatedSprite, new Color(0.38f, 0.88f, 1.0f, alpha), Scale);
				}
			};
		}
	}

	private void ApplyCameraShake(float intensity)
	{
		if (_camera == null) return;

		var tween = CreateTween();
		for (int i = 0; i < 4; i++)
		{
			Vector2 offset = new Vector2(GD.Randf() * 2 - 1, GD.Randf() * 2 - 1) * intensity;
			tween.TweenProperty(_camera, "offset", offset, 0.05f);
		}
		tween.TweenProperty(_camera, "offset", Vector2.Zero, 0.05f);
	}

	public void ApplyRecoil(Vector2 attackDir, float force)
	{
		_externalVelocity -= attackDir.Normalized() * force * 45.0f;
	}

	private void ApplyKnockback(Vector2 source, float force)
	{
		Vector2 dir = (GlobalPosition - source).Normalized();
		_externalVelocity = dir * force * 300.0f;
	}

	private void Die()
	{
		try
		{
			var deadSound = GD.Load<AudioStream>("res://assets/sounds/dead.wav");
			if (deadSound != null)
			{
				GameResourceCache.PlayPooledSound(GetTree(), deadSound, "SFX");
			}
		}
		catch (Exception e)
		{
			GD.PrintErr($"Failed to play death sound: {e.Message}");
		}

		if (!string.IsNullOrEmpty(SaveManager.ActiveSlotId))
		{
			SaveManager.DeleteSave(SaveManager.ActiveSlotId);
		}

		GetTree().Paused = true;

		var deathScreen = new DeathScreen();

		var level = GetTree().GetFirstNodeInGroup("Level") as Level;
		float timePlayed = level != null ? level.ConsumePlayedTime() : 0.0f;
		int levelIndex = level != null ? level.CurrentLevelIndex : 1;

		if (SaveManager.CurrentSave != null)
		{
			timePlayed += SaveManager.CurrentSave.PlayedTimeSeconds;
		}

		var weaponsList = new List<string>();
		foreach (var slot in WeaponSlots)
		{
			if (slot.CurrentWeapon != null)
			{
				weaponsList.Add(slot.CurrentWeapon.WeaponName);
			}
		}

		deathScreen.SetupStats(
			Config != null ? Config.CharacterName : "Knight",
			_animatedSprite.SpriteFrames,
			levelIndex,
			timePlayed,
			CoinCount,
			weaponsList
		);

		GetTree().Root.AddChild(deathScreen);
	}

	private void _on_area_2d_area_entered(Area2D area)
	{
		if (area.IsInGroup("EnemyWeapon"))
		{
			var controller = GameResourceCache.FindAncestor<AttackController>(area);
			if (controller != null && controller.CurrentWeapon != null)
			{
				float damage = controller.CurrentWeapon.Damage;
				if (controller.OwnerNode is Enemy enemy)
				{
					damage *= enemy.DamageMultiplier;
				}
				TakeDamage(damage, controller.GlobalPosition, controller.CurrentWeapon.KnockbackForce, true);
			}
		}
	}

	private void ProcessRegen(double delta)
	{
		if (Config == null) return;

		// Регенерация ХП и маны работает во время обратного отсчета до начала волны и во время самой волны,
		// но отключается после зачистки этажа, чтобы игрок не стоял на пустом этаже, абузя восстановление
		if (!IsInstanceValid(_cachedWaveManager)) _cachedWaveManager = GetTree().GetFirstNodeInGroup("WaveManager") as WaveManager;
		if (!IsInstanceValid(_cachedHud)) _cachedHud = GetTree().GetFirstNodeInGroup("HUD") as HUD;
		bool canRegen = (_cachedWaveManager != null && _cachedWaveManager.WaveInProgress) || (_cachedHud != null && _cachedHud.IsCountingDown);
		if (!canRegen) return;

		float maxHp = MaxHealthEffective;
		if (CurrentHealth < maxHp)
		{
			CurrentHealth = Mathf.Min(maxHp, CurrentHealth + HealthRegenEffective * (float)delta);
		}

		float maxMp = MaxManaEffective;
		if (CurrentMana < maxMp)
		{
			CurrentMana = Mathf.Min(maxMp, CurrentMana + ManaRegenEffective * (float)delta);
		}
	}

	private void UpdateAnimation(Vector2 direction)
	{
		bool isAttacking = false;
		if (WeaponSlots.Count > 0 && _currentSlotIndex < WeaponSlots.Count)
		{
			var currentWeapon = WeaponSlots[_currentSlotIndex];
			if (currentWeapon != null && currentWeapon.IsAttacking)
			{
				isAttacking = true;
			}
		}

		if (direction != Vector2.Zero)
		{
			if (Input.IsActionPressed("shift"))
			{
				_animatedSprite.Play("run");
			}
			else
			{
				_animatedSprite.Play("walk");
			}

			if (direction.X != 0 && !isAttacking)
			{
				_animatedSprite.FlipH = direction.X < 0;
			}
		}
		else
		{
			_animatedSprite.Play("idle");
		}
	}

	private void SpawnStartingWeapons()
	{
		foreach (Node child in _abilityManager.GetChildren())
		{
			child.QueueFree();
		}
		var controllerPrefab = GD.Load<PackedScene>("res://scene/weapons/attack_controller.tscn");
		WeaponSlots.Clear();
		for (int i = 0; i < 3; i++)
		{
			var controllerInstance = controllerPrefab.Instantiate() as AttackController;
			_abilityManager.AddChild(controllerInstance);
			WeaponSlots.Add(controllerInstance);
			controllerInstance.Visible = false;
		}

		if (Config == null || Config.StartingWeapons == null) return;

		for (int i = 0; i < Config.StartingWeapons.Count && i < 3; i++)
		{
			var weaponData = Config.StartingWeapons[i];
			if (weaponData != null)
			{
				WeaponSlots[i].CurrentWeapon = weaponData;
				WeaponSlots[i].ApplyWeaponSettings();
			}
		}

		SelectSlot(0);
	}

	private void HandleWeaponSwitching()
	{
		if (CharacterAbilities != null && CharacterAbilities.IsNormalAttackBlocked) return;

		if (Input.IsActionJustPressed("weapon_1")) SelectSlot(0);
		else if (Input.IsActionJustPressed("weapon_2")) SelectSlot(1);
		else if (Input.IsActionJustPressed("weapon_3")) SelectSlot(2);
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (CharacterAbilities != null && CharacterAbilities.IsNormalAttackBlocked) return;

		if (@event is InputEventMouseButton mb && mb.Pressed && WeaponSlots.Count > 0)
		{
			if (mb.ButtonIndex == MouseButton.WheelUp || mb.ButtonIndex == MouseButton.WheelDown)
			{
				int step = mb.ButtonIndex == MouseButton.WheelDown ? 1 : -1;
				for (int attempt = 1; attempt <= WeaponSlots.Count; attempt++)
				{
					int candidate = (_currentSlotIndex + step * attempt + WeaponSlots.Count) % WeaponSlots.Count;
					if (WeaponSlots[candidate].CurrentWeapon != null)
					{
						SelectSlot(candidate);
						break;
					}
				}
			}
		}
	}

	public void SelectSlot(int slotIndex)
	{
		if (slotIndex < 0 || slotIndex >= WeaponSlots.Count) return;
		if (slotIndex == _currentSlotIndex && WeaponSlots[slotIndex].Visible) return;

		if (WeaponSlots[slotIndex].CurrentWeapon == null)
		{
			return;
		}

		if (WeaponSlots[_currentSlotIndex] != null)
		{
			WeaponSlots[_currentSlotIndex].Deactivate();
		}

		_currentSlotIndex = slotIndex;

		if (WeaponSlots[_currentSlotIndex] != null)
		{
			WeaponSlots[_currentSlotIndex].Visible = true;
			WeaponSlots[_currentSlotIndex].ApplyWeaponSettings();

			if (WeaponSlots[_currentSlotIndex].CurrentWeapon.EquipSound != null)
			{
				WeaponSlots[_currentSlotIndex].PlaySound(WeaponSlots[_currentSlotIndex].CurrentWeapon.EquipSound);
			}
		}
	}

	private void ExecuteAttack()
	{
		if (WeaponSlots.Count > 0 && _currentSlotIndex < WeaponSlots.Count)
		{
			WeaponSlots[_currentSlotIndex].HandleManualAttack();
		}
	}

	public void ApplyStatusEffect(StatusEffectType type, float duration, float strength = 1.0f)
	{
		float resist = Config?.EffectResistance ?? 0.0f;
		_statusManager.ApplyEffect(type, duration * (1.0f - Mathf.Clamp(resist, 0.0f, 0.95f)), strength);
	}

	public void UpdateTorchLight()
	{
		if (_torchLight == null) return;
		var gradTex = _torchLight.Texture as GradientTexture2D;
		if (gradTex != null)
		{
			gradTex.Width = TorchRadius;
			gradTex.Height = TorchRadius;
			gradTex.FillTo = new Vector2(TorchFillToX, TorchFillToY);
			if (gradTex.Gradient != null)
			{
				gradTex.Gradient.Offsets = new float[] { 0.0f, TorchGradientMiddleOffset, 1.0f };
			}
		}
	}

	public void ShowNoManaWarning()
	{
		if (_noManaWarningCooldown <= 0.0f)
		{
			SpawnFloatingMessage("Not enough mana!", new Color(0.2f, 0.6f, 1.0f));
			_noManaWarningCooldown = 1.5f;
		}
	}

	public void SpawnFloatingMessage(string message, Color color)
	{
		var ft = new FloatingText();
		GetParent().AddChild(ft);
		ft.GlobalPosition = GlobalPosition + new Vector2(0, -28);
		ft.Init(message, color);
	}

	public void ActivateShieldEffect(float duration)
	{
		ActiveShieldTimer = duration;
		_shieldPulseTime = 0.0;
	}

	public void SetDashVelocity(Vector2 velocity)
	{
		_externalVelocity = velocity;
	}

	private void UseFireball()
	{
		CharacterAbilities?.UseAbility1();
	}

	private void UseArcaneShieldDash()
	{
		CharacterAbilities?.UseAbility2();
	}

	public void OnWaveCleared()
	{
		SkillPoints++;
		var hud = GetTree().GetFirstNodeInGroup("HUD") as HUD;
		if (hud != null)
		{
			hud.UpdateAbilitiesUI();
		}
	}
}
