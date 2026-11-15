using System.ComponentModel;
using Microsoft.AspNetCore.Components;

namespace StudyScope.Setup.Components;

/// <summary>
/// Basis für Komponenten, die ViewModels anzeigen: abonniert PropertyChanged und rendert neu
/// (dasselbe Muster wie in StudyScope.UI, hier einmal zentral statt in jeder Komponente).
/// </summary>
public abstract class ViewModelComponent : ComponentBase, IDisposable
{
    private IReadOnlyList<INotifyPropertyChanged> _sources = Array.Empty<INotifyPropertyChanged>();

    /// <summary>Die ViewModels, deren Änderungen ein Neuzeichnen auslösen.</summary>
    protected abstract IEnumerable<INotifyPropertyChanged> ViewModels { get; }

    protected override void OnInitialized()
    {
        _sources = ViewModels.ToList();
        foreach (var vm in _sources)
            vm.PropertyChanged += OnViewModelChanged;
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e) => InvokeAsync(StateHasChanged);

    public void Dispose()
    {
        foreach (var vm in _sources)
            vm.PropertyChanged -= OnViewModelChanged;
    }
}
