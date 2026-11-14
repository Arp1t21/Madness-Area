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
	private ShaderMaterial _damageFlashMat;
	private Tween _damageFlashTween;
	private float _damageFlashAmount = 0.0f;
	private float _damageLowHpSmoothed = 0.0f;
	private float _damagePulseTime = 0.0f;
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
	public WeaponData ActiveWeaponData => (_currentSlotIndex >= 0 && _currentSlotIndex < WeaponSlots.Count)
		? WeaponSlots[_currentSlotIndex]?.CurrentWeapon
		: null;

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
	private readonly HashSet<string> _relics = new HashSet<string>(StringComparer.Ordinal);
	public float Madness { get; private set; } = 0.0f;
	private float _timeSinceLastKill = 99.0f;
	private float _playerDotTimer = 0.0f;
	private int _vampiricKillCounter = 0;

	public int PeerId { get; set; } = 1;
	public string PlayerDisplayName { get; set; } = "Hero";
	public bool IsCoopSpawned { get; set; } = false;
	public bool IsDead { get; private set; } = false;
	public bool IsCoopSession => NetworkManager.Instance != null && NetworkManager.Instance.IsCoopSession;
	public bool IsLocalPlayer => !IsCoopSession || PeerId == NetworkManager.Instance.LocalPeerId;
	public Vector2 NetworkAimWorldPos { get; private set; } = Vector2.Right * 48.0f;

	private Vector2 _remoteTargetPos = Vector2.Zero;
	private string _remoteAnim = "idle";
	private bool _remoteFlipH = false;
	private float _netSyncTimer = 0.0f;
	private float _vitalsSyncTimer = 0.0f;
	private Label _nameTagLabel;
	private int _spectateIndex = 0;
	private bool _teamDefeatShown = false;

	public StatusEffectManager StatusManager => _statusManager;
	public bool HasRelic(string relicId) => !string.IsNullOrEmpty(relicId) && _relics.Contains(relicId);

	public List<string> GetRelicIdsList() => new List<string>(_relics);

	public void AddRelic(string relicId, bool announce = true)
	{
		if (string.IsNullOrEmpty(relicId)) return;
		if (!_relics.Add(relicId)) return;

		if (announce && IsLocalPlayer)
		{
			var def = GameLogic.FindRelic(relicId);
			string label = def != null ? $"RELIC ACQUIRED: {def.Name.ToUpper()}" : relicId;
			Color col = def != null ? Color.Color8((byte)((def.AccentHex >> 16) & 0xFF), (byte)((def.AccentHex >> 8) & 0xFF), (byte)(def.AccentHex & 0xFF)) : Colors.Gold;
			var hud = GetTree().GetFirstNodeInGroup("HUD") as HUD;
			if (hud != null)
			{
				hud.ShowToast(label, col);
				hud.UpdateRelicsUI();
			}
			else
			{
				SpawnFloatingMessage(label, col);
			}
		}
	}

	private int GetCurrentFloorIndex()
	{
		var level = GetTree().GetFirstNodeInGroup("Level") as Level;
		return level != null ? level.CurrentLevelIndex : 1;
	}

	public bool IsMadnessUnlocked() => GameLogic.IsMadnessUnlocked(GetCurrentFloorIndex());

	public void OnEnemyKilled(bool isBoss, bool isElite)
	{
		_timeSinceLastKill = 0.0f;
		int floorIdx = GetCurrentFloorIndex();
		if (GameLogic.IsMadnessUnlocked(floorIdx))
		{
			float prev = Madness;
			Madness = GameLogic.AddMadnessOnKill(floorIdx, Madness, isBoss, isElite);
			if (prev < 25.0f && Madness >= 25.0f && IsLocalPlayer)
			{
				var hud = GetTree().GetFirstNodeInGroup("HUD") as HUD;
				if (hud != null)
					hud.ShowToast("MADNESS AWAKENED!  (+ATK SPD & CRIT)", new Color(1.0f, 0.36f, 0.28f));
				else
					SpawnFloatingMessage("MADNESS AWAKENED!", new Color(1.0f, 0.36f, 0.28f));
			}
		}

		if (HasRelic(GameLogic.RelicVampiricFang))
		{
			int healAmt = 0;
			if (isBoss)
			{
				healAmt = 2;
			}
			else
			{
				_vampiricKillCounter++;
				if (_vampiricKillCounter >= 3)
				{
					_vampiricKillCounter = 0;
					healAmt = 1;
				}
			}
			if (healAmt > 0 && CurrentHealth > 0 && CurrentHealth < MaxHealthEffective)
			{
				CurrentHealth = Mathf.Min(MaxHealthEffective, CurrentHealth + healAmt);
				SpawnFloatingMessage($"+{healAmt} HP", new Color(0.95f, 0.30f, 0.52f));
			}
		}
	}

	public float GetMadnessAttackRateMultiplier()
	{
		return GameLogic.GetMadnessAttackRateMultiplier(GetCurrentFloorIndex(), Madness);
	}

	public float GetCritChance(bool isBladeWeapon)
	{
		int floorIdx = GetCurrentFloorIndex();
		float critChance = isBladeWeapon ? 0.06f : 0.02f;
		if (HasRelic(GameLogic.RelicCrimsonEdge)) critChance += 0.10f;
		critChance += GameLogic.GetMadnessCritChanceBonus(floorIdx, Madness);
		return Mathf.Clamp(critChance, 0.0f, 1.0f);
	}

	public (float damage, bool isCrit) RollDamageCrit(float baseDamage, bool isBladeWeapon)
	{
		int floorIdx = GetCurrentFloorIndex();
		float critChance = GetCritChance(isBladeWeapon);

		if (critChance > 0.0f && GD.Randf() < critChance)
		{
			float mult = GameLogic.GetMadnessCritMultiplier(floorIdx, Madness);
			return (baseDamage * mult, true);
		}
		return (baseDamage, false);
	}

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
		if (!IsLocalPlayer || IsDead) return;
		if (!_interactablesInRange.Contains(interactable))
		{
			_interactablesInRange.Add(interactable);
		}
	}

	public void UnregisterInteractable(IInteractable interactable)
	{
		_interactablesInRange.Remove(interactable);
		interactable?.ShowPrompt(false);
	}

	public override void _Ready()
	{
		if (IsCoopSession && !IsCoopSpawned)
		{
			return;
		}

		Scale = Vector2.One;
		ZIndex = 2;
		_animatedSprite = GetNode<AnimatedSprite2D>("AnimatedSprite2D");
		_abilityManager = GetNode<Node2D>("AbilityManager");
		_camera = GetNodeOrNull<Camera2D>("Camera2D") ?? GetNodeOrNull<Camera2D>("../Camera2D");
		if (_camera != null)
		{
			_camera.Zoom = new Vector2(2.7f, 2.7f);
			_camera.Enabled = IsLocalPlayer;
			if (IsLocalPlayer)
			{
				_camera.MakeCurrent();
			}
		}

		// Гарантируем, что бинды и аудио-шины инициализированы даже при запуске напрямую со сцены уровня
		SettingsManager.ApplySettings();

		// Подгружаем конфиг выбранного героя из сохранения (Knight, Magician, Rogue, Berserker)
		if (!IsCoopSession && SaveManager.CurrentSave != null && !string.IsNullOrEmpty(SaveManager.CurrentSave.CharacterName))
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

		if (!IsCoopSession && SaveManager.CurrentSave != null)
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
			_relics.Clear();
			if (save.UnlockedRelics != null)
			{
				foreach (string relId in save.UnlockedRelics)
				{
					if (!string.IsNullOrEmpty(relId)) _relics.Add(relId);
				}
			}

			foreach (Node child in _abilityManager.GetChildren())
			{
				_abilityManager.RemoveChild(child);
				child.QueueFree();
			}
			var controllerPrefab = GD.Load<PackedScene>("res://scene/weapons/attack_controller.tscn");
			WeaponSlots.Clear();
			for (int i = 0; i < 3; i++)
			{
				var controllerInstance = controllerPrefab.Instantiate() as AttackController;
				controllerInstance.Name = $"WeaponSlot_{i}";
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

		if (IsLocalPlayer)
		{
			_damageFlashLayer = new CanvasLayer();
			_damageFlashLayer.Layer = 50;
			_damageFlashRect = new ColorRect();
			_damageFlashRect.Color = Colors.White;
			_damageFlashMat = DungeonVisuals.GetDamageVignetteMaterial();
			_damageFlashRect.Material = _damageFlashMat;
			_damageFlashRect.MouseFilter = Control.MouseFilterEnum.Ignore;
			_damageFlashRect.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
			_damageFlashRect.Visible = false;
			_damageFlashLayer.AddChild(_damageFlashRect);
			AddChild(_damageFlashLayer);
		}
		else
		{
			CreateRemoteNameTag();
		}

		_remoteTargetPos = GlobalPosition;
		NetworkAimWorldPos = GlobalPosition + Vector2.Right * 32.0f;
	}

	private void CreateRemoteNameTag()
	{
		if (string.IsNullOrEmpty(PlayerDisplayName)) return;
		_nameTagLabel = new Label
		{
			Text = PlayerDisplayName,
			HorizontalAlignment = HorizontalAlignment.Center,
			Position = new Vector2(-48, -32),
			Size = new Vector2(96, 14),
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		var font = GameResourceCache.GetSilverFont();
		if (font != null) _nameTagLabel.AddThemeFontOverride("font", font);
		_nameTagLabel.AddThemeFontSizeOverride("font_size", 9);
		_nameTagLabel.AddThemeColorOverride("font_color", new Color(0.82f, 0.94f, 1.0f));
		DungeonVisuals.ApplyPixelOutline(_nameTagLabel, 2);
		AddChild(_nameTagLabel);
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
		if (IsCoopSession && !IsCoopSpawned) return;
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
		if (_noManaWarningCooldown > 0.0f)
		{
			_noManaWarningCooldown -= (float)delta;
		}
		UpdateDamageVignetteVisuals((float)delta);

		if (IsDead)
		{
			Velocity = Vector2.Zero;
			var deadSlot = (WeaponSlots.Count > 0 && _currentSlotIndex < WeaponSlots.Count) ? WeaponSlots[_currentSlotIndex] : null;
			deadSlot?.SetContinuousTriggerHeld(false);
			if (IsLocalPlayer && IsCoopSession)
			{
				UpdateSpectateCamera(delta);
			}
			return;
		}

		if (!IsLocalPlayer)
		{
			UpdateRemotePuppet(delta);
			return;
		}

		NetworkAimWorldPos = GetGlobalMousePosition();

		if (Input.IsActionJustPressed("ability_1"))
		{
			UseFireball();
		}
		if (Input.IsActionJustPressed("ability_2"))
		{
			UseArcaneShieldDash();
		}

		if (_statusManager.HasEffect(StatusEffectType.Stun) || _statusManager.HasEffect(StatusEffectType.Root))
		{
			CharacterAbilities?.CancelSpecialAttack();
			Velocity = Vector2.Zero;
			MoveAndSlide();
			UpdateAnimation(Vector2.Zero);
			SendLocalCoopStateTick((float)delta);
			TickAuthoritativeHostVitals((float)delta);
			return;
		}

		Vector2 direction = Input.GetVector("move_left", "move_right", "move_up", "move_down");

		float slowStrength = _statusManager.GetEffectStrength(StatusEffectType.Slow);
		float speedMod = 1.0f - Mathf.Clamp(slowStrength, 0.0f, 0.95f);
		float currentSpeed = WalkSpeedEffective * speedMod;

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
				DungeonVisuals.SpawnFootstepDust(GetParent(), GlobalPosition + new Vector2(0, 1), dustColor);
			}
		}
		else
		{
			_footstepTimer = 0.05f;
		}

		if (_externalVelocity.Length() > 0)
		{
			_externalVelocity = _externalVelocity.Lerp(Vector2.Zero, 0.15f);
			if (_externalVelocity.Length() < 0.35f)
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
				if (IsCoopSession && IsLocalPlayer)
				{
					Rpc(nameof(RpcStartReload), _currentSlotIndex);
				}
			}
		}

		bool triggerHeld = Input.IsActionPressed("attack")
			&& !_statusManager.HasEffect(StatusEffectType.Disarm)
			&& !_statusManager.HasEffect(StatusEffectType.Stun)
			&& (CharacterAbilities == null || !CharacterAbilities.IsNormalAttackBlocked);

		var activeWeaponSlot = (WeaponSlots.Count > 0 && _currentSlotIndex < WeaponSlots.Count) ? WeaponSlots[_currentSlotIndex] : null;
		activeWeaponSlot?.SetContinuousTriggerHeld(triggerHeld);

		if (triggerHeld && activeWeaponSlot?.CurrentWeapon != null && !activeWeaponSlot.CurrentWeapon.IsContinuousLaser)
		{
			ExecuteAttack();
		}

		ProcessRegen(delta);
		ProcessMadnessAndDots((float)delta);
		SendLocalCoopStateTick((float)delta);
		TickAuthoritativeHostVitals((float)delta);

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
		if (IsDead || CurrentHealth <= 0) return;
		if (IsCoopSession && (NetworkManager.Instance == null || !NetworkManager.Instance.IsServerAuthority)) return;

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

		if (IsCoopSession)
		{
			Rpc(nameof(RpcOnPlayerDamaged), Mathf.Max(0.0f, CurrentHealth), sourcePos, knockForce);
		}
		else
		{
			ApplyDamageFeedback(sourcePos, knockForce);
		}

		if (CurrentHealth <= 0)
		{
			Die();
		}
	}

	private void ApplyDamageFeedback(Vector2 sourcePos, float knockForce)
	{
		if (IsLocalPlayer)
		{
			ApplyCameraShake(DamageShakeIntensity * SettingsManager.ScreenShake);
			FlashRedScreen();
			if (knockForce > 0 && sourcePos != default)
			{
				ApplyKnockback(sourcePos, knockForce);
			}
		}

		TriggerSpriteHitFlash();

		if (sourcePos != default && GetParent() != null)
		{
			DungeonVisuals.SpawnImpactSparks(
				GetParent(),
				GlobalPosition + new Vector2(0, -6),
				GlobalPosition - sourcePos,
				new Color(1.0f, 0.28f, 0.22f),
				7,
				45.0f,
				75.0f,
				0.9f);
		}
	}

	private void FlashRedScreen()
	{
		if (_damageFlashRect == null || _damageFlashMat == null) return;

		float hpRatio = Mathf.Clamp(CurrentHealth / Mathf.Max(1.0f, MaxHealthEffective), 0.0f, 1.0f);
		float danger = GameLogic.CalculateLowHealthDangerFactor(hpRatio);
		float peakFlash = Mathf.Lerp(0.48f, 0.92f, danger);
		float startVal = Mathf.Max(_damageFlashAmount, peakFlash * 0.35f);

		_damageFlashTween?.Kill();
		_damageFlashTween = CreateTween();
		_damageFlashTween.TweenMethod(
			Callable.From<float>(v => { _damageFlashAmount = v; }),
			startVal,
			peakFlash,
			0.055f)
			.SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
		_damageFlashTween.TweenMethod(
			Callable.From<float>(v => { _damageFlashAmount = v; }),
			peakFlash,
			0.0f,
			0.55f)
			.SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
	}

	private void UpdateDamageVignetteVisuals(float dt)
	{
		if (!IsLocalPlayer || _damageFlashRect == null || _damageFlashMat == null) return;

		if (IsDead)
		{
			_damageFlashTween?.Kill();
			_damageFlashAmount = Mathf.MoveToward(_damageFlashAmount, 0.0f, dt * 4.0f);
			_damageLowHpSmoothed = Mathf.MoveToward(_damageLowHpSmoothed, 0.0f, dt * 4.0f);
		}
		else
		{
			float hpRatio = Mathf.Clamp(CurrentHealth / Mathf.Max(1.0f, MaxHealthEffective), 0.0f, 1.0f);
			float targetDanger = GameLogic.CalculateLowHealthDangerFactor(hpRatio);
			_damageLowHpSmoothed = Mathf.Lerp(_damageLowHpSmoothed, targetDanger, 1.0f - Mathf.Exp(-7.0f * dt));
		}

		_damagePulseTime += dt;
		bool visible = _damageFlashAmount > 0.002f || _damageLowHpSmoothed > 0.002f;
		_damageFlashRect.Visible = visible;
		if (visible)
		{
			_damageFlashMat.SetShaderParameter("flash_amount", _damageFlashAmount);
			_damageFlashMat.SetShaderParameter("low_hp_amount", _damageLowHpSmoothed);
			_damageFlashMat.SetShaderParameter("pulse_time", _damagePulseTime);
		}
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
			GlobalPosition + new Vector2(0, -3.5f),
			-dashDir,
			new Color(0.45f, 0.92f, 1.0f),
			10,
			55.0f,
			65.0f,
			1.0f);

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

		float zoomedIntensity = intensity * (1.8f / 2.7f);
		var tween = CreateTween();
		for (int i = 0; i < 4; i++)
		{
			Vector2 offset = new Vector2(GD.Randf() * 2 - 1, GD.Randf() * 2 - 1) * zoomedIntensity;
			tween.TweenProperty(_camera, "offset", offset, 0.05f);
		}
		tween.TweenProperty(_camera, "offset", Vector2.Zero, 0.05f);
	}

	public void ApplyRecoil(Vector2 attackDir, float force)
	{
		_externalVelocity -= attackDir.Normalized() * force * 15.0f;
	}

	private void ApplyKnockback(Vector2 source, float force)
	{
		Vector2 dir = (GlobalPosition - source).Normalized();
		_externalVelocity = dir * force * 100.0f;
	}

	private void Die()
	{
		if (IsDead) return;

		if (IsCoopSession)
		{
			if (NetworkManager.Instance == null || !NetworkManager.Instance.IsServerAuthority) return;
			Rpc(nameof(RpcSetDeadState), true);
			GetTree().GetFirstNodeInGroup("Level")?.GetNodeOrNull<ExitLadder>("ExitLadder")?.RefreshReadyCheckOnServer();
			if (NetworkManager.GetAlivePlayers(GetTree()).Count == 0)
			{
				Rpc(nameof(RpcTriggerTeamDefeat));
			}
			return;
		}

		IsDead = true;
		PlayDeathSoundSafe();

		if (!string.IsNullOrEmpty(SaveManager.ActiveSlotId))
		{
			SaveManager.DeleteSave(SaveManager.ActiveSlotId);
		}

		GetTree().Paused = true;
		OpenDeathScreen();
	}

	private void PlayDeathSoundSafe()
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
	}

	private void OpenDeathScreen()
	{
		var deathScreen = new DeathScreen();

		var level = GetTree().GetFirstNodeInGroup("Level") as Level;
		float timePlayed = level != null ? level.ConsumePlayedTime() : 0.0f;
		int levelIndex = level != null ? level.CurrentLevelIndex : 1;

		if (!IsCoopSession && SaveManager.CurrentSave != null)
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
			_animatedSprite != null ? _animatedSprite.SpriteFrames : null,
			levelIndex,
			timePlayed,
			CoinCount,
			weaponsList
		);

		GetTree().Root.AddChild(deathScreen);
	}

	private void _on_area_2d_area_entered(Area2D area)
	{
		if (IsDead) return;
		if (IsCoopSession && (NetworkManager.Instance == null || !NetworkManager.Instance.IsServerAuthority)) return;

		if (area.IsInGroup("EnemyWeapon"))
		{
			var controller = GameResourceCache.FindAncestor<AttackController>(area);
			if (controller != null && controller.CurrentWeapon != null)
			{
				float damage = controller.CurrentWeapon.Damage;
				if (controller.OwnerNode is Enemy enemy)
				{
					damage *= enemy.DamageMultiplier;
					enemy.ApplyEliteOnHitToPlayer(this);
				}
				TakeDamage(damage, controller.GlobalPosition, controller.CurrentWeapon.KnockbackForce, true);
			}
		}
	}

	private void ProcessMadnessAndDots(float dt)
	{
		if (IsDead) return;
		if (IsCoopSession && (NetworkManager.Instance == null || !NetworkManager.Instance.IsServerAuthority)) return;

		_timeSinceLastKill += dt;
		int floorIdx = GetCurrentFloorIndex();
		if (GameLogic.IsMadnessUnlocked(floorIdx))
		{
			Madness = GameLogic.DecayMadness(floorIdx, Madness, dt, _timeSinceLastKill);
		}
		else
		{
			Madness = 0.0f;
		}

		if (_statusManager == null || CurrentHealth <= 0) return;
		_playerDotTimer += dt;
		if (_playerDotTimer >= 0.50f)
		{
			_playerDotTimer = 0.0f;
			float totalDot = 0.0f;
			Color dotCol = Colors.OrangeRed;
			if (_statusManager.HasEffect(StatusEffectType.Burn))
			{
				totalDot += GameLogic.ComputeStatusDotTickDamage(_statusManager.GetEffectStrength(StatusEffectType.Burn), 0.50f);
				dotCol = new Color(1.0f, 0.45f, 0.14f);
			}
			if (_statusManager.HasEffect(StatusEffectType.Poison))
			{
				totalDot += GameLogic.ComputeStatusDotTickDamage(_statusManager.GetEffectStrength(StatusEffectType.Poison), 0.50f);
				dotCol = new Color(0.35f, 0.90f, 0.30f);
			}
			if (_statusManager.HasEffect(StatusEffectType.Bleed))
			{
				totalDot += GameLogic.ComputeStatusDotTickDamage(_statusManager.GetEffectStrength(StatusEffectType.Bleed), 0.50f);
				dotCol = new Color(0.95f, 0.18f, 0.24f);
			}
			if (totalDot > 0.0f)
			{
				CurrentHealth = Mathf.Max(0.0f, CurrentHealth - totalDot);
				int dotDisplay = Mathf.Max(1, Mathf.RoundToInt(totalDot));
				if (IsCoopSession)
				{
					Rpc(nameof(RpcNotifyDotDamage), CurrentHealth, dotDisplay, dotCol);
				}
				else
				{
					SpawnDotFloatingText(dotDisplay, dotCol);
				}
				if (CurrentHealth <= 0) Die();
			}
		}
	}

	private void SpawnDotFloatingText(int dmgAmount, Color dotCol)
	{
		var ft = new FloatingText();
		GetParent()?.AddChild(ft);
		ft.GlobalPosition = GlobalPosition + new Vector2(0, -14);
		ft.Init(dmgAmount.ToString(), dotCol);
	}

	private bool CanRegenNow()
	{
		if (!IsInstanceValid(_cachedWaveManager)) _cachedWaveManager = GetTree().GetFirstNodeInGroup("WaveManager") as WaveManager;
		if (!IsInstanceValid(_cachedHud)) _cachedHud = GetTree().GetFirstNodeInGroup("HUD") as HUD;
		return (_cachedWaveManager != null && _cachedWaveManager.WaveInProgress) || (_cachedHud != null && _cachedHud.IsCountingDown);
	}

	private void ProcessRegenHpOnly(double delta)
	{
		if (Config == null || IsDead || !CanRegenNow()) return;
		float maxHp = MaxHealthEffective;
		if (CurrentHealth < maxHp)
		{
			CurrentHealth = Mathf.Min(maxHp, CurrentHealth + HealthRegenEffective * (float)delta);
		}
	}

	private void ProcessRegenManaOnly(double delta)
	{
		if (Config == null || IsDead || !CanRegenNow()) return;
		float maxMp = MaxManaEffective;
		if (CurrentMana < maxMp)
		{
			CurrentMana = Mathf.Min(maxMp, CurrentMana + ManaRegenEffective * (float)delta);
		}
	}

	private void ProcessRegen(double delta)
	{
		if (!IsCoopSession || (NetworkManager.Instance != null && NetworkManager.Instance.IsServerAuthority))
		{
			ProcessRegenHpOnly(delta);
		}
		ProcessRegenManaOnly(delta);
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
			_abilityManager.RemoveChild(child);
			child.QueueFree();
		}
		var controllerPrefab = GD.Load<PackedScene>("res://scene/weapons/attack_controller.tscn");
		WeaponSlots.Clear();
		for (int i = 0; i < 3; i++)
		{
			var controllerInstance = controllerPrefab.Instantiate() as AttackController;
			controllerInstance.Name = $"WeaponSlot_{i}";
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
		if (!IsLocalPlayer || IsDead) return;
		if (CharacterAbilities != null && CharacterAbilities.IsNormalAttackBlocked) return;

		if (Input.IsActionJustPressed("weapon_1")) SelectSlot(0);
		else if (Input.IsActionJustPressed("weapon_2")) SelectSlot(1);
		else if (Input.IsActionJustPressed("weapon_3")) SelectSlot(2);
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (!IsLocalPlayer || IsDead) return;
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
		SelectSlotInternal(slotIndex, broadcastToNetwork: IsCoopSession && IsLocalPlayer);
	}

	private void SelectSlotInternal(int slotIndex, bool broadcastToNetwork)
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

			if (IsLocalPlayer && WeaponSlots[_currentSlotIndex].CurrentWeapon.EquipSound != null)
			{
				WeaponSlots[_currentSlotIndex].PlaySound(WeaponSlots[_currentSlotIndex].CurrentWeapon.EquipSound);
			}
		}

		if (broadcastToNetwork)
		{
			Rpc(nameof(RpcSelectSlot), _currentSlotIndex);
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
		if (IsCoopSession && (NetworkManager.Instance == null || !NetworkManager.Instance.IsServerAuthority)) return;

		float resist = Config?.EffectResistance ?? 0.0f;
		float adjusted = duration * (1.0f - Mathf.Clamp(resist, 0.0f, 0.95f));
		if (IsCoopSession)
		{
			Rpc(nameof(RpcApplyStatusEffect), (int)type, adjusted, strength);
		}
		else
		{
			_statusManager?.ApplyEffect(type, adjusted, strength);
		}
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
		ft.GlobalPosition = GlobalPosition + new Vector2(0, -10);
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
		if (IsLocalPlayer)
		{
			var hud = GetTree().GetFirstNodeInGroup("HUD") as HUD;
			if (hud != null)
			{
				hud.UpdateAbilitiesUI();
			}
		}
	}

	private void SendLocalCoopStateTick(float dt)
	{
		if (!IsCoopSession || !IsLocalPlayer) return;
		_netSyncTimer -= dt;
		if (_netSyncTimer > 0.0f) return;
		_netSyncTimer = 0.05f;

		string anim = _animatedSprite != null ? _animatedSprite.Animation.ToString() : "idle";
		bool flipH = _animatedSprite != null && _animatedSprite.FlipH;
		Rpc(nameof(RpcSyncRemoteState), GlobalPosition, NetworkAimWorldPos, anim, flipH, CurrentMana);
	}

	private void TickAuthoritativeHostVitals(float dt)
	{
		if (!IsCoopSession || NetworkManager.Instance == null || !NetworkManager.Instance.IsServerAuthority) return;
		_vitalsSyncTimer -= dt;
		if (_vitalsSyncTimer > 0.0f) return;
		_vitalsSyncTimer = 0.10f;

		Rpc(nameof(RpcSyncAuthoritativeVitals), CurrentHealth, BonusMaxHealth, BonusMaxMana, Madness);
	}

	private void UpdateRemotePuppet(double delta)
	{
		if (_remoteTargetPos.DistanceTo(GlobalPosition) > 180.0f)
		{
			GlobalPosition = _remoteTargetPos;
		}
		else
		{
			GlobalPosition = GlobalPosition.Lerp(_remoteTargetPos, 1.0f - Mathf.Exp(-18.0f * (float)delta));
		}

		if (_animatedSprite != null)
		{
			if (!string.IsNullOrEmpty(_remoteAnim) && _animatedSprite.Animation != _remoteAnim
				&& (_animatedSprite.SpriteFrames == null || _animatedSprite.SpriteFrames.HasAnimation(_remoteAnim)))
			{
				_animatedSprite.Play(_remoteAnim);
			}
			_animatedSprite.FlipH = _remoteFlipH;
		}

		if (NetworkManager.Instance != null && NetworkManager.Instance.IsServerAuthority)
		{
			ProcessRegenHpOnly(delta);
			ProcessMadnessAndDots((float)delta);
			TickAuthoritativeHostVitals((float)delta);
		}
	}

	private string _lastSpectatedName = "";

	private void UpdateSpectateCamera(double delta)
	{
		if (_camera == null) return;
		var alive = NetworkManager.GetAlivePlayers(GetTree());
		if (alive.Count == 0) return;

		_camera.Enabled = true;
		_camera.MakeCurrent();
		_camera.TopLevel = true;

		if (Input.IsActionJustPressed("move_left"))
		{
			_spectateIndex = (_spectateIndex - 1 + alive.Count) % alive.Count;
		}
		else if (Input.IsActionJustPressed("move_right"))
		{
			_spectateIndex = (_spectateIndex + 1) % alive.Count;
		}

		_spectateIndex = ((_spectateIndex % alive.Count) + alive.Count) % alive.Count;
		Player target = alive[_spectateIndex];
		_camera.GlobalPosition = _camera.GlobalPosition.Lerp(target.GlobalPosition, 1.0f - Mathf.Exp(-12.0f * (float)delta));

		if (_lastSpectatedName != target.PlayerDisplayName)
		{
			_lastSpectatedName = target.PlayerDisplayName;
			(GetTree().GetFirstNodeInGroup("HUD") as HUD)?.SetSpectateBanner(true, _lastSpectatedName);
		}
	}

	private void ApplyDeadState(bool dead)
	{
		IsDead = dead;
		if (dead)
		{
			CurrentHealth = 0.0f;
			_statusManager?.ClearEffects();
			CharacterAbilities?.CancelSpecialAttack();
			var slot = (WeaponSlots.Count > 0 && _currentSlotIndex < WeaponSlots.Count) ? WeaponSlots[_currentSlotIndex] : null;
			slot?.SetContinuousTriggerHeld(false);
			foreach (var inter in _interactablesInRange)
			{
				inter?.ShowPrompt(false);
			}
			_interactablesInRange.Clear();

			if (_animatedSprite != null) _animatedSprite.Modulate = new Color(0.55f, 0.55f, 0.65f, 0.35f);
			if (_abilityManager != null) _abilityManager.Visible = false;
			if (_torchLight != null) _torchLight.Enabled = false;
			if (_nameTagLabel != null) _nameTagLabel.Text = $"{PlayerDisplayName} [DEAD]";

			if (IsLocalPlayer)
			{
				PlayDeathSoundSafe();
				_lastSpectatedName = "";
				if (_camera != null)
				{
					_camera.TopLevel = true;
					_camera.GlobalPosition = GlobalPosition;
				}
				var alive = NetworkManager.GetAlivePlayers(GetTree());
				string targetName = alive.Count > 0 ? alive[0].PlayerDisplayName : "";
				_lastSpectatedName = targetName;
				(GetTree().GetFirstNodeInGroup("HUD") as HUD)?.SetSpectateBanner(true, targetName);
			}
		}
		else
		{
			if (_animatedSprite != null) _animatedSprite.Modulate = Colors.White;
			if (_abilityManager != null) _abilityManager.Visible = true;
			if (_torchLight != null) _torchLight.Enabled = true;
			if (_nameTagLabel != null) _nameTagLabel.Text = PlayerDisplayName;

			if (IsLocalPlayer)
			{
				_lastSpectatedName = "";
				if (_camera != null)
				{
					_camera.TopLevel = false;
					_camera.Position = Vector2.Zero;
					_camera.Enabled = true;
					_camera.MakeCurrent();
				}
				(GetTree().GetFirstNodeInGroup("HUD") as HUD)?.SetSpectateBanner(false, "");
			}
		}

		GetNodeOrNull<CollisionShape2D>("CollisionShape2D")?.SetDeferred("disabled", dead);
		foreach (string areaName in new[] { "Area2DHitBox", "Area2DPickCoin", "Area2DPickWeapon" })
		{
			var area = GetNodeOrNull<Area2D>(areaName);
			if (area != null)
			{
				area.SetDeferred("monitoring", !dead);
				area.SetDeferred("monitorable", !dead);
			}
		}

		(GetTree().GetFirstNodeInGroup("HUD") as HUD)?.UpdatePartyRosterUI();
	}

	public void ReviveForNextFloor(Vector2 spawnPos)
	{
		if (IsCoopSession)
		{
			if (NetworkManager.Instance == null || !NetworkManager.Instance.IsServerAuthority) return;
			float hp = GameLogic.CalculateCoopReviveHealth(MaxHealthEffective);
			float mp = MaxManaEffective;
			Rpc(nameof(RpcReviveAt), spawnPos, hp, mp);
		}
		else
		{
			GlobalPosition = spawnPos;
			_remoteTargetPos = spawnPos;
		}
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered)]
	private void RpcSyncRemoteState(Vector2 pos, Vector2 aimPos, string anim, bool flipH, float mana)
	{
		if (Multiplayer.GetRemoteSenderId() != PeerId) return;
		_remoteTargetPos = pos;
		NetworkAimWorldPos = aimPos;
		_remoteAnim = anim;
		_remoteFlipH = flipH;
		CurrentMana = mana;
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered)]
	private void RpcSyncAuthoritativeVitals(float hp, float bonusMaxHp, float bonusMaxMp, float madness)
	{
		if (NetworkManager.Instance != null && NetworkManager.Instance.IsServerAuthority) return;
		if (!IsDead) CurrentHealth = hp;
		BonusMaxHealth = bonusMaxHp;
		BonusMaxMana = bonusMaxMp;
		Madness = madness;
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RpcOnPlayerDamaged(float newHp, Vector2 sourcePos, float knockForce)
	{
		if (NetworkManager.Instance != null && !NetworkManager.Instance.IsServerAuthority)
		{
			CurrentHealth = newHp;
		}
		ApplyDamageFeedback(sourcePos, knockForce);
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RpcNotifyDotDamage(float newHp, int dmgAmount, Color dotCol)
	{
		if (NetworkManager.Instance != null && !NetworkManager.Instance.IsServerAuthority)
		{
			CurrentHealth = newHp;
		}
		SpawnDotFloatingText(dmgAmount, dotCol);
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RpcSetDeadState(bool dead)
	{
		ApplyDeadState(dead);
	}

	public void ApplyFloorRevive(Vector2 spawnPos, float revivedHp, float revivedMana)
	{
		GlobalPosition = spawnPos;
		_remoteTargetPos = spawnPos;
		_statusManager?.ClearEffects();
		ApplyDeadState(false);
		CurrentHealth = revivedHp;
		CurrentMana = revivedMana;
		SpawnFloatingMessage("REVIVED!", new Color(1.0f, 0.88f, 0.35f));
	}

	public void TeleportForNewFloor(Vector2 spawnPos, float currentHp, float currentMp)
	{
		GlobalPosition = spawnPos;
		_remoteTargetPos = spawnPos;
		CurrentHealth = currentHp;
		CurrentMana = currentMp;
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RpcReviveAt(Vector2 spawnPos, float revivedHp, float revivedMana)
	{
		ApplyFloorRevive(spawnPos, revivedHp, revivedMana);
	}

	public void CheckTeamWipeOnServer()
	{
		if (!IsCoopSession || NetworkManager.Instance == null || !NetworkManager.Instance.IsServerAuthority) return;
		if (NetworkManager.GetAlivePlayers(GetTree()).Count == 0)
		{
			Rpc(nameof(RpcTriggerTeamDefeat));
		}
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RpcTriggerTeamDefeat()
	{
		var localPlayer = NetworkManager.GetLocalPlayer(GetTree());
		Player target = localPlayer ?? this;
		if (target._teamDefeatShown) return;
		target._teamDefeatShown = true;
		target.PlayDeathSoundSafe();
		target.OpenDeathScreen();
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RpcSelectSlot(int slotIndex)
	{
		if (Multiplayer.GetRemoteSenderId() != PeerId) return;
		SelectSlotInternal(slotIndex, broadcastToNetwork: false);
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RpcStartReload(int slotIndex)
	{
		if (Multiplayer.GetRemoteSenderId() != PeerId) return;
		if (slotIndex >= 0 && slotIndex < WeaponSlots.Count)
		{
			WeaponSlots[slotIndex]?.StartReload();
		}
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RpcApplyStatusEffect(int typeInt, float duration, float strength)
	{
		_statusManager?.ApplyEffect((StatusEffectType)typeInt, duration, strength);
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void RpcGrantCoins(int amount)
	{
		CoinCount = Mathf.Max(0, CoinCount + amount);
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void RpcRestoreMana(float amount)
	{
		CurrentMana = Mathf.Min(MaxManaEffective, CurrentMana + amount);
	}

	public void BroadcastCoopBuildSync()
	{
		if (!IsCoopSession || !IsLocalPlayer) return;

		float[] scalarFloats = new float[]
		{
			BonusMaxHealth,
			BonusMaxMana,
			BonusHealthRegen,
			BonusManaRegen,
			BonusMoveSpeed,
			WeaponDamageMultiplier,
			CurrentHealth,
			CurrentMana
		};

		int[] scalarInts = new int[]
		{
			CoinCount,
			SkillPoints,
			Ability1Level,
			Ability2Level
		};

		string[] relicIds = GetRelicIdsList().ToArray();

		int slotCount = WeaponSlots.Count;
		string[] slotWeaponPaths = new string[slotCount];
		int[] slotInts = new int[slotCount * 4];
		float[] slotFloats = new float[slotCount * 6];

		for (int i = 0; i < slotCount; i++)
		{
			WeaponData w = WeaponSlots[i]?.CurrentWeapon;
			if (w == null)
			{
				slotWeaponPaths[i] = "";
				continue;
			}

			slotWeaponPaths[i] = w.GetEffectiveResourcePath();
			int iBase = i * 4;
			slotInts[iBase] = w.Damage;
			slotInts[iBase + 1] = w.BulletCount;
			slotInts[iBase + 2] = w.MaxAmmo;
			slotInts[iBase + 3] = (w.AllowMultiHit ? 1 : 0) | (w.IsPiercing ? 2 : 0);

			int fBase = i * 6;
			slotFloats[fBase] = w.AttackCooldown;
			slotFloats[fBase + 1] = w.KnockbackForce;
			slotFloats[fBase + 2] = w.ManaCost;
			slotFloats[fBase + 3] = w.BulletSpeed;
			slotFloats[fBase + 4] = w.Spread;
			slotFloats[fBase + 5] = w.ReloadTime;
		}

		Rpc(nameof(RpcSyncPlayerBuild), scalarFloats, scalarInts, relicIds, slotWeaponPaths, slotInts, slotFloats);
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RpcSyncPlayerBuild(
		float[] scalarFloats,
		int[] scalarInts,
		string[] relicIds,
		string[] slotWeaponPaths,
		int[] slotInts,
		float[] slotFloats)
	{
		int sender = Multiplayer.GetRemoteSenderId();
		if (sender != PeerId) return;

		if (scalarFloats != null && scalarFloats.Length >= 8)
		{
			BonusMaxHealth = scalarFloats[0];
			BonusMaxMana = scalarFloats[1];
			BonusHealthRegen = scalarFloats[2];
			BonusManaRegen = scalarFloats[3];
			BonusMoveSpeed = scalarFloats[4];
			WeaponDamageMultiplier = scalarFloats[5];
			if (!IsDead)
			{
				CurrentHealth = Mathf.Clamp(scalarFloats[6], 1.0f, MaxHealthEffective);
			}
			CurrentMana = Mathf.Clamp(scalarFloats[7], 0.0f, MaxManaEffective);
		}

		if (scalarInts != null && scalarInts.Length >= 4)
		{
			CoinCount = Mathf.Max(0, scalarInts[0]);
			SkillPoints = Mathf.Max(0, scalarInts[1]);
			Ability1Level = GameLogic.ClampAbilityLevel(scalarInts[2]);
			Ability2Level = GameLogic.ClampAbilityLevel(scalarInts[3]);
		}

		if (relicIds != null)
		{
			foreach (string r in relicIds)
			{
				if (!string.IsNullOrEmpty(r)) _relics.Add(r);
			}
		}

		if (slotWeaponPaths != null && slotInts != null && slotFloats != null)
		{
			int count = Mathf.Min(WeaponSlots.Count, slotWeaponPaths.Length);
			for (int i = 0; i < count; i++)
			{
				var ctrl = WeaponSlots[i];
				if (ctrl == null || ctrl.CurrentWeapon == null) continue;
				if (i * 4 + 3 >= slotInts.Length || i * 6 + 5 >= slotFloats.Length) continue;

				WeaponData w = EnsureUniqueWeaponInSlot(i);
				if (w == null) continue;

				int iBase = i * 4;
				int fBase = i * 6;
				w.Damage = slotInts[iBase];
				w.BulletCount = slotInts[iBase + 1];
				int prevMaxAmmo = w.MaxAmmo;
				w.MaxAmmo = slotInts[iBase + 2];
				int flags = slotInts[iBase + 3];
				w.AllowMultiHit = (flags & 1) != 0;
				w.IsPiercing = (flags & 2) != 0;

				w.AttackCooldown = slotFloats[fBase];
				w.KnockbackForce = slotFloats[fBase + 1];
				w.ManaCost = slotFloats[fBase + 2];
				w.BulletSpeed = slotFloats[fBase + 3];
				w.Spread = slotFloats[fBase + 4];
				w.ReloadTime = slotFloats[fBase + 5];

				ctrl.ApplyWeaponSettings();
				if (w.MaxAmmo > prevMaxAmmo)
				{
					ctrl.AddBonusAmmo(w.MaxAmmo - prevMaxAmmo);
				}
			}
		}
	}

	public void BroadcastSpawnFireball(Vector2 spawnPos, Vector2 dir, float damage)
	{
		Rpc(nameof(RpcSpawnFireball), spawnPos, dir, damage);
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RpcSpawnFireball(Vector2 spawnPos, Vector2 dir, float damage)
	{
		int sender = Multiplayer.GetRemoteSenderId();
		if (sender != 0 && sender != PeerId) return;
		CharacterAbilities?.ExecuteFireballVisualAndSpawn(spawnPos, dir, damage);
	}

	public void BroadcastActivateShieldPulse(Vector2 dashDir, float shieldDuration, float pulseDamage, bool hasFrostAegis)
	{
		Rpc(nameof(RpcActivateShieldPulse), dashDir, shieldDuration, pulseDamage, hasFrostAegis);
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RpcActivateShieldPulse(Vector2 dashDir, float shieldDuration, float pulseDamage, bool hasFrostAegis)
	{
		int sender = Multiplayer.GetRemoteSenderId();
		if (sender != 0 && sender != PeerId) return;
		CharacterAbilities?.ExecuteShieldPulseVisualAndHit(dashDir, shieldDuration, pulseDamage, hasFrostAegis);
	}

	public void BroadcastThrowBoomerang(int slotIdx, Vector2 dir, float throwDistance, float speed, float damage, float knockback)
	{
		Rpc(nameof(RpcThrowBoomerang), slotIdx, dir, throwDistance, speed, damage, knockback);
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RpcThrowBoomerang(int slotIdx, Vector2 dir, float throwDistance, float speed, float damage, float knockback)
	{
		int sender = Multiplayer.GetRemoteSenderId();
		if (sender != 0 && sender != PeerId) return;
		if (CharacterAbilities is BerserkerAbilitySet berserker)
		{
			berserker.SpawnBoomerangLocal(slotIdx, dir, throwDistance, speed, damage, knockback);
		}
	}
}
