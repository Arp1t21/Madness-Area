using Godot;
using System;
using System.Collections.Generic;

public partial class AbilitySlotUI : Control
{
	private int _abilityId; // 1 = Fireball, 2 = Arcane Shield & Dash
	private HUD _hud;
	private Player _player;

	private AbilityCircle _circle;
	private Button _upgradeButton;

	public AbilitySlotUI(int abilityId, HUD hud)
	{
		_abilityId = abilityId;
		_hud = hud;
		CustomMinimumSize = new Vector2(26, 26);
		Size = new Vector2(26, 26);
	}

	public override void _Ready()
	{
		_player = GetTree().GetFirstNodeInGroup("Player") as Player;

		_circle = new AbilityCircle(_abilityId, this);
		_circle.Size = new Vector2(24, 24);
		_circle.Position = Vector2.Zero;
		_circle.Scale = new Vector2(0.84f * 1.3f, 0.84f * 1.3f);
		AddChild(_circle);

		_upgradeButton = new AbilityUpgradeButton();
		_upgradeButton.CustomMinimumSize = new Vector2(21, 12);
		_upgradeButton.Size = new Vector2(21, 12);
		_upgradeButton.Position = new Vector2(2.5f, -14);

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
		if (!GodotObject.IsInstanceValid(_player))
		{
			_player = GetTree().GetFirstNodeInGroup("Player") as Player;
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
	}

	private string _lastBindText = "";
	private bool _wasActiveLastFrame = false;

	private void OnUpgradePressed()
	{
		if (_player == null || _player.SkillPoints <= 0) return;

		int currentLevel = _abilityId == 1 ? _player.Ability1Level : _player.Ability2Level;
		if (!GameLogic.CanUpgradeAbility(currentLevel)) return;

		_player.SkillPoints--;

		if (_abilityId == 1)
		{
			_player.Ability1Level = GameLogic.ClampAbilityLevel(_player.Ability1Level + 1);
		}
		else
		{
			_player.Ability2Level = GameLogic.ClampAbilityLevel(_player.Ability2Level + 1);
		}

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
		_player = GetTree().GetFirstNodeInGroup("Player") as Player;
	}

	public override void _Draw()
	{
		if (!GodotObject.IsInstanceValid(_player))
		{
			_player = GetTree().GetFirstNodeInGroup("Player") as Player;
		}

		if (_player == null) return;

		int lvl = _abilityId == 1 ? _player.Ability1Level : _player.Ability2Level;
		float cooldown = _abilityId == 1 ? _player.FireballCooldownTimer : _player.ShieldCooldownTimer;
		float maxCooldown = _abilityId == 1 ? Player.FireballCooldownMax : Player.ShieldCooldownMax;

		Vector2 center = new Vector2(12, 12);
		float radius = 11.2f;
		bool isUnlocked = lvl > 0;
		bool isReady = isUnlocked && cooldown <= 0.0f;

		// 1. Внешняя тень и обсидиановый медальон
		DrawCircle(center + new Vector2(0.8f, 1.2f), radius + 0.4f, new Color(0f, 0f, 0f, 0.55f));
		DrawCircle(center, radius, new Color(0.06f, 0.065f, 0.10f, 0.96f));
		DrawCircle(center, radius - 2.0f, isUnlocked ? new Color(0.11f, 0.115f, 0.17f, 0.95f) : new Color(0.08f, 0.08f, 0.11f, 0.9f));

		// Мягкое фоновое свечение при готовности
		if (isReady)
		{
			Color aura = _abilityId == 1 ? new Color(1.0f, 0.45f, 0.12f, 0.14f) : new Color(0.24f, 0.78f, 1.0f, 0.14f);
			DrawCircle(center, radius - 2.2f, aura);
		}

		// 2. Двойная окантовка (бронза/циан при активности)
		Color borderColor = !isUnlocked
			? new Color(0.28f, 0.30f, 0.36f)
			: (_abilityId == 2 && _player.IsShieldActive
				? new Color(0.32f, 0.96f, 1.0f)
				: (isReady ? DungeonVisuals.BrassGold : DungeonVisuals.BrassDim));
		DrawArc(center, radius, 0f, Mathf.Tau, 32, borderColor, 1.4f, false);
		DrawArc(center, radius - 1.6f, 0f, Mathf.Tau, 28, new Color(1f, 1f, 1f, isReady ? 0.14f : 0.05f), 0.7f, false);

		// 3. Детализированные эмблемы способностей
		if (_abilityId == 1)
		{
			// Многослойное пламя огненного шара
			Vector2[] outerFlame = new Vector2[]
			{
				new Vector2(12, 4.8f),
				new Vector2(15.2f, 8.2f),
				new Vector2(16.6f, 12.2f),
				new Vector2(14.8f, 16.2f),
				new Vector2(9.2f, 16.2f),
				new Vector2(7.4f, 12.2f),
				new Vector2(8.8f, 8.2f),
			};
			Color outerColor = isUnlocked ? new Color(0.98f, 0.38f, 0.08f) : new Color(0.38f, 0.36f, 0.38f);
			DrawColoredPolygon(outerFlame, outerColor);

			if (isUnlocked)
			{
				Vector2[] midFlame = new Vector2[]
				{
					new Vector2(12, 7.0f),
					new Vector2(14.5f, 10.6f),
					new Vector2(13.8f, 15.2f),
					new Vector2(10.2f, 15.2f),
					new Vector2(9.5f, 10.6f),
				};
				DrawColoredPolygon(midFlame, new Color(1.0f, 0.72f, 0.18f));

				DrawCircle(new Vector2(12, 13.0f), 1.8f, new Color(1.0f, 0.96f, 0.72f));
				DrawCircle(new Vector2(9.0f, 7.6f), 0.8f, new Color(1.0f, 0.75f, 0.30f, 0.85f));
			}
		}
		else
		{
			// Щит Эгиды с руническим крестом
			Vector2[] shieldOuter = new Vector2[]
			{
				new Vector2(12, 5.2f),
				new Vector2(16.8f, 6.6f),
				new Vector2(16.4f, 11.8f),
				new Vector2(12, 16.8f),
				new Vector2(7.6f, 11.8f),
				new Vector2(7.2f, 6.6f),
			};
			Color shieldColor = isUnlocked ? new Color(0.16f, 0.52f, 0.90f) : new Color(0.35f, 0.38f, 0.44f);
			DrawColoredPolygon(shieldOuter, shieldColor);

			if (isUnlocked)
			{
				Vector2[] outlinePts = new Vector2[]
				{
					shieldOuter[0], shieldOuter[1], shieldOuter[2], shieldOuter[3], shieldOuter[4], shieldOuter[5], shieldOuter[0]
				};
				DrawPolyline(outlinePts, new Color(0.65f, 0.92f, 1.0f), 1.0f, false);
				// Внутренний светящийся крест
				DrawLine(new Vector2(12, 7.2f), new Vector2(12, 14.8f), new Color(0.82f, 0.97f, 1.0f), 1.4f);
				DrawLine(new Vector2(8.8f, 10.2f), new Vector2(15.2f, 10.2f), new Color(0.82f, 0.97f, 1.0f), 1.4f);
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
				Vector2 textSize = font.GetStringSize(cdText, HorizontalAlignment.Center, -1, 10);
				Vector2 textOrigin = center - new Vector2(textSize.X / 2.0f, -4);
				DrawString(font, textOrigin + new Vector2(1, 1), cdText, HorizontalAlignment.Center, -1, 10, new Color(0, 0, 0, 0.9f));
				DrawString(font, textOrigin, cdText, HorizontalAlignment.Center, -1, 10, Colors.White);
			}
		}

		// 6. Гнезда уровня (4 ячеек по низу, сдвинуты левее от клавиши)
		if (isUnlocked)
		{
			const int maxLvl = 4;
			float spacing = 3.2f;
			float startX = 9.0f - ((maxLvl - 1) * spacing) * 0.5f;
			float dotY = 20.5f;

			for (int i = 0; i < maxLvl; i++)
			{
				float dotX = startX + i * spacing;
				bool filled = i < lvl;
				DrawRect(new Rect2(dotX - 1.3f, dotY - 1.3f, 2.6f, 2.6f), new Color(0.04f, 0.04f, 0.06f, 0.95f));
				Color gemColor = filled ? new Color(0.32f, 0.95f, 0.52f) : new Color(0.22f, 0.24f, 0.30f);
				DrawRect(new Rect2(dotX - 0.8f, dotY - 0.8f, 1.6f, 1.6f), gemColor);
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

			Rect2 badge = new Rect2(15.5f, 15.5f, 8.5f, 8.5f);
			DrawRect(badge, new Color(0.05f, 0.055f, 0.085f, 0.95f));
			DrawRect(badge, isReady ? DungeonVisuals.BrassGold : DungeonVisuals.IronBorder, false, 0.9f);
			Color keyColor = isReady ? DungeonVisuals.Parchment : new Color(0.58f, 0.60f, 0.68f);
			DrawString(keyFont, new Vector2(19.8f, 22.2f), keyText, HorizontalAlignment.Center, -1, 8, keyColor);
		}
	}
}

public partial class AbilityUpgradeButton : Button
{
	public override void _Draw()
	{
		Color outer = new Color(0.10f, 0.07f, 0.02f, 0.95f);
		Color bgColor = new Color(0.95f, 0.76f, 0.22f);
		if (IsPressed())
		{
			bgColor = new Color(0.75f, 0.56f, 0.12f);
		}
		else if (IsHovered())
		{
			bgColor = new Color(1.0f, 0.88f, 0.38f);
		}

		DrawRect(new Rect2(Vector2.Zero, Size), outer, true);
		DrawRect(new Rect2(1, 1, Size.X - 2, Size.Y - 2), bgColor, true);
		DrawRect(new Rect2(1, 1, Size.X - 2, 1), new Color(1f, 0.98f, 0.75f, 0.65f), true);

		Color plusColor = new Color(0.08f, 0.05f, 0.02f);
		float cx = Mathf.Floor(Size.X / 2.0f);
		float cy = Mathf.Floor(Size.Y / 2.0f);
		DrawRect(new Rect2(cx - 3, cy - 0.6f, 7, 1.6f), plusColor);
		DrawRect(new Rect2(cx - 0.6f, cy - 3, 1.6f, 7), plusColor);
	}
}
