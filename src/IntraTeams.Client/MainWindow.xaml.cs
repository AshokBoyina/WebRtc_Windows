using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using IntraTeams.Client.ViewModels;

namespace IntraTeams.Client;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not MainViewModel viewModel)
        {
            return;
        }

        // Keep the newest message visible without the user having to scroll.
        viewModel.Chat.CollectionChanged += OnChatChanged;
    }

    private void OnChatChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add || ChatList.Items.Count == 0)
        {
            return;
        }

        ChatList.ScrollIntoView(ChatList.Items[^1]);
    }

    protected override void OnClosed(EventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.Chat.CollectionChanged -= OnChatChanged;
        }

        base.OnClosed(e);

        // The media threads are background threads, but an in-flight call
        // should not keep the process alive if teardown stalls.
        Application.Current?.Shutdown();
    }
}
