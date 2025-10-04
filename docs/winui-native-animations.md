# WinUI view-model and code-behind extension notes

## Current structure
- `Veriado.WinUI/ViewModels/Files/FilesPageViewModel.cs` is declared as a `partial` class and already exposes observable properties generated through the CommunityToolkit source generators. This makes it straightforward to add more state or commands from a separate file without touching the existing implementation.
- `Veriado.WinUI/Views/Files/FilesPage.xaml.cs` is also `partial` (as all XAML-backed types are) and wires up composition animations manually via `AnimationSettings`, `AnimationResourceHelper`, and `ImplicitListAnimations` helpers.

## Extending the view-model without modifying existing files
Because the view-model is partial, you can create a companion file (for example `FilesPageViewModel.NativeAnimations.cs`) in the same namespace and declare:

```csharp
namespace Veriado.WinUI.ViewModels.Files;

public partial class FilesPageViewModel
{
    [ObservableProperty]
    private bool useNativeAnimations = true;

    partial void OnUseNativeAnimationsChanged(bool value)
    {
        Messenger.Send(new NativeAnimationPreferenceChangedMessage(value));
    }
}
```

This pattern keeps the generated backing fields and the original logic intact while exposing a new property that the UI can bind to. By raising a messenger message (or any other existing infrastructure), the rest of the application can react without altering `FilesPageViewModel.cs`.

## Extending the code-behind with native WinUI animations
Similarly, you can drop a new `FilesPage.NativeAnimations.cs` file next to `FilesPage.xaml.cs`:

```csharp
namespace Veriado.WinUI.Views.Files;

public sealed partial class FilesPage
{
    private void OnNativeAnimationsToggled(bool isEnabled)
    {
        ResultsHost.ContentTransitions = isEnabled
            ? new TransitionCollection { new EntranceThemeTransition() }
            : null;
    }
}
```

The partial class gains access to the named XAML elements (`ResultsHost`, `FilesRepeater`, etc.) without re-opening the original file. You can subscribe to view-model messages (e.g., in the existing `ViewModel.PropertyChanged` callback) or to any of the already exposed events from within this new file, all while relying exclusively on WinUI's built-in transitions (`EntranceThemeTransition`, `ContentThemeTransition`, `ConnectedAnimationService`, and `ThemeTransition` derivatives).

## Recommended workflow
1. Introduce a view-model property (as above) that captures the native animation preference.
2. In the code-behind partial file, listen for that property (via `PropertyChanged` or the messenger pattern already in place) and assign WinUI-provided transitions to the relevant controls.
3. Remove calls to `ImplicitListAnimations.Attach` when the native mode is active; this can be done by branching inside the new partial class, leaving the existing implementation untouched for the composition-powered path.

By keeping the new logic inside partial companions you respect the "no modifications to existing files" constraint while still leveraging the native animation APIs offered by WinUI.
