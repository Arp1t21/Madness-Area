using Godot;
using System;
using System.Collections.Generic;

public partial class Fireball : Area2D
{
	public Vector2 Direction = Vector2.Right;
	public float Speed = 200.0f;
	public float Range = 135.0f;
	public float Damage = 10.0f;
	public float ExplosionRadius = 45.0f;
	public Player OwnerPlayer = null;

	private Vector2 _startPos;
	private CpuParticles2D _trail;
	private bool _isDestroyed = false;
	private HashSet<ulong> _hitTargets = new HashSet<ulong>();

	public override void _Ready()
	{
		Scale = Vector2.One;
		_startPos = GlobalPosition;
		Material = GameResourceCache.GetUnshadedMaterial();

		CollisionLayer = 0;
		CollisionMask = 1 | 2 | 4;

		var fireGlowTex = GameResourceCache.GetRadialGlowTexture(
			new Color(1.0f, 0.92f, 0.65f, 1.0f),
			new Color(1.0f, 0.45f, 0.10f, 0.55f),
			0.45f,
			96);
		GameResourceCache.CreateGlowLight(
			this,
			"FireballLight",
			Vector2.Zero,
			new Color(1.0f, 0.62f, 0.22f, 1.0f),
			0.95f,
			fireGlowTex,
			shadowEnabled: false,
			itemCullMask: 3);

		_trail = GetNodeOrNull<CpuParticles2D>("Trail") ?? GetNodeOrNull<CpuParticles2D>("CpuParticles2D");

		if (_trail == null)
		{
			_trail = new CpuParticles2D();
			_trail.Name = "Trail";
			_trail.Texture = DungeonVisuals.GetPixelSquareTexture();
			_trail.Material = GameResourceCache.GetUnshadedMaterial();
			_trail.Amount = 50;
			_trail.Lifetime = 0.35f;
			_trail.LocalCoords = false;
			_trail.Gravity = Vector2.Zero;
			_trail.Spread = 20f;
			_trail.InitialVelocityMin = 4f;
			_trail.InitialVelocityMax = 10f;

			var scaleCurve = new Curve();
			scaleCurve.AddPoint(new Vector2(0f, 0.7f));
			scaleCurve.AddPoint(new Vector2(1f, 0.1f));
			_trail.ScaleAmountCurve = scaleCurve;

			var gradient = new Gradient();
			gradient.Colors = new Color[]
			{
				new Color(1.0f, 0.9f, 0.3f, 1.0f),
				new Color(1.0f, 0.5f, 0.0f, 0.8f),
				new Color(0.9f, 0.1f, 0.0f, 0.5f),
				new Color(0.2f, 0.2f, 0.2f, 0.0f)
			};
			gradient.Offsets = new float[] { 0.0f, 0.3f, 0.7f, 1.0f };
			_trail.ColorRamp = gradient;

			AddChild(_trail);
		}

		if (_trail != null)
		{
			_trail.Direction = -Direction;
		}

		bool hasCollisionShape = false;
		foreach (var child in GetChildren())
		{
			if (child is CollisionShape2D)
			{
				hasCollisionShape = true;
				break;
			}
		}

		if (!hasCollisionShape)
		{
			var collisionShape = new CollisionShape2D();
			var circleShape = new CircleShape2D();
			circleShape.Radius = 6.0f;
			collisionShape.Shape = circleShape;
			AddChild(collisionShape);
		}

		BodyEntered += OnBodyEntered;
		AreaEntered += OnAreaEntered;
	}

	public override void _PhysicsProcess(double delta)
	{
		if (_isDestroyed) return;

		Position += Direction * Speed * (float)delta;

		if (GlobalPosition.DistanceTo(_startPos) >= Range)
		{
			Explode();
		}
	}

	public override void _Draw()
	{
		if (_isDestroyed) return;

		DrawCircle(Vector2.Zero, 8.0f, new Color(1.0f, 0.4f, 0.0f, 0.3f));
		DrawCircle(Vector2.Zero, 5.0f, new Color(1.0f, 0.65f, 0.0f, 0.8f));
		DrawCircle(Vector2.Zero, 2.5f, new Color(1.0f, 1.0f, 0.8f, 1.0f));
	}

	private void OnBodyEntered(Node2D body)
	{
		HandleCollision(body);
	}

	private void OnAreaEntered(Area2D area)
	{
		if (area.IsInGroup("enemy"))
		{
			HandleCollision(area.GetParent());
		}
	}

	private void HandleCollision(Node hitNode)
	{
		if (_isDestroyed || hitNode == null || hitNode.IsQueuedForDeletion()) return;

		if (hitNode.IsInGroup("Player")) return;

		// Пробиваем врагов насквозь, нанося урон ровно один раз за каст
		if (hitNode is Enemy enemy)
		{
			if (_hitTargets.Add(enemy.GetInstanceId()))
			{
				if (NetworkManager.Instance == null || NetworkManager.Instance.IsServerAuthority)
				{
					enemy.TakeDamage(Damage, GlobalPosition, 8.0f, false, false, OwnerPlayer);
					enemy.ApplyStatusEffect(StatusEffectType.Burn, 3.0f, MathF.Max(4.0f, Damage * 0.35f));
				}
			}
			return;
		}

		// Взрываемся только при контакте с твердыми препятствиями (стены, колонны, двери)
		if (hitNode is TileMapLayer ||
			hitNode is StaticBody2D ||
			hitNode is Door ||
			hitNode.Name.ToString().Contains("Wall") ||
			hitNode.Name.ToString().Contains("Collumn"))
		{
			Explode();
		}
	}

	private void Explode()
	{
		if (_isDestroyed) return;
		_isDestroyed = true;

		Visible = false;
		SetDeferred("monitoring", false);
		SetDeferred("monitorable", false);

		float radiusScale = Mathf.Max(0.5f, ExplosionRadius / 45.0f);
		var explosion = new FireballExplosion();
		explosion.GlobalPosition = GlobalPosition;
		explosion.Scale = Vector2.One * radiusScale;
		explosion.Amount = Mathf.Clamp(Mathf.RoundToInt(35 * radiusScale), 35, 80);
		GetParent().AddChild(explosion);
		DungeonVisuals.SpawnMuzzleFlash(GetParent(), GlobalPosition, new Color(1.0f, 0.72f, 0.24f), 7.5f);
		DungeonVisuals.SpawnImpactSparks(
			GetParent(),
			GlobalPosition,
			Vector2.Up,
			new Color(1.0f, 0.72f, 0.22f),
			18,
			70.0f,
			180.0f,
			1.1f);

		// Наносим АОЕ урон врагам в радиусе взрыва (избегая двойного полного урона по уже пробитым целям)
		if (NetworkManager.Instance == null || NetworkManager.Instance.IsServerAuthority)
		{
			var enemies = GetTree().GetNodesInGroup("Enemies");
			foreach (Node enemyNode in enemies)
			{
				if (enemyNode is Enemy enemy && GodotObject.IsInstanceValid(enemy) && enemy.CurrentHealth > 0)
				{
					float dist = GlobalPosition.DistanceTo(enemy.GlobalPosition);
					if (dist <= ExplosionRadius)
					{
						if (_hitTargets.Add(enemy.GetInstanceId()))
						{
							enemy.TakeDamage(Damage, GlobalPosition, 12.0f, false, false, OwnerPlayer);
							enemy.ApplyStatusEffect(StatusEffectType.Burn, 3.0f, MathF.Max(4.0f, Damage * 0.35f));
						}
					}
				}
			}
		}

		if (_trail != null)
		{
			_trail.Emitting = false;
			var timer = GetTree().CreateTimer(_trail.Lifetime, processAlways: false);
			timer.Timeout += QueueFree;
		}
		else
		{
			QueueFree();
		}
	}
}

public partial class FireballExplosion : CpuParticles2D
{
	public override void _Ready()
	{
		Scale = Vector2.One;
		Texture = DungeonVisuals.GetPixelSquareTexture();
		Material = GameResourceCache.GetUnshadedMaterial();
		Emitting = true;
		OneShot = true;
		Explosiveness = 1.0f;
		Amount = 35;
		Lifetime = 0.45f;
		Spread = 180f;
		Gravity = Vector2.Zero;
		InitialVelocityMin = 60f;
		InitialVelocityMax = 110f;

		var scaleCurve = new Curve();
		scaleCurve.AddPoint(new Vector2(0f, 0.8f));
		scaleCurve.AddPoint(new Vector2(1f, 0.1f));
		ScaleAmountCurve = scaleCurve;

		var gradient = new Gradient();
		gradient.Colors = new Color[]
		{
			new Color(1.0f, 1.0f, 0.7f, 1.0f),
			new Color(1.0f, 0.6f, 0.0f, 0.9f),
			new Color(0.9f, 0.1f, 0.0f, 0.6f),
			new Color(0.1f, 0.1f, 0.1f, 0.0f)
		};
		gradient.Offsets = new float[] { 0.0f, 0.2f, 0.6f, 1.0f };
		ColorRamp = gradient;

		var timer = GetTree().CreateTimer(0.55f, processAlways: false);
		timer.Timeout += QueueFree;
	}
}
