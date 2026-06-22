using Godot;

public interface IInteractable
{
	void Interact(Player player);
	Vector2 GetInteractionPosition();
	void ShowPrompt(bool show);
}
