using Godot;
using System;
using System.Collections.Generic;

public partial class AbilitySlotUI : Control
{
	private int _abilityId; // 1 = Fireball, 2 = Passive Shield
	private HUD _hud;
	private Player _player;

	private AbilityCircle _circle;
	private Button _upgradeButton;

	public AbilitySlotUI(int abilityId, HUD hud)
	{
		_abilityId = abilityId;
		_hud = hud;
		CustomMinimumSize = new Vector2(16, 16);
		Size = new Vector2(16, 16);
	}

	public override void _Ready()
	{
		_player = GetTree().GetFirstNodeInGroup("Player") as Player;

		// 1. Создаем круглую иконку способности
		_circle = new AbilityCircle(_abilityId, this);
		_circle.Size = new Vector2(24, 24);
		_circle.Position = Vector2.Zero;
		_circle.Scale = new Vector2(0.84f, 0.84f); // +20% к прежним 0.7
		AddChild(_circle);

		// 2. Создаем круглую кнопку "+" для прокачки (размер 16x9, позиция (0, 34))
		_upgradeButton = new AbilityUpgradeButton();
		_upgradeButton.CustomMinimumSize = new Vector2(16, 9);
		_upgradeButton.Size = new Vector2(16, 9);
		_upgradeButton.Position = new Vector2(0, 34); // Смещено на 50 пикселей вниз от -16
		
		// Настраиваем пустой стиль, так как рисуем сами в _Draw
		var styleEmpty = new StyleBoxEmpty();
		_upgradeButton.AddThemeStyleboxOverride("normal", styleEmpty);
		_upgradeButton.AddThemeStyleboxOverride("hover", styleEmpty);
		_upgradeButton.AddThemeStyleboxOverride("pressed", styleEmpty);
		_upgradeButton.AddThemeStyleboxOverride("focus", styleEmpty);

		_upgradeButton.Pressed += OnUpgradePressed;
		AddChild(_upgradeButton);

		UpdateState();
	}

	public void UpdateState()
	{
		if (!GodotObject.IsInstanceValid(_player))
		{
			_player = GetTree().GetFirstNodeInGroup("Player") as Player;
		}

		if (_player == null || _upgradeButton == null || _circle == null) return;

		int currentLevel = _abilityId == 1 ? _player.Ability1Level : _player.Ability2Level;
		bool hasPoints = _player.SkillPoints > 0;
		bool isMaxLevel = currentLevel >= 4; // Максимальный уровень 4

		// Показываем плюс только если есть неиспользованные очки умений и уровень не максимальный
		_upgradeButton.Visible = hasPoints && !isMaxLevel;
		
		_circle.QueueRedraw();
	}

	private void OnUpgradePressed()
	{
		if (_player == null || _player.SkillPoints <= 0) return;

		_player.SkillPoints--;

		if (_abilityId == 1)
		{
			_player.Ability1Level++;
			GD.Print($"Прокачан Огненный шар! Уровень: {_player.Ability1Level}");
		}
		else
		{
			_player.Ability2Level++;
			GD.Print($"Прокачан Щит! Уровень: {_player.Ability2Level}");
		}

		// Обновляем весь HUD
		_hud.UpdateAbilitiesUI();
	}
}

// Кастомный элемент отрисовки круглой кнопки с иконкой и КД
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
		float cooldown = _abilityId == 1 ? _player.FireballCooldownTimer : 0.0f;
		float maxCooldown = _abilityId == 1 ? Player.FireballCooldownMax : 1.0f;

		Vector2 center = new Vector2(12, 12);
		float radius = 11.0f;

		// 1. Отрисовка темного фона
		DrawCircle(center, radius, new Color(0.12f, 0.12f, 0.12f, 0.85f));

		// 2. Отрисовка рамки (золотая если прокачана, серая если заблокирована)
		Color borderColor = lvl > 0 ? new Color(0.85f, 0.65f, 0.15f) : new Color(0.35f, 0.35f, 0.35f);
		DrawArc(center, radius, 0f, Mathf.Tau, 32, borderColor, 1.0f, true);

		// 3. Рисуем иконки
		if (_abilityId == 1)
		{
			// Иконка Огня (Flame symbol)
			Vector2[] points = new Vector2[] {
				new Vector2(12, 6),
				new Vector2(15, 10),
				new Vector2(16, 14),
				new Vector2(14, 18),
				new Vector2(10, 18),
				new Vector2(8, 14),
				new Vector2(9, 10),
				new Vector2(12, 6)
			};
			
			Color fireColor = lvl > 0 ? new Color(1.0f, 0.4f, 0.0f) : new Color(0.5f, 0.5f, 0.5f);
			DrawColoredPolygon(points, fireColor);

			// Сердцевина пламени
			if (lvl > 0)
			{
				Vector2[] innerPoints = new Vector2[] {
					new Vector2(12, 10),
					new Vector2(14, 13),
					new Vector2(13, 16),
					new Vector2(11, 16),
					new Vector2(10, 13),
					new Vector2(12, 10)
				};
				DrawColoredPolygon(innerPoints, new Color(1.0f, 0.85f, 0.2f));
			}
		}
		else
		{
			// Иконка Щита (Shield outline & color fill)
			Vector2[] shieldPoints = new Vector2[] {
				new Vector2(12, 6),   // верх центр
				new Vector2(17, 7),   // верх право
				new Vector2(17, 12),  // право
				new Vector2(12, 18),  // низ центр
				new Vector2(7, 12),   // лево
				new Vector2(7, 7),    // верх лево
				new Vector2(12, 6)    // верх центр
			};
			Color shieldColor = lvl > 0 ? new Color(0.2f, 0.5f, 0.85f) : new Color(0.5f, 0.5f, 0.5f);
			DrawColoredPolygon(shieldPoints, shieldColor);

			// Внутренняя обводка
			if (lvl > 0)
			{
				DrawPolyline(shieldPoints, new Color(0.7f, 0.9f, 1.0f), 0.75f, true);
			}
		}

		// 4. Затемнение для заблокированной способности (lvl == 0)
		if (lvl == 0)
		{
			DrawCircle(center, radius, new Color(0, 0, 0, 0.45f));
		}

		// 5. Оверлей Cooldown (темный сектор)
		if (cooldown > 0.0f)
		{
			if (cooldown / maxCooldown >= 0.99f)
			{
				DrawCircle(center, radius, new Color(0, 0, 0, 0.65f));
			}
			else
			{
				float angle = (cooldown / maxCooldown) * Mathf.Tau;
				
				// Отрисовываем темную маску перезарядки
				int segments = 24;
				var vertices = new List<Vector2> { center };
				
				// Стартуем сверху (-90 градусов / -PI/2)
				float startAngle = -Mathf.Pi / 2.0f;
				for (int i = 0; i <= segments; i++)
				{
					float currentAngle = startAngle + angle * (i / (float)segments);
					Vector2 vertex = center + new Vector2(Mathf.Cos(currentAngle), Mathf.Sin(currentAngle)) * radius;
					
					// Filter out vertices that are extremely close to prevent duplicate/collinear vertices
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
						colors[i] = new Color(0, 0, 0, 0.65f);
					}
					DrawPolygon(vertices.ToArray(), colors);
				}
			}

			// Отображаем секунды КД текстом
			string cdText = string.Format("{0:0.0}", cooldown);
			var font = GD.Load<Font>("res://assets/fonts/silver/Silver.ttf");
			if (font != null)
			{
				Vector2 textSize = font.GetStringSize(cdText, HorizontalAlignment.Center, -1, 10);
				DrawString(font, center - new Vector2(textSize.X / 2.0f, -4), cdText, HorizontalAlignment.Center, -1, 10, new Color(1, 1, 1));
			}
		}

		// 6. Отрисовываем точки уровня (Level Dots) внизу
		if (lvl > 0)
		{
			float dotY = 20.0f;
			float spacing = 3.0f;
			float startX = 12.0f - ((lvl - 1) * spacing) / 2.0f;

			for (int i = 0; i < lvl; i++)
			{
				float dotX = startX + i * spacing;
				DrawCircle(new Vector2(dotX, dotY), 1.0f, new Color(0.2f, 1.0f, 0.2f)); // зелененькие точки уровня
			}
		}

		// 7. Горячая клавиша
		var keyFont = GD.Load<Font>("res://assets/fonts/silver/Silver.ttf");
		if (keyFont != null && cooldown <= 0.0f)
		{
			string keyText = _abilityId == 1 ? "Q" : "V";
			// Рисуем в правом нижнем углу
			Vector2 textPos = new Vector2(17, 21);
			DrawString(keyFont, textPos, keyText, HorizontalAlignment.Right, -1, 8, new Color(0.9f, 0.9f, 0.9f));
		}
	}
}

public partial class AbilityUpgradeButton : Button
{
	public override void _Draw()
	{
		// Draw background based on state (hover, pressed, normal)
		Color bgColor = new Color(0.95f, 0.75f, 0.15f); // Gold
		if (IsPressed()) {
			bgColor = new Color(0.8f, 0.6f, 0.1f);
		} else if (IsHovered()) {
			bgColor = new Color(1.0f, 0.85f, 0.25f);
		}

		// Draw rounded rectangle background manually
		DrawRect(new Rect2(Vector2.Zero, Size), bgColor, true);

		// Draw pixel-perfect plus sign centered in 16x9 button
		Color plusColor = new Color(0, 0, 0); // Black plus
		DrawRect(new Rect2(6, 4, 5, 1), plusColor); // Horizontal bar
		DrawRect(new Rect2(8, 2, 1, 5), plusColor); // Vertical bar
	}
}
