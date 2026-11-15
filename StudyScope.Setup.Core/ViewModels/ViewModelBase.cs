using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace StudyScope.Setup.Core.ViewModels;

/// <summary>
/// Gemeinsame Basis der Setup-ViewModels. Die Razor-Komponenten abonnieren PropertyChanged und rufen
/// InvokeAsync(StateHasChanged) auf – dasselbe Muster wie StudyScope.UI mit StudyScope.ViewModels.
/// </summary>
public abstract class ViewModelBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
