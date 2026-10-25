using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace EcosystemClient.Views
{
    /// <summary>
    /// Terminal page. The only view logic here is tail-following: when "follow" is on,
    /// the list scrolls to the newest line, and the scroll happens at
    /// <see cref="DispatcherPriority.Background"/> so a burst of frames never delays
    /// layout or input.
    /// </summary>
    public partial class TerminalView : UserControl
    {
        private ScrollViewer? _scrollViewer;
        private TerminalViewModel? _viewModel;

        public TerminalView()
        {
            InitializeComponent();

            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
            DataContextChanged += OnDataContextChanged;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _scrollViewer ??= FindScrollViewer(LogList);
            Attach();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e) => Detach();

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e) => Attach();

        private void Attach()
        {
            Detach();

            if (DataContext is not TerminalViewModel viewModel)
            {
                return;
            }

            _viewModel = viewModel;
            _viewModel.Entries.CollectionChanged += OnEntriesChanged;
        }

        private void Detach()
        {
            if (_viewModel is not null)
            {
                _viewModel.Entries.CollectionChanged -= OnEntriesChanged;
                _viewModel = null;
            }
        }

        private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (_viewModel?.AutoScroll != true)
            {
                return;
            }

            ScrollViewer? scroll = _scrollViewer;
            if (scroll is null)
            {
                return;
            }

            Dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new System.Action(() => scroll.ScrollToEnd()));
        }

        private static ScrollViewer? FindScrollViewer(DependencyObject root)
        {
            if (root is ScrollViewer viewer)
            {
                return viewer;
            }

            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                ScrollViewer? found = FindScrollViewer(VisualTreeHelper.GetChild(root, i));
                if (found is not null)
                {
                    return found;
                }
            }

            return null;
        }
    }
}
