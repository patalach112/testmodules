using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace EcoUI.Controls
{
    /// <summary>
    /// A lightweight, virtualised log/console viewer.
    ///
    /// <para><b>Bounded by design.</b> The control owns a ring buffer capped at
    /// <see cref="MaxEntries"/>; appending never grows memory without limit. The
    /// template renders that buffer through a <c>VirtualizingStackPanel</c> with
    /// recycling, so only the visible rows exist in the visual tree - a 10 000-line
    /// buffer scrolls as cheaply as a 10-line one.</para>
    ///
    /// <para><b>Thread-safe entry point.</b> <see cref="Append(string, EcoLogLevel)"/>
    /// may be called from any thread; calls from a background thread are marshalled to
    /// the control's dispatcher, so producers never need to know about the UI.</para>
    ///
    /// <code>
    /// terminal.Append("Service started", EcoLogLevel.Success);
    /// </code>
    /// </summary>
    [TemplatePart(Name = PartScroller, Type = typeof(ScrollViewer))]
    public class EcoTerminal : Control
    {
        /// <summary>Template part: the scroll host used for tail-following.</summary>
        public const string PartScroller = "PART_Scroller";

        /// <summary>Maximum number of retained lines (oldest are dropped first).</summary>
        public static readonly DependencyProperty MaxEntriesProperty =
            DependencyProperty.Register(
                nameof(MaxEntries),
                typeof(int),
                typeof(EcoTerminal),
                new FrameworkPropertyMetadata(1000, null, CoerceMaxEntries));

        /// <summary>Follow new output automatically.</summary>
        public static readonly DependencyProperty AutoScrollProperty =
            DependencyProperty.Register(
                nameof(AutoScroll),
                typeof(bool),
                typeof(EcoTerminal),
                new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.None, OnAutoScrollChanged));

        /// <summary>Show the time gutter.</summary>
        public static readonly DependencyProperty ShowTimestampsProperty =
            DependencyProperty.Register(
                nameof(ShowTimestamps),
                typeof(bool),
                typeof(EcoTerminal),
                new FrameworkPropertyMetadata(true));

        private static readonly DependencyPropertyKey EntriesPropertyKey =
            DependencyProperty.RegisterReadOnly(
                nameof(Entries),
                typeof(ReadOnlyObservableCollection<EcoLogEntry>),
                typeof(EcoTerminal),
                new FrameworkPropertyMetadata(null));

        /// <summary>Read-only view of the retained lines, bindable as an item source.</summary>
        public static readonly DependencyProperty EntriesProperty = EntriesPropertyKey.DependencyProperty;

        private readonly ObservableCollection<EcoLogEntry> _entries = new ObservableCollection<EcoLogEntry>();
        private ScrollViewer? _scroller;

        /// <summary>Creates an empty terminal.</summary>
        public EcoTerminal()
        {
            SetValue(EntriesPropertyKey, new ReadOnlyObservableCollection<EcoLogEntry>(_entries));
            _entries.CollectionChanged += OnEntriesChanged;
        }

        static EcoTerminal()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(EcoTerminal),
                new FrameworkPropertyMetadata(typeof(EcoTerminal)));
        }

        /// <summary>Lines currently retained by the control.</summary>
        public ReadOnlyObservableCollection<EcoLogEntry>? Entries =>
            (ReadOnlyObservableCollection<EcoLogEntry>?)GetValue(EntriesProperty);

        /// <summary>Maximum retained lines. Coerced to at least 1.</summary>
        public int MaxEntries
        {
            get => (int)GetValue(MaxEntriesProperty);
            set => SetValue(MaxEntriesProperty, value);
        }

        /// <summary>When true, new lines scroll the view to the newest entry.</summary>
        public bool AutoScroll
        {
            get => (bool)GetValue(AutoScrollProperty);
            set => SetValue(AutoScrollProperty, value);
        }

        /// <summary>When true, the timestamp gutter is rendered.</summary>
        public bool ShowTimestamps
        {
            get => (bool)GetValue(ShowTimestampsProperty);
            set => SetValue(ShowTimestampsProperty, value);
        }

        /// <summary>
        /// Appends a line. Safe from any thread: background calls are marshalled to the
        /// control's dispatcher.
        /// </summary>
        /// <param name="message">Line text.</param>
        /// <param name="level">Severity, used for colouring.</param>
        public void Append(string message, EcoLogLevel level = EcoLogLevel.Info)
        {
            if (message is null)
            {
                return;
            }

            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.InvokeAsync(() => AppendCore(message, level));
                return;
            }

            AppendCore(message, level);
        }

        /// <summary>Appends a pre-built entry.</summary>
        /// <param name="entry">Entry to append.</param>
        public void Append(EcoLogEntry entry)
        {
            if (entry is null)
            {
                return;
            }

            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.InvokeAsync(() => AppendEntry(entry));
                return;
            }

            AppendEntry(entry);
        }

        /// <summary>Removes every retained line.</summary>
        public void Clear()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.InvokeAsync(new Action(Clear));
                return;
            }

            _entries.Clear();
        }

        /// <inheritdoc />
        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            if (_scroller is not null)
            {
                _scroller.ScrollChanged -= OnScrollChanged;
            }

            _scroller = GetTemplateChild(PartScroller) as ScrollViewer;

            if (_scroller is not null)
            {
                _scroller.ScrollChanged += OnScrollChanged;
                ScrollToEnd();
            }
        }

        // ---------------------------------------------------------------------

        private void AppendCore(string message, EcoLogLevel level) =>
            AppendEntry(new EcoLogEntry(message, level));

        private void AppendEntry(EcoLogEntry entry)
        {
            _entries.Add(entry);

            // Trim from the front: a bounded buffer is the whole point of a viewer
            // that may run for days.
            int overflow = _entries.Count - MaxEntries;
            for (int i = 0; i < overflow; i++)
            {
                _entries.RemoveAt(0);
            }

            ScrollToEnd();
        }

        private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Add && AutoScroll)
            {
                ScrollToEnd();
            }
        }

        /// <summary>Scrolls at background priority so a burst of lines cannot stall input.</summary>
        private void ScrollToEnd()
        {
            if (!AutoScroll || _scroller is null)
            {
                return;
            }

            Dispatcher.InvokeAsync(new Action(_scroller.ScrollToEnd), DispatcherPriority.Background);
        }

        private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (_scroller is null)
            {
                return;
            }

            bool atBottom = _scroller.VerticalOffset >= _scroller.ScrollableHeight - 0.5d;

            if (atBottom)
            {
                // Back at the tail: follow new output again.
                if (!AutoScroll)
                {
                    SetCurrentValue(AutoScrollProperty, true);
                }

                return;
            }

            // A user-initiated scroll (no content growth) detaches the tail, so the
            // view never fights someone reading history. Content growth while detached
            // is ignored on purpose.
            if (AutoScroll && e.ExtentHeightChange == 0d)
            {
                SetCurrentValue(AutoScrollProperty, false);
            }
        }

        private static object CoerceMaxEntries(DependencyObject d, object baseValue)
        {
            int value = (int)baseValue;
            return value < 1 ? 1 : value;
        }

        private static void OnAutoScrollChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is EcoTerminal terminal && (bool)e.NewValue)
            {
                terminal.ScrollToEnd();
            }
        }
    }
}
