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
	public string IconPath = null;
	public bool IsPurchased = false;
	public bool IsFreeDraftOffer = false;
	public bool RequiresWeaponChoice = false;
	public Func<Player, bool> CanPurchase = null;
	public Func<Player, string> UnavailableReason = null;
	public Func<WeaponData, bool> IsWeaponCompatible = null;
	public Func<Player, Level, SupplyAltar, string> OnPurchase;
	public Func<Player, int, WeaponData, string> OnWeaponPurchase;

	public string GetEffectiveIconPath()
	{
		if (!string.IsNullOrEmpty(IconPath) && Godot.FileAccess.FileExists(IconPath))
		{
			return IconPath;
		}
		string lowerId = Id != null ? Id.ToLower() : "";
		if (lowerId == "global_weapon_dmg") return "res://assets/ui/shop_icons/dmg_upgrade.png";
		if (lowerId == "stat_hp_regen") return "res://assets/ui/shop_icons/hp_regen.png";
		if (lowerId == "stat_max_mana") return "res://assets/ui/shop_icons/max_mana.png";
		if (lowerId == "stat_max_hp") return "res://assets/ui/shop_icons/max_hp.png";
		if (lowerId == "stat_mana_regen") return "res://assets/ui/shop_icons/mana_regen.png";
		if (lowerId == "stat_move_speed") return "res://assets/ui/shop_icons/move_speed.png";
		if (lowerId == "stat_fury_draught") return "res://assets/ui/shop_icons/fury_draught.png";
		if (lowerId == "consumable_heal_boost") return "res://assets/ui/shop_icons/crimson_elixir.png";
		if (lowerId == "stat_mana_harmony") return "res://assets/ui/shop_icons/mana_harmony.png";
		if (lowerId == "ability_skill_point") return "res://assets/ui/shop_icons/skill_point.png";
		if (lowerId == "ability_1_level") return "res://assets/ui/shop_icons/ability_q.png";
		if (lowerId == "ability_2_level") return "res://assets/ui/shop_icons/ability_v.png";
		if (lowerId.StartsWith("armory")) return "res://assets/ui/shop_icons/armory.png";
		if (lowerId == "single_extended_mag") return "res://assets/ui/shop_icons/extended_mag.png";
		if (lowerId == "single_quickloader") return "res://assets/ui/shop_icons/quickloader.png";
		if (lowerId == "single_mana_reduction") return "res://assets/ui/shop_icons/mana_reduction.png";
		if (lowerId == "single_sharpened_core") return "res://assets/ui/shop_icons/sharpened_core.png";
		if (lowerId == "single_impact_core") return "res://assets/ui/shop_icons/impact_core.png";
		if (lowerId == "single_feather_grip") return "res://assets/ui/shop_icons/feather_grip.png";
		if (lowerId == "single_piercing") return "res://assets/ui/shop_icons/piercing.png";
		if (lowerId == "single_multishot") return "res://assets/ui/shop_icons/multishot.png";
		if (lowerId == "single_velocity_core") return "res://assets/ui/shop_icons/velocity_core.png";
		if (lowerId == "single_cleave_edge") return "res://assets/ui/shop_icons/cleave_edge.png";

		string path = $"res://assets/ui/shop_icons/{lowerId}.png";
		if (Godot.FileAccess.FileExists(path))
		{
			return path;
		}
		return "res://assets/ui/shop_icons/default.png";
	}

	public int GetEffectiveCost(Player player, int levelIndex)
	{
		if (IsFreeDraftOffer || BaseCost <= 0) return 0;
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
	public List<ShopOffer> MilestoneRelicOffers { get; private set; } = new List<ShopOffer>();
	public bool HasClaimedMilestoneRelic { get; set; } = false;
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
		"res://data/waepons/magic_staff.tres",
		"res://data/waepons/laser_cannon.tres"
	};

	public void Setup(Level level)
	{
		_level = level;
		Name = "SupplyAltar";
		Scale = Vector2.One;
		CollisionLayer = 0;
		CollisionMask = 16; // Слой 5 (Area2DPickWeapon игрока)

		var collision = new CollisionShape2D();
		var circle = new CircleShape2D();
		circle.Radius = 18.0f;
		collision.Shape = circle;
		AddChild(collision);

		_closedTex = GD.Load<Texture2D>("res://assets/objects/closed_chest.png");
		_openTex = GD.Load<Texture2D>("res://assets/objects/open_chest.png");

		DungeonVisuals.AttachGroundShadow(this, new Vector2(0, 1), new Vector2(0.72f, 0.56f), 0.58f);
		var aura = DungeonVisuals.CreateRuneAura(16.0f, new Color(1.0f, 0.82f, 0.28f, 0.72f), 6, -1);
		aura.Position = new Vector2(0, -1);
		AddChild(aura);

		_sprite = new Sprite2D();
		_sprite.Texture = _closedTex;
		_sprite.Position = new Vector2(0, -6);
		AddChild(_sprite);

		// Золотое свечение вокруг магазина
		var gradTex = GameResourceCache.GetRadialGlowTexture(
			new Color(1f, 0.9f, 0.4f, 0.95f),
			Colors.Transparent,
			1.0f,
			72,
			new Vector2(0.85f, 0.85f));
		GameResourceCache.CreateGlowLight(
			this,
			"ShopGlow",
			Vector2.Zero,
			new Color(1.0f, 0.85f, 0.35f, 0.95f),
			1.0f,
			gradTex,
			shadowEnabled: false);

		_promptLabel = new Label();
		_promptLabel.Text = "Shop [E]";
		_promptLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_promptLabel.Position = new Vector2(-45, -24);
		_promptLabel.Size = new Vector2(90, 15);
		_promptLabel.AddThemeColorOverride("font_color", new Color(1.0f, 0.88f, 0.35f));

		var font = GameResourceCache.GetSilverFont();
		if (font != null)
		{
			_promptLabel.AddThemeFontOverride("font", font);
			_promptLabel.AddThemeFontSizeOverride("font_size", 8);
		}
		_promptLabel.AddThemeConstantOverride("letter_spacing", 1);
		DungeonVisuals.ApplyPixelOutline(_promptLabel, 2);
		_promptLabel.Visible = false;
		AddChild(_promptLabel);

		RollFourRandomOffers();
		UpdatePromptText();

		AreaEntered += OnAreaEntered;
		AreaExited += OnAreaExited;
	}

	private static string GetRelicIconPath(string relicId) => relicId switch
	{
		GameLogic.RelicPrismLens => "res://assets/ui/shop_icons/piercing.png",
		GameLogic.RelicRicochet => "res://assets/ui/shop_icons/velocity_core.png",
		GameLogic.RelicEmberCore => "res://assets/ui/shop_icons/ability_q.png",
		GameLogic.RelicVenomCoat => "res://assets/ui/shop_icons/mana_harmony.png",
		GameLogic.RelicCrimsonEdge => "res://assets/ui/shop_icons/fury_draught.png",
		GameLogic.RelicChainSpark => "res://assets/ui/shop_icons/multishot.png",
		GameLogic.RelicFrostAegis => "res://assets/ui/shop_icons/ability_v.png",
		GameLogic.RelicVampiricFang => "res://assets/ui/shop_icons/crimson_elixir.png",
		GameLogic.RelicVolatileSigil => "res://assets/ui/shop_icons/impact_core.png",
		_ => "res://assets/ui/shop_icons/skill_point.png"
	};

	private static ShopOffer CreateRelicOffer(GameLogic.RelicDefinition relic, bool isFreeDraft)
	{
		Color accent = new Color(
			((relic.AccentHex >> 16) & 0xFF) / 255.0f,
			((relic.AccentHex >> 8) & 0xFF) / 255.0f,
			(relic.AccentHex & 0xFF) / 255.0f,
			1.0f);

		return new ShopOffer
		{
			Id = relic.Id,
			Category = isFreeDraft ? "MILESTONE RELIC" : "PASSIVE RELIC",
			Title = relic.Name.ToUpper(),
			Description = relic.Description,
			BaseCost = isFreeDraft ? 0 : relic.ShopCost,
			IsFreeDraftOffer = isFreeDraft,
			AccentColor = accent,
			IconPath = GetRelicIconPath(relic.Id),
			CanPurchase = p => p != null && !p.HasRelic(relic.Id),
			UnavailableReason = _ => "[OWNED]",
			OnPurchase = (p, lvl, alt) =>
			{
				p.AddRelic(relic.Id);
				if (isFreeDraft && alt != null)
				{
					alt.HasClaimedMilestoneRelic = true;
				}
				return $"RELIC ACQUIRED: {relic.Name.ToUpper()}!";
			}
		};
	}

	public void EnsureMilestoneRelicsRolled(Player player)
	{
		if (HasClaimedMilestoneRelic) return;
		int floor = _level != null ? _level.CurrentLevelIndex : 1;
		if (!GameLogic.IsMilestoneRelicFloor(floor)) return;
		if (MilestoneRelicOffers.Count > 0) return;

		var picks = GameLogic.PickRelicChoices(player?.GetRelicIdsList(), n => (int)(GD.Randi() % (uint)n), 3);
		foreach (var relic in picks)
		{
			MilestoneRelicOffers.Add(CreateRelicOffer(relic, isFreeDraft: true));
		}
		if (MilestoneRelicOffers.Count == 0)
		{
			HasClaimedMilestoneRelic = true;
		}
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
				IconPath = "res://assets/ui/shop_icons/hp_regen.png",
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
				IconPath = "res://assets/ui/shop_icons/max_mana.png",
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
				IconPath = "res://assets/ui/shop_icons/max_hp.png",
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
				IconPath = "res://assets/ui/shop_icons/mana_regen.png",
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
				IconPath = "res://assets/ui/shop_icons/move_speed.png",
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
				IconPath = "res://assets/ui/shop_icons/dmg_upgrade.png",
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
				IconPath = "res://assets/ui/shop_icons/fury_draught.png",
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
				IconPath = "res://assets/ui/shop_icons/crimson_elixir.png",
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
				IconPath = "res://assets/ui/shop_icons/mana_harmony.png",
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
				IconPath = "res://assets/ui/shop_icons/skill_point.png",
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
				Description = "+1 Level to Ability 1 (Fireball, Max Lv 4)",
				BaseCost = 16,
				AccentColor = new Color(1.0f, 0.52f, 0.22f),
				IconPath = "res://assets/ui/shop_icons/ability_q.png",
				CanPurchase = (p) => p != null && GameLogic.CanUpgradeAbility(p.Ability1Level),
				UnavailableReason = (_) => "[MAX LEVEL 4/4]",
				OnPurchase = (p, lvl, alt) =>
				{
					p.Ability1Level = GameLogic.ClampAbilityLevel(p.Ability1Level + 1);
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
				Description = "+1 Level to Ability 2 (Arcane Shield & Dash, Max Lv 4)",
				BaseCost = 16,
				AccentColor = new Color(0.35f, 0.88f, 1.0f),
				IconPath = "res://assets/ui/shop_icons/ability_v.png",
				CanPurchase = (p) => p != null && GameLogic.CanUpgradeAbility(p.Ability2Level),
				UnavailableReason = (_) => "[MAX LEVEL 4/4]",
				OnPurchase = (p, lvl, alt) =>
				{
					p.Ability2Level = GameLogic.ClampAbilityLevel(p.Ability2Level + 1);
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
				IconPath = "res://assets/ui/shop_icons/armory.png",
				OnPurchase = (p, lvl, alt) =>
				{
					Vector2 dropPos = alt != null ? alt.GlobalPosition + new Vector2(0, 42) : p.GlobalPosition;
					lvl?.RequestSpawnWeaponPickup(dropPos, rolledWeaponPath);
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
				IconPath = "res://assets/ui/shop_icons/extended_mag.png",
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
				IconPath = "res://assets/ui/shop_icons/quickloader.png",
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
				IconPath = "res://assets/ui/shop_icons/mana_reduction.png",
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
				IconPath = "res://assets/ui/shop_icons/sharpened_core.png",
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
				IconPath = "res://assets/ui/shop_icons/impact_core.png",
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
				IconPath = "res://assets/ui/shop_icons/feather_grip.png",
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
				IconPath = "res://assets/ui/shop_icons/piercing.png",
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
				IconPath = "res://assets/ui/shop_icons/multishot.png",
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
				IconPath = "res://assets/ui/shop_icons/velocity_core.png",
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
				IconPath = "res://assets/ui/shop_icons/cleave_edge.png",
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

		var combinedPool = new List<ShopOffer>();
		var currentPlayer = _level?.Player
			?? (IsInsideTree() ? GetTree()?.GetFirstNodeInGroup("Player") as Player : null)
			?? (_level != null && _level.IsInsideTree() ? _level.GetTree()?.GetFirstNodeInGroup("Player") as Player : null);

		EnsureMilestoneRelicsRolled(currentPlayer);

		foreach (var offer in generalPool)
		{
			if (offer.CanPurchase != null && currentPlayer != null && !offer.CanPurchase(currentPlayer))
			{
				continue;
			}
			combinedPool.Add(offer);
		}
		combinedPool.AddRange(singleWeaponPool);

		// Add up to 2 unowned passive relics into normal shop rotation
		var shopRelicPicks = GameLogic.PickRelicChoices(currentPlayer?.GetRelicIdsList(), n => (int)(GD.Randi() % (uint)n), 2);
		foreach (var relic in shopRelicPicks)
		{
			combinedPool.Add(CreateRelicOffer(relic, isFreeDraft: false));
		}

		while (RolledOffers.Count < 4 && combinedPool.Count > 0)
		{
			int idx = (int)(GD.Randi() % (uint)combinedPool.Count);
			RolledOffers.Add(combinedPool[idx]);
			combinedPool.RemoveAt(idx);
		}
	}

	public bool ShouldShowMilestoneDraft(Player player)
	{
		EnsureMilestoneRelicsRolled(player);
		int floor = _level != null ? _level.CurrentLevelIndex : 1;
		return GameLogic.IsMilestoneRelicFloor(floor) && !HasClaimedMilestoneRelic && MilestoneRelicOffers.Count > 0;
	}

	public void UpdatePromptText()
	{
		if (_promptLabel != null)
		{
			int floor = _level != null ? _level.CurrentLevelIndex : 1;
			if (GameLogic.IsMilestoneRelicFloor(floor) && !HasClaimedMilestoneRelic)
			{
				_promptLabel.Text = "Relic Choice [E]";
			}
			else
			{
				_promptLabel.Text = HasPurchased ? "Shop [Sold Out]" : "Shop [E]";
			}
		}
	}

	public void Interact(Player player)
	{
		bool isCoop = NetworkManager.Instance != null && NetworkManager.Instance.IsCoopSession;
		if (isCoop && (player == null || !player.IsLocalPlayer || player.IsDead)) return;
		if (_activeShop != null && GodotObject.IsInstanceValid(_activeShop)) return;

		if (_sprite != null && _openTex != null)
		{
			_sprite.Texture = _openTex;
		}

		if (!isCoop)
		{
			GetTree().Paused = true;
		}
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
			bool isCoop = NetworkManager.Instance != null && NetworkManager.Instance.IsCoopSession;
			if (isCoop && (!player.IsLocalPlayer || player.IsDead)) return;
			player.RegisterInteractable(this);
		}
	}

	private void OnAreaExited(Area2D area)
	{
		if (area.GetParent() is Player player)
		{
			bool isCoop = NetworkManager.Instance != null && NetworkManager.Instance.IsCoopSession;
			if (isCoop && !player.IsLocalPlayer) return;
			player.UnregisterInteractable(this);
		}
	}

	public override void _ExitTree()
	{
		if (_activeShop != null && GodotObject.IsInstanceValid(_activeShop))
		{
			bool isCoop = NetworkManager.Instance != null && NetworkManager.Instance.IsCoopSession;
			var tree = GetTree();
			if (!isCoop && tree != null && tree.Paused)
			{
				tree.Paused = false;
			}
			_activeShop.QueueFree();
			_activeShop = null;
		}
	}
}

public partial class SupplyShopOverlay : CanvasLayer
{
	private class OfferCardUI
	{
		public ShopOffer Offer;
		public PanelContainer CardPanel;
		public StyleBoxFlat CardNormalStyle;
		public StyleBoxFlat CardSoldStyle;
		public Label CategoryLabel;
		public TextureRect IconRect;
		public Label TitleLabel;
		public Label DescLabel;
		public Label ExtraLabel;
		public Button BuyButton;
		public StyleBoxFlat BtnNormalStyle;
		public StyleBoxFlat BtnHoverStyle;
		public StyleBoxFlat BtnDisabledStyle;
		public StyleBoxFlat BtnSoldStyle;
	}

	private readonly Player _player;
	private readonly Level _level;
	private readonly SupplyAltar _altar;
	private Control _rootControl;
	private Font _font;
	private Label _goldLabel;
	private Label _statusLabel;
	private Control _weaponChoiceModal;
	private Label _titleLabel;
	private HBoxContainer _cardsHBox;
	private bool _showingMilestoneDraft = false;
	private readonly List<OfferCardUI> _offerCards = new();

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
		_rootControl.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		Vector2 rootSize = GetViewport().GetVisibleRect().Size;

		var bgOverlay = new ColorRect();
		bgOverlay.Color = new Color(0.02f, 0.022f, 0.035f, 0.80f);
		_rootControl.AddChild(bgOverlay);
		bgOverlay.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

		var vig = new ColorRect
		{
			Material = DungeonVisuals.GetVignetteMaterial(0.8f, 0.56f),
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		vig.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		_rootControl.AddChild(vig);

		var panel = new Panel();
		Vector2 panelSize = new Vector2(840, 476);
		panel.Size = panelSize;
		panel.Position = (rootSize - panelSize) / 2.0f;
		_rootControl.AddChild(panel);

		var panelStyle = DungeonVisuals.CreatePanelStyle(
			DungeonVisuals.ObsidianDeep, DungeonVisuals.BrassGold, 2, 6, 12);
		panel.AddThemeStyleboxOverride("panel", panelStyle);

		var vbox = new VBoxContainer();
		vbox.Position = new Vector2(18, 12);
		vbox.Size = new Vector2(panelSize.X - 36, panelSize.Y - 24);
		vbox.AddThemeConstantOverride("separation", 6);
		panel.AddChild(vbox);

		_font = GD.Load<Font>("res://assets/fonts/silver/Silver.ttf");

		_titleLabel = new Label();
		_titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
		if (_font != null) _titleLabel.AddThemeFontOverride("font", _font);
		_titleLabel.AddThemeFontSizeOverride("font_size", 28);
		_titleLabel.AddThemeColorOverride("font_color", DungeonVisuals.BrassGold);
		DungeonVisuals.ApplyPixelOutline(_titleLabel, 2);
		vbox.AddChild(_titleLabel);

		var statsPill = new PanelContainer();
		statsPill.AddThemeStyleboxOverride("panel", DungeonVisuals.CreatePillStyle(
			DungeonVisuals.ObsidianCard, DungeonVisuals.BrassDim, 1, 3, 14, 3));
		statsPill.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
		vbox.AddChild(statsPill);

		_goldLabel = new Label();
		_goldLabel.HorizontalAlignment = HorizontalAlignment.Center;
		if (_font != null) _goldLabel.AddThemeFontOverride("font", _font);
		_goldLabel.AddThemeFontSizeOverride("font_size", 16);
		_goldLabel.AddThemeColorOverride("font_color", new Color(1.0f, 0.94f, 0.62f));
		DungeonVisuals.ApplyPixelOutline(_goldLabel, 1);
		statsPill.AddChild(_goldLabel);

		DungeonVisuals.AddDivider(vbox, new Color(DungeonVisuals.BrassDim.R, DungeonVisuals.BrassDim.G, DungeonVisuals.BrassDim.B, 0.6f), 1.5f);

		_cardsHBox = new HBoxContainer();
		_cardsHBox.CustomMinimumSize = new Vector2(0, 316);
		_cardsHBox.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		_cardsHBox.AddThemeConstantOverride("separation", 10);
		vbox.AddChild(_cardsHBox);

		_statusLabel = new Label();
		_statusLabel.HorizontalAlignment = HorizontalAlignment.Center;
		if (_font != null) _statusLabel.AddThemeFontOverride("font", _font);
		_statusLabel.AddThemeFontSizeOverride("font_size", 15);
		_statusLabel.AddThemeColorOverride("font_color", new Color(0.86f, 0.78f, 0.58f));
		DungeonVisuals.ApplyPixelOutline(_statusLabel, 1);
		vbox.AddChild(_statusLabel);

		RebuildDisplayedOffers();

		var bottomHBox = new HBoxContainer();
		bottomHBox.Alignment = BoxContainer.AlignmentMode.Center;
		vbox.AddChild(bottomHBox);

		var closeBtn = new Button();
		closeBtn.Text = "CLOSE [ESC]";
		closeBtn.CustomMinimumSize = new Vector2(160, 28);
		DungeonVisuals.StyleButton(closeBtn, DungeonVisuals.IronBorder.Lightened(0.3f), _font, 17, 3, DungeonVisuals.ObsidianCard, 12, 4);
		closeBtn.Pressed += CloseShop;
		bottomHBox.AddChild(closeBtn);

		DungeonVisuals.AttachFrameAccent(panel, DungeonVisuals.BrassGold, 6);

		RefreshUI();
		MainMenu.ConnectButtonSounds(this);
	}

	private void RebuildDisplayedOffers()
	{
		if (!IsInstanceValid(_cardsHBox)) return;
		foreach (Node child in _cardsHBox.GetChildren())
		{
			child.QueueFree();
		}
		_offerCards.Clear();

		_showingMilestoneDraft = _altar != null && _altar.ShouldShowMilestoneDraft(_player);
		if (_showingMilestoneDraft)
		{
			if (_titleLabel != null)
			{
				_titleLabel.Text = $"FLOOR {GetCurrentLevelIndex()} REWARD  —  CHOOSE 1 FREE RELIC";
				_titleLabel.AddThemeColorOverride("font_color", new Color(0.38f, 0.95f, 1.0f));
			}
			if (_statusLabel != null)
			{
				_statusLabel.Text = "Every 5 floors grants a FREE passive Relic! Pick 1 of 3 before browsing the shop.";
				_statusLabel.AddThemeColorOverride("font_color", DungeonVisuals.ArcaneCyan.Lightened(0.22f));
			}
			foreach (var offer in _altar.MilestoneRelicOffers)
			{
				AddOfferCard(_cardsHBox, _font, offer);
			}
		}
		else
		{
			if (_titleLabel != null)
			{
				_titleLabel.Text = "SUPPLY ALTAR  —  WAVE SHOP";
				_titleLabel.AddThemeColorOverride("font_color", DungeonVisuals.BrassGold);
			}
			if (_statusLabel != null && string.IsNullOrEmpty(_statusLabel.Text))
			{
				_statusLabel.Text = "Buy items to strengthen your hero. Repeat purchases cost 1.5x more.";
			}
			if (_altar?.RolledOffers != null)
			{
				foreach (var offer in _altar.RolledOffers)
				{
					AddOfferCard(_cardsHBox, _font, offer);
				}
			}
		}
	}

	private int GetCurrentLevelIndex()
	{
		return _level != null ? _level.CurrentLevelIndex : 1;
	}

	private void AddOfferCard(Control parent, Font font, ShopOffer offer)
	{
		var cardPanel = new PanelContainer();
		cardPanel.CustomMinimumSize = new Vector2(192, 316);
		cardPanel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		cardPanel.SizeFlagsVertical = Control.SizeFlags.Fill;

		var cardNormalStyle = DungeonVisuals.CreatePanelStyle(
			DungeonVisuals.ObsidianPanel, offer.AccentColor.Darkened(0.22f), 2, 5, 6);

		var cardSoldStyle = DungeonVisuals.CreatePanelStyle(
			new Color(0.05f, 0.05f, 0.07f, 0.72f), new Color(0.22f, 0.22f, 0.26f, 0.6f), 2, 5, 1);

		cardPanel.AddThemeStyleboxOverride("panel", cardNormalStyle);

		var margin = new MarginContainer();
		margin.AddThemeConstantOverride("margin_left", 8);
		margin.AddThemeConstantOverride("margin_right", 8);
		margin.AddThemeConstantOverride("margin_top", 8);
		margin.AddThemeConstantOverride("margin_bottom", 8);
		cardPanel.AddChild(margin);

		var vbox = new VBoxContainer();
		vbox.AddThemeConstantOverride("separation", 4);
		margin.AddChild(vbox);

		// 1. Category Tag
		var catLabel = new Label();
		catLabel.Text = offer.Category?.ToUpper() ?? "";
		catLabel.HorizontalAlignment = HorizontalAlignment.Center;
		if (font != null) catLabel.AddThemeFontOverride("font", font);
		catLabel.AddThemeFontSizeOverride("font_size", 12);
		catLabel.AddThemeColorOverride("font_color", offer.AccentColor.Lerp(Colors.White, 0.25f));
		DungeonVisuals.ApplyPixelOutline(catLabel, 1);
		vbox.AddChild(catLabel);

		// 2. Icon in center (icons already include 64x64 brass frame, keep clean accent halo behind)
		var iconCenter = new CenterContainer();
		iconCenter.CustomMinimumSize = new Vector2(0, 74);
		vbox.AddChild(iconCenter);

		var iconFrame = new PanelContainer();
		iconFrame.CustomMinimumSize = new Vector2(68, 68);
		var iconFrameStyle = DungeonVisuals.CreatePanelStyle(
			DungeonVisuals.ObsidianDeep, offer.AccentColor, 2, 4, 4,
			new Color(offer.AccentColor.R, offer.AccentColor.G, offer.AccentColor.B, 0.3f));
		iconFrame.AddThemeStyleboxOverride("panel", iconFrameStyle);
		iconCenter.AddChild(iconFrame);

		var iconRect = new TextureRect();
		iconRect.CustomMinimumSize = new Vector2(64, 64);
		iconRect.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		iconRect.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
		iconRect.TextureFilter = CanvasItem.TextureFilterEnum.Nearest;

		string iconPath = offer.GetEffectiveIconPath();
		Texture2D tex = GameLogic.FindRelic(offer.Id) != null
			? DungeonVisuals.GetRelicIconTexture(offer.Id)
			: GD.Load<Texture2D>(iconPath);
		if (tex != null)
		{
			iconRect.Texture = tex;
		}
		iconFrame.AddChild(iconRect);

		// 3. Title Label
		var titleLabel = new Label();
		titleLabel.Text = offer.Title;
		titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
		titleLabel.VerticalAlignment = VerticalAlignment.Center;
		titleLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		titleLabel.CustomMinimumSize = new Vector2(0, 36);
		if (font != null) titleLabel.AddThemeFontOverride("font", font);
		titleLabel.AddThemeFontSizeOverride("font_size", 17);
		titleLabel.AddThemeColorOverride("font_color", offer.IsFreeDraftOffer ? offer.AccentColor.Lightened(0.28f) : DungeonVisuals.Parchment);
		DungeonVisuals.ApplyPixelOutline(titleLabel, 1);
		vbox.AddChild(titleLabel);

		DungeonVisuals.AddDivider(vbox, new Color(offer.AccentColor.R, offer.AccentColor.G, offer.AccentColor.B, 0.45f), 1.5f);

		// 4. Description Label
		var descLabel = new Label();
		descLabel.Text = offer.Description;
		descLabel.HorizontalAlignment = HorizontalAlignment.Center;
		descLabel.VerticalAlignment = VerticalAlignment.Center;
		descLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		descLabel.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		if (font != null) descLabel.AddThemeFontOverride("font", font);
		descLabel.AddThemeFontSizeOverride("font_size", 15);
		descLabel.AddThemeColorOverride("font_color", new Color(0.88f, 0.91f, 0.96f));
		vbox.AddChild(descLabel);

		// 5. Extra Label
		var extraLabel = new Label();
		extraLabel.Text = "";
		extraLabel.HorizontalAlignment = HorizontalAlignment.Center;
		extraLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		extraLabel.CustomMinimumSize = new Vector2(0, 16);
		if (font != null) extraLabel.AddThemeFontOverride("font", font);
		extraLabel.AddThemeFontSizeOverride("font_size", 11);
		vbox.AddChild(extraLabel);

		// 6. Buy Button
		var btn = new Button();
		btn.CustomMinimumSize = new Vector2(0, 34);
		btn.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
		if (font != null) btn.AddThemeFontOverride("font", font);
		btn.AddThemeFontSizeOverride("font_size", 15);

		var btnNormal = DungeonVisuals.CreatePanelStyle(DungeonVisuals.ObsidianCard, offer.AccentColor, 2, 4, 3);
		var btnHover = DungeonVisuals.CreatePanelStyle(
			DungeonVisuals.ObsidianElevated, offer.AccentColor.Lightened(0.25f), 2, 4, 5,
			new Color(offer.AccentColor.R, offer.AccentColor.G, offer.AccentColor.B, 0.35f));
		var btnDisabled = DungeonVisuals.CreatePanelStyle(
			new Color(0.08f, 0.08f, 0.10f, 0.85f), new Color(0.28f, 0.28f, 0.32f), 1, 4, 0);
		var btnSold = DungeonVisuals.CreatePanelStyle(
			new Color(0.05f, 0.05f, 0.06f, 0.65f), new Color(0.18f, 0.18f, 0.22f, 0.6f), 1, 4, 0);

		btn.AddThemeStyleboxOverride("normal", btnNormal);
		btn.AddThemeStyleboxOverride("hover", btnHover);
		btn.AddThemeStyleboxOverride("pressed", btnHover);
		btn.AddThemeStyleboxOverride("disabled", btnDisabled);

		btn.Pressed += () =>
		{
			if (_player == null || _altar == null || offer.IsPurchased) return;
			if (offer.CanPurchase != null && !offer.CanPurchase(_player)) return;
			int cost = offer.GetEffectiveCost(_player, GetCurrentLevelIndex());
			if (_player.CoinCount < cost) return;

			if (offer.RequiresWeaponChoice)
			{
				OpenWeaponSelectionModal(offer, cost);
				return;
			}

			CompleteOfferPurchase(offer, cost, -1);
		};

		vbox.AddChild(btn);
		DungeonVisuals.AttachFrameAccent(cardPanel, offer.AccentColor, 4);
		parent.AddChild(cardPanel);

		_offerCards.Add(new OfferCardUI
		{
			Offer = offer,
			CardPanel = cardPanel,
			CardNormalStyle = cardNormalStyle,
			CardSoldStyle = cardSoldStyle,
			CategoryLabel = catLabel,
			IconRect = iconRect,
			TitleLabel = titleLabel,
			DescLabel = descLabel,
			ExtraLabel = extraLabel,
			BuyButton = btn,
			BtnNormalStyle = btnNormal,
			BtnHoverStyle = btnHover,
			BtnDisabledStyle = btnDisabled,
			BtnSoldStyle = btnSold
		});
	}

	private void CompleteOfferPurchase(ShopOffer offer, int cost, int weaponSlotIndex)
	{
		if (_player == null || _altar == null || offer.IsPurchased) return;
		if (offer.CanPurchase != null && !offer.CanPurchase(_player)) return;
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
		if (!offer.IsFreeDraftOffer)
		{
			_player.RecordShopPurchase(offer.Id);
		}
		offer.IsPurchased = true;
		_altar.UpdatePromptText();
		_player.BroadcastCoopBuildSync();

		if (_statusLabel != null)
		{
			_statusLabel.Text = msg;
			_statusLabel.AddThemeColorOverride("font_color", DungeonVisuals.Emerald);
		}

		var sound = GD.Load<AudioStream>("res://assets/sounds/buff_collecting.wav");
		if (sound != null) MainMenu.PlayUISound(sound);

		if (offer.IsFreeDraftOffer && _showingMilestoneDraft)
		{
			RebuildDisplayedOffers();
		}

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
		Vector2 modalSize = new Vector2(490, 310);
		modalPanel.Size = modalSize;
		Vector2 rootSize = _rootControl.Size != Vector2.Zero ? _rootControl.Size : GetViewport().GetVisibleRect().Size;
		modalPanel.Position = (rootSize - modalSize) / 2.0f;
		_weaponChoiceModal.AddChild(modalPanel);

		modalPanel.AddThemeStyleboxOverride("panel", DungeonVisuals.CreatePanelStyle(
			DungeonVisuals.ObsidianDeep, offer.AccentColor, 2, 5, 10));

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
		DungeonVisuals.ApplyPixelOutline(header, 1);
		vbox.AddChild(header);

		var subHeader = new Label();
		subHeader.Text = offer.Description;
		subHeader.HorizontalAlignment = HorizontalAlignment.Center;
		if (_font != null) subHeader.AddThemeFontOverride("font", _font);
		subHeader.AddThemeFontSizeOverride("font_size", 14);
		subHeader.AddThemeColorOverride("font_color", DungeonVisuals.Parchment);
		vbox.AddChild(subHeader);

		DungeonVisuals.AddDivider(vbox, new Color(offer.AccentColor.R, offer.AccentColor.G, offer.AccentColor.B, 0.5f), 1.5f);

		for (int i = 0; i < 3; i++)
		{
			int slotIdx = i;
			WeaponData w = (slotIdx < _player.WeaponSlots.Count) ? _player.WeaponSlots[slotIdx]?.CurrentWeapon : null;

			var slotBtn = new Button();
			slotBtn.CustomMinimumSize = new Vector2(0, 48);
			slotBtn.Alignment = HorizontalAlignment.Left;

			if (w == null)
			{
				slotBtn.Text = $"   SLOT {slotIdx + 1}: [EMPTY]";
				slotBtn.Disabled = true;
				DungeonVisuals.StyleButton(slotBtn, DungeonVisuals.IronBorder, _font, 15, 3, DungeonVisuals.ObsidianPanel, 12, 4);
			}
			else
			{
				bool compatible = offer.IsWeaponCompatible == null || offer.IsWeaponCompatible(w);
				string wName = !string.IsNullOrEmpty(w.WeaponName) && w.WeaponName != "Новое оружие"
					? w.WeaponName.ToUpper()
					: System.IO.Path.GetFileNameWithoutExtension(w.GetEffectiveResourcePath()).ToUpper();

				string ammoInfo = (w.IsRanged && w.MaxAmmo < 500) ? $"Ammo {w.MaxAmmo}, Rld {w.ReloadTime:0.##}s" : "Melee / No Mag";
				string manaInfo = w.ManaCost > 0.01f ? $", Mana {w.ManaCost:0.#}" : "";

				var wTex = WeaponShowcaseSlot.ExtractWeaponTextureStatic(w);
				if (wTex != null)
				{
					slotBtn.Icon = wTex;
					slotBtn.ExpandIcon = false;
				}

				if (compatible)
				{
					slotBtn.Text = $" SLOT {slotIdx + 1}: {wName}  (DMG {w.Damage}, CD {w.AttackCooldown:0.##}s, {ammoInfo}{manaInfo})";
					DungeonVisuals.StyleButton(slotBtn, offer.AccentColor, _font, 15, 3, DungeonVisuals.ObsidianCard, 12, 4);
					slotBtn.Pressed += () =>
					{
						_weaponChoiceModal.QueueFree();
						_weaponChoiceModal = null;
						CompleteOfferPurchase(offer, cost, slotIdx);
					};
				}
				else
				{
					slotBtn.Text = $" SLOT {slotIdx + 1}: {wName} — [NOT COMPATIBLE]";
					slotBtn.Disabled = true;
					DungeonVisuals.StyleButton(slotBtn, DungeonVisuals.IronBorder, _font, 14, 3, DungeonVisuals.ObsidianPanel, 12, 4);
				}
			}

			vbox.AddChild(slotBtn);
		}

		var cancelCenter = new CenterContainer();
		vbox.AddChild(cancelCenter);

		var cancelBtn = new Button();
		cancelBtn.Text = "CANCEL";
		cancelBtn.CustomMinimumSize = new Vector2(140, 30);
		DungeonVisuals.StyleButton(cancelBtn, DungeonVisuals.IronBorder.Lightened(0.3f), _font, 16, 3, DungeonVisuals.ObsidianCard, 12, 4);
		cancelBtn.Pressed += () =>
		{
			_weaponChoiceModal.QueueFree();
			_weaponChoiceModal = null;
		};
		cancelCenter.AddChild(cancelBtn);

		DungeonVisuals.AttachFrameAccent(modalPanel, offer.AccentColor, 5);
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
		foreach (var card in _offerCards)
		{
			var offer = card.Offer;
			if (offer.IsPurchased)
			{
				card.CardPanel.AddThemeStyleboxOverride("panel", card.CardSoldStyle);
				card.ExtraLabel.Text = "[SOLD OUT]";
				card.ExtraLabel.AddThemeColorOverride("font_color", new Color(0.50f, 0.50f, 0.55f));
				card.BuyButton.Text = "SOLD";
				card.BuyButton.Disabled = true;
				card.BuyButton.AddThemeStyleboxOverride("disabled", card.BtnSoldStyle);
				card.BuyButton.AddThemeColorOverride("font_disabled_color", new Color(0.40f, 0.40f, 0.45f));
			}
			else
			{
				int cost = offer.GetEffectiveCost(_player, levelIndex);
				int boughtCount = _player.GetShopPurchaseCount(offer.Id);
				string repeatTag = boughtCount > 0 ? $"Bought {boughtCount}x" : "";

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

				bool canOfferPurchase = offer.CanPurchase == null || offer.CanPurchase(_player);

				if (!canOfferPurchase)
				{
					card.ExtraLabel.Text = offer.UnavailableReason != null ? offer.UnavailableReason(_player) : "[UNAVAILABLE]";
					card.ExtraLabel.AddThemeColorOverride("font_color", new Color(1.0f, 0.45f, 0.45f));
				}
				else if (!hasCompatibleWeapon)
				{
					card.ExtraLabel.Text = "[Needs weapon]";
					card.ExtraLabel.AddThemeColorOverride("font_color", new Color(1.0f, 0.45f, 0.45f));
				}
				else if (boughtCount > 0)
				{
					card.ExtraLabel.Text = repeatTag;
					card.ExtraLabel.AddThemeColorOverride("font_color", new Color(1.0f, 0.82f, 0.35f));
				}
				else
				{
					card.ExtraLabel.Text = "";
				}

				bool canAfford = _player.CoinCount >= cost;
				if (offer.IsFreeDraftOffer)
				{
					card.BuyButton.Text = canOfferPurchase ? "CLAIM RELIC (FREE)" : "OWNED";
				}
				else
				{
					card.BuyButton.Text = canOfferPurchase
						? (canAfford ? $"BUY: {cost} GOLD" : $"NEED {cost} GOLD")
						: "MAXED";
				}
				card.BuyButton.Disabled = !canOfferPurchase || !canAfford || !hasCompatibleWeapon;
				card.CardPanel.AddThemeStyleboxOverride("panel", card.CardNormalStyle);

				if (card.BuyButton.Disabled)
				{
					card.BuyButton.AddThemeStyleboxOverride("disabled", card.BtnDisabledStyle);
					Color disabledTextCol = (!canAfford && canOfferPurchase && hasCompatibleWeapon)
						? new Color(0.88f, 0.45f, 0.42f, 0.95f)
						: new Color(0.55f, 0.57f, 0.62f, 0.85f);
					card.BuyButton.AddThemeColorOverride("font_disabled_color", disabledTextCol);
				}
			}
		}
	}

	private void CloseShop()
	{
		bool isCoop = NetworkManager.Instance != null && NetworkManager.Instance.IsCoopSession;
		if (!isCoop && GetTree() != null)
		{
			GetTree().Paused = false;
		}
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
