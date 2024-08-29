namespace moe.sylvi.SaveItemRotations.Features;

public static class Common
{
	// Temporary variables
	public static int LoadedFormatVersion = 0;
	public static bool LoadedParityCheck = false;

	public static void SaveInitialValues(GameNetworkManager gameNetworkManager)
	{
		ES3.Save(SaveKeys.FormatVersion, Plugin.FormatVersion, gameNetworkManager.currentSaveFileName);
		ES3.Save(SaveKeys.ParityStepsTaken, StartOfRound.Instance.gameStats.allStepsTaken, gameNetworkManager.currentSaveFileName);
	}

	public static void LoadInitialValues(StartOfRound startOfRound)
	{
		var currentSaveFileName = GameNetworkManager.Instance.currentSaveFileName;

		if (!ES3.KeyExists(SaveKeys.FormatVersion, currentSaveFileName))
		{
			LoadedFormatVersion = 0;
			LoadedParityCheck = false;

			Plugin.Logger.LogWarning($"Load | No {MyPluginInfo.PLUGIN_NAME} save data found, skipping all");
			return;
		}

		LoadedFormatVersion = ES3.Load(SaveKeys.FormatVersion, currentSaveFileName, Plugin.FormatVersion);
		var loadedStepsTaken = ES3.Load(SaveKeys.ParityStepsTaken, currentSaveFileName, startOfRound.gameStats.allStepsTaken);

		if (loadedStepsTaken != startOfRound.gameStats.allStepsTaken)
		{
			LoadedParityCheck = false;

			Plugin.Logger.LogWarning($"Load | Steps Taken mismatch (Expected {loadedStepsTaken}, got {startOfRound.gameStats.allStepsTaken}), likely outdated save, skipping all");
			return;
		}

		LoadedParityCheck = true;
	}

	public static class Patches
	{
		public static void Initialize()
		{
			On.GameNetworkManager.SaveItemsInShip += GameNetworkManager_SaveItemsInShip;
			On.StartOfRound.SetTimeAndPlanetToSavedSettings += StartOfRound_SetTimeAndPlanetToSavedSettings;
		}

		private static void GameNetworkManager_SaveItemsInShip(On.GameNetworkManager.orig_SaveItemsInShip orig, GameNetworkManager self)
		{
			if (!StartOfRound.Instance.isChallengeFile)
			{
				SaveInitialValues(self);
			}

			orig(self);
		}

		private static void StartOfRound_SetTimeAndPlanetToSavedSettings(On.StartOfRound.orig_SetTimeAndPlanetToSavedSettings orig, StartOfRound self)
		{
			orig(self);

			LoadInitialValues(self);
		}
	}
}
