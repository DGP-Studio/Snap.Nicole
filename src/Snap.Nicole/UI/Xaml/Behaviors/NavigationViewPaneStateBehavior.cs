using CommunityToolkit.WinUI.Behaviors;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Snap.Nicole.Core;

namespace Snap.Nicole.UI.Xaml.Behaviors;

[GeneratedDependencyProperty<bool>("IsPaneOpen", DefaultValue = true, NotNull = true, PropertyChangedCallbackName = nameof(OnIsPaneOpenChanged))]
internal sealed partial class NavigationViewPaneStateBehavior : BehaviorBase<NavigationView>
{
    private bool isUpdatingAssociatedObject;
    private bool isUpdatingIsPaneOpen;

    protected override bool Initialize()
    {
        if (!base.Initialize())
        {
            return false;
        }

        AssociatedObject.PaneOpened += OnPaneOpened;
        AssociatedObject.PaneClosed += OnPaneClosed;
        UpdateAssociatedObject();
        return true;
    }

    protected override void OnAssociatedObjectLoaded()
    {
        UpdateAssociatedObject();
    }

    protected override bool Uninitialize()
    {
        AssociatedObject.PaneOpened -= OnPaneOpened;
        AssociatedObject.PaneClosed -= OnPaneClosed;
        return base.Uninitialize();
    }

    private static void OnIsPaneOpenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not NavigationViewPaneStateBehavior behavior)
        {
            return;
        }

        if (behavior.isUpdatingIsPaneOpen)
        {
            return;
        }

        behavior.UpdateAssociatedObject();
    }

    private void OnPaneOpened(NavigationView sender, object args)
    {
        UpdateIsPaneOpen(true);
    }

    private void OnPaneClosed(NavigationView sender, object args)
    {
        UpdateIsPaneOpen(false);
    }

    private void UpdateIsPaneOpen(bool value)
    {
        if (isUpdatingAssociatedObject)
        {
            return;
        }

        using (BooleanTrueScope.Create(ref isUpdatingIsPaneOpen))
        {
            IsPaneOpen = value;
        }
    }

    private void UpdateAssociatedObject()
    {
        if (AssociatedObject == null)
        {
            return;
        }

        if (AssociatedObject.IsPaneOpen == IsPaneOpen)
        {
            return;
        }

        using (BooleanTrueScope.Create(ref isUpdatingAssociatedObject))
        {
            AssociatedObject.IsPaneOpen = IsPaneOpen;
        }
    }
}
