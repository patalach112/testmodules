using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace EcosystemClient
{
    /// <summary>
    /// Minimal <see cref="INotifyPropertyChanged"/> base class. Deliberately tiny:
    /// no dependency injection, no reflection, no property-name strings at call sites.
    /// </summary>
    public abstract class ObservableObject : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return false;
            }

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>Raises change notifications for several dependent properties at once.</summary>
        protected void OnPropertiesChanged(params string[] propertyNames)
        {
            foreach (string name in propertyNames)
            {
                OnPropertyChanged(name);
            }
        }
    }
}
