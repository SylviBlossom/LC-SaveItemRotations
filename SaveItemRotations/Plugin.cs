using BepInEx;
using BepInEx.Logging;
using moe.sylvi.SaveItemRotations.Features;
using System.Reflection;
using UnityEngine;

namespace moe.sylvi.SaveItemRotations;

[BepInDependency("LethalNetworkAPI")]
[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin
{
	// Constant variables
	public const int FormatVersion = 1;

	// Plugin variables
	public static Plugin Instance { get; private set; } = null!;
	internal new static ManualLogSource Logger { get; private set; } = null!;

	private void Awake()
	{
		Logger = base.Logger;
		Instance = this;
		new Config(Config);

		Common.Patches.Initialize();
		FixItemDrop.Patches.Initialize();
		SaveRotations.Patches.Initialize();
		SyncRotations.Patches.Initialize();

		NetcodePatcher();

		Logger.LogInfo($"{MyPluginInfo.PLUGIN_GUID} v{MyPluginInfo.PLUGIN_VERSION} has loaded!");
	}

	private void NetcodePatcher()
	{
		var types = Assembly.GetExecutingAssembly().GetTypes();
		foreach (var type in types)
		{
			var methods = type.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
			foreach (var method in methods)
			{
				var attributes = method.GetCustomAttributes(typeof(RuntimeInitializeOnLoadMethodAttribute), false);
				if (attributes.Length > 0)
				{
					method.Invoke(null, null);
				}
			}
		}
	}
}
