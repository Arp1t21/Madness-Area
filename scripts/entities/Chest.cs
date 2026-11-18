using Godot;
using System;
using System.Linq;

public partial class Chest : StaticBody2D, IInteractable
{
	[Export] public Texture2D ClosedTexture;
	[Export] public Texture2D OpenTexture;

	private Sprite2D _sprite;
	private Area2D _interactArea;
	private Label _promptLabel;
	private bool _playerInRange = false;
	private bool _isOpen = false;

	public static int OpenedChestsCount = 0;

	public override void _Ready()
	{
		AddToGroup("Chest");

		_sprite = GetNodeOrNull<Sprite2D>("Sprite2D") ??
				  GetChildren().Cast<Node>().OfType<Sprite2D>().FirstOrDefault();

		_interactArea = GetNodeOrNull<Area2D>("Area2D") ??
						GetNodeOrNull<Area2D>("InteractArea") ??
						GetChildren().Cast<Node>().OfType<Area2D>().FirstOrDefault();

		_promptLabel = GetNodeOrNull<Label>("PromptLabel") ??
					   GetChildren().Cast<Node>().OfType<Label>().FirstOrDefault();

		if (_promptLabel == null)
		{
			_promptLabel = new Label();
			AddChild(_promptLabel);
		}
		_promptLabel.Text = "[E] Open Chest";
		_promptLabel.Visible = false;
		DungeonVisuals.StyleWorldPromptLabel(_promptLabel, DungeonVisuals.BrassGold, -25.0f);

		if (_sprite != null && ClosedTexture != null)
		{
			_sprite.Texture = ClosedTexture;
		}

		if (_interactArea != null)
		{
			_interactArea.CollisionMask = 16; // Player interaction layer
			_interactArea.BodyEntered += OnBodyEntered;
			_interactArea.BodyExited += OnBodyExited;
			_interactArea.AreaEntered += OnAreaEntered;
			_interactArea.AreaExited += OnAreaExited;
		}
	}

	private bool IsCoopSession => NetworkManager.Instance != null && NetworkManager.Instance.IsCoopSession;
	private bool IsServerAuthority => !IsCoopSession || (NetworkManager.Instance != null && NetworkManager.Instance.IsServerAuthority);

	public void Interact(Player player)
	{
		if (_isOpen) return;

		if (IsCoopSession)
		{
			if (player == null || !player.IsLocalPlayer || player.IsDead) return;
			if (!IsServerAuthority)
			{
				RpcId(1, nameof(RpcRequestOpenChest));
				return;
			}
			OpenChestOnServer();
			return;
		}

		ApplyChestOpenState(player);
		SpawnWeaponReward();
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RpcRequestOpenChest()
	{
		if (!IsServerAuthority || _isOpen) return;
		OpenChestOnServer();
	}

	private void OpenChestOnServer()
	{
		if (_isOpen) return;
		Rpc(nameof(RpcOpenChestVisuals));
		SpawnWeaponReward();
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RpcOpenChestVisuals()
	{
		int sender = Multiplayer.GetRemoteSenderId();
		if (IsCoopSession && sender != 0 && sender != 1) return;
		ApplyChestOpenState(NetworkManager.GetLocalPlayer(GetTree()));
	}

	private void ApplyChestOpenState(Player playerToUnregister)
	{
		if (_isOpen) return;
		_isOpen = true;
		OpenedChestsCount++;

		if (_sprite != null && OpenTexture != null)
		{
			_sprite.Texture = OpenTexture;
		}

		if (_promptLabel != null)
		{
			_promptLabel.Visible = false;
		}

		// Disable interaction area
		if (_interactArea != null)
		{
			foreach (var child in _interactArea.GetChildren())
			{
				if (child is CollisionShape2D cs)
				{
					cs.SetDeferred("disabled", true);
				}
			}
		}

		playerToUnregister?.UnregisterInteractable(this);
	}

	private string PickChestWeaponPath()
	{
		string weaponPath = "res://data/waepons/revolver.tres";
		if (OpenedChestsCount >= 2)
		{
			string[] chestPool = new[]
			{
				"res://data/waepons/shotgun.tres",
				"res://data/waepons/laser_cannon.tres",
				"res://data/waepons/katana.tres",
				"res://data/waepons/magic_staff.tres"
			};
			weaponPath = chestPool[GD.Randi() % chestPool.Length];
		}
		return weaponPath;
	}

	private void SpawnWeaponReward()
	{
		string weaponPath = PickChestWeaponPath();
		if (IsCoopSession)
		{
			(GetTree().GetFirstNodeInGroup("Level") as Level)?.SpawnWeaponPickupAt(GlobalPosition + new Vector2(0, 10), weaponPath);
			return;
		}

		var pickupPrefab = GD.Load<PackedScene>("res://scene/weapons/weapon_pickup.tscn");
		if (pickupPrefab == null) return;

		var pickup = pickupPrefab.Instantiate() as WeaponPickup;
		if (pickup == null) return;

		pickup.WeaponToGive = GameDataRegistry.LoadFreshWeapon(weaponPath) ?? GD.Load<WeaponData>(weaponPath);
		pickup.GlobalPosition = GlobalPosition + new Vector2(0, 10);
		
		// Add child to the level node
		var level = GetTree().GetFirstNodeInGroup("Level");
		if (level != null)
		{
			level.AddChild(pickup);
		}
		else
		{
			GetParent().AddChild(pickup);
		}
	}

	public Vector2 GetInteractionPosition()
	{
		return GlobalPosition;
	}

	public void ShowPrompt(bool show)
	{
		if (_isOpen) return;
		if (_promptLabel != null)
		{
			_promptLabel.Visible = show;
		}
	}

	private void OnBodyEntered(Node2D body) { CheckPlayer(body, true); }
	private void OnBodyExited(Node2D body) { CheckPlayer(body, false); }
	private void OnAreaEntered(Area2D area) { CheckPlayer(area.GetParent(), true); }
	private void OnAreaExited(Area2D area) { CheckPlayer(area.GetParent(), false); }

	private void CheckPlayer(Node node, bool entered)
	{
		if (_isOpen) return;
		Player player = node as Player;
		if (player == null && node != null && node.IsInGroup("Player") && !IsCoopSession)
		{
			player = GetTree().GetFirstNodeInGroup("Player") as Player;
		}

		if (player != null)
		{
			if (IsCoopSession && (!player.IsLocalPlayer || player.IsDead)) return;
			_playerInRange = entered;
			if (entered)
			{
				player.RegisterInteractable(this);
			}
			else
			{
				player.UnregisterInteractable(this);
			}
		}
	}
}
