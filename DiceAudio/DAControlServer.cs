/*
 * DiceAudio - Copyright (C) 2025 Yann Charbon
 * SPDX-License-Identifier: GPL-3.0-or-later
 *
 * This file is part of DiceAudio, released under the GNU GPL v3.
 * See the LICENSE file in the repository root for details.
 */

using System.Text;
using System.Text.Json;

namespace DiceAudio
{
    /// <summary>
    /// Minimal localhost HTTP+JSON API so external tools (e.g. the Trilium notes
    /// widget in TriliumPlugin/) can drive playback remotely. All actions go
    /// through <see cref="DAPlaybackCoordinator"/> on the main thread, so remote
    /// commands behave exactly like clicks in the app UI.
    ///
    /// Windows-only (the primary desktop target); Start() is a no-op elsewhere.
    /// </summary>
    public class DAControlServer : IDisposable
    {
        public const string PrefEnabled = "controlServer.enabled";
        public const string PrefPort = "controlServer.port";
        public const int DefaultPort = 8765;

        private readonly DiceAudioService _service;
        private readonly DAPlaybackCoordinator _coordinator;

        public bool IsRunning { get; private set; }
        public int Port { get; private set; } = DefaultPort;
        public string? LastError { get; private set; }

        public static bool IsSupported =>
#if WINDOWS
            true;
#else
            false;
#endif

        public DAControlServer(DiceAudioService service, DAPlaybackCoordinator coordinator)
        {
            _service = service;
            _coordinator = coordinator;
        }

        /// <summary>Starts the server if the user enabled it in Settings.</summary>
        public void ApplySavedSettings()
        {
            if (Preferences.Default.Get(PrefEnabled, false))
                Start(Preferences.Default.Get(PrefPort, DefaultPort));
        }

#if WINDOWS
        private System.Net.HttpListener? _listener;
        private CancellationTokenSource? _cts;

        public void Start(int port)
        {
            Stop();
            LastError = null;
            Port = port;
            try
            {
                _listener = new System.Net.HttpListener();
                // "localhost" prefixes are usable without URL-ACL registration.
                _listener.Prefixes.Add($"http://localhost:{port}/");
                _listener.Start();
                _cts = new CancellationTokenSource();
                IsRunning = true;
                _ = AcceptLoopAsync(_listener, _cts.Token);
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                IsRunning = false;
                _listener = null;
            }
        }

        public void Stop()
        {
            _cts?.Cancel();
            _cts = null;
            try { _listener?.Stop(); _listener?.Close(); } catch { }
            _listener = null;
            IsRunning = false;
        }

        private async Task AcceptLoopAsync(System.Net.HttpListener listener, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && listener.IsListening)
            {
                System.Net.HttpListenerContext context;
                try { context = await listener.GetContextAsync(); }
                catch { break; }   // listener stopped
                _ = HandleRequestAsync(context);
            }
        }

        private async Task HandleRequestAsync(System.Net.HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;

            // CORS — the Trilium frontend runs on another origin.
            response.Headers["Access-Control-Allow-Origin"] = "*";
            response.Headers["Access-Control-Allow-Methods"] = "GET, POST, OPTIONS";
            response.Headers["Access-Control-Allow-Headers"] = "Content-Type";

            try
            {
                if (request.HttpMethod == "OPTIONS")
                {
                    response.StatusCode = 204;
                    response.Close();
                    return;
                }

                string path = request.Url?.AbsolutePath.TrimEnd('/') ?? "";
                string body = "";
                if (request.HasEntityBody)
                {
                    using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
                    body = await reader.ReadToEndAsync();
                }

                object? result = (request.HttpMethod, path) switch
                {
                    ("GET", "/api/state") => BuildState(),
                    ("GET", "/api/groups") => BuildGroups(),
                    ("POST", "/api/play") => await OnPlayAsync(Parse(body)),
                    ("POST", "/api/pause") => await OnPauseAsync(Parse(body)),
                    ("POST", "/api/stop") => await OnStopAsync(Parse(body)),
                    ("POST", "/api/next") => await OnNextPrevAsync(Parse(body), +1),
                    ("POST", "/api/prev") => await OnNextPrevAsync(Parse(body), -1),
                    ("POST", "/api/scene/advance") => await OnSceneAdvanceAsync(Parse(body)),
                    ("POST", "/api/scene/goto") => await OnSceneGotoAsync(Parse(body)),
                    _ => null,
                };

                if (result == null)
                {
                    response.StatusCode = 404;
                    await WriteJsonAsync(response, new { error = "unknown endpoint" });
                }
                else
                {
                    response.StatusCode = 200;
                    await WriteJsonAsync(response, result);
                }
            }
            catch (Exception ex)
            {
                try
                {
                    response.StatusCode = 500;
                    await WriteJsonAsync(response, new { error = ex.Message });
                }
                catch { }
            }
        }

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
        };

        private static async Task WriteJsonAsync(System.Net.HttpListenerResponse response, object payload)
        {
            response.ContentType = "application/json";
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload, JsonOptions));
            response.ContentLength64 = bytes.Length;
            await response.OutputStream.WriteAsync(bytes);
            response.Close();
        }

        private sealed record ControlRequest(Guid? ScenarioId, Guid? ItemId, int? StepIndex);

        private static ControlRequest Parse(string body)
        {
            if (string.IsNullOrWhiteSpace(body)) return new ControlRequest(null, null, null);
            try { return JsonSerializer.Deserialize<ControlRequest>(body, JsonOptions) ?? new ControlRequest(null, null, null); }
            catch { return new ControlRequest(null, null, null); }
        }

        // ── Lookups ──────────────────────────────────────────────────────────

        private DAScenario? FindScenario(Guid? id) =>
            id == null ? null
                       : _service.ScenarioGroups.SelectMany(g => g.Scenarios).FirstOrDefault(s => s.Id == id);

        // ── Read endpoints ───────────────────────────────────────────────────

        private object BuildState()
        {
            var active = _coordinator.ScenarioPlayers
                .Where(p => p.IsPlaying)
                .Select(p =>
                {
                    var item = p.CurrentItem;
                    var scenePlayer = p.CurrentItemPlayer?.ScenePlayer;
                    return new
                    {
                        scenarioId = p.Scenario.Id,
                        scenarioName = p.Scenario.Name,
                        isPlaying = p.IsPlaying,
                        currentItemId = item?.Id,
                        currentItemName = item?.Name,
                        itemType = item?.Type.ToString(),
                        sceneMode = scenePlayer?.Scene.Mode.ToString(),
                        // Unified over linear steps and contextual contexts.
                        sceneStepIndex = scenePlayer?.CurrentCueIndex,
                        sceneStepName = scenePlayer?.CurrentCueName,
                        sceneStepCount = scenePlayer?.CueCount,
                    };
                })
                .ToList();

            return new { active };
        }

        private object BuildGroups()
        {
            var groups = _service.ScenarioGroups.Select(g => new
            {
                id = g.Id,
                name = g.Name,
                scenarios = g.Scenarios.Select(s => new
                {
                    id = s.Id,
                    name = s.Name,
                    items = s.Items.Select(i => new
                    {
                        id = i.Id,
                        name = i.Name,
                        type = i.Type.ToString(),
                        // Scene driving model, so the widget can render a linear
                        // step selector vs. contextual state buttons. Null for
                        // non-scene items.
                        sceneMode = i.Scene?.Mode.ToString(),
                        // Cue names: steps (linear) or contexts (contextual) — the
                        // widget drives both via /api/scene/goto.
                        steps = i.Scene == null ? null
                              : i.Scene.Mode == DASceneMode.Contextual
                                  ? i.Scene.Contexts.Select(c => c.Name).ToList()
                                  : i.Scene.Steps.Select(st => st.Name).ToList(),
                    }).ToList(),
                }).ToList(),
            }).ToList();

            return new { groups };
        }

        // ── Action endpoints (marshalled to the main thread) ─────────────────

        private async Task<object> OnPlayAsync(ControlRequest req)
        {
            var scenario = FindScenario(req.ScenarioId);
            if (scenario == null) return new { ok = false, error = "scenario not found" };

            var item = req.ItemId != null ? scenario.Items.FirstOrDefault(i => i.Id == req.ItemId) : null;

            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                var player = _coordinator.GetOrCreateScenarioPlayer(scenario);
                if (item != null) await player.PlayItemAsync(item);
                else await player.PlayAsync();
            });
            return new { ok = true };
        }

        private async Task<object> OnPauseAsync(ControlRequest req)
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                var scenario = FindScenario(req.ScenarioId);
                if (scenario != null)
                    _coordinator.GetOrCreateScenarioPlayer(scenario).Pause();
                else
                    foreach (var p in _coordinator.ActiveScenarioPlayers) p.Pause();
            });
            return new { ok = true };
        }

        private async Task<object> OnStopAsync(ControlRequest req)
        {
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                var scenario = FindScenario(req.ScenarioId);
                if (scenario != null)
                    await _coordinator.GetOrCreateScenarioPlayer(scenario).StopAsync();
                else
                    await _coordinator.StopAllAsync();
            });
            return new { ok = true };
        }

        private async Task<object> OnNextPrevAsync(ControlRequest req, int direction)
        {
            var scenario = FindScenario(req.ScenarioId);
            if (scenario == null) return new { ok = false, error = "scenario not found" };

            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                var player = _coordinator.GetOrCreateScenarioPlayer(scenario);
                if (direction > 0) await player.PlayNextAsync();
                else await player.PlayPreviousAsync();
            });
            return new { ok = true };
        }

        private async Task<object> OnSceneAdvanceAsync(ControlRequest req)
        {
            var (player, error) = await GetSceneItemPlayerAsync(req);
            if (player == null) return new { ok = false, error };

            await MainThread.InvokeOnMainThreadAsync(() => player.AdvanceSceneAsync());
            return new { ok = true };
        }

        private async Task<object> OnSceneGotoAsync(ControlRequest req)
        {
            if (req.StepIndex == null) return new { ok = false, error = "stepIndex required" };
            var (player, error) = await GetSceneItemPlayerAsync(req);
            if (player == null) return new { ok = false, error };

            // Starts the scene if it wasn't playing (a contextual scene can be
            // entered directly at any context).
            await MainThread.InvokeOnMainThreadAsync(() => player.GoToSceneCueAsync(req.StepIndex.Value));
            return new { ok = true };
        }

        private async Task<(DAScenarioItemPlayer? player, string? error)> GetSceneItemPlayerAsync(ControlRequest req)
        {
            var scenario = FindScenario(req.ScenarioId);
            if (scenario == null) return (null, "scenario not found");
            var item = scenario.Items.FirstOrDefault(i => i.Id == req.ItemId);
            if (item == null) return (null, "item not found");
            if (item.Type != DAScenarioItem.ItemType.Scene) return (null, "item is not a scene");

            DAScenarioItemPlayer? player = null;
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                player = _coordinator.GetOrCreateScenarioPlayer(scenario).GetItemPlayer(item);
            });
            return (player, player == null ? "player unavailable" : null);
        }
#else
        public void Start(int port) { LastError = "Remote control is only available on Windows."; }
        public void Stop() { }
#endif

        public void Dispose() => Stop();
    }
}
