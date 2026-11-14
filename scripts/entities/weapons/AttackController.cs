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
	private float _reloadElapsed = 0.0f;
	private bool _playedEmptySoundThisPress = false;
	private WeaponData _instantiatedWeapon;

	public int CurrentAmmo => _currentAmmo;
	public int MaxAmmo => CurrentWeapon?.MaxAmmo ?? 0;
	public bool IsReloading => _isReloading;
	public bool UsesAmmo => CurrentWeapon != null && CurrentWeapon.IsRanged && CurrentWeapon.MaxAmmo < 500;
	public float ReloadProgress => (_isReloading && CurrentWeapon != null && CurrentWeapon.ReloadTime > 0.01f)
		? Mathf.Clamp(_reloadElapsed / CurrentWeapon.ReloadTime, 0f, 1f)
		: 0f;

	public bool IsAttacking
	{
		get
		{
			bool isPlayerPlaying = _animPlayer != null && _animPlayer.IsPlaying() && _animPlayer.CurrentAnimation != "RESET" && _animPlayer.CurrentAnimation != "reload";
			bool isSpritePlaying = _animSpriteNode != null && _animSpriteNode.IsPlaying() && _animSpriteNode.Animation != "idle" && _animSpriteNode.Animation != "reload";
			return isPlayerPlaying || isSpritePlaying;
		}
	}

	public bool CanAttack()
	{
		if (!_canAttack || IsAttacking || _isReloading) return false;
		if (_isPlayerOwned && _player != null && CurrentWeapon != null)
		{
			if (_player.CurrentMana < CurrentWeapon.ManaCost) return false;
		}
		return true;
	}

	public void Deactivate()
	{
		Visible = false;
		if (_animPlayer != null) _animPlayer.Stop();
		if (_animSpriteNode != null) _animSpriteNode.Stop();
		_isReloading = false;
		_reloadElapsed = 0.0f;

		if (_visualContainer != null)
		{
			var area = _visualContainer.GetNodeOrNull<Area2D>("Area2D") ??
					   _visualContainer.GetChildren().Cast<Node>().OfType<Area2D>().FirstOrDefault();
			if (area != null)
			{
				var shape = area.GetNodeOrNull<CollisionShape2D>("CollisionShape2D") ??
							area.GetChildren().Cast<Node>().OfType<CollisionShape2D>().FirstOrDefault();
				if (shape != null)
				{
					shape.SetDeferred("disabled", true);
				}
			}
		}

		_hitEnemies.Clear();
		_playedEmptySoundThisPress = false;
	}

	private Node2D _ownerNode;
	private AnimatedSprite2D _ownerSprite;
	public Node2D OwnerNode => _ownerNode;

	public override void _Ready()
	{
		_timer = GetNode<Timer>("Timer");
		_timer.Timeout += () => _canAttack = true;
		_player = GetTree().GetFirstNodeInGroup("Player") as Player;

		Node pNode = GetParent();
		while (pNode != null)
		{
			if (pNode is CharacterBody2D cb)
			{
				_ownerNode = cb;
				_ownerSprite = cb.GetNodeOrNull<AnimatedSprite2D>("AnimatedSprite2D");
				break;
			}
			pNode = pNode.GetParent();
		}

		ApplyWeaponSettings();
	}

	private bool _isPlayerOwned = false;

	public void AddBonusAmmo(int amount)
	{
		if (CurrentWeapon != null)
		{
			_currentAmmo = Mathf.Clamp(_currentAmmo + amount, 0, CurrentWeapon.MaxAmmo);
		}
	}

	public void ApplyWeaponSettings()
	{
		if (CurrentWeapon == null || _timer == null) return;

		_timer.WaitTime = Mathf.Max(0.01f, CurrentWeapon.AttackCooldown);

		if (_instantiatedWeapon != CurrentWeapon && !_isReloading)
		{
			_currentAmmo = CurrentWeapon.MaxAmmo;
		}
		else if (_currentAmmo <= 0 && !_isReloading)
		{
			_currentAmmo = CurrentWeapon.MaxAmmo;
		}

		_isPlayerOwned = false;
		Node pNode = GetParent();
		while (pNode != null)
		{
			if (pNode is Player)
			{
				_isPlayerOwned = true;
				break;
			}
			pNode = pNode.GetParent();
		}

		if (_visualContainer != null && _instantiatedWeapon != CurrentWeapon)
		{
			_visualContainer.QueueFree();
			_visualContainer = null;
			_rangedVisual = null;
			_meleeVisual = null;
			_weaponSpriteNode = null;
			_animSpriteNode = null;
			_animPlayer = null;
		}

		if (_visualContainer == null && CurrentWeapon.WeaponVisualPrefab != null)
		{
			_visualContainer = CurrentWeapon.WeaponVisualPrefab.Instantiate() as Node2D;
			AddChild(_visualContainer);
			_instantiatedWeapon = CurrentWeapon;

			_rangedVisual = _visualContainer as RangedWeaponVisual ?? _visualContainer.GetNodeOrNull<RangedWeaponVisual>(".");
			_meleeVisual = _visualContainer as MeleeWeaponVisual ?? _visualContainer.GetNodeOrNull<MeleeWeaponVisual>(".");

			_weaponSpriteNode = _visualContainer.GetNodeOrNull<Sprite2D>("WeaponSprite") ??
								_visualContainer.GetNodeOrNull<Sprite2D>("SpriteDagger") ??
								_visualContainer.GetNodeOrNull<Sprite2D>("SpriteAxe");

			_animSpriteNode = _visualContainer.GetNodeOrNull<AnimatedSprite2D>("WeaponSprite") ??
							  _visualContainer.GetNodeOrNull<AnimatedSprite2D>("AnimatedSprite2D") ??
							  _visualContainer.GetChildren().Cast<Node>().OfType<AnimatedSprite2D>().FirstOrDefault();

			if (_animSpriteNode != null)
			{
				_animSpriteNode.AnimationFinished += () =>
				{
					if (_animSpriteNode.Animation != "idle" && _animSpriteNode.SpriteFrames.HasAnimation("idle"))
					{
						_animSpriteNode.Play("idle");
					}
				};
				_animSpriteNode.AnimationLooped += () =>
				{
					if (_animSpriteNode.Animation != "idle" && _animSpriteNode.SpriteFrames.HasAnimation("idle"))
					{
						_animSpriteNode.Play("idle");
					}
				};
			}

			_animPlayer = _visualContainer.GetNodeOrNull<AnimationPlayer>("AnimationPlayer") ??
						  _visualContainer.GetChildren().Cast<Node>().OfType<AnimationPlayer>().FirstOrDefault();

			var area = _visualContainer.GetNodeOrNull<Area2D>("Area2D") ??
					   _visualContainer.GetChildren().Cast<Node>().OfType<Area2D>().FirstOrDefault();
			if (area != null)
			{
				area.AddToGroup(_isPlayerOwned ? "PlayerWeapon" : "EnemyWeapon");
			}
		}

		HideSlashes(_visualContainer);

		if (_animSpriteNode != null && _animSpriteNode.SpriteFrames.HasAnimation("idle"))
		{
			_animSpriteNode.Play("idle");
		}
		if (_animPlayer != null && _animPlayer.HasAnimation("RESET"))
		{
			_animPlayer.Play("RESET");
		}
	}

	public override void _Process(double delta)
	{
		if (_isReloading)
		{
			_reloadElapsed += (float)delta;
		}

		if (!IsAttacking)
		{
			HideSlashes(_visualContainer);
		}

		if (!(CurrentWeapon != null && !CurrentWeapon.IsRanged && IsAttacking))
		{
			UpdateWeaponOrientation(GetTargetPosition());
		}

		if (IsInstanceValid(_ownerSprite))
		{
			Vector2 pos = Position;
			pos.X = _ownerSprite.FlipH ? -2.0f : 2.0f;
			pos.Y = -4.0f;
			Position = pos;
		}

		if (_isPlayerOwned)
		{
			if (!Input.IsActionPressed("attack"))
			{
				_playedEmptySoundThisPress = false;
			}
		}
	}

	private void UpdateWeaponOrientation(Vector2 targetPos)
	{
		Vector2 ownerPos = _ownerNode != null ? _ownerNode.GlobalPosition : GlobalPosition;
		float angleToOwner = (targetPos - ownerPos).Angle();
		float angle = (targetPos - GlobalPosition).Angle();

		Rotation = angle;

		if (_visualContainer != null)
		{
			if (_rangedVisual != null)
			{
				_rangedVisual.UpdateOrientation(targetPos, IsAttacking);
				return;
			}
			if (_meleeVisual != null)
			{
				_meleeVisual.UpdateOrientation(targetPos, IsAttacking);
				return;
			}

			var ownerSprite = _ownerNode?.GetNodeOrNull<AnimatedSprite2D>("AnimatedSprite2D");
			bool currentFlip = ownerSprite?.FlipH ?? false;
			bool isLeft = currentFlip;

			float angleDeg = Mathf.Abs(Mathf.RadToDeg(angleToOwner));
			if (angleDeg > 100) isLeft = true;
			else if (angleDeg < 80) isLeft = false;

			if (!IsAttacking)
			{
				_visualContainer.Rotation = Mathf.DegToRad(CurrentWeapon?.OrientationOffset ?? 0);
			}

			Vector2 scale = _visualContainer.Scale;
			scale.Y = isLeft ? -Mathf.Abs(scale.Y) : Mathf.Abs(scale.Y);
			_visualContainer.Scale = scale;

			if (ownerSprite != null) ownerSprite.FlipH = isLeft;
		}
	}

	private void HideSlashes(Node parent)
	{
		if (parent == null) return;
		foreach (Node child in parent.GetChildren())
		{
			if (child is Sprite2D s && s.Name.ToString().Contains("Slash"))
			{
				s.Visible = false;
			}
			HideSlashes(child);
		}
	}

	public void HandleManualAttack()
	{
		if (!_canAttack || CurrentWeapon == null || _isReloading) return;

		// 1. Проверяем патроны (только для оружия с конечным магазином)
		if (_isPlayerOwned && UsesAmmo && _currentAmmo <= 0)
		{
			if (!_playedEmptySoundThisPress)
			{
				if (CurrentWeapon.EmptySound != null)
				{
					PlaySound(CurrentWeapon.EmptySound);
				}
				_playedEmptySoundThisPress = true;
			}
			return;
		}

		// 2. Проверяем и тратим ману
		if (_isPlayerOwned && _player != null)
		{
			if (_player.CurrentMana < CurrentWeapon.ManaCost)
			{
				_player.ShowNoManaWarning();
				return;
			}
			_player.CurrentMana = Mathf.Max(0.0f, _player.CurrentMana - CurrentWeapon.ManaCost);
		}

		Vector2 targetPosition = GetTargetPosition();
		UpdateWeaponOrientation(targetPosition);

		_hitEnemies.Clear();

		if (CurrentWeapon != null && CurrentWeapon.AttackSound != null)
		{
			PlaySound(CurrentWeapon.AttackSound);
		}

		if (CurrentWeapon.IsRanged)
		{
			Shoot(targetPosition);
			if (_isPlayerOwned && UsesAmmo)
			{
				_currentAmmo--;
			}
		}
		else if (_isPlayerOwned)
		{
			Node vfxHost = GetTree().CurrentScene ?? GetTree().Root;
			Vector2 swingDir = (targetPosition - GlobalPosition).Normalized();
			Color glintColor = CurrentWeapon.WeaponName.Contains("Katana", StringComparison.OrdinalIgnoreCase)
				? new Color(0.55f, 0.92f, 1.0f)
				: new Color(0.95f, 0.95f, 1.0f);
			DungeonVisuals.SpawnImpactSparks(
				vfxHost,
				GlobalPosition + swingDir * 24.0f,
				swingDir,
				glintColor,
				3,
				95.0f,
				55.0f,
				1.2f);
		}

		if (CurrentWeapon.Recoil > 0 && _isPlayerOwned && _player != null)
		{
			Vector2 dir = (targetPosition - GlobalPosition).Normalized();
			_player.ApplyRecoil(dir, CurrentWeapon.Recoil);
		}

		if (_visualContainer != null)
		{
			var sleeveParticles = _visualContainer.GetNodeOrNull<CpuParticles2D>("CPUParticles2DSleeve") ??
								  _visualContainer.GetNodeOrNull<CpuParticles2D>("CPUParticles2D");
			if (sleeveParticles != null)
			{
				sleeveParticles.Restart();
				sleeveParticles.Emitting = true;
			}
		}

		if (_animPlayer != null)
		{
			_animPlayer.Stop();
			string animToPlay = "";
			var animList = _animPlayer.GetAnimationList();
			foreach (var anim in animList)
			{
				if (anim != "RESET")
				{
					animToPlay = anim;
					break;
				}
			}
			if (!string.IsNullOrEmpty(animToPlay)) _animPlayer.Play(animToPlay);
		}
		else if (_animSpriteNode != null)
		{
			_animSpriteNode.Stop();
			_animSpriteNode.Frame = 0;
			string animToPlay = CurrentWeapon.IsRanged ? "shoot" : "attack";
			if (!_animSpriteNode.SpriteFrames.HasAnimation(animToPlay))
			{
				var names = _animSpriteNode.SpriteFrames.GetAnimationNames();
				if (names.Length > 0) animToPlay = names[0];
			}
			_animSpriteNode.Play(animToPlay);
		}

		_canAttack = false;
		_timer.Start();
	}

	public void StartReload()
	{
		if (_isReloading || CurrentWeapon == null || !UsesAmmo || _currentAmmo == CurrentWeapon.MaxAmmo) return;

		_isReloading = true;
		_reloadElapsed = 0.0f;

		if (CurrentWeapon.ReloadSound != null)
		{
			PlaySound(CurrentWeapon.ReloadSound);
		}

		if (_animSpriteNode != null && _animSpriteNode.SpriteFrames.HasAnimation("reload"))
		{
			_animSpriteNode.Play("reload");
		}
		else if (_animPlayer != null && _animPlayer.HasAnimation("reload"))
		{
			_animPlayer.Play("reload");
		}

		var reloadTimer = GetTree().CreateTimer(CurrentWeapon.ReloadTime, processAlways: false);
		reloadTimer.Timeout += () =>
		{
			if (!GodotObject.IsInstanceValid(this) || !_isReloading) return;
			_currentAmmo = CurrentWeapon.MaxAmmo;
			_isReloading = false;
			_reloadElapsed = 0.0f;
			if (_animSpriteNode != null && _animSpriteNode.SpriteFrames.HasAnimation("idle"))
			{
				_animSpriteNode.Play("idle");
			}
		};
	}

	private void Shoot(Vector2 targetPos)
	{
		if (CurrentWeapon.BulletPrefab == null) return;

		Marker2D muzzle = null;
		if (_visualContainer != null)
		{
			muzzle = _visualContainer.GetNodeOrNull<Marker2D>("Muzzle") ??
					 _visualContainer.GetChildren().Cast<Node>().OfType<Marker2D>().FirstOrDefault();
		}

		Vector2 spawnPos = muzzle != null ? muzzle.GlobalPosition : GlobalPosition;
		Node vfxHost = GetTree().CurrentScene ?? GetTree().Root;
		Vector2 aimDir = (targetPos - spawnPos).Normalized();
		Color muzzleCol = _isPlayerOwned
			? (CurrentWeapon.WeaponName.Contains("Staff", StringComparison.OrdinalIgnoreCase)
				? new Color(0.42f, 0.88f, 1.0f)
				: new Color(1.0f, 0.82f, 0.36f))
			: new Color(1.0f, 0.44f, 0.24f);
		float flashRadius = CurrentWeapon.BulletCount > 1 ? 13.0f : 8.5f;
		DungeonVisuals.SpawnMuzzleFlash(vfxHost, spawnPos, muzzleCol, flashRadius);
		if (_isPlayerOwned)
		{
			DungeonVisuals.SpawnImpactSparks(
				vfxHost,
				spawnPos,
				aimDir,
				muzzleCol,
				CurrentWeapon.BulletCount > 1 ? 6 : 4,
				110.0f,
				38.0f,
				1.15f);
		}

		for (int i = 0; i < CurrentWeapon.BulletCount; i++)
		{
			var bulletNode = CurrentWeapon.BulletPrefab.Instantiate();

			if (bulletNode is Projectile bullet)
			{
				bullet.Position = spawnPos;

				float bulletDamage = CurrentWeapon.Damage;
				if (_ownerNode is Enemy enemy)
				{
					bulletDamage *= enemy.DamageMultiplier;
				}
				else if (_isPlayerOwned && _player != null)
				{
					bulletDamage *= _player.WeaponDamageMultiplier;
				}
				bullet.Damage = bulletDamage;

				bullet.IsPlayerOwned = _isPlayerOwned;
				bullet.IsPiercing = CurrentWeapon.IsPiercing;
				bullet.Speed = CurrentWeapon.BulletSpeed * World.Scale;
				bullet.Range = CurrentWeapon.AttackRange > 0 ? CurrentWeapon.AttackRange * 2.5f : 500.0f;
				bullet.KnockbackForce = CurrentWeapon.KnockbackForce;

				Vector2 dir = (targetPos - spawnPos).Normalized();
				if (CurrentWeapon.Spread > 0)
				{
					dir = dir.Rotated((float)GD.RandRange(-CurrentWeapon.Spread, CurrentWeapon.Spread));
				}
				bullet.Direction = dir;
				bullet.Rotation = dir.Angle();

				Area2D bulletArea = bullet.GetNodeOrNull<Area2D>("Area2D");
				if (bulletArea == null && bullet is Node bNode && bNode is Area2D rootArea)
				{
					bulletArea = rootArea;
				}

				if (bulletArea != null)
				{
					string ownerGroup = _isPlayerOwned ? "PlayerWeapon" : "EnemyWeapon";
					string wrongGroup = _isPlayerOwned ? "EnemyWeapon" : "PlayerWeapon";
					if (bulletArea.IsInGroup(wrongGroup)) bulletArea.RemoveFromGroup(wrongGroup);
					if (!bulletArea.IsInGroup(ownerGroup)) bulletArea.AddToGroup(ownerGroup);
				}
			}
			else if (bulletNode is Node2D node2D)
			{
				node2D.Position = spawnPos;
			}

			GetTree().Root.AddChild(bulletNode);
		}
	}

	public bool CanHitEnemy(ulong enemyId)
	{
		if (CurrentWeapon == null) return false;
		if (CurrentWeapon.AllowMultiHit) return true;
		if (_hitEnemies.Contains(enemyId)) return false;
		_hitEnemies.Add(enemyId);
		return true;
	}

	private Vector2 GetTargetPosition()
	{
		if (!_isPlayerOwned)
		{
			if (!IsInstanceValid(_player)) _player = GetTree().GetFirstNodeInGroup("Player") as Player;
			return _player != null ? _player.GlobalPosition : GlobalPosition;
		}

		if (CurrentWeapon != null && CurrentWeapon.IsAutoAim)
		{
			var enemies = GetTree().GetNodesInGroup("Enemies");
			Node2D nearest = null;
			float minDist = float.MaxValue;
			foreach (Node node in enemies)
			{
				if (node is Enemy enemy && GodotObject.IsInstanceValid(enemy) && enemy.CurrentHealth > 0)
				{
					float dist = GlobalPosition.DistanceTo(enemy.GlobalPosition);
					if (dist < minDist)
					{
						minDist = dist;
						nearest = enemy;
					}
				}
			}
			if (nearest != null) return nearest.GlobalPosition;
		}
		return GetGlobalMousePosition();
	}

	public void PlaySound(AudioStream stream)
	{
		GameResourceCache.PlayPooledSound(GetTree(), stream, "SFX");
	}
}
