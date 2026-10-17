using Godot;
using System;
using System.Collections.Generic;

public class ShopOffer
{
	public string Category;
	public string Title;
	public string Description;
	public int Cost;
	public Color AccentColor;
	public bool IsPurchased = false;
	public Func<Player, Level, SupplyAltar, string> OnPurchase;
}

public partial class SupplyAltar : Area2D, IInteractable
{
	private Label _promptLabel;
	private Sprite2D _sprite;
	private Texture2D _closedTex;
	private Texture2D _openTex;
	private Level _level;
	private SupplyShopOverlay _activeShop;

	public List<ShopOffer> RolledOffers { get; private set; } = new List<ShopOffer>();
	public bool HasPurchased => RolledOffers.Count > 0 && RolledOffers.TrueForAll(o => o.IsPurchased);

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

		// Золотое свечение вокруг магазина
		var glow = new PointLight2D();
		glow.Color = new Color(1.0f, 0.85f, 0.35f, 0.95f);
		glow.Energy = 1.0f;
		var grad = new Gradient();
		grad.Colors = new Color[]
		{
			new Color(1f, 0.9f, 0.4f, 0.95f),
			new Color(0f, 0f, 0f, 0f)
		};
		grad.Offsets = new float[] { 0f, 1f };
		var gradTex = new GradientTexture2D();
		gradTex.Gradient = grad;
		gradTex.Fill = GradientTexture2D.FillEnum.Radial;
		gradTex.FillFrom = new Vector2(0.5f, 0.5f);
		gradTex.FillTo = new Vector2(0.85f, 0.85f);
		gradTex.Width = 72;
		gradTex.Height = 72;
		glow.Texture = gradTex;
		AddChild(glow);

		_promptLabel = new Label();
		_promptLabel.Text = "Shop [E]";
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

		RollThreeRandomOffers();

		AreaEntered += OnAreaEntered;
		AreaExited += OnAreaExited;
	}

	private void RollThreeRandomOffers()
	{
		string rolledWeaponPath = AllShopWeapons[GD.Randi() % AllShopWeapons.Length];
		string weaponDisplayName = System.IO.Path.GetFileNameWithoutExtension(rolledWeaponPath).Replace("_", " ").ToUpper();

		var pool = new List<ShopOffer>
		{
			new ShopOffer
			{
				Category = "PERMANENT STAT",
				Title = "RING OF REGENERATION",
				Description = "+1 HP Regen Forever (during waves)",
				Cost = 5,
				AccentColor = new Color(0.35f, 0.95f, 0.45f),
				OnPurchase = (p, lvl, alt) =>
				{
					p.BonusHealthRegen += 1.0f;
					return $"Permanent +1 HP Regen! (Now {p.HealthRegenEffective:0.#}/s)";
				}
			},
			new ShopOffer
			{
				Category = "PERMANENT STAT",
				Title = "ARCANE MANA VESSEL",
				Description = "+50 Max Mana Forever & restores +50 Mana",
				Cost = 4,
				AccentColor = new Color(0.35f, 0.72f, 1.0f),
				OnPurchase = (p, lvl, alt) =>
				{
					p.BonusMaxMana += 50.0f;
					p.CurrentMana = Mathf.Min(p.MaxManaEffective, p.CurrentMana + 50.0f);
					return $"Permanent +50 Max Mana! (Now {(int)p.MaxManaEffective} MP)";
				}
			},
			new ShopOffer
			{
				Category = "PERMANENT STAT",
				Title = "TITAN HEARTSTONE",
				Description = "+25 Max HP Forever & heals +25 HP",
				Cost = 4,
				AccentColor = new Color(1.0f, 0.42f, 0.42f),
				OnPurchase = (p, lvl, alt) =>
				{
					p.BonusMaxHealth += 25.0f;
					p.CurrentHealth = Mathf.Min(p.MaxHealthEffective, p.CurrentHealth + 25.0f);
					return $"Permanent +25 Max HP! (Now {(int)p.MaxHealthEffective} HP)";
				}
			},
			new ShopOffer
			{
				Category = "PERMANENT STAT",
				Title = "AETHER SPRING",
				Description = "+2 Mana Regen Forever (during waves)",
				Cost = 5,
				AccentColor = new Color(0.45f, 0.85f, 1.0f),
				OnPurchase = (p, lvl, alt) =>
				{
					p.BonusManaRegen += 2.0f;
					return $"Permanent +2 Mana Regen! (Now {p.ManaRegenEffective:0.#}/s)";
				}
			},
			new ShopOffer
			{
				Category = "PERMANENT STAT",
				Title = "WINDRUNNER GREAVES",
				Description = "+10 Move Speed Forever",
				Cost = 5,
				AccentColor = new Color(0.55f, 1.0f, 0.8f),
				OnPurchase = (p, lvl, alt) =>
				{
					p.BonusMoveSpeed += 10.0f;
					return "Permanent +10 Move Speed!";
				}
			},
			new ShopOffer
			{
				Category = "ITEM UPGRADE",
				Title = "MASTERWORK WHETSTONE",
				Description = "+25% All Weapon Damage Forever",
				Cost = 6,
				AccentColor = new Color(1.0f, 0.72f, 0.25f),
				OnPurchase = (p, lvl, alt) =>
				{
					p.WeaponDamageMultiplier += 0.25f;
					return $"Weapon Damage increased to {(int)Mathf.Round(p.WeaponDamageMultiplier * 100)}%!";
				}
			},
			new ShopOffer
			{
				Category = "HERO ABILITY",
				Title = "TOME OF MASTERY",
				Description = "+1 Skill Point to upgrade any hero ability",
				Cost = 7,
				AccentColor = new Color(0.82f, 0.52f, 1.0f),
				OnPurchase = (p, lvl, alt) =>
				{
					p.SkillPoints++;
					var hud = GetTree().GetFirstNodeInGroup("HUD") as HUD;
					hud?.UpdateAbilitiesUI();
					return "Gained +1 Skill Point!";
				}
			},
			new ShopOffer
			{
				Category = "HERO ABILITY",
				Title = "GRIMOIRE OF FLAME [Q]",
				Description = "+1 Level to Ability 1 (Fireball)",
				Cost = 7,
				AccentColor = new Color(1.0f, 0.52f, 0.22f),
				OnPurchase = (p, lvl, alt) =>
				{
					p.Ability1Level++;
					var hud = GetTree().GetFirstNodeInGroup("HUD") as HUD;
					hud?.UpdateAbilitiesUI();
					return $"Ability 1 upgraded to Level {p.Ability1Level}!";
				}
			},
			new ShopOffer
			{
				Category = "HERO ABILITY",
				Title = "RUNE OF THE AEGIS [V]",
				Description = "+1 Level to Ability 2 (Arcane Shield & Dash)",
				Cost = 7,
				AccentColor = new Color(0.35f, 0.88f, 1.0f),
				OnPurchase = (p, lvl, alt) =>
				{
					p.Ability2Level++;
					var hud = GetTree().GetFirstNodeInGroup("HUD") as HUD;
					hud?.UpdateAbilitiesUI();
					return $"Ability 2 upgraded to Level {p.Ability2Level}!";
				}
			},
			new ShopOffer
			{
				Category = "WEAPON",
				Title = $"ARMORY: {weaponDisplayName}",
				Description = $"Spawns {weaponDisplayName} next to the shop",
				Cost = 6,
				AccentColor = new Color(1.0f, 0.88f, 0.32f),
				OnPurchase = (p, lvl, alt) =>
				{
					Vector2 dropPos = alt != null ? alt.GlobalPosition + new Vector2(0, 42) : p.GlobalPosition;
					lvl?.SpawnWeaponPickupAt(dropPos, rolledWeaponPath);
					return $"Dropped {weaponDisplayName} next to the shop!";
				}
			}
		};

		// Перемешиваем пул и выбираем ровно 3 случайных товара на эту волну
		RolledOffers.Clear();
		for (int i = 0; i < 3 && pool.Count > 0; i++)
		{
			int idx = (int)(GD.Randi() % (uint)pool.Count);
			RolledOffers.Add(pool[idx]);
			pool.RemoveAt(idx);
		}
	}

	public void UpdatePromptText()
	{
		if (_promptLabel != null)
		{
			_promptLabel.Text = HasPurchased ? "Shop [Sold Out]" : "Shop [E]";
		}
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
			UpdatePromptText();
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
	private readonly List<(Button btn, ShopOffer offer, StyleBoxFlat normalStyle, StyleBoxFlat purchasedStyle)> _offerButtons = new();

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
		bgOverlay.Color = new Color(0.0f, 0.0f, 0.0f, 0.76f);
		rootControl.AddChild(bgOverlay);
		bgOverlay.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

		var panel = new Panel();
		Vector2 panelSize = new Vector2(540, 360);
		panel.Size = panelSize;
		panel.Position = (rootControl.Size - panelSize) / 2.0f;
		rootControl.AddChild(panel);

		var panelStyle = new StyleBoxFlat();
		panelStyle.BgColor = new Color(0.08f, 0.08f, 0.11f, 0.97f);
		panelStyle.BorderWidthBottom = panelStyle.BorderWidthLeft = panelStyle.BorderWidthRight = panelStyle.BorderWidthTop = 2;
		panelStyle.BorderColor = new Color(0.88f, 0.70f, 0.22f);
		panel.AddThemeStyleboxOverride("panel", panelStyle);

		var vbox = new VBoxContainer();
		vbox.Position = new Vector2(22, 14);
		vbox.Size = new Vector2(panelSize.X - 44, panelSize.Y - 28);
		vbox.AddThemeConstantOverride("separation", 10);
		panel.AddChild(vbox);

		var font = GD.Load<Font>("res://assets/fonts/silver/Silver.ttf");

		var title = new Label();
		title.Text = "WAVE SHOP";
		title.HorizontalAlignment = HorizontalAlignment.Center;
		if (font != null) title.AddThemeFontOverride("font", font);
		title.AddThemeFontSizeOverride("font_size", 28);
		title.AddThemeColorOverride("font_color", new Color(1.0f, 0.86f, 0.35f));
		vbox.AddChild(title);

		_goldLabel = new Label();
		_goldLabel.HorizontalAlignment = HorizontalAlignment.Center;
		if (font != null) _goldLabel.AddThemeFontOverride("font", font);
		_goldLabel.AddThemeFontSizeOverride("font_size", 18);
		_goldLabel.AddThemeColorOverride("font_color", new Color(1.0f, 0.95f, 0.6f));
		vbox.AddChild(_goldLabel);

		if (_altar != null && _altar.RolledOffers != null)
		{
			foreach (var offer in _altar.RolledOffers)
			{
				AddOfferButton(vbox, font, offer);
			}
		}

		_statusLabel = new Label();
		_statusLabel.Text = "Buy items to strengthen your hero. Purchased items stay greyed out.";
		_statusLabel.HorizontalAlignment = HorizontalAlignment.Center;
		if (font != null) _statusLabel.AddThemeFontOverride("font", font);
		_statusLabel.AddThemeFontSizeOverride("font_size", 16);
		_statusLabel.AddThemeColorOverride("font_color", new Color(0.55f, 0.95f, 0.55f));
		vbox.AddChild(_statusLabel);

		var closeBtn = new Button();
		closeBtn.Text = "CLOSE [ESC]";
		closeBtn.CustomMinimumSize = new Vector2(150, 32);
		if (font != null) closeBtn.AddThemeFontOverride("font", font);
		closeBtn.AddThemeFontSizeOverride("font_size", 18);
		closeBtn.Pressed += CloseShop;
		vbox.AddChild(closeBtn);

		RefreshUI();
		MainMenu.ConnectButtonSounds(this);
	}

	private void AddOfferButton(Control parent, Font font, ShopOffer offer)
	{
		var btn = new Button();
		btn.CustomMinimumSize = new Vector2(0, 56);
		if (font != null) btn.AddThemeFontOverride("font", font);
		btn.AddThemeFontSizeOverride("font_size", 17);

		var normalStyle = new StyleBoxFlat();
		normalStyle.BgColor = new Color(0.14f, 0.14f, 0.18f, 0.96f);
		normalStyle.BorderWidthBottom = normalStyle.BorderWidthLeft = normalStyle.BorderWidthRight = normalStyle.BorderWidthTop = 2;
		normalStyle.BorderColor = offer.AccentColor.Darkened(0.25f);

		var hoverStyle = normalStyle.Duplicate() as StyleBoxFlat;
		hoverStyle.BgColor = new Color(0.22f, 0.20f, 0.15f, 0.98f);
		hoverStyle.BorderColor = offer.AccentColor;

		var cantAffordStyle = normalStyle.Duplicate() as StyleBoxFlat;
		cantAffordStyle.BgColor = new Color(0.10f, 0.10f, 0.12f, 0.80f);
		cantAffordStyle.BorderColor = new Color(0.28f, 0.28f, 0.32f);

		var purchasedStyle = normalStyle.Duplicate() as StyleBoxFlat;
		purchasedStyle.BgColor = new Color(0.07f, 0.07f, 0.08f, 0.65f);
		purchasedStyle.BorderColor = new Color(0.20f, 0.20f, 0.22f, 0.7f);

		btn.AddThemeStyleboxOverride("normal", normalStyle);
		btn.AddThemeStyleboxOverride("hover", hoverStyle);
		btn.AddThemeStyleboxOverride("pressed", hoverStyle);
		btn.AddThemeStyleboxOverride("disabled", cantAffordStyle);

		btn.Pressed += () =>
		{
			if (_player == null || _altar == null || offer.IsPurchased) return;
			if (_player.CoinCount < offer.Cost) return;

			_player.CoinCount -= offer.Cost;
			offer.IsPurchased = true;
			_altar.UpdatePromptText();

			string msg = offer.OnPurchase != null ? offer.OnPurchase(_player, _level, _altar) : "Purchased!";
			if (_statusLabel != null) _statusLabel.Text = msg;

			var sound = GD.Load<AudioStream>("res://assets/sounds/buff_collecting.wav");
			if (sound != null) MainMenu.PlayUISound(sound);

			RefreshUI();
		};

		parent.AddChild(btn);
		_offerButtons.Add((btn, offer, cantAffordStyle, purchasedStyle));
	}

	private void RefreshUI()
	{
		if (_player == null) return;

		if (_goldLabel != null)
		{
			_goldLabel.Text = $"GOLD: {_player.CoinCount}   |   HP: {(int)_player.CurrentHealth}/{(int)_player.MaxHealthEffective} (Regen {_player.HealthRegenEffective:0.#}/s)   |   MP: {(int)_player.CurrentMana}/{(int)_player.MaxManaEffective}";
		}

		foreach (var (btn, offer, cantAffordStyle, purchasedStyle) in _offerButtons)
		{
			if (offer.IsPurchased)
			{
				// Купленный товар становится серым и неактивным, остальные остаются на своих местах
				btn.Text = $"[SOLD] {offer.Title}\n{offer.Description}";
				btn.Disabled = true;
				btn.AddThemeStyleboxOverride("disabled", purchasedStyle);
				btn.AddThemeColorOverride("font_disabled_color", new Color(0.42f, 0.42f, 0.46f));
			}
			else
			{
				btn.Text = $"[{offer.Category}] {offer.Title}  —  {offer.Cost} GOLD\n{offer.Description}";
				btn.Disabled = _player.CoinCount < offer.Cost;
				btn.AddThemeStyleboxOverride("disabled", cantAffordStyle);
				btn.AddThemeColorOverride("font_disabled_color", new Color(0.62f, 0.62f, 0.66f));
			}
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
