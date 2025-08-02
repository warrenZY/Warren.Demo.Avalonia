using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Platform;

namespace Bookmark.Views;

public partial class MainView : UserControl
{

    public MainView()
    {
        InitializeComponent();
        Loaded += MainView_Loaded;
    }

    private void MainView_Loaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var top = TopLevel.GetTopLevel(this);
        if (top != null)
        {
            _topLevel = top;
            if (top.InputPane != null)
            {
                top.InputPane.StateChanged += InputPane_StateChanged;
            }
        }
    }
    private TopLevel? _topLevel;
    private void InputPane_StateChanged(object? sender, InputPaneStateEventArgs e)
    {
        if (_topLevel != null)
        {
            var element = _topLevel.FocusManager?.GetFocusedElement();
            if (e.NewState == InputPaneState.Open && element is TemplatedControl control)
            {
                var position = control.TransformToVisual(_topLevel)?.Transform(new Point());
                if (position != null)
                {
                    var bottomY = position.Value.Y + control.DesiredSize.Height;
                    if (bottomY > e.EndRect.Y)
                    {
                        Padding = new Thickness(0, 0, 0, e.EndRect.Height);
                    }
                    if (e.NewState == InputPaneState.Closed)
                    {
                        Padding = new Thickness(0, 0, 0, 0);
                    }
                }
            }

            //FileContentTextBox.BringIntoView();

            if (e.NewState == InputPaneState.Closed)
                Padding = new Thickness(0, 0, 0, 0);
        }
    }
}
