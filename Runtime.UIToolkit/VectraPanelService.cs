using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace FirstSymbol.Vectra.UIToolkit
{
    /// <summary>An element that is rebuilt and polled by its panel's <see cref="VectraPanelService"/>.</summary>
    interface IVectraPanelClient
    {
        /// <summary>The slot in the service's client list (kept by the service).</summary>
        int ServiceIndex { get; set; }

        /// <summary>Rebuilds when it asked for it through <see cref="VectraPanelService.Queue"/> and is still attached to <paramref name="service"/>.</summary>
        void RunScheduled(VectraPanelService service);

        /// <summary>The slow check for a lost texture or a changed screen density.</summary>
        void PollNow();
    }

    /// <summary>
    /// One rebuild item and one poll item per panel for every Vectra element in it. The scheduler of UI Toolkit removes a finished item from a
    /// list by a linear search, so a thousand items of one-time rebuilds (one per element and frame) cost a million comparisons.
    /// </summary>
    sealed class VectraPanelService
    {
        static readonly Dictionary<IPanel, VectraPanelService> s_Services = new Dictionary<IPanel, VectraPanelService>();

        readonly IPanel m_Panel;
        readonly List<IVectraPanelClient> m_Clients = new List<IVectraPanelClient>();
        List<IVectraPanelClient> m_Queue = new List<IVectraPanelClient>();
        List<IVectraPanelClient> m_Work = new List<IVectraPanelClient>();
        IVisualElementScheduledItem m_Rebuild, m_Poll;
        bool m_RebuildPending;

        VectraPanelService(IPanel panel) => m_Panel = panel;

        /// <summary>Elements attached to a panel through a service now (tests).</summary>
        public static int ServiceCount => s_Services.Count;

        /// <summary>Puts <paramref name="client"/> under the service of the panel <paramref name="element"/> is in; null when it has no panel.</summary>
        public static VectraPanelService Join(VisualElement element, IVectraPanelClient client)
        {
            var panel = element.panel;
            if (panel == null) return null;
            if (!s_Services.TryGetValue(panel, out var service))
            {
                service = new VectraPanelService(panel);
                s_Services.Add(panel, service);
            }

            client.ServiceIndex = service.m_Clients.Count;
            service.m_Clients.Add(client);
            if (service.m_Poll == null) service.m_Poll = panel.visualTree.schedule.Execute(service.PollAll).Every(250);
            return service;
        }

        public void Leave(IVectraPanelClient client)
        {
            int i = client.ServiceIndex;
            if (i < 0 || i >= m_Clients.Count || !ReferenceEquals(m_Clients[i], client)) return;
            int last = m_Clients.Count - 1;
            if (i != last)
            {
                m_Clients[i] = m_Clients[last];
                m_Clients[i].ServiceIndex = i;
            }

            m_Clients.RemoveAt(last);
            client.ServiceIndex = -1;
            if (m_Clients.Count > 0) return;
            m_Rebuild?.Pause();
            m_Poll?.Pause();
            m_Rebuild = m_Poll = null;
            m_Queue.Clear();
            m_Work.Clear();
            m_RebuildPending = false;
            s_Services.Remove(m_Panel);
        }

        /// <summary>Asks for <see cref="IVectraPanelClient.RunScheduled"/> in the next scheduler update of the panel.</summary>
        public void Queue(IVectraPanelClient client)
        {
            m_Queue.Add(client);
            if (m_RebuildPending) return;
            m_RebuildPending = true;
            if (m_Rebuild == null) m_Rebuild = m_Panel.visualTree.schedule.Execute(RunQueue);
            m_Rebuild.ExecuteLater(0);
        }

        void RunQueue()
        {
            m_RebuildPending = false;
            if (m_Queue.Count == 0) return;
            // Rebuilds that ask for another one (or other elements) are queued for the next update.
            var work = m_Queue;
            m_Queue = m_Work;
            m_Work = work;
            for (int i = 0; i < work.Count; i++) work[i].RunScheduled(this);
            work.Clear();
        }

        void PollAll()
        {
            for (int i = 0; i < m_Clients.Count; i++) m_Clients[i].PollNow();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => s_Services.Clear();
    }
}
