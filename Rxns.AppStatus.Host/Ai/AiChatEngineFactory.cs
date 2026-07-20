using System;
using System.Collections.Generic;
using System.Linq;
using Rxns.Ai;

namespace Rxns.AppStatus.Host.Ai
{
    /// <summary>
    /// Resolves the engine for a chat turn.
    ///
    /// <para>Two sources combine into the engine pool:</para>
    /// <list type="number">
    /// <item><description><b>Config-declared engines</b> — built from
    /// <see cref="AiOptions.Engines"/> by <see cref="AiModule"/>: a
    /// <see cref="ClaudeApiAiEngine"/> per <c>claude</c> entry, an
    /// <see cref="OllamaAiEngine"/> per <c>ollama</c>, etc.</description></item>
    /// <item><description><b>DI-registered engines</b> — anything registered as
    /// <see cref="IAiChatEngine"/> by augmentation modules. Same pattern that
    /// already works for <see cref="IAiToolHandler"/>: a downstream portal
    /// can register a bespoke engine and it shows up in the picker.</description></item>
    /// </list>
    ///
    /// <para><see cref="Pick"/> returns the engine matching the given id, or the
    /// default when id is null/empty. <c>IsAvailable</c> is honoured — an engine
    /// whose backend is unreachable is skipped over.</para>
    /// </summary>
    public class AiChatEngineFactory
    {
        private readonly IReadOnlyList<IAiChatEngine> _static;
        private readonly DynamicAiEngineRegistry _dynamic;
        private readonly AiOptions _options;

        public AiChatEngineFactory(IEnumerable<IAiChatEngine> engines, DynamicAiEngineRegistry dynamicRegistry, AiOptions options)
        {
            _static = (engines ?? new IAiChatEngine[0]).Where(e => e != null).ToList();
            _dynamic = dynamicRegistry;
            _options = options ?? new AiOptions();
        }

        /// <summary>All registered engines, including unavailable ones (so the UI
        /// can show them greyed out with a reason). Concatenates static (DI) +
        /// dynamic (runtime-added via /api/ai/engines POST). Dynamic entries
        /// override static ones with the same id — useful when an operator
        /// "edits" an env-derived default by adding a same-id entry.</summary>
        public IReadOnlyList<IAiChatEngine> All()
        {
            var dyn = _dynamic?.All() ?? new List<IAiChatEngine>();
            var dynIds = new System.Collections.Generic.HashSet<string>(dyn.Select(e => e.EngineId), StringComparer.OrdinalIgnoreCase);
            // dynamic entries win on id collision
            return _static.Where(e => !dynIds.Contains(e.EngineId)).Concat(dyn).ToList();
        }

        /// <summary>Available engines only — the set Pick() will actually return from.</summary>
        public IReadOnlyList<IAiChatEngine> Available() => All().Where(e => e.IsAvailable).ToList();

        /// <summary>Pick the engine matching <paramref name="engineId"/>. When the
        /// id is empty/null, falls back to <see cref="AiOptions.DefaultEngineId"/>,
        /// then to the first engine flagged Default in config, then to the first
        /// available engine. Throws <see cref="InvalidOperationException"/> if
        /// nothing is available.</summary>
        public IAiChatEngine Pick(string engineId = null)
        {
            var all = All();
            if (!string.IsNullOrWhiteSpace(engineId))
            {
                var byId = all.FirstOrDefault(e => string.Equals(e.EngineId, engineId, StringComparison.OrdinalIgnoreCase));
                if (byId != null)
                {
                    if (!byId.IsAvailable)
                        throw new InvalidOperationException("Engine '" + engineId + "' is registered but currently unavailable.");
                    return byId;
                }
                throw new InvalidOperationException("Unknown engine id '" + engineId + "'. Registered: " + string.Join(",", all.Select(e => e.EngineId)));
            }

            // No explicit id → walk defaults.
            if (!string.IsNullOrWhiteSpace(_options.DefaultEngineId))
            {
                var def = all.FirstOrDefault(e =>
                    string.Equals(e.EngineId, _options.DefaultEngineId, StringComparison.OrdinalIgnoreCase) && e.IsAvailable);
                if (def != null) return def;
            }

            var firstAvailable = all.FirstOrDefault(e => e.IsAvailable);
            if (firstAvailable != null) return firstAvailable;

            throw new InvalidOperationException(
                "No AI engine available. Configure one in appstatus.config (Ai.Engines[]) or set CLAUDE_API_KEY / OLLAMA_URL / FOUNDRY_URL env vars.");
        }
    }
}
