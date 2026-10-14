using Godot;

/// <summary>How the map is seen: straight down onto the roofs, or at an angle with the buildings standing up. The choice is kept between games.</summary>
public partial class Main
{
    private const string ViewSettingsPath = "user://settings.cfg";
    private const int AngledMenuId = 30, FilmMenuId = 31;

    private void LoadView()
    {
        var config = new ConfigFile();
        bool angled = config.Load(ViewSettingsPath) == Error.Ok && (bool)config.GetValue("view", "angled", false);
        if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--angled") >= 0) angled = true;
        _map.Angled = angled;
        _map.FilmLook = config.GetValue("view", "film", true).AsBool();
        if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--no-film") >= 0) _map.FilmLook = false;
    }

    private void SetFilmLook(bool on)
    {
        _map.FilmLook = on;
        var config = new ConfigFile();
        config.Load(ViewSettingsPath);
        config.SetValue("view", "film", on);
        config.Save(ViewSettingsPath);
    }

    private void SetAngled(bool angled)
    {
        _map.Angled = angled;
        // Load first so the sound settings kept in the same file survive.
        var config = new ConfigFile();
        config.Load(ViewSettingsPath);
        config.SetValue("view", "angled", angled);
        config.Save(ViewSettingsPath);
    }
}
