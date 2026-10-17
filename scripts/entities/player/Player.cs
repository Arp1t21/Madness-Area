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

	private AnimatedSprite2D _animatedSprite;
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

	private PackedScene _fireballScene;
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
			string charPath = "res://data/characters/knight.tres";
			if (savedName.Equals("Magician", StringComparison.OrdinalIgnoreCase))
				charPath = "res://data/characters/magician.tres";
			else if (savedName.Equals("Rogue", StringComparison.OrdinalIgnoreCase))
				charPath = "res://data/characters/rogue.tres";
			else if (savedName.Equals("Berserker", StringComparison.OrdinalIgnoreCase))
				charPath = "res://data/characters/berserker.tres";

			var loadedConfig = GD.Load<PlayerData>(charPath);
			if (loadedConfig != null)
			{
				Config = loadedConfig;
			}
		}

		// Создаем индивидуальный набор способностей для текущего персонажа (KnightAbilitySet / MagicianAbilitySet)
		CharacterAbilities = CharacterAbilitySet.CreateForCharacter(this, Config?.CharacterName ?? "Knight");

		_animatedSprite.Visible = true;

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
					save.WeaponResourcePaths = new List<string>();
					for (int i = 0; i < Config.StartingWeapons.Count && i < 3; i++)
					{
						var weaponData = Config.StartingWeapons[i];
						if (weaponData != null)
						{
							WeaponSlots[i].CurrentWeapon = weaponData;
							WeaponSlots[i].ApplyWeaponSettings();
							save.WeaponResourcePaths.Add(weaponData.ResourcePath);
						}
					}
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
						var weaponData = GD.Load<WeaponData>(resPath);
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

		var gradient = new Gradient();
		gradient.Colors = new Color[]
		{
			new Color(1f, 0.92f, 0.75f, 1f),
			new Color(1f, 0.92f, 0.75f, 0.8f),
			new Color(0f, 0f, 0f, 0f)
		};
		gradient.Offsets = new float[] { 0.0f, TorchGradientMiddleOffset, 1.0f };

		var gradTex = new GradientTexture2D();
		gradTex.Gradient = gradient;
		gradTex.Fill = GradientTexture2D.FillEnum.Radial;
		gradTex.FillFrom = new Vector2(0.5f, 0.5f);
		gradTex.FillTo = new Vector2(TorchFillToX, TorchFillToY);
		gradTex.Width = TorchRadius;
		gradTex.Height = TorchRadius;

		_torchLight.Texture = gradTex;
		AddChild(_torchLight);

		_fireballScene = GD.Load<PackedScene>("res://scene/abilities/fireball.tscn");

		_damageFlashLayer = new CanvasLayer();
		_damageFlashLayer.Layer = 50;
		_damageFlashRect = new ColorRect();
		_damageFlashRect.Color = new Color(0.7f, 0.03f, 0.03f);
		_damageFlashRect.MouseFilter = Control.MouseFilterEnum.Ignore;
		_damageFlashRect.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
		_damageFlashRect.Size = GetViewport().GetVisibleRect().Size;
		_damageFlashRect.Modulate = new Color(1, 1, 1, 0);
		_damageFlashLayer.AddChild(_damageFlashRect);
		AddChild(_damageFlashLayer);
	}

	public override void _Draw()
	{
		CharacterAbilities?.DrawSpecialAttackIndicator(this);

		if (!IsShieldActive) return;

		float pulse = 1.0f + Mathf.Sin((float)_shieldPulseTime * 8.0f) * 0.08f;
		Vector2 center = new Vector2(0, -9);
		float r = 13.5f * pulse;

		DrawCircle(center, r, new Color(0.15f, 0.75f, 1.0f, 0.22f));
		DrawArc(center, r, 0f, Mathf.Tau, 32, new Color(0.45f, 0.92f, 1.0f, 0.88f), 1.4f, true);
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
			CharacterAbilities?.UpdateSpecialAttack(delta, false);
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
		tween.TweenProperty(_damageFlashRect, "modulate:a", 0.32f, 0.08);
		tween.TweenProperty(_damageFlashRect, "modulate:a", 0.0f, 0.92);
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
				MainMenu.PlayUISound(deadSound);
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
			var controller = area.GetParent()?.GetParent() as AttackController;
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
		var waveManager = GetTree().GetFirstNodeInGroup("WaveManager") as WaveManager;
		var hud = GetTree().GetFirstNodeInGroup("HUD") as HUD;
		bool canRegen = (waveManager != null && waveManager.WaveInProgress) || (hud != null && hud.IsCountingDown);
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
