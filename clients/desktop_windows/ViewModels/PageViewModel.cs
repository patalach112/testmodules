namespace EcosystemClient
{
    /// <summary>
    /// A destination in the side navigation. Concrete pages add their own data;
    /// the shell only needs a title, a subtitle and a vector icon.
    /// </summary>
    public abstract class PageViewModel : ObservableObject
    {
        protected PageViewModel(string key, string title, string subtitle, string iconData)
        {
            Key = key;
            Title = title;
            Subtitle = subtitle;
            IconData = iconData;
        }

        /// <summary>Stable identifier, also used for the breadcrumb.</summary>
        public string Key { get; }

        public string Title { get; }

        public string Subtitle { get; }

        /// <summary>Path mini-language geometry drawn with a neon stroke in the sidebar.</summary>
        public string IconData { get; }

        /// <summary>Called when the page becomes visible.</summary>
        public virtual void OnActivated()
        {
        }

        /// <summary>Called when the page is navigated away from; pause timers here.</summary>
        public virtual void OnDeactivated()
        {
        }
    }
}
