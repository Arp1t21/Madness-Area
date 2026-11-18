using Godot;
using System;

public partial class Projectile : Node2D
{
	public float Damage = 10;
	public float Speed = 100.0f;
	public float Range = 500.0f;
	public Vector2 Direction = Vector2.Right;
	public bool IsPlayerOwned = true;
	public bool IsPiercing = false;
	public bool IsBladeProjectile = false;
	public int RemainingWallBounces = 0;
	public Player OwnerPlayer = null;
	public Enemy SourceEnemy = null;
	public float KnockbackForce = 2.5f;

	[Export] public bool EnableSoftGlow = false;
	[Export] public bool DrawOrbVisual = false;
	[Export] public Color GlowColor = new Color(0.4f, 0.85f, 1.0f, 1.0f);
	[Export] public float GlowEnergy = 0.65f;
	[Export] public int GlowTextureSize = 68;

	private Vector2 _startPos;
	private readonly System.Collections.Generic.HashSet<ulong> _hitTargets = new System.Collections.Generic.HashSet<ulong>();

	public override void _Ready()
	{
		Scale = Vector2.One;
		_startPos = GlobalPosition;

		var area = GetNodeOrNull<Area2D>("Area2D") ?? GetNodeOrNull<Area2D>(".");
		if (area != null)
		{
			area.AreaEntered += _on_area_2d_area_entered;
			area.BodyEntered += _on_body_entered;
		}
		else
		{
			GD.PushWarning("Projectile: No Area2D found to detect collisions!");
		}

		if (EnableSoftGlow || DrawOrbVisual)
		{
			var unshaded = GameResourceCache.GetUnshadedMaterial();
			Material = unshaded;
			foreach (var child in GetChildren())
			{
				if (child is CanvasItem ci)
				{
					ci.Material = unshaded;
				}
			}
			SetupSoftLight();
		}

		if (DrawOrbVisual)
		{
			SetupTrail();
		}
	}

	private void SetupSoftLight()
	{
		var gradTex = GameResourceCache.GetRadialGlowTexture(
			new Color(1.0f, 1.0f, 1.0f, 1.0f),
			new Color(0.85f, 0.95f, 1.0f, 0.65f),
			0.45f,
			GlowTextureSize);

		GameResourceCache.CreateGlowLight(
			this,
			"SoftProjectileLight",
			Vector2.Zero,
			GlowColor,
			GlowEnergy,
			gradTex,
			shadowEnabled: false,
			itemCullMask: IsPlayerOwned ? 3 : 1);
	}

	public override void _Draw()
	{
		if (!DrawOrbVisual || _isDestroyed) return;

		Color outer = new Color(GlowColor.R, GlowColor.G, GlowColor.B, 0.42f);
		Color mid = new Color(
			Mathf.Min(1.0f, GlowColor.R + 0.15f),
			Mathf.Min(1.0f, GlowColor.G + 0.12f),
			Mathf.Min(1.0f, GlowColor.B + 0.12f),
			0.88f);
		DrawCircle(Vector2.Zero, 4.6f, outer);
		DrawCircle(Vector2.Zero, 2.9f, mid);
		DrawCircle(Vector2.Zero, 1.5f, new Color(0.95f, 0.99f, 1.0f, 1.0f));
	}

	private void SetupTrail()
	{
		foreach (var child in GetChildren())
		{
			if (child is CpuParticles2D) return;
		}

		var trail = new CpuParticles2D();
		trail.Name = "Trail";
		trail.Amount = 24;
		trail.Lifetime = 0.22f;
		trail.LocalCoords = false;
		trail.Gravity = Vector2.Zero;
		trail.Direction = -Direction;
		trail.Spread = 12f;
		trail.InitialVelocityMin = 2f;
		trail.InitialVelocityMax = 6f;
		trail.ScaleAmountMin = 0.6f;
		trail.ScaleAmountMax = 1.3f;

		var gradient = new Gradient();
		if (IsPlayerOwned)
		{
			gradient.Colors = new Color[]
			{
				new Color(1.0f, 0.85f, 0.4f, 0.55f),
				new Color(0.9f, 0.6f, 0.1f, 0.0f)
			};
		}
		else
		{
			gradient.Colors = new Color[]
			{
				new Color(0.7f, 0.9f, 1.0f, 0.5f),
				new Color(0.3f, 0.5f, 0.9f, 0.0f)
			};
		}
		gradient.Offsets = new float[] { 0.0f, 1.0f };
		trail.ColorRamp = gradient;

		AddChild(trail);
	}

	public override void _PhysicsProcess(double delta)
	{
		Position += Direction * Speed * (float)delta;

		if (GlobalPosition.DistanceTo(_startPos) >= Range)
		{
			QueueFree();
		}
	}

	private void _on_body_entered(Node2D body)
	{
		HandleCollision(body);
	}

	private void _on_area_2d_area_entered(Area2D area)
	{
		HandleCollision(area.GetParent());
	}

	private bool _isDestroyed = false;

	private void HandleCollision(Node hitNode)
	{
		if (_isDestroyed || hitNode == null || hitNode.IsQueuedForDeletion()) return;
		bool isServerAuthority = NetworkManager.Instance == null || NetworkManager.Instance.IsServerAuthority;

		if (IsPlayerOwned && hitNode is Enemy enemy && !_hitTargets.Contains(hitNode.GetInstanceId()))
		{
			_hitTargets.Add(hitNode.GetInstanceId());
			if (isServerAuthority)
			{
				Player ownerPlayer = GodotObject.IsInstanceValid(OwnerPlayer)
					? OwnerPlayer
					: (GetTree()?.GetFirstNodeInGroup("Player") as Player);
				float dmg = Damage;
				bool isCrit = false;
				if (ownerPlayer != null)
				{
					(dmg, isCrit) = ownerPlayer.RollDamageCrit(Damage, IsBladeProjectile);
				}
				enemy.TakeDamage(dmg, GlobalPosition, KnockbackForce, false, isCrit, ownerPlayer);
				if (ownerPlayer != null)
				{
					enemy.ApplyPlayerHitRelicEffects(ownerPlayer, dmg, IsBladeProjectile, isCrit);
				}
			}
			DungeonVisuals.SpawnImpactSparks(
				GetTree().CurrentScene ?? GetTree().Root,
				GlobalPosition,
				-Direction,
				GlowColor != default ? GlowColor : new Color(1.0f, 0.86f, 0.42f),
				6,
				45.0f,
				70.0f,
				0.8f);

			if (!IsPiercing)
			{
				_isDestroyed = true;
				QueueFree();
			}
		}
		else if (!IsPlayerOwned && hitNode is Player player && !player.IsDead && !_hitTargets.Contains(hitNode.GetInstanceId()))
		{
			_hitTargets.Add(hitNode.GetInstanceId());
			if (isServerAuthority)
			{
				player.TakeDamage(Damage, GlobalPosition, KnockbackForce, false);
				if (SourceEnemy != null && GodotObject.IsInstanceValid(SourceEnemy))
				{
					SourceEnemy.ApplyEliteOnHitToPlayer(player);
				}
			}
			DungeonVisuals.SpawnImpactSparks(
				GetTree().CurrentScene ?? GetTree().Root,
				GlobalPosition,
				-Direction,
				new Color(1.0f, 0.35f, 0.28f),
				6,
				45.0f,
				70.0f,
				0.8f);

			if (!IsPiercing)
			{
				_isDestroyed = true;
				QueueFree();
			}
		}
		else if (hitNode is TileMapLayer ||
				 hitNode is StaticBody2D ||
				 hitNode.GetType().Name.Contains("TileMap") ||
				 hitNode.GetType().Name.Contains("StaticBody") ||
				 hitNode.Name.ToString().Contains("Wall") ||
				 hitNode.Name.ToString().Contains("Floor"))
		{
			if (IsPlayerOwned && RemainingWallBounces > 0 && TryBounceOffWall())
			{
				return;
			}
			Node vfxHost = GetTree().CurrentScene ?? GetTree().Root;
			Color wallSparkCol = IsPlayerOwned ? new Color(0.95f, 0.84f, 0.56f) : GlowColor;
			DungeonVisuals.SpawnMuzzleFlash(vfxHost, GlobalPosition, wallSparkCol, 1.6f);
			DungeonVisuals.SpawnImpactSparks(vfxHost, GlobalPosition, -Direction, wallSparkCol, 6, 40.0f, 80.0f, 0.75f);
			_isDestroyed = true;
			QueueFree();
		}
	}

	private bool TryBounceOffWall()
	{
		var space = GetWorld2D()?.DirectSpaceState;
		Vector2 normal = Vector2.Zero;
		Vector2 hitPos = GlobalPosition;

		if (space != null)
		{
			var excludeRids = new Godot.Collections.Array<Rid>();
			foreach (Node n in GetTree().GetNodesInGroup("Enemies"))
			{
				if (n is CollisionObject2D co && GodotObject.IsInstanceValid(co)) excludeRids.Add(co.GetRid());
			}
			foreach (Node n in GetTree().GetNodesInGroup("Player"))
			{
				if (n is CollisionObject2D co && GodotObject.IsInstanceValid(co)) excludeRids.Add(co.GetRid());
			}

			var query = PhysicsRayQueryParameters2D.Create(
				GlobalPosition - Direction * 12.0f,
				GlobalPosition + Direction * 12.0f,
				1u);
			query.Exclude = excludeRids;
			var res = space.IntersectRay(query);
			if (res != null && res.Count > 0 && res.ContainsKey("normal"))
			{
				normal = res["normal"].AsVector2();
				if (res.ContainsKey("position")) hitPos = res["position"].AsVector2();
			}
		}

		if (normal == Vector2.Zero)
		{
			normal = Mathf.Abs(Direction.X) >= Mathf.Abs(Direction.Y)
				? new Vector2(-Mathf.Sign(Direction.X), 0.0f)
				: new Vector2(0.0f, -Mathf.Sign(Direction.Y));
		}

		Direction = Direction.Bounce(normal).Normalized();
		Rotation = Direction.Angle();
		GlobalPosition = hitPos + normal * 2.5f + Direction * 2.5f;
		RemainingWallBounces--;
		_hitTargets.Clear();

		Node vfxHost = GetTree()?.CurrentScene ?? GetTree()?.Root;
		if (vfxHost != null)
		{
			DungeonVisuals.SpawnImpactSparks(vfxHost, hitPos, normal, new Color(1.0f, 0.90f, 0.48f), 5, 35.0f, 65.0f, 0.65f);
		}
		return true;
	}
}
