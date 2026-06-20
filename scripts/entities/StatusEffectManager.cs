using Godot;
using System.Collections.Generic;

public enum StatusEffectType { Stun, Slow, Root, Disarm }

public partial class StatusEffectManager : Node
{
	private CharacterBody2D _owner;
	private Dictionary<StatusEffectType, float> _activeEffects = new Dictionary<StatusEffectType, float>();

	public override void _Ready()
	{
		_owner = GetParent() as CharacterBody2D;
	}

	public void ApplyEffect(StatusEffectType type, float duration)
	{
		if (_activeEffects.ContainsKey(type))
		{
			_activeEffects[type] = Mathf.Max(_activeEffects[type], duration);
		}
		else
		{
			_activeEffects[type] = duration;
		}
	}

	public override void _Process(double delta)
	{
		List<StatusEffectType> toRemove = new List<StatusEffectType>();
		var keys = new List<StatusEffectType>(_activeEffects.Keys);

		foreach (var type in keys)
		{
			_activeEffects[type] -= (float)delta;
			if (_activeEffects[type] <= 0)
			{
				toRemove.Add(type);
			}
		}

		foreach (var type in toRemove)
		{
			_activeEffects.Remove(type);
		}
	}

	public bool HasEffect(StatusEffectType type) => _activeEffects.ContainsKey(type);
	
	public float GetEffectValue(StatusEffectType type) 
	{
		return _activeEffects.ContainsKey(type) ? _activeEffects[type] : 0f;
	}
}
