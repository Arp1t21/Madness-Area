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

	// Текущие показатели
	public float CurrentHealth;
	public float CurrentMana;
	public int CoinCount = 0;

	private StatusEffectManager _statusManager;
	private Vector2 _externalVelocity = Vector2.Zero;
	private float _noManaWarningCooldown = 0.0f;

	private readonly List<IInteractable> _interactablesInRange = new List<IInteractable>();
	private bool _eJustPressed = false;

	public int Ability1Level = 0; // Fireball
	public int Ability2Level = 0; // Passive Shield
	public int SkillPoints = 0;
	public float FireballCooldownTimer = 0.0f;
	public const float FireballCooldownMax = 10.0f;
	private bool _qWasPressed = false;
	private PackedScene _fireballScene;

	public void RegisterInteractable(IInteractable interactable)
	{
		if (!_interactablesInRange.Contains(interactable)) {
			_interactablesInRange.Add(interactable);
		}
	}

	public void UnregisterInteractable(IInteractable interactable)
	{
		_interactablesInRange.Remove(interactable);
		interactable.ShowPrompt(false);
	}

	public override void _Ready() {
		Scale = new Vector2(3.0f, 3.0f);
		_animatedSprite = GetNode<AnimatedSprite2D>("AnimatedSprite2D");
		_abilityManager = GetNode<Node2D>("AbilityManager");
		_camera = GetNodeOrNull<Camera2D>("Camera2D") ?? GetNodeOrNull<Camera2D>("../Camera2D");
		if (_camera != null) {
			// Приближаем камеру: со стандартным зумом вся арена выглядит крошечной
			_camera.Zoom = new Vector2(1.8f, 1.8f);
		}

		// Гарантируем видимость спрайта
		_animatedSprite.Visible = true;
		
		// Если в конфиге есть анимации, применяем их
		if (Config != null && Config.CharacterAnimations != null) {
			_animatedSprite.SpriteFrames = Config.CharacterAnimations;
			
			// Проверяем наличие анимации перед запуском
			if (_animatedSprite.SpriteFrames.HasAnimation("idle")) {
				_animatedSprite.Play("idle");
			} else {
				GD.PushError("Player error: Animation 'idle' not found in SpriteFrames!");
			}
		} else {
			GD.PushWarning("Player warning: No CharacterAnimations found in Config.");
		}

		_statusManager = new StatusEffectManager();
		AddChild(_statusManager);

		AddToGroup("Player");

		if (SaveManager.CurrentSave != null) {
			var save = SaveManager.CurrentSave;
			CurrentHealth = save.CurrentHealth;
			CurrentMana = save.CurrentMana;
			CoinCount = save.Coins;
			SkillPoints = save.SkillPoints;
			Ability1Level = save.Ability1Level;
			Ability2Level = save.Ability2Level;

			// Spawn weapon slots
			foreach (Node child in _abilityManager.GetChildren()) {
				child.QueueFree();
			}
			var controllerPrefab = GD.Load<PackedScene>("res://scene/weapons/attack_controller.tscn");
			WeaponSlots.Clear();
			for (int i = 0; i < 3; i++) {
				var controllerInstance = controllerPrefab.Instantiate() as AttackController;
				_abilityManager.AddChild(controllerInstance);
				WeaponSlots.Add(controllerInstance);
				controllerInstance.Visible = false;
			}

			// Load weapons from save
			if (save.WeaponResourcePaths == null || save.WeaponResourcePaths.Count == 0) {
				if (Config != null && Config.StartingWeapons != null) {
					for (int i = 0; i < Config.StartingWeapons.Count && i < 3; i++) {
						var weaponData = Config.StartingWeapons[i];
						if (weaponData != null) {
							WeaponSlots[i].CurrentWeapon = weaponData;
							WeaponSlots[i].ApplyWeaponSettings();
							save.WeaponResourcePaths.Add(weaponData.ResourcePath);
						}
					}
					SaveManager.SaveGame(save);
				}
			} else {
				for (int i = 0; i < save.WeaponResourcePaths.Count && i < 3; i++) {
					string resPath = save.WeaponResourcePaths[i];
					if (!string.IsNullOrEmpty(resPath)) {
						var weaponData = GD.Load<WeaponData>(resPath);
						if (weaponData != null) {
							WeaponSlots[i].CurrentWeapon = weaponData;
							WeaponSlots[i].ApplyWeaponSettings();
						}
					}
				}
			}
			SelectSlot(0);
		} else {
			if (Config != null) {
				CurrentHealth = Config.MaxHealth;
				CurrentMana = Config.MaxMana;
			}
			SpawnStartingWeapons();
		}

		// Добавляем факел/свет вокруг игрока
		_torchLight = new PointLight2D();
		_torchLight.Name = "TorchLight";
		_torchLight.ShadowEnabled = true; // Включаем тени от стен!
		_torchLight.ShadowFilter = Light2D.ShadowFilterEnum.Pcf5; // Мягкие тени
		_torchLight.Position = new Vector2(0, -9); // Выравниваем источник света по центру коллизии игрока
		
		var gradient = new Gradient();
		gradient.Colors = new Color[] { 
			new Color(1f, 0.92f, 0.75f, 1f), // Теплый свет факела в центре
			new Color(1f, 0.92f, 0.75f, 0.8f),
			new Color(0f, 0f, 0f, 0f)      // Плавное затухание в темноту
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

		// Слой с красной вспышкой на весь экран при получении урона
		_damageFlashLayer = new CanvasLayer();
		_damageFlashLayer.Layer = 50; // поверх HUD
		_damageFlashRect = new ColorRect();
		_damageFlashRect.Color = new Color(0.7f, 0.03f, 0.03f);
		_damageFlashRect.MouseFilter = Control.MouseFilterEnum.Ignore;
		_damageFlashRect.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
		_damageFlashRect.Size = GetViewport().GetVisibleRect().Size;
		_damageFlashRect.Modulate = new Color(1, 1, 1, 0);
		_damageFlashLayer.AddChild(_damageFlashRect);
		AddChild(_damageFlashLayer);
	}

	public override void _PhysicsProcess(double delta) {
		if (Config == null) return;

		if (FireballCooldownTimer > 0.0f) {
			FireballCooldownTimer = Mathf.Max(0.0f, FireballCooldownTimer - (float)delta);
		}

		bool qPressed = Input.IsKeyPressed(Key.Q);
		if (qPressed && !_qWasPressed) {
			UseFireball();
		}
		_qWasPressed = qPressed;

		if (_noManaWarningCooldown > 0.0f) {
			_noManaWarningCooldown -= (float)delta;
		}

		// Если в стане или оцепенении - не можем двигаться
		if (_statusManager.HasEffect(StatusEffectType.Stun) || _statusManager.HasEffect(StatusEffectType.Root)) {
			Velocity = Vector2.Zero;
			MoveAndSlide();
			UpdateAnimation(Vector2.Zero);
			return;
		}

		Vector2 direction = Input.GetVector("move_left", "move_right", "move_up", "move_down");

		// Сила Slow задается источником эффекта (например, аура Quagmire = 0.25)
		float slowStrength = _statusManager.GetEffectStrength(StatusEffectType.Slow);
		float speedMod = 1.0f - Mathf.Clamp(slowStrength, 0.0f, 0.95f);
			float currentSpeed = Config.WalkSpeed * speedMod * World.Scale;

		bool isRunningPressed = direction != Vector2.Zero && Input.IsActionPressed("shift");
		bool hasManaToRun = CurrentMana > 0;
		bool isRunning = isRunningPressed && hasManaToRun;
		if (isRunning) {
			currentSpeed *= Config.RunMultiplier;
			CurrentMana = Mathf.Max(0.0f, CurrentMana - Config.ManaCostPerSecondRunning * (float)delta);
		} else if (isRunningPressed && !hasManaToRun) {
			ShowNoManaWarning();
		}
		Velocity = direction * currentSpeed + _externalVelocity;
		MoveAndSlide();

		if (_externalVelocity.Length() > 0) {
			_externalVelocity = _externalVelocity.Lerp(Vector2.Zero, 0.15f);
			if (_externalVelocity.Length() < 1.0f) {
				_externalVelocity = Vector2.Zero;
			}
		}

		UpdateAnimation(direction);

		HandleWeaponSwitching();

		if (Input.IsActionJustPressed("reload")) {
			if (WeaponSlots.Count > 0 && _currentSlotIndex < WeaponSlots.Count) {
				WeaponSlots[_currentSlotIndex].StartReload();
			}
		}

		if (Input.IsActionPressed("attack") && !_statusManager.HasEffect(StatusEffectType.Disarm) && !_statusManager.HasEffect(StatusEffectType.Stun)) {
			ExecuteAttack();
		}

		ProcessRegen(delta);

		// Centralized interaction check
		bool isInteractPressed = Input.IsActionPressed("interact") || Input.IsPhysicalKeyPressed(Key.E);
		bool interactJustPressed = isInteractPressed && !_eJustPressed;
		_eJustPressed = isInteractPressed;

		_interactablesInRange.RemoveAll(x => !GodotObject.IsInstanceValid(x as GodotObject));

		IInteractable closest = null;
		float minDist = float.MaxValue;
		foreach (var interactable in _interactablesInRange) {
			float dist = GlobalPosition.DistanceTo(interactable.GetInteractionPosition());
			if (dist < minDist) {
				minDist = dist;
				closest = interactable;
			}
		}

		// Update prompt visibility so only the closest one is shown
		foreach (var interactable in _interactablesInRange) {
			interactable.ShowPrompt(interactable == closest);
		}

		if (interactJustPressed && closest != null) {
			closest.Interact(this);
		}
	}

	public void TakeDamage(float amount, Vector2 sourcePos = default, float knockForce = 0, bool isMelee = false) {
		if (CurrentHealth <= 0) return;

		// Сопротивления из конфига персонажа (к ближнему/дальнему бою)
		if (Config != null) {
			float res = isMelee ? Config.MeleeResistance : Config.RangedResistance;
			amount *= (1.0f - Mathf.Clamp(res, 0.0f, 0.95f));
		}

		// Пассивный щит (способность 2)
		if (Ability2Level > 0) {
			float shieldRes = 0.05f + (Ability2Level - 1) * 0.05f;
			amount *= (1.0f - shieldRes);
		}

		CurrentHealth -= amount;
		GD.Print($"Игрок получил урон: {amount}. ХП: {CurrentHealth}");
		
		// 1. Тряска камеры
		ApplyCameraShake(DamageShakeIntensity * SettingsManager.ScreenShake);

		// 2. Красный оттенок экрана примерно на секунду
		FlashRedScreen();

		// 2. Отброс
		if (knockForce > 0 && sourcePos != default) {
			ApplyKnockback(sourcePos, knockForce);
		}

		if (CurrentHealth <= 0) {
			Die();
		}
	}

	private void FlashRedScreen() {
		if (_damageFlashRect == null) return;

		// Быстрое проявление и плавный спад в течение ~1 секунды
		var tween = _damageFlashRect.CreateTween();
		tween.TweenProperty(_damageFlashRect, "modulate:a", 0.32f, 0.08);
		tween.TweenProperty(_damageFlashRect, "modulate:a", 0.0f, 0.92);
	}

	private void ApplyCameraShake(float intensity) {
		if (_camera == null) return;
		
		var tween = CreateTween();
		for (int i = 0; i < 4; i++) {
			Vector2 offset = new Vector2(GD.Randf() * 2 - 1, GD.Randf() * 2 - 1) * intensity;
			tween.TweenProperty(_camera, "offset", offset, 0.05f);
		}
		tween.TweenProperty(_camera, "offset", Vector2.Zero, 0.05f);
	}

	public void ApplyRecoil(Vector2 attackDir, float force) {
		_externalVelocity -= attackDir.Normalized() * force * 45.0f;
	}

	private void ApplyKnockback(Vector2 source, float force) {
		Vector2 dir = (GlobalPosition - source).Normalized();
		_externalVelocity = dir * force * 300.0f;
	}

	private void Die() {
		GD.Print("Player died!");

		// Play dead.wav
		try {
			var deadSound = GD.Load<AudioStream>("res://assets/sounds/dead.wav");
			if (deadSound != null) {
				MainMenu.PlayUISound(deadSound);
			}
		} catch (Exception e) {
			GD.PrintErr($"Failed to play death sound: {e.Message}");
		}

		// Delete save slot progress
		if (!string.IsNullOrEmpty(SaveManager.ActiveSlotId)) {
			SaveManager.DeleteSave(SaveManager.ActiveSlotId);
		}

		// Pause the game
		GetTree().Paused = true;

		// Instantiate DeathScreen and setup stats
		var deathScreen = new DeathScreen();
		
		var level = GetTree().GetFirstNodeInGroup("Level") as Level;
		float timePlayed = level != null ? level.ConsumePlayedTime() : 0.0f;
		int levelIndex = level != null ? level.CurrentLevelIndex : 1;

		if (SaveManager.CurrentSave != null) {
			timePlayed += SaveManager.CurrentSave.PlayedTimeSeconds;
		}

		var weaponsList = new List<string>();
		foreach (var slot in WeaponSlots) {
			if (slot.CurrentWeapon != null) {
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

	private void _on_area_2d_area_entered(Area2D area) {
		if (area.IsInGroup("EnemyWeapon")) {
			// Находим контроллер врага, чтобы взять урон
			var controller = area.GetParent()?.GetParent() as AttackController;
			if (controller != null && controller.CurrentWeapon != null) {
				float damage = controller.CurrentWeapon.Damage;
				if (controller.OwnerNode is Enemy enemy) {
					damage *= enemy.DamageMultiplier;
				}
				TakeDamage(damage, controller.GlobalPosition, controller.CurrentWeapon.KnockbackForce, true);
			}
		}
	}

	private void ProcessRegen(double delta) {
		if (Config == null) return;

		if (CurrentHealth < Config.MaxHealth) {
			CurrentHealth = Mathf.Min(Config.MaxHealth, CurrentHealth + Config.HealthRegen * (float)delta);
		}

		if (CurrentMana < Config.MaxMana) {
			CurrentMana = Mathf.Min(Config.MaxMana, CurrentMana + Config.ManaRegen * (float)delta);
		}
	}

	private void UpdateAnimation(Vector2 direction) {
		bool isAttacking = false;
		if (WeaponSlots.Count > 0 && _currentSlotIndex < WeaponSlots.Count) {
			var currentWeapon = WeaponSlots[_currentSlotIndex];
			if (currentWeapon != null && currentWeapon.IsAttacking) {
				isAttacking = true;
			}
		}

		if (direction != Vector2.Zero) {
			if (Input.IsActionPressed("shift")) {
				_animatedSprite.Play("run");
			} else {
				_animatedSprite.Play("walk");
			}

			// Разворачиваем персонажа по направлению движения, ТОЛЬКО если он сейчас не атакует
			if (direction.X != 0 && !isAttacking) {
				_animatedSprite.FlipH = direction.X < 0;
			}
		} else {
			_animatedSprite.Play("idle");
		}
	}

	private void SpawnStartingWeapons() {
		// Создаем 3 пустых контроллера (слота)
		foreach (Node child in _abilityManager.GetChildren()) {
			child.QueueFree();
		}
		var controllerPrefab = GD.Load<PackedScene>("res://scene/weapons/attack_controller.tscn");
		WeaponSlots.Clear();
		for (int i = 0; i < 3; i++) {
			var controllerInstance = controllerPrefab.Instantiate() as AttackController;
			_abilityManager.AddChild(controllerInstance);
			WeaponSlots.Add(controllerInstance);
			controllerInstance.Visible = false;
		}

		if (Config == null || Config.StartingWeapons == null) return;

		// Заполняем слоты стартовым оружием
		for (int i = 0; i < Config.StartingWeapons.Count && i < 3; i++) {
			var weaponData = Config.StartingWeapons[i];
			if (weaponData != null) {
				WeaponSlots[i].CurrentWeapon = weaponData;
				WeaponSlots[i].ApplyWeaponSettings();
			}
		}

		SelectSlot(0);
	}

	private void HandleWeaponSwitching() {
		if (Input.IsActionJustPressed("weapon_1")) SelectSlot(0);
		if (Input.IsActionJustPressed("weapon_2")) SelectSlot(1);
		if (Input.IsActionJustPressed("weapon_3")) SelectSlot(2);
	}

	public override void _Input(InputEvent @event)
	{
		if (@event is InputEventKey keyEvent && keyEvent.Pressed && !keyEvent.Echo)
		{
			var key = keyEvent.Keycode;
			var physKey = keyEvent.PhysicalKeycode;
			if (key == Key.Key1 || physKey == Key.Key1) SelectSlot(0);
			else if (key == Key.Key2 || physKey == Key.Key2) SelectSlot(1);
			else if (key == Key.Key3 || physKey == Key.Key3) SelectSlot(2);
		}
	}

	public void SelectSlot(int slotIndex) {
		if (slotIndex < 0 || slotIndex >= WeaponSlots.Count) return;
		if (slotIndex == _currentSlotIndex && WeaponSlots[slotIndex].Visible) return;

		{
			// Проверяем, есть ли оружие в этом слоте
			if (WeaponSlots[slotIndex].CurrentWeapon == null) {
				GD.Print($"Слот {slotIndex + 1} пуст.");
				return;
			}

			// Деактивируем старое оружие (гасим анимации и хитбоксы)
			if (WeaponSlots[_currentSlotIndex] != null) {
				WeaponSlots[_currentSlotIndex].Deactivate();
			}

			_currentSlotIndex = slotIndex;

			if (WeaponSlots[_currentSlotIndex] != null) {
				WeaponSlots[_currentSlotIndex].Visible = true;
				WeaponSlots[_currentSlotIndex].ApplyWeaponSettings();

				if (WeaponSlots[_currentSlotIndex].CurrentWeapon.EquipSound != null) {
					WeaponSlots[_currentSlotIndex].PlaySound(WeaponSlots[_currentSlotIndex].CurrentWeapon.EquipSound);
				}
			}
		}
	}

	private void ExecuteAttack() {
		if (WeaponSlots.Count > 0 && _currentSlotIndex < WeaponSlots.Count) {
			WeaponSlots[_currentSlotIndex].HandleManualAttack();
		}
	}

	// Публичный доступ для врагов (ауры и т.п.); EffectResistance сокращает длительность.
	// strength — сила эффекта (для Slow: доля замедления, 0.25 = -25% скорости)
	public void ApplyStatusEffect(StatusEffectType type, float duration, float strength = 1.0f) {
		float resist = Config?.EffectResistance ?? 0.0f;
		_statusManager.ApplyEffect(type, duration * (1.0f - Mathf.Clamp(resist, 0.0f, 0.95f)), strength);
	}

	public void UpdateTorchLight() {
		if (_torchLight == null) return;
		var gradTex = _torchLight.Texture as GradientTexture2D;
		if (gradTex != null) {
			gradTex.Width = TorchRadius;
			gradTex.Height = TorchRadius;
			gradTex.FillTo = new Vector2(TorchFillToX, TorchFillToY);
			if (gradTex.Gradient != null) {
				gradTex.Gradient.Offsets = new float[] { 0.0f, TorchGradientMiddleOffset, 1.0f };
			}
		}
	}

	public void ShowNoManaWarning() {
		if (_noManaWarningCooldown <= 0.0f) {
			var ft = new FloatingText();
			GetParent().AddChild(ft);
			ft.GlobalPosition = GlobalPosition + new Vector2(0, -25);
			ft.Init("You dont have enough mana", new Color(0.2f, 0.6f, 1.0f));
			_noManaWarningCooldown = 1.5f;
		}
	}

	private void UseFireball()
	{
		if (Ability1Level <= 0 || FireballCooldownTimer > 0.0f) return;

		if (CurrentMana < 20.0f)
		{
			ShowNoManaWarning();
			return;
		}

		CurrentMana -= 20.0f;
		FireballCooldownTimer = FireballCooldownMax;

		Vector2 mousePos = GetGlobalMousePosition();
		Vector2 dir = (mousePos - GlobalPosition).Normalized();

		Fireball fireball;
		if (_fireballScene != null)
		{
			fireball = _fireballScene.Instantiate<Fireball>();
		}
		else
		{
			fireball = new Fireball();
		}
		
		fireball.GlobalPosition = GlobalPosition;
		fireball.Direction = dir;
		fireball.Damage = 5.0f + (Ability1Level - 1) * 5.0f;
		GetParent().AddChild(fireball);
	}

	public void OnWaveCleared()
	{
		SkillPoints++;
		GD.Print($"Игрок получил очко способностей! Всего очков: {SkillPoints}");
		
		var hud = GetTree().GetFirstNodeInGroup("HUD") as HUD;
		if (hud != null)
		{
			hud.UpdateAbilitiesUI();
		}
	}
}
