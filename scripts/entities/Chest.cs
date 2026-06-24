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
			_promptLabel.Text = "Press E to open";
			_promptLabel.HorizontalAlignment = HorizontalAlignment.Center;
			_promptLabel.Position = new Vector2(-50, -35);
			_promptLabel.Size = new Vector2(100, 20);
			_promptLabel.Visible = false;

			var font = GD.Load<Font>("res://assets/fonts/silver/Silver.ttf");
			if (font != null)
			{
				_promptLabel.AddThemeFontOverride("font", font);
				_promptLabel.AddThemeFontSizeOverride("font_size", 8);
			}
			_promptLabel.AddThemeConstantOverride("letter_spacing", 2);
			AddChild(_promptLabel);
		}
		else
		{
			_promptLabel.AddThemeConstantOverride("letter_spacing", 2);
			_promptLabel.Position = new Vector2(-50, -35);
		}

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

	public void Interact(Player player)
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

		player.UnregisterInteractable(this);

		// Spawn weapon
		SpawnWeaponReward();
	}

	private void SpawnWeaponReward()
	{
		var pickupPrefab = GD.Load<PackedScene>("res://scene/weapons/weapon_pickup.tscn");
		if (pickupPrefab == null) return;

		var pickup = pickupPrefab.Instantiate() as WeaponPickup;
		if (pickup == null) return;

		string weaponPath = "res://data/waepons/revolver.tres";
		if (OpenedChestsCount >= 2)
		{
			weaponPath = "res://data/waepons/shotgun.tres";
		}

		pickup.WeaponToGive = GD.Load<WeaponData>(weaponPath);
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
		if (player == null && node != null && node.IsInGroup("Player"))
		{
			player = GetTree().GetFirstNodeInGroup("Player") as Player;
		}

		if (player != null)
		{
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
