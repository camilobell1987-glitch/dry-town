using System.Collections.Generic;
using Godot;

/// <summary>
/// Plays the game's sound: a looping jazz record and street ambience, plus overlapping
/// one-shot effects from a small player pool. Created from code (add it to the tree once);
/// volume and mute settings persist to user://settings.cfg.
/// </summary>
public partial class Audio : Node
{
    private const string SettingsPath = "user://settings.cfg";
    private const string Section = "audio";
    private const int PoolSize = 8;

    /// <summary>The live instance, set once the node enters the tree.</summary>
    public static Audio? Instance { get; private set; }

    private float _musicVolume = 0.5f;
    private float _effectsVolume = 0.8f;
    private bool _muted;
    private bool _loading;

    /// <summary>Music volume, 0..1. Applied immediately and saved.</summary>
    public float MusicVolume
    {
        get => _musicVolume;
        set { _musicVolume = Mathf.Clamp(value, 0f, 1f); Changed(); }
    }

    /// <summary>Volume of one-shots and the street ambience, 0..1. Applied immediately and saved.</summary>
    public float EffectsVolume
    {
        get => _effectsVolume;
        set { _effectsVolume = Mathf.Clamp(value, 0f, 1f); Changed(); }
    }

    /// <summary>Silences everything without touching the volume levels. Saved.</summary>
    public bool Muted
    {
        get => _muted;
        set { _muted = value; Changed(); }
    }

    private readonly Dictionary<string, AudioStreamWav?> _cache = new();
    private readonly List<AudioStreamPlayer> _pool = new();
    private int _nextPlayer;
    private AudioStreamPlayer _music = null!;
    private AudioStreamPlayer _ambience = null!;

    /// <summary>The ambience sits under the music; this keeps it there at equal slider positions.</summary>
    private const float AmbienceLevel = 0.6f;

    public override void _Ready()
    {
        Instance = this;
        ProcessMode = ProcessModeEnum.Always; // keep playing while the game is paused

        for (int i = 0; i < PoolSize; i++)
        {
            var player = new AudioStreamPlayer();
            AddChild(player);
            _pool.Add(player);
        }
        _music = new AudioStreamPlayer();
        _ambience = new AudioStreamPlayer();
        AddChild(_music);
        AddChild(_ambience);

        LoadSettings();
        ApplyLoopVolumes();
    }

    public override void _ExitTree()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>
    /// Plays res://audio/{name}.wav once. Sounds overlap up to the pool size, after which the
    /// oldest is cut. Pitch varies randomly by up to ±pitchJitter so repeats don't sound canned.
    /// </summary>
    public void Play(string name, float volume = 1f, float pitchJitter = 0.05f)
    {
        var stream = Load(name);
        if (stream == null || _pool.Count == 0)
            return;

        // Prefer an idle player; otherwise steal round-robin.
        AudioStreamPlayer? player = null;
        for (int i = 0; i < _pool.Count; i++)
        {
            var candidate = _pool[(_nextPlayer + i) % _pool.Count];
            if (!candidate.Playing)
            {
                player = candidate;
                break;
            }
        }
        player ??= _pool[_nextPlayer];
        _nextPlayer = (_pool.IndexOf(player) + 1) % _pool.Count;

        player.Stream = stream;
        player.VolumeDb = ToDb(_muted ? 0f : _effectsVolume * volume);
        player.PitchScale = 1f + (float)GD.RandRange(-pitchJitter, pitchJitter);
        player.Play();
    }

    /// <summary>Starts the jazz record and the street ambience looping, unless they already are.</summary>
    public void StartLoops()
    {
        StartLoop(_music, "music_jazz");
        StartLoop(_ambience, "ambience_street");
        ApplyLoopVolumes();
    }

    private void StartLoop(AudioStreamPlayer player, string name)
    {
        if (player.Playing)
            return;
        var source = Load(name);
        if (source == null)
            return;

        // The loops are imported with looping on; set it here too in case the import settings are lost.
        var looped = source;
        if (source.LoopMode == AudioStreamWav.LoopModeEnum.Disabled)
        {
            looped = (AudioStreamWav)source.Duplicate();
            looped.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
            looped.LoopBegin = 0;
            looped.LoopEnd = (int)Mathf.Round(source.GetLength() * source.MixRate);
        }
        player.Stream = looped;
        player.Play();
    }

    private AudioStreamWav? Load(string name)
    {
        if (_cache.TryGetValue(name, out var cached))
            return cached;

        string path = $"res://audio/{name}.wav";
        AudioStreamWav? stream = ResourceLoader.Exists(path) ? GD.Load<AudioStreamWav>(path) : null;
        if (stream == null)
            GD.PushWarning($"Audio: missing sound '{path}'");
        _cache[name] = stream; // cache misses too, so a missing file warns once
        return stream;
    }

    private void Changed()
    {
        if (_loading)
            return;
        ApplyLoopVolumes();
        SaveSettings();
    }

    private void ApplyLoopVolumes()
    {
        if (_music == null || _ambience == null)
            return;
        _music.VolumeDb = ToDb(_muted ? 0f : _musicVolume);
        _ambience.VolumeDb = ToDb(_muted ? 0f : _effectsVolume * AmbienceLevel);
    }

    /// <summary>Linear 0..1 to decibels, with silence as -80 dB.</summary>
    private static float ToDb(float linear) => linear <= 0.0001f ? -80f : Mathf.LinearToDb(linear);

    private void LoadSettings()
    {
        var config = new ConfigFile();
        if (config.Load(SettingsPath) != Error.Ok)
            return;
        _loading = true;
        MusicVolume = (float)config.GetValue(Section, "music_volume", _musicVolume);
        EffectsVolume = (float)config.GetValue(Section, "effects_volume", _effectsVolume);
        Muted = (bool)config.GetValue(Section, "muted", _muted);
        _loading = false;
    }

    private void SaveSettings()
    {
        // Load first so settings other systems keep in the same file survive.
        var config = new ConfigFile();
        config.Load(SettingsPath);
        config.SetValue(Section, "music_volume", _musicVolume);
        config.SetValue(Section, "effects_volume", _effectsVolume);
        config.SetValue(Section, "muted", _muted);
        if (config.Save(SettingsPath) != Error.Ok)
            GD.PushWarning($"Audio: could not save {SettingsPath}");
    }
}
