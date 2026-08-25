using Godot;
using System;
using System.Collections.Generic;

public partial class Archer : Enemy
{
	[Export] public WeaponData AttackConfig;
	private AttackController _attackController;
	
	private Node2D _player;
	private bool _isStopping = false; 
	private bool _isPreparingAttack = false;
	private float _preAttackTimer = 0.0f;
	private float _periodicCheckTimer = 0.0f;
	private float _cancelAttackCooldown = 0.0f;
	
	private List<Vector2> _navigationPath = new List<Vector2>();
	private float _pathUpdateTimer = 0.0f;
	
	private SceneTreeTimer _stopTimer;
	private Label _warningLabel;
	
	public override void _Ready()
	{
		base._Ready(); // Настройка здоровья, группы и т.д.
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
		_warningLabel.AddThemeColorOverride("font_color", new Color(1.0f, 0.5f, 0.1f)); // Оранжевый (лучник)
		
		var font = GD.Load<Font>("res://assets/fonts/fff-forward/FFFFORWA.TTF");
		_warningLabel.AddThemeFontOverride("font", font);
		_warningLabel.AddThemeFontSizeOverride("font_size", 13);
		
		_warningLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_warningLabel.VerticalAlignment = VerticalAlignment.Center;
		_warningLabel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterTop);
		_warningLabel.Size = new Vector2(40, 20);
		_warningLabel.Position = new Vector2(-20, -35); 
		_warningLabel.Visible = false;
		AddChild(_warningLabel);

		var hitBox = GetNodeOrNull<Area2D>("Area2DHitBox");
		if (hitBox != null) {
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
			Velocity = Velocity.Lerp(Vector2.Zero, 0.1f);
		}
		else 
		{
			float dist = GlobalPosition.DistanceTo(_player.GlobalPosition);
			Vector2 direction = Vector2.Zero;
			float currentSpeed = Config.Speed * World.Scale;

			if (dist > 120.0f * World.Scale)
			{
				_pathUpdateTimer -= (float)delta;
				if (_pathUpdateTimer <= 0)
				{
					_pathUpdateTimer = 0.25f + (float)GD.RandRange(-0.05f, 0.05f);
					var level = GetTree().GetFirstNodeInGroup("Level") as Level;
					if (level != null && _player != null)
					{
						_navigationPath = level.GetPathToPlayer(GlobalPosition, _player.GlobalPosition);
					}
				}

				if (_navigationPath != null && _navigationPath.Count > 0)
				{
					while (_navigationPath.Count > 1 && GlobalPosition.DistanceTo(_navigationPath[0]) < 12.0f * World.Scale)
					{
						_navigationPath.RemoveAt(0);
					}
					Vector2 nextPos = _navigationPath[0];
					direction = (nextPos - GlobalPosition).Normalized();
				}
				else
				{
					direction = (_player.GlobalPosition - GlobalPosition).Normalized();
				}
			}
			else if (dist < 85.0f * World.Scale)
			{
				// Retreat away from player
				direction = (GlobalPosition - _player.GlobalPosition).Normalized();
				currentSpeed = Config.Speed * 0.6f * World.Scale;
			}
			else
			{
				// Stand still (between 110 and 150)
				direction = Vector2.Zero;
			}

			// Вычисляем силу разделения (Separation), чтобы враги не ходили паровозиком и не накладывались друг на друга
			Vector2 separation = Vector2.Zero;
			var enemies = GetTree().GetNodesInGroup("enemy");
			float separationRadius = 18.0f; // Радиус расталкивания
			int neighborsCount = 0;

			foreach (Node node in enemies) {
				if (node is CharacterBody2D otherEnemy && otherEnemy != this && IsInstanceValid(otherEnemy)) {
					float d = GlobalPosition.DistanceTo(otherEnemy.GlobalPosition);
					if (d < separationRadius && d > 0.1f) {
						separation += (GlobalPosition - otherEnemy.GlobalPosition).Normalized() * (separationRadius - d);
						neighborsCount++;
					}
				}
			}

			if (neighborsCount > 0) {
				separation /= neighborsCount;
				direction = (direction + separation * 0.15f).Normalized();
			}
			if (direction != Vector2.Zero) {
				// Обход препятствий при застревании
				direction = ApplyAntiStuck(direction, delta, () => _navigationPath.Clear());
			}

			Vector2 targetVelocity = direction * currentSpeed;
			Velocity = Velocity.Lerp(targetVelocity, 0.1f);

			if (_sprite != null) {
				if (Velocity.Length() > 5.0f) {
					_sprite.Play("walk");
					_sprite.FlipH = _player.GlobalPosition.X < GlobalPosition.X;
				} else {
					_sprite.Play("idle");
					_sprite.FlipH = _player.GlobalPosition.X < GlobalPosition.X;
				}
			}

			// Проверка на атаку
			if (_cancelAttackCooldown <= 0 && _attackController != null && _attackController.CanAttack()) {
				if (dist <= 120.0f * World.Scale) {
					StartAttackPreparation();
				}
			}
		}
		
		MoveAndSlide();
	}

	private void StartAttackPreparation() {
		_isPreparingAttack = true;
		_preAttackTimer = 0.0f;
		_periodicCheckTimer = 0.5f;

		if (_warningLabel != null) _warningLabel.Visible = true;
		if (_sprite != null) {
			_sprite.FlipH = _player.GlobalPosition.X < GlobalPosition.X;
			_sprite.Play("idle");
		}
	}

	private void HandlePreparingState(double delta) {
		_preAttackTimer += (float)delta;
		_periodicCheckTimer -= (float)delta;

		if (_periodicCheckTimer <= 0) {
			_periodicCheckTimer = 0.5f;
			if (GlobalPosition.DistanceTo(_player.GlobalPosition) > (Config.AttackRange + 15.0f) * World.Scale) {
				CancelAttack();
				return;
			}
		}

		if (_preAttackTimer >= Config.PreAttackDelay) {
			FinishAttack();
		}
	}

	private void CancelAttack() {
		_isPreparingAttack = false;
		_cancelAttackCooldown = 0.6f;
		if (_warningLabel != null) _warningLabel.Visible = false;
		GD.Print($"{Config?.EnemyName} отменил атаку: игрок вышел из радиуса");
	}

	private void FinishAttack() {
		_isPreparingAttack = false;
		if (_warningLabel != null) _warningLabel.Visible = false;

		if (_attackController != null) {
			_attackController.HandleManualAttack();
		}
		
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

	private void _on_area_2d_area_entered(Area2D area) {
		// Поддержка взаимодействия с оружием игрока (для регистрации урона по призраку)
		if (area.IsInGroup("PlayerWeapon")) {
			var controller = area.GetParent()?.GetParent() as AttackController;
			if (controller != null && controller.CurrentWeapon != null) {
				var player = GetTree().GetFirstNodeInGroup("Player") as Node2D;
				Vector2 source = player != null ? player.GlobalPosition : GlobalPosition;
				
				float dist = GlobalPosition.DistanceTo(source);
				float weaponRange = controller.CurrentWeapon.AttackRange * World.Scale;

				if (dist <= weaponRange + 15.0f * World.Scale) {
					if (controller.CanHitEnemy(GetInstanceId())) {
						TakeDamage(controller.CurrentWeapon.Damage, source, controller.CurrentWeapon.KnockbackForce, true); 
					}
				}
			}
		}
	}
}
