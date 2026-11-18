using Godot;
using System;
using System.Collections.Generic;

public partial class AbilitySlotUI : Control
{
	public const float SlotSize = 56.0f;
	public const float UiScale = SlotSize / 46.0f;

	private int _abilityId; // 1 = Fireball, 2 = Arcane Shield & Dash
	private HUD _hud;
	private Player _player;

	private AbilityCircle _circle;
	private Button _upgradeButton;

	public AbilitySlotUI(int abilityId, HUD hud)
	{
		_abilityId = abilityId;
		_hud = hud;
		CustomMinimumSize = new Vector2(SlotSize, SlotSize);
		Size = new Vector2(SlotSize, SlotSize);
		MouseFilter = MouseFilterEnum.Ignore;
	}

	public override void _Ready()
	{
		_player = NetworkManager.GetLocalPlayer(GetTree());

		_circle = new AbilityCircle(_abilityId, this);
		_circle.Size = new Vector2(SlotSize, SlotSize);
		_circle.Position = Vector2.Zero;
		_circle.Scale = Vector2.One;
		_circle.MouseFilter = MouseFilterEnum.Ignore;
		AddChild(_circle);

		_upgradeButton = new AbilityUpgradeButton();
		_upgradeButton.CustomMinimumSize = new Vector2(40, 22);
		_upgradeButton.Size = new Vector2(40, 22);
		_upgradeButton.Position = new Vector2((SlotSize - 40.0f) * 0.5f, -25.0f);
		_upgradeButton.FocusMode = FocusModeEnum.None;

		var styleEmpty = new StyleBoxEmpty();
		_upgradeButton.AddThemeStyleboxOverride("normal", styleEmpty);
		_upgradeButton.AddThemeStyleboxOverride("hover", styleEmpty);
		_upgradeButton.AddThemeStyleboxOverride("pressed", styleEmpty);
		_upgradeButton.AddThemeStyleboxOverride("focus", styleEmpty);

		_upgradeButton.Pressed += OnUpgradePressed;
		AddChild(_upgradeButton);

		UpdateState();
	}

	private int _lastLevel = -1;
	private bool _lastUpgradeVisible = false;

	public void UpdateState()
	{
		if (!GodotObject.IsInstanceValid(_player) || _player.IsQueuedForDeletion())
		{
			_player = NetworkManager.GetLocalPlayer(GetTree());
		}

		if (_player == null || _upgradeButton == null || _circle == null) return;

		int currentLevel = _abilityId == 1 ? _player.Ability1Level : _player.Ability2Level;
		bool hasPoints = _player.SkillPoints > 0;
		bool isMaxLevel = !GameLogic.CanUpgradeAbility(currentLevel);
		bool showUpgrade = hasPoints && !isMaxLevel;

		if (showUpgrade != _lastUpgradeVisible || _upgradeButton.Visible != showUpgrade)
		{
			_upgradeButton.Visible = showUpgrade;
			_lastUpgradeVisible = showUpgrade;
		}

		float cooldown = _abilityId == 1 ? _player.FireballCooldownTimer : _player.ShieldCooldownTimer;
		bool isActive = (_abilityId == 2 && _player.IsShieldActive) || cooldown > 0.0f;
		string currentBind = _abilityId == 1
			? (SettingsManager.KeyBinds.TryGetValue("ability_1", out var k1) ? k1.ToString() : "Q")
			: (SettingsManager.KeyBinds.TryGetValue("ability_2", out var k2) ? k2.ToString() : "V");

		if (isActive || currentLevel != _lastLevel || _lastBindText != currentBind || _wasActiveLastFrame)
		{
			_lastLevel = currentLevel;
			_lastBindText = currentBind;
			_wasActiveLastFrame = isActive;
			_circle.QueueRedraw();
		}

		if (_upgradeButton.Visible)
		{
			float bob = Mathf.Sin(Time.GetTicksMsec() * 0.008f) * 1.8f;
			_upgradeButton.Position = new Vector2((SlotSize - 40.0f) * 0.5f, -26.0f + bob);
			_upgradeButton.QueueRedraw();
		}
	}

	public bool IsMouseOverSlot(Vector2 screenPos)
	{
		if (!IsVisibleInTree()) return false;
		if (GetGlobalRect().HasPoint(screenPos)) return true;
		if (_upgradeButton != null && _upgradeButton.Visible && _upgradeButton.GetGlobalRect().HasPoint(screenPos)) return true;
		return false;
	}

	public string FormatAbilityTooltip(Player player)
	{
		if (player == null) return "";
		int lvl = _abilityId == 1 ? player.Ability1Level : player.Ability2Level;
		string bindKey = _abilityId == 1 ? "ability_1" : "ability_2";
		string keyText = SettingsManager.KeyBinds.TryGetValue(bindKey, out var k) ? k.ToString() : (_abilityId == 1 ? "Q" : "V");

		if (_abilityId == 1)
		{
			int curLvl = Mathf.Max(1, lvl);
			float dmg = (player.FireballBaseDamage + (curLvl - 1) * player.FireballDamagePerLevel) * player.WeaponDamageMultiplier * player.Ability1DamageMultiplier;
			float radius = 45.0f * Mathf.Clamp(player.Ability1RadiusMultiplier, 1.0f, 2.5f);
			float cd = player.GetAbility1CooldownMax();
			string status = lvl <= 0 ? "LOCKED" : $"LVL {lvl}/{GameLogic.MaxAbilityLevel}";
			string nextLine = lvl <= 0
				? (player.SkillPoints > 0 ? "CLICK [▲] TO UNLOCK" : "EARN SKILL POINT TO UNLOCK")
				: (lvl < GameLogic.MaxAbilityLevel ? $"NEXT LVL: +{(int)(player.FireballDamagePerLevel * player.WeaponDamageMultiplier * player.Ability1DamageMultiplier)} DMG" : "MAX LEVEL REACHED");
			return $"[{keyText}] FIREBALL ({status})\n" +
				   $"DMG: {dmg:0} AOE (R:{radius:0})   MP: 20   CD: {cd:0.#}s\n" +
				   nextLine;
		}
		else
		{
			int curLvl = Mathf.Max(1, lvl);
			int pasRes = 5 + (curLvl - 1) * 5;
			float dur = 2.5f + (curLvl - 1) * 0.7f;
			float pulse = (4.0f + curLvl * 3.0f) * player.WeaponDamageMultiplier * player.Ability2DamageMultiplier;
			float radius = 52.0f * Mathf.Clamp(player.Ability2RadiusMultiplier, 1.0f, 2.5f) * (player.HasRelic(GameLogic.RelicFrostAegis) ? 1.25f : 1.0f);
			float cd = player.GetAbility2CooldownMax();
			string status = lvl <= 0 ? "LOCKED" : $"LVL {lvl}/{GameLogic.MaxAbilityLevel}";
			string nextLine = lvl <= 0
				? (player.SkillPoints > 0 ? "CLICK [▲] TO UNLOCK" : "EARN SKILL POINT TO UNLOCK")
				: (lvl < GameLogic.MaxAbilityLevel ? "NEXT: +5% DEF, +0.7s SHIELD" : "MAX LEVEL REACHED");
			return $"[{keyText}] AEGIS DASH ({status})\n" +
				   $"PASSIVE: -{pasRes}% DMG  SHIELD: {dur:0.0}s\n" +
				   $"PULSE: {pulse:0} DMG (R:{radius:0})   MP: 15   CD: {cd:0.#}s\n" +
				   nextLine;
		}
	}

	private string _lastBindText = "";
	private bool _wasActiveLastFrame = false;

	private void OnUpgradePressed()
	{
		if (_player == null || _player.SkillPoints <= 0) return;

		int currentLevel = _abilityId == 1 ? _player.Ability1Level : _player.Ability2Level;
		if (!GameLogic.CanUpgradeAbility(currentLevel)) return;

		DungeonVisuals.PlayUiClickSound(this, 1.12f);
		_player.SkillPoints--;

		if (_abilityId == 1)
		{
			_player.Ability1Level = GameLogic.ClampAbilityLevel(_player.Ability1Level + 1);
		}
		else
		{
			_player.Ability2Level = GameLogic.ClampAbilityLevel(_player.Ability2Level + 1);
		}

		_player.BroadcastCoopBuildSync();
		_hud.UpdateAbilitiesUI();
	}
}

public partial class AbilityCircle : Control
{
	private int _abilityId;
	private AbilitySlotUI _slotUI;
	private Player _player;

	public AbilityCircle(int abilityId, AbilitySlotUI slotUI)
	{
		_abilityId = abilityId;
		_slotUI = slotUI;
	}

	public override void _Ready()
	{
		_player = NetworkManager.GetLocalPlayer(GetTree());
	}

	public override void _Draw()
	{
		if (!GodotObject.IsInstanceValid(_player) || _player.IsQueuedForDeletion())
		{
			_player = NetworkManager.GetLocalPlayer(GetTree());
		}

		if (_player == null) return;

		int lvl = _abilityId == 1 ? _player.Ability1Level : _player.Ability2Level;
		float cooldown = _abilityId == 1 ? _player.FireballCooldownTimer : _player.ShieldCooldownTimer;
		float maxCooldown = _abilityId == 1 ? _player.GetAbility1CooldownMax() : _player.GetAbility2CooldownMax();

		const float s = AbilitySlotUI.UiScale;
		Vector2 center = new Vector2(23f, 23f) * s;
		float radius = 21.5f * s;
		bool isUnlocked = lvl > 0;
		bool isReady = isUnlocked && cooldown <= 0.0f;

		// 1. Внешняя тень и обсидиановый медальон
		DrawCircle(center + new Vector2(1.5f, 2.3f) * s, radius + 0.8f * s, new Color(0f, 0f, 0f, 0.55f));
		DrawCircle(center, radius, new Color(0.06f, 0.065f, 0.10f, 0.96f));
		DrawCircle(center, radius - 3.8f * s, isUnlocked ? new Color(0.11f, 0.115f, 0.17f, 0.95f) : new Color(0.08f, 0.08f, 0.11f, 0.9f));

		// Мягкое фоновое свечение при готовности
		if (isReady)
		{
			Color aura = _abilityId == 1 ? new Color(1.0f, 0.45f, 0.12f, 0.14f) : new Color(0.24f, 0.78f, 1.0f, 0.14f);
			DrawCircle(center, radius - 4.2f * s, aura);
		}

		// 2. Двойная окантовка (бронза/циан при активности) и 4 рунических засечки по сторонам света
		Color borderColor = !isUnlocked
			? new Color(0.28f, 0.30f, 0.36f)
			: (_abilityId == 2 && _player.IsShieldActive
				? new Color(0.32f, 0.96f, 1.0f)
				: (isReady ? DungeonVisuals.BrassGold : DungeonVisuals.BrassDim));
		DrawArc(center, radius, 0f, Mathf.Tau, 32, borderColor, 2.6f * s, false);
		DrawArc(center, radius - 3.0f * s, 0f, Mathf.Tau, 28, new Color(1f, 1f, 1f, isReady ? 0.16f : 0.06f), 1.3f * s, false);
		if (isUnlocked)
		{
			Color studCol = isReady ? borderColor.Lightened(0.25f) : borderColor;
			for (int k = 0; k < 4; k++)
			{
				float ang = k * Mathf.Pi * 0.5f;
				Vector2 p = center + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * (radius - 0.5f * s);
				DrawRect(new Rect2(p.X - 1.4f * s, p.Y - 1.4f * s, 2.8f * s, 2.8f * s), studCol);
			}
		}

		// 3. Детализированные эмблемы способностей
		if (_abilityId == 1)
		{
			// Многослойное пламя огненного шара
			Vector2[] outerFlame = new Vector2[]
			{
				new Vector2(23.0f, 9.2f) * s,
				new Vector2(29.1f, 15.7f) * s,
				new Vector2(31.8f, 23.4f) * s,
				new Vector2(28.4f, 31.0f) * s,
				new Vector2(17.6f, 31.0f) * s,
				new Vector2(14.2f, 23.4f) * s,
				new Vector2(16.9f, 15.7f) * s,
			};
			Color outerColor = isUnlocked ? new Color(0.98f, 0.38f, 0.08f) : new Color(0.38f, 0.36f, 0.38f);
			DrawColoredPolygon(outerFlame, outerColor);

			if (isUnlocked)
			{
				Vector2[] midFlame = new Vector2[]
				{
					new Vector2(23.0f, 13.4f) * s,
					new Vector2(27.8f, 20.3f) * s,
					new Vector2(26.5f, 29.1f) * s,
					new Vector2(19.5f, 29.1f) * s,
					new Vector2(18.2f, 20.3f) * s,
				};
				DrawColoredPolygon(midFlame, new Color(1.0f, 0.72f, 0.18f));

				DrawCircle(new Vector2(23.0f, 24.9f) * s, 3.4f * s, new Color(1.0f, 0.96f, 0.72f));
				DrawCircle(new Vector2(17.2f, 14.6f) * s, 1.5f * s, new Color(1.0f, 0.75f, 0.30f, 0.85f));
			}
		}
		else
		{
			// Щит Эгиды с руническим крестом
			Vector2[] shieldOuter = new Vector2[]
			{
				new Vector2(23.0f, 10.0f) * s,
				new Vector2(32.2f, 12.6f) * s,
				new Vector2(31.4f, 22.6f) * s,
				new Vector2(23.0f, 32.2f) * s,
				new Vector2(14.6f, 22.6f) * s,
				new Vector2(13.8f, 12.6f) * s,
			};
			Color shieldColor = isUnlocked ? new Color(0.16f, 0.52f, 0.90f) : new Color(0.35f, 0.38f, 0.44f);
			DrawColoredPolygon(shieldOuter, shieldColor);

			if (isUnlocked)
			{
				Vector2[] outlinePts = new Vector2[]
				{
					shieldOuter[0], shieldOuter[1], shieldOuter[2], shieldOuter[3], shieldOuter[4], shieldOuter[5], shieldOuter[0]
				};
				DrawPolyline(outlinePts, new Color(0.65f, 0.92f, 1.0f), 1.9f * s, false);
				// Внутренний светящийся крест
				DrawLine(new Vector2(23.0f, 13.8f) * s, new Vector2(23.0f, 28.4f) * s, new Color(0.82f, 0.97f, 1.0f), 2.6f * s);
				DrawLine(new Vector2(16.9f, 19.5f) * s, new Vector2(29.1f, 19.5f) * s, new Color(0.82f, 0.97f, 1.0f), 2.6f * s);
			}
		}

		// 4. Затемнение для заблокированной способности
		if (!isUnlocked)
		{
			DrawCircle(center, radius, new Color(0f, 0f, 0f, 0.48f));
		}

		// 5. Оверлей перезарядки
		if (cooldown > 0.0f)
		{
			if (cooldown / maxCooldown >= 0.99f)
			{
				DrawCircle(center, radius, new Color(0, 0, 0, 0.68f));
			}
			else
			{
				float angle = (cooldown / maxCooldown) * Mathf.Tau;
				int segments = 24;
				var vertices = new List<Vector2> { center };

				float startAngle = -Mathf.Pi / 2.0f;
				for (int i = 0; i <= segments; i++)
				{
					float currentAngle = startAngle + angle * (i / (float)segments);
					Vector2 vertex = center + new Vector2(Mathf.Cos(currentAngle), Mathf.Sin(currentAngle)) * radius;

					bool duplicate = false;
					foreach (var v in vertices)
					{
						if (v.DistanceTo(vertex) < 0.5f)
						{
							duplicate = true;
							break;
						}
					}
					if (!duplicate)
					{
						vertices.Add(vertex);
					}
				}

				if (vertices.Count >= 3)
				{
					var colors = new Color[vertices.Count];
					for (int i = 0; i < colors.Length; i++)
					{
						colors[i] = new Color(0.02f, 0.02f, 0.05f, 0.72f);
					}
					DrawPolygon(vertices.ToArray(), colors);
				}
			}

			string cdText = string.Format("{0:0.0}", cooldown);
			var font = GameResourceCache.GetSilverFont();
			if (font != null)
			{
				int cdFontSize = Mathf.RoundToInt(19 * s);
				Vector2 textSize = font.GetStringSize(cdText, HorizontalAlignment.Center, -1, cdFontSize);
				float baselineY = DungeonVisuals.GetCenteredDrawStringBaseline(font, cdFontSize, 0.0f, Size.Y);
				Vector2 textOrigin = new Vector2(center.X - textSize.X / 2.0f, baselineY);
				DrawString(font, textOrigin + new Vector2(1.5f, 1.5f) * s, cdText, HorizontalAlignment.Left, -1, cdFontSize, new Color(0, 0, 0, 0.9f));
				DrawString(font, textOrigin, cdText, HorizontalAlignment.Left, -1, cdFontSize, Colors.White);
			}
		}

		// 6. Гнезда уровня (4 ячейки по низу, сдвинуты левее от клавиши)
		if (isUnlocked)
		{
			const int maxLvl = 4;
			float spacing = 6.1f * s;
			float startX = 17.2f * s - ((maxLvl - 1) * spacing) * 0.5f;
			float dotY = 39.3f * s;

			for (int i = 0; i < maxLvl; i++)
			{
				float dotX = startX + i * spacing;
				bool filled = i < lvl;
				DrawRect(new Rect2(dotX - 2.5f * s, dotY - 2.5f * s, 5.0f * s, 5.0f * s), new Color(0.04f, 0.04f, 0.06f, 0.95f));
				Color gemColor = filled ? new Color(0.32f, 0.95f, 0.52f) : new Color(0.22f, 0.24f, 0.30f);
				DrawRect(new Rect2(dotX - 1.5f * s, dotY - 1.5f * s, 3.0f * s, 3.0f * s), gemColor);
				if (filled)
				{
					DrawRect(new Rect2(dotX - 1.5f * s, dotY - 1.5f * s, 3.0f * s, 1.0f * s), new Color(0.82f, 1.0f, 0.88f, 0.90f));
				}
			}
		}

		// 7. Плашка горячей клавиши в правом нижнем углу (без наложения на самоцветы уровня)
		var keyFont = GameResourceCache.GetSilverFont();
		if (keyFont != null)
		{
			string bindKey = _abilityId == 1 ? "ability_1" : "ability_2";
			string keyText = SettingsManager.KeyBinds.ContainsKey(bindKey)
				? SettingsManager.KeyBinds[bindKey].ToString()
				: (_abilityId == 1 ? "Q" : "V");
			if (keyText.Length > 2) keyText = keyText.Substring(0, 2);

			Rect2 badge = new Rect2(29.5f * s, 29.5f * s, 16.0f * s, 16.0f * s);
			DrawRect(badge, new Color(0.05f, 0.055f, 0.085f, 0.96f));
			DrawLine(badge.Position + new Vector2(1.5f, 1.5f), badge.Position + new Vector2(badge.Size.X - 1.5f, 1.5f), new Color(1f, 0.96f, 0.85f, isReady ? 0.22f : 0.08f), 1.0f);
			DrawRect(badge, isReady ? DungeonVisuals.BrassGold : DungeonVisuals.IronBorder, false, 1.6f);
			Color keyColor = isReady ? DungeonVisuals.Parchment : new Color(0.58f, 0.60f, 0.68f);
			int keyFontSize = Mathf.RoundToInt(15 * s);
			float badgeBaselineY = DungeonVisuals.GetCenteredDrawStringBaseline(keyFont, keyFontSize, badge.Position.Y, badge.Size.Y);
			DrawString(keyFont, new Vector2(badge.Position.X, badgeBaselineY), keyText, HorizontalAlignment.Center, badge.Size.X, keyFontSize, keyColor);
		}
	}
}

public partial class AbilityUpgradeButton : Button
{
	public override void _Draw()
	{
		float pulse = 0.5f + 0.5f * Mathf.Sin(Time.GetTicksMsec() * 0.01f);
		Color outer = new Color(0.09f, 0.06f, 0.02f, 0.98f);
		Color rim = new Color(1.0f, 0.88f + 0.10f * pulse, 0.38f + 0.30f * pulse, 0.96f);
		Color bgColor = new Color(0.86f + 0.06f * pulse, 0.62f + 0.08f * pulse, 0.14f);
		if (IsPressed())
		{
			bgColor = new Color(0.66f, 0.46f, 0.10f);
		}
		else if (IsHovered())
		{
			bgColor = new Color(1.0f, 0.84f, 0.30f);
			rim = new Color(1.0f, 0.98f, 0.78f, 1.0f);
		}

		DrawRect(new Rect2(1, 2, Size.X, Size.Y), new Color(0f, 0f, 0f, 0.45f), true);
		DrawRect(new Rect2(Vector2.Zero, Size), outer, true);
		DrawRect(new Rect2(1, 1, Size.X - 2, Size.Y - 2), rim, false, 1.2f);
		DrawRect(new Rect2(2, 2, Size.X - 4, Size.Y - 4), bgColor, true);
		DrawRect(new Rect2(2, 2, Size.X - 4, 2), new Color(1f, 0.96f, 0.72f, 0.82f), true);

		Color ink = new Color(0.09f, 0.05f, 0.02f);
		float cx = Mathf.Floor(Size.X * 0.5f);
		float cy = Mathf.Floor(Size.Y * 0.5f);
		// Crisp bold upward pixel arrow (unambiguous Level-Up badge)
		DrawRect(new Rect2(cx - 1.5f, cy - 6.0f, 3.0f, 2.0f), ink);
		DrawRect(new Rect2(cx - 3.5f, cy - 4.0f, 7.0f, 2.0f), ink);
		DrawRect(new Rect2(cx - 5.5f, cy - 2.0f, 11.0f, 2.0f), ink);
		DrawRect(new Rect2(cx - 2.0f, cy + 0.0f, 4.0f, 5.5f), ink);
	}
}
