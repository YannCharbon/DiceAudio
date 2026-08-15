/*
 * DiceAudio - Copyright (C) 2025 Yann Charbon
 * SPDX-License-Identifier: GPL-3.0-or-later
 *
 * This file is part of DiceAudio, released under the GNU GPL v3.
 * See the LICENSE file in the repository root for details.
 */

namespace DiceAudio
{
    /// <summary>
    /// A layered, cue-driven audio scene: several concurrently-playing layers
    /// (ambience / music / sound effects) whose state changes through an ordered
    /// list of steps. Steps[0] runs when the scene starts; each later step is
    /// triggered by the user pressing "advance".
    /// Plain serializable POCO — persisted both in the scene catalogue and
    /// embedded inside scenario items.
    /// </summary>
    /// <summary>
    /// How a scene is driven live:
    /// Linear — an ordered list of steps advanced one after another.
    /// Contextual — a set of named states (e.g. tavern: fireplace / crowded /
    /// bard / brawl) the user jumps between freely; every switch crossfades
    /// all layers to the target context's per-layer levels.
    /// </summary>
    public enum DASceneMode { Linear, Contextual }

    public class DAScene
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = "New scene";

        /// <summary>Free-text category used to organize the scene catalogue (e.g. "Town", "Combat").</summary>
        public string Category { get; set; } = "";

        public DASceneMode Mode { get; set; } = DASceneMode.Linear;

        public List<DASceneLayer> Layers { get; set; } = new();

        /// <summary>Linear mode: Steps[0] runs on start, later steps on "advance".</summary>
        public List<DASceneStep> Steps { get; set; } = new();

        /// <summary>Contextual mode: the selectable states of the scene.</summary>
        public List<DASceneContext> Contexts { get; set; } = new();

        /// <summary>
        /// Contextual mode: the context entered when the scene starts. When null
        /// or referencing a deleted context, the first context is used.
        /// </summary>
        public Guid? DefaultContextId { get; set; }

        [System.Text.Json.Serialization.JsonIgnore]
        public string CueSummary => Mode == DASceneMode.Contextual
            ? $"{Layers.Count} layer(s) · {Contexts.Count} context(s)"
            : $"{Layers.Count} layer(s) · {Steps.Count} step(s)";
    }

    /// <summary>A named state of a contextual scene: which layers play, and how loud.</summary>
    public class DASceneContext
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = "Context";

        /// <summary>Crossfade duration used when entering this context.</summary>
        public double TransitionSeconds { get; set; } = 3.0;

        public List<DAContextLayerState> LayerStates { get; set; } = new();
    }

    /// <summary>
    /// One layer's state within a context. For Ambience/Music layers, Active
    /// means the layer plays (looping) at <see cref="Volume"/>. For SoundEffect
    /// layers, Active means the random one-shot loop runs, with one-shots
    /// played at <see cref="Volume"/>.
    /// </summary>
    public class DAContextLayerState
    {
        public Guid LayerId { get; set; }
        public bool Active { get; set; } = false;
        public double Volume { get; set; } = 1.0;
    }

    public enum DALayerRole { Ambience, Music, SoundEffect }

    public class DASceneLayer
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = "Layer";
        public DALayerRole Role { get; set; } = DALayerRole.Ambience;

        /// <summary>Audio reference + per-usage settings (clip/volume/fade/loop).</summary>
        public DAAudioUsage Audio { get; set; } = new();

        // Random scheduling (used by StartRandom, typically for SoundEffect layers)
        public bool RandomRepeat { get; set; } = true;
        public double RandomMinSeconds { get; set; } = 30;
        public double RandomMaxSeconds { get; set; } = 120;
        /// <summary>Play once immediately when the random loop starts, then wait.</summary>
        public bool FireImmediatelyOnStart { get; set; } = false;
    }

    public class DASceneStep
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = "Step";               // e.g. "Storm rolls in"
        public List<DASceneCommand> Commands { get; set; } = new();
    }

    public enum DACommandType
    {
        StartLayer,      // start immediately (DurationSeconds ignored)
        FadeInLayer,     // start at volume 0, ramp to the layer volume over DurationSeconds
        StopLayer,       // stop immediately
        FadeOutLayer,    // ramp to 0 over DurationSeconds, then stop
        SetVolume,       // ramp to TargetVolume over DurationSeconds
        StartRandom,     // start the layer's random one-shot loop
        StopRandom,      // cancel the layer's random one-shot loop
    }

    public class DASceneCommand
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public DACommandType Type { get; set; } = DACommandType.FadeInLayer;
        public Guid LayerId { get; set; }

        /// <summary>Seconds after the step is triggered before this command runs.</summary>
        public double DelaySeconds { get; set; } = 0;

        /// <summary>Fade/ramp duration (0 = instant).</summary>
        public double DurationSeconds { get; set; } = 2;

        /// <summary>
        /// Volume the layer plays at (0..1): the level reached by
        /// StartLayer / FadeInLayer, and the ramp target of SetVolume.
        /// </summary>
        public double TargetVolume { get; set; } = 1.0;
    }
}
