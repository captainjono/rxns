using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Rxns.Ai
{
    /// <summary>
    /// Transport-agnostic embeddings surface — same pattern as
    /// <see cref="IAiChatEngine"/>. Implementations live in
    /// <c>Rxns.AppStatus.Host.Ai</c>; augmentation modules can register
    /// additional engines the same way they register chat engines or tool
    /// handlers.
    ///
    /// <para>Used by <c>WorkspaceIndexer</c> to embed file chunks at index-build
    /// time and by <c>WorkspaceKnowledgeSearchTool</c> to embed the query at
    /// search time. The two MUST use the same engine + model so the cosine
    /// similarity comparison is meaningful — a query embedded with
    /// nomic-embed-text doesn't usefully compare against chunks embedded with
    /// BGE.</para>
    /// </summary>
    public interface IAiEmbeddingsEngine
    {
        string EngineId { get; }
        string Label    { get; }
        string Kind     { get; }   // "ollama" | "foundry" | "voyage" | ...
        string ModelId  { get; }
        bool   IsAvailable { get; }

        /// <summary>Batch embed. One output vector per input string, in order.
        /// Implementations may chunk internally if the backend imposes a batch
        /// size limit (Ollama accepts arbitrary list sizes; OpenAI caps at
        /// ~2048 per request — engines that need to chunk do so transparently).</summary>
        Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct = default);
    }
}
