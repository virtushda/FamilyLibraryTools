using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace FamilyLibraryTools.Views;

/// <summary>Requires fresh button gestures and cancels pending presses when their review context changes.</summary>
public sealed class ReviewInputGuard
{
    bool enterDown, spaceDown, mouseDown;
    Button? spaceTarget, mouseTarget;

    public void Activated()
    {
        // A press may have started in another window or a native folder dialog.
        enterDown |= Keyboard.IsKeyDown(Key.Enter);
        spaceDown |= Keyboard.IsKeyDown(Key.Space);
    }

    public void KeyDown(KeyEventArgs e, Button? shortcut = null)
    {
        if (e.Key is not (Key.Enter or Key.Space)) return;
        bool held = e.Key == Key.Enter ? enterDown : spaceDown;
        if (e.Key == Key.Enter) enterDown = true;
        else spaceDown = true;
        Button? target = shortcut ?? FindButton(e.OriginalSource as DependencyObject);
        if (target == null) return; // Text editing and navigation keep their normal repeat behavior.
        if (held || e.IsRepeat)
        {
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Space) spaceTarget = target;
    }

    public void KeyUp(KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { enterDown = false; return; }
        if (e.Key != Key.Space) return;
        spaceDown = false;
        Button? target = FindButton(e.OriginalSource as DependencyObject);
        if (target != null && target != spaceTarget)
        {
            e.Handled = true;
            CancelPress(target);
        }
        spaceTarget = null;
    }

    public void MouseDown(MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        Button? target = FindButton(e.OriginalSource as DependencyObject);
        mouseTarget = !mouseDown && e.ClickCount == 1 ? target : null;
        mouseDown = true;
        if (target != null && mouseTarget == null) e.Handled = true;
    }

    public void MouseUp(MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        mouseDown = false;
        Button? target = FindButton(e.OriginalSource as DependencyObject);
        if (target != null && target != mouseTarget)
        {
            e.Handled = true;
            CancelPress(target);
        }
        mouseTarget = null;
    }

    public void Invalidate(DependencyObject scope)
    {
        // Do not reset held-key state: a missed release outside the window must fail closed.
        if (IsWithin(spaceTarget, scope))
        {
            CancelPress(spaceTarget!);
            spaceTarget = null;
        }
        if (IsWithin(mouseTarget, scope))
        {
            CancelPress(mouseTarget!);
            mouseTarget = null;
        }
    }

    static void CancelPress(Button button)
    {
        // WPF must clear its own Space/pressed state too, not just our pending gesture.
        if (button.IsKeyboardFocused) Keyboard.ClearFocus();
        if (button.IsMouseCaptured) button.ReleaseMouseCapture();
    }

    static Button? FindButton(DependencyObject? element)
    {
        for (; element != null; element = Parent(element))
            if (element is Button button) return button;
        return null;
    }

    static bool IsWithin(DependencyObject? element, DependencyObject scope)
    {
        for (; element != null; element = Parent(element))
            if (element == scope) return true;
        return false;
    }

    static DependencyObject? Parent(DependencyObject element) => element is Visual
        ? VisualTreeHelper.GetParent(element) ?? LogicalTreeHelper.GetParent(element)
        : LogicalTreeHelper.GetParent(element);
}
