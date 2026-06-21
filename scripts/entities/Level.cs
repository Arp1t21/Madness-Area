using Godot;
using System;
using System.Linq;

public partial class Level : Node2D
{
	public override void _Ready()
	{
		// 1. Создаем CanvasModulate для общего затемнения сцены (эффект подземелья)
		var modulate = new CanvasModulate();
		modulate.Color = new Color(0.04f, 0.04f, 0.06f, 1.0f); // Глубокий темный синий цвет
		AddChild(modulate);

		// 2. Находим слой тайлов карты
		var tileMapLayer = GetNodeOrNull<TileMapLayer>("Map/Layer0") ?? 
		                   GetNodeOrNull<TileMapLayer>("Map") ??
		                   GetChildren().Cast<Node>().OfType<TileMapLayer>().FirstOrDefault();

		if (tileMapLayer != null) {
			var usedCells = tileMapLayer.GetUsedCells();
			foreach (var cell in usedCells) {
				var tileData = tileMapLayer.GetCellTileData(cell);
				// Если у тайла террейн равен 1 (террейн "wall" в floor.tres), создаем для него окклюдер
				if (tileData != null && tileData.Terrain == 1) {
					var occluder = new LightOccluder2D();
					var polygon = new OccluderPolygon2D();
					
					// Тайлы имеют размер 16x16, поэтому делаем квадратный окклюдер
					polygon.Polygon = new Vector2[] {
						new Vector2(-8, -8),
						new Vector2(8, -8),
						new Vector2(8, 8),
						new Vector2(-8, 8)
					};
					
					occluder.Occluder = polygon;
					// MapToLocal возвращает локальный центр плитки
					occluder.Position = tileMapLayer.MapToLocal(cell);
					tileMapLayer.AddChild(occluder);
				}
			}
		}
	}
}
