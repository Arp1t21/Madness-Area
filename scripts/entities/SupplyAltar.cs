using Godot;
using System;
using System.Collections.Generic;

public partial class SupplyAltar : Area2D, IInteractable
{
	private Label _promptLabel;
	private Sprite2D _sprite;
	private Texture2D _closedTex;
	private Texture2D _openTex;
	private Level _level;
	private SupplyShopOverlay _activeShop;

	public void Setup(Level level)
	{
		_level = level;
		Name = "SupplyAltar";
		Scale = new Vector2(3.0f, 3.0f);
		CollisionLayer = 0;
		CollisionMask = 16; // Слой 5 (Area2DPickWeapon игрока)

		var collision = new CollisionShape2D();
		var circle = new CircleShape2D();
		circle.Radius = 18.0f;
		collision.Shape = circle;
		AddChild(collision);

		_closedTex = GD.Load<Texture2D>("res://assets/objects/closed_chest.png");
		_openTex = GD.Load<Texture2D>("res://assets/objects/open_chest.png");

		_sprite = new Sprite2D();
		_sprite.Texture = _closedTex;
		_sprite.Position = new Vector2(0, -6);
		AddChild(_sprite);

		// Золотое свечение вокруг сундука снабжения
		var glow = new PointLight2D();
		glow.Color = new Color(1.0f, 0.85f, 0.35f, 0.9f);
		glow.Energy = 0.8f;
		var grad = new Gradient();
		grad.Colors = new Color[]
		{
			new Color(1f, 0.9f, 0.4f, 0.9f),
			new Color(0f, 0f, 0f, 0f)
		};
		grad.Offsets = new float[] { 0f, 1f };
		var gradTex = new GradientTexture2D();
		gradTex.Gradient = grad;
		gradTex.Fill = GradientTexture2D.FillEnum.Radial;
		gradTex.FillFrom = new Vector2(0.5f, 0.5f);
		gradTex.FillTo = new Vector2(0.85f, 0.85f);
		gradTex.Width = 64;
		gradTex.Height = 64;
		glow.Texture = gradTex;
		AddChild(glow);

		_promptLabel = new Label();
		_promptLabel.Text = "Supply Shop [E]";
		_promptLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_promptLabel.Position = new Vector2(-45, -28);
		_promptLabel.Size = new Vector2(90, 15);
		_promptLabel.AddThemeColorOverride("font_color", new Color(1.0f, 0.88f, 0.35f));

		var font = GD.Load<Font>("res://assets/fonts/silver/Silver.ttf");
		if (font != null)
		{
			_promptLabel.AddThemeFontOverride("font", font);
			_promptLabel.AddThemeFontSizeOverride("font_size", 8);
		}
		_promptLabel.AddThemeConstantOverride("letter_spacing", 1);
		_promptLabel.Visible = false;
		AddChild(_promptLabel);

		AreaEntered += OnAreaEntered;
		AreaExited += OnAreaExited;
	}

	public void Interact(Player player)
	{
		if (_activeShop != null && GodotObject.IsInstanceValid(_activeShop)) return;

		if (_sprite != null && _openTex != null)
		{
			_sprite.Texture = _openTex;
		}

		GetTree().Paused = true;
		_activeShop = new SupplyShopOverlay(player, _level, this);
		GetTree().Root.AddChild(_activeShop);
	}

	public Vector2 GetInteractionPosition()
	{
		return GlobalPosition;
	}

	public void ShowPrompt(bool show)
	{
		if (_promptLabel != null)
		{
			_promptLabel.Visible = show;
		}
	}

	private void OnAreaEntered(Area2D area)
	{
		if (area.GetParent() is Player player)
		{
			player.RegisterInteractable(this);
		}
	}

	private void OnAreaExited(Area2D area)
	{
		if (area.GetParent() is Player player)
		{
			player.UnregisterInteractable(this);
		}
	}

	public override void _ExitTree()
	{
		if (_activeShop != null && GodotObject.IsInstanceValid(_activeShop))
		{
			_activeShop.QueueFree();
		}
	}
}

public partial class SupplyShopOverlay : CanvasLayer
{
	private readonly Player _player;
	private readonly Level _level;
	private readonly SupplyAltar _altar;
	private Label _goldLabel;
	private Label _statusLabel;
	private readonly List<(Button btn, int cost, Func<string> labelGen)> _shopButtons = new();

	private static readonly string[] AllShopWeapons = new[]
	{
		"res://data/waepons/katana.tres",
		"res://data/waepons/double_axe.tres",
		"res://data/waepons/auto_rifle.tres",
		"res://data/waepons/shotgun.tres",
		"res://data/waepons/revolver.tres",
		"res://data/waepons/dagger.tres",
		"res://data/waepons/magic_staff.tres"
	};

	public SupplyShopOverlay(Player player, Level level, SupplyAltar altar)
	{
		_player = player;
		_level = level;
		_altar = altar;
	}

	public override void _Ready()
	{
		ProcessMode = ProcessModeEnum.Always;
		Layer = 20;
		SettingsManager.ApplyFontQualityFixes();

		var rootControl = new Control();
		AddChild(rootControl);
		var viewportSize = GetViewport().GetVisibleRect().Size;
		float uiScale = Mathf.Clamp(viewportSize.X / 960.0f, 1.0f, 3.0f);
		rootControl.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
		rootControl.Size = viewportSize / uiScale;
		rootControl.Scale = new Vector2(uiScale, uiScale);

		var bgOverlay = new ColorRect();
		bgOverlay.Color = new Color(0.0f, 0.0f, 0.0f, 0.72f);
		rootControl.AddChild(bgOverlay);
		bgOverlay.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

		var panel = new Panel();
		Vector2 panelSize = new Vector2(460, 340);
		panel.Size = panelSize;
		panel.Position = (rootControl.Size - panelSize) / 2.0f;
		rootControl.AddChild(panel);

		var panelStyle = new StyleBoxFlat();
		panelStyle.BgColor = new Color(0.08f, 0.08f, 0.1f, 0.96f);
		panelStyle.BorderWidthBottom = panelStyle.BorderWidthLeft = panelStyle.BorderWidthRight = panelStyle.BorderWidthTop = 2;
		panelStyle.BorderColor = new Color(0.85f, 0.68f, 0.2f);
		panel.AddThemeStyleboxOverride("panel", panelStyle);

		var vbox = new VBoxContainer();
		vbox.Position = new Vector2(20, 14);
		vbox.Size = new Vector2(panelSize.X - 40, panelSize.Y - 28);
		vbox.AddThemeConstantOverride("separation", 8);
		panel.AddChild(vbox);

		var font = GD.Load<Font>("res://assets/fonts/silver/Silver.ttf");

		var title = new Label();
		title.Text = "SUPPLY ALTAR";
		title.HorizontalAlignment = HorizontalAlignment.Center;
		title.AddThemeFontOverride("font", font);
		title.AddThemeFontSizeOverride("font_size", 28);
		title.AddThemeColorOverride("font_color", new Color(1.0f, 0.86f, 0.35f));
		vbox.AddChild(title);

		_goldLabel = new Label();
		_goldLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_goldLabel.AddThemeFontOverride("font", font);
		_goldLabel.AddThemeFontSizeOverride("font_size", 18);
		_goldLabel.AddThemeColorOverride("font_color", new Color(1.0f, 0.95f, 0.6f));
		vbox.AddChild(_goldLabel);

		AddShopItem(vbox, font, 3,
			() => $"HEAL POTION (+25 HP, +10 MAX HP)  —  3 GOLD",
			() =>
			{
				_player.BonusMaxHealth += 10.0f;
				_player.CurrentHealth = Mathf.Min(_player.MaxHealthEffective, _player.CurrentHealth + 25.0f);
				return "Restored +25 HP & +10 Max HP!";
			});

		AddShopItem(vbox, font, 3,
			() => $"MANA ELIXIR (+50 MP, +20 MAX MP)  —  3 GOLD",
			() =>
			{
				_player.BonusMaxMana += 20.0f;
				_player.CurrentMana = Mathf.Min(_player.MaxManaEffective, _player.CurrentMana + 50.0f);
				return "Restored +50 Mana & +20 Max Mana!";
			});

		AddShopItem(vbox, font, 5,
			() => $"SHARPEN WEAPONS (+20% DMG, NOW {(int)Mathf.Round(_player.WeaponDamageMultiplier * 100)}%)  —  5 GOLD",
			() =>
			{
				_player.WeaponDamageMultiplier += 0.20f;
				return $"Weapon damage increased to {(int)Mathf.Round(_player.WeaponDamageMultiplier * 100)}%!";
			});

		AddShopItem(vbox, font, 6,
			() => "MYSTERY WEAPON CRATE (RANDOM WEAPON)  —  6 GOLD",
			() =>
			{
				string chosen = PickRandomShopWeapon();
				Vector2 dropPos = _altar != null ? _altar.GlobalPosition + new Vector2(0, 42) : _player.GlobalPosition;
				_level?.SpawnWeaponPickupAt(dropPos, chosen);
				string wName = System.IO.Path.GetFileNameWithoutExtension(chosen).Replace("_", " ").ToUpper();
				return $"Dropped {wName} next to the altar!";
			});

		AddShopItem(vbox, font, 8,
			() => $"ANCIENT TOME (+1 SKILL POINT)  —  8 GOLD",
			() =>
			{
				_player.SkillPoints++;
				var hud = GetTree().GetFirstNodeInGroup("HUD") as HUD;
				hud?.UpdateAbilitiesUI();
				return "Gained +1 Skill Point!";
			});

		_statusLabel = new Label();
		_statusLabel.Text = "Spend your coins to prepare for the next floor.";
		_statusLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_statusLabel.AddThemeFontOverride("font", font);
		_statusLabel.AddThemeFontSizeOverride("font_size", 15);
		_statusLabel.AddThemeColorOverride("font_color", new Color(0.55f, 0.95f, 0.55f));
		vbox.AddChild(_statusLabel);

		var closeBtn = new Button();
		closeBtn.Text = "CLOSE [ESC]";
		closeBtn.CustomMinimumSize = new Vector2(140, 28);
		closeBtn.AddThemeFontOverride("font", font);
		closeBtn.AddThemeFontSizeOverride("font_size", 18);
		closeBtn.Pressed += CloseShop;
		vbox.AddChild(closeBtn);

		RefreshUI();
		MainMenu.ConnectButtonSounds(this);
	}

	private void AddShopItem(Control parent, Font font, int cost, Func<string> labelGen, Func<string> onBuy)
	{
		var btn = new Button();
		btn.CustomMinimumSize = new Vector2(0, 32);
		btn.AddThemeFontOverride("font", font);
		btn.AddThemeFontSizeOverride("font_size", 16);

		var normalStyle = new StyleBoxFlat();
		normalStyle.BgColor = new Color(0.15f, 0.15f, 0.18f, 0.95f);
		normalStyle.BorderWidthBottom = normalStyle.BorderWidthLeft = normalStyle.BorderWidthRight = normalStyle.BorderWidthTop = 1;
		normalStyle.BorderColor = new Color(0.45f, 0.45f, 0.5f);

		var hoverStyle = normalStyle.Duplicate() as StyleBoxFlat;
		hoverStyle.BgColor = new Color(0.24f, 0.22f, 0.16f, 0.98f);
		hoverStyle.BorderColor = new Color(0.95f, 0.78f, 0.25f);

		var disabledStyle = normalStyle.Duplicate() as StyleBoxFlat;
		disabledStyle.BgColor = new Color(0.09f, 0.09f, 0.1f, 0.7f);
		disabledStyle.BorderColor = new Color(0.25f, 0.25f, 0.25f);

		btn.AddThemeStyleboxOverride("normal", normalStyle);
		btn.AddThemeStyleboxOverride("hover", hoverStyle);
		btn.AddThemeStyleboxOverride("pressed", hoverStyle);
		btn.AddThemeStyleboxOverride("disabled", disabledStyle);

		btn.Pressed += () =>
		{
			if (_player == null || _player.CoinCount < cost) return;
			_player.CoinCount -= cost;
			string msg = onBuy();
			if (_statusLabel != null) _statusLabel.Text = msg;

			var sound = GD.Load<AudioStream>("res://assets/sounds/buff_collecting.wav");
			if (sound != null) MainMenu.PlayUISound(sound);

			RefreshUI();
		};

		parent.AddChild(btn);
		_shopButtons.Add((btn, cost, labelGen));
	}

	private string PickRandomShopWeapon()
	{
		var ownedPaths = new HashSet<string>();
		if (_player != null)
		{
			foreach (var slot in _player.WeaponSlots)
			{
				if (slot.CurrentWeapon != null && !string.IsNullOrEmpty(slot.CurrentWeapon.ResourcePath))
				{
					ownedPaths.Add(slot.CurrentWeapon.ResourcePath);
				}
			}
		}

		var candidates = new List<string>();
		foreach (var path in AllShopWeapons)
		{
			if (!ownedPaths.Contains(path))
			{
				candidates.Add(path);
			}
		}

		if (candidates.Count == 0)
		{
			candidates.AddRange(AllShopWeapons);
		}

		int idx = (int)(GD.Randi() % (uint)candidates.Count);
		return candidates[idx];
	}

	private void RefreshUI()
	{
		if (_player == null) return;

		if (_goldLabel != null)
		{
			_goldLabel.Text = $"YOUR GOLD: {_player.CoinCount}   |   HP: {(int)_player.CurrentHealth}/{(int)_player.MaxHealthEffective}   |   MP: {(int)_player.CurrentMana}/{(int)_player.MaxManaEffective}";
		}

		foreach (var item in _shopButtons)
		{
			item.btn.Text = item.labelGen();
			item.btn.Disabled = _player.CoinCount < item.cost;
		}
	}

	private void CloseShop()
	{
		GetTree().Paused = false;
		QueueFree();
	}

	public override void _Input(InputEvent @event)
	{
		if (@event.IsActionPressed("ui_cancel"))
		{
			CloseShop();
			GetViewport().SetInputAsHandled();
		}
	}
}
