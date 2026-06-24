using Godot;
using System;
using System.Collections.Generic;

public partial class Fireball : Area2D
{
	public Vector2 Direction = Vector2.Right;
	public float Speed = 900.0f;
	public float Range = 400.0f;
	public float Damage = 5.0f;
	public float ExplosionRadius = 45.0f;

	private Vector2 _startPos;
	private CpuParticles2D _trail;
	private bool _isDestroyed = false;
	private HashSet<ulong> _hitTargets = new HashSet<ulong>();

	public override void _Ready()
	{
		Scale = new Vector2(3.0f, 3.0f);
		_startPos = GlobalPosition;

		// Настройка коллизий: маска коллизий для врагов и стен/преград
		CollisionLayer = 0; // Огненный шар сам по себе не коллизит с другими вещами напрямую как тело
		CollisionMask = 1 | 2 | 4; // Слой 1 (стены/земля), слои врагов

		// Ищем существующий шлейф в сцене
		_trail = GetNodeOrNull<CpuParticles2D>("Trail") ?? GetNodeOrNull<CpuParticles2D>("CpuParticles2D");

		if (_trail == null)
		{
			// Если его нет в сцене, создаем программно
			_trail = new CpuParticles2D();
			_trail.Name = "Trail";
			_trail.Amount = 50;
			_trail.Lifetime = 0.35f;
			_trail.LocalCoords = false;
			_trail.Gravity = Vector2.Zero;
			_trail.Spread = 20f;
			_trail.InitialVelocityMin = 10f;
			_trail.InitialVelocityMax = 30f;

			var scaleCurve = new Curve();
			scaleCurve.AddPoint(new Vector2(0f, 1.2f));
			scaleCurve.AddPoint(new Vector2(1f, 0.2f));
			_trail.ScaleAmountCurve = scaleCurve;

			var gradient = new Gradient();
			gradient.Colors = new Color[] {
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

		// Проверяем наличие CollisionShape2D в сцене
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

		// Подписываемся на коллизии
		BodyEntered += OnBodyEntered;
		AreaEntered += OnAreaEntered;
	}

	public override void _PhysicsProcess(double delta)
	{
		if (_isDestroyed) return;

		Position += Direction * Speed * (float)delta;

		// Ограничение дистанции
		if (GlobalPosition.DistanceTo(_startPos) >= Range)
		{
			Explode();
		}
	}

	public override void _Draw()
	{
		if (_isDestroyed) return;

		// Отрисовка светящегося огненного ядра
		DrawCircle(Vector2.Zero, 8.0f, new Color(1.0f, 0.4f, 0.0f, 0.3f)); // Внешнее свечение
		DrawCircle(Vector2.Zero, 5.0f, new Color(1.0f, 0.65f, 0.0f, 0.8f)); // Средний слой
		DrawCircle(Vector2.Zero, 2.5f, new Color(1.0f, 1.0f, 0.8f, 1.0f)); // Яркое ядро
	}

	private void OnBodyEntered(Node2D body)
	{
		HandleCollision(body);
	}

	private void OnAreaEntered(Area2D area)
	{
		HandleCollision(area.GetParent());
	}

	private void HandleCollision(Node hitNode)
	{
		if (_isDestroyed || hitNode == null || hitNode.IsQueuedForDeletion()) return;

		// Не взрываемся о самого игрока
		if (hitNode.IsInGroup("Player")) return;

		// Пробиваем врагов насквозь, нанося урон один раз
		if (hitNode.IsInGroup("enemy"))
		{
			if (_hitTargets.Add(hitNode.GetInstanceId()))
			{
				if (hitNode.HasMethod("TakeDamage"))
				{
					hitNode.Call("TakeDamage", Damage, GlobalPosition, 8.0f, false);
				}
			}
			return; // Летим дальше
		}

		// Взрываемся при контакте с твердыми объектами (стены, преграды, двери)
		if (hitNode is TileMapLayer || 
			hitNode is StaticBody2D ||
			hitNode is Door ||
			hitNode.Name.ToString().Contains("Wall") ||
			hitNode.Name.ToString().Contains("Floor"))
		{
			Explode();
		}
	}

	private void Explode()
	{
		if (_isDestroyed) return;
		_isDestroyed = true;

		// Скрываем ядро и отключаем коллизии
		Visible = false;
		SetDeferred("monitoring", false);
		SetDeferred("monitorable", false);

		// Создаем взрывные частицы
		var explosion = new FireballExplosion();
		explosion.GlobalPosition = GlobalPosition;
		GetParent().AddChild(explosion);

		// Наносим АОЕ урон всем врагам в радиусе взрыва
		var enemies = GetTree().GetNodesInGroup("enemy");
		foreach (Node enemyNode in enemies)
		{
			if (enemyNode is CharacterBody2D enemy && GodotObject.IsInstanceValid(enemy))
			{
				float dist = GlobalPosition.DistanceTo(enemy.GlobalPosition);
				if (dist <= ExplosionRadius * 3f)
				{
					if (enemy.HasMethod("TakeDamage"))
					{
						// Направление отскока от центра взрыва
						Vector2 knockDir = (enemy.GlobalPosition - GlobalPosition).Normalized();
						enemy.Call("TakeDamage", Damage, GlobalPosition, 12.0f, false);
					}
				}
			}
		}

		// Даем шлейфу доиграть частицы, после чего удаляем узел
		if (_trail != null)
		{
			_trail.Emitting = false;
			var timer = GetTree().CreateTimer(_trail.Lifetime);
			timer.Timeout += QueueFree;
		}
		else
		{
			QueueFree();
		}
	}
}

// Отдельный узел для взрывных частиц огненного шара
public partial class FireballExplosion : CpuParticles2D
{
	public override void _Ready()
	{
		Scale = new Vector2(3.0f, 3.0f);
		Emitting = true;
		OneShot = true;
		Explosiveness = 1.0f;
		Amount = 35;
		Lifetime = 0.45f;
		Spread = 180f;
		Gravity = Vector2.Zero;
		InitialVelocityMin = 180f;
		InitialVelocityMax = 330f;
		
		// Кривая размера
		var scaleCurve = new Curve();
		scaleCurve.AddPoint(new Vector2(0f, 1.5f));
		scaleCurve.AddPoint(new Vector2(1f, 0.1f));
		ScaleAmountCurve = scaleCurve;
		
		// Цветовой градиент вспышки
		var gradient = new Gradient();
		gradient.Colors = new Color[] {
			new Color(1.0f, 1.0f, 0.7f, 1.0f),
			new Color(1.0f, 0.6f, 0.0f, 0.9f),
			new Color(0.9f, 0.1f, 0.0f, 0.6f),
			new Color(0.1f, 0.1f, 0.1f, 0.0f)
		};
		gradient.Offsets = new float[] { 0.0f, 0.2f, 0.6f, 1.0f };
		ColorRamp = gradient;

		// Автоматически удаляем после окончания анимации
		var timer = GetTree().CreateTimer(0.55f);
		timer.Timeout += QueueFree;
	}
}
