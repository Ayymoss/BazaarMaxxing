namespace BazaarCompanionWeb.Services;

public class AboutModalService
{
    public bool IsVisible { get; private set; }
    
    public event Action? OnChange;

    public void Show()
    {
        IsVisible = true;
        OnChange?.Invoke();
    }

    public void Hide()
    {
        IsVisible = false;
        OnChange?.Invoke();
    }
}
