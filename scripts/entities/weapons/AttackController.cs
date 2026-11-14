using Godot;
using System;
using System.Collections.Generic;
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

	private bool _laserTriggerHeld = false;
	private float _laserTickAccumulator = 0.0f;
	private float _laserSoundCooldown = 0.0f;
	private float _laserNoManaWarnCooldown = 0.0f;
	private Line2D _laserOuterLine;
	private Line2D _laserCoreLine;
	private PointLight2D _laserEndLight;

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

	private bool IsCoopSession => NetworkManager.Instance != null && NetworkManager.Instance.IsCoopSession;
	private bool IsCoopLocalShooter => IsCoopSession && _isPlayerOwned && _player != null && GodotObject.IsInstanceValid(_player) && _player.IsLocalPlayer;

	public void Deactivate()
	{
		if (_laserTriggerHeld && IsCoopLocalShooter)
		{
			Rpc(nameof(RpcSetRemoteLaserActive), false);
		}
		Visible = false;
		if (_animPlayer != null)
		{
			_animPlayer.Stop();
			_animPlayer.SpeedScale = 1.0f;
		}
		if (_animSpriteNode != null)
		{
			_animSpriteNode.Stop();
			_animSpriteNode.SpeedScale = 1.0f;
		}
		_isReloading = false;
		_reloadElapsed = 0.0f;
		_laserTriggerHeld = false;
		_laserTickAccumulator = 0.0f;
		StopLaserVisuals();

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

	public override void _ExitTree()
	{
		StopLaserVisuals();
		base._ExitTree();
	}

	public void SetContinuousTriggerHeld(bool held)
	{
		if (CurrentWeapon == null || !CurrentWeapon.IsContinuousLaser)
		{
			if (_laserTriggerHeld && IsCoopLocalShooter)
			{
				Rpc(nameof(RpcSetRemoteLaserActive), false);
			}
			_laserTriggerHeld = false;
			StopLaserVisuals();
			return;
		}

		bool changed = held != _laserTriggerHeld;
		if (held && !_laserTriggerHeld)
		{
			_laserTickAccumulator = GetEffectiveAttackCooldown();
		}
		_laserTriggerHeld = held;
		if (!held)
		{
			StopLaserVisuals();
		}
		if (changed && IsCoopLocalShooter)
		{
			Rpc(nameof(RpcSetRemoteLaserActive), held);
		}
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RpcSetRemoteLaserActive(bool active)
	{
		if (!_isPlayerOwned || _player == null || Multiplayer.GetRemoteSenderId() != _player.PeerId) return;
		if (active && !_laserTriggerHeld)
		{
			_laserTickAccumulator = GetEffectiveAttackCooldown();
		}
		_laserTriggerHeld = active;
		if (!active)
		{
			StopLaserVisuals();
		}
	}

	private float GetEffectiveAttackCooldown()
	{
		float baseCd = CurrentWeapon != null ? Mathf.Max(0.01f, CurrentWeapon.AttackCooldown) : 0.35f;
		if (_isPlayerOwned && _player != null && GodotObject.IsInstanceValid(_player))
		{
			baseCd /= Mathf.Max(1.0f, _player.GetMadnessAttackRateMultiplier());
		}
		else if (_ownerNode is Enemy enemy && GodotObject.IsInstanceValid(enemy))
		{
			baseCd /= Mathf.Max(1.0f, enemy.FloorAttackSpeedMultiplier);
		}
		return Mathf.Max(0.03f, baseCd);
	}

	private Node2D _ownerNode;
	private AnimatedSprite2D _ownerSprite;
	public Node2D OwnerNode => _ownerNode;

	public override void _Ready()
	{
		_timer = GetNode<Timer>("Timer");
		_timer.Timeout += () => _canAttack = true;

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

		_player = _ownerNode as Player;

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

		_laserTriggerHeld = false;
		_laserTickAccumulator = 0.0f;
		StopLaserVisuals();

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

		if (_isPlayerOwned && (_player == null || _player.IsLocalPlayer))
		{
			if (!Input.IsActionPressed("attack"))
			{
				_playedEmptySoundThisPress = false;
			}
		}
	}

	public override void _PhysicsProcess(double delta)
	{
		UpdateContinuousLaser((float)delta);
	}

	private void StopLaserVisuals()
	{
		if (GodotObject.IsInstanceValid(_laserOuterLine)) _laserOuterLine.QueueFree();
		if (GodotObject.IsInstanceValid(_laserCoreLine)) _laserCoreLine.QueueFree();
		if (GodotObject.IsInstanceValid(_laserEndLight)) _laserEndLight.QueueFree();
		_laserOuterLine = null;
		_laserCoreLine = null;
		_laserEndLight = null;
	}

	private void EnsureLaserVisualNodes()
	{
		Node vfxHost = GetTree()?.CurrentScene ?? GetTree()?.Root;
		if (vfxHost == null) return;

		if (!GodotObject.IsInstanceValid(_laserOuterLine))
		{
			_laserOuterLine = new Line2D
			{
				Width = 6.5f,
				DefaultColor = new Color(0.24f, 0.92f, 1.0f, 0.45f),
				BeginCapMode = Line2D.LineCapMode.Round,
				EndCapMode = Line2D.LineCapMode.Round,
				JointMode = Line2D.LineJointMode.Round,
				ZIndex = 11,
				Material = GameResourceCache.GetUnshadedMaterial()
			};
			vfxHost.AddChild(_laserOuterLine);
		}

		if (!GodotObject.IsInstanceValid(_laserCoreLine))
		{
			_laserCoreLine = new Line2D
			{
				Width = 2.4f,
				DefaultColor = new Color(0.92f, 0.99f, 1.0f, 0.96f),
				BeginCapMode = Line2D.LineCapMode.Round,
				EndCapMode = Line2D.LineCapMode.Round,
				JointMode = Line2D.LineJointMode.Round,
				ZIndex = 12,
				Material = GameResourceCache.GetUnshadedMaterial()
			};
			vfxHost.AddChild(_laserCoreLine);
		}

		if (!GodotObject.IsInstanceValid(_laserEndLight))
		{
			_laserEndLight = new PointLight2D
			{
				Texture = GameResourceCache.GetRadialGlowTexture(
					new Color(1f, 1f, 1f, 1f),
					new Color(1f, 1f, 1f, 0.35f),
					0.45f,
					64),
				Color = new Color(0.32f, 0.94f, 1.0f, 1.0f),
				Energy = 0.85f,
				TextureScale = 0.72f,
				BlendMode = Light2D.BlendModeEnum.Add
			};
			vfxHost.AddChild(_laserEndLight);
		}
	}

	private void UpdateContinuousLaser(float dt)
	{
		if (_laserSoundCooldown > 0.0f) _laserSoundCooldown = Mathf.Max(0.0f, _laserSoundCooldown - dt);
		if (_laserNoManaWarnCooldown > 0.0f) _laserNoManaWarnCooldown = Mathf.Max(0.0f, _laserNoManaWarnCooldown - dt);

		if (!_isPlayerOwned || !Visible || _isReloading || CurrentWeapon == null || !CurrentWeapon.IsContinuousLaser || !_laserTriggerHeld || _player == null || !GodotObject.IsInstanceValid(_player) || _player.CurrentHealth <= 0)
		{
			StopLaserVisuals();
			return;
		}

		float manaDrain = CurrentWeapon.ManaCost * dt;
		if (_player.CurrentMana < Mathf.Max(0.20f, manaDrain))
		{
			if (_player.IsLocalPlayer && _laserNoManaWarnCooldown <= 0.0f)
			{
				_player.ShowNoManaWarning();
				_laserNoManaWarnCooldown = 0.65f;
			}
			StopLaserVisuals();
			return;
		}

		if (_player.IsLocalPlayer)
		{
			_player.CurrentMana = Mathf.Max(0.0f, _player.CurrentMana - manaDrain);
		}

		Vector2 targetPos = GetTargetPosition();
		UpdateWeaponOrientation(targetPos);

		Marker2D muzzle = null;
		if (_visualContainer != null)
		{
			muzzle = _visualContainer.GetNodeOrNull<Marker2D>("Muzzle") ??
					 _visualContainer.GetChildren().Cast<Node>().OfType<Marker2D>().FirstOrDefault();
		}

		Vector2 ownerPos = _ownerNode != null && GodotObject.IsInstanceValid(_ownerNode) ? _ownerNode.GlobalPosition : GlobalPosition;
		Vector2 muzzlePos = muzzle != null ? muzzle.GlobalPosition : GlobalPosition;
		Vector2 dir = targetPos - muzzlePos;
		if (dir.LengthSquared() < 0.0001f)
		{
			dir = Vector2.Right.Rotated(GlobalRotation);
		}
		else
		{
			dir = dir.Normalized();
		}

		var excludeWallRids = new Godot.Collections.Array<Rid>();
		excludeWallRids.Add(_player.GetRid());
		foreach (Node en in GetTree().GetNodesInGroup("Enemies"))
		{
			if (en is CollisionObject2D co && GodotObject.IsInstanceValid(co))
			{
				excludeWallRids.Add(co.GetRid());
			}
		}

		var spaceState = GetWorld2D()?.DirectSpaceState;
		if (spaceState != null && ownerPos.DistanceSquaredTo(muzzlePos) > 0.25f)
		{
			var preQuery = PhysicsRayQueryParameters2D.Create(ownerPos, muzzlePos, 1u);
			preQuery.Exclude = excludeWallRids;
			var preHit = spaceState.IntersectRay(preQuery);
			if (preHit.Count > 0)
			{
				Vector2 p = preHit["position"].AsVector2();
				Vector2 n = preHit["normal"].AsVector2();
				if (n.LengthSquared() > 0.01f) muzzlePos = p + n.Normalized() * 0.5f;
				else muzzlePos = ownerPos;
			}
		}

		bool hasPrism = _player.HasRelic(GameLogic.RelicPrismLens);
		int maxBounces = hasPrism ? 3 : 0;
		float maxTotalRange = (CurrentWeapon.AttackRange > 0 ? CurrentWeapon.AttackRange : 360.0f) * (hasPrism ? 1.45f : 1.0f);
		float remainingRange = maxTotalRange;

		var points = new List<Vector2> { muzzlePos };
		var segments = new List<(Vector2 start, Vector2 end)>();
		Vector2 segStart = muzzlePos;
		Vector2 segDir = dir;
		bool endHitWall = false;
		var wallBouncePoints = new List<(Vector2 pos, Vector2 norm)>();

		for (int b = 0; b <= maxBounces && remainingRange > 1.0f; b++)
		{
			Vector2 rayEnd = segStart + segDir * remainingRange;
			bool bouncedThisStep = false;
			if (spaceState != null)
			{
				Vector2 probeStart = segStart;
				for (int skip = 0; skip < 8; skip++)
				{
					var query = PhysicsRayQueryParameters2D.Create(probeStart, rayEnd, 1u);
					query.Exclude = excludeWallRids;
					var hit = spaceState.IntersectRay(query);
					if (hit.Count == 0) break;

					var colliderObj = hit["collider"].Obj;
					if (colliderObj is Enemy || colliderObj is Player)
					{
						if (colliderObj is CollisionObject2D skipCo)
						{
							excludeWallRids.Add(skipCo.GetRid());
							probeStart = hit["position"].AsVector2() + segDir * 0.5f;
							continue;
						}
					}

					Vector2 hitPos = hit["position"].AsVector2();
					Vector2 hitNorm = hit["normal"].AsVector2();
					hitNorm = hitNorm.LengthSquared() > 0.01f ? hitNorm.Normalized() : -segDir;

					segments.Add((segStart, hitPos));
					points.Add(hitPos);
					remainingRange -= segStart.DistanceTo(hitPos);
					wallBouncePoints.Add((hitPos, hitNorm));
					endHitWall = true;

					if (b < maxBounces && remainingRange > 4.0f)
					{
						segDir = segDir.Bounce(hitNorm).Normalized();
						segStart = hitPos + hitNorm * 0.75f;
						endHitWall = false;
						bouncedThisStep = true;
					}
					break;
				}
			}

			if (bouncedThisStep) continue;
			if (!endHitWall)
			{
				segments.Add((segStart, rayEnd));
				points.Add(rayEnd);
			}
			break;
		}

		EnsureLaserVisualNodes();
		Color beamColor = CurrentWeapon.LaserColor.A > 0.01f
			? CurrentWeapon.LaserColor
			: new Color(0.24f, 0.92f, 1.0f, 1.0f);
		if (hasPrism)
		{
			beamColor = beamColor.Lerp(new Color(0.65f, 0.48f, 1.0f, 1.0f), 0.25f);
		}
		float pulse = 1.0f + 0.16f * Mathf.Sin((float)Time.GetTicksMsec() * 0.032f);

		if (GodotObject.IsInstanceValid(_laserOuterLine))
		{
			_laserOuterLine.Points = points.ToArray();
			_laserOuterLine.Width = (hasPrism ? 8.0f : 6.4f) * pulse;
			_laserOuterLine.DefaultColor = new Color(beamColor.R, beamColor.G, beamColor.B, 0.45f);
		}
		if (GodotObject.IsInstanceValid(_laserCoreLine))
		{
			_laserCoreLine.Points = points.ToArray();
			_laserCoreLine.Width = 2.4f * pulse;
			_laserCoreLine.DefaultColor = new Color(0.94f, 0.99f, 1.0f, 0.96f);
		}
		if (GodotObject.IsInstanceValid(_laserEndLight))
		{
			_laserEndLight.GlobalPosition = points[points.Count - 1];
			_laserEndLight.Color = beamColor;
			_laserEndLight.Energy = 0.78f + 0.14f * pulse;
		}

		if (_laserSoundCooldown <= 0.0f && CurrentWeapon.AttackSound != null)
		{
			PlaySound(CurrentWeapon.AttackSound);
			_laserSoundCooldown = 0.16f;
		}

		bool isServerAuthority = NetworkManager.Instance == null || NetworkManager.Instance.IsServerAuthority;
		if (isServerAuthority)
		{
			foreach (Node node in GetTree().GetNodesInGroup("ArenaBombs"))
			{
				if (node is ExplosiveBomb bomb && GodotObject.IsInstanceValid(bomb))
				{
					foreach (var (s, e) in segments)
					{
						if (bomb.GlobalPosition.DistanceSquaredTo(Geometry2D.GetClosestPointToSegment(bomb.GlobalPosition, s, e)) <= 11.0f * 11.0f)
						{
							bomb.TriggerByWeaponHit();
							break;
						}
					}
				}
			}

			foreach (Node node in GetTree().GetNodesInGroup("Mimics"))
			{
				if (node is MimicChest mimic && GodotObject.IsInstanceValid(mimic))
				{
					foreach (var (s, e) in segments)
					{
						if (mimic.GlobalPosition.DistanceSquaredTo(Geometry2D.GetClosestPointToSegment(mimic.GlobalPosition, s, e)) <= 12.0f * 12.0f)
						{
							mimic.WakeUp();
							break;
						}
					}
				}
			}
		}

		float tickInterval = GetEffectiveAttackCooldown();
		_laserTickAccumulator += dt;
		if (_laserTickAccumulator < tickInterval) return;
		_laserTickAccumulator = Mathf.Min(_laserTickAccumulator - tickInterval, tickInterval);

		float splashR = (CurrentWeapon.SplashRadius > 0.0f ? CurrentWeapon.SplashRadius : 28.0f) * (hasPrism ? 1.35f : 1.0f);
		float rawTickDmg = CurrentWeapon.Damage * _player.WeaponDamageMultiplier;

		var enemies = new List<Enemy>();
		foreach (Node node in GetTree().GetNodesInGroup("Enemies"))
		{
			if (node is Enemy e && GodotObject.IsInstanceValid(e) && e.CurrentHealth > 0)
			{
				enemies.Add(e);
			}
		}

		var directHitIds = new HashSet<ulong>();
		var splashCenters = new List<Vector2>();

		foreach (var enemy in enemies)
		{
			float minDistSq = float.MaxValue;
			foreach (var (s, e) in segments)
			{
				Vector2 closest = Geometry2D.GetClosestPointToSegment(enemy.GlobalPosition, s, e);
				float dSq = enemy.GlobalPosition.DistanceSquaredTo(closest);
				if (dSq < minDistSq) minDistSq = dSq;
			}

			if (minDistSq <= 12.5f * 12.5f)
			{
				if (isServerAuthority)
				{
					(float dmg, bool isCrit) = _player.RollDamageCrit(rawTickDmg, false);
					enemy.TakeDamage(dmg, muzzlePos, CurrentWeapon.KnockbackForce, false, isCrit, _player);
					enemy.ApplyPlayerHitRelicEffects(_player, dmg, false, isCrit);
				}
				directHitIds.Add(enemy.GetInstanceId());
				splashCenters.Add(enemy.GlobalPosition);
			}
		}

		if (endHitWall || wallBouncePoints.Count > 0)
		{
			splashCenters.Add(points[points.Count - 1]);
		}

		var splashHitIds = new HashSet<ulong>();
		float splashRadiusSq = splashR * splashR;
		float splashRaw = rawTickDmg * 0.55f;

		foreach (Vector2 center in splashCenters)
		{
			foreach (var enemy in enemies)
			{
				if (!GodotObject.IsInstanceValid(enemy) || enemy.CurrentHealth <= 0) continue;
				ulong id = enemy.GetInstanceId();
				if (directHitIds.Contains(id) || splashHitIds.Contains(id)) continue;
				if (enemy.GlobalPosition.DistanceSquaredTo(center) <= splashRadiusSq)
				{
					if (isServerAuthority)
					{
						(float sDmg, bool sCrit) = _player.RollDamageCrit(splashRaw, false);
						enemy.TakeDamage(sDmg, center, CurrentWeapon.KnockbackForce * 0.5f, false, sCrit, _player);
						enemy.ApplyPlayerHitRelicEffects(_player, sDmg, false, sCrit);
					}
					splashHitIds.Add(id);
				}
			}
		}

		Node vfxHost = GetTree()?.CurrentScene ?? GetTree()?.Root;
		if (vfxHost != null)
		{
			int ringBudget = Math.Min(3, splashCenters.Count);
			for (int i = 0; i < ringBudget; i++)
			{
				vfxHost.AddChild(new ExpandingRingVfx(splashCenters[i], splashR, new Color(beamColor.R, beamColor.G, beamColor.B, 0.65f), 0.15f));
			}
			foreach (var (bp, bNorm) in wallBouncePoints)
			{
				DungeonVisuals.SpawnImpactSparks(vfxHost, bp, bNorm, beamColor, 3, 28.0f, 55.0f, 0.7f);
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
		if (CurrentWeapon.IsContinuousLaser) return;
		if (IsCoopSession)
		{
			if (_isPlayerOwned && (_player == null || !_player.IsLocalPlayer)) return;
			if (!_isPlayerOwned && !NetworkManager.Instance.IsServerAuthority) return;
		}

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
		Vector2 spawnPos = GetMuzzleGlobalPosition();
		Vector2[] shotDirections = CurrentWeapon.IsRanged
			? ComputeShotDirections(spawnPos, targetPosition)
			: Array.Empty<Vector2>();

		ExecuteAttackVisualsAndProjectiles(targetPosition, shotDirections, applyRecoil: true);

		if (IsCoopSession)
		{
			Rpc(nameof(RpcPerformRemoteAttack), targetPosition, shotDirections);
		}
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RpcPerformRemoteAttack(Vector2 targetPosition, Vector2[] shotDirections)
	{
		int sender = Multiplayer.GetRemoteSenderId();
		if (_isPlayerOwned)
		{
			if (_player == null || sender != _player.PeerId) return;
		}
		else
		{
			if (sender != 1) return;
		}

		if (CurrentWeapon == null || CurrentWeapon.IsContinuousLaser) return;
		ExecuteAttackVisualsAndProjectiles(targetPosition, shotDirections, applyRecoil: false);
	}

	private void ExecuteAttackVisualsAndProjectiles(Vector2 targetPosition, Vector2[] shotDirections, bool applyRecoil)
	{
		if (CurrentWeapon == null) return;

		UpdateWeaponOrientation(targetPosition);
		_hitEnemies.Clear();

		if (CurrentWeapon.AttackSound != null)
		{
			PlaySound(CurrentWeapon.AttackSound);
		}

		if (CurrentWeapon.IsRanged)
		{
			Shoot(targetPosition, shotDirections);
			if (_isPlayerOwned && UsesAmmo && _currentAmmo > 0)
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
				GlobalPosition + swingDir * 8.0f,
				swingDir,
				glintColor,
				3,
				32.0f,
				55.0f,
				0.75f);
		}

		if (applyRecoil && CurrentWeapon.Recoil > 0 && _isPlayerOwned && _player != null && _player.IsLocalPlayer)
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

		float animSpeed = (_isPlayerOwned && _player != null && GodotObject.IsInstanceValid(_player))
			? Mathf.Max(1.0f, _player.GetMadnessAttackRateMultiplier())
			: 1.0f;

		if (_animPlayer != null)
		{
			_animPlayer.Stop();
			_animPlayer.SpeedScale = animSpeed;
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
			_animSpriteNode.SpeedScale = animSpeed;
			string animToPlay = CurrentWeapon.IsRanged ? "shoot" : "attack";
			if (!_animSpriteNode.SpriteFrames.HasAnimation(animToPlay))
			{
				var names = _animSpriteNode.SpriteFrames.GetAnimationNames();
				if (names.Length > 0) animToPlay = names[0];
			}
			_animSpriteNode.Play(animToPlay);
		}

		_canAttack = false;
		_timer.Start(GetEffectiveAttackCooldown());
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

	private Vector2 GetMuzzleGlobalPosition()
	{
		Marker2D muzzle = null;
		if (_visualContainer != null)
		{
			muzzle = _visualContainer.GetNodeOrNull<Marker2D>("Muzzle") ??
					 _visualContainer.GetChildren().Cast<Node>().OfType<Marker2D>().FirstOrDefault();
		}
		return muzzle != null ? muzzle.GlobalPosition : GlobalPosition;
	}

	private Vector2[] ComputeShotDirections(Vector2 spawnPos, Vector2 targetPos)
	{
		int count = CurrentWeapon != null ? Math.Max(0, CurrentWeapon.BulletCount) : 0;
		Vector2[] dirs = new Vector2[count];
		Vector2 baseDir = (targetPos - spawnPos).Normalized();
		if (baseDir == Vector2.Zero) baseDir = Vector2.Right;

		for (int i = 0; i < count; i++)
		{
			Vector2 dir = baseDir;
			if (CurrentWeapon.Spread > 0)
			{
				dir = dir.Rotated((float)GD.RandRange(-CurrentWeapon.Spread, CurrentWeapon.Spread));
			}
			dirs[i] = dir;
		}
		return dirs;
	}

	private void Shoot(Vector2 targetPos, Vector2[] shotDirections = null)
	{
		if (CurrentWeapon.BulletPrefab == null) return;

		Vector2 spawnPos = GetMuzzleGlobalPosition();
		Vector2[] dirs = (shotDirections != null && shotDirections.Length >= CurrentWeapon.BulletCount)
			? shotDirections
			: ComputeShotDirections(spawnPos, targetPos);
		Node vfxHost = GetTree().CurrentScene ?? GetTree().Root;
		Vector2 aimDir = (targetPos - spawnPos).Normalized();
		Color muzzleCol = _isPlayerOwned
			? (CurrentWeapon.WeaponName.Contains("Staff", StringComparison.OrdinalIgnoreCase)
				? new Color(0.42f, 0.88f, 1.0f)
				: new Color(1.0f, 0.82f, 0.36f))
			: new Color(1.0f, 0.44f, 0.24f);
		float flashRadius = CurrentWeapon.BulletCount > 1 ? 4.4f : 2.9f;
		DungeonVisuals.SpawnMuzzleFlash(vfxHost, spawnPos, muzzleCol, flashRadius);
		if (_isPlayerOwned)
		{
			DungeonVisuals.SpawnImpactSparks(
				vfxHost,
				spawnPos,
				aimDir,
				muzzleCol,
				CurrentWeapon.BulletCount > 1 ? 6 : 4,
				38.0f,
				38.0f,
				0.75f);
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
				bullet.IsBladeProjectile = _isPlayerOwned && GameLogic.IsBladeWeaponName(CurrentWeapon.WeaponName);
				bullet.OwnerPlayer = _player;
				bullet.SourceEnemy = _ownerNode as Enemy;
				bullet.Speed = CurrentWeapon.BulletSpeed;
				bullet.Range = CurrentWeapon.AttackRange > 0 ? CurrentWeapon.AttackRange * 2.5f : 500.0f;
				bullet.KnockbackForce = CurrentWeapon.KnockbackForce;

				if (_isPlayerOwned && _player != null && GodotObject.IsInstanceValid(_player) && _player.HasRelic(GameLogic.RelicRicochet))
				{
					bullet.RemainingWallBounces = 1;
					bullet.Speed *= 1.12f;
				}

				Vector2 dir = dirs[i];
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
			var nearestAlive = NetworkManager.GetNearestAlivePlayer(GetTree(), GlobalPosition);
			return nearestAlive != null ? nearestAlive.GlobalPosition : GlobalPosition;
		}

		if (_player != null && !_player.IsLocalPlayer)
		{
			return _player.NetworkAimWorldPos;
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
