using Godot;

/// <summary>
/// Shared base class for magnetizing dungeon pickups (coins, mana crystals, etc.).
/// </summary>
public abstract partial class PickupItem : Node2D
{
	private bool _isBeingPickedUp = false;
	private bool _isAnimatingSpawn = true;
	private Node2D _targetPlayer;
	private Area2D _area;
	private float _speed = 200.0f;
	private float _pickupDelay = 0.15f;

	protected abstract string LightName { get; }
	protected abstract Color LightColor { get; }
	protected abstract Color GlowInnerColor { get; }
	protected abstract Color GlowMidColor { get; }
	protected abstract string VisualNodeName { get; }
	protected virtual float GlowEnergy => 0.85f;
	protected virtual int GlowTextureSize => 64;

	protected abstract AudioStream GetPickupSound();
	protected abstract void ApplyPickupEffect(Player player);

	public override void _Ready()
	{
		Scale = Vector2.One;
		_area = GetNodeOrNull<Area2D>("Area2D");
		if (_area != null)
		{
			_area.AreaEntered += _on_area_2d_area_entered;
		}

		var visual = GetNodeOrNull<CanvasItem>(VisualNodeName);
		if (visual != null)
		{
			if (visual is AnimatedSprite2D animSprite)
			{
				animSprite.Play("idle");
			}
			visual.Material = GameResourceCache.GetUnshadedMaterial();
		}

		SetupGlow();
		PlaySpawnAnimation();
	}

	private void SetupGlow()
	{
		var glowTex = GameResourceCache.GetRadialGlowTexture(
			GlowInnerColor,
			GlowMidColor,
			0.45f,
			GlowTextureSize);

		GameResourceCache.CreateGlowLight(
			this,
			LightName,
			Vector2.Zero,
			LightColor,
			GlowEnergy,
			glowTex,
			shadowEnabled: false);
	}

	private bool _collected = false;
	private bool IsCoopSession => NetworkManager.Instance != null && NetworkManager.Instance.IsCoopSession;
	private bool IsServerAuthority => !IsCoopSession || (NetworkManager.Instance != null && NetworkManager.Instance.IsServerAuthority);

	private void PlaySpawnAnimation()
	{
		_isAnimatingSpawn = true;

		Vector2 startPos = GlobalPosition;
		Vector2 jumpPos = startPos + new Vector2(0, -20.0f);
		Vector2 landPos = startPos + new Vector2((float)GD.RandRange(-15.0, 15.0), 5.0f);

		var tween = CreateTween();
		tween.TweenProperty(this, "global_position", jumpPos, 0.25f)
			.SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
		tween.TweenProperty(this, "global_position", landPos, 0.25f)
			.SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);

		tween.Finished += () => _isAnimatingSpawn = false;
	}

	public override void _Process(double delta)
	{
		if (_isAnimatingSpawn) return;

		if (_pickupDelay > 0)
		{
			_pickupDelay -= (float)delta;
			return;
		}

		if (!_isBeingPickedUp)
		{
			TryDetectPlayerInZone();
		}

		if (_isBeingPickedUp)
		{
			bool targetInvalid = !IsInstanceValid(_targetPlayer) || (_targetPlayer is Player tp && tp.IsDead);
			if (targetInvalid)
			{
				if (!IsCoopSession)
				{
					_isBeingPickedUp = false;
					_targetPlayer = null;
					Scale = Vector2.One;
				}
				else if (IsServerAuthority)
				{
					Rpc(nameof(RpcCancelPickup));
				}
				return;
			}

			Vector2 direction = (_targetPlayer.GlobalPosition - GlobalPosition).Normalized();
			GlobalPosition += direction * _speed * (float)delta;
			_speed += 500.0f * (float)delta;

			if (IsCoopSession && !IsServerAuthority) return;

			if (GlobalPosition.DistanceTo(_targetPlayer.GlobalPosition) < 6.0f && !_collected)
			{
				_collected = true;
				if (_targetPlayer is Player player)
				{
					ApplyPickupEffect(player);
				}

				if (IsCoopSession)
				{
					Rpc(nameof(RpcFinishPickup));
				}
				else
				{
					FinishPickupLocal();
				}
			}
		}
	}

	private void TryDetectPlayerInZone()
	{
		if (IsCoopSession && !IsServerAuthority) return;

		if (_area != null && _area.Monitoring)
		{
			foreach (var overlapping in _area.GetOverlappingAreas())
			{
				if (overlapping.GetParent() is Player overlappingPlayer && !overlappingPlayer.IsDead)
				{
					StartPickup(overlappingPlayer);
					return;
				}
			}
		}

		if (IsCoopSession)
		{
			var nearbyCoop = NetworkManager.GetNearestAlivePlayer(GetTree(), GlobalPosition);
			if (nearbyCoop != null && GlobalPosition.DistanceTo(nearbyCoop.GlobalPosition) <= 32.0f)
			{
				StartPickup(nearbyCoop);
			}
			return;
		}

		var nearby = GetTree().GetFirstNodeInGroup("Player") as Node2D;
		if (nearby != null && GlobalPosition.DistanceTo(nearby.GlobalPosition) <= 32.0f)
		{
			StartPickup(nearby);
		}
	}

	private void _on_area_2d_area_entered(Area2D area)
	{
		if (IsCoopSession && !IsServerAuthority) return;
		if (!_isBeingPickedUp && !_isAnimatingSpawn && _pickupDelay <= 0 && area.GetParent() is Player player && !player.IsDead)
		{
			StartPickup(player);
		}
	}

	private void StartPickup(Node2D player)
	{
		if (IsCoopSession)
		{
			if (!IsServerAuthority) return;
			if (player is Player targetP && !targetP.IsDead)
			{
				Rpc(nameof(RpcBeginPickup), (long)targetP.PeerId);
			}
			return;
		}

		BeginPickupLocal(player);
	}

	private void BeginPickupLocal(Node2D player)
	{
		_isAnimatingSpawn = false;
		_targetPlayer = player;
		_isBeingPickedUp = true;

		var tween = CreateTween();
		tween.TweenProperty(this, "scale", Scale * 0.8f, 0.3f);
	}

	private void FinishPickupLocal()
	{
		if (GetParent() != null)
		{
			var sound = GetPickupSound();
			if (sound != null)
			{
				GameResourceCache.PlayPooledSound(GetTree(), sound, "SFX");
			}
		}
		QueueFree();
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RpcBeginPickup(long peerId)
	{
		int sender = Multiplayer.GetRemoteSenderId();
		if (IsCoopSession && sender != 0 && sender != 1) return;
		var target = NetworkManager.GetPlayerByPeerId(GetTree(), peerId);
		if (target == null) return;
		BeginPickupLocal(target);
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RpcCancelPickup()
	{
		int sender = Multiplayer.GetRemoteSenderId();
		if (IsCoopSession && sender != 0 && sender != 1) return;
		_isBeingPickedUp = false;
		_targetPlayer = null;
		_speed = 250.0f;
		Scale = Vector2.One;
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RpcFinishPickup()
	{
		int sender = Multiplayer.GetRemoteSenderId();
		if (IsCoopSession && sender != 0 && sender != 1) return;
		_collected = true;
		FinishPickupLocal();
	}
}
