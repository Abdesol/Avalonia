using System;
using System.Collections.Generic;
using System.Linq;

namespace Avalonia.Threading;

public partial class Dispatcher
{
    private List<DispatcherTimer> _timers = new();
    private long _timersVersion;
    private bool _dueTimeFound;
    private int _dueTimeInMs;

    private int? _dueTimeForTimers;
    private int? _dueTimeForBackgroundProcessing;
    private int? _osTimerSetTo;

    private void UpdateOSTimer()
    {
        lock (InstanceLock)
        {
            var nextDueTime =
                (_dueTimeForTimers.HasValue && _dueTimeForBackgroundProcessing.HasValue)
                    ? Math.Min(_dueTimeForTimers.Value, _dueTimeForBackgroundProcessing.Value)
                    : _dueTimeForTimers ?? _dueTimeForBackgroundProcessing;
            if(_osTimerSetTo == nextDueTime)
                return;
            _impl.UpdateTimer(_osTimerSetTo = nextDueTime);
        }
    }

    internal void UpdateOSTimerForTimers()
    {
        if (!CheckAccess())
        {
            Post(UpdateOSTimerForTimers, DispatcherPriority.Send);
            return;
        }

        lock (InstanceLock)
        {
            if (!_hasShutdownFinished) // Dispatcher thread, does not technically need the lock to read
            {
                bool oldDueTimeFound = _dueTimeFound;
                int oldDueTimeInTicks = _dueTimeInMs;
                _dueTimeFound = false;
                _dueTimeInMs = 0;

                if (_timers.Count > 0)
                {
                    // We could do better if we sorted the list of timers.
                    for (int i = 0; i < _timers.Count; i++)
                    {
                        var timer = _timers[i];

                        if (!_dueTimeFound || timer.DueTimeInMs - _dueTimeInMs < 0)
                        {
                            _dueTimeFound = true;
                            _dueTimeInMs = timer.DueTimeInMs;
                        }
                    }
                }

                if (_dueTimeFound)
                {
                    if (_dueTimeForTimers == null || !oldDueTimeFound || (oldDueTimeInTicks != _dueTimeInMs))
                    {
                        _dueTimeForTimers = _dueTimeInMs;
                        UpdateOSTimer();
                    }
                }
                else if (oldDueTimeFound)
                {
                    _dueTimeForTimers = null;
                    UpdateOSTimer();
                }
            }
        }
    }

    internal void AddTimer(DispatcherTimer timer)
    {
        lock (InstanceLock)
        {
            if (!_hasShutdownFinished) // Could be a non-dispatcher thread, lock to read
            {
                _timers.Add(timer);
                _timersVersion++;
            }
        }

        UpdateOSTimerForTimers();
    }

    internal void RemoveTimer(DispatcherTimer timer)
    {
        lock (InstanceLock)
        {
            if (!_hasShutdownFinished) // Could be a non-dispatcher thread, lock to read
            {
                _timers.Remove(timer);
                _timersVersion++;
            }
        }

        UpdateOSTimerForTimers();
    }

    private void OnOSTimer()
    {
        bool needToPromoteTimers = false;
        bool needToProcessQueue = false;
        lock (InstanceLock)
        {
            needToPromoteTimers = _dueTimeForTimers.HasValue && _dueTimeForTimers.Value <= Clock.TickCount;
            if (needToPromoteTimers)
                _dueTimeForTimers = null;
            needToProcessQueue = _dueTimeForBackgroundProcessing.HasValue &&
                                 _dueTimeForBackgroundProcessing.Value <= Clock.TickCount;
            if (needToProcessQueue)
                _dueTimeForBackgroundProcessing = null;
            UpdateOSTimer();
        }

        if (needToPromoteTimers)
            PromoteTimers();
        if (needToProcessQueue)
            ExecuteJobsCore();
    }
    
    internal void PromoteTimers()
    {
        int currentTimeInTicks = Clock.TickCount;
        try
        {
            List<DispatcherTimer>? timers = null;
            long timersVersion = 0;

            lock (InstanceLock)
            {
                if (!_hasShutdownFinished) // Could be a non-dispatcher thread, lock to read
                {
                    if (_dueTimeFound && _dueTimeInMs - currentTimeInTicks <= 0)
                    {
                        timers = _timers;
                        timersVersion = _timersVersion;
                    }
                }
            }

            if (timers != null)
            {
                DispatcherTimer? timer = null;
                int iTimer = 0;

                do
                {
                    lock (InstanceLock)
                    {
                        timer = null;

                        // If the timers collection changed while we are in the middle of
                        // looking for timers, start over.
                        if (timersVersion != _timersVersion)
                        {
                            timersVersion = _timersVersion;
                            iTimer = 0;
                        }

                        while (iTimer < _timers.Count)
                        {
                            // WARNING: this is vulnerable to wrapping
                            if (timers[iTimer].DueTimeInMs - currentTimeInTicks <= 0)
                            {
                                // Remove this timer from our list.
                                // Do not increment the index.
                                timer = timers[iTimer];
                                timers.RemoveAt(iTimer);
                                break;
                            }
                            else
                            {
                                iTimer++;
                            }
                        }
                    }

                    // Now that we are outside of the lock, promote the timer.
                    if (timer != null)
                    {
                        timer.Promote();
                    }
                } while (timer != null);
            }
        }
        finally
        {
            UpdateOSTimerForTimers();
        }
    }

    internal static List<DispatcherTimer> SnapshotTimersForUnitTests() =>
        s_uiThread!._timers.ToList();
}