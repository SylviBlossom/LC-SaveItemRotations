using Mono.Cecil.Cil;
using MonoMod.Cil;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace moe.sylvi.SaveItemRotations.Features;

public static class SaveRotations
{
	// Temporary variables
	public static Vector3[]? LoadedItemRotations;
	public static Dictionary<GrabbableObject, Vector3> NeedsItemRotation = [];
	public static List<Vector3>? SavedItemRotations;

	public static void PreSave()
	{
		SavedItemRotations = [];
	}

	public static void Save(GrabbableObject[] grabbableObjects, int i)
	{
		var grabbableObject = grabbableObjects[i];

		SavedItemRotations?.Add(grabbableObject.transform.eulerAngles);
	}

	public static void PostSave(GameNetworkManager gameNetworkManager)
	{
		if (SavedItemRotations == null)
		{
			return;
		}

		ES3.Save(SaveKeys.ItemRotations, SavedItemRotations.ToArray(), gameNetworkManager.currentSaveFileName);
	}

	public static void PreLoad(int[] ids)
	{
		// Reset variable to null if loading fails
		LoadedItemRotations = null;
		NeedsItemRotation = [];

		var currentSaveFileName = GameNetworkManager.Instance.currentSaveFileName;

		// Skip if parity check failed
		if (!Common.LoadedParityCheck)
		{
			return;
		}

		// Make sure modded values exist
		if (!ES3.KeyExists(SaveKeys.ItemRotations, currentSaveFileName))
		{
			Plugin.Logger.LogWarning($"Load | No item rotation save data found, skipping load item rotation");
			return;
		}

		// Load values for item rotations
		LoadedItemRotations = ES3.Load<Vector3[]>(SaveKeys.ItemRotations, currentSaveFileName);

		// Make sure our lists are the same size
		if (LoadedItemRotations.Length != ids.Length)
		{
			Plugin.Logger.LogError($"Load | Item count mismatch (Expected {LoadedItemRotations.Length}, got {ids.Length}), likely outdated save, skipping load item rotation");

			LoadedItemRotations = null;
			return;
		}
	}

	public static void Load(int i, GrabbableObject grabbableObject)
	{
		// Skip if preload failed
		if (LoadedItemRotations == null)
		{
			return;
		}

		// Make sure our index is in-bounds (it always should be, unless some other mod tampers with item loading here)
		if (i >= LoadedItemRotations.Length)
		{
			Plugin.Logger.LogError($"Load | Item index outside bounds of saved rotations, this shouldn't happen");
			return;
		}

		// Mark this item as needing to be rotated later in Update (after initial position/rotation update)
		NeedsItemRotation.Add(grabbableObject, LoadedItemRotations[i]);

		// Though we do this later, also do this now to sync values between clients
		ApplyRotationTo(grabbableObject, LoadedItemRotations[i]);
	}

	public static void Apply(GrabbableObject grabbableObject)
	{
		if (!grabbableObject.IsServer)
		{
			return;
		}

		if (!NeedsItemRotation.TryGetValue(grabbableObject, out var rotation))
		{
			return;
		}

		ApplyRotationTo(grabbableObject, rotation);

		NeedsItemRotation.Remove(grabbableObject);
	}

	private static void ApplyRotationTo(GrabbableObject grabbableObject, Vector3 eulerAngles)
	{
		grabbableObject.floorYRot = -1;
		grabbableObject.transform.rotation = Quaternion.Euler(grabbableObject.transform.eulerAngles.x, eulerAngles.y, grabbableObject.transform.eulerAngles.z);
	}

	public static class Patches
	{
		public static void Initialize()
		{
			IL.GameNetworkManager.SaveItemsInShip += GameNetworkManager_SaveItemsInShip;
			IL.StartOfRound.LoadShipGrabbableItems += StartOfRound_LoadShipGrabbableItems;
			On.GrabbableObject.Update += GrabbableObject_Update;
		}

		private static void GameNetworkManager_SaveItemsInShip(ILContext il)
		{
			var cursor = new ILCursor(il);

			// step:
			// Get the GrabbableObject[] array variable.
			//
			//		private void SaveItemsInShip()
			//		{
			//	>_		GrabbableObject[] grabbableObjects = Object.FindObjectsByType<GrabbableObject>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
			//			if (grabbableObjects == null || grabbableObjects.Length == 0)
			//			...

			if (!cursor.TryGotoNext(MoveType.Before, instr => instr.MatchCallOrCallvirt<Object>("FindObjectsByType")))
			{
				Plugin.Logger.LogError("Failed IL hook for GameNetworkManager.SaveItemsInShip @ Find grabbable objects");
				return;
			}

			var grabbableObjectsLoc = -1;

			if (!cursor.TryGotoNext(MoveType.After, instr => instr.MatchStloc(out grabbableObjectsLoc)))
			{
				Plugin.Logger.LogError("Failed IL hook for GameNetworkManager.SaveItemsInShip @ Get grabbable objects local");
				return;
			}

			// step:
			// Get the variables of all 4 defined save lists.
			//		...
			//	>_	List<int> itemIDs = new List<int>();
			//	>_	List<Vector3> itemPos = new List<Vector3>();
			//	>_	List<int> scrapValues = new List<int>();
			//	>_	List<int> itemSaveData = new List<int>();
			//		...

			var listLocs = new int[4];

			for (var i = 0; i < listLocs.Length; i++)
			{
				var loc = -1;

				if (!cursor.TryGotoNext(MoveType.After, instr => instr.MatchStloc(out loc)))
				{
					Plugin.Logger.LogError($"Failed IL hook for GameNetworkManager.SaveItemsInShip @ Get list {i} local");
					return;
				}

				listLocs[i] = loc;
			}

			var idsLoc = listLocs[0];
			var posLoc = listLocs[1];
			var valuesLoc = listLocs[2];
			var dataLoc = listLocs[3];

			// diff A:
			//		...
			//		List<int> itemSaveData = new List<int>();
			//	+	SaveItemRotations.PreSave();
			//		int i = 0;
			//		for (int i = 0; i < grabbableObjects.Length && i <= StartOfRound.Instance.maxShipItemCapacity; i++)
			//		{
			//			...

			var iLoc = -1;

			if (!cursor.TryGotoNext(MoveType.Before,
					instr1 => instr1.MatchLdcI4(0),
					instr2 => instr2.MatchStloc(out iLoc),
					instr3 => instr3.MatchBr(out _)))
			{
				Plugin.Logger.LogError("Failed IL hook for GameNetworkManager.SaveItemsInShip @ Get loop 'i' local");
				return;
			}

			var preSaveInstr = cursor.Next;

			// diff B:
			//		...
			//		if (StartOfRound.Instance.allItemsList.itemsList[j] == grabbableObjects[i].itemProperties)
			//		{
			//			itemIDs.Add(i);
			//	+		SaveItemRotations.Save(grabbableObjects, i);
			//			itemPos.Add(grabbableObjects[i].transform.position);
			//			break;
			//		}
			//		...

			if (!cursor.TryGotoNext(MoveType.Before, instr => instr.MatchLdloc(posLoc)))
			{
				Plugin.Logger.LogError("Failed IL hook for GameNetworkManager.SaveItemsInShip @ Before add item position");
				return;
			}

			var saveInstr = cursor.Next;

			// diff C:
			//		...
			//		ES3.Save<Vector3[]>("shipGrabbableItemPos", itemPos.ToArray(), this.currentSaveFileName);
			//	+	SaveItemRotations.PostSave(this);
			//		ES3.Save<int[]>("shipGrabbableItemIDs", itemIDs.ToArray(), this.currentSaveFileName);
			//		...

			if (!cursor.TryGotoNext(MoveType.After, instr => instr.MatchCallOrCallvirt<ES3>("Save")))
			{
				Plugin.Logger.LogError("Failed IL hook for GameNetworkManager.SaveItemsInShip @ First save call");
				return;
			}

			var postSaveInstr = cursor.Next;

			// apply diff A

			cursor.Goto(preSaveInstr);
			cursor.EmitDelegate(PreSave);

			// apply diff B

			cursor.Goto(saveInstr);
			cursor.Emit(OpCodes.Ldloc, grabbableObjectsLoc);
			cursor.Emit(OpCodes.Ldloc, iLoc);
			cursor.EmitDelegate(Save);

			// apply diff C

			cursor.Goto(postSaveInstr);
			cursor.Emit(OpCodes.Ldarg_0);
			cursor.EmitDelegate(PostSave);
		}

		private static void StartOfRound_LoadShipGrabbableItems(ILContext il)
		{
			var cursor = new ILCursor(il);

			// diff A:
			//		...
			//		int[] itemIDs = ES3.Load<int[]>("shipGrabbableItemIDs", GameNetworkManager.Instance.currentSaveFileName);
			//	+	SaveItemRotations.PreLoad(itemIDs);
			//		Vector3[] itemPos = ES3.Load<Vector3[]>("shipGrabbableItemPos", GameNetworkManager.Instance.currentSaveFileName);
			//		...

			var idsLoc = -1;

			if (!cursor.TryGotoNext(MoveType.Before, instr => instr.MatchLdstr("shipGrabbableItemIDs")) ||
				!cursor.TryGotoNext(MoveType.After, instr => instr.MatchStloc(out idsLoc)))
			{
				Plugin.Logger.LogError("Failed IL hook for StartOfRound.LoadShipGrabbableItems @ Get item IDs local");
				return;
			}

			var preLoadInstr = cursor.Next;

			// step:
			// Get the "i" variable of the loop.
			//		...
			//		int scrapValuesIndex = 0;
			//		int itemSaveDataIndex = 0;
			//	>_	for (int i = 0; i < itemIDs.Length; i++)
			//		{
			//			...

			var iLoc = -1;

			if (!cursor.TryGotoNext(MoveType.After,
					instr1 => instr1.MatchLdcI4(0),
					instr2 => instr2.MatchStloc(out iLoc),
					instr3 => instr3.MatchBr(out _)))
			{
				Plugin.Logger.LogError("Failed IL hook for StartOfRound.LoadShipGrabbableItems @ Get loop 'i' local");
				return;
			}

			// diff B:
			//		...
			//		GrabbableObject grabbableObject = Object.Instantiate<GameObject>(this.allItemsList.itemsList[itemIDs[i]].spawnPrefab, itemPos[i], Quaternion.identity, this.elevatorTransform).GetComponent<GrabbableObject>();
			//	+	SaveItemRotations.Load(i, grabbableObject);
			//		grabbableObject.fallTime = 1f;
			//		...

			if (!cursor.TryGotoNext(MoveType.Before, instr => instr.MatchCallOrCallvirt<GameObject>("GetComponent")))
			{
				Plugin.Logger.LogError("Failed IL hook for StartOfRound.LoadShipGrabbableItems @ Grabbable object instantiation");
				return;
			}

			var grabbableObjectLoc = -1;

			if (!cursor.TryGotoNext(MoveType.After, instr => instr.MatchStloc(out grabbableObjectLoc)))
			{
				Plugin.Logger.LogError("Failed IL hook for StartOfRound.LoadShipGrabbableItems @ Get grabbable object local");
				return;
			}

			var loadInstr = cursor.Next;

			// apply diff A

			cursor.Goto(preLoadInstr);
			cursor.Emit(OpCodes.Ldloc, idsLoc);
			cursor.EmitDelegate(PreLoad);

			// apply diff B

			cursor.Goto(loadInstr);
			cursor.Emit(OpCodes.Ldloc, iLoc);
			cursor.Emit(OpCodes.Ldloc, grabbableObjectLoc);
			cursor.EmitDelegate(Load);
		}

		private static void GrabbableObject_Update(On.GrabbableObject.orig_Update orig, GrabbableObject self)
		{
			orig(self);

			Apply(self);
		}
	}
}
