using Godot;
using System;
using System.Linq;

public partial class AttackController : Node2D
{
	[Export] public WeaponData CurrentWeapon; 

	private Timer _timer;
	private bool _canAttack = true;
	private Player _player;
	
	private Sprite2D _weaponSpriteNode;
	private AnimatedSprite2D _animSpriteNode;
	private AnimationPlayer _animPlayer;
	private Node2D _visualContainer;
	private RangedWeaponVisual _rangedVisual;
	private MeleeWeaponVisual _meleeVisual;
	private System.Collections.Generic.HashSet<ulong> _hitEnemies = new System.Collections.Generic.HashSet<ulong>();

	private int _currentAmmo = 0;
	private bool _isReloading = false;
	private WeaponData _instantiatedWeapon;

	public bool IsAttacking {
		get {
			bool isPlayerPlaying = _animPlayer != null && _animPlayer.IsPlaying() && _animPlayer.CurrentAnimation != "RESET" && _animPlayer.CurrentAnimation != "reload";
			bool isSpritePlaying = _animSpriteNode != null && _animSpriteNode.IsPlaying() && _animSpriteNode.Animation != "idle" && _animSpriteNode.Animation != "reload";
			return isPlayerPlaying || isSpritePlaying;
		}
	}

	public bool CanAttack() {
		if (!_canAttack || IsAttacking || _isReloading) return false;
		if (_isPlayerOwned && _player != null && CurrentWeapon != null) {
			if (_player.CurrentMana < CurrentWeapon.ManaCost) return false;
		}
		return true;
	}

	public void Deactivate() {
		Visible = false;
		if (_animPlayer != null) _animPlayer.Stop();
		if (_animSpriteNode != null) _animSpriteNode.Stop();
		_isReloading = false;
		
		if (_visualContainer != null) {
			var area = _visualContainer.GetNodeOrNull<Area2D>("Area2D") ?? 
					   _visualContainer.GetChildren().Cast<Node>().OfType<Area2D>().FirstOrDefault();
			if (area != null) {
				var shape = area.GetNodeOrNull<CollisionShape2D>("CollisionShape2D") ??
							area.GetChildren().Cast<Node>().OfType<CollisionShape2D>().FirstOrDefault();
				if (shape != null) {
					shape.SetDeferred("disabled", true);
				}
			}
		}
		
		_hitEnemies.Clear();
	}

	private Node2D _ownerNode;
	public Node2D OwnerNode => _ownerNode;

	public override void _Ready() {
		_timer = GetNode<Timer>("Timer");
		_timer.Timeout += () => _canAttack = true;
		_player = GetTree().GetFirstNodeInGroup("Player") as Player;

		Node pNode = GetParent();
		while (pNode != null) {
			if (pNode is CharacterBody2D cb) {
				_ownerNode = cb;
				break;
			}
			pNode = pNode.GetParent();
		}

		ApplyWeaponSettings();
	}

	private bool _isPlayerOwned = false;

	public void ApplyWeaponSettings() {
		if (CurrentWeapon == null || _timer == null) return;
		
		_timer.WaitTime = Mathf.Max(0.01f, CurrentWeapon.AttackCooldown); 
		
		if (!_isReloading) _currentAmmo = CurrentWeapon.MaxAmmo;

		_isPlayerOwned = false;
		Node pNode = GetParent();
		while (pNode != null) {
			if (pNode is Player) {
				_isPlayerOwned = true;
				break;
			}
			pNode = pNode.GetParent();
		}

		if (_visualContainer != null && _instantiatedWeapon != CurrentWeapon) {
			_visualContainer.QueueFree();
			_visualContainer = null;
			_rangedVisual = null;
			_meleeVisual = null;
			_weaponSpriteNode = null;
			_animSpriteNode = null;
			_animPlayer = null;
		}

		if (_visualContainer == null && CurrentWeapon.WeaponVisualPrefab != null) {
			_visualContainer = CurrentWeapon.WeaponVisualPrefab.Instantiate() as Node2D;
			AddChild(_visualContainer); 
			_instantiatedWeapon = CurrentWeapon;

			// Пытаемся найти специализированный контроллер визуала
			_rangedVisual = _visualContainer as RangedWeaponVisual ?? _visualContainer.GetNodeOrNull<RangedWeaponVisual>(".");
			_meleeVisual = _visualContainer as MeleeWeaponVisual ?? _visualContainer.GetNodeOrNull<MeleeWeaponVisual>(".");
			
			_weaponSpriteNode = _visualContainer.GetNodeOrNull<Sprite2D>("WeaponSprite") ?? 
								_visualContainer.GetNodeOrNull<Sprite2D>("SpriteDagger") ?? 
								_visualContainer.GetNodeOrNull<Sprite2D>("SpriteAxe");

			_animSpriteNode = _visualContainer.GetNodeOrNull<AnimatedSprite2D>("WeaponSprite") ??
							  _visualContainer.GetNodeOrNull<AnimatedSprite2D>("AnimatedSprite2D") ??
							  _visualContainer.GetChildren().Cast<Node>().OfType<AnimatedSprite2D>().FirstOrDefault();
								
			if (_animSpriteNode != null) {
				_animSpriteNode.AnimationFinished += () => {
					if (_animSpriteNode.Animation != "idle" && _animSpriteNode.SpriteFrames.HasAnimation("idle")) {
						_animSpriteNode.Play("idle");
					}
				};
				_animSpriteNode.AnimationLooped += () => {
					if (_animSpriteNode.Animation != "idle" && _animSpriteNode.SpriteFrames.HasAnimation("idle")) {
						_animSpriteNode.Play("idle");
					}
				};
			}

			_animPlayer = _visualContainer.GetNodeOrNull<AnimationPlayer>("AnimationPlayer") ?? 
						  _visualContainer.GetChildren().Cast<Node>().OfType<AnimationPlayer>().FirstOrDefault();

			var area = _visualContainer.GetNodeOrNull<Area2D>("Area2D") ?? 
					   _visualContainer.GetChildren().Cast<Node>().OfType<Area2D>().FirstOrDefault();
			if (area != null) {
				area.AddToGroup(_isPlayerOwned ? "PlayerWeapon" : "EnemyWeapon");
			}
		}

		HideSlashes(_visualContainer);

		// Сбрасываем анимацию визуала в idle/RESET при каждом выборе/подборе оружия
		if (_animSpriteNode != null && _animSpriteNode.SpriteFrames.HasAnimation("idle")) {
			_animSpriteNode.Play("idle");
		}
		if (_animPlayer != null && _animPlayer.HasAnimation("RESET")) {
			_animPlayer.Play("RESET");
		}
	}

	public override void _Process(double delta) {
		if (!IsAttacking) {
			HideSlashes(_visualContainer);
		}

		// Если это оружие ближнего боя и оно атакует, не обновляем его ориентацию, 
		// чтобы анимация удара проигрывалась в фиксированном направлении.
		if (!(CurrentWeapon != null && !CurrentWeapon.IsRanged && IsAttacking)) {
			UpdateWeaponOrientation(GetTargetPosition());
		}

		var ownerSprite = GetParent()?.GetParent()?.GetNodeOrNull<AnimatedSprite2D>("AnimatedSprite2D");
		if (ownerSprite != null) {
			Vector2 pos = Position;
			pos.X = ownerSprite.FlipH ? -2.0f : 2.0f; 
			pos.Y = -4.0f; 
			Position = pos;
		}
	}

	private void UpdateWeaponOrientation(Vector2 targetPos) {
		Vector2 ownerPos = _ownerNode != null ? _ownerNode.GlobalPosition : GlobalPosition;
		float angleToOwner = (targetPos - ownerPos).Angle();
		float angle = (targetPos - GlobalPosition).Angle();
		
		// 1. БАЗОВОЕ ПРИЦЕЛИВАНИЕ (родитель всегда смотрит на мышь)
		Rotation = angle;

		if (_visualContainer != null) {
			// 2. ДЕЛЕГИРОВАНИЕ СПЕЦИАЛЬНЫМ СКРИПТАМ
			if (_rangedVisual != null) {
				_rangedVisual.UpdateOrientation(targetPos, IsAttacking);
				return;
			}
			if (_meleeVisual != null) {
				_meleeVisual.UpdateOrientation(targetPos, IsAttacking);
				return;
			}

			// 3. ФОЛБЭК (Дефолтная логика, если скриптов нет)
			var ownerSprite = _ownerNode?.GetNodeOrNull<AnimatedSprite2D>("AnimatedSprite2D");
			bool currentFlip = ownerSprite?.FlipH ?? false;
			bool isLeft = currentFlip;

			// Гистерезис 10 градусов для предотвращения дребезга
			float angleDeg = Mathf.Abs(Mathf.RadToDeg(angleToOwner));
			if (angleDeg > 100) isLeft = true;
			else if (angleDeg < 80) isLeft = false;

			if (!IsAttacking) {
				_visualContainer.Rotation = Mathf.DegToRad(CurrentWeapon?.OrientationOffset ?? 0);
			}

			Vector2 scale = _visualContainer.Scale;
			scale.Y = isLeft ? -Mathf.Abs(scale.Y) : Mathf.Abs(scale.Y);
			_visualContainer.Scale = scale;

			if (ownerSprite != null) ownerSprite.FlipH = isLeft;
		}
	}

	private void HideSlashes(Node parent) {
		if (parent == null) return;
		foreach (Node child in parent.GetChildren()) {
			if (child is Sprite2D s && s.Name.ToString().Contains("Slash")) {
				s.Visible = false;
			}
			HideSlashes(child);
		}
	}

	public void HandleManualAttack() {
		if (!_canAttack || CurrentWeapon == null || _isReloading) return;

		// 1. Сначала проверяем патроны (только для игрока)
		if (_isPlayerOwned && CurrentWeapon.IsRanged && _currentAmmo <= 0) {
			StartReload();
			return;
		}

		// 2. Затем проверяем и тратим ману
		if (_isPlayerOwned && _player != null) {
			if (_player.CurrentMana < CurrentWeapon.ManaCost) {
				_player.ShowNoManaWarning();
				return;
			}
			_player.CurrentMana = Mathf.Max(0.0f, _player.CurrentMana - CurrentWeapon.ManaCost);
		}

		Vector2 targetPosition = GetTargetPosition();
		UpdateWeaponOrientation(targetPosition);

		_hitEnemies.Clear();

		if (CurrentWeapon.IsRanged) {
			Shoot(targetPosition);
			if (_isPlayerOwned) {
				_currentAmmo--;
			}
		}

		if (CurrentWeapon.Recoil > 0 && _isPlayerOwned && _player != null) {
			Vector2 dir = (targetPosition - GlobalPosition).Normalized();
			_player.ApplyRecoil(dir, CurrentWeapon.Recoil);
		}

		// Вызов CPUParticles2DSleeve, если они есть у оружия
		if (_visualContainer != null) {
			var sleeveParticles = _visualContainer.GetNodeOrNull<CpuParticles2D>("CPUParticles2DSleeve") ?? 
								  _visualContainer.GetNodeOrNull<CpuParticles2D>("CPUParticles2D");
			if (sleeveParticles != null) {
				sleeveParticles.Restart();
				sleeveParticles.Emitting = true;
			}
		}

		// Логика анимации (только если это не дальний бой с закончившейся обоймой)
		if (!(CurrentWeapon.IsRanged && _currentAmmo < 0)) {
			if (_animPlayer != null) {
				_animPlayer.Stop();
				string animToPlay = ""; 
				var animList = _animPlayer.GetAnimationList();
				foreach (var anim in animList) {
					if (anim != "RESET") {
						animToPlay = anim;
						break;
					}
				}
				if (!string.IsNullOrEmpty(animToPlay)) _animPlayer.Play(animToPlay); 
			}
			else if (_animSpriteNode != null) {
				_animSpriteNode.Stop();
				_animSpriteNode.Frame = 0;
				string animToPlay = CurrentWeapon.IsRanged ? "shoot" : "attack";
				if (!_animSpriteNode.SpriteFrames.HasAnimation(animToPlay)) {
					var names = _animSpriteNode.SpriteFrames.GetAnimationNames();
					if (names.Length > 0) animToPlay = names[0];
				}
				_animSpriteNode.Play(animToPlay);
			}
		}

		_canAttack = false;
		_timer.Start();
	}

	public void StartReload() {
		if (_isReloading || CurrentWeapon == null || !CurrentWeapon.IsRanged || _currentAmmo == CurrentWeapon.MaxAmmo) return;
		
		_isReloading = true;
		GD.Print($"{CurrentWeapon.WeaponName}: Перезарядка...");
		
		if (_animSpriteNode != null && _animSpriteNode.SpriteFrames.HasAnimation("reload")) {
			_animSpriteNode.Play("reload");
		} else if (_animPlayer != null && _animPlayer.HasAnimation("reload")) {
			_animPlayer.Play("reload");
		}
		
		var reloadTimer = GetTree().CreateTimer(CurrentWeapon.ReloadTime);
		reloadTimer.Timeout += () => {
			_currentAmmo = CurrentWeapon.MaxAmmo;
			_isReloading = false;
			GD.Print($"{CurrentWeapon.WeaponName}: Перезаряжено!");
			if (_animSpriteNode != null && _animSpriteNode.SpriteFrames.HasAnimation("idle")) {
				_animSpriteNode.Play("idle");
			}
		};
	}

	private void Shoot(Vector2 targetPos) {
		if (CurrentWeapon.BulletPrefab == null) return;

		Marker2D muzzle = null;
		if (_visualContainer != null) {
			muzzle = _visualContainer.GetNodeOrNull<Marker2D>("Muzzle") ?? 
					 _visualContainer.GetChildren().Cast<Node>().OfType<Marker2D>().FirstOrDefault();
		}
		
		Vector2 spawnPos = muzzle != null ? muzzle.GlobalPosition : GlobalPosition;

		for (int i = 0; i < CurrentWeapon.BulletCount; i++) {
			var bulletNode = CurrentWeapon.BulletPrefab.Instantiate();
			
			if (bulletNode is Projectile bullet) {
				bullet.Position = spawnPos;
				
				float bulletDamage = CurrentWeapon.Damage;
				if (_ownerNode is Enemy enemy) {
					bulletDamage *= enemy.DamageMultiplier;
				}
				bullet.Damage = bulletDamage;

				bullet.IsPlayerOwned = _isPlayerOwned;
				bullet.IsPiercing = CurrentWeapon.IsPiercing; // Передаем настройку пробития
				bullet.Speed = CurrentWeapon.BulletSpeed; // Устанавливаем скорость пули из настроек оружия
				bullet.KnockbackForce = CurrentWeapon.KnockbackForce;
				
				Vector2 dir = (targetPos - spawnPos).Normalized();
				if (CurrentWeapon.Spread > 0) {
					dir = dir.Rotated((float)GD.RandRange(-CurrentWeapon.Spread, CurrentWeapon.Spread));
				}
				bullet.Direction = dir;
				bullet.Rotation = dir.Angle();

				// ГАРАНТИРУЕМ ГРУППУ ДЛЯ РЕГИСТРАЦИИ УРОНА
				// Сначала ищем дочерний Area2D (как в glock_bullet.tscn)
				Area2D bulletArea = bullet.GetNodeOrNull<Area2D>("Area2D");
				
				// Если не нашли, проверяем саму пулю (на случай если корень - Area2D)
				// Но так как Projectile : Node2D, мы используем динамическую проверку
				if (bulletArea == null && bullet is Node bNode && bNode is Area2D rootArea) {
					bulletArea = rootArea;
				}
				
				if (bulletArea != null) {
					if (!bulletArea.IsInGroup("PlayerWeapon")) bulletArea.AddToGroup("PlayerWeapon");
				}
			}
			else if (bulletNode is Node2D node2D) {
				node2D.Position = spawnPos;
			}

			GetTree().Root.AddChild(bulletNode);
		}
	}

	public bool CanHitEnemy(ulong enemyId) {
		if (CurrentWeapon == null) return false;
		if (CurrentWeapon.AllowMultiHit) return true;
		if (_hitEnemies.Contains(enemyId)) return false;
		_hitEnemies.Add(enemyId);
		return true;
	}

	private Vector2 GetTargetPosition() {
		if (!_isPlayerOwned) {
			var p = GetTree().GetFirstNodeInGroup("Player") as Node2D;
			return p != null ? p.GlobalPosition : GlobalPosition;
		}

		if (CurrentWeapon != null && CurrentWeapon.IsAutoAim) {
			var enemies = GetTree().GetNodesInGroup("enemy");
			Node2D nearest = null;
			float minDist = float.MaxValue;
			foreach (Node node in enemies) {
				if (node is Node2D enemy) {
					if (enemy is OrcWarrior orc && orc.CurrentHealth <= 0) continue;
					float dist = GlobalPosition.DistanceTo(enemy.GlobalPosition);
					if (dist < minDist) {
						minDist = dist;
						nearest = enemy;
					}
				}
			}
			if (nearest != null) return nearest.GlobalPosition;
		}
		return GetGlobalMousePosition();
	}

	private void DealDamage() {
		if (CurrentWeapon == null) return;
		if (_isPlayerOwned) {
			var enemies = GetTree().GetNodesInGroup("enemy");
			foreach (Node node in enemies) {
				if (node is OrcWarrior orc) {
					float dist = GlobalPosition.DistanceTo(orc.GlobalPosition);
					if (dist <= CurrentWeapon.AttackRange) {
						Vector2 dirToEnemy = (orc.GlobalPosition - GlobalPosition).Normalized();
						Vector2 faceDir = Vector2.Up.Rotated(Rotation);
						if (faceDir.Dot(dirToEnemy) > 0.5f) {
							if (CanHitEnemy(orc.GetInstanceId())) {
								Vector2 source = _player != null ? _player.GlobalPosition : GlobalPosition;
								orc.TakeDamage(CurrentWeapon.Damage, source, CurrentWeapon.KnockbackForce, !CurrentWeapon.IsRanged);
							}
						}
					}
				}
			}
		} else {
			var playerNode = GetTree().GetFirstNodeInGroup("Player") as Player;
			if (playerNode != null) {
				float dist = GlobalPosition.DistanceTo(playerNode.GlobalPosition);
				if (dist <= CurrentWeapon.AttackRange) playerNode.TakeDamage(CurrentWeapon.Damage);
			}
		}
	}
}
