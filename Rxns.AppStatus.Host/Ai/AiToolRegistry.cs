using System.Collections.Generic;
using System.Linq;
using Rxns.Ai;

namespace Rxns.AppStatus.Host.Ai
{
    /// <summary>
    /// Singleton registry of every <see cref="IAiToolHandler"/> impl resolved
    /// from DI. Engines call <see cref="List"/> to advertise tools to the model
    /// and <see cref="FindByName"/> to dispatch tool calls back to their handlers.
    ///
    /// The <see cref="AiOptions.ReadOnly"/> flag is applied here: when on, any
    /// tool that declares <c>RequiresWriteAccess = true</c> is filtered out
    /// before the model sees it. Augmentation modules add tools by registering
    /// them as <c>IAiToolHandler</c> — the registry collects them automatically.
    /// </summary>
    public class AiToolRegistry
    {
        private readonly IReadOnlyList<IAiToolHandler> _handlers;
        private readonly AiOptions _options;

        public AiToolRegistry(IEnumerable<IAiToolHandler> handlers, AiOptions options)
        {
            _handlers = (handlers ?? new IAiToolHandler[0]).Where(h => h?.Definition != null).ToList();
            _options = options ?? new AiOptions();
        }

        public IReadOnlyList<IAiToolHandler> List()
        {
            if (_options.ReadOnly)
                return _handlers.Where(h => !h.Definition.RequiresWriteAccess).ToList();
            return _handlers;
        }

        public IAiToolHandler FindByName(string name)
        {
            return _handlers.FirstOrDefault(h => h.Definition.Name == name);
        }
    }
}
