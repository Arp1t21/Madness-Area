using Godot;
using System;
using System.Collections.Generic;

public class ShopOffer
{
	public string Id;
	public string Category;
	public string Title;
	public string Description;
	public int BaseCost;
	public Color AccentColor;
	public bool IsPurchased = false;
	public bool RequiresWeaponChoice = false;
	public Func<WeaponData, bool> IsWeaponCompatible = null;
	public Func<Player, Level, SupplyAltar, string> OnPurchase;
	public Func<Player, int, WeaponData, string> OnWeaponPurchase;

	public int GetEffectiveCost(Player player, int levelIndex)
	{
		int floorBonus = Mathf.Max(0, levelIndex - 1) * 2;
		int baseScaled = BaseCost + floorBonus;
		int boughtCount = player != null ? player.GetShopPurchaseCount(Id) : 0;
		float mult = Mathf.Pow(1.5f, boughtCount);
		return Mathf.Max(1, Mathf.RoundToInt(baseScaled * mult));
	}
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
		"res://data/waepons/greatsword.tres",
		"res://data/waepons/knight_sword.tres",
		"res://data/waepons/hunter_bow.tres",
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

		RollFourRandomOffers();

		AreaEntered += OnAreaEntered;
		AreaExited += OnAreaExited;
	}

	private void RollFourRandomOffers()
	{
		string rolledWeaponPath = AllShopWeapons[GD.Randi() % AllShopWeapons.Length];
		string weaponDisplayName = System.IO.Path.GetFileNameWithoutExtension(rolledWeaponPath).Replace("_", " ").ToUpper();

		var generalPool = new List<ShopOffer>
		{
			new ShopOffer
			{
				Id = "STAT_HP_REGEN",
				Category = "PERMANENT STAT",
				Title = "RING OF REGENERATION",
				Description = "+0.25 HP Regen Forever (during waves)",
				BaseCost = 11,
				AccentColor = new Color(0.35f, 0.95f, 0.45f),
				OnPurchase = (p, lvl, alt) =>
				{
					p.BonusHealthRegen += 0.25f;
					return $"Permanent +0.25 HP Regen! (Now {p.HealthRegenEffective:0.##}/s)";
				}
			},
			new ShopOffer
			{
				Id = "STAT_MAX_MANA",
				Category = "PERMANENT STAT",
				Title = "ARCANE MANA VESSEL",
				Description = "+50 Max Mana Forever & restores +50 Mana",
				BaseCost = 10,
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
				Id = "STAT_MAX_HP",
				Category = "PERMANENT STAT",
				Title = "TITAN HEARTSTONE",
				Description = "+25 Max HP Forever & heals +25 HP",
				BaseCost = 10,
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
				Id = "STAT_MANA_REGEN",
				Category = "PERMANENT STAT",
				Title = "AETHER SPRING",
				Description = "+2 Mana Regen Forever (during waves)",
				BaseCost = 11,
				AccentColor = new Color(0.45f, 0.85f, 1.0f),
				OnPurchase = (p, lvl, alt) =>
				{
					p.BonusManaRegen += 2.0f;
					return $"Permanent +2 Mana Regen! (Now {p.ManaRegenEffective:0.#}/s)";
				}
			},
			new ShopOffer
			{
				Id = "STAT_MOVE_SPEED",
				Category = "PERMANENT STAT",
				Title = "WINDRUNNER GREAVES",
				Description = "+10 Move Speed Forever",
				BaseCost = 12,
				AccentColor = new Color(0.55f, 1.0f, 0.8f),
				OnPurchase = (p, lvl, alt) =>
				{
					p.BonusMoveSpeed += 10.0f;
					return "Permanent +10 Move Speed!";
				}
			},
			new ShopOffer
			{
				Id = "GLOBAL_WEAPON_DMG",
				Category = "ITEM UPGRADE",
				Title = "MASTERWORK WHETSTONE",
				Description = "+15% All Weapon Damage Forever",
				BaseCost = 15,
				AccentColor = new Color(1.0f, 0.72f, 0.25f),
				OnPurchase = (p, lvl, alt) =>
				{
					p.WeaponDamageMultiplier += 0.15f;
					return $"All Weapon Damage increased to {(int)Mathf.Round(p.WeaponDamageMultiplier * 100)}%!";
				}
			},
			new ShopOffer
			{
				Id = "STAT_FURY_DRAUGHT",
				Category = "PERMANENT STAT",
				Title = "BERSERKER'S DRAUGHT",
				Description = "+10% All Weapon Damage & +6 Move Speed Forever",
				BaseCost = 14,
				AccentColor = new Color(1.0f, 0.48f, 0.28f),
				OnPurchase = (p, lvl, alt) =>
				{
					p.WeaponDamageMultiplier += 0.10f;
					p.BonusMoveSpeed += 6.0f;
					return $"All Damage {(int)Mathf.Round(p.WeaponDamageMultiplier * 100)}% & +6 Move Speed!";
				}
			},
			new ShopOffer
			{
				Id = "CONSUMABLE_HEAL_BOOST",
				Category = "PERMANENT STAT",
				Title = "CRIMSON ELIXIR",
				Description = "Heals 45% Max HP & grants +15 Max HP Forever",
				BaseCost = 11,
				AccentColor = new Color(0.95f, 0.30f, 0.45f),
				OnPurchase = (p, lvl, alt) =>
				{
					p.BonusMaxHealth += 15.0f;
					float heal = p.MaxHealthEffective * 0.45f;
					p.CurrentHealth = Mathf.Min(p.MaxHealthEffective, p.CurrentHealth + heal);
					return $"Healed {(int)heal} HP & gained +15 Max HP!";
				}
			},
			new ShopOffer
			{
				Id = "STAT_MANA_HARMONY",
				Category = "PERMANENT STAT",
				Title = "AETHERIC HARMONY",
				Description = "+30 Max Mana & +1 Mana Regen Forever",
				BaseCost = 12,
				AccentColor = new Color(0.42f, 0.78f, 1.0f),
				OnPurchase = (p, lvl, alt) =>
				{
					p.BonusMaxMana += 30.0f;
					p.BonusManaRegen += 1.0f;
					p.CurrentMana = Mathf.Min(p.MaxManaEffective, p.CurrentMana + 30.0f);
					return "+30 Max Mana & +1 Mana Regen Forever!";
				}
			},
			new ShopOffer
			{
				Id = "ABILITY_SKILL_POINT",
				Category = "HERO ABILITY",
				Title = "TOME OF MASTERY",
				Description = "+1 Skill Point to upgrade any hero ability",
				BaseCost = 16,
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
				Id = "ABILITY_1_LEVEL",
				Category = "HERO ABILITY",
				Title = "GRIMOIRE OF FLAME [Q]",
				Description = "+1 Level to Ability 1 (Fireball)",
				BaseCost = 16,
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
				Id = "ABILITY_2_LEVEL",
				Category = "HERO ABILITY",
				Title = "RUNE OF THE AEGIS [V]",
				Description = "+1 Level to Ability 2 (Arcane Shield & Dash)",
				BaseCost = 16,
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
				Id = $"ARMORY_{weaponDisplayName}",
				Category = "WEAPON",
				Title = $"ARMORY: {weaponDisplayName}",
				Description = $"Spawns {weaponDisplayName} next to the shop",
				BaseCost = 13,
				AccentColor = new Color(1.0f, 0.88f, 0.32f),
				OnPurchase = (p, lvl, alt) =>
				{
					Vector2 dropPos = alt != null ? alt.GlobalPosition + new Vector2(0, 42) : p.GlobalPosition;
					lvl?.SpawnWeaponPickupAt(dropPos, rolledWeaponPath);
					return $"Dropped {weaponDisplayName} next to the shop!";
				}
			}
		};

		var singleWeaponPool = new List<ShopOffer>
		{
			new ShopOffer
			{
				Id = "SINGLE_EXTENDED_MAG",
				Category = "SINGLE WEAPON",
				Title = "EXTENDED MAGAZINE",
				Description = "+5 Max Ammo in Magazine for 1 chosen weapon",
				BaseCost = 11,
				AccentColor = new Color(1.0f, 0.78f, 0.32f),
				RequiresWeaponChoice = true,
				IsWeaponCompatible = (w) => w != null && w.IsRanged && w.MaxAmmo < 500,
				OnWeaponPurchase = (p, slotIdx, w) =>
				{
					w.MaxAmmo += 5;
					p.WeaponSlots[slotIdx]?.AddBonusAmmo(5);
					return $"{w.WeaponName.ToUpper()}: +5 Max Ammo (Now {w.MaxAmmo})!";
				}
			},
			new ShopOffer
			{
				Id = "SINGLE_QUICKLOADER",
				Category = "SINGLE WEAPON",
				Title = "QUICKLOADER MECHANISM",
				Description = "-10% Reload & -10% Cooldown for 1 chosen weapon",
				BaseCost = 12,
				AccentColor = new Color(0.45f, 0.95f, 0.75f),
				RequiresWeaponChoice = true,
				OnWeaponPurchase = (p, slotIdx, w) =>
				{
					w.ReloadTime = Mathf.Max(0.15f, w.ReloadTime * 0.90f);
					w.AttackCooldown = Mathf.Max(0.05f, w.AttackCooldown * 0.90f);
					p.WeaponSlots[slotIdx]?.ApplyWeaponSettings();
					return $"{w.WeaponName.ToUpper()}: -10% Reload & Cooldown!";
				}
			},
			new ShopOffer
			{
				Id = "SINGLE_MANA_REDUCTION",
				Category = "SINGLE WEAPON",
				Title = "ARCANE CONDUCTOR",
				Description = "-20% Mana Cost for 1 chosen weapon",
				BaseCost = 11,
				AccentColor = new Color(0.42f, 0.75f, 1.0f),
				RequiresWeaponChoice = true,
				IsWeaponCompatible = (w) => w != null && w.ManaCost > 0.01f,
				OnWeaponPurchase = (p, slotIdx, w) =>
				{
					w.ManaCost = Mathf.Max(0.0f, w.ManaCost * 0.80f);
					return $"{w.WeaponName.ToUpper()}: Mana Cost reduced to {w.ManaCost:0.#}!";
				}
			},
			new ShopOffer
			{
				Id = "SINGLE_SHARPENED_CORE",
				Category = "SINGLE WEAPON",
				Title = "SHARPENED WEAPON CORE",
				Description = "+25% Base Damage for 1 chosen weapon",
				BaseCost = 13,
				AccentColor = new Color(1.0f, 0.55f, 0.35f),
				RequiresWeaponChoice = true,
				OnWeaponPurchase = (p, slotIdx, w) =>
				{
					int newDmg = Mathf.Max(w.Damage + 2, Mathf.RoundToInt(w.Damage * 1.25f));
					w.Damage = newDmg;
					return $"{w.WeaponName.ToUpper()}: Base Damage increased to {w.Damage}!";
				}
			},
			new ShopOffer
			{
				Id = "SINGLE_IMPACT_CORE",
				Category = "SINGLE WEAPON",
				Title = "HEAVY IMPACT CORE",
				Description = "+30% Knockback & -8% Cooldown for 1 chosen weapon",
				BaseCost = 11,
				AccentColor = new Color(0.95f, 0.65f, 0.95f),
				RequiresWeaponChoice = true,
				OnWeaponPurchase = (p, slotIdx, w) =>
				{
					w.KnockbackForce = Mathf.Round(w.KnockbackForce * 1.30f * 10f) / 10f;
					w.AttackCooldown = Mathf.Max(0.05f, w.AttackCooldown * 0.92f);
					p.WeaponSlots[slotIdx]?.ApplyWeaponSettings();
					return $"{w.WeaponName.ToUpper()}: +30% Knockback & -8% Cooldown!";
				}
			},
			new ShopOffer
			{
				Id = "SINGLE_FEATHER_GRIP",
				Category = "SINGLE WEAPON",
				Title = "FEATHERWEIGHT GRIP",
				Description = "-16% Attack Cooldown (faster attacks) for 1 chosen weapon",
				BaseCost = 13,
				AccentColor = new Color(0.55f, 1.0f, 0.85f),
				RequiresWeaponChoice = true,
				OnWeaponPurchase = (p, slotIdx, w) =>
				{
					w.AttackCooldown = Mathf.Max(0.05f, w.AttackCooldown * 0.84f);
					p.WeaponSlots[slotIdx]?.ApplyWeaponSettings();
					return $"{w.WeaponName.ToUpper()}: -16% Attack Cooldown!";
				}
			},
			new ShopOffer
			{
				Id = "SINGLE_PIERCING",
				Category = "SINGLE WEAPON",
				Title = "PIERCING ENCHANTMENT",
				Description = "Projectiles pierce enemies & +15% Damage for 1 ranged weapon",
				BaseCost = 14,
				AccentColor = new Color(0.45f, 1.0f, 0.65f),
				RequiresWeaponChoice = true,
				IsWeaponCompatible = (w) => w != null && w.IsRanged,
				OnWeaponPurchase = (p, slotIdx, w) =>
				{
					w.IsPiercing = true;
					w.Damage = Mathf.Max(w.Damage + 1, Mathf.RoundToInt(w.Damage * 1.15f));
					return $"{w.WeaponName.ToUpper()}: Projectiles now Pierce & Damage is {w.Damage}!";
				}
			},
			new ShopOffer
			{
				Id = "SINGLE_MULTISHOT",
				Category = "SINGLE WEAPON",
				Title = "SPLIT-BARREL CHAMBER",
				Description = "+1 Projectile per shot for 1 chosen ranged weapon",
				BaseCost = 16,
				AccentColor = new Color(1.0f, 0.68f, 0.25f),
				RequiresWeaponChoice = true,
				IsWeaponCompatible = (w) => w != null && w.IsRanged && w.BulletCount < 10,
				OnWeaponPurchase = (p, slotIdx, w) =>
				{
					w.BulletCount += 1;
					w.Spread = Mathf.Max(w.Spread, 0.08f);
					return $"{w.WeaponName.ToUpper()}: Now fires {w.BulletCount} projectiles per shot!";
				}
			},
			new ShopOffer
			{
				Id = "SINGLE_VELOCITY_CORE",
				Category = "SINGLE WEAPON",
				Title = "OVERCHARGED CAPACITOR",
				Description = "+25% Bullet Speed & +15% Base Damage for 1 ranged weapon",
				BaseCost = 13,
				AccentColor = new Color(0.45f, 0.88f, 1.0f),
				RequiresWeaponChoice = true,
				IsWeaponCompatible = (w) => w != null && w.IsRanged,
				OnWeaponPurchase = (p, slotIdx, w) =>
				{
					w.BulletSpeed = Mathf.Round(w.BulletSpeed * 1.25f);
					w.Damage = Mathf.Max(w.Damage + 1, Mathf.RoundToInt(w.Damage * 1.15f));
					return $"{w.WeaponName.ToUpper()}: +25% Bullet Speed & Damage is {w.Damage}!";
				}
			},
			new ShopOffer
			{
				Id = "SINGLE_CLEAVE_EDGE",
				Category = "SINGLE WEAPON",
				Title = "VAMPIRIC CLEAVE RUNES",
				Description = "Enables Multi-Hit cleave & +20% Damage for 1 melee weapon",
				BaseCost = 13,
				AccentColor = new Color(1.0f, 0.38f, 0.48f),
				RequiresWeaponChoice = true,
				IsWeaponCompatible = (w) => w != null && !w.IsRanged,
				OnWeaponPurchase = (p, slotIdx, w) =>
				{
					w.AllowMultiHit = true;
					w.Damage = Mathf.Max(w.Damage + 2, Mathf.RoundToInt(w.Damage * 1.20f));
					return $"{w.WeaponName.ToUpper()}: Multi-Hit Cleave & Damage increased to {w.Damage}!";
				}
			}
		};

		// Выбираем 4 предмета в магазин (гарантируем минимум 1 улучшение для одиночного оружия, остальные из объединенного пула)
		RolledOffers.Clear();

		int firstSingleIdx = (int)(GD.Randi() % (uint)singleWeaponPool.Count);
		RolledOffers.Add(singleWeaponPool[firstSingleIdx]);
		singleWeaponPool.RemoveAt(firstSingleIdx);

		var combinedPool = new List<ShopOffer>(generalPool);
		combinedPool.AddRange(singleWeaponPool);

		while (RolledOffers.Count < 4 && combinedPool.Count > 0)
		{
			int idx = (int)(GD.Randi() % (uint)combinedPool.Count);
			RolledOffers.Add(combinedPool[idx]);
			combinedPool.RemoveAt(idx);
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
	private Control _rootControl;
	private Font _font;
	private Label _goldLabel;
	private Label _statusLabel;
	private Control _weaponChoiceModal;
	private readonly List<(Button btn, ShopOffer offer, StyleBoxFlat cantAffordStyle, StyleBoxFlat purchasedStyle)> _offerButtons = new();

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

		_rootControl = new Control();
		AddChild(_rootControl);
		var viewportSize = GetViewport().GetVisibleRect().Size;
		float uiScale = Mathf.Clamp(viewportSize.X / 960.0f, 1.0f, 3.0f);
		_rootControl.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
		_rootControl.Size = viewportSize / uiScale;
		_rootControl.Scale = new Vector2(uiScale, uiScale);

		var bgOverlay = new ColorRect();
		bgOverlay.Color = new Color(0.0f, 0.0f, 0.0f, 0.76f);
		_rootControl.AddChild(bgOverlay);
		bgOverlay.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

		var panel = new Panel();
		Vector2 panelSize = new Vector2(560, 416);
		panel.Size = panelSize;
		panel.Position = (_rootControl.Size - panelSize) / 2.0f;
		_rootControl.AddChild(panel);

		var panelStyle = new StyleBoxFlat();
		panelStyle.BgColor = new Color(0.08f, 0.08f, 0.11f, 0.97f);
		panelStyle.BorderWidthBottom = panelStyle.BorderWidthLeft = panelStyle.BorderWidthRight = panelStyle.BorderWidthTop = 2;
		panelStyle.BorderColor = new Color(0.88f, 0.70f, 0.22f);
		panel.AddThemeStyleboxOverride("panel", panelStyle);

		var vbox = new VBoxContainer();
		vbox.Position = new Vector2(22, 12);
		vbox.Size = new Vector2(panelSize.X - 44, panelSize.Y - 24);
		vbox.AddThemeConstantOverride("separation", 8);
		panel.AddChild(vbox);

		_font = GD.Load<Font>("res://assets/fonts/silver/Silver.ttf");

		var title = new Label();
		title.Text = "WAVE SHOP";
		title.HorizontalAlignment = HorizontalAlignment.Center;
		if (_font != null) title.AddThemeFontOverride("font", _font);
		title.AddThemeFontSizeOverride("font_size", 26);
		title.AddThemeColorOverride("font_color", new Color(1.0f, 0.86f, 0.35f));
		vbox.AddChild(title);

		_goldLabel = new Label();
		_goldLabel.HorizontalAlignment = HorizontalAlignment.Center;
		if (_font != null) _goldLabel.AddThemeFontOverride("font", _font);
		_goldLabel.AddThemeFontSizeOverride("font_size", 17);
		_goldLabel.AddThemeColorOverride("font_color", new Color(1.0f, 0.95f, 0.6f));
		vbox.AddChild(_goldLabel);

		if (_altar != null && _altar.RolledOffers != null)
		{
			foreach (var offer in _altar.RolledOffers)
			{
				AddOfferButton(vbox, _font, offer);
			}
		}

		_statusLabel = new Label();
		_statusLabel.Text = "Buy items to strengthen your hero. Repeat purchases cost 1.5x more.";
		_statusLabel.HorizontalAlignment = HorizontalAlignment.Center;
		if (_font != null) _statusLabel.AddThemeFontOverride("font", _font);
		_statusLabel.AddThemeFontSizeOverride("font_size", 15);
		_statusLabel.AddThemeColorOverride("font_color", new Color(0.55f, 0.95f, 0.55f));
		vbox.AddChild(_statusLabel);

		var closeBtn = new Button();
		closeBtn.Text = "CLOSE [ESC]";
		closeBtn.CustomMinimumSize = new Vector2(150, 30);
		if (_font != null) closeBtn.AddThemeFontOverride("font", _font);
		closeBtn.AddThemeFontSizeOverride("font_size", 18);
		closeBtn.Pressed += CloseShop;
		vbox.AddChild(closeBtn);

		RefreshUI();
		MainMenu.ConnectButtonSounds(this);
	}

	private int GetCurrentLevelIndex()
	{
		return _level != null ? _level.CurrentLevelIndex : 1;
	}

	private void AddOfferButton(Control parent, Font font, ShopOffer offer)
	{
		var btn = new Button();
		btn.CustomMinimumSize = new Vector2(0, 52);
		if (font != null) btn.AddThemeFontOverride("font", font);
		btn.AddThemeFontSizeOverride("font_size", 16);

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
			int cost = offer.GetEffectiveCost(_player, GetCurrentLevelIndex());
			if (_player.CoinCount < cost) return;

			if (offer.RequiresWeaponChoice)
			{
				OpenWeaponSelectionModal(offer, cost);
				return;
			}

			CompleteOfferPurchase(offer, cost, -1);
		};

		parent.AddChild(btn);
		_offerButtons.Add((btn, offer, cantAffordStyle, purchasedStyle));
	}

	private void CompleteOfferPurchase(ShopOffer offer, int cost, int weaponSlotIndex)
	{
		if (_player == null || _altar == null || offer.IsPurchased) return;
		if (_player.CoinCount < cost) return;

		string msg;
		if (offer.RequiresWeaponChoice)
		{
			WeaponData uniqueWeapon = _player.EnsureUniqueWeaponInSlot(weaponSlotIndex);
			if (uniqueWeapon == null) return;
			msg = offer.OnWeaponPurchase != null
				? offer.OnWeaponPurchase(_player, weaponSlotIndex, uniqueWeapon)
				: "Weapon upgraded!";
		}
		else
		{
			msg = offer.OnPurchase != null ? offer.OnPurchase(_player, _level, _altar) : "Purchased!";
		}

		_player.CoinCount -= cost;
		_player.RecordShopPurchase(offer.Id);
		offer.IsPurchased = true;
		_altar.UpdatePromptText();

		if (_statusLabel != null) _statusLabel.Text = msg;

		var sound = GD.Load<AudioStream>("res://assets/sounds/buff_collecting.wav");
		if (sound != null) MainMenu.PlayUISound(sound);

		RefreshUI();
	}

	private void OpenWeaponSelectionModal(ShopOffer offer, int cost)
	{
		if (_weaponChoiceModal != null && GodotObject.IsInstanceValid(_weaponChoiceModal))
		{
			_weaponChoiceModal.QueueFree();
		}

		_weaponChoiceModal = new Control();
		_weaponChoiceModal.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		_rootControl.AddChild(_weaponChoiceModal);

		var dim = new ColorRect();
		dim.Color = new Color(0f, 0f, 0f, 0.72f);
		dim.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		_weaponChoiceModal.AddChild(dim);

		var modalPanel = new Panel();
		Vector2 modalSize = new Vector2(440, 290);
		modalPanel.Size = modalSize;
		modalPanel.Position = (_rootControl.Size - modalSize) / 2.0f;
		_weaponChoiceModal.AddChild(modalPanel);

		var style = new StyleBoxFlat
		{
			BgColor = new Color(0.09f, 0.09f, 0.13f, 0.99f),
			BorderColor = offer.AccentColor,
			BorderWidthLeft = 2,
			BorderWidthTop = 2,
			BorderWidthRight = 2,
			BorderWidthBottom = 2,
			CornerRadiusTopLeft = 4,
			CornerRadiusTopRight = 4,
			CornerRadiusBottomLeft = 4,
			CornerRadiusBottomRight = 4
		};
		modalPanel.AddThemeStyleboxOverride("panel", style);

		var vbox = new VBoxContainer();
		vbox.Position = new Vector2(18, 14);
		vbox.Size = new Vector2(modalSize.X - 36, modalSize.Y - 28);
		vbox.AddThemeConstantOverride("separation", 8);
		modalPanel.AddChild(vbox);

		var header = new Label();
		header.Text = $"SELECT WEAPON FOR: {offer.Title}";
		header.HorizontalAlignment = HorizontalAlignment.Center;
		if (_font != null) header.AddThemeFontOverride("font", _font);
		header.AddThemeFontSizeOverride("font_size", 20);
		header.AddThemeColorOverride("font_color", offer.AccentColor);
		vbox.AddChild(header);

		var subHeader = new Label();
		subHeader.Text = offer.Description;
		subHeader.HorizontalAlignment = HorizontalAlignment.Center;
		if (_font != null) subHeader.AddThemeFontOverride("font", _font);
		subHeader.AddThemeFontSizeOverride("font_size", 15);
		subHeader.AddThemeColorOverride("font_color", new Color(0.88f, 0.88f, 0.92f));
		vbox.AddChild(subHeader);

		for (int i = 0; i < 3; i++)
		{
			int slotIdx = i;
			WeaponData w = (slotIdx < _player.WeaponSlots.Count) ? _player.WeaponSlots[slotIdx]?.CurrentWeapon : null;

			var slotBtn = new Button();
			slotBtn.CustomMinimumSize = new Vector2(0, 46);
			if (_font != null) slotBtn.AddThemeFontOverride("font", _font);
			slotBtn.AddThemeFontSizeOverride("font_size", 16);

			if (w == null)
			{
				slotBtn.Text = $"SLOT {slotIdx + 1}: [EMPTY]";
				slotBtn.Disabled = true;
			}
			else
			{
				bool compatible = offer.IsWeaponCompatible == null || offer.IsWeaponCompatible(w);
				string wName = !string.IsNullOrEmpty(w.WeaponName) && w.WeaponName != "Новое оружие"
					? w.WeaponName.ToUpper()
					: System.IO.Path.GetFileNameWithoutExtension(w.GetEffectiveResourcePath()).ToUpper();

				string ammoInfo = (w.IsRanged && w.MaxAmmo < 500) ? $"Ammo {w.MaxAmmo}, Rld {w.ReloadTime:0.##}s" : "Melee / No Mag";
				string manaInfo = w.ManaCost > 0.01f ? $", Mana {w.ManaCost:0.#}" : "";

				if (compatible)
				{
					slotBtn.Text = $"SLOT {slotIdx + 1}: {wName}  (DMG {w.Damage}, CD {w.AttackCooldown:0.##}s, {ammoInfo}{manaInfo})";
					slotBtn.Pressed += () =>
					{
						_weaponChoiceModal.QueueFree();
						_weaponChoiceModal = null;
						CompleteOfferPurchase(offer, cost, slotIdx);
					};
				}
				else
				{
					slotBtn.Text = $"SLOT {slotIdx + 1}: {wName} — [NOT COMPATIBLE WITH THIS UPGRADE]";
					slotBtn.Disabled = true;
				}
			}

			vbox.AddChild(slotBtn);
		}

		var cancelBtn = new Button();
		cancelBtn.Text = "CANCEL";
		cancelBtn.CustomMinimumSize = new Vector2(120, 30);
		if (_font != null) cancelBtn.AddThemeFontOverride("font", _font);
		cancelBtn.AddThemeFontSizeOverride("font_size", 17);
		cancelBtn.Pressed += () =>
		{
			_weaponChoiceModal.QueueFree();
			_weaponChoiceModal = null;
		};
		vbox.AddChild(cancelBtn);

		MainMenu.ConnectButtonSounds(_weaponChoiceModal);
	}

	private void RefreshUI()
	{
		if (_player == null) return;

		if (_goldLabel != null)
		{
			_goldLabel.Text = $"GOLD: {_player.CoinCount}   |   HP: {(int)_player.CurrentHealth}/{(int)_player.MaxHealthEffective} (Regen {_player.HealthRegenEffective:0.#}/s)   |   MP: {(int)_player.CurrentMana}/{(int)_player.MaxManaEffective}";
		}

		int levelIndex = GetCurrentLevelIndex();
		foreach (var (btn, offer, cantAffordStyle, purchasedStyle) in _offerButtons)
		{
			if (offer.IsPurchased)
			{
				btn.Text = $"[SOLD] {offer.Title}\n{offer.Description}";
				btn.Disabled = true;
				btn.AddThemeStyleboxOverride("disabled", purchasedStyle);
				btn.AddThemeColorOverride("font_disabled_color", new Color(0.42f, 0.42f, 0.46f));
			}
			else
			{
				int cost = offer.GetEffectiveCost(_player, levelIndex);
				int boughtCount = _player.GetShopPurchaseCount(offer.Id);
				string repeatTag = boughtCount > 0 ? $" (Bought {boughtCount}x)" : "";

				bool hasCompatibleWeapon = true;
				if (offer.RequiresWeaponChoice && offer.IsWeaponCompatible != null)
				{
					hasCompatibleWeapon = false;
					foreach (var slot in _player.WeaponSlots)
					{
						if (slot?.CurrentWeapon != null && offer.IsWeaponCompatible(slot.CurrentWeapon))
						{
							hasCompatibleWeapon = true;
							break;
						}
					}
				}

				string compatSuffix = hasCompatibleWeapon ? "" : " [NO COMPATIBLE WEAPON EQUIPPED]";
				btn.Text = $"[{offer.Category}] {offer.Title}{repeatTag}  —  {cost} GOLD\n{offer.Description}{compatSuffix}";
				btn.Disabled = (_player.CoinCount < cost) || !hasCompatibleWeapon;
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
			if (_weaponChoiceModal != null && GodotObject.IsInstanceValid(_weaponChoiceModal))
			{
				_weaponChoiceModal.QueueFree();
				_weaponChoiceModal = null;
			}
			else
			{
				CloseShop();
			}
			GetViewport().SetInputAsHandled();
		}
	}
}
