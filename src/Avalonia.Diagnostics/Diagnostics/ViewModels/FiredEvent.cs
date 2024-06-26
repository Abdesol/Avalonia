﻿using System;
using System.Collections.ObjectModel;
using Avalonia.Diagnostics.Models;
using Avalonia.Interactivity;

namespace Avalonia.Diagnostics.ViewModels
{
    internal class FiredEvent : ViewModelBase
    {
        private readonly RoutedEventArgs _eventArgs;
        private readonly RoutedEvent? _originalEvent;
        private EventChainLink? _handledBy;

        public FiredEvent(RoutedEventArgs eventArgs, EventChainLink originator, DateTime triggerTime)
        {
            _eventArgs = eventArgs ?? throw new ArgumentNullException(nameof(eventArgs));
            Originator = originator ?? throw new ArgumentNullException(nameof(originator));
            _originalEvent = _eventArgs.RoutedEvent;
            AddToChain(originator);
            TriggerTime = triggerTime;
        }

        public bool IsPartOfSameEventChain(RoutedEventArgs e)
        {
            // Note, Avalonia might reuse RoutedEventArgs for different events to avoid extra allocations.
            // Like, PointerEntered and PointerExited will use the same instance of RoutedEventArgs. 
            return e == _eventArgs && e.RoutedEvent == _originalEvent;
        }

        public DateTime TriggerTime { get; }

        public RoutedEvent Event => _originalEvent!;

        public bool IsHandled => HandledBy?.Handled == true;

        public ObservableCollection<EventChainLink> EventChain { get; } = new ObservableCollection<EventChainLink>();

        public string DisplayText
        {
            get
            {
                if (IsHandled)
                {
                    return $"{Event.Name} on {Originator.HandlerName};" + Environment.NewLine +
                           $"strategies: {Event.RoutingStrategies}; handled by: {HandledBy!.HandlerName}";
                }

                return $"{Event.Name} on {Originator.HandlerName}; strategies: {Event.RoutingStrategies}";
            }
        }

        public EventChainLink Originator { get; }

        public EventChainLink? HandledBy
        {
            get => _handledBy;
            set
            {
                if (_handledBy != value)
                {
                    _handledBy = value;
                    RaisePropertyChanged();
                    RaisePropertyChanged(nameof(IsHandled));
                    RaisePropertyChanged(nameof(DisplayText));
                }
            }
        }

        public void AddToChain(EventChainLink link)
        {
            if (EventChain.Count > 0)
            {
                var prevLink = EventChain[EventChain.Count - 1];

                if (prevLink.Route != link.Route)
                {
                    link.BeginsNewRoute = true;
                }
            }

            EventChain.Add(link);

            if (HandledBy == null && link.Handled)
                HandledBy = link;
        }
    }
}
