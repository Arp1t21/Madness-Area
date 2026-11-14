using Godot;
using System.Collections.Generic;

public enum StatusEffectType { Stun, Slow, Root, Disarm }

public partial class StatusEffectManager : Node
{
	private CharacterBody2D _owner;
	// Для каждого эффекта храним оставшееся время и силу (например, Slow 0.25 = -25% скорости)
	private Dictionary<StatusEffectType, (float time, float strength)> _activeEffects =
		new Dictionary<StatusEffectType, (float, float)>();

	private readonly List<StatusEffectType> _keysBuffer = new List<StatusEffectType>();
	private readonly List<StatusEffectType> _toRemoveBuffer = new List<StatusEffectType>();

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
		if (_activeEffects.Count == 0) return;

		_toRemoveBuffer.Clear();
		_keysBuffer.Clear();
		_keysBuffer.AddRange(_activeEffects.Keys);

		foreach (var type in _keysBuffer)
		{
			var effect = _activeEffects[type];
			effect.time -= (float)delta;
			_activeEffects[type] = effect;
			if (effect.time <= 0)
			{
				_toRemoveBuffer.Add(type);
			}
		}

		foreach (var type in _toRemoveBuffer)
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
