using Godot;
using System;

/// <summary>
/// Базовый класс набора способностей персонажа.
/// У каждого героя (Knight, Magician, Rogue, Berserker) свой отдельный класс-наследник,
/// чтобы способности и ПКМ-атаки можно было независимо менять и настраивать.
/// </summary>
public abstract class CharacterAbilitySet
{
	protected readonly Player Player;
	protected readonly PackedScene FireballScene;
	protected readonly AudioStream BuffSound;

	public virtual string Ability1Name => "Fireball";
	public virtual string Ability2Name => "Arcane Shield";

	public virtual float Ability1CooldownMax => 10.0f;
	public virtual float Ability2CooldownMax => 8.0f;

	public virtual float Ability1ManaCost => 20.0f;
	public virtual float Ability2ManaCost => 15.0f;

	protected CharacterAbilitySet(Player player)
	{
		Player = player;
		FireballScene = GD.Load<PackedScene>("res://scene/abilities/fireball.tscn");
		BuffSound = GD.Load<AudioStream>("res://assets/sounds/buff_collecting.wav");
	}

	public virtual void UseAbility1()
	{
		if (Player.Ability1Level <= 0 || Player.FireballCooldownTimer > 0.0f) return;

		if (Player.CurrentMana < Ability1ManaCost)
		{
			Player.ShowNoManaWarning();
			return;
		}

		Player.CurrentMana -= Ability1ManaCost;
		Player.FireballCooldownTimer = Ability1CooldownMax;

		Vector2 mousePos = Player.GetGlobalMousePosition();
		Vector2 dir = (mousePos - Player.GlobalPosition).Normalized();
		if (dir == Vector2.Zero) dir = Vector2.Right;
		float damage = (Player.FireballBaseDamage + (Player.Ability1Level - 1) * Player.FireballDamagePerLevel) * Player.WeaponDamageMultiplier;

		if (Player.IsCoopSession)
		{
			Player.BroadcastSpawnFireball(Player.GlobalPosition, dir, damage);
		}
		else
		{
			ExecuteFireballVisualAndSpawn(Player.GlobalPosition, dir, damage);
		}
	}

	public void ExecuteFireballVisualAndSpawn(Vector2 spawnPos, Vector2 dir, float damage)
	{
		Fireball fireball = FireballScene != null ? FireballScene.Instantiate<Fireball>() : new Fireball();
		fireball.GlobalPosition = spawnPos;
		fireball.Direction = dir;
		fireball.Damage = damage;
		fireball.OwnerPlayer = Player;
		Player.GetParent()?.AddChild(fireball);
	}

	public virtual void UseAbility2()
	{
		if (Player.Ability2Level <= 0 || Player.ShieldCooldownTimer > 0.0f) return;

		if (Player.CurrentMana < Ability2ManaCost)
		{
			Player.ShowNoManaWarning();
			return;
		}

		Player.CurrentMana -= Ability2ManaCost;
		Player.ShieldCooldownTimer = Ability2CooldownMax;

		Vector2 moveDir = Input.GetVector("move_left", "move_right", "move_up", "move_down");
		if (moveDir == Vector2.Zero)
		{
			moveDir = (Player.GetGlobalMousePosition() - Player.GlobalPosition).Normalized();
		}
		if (moveDir == Vector2.Zero) moveDir = Vector2.Right;
		Vector2 dashDir = moveDir.Normalized();
		Player.SetDashVelocity(dashDir * 228.0f);

		float shieldDuration = 2.5f + (Player.Ability2Level - 1) * 0.7f;
		float pulseDamage = (4.0f + Player.Ability2Level * 3.0f) * Player.WeaponDamageMultiplier;
		bool hasFrostAegis = Player.HasRelic(GameLogic.RelicFrostAegis);

		if (Player.IsCoopSession)
		{
			Player.BroadcastActivateShieldPulse(dashDir, shieldDuration, pulseDamage, hasFrostAegis);
		}
		else
		{
			ExecuteShieldPulseVisualAndHit(dashDir, shieldDuration, pulseDamage, hasFrostAegis);
		}
	}

	public void ExecuteShieldPulseVisualAndHit(Vector2 dashDir, float shieldDuration, float pulseDamage, bool hasFrostAegis)
	{
		Player.ActivateShieldEffect(shieldDuration);
		Player.PlayDashVisuals(dashDir);

		float pulseRadius = 52.0f;
		float effectiveRadius = hasFrostAegis ? pulseRadius * 1.25f : pulseRadius;
		bool isServerAuthority = NetworkManager.Instance == null || NetworkManager.Instance.IsServerAuthority;
		foreach (Node node in Player.GetTree().GetNodesInGroup("Enemies"))
		{
			if (node is Enemy enemy && GodotObject.IsInstanceValid(enemy) && enemy.CurrentHealth > 0)
			{
				if (Player.GlobalPosition.DistanceTo(enemy.GlobalPosition) <= effectiveRadius)
				{
					if (isServerAuthority)
					{
						enemy.TakeDamage(pulseDamage, Player.GlobalPosition, 16.0f, false, false, Player);
						if (hasFrostAegis)
						{
							enemy.ApplyStatusEffect(StatusEffectType.Stun, 0.9f, 1.0f);
							enemy.ApplyStatusEffect(StatusEffectType.Slow, 3.0f, 0.40f);
						}
					}
					DungeonVisuals.SpawnImpactSparks(
						Player.GetParent(),
						enemy.GlobalPosition + new Vector2(0, -6),
						enemy.GlobalPosition - Player.GlobalPosition,
						hasFrostAegis ? new Color(0.65f, 0.96f, 1.0f) : new Color(0.45f, 0.92f, 1.0f),
						hasFrostAegis ? 12 : 8,
						50.0f,
						75.0f,
						hasFrostAegis ? 1.15f : 0.9f);
				}
			}
		}

		if (BuffSound != null)
		{
			GameResourceCache.PlayPooledSound(Player.GetTree(), BuffSound, "SFX");
		}

		Player.QueueRedraw();
	}

	public virtual float ApplyPassiveAndActiveDefense(float incomingDamage, ref float knockForce)
	{
		if (Player.Ability2Level > 0)
		{
			float shieldRes = 0.05f + (Player.Ability2Level - 1) * 0.05f;
			incomingDamage *= (1.0f - shieldRes);
		}

		if (Player.IsShieldActive)
		{
			incomingDamage *= 0.4f;
			knockForce = 0.0f;
		}

		return incomingDamage;
	}

	// Специальная атака на Правую Кнопку Мыши (ПКМ).
	// По умолчанию у персонажей, которым спец-атака еще не назначена, ничего не происходит.
	public virtual void UpdateSpecialAttack(double delta, bool isRmbPressed)
	{
	}

	public virtual void CancelSpecialAttack()
	{
	}

	public virtual void DrawSpecialAttackIndicator(Player player)
	{
	}

	public virtual bool IsNormalAttackBlocked => false;

	public static CharacterAbilitySet CreateForCharacter(Player player, string characterName)
	{
		if (!string.IsNullOrEmpty(characterName))
		{
			if (characterName.Equals("Magician", StringComparison.OrdinalIgnoreCase))
				return new MagicianAbilitySet(player);
			if (characterName.Equals("Rogue", StringComparison.OrdinalIgnoreCase))
				return new RogueAbilitySet(player);
			if (characterName.Equals("Berserker", StringComparison.OrdinalIgnoreCase))
				return new BerserkerAbilitySet(player);
		}

		return new KnightAbilitySet(player);
	}
}

/// <summary>
/// Способности Рыцаря (Knight).
/// Спец-атака на ПКМ пока не назначена.
/// </summary>
public class KnightAbilitySet : CharacterAbilitySet
{
	public KnightAbilitySet(Player player) : base(player)
	{
	}
}

/// <summary>
/// Способности Мага (Magician).
/// </summary>
public class MagicianAbilitySet : CharacterAbilitySet
{
	public MagicianAbilitySet(Player player) : base(player)
	{
	}
}

/// <summary>
/// Способности Разбойника (Rogue).
/// </summary>
public class RogueAbilitySet : CharacterAbilitySet
{
	public RogueAbilitySet(Player player) : base(player)
	{
	}
}

/// <summary>
/// Способности Берсеркера (Berserker).
/// Специальная атака на ПКМ: зарядка оружия ближнего боя (5 компактных заполняющихся квадратиков над головой
/// с анимацией полного заряда на 5-м делении) и бросок по дуговой траектории бумеранга.
/// </summary>
public class BerserkerAbilitySet : CharacterAbilitySet
{
	// В 1.05 раза быстрее прежних 1.20 сек (~1.143 сек до полного заряда)
	private const float MaxChargeSeconds = 1.20f / 1.05f;
	private bool _isCharging = false;
	private float _chargeSeconds = 0.0f;
	private float _fullChargeAnimTime = 0.0f;
	private bool _isBoomerangInFlight = false;

	public override bool IsNormalAttackBlocked => _isCharging || _isBoomerangInFlight;

	public BerserkerAbilitySet(Player player) : base(player)
	{
	}

	public override void CancelSpecialAttack()
	{
		if (_isCharging)
		{
			ResetChargeState();
		}
	}

	private void ResetChargeState()
	{
		_isCharging = false;
		_chargeSeconds = 0.0f;
		_fullChargeAnimTime = 0.0f;
		Player.QueueRedraw();
	}

	public override void UpdateSpecialAttack(double delta, bool isRmbPressed)
	{
		if (!Player.IsLocalPlayer || Player.IsDead)
		{
			if (_isCharging) ResetChargeState();
			return;
		}
		if (_isBoomerangInFlight)
		{
			if (_isCharging) ResetChargeState();
			return;
		}

		AttackController activeSlot = (Player.CurrentSlotIndex >= 0 && Player.CurrentSlotIndex < Player.WeaponSlots.Count)
			? Player.WeaponSlots[Player.CurrentSlotIndex]
			: null;
		WeaponData activeWeapon = activeSlot?.CurrentWeapon;
		bool hasMeleeWeapon = activeWeapon != null && !activeWeapon.IsRanged;

		if (!hasMeleeWeapon)
		{
			if (_isCharging) ResetChargeState();
			return;
		}

		if (isRmbPressed)
		{
			_isCharging = true;
			_chargeSeconds = Mathf.Min(MaxChargeSeconds, _chargeSeconds + (float)delta);
			if (_chargeSeconds >= MaxChargeSeconds)
			{
				_fullChargeAnimTime += (float)delta;
			}
			else
			{
				_fullChargeAnimTime = 0.0f;
			}
			Player.QueueRedraw();
		}
		else if (_isCharging)
		{
			float chargeRatio = Mathf.Clamp(_chargeSeconds / MaxChargeSeconds, 0.0f, 1.0f);
			ResetChargeState();

			// Даже если атака заряжена не полностью, топор всегда вылетает (на меньшую дистанцию)
			LaunchBoomerang(activeSlot, activeWeapon, chargeRatio);
		}
	}

	private void LaunchBoomerang(AttackController activeSlot, WeaponData weapon, float chargeRatio)
	{
		int slotIdx = Player.WeaponSlots.IndexOf(activeSlot);
		// Дистанция полета уменьшена на 0.7 раз, скорость полета и возврата увеличена в 2 раза
		float minDist = (85.0f / 1.2f) * 0.7f;
		float maxDist = (320.0f / 1.2f) * 0.7f;
		float throwDistance = Mathf.Lerp(minDist, maxDist, chargeRatio);
		float speed = Mathf.Lerp(145.0f, 200.0f, chargeRatio);

		// Урон оружия при метании остается таким же, как у самого оружия
		float damage = weapon.Damage * Player.WeaponDamageMultiplier;
		float knockback = weapon.KnockbackForce;

		Vector2 dir = (Player.GetGlobalMousePosition() - Player.GlobalPosition).Normalized();
		if (dir == Vector2.Zero) dir = Vector2.Right;

		if (Player.IsCoopSession)
		{
			Player.BroadcastThrowBoomerang(slotIdx, dir, throwDistance, speed, damage, knockback);
		}
		else
		{
			SpawnBoomerangLocal(slotIdx, dir, throwDistance, speed, damage, knockback);
		}
	}

	public void SpawnBoomerangLocal(int slotIdx, Vector2 dir, float throwDistance, float speed, float damage, float knockback)
	{
		AttackController activeSlot = (slotIdx >= 0 && slotIdx < Player.WeaponSlots.Count)
			? Player.WeaponSlots[slotIdx]
			: null;
		WeaponData weapon = activeSlot?.CurrentWeapon;
		if (weapon == null) return;

		_isBoomerangInFlight = true;
		if (activeSlot != null)
		{
			activeSlot.Visible = false;
		}

		Texture2D weaponTex = WeaponShowcaseSlot.ExtractWeaponTextureStatic(weapon);

		var boomerang = new BoomerangWeaponProjectile();
		boomerang.Setup(
			Player,
			activeSlot,
			weaponTex,
			dir,
			throwDistance,
			speed,
			damage,
			knockback,
			() =>
			{
				_isBoomerangInFlight = false;
				if (activeSlot != null && GodotObject.IsInstanceValid(activeSlot) &&
					Player != null && GodotObject.IsInstanceValid(Player))
				{
					int idx = Player.WeaponSlots.IndexOf(activeSlot);
					if (idx != -1 && Player.CurrentSlotIndex == idx)
					{
						activeSlot.Visible = true;
					}
				}
			});

		Player.GetParent()?.AddChild(boomerang);
	}

	public override void DrawSpecialAttackIndicator(Player player)
	{
		if (!_isCharging) return;

		const int totalSquares = 5;
		const float sqSize = 2.35f; // Компактнее прежних 3.2f
		const float gap = 1.15f;
		float totalWidth = totalSquares * sqSize + (totalSquares - 1) * gap;
		float startX = -totalWidth * 0.5f;
		float y = -26.5f; // Расположены чуть выше над головой героя

		// Гарантируем, что с первой же доли секунды зажатия 1-й квадрат уже начинает светиться
		float rawRatio = Mathf.Clamp(_chargeSeconds / MaxChargeSeconds, 0.0f, 1.0f);
		float displayRatio = Mathf.Lerp(0.18f, 1.0f, rawRatio);
		bool isFullyCharged = rawRatio >= 0.999f;

		Color greyBg = new Color(0.24f, 0.26f, 0.30f, 0.88f);
		Color outlineColor = new Color(0.05f, 0.05f, 0.07f, 0.96f);

		// Анимация полного заряда (когда заполнился 5-й квадрат)
		if (isFullyCharged)
		{
			// 1) Расходящаяся золотая волна в момент достижения 100% заряда
			if (_fullChargeAnimTime < 0.38f)
			{
				float burstT = _fullChargeAnimTime / 0.38f;
				float expand = burstT * 3.8f;
				float waveAlpha = (1.0f - burstT) * 0.92f;
				Rect2 waveRect = new Rect2(
					startX - 1.2f - expand,
					y - 1.2f - expand * 0.65f,
					totalWidth + 2.4f + expand * 2.0f,
					sqSize + 2.4f + expand * 1.3f);
				player.DrawRect(waveRect, new Color(1.0f, 0.88f, 0.32f, waveAlpha), false, 0.85f);
			}

			// 2) Пульсирующий золотисто-огненный ореол вокруг всей шкалы при полном заряде
			float glowPulse = 0.5f + 0.5f * Mathf.Sin(_fullChargeAnimTime * 16.0f);
			Rect2 glowRect = new Rect2(startX - 1.1f, y - 1.1f, totalWidth + 2.2f, sqSize + 2.2f);
			player.DrawRect(glowRect, new Color(1.0f, 0.78f, 0.20f, 0.35f + 0.35f * glowPulse), false, 0.8f);
		}

		for (int i = 0; i < totalSquares; i++)
		{
			// Прогресс заполнения конкретного i-го квадратика (от 0.0 до 1.0)
			float segFill = Mathf.Clamp(displayRatio * totalSquares - i, 0.0f, 1.0f);
			if (i == 0 && segFill < 0.85f)
			{
				segFill = Mathf.Max(segFill, 0.85f);
			}

			// Небольшой "прыжок" масштаба квадратика в момент заполнения и общая пульсация на 100% заряде
			float popScale = 1.0f;
			if (isFullyCharged)
			{
				popScale = 1.0f + 0.14f * Mathf.Sin(_fullChargeAnimTime * 15.0f + i * 0.35f);
			}
			else if (segFill > 0.0f && segFill < 1.0f)
			{
				popScale = 1.0f + 0.12f * Mathf.Sin(segFill * Mathf.Pi);
			}

			float drawSize = sqSize * popScale;
			float offset = (drawSize - sqSize) * 0.5f;
			float x = startX + i * (sqSize + gap) - offset;
			float sqY = y - offset;

			Rect2 outerRect = new Rect2(x - 0.45f, sqY - 0.45f, drawSize + 0.9f, drawSize + 0.9f);
			Rect2 bgRect = new Rect2(x, sqY, drawSize, drawSize);

			player.DrawRect(outerRect, isFullyCharged ? new Color(1.0f, 0.55f, 0.12f, 0.98f) : outlineColor);
			player.DrawRect(bgRect, greyBg);

			if (segFill > 0.001f)
			{
				float fillH = drawSize * segFill;
				Rect2 fillRect = new Rect2(x, sqY + (drawSize - fillH), drawSize, fillH);

				Color fillCol;
				if (isFullyCharged)
				{
					float flash = 0.5f + 0.5f * Mathf.Sin(_fullChargeAnimTime * 18.0f);
					fillCol = new Color(1.0f, 0.84f, 0.25f).Lerp(new Color(1.0f, 0.98f, 0.80f), flash);
				}
				else if (segFill >= 0.99f)
				{
					fillCol = new Color(1.0f, 0.92f, 0.44f, 1.0f);
				}
				else
				{
					fillCol = new Color(0.95f, 0.72f, 0.24f, 0.95f);
				}

				player.DrawRect(fillRect, fillCol);
			}
		}
	}
}
