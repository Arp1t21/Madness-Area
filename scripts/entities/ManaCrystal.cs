using Godot;
using System;

public partial class ManaCrystal : Node2D
{
	private bool _isBeingPickedUp = false;
	private bool _isAnimatingSpawn = true;
	private Node2D _targetPlayer;
	private float _speed = 600.0f;
	private float _pickupDelay = 0.6f;
	private static AudioStream _manaSound;

	public override void _Ready()
	{
		Scale = new Vector2(3.0f, 3.0f);
		var area = GetNode<Area2D>("Area2D");
		area.AreaEntered += _on_area_2d_area_entered;

		if (_manaSound == null)
		{
			_manaSound = GD.Load<AudioStream>("res://assets/sounds/buff_collecting.wav");
		}

		PlaySpawnAnimation();
	}

	private void PlaySpawnAnimation()
	{
		_isAnimatingSpawn = true;

		Vector2 startPos = GlobalPosition;
		Vector2 jumpPos = startPos + new Vector2(0, -20 * 3f);
		Vector2 landPos = startPos + new Vector2((float)GD.RandRange(-15, 15) * 3f, 5 * 3f);

		var tween = CreateTween();
		tween.TweenProperty(this, "global_position", jumpPos, 0.25f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
		tween.TweenProperty(this, "global_position", landPos, 0.25f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);

		tween.Finished += () => _isAnimatingSpawn = false;
	}

	public override void _Process(double delta)
	{
		if (_isAnimatingSpawn) return;

		if (_pickupDelay > 0)
		{
			_pickupDelay -= (float)delta;
			if (_pickupDelay <= 0)
			{
				var nearby = GetTree().GetFirstNodeInGroup("Player") as Node2D;
				if (nearby != null && GlobalPosition.DistanceTo(nearby.GlobalPosition) < 15.0f * World.Scale)
				{
					StartPickup(nearby);
				}
			}
			return;
		}

		if (_isBeingPickedUp && IsInstanceValid(_targetPlayer))
		{
			Vector2 direction = (_targetPlayer.GlobalPosition - GlobalPosition).Normalized();
			GlobalPosition += direction * _speed * (float)delta;
			_speed += 1500.0f * (float)delta;

			if (GlobalPosition.DistanceTo(_targetPlayer.GlobalPosition) < 15.0f)
			{
				if (_targetPlayer is Player p)
				{
					float maxMana = p.MaxManaEffective;
					p.CurrentMana = Mathf.Min(maxMana, p.CurrentMana + 8.0f);
					if (_manaSound != null)
					{
						MainMenu.PlayUISound(_manaSound);
					}
				}
				QueueFree();
			}
		}
	}

	private void _on_area_2d_area_entered(Area2D area)
	{
		if (!_isBeingPickedUp && _pickupDelay <= 0 && area.GetParent() is Player player)
		{
			StartPickup(player);
		}
	}

	private void StartPickup(Node2D player)
	{
		_targetPlayer = player;
		_isBeingPickedUp = true;

		var tween = CreateTween();
		tween.TweenProperty(this, "scale", Scale * 0.8f, 0.3f);
	}
}
