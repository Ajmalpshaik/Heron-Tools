// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Quick Clash - the one door from the modeless window to the Revit API. The window never calls Revit itself:
// it posts a piece of work here and raises the ExternalEvent; Revit runs it on its own thread when it is ready.

using System;
using System.Collections.Generic;
using Autodesk.Revit.UI;

namespace HeronTools.Services.QuickClash
{
    internal sealed class QuickClashRequestHandler : IExternalEventHandler
    {
        private readonly object _lock = new object();
        private readonly Queue<Action<UIApplication>> _queue = new Queue<Action<UIApplication>>();

        /// <summary>Called when a piece of work throws - the session turns it into a plain status line.</summary>
        internal Action<Exception> OnError { get; set; }

        internal void Enqueue(Action<UIApplication> work)
        {
            lock (_lock) _queue.Enqueue(work);
        }

        public void Execute(UIApplication app)
        {
            while (true)
            {
                Action<UIApplication> work;
                lock (_lock)
                {
                    if (_queue.Count == 0)
                        return;
                    work = _queue.Dequeue();
                }

                try
                {
                    work(app);
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    // the modeller cancelled a Revit prompt - nothing to report
                }
                catch (Exception ex)
                {
                    // Broad on purpose: an exception escaping an ExternalEvent shows Revit's own crash message.
                    // Transactions are inside "using", so whatever was started is rolled back before we get here.
                    OnError?.Invoke(ex);
                }
            }
        }

        public string GetName() => "Heron Tools - Quick Clash";
    }
}
