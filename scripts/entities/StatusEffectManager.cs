using Godot;
using System.Collections.Generic;

public enum StatusEffectType { Stun, Slow, Root, Disarm }

public partial class StatusEffectManager : Node
{
	private CharacterBody2D _owner;
	// Для каждого эффекта храним оставшееся время и силу (например, Slow 0.25 = -25% скорости)
	private Dictionary<StatusEffectType, (float time, float strength)> _activeEffects =
		new Dictionary<StatusEffectType, (float, float)>();

	public override void _Ready()
	{
		_owner = GetParent() as CharacterBody2D;
	}

	public void ApplyEffect(StatusEffectType type, float duration, float strength = 1.0f)
	{
		if (_activeEffects.ContainsKey(type))
		{
			var existing = _activeEffects[type];
			_activeEffects[type] = (Mathf.Max(existing.time, duration), Mathf.Max(existing.strength, strength));
		}
		else
		{
			_activeEffects[type] = (duration, strength);
		}
	}

	public override void _Process(double delta)
	{
		List<StatusEffectType> toRemove = new List<StatusEffectType>();
		var keys = new List<StatusEffectType>(_activeEffects.Keys);

		foreach (var type in keys)
		{
			var effect = _activeEffects[type];
			effect.time -= (float)delta;
			_activeEffects[type] = effect;
			if (effect.time <= 0)
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
		return _activeEffects.ContainsKey(type) ? _activeEffects[type].time : 0f;
	}

	public float GetEffectStrength(StatusEffectType type)
	{
		return _activeEffects.ContainsKey(type) ? _activeEffects[type].strength : 0f;
	}
}
