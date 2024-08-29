using BepInEx.Configuration;

namespace moe.sylvi.SaveItemRotations;

public class Config
{
	public static Config Instance { get; internal set; } = null!;

	public ConfigEntry<bool> SyncOnLoad { get; internal set; }

	public Config(ConfigFile cfg)
	{
		Instance = this;

		SyncOnLoad = cfg.Bind(
			"General",
			"SyncOnLoad",
			true,
			"Whether to sync item rotations to clients when they join the game. Should only be disabled if it causes issues.");
	}
}
