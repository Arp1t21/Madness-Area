using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// Shared Godot resource caches, hierarchy lookup helpers, and pooled audio playback.
/// </summary>
public static class GameResourceCache
{
	public const float SilverBaselineOffset = 0.135f;
	public const float ForwardBaselineOffset = 0.13f;

	private static Font _silverFont;
	private static Font _forwardFont;
	private static CanvasItemMaterial _unshadedMaterial;
	private static CanvasItemMaterial _lightOnlyMaterial;

	private static readonly Dictionary<string, GradientTexture2D> _radialGlowTextures =
		new Dictionary<string, GradientTexture2D>();

	private static readonly Dictionary<string, List<AudioStreamPlayer>> _audioPools =
		new Dictionary<string, List<AudioStreamPlayer>>();

	private static readonly Dictionary<string, int> _audioRoundRobin =
		new Dictionary<string, int>();

	private static Node _audioPoolHost;

	public static Font GetSilverFont()
	{
		if (_silverFont == null)
		{
			_silverFont = GD.Load<Font>("res://assets/fonts/silver/SilverCentered.tres");
			if (_silverFont == null)
			{
				var raw = GD.Load<Font>("res://assets/fonts/silver/Silver.ttf");
				if (raw != null)
				{
					_silverFont = new FontVariation
					{
						BaseFont = raw,
						BaselineOffset = SilverBaselineOffset
					};
				}
			}
		}
		return _silverFont;
	}

	public static Font GetForwardFont()
	{
		if (_forwardFont == null)
		{
			_forwardFont = GD.Load<Font>("res://assets/fonts/fff-forward/FffForwardCentered.tres");
			if (_forwardFont == null)
			{
				var raw = GD.Load<Font>("res://assets/fonts/fff-forward/FFFFORWA.TTF");
				if (raw != null)
				{
					_forwardFont = new FontVariation
					{
						BaseFont = raw,
						BaselineOffset = ForwardBaselineOffset
					};
				}
			}
		}
		return _forwardFont;
	}

	/// <summary>
	/// Walks wrapped FontVariation layers to obtain the underlying FontFile.
	/// </summary>
	public static FontFile UnwrapFontFile(Font font)
	{
		Font cur = font;
		for (int i = 0; i < 8 && cur is FontVariation fv; i++)
		{
			cur = fv.BaseFont;
		}
		return cur as FontFile;
	}

	/// <summary>
	/// Resolves the vertical-centering baseline offset for Silver / FFF Forward fonts.
	/// </summary>
	public static float GetCenteredBaselineOffset(Font font)
	{
		if (font is FontVariation fv && Mathf.Abs(fv.BaselineOffset) > 0.0001f)
		{
			return fv.BaselineOffset;
		}

		FontFile ff = UnwrapFontFile(font);
		string path = ff?.ResourcePath ?? font?.ResourcePath ?? "";
		if (path.Contains("Silver", StringComparison.OrdinalIgnoreCase))
		{
			return SilverBaselineOffset;
		}
		if (path.Contains("FFFFORWA", StringComparison.OrdinalIgnoreCase)
			|| path.Contains("FffForward", StringComparison.OrdinalIgnoreCase))
		{
			return ForwardBaselineOffset;
		}
		return 0.0f;
	}

	/// <summary>
	/// Creates a single-layer FontVariation that preserves vertical centering alongside styling overrides.
	/// </summary>
	public static Font CreateFontVariation(
		Font baseFont,
		float embolden = 0.0f,
		int spacingGlyph = 0,
		int spacingTop = 0,
		int spacingBottom = 0)
	{
		if (baseFont == null) return null;
		Font rootBase = (Font)UnwrapFontFile(baseFont) ?? baseFont;
		return new FontVariation
		{
			BaseFont = rootBase,
			BaselineOffset = GetCenteredBaselineOffset(baseFont),
			VariationEmbolden = embolden,
			SpacingGlyph = spacingGlyph,
			SpacingTop = spacingTop,
			SpacingBottom = spacingBottom
		};
	}

	public static CanvasItemMaterial GetUnshadedMaterial()
	{
		if (_unshadedMaterial == null)
		{
			_unshadedMaterial = new CanvasItemMaterial
			{
				LightMode = CanvasItemMaterial.LightModeEnum.Unshaded
			};
		}
		return _unshadedMaterial;
	}

	public static CanvasItemMaterial GetLightOnlyMaterial()
	{
		if (_lightOnlyMaterial == null)
		{
			_lightOnlyMaterial = new CanvasItemMaterial
			{
				LightMode = CanvasItemMaterial.LightModeEnum.LightOnly
			};
		}
		return _lightOnlyMaterial;
	}

	public static GradientTexture2D GetRadialGlowTexture(
		Color innerColor,
		Color midColor,
		float midOffset,
		int size,
		Vector2? fillTo = null)
	{
		Vector2 resolvedFillTo = fillTo ?? new Vector2(0.5f, 0.0f);
		string key =
			$"{Quantize(innerColor)}|{Quantize(midColor)}|{Mathf.RoundToInt(midOffset * 1000f)}|{size}|{Mathf.RoundToInt(resolvedFillTo.X * 1000f)},{Mathf.RoundToInt(resolvedFillTo.Y * 1000f)}";

		if (_radialGlowTextures.TryGetValue(key, out var cached) && cached != null)
		{
			return cached;
		}

		var gradient = new Gradient();
		if (midOffset > 0.01f && midOffset < 0.99f)
		{
			gradient.Colors = new Color[]
			{
				innerColor,
				midColor,
				new Color(0.0f, 0.0f, 0.0f, 0.0f)
			};
			gradient.Offsets = new float[] { 0.0f, midOffset, 1.0f };
		}
		else
		{
			gradient.Colors = new Color[]
			{
				innerColor,
				new Color(0.0f, 0.0f, 0.0f, 0.0f)
			};
			gradient.Offsets = new float[] { 0.0f, 1.0f };
		}

		var tex = new GradientTexture2D
		{
			Gradient = gradient,
			Fill = GradientTexture2D.FillEnum.Radial,
			FillFrom = new Vector2(0.5f, 0.5f),
			FillTo = resolvedFillTo,
			Width = size,
			Height = size
		};

		_radialGlowTextures[key] = tex;
		return tex;
	}

	public static PointLight2D CreateGlowLight(
		Node parent,
		string name,
		Vector2 position,
		Color color,
		float energy,
		Texture2D texture,
		bool shadowEnabled = false,
		int itemCullMask = 1)
	{
		var light = new PointLight2D
		{
			Name = name,
			Position = position,
			ShadowEnabled = shadowEnabled,
			Color = color,
			Energy = energy,
			Texture = texture,
			RangeItemCullMask = itemCullMask
		};
		parent?.AddChild(light);
		return light;
	}

	/// <summary>
	/// Walks parent chain safely instead of relying on fixed GetParent()?.GetParent() hops.
	/// </summary>
	public static T FindAncestor<T>(Node startNode, int maxSteps = 8) where T : Node
	{
		Node current = startNode;
		for (int i = 0; i < maxSteps && current != null; i++)
		{
			current = current.GetParent();
			if (current is T match)
			{
				return match;
			}
		}
		return null;
	}

	public static void PlayPooledSound(SceneTree tree, AudioStream stream, string busName = "SFX", int maxVoicesPerBus = 14)
	{
		if (stream == null) return;
		tree ??= Engine.GetMainLoop() as SceneTree;
		if (tree?.Root == null) return;

		if (_audioPoolHost == null || !GodotObject.IsInstanceValid(_audioPoolHost) || !_audioPoolHost.IsInsideTree())
		{
			_audioPoolHost = new Node
			{
				Name = "SoundPoolHost",
				ProcessMode = Node.ProcessModeEnum.Always
			};
			tree.Root.AddChild(_audioPoolHost);
			_audioPools.Clear();
			_audioRoundRobin.Clear();
		}

		if (!_audioPools.TryGetValue(busName, out var pool))
		{
			pool = new List<AudioStreamPlayer>(maxVoicesPerBus);
			_audioPools[busName] = pool;
		}

		AudioStreamPlayer chosen = null;
		for (int i = pool.Count - 1; i >= 0; i--)
		{
			var p = pool[i];
			if (!GodotObject.IsInstanceValid(p))
			{
				pool.RemoveAt(i);
				continue;
			}
			if (!p.Playing)
			{
				chosen = p;
				break;
			}
		}

		if (chosen == null && pool.Count < maxVoicesPerBus)
		{
			chosen = new AudioStreamPlayer
			{
				Bus = busName
			};
			_audioPoolHost.AddChild(chosen);
			pool.Add(chosen);
		}

		if (chosen == null && pool.Count > 0)
		{
			_audioRoundRobin.TryGetValue(busName, out int nextIdx);
			nextIdx %= pool.Count;
			chosen = pool[nextIdx];
			_audioRoundRobin[busName] = nextIdx + 1;
		}

		if (chosen != null)
		{
			chosen.Bus = busName;
			chosen.Stream = stream;
			chosen.Play();
		}
	}

	private static string Quantize(Color c)
	{
		return $"{Mathf.RoundToInt(c.R * 255f)},{Mathf.RoundToInt(c.G * 255f)},{Mathf.RoundToInt(c.B * 255f)},{Mathf.RoundToInt(c.A * 255f)}";
	}
}
