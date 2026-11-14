using Godot;
using System;

/// <summary>
/// Unshaded telegraph danger indicators displayed on the floor ahead of Boss attacks.
/// </summary>
public partial class TelegraphLineNode : Node2D
{
	private readonly Vector2 _dir;
	private readonly float _length;
	private readonly float _width;
	private readonly float _duration;
	private readonly Color _color;
	private float _elapsed = 0.0f;

	public TelegraphLineNode() { }

	public TelegraphLineNode(Vector2 origin, Vector2 dir, float length, float width, float duration, Color color)
	{
		GlobalPosition = origin;
		_dir = dir.Normalized();
		Rotation = _dir.Angle();
		_length = length;
		_width = width;
		_duration = Mathf.Max(0.05f, duration);
		_color = color;
		ZIndex = 1;
		Material = GameResourceCache.GetUnshadedMaterial();
	}

	public override void _Process(double delta)
	{
		_elapsed += (float)delta;
		if (_elapsed >= _duration)
		{
			QueueFree();
			return;
		}
		QueueRedraw();
	}

	public override void _Draw()
	{
		float p = Mathf.Clamp(_elapsed / _duration, 0.0f, 1.0f);
		float halfW = _width * 0.5f;
		Rect2 fullRect = new Rect2(0, -halfW, _length, _width);
		Rect2 fillRect = new Rect2(0, -halfW, _length * p, _width);

		DrawRect(fullRect, new Color(_color.R, _color.G, _color.B, 0.16f), true);
		DrawRect(fillRect, new Color(_color.R, _color.G, _color.B, 0.34f), true);
		DrawRect(fullRect, new Color(_color.R, _color.G, _color.B, 0.85f), false, 1.6f);
	}
}

public partial class TelegraphCircleNode : Node2D
{
	private readonly float _radius;
	private readonly float _duration;
	private readonly Color _color;
	private float _elapsed = 0.0f;

	public TelegraphCircleNode() { }

	public TelegraphCircleNode(Vector2 center, float radius, float duration, Color color)
	{
		GlobalPosition = center;
		_radius = radius;
		_duration = Mathf.Max(0.05f, duration);
		_color = color;
		ZIndex = 1;
		Material = GameResourceCache.GetUnshadedMaterial();
	}

	public override void _Process(double delta)
	{
		_elapsed += (float)delta;
		if (_elapsed >= _duration)
		{
			QueueFree();
			return;
		}
		QueueRedraw();
	}

	public override void _Draw()
	{
		float p = Mathf.Clamp(_elapsed / _duration, 0.0f, 1.0f);
		DrawCircle(Vector2.Zero, _radius, new Color(_color.R, _color.G, _color.B, 0.14f));
		DrawCircle(Vector2.Zero, _radius * p, new Color(_color.R, _color.G, _color.B, 0.28f));
		DrawArc(Vector2.Zero, _radius, 0f, Mathf.Tau, 36, new Color(_color.R, _color.G, _color.B, 0.90f), 1.8f);
	}
}
