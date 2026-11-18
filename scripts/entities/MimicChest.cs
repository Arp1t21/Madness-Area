using Godot;
using System;

/// <summary>
/// Rare disguised chest that ambushes the player when interacted with [E] or damaged,
/// transforming into a fast Mimic mini-boss that drops guaranteed coins and a weapon.
/// </summary>
public partial class MimicChest : Node2D, IInteractable
{
	public int LevelIndex { get; set; } = 1;

	private Sprite2D _sprite;
	private AtlasTexture _closedTex;
	private AtlasTexture _jawHalfTex;
	private AtlasTexture _jawOpenTex;
	private Label _promptLabel;
	private bool _woken = false;

	public override void _Ready()
	{
		AddToGroup("ArenaInteractable");
		AddToGroup("Mimics");
		ZIndex = 2;

		var sheet = GD.Load<Texture2D>("res://assets/objects/0x72_DungeonTilesetII_v1.7.png");
		_closedTex = new AtlasTexture { Atlas = sheet, Region = new Rect2(19 * 16, 26 * 16, 16, 16) };
		_jawHalfTex = new AtlasTexture { Atlas = sheet, Region = new Rect2(20 * 16, 27 * 16, 16, 16) };
		_jawOpenTex = new AtlasTexture { Atlas = sheet, Region = new Rect2(21 * 16, 27 * 16, 16, 16) };

		_sprite = new Sprite2D
		{
			Texture = _closedTex,
			TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
			Position = new Vector2(0, -4)
		};
		AddChild(_sprite);

		var shadow = new Sprite2D
		{
			Texture = GameResourceCache.GetRadialGlowTexture(
				new Color(0, 0, 0, 0.55f),
				new Color(0, 0, 0, 0),
				0.5f,
				24),
			Position = new Vector2(0, 3),
			Scale = new Vector2(0.65f, 0.25f),
			ZIndex = -1
		};
		AddChild(shadow);

		_promptLabel = new Label
		{
			Text = "[E] Open Chest",
			Visible = false
		};
		DungeonVisuals.StyleWorldPromptLabel(_promptLabel, DungeonVisuals.BrassGold, -25.0f);
		AddChild(_promptLabel);

		var interactArea = new Area2D
		{
			CollisionLayer = 0,
			CollisionMask = 16
		};
		interactArea.AddChild(new CollisionShape2D { Shape = new CircleShape2D { Radius = 18.0f } });
		interactArea.AreaEntered += area => CheckRange(area.GetParent(), true);
		interactArea.AreaExited += area => CheckRange(area.GetParent(), false);
		interactArea.BodyEntered += body => CheckRange(body, true);
		interactArea.BodyExited += body => CheckRange(body, false);
		AddChild(interactArea);

		var hitArea = new Area2D
		{
			CollisionLayer = 0,
			CollisionMask = 0xFFFF
		};
		hitArea.AddChild(new CollisionShape2D
		{
			Shape = new RectangleShape2D { Size = new Vector2(14, 14) },
			Position = new Vector2(0, -4)
		});
		hitArea.AreaEntered += area =>
		{
			if (!_woken && IsServerAuthority && area != null && area.IsInGroup("PlayerWeapon"))
			{
				WakeUp();
			}
		};
		AddChild(hitArea);
	}

	private bool IsCoopSession => NetworkManager.Instance != null && NetworkManager.Instance.IsCoopSession;
	private bool IsServerAuthority => !IsCoopSession || (NetworkManager.Instance != null && NetworkManager.Instance.IsServerAuthority);

	private void CheckRange(Node node, bool entered)
	{
		if (_woken) return;
		if (node is Player p && p.IsLocalPlayer && !p.IsDead)
		{
			if (entered) p.RegisterInteractable(this);
			else p.UnregisterInteractable(this);
		}
	}

	public void Interact(Player player)
	{
		if (_woken) return;
		if (IsCoopSession && (player == null || !player.IsLocalPlayer || player.IsDead)) return;
		player?.UnregisterInteractable(this);
		if (IsCoopSession && !IsServerAuthority)
		{
			RpcId(1, nameof(RpcRequestWakeMimic));
			return;
		}
		WakeUp();
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RpcRequestWakeMimic()
	{
		if (!IsServerAuthority || _woken) return;
		WakeUp();
	}

	public void WakeUp()
	{
		if (_woken) return;
		if (IsCoopSession)
		{
			if (!IsServerAuthority) return;
			_woken = true;
			int netId = NetworkManager.Instance != null ? NetworkManager.Instance.AllocateEntityNetId() : 0;
			Rpc(nameof(RpcWakeMimic), netId);
			return;
		}

		_woken = true;
		RunWakeUpSequenceAsync(0);
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RpcWakeMimic(int netId)
	{
		int sender = Multiplayer.GetRemoteSenderId();
		if (IsCoopSession && sender != 0 && sender != 1) return;
		_woken = true;
		RunWakeUpSequenceAsync(netId);
	}

	private async void RunWakeUpSequenceAsync(int netId)
	{
		if (_promptLabel != null) _promptLabel.Visible = false;

		Node host = GetParent();
		if (host != null)
		{
			host.AddChild(new ExpandingRingVfx(GlobalPosition, 26.0f, new Color(1.0f, 0.25f, 0.28f, 0.9f), 0.22f));
		}

		if (_sprite != null) _sprite.Texture = _jawHalfTex;
		await ToSignal(GetTree().CreateTimer(0.11f, processAlways: false), "timeout");
		if (!GodotObject.IsInstanceValid(this)) return;
		if (_sprite != null) _sprite.Texture = _jawOpenTex;
		await ToSignal(GetTree().CreateTimer(0.11f, processAlways: false), "timeout");
		if (!GodotObject.IsInstanceValid(this)) return;

		var mimic = new MimicEnemy(LevelIndex);
		if (netId > 0)
		{
			mimic.NetId = netId;
			mimic.Name = $"Enemy_{netId}";
		}
		mimic.GlobalPosition = GlobalPosition;
		host?.AddChild(mimic);

		QueueFree();
	}

	public Vector2 GetInteractionPosition() => GlobalPosition;

	public void ShowPrompt(bool show)
	{
		if (_promptLabel != null && !_woken)
		{
			_promptLabel.Visible = show;
		}
	}
}

public partial class MimicEnemy : Enemy
{
	private readonly int _spawnFloor;

	public MimicEnemy() : this(1) { }

	public MimicEnemy(int floorIndex)
	{
		_spawnFloor = Math.Max(1, floorIndex);
		CollisionLayer = 0;
		CollisionMask = 1;
	}

	protected override Color WarningLabelColor => new Color(1.0f, 0.28f, 0.35f);

	public override void _Ready()
	{
		Config = new EnemyData
		{
			EnemyName = "Treasure Mimic",
			Speed = 64.0f,
			MaxHealth = 48 + (_spawnFloor - 1) * 14,
			Damage = 11.0f + (_spawnFloor - 1) * 1.4f,
			AttackRange = 22.0f,
			PreAttackDelay = 0.28f,
			StopDuration = 0.45f,
			CoinDropChance = 1.0f,
			ManaCrystalDropChance = 0.65f,
			DamageNumberColor = new Color(1.0f, 0.85f, 0.25f),
			FlashColor = new Color(1.0f, 0.3f, 0.25f)
		};
		AttackConfig = GameDataRegistry.LoadFreshWeapon("res://data/waepons/enemies/orc_melee.tres")
			?? GD.Load<WeaponData>("res://data/waepons/enemies/orc_melee.tres");

		var sheet = GD.Load<Texture2D>("res://assets/objects/0x72_DungeonTilesetII_v1.7.png");
		var frames = new SpriteFrames();
		frames.AddAnimation("Idle");
		frames.SetAnimationSpeed("Idle", 6.0);
		frames.AddFrame("Idle", new AtlasTexture { Atlas = sheet, Region = new Rect2(19 * 16, 27 * 16, 16, 16) });
		frames.AddFrame("Idle", new AtlasTexture { Atlas = sheet, Region = new Rect2(20 * 16, 27 * 16, 16, 16) });

		frames.AddAnimation("Run");
		frames.SetAnimationSpeed("Run", 11.0);
		frames.AddFrame("Run", new AtlasTexture { Atlas = sheet, Region = new Rect2(19 * 16, 27 * 16, 16, 16) });
		frames.AddFrame("Run", new AtlasTexture { Atlas = sheet, Region = new Rect2(20 * 16, 27 * 16, 16, 16) });
		frames.AddFrame("Run", new AtlasTexture { Atlas = sheet, Region = new Rect2(21 * 16, 27 * 16, 16, 16) });

		var animSprite = new AnimatedSprite2D
		{
			Name = "AnimatedSprite2D",
			SpriteFrames = frames,
			Animation = "Idle",
			Position = new Vector2(0, -8),
			TextureFilter = CanvasItem.TextureFilterEnum.Nearest
		};
		AddChild(animSprite);

		var hitBox = new Area2D
		{
			Name = "Area2DHitBox",
			CollisionLayer = 0,
			CollisionMask = 4
		};
		hitBox.AddToGroup("enemy");
		hitBox.AddChild(new CollisionShape2D
		{
			Shape = new RectangleShape2D { Size = new Vector2(13, 13) },
			Position = new Vector2(0, -7)
		});
		AddChild(hitBox);

		AddChild(new CollisionShape2D
		{
			Shape = new RectangleShape2D { Size = new Vector2(10, 10) },
			Position = new Vector2(0, -6)
		});

		base._Ready();
		animSprite.Play("Idle");
		SetupGlowLight(
			"MimicLight",
			new Vector2(0, -8),
			new Color(1.0f, 0.65f, 0.22f, 1.0f),
			0.75f,
			new Color(1.0f, 0.8f, 0.35f, 1.0f),
			new Color(1.0f, 0.45f, 0.1f, 0.45f),
			0.45f,
			64);
	}

	protected override void TickEnemyAI(double delta)
	{
		if (StepEnemyActionState(delta))
		{
			float dist = GlobalPosition.DistanceTo(_player.GlobalPosition);
			UpdateMeleeChaseVelocity(delta, 8.0f, 20.0f, 1.15f, 0.06f);
			if (_cancelAttackCooldown <= 0 && _attackController != null && _attackController.CanAttack())
			{
				if (dist <= Config.AttackRange)
				{
					StartAttackPreparation();
				}
			}
		}

		MoveAndSlide();
	}

	protected override void Die()
	{
		if (!IsServerAuthority || _hasDied) return;
		Vector2 dropPos = GlobalPosition;
		Node parentNode = GetParent();
		base.Die();

		if (parentNode != null)
		{
			var level = GetTree()?.GetFirstNodeInGroup("Level") as Level;
			string[] mimicWeapons = new[]
			{
				"res://data/waepons/laser_cannon.tres",
				"res://data/waepons/katana.tres",
				"res://data/waepons/shotgun.tres",
				"res://data/waepons/auto_rifle.tres",
				"res://data/waepons/greatsword.tres",
				"res://data/waepons/magic_staff.tres"
			};
			string pickedWeapon = mimicWeapons[GD.Randi() % mimicWeapons.Length];
			level?.SpawnWeaponPickupAt(dropPos, pickedWeapon);

			if (IsCoopSession)
			{
				for (int i = 0; i < 4; i++)
				{
					Vector2 coinPos = dropPos + new Vector2((float)GD.RandRange(-10, 10), (float)GD.RandRange(-10, 10));
					level?.ServerSpawnPickupDrop(coinPos, isMana: false);
				}
				return;
			}

			var coinPrefab = GD.Load<PackedScene>("res://scene/objects/coin.tscn");
			if (coinPrefab != null)
			{
				for (int i = 0; i < 4; i++)
				{
					var c = coinPrefab.Instantiate() as Node2D;
					if (c != null)
					{
						c.GlobalPosition = dropPos + new Vector2((float)GD.RandRange(-10, 10), (float)GD.RandRange(-10, 10));
						parentNode.CallDeferred("add_child", c);
					}
				}
			}
		}
	}
}
