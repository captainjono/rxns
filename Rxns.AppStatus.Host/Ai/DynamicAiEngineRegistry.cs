using System;
using System.Collections.Generic;
using System.Linq;
using Rxns.Ai;

namespace Rxns.AppStatus.Host.Ai
{
    /// <summary>
    /// Mutable engine registry layered on top of the static engines created by
    /// <see cref="AiModule"/> at startup. Runtime additions (operator clicks
    /// "Add host" in the bubble Settings tab → <see cref="AiChatController.AddEngine"/>)
    /// land here and are surfaced via <see cref="AiChatEngineFactory.All"/> so the
    /// engine selector reflects them without a portal restart.
    ///
    /// <para>Persistence is the writer's job (see <see cref="AiEngineLocalConfigStore"/>) —
    /// this class only holds the in-memory snapshot. On portal restart the local
    /// config is replayed into the static registration path, so dynamic state is
    /// the snapshot of "added but not yet restarted" + "loaded from local cfg".</para>
    /// </summary>
    public class DynamicAiEngineRegistry
    {
        private readonly object _lock = new object();
        private readonly Dictionary<string, IAiChatEngine> _byId = new Dictionary<string, IAiChatEngine>(StringComparer.OrdinalIgnoreCase);
        private readonly AiToolRegistry _tools;

        public DynamicAiEngineRegistry(AiToolRegistry tools)
        {
            _tools = tools;
        }

        public IReadOnlyList<IAiChatEngine> All()
        {
            lock (_lock) return _byId.Values.ToList();
        }

        public bool Register(AiEngineCfg cfg)
        {
            if (cfg == null || string.IsNullOrWhiteSpace(cfg.Id) || string.IsNullOrWhiteSpace(cfg.Kind))
                return false;
            var engine = AiModule.BuildEngine(cfg, _tools);
            lock (_lock) _byId[cfg.Id] = engine;
            return true;
        }

        public bool Unregister(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return false;
            lock (_lock) return _byId.Remove(id);
        }

        public bool Contains(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return false;
            lock (_lock) return _byId.ContainsKey(id);
        }
    }
}
