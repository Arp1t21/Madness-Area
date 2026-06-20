using Godot;
using System;

public partial class MainMenu : CanvasLayer
{
	private Control _menuRoot;

	public override void _Ready()
	{
		_menuRoot = GetNode<Control>("MenuRoot");
		
		// Подключаем кнопки
		GetNode<Button>("MenuRoot/VBoxContainer/PlayButton").Pressed += OnPlayPressed;
		GetNode<Button>("MenuRoot/VBoxContainer/SettingsButton").Pressed += OnSettingsPressed;
		GetNode<Button>("MenuRoot/VBoxContainer/ExitButton").Pressed += OnExitPressed;
	}

	private void OnPlayPressed()
	{
		GetTree().ChangeSceneToFile("res://scene/levels/level.tscn");
	}

	private void OnSettingsPressed()
	{
		GD.Print("Settings opened (not implemented)");
	}

	private void OnExitPressed()
	{
		GetTree().Quit();
	}
}
