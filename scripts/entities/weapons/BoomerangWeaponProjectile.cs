using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// Специальная атака (ПКМ) Берсеркера: заряжаемый бросок оружия ближнего боя по дуговой траектории бумеранга.
/// Дальность полета зависит от степени заряда ПКМ, после чего оружие по обратной дуге гарантированно возвращается в руки.
/// </summary>
public partial class BoomerangWeaponProjectile : Node2D
{
	private Player _ownerPlayer;
	private AttackController _sourceSlot;
	private Texture2D _weaponTexture;
	private Vector2 _startPos;
	private Vector2 _apexPos;
	private Vector2 _forwardDir;
	private Vector2 _sideDir;
	private float _maxDistance;
	private float _curveWidth;
	private float _outboundDuration;
	private float _returnDuration;
	private float _phaseProgress = 0.0f;
	private float _damage;
	private float _knockback;
	private bool _returning = false;
	private float _spinAngle = 0.0f;
	private Action _onReturnedCallback;

	private readonly HashSet<ulong> _outboundHits = new HashSet<ulong>();
	private readonly HashSet<ulong> _returnHits = new HashSet<ulong>();

	public void Setup(
		Player owner,
		AttackController sourceSlot,
		Texture2D weaponTexture,
		Vector2 direction,
		float maxDistance,
		float speed,
		float damage,
		float knockback,
		Action onReturned)
	{
		_ownerPlayer = owner;
		_sourceSlot = sourceSlot;
		_weaponTexture = weaponTexture;
		_startPos = owner.GlobalPosition + new Vector2(0, -10);
		_forwardDir = direction == Vector2.Zero ? Vector2.Right : direction.Normalized();
		// Перпендикулярный вектор для дугового отклонения бумеранга
		_sideDir = new Vector2(-_forwardDir.Y, _forwardDir.X);
		_maxDistance = Mathf.Max(40.0f, maxDistance);
		_curveWidth = _maxDistance * 0.28f;

		float effectiveSpeed = Mathf.Max(250.0f, speed);
		_outboundDuration = Mathf.Clamp(_maxDistance / effectiveSpeed, 0.16f, 0.85f);
		_returnDuration = Mathf.Clamp(_outboundDuration * 0.92f, 0.15f, 0.80f);

		_damage = damage;
		_knockback = knockback;
		_onReturnedCallback = onReturned;
		GlobalPosition = _startPos;
		Scale = new Vector2(3.0f, 3.0f);
		ZIndex = 4;
	}

	public override void _Ready()
	{
		// Гарантированно возвращаем оружие в руку при любом удалении узла/смене этажа
		TreeExiting += RestoreOwnerWeapon;
	}

	private void RestoreOwnerWeapon()
	{
		if (_onReturnedCallback != null)
		{
			var cb = _onReturnedCallback;
			_onReturnedCallback = null;
			cb.Invoke();
		}
	}

	public override void _PhysicsProcess(double delta)
	{
		float dt = (float)delta;
		_spinAngle += 25.0f * dt;
		QueueRedraw();

		if (_ownerPlayer == null || !GodotObject.IsInstanceValid(_ownerPlayer))
		{
			RestoreOwnerWeapon();
			QueueFree();
			return;
		}

		Vector2 playerHandPos = _ownerPlayer.GlobalPosition + new Vector2(0, -10);

		if (!_returning)
		{
			_phaseProgress += dt / _outboundDuration;
			float u = Mathf.Clamp(_phaseProgress, 0.0f, 1.0f);

			// Плавное замедление к вершине дуги + боковой изгиб бумеранга вправо по ходу броска
			float forwardFactor = Mathf.Sin(u * Mathf.Pi * 0.5f);
			float sideFactor = Mathf.Sin(u * Mathf.Pi);

			GlobalPosition = _startPos
				+ _forwardDir * (_maxDistance * forwardFactor)
				+ _sideDir * (_curveWidth * sideFactor);

			if (_phaseProgress >= 1.0f)
			{
				_apexPos = GlobalPosition;
				_returning = true;
				_phaseProgress = 0.0f;
			}
		}
		else
		{
			_phaseProgress += dt / _returnDuration;
			float u = Mathf.Clamp(_phaseProgress, 0.0f, 1.0f);

			// Плавное ускорение обратно в руки движущегося игрока по противоположной дуге бумеранга
			float returnFactor = 1.0f - Mathf.Cos(u * Mathf.Pi * 0.5f);
			float sideFactor = Mathf.Sin(u * Mathf.Pi);

			Vector2 basePos = _apexPos.Lerp(playerHandPos, returnFactor);
			GlobalPosition = basePos - _sideDir * (_curveWidth * sideFactor);

			if (_phaseProgress >= 1.0f || (_phaseProgress >= 0.35f && GlobalPosition.DistanceTo(playerHandPos) <= 26.0f))
			{
				RestoreOwnerWeapon();
				QueueFree();
				return;
			}
		}

		CheckEnemyHits();
	}

	private void CheckEnemyHits()
	{
		float hitRadius = 18.0f * World.Scale;
		var hitSet = _returning ? _returnHits : _outboundHits;

		foreach (Node node in GetTree().GetNodesInGroup("enemy"))
		{
			if (node is Enemy enemy && GodotObject.IsInstanceValid(enemy) && enemy.CurrentHealth > 0)
			{
				ulong id = enemy.GetInstanceId();
				if (hitSet.Contains(id)) continue;

				if (GlobalPosition.DistanceTo(enemy.GlobalPosition) <= hitRadius)
				{
					hitSet.Add(id);
					enemy.TakeDamage(_damage, GlobalPosition, _knockback, true);
				}
			}
		}
	}

	public override void _Draw()
	{
		// 1. Вихревой след вращения вокруг летящего топора/оружия
		DrawArc(Vector2.Zero, 10.5f, _spinAngle, _spinAngle + Mathf.Pi * 1.35f, 20, new Color(1.0f, 0.86f, 0.38f, 0.60f), 2.0f);
		DrawArc(Vector2.Zero, 10.5f, _spinAngle + Mathf.Pi, _spinAngle + Mathf.Pi * 2.35f, 20, new Color(0.95f, 0.55f, 0.25f, 0.48f), 1.6f);

		// 2. Само вращающееся оружие ближнего боя
		if (_weaponTexture != null)
		{
			Vector2 texSize = _weaponTexture.GetSize();
			DrawSetTransform(Vector2.Zero, _spinAngle, new Vector2(1.15f, 1.15f));
			DrawTexture(_weaponTexture, -texSize * 0.5f);
			DrawSetTransform(Vector2.Zero, 0.0f, Vector2.One);
		}
		else
		{
			DrawCircle(Vector2.Zero, 6.0f, new Color(0.9f, 0.9f, 0.95f));
		}
	}
}
