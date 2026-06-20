using Godot;
using System;

public partial class OrcWarrior : Enemy
{
	[Export] public WeaponData AttackConfig;
	private AttackController _attackController;
	
	private Node2D _player;
	private bool _isStopping = false; 
	private bool _isPreparingAttack = false;
	private float _preAttackTimer = 0.0f;
	private float _periodicCheckTimer = 0.0f;
	private float _cancelAttackCooldown = 0.0f;
	
	private SceneTreeTimer _stopTimer;
	private Label _warningLabel;
	
	public override void _Ready()
	{
		base._Ready(); // Вызываем базу для настройки здоровья и группы
		_player = GetTree().GetFirstNodeInGroup("Player") as Node2D;

		// Создаем контроллер оружия для моба
		if (AttackConfig != null) {
			var controllerPrefab = GD.Load<PackedScene>("res://scene/weapons/attack_controller.tscn");
			_attackController = controllerPrefab.Instantiate() as AttackController;
			_attackController.CurrentWeapon = AttackConfig;
			AddChild(_attackController);
			_attackController.ApplyWeaponSettings();
		}

		// Подготавливаем восклицательный знак
		_warningLabel = new Label();
		_warningLabel.Text = "!";
		_warningLabel.AddThemeColorOverride("font_color", new Color(1, 0, 0)); // Красный
		
		var font = GD.Load<Font>("res://assets/fonts/fff-forward/FFFFORWA.TTF");
		_warningLabel.AddThemeFontOverride("font", font);
		_warningLabel.AddThemeFontSizeOverride("font_size", 13);
		
		_warningLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_warningLabel.VerticalAlignment = VerticalAlignment.Center;
		_warningLabel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterTop);
		
		// Центрирование: сдвигаем X на -20 (половина ширины), чтобы центр был в 0
		_warningLabel.Size = new Vector2(40, 20);
		_warningLabel.Position = new Vector2(-20, -35); 
		
		_warningLabel.Visible = false;
		AddChild(_warningLabel);

		// ГАРАНТИРУЕМ, ЧТО ВРАГ ВИДИТ ОРУЖИЕ
		var hitBox = GetNodeOrNull<Area2D>("Area2DHitBox");
		if (hitBox != null) {
			// Включаем маску для слоя 3 (где лежат топор и пули)
			hitBox.CollisionMask |= (1 << 2); // Бит 3 (значение 4)
		}
	}

	public override void _PhysicsProcess(double delta)
	{
		if (_player == null || Config == null || CurrentHealth <= 0) 
		{
			Velocity = Velocity.Lerp(Vector2.Zero, 0.1f);
			MoveAndSlide();
			return;
		}

		if (_cancelAttackCooldown > 0) {
			_cancelAttackCooldown -= (float)delta;
		}

		// Логика состояний
		if (_isPreparingAttack) 
		{
			HandlePreparingState(delta);
			Velocity = Velocity.Lerp(Vector2.Zero, 0.1f);
		}
		else if (_isStopping) 
		{
			// Фаза восстановления после удара
			Velocity = Velocity.Lerp(Vector2.Zero, 0.1f);
		}
		else 
		{
			// Движение к игроку с учетом распределения (разделения) от других врагов
			Vector2 direction = (_player.GlobalPosition - GlobalPosition).Normalized();
			
			// Вычисляем силу разделения (Separation), чтобы враги не ходили паровозиком
			Vector2 separation = Vector2.Zero;
			var enemies = GetTree().GetNodesInGroup("enemy");
			float separationRadius = 35.0f; // Радиус расталкивания
			int neighborsCount = 0;

			foreach (Node node in enemies) {
				if (node is CharacterBody2D otherEnemy && otherEnemy != this && IsInstanceValid(otherEnemy)) {
					float dist = GlobalPosition.DistanceTo(otherEnemy.GlobalPosition);
					if (dist < separationRadius && dist > 0.1f) {
						// Сила толчка обратно пропорциональна расстоянию
						separation += (GlobalPosition - otherEnemy.GlobalPosition).Normalized() * (separationRadius - dist);
						neighborsCount++;
					}
				}
			}

			if (neighborsCount > 0) {
				// Смешиваем направление на игрока и силу расталкивания
				direction = (direction + separation.Normalized() * 1.5f).Normalized();
			}

			Vector2 targetVelocity = direction * Config.Speed;
			Velocity = Velocity.Lerp(targetVelocity, 0.1f);

			if (_sprite != null) {
				if (Mathf.Abs(Velocity.X) > 5.0f) {
					_sprite.FlipH = Velocity.X < 0;
				}
				_sprite.Play("Walk");
			}

			// Проверка на начало атаки: должен быть рядом, оружие должно быть готово и кулдаун отмены прошел
			if (_cancelAttackCooldown <= 0 && _attackController != null && _attackController.CanAttack()) {
				float dist = GlobalPosition.DistanceTo(_player.GlobalPosition);
				if (dist < Config.AttackRange) {
					StartAttackPreparation();
				}
			}
		}
		
		MoveAndSlide();
	}

	private void StartAttackPreparation() {
		_isPreparingAttack = true;
		_preAttackTimer = 0.0f;
		_periodicCheckTimer = 0.5f; // Первая проверка через 0.5с

		if (_warningLabel != null) _warningLabel.Visible = true;
		if (_sprite != null) {
			_sprite.FlipH = _player.GlobalPosition.X < GlobalPosition.X;
			_sprite.Play("Idle");
		}
	}

	private void HandlePreparingState(double delta) {
		_preAttackTimer += (float)delta;
		_periodicCheckTimer -= (float)delta;

		// ТРЕБОВАНИЕ: Каждые 0.5 сек проверяем, не убежал ли игрок
		if (_periodicCheckTimer <= 0) {
			_periodicCheckTimer = 0.5f;
			if (GlobalPosition.DistanceTo(_player.GlobalPosition) > Config.AttackRange + 15.0f) {
				CancelAttack();
				return;
			}
		}

		// Если время подготовки вышло - наносим удар
		if (_preAttackTimer >= Config.PreAttackDelay) {
			FinishAttack();
		}
	}

	private void CancelAttack() {
		_isPreparingAttack = false;
		_cancelAttackCooldown = 0.6f; // Задержка перед следующей попыткой атаки (в секундах)
		if (_warningLabel != null) _warningLabel.Visible = false;
		GD.Print($"{Config?.EnemyName} отменил атаку: игрок вышел из радиуса");
	}

	private void FinishAttack() {
		_isPreparingAttack = false;
		if (_warningLabel != null) _warningLabel.Visible = false;

		if (_attackController != null) {
			_attackController.HandleManualAttack();
		}
		
		// Входим в фазу восстановления (замираем на StopDuration)
		TriggerStop(Config.StopDuration);
	}

	public void TriggerStop(float duration)
	{
		_isStopping = true;
		_stopTimer = GetTree().CreateTimer(duration);
		_stopTimer.Timeout += () => 
		{
			_isStopping = false; 
		};
	}

	public override void TakeDamage(float amount, Vector2 knockbackSource, float knockbackForce = 15.0f, bool isMelee = true) {
		base.TakeDamage(amount, knockbackSource, knockbackForce, isMelee);
		
		// Специфичная для орка логика: отмена атаки при получении урона
		if (CurrentHealth > 0) {
			CancelAttack();
			_isStopping = false;
		}
	}

	private void _on_area_2d_area_entered(Area2D area) {
		if (area.IsInGroup("PlayerWeapon")) {
			// Находим либо контроллер оружия (ближний бой), либо саму пулю
			var projectile = area.GetParent() as Projectile;
			var controller = area.GetParent()?.GetParent() as AttackController;

			if (projectile != null) {
				TakeDamage(projectile.Damage, projectile.GlobalPosition, 5.0f, false);
				// ФИКС ПРОБИТИЯ: Если пуля не пронзающая — удаляем её после попадания
				if (!projectile.IsPiercing) {
					projectile.QueueFree();
				}
			}
			else if (controller != null && controller.CurrentWeapon != null) {
				var player = GetTree().GetFirstNodeInGroup("Player") as Node2D;
				Vector2 source = player != null ? player.GlobalPosition : GlobalPosition;
				
				float dist = GlobalPosition.DistanceTo(source);
				float weaponRange = controller.CurrentWeapon.AttackRange;

				if (dist <= weaponRange + 15.0f) {
					if (controller.CanHitEnemy(GetInstanceId())) {
						TakeDamage(controller.CurrentWeapon.Damage, source, controller.CurrentWeapon.KnockbackForce, true); 
					}
				}
			}
		}
	}

	protected override void Die() {
		// Прячем знак перед удалением
		if (IsInstanceValid(_warningLabel)) _warningLabel.Visible = false;
		base.Die();
	}
}
