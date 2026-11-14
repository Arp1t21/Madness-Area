using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// Interactive and destructible dungeon hazards: explosive powder bombs (hit or [E] fuse)
/// and spike-surge levers connected to nearby retractable floor spike pads.
/// </summary>
public partial class ExplosiveBomb : Node2D, IInteractable
{
	public float BlastRadius { get; set; } = 50.0f;
	public float BlastDamage { get; set; } = 30.0f;

	private Sprite2D _sprite;
	private AtlasTexture[] _fuseFrames;
	private PointLight2D _fuseLight;
	private Label _promptLabel;
	private Area2D _interactArea;
	private Area2D _hitArea;
	private bool _fuseLit = false;
	private bool _detonated = false;
	private float _fuseRemaining = 0.0f;
	private float _animTimer = 0.0f;

	public override void _Ready()
	{
		AddToGroup("ArenaInteractable");
		AddToGroup("ArenaBombs");
		ZIndex = 2;

		var sheet = GD.Load<Texture2D>("res://assets/objects/0x72_DungeonTilesetII_v1.7.png");
		_fuseFrames = new AtlasTexture[3];
		for (int i = 0; i < 3; i++)
		{
			_fuseFrames[i] = new AtlasTexture
			{
				Atlas = sheet,
				Region = new Rect2((18 + i) * 16, 20 * 16, 16, 16)
			};
		}

		_sprite = new Sprite2D
		{
			Texture = _fuseFrames[0],
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
			Position = new Vector2(0, 2),
			Scale = new Vector2(0.55f, 0.25f),
			ZIndex = -1
		};
		AddChild(shadow);

		_fuseLight = GameResourceCache.CreateGlowLight(
			this,
			"FuseGlow",
			new Vector2(0, -10),
			new Color(1.0f, 0.52f, 0.16f, 1.0f),
			0.55f,
			GameResourceCache.GetRadialGlowTexture(
				new Color(1, 0.7f, 0.25f, 0.9f),
				new Color(1, 0.3f, 0.05f, 0.35f),
				0.45f,
				48),
			shadowEnabled: false);
		if (_fuseLight != null) _fuseLight.TextureScale = 0.45f;

		_promptLabel = new Label
		{
			Text = "[E] Light Fuse",
			Visible = false
		};
		DungeonVisuals.StyleWorldPromptLabel(_promptLabel, new Color(1.0f, 0.65f, 0.22f), -24.0f);
		AddChild(_promptLabel);

		_interactArea = new Area2D
		{
			CollisionLayer = 0,
			CollisionMask = 16
		};
		var interactShape = new CollisionShape2D
		{
			Shape = new CircleShape2D { Radius = 18.0f }
		};
		_interactArea.AddChild(interactShape);
		_interactArea.AreaEntered += area => CheckPlayerRange(area.GetParent(), true);
		_interactArea.AreaExited += area => CheckPlayerRange(area.GetParent(), false);
		_interactArea.BodyEntered += body => CheckPlayerRange(body, true);
		_interactArea.BodyExited += body => CheckPlayerRange(body, false);
		AddChild(_interactArea);

		_hitArea = new Area2D
		{
			CollisionLayer = 0,
			CollisionMask = 0xFFFF
		};
		var hitShape = new CollisionShape2D
		{
			Shape = new CircleShape2D { Radius = 9.0f },
			Position = new Vector2(0, -3)
		};
		_hitArea.AddChild(hitShape);
		_hitArea.AreaEntered += OnHitAreaEntered;
		AddChild(_hitArea);
	}

	private void CheckPlayerRange(Node node, bool entered)
	{
		if (_detonated || _fuseLit) return;
		if (node is Player player)
		{
			if (entered) player.RegisterInteractable(this);
			else player.UnregisterInteractable(this);
		}
	}

	private void OnHitAreaEntered(Area2D area)
	{
		if (_detonated) return;
		if (area != null && area.IsInGroup("PlayerWeapon"))
		{
			TriggerByWeaponHit();
		}
	}

	private bool IsCoopSession => NetworkManager.Instance != null && NetworkManager.Instance.IsCoopSession;
	private bool IsServerAuthority => NetworkManager.Instance == null || NetworkManager.Instance.IsServerAuthority;

	public void TriggerByWeaponHit()
	{
		if (IsCoopSession && !IsServerAuthority) return;
		if (_detonated || _fuseLit) return;
		BroadcastLightFuse(0.08f);
	}

	public void Interact(Player player)
	{
		if (_detonated || _fuseLit) return;
		if (IsCoopSession && (player == null || !player.IsLocalPlayer)) return;
		player?.UnregisterInteractable(this);
		ShowPrompt(false);
		if (IsCoopSession && !IsServerAuthority)
		{
			RpcId(1, nameof(RpcRequestFuseBomb));
		}
		else
		{
			BroadcastLightFuse(0.45f);
		}
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RpcRequestFuseBomb()
	{
		if (!IsServerAuthority || _detonated || _fuseLit) return;
		BroadcastLightFuse(0.45f);
	}

	private void BroadcastLightFuse(float duration)
	{
		if (IsCoopSession)
		{
			Rpc(nameof(RpcLightFuse), duration);
		}
		else
		{
			LightFuse(duration);
		}
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RpcLightFuse(float duration)
	{
		int sender = Multiplayer.GetRemoteSenderId();
		if (IsCoopSession && sender != 0 && sender != 1) return;
		LightFuse(duration);
	}

	private void LightFuse(float duration)
	{
		if (_fuseLit || _detonated) return;
		_fuseLit = true;
		_fuseRemaining = duration;
		if (_promptLabel != null) _promptLabel.Visible = false;
		if (_fuseLight != null) _fuseLight.Energy = 1.25f;
	}

	public override void _Process(double delta)
	{
		if (_detonated) return;
		float dt = (float)delta;
		_animTimer += dt * (_fuseLit ? 16.0f : 5.0f);
		int frameIdx = ((int)_animTimer) % _fuseFrames.Length;
		if (_sprite != null)
		{
			_sprite.Texture = _fuseFrames[frameIdx];
			_sprite.Modulate = _fuseLit && (((int)_animTimer) % 2 == 0)
				? new Color(1.45f, 0.85f, 0.55f, 1.0f)
				: Colors.White;
		}

		if (_fuseLit)
		{
			_fuseRemaining -= dt;
			if (_fuseRemaining <= 0.0f)
			{
				Detonate();
			}
		}
		QueueRedraw();
	}

	public override void _Draw()
	{
		if (_detonated) return;
		if (_fuseLit)
		{
			float pulse = 0.6f + 0.4f * Mathf.Sin(_animTimer * 1.4f);
			DrawCircle(Vector2.Zero, BlastRadius, new Color(1.0f, 0.24f, 0.08f, 0.10f * pulse));
			DrawArc(Vector2.Zero, BlastRadius, 0.0f, Mathf.Tau, 32, new Color(1.0f, 0.48f, 0.16f, 0.55f * pulse), 1.2f, false);
		}
		else
		{
			float idlePulse = 0.65f + 0.35f * Mathf.Sin(_animTimer * 0.7f);
			DrawArc(new Vector2(0, 1), 10.5f, 0.0f, Mathf.Tau, 20, new Color(1.0f, 0.55f, 0.18f, 0.30f * idlePulse), 1.0f, false);
		}
	}

	private void Detonate()
	{
		if (_detonated) return;
		_detonated = true;

		Node vfxHost = GetTree()?.CurrentScene ?? GetParent();
		if (vfxHost != null)
		{
			vfxHost.AddChild(new ExpandingRingVfx(GlobalPosition, BlastRadius, new Color(1.0f, 0.45f, 0.14f, 0.92f), 0.24f));
			DungeonVisuals.SpawnImpactSparks(vfxHost, GlobalPosition, Vector2.Up, new Color(1.0f, 0.56f, 0.18f), 16, 60.0f, 115.0f, 1.05f);
			DungeonVisuals.SpawnMuzzleFlash(vfxHost, GlobalPosition, new Color(1.0f, 0.65f, 0.22f), 5.5f);
		}

		if (IsServerAuthority)
		{
			foreach (Node node in GetTree().GetNodesInGroup("Enemies"))
			{
				if (node is Enemy enemy && GodotObject.IsInstanceValid(enemy) && enemy.CurrentHealth > 0)
				{
					if (GlobalPosition.DistanceTo(enemy.GlobalPosition) <= BlastRadius)
					{
						enemy.TakeDamage(BlastDamage, GlobalPosition, 22.0f, false, true);
						enemy.ApplyStatusEffect(StatusEffectType.Burn, 3.5f, 6.0f);
					}
				}
			}

			foreach (Node node in GetTree().GetNodesInGroup("ArenaBombs"))
			{
				if (node is ExplosiveBomb other && other != this && GodotObject.IsInstanceValid(other) && !other._detonated)
				{
					if (GlobalPosition.DistanceTo(other.GlobalPosition) <= BlastRadius + 10.0f)
					{
						other.BroadcastLightFuse(0.12f);
					}
				}
			}

			foreach (Node node in GetTree().GetNodesInGroup("Mimics"))
			{
				if (node is MimicChest mimic && GodotObject.IsInstanceValid(mimic))
				{
					if (GlobalPosition.DistanceTo(mimic.GlobalPosition) <= BlastRadius)
					{
						mimic.WakeUp();
					}
				}
			}
		}

		var sound = GD.Load<AudioStream>("res://assets/sounds/shotgun_shot.wav");
		if (sound != null) GameResourceCache.PlayPooledSound(GetTree(), sound, "SFX");

		QueueFree();
	}

	public Vector2 GetInteractionPosition() => GlobalPosition;

	public void ShowPrompt(bool show)
	{
		if (_promptLabel != null && !_detonated && !_fuseLit)
		{
			_promptLabel.Visible = show;
		}
	}
}

public partial class SpikeTrapPad : Node2D
{
	private Sprite2D _sprite;
	private AtlasTexture _idleTex;
	private AtlasTexture _activeTex;
	private bool _active = false;

	public override void _Ready()
	{
		ZIndex = 1;
		var sheet = GD.Load<Texture2D>("res://assets/objects/0x72_DungeonTilesetII_v1.7.png");
		_idleTex = new AtlasTexture { Atlas = sheet, Region = new Rect2(1 * 16, 12 * 16, 16, 16) };
		_activeTex = new AtlasTexture { Atlas = sheet, Region = new Rect2(4 * 16, 12 * 16, 16, 16) };

		_sprite = new Sprite2D
		{
			Texture = _idleTex,
			TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
			Modulate = new Color(1.18f, 1.08f, 1.08f, 0.98f)
		};
		AddChild(_sprite);

		// Subtle corner iron studs to make floor spike plates legible in dim rooms
		QueueRedraw();
	}

	public override void _Draw()
	{
		Color rim = _active
			? new Color(1.0f, 0.32f, 0.24f, 0.72f)
			: new Color(0.68f, 0.72f, 0.80f, 0.28f);
		DrawRect(new Rect2(-7.5f, -7.5f, 15.0f, 15.0f), rim, false, 1.0f);
	}

	public void TriggerSpikeSurge(float damage)
	{
		if (_active) return;
		_active = true;
		if (_sprite != null)
		{
			_sprite.Texture = _activeTex;
			_sprite.Modulate = new Color(1.3f, 0.55f, 0.55f, 1.0f);
		}

		const float hitRadius = 42.0f;
		Node vfxHost = GetTree()?.CurrentScene ?? GetParent();
		if (vfxHost != null)
		{
			vfxHost.AddChild(new ExpandingRingVfx(GlobalPosition, hitRadius, new Color(0.88f, 0.30f, 0.35f, 0.85f), 0.28f));
			DungeonVisuals.SpawnImpactSparks(vfxHost, GlobalPosition, Vector2.Up, new Color(0.92f, 0.88f, 0.96f), 8, 35.0f, 70.0f, 0.9f);
		}

		if (NetworkManager.Instance == null || NetworkManager.Instance.IsServerAuthority)
		{
			foreach (Node node in GetTree().GetNodesInGroup("Enemies"))
			{
				if (node is Enemy enemy && GodotObject.IsInstanceValid(enemy) && enemy.CurrentHealth > 0)
				{
					if (GlobalPosition.DistanceTo(enemy.GlobalPosition) <= hitRadius)
					{
						enemy.TakeDamage(damage, GlobalPosition, 12.0f, true);
						enemy.ApplyStatusEffect(StatusEffectType.Bleed, 3.0f, 5.0f);
						enemy.ApplyStatusEffect(StatusEffectType.Root, 0.95f, 1.0f);
					}
				}
			}
		}

		var t = GetTree().CreateTimer(1.25f, processAlways: false);
		t.Timeout += () =>
		{
			_active = false;
			if (GodotObject.IsInstanceValid(_sprite))
			{
				_sprite.Texture = _idleTex;
				_sprite.Modulate = new Color(1.05f, 0.95f, 0.95f, 0.95f);
			}
		};
	}
}

public partial class TrapLever : Node2D, IInteractable
{
	public readonly List<SpikeTrapPad> LinkedPads = new List<SpikeTrapPad>();
	public float SurgeDamage { get; set; } = 22.0f;

	private Sprite2D _sprite;
	private AtlasTexture _offTex;
	private AtlasTexture _onTex;
	private Label _promptLabel;
	private float _cooldownRemaining = 0.0f;

	public override void _Ready()
	{
		AddToGroup("ArenaInteractable");
		ZIndex = 2;

		var sheet = GD.Load<Texture2D>("res://assets/objects/0x72_DungeonTilesetII_v1.7.png");
		_offTex = new AtlasTexture { Atlas = sheet, Region = new Rect2(5 * 16, 13 * 16, 16, 16) };
		_onTex = new AtlasTexture { Atlas = sheet, Region = new Rect2(6 * 16, 13 * 16, 16, 16) };

		_sprite = new Sprite2D
		{
			Texture = _offTex,
			TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
			Modulate = new Color(1.12f, 1.15f, 1.20f, 1.0f)
		};
		AddChild(_sprite);

		var floorGlow = GameResourceCache.CreateGlowLight(
			this,
			"LeverReadyGlow",
			Vector2.Zero,
			new Color(0.35f, 0.88f, 1.0f, 0.75f),
			0.35f,
			GameResourceCache.GetRadialGlowTexture(
				new Color(0.4f, 0.9f, 1.0f, 0.65f),
				Colors.Transparent,
				0.5f,
				32),
			shadowEnabled: false);
		if (floorGlow != null) floorGlow.TextureScale = 0.42f;

		_promptLabel = new Label
		{
			Text = "[E] Pull Lever",
			Visible = false
		};
		DungeonVisuals.StyleWorldPromptLabel(_promptLabel, DungeonVisuals.ArcaneCyan, -24.0f);
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
	}

	private void CheckRange(Node node, bool entered)
	{
		if (node is Player p)
		{
			if (entered) p.RegisterInteractable(this);
			else p.UnregisterInteractable(this);
		}
	}

	public override void _Process(double delta)
	{
		if (_cooldownRemaining > 0.0f)
		{
			_cooldownRemaining = Mathf.Max(0.0f, _cooldownRemaining - (float)delta);
			if (_cooldownRemaining <= 0.0f && _sprite != null)
			{
				_sprite.Texture = _offTex;
				if (_promptLabel != null) _promptLabel.Text = "[E] Pull Lever";
			}
		}
	}

	private bool IsCoopSession => NetworkManager.Instance != null && NetworkManager.Instance.IsCoopSession;
	private bool IsServerAuthority => NetworkManager.Instance == null || NetworkManager.Instance.IsServerAuthority;

	public void Interact(Player player)
	{
		if (_cooldownRemaining > 0.0f) return;
		if (IsCoopSession && (player == null || !player.IsLocalPlayer)) return;
		if (IsCoopSession && !IsServerAuthority)
		{
			RpcId(1, nameof(RpcRequestPullLever));
			return;
		}
		if (IsCoopSession)
		{
			Rpc(nameof(RpcActivateLever));
		}
		else
		{
			ActivateLever();
		}
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RpcRequestPullLever()
	{
		if (!IsServerAuthority || _cooldownRemaining > 0.0f) return;
		Rpc(nameof(RpcActivateLever));
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RpcActivateLever()
	{
		int sender = Multiplayer.GetRemoteSenderId();
		if (IsCoopSession && sender != 0 && sender != 1) return;
		ActivateLever();
	}

	private void ActivateLever()
	{
		if (_cooldownRemaining > 0.0f) return;
		_cooldownRemaining = 4.5f;
		if (_sprite != null) _sprite.Texture = _onTex;
		if (_promptLabel != null) _promptLabel.Text = "Recharging...";

		foreach (var pad in LinkedPads)
		{
			if (GodotObject.IsInstanceValid(pad))
			{
				pad.TriggerSpikeSurge(SurgeDamage);
			}
		}

		var sound = GD.Load<AudioStream>("res://assets/sounds/click.mp3");
		if (sound != null) GameResourceCache.PlayPooledSound(GetTree(), sound, "SFX");
	}

	public Vector2 GetInteractionPosition() => GlobalPosition;

	public void ShowPrompt(bool show)
	{
		if (_promptLabel != null)
		{
			_promptLabel.Visible = show;
		}
	}
}
