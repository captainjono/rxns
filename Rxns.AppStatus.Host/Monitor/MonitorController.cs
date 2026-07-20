using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

namespace Rxns.AppStatus.Host.Monitor
{
    /// <summary>
    /// REST surface for the portal's monitor pane.
    ///
    /// <para>Endpoints:</para>
    /// <list type="bullet">
    /// <item><c>GET  /api/monitor/state</c> — full snapshot for initial UI load</item>
    /// <item><c>GET  /api/monitor/sources</c> — available sources (enabled flag, availability)</item>
    /// <item><c>POST /api/monitor/sources/{id}</c> — body { enabled: bool }</item>
    /// <item><c>POST /api/monitor/mode</c> — body { mode: 'manual' | 'semi' | 'auto' }</item>
    /// <item><c>POST /api/monitor/suggestions/{id}/ack</c></item>
    /// <item><c>POST /api/monitor/suggestions/{id}/snooze</c> — body { minutes: int }</item>
    /// <item><c>POST /api/monitor/trust</c> — body { tool, argumentsJson, label? }</item>
    /// <item><c>DELETE /api/monitor/trust</c> — body { tool, argSchemaHash }</item>
    /// <item><c>POST /api/monitor/analyse-now</c> — manual flush, returns raised count</item>
    /// </list>
    /// </summary>
    [ApiController]
    [Route("api/monitor")]
    public class MonitorController : ControllerBase
    {
        private readonly MonitorService _service;

        public MonitorController(MonitorService service)
        {
            _service = service;
        }

        public class SourceToggleBody { public bool Enabled { get; set; } }
        public class ModeBody { public string Mode { get; set; } }
        public class SnoozeBody { public int Minutes { get; set; } }
        public class TrustBody { public string Tool { get; set; } public string ArgumentsJson { get; set; } public string Label { get; set; } }
        public class RevokeTrustBody { public string Tool { get; set; } public string ArgSchemaHash { get; set; } }

        [HttpGet("state")]
        public IActionResult State()
        {
            return Ok(new
            {
                mode = _service.Mode.ToString().ToLowerInvariant(),
                enabledSourceIds = _service.EnabledSourceIds,
                sources = _service.AllSources.Select(s => new
                {
                    id = s.Id,
                    label = s.Label,
                    description = s.Description,
                    available = s.IsAvailable,
                    enabled = _service.EnabledSourceIds.Contains(s.Id)
                }),
                suggestions = _service.ActiveSuggestions,
                trustedActions = _service.Trusted
            });
        }

        [HttpGet("sources")]
        public IActionResult Sources()
        {
            return Ok(_service.AllSources.Select(s => new
            {
                id = s.Id,
                label = s.Label,
                description = s.Description,
                available = s.IsAvailable,
                enabled = _service.EnabledSourceIds.Contains(s.Id)
            }));
        }

        [HttpPost("sources/{id}")]
        public IActionResult ToggleSource(string id, [FromBody] SourceToggleBody body)
        {
            if (string.IsNullOrWhiteSpace(id)) return BadRequest("source id is required");
            _service.ToggleSource(id, body?.Enabled ?? false);
            return Ok(new { id, enabled = body?.Enabled ?? false });
        }

        [HttpPost("mode")]
        public IActionResult SetMode([FromBody] ModeBody body)
        {
            if (string.IsNullOrWhiteSpace(body?.Mode)) return BadRequest("mode is required");
            if (!Enum.TryParse<MonitorMode>(body.Mode, ignoreCase: true, out var m))
                return BadRequest("mode must be one of: manual, semi, auto");
            _service.SwitchMode(m);
            return Ok(new { mode = m.ToString().ToLowerInvariant() });
        }

        [HttpPost("suggestions/{id}/ack")]
        public IActionResult Ack(string id)
        {
            _service.AckSuggestion(id);
            return Ok();
        }

        [HttpPost("suggestions/{id}/snooze")]
        public IActionResult Snooze(string id, [FromBody] SnoozeBody body)
        {
            var minutes = body?.Minutes ?? 30;
            _service.SnoozeSuggestion(id, minutes);
            return Ok(new { id, snoozedFor = minutes });
        }

        [HttpPost("trust")]
        public IActionResult Trust([FromBody] TrustBody body)
        {
            if (body == null || string.IsNullOrWhiteSpace(body.Tool))
                return BadRequest("tool is required");
            var hash = MonitorService.ComputeArgHash(body.Tool, body.ArgumentsJson);
            _service.TrustAction(body.Tool, hash, body.Label);
            return Ok(new { tool = body.Tool, argSchemaHash = hash });
        }

        [HttpDelete("trust")]
        public IActionResult Revoke([FromBody] RevokeTrustBody body)
        {
            if (body == null || string.IsNullOrWhiteSpace(body.Tool) || string.IsNullOrWhiteSpace(body.ArgSchemaHash))
                return BadRequest("tool and argSchemaHash are required");
            _service.RevokeTrust(body.Tool, body.ArgSchemaHash);
            return Ok();
        }

        [HttpPost("analyse-now")]
        public async Task<IActionResult> AnalyseNow()
        {
            var raised = await _service.AnalyseNowAsync().ConfigureAwait(false);
            return Ok(new { raised });
        }
    }
}
